"""Портрет Тузяры в раме слева от бара — из утверждённого эталона `characters/tuzyara-main.png`, а не из
генератора (у того портрет вышел кривым: карта завалена, лицо поплыло). Рама — из пустого зала, она ровная
и смотрит прямо на нас; внутрь неё — Тузяра по пояс на тёмно-бордовом фоне, в тоне зала.

    python scripts/club-live/portrait.py

Пишет в мастер-кадр `screens/references/city/club-hall-live.png` и в `Resources/UI/Hub/club-hall.png`.
"""
import os

import cv2
import numpy as np

repo = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
refs = os.path.join(repo, 'screens', 'references')
hub = os.path.join(repo, 'unity', 'CardGameClient', 'Assets', 'Resources', 'UI', 'Hub')
# Рама в единицах зала 1916×821: заведомо с запасом вокруг неё.
FRAME_BOX = (478, 200, 590, 346)


def load(path, flags=cv2.IMREAD_COLOR):
    return cv2.imdecode(np.fromfile(path, np.uint8), flags)


def save(path, img):
    cv2.imencode('.png', img)[1].tofile(path)


empty = load(os.path.join(refs, 'city', 'club-hall-empty-3296.png'))
master_path = os.path.join(refs, 'city', 'club-hall-live.png')
master = load(master_path)
s = master.shape[1] / 1916
x0, y0, x1, y1 = [int(round(v * s)) for v in FRAME_BOX]
frame = empty[y0:y1, x0:x1].copy()

# Внутреннее тёмное поле рамы: от середины идём в стороны, пока не начнётся светлая позолота.
gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY).astype(np.float32)
cy, cx = gray.shape[0] // 2, gray.shape[1] // 2
dark = np.median(gray[cy - 20:cy + 20, cx - 20:cx + 20]) + 25


def edge(line, start, step):
    i = start
    while 0 < i < len(line) - 1 and line[i] < dark:
        i += step
    return i


row, col = gray[cy - 10:cy + 10].mean(axis=0), gray[:, cx - 10:cx + 10].mean(axis=1)
left, right = edge(row, cx, -1) + 1, edge(row, cx, 1) - 1
top, bottom = edge(col, cy, -1) + 1, edge(col, cy, 1) - 1
w, h = right - left + 1, bottom - top + 1
print('inner field', left, top, w, h, 'aspect %.3f' % (w / h))

# Тузяра: пурпурный фон — в прозрачность, край без пурпурной каймы.
hero = load(os.path.join(refs, 'characters', 'tuzyara-main.png'))
b, g, r = [hero[..., i].astype(np.float32) for i in range(3)]
magenta = np.clip((np.minimum(r, b) - g - 60) / 80, 0, 1)
alpha = 1 - magenta
spill = np.minimum(r, b) - np.maximum(g, 0)
hero_f = hero.astype(np.float32)
fix = (alpha < .999)[..., None] & (spill > 40)[..., None]
hero_f = np.where(fix, np.repeat(hero_f[..., 1:2], 3, axis=2) * .5 + hero_f * .5, hero_f)
# Выпрямить: в эталоне карта наклонена (низ правее верха на ~5°) — для парадного портрета ставим ровно.
H, W = hero.shape[:2]
R = cv2.getRotationMatrix2D((W / 2, H / 2), -4.5, 1)
hero_f = cv2.warpAffine(hero_f, R, (W, H), flags=cv2.INTER_CUBIC, borderValue=(0, 0, 0))
alpha = cv2.warpAffine(alpha, R, (W, H), flags=cv2.INTER_LINEAR, borderValue=0)

# По пояс: от шляпы до середины трефы, по пропорции поля.
crop_top, crop_bottom = 30, 830
crop_h = crop_bottom - crop_top
crop_w = crop_h * w / h
crop_cx = 560
cl = int(round(crop_cx - crop_w / 2))
part = hero_f[crop_top:crop_bottom, cl:cl + int(round(crop_w))]
part_a = alpha[crop_top:crop_bottom, cl:cl + int(round(crop_w))]
part = cv2.resize(part, (w, h), interpolation=cv2.INTER_AREA)
part_a = cv2.resize(part_a, (w, h), interpolation=cv2.INTER_AREA)[..., None]

# Фон картины: тёмно-бордовый, светлее за головой, темнее к краям.
yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
glow = np.exp(-(((xx - w * .55) / (w * .55)) ** 2 + ((yy - h * .32) / (h * .5)) ** 2))
back = np.dstack([18 + 22 * glow, 16 + 18 * glow, 70 + 70 * glow])  # BGR
paint = part * part_a + back * (1 - part_a)
# В тоне зала: приглушить и согреть, как всё под тусклым светом бра, и чуть затемнить низ.
paint = paint * np.array([.72, .80, .90]) * (0.95 - .18 * (yy / h))[..., None]
# Тень от рамы на холсте по краям.
edge_shadow = np.minimum.reduce([xx, w - 1 - xx, yy, h - 1 - yy])
paint *= (0.55 + 0.45 * np.clip(edge_shadow / (w * .06), 0, 1))[..., None]

frame[top:top + h, left:left + w] = np.clip(paint, 0, 255).astype(np.uint8)
master[y0:y1, x0:x1] = frame
save(master_path, master)
save(os.path.join(hub, 'club-hall.png'), master)
print('ok')
