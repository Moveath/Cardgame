"""Кадр с места игрока: руки, веер, стол, свет как в Unity. Проверка позы партии до установки в Unity.
Поза Pose_InGame пересобирается из in_game.py, файл .blend не сохраняется. Кадры — screens/3d-hands/<имя>.png и <имя>-hand.png (крупно левая рука).
Запуск: blender art3d/hands/CardCity_Hands.blend --background --disable-autoexec --python scripts/3d/hands/seat_view.py -- [имя]"""
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(__file__))
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
import in_game
import source
import table_model

CARDS = os.path.join(source.ROOT, "unity", "CardGameClient", "Assets", "Resources", "Cards")
ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
NAME = ARGS[0] if ARGS else "seat"
# Второй аргумент — формат экрана: 16x9 (ноутбук) или 20x9 (телефон, по умолчанию).
WIDE = not (len(ARGS) > 1 and ARGS[1] == "16x9")
OUT = os.path.join(source.ROOT, "screens", "3d-hands", NAME + ".png")
CARD_H, ASPECT = .088, .714
FAN = ("10_clubs", "q_diamonds", "a_hearts", "7_spades", "10_spades", "j_spades")
# Главный свет Unity (CardTable3DBootstrap): Euler(50, 35, 0). В осях Blender (Y вперёд, Z вверх) он светит так:
SUN = Vector((math.sin(math.radians(35)) * math.cos(math.radians(50)), math.cos(math.radians(35)) * math.cos(math.radians(50)), -math.sin(math.radians(50))))


def field_of_view(aspect):
    """Как Table3DLayout.FieldOfView: на 16:9 — 62° по вертикали, на 20:9 — 52°, по горизонтали в обоих около 94°."""
    t = min(1., max(0., (aspect - 1.78) / (2.22 - 1.78)))
    return 62 + (52 - 62) * t


SKINS = {"young": ("T_Hand_Male_Young_BaseColor", "T_Hand_Male_Young_Normal"), "photo": ("T_Hand_Male_Photo_BaseColor", "T_Hand_Male_Photo_Normal"),
         # Сглаженная молодая кожа (skin_smooth.py), с 2026-11-16 она в игре.
         "smooth": ("T_Hand_Male_Smooth_BaseColor", "T_Hand_Male_Smooth_Normal")}


def game_skin():
    """Кожа как в игре вместо почти белой карты с оттенком: цвет даёт сама текстура. SKIN=young — молодая кожа (skin_young.py,
    она в игре), SKIN=photo — прежняя с фото (skin_photo.py). Без переменной кожа из файла .blend; сам файл не меняется."""
    kind = os.environ.get("SKIN") or ("photo" if os.environ.get("SKIN_PHOTO") == "1" else None)
    if kind is None:
        return
    textures = os.path.join(source.ROOT, "art3d", "hands", "textures")
    base_name, normal_name = SKINS[kind]
    base = bpy.data.images.load(os.path.join(textures, base_name + ".png"), check_existing=True)
    normal = bpy.data.images.load(os.path.join(textures, normal_name + ".png"), check_existing=True)
    normal.colorspace_settings.name = "Non-Color"
    for name in ("Arm_Male_L", "Arm_Male_R"):
        for material in bpy.data.objects[name].data.materials:
            nodes = material.node_tree.nodes
            if "BaseMap" not in nodes:
                continue
            nodes["BaseMap"].image = base
            nodes["NormalMap"].image = normal
            for node in nodes:
                if node.type == "MIX" and node.data_type == "RGBA":
                    node.inputs["B"].default_value = (1, 1, 1, 1)


def card_material(key):
    material = bpy.data.materials.get(key) or bpy.data.materials.new(key)
    material.use_nodes = True
    tree = material.node_tree
    texture = tree.nodes.new("ShaderNodeTexImage")
    texture.image = bpy.data.images.load(os.path.join(CARDS, key + ".png"), check_existing=True)
    principled = next(n for n in tree.nodes if n.type == "BSDF_PRINCIPLED")
    tree.links.new(texture.outputs["Color"], principled.inputs["Base Color"])
    tree.links.new(texture.outputs["Alpha"], principled.inputs["Alpha"])
    principled.inputs["Roughness"].default_value = .45
    return material


def card(key, position, x, y, scale):
    w, h = CARD_H * ASPECT * scale / 2, CARD_H * scale / 2
    corners = [position - x * w - y * h, position + x * w - y * h, position + x * w + y * h, position - x * w + y * h]
    mesh = bpy.data.meshes.new(key)
    mesh.from_pydata([tuple(c) for c in corners], [], [(0, 1, 2, 3)])
    layer = mesh.uv_layers.new(name="UV")
    uvs = [(0, 0), (1, 0), (1, 1), (0, 1)]
    for i, loop in enumerate(mesh.loops):
        layer.uv[i].vector = uvs[loop.vertex_index]
    mesh.materials.append(card_material(key))
    ob = bpy.data.objects.new(key, mesh)
    bpy.context.scene.collection.objects.link(ob)
    # Карта с толщиной: иначе свет проходит между картами веера и тени не видно.
    solid = ob.modifiers.new("thickness", "SOLIDIFY")
    solid.thickness = .0006
    return ob


def rebuild_pose(armature):
    old = bpy.data.actions.get(in_game.ACTION)
    if old is not None:
        bpy.data.actions.remove(old)
    armature.animation_data_create()
    in_game.store(armature)
    armature.animation_data.action = bpy.data.actions[in_game.ACTION]


def render(sc, path, border=None):
    sc.render.use_border = border is not None
    sc.render.use_crop_to_border = border is not None
    if border:
        sc.render.border_min_x, sc.render.border_max_x, sc.render.border_min_y, sc.render.border_max_y = border
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)


def stage(rebuild=True):
    """Сцена с места игрока: свет, стол, камера, руки в позе партии и веер. Возвращает сцену, скелет и переход из осей модели в мир."""
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.samples = 48
    sc.cycles.use_denoising = True
    # SEAT_CPU=1 — считать на процессоре: видеокарте (OptiX) нужно ещё 1–2 ГБ оперативной памяти, а при открытом Unity её может не быть.
    if os.environ.get("SEAT_CPU") == "1":
        sc.cycles.device = "CPU"
        sc.cycles.denoiser = "OPENIMAGEDENOISE"
    else:
        prefs = bpy.context.preferences.addons["cycles"].preferences
        prefs.compute_device_type = "OPTIX"
        prefs.get_devices()
        for device in prefs.devices:
            device.use = device.type == "OPTIX"
        sc.cycles.device = "GPU"
        sc.cycles.denoiser = "OPTIX"
    sc.render.resolution_x, sc.render.resolution_y = (1822, 820) if WIDE else (1600, 900)
    sc.view_settings.view_transform = "AgX"
    world = bpy.data.worlds.new("seat")
    world.use_nodes = False
    world.color = (.05, .042, .036)
    sc.world = world
    sun = bpy.data.objects.new("seat sun", bpy.data.lights.new("seat sun", "SUN"))
    sun.data.energy, sun.data.angle, sun.data.color = 3.2, math.radians(3), (1, .92, .82)
    sun.rotation_euler = SUN.to_track_quat("-Z", "Y").to_euler()
    sc.collection.objects.link(sun)
    lamp = bpy.data.objects.new("lamp pool", bpy.data.lights.new("lamp pool", "POINT"))
    lamp.data.energy, lamp.data.color, lamp.data.shadow_soft_size = 60, (1, .8, .55), .15
    lamp.location = (0, .1, table_model.TABLE_Z + .95)
    sc.collection.objects.link(lamp)
    table_model.build_table()
    game_skin()

    data = bpy.data.cameras.new("eye")
    data.lens_unit = "FOV"
    data.sensor_fit = "VERTICAL"
    data.angle_y = math.radians(field_of_view(sc.render.resolution_x / sc.render.resolution_y))
    data.clip_start = .05
    camera = bpy.data.objects.new("eye", data)
    sc.collection.objects.link(camera)
    sc.camera = camera
    camera.matrix_world = Matrix.Translation((0, -1.01, 1.30)) @ Matrix.Rotation(math.radians(90) - in_game.PITCH, 4, "X")
    # Модель задана от глаз (X вправо, Y вперёд, Z вверх), камера Blender смотрит в -Z.
    to_eye = camera.matrix_world @ Matrix.Rotation(math.radians(-90), 4, "X")

    armature = bpy.data.objects["Rig_Hands"]
    if rebuild:
        rebuild_pose(armature)
    else:
        armature.animation_data.action = bpy.data.actions[in_game.ACTION]
    sc.frame_set(1)
    armature.matrix_world = to_eye
    for gender in ("Female",):
        bpy.data.collections["Hands_" + gender].hide_render = True
    bpy.data.objects["Test_Card"].hide_render = True

    n = len(FAN)
    step = min(in_game.FAN_STEP, in_game.FAN_LIMIT / (n - 1))
    pivot, fan_right, fan_up, fan_deep = in_game.fan_axes()
    for i, key in enumerate(FAN):
        spin = math.radians((n - 1) / 2 * step - i * step)
        up = fan_up * math.cos(spin) - fan_right * math.sin(spin)
        right = fan_right * math.cos(spin) + fan_up * math.sin(spin)
        position = pivot + up * in_game.FAN_RADIUS + fan_deep * in_game.card_depth(i, len(FAN))
        ob = card(key, position, right, up, in_game.CARD_SCALE)
        ob.matrix_world = to_eye
    z, x, y = table_model.TABLE_Z + .012, Vector((1, 0, 0)), Vector((0, 1, 0))
    big = 1.4
    for key, at in (("back", (-.66, .3)), ("back", (.66, .3)), ("k_clubs", (-.24, .03)), ("6_clubs", (.0, .03)), ("8_clubs", (.24, .03))):
        card(key, Vector((*at, z)), x, y, big)
    return sc, armature, to_eye


def main():
    sc, armature, to_eye = stage()
    render(sc, OUT)
    sc.render.resolution_percentage = 200
    sc.cycles.samples = 64
    # Крупно рука с веером: левая половина кадра, а при веере в правой — правая.
    render(sc, OUT.replace(".png", "-hand.png"), (.44, .94, 0, .9) if in_game.MIRROR else (.06, .56, 0, .9))
    pivot, fan_right, fan_up, fan_deep = in_game.fan_axes()
    outward = -fan_right if not in_game.MIRROR else fan_right

    # Сбоку снаружи, вдоль веера: видно, что лежит перед картами, а что за ними, и не проходит ли палец сквозь карту.
    side = bpy.data.objects.new("side", bpy.data.cameras.new("side"))
    side.data.lens = 50
    sc.collection.objects.link(side)
    look_at = to_eye @ (pivot + fan_up * .02)
    side.location = to_eye @ (pivot + outward * .3 + fan_up * .05 - fan_deep * .06)
    side.rotation_euler = (look_at - side.location).to_track_quat("-Z", "Y").to_euler()
    sc.camera = side
    sc.render.resolution_percentage = 100
    sc.render.resolution_x, sc.render.resolution_y = 900, 700
    render(sc, OUT.replace(".png", "-side.png"))

    # Сзади, со стороны соперника: где лежат пальцы на рубашках (сравнить с фото руки автора 03 и 07).
    look_at = to_eye @ (pivot + fan_up * .01)
    side.location = to_eye @ (pivot + fan_deep * .32 + fan_up * .03 + fan_right * .05)
    side.rotation_euler = (look_at - side.location).to_track_quat("-Z", "Y").to_euler()
    render(sc, OUT.replace(".png", "-back.png"))
    print("=== SEAT DONE", OUT)


if __name__ == "__main__":
    main()
