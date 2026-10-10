"""Keep Durak's deck on the felt while the resident draws one card.

The selected v3a gesture erroneously picks up the entire deck and redraws
the trump below it. This narrowly restores the reference felt/deck/trump,
then keeps the moving glove and the single top card in front. It never
changes faces or the rest of the prepared action. Output is QA-only.
"""
import argparse
import json
import shutil
from pathlib import Path

import cv2
import numpy as np

from prepare_table_clips import load, save, write_video


ROOT = Path(__file__).resolve().parents[2]
WORK = 1152
# Frame numbers refer to the prepared 24fps action, including its eight
# initial overlap frames. Corners follow only the printed top card plane;
# the hallucinated thick stack under that plane is deliberately excluded.
CARD_KEYS = {
    20: [(544, 701), (589, 701), (595, 733), (541, 734)],
    22: [(554, 702), (590, 701), (596, 731), (548, 736)],
    24: [(548, 681), (591, 681), (599, 716), (560, 727)],
    26: [(553, 661), (595, 660), (601, 705), (552, 717)],
    28: [(544, 649), (589, 646), (595, 694), (548, 706)],
    30: [(541, 635), (585, 631), (595, 682), (545, 699)],
    32: [(533, 621), (580, 620), (589, 671), (540, 686)],
    34: [(528, 615), (573, 611), (584, 667), (539, 677)],
    36: [(520, 608), (568, 603), (577, 657), (533, 668)],
    38: [(511, 594), (562, 590), (570, 643), (525, 652)],
    40: [(508, 581), (555, 576), (563, 623), (520, 636)],
}


def interpolated_card(frame):
    keys = sorted(CARD_KEYS)
    lo = max(k for k in keys if k <= frame)
    hi = min(k for k in keys if k >= frame)
    t = 0 if hi == lo else (frame-lo)/(hi-lo)
    return np.rint(np.array(CARD_KEYS[lo])*(1-t) + np.array(CARD_KEYS[hi])*t).astype(np.int32)


def glove_mask(full, frame):
    mask = np.zeros((WORK, WORK), np.uint8)
    if not 16 <= frame <= 42:
        return mask.astype(np.float32)
    hsv = cv2.cvtColor(full, cv2.COLOR_BGR2HSV)
    region = hsv[600:755, 460:615]
    cream = ((region[..., 1] < 88) & (region[..., 2] > 155)).astype(np.uint8)
    count, labels, stats, centers = cv2.connectedComponentsWithStats(cream)
    candidates = [i for i in range(1, count)
                  if stats[i, cv2.CC_STAT_AREA] > 140
                  and centers[i, 0] < 100 and centers[i, 1] < 145]
    if not candidates:
        raise RuntimeError('Glove segmentation failed at frame %d' % frame)
    chosen = max(candidates, key=lambda i: stats[i, cv2.CC_STAT_AREA])
    component = (labels == chosen).astype(np.uint8)
    contours, _ = cv2.findContours(component, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    cv2.drawContours(component, contours, -1, 1, cv2.FILLED)
    # Include its existing black silhouette and inner finger lines.
    component = cv2.dilate(component, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (7, 7)))
    mask[600:755, 460:615] = component
    return np.clip(cv2.GaussianBlur(mask.astype(np.float32), (0, 0), .55), 0, 1)


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--prepared', type=Path, default=ROOT/'screens/qa/durak-variation/action')
    p.add_argument('--out', type=Path, default=ROOT/'screens/qa/durak-variation/action-repaired')
    a = p.parse_args()
    a.out.mkdir(parents=True, exist_ok=True)
    baked = a.out/'baked'
    baked.mkdir(exist_ok=True)
    report = json.loads((a.prepared/'action-report.json').read_text(encoding='utf8'))
    x, y, w, h = report['crop']
    if report['fps'] != 24 or report['overlap_frames'] != 8:
        raise ValueError('This local repair is authored for the v3a 24fps gesture with eight overlap frames')
    still = cv2.resize(load(ROOT/'screens/references/city/video-frames/table-durak.png'),
                       (WORK, WORK), interpolation=cv2.INTER_AREA)
    cap = cv2.VideoCapture(str(a.prepared/'baked/club-durak-action.mp4'))
    repaired, inspections = [], []
    outside_max = 0
    frame = 0
    while True:
        ok, crop = cap.read()
        if not ok:
            break
        full = still.copy()
        full[y:y+h, x:x+w] = crop
        replacement = np.zeros((WORK, WORK), np.float32)
        replacement[687:783, 528:634] = 1
        replacement = cv2.GaussianBlur(replacement, (0, 0), 1.1)
        keep = glove_mask(full, frame)
        if 20 <= frame <= 40:
            card = np.zeros((WORK, WORK), np.uint8)
            cv2.fillConvexPoly(card, interpolated_card(frame), 1)
            card = cv2.GaussianBlur(card.astype(np.float32), (0, 0), .5)
            keep = np.maximum(keep, card)
        restore = replacement * (1-keep)
        fixed = np.rint(full.astype(np.float32)*(1-restore[..., None]) +
                       still.astype(np.float32)*restore[..., None]).astype(np.uint8)
        diff = cv2.absdiff(fixed, full)
        outside = replacement < 1e-6
        outside_max = max(outside_max, int(diff[outside].max()))
        repaired.append(fixed[y:y+h, x:x+w])
        if 8 <= frame <= 43:
            before = full[600:800, 460:680]
            after = fixed[600:800, 460:680]
            pair = np.hstack([before, after])
            cv2.putText(pair, '%d / source %.3fs' % (frame, (frame-8)/24), (5, 20),
                        cv2.FONT_HERSHEY_SIMPLEX, .45, (0, 255, 255), 1)
            inspections.append(pair)
        frame += 1
    cap.release()
    if len(repaired) != report['frames']:
        raise ValueError('Unexpected prepared action length')
    write_video(baked/'club-durak-action.mp4', repaired, (w, h), report['fps'])
    for name in ['club-durak-play.mp4', 'club-durak-mask.png']:
        shutil.copy2(a.prepared/'baked'/name, baked/name)
    (a.out/'action-report.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf8')
    for page in range(2):
        cells = inspections[page*18:(page+1)*18]
        save(a.out/('deck-inspect-%d.jpg' % (page+1)), np.vstack([
            np.hstack(cells[k:k+3]) for k in range(0, 18, 3)]))
    def previews():
        for crop in repaired:
            full = still.copy()
            full[y:y+h, x:x+w] = crop
            yield cv2.resize(full, (720, 720), interpolation=cv2.INTER_AREA)
    write_video(a.out/'preview.mp4', previews(), (720, 720), report['fps'])
    repair_report = {'frames': len(repaired), 'fps': report['fps'],
                     'outside_repair_max_pixel_change': outside_max,
                     'native_frames_inspected': list(range(36)),
                     'unchanged': ['faces', 'other resident', 'chairs', 'camera'],
                     'repair': 'reference deck and trump behind original moving glove and single lifted top card'}
    (a.out/'repair-report.json').write_text(json.dumps(repair_report, indent=2), encoding='utf8')
    print(json.dumps(repair_report), flush=True)


if __name__ == '__main__':
    main()
