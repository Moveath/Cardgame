"""Базовые персонажи из листов «в полный рост на пурпурном фоне» (screens/references/characters).

Из каждой фигурки получается:
  * портрет по грудь для обычных игр — Resources/PlayerAvatars/<имя>/<имя>_01.png,
    в том же кадре, что и прежние портреты (глаза на ~0,28 высоты сверху);
  * кадры для «Говна» — Resources/Govno/Stickers/Figures/<имя>_01/stage-0..3.png:
    весь рост, по колено, по пояс, по шею; на стадиях 1–3 поверх лица — тревога и испуг
    (face_fx.py), всё в одном масштабе и выровнено по макушке. Верх фигуры плавно
    расширен (bobble): голова ×HEAD_SCALE, плечи ×CHEST_SCALE, от пояса — как было.

    python scripts/govno-figures/make_characters.py
    python scripts/govno-figures/make_characters.py --figures-only  # не перезаписывать портреты
"""
import os
import sys
import uuid

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import face_fx  # noqa: E402
from slice_figures import key_magenta, main_parts, spans  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
REF = os.path.join(ROOT, "screens", "references", "characters")
RES = os.path.join(ROOT, "unity", "CardGameClient", "Assets", "Resources")
STICKERS = os.path.join(RES, "Govno", "Stickers")
FIGURE_META = os.path.join(STICKERS, "mud-pool.png.meta")
# Лист, имена по порядку (слева направо, сверху вниз), папка портрета: люди — своими
# наборами в PlayerAvatars, общие персонажи — на местах прежних avatar_01…12.
SHEETS = [
    ("marat-yegor-yelnur.png", ["marat", "yegor", "yelnur"], "person"),
    ("ali-aslan-danil.png", ["ali", "aslan", "danil"], "person"),
    ("common-12.png", [f"avatar_{i:02d}" for i in range(1, 13)], "common"),
]
FRAME_HEIGHT = 480          # весь рост в кадре «Говна»
HEAD_SCALE = 1.6            # голова крупнее, чтобы за столом были видны лицо и эмоции (фигура ростом 290)
CHEST_SCALE = 1.35          # плечи и грудь тоже шире — к поясу плавно до 1: голова не сидит отдельно
LEGS_WIDTH = 1.2            # ноги только шире (не длиннее) — вровень с широким верхом
KNEE, WAIST = 0.27, 0.5     # доли роста от ступней
PORTRAIT_MAX_WIDTH = 1.02   # ширина портрета к высоте, не больше
PORTRAIT_MIN_HEIGHT = 320   # SeatFigure.ReferencePortraitHeight · 1,05 с запасом: все портреты за столом одного роста


def write(img, path, meta_template):
    img.save(path)
    meta = path + ".meta"
    if not os.path.exists(meta) and meta_template:
        text = open(meta_template, encoding="utf-8").read()
        lines = [("guid: " + uuid.uuid4().hex) if l.startswith("guid:") else l for l in text.split("\n")]
        open(meta, "w", encoding="utf-8", newline="\n").write("\n".join(lines))


def bands(mask, axis, min_gap=12, min_pixels=400):
    """Полосы, где есть фигура, вдоль оси (0 — строки, 1 — столбцы)."""
    return spans(mask if axis == 1 else mask.T, min_gap, min_pixels)


def figures(sheet_path):
    """Каждая фигура — отдельное пятно: ряды на листе могут стоять почти вплотную.
    Порядок — по рядам сверху вниз, в ряду слева направо."""
    from scipy import ndimage
    rgb = np.asarray(Image.open(sheet_path).convert("RGB")).astype(np.float32)
    col, alpha = key_magenta(rgb)
    lab, n = ndimage.label(alpha > 0.5)
    sizes = ndimage.sum(np.ones_like(alpha), lab, range(1, n + 1))
    blobs = []
    for i, sl in enumerate(ndimage.find_objects(lab), 1):
        if sizes[i - 1] < sizes.max() * 0.2:
            continue
        blobs.append((i, sl))
    heights = np.median([sl[0].stop - sl[0].start for _, sl in blobs])
    blobs.sort(key=lambda b: (round(((b[1][0].start + b[1][0].stop) / 2) / heights), b[1][1].start))
    out = []
    for i, sl in blobs:
        # само пятно плюс мелкие отдельные кусочки рядом с ним (серьги, пряди)
        near = np.zeros_like(alpha, dtype=bool)
        near[max(0, sl[0].start - 6):sl[0].stop + 6, max(0, sl[1].start - 6):sl[1].stop + 6] = True
        other = (lab > 0) & (lab != i) & np.isin(lab, [b[0] for b in blobs])
        al = alpha * near * ~ndimage.binary_dilation(other, iterations=2)
        ys, xs = np.where(al > 0.02)
        top, feet = ys.min(), ys.max() + 1
        h = feet - top
        chest = al[int(top + h * 0.25):int(top + h * 0.3)] > 0.5
        cx = np.median(np.where(chest)[1])
        half = int(np.ceil(max(cx - xs.min(), xs.max() + 1 - cx))) + 4
        canvas = np.zeros((h, 2 * half, 4), np.float32)
        x0 = int(round(cx)) - half
        lo, hi = max(0, x0), min(al.shape[1], x0 + 2 * half)
        canvas[:, lo - x0:hi - x0, :3] = col[top:feet, lo:hi]
        canvas[:, lo - x0:hi - x0, 3] = al[top:feet, lo:hi] * 255
        out.append(Image.fromarray(canvas.astype(np.uint8), "RGBA"))
    return out


def find_collar(figure, cx, chin):
    """Ворот — первая строка под подбородком, где посередине уже свитер."""
    arr = np.asarray(figure).astype(np.float32)
    rgb, a = arr[..., :3], arr[..., 3] / 255
    H, W = a.shape
    torso = rgb[int(H * 0.3):int(H * 0.4), int(W * 0.4):int(W * 0.6)].reshape(-1, 3)
    sweater = np.median(torso[a[int(H * 0.3):int(H * 0.4), int(W * 0.4):int(W * 0.6)].reshape(-1) > 0.9], 0)
    near_sweater = np.abs(rgb - sweater).sum(-1) < 70
    collar = int(chin)
    c0, c1 = int(cx - W * 0.03), int(cx + W * 0.03) + 1
    while collar < H * 0.4 and not (near_sweater[collar, c0:c1].mean() > 0.6 and a[collar, c0:c1].mean() > 0.9):
        collar += 1
    return collar


def bobble(figure, cx, chin, collar, s_head=HEAD_SCALE, s_chest=CHEST_SCALE, s_legs=LEGS_WIDTH):
    """Плавно увеличивает верх фигуры: голова — в s_head раз, от подбородка к плечам масштаб
    сходит к s_chest, от плеч к поясу — к 1 по высоте и к s_legs по ширине; ноги той же длины,
    только шире. Голова растянута одинаково в обе стороны и не искажается, а шва между
    головой и телом нет вовсе. Прежняя «голова отдельно» давала кривой стык у шеи.
    Возвращает картинку и функцию: строка исходной фигуры -> строка результата."""
    from scipy import ndimage
    arr = np.asarray(figure).astype(np.float32) / 255
    a = arr[..., 3:4]
    pre = np.concatenate([arr[..., :3] * a, a], -1)
    H, W = a.shape[:2]
    shoulder = collar + 0.05 * H
    waist = H * (1 - WAIST)

    def scale_at(y, low):
        t1 = np.clip((y - chin) / max(1.0, shoulder - chin), 0, 1)
        t2 = np.clip((y - shoulder) / max(1.0, waist - shoulder), 0, 1)
        t1, t2 = t1 * t1 * (3 - 2 * t1), t2 * t2 * (3 - 2 * t2)
        return np.where(y <= shoulder, s_head + (s_chest - s_head) * t1, s_chest + (low - s_chest) * t2)

    edges = np.arange(H + 1, dtype=np.float64)
    out_edges = np.concatenate([[0], np.cumsum(scale_at(np.arange(H) + 0.5, 1.0))])
    out_h = int(np.ceil(out_edges[-1]))
    out_w = int(np.ceil(W * max(s_head, s_chest, s_legs))) + 4
    src_y = np.interp(np.arange(out_h) + 0.5, out_edges, edges)
    src_x = cx + (np.arange(out_w)[None, :] + 0.5 - out_w / 2) / scale_at(src_y, s_legs)[:, None]
    coords = [np.repeat(src_y[:, None] - 0.5, out_w, 1), src_x - 0.5]
    out = np.stack([ndimage.map_coordinates(pre[..., c], coords, order=3, mode="constant", cval=0)
                    for c in range(4)], -1)
    out = np.clip(out, 0, 1)
    alpha = out[..., 3:4]
    rgb = np.where(alpha > 1e-4, out[..., :3] / np.maximum(alpha, 1e-4), 0)
    img = Image.fromarray((np.concatenate([np.clip(rgb, 0, 1), alpha], -1) * 255 + 0.5).astype(np.uint8), "RGBA")
    bbox = img.getbbox()
    side = min(bbox[0], out_w - bbox[2])
    img = img.crop((side, 0, out_w - side, out_h))
    return img, (lambda y: float(np.interp(y, edges, out_edges)))


def build(name, figure, kind, figures_only=False):
    W, H = figure.size
    # лицо ищем на верхней трети — там только голова и плечи
    bust_h = int(H * 0.32)
    bust = figure.crop((0, 0, W, bust_h))
    face = face_fx.analyse(bust, "char:" + name)
    if face is None:
        raise SystemExit(f"{name}: не нашёл лицо")
    chin = face["my"] + face["d"] * 0.55
    head_cx = face["mid"]

    # портрет в кадре прежних: от макушки до подбородка — 48% высоты, ширина 0,86 высоты
    a = np.asarray(figure)[..., 3]

    def need_width(ph):
        xs = np.where((a[:int(ph * 0.98)] > 20).any(0))[0]
        return 2 * max(head_cx - xs.min(), xs.max() + 1 - head_cx) + 4

    # ширина — чтобы плечи и руки не обрезались по бокам ровной линией; но не шире 1,02 высоты:
    # места за столами рассчитаны на портрет не шире рамки в 1,25 раза. Кто шире (толстяк) —
    # камера отъезжает: голова чуть мельче, зато плечи видны целиком
    ph = (chin - 0) / 0.48
    for _ in range(40):
        if need_width(ph) <= ph * PORTRAIT_MAX_WIDTH:
            break
        ph *= 1.02
    top = -ph * 0.02
    pw = max(ph * 0.86, need_width(ph))
    box = (int(round(head_cx - pw / 2)), int(round(top)), int(round(head_cx + pw / 2)), int(round(top + ph)))
    portrait = Image.new("RGBA", (box[2] - box[0], box[3] - box[1]), (0, 0, 0, 0))
    portrait.alpha_composite(figure.crop((max(0, box[0]), max(0, box[1]), min(W, box[2]), min(H, box[3]))),
                             (max(0, -box[0]), max(0, -box[1])))
    if portrait.height < PORTRAIT_MIN_HEIGHT:
        # общие персонажи на листе мельче друзей; SeatFigure рисует портрет от его высоты
        # в пикселях, и ниже ~305 пикселей голова за столом выходит мельче
        k = PORTRAIT_MIN_HEIGHT / portrait.height
        portrait = portrait.resize((round(portrait.width * k), PORTRAIT_MIN_HEIGHT), Image.LANCZOS)
    folder = os.path.join(RES, "PlayerAvatars", name) if kind == "person" else os.path.join(RES, "Avatars")
    os.makedirs(folder, exist_ok=True)
    # чистый шаблон одиночного спрайта; мипмапы и сжатие потом выставит CardArtImporter
    portrait_meta = FIGURE_META
    skin = name + "_01" if kind == "person" else name
    if not figures_only:
        write(portrait, os.path.join(folder, skin + ".png"), portrait_meta)

    # кадры «Говна»: крупный верх, лица тревоги и испуга, срезы по колено, пояс, шею
    out_dir = os.path.join(STICKERS, "Figures", skin)
    os.makedirs(out_dir, exist_ok=True)
    collar = find_collar(figure, head_cx, chin)
    frames = []
    for stage in range(4):
        img = figure.copy()
        if stage > 0:
            patch, fx, fy = face_fx.make_patch(bust, face, stage)
            img.alpha_composite(patch, (fx, fy))
        big, row = bobble(img, head_cx, chin, collar)
        frames.append(big)
    total = frames[0].height
    scale = FRAME_HEIGHT / total
    # срезы — по исходной фигуре (колени и пояс от ступней, шея по вороту), пересчитанные в кадр
    cuts = [total, row(H * (1 - KNEE)), row(H * (1 - WAIST)), row(collar + H * 0.01)]
    for stage, (img, cut) in enumerate(zip(frames, cuts)):
        img = img.crop((0, 0, img.width, int(round(cut))))
        size = (max(1, round(img.width * scale)), max(1, round(img.height * scale)))
        write(img.resize(size, Image.LANCZOS), os.path.join(out_dir, f"stage-{stage}.png"), FIGURE_META)
    figure = frames[0]
    return portrait, figure


if __name__ == "__main__":
    figures_only = "--figures-only" in sys.argv
    args = [a for a in sys.argv[1:] if a != "--figures-only"]
    preview = []
    only = set(args[1:])
    for sheet, names, kind in SHEETS:
        if not os.path.exists(os.path.join(REF, sheet)):
            continue
        figs = figures(os.path.join(REF, sheet))
        if len(figs) != len(names):
            raise SystemExit(f"{sheet}: ожидалось {len(names)} фигур, найдено {len(figs)}")
        for name, fig in zip(names, figs):
            if only and name not in only:
                continue
            portrait, figure = build(name, fig, kind, figures_only=figures_only)
            print(name, "фигура", figure.size, "портрет", portrait.size)
            preview.append((portrait, figure))
    if args:
        cell = Image.new("RGBA", (max(6, len(preview)) * 260, 700), (29, 58, 44, 255))
        for i, (p, f) in enumerate(preview):
            q = p.copy(); q.thumbnail((240, 260)); cell.alpha_composite(q, (i * 260 + 10, 10))
            g = f.copy(); g.thumbnail((240, 420)); cell.alpha_composite(g, (i * 260 + 10, 280))
        cell.convert("RGB").save(args[0])
