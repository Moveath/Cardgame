"""Зал банка Тузяры (2026-10-08): `screens/references/city/bank-hall-v3.png` (ChatGPT) удачен целиком, кроме Тузяры
(лишний нос, кривая монета в руке). Кусок вокруг него `canvases/bank-boss-crop.png` (480×480 от (600, 180)) правлен
отдельно → `canvases/bank-boss-edit.png`. Скрипт приводит ответ к размеру куска, выравнивает (ECC, аффинно), подгоняет
цвет и вклеивает только то, что поменялось (прозрачность по разнице, мягкий край), → `bank-hall-v4.png` и игровая
картинка `Resources/UI/Hub/bank-hall.png`.

    python scripts/bank/boss_patch.py
"""
from pathlib import Path

import cv2
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
CITY = ROOT / 'screens/references/city'
HALL, EDIT = CITY / 'bank-hall-v3.png', CITY / 'canvases/bank-boss-edit.png'
OUTS = (CITY / 'bank-hall-v4.png', ROOT / 'unity/CardGameClient/Assets/Resources/UI/Hub/bank-hall.png')
X, Y, W, H = 600, 180, 480, 480
CHANGED, GROW, SOFT, FEATHER = (10, 30), 8, 11, 24


def read(path):
    return cv2.imdecode(np.fromfile(str(path), np.uint8), cv2.IMREAD_COLOR)


hall = read(HALL)
original = hall[Y:Y + H, X:X + W].copy()
edited = cv2.resize(read(EDIT), (W, H), interpolation=cv2.INTER_AREA)
gray = lambda image: cv2.GaussianBlur(cv2.cvtColor(image, cv2.COLOR_BGR2GRAY), (5, 5), 0).astype(np.float32)
warp = np.eye(2, 3, dtype=np.float32)
_, warp = cv2.findTransformECC(gray(original), gray(edited), warp, cv2.MOTION_AFFINE,
                               (cv2.TERM_CRITERIA_EPS | cv2.TERM_CRITERIA_COUNT, 200, 1e-6), None, 5)
aligned = cv2.warpAffine(edited, warp, (W, H), flags=cv2.INTER_LINEAR | cv2.WARP_INVERSE_MAP,
                         borderMode=cv2.BORDER_REFLECT).astype(np.float64)
gain = (original.reshape(-1, 3).mean(0) + 1) / (aligned.reshape(-1, 3).mean(0) + 1)
aligned = np.clip(aligned * gain, 0, 255)
difference = cv2.GaussianBlur(np.abs(aligned - original).max(axis=2).astype(np.float32), (7, 7), 0)
alpha = np.clip((difference - CHANGED[0]) / (CHANGED[1] - CHANGED[0]), 0, 1)
alpha = cv2.dilate(alpha, np.ones((2 * GROW + 1, 2 * GROW + 1), np.uint8))
alpha = cv2.GaussianBlur(alpha, (2 * SOFT + 1, 2 * SOFT + 1), 0)
ramp = lambda n: np.clip(np.minimum(np.arange(n), np.arange(n)[::-1]) / FEATHER, 0, 1)
alpha = (alpha * np.minimum.outer(ramp(H), ramp(W)))[..., None]
hall[Y:Y + H, X:X + W] = np.clip(original * (1 - alpha) + aligned * alpha, 0, 255).round().astype(np.uint8)
for out in OUTS:
    cv2.imencode('.png', hall)[1].tofile(str(out))
print('warp', np.round(warp, 3).tolist(), 'gain', np.round(gain, 3).tolist(), 'changed', round(float(alpha.mean()), 3))
