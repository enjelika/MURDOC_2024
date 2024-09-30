# -*- coding: utf-8 -*-
"""
Created on Mon Mar 27 13:10:39 2023

@author: Debra Hogue

Description: Decision Hierarchy for CODS XAI
             Lvl 1 - Binary mask evaluation  - Is anything present?
             Lvl 2 - Ranking mask evaluation - Where is the weak camouflage located?
             Lvl 3 - Object Part Identification of weak camouflage - What part of the object breaks the camouflage concealment?
"""

import cv2
import os
import json
import numpy as np
from PIL import Image, ImageFile
ImageFile.LOAD_TRUNCATED_IMAGES = True

import matplotlib.colors as color
import matplotlib.pyplot as plt
from skimage.measure import label, regionprops, find_contours
import tensorflow as tf
os.environ["CUDA_VISIBLE_DEVICES"] = '0'
from matplotlib.patches import Rectangle

import traceback
import time
import sys

import torch
import torch.nn as nn
import torch.nn.functional as F
from torch.autograd import Variable

import numpy as np
import pdb, os, argparse

from pytorch_grad_cam import GradCAM
from pytorch_grad_cam.utils.model_targets import ClassifierOutputTarget
from pytorch_grad_cam.utils.image import show_cam_on_image, preprocess_image

from scipy import misc
from model.ResNet_models import Generator

"""
===================================================================================================
    Helper function
        - Converts the grayscale CAMs to RGB and overlay them on the original image
===================================================================================================
"""
def show_cam_on_image(img: np.ndarray, mask: np.ndarray, use_rgb: bool = False, colormap: int = cv2.COLORMAP_JET) -> np.ndarray:
    heatmap = cv2.applyColorMap(np.uint8(255 * mask), colormap)
    if use_rgb:
        heatmap = cv2.cvtColor(heatmap, cv2.COLOR_BGR2RGB)
    heatmap = np.float32(heatmap) / 255

    if img.shape != heatmap.shape:
        heatmap = np.transpose(heatmap, (1, 0, 2))

    if np.max(img) > 1:
        raise Exception("The input image should np.float32 in the range [0, 1]")

    cam = heatmap + img
    cam = cam / np.max(cam)
    return np.uint8(255 * cam)

"""
===================================================================================================
    Helper function
        - Adds the XAI message as a label onto a copy of th esegmented image
===================================================================================================
"""
from PIL import ImageDraw
from PIL import ImageFont

def add_label(image, label_text, label_position):

    # Create a drawing object
    draw = ImageDraw.Draw(image)

    # Define the font and font size
    font = ImageFont.truetype('arial.ttf', 20)

     # Draw the label on the image
    draw.text(label_position, label_text, fill='white', font=font, stroke_width=2, stroke_fill='black')

    # Save the image with the label
    image.save('labeled_image.jpg')


"""
===================================================================================================
    Helper function
        - Segments the detected object in yellow and the weak camo regions in red
===================================================================================================
"""
def get_color_based_on_rank(rank_value):
    # Define a color mapping based on rank
    color_mapping = {
        1: (255, 0, 0),    # Red for rank 1
        2: (0, 255, 0),    # Green for rank 2
        3: (0, 0, 255),    # Blue for rank 3
        # Add more colors for other ranks if needed
    }
    
    # Assign the color based on the rank value
    color = color_mapping.get(rank_value, (255, 255, 0))  # Default to yellow for unknown ranks
    
    return color

def segment_image(original_image, mask_image, rank_mask, alpha=128):
    # Convert the mask and rank masks to mode 1
    mask_image = mask_image.convert('1')
    rank_mask = rank_mask.convert('1')

    # Check that the mask, rank mask, and image have the same size
    if original_image.size != mask_image.size or original_image.size != rank_mask.size:
        raise ValueError('Image, mask, and rank mask must have the same size.')

    # Convert the original image and masks to NumPy arrays
    original_array = np.array(original_image)
    mask_array = np.array(mask_image, dtype=bool)
    rank_array = np.array(rank_mask, dtype=bool)

    # Create a copy of the original image
    segmented_image = original_array.copy()

    # Apply the segmentation to the image
    indices = np.nonzero(mask_array)
    for idx in zip(*indices):
        pixel_color = original_array[idx]
        rank_value = rank_array[idx]
        color = get_color_based_on_rank(rank_value)  # Custom function to determine color based on rank
        highlighted_color = list(color) + [alpha]
        new_color = tuple([int((c1 * (255 - alpha) + c2 * alpha) / 255) for c1, c2 in zip(pixel_color, highlighted_color)])
        segmented_image[idx] = new_color

    # Convert the numpy array back to a PIL Image
    segmented_image = Image.fromarray(segmented_image)

    return segmented_image


"""
===================================================================================================
    Helper function
        - Reassigns grayscale values to colors: (Hard: Blue, Medium: Green, Weak: Red)
        -- Only the red will be visible, the medium and hard areas will be black
===================================================================================================
"""
def processFixationMap(fix_image):   
    global blGrRdBl
    
    # Input data should range from 0-1
    img_np = np.asarray(fix_image)/255
    
    # Ensure the array is 2D
    if img_np.ndim > 2:
        img_np = img_np.squeeze()  # Remove single-dimensional entries
        if img_np.ndim > 2:
            img_np = img_np[:,:,0]  # Take only the first channel if still 3D
            
    # print("Shape of img_np after processing:", img_np.shape)
    # print("Data type of img_np:", img_np.dtype)
    
    # Colorize the fixation map
    color_map = Image.fromarray(blGrRdBl(img_np, bytes=True))
    # color_map.show()
    
    return color_map


"""
===================================================================================================
    Helper function
        - Reassigns grayscale values to colors: (Hard: Blue, Medium: Green, Weak: Red)
        -- Only the red will be visible, the medium and hard areas will be black
===================================================================================================
"""
def findAreasOfWeakCamouflage(fix_image):  
    global RdBl
    
    # Input data should range from 0-1
    img_np = np.asarray(fix_image)/255
    
    # Ensure the array is 2D
    if img_np.ndim > 2:
        img_np = img_np.squeeze()  # Remove single-dimensional entries
        if img_np.ndim > 2:
            img_np = img_np[:,:,0]  # Take only the first channel if still 3D
    
    # print("Shape of img_np after processing:", img_np.shape)
    # print("Data type of img_np:", img_np.dtype)
    
    # Colorize the fixation map
    color_map = Image.fromarray((RdBl(img_np) * 255).astype(np.uint8))
    # color_map.show()
    
    return color_map


"""
===================================================================================================
    Helper function
        - Convert a mask to border image
===================================================================================================
"""
def mask_to_border(mask):
    # Convert PIL image (RGB), mask, to cv2 image (BGR), cv_mask
    open_cv_image = cv2.cvtColor(np.asarray(mask), cv2.COLOR_RGB2GRAY)
    
    # Get the height and width    
    h = open_cv_image.shape[0]
    w = open_cv_image.shape[1]
    border = np.zeros((h, w))

    contours = find_contours(open_cv_image, 1)
    for contour in contours:
        for c in contour:
            x = int(c[0])
            y = int(c[1])
            border[x][y] = 255

    return border


"""
===================================================================================================
    Helper function
        - Mask to bounding boxes
===================================================================================================
"""
def mask_to_bbox(mask):
    bboxes = []

    mask = mask_to_border(mask)
    lbl = label(mask)
    props = regionprops(lbl)
    
    for prop in props:
        x1 = prop.bbox[1]
        y1 = prop.bbox[0]

        x2 = prop.bbox[3]
        y2 = prop.bbox[2]

        bboxes.append([x1, y1, x2, y2])

    return bboxes


"""
===================================================================================================
    Helper function
        - Parsing mask for drawing bounding box(es) on an image
===================================================================================================
"""
def parse_mask(mask):
    mask = np.expand_dims(mask, axis=-1)
    mask = np.concatenate([mask, mask, mask], axis=-1)
    return mask


"""
===================================================================================================
    Helper function
        - Returns overlapping boxes 
        box format [xmin, xmax, ymin, ymax]
===================================================================================================
"""
def overlap(bbox1,bbox2):
    def overlap1D(b1,b2):
        return b1[1] >= b2[0] and b2[1] >= b1[0]
    
    return overlap1D(bbox1[:2],bbox2[:2]) and overlap1D(bbox1[2:],bbox2[2:])



"""
===================================================================================================
    Lvl 3 - What part of the object breaks the camouflage concealment?
===================================================================================================
"""
def levelThree(original_image, bbox, message, filename, detect_fn):   
    y_size, x_size, channel = original_image.shape
    
    label_map = ["leg","mouth","shadow","tail","arm","eye"]
    
    # The input needs to be a tensor, convert it using `tf.convert_to_tensor`.
    input_tensor = tf.convert_to_tensor(original_image)
    
    # The model expects a batch of images, so add an axis with `tf.newaxis`.
    input_tensor = input_tensor[tf.newaxis, ...]
    detections = detect_fn(input_tensor)
        
    d_class = []
    d_box = []
    
    if not os.path.exists('detection_results'):
        os.makedirs('detection_results')
    
    for i,s in enumerate(detections['detection_scores'].numpy()[0]):
        if s > 0.3:
            d_class.append(detections['detection_classes'].numpy()[0][i])
            d_box.append(detections['detection_boxes'].numpy()[0][i])
    
    # Create a figure with no axes
    fig, axis = plt.subplots(1, figsize=(12,6))
    axis.imshow(original_image)
    axis.axis('off')  # Turn off the axis
               
    for i,b in enumerate(detections['detection_boxes'].numpy()[0]):
        if  detections['detection_scores'].numpy()[0][i] > 0.3:
            axis.add_patch(plt.Rectangle((b[1]*x_size, b[0]*y_size),  (b[3]-b[1])*x_size,  (b[2]-b[0])*y_size, label="Test", fill=False, linewidth=2, color=(1,0,0)))
            axis.text(b[1]*x_size, b[0]*y_size-10,label_map[int(detections['detection_classes'].numpy()[0][i])-1] + " " + str(detections['detection_scores'].numpy()[0][i]), fontweight=400, color=(1,0,0))
    
    # Save the plot as a PNG file
    fig.savefig('detection_results/'+filename+'.png', bbox_inches='tight', pad_inches=0)
    plt.close(fig)  # Close the figure to free up memory
    
    weak = []
    
    # Prepare data for the TXT file
    txt_content = []
    
    for box1 in bbox:
        feat = []
        for count, box2 in enumerate(d_box):
            if overlap([box1['x1'], box1['x2'], box1['y1'], box1['y2']], [box2[1]*x_size, box2[3]*x_size, box2[0]*y_size,  box2[2]*y_size]):
                detected_class = label_map[int(d_class[count])-1]
                detection_score = detections['detection_scores'].numpy()[0][count]
                message += f"Object's {detected_class}, Detection Score: {detection_score:.2%}\n"
                feat.append(detected_class)
                txt_content.append(f"Class: {detected_class}, Score: {detection_score:.4f}")
        weak.append(feat)        
    
    # Save the detected classes and scores to a TXT file
    with open('detection_results/'+filename+'.txt', 'w') as f:
        f.write('\n'.join(txt_content))
        
    return message


"""
===================================================================================================
    Lvl 2 - Where is the weak camouflage located?
        Input:  original_image & fixation_map from Lvl 1 (Camouflage Ranking Map)
        Output: original image & list of bounding boxes
===================================================================================================
"""
def levelTwo(filename, original_image, all_fix_map, fixation_map, message, detect_fn):
    
    # Mask the red area(s) in the fixation map (weak camouflaged area(s))
    fig, axis = plt.subplots(1,2, figsize=(12,6))
    axis[0].imshow(original_image);
    axis[0].set_title('Original Image')
    axis[1].imshow(all_fix_map)
    axis[1].set_title('Fixation Map')
    plt.tight_layout()
    
    # Save plot to output folder for paper
    plt.savefig("figures/fig_"+filename)
    
    # Applying bounding box(es) with the original image for output to lvl 3
    bboxes = mask_to_bbox(fixation_map)
    
    # Convert original_image to cv2 image (BGR) and apply the bounding box
    open_cv_orImage1 = original_image.copy()
    open_cv_orImage2 = original_image.copy()
    
    # Crop areas of weak camo into a collection of images
    cropped_images = []
    data = {"item": 
            {
                "name": filename + ".jpg",
                "num_of_weak_areas": len(bboxes)
            },
            "weak_area_bbox": []
        }
    index = 1
    
    marked_image = []
    
    # Looping through the bounding boxes
    for bbox in bboxes:
        # Marking red bounding box(es) on the original image
        starting_point = (bbox[0], bbox[1])
        ending_point = (bbox[2], bbox[3])
        marked_image = cv2.rectangle(open_cv_orImage1, starting_point, ending_point, (255,0,0), 2)
        
        # Slicing to crop the image (y1:y2, x1:x2)
        ci = open_cv_orImage2[bbox[1]:bbox[3], bbox[0]:bbox[2]]
        cropped_images.append(ci)
        # cv2.imwrite("bbox_figures/cropped_"+filename+str(index)+".png", ci)
        
        # Create json of bounding box(es)
        data["weak_area_bbox"].append(
            {
                    "x1":bbox[0],
                    "y1":bbox[1],
                    "x2":bbox[2],
                    "y2":bbox[3]
            })
        
        # Increase Index
        index = index + 1
    
    # the json file to save the output data   
    with open("jsons/"+filename+".json", "w")  as f: 
        json.dump(data, f, indent = 6)  
        f.close() 
        
    # Figure of original image and marked image
    fig, axis = plt.subplots(1,2, figsize=(12,6))
    axis[0].imshow(marked_image)
    axis[0].set_title('Identified Weak Camo')
    if not (cropped_images[0].shape[0] ==0 or cropped_images[0].shape[1]==0):
        axis[1].imshow(cropped_images[0])
    axis[1].set_title('Cropped Weak Camo Area')
    
    # Save plot to output folder for paper
    plt.savefig("bbox_figures/fig_"+filename)
    
    message += "Identified " + str(index-1) + " weak camouflaged area(s).  \n"
    print("Identified " + str(index-1) + " weak camouflaged area(s).")
    
    # Weak camouflaged area annotated image
    output = levelThree(original_image, data["weak_area_bbox"], message, filename, detect_fn)
    
    return output


"""
===================================================================================================
    Lvl 1 - Is anything present?
        Input:  binary_map & fix_map
        Output: fix_map (if a camouflaged object is detected)
===================================================================================================
"""
def levelOne(filename, binary_map, all_fix_map, fix_image, original_image, message, detect_fn):

    # Does the numpy array contain any non-zero values?
    all_zeros = not binary_map.any()
    
    if all_zeros:
        # No object detected, no need to continue to lower levels
        message += "No object present. \n"
        print("No object present.")
        output = message
    else:
        # Object detected, continue to Lvl 2
        message += "Object present. \n"
        print("Object detected.")
        output = levelTwo(filename, original_image, all_fix_map, fix_image, message, detect_fn)
        
    return output


"""
===================================================================================================
    Helper Function - 
        Retrieves the overlap areas of weak camouflage within the binary map
===================================================================================================
"""
def apply_mask(heatmap, mask):
    # print("heatmap type:", type(heatmap))
    
    # Convert the heatmap to a NumPy array if it's an image
    if isinstance(heatmap, Image.Image):
        heatmap = np.asarray(heatmap)
        trans_heatmap = np.transpose(heatmap)
        # print("heatmap shape:", trans_heatmap.shape)
        
    # Broadcast the mask to match the shape of the heatmap
    mask_broadcasted = np.broadcast_to(mask, trans_heatmap.shape)
    
    # Apply the mask by multiplying the heatmap with the mask
    masked_heatmap = mask_broadcasted * trans_heatmap
    
    masked_heatmap = np.transpose(masked_heatmap)

    return masked_heatmap


"""
===================================================================================================
    IAI Function
===================================================================================================
"""
def create_custom_colormaps():
    log_step("Creating custom colormaps")
    
    # Create RdBl colormap
    if 'RdBl' not in plt.colormaps():
        RdBl_colors = ["black", "black", "red", "red"]
        RdBl = color.LinearSegmentedColormap.from_list('RdBl', RdBl_colors)
        plt.register_cmap(cmap=RdBl)
    else:
        RdBl = plt.get_cmap('RdBl')

    # Create blGrRdBl colormap
    if 'blGrRdBl' not in plt.colormaps():
        blGrRdBl_colors = ["black", "blue", "green", "red", "red"]
        blGrRdBl = color.LinearSegmentedColormap.from_list('blGrRdBl', blGrRdBl_colors)
        plt.register_cmap(cmap=blGrRdBl)
    else:
        blGrRdBl = plt.get_cmap('blGrRdBl')

    return RdBl, blGrRdBl

def iaiDecision(file_path):
    global RdBl, blGrRdBl, detect_fn
    
    try:
        log_step("iaiDecision")
        
        #Turning off gpu since loading 2 models takes too much VRAM
        os.environ["CUDA_VISIBLE_DEVICES"] = "0"

        log_step("Initializing model")
        # Loading the model and handling the CUDA availability
        cods = Generator(channel=32)

        # Load the model with appropriate handling for CPU-only environments
        model_path = './models/Resnet/Model_50_gen.pth'
        #model_path = 'C:\\Users\\pharm\\Documents\\0 - Debra PhD Courses\\2 - Academic Collaborations\\00 - OADII MURDOC\\MURDOC\\models\\resnet\\Model_50_gen.pth'
        if torch.cuda.is_available():
            cods.load_state_dict(torch.load(model_path))
            cods.cuda()
        else:
            cods.load_state_dict(torch.load(model_path, map_location=torch.device('cpu')))

        cods.eval()

        PATH_TO_SAVED_MODEL = "models/d7_f/saved_model"
        #PATH_TO_SAVED_MODEL = "C:\\Users\\pharm\\Documents\\0 - Debra PhD Courses\\2 - Academic Collaborations\\00 - OADII MURDOC\\MURDOC\\models\\d7_f\\saved_model"

        log_step("Loading TensorFlow model")
        detect_fn = []
        with tf.device('/CPU:0'):
            detect_fn = tf.saved_model.load(PATH_TO_SAVED_MODEL)
        log_step("TensorFlow model loaded successfully")

        log_step("Setting up colormaps")
        # Custom colormap for fixation maps
        RdBl, blGrRdBl = create_custom_colormaps()
        
        log_step("Creating output directories")
        # Create output directories
        for dir in ["figures", "bbox_figures", "jsons", "outputs", "results"]:
            if not os.path.exists(dir):
                os.mkdir(dir)
        
        file_name = os.path.splitext(os.path.basename(file_path))[0]
        if not os.path.exists(f'outputs/{file_name}'):
            os.makedirs(f'outputs/{file_name}')
                        
        # XAI Message
        message = f"Decision for {file_name}: \n"
        
        log_step(f"Loading image: {file_name}")
        # Gather the images: Original, Binary Mapping, Fixation Mapping
        original_image = cv2.imread(file_path)
        if original_image is None:
            raise ValueError(f"Unable to load image from path: {file_path}")

        log_step("Preprocessing image")
        # Preprocess the image
        image = cv2.cvtColor(original_image, cv2.COLOR_BGR2RGB)
        image = cv2.resize(image, (224, 224))  # Adjust size as needed
        image = image.transpose((2, 0, 1))  # Change from HWC to CHW format
        image = image / 255.0  # Normalize to [0, 1]
        image = torch.from_numpy(image).float().unsqueeze(0)  # Add batch dimension

        # Get original image dimensions
        HH, WW = original_image.shape[:2]
        
        # Move image to GPU if available
        if torch.cuda.is_available():
            image = image.cuda()
        
        log_step("Running inference")
        # Get the Fixation Mapping, and Binary Mapping Predictions
        fix_pred, _, cod_pred2 = cods.forward(image)
        
        log_step("Postprocessing results")
        # Process fixation and binary mapping predictions
        fix_image = process_prediction(fix_pred, WW, HH)
        bm_image = process_prediction(cod_pred2, WW, HH)
        
        bm_image_pil = Image.fromarray(bm_image).convert('L')
        fix_image_pil = Image.fromarray(fix_image).convert('L')
        
        bm_image_pil.save(f'outputs/{file_name}/binary_image.png')
        fix_image_pil.save(f'outputs/{file_name}/fixation_image.png')
        
        log_step("Getting the Grad-CAM Predictions")
        #=======================================================================        
        # Grad-CAM for fix_pred
        target_layer_fix = [cods.get_x4_layer()]
        grad_cam_fix = MultiOutputGradCAM(model=cods, target_layers=target_layer_fix, output_index=0)
        
        # Grad-CAM for cod_pred2
        target_layer_cod = [cods.get_x4_2_layer()]
        grad_cam_cod = MultiOutputGradCAM(model=cods, target_layers=target_layer_cod, output_index=2)
        
        # Compute Grad-CAM
        grayscale_cam_fix = None
        grayscale_cam_cod = None

        try:
            grayscale_cam_fix = grad_cam_fix(input_tensor=image)
            print(f"Shape of grayscale_cam_fix: {grayscale_cam_fix.shape}")
        except Exception as e:
            print(f"Error in grad_cam_fix: {str(e)}")
        
        try:
            grayscale_cam_cod = grad_cam_cod(input_tensor=image)
            print(f"Shape of grayscale_cam_cod: {grayscale_cam_cod.shape}")
        except Exception as e:
            print(f"Error in grad_cam_cod: {str(e)}")
        
        # Convert the input tensor to a numpy array for visualization
        input_image = image.squeeze(0).permute(1, 2, 0).cpu().numpy()
        input_image = (input_image - input_image.min()) / (input_image.max() - input_image.min())  # Normalize to [0, 1]
        
        print(f"Shape of input_image: {input_image.shape}")
        
        # Convert grayscale heatmaps to RGB
        if grayscale_cam_fix is not None:
            heatmap_fix = show_cam_on_image(input_image, grayscale_cam_fix[0], use_rgb=True)
            cv2.imwrite(f'outputs/{file_name}/gradcam_fix.png', cv2.cvtColor(heatmap_fix, cv2.COLOR_RGB2BGR))
        else:
            print("Unable to generate heatmap for fix_pred")
        
        if grayscale_cam_cod is not None:
            heatmap_cod = show_cam_on_image(input_image, grayscale_cam_cod[0], use_rgb=True)
            cv2.imwrite(f'outputs/{file_name}/gradcam_cod.png', cv2.cvtColor(heatmap_cod, cv2.COLOR_RGB2BGR))
        else:
            print("Unable to generate heatmap for cod_pred2")
        
        # Save the heatmaps
        cv2.imwrite(f'outputs/{file_name}/gradcam_fix.png', cv2.cvtColor(heatmap_fix, cv2.COLOR_RGB2BGR))
        cv2.imwrite(f'outputs/{file_name}/gradcam_cod.png', cv2.cvtColor(heatmap_cod, cv2.COLOR_RGB2BGR))
        #=======================================================================

        # Normalize the Binary Mapping 
        trans_img = np.transpose(np.where(bm_image>0.5,1,0))
        img_np = np.asarray(trans_img)
        
        # Only get the weak camouflaged areas that are present in the binary map
        masked_fix_map = apply_mask(Image.fromarray(fix_image), img_np)
        
        log_step("Processing fixation mapping")
        # Preprocess the Fixation Mapping
        weak_fix_map = findAreasOfWeakCamouflage(masked_fix_map)
        all_fix_map = processFixationMap(masked_fix_map)
        
        log_step("Running through decision levels")
        output = levelOne(file_name, img_np, all_fix_map, weak_fix_map, original_image, message, detect_fn)

        log_step("Saving output")
        org_image = Image.fromarray(cv2.cvtColor(original_image, cv2.COLOR_BGR2RGB))
        print(f'dim of org_image: {org_image.size}')
        print(f'dim of bm_image: {Image.fromarray(bm_image).size}')
        print(f'dim of fix_image: {Image.fromarray(fix_image).size}')
    
        # Resize binary and fixation images to match original image size
        bm_image_resized = cv2.resize(bm_image, (org_image.width, org_image.height))
        fix_image_resized = cv2.resize(fix_image, (org_image.width, org_image.height))
    
        segmented_image = segment_image(org_image, Image.fromarray(bm_image_resized), Image.fromarray(fix_image_resized))
        print(f'dim of segmented_image: {segmented_image.size}')
        add_label(segmented_image, output, (15, 15))
        segmented_image.save(f'results/segmented_{file_name}.jpg')

        log_step("iaiDecision completed")
        return output

    except Exception as e:
        error_message = f"An error occurred: {str(e)}\nTraceback: {traceback.format_exc()}"
        print(error_message)  # This will be captured by the redirected stdout in C#
        return f"Error occurred: {str(e)}"

class MultiOutputGradCAM(GradCAM):
    def __init__(self, model, target_layers, output_index=0, reshape_transform=None):
        super(MultiOutputGradCAM, self).__init__(model, target_layers, reshape_transform)
        self.output_index = output_index

    def forward(self, input_tensor, targets=None, eigen_smooth=False):
        # if self.cuda:
        #     input_tensor = input_tensor.cuda()

        outputs = self.activations_and_grads(input_tensor)
        output = outputs[self.output_index]
        
        if targets is None:
            target_categories = np.argmax(output.cpu().data.numpy(), axis=-1)
            targets = [ClassifierOutputTarget(category) for category in target_categories]
        
        if self.uses_gradients:
            self.model.zero_grad()
            # Sum the output tensor to create a scalar value
            loss = output.sum()
            loss.backward(retain_graph=True)

        # In most of the saliency attribution papers, the saliency is
        # computed with a single target layer.
        # Commonly it is the last convolutional layer.
        # Here we support passing a list with multiple target layers.
        # It will compute the saliency image for every image,
        # and then aggregate them (with a default mean aggregation).
        # This gives you more flexibility in case you just want to
        # use all conv layers for example, all Batchnorm layers,
        # or something else.
        cam_per_layer = self.compute_cam_per_layer(input_tensor,
                                                   targets,
                                                   eigen_smooth)
        return self.aggregate_multi_layers(cam_per_layer)
    
def log_step(step_name):
    print(f"{time.time()}: Starting {step_name}")
    sys.stdout.flush()  # Ensure the output is immediately visible

def process_prediction(pred, WW, HH):
    pred = F.upsample(pred, size=[WW,HH], mode='bilinear', align_corners=False)
    pred = pred.sigmoid().data.cpu().numpy().squeeze()
    pred = 255*(pred - pred.min()) / (pred.max() - pred.min() + 1e-8)
    return pred.astype(np.uint8)

"""
===================================================================================================
    Main
===================================================================================================
"""
if __name__ == "__main__":
    # Counter
    counter = 1

    # Folder containing images
    image_folder_path = 'C:\\Users\\pharm\\Documents\\0 - Debra PhD Courses\\2 - Academic Collaborations\\00 - OADII MURDOC\\dataset\\train\\imgs\\'
    
    # List all images in the folder
    image_files = [os.path.join(image_folder_path, file) for file in os.listdir(image_folder_path) if file.endswith(('jpg', 'jpeg', 'png'))]
    
    # Loop through each image in the folder
    for image_file in image_files:
        iaiDecision(image_file)
        counter += 1
        
        if counter >= 5:
            break