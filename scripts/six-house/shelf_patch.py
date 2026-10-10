"""Открытый подарок на полке Шестёрки — заплатка той же комнаты (2026-10-07).

Спрайты коробки на полке не сходились с ракурсом комнаты, поэтому открытая коробка (и крышка рядом) — правка куска
самой комнаты: `screens/references/city/canvases/six-gift-shelf-crop.png` (600×400 из `six-room-v2-wide.png`, левый
верхний угол — CROP) → ChatGPT открывает коробку → `six-gift-shelf-open.png` рядом. Скрипт приводит ответ к 600×400,
выравнивает его по исходному куску (ECC, аффинно, без области полки с коробкой — генератор слегка сдвигает
и масштабирует), подгоняет цвет и берёт из участка PATCH только то, что генератор поменял (коробка, крышка, свет):
прозрачность — по разнице с комнатой, расширенной и размытой, к краям участка — ноль. Остальное в игре — сама комната,
пиксель в пиксель. → `Resources/UI/Hub/six-gift-shelf-open.png`; в игре ложится левым верхним углом в
(GiftPatchX, GiftPatchY) = PATCH[:2] (PlayerHubSixGift.cs).

    python scripts/six-house/shelf_patch.py [ответ генератора] [куда сохранить]
"""
import sys
from pathlib import Path

import cv2
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
ROOM = ROOT / 'screens/references/city/six-room-v2-wide.png'
EDITED = ROOT / 'screens/references/city/canvases/six-gift-shelf-open.png'
OUT = ROOT / 'unity/CardGameClient/Assets/Resources/UI/Hub/six-gift-shelf-open.png'
CROP = (1480, 0, 600, 400)    # x, y, ширина, высота куска в картинке комнаты
PATCH = (1600, 0, 270, 210)   # где может быть правка: коробка, крышка рядом, свет над ними
CHANGED = (16, 40)            # разница (из 255), с которой пиксель берётся из правки: от — до полностью
CORE, NEAR = 80, 16           # правка — пятна сильной разницы не меньше CORE px; мягкая маска — только в NEAR px от них
GROW, SOFT, FEATHER = 6, 9, 12  # расширение и размытие маски, мягкий край участка, px


def read(path):
    return cv2.imdecode(np.fromfile(str(path), np.uint8), cv2.IMREAD_COLOR)


def main():
    edited_path = Path(sys.argv[1]) if len(sys.argv) > 1 else EDITED
    out_path = Path(sys.argv[2]) if len(sys.argv) > 2 else OUT
    cx, cy, cw, ch = CROP
    px, py, pw, ph = PATCH
    original = read(ROOM)[cy:cy + ch, cx:cx + cw]
    edited = cv2.resize(read(edited_path), (cw, ch), interpolation=cv2.INTER_AREA)
    # Выравнивание и цвет — по всему, кроме участка правки.
    outside = np.full((ch, cw), 255, np.uint8)
    outside[py - cy:py - cy + ph, px - cx:px - cx + pw] = 0
    warp = np.eye(2, 3, dtype=np.float32)
    gray = lambda image: cv2.GaussianBlur(cv2.cvtColor(image, cv2.COLOR_BGR2GRAY), (5, 5), 0).astype(np.float32)
    criteria = (cv2.TERM_CRITERIA_EPS | cv2.TERM_CRITERIA_COUNT, 200, 1e-6)
    _, warp = cv2.findTransformECC(gray(original), gray(edited), warp, cv2.MOTION_AFFINE, criteria, outside, 5)
    aligned = cv2.warpAffine(edited, warp, (cw, ch), flags=cv2.INTER_LINEAR | cv2.WARP_INVERSE_MAP, borderMode=cv2.BORDER_REFLECT)
    keep = outside > 0
    gain = (original[keep].mean(0) + 1) / (aligned[keep].mean(0) + 1)
    aligned = np.clip(aligned * gain, 0, 255)
    rest = np.abs(aligned[keep] - original[keep].astype(np.float64)).mean()
    patch = aligned[py - cy:py - cy + ph, px - cx:px - cx + pw]
    base = original[py - cy:py - cy + ph, px - cx:px - cx + pw].astype(np.float64)
    # Что поменялось: сильная разница крупными пятнами (коробка, крышка) и мягкая — рядом с ними (свет); мелкий шум
    # пересказа генератора по краям плюща и полки — мимо. Потом расширена и размыта, к краям участка — ноль.
    difference = cv2.GaussianBlur(np.abs(patch - base).max(axis=2).astype(np.float32), (5, 5), 0)
    count, labels, stats, _ = cv2.connectedComponentsWithStats((difference >= CHANGED[1]).astype(np.uint8))
    core = np.isin(labels, [i for i in range(1, count) if stats[i, cv2.CC_STAT_AREA] >= CORE]).astype(np.uint8)
    near = cv2.dilate(core, np.ones((2 * NEAR + 1, 2 * NEAR + 1), np.uint8)).astype(np.float32)
    alpha = np.clip((difference - CHANGED[0]) / (CHANGED[1] - CHANGED[0]), 0, 1) * near
    alpha = cv2.dilate(alpha, np.ones((2 * GROW + 1, 2 * GROW + 1), np.uint8))
    alpha = cv2.GaussianBlur(alpha, (2 * SOFT + 1, 2 * SOFT + 1), 0)
    ramp = lambda n: np.clip(np.minimum(np.arange(n), np.arange(n)[::-1]) / FEATHER, 0, 1)
    alpha *= np.minimum.outer(ramp(ph), ramp(pw))
    rgba = np.dstack([patch, alpha * 255]).round().astype(np.uint8)
    cv2.imencode('.png', rgba)[1].tofile(str(out_path))
    print('warp', np.round(warp, 4).tolist(), '; colour gain', np.round(gain, 3).tolist(),
          '; mean difference outside', round(float(rest), 2), '; changed share of the patch', round(float(alpha.mean()), 3),
          '->', out_path.name)


if __name__ == '__main__':
    main()
