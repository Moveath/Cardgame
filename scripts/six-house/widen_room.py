"""Комната Шестёрки шире — 21:9 для телефонов 20:9 (2026-10-07).

Картинка 16:9 на телефоне закрывает экран по ширине и теряет верх и низ. Края дорисованы ChatGPT по заготовкам
`screens/references/city/canvases/six-room-v1-wide-left/right-canvas.png` (комната 60 % | пурпурное 40 %,
промпт — `prompts-v2.md`, «Комната шире»). Скрипт берёт из дорисованных картинок по EXTRA px за краями комнаты,
подгоняет их цвет к оригиналу по полосе у шва и плавно сводит в оригинал на BAND px. Середина — оригинал без изменений.

    python scripts/six-house/widen_room.py [six | bank | kiosk] [canvas]

`canvas` — только заготовки: левая (пурпурное 40 % | левые 60 % картинки) и правая (правые 60 % | пурпурное 40 %).

Зал банка (2026-10-08) — так же: заготовки `canvases/bank-hall-v4-wide-left/right-canvas.png`, ответы —
`bank-hall-v4-wide-left/right.png`, итог — `bank-hall-v5-wide.png` и игровая `UI/Hub/bank-hall.png`.

Киоск «Карточный вестник» (2026-10-09) — так же: на экране 20:9 у 16:9 срезана табличка. «Камерой дальше» со всех
сторон (`scripts/city/zoom_out.py kiosk`) автор отверг: фон не как в городе, киоск далеко, второй фонтан.
Заготовки `canvases/news-kiosk-v2-wide-left/right-canvas.png`, итог — `news-kiosk-v3-wide.png` и `UI/Hub/news-kiosk.png`.

Зал таверны (2026-10-10) — так же: `tavern-hall-v1.png` → заготовки `canvases/tavern-hall-v1-wide-left/right-canvas.png`,
итог — `tavern-hall-v2-wide.png` и `UI/Hub/tavern-hall.png` (meta — при подключении этажа).

Прилавок Лавки (2026-10-17) — так же: `shop-counter-v2.png` (касса вместо банки, `scripts/shop/register_patch.py`) →
заготовки `canvases/shop-counter-v2-wide-left/right-canvas.png`, промпт — `shop-prompts.txt`, итог —
`shop-counter-v3-wide.png` и `UI/Hub/shop-counter.png` (meta — при подключении этажа).

Зал Архива (2026-10-18) — так же: `archive-hall-v1.png` → заготовки `canvases/archive-hall-v1-wide-left/right-canvas.png`,
промпт — `archive-prompts.txt`, итог — `archive-hall-v2-wide.png` и `UI/Hub/archive-hall.png` (meta — при подключении этажа).

Зал рынка Короля бубен (2026-10-19) — так же: `market-hall-v1.png` (с кораблём в арке) → заготовки
`canvases/market-hall-v1-wide-left/right-canvas.png`, промпт — `market-prompts.txt`, итог — `market-hall-v2-wide.png`
и `UI/Hub/market-hall.png`. Арка без корабля — отдельная накладка (`scripts/market/ship_patch.py`).
"""
import sys
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
CITY = ROOT / 'screens/references/city'
HUB = ROOT / 'unity/CardGameClient/Assets/Resources/UI/Hub'
# комната: исходник 16:9, левая и правая дорисовки, итог, игровая картинка
ROOMS = {
    'six': ('six-room-v1.png', 'canvases/six-room-v1-wide-left.png', 'canvases/six-room-v1-wide-right.png',
            'six-room-v2-wide.png', 'six-room.png'),
    'bank': ('bank-hall-v4.png', 'canvases/bank-hall-v4-wide-left.png', 'canvases/bank-hall-v4-wide-right.png',
             'bank-hall-v5-wide.png', 'bank-hall.png'),
    'tavern': ('tavern-hall-v1.png', 'canvases/tavern-hall-v1-wide-left.png', 'canvases/tavern-hall-v1-wide-right.png',
               'tavern-hall-v2-wide.png', 'tavern-hall.png'),
    'kiosk': ('news-kiosk-v2.png', 'canvases/news-kiosk-v2-wide-left.png', 'canvases/news-kiosk-v2-wide-right.png',
              'news-kiosk-v3-wide.png', 'news-kiosk.png'),
    'shop': ('shop-counter-v2.png', 'canvases/shop-counter-v2-wide-left.png', 'canvases/shop-counter-v2-wide-right.png',
             'shop-counter-v3-wide.png', 'shop-counter.png'),
    'archive': ('archive-hall-v1.png', 'canvases/archive-hall-v1-wide-left.png', 'canvases/archive-hall-v1-wide-right.png',
                'archive-hall-v2-wide.png', 'archive-hall.png'),
    'market': ('market-hall-v1.png', 'canvases/market-hall-v1-wide-left.png', 'canvases/market-hall-v1-wide-right.png',
               'market-hall-v2-wide.png', 'market-hall.png'),
}
SOURCE, LEFT, RIGHT, OUT, GAME = None, None, None, None, None
EXTRA = 262   # (2196 - 1672) / 2: 21:9 при высоте 941; ширина кратна 4
BAND = 48     # шов: столько px оригинала у края плавно переходят из дорисованного
MATCH = 120   # полоса оригинала у края, по которой подгоняется цвет


def load(path, size):
    image = Image.open(path).convert('RGB')
    if image.size != size:
        image = image.resize(size, Image.LANCZOS)
    return np.asarray(image).astype(np.float64)


def main():
    source = load(SOURCE, Image.open(SOURCE).size)
    h, w = source.shape[:2]
    keep = round(w * .6)
    new = w - keep
    left = load(LEFT, (w, h))
    right = load(RIGHT, (w, h))
    # Где в дорисованных картинках лежит оригинал: в левой — справа от пурпурного, в правой — слева.
    left_orig = left[:, new:]           # = source[:, :keep]
    right_orig = right[:, :keep]        # = source[:, w - keep:]

    def gain(painted, original):
        return (original.reshape(-1, 3).mean(0) + 1) / (painted.reshape(-1, 3).mean(0) + 1)

    left = np.clip(left * gain(left_orig[:, :MATCH], source[:, :MATCH]), 0, 255)
    right = np.clip(right * gain(right_orig[:, -MATCH:], source[:, -MATCH:]), 0, 255)

    out = np.zeros((h, w + 2 * EXTRA, 3))
    out[:, EXTRA:EXTRA + w] = source
    out[:, :EXTRA] = left[:, new - EXTRA:new]
    out[:, EXTRA + w:] = right[:, keep:keep + EXTRA]
    ramp = np.linspace(1, 0, BAND)[None, :, None]
    out[:, EXTRA:EXTRA + BAND] = left[:, new:new + BAND] * ramp + source[:, :BAND] * (1 - ramp)
    out[:, EXTRA + w - BAND:EXTRA + w] = (right[:, keep - BAND:keep] * ramp[:, ::-1]
                                         + source[:, w - BAND:] * (1 - ramp[:, ::-1]))
    image = Image.fromarray(np.round(out).astype(np.uint8))
    image.save(OUT)
    image.save(GAME)
    print(OUT.name, image.size)


def canvases():
    source = Image.open(SOURCE).convert('RGB')
    w, h = source.size
    keep = round(w * .6)
    for answer, crop, at in ((LEFT, (0, 0, keep, h), w - keep), (RIGHT, (w - keep, 0, w, h), 0)):
        canvas = Image.new('RGB', (w, h), (255, 0, 255))
        canvas.paste(source.crop(crop), (at, 0))
        path = answer.with_name(answer.stem + '-canvas.png')
        canvas.save(path)
        print(path.name, canvas.size)


if __name__ == '__main__':
    room = ROOMS[sys.argv[1] if len(sys.argv) > 1 else 'six']
    SOURCE, LEFT, RIGHT, OUT = (CITY / name for name in room[:4])
    GAME = HUB / room[4]
    canvases() if len(sys.argv) > 2 and sys.argv[2] == 'canvas' else main()
