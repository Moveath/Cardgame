"""Создаёт безупречно чистое безголовое тело BaseMale_Headless.fbx из BaseMale.fbx:
  - Полностью сохраняет все 65 костей Mixamo Armature, веса вершин, 5-палые кисти и UVMap.
  - По цвету текстуры BodyMale_BaseColor.png находит точную линию перехода синего вязаного ворота в кожу шеи
    и через bmesh.ops.bisect_plane ровно срезает временную лысую голову строго по внутренней кромке ворота.
  - Заворачивает кромку синего трикотажного ворота на 1.2 см внутрь и вниз (как на манжетах рукавов),
    чтобы у любого друга (Марат, Аслан, Али...) шея входила в плотный, ровный, бесшовный воротник без зубцов и дыр!
"""
import math
import os
import shutil

import bmesh
import bpy
import numpy as np
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
MIXAMO_BASE_FBX = os.path.join(
    ROOT, "3д обьекты", "персонажи", "тело", "mixamo", "body_mixamo_clean.fbx",
)
BODY_COL = os.path.join(
    ROOT, "unity", "CardGameClient", "Assets", "Resources", "ThreeD", "Characters", "BodyMale_BaseColor.png",
)
HEAD_MARAT_FBX = os.path.join(
    ROOT, "unity", "CardGameClient", "Assets", "Resources", "ThreeD", "Characters", "Heads", "Head_Marat.fbx",
)
HEAD_MARAT_COL = os.path.join(
    ROOT, "unity", "CardGameClient", "Assets", "Resources", "ThreeD", "Characters", "Heads", "Head_Marat_BaseColor.png",
)
HEAD_MARAT_NRM = os.path.join(
    ROOT, "unity", "CardGameClient", "Assets", "Resources", "ThreeD", "Characters", "Heads", "Head_Marat_Normal.png",
)
MARAT_PREVIEW_DIR = os.path.join(ROOT, "3д обьекты", "персонажи", "головы", "марат")
UNITY_CHARS_DIRS = [
    os.path.join(ROOT, "unity", "CardGameClient", "Assets", "Resources", "ThreeD", "Characters"),
    os.path.join(ROOT, "screens", "qa", "shop-project", "Assets", "Resources", "ThreeD", "Characters"),
]
HEAD_PIVOT = Vector((0.0, 0.0239, 1.5395))


def smooth_hand_creases(body):
    """Очищает 5-палые кисти (material_index == 1) от 2 дефектов исходной Tripo-геометрии:
      1. Удаляет 8 отдельных накладных «губок» морщин на средних суставах (по 4 на каждую кисть, 180–400 граней),
         под которыми на основном меше кисти уже есть цельная гладкая герметичная кожа пальца без единой дырки.
      2. На 8 отдельных оболочках ногтей (80–170 граней) удаляет невидимую нижнюю/заднюю стенку (normal·dorsal <= 0.30),
         из-за которой усреднение нормалей на кромке кутикулы красило основание каждого ногтя в тёмно-коричневый цвет.
      3. Мягко выглаживает оставшиеся резкие стыки (>28°) на основном меше кисти (4 шага без усадки) и сбрасывает
         устаревшие split-нормали, сохраняя 100% UV, манжеты рукавов и веса всех 65 костей Mixamo."""
    mw = body.matrix_world
    bm = bmesh.new()
    bm.from_mesh(body.data)
    bm.verts.ensure_lookup_table()
    bm.edges.ensure_lookup_table()
    bm.faces.ensure_lookup_table()

    # Разбиваем слот кистей на связные компоненты
    hfaces = [f for f in bm.faces if f.material_index == 1]
    vis = set()
    comps = []
    for f in hfaces:
        if f in vis:
            continue
        st = [f]
        cp = []
        while st:
            cf = st.pop()
            if cf in vis:
                continue
            vis.add(cf)
            cp.append(cf)
            for e in cf.edges:
                for nf in e.link_faces:
                    if nf.material_index == 1 and nf not in vis:
                        st.append(nf)
        comps.append(cp)

    to_del = []
    del_knuckles = 0
    trimmed_nails = 0
    nail_verts = set()
    for c in comps:
        if 180 <= len(c) <= 400:
            # Накладная «складка» на среднем суставе — целиком лишний дубль поверх гладкого пальца!
            to_del.extend(c)
            del_knuckles += 1
        elif 80 <= len(c) <= 170:
            # Отдельная пластина ногтя: вычисляем СОБСТВЕННУЮ локальную нормаль тыла именно этого пальца
            # (указательный и мизинец чуть развёрнуты вокруг оси!) и оставляем только выпуклую верхнюю пластину ногтя.
            side_sign = 1.0 if (mw @ c[0].calc_center_median()).x > 0 else -1.0
            approx_dorsal = Vector((0.68 * side_sign, 0.14, 0.72)).normalized()
            scored = sorted(c, key=lambda f: (mw.to_3x3() @ f.normal).normalized().dot(approx_dorsal), reverse=True)
            top_seed = scored[: max(10, len(scored) // 4)]
            local_dorsal = sum(((mw.to_3x3() @ f.normal).normalized() for f in top_seed), Vector()).normalized()
            for f in c:
                if (mw.to_3x3() @ f.normal).normalized().dot(local_dorsal) <= 0.18:
                    to_del.append(f)
                else:
                    for v in f.verts:
                        nail_verts.add(v)
            trimmed_nails += 1

    if to_del:
        bmesh.ops.delete(bm, geom=to_del, context="FACES")
    bm.verts.ensure_lookup_table()
    bm.edges.ensure_lookup_table()
    bm.faces.ensure_lookup_table()
    bm.normal_update()

    # Мягко сглаживаем оставшиеся резкие перегибы (>28°) строго на основном меше кисти (не трогая границу манжеты и ногти)
    hand_faces = [f for f in bm.faces if f.material_index == 1]
    hand_verts = {v for f in hand_faces for v in f.verts}
    locked = {
        v for v in hand_verts
        if any(f.material_index != 1 for f in v.link_faces) or v.is_boundary or v in nail_verts
    }
    hand_edges = [e for e in bm.edges if len(e.link_faces) == 2 and all(f.material_index == 1 for f in e.link_faces)]
    before_gt70 = sum(math.degrees(e.calc_face_angle(0.0)) > 70.0 for e in hand_edges)

    ring0 = {v for e in hand_edges if math.degrees(e.calc_face_angle(0.0)) > 28.0 for v in e.verts if v not in locked}
    ring1 = {
        e.other_vert(v) for v in ring0 for e in v.link_edges
        if e.other_vert(v) in hand_verts and e.other_vert(v) not in locked and e.other_vert(v) not in ring0
    }
    weights = {v: 1.0 for v in ring0}
    weights.update({v: 0.50 for v in ring1})
    nbrs = {v: [e.other_vert(v) for e in v.link_edges if e.other_vert(v) in hand_verts] for v in weights}

    for _ in range(4):
        deltas = {}
        for v, w in weights.items():
            ns = nbrs[v]
            if not ns:
                continue
            avg = Vector((0.0, 0.0, 0.0))
            for nv in ns:
                avg += nv.co
            avg /= len(ns)
            deltas[v] = (avg - v.co) * (0.45 * w)
        for v, d in deltas.items():
            v.co += d

    bm.normal_update()
    for f in hand_faces:
        f.smooth = True

    after_gt70 = sum(math.degrees(e.calc_face_angle(0.0)) > 70.0 for e in hand_edges)
    after_gt45 = sum(math.degrees(e.calc_face_angle(0.0)) > 45.0 for e in hand_edges)
    after_max = max((math.degrees(e.calc_face_angle(0.0)) for e in hand_edges), default=0.0)
    print(
        f"=== HAND CLEANUP: del_knuckles={del_knuckles}, trimmed_nails={trimmed_nails}, del_faces={len(to_del)}, "
        f">70deg: {before_gt70} -> {after_gt70}, >45deg: {after_gt45}, maxDeg: {after_max:.1f}"
    )

    bm.to_mesh(body.data)
    bm.free()

    # Сбрасываем старые жёсткие split-нормали из исходного Tripo FBX, чтобы Unity взял идеально гладкие нормали!
    bpy.ops.object.select_all(action="DESELECT")
    body.select_set(True)
    bpy.context.view_layer.objects.active = body
    try:
        bpy.ops.mesh.customdata_custom_splitnormals_clear()
    except Exception:
        pass
    body.data.update()


def export_rigged_fbx(arm, body, filename):
    tmp_fbx = os.path.join(os.environ.get("TEMP", r"C:\Windows\Temp"), filename)
    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    body.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.fbx(
        filepath=tmp_fbx,
        use_selection=True,
        object_types={"ARMATURE", "MESH"},
        axis_forward="-Z",
        axis_up="Y",
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL",
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        add_leaf_bones=False,
        bake_anim=False,
        path_mode="AUTO",
        embed_textures=False,
    )
    for d in UNITY_CHARS_DIRS:
        shutil.copy2(tmp_fbx, os.path.join(d, filename))
        print(f"=== Synced {filename} ({os.path.getsize(tmp_fbx)//1024} KB) to {d}")


def main():
    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    for m in list(bpy.data.meshes):
        bpy.data.meshes.remove(m)
    for mat in list(bpy.data.materials):
        bpy.data.materials.remove(mat)

    bpy.ops.import_scene.fbx(filepath=MIXAMO_BASE_FBX)
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    body = next(o for o in bpy.data.objects if o.type == "MESH")

    # 1. Сначала выглаживаем глубокие канавки костяшек и впалые ногти на кистях и сохраняем BaseMale.fbx
    #    (для всех остальных соперников за столом)!
    smooth_hand_creases(body)
    export_rigged_fbx(arm, body, "BaseMale.fbx")

    # Загружаем BodyMale_BaseColor.png, чтобы найти точную наклонную плоскость перехода синего ворота в кожу
    col_img = bpy.data.images.load(BODY_COL)
    cw, ch = col_img.size[0], col_img.size[1]
    buf = np.empty(cw * ch * 4, dtype=np.float32)
    col_img.pixels.foreach_get(buf)
    col_arr = buf.reshape((ch, cw, 4))
    bpy.data.images.remove(col_img)

    uv_layer = body.data.uv_layers.active.data
    vert_uv = {}
    for p in body.data.polygons:
        if p.material_index != 0:
            continue
        for li in p.loop_indices:
            vi = body.data.loops[li].vertex_index
            if vi not in vert_uv:
                vert_uv[vi] = uv_layer[li].uv[:]

    mw = body.matrix_world
    mw_inv = mw.inverted()

    # Собираем все точки нижней границы кожи шеи (в мировых координатах Blender) и подгоняем идеальную плоскость среза!
    neck_skin_pts = []
    for v in body.data.vertices:
        if v.index not in vert_uv:
            continue
        wp = mw @ v.co
        if wp.z < 1.46 or wp.z > 1.57 or abs(wp.x) > 0.11:
            continue
        u, vv = vert_uv[v.index]
        r, g, b = col_arr[min(ch - 1, max(0, int((vv % 1) * (ch - 1)))), min(cw - 1, max(0, int((u % 1) * (cw - 1)))), :3]
        if (r > 0.48) and (r > b + 0.02):
            neck_skin_pts.append((wp.x, wp.y, wp.z))

    pts = np.array(neck_skin_pts)
    # Для каждого сектора вокруг шеи берём минимальную Z кожи -> получаем точную наклонную плоскость самого низа кожи
    ys = np.linspace(-0.055, 0.070, 8)
    fit_y, fit_z = [], []
    for y0 in ys:
        sl = pts[(pts[:, 1] >= y0 - 0.012) & (pts[:, 1] <= y0 + 0.012)]
        if len(sl) >= 3:
            fit_y.append(float(sl[:, 1].mean()))
            fit_z.append(float(np.percentile(sl[:, 2], 5)))
    slope_y, intercept_z = np.polyfit(fit_y, fit_z, 1)
    # Сдвигаем плоскость на 6 мм ВНИЗ в синюю вязку — гарантированно 0 светлых зубцов/полосок кожи на кромке ворота!
    plane_co_w = Vector((0.0, 0.012, float(intercept_z + slope_y * 0.012 - 0.006)))
    plane_no_w = Vector((0.0, -float(slope_y), 1.0)).normalized()
    print(f"=== COLLAR PLANE: slope_y={slope_y:.3f}, co={tuple(round(c,4) for c in plane_co_w)}, no={tuple(round(c,4) for c in plane_no_w)}")

    # Находим гарантированно чистую точку глубокого БИРЮЗОВОГО трикотажа (не тёмных джинсов!):
    # у свитера насыщенный бирюзовый оттенок (g > r*2.0, b > r*2.2, g в диапазоне 0.08..0.24) — 40x40 px без примесей.
    teal_mask = (
        (col_arr[:, :, 1] > col_arr[:, :, 0] * 2.1)
        & (col_arr[:, :, 2] > col_arr[:, :, 0] * 2.3)
        & (col_arr[:, :, 1] > 0.07)
        & (col_arr[:, :, 1] < 0.22)
    )
    dys, dxs = np.where(teal_mask)
    inner_u, inner_v = 0.7949, 0.1035
    if len(dxs):
        best_lum = 9.0
        for idx in np.linspace(0, len(dxs) - 1, min(600, len(dxs)), dtype=int):
            py, px = int(dys[idx]), int(dxs[idx])
            if py < 24 or py >= ch - 24 or px < 24 or px >= cw - 24:
                continue
            patch = col_arr[py - 20:py + 20, px - 20:px + 20, :3]
            if (patch[:, :, 1] > patch[:, :, 0] * 1.9).all() and (patch[:, :, 2] > patch[:, :, 0] * 2.0).all():
                lum = float(patch.mean())
                if lum < best_lum:
                    best_lum = lum
                    inner_u, inner_v = px / cw, py / ch
    sample_rgb = col_arr[int(inner_v * (ch - 1)), int(inner_u * (cw - 1)), :3]
    print(f"=== INNER COLLAR DARK TEAL UV: ({inner_u:.4f}, {inner_v:.4f}), RGB={sample_rgb.round(3)}")

    # Переводим плоскость в локальные координаты меша body (ведь body повернут на +90° X из FBX!)
    plane_co_l = mw_inv @ plane_co_w
    plane_no_l = (mw_inv.to_3x3() @ plane_no_w).normalized()

    bm = bmesh.new()
    bm.from_mesh(body.data)
    bm.verts.ensure_lookup_table()
    bm.edges.ensure_lookup_table()
    bm.faces.ensure_lookup_table()
    uv_lay = bm.loops.layers.uv.active
    deform_lay = bm.verts.layers.deform.active

    # Индексы групп вершин для строго правильного скиннинга ворота (0% Head — ворот не перекашивается при повороте головы!).
    vg_idx = {g.name: g.index for g in body.vertex_groups}
    vg_spine2 = vg_idx.get("mixamorig:Spine2")
    vg_neck = vg_idx.get("mixamorig:Neck")
    vg_head = vg_idx.get("mixamorig:Head")

    # Режем ТОЛЬКО область головы/шеи (|x| < 0.14, world z > 1.44), чтобы плечи остались нетронутыми!
    neck_faces = [f for f in bm.faces if f.material_index == 0 and any((mw @ v.co).z > 1.44 and abs((mw @ v.co).x) < 0.14 for v in f.verts)]
    neck_edges = list({e for f in neck_faces for e in f.edges})
    neck_verts = list({v for f in neck_faces for v in f.verts})

    res = bmesh.ops.bisect_plane(
        bm,
        geom=neck_verts + neck_edges + neck_faces,
        plane_co=plane_co_l,
        plane_no=plane_no_l,
        clear_outer=True,
        clear_inner=False,
    )
    cut_edges = [g for g in res["geom_cut"] if isinstance(g, bmesh.types.BMEdge)]
    cut_verts = [g for g in res["geom_cut"] if isinstance(g, bmesh.types.BMVert)]
    if cut_verts:
        ring_w = np.array([(mw @ v.co)[:] for v in cut_verts])
        ring_center_w = Vector(ring_w.mean(axis=0))
        print(f"=== COLLAR CUT RING: verts={len(cut_verts)}, center={tuple(round(c,4) for c in ring_center_w)}, spanX={np.ptp(ring_w[:,0]):.3f}, spanY={np.ptp(ring_w[:,1]):.3f}")
    else:
        ring_center_w = plane_co_w

    # Убираем вес кости Head со всего оставшегося ворота (переводим в 72% Spine2 + 28% Neck)
    if deform_lay is not None and vg_head is not None and vg_spine2 is not None:
        for v in bm.verts:
            d = v[deform_lay]
            hw = d.get(vg_head, 0.0)
            if hw > 0.0:
                del d[vg_head]
                d[vg_spine2] = d.get(vg_spine2, 0.0) + hw * 0.72
                if vg_neck is not None:
                    d[vg_neck] = d.get(vg_neck, 0.0) + hw * 0.28
                else:
                    d[vg_spine2] += hw * 0.28

    # Сначала очищаем остаточные «кожные» UV на верхней кромке свитера ДО экструзии, копируя чистую текстуру соседней резинки!
    if uv_lay is not None:
        recolored = 0
        for f in bm.faces:
            if f.material_index != 0:
                continue
            fc_w = mw @ f.calc_center_median()
            if fc_w.z > 1.465 and abs(fc_w.x) < 0.13:
                for loop in f.loops:
                    u, vv = loop[uv_lay].uv
                    r, g, b = col_arr[min(ch - 1, max(0, int((vv % 1) * (ch - 1)))), min(cw - 1, max(0, int((u % 1) * (cw - 1)))), :3]
                    if r > 0.26 and r > b + 0.02:
                        # Ищем ближайший чистый бирюзовый UV на соседних рёбрах этой же вершины, чтобы сохранить полоски вязки
                        best_uv = (inner_u, inner_v)
                        for lf in loop.vert.link_faces:
                            for ll in lf.loops:
                                lu, lv = ll[uv_lay].uv
                                lr, lg, lb = col_arr[min(ch - 1, max(0, int((lv % 1) * (ch - 1)))), min(cw - 1, max(0, int((lu % 1) * (cw - 1)))), :3]
                                if lg > lr * 1.6 and lb > lr * 1.7:
                                    best_uv = (lu, lv)
                                    break
                        loop[uv_lay].uv = best_uv
                        recolored += 1
        print(f"=== CLEANED SKIN-EDGE COLLAR LOOPS (PRE-EXTRUDE): {recolored}")

    # Строим стильный объёмный вязаный ворот-стойку (mock-neck ~2.8 см) из той же бирюзовой резинки:
    #   - Внешняя стенка (rx=6.8 см, ry=6.9 см вокруг оси шеи y=+0.005) чисто охватывает шеи ВСЕХ 6 друзей (r=6.0..6.5 см) без проступания;
    #   - Округлый толстый верхний кант плавно заворачивается ВНУТРЬ шеи (rx=5.3 см) — 0 щелей, 0 шва кожа-к-коже, 0 чёрной ямы!
    #   - Верхняя часть ворота привязана к mixamorig:Neck (до 85%), поэтому при любом повороте/наклоне головы ворот гнётся вместе с шеей!
    if cut_edges:
        neck_ctr_xy = Vector((0.0, 0.005, 0.0))

        def shape_mock_ring(verts, blend_to_ellipse, rx, ry, lift_z, slope_y_factor=0.30):
            for v in verts:
                wp = mw @ v.co
                dx = wp.x - neck_ctr_xy.x
                dy = wp.y - neck_ctr_xy.y
                ang = math.atan2(dy, dx)
                tx = neck_ctr_xy.x + math.cos(ang) * rx
                ty = neck_ctr_xy.y + math.sin(ang) * ry
                nx = wp.x + (tx - wp.x) * blend_to_ellipse
                ny = wp.y + (ty - wp.y) * blend_to_ellipse
                # Высота: аккуратно выше у затылка и чуть ниже спереди (чтобы оставалась открытая шея под подбородком и бородой Аслана!)
                base_z = 1.509 + slope_y_factor * (ny - 0.005)
                nz = max(wp.z, base_z) if lift_z == 0.0 else (base_z + lift_z)
                v.co = mw_inv @ Vector((nx, ny, nz))

        def extrude_ring(ring_edges):
            ex = bmesh.ops.extrude_edge_only(bm, edges=ring_edges)
            vs = [g for g in ex["geom"] if isinstance(g, bmesh.types.BMVert)]
            vset = set(vs)
            es = [g for g in ex["geom"] if isinstance(g, bmesh.types.BMEdge) and g.verts[0] in vset and g.verts[1] in vset]
            fs = [g for g in ex["geom"] if isinstance(g, bmesh.types.BMFace)]
            return vs, es, fs

        # Сохраняем эталонные чистые UV СТРОГО ПО РЁБРАМ (edge -> (uv_v0, uv_v1) из родительской грани свитера!),
        # чтобы через UV-швы на плечах ни один квад не растягивался по диагонали атласа!
        edge_uv = {}
        if uv_lay is not None:
            for e in cut_edges:
                v0, v1 = e.verts[0], e.verts[1]
                parent_f = e.link_faces[0] if e.link_faces else None
                if parent_f is not None:
                    uv0 = uv1 = None
                    for ll in parent_f.loops:
                        if ll.vert == v0:
                            uv0 = ll[uv_lay].uv.copy()
                        elif ll.vert == v1:
                            uv1 = ll[uv_lay].uv.copy()
                    if uv0 is not None and uv1 is not None:
                        edge_uv[(v0, v1)] = (uv0, uv1)
                        edge_uv[(v1, v0)] = (uv1, uv0)

        m1_v, m1_e, m1_f = extrude_ring(cut_edges)
        shape_mock_ring(m1_v, blend_to_ellipse=0.58, rx=0.071, ry=0.072, lift_z=0.011, slope_y_factor=0.34)

        m2_v, m2_e, m2_f = extrude_ring(m1_e)
        shape_mock_ring(m2_v, blend_to_ellipse=0.92, rx=0.068, ry=0.069, lift_z=0.023, slope_y_factor=0.30)

        m3_v, m3_e, m3_f = extrude_ring(m2_e)
        shape_mock_ring(m3_v, blend_to_ellipse=1.00, rx=0.064, ry=0.065, lift_z=0.029, slope_y_factor=0.28)

        # Толстый мягкий подворот кромки внутрь шеи (rx=5.3 см < 6.0 см шеи -> 100% бесшовное облегание!)
        m4_v, m4_e, m4_f = extrude_ring(m3_e)
        shape_mock_ring(m4_v, blend_to_ellipse=1.00, rx=0.053, ry=0.053, lift_z=0.025, slope_y_factor=0.26)

        m5_v, m5_e, m5_f = extrude_ring(m4_e)
        shape_mock_ring(m5_v, blend_to_ellipse=1.00, rx=0.042, ry=0.042, lift_z=0.006, slope_y_factor=0.20)

        cap_faces = []
        cap_v = None
        if m5_e:
            cap_v = bm.verts.new(mw_inv @ Vector((0.0, 0.005, 1.500)))
            for e in m5_e:
                try:
                    f = bm.faces.new((e.verts[0], e.verts[1], cap_v))
                    f.material_index = 0
                    cap_faces.append(f)
                except ValueError:
                    pass

        # Плавные веса Spine2 -> Neck (0% Head): верх ворота органично сгибается и поворачивается вслед за шеей!
        if deform_lay is not None and vg_spine2 is not None:
            for grp, w_sp, w_nk in (
                (m1_v, 0.55, 0.45),
                (m2_v, 0.32, 0.68),
                (m3_v, 0.16, 0.84),
                (m4_v, 0.14, 0.86),
                (m5_v + ([cap_v] if cap_v is not None else []), 0.25, 0.75),
            ):
                for v in grp:
                    d = v[deform_lay]
                    d.clear()
                    if vg_neck is not None:
                        d[vg_spine2] = w_sp
                        d[vg_neck] = w_nk
                    else:
                        d[vg_spine2] = 1.0

        # Копируем UV СТРОГО по колонкам квадов (base edge -> top edge) — 0 растяжек на UV-швах и 100% живая вертикальная резинка!
        all_outer_mock = m1_f + m2_f + m3_f + m4_f
        for f in all_outer_mock:
            f.material_index = 0
            f.smooth = True
        if uv_lay is not None:
            cur_edge_uv = dict(edge_uv)
            for ring_faces, top_v_list in ((m1_f, m1_v), (m2_f, m2_v), (m3_f, m3_v), (m4_f, m4_v)):
                top_set = set(top_v_list)
                next_edge_uv = {}
                for f in ring_faces:
                    bot_verts = [v for v in f.verts if v not in top_set]
                    top_verts = [v for v in f.verts if v in top_set]
                    if len(bot_verts) == 2 and len(top_verts) == 2:
                        b0, b1 = bot_verts[0], bot_verts[1]
                        pair = cur_edge_uv.get((b0, b1))
                        if pair is None:
                            pair = (Vector((inner_u, inner_v)), Vector((inner_u, inner_v)))
                        uv0, uv1 = pair
                        # Сопоставляем каждую верхнюю вершину с её нижней по вертикальному ребру этого квада
                        t_for = {}
                        for tv in top_verts:
                            for e in f.edges:
                                ov = e.other_vert(tv) if tv in e.verts else None
                                if ov == b0:
                                    t_for[tv] = (b0, uv0)
                                elif ov == b1:
                                    t_for[tv] = (b1, uv1)
                        for ll in f.loops:
                            if ll.vert == b0:
                                ll[uv_lay].uv = uv0
                            elif ll.vert == b1:
                                ll[uv_lay].uv = uv1
                            elif ll.vert in t_for:
                                ll[uv_lay].uv = t_for[ll.vert][1]
                            else:
                                ll[uv_lay].uv = (inner_u, inner_v)
                        if len(t_for) == 2:
                            t0, t1 = top_verts[0], top_verts[1]
                            next_edge_uv[(t0, t1)] = (t_for[t0][1], t_for[t1][1])
                            next_edge_uv[(t1, t0)] = (t_for[t1][1], t_for[t0][1])
                cur_edge_uv = next_edge_uv

        for f in m5_f + cap_faces:
            f.material_index = 0
            f.smooth = True
            if uv_lay is not None:
                for loop in f.loops:
                    loop[uv_lay].uv = (inner_u, inner_v)

    # Триангулируем случайные n-гоны и выравниваем нормали всего свитера к единой наружной ориентации!
    ngons = [f for f in bm.faces if len(f.verts) > 4]
    if ngons:
        bmesh.ops.triangulate(bm, faces=ngons)

    outfit_faces = [f for f in bm.faces if f.material_index != 1]
    bmesh.ops.recalc_face_normals(bm, faces=outfit_faces)
    bm.normal_update()
    bm.to_mesh(body.data)
    bm.free()
    body.data.update()

    # Экспортируем BaseMale_Headless.fbx с ТЕМ ЖЕ скелетом Mixamo (add_leaf_bones=False!)
    export_rigged_fbx(arm, body, "BaseMale_Headless.fbx")

    # Контрольный рендер Марата в новом идеально завёрнутом вязаном вороте!
    bpy.ops.import_scene.fbx(filepath=HEAD_MARAT_FBX)
    head = next(o for o in bpy.data.objects if o.type == "MESH" and o != body)
    head.location = HEAD_PIVOT

    hmat = bpy.data.materials.new("MaratPreview")
    hmat.use_nodes = True
    hnt = hmat.node_tree
    hbsdf = next(n for n in hnt.nodes if n.type == "BSDF_PRINCIPLED")
    htc = hnt.nodes.new("ShaderNodeTexImage")
    htc.image = bpy.data.images.load(HEAD_MARAT_COL)
    hnt.links.new(htc.outputs["Color"], hbsdf.inputs["Base Color"])
    htn = hnt.nodes.new("ShaderNodeTexImage")
    htn.image = bpy.data.images.load(HEAD_MARAT_NRM)
    htn.image.colorspace_settings.name = "Non-Color"
    hnrm = hnt.nodes.new("ShaderNodeNormalMap")
    hnt.links.new(htn.outputs["Color"], hnrm.inputs["Color"])
    hnt.links.new(hnrm.outputs["Normal"], hbsdf.inputs["Normal"])
    hbsdf.inputs["Roughness"].default_value = 0.72
    head.data.materials[0] = hmat

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
        sc.render.filepath = os.path.join(MARAT_PREVIEW_DIR, fname)
        bpy.ops.render.render(write_still=True)

    snap((0.0, -1.05, 1.62), (0.0, 0.0, 1.58), "превью_марат_перед.png")
    snap((0.68, -0.82, 1.64), (0.0, 0.0, 1.57), "превью_марат_3_4.png")
    print("=== BUILD HEADLESS BODY DONE")


if __name__ == "__main__":
    main()
