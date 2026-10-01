"""Мультяшная говняная лужа, муха (2 кадра) и линия вони для стикеров «Говна».
Рисуем в 4x и уменьшаем — сглаженные края, стиль с тёмным контуром как у остального арта."""
import math
import os
import random
import sys

import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
# По умолчанию пишет прямо в ресурсы игры; .meta у файлов уже есть.
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "..", "..", "unity", "CardGameClient", "Assets",
                                                         "Resources", "Govno", "Stickers")
SS = 4

OUTLINE = (34, 18, 7, 255)
SIDE = (58, 32, 12, 255)
TOP = (96, 58, 23, 255)
DARK = (72, 42, 15, 255)
LIGHT = (124, 80, 35, 255)
OLIVE = (92, 80, 30, 255)
GLOSS = (196, 156, 108, 255)
CORN = (232, 196, 74, 255)
BODY_DARK = (70, 40, 15, 255)
BODY = (98, 60, 24, 255)
SHINE = (170, 124, 78, 255)


def blob(cx, cy, a, b, rnd, lobes=7, amp=0.05, n=160):
    ph1, ph2 = rnd.uniform(0, 6.28), rnd.uniform(0, 6.28)
    pts = []
    for i in range(n):
        t = 2 * math.pi * i / n
        k = 1 + amp * math.sin(lobes * t + ph1) + amp * 0.5 * math.sin((lobes * 2 - 1) * t + ph2)
        pts.append((cx + a * k * math.cos(t), cy + b * k * math.sin(t)))
    return pts


def curve_pts(p0, p1, p2, n=40):
    out = []
    for i in range(n):
        t = i / (n - 1)
        out.append(((1 - t) ** 2 * p0[0] + 2 * (1 - t) * t * p1[0] + t * t * p2[0],
                    (1 - t) ** 2 * p0[1] + 2 * (1 - t) * t * p1[1] + t * t * p2[1]))
    return out


def sausage(img, p0, p1, p2, r0, r1):
    """Колбаска: штампуем круги вдоль кривой — тень, контур, тело, светлый верх, перетяжки, блик."""
    s = SS
    pts = curve_pts(p0, p1, p2)
    n = len(pts)
    rad = [r0 + (r1 - r0) * (i / (n - 1)) for i in range(n)]
    d = ImageDraw.Draw(img)
    shadow = Image.new("RGBA", img.size, (0, 0, 0, 0))
    sd = ImageDraw.Draw(shadow)
    for (x, y), r in zip(pts, rad):
        sd.ellipse((x - r * 1.25, y + r * 0.2, x + r * 1.25, y + r * 1.2), fill=(40, 22, 8, 150))
    img.alpha_composite(shadow)
    for (x, y), r in zip(pts, rad):
        R = r + 2.4 * s
        d.ellipse((x - R, y - R, x + R, y + R), fill=OUTLINE)
    for (x, y), r in zip(pts, rad):
        d.ellipse((x - r, y - r, x + r, y + r), fill=BODY_DARK)
    for (x, y), r in zip(pts, rad):
        d.ellipse((x - r * 0.9, y - r, x + r * 0.9, y + r * 0.35), fill=BODY)
    for i in range(6, n - 4, 10):
        x, y = pts[i]
        r = rad[i]
        d.arc((x - r * 0.95, y - r * 0.95, x + r * 0.95, y + r * 0.95), 95, 175, fill=(48, 26, 9, 255), width=max(2, int(1.6 * s)))
    for i in range(3, n - 3, 3):
        x, y = pts[i]
        r = rad[i]
        d.ellipse((x - r * 0.35, y - r * 0.72, x + r * 0.2, y - r * 0.45), fill=SHINE)


def pile(img, cx, base_y, width):
    """Маленькая кучка-завиток из трёх ярусов с хвостиком."""
    s = SS
    d = ImageDraw.Draw(img)
    h = width * 0.36
    shadow = Image.new("RGBA", img.size, (0, 0, 0, 0))
    ImageDraw.Draw(shadow).ellipse((cx - width * 0.62, base_y - h * 0.05, cx + width * 0.62, base_y + h * 0.55), fill=(40, 22, 8, 150))
    img.alpha_composite(shadow)
    tip_y = base_y - width * 0.8
    d.polygon([(cx - width * 0.13, tip_y + width * 0.14), (cx + width * 0.13, tip_y + width * 0.14), (cx + width * 0.12, tip_y - width * 0.05)], fill=OUTLINE)
    tiers = [(1.0, 0.0), (0.74, -0.3), (0.48, -0.55)]
    for k, (sc, oy) in enumerate(tiers):
        ww, hh = width * sc, h * (0.9 if k else 1.0)
        y = base_y + oy * width
        d.ellipse((cx - ww / 2 - 2.4 * s, y - hh / 2 - 2.4 * s, cx + ww / 2 + 2.4 * s, y + hh / 2 + 2.4 * s), fill=OUTLINE)
        d.ellipse((cx - ww / 2, y - hh / 2, cx + ww / 2, y + hh / 2), fill=BODY_DARK)
        d.ellipse((cx - ww / 2, y - hh / 2, cx + ww / 2, y + hh * 0.2), fill=BODY)
        d.ellipse((cx - ww * 0.3, y - hh * 0.36, cx - ww * 0.02, y - hh * 0.12), fill=SHINE)
    d.polygon([(cx - width * 0.07, tip_y + width * 0.12), (cx + width * 0.08, tip_y + width * 0.12), (cx + width * 0.085, tip_y - width * 0.0)], fill=BODY)


def corn(img, x, y, ang):
    k = Image.new("RGBA", (22 * SS, 18 * SS), (0, 0, 0, 0))
    kd = ImageDraw.Draw(k)
    kd.rounded_rectangle((2 * SS, 2 * SS, 20 * SS, 16 * SS), radius=5 * SS, fill=OUTLINE)
    kd.rounded_rectangle((4 * SS, 4 * SS, 18 * SS, 14 * SS), radius=4 * SS, fill=CORN)
    kd.ellipse((6 * SS, 5 * SS, 11 * SS, 8 * SS), fill=(250, 232, 150, 255))
    k = k.rotate(ang, resample=Image.BICUBIC, expand=True)
    k = k.resize((int(k.width * 0.55), int(k.height * 0.55)), Image.LANCZOS)
    img.alpha_composite(k, (int(x - k.width / 2), int(y - k.height / 2)))


def mud_pool(W=512, H=128, seed=7):
    rnd = random.Random(seed)
    w, h = W * SS, H * SS
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    cx, cy = w / 2, h * 0.5
    a, b = w * 0.46, h * 0.3
    thick = h * 0.12
    top = blob(cx, cy, a, b, random.Random(seed), lobes=6, amp=0.03)
    side = [(x, y + thick) for x, y in top]
    o = 3 * SS
    d.polygon(blob(cx, cy + thick, a + o, b + o, random.Random(seed), lobes=6, amp=0.03), fill=OUTLINE)
    d.polygon(blob(cx, cy, a + o, b + o, random.Random(seed), lobes=6, amp=0.03), fill=OUTLINE)
    d.polygon(side, fill=SIDE)
    d.polygon(top, fill=TOP)

    # разводы: тёмные и светлые дуги, как размешанная жижа
    mask = Image.new("L", (w, h), 0)
    ImageDraw.Draw(mask).polygon(top, fill=255)
    layer = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    ld = ImageDraw.Draw(layer)
    ld.polygon(blob(cx, cy + b * 0.62, a * 0.9, b * 0.48, random.Random(seed + 2), lobes=6, amp=0.03), fill=(76, 45, 17, 255))
    for _ in range(4):
        x = cx + rnd.uniform(-0.8, 0.8) * a
        y = cy + rnd.uniform(-0.4, 0.5) * b
        ld.polygon(blob(x, y, rnd.uniform(0.06, 0.12) * a, rnd.uniform(0.14, 0.26) * b, rnd, lobes=5, amp=0.2), fill=OLIVE)
    for _ in range(8):
        sx = cx + rnd.uniform(-0.75, 0.75) * a
        sy = cy + rnd.uniform(-0.55, 0.55) * b
        ra, rb = rnd.uniform(0.12, 0.3) * a, rnd.uniform(0.35, 0.7) * b
        a0 = rnd.uniform(0, 360)
        ld.arc((sx - ra, sy - rb, sx + ra, sy + rb), a0, a0 + rnd.uniform(90, 200), fill=DARK, width=int(rnd.uniform(3, 5.5) * SS))
    for _ in range(6):
        sx = cx + rnd.uniform(-0.8, 0.8) * a
        sy = cy + rnd.uniform(-0.7, 0.0) * b
        ra, rb = rnd.uniform(0.08, 0.2) * a, rnd.uniform(0.25, 0.5) * b
        a0 = rnd.uniform(180, 300)
        ld.arc((sx - ra, sy - rb, sx + ra, sy + rb), a0, a0 + rnd.uniform(40, 90), fill=LIGHT, width=int(rnd.uniform(2.5, 4) * SS))
    la = np.asarray(layer).copy()
    la[..., 3] = (la[..., 3].astype(np.float32) * 0.85 * (np.asarray(mask) / 255)).astype(np.uint8)
    img.alpha_composite(Image.fromarray(la))

    # мокрые блики
    for gx, gy, gw, a0, a1 in [(-0.58, -0.55, 0.14, 195, 255), (0.5, -0.62, 0.12, 190, 250), (-0.1, -0.8, 0.08, 200, 245),
                               (0.2, 0.1, 0.05, 190, 240), (-0.18, 0.28, 0.04, 190, 240)]:
        x, y = cx + gx * a, cy + gy * b
        d.arc((x - gw * a, y - gw * a * 0.3, x + gw * a, y + gw * a * 0.3), a0, a1, fill=GLOSS, width=int(2.2 * SS))

    # пузыри
    for bx, by, r in [(-0.26, 0.3, 4.5), (0.32, 0.42, 3.5), (0.12, -0.5, 3), (-0.62, 0.5, 3)]:
        x, y, rr = cx + bx * a, cy + by * b, r * SS
        d.ellipse((x - rr - 1.4 * SS, y - rr * 0.8 - 1.4 * SS, x + rr + 1.4 * SS, y + rr * 0.8 + 1.4 * SS), fill=OUTLINE)
        d.ellipse((x - rr, y - rr * 0.8, x + rr, y + rr * 0.8), fill=(112, 74, 32, 255))
        d.ellipse((x - rr * 0.55, y - rr * 0.6, x - rr * 0.05, y - rr * 0.2), fill=GLOSS)

    for kx, ky, ang in [(-0.5, 0.36, 20), (0.6, 0.12, -30), (0.26, -0.62, 60)]:
        corn(img, cx + kx * a, cy + ky * b, ang)

    # колбаски по краям (в середине стоит персонаж) и кучка сзади слева
    s = SS
    pile(img, cx - a * 0.6, cy - b * 0.32, 30 * s)
    sausage(img, (cx - a * 0.97, cy + b * 0.3), (cx - a * 0.9, cy - b * 0.1), (cx - a * 0.74, cy + b * 0.28), 7 * s, 5 * s)
    sausage(img, (cx + a * 0.56, cy - b * 0.4), (cx + a * 0.72, cy - b * 0.85), (cx + a * 0.88, cy - b * 0.25), 6.5 * s, 4.5 * s)
    sausage(img, (cx + a * 0.64, cy + b * 0.6), (cx + a * 0.76, cy + b * 0.25), (cx + a * 0.9, cy + b * 0.45), 5 * s, 3.8 * s)
    sausage(img, (cx - a * 0.52, cy + b * 0.72), (cx - a * 0.44, cy + b * 0.45), (cx - a * 0.34, cy + b * 0.64), 4.2 * s, 3.2 * s)
    return img.resize((W, H), Image.LANCZOS)


def fly(frame, W=48, H=40):
    w, h = W * SS, H * SS
    s = SS
    cx, cy = w * 0.5, h * 0.64
    wing = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    wd = ImageDraw.Draw(wing)
    lift = -10 if frame == 0 else -3
    for side in (-1, 1):
        x0 = cx + side * 2 * s
        if side > 0:
            box = (x0 - 2 * s, cy + (lift - 8) * s, x0 + 18 * s, cy + (lift + 3) * s)
        else:
            box = (x0 - 18 * s, cy + (lift - 8) * s, x0 + 2 * s, cy + (lift + 3) * s)
        wd.ellipse(box, fill=(40, 48, 60, 170))
        wd.ellipse((box[0] + 1.6 * s, box[1] + 1.6 * s, box[2] - 1.6 * s, box[3] - 1.6 * s), fill=(214, 234, 246, 150))
    body = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(body)
    d.ellipse((cx - 8 * s, cy - 5 * s, cx + 8 * s, cy + 8 * s), fill=(14, 14, 18, 255))
    d.ellipse((cx - 6.5 * s, cy - 3.5 * s, cx + 6.5 * s, cy + 6.5 * s), fill=(44, 46, 54, 255))
    for i in range(3):
        d.arc((cx - 6 * s, cy - 2 * s + i * 2.5 * s, cx + 6 * s, cy + 3 * s + i * 2.5 * s), 20, 160, fill=(18, 18, 22, 255), width=s)
    for side in (-1, 1):
        for k in range(3):
            y = cy + (1 + k * 3) * s
            d.line((cx + side * 6 * s, y, cx + side * 10 * s, y + 3 * s), fill=(14, 14, 18, 255), width=int(1.2 * s))
    d.ellipse((cx - 5 * s, cy - 11 * s, cx + 5 * s, cy - 2 * s), fill=(14, 14, 18, 255))
    for side in (-1, 1):
        ex = cx + side * 4 * s
        d.ellipse((ex - 4 * s, cy - 14 * s, ex + 4 * s, cy - 6 * s), fill=(14, 14, 18, 255))
        d.ellipse((ex - 3 * s, cy - 13 * s, ex + 3 * s, cy - 7 * s), fill=(176, 28, 24, 255))
        d.ellipse((ex - 1.8 * s, cy - 12.4 * s, ex - 0.2 * s, cy - 10.8 * s), fill=(255, 190, 170, 255))
    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    out.alpha_composite(wing)
    out.alpha_composite(body)
    return out.resize((W, H), Image.LANCZOS)


def stink(W=40, H=160):
    """Волнистая зелёная линия, прозрачная на концах."""
    w, h = W * SS, H * SS
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    t = yy / h
    center = w / 2 + (w * 0.26) * np.sin(2 * math.pi * (t * 1.6 + 0.1))
    dist = np.abs(xx - center)
    core = np.clip(1 - (dist - 2.6 * SS) / (1.2 * SS), 0, 1)
    edge = np.clip(1 - (dist - 4.2 * SS) / (1.2 * SS), 0, 1)
    fade = np.clip(np.sin(np.clip(t, 0, 1) * math.pi) * 1.6, 0, 1) ** 1.2
    rgb = np.zeros((h, w, 3), np.float32)
    rgb[:] = (78, 120, 28)
    rgb = rgb * (1 - core[..., None]) + np.array((168, 214, 84), np.float32) * core[..., None]
    alpha = np.maximum(edge * 0.9, core) * fade
    arr = np.dstack([rgb, alpha * 255]).astype(np.uint8)
    return Image.fromarray(arr, "RGBA").resize((W, H), Image.LANCZOS)


if __name__ == "__main__":
    mud_pool().save(os.path.join(OUT, "mud-pool.png"))
    fly(0).save(os.path.join(OUT, "fly-0.png"))
    fly(1).save(os.path.join(OUT, "fly-1.png"))
    stink().save(os.path.join(OUT, "stink.png"))
    print("ok")
