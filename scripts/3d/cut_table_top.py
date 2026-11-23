"""Текстура верха стола из листа генерации: вид сверху разворачивается длинной стороной поперёк, тиснёная треф заменяется золотой вышивкой.
Красная на красном с места игрока не видна: на сукно смотрим под острым углом (автор, 2026-11-06).
Запуск: python scripts/3d/cut_table_top.py. Вход: генерации/2-стол-и-стул/готово/лист.jpg и генерации/6-золотая-треф/готово/треф.jpg,
выход: art3d/table/table-top.png."""
from pathlib import Path

import cv2
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "генерации/2-стол-и-стул/готово/лист.jpg"
EMBLEM = ROOT / "генерации/6-золотая-треф/готово/треф.jpg"
OUT = ROOT / "art3d/table/table-top.png"

CX, CY, RX, RY = 516.5, 672.5, 430.5, 608.5   # Овал на листе, замерен по границе с серым фоном.
OLD_EMBLEM_X, OLD_EMBLEM_Y, OLD_EMBLEM_R = 517, 687, 124  # Старая треф на листе, радиус с запасом.
WIDTH = 2048
TABLE_M = 1.84                                 # Длина стола: сколько метров в ширине текстуры.
EMBLEM_R_M = .2                                # Радиус нового герба на столе.
EDGE = (58, 20, 10)                            # За овалом — тёмное дерево: при уменьшении текстуры край не светлеет.
KEY_LIMIT = 40                                 # Сколько пурпура в пикселе ещё считается вышивкой.
CLUB_LIMIT = 760                               # Внутри этого радиуса на листе вышивки только треф, снаружи только кольца.
EMBLEM_STEM_END = 1603                         # Здесь кончается ножка, ниже генерация оставила розовое пятно.
SHADOW_ROOM = 16                               # Поле вокруг герба, куда ложится его тень.


def erase_old_emblem(table, x, y, r):
    """Закрашивает старую треф ближайшим сукном и возвращает ему зерно, снятое с соседнего участка."""
    mask = np.zeros(table.shape[:2], np.uint8)
    cv2.circle(mask, (x, y), r, 255, -1)
    small = cv2.resize(table, None, fx=.25, fy=.25, interpolation=cv2.INTER_AREA)
    filled = cv2.inpaint(small, cv2.resize(mask, (small.shape[1], small.shape[0]), interpolation=cv2.INTER_NEAREST), 6, cv2.INPAINT_TELEA)
    smooth = cv2.GaussianBlur(cv2.resize(filled, (table.shape[1], table.shape[0]), interpolation=cv2.INTER_CUBIC), (0, 0), 14)
    donor = np.roll(table.astype(np.float32), -2 * r - 40, axis=1)
    grain = donor - cv2.GaussianBlur(donor, (0, 0), 6)
    alpha = cv2.GaussianBlur(mask.astype(np.float32) / 255, (0, 0), 8)[..., None]
    return np.clip(table * (1 - alpha) + (smooth + grain) * alpha, 0, 255).astype(np.uint8)


def cut_emblem(diameter):
    """Вышивка с листа генерации: фон вырезан, пятно под ножкой убрано, треф поставлена в середину кольца.
    Цвет возвращается умноженным на непрозрачность: так пурпур не просачивается в край при уменьшении."""
    pixels = np.asarray(Image.open(EMBLEM).convert("RGB")).astype(np.float32)
    magenta = np.minimum(pixels[..., 0], pixels[..., 2]) - pixels[..., 1]
    # Сжатие JPEG оставляет розовую кайму: край срезается на два пикселя внутрь контура.
    solid = cv2.erode((magenta < KEY_LIMIT).astype(np.uint8), np.ones((5, 5), np.uint8)).astype(np.float32)
    rows, columns = np.nonzero(solid)
    ring_left, ring_right = columns.min(), columns.max()
    ring_top, ring_bottom = rows.min(), rows.max()
    middle_x, middle_y = (ring_left + ring_right) / 2, (ring_top + ring_bottom) / 2
    yy, xx = np.mgrid[0:solid.shape[0], 0:solid.shape[1]]
    near_middle = np.hypot(xx - middle_x, yy - middle_y) < CLUB_LIMIT
    club = solid * near_middle
    club[EMBLEM_STEM_END:] = 0
    ring = solid * ~near_middle
    club_rows = np.nonzero(club.sum(axis=1))[0]
    lift = round((club_rows.min() + club_rows.max()) / 2 - middle_y)
    alpha = ring + np.roll(club, -lift, axis=0)
    paint = pixels * ring[..., None] + np.roll(pixels * club[..., None], -lift, axis=0)
    box = (ring_top, ring_bottom + 1, ring_left, ring_right + 1)
    size = (diameter, diameter)
    return (cv2.resize(paint[box[0]:box[1], box[2]:box[3]], size, interpolation=cv2.INTER_AREA),
            cv2.resize(alpha[box[0]:box[1], box[2]:box[3]], size, interpolation=cv2.INTER_AREA))


def draw_emblem(table, x, y, radius):
    paint, alpha = cut_emblem(radius * 2)
    paint = np.pad(paint, ((SHADOW_ROOM, SHADOW_ROOM), (SHADOW_ROOM, SHADOW_ROOM), (0, 0)))
    alpha = np.pad(alpha, SHADOW_ROOM)
    size = alpha.shape[0]
    left, top = x - size // 2, y - size // 2
    area = table[top:top + size, left:left + size].astype(np.float32)
    # Тень на сукне: вышивка лежит поверх ткани.
    shadow = cv2.GaussianBlur(np.roll(alpha, 3, axis=0), (0, 0), 3)[..., None]
    area *= 1 - shadow * .5
    area = area * (1 - alpha[..., None]) + paint
    table[top:top + size, left:left + size] = np.clip(area, 0, 255).astype(np.uint8)


def main():
    sheet = Image.open(SOURCE).convert("RGB")
    left, top = round(CX - RX), round(CY - RY)
    crop = sheet.crop((left, top, round(CX + RX), round(CY + RY)))
    source_width = crop.width
    table = crop.rotate(90, expand=True)
    k = WIDTH / table.width
    height = round(WIDTH * table.height / table.width)
    # Поворот на 90° переводит (x, y) в (y, ширина - 1 - x).
    old_x = round((OLD_EMBLEM_Y - top) * k)
    old_y = round((source_width - 1 - (OLD_EMBLEM_X - left)) * k)
    pixels = np.asarray(table.resize((WIDTH, height), Image.LANCZOS)).copy()
    pixels = erase_old_emblem(pixels, old_x, old_y, round(OLD_EMBLEM_R * k))
    draw_emblem(pixels, WIDTH // 2, height // 2, round(EMBLEM_R_M / TABLE_M * WIDTH))
    yy, xx = np.mgrid[0:height, 0:WIDTH]
    pixels[((xx + .5 - WIDTH / 2) / (WIDTH / 2)) ** 2 + ((yy + .5 - height / 2) / (height / 2)) ** 2 > 1] = EDGE
    OUT.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(pixels).save(OUT)
    print(OUT.name, pixels.shape[1], pixels.shape[0])


if __name__ == "__main__":
    main()
