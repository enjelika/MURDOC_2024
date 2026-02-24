"""
lora_modules.py - LoRA (Low-Rank Adaptation) building blocks for Conv2d layers.

Used by lora_retrain.py for session-based model refinement.
Used by lora_inference.py for runtime adapter loading.

Reference: Hu et al., "LoRA: Low-Rank Adaptation of Large Language Models" (2021)
Adapted for convolutional layers.

Author: Debra Hogue - MURDOC/MICA Project
"""

import math
import torch
import torch.nn as nn
from typing import Dict, List, Optional, Tuple


class LoRAConv2d(nn.Module):
    """
    LoRA adapter wrapping a frozen Conv2d.

    Forward: out = original_conv(x) + B(A(x)) * scaling
    where A projects down to rank, B projects back up.
    """

    def __init__(self, original_conv, rank=4, alpha=4.0, dropout=0.0):
        super().__init__()
        self.original_conv = original_conv
        self.rank = rank
        self.scaling = alpha / rank

        for p in self.original_conv.parameters():
            p.requires_grad = False

        self.lora_A = nn.Conv2d(
            original_conv.in_channels, rank,
            kernel_size=original_conv.kernel_size,
            stride=original_conv.stride,
            padding=original_conv.padding,
            dilation=original_conv.dilation,
            groups=original_conv.groups,
            bias=False,
        )
        self.lora_B = nn.Conv2d(
            rank, original_conv.out_channels,
            kernel_size=1, bias=False,
        )
        self.lora_dropout = nn.Dropout(dropout) if dropout > 0 else nn.Identity()

        nn.init.kaiming_uniform_(self.lora_A.weight, a=math.sqrt(5))
        nn.init.zeros_(self.lora_B.weight)

    def forward(self, x):
        base_out = self.original_conv(x)
        lora_out = self.lora_B(self.lora_A(self.lora_dropout(x)))
        return base_out + lora_out * self.scaling

    def get_lora_state_dict(self):
        return {
            "lora_A.weight": self.lora_A.weight.data.clone(),
            "lora_B.weight": self.lora_B.weight.data.clone(),
        }


def _replace_module(parent, name, new_module):
    parts = name.split(".")
    current = parent
    for part in parts[:-1]:
        current = getattr(current, part)
    setattr(current, parts[-1], new_module)


def inject_lora_into_decoder(decoder, rank=4, alpha=4.0, dropout=0.0, target_layer_names=None):
    """
    Inject LoRA adapters into Conv2d layers of a decoder module.

    Parameters
    ----------
    decoder : nn.Module
        Target decoder (e.g. Saliency_feat_decoder).
    rank : int
        LoRA rank.
    alpha : float
        Scaling factor.
    dropout : float
        Dropout on LoRA path.
    target_layer_names : list of str, optional
        If set, only layers whose names contain one of these strings are wrapped.

    Returns
    -------
    dict : name -> LoRAConv2d mapping
    """
    lora_layers = {}
    for name, module in list(decoder.named_modules()):
        if not isinstance(module, nn.Conv2d):
            continue
        if target_layer_names is not None:
            if not any(t in name for t in target_layer_names):
                continue
        lora_conv = LoRAConv2d(module, rank=rank, alpha=alpha, dropout=dropout)
        _replace_module(decoder, name, lora_conv)
        lora_layers[name] = lora_conv
    return lora_layers


def get_lora_parameters(model):
    """Collect all trainable LoRA parameters from a model."""
    params = []
    for module in model.modules():
        if isinstance(module, LoRAConv2d):
            params.extend(module.lora_A.parameters())
            params.extend(module.lora_B.parameters())
    return params


def save_lora_weights(lora_layers, path, metadata=None):
    """Save LoRA adapter weights to a .pth file."""
    state = {}
    for name, lora_module in lora_layers.items():
        for k, v in lora_module.get_lora_state_dict().items():
            state[f"{name}.{k}"] = v
    save_dict = {"lora_state_dict": state}
    if metadata:
        save_dict["metadata"] = metadata
    torch.save(save_dict, path)


def load_lora_weights(lora_layers, path):
    """Load LoRA adapter weights from a .pth file. Returns metadata dict."""
    checkpoint = torch.load(path, map_location="cpu")
    state = checkpoint.get("lora_state_dict", {})
    for name, lora_module in lora_layers.items():
        a_key = f"{name}.lora_A.weight"
        b_key = f"{name}.lora_B.weight"
        if a_key in state and b_key in state:
            lora_module.lora_A.weight.data.copy_(state[a_key])
            lora_module.lora_B.weight.data.copy_(state[b_key])
    return checkpoint.get("metadata", {})


def count_lora_parameters(lora_layers):
    """Returns (lora_param_count, frozen_param_count)."""
    lora_count = 0
    frozen_count = 0
    for lora_module in lora_layers.values():
        for p in lora_module.lora_A.parameters():
            lora_count += p.numel()
        for p in lora_module.lora_B.parameters():
            lora_count += p.numel()
        for p in lora_module.original_conv.parameters():
            frozen_count += p.numel()
    return lora_count, frozen_count