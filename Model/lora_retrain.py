"""
lora_retrain.py — Session-Based LoRA Retraining Pipeline for MICA

Consumes queued training sessions (from MICA's session management) and applies
LoRA fine-tuning to the RankNet model's Saliency_feat_decoder using expert-edited
rank maps and binary masks as supervisory signals.

This implements Section 5.5.3 of the dissertation:
    "The retraining pipeline consumes queued sessions and updates RankNet parameters
     using edited rank maps as supervisory signals. Retraining occurs after session
     completion rather than during interaction, preserving user interface responsiveness
     while enabling incremental adaptation across sessions."

Architecture:
    - Reads retrain_queue.txt for pending sessions
    - Loads session data: original images, edited rank maps (fix/), edited masks (bi_gt/)
    - Injects LoRA adapters into sal_dec (Saliency_feat_decoder) Conv2d layers
    - Fine-tunes LoRA parameters using combined rank map + mask supervision loss
    - Saves LoRA weights as separate adapter file (preserving base model)
    - Optionally merges LoRA into base weights for deployment

Usage:
    # Process all queued sessions
    python lora_retrain.py

    # Process specific session
    python lora_retrain.py --session session_20260224_143000

    # Merge LoRA weights into base model for deployment
    python lora_retrain.py --merge --lora-path lora_adapters/latest.pth

    # Dry run (validate session data without training)
    python lora_retrain.py --dry-run

Author: Debra Hogue — MURDOC/MICA Project
"""

import os
import sys
import json
import glob
import argparse
import traceback
import datetime
from pathlib import Path
from typing import List, Dict, Optional, Tuple

import numpy as np
import cv2
from PIL import Image

import torch
import torch.nn as nn
import torch.nn.functional as F
from torch.utils.data import Dataset, DataLoader
from torch.optim import AdamW
from torch.optim.lr_scheduler import CosineAnnealingLR

# Add model directory to path for imports
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
MODEL_DIR = os.path.join(SCRIPT_DIR, "Model")
if MODEL_DIR not in sys.path:
    sys.path.insert(0, MODEL_DIR)
if SCRIPT_DIR not in sys.path:
    sys.path.insert(0, SCRIPT_DIR)

from model.ResNet_models import Generator
from lora_modules import (
    inject_lora_into_decoder,
    get_lora_parameters,
    save_lora_weights,
    load_lora_weights,
    merge_all_lora,
    count_lora_parameters,
)


# ============================================================================
# Configuration
# ============================================================================

class RetrainConfig:
    """Configuration for LoRA retraining pipeline."""

    def __init__(self, **overrides):
        # Paths (relative to exe/build directory)
        self.base_dir = os.path.join(SCRIPT_DIR, "bin", "x64", "Debug")
        self.model_path = os.path.join(self.base_dir, "models", "RankNet.pth")
        self.sessions_dir = os.path.join(self.base_dir, "training_sessions")
        self.queue_file = os.path.join(self.sessions_dir, "retrain_queue.txt")
        self.lora_output_dir = os.path.join(self.sessions_dir, "lora_adapters")

        # Model
        self.channel = 32  # Must match Generator(channel=32) used in MICA

        # LoRA hyperparameters
        self.lora_rank = 4          # Low rank keeps params small
        self.lora_alpha = 4.0       # alpha = rank → scaling = 1.0
        self.lora_dropout = 0.05    # Light dropout on LoRA path
        self.target_layers = None   # None = all Conv2d in sal_dec

        # Training
        self.learning_rate = 1e-4
        self.weight_decay = 0.01
        self.num_epochs = 30        # Per-session fine-tuning epochs
        self.batch_size = 4
        self.image_size = 224       # Must match model input size
        self.num_workers = 0        # 0 for Windows compatibility

        # Loss weights
        self.rank_loss_weight = 1.0     # Weight for rank map supervision
        self.mask_loss_weight = 0.5     # Weight for binary mask supervision
        self.reg_loss_weight = 0.001    # L2 regularization on LoRA params

        # Augmentation (light — user edits are precise)
        self.augment = True
        self.flip_prob = 0.3
        self.brightness_jitter = 0.1

        # Apply overrides
        for k, v in overrides.items():
            if hasattr(self, k):
                setattr(self, k, v)
            else:
                print(f"[WARN] Unknown config key: {k}")


# ============================================================================
# Dataset
# ============================================================================

class SessionDataset(Dataset):
    """
    Dataset for a single MICA training session.

    Each sample is a triplet:
        (original_image, edited_rank_map, edited_binary_mask)

    The edited maps serve as ground-truth supervision signals from the expert.
    """

    def __init__(
        self,
        session_dir: str,
        image_size: int = 224,
        augment: bool = False,
        flip_prob: float = 0.3,
        brightness_jitter: float = 0.1,
    ):
        self.session_dir = session_dir
        self.image_size = image_size
        self.augment = augment
        self.flip_prob = flip_prob
        self.brightness_jitter = brightness_jitter

        # Locate session subfolders
        self.img_dir = os.path.join(session_dir, "img")
        self.fix_dir = os.path.join(session_dir, "fix")      # Edited rank maps
        self.bigt_dir = os.path.join(session_dir, "bi_gt")    # Edited binary masks

        # Find all image triplets (must have all three)
        self.samples = self._find_samples()

    def _find_samples(self) -> List[Dict[str, str]]:
        """Find matching triplets of (image, rank_map, mask)."""
        samples = []

        if not os.path.isdir(self.img_dir):
            print(f"[WARN] No img/ directory in {self.session_dir}")
            return samples

        for img_file in sorted(os.listdir(self.img_dir)):
            name = os.path.splitext(img_file)[0]

            img_path = os.path.join(self.img_dir, img_file)

            # Look for corresponding rank map and mask
            rank_path = self._find_matching_file(self.fix_dir, name)
            mask_path = self._find_matching_file(self.bigt_dir, name)

            if rank_path or mask_path:
                samples.append({
                    "name": name,
                    "image": img_path,
                    "rank_map": rank_path,      # May be None
                    "binary_mask": mask_path,    # May be None
                })

        return samples

    def _find_matching_file(self, directory: str, name: str) -> Optional[str]:
        """Find a file matching the given name (any image extension)."""
        if not os.path.isdir(directory):
            return None
        for ext in [".png", ".jpg", ".jpeg", ".bmp"]:
            path = os.path.join(directory, name + ext)
            if os.path.exists(path):
                return path
        return None

    def __len__(self) -> int:
        return len(self.samples)

    def __getitem__(self, idx: int) -> Dict[str, torch.Tensor]:
        sample = self.samples[idx]

        # Load and preprocess image
        image = cv2.imread(sample["image"])
        if image is None:
            raise RuntimeError(f"Cannot load image: {sample['image']}")
        image = cv2.cvtColor(image, cv2.COLOR_BGR2RGB)
        image = cv2.resize(image, (self.image_size, self.image_size))

        # Load rank map (grayscale, 0-255)
        has_rank = sample["rank_map"] is not None
        if has_rank:
            rank_map = cv2.imread(sample["rank_map"], cv2.IMREAD_GRAYSCALE)
            if rank_map is None:
                has_rank = False
            else:
                rank_map = cv2.resize(rank_map, (self.image_size, self.image_size))
        if not has_rank:
            rank_map = np.zeros((self.image_size, self.image_size), dtype=np.uint8)

        # Load binary mask (grayscale, 0 or 255)
        has_mask = sample["binary_mask"] is not None
        if has_mask:
            mask = cv2.imread(sample["binary_mask"], cv2.IMREAD_GRAYSCALE)
            if mask is None:
                has_mask = False
            else:
                mask = cv2.resize(mask, (self.image_size, self.image_size),
                                  interpolation=cv2.INTER_NEAREST)
        if not has_mask:
            mask = np.zeros((self.image_size, self.image_size), dtype=np.uint8)

        # Augmentation (applied consistently to all three)
        if self.augment:
            image, rank_map, mask = self._augment(image, rank_map, mask)

        # Convert to tensors
        # Image: [C, H, W] normalized to [0, 1]
        image_tensor = torch.from_numpy(image.transpose(2, 0, 1)).float() / 255.0

        # Rank map: [1, H, W] normalized to [0, 1]
        rank_tensor = torch.from_numpy(rank_map).float().unsqueeze(0) / 255.0

        # Binary mask: [1, H, W] as binary {0, 1}
        mask_tensor = torch.from_numpy((mask > 127).astype(np.float32)).unsqueeze(0)

        return {
            "name": sample["name"],
            "image": image_tensor,
            "rank_map": rank_tensor,
            "binary_mask": mask_tensor,
            "has_rank": has_rank,
            "has_mask": has_mask,
        }

    def _augment(
        self,
        image: np.ndarray,
        rank_map: np.ndarray,
        mask: np.ndarray,
    ) -> Tuple[np.ndarray, np.ndarray, np.ndarray]:
        """Light augmentation — spatial transforms applied consistently."""
        # Random horizontal flip
        if np.random.random() < self.flip_prob:
            image = np.fliplr(image).copy()
            rank_map = np.fliplr(rank_map).copy()
            mask = np.fliplr(mask).copy()

        # Brightness jitter (image only)
        if self.brightness_jitter > 0:
            factor = 1.0 + np.random.uniform(
                -self.brightness_jitter, self.brightness_jitter
            )
            image = np.clip(image.astype(np.float32) * factor, 0, 255).astype(np.uint8)

        return image, rank_map, mask


class MultiSessionDataset(Dataset):
    """Combines multiple session datasets for cumulative training."""

    def __init__(self, session_datasets: List[SessionDataset]):
        self.datasets = session_datasets
        self._lengths = [len(d) for d in session_datasets]
        self._cumulative = []
        total = 0
        for length in self._lengths:
            self._cumulative.append(total)
            total += length
        self._total = total

    def __len__(self) -> int:
        return self._total

    def __getitem__(self, idx: int) -> Dict[str, torch.Tensor]:
        # Find which dataset this index belongs to
        for i, (start, length) in enumerate(
            zip(self._cumulative, self._lengths)
        ):
            if idx < start + length:
                return self.datasets[i][idx - start]
        raise IndexError(f"Index {idx} out of range for {self._total} samples")


# ============================================================================
# Training
# ============================================================================

class LoRATrainer:
    """
    LoRA fine-tuning trainer for RankNet.

    Handles:
    - Model loading and LoRA injection
    - Training loop with combined rank map + mask supervision
    - Checkpoint saving and loading
    - Weight merging for deployment
    """

    def __init__(self, config: RetrainConfig):
        self.config = config
        self.device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
        self.model = None
        self.lora_layers = None
        self.optimizer = None
        self.scheduler = None

        print(f"[INFO] Device: {self.device}")

    def setup_model(self, existing_lora_path: Optional[str] = None) -> None:
        """Load base model, inject LoRA, optionally load existing LoRA weights."""
        print(f"[INFO] Loading base model from: {self.config.model_path}")

        # Load Generator (wraps Saliency_feat_encoder)
        self.model = Generator(self.config.channel)

        if os.path.exists(self.config.model_path):
            if torch.cuda.is_available():
                self.model.load_state_dict(torch.load(self.config.model_path))
            else:
                self.model.load_state_dict(
                    torch.load(self.config.model_path, map_location="cpu")
                )
            print("[INFO] Base model weights loaded.")
        else:
            print(f"[WARN] Model checkpoint not found at {self.config.model_path}")
            print("[WARN] Using randomly initialized model — results will be poor!")

        # Freeze all base parameters
        for param in self.model.parameters():
            param.requires_grad = False

        # Inject LoRA into the saliency decoder (rank map branch)
        # sal_dec is the decoder that produces camouflage rank predictions
        print("[INFO] Injecting LoRA adapters into sal_dec...")
        self.lora_layers = inject_lora_into_decoder(
            decoder=self.model.sal_encoder.sal_dec,
            rank=self.config.lora_rank,
            alpha=self.config.lora_alpha,
            dropout=self.config.lora_dropout,
            target_layer_names=self.config.target_layers,
        )

        lora_params, frozen_params = count_lora_parameters(self.lora_layers)
        print(f"[INFO] LoRA layers injected: {len(self.lora_layers)}")
        print(f"[INFO] Trainable LoRA params: {lora_params:,}")
        print(f"[INFO] Frozen base params in decoder: {frozen_params:,}")
        print(f"[INFO] Parameter efficiency: {lora_params / max(frozen_params, 1) * 100:.2f}%")

        # Load existing LoRA weights if continuing from previous sessions
        if existing_lora_path and os.path.exists(existing_lora_path):
            print(f"[INFO] Loading existing LoRA weights from: {existing_lora_path}")
            meta = load_lora_weights(self.lora_layers, existing_lora_path)
            if meta:
                print(f"[INFO] Previous training metadata: {meta}")

        self.model.to(self.device)

        # Setup optimizer — only LoRA parameters are trainable
        lora_param_list = get_lora_parameters(self.model)
        self.optimizer = AdamW(
            lora_param_list,
            lr=self.config.learning_rate,
            weight_decay=self.config.weight_decay,
        )

        self.scheduler = CosineAnnealingLR(
            self.optimizer,
            T_max=self.config.num_epochs,
            eta_min=self.config.learning_rate * 0.01,
        )

    def train_on_session(
        self,
        dataset: Dataset,
        session_id: str,
    ) -> Dict[str, float]:
        """
        Train LoRA adapters on a single session's data.

        Returns dict of training metrics.
        """
        if len(dataset) == 0:
            print(f"[WARN] Session {session_id} has no training samples. Skipping.")
            return {"status": "skipped", "reason": "no_samples"}

        dataloader = DataLoader(
            dataset,
            batch_size=min(self.config.batch_size, len(dataset)),
            shuffle=True,
            num_workers=self.config.num_workers,
            pin_memory=torch.cuda.is_available(),
            drop_last=False,
        )

        self.model.train()

        # Only LoRA layers should be in train mode — base is frozen
        # But we need the full forward pass, so we set model to train
        # and rely on requires_grad=False for frozen params
        best_loss = float("inf")
        epoch_losses = []

        print(f"\n[TRAIN] Session: {session_id}")
        print(f"[TRAIN] Samples: {len(dataset)}, Epochs: {self.config.num_epochs}")
        print(f"[TRAIN] Batch size: {min(self.config.batch_size, len(dataset))}")

        for epoch in range(self.config.num_epochs):
            epoch_loss = 0.0
            rank_loss_total = 0.0
            mask_loss_total = 0.0
            n_batches = 0

            for batch in dataloader:
                images = batch["image"].to(self.device)
                rank_targets = batch["rank_map"].to(self.device)
                mask_targets = batch["binary_mask"].to(self.device)
                has_rank = batch["has_rank"]
                has_mask = batch["has_mask"]

                self.optimizer.zero_grad()

                # Forward pass through the model
                # Generator returns: fix_pred, init_pred (sal_dec output), ref_pred
                fix_pred, init_pred, ref_pred = self.model(images)

                # Compute supervision losses
                loss = torch.tensor(0.0, device=self.device)

                # Rank map loss (sal_dec output → user-edited rank map)
                # init_pred and ref_pred are the saliency decoder outputs
                if has_rank.any():
                    rank_mask_idx = has_rank.bool()

                    # Resize predictions to match target size
                    pred_rank = torch.sigmoid(ref_pred[rank_mask_idx])
                    target_rank = rank_targets[rank_mask_idx]

                    if pred_rank.shape[-2:] != target_rank.shape[-2:]:
                        pred_rank = F.interpolate(
                            pred_rank,
                            size=target_rank.shape[-2:],
                            mode="bilinear",
                            align_corners=False,
                        )

                    rank_loss = F.mse_loss(pred_rank, target_rank)
                    loss = loss + self.config.rank_loss_weight * rank_loss
                    rank_loss_total += rank_loss.item()

                # Binary mask loss (fix_pred → user-edited mask)
                if has_mask.any():
                    mask_idx = has_mask.bool()

                    pred_mask = torch.sigmoid(fix_pred[mask_idx])
                    target_mask = mask_targets[mask_idx]

                    if pred_mask.shape[-2:] != target_mask.shape[-2:]:
                        pred_mask = F.interpolate(
                            pred_mask,
                            size=target_mask.shape[-2:],
                            mode="bilinear",
                            align_corners=False,
                        )

                    mask_loss = F.binary_cross_entropy(pred_mask, target_mask)
                    loss = loss + self.config.mask_loss_weight * mask_loss
                    mask_loss_total += mask_loss.item()

                # L2 regularization on LoRA params (prevents catastrophic drift)
                if self.config.reg_loss_weight > 0:
                    reg_loss = torch.tensor(0.0, device=self.device)
                    for param in get_lora_parameters(self.model):
                        reg_loss = reg_loss + param.norm(2)
                    loss = loss + self.config.reg_loss_weight * reg_loss

                if loss.requires_grad:
                    loss.backward()
                    # Gradient clipping for stability
                    torch.nn.utils.clip_grad_norm_(
                        get_lora_parameters(self.model), max_norm=1.0
                    )
                    self.optimizer.step()

                epoch_loss += loss.item()
                n_batches += 1

            self.scheduler.step()

            avg_loss = epoch_loss / max(n_batches, 1)
            epoch_losses.append(avg_loss)

            if avg_loss < best_loss:
                best_loss = avg_loss

            # Print progress every 5 epochs
            if (epoch + 1) % 5 == 0 or epoch == 0:
                lr = self.optimizer.param_groups[0]["lr"]
                print(
                    f"  Epoch {epoch + 1:3d}/{self.config.num_epochs} | "
                    f"Loss: {avg_loss:.6f} | "
                    f"Rank: {rank_loss_total / max(n_batches, 1):.6f} | "
                    f"Mask: {mask_loss_total / max(n_batches, 1):.6f} | "
                    f"LR: {lr:.2e}"
                )

        return {
            "status": "completed",
            "session_id": session_id,
            "num_samples": len(dataset),
            "num_epochs": self.config.num_epochs,
            "best_loss": best_loss,
            "final_loss": epoch_losses[-1] if epoch_losses else None,
            "loss_history": epoch_losses,
        }

    def save_lora(
        self,
        session_id: str,
        metrics: Optional[Dict] = None,
    ) -> str:
        """Save LoRA weights to the output directory."""
        os.makedirs(self.config.lora_output_dir, exist_ok=True)

        # Save timestamped version
        timestamp = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
        filename = f"lora_{session_id}_{timestamp}.pth"
        filepath = os.path.join(self.config.lora_output_dir, filename)

        metadata = {
            "session_id": session_id,
            "timestamp": timestamp,
            "lora_rank": self.config.lora_rank,
            "lora_alpha": self.config.lora_alpha,
            "learning_rate": self.config.learning_rate,
            "num_epochs": self.config.num_epochs,
        }
        if metrics:
            metadata["training_metrics"] = {
                k: v for k, v in metrics.items()
                if k != "loss_history"  # Don't store full history in metadata
            }

        save_lora_weights(self.lora_layers, filepath, metadata)
        print(f"[INFO] LoRA weights saved: {filepath}")

        # Also save as 'latest.pth' for easy loading
        latest_path = os.path.join(self.config.lora_output_dir, "latest.pth")
        save_lora_weights(self.lora_layers, latest_path, metadata)
        print(f"[INFO] Latest LoRA weights updated: {latest_path}")

        return filepath

    def merge_and_save(self, output_path: Optional[str] = None) -> str:
        """
        Merge LoRA weights into the base model and save as a new checkpoint.

        This creates a standalone model that doesn't need the LoRA module at
        inference time — useful for deployment.
        """
        if output_path is None:
            output_path = self.config.model_path.replace(
                ".pth", "_lora_merged.pth"
            )

        print("[INFO] Merging LoRA weights into base model...")
        merge_all_lora(self.lora_layers)

        torch.save(self.model.state_dict(), output_path)
        print(f"[INFO] Merged model saved: {output_path}")

        return output_path


# ============================================================================
# Queue Management
# ============================================================================

def read_queue(queue_file: str) -> List[Dict[str, str]]:
    """Read pending sessions from retrain_queue.txt."""
    entries = []
    if not os.path.exists(queue_file):
        return entries

    with open(queue_file, "r") as f:
        for line in f:
            line = line.strip()
            if not line or line.startswith("#"):
                continue
            parts = line.split("|")
            entry = {"session_id": parts[0]}
            if len(parts) > 1:
                entry["queued_at"] = parts[1]
            entries.append(entry)

    return entries


def mark_session_processed(
    queue_file: str,
    session_id: str,
    processed_file: Optional[str] = None,
) -> None:
    """
    Remove a processed session from the queue and add to processed log.
    """
    # Remove from queue
    if os.path.exists(queue_file):
        with open(queue_file, "r") as f:
            lines = f.readlines()
        with open(queue_file, "w") as f:
            for line in lines:
                if session_id not in line:
                    f.write(line)

    # Add to processed log
    if processed_file is None:
        processed_file = queue_file.replace("retrain_queue.txt", "processed_sessions.txt")

    timestamp = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    with open(processed_file, "a") as f:
        f.write(f"{session_id}|{timestamp}|completed\n")


def find_session_dir(sessions_dir: str, session_id: str) -> Optional[str]:
    """Locate the session directory for a given session ID."""
    # Try exact match first
    direct = os.path.join(sessions_dir, session_id)
    if os.path.isdir(direct):
        return direct

    # Search for matching directories
    for entry in os.listdir(sessions_dir):
        full = os.path.join(sessions_dir, entry)
        if os.path.isdir(full) and session_id in entry:
            return full

    return None


# ============================================================================
# Main Pipeline
# ============================================================================

def run_pipeline(args: argparse.Namespace) -> None:
    """Main retraining pipeline."""
    config = RetrainConfig()

    # Override paths if specified
    if args.base_dir:
        config.base_dir = args.base_dir
        config.model_path = os.path.join(config.base_dir, "models", "RankNet.pth")
        config.sessions_dir = os.path.join(config.base_dir, "training_sessions")
        config.queue_file = os.path.join(config.sessions_dir, "retrain_queue.txt")
        config.lora_output_dir = os.path.join(config.sessions_dir, "lora_adapters")

    if args.model_path:
        config.model_path = args.model_path

    if args.epochs:
        config.num_epochs = args.epochs

    if args.rank:
        config.lora_rank = args.rank
        config.lora_alpha = float(args.rank)  # Keep scaling = 1.0

    if args.lr:
        config.learning_rate = args.lr

    # ---- Merge mode ----
    if args.merge:
        if not args.lora_path:
            print("[ERROR] --merge requires --lora-path")
            sys.exit(1)
        trainer = LoRATrainer(config)
        trainer.setup_model(existing_lora_path=args.lora_path)
        trainer.merge_and_save(args.output)
        return

    # ---- Determine sessions to process ----
    if args.session:
        # Process specific session
        sessions = [{"session_id": args.session}]
    else:
        # Process queue
        sessions = read_queue(config.queue_file)
        if not sessions:
            print("[INFO] No sessions in the retraining queue. Nothing to do.")
            return

    print(f"\n{'=' * 60}")
    print(f"  MICA LoRA Retraining Pipeline")
    print(f"  Sessions to process: {len(sessions)}")
    print(f"  LoRA rank: {config.lora_rank}")
    print(f"  Learning rate: {config.learning_rate}")
    print(f"  Epochs per session: {config.num_epochs}")
    print(f"{'=' * 60}\n")

    # ---- Setup trainer ----
    trainer = LoRATrainer(config)

    # Check for existing LoRA weights to continue from
    latest_lora = os.path.join(config.lora_output_dir, "latest.pth")
    existing_lora = latest_lora if os.path.exists(latest_lora) else None

    trainer.setup_model(existing_lora_path=existing_lora)

    # ---- Process each session ----
    results = []

    for entry in sessions:
        session_id = entry["session_id"]
        print(f"\n{'─' * 40}")
        print(f"Processing: {session_id}")
        print(f"{'─' * 40}")

        # Find session directory
        session_dir = find_session_dir(config.sessions_dir, session_id)
        if session_dir is None:
            print(f"[ERROR] Session directory not found for: {session_id}")
            results.append({"session_id": session_id, "status": "error_not_found"})
            continue

        # Load session dataset
        dataset = SessionDataset(
            session_dir=session_dir,
            image_size=config.image_size,
            augment=config.augment,
            flip_prob=config.flip_prob,
            brightness_jitter=config.brightness_jitter,
        )

        print(f"[INFO] Found {len(dataset)} training samples in {session_dir}")

        if args.dry_run:
            print("[DRY RUN] Would train on this session. Skipping.")
            results.append({
                "session_id": session_id,
                "status": "dry_run",
                "num_samples": len(dataset),
            })
            continue

        # Train
        metrics = trainer.train_on_session(dataset, session_id)
        results.append(metrics)

        # Save LoRA weights after each session
        if metrics.get("status") == "completed":
            trainer.save_lora(session_id, metrics)

            # Mark as processed in queue
            if not args.session:
                mark_session_processed(config.queue_file, session_id)

    # ---- Summary ----
    print(f"\n{'=' * 60}")
    print(f"  Retraining Summary")
    print(f"{'=' * 60}")
    for r in results:
        sid = r.get("session_id", "unknown")
        status = r.get("status", "unknown")
        samples = r.get("num_samples", 0)
        best = r.get("best_loss")
        best_str = f"{best:.6f}" if best is not None else "N/A"
        print(f"  {sid}: {status} ({samples} samples, best loss: {best_str})")

    # Save training report
    report_path = os.path.join(
        config.lora_output_dir,
        f"training_report_{datetime.datetime.now():%Y%m%d_%H%M%S}.json",
    )
    os.makedirs(config.lora_output_dir, exist_ok=True)
    with open(report_path, "w") as f:
        # Remove non-serializable items
        clean_results = []
        for r in results:
            clean = {k: v for k, v in r.items() if k != "loss_history"}
            clean_results.append(clean)
        json.dump(
            {
                "timestamp": datetime.datetime.now().isoformat(),
                "config": {
                    "lora_rank": config.lora_rank,
                    "lora_alpha": config.lora_alpha,
                    "learning_rate": config.learning_rate,
                    "num_epochs": config.num_epochs,
                },
                "sessions": clean_results,
            },
            f,
            indent=2,
        )
    print(f"\n[INFO] Training report saved: {report_path}")


# ============================================================================
# CLI
# ============================================================================

def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="MICA LoRA Retraining Pipeline",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Examples:
  # Process all queued sessions
  python lora_retrain.py

  # Process a specific session
  python lora_retrain.py --session session_20260224_143000

  # Dry run (validate data without training)
  python lora_retrain.py --dry-run

  # Continue from previous LoRA weights with custom settings
  python lora_retrain.py --rank 8 --epochs 50 --lr 5e-5

  # Merge LoRA into base model for deployment
  python lora_retrain.py --merge --lora-path training_sessions/lora_adapters/latest.pth
        """,
    )

    parser.add_argument(
        "--session",
        type=str,
        default=None,
        help="Process a specific session ID (instead of the queue).",
    )
    parser.add_argument(
        "--base-dir",
        type=str,
        default=None,
        help="Override the base directory (default: bin/x64/Debug).",
    )
    parser.add_argument(
        "--model-path",
        type=str,
        default=None,
        help="Override the base model checkpoint path.",
    )
    parser.add_argument(
        "--rank",
        type=int,
        default=None,
        help="LoRA rank (default: 4). Higher = more capacity but more params.",
    )
    parser.add_argument(
        "--epochs",
        type=int,
        default=None,
        help="Number of training epochs per session (default: 30).",
    )
    parser.add_argument(
        "--lr",
        type=float,
        default=None,
        help="Learning rate (default: 1e-4).",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Validate session data without actually training.",
    )
    parser.add_argument(
        "--merge",
        action="store_true",
        help="Merge LoRA weights into base model for deployment.",
    )
    parser.add_argument(
        "--lora-path",
        type=str,
        default=None,
        help="Path to LoRA weights file (for --merge or to continue training).",
    )
    parser.add_argument(
        "--output",
        type=str,
        default=None,
        help="Output path for merged model (with --merge).",
    )

    return parser.parse_args()


if __name__ == "__main__":
    args = parse_args()
    try:
        run_pipeline(args)
    except KeyboardInterrupt:
        print("\n[INFO] Training interrupted by user.")
    except Exception:
        traceback.print_exc()
        sys.exit(1)