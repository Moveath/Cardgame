"""Готовит 2K URP PBR-карты базового мужского тела из Tripo 4K, раскладывает ригнутые Mixamo FBX
в Unity (unity/CardGameClient и screens/qa/shop-project) и рендерит контрольный кадр посадки на стул chair.fbx.
"""
import math
import os
import shutil

import bpy
import numpy as np
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
RAW_FBM = os.path.join(
    ROOT, "3д обьекты", "персонажи", "тело", "tripo_raw",
    "tripo_convert_699299a2-cc6a-4f9a-966d-cc209f6c690b.fbm",
)
MIXAMO_DIR = os.path.join(ROOT, "3д обьекты", "персонажи", "тело", "mixamo")
OUT_PREVIEW_DIR = os.path.join(ROOT, "3д обьекты", "персонажи", "тело")
CHAIR_BLEND = os.path.join(ROOT, "art3d", "props", "chair.blend")
HANDS_TEX = os.path.join(
    ROOT, "unity", "CardGameClient", "Assets", "Resources", "ThreeD", "Hands", "TripoHands_BaseColor.jpg",
)

UNITY_TARGETS = [
    os.path.join(ROOT, "unity", "CardGameClient", "Assets", "Resources", "ThreeD", "Characters"),
    os.path.join(ROOT, "screens", "qa", "shop-project", "Assets", "Resources", "ThreeD", "Characters"),
]

TEX_SIZE = 2048


def find_fbm(suffix):
    for fn in os.listdir(RAW_FBM):
        if fn.lower().endswith(suffix.lower()):
            return os.path.join(RAW_FBM, fn)
    raise FileNotFoundError(f"Missing {suffix} in {RAW_FBM}")


def load_resized_rgba(path, size, colorspace="sRGB"):
    img = bpy.data.images.load(path)
    img.colorspace_settings.name = colorspace
    if img.size[0] != size or img.size[1] != size:
        img.scale(size, size)
    buf = np.empty(size * size * 4, dtype=np.float32)
    img.pixels.foreach_get(buf)
    arr = buf.reshape((size, size, 4)).copy()
    bpy.data.images.remove(img)
    return arr


def save_png(arr, path, colorspace="sRGB"):
    h, w, _ = arr.shape
    img = bpy.data.images.new(os.path.basename(path), width=w, height=h, alpha=True)
    img.colorspace_settings.name = colorspace
    img.pixels.foreach_set(np.clip(arr, 0.0, 1.0).astype(np.float32).ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


def main():
    color_src = find_fbm("Color.jpg")
    normal_src = find_fbm("Normal.jpg")
    rough_src = find_fbm("_roughness.jpg")
    metal_src = find_fbm("_metallic.jpg")

    print("=== Packing 2K URP PBR maps from 4K Tripo textures...")
    col = load_resized_rgba(color_src, TEX_SIZE, "sRGB")
    col[:, :, 3] = 1.0
    nrm = load_resized_rgba(normal_src, TEX_SIZE, "Non-Color")
    nrm[:, :, 3] = 1.0
    rgh = load_resized_rgba(rough_src, TEX_SIZE, "Non-Color")
    mtl = load_resized_rgba(metal_src, TEX_SIZE, "Non-Color")

    # URP Lit MetallicGlossMap: R = Metallic, A = Smoothness = (1 - Roughness).
    # Мягко приглушаем максимум глянца (x0.72), чтобы вязаный свитер и джинсы не блестели пластиком в тёмном зале.
    mg = np.zeros((TEX_SIZE, TEX_SIZE, 4), dtype=np.float32)
    mg[:, :, 0] = np.clip(mtl[:, :, 0] * 0.5, 0.0, 1.0)
    mg[:, :, 3] = np.clip((1.0 - rgh[:, :, 0]) * 0.72, 0.04, 0.65)

    for target_dir in UNITY_TARGETS:
        os.makedirs(target_dir, exist_ok=True)
        save_png(col, os.path.join(target_dir, "BodyMale_BaseColor.png"), "sRGB")
        save_png(nrm, os.path.join(target_dir, "BodyMale_Normal.png"), "Non-Color")
        save_png(mg, os.path.join(target_dir, "BodyMale_MetallicGloss.png"), "Non-Color")

        fbx_map = {
            "body_mixamo_clean.fbx": "BaseMale.fbx",
            "body_mixamo_clean_Sitting_Idle.fbx": "BaseMale_SitIdle.fbx",
            "body_mixamo_clean_Sitting_Talking.fbx": "BaseMale_SitTalk.fbx",
            "body_mixamo_clean_Sitting_Disbelief.fbx": "BaseMale_SitDisbelief.fbx",
        }
        for src_name, dst_name in fbx_map.items():
            shutil.copy2(os.path.join(MIXAMO_DIR, src_name), os.path.join(target_dir, dst_name))
        print(f"=== Synced Characters assets to {target_dir}")

    # Рендерим контрольное превью: наш ригнутый персонаж Mixamo в позе Sitting_Idle на нашем бархатном стуле chair.blend!
    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    for m in list(bpy.data.meshes):
        bpy.data.meshes.remove(m)
    for mat in list(bpy.data.materials):
        bpy.data.materials.remove(mat)

    bpy.ops.import_scene.fbx(filepath=os.path.join(MIXAMO_DIR, "body_mixamo_clean.fbx"))
    arm_base = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    mesh_base = next(o for o in bpy.data.objects if o.type == "MESH")

    bpy.ops.import_scene.fbx(filepath=os.path.join(MIXAMO_DIR, "body_mixamo_clean_Sitting_Idle.fbx"))
    arm_idle = [o for o in bpy.data.objects if o.type == "ARMATURE" and o != arm_base][0]
    act = arm_idle.animation_data.action
    arm_base.animation_data_create()
    arm_base.animation_data.action = act
    bpy.data.objects.remove(arm_idle, do_unlink=True)

    # Применяем реальные PBR-текстуры на оба слота (свитер+джинсы+ботинки и 5-палые кисти)
    outfit_mat = bpy.data.materials.new("OutfitPreview")
    outfit_mat.use_nodes = True
    nt = outfit_mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    tex_col = nt.nodes.new("ShaderNodeTexImage")
    tex_col.image = bpy.data.images.load(os.path.join(UNITY_TARGETS[0], "BodyMale_BaseColor.png"))
    nt.links.new(tex_col.outputs["Color"], bsdf.inputs["Base Color"])
    tex_nrm = nt.nodes.new("ShaderNodeTexImage")
    tex_nrm.image = bpy.data.images.load(os.path.join(UNITY_TARGETS[0], "BodyMale_Normal.png"))
    tex_nrm.image.colorspace_settings.name = "Non-Color"
    nrm_node = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(tex_nrm.outputs["Color"], nrm_node.inputs["Color"])
    nt.links.new(nrm_node.outputs["Normal"], bsdf.inputs["Normal"])
    bsdf.inputs["Roughness"].default_value = 0.82

    hands_mat = bpy.data.materials.new("HandsPreview")
    hands_mat.use_nodes = True
    hnt = hands_mat.node_tree
    hbsdf = next(n for n in hnt.nodes if n.type == "BSDF_PRINCIPLED")
    htex = hnt.nodes.new("ShaderNodeTexImage")
    htex.image = bpy.data.images.load(HANDS_TEX)
    hnt.links.new(htex.outputs["Color"], hbsdf.inputs["Base Color"])
    hbsdf.inputs["Roughness"].default_value = 0.65

    mesh_base.data.materials[0] = outfit_mat
    mesh_base.data.materials[1] = hands_mat

    # Подгружаем наш бархатный стул из art3d/props/chair.blend (лицо стула +Y в Blender -> разворачиваем на -Y как персонаж!)
    with bpy.data.libraries.load(CHAIR_BLEND, link=False) as (df, dt):
        dt.objects = [n for n in df.objects if "Chair" in n or "chair" in n]
    for ob in bpy.data.objects:
        if ob not in (arm_base, mesh_base) and ob.type == "MESH":
            bpy.context.scene.collection.objects.link(ob)
            ob.rotation_mode = "XYZ"
            ob.rotation_euler = (0.0, 0.0, math.pi)
            ob.location = (0.0, 0.04, 0.0)

    # Сажаем ригнутого персонажа на подушку стула (Z=+0.095 м, Y=+0.04 м к спинке)
    arm_base.location = (0.0, 0.04, 0.095)
    bpy.context.scene.frame_set(15)
    bpy.context.view_layer.update()

    # Cycles GPU render
    sc = bpy.context.scene
    prefs = bpy.context.preferences.addons["cycles"].preferences
    prefs.compute_device_type = "OPTIX"
    prefs.get_devices()
    for d in prefs.devices:
        d.use = d.type == "OPTIX"
    sc.render.engine = "CYCLES"
    sc.cycles.device = "GPU"
    sc.cycles.samples = 32
    sc.cycles.use_denoising = True
    sc.view_settings.view_transform = "AgX"

    sc.world = bpy.data.worlds.new("w")
    sc.world.use_nodes = True
    bg = next(n for n in sc.world.node_tree.nodes if n.type == "BACKGROUND")
    bg.inputs[0].default_value = (0.14, 0.12, 0.11, 1.0)
    bg.inputs[1].default_value = 0.8

    def add_area(name, loc, rot_deg, energy, sz):
        ld = bpy.data.lights.new(name, "AREA")
        ld.energy = energy
        ld.size = sz
        lo = bpy.data.objects.new(name, ld)
        lo.location = loc
        lo.rotation_euler = tuple(math.radians(a) for a in rot_deg)
        sc.collection.objects.link(lo)

    add_area("Key", (1.6, -2.4, 2.4), (50, 0, 32), 450, 2.0)
    add_area("Fill", (-2.0, -1.6, 1.8), (65, 0, -45), 180, 2.2)
    add_area("Rim", (0.2, 2.2, 2.0), (55, 0, 175), 240, 2.0)

    cam_data = bpy.data.cameras.new("C")
    cam_data.lens = 50
    cam = bpy.data.objects.new("C", cam_data)
    sc.collection.objects.link(cam)
    sc.camera = cam
    sc.render.resolution_x = 900
    sc.render.resolution_y = 900

    def snap(pos, target, fname):
        cam.location = Vector(pos)
        cam.rotation_euler = (Vector(target) - cam.location).to_track_quat("-Z", "Y").to_euler()
        sc.render.filepath = os.path.join(OUT_PREVIEW_DIR, fname)
        bpy.ops.render.render(write_still=True)

    snap((1.05, -2.25, 1.32), (0.0, -0.10, 0.78), "превью_mixamo_на_стуле.png")
    print("=== BUILD BASE MALE UNITY DONE")


if __name__ == "__main__":
    main()
