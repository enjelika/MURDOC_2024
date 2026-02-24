"""
lora_modules.py — Lightweight LoRA (Low-Rank Adaptation) for Conv2d layers.

Designed for integration with MICA's RankNet (Saliency_feat_decoder) architecture.
Applies low-rank updates to Conv2d layers to enable session-based model refinement
using expert-edited rank maps as supervisory signals.

Reference: Hu et al., "LoRA: Low-Rank Adaptation of Large Language Models" (2021)
Adapted for convolutional layers per the approach in FedPara / LoRA-Conv.

Author: Debra Hogue — MURDOC/MICA Project
"""

import torch
import torch.nn as nn
import torch.nn.functional as F
import math
from typing import Dict, List, Optional, Tuple


class LoRAConv2d(nn.Module):
    """
    LoRA adapter for Conv2d layers.

    Wraps an existing (frozen) Conv2d and adds a trainable low-rank
    decomposition:  ΔW = A @ B  (reshaped for conv kernels).

    The forward pass computes:
        out = original_conv(x) + (B_conv ∘ A_conv)(x) * scaling

    where:
        A_conv: Conv2d(in_ch, rank, kernel_size, ...)   — projects down
        B_conv: Conv2d(rank, out_ch, 1, ...)            — projects back up
        scaling = alpha / rank

    Parameters
    ----------
    original_conv : nn.Conv2d
        The frozen base convolution layer.
    rank : int
        Rank of the low-rank decomposition. Lower = fewer params, less capacity.
    alpha : float
        Scaling factor. Controls the magnitude of the LoRA update.
    dropout : float
        Dropout applied to the LoRA path (0 = disabled).
    """

    def __init__(
        self,
        original_conv: nn.Conv2d,
        rank: int = 4,
        alpha: float = 1.0,
        dropout: float = 0.0,
    ):
        super().__init__()

        self.original_conv = original_conv
        self.rank = rank
        self.alpha = alpha
        self.scaling = alpha / rank

        in_channels = original_conv.in_channels
        out_channels = original_conv.out_channels
        kernel_size = original_conv.kernel_size
        stride = original_conv.stride
        padding = original_conv.padding
        dilation = original_conv.dilation
        groups = original_conv.groups

        # Freeze original weights
        for param in self.original_conv.parameters():
            param.requires_grad = False

        # LoRA down-projection: in_channels -> rank (same spatial kernel)
        self.lora_A = nn.Conv2d(
            in_channels,
            rank,
            kernel_size=kernel_size,
            stride=stride,
            padding=padding,
            dilation=dilation,
            groups=groups,
            bias=False,
        )

        # LoRA up-projection: rank -> out_channels (1x1 pointwise)
        self.lora_B = nn.Conv2d(
            rank,
            out_channels,
            kernel_size=1,
            stride=1,
            padding=0,
            bias=False,
        )

        # Dropout on the LoRA path
        self.lora_dropout = nn.Dropout(dropout) if dropout > 0 else nn.Identity()

        # Initialize: A with Kaiming, B with zeros → ΔW starts at 0
        nn.init.kaiming_uniform_(self.lora_A.weight, a=math.sqrt(5))
        nn.init.zeros_(self.lora_B.weight)

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        # Base forward (frozen)
        base_out = self.original_conv(x)

        # LoRA path
        lora_out = self.lora_B(self.lora_A(self.lora_dropout(x)))

        return base_out + lora_out * self.scaling

    def merge_weights(self) -> None:
        """
        Merge LoRA weights into the original conv for inference efficiency.
        After merging, the layer behaves identically but without the extra
        forward pass through A and B.
        """
        with torch.no_grad():
            # Compute effective ΔW by running a unit impulse through A then B
            # For simplicity, we merge by adding the outer product to the weight
            # This works cleanly for 1x1 B projections
            A_weight = self.lora_A.weight.data  # [rank, in_ch, kH, kW]
            B_weight = self.lora_B.weight.data  # [out_ch, rank, 1, 1]

            # Compute ΔW = B @ A (in the conv weight space)
            # B is [out_ch, rank, 1, 1], A is [rank, in_ch, kH, kW]
            # Result should be [out_ch, in_ch, kH, kW]
            delta_w = torch.einsum("orij,rikl->oikl", B_weight, A_weight)

            self.original_conv.weight.data += delta_w * self.scaling

    def get_lora_state_dict(self) -> Dict[str, torch.Tensor]:
        """Return only the LoRA parameters (A and B weights)."""
        return {
            "lora_A.weight": self.lora_A.weight.data.clone(),
            "lora_B.weight": self.lora_B.weight.data.clone(),
        }


def inject_lora_into_decoder(
    decoder: nn.Module,
    rank: int = 4,
    alpha: float = 1.0,
    dropout: float = 0.0,
    target_layer_names: Optional[List[str]] = None,
) -> Dict[str, LoRAConv2d]:
    """
    Inject LoRA adapters into Conv2d layers of a decoder module.

    Replaces targeted Conv2d layers with LoRAConv2d wrappers, freezing the
    original weights and making only the LoRA parameters trainable.

    Parameters
    ----------
    decoder : nn.Module
        The decoder module (e.g., Saliency_feat_decoder).
    rank : int
        LoRA rank for all injected layers.
    alpha : float
        LoRA scaling factor.
    dropout : float
        Dropout rate on LoRA path.
    target_layer_names : list of str, optional
        If provided, only layers whose names contain one of these strings
        will be wrapped. If None, ALL Conv2d layers in the decoder are wrapped.

    Returns
    -------
    dict
        Mapping from layer name → LoRAConv2d module (for later extraction).
    """
    lora_layers = {}

    for name, module in list(decoder.named_modules()):
        if not isinstance(module, nn.Conv2d):
            continue

        # Skip if target filtering is active and this layer doesn't match
        if target_layer_names is not None:
            if not any(t in name for t in target_layer_names):
                continue

        # Create LoRA wrapper
        lora_conv = LoRAConv2d(
            original_conv=module,
            rank=rank,
            alpha=alpha,
            dropout=dropout,
        )

        # Replace the module in the parent
        _replace_module(decoder, name, lora_conv)
        lora_layers[name] = lora_conv

    return lora_layers


def _replace_module(parent: nn.Module, name: str, new_module: nn.Module):
    """Replace a nested submodule by its dotted name path."""
    parts = name.split(".")
    current = parent
    for part in parts[:-1]:
        current = getattr(current, part)
    setattr(current, parts[-1], new_module)


def get_lora_parameters(model: nn.Module) -> List[nn.Parameter]:
    """Collect all trainable LoRA parameters from a model."""
    params = []
    for module in model.modules():
        if isinstance(module, LoRAConv2d):
            params.extend(module.lora_A.parameters())
            params.extend(module.lora_B.parameters())
    return params


def save_lora_weights(
    lora_layers: Dict[str, LoRAConv2d],
    path: str,
    metadata: Optional[dict] = None,
) -> None:
    """
    Save LoRA adapter weights to a file.

    Parameters
    ----------
    lora_layers : dict
        Mapping from layer name → LoRAConv2d module.
    path : str
        Output .pth file path.
    metadata : dict, optional
        Extra metadata to store (e.g., session ID, training config).
    """
    state = {}
    for name, lora_module in lora_layers.items():
        lora_sd = lora_module.get_lora_state_dict()
        for k, v in lora_sd.items():
            state[f"{name}.{k}"] = v

    save_dict = {"lora_state_dict": state}
    if metadata:
        save_dict["metadata"] = metadata

    torch.save(save_dict, path)


def load_lora_weights(
    lora_layers: Dict[str, LoRAConv2d],
    path: str,
) -> dict:
    """
    Load LoRA adapter weights from a file.

    Returns the metadata dict if present.
    """
    checkpoint = torch.load(path, map_location="cpu")
    state = checkpoint["lora_state_dict"]

    for name, lora_module in lora_layers.items():
        a_key = f"{name}.lora_A.weight"
        b_key = f"{name}.lora_B.weight"
        if a_key in state and b_key in state:
            lora_module.lora_A.weight.data.copy_(state[a_key])
            lora_module.lora_B.weight.data.copy_(state[b_key])

    return checkpoint.get("metadata", {})


def merge_all_lora(lora_layers: Dict[str, LoRAConv2d]) -> None:
    """Merge all LoRA weights into their base convolutions (for deployment)."""
    for lora_module in lora_layers.values():
        lora_module.merge_weights()


def count_lora_parameters(lora_layers: Dict[str, LoRAConv2d]) -> Tuple[int, int]:
    """
    Count trainable LoRA params vs total model params.

    Returns (lora_params, total_frozen_params).
    """
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