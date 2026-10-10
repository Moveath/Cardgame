"""Жест жителей за столом Клуба — в игру (2026-10-06, первым — «Сундучки»).

    python scripts/club-live/prepare_table_clips.py <игра> <ролик> [--fade-in 6] [--fade-out 14]
    python scripts/club-live/prepare_table_clips.py durak <ролик> --loop

Ролик Grok снят с кадра `video-frames/table-<игра>.png` (живой слой стола поверх зала, квадрат 360 единиц от y 300).
По умолчанию подготавливается отдельный жест: play — неподвижная заготовка живого слоя,
а action — жест, который проявляется из него и в него же гаснет (`HubClubVideo.SetupVariation`:
action начинается и кончается одинаковыми кадрами петли на время стыка). В каждом кадре от ролика берутся только жители
и то, что сдвинулось (рука, карта): отличие от «подложки» — живого слоя, где сами жители заменены пустым квадратом
стола `video-frames/table-<игра>-empty.png`. Всё неподвижное — стол, стулья, стопки карт на сукне — остаётся
пикселями живого слоя, поэтому не рябит и на краю маски нет шва. Цвет ролика — к живому слою.
Пишет в `Resources/UI/Hub/Video/` `club-<игра>-play.mp4`, `club-<игра>-action.mp4`, `club-<игра>-mask.png`, превью
в `screens/qa/table-clips/<игра>/` и печатает прямоугольник для PlayerHubInterior.TableVideos.
С --loop выбранный ролик становится петлёй play; action не создаётся.
--loop-mode mirror подходит только для спокойного ожидания: исходные кадры идут туда и обратно,
без деформации глаз и смешивания разных лиц. Начало/конец выбираются в спокойной позе.
--loop-mode flow совмещает контуры хвоста/начала; требует отдельной проверки мимики крупным планом.
CLUB_VIDEO_OUT позволяет сначала писать в QA-папку, затем установить проверенные play и mask с их rect.

Для «Сундучков» после этой подготовки запустить animate_chests_idle.py --action <новый action>:
он заменяет неподвижную заготовку play программной живой петлёй, согласует движущиеся края action
и сохраняет результат для просмотра. После проверки установить через --install.
"""
import argparse
import json
import os
import subprocess
from pathlib import Path

import cv2
import imageio_ffmpeg
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
REFS = ROOT / 'screens/references/city'
OUT = Path(os.environ.get('CLUB_VIDEO_OUT') or ROOT / 'unity/CardGameClient/Assets/Resources/UI/Hub/Video')
TABLES = {'durak': 500, 'bluff': 845, 'chests': 1197, 'govno': 1563}  # оси столов, как в video_frames.py
TOP, SIDE = 300, 360  # квадрат стола в единицах зала 1916×821
WORK = 1152  # 3,2 px на единицу зала, как у пианистки и бармена
BLEND_SECONDS = 15 / 48  # стык петли и жеста в игре (SetupVariation): 8 кадров при 24 к/с, 10 — при 30
STILL_FRAMES = 48  # «петля» play — 2 с неподвижного кадра
FG = 40  # порог «житель или сдвинувшееся, а не подложка» (размытое отличие, 0–255)
MOVED = 30  # порог «здесь в ролике что-то двигалось» (размытое отличие от первого кадра)
REACH = 61  # px WORK: запас вокруг двигавшегося
RESIDENT_AREA = 15000  # пятна отличия живого слоя от пустого стола крупнее этого — жители, мельче — карты на сукне


def load(path, flags=cv2.IMREAD_COLOR):
    return cv2.imdecode(np.fromfile(str(path), np.uint8), flags)


def save(path, img):
    cv2.imencode('.png', img)[1].tofile(str(path))


def write_video(path, frames, size, fps):
    ffmpeg = subprocess.Popen([imageio_ffmpeg.get_ffmpeg_exe(), '-y', '-loglevel', 'error', '-f', 'rawvideo',
                               '-pix_fmt', 'bgr24', '-s', '%dx%d' % size, '-r', str(fps), '-i', '-', '-an',
                               '-c:v', 'libx264', '-profile:v', 'main', '-pix_fmt', 'yuv420p', '-crf', '20',
                               '-preset', 'slow', '-movflags', '+faststart', str(path)], stdin=subprocess.PIPE)
    for frame in frames:
        ffmpeg.stdin.write(np.ascontiguousarray(np.clip(frame, 0, 255).astype(np.uint8)).tobytes())
    ffmpeg.stdin.close()
    if ffmpeg.wait() != 0:
        raise RuntimeError('Video encoding failed: ' + str(path))


def frames_of(path):
    """Кадры ролика по одному (все сразу в 1152² — это гигабайты)."""
    cap = cv2.VideoCapture(str(path))
    while True:
        ok, f = cap.read()
        if not ok:
            break
        yield cv2.resize(f, (WORK, WORK), interpolation=cv2.INTER_AREA)
    cap.release()


def fill_holes(mask):
    flood = mask * 255
    cv2.floodFill(flood, np.zeros((mask.shape[0] + 2, mask.shape[1] + 2), np.uint8), (0, 0), 128)
    return ((flood != 128) | (mask > 0)).astype(np.uint8)


def loop_frames(frames, overlap):
    """Forward-only cycle. Match moving outlines across the join before blending colours.

    The last overlap frames meet the first overlap frames; after the join playback
    continues at source frame `overlap`. No frozen padding or reverse card gestures.
    """
    yield from frames[overlap:-overlap]
    h, w = frames[0].shape[:2]
    yy, xx = np.mgrid[:h, :w].astype(np.float32)
    flow_size = (max(16, w // 2), max(16, h // 2))
    def gray(f):
        return cv2.resize(cv2.cvtColor(f, cv2.COLOR_BGR2GRAY), flow_size)
    def flow(a, b):
        field = cv2.DISOpticalFlow_create(cv2.DISOPTICAL_FLOW_PRESET_MEDIUM).calc(gray(a), gray(b), None)
        field = cv2.resize(field, (w, h), interpolation=cv2.INTER_LINEAR)
        field[..., 0] *= w / flow_size[0]
        field[..., 1] *= h / flow_size[1]
        return field
    for i in range(overlap):
        a, b = frames[-overlap + i], frames[i]
        t = i / (overlap - 1)
        t = t*t*(3-2*t)
        if i == 0:
            yield a
            continue
        if i == overlap - 1:
            yield b
            continue
        ab, ba = flow(a, b), flow(b, a)
        aa = cv2.remap(a, xx - t*ab[..., 0], yy - t*ab[..., 1], cv2.INTER_CUBIC,
                       borderMode=cv2.BORDER_REPLICATE)
        bb = cv2.remap(b, xx - (1-t)*ba[..., 0], yy - (1-t)*ba[..., 1], cv2.INTER_CUBIC,
                       borderMode=cv2.BORDER_REPLICATE)
        yield np.rint(aa.astype(np.float32)*(1-t) + bb.astype(np.float32)*t).astype(np.uint8)


def main():
    p = argparse.ArgumentParser()
    p.add_argument('game', choices=sorted(TABLES))
    p.add_argument('video')
    p.add_argument('--fade-in', type=int, default=6, help='кадров проявления жеста из неподвижного слоя')
    p.add_argument('--fade-out', type=int, default=14, help='кадров угасания жеста в неподвижный слой')
    p.add_argument('--loop', action='store_true', help='непрерывный play из самого видео, без неподвижных кадров/action')
    p.add_argument('--loop-mode', choices=('flow', 'mirror'), default='flow')
    p.add_argument('--loop-start', type=int, default=0, help='первый исходный кадр спокойной петли')
    p.add_argument('--loop-end', type=int, help='последний исходный кадр спокойной петли, включительно')
    p.add_argument('--loop-blend', type=float, default=.75, help='секунд согласованного перехода конца петли к началу')
    p.add_argument('--qa-out', type=Path, help='отдельная папка просмотра, чтобы сохранить прежнюю петлю')
    a = p.parse_args()
    OUT.mkdir(parents=True, exist_ok=True)
    qa = a.qa_out or ROOT / 'screens/qa/table-clips' / a.game
    qa.mkdir(parents=True, exist_ok=True)
    F = lambda f: f.astype(np.float32)

    still = F(cv2.resize(load(REFS / 'video-frames' / ('table-%s.png' % a.game)), (WORK, WORK), interpolation=cv2.INTER_AREA))
    empty = F(cv2.resize(load(REFS / 'video-frames' / ('table-%s-empty.png' % a.game)), (WORK, WORK), interpolation=cv2.INTER_AREA))

    # Жители на живом слое: крупные пятна отличия от пустого стола. Мелкие — карты на сукне, они остаются слоем.
    diff = (cv2.GaussianBlur(np.abs(still - empty).max(axis=2), (9, 9), 0) > FG).astype(np.uint8)
    diff = cv2.morphologyEx(diff, cv2.MORPH_CLOSE, np.ones((15, 15), np.uint8))
    count, labels, stats, _ = cv2.connectedComponentsWithStats(diff)
    big = [c for c in range(1, count) if stats[c, cv2.CC_STAT_AREA] >= RESIDENT_AREA]
    residents = cv2.dilate(fill_holes(np.isin(labels, big).astype(np.uint8)), np.ones((9, 9), np.uint8))
    print('residents on the live layer: %d blobs, %.1f%% of the square' % (len(big), 100 * residents.mean()))

    # Выравнивание по неподвижному: всё, кроме жителей и места вокруг них.
    bg = (1 - cv2.dilate(residents, np.ones((61, 61), np.uint8))).astype(np.uint8)
    half = WORK // 2
    gray_half = lambda f: cv2.resize(cv2.cvtColor(np.clip(f, 0, 255).astype(np.uint8), cv2.COLOR_BGR2GRAY),
                                     (half, half), interpolation=cv2.INTER_AREA).astype(np.float32) / 255
    template = gray_half(still)
    bg_half = cv2.resize(bg, (half, half), interpolation=cv2.INTER_NEAREST)

    cap = cv2.VideoCapture(a.video)
    fps = cap.get(cv2.CAP_PROP_FPS) or 24
    cap.release()
    warps, warp = [], np.eye(2, 3, dtype=np.float32)
    for f in frames_of(a.video):
        try:
            _, warp = cv2.findTransformECC(template, gray_half(f), warp.copy(), cv2.MOTION_AFFINE,
                                           (cv2.TERM_CRITERIA_EPS | cv2.TERM_CRITERIA_COUNT, 100, 1e-6), bg_half, 3)
        except cv2.error:
            pass
        warps.append(warp.copy())
    warps = np.array(warps)
    warps = np.array([np.median(warps[max(0, i - 3):i + 4], axis=0) for i in range(len(warps))])
    warps[:, :, 2] *= WORK / half
    n = len(warps)
    overlap = int(np.ceil(fps * BLEND_SECONDS - 1e-6))  # одинаковых кадров слоя на стыках
    print(n, 'frames', fps, 'fps; shift x %.2f..%.2f y %.2f..%.2f, scale %.4f..%.4f' % (
        warps[:, 0, 2].min(), warps[:, 0, 2].max(), warps[:, 1, 2].min(), warps[:, 1, 2].max(),
        warps[:, 0, 0].min(), warps[:, 0, 0].max()))

    gain = None

    def lit_frames():
        """Кадры ролика, выровненные по слою и в его цвете."""
        nonlocal gain
        for f, w in zip(frames_of(a.video), warps):
            f = F(cv2.warpAffine(f, w, (WORK, WORK), flags=cv2.INTER_CUBIC | cv2.WARP_INVERSE_MAP,
                                 borderMode=cv2.BORDER_REPLICATE))
            if gain is None:
                near = (np.abs(f - still).max(axis=2) < 40) & (bg > 0)
                gain = np.float32([np.polyfit(f[..., c][near], still[..., c][near], 1) for c in range(3)])
                print('colour gain', np.round(gain[:, 0], 3), 'offset', np.round(gain[:, 1], 1))
            yield np.clip(f * gain[:, 0] + gain[:, 1], 0, 255)

    # Мягкая маска в каждом кадре: отличие от подложки, крупные пятна без дыр, ±1 кадр по времени, мягкий край.
    # Только там, где в ролике что-то двигается (руки, лица, карта), с запасом: стол и пустой стул Grok тоже чуть
    # перерисовывает, но они стоят — их оставляем пикселями живого слоя.
    first, change = None, None
    for f in lit_frames():
        if first is None:
            first = f
            change = np.zeros(f.shape[:2], np.float32)
        change = np.maximum(change, cv2.GaussianBlur(np.abs(f - first).max(axis=2), (9, 9), 0))
    moved = (change > MOVED).astype(np.uint8)
    moved = cv2.morphologyEx(moved, cv2.MORPH_OPEN, np.ones((7, 7), np.uint8))
    count, labels, stats, _ = cv2.connectedComponentsWithStats(moved)
    moved = np.isin(labels, [c for c in range(1, count) if stats[c, cv2.CC_STAT_AREA] >= 2000]).astype(np.uint8)
    zone = cv2.dilate(fill_holes(moved), cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (REACH, REACH)))
    print('moving zone: %.1f%% of the square' % (100 * zone.mean()))
    # Подложка — живой слой, а там, где житель двигался, — пустой стол: место, откуда ушла рука, — это фон.
    r = cv2.GaussianBlur((residents & zone).astype(np.float32), (0, 0), 2)[..., None]
    plate = still * (1 - r) + empty * r
    save(qa / 'moving.png', zone * 255)
    del first
    clean = []
    for f in lit_frames():
        m = (cv2.GaussianBlur(np.abs(f - plate).max(axis=2), (9, 9), 0) > FG).astype(np.uint8) & zone
        m = cv2.morphologyEx(m, cv2.MORPH_OPEN, np.ones((5, 5), np.uint8))
        m = cv2.morphologyEx(m, cv2.MORPH_CLOSE, np.ones((15, 15), np.uint8))
        count, labels, stats, _ = cv2.connectedComponentsWithStats(m)
        keep = [c for c in range(1, count) if stats[c, cv2.CC_STAT_AREA] >= 2500]
        clean.append(fill_holes(np.isin(labels, keep).astype(np.uint8)))
    disk = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (9, 9))
    alpha = []
    for t in range(n):
        m = cv2.dilate(np.max(np.stack(clean[max(0, t - 1):t + 2]), axis=0), disk).astype(np.float32)
        alpha.append((np.clip(cv2.GaussianBlur(m, (0, 0), 2.5), 0, 1) * 255).astype(np.uint8))
    del clean

    # Маска ролика в игре — где хоть в одном кадре что-то от ролика, с запасом.
    union = (np.max(np.stack(alpha), axis=0) > 5).astype(np.uint8)
    keep = cv2.dilate(fill_holes(union), np.ones((15, 15), np.uint8))
    soft = np.clip(cv2.GaussianBlur(keep.astype(np.float32), (0, 0), 5), 0, 1)
    ys, xs = np.nonzero(soft > .01)
    x0, y0, x1, y1 = xs.min(), ys.min(), xs.max() + 1, ys.max() + 1
    if (x1 - x0) % 2:
        x1, x0 = (x1 + 1, x0) if x1 < WORK else (x1, x0 - 1)
    if (y1 - y0) % 2:
        y1, y0 = (y1 + 1, y0) if y1 < WORK else (y1, y0 - 1)
    w, h = x1 - x0, y1 - y0
    name = 'club-' + a.game
    sm = soft[..., None]
    outside = (sm[..., 0] > .5) & (union == 0)
    still8 = np.clip(still, 0, 255).astype(np.uint8)

    if a.loop:
        overlap = max(3, round(fps * a.loop_blend))
        if a.loop_mode == 'flow' and overlap * 2 >= n:
            raise ValueError('Loop overlap must be shorter than half the source clip')
        end = n - 1 if a.loop_end is None else a.loop_end
        if not 0 <= a.loop_start < end < n:
            raise ValueError('Loop range must contain at least two source frames')
        crops = []
        for t, f in enumerate(lit_frames()):
            m = F(alpha[t])[..., None] / 255
            frame = np.clip(f*m + plate*(1-m), 0, 255).astype(np.uint8)
            crops.append(frame[y0:y1, x0:x1])
        if a.loop_mode == 'mirror':
            # Each step, including both turns and the loop seam, joins adjacent
            # source frames. Do not synthesize pupils or blend facial expressions.
            indices = list(range(a.loop_start, end + 1)) + list(range(end - 1, a.loop_start, -1))
            joined = [crops[i] for i in indices]
            overlap = 0
        else:
            joined = list(loop_frames(crops[a.loop_start:end + 1], overlap))
        del crops
        write_video(OUT / (name + '-play.mp4'), joined, (w, h), fps)
        save(OUT / (name + '-mask.png'), (soft[y0:y1, x0:x1] * 255).astype(np.uint8))
        save(qa / 'mask.png', (soft * 255).astype(np.uint8))
        def full(crop):
            frame = still8.copy()
            mm = sm[y0:y1, x0:x1]
            frame[y0:y1, x0:x1] = np.clip(crop*mm + still[y0:y1, x0:x1]*(1-mm), 0, 255).astype(np.uint8)
            return cv2.resize(frame, (720, 720), interpolation=cv2.INTER_AREA)
        # Two actual loops make the wrap inspectable in the preview.
        write_video(qa / 'preview.mp4', (full(f) for _ in range(2) for f in joined), (720, 720), fps)
        thumbs = []
        for i in np.linspace(0, len(joined)-1, 12).astype(int):
            th = cv2.resize(full(joined[i]), (360, 360))
            cv2.putText(th, '%.2fs' % (i/fps), (8,24), 0, .65, (255,255,255), 2)
            thumbs.append(th)
        save(qa / 'loop.png', np.vstack([np.hstack(thumbs[i:i+4]) for i in range(0,12,4)]))
        # Full-size join samples catch doubled eyes or fingers hidden by a small overview.
        marks = range(len(joined)-max(overlap, 8), len(joined), max(1, overlap//6))
        for i in marks:
            save(qa / ('join-%03d.png' % i), joined[i])
        differences = [float(np.abs(F(joined[i])-F(joined[i-1])).mean()) for i in range(1,len(joined))]
        unit = SIDE / WORK
        rect = [TABLES[a.game]-SIDE/2+x0*unit, TOP+y0*unit, w*unit, h*unit]
        report = {'source': str(Path(a.video).resolve()), 'frames':len(joined), 'fps':fps,
                  'seconds':len(joined)/fps, 'crop':[int(x0),int(y0),int(w),int(h)], 'rect':rect,
                  'blend_frames':overlap, 'loop_mode':a.loop_mode,
                  'source_range':[a.loop_start, end], 'mean_adjacent_mae':float(np.mean(differences)),
                  'max_adjacent_mae':max(differences),
                  'seam_mae':float(np.abs(F(joined[-1])-F(joined[0])).mean())}
        (qa / 'loop-report.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf8')
        print(json.dumps(report, ensure_ascii=False), flush=True)
        return

    # Жест: житель из ролика, остальное — подложка; проявление из неподвижного слоя и угасание в него.
    # Неподвижный слой — и «петля» play, и кадры стыка в начале и в конце action.
    def gesture():
        for t, f in enumerate(lit_frames()):
            m = F(alpha[t])[..., None] / 255
            frame = f * m + plate * (1 - m)
            k = 1.0
            if t < a.fade_in:
                k = (t + 1) / (a.fade_in + 1)
            if t >= n - a.fade_out:
                k = min(k, (n - t) / (a.fade_out + 1))
            yield np.clip(frame * k + still * (1 - k), 0, 255).astype(np.uint8)

    def action():
        for _ in range(overlap):
            yield still8
        yield from gesture()
        for _ in range(overlap):
            yield still8

    write_video(OUT / (name + '-play.mp4'), (still8[y0:y1, x0:x1] for _ in range(STILL_FRAMES)), (w, h), fps)
    total = n + 2 * overlap
    marks = set(np.linspace(0, total - 1, 12).astype(int).tolist())
    thumbs, worst = [], 0.0
    comp = lambda f: F(f) * sm + still * (1 - sm)
    preview = subprocess.Popen([imageio_ffmpeg.get_ffmpeg_exe(), '-y', '-loglevel', 'error', '-f', 'rawvideo',
                                '-pix_fmt', 'bgr24', '-s', '%dx%d' % (WORK, WORK), '-r', str(fps), '-i', '-', '-an',
                                '-c:v', 'libx264', '-pix_fmt', 'yuv420p', '-crf', '22', str(qa / 'preview.mp4')],
                               stdin=subprocess.PIPE)

    def tee():
        nonlocal worst
        for _ in range(STILL_FRAMES):
            preview.stdin.write(still8.tobytes())
        for k, f in enumerate(action()):
            c = np.clip(comp(f), 0, 255).astype(np.uint8)
            preview.stdin.write(np.ascontiguousarray(c).tobytes())
            if k in marks:
                th = cv2.resize(c, (320, 320))
                cv2.putText(th, str(k), (4, 18), 0, .6, (0, 255, 255), 2)
                thumbs.append(th)
            if k % 6 == 0 and outside.any():
                worst = max(worst, float(np.abs(F(f) - still).max(axis=2)[outside].mean()))
            yield f[y0:y1, x0:x1]
        for _ in range(STILL_FRAMES):
            preview.stdin.write(still8.tobytes())

    write_video(OUT / (name + '-action.mp4'), tee(), (w, h), fps)
    preview.stdin.close()
    preview.wait()
    save(OUT / (name + '-mask.png'), (soft[y0:y1, x0:x1] * 255).astype(np.uint8))
    save(qa / 'action.png', np.vstack([np.hstack(thumbs[i:i + 4]) for i in range(0, 12, 4)]))
    save(qa / 'mask.png', (soft * 255).astype(np.uint8))
    print('outside the residents vs the live layer: worst mean %.2f' % worst)
    unit = SIDE / WORK
    left = TABLES[a.game] - SIDE / 2
    print('action', total, 'frames (%.1f s), size' % (total / fps), w, h)
    print('rect in hall units: x %.3f y %.3f w %.3f h %.3f' % (left + x0 * unit, TOP + y0 * unit, w * unit, h * unit))
    report = {'source': str(Path(a.video).resolve()), 'source_frames': n, 'frames': total, 'fps': fps,
              'crop': [int(x0), int(y0), int(w), int(h)], 'overlap_frames': overlap,
              'fade_in': a.fade_in, 'fade_out': a.fade_out,
              'rect': [left + x0 * unit, TOP + y0 * unit, w * unit, h * unit]}
    (qa / 'action-report.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf8')


if __name__ == '__main__':
    main()
