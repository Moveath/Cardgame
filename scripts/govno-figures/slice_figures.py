"""Режет лист фигурки скина для «Говна» на кадры стадий.

Лист (см. docs/govno-figures.md): на сплошном пурпурном фоне #FF00FF слева направо
четыре позы в полный рост в одном масштабе — «На суше», «По колено», «По пояс», «По шею» —
и пятой картинкой отдельно головной убор (если его нет — пятой картинки нет).

    python scripts/govno-figures/slice_figures.py лист.png yegor_03
    python scripts/govno-figures/slice_figures.py --all screens/govno-sheets   # папка <скин>.png

Пишет в unity/CardGameClient/Assets/Resources/Govno/Stickers/Figures/<скин>/
stage-0..3.png и hat.png (+ .meta). Игра сама найдёт их по имени портрета.

Кадр 0 — весь рост; кадры 1–3 обрезаются по линии жижи своей стадии (доля роста от ступней —
CUT). Все кадры одной высоты над ступнями, поэтому игра выравнивает их по верху.
"""
import os
import sys
import uuid

import numpy as np
from PIL import Image
from scipy import ndimage

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
STICKERS = os.path.join(ROOT, "unity", "CardGameClient", "Assets", "Resources", "Govno", "Stickers")
META_TEMPLATE = os.path.join(STICKERS, "mud-pool.png.meta")
FULL_HEIGHT_PX = 384   # весь рост в готовом кадре; в игре фигура 175 единиц
CUT = [0.0, 0.29, 0.44, 0.68]   # колено, пояс, шея — как на пробнике Егора


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def key_magenta(rgb):
    """Тот же ключ, что в StickerCutout.shader, плюс очистка пурпурной каймы."""
    c = rgb / 255.0
    mag = np.clip(np.minimum(c[..., 0], c[..., 2]) - c[..., 1], 0, 1)
    alpha = 1 - smoothstep(0.05, 0.78, mag)
    clean = np.clip((c - np.stack([mag, np.zeros_like(mag), mag], -1)) / np.maximum(1 - mag, 1e-3)[..., None], 0, 1)
    cleanup = smoothstep(0.01, 0.10, mag)[..., None]
    c = c + (clean - c) * cleanup
    return c * 255, alpha


def spans(mask, min_gap=12, min_pixels=400):
    cols = mask.sum(0)
    out, start, gap = [], None, 0
    for x, v in enumerate(cols):
        if v > 0:
            if start is None:
                start = x
            gap = 0
        elif start is not None:
            gap += 1
            if gap >= min_gap:
                out.append((start, x - gap + 1))
                start, gap = None, 0
    if start is not None:
        out.append((start, len(cols)))
    return [(a, b) for a, b in out if mask[:, a:b].sum() >= min_pixels]


def main_parts(alpha):
    lab, n = ndimage.label(alpha > 0.35)
    if n <= 1:
        return alpha
    sizes = ndimage.sum(np.ones_like(alpha), lab, range(1, n + 1))
    keep = np.isin(lab, 1 + np.where(sizes >= sizes.max() * 0.02)[0])
    return alpha * ndimage.binary_dilation(keep, iterations=2)


def write(img, path):
    img.save(path)
    meta = path + ".meta"
    if not os.path.exists(meta):
        text = open(META_TEMPLATE, encoding="utf-8").read()
        lines = [("guid: " + uuid.uuid4().hex) if l.startswith("guid:") else l for l in text.split("\n")]
        open(meta, "w", encoding="utf-8", newline="\n").write("\n".join(lines))


def main(sheet_path, skin, out_root=None):
    rgb = np.asarray(Image.open(sheet_path).convert("RGB")).astype(np.float32)
    col, alpha = key_magenta(rgb)
    panels = spans(alpha > 0.5)
    if len(panels) not in (4, 5):
        sys.exit(f"ожидалось 4 позы и шляпа (4–5 картинок), найдено {len(panels)}: {panels}")

    figures = []
    for a, b in panels[:4]:
        al = main_parts(alpha[:, a:b])
        ys, xs = np.where(al > 0.02)
        figures.append(dict(a=a, b=b, alpha=al, top=ys.min(), feet=ys.max() + 1))
    h0 = figures[0]["feet"] - figures[0]["top"]
    height = max(f["feet"] - f["top"] for f in figures)
    scale = FULL_HEIGHT_PX / height

    out_dir = os.path.join(out_root or os.path.join(STICKERS, "Figures"), skin)
    os.makedirs(out_dir, exist_ok=True)
    for i, f in enumerate(figures):
        al, a = f["alpha"], f["a"]
        cut = int(round(f["feet"] - CUT[i] * h0))
        top = f["feet"] - height
        # ось тела — медиана x в полосе плеч под линией шеи: руки и шляпа её не сдвигают
        neck = int(round(f["feet"] - CUT[3] * h0))
        band = al[neck:neck + max(4, int(h0 * 0.08))] > 0.5
        cx = np.median(np.where(band)[1]) if band.any() else al.shape[1] / 2
        ys, xs = np.where(al[:cut] > 0.02)
        half = int(np.ceil(max(cx - xs.min(), xs.max() + 1 - cx))) + 2
        canvas = np.zeros((cut - top, 2 * half, 4), np.float32)
        src_top = max(0, top)
        for y in range(src_top, cut):
            x0 = int(round(cx)) - half
            lo, hi = max(0, x0), min(al.shape[1], x0 + 2 * half)
            canvas[y - top, lo - x0:hi - x0, :3] = col[y, a + lo:a + hi]
            canvas[y - top, lo - x0:hi - x0, 3] = al[y, lo:hi] * 255
        im = Image.fromarray(canvas.astype(np.uint8), "RGBA")
        size = (max(1, round(im.width * scale)), max(1, round(im.height * scale)))
        write(im.resize(size, Image.LANCZOS), os.path.join(out_dir, f"stage-{i}.png"))
        print(f"stage-{i}: {size[0]}x{size[1]}")

    if len(panels) == 5:
        a, b = panels[4]
        al = main_parts(alpha[:, a:b])
        ys, xs = np.where(al > 0.02)
        crop = np.dstack([col[:, a:b], al[..., None] * 255])[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
        im = Image.fromarray(crop.astype(np.uint8), "RGBA")
        size = (max(1, round(im.width * scale)), max(1, round(im.height * scale)))
        write(im.resize(size, Image.LANCZOS), os.path.join(out_dir, "hat.png"))
        print(f"hat: {size[0]}x{size[1]}")
    print("готово:", out_dir)


def main_all(folder):
    done, failed = [], []
    for f in sorted(os.listdir(folder)):
        if not f.lower().endswith(".png"):
            continue
        skin = f[:-4]
        try:
            main(os.path.join(folder, f), skin)
            done.append(skin)
        except SystemExit as e:
            failed.append(f"{skin}: {e}")
    print(f"нарезано: {len(done)}")
    for line in failed:
        print("не вышло —", line)


if __name__ == "__main__":
    if len(sys.argv) == 3 and sys.argv[1] == "--all":
        main_all(sys.argv[2])
    elif len(sys.argv) in (3, 4):
        main(*sys.argv[1:])
    else:
        sys.exit(__doc__)
