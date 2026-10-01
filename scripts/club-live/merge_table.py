"""Вклеить готовый стол (ответ генератора по квадрату стола) в живой первый зал Клуба.

    python scripts/club-live/merge_table.py <игра> <картинка генератора> [--patch-only]

Берётся только то, что изменилось около стола. Оно ложится в мастер-кадр
`screens/references/city/club-hall-live.png` (уменьшенное в пиксели зала) и живым слоем
`unity/.../Resources/UI/Hub/club-live-<игра>.png` в двойной чёткости (его ставит PlayerHubInterior.AddLiveTable).
Потом заново режутся квадраты столов, у которых живого слоя ещё нет.
Дальше: скопировать мастер в `Resources/UI/Hub/club-hall.png` и запустить `sync_bridge.py`.
Увеличение — Real-ESRGAN (`realesrgan-ncnn-vulkan`, модель `realesr-animevideov3`), путь к exe —
переменная окружения ESRGAN.
"""
import os
import subprocess
import sys
import tempfile

import cv2
import numpy as np

repo = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
refs = os.path.join(repo, 'screens', 'references', 'city')
hub = os.path.join(repo, 'unity', 'CardGameClient', 'Assets', 'Resources', 'UI', 'Hub')
esrgan = os.environ.get('ESRGAN', 'realesrgan-ncnn-vulkan')
work = os.path.join(tempfile.gettempdir(), 'club-live')
os.makedirs(work, exist_ok=True)
master_path = os.path.join(refs, 'club-hall-live.png')
# Оси столов в единицах картинки зала 1916×821 (PlayerHubInterior.ClubTableX).
TABLES = {'durak': 500, 'bluff': 845, 'chests': 1197, 'govno': 1563}
# Квадрат стола: 360×360 единиц от y 300 (PlayerHubInterior.LiveSquare, LiveTop).
SQUARE, TOP = 360, 300
game, image = sys.argv[1], sys.argv[2]


def load(path):
    return cv2.imdecode(np.fromfile(path, np.uint8), cv2.IMREAD_COLOR)


def save(path, img):
    cv2.imencode('.png', img)[1].tofile(path)


def square_path(name):
    return os.path.join(refs, 'canvases', 'club-hall-live-table-' + name + '.png')


def crop_box(name, s):
    left = TABLES[name] - SQUARE / 2
    return int(round(left * s)), int(round(TOP * s)), int(round(SQUARE * s))


master = load(master_path)
H, W = master.shape[:2]
s = W / 1916
ref = load(square_path(game))
new = cv2.resize(load(image), ref.shape[1::-1], interpolation=cv2.INTER_AREA)

# Подгонка по неизменившемуся фону.
sift = cv2.SIFT_create(8000)
k1, d1 = sift.detectAndCompute(cv2.cvtColor(ref, cv2.COLOR_BGR2GRAY), None)
k2, d2 = sift.detectAndCompute(cv2.cvtColor(new, cv2.COLOR_BGR2GRAY), None)
good = [m for m, n in cv2.BFMatcher().knnMatch(d2, d1, k=2) if m.distance < .7 * n.distance]
M, inliers = cv2.estimateAffinePartial2D(np.float32([k2[m.queryIdx].pt for m in good]),
                                         np.float32([k1[m.trainIdx].pt for m in good]), ransacReprojThreshold=3)
print('matches', len(good), 'inliers', int(inliers.sum()), 'scale %.4f' % np.hypot(M[0, 0], M[1, 0]))
warped = cv2.warpAffine(new, M, ref.shape[1::-1], flags=cv2.INTER_CUBIC, borderMode=cv2.BORDER_REPLICATE)

# Изменилось: жители и карты на сукне. Берём заметную разницу в пределах группы стола
# (стол со стульями и над ними место для сидящих), дырки внутри фигур заполняем.
n = ref.shape[0]
d = cv2.absdiff(cv2.GaussianBlur(warped, (7, 7), 0), cv2.GaussianBlur(ref, (7, 7), 0)).max(axis=2)
mask = (d > 34).astype(np.uint8)
mask = cv2.morphologyEx(mask, cv2.MORPH_OPEN, np.ones((5, 5), np.uint8))
mask = cv2.morphologyEx(mask, cv2.MORPH_CLOSE, np.ones((41, 41), np.uint8))
# Группа стола в квадрате: по ширине — стол со стульями и запас, по высоте — от y 330 единиц
# (выше — стена и бар) до пола под столом.
group = np.zeros_like(mask)
group[int(n * 30 / SQUARE):int(n * 340 / SQUARE), int(n * 5 / SQUARE):int(n * 355 / SQUARE)] = 1
mask &= group
count, labels, stats, _ = cv2.connectedComponentsWithStats(mask)
keep = np.zeros_like(mask)
for i in range(1, count):
    if stats[i, cv2.CC_STAT_AREA] >= 1500:
        keep[labels == i] = 1
flood = keep.copy() * 255
cv2.floodFill(flood, np.zeros((n + 2, n + 2), np.uint8), (0, 0), 128)
keep = ((flood != 128) | (keep > 0)).astype(np.uint8)
keep = cv2.dilate(keep, np.ones((13, 13), np.uint8))
print('changed share of the square: %.1f%%' % (100 * keep.mean()))

# Живой слой: тот же квадрат в двойной чёткости, прозрачный вне жителей и мебели (контур мебели —
# PlayerHubInterior.TableOutline): у стола всё одинаково чёткое. Потом на его место встанет видео.
half, top_u = 140, 624 - 280 * (560 / 720) * (487 / 560)
bottom_u = top_u + 280 * 560 / 720
cx_u = TABLES[game]
outline = [(cx_u - half * .42, bottom_u), (cx_u - half * .85, bottom_u - 30), (cx_u - half, top_u + 40),
           (cx_u - half * .7, top_u + 2), (cx_u + half * .7, top_u + 2), (cx_u + half, top_u + 40),
           (cx_u + half * .85, bottom_u - 30), (cx_u + half * .42, bottom_u)]
furniture = np.zeros_like(keep)
cv2.fillPoly(furniture, [np.int32([[(u - (cx_u - SQUARE / 2)) / SQUARE * n, (v - TOP) / SQUARE * n] for u, v in outline])], 1)
alpha = cv2.GaussianBlur(np.maximum(keep, furniture).astype(np.float32), (0, 0), 6)
patch = np.dstack([warped, (np.clip(alpha, 0, 1) * 255).astype(np.uint8)])
save(os.path.join(hub, 'club-live-' + game + '.png'), patch)
if '--patch-only' in sys.argv:
    sys.exit(0)

# Квадрат — это 360 единиц зала, увеличенные ×2; назад — в пиксели зала.
x0, y0, side = crop_box(game, s)
small = cv2.resize(warped, (side, side), interpolation=cv2.INTER_AREA).astype(np.float32)
soft = cv2.GaussianBlur(cv2.resize(keep.astype(np.float32), (side, side), interpolation=cv2.INTER_AREA), (0, 0), 3)
soft = np.clip(soft, 0, 1)[..., None]
region = master[y0:y0 + side, x0:x0 + side].astype(np.float32)
master[y0:y0 + side, x0:x0 + side] = (small * soft + region * (1 - soft)).astype(np.uint8)
save(master_path, master)

# Квадраты столов без живого слоя — заново из обновлённого зала (соседние квадраты чуть перекрываются).
for name in TABLES:
    if name == game or os.path.exists(os.path.join(hub, 'club-live-' + name + '.png')):
        continue
    cx, cy, cs = crop_box(name, s)
    src = os.path.join(work, 'crop-' + name + '.png')
    save(src, master[cy:cy + cs, cx:cx + cs])
    subprocess.run([esrgan, '-i', src, '-o', square_path(name), '-n', 'realesr-animevideov3', '-s', '2'],
                   check=True, capture_output=True)
print('ok')
