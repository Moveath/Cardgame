"""Картинка прохода между залами Клуба (1916×821, её x 0 = x 1105 первого зала) повторяет правую часть первого
зала и проступает поверх неё. Всё, что в первом зале поменялось относительно пустого (пианист, доска, столы,
жители), переносим в проход — иначе на стыке новое просвечивает призраком.

    python scripts/club-live/sync_bridge.py

Исходный проход — `screens/references/city/club-bridge-1-2-before-live.png`, пустой зал —
`club-hall-empty-3296.png` там же; новый зал — `Resources/UI/Hub/club-hall.png`.
"""
import os
import shutil

import cv2
import numpy as np

repo = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
hub = os.path.join(repo, 'unity', 'CardGameClient', 'Assets', 'Resources', 'UI', 'Hub')
refs = os.path.join(repo, 'screens', 'references', 'city')
BridgeX, Units = 1105, 1916


def load(path):
    return cv2.imdecode(np.fromfile(path, np.uint8), cv2.IMREAD_COLOR)


def save(path, img):
    cv2.imencode('.png', img)[1].tofile(path)


original = os.path.join(refs, 'club-bridge-1-2-before-live.png')
if not os.path.exists(original):
    shutil.copyfile(os.path.join(hub, 'club-bridge-1-2.png'), original)
bridge = load(original)
old = cv2.resize(load(os.path.join(refs, 'club-hall-empty-3296.png')), (Units, 821), interpolation=cv2.INTER_AREA)
new = cv2.resize(load(os.path.join(hub, 'club-hall.png')), (Units, 821), interpolation=cv2.INTER_AREA)
old_part, new_part = old[:, BridgeX:], new[:, BridgeX:]
d = cv2.absdiff(cv2.GaussianBlur(new_part, (5, 5), 0), cv2.GaussianBlur(old_part, (5, 5), 0)).max(axis=2)
mask = (d > 12).astype(np.uint8)
mask = cv2.morphologyEx(mask, cv2.MORPH_OPEN, np.ones((3, 3), np.uint8))
mask = cv2.dilate(cv2.morphologyEx(mask, cv2.MORPH_CLOSE, np.ones((15, 15), np.uint8)), np.ones((7, 7), np.uint8))
soft = np.clip(cv2.GaussianBlur(mask.astype(np.float32), (0, 0), 3), 0, 1)[..., None]
w = Units - BridgeX
region = bridge[:, :w].astype(np.float32)
bridge[:, :w] = (new_part.astype(np.float32) * soft + region * (1 - soft)).astype(np.uint8)
# Свои жители прохода (merge_bridge.py) — поверх зала.
for patch_name in sorted(os.listdir(refs)):
    if patch_name.startswith('club-bridge-patch-') and patch_name.endswith('.png'):
        patch = cv2.imdecode(np.fromfile(os.path.join(refs, patch_name), np.uint8), cv2.IMREAD_UNCHANGED)
        a = patch[..., 3:].astype(np.float32) / 255
        bridge = (patch[..., :3].astype(np.float32) * a + bridge.astype(np.float32) * (1 - a)).astype(np.uint8)
save(os.path.join(hub, 'club-bridge-1-2.png'), bridge)
print('changed share of the overlap: %.1f%%' % (100 * mask.mean()))
