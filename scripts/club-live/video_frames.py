"""Кадры первого зала Клуба для ИИ-видео: `screens/references/city/video-frames/<имя>.png`.

    python scripts/club-live/video_frames.py

Столы — RGB живых слоев `UI/Hub/club-live-<игра>.png` (квадрат 360 единиц в двойной чёткости, ровно то, что
видно в игре). Остальное — квадраты мастер-кадра, увеличенные Real-ESRGAN (путь к exe — переменная ESRGAN).
Все кадры квадратные, 1440 px. Квадрат каждого кадра в единицах зала 1916×821 — FRAMES / таблицы:
по нему ролик потом встаёт на место.
"""
import os
import subprocess
import tempfile

import cv2
import numpy as np

repo = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
refs = os.path.join(repo, 'screens', 'references', 'city')
hub = os.path.join(repo, 'unity', 'CardGameClient', 'Assets', 'Resources', 'UI', 'Hub')
out_dir = os.path.join(refs, 'video-frames')
esrgan = os.environ.get('ESRGAN', 'realesrgan-ncnn-vulkan')
work = os.path.join(tempfile.gettempdir(), 'club-video')
os.makedirs(out_dir, exist_ok=True)
os.makedirs(work, exist_ok=True)
SIDE = 1440
# Не столы: левый, верхний, сторона квадрата в единицах зала.
FRAMES = {
    'pianist': (1330, 250, 240),
    'bartender': (635, 290, 240),
    'bouncer': (38, 300, 360),
}


def load(path, flags=cv2.IMREAD_COLOR):
    return cv2.imdecode(np.fromfile(path, np.uint8), flags)


def save(path, img):
    cv2.imencode('.png', img)[1].tofile(path)


master = load(os.path.join(refs, 'club-hall-live.png'))
s = master.shape[1] / 1916
for name, (x, y, side) in FRAMES.items():
    crop = master[int(round(y * s)):int(round((y + side) * s)), int(round(x * s)):int(round((x + side) * s))]
    src, big = os.path.join(work, name + '.png'), os.path.join(work, name + '-x4.png')
    save(src, crop)
    subprocess.run([esrgan, '-i', src, '-o', big, '-n', 'realesr-animevideov3', '-s', '4'], check=True, capture_output=True)
    save(os.path.join(out_dir, name + '.png'), cv2.resize(load(big), (SIDE, SIDE), interpolation=cv2.INTER_AREA))
for game in ('durak', 'bluff', 'chests', 'govno'):
    layer = load(os.path.join(hub, 'club-live-' + game + '.png'), cv2.IMREAD_UNCHANGED)[..., :3]
    save(os.path.join(out_dir, 'table-' + game + '.png'), cv2.resize(layer, (SIDE, SIDE), interpolation=cv2.INTER_CUBIC))
print('ok')
