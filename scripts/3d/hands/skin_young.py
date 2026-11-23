"""Молодая кожа: T_Hand_Male_Young_BaseColor.png, T_Hand_Male_Young_Normal.png и красный канал T_Hand_Male_Mask.png.

Тон считается по точкам руки в пространстве, а не по островам развёртки, поэтому на стыках островов (запястье, бока
пальцев) шва нет. Тон и розовые места — по листу генерации/12-жесты: светлая кожа, розовее костяшки, кончики пальцев
и ладонь. Поверх тона — детали пальцев (skin_detail.py): ногти с лункой и светлым краем, складки на сгибах, морщинки
на костяшках и линии ладони с фото-кожи, без вен и волос. Без этих деталей пальцы выглядели гладкими сосисками.
Пустое место вокруг островов залито соседним цветом на всю текстуру: на дальних мип-уровнях не протекает фон.

Запуск: blender art3d/hands/CardCity_Hands.blend --background --disable-autoexec --python scripts/3d/hands/skin_young.py"""
import os
import shutil
import sys

import bpy
import numpy as np

sys.path.insert(0, os.path.dirname(__file__))
import skin_detail
import source

TEXTURES = os.path.join(source.ROOT, "art3d", "hands", "textures")
UNITY = os.path.join(source.ROOT, "unity", "CardGameClient", "Assets", "Resources", "ThreeD", "Hands")
NAME = "T_Hand_Male_Young_BaseColor"
OFFSET = 10.   # Цвет запекания не бывает отрицательным: точки сдвинуты на 10 м.


def srgb_to_linear(c):
    c = np.asarray(c, float) / 255.
    return np.where(c <= .04045, c / 12.92, ((c + .055) / 1.055) ** 2.4)


def linear_to_srgb(c):
    c = np.clip(c, 0., 1.)
    return np.where(c <= .0031308, c * 12.92, 1.055 * c ** (1 / 2.4) - .055)


# Тон по листу 12 (освещённые места там светлее: свет ярче белого листа).
BASE = srgb_to_linear((226, 180, 158))       # Тыл кисти и пальцев.
PALM = srgb_to_linear((236, 190, 172))       # Ладонь светлее и розовее.
PINK = srgb_to_linear((222, 148, 136))       # Костяшки, кончики пальцев, складки ладони.
FOREARM = srgb_to_linear((222, 178, 152))    # Предплечье чуть желтее.
NAIL = srgb_to_linear((233, 178, 172))       # Пластина ногтя: розовое ложе.
NAIL_FREE = srgb_to_linear((244, 233, 223))  # Свободный край.
NAIL_LUNULA = srgb_to_linear((243, 214, 207))


def smooth(x):
    x = np.clip(x, 0., 1.)
    return x * x * (3 - 2 * x)


def bake_attribute(arm, size, socket):
    """Запекает в развёртку точку или нормаль руки в покое: float-картинка, 1 канал на ось."""
    image = bpy.data.images.new("bake_" + socket, size[0], size[1], float_buffer=True, alpha=True)
    image.generated_color = (0, 0, 0, 0)
    material = bpy.data.materials.new("bake_" + socket)
    material.use_nodes = True
    nodes, links = material.node_tree.nodes, material.node_tree.links
    nodes.clear()
    geometry = nodes.new("ShaderNodeNewGeometry")
    shift = nodes.new("ShaderNodeVectorMath")
    shift.operation = "ADD"
    shift.inputs[1].default_value = (OFFSET, OFFSET, OFFSET)
    emission = nodes.new("ShaderNodeEmission")
    output = nodes.new("ShaderNodeOutputMaterial")
    target = nodes.new("ShaderNodeTexImage")
    target.image = image
    links.new(geometry.outputs[socket], shift.inputs[0])
    links.new(shift.outputs[0], emission.inputs["Color"])
    links.new(emission.outputs[0], output.inputs["Surface"])
    nodes.active = target
    arm.data.materials.clear()
    arm.data.materials.append(material)
    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.context.scene.render.bake.use_selected_to_active = False
    bpy.ops.object.bake(type="EMIT", margin=0, use_clear=True, use_selected_to_active=False)
    pixels = np.array(image.pixels[:], dtype=np.float32).reshape(size[1], size[0], 4)
    covered = pixels[..., :3].min(axis=-1) > OFFSET / 2
    return pixels[..., :3] - OFFSET, covered


def noise(points, scale, seed):
    """Плавный шум в пространстве: сумма синусов по случайным направлениям, от -1 до 1."""
    rng = np.random.default_rng(seed)
    total = np.zeros(len(points))
    for _ in range(7):
        direction = rng.normal(size=3)
        direction /= np.linalg.norm(direction)
        total += np.sin(points @ direction / scale + rng.uniform(0, 6.283))
    return total / np.sqrt(7 / 2)


def bump(points, centre, radius):
    return np.exp(-np.sum((points - centre) ** 2, axis=-1) / (2 * radius * radius))


def colour(points, normals, armature, nail, crease):
    bones = armature.data.bones
    world = armature.matrix_world

    def head(name):
        return np.array(world @ bones[name].head_local)

    def tail(name):
        return np.array(world @ bones[name].tail_local)

    hand = bones["Hand_L"]
    back = np.array((world.to_3x3() @ hand.matrix_local.to_3x3()).col[2])
    back /= np.linalg.norm(back)
    wrist, knuckle = head("Hand_L"), head("Middle01_L")
    along = (knuckle - wrist) / np.linalg.norm(knuckle - wrist)

    dorsal = np.clip(normals @ back, -1., 1.)
    behind_wrist = (wrist - points) @ along          # Больше нуля — уже предплечье.
    on_hand = 1 - smooth(behind_wrist / .03)

    c = np.tile(BASE, (len(points), 1))
    c += (PALM - BASE) * (smooth(-dorsal * 1.6) * on_hand)[:, None]

    # Костяшки — только с тыла, сильнее у основания пальцев; кончики пальцев и подушечки — со всех сторон.
    knuckles = np.zeros(len(points))
    for finger in ("Index", "Middle", "Ring", "Pinky"):
        for number, radius, strength in (("01", .011, .55), ("02", .008, .5), ("03", .006, .35)):
            knuckles = np.maximum(knuckles, strength * bump(points, head(finger + number + "_L"), radius))
        tip = tail(finger + "03_L")
        knuckles = np.maximum(knuckles, .55 * bump(points, tip, .009))
    for number, radius, strength in (("02", .01, .45), ("03", .007, .3)):
        knuckles = np.maximum(knuckles, strength * bump(points, head("Thumb" + number + "_L"), radius))
    knuckles = np.maximum(knuckles, .5 * bump(points, tail("Thumb03_L"), .01))
    dorsal_knuckle = np.where(dorsal > 0, 1., .55)
    pink = knuckles * dorsal_knuckle
    # Ладонь розовее к краям и у тенара, складки ладони чуть темнее и розовее.
    palm_centre = (wrist + knuckle) / 2
    palm_edge = (1 - bump(points, palm_centre, .03)) * smooth(-dorsal * 1.6) * on_hand
    pink = np.maximum(pink, .3 * palm_edge)
    pink = np.maximum(pink, .45 * crease * on_hand)
    c += (PINK - c) * np.clip(pink, 0., 1.)[:, None]

    c += (FOREARM - c) * (1 - on_hand)[:, None]
    # Неровность тона: крупная почти незаметная и мелкая, чтобы кожа не была пластиком.
    c *= (1 + .035 * noise(points, .012, 1) + .018 * noise(points, .0025, 2))[:, None]
    c += (NAIL - c) * smooth(nail * 1.4)[:, None]
    return c


def fill_empty(image, covered, rounds=40):
    """Каждый пустой пиксель берёт средний цвет заполненных соседей; дальше остатков — средний цвет кожи."""
    filled = covered.copy()
    for _ in range(rounds):
        total = np.zeros_like(image)
        count = np.zeros(filled.shape)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, -1), (1, -1), (-1, 1)):
            total += np.roll(np.roll(image * filled[..., None], dy, 0), dx, 1)
            count += np.roll(np.roll(filled, dy, 0), dx, 1)
        grow = ~filled & (count > 0)
        image[grow] = total[grow] / count[grow, None]
        filled |= grow
    image[~filled] = image[covered].mean(axis=0)
    return image


def main():
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = 1
    armature = bpy.data.objects["Rig_Hands"]
    armature.data.pose_position = "REST"
    arm = bpy.data.objects["Arm_Male_L"]
    bpy.context.view_layer.update()
    mask = bpy.data.images.load(os.path.join(TEXTURES, "T_Hand_Male_Mask.png"))
    mask.colorspace_settings.name = "Non-Color"
    size = tuple(mask.size)
    mask_pixels = np.array(mask.pixels[:], dtype=np.float32).reshape(size[1], size[0], 4)

    points, covered = bake_attribute(arm, size, "Position")
    normals, _ = bake_attribute(arm, size, "Normal")
    normals /= np.maximum(np.linalg.norm(normals, axis=-1, keepdims=True), 1e-6)
    nail = skin_detail.nails(points, normals, covered, armature)
    image = np.zeros((size[1], size[0], 3), np.float32)
    image[covered] = colour(points[covered], normals[covered], armature, nail["plate"][covered], mask_pixels[..., 1][covered])
    image, height = skin_detail.apply(image, covered, points, normals, armature, nail, TEXTURES, {"free": NAIL_FREE, "lunula": NAIL_LUNULA})
    image[~covered] = 0
    image = fill_empty(image, covered)
    print("COVERED %.1f%% of texels, mean sRGB %s" % (covered.mean() * 100, np.round(linear_to_srgb(image[covered].mean(axis=0)) * 255).tolist()))

    out = bpy.data.images.new(NAME, size[0], size[1], alpha=False)
    rgba = np.ones((size[1], size[0], 4), np.float32)
    rgba[..., :3] = linear_to_srgb(image)
    out.pixels[:] = rgba.ravel()
    path = os.path.join(TEXTURES, NAME + ".png")
    out.filepath_raw = path
    out.file_format = "PNG"
    out.save()
    shutil.copyfile(path, os.path.join(UNITY, NAME + ".png"))

    normal, texel = skin_detail.normal_map(height, covered, points, TEXTURES, nail)
    skin_detail.save(normal, "T_Hand_Male_Young_Normal", TEXTURES, (UNITY,), colour=False)
    # Маска шейдера: красный канал — новая пластина ногтя (форма со скульптуры закрывала весь кончик пальца), зелёный прежний.
    new_mask = np.zeros((size[1], size[0], 3), np.float32)
    new_mask[..., 0] = nail["plate"]
    new_mask[..., 1] = mask_pixels[..., 1]
    skin_detail.save(new_mask, "T_Hand_Male_Mask", TEXTURES, (UNITY,), colour=False)
    print("NAILS %.2f%% of texels, texel %.2f mm, relief %.2f..%.2f mm" % (nail["plate"][covered].mean() * 100, texel * 1000, height.min() * 1000, height.max() * 1000))
    print("=== SKIN SAVED", path)


if __name__ == "__main__":
    main()
