"""Места под нарисованные вывески (решение автора, 2026-10-07): у Таверны, Гадалки, Дома Шестёрки, Банка Тузяры и трёх
мест Подполья надпись была своей плашкой поверх картинки — «как будто просто поверх». Как у Архива, Лавки и Клуба,
пустая табличка должна быть частью самой картинки, а буквы — кодом (Painted в PlayerHubSigns.cs).

Шаг 1 — `python scripts/six-house/sign_spots.py crops [<место> ...]`: вырезает куски районов (всех мест или только
названных) из игровых картинок 2508×1412 (координаты ниже — в системе вывесок 1672×941, ×1.5) в
`screens/references/city/canvases/sign-<место>-crop.png`.
Пропорции кусков — 1:1, 2:3 или 3:2: ChatGPT отдаёт картинку тех же пропорций, без обрезки.

Шаг 2 — ChatGPT дорисовывает пустую табличку (промпты — prompts-v2.md, «Вывески — места под надписи»), ответ —
`canvases/sign-<место>-edit.png`.

Шаг 3 — `python scripts/six-house/sign_spots.py preview <место> [<место> ...]`: ответ приводится к размеру куска,
выравнивается по нему (ECC, аффинно, без участка правки), подгоняется по цвету, и из участка EDIT берётся только то,
что генератор поменял (прозрачность по разнице, как у полки подарка — shelf_patch.py). Только превью, игра не
меняется: `canvases/sign-<место>-result.png` (кусок с табличкой) и `canvases/<район>-signs-preview.png` (весь район со
всеми готовыми табличками).

Шаг 4 — `python scripts/six-house/sign_spots.py install <место> [<место> ...]`: то же прямо в игровую картинку района
(накладывается на её текущий вид); до первой правки район сохраняется в `canvases/<район>-before-signs.png`.
"""
import sys
from pathlib import Path

import cv2
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
HUB = ROOT / 'unity/CardGameClient/Assets/Resources/UI/Hub'
CANVAS = ROOT / 'screens/references/city/canvases'
SCALE = 1.5  # игровая картинка 2508×1412 к системе вывесок 1672×941

# место: район, кусок (x, y, ширина, высота) и участок, где можно менять (x0, y0, x1, y1) — в системе вывесок.
SPOTS = {
    'tavern':  ('card-city-old-town',    (940, 90, 340, 510),   (950, 230, 1270, 400)),
    'fortune': ('card-city-old-town',    (1240, 220, 380, 380), (1270, 260, 1520, 500)),
    # Первые два круга (над окном, над крышей) — кусок (480, 150, 340, 510), `sign-six-crop-v1.png`; третий — табличка
    # на кронштейне с правого угла домика над проулком, кусок шире вправо.
    'six':     ('card-city-old-town',    (520, 230, 420, 420),  (690, 340, 935, 570)),
    'bank':    ('card-city-tuzyara',     (180, 110, 520, 520),  (230, 250, 640, 480)),
    'market':  ('card-city-underground', (150, 100, 500, 500),  (240, 180, 640, 400)),
    'club':    ('card-city-underground', (588, 60, 360, 540),   (600, 120, 935, 330)),
    'arena':   ('card-city-underground', (1020, 180, 570, 380), (1040, 190, 1580, 330)),
}
CHANGED = (16, 40)            # разница (из 255), с которой пиксель берётся из правки: от — до полностью
CORE, NEAR = 120, 16          # правка — пятна сильной разницы не меньше CORE px; мягкая маска — в NEAR px от них
GROW, SOFT, FEATHER = 6, 9, 14  # расширение и размытие маски, мягкий край участка, px


def read(path):
    return cv2.imdecode(np.fromfile(str(path), np.uint8), cv2.IMREAD_COLOR)


def write(path, image):
    cv2.imencode('.png', image)[1].tofile(str(path))


def px(values):
    return [int(round(v * SCALE)) for v in values]


def crops(names):
    for name in names:
        district, crop, _ = SPOTS[name]
        x, y, w, h = px(crop)
        part = read(HUB / (district + '.png'))[y:y + h, x:x + w]
        write(CANVAS / f'sign-{name}-crop.png', part)
        print(name, district, 'crop', w, 'x', h)


def patch(name, room):
    """Вклеить табличку места в картинку района room (на месте); вернуть строку отчёта."""
    district, crop, edit = SPOTS[name]
    cx, cy, cw, ch = px(crop)
    ex0, ey0, ex1, ey1 = [v - o for v, o in zip(px(edit), (cx, cy, cx, cy))]
    original = room[cy:cy + ch, cx:cx + cw].copy()
    edited = cv2.resize(read(CANVAS / f'sign-{name}-edit.png'), (cw, ch), interpolation=cv2.INTER_AREA)
    outside = np.full((ch, cw), 255, np.uint8)
    outside[ey0:ey1, ex0:ex1] = 0
    warp = np.eye(2, 3, dtype=np.float32)
    gray = lambda image: cv2.GaussianBlur(cv2.cvtColor(image, cv2.COLOR_BGR2GRAY), (5, 5), 0).astype(np.float32)
    criteria = (cv2.TERM_CRITERIA_EPS | cv2.TERM_CRITERIA_COUNT, 200, 1e-6)
    _, warp = cv2.findTransformECC(gray(original), gray(edited), warp, cv2.MOTION_AFFINE, criteria, outside, 5)
    aligned = cv2.warpAffine(edited, warp, (cw, ch), flags=cv2.INTER_LINEAR | cv2.WARP_INVERSE_MAP,
                             borderMode=cv2.BORDER_REFLECT).astype(np.float64)
    keep = outside > 0
    gain = (original[keep].mean(0) + 1) / (aligned[keep].mean(0) + 1)
    aligned = np.clip(aligned * gain, 0, 255)
    rest = np.abs(aligned[keep] - original[keep].astype(np.float64)).mean()
    new = aligned[ey0:ey1, ex0:ex1]
    base = original[ey0:ey1, ex0:ex1].astype(np.float64)
    difference = cv2.GaussianBlur(np.abs(new - base).max(axis=2).astype(np.float32), (5, 5), 0)
    count, labels, stats, _ = cv2.connectedComponentsWithStats((difference >= CHANGED[1]).astype(np.uint8))
    core = np.isin(labels, [i for i in range(1, count) if stats[i, cv2.CC_STAT_AREA] >= CORE]).astype(np.uint8)
    near = cv2.dilate(core, np.ones((2 * NEAR + 1, 2 * NEAR + 1), np.uint8)).astype(np.float32)
    alpha = np.clip((difference - CHANGED[0]) / (CHANGED[1] - CHANGED[0]), 0, 1) * near
    alpha = cv2.dilate(alpha, np.ones((2 * GROW + 1, 2 * GROW + 1), np.uint8))
    alpha = cv2.GaussianBlur(alpha, (2 * SOFT + 1, 2 * SOFT + 1), 0)
    eh, ew = alpha.shape
    ramp = lambda n: np.clip(np.minimum(np.arange(n), np.arange(n)[::-1]) / FEATHER, 0, 1)
    alpha = (alpha * np.minimum.outer(ramp(eh), ramp(ew)))[..., None]
    region = room[cy + ey0:cy + ey1, cx + ex0:cx + ex1].astype(np.float64)
    room[cy + ey0:cy + ey1, cx + ex0:cx + ex1] = np.clip(region * (1 - alpha) + new * alpha, 0, 255).round().astype(np.uint8)
    write(CANVAS / f'sign-{name}-result.png', room[cy:cy + ch, cx:cx + cw])
    return (f'{name}: warp {np.round(warp, 4).tolist()}; colour gain {np.round(gain, 3).tolist()}; '
            f'mean difference outside {rest:.2f}; changed share {alpha.mean():.3f}')


def apply(names, install):
    """Таблички мест names — в превью района или (install) прямо в игровую картинку."""
    for district in dict.fromkeys(SPOTS[name][0] for name in names):
        source = HUB / (district + '.png')
        room = read(source)
        for name in [n for n in names if SPOTS[n][0] == district]:
            print(patch(name, room))
        if install:
            backup = CANVAS / f'{district}-before-signs.png'
            if not backup.exists():
                write(backup, read(source))
            write(source, room)
            print(district, '-> game picture', source.name)
        else:
            write(CANVAS / f'{district}-signs-preview.png', room)
            print(district, '-> preview', f'{district}-signs-preview.png')


if __name__ == '__main__':
    command, spots = sys.argv[1:2], sys.argv[2:]
    if command == ['crops'] and all(spot in SPOTS for spot in spots):
        crops(spots or list(SPOTS))
    elif command in (['preview'], ['install']) and spots and all(spot in SPOTS for spot in spots):
        apply(spots, command == ['install'])
    else:
        print(__doc__)
