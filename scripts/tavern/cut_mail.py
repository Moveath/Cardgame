"""Почта таверны «Червовая дама» — окно чатов (2026-10-10).

Лист `screens/references/city/tavern-mail-sheet.png` (ChatGPT, на сплошной мадженте; промпт —
`screens/references/city/tavern-mail-prompt.txt`) → четыре картинки в `Resources/UI/Hub` на прозрачном фоне:
открытый письменный ящик `tavern-mail-case`, печать-сердце `tavern-mail-seal`, перо в чернильнице
`tavern-mail-quill`, конверт `tavern-mail-envelope`. Прозрачность и цвет кромки — как у газеты (`scripts/news/cut_news.py`).

Ящик на листе плоский (≈2,9 : 1); в игре он выше — середина растянута по высоте только там, где ровное дерево,
кожа и пергамент (края с сердечками и уголками не тронуты), а петли вынуты до растяжки и поставлены обратно
неискажёнными. Так на 16:9 и 20:9 у переписки хватает строк.

    python scripts/tavern/cut_mail.py
"""
from pathlib import Path

import cv2
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
SHEET = ROOT / 'screens/references/city/tavern-mail-sheet.png'
OUT = ROOT / 'unity/CardGameClient/Assets/Resources/UI/Hub'
PAD = 4
CASE_ASPECT = 2.3  # ширина / высота ящика в игре


def read(path):
    return cv2.imdecode(np.fromfile(str(path), np.uint8), cv2.IMREAD_COLOR).astype(np.float32) / 255


def write(path, rgba):
    cv2.imencode('.png', (np.clip(rgba, 0, 1) * 255 + .5).astype(np.uint8))[1].tofile(str(path))


def unmix(img):
    """Альфа по мадженте и цвет без её подмеса на кромке."""
    border = np.concatenate([img[:8].reshape(-1, 3), img[-8:].reshape(-1, 3), img[:, :8].reshape(-1, 3), img[:, -8:].reshape(-1, 3)])
    magenta = np.median(border, axis=0)
    key = lambda c: np.minimum(c[..., 2], c[..., 0]) - c[..., 1]
    alpha = np.clip(1 - key(img) / key(magenta), 0, 1)
    return alpha, magenta


def objects(img):
    """Предметы листа: группы пятен (перо и чернильница — одна группа), сверху вниз и слева направо."""
    alpha, magenta = unmix(img)
    solid = (alpha > .5).astype(np.uint8)
    count, labels, stats, _ = cv2.connectedComponentsWithStats(cv2.dilate(solid, np.ones((9, 9), np.uint8)))
    groups = [i for i in range(1, count) if stats[i, cv2.CC_STAT_AREA] > 4000]
    result = []
    for i in groups:
        x, y, w, h = stats[i, :4]
        part = alpha * cv2.dilate((labels == i).astype(np.uint8), np.ones((3, 3), np.uint8))
        part[part < .06] = 0
        fg = np.clip((img - (1 - part[..., None]) * magenta) / np.maximum(part, 1e-3)[..., None], 0, 1)
        ys, xs = np.nonzero(part > .02)
        y0, y1, x0, x1 = max(0, ys.min() - PAD), ys.max() + 1 + PAD, max(0, xs.min() - PAD), xs.max() + 1 + PAD
        result.append(((y0, x0), np.dstack([fg, part])[y0:y1, x0:x1]))
    result.sort(key=lambda item: (item[0][0] > img.shape[0] * .55, item[0][1]))
    return result


def hinge_rows(case):
    """Строки петель: латунь (жёлто-оранжевая, яркая) в средней полосе между половинами."""
    h, w = case.shape[:2]
    middle = case[:, int(w * .33):int(w * .47)]
    b, g, r = middle[..., 0], middle[..., 1], middle[..., 2]
    brass = (r > .55) & (g > .38) & (b < .35) & (r - b > .3) & (middle[..., 3] > .9)
    rows = brass.sum(axis=1) > 8
    runs, start = [], None
    for y, on in enumerate(rows):
        if on and start is None: start = y
        if not on and start is not None:
            if y - start > 20: runs.append((start, y))
            start = None
    # Ширина петель — по их створкам: пятна латуни высотой с петлю (тонкие золотые рамки рядом — длинные, не в счёт).
    count, _, stats, _ = cv2.connectedComponentsWithStats(brass.astype(np.uint8))
    big = [stats[i] for i in range(1, count) if stats[i, cv2.CC_STAT_AREA] > 400 and 50 < stats[i, cv2.CC_STAT_HEIGHT] < 130]
    x0 = min(s[0] for s in big) + int(w * .33)
    x1 = max(s[0] + s[2] for s in big) + int(w * .33)
    return runs, (x0 - 6, x1 + 6)


def stretch_case(case):
    h, w = case.shape[:2]
    runs, (cx0, cx1) = hinge_rows(case)
    assert len(runs) == 2, runs
    (a0, a1), (b0, b1) = runs
    a0, a1, b0, b1 = a0 - 6, a1 + 6, b0 - 6, b1 + 6
    hinges = [case[a0:a1, cx0:cx1].copy(), case[b0:b1, cx0:cx1].copy()]
    clean = case.copy()
    # Под петлями — та же середина, что между ними (дерево и щель), сдвигом блока строк.
    free = case[a1 + 4:b0 - 4, cx0:cx1]
    for y0, y1 in ((a0, a1), (b0, b1)):
        need = y1 - y0
        block = np.concatenate([free] * (need // len(free) + 1))[:need]
        clean[y0:y1, cx0:cx1] = block
    # Тянем полосу от верха верхней петли до низа нижней: края с орнаментом остаются как были.
    top, bottom = a0, b1
    new_h = int(round(w / CASE_ASPECT))
    band_h = bottom - top + new_h - h
    band = cv2.resize(clean[top:bottom], (w, band_h), interpolation=cv2.INTER_CUBIC)
    out = np.concatenate([clean[:top], band, clean[bottom:]])
    k = band_h / (bottom - top)
    for (y0, y1), hinge in zip(((a0, a1), (b0, b1)), hinges):
        centre = top + ((y0 + y1) / 2 - top) * k
        y = int(round(centre - (y1 - y0) / 2))
        region = out[y:y + (y1 - y0), cx0:cx1]
        a = hinge[..., 3:4]
        region[..., :3] = hinge[..., :3] * a + region[..., :3] * (1 - a)
        region[..., 3:4] = np.maximum(region[..., 3:4], a)
    print('case', w, 'x', h, '->', w, 'x', out.shape[0], '; hinges', runs, 'cols', cx0, cx1, '; band', top, bottom, 'x%.2f' % k)
    return np.clip(out, 0, 1)


def main():
    img = read(SHEET)
    found = objects(img)
    assert len(found) == 4, [f[0] for f in found]
    names = ['tavern-mail-case', 'tavern-mail-seal', 'tavern-mail-quill', 'tavern-mail-envelope']
    for name, ((y, x), rgba) in zip(names, found):
        if name == 'tavern-mail-case':
            rgba = stretch_case(rgba)
        write(OUT / (name + '.png'), rgba)
        print(name, rgba.shape[1], 'x', rgba.shape[0], 'at', x, y)


main()
