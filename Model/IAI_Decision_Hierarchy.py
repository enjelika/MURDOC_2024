# -*- coding: utf-8 -*-
"""
IAI_Decision_Hierarchy.py (with Detection Consolidation)

Description: Decision Hierarchy for CODS XAI with MICA parameter support
    and consolidated detection output
    
    Lvl 1 - Binary mask evaluation  - Is anything present?
    Lvl 2 - Ranking mask evaluation - Where is the weak camouflage located?
    Lvl 3 - Object Part Identification with Consolidation - What parts break camouflage?
"""

import os
import sys
import json
import time
import traceback
from collections import defaultdict

import cv2
import numpy as np
from PIL import Image, ImageFile, ImageDraw, ImageFont
ImageFile.LOAD_TRUNCATED_IMAGES = True

import matplotlib.colors as color
import matplotlib.pyplot as plt
from skimage.measure import label, regionprops, find_contours

import tensorflow as tf
os.environ["CUDA_VISIBLE_DEVICES"] = "0"

import torch
import torch.nn.functional as F

from pytorch_grad_cam import GradCAM
from pytorch_grad_cam.utils.model_targets import ClassifierOutputTarget
from pytorch_grad_cam.utils.image import show_cam_on_image as cam_overlay

from model.ResNet_models import Generator


# ================================================================================================
# Detection Consolidation Classes
# ================================================================================================
class DetectionConsolidator:
    """
    Consolidates overlapping and nearby detections into coherent objects
    """
    
    def __init__(self, 
                 iou_threshold: float = 0.25,
                 distance_threshold: float = 80.0,
                 min_confidence: float = 0.10):
        """
        Args:
            iou_threshold: IoU threshold for considering boxes as overlapping
            distance_threshold: Distance threshold (pixels) for grouping nearby boxes
            min_confidence: Minimum confidence to keep a detection
        """
        self.iou_threshold = iou_threshold
        self.distance_threshold = distance_threshold
        self.min_confidence = min_confidence
    
    def consolidate_detections(self, detections):
        """
        Main consolidation pipeline
        
        Args:
            detections: List of detection dicts with keys:
                - 'bbox': [x1, y1, x2, y2]
                - 'confidence': float
                - 'label': str (e.g., "Object's leg")
                
        Returns:
            Consolidated list of detections
        """
        if not detections:
            return []
        
        # Step 1: Filter by minimum confidence
        filtered = [d for d in detections if d['confidence'] >= self.min_confidence]
        
        if not filtered:
            return []
        
        # Step 2: Apply Non-Maximum Suppression
        nms_detections = self.non_max_suppression(filtered)
        
        # Step 3: Group nearby detections (parts of same object)
        grouped = self.group_nearby_detections(nms_detections)
        
        # Step 4: Merge groups into single detections
        consolidated = self.merge_groups(grouped)
        
        return consolidated
    
    def non_max_suppression(self, detections):
        """Apply NMS to remove highly overlapping detections"""
        if len(detections) == 0:
            return []
        
        # Sort by confidence (descending)
        detections = sorted(detections, key=lambda x: x['confidence'], reverse=True)
        
        kept = []
        suppressed = set()
        
        for i, det in enumerate(detections):
            if i in suppressed:
                continue
                
            kept.append(det)
            
            # Suppress overlapping boxes
            for j in range(i + 1, len(detections)):
                if j in suppressed:
                    continue
                
                iou = self.calculate_iou(det['bbox'], detections[j]['bbox'])
                
                if iou > self.iou_threshold:
                    suppressed.add(j)
        
        return kept
    
    def group_nearby_detections(self, detections):
        """Group detections that are close to each other (likely same object)"""
        if len(detections) == 0:
            return []
        
        # Calculate centroids
        centroids = []
        for det in detections:
            bbox = det['bbox']
            cx = (bbox[0] + bbox[2]) / 2
            cy = (bbox[1] + bbox[3]) / 2
            centroids.append((cx, cy))
        
        # Group by proximity
        groups = []
        assigned = set()
        
        for i, det in enumerate(detections):
            if i in assigned:
                continue
            
            # Start new group
            group = [det]
            assigned.add(i)
            
            # Find nearby detections
            for j in range(i + 1, len(detections)):
                if j in assigned:
                    continue
                
                dist = self.euclidean_distance(centroids[i], centroids[j])
                
                if dist < self.distance_threshold:
                    group.append(detections[j])
                    assigned.add(j)
            
            groups.append(group)
        
        return groups
    
    def merge_groups(self, groups):
        """Merge each group into a single consolidated detection"""
        consolidated = []
        
        for group in groups:
            if not group:
                continue
            
            # Calculate merged bounding box (union of all boxes)
            merged_bbox = self.merge_bboxes([d['bbox'] for d in group])
            
            # Average confidence
            avg_confidence = np.mean([d['confidence'] for d in group])
            
            # Max confidence
            max_confidence = max([d['confidence'] for d in group])
            
            # Determine label (most common or highest confidence)
            label = self.determine_label(group)
            
            # Count parts
            part_counts = defaultdict(int)
            for d in group:
                base_label = self.extract_base_label(d['label'])
                part_counts[base_label] += 1
            
            consolidated.append({
                'bbox': merged_bbox,
                'confidence': max_confidence,
                'avg_confidence': avg_confidence,
                'label': label,
                'part_count': len(group),
                'parts': dict(part_counts)
            })
        
        # Sort by confidence
        consolidated.sort(key=lambda x: x['confidence'], reverse=True)
        
        return consolidated
    
    def calculate_iou(self, bbox1, bbox2):
        """Calculate Intersection over Union"""
        x1_min, y1_min, x1_max, y1_max = bbox1
        x2_min, y2_min, x2_max, y2_max = bbox2
        
        # Calculate intersection
        x_inter_min = max(x1_min, x2_min)
        y_inter_min = max(y1_min, y2_min)
        x_inter_max = min(x1_max, x2_max)
        y_inter_max = min(y1_max, y2_max)
        
        if x_inter_max < x_inter_min or y_inter_max < y_inter_min:
            return 0.0
        
        intersection = (x_inter_max - x_inter_min) * (y_inter_max - y_inter_min)
        
        # Calculate union
        area1 = (x1_max - x1_min) * (y1_max - y1_min)
        area2 = (x2_max - x2_min) * (y2_max - y2_min)
        union = area1 + area2 - intersection
        
        return intersection / union if union > 0 else 0.0
    
    def euclidean_distance(self, point1, point2):
        """Calculate Euclidean distance between two points"""
        return np.sqrt((point1[0] - point2[0])**2 + (point1[1] - point2[1])**2)
    
    def merge_bboxes(self, bboxes):
        """Merge multiple bounding boxes into one (union)"""
        x_mins = [bbox[0] for bbox in bboxes]
        y_mins = [bbox[1] for bbox in bboxes]
        x_maxs = [bbox[2] for bbox in bboxes]
        y_maxs = [bbox[3] for bbox in bboxes]
        
        return [
            min(x_mins),
            min(y_mins),
            max(x_maxs),
            max(y_maxs)
        ]
    
    def determine_label(self, group):
        """Determine the best label for a merged group"""
        # Count label frequencies
        label_counts = defaultdict(int)
        label_max_conf = defaultdict(float)
        
        for det in group:
            base_label = self.extract_base_label(det['label'])
            label_counts[base_label] += 1
            label_max_conf[base_label] = max(
                label_max_conf[base_label], 
                det['confidence']
            )
        
        # If multiple different parts (>2), call it "Camouflaged object"
        if len(label_counts) > 2:
            return "Camouflaged object"
        
        # Otherwise return most confident label
        best_label = max(label_max_conf.items(), key=lambda x: x[1])[0]
        
        return f"Object ({best_label})"
    
    def extract_base_label(self, label):
        """Extract base part name from label like "Object's leg" """
        if "'s " in label:
            return label.split("'s ")[-1].strip()
        return label.strip()


# ================================================================================================
# Lazy Loading Manager - Singleton pattern for managing resources
# ================================================================================================
class LazyResourceManager:
    _instance = None
    _initialized = False

    def __new__(cls):
        if cls._instance is None:
            cls._instance = super(LazyResourceManager, cls).__new__(cls)
        return cls._instance

    def __init__(self):
        if not LazyResourceManager._initialized:
            self._cods_model = None
            self._detect_fn = None
            self._rd_bl_colormap = None
            self._bl_gr_rd_bl_colormap = None
            self._output_dirs_created = False
            LazyResourceManager._initialized = True

    @property
    def cods_model(self):
        if self._cods_model is None:
            self._cods_model = self._load_cods_model()
        return self._cods_model

    @property
    def detect_fn(self):
        if self._detect_fn is None:
            self._detect_fn = self._load_tensorflow_model()
        return self._detect_fn

    @property
    def RdBl(self):
        if self._rd_bl_colormap is None:
            self._create_colormaps()
        return self._rd_bl_colormap

    @property
    def blGrRdBl(self):
        if self._bl_gr_rd_bl_colormap is None:
            self._create_colormaps()
        return self._bl_gr_rd_bl_colormap

    def _load_cods_model(self):
        cods = Generator(channel=32)
        model_path = "./models/Resnet/Model_50_gen.pth"

        if torch.cuda.is_available():
            cods.load_state_dict(torch.load(model_path))
            cods.cuda()
        else:
            cods.load_state_dict(torch.load(model_path, map_location=torch.device("cpu")))

        cods.eval()
        return cods

    def _load_tensorflow_model(self):
        PATH_TO_SAVED_MODEL = "models/d7_f/saved_model"
        with tf.device("/CPU:0"):
            detect_fn = tf.saved_model.load(PATH_TO_SAVED_MODEL)
        return detect_fn

    def _create_colormaps(self):
        # RdBl
        if "RdBl" not in plt.colormaps():
            RdBl_colors = ["black", "black", "red", "red"]
            self._rd_bl_colormap = color.LinearSegmentedColormap.from_list("RdBl", RdBl_colors)
            plt.register_cmap(cmap=self._rd_bl_colormap)
        else:
            self._rd_bl_colormap = plt.get_cmap("RdBl")

        # blGrRdBl
        if "blGrRdBl" not in plt.colormaps():
            blGrRdBl_colors = ["black", "blue", "green", "red", "red"]
            self._bl_gr_rd_bl_colormap = color.LinearSegmentedColormap.from_list("blGrRdBl", blGrRdBl_colors)
            plt.register_cmap(cmap=self._bl_gr_rd_bl_colormap)
        else:
            self._bl_gr_rd_bl_colormap = plt.get_cmap("blGrRdBl")

    def ensure_output_dirs(self):
        if not self._output_dirs_created:
            for d in ["figures", "bbox_figures", "jsons", "outputs", "results", "detection_results"]:
                os.makedirs(d, exist_ok=True)
            self._output_dirs_created = True

    def clear_cache(self):
        self._cods_model = None
        self._detect_fn = None
        if torch.cuda.is_available():
            torch.cuda.empty_cache()


resource_manager = LazyResourceManager()


# ================================================================================================
# MICA parameter loading + threshold mapping
# ================================================================================================
def load_mica_params(params_path=None):
    """Load MICA parameters, with fallback to defaults"""
    if params_path is None:
        params_path = os.path.join(".", "models", "mica_params.json")
    
    default = {"sensitivity": 1.5, "bias": 0.0}
    
    try:
        if os.path.exists(params_path):
            with open(params_path, "r") as f:
                data = json.load(f)
            s = float(data.get("sensitivity", default["sensitivity"]))
            b = float(data.get("bias", default["bias"]))
            print(f"[INFO] Loaded MICA params: sensitivity={s}, bias={b}")
            return {"sensitivity": s, "bias": b}
    except Exception as e:
        print(f"[INFO] Using default params (could not read {params_path}: {e})")
    
    return default


def compute_binary_threshold(mica_params, base_thresh=0.5):
    """
    Maps sensitivity/bias -> threshold in [0.05, 0.95].
    - Higher sensitivity => lower threshold (more positives)
    - Bias shifts threshold directly
    """
    s = mica_params.get("sensitivity", 1.5)
    b = mica_params.get("bias", 0.0)

    thresh = base_thresh - 0.1 * (s - 1.0) + b
    thresh = float(np.clip(thresh, 0.05, 0.95))
    return thresh


# ================================================================================================
# Helper functions
# ================================================================================================
def add_label(image, label_text, label_position):
    draw = ImageDraw.Draw(image)
    try:
        font = ImageFont.truetype("arial.ttf", 20)
    except Exception:
        font = ImageFont.load_default()
    draw.text(label_position, label_text, fill="white", font=font, stroke_width=2, stroke_fill="black")
    return image


def processFixationMap(fix_image):
    blGrRdBl = resource_manager.blGrRdBl
    img_np = np.asarray(fix_image) / 255.0

    if img_np.ndim > 2:
        img_np = img_np.squeeze()
        if img_np.ndim > 2:
            img_np = img_np[:, :, 0]

    return Image.fromarray(blGrRdBl(img_np, bytes=True))


def findAreasOfWeakCamouflage(fix_image):
    RdBl = resource_manager.RdBl
    img_np = np.asarray(fix_image) / 255.0

    if img_np.ndim > 2:
        img_np = img_np.squeeze()
        if img_np.ndim > 2:
            img_np = img_np[:, :, 0]

    return Image.fromarray((RdBl(img_np) * 255).astype(np.uint8))


def mask_to_border(mask):
    open_cv_image = cv2.cvtColor(np.asarray(mask), cv2.COLOR_RGB2GRAY)
    h, w = open_cv_image.shape[:2]
    border = np.zeros((h, w), dtype=np.uint8)

    contours = find_contours(open_cv_image, 1)
    for contour in contours:
        for c in contour:
            x = int(c[0])
            y = int(c[1])
            border[x, y] = 255

    return border


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


def overlap(bbox1, bbox2):
    def overlap1D(b1, b2):
        return b1[1] >= b2[0] and b2[1] >= b1[0]

    return overlap1D(bbox1[:2], bbox2[:2]) and overlap1D(bbox1[2:], bbox2[2:])


def apply_mask(heatmap, mask):
    """Apply a binary mask to a heatmap."""
    if isinstance(heatmap, Image.Image):
        heatmap = np.asarray(heatmap)
        trans_heatmap = np.transpose(heatmap)
    else:
        trans_heatmap = np.transpose(heatmap)

    mask_broadcasted = np.broadcast_to(mask, trans_heatmap.shape)
    masked_heatmap = mask_broadcasted * trans_heatmap
    masked_heatmap = np.transpose(masked_heatmap)
    return masked_heatmap


def process_prediction(pred, WW, HH):
    pred = F.upsample(pred, size=[HH, WW], mode="bilinear", align_corners=False)
    pred = pred.sigmoid().data.cpu().numpy().squeeze()
    pred = 255 * (pred - pred.min()) / (pred.max() - pred.min() + 1e-8)
    return pred.astype(np.uint8)


def create_segmented_overlay(original_image, rank_map, binary_mask, alpha=0.5):
    """Create a segmented overlay showing the camouflaged object prediction."""
    if original_image.dtype != np.uint8:
        original_image = original_image.astype(np.uint8)
    
    h, w = original_image.shape[:2]
    
    if rank_map.shape[:2] != (h, w):
        rank_map = cv2.resize(rank_map, (w, h), interpolation=cv2.INTER_LINEAR)
    
    if binary_mask.shape[:2] != (h, w):
        if binary_mask.shape == (w, h):
            binary_mask = binary_mask.T
        else:
            binary_mask = cv2.resize(binary_mask.astype(np.uint8), (w, h), interpolation=cv2.INTER_NEAREST)
    
    heatmap_colored = cv2.applyColorMap(rank_map, cv2.COLORMAP_JET)
    mask_3ch = np.stack([binary_mask] * 3, axis=-1).astype(np.float32)
    blended = original_image.copy().astype(np.float32)
    blended = blended * (1 - mask_3ch * alpha) + heatmap_colored.astype(np.float32) * (mask_3ch * alpha)
    
    return blended.astype(np.uint8)


# ================================================================================================
# Level Three (WITH CONSOLIDATION)
# ================================================================================================
def levelThree(original_image, bbox, message, filename, mica_params):
    """
    Object part detection with consolidation
    """
    detect_fn = resource_manager.detect_fn

    y_size, x_size, _ = original_image.shape
    label_map = ["leg", "mouth", "shadow", "tail", "arm", "eye"]

    input_tensor = tf.convert_to_tensor(original_image)[tf.newaxis, ...]
    detections = detect_fn(input_tensor)

    # Collect all raw detections
    raw_detections = []
    
    for box1 in bbox:
        for i, score in enumerate(detections["detection_scores"].numpy()[0]):
            if score < 0.05:  # Skip very low confidence
                continue
                
            box2 = detections["detection_boxes"].numpy()[0][i]
            
            # Check if detection overlaps with weak camouflage region
            if overlap(
                [box1["x1"], box1["x2"], box1["y1"], box1["y2"]],
                [box2[1] * x_size, box2[3] * x_size, box2[0] * y_size, box2[2] * y_size],
            ):
                detected_class_idx = int(detections["detection_classes"].numpy()[0][i]) - 1
                if 0 <= detected_class_idx < len(label_map):
                    detected_class = label_map[detected_class_idx]
                else:
                    detected_class = "unknown"
                
                raw_detections.append({
                    'bbox': [
                        box2[1] * x_size,  # x1
                        box2[0] * y_size,  # y1
                        box2[3] * x_size,  # x2
                        box2[2] * y_size   # y2
                    ],
                    'confidence': float(score),
                    'label': f"Object's {detected_class}"
                })
    
    # CONSOLIDATE DETECTIONS
    sensitivity = mica_params.get("sensitivity", 1.5)
    
    # Adjust consolidation parameters based on sensitivity
    iou_threshold = 0.25
    distance_threshold = 100.0 if sensitivity < 1.0 else 50.0
    min_confidence = 0.10
    
    consolidator = DetectionConsolidator(
        iou_threshold=iou_threshold,
        distance_threshold=distance_threshold,
        min_confidence=min_confidence
    )
    
    consolidated = consolidator.consolidate_detections(raw_detections)
    
    # Save visualization with consolidated detections
    fig, axis = plt.subplots(1, figsize=(12, 6))
    axis.imshow(original_image)
    axis.axis("off")

    for det in consolidated:
        bbox_coords = det['bbox']
        axis.add_patch(
            plt.Rectangle(
                (bbox_coords[0], bbox_coords[1]),
                bbox_coords[2] - bbox_coords[0],
                bbox_coords[3] - bbox_coords[1],
                fill=False,
                linewidth=2,
                color=(1, 0, 0),
            )
        )
        axis.text(
            bbox_coords[0], 
            bbox_coords[1] - 10, 
            f"{det['label']} {det['confidence']:.2%}", 
            color=(1, 0, 0),
            fontsize=10,
            weight='bold'
        )

    fig.savefig(f"detection_results/{filename}.png", bbox_inches="tight", pad_inches=0)
    plt.close(fig)

    # Format consolidated message
    txt_content = []
    
    if not consolidated:
        message += "No camouflaged object parts detected.\n"
        txt_content.append("No object parts detected in weak camouflage areas.")
    elif len(consolidated) == 1:
        det = consolidated[0]
        part_word = "part" if det['part_count'] == 1 else "parts"
        message += f"Consolidated detection from {det['part_count']} {part_word}.\n"
        message += f"{det['label']}, Confidence: {det['confidence']:.2%}\n"
        
        # Show parts breakdown
        parts_str = ", ".join([f"{k} ({v})" for k, v in det['parts'].items()])
        message += f"Parts found: {parts_str}\n"
        
        txt_content.append(f"Consolidated Detection: {det['label']}")
        txt_content.append(f"Confidence: {det['confidence']:.4f}")
        txt_content.append(f"Part Count: {det['part_count']}")
        txt_content.append(f"Parts: {parts_str}")
    else:
        message += f"Identified {len(consolidated)} consolidated region(s).\n"
        for i, det in enumerate(consolidated[:5], 1):  # Top 5
            part_word = "part" if det['part_count'] == 1 else "parts"
            parts_str = ", ".join([f"{k}({v})" for k, v in det['parts'].items()])
            
            message += (
                f"{i}. {det['label']}, "
                f"Confidence: {det['confidence']:.2%} "
                f"({det['part_count']} {part_word})\n"
                f"   Parts: {parts_str}\n"
            )
            
            txt_content.append(f"\nRegion {i}:")
            txt_content.append(f"  Label: {det['label']}")
            txt_content.append(f"  Confidence: {det['confidence']:.4f}")
            txt_content.append(f"  Part Count: {det['part_count']}")
            txt_content.append(f"  Parts: {parts_str}")

    with open(f"detection_results/{filename}.txt", "w") as f:
        f.write("\n".join(txt_content))

    return message


# ================================================================================================
# Level Two
# ================================================================================================
def levelTwo(filename, original_image, all_fix_map, fixation_map, message, mica_params):
    # Save overview figure
    fig, axis = plt.subplots(1, 2, figsize=(12, 6))
    axis[0].imshow(original_image)
    axis[0].set_title("Original Image")
    axis[1].imshow(all_fix_map)
    axis[1].set_title("Fixation Map")
    plt.tight_layout()
    plt.savefig(f"figures/fig_{filename}")
    plt.close(fig)

    # Bounding boxes from weak fixation
    bboxes = mask_to_bbox(fixation_map)

    open_cv_orImage1 = original_image.copy()
    open_cv_orImage2 = original_image.copy()

    cropped_images = []
    marked_image = open_cv_orImage1
    data = {
        "item": {"name": filename + ".jpg", "num_of_weak_areas": len(bboxes)},
        "weak_area_bbox": [],
    }

    for bbox in bboxes:
        starting_point = (bbox[0], bbox[1])
        ending_point = (bbox[2], bbox[3])
        marked_image = cv2.rectangle(open_cv_orImage1, starting_point, ending_point, (255, 0, 0), 2)

        ci = open_cv_orImage2[bbox[1] : bbox[3], bbox[0] : bbox[2]]
        cropped_images.append(ci)

        data["weak_area_bbox"].append({"x1": bbox[0], "y1": bbox[1], "x2": bbox[2], "y2": bbox[3]})

    with open(f"jsons/{filename}.json", "w") as f:
        json.dump(data, f, indent=6)

    # Figure of marked + first crop
    fig, axis = plt.subplots(1, 2, figsize=(12, 6))
    axis[0].imshow(marked_image)
    axis[0].set_title("Identified Weak Camo")
    if cropped_images and cropped_images[0].size != 0:
        axis[1].imshow(cropped_images[0])
    axis[1].set_title("Cropped Weak Camo Area")
    plt.savefig(f"bbox_figures/fig_{filename}")
    plt.close(fig)

    message += f"Identified {len(bboxes)} weak camouflaged area(s).\n"
    output = levelThree(original_image, data["weak_area_bbox"], message, filename, mica_params)
    return output


# ================================================================================================
# Level One
# ================================================================================================
def levelOne(filename, binary_map, all_fix_map, fix_image, original_image, message, mica_params):
    all_zeros = not binary_map.any()
    if all_zeros:
        message += "No object present.\n"
        return message

    message += "Object present.\n"
    return levelTwo(filename, original_image, all_fix_map, fix_image, message, mica_params)


# ================================================================================================
# GradCAM helper
# ================================================================================================
class MultiOutputGradCAM(GradCAM):
    def __init__(self, model, target_layers, output_index=0, reshape_transform=None):
        super().__init__(model, target_layers, reshape_transform)
        self.output_index = output_index

    def forward(self, input_tensor, targets=None, eigen_smooth=False):
        outputs = self.activations_and_grads(input_tensor)
        output = outputs[self.output_index]

        if targets is None:
            target_categories = np.argmax(output.cpu().data.numpy(), axis=-1)
            targets = [ClassifierOutputTarget(category) for category in target_categories]

        if self.uses_gradients:
            self.model.zero_grad()
            loss = output.sum()
            loss.backward(retain_graph=True)

        cam_per_layer = self.compute_cam_per_layer(input_tensor, targets, eigen_smooth)
        return self.aggregate_multi_layers(cam_per_layer)


# ================================================================================================
# Main IAI entry point
# ================================================================================================
def iaiDecision(file_path, output_root=None, force_reload=False):
    try:
        if force_reload:
            resource_manager.clear_cache()

        resource_manager.ensure_output_dirs()

        cods = resource_manager.cods_model
        _ = resource_manager.detect_fn

        file_name = os.path.splitext(os.path.basename(file_path))[0]

        # Output directory per image
        if output_root:
            os.makedirs(output_root, exist_ok=True)
            out_dir = os.path.join(output_root, file_name)
        else:
            out_dir = os.path.join("outputs", file_name)
        os.makedirs(out_dir, exist_ok=True)

        message = f"Decision for {file_name}:\n"

        original_image = cv2.imread(file_path)
        if original_image is None:
            raise ValueError(f"Unable to load image from path: {file_path}")

        # Preprocess image for CODS model
        image = cv2.cvtColor(original_image, cv2.COLOR_BGR2RGB)
        image = cv2.resize(image, (224, 224))
        image = image.transpose((2, 0, 1))
        image = image / 255.0
        image = torch.from_numpy(image).float().unsqueeze(0)

        HH, WW = original_image.shape[:2]

        if torch.cuda.is_available():
            image = image.cuda()

        # Model forward
        fix_pred, _, cod_pred2 = cods.forward(image)

        # Resize preds to original dims
        fix_image = process_prediction(fix_pred, WW, HH)
        bm_image = process_prediction(cod_pred2, WW, HH)

        # Save raw output maps
        Image.fromarray(bm_image).convert("L").save(os.path.join(out_dir, "binary_image.png"))
        Image.fromarray(fix_image).convert("L").save(os.path.join(out_dir, "fixation_image.png"))

        # Grad-CAM
        target_layer_fix = [cods.get_x4_layer()]
        grad_cam_fix = MultiOutputGradCAM(model=cods, target_layers=target_layer_fix, output_index=0)

        target_layer_cod = [cods.get_x4_2_layer()]
        grad_cam_cod = MultiOutputGradCAM(model=cods, target_layers=target_layer_cod, output_index=2)

        grayscale_cam_fix = None
        grayscale_cam_cod = None

        try:
            grayscale_cam_fix = grad_cam_fix(input_tensor=image)
        except Exception as e:
            print(f"[WARN] grad_cam_fix failed: {e}")

        try:
            grayscale_cam_cod = grad_cam_cod(input_tensor=image)
        except Exception as e:
            print(f"[WARN] grad_cam_cod failed: {e}")

        input_image = image.squeeze(0).permute(1, 2, 0).detach().cpu().numpy()
        denom = (input_image.max() - input_image.min()) + 1e-8
        input_image = (input_image - input_image.min()) / denom
        input_image = input_image.astype(np.float32)

        if grayscale_cam_fix is not None:
            heatmap_fix = cam_overlay(input_image, grayscale_cam_fix[0], use_rgb=True)
            cv2.imwrite(os.path.join(out_dir, "gradcam_fix.png"), cv2.cvtColor(heatmap_fix, cv2.COLOR_RGB2BGR))

        if grayscale_cam_cod is not None:
            heatmap_cod = cam_overlay(input_image, grayscale_cam_cod[0], use_rgb=True)
            cv2.imwrite(os.path.join(out_dir, "gradcam_cod.png"), cv2.cvtColor(heatmap_cod, cv2.COLOR_RGB2BGR))

        # MICA thresholding
        mica = load_mica_params()
        thresh = compute_binary_threshold(mica_params=mica, base_thresh=0.5)
        bm_thresh_255 = int(round(255 * thresh))

        trans_img = np.transpose(np.where(bm_image > bm_thresh_255, 1, 0))
        img_np = np.asarray(trans_img, dtype=np.uint8)

        masked_fix_map = apply_mask(Image.fromarray(fix_image), img_np)

        weak_fix_map = findAreasOfWeakCamouflage(masked_fix_map)
        all_fix_map = processFixationMap(masked_fix_map)

        # Create segmented overlay
        binary_mask_for_overlay = np.where(bm_image > bm_thresh_255, 1, 0).astype(np.uint8)
        
        segmented_output = create_segmented_overlay(
            original_image=original_image,
            rank_map=fix_image,
            binary_mask=binary_mask_for_overlay,
            alpha=0.6
        )
        
        results_dir = "results"
        os.makedirs(results_dir, exist_ok=True)
        segmented_path = os.path.join(results_dir, f"segmented_{file_name}.jpg")
        cv2.imwrite(segmented_path, segmented_output)
        print(f"[INFO] Saved segmented output to: {segmented_path}")

        cv2.imwrite(os.path.join(out_dir, "segmented_overlay.jpg"), segmented_output)

        # Run decision hierarchy (now with consolidation)
        output = levelOne(file_name, img_np, all_fix_map, weak_fix_map, original_image, message, mica)

        return output

    except Exception as e:
        error_message = f"An error occurred: {str(e)}\nTraceback:\n{traceback.format_exc()}"
        print(error_message)
        return f"Error occurred: {str(e)}"


# ================================================================================================
# Optional utilities
# ================================================================================================
def preload_resources():
    _ = resource_manager.cods_model
    _ = resource_manager.detect_fn
    _ = resource_manager.RdBl
    _ = resource_manager.blGrRdBl
    resource_manager.ensure_output_dirs()


def clear_resources():
    resource_manager.clear_cache()


# ================================================================================================
# Main
# ================================================================================================
def parse_args(argv):
    """
    Usage:
      python IAI_Decision_Hierarchy.py <image_path> [output_dir] [--force-reload] [--clear]
    """
    if len(argv) < 2:
        return None

    image_path = argv[1]
    output_dir = None
    force_reload = False
    do_clear = False

    if len(argv) >= 3 and not argv[2].startswith("--"):
        output_dir = argv[2]

    for a in argv[2:]:
        if a == "--force-reload":
            force_reload = True
        if a == "--clear":
            do_clear = True

    return image_path, output_dir, force_reload, do_clear


if __name__ == "__main__":
    parsed = parse_args(sys.argv)
    if parsed is None:
        print("Error: Missing required arguments. Usage: python script.py <image_path> [output_dir] [--force-reload] [--clear]",
              file=sys.stderr)
        sys.exit(1)

    image_path, output_dir, force_reload, do_clear = parsed

    try:
        final_result = iaiDecision(image_path, output_root=output_dir, force_reload=force_reload)
        print(final_result)
    except Exception:
        traceback.print_exc(file=sys.stderr)
        sys.exit(1)
    finally:
        if do_clear:
            clear_resources()