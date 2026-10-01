"""Ролики жителя первого зала Клуба — в игру (ответы ИИ-видео по кадрам из `video_frames.py`).

    python scripts/club-live/video_clip.py <кадр>

<кадр> — имя из FRAMES (`pianist`, ...). Берёт последние версии роликов этого жителя
`screens/references/city/video-tests/<кадр>-<вид>-v<N>.mp4`, виды — play (петля), idle (петля), stop
(play → idle), start (idle → play); нужен хотя бы play. Нет stop, но есть start — stop делается из start задом наперёд. Каждый ролик подгоняется по цвету к мастер-кадру (фон в
игре — картинка зала), у петель конец плавно сводится к началу. Маска одна на все ролики жителя: всё, что
хоть в одном ролике отличается от картинки зала (у сидящей пианистки — и рука на клавишах из картинки), с мягким
краем. Пишет в `Resources/UI/Hub/Video/`: `club-<кадр>-<вид>.mp4` (H.264, все обрезаны по одной маске) и
`club-<кадр>-mask.png`, и печатает прямоугольник в единицах зала 1916×821 для PlayerHubInterior.ClubVideos.
ffmpeg — из пакета imageio-ffmpeg.
"""
import glob
import os
import re
import subprocess
import sys

import cv2
import imageio_ffmpeg
import numpy as np

repo = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
refs = os.path.join(repo, 'screens', 'references', 'city')
tests = os.path.join(refs, 'video-tests')
out_dir = os.environ.get('CLUB_VIDEO_OUT') or os.path.join(repo, 'unity', 'CardGameClient', 'Assets', 'Resources', 'UI', 'Hub', 'Video')
os.makedirs(out_dir, exist_ok=True)
# Квадраты кадров — как в video_frames.py (левый, верхний, сторона в единицах зала).
FRAMES = {'pianist': (1330, 250, 240), 'bartender': (635, 290, 240), 'bouncer': (38, 300, 360)}
# Где искать движение (доли кадра): соседей, попавших в кадр, и угол со знаком генератора не берём.
AREAS = {'pianist': (0, 0, .78, .9)}
LOOPS, KINDS = ('play', 'idle'), ('play', 'idle', 'stop', 'start')
FADE = 8  # кадров сведения: конца петли к началу, краёв перехода к кадрам петель
WORK = 768  # общий размер кадра роликов (квадрат)
LOOP_BLEND = 3  # кадров сведения у петли с подобранной точкой
# Какие кадры исходника брать (до разворота stop): генераторы любят медленно «доигрывать» после движения.
# Пианистка (2026-10-01, по замечаниям автора): start-v2 — до кадра 84 (руки легли на клавиши, дальше медленная
# игра — её заменяет петля play); stop — тот же start-v2 задом наперёд, кадры 0–61: руки уходят с клавиш,
# разводятся и складываются, без медленной игры в начале.
TRIMS = {('pianist', 'start'): (0, 84), ('pianist', 'stop'): (0, 62)}
frame_name = sys.argv[1]


def load(path):
    return cv2.imdecode(np.fromfile(path, np.uint8), cv2.IMREAD_COLOR)


def latest(kind):
    found = []
    for path in glob.glob(os.path.join(tests, frame_name + '-' + kind + '-v*.mp4')):
        match = re.search(r'-v(\d+)\.mp4$', path)
        if match:
            found.append((int(match.group(1)), path))
    return max(found)[1] if found else None


def read(path):
    capture = cv2.VideoCapture(path)
    frames = []
    while True:
        ok, frame = capture.read()
        if not ok:
            break
        # Сервисы отдают разный размер (Hailuo 768, Grok 560): все ролики жителя — к одному, под общую маску.
        if frame.shape[0] != WORK or frame.shape[1] != WORK:
            frame = cv2.resize(frame, (WORK, WORK), interpolation=cv2.INTER_CUBIC if frame.shape[0] < WORK else cv2.INTER_AREA)
        frames.append(frame.astype(np.float32))
    return frames, capture.get(cv2.CAP_PROP_FPS) or 24


clips = {kind: latest(kind) for kind in KINDS}
clips = {kind: path for kind, path in clips.items() if path}
# stop можно взять из ролика «начинает» задом наперёд (решение автора, 2026-10-01): `<кадр>-stop-rev-v<N>.mp4`.
# Нет ни своего stop, ни такого — stop = нынешний start задом наперёд.
reversed_stop = None
if 'stop' not in clips:
    reversed_stop = latest('stop-rev') or clips.get('start')
    if reversed_stop:
        clips['stop'] = reversed_stop
assert 'play' in clips, 'нужен хотя бы ролик play'
x, y, side = FRAMES[frame_name]
master = load(os.path.join(refs, 'club-hall-live.png'))
s = master.shape[1] / 1916
square = master[int(round(y * s)):int(round((y + side) * s)), int(round(x * s)):int(round((x + side) * s))]
size = WORK
still = cv2.resize(square, (size, size), interpolation=cv2.INTER_AREA).astype(np.float32)
area = np.zeros((size, size), np.uint8)
l, t, r, b = AREAS.get(frame_name, (0, 0, 1, 1))
area[int(t * size):int(b * size), int(l * size):int(r * size)] = 1
data = {}
entries = {}
blended = {}  # сколько последних кадров петли — сведение (в них после перехода не входим)
for kind in KINDS:
    if kind not in clips:
        continue
    path = clips[kind]
    frames, fps = read(path)
    if (frame_name, kind) in TRIMS:
        a0, a1 = TRIMS[frame_name, kind]
        frames = frames[a0:a1]
    if kind == 'stop' and path == reversed_stop:
        frames = frames[::-1]
    # Цвет — к мастер-кадру по фону: пиксели, где кадр и картинка зала почти совпадают.
    near = (np.abs(frames[0] - still).max(axis=2) < 40) & (area > 0)
    gain = np.float32([np.polyfit(frames[0][..., c][near], still[..., c][near], 1) for c in range(3)])
    frames = [frame * gain[:, 0] + gain[:, 1] for frame in frames]
    # Паузы, где житель замер (генераторы держат позу по секунде-две, чаще в начале и в конце), — вырезаем,
    # оставив пару кадров: замершие кадры одинаковые, склейка не видна. Иначе на стыке житель «доиграл, замер,
    # рывком пошёл дальше».
    energy = [float(np.mean(cv2.GaussianBlur(np.abs(frames[i + 1] - frames[i]).max(axis=2), (5, 5), 0)[area > 0]))
              for i in range(len(frames) - 1)]
    still_step = .3 * float(np.median(energy))
    drop = set()
    i = 0
    while i < len(energy):
        j = i
        while j < len(energy) and energy[j] < still_step:
            j += 1
        if j - i >= 6:
            drop.update(range(i + 2, j + 1))
        i = max(j, i + 1)
    drop.discard(0)
    trimmed = len(drop)
    frames = [frame for i, frame in enumerate(frames) if i not in drop]
    n = len(frames)
    if kind == 'play':
        # Петля без замедления (только «играет»; «сидит» и должна затихать): генераторы к концу затихают и встают в позу первого кадра — житель «доиграл,
        # замер, заиграл снова». Ищем два места — в начале и ближе к концу, но до затихания, — где поза и движение
        # (три кадра подряд) совпадают, и зацикливаем отрезок между ними.
        area_small = cv2.resize(area, (96, 96), interpolation=cv2.INTER_NEAREST)
        sf = [cv2.resize(f, (96, 96), interpolation=cv2.INTER_AREA)[area_small > 0] for f in frames]
        steps = [float(np.mean(np.abs(sf[k + 1] - sf[k]))) for k in range(n - 1)]
        lively = .5 * float(np.median(steps))
        last = n - 1
        while last > n // 2 and np.mean(steps[max(0, last - 6):last]) < lively:
            last -= 1
        best = None
        for i in range(FADE, n // 3):
            for j in range(max(i + n // 2, n // 2), last - 2):
                cost = sum(float(np.mean(np.abs(sf[i + k] - sf[j + k]))) for k in range(3))
                cost += .3 * (n - (j - i)) / n  # при равенстве — длиннее петля
                if best is None or cost < best[0]:
                    best = (cost, i, j)
        if best is not None:
            _, i, j = best
            print(kind, 'loop', i, '..', j, 'of', n, 'cost %.2f' % best[0])
            # Конец петли перетекает в кадры, что идут прямо перед её началом: движение продолжается, не двоится.
            source = frames
            frames = source[i:j]
            n = len(frames)
            # Точка совпадения подобрана, сводить надо чуть-чуть: длинное сведение разных фаз движения двоит руки.
            for k in range(1, LOOP_BLEND + 1):
                w = (LOOP_BLEND - k + 1) / (LOOP_BLEND + 1)
                frames[n - k] = source[j - k] * (1 - w) + source[i - k] * w
            blended[kind] = LOOP_BLEND
        else:
            first = frames[0]
            frames = [frame * (1 - k) + first * k if (k := (i - (n - FADE) + 1) / (FADE + 1)) > 0 else frame
                      for i, frame in enumerate(frames)]
    elif kind in LOOPS:
        # Петля: конец плавно сводится к началу.
        first = frames[0]
        frames = [frame * (1 - k) + first * k if (k := (i - (n - FADE) + 1) / (FADE + 1)) > 0 else frame
                  for i, frame in enumerate(frames)]
    else:
        # Переход: начало — ровно кадр петли, откуда идём, конец — ровно кадр петли, куда приходим (стык без шва).
        begin, end = ('play', 'idle') if kind == 'stop' else ('idle', 'play')
        if begin in data and end in data:
            a, loop = data[begin][0][0], data[end][0]
            frames = [frame * (1 - k) + a * k if (k := 1 - i / FADE) > 0 else frame for i, frame in enumerate(frames)]
            # Конец перехода — не обязательно его последний кадр: ищем место во второй половине перехода и кадр
            # петли, где поза и движение (три кадра подряд) ближе всего, режем переход там и входим в петлю с этого
            # кадра. Иначе «начинает» в конце замедляется, а петля с начала идёт быстро — «медленно, пауза, быстро».
            small = lambda f: cv2.resize(f, (96, 96), interpolation=cv2.INTER_AREA)[area_small > 0]
            area_small = cv2.resize(area, (96, 96), interpolation=cv2.INTER_NEAREST)
            tf, lf = [small(f) for f in frames], [small(f) for f in loop]
            # Подбор и по скорости рук пробовали (2026-10-01) — переход обрывался раньше, руки не успевали лечь
            # на клавиши; автор вернул подбор только по позе.
            best = None
            for t in range(max(3, n // 2), n):
                for q in range(len(loop) - blended.get(end, FADE) - 1):
                    cost = sum(float(np.mean(np.abs(tf[t - k] - lf[(q - k) % len(loop)]))) for k in range(3))
                    cost += .5 * (n - 1 - t) / n  # при равенстве — дольше оставить сам переход
                    if best is None or cost < best[0]:
                        best = (cost, t, q)
            _, t, q = best
            frames = frames[:t + 1]
            n = len(frames)
            # Конец не склеиваем с кадром петли: в игре оба ролика идут, пока один проявляется поверх другого.
            entries[end] = (q + 1) % len(loop)
            print(kind, 'cut at', t + 1, 'of', len(tf), '-> enters', end, 'at frame', entries[end], 'cost %.2f' % best[0])
    # Отличие от картинки зала за весь ролик — то, что маска должна показать.
    diff = np.zeros((size, size), np.float32)
    for frame in frames:
        diff = np.maximum(diff, cv2.GaussianBlur(np.abs(frame - still).max(axis=2), (9, 9), 0))
    data[kind] = (frames, fps, diff)
    print(kind, os.path.basename(path), '(reversed)' if kind == 'stop' and path == reversed_stop else '', n, 'frames',
          'trimmed', trimmed,
          'gain', np.round(gain[:, 0], 3))

baked_landing = frame_name == 'pianist' and 'start' in data and 'play' in data
if baked_landing:
    from bake_pianist_landing import bake_landing
    start_frames, start_fps, start_diff = data['start']
    play_frames, play_fps, _ = data['play']
    if abs(start_fps - play_fps) > .001:
        raise ValueError('Pianist landing requires matching frame rates')
    start_frames, entries['play'] = bake_landing(start_frames, play_frames, start_fps, entries['play'])
    data['start'] = (start_frames, start_fps, start_diff)

size = area.shape[0]
mask = (np.max([d[2] for d in data.values()], axis=0) > 24).astype(np.uint8) & area
mask = cv2.morphologyEx(mask, cv2.MORPH_OPEN, np.ones((5, 5), np.uint8))
mask = cv2.morphologyEx(mask, cv2.MORPH_CLOSE, np.ones((31, 31), np.uint8))
count, labels, stats, _ = cv2.connectedComponentsWithStats(mask)
keep = np.zeros_like(mask)
for i in range(1, count):
    if stats[i, cv2.CC_STAT_AREA] >= size * size // 400:
        keep[labels == i] = 1
flood = keep * 255
cv2.floodFill(flood, np.zeros((size + 2, size + 2), np.uint8), (0, 0), 128)
keep = ((flood != 128) | (keep > 0)).astype(np.uint8)
keep = cv2.dilate(keep, np.ones((25, 25), np.uint8))
soft = np.clip(cv2.GaussianBlur(keep.astype(np.float32), (0, 0), 7), 0, 1)

# Прямоугольник — по маске, с чётными сторонами (H.264), один на все ролики.
ys, xs = np.nonzero(soft > .01)
x0, y0, x1, y1 = xs.min(), ys.min(), xs.max() + 1, ys.max() + 1
if (x1 - x0) % 2: x1, x0 = (x1 + 1, x0) if x1 < size else (x1, x0 - 1)
if (y1 - y0) % 2: y1, y0 = (y1 + 1, y0) if y1 < size else (y1, y0 - 1)
w, h = x1 - x0, y1 - y0
for kind, (frames, fps, _) in data.items():
    path = os.path.join(out_dir, 'club-' + frame_name + '-' + kind + '.mp4')
    ffmpeg = subprocess.Popen([imageio_ffmpeg.get_ffmpeg_exe(), '-y', '-loglevel', 'error', '-f', 'rawvideo',
                               '-pix_fmt', 'bgr24', '-s', '%dx%d' % (w, h), '-r', str(fps), '-i', '-', '-an',
                               '-c:v', 'libx264', '-profile:v', 'main', '-pix_fmt', 'yuv420p', '-crf', '20',
                               '-preset', 'slow', '-movflags', '+faststart', path], stdin=subprocess.PIPE)
    for frame in frames:
        ffmpeg.stdin.write(np.clip(frame, 0, 255).astype(np.uint8)[y0:y1, x0:x1].tobytes())
    ffmpeg.stdin.close()
    ffmpeg.wait()
    print(kind, 'kb', os.path.getsize(path) // 1024)
cv2.imencode('.png', (soft[y0:y1, x0:x1] * 255).astype(np.uint8))[1].tofile(os.path.join(out_dir, 'club-' + frame_name + '-mask.png'))
# С какого кадра петля начинается после перехода (HubClubVideo читает `club-<кадр>-entry`).
with open(os.path.join(out_dir, 'club-' + frame_name + '-entry.txt'), 'w', newline='\n') as f:
    f.write(''.join('%s %d\n' % item for item in sorted(entries.items())))
    if baked_landing:
        f.write('landing baked\n')
unit = side / size
print('size', w, h)
print('rect in hall units: x %.1f y %.1f w %.1f h %.1f' % (x + x0 * unit, y + y0 * unit, w * unit, h * unit))
