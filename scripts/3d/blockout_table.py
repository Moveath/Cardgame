"""Черновой макет 3D-стола: размеры в метрах, вид с места игрока. Проверяет высоту взгляда и размер карт до всякого арта.
Запуск: blender --background --factory-startup --python scripts/3d/blockout_table.py
Кадры кладёт в screens/3d-blockout. В проект Unity ничего не пишет; люди и зал здесь — заглушки."""
import glob
import math
import os
import random
import sys

import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(__file__))
import table_model

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
RES = os.path.join(ROOT, "unity", "CardGameClient", "Assets", "Resources")
OUT = os.path.join(ROOT, "screens", "3d-blockout")

TABLE_A, TABLE_B, TABLE_Z = table_model.TABLE_A, table_model.TABLE_B, table_model.TABLE_Z
SEAT_GAP = .32                              # От края стола до середины сидящего.
EYE_Z, EYE_GAP = 1.30, .36                  # Глаза сидящего и их отступ от края стола.
CARD_W, CARD_H = .063, .088                 # Настоящая игральная карта.
SEATS = (108, 144, 180, 216, 252)           # Соперники по дальней части овала, 0° — своё место: все в кадре без поворота головы.
SKIN = ((.78, .56, .42), (.70, .48, .36), (.82, .62, .50), (.60, .42, .31), (.76, .55, .44))
HAIR = ((.03, .025, .02), (.06, .04, .03), (.02, .02, .02), (.10, .07, .05), (.04, .03, .03))


def material(name, color=(.5, .5, .5), rough=.6, metal=0.):
    m = bpy.data.materials.get(name)
    if m:
        return m
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    p = m.node_tree.nodes["Principled BSDF"]
    p.inputs["Base Color"].default_value = (*color, 1)
    p.inputs["Roughness"].default_value = rough
    p.inputs["Metallic"].default_value = metal
    return m


def image_material(path, emit=0.):
    name = os.path.basename(path) + ("-emit" if emit else "")
    m = bpy.data.materials.get(name)
    if m:
        return m
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nodes, links = m.node_tree.nodes, m.node_tree.links
    tex = nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(path, check_existing=True)
    tex.extension = "EXTEND"
    p = nodes["Principled BSDF"]
    if emit:
        out = nodes["Material Output"]
        e = nodes.new("ShaderNodeEmission")
        e.inputs["Strength"].default_value = emit
        links.new(tex.outputs["Color"], e.inputs["Color"])
        links.new(e.outputs[0], out.inputs["Surface"])
    else:
        links.new(tex.outputs["Color"], p.inputs["Base Color"])
        links.new(tex.outputs["Alpha"], p.inputs["Alpha"])
        p.inputs["Roughness"].default_value = .42
    return m


def mesh_object(name, verts, faces, uvs, mat, group=None):
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    me.update()
    layer = me.uv_layers.new(name="UV")
    flat = [c for loop in me.loops for c in uvs[loop.vertex_index]]
    try:
        layer.uv.foreach_set("vector", flat)
    except (AttributeError, TypeError):
        layer.data.foreach_set("uv", flat)
    me.materials.append(mat)
    ob = bpy.data.objects.new(name, me)
    (group or bpy.context.scene.collection).objects.link(ob)
    return ob


def card(tex, matrix, group, scale=1., parent=None):
    w, h = CARD_W * scale / 2, CARD_H * scale / 2
    ob = mesh_object("card", [(-w, -h, 0), (w, -h, 0), (w, h, 0), (-w, h, 0)], [(0, 1, 2, 3)],
                     [(0, 0), (1, 0), (1, 1), (0, 1)], image_material(tex), group)
    ob.parent = parent
    ob.matrix_parent_inverse = Matrix.Identity(4)
    ob.matrix_basis = matrix
    return ob


def primitive(kind, location, scale, mat, rotation=None, **options):
    getattr(bpy.ops.mesh, kind)(location=location, **options)
    ob = bpy.context.object
    ob.scale = scale
    if rotation is not None:
        ob.rotation_mode = "QUATERNION"
        ob.rotation_quaternion = rotation
    ob.data.materials.append(mat)
    if kind != "primitive_cube_add":
        bpy.ops.object.shade_smooth()
    return ob


def limb(a, b, radius, mat):
    a, b = Vector(a), Vector(b)
    d = b - a
    primitive("primitive_cylinder_add", (a + b) / 2, (radius, radius, d.length / 2), mat,
              d.to_track_quat("Z", "Y"), vertices=12)


def ball(at, scale, mat, rotation=None):
    return primitive("primitive_uv_sphere_add", at, scale, mat, rotation, segments=24, ring_count=16)


def build_person(angle, index):
    t = math.radians(angle)
    at = Vector(((TABLE_A + SEAT_GAP) * math.sin(t), -(TABLE_B + SEAT_GAP) * math.cos(t), 0))
    fwd = (-at).normalized()
    right = Vector((0, 0, 1)).cross(fwd).normalized()
    turn = Vector((0, 1, 0)).rotation_difference(fwd)
    sweater = material("sweater", (.05, .16, .19), .8)
    skin = material("skin %d" % index, SKIN[index], .55)
    chair = table_model.build_chair("Chair %d" % index)
    chair.location = at - fwd * .04
    chair.rotation_euler = (0, 0, math.atan2(fwd.y, fwd.x) - math.pi / 2)
    ball(at + Vector((0, 0, .9)), (.2, .13, .28), sweater, turn)
    limb(at + Vector((0, 0, 1.08)), at + Vector((0, 0, 1.16)), .05, skin)
    head = at + Vector((0, 0, 1.24))
    ball(head, (.095, .105, .118), skin, turn)
    ball(head + Vector((0, 0, .03)) - fwd * .018, (.1, .104, .105), material("hair %d" % index, HAIR[index], .7), turn)
    hands = at + fwd * .3 + Vector((0, 0, .9))
    for side in (-1, 1):
        shoulder = at + right * (.2 * side) + Vector((0, 0, 1.03))
        elbow = at + right * (.25 * side) + fwd * .1 + Vector((0, 0, .84))
        wrist = hands + right * (.06 * side) - fwd * .03
        limb(shoulder, elbow, .05, sweater)
        limb(elbow, wrist, .042, sweater)
        ball(wrist + fwd * .03, (.042, .042, .05), skin)
    # Веер рубашек лицевой стороной к хозяину: остальным за столом видна рубашка.
    x, y, z = Vector((0, 0, 1)).cross(fwd).normalized(), Vector((0, 0, 1)), fwd
    basis = Matrix((x, y, z)).transposed().to_4x4()
    back = os.path.join(RES, "Cards/back.png")
    for i in range(5):
        spin = Matrix.Rotation(math.radians((2 - i) * 9), 4, "Z")
        place = Matrix.Translation(hands + fwd * (.03 + i * .001)) @ basis @ spin @ Matrix.Translation((0, .05, 0))
        card(back, place, bpy.context.scene.collection, 1.1)


def table_cards(pool, scale):
    group = bpy.data.collections.new("table cards")
    bpy.context.scene.collection.objects  # Коллекция сцены живёт дольше кадра.
    bpy.context.scene.collection.children.link(group)
    z = TABLE_Z + .002
    tilt = lambda deg: Matrix.Rotation(math.radians(deg), 4, "Z")
    faces = iter(pool)
    step = .17 * max(1, scale)
    for slot, x in enumerate((-step, 0, step)):
        card(next(faces), Matrix.Translation((x - .012, .05, z)) @ tilt(4 - slot * 3), group, scale)
        card(next(faces), Matrix.Translation((x + .016, .01, z + .001)) @ tilt(-7 + slot * 4), group, scale)
    back = os.path.join(RES, "Cards/back.png")
    # Колода слева, козырь поперёк под ней; бита справа.
    deck = Vector((-.62, -.02, 0))
    card(next(faces), Matrix.Translation(deck + Vector((.05 * scale, -.012, z))) @ tilt(-84), group, scale)
    for i in range(14):
        card(back, Matrix.Translation(deck + Vector((0, 0, z + .002 + i * .0012))) @ tilt(2), group, scale)
    rng = random.Random(11)
    for i in range(10):
        at = Vector((.62 + rng.uniform(-.03, .03), rng.uniform(-.03, .03), z + i * .0007))
        card(back, Matrix.Translation(at) @ tilt(rng.uniform(-40, 40)), group, scale)
    return group


def own_hand(camera, pool):
    """Рука как в игре: держится за камеру, поэтому при повороте головы остаётся на месте."""
    group = bpy.context.scene.collection
    pivot = Matrix.Translation((-.125, -.185, -.37)) @ Matrix.Rotation(math.radians(14), 4, "X") @ Matrix.Rotation(math.radians(-7), 4, "Z")
    faces = list(pool)
    for i, tex in enumerate(faces):
        spin = Matrix.Rotation(math.radians((len(faces) - 1) / 2 * 7 - i * 7), 4, "Z")
        card(tex, pivot @ spin @ Matrix.Translation((0, .085, i * .0009)), group, 1.1, camera)
    sleeve, skin = material("sweater", (.05, .16, .19), .8), material("own skin", SKIN[0], .55)
    parts = []
    for a, b, r in (((-.42, -.42, -.2), (-.185, -.225, -.37), .036), ((.42, -.42, -.2), (.2, -.2, -.5), .034)):
        limb(a, b, r, sleeve)
        parts.append(bpy.context.object)
    parts.append(ball((-.145, -.2, -.362), (.04, .05, .022), skin))
    parts.append(ball((.175, -.18, -.52), (.036, .045, .024), skin))
    for ob in parts:
        ob.parent = camera
        ob.matrix_parent_inverse = Matrix.Identity(4)


def build_room():
    hall = os.path.join(RES, "UI/Hub/club-hall.png")
    n, radius, height = 96, 5.2, 6.7
    verts, uvs, faces = [], [], []
    for i in range(n + 1):
        t = i / n
        a = math.pi * (1 - t)  # Половина круга перед игроком.
        for v in (0, 1):
            verts.append((radius * math.cos(a), radius * math.sin(a) - .6, -2.1 + v * height))
            uvs.append((t, v))
    for i in range(n):
        faces.append((2 * i, 2 * i + 2, 2 * i + 3, 2 * i + 1))
    mesh_object("hall backdrop", verts, faces, uvs, image_material(hall, .55))
    primitive("primitive_plane_add", (0, 0, 0), (9, 9, 1), material("carpet", (.13, .02, .03), .9))
    behind = material("dark", (.02, .012, .01), .8)
    primitive("primitive_cube_add", (0, -5.8, 2), (9, .1, 4), behind)
    primitive("primitive_cone_add", (0, 0, 1.92), (.32, .32, .1), material("lamp", (.03, .2, .1), .4), vertices=32)
    lamp = bpy.data.lights.new("lamp", "SPOT")
    lamp.energy, lamp.spot_size, lamp.spot_blend, lamp.color = 190, math.radians(125), .6, (1, .84, .62)
    ob = bpy.data.objects.new("lamp", lamp)
    ob.location = (0, 0, 1.86)
    bpy.context.scene.collection.objects.link(ob)
    fill = bpy.data.lights.new("fill", "POINT")
    fill.energy, fill.color = 42, (1, .78, .55)
    ob = bpy.data.objects.new("fill", fill)
    ob.location = (0, -1.3, 1.9)
    bpy.context.scene.collection.objects.link(ob)


def setup_render():
    sc = bpy.context.scene
    prefs = bpy.context.preferences.addons["cycles"].preferences
    prefs.compute_device_type = "OPTIX"
    prefs.get_devices()
    for device in prefs.devices:
        device.use = device.type == "OPTIX"
    sc.render.engine = "CYCLES"
    sc.cycles.device = "GPU"
    sc.cycles.samples = 64
    sc.cycles.use_denoising = True
    sc.render.resolution_x, sc.render.resolution_y = 1822, 820
    sc.view_settings.view_transform = "Standard"
    sc.world = bpy.data.worlds.new("world")
    sc.world.use_nodes = True
    sc.world.node_tree.nodes["Background"].inputs[0].default_value = (.012, .009, .007, 1)


def main():
    os.makedirs(OUT, exist_ok=True)
    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob)
    setup_render()
    faces = sorted(p for p in glob.glob(os.path.join(RES, "Cards", "*.png"))
                   if any(s in os.path.basename(p) for s in ("clubs", "diamonds", "hearts", "spades")))
    random.Random(7).shuffle(faces)
    table_model.build_table()
    build_room()
    for i, angle in enumerate(SEATS):
        build_person(angle, i)
    data = bpy.data.cameras.new("eye")
    data.lens_unit = "FOV"
    data.sensor_fit = "VERTICAL"
    data.angle_y = math.radians(48)
    camera = bpy.data.objects.new("eye", data)
    bpy.context.scene.collection.objects.link(camera)
    bpy.context.scene.camera = camera
    own_hand(camera, faces[:6])
    eye = (0, -(TABLE_B + EYE_GAP), EYE_Z)
    shots = (("seat", 1.4, eye, 15, 0), ("look-table", 1.4, eye, 30, 0), ("lean-in", 1.4, (0, eye[1] + .22, EYE_Z - .08), 42, 0))
    for name, scale, position, pitch, yaw in shots:
        group = table_cards(faces[6:], scale)
        camera.location = position
        camera.rotation_euler = (math.radians(90 - pitch), 0, math.radians(yaw))
        bpy.context.scene.render.filepath = os.path.join(OUT, name + ".png")
        bpy.ops.render.render(write_still=True)
        for ob in list(group.objects):
            bpy.data.objects.remove(ob)
        bpy.data.collections.remove(group)
    print("BLOCKOUT DONE")


main()
