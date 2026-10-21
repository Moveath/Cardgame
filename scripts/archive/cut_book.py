"""Летопись Архива для игры: лист `screens/references/city/archive-book-sheet.png` (ChatGPT, 2026-10-18, на сплошной
мадженте: раскрытая книга с закладкой, сургучная печать с сердцем, вырванный лист) → `unity/.../Resources/UI/Hub/`
`archive-book.png`, `archive-seal.png`, `archive-page.png` — на прозрачном фоне, обрезанные по краям, в масштабе листа.

    python scripts/archive/cut_book.py

Прозрачность и цвет края — как у доски поручений (`scripts/six-house/cut_quests.py`). Пятна раскладываются по центру:
нижний левый угол — печать (с капельками сургуча), нижний правый — лист, всё остальное — книга с закладкой.
"""
from pathlib import Path

import cv2
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / 'screens/references/city/archive-book-sheet.png'
OUT = ROOT / 'unity/CardGameClient/Assets/Resources/UI/Hub'
PAD = 4

img = cv2.imdecode(np.fromfile(str(SRC), np.uint8), cv2.IMREAD_COLOR).astype(np.float32) / 255
h, w = img.shape[:2]
border = np.concatenate([img[:8].reshape(-1, 3), img[-8:].reshape(-1, 3), img[:, :8].reshape(-1, 3), img[:, -8:].reshape(-1, 3)])
magenta = np.median(border, axis=0)  # BGR
key = lambda c: np.minimum(c[..., 2], c[..., 0]) - c[..., 1]
alpha = np.clip(1 - key(img) / key(magenta), 0, 1)
count, labels, stats, centroids = cv2.connectedComponentsWithStats((alpha > .5).astype(np.uint8))


def owner(index):
    x, y = centroids[index]
    if y < h * .62: return 'archive-book'
    return 'archive-seal' if x < w * .5 else 'archive-page'


for name in ('archive-book', 'archive-seal', 'archive-page'):
    mine = [i for i in range(1, count) if stats[i, cv2.CC_STAT_AREA] >= 20 and owner(i) == name]
    part = alpha * cv2.dilate(np.isin(labels, mine).astype(np.uint8), np.ones((7, 7), np.uint8))
    part[part < .06] = 0
    safe = np.maximum(part, 1e-3)[..., None]
    fg = np.clip((img - (1 - part[..., None]) * magenta) / safe, 0, 1)
    ys, xs = np.nonzero(part > .02)
    y0, y1 = max(0, ys.min() - PAD), min(h, ys.max() + 1 + PAD)
    x0, x1 = max(0, xs.min() - PAD), min(w, xs.max() + 1 + PAD)
    out = (np.dstack([fg, part])[y0:y1, x0:x1] * 255 + .5).astype(np.uint8)
    cv2.imencode('.png', out)[1].tofile(str(OUT / (name + '.png')))
    print(name, 'size', x1 - x0, 'x', y1 - y0, '; box in sheet', x0, y0, x1, y1, '; pieces', len(mine))
