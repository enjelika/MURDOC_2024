"""
lora_inference.py - Runtime LoRA Adapter Loading for MICA

Loads LoRA adapters into the RankNet model at inference time so that
session-trained improvements carry forward to subsequent sessions.

Integration point in IAI_Decision_Hierarchy.py _load_cods_model():
    from lora_inference import apply_lora_to_model
    cods = apply_lora_to_model(cods)

Author: Debra Hogue - MURDOC/MICA Project
"""

import os
import math
import torch
import torch.nn as nn
from typing import Dict, Optional


# ============================================================================
# LoRA Conv2d Layer (self-contained)
# ============================================================================

class LoRAConv2d(nn.Module):
    """LoRA adapter wrapping a frozen Conv2d."""

    def __init__(self, original_conv, rank=4, alpha=4.0):
        """Wrap a frozen Conv2d with trainable low-rank A and B adapter convolutions.

        Parameters
        ----------
        original_conv : nn.Conv2d
            The pre-trained convolution to freeze and augment.
        rank : int
            Inner dimension of the LoRA factorization (default 4).
        alpha : float
            Scaling factor; effective weight = alpha/rank * lora_B(lora_A(x)).
        """
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

        nn.init.kaiming_uniform_(self.lora_A.weight, a=math.sqrt(5))
        nn.init.zeros_(self.lora_B.weight)

    def forward(self, x):
        """Compute frozen conv output plus scaled LoRA residual."""
        return self.original_conv(x) + self.lora_B(self.lora_A(x)) * self.scaling


# ============================================================================
# Internal helpers
# ============================================================================

def _replace_module(parent, name, new_module):
    """Traverse a dotted attribute path on `parent` and replace the leaf with `new_module`."""
    parts = name.split(".")
    current = parent
    for part in parts[:-1]:
        current = getattr(current, part)
    setattr(current, parts[-1], new_module)


def _inject_lora(decoder, rank=4, alpha=4.0):
    """Replace every Conv2d in `decoder` with a LoRAConv2d and return the mapping of name → layer."""
    lora_layers = {}
    for name, module in list(decoder.named_modules()):
        if isinstance(module, nn.Conv2d):
            lora_conv = LoRAConv2d(module, rank=rank, alpha=alpha)
            _replace_module(decoder, name, lora_conv)
            lora_layers[name] = lora_conv
    return lora_layers


def _load_lora_state(lora_layers, path):
    """Copy saved lora_A/lora_B weights from a checkpoint into the injected LoRA layers.

    Returns (number_of_layers_loaded, metadata_dict).
    """
    checkpoint = torch.load(path, map_location="cpu")
    state = checkpoint.get("lora_state_dict", {})
    loaded = 0
    for name, lora_module in lora_layers.items():
        a_key = f"{name}.lora_A.weight"
        b_key = f"{name}.lora_B.weight"
        if a_key in state and b_key in state:
            lora_module.lora_A.weight.data.copy_(state[a_key])
            lora_module.lora_B.weight.data.copy_(state[b_key])
            loaded += 1
    return loaded, checkpoint.get("metadata", {})


# ============================================================================
# Public API
# ============================================================================

def apply_lora_to_model(model, lora_path=None, rank=4, alpha=4.0):
    """
    Apply LoRA adapters to a loaded Generator model.

    Call after loading base weights, before cods.eval().
    If no LoRA file exists, returns the model unchanged.

    Parameters
    ----------
    model : Generator
        The loaded RankNet Generator model.
    lora_path : str, optional
        Path to LoRA .pth file. Defaults to training_sessions/lora_adapters/latest.pth
    rank : int
        Must match the rank used during training (default 4).
    alpha : float
        Must match alpha used during training (default 4.0).

    Returns
    -------
    model : Generator
        Model with LoRA adapters applied (or unchanged if no file found).
    """
    if lora_path is None:
        lora_path = os.path.join("training_sessions", "lora_adapters", "latest.pth")

    if not os.path.exists(lora_path):
        print(f"[INFO] No LoRA adapters at {lora_path}. Using base model.")
        return model

    try:
        print(f"[INFO] Loading LoRA adapters from: {lora_path}")

        lora_layers = _inject_lora(model.sal_encoder.sal_dec, rank=rank, alpha=alpha)
        loaded, metadata = _load_lora_state(lora_layers, lora_path)

        total_params = sum(
            p.numel() for m in lora_layers.values()
            for p in list(m.lora_A.parameters()) + list(m.lora_B.parameters())
        )
        session = metadata.get("session_id", "unknown")
        print(f"[INFO] LoRA applied: {loaded} layers, {total_params:,} params (session: {session})")

    except Exception as e:
        print(f"[WARN] Failed to load LoRA adapters: {e}")
        print("[WARN] Continuing with base model.")

    return model