"""Запекает детализацию скульптуры на рабочую сетку и собирает материалы кожи.
Запуск: blender art3d/hands/CardCity_Hands.blend --background --disable-autoexec --python scripts/3d/hands/bake.py

Текстуры лежат в art3d/hands/textures. Одна развёртка на обе руки, поэтому правая рука берёт те же файлы, что левая.
Оттенок кожи — множитель в материале, сама карта почти белая: новый оттенок не требует новой текстуры."""
import os
import sys

import bmesh
import bpy
import numpy as np

sys.path.insert(0, os.path.dirname(__file__))
import joints as J
import source

TEXTURES = os.path.join(source.ROOT, "art3d", "hands", "textures")
SIZE = (2048, 1024)            # Развёртка руки почти вдвое шире своей высоты.
UV_MARGIN = .012
BAKE_MARGIN = 16
CAGE, REACH = .004, .012       # Как далеко ищется скульптура от рабочей сетки.
TONES = {"Light": (.95, .76, .63), "Medium": (.8, .55, .42), "Tan": (.62, .4, .28), "Dark": (.36, .22, .16)}
# Покраснение: кожа тоньше на суставах и подушечках.
FLUSH = {"tip": (.011, 1.), "dip": (.009, .55), "pip": (.011, .6), "mcp": (.014, .4)}
NAIL_REACH = .0115             # Дальше этого от середины ногтя пластины нет.
GROOVE_LIMIT = 1.1e-4          # Вогнутость, на которой кончается ноготь: это канавка ногтевого валика.


def normalise_uv(mesh):
    """Рука занимает в развёртке тела кусок двух тайлов: растягивается на всю текстуру, у левой и правой руки развёртка совпадает."""
    data = mesh.attributes["UVMap"].data
    uv = np.zeros(len(data) * 2)
    data.foreach_get("vector", uv)
    uv = uv.reshape(-1, 2)
    low, high = uv.min(axis=0), uv.max(axis=0)
    uv = (uv - low) / (high - low) * (1 - 2 * UV_MARGIN) + UV_MARGIN
    data.foreach_set("vector", uv.ravel())


def new_image(name, colour, is_data):
    image = bpy.data.images.new(name, SIZE[0], SIZE[1], alpha=False, float_buffer=is_data)
    image.colorspace_settings.name = "Non-Color" if is_data else "sRGB"
    image.generated_color = colour
    return image


def fill_outside(image):
    """Поля между кусками развёртки становятся цветом кожи: иначе мип-карты на телефоне дали бы тёмные швы."""
    pixels = np.array(image.pixels[:]).reshape(image.size[1], image.size[0], 4)
    empty = pixels[..., :3].sum(axis=2) < .05
    pixels[empty, :3] = pixels[~empty, :3].mean(axis=0)
    image.pixels = pixels.ravel()
    image.update()


def save_image(image, name):
    path = os.path.join(TEXTURES, name + ".png")
    image.filepath_raw = path
    image.file_format = "PNG"
    image.save()
    return path


def neighbours(mesh):
    edges = np.zeros(len(mesh.edges) * 2, int)
    mesh.edges.foreach_get("vertices", edges)
    return edges.reshape(-1, 2)


def cavity_of(co, normals, edges):
    """Вогнутость в вершине: насколько соседи выше неё вдоль нормали."""
    total = np.zeros_like(co)
    count = np.zeros(len(co))
    np.add.at(total, edges[:, 0], co[edges[:, 1]])
    np.add.at(total, edges[:, 1], co[edges[:, 0]])
    np.add.at(count, edges, 1)
    offset = total / np.maximum(count, 1)[:, None] - co
    return (offset * normals).sum(axis=1)


def nail_mask(co, cavity, edges, sets, polygons):
    """Ноготь: заливка от размеченной середины пластины до канавки вокруг неё."""
    links = [[] for _ in range(len(co))]
    for a, b in edges:
        links[a].append(int(b))
        links[b].append(int(a))
    mask = np.zeros(len(co))
    for name in list(J.FINGER_SETS) + ["Thumb"]:
        nail_set = J.THUMB_SETS[3] if name == "Thumb" else J.FINGER_SETS[name][3]
        seeds = set()
        for poly, value in zip(polygons, sets):
            if value == nail_set:
                seeds.update(poly.vertices)
        centre = co[list(seeds)].mean(axis=0)
        filled = set(seeds)
        front = list(seeds)
        while front:
            ahead = []
            for v in front:
                for w in links[v]:
                    if w in filled or np.linalg.norm(co[w] - centre) > NAIL_REACH or cavity[w] > GROOVE_LIMIT:
                        continue
                    filled.add(w)
                    ahead.append(w)
            front = ahead
        mask[list(filled)] = 1.
        print("NAIL", name, len(seeds), "->", len(filled))
    return mask


def paint_detail(ob, armature):
    """Вершинный цвет скульптуры: R — покраснение, G — вогнутость, B — ноготь. Из него печётся карта кожи."""
    mesh = ob.data
    co = np.zeros(len(mesh.vertices) * 3)
    mesh.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    mesh.calc_loop_triangles()
    normals = np.zeros(len(mesh.vertices) * 3)
    mesh.vertex_normals.foreach_get("vector", normals)
    normals = normals.reshape(-1, 3)
    edges = neighbours(mesh)
    cavity = cavity_of(co, normals, edges)
    sets = np.zeros(len(mesh.polygons), int)
    mesh.attributes[".sculpt_face_set"].data.foreach_get("value", sets)
    nail = nail_mask(co, cavity, edges, sets, mesh.polygons)
    flush = np.zeros(len(co))
    bones = armature.data.bones
    for finger in J.FINGERS:
        points = {"mcp": bones[finger + "01_L"].head_local, "pip": bones[finger + "02_L"].head_local,
                  "dip": bones[finger + "03_L"].head_local, "tip": bones[finger + "03_L"].tail_local}
        for key, point in points.items():
            reach, strength = FLUSH[key]
            distance = np.linalg.norm(co - np.array(point), axis=1)
            flush = np.maximum(flush, strength * np.exp(-(distance / reach) ** 2))
    layer = mesh.color_attributes.new("Detail", "FLOAT_COLOR", "POINT")
    colours = np.ones((len(co), 4))
    colours[:, 0] = flush
    colours[:, 1] = np.clip(cavity / 2.2e-4, 0, 1)
    colours[:, 2] = nail
    layer.data.foreach_set("color", colours.ravel())


def bake_material(name):
    """Что запекается в карту кожи: почти белая основа, краснее на суставах, темнее в складках, ноготь светлее."""
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    tree = material.node_tree
    tree.nodes.clear()
    detail = tree.nodes.new("ShaderNodeAttribute")
    detail.attribute_name = "Detail"
    split = tree.nodes.new("ShaderNodeSeparateColor")
    tree.links.new(detail.outputs["Color"], split.inputs[0])
    flushed = tree.nodes.new("ShaderNodeMix")
    flushed.data_type = "RGBA"
    flushed.inputs["A"].default_value = (.93, .9, .88, 1)
    flushed.inputs["B"].default_value = (1., .74, .68, 1)
    tree.links.new(split.outputs[0], flushed.inputs["Factor"])
    creased = tree.nodes.new("ShaderNodeMix")
    creased.data_type = "RGBA"
    creased.inputs["B"].default_value = (.62, .46, .42, 1)
    tree.links.new(flushed.outputs["Result"], creased.inputs["A"])
    tree.links.new(split.outputs[1], creased.inputs["Factor"])
    nailed = tree.nodes.new("ShaderNodeMix")
    nailed.data_type = "RGBA"
    nailed.inputs["B"].default_value = (1., .86, .84, 1)
    tree.links.new(creased.outputs["Result"], nailed.inputs["A"])
    tree.links.new(split.outputs[2], nailed.inputs["Factor"])
    emit = tree.nodes.new("ShaderNodeEmission")
    tree.links.new(nailed.outputs["Result"], emit.inputs["Color"])
    output = tree.nodes.new("ShaderNodeOutputMaterial")
    tree.links.new(emit.outputs[0], output.inputs["Surface"])
    return material


def mask_material(name):
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    tree = material.node_tree
    tree.nodes.clear()
    detail = tree.nodes.new("ShaderNodeAttribute")
    detail.attribute_name = "Detail"
    split = tree.nodes.new("ShaderNodeSeparateColor")
    tree.links.new(detail.outputs["Color"], split.inputs[0])
    emit = tree.nodes.new("ShaderNodeEmission")
    tree.links.new(split.outputs[2], emit.inputs["Color"])
    output = tree.nodes.new("ShaderNodeOutputMaterial")
    tree.links.new(emit.outputs[0], output.inputs["Surface"])
    return material


def target_material(image):
    """Материал рабочей сетки на время запекания: в него пишется картинка."""
    material = bpy.data.materials.new("Bake_Target")
    material.use_nodes = True
    node = material.node_tree.nodes.new("ShaderNodeTexImage")
    node.image = image
    material.node_tree.nodes.active = node
    return material


def bake(kind, arm, sculpt, image, sculpt_material=None):
    arm.data.materials.clear()
    arm.data.materials.append(target_material(image))
    if sculpt_material:
        sculpt.data.materials.clear()
        sculpt.data.materials.append(sculpt_material)
    bpy.ops.object.select_all(action="DESELECT")
    sculpt.select_set(True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    settings = bpy.context.scene.render.bake
    settings.use_selected_to_active = True
    settings.cage_extrusion = CAGE
    settings.max_ray_distance = REACH
    settings.margin = BAKE_MARGIN
    settings.normal_space = "TANGENT"
    bpy.ops.object.bake(type=kind)


def skin_material(tone, colour, base_map, normal_map):
    material = bpy.data.materials.get("M_Skin_" + tone) or bpy.data.materials.new("M_Skin_" + tone)
    material.use_nodes = True
    material.use_fake_user = True
    tree = material.node_tree
    tree.nodes.clear()
    uv = tree.nodes.new("ShaderNodeTexImage")
    uv.image = base_map
    uv.name = "BaseMap"
    tint = tree.nodes.new("ShaderNodeMix")
    tint.data_type = "RGBA"
    tint.blend_type = "MULTIPLY"
    tint.inputs["Factor"].default_value = 1.
    tint.inputs["B"].default_value = (*srgb(colour), 1)
    tree.links.new(uv.outputs["Color"], tint.inputs["A"])
    normal_image = tree.nodes.new("ShaderNodeTexImage")
    normal_image.image = normal_map
    normal_image.name = "NormalMap"
    normal = tree.nodes.new("ShaderNodeNormalMap")
    tree.links.new(normal_image.outputs["Color"], normal.inputs["Color"])
    shader = tree.nodes.new("ShaderNodeBsdfPrincipled")
    shader.inputs["Roughness"].default_value = .55
    shader.inputs["Subsurface Weight"].default_value = .25
    shader.inputs["Subsurface Radius"].default_value = (.9, .32, .18)
    shader.inputs["Subsurface Scale"].default_value = .004
    tree.links.new(tint.outputs["Result"], shader.inputs["Base Color"])
    tree.links.new(normal.outputs["Normal"], shader.inputs["Normal"])
    output = tree.nodes.new("ShaderNodeOutputMaterial")
    tree.links.new(shader.outputs[0], output.inputs["Surface"])
    return material


def srgb(colour):
    return tuple(c / 12.92 if c < .04045 else ((c + .055) / 1.055) ** 2.4 for c in colour)


def main():
    os.makedirs(TEXTURES, exist_ok=True)
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    prefs = bpy.context.preferences.addons["cycles"].preferences
    prefs.compute_device_type = "OPTIX"
    prefs.get_devices()
    for device in prefs.devices:
        device.use = device.type == "OPTIX"
    sc.cycles.device = "GPU"
    sc.cycles.samples = 8
    sc.cycles.use_denoising = False
    armature = bpy.data.objects["Rig_Hands"]
    armature.data.pose_position = "REST"
    sculpt_group = bpy.data.collections["Sculpt"]
    sculpt_group.hide_render = False
    female_group = bpy.data.collections["Hands_Female"]
    female_group.hide_render = False
    for ob in bpy.data.objects:
        if ob.name.startswith("Arm_"):
            normalise_uv(ob.data)
    bpy.context.view_layer.update()

    shared_map = {}
    for gender in ("Male", "Female"):
        arm = bpy.data.objects["Arm_%s_L" % gender]
        sculpt = bpy.data.objects["Sculpt_%s_L" % gender]
        paint_detail(sculpt, armature)
        normal = new_image("T_Hand_%s_Normal" % gender, (.5, .5, 1, 1), True)
        bake("NORMAL", arm, sculpt, normal)
        save_image(normal, normal.name)
        base = new_image("T_Hand_%s_BaseColor" % gender, (.9, .86, .84, 1), False)
        bake("EMIT", arm, sculpt, base, bake_material("Bake_Skin_" + gender))
        fill_outside(base)
        save_image(base, base.name)
        mask = new_image("T_Hand_%s_Mask" % gender, (0, 0, 0, 1), True)
        bake("EMIT", arm, sculpt, mask, mask_material("Bake_Mask_" + gender))
        save_image(mask, mask.name)
        shared_map[gender] = (base, normal, mask)
        print("BAKED", gender)

    # Оттенки есть у обоих вариантов: материал отличается только множителем.
    for gender in ("Male", "Female"):
        base, normal, _ = shared_map[gender]
        suffix = "" if gender == "Male" else "_Female"
        made = {tone: skin_material(tone + suffix, colour, base, normal) for tone, colour in TONES.items()}
        for side in "LR":
            mesh = bpy.data.objects["Arm_%s_%s" % (gender, side)].data
            mesh.materials.clear()
            mesh.materials.append(made["Medium"])

    armature.data.pose_position = "POSE"
    sculpt_group.hide_render = True
    female_group.hide_render = True
    for image in bpy.data.images:
        if image.name.startswith("T_Hand"):
            image.filepath = os.path.join(TEXTURES, image.name + ".png")
            image.source = "FILE"
            image.reload()
    bpy.ops.wm.save_mainfile()
    print("=== BAKE DONE")


main()
