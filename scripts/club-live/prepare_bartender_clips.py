"""Ролики бармена — в игру: петля «протирает» и редкий жест «наливает себе и пробует» (2026-10-04).

    python scripts/club-live/prepare_bartender_clips.py [--cut 47 159]

Бармен поставлен по листу в фартуке на пустой бар (`place_bartender.py`), оба ролика Grok сняты с кадра
`video-frames/bartender-start.png` (первый и последний кадр):
- play — `video-tests/bartender-play-v2.mp4`. Когда бармен поднимает бокал к свету, бокал «раздваивается» (внизу
  остаётся второй стакан) — этот кусок вырезан: петля — кадры до --cut[0] и от --cut[1], склейка там, где поза
  до и после совпадает (подбирается рядом, печатается);
- action — `video-tests/bartender-action-v3.mp4` (ролик v2, исправленный редактированием Grok: убраны лишние
  стаканы, в конце открыты глаза). В игре жест начинается, когда петле осталось 8 кадров, и проявляется за
  0,31 с (`HubClubVideo.SetupVariation`), поэтому action = последние 8 кадров петли → петля до места, где поза
  ближе всего к жесту, → жест → (поза подобрана) петля до конца → первые 8 кадров петли.
Кадры выравниваются по фону (пустой бар) с растяжением: правка Grok растянула картинку по вертикали на 0,4 %,
одним сдвигом полки в жесте «подпрыгивали» на 1–2 px против петли (автор, 2026-10-04). Фон в обоих роликах —
картинка зала: в каждом кадре от ролика берётся только бармен (что отличается от того же увеличенного ESRGAN
кадра пустого бара `video-frames/bartender-empty.png`, с которым работал Grok) с бутылкой, бокалом и тряпкой,
с узким мягким краем; остальное — пиксели зала. Полки в петле и в жесте — одни и те же пиксели, а маска ролика в
игре не видна. Обрывок горлышка, который Grok оставил торчать над стойкой слева после того, как бармен убрал
бутылку, стирается (где его не закрывает рука). Цвет — к картинке зала. Пишет в `Resources/UI/Hub/Video/`
`club-bartender-play.mp4`, `club-bartender-action.mp4`, `club-bartender-mask.png`, превью в
`screens/qa/bartender-clips/` и печатает прямоугольник для PlayerHubInterior.ClubVideos.
"""
import argparse
import os
import subprocess
from pathlib import Path

import cv2
import imageio_ffmpeg
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
REFS = ROOT / 'screens/references/city'
OUT = Path(os.environ.get('CLUB_VIDEO_OUT') or ROOT / 'unity/CardGameClient/Assets/Resources/UI/Hub/Video')
QA = ROOT / 'screens/qa/bartender-clips'
LEFT, TOP, SIDE = 640, 245, 250  # квадрат кадра в единицах зала 1916×821 (как в video_frames.py)
WORK = 800  # 3,2 px на единицу зала, как у пианистки
BLEND = 3  # кадров сведения на склейках
OVERLAP = 8  # одинаковых кадров на стыках петли и жеста (SetupVariation 15/48 с при 24 к/с → 8 кадров)
EXIT_FROM = 200  # раньше этого кадра жеста бармен ещё не вернулся к протиранию
FG = 40  # порог «бармен, а не фон» против пустого бара (размытое отличие, 0–255)
COUNTER_FRONT = 403  # передний край стойки, единицы зала: тряпка лежит на стойке, ниже — живой слой стола
STUB_BOX = (40, 380, 135, 492)  # x0, y0, x1, y1 в кадре WORK: где у жеста торчит обрывок горлышка
STUB_FROM, STUB_STILL = 130, (160, 206)  # с какого кадра жеста искать обрывок и где он точно стоит


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
    ffmpeg.wait()


def read(path):
    cap = cv2.VideoCapture(str(path))
    fps = cap.get(cv2.CAP_PROP_FPS) or 24
    frames = []
    while True:
        ok, f = cap.read()
        if not ok:
            break
        frames.append(cv2.resize(f, (WORK, WORK), interpolation=cv2.INTER_AREA))
    cap.release()
    return frames, fps


def fill_holes(mask):
    flood = mask * 255
    cv2.floodFill(flood, np.zeros((mask.shape[0] + 2, mask.shape[1] + 2), np.uint8), (0, 0), 128)
    return ((flood != 128) | (mask > 0)).astype(np.uint8)


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--play', default=str(REFS / 'video-tests/bartender-play-v2.mp4'))
    p.add_argument('--action', default=str(REFS / 'video-tests/bartender-action-v3.mp4'))
    p.add_argument('--cut', type=int, nargs=2, default=(47, 159))
    a = p.parse_args()
    QA.mkdir(parents=True, exist_ok=True)

    master = load(REFS / 'club-hall-live.png')
    plate = load(REFS / 'club-hall-live-no-bartender.png')
    s = master.shape[1] / 1916
    box = (slice(round(TOP * s), round((TOP + SIDE) * s)), slice(round(LEFT * s), round((LEFT + SIDE) * s)))
    still = cv2.resize(master[box], (WORK, WORK), interpolation=cv2.INTER_CUBIC).astype(np.float32)
    empty = cv2.resize(plate[box], (WORK, WORK), interpolation=cv2.INTER_CUBIC).astype(np.float32)
    start_frame = cv2.resize(load(REFS / 'video-frames/bartender-start.png'), (WORK, WORK),
                             interpolation=cv2.INTER_AREA).astype(np.float32)
    empty_frame = cv2.resize(load(REFS / 'video-frames/bartender-empty.png'), (WORK, WORK),
                             interpolation=cv2.INTER_AREA).astype(np.float32)
    front = int(round((COUNTER_FRONT - TOP) * WORK / SIDE))
    rows, cols = np.mgrid[0:WORK, 0:WORK]
    # Фон для выравнивания: где бармена нет на кадре и куда он в жесте не тянется — полки над картой и по
    # краям (по ним видно и сдвиг, и растяжение); без тройки пик внизу (она моргает).
    bg = (np.abs(start_frame - empty_frame).max(axis=2) < 10).astype(np.uint8)
    bg = cv2.erode(bg, np.ones((41, 41), np.uint8))
    bg &= ((rows < 110) | (cols > 705) | (cols < 30)).astype(np.uint8)
    bg[front:] = 0
    half = WORK // 2
    gray_half = lambda f: cv2.resize(cv2.cvtColor(f, cv2.COLOR_BGR2GRAY), (half, half),
                                     interpolation=cv2.INTER_AREA).astype(np.float32) / 255
    template = gray_half(empty_frame.astype(np.uint8))
    bg_half = cv2.resize(bg, (half, half), interpolation=cv2.INTER_NEAREST)

    def prepare(path, name, stub=False):
        """Кадры ролика: выровненные по фону и в цвете зала (uint8), где в каждом бармен (0/1)."""
        frames, fps = read(path)
        warps, warp = [], np.eye(2, 3, dtype=np.float32)
        for f in frames:
            try:
                _, warp = cv2.findTransformECC(template, gray_half(f), warp.copy(), cv2.MOTION_AFFINE,
                                               (cv2.TERM_CRITERIA_EPS | cv2.TERM_CRITERIA_COUNT, 100, 1e-6), bg_half, 3)
            except cv2.error:
                pass  # не сошлось — берём преобразование прошлого кадра
            warps.append(warp.copy())
        # Камера плавная: медиана по соседним кадрам (одиночный промах не дёргает картинку).
        warps = np.array(warps)
        warps = np.array([np.median(warps[max(0, i - 3):i + 4], axis=0) for i in range(len(warps))])
        warps[:, :, 2] *= WORK / half
        lit, fg, crops, gain = [], [], [], None
        for f, warp in zip(frames, warps):
            f = cv2.warpAffine(f, warp, (WORK, WORK), flags=cv2.INTER_CUBIC | cv2.WARP_INVERSE_MAP,
                               borderMode=cv2.BORDER_REPLICATE).astype(np.float32)
            if gain is None:
                near = (np.abs(f - still).max(axis=2) < 40) & (bg > 0)
                gain = np.float32([np.polyfit(f[..., c][near], still[..., c][near], 1) for c in range(3)])
            d = cv2.GaussianBlur(np.abs(f - empty_frame).max(axis=2), (9, 9), 0)
            fg.append((d > FG).astype(np.uint8))
            if stub:
                x0, y0, x1, y1 = STUB_BOX
                crops.append(f[y0:y1, x0:x1].astype(np.uint8))
            lit.append(np.clip(f * gain[:, 0] + gain[:, 1], 0, 255).astype(np.uint8))
        frames.clear()
        stretch = warps[:, 1, 1]
        print(name, len(lit), 'frames', fps, 'fps; shift x %.2f..%.2f y %.2f..%.2f, stretch y %.4f..%.4f' % (
            warps[:, 0, 2].min(), warps[:, 0, 2].max(), warps[:, 1, 2].min(), warps[:, 1, 2].max(),
            stretch.min(), stretch.max()), 'gain', np.round(gain[:, 0], 3))
        if stub:
            erase_stub(fg, crops)
        return lit, fg, fps

    def erase_stub(fg, crops):
        """Бутылка, которую Grok оставил стоять у стойки слева, когда бармен её убрал: тёмная, почти сливается
        с фоном, поэтому стирается вся её область (по кадрам, где она точно стоит, с запасом) с того кадра, как
        бармен её отпустил, — кроме белой перчатки с обводкой, когда рука проходит рядом."""
        x0, y0, x1, y1 = STUB_BOX
        plate_crop = empty_frame[y0:y1, x0:x1]
        shape = np.zeros(plate_crop.shape[:2], np.uint8)
        for c in crops[STUB_STILL[0]:STUB_STILL[1]:3]:
            shape |= (cv2.GaussianBlur(np.abs(c.astype(np.float32) - plate_crop).max(axis=2), (5, 5), 0) > 20).astype(np.uint8)
        count, labels, stats, _ = cv2.connectedComponentsWithStats(shape)
        if count < 2:
            print('no bottle stub found')
            return
        shape = fill_holes((labels == 1 + int(np.argmax(stats[1:, cv2.CC_STAT_AREA]))).astype(np.uint8))
        region = cv2.dilate(shape, np.ones((9, 9), np.uint8))
        still_stub = np.median(np.stack(crops[STUB_STILL[0]:STUB_STILL[1]]), axis=0).astype(np.float32)
        like = [((np.abs(c.astype(np.float32) - still_stub).max(axis=2) < 30) & (shape > 0)).sum() for c in crops]
        first = next(t for t in range(STUB_FROM, len(crops)) if like[t] > .5 * shape.sum())
        for t in range(first - 1, len(crops)):
            hsv = cv2.cvtColor(crops[t], cv2.COLOR_BGR2HSV)
            # Перчатка — светлая и тёплая (кремовая); бледно-зелёная полоска на бутылке — не перчатка.
            glove = ((hsv[..., 2] > 180) & (hsv[..., 1] < 80) & (hsv[..., 0] >= 5) & (hsv[..., 0] <= 40)).astype(np.uint8)
            keep = cv2.dilate(glove, np.ones((5, 5), np.uint8))
            fg[t][y0:y1, x0:x1] &= 1 - (region & (1 - keep))
        print('bottle stub erased from action frame', first - 1, '(%d px)' % region.sum())

    def alphas(fg):
        """Мягкая маска бармена в каждом кадре: крупные пятна, без дыр, по времени ±1 кадр, узкий мягкий край."""
        clean = []
        for m in fg:
            m = m.copy()
            m[front:] = 0
            m = cv2.morphologyEx(m, cv2.MORPH_OPEN, np.ones((5, 5), np.uint8))
            m = cv2.morphologyEx(m, cv2.MORPH_CLOSE, np.ones((15, 15), np.uint8))
            count, labels, stats, _ = cv2.connectedComponentsWithStats(m)
            big = [c for c in range(1, count) if stats[c, cv2.CC_STAT_AREA] >= 1500]
            clean.append(fill_holes(np.isin(labels, big).astype(np.uint8)))
        result = []
        disk = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (9, 9))
        for t in range(len(clean)):
            m = np.max(np.stack(clean[max(0, t - 1):t + 2]), axis=0)
            m = cv2.dilate(m, disk).astype(np.float32)
            m = np.clip(cv2.GaussianBlur(m, (0, 0), 2.5), 0, 1)
            m[front:] = 0
            result.append((m * 255).astype(np.uint8))
        return result

    def compose(lit, alpha):
        """Бармен из ролика, остальное — картинка зала."""
        out = []
        for f, m in zip(lit, alpha):
            w = m[..., None].astype(np.float32) / 255
            out.append(np.clip(f.astype(np.float32) * w + empty * (1 - w), 0, 255).astype(np.uint8))
        return out

    play_lit, play_fg, fps = prepare(a.play, 'play')
    play_alpha = alphas(play_fg)
    play = compose(play_lit, play_alpha)
    del play_lit, play_fg
    act_lit, act_fg, act_fps = prepare(a.action, 'action', stub=True)
    act_alpha = alphas(act_fg)
    act = compose(act_lit, act_alpha)
    del act_lit, act_fg
    assert abs(fps - act_fps) < .01, 'play и action — с одной частотой кадров'
    F = lambda f: f.astype(np.float32)
    mix = lambda x, y, w: np.clip(F(x) * (1 - w) + F(y) * w, 0, 255).astype(np.uint8)

    body = (slice(150, 520), slice(200, 760))
    small = lambda f: cv2.resize(F(f[body]), (112, 74), interpolation=cv2.INTER_AREA)
    dist = lambda x, y: float(np.abs(x - y).mean())
    cost3 = lambda x, y, i, j: sum(dist(x[i + k], y[j + k]) for k in range(3))

    # Петля: без куска с раздвоившимся бокалом, склейка по позе (три кадра подряд).
    sp = [small(f) for f in play]
    c0, c1 = a.cut
    cost, i, j = min((cost3(sp, sp, i, j), i, j) for i in range(c0 - 6, c0 + 7) for j in range(c1 - 6, c1 + 7))
    print('play cut', i, '->', j, 'cost %.2f' % cost)
    loop = play[:i] + play[j:]
    loop_alpha = play_alpha[:i] + play_alpha[j:]
    n = len(loop)
    for k in range(1, BLEND + 1):
        w = (BLEND - k + 1) / (BLEND + 1)
        loop[i - k] = mix(play[i - k], play[j - k], w)
        loop[n - k] = mix(loop[n - k], play[0], w)

    # Жест. Начало: последние OVERLAP кадров петли, дальше петля идёт с начала до места, где поза ближе всего
    # к началу жеста, — там короткое сведение в жест. Конец: после того как бармен снова протирает
    # (кадр жеста >= EXIT_FROM), место, где поза ближе всего к какому-то кадру петли, — сведение в петлю,
    # петля до конца и её первые OVERLAP кадров. Сводим всего BLEND кадров: длинное сведение разных поз двоит руки.
    sa, sl = [small(f) for f in act], [small(f) for f in loop]
    m = len(act)
    cost, pin, qin = min((cost3(sl, sa, p_, q_) + p_ / 40, p_, q_)
                         for p_ in range(BLEND, 40) for q_ in range(BLEND, 40))
    print('enter: loop', pin, '-> action', qin, 'cost %.2f' % cost)
    cost, tout, rout = min((cost3(sa, sl, t_, r_) + 2 * (n - r_) / n, t_, r_)
                           for t_ in range(EXIT_FROM, m - 3) for r_ in range(BLEND, n - 3))
    print('leave: action', tout, '-> loop', rout, 'cost %.2f' % cost)
    head = loop[:pin]
    for k in range(1, BLEND + 1):
        head[pin - k] = mix(loop[pin - k], act[qin - k], (BLEND - k + 1) / (BLEND + 1))
    body_frames = act[qin:tout]
    for k in range(1, BLEND + 1):
        body_frames[-k] = mix(act[tout - k], loop[rout - k], (BLEND - k + 1) / (BLEND + 1))
    action = loop[n - OVERLAP:] + head + body_frames + loop[rout:] + loop[:OVERLAP]

    # Маска ролика в игре — где бармен хоть в одном кадре, с запасом: вне бармена кадры и так равны залу.
    union = np.max(np.stack(loop_alpha + act_alpha[qin:tout]), axis=0) > 5
    union |= cv2.GaussianBlur(np.abs(still - empty).max(axis=2), (9, 9), 0) > 30
    keep = cv2.dilate(fill_holes(union.astype(np.uint8)), np.ones((15, 15), np.uint8))
    keep[front:] = 0
    soft = np.clip(cv2.GaussianBlur(keep.astype(np.float32), (0, 0), 5), 0, 1)
    soft[front:] = 0

    # Проверка: вне бармена кадры совпадают с залом — у петли и у жеста одни и те же полки.
    outside = (soft > .5) & ~cv2.dilate(union.astype(np.uint8), np.ones((9, 9), np.uint8)).astype(bool)
    for name, seq in (('play', loop), ('action', action)):
        worst = max(float(np.abs(F(f) - empty).max(axis=2)[outside].mean()) for f in seq[::5])
        print(name, 'background inside the mask vs hall: worst mean %.2f' % worst)

    ys, xs = np.nonzero(soft > .01)
    x0, y0, x1, y1 = xs.min(), ys.min(), xs.max() + 1, ys.max() + 1
    if (x1 - x0) % 2:
        x1, x0 = (x1 + 1, x0) if x1 < WORK else (x1, x0 - 1)
    if (y1 - y0) % 2:
        y1, y0 = (y1 + 1, y0) if y1 < WORK else (y1, y0 - 1)
    w, h = x1 - x0, y1 - y0
    write_video(OUT / 'club-bartender-play.mp4', (f[y0:y1, x0:x1] for f in loop), (w, h), fps)
    write_video(OUT / 'club-bartender-action.mp4', (f[y0:y1, x0:x1] for f in action), (w, h), fps)
    save(OUT / 'club-bartender-mask.png', (soft[y0:y1, x0:x1] * 255).astype(np.uint8))

    # Превью как в игре: петля, жест с наложением OVERLAP кадров на стыках, снова петля.
    sm = soft[..., None]
    comp = lambda f: F(f) * sm + still * (1 - sm)
    sequence = loop[:n - OVERLAP] + action + loop[OVERLAP:] + loop
    write_video(QA / 'preview.mp4', (comp(f) for f in sequence), (WORK, WORK), fps)
    cut_in, cut_out = OVERLAP + pin, OVERLAP + pin + tout - qin
    thumbs = []
    for k in [cut_in - 4, cut_in - 2, cut_in, cut_in + 2, cut_out - 4, cut_out - 2, cut_out, cut_out + 2]:
        th = cv2.resize(np.clip(comp(action[k]), 0, 255).astype(np.uint8)[60:560, 60:780], (360, 250))
        cv2.putText(th, str(k), (4, 18), 0, .6, (0, 255, 255), 2)
        thumbs.append(th)
    save(QA / 'seams.png', np.vstack([np.hstack(thumbs[:4]), np.hstack(thumbs[4:8])]))
    save(QA / 'mask.png', (soft * 255).astype(np.uint8))
    unit = SIDE / WORK
    print('play', n, 'frames (%.1f s), action' % (n / fps), len(action), 'frames (%.1f s), size' % (len(action) / fps), w, h)
    print('rect in hall units: x %.3f y %.3f w %.3f h %.3f' % (LEFT + x0 * unit, TOP + y0 * unit, w * unit, h * unit))
    print('action: enter at', cut_in, 'leave at', cut_out)


if __name__ == '__main__':
    main()
