"""Вклеить жителя не за столом — в первый зал Клуба или в проход между залами (ответ генератора по квадрату).

    python scripts/club-live/merge_resident.py <имя> <картинка генератора>

Квадрат — `screens/references/city/canvases/club-<hall|bridge>-live-<имя>.png`, вырезан по BOXES и увеличен.
В зал изменившееся ложится прямо в мастер-кадр `club-hall-live.png` (и копируется в Resources), в проход —
слоем `screens/references/city/club-bridge-patch-<имя>.png` (размер прохода, прозрачный вне жителя): его
накладывает `sync_bridge.py` после переноса зала, так что житель переживает вклейку новых столов.
В конце сам запускает `sync_bridge.py`.
"""
import os
import shutil
import subprocess
import sys

import cv2
import numpy as np

repo = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
refs = os.path.join(repo, 'screens', 'references', 'city')
hub = os.path.join(repo, 'unity', 'CardGameClient', 'Assets', 'Resources', 'UI', 'Hub')
# Квадраты в единицах картинки 1916×821 (x прохода = x первого зала − 1105): где, левый, верхний, сторона.
# Вышибала сначала стоял у арки прохода, потом автор перевёл его к входным дверям зала (2026-09-30).
# Последнее — сдвиг жителя по x в единицах: генератор ставит его чуть не туда (у двери он задевал поле
# афиши, где код пишет «ИГРЫ» — PlayerHubSigns.FirstHallBoardTitle, x 298–394).
BOXES = {'bouncer': ('hall', 60, 300, 360, -22)}
# Контур жителя в квадрате (доли стороны): генератор чуть перерисовывает и фон рядом — берём только его самого.
OUTLINES = {'bouncer': [(.47, .15), (.64, .15), (.71, .2), (.74, .35), (.74, .52), (.71, .68), (.8, .85), (.8, .93),
                        (.3, .93), (.3, .82), (.39, .68), (.39, .52), (.36, .48), (.36, .35), (.4, .2)]}
name, image = sys.argv[1], sys.argv[2]


def load(path, flags=cv2.IMREAD_COLOR):
    return cv2.imdecode(np.fromfile(path, np.uint8), flags)


where, x0, y0, side, shift = BOXES[name]
ref = load(os.path.join(refs, 'canvases', 'club-' + where + '-live-' + name + '.png'))
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

# Изменилось: житель и его тень. Дырки внутри фигуры заполняем, мелочь отбрасываем.
n = ref.shape[0]
d = cv2.absdiff(cv2.GaussianBlur(warped, (7, 7), 0), cv2.GaussianBlur(ref, (7, 7), 0)).max(axis=2)
mask = (d > 34).astype(np.uint8)
mask = cv2.morphologyEx(mask, cv2.MORPH_OPEN, np.ones((5, 5), np.uint8))
mask = cv2.morphologyEx(mask, cv2.MORPH_CLOSE, np.ones((41, 41), np.uint8))
count, labels, stats, _ = cv2.connectedComponentsWithStats(mask)
keep = np.zeros_like(mask)
for i in range(1, count):
    if stats[i, cv2.CC_STAT_AREA] >= 1500:
        keep[labels == i] = 1
flood = keep.copy() * 255
cv2.floodFill(flood, np.zeros((n + 2, n + 2), np.uint8), (0, 0), 128)
keep = ((flood != 128) | (keep > 0)).astype(np.uint8)
keep = cv2.dilate(keep, np.ones((13, 13), np.uint8))
if name in OUTLINES:
    hull = np.zeros_like(keep)
    cv2.fillPoly(hull, [np.int32([[u * n, v * n] for u, v in OUTLINES[name]])], 1)
    keep &= hull
print('changed share of the square: %.1f%%' % (100 * keep.mean()))
if shift:
    move = np.float32([[1, 0, shift * n / side], [0, 1, 0]])
    warped = cv2.warpAffine(warped, move, (n, n), flags=cv2.INTER_CUBIC, borderMode=cv2.BORDER_REPLICATE)
    keep = cv2.warpAffine(keep, move, (n, n), flags=cv2.INTER_NEAREST)

if where == 'hall':
    master_path = os.path.join(refs, 'club-hall-live.png')
    master = load(master_path)
    s = master.shape[1] / 1916
    px, py, ps = int(round(x0 * s)), int(round(y0 * s)), int(round(side * s))
    small = cv2.resize(warped, (ps, ps), interpolation=cv2.INTER_AREA).astype(np.float32)
    soft = cv2.GaussianBlur(cv2.resize(keep.astype(np.float32), (ps, ps), interpolation=cv2.INTER_AREA), (0, 0), 2.5)
    soft = np.clip(soft, 0, 1)[..., None]
    region = master[py:py + ps, px:px + ps].astype(np.float32)
    master[py:py + ps, px:px + ps] = (small * soft + region * (1 - soft)).astype(np.uint8)
    cv2.imencode('.png', master)[1].tofile(master_path)
    shutil.copyfile(master_path, os.path.join(hub, 'club-hall.png'))
else:
    small = cv2.resize(warped, (side, side), interpolation=cv2.INTER_AREA)
    soft = cv2.GaussianBlur(cv2.resize(keep.astype(np.float32), (side, side), interpolation=cv2.INTER_AREA), (0, 0), 1.5)
    patch = np.zeros((821, 1916, 4), np.uint8)
    patch[y0:y0 + side, x0:x0 + side, :3] = small
    patch[y0:y0 + side, x0:x0 + side, 3] = (np.clip(soft, 0, 1) * 255).astype(np.uint8)
    cv2.imencode('.png', patch)[1].tofile(os.path.join(refs, 'club-bridge-patch-' + name + '.png'))
subprocess.run([sys.executable, os.path.join(os.path.dirname(__file__), 'sync_bridge.py')], check=True)
print('ok')
