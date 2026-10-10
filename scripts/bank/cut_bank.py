"""Cut the supplied bank props and import the unchanged desk into Unity.

    python scripts/bank/cut_bank.py

Inputs are the author's artwork in screens/references/city. Magenta is removed
with the same linear matte/unmixing as six-house/cut_quests.py. Explicit regions
keep disconnected worn stamp marks, highlights and the magnifier glass together.
No artwork is generated or painted. Existing .meta GUIDs are preserved on rerun.
"""
from pathlib import Path
import re
import shutil
import uuid

import cv2
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
CITY = ROOT / 'screens/references/city'
OUT = ROOT / 'unity/CardGameClient/Assets/Resources/UI/Hub'
META = OUT / 'six-guide.png.meta'
PAD = 4
SHEETS = {
    'bank-paper-sheet.png': (
        ('bank-contract', (130, 20, 695, 698)),
        ('bank-receipt', (765, 150, 1340, 540)),
        ('bank-pen', (30, 690, 555, 1055)),
        ('bank-stamp-ring', (580, 697, 945, 1060)),
        ('bank-magnifier', (972, 638, 1422, 1070)),
    ),
    'bank-seal-sheet.png': (
        ('bank-wax-seal', (15, 197, 617, 815)),
        ('bank-wax-drop', (630, 390, 996, 747)),
        ('bank-ring', (1018, 309, 1440, 770)),
    ),
    'bank-stamp-face-sheet.png': (
        ('bank-stamp-face', (0, 0, 1254, 1254)),
    ),
}


def read(path):
    image = cv2.imdecode(np.fromfile(str(path), np.uint8), cv2.IMREAD_COLOR)
    if image is None:
        raise ValueError(f'Cannot read {path}')
    return image


def meta(path, width, height):
    target = path.with_suffix(path.suffix + '.meta')
    if target.exists():
        return
    text = META.read_text(encoding='utf-8')
    text = re.sub(r'^guid: .*$', 'guid: ' + uuid.uuid4().hex, text, flags=re.M)
    text = text.replace('map-icon_0', path.stem)
    size = 2 ** int(np.ceil(np.log2(max(width, height))))
    size = max(32, min(8192, size))
    text = re.sub(r'maxTextureSize: \d+', f'maxTextureSize: {size}', text)
    text = re.sub(r'width: \d+', f'width: {width}', text)
    text = re.sub(r'height: \d+', f'height: {height}', text)
    text = re.sub(r'spriteID: [0-9a-f]+', lambda _: 'spriteID: ' + uuid.uuid4().hex, text)
    target.write_text(text, encoding='utf-8')


def cut(sheet, pieces):
    image = read(CITY / sheet).astype(np.float32) / 255
    border = np.concatenate([image[:8].reshape(-1, 3), image[-8:].reshape(-1, 3),
                             image[:, :8].reshape(-1, 3), image[:, -8:].reshape(-1, 3)])
    magenta = np.median(border, axis=0)
    key = lambda pixels: np.minimum(pixels[..., 2], pixels[..., 0]) - pixels[..., 1]
    if key(magenta) < .5:
        raise ValueError(f'{sheet}: expected a magenta background')
    alpha = np.clip(1 - key(image) / key(magenta), 0, 1)
    # The author-provided blue stamp has JPEG-like pink/gray paper noise.
    # Keep only blue ink; otherwise that neutral noise becomes a solid haze.
    if sheet == 'bank-stamp-face-sheet.png':
        blue_ink = image[..., 0] - image[..., 2]
        alpha *= np.clip((blue_ink - .06) / .06, 0, 1)
    alpha[alpha < .06] = 0
    safe = np.maximum(alpha, 1e-3)[..., None]
    foreground = np.clip((image - (1 - alpha[..., None]) * magenta) / safe, 0, 1)
    foreground[alpha == 0] = 0
    rgba = np.dstack([foreground, alpha])
    for name, (left, top, right, bottom) in pieces:
        region = rgba[top:bottom, left:right]
        ys, xs = np.nonzero(region[..., 3] > .02)
        if not len(xs):
            raise ValueError(f'{name}: empty sprite')
        x0 = max(0, int(xs.min()) - PAD)
        y0 = max(0, int(ys.min()) - PAD)
        x1 = min(region.shape[1], int(xs.max()) + 1 + PAD)
        y1 = min(region.shape[0], int(ys.max()) + 1 + PAD)
        result = np.round(region[y0:y1, x0:x1] * 255).astype(np.uint8)
        path = OUT / (name + '.png')
        cv2.imencode('.png', result)[1].tofile(str(path))
        height, width = result.shape[:2]
        meta(path, width, height)
        print(name, 'size', width, 'x', height, '; source box',
              left + x0, top + y0, left + x1, top + y1)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    desk = CITY / 'bank-desk-v1.png'
    target = OUT / 'bank-desk.png'
    shutil.copyfile(desk, target)
    height, width = read(desk).shape[:2]
    meta(target, width, height)
    print('bank-desk size', width, 'x', height, '; unchanged source')
    for sheet, pieces in SHEETS.items():
        cut(sheet, pieces)


if __name__ == '__main__':
    main()
