"""Кадры первого зала Клуба для ИИ-видео: `screens/references/city/video-frames/<имя>.png`.

    python scripts/club-live/video_frames.py [имя ...]

Без имён — все кадры; с именами — только они (например `bartender-start bartender-empty`).

Столы — живые слои `UI/Hub/club-live-<игра>.png` (квадрат 360 единиц в двойной чёткости, ровно то, что
видно в игре) поверх нынешнего зала, рядом `table-<игра>-empty` — тот же квадрат без жителей.
Остальное — квадраты мастер-кадра, увеличенные Real-ESRGAN (путь к exe — переменная ESRGAN).
Все кадры квадратные, 1440 px. Квадрат каждого кадра в единицах зала 1916×821 — FRAMES / таблицы:
по нему ролик потом встаёт на место.
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
out_dir = os.path.join(refs, 'video-frames')
esrgan = os.environ.get('ESRGAN', 'realesrgan-ncnn-vulkan')
work = os.path.join(tempfile.gettempdir(), 'club-video')
os.makedirs(out_dir, exist_ok=True)
os.makedirs(work, exist_ok=True)
SIDE = 1440
# Не столы: левый, верхний, сторона квадрата в единицах зала.
FRAMES = {
    'pianist': (1330, 250, 240),
    # Бармен (2026-10-04): первый и последний кадр всех его роликов; `-empty` — тот же квадрат пустого бара
    # (`club-hall-live-no-bartender.png`), по нему потом вырезается бармен.
    'bartender-start': (640, 245, 250),
    'bartender-empty': (640, 245, 250),
    'bouncer': (38, 300, 360),
}


def load(path, flags=cv2.IMREAD_COLOR):
    return cv2.imdecode(np.fromfile(path, np.uint8), flags)


def save(path, img):
    cv2.imencode('.png', img)[1].tofile(path)


master = load(os.path.join(refs, 'club-hall-live.png'))
plate = load(os.path.join(refs, 'club-hall-live-no-bartender.png'))
s = master.shape[1] / 1916
only = set(sys.argv[1:])
for name, (x, y, side) in FRAMES.items():
    if only and name not in only:
        continue
    crop = (plate if name.endswith('-empty') else master)[int(round(y * s)):int(round((y + side) * s)), int(round(x * s)):int(round((x + side) * s))]
    src, big = os.path.join(work, name + '.png'), os.path.join(work, name + '-x4.png')
    save(src, crop)
    subprocess.run([esrgan, '-i', src, '-o', big, '-n', 'realesr-animevideov3', '-s', '4'], check=True, capture_output=True)
    save(os.path.join(out_dir, name + '.png'), cv2.resize(load(big), (SIDE, SIDE), interpolation=cv2.INTER_AREA))
# Столы (2026-10-05): живой слой поверх нынешнего зала — RGB слоя вне его маски остался от ответа генератора
# (у «Верю — не верю» там старый бармен). `-empty` — пустой квадрат стола (`canvases/club-hall-live-table-<игра>.png`,
# по нему вклеивались жители): по нему потом вырезаются жители из ролика.
TABLES = {'durak': 500, 'bluff': 845, 'chests': 1197, 'govno': 1563}
for game, cx in TABLES.items():
    if only and 'table-' + game not in only:
        continue
    layer = load(os.path.join(hub, 'club-live-' + game + '.png'), cv2.IMREAD_UNCHANGED)
    n = layer.shape[0]
    x, y = cx - 180, 300
    crop = master[int(round(y * s)):int(round((y + 360) * s)), int(round(x * s)):int(round((x + 360) * s))]
    src, big = os.path.join(work, 'table-' + game + '.png'), os.path.join(work, 'table-' + game + '-x4.png')
    save(src, crop)
    subprocess.run([esrgan, '-i', src, '-o', big, '-n', 'realesr-animevideov3', '-s', '4'], check=True, capture_output=True)
    hall = cv2.resize(load(big), (n, n), interpolation=cv2.INTER_AREA).astype(np.float32)
    a = (layer[..., 3:4].astype(np.float32) / 255)
    frame = (layer[..., :3].astype(np.float32) * a + hall * (1 - a)).astype(np.uint8)
    save(os.path.join(out_dir, 'table-' + game + '.png'), cv2.resize(frame, (SIDE, SIDE), interpolation=cv2.INTER_CUBIC))
    empty = load(os.path.join(refs, 'canvases', 'club-hall-live-table-' + game + '.png'))
    save(os.path.join(out_dir, 'table-' + game + '-empty.png'), cv2.resize(empty, (SIDE, SIDE), interpolation=cv2.INTER_CUBIC))
print('ok')
