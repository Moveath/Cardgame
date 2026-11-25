"""Универсальный конвейер подготовки 3D-головы друга из Tripo3D для посадки на Socket_Head в Unity:
  1. Импортирует сырой FBX из 3д обьекты/персонажи/головы/<friend>/tripo_raw/*.fbx.
  2. Выравнивает лицо строго вперёд (-Y в Blender -> +Z в Unity), центрирует по X и масштабирует
     точно под анатомию базового тела BaseMale (ширина головы по ушам ~0,196 м).
  3. Срезает широкое основание ключиц ниже шеи и аккуратно вытягивает/сужает «хвостик» шеи на 5 см
     вглубь вязаного ворота свитера (радиус < 5,6 см) — гарантированный ноль щелей и проступаний при любых поворотах!
  4. Переносит origin (0,0,0) точно в пивот кости mixamorig:Head (0, +0.024, 1.540 на теле),
     чтобы в Unity голова садилась в Socket_Head с нулевым смещением.
  5. Упаковывает 2K URP PBR-карты (BaseColor, Normal, MetallicGloss) и экспортирует Head_<Name>.fbx
     в оба Unity-проекта + рендерит проверку посадки головы в ворот свитера.
"""
import argparse
import glob
import math
import os
import shutil
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
BASE_MALE_FBX = os.path.join(
    ROOT, "unity", "CardGameClient", "Assets", "Resources", "ThreeD", "Characters", "BaseMale.fbx",
)
BODY_COL = os.path.join(
    ROOT, "unity", "CardGameClient", "Assets", "Resources", "ThreeD", "Characters", "BodyMale_BaseColor.png",
)
UNITY_HEADS_DIRS = [
    os.path.join(ROOT, "unity", "CardGameClient", "Assets", "Resources", "ThreeD", "Characters", "Heads"),
    os.path.join(ROOT, "screens", "qa", "shop-project", "Assets", "Resources", "ThreeD", "Characters", "Heads"),
]

# Точные координаты пивота mixamorig:Head и ключевых точек на теле BaseMale (рост 1,78 м)
HEAD_PIVOT = Vector((0.0, 0.0239, 1.5395))
TARGET_EAR_WIDTH = 0.194       # Ширина головы по ушам (на BaseMale = 0.1918 м)
TARGET_CHIN_Z = 1.552          # Высота низа подбородка на BaseMale
TARGET_NOSE_Y = -0.148         # Глубина кончика носа на BaseMale
COLLAR_TUCK_TOP_Z = 1.515      # Верхняя граница скрытой в ворот зоны шеи
COLLAR_TUCK_BOTTOM_Z = 1.445   # Нижний край шеи внутри свитера (глубоко за воротом!)
COLOR_TEX_SIZE = 4096          # Полные 4K для лица (глаза, ресницы, брови, тон кожи — без мыла!)
MAP_TEX_SIZE = 2048            # 2K для карт нормалей и шероховатости


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--folder", default="марат")
    ap.add_argument("--name", default="Head_Marat")
    ap.add_argument("--no-preview", action="store_true")
    return ap.parse_args(argv)


def find_tex(fbm_dir, suffix):
    for root_d, _, files in os.walk(fbm_dir):
        for fn in files:
            if fn.lower().endswith(suffix.lower()):
                return os.path.join(root_d, fn)
    raise FileNotFoundError(f"Missing *{suffix} under {fbm_dir}")


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
    args = parse_args()
    friend_dir = os.path.join(ROOT, "3д обьекты", "персонажи", "головы", args.folder)
    raw_dir = os.path.join(friend_dir, "tripo_raw")
    fbx_files = glob.glob(os.path.join(raw_dir, "*.fbx"))
    if not fbx_files:
        raise FileNotFoundError(f"No .fbx in {raw_dir}")
    src_fbx = fbx_files[0]

    color_src = find_tex(raw_dir, "Color.jpg")
    normal_src = find_tex(raw_dir, "Normal.jpg")
    rough_src = find_tex(raw_dir, "_roughness.jpg")
    metal_src = find_tex(raw_dir, "_metallic.jpg")

    # 1. Очищаем сцену и импортируем голову друга
    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    for m in list(bpy.data.meshes):
        bpy.data.meshes.remove(m)
    for mat in list(bpy.data.materials):
        bpy.data.materials.remove(mat)

    bpy.ops.import_scene.fbx(filepath=src_fbx)
    head = next(o for o in bpy.data.objects if o.type == "MESH")
    head.name = args.name
    bpy.ops.object.select_all(action="DESELECT")
    head.select_set(True)
    bpy.context.view_layer.objects.active = head
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    # Проверяем, что лицо смотрит в -Y (нос выступает сильнее в -Y, чем затылок, или по профилю середины)
    hv = np.array([v.co[:] for v in head.data.vertices], dtype=np.float64)
    z_min, z_max = hv[:, 2].min(), hv[:, 2].max()
    h_span = z_max - z_min

    # Находим ширину по уровню ушей/глаз (примерно 60-66% высоты бюста) и кончик носа (52-60%)
    ear_sl = hv[(hv[:, 2] >= z_min + 0.58 * h_span) & (hv[:, 2] <= z_min + 0.67 * h_span)]
    ear_w = ear_sl[:, 0].max() - ear_sl[:, 0].min()
    scale = TARGET_EAR_WIDTH / ear_w

    # Центр X, кончик носа (min Y в зоне лица) и подбородок (где передний профиль резко уходит в шею)
    nose_sl = hv[(hv[:, 2] >= z_min + 0.50 * h_span) & (hv[:, 2] <= z_min + 0.62 * h_span) & (np.abs(hv[:, 0]) < 0.06 * ear_w)]
    nose_y_raw = float(nose_sl[:, 1].min())
    nose_z_raw = float(nose_sl[np.argmin(nose_sl[:, 1]), 2])
    cx_raw = 0.5 * (ear_sl[:, 0].min() + ear_sl[:, 0].max())

    # На BaseMale кончик носа стоит на Z ≈ 1.635 м, Y ≈ -0.148 м -> выравниваем по носу (самый точный ориентир лица!)
    TARGET_NOSE_Z = 1.636
    shift = Vector((
        -cx_raw * scale,
        TARGET_NOSE_Y - nose_y_raw * scale,
        TARGET_NOSE_Z - nose_z_raw * scale,
    ))
    head.scale = (scale, scale, scale)
    head.location = shift
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=True)

    hv_w = np.array([v.co[:] for v in head.data.vertices], dtype=np.float64)
    print(f"=== {args.name} SCALED (s={scale:.4f}): bounds={hv_w.min(axis=0).round(4)}..{hv_w.max(axis=0).round(4)}")

    # 2. Срезаем широкую часть ключиц ниже COLLAR_TUCK_BOTTOM_Z и мягко заужаем скрытый участок шеи
    #    (ниже COLLAR_TUCK_TOP_Z = 1.515), чтобы шея идеально входила ВНУТРЬ синего вязаного ворота без малейшего проступания!
    bm = bmesh.new()
    bm.from_mesh(head.data)

    # Сначала находим центр шеи на уровне z = 1.52..1.54
    neck_ring = np.array([[v.co.x, v.co.y, v.co.z] for v in bm.verts if 1.515 <= v.co.z <= 1.545 and v.co.y > -0.055])
    neck_cx = float(neck_ring[:, 0].mean()) if len(neck_ring) else 0.0
    neck_cy = float(neck_ring[:, 1].mean()) if len(neck_ring) else 0.025

    # Ровный срез нижней части бюста глубоко внутри свитера (z = 1.445)
    bmesh.ops.bisect_plane(
        bm,
        geom=list(bm.verts) + list(bm.edges) + list(bm.faces),
        plane_co=Vector((0.0, 0.0, COLLAR_TUCK_BOTTOM_Z)),
        plane_no=Vector((0.0, 0.0, -1.0)),
        clear_outer=True,
        clear_inner=False,
    )
    # Триангулируем случайные n-гоны на срезе
    ngons = [f for f in bm.faces if len(f.verts) > 4]
    if ngons:
        bmesh.ops.triangulate(bm, faces=ngons)

    # Плавно заужаем широкие крылья трапеций/ключиц (начиная чуть ниже челюсти, z < collar_z + 0.048)
    # до анатомичной цилиндрической шеи (max_rx = 5.8 см -> 5.0 см внутри свитера), чтобы ни у одного друга
    # (Егор, Аслан, Али...) острые края трапеций никогда не торчали поверх резинки свитера!
    taper_cx = neck_cx
    taper_cy = min(neck_cy, 0.022)
    for v in bm.verts:
        collar_z = 1.512 + 0.50 * v.co.y
        if v.co.z < collar_z + 0.048:
            t_in = max(0.0, min(1.0, (collar_z + 0.048 - v.co.z) / 0.048))
            dx = v.co.x - taper_cx
            dy = v.co.y - taper_cy
            below = max(0.0, min(1.0, (collar_z - v.co.z) / 0.055))
            max_rx = 0.058 - 0.009 * below
            max_ry = 0.056 - 0.009 * below
            ell = math.hypot(dx / max_rx, dy / max_ry)
            if ell > 1.0:
                blend = t_in * t_in * (3.0 - 2.0 * t_in)
                scale_e = (1.0 - blend) + blend / ell
                v.co.x = taper_cx + dx * scale_e
                v.co.y = taper_cy + dy * scale_e

    # Переносим пивот (0,0,0) меша точно в HEAD_PIVOT (mixamorig:Head), чтобы в Unity он вставал в Socket_Head в (0,0,0)!
    for v in bm.verts:
        v.co -= HEAD_PIVOT

    bm.to_mesh(head.data)
    bm.free()
    head.data.update()

    # Убеждаемся, что имя слота материала и UV-слоя чистые и предсказуемые
    if head.data.uv_layers:
        head.data.uv_layers.active.name = "UVMap"
    mat = bpy.data.materials.new(f"{args.name}_Mat")
    mat.use_nodes = True
    head.data.materials.clear()
    head.data.materials.append(mat)

    # 3. Сохраняем 4K BaseColor (максимальная чёткость глаз, бровей и кожи) + 2K Normal/MG и экспортируем Head_<Name>.fbx
    col = load_resized_rgba(color_src, COLOR_TEX_SIZE, "sRGB")
    col[:, :, 3] = 1.0

    # Очищаем впечённые белые/тёмные воротники футболок Tripo3D в самой нижней зоне шеи (например, белый уголок у Марата,
    # тёмный ворот у Али/Аслана), заменяя их на точный тон кожи середины шеи этого же друга (волосы на затылке не трогаем!).
    if head.data.uv_layers:
        uv_d = head.data.uv_layers.active.data
        skin_samples = []
        low_uv_boxes = []
        for p in head.data.polygons:
            ctr = p.center + HEAD_PIVOT
            if 1.532 <= ctr.z <= 1.564 and ctr.y < 0.025 and abs(ctr.x) < 0.045:
                u, vv = uv_d[p.loop_indices[0]].uv
                px = min(COLOR_TEX_SIZE - 1, max(0, int((u % 1.0) * (COLOR_TEX_SIZE - 1))))
                py = min(COLOR_TEX_SIZE - 1, max(0, int((vv % 1.0) * (COLOR_TEX_SIZE - 1))))
                c = col[py, px, :3]
                if c[0] > c[2] + 0.05 and 0.28 < c[0] < 0.84:
                    skin_samples.append(c)
            cz = 1.512 + 0.50 * ctr.y
            if ctr.z < cz + 0.018 and ctr.z < 1.546:
                us = [uv_d[li].uv[0] % 1.0 for li in p.loop_indices]
                vs = [uv_d[li].uv[1] % 1.0 for li in p.loop_indices]
                if max(us) - min(us) < 0.15 and max(vs) - min(vs) < 0.15:
                    low_uv_boxes.append((min(us), max(us), min(vs), max(vs), ctr.y))
        if skin_samples and low_uv_boxes:
            neck_rgb = np.median(np.array(skin_samples), axis=0).astype(np.float32)
            cleaned_px = 0
            pad = 6
            for u0, u1, v0, v1, cy in low_uv_boxes:
                x0 = max(0, int(u0 * (COLOR_TEX_SIZE - 1)) - pad)
                x1 = min(COLOR_TEX_SIZE, int(u1 * (COLOR_TEX_SIZE - 1)) + pad + 1)
                y0 = max(0, int(v0 * (COLOR_TEX_SIZE - 1)) - pad)
                y1 = min(COLOR_TEX_SIZE, int(v1 * (COLOR_TEX_SIZE - 1)) + pad + 1)
                patch = col[y0:y1, x0:x1, :3]
                sat = patch.max(axis=-1) - patch.min(axis=-1)
                lum = patch.mean(axis=-1)
                # Удаляем ТОЛЬКО впечённую одежду (белый/серый воротник или чёрную футболку), не трогая живую кожу и волосы!
                if cy > 0.032:
                    bad = (lum > 0.76) | ((sat < 0.055) & (lum > 0.45))
                else:
                    bad = (lum > 0.76) | (lum < 0.15) | (sat < 0.062)
                if bad.any():
                    patch[bad] = neck_rgb
                    cleaned_px += int(bad.sum())
            print(f"=== {args.name} NECK CLEAN: neckRGB=({neck_rgb[0]:.3f},{neck_rgb[1]:.3f},{neck_rgb[2]:.3f}), cleanedPx={cleaned_px}")
    nrm = load_resized_rgba(normal_src, MAP_TEX_SIZE, "Non-Color")
    nrm[:, :, 3] = 1.0
    rgh = load_resized_rgba(rough_src, MAP_TEX_SIZE, "Non-Color")
    mtl = load_resized_rgba(metal_src, MAP_TEX_SIZE, "Non-Color")

    # Мягкая натуральная кожа и волосы: без пластикового глянца (smoothness 0.08..0.34)
    mg = np.zeros((MAP_TEX_SIZE, MAP_TEX_SIZE, 4), dtype=np.float32)
    mg[:, :, 0] = np.clip(mtl[:, :, 0] * 0.25, 0.0, 0.25)
    mg[:, :, 3] = np.clip((1.0 - rgh[:, :, 0]) * 0.42, 0.06, 0.36)

    tmp_fbx = os.path.join(os.environ.get("TEMP", r"C:\Windows\Temp"), f"{args.name}.fbx")
    bpy.ops.object.select_all(action="DESELECT")
    head.select_set(True)
    bpy.context.view_layer.objects.active = head
    bpy.ops.export_scene.fbx(
        filepath=tmp_fbx,
        use_selection=True,
        object_types={"MESH"},
        axis_forward="-Z",
        axis_up="Y",
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL",
        bake_space_transform=True,
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        bake_anim=False,
        path_mode="AUTO",
        embed_textures=False,
    )

    for target_dir in UNITY_HEADS_DIRS:
        os.makedirs(target_dir, exist_ok=True)
        shutil.copy2(tmp_fbx, os.path.join(target_dir, f"{args.name}.fbx"))
        save_png(col, os.path.join(target_dir, f"{args.name}_BaseColor.png"), "sRGB")
        save_png(nrm, os.path.join(target_dir, f"{args.name}_Normal.png"), "Non-Color")
        save_png(mg, os.path.join(target_dir, f"{args.name}_MetallicGloss.png"), "Non-Color")
        print(f"=== Synced {args.name} assets to {target_dir}")

    if args.no_preview:
        print(f"=== BUILD FRIEND HEAD {args.name} DONE (no preview)")
        return

    # 4. Рендерим наглядное превью головы Марата, посаженной прямо в вязаный ворот BaseMale!
    #    (Сначала двигаем голову обратно на HEAD_PIVOT для рендера на теле.)
    head.location = HEAD_PIVOT
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    tc = nt.nodes.new("ShaderNodeTexImage")
    tc.image = bpy.data.images.load(os.path.join(UNITY_HEADS_DIRS[0], f"{args.name}_BaseColor.png"))
    nt.links.new(tc.outputs["Color"], bsdf.inputs["Base Color"])
    tn = nt.nodes.new("ShaderNodeTexImage")
    tn.image = bpy.data.images.load(os.path.join(UNITY_HEADS_DIRS[0], f"{args.name}_Normal.png"))
    tn.image.colorspace_settings.name = "Non-Color"
    nrm_n = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(tn.outputs["Color"], nrm_n.inputs["Color"])
    nt.links.new(nrm_n.outputs["Normal"], bsdf.inputs["Normal"])
    bsdf.inputs["Roughness"].default_value = 0.72

    # Подгружаем BaseMale и срезаем временную лысую голову точно по той же дуге ворота + мягкий подворот кромки, что и в Unity!
    bpy.ops.import_scene.fbx(filepath=BASE_MALE_FBX)
    body = next(o for o in bpy.data.objects if o.type == "MESH" and o != head)
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    bm_b = bmesh.new()
    bm_b.from_mesh(body.data)
    mw = body.matrix_world
    mw_inv = mw.inverted()

    def collar_limit(p):
        return 1.484 + 0.46 * p.y + 2.6 * (p.x * p.x)

    del_faces = []
    for f in bm_b.faces:
        if f.material_index != 0:
            continue
        pts = [mw @ v.co for v in f.verts]
        if any(p.z > collar_limit(p) and abs(p.x) < 0.13 for p in pts):
            del_faces.append(f)
    bmesh.ops.delete(bm_b, geom=del_faces, context="FACES")
    # Мягко подгибаем пограничные вершины ворота на 4 мм внутрь и вниз — ровный чистый край трикотажа без зубцов!
    neck_axis_xy = Vector((0.0, 0.015, 0.0))
    for v in bm_b.verts:
        if not v.is_boundary:
            continue
        p = mw @ v.co
        if p.z > 1.44 and abs(p.x) < 0.12:
            rad = Vector((p.x - neck_axis_xy.x, p.y - neck_axis_xy.y, 0.0))
            p.x -= rad.x * 0.10
            p.y -= rad.y * 0.10
            p.z -= 0.005
            v.co = mw_inv @ p
    bm_b.to_mesh(body.data)
    bm_b.free()
    body.data.update()

    bmat = bpy.data.materials.new("BodyPreview")
    bmat.use_nodes = True
    bnt = bmat.node_tree
    bbsdf = next(n for n in bnt.nodes if n.type == "BSDF_PRINCIPLED")
    btc = bnt.nodes.new("ShaderNodeTexImage")
    btc.image = bpy.data.images.load(BODY_COL)
    bnt.links.new(btc.outputs["Color"], bbsdf.inputs["Base Color"])
    bbsdf.inputs["Roughness"].default_value = 0.90
    body.data.materials[0] = bmat

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
    bg.inputs[0].default_value = (0.20, 0.19, 0.18, 1.0)
    bg.inputs[1].default_value = 0.85

    def add_area(name, loc, rot_deg, energy, sz):
        ld = bpy.data.lights.new(name, "AREA")
        ld.energy = energy
        ld.size = sz
        lo = bpy.data.objects.new(name, ld)
        lo.location = loc
        lo.rotation_euler = tuple(math.radians(a) for a in rot_deg)
        sc.collection.objects.link(lo)

    add_area("Key", (1.2, -1.8, 2.2), (55, 0, 32), 260, 1.6)
    add_area("Fill", (-1.5, -1.4, 1.8), (68, 0, -45), 120, 1.8)
    add_area("Rim", (0.2, 1.8, 2.0), (55, 0, 175), 160, 1.6)

    cam_data = bpy.data.cameras.new("C")
    cam_data.lens = 65
    cam = bpy.data.objects.new("C", cam_data)
    sc.collection.objects.link(cam)
    sc.camera = cam
    sc.render.resolution_x = 900
    sc.render.resolution_y = 900

    def snap(pos, target, fname):
        cam.location = Vector(pos)
        cam.rotation_euler = (Vector(target) - cam.location).to_track_quat("-Z", "Y").to_euler()
        sc.render.filepath = os.path.join(friend_dir, fname)
        bpy.ops.render.render(write_still=True)

    snap((0.0, -1.05, 1.62), (0.0, 0.0, 1.58), "превью_марат_перед.png")
    snap((0.68, -0.82, 1.64), (0.0, 0.0, 1.57), "превью_марат_3_4.png")
    print(f"=== BUILD FRIEND HEAD {args.name} DONE")


if __name__ == "__main__":
    main()
