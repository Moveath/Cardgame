"""Мимика для рисованных портретов: тревога, испуг и паника при погружении.
Находим черты лица, плавно поднимаем исходные брови, меняем положение уголков губ;
при испуге приоткрываем рот. Цвет кожи восстанавливается из соседних участков.
Результат — небольшая прозрачная заплатка с изменёнными участками лица.

Используется из gen_bodies.py; отдельно — для проверки:
    python scripts/govno-figures/face_fx.py превью.png [скины…]
"""
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from scipy import ndimage

SS = 4
LINE = (38, 20, 16)


def lum(rgb):
    return rgb[..., 0] * 0.3 + rgb[..., 1] * 0.59 + rgb[..., 2] * 0.11


def skin_mask(rgb, a):
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    return (a > 0.9) & (r > 120) & (r > g) & (g > b * 0.9) & (r - b > 25) & (g > 70) & (r - g < 90)


def white_mask(rgb, a):
    mx, mn = rgb.max(-1), rgb.min(-1)
    return (a > 0.9) & (mn > 185) & (mx - mn < 45)


def find_eyes(rgb, a):
    """Два белка рядом с кожей, примерно на одной высоте, симметрично от середины лица."""
    H, W = a.shape
    skin = skin_mask(rgb, a)
    wm = white_mask(rgb, a)
    wm[:int(H * 0.12)] = False
    wm[int(H * 0.72):] = False
    lab, n = ndimage.label(wm)
    blobs = []
    near_skin = ndimage.binary_dilation(skin, iterations=3)
    for i, sl in enumerate(ndimage.find_objects(lab), 1):
        ys, xs = sl
        h, w = ys.stop - ys.start, xs.stop - xs.start
        area = (lab[sl] == i).sum()
        if area < 6 or w > W * 0.12 or h > H * 0.08 or w < 3:
            continue
        ring = ndimage.binary_dilation(lab == i, iterations=2) & ~(lab == i)
        skin_share = (near_skin & ring).sum() / max(1, ring.sum())
        if skin_share < 0.25:
            continue
        cy, cx = ndimage.center_of_mass(lab == i)
        blobs.append((cx, cy, w, h, area))
    best, score = None, -1
    for i in range(len(blobs)):
        for j in range(len(blobs)):
            l, r = blobs[i], blobs[j]
            if l[0] >= r[0]:
                continue
            d = r[0] - l[0]
            if d < W * 0.1 or d > W * 0.4 or abs(l[1] - r[1]) > d * 0.2:
                continue
            mid = (l[0] + r[0]) / 2
            # между глазами — кожа переносицы
            y = int(round((l[1] + r[1]) / 2))
            bridge = skin[y, int(mid) - 1:int(mid) + 2].mean() if 0 < y < H else 0
            s = (l[4] + r[4]) * (1 - abs(l[4] - r[4]) / max(l[4], r[4])) * (0.3 + bridge) * (1 - abs(mid - W / 2) / W)
            if s > score:
                best, score = (l, r), s
    return best


def sample_skin(rgb, a, cx, cy, rx, ry):
    H, W = a.shape
    y0, y1 = max(0, int(cy - ry)), min(H, int(cy + ry))
    x0, x1 = max(0, int(cx - rx)), min(W, int(cx + rx))
    reg, al = rgb[y0:y1, x0:x1], a[y0:y1, x0:x1]
    m = skin_mask(reg, al)
    if m.sum() < 10:
        return None
    return np.median(reg[m], 0)


def face_region(rgb, a):
    """Самое крупное пятно кожи в верхних четырёх пятых портрета — лицо."""
    H, W = a.shape
    skin = skin_mask(rgb, a)
    skin[int(H * 0.85):] = False
    skin = ndimage.binary_opening(skin, iterations=1)
    lab, n = ndimage.label(skin)
    if n == 0:
        return None, None
    sizes = ndimage.sum(np.ones_like(a), lab, range(1, n + 1))
    # лицо — крупное пятно ближе к середине и к верху (кисть с лупой бывает не меньше)
    ys, xs = np.mgrid[0:H, 0:W]
    best, score = None, -1
    for i in range(1, n + 1):
        if sizes[i - 1] < sizes.max() * 0.25:
            continue
        cy, cx = ndimage.center_of_mass(lab == i)
        sc = sizes[i - 1] * (1 - abs(cx - W / 2) / W) * (1 - 0.6 * cy / H)
        if sc > score:
            best, score = i, sc
    face = lab == best
    return face, skin


def hull_mask(mask):
    from scipy.spatial import ConvexHull
    ys, xs = np.where(mask)
    pts = np.stack([xs, ys], 1)
    hull = ConvexHull(pts)
    m = Image.new("L", (mask.shape[1], mask.shape[0]), 0)
    ImageDraw.Draw(m).polygon([tuple(pts[v]) for v in hull.vertices], fill=255)
    return np.asarray(m) > 127


def find_features(rgb, a):
    H, W = a.shape
    face, skin = face_region(rgb, a)
    if face is None or face.sum() < 200:
        return None
    ys, xs = np.where(face)
    ft, fb, fl, fr = ys.min(), ys.max(), xs.min(), xs.max()
    fw, fh = fr - fl, fb - ft
    fx = (fl + fr) / 2
    hull = hull_mask(face)
    interior = ndimage.binary_erosion(hull, iterations=2)
    feat = interior & ~face & (a > 0.5)
    lab, n = ndimage.label(feat)
    white = white_mask(rgb, a) | ((rgb.min(-1) > 150) & (rgb.max(-1) - rgb.min(-1) < 60))
    edge = ~ndimage.binary_erosion(hull, iterations=3)
    comps = []
    for i, sl in enumerate(ndimage.find_objects(lab), 1):
        m = lab == i
        area = m.sum()
        if area < 10:
            continue
        cy, cx = ndimage.center_of_mass(m)
        comps.append(dict(i=i, x=cx, y=cy, area=area, w=sl[1].stop - sl[1].start, h=sl[0].stop - sl[0].start,
                          white=(white & m).sum() / area, touches=(m & edge).any(), sl=sl))
    band = lambda c: ft + fh * 0.15 < c["y"] < ft + fh * 0.68
    eyes, score = None, -1
    cand = [c for c in comps if band(c)]
    for l in cand:
        for r in cand:
            if l["x"] >= r["x"]:
                continue
            d = r["x"] - l["x"]
            if not (fw * 0.2 < d < fw * 0.62) or abs(l["y"] - r["y"]) > d * 0.2:
                continue
            if max(l["w"], r["w"]) > d * 0.95:
                continue
            mid = (l["x"] + r["x"]) / 2
            if abs(mid - fx) > fw * 0.16:
                continue
            ratio = min(l["area"], r["area"]) / max(l["area"], r["area"])
            if ratio < 0.3:
                continue
            sc = (l["area"] + r["area"]) * ratio * (0.15 + l["white"] + r["white"]) * (1 - abs(mid - fx) / fw)
            sc *= 0.5 if (l["touches"] or r["touches"]) else 1
            if sc > score:
                eyes, score = (l, r), sc
    if eyes is None:
        # очки или слитые глаза: одно широкое пятно поперёк лица — делим пополам по белкам
        wide = [c for c in cand if c["w"] > fw * 0.45 and abs(c["x"] - fx) < fw * 0.15]
        if not wide:
            return None
        c = max(wide, key=lambda c: c["area"])
        m = lab == c["i"]
        pts = []
        for side in (-1, 1):
            half = m.copy()
            if side < 0:
                half[:, int(c["x"]):] = False
            else:
                half[:, :int(c["x"])] = False
            wh = half & white
            src = wh if wh.sum() > 6 else half
            cy, cx = ndimage.center_of_mass(src)
            pts.append(dict(i=-1, x=cx, y=cy, area=src.sum(), w=c["w"] // 2, h=c["h"], white=0, touches=False))
        eyes = tuple(pts)
    l, r = eyes
    d = r["x"] - l["x"]
    mid = (l["x"] + r["x"]) / 2
    ey = (l["y"] + r["y"]) / 2
    mouth = None
    for c in comps:
        if c["i"] in (l["i"], r["i"]):
            continue
        if ey + d * 0.6 < c["y"] < ey + d * 1.35 and abs(c["x"] - mid) < d * 0.4 and c["w"] > d * 0.25 and not c["touches"]:
            # складка второго подбородка тоже широкая — берём ближайшее к обычному месту рта
            fit = c["w"] * c["area"] / (1 + abs(c["y"] - (ey + d * 1.0)) / (d * 0.2))
            if mouth is None or fit > mouth["fit"]:
                mouth = dict(c, fit=fit)
    return dict(l=l, r=r, d=d, mid=mid, ey=ey, mouth=mouth, face=face, skin=skin, lab=lab, ft=ft, fb=fb)


# Лица, где автоматический поиск не справился (очки, повязка, шлем, ушанка): глаза, рот,
# открыты ли брови (левая, правая), насколько их можно поднять, ширина рта в долях d.
MANUAL = {
    "yegor_08": dict(l=(98, 87), r=(138, 87), my=136, brows=(False, False)),
    "yegor_10": dict(l=(99.5, 90), r=(137.5, 90.5), my=135.5, brows=(False, True)),
    "danil_02": dict(l=(105.5, 102.5), r=(145.5, 102.5), my=141, brows=(False, False)),
    "danil_11": dict(l=(109.5, 113.5), r=(147, 113.5), my=153.5, lift=0.4),
    "aslan_10": dict(l=(105, 85), r=(138, 85), my=121, brows=(False, True), beard=True),
    "marat_02": dict(l=(104, 97), r=(142.5, 97), my=139.5, lift=0.5),
    "marat_07": dict(l=(99, 78), r=(141.5, 78), my=124.5),
    "marat_11": dict(l=(104.5, 79), r=(144.5, 79), my=117.5, lift=0.4),
    "marat_12": dict(l=(102, 77), r=(141, 77), my=121),
    "yelnur_10": dict(l=(97, 81), r=(138, 81), my=119.5),
    "yelnur_11": dict(l=(104, 77), r=(141.5, 77), my=117),
    "avatar_06": dict(l=(88, 81), r=(127.5, 81), my=119.5),
    "avatar_09": dict(l=(112.5, 77), r=(152.5, 77), my=122, lift=0.6, beard=True),
    "avatar_11": dict(l=(104.5, 77), r=(141.5, 77), my=113, mw=0.6),
}


# Поправки к автоматическому поиску: усы не стирать (рот — под ними), брови под чёлкой не трогать.
TWEAKS = {
    "char:avatar_01": dict(beard=True, mouth_shift=0.12, brows=(False, False)),
    "char:avatar_10": dict(brows=(False, False)),
}


def ellipse_mask(shape, cx, cy, rx, ry):
    yy, xx = np.mgrid[0:shape[0], 0:shape[1]]
    return ((xx - cx) / rx) ** 2 + ((yy - cy) / ry) ** 2 <= 1


def analyse(portrait, name=None):
    arr = np.asarray(portrait.convert("RGBA")).astype(np.float32)
    rgb, a = arr[..., :3], arr[..., 3] / 255
    if name in MANUAL:
        m = MANUAL[name]
        (lx, ly), (rx, ry) = m["l"], m["r"]
        d = rx - lx
        mid, ey = (lx + rx) / 2, (ly + ry) / 2
        skin = sample_skin(rgb, a, mid, ey + d * 0.55, d * 0.9, d * 0.35)
        if skin is None:
            skin = sample_skin(rgb, a, mid, ey + d * 0.4, d * 1.2, d * 0.7)
        beard = m.get("beard", False)
        mouth_mask = None if beard else ellipse_mask(a.shape, mid, m["my"], d * m.get("mw", 0.36), d * 0.16)
        return dict(lx=lx, ly=ly, rx=rx, ry=ry, d=d, mid=mid, ey=ey, my=m["my"], skin=skin, beard=beard,
                    mouth_mask=mouth_mask, face=skin_mask(rgb, a), brows=list(m.get("brows", (True, True))),
                    lift=m.get("lift", 1.0), rgb=rgb, a=a)
    f = find_features(rgb, a)
    if f is None:
        return None
    d, mid, ey = f["d"], f["mid"], f["ey"]
    skin = sample_skin(rgb, a, mid, ey + d * 0.55, d * 0.9, d * 0.35)
    if skin is None:
        return None
    H, W = a.shape
    if f["mouth"] is not None:
        m = f["mouth"]
        my = m["y"]
        mouth_mask = ndimage.binary_dilation(f["lab"] == m["i"], iterations=max(2, int(d * 0.06)))
        beard = False
    else:
        # губы не окружены кожей — значит, борода или усы: рот рисуем поверх неё
        my = ey + d * 1.05
        mouth_mask = None
        beard = True
    # брови: тёмное над глазом, если над ним открытый лоб (кожа выше и ниже)
    brows = []
    face = f["face"]
    for e in (f["l"], f["r"]):
        x0, x1 = int(e["x"] - d * 0.36), int(e["x"] + d * 0.36)
        y0, y1 = int(e["y"] - d * 0.62), int(e["y"] - d * 0.12)
        box = face[max(0, y0):max(0, y1), max(0, x0):x1]
        forehead = face[max(0, int(e["y"] - d * 0.75)):max(0, int(e["y"] - d * 0.45)), max(0, x0):x1]
        open_brow = box.size > 0 and forehead.size > 0 and forehead.mean() > 0.35
        brows.append(open_brow)
    t = TWEAKS.get(name, {})
    if t.get("beard"):
        beard, mouth_mask = True, None
    my += d * t.get("mouth_shift", 0)
    if "brows" in t:
        brows = list(t["brows"])
    return dict(lx=f["l"]["x"], ly=f["l"]["y"], rx=f["r"]["x"], ry=f["r"]["y"], d=d, mid=mid, ey=ey, my=my,
                skin=skin, beard=beard, mouth_mask=mouth_mask, face=face, brows=brows, lift=1.0, rgb=rgb, a=a)


def soft_ellipse(size, box, blur):
    m = Image.new("L", size, 0)
    ImageDraw.Draw(m).ellipse(box, fill=255)
    return m.filter(ImageFilter.GaussianBlur(blur))


def crop_up(mask, px0, py0, pw, ph, s):
    m = mask[py0:py0 + ph, px0:px0 + pw].astype(np.uint8) * 255
    return np.asarray(Image.fromarray(m).resize((pw * s, ph * s), Image.BILINEAR)).astype(np.float32) / 255


def make_patch(portrait, info, level):
    """Тревога / испуг / паника. Сохраняет форму исходных бровей и губ.

    Это мимика существующего рисованного лица, а не замена его одинаковой маской.
    Возвращает (картинка, x, y) в пикселях портрета.
    """
    level = max(1, min(3, level))
    d, mid, ey, my = info["d"], info["mid"], info["ey"], info["my"]
    skin = tuple(int(v) for v in info["skin"])
    px0, py0 = int(mid - d * 1.25), int(ey - d * 0.95)
    pw, ph = int(d * 2.5), int((my - ey) + d * 1.5)
    W, H = portrait.size
    px0, py0 = max(0, px0), max(0, py0)
    pw, ph = min(pw, W - px0), min(ph, H - py0)
    base = portrait.crop((px0, py0, px0 + pw, py0 + ph)).convert("RGBA").resize((pw * SS, ph * SS), Image.LANCZOS)
    s = SS

    def P(x, y):
        return ((x - px0) * s, (y - py0) * s)

    arr = np.asarray(base).astype(np.float32)
    rgb, al = arr[..., :3], arr[..., 3] / 255
    k = np.zeros(al.shape, np.float32)
    brows = []
    face = info["face"]
    # всё, что внутри контура лица над глазами и не кожа — брови и их тени
    for open_brow, ex, eyy in zip(info["brows"], (info["lx"], info["rx"]), (info["ly"], info["ry"])):
        if not open_brow:
            continue
        m = np.zeros(face.shape, bool)
        y0, y1 = max(0, int(eyy - d * 0.62)), max(0, int(eyy - d * 0.2))
        x0, x1 = max(0, int(ex - d * 0.4)), int(ex + d * 0.4)
        m[y0:y1, x0:x1] = True
        m &= ~face
        # тёмные штрихи, у которых есть кожа и выше, и ниже по столбцу
        above = np.zeros_like(m)
        below = np.zeros_like(m)
        reach = max(2, int(d * 0.3))
        for sh in range(1, reach):
            above[sh:] |= face[:-sh]
            below[:-sh] |= face[sh:]
        m &= above & below
        m = ndimage.binary_dilation(m, iterations=max(1, int(d * 0.04)))
        mask = crop_up(m, px0, py0, pw, ph, s)
        brows.append((mask, ex))
        k = np.maximum(k, mask)
    mouth_mask = None
    if info["mouth_mask"] is not None:
        mouth_mask = crop_up(info["mouth_mask"], px0, py0, pw, ph, s)
        k = np.maximum(k, mouth_mask)
    k = np.asarray(Image.fromarray((k * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(s * 0.8))).astype(np.float32) / 255
    k = np.clip(k * 1.6, 0, 1)[..., None]
    # Восстанавливаем местный тон и тень кожи, а не плоский прямоугольник одного цвета.
    clean = skin_mask(rgb, al).astype(np.float32) * (1 - k[..., 0])
    sigma = max(2, d * s * 0.12)
    weights = ndimage.gaussian_filter(clean, sigma)
    tone = np.stack([ndimage.gaussian_filter(rgb[..., c] * clean, sigma)
                     / np.maximum(weights, 1e-5) for c in range(3)], -1)
    tone = np.where((weights > 0.01)[..., None], tone, np.array(skin, np.float32))
    original_rgb = rgb.copy()
    rgb = rgb * (1 - k) + tone * k

    # Поднимаем внутренний край РОДНОЙ брови: её цвет, волоски, толщина и изгиб остаются.
    yy, xx = np.mgrid[0:al.shape[0], 0:al.shape[1]].astype(np.float32)
    portrait_x = xx / s + px0

    def transport(mask, shift):
        nonlocal rgb
        coords = [yy - shift * s, xx]
        soft = ndimage.gaussian_filter(mask, s * 0.25)
        moved = ndimage.map_coordinates(soft, coords, order=1, mode="constant", cval=0)
        color = np.stack([ndimage.map_coordinates(original_rgb[..., c], coords,
                          order=1, mode="nearest") for c in range(3)], -1)
        rgb = rgb * (1 - moved[..., None]) + color * moved[..., None]

    lift = (0.07, 0.14, 0.20)[level - 1] * info.get("lift", 1.0)
    for mask, ex in brows:
        side = -1 if ex < mid else 1
        inner = np.clip(0.5 - side * (portrait_x - ex) / (d * 0.65), 0, 1)
        transport(mask, -d * (0.015 * level + lift * inner))

    if level == 1 and mouth_mask is not None:
        # Родные сомкнутые губы, уголки чуть опущены; без прежней волнистой гримасы.
        corners = np.clip(np.abs(portrait_x - mid) / (d * 0.3), 0, 1)
        transport(mouth_mask, d * (0.06 * corners ** 1.5 - 0.01))
    img = Image.fromarray(np.dstack([rgb, al * 255]).astype(np.uint8), "RGBA")
    dr = ImageDraw.Draw(img)
    if level >= 2:
        # Приоткрытый рот с мягким контуром; по шею — сильнее, чем по пояс.
        # Ширина зависит от исходных губ. Усы и борода остаются нетронутыми.
        half_width = d * 0.25
        if info["mouth_mask"] is not None:
            xs = np.where(info["mouth_mask"])[1]
            if len(xs):
                half_width = float(np.clip((xs.max() - xs.min()) * 0.42, d * 0.21, d * 0.32))
        # Вертикальное раскрытие важнее ширины: узкая щель с зубами читалась как улыбка.
        half_width *= 0.9
        height = d * (0.28 if level == 2 else 0.40)
        cy = my + d * 0.025

        def curve(a, b, c):
            return [P((1-t)**2*a[0] + 2*(1-t)*t*b[0] + t*t*c[0],
                      (1-t)**2*a[1] + 2*(1-t)*t*b[1] + t*t*c[1])
                    for t in np.linspace(0, 1, 24)]

        left, right = (mid-half_width, cy), (mid+half_width, cy)
        top = curve(left, (mid, cy-height*0.75), right)
        bottom = curve(right, (mid, cy+height*1.35), left)
        outline = top + bottom
        dr.polygon(outline, fill=(55, 31, 29, 255))
        dr.line(outline + [outline[0]], fill=(83, 49, 40, 255),
                width=max(1, round(d*s*0.024)), joint="curve")
        # Небольшая полоска верхних зубов, без яркого языка и белого прямоугольника.
        teeth = curve((mid-half_width*.68, cy-height*.10),
                      (mid, cy-height*.42), (mid+half_width*.68, cy-height*.10))
        teeth += curve((mid+half_width*.68, cy-height*.10),
                       (mid, cy+height*.02), (mid-half_width*.68, cy-height*.10))
        dr.polygon(teeth, fill=(221, 208, 185, 255))
        lip = curve((mid+half_width*.65, cy+height*.42),
                    (mid, cy+height*.85), (mid-half_width*.65, cy+height*.42))
        lip_color = tuple(int(skin[c]*.68 + (135, 75, 65)[c]*.32) for c in range(3))
        dr.line(lip, fill=lip_color + (255,), width=max(1, round(d*s*.028)), joint="curve")
    out = img.resize((pw, ph), Image.LANCZOS)
    # оставляем только изменённое: остальное прозрачно, под ним тот же портрет пиксель в пиксель
    arr = np.asarray(out).astype(np.float32)
    orig = np.asarray(portrait.convert("RGBA").crop((px0, py0, px0 + pw, py0 + ph))).astype(np.float32)
    changed = np.abs(arr[..., :3] - orig[..., :3]).max(-1) > 6
    changed = ndimage.binary_dilation(changed, iterations=2)
    alpha = np.asarray(Image.fromarray((changed * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(1.0))).astype(np.float32) / 255
    alpha = np.clip(alpha * 1.4, 0, 1) * (orig[..., 3] / 255)
    rgba = np.dstack([arr[..., :3], alpha * 255])
    rgba[alpha < 0.004] = 0
    return Image.fromarray(rgba.astype(np.uint8), "RGBA"), px0, py0

if __name__ == "__main__":
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    from gen_bodies import all_skins
    out = sys.argv[1]
    names = set(sys.argv[2:])
    cells = []
    for name, path in all_skins():
        if names and name not in names:
            continue
        p = Image.open(path).convert("RGBA")
        info = analyse(p, name)
        row = [p]
        if info is None:
            print("не нашёл лицо:", name)
            row += [p, p]
        else:
            for level in (1, 2):
                q = p.copy()
                patch, x, y = make_patch(p, info, level)
                q.alpha_composite(patch, (x, y))
                row.append(q)
        cells.append((name, row, info is not None))
    per = 4
    cw, chh = 3 * 130, 150
    rows = (len(cells) + per - 1) // per
    sheet = Image.new("RGBA", (per * cw, rows * chh), (29, 58, 44, 255))
    for k, (name, row, ok) in enumerate(cells):
        for j, im in enumerate(row):
            im = im.copy()
            # крупно лицо: верхние две трети
            im = im.crop((0, 0, im.width, int(im.height * 0.72)))
            im.thumbnail((126, 146))
            sheet.alpha_composite(im, ((k % per) * cw + j * 130, (k // per) * chh))
        ImageDraw.Draw(sheet).text(((k % per) * cw + 2, (k // per) * chh + 2), name if ok else name + " ?", fill=(255, 230, 120, 255))
    sheet.convert("RGB").save(out)
    print("ok", len(cells))
