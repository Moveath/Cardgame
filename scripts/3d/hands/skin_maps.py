"""Маска кожи для шейдера CardCity/Skin: R — ногти (печёт bake.py), G — складки для карты шероховатости.
Складки берутся из карты кожи: bake.py затемняет в них цвет к (0,62; 0,46; 0,42), а покраснение на суставах красный канал не опускает,
поэтому по красному каналу складка отделяется от румянца. Запускать обычным Python после bake.py:
python scripts/3d/hands/skin_maps.py"""
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter

ROOT = Path(__file__).resolve().parents[3]
TEXTURES = ROOT / "art3d" / "hands" / "textures"
UNITY = ROOT / "unity" / "CardGameClient" / "Assets" / "Resources" / "ThreeD" / "Hands"
# Красный канал карты (sRGB, 0–255): чистая кожа и самая глубокая складка.
SKIN_RED, CREASE_RED = 247., 207.

for gender in ("Male", "Female"):
    base = np.asarray(Image.open(TEXTURES / f"T_Hand_{gender}_BaseColor.png").convert("RGB"), dtype=np.float32)
    mask = np.asarray(Image.open(TEXTURES / f"T_Hand_{gender}_Mask.png").convert("RGB"), dtype=np.uint8)
    crease = np.clip((SKIN_RED - base[:, :, 0]) / (SKIN_RED - CREASE_RED), 0, 1)
    # Складка чуть шире, чем на карте цвета: шершавая кожа видна и по краю морщины.
    crease = np.asarray(Image.fromarray((crease * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(3)).filter(ImageFilter.GaussianBlur(1.2)))
    out = np.zeros_like(mask)
    out[:, :, 0] = mask[:, :, 0]
    out[:, :, 1] = crease
    image = Image.fromarray(out)
    image.save(TEXTURES / f"T_Hand_{gender}_Mask.png")
    if gender == "Male":
        image.save(UNITY / f"T_Hand_{gender}_Mask.png")
    print(gender, "crease coverage %.3f" % (crease > 64).mean())
