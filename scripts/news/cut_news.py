"""Газета «Карточного вестника» для игры (2026-10-09).

Разворот `screens/references/city/news-spread-sheet.png` (ChatGPT, на сплошной мадженте; одна страница
`news-paper-sheet.png` на экране была мелкой и «пустой» — автор попросил две) → `Resources/UI/Hub/news-paper.png`
на прозрачном фоне, обрезанный по краю. Прозрачность и цвет кромки — как у подарка (`scripts/six-house/cut_gift.py`).
Киоск сюда больше не копируется: его игровую картинку пишет `scripts/six-house/widen_room.py kiosk`.

    python scripts/news/cut_news.py
"""
from pathlib import Path

import cv2
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
CITY = ROOT / 'screens/references/city'
OUT = ROOT / 'unity/CardGameClient/Assets/Resources/UI/Hub'
PAD = 4

img = cv2.imdecode(np.fromfile(str(CITY / 'news-spread-sheet.png'), np.uint8), cv2.IMREAD_COLOR).astype(np.float32) / 255
border = np.concatenate([img[:8].reshape(-1, 3), img[-8:].reshape(-1, 3), img[:, :8].reshape(-1, 3), img[:, -8:].reshape(-1, 3)])
magenta = np.median(border, axis=0)
key = lambda c: np.minimum(c[..., 2], c[..., 0]) - c[..., 1]
alpha = np.clip(1 - key(img) / key(magenta), 0, 1)
count, labels, stats, _ = cv2.connectedComponentsWithStats((alpha > .5).astype(np.uint8))
paper = 1 + int(np.argmax(stats[1:, cv2.CC_STAT_AREA]))
part = alpha * cv2.dilate((labels == paper).astype(np.uint8), np.ones((7, 7), np.uint8))
part[part < .06] = 0
fg = np.clip((img - (1 - part[..., None]) * magenta) / np.maximum(part, 1e-3)[..., None], 0, 1)
ys, xs = np.nonzero(part > .02)
y0, y1, x0, x1 = max(0, ys.min() - PAD), ys.max() + 1 + PAD, max(0, xs.min() - PAD), xs.max() + 1 + PAD
cv2.imencode('.png', (np.dstack([fg, part])[y0:y1, x0:x1] * 255 + .5).astype(np.uint8))[1].tofile(str(OUT / 'news-paper.png'))
print('news-paper', x1 - x0, 'x', y1 - y0, '; box', x0, y0, x1, y1)
