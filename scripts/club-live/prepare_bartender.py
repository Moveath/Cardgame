"""Recover the approved King bartender clip's camera and build its masked loop.

Requires numpy, opencv-python-headless and imageio-ffmpeg. Example from repo root:

    python scripts/club-live/prepare_bartender.py --source "bartender.mp4" --preview

The calibration belongs to the approved b_ACTION_AND_PERFECT_L (1).mp4 only.
It compensates for that generator's immediate zoom, rather than tracking the
actor every frame (which would remove the intended acting). The source hash is
checked to avoid silently applying this transform to another generation.

Output defaults to ignored QA storage. This script does not register Unity
assets or change PlayerHubInterior.ClubVideos. After reviewing the preview,
copy the MP4/mask and use metadata.json's rect for that registration.
"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile


REPO = Path(__file__).resolve().parents[2]
SOURCE_SHA256 = "198a8faffb4d4319d7b131e0b37b546bd10e1bd382db11590d7965b539ba1a9b"
SOURCE_SIZE = (768, 768)
SOURCE_FPS = 24
OUTPUT_FPS = 48
START, END, FADE = 27, 217, 10  # Source frame indices; END is exclusive.
CANVAS_SIZE = (768, 864)
HALL_WIDTH = 1916
HALL_CROP = (635, 260, 875, 530)
HALL_UNITS_PER_PIXEL = 240 / 768
TRANSFORM = ((.578290880, -.005049459, 313.523446),
             (.005049459, .578290880, 4.136809))
MASK_POINTS = ((322, -55), (622, -45), (628, -10), (754, -45),
               (766, 110), (756, 249), (719, 338), (363, 338),
               (331, 276), (315, 208), (326, 130), (317, 28))


def arguments():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--source", required=True, type=Path,
                        help="Approved original MP4, checked by SHA-256.")
    parser.add_argument("--hall", type=Path,
                        default=REPO / "unity/CardGameClient/Assets/Resources/UI/Hub/club-hall.png",
                        help="Full static hall image; defaults to the shipped hall.")
    parser.add_argument("--output", type=Path,
                        default=REPO / "screens/qa/bartender-prepared",
                        help="Output directory; defaults to ignored QA storage.")
    parser.add_argument("--preview", action="store_true",
                        help="Also write two composited loops, a contact sheet and selected frames.")
    return parser.parse_args()


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def read_image(path):
    # imread does not reliably support Cyrillic paths on all Windows builds.
    result = cv2.imdecode(np.fromfile(str(path), np.uint8), cv2.IMREAD_COLOR)
    if result is None:
        raise ValueError(f"Cannot decode hall image: {path}")
    return result


def save_image(path, pixels):
    ok, data = cv2.imencode(".png", np.uint8(np.clip(pixels, 0, 255)))
    if not ok:
        raise RuntimeError(f"Cannot encode PNG: {path}")
    data.tofile(str(path))


def reference_and_mask(hall):
    unit = hall.shape[1] / HALL_WIDTH
    left, top, right, bottom = (round(value * unit) for value in HALL_CROP)
    if left < 0 or top < 0 or right > hall.shape[1] or bottom > hall.shape[0]:
        raise ValueError("Hall image is too short for the calibrated 1916 x 821 layout.")
    still = cv2.resize(hall[top:bottom, left:right], CANVAS_SIZE,
                       interpolation=cv2.INTER_AREA)
    polygon = np.array(MASK_POINTS, np.int32)
    # The original square began at hall y=290. Expand 30 hall units upward so
    # the whole head and raised goblet remain visible: 30 / (240 / 768) = 96 px.
    polygon[:, 1] += 96
    hard = np.zeros((CANVAS_SIZE[1], CANVAS_SIZE[0]), np.uint8)
    cv2.fillPoly(hard, [polygon], 255)
    alpha = cv2.GaussianBlur(hard.astype(np.float32) / 255, (0, 0), 5)
    # Hold the painted bar rail completely still while feathering into it.
    alpha *= np.clip((436 - np.arange(CANVAS_SIZE[1])[:, None]) / 12, 0, 1)
    ys, xs = np.nonzero(alpha > .001)
    x0, y0, x1, y1 = int(xs.min()), int(ys.min()), int(xs.max() + 1), int(ys.max() + 1)
    # H.264 yuv420p requires even dimensions.
    if (x1 - x0) % 2:
        x0 -= 1
    if (y1 - y0) % 2:
        y1 += 1
    return still, alpha, (x0, y0, x1, y1)


def read_calibrated_frames(source, still, bounds):
    capture = cv2.VideoCapture(str(source))
    if not capture.isOpened():
        raise ValueError(f"Cannot open source video: {source}")
    fps = capture.get(cv2.CAP_PROP_FPS)
    dimensions = (round(capture.get(cv2.CAP_PROP_FRAME_WIDTH)),
                  round(capture.get(cv2.CAP_PROP_FRAME_HEIGHT)))
    if dimensions != SOURCE_SIZE or abs(fps - SOURCE_FPS) > .01:
        capture.release()
        raise ValueError(f"Calibration requires 768 x 768 at 24 fps; got {dimensions} at {fps}.")
    x0, y0, x1, y1 = bounds
    transform = np.float32(TRANSFORM)
    frames, gain, count = {}, None, 0
    try:
        while True:
            ok, frame = capture.read()
            if not ok:
                break
            if frame.shape != (768, 768, 3):
                raise ValueError(f"Unexpected dimensions at source frame {count}: {frame.shape}")
            if count == 0:
                # Frame zero is still the original wide shot. Fit codec colour
                # only where it already agrees with the corresponding painting.
                near = np.max(abs(frame.astype(float) - still[96:]), axis=2) < 28
                if np.count_nonzero(near) < 1000:
                    raise ValueError("Hall reference does not match the source's initial wide frame.")
                gain = np.array([np.polyfit(frame[..., channel][near],
                                          still[96:, ..., channel][near], 1)
                                 for channel in range(3)])
                if not np.isfinite(gain).all():
                    raise ValueError("Invalid colour calibration.")
            if START - FADE <= count < END:
                warped = cv2.warpAffine(frame, transform, CANVAS_SIZE,
                                       borderMode=cv2.BORDER_CONSTANT)
                # Keep only byte-valued cropped frames. Avoid retaining full
                # float canvases for every frame of the input movie.
                cropped = warped[y0:y1, x0:x1].astype(np.float32)
                frames[count] = np.clip(cropped * gain[:, 0] + gain[:, 1], 0, 255).astype(np.uint8)
            count += 1
    finally:
        capture.release()
    if count < END:
        raise ValueError(f"Source ended after {count} frames; at least {END} are required.")
    return frames, gain, count, fps


def flow_fields(a, b):
    first, second = (cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY) for frame in (a, b))
    forward = cv2.calcOpticalFlowFarneback(first, second, None, .5, 4, 23, 5, 7, 1.5, 0)
    backward = cv2.calcOpticalFlowFarneback(second, first, None, .5, 4, 23, 5, 7, 1.5, 0)
    yy, xx = np.mgrid[0:a.shape[0], 0:a.shape[1]].astype(np.float32)
    return forward, backward, xx, yy


def blend_flow(a, b, weight, fields=None):
    forward, backward, xx, yy = fields if fields is not None else flow_fields(a, b)
    first = cv2.remap(a, xx - weight * forward[..., 0], yy - weight * forward[..., 1],
                      cv2.INTER_LINEAR, borderMode=cv2.BORDER_REFLECT)
    second = cv2.remap(b, xx - (1 - weight) * backward[..., 0],
                       yy - (1 - weight) * backward[..., 1], cv2.INTER_LINEAR,
                       borderMode=cv2.BORDER_REFLECT)
    return cv2.addWeighted(first, 1 - weight, second, weight, 0)


def smooth_loop(frames):
    loop = [frames[index] for index in range(START, END)]
    for index in range(FADE):
        weight = (index + 1) / (FADE + 1)
        weight = weight * weight * (3 - 2 * weight)
        loop[-FADE + index] = blend_flow(loop[-FADE + index],
                                        frames[START - FADE + index], weight)
    # The 24 fps source repeats poses in pairs. Recover its 12 fps samples and
    # insert three bidirectional-flow frames per sample, including the wrap.
    # This produces 48 fps without changing the 190 / 24 second action duration.
    base = loop[::2]
    for index, first in enumerate(base):
        second = base[(index + 1) % len(base)]
        fields = flow_fields(first, second)
        yield first
        for weight in (.25, .5, .75):
            yield blend_flow(first, second, weight, fields)


def write_video(path, frames, size, fps):
    command = [imageio_ffmpeg.get_ffmpeg_exe(), "-y", "-v", "error", "-f", "rawvideo",
               "-pix_fmt", "bgr24", "-s", f"{size[0]}x{size[1]}", "-r", str(fps),
               "-i", "-", "-an", "-vf", "scale=in_range=full:out_range=tv:out_color_matrix=bt709",
               "-c:v", "libx264", "-profile:v", "baseline", "-bf", "0", "-pix_fmt", "yuv420p",
               "-colorspace", "bt709", "-color_primaries", "bt709", "-color_trc", "bt709",
               "-color_range", "tv", "-crf", "18", "-preset", "medium", "-g", "24", "-movflags",
               "+faststart", str(path)]
    # A real file for stderr cannot fill up and deadlock while stdin is fed.
    with tempfile.TemporaryFile() as errors:
        process = subprocess.Popen(command, stdin=subprocess.PIPE, stderr=errors)
        failure, count = None, 0
        try:
            for frame in frames:
                if frame.shape != (size[1], size[0], 3) or frame.dtype != np.uint8:
                    raise ValueError(f"Unexpected encoder frame: {frame.shape}, {frame.dtype}")
                process.stdin.write(frame.tobytes())
                count += 1
        except BaseException as exc:
            failure = exc
        finally:
            try:
                process.stdin.close()
            except BrokenPipeError:
                pass
            return_code = process.wait()
        errors.seek(0)
        message = errors.read().decode("utf-8", errors="replace").strip()
    if failure is not None:
        if isinstance(failure, BrokenPipeError):
            raise RuntimeError(f"ffmpeg stopped while writing {path}: {message}") from failure
        raise failure
    if return_code != 0 or count == 0:
        raise RuntimeError(f"ffmpeg failed ({return_code}, {count} frames): {message}")
    return count


def composite(frame, still, mask, bounds):
    x0, y0, x1, y1 = bounds
    result = still.copy()
    result[y0:y1, x0:x1] = np.clip(frame * mask[..., None]
                                  + still[y0:y1, x0:x1] * (1 - mask[..., None]),
                                  0, 255).astype(np.uint8)
    return result


def main():
    args = arguments()
    # Delay optional dependencies so --help also works before installation.
    global cv2, np, imageio_ffmpeg
    import cv2
    import numpy as np
    import imageio_ffmpeg

    source, hall_path, output = args.source.resolve(), args.hall.resolve(), args.output.resolve()
    source_hash = sha256(source)
    if source_hash != SOURCE_SHA256:
        raise ValueError("Source SHA-256 does not match the approved bartender clip. "
                         "A different generation needs a new camera/mask calibration.")
    still, alpha, bounds = reference_and_mask(read_image(hall_path))
    frames, gain, source_count, source_fps = read_calibrated_frames(source, still, bounds)
    x0, y0, x1, y1 = bounds
    mask = alpha[y0:y1, x0:x1]
    output.mkdir(parents=True, exist_ok=True)
    rect = [635 + x0 * HALL_UNITS_PER_PIXEL, 260 + y0 * HALL_UNITS_PER_PIXEL,
            (x1 - x0) * HALL_UNITS_PER_PIXEL, (y1 - y0) * HALL_UNITS_PER_PIXEL]
    print(f"Preparing bartender loop; hall rect: {rect}", flush=True)
    samples, differences = [], []
    first_frame, previous_frame = None, None

    def monitored_frames():
        nonlocal first_frame, previous_frame
        for frame in smooth_loop(frames):
            if first_frame is None:
                first_frame = frame
            if previous_frame is not None:
                differences.append(float(np.mean(abs(frame.astype(float) - previous_frame)
                                                  * mask[..., None])))
            previous_frame = frame
            if args.preview:
                samples.append(frame)
            yield frame

    frame_count = write_video(output / "club-bartender-play.mp4", monitored_frames(),
                              (x1 - x0, y1 - y0), OUTPUT_FPS)
    expected_count = (END - START) // 2 * 4
    if frame_count != expected_count:
        raise RuntimeError(f"Expected {expected_count} output frames, got {frame_count}.")
    seam = float(np.mean(abs(first_frame.astype(float) - previous_frame) * mask[..., None]))
    differences.append(seam)
    save_image(output / "club-bartender-mask.png", mask * 255)
    if args.preview:
        save_image(output / "mask.png", alpha * 255)
        preview_frames = (composite(frame, still, mask, bounds)
                          for _ in range(2) for frame in samples)
        write_video(output / "preview.mp4", preview_frames, CANVAS_SIZE, OUTPUT_FPS)
        thumbs = []
        for index in (0, 48, 120, 180, 240, 300, frame_count - 20, frame_count - 1):
            full = composite(samples[index], still, mask, bounds)
            save_image(output / f"composite-{index}.png", full)
            small = cv2.resize(full, (384, 432))
            cv2.putText(small, str(index), (8, 20), cv2.FONT_HERSHEY_SIMPLEX,
                        .6, (255, 255, 255), 1)
            thumbs.append(small)
        save_image(output / "contact.png", np.concatenate([
            np.concatenate(thumbs[index:index + 4], axis=1) for index in (0, 4)], axis=0))
    metadata = dict(source=str(source), source_sha256=source_hash,
                    hall=str(hall_path), hall_sha256=sha256(hall_path),
                    source_size=list(SOURCE_SIZE), source_fps=source_fps,
                    source_frame_count=source_count, start=START, end=END,
                    end_exclusive=True, fade=FADE, fps=OUTPUT_FPS, frame_count=frame_count,
                    duration_seconds=frame_count / OUTPUT_FPS, rect=rect,
                    pixel_crop=list(bounds), canvas_size=list(CANVAS_SIZE),
                    transform=TRANSFORM, colour_fit_bgr=gain.tolist(),
                    encoding=dict(codec="h264", profile="baseline", b_frames=0,
                                  pixel_format="yuv420p", colour_space="bt709", colour_range="tv"),
                    median=float(np.median(differences)), maximum=float(max(differences)),
                    seam=seam, opencv_version=cv2.__version__, numpy_version=np.__version__)
    (output / "metadata.json").write_text(json.dumps(metadata, indent=2), encoding="utf-8")
    print(f"Wrote {frame_count} frames at {OUTPUT_FPS} fps to {output}", flush=True)
    print(f"Masked mean step: median={metadata['median']:.4f}, seam={seam:.4f}")


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, RuntimeError) as error:
        print(f"Error: {error}", file=sys.stderr)
        raise SystemExit(1)
