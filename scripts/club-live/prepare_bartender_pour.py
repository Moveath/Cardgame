"""Редкий жест бармена «наливает» между петлями «протирает» (по работе Codex, 2026-10-02).

    python scripts/club-live/prepare_bartender_pour.py [--source <mp4>] [--output <папка>]

Источник — ролик Grok `a_Animate_this_exact_i.mp4` (10 с, 768², камера с 2-й секунды наезжает). Камера
возвращается к общему плану по `screens/qa/bartender-pour-inspect/registration.json` (SIFT по фону, сглажено
медианой по 5 кадрам). Grok перерисовал полки, поэтому фон ролика заменяется картинкой зала: берём только то,
что отличается от медианы ролика по времени (бармен, бутылка, стаканы), плюс неизменную область бармена из
петли; остальное — пиксели зала, и полки за жестом не мигают. Пишет `club-bartender-play.mp4` (та же петля
Codex, в общей рамке), `club-bartender-action.mp4` (начинается последними 0,3 с play, за 0,5 с перетекает
в жест, в конце — обратно и первыми 0,3 с play: в игре стыки накладывают одинаковые кадры), общую маску и
`preview.mp4` (play → action → play на картинке зала). Прямоугольник для ClubVideos печатается.
"""
import argparse
import json
import os
import sys
from pathlib import Path

import cv2
import numpy as np

sys.path.insert(0, os.path.dirname(__file__))
import imageio_ffmpeg  # noqa: E402
import prepare_bartender as base  # noqa: E402

base.cv2, base.np, base.imageio_ffmpeg = cv2, np, imageio_ffmpeg
ROOT = Path(__file__).resolve().parents[2]
HUB = ROOT / 'unity/CardGameClient/Assets/Resources/UI/Hub'
# Рамка работы в единицах зала и масштаб (как у Codex): 280×290 единиц → 896×928 px.
LEFT, TOP, SCALE, SIZE = 620, 245, 3.2, (896, 928)
# Где стоит петля play в этой рамке (её прямоугольник в ClubVideos: 728.75, 268.125).
PLAY_AT = (348, 74)
OVERLAP, BRIDGE = 15, 24  # кадров при 48 fps: общий стык и перетекание


def args():
    parser = argparse.ArgumentParser()
    parser.add_argument('--source', default=str(Path.home() / 'Downloads/a_Animate_this_exact_i.mp4'))
    parser.add_argument('--output', default=str(ROOT / 'screens/qa/bartender-pour-final'))
    return parser.parse_args()


def matte(frames, ref_f, region):
    """Где в кадре бармен, а где фон, который надо взять из зала. Движется (отличие от медианы ролика по
    времени) — ролик. Стоит на месте, но отличается от зала — ролик, если там бармен в ролике или на картинке
    (белая карта, серые усы, чёрный фрак — мало цвета), и зал, если это перерисованные полки (насыщенный янтарь)."""
    plate = np.median(np.stack(frames[::3]), axis=0)
    def plain(image, glass=False):  # мало насыщенности — бармен (белая карта, перчатки, усы, фрак)
        hsv = cv2.cvtColor(np.clip(image, 0, 255).astype(np.uint8), cv2.COLOR_BGR2HSV).astype(np.float32)
        result = np.clip((110 - hsv[..., 1]) / 40, 0, 1)
        if glass:
            # Голубой бокал на картинке зала насыщен — по тону: голубой (90–130). Зелёные и красные бутылки
            # перерисованных полок в ролике так не пройдут.
            blue = ((hsv[..., 0] >= 90) & (hsv[..., 0] <= 130) & (hsv[..., 2] > 60)).astype(np.float32)
            result = np.maximum(result, blue)
        return result
    still = cv2.GaussianBlur(np.abs(plate - ref_f).max(axis=2), (0, 0), 2)
    # Бармен в ролике — или бармен на картинке зала, которого надо закрыть (рука с бокалом там, где её уже нет).
    body = np.clip((still - 20) / 20, 0, 1) * np.maximum(plain(plate), plain(ref_f, glass=True))
    body = cv2.morphologyEx(body, cv2.MORPH_CLOSE, np.ones((9, 9), np.uint8))
    raw = []
    for f in frames:
        d = cv2.GaussianBlur(np.abs(f - plate).max(axis=2), (0, 0), 2)
        fg = (np.maximum(np.clip((d - 12) / 16, 0, 1), body) > .3).astype(np.uint8)
        # Дырки внутри фигуры (чёрный рукав на тёмных полках похож на фон) — заполняем.
        fg = cv2.morphologyEx(fg, cv2.MORPH_CLOSE, np.ones((25, 25), np.uint8))
        flood = fg * 255
        cv2.floodFill(flood, np.zeros((fg.shape[0] + 2, fg.shape[1] + 2), np.uint8), (0, 0), 128)
        raw.append(((flood != 128) | (fg > 0)).astype(np.float32))
    # По времени — максимум за ±4 кадра: граница не дрожит от кадра к кадру (рябь), рука не пропадает на кадр.
    result = []
    for i in range(len(raw)):
        fg = np.max(np.stack(raw[max(0, i - 4):i + 5]), axis=0)
        fg = cv2.dilate(fg, np.ones((13, 13), np.uint8))
        result.append(cv2.GaussianBlur(fg, (0, 0), 4) * region)
    return result


def smooth(values, size):
    return [np.median(values[max(0, i - size):min(len(values), i + size + 1)], axis=0) for i in range(len(values))]


def main():
    a = args()
    out = Path(a.output)
    out.mkdir(parents=True, exist_ok=True)
    hall = base.read_image(HUB / 'club-hall.png')
    s = hall.shape[1] / 1916
    ref = cv2.resize(hall[round(TOP * s):round((TOP + SIZE[1] / SCALE) * s), round(LEFT * s):round((LEFT + SIZE[0] / SCALE) * s)],
                     SIZE, interpolation=cv2.INTER_AREA)
    ref_f = ref.astype(np.float32)

    # Петля play (уже в игре) — на своё место в рамке.
    play_mask = base.read_image(HUB / 'Video/club-bartender-mask.png')[..., 0].astype(np.float32) / 255
    ph, pw = play_mask.shape
    px, py = PLAY_AT
    region = np.zeros(SIZE[::-1], np.float32)
    region[40:540, 180:860] = 1  # стойка и над ней; ниже — край стола с тройкой, его не трогаем
    region *= np.clip((520 - np.arange(SIZE[1])[:, None]) / 10, 0, 1)
    # Исходник петли — из ролика Codex до замены фона (у вставленной в игру петли фон может быть уже из зала).
    cap = cv2.VideoCapture(str(ROOT / 'screens/qa/bartender-prepared-final/club-bartender-play.mp4'))
    play_mask = base.read_image(ROOT / 'screens/qa/bartender-prepared-final/club-bartender-mask.png')[..., 0].astype(np.float32) / 255
    ph, pw = play_mask.shape
    px, py = PLAY_AT
    raw = []
    while True:
        ok, f = cap.read()
        if not ok:
            break
        full = ref_f.copy()
        full[py:py + ph, px:px + pw] = f * play_mask[..., None] + full[py:py + ph, px:px + pw] * (1 - play_mask[..., None])
        raw.append(full)
    cap.release()
    # Петля — ровно как у Codex (автору она нравилась): её маска, фон под маской — из ролика.
    play_region = np.zeros(SIZE[::-1], np.float32)
    play_region[py:py + ph, px:px + pw] = play_mask
    play_mattes = [play_region]
    plays = raw

    # Жест: камера — к общему плану.
    reg = json.loads((ROOT / 'screens/qa/bartender-pour-inspect/registration.json').read_text())
    mats = smooth(np.array([r['matrix'] for r in reg]), 2)
    cap = cv2.VideoCapture(a.source)
    acts, valids = [], []
    for m in mats:
        ok, f = cap.read()
        if not ok:
            break
        acts.append(cv2.warpAffine(f, m.astype(np.float32), SIZE, flags=cv2.INTER_CUBIC).astype(np.float32))
        valids.append(cv2.warpAffine(np.ones((768, 768), np.float32), m.astype(np.float32), SIZE))
    cap.release()
    # Цвет — к залу по фону первого кадра (он совпадает с картинкой).
    near = (np.abs(acts[0] - ref_f).max(axis=2) < 28) & (valids[0] > .99)
    gain = np.float32([np.polyfit(acts[0][..., c][near], ref_f[..., c][near], 1) for c in range(3)])
    acts = [f * gain[:, 0] + gain[:, 1] for f in acts]

    mattes = [m * np.clip(v, 0, 1) for m, v in zip(matte(acts, ref_f, region), valids)]
    composed = [f * m[..., None] + ref_f * (1 - m[..., None]) for f, m in zip(acts, mattes)]

    # 24 fps источника — к 48 fps повтором кадров. Промежуточные кадры оптическим потоком давали «призрачные»
    # руки и рябь (автор, 2026-10-03). Кадры 51–57 генератор смазал при наезде камеры (рука исчезает) —
    # держим соседние целые позы.
    hold = {i: 50 for i in range(51, 58)}
    u8 = [np.clip(composed[hold.get(i, i)], 0, 255).astype(np.uint8) for i in range(len(composed))]
    smooth48 = [f for f in u8 for _ in range(2)]
    playu8 = [np.clip(p, 0, 255).astype(np.uint8) for p in plays]

    action = list(playu8[-OVERLAP:])
    for i in range(BRIDGE):
        w = (i + 1) / (BRIDGE + 1)
        action.append(base.blend_flow(playu8[i], smooth48[i], w * w * (3 - 2 * w)))
    action.extend(smooth48[BRIDGE:len(smooth48) - BRIDGE])
    for i in range(BRIDGE):
        w = (i + 1) / (BRIDGE + 1)
        action.append(base.blend_flow(smooth48[-BRIDGE + i], playu8[-BRIDGE + i], w * w * (3 - 2 * w)))
    action.extend(playu8[:OVERLAP])

    # Общая маска: где хоть в одном кадре видно не зал.
    union = np.max(np.stack(play_mattes + mattes), axis=0)
    union = np.maximum(union, play_region)
    union = cv2.GaussianBlur(cv2.dilate((union > .02).astype(np.float32), np.ones((15, 15), np.uint8)), (0, 0), 5) * region
    ys, xs = np.nonzero(union > .001)
    x0, y0, x1, y1 = int(xs.min()), int(ys.min()), int(xs.max() + 1), int(ys.max() + 1)
    x0 -= (x1 - x0) % 2
    y1 += (y1 - y0) % 2
    crop = lambda f: np.ascontiguousarray(f[y0:y1, x0:x1])
    base.write_video(out / 'club-bartender-play.mp4', (crop(p) for p in playu8), (x1 - x0, y1 - y0), 48)
    base.write_video(out / 'club-bartender-action.mp4', (crop(f) for f in action), (x1 - x0, y1 - y0), 48)
    mask = union[y0:y1, x0:x1]
    base.save_image(out / 'club-bartender-mask.png', mask * 255)

    def comp(f):
        full = ref.copy()
        full[y0:y1, x0:x1] = np.clip(f[y0:y1, x0:x1] * mask[..., None] + ref_f[y0:y1, x0:x1] * (1 - mask[..., None]), 0, 255)
        return full
    sequence = playu8 + action[OVERLAP:] + playu8[OVERLAP:]
    base.write_video(out / 'preview.mp4', (comp(f) for f in sequence), SIZE, 48)
    thumbs = []
    for i in np.linspace(0, len(action) - 1, 16).astype(int):
        t = cv2.resize(comp(action[i])[0:620, 150:896], (373, 310))
        cv2.putText(t, str(i), (4, 16), 0, .5, (0, 255, 255), 1)
        thumbs.append(t)
    base.save_image(out / 'contact.png', np.vstack([np.hstack(thumbs[k:k + 4]) for k in range(0, 16, 4)]))
    rect = [LEFT + x0 / SCALE, TOP + y0 / SCALE, (x1 - x0) / SCALE, (y1 - y0) / SCALE]
    (out / 'metadata.json').write_text(json.dumps(dict(rect=rect, play=len(playu8), action=len(action)), indent=2))
    print('rect', [round(v, 3) for v in rect], 'play', len(playu8), 'action', len(action))


if __name__ == '__main__':
    main()
