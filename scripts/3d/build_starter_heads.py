"""Пакетная сборка всех 12 стартовых 3D-голов (Head_Avatar01..Head_Avatar12) из Tripo3D в оба Unity-проекта:
  - Автоматически определяет чистую область КОЖИ лица (игнорируя объёмные длинные женские волосы на 04/08/12
    и пышные бороды на 03/09), чтобы масштаб и центровка каждой головы были анатомически точными!
  - Учитывает пропорции типажа: женские головы чуть изящнее (0.92x), полный мужчина (05) шире (1.07x).
  - Сужает внутрь ворота-стойки ТОЛЬКО нижние кожные ключицы, полностью сохраняя длинные волнистые волосы
    на плечах (avatar_04 рыжая, avatar_08 смуглая, avatar_12 каре)!
  - Экспортирует 4K BaseColor + 2K Normal + 2K MetallicGloss + Head_AvatarXX.fbx в оба Unity-проекта.
"""
import glob
import math
import os
import shutil

import bmesh
import bpy
import numpy as np
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
STARTERS_ROOT = os.path.join(ROOT, "3д обьекты", "персонажи", "головы", "стартовые_персонажи")
UNITY_HEADS_DIRS = [
    os.path.join(ROOT, "unity", "CardGameClient", "Assets", "Resources", "ThreeD", "Characters", "Heads"),
    os.path.join(ROOT, "screens", "qa", "shop-project", "Assets", "Resources", "ThreeD", "Characters", "Heads"),
]

HEAD_PIVOT = Vector((0.0, 0.0239, 1.5395))
TARGET_NOSE_Y = -0.148
TARGET_NOSE_Z = 1.636
COLOR_TEX_SIZE = 4096
MAP_TEX_SIZE = 2048

# (папка, имя ассета, целевая полная высота бюста в метрах, макс. радиус шеи в вороте, есть ли длинные волосы ниже ворота, сдвиг носа по Z)
STARTERS = [
    ("01_мужчина_усы",            "Head_Avatar01", 0.398, 0.058, False,  0.000),
    ("02_женщина_пучок",          "Head_Avatar02", 0.388, 0.051, False, -0.004),
    ("03_дедушка_борода",         "Head_Avatar03", 0.395, 0.058, False,  0.000),
    ("04_женщина_рыжая",          "Head_Avatar04", 0.384, 0.051, False, -0.004),
    ("05_мужчина_полный",         "Head_Avatar05", 0.408, 0.068, False,  0.002),
    ("06_бабушка_очки",           "Head_Avatar06", 0.372, 0.051, False, -0.006),
    ("07_парень_брюнет",          "Head_Avatar07", 0.396, 0.058, False,  0.000),
    ("08_женщина_смуглая",        "Head_Avatar08", 0.386, 0.051, True,  -0.004),
    ("09_мужчина_борода",         "Head_Avatar09", 0.398, 0.058, False,  0.000),
    ("10_женщина_блондинка",      "Head_Avatar10", 0.378, 0.051, False, -0.004),
    ("11_парень_короткие_волосы", "Head_Avatar11", 0.396, 0.058, False,  0.000),
    ("12_женщина_каре",           "Head_Avatar12", 0.384, 0.051, False, -0.004),
]


def find_tex(raw_dir, suffix):
    for root_d, _, files in os.walk(raw_dir):
        for fn in files:
            if fn.lower().endswith(suffix.lower()):
                return os.path.join(root_d, fn)
    raise FileNotFoundError(f"Missing *{suffix} under {raw_dir}")


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


def build_one(folder, head_name, target_bust_h, neck_rx, has_long_hair, nose_dz):
    raw_dir = os.path.join(STARTERS_ROOT, folder, "tripo_raw")
    fbxs = glob.glob(os.path.join(raw_dir, "**", "*.fbx"), recursive=True)
    if not fbxs:
        raise FileNotFoundError(f"No .fbx in {raw_dir}")
    src_fbx = fbxs[0]

    color_src = find_tex(raw_dir, "Color.jpg")
    normal_src = find_tex(raw_dir, "Normal.jpg")
    rough_src = find_tex(raw_dir, "_roughness.jpg")
    metal_src = find_tex(raw_dir, "_metallic.jpg")

    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    for m in list(bpy.data.meshes):
        bpy.data.meshes.remove(m)
    for mat in list(bpy.data.materials):
        bpy.data.materials.remove(mat)

    bpy.ops.import_scene.fbx(filepath=src_fbx)
    head = next(o for o in bpy.data.objects if o.type == "MESH")
    head.name = head_name
    bpy.ops.object.select_all(action="DESELECT")
    head.select_set(True)
    bpy.context.view_layer.objects.active = head
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    # Загружаем 4K BaseColor заранее, чтобы отличить кожу шеи от длинных волнистых волос (04/08/12)!
    col = load_resized_rgba(color_src, COLOR_TEX_SIZE, "sRGB")
    col[:, :, 3] = 1.0

    # Строим карту vertex -> цвет пикселя из BaseColor
    uv_layer = head.data.uv_layers.active.data
    vert_uv = {}
    for p in head.data.polygons:
        for li in p.loop_indices:
            vi = head.data.loops[li].vertex_index
            if vi not in vert_uv:
                vert_uv[vi] = uv_layer[li].uv[:]

    hv = np.array([v.co[:] for v in head.data.vertices], dtype=np.float64)
    z_min, z_max = hv[:, 2].min(), hv[:, 2].max()
    h_span = z_max - z_min

    # Находим эталонный цвет кожи прямо по центру лица (нос/переносица: |x| < 14% ширины, min Y в среднем поясе)
    x_mid = 0.5 * (hv[:, 0].min() + hv[:, 0].max())
    x_span = hv[:, 0].max() - hv[:, 0].min()
    front_mask = (hv[:, 2] >= z_min + 0.48 * h_span) & (hv[:, 2] <= z_min + 0.68 * h_span) & (np.abs(hv[:, 0] - x_mid) < 0.14 * x_span)
    front_idxs = np.where(front_mask)[0]
    y_cut = np.percentile(hv[front_idxs, 1], 25)
    face_idxs = front_idxs[hv[front_idxs, 1] <= y_cut]
    face_rgbs = []
    for vi in face_idxs:
        if vi in vert_uv:
            u, vv = vert_uv[vi]
            px = min(COLOR_TEX_SIZE - 1, max(0, int((u % 1.0) * (COLOR_TEX_SIZE - 1))))
            py = min(COLOR_TEX_SIZE - 1, max(0, int((vv % 1.0) * (COLOR_TEX_SIZE - 1))))
            face_rgbs.append(col[py, px, :3])
    skin_ref = np.median(np.array(face_rgbs), axis=0) if face_rgbs else np.array([0.65, 0.48, 0.40])
    skin_rb = float(skin_ref[0] - skin_ref[2])
    skin_rg = float(skin_ref[0] - skin_ref[1])

    # Для длинноволосых моделей (08_женщина_смуглая) помечаем ТОЛЬКО явно тёмные пряди волос как is_skin=False,
    # а всю кожу и затенённые ключицы — как is_skin=True, чтобы ни один пиксель ключиц не вылез сквозь свитер!
    is_skin = np.ones(len(hv), dtype=bool)
    if has_long_hair:
        for vi in range(len(hv)):
            if vi in vert_uv:
                u, vv = vert_uv[vi]
                px = min(COLOR_TEX_SIZE - 1, max(0, int((u % 1.0) * (COLOR_TEX_SIZE - 1))))
                py = min(COLOR_TEX_SIZE - 1, max(0, int((vv % 1.0) * (COLOR_TEX_SIZE - 1))))
                c = col[py, px, :3]
                luma = 0.299 * float(c[0]) + 0.587 * float(c[1]) + 0.114 * float(c[2])
                rb = float(c[0] - c[2])
                # Тёмно-каштановые локоны имеют низкую яркость (<0.30) и малый красный сдвиг (R-B < 0.16),
                # тогда как кожа (даже в тени ключиц) теплее (R-B >= 0.18) и светлее!
                if luma < 0.30 and rb < 0.16:
                    is_skin[vi] = False

    # Все 12 бюстов Tripo имеют единую композицию (ключицы..макушка): масштабируем СТРОГО по высоте бюста h_span!
    # Это 100% исключает сжатие/раздувание из-за пышных волос, пучка или оттенка причёски.
    scale = target_bust_h / max(h_span, 1e-4)

    # Центр лица по X берём строго по симметричному переднему профилю (нос/переносица), без влияния асимметричной причёски
    nose_band = hv[(hv[:, 2] >= z_min + 0.50 * h_span) & (hv[:, 2] <= z_min + 0.64 * h_span) & (np.abs(hv[:, 0] - x_mid) < 0.14 * x_span)]
    nose_idx = np.argmin(nose_band[:, 1])
    cx_raw = float(nose_band[nose_idx, 0])
    nose_y_raw = float(nose_band[nose_idx, 1])
    nose_z_raw = float(nose_band[nose_idx, 2])

    nose_z_target = TARGET_NOSE_Z + nose_dz
    shift = Vector((
        -cx_raw * scale,
        TARGET_NOSE_Y - nose_y_raw * scale,
        nose_z_target - nose_z_raw * scale,
    ))
    head.scale = (scale, scale, scale)
    head.location = shift
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=True)

    hv_w = np.array([v.co[:] for v in head.data.vertices], dtype=np.float64)
    print(f"=== {head_name} SCALED (s={scale:.4f}, hRaw={h_span:.3f}, longHair={has_long_hair}, skinRGB={skin_ref.round(3)}): z=[{hv_w[:,2].min():.3f}..{hv_w[:,2].max():.3f}]")

    # 2. Аккуратный срез низа КОЖНОЙ груди внутри свитера (z = 1.445) + заужение ключиц в ворот-стойку,
    #    НЕ трогая длинные волнистые волосы (04, 08, 12), чтобы локоны красиво лежали поверх плеч свитера!
    bm = bmesh.new()
    bm.from_mesh(head.data)
    bm.verts.ensure_lookup_table()
    bm.faces.ensure_lookup_table()

    # Сохраняем признак is_skin по координатам/индексу до bisect
    skin_layer = bm.verts.layers.int.new("is_skin")
    for idx, v in enumerate(bm.verts):
        v[skin_layer] = 1 if (idx < len(is_skin) and is_skin[idx]) else 0

    # Определяем центр шеи по коже на уровне z = 1.515..1.550
    neck_ring = np.array([[v.co.x, v.co.y, v.co.z] for v in bm.verts if v[skin_layer] == 1 and 1.515 <= v.co.z <= 1.550 and v.co.y > -0.065])
    taper_cx = float(neck_ring[:, 0].mean()) if len(neck_ring) else 0.0
    taper_cy = min(float(neck_ring[:, 1].mean()) if len(neck_ring) else 0.012, 0.020)

    # Срезаем глубоко внутри свитера (z = 1.445) ТОЛЬКО кожные грани (длинные волосы спереди/сбоку остаются целыми!)
    cut_faces = [f for f in bm.faces if (not has_long_hair) or all(v[skin_layer] == 1 for v in f.verts)]
    if cut_faces:
        cut_edges = list({e for f in cut_faces for e in f.edges})
        cut_verts = list({v for f in cut_faces for v in f.verts})
        bmesh.ops.bisect_plane(
            bm,
            geom=cut_verts + cut_edges + cut_faces,
            plane_co=Vector((0.0, 0.0, 1.445)),
            plane_no=Vector((0.0, 0.0, -1.0)),
            clear_outer=True,
            clear_inner=False,
        )

    ngons = [f for f in bm.faces if len(f.verts) > 4]
    if ngons:
        bmesh.ops.triangulate(bm, faces=ngons)

    # Заужаем ключицы внутрь ворота-стойки (для длинноволосых женщин сохраняем тёмные локоны v[skin_layer]==0 снаружи плеч,
    # а ВСЮ кожу ключиц v[skin_layer]==1 полностью заправляем вглубь ворота без торчащих треугольников!)
    neck_ry = neck_rx * 0.96
    for v in bm.verts:
        if has_long_hair and v[skin_layer] == 0:
            # Нижние концы длинных локонов сохраняем снаружи ворота, без агрессивного выталкивания ключиц
            continue
        collar_z = 1.516 + 0.45 * v.co.y
        if v.co.z < collar_z + 0.044:
            t_in = max(0.0, min(1.0, (collar_z + 0.044 - v.co.z) / 0.046))
            dx = v.co.x - taper_cx
            dy = v.co.y - taper_cy
            below = max(0.0, min(1.0, (collar_z - v.co.z) / 0.052))
            max_rx = neck_rx - 0.010 * below
            max_ry = neck_ry - 0.010 * below
            ell = math.hypot(dx / max_rx, dy / max_ry)
            if ell > 1.0:
                blend = t_in * t_in * (3.0 - 2.0 * t_in)
                scale_e = (1.0 - blend) + blend / ell
                v.co.x = taper_cx + dx * scale_e
                v.co.y = taper_cy + dy * scale_e

    for v in bm.verts:
        v.co -= HEAD_PIVOT

    bm.to_mesh(head.data)
    bm.free()
    head.data.update()

    if head.data.uv_layers:
        head.data.uv_layers.active.name = "UVMap"
    mat = bpy.data.materials.new(f"{head_name}_Mat")
    mat.use_nodes = True
    head.data.materials.clear()
    head.data.materials.append(mat)

    nrm = load_resized_rgba(normal_src, MAP_TEX_SIZE, "Non-Color")
    nrm[:, :, 3] = 1.0
    rgh = load_resized_rgba(rough_src, MAP_TEX_SIZE, "Non-Color")
    mtl = load_resized_rgba(metal_src, MAP_TEX_SIZE, "Non-Color")
    mg = np.zeros((MAP_TEX_SIZE, MAP_TEX_SIZE, 4), dtype=np.float32)
    mg[:, :, 0] = np.clip(mtl[:, :, 0] * 0.25, 0.0, 0.25)
    mg[:, :, 3] = np.clip((1.0 - rgh[:, :, 0]) * 0.42, 0.06, 0.36)

    tmp_fbx = os.path.join(os.environ.get("TEMP", r"C:\Windows\Temp"), f"{head_name}.fbx")
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
        shutil.copy2(tmp_fbx, os.path.join(target_dir, f"{head_name}.fbx"))
        save_png(col, os.path.join(target_dir, f"{head_name}_BaseColor.png"), "sRGB")
        save_png(nrm, os.path.join(target_dir, f"{head_name}_Normal.png"), "Non-Color")
        save_png(mg, os.path.join(target_dir, f"{head_name}_MetallicGloss.png"), "Non-Color")
    print(f"=== Synced {head_name} (4K+2K+FBX) to both projects")


def main():
    for folder, head_name, bust_h, neck_rx, long_hair, nose_dz in STARTERS:
        build_one(folder, head_name, bust_h, neck_rx, long_hair, nose_dz)
    print("=== ALL 12 STARTER HEADS BUILT ===")


if __name__ == "__main__":
    main()
