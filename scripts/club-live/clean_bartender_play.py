"""Петля бармена «протирает» с родным фоном зала (автор, 2026-10-04: «на заднем фоне уже изначально рябит»).

    python scripts/club-live/clean_bartender_play.py

Исходник — петля Codex `screens/qa/bartender-prepared-final/club-bartender-play.mp4` с маской: внутри маски
генератор перерисовал полки и бокалы (мягче и чуть иначе, чем картинка зала), отсюда «рябь» вокруг бармена.
Берём одну маску на всю петлю — где хоть в одном кадре бармен (заметно отличается от зала и не янтарь полок),
залитую и сглаженную; остальное — пиксели зала. Маска одна на все кадры, граница не дрожит.
Пишет в Resources петлю и маску (прямоугольник прежний — в PlayerHubInterior.ClubVideos не меняется) и
`video-frames/bartender-start.png` — первый кадр новой петли на картинке зала (первый и последний кадр для
генераций жестов бармена).
"""
import os
import sys
from pathlib import Path

import cv2
import numpy as np

sys.path.insert(0, os.path.dirname(__file__))
import imageio_ffmpeg  # noqa: E402
import prepare_bartender as base  # noqa: E402

base.cv2, base.np, base.imageio_ffmpeg = cv2, np, imageio_ffmpeg
ROOT = Path(__file__).resolve().parents[2]
HUB = ROOT / 'unity/CardGameClient/Assets/Resources/UI/Hub'
SRC = ROOT / 'screens/qa/bartender-prepared-final'
RECT = (728.75, 268.125, 146.25, 128.125)  # прямоугольник петли в единицах зала 1916×821


def main():
    hall = base.read_image(HUB / 'club-hall.png')
    s = hall.shape[1] / 1916
    mask0 = base.read_image(SRC / 'club-bartender-mask.png')[..., 0].astype(np.float32) / 255
    h, w = mask0.shape
    x, y, rw, rh = RECT
    # Картинка зала под петлёй — в пикселях ролика.
    ref = cv2.resize(hall[round(y * s):round((y + rh) * s), round(x * s):round((x + rw) * s)], (w, h),
                     interpolation=cv2.INTER_CUBIC).astype(np.float32)
    cap = cv2.VideoCapture(str(SRC / 'club-bartender-play.mp4'))
    frames = []
    while True:
        ok, f = cap.read()
        if not ok:
            break
        frames.append(f.astype(np.float32))
    cap.release()

    # Бармен = то, что двигается за петлю (руки, бокал, полотенце), + белая карта и перчатки (светлое и без цвета).
    stack = np.stack(frames)
    moving = cv2.GaussianBlur(stack.std(axis=0).max(axis=2), (0, 0), 2) > 9
    median = np.median(stack, axis=0).astype(np.uint8)
    hsv = cv2.cvtColor(median, cv2.COLOR_BGR2HSV).astype(np.float32)
    white = (hsv[..., 2] > 150) & (hsv[..., 1] < 90)
    union = (moving | white).astype(np.float32)
    union *= (mask0 > .05)
    m = (union > 0).astype(np.uint8)
    m = cv2.morphologyEx(m, cv2.MORPH_OPEN, np.ones((3, 3), np.uint8))
    m = cv2.morphologyEx(m, cv2.MORPH_CLOSE, np.ones((25, 25), np.uint8))
    count, labels, stats, _ = cv2.connectedComponentsWithStats(m)
    biggest = 1 + int(np.argmax(stats[1:, cv2.CC_STAT_AREA]))
    keep = (labels == biggest).astype(np.uint8)  # один бармен — одна область; блики на полках отбрасываем
    flood = keep * 255
    cv2.floodFill(flood, np.zeros((h + 2, w + 2), np.uint8), (0, 0), 128)
    keep = ((flood != 128) | (keep > 0)).astype(np.float32)
    keep = cv2.dilate(keep, np.ones((5, 5), np.uint8))
    mask = np.clip(cv2.GaussianBlur(keep, (0, 0), 2.5), 0, 1) * mask0
    print('mask share %.1f%% (was %.1f%%)' % (100 * (mask > .5).mean(), 100 * (mask0 > .5).mean()))

    # В ролик пишем уже собранный кадр: вне маски — зал (на стыке с картинкой ничего не дрожит).
    out = [np.clip(f * mask[..., None] + ref * (1 - mask[..., None]), 0, 255).astype(np.uint8) for f in frames]
    base.write_video(HUB / 'Video/club-bartender-play.mp4', out, (w, h), 48)
    base.save_image(HUB / 'Video/club-bartender-mask.png', mask * 255)

    # Первый кадр петли на картинке зала — заготовка для генераций жестов.
    full = hall.astype(np.float32)
    X, Y, W, H = (int(round(v * s)) for v in RECT)
    first = cv2.resize(out[0], (W, H), interpolation=cv2.INTER_AREA).astype(np.float32)
    mm = cv2.resize(mask, (W, H))[..., None]
    full[Y:Y + H, X:X + W] = first * mm + full[Y:Y + H, X:X + W] * (1 - mm)
    L, T, S = 640, 245, 250
    crop = full[int(T * s):int((T + S) * s), int(L * s):int((L + S) * s)]
    base.save_image(ROOT / 'screens/references/city/video-frames/bartender-start.png',
                    cv2.resize(np.clip(crop, 0, 255).astype(np.uint8), (1440, 1440), interpolation=cv2.INTER_CUBIC))
    print('ok', len(out), 'frames')


if __name__ == '__main__':
    main()
