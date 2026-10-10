"""Стол Тузяры крупным планом (2026-10-08): на `screens/references/city/bank-desk-v1.png` генератор забыл Тузяре усы.
Кусок лица `canvases/bank-desk-face-crop.png` (480×480 от (600, 150)) правлен отдельно → `canvases/bank-desk-face-edit.png`
(генератор при этом приблизил кадр). Скрипт находит, где кусок лежит в ответе (ORB-точки → подобие, затем уточнение
ECC), приводит ответ к куску, подгоняет цвет и вклеивает только то, что поменялось (прозрачность по разнице, мягкий
край), → `bank-desk-v2.png` и игровая `Resources/UI/Hub/bank-desk.png`.

    python scripts/bank/desk_face_patch.py [desk | kiosk]

Киоск новостей (2026-10-09) — так же: `news-kiosk-v1.png`, лицо Двойки (хмурое, «как гопник») правлено отдельно,
кусок `canvases/news-kiosk-face-crop.png` (480×480 от (560, 220)) → `canvases/news-kiosk-face-edit-v2.png`,
итог — `news-kiosk-v2.png` (в игру пока не ставится).
"""
import sys
from pathlib import Path

import cv2
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
CITY = ROOT / 'screens/references/city'
JOBS = {
    'desk': (CITY / 'bank-desk-v1.png', CITY / 'canvases/bank-desk-face-edit.png',
             (CITY / 'bank-desk-v2.png', ROOT / 'unity/CardGameClient/Assets/Resources/UI/Hub/bank-desk.png'), (600, 150)),
    'kiosk': (CITY / 'news-kiosk-v1.png', CITY / 'canvases/news-kiosk-face-edit-v2.png', (CITY / 'news-kiosk-v2.png',), (560, 220)),
}
DESK, EDIT, OUTS, (X, Y) = JOBS[sys.argv[1] if len(sys.argv) > 1 else 'desk']
W, H = 480, 480
CHANGED, GROW, SOFT, FEATHER = (12, 34), 6, 9, 30


def read(path):
    return cv2.imdecode(np.fromfile(str(path), np.uint8), cv2.IMREAD_COLOR)


desk = read(DESK)
original = desk[Y:Y + H, X:X + W].copy()
edited = read(EDIT)
gray = lambda image: cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
orb = cv2.ORB_create(4000)
ka, da = orb.detectAndCompute(gray(original), None)
kb, db = orb.detectAndCompute(gray(edited), None)
matches = sorted(cv2.BFMatcher(cv2.NORM_HAMMING, crossCheck=True).match(da, db), key=lambda m: m.distance)[:400]
src = np.float32([ka[m.queryIdx].pt for m in matches])
dst = np.float32([kb[m.trainIdx].pt for m in matches])
# Куда пиксель куска попадает в ответе.
guess, inliers = cv2.estimateAffinePartial2D(src, dst, method=cv2.RANSAC, ransacReprojThreshold=4)
aligned = cv2.warpAffine(edited, guess, (W, H), flags=cv2.INTER_AREA | cv2.WARP_INVERSE_MAP, borderMode=cv2.BORDER_REFLECT)
blur = lambda image: cv2.GaussianBlur(gray(image), (5, 5), 0).astype(np.float32)
warp = np.eye(2, 3, dtype=np.float32)
_, warp = cv2.findTransformECC(blur(original), blur(aligned), warp, cv2.MOTION_AFFINE,
                               (cv2.TERM_CRITERIA_EPS | cv2.TERM_CRITERIA_COUNT, 200, 1e-6), None, 5)
aligned = cv2.warpAffine(aligned, warp, (W, H), flags=cv2.INTER_LINEAR | cv2.WARP_INVERSE_MAP,
                         borderMode=cv2.BORDER_REFLECT).astype(np.float64)
gain = (original.reshape(-1, 3).mean(0) + 1) / (aligned.reshape(-1, 3).mean(0) + 1)
aligned = np.clip(aligned * gain, 0, 255)
difference = cv2.GaussianBlur(np.abs(aligned - original).max(axis=2).astype(np.float32), (7, 7), 0)
alpha = np.clip((difference - CHANGED[0]) / (CHANGED[1] - CHANGED[0]), 0, 1)
alpha = cv2.dilate(alpha, np.ones((2 * GROW + 1, 2 * GROW + 1), np.uint8))
alpha = cv2.GaussianBlur(alpha, (2 * SOFT + 1, 2 * SOFT + 1), 0)
ramp = lambda n: np.clip(np.minimum(np.arange(n), np.arange(n)[::-1]) / FEATHER, 0, 1)
alpha = (alpha * np.minimum.outer(ramp(H), ramp(W)))[..., None]
desk[Y:Y + H, X:X + W] = np.clip(original * (1 - alpha) + aligned * alpha, 0, 255).round().astype(np.uint8)
for out in OUTS:
    cv2.imencode('.png', desk)[1].tofile(str(out))
print('scale', round(float(np.hypot(*guess[0, :2])), 3), 'inliers', int(inliers.sum()), 'ecc', np.round(warp, 3).tolist(),
      'changed', round(float(alpha.mean()), 3))
