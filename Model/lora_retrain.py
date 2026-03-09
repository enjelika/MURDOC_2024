"""
lora_retrain.py - Session-End LoRA Retraining Pipeline for MICA

Triggered by C# when a session ends. Consumes the session's edited
rank maps and binary masks to fine-tune RankNet's saliency decoder
using LoRA (Low-Rank Adaptation).

This implements Section 5.5.3 of the dissertation:
    "Retraining occurs after session completion rather than during
     interaction, preserving user interface responsiveness while
     enabling incremental adaptation across sessions."

C# integration (in OnEndSessionRequested, after QueueSessionForRetraining):
    var startInfo = new ProcessStartInfo {
        FileName = "python",
        Arguments = $"Model\\lora_retrain.py --session session_{sessionId}",
        WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    Process.Start(startInfo);

Usage:
    python lora_retrain.py --session session_20260224_143000
    python lora_retrain.py --session session_20260224_143000 --epochs 50
    python lora_retrain.py --dry-run --session session_20260224_143000

Author: Debra Hogue - MURDOC/MICA Project
"""

import os
import sys
import json
import argparse
import traceback
import datetime
from typing import List, Dict, Optional, Tuple

import numpy as np
import cv2

import torch
import torch.nn as nn
import torch.nn.functional as F
from torch.utils.data import Dataset, DataLoader
from torch.optim import AdamW
from torch.optim.lr_scheduler import CosineAnnealingLR

# Add Model directory to path for imports
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
if SCRIPT_DIR not in sys.path:
    sys.path.insert(0, SCRIPT_DIR)

from model.ResNet_models import Generator
from lora_modules import (
    inject_lora_into_decoder,
    get_lora_parameters,
    save_lora_weights,
    load_lora_weights,
    count_lora_parameters,
)


# ============================================================================
# Configuration
# ============================================================================

class RetrainConfig:
    """All configuration for the LoRA retraining pipeline."""

    def __init__(self, **overrides):
        # Paths - relative to working directory (bin/x64/Debug at runtime)
        self.model_path = os.path.join("models", "Resnet", "Model_50_gen.pth")
        self.sessions_dir = "training_sessions"
        self.queue_file = os.path.join(self.sessions_dir, "retrain_queue.txt")
        self.lora_output_dir = os.path.join(self.sessions_dir, "lora_adapters")

        # Model architecture (must match Generator init)
        self.channel = 32

        # LoRA hyperparameters
        self.lora_rank = 4
        self.lora_alpha = 4.0
        self.lora_dropout = 0.05

        # Training
        self.learning_rate = 1e-4
        self.weight_decay = 0.01
        self.num_epochs = 30
        self.batch_size = 4
        self.image_size = 224
        self.num_workers = 0  # 0 for Windows

        # Loss weights
        self.rank_loss_weight = 1.0
        self.mask_loss_weight = 0.5
        self.reg_loss_weight = 0.001

        # Augmentation
        self.augment = True
        self.flip_prob = 0.3

        for k, v in overrides.items():
            if hasattr(self, k):
                setattr(self, k, v)


# ============================================================================
# Dataset
# ============================================================================

class SessionDataset(Dataset):
    """
    Loads a session's edited artifacts as training samples.

    Each sample: (original_image, edited_rank_map, edited_binary_mask)
    """

    def __init__(self, session_dir, image_size=224, augment=False):
        self.image_size = image_size
        self.augment = augment

        self.img_dir = os.path.join(session_dir, "img")
        self.fix_dir = os.path.join(session_dir, "fix")
        self.bigt_dir = os.path.join(session_dir, "bi_gt")

        self.samples = self._find_samples()

    def _find_samples(self):
        samples = []
        if not os.path.isdir(self.img_dir):
            return samples

        for img_file in sorted(os.listdir(self.img_dir)):
            name = os.path.splitext(img_file)[0]
            img_path = os.path.join(self.img_dir, img_file)
            rank_path = self._find_file(self.fix_dir, name)
            mask_path = self._find_file(self.bigt_dir, name)

            if rank_path or mask_path:
                samples.append({
                    "name": name,
                    "image": img_path,
                    "rank_map": rank_path,
                    "binary_mask": mask_path,
                })
        return samples

    def _find_file(self, directory, name):
        if not os.path.isdir(directory):
            return None
        for ext in [".png", ".jpg", ".jpeg", ".bmp"]:
            path = os.path.join(directory, name + ext)
            if os.path.exists(path):
                return path
        return None

    def __len__(self):
        return len(self.samples)

    def __getitem__(self, idx):
        sample = self.samples[idx]

        # Load image
        image = cv2.imread(sample["image"])
        image = cv2.cvtColor(image, cv2.COLOR_BGR2RGB)
        image = cv2.resize(image, (self.image_size, self.image_size))

        # Load rank map
        has_rank = sample["rank_map"] is not None
        if has_rank:
            rank_map = cv2.imread(sample["rank_map"], cv2.IMREAD_GRAYSCALE)
            if rank_map is not None:
                rank_map = cv2.resize(rank_map, (self.image_size, self.image_size))
            else:
                has_rank = False
        if not has_rank:
            rank_map = np.zeros((self.image_size, self.image_size), dtype=np.uint8)

        # Load binary mask
        has_mask = sample["binary_mask"] is not None
        if has_mask:
            mask = cv2.imread(sample["binary_mask"], cv2.IMREAD_GRAYSCALE)
            if mask is not None:
                mask = cv2.resize(mask, (self.image_size, self.image_size),
                                  interpolation=cv2.INTER_NEAREST)
            else:
                has_mask = False
        if not has_mask:
            mask = np.zeros((self.image_size, self.image_size), dtype=np.uint8)

        # Augmentation
        if self.augment and np.random.random() < 0.3:
            image = np.fliplr(image).copy()
            rank_map = np.fliplr(rank_map).copy()
            mask = np.fliplr(mask).copy()

        # To tensors
        image_t = torch.from_numpy(image.transpose(2, 0, 1)).float() / 255.0
        rank_t = torch.from_numpy(rank_map).float().unsqueeze(0) / 255.0
        mask_t = torch.from_numpy((mask > 127).astype(np.float32)).unsqueeze(0)

        return {
            "name": sample["name"],
            "image": image_t,
            "rank_map": rank_t,
            "binary_mask": mask_t,
            "has_rank": has_rank,
            "has_mask": has_mask,
        }


# ============================================================================
# Trainer
# ============================================================================

class LoRATrainer:
    """Handles model loading, LoRA injection, training, and saving."""

    def __init__(self, config):
        self.config = config
        self.device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
        self.model = None
        self.lora_layers = None
        self.optimizer = None
        self.scheduler = None

    def setup_model(self, existing_lora_path=None):
        """Load base model, inject LoRA, setup optimizer."""
        print(f"[INFO] Device: {self.device}")
        print(f"[INFO] Loading base model: {self.config.model_path}")

        self.model = Generator(self.config.channel)

        if os.path.exists(self.config.model_path):
            state = torch.load(self.config.model_path, map_location="cpu")
            self.model.load_state_dict(state)
            print("[INFO] Base model weights loaded.")
        else:
            print(f"[ERROR] Model not found: {self.config.model_path}")
            raise FileNotFoundError(f"Model not found: {self.config.model_path}")

        # Freeze all base parameters
        for param in self.model.parameters():
            param.requires_grad = False

        # Inject LoRA into sal_dec (saliency/rank map decoder)
        print("[INFO] Injecting LoRA into sal_dec...")
        self.lora_layers = inject_lora_into_decoder(
            decoder=self.model.sal_encoder.sal_dec,
            rank=self.config.lora_rank,
            alpha=self.config.lora_alpha,
            dropout=self.config.lora_dropout,
        )

        lora_params, frozen_params = count_lora_parameters(self.lora_layers)
        print(f"[INFO] LoRA layers: {len(self.lora_layers)}")
        print(f"[INFO] Trainable params: {lora_params:,} ({lora_params/max(frozen_params,1)*100:.2f}% of decoder)")

        # Load existing LoRA if continuing from previous sessions
        if existing_lora_path and os.path.exists(existing_lora_path):
            print(f"[INFO] Continuing from: {existing_lora_path}")
            load_lora_weights(self.lora_layers, existing_lora_path)

        self.model.to(self.device)

        # Optimizer - only LoRA params
        self.optimizer = AdamW(
            get_lora_parameters(self.model),
            lr=self.config.learning_rate,
            weight_decay=self.config.weight_decay,
        )
        self.scheduler = CosineAnnealingLR(
            self.optimizer,
            T_max=self.config.num_epochs,
            eta_min=self.config.learning_rate * 0.01,
        )

    def train_on_session(self, dataset, session_id):
        """Fine-tune LoRA on one session's data. Returns metrics dict."""
        if len(dataset) == 0:
            print(f"[WARN] No samples for {session_id}. Skipping.")
            return {"status": "skipped", "reason": "no_samples"}

        loader = DataLoader(
            dataset,
            batch_size=min(self.config.batch_size, len(dataset)),
            shuffle=True,
            num_workers=self.config.num_workers,
            drop_last=False,
        )

        self.model.train()
        best_loss = float("inf")

        print(f"\n[TRAIN] Session: {session_id} | {len(dataset)} samples | {self.config.num_epochs} epochs")

        for epoch in range(self.config.num_epochs):
            epoch_loss = 0.0
            n_batches = 0

            for batch in loader:
                images = batch["image"].to(self.device)
                rank_targets = batch["rank_map"].to(self.device)
                mask_targets = batch["binary_mask"].to(self.device)
                has_rank = batch["has_rank"]
                has_mask = batch["has_mask"]

                self.optimizer.zero_grad()

                # Forward: Generator returns (fix_pred, init_pred, ref_pred)
                fix_pred, init_pred, ref_pred = self.model(images)

                loss = torch.tensor(0.0, device=self.device, requires_grad=True)

                # Rank map supervision (ref_pred from sal_dec -> edited rank map)
                if has_rank.any():
                    idx = has_rank.bool()
                    pred = torch.sigmoid(ref_pred[idx])
                    target = rank_targets[idx]
                    if pred.shape[-2:] != target.shape[-2:]:
                        pred = F.interpolate(pred, size=target.shape[-2:],
                                             mode="bilinear", align_corners=False)
                    loss = loss + self.config.rank_loss_weight * F.mse_loss(pred, target)

                # Binary mask supervision (fix_pred -> edited mask)
                if has_mask.any():
                    idx = has_mask.bool()
                    pred = torch.sigmoid(fix_pred[idx])
                    target = mask_targets[idx]
                    if pred.shape[-2:] != target.shape[-2:]:
                        pred = F.interpolate(pred, size=target.shape[-2:],
                                             mode="bilinear", align_corners=False)
                    loss = loss + self.config.mask_loss_weight * F.binary_cross_entropy(pred, target)

                # L2 regularization on LoRA params
                if self.config.reg_loss_weight > 0:
                    reg = sum(p.norm(2) for p in get_lora_parameters(self.model))
                    loss = loss + self.config.reg_loss_weight * reg

                loss.backward()
                torch.nn.utils.clip_grad_norm_(get_lora_parameters(self.model), 1.0)
                self.optimizer.step()

                epoch_loss += loss.item()
                n_batches += 1

            self.scheduler.step()
            avg = epoch_loss / max(n_batches, 1)
            if avg < best_loss:
                best_loss = avg

            if (epoch + 1) % 5 == 0 or epoch == 0:
                print(f"  Epoch {epoch+1:3d}/{self.config.num_epochs} | Loss: {avg:.6f} | LR: {self.optimizer.param_groups[0]['lr']:.2e}")

        return {
            "status": "completed",
            "session_id": session_id,
            "num_samples": len(dataset),
            "best_loss": best_loss,
            "final_loss": avg,
        }

    def save_lora(self, session_id, metrics=None):
        """Save LoRA weights as latest.pth and timestamped version."""
        os.makedirs(self.config.lora_output_dir, exist_ok=True)

        timestamp = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
        metadata = {
            "session_id": session_id,
            "timestamp": timestamp,
            "lora_rank": self.config.lora_rank,
            "lora_alpha": self.config.lora_alpha,
            "learning_rate": self.config.learning_rate,
            "num_epochs": self.config.num_epochs,
        }
        if metrics:
            metadata["metrics"] = {k: v for k, v in metrics.items()}

        # Timestamped backup
        ts_path = os.path.join(self.config.lora_output_dir, f"lora_{session_id}_{timestamp}.pth")
        save_lora_weights(self.lora_layers, ts_path, metadata)
        print(f"[INFO] Saved: {ts_path}")

        # Latest (loaded at next session)
        latest_path = os.path.join(self.config.lora_output_dir, "latest.pth")
        save_lora_weights(self.lora_layers, latest_path, metadata)
        print(f"[INFO] Updated: {latest_path}")

        return ts_path


# ============================================================================
# Queue helpers
# ============================================================================

def mark_session_processed(queue_file, session_id):
    """Remove session from queue, add to processed log."""
    if os.path.exists(queue_file):
        with open(queue_file, "r") as f:
            lines = f.readlines()
        with open(queue_file, "w") as f:
            for line in lines:
                if session_id not in line:
                    f.write(line)

    processed_file = queue_file.replace("retrain_queue.txt", "processed_sessions.txt")
    timestamp = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    with open(processed_file, "a") as f:
        f.write(f"{session_id}|{timestamp}|completed\n")


def find_session_dir(sessions_dir, session_id):
    """Find the session directory, trying exact match then partial."""
    direct = os.path.join(sessions_dir, session_id)
    if os.path.isdir(direct):
        return direct
    for entry in os.listdir(sessions_dir):
        full = os.path.join(sessions_dir, entry)
        if os.path.isdir(full) and session_id in entry:
            return full
    return None


# ============================================================================
# Main
# ============================================================================

def main():
    parser = argparse.ArgumentParser(description="MICA LoRA Retraining (session-end)")
    parser.add_argument("--session", type=str, required=True,
                        help="Session ID to retrain on (e.g. session_20260224_143000)")
    parser.add_argument("--epochs", type=int, default=None, help="Override epoch count")
    parser.add_argument("--rank", type=int, default=None, help="Override LoRA rank")
    parser.add_argument("--lr", type=float, default=None, help="Override learning rate")
    parser.add_argument("--dry-run", action="store_true", help="Validate data only")
    args = parser.parse_args()

    config = RetrainConfig()
    if args.epochs:
        config.num_epochs = args.epochs
    if args.rank:
        config.lora_rank = args.rank
        config.lora_alpha = float(args.rank)
    if args.lr:
        config.learning_rate = args.lr

    session_id = args.session

    # Find session data
    session_dir = find_session_dir(config.sessions_dir, session_id)
    if session_dir is None:
        print(f"[ERROR] Session directory not found for: {session_id}")
        sys.exit(1)

    dataset = SessionDataset(
        session_dir=session_dir,
        image_size=config.image_size,
        augment=config.augment,
    )
    print(f"[INFO] Found {len(dataset)} training samples in {session_dir}")

    MIN_SAMPLES = 3  # Must have more than 2 edited images

    if len(dataset) < MIN_SAMPLES:
        print(f"[INFO] Only {len(dataset)} edited images (need {MIN_SAMPLES}+). Skipping retraining.")
        sys.exit(0)

    if args.dry_run:
        print("[DRY RUN] Data validated. Would train on this session.")
        sys.exit(0)

    # Setup and train
    trainer = LoRATrainer(config)

    # Continue from previous LoRA if exists
    latest_lora = os.path.join(config.lora_output_dir, "latest.pth")
    existing = latest_lora if os.path.exists(latest_lora) else None

    trainer.setup_model(existing_lora_path=existing)
    metrics = trainer.train_on_session(dataset, session_id)

    if metrics.get("status") == "completed":
        trainer.save_lora(session_id, metrics)
        mark_session_processed(config.queue_file, session_id)
        print(f"\n[DONE] Retraining complete. Best loss: {metrics['best_loss']:.6f}")
    else:
        print(f"\n[DONE] Status: {metrics.get('status')}")


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        print("\n[INFO] Interrupted.")
    except Exception:
        traceback.print_exc()
        sys.exit(1)