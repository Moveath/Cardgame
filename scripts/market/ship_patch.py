"""Накладка «арка без корабля» для Рынка Короля бубен (2026-10-19).

Когда корабль Туза бубен ещё в пути (заказ плывёт), у причала в арке пусто: лунная вода, город вдали
и пустой причал. Накладка вырезается из ответа генератора `screens/references/city/canvases/market-arch-empty.png`,
выравнивается по куску зала `market-arch-crop.png` (620×620, CROP=(0, 30) в 16:9 или (262, 30) в 21:9),
подгоняется по цвету и сохраняется с альфа-каналом. В игре код кладёт накладку поверх арки зала,
пока заказ плывёт.

    py scripts/market/ship_patch.py [ответ генератора] [куда сохранить]
"""
import sys
from pathlib import Path

import cv2
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
CITY = ROOT / 'screens/references/city'
CANVASES = CITY / 'canvases'
CROP_IMAGE = CANVASES / 'market-arch-crop.png'
EDITED_IMAGE = CANVASES / 'market-arch-empty.png'
OUT_IMAGE = ROOT / 'unity/CardGameClient/Assets/Resources/UI/Hub/market-arch-empty.png'

CROP = (262, 30, 620, 620)  # x, y, ширина, высота куска в картинке 21:9 (market-hall-v2-wide.png)
CHANGED = (16, 38)          # порог разницы: от — до полной непрозрачности


def read(path):
    return cv2.imdecode(np.fromfile(str(path), np.uint8), cv2.IMREAD_COLOR)


def main():
    crop_path = CROP_IMAGE
    edited_path = Path(sys.argv[1]) if len(sys.argv) > 1 else EDITED_IMAGE
    out_path = Path(sys.argv[2]) if len(sys.argv) > 2 else OUT_IMAGE

    crop = read(crop_path)
    cw, ch = crop.shape[1], crop.shape[0]
    edited = cv2.resize(read(edited_path), (cw, ch), interpolation=cv2.INTER_AREA)

    # Выравнивание ECC по статичным элементам (арка, фонари, пол)
    mask = np.ones((ch, cw), dtype=np.uint8) * 255
    mask[70:530, 100:500] = 0

    gray_crop = cv2.cvtColor(crop, cv2.COLOR_BGR2GRAY)
    gray_edited = cv2.cvtColor(edited, cv2.COLOR_BGR2GRAY)

    warp = np.eye(2, 3, dtype=np.float32)
    criteria = (cv2.TERM_CRITERIA_EPS | cv2.TERM_CRITERIA_COUNT, 200, 1e-6)
    try:
        _, warp = cv2.findTransformECC(gray_crop, gray_edited, warp, cv2.MOTION_AFFINE, criteria, mask, 5)
        aligned = cv2.warpAffine(edited, warp, (cw, ch), flags=cv2.INTER_LINEAR | cv2.WARP_INVERSE_MAP, borderMode=cv2.BORDER_REFLECT)
    except Exception:
        aligned = edited

    keep = mask > 0
    gain = (crop[keep].mean(0) + 1) / (aligned[keep].mean(0) + 1)
    aligned = np.clip(aligned * gain, 0, 255)

    # Разница и мягкая маска внутри проёма арки
    diff = cv2.GaussianBlur(np.abs(aligned.astype(float) - crop.astype(float)).max(axis=2).astype(np.float32), (5, 5), 0)
    thresh = np.clip((diff - CHANGED[0]) / (CHANGED[1] - CHANGED[0]), 0, 1)

    y_indices, x_indices = np.indices((ch, cw))
    inside_arch = (x_indices >= 38) & (x_indices <= 512) & (y_indices >= 35) & (y_indices <= 560)
    thresh[~inside_arch] = 0

    thresh = cv2.dilate(thresh, np.ones((7, 7), np.uint8))
    thresh = cv2.GaussianBlur(thresh, (11, 11), 0)

    # Собираем RGBA картинку
    rgba = np.zeros((ch, cw, 4), dtype=np.uint8)
    rgba[:, :, :3] = aligned.astype(np.uint8)
    rgba[:, :, 3] = np.round(thresh * 255).astype(np.uint8)

    out_path.parent.mkdir(parents=True, exist_ok=True)
    cv2.imencode('.png', rgba)[1].tofile(str(out_path))
    print(f'Saved overlay patch: {out_path} ({cw}x{ch})')


if __name__ == '__main__':
    main()
