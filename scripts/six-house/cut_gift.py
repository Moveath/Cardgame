"""Подарок Шестёрки для игры: лист `screens/references/city/six-gift-sheet.png` (ChatGPT, на сплошной мадженте:
закрытая коробка, открытая, крышка) → `unity/.../Resources/UI/Hub/six-gift-closed.png`, `six-gift-open.png`,
`six-gift-lid.png` — на прозрачном фоне, обрезанные по краям, в масштабе листа (одна единица на всех трёх).

    python scripts/six-house/cut_gift.py

Прозрачность и «отмешивание» края — как у Шестёрки (`cut_six.py`): alpha = 1 − (min(R, B) − G) / (min(mR, mB) − mG),
цвет края f = (c − (1 − a)·m) / a. Три самых больших пятна слева направо — закрытая, открытая, крышка.
"""
from pathlib import Path

import cv2
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / 'screens/references/city/six-gift-sheet.png'
OUT = ROOT / 'unity/CardGameClient/Assets/Resources/UI/Hub'
NAMES = ('six-gift-closed', 'six-gift-open', 'six-gift-lid')
PAD = 4

img = cv2.imdecode(np.fromfile(str(SRC), np.uint8), cv2.IMREAD_COLOR).astype(np.float32) / 255
border = np.concatenate([img[:8].reshape(-1, 3), img[-8:].reshape(-1, 3), img[:, :8].reshape(-1, 3), img[:, -8:].reshape(-1, 3)])
magenta = np.median(border, axis=0)  # BGR
key = lambda c: np.minimum(c[..., 2], c[..., 0]) - c[..., 1]
alpha = np.clip(1 - key(img) / key(magenta), 0, 1)
count, labels, stats, _ = cv2.connectedComponentsWithStats((alpha > .5).astype(np.uint8))
biggest = 1 + np.argsort(stats[1:, cv2.CC_STAT_AREA])[::-1][:3]
biggest = sorted(biggest, key=lambda i: stats[i, cv2.CC_STAT_LEFT])
for name, index in zip(NAMES, biggest):
    part = alpha * cv2.dilate((labels == index).astype(np.uint8), np.ones((7, 7), np.uint8))
    part[part < .06] = 0
    safe = np.maximum(part, 1e-3)[..., None]
    fg = np.clip((img - (1 - part[..., None]) * magenta) / safe, 0, 1)
    ys, xs = np.nonzero(part > .02)
    y0, y1 = max(0, ys.min() - PAD), min(img.shape[0], ys.max() + 1 + PAD)
    x0, x1 = max(0, xs.min() - PAD), min(img.shape[1], xs.max() + 1 + PAD)
    out = (np.dstack([fg, part])[y0:y1, x0:x1] * 255 + .5).astype(np.uint8)
    cv2.imencode('.png', out)[1].tofile(str(OUT / (name + '.png')))
    print(name, 'size', x1 - x0, 'x', y1 - y0, '; box in sheet', x0, y0, x1, y1)
