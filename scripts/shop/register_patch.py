"""Касса вместо банки помидоров на прилавке Лавки (2026-10-17; кидалки ушли на чёрный рынок).

Кусок прилавка `screens/references/city/canvases/shop-jar-crop.png` (400×400 из `shop-counter-v1.png`, левый верхний
угол — CROP) → ChatGPT ставит кассу вместо банки → `shop-jar-register.png` рядом. Скрипт приводит ответ к 400×400,
выравнивает по исходному куску (ECC, аффинно, без участка банки — генератор чуть сдвигает и масштабирует), подгоняет
цвет и берёт из участка PATCH только то, что поменялось (касса и место, где стояла банка): прозрачность — по разнице,
расширенной и размытой, мягкий край слева, сверху и снизу (справа — край картинки). Остальное — прилавок пиксель
в пиксель. → `shop-counter-v2.png`.

    python scripts/shop/register_patch.py [ответ генератора] [куда сохранить]
"""
import sys
from pathlib import Path

import cv2
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
CITY = ROOT / 'screens/references/city'
COUNTER = CITY / 'shop-counter-v1.png'
EDITED = CITY / 'canvases/shop-jar-register.png'
OUT = CITY / 'shop-counter-v2.png'
CROP = (1272, 420, 400, 400)   # x, y, ширина, высота куска в картинке прилавка
PATCH = (1422, 480, 250, 300)  # где может быть правка: банка с крышкой и тень под ней
CHANGED = (14, 36)             # разница (из 255), с которой пиксель берётся из правки: от — до полностью
GROW, SOFT, FEATHER = 8, 9, 14  # расширение и размытие маски, мягкий край участка, px


def read(path):
    return cv2.imdecode(np.fromfile(str(path), np.uint8), cv2.IMREAD_COLOR)


def main():
    edited_path = Path(sys.argv[1]) if len(sys.argv) > 1 else EDITED
    out_path = Path(sys.argv[2]) if len(sys.argv) > 2 else OUT
    cx, cy, cw, ch = CROP
    px, py, pw, ph = PATCH
    picture = read(COUNTER)
    original = picture[cy:cy + ch, cx:cx + cw]
    edited = cv2.resize(read(edited_path), (cw, ch), interpolation=cv2.INTER_AREA)
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
    difference = cv2.GaussianBlur(np.abs(patch - base).max(axis=2).astype(np.float32), (5, 5), 0)
    alpha = np.clip((difference - CHANGED[0]) / (CHANGED[1] - CHANGED[0]), 0, 1)
    alpha = cv2.dilate(alpha, np.ones((2 * GROW + 1, 2 * GROW + 1), np.uint8))
    alpha = cv2.GaussianBlur(alpha, (2 * SOFT + 1, 2 * SOFT + 1), 0)
    ramp = lambda n: np.clip(np.arange(n) / FEATHER, 0, 1)
    rows = np.minimum(ramp(ph), ramp(ph)[::-1])
    alpha *= np.minimum.outer(rows, ramp(pw))  # справа участок упирается в край картинки — там без растушёвки
    region = picture[py:py + ph, px:px + pw].astype(np.float64)
    picture[py:py + ph, px:px + pw] = (patch * alpha[..., None] + region * (1 - alpha[..., None])).round().astype(np.uint8)
    cv2.imencode('.png', picture)[1].tofile(str(out_path))
    print('warp', np.round(warp, 4).tolist(), '; colour gain', np.round(gain, 3).tolist(),
          '; mean difference outside', round(float(rest), 2), '; changed share of the patch', round(float(alpha.mean()), 3),
          '->', out_path.name)


if __name__ == '__main__':
    main()
