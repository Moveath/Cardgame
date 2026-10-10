"""Поставить бармена (лист на маджента-фоне) за стойку первого зала Клуба, не трогая фон.

    python scripts/club-live/place_bartender.py [--scale 0.32] [--x 1199] [--y 449]

Лист — `screens/references/characters/king-of-clubs-bartender-apron.png` (2026-10-04). Фон вырезается по цвету
#FF00FF, края очищаются от розового; стекло бокала, через которое просвечивал маджента, перекрашивается в голубой,
как бокал в зале. Бармен уменьшается и ставится на прежнее место (верх-лево листа → --x/--y в пикселях зала
3296×1412), всё ниже верхнего края стойки (COUNTER_Y) срезается: он стоит за стойкой. Свет — по цвету карты
прежнего бармена. Пиксели зала вне бармена не меняются. Пишет мастер-кадр `club-hall-live.png`
и `Resources/UI/Hub/club-hall.png`; фон без бармена — `screens/references/city/club-hall-live-no-bartender.png`.
"""
import argparse
from pathlib import Path

import cv2
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
REFS = ROOT / 'screens/references/city'
SHEET = ROOT / 'screens/references/characters/king-of-clubs-bartender-apron.png'
HUB = ROOT / 'unity/CardGameClient/Assets/Resources/UI/Hub/club-hall.png'
COUNTER_Y = 679  # верхний край стойки в пикселях зала: ниже бармена не видно
# Цвет карты прежнего бармена в зале (BGR) — к нему подгоняется свет.
CARD_LIT = None


def load(path, flags=cv2.IMREAD_COLOR):
    return cv2.imdecode(np.fromfile(str(path), np.uint8), flags)


def save(path, img):
    cv2.imencode('.png', img)[1].tofile(str(path))


def key_out(sheet):
    img = sheet.astype(np.float32)
    b, g, r = img[..., 0], img[..., 1], img[..., 2]
    # Насколько пиксель «маджента»: синий и красный высокие, зелёный низкий.
    magenta = np.clip((np.minimum(b, r) - g - 60) / 120, 0, 1) * np.clip((np.minimum(b, r) - 120) / 80, 0, 1)
    alpha = 1 - magenta
    # Внутри фигуры (заливка от краёв листа не дошла) фон не вырезаем — кроме стекла бокала.
    hard = (alpha < .5).astype(np.uint8)
    flood = hard.copy() * 255
    h, w = hard.shape
    mask = np.zeros((h + 2, w + 2), np.uint8)
    cv2.floodFill(flood, mask, (0, 0), 128)
    outside = (flood == 128)
    # Стекло: сиреневое пятно внутри фигуры — делаем голубым, как бокал в зале.
    hsv = cv2.cvtColor(sheet, cv2.COLOR_BGR2HSV)
    glass = (~outside) & (hsv[..., 0] >= 115) & (hsv[..., 0] <= 170) & (hsv[..., 1] > 35)
    hsv = hsv.astype(np.float32)
    hsv[..., 0] = np.where(glass, 104, hsv[..., 0])
    hsv[..., 1] = np.where(glass, np.minimum(hsv[..., 1], 120), hsv[..., 1])
    img = cv2.cvtColor(hsv.astype(np.uint8), cv2.COLOR_HSV2BGR).astype(np.float32)
    alpha = np.where(outside, alpha, 1.0)
    alpha = np.where(outside & (alpha > .5), alpha, alpha)
    # Розовая кайма по краю: убираем избыток красного и синего над зелёным.
    b, g, r = img[..., 0], img[..., 1], img[..., 2]
    spill = np.clip(np.minimum(b, r) - g, 0, None) * (alpha < .999)
    img[..., 0] -= spill
    img[..., 2] -= spill
    return np.clip(img, 0, 255), np.clip(alpha, 0, 1)


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--scale', type=float, default=.32)
    p.add_argument('--x', type=float, default=1199)
    p.add_argument('--y', type=float, default=449)
    a = p.parse_args()

    plate_path = REFS / 'club-hall-live-no-bartender.png'
    if not plate_path.exists():
        save(plate_path, load(REFS / 'club-hall-live.png'))
    hall = load(plate_path).astype(np.float32)
    rgb, alpha = key_out(load(SHEET))
    # Уменьшение с учётом прозрачности (premultiplied), чтобы края не темнели.
    h, w = alpha.shape
    size = (round(w * a.scale), round(h * a.scale))
    pre = cv2.resize(rgb * alpha[..., None], size, interpolation=cv2.INTER_AREA)
    al = cv2.resize(alpha, size, interpolation=cv2.INTER_AREA)
    col = pre / np.maximum(al[..., None], 1e-4)

    # Свет зала: карту прежнего бармена в зале освещали лампы стойки — подгоняем белое карты к ней.
    card = (al > .99) & (col.min(axis=2) > 200) & (np.ptp(col, axis=2) < 40)
    lit = np.float32([186, 226, 247])  # BGR тёплый кремовый — карта прежнего бармена под лампами (замер 201, 234, 248)
    gain = lit / np.maximum(col[card].mean(axis=0), 1)
    col = np.clip(col * gain, 0, 255)

    x0, y0 = int(round(a.x)), int(round(a.y))
    sh, sw = al.shape
    # Срез стойкой: ниже COUNTER_Y — не видно (край чуть мягкий, как у нарисованного).
    rows = np.arange(y0, y0 + sh)[:, None]
    al = al * np.clip((COUNTER_Y - rows) / 1.5 + .5, 0, 1)
    region = hall[y0:y0 + sh, x0:x0 + sw]
    out = hall.copy()
    out[y0:y0 + sh, x0:x0 + sw] = col * al[..., None] + region * (1 - al[..., None])
    out = np.clip(out, 0, 255).astype(np.uint8)
    save(REFS / 'club-hall-live.png', out)
    save(HUB, out)
    print('placed', size, 'at', x0, y0, 'gain', np.round(gain, 3))


if __name__ == '__main__':
    main()
