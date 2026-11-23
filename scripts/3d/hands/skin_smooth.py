"""Тестовая молодая кожа (переработка рук, 2026-11-16): T_Hand_Male_Smooth_BaseColor.png и T_Hand_Male_Smooth_Normal.png.
Строится из игровых T_Hand_Male_Young_*; игровые текстуры, женские руки и другие скины не меняются.

Отчего кожа выглядела сухой и возрастной (разбор по кадрам, gesture_lab.py с LAB_SKIN): не от сетки — без текстур рука
гладкая и молодая. Две причины:
- карта цвета: средние детали с фото взрослой руки (skin_detail.photo_detail) — тёмные продольные борозды на пальцах,
  сеть морщин на тыле кисти и пальцах, пятнистость;
- карта рельефа: рельеф со скульптуры — сухожилия и вены на тыле кисти, борозды на пальцах.

Что делает скрипт. Обе карты раскладываются на полосы по размеру деталей (в миллиметрах кожи, а не в пикселях):
поры (меньше FINE мм), морщины (FINE–MID мм), борозды, сухожилия и вены (MID–BROAD мм) и тон с формой (крупнее).
Цвет раскладывается в логарифме яркости (отношения, как в skin_detail), рельеф — по наклонам нормали. Полосы
складываются заново с весами по месту на руке (GAINS): на тыле кисти и на фалангах морщины и сухожилия ослаблены,
у суставов складки остаются, ногти и валик вокруг них не трогаются, поры остаются почти целиком, тон не меняется.

Запуск: blender art3d/hands/CardCity_Hands.blend --background --disable-autoexec --python scripts/3d/hands/skin_smooth.py
Файл .blend не сохраняется. SMOOTH_SUFFIX — имя варианта (по умолчанию Smooth)."""
import os
import sys

import bpy
import numpy as np

sys.path.insert(0, os.path.dirname(__file__))
import skin_detail as detail
import skin_young as young
import source

TEXTURES = os.path.join(source.ROOT, "art3d", "hands", "textures")
SUFFIX = os.environ.get("SMOOTH_SUFFIX", "Smooth")
# Границы полос, миллиметры кожи: поры, морщины, борозды и сухожилия, дальше — тон и форма.
FINE, MID, BROAD = .5, 2.2, 7.
# Веса полос по месту на руке: (поры, морщины, борозды и сухожилия) для цвета и для рельефа.
GAINS = {
    "back":    {"colour": (.75, .3, .35), "relief": (.8, .3, .3)},    # Тыл кисти: сухожилия, вены, сеть морщин.
    "finger":  {"colour": (.75, .3, .5), "relief": (.8, .35, .5)},    # Фаланги между суставами: продольные борозды.
    "joint":   {"colour": (.8, .6, .8), "relief": (.85, .65, .85)},   # Складки на средних и дальних суставах — остаются мягче.
    "knuckle": {"colour": (.7, .4, .55), "relief": (.8, .45, .6)},    # Костяшки основания пальцев: сухая сетка морщин уходит.
    "palm":    {"colour": (.8, .7, .85), "relief": (.85, .8, .9)},    # Ладонь: линии ладони естественны.
    "forearm": {"colour": (.75, .5, .6), "relief": (.8, .5, .6)},
}
# Складки у суставов: кость, доля её длины от начала, полуширина полосы вдоль пальца (м) и с какой стороны пальца
# (1 — только с тыла: костяшка основания пальца; -1 — только с ладони: складка у основания пальца; 0 — кругом).
# Полоса меряется вдоль кости, а не от центра сустава: центр основного сустава лежит в глубине ладони, на 2 см от кожи.
JOINTS = ([(f + "01", 0., .0065, 1) for f in ("Index", "Middle", "Ring", "Pinky")]
          + [(f + "01", .42, .0035, -1) for f in ("Index", "Middle", "Ring", "Pinky")]
          + [(f + "02", 0., .0042, 0) for f in ("Index", "Middle", "Ring", "Pinky")]
          + [(f + "03", 0., .0034, 0) for f in ("Index", "Middle", "Ring", "Pinky")]
          + [("Thumb02", 0., .0055, 0), ("Thumb03", 0., .0042, 0)])


def regions(points, normals, covered, armature):
    """Доли каждой области в каждом пикселе развёртки (в сумме 1)."""
    bones = armature.data.bones
    world = armature.matrix_world

    def head(name):
        return np.array(world @ bones[name + "_L"].head_local)

    back = np.array((world.to_3x3() @ bones["Hand_L"].matrix_local.to_3x3()).col[2])
    back /= np.linalg.norm(back)
    wrist, knuckle = head("Hand"), head("Middle01")
    palm_length = np.linalg.norm(knuckle - wrist)
    t = ((points - wrist) @ ((knuckle - wrist) / palm_length)) / palm_length
    on_hand = 1 - detail.smooth(-t / .12)
    fingers = detail.smooth((t - .9) / .15)
    # Большой палец: всё, что ближе к его костям, чем 14 мм, — тоже «палец».
    thumb = np.zeros(points.shape[:-1], np.float32)
    for name in ("Thumb02", "Thumb03"):
        a = head(name)
        b = np.array(world @ bones[name + "_L"].tail_local)
        k = np.clip(((points - a) @ (b - a)) / ((b - a) @ (b - a)), 0., 1.)
        thumb = np.maximum(thumb, 1 - detail.smooth((np.linalg.norm(points - (a + k[..., None] * (b - a)), axis=-1) - .012) / .006))
    fingers = np.maximum(fingers, thumb)
    palmar = detail.smooth(-(normals @ back) * 1.6 + .1)
    joint = np.zeros(points.shape[:-1], np.float32)
    knuckle = np.zeros(points.shape[:-1], np.float32)
    for name, share, half, facing in JOINTS:
        a = head(name)
        b = np.array(world @ bones[name + "_L"].tail_local)
        along = (b - a) / np.linalg.norm(b - a)
        rel = points - (a + (b - a) * share)
        s = rel @ along
        off_axis = np.linalg.norm(rel - s[..., None] * along, axis=-1)
        band = np.exp(-(s / half) ** 2 / 2) * (1 - detail.smooth((off_axis - .011) / .004))
        side = {1: 1 - palmar, -1: palmar, 0: 1.}[facing]
        if facing == 1:
            knuckle = np.maximum(knuckle, band * side)
        else:
            joint = np.maximum(joint, band * side)
    joint = np.maximum(joint - knuckle, 0.)
    weights = {
        "forearm": 1 - on_hand,
        "palm": on_hand * palmar * (1 - fingers),
        "back": on_hand * (1 - palmar) * (1 - fingers),
        "finger": on_hand * fingers,
    }
    # Суставы поверх остального: складки на сгибах и костяшки с обеих сторон пальца.
    for key in weights:
        weights[key] = weights[key] * (1 - joint - knuckle)
    weights["joint"] = joint * on_hand
    weights["knuckle"] = knuckle * on_hand
    total = sum(weights.values())
    return {k: np.where(covered, v / np.maximum(total, 1e-6), 0.).astype(np.float32) for k, v in weights.items()}


def gains(weights, kind):
    """Веса трёх полос в каждом пикселе: смесь весов областей."""
    result = [np.zeros_like(weights["back"]) for _ in range(3)]
    for region, share in weights.items():
        for band in range(3):
            result[band] += share * GAINS[region][kind][band]
    return result


def recombine(layer, covered, sigmas, weight, keep):
    """Полосы слоя (по последней оси — каналы) складываются заново с весами; там, где keep = 1, слой не меняется."""
    blurred = [detail.masked_blur(layer, covered, s) for s in sigmas]
    fine, mid, broad = layer - blurred[0], blurred[0] - blurred[1], blurred[1] - blurred[2]
    rebuilt = blurred[2] + broad * weight[2][..., None] + mid * weight[1][..., None] + fine * weight[0][..., None]
    return layer + (rebuilt - layer) * (1 - keep)[..., None]


def band_energy(layer, covered, sigmas, mask):
    blurred = [detail.masked_blur(layer, covered, s) for s in sigmas]
    bands = (layer - blurred[0], blurred[0] - blurred[1], blurred[1] - blurred[2])
    return [float(np.sqrt((b[mask] ** 2).mean())) for b in bands]


def main():
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = 1
    armature = bpy.data.objects["Rig_Hands"]
    armature.data.pose_position = "REST"
    arm = bpy.data.objects["Arm_Male_L"]
    bpy.context.view_layer.update()
    base = detail.load(os.path.join(TEXTURES, "T_Hand_Male_Young_BaseColor.png"), True)[..., :3]
    normal = detail.load(os.path.join(TEXTURES, "T_Hand_Male_Young_Normal.png"), False)[..., :3] * 2 - 1
    size = (base.shape[1], base.shape[0])
    points, covered = young.bake_attribute(arm, size, "Position")
    normals, _ = young.bake_attribute(arm, size, "Normal")
    normals /= np.maximum(np.linalg.norm(normals, axis=-1, keepdims=True), 1e-6)

    # Сколько миллиметров кожи в пикселе развёртки: полосы задаются в миллиметрах.
    step = np.linalg.norm(points[:, 2:] - points[:, :-2], axis=-1) / 2
    both = covered[:, 2:] & covered[:, :-2]
    texel = float(np.median(step[both & (step > 1e-5) & (step < .001)])) * 1000
    sigmas = [FINE / texel, MID / texel, BROAD / texel]
    print("=== SMOOTH texel %.3f мм, полосы в пикселях: %s" % (texel, ", ".join("%.1f" % s for s in sigmas)))

    weights = regions(points, normals, covered, armature)
    for name, share in weights.items():
        print("=== SMOOTH область %-8s %.1f%% развёртки" % (name, share[covered].sum() / covered.sum() * 100))
    nail = detail.nails(points, normals, covered, armature)
    keep = np.clip(detail.blur(np.maximum(nail["plate"], np.maximum(nail["fold"], nail["zone"] * .7)), 1.5) * 1.6, 0., 1.)

    # Цвет: в логарифме линейной яркости — полосы становятся отношениями к соседям, тон не сдвигается.
    linear = young.srgb_to_linear(np.clip(base, 0, 1) * 255.)
    log = np.log(np.maximum(linear, 1e-4)).astype(np.float32)
    regions_for_stats = {"тыл кисти": weights["back"] > .6, "фаланги": weights["finger"] > .6, "суставы": weights["joint"] > .6,
                         "костяшки": weights["knuckle"] > .6}
    colour_gain = gains(weights, "colour")
    new_log = recombine(log, covered, sigmas, colour_gain, keep)
    new_colour = young.linear_to_srgb(np.exp(new_log))
    new_colour[~covered] = base[~covered]

    # Рельеф: наклоны нормали (x/z, y/z) — полосы рельефа складываются так же.
    slopes = (normal[..., :2] / np.maximum(normal[..., 2:3], .2)).astype(np.float32)
    relief_gain = gains(weights, "relief")
    new_slopes = recombine(slopes, covered, sigmas, relief_gain, keep)
    new_normal = np.concatenate((new_slopes, np.ones_like(new_slopes[..., :1])), axis=-1)
    new_normal /= np.linalg.norm(new_normal, axis=-1, keepdims=True)
    new_normal[~covered] = normal[~covered] / np.maximum(np.linalg.norm(normal[~covered], axis=-1, keepdims=True), 1e-6)

    luminance = np.array((.2126, .7152, .0722), np.float32)
    for title, mask in regions_for_stats.items():
        mask = mask & covered
        before_c = band_energy(log @ luminance, covered, sigmas, mask)
        after_c = band_energy(new_log @ luminance, covered, sigmas, mask)
        before_r = band_energy(np.linalg.norm(slopes, axis=-1), covered, sigmas, mask)
        after_r = band_energy(np.linalg.norm(new_slopes, axis=-1), covered, sigmas, mask)
        print("=== SMOOTH %-10s цвет (поры, морщины, борозды): %s -> %s | рельеф: %s -> %s" % (
            title, " ".join("%.3f" % v for v in before_c), " ".join("%.3f" % v for v in after_c),
            " ".join("%.3f" % v for v in before_r), " ".join("%.3f" % v for v in after_r)))
    tone_before = linear[covered].mean(axis=0)
    tone_after = np.exp(new_log)[covered].mean(axis=0)
    print("=== SMOOTH средний тон sRGB: %s -> %s" % (np.round(young.linear_to_srgb(tone_before) * 255).tolist(), np.round(young.linear_to_srgb(tone_after) * 255).tolist()))

    detail.save(new_colour, "T_Hand_Male_%s_BaseColor" % SUFFIX, TEXTURES)
    detail.save(new_normal * .5 + .5, "T_Hand_Male_%s_Normal" % SUFFIX, TEXTURES, colour=False)
    # Карта областей — для проверки глазами: красный — тыл кисти, зелёный — фаланги, синий — суставы, лиловый — костяшки.
    regions_image = np.stack((weights["back"] + weights["knuckle"], weights["finger"], weights["joint"] + weights["knuckle"]), axis=-1)
    detail.save(regions_image, "regions_%s" % SUFFIX, os.environ.get("SMOOTH_DEBUG", bpy.app.tempdir), colour=False)
    print("=== SMOOTH SAVED", os.path.join(TEXTURES, "T_Hand_Male_%s_*.png" % SUFFIX))


main()
