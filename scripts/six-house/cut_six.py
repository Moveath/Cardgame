"""Шестёрка пик для игры: лист `screens/references/characters/six-of-spades-guide.png` (на сплошной мадженте) →
`unity/.../Resources/UI/Hub/six-guide.png` — фигура на прозрачном фоне, обрезанная по краям, высотой HEIGHT.

    python scripts/six-house/cut_six.py

Прозрачность — по мадженте фона m (меряется по краям листа, около (248, 5, 248)): у неё min(R, B) − G почти
во всю яркость, у любой краски фигуры (чёрный контур, белые перчатки, кремовая карта, серый шарф) — около нуля,
а на краю — в долю смешения: alpha = 1 − (min(R, B) − G) / (min(mR, mB) − mG). Цвет края «отмешивается» от
маджента: f = (c − (1 − a)·m) / a — ни розовой каймы, ни тёмного ореола.
"""
from pathlib import Path

import cv2
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / 'screens/references/characters/six-of-spades-guide.png'
OUT = ROOT / 'unity/CardGameClient/Assets/Resources/UI/Hub/six-guide.png'
HEIGHT = 900

img = cv2.imdecode(np.fromfile(str(SRC), np.uint8), cv2.IMREAD_COLOR).astype(np.float32) / 255
border = np.concatenate([img[:8].reshape(-1, 3), img[-8:].reshape(-1, 3), img[:, :8].reshape(-1, 3), img[:, -8:].reshape(-1, 3)])
magenta = np.median(border, axis=0)  # BGR
key = lambda c: np.minimum(c[..., 2], c[..., 0]) - c[..., 1]
alpha = np.clip(1 - key(img) / key(magenta), 0, 1)
# Только сама фигура: самое большое сплошное пятно с узким запасом под мягкий край (шум сжатия по фону — прочь).
count, labels, stats, _ = cv2.connectedComponentsWithStats((alpha > .5).astype(np.uint8))
figure = (labels == 1 + int(np.argmax(stats[1:, cv2.CC_STAT_AREA]))).astype(np.uint8)
alpha *= cv2.dilate(figure, np.ones((7, 7), np.uint8))
alpha[alpha < .06] = 0
safe = np.maximum(alpha, 1e-3)[..., None]
fg = np.clip((img - (1 - alpha[..., None]) * magenta) / safe, 0, 1)
ys, xs = np.nonzero(alpha > .02)
pad = 6
y0, y1 = max(0, ys.min() - pad), min(img.shape[0], ys.max() + 1 + pad)
x0, x1 = max(0, xs.min() - pad), min(img.shape[1], xs.max() + 1 + pad)
rgba = np.dstack([fg, alpha])[y0:y1, x0:x1]
w = int(round(rgba.shape[1] * HEIGHT / rgba.shape[0]))
# Уменьшаем в предумноженном виде, чтобы край не темнел и не розовел.
pre = rgba[..., :3] * rgba[..., 3:]
pre = cv2.resize(pre, (w, HEIGHT), interpolation=cv2.INTER_AREA)
a = cv2.resize(rgba[..., 3], (w, HEIGHT), interpolation=cv2.INTER_AREA)
col = np.where(a[..., None] > 1e-3, pre / np.maximum(a, 1e-3)[..., None], 0)
out = (np.dstack([np.clip(col, 0, 1), np.clip(a, 0, 1)]) * 255 + .5).astype(np.uint8)
cv2.imencode('.png', out)[1].tofile(str(OUT))
print('six-guide', out.shape[1], 'x', out.shape[0], '; figure box in sheet', x0, y0, x1, y1)
