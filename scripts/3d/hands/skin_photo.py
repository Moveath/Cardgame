"""Фотография кожи на развёртку руки: тыл — с листа генерации 8, ладонь — с листа 9 (правая рука; левая сетка получает её зеркально
через ориентиры, правая — как есть, развёртка у них общая).
1. Рука на фото выделяется по цвету кожи на сером фоне, отрезается по запястью.
2. На контуре руки и на контуре острова развёртки находятся кончики пяти пальцев, четыре впадины между ними и края запястья;
   контур между ними делится на равные отрезки. По этим парам точек тонкая пластина (thin plate spline) переводит развёртку в фото.
3. С фото снимаются крупные тени (кожа должна быть без света, свет даёт Unity), остаются поры, складки, вены и ногти.
   Цвет подгоняется под прежний оттенок кожи материала. Фон у краёв руки заполняется ближайшей кожей.
4. Предплечья под рукавом получают кожу предплечья с тех же фото.
5. Из мелких деталей фото получается рельеф, он добавляется к запечённой карте нормалей.
Пишет T_Hand_Male_Photo_BaseColor.png и T_Hand_Male_Photo_Normal.png в art3d/hands/textures и в Resources/ThreeD/Hands.
Старые текстуры не трогает. Сначала uv_landmarks.py (Blender), затем: python scripts/3d/hands/skin_photo.py"""
import json
from pathlib import Path

import cv2
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage
from scipy.interpolate import RBFInterpolator

ROOT = Path(__file__).resolve().parents[3]
TEXTURES = ROOT / "art3d" / "hands" / "textures"
UNITY = ROOT / "unity" / "CardGameClient" / "Assets" / "Resources" / "ThreeD" / "Hands"
PHOTOS = {"back": ROOT / "генерации" / "8-рука-тыл" / "готово" / "тыл.jpg",
          "palm": ROOT / "генерации" / "9-рука-ладонь" / "готово" / "ладонь.jpg"}
CHECK = ROOT / "screens" / "3d-hands"
DIGITS = ("Thumb", "Index", "Middle", "Ring", "Pinky")
# Прежний цвет кожи в Unity, линейный: оттенок материала (0,82; 0,64; 0,55) в гамме, переведённый в линейный,
# умноженный на почти белую карту (около 0,93). Под него подгоняется среднее фото.
SKIN_LINEAR = np.array([.59, .34, .245])
SEGMENT_POINTS = 14


def to_linear(c):
    return np.where(c <= .04045, c / 12.92, ((c + .055) / 1.055) ** 2.4)


def to_srgb(c):
    c = np.clip(c, 0, 1)
    return np.where(c <= .0031308, c * 12.92, 1.055 * c ** (1 / 2.4) - .055)


def largest(mask):
    labels, count = ndimage.label(mask)
    if count == 0:
        return mask
    sizes = ndimage.sum(mask, labels, range(1, count + 1))
    return labels == (np.argmax(sizes) + 1)


def outline(mask):
    contours, _ = cv2.findContours(mask.astype(np.uint8), cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
    return max(contours, key=cv2.contourArea)[:, 0, :].astype(np.float64)


def valleys(contour, count=4):
    """Самые глубокие впадины контура (между пальцами): индексы точек контура."""
    hull = cv2.convexHull(contour.astype(np.int32), returnPoints=False)
    defects = cv2.convexityDefects(contour.astype(np.int32), hull)
    deepest = sorted(np.asarray(defects).reshape(-1, 4).tolist(), key=lambda d: -d[3])[:count]
    return sorted(d[2] for d in deepest)


def between(contour, a, b):
    """Точки контура от индекса a до индекса b по ходу контура."""
    n = len(contour)
    return contour[np.arange(a, a + (b - a) % n + 1) % n]


def resample(points, count):
    steps = np.r_[0, np.cumsum(np.linalg.norm(np.diff(points, axis=0), axis=1))]
    at = np.linspace(0, steps[-1], count)
    return np.stack([np.interp(at, steps, points[:, 0]), np.interp(at, steps, points[:, 1])], axis=1)


def digits_and_wrist(contour, wrist_a, wrist_b):
    """По контуру руки: кончики пяти пальцев, впадины, края запястья. wrist_a, wrist_b — индексы краёв запястья на контуре.
    Пальцы именуются по ходу контура; большой — тот из крайних, чей кончик ближе к запястью."""
    n = len(contour)
    cuts = valleys(contour)
    # Ход контура от wrist_a через пальцы к wrist_b: впадины должны лежать на этом пути.
    path = (wrist_b - wrist_a) % n
    cuts = sorted(cuts, key=lambda i: (i - wrist_a) % n)
    if any((c - wrist_a) % n > path for c in cuts):
        wrist_a, wrist_b = wrist_b, wrist_a
        path = (wrist_b - wrist_a) % n
        cuts = sorted(cuts, key=lambda i: (i - wrist_a) % n)
    centre = contour.mean(axis=0)
    wrist_mid = (contour[wrist_a] + contour[wrist_b]) / 2
    marks = [wrist_a]
    edges = [wrist_a] + cuts + [wrist_b]
    tips = []
    for start, end in zip(edges, edges[1:]):
        span = np.arange(start, start + (end - start) % n + 1) % n
        far = span[np.argmax(np.linalg.norm(contour[span] - wrist_mid, axis=1))]
        tips.append(far)
    # Большой палец — крайний, чей кончик ближе к запястью.
    first, last = np.linalg.norm(contour[tips[0]] - wrist_mid), np.linalg.norm(contour[tips[-1]] - wrist_mid)
    order = DIGITS if first < last else DIGITS[::-1]
    named = {"wrist_a": wrist_a, "wrist_b": wrist_b}
    for name, tip in zip(order, tips):
        named["tip_" + name] = tip
    for k, cut in enumerate(cuts):
        pair = (order[k], order[k + 1])
        named["valley_" + "_".join(sorted(pair))] = cut
    # Край запястья со стороны большого пальца — radial.
    if order[0] == "Thumb":
        named["wrist_radial"], named["wrist_ulnar"] = wrist_a, wrist_b
    else:
        named["wrist_radial"], named["wrist_ulnar"] = wrist_b, wrist_a
    del named["wrist_a"], named["wrist_b"]
    return named, centre


def chain(named):
    thumb_side = ["wrist_radial", "tip_Thumb", "valley_Index_Thumb", "tip_Index", "valley_Index_Middle", "tip_Middle",
                  "valley_Middle_Ring", "tip_Ring", "valley_Pinky_Ring", "tip_Pinky", "wrist_ulnar"]
    return thumb_side


def correspondences(contour_a, named_a, contour_b, named_b):
    """Пары точек: по каждому отрезку контура между одноимёнными ориентирами — равные шаги в обоих контурах."""
    src, dst = [], []
    names = chain(named_a)
    for x, y in zip(names, names[1:]):
        pa = between(contour_a, named_a[x], named_a[y])
        pb = between(contour_b, named_b[x], named_b[y])
        # Если ход контура обратный, отрезок между ориентирами берётся в другую сторону.
        if len(pa) > len(contour_a) * .6:
            pa = between(contour_a, named_a[y], named_a[x])[::-1]
        if len(pb) > len(contour_b) * .6:
            pb = between(contour_b, named_b[y], named_b[x])[::-1]
        src.append(resample(pa, SEGMENT_POINTS)[:-1])
        dst.append(resample(pb, SEGMENT_POINTS)[:-1])
    # Край запястья — прямая между его краями.
    for points, named, out in ((contour_a, named_a, src), (contour_b, named_b, dst)):
        line = np.stack([points[named["wrist_ulnar"]], points[named["wrist_radial"]]])
        out.append(resample(line, SEGMENT_POINTS)[:-1])
    return np.concatenate(src), np.concatenate(dst)


def photo_hand(path):
    """Фото: маска руки до запястья, контур, ориентиры, кожа без крупных теней и с заполненным фоном."""
    rgb = np.asarray(Image.open(path).convert("RGB"), dtype=np.float32) / 255
    chroma = (rgb[:, :, 0] - rgb[:, :, 2]) * 255
    mask = largest(ndimage.binary_opening(chroma > 14, iterations=3))
    mask = ndimage.binary_fill_holes(ndimage.binary_closing(mask, iterations=4))
    height = mask.sum(axis=0)
    columns = np.nonzero(height)[0]
    left = columns.min()
    # Запястье — самое узкое место руки на 55–80 % длины кадра от кончиков пальцев.
    span = np.arange(left + int(.55 * (mask.shape[1] - left)), left + int(.8 * (mask.shape[1] - left)))
    wrist_x = span[np.argmin(height[span])]
    hand = mask.copy()
    hand[:, wrist_x:] = False
    contour = outline(hand)
    on_cut = np.nonzero(contour[:, 0] >= wrist_x - 2)[0]
    ys = contour[on_cut, 1]
    wrist_a, wrist_b = on_cut[np.argmin(ys)], on_cut[np.argmax(ys)]
    named, _ = digits_and_wrist(contour, wrist_a, wrist_b)

    # Кожа без света: делим на сильно размытую яркость, остаются мелкие детали.
    linear = to_linear(rgb)
    weight = ndimage.gaussian_filter(mask.astype(np.float64), 45) + 1e-4
    low = np.stack([ndimage.gaussian_filter(linear[:, :, c] * mask, 45) / weight for c in range(3)], axis=2)
    luminance = low.mean(axis=2, keepdims=True)
    flat = linear / np.maximum(luminance, 1e-3) * luminance[mask].mean()
    skin_mean = flat[mask].mean(axis=0)
    flat = flat * (SKIN_LINEAR / skin_mean)
    # Фон и край руки заполняются ближайшей кожей: у края на фото кожа темнее и серее (смешана с фоном),
    # на модели эта кайма легла бы полосой вдоль боков пальцев.
    inner = ndimage.binary_erosion(mask, iterations=14)
    _, (iy, ix) = ndimage.distance_transform_edt(~inner, return_indices=True)
    flat = flat[iy, ix]
    forearm = flat[:, wrist_x + 40:]
    forearm_mask = mask[:, wrist_x + 40:]
    rows = np.nonzero(forearm_mask.any(axis=1))[0]
    forearm = forearm[rows.min() + 40: rows.max() - 40]
    return {"contour": contour, "named": named, "skin": flat, "forearm": forearm, "wrist_x": wrist_x, "mask": mask}


def island_mask(faces, island, size):
    image = Image.new("L", size, 0)
    draw = ImageDraw.Draw(image)
    for number, points in faces:
        if number == island:
            draw.polygon([tuple(p) for p in points], fill=255)
    return np.asarray(image) > 0


def island_hand(mask, marks):
    contour = outline(mask)
    tips = marks["points"]

    def nearest(point):
        return int(np.argmin(np.linalg.norm(contour - np.array(point), axis=1)))

    # Края запястья острова — углы его кромки, самой дальней от пальцев: вдоль оси кисти (от запястья к среднему пальцу)
    # берутся точки контура в нижних 12 %, из них — крайние поперёк оси.
    wrist = (np.array(tips["wrist_radial"]) + np.array(tips["wrist_ulnar"])) / 2
    axis = np.array(tips["tip_Middle"]) - wrist
    axis /= np.linalg.norm(axis)
    across = np.array([-axis[1], axis[0]])
    along = (contour - wrist) @ axis
    low = np.nonzero(along < along.min() + .12 * (along.max() - along.min()))[0]
    side = (contour[low] - wrist) @ across
    wrist_a, wrist_b = int(low[np.argmin(side)]), int(low[np.argmax(side)])
    named, _ = digits_and_wrist(contour, wrist_a, wrist_b)
    # Кончики — по Blender: на развёртке пальцы сходятся под углом, и самая далёкая точка не всегда кончик.
    for digit in DIGITS:
        named["tip_" + digit] = nearest(tips["tip_" + digit])
    return contour, named


def warp(photo, contour, named, mask):
    src, dst = correspondences(contour, named, photo["contour"], photo["named"])
    centre_src, centre_dst = contour.mean(axis=0), photo["contour"].mean(axis=0)
    src = np.vstack([src, centre_src])
    dst = np.vstack([dst, centre_dst])
    tps = RBFInterpolator(src, dst, kernel="thin_plate_spline", smoothing=2.)
    ys, xs = np.nonzero(ndimage.binary_dilation(mask, iterations=6))
    where = tps(np.stack([xs, ys], axis=1).astype(np.float64))
    out = np.zeros(mask.shape + (3,), np.float32)
    for c in range(3):
        out[ys, xs, c] = ndimage.map_coordinates(photo["skin"][:, :, c], [where[:, 1], where[:, 0]], order=1, mode="nearest")
    return out, (ys, xs), src, dst


def fill_forearm(texture, mask, patch):
    ys, xs = np.nonzero(mask)
    top, bottom, left, right = ys.min(), ys.max(), xs.min(), xs.max()
    resized = cv2.resize(patch.astype(np.float32), (right - left + 1, bottom - top + 1), interpolation=cv2.INTER_AREA)
    texture[ys, xs] = resized[ys - top, xs - left]


def detail_normal(albedo_srgb, base_normal, islands, strength=.9):
    """Рельеф из мелких деталей фото (поры, складки): тёмное — ниже. Добавляется к запечённым нормалям.
    У краёв островов развёртки гаснет: на шве тыла и ладони иначе получился бы ложный гребень вдоль пальцев."""
    lum = albedo_srgb.mean(axis=2)
    height = -(lum - ndimage.gaussian_filter(lum, 5))
    fade = np.clip(ndimage.distance_transform_edt(islands) / 10., 0, 1)
    gy, gx = np.gradient(ndimage.gaussian_filter(height, .8))
    gx, gy = gx * fade, gy * fade
    base = base_normal * 2 - 1
    nx = base[:, :, 0] - gx * strength * 255 / 40
    ny = base[:, :, 1] + gy * strength * 255 / 40
    nz = base[:, :, 2]
    norm = np.sqrt(nx ** 2 + ny ** 2 + nz ** 2)
    return np.stack([nx / norm, ny / norm, nz / norm], axis=2) * .5 + .5


def main():
    data = json.loads((TEXTURES / "uv_landmarks.json").read_text(encoding="utf-8"))
    size = tuple(data["size"])
    texture = np.zeros((size[1], size[0], 3), np.float32)
    filled = np.zeros((size[1], size[0]), bool)
    island_area = np.zeros((size[1], size[0]), bool)
    for number in {n for n, _ in data["faces"]}:
        island_area |= island_mask(data["faces"], number, size)
    photos = {side: photo_hand(path) for side, path in PHOTOS.items()}
    check = Image.new("RGB", (size[0], size[1]), (40, 40, 40))
    for side in ("back", "palm"):
        marks = data["marks"][side]
        mask = island_mask(data["faces"], marks["island"], size)
        contour, named = island_hand(mask, marks)
        out, (ys, xs), src, dst = warp(photos[side], contour, named, mask)
        texture[ys, xs] = out[ys, xs]
        filled[ys, xs] = True
        print(side, "island", marks["island"], "landmarks", {k: tuple(int(v) for v in contour[i]) for k, i in named.items()})
        print(side, "photo", {k: tuple(int(v) for v in photos[side]["contour"][i]) for k, i in photos[side]["named"].items()})
    # Предплечья под рукавом: кожа предплечья с фото тыла и ладони.
    islands = sorted({n for n, _ in data["faces"]} - {data["marks"]["back"]["island"], data["marks"]["palm"]["island"]})
    for island, side in zip(islands, ("palm", "back")):
        mask = ndimage.binary_dilation(island_mask(data["faces"], island, size), iterations=6)
        fill_forearm(texture, mask, photos[side]["forearm"])
        filled |= mask
    # Поля вокруг островов — ближайшей кожей, чтобы на краях развёртки не было каймы.
    _, (iy, ix) = ndimage.distance_transform_edt(~filled, return_indices=True)
    texture = texture[iy, ix]
    srgb = to_srgb(texture)
    base_normal = np.asarray(Image.open(TEXTURES / "T_Hand_Male_Normal.png").convert("RGB"), dtype=np.float32) / 255
    normal = detail_normal(srgb, base_normal, island_area)
    color_image = Image.fromarray((srgb * 255 + .5).astype(np.uint8))
    normal_image = Image.fromarray((np.clip(normal, 0, 1) * 255 + .5).astype(np.uint8))
    for folder in (TEXTURES, UNITY):
        color_image.save(folder / "T_Hand_Male_Photo_BaseColor.png")
        normal_image.save(folder / "T_Hand_Male_Photo_Normal.png")
    # Проверочный лист: развёртка с наложенными контурами островов.
    preview = color_image.copy()
    draw = ImageDraw.Draw(preview)
    for _, points in data["faces"]:
        draw.polygon([tuple(p) for p in points], outline=(0, 255, 0))
    preview.resize((1024, 512)).save(CHECK / "skin-photo-uv.png")
    print("=== SKIN PHOTO DONE", size)


if __name__ == "__main__":
    main()
