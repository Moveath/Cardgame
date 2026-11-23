"""Чёткая табличка вывесок для окон города (2026-11-01): UI/Hub/Hud/plaque-hd.png.
Тот же рисунок, что у UI/Hub/sign-plaque.png (160×72, рамка 20 px): тёмный контур, золотой обод с переливом
сверху вниз, тёмная и тонкая золотая линии, тёмное поле и четыре заклёпки, но в SCALE раз крупнее и со
сглаживанием. Окно настроек растягивало старую рамку в 2,3 раза — она мылилась; эта на экране только уменьшается.
Рамка (border) в meta — 20 * SCALE с каждой стороны."""
from pathlib import Path
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "unity/CardGameClient/Assets/Resources/UI/Hub/Hud/plaque-hd.png"
SCALE, SUPER = 4, 4          # во сколько раз крупнее оригинала; сглаживание
BORDER, MIDDLE = 20, 4       # рамка и растягиваемая середина — в пикселях оригинала
RADIUS = 10                  # скругление внешнего края


def lerp3(t, top, mid, bottom):
    t = t[..., None]
    a, b, c = (np.array(v, float) for v in (top, mid, bottom))
    return np.where(t < .5, a + (b - a) * (t / .5), b + (c - b) * ((t - .5) / .5))


def main():
    side = 2 * BORDER + MIDDLE
    n = side * SCALE * SUPER
    unit = SCALE * SUPER                         # пикселей на пиксель оригинала
    ys, xs = np.mgrid[0:n, 0:n].astype(float) + .5
    u, v = xs / unit, ys / unit                  # координаты в пикселях оригинала
    half = side / 2
    qx = np.abs(u - half) - (half - RADIUS)
    qy = np.abs(v - half) - (half - RADIUS)
    outside = np.hypot(np.maximum(qx, 0), np.maximum(qy, 0)) + np.minimum(np.maximum(qx, qy), 0) - RADIUS
    d = -outside                                 # глубина от внешнего края внутрь
    t = v / side                                 # 0 сверху, 1 снизу

    rgb = lerp3(t, (41, 30, 22), (31, 22, 17), (22, 16, 13))                      # поле
    layers = [
        (0, 1, lerp3(t, (12, 5, 2), (10, 4, 2), (16, 8, 3))),                     # контур
        (1, 6, lerp3(t, (252, 238, 168), (210, 166, 90), (158, 100, 34))),        # обод
        (6, 7, lerp3(t, (36, 26, 16), (28, 19, 12), (16, 8, 5))),                 # тёмная линия
        (7, 8.6, lerp3(t, (238, 192, 106), (205, 160, 84), (146, 98, 40))),       # тонкое золото
        (8.6, 9.6, lerp3(t, (24, 17, 12), (22, 15, 12), (14, 10, 10))),           # тёмная линия
    ]
    for lo, hi, color in layers:
        mask = (d >= lo) & (d < hi)
        rgb[mask] = color[mask]
    # Блик по верхней кромке обода — как у металла под фонарём.
    shine = (d >= 1) & (d < 2.2) & (t < .5)
    rgb[shine] = rgb[shine] * .6 + np.array([255, 250, 210]) * .4

    # Заклёпки: тёмная посадка, золотая шляпка с переливом и бликом.
    for cx in (13.5, side - 13.5):
        for cy in (13.5, side - 13.5):
            r = np.hypot(u - cx, v - cy)
            seat = r < 3.3
            rgb[seat] = rgb[seat] * .35 + np.array([70, 52, 28]) * .65
            head = r < 2.5
            k = np.clip((v - (cy - 2.5)) / 5, 0, 1)[..., None]
            gold = np.array([252, 232, 165]) * (1 - k) + np.array([200, 150, 62]) * k
            rgb[head] = gold[head]
            glint = np.hypot(u - (cx - .8), v - (cy - .9)) < .8
            rgb[glint] = rgb[glint] * .3 + np.array([255, 252, 230]) * .7

    alpha = (d >= 0).astype(float)
    image = np.dstack([rgb * alpha[..., None], alpha * 255])
    # Сглаживание: усредняем блоки SUPER×SUPER (цвет — с учётом прозрачности).
    m = side * SCALE
    image = image.reshape(m, SUPER, m, SUPER, 4).mean(axis=(1, 3))
    a = image[..., 3:4]
    image[..., :3] = np.where(a > 0, image[..., :3] * 255 / np.maximum(a, 1e-6), 0)
    Image.fromarray(np.clip(image, 0, 255).astype(np.uint8), "RGBA").save(OUT)
    print(OUT.name, m, "x", m, "border", BORDER * SCALE)


if __name__ == "__main__":
    main()
