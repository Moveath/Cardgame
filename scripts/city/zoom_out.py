"""Картинка места «камерой дальше»: утверждённая остаётся пиксель в пиксель в середине новой 21:9, края дорисовывает ChatGPT.

    python scripts/city/zoom_out.py <место> canvas   # заготовка для ChatGPT (1536×1024)
    python scripts/city/zoom_out.py <место> build    # склейка ответа → итог в references и игровая картинка

Места:
- tent (2026-10-09) — шатёр гадалки: на весь экран Пиковая дама была слишком крупной. Старая 1904×826 — 75 %
  новой 2540×1102. Промпт — `screens/references/fortune/tent-zoom-prompt.txt`.
- kiosk (2026-10-09) — киоск «Карточный вестник»: 16:9 на экране 20:9 теряло верх (табличку), «камера очень близко».
  Старая 1672×941 — 80 % высоты новой 2712×1176. Промпт — `screens/references/city/news-kiosk-zoom-prompt.txt`.
  ОТВЕРГНУТО автором: фон не как в городе, киоск далеко, второй фонтан. Киоск расширен только по бокам —
  `scripts/six-house/widen_room.py kiosk`.

Заготовка: кадр 21:9 во всю ширину посередине холста 3:2, в нём старая картинка, всё остальное — пурпурное
(сверху и снизу кадра тоже: генератору проще, лишнее отрезается). Склейка находит, где старая картинка легла
в ответе (ORB-точки → подобие, затем ECC), приводит ответ к ней, подгоняет цвет по полосе у края и вклеивает
оригинал с мягким швом в BAND px. Где старая лежит в новой (OX, OY) — то же в коде места
(`PlayerHubFortune.FortuneOldArt`, `PlayerHubNews.NewsOldArt`).
"""
import sys
from pathlib import Path

import cv2
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
FORTUNE = ROOT / 'screens/references/fortune'
CITY = ROOT / 'screens/references/city'
HUB = ROOT / 'unity/CardGameClient/Assets/Resources/UI/Hub'
# место: исходник, заготовка, ответ ChatGPT, итог, игровая картинка, размер итога, где в нём старая
JOBS = {
    'tent': (FORTUNE / 'fortune-tent-dark-selected.png', FORTUNE / 'canvases/fortune-tent-zoom-canvas.png',
             FORTUNE / 'fortune-tent-zoom-answer.png', FORTUNE / 'fortune-tent-zoom-v1.png', HUB / 'fortune-tent.png',
             (2540, 1102), (318, 138)),
    'kiosk': (CITY / 'news-kiosk-v2.png', CITY / 'canvases/news-kiosk-zoom-canvas.png',
              CITY / 'news-kiosk-zoom-answer.png', CITY / 'news-kiosk-v3-zoom.png', HUB / 'news-kiosk.png',
              (2712, 1176), (520, 118)),
}
CW, CH = 1536, 1024        # ChatGPT отвечает 3:2
MAGENTA = (255, 0, 255)    # BGR и RGB совпадают
BAND, MATCH = 40, 60


def read(path):
    return cv2.imdecode(np.fromfile(str(path), np.uint8), cv2.IMREAD_COLOR)


def write(path, image):
    path.parent.mkdir(parents=True, exist_ok=True)
    cv2.imencode('.png', image)[1].tofile(str(path))


def canvas(source_path, canvas_path, size, at):
    (w_out, h_out), (ox, oy) = size, at
    source = read(source_path)
    frame_h = round(h_out * CW / w_out)
    frame_y = (CH - frame_h) // 2
    frame = np.full((h_out, w_out, 3), MAGENTA, np.uint8)
    frame[oy:oy + source.shape[0], ox:ox + source.shape[1]] = source
    out = np.full((CH, CW, 3), MAGENTA, np.uint8)
    out[frame_y:frame_y + frame_h] = cv2.resize(frame, (CW, frame_h), interpolation=cv2.INTER_AREA)
    # Края старой картинки после уменьшения смешаны с пурпурным — делаем их чисто пурпурными или чисто картинкой.
    sx, sy = CW / w_out, frame_h / h_out
    x0, x1 = round(ox * sx) + 1, round((ox + source.shape[1]) * sx) - 1
    y0, y1 = frame_y + round(oy * sy) + 1, frame_y + round((oy + source.shape[0]) * sy) - 1
    mask = np.ones((CH, CW), bool); mask[y0:y1, x0:x1] = False
    out[mask] = MAGENTA
    write(canvas_path, out)
    print(canvas_path.name, CW, 'x', CH, 'old picture at', (x0, y0, x1, y1))


def build(source_path, answer_path, out_path, game_path, size, at):
    (w_out, h_out), (ox, oy) = size, at
    source = read(source_path).astype(np.float64)
    h, w = source.shape[:2]
    frame_h = round(h_out * CW / w_out)
    frame_y = (CH - frame_h) // 2
    answer = read(answer_path)
    if answer.shape[:2] != (CH, CW):
        answer = cv2.resize(answer, (CW, CH), interpolation=cv2.INTER_LANCZOS4)
    big = cv2.resize(answer[frame_y:frame_y + frame_h], (w_out, h_out), interpolation=cv2.INTER_LANCZOS4)
    gray = lambda image: cv2.cvtColor(np.clip(image, 0, 255).astype(np.uint8), cv2.COLOR_BGR2GRAY)
    orb = cv2.ORB_create(6000)
    ka, da = orb.detectAndCompute(gray(source), None)
    kb, db = orb.detectAndCompute(gray(big), None)
    matches = sorted(cv2.BFMatcher(cv2.NORM_HAMMING, crossCheck=True).match(da, db), key=lambda m: m.distance)[:600]
    src = np.float32([ka[m.queryIdx].pt for m in matches])
    dst = np.float32([kb[m.trainIdx].pt for m in matches])
    guess, inliers = cv2.estimateAffinePartial2D(src, dst, method=cv2.RANSAC, ransacReprojThreshold=4)
    # Ответ в координатах старой картинки, затем уточнение ECC.
    crop = cv2.warpAffine(big, guess, (w, h), flags=cv2.INTER_LINEAR | cv2.WARP_INVERSE_MAP, borderMode=cv2.BORDER_REFLECT)
    blur = lambda image: cv2.GaussianBlur(gray(image), (5, 5), 0).astype(np.float32)
    warp = np.eye(2, 3, dtype=np.float32)
    _, warp = cv2.findTransformECC(blur(source), blur(crop), warp, cv2.MOTION_AFFINE,
                                   (cv2.TERM_CRITERIA_EPS | cv2.TERM_CRITERIA_COUNT, 200, 1e-6), None, 5)
    # Точка итога q → ответ: guess(warp(q - (ox, oy))).
    full = lambda m: np.vstack([m, [0, 0, 1]]).astype(np.float64)
    shift = np.array([[1, 0, -ox], [0, 1, -oy], [0, 0, 1]], np.float64)
    mapping = (full(guess) @ full(warp) @ shift)[:2]
    out = cv2.warpAffine(big, mapping, (w_out, h_out), flags=cv2.INTER_LINEAR | cv2.WARP_INVERSE_MAP,
                         borderMode=cv2.BORDER_REFLECT).astype(np.float64)
    inner = out[oy:oy + h, ox:ox + w]
    ring = np.ones((h, w), bool); ring[MATCH:-MATCH, MATCH:-MATCH] = False
    gain = (source[ring].mean(0) + 1) / (inner[ring].mean(0) + 1)
    out = np.clip(out * gain, 0, 255)
    ramp = lambda n: np.clip(np.minimum(np.arange(n), np.arange(n)[::-1]) / BAND, 0, 1)
    alpha = np.minimum.outer(ramp(h), ramp(w))[..., None]
    out[oy:oy + h, ox:ox + w] = source * alpha + out[oy:oy + h, ox:ox + w] * (1 - alpha)
    image = np.round(out).astype(np.uint8)
    write(out_path, image)
    write(game_path, image)
    print(out_path.name, w_out, 'x', h_out, 'scale', round(float(np.hypot(*guess[0, :2])), 3), 'inliers', int(inliers.sum()),
          'ecc', np.round(warp, 3).tolist(), 'gain', np.round(gain, 3).tolist())


if __name__ == '__main__':
    source, canvas_path, answer, out, game, size, at = JOBS[sys.argv[1]]
    if sys.argv[2] == 'canvas':
        canvas(source, canvas_path, size, at)
    else:
        build(source, answer, out, game, size, at)
