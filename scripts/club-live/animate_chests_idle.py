"""Continuous, deterministic animation of the original Chests table drawing.

No generation service: pinned character deformation, asymmetric hand gestures and
drawn eyelids. Bake once to video; Unity keeps the existing two-decoder scheduler.
The table/background stay fixed, including the boundary of the existing video mask.
The existing card-exchange action is retained, with matching moving overlap frames.

Run after prepare_table_clips.py, or pass the preserved original action explicitly:
  python animate_chests_idle.py --action <original-action.mp4> --out <directory>
Default output is a review directory; --install copies reviewed files to Resources.
"""
import argparse
import json
import math
import shutil
import subprocess
from pathlib import Path

import cv2
import imageio_ffmpeg
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
RES = ROOT / 'unity/CardGameClient/Assets/Resources/UI/Hub/Video'
QA = ROOT / 'screens/qa/chests-living'
REF = ROOT / 'screens/references/city/video-frames'
WORK = 1152
SCALE = WORK / 1440
FPS, SECONDS = 30, 12
COUNT = FPS * SECONDS
OVERLAP = math.ceil(FPS * 15 / 48)
# Same crop as prepare_table_clips.py and PlayerHubInterior.TableVideos.
X, Y, W, H = 26, 262, 710, 798


def load(path, flags=cv2.IMREAD_COLOR):
    image = cv2.imdecode(np.fromfile(str(path), np.uint8), flags)
    if image is None:
        raise ValueError('Unreadable image: ' + str(path))
    return image


def save(path, image):
    cv2.imencode('.png', image)[1].tofile(str(path))


def encode(path, frames, size, fps=FPS):
    process = subprocess.Popen([imageio_ffmpeg.get_ffmpeg_exe(), '-y', '-loglevel', 'error',
        '-f', 'rawvideo', '-pix_fmt', 'bgr24', '-s', '%dx%d' % size, '-r', str(fps),
        '-i', '-', '-an', '-c:v', 'libx264', '-profile:v', 'main', '-pix_fmt', 'yuv420p',
        '-crf', '17', '-preset', 'slow', '-movflags', '+faststart', str(path)], stdin=subprocess.PIPE)
    try:
        for frame in frames:
            process.stdin.write(np.ascontiguousarray(frame).tobytes())
    finally:
        process.stdin.close()
    if process.wait() != 0:
        raise RuntimeError('Video encoding failed: ' + str(path))


def smooth(value):
    v = np.clip(value, 0, 1)
    return v * v * (3 - 2 * v)


def bump(t, center, duration):
    """A gesture with zero speed and acceleration at both ends."""
    d = abs(t - center) / (duration / 2)
    return 0 if d >= 1 else (1 - d * d) ** 3


def blink(t, peaks):
    amount = 0.
    for peak in peaks:
        delta = t - peak
        # Quick close, slightly longer reopening, no cross-fade between two pupils.
        if -.075 < delta < 0:
            amount = max(amount, float(smooth((delta + .075) / .075)))
        elif 0 <= delta < .11:
            amount = max(amount, 1 - float(smooth(delta / .11)))
    return amount


class Drawing:
    def __init__(self):
        self.still = cv2.resize(load(REF / 'table-chests.png'), (WORK, WORK), interpolation=cv2.INTER_AREA)
        self.empty = cv2.resize(load(REF / 'table-chests-empty.png'), (WORK, WORK), interpolation=cv2.INTER_AREA)
        self.empty_f = self.empty.astype(np.float32)
        self.still_f = self.still.astype(np.float32)
        self.x, self.y = np.meshgrid(np.arange(WORK, dtype=np.float32), np.arange(WORK, dtype=np.float32))
        self.mask = np.zeros((WORK, WORK), np.float32)
        original_mask = load(RES / 'club-chests-mask.png', cv2.IMREAD_GRAYSCALE)
        if original_mask.shape != (H, W):
            raise ValueError('Chests crop changed: update crop constants and TableVideos together.')
        self.mask[Y:Y + H, X:X + W] = original_mask / 255.
        # Pixels on the edge of the external mask must remain exactly still.
        self.safe = smooth((self.mask - .92) / .08)
        self.body = []
        contours = [
            [(170,499),(229,426),(326,407),(439,434),(482,486),(485,525),(441,582),
             (428,650),(530,666),(535,719),(508,804),(403,834),(347,948),(183,962),
             (160,928),(151,801),(168,697)],
            [(575,462),(624,389),(724,369),(806,392),(864,447),(869,482),(833,514),
             (830,612),(854,654),(888,722),(879,770),(796,782),(629,789),(521,770),
             (527,694),(552,621)],
        ]
        difference = np.max(np.abs(self.still.astype(np.float32) - self.empty), axis=2)
        for points in contours:
            region = np.zeros((WORK, WORK), np.uint8)
            cv2.fillPoly(region, [np.round(np.array(points) * SCALE).astype(np.int32)], 1)
            m = ((difference > 20) & (region > 0)).astype(np.uint8)
            m = cv2.morphologyEx(m, cv2.MORPH_CLOSE, np.ones((7, 7), np.uint8))
            found, _ = cv2.findContours(m, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
            cv2.drawContours(m, found, -1, 1, cv2.FILLED)
            m = cv2.dilate(m, np.ones((3, 3), np.uint8)) * region
            self.body.append(cv2.GaussianBlur(m.astype(np.float32), (0, 0), .7))
        # Eye opening: endpoint heights, upper arc and lower arc. Coordinates in 1440px reference.
        # Only the opening is painted: eyebrows, glasses, outline and face keep their source pixels.
        self.eyes = [
            (286,344,576,589,-4,43), (362,407,599,609,-4,34),
            (656,711,523,538,-4,39), (736,787,538,551,-4,37),
        ]
        self.cards = [(440,734,112,113), (706,702,116,101)]
        self.weights = [smooth((base*SCALE-self.y)/(250*SCALE)) for base in [958,785]]
        self.hand_weights = [np.exp(-2*((self.x-cx*SCALE)/(rx*SCALE))**2
                                  -2*((self.y-cy*SCALE)/(ry*SCALE))**2)
                             for cx,cy,rx,ry in self.cards]
        # Sample the pale lid material explicitly, never a pupil/glasses line.
        self.skin = []
        for sx,sy in [(320,564),(386,587),(688,515),(762,528)]:
            patch = self.still[int((sy-3)*SCALE):int((sy+3)*SCALE),
                               int((sx-3)*SCALE):int((sx+3)*SCALE)].reshape(-1,3)
            self.skin.append(np.percentile(patch,75,axis=0).astype(np.float32))

    def lids(self, t):
        frame = self.still.copy()
        values = [blink(t, [1.7, 5.5, 9.3]), blink(t, [3.2, 7.65, 7.95, 10.7])]
        for i, (left,right,ly,ry,top,bottom) in enumerate(self.eyes):
            amount = values[i // 2]
            if amount < .002:
                continue
            x0, x1 = int(left * SCALE), int(right * SCALE) + 1
            y0, y1 = int((min(ly, ry) - 10) * SCALE), int((max(ly, ry) + bottom + 6) * SCALE)
            xx = self.x[y0:y1, x0:x1]
            yy = self.y[y0:y1, x0:x1]
            u = np.clip((xx / SCALE - left) / (right - left), 0, 1)
            arc = 4 * u * (1 - u)
            base = (ly + (ry - ly) * u + top * arc) * SCALE
            lid = base + (bottom - top) * arc * SCALE * amount
            coverage = smooth((yy - base + 7) / 1.6) * smooth((lid - yy + .4) / 1.6)
            coverage *= smooth(u / .055) * smooth((1 - u) / .055)
            # Continue the skin just above the existing lid, preserving its golden lighting.
            skin = np.clip(self.skin[i] + (2-4*u)[...,None],0,255)
            roi = frame[y0:y1, x0:x1]
            roi[:] = np.clip(roi * (1 - coverage[...,None]) + skin * coverage[...,None], 0, 255).astype(np.uint8)
            us = np.linspace(.035,.965,50)
            line_x = (left + (right-left)*us)*SCALE
            line_y = (ly + (ry-ly)*us + (top+(bottom-top)*amount)*4*us*(1-us))*SCALE
            path = np.round(np.stack([line_x,line_y],axis=1)).astype(np.int32)
            cv2.polylines(frame,[path],False,(28,30,27),max(1,round(2.6*SCALE)),cv2.LINE_AA)
        return frame

    def frame(self, t):
        source = self.lids(t)
        output = self.still_f.copy()
        q = 2 * math.pi * t / SECONDS
        for i, alpha in enumerate(self.body):
            # Independent breathing/weight shifts; hands return to their original resting position.
            if i == 0:
                dx = 3.6 * math.sin(2*q) + 1.1 * math.sin(3*q)
                dy = -3.2 * math.sin(3*q) - 1.3 * math.sin(q)
                angle = .38 * math.sin(q)
                gesture = bump(t,2.8,1.5) - .7*bump(t,8.25,1.7)
                px,py,base = 310,755,958
            else:
                dx = -2.8 * math.sin(3*q) + 1.3 * math.sin(q)
                dy = -3.5 * math.sin(2*q) + .8 * math.sin(4*q)
                angle = -.4 * math.sin(2*q)
                gesture = -.9*bump(t,4.5,1.7) + bump(t,10.0,1.5)
                px,py,base = 715,740,785
            weight = self.weights[i]
            radians = math.radians(angle)
            mx = (dx*SCALE - (self.y-py*SCALE)*radians)*weight
            my = (dy*SCALE + (self.x-px*SCALE)*radians)*weight
            card_weight = self.hand_weights[i]
            # A little adjustment of the fan and wrists, not independent floating cards.
            mx += 5*SCALE*gesture*card_weight
            my += -8*SCALE*gesture*card_weight
            mapx,mapy = self.x-mx.astype(np.float32),self.y-my.astype(np.float32)
            moved = cv2.remap(source,mapx,mapy,cv2.INTER_CUBIC,borderMode=cv2.BORDER_REPLICATE).astype(np.float32)
            moved_alpha = cv2.remap(alpha,mapx,mapy,cv2.INTER_LINEAR,borderMode=cv2.BORDER_CONSTANT)
            # Reconstruct revealed pixels from the same empty table, without stretching the furniture.
            # Differential compositing is exactly the source at the neutral pose, including
            # antialiased edges; ordinary two-step alpha-over would darken that edge twice.
            output += (moved-self.empty_f)*moved_alpha[...,None] \
                - (self.still_f-self.empty_f)*alpha[...,None]
        # Keep external mask feather pixels identical to the backing still, even after encoding.
        output = self.still*(1-self.safe[...,None])+output*self.safe[...,None]
        return np.rint(np.clip(output,0,255)).astype(np.uint8)


def make_preview(folder):
    """Full table view: idle, card exchange, idle, with the same overlap phase as Unity."""
    still = cv2.resize(load(REF/'table-chests.png'),(WORK,WORK),interpolation=cv2.INTER_AREA)
    mask = load(folder/'club-chests-mask.png',cv2.IMREAD_GRAYSCALE)[...,None]/255.
    def frames():
        for name,limit in [('play',COUNT-OVERLAP),('action',None),('play',4*FPS)]:
            cap = cv2.VideoCapture(str(folder/('club-chests-'+name+'.mp4')))
            if limit is None: limit = round(cap.get(cv2.CAP_PROP_FRAME_COUNT))-OVERLAP
            for _ in range(limit):
                ok,crop=cap.read()
                if not ok: raise RuntimeError('Preview clip did not decode')
                frame=still.copy()
                frame[Y:Y+H,X:X+W]=np.rint(crop*mask+still[Y:Y+H,X:X+W]*(1-mask)).astype(np.uint8)
                yield cv2.resize(frame,(720,720),interpolation=cv2.INTER_AREA)
            cap.release()
    encode(QA/'preview.mp4',frames(),(720,720))


def main():
    parser = argparse.ArgumentParser(__doc__)
    parser.add_argument('--action',type=Path)
    parser.add_argument('--out',type=Path,default=QA/'baked')
    parser.add_argument('--install',action='store_true')
    args = parser.parse_args()
    QA.mkdir(parents=True,exist_ok=True)
    args.out.mkdir(parents=True,exist_ok=True)
    # Preserve pre-animation source once, so repeated renders never reprocess their own baked action.
    archived = QA/'original-action.mp4'
    if args.action is None and not archived.exists():
        shutil.copy2(RES/'club-chests-action.mp4',archived)
    action_source = args.action or archived
    drawing = Drawing()
    samples, differences, previous = [], [], None
    head,tail = [],[]
    def idle():
        nonlocal previous
        for n in range(COUNT):
            if n % 90 == 0: print('idle frame',n,'/',COUNT,flush=True)
            frame = drawing.frame(n/FPS)
            crop = frame[Y:Y+H,X:X+W]
            if n<OVERLAP: head.append(crop.copy())
            if n>=COUNT-OVERLAP: tail.append(crop.copy())
            if previous is not None: differences.append(float(np.abs(crop.astype(np.float32)-previous).mean()))
            previous = crop.astype(np.float32)
            if n in [0,51,84,96,135,165,230,248,279,300,321,359]:
                thumb=cv2.resize(frame,(384,384))
                cv2.putText(thumb,'%.2fs'%(n/FPS),(8,24),0,.65,(240,235,220),2,cv2.LINE_AA)
                samples.append(thumb)
            yield crop
    encode(args.out/'club-chests-play.mp4',idle(),(W,H))
    cap = cv2.VideoCapture(str(action_source))
    count = round(cap.get(cv2.CAP_PROP_FRAME_COUNT))
    if (round(cap.get(3)),round(cap.get(4)),round(cap.get(5))) != (W,H,FPS):
        raise ValueError('Unexpected action size or fps; do not silently resize it.')
    def action():
        yield from tail
        for n in range(count):
            ok,frame=cap.read()
            if not ok: raise RuntimeError('Truncated source action')
            if OVERLAP<=n<count-OVERLAP: yield frame
        cap.release()
        yield from head
    encode(args.out/'club-chests-action.mp4',action(),(W,H))
    shutil.copy2(RES/'club-chests-mask.png',args.out/'club-chests-mask.png')
    save(QA/'idle-contact.png',np.vstack([np.hstack(samples[n:n+4]) for n in range(0,12,4)]))
    save(QA/'body-masks.png',np.clip(sum(drawing.body)*255,0,255).astype(np.uint8))
    for t in [1.7,3.2]:
        save(QA/('blink-%.1f.png'%t),drawing.frame(t))
    first,last=drawing.frame(0),drawing.frame((COUNT-1)/FPS)
    report={'fps':FPS,'seconds':SECONDS,'frames':COUNT,'crop':[X,Y,W,H],
            'mean_adjacent_pixel_change':float(np.mean(differences)),
            'min_adjacent_pixel_change':float(min(differences)),
            'loop_seam_mae':float(np.abs(first.astype(np.float32)-last).mean()),
            'preserved_action_source':str(action_source.relative_to(ROOT)) if action_source.is_relative_to(ROOT) else str(action_source),
            'overlap_frames':OVERLAP}
    (QA/'bake-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
    print(json.dumps(report,ensure_ascii=False),flush=True)
    make_preview(args.out)
    if args.install:
        for name in ['club-chests-play.mp4','club-chests-action.mp4','club-chests-mask.png']:
            shutil.copy2(args.out/name,RES/name)


if __name__ == '__main__':
    main()
