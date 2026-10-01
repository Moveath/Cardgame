"""Дорисовывает к портрету каждого скина тело в полный рост для стикеров «Говна».

Туловище и рукава продолжают цвета одежды с нижнего края самого портрета, поэтому шва нет;
ниже — брюки и обувь (цвета взяты с общих рисунков костюмов) или длинное одеяние.
Лицо не меняется: это замена склейки «портрет + общие брюки», пока у скина нет своих
кадров от художника (docs/govno-figures.md).

    python scripts/govno-figures/gen_bodies.py            # все скины
    python scripts/govno-figures/gen_bodies.py marat_05   # выбранные

Пишет Resources/Govno/Stickers/Bodies/<скин>.png (только тело, место портрета прозрачное)
Faces/<скин>-1.png (тревога) и -2.png (испуг) — заплатки на лицо, и bodies.txt:
<скин> <от макушки до земли> <пояс> <колено> <шея от макушки> <x y заплатки> <глаза> <между глазами>
— всё в пикселях портрета (высоты пояса и колена — от земли).
"""
import math
import os
import sys
import uuid

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import face_fx  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
RES = os.path.join(ROOT, "unity", "CardGameClient", "Assets", "Resources")
STICKERS = os.path.join(RES, "Govno", "Stickers")
OUT = os.path.join(STICKERS, "Bodies")
META_TEMPLATE = os.path.join(STICKERS, "mud-pool.png.meta")
SS = 3
LINE = (30, 22, 20)

# Костюмы — тот же каталог, что в GovnoAvatarBody.cs (asset, оттенок одеяния)
ROYAL_RED, BURGUNDY, WIZARD_BLUE = (143, 34, 40), (105, 33, 60), (41, 72, 126)
TEAL, PURPLE, DRESS_BLUE, DRESS_GREEN = (38, 81, 87), (91, 55, 86), (31, 63, 112), (51, 85, 57)
CREAM, KIMONO = (222, 205, 165), (148, 123, 78)
S_, B_, DET, CAP = ("suit", None), ("brown", None), ("brown", None), ("blue", None)
KING, WIZ, APR, COW = ("robe", ROYAL_RED), ("robe", WIZARD_BLUE), ("apron", None), ("brown", None)
PIR, ROM, DOC, WIN = ("pirate", None), ("roman", None), ("coat", None), ("brown", None)
COSTUMES = {
    "yegor": [DET, CAP, KING, WIZ, APR, ("robe", TEAL), S_, COW, B_, PIR, ROM, DOC],
    "danil": [DET, CAP, KING, WIZ, APR, ("robe", BURGUNDY), S_, COW, APR, PIR, ROM, DOC],
    "ali": [DET, CAP, ("robe", ROYAL_RED), WIZ, APR, ("robe", PURPLE), S_, COW, B_, PIR, ROM, DOC],
    "aslan": [DET, CAP, KING, WIZ, APR, ("robe", TEAL), S_, COW, B_, PIR, ROM, DOC],
    "marat": [DET, CAP, ("robe", BURGUNDY), WIZ, ("robe", BURGUNDY), S_, APR, COW, WIN, B_, ROM, DOC],
    "yelnur": [S_, DET, CAP, B_, ("robe", BURGUNDY), ("robe", KIMONO), S_, ("robe", CREAM), WIN, B_, B_, S_],
    "avatar": [DET, ("robe", DRESS_BLUE), CAP, S_, ("robe", BURGUNDY), ("robe", DRESS_GREEN), S_,
               ("robe", BURGUNDY), WIN, S_, B_, ("robe", BURGUNDY)],
}
PORTRAIT_DIRS = {g: os.path.join(RES, "PlayerAvatars", g) for g in ("yegor", "danil", "ali", "aslan", "marat", "yelnur")}
PORTRAIT_DIRS["avatar"] = os.path.join(RES, "Avatars")


def costume(name):
    group, idx = name.rsplit("_", 1)
    return COSTUMES[group][int(idx) - 1]


_module_cache = {}


def module_colors(asset):
    """Цвет штанин и обуви с общего рисунка костюма (фон — пурпурный)."""
    if asset in _module_cache:
        return _module_cache[asset]
    im = np.asarray(Image.open(os.path.join(STICKERS, f"lower-{asset}.png")).convert("RGB")).astype(np.float32)
    mag = np.minimum(im[..., 0], im[..., 2]) - im[..., 1]
    solid = mag < 40
    ys, xs = np.where(solid)
    y0, y1 = ys.min(), ys.max()

    def sample(f0, f1):
        rows = slice(int(y0 + (y1 - y0) * f0), int(y0 + (y1 - y0) * f1))
        px = im[rows][solid[rows]]
        lum = px.mean(1)
        px = px[(lum > 35) & (lum < 235)]
        return tuple(int(v) for v in np.median(px, 0))

    res = (sample(0.62, 0.8), sample(0.9, 0.97))
    _module_cache[asset] = res
    return res


def shade(color, k):
    return tuple(int(max(0, min(255, c * k))) for c in color)


def skin_color(rgb, a, W, H):
    reg = rgb[int(H * 0.25):int(H * 0.6), int(W * 0.3):int(W * 0.7)].reshape(-1, 3)
    al = a[int(H * 0.25):int(H * 0.6), int(W * 0.3):int(W * 0.7)].reshape(-1)
    r, g, b = reg[:, 0], reg[:, 1], reg[:, 2]
    m = (al > 0.9) & (r > 140) & (r > g) & (g > b) & (r - b > 30) & (g > 90)
    return tuple(int(v) for v in np.median(reg[m], 0)) if m.sum() > 20 else (225, 180, 145)


def kmeans(px, k, iters=12, seed=1):
    rnd = np.random.default_rng(seed)
    k = min(k, len(px))
    centers = px[rnd.choice(len(px), k, replace=False)].copy()
    for _ in range(iters):
        lab = np.argmin(((px[:, None] - centers[None]) ** 2).sum(-1), 1)
        for j in range(k):
            if (lab == j).any():
                centers[j] = px[lab == j].mean(0)
    return centers, np.bincount(lab, minlength=k) / len(px)


def garment_only(cols, L, R, skin, rgb, a, yb):
    """Оставляет по нижнему краю только одежду. Образец одежды — грудь и плечи выше среза,
    где обычно ничего не держат; кожа рук и предметы (колба, миска, книга) у края заменяются
    ближайшей одеждой по соседству, иначе тянулись бы вниз полосой."""
    H = rgb.shape[0]
    y0, y1 = int(yb - H * 0.36), int(yb - H * 0.16)
    reg, al = rgb[y0:y1, L:R].reshape(-1, 3), a[y0:y1, L:R].reshape(-1)
    far_skin = np.abs(reg - np.array(skin, np.float32)).sum(-1) > 110
    lum = reg.mean(1)
    px = reg[(al > 0.9) & far_skin & (lum > 22)]
    if len(px) < 200:
        return cols
    centers, share = kmeans(px[:: max(1, len(px) // 4000)], 7)
    palette = centers[share > 0.05]
    row = cols[L:R].astype(np.float32)
    dist = np.sqrt(((row[:, None] - palette[None]) ** 2).sum(-1)).min(1)
    skin_like = np.abs(row - np.array(skin, np.float32)).sum(-1) < 100
    valid = (dist < 70) & ~skin_like
    if valid.sum() < len(row) * 0.3:
        return cols
    out = cols.copy()
    idx = np.where(valid)[0]
    for i in np.where(~valid)[0]:
        j = idx[np.argmin(np.abs(idx - i))]
        out[L + i] = row[j]
    return out


def build(name, path):
    portrait = Image.open(path).convert("RGBA")
    W, H = portrait.size
    arr = np.asarray(portrait).astype(np.float32)
    rgb, a = arr[..., :3], arr[..., 3] / 255

    cov = (a > 0.5).mean(1)
    yb = int(np.where(cov >= cov.max() * 0.5)[0].max())       # нижний срез портрета
    row = np.where(a[yb - 1] > 0.5)[0]
    L, R = int(row.min()), int(row.max()) + 1
    S = R - L
    cx = (L + R) / 2

    # цвета одежды на срезе: медиана по нескольким строкам и сглаживание по горизонтали
    band = np.median(rgb[yb - 6:yb - 1], 0)
    cols = np.stack([np.median(band[max(0, x - 3):x + 4], 0) for x in range(W)])
    skin = skin_color(rgb, a, W, H)
    # рукава по краям не трогаем: предметы и кисти почти всегда перед туловищем
    cols = garment_only(cols, int(L + 0.19 * (R - L)), int(R - 0.19 * (R - L)), skin, rgb, a, yb)
    # крупные массы одежды без мелочей: пуговицы, галстук, вырез рубашки к поясу сходят на нет
    wide = max(3, int((R - L) * 0.06))
    solid_cols = np.stack([np.median(cols[max(L, x - wide):min(R, x + wide + 1)], 0) if L <= x < R else cols[x]
                           for x in range(W)])
    lower_rows = slice(int(H * 0.62), yb)
    sk = rgb[lower_rows]
    skm = (a[lower_rows] > 0.9) & (np.abs(sk - np.array(skin)).sum(-1) < 60)
    skm[:, int(cx - W * 0.12):int(cx + W * 0.12)] = False
    hands_visible = skm.sum() > W * (yb - H * 0.62) * 0.012

    asset, tint = costume(name)
    pants, shoes = module_colors(asset)
    robe = asset == "robe"
    if robe or max(shoes) - min(shoes) < 12 and sum(shoes) > 240:
        shoes = (48, 36, 30)   # у одеяния на общем рисунке обувь серая под перекраску

    aw = 0.19 * S
    hb = 0.42 * S                  # от среза до пояса
    hl = 0.6 * S                   # от пояса до земли
    shoe_h = 0.09 * S
    pad = int(math.ceil(0.14 * S))
    total = yb + hb + hl
    CW, CH = W + 2 * pad, int(math.ceil(total)) + 2

    s = SS
    ox = pad                       # сдвиг холста относительно портрета

    def P(x, y):
        return ((x + ox) * s, y * s)

    top = yb - 7
    hip = yb + hb
    ground = total
    arm_len = hb * (0.46 if hands_visible else 0.86)

    # маски частей
    def poly_mask(pts):
        m = Image.new("L", (CW * s, CH * s), 0)
        ImageDraw.Draw(m).polygon([P(x, y) for x, y in pts], fill=255)
        return np.asarray(m) > 127

    def ell_mask(cx_, cy_, rx, ry):
        m = Image.new("L", (CW * s, CH * s), 0)
        ImageDraw.Draw(m).ellipse((P(cx_ - rx, cy_ - ry)[0], P(cx_ - rx, cy_ - ry)[1], P(cx_ + rx, cy_ + ry)[0], P(cx_ + rx, cy_ + ry)[1]), fill=255)
        return np.asarray(m) > 127

    inset = 0.05 * S
    torso = poly_mask([(L + aw * 0.9, top), (R - aw * 0.9, top), (R - aw * 0.9 - inset, hip), (L + aw * 0.9 + inset, hip)])
    # рукава сужаются книзу и чуть прижимаются к телу; кисть — меньше рукава
    taper = 0.075 * S
    lcx = L + (taper + aw) / 2              # середина конца левого рукава
    rcx = R - (taper + aw) / 2
    cuff_r = (aw - taper) / 2
    arm_l = poly_mask([(L, top), (L + aw, top), (L + aw - 0.01 * S, top + arm_len), (L + taper, top + arm_len)]) |         ell_mask(lcx, top + arm_len, cuff_r, cuff_r * 0.55)
    arm_r = poly_mask([(R - aw, top), (R, top), (R - taper, top + arm_len), (R - aw + 0.01 * S, top + arm_len)]) |         ell_mask(rcx, top + arm_len, cuff_r, cuff_r * 0.55)
    hands = np.zeros_like(torso)
    if not hands_visible:
        hy = top + arm_len + cuff_r * 0.9
        hands = ell_mask(lcx, hy, cuff_r * 0.8, cuff_r * 1.0) | ell_mask(rcx, hy, cuff_r * 0.8, cuff_r * 1.0)
    tl, tr = L + aw * 0.9 + inset, R - aw * 0.9 - inset
    if robe:
        flare = 0.08 * S
        legs = poly_mask([(tl - 0.01 * S, hip - 2), (tr + 0.01 * S, hip - 2), (tr + flare, ground - shoe_h * 0.55), (tl - flare, ground - shoe_h * 0.55)])
        leg_l = leg_r = None
    else:
        crotch = hip + 0.2 * hl
        ankle = ground - shoe_h * 0.7
        gap = 0.03 * S
        leg_l = poly_mask([(tl, hip - 2), (cx, hip - 2), (cx, crotch), (cx - gap, ankle), (tl + 0.03 * S, ankle)])
        leg_r = poly_mask([(cx, hip - 2), (tr, hip - 2), (tr - 0.03 * S, ankle), (cx + gap, ankle), (cx, crotch)])
        legs = leg_l | leg_r
    sw = 0.15 * S
    shoe_l = ell_mask(cx - 0.12 * S - 0.02 * S, ground - shoe_h * 0.5, sw, shoe_h * 0.55)
    shoe_r = ell_mask(cx + 0.12 * S + 0.02 * S, ground - shoe_h * 0.5, sw, shoe_h * 0.55)
    shoes_m = shoe_l | shoe_r

    yy, xx = np.mgrid[0:CH * s, 0:CW * s].astype(np.float32)
    px = xx / s - ox                # x в пикселях портрета
    py = yy / s
    img = np.zeros((CH * s, CW * s, 3), np.float32)
    fill = np.zeros((CH * s, CW * s), bool)

    def cloth(mask, k_shadow):
        xi = np.clip(px[mask].astype(int), L, R - 1)
        t = np.clip((py[mask] - yb) / max(1, hb), 0, 1)
        blend = np.clip(t / 0.55, 0, 1)
        blend = (blend * blend * (3 - 2 * blend))[:, None]
        c = cols[xi] * (1 - blend) + solid_cols[xi] * blend
        u = np.clip((px[mask] - L) / S, 0, 1)
        k = (1 - 0.2 * t) * (1 - k_shadow * (2 * u - 1) ** 2)
        img[mask] = c * k[:, None]

    cloth(torso, 0.18)
    cloth(arm_l & ~torso, 0.05)
    cloth(arm_r & ~torso, 0.05)
    img[arm_l & ~torso] *= 0.94
    img[arm_r & ~torso] *= 0.94
    fill |= torso | arm_l | arm_r

    if robe:
        base = np.array(tint, np.float32)
        u = np.clip((px - cx) / (0.5 * S), -1, 1)
        k = 1 - 0.22 * u ** 2
        folds = 1 - 0.1 * (np.cos((px - cx) / (0.11 * S) * math.pi) > 0.85)
        m = legs & ~fill
        img[m] = base * (k * folds)[m][:, None]
    else:
        base = np.array(pants, np.float32)
        for leg, lx in ((leg_l, cx - 0.14 * S), (leg_r, cx + 0.14 * S)):
            m = leg & ~fill
            u = np.clip((px[m] - lx) / (0.14 * S), -1, 1)
            img[m] = base * (1 - 0.18 * u ** 2)[:, None]
    fill |= legs

    m = shoes_m & ~(legs & (py < ground - shoe_h * 0.95))
    img[m] = np.array(shoes, np.float32)
    fill |= shoes_m
    if not hands_visible:
        img[hands] = np.array(skin, np.float32) * 0.96
        fill |= hands

    # контур: расширение силуэта; внутренние линии — рукава, пояс, штанины, обувь
    sil = Image.fromarray((fill * 255).astype(np.uint8))
    outline = np.asarray(sil.filter(ImageFilter.MaxFilter(2 * int(1.6 * s) + 1))) > 127
    out_rgb = np.where(outline[..., None] & ~fill[..., None], np.array(LINE, np.float32), img)
    lines = Image.new("L", (CW * s, CH * s), 0)
    d = ImageDraw.Draw(lines)
    w = int(1.5 * s)
    d.line([P(L + aw, top + 4), P(L + aw - 0.01 * S, top + arm_len)], fill=255, width=w)
    d.line([P(R - aw, top + 4), P(R - aw + 0.01 * S, top + arm_len)], fill=255, width=w)
    # манжеты
    for c in (lcx, rcx):
        d.line([P(c - cuff_r, top + arm_len - cuff_r * 0.5), P(c + cuff_r, top + arm_len - cuff_r * 0.5)], fill=255, width=w)
    d.line([P(tl, hip), P(tr, hip)], fill=255, width=int(2 * s))
    if not robe:
        d.line([P(cx, hip + 0.04 * S), P(cx, crotch)], fill=255, width=w)
    else:
        d.line([P(tl - 0.1 * S, ground - shoe_h * 0.55), P(tr + 0.1 * S, ground - shoe_h * 0.55)], fill=255, width=w)
    for sm in (shoe_l, shoe_r):
        e = np.asarray(Image.fromarray((sm * 255).astype(np.uint8)).filter(ImageFilter.FIND_EDGES)) > 60
        e = np.asarray(Image.fromarray((e * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(3))) > 127
        out_rgb[e & fill] = np.array(LINE, np.float32)
    if not hands_visible:
        e = np.asarray(Image.fromarray((hands * 255).astype(np.uint8)).filter(ImageFilter.FIND_EDGES)) > 60
        e = np.asarray(Image.fromarray((e * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(3))) > 127
        out_rgb[e] = np.array(LINE, np.float32)
    ln = np.asarray(lines) > 127
    out_rgb[ln & fill] = np.array(LINE, np.float32)
    alpha = (fill | outline).astype(np.float32)
    # место портрета выше среза — прозрачное: портрет рисуется поверх
    alpha[:int(top * s)] = 0
    body = Image.fromarray(np.dstack([out_rgb, alpha * 255]).astype(np.uint8), "RGBA")
    body = body.resize((CW, CH), Image.LANCZOS)
    knee = hl * 0.5
    return body, dict(total=total, belt=hl, knee=knee, pad=pad, portrait=portrait)


def write(img, path):
    img.save(path)
    meta = path + ".meta"
    if not os.path.exists(meta):
        text = open(META_TEMPLATE, encoding="utf-8").read()
        lines = [("guid: " + uuid.uuid4().hex) if l.startswith("guid:") else l for l in text.split("\n")]
        open(meta, "w", encoding="utf-8", newline="\n").write("\n".join(lines))


def all_skins():
    for group, folder in PORTRAIT_DIRS.items():
        for i in range(1, 13):
            name = f"{group}_{i:02d}"
            path = os.path.join(folder, name + ".png")
            if os.path.exists(path):
                yield name, path


def main(names, out_dir=OUT, preview=None):
    os.makedirs(out_dir, exist_ok=True)
    table_path = os.path.join(out_dir, "bodies.txt")
    table = {}
    if os.path.exists(table_path):
        for line in open(table_path, encoding="utf-8"):
            parts = line.split()
            if len(parts) == 9:
                table[parts[0]] = line.strip()
    shots = []
    for name, path in all_skins():
        if names and name not in names:
            continue
        body, info = build(name, path)
        write(body, os.path.join(out_dir, name + ".png"))
        portrait = info["portrait"]
        face = face_fx.analyse(portrait, name)
        if face is not None:
            faces_dir = os.path.join(os.path.dirname(out_dir), "Faces")
            os.makedirs(faces_dir, exist_ok=True)
            for level in (1, 2):
                patch, fx, fy = face_fx.make_patch(portrait, face, level)
                write(patch, os.path.join(faces_dir, f"{name}-{level}.png"))
                if preview is not None and level == 2:
                    info["scared"] = (patch, fx, fy)
            neck = face["my"] + face["d"] * 0.55
            table[name] = (f"{name} {info['total']:.1f} {info['belt']:.1f} {info['knee']:.1f} {neck:.1f} "
                           f"{fx} {fy} {face['ey']:.1f} {face['d']:.1f}")
        else:
            print("лицо не найдено:", name)
            table[name] = f"{name} {info['total']:.1f} {info['belt']:.1f} {info['knee']:.1f} {portrait.height * 0.62:.1f} -1 -1 {portrait.height * 0.4:.1f} {portrait.width * 0.17:.1f}"
        if preview is not None:
            full = body.copy()
            full.alpha_composite(info["portrait"], (info["pad"], 0))
            if "scared" in info:
                patch, fx, fy = info["scared"]
                full.alpha_composite(patch, (info["pad"] + fx, fy))
            shots.append((name, full))
    with open(table_path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(table[k] for k in sorted(table)) + "\n")
    write_meta_txt = table_path + ".meta"
    if not os.path.exists(write_meta_txt):
        open(write_meta_txt, "w", encoding="utf-8", newline="\n").write(
            "fileFormatVersion: 2\nguid: " + uuid.uuid4().hex + "\nTextScriptImporter:\n  externalObjects: {}\n"
            "  userData: \n  assetBundleName: \n  assetBundleVariant: \n")
    if preview is not None and shots:
        cell_h = 300
        per_row = 12
        rows = (len(shots) + per_row - 1) // per_row
        sheet = Image.new("RGBA", (per_row * 170, rows * (cell_h + 10)), (29, 58, 44, 255))
        for k, (name, im) in enumerate(shots):
            im = im.copy()
            im.thumbnail((160, cell_h))
            x = (k % per_row) * 170 + (170 - im.width) // 2
            y = (k // per_row) * (cell_h + 10) + cell_h - im.height
            sheet.alpha_composite(im, (x, y))
        sheet.convert("RGB").save(preview)
    print("готово:", len(shots) if preview else len(table))


if __name__ == "__main__":
    args = sys.argv[1:]
    out, prev = OUT, None
    if "--out" in args:
        i = args.index("--out"); out = args[i + 1]; del args[i:i + 2]
    if "--preview" in args:
        i = args.index("--preview"); prev = args[i + 1]; del args[i:i + 2]
    main(set(args), out, prev)
