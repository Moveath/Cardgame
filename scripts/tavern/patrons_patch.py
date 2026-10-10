"""Завсегдатаи таверны «Червовая дама» — заплатки зала (2026-10-10, идея автора).

Кусок чистого зала (`screens/references/city/tavern-hall-v2-wide.png`) → `canvases/tavern-<место>-crop.png` (увеличен) →
ChatGPT сажает туда готовых жителей (промпты — `screens/references/city/tavern-prompts.txt`, шаги 4–5) → ответ
`canvases/tavern-<место>-patrons.png`. Скрипт приводит ответ к размеру куска, выравнивает его (сначала по особым
точкам ORB — генератор чуть перекадрирует, потом ECC аффинно по всему, кроме места жителей), подгоняет яркость и
берёт из участка PATCH только то, что генератор поменял крупными пятнами (сами жители, их кружки, сумки, карты), мягко.
Заплатки ложатся на чистый зал сзади вперёд (порядок JOBS): дальние закрываются ближними, как в жизни. Итог — игровая
картинка `UI/Hub/tavern-hall.png` (и копия `tavern-hall-v3-patrons.png`); отдельные заплатки —
`canvases/tavern-<место>-patch.png`. Чего ещё нет — пропускается.

    python scripts/tavern/patrons_patch.py
"""
from pathlib import Path

import cv2
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
CITY = ROOT / 'screens/references/city'
CLEAN = CITY / 'tavern-hall-v2-wide.png'
OUT = ROOT / 'unity/CardGameClient/Assets/Resources/UI/Hub/tavern-hall.png'
COPY = CITY / 'tavern-hall-v3-patrons.png'
# Место: кусок зала (x, y, ширина, высота), участок, где могут появиться жители, и с какого зала резан кусок (с ним
# и сравнивается ответ): чистый или уже с жителями — передние столы резаны после полиции и средних столов
# (`canvases/tavern-front-base.png`), иначе ответ «вставил» бы их заново. Сзади вперёд.
FRONT = CITY / 'canvases/tavern-front-base.png'
JOBS = [
    ('police', (1530, 250, 420, 280), (1575, 255, 315, 275)),   # девятки пик у верёвки угла кланов
    ('table-2', (560, 300, 560, 373), (650, 330, 420, 330)),    # восьмёрки-строители
    ('table-3', (1160, 300, 560, 373), (1240, 340, 440, 310)),  # четвёрки-почтальоны
    ('table-1', (140, 380, 560, 373), (145, 400, 545, 350), FRONT),    # Король червей — мэр, один, у сцены
    # Свидание семёрок — без почтальонов и сапог полиции над ним: генератор их перерисовал.
    ('table-4', (1390, 400, 560, 373), (1490, 465, 440, 295), FRONT),
]
CHANGED = (20, 50)             # разница (из 255): от — до полностью из ответа
CORE, NEAR = 150, 10           # жители — пятна сильной разницы не меньше CORE px; мягкая маска — в NEAR px от них
GROW, SOFT, FEATHER = 2, 3, 10  # расширение и размытие маски, мягкий край участка, px


def read(path):
    return cv2.imdecode(np.fromfile(str(path), np.uint8), cv2.IMREAD_COLOR)


def align(original, edited, outside):
    """Ответ на кусок: подобие по ORB (RANSAC), уточнение ECC аффинно — всё без места жителей."""
    h, w = original.shape[:2]
    big = lambda image: cv2.resize(image, (w * 2, h * 2), interpolation=cv2.INTER_CUBIC)
    orb = cv2.ORB_create(4000)
    mask = cv2.resize(outside, (w * 2, h * 2), interpolation=cv2.INTER_NEAREST)
    k1, d1 = orb.detectAndCompute(cv2.cvtColor(big(original), cv2.COLOR_BGR2GRAY), mask)
    k2, d2 = orb.detectAndCompute(cv2.cvtColor(big(edited), cv2.COLOR_BGR2GRAY), mask)
    warp = np.eye(2, 3, dtype=np.float32)
    if d1 is not None and d2 is not None:
        matches = sorted(cv2.BFMatcher(cv2.NORM_HAMMING, crossCheck=True).match(d1, d2), key=lambda m: m.distance)[:600]
        if len(matches) >= 12:
            src = np.float32([k2[m.trainIdx].pt for m in matches]) / 2
            dst = np.float32([k1[m.queryIdx].pt for m in matches]) / 2
            found, inliers = cv2.estimateAffinePartial2D(src, dst, method=cv2.RANSAC, ransacReprojThreshold=2.5)
            if found is not None and inliers.sum() >= 12:
                # ECC ждёт обратное: точка куска → точка ответа.
                warp = cv2.invertAffineTransform(found).astype(np.float32)
    gray = lambda image: cv2.GaussianBlur(cv2.cvtColor(image, cv2.COLOR_BGR2GRAY), (5, 5), 0).astype(np.float32)
    criteria = (cv2.TERM_CRITERIA_EPS | cv2.TERM_CRITERIA_COUNT, 200, 1e-6)
    try: _, warp = cv2.findTransformECC(gray(original), gray(edited), warp, cv2.MOTION_AFFINE, criteria, outside, 5)
    except cv2.error: pass
    return cv2.warpAffine(edited, warp, (w, h), flags=cv2.INTER_LINEAR | cv2.WARP_INVERSE_MAP, borderMode=cv2.BORDER_REFLECT), warp


def patch(clean, name, crop, area, base=None):
    answer = CITY / 'canvases' / ('tavern-' + name + '-patrons.png')
    if not answer.exists():
        print('нет ответа:', answer.name)
        return None
    cx, cy, cw, ch = crop
    px, py, pw, ph = area
    original = (clean if base is None else read(base))[cy:cy + ch, cx:cx + cw]
    edited = cv2.resize(read(answer), (cw, ch), interpolation=cv2.INTER_AREA)
    outside = np.full((ch, cw), 255, np.uint8)
    outside[py - cy:py - cy + ph, px - cx:px - cx + pw] = 0
    aligned, warp = align(original, edited, outside)
    keep = outside > 0
    gain = float(np.clip(original[keep].mean() / max(1.0, aligned[keep].mean()), .85, 1.18))
    aligned = np.clip(aligned.astype(np.float32) * gain, 0, 255)
    rest = np.abs(aligned[keep] - original[keep].astype(np.float32)).mean()
    part = aligned[py - cy:py - cy + ph, px - cx:px - cx + pw]
    base = original[py - cy:py - cy + ph, px - cx:px - cx + pw].astype(np.float32)
    difference = cv2.GaussianBlur(np.abs(part - base).max(axis=2), (5, 5), 0)
    count, labels, stats, _ = cv2.connectedComponentsWithStats((difference >= CHANGED[1]).astype(np.uint8))
    core = np.isin(labels, [i for i in range(1, count) if stats[i, cv2.CC_STAT_AREA] >= CORE]).astype(np.uint8)
    core = cv2.morphologyEx(core, cv2.MORPH_CLOSE, np.ones((9, 9), np.uint8))
    near = cv2.dilate(core, np.ones((2 * NEAR + 1, 2 * NEAR + 1), np.uint8)).astype(np.float32)
    alpha = np.maximum(np.clip((difference - CHANGED[0]) / (CHANGED[1] - CHANGED[0]), 0, 1) * near, core)
    alpha = cv2.dilate(alpha, np.ones((2 * GROW + 1, 2 * GROW + 1), np.uint8))
    alpha = cv2.GaussianBlur(alpha, (2 * SOFT + 1, 2 * SOFT + 1), 0)
    ramp = lambda n: np.clip(np.minimum(np.arange(n), np.arange(n)[::-1]) / FEATHER, 0, 1)
    alpha *= np.minimum.outer(ramp(ph), ramp(pw))
    cv2.imencode('.png', np.dstack([part, alpha * 255]).round().astype(np.uint8))[1].tofile(
        str(CITY / 'canvases' / ('tavern-' + name + '-patch.png')))
    print(name, '- warp', np.round(warp, 3).tolist(), '; gain', round(gain, 3), '; mean difference outside', round(float(rest), 1),
          '; changed share', round(float(alpha.mean()), 3))
    return part, alpha


def main():
    clean = read(CLEAN)
    hall = clean.astype(np.float32)
    for name, crop, area, *base in JOBS:
        result = patch(clean, name, crop, area, *base)
        if result is None: continue
        part, alpha = result
        px, py, pw, ph = area
        region = hall[py:py + ph, px:px + pw]
        hall[py:py + ph, px:px + pw] = part * alpha[..., None] + region * (1 - alpha[..., None])
    image = hall.round().clip(0, 255).astype(np.uint8)
    for path in (OUT, COPY):
        cv2.imencode('.png', image)[1].tofile(str(path))
    print('->', OUT.name, COPY.name)


if __name__ == '__main__':
    main()
