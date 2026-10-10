"""Pair an existing moving table loop with a separately prepared action.

Prepare the action with prepare_table_clips.py --fade-in 0 --fade-out 0 first.
The two input reports must describe their crop in the 1152-square work image;
the action report also supplies overlap_frames and source_frames. Both clips
must have the same frame rate. No source or production asset is overwritten.

Example (paths may be absolute):
  python scripts/club-live/pair_table_variation.py durak \
    --idle-dir screens/qa/durak-idle/assets \
    --idle-report screens/qa/durak-idle/loop-report.json \
    --action-dir screens/qa/durak-action/assets \
    --action-report screens/qa/durak-action/action-report.json \
    --out screens/qa/durak-pair

Output: club-<game>-play.mp4, club-<game>-action.mp4, shared mask, pair-report.json,
a full-table preview.mp4, and full-resolution transition PNGs. Review these before
copying assets and the reported rect into the game. The production scheduler must
round its blend duration up to overlap_frames at this fps (currently
ceil(15/48 * 24) == 8, as in HubClubVideo.SetupVariation).

The action's old still padding is removed. Its new prefix is the moving idle tail,
its suffix the moving idle head. Short dissolves match the complete action image to
the corresponding idle phases without separating faces from bodies. Enlarging
the common mask preserves each old layer's appearance through alpha correction.

Do not paste idle faces over the action: the independently generated poses do not
track each other. That approach made the expressions slide over the card bodies.
Keep the complete action and append its exit, so the hand can finish returning.
For reversible ambient card inspection, --return-to-start adds an eased return
through the native action frames. This is not suitable for irreversible actions
such as a drink being poured or a card being discarded during an actual game.
"""
import argparse
import json
import subprocess
from pathlib import Path

import cv2
import imageio_ffmpeg
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
WORK = 1152
SIDE, TOP = 360, 300
TABLES = {'durak': 500, 'bluff': 845, 'chests': 1197, 'govno': 1563}


def load_image(path, flags=cv2.IMREAD_COLOR):
    image = cv2.imdecode(np.fromfile(str(path), np.uint8), flags)
    if image is None:
        raise ValueError('Unreadable image: ' + str(path))
    return image


def save_image(path, image):
    ok, encoded = cv2.imencode('.png', image)
    if not ok:
        raise RuntimeError('Image encoding failed: ' + str(path))
    encoded.tofile(str(path))


def u8(image):
    return np.rint(np.clip(image, 0, 255)).astype(np.uint8)


def encode(path, frames, size, fps):
    """Always reap ffmpeg, including broken pipes and exceptions in the generator."""
    process = subprocess.Popen([
        imageio_ffmpeg.get_ffmpeg_exe(), '-y', '-loglevel', 'error',
        '-f', 'rawvideo', '-pix_fmt', 'bgr24', '-s', '%dx%d' % size,
        '-r', str(fps), '-i', '-', '-an', '-c:v', 'libx264',
        '-profile:v', 'main', '-pix_fmt', 'yuv420p', '-crf', '17',
        '-preset', 'slow', '-movflags', '+faststart', str(path),
    ], stdin=subprocess.PIPE)
    problem = None
    try:
        for frame in frames:
            if frame.shape != (size[1], size[0], 3) or frame.dtype != np.uint8:
                raise ValueError('Unexpected video frame shape/type: ' + str(frame.shape))
            process.stdin.write(np.ascontiguousarray(frame).tobytes())
    except BaseException as error:
        problem = error
    finally:
        try:
            process.stdin.close()
        except BrokenPipeError as error:
            problem = problem or error
        return_code = process.wait()
    if problem is not None:
        raise RuntimeError('Video encoding interrupted: ' + str(path)) from problem
    if return_code != 0:
        raise RuntimeError('Video encoding failed (%d): %s' % (return_code, path))


def crop_from(report):
    crop = report.get('crop')
    if not isinstance(crop, list) or len(crop) != 4 or any(int(v) != v for v in crop):
        raise ValueError('Report must contain integer crop: [x, y, width, height]')
    x, y, w, h = map(int, crop)
    if min(x, y) < 0 or min(w, h) <= 0 or x+w > WORK or y+h > WORK:
        raise ValueError('Report crop lies outside the %d-square work image' % WORK)
    return x, y, w, h


def common_crop(crops):
    # Include every old crop pixel, even zero-alpha padding; preserve the old
    # alpha exactly instead of truncating low-opacity feather pixels.
    x0, y0 = min(c[0] for c in crops), min(c[1] for c in crops)
    x1, y1 = max(c[0]+c[2] for c in crops), max(c[1]+c[3] for c in crops)
    if (x1-x0) % 2:
        if x1 < WORK:
            x1 += 1
        else:
            x0 -= 1
    if (y1-y0) % 2:
        if y1 < WORK:
            y1 += 1
        else:
            y0 -= 1
    return x0, y0, x1-x0, y1-y0


def local_slice(crop, common):
    x, y, w, h = crop
    return np.s_[y-common[1]:y-common[1]+h, x-common[0]:x-common[0]+w]


def mask_in_common(path, crop, common):
    mask = load_image(path, cv2.IMREAD_GRAYSCALE)
    if mask.shape != (crop[3], crop[2]):
        raise ValueError('Mask dimensions disagree with report: ' + str(path))
    canvas = np.zeros((common[3], common[2]), np.uint8)
    canvas[local_slice(crop, common)] = mask
    return canvas


def reframed_frames(path, report, crop, common, old_alpha, new_alpha, still):
    """newRaw * newAlpha + still * (1-newAlpha) == old composition.

    Written as a delta from the still to avoid unstable subtraction at the
    low-alpha edge. Rounding is the only loss before the subsequent video encode.
    """
    cap = cv2.VideoCapture(str(path))
    if not cap.isOpened():
        raise ValueError('Cannot open video: ' + str(path))
    fps = float(cap.get(cv2.CAP_PROP_FPS))
    if abs(fps-float(report['fps'])) > .01:
        cap.release()
        raise ValueError('Video FPS disagrees with report: ' + str(path))
    slot = local_slice(crop, common)
    base = still.astype(np.float32)
    ratio = np.divide(old_alpha, new_alpha, out=np.zeros_like(old_alpha), where=new_alpha > 0)[..., None]
    ratio = ratio[slot]
    count = 0
    try:
        while True:
            ok, frame = cap.read()
            if not ok:
                break
            if frame.shape[:2] != (crop[3], crop[2]):
                raise ValueError('Video dimensions disagree with report: ' + str(path))
            result = still.copy()
            result[slot] = u8(base[slot] + (frame.astype(np.float32)-base[slot])*ratio)
            count += 1
            yield result
    finally:
        cap.release()
    if 'frames' in report and count != int(report['frames']):
        raise ValueError('Decoded frame count %d differs from report %s: %s' % (count, report['frames'], path))


class Matcher:
    def __init__(self, still, alpha, transition='dissolve'):
        self.transition = transition
        self.still = still.astype(np.float32)
        self.alpha = alpha[..., None]
        self.h, self.w = still.shape[:2]
        self.yy, self.xx = np.mgrid[:self.h, :self.w].astype(np.float32)
        self.small = (max(16, self.w//2), max(16, self.h//2))

    def composite(self, frame):
        return frame.astype(np.float32)*self.alpha + self.still*(1-self.alpha)

    def flow(self, a, b):
        def gray(f):
            return cv2.resize(cv2.cvtColor(u8(f), cv2.COLOR_BGR2GRAY), self.small, interpolation=cv2.INTER_AREA)
        field = cv2.DISOpticalFlow_create(cv2.DISOPTICAL_FLOW_PRESET_MEDIUM).calc(gray(a), gray(b), None)
        field = cv2.resize(field, (self.w, self.h), interpolation=cv2.INTER_LINEAR)
        field[..., 0] *= self.w/self.small[0]
        field[..., 1] *= self.h/self.small[1]
        return field

    def blend(self, a, b, t):
        if t <= 0:
            return a.copy()
        if t >= 1:
            return b.copy()
        t = t*t*(3-2*t)
        if self.transition == 'dissolve':
            # Whole source frames only. No dense warp of pupils/brows and no
            # separately timed facial overlay. Keep this transition brief.
            return u8(a.astype(np.float32)*(1-t) + b.astype(np.float32)*t)
        # Flow sees the actual masked appearance, avoiding exaggerated vectors
        # in raw pixels that only contribute a tiny amount to the final image.
        av, bv = self.composite(a), self.composite(b)
        ab, ba = self.flow(av, bv), self.flow(bv, av)
        aa = cv2.remap(av, self.xx-t*ab[..., 0], self.yy-t*ab[..., 1],
                       cv2.INTER_CUBIC, borderMode=cv2.BORDER_REPLICATE)
        bb = cv2.remap(bv, self.xx-(1-t)*ba[..., 0], self.yy-(1-t)*ba[..., 1],
                       cv2.INTER_CUBIC, borderMode=cv2.BORDER_REPLICATE)
        visual = aa*(1-t) + bb*t
        raw_delta = np.divide(visual-self.still, self.alpha,
                              out=np.zeros_like(visual), where=self.alpha > 0)
        return u8(self.still + raw_delta)


def motion_metrics(frames, matcher):
    changes = []
    previous = matcher.composite(frames[0])
    for frame in frames[1:]:
        current = matcher.composite(frame)
        changes.append(float(np.abs(current-previous).mean()))
        previous = current
    return {'mean_adjacent_mae': float(np.mean(changes)),
            'max_adjacent_mae': max(changes), 'min_adjacent_mae': min(changes)}


def inspection_cycle(frames, ramp):
    """Complete the native inspection, ease at its turnaround, then put cards back.

    Interpolation is only between adjacent frames of the SAME performance. No
    face replacement, optical-flow deformation or morph to a different pose.
    Both the turnaround and final settling have zero source-time velocity.
    """
    last = len(frames) - 1
    if not 1 <= ramp < last / 2:
        raise ValueError('Return ramp must be shorter than half the action')

    def sample(position):
        position = float(np.clip(position, 0, last))
        left = int(np.floor(position))
        fraction = position - left
        if fraction < 1e-6 or left == last:
            return frames[left]
        return u8(frames[left].astype(np.float32)*(1-fraction)
                  + frames[left+1].astype(np.float32)*fraction)

    # Keep the complete forward gesture. Slow only its final ramp source frames,
    # doubling their duration while matching incoming native speed and ending at 0.
    positions = list(range(last-ramp+1))
    for step in range(1, 2*ramp+1):
        t = step / (2*ramp)
        positions.append(last-ramp + ramp*(2*t-t*t))
    duration = last+ramp
    for step in range(1, duration+1):
        if step < ramp:
            distance = step*step / (2*ramp)
        elif step > duration-ramp:
            distance = last-(duration-step)**2 / (2*ramp)
        else:
            distance = step-ramp/2
        positions.append(last-distance)
    return [sample(position) for position in positions], positions


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('game', choices=sorted(TABLES))
    parser.add_argument('--idle-dir', type=Path, required=True)
    parser.add_argument('--idle-report', type=Path, required=True)
    parser.add_argument('--action-dir', type=Path, required=True)
    parser.add_argument('--action-report', type=Path, required=True)
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--entry-frames', type=int, default=4)
    parser.add_argument('--exit-frames', type=int, default=4)
    parser.add_argument('--append-exit', action='store_true',
                        help='keep the complete action, then morph its final pose to idle')
    parser.add_argument('--return-to-start', action='store_true',
                        help='reversible card inspection only: use native frames to return cards to their initial pose')
    parser.add_argument('--return-ramp', type=int, default=10,
                        help='native frames eased at the turnaround and end of a reversible inspection')
    parser.add_argument('--freeze-below', type=int,
                        help='exclude action pixels at/below this global work-image Y (e.g. stray moving boots)')
    parser.add_argument('--transition', choices=('dissolve', 'flow'), default='dissolve',
                        help='dissolve preserves source geometry; flow is optional and needs close-up review')
    args = parser.parse_args()
    for source in (args.idle_dir, args.action_dir):
        if args.out.resolve() == source.resolve():
            parser.error('--out must be a separate review directory, not an input directory')
    if min(args.entry_frames, args.exit_frames) < 2:
        parser.error('Entry and exit transitions need at least two frames')
    if args.freeze_below is not None and not 0 <= args.freeze_below <= WORK:
        parser.error('--freeze-below must lie between 0 and 1152')
    idle_report = json.loads(args.idle_report.read_text(encoding='utf-8-sig'))
    action_report = json.loads(args.action_report.read_text(encoding='utf-8-sig'))
    fps = float(idle_report['fps'])
    if not np.isfinite(fps) or fps <= 0 or abs(fps-float(action_report['fps'])) > .01:
        parser.error('Idle and action reports must have matching positive frame rates')
    if action_report.get('fade_in', 0) != 0 or action_report.get('fade_out', 0) != 0:
        parser.error('Prepare the action with --fade-in 0 --fade-out 0')
    overlap = int(action_report['overlap_frames'])
    if overlap < 1:
        parser.error('Action report must specify a positive overlap_frames count')
    ic, ac = crop_from(idle_report), crop_from(action_report)
    crop = common_crop((ic, ac))
    x, y, w, h = crop
    name = 'club-' + args.game
    imask = mask_in_common(args.idle_dir/(name+'-mask.png'), ic, crop)
    amask = mask_in_common(args.action_dir/(name+'-mask.png'), ac, crop)
    if args.freeze_below is not None:
        amask[max(0, args.freeze_below-y):] = 0
    shared_mask = np.maximum(imask, amask)
    alpha = shared_mask.astype(np.float32)/255
    full_still = cv2.resize(load_image(ROOT/'screens/references/city/video-frames'/('table-'+args.game+'.png')),
                           (WORK, WORK), interpolation=cv2.INTER_AREA)
    still = full_still[y:y+h, x:x+w].copy()
    idle = list(reframed_frames(args.idle_dir/(name+'-play.mp4'), idle_report, ic, crop,
                                imask.astype(np.float32)/255, alpha, still))
    body = list(reframed_frames(args.action_dir/(name+'-action.mp4'), action_report, ac, crop,
                                amask.astype(np.float32)/255, alpha, still))
    body = body[overlap:-overlap]
    if len(body) != int(action_report['source_frames']):
        raise ValueError('Action padding/source_frames do not match its decoded length')
    source_body_frames = len(body)
    return_positions = None
    if args.return_to_start:
        body, return_positions = inspection_cycle(body, args.return_ramp)
    if len(idle) <= max(overlap, args.entry_frames, args.exit_frames):
        raise ValueError('Idle is shorter than the requested transition phases')
    if len(body) < args.entry_frames+args.exit_frames:
        raise ValueError('Action body is too short for both transitions')
    print('Pairing %s: idle %d, action body %d, overlap %d, crop %s' %
          (args.game, len(idle), len(body), overlap, crop), flush=True)
    matcher = Matcher(still, alpha, args.transition)
    for i in range(args.entry_frames):
        body[i] = matcher.blend(idle[i], body[i], i/(args.entry_frames-1))
    if args.append_exit:
        last_pose = body[-1]
        body += [matcher.blend(last_pose, idle[-args.exit_frames+i], i/(args.exit_frames-1))
                 for i in range(args.exit_frames)]
    else:
        for i in range(args.exit_frames):
            j = len(body)-args.exit_frames+i
            body[j] = matcher.blend(body[j], idle[-args.exit_frames+i], i/(args.exit_frames-1))
    action = idle[-overlap:] + body + idle[:overlap]
    args.out.mkdir(parents=True, exist_ok=True)
    encode(args.out/(name+'-play.mp4'), iter(idle), (w, h), fps)
    encode(args.out/(name+'-action.mp4'), iter(action), (w, h), fps)
    save_image(args.out/(name+'-mask.png'), shared_mask)

    def full(frame):
        result = full_still.copy()
        result[y:y+h, x:x+w] = u8(matcher.composite(frame))
        return result

    # During production overlap the two decoders show the same idle frames.
    # Skipping these duplicated phases gives the identical temporal sequence.
    # Show the join promptly: four seconds of waiting on each side are enough
    # for this action preview. The full idle clip is checked separately below.
    preview_idle = min(len(idle), max(overlap+1, round(4*fps)))
    sequence = idle[-preview_idle:-overlap] + action + idle[overlap:overlap+preview_idle]
    encode(args.out/'preview.mp4', (cv2.resize(full(f), (720, 720), interpolation=cv2.INTER_AREA)
                                    for f in sequence), (720, 720), fps)
    transitions = args.out/'transitions'
    transitions.mkdir(exist_ok=True)
    for label, indices in (
        ('entry', range(overlap, overlap+args.entry_frames)),
        ('exit', range(overlap+len(body)-args.exit_frames, overlap+len(body))),
    ):
        marks = np.linspace(indices.start, indices.stop-1, min(6, len(indices))).round().astype(int)
        for index in sorted(set(marks.tolist())):
            save_image(transitions/('%s-%03d.png' % (label, index)), full(action[index]))
    save_image(transitions/'idle-last.png', full(idle[-1]))
    save_image(transitions/'idle-first.png', full(idle[0]))
    unit = SIDE/WORK
    report = {
        'game': args.game, 'idle_input': str(args.idle_dir.resolve()),
        'action_input': str(args.action_dir.resolve()), 'fps': fps,
        'crop': list(crop), 'rect': [TABLES[args.game]-SIDE/2+x*unit, TOP+y*unit, w*unit, h*unit],
        'idle_frames': len(idle), 'action_frames': len(action), 'source_frames': source_body_frames,
        'idle_seconds': len(idle)/fps, 'action_seconds': len(action)/fps,
        'overlap_frames': overlap, 'overlap_seconds': overlap/fps,
        'entry_frames': args.entry_frames, 'exit_frames': args.exit_frames,
        'append_exit': args.append_exit, 'freeze_below': args.freeze_below,
        'return_to_start': args.return_to_start,
        'return_ramp_frames': args.return_ramp if args.return_to_start else 0,
        'source_positions': return_positions,
        'transition': args.transition, 'face_overlays': False,
        'idle': motion_metrics(idle, matcher), 'action': motion_metrics(action, matcher),
        'preview': motion_metrics(sequence, matcher),
        'idle_loop_seam_mae': float(np.abs(matcher.composite(idle[-1])-matcher.composite(idle[0])).mean()),
        'matching_prefix_mae': max(float(np.abs(a.astype(np.float32)-b.astype(np.float32)).mean())
                                   for a, b in zip(action[:overlap], idle[-overlap:])),
        'matching_suffix_mae': max(float(np.abs(a.astype(np.float32)-b.astype(np.float32)).mean())
                                   for a, b in zip(action[-overlap:], idle[:overlap])),
        'metrics_note': 'Composited working-resolution pixels before H.264 encoding; prefix/suffix are exact source frames.',
    }
    (args.out/'pair-report.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf8')
    print(json.dumps({k:v for k,v in report.items() if k != 'source_positions'},
                     ensure_ascii=False, indent=2), flush=True)


if __name__ == '__main__':
    main()
