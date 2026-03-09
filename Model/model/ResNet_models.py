import torch
import torch.nn as nn
import torchvision.models as models
from model.ResNet import B2_ResNet
device = torch.device('cuda' if torch.cuda.is_available() else 'cpu')
from torch.nn import Parameter, Softmax
import torch.nn.functional as F
from model.HolisticAttention import HA
from torch.autograd import Variable
from torch.distributions import Normal, Independent, kl
import matplotlib.pyplot as plt
import numpy as np
import os
from torchvision.utils import save_image
import datetime

class Generator(nn.Module):
    """Top-level MICA detection model.

    Wraps Saliency_feat_encoder and applies MICA sensitivity/bias adjustments
    to the three output prediction maps before bilinear upsampling to input resolution.
    """

    def __init__(self, channel):
        """Initialize the Generator with a Saliency_feat_encoder and default MICA parameters."""
        super(Generator, self).__init__()
        self.sal_encoder = Saliency_feat_encoder(channel)
        self.current_filename = ""
        
        # MICA parameters
        self.sensitivity = 1.5  # d' parameter
        self.bias = 0.0         # β parameter
    
    def set_mica_parameters(self, sensitivity, bias):
        """Set MICA detection parameters"""
        self.sensitivity = sensitivity
        self.bias = bias
    
    def apply_mica_adjustment(self, predictions):
        """Apply MICA sensitivity and bias adjustments"""
        # Apply sensitivity (d') - affects discriminability
        adjusted = predictions * (self.sensitivity / 1.5)  # Normalize to default
        
        # Apply bias (β) - shifts decision threshold
        threshold_shift = torch.sigmoid(torch.tensor(self.bias * 0.2))
        adjusted = adjusted + (threshold_shift - 0.5)
        
        return adjusted

    def get_x4_layer(self):
        """Return the branch-1 deep layer (layer4_1) for Grad-CAM hook attachment."""
        # Return the appropriate layer from your model
        return self.sal_encoder.resnet.layer4_1  # Or whatever layer exists
    
    def get_x4_2_layer(self):
        """Return the branch-2 deep layer (layer4_2) for Grad-CAM hook attachment."""
        # Return the appropriate layer from your model
        return self.sal_encoder.resnet.layer4_2  # Or whatever layer exists

    def forward(self, x):
        """Run encoder, apply MICA adjustments, and upsample all predictions to input size."""
        fix_pred, cod_pred1, cod_pred2 = self.sal_encoder(x)
        
        # Apply MICA adjustments
        fix_pred = self.apply_mica_adjustment(fix_pred)
        cod_pred1 = self.apply_mica_adjustment(cod_pred1)
        cod_pred2 = self.apply_mica_adjustment(cod_pred2)
        
        # Upsample as before
        fix_pred = F.upsample(fix_pred, size=(x.shape[2], x.shape[3]), mode='bilinear', align_corners=True)
        cod_pred1 = F.upsample(cod_pred1, size=(x.shape[2], x.shape[3]), mode='bilinear', align_corners=True)
        cod_pred2 = F.upsample(cod_pred2, size=(x.shape[2], x.shape[3]), mode='bilinear', align_corners=True)
        
        return fix_pred, cod_pred1, cod_pred2

class PAM_Module(nn.Module):
    """ Position attention module"""
    #paper: Dual Attention Network for Scene Segmentation
    def __init__(self, in_dim):
        super(PAM_Module, self).__init__()
        self.chanel_in = in_dim

        self.query_conv = nn.Conv2d(in_channels=in_dim, out_channels=in_dim//8, kernel_size=1)
        self.key_conv = nn.Conv2d(in_channels=in_dim, out_channels=in_dim//8, kernel_size=1)
        self.value_conv = nn.Conv2d(in_channels=in_dim, out_channels=in_dim, kernel_size=1)
        self.gamma = Parameter(torch.zeros(1))
        self.softmax = Softmax(dim=-1)

    def forward(self, x):
        """
            inputs :
                x : input feature maps( B X C X H X W)
            returns :
                out : attention value + input feature ( B X C X H X W)
                attention: B X (HxW) X (HxW)
        """
        m_batchsize, C, height, width = x.size()
        proj_query = self.query_conv(x).view(m_batchsize, -1, width*height).permute(0, 2, 1)
        proj_key = self.key_conv(x).view(m_batchsize, -1, width*height)
        energy = torch.bmm(proj_query, proj_key)
        attention = self.softmax(energy)
        proj_value = self.value_conv(x).view(m_batchsize, -1, width*height)

        out = torch.bmm(proj_value, attention.permute(0, 2, 1))
        out = out.view(m_batchsize, C, height, width)

        out = self.gamma*out + x
        return out


class Classifier_Module(nn.Module):
    """ASPP-style multi-scale classifier that sums outputs from parallel dilated convolutions."""

    def __init__(self,dilation_series,padding_series,NoLabels, input_channel):
        """Build parallel dilated Conv2d branches for each (dilation, padding) pair."""
        super(Classifier_Module, self).__init__()
        self.conv2d_list = nn.ModuleList()
        for dilation,padding in zip(dilation_series,padding_series):
            self.conv2d_list.append(nn.Conv2d(input_channel,NoLabels,kernel_size=3,stride=1, padding =padding, dilation = dilation,bias = True))
        for m in self.conv2d_list:
            m.weight.data.normal_(0, 0.01)

    def forward(self, x):
        """Sum outputs from all dilated convolution branches."""
        out = self.conv2d_list[0](x)
        for i in range(len(self.conv2d_list)-1):
            out += self.conv2d_list[i+1](x)
        return out

## Channel Attention (CA) Layer
class CALayer(nn.Module):
    """Channel Attention Layer: recalibrates channel-wise feature responses via squeeze-and-excitation."""

    def __init__(self, channel, reduction=16):
        """Build global average pooling followed by two 1×1 convolutions with Sigmoid gating."""
        super(CALayer, self).__init__()
        # global average pooling: feature --> point
        self.avg_pool = nn.AdaptiveAvgPool2d(1)
        # feature channel downscale and upscale --> channel weight
        self.conv_du = nn.Sequential(
                nn.Conv2d(channel, channel // reduction, 1, padding=0, bias=True),
                nn.ReLU(inplace=True),
                nn.Conv2d(channel // reduction, channel, 1, padding=0, bias=True),
                nn.Sigmoid()
        )

    def forward(self, x):
        """Scale input feature map by learned per-channel attention weights."""
        y = self.avg_pool(x)
        y = self.conv_du(y)
        return x * y

## Residual Channel Attention Block (RCAB)
class RCAB(nn.Module):
    """Residual Channel Attention Block (RCAB).

    Two conv layers with optional BN and activation, followed by channel attention,
    with a residual skip connection. Reference: Zhang et al., ECCV 2018.
    Input/output: B×C×H×W.
    """

    # paper: Image Super-Resolution Using Very DeepResidual Channel Attention Networks
    # input: B*C*H*W
    # output: B*C*H*W
    def __init__(
        self, n_feat, kernel_size=3, reduction=16,
        bias=True, bn=False, act=nn.ReLU(True), res_scale=1):
        """Build the RCAB body: two conv layers, optional BN/activation, then CALayer."""
        super(RCAB, self).__init__()
        modules_body = []
        for i in range(2):
            modules_body.append(self.default_conv(n_feat, n_feat, kernel_size, bias=bias))
            if bn: modules_body.append(nn.BatchNorm2d(n_feat))
            if i == 0: modules_body.append(act)
        modules_body.append(CALayer(n_feat, reduction))
        self.body = nn.Sequential(*modules_body)
        self.res_scale = res_scale

    def default_conv(self, in_channels, out_channels, kernel_size, bias=True):
        """Return a Conv2d with symmetric padding so spatial size is preserved."""
        return nn.Conv2d(in_channels, out_channels, kernel_size,padding=(kernel_size // 2), bias=bias)

    def forward(self, x):
        """Apply body (conv + CA) then add residual skip connection."""
        res = self.body(x)
        #res = self.body(x).mul(self.res_scale)
        res += x
        return res

class BasicConv2d(nn.Module):
    """Conv2d + BatchNorm2d fused building block (no activation)."""

    def __init__(self, in_planes, out_planes, kernel_size, stride=1, padding=0, dilation=1):
        """Build a Conv-BN pair with the given spatial parameters."""
        super(BasicConv2d, self).__init__()
        self.conv_bn = nn.Sequential(
            nn.Conv2d(in_planes, out_planes,
                      kernel_size=kernel_size, stride=stride,
                      padding=padding, dilation=dilation, bias=False),
            nn.BatchNorm2d(out_planes)
        )

    def forward(self, x):
        """Apply Conv-BN and return result."""
        x = self.conv_bn(x)
        return x


class Triple_Conv(nn.Module):
    """Three sequential BasicConv2d layers: 1×1 channel reduction followed by two 3×3 refinements."""

    def __init__(self, in_channel, out_channel):
        """Build the 1×1 → 3×3 → 3×3 conv-BN sequence."""
        super(Triple_Conv, self).__init__()
        self.reduce = nn.Sequential(
            BasicConv2d(in_channel, out_channel, 1),
            BasicConv2d(out_channel, out_channel, 3, padding=1),
            BasicConv2d(out_channel, out_channel, 3, padding=1)
        )

    def forward(self, x):
        """Pass input through the three-stage conv-BN reduction."""
        return self.reduce(x)

class Saliency_feat_decoder(nn.Module):
    """ResNet-based decoder for camouflage (saliency) prediction.

    Fuses four encoder feature levels (x1–x4) through progressive RCAB blocks and
    dilated classifier modules, producing a single-channel saliency prediction map.
    """

    # resnet based encoder decoder
    def __init__(self, channel):
        """Build the multi-scale fusion decoder with RCAB blocks and dilated classifiers."""
        super(Saliency_feat_decoder, self).__init__()
        self.relu = nn.ReLU(inplace=True)
        self.upsample8 = nn.Upsample(scale_factor=8, mode='bilinear', align_corners=True)
        self.upsample4 = nn.Upsample(scale_factor=4, mode='bilinear', align_corners=True)
        self.upsample2 = nn.Upsample(scale_factor=2, mode='bilinear', align_corners=True)
        self.upsample05 = nn.Upsample(scale_factor=0.5, mode='bilinear', align_corners=True)
        self.dropout = nn.Dropout(0.3)
        self.layer6 = self._make_pred_layer(Classifier_Module, [6, 12, 18, 24], [6, 12, 18, 24], 1, channel*4)
        self.conv4 = self._make_pred_layer(Classifier_Module, [3, 6, 12, 18], [3, 6, 12, 18], channel, 2048)
        self.conv3 = self._make_pred_layer(Classifier_Module, [3, 6, 12, 18], [3, 6, 12, 18], channel, 1024)
        self.conv2 = self._make_pred_layer(Classifier_Module, [3, 6, 12, 18], [3, 6, 12, 18], channel, 512)
        self.conv1 = self._make_pred_layer(Classifier_Module, [3, 6, 12, 18], [3, 6, 12, 18], channel, 256)

        self.racb_43 = RCAB(channel * 2)
        self.racb_432 = RCAB(channel * 3)
        self.racb_4321 = RCAB(channel * 4)

        self.conv43 = self._make_pred_layer(Classifier_Module, [3, 6, 12, 18], [3, 6, 12, 18], channel, 2*channel)
        self.conv432 = self._make_pred_layer(Classifier_Module, [3, 6, 12, 18], [3, 6, 12, 18], channel, 3*channel)
        self.conv4321 = self._make_pred_layer(Classifier_Module, [3, 6, 12, 18], [3, 6, 12, 18], channel, 4*channel)

        self.cls_layer = self._make_pred_layer(Classifier_Module, [6, 12, 18, 24], [6, 12, 18, 24], 1, channel * 4)


    def _make_pred_layer(self, block, dilation_series, padding_series, NoLabels, input_channel):
        """Instantiate a prediction layer (Classifier_Module) with the given dilation config."""
        return block(dilation_series, padding_series, NoLabels, input_channel)


    def forward(self, x1,x2,x3,x4):
        """Progressively fuse four feature levels (coarse-to-fine) and predict a saliency map."""
        conv1_feat = self.conv1(x1)
        conv2_feat = self.conv2(x2)
        conv3_feat = self.conv3(x3)
        conv4_feat = self.conv4(x4)
        conv4_feat = self.upsample2(conv4_feat)

        conv4_feat = F.interpolate(conv4_feat, size=(conv3_feat.shape[2], conv3_feat.shape[3]), mode='bilinear', align_corners=False)
    
        conv43 = torch.cat((conv4_feat, conv3_feat),1)
        conv43 = self.racb_43(conv43)
        conv43 = self.conv43(conv43)

        conv43 = F.interpolate(conv43, size=(conv2_feat.shape[2], conv2_feat.shape[3]), mode='bilinear', align_corners=False)
        conv432 = torch.cat((F.interpolate(conv4_feat, size=(conv2_feat.shape[2], conv2_feat.shape[3]), mode='bilinear', align_corners=False), conv43, conv2_feat), 1)
        conv432 = self.racb_432(conv432)
        conv432 = self.conv432(conv432)
    
        conv432 = F.interpolate(conv432, size=(conv1_feat.shape[2], conv1_feat.shape[3]), mode='bilinear', align_corners=False)
        conv4_feat_up4 = F.interpolate(conv4_feat, size=(conv1_feat.shape[2], conv1_feat.shape[3]), mode='bilinear', align_corners=False)
        conv43_up2 = F.interpolate(conv43, size=(conv1_feat.shape[2], conv1_feat.shape[3]), mode='bilinear', align_corners=False)
    
        conv4321 = torch.cat((conv4_feat_up4, conv43_up2, conv432, conv1_feat), 1)
        conv4321 = self.racb_4321(conv4321)
    
        sal_pred = self.cls_layer(conv4321)
    
        return sal_pred

class Fix_feat_decoder(nn.Module):
    """ResNet-based decoder for fixation prediction.

    Upsamples all four encoder levels to the finest resolution, concatenates them,
    applies RCAB channel attention, then produces a single-channel fixation map.
    """

    # resnet based encoder decoder
    def __init__(self, channel):
        """Build the flat-fusion fixation decoder with RCAB and dilated classifiers."""
        super(Fix_feat_decoder, self).__init__()
        self.relu = nn.ReLU(inplace=True)
        self.upsample8 = nn.Upsample(scale_factor=8, mode='bilinear', align_corners=True)
        self.upsample4 = nn.Upsample(scale_factor=4, mode='bilinear', align_corners=True)
        self.upsample2 = nn.Upsample(scale_factor=2, mode='bilinear', align_corners=True)
        self.upsample05 = nn.Upsample(scale_factor=0.5, mode='bilinear', align_corners=True)
        self.dropout = nn.Dropout(0.3)
        self.conv4 = self._make_pred_layer(Classifier_Module, [3, 6, 12, 18], [3, 6, 12, 18], channel, 2048)
        self.conv3 = self._make_pred_layer(Classifier_Module, [3, 6, 12, 18], [3, 6, 12, 18], channel, 1024)
        self.conv2 = self._make_pred_layer(Classifier_Module, [3, 6, 12, 18], [3, 6, 12, 18], channel, 512)
        self.conv1 = self._make_pred_layer(Classifier_Module, [3, 6, 12, 18], [3, 6, 12, 18], channel, 256)

        self.racb4 = RCAB(channel * 4)

        self.cls_layer = self._make_pred_layer(Classifier_Module, [6, 12, 18, 24], [6, 12, 18, 24], 1, channel * 4)


    def _make_pred_layer(self, block, dilation_series, padding_series, NoLabels, input_channel):
        """Instantiate a prediction layer (Classifier_Module) with the given dilation config."""
        return block(dilation_series, padding_series, NoLabels, input_channel)


    def forward(self, x1,x2,x3,x4):
        """Upsample all encoder levels to x1 resolution, concatenate, and predict fixation map."""
        conv1_feat = self.conv1(x1)
        conv2_feat = self.conv2(x2)
        conv3_feat = self.conv3(x3)
        conv4_feat = self.conv4(x4)
                
        # Upsample conv2_feat, conv3_feat, conv4_feat to match size of conv1_feat
        up2 = F.interpolate(conv2_feat, size=(conv1_feat.shape[2], conv1_feat.shape[3]), mode='bilinear', align_corners=False)
        up4 = F.interpolate(conv3_feat, size=(conv1_feat.shape[2], conv1_feat.shape[3]), mode='bilinear', align_corners=False)
        up8 = F.interpolate(conv4_feat, size=(conv1_feat.shape[2], conv1_feat.shape[3]), mode='bilinear', align_corners=False)

        
        # Concatenate conv1_feat, up2, up4, up8 along dimension 1
        conv4321 = torch.cat((conv1_feat, up2, up4, up8), 1)
        conv4321 = self.racb4(conv4321)

        sal_pred = self.cls_layer(conv4321)

        return sal_pred


class Saliency_feat_encoder(nn.Module):
    """Full encoder that combines B2_ResNet backbone with fixation and saliency decoders.

    Runs the shared ResNet stem, produces initial fixation and saliency predictions,
    then uses the Holistic Attention module to guide a refined second-pass through
    the deeper ResNet branches (layer3_2 / layer4_2).
    """

    # resnet based encoder decoder
    def __init__(self, channel):
        """Build backbone, decoders, and holistic attention; load ImageNet weights at train time."""
        super(Saliency_feat_encoder, self).__init__()
        self.resnet = B2_ResNet()
        self.relu = nn.ReLU(inplace=True)
        self.upsample8 = nn.Upsample(scale_factor=8, mode='bilinear', align_corners=True)
        self.upsample4 = nn.Upsample(scale_factor=4, mode='bilinear', align_corners=True)
        self.upsample2 = nn.Upsample(scale_factor=2, mode='bilinear', align_corners=True)
        self.upsample05 = nn.Upsample(scale_factor=0.5, mode='bilinear', align_corners=True)
        self.dropout = nn.Dropout(0.3)
        self.cod_dec = Fix_feat_decoder(channel)
        self.sal_dec = Saliency_feat_decoder(channel)

        self.HA = HA()
        
        self.current_filename = ""

        if self.training:
            self.initialize_weights()
    
    def set_filename(self, filename):
        """Set the current image filename used as the offramp output subdirectory name."""
        self.current_filename = filename

    def forward(self, x):
        """Run two-pass inference: initial predictions → holistic attention → refined predictions.

        Returns (fix_pred, init_pred, ref_pred) each upsampled 4× to near-input resolution.
        """
        x = self.resnet.conv1(x)
        x = self.resnet.bn1(x)
        x = self.resnet.relu(x)
        x = self.resnet.maxpool(x)
        x1 = self.resnet.layer1(x)  # 256 x 64 x 64
        x2 = self.resnet.layer2(x1)  # 512 x 32 x 32
        x3 = self.resnet.layer3_1(x2)  # 1024 x 16 x 16
        x4 = self.resnet.layer4_1(x3)  # 2048 x 8 x 8
                
        # Saving intermediate feature maps as images
        self.save_feature_maps(x1, 'x1')
        self.save_feature_maps(x2, 'x2')
        self.save_feature_maps(x3, 'x3')
        self.save_feature_maps(x4, 'x4')
        
        fix_pred = self.cod_dec(x1,x2,x3,x4)
        init_pred = self.sal_dec(x1,x2,x3,x4)

        x2_2 = self.HA(1-self.upsample05(fix_pred).sigmoid(), x2)
        x3_2 = self.resnet.layer3_2(x2_2)  # 1024 x 16 x 16
        x4_2 = self.resnet.layer4_2(x3_2)  # 2048 x 8 x 8
        ref_pred = self.sal_dec(x1,x2_2,x3_2,x4_2)
        
        # Saving intermediate feature maps as images
        self.save_feature_maps(x2_2, 'x2_2')
        self.save_feature_maps(x3_2, 'x3_2')
        self.save_feature_maps(x4_2, 'x4_2')
        self.save_feature_maps(ref_pred, 'ref_pred')
                
        return self.upsample4(fix_pred),self.upsample4(init_pred),self.upsample4(ref_pred)

    def initialize_weights(self):
        """Load ImageNet-pretrained ResNet-50 weights, mapping dual-branch keys to single-branch names."""
        res50 = models.resnet50(pretrained=True)
        pretrained_dict = res50.state_dict()
        all_params = {}
        for k, v in self.resnet.state_dict().items():
            if k in pretrained_dict.keys():
                v = pretrained_dict[k]
                all_params[k] = v
            elif '_1' in k:
                name = k.split('_1')[0] + k.split('_1')[1]
                v = pretrained_dict[name]
                all_params[k] = v
            elif '_2' in k:
                name = k.split('_2')[0] + k.split('_2')[1]
                v = pretrained_dict[name]
                all_params[k] = v
        assert len(all_params.keys()) == len(self.resnet.state_dict().keys())
        self.resnet.load_state_dict(all_params)
  
    def save_feature_maps(self, feature_map, feature_name):
        """Save a channel-averaged, min-max normalized feature map as a viridis PNG for visualization.

        Output path: offramp_output_images/{current_filename}/{feature_name}.png
        """
        # timestamp = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
        filename = self.current_filename
        save_dir = f"offramp_output_images/{filename}"
        os.makedirs(save_dir, exist_ok=True)
        
        # Convert to numpy and take the first item in the batch
        feature_map_np = feature_map.detach().cpu().numpy()[0]
        
        # Aggregate across channels
        aggregated_feature = np.mean(feature_map_np, axis=0)
        
        # Normalize to [0, 1] range
        aggregated_feature = (aggregated_feature - aggregated_feature.min()) / (aggregated_feature.max() - aggregated_feature.min())
        
        # Save the aggregated feature map
        save_path = os.path.join(save_dir, f'{feature_name}.png')
        plt.imsave(save_path, aggregated_feature, cmap='viridis')