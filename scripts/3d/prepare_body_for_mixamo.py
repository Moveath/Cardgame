"""Подготавливает базовое тело персонажа к авто-ригу в Mixamo:
  1. Импортирует чистое quad-тело из 3д обьекты/персонажи/тело/tripo_raw/*.fbx и масштабирует до роста 1,78 м (ступни на Z = 0).
  2. По цвету текстуры Color.jpg точно находит границу манжеты свитера на обеих руках и аккуратно срезает слипшиеся «варежки» кистей.
  3. Берёт детальные 5-палые кисти (Arm_Male_L и Arm_Male_R) из art3d/hands/TripoHands.blend в спокойной открытой позе,
     подгоняет масштаб и ориентацию строго по оси предплечья (большой палец вперёд, ладонь вниз) и заводит запястье внутрь манжеты без швов.
  4. Сохраняет art3d/characters/base_body_mixamo.blend и экспортирует готовый к загрузке на Mixamo.com файл:
     3д обьекты/персонажи/тело/body_for_mixamo.fbx (плюс рендеры проверки превью_для_mixamo_*.png).
"""
import math
import os

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
RAW_DIR = os.path.join(ROOT, "3д обьекты", "персонажи", "тело", "tripo_raw")
SRC_FBX = os.path.join(RAW_DIR, "tripo_convert_699299a2-cc6a-4f9a-966d-cc209f6c690b.fbx")
SRC_COLOR = os.path.join(RAW_DIR, "tripo_convert_699299a2-cc6a-4f9a-966d-cc209f6c690b.fbm", "Color.jpg")
HANDS_BLEND = os.path.join(ROOT, "art3d", "hands", "TripoHands.blend")
HANDS_TEX = os.path.join(ROOT, "unity", "CardGameClient", "Assets", "Resources", "ThreeD", "Hands", "TripoHands_BaseColor.jpg")

OUT_DIR = os.path.join(ROOT, "3д обьекты", "персонажи", "тело")
OUT_FBX = os.path.join(OUT_DIR, "body_for_mixamo.fbx")
OUT_BLEND = os.path.join(ROOT, "art3d", "characters", "base_body_mixamo.blend")

TARGET_HEIGHT = 1.78


def sample_uv_rgb(img_arr, uv):
    h, w, _ = img_arr.shape
    u = float(uv[0]) % 1.0
    v = float(uv[1]) % 1.0
    x = min(w - 1, max(0, int(u * (w - 1))))
    y = min(h - 1, max(0, int(v * (h - 1))))
    return img_arr[y, x, :3]


def is_skin_color(rgb):
    # Свитер — тёмный сине-бирюзовый (B > R, низкая яркость); кожа — светлая тёплая (R > B и яркая).
    r, g, b = float(rgb[0]), float(rgb[1]), float(rgb[2])
    return (r > 0.42) and (g > 0.35) and (r >= b - 0.02) and ((r + g + b) / 3.0 > 0.42)


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    os.makedirs(os.path.dirname(OUT_BLEND), exist_ok=True)

    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    for m in list(bpy.data.meshes):
        bpy.data.meshes.remove(m)
    for mat in list(bpy.data.materials):
        bpy.data.materials.remove(mat)

    # 1. Импортируем базовое тело и масштабируем до 1.78 м (ступни на Z = 0, центр X = 0).
    bpy.ops.import_scene.fbx(filepath=SRC_FBX)
    body = next(o for o in bpy.data.objects if o.type == "MESH")
    body.name = "Body_Base"
    bpy.ops.object.select_all(action="DESELECT")
    body.select_set(True)
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    v_raw = np.array([v.co[:] for v in body.data.vertices], dtype=np.float64)
    min_z, max_z = v_raw[:, 2].min(), v_raw[:, 2].max()
    cx = 0.5 * (v_raw[:, 0].min() + v_raw[:, 0].max())
    cy = 0.5 * (v_raw[:, 1].min() + v_raw[:, 1].max())
    sc = TARGET_HEIGHT / (max_z - min_z)
    body.location = (-cx * sc, -cy * sc, -min_z * sc)
    body.scale = (sc, sc, sc)
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=True)

    # Загружаем Color.jpg для точного определения границы синей манжеты и бледной кожи кистей.
    col_img = bpy.data.images.load(SRC_COLOR)
    cw, ch = col_img.size[0], col_img.size[1]
    buf = np.empty(cw * ch * 4, dtype=np.float32)
    col_img.pixels.foreach_get(buf)
    col_arr = buf.reshape((ch, cw, 4))
    bpy.data.images.remove(col_img)

    uv_layer = body.data.uv_layers.active.data
    vert_uv = {}
    for poly in body.data.polygons:
        for li in poly.loop_indices:
            vi = body.data.loops[li].vertex_index
            if vi not in vert_uv:
                vert_uv[vi] = uv_layer[li].uv[:]

    # Вычисляем точный срез манжеты на левой руке (+X) и зеркалим 100% симметрично на правую (-X) для идеального рига Mixamo!
    bm = bmesh.new()
    bm.from_mesh(body.data)
    bm.verts.ensure_lookup_table()
    bm.faces.ensure_lookup_table()

    sleeve_pts = []
    skin_hand_pts = []
    for v in bm.verts:
        p = v.co
        if p.x < 0.34 or p.z < 0.70 or p.z > 1.48:
            continue
        uv = vert_uv.get(v.index, (0.5, 0.5))
        rgb = sample_uv_rgb(col_arr, uv)
        if is_skin_color(rgb) and p.x > 0.46:
            skin_hand_pts.append((v, p.copy()))
        elif p.x < 0.54 and not is_skin_color(rgb):
            sleeve_pts.append(p.copy())

    sleeve_arr = np.array([[p.x, p.y, p.z] for p in sleeve_pts])
    near_m = (sleeve_arr[:, 0] >= 0.37) & (sleeve_arr[:, 0] <= 0.43)
    far_m = (sleeve_arr[:, 0] >= 0.49) & (sleeve_arr[:, 0] <= 0.54)
    p_near_l = Vector(sleeve_arr[near_m].mean(axis=0))
    p_far_l = Vector(sleeve_arr[far_m].mean(axis=0))
    arm_dir_l = (p_far_l - p_near_l).normalized()

    skin_projs = [((p - p_near_l).dot(arm_dir_l), p, v) for v, p in skin_hand_pts]
    skin_projs.sort(key=lambda t: t[0])
    # Точная плоскость реза прямо по синей кромке манжеты (-5 мм внутрь синего рукава — 0 белых зубцов!)
    cuff_proj = skin_projs[0][0] - 0.005
    tip_proj = skin_projs[-1][0]
    hand_len_l = tip_proj - cuff_proj

    ring_pts = np.array([[p.x, p.y, p.z] for pr, p, _ in skin_projs if pr <= cuff_proj + 0.024])
    wrist_l = Vector(ring_pts.mean(axis=0))
    # Смещаем центр точно на плоскость среза cuff_proj
    wrist_l = p_near_l + arm_dir_l * cuff_proj + (wrist_l - (p_near_l + arm_dir_l * (wrist_l - p_near_l).dot(arm_dir_l)))

    wrist_info = {
        1: (wrist_l, arm_dir_l, hand_len_l),
        -1: (Vector((-wrist_l.x, wrist_l.y, wrist_l.z)), Vector((-arm_dir_l.x, arm_dir_l.y, arm_dir_l.z)).normalized(), hand_len_l),
    }

    # Делаем идеально ровный плоский срез обеих манжет через bmesh.ops.bisect_plane и берём ТОЧНЫЙ центр самого кольца манжеты!
    cuff_rings = {}
    for side in (1, -1):
        wc, ad, hlen = wrist_info[side]
        # Берём только геометрию этой руки (|x| > 0.42 и z > 0.72), чтобы плоскость не задела бедро!
        arm_geom = [e for e in bm.edges if any(v.co.x * side > 0.42 and v.co.z > 0.72 for v in e.verts)]
        arm_faces = [f for f in bm.faces if any(v.co.x * side > 0.42 and v.co.z > 0.72 for v in f.verts)]
        arm_verts = list({v for f in arm_faces for v in f.verts})
        res = bmesh.ops.bisect_plane(
            bm,
            geom=arm_verts + arm_geom + arm_faces,
            plane_co=wc,
            plane_no=ad,
            clear_outer=True,
            clear_inner=False,
        )
        cut_verts = [g for g in res["geom_cut"] if isinstance(g, bmesh.types.BMVert)]
        cut_edges = [g for g in res["geom_cut"] if isinstance(g, bmesh.types.BMEdge)]
        if cut_verts:
            ring_arr = np.array([[v.co.x, v.co.y, v.co.z] for v in cut_verts])
            wc_exact = Vector(ring_arr.mean(axis=0))
            cuff_rings[side] = (wc_exact, ring_arr.copy())
        else:
            wc_exact = wc
            cuff_rings[side] = (wc_exact, np.array([[wc.x, wc.y, wc.z]]))

        # Заворачиваем край манжеты чуть внутрь (толщина вязаной кромки), чтобы внутри рукава не было пустоты!
        if cut_edges:
            ext = bmesh.ops.extrude_edge_only(bm, edges=cut_edges)
            ext_verts = [g for g in ext["geom"] if isinstance(g, bmesh.types.BMVert)]
            for v in ext_verts:
                rad = v.co - wc_exact
                rad -= ad * rad.dot(ad)
                v.co = wc_exact + rad * 0.72 - ad * 0.012
        print(f"=== SIDE {side:+d}: cuffCenter={tuple(round(c,3) for c in wc_exact)}, armDir={tuple(round(c,3) for c in ad)}, handLen={hlen:.3f}m")

    # Гарантируем 100% зеркальную симметрию центров посадки обеих кистей по левому (+X) кольцу манжеты.
    wc_l_exact, ring_l_arr = cuff_rings[1]
    wrist_info = {
        1: (wc_l_exact, arm_dir_l, hand_len_l),
        -1: (Vector((-wc_l_exact.x, wc_l_exact.y, wc_l_exact.z)), Vector((-arm_dir_l.x, arm_dir_l.y, arm_dir_l.z)).normalized(), hand_len_l),
    }

    bm.to_mesh(body.data)
    bm.free()
    body.data.update()

    # Унифицируем имя UV-слоя тела до "UVMap", чтобы после join() UV-развёртка кистей не слетела!
    if body.data.uv_layers:
        body.data.uv_layers.active.name = "UVMap"

    # 2. Импортируем идеальные 5-палые кисти Arm_Male_L и Arm_Male_R из art3d/hands/TripoHands.blend!
    with bpy.data.libraries.load(HANDS_BLEND, link=False) as (data_from, data_to):
        data_to.objects = ["Arm_Male_L", "Arm_Male_R"]

    hand_objs = []
    for side, obj_name, hand_origin_x in ((1, "Arm_Male_L", -0.15), (-1, "Arm_Male_R", 0.15)):
        hobj = bpy.data.objects[obj_name]
        bpy.context.scene.collection.objects.link(hobj)
        hobj.parent = None
        for mod in list(hobj.modifiers):
            hobj.modifiers.remove(mod)
        if hobj.data.shape_keys:
            for kb in list(hobj.data.shape_keys.key_blocks)[::-1]:
                hobj.shape_key_remove(kb)
        hobj.vertex_groups.clear()

        # Убеждаемся, что активный UV-слой кисти тоже называется ровно "UVMap"!
        while len(hobj.data.uv_layers) > 1:
            hobj.data.uv_layers.remove(hobj.data.uv_layers[-1])
        if hobj.data.uv_layers:
            hobj.data.uv_layers[0].name = "UVMap"

        # Переводим (0,0,0) в центр запястья и ПОЛНОСТЬЮ срезаем серую зону старой манжеты (y < -0.008 — остаётся 100% чистая тёплая кожа!).
        hbm = bmesh.new()
        hbm.from_mesh(hobj.data)
        for v in hbm.verts:
            v.co.x -= hand_origin_x
        hcut = bmesh.ops.bisect_plane(
            hbm,
            geom=list(hbm.verts) + list(hbm.edges) + list(hbm.faces),
            plane_co=Vector((0.0, -0.008, 0.0)),
            plane_no=Vector((0.0, -1.0, 0.0)),
            clear_outer=True,
            clear_inner=False,
        )
        # Вытягиваем чисто-кожаный срез запястья на 2.4 см внутрь синего рукава с плавным сужением (0.76x),
        # чтобы запястье плотно сидело внутри вязаной манжеты и НИКОГДА не вылезало наружу сквозь ткань!
        hcut_edges = [g for g in hcut["geom_cut"] if isinstance(g, bmesh.types.BMEdge)]
        hcut_verts = [g for g in hcut["geom_cut"] if isinstance(g, bmesh.types.BMVert)]
        if hcut_verts:
            w_cx = sum(v.co.x for v in hcut_verts) / len(hcut_verts)
            w_cz = sum(v.co.z for v in hcut_verts) / len(hcut_verts)
            for v in hbm.verts:
                v.co.x -= w_cx
                v.co.z -= w_cz
        if hcut_edges:
            ext1 = bmesh.ops.extrude_edge_only(hbm, edges=hcut_edges)
            ext1_verts = [g for g in ext1["geom"] if isinstance(g, bmesh.types.BMVert)]
            for v in ext1_verts:
                v.co.x *= 0.84
                v.co.z *= 0.84
                v.co.y = -0.020
            ext1_edges = [g for g in ext1["geom"] if isinstance(g, bmesh.types.BMEdge)]
            ext2 = bmesh.ops.extrude_edge_only(hbm, edges=ext1_edges)
            ext2_verts = [g for g in ext2["geom"] if isinstance(g, bmesh.types.BMVert)]
            for v in ext2_verts:
                v.co.x *= 0.76
                v.co.z *= 0.76
                v.co.y = -0.034

        hbm.to_mesh(hobj.data)
        hbm.free()
        hobj.data.update()

        hv = np.array([v.co[:] for v in hobj.data.vertices])
        src_hand_len = float(hv[:, 1].max())
        wrist_center, arm_dir, old_hand_len = wrist_info[side]
        target_len = max(0.178, min(0.194, old_hand_len * 1.05))
        h_scale = target_len / src_hand_len

        fwd = arm_dir.normalized()
        want_thumb = Vector((0.0, -0.96, -0.28)).normalized()
        radial = (want_thumb - fwd * want_thumb.dot(fwd)).normalized()
        if side == 1:
            dorsal = radial.cross(fwd).normalized()
            radial = fwd.cross(dorsal).normalized()
            col_x = radial
        else:
            dorsal = fwd.cross(radial).normalized()
            radial = dorsal.cross(fwd).normalized()
            col_x = -radial
        col_y = fwd
        col_z = dorsal

        # Проверяем точный полуразмер кольца синей манжеты вдоль осей col_x и col_z и мягко подгоняем запястье (y < 0.012),
        # чтобы кожа входила строго ВНУТРЬ манжеты без малейшего проступания сквозь синий трикотаж!
        rel_ring = ring_l_arr - np.array([wc_l_exact.x, wc_l_exact.y, wc_l_exact.z])
        if side == -1:
            rel_ring[:, 0] = -rel_ring[:, 0]
        cuff_rx = float(np.percentile(np.abs(rel_ring @ np.array([col_x.x, col_x.y, col_x.z])), 85)) * 0.86
        cuff_rz = float(np.percentile(np.abs(rel_ring @ np.array([col_z.x, col_z.y, col_z.z])), 85)) * 0.86
        w_mask = (hv[:, 1] >= -0.010) & (hv[:, 1] <= 0.004)
        hand_rx = float(np.max(np.abs(hv[w_mask, 0]))) * h_scale
        hand_rz = float(np.max(np.abs(hv[w_mask, 2]))) * h_scale
        fx = min(1.0, cuff_rx / max(1e-4, hand_rx))
        fz = min(1.0, cuff_rz / max(1e-4, hand_rz))
        if fx < 0.999 or fz < 0.999:
            for v in hobj.data.vertices:
                if v.co.y < 0.022:
                    t = max(0.0, min(1.0, (0.022 - v.co.y) / 0.026))
                    v.co.x *= 1.0 - (1.0 - fx) * t
                    v.co.z *= 1.0 - (1.0 - fz) * t
            hobj.data.update()

        rot_mat = Matrix((
            (col_x.x, col_y.x, col_z.x, 0.0),
            (col_x.y, col_y.y, col_z.y, 0.0),
            (col_x.z, col_y.z, col_z.z, 0.0),
            (0.0, 0.0, 0.0, 1.0),
        ))
        place_mat = Matrix.Translation(wrist_center + fwd * 0.002) @ rot_mat @ Matrix.Scale(h_scale, 4)
        hobj.data.transform(place_mat)
        hobj.matrix_world = Matrix.Identity(4)

        skin_mat = bpy.data.materials.get("Body_HandsSkin")
        if skin_mat is None:
            skin_mat = bpy.data.materials.new("Body_HandsSkin")
            skin_mat.use_nodes = True
            nt = skin_mat.node_tree
            bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
            bsdf.inputs["Roughness"].default_value = 0.62
            tex_node = nt.nodes.new("ShaderNodeTexImage")
            tex_node.image = bpy.data.images.load(HANDS_TEX, check_existing=True)
            nt.links.new(tex_node.outputs["Color"], bsdf.inputs["Base Color"])
        hobj.data.materials.clear()
        hobj.data.materials.append(skin_mat)
        hand_objs.append(hobj)
        print(f"=== GRAFTED {obj_name} (side={side:+d}): scale={h_scale:.3f}, cuffFit=({fx:.2f},{fz:.2f}), verts={len(hobj.data.vertices)}")

    # 3. Переименовываем материал тела и объединяем тело + обе 5-палые кисти в один чистый меш для Mixamo (с двумя слотами материалов!).
    if body.data.materials:
        body.data.materials[0].name = "Body_Outfit"
    bpy.ops.object.select_all(action="DESELECT")
    body.select_set(True)
    for ho in hand_objs:
        ho.select_set(True)
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.join()
    body.name = "CardCity_BaseMale"

    total_v = len(body.data.vertices)
    total_f = len(body.data.polygons)
    print(f"=== COMBINED MIXAMO BODY: verts={total_v}, faces={total_f}, mats={[m.name for m in body.data.materials]}")

    # Сохраняем рабочий .blend и экспортируем FBX с вшитыми текстурами (path_mode='COPY', embed_textures=True) для Mixamo!
    bpy.ops.wm.save_as_mainfile(filepath=OUT_BLEND)
    bpy.ops.export_scene.fbx(
        filepath=OUT_FBX,
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
        path_mode="COPY",
        embed_textures=True,
    )
    print(f"=== EXPORTED {OUT_FBX} ({os.path.getsize(OUT_FBX) // 1024} KB)")

    # 4. Рендерим контрольное превью всего тела и крупного плана новой 5-палой кисти в манжете!
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
    bg.inputs[0].default_value = (0.30, 0.30, 0.30, 1.0)
    bg.inputs[1].default_value = 0.9

    def add_area(name, loc, rot_deg, energy, sz):
        ld = bpy.data.lights.new(name, "AREA")
        ld.energy = energy
        ld.size = sz
        lo = bpy.data.objects.new(name, ld)
        lo.location = loc
        lo.rotation_euler = tuple(math.radians(a) for a in rot_deg)
        sc.collection.objects.link(lo)

    add_area("Key", (2.2, -2.8, 2.8), (50, 0, 35), 480, 2.2)
    add_area("Fill", (-2.4, -2.0, 2.0), (65, 0, -45), 220, 2.5)
    add_area("Back", (0.0, 2.6, 2.4), (55, 0, 180), 280, 2.2)

    cam_data = bpy.data.cameras.new("C")
    cam_data.lens = 55
    cam = bpy.data.objects.new("C", cam_data)
    sc.collection.objects.link(cam)
    sc.camera = cam
    sc.render.resolution_x = 900
    sc.render.resolution_y = 900

    def snap(pos, target, fname):
        cam.location = Vector(pos)
        cam.rotation_euler = (Vector(target) - cam.location).to_track_quat("-Z", "Y").to_euler()
        sc.render.filepath = os.path.join(OUT_DIR, fname)
        bpy.ops.render.render(write_still=True)

    snap((0.0, -3.1, 0.98), (0.0, 0.0, 0.90), "превью_для_mixamo_тело.png")
    wc_l, ad_l, _ = wrist_info[1]
    hc = wc_l + ad_l * 0.075
    snap(hc + Vector((0.06, -0.45, 0.28)), hc, "превью_для_mixamo_кисть.png")
    print("=== MIXAMO PREVIEW DONE")


if __name__ == "__main__":
    main()
