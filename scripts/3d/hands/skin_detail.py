"""Детали кожи пальцев поверх ровного тона skin_young.py: без них палец выглядит гладкой сосиской.

- Ногти. Форма считается по кости дальней фаланги в пространстве, а не по маске со скульптуры (та закрывала весь кончик
  пальца): пластина от 40 % длины фаланги до кончика, закруглённое основание, светлый свободный край, лунка, тёмный
  валик кожи вокруг. Новая пластина пишется в красный канал маски T_Hand_Male_Mask (по ней шейдер делает ноготь гладким).
- Складки и морщинки. Берутся с фото-кожи T_Hand_Male_Photo_BaseColor как отношение к её же размытой копии: остаются
  морщинки на костяшках, складки на сгибах пальцев и линии ладони, а общий тон, вены и загар уходят. На тыле кисти
  деталь ослаблена: там на фото вены и волосы, автору нужна молодая кожа.
- Рельеф. Те же складки и валик у ногтя — в карту нормалей T_Hand_Male_Young_Normal поверх рельефа со скульптуры.

Функции вызывает skin_young.py; картинки — массивы Blender (строка 0 внизу)."""
import os

import bpy
import numpy as np

FINGERS = ("Index", "Middle", "Ring", "Pinky", "Thumb")
NAIL_START, NAIL_END, NAIL_FREE = .33, .99, .885      # Доли длины дальней фаланги: основание пластины, конец, свободный край.
CREASE_DEPTH = .0003                                  # Глубина складки на сгибе пальца, м.
# Складки на ладонной стороне пальцев: кость, от начала которой считается место, доля её длины и сдвиги линий, м.
CREASES = ([(finger + "03", 0., (0.,)) for finger in ("Index", "Middle", "Ring", "Pinky")]
           + [(finger + "02", 0., (-.0011, .0011)) for finger in ("Index", "Middle", "Ring", "Pinky")]
           + [(finger + "01", .42, (0., .0017)) for finger in ("Index", "Middle", "Ring", "Pinky")]
           + [("Thumb03", 0., (0., .0015)), ("Thumb02", 0., (0.,))])
NAIL_WIDTH = .78                                      # Доля полуширины пальца.
WRINKLE_DEPTH = .0011                                 # Метров рельефа на единицу отношения яркости.
FOLD_DEPTH = .00035                                   # Глубина валика у ногтя, м.


def smooth(x):
    x = np.clip(x, 0., 1.)
    return x * x * (3 - 2 * x)


def box(a, r, axis):
    n = a.shape[axis]
    pad = [(0, 0)] * a.ndim
    pad[axis] = (r + 1, r)
    total = np.cumsum(np.pad(a, pad, mode="edge"), axis=axis, dtype=np.float64)
    high = np.take(total, np.arange(2 * r + 1, 2 * r + 1 + n), axis=axis)
    low = np.take(total, np.arange(0, n), axis=axis)
    return ((high - low) / (2 * r + 1)).astype(np.float32)


def blur(a, sigma):
    """Почти гауссово размытие: три прохода усреднения по каждой оси."""
    r = max(1, int(round(sigma * .58)))
    for _ in range(3):
        a = box(box(a, r, 0), r, 1)
    return a


def masked_blur(a, mask, sigma):
    """Размытие только по заполненным пикселям: фон вокруг островов развёртки в среднее не попадает."""
    weight = blur(mask.astype(np.float32), sigma)
    if a.ndim == 3:
        return blur(a * mask[..., None], sigma) / np.maximum(weight, 1e-3)[..., None]
    return blur(a * mask, sigma) / np.maximum(weight, 1e-3)


def load(path, colour):
    image = bpy.data.images.load(path)
    if not colour:
        image.colorspace_settings.name = "Non-Color"
    width, height = image.size
    return np.array(image.pixels[:], dtype=np.float32).reshape(height, width, 4)


def frame_of(armature, name):
    bone = armature.data.bones[name]
    world = armature.matrix_world
    matrix = np.array((world.to_3x3() @ bone.matrix_local.to_3x3()))
    return np.array(world @ bone.head_local), np.array(world @ bone.tail_local), matrix[:, 0], matrix[:, 2]


def nails(points, normals, covered, armature):
    """Пластина ногтя, свободный край, лунка и валик вокруг — по положению точки относительно дальней фаланги."""
    shape = covered.shape
    plate, free, lunula, zone = (np.zeros(shape, np.float32) for _ in range(4))
    for finger in FINGERS:
        head, tail, side, back = frame_of(armature, finger + "03_L")
        length = np.linalg.norm(tail - head)
        along = (tail - head) / length
        rel = points - head
        s = (rel @ along) / length
        u = rel @ side
        off_axis = np.linalg.norm(rel - np.outer(rel @ along, along).reshape(rel.shape), axis=-1)
        near = covered & (s > -.2) & (s < 1.25) & (off_axis < .016)
        if not near.any():
            continue
        body = near & (s > .35) & (s < .8)
        half = np.percentile(np.abs(u[body]), 98) if body.any() else .008
        edge_s = .0005 / length                                       # Мягкий край пластины: полмиллиметра.
        across = np.abs(u) / (NAIL_WIDTH * half)
        narrow = 1 + .22 * smooth((s - .7) / .3)                      # К кончику пластина чуть сужается.
        base = NAIL_START + .15 * np.clip(across, 0, 1.2) ** 2        # Основание закруглено: по бокам оно дальше.
        dorsal = smooth(((normals @ back) - .05) / .35)
        inside = (smooth((s - base) / edge_s) * smooth((NAIL_END - s) / edge_s)
                  * smooth((1 - across * narrow) / (.0005 / (NAIL_WIDTH * half))) * dorsal)
        inside = np.where(near, inside, 0.)
        plate = np.maximum(plate, inside)
        free = np.maximum(free, inside * smooth((s - (NAIL_FREE - .05 * np.clip(across, 0, 1) ** 2)) / (2 * edge_s)))
        moon = 1 - ((s - NAIL_START) / .13) ** 2 - (across / .5) ** 2
        lunula = np.maximum(lunula, inside * smooth(moon / .3))
        # Тыл кончика пальца: здесь на фото свой ноготь другой формы, его деталь сюда не берётся.
        zone = np.maximum(zone, np.where(near, smooth((s - .12) / .12) * dorsal, 0.))
    fold = np.clip(blur(plate, 2.6) - plate, 0., 1.) * covered
    fold = np.clip(fold / max(float(fold.max()), 1e-6), 0., 1.)
    return {"plate": plate, "free": free, "lunula": lunula, "fold": fold, "zone": np.clip(blur(zone, 1.5), 0., 1.)}


def creases(points, normals, covered, armature):
    """Складки на сгибах пальцев с ладонной стороны: тонкие линии поперёк пальца у каждого сустава."""
    result = np.zeros(covered.shape, np.float32)
    for name, share, offsets in CREASES:
        head, tail, side, back = frame_of(armature, name + "_L")
        length = np.linalg.norm(tail - head)
        along = (tail - head) / length
        rel = points - (head + along * length * share)
        s = rel @ along
        off_axis = np.linalg.norm(rel - np.outer(s, along).reshape(rel.shape), axis=-1)
        palmar = smooth((-(normals @ back) + .05) / .45)
        near = covered & (off_axis < .014) & (np.abs(s) < .006)
        lines = np.zeros(covered.shape, np.float32)
        for offset in offsets:
            # Линия чуть волнистая: по бокам пальца уходит к кончику, как настоящая складка.
            bend = offset + .0006 * (rel @ side / .008) ** 2
            lines = np.maximum(lines, np.exp(-((s - bend) / .00042) ** 2 / 2))
        result = np.maximum(result, np.where(near, lines * palmar, 0.))
    return result


def regions(points, normals, armature):
    """Где деталь с фото берётся целиком: пальцы, большой палец и вся ладонная сторона; на тыле кисти — слабо."""
    bones = armature.data.bones
    world = armature.matrix_world

    def head(name):
        return np.array(world @ bones[name].head_local)

    def tail(name):
        return np.array(world @ bones[name].tail_local)

    back = np.array((world.to_3x3() @ bones["Hand_L"].matrix_local.to_3x3()).col[2])
    back /= np.linalg.norm(back)
    wrist, knuckle = head("Hand_L"), head("Middle01_L")
    palm = np.linalg.norm(knuckle - wrist)
    along = (knuckle - wrist) / palm
    t = ((points - wrist) @ along) / palm
    on_hand = 1 - smooth((-t * palm) / .03)
    fingers = smooth((t - .86) / .2)
    distance = np.full(points.shape[:-1], 1e9)
    for a, b in ((head("Thumb02_L"), head("Thumb03_L")), (head("Thumb03_L"), tail("Thumb03_L"))):
        axis = b - a
        k = np.clip(((points - a) @ axis) / (axis @ axis), 0., 1.)
        distance = np.minimum(distance, np.linalg.norm(points - (a + k[..., None] * axis), axis=-1))
    thumb = 1 - smooth((distance - .017) / .009)
    facing = normals @ back
    palmar = smooth(-facing * 1.6)
    full = np.maximum(np.maximum(fingers, thumb), palmar)
    # Фото снято с тыла и с ладони: на боках пальцев и кисти оно растянуто полосами, там его деталь не берётся.
    seen = smooth((np.abs(facing) - .58) / .3)
    return on_hand * seen, full


def photo_detail(textures, covered):
    """Отношение фото-кожи к её размытой копии: средние детали (складки, линии, цвет ногтей не нужен) и мелкие (поры)."""
    photo = load(os.path.join(textures, "T_Hand_Male_Photo_BaseColor.png"), True)[..., :3]
    photo = np.where(photo <= .04045, photo / 12.92, ((photo + .055) / 1.055) ** 2.4)
    luminance = photo @ np.array((.2126, .7152, .0722), np.float32)
    middle = np.clip(luminance / np.maximum(masked_blur(luminance, covered, 11.), 1e-3), .55, 1.25)
    fine = np.clip(luminance / np.maximum(masked_blur(luminance, covered, 2.6), 1e-3), .8, 1.2)
    tint = np.clip(photo / np.maximum(masked_blur(photo, covered, 11.), 1e-3), .72, 1.14)
    return middle, fine, tint


def apply(image, covered, points, normals, armature, nail, textures, colours):
    """Красит ногти и кладёт складки с фото. Возвращает карту цвета и высоту рельефа в метрах."""
    on_hand, full = regions(points, normals, armature)
    middle, fine, tint = photo_detail(textures, covered)
    skin = 1 - nail["plate"]
    weight = (on_hand * (.3 + .7 * full) * skin * (1 - nail["zone"]) * covered)
    fold_lines = creases(points, normals, covered, armature) * skin
    # В складке кожа темнее и чуть краснее: отношение по каналам сохраняет этот оттенок.
    ratio = 1 + weight[..., None] * (tint - 1)
    pores = 1 + (on_hand * (1 - .65 * (1 - full)) * skin * (1 - nail["zone"]) * covered * .5)[..., None] * (fine[..., None] - 1)
    image = image * ratio * pores
    # Складка на сгибе темнее и краснее кожи вокруг.
    image = image * (1 - fold_lines[..., None] * np.array((.2, .3, .32), np.float32))
    # Валик кожи у ногтя темнее, пластина розовая, свободный край светлый, у основания лунка.
    image = image * (1 - .2 * nail["fold"] * skin)[..., None]
    for key, strength in (("lunula", .35), ("free", .9)):
        image = image + (colours[key] - image) * (nail[key] * strength)[..., None]
    height = WRINKLE_DEPTH * weight * (middle - 1) - FOLD_DEPTH * nail["fold"] - CREASE_DEPTH * fold_lines
    return image, height


def normal_map(height, covered, points, textures, nail):
    """Карта нормалей: рельеф со скульптуры плюс складки. У ногтей старый рельеф ослаблен: там была обводка прежней,
    слишком большой пластины."""
    step = np.linalg.norm(points[:, 2:] - points[:, :-2], axis=-1) / 2
    both = covered[:, 2:] & covered[:, :-2]
    texel = float(np.median(step[both & (step > 1e-5) & (step < .001)]))
    smooth_height = blur(height, .8)
    du = np.zeros_like(height)
    dv = np.zeros_like(height)
    du[:, 1:-1] = (smooth_height[:, 2:] - smooth_height[:, :-2]) / (2 * texel)
    dv[1:-1, :] = (smooth_height[2:, :] - smooth_height[:-2, :]) / (2 * texel)
    detail = np.stack((-du, -dv, np.ones_like(du)), axis=-1)
    detail /= np.linalg.norm(detail, axis=-1, keepdims=True)
    sculpt = load(os.path.join(textures, "T_Hand_Male_Normal.png"), False)[..., :3] * 2 - 1
    calm = np.clip(blur(np.maximum(nail["plate"], nail["fold"]), 6.) * 3, 0., 1.)[..., None]
    sculpt = sculpt * (1 - calm) + np.array((0., 0., 1.), np.float32) * calm
    mixed = np.stack((sculpt[..., 0] + detail[..., 0], sculpt[..., 1] + detail[..., 1], sculpt[..., 2] * detail[..., 2]), axis=-1)
    mixed /= np.maximum(np.linalg.norm(mixed, axis=-1, keepdims=True), 1e-6)
    mixed[~covered] = (0., 0., 1.)
    return mixed * .5 + .5, texel


def save(pixels, name, folder, copies=(), colour=True):
    height, width = pixels.shape[:2]
    image = bpy.data.images.new(name, width, height, alpha=False)
    if not colour:
        image.colorspace_settings.name = "Non-Color"
    rgba = np.ones((height, width, 4), np.float32)
    rgba[..., :3] = pixels
    image.pixels[:] = rgba.ravel()
    path = os.path.join(folder, name + ".png")
    image.filepath_raw = path
    image.file_format = "PNG"
    image.save()
    for target in copies:
        import shutil
        shutil.copyfile(path, os.path.join(target, name + ".png"))
    return path
