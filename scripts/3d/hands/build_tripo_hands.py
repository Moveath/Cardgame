"""Сборка игровых рук Card City из открытой 3D-модели Tripo3D (2026-11-23):
- Исходник: references/hands/открытая-рука/tripo-model/f082b78c-6656-4a3c-b16e-beee5150e7ff.fbx (чистая quad-сетка, 5 раздельных пальцев, 4K текстура кожи и вязаной манжеты).
- Правая рука + точная зеркальная левая рука (Arm_Male_R/L, Sleeve_R/L) на едином анатомическом скелете Rig_Hands с плавными
  весами без угловатых заломов (макс. 4 кости на вершину, изоляция между пальцами, плавный ForearmTwist).
- Вязаный рукав бесшовно продлён до локтя с чистой UV-развёрткой внутри области вязаного свитера (без полос кожи).
- Все игровые позы (Pose_Base, Pose_InGame, Pose_TakeCard и др.) и клипы анимаций (Take_Idle, Take_TableRest, Take_HoldCards,
  Take_ThumbsUp, Take_ThumbsDown, Take_Fist, Take_Point, Take_TapTable, Take_Shrug, Take_DrawCard, Take_PlayCard,
  Take_TestClench, Take_TestThumbsUp*, Take_TestThumbsDown*) совместимы с HandsAnimatorBuilder и OwnArms3DView.

Запуск:
  blender --background --factory-startup --disable-autoexec --python-exit-code 1 --python scripts/3d/hands/build_tripo_hands.py -- preview
  blender --background --factory-startup --disable-autoexec --python-exit-code 1 --python scripts/3d/hands/build_tripo_hands.py -- build
"""
import copy
import math
import os
import sys
import time

import bpy
import bmesh
import numpy as np
from mathutils import Euler, Matrix, Vector
from mathutils.bvhtree import BVHTree

sys.path.insert(0, os.path.dirname(__file__))
import in_game  # noqa: E402
import source  # noqa: E402

OPEN_FBX = os.path.join(source.ROOT, "references", "hands", "открытая-рука", "tripo-model", "f082b78c-6656-4a3c-b16e-beee5150e7ff.fbx")
REF_FBX = os.path.join(source.ROOT, "references", "hands", "жесты", "класс-модель", "Hand_Right_ThumbsUp.fbx")
SWEATER_TILE = os.path.join(source.ROOT, "unity", "CardGameClient", "Assets", "Resources", "ThreeD", "Hands", "TripoSweater_Tile.jpg")
OUT_BLEND = os.path.join(source.ROOT, "art3d", "hands", "TripoHands.blend")
OUT_FBX = os.path.join(source.ROOT, "art3d", "hands", "TripoHands.fbx")

# Масштаб: от запястья (Z=0.285) до кончика среднего пальца (Z=1.000) = 0.715 ед. Tripo -> 0.190 м (живая мужская кисть).
SCALE = 0.190 / 0.715
WRIST_TRIPO = np.array([-0.042, 0.028, 0.285], dtype=np.float64)
REST_SHIFT_X = 0.15  # Покой: правая кисть в +0.15 м по X, левая зеркально в -0.15 м.
FPS = 60  # Запекаем все клипы на 60 кадрах/с (вдвое плотнее по времени) для абсолютно плавной анимации без покадровости.
FPS_SUB = FPS // 30

# Суставы правой руки в координатах Tripo (X=+Большой, Y=+Тыл/-Ладонь, Z=+Пальцы).
# Точные 3D-центры поперечных сечений каждого пальца по сетке Tripo (ошибка < 0.5 мм, без смещения осей сгиба!).
JOINTS_TRIPO = {
    "Forearm":      [-0.042,  0.032, -0.715],
    "ForearmTwist": [-0.042,  0.030, -0.215],
    "Hand":         [-0.042,  0.028,  0.285],

    "Index_Meta":   [ 0.012,  0.032,  0.345],
    "Index_01":     [ 0.044,  0.039,  0.635],
    "Index_02":     [ 0.065,  0.039,  0.790],
    "Index_03":     [ 0.075,  0.022,  0.881],
    "Index_Tip":    [ 0.078,  0.014,  0.959],

    "Middle_Meta":  [-0.038,  0.033,  0.345],
    "Middle_01":    [-0.044,  0.039,  0.640],
    "Middle_02":    [-0.046,  0.038,  0.805],
    "Middle_03":    [-0.054,  0.025,  0.914],
    "Middle_Tip":   [-0.062,  0.019,  0.999],

    "Ring_Meta":    [-0.078,  0.030,  0.345],
    "Ring_01":      [-0.125,  0.036,  0.628],
    "Ring_02":      [-0.138,  0.032,  0.785],
    "Ring_03":      [-0.151,  0.016,  0.874],
    "Ring_Tip":     [-0.157,  0.007,  0.949],

    "Pinky_Meta":   [-0.112,  0.022,  0.345],
    "Pinky_01":     [-0.196,  0.021,  0.580],
    "Pinky_02":     [-0.236,  0.018,  0.688],
    "Pinky_03":     [-0.261,  0.000,  0.760],
    "Pinky_Tip":    [-0.279, -0.020,  0.828],

    "Thumb_01":     [ 0.045, -0.015,  0.355],
    "Thumb_02":     [ 0.140, -0.062,  0.495],
    "Thumb_03":     [ 0.212, -0.102,  0.580],
    "Thumb_Tip":    [ 0.296, -0.120,  0.626],
}


def tripo_to_hand(pt, side="_R"):
    """Tripo -> покой руки в метрах: +Y вперёд по пальцам, +Z тыл вверх, для _R большой палец в -X (+REST_SHIFT_X),
    для _L — зеркально (большой палец в +X, смещение -REST_SHIFT_X)."""
    p = np.asarray(pt, dtype=np.float64) - WRIST_TRIPO
    x = -p[0] * SCALE
    y = p[2] * SCALE
    z = p[1] * SCALE
    if side == "_L":
        return Vector((-x - REST_SHIFT_X, y, z))
    return Vector((x + REST_SHIFT_X, y, z))


def smoothstep(a, b, x):
    t = np.clip((x - a) / max(b - a, 1e-9), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def pt_seg_param_dist(pts, a, b):
    ab = b - a
    L = np.linalg.norm(ab)
    u = ab / max(L, 1e-9)
    proj = (pts - a) @ u
    t = np.clip(proj / max(L, 1e-9), 0.0, 1.0)
    closest = a + (t * L)[:, None] * u
    dist = np.linalg.norm(pts - closest, axis=1)
    return t, dist, proj, L


def find_clean_wool_uv_box(image):
    """Находит в 4K текстуре Tripo самый большой чистый прямоугольник вязаного тёмно-бирюзового свитера (без пикселей кожи),
    чтобы продлённый до локтя рукав тайлился строго по вязаной ткани без светлых полос."""
    tmp = image.copy()
    tmp.scale(256, 256)
    w, h = tmp.size
    px = np.empty(w * h * 4, dtype=np.float32)
    tmp.pixels.foreach_get(px)
    px = px.reshape(h, w, 4)
    bpy.data.images.remove(tmp)
    # Вязаная сине-зелёная шерсть: синий и зелёный явно выше красного, и не серый фон/кожа!
    r, g, b = px[:, :, 0], px[:, :, 1], px[:, :, 2]
    wool = (b > r + 0.04) & (g > r + 0.02) & (r < 0.25) & (b > 0.06)
    # Отступаем на 4 пикселя от границ, чтобы при билинейной фильтрации в Unity кожа никогда не подмешивалась!
    safe = wool.copy()
    for _ in range(4):
        s2 = safe.copy()
        s2[1:, :] &= safe[:-1, :]
        s2[:-1, :] &= safe[1:, :]
        s2[:, 1:] &= safe[:, :-1]
        s2[:, :-1] &= safe[:, 1:]
        s2[0, :] = s2[-1, :] = s2[:, 0] = s2[:, -1] = False
        safe = s2
    # Находим максимальный чистый прямоугольник в матрице safe (классический алгоритм гистограммы)
    best_area = 0
    best_rect = (0.1, 0.1, 0.2, 0.2)
    heights = np.zeros(w, dtype=np.int32)
    for y in range(h):
        heights = np.where(safe[y], heights + 1, 0)
        stack = []
        for x in range(w + 1):
            cur_h = int(heights[x]) if x < w else 0
            start = x
            while stack and stack[-1][1] > cur_h:
                sx, sh = stack.pop()
                area = sh * (x - sx)
                if area > best_area:
                    best_area = area
                    best_rect = (sx / w, (y - sh + 1) / h, x / w, (y + 1) / h)
                start = sx
            stack.append((start, cur_h))
    print(f"=== WOOL UV BOX: u=[{best_rect[0]:.3f}..{best_rect[2]:.3f}], v=[{best_rect[1]:.3f}..{best_rect[3]:.3f}] (area={best_area} px)")
    return best_rect


def extend_knitted_sleeve(sleeve_ob, target_y=-0.275, rings=12):
    """Удаляет плоскую донную заглушку манжеты, плавно продлевает вязаный рукав до локтя и кладёт единую
    непрерывную бесшовную цилиндрическую UV-развёртку на ВЕСЬ рукав (манжета + предплечье, 1 материал M_TripoSweater —
    полная совместимость с Unity materialImportMode=0, без единого шва и полос кожи!)."""
    bm = bmesh.new()
    bm.from_mesh(sleeve_ob.data)
    uv_layer = bm.loops.layers.uv.active
    bm.verts.ensure_lookup_table()
    bm.faces.ensure_lookup_table()

    ys = [v.co.y for v in bm.verts]
    y_min = min(ys)
    y_top = max(ys)
    cap_faces = [f for f in bm.faces if f.normal.y < -0.55 and all(v.co.y < y_min + 0.012 for v in f.verts)]
    if cap_faces:
        bmesh.ops.delete(bm, geom=cap_faces, context="FACES")

    bm.edges.ensure_lookup_table()
    bnd_edges = [e for e in bm.edges if e.is_boundary and all(v.co.y < y_min + 0.025 for v in e.verts)]
    bnd_verts = list({v for e in bnd_edges for v in e.verts})
    if not bnd_edges:
        bm.free()
        return

    loop_pts = np.array([tuple(v.co) for v in bnd_verts])
    cx, cz = loop_pts[:, 0].mean(), loop_pts[:, 2].mean()
    y0 = loop_pts[:, 1].mean()

    cur_edges = bnd_edges
    base_info = {v: Vector((v.co.x - cx, 0.0, v.co.z - cz)) for v in bnd_verts}

    for r_i in range(1, rings + 1):
        frac = r_i / rings
        flare = 1.0 + 0.34 * (frac * frac * (3.0 - 2.0 * frac))
        y_new = y0 + (target_y - y0) * frac

        ret = bmesh.ops.extrude_edge_only(bm, edges=cur_edges)
        new_geom = ret["geom"]
        new_verts = [g for g in new_geom if isinstance(g, bmesh.types.BMVert)]
        new_edges = [g for g in new_geom if isinstance(g, bmesh.types.BMEdge) and all(v in new_verts for v in g.verts)]

        new_info = {}
        for nv in new_verts:
            parent = next((e.other_vert(nv) for e in nv.link_edges if e.other_vert(nv) in base_info), None)
            if parent is not None:
                off_xz = base_info[parent]
                nv.co = Vector((cx + off_xz.x * flare, y_new, cz + off_xz.z * flare))
                new_info[nv] = off_xz

        cur_edges = new_edges
        base_info = new_info

    # Единая бесшовная цилиндрическая UV-развёртка на ВСЕ полигоны рукава (от верхнего края манжеты до локтя!)
    u_reps = 3.0   # 3 целых повтора бесшовной вязки по окружности (шов = 0)
    v_scale = 5.0  # шаг петель вдоль рукава, совпадающий с плечом (Upper arm)
    for f in bm.faces:
        f.material_index = 0
        angs = [(math.atan2(l.vert.co.z - cz, l.vert.co.x - cx) + math.pi) / (2.0 * math.pi) for l in f.loops]
        wrap = (max(angs) - min(angs)) > 0.5
        for l, a in zip(f.loops, angs):
            a_use = a + 1.0 if (wrap and a < 0.5) else a
            l[uv_layer].uv = Vector((a_use * u_reps, (y_top - l.vert.co.y) * v_scale))

    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(sleeve_ob.data)
    bm.free()
    sleeve_ob.data.polygons.foreach_set("use_smooth", [True] * len(sleeve_ob.data.polygons))
    sleeve_ob.data.update()


def mirror_mesh_to_left(ob_r, name_l):
    """Зеркальная копия правой сетки в левую (X -> -X, нормали наружу, имена групп вершин _R -> _L)."""
    ob_l = ob_r.copy()
    ob_l.data = ob_r.data.copy()
    ob_l.name = name_l
    ob_l.data.name = name_l + "_Mesh"
    bpy.context.scene.collection.objects.link(ob_l)

    bm = bmesh.new()
    bm.from_mesh(ob_l.data)
    for v in bm.verts:
        v.co.x = -v.co.x
    bmesh.ops.reverse_faces(bm, faces=bm.faces)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(ob_l.data)
    bm.free()
    ob_l.data.polygons.foreach_set("use_smooth", [True] * len(ob_l.data.polygons))
    ob_l.data.update()

    for vg in ob_l.vertex_groups:
        if vg.name.endswith("_R"):
            vg.name = vg.name[:-2] + "_L"
    return ob_l


def build_rig_and_meshes():
    """Импортирует открытую руку Tripo3D, чистит внутренний пузырь, продлевает рукав, строит скелет Rig_Hands
    и мягкие веса для обеих рук (Arm_Male_R/L, Sleeve_R/L)."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=OPEN_FBX)
    src_ob = next(o for o in bpy.data.objects if o.type == "MESH")
    src_mesh = src_ob.data
    src_world = src_ob.matrix_world.copy()

    tex_img = next(n.image for n in src_ob.active_material.node_tree.nodes if n.type == "TEX_IMAGE" and n.image)
    wool_uv_box = find_clean_wool_uv_box(tex_img)

    co_all = np.array([tuple(src_world @ v.co) for v in src_mesh.vertices], dtype=np.float64)
    adj = [[] for _ in range(len(co_all))]
    for e in src_mesh.edges:
        a, b = e.vertices
        adj[a].append(b); adj[b].append(a)
    comp_id = np.full(len(co_all), -1, dtype=np.int32)
    comps = []
    for i in range(len(co_all)):
        if comp_id[i] == -1:
            cid = len(comps); st = [i]; comp_id[i] = cid; vs = []
            while st:
                u = st.pop(); vs.append(u)
                for v in adj[u]:
                    if comp_id[v] == -1:
                        comp_id[v] = cid; st.append(v)
            comps.append(np.array(vs, dtype=np.int32))
    order = sorted(range(len(comps)), key=lambda c: len(comps[c]), reverse=True)
    skin_cid, cuff_cid = order[0], order[1]
    extra_cids = order[2:]

    skin_set = set(comps[skin_cid].tolist())
    skin_faces_tripo = [tuple(p.vertices) for p in src_mesh.polygons if all(v in skin_set for v in p.vertices)]
    skin_bvh = BVHTree.FromPolygons([Vector(p) for p in co_all], skin_faces_tripo)
    keep_skin = {skin_cid}
    for ec in extra_cids:
        ctr = Vector(co_all[comps[ec]].mean(0))
        hits = sum(1 for d in ((1,0,0),(-1,0,0),(0,1,0),(0,-1,0),(0,0,1),(0,0,-1)) if skin_bvh.ray_cast(ctr, Vector(d))[0] is not None)
        if hits < 5:
            keep_skin.add(ec)

    src_ob.matrix_world.identity()
    for i, v in enumerate(src_mesh.vertices):
        v.co = tripo_to_hand(co_all[i], "_R")
    src_mesh.update()

    def extract(name, cids):
        ob = src_ob.copy()
        ob.data = src_mesh.copy()
        ob.name = name
        ob.data.name = name + "_Mesh"
        bpy.context.scene.collection.objects.link(ob)
        bm = bmesh.new()
        bm.from_mesh(ob.data)
        bm.verts.ensure_lookup_table()
        kill = [v for i, v in enumerate(bm.verts) if comp_id[i] not in cids]
        bmesh.ops.delete(bm, geom=kill, context="VERTS")
        bm.to_mesh(ob.data)
        bm.free()
        ob.data.polygons.foreach_set("use_smooth", [True] * len(ob.data.polygons))
        ob.data.update()
        return ob

    hand_r = extract("Arm_Male_R", keep_skin)
    sleeve_r = extract("Sleeve_R", {cuff_cid})
    bpy.data.objects.remove(src_ob, do_unlink=True)

    # Материалы: кожа (TripoHands_BaseColor) и единый бесшовный вязаный свитер (TripoSweater_Tile) на весь рукав!
    mat_skin = hand_r.active_material
    mat_skin.name = "M_TripoSkin"
    bsdf_s = next((n for n in mat_skin.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
    if bsdf_s:
        bsdf_s.inputs["Roughness"].default_value = 0.72
        if "Specular IOR Level" in bsdf_s.inputs:
            bsdf_s.inputs["Specular IOR Level"].default_value = 0.22
    mat_wool = mat_skin.copy()
    mat_wool.name = "M_TripoSweater"
    bsdf_w = next((n for n in mat_wool.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
    if bsdf_w:
        bsdf_w.inputs["Roughness"].default_value = 0.96
        if "Specular IOR Level" in bsdf_w.inputs:
            bsdf_w.inputs["Specular IOR Level"].default_value = 0.04
    if os.path.exists(SWEATER_TILE):
        tile_img = bpy.data.images.load(SWEATER_TILE)
        tex_n = next((n for n in mat_wool.node_tree.nodes if n.type == "TEX_IMAGE"), None)
        if tex_n:
            tex_n.image = tile_img
    sleeve_r.data.materials.clear()
    sleeve_r.data.materials.append(mat_wool)

    # Мягко расширяем и чуть поднимаем верхний край вязаной манжеты (+9 мм), чтобы полностью скрыть
    # тёмный стык текстуры у запястья Tripo и исключить пробивание кожи сквозь манжету при сгибе кисти!
    bm_c = bmesh.new()
    bm_c.from_mesh(sleeve_r.data)
    c_pts = np.array([tuple(v.co) for v in bm_c.verts])
    c_cx, c_cz = c_pts[:, 0].mean(), c_pts[:, 2].mean()
    for v in bm_c.verts:
        w_top = float(smoothstep(-0.045, -0.004, v.co.y))
        if w_top > 0.0:
            v.co.x = c_cx + (v.co.x - c_cx) * (1.0 + 0.068 * w_top)
            v.co.z = c_cz + (v.co.z - c_cz) * (1.0 + 0.068 * w_top)
            v.co.y += 0.009 * w_top
    bm_c.to_mesh(sleeve_r.data)
    bm_c.free()
    sleeve_r.data.update()

    extend_knitted_sleeve(sleeve_r, target_y=-0.275, rings=12)

    # Скелет Rig_Hands для обеих рук (_R и _L)
    arm_data = bpy.data.armatures.new("Rig_Hands")
    arm_ob = bpy.data.objects.new("Rig_Hands", arm_data)
    bpy.context.scene.collection.objects.link(arm_ob)
    bpy.context.view_layer.objects.active = arm_ob
    bpy.ops.object.mode_set(mode="EDIT")
    eb = arm_data.edit_bones

    def add_bone(name, head, tail, parent=None, z_hint=Vector((0, 0, 1)), deform=True):
        b = eb.new(name)
        b.head = head.copy()
        b.tail = tail.copy()
        b.use_deform = deform
        if parent is not None:
            b.parent = eb[parent]
        b.align_roll(z_hint)
        return b

    add_bone("Root", Vector((0, -0.27, 0)), Vector((0, -0.22, 0)), None, Vector((0, 0, 1)), deform=False)

    for side in ("_R", "_L"):
        J = {k: tripo_to_hand(v, side) for k, v in JOINTS_TRIPO.items()}
        sx = 1.0 if side == "_R" else -1.0
        add_bone("Forearm" + side, J["Forearm"], J["ForearmTwist"], "Root", Vector((0, 0, 1)))
        add_bone("ForearmTwist" + side, J["ForearmTwist"], J["Hand"], "Forearm" + side, Vector((0, 0, 1)))
        add_bone("Hand" + side, J["Hand"], J["Middle_01"], "ForearmTwist" + side, Vector((0, 0, 1)))
        for f in ("Index", "Middle", "Ring", "Pinky"):
            add_bone(f"Meta{f}{side}", J[f"{f}_Meta"], J[f"{f}_01"], "Hand" + side, Vector((0, 0, 1)))
            add_bone(f"{f}01{side}", J[f"{f}_01"], J[f"{f}_02"], f"Meta{f}{side}", Vector((0, 0, 1)))
            add_bone(f"{f}02{side}", J[f"{f}_02"], J[f"{f}_03"], f"{f}01{side}", Vector((0, 0, 1)))
            add_bone(f"{f}03{side}", J[f"{f}_03"], J[f"{f}_Tip"], f"{f}02{side}", Vector((0, 0, 1)))
            td = (J[f"{f}_Tip"] - J[f"{f}_03"]).normalized()
            add_bone(f"Tip{f}{side}", J[f"{f}_Tip"] - td * 0.008, J[f"{f}_Tip"], f"{f}03{side}", Vector((0, 0, 1)), deform=False)
        thumb_z = Vector((-0.55 * sx, 0.15, 0.82)).normalized()
        add_bone("Thumb01" + side, J["Thumb_01"], J["Thumb_02"], "Hand" + side, thumb_z)
        add_bone("Thumb02" + side, J["Thumb_02"], J["Thumb_03"], "Thumb01" + side, thumb_z)
        add_bone("Thumb03" + side, J["Thumb_03"], J["Thumb_Tip"], "Thumb02" + side, thumb_z)
        td = (J["Thumb_Tip"] - J["Thumb_03"]).normalized()
        add_bone("TipThumb" + side, J["Thumb_Tip"] - td * 0.008, J["Thumb_Tip"], "Thumb03" + side, thumb_z, deform=False)

    bpy.ops.object.mode_set(mode="OBJECT")

    # Мягкие изолированные веса для правой руки
    assign_hand_weights(hand_r, arm_ob, "_R")
    assign_sleeve_weights(sleeve_r, arm_ob, "_R")

    # Точная зеркальная копия для левой руки
    hand_l = mirror_mesh_to_left(hand_r, "Arm_Male_L")
    sleeve_l = mirror_mesh_to_left(sleeve_r, "Sleeve_L")
    for ob in (hand_l, sleeve_l):
        ob.parent = arm_ob
        for m in ob.modifiers:
            if m.type == "ARMATURE":
                m.object = arm_ob

    print(f"=== BUILT TRIPO HANDS: Arm_Male_R={len(hand_r.data.vertices)}, Sleeve_R={len(sleeve_r.data.vertices)}, Bones={len(arm_ob.data.bones)}", flush=True)
    return arm_ob, (hand_l, hand_r, sleeve_l, sleeve_r)


def assign_hand_weights(ob, arm_ob, side):
    mesh = ob.data
    co = np.array([tuple(v.co) for v in mesh.vertices], dtype=np.float64)
    n_v = len(co)
    segs = {b.name: (np.array(b.head_local, dtype=np.float64), np.array(b.tail_local, dtype=np.float64))
            for b in arm_ob.data.bones if b.use_deform and b.name.endswith(side)}

    ea = np.array([e.vertices[0] for e in mesh.edges], dtype=np.int32)
    eb = np.array([e.vertices[1] for e in mesh.edges], dtype=np.int32)

    f_names = ("Index", "Middle", "Ring", "Pinky", "Thumb")
    chain_dist = {}
    for f in f_names:
        ds = [pt_seg_param_dist(co, *segs[f"{f}{s}{side}"])[1] for s in ("01", "02", "03")]
        chain_dist[f] = np.minimum.reduce(ds)
    all_d = np.stack([chain_dist[f] for f in f_names], axis=1)
    best_f = np.argmin(all_d, axis=1)

    W = {name: np.zeros(n_v, dtype=np.float64) for name in segs}
    # Мягкие радиусы переходов в суставах: при сгибе на 90° костяшка складывается плавной дугой, а не «квадратным» шарниром.
    J_MCP, J_PIP, J_DIP = 0.015, 0.013, 0.0105
    J_TMCP, J_TIP = 0.012, 0.0095

    for fi, f in enumerate(f_names):
        if f != "Thumb":
            _, _, p1, _ = pt_seg_param_dist(co, *segs[f"{f}01{side}"])
            _, _, p2, _ = pt_seg_param_dist(co, *segs[f"{f}02{side}"])
            _, _, p3, _ = pt_seg_param_dist(co, *segs[f"{f}03{side}"])
            mask = (best_f == fi) & (p1 > -J_MCP)
            if not mask.any():
                continue
            w1 = smoothstep(-J_MCP, J_MCP, p1)
            w2 = smoothstep(-J_PIP, J_PIP, p2)
            w3 = smoothstep(-J_DIP, J_DIP, p3)
            W[f"{f}03{side}"][mask] = (w1 * w2 * w3)[mask]
            W[f"{f}02{side}"][mask] = (w1 * w2 * (1.0 - w3))[mask]
            W[f"{f}01{side}"][mask] = (w1 * (1.0 - w2))[mask]
            W[f"Meta{f}{side}"][mask] = (1.0 - w1)[mask]
        else:
            _, d1, p1, _ = pt_seg_param_dist(co, *segs[f"Thumb01{side}"])
            _, _, p2, _ = pt_seg_param_dist(co, *segs[f"Thumb02{side}"])
            _, _, p3, _ = pt_seg_param_dist(co, *segs[f"Thumb03{side}"])
            t_zone = (best_f == fi) & (p1 > -0.015) & (d1 < 0.042)
            w_cmc = smoothstep(-0.012, 0.022, p1) * (1.0 - smoothstep(0.026, 0.044, d1))
            in_tf = (best_f == fi) & (p2 > -0.006)
            w_cmc = np.where(in_tf, 1.0, w_cmc)
            w_mcp = smoothstep(-J_TMCP, J_TMCP, p2)
            w_ip = smoothstep(-J_TIP, J_TIP, p3)
            mask = t_zone | in_tf
            W[f"Thumb03{side}"][mask] = (w_cmc * w_mcp * w_ip)[mask]
            W[f"Thumb02{side}"][mask] = (w_cmc * w_mcp * (1.0 - w_ip))[mask]
            W[f"Thumb01{side}"][mask] = (w_cmc * (1.0 - w_mcp))[mask]
            W[f"Hand{side}"][mask] += (1.0 - w_cmc)[mask]

    rem = np.clip(1.0 - sum(W.values()), 0.0, 1.0)
    w_hand = smoothstep(-0.015, 0.015, co[:, 1])
    w_tw = 1.0 - w_hand
    _, dp_m, pp_m, _ = pt_seg_param_dist(co, *segs[f"MetaPinky{side}"])
    _, dr_m, pr_m, _ = pt_seg_param_dist(co, *segs[f"MetaRing{side}"])
    w_mp = (1.0 - smoothstep(0.012, 0.028, dp_m)) * smoothstep(0.005, 0.030, pp_m) * w_hand
    w_mr = (1.0 - smoothstep(0.012, 0.026, dr_m)) * smoothstep(0.005, 0.030, pr_m) * w_hand * (1.0 - w_mp)
    W[f"MetaPinky{side}"] += rem * w_mp
    W[f"MetaRing{side}"] += rem * w_mr
    W[f"Hand{side}"] += rem * np.clip(1.0 - w_mp - w_mr, 0.0, 1.0) * w_hand
    W[f"ForearmTwist{side}"] += rem * w_tw

    # Сглаживание весов строго внутри каждого пальца и ладони (плюс мягкий переход строго на нижней перепонке между пальцами,
    # чтобы поднятый средний палец рядом с согнутым безымянным не рвал шов перепонки!)
    f_tag = np.full(n_v, -1, dtype=np.int32)
    web_zone = np.zeros(n_v, dtype=bool)
    for fi, f in enumerate(f_names):
        f_w = W[f"{f}01{side}"] + W[f"{f}02{side}"] + W[f"{f}03{side}"]
        if (f_w > 0.4).any():
            f_tag[f_w > 0.4] = fi
        if f != "Thumb":
            _, _, p1, _ = pt_seg_param_dist(co, *segs[f"{f}01{side}"])
            web_zone |= (best_f == fi) & (p1 > -0.012) & (p1 < 0.009)
    same = (f_tag[ea] == f_tag[eb]) | (f_tag[ea] == -1) | (f_tag[eb] == -1) | (web_zone[ea] & web_zone[eb])
    ea_s, eb_s = ea[same], eb[same]
    deg_s = np.zeros(n_v, dtype=np.float64)
    np.add.at(deg_s, ea_s, 1.0); np.add.at(deg_s, eb_s, 1.0)
    for _ in range(5):
        for name in W:
            arr = W[name]
            s = np.zeros(n_v, dtype=np.float64)
            np.add.at(s, ea_s, arr[eb_s]); np.add.at(s, eb_s, arr[ea_s])
            W[name] = np.where(deg_s > 0, 0.65 * arr + 0.35 * (s / np.maximum(deg_s, 1.0)), arr)

    bone_list = list(W.keys())
    W_mat = np.stack([W[b] for b in bone_list], axis=1)
    ob.vertex_groups.clear()
    vgs = [ob.vertex_groups.new(name=b) for b in bone_list]
    for i in range(n_v):
        row = W_mat[i]
        top4 = np.argsort(row)[-4:]
        vals = row[top4]
        keep = vals > 1e-4
        top4, vals = top4[keep], vals[keep]
        if len(vals) == 0:
            vgs[bone_list.index(f"Hand{side}")].add([i], 1.0, "REPLACE")
        else:
            vals = vals / vals.sum()
            for bi, wv in zip(top4, vals):
                vgs[bi].add([i], float(wv), "REPLACE")

    ob.parent = arm_ob
    mod = ob.modifiers.new("Armature", "ARMATURE")
    mod.object = arm_ob
    mod.use_deform_preserve_volume = False


def assign_sleeve_weights(sleeve_ob, arm_ob, side):
    co = np.array([tuple(v.co) for v in sleeve_ob.data.vertices], dtype=np.float64)
    sleeve_ob.vertex_groups.clear()
    vg_f = sleeve_ob.vertex_groups.new(name="Forearm" + side)
    vg_t = sleeve_ob.vertex_groups.new(name="ForearmTwist" + side)
    for i, p in enumerate(co):
        t = float(np.clip((p[1] - (-0.25)) / 0.25, 0.0, 1.0))
        t = t * t * (3.0 - 2.0 * t)
        if t < 0.999:
            vg_f.add([i], 1.0 - t, "REPLACE")
        if t > 0.001:
            vg_t.add([i], t, "REPLACE")
    sleeve_ob.parent = arm_ob
    mod = sleeve_ob.modifiers.new("Armature", "ARMATURE")
    mod.object = arm_ob
    mod.use_deform_preserve_volume = False


# ======================================================================================================================
# Позы кисти и положения рук в игре
# ======================================================================================================================

# Формат позы кисти:
#   fingers: {палец: (mcp, pip, dip, spread, roll)} в градусах; +curl -> сгиб к ладони, +spread -> к большому пальцу (для _R и симметрично _L)
#   thumb:   (cmc_x, cmc_y, cmc_z, mcp_x, mcp_z, ip_x) в градусах (симметрично отражается по Y/Z для _L)
#   cup:     градусы свода ладони (MetaPinky/MetaRing к ладони)

HAND_RELAXED = {
    "fingers": {
        "Index":  (18, 22, 10, -2.5, 0),
        "Middle": (22, 26, 12,  0.0, 0),
        "Ring":   (26, 30, 14,  2.0, 0),
        "Pinky":  (30, 34, 16,  4.0, 0),
    },
    "thumb": (-12, 18, -14, -8, 0, -6),
    "cup": 1.8,
}

# Лежащая на сукне расслабленная правая кисть: основание ладони, большой палец и подушечки всех 4 пальцев
# плотно и статично лежат прямо на красном сукне (~0.5–1.5 мм, без «парения» в воздухе и без проваливания в стол, 0 мм нахлёста пальцев).
HAND_TABLE = {
    "fingers": {
        "Index":  ( 6, 12,  5, -3.0, 0),
        "Middle": ( 6, 13,  5, -0.5, 0),
        "Ring":   ( 7, 14,  6,  2.5, 0),
        "Pinky":  ( 8, 12,  5,  6.5, 0),
    },
    "thumb": (14, 6, 1, 5, 0, 4),
    "cup": 0.8,
}

# «Класс» — ровно по 3D-модели автора Hand_Right_ThumbsUp.fbx / картинка-gemini.jpg:
# Проверено реальным 3D-детектором коллизий: все 4 пальца (включая мизинец!) плотно прилегают друг к другу без наложений,
# ногти лежат ровно снаружи ладони (не уходят внутрь кожи), большой палец прямо вверх!
HAND_THUMBS_UP = {
    "fingers": {
        "Index":  (84, 96, 54, -6, 0),
        "Middle": (86, 98, 54,  1, 0),
        "Ring":   (86, 96, 50,  3, 0),
        "Pinky":  (80, 90, 43,  4, 0),
    },
    "thumb": (12, -8, 14, 6, -4, 8),
    "cup": 3.5,
}

# Истинные анатомические оси сгибания фаланг большого пальца Tripo-модели (измерены по эллипсу сечения ногтя Thumb03):
# сгибание вокруг этих осей ведёт подушечку строго по естественному шарниру без бокового излома ногтя!
_THUMB2_HINGE = Vector((0.610, -0.019, 0.792)).normalized()
_THUMB3_HINGE = Vector((0.532, 0.0, 0.847)).normalized()


def anat_thumb(cx, cy, cz, f2, f3):
    """Строит 9-числовую позу большого пальца: CMC (cx, cy, cz) + строго анатомический сгиб MCP (f2) и IP (f3) по оси ногтя."""
    e2 = Matrix.Rotation(math.radians(f2), 3, _THUMB2_HINGE).to_euler("XYZ")
    e3 = Matrix.Rotation(math.radians(f3), 3, _THUMB3_HINGE).to_euler("XYZ")
    return (
        cx, cy, cz,
        math.degrees(e2.x), math.degrees(e2.y), math.degrees(e2.z),
        math.degrees(e3.x), math.degrees(e3.y), math.degrees(e3.z),
    )


def _thumb9(t):
    if len(t) == 9:
        return t
    cx, cy, cz, mx, mz, ix = t
    return (cx, cy, cz, mx, 0.0, mz, ix, 0.0, 0.0)


# «Фу» — тот же чистый кулак без пересечений, естественный угол большого пальца (мягко довёрнут от первой версии, без выворота!).
HAND_THUMBS_DOWN = {
    "fingers": HAND_THUMBS_UP["fingers"],
    "thumb": (15, -4, 18, 7, -3, 8),
    "cup": 3.5,
}

# Кулак: 4 чистых пальца (0 мм коллизий), большой палец гнётся строго по своей анатомической оси ногтя:
# верхняя фаланга и ноготь полностью видны на передней стороне кулака, 0.0 мм пересечений с указательным и средним (FistSmooth).
HAND_FIST = {
    "fingers": HAND_THUMBS_UP["fingers"],
    "thumb": anat_thumb(-50, -42, 32, -40, -34),
    "cup": 3.5,
}

# Щипок (взятие карты из веера): подушечки большого и указательного пальцев оставляют чистый зазор ~0.7 мм под картон карты
# (0.00 мм проникновения пальца в палец!), а средний, безымянный и мизинец лежат параллельным каскадом без пересечений.
HAND_TAKE_CARD = {
    "fingers": {
        "Index":  (44, 54, 32, -2.5, 0),
        "Middle": (56, 66, 38,  0.0, 0),
        "Ring":   (64, 72, 42,  1.5, 0),
        "Pinky":  (68, 74, 40,  2.0, 0),
    },
    "thumb": (-42.6, -55.8, 26.2, -14.2, 4.0, -18.2),
    "cup": 2.8,
}

# Хват веера (левая рука): большой палец уверенно прижимает веер спереди, 4 пальца мягко и раздельно поддерживают карты сзади (0 мм коллизий).
HAND_HOLD_FAN = {
    "fingers": {
        "Index":  (38, 42, 22, -4.5, 0),
        "Middle": (46, 52, 28,  0.0, 0),
        "Ring":   (55, 60, 32,  1.8, 0),
        "Pinky":  (62, 66, 36,  2.5, 0),
    },
    "thumb": (-11, -22, -7, -6, 0, -6),
    "cup": 3.2,
}

# Указание пальцем через стол («Указать» — прямой указательный стрелой, расслабленный естественный большой палец
# вперёд-влево как на референсе, средний/безымянный/мизинец аккуратно собраны под ладонью; 0 вывернутых граней!).
HAND_POINT = {
    "fingers": {
        "Index":  (-8, -6, -4, -4, 0),
        "Middle": (80, 94, 48,  1, 0),
        "Ring":   (80, 94, 46,  4, 0),
        "Pinky":  (76, 88, 40,  5, 0),
    },
    "thumb": anat_thumb(-14, -18, 10, -12, -14),
    "cup": 3.0,
}

# «Фак» (средний палец естественно и без переразгибания вверх, слегка укорочен в MiddleFingerSmooth до живой пропорции ~85 мм,
# указательный/безымянный/мизинец мягко лежат на ладони без острой ступеньки у основания, ровное прямое запястье!).
HAND_MIDDLE_FINGER = {
    "fingers": {
        "Index":  (75, 93, 46, -2, 0),
        "Middle": ( 3,  5,  3,  0, 0),
        "Ring":   (75, 93, 44,  2, 0),
        "Pinky":  (73, 87, 38,  4, 0),
    },
    "thumb": anat_thumb(-40, -36, 26, -34, -26),
    "cup": 2.5,
}


def pose_fingers(armature, hand_pose, side):
    pb = armature.pose.bones
    sx = 1.0 if side == "_R" else -1.0
    cup = hand_pose.get("cup", 0.0)
    pb["MetaPinky" + side].rotation_mode = "XYZ"
    pb["MetaRing" + side].rotation_mode = "XYZ"
    pb["MetaPinky" + side].rotation_euler = Euler(
        (math.radians(-cup), math.radians(cup * 0.55 * sx), math.radians(cup * 0.40 * sx)), "ZYX"
    ).to_matrix().to_euler("XYZ")
    pb["MetaRing" + side].rotation_euler = Euler(
        (math.radians(-cup * 0.45), math.radians(cup * 0.20 * sx), math.radians(cup * 0.15 * sx)), "ZYX"
    ).to_matrix().to_euler("XYZ")
    for f, (mcp, pip, dip, spread, roll) in hand_pose["fingers"].items():
        for n in ("01", "02", "03"):
            pb[f"{f}{n}{side}"].rotation_mode = "XYZ"
        # Порядок ZYX: сначала сведение пальца в плоскости ладони (spread), затем небольшой разворот (roll),
        # затем сгиб к ладони (-mcp) — так мизинец при сгибе на 100° плотно прилегает к безымянному, а не оттопыривается!
        pb[f"{f}01{side}"].rotation_euler = Euler(
            (math.radians(-mcp), math.radians(roll * sx), math.radians(spread * sx)), "ZYX"
        ).to_matrix().to_euler("XYZ")
        pb[f"{f}02{side}"].rotation_euler = (math.radians(-pip), 0, 0)
        pb[f"{f}03{side}"].rotation_euler = (math.radians(-dip), 0, 0)
    cx, cy, cz, m2x, m2y, m2z, m3x, m3y, m3z = _thumb9(hand_pose["thumb"])
    for n in ("01", "02", "03"):
        pb[f"Thumb{n}{side}"].rotation_mode = "XYZ"
    pb["Thumb01" + side].rotation_euler = (math.radians(cx), math.radians(cy * sx), math.radians(cz * sx))
    pb["Thumb02" + side].rotation_euler = (math.radians(m2x), math.radians(m2y * sx), math.radians(m2z * sx))
    pb["Thumb03" + side].rotation_euler = (math.radians(m3x), math.radians(m3y * sx), math.radians(m3z * sx))
    bpy.context.view_layer.update()


def bone_frame(origin, y, z):
    y = y.normalized()
    x = y.cross(z).normalized()
    z = x.cross(y)
    m = Matrix((x, y, z)).transposed().to_4x4()
    m.translation = origin
    return m


def set_arm(armature, spec, side):
    """Ставит руку целиком в координатах от глаз игрока (как in_game.set_arm): предплечье, скручивание, кисть, пальцы."""
    pb = armature.pose.bones
    for b in pb:
        if b.name.endswith(side):
            b.rotation_mode = "XYZ"
    # Сначала ставим предплечье и кисть
    fore_len = pb["Forearm" + side].length + pb["ForearmTwist" + side].length
    wrist = spec["wrist"]
    elbow = wrist + spec["elbow_from_wrist"].normalized() * fore_len
    forward = (wrist - elbow).normalized()
    back_hand = spec["back"].normalized()
    # Предплечье у локтя меньше крутится (половина угла — как живая рука, остальное докручивает ForearmTwist;
    # при вертикальной кисти с twist_blend=0.0 локоть выровнен по кисти, чтобы рукав не скручивался спиралью!).
    back_fore = (back_hand - forward * back_hand.dot(forward)).normalized()
    twist_blend = float(spec.get("twist_blend", 0.4))
    up_ref = Vector((0, 0, 1))
    up_perp = up_ref - forward * up_ref.dot(forward)
    if twist_blend > 1e-4 and up_perp.length > 0.25:
        up_perp = up_perp.normalized()
        back_elbow = (up_perp * twist_blend + back_fore * (1.0 - twist_blend)).normalized()
        back_elbow = (back_elbow - forward * back_elbow.dot(forward)).normalized()
    else:
        back_elbow = back_fore

    pb["Forearm" + side].matrix = bone_frame(elbow, forward, back_elbow)
    bpy.context.view_layer.update()
    mid = elbow + forward * pb["Forearm" + side].length
    pb["ForearmTwist" + side].matrix = bone_frame(mid, forward, back_fore)
    pb["ForearmTwist" + side].location = (0, 0, 0)
    bpy.context.view_layer.update()
    pb["Hand" + side].matrix = bone_frame(wrist, spec["along"], back_hand)
    pb["Hand" + side].location = (0, 0, 0)
    bpy.context.view_layer.update()
    pose_fingers(armature, spec["hand"], side)
    # Точечное наведение большого пальца по 3D-точкам на лице передней карты (без скручивания кожи и без пересечения веера!)
    if "thumb_targets" in spec and spec["thumb_targets"]:
        for bn in ("Thumb01" + side, "Thumb02" + side, "Thumb03" + side):
            pb[bn].rotation_euler = (0, 0, 0)
        bpy.context.view_layer.update()
        for bn, tgt in zip(("Thumb01" + side, "Thumb02" + side, "Thumb03" + side), spec["thumb_targets"]):
            head = pb[bn].head.copy()
            q = (pb[bn].tail - head).normalized().rotation_difference((tgt - head).normalized())
            m = (q.to_matrix() @ pb[bn].matrix.to_3x3()).to_4x4()
            m.translation = head
            pb[bn].matrix = m
            pb[bn].location = (0, 0, 0)
            bpy.context.view_layer.update()


def free_arm_spec(wrist, elbow, roll, ulnar, extend, hand_pose):
    """Как game_test.free_arm: из запястья, локтя, разворота предплечья и угла запястья строит spec для правой руки."""
    wrist, elbow = Vector(wrist), Vector(elbow)
    forward = (wrist - elbow).normalized()
    up = Vector((0, 0, 1))
    back = (up - forward * up.dot(forward)).normalized()
    back = (Matrix.Rotation(math.radians(roll), 3, forward) @ back).normalized()
    thumb_side = back.cross(forward).normalized()
    along = (Matrix.Rotation(math.radians(ulnar), 3, back) @ forward).normalized()
    if ulnar >= 0 and along.dot(thumb_side) > forward.dot(thumb_side):
        along = (Matrix.Rotation(math.radians(-ulnar), 3, back) @ forward).normalized()
    elif ulnar < 0 and along.dot(thumb_side) < forward.dot(thumb_side):
        along = (Matrix.Rotation(math.radians(-ulnar), 3, back) @ forward).normalized()
    side = along.cross(back).normalized()
    along = (Matrix.Rotation(math.radians(extend), 3, side) @ along).normalized()
    if extend >= 0 and along.dot(back) < 0:
        along = (Matrix.Rotation(math.radians(-2 * extend), 3, side) @ along).normalized()
    elif extend < 0 and along.dot(back) > 0:
        along = (Matrix.Rotation(math.radians(-2 * extend), 3, side) @ along).normalized()
    back = side.cross(along).normalized()
    return {
        "wrist": wrist, "along": along, "back": back,
        "elbow_from_wrist": (elbow - wrist).normalized(),
        "hand": hand_pose,
    }


def mirror_spec(spec):
    return {
        "wrist": Vector((-spec["wrist"].x, spec["wrist"].y, spec["wrist"].z)),
        "along": Vector((-spec["along"].x, spec["along"].y, spec["along"].z)),
        "back": Vector((-spec["back"].x, spec["back"].y, spec["back"].z)),
        "elbow_from_wrist": Vector((-spec["elbow_from_wrist"].x, spec["elbow_from_wrist"].y, spec["elbow_from_wrist"].z)),
        "hand": spec["hand"],
    }


def mix_spec(a, b, t):
    def lerp(x, y, key=None):
        if key == "thumb":
            x, y = _thumb9(x), _thumb9(y)
        if isinstance(x, (int, float)):
            return x + (y - x) * t
        if isinstance(x, tuple):
            return tuple(lerp(p, q) for p, q in zip(x, y))
        if isinstance(x, dict):
            return {k: lerp(x[k], y[k], k) for k in x}
        return x.lerp(y, t)
    res = {k: lerp(a[k], b[k], k) for k in a if k in b}
    if "twist_blend" in a or "twist_blend" in b:
        ta = float(a.get("twist_blend", 0.4))
        tb = float(b.get("twist_blend", 0.4))
        res["twist_blend"] = ta + (tb - ta) * t
    for k in ("along", "back", "elbow_from_wrist"):
        res[k] = res[k].normalized()
    return res


def moved_spec(spec, delta):
    res = copy.deepcopy(spec)
    res["wrist"] = res["wrist"] + delta
    if "thumb_targets" in res and res["thumb_targets"]:
        res["thumb_targets"] = tuple(p + delta for p in res["thumb_targets"])
    return res


def curl_spec(spec, deg):
    res = copy.deepcopy(spec)
    res["hand"] = copy.deepcopy(spec["hand"])
    res["hand"]["fingers"] = {
        k: (v[0] + deg, v[1] + deg, v[2] + deg * 0.5, v[3], v[4])
        for k, v in spec["hand"]["fingers"].items()
    }
    return res


# --- Игровые позы обеих рук ---
# Левая рука держит веер карт (FAN_PIVOT = (-0.154, 0.32, -0.031))
# Подобрано по 3D-детектору коллизий с реальными 6 и 12 картами Unity:
# нижние углы карт не режут кожу между большим и указательным пальцами, 4 пальца мягко поддерживают веер сзади,
# а большой палец без скручивания кожи ложится ровно по лицевой стороне передней карты!
LEFT_FAN = dict(
    in_game.edge_on(in_game.on_fan(-0.077, -0.085, 0.048), 20, 96),
    elbow_from_wrist=Vector((-0.42, -0.38, -0.82)).normalized(),
    hand=HAND_HOLD_FAN,
    thumb_targets=(
        in_game.on_fan(-0.028, -0.019, -0.0265),
        in_game.on_fan( 0.004,  0.002, -0.0245),
        in_game.on_fan( 0.029,  0.017, -0.0235),
    ),
)
# Правая рука статично и плотно лежит на сукне: основание ладони и подушечки пальцев на ткани, рукав — на золотом ободе.
RIGHT_TABLE = {
    "wrist": in_game.on_table(0.195, 0.485, 0.0352),
    "along": in_game.table_dir(-0.34, 0.94, 0.000).normalized(),
    "back": in_game.table_dir(-0.12, 0.03, 1.0).normalized(),
    "elbow_from_wrist": in_game.table_dir(0.19, -0.34, 0.058).normalized(),
    "hand": HAND_TABLE,
}
LEFT_TABLE = mirror_spec(RIGHT_TABLE)

# Положение рук в проверке (F, U, J) и в жестах меню «ЖЕСТЫ»
IDLE_TEST_R = free_arm_spec((0.17, 0.36, -0.11), (0.23, 0.22, -0.27), 55, 8, 10, HAND_RELAXED)
# «Класс»: рука вынесена чуть вперёд над столом (y=0.395 м), кулак ровно перпендикулярно столу и горизонту (~90°),
# 4 пальца горизонтальной стопкой, большой палец строго вверх (~90°)!
THUMBS_UP_R = free_arm_spec((0.145, 0.395, -0.052), (0.285, 0.250, -0.175), 80, 7, 4, HAND_THUMBS_UP)
# «Фу»: естественная поза как в первой версии (низкий локоть из правого нижнего угла, чистый верх кулака без торчащего мизинца),
# мягко довёрнута ближе к вертикали (~-110°) и чуть приподнята над кнопкой «БЕРУ/ПАС»!
THUMBS_DOWN_R = free_arm_spec((0.148, 0.392, -0.025), (0.270, 0.232, -0.142), -100, -12, 10, HAND_THUMBS_DOWN)
# Кулак: рука вынесена вперёд над столом в 3/4, ясно видны костяшки и вся верхняя фаланга большого пальца с ногтем!
FIST_R = free_arm_spec((0.145, 0.395, -0.055), (0.285, 0.250, -0.180), 72, 8, 6, HAND_FIST)

def orient_arm_spec(wrist, elbow, along, back, hand_pose):
    """Задаёт точную позу руки по запястью, локтю, направлению кисти along и тылу back (ортогонализуя back к along)."""
    wrist, elbow = Vector(wrist), Vector(elbow)
    along = Vector(along).normalized()
    back = Vector(back).normalized()
    back = (back - along * back.dot(along)).normalized()
    return {
        "wrist": wrist, "along": along, "back": back,
        "elbow_from_wrist": (elbow - wrist).normalized(),
        "hand": hand_pose,
    }


# «Указать»: правая рука тылом вверх (как на референсе), прямой указательный стрелой на соперника, плавная перепонка к большому пальцу!
POINT_R = orient_arm_spec(
    (0.145, 0.370, -0.055), (0.290, 0.220, -0.175),
    (-0.42, 0.83, 0.36), (0.28, -0.16, 0.94), HAND_POINT,
)
# «Фак»: строго вертикально к столу с почти соосным запястьем (~2,5° вместо 22°), чтобы вязаная манжета
# сидела ровно по кругу без перекоса и не врезалась в основание кисти!
MIDDLE_FINGER_R = dict(
    orient_arm_spec(
        in_game.on_table(0.115, 0.355, 0.242),
        in_game.on_table(0.128, 0.338, 0.015),
        in_game.table_dir(-0.02, 0.05, 0.998),
        in_game.table_dir(0.05, 0.998, -0.05),
        HAND_MIDDLE_FINGER,
    ),
    twist_blend=0.0,
)
TAP_UP_R = moved_spec(curl_spec(RIGHT_TABLE, 4), in_game.table_dir(0, 0, 0.025))
TAP_DOWN_R = moved_spec(RIGHT_TABLE, in_game.table_dir(0, 0, 0.004))
SHRUG_R = {
    "wrist": in_game.on_table(0.22, 0.50, 0.12),
    "along": in_game.table_dir(-0.42, 0.86, 0.18).normalized(),
    "back": in_game.table_dir(0.1, 0.15, -1.0).normalized(),
    "elbow_from_wrist": in_game.table_dir(0.3, -0.8, -0.45).normalized(),
    "hand": HAND_RELAXED,
}
SHRUG_L = moved_spec(LEFT_FAN, in_game.FAN_UP * 0.028 - in_game.FAN_RIGHT * 0.01)
AT_FAN_R = {
    "wrist": in_game.on_fan(0.13, 0.0, -0.035),
    "along": in_game.fan_dir(-0.55, 0.8, 0.2).normalized(),
    "back": in_game.fan_dir(0.35, 0.15, -1.0).normalized(),
    "elbow_from_wrist": in_game.table_dir(0.4, -0.7, -0.6).normalized(),
    "hand": HAND_TAKE_CARD,
}
OVER_TABLE_R = {
    "wrist": in_game.on_table(0.08, 0.74, 0.09),
    "along": in_game.table_dir(-0.3, 0.9, -0.2).normalized(),
    "back": in_game.table_dir(0.2, 0.1, 1.0).normalized(),
    "elbow_from_wrist": in_game.table_dir(0.3, -0.9, -0.2).normalized(),
    "hand": HAND_TAKE_CARD,
}
TOWARD_DECK_R = {
    "wrist": in_game.on_table(-0.02, 0.8, 0.09),
    "along": in_game.table_dir(-0.45, 0.88, -0.1).normalized(),
    "back": in_game.table_dir(0.1, 0.1, 1.0).normalized(),
    "elbow_from_wrist": in_game.table_dir(0.4, -0.9, -0.15).normalized(),
    "hand": HAND_TAKE_CARD,
}
HOVER_R = moved_spec(curl_spec(RIGHT_TABLE, 10), in_game.table_dir(0, 0, 0.055))


def place_fan_socket(armature):
    """Ставит кость SocketFan_L ровно в точку веера (FAN_PIVOT, FAN_UP, FAN_DEEP) относительно Hand_L в позе LEFT_FAN."""
    armature.matrix_world.identity()
    for pb in armature.pose.bones:
        pb.rotation_mode = "XYZ"
        pb.rotation_euler = (0, 0, 0)
        pb.location = (0, 0, 0)
    set_arm(armature, LEFT_FAN, "_L")
    hand = armature.pose.bones["Hand_L"]
    pivot, _, up, deep = in_game.fan_axes()
    socket = hand.bone.matrix_local @ hand.matrix.inverted() @ bone_frame(pivot, up, deep)
    for pb in armature.pose.bones:
        pb.rotation_euler = (0, 0, 0)
        pb.location = (0, 0, 0)
    bpy.context.view_layer.update()

    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = armature
    armature.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    eb = armature.data.edit_bones
    bone = eb.get("SocketFan_L") or eb.new("SocketFan_L")
    bone.head = socket.translation
    bone.tail = socket.translation + socket.col[1].xyz * 0.03
    bone.align_roll(socket.col[2].xyz)
    bone.parent = eb["Hand_L"]
    bone.use_connect = False
    bone.use_deform = False
    bpy.ops.object.mode_set(mode="OBJECT")
    armature.select_set(False)


ALL_SHAPE_PROPS = (
    "thumbsup_smooth_L", "thumbsup_smooth_R",
    "fist_smooth_L", "fist_smooth_R",
    "thumbs_smooth_L", "thumbs_smooth_R",
    "point_smooth_L", "point_smooth_R",
    "middlefinger_smooth_L", "middlefinger_smooth_R",
)


def _solve_pose_corrective_delta(arm_ob, hand_r, hand_pose, label, resolve_thumb=False):
    """Считает точную обратную LBS-дельту корректирующей формы (Dual-Quaternion сохранение объёма + 3D-солвер коллизий BVHTree)."""
    reset_all_bones(arm_ob)
    pose_fingers(arm_ob, hand_pose, "_R")
    bpy.context.view_layer.update()

    mod_r = hand_r.modifiers["Armature"]
    deps = bpy.context.evaluated_depsgraph_get()

    mod_r.use_deform_preserve_volume = False
    bpy.context.view_layer.update()
    ev_lbs = hand_r.evaluated_get(deps)
    em_lbs = ev_lbs.to_mesh()
    co_lbs = np.array([tuple(v.co) for v in em_lbs.vertices], dtype=np.float64)
    ev_lbs.to_mesh_clear()

    mod_r.use_deform_preserve_volume = True
    bpy.context.view_layer.update()
    ev_dq = hand_r.evaluated_get(deps)
    em_dq = ev_dq.to_mesh()
    co_tgt = np.array([tuple(v.co) for v in em_dq.vertices], dtype=np.float64)
    polys_all = [tuple(p.vertices) for p in em_dq.polygons]
    ev_dq.to_mesh_clear()
    mod_r.use_deform_preserve_volume = False
    bpy.context.view_layer.update()

    n_v = len(co_tgt)
    co_rest = np.array([tuple(v.co) for v in hand_r.data.vertices], dtype=np.float64)
    vg_names = [vg.name for vg in hand_r.vertex_groups]

    v_part = ["Palm"] * n_v
    v_shaft = np.zeros(n_v, dtype=bool)
    v_shaft_w = np.ones(n_v, dtype=np.float64)
    v_thumb_distal_w = np.zeros(n_v, dtype=np.float64)
    joint_blend = np.zeros(n_v, dtype=np.float64)
    extra_smooth_w = np.zeros(n_v, dtype=np.float64)
    mf_web_w = np.zeros(n_v, dtype=np.float64)
    mf_mid_w = np.zeros(n_v, dtype=np.float64)
    mf_mid_proj = np.zeros(n_v, dtype=np.float64)
    pip_rest_map = {f: np.array(arm_ob.data.bones[f"{f}02_R"].head_local, dtype=np.float64)
                    for f in ("Index", "Middle", "Ring", "Pinky")}
    dip_rest_map = {f: np.array(arm_ob.data.bones[f"{f}03_R"].head_local, dtype=np.float64)
                    for f in ("Index", "Middle", "Ring", "Pinky")}
    mid_b01 = arm_ob.data.bones["Middle01_R"]
    mid_mcp_rest = np.array(mid_b01.head_local, dtype=np.float64)
    mid_tip_rest = np.array(arm_ob.data.bones["TipMiddle_R"].tail_local, dtype=np.float64)
    mid_axis_rest = mid_tip_rest - mid_mcp_rest
    mid_len_rest = max(float(np.linalg.norm(mid_axis_rest)), 1e-6)
    mid_axis_rest /= mid_len_rest
    is_mf_pose = label == "MiddleFingerSmooth"
    for i, v in enumerate(hand_r.data.vertices):
        if not v.groups:
            continue
        best_g = max(v.groups, key=lambda g: g.weight)
        bn = vg_names[best_g.group]
        ws = sorted([g.weight for g in v.groups], reverse=True)
        if len(ws) >= 2 and ws[1] > 0.12:
            joint_blend[i] = min(1.0, (ws[1] - 0.12) / 0.30)
        if is_mf_pose:
            w_m = sum(g.weight for g in v.groups if vg_names[g.group].startswith("Middle0"))
            mf_mid_w[i] = w_m
            rel_m = co_rest[i] - mid_mcp_rest
            pm = float(rel_m @ mid_axis_rest)
            mf_mid_proj[i] = pm
            rm = float(np.linalg.norm(rel_m - pm * mid_axis_rest))
            # Широкая плавная зона основания среднего пальца и обеих межпальцевых перепонок (Index-Middle и Ring-Middle):
            # захватывает всю складку от -10 мм до +28 мм по оси пальца и до 24 мм в стороны — без резких границ!
            if -0.012 < pm < 0.032 and rm < 0.026:
                w_ax = float(smoothstep(-0.012, 0.002, pm)) * (1.0 - float(smoothstep(0.016, 0.032, pm)))
                w_rad = 1.0 - float(smoothstep(0.011, 0.026, rm))
                mf_web_w[i] = w_ax * w_rad
        for f in ("Index", "Middle", "Ring", "Pinky", "Thumb"):
            if bn.startswith(f):
                v_part[i] = f
                b01 = arm_ob.data.bones[f"{f}01_R"]
                h = np.array(b01.head_local, dtype=np.float64)
                u = np.array(b01.tail_local, dtype=np.float64) - h
                u /= max(np.linalg.norm(u), 1e-9)
                proj = (co_rest[i] - h) @ u
                if f == "Thumb":
                    # Разводим только фаланги Thumb02/Thumb03 (плавно с 36 до 48 мм от основания Thumb01),
                    # чтобы никогда не рвать основание большого пальца от ладони треугольной ступенькой!
                    if proj > 0.036:
                        v_shaft[i] = True
                        v_thumb_distal_w[i] = min(1.0, (proj - 0.036) / 0.012)
                else:
                    if is_mf_pose:
                        # Плавный вход без жёсткой ступеньки на 16 мм у основания среднего пальца!
                        if proj > 0.012:
                            v_shaft[i] = True
                            v_shaft_w[i] = float(smoothstep(0.012, 0.028, proj))
                    else:
                        if proj > 0.016:
                            v_shaft[i] = True
                    # Плавное объёмное сглаживание на согнутых суставах PIP и DIP ВСЕХ пальцев
                    mcp_a, pip_a, dip_a, _, _ = hand_pose["fingers"][f]
                    if pip_a > 45:
                        d_pip = np.linalg.norm(co_rest[i] - pip_rest_map[f])
                        if d_pip < 0.015:
                            extra_smooth_w[i] = max(extra_smooth_w[i], 1.0 - d_pip / 0.015)
                    if dip_a > 30:
                        d_dip = np.linalg.norm(co_rest[i] - dip_rest_map[f])
                        if d_dip < 0.012:
                            extra_smooth_w[i] = max(extra_smooth_w[i], 0.85 * (1.0 - d_dip / 0.012))
                break
        if is_mf_pose and mf_web_w[i] > 0.0:
            extra_smooth_w[i] = max(extra_smooth_w[i], 0.95 * mf_web_w[i])

    ea = np.array([e.vertices[0] for e in hand_r.data.edges], dtype=np.int32)
    eb = np.array([e.vertices[1] for e in hand_r.data.edges], dtype=np.int32)
    deg = np.zeros(n_v, dtype=np.float64)
    np.add.at(deg, ea, 1.0); np.add.at(deg, eb, 1.0)
    deg_safe = np.maximum(deg, 1.0)

    def build_part_bvhs(coords):
        bvhs, idx_map = {}, {}
        for p in ("Index", "Middle", "Ring", "Pinky", "Thumb", "Palm"):
            if p == "Palm":
                ok = lambda vi: v_part[vi] == "Palm" and co_rest[vi, 1] < 0.082
            else:
                ok = lambda vi, pp=p: v_part[vi] == pp and v_shaft[vi]
            idx_map[p] = np.array([i for i in range(n_v) if ok(i)], dtype=np.int32)
            p_polys = [poly for poly in polys_all if all(ok(vi) for vi in poly)]
            bvhs[p] = BVHTree.FromPolygons([Vector(c) for c in coords], p_polys)
        return bvhs, idx_map

    def report_collisions(tag, coords):
        bvhs, idx_map = build_part_bvhs(coords)
        pairs = [("Index", "Middle"), ("Middle", "Ring"), ("Ring", "Pinky"), ("Index", "Palm"), ("Middle", "Palm"), ("Ring", "Palm"), ("Pinky", "Palm")]
        if resolve_thumb:
            pairs = [("Thumb", "Index"), ("Thumb", "Middle")] + pairs
        parts_str = []
        for a, b in pairs:
            bvh_b = bvhs[b]
            pens, max_d = 0, 0.0
            for vi in idx_map[a]:
                pt = Vector(coords[vi])
                loc, norm, _, dist = bvh_b.find_nearest(pt)
                if loc is not None and (pt - loc).dot(norm) < 0.0 and dist < 0.008:
                    pens += 1
                    if dist > max_d:
                        max_d = dist
            parts_str.append(f"{a[0]}-{b[0]}:{pens}({max_d*1000:.1f}mm)")
        print(f"=== 3D COLLISIONS [{label}/{tag}]: " + "  ".join(parts_str), flush=True)

    report_collisions("BEFORE_LBS", co_lbs)

    # 1. Мягкое Таубин-сглаживание (без усадки объёма!) строго на сгибах суставов
    for _ in range(3):
        avg = np.zeros_like(co_tgt)
        np.add.at(avg, ea, co_tgt[eb]); np.add.at(avg, eb, co_tgt[ea])
        avg /= deg_safe[:, None]
        w = (joint_blend * 0.38)[:, None]
        co_tgt = co_tgt + w * (avg - co_tgt)
        avg2 = np.zeros_like(co_tgt)
        np.add.at(avg2, ea, co_tgt[eb]); np.add.at(avg2, eb, co_tgt[ea])
        avg2 /= deg_safe[:, None]
        co_tgt = co_tgt - (w * 0.92) * (avg2 - co_tgt)
    if extra_smooth_w.any():
        # Дополнительное сохраняющее объём Таубин-сглаживание на костяшке мизинца (PIP) и перепонке среднего пальца
        for _ in range(8):
            avg = np.zeros_like(co_tgt)
            np.add.at(avg, ea, co_tgt[eb]); np.add.at(avg, eb, co_tgt[ea])
            avg /= deg_safe[:, None]
            wp = (extra_smooth_w * 0.55)[:, None]
            co_tgt = co_tgt + wp * (avg - co_tgt)
            avg2 = np.zeros_like(co_tgt)
            np.add.at(avg2, ea, co_tgt[eb]); np.add.at(avg2, eb, co_tgt[ea])
            avg2 /= deg_safe[:, None]
            co_tgt = co_tgt - (wp * 0.96) * (avg2 - co_tgt)

    # 2. Физическое разведение наложений ПОСЛЕ сглаживания (5 проходов; 8 для кулака, чтобы большой палец лёг ровно с 0 мм коллизий)
    finger_clr = 0.0004
    palm_clr = 0.0018
    n_passes = 8 if resolve_thumb else 5
    for _pass in range(n_passes):
        bvhs, idx_map = build_part_bvhs(co_tgt)
        push = np.zeros((n_v, 3), dtype=np.float64)
        for a, b in [("Index", "Middle"), ("Middle", "Ring"), ("Ring", "Pinky")]:
            for src_p, dst_p in ((a, b), (b, a)):
                bvh_dst = bvhs[dst_p]
                for vi in idx_map[src_p]:
                    pt = Vector(co_tgt[vi])
                    loc, norm, _, dist = bvh_dst.find_nearest(pt)
                    if loc is not None:
                        signed = (pt - loc).dot(norm)
                        if signed < finger_clr and dist < 0.008:
                            push[vi] += np.array(norm, dtype=np.float64) * ((finger_clr - signed) * 0.55 * v_shaft_w[vi])
        bvh_palm = bvhs["Palm"]
        for f in ("Index", "Middle", "Ring", "Pinky"):
            for vi in idx_map[f]:
                pt = Vector(co_tgt[vi])
                loc, norm, _, dist = bvh_palm.find_nearest(pt)
                if loc is not None:
                    signed = (pt - loc).dot(norm)
                    if signed < palm_clr and dist < 0.009:
                        push[vi] += np.array(norm, dtype=np.float64) * ((palm_clr - signed) * 0.90)
        if resolve_thumb:
            # Разводим только верхние фаланги большого пальца (Thumb02/Thumb03) от указательного и среднего,
            # НЕ трогая основание Thumb01 у ладони (никаких острых треугольных выступов!).
            for dst_p in ("Index", "Middle"):
                bvh_dst = bvhs[dst_p]
                clr_t = 0.0013
                for vi in idx_map["Thumb"]:
                    w_t = v_thumb_distal_w[vi]
                    if w_t <= 0.0:
                        continue
                    pt = Vector(co_tgt[vi])
                    loc, norm, _, dist = bvh_dst.find_nearest(pt)
                    if loc is not None:
                        signed = (pt - loc).dot(norm)
                        if signed < clr_t and dist < 0.012:
                            push[vi] += np.array(norm, dtype=np.float64) * ((clr_t - signed) * 0.85 * w_t)
        for _ in range(2 if resolve_thumb else 1):
            s = np.zeros_like(push)
            np.add.at(s, ea, push[eb]); np.add.at(s, eb, push[ea])
            push = (0.64 * push + 0.36 * (s / deg_safe[:, None])) if resolve_thumb else (0.72 * push + 0.28 * (s / deg_safe[:, None]))
        co_tgt += push

    if is_mf_pose:
        # 2b. Пост-сглаживание основания среднего пальца и перепонок (ПОСЛЕ солвера коллизий!), чтобы полностью убрать
        # острые вертикальные грани и ступеньки между поднятым средним и согнутыми указательным/безымянным пальцами.
        for _ in range(12):
            avg = np.zeros_like(co_tgt)
            np.add.at(avg, ea, co_tgt[eb]); np.add.at(avg, eb, co_tgt[ea])
            avg /= deg_safe[:, None]
            wm = (mf_web_w * 0.52)[:, None]
            co_tgt = co_tgt + wm * (avg - co_tgt)
            avg2 = np.zeros_like(co_tgt)
            np.add.at(avg2, ea, co_tgt[eb]); np.add.at(avg2, eb, co_tgt[ea])
            avg2 /= deg_safe[:, None]
            # Лёгкая релаксация (0.82 вместо 0.98) мягко снимает острый гребень растянутой перепонки, сохраняя округлость!
            co_tgt = co_tgt - (wm * 0.82) * (avg2 - co_tgt)

        # 2c. Плавное анатомическое укорачивание среднего пальца в «факе» (~10 мм, с 95,7 до ~85,5 мм):
        # равномерное сжатие по длине 3 фаланг (10..84 мм от основания), а подушечка и ноготь (>84 мм)
        # сдвигаются как жёсткий круглый кончик без сплющивания!
        pb_m1 = arm_ob.pose.bones["Middle01_R"]
        pb_mt = arm_ob.pose.bones["TipMiddle_R"]
        u_pose = np.array(pb_mt.tail - pb_m1.head, dtype=np.float64)
        u_pose /= max(float(np.linalg.norm(u_pose)), 1e-6)
        p0, p1_s = 0.010, 0.084
        shrink_total = 0.0105  # 10,5 мм короче — идеальная живая пропорция среднего пальца
        t_s = np.clip((mf_mid_proj - p0) / max(p1_s - p0, 1e-6), 0.0, 1.0)
        # Плавный C1-профиль укорочения (0 у перепонки -> shrink_total на кончике без излома на стыке фаланг)
        shift_len = shrink_total * (0.65 * t_s + 0.35 * (t_s * t_s * (3.0 - 2.0 * t_s))) * smoothstep(0.008, 0.022, mf_mid_proj)
        co_tgt -= (shift_len * mf_mid_w)[:, None] * u_pose[None, :]

    report_collisions("AFTER_SOLVED", co_tgt)

    # 3. Точная обратная LBS-матрица для каждой вершины
    bone_xforms = {}
    for b in arm_ob.data.bones:
        if b.use_deform and b.name.endswith("_R"):
            pb = arm_ob.pose.bones[b.name]
            m = pb.matrix @ b.matrix_local.inverted()
            bone_xforms[b.name] = np.array(m.to_3x3(), dtype=np.float64)

    delta_pose = co_tgt - co_lbs
    delta_rest_r = np.zeros((n_v, 3), dtype=np.float64)
    for i, v in enumerate(hand_r.data.vertices):
        dp = delta_pose[i]
        if np.dot(dp, dp) < 1e-12:
            continue
        M = np.zeros((3, 3), dtype=np.float64)
        wsum = 0.0
        for g in v.groups:
            bn = vg_names[g.group]
            if bn in bone_xforms:
                M += g.weight * bone_xforms[bn]
                wsum += g.weight
        if wsum > 1e-4:
            M /= wsum
            try:
                delta_rest_r[i] = np.linalg.solve(M, dp)
            except np.linalg.LinAlgError:
                delta_rest_r[i] = dp
        else:
            delta_rest_r[i] = dp
    norms = np.linalg.norm(delta_rest_r, axis=1)
    max_cap = 0.016 if is_mf_pose else 0.014
    over = norms > max_cap
    if over.any():
        delta_rest_r[over] *= (max_cap / norms[over])[:, None]
    print(f"=== {label} baked: max_delta={norms.max()*1000:.2f} mm, mean={norms[norms>1e-5].mean()*1000:.2f} mm", flush=True)
    return delta_rest_r


def bake_thumbsup_corrective_shape(arm_ob, hand_r, hand_l):
    """Запекает корректирующие формы ThumbsUpSmooth («Класс»), FistSmooth («Кулак»), ThumbsSmooth («Фу»),
    PointSmooth («Указать») и MiddleFingerSmooth («Фак») для обеих рук с драйверами на свойствах скелета."""
    shapes = [
        ("ThumbsUpSmooth",     "thumbsup_smooth",     HAND_THUMBS_UP,     False),
        ("FistSmooth",         "fist_smooth",         HAND_FIST,          True),
        ("ThumbsSmooth",       "thumbs_smooth",       HAND_THUMBS_DOWN,   False),
        ("PointSmooth",        "point_smooth",        HAND_POINT,         False),
        ("MiddleFingerSmooth", "middlefinger_smooth", HAND_MIDDLE_FINGER, True),
    ]
    deltas = [(name, prop, _solve_pose_corrective_delta(arm_ob, hand_r, pose, name, resolve_thumb=rt))
              for name, prop, pose, rt in shapes]

    for ob, side, sign_x in ((hand_r, "_R", 1.0), (hand_l, "_L", -1.0)):
        ob.shape_key_clear()
        ob.shape_key_add(name="Basis", from_mix=False)
        for name, prop_base, delta_rest_r in deltas:
            sk = ob.shape_key_add(name=name, from_mix=False)
            sk.value = 0.0
            sk.slider_min = 0.0
            sk.slider_max = 1.0
            for i, kv in enumerate(sk.data):
                d = delta_rest_r[i]
                kv.co.x += d[0] * sign_x
                kv.co.y += d[1]
                kv.co.z += d[2]
            prop = prop_base + side
            arm_ob[prop] = 0.0
            fc = sk.driver_add("value")
            drv = fc.driver
            drv.type = "SUM"
            var = drv.variables.new()
            var.name = "w"
            var.type = "SINGLE_PROP"
            var.targets[0].id_type = "OBJECT"
            var.targets[0].id = arm_ob
            var.targets[0].data_path = f'["{prop}"]'

    reset_all_bones(arm_ob)


def reset_all_bones(armature):
    for pb in armature.pose.bones:
        pb.rotation_mode = "XYZ"
        pb.rotation_euler = (0, 0, 0)
        pb.location = (0, 0, 0)
        pb.scale = (1, 1, 1)
    for prop in ALL_SHAPE_PROPS:
        if prop in armature:
            armature[prop] = 0.0
    bpy.context.view_layer.update()


def key_pose_action(armature, name, setup_fn):
    old = bpy.data.actions.get(name)
    if old:
        bpy.data.actions.remove(old)
    action = bpy.data.actions.new(name)
    action.use_fake_user = True
    armature.animation_data_create()
    armature.animation_data.action = action
    reset_all_bones(armature)
    setup_fn()
    for pb in armature.pose.bones:
        if pb.name == "Root" or "Socket" in pb.name:
            continue
        pb.rotation_mode = "XYZ"
        for f in (1, 2):
            pb.keyframe_insert("rotation_euler", frame=f)
            pb.keyframe_insert("location", frame=f)
    for prop in ALL_SHAPE_PROPS:
        if prop in armature:
            armature[prop] = 0.0
            for f in (1, 2):
                armature.keyframe_insert(f'["{prop}"]', frame=f)
    armature.animation_data.action = None
    reset_all_bones(armature)


def _pack_spec_vec(spec, has_tt):
    f = spec["hand"]["fingers"]
    vals = [
        *spec["wrist"], *spec["along"], *spec["back"], *spec["elbow_from_wrist"],
        *f["Index"], *f["Middle"], *f["Ring"], *f["Pinky"],
        *_thumb9(spec["hand"]["thumb"]),
        float(spec["hand"].get("cup", 0.0)),
        float(spec.get("twist_blend", 0.4)),
    ]
    if has_tt:
        for p in spec["thumb_targets"]:
            vals.extend(p)
    return np.array(vals, dtype=np.float64)


def _unpack_spec_vec(v, has_tt):
    res = {
        "wrist": Vector(v[0:3]),
        "along": Vector(v[3:6]).normalized(),
        "back": Vector(v[6:9]).normalized(),
        "elbow_from_wrist": Vector(v[9:12]).normalized(),
        "hand": {
            "fingers": {
                "Index":  tuple(float(x) for x in v[12:17]),
                "Middle": tuple(float(x) for x in v[17:22]),
                "Ring":   tuple(float(x) for x in v[22:27]),
                "Pinky":  tuple(float(x) for x in v[27:32]),
            },
            "thumb": tuple(float(x) for x in v[32:41]),
            "cup": float(v[41]),
        },
        "twist_blend": float(v[42]),
    }
    if has_tt:
        res["thumb_targets"] = (Vector(v[43:46]), Vector(v[46:49]), Vector(v[49:52]))
    return res


def _build_pchip_evaluator(times, specs):
    """Монотонный кубический сплайн Эрмита (Fritsch-Carlson PCHIP): C1-непрерывная скорость через промежуточные фазы
    подъёма/сгибания (без микро-остановок!) и строго нулевой перелёт (0% overshoot) на полках и у сукна стола."""
    has_tt = all("thumb_targets" in s and s["thumb_targets"] for s in specs)
    T = np.asarray(times, dtype=np.float64)
    Y = np.stack([_pack_spec_vec(s, has_tt) for s in specs], axis=0)
    m = len(T)
    D = np.zeros_like(Y)
    if m > 2:
        h = (T[1:] - T[:-1])[:, None]
        sec = (Y[1:] - Y[:-1]) / np.maximum(h, 1e-9)
        for k in range(1, m - 1):
            s0, s1 = sec[k - 1], sec[k]
            h0, h1 = h[k - 1, 0], h[k, 0]
            w1 = 2.0 * h1 + h0
            w2 = h1 + 2.0 * h0
            mono = (s0 * s1) > 1e-12
            s0_safe = np.where(mono, s0, 1.0)
            s1_safe = np.where(mono, s1, 1.0)
            D[k] = np.where(mono, (w1 + w2) / (w1 / s0_safe + w2 / s1_safe), 0.0)

    def eval_at(tq):
        if tq <= T[0]:
            return _unpack_spec_vec(Y[0], has_tt)
        if tq >= T[-1]:
            return _unpack_spec_vec(Y[-1], has_tt)
        i = int(np.searchsorted(T, tq, side="right")) - 1
        i = max(0, min(m - 2, i))
        dt = max(T[i + 1] - T[i], 1e-9)
        u = (tq - T[i]) / dt
        u2 = u * u
        u3 = u2 * u
        val = (
            (2.0 * u3 - 3.0 * u2 + 1.0) * Y[i]
            + (u3 - 2.0 * u2 + u) * dt * D[i]
            + (-2.0 * u3 + 3.0 * u2) * Y[i + 1]
            + (u3 - u2) * dt * D[i + 1]
        )
        return _unpack_spec_vec(val, has_tt)

    return eval_at


def bake_clip(armature, name, keys, shape_fn=None, shape_prop="thumbsup_smooth"):
    """Запекает клип на 60 FPS по непрерывному сплайну PCHIP между ключами [(t30, left_spec, right_spec), ...]
    без ступенчатых остановок на промежуточных ключах и с плавными весами корректирующей формы shape_prop."""
    old = bpy.data.actions.get(name)
    if old:
        bpy.data.actions.remove(old)
    action = bpy.data.actions.new(name)
    action.use_fake_user = True
    armature.animation_data_create()
    armature.animation_data.action = action
    prev = {}

    times = [float(k[0]) for k in keys]
    eval_l = _build_pchip_evaluator(times, [k[1] for k in keys])
    eval_r = _build_pchip_evaluator(times, [k[2] for k in keys])
    total_sub = int(round(times[-1] * FPS_SUB))

    for sub_fr in range(total_sub + 1):
        t30 = sub_fr / FPS_SUB
        ls = eval_l(t30)
        rs = eval_r(t30)
        reset_all_bones(armature)
        set_arm(armature, ls, "_L")
        set_arm(armature, rs, "_R")
        for pb in armature.pose.bones:
            if pb.name == "Root" or "Socket" in pb.name:
                continue
            pb.rotation_mode = "XYZ"
            eu = pb.matrix_basis.to_euler("XYZ", prev[pb.name]) if pb.name in prev else pb.matrix_basis.to_euler("XYZ")
            prev[pb.name] = eu
            pb.rotation_euler = eu
            pb.keyframe_insert("rotation_euler", frame=sub_fr + 1)
            pb.keyframe_insert("location", frame=sub_fr + 1)
        wl, wr = shape_fn(t30) if shape_fn is not None else (0.0, 0.0)
        active = {shape_prop + "_L": wl, shape_prop + "_R": wr}
        for prop in ALL_SHAPE_PROPS:
            if prop in armature:
                armature[prop] = float(active.get(prop, 0.0))
                armature.keyframe_insert(f'["{prop}"]', frame=sub_fr + 1)
    armature.animation_data.action = None
    reset_all_bones(armature)


def bake_idle_finger_tap(armature):
    """Небольшая фоновая анимация ожидания хода (Take_IdleFingerTap, 60 FPS):
    ладонь, запястье и большой палец статично лежат на сукне, а 4 пальца делают две живые мягкие
    волны постукивания по столу (Мизинец -> Безымянный -> Средний -> Указательный) и спокойно ложатся обратно."""
    name = "Take_IdleFingerTap"
    old = bpy.data.actions.get(name)
    if old:
        bpy.data.actions.remove(old)
    action = bpy.data.actions.new(name)
    action.use_fake_user = True
    armature.animation_data_create()
    armature.animation_data.action = action

    # Две мягкие ритмичные волны барабана пальцами: (время старта в 30fps-единицах, угол подъёма в MCP)
    waves = {
        "Pinky":  [(6.0, 15.0), (40.0, 14.0)],
        "Ring":   [(10.5, 16.5), (44.5, 15.5)],
        "Middle": [(15.0, 17.5), (49.0, 16.5)],
        "Index":  [(19.5, 16.0), (53.5, 15.0)],
    }
    dur_up, dur_down = 7.5, 5.5
    total_t30 = 80
    total_sub = total_t30 * FPS_SUB
    prev = {}

    def finger_pulse(fr, pulses):
        lift = 0.0
        for t0, amp in pulses:
            dt = fr - t0
            if 0.0 <= dt < dur_up:
                u = dt / dur_up
                lift = max(lift, amp * 0.5 * (1.0 - math.cos(math.pi * u)))
            elif dur_up <= dt < dur_up + dur_down:
                v = (dt - dur_up) / dur_down
                lift = max(lift, amp * 0.5 * (1.0 + math.cos(math.pi * (v ** 1.15))))
        return lift

    for sub_fr in range(total_sub + 1):
        fr = sub_fr / FPS_SUB
        rs = copy.deepcopy(RIGHT_TABLE)
        rs["hand"] = copy.deepcopy(HAND_TABLE)
        for fname, pulses in waves.items():
            lift = finger_pulse(fr, pulses)
            if lift > 1e-4:
                mcp, pip, dip, sp, rl = HAND_TABLE["fingers"][fname]
                rs["hand"]["fingers"][fname] = (mcp - lift, pip + lift * 0.24, dip + lift * 0.08, sp, rl)
        reset_all_bones(armature)
        set_arm(armature, LEFT_FAN, "_L")
        set_arm(armature, rs, "_R")
        for pb in armature.pose.bones:
            if pb.name == "Root" or "Socket" in pb.name:
                continue
            pb.rotation_mode = "XYZ"
            eu = pb.matrix_basis.to_euler("XYZ", prev[pb.name]) if pb.name in prev else pb.matrix_basis.to_euler("XYZ")
            prev[pb.name] = eu
            pb.rotation_euler = eu
            pb.keyframe_insert("rotation_euler", frame=sub_fr + 1)
            pb.keyframe_insert("location", frame=sub_fr + 1)
        for prop in ALL_SHAPE_PROPS:
            if prop in armature:
                armature[prop] = 0.0
                armature.keyframe_insert(f'["{prop}"]', frame=sub_fr + 1)
    armature.animation_data.action = None
    reset_all_bones(armature)


def build_all_animations(armature):
    bpy.context.scene.render.fps = FPS
    # 1. Pose_Base (ОБЯЗАТЕЛЬНО первая по алфавиту — покой костей в Unity!)
    key_pose_action(armature, "Pose_Base", lambda: None)
    key_pose_action(armature, "Pose_InGame", lambda: (set_arm(armature, LEFT_FAN, "_L"), set_arm(armature, RIGHT_TABLE, "_R")))
    key_pose_action(armature, "Pose_Relaxed", lambda: (pose_fingers(armature, HAND_RELAXED, "_L"), pose_fingers(armature, HAND_RELAXED, "_R")))
    key_pose_action(armature, "Pose_HoldCard", lambda: (pose_fingers(armature, HAND_HOLD_FAN, "_L"), pose_fingers(armature, HAND_HOLD_FAN, "_R")))
    key_pose_action(armature, "Pose_TakeCard", lambda: (pose_fingers(armature, HAND_TAKE_CARD, "_L"), pose_fingers(armature, HAND_TAKE_CARD, "_R")))
    key_pose_action(armature, "Pose_PlayCard", lambda: (pose_fingers(armature, HAND_TAKE_CARD, "_L"), pose_fingers(armature, HAND_TAKE_CARD, "_R")))
    key_pose_action(armature, "Pose_Point", lambda: (pose_fingers(armature, HAND_POINT, "_L"), pose_fingers(armature, HAND_POINT, "_R")))
    key_pose_action(armature, "Pose_MiddleFinger", lambda: (pose_fingers(armature, HAND_MIDDLE_FINGER, "_L"), pose_fingers(armature, HAND_MIDDLE_FINGER, "_R")))
    key_pose_action(armature, "Pose_TapTable", lambda: (pose_fingers(armature, HAND_TABLE, "_L"), pose_fingers(armature, HAND_TABLE, "_R")))
    key_pose_action(armature, "Pose_Fist", lambda: (pose_fingers(armature, HAND_FIST, "_L"), pose_fingers(armature, HAND_FIST, "_R")))

    # 2. Игровые клипы Take_*
    # Дыхание и покой: левая рука с веером мягко дышит, правая статично и плотно лежит на сукне (не парит!).
    bake_clip(armature, "Take_Idle", [
        (0, LEFT_FAN, RIGHT_TABLE),
        (40, moved_spec(LEFT_FAN, in_game.FAN_UP * 0.002), RIGHT_TABLE),
        (80, moved_spec(LEFT_FAN, in_game.FAN_UP * -0.0015), RIGHT_TABLE),
        (120, LEFT_FAN, RIGHT_TABLE),
    ])
    bake_clip(armature, "Take_TableRest", [
        (0, LEFT_TABLE, RIGHT_TABLE),
        (60, LEFT_TABLE, RIGHT_TABLE),
        (120, LEFT_TABLE, RIGHT_TABLE),
    ])
    bake_idle_finger_tap(armature)
    bake_clip(armature, "Take_HoldCards", [
        (0, LEFT_FAN, RIGHT_TABLE),
        (24, curl_spec(moved_spec(LEFT_FAN, in_game.FAN_UP * 0.0035), 2.5), RIGHT_TABLE),
        (54, curl_spec(LEFT_FAN, 0.8), RIGHT_TABLE),
        (80, LEFT_FAN, RIGHT_TABLE),
    ])
    bake_clip(armature, "Take_DrawCard", [
        (0, LEFT_FAN, RIGHT_TABLE), (18, LEFT_FAN, TOWARD_DECK_R), (25, LEFT_FAN, curl_spec(TOWARD_DECK_R, 5)),
        (44, LEFT_FAN, AT_FAN_R), (52, LEFT_FAN, curl_spec(AT_FAN_R, -6)), (70, LEFT_FAN, RIGHT_TABLE),
    ])
    bake_clip(armature, "Take_PlayCard", [
        (0, LEFT_FAN, RIGHT_TABLE), (16, LEFT_FAN, AT_FAN_R), (21, LEFT_FAN, curl_spec(AT_FAN_R, 5)),
        (36, LEFT_FAN, OVER_TABLE_R), (41, LEFT_FAN, curl_spec(OVER_TABLE_R, -10)), (60, LEFT_FAN, RIGHT_TABLE),
    ])
    # «Указать» (Take_Point): непрерывный плавный подъём без микро-остановок, два мягких акцента и плавный возврат.
    def shape_in_game_point(fr):
        if fr <= 9 or fr >= 94: return (0.0, 0.0)
        if fr < 23: s = float(smoothstep(9.0, 23.0, fr)); return (0.0, s * s)
        if fr <= 82: return (0.0, 1.0)
        s = float(smoothstep(94.0, 82.0, fr)); return (0.0, s * s)
    POINT_BEAT_R = moved_spec(POINT_R, Vector((-0.010, 0.022, 0.010)))
    bake_clip(armature, "Take_Point", [
        (0, LEFT_FAN, RIGHT_TABLE),
        (10, LEFT_FAN, IDLE_TEST_R),
        (23, LEFT_FAN, POINT_R),
        (34, LEFT_FAN, POINT_BEAT_R),
        (47, LEFT_FAN, POINT_R),
        (60, LEFT_FAN, moved_spec(POINT_R, Vector((-0.006, 0.014, 0.007)))),
        (73, LEFT_FAN, POINT_R),
        (82, LEFT_FAN, POINT_R),
        (93, LEFT_FAN, IDLE_TEST_R),
        (106, LEFT_FAN, RIGHT_TABLE),
    ], shape_fn=shape_in_game_point, shape_prop="point_smooth")

    # «Фак» (Take_MiddleFinger): большой палец ждёт снаружи, пока указательный уходит под ладонь (0 мм пересечений!).
    MF_HALF_HAND = copy.deepcopy(HAND_MIDDLE_FINGER)
    MF_HALF_HAND["thumb"] = anat_thumb(-12, -10, 14, -8, -10)
    MF_HALF_R = dict(
        orient_arm_spec(
            in_game.on_table(0.135, 0.365, 0.195),
            in_game.on_table(0.150, 0.340, -0.010),
            in_game.table_dir(-0.04, 0.14, 0.988),
            in_game.table_dir(0.08, 0.988, -0.14),
            MF_HALF_HAND,
        ),
        twist_blend=0.0,
    )
    def shape_in_game_mf(fr):
        if fr <= 9 or fr >= 100: return (0.0, 0.0)
        if fr < 25: s = float(smoothstep(9.0, 25.0, fr)); return (0.0, s * s)
        if fr <= 88: return (0.0, 1.0)
        s = float(smoothstep(100.0, 88.0, fr)); return (0.0, s * s)
    bake_clip(armature, "Take_MiddleFinger", [
        (0, LEFT_FAN, RIGHT_TABLE),
        (9, LEFT_FAN, IDLE_TEST_R),
        (16, LEFT_FAN, MF_HALF_R),
        (25, LEFT_FAN, MIDDLE_FINGER_R),
        (36, LEFT_FAN, moved_spec(MIDDLE_FINGER_R, Vector((-0.004, 0.014, 0.016)))),
        (58, LEFT_FAN, MIDDLE_FINGER_R),
        (74, LEFT_FAN, moved_spec(MIDDLE_FINGER_R, Vector((-0.003, 0.008, 0.010)))),
        (88, LEFT_FAN, MIDDLE_FINGER_R),
        (96, LEFT_FAN, MF_HALF_R),
        (102, LEFT_FAN, IDLE_TEST_R),
        (114, LEFT_FAN, RIGHT_TABLE),
    ], shape_fn=shape_in_game_mf, shape_prop="middlefinger_smooth")
    bake_clip(armature, "Take_TapTable", [
        (0, LEFT_FAN, RIGHT_TABLE),
        (7, LEFT_FAN, TAP_UP_R), (12, LEFT_FAN, TAP_DOWN_R),
        (18, LEFT_FAN, TAP_UP_R), (23, LEFT_FAN, TAP_DOWN_R),
        (29, LEFT_FAN, TAP_UP_R), (34, LEFT_FAN, TAP_DOWN_R),
        (48, LEFT_FAN, RIGHT_TABLE),
    ])
    bake_clip(armature, "Take_Shrug", [
        (0, LEFT_FAN, RIGHT_TABLE), (16, SHRUG_L, SHRUG_R), (40, SHRUG_L, curl_spec(SHRUG_R, -3)), (58, LEFT_FAN, RIGHT_TABLE),
    ])

    # ГЛАВНАЯ АНИМАЦИЯ «КЛАСС» (Take_ThumbsUp): непрерывный плавный подъём через сплайн без остановок.
    THUMBS_LIFT_R = mix_spec(IDLE_TEST_R, THUMBS_UP_R, 0.35)
    def shape_in_game_thumbs(fr):
        if fr <= 10 or fr >= 102:
            return (0.0, 0.0)
        if fr < 25:
            s = float(smoothstep(10.0, 25.0, fr))
            return (0.0, s * s)
        if fr <= 92:
            return (0.0, 1.0)
        s = float(smoothstep(102.0, 92.0, fr))
        return (0.0, s * s)
    bake_clip(armature, "Take_ThumbsUp", [
        (0, LEFT_FAN, RIGHT_TABLE),
        (9, LEFT_FAN, IDLE_TEST_R),
        (16, LEFT_FAN, THUMBS_LIFT_R),
        (25, LEFT_FAN, THUMBS_UP_R),
        (36, LEFT_FAN, moved_spec(THUMBS_UP_R, in_game.table_dir(0, 0.008, 0.012))),
        (60, LEFT_FAN, THUMBS_UP_R),
        (78, LEFT_FAN, moved_spec(THUMBS_UP_R, in_game.table_dir(0, 0.005, 0.007))),
        (92, LEFT_FAN, THUMBS_UP_R),
        (101, LEFT_FAN, IDLE_TEST_R),
        (115, LEFT_FAN, RIGHT_TABLE),
    ], shape_fn=shape_in_game_thumbs)

    def shape_in_game_td(fr):
        if fr <= 9 or fr >= 102: return (0.0, 0.0)
        if fr < 23: s = float(smoothstep(9.0, 23.0, fr)); return (0.0, s * s)
        if fr <= 90: return (0.0, 1.0)
        s = float(smoothstep(102.0, 90.0, fr)); return (0.0, s * s)
    bake_clip(armature, "Take_ThumbsDown", [
        (0, LEFT_FAN, RIGHT_TABLE),
        (10, LEFT_FAN, IDLE_TEST_R),
        (23, LEFT_FAN, THUMBS_DOWN_R),
        (34, LEFT_FAN, moved_spec(THUMBS_DOWN_R, in_game.table_dir(0, 0.008, -0.012))),
        (58, LEFT_FAN, THUMBS_DOWN_R),
        (76, LEFT_FAN, moved_spec(THUMBS_DOWN_R, in_game.table_dir(0, 0.005, -0.008))),
        (90, LEFT_FAN, THUMBS_DOWN_R),
        (100, LEFT_FAN, IDLE_TEST_R),
        (113, LEFT_FAN, RIGHT_TABLE),
    ], shape_fn=shape_in_game_td, shape_prop="thumbs_smooth")

    # Промежуточная фаза кулака: большой палец держится чуть шире снаружи, пока 4 пальца полностью сгибаются (0 мм нахлёста!).
    FIST_HALF_HAND = copy.deepcopy(HAND_FIST)
    FIST_HALF_HAND["thumb"] = anat_thumb(-14, -12, 16, -10, -12)
    FIST_HALF_R = free_arm_spec((0.155, 0.380, -0.075), (0.265, 0.240, -0.210), 68, 8, 8, FIST_HALF_HAND)
    def shape_in_game_fist(fr):
        if fr <= 9 or fr >= 103: return (0.0, 0.0)
        if fr < 23: s = float(smoothstep(9.0, 23.0, fr)); return (0.0, s * s)
        if fr <= 88: return (0.0, 1.0)
        s = float(smoothstep(103.0, 88.0, fr)); return (0.0, s * s)
    bake_clip(armature, "Take_Fist", [
        (0, LEFT_FAN, RIGHT_TABLE),
        (9, LEFT_FAN, IDLE_TEST_R),
        (15, LEFT_FAN, FIST_HALF_R),
        (23, LEFT_FAN, FIST_R),
        (32, LEFT_FAN, moved_spec(FIST_R, in_game.table_dir(0, 0.01, 0.014))),
        (56, LEFT_FAN, FIST_R),
        (74, LEFT_FAN, moved_spec(FIST_R, in_game.table_dir(0, 0.006, 0.009))),
        (88, LEFT_FAN, FIST_R),
        (95, LEFT_FAN, FIST_HALF_R),
        (102, LEFT_FAN, IDLE_TEST_R),
        (114, LEFT_FAN, RIGHT_TABLE),
    ], shape_fn=shape_in_game_fist, shape_prop="fist_smooth")

    # 3. Клипы проверки (F, U, J, H) — совместимы со слоями PoseTest и GestureTest!
    IDLE_TEST_L = mirror_spec(IDLE_TEST_R)
    FIST_L = mirror_spec(FIST_R)
    FIST_HALF_L = mirror_spec(FIST_HALF_R)
    THUMBS_UP_L = mirror_spec(THUMBS_UP_R)
    THUMBS_DOWN_L = mirror_spec(THUMBS_DOWN_R)

    bake_clip(armature, "Take_TestClench", [
        (0, IDLE_TEST_L, IDLE_TEST_R),
        (10, FIST_HALF_L, FIST_HALF_R),
        (18, FIST_L, FIST_R),
    ], shape_fn=lambda fr: (float(smoothstep(0.0, 18.0, fr))**2, float(smoothstep(0.0, 18.0, fr))**2), shape_prop="fist_smooth")
    for suffix, l_end, r_end, use_l, use_r in (
        ("", THUMBS_UP_L, THUMBS_UP_R, True, True),
        ("_R", IDLE_TEST_L, THUMBS_UP_R, False, True),
        ("_L", THUMBS_UP_L, IDLE_TEST_R, True, False),
    ):
        def _tu_shape(fr, ul=use_l, ur=use_r):
            s = float(smoothstep(0.0, 20.0, fr))
            w = s * s
            return (w if ul else 0.0, w if ur else 0.0)
        bake_clip(armature, "Take_TestThumbsUp" + suffix, [
            (0, IDLE_TEST_L, IDLE_TEST_R),
            (20, l_end, r_end),
        ], shape_fn=_tu_shape, shape_prop="thumbsup_smooth")
    for suffix, l_end, r_end, use_l, use_r in (
        ("", THUMBS_DOWN_L, THUMBS_DOWN_R, True, True),
        ("_R", IDLE_TEST_L, THUMBS_DOWN_R, False, True),
        ("_L", THUMBS_DOWN_L, IDLE_TEST_R, True, False),
    ):
        def _td_shape(fr, ul=use_l, ur=use_r):
            s = float(smoothstep(0.0, 24.0, fr))
            w = s * s
            return (w if ul else 0.0, w if ur else 0.0)
        bake_clip(armature, "Take_TestThumbsDown" + suffix, [
            (0, IDLE_TEST_L, IDLE_TEST_R),
            (24, l_end, r_end),
        ], shape_fn=_td_shape, shape_prop="thumbs_smooth")
    print(f"=== BAKED {len(bpy.data.actions)} ACTIONS CLEANLY AT {FPS} FPS!", flush=True)


def render_preview():
    """Рендерит 2x4 лист проверки с реальной игровой камеры (с веером карт, столом, крупным планом кулака и рукава)."""
    scratch = r"C:\Users\Пользователь\.gemini\antigravity\brain\1bf10b84-65f0-44e9-9cd5-d85e00545270\scratch"
    arm_ob, meshes = build_rig_and_meshes()
    hand_l, hand_r, sleeve_l, sleeve_r = meshes
    place_fan_socket(arm_ob)
    bake_thumbsup_corrective_shape(arm_ob, hand_r, hand_l)

    # Импортируем статичную модель автора для сравнения 1-в-1
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=REF_FBX)
    ref_ob = next(o for o in bpy.data.objects if o not in before and o.type == "MESH")
    ref_ob.hide_render = True

    # Простой макет стола и 6 карт на SocketFan_L для визуального контроля посадки в игре!
    mat_felt = bpy.data.materials.new("Felt")
    mat_felt.use_nodes = True
    mat_felt.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.32, 0.04, 0.06, 1.0)
    mat_felt.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.88
    bpy.ops.mesh.primitive_cylinder_add(vertices=48, radius=0.84, depth=0.04, location=(0, 0, 0))
    table = bpy.context.active_object
    table.scale = (1.0, 0.72, 1.0)
    # Центр стола — на 0.78м вперёд от глаз и на 0.54м ниже глаз (в координатах стола)
    table_center = in_game.on_table(0, 0.92, -0.02)
    table.matrix_world = bone_frame(table_center, in_game.TABLE_AHEAD, in_game.TABLE_UP)
    table.data.materials.append(mat_felt)

    # 6 проверочных карт веера
    mat_card = bpy.data.materials.new("Card")
    mat_card.use_nodes = True
    mat_card.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.86, 0.80, 0.68, 1.0)
    mat_card.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.45
    cards = []
    pivot, fan_r, fan_u, fan_d = in_game.fan_axes()
    for i in range(6):
        ang = math.radians((i - 2.5) * 11.5)
        bpy.ops.mesh.primitive_cube_add(size=1.0)
        c = bpy.context.active_object
        cw, ch, ct = 0.063 * in_game.CARD_SCALE, 0.088 * in_game.CARD_SCALE, 0.0005
        c.scale = (cw, ch, ct)
        bpy.ops.object.transform_apply(scale=True)
        rot_f = Matrix((fan_r, fan_u, fan_d)).transposed().to_4x4() @ Matrix.Rotation(-ang, 4, "Z")
        pos = pivot + rot_f.to_3x3() @ Vector((0, ch * 0.43, (i - 2.5) * 0.0009))
        c.matrix_world = Matrix.Translation(pos) @ rot_f
        c.data.materials.append(mat_card)
        cards.append(c)

    # Освещение и камера от глаз игрока (0,0,0), взгляд по +Y, вверх +Z (как в Unity TableCameraController!)
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.render.threads_mode = "FIXED"
    sc.render.threads = 4
    sc.cycles.device = "CPU"
    sc.cycles.samples = 16
    sc.cycles.use_denoising = False
    sc.cycles.max_bounces = 2
    sc.render.resolution_x = 560
    sc.render.resolution_y = 315
    sc.world = bpy.data.worlds.new("W")
    sc.world.use_nodes = True
    sc.world.node_tree.nodes["Background"].inputs[0].default_value = (0.10, 0.08, 0.08, 1.0)
    sc.world.node_tree.nodes["Background"].inputs[1].default_value = 0.55

    def add_sun(name, dir_v, energy, color=(1, 0.96, 0.92)):
        ld = bpy.data.lights.new(name, "SUN"); ld.energy = energy; ld.color = color; ld.angle = math.radians(18)
        lo = bpy.data.objects.new(name, ld)
        lo.rotation_euler = Vector(dir_v).to_track_quat("-Z", "Y").to_euler()
        sc.collection.objects.link(lo)
    add_sun("Key", (0.2, 0.7, -0.65), 2.4, (1.0, 0.95, 0.88))
    add_sun("Fill", (-0.1, 0.9, -0.35), 1.3, (1.0, 0.95, 0.90))
    add_sun("Bounce", (0.1, -0.3, 0.9), 0.55, (1.0, 0.72, 0.62))

    cam_d = bpy.data.cameras.new("Eye")
    cam_d.lens_unit = "FOV"
    cam_d.angle = math.radians(66)  # ~как горизонтальный/вертикальный FOV телефона/ПК
    cam = bpy.data.objects.new("Eye", cam_d)
    sc.collection.objects.link(cam)
    sc.camera = cam

    def set_eye_cam(pos=Vector((0, 0, 0)), target=Vector((0, 1, 0)), up=Vector((0, 0, 1)), fov=66):
        cam_d.angle = math.radians(fov)
        fwd = (target - pos).normalized()
        right = fwd.cross(up).normalized()
        up_o = right.cross(fwd).normalized()
        cam.matrix_world = Matrix.Translation(pos) @ Matrix((right, up_o, -fwd)).transposed().to_4x4()

    def snap(tag):
        p = os.path.join(scratch, f"ti_{tag}.png")
        sc.render.filepath = p
        bpy.ops.render.render(write_still=True)

    # 1. Обычный стол: левая держит веер, правая лежит на сукне
    set_eye_cam()
    reset_all_bones(arm_ob)
    set_arm(arm_ob, LEFT_FAN, "_L"); set_arm(arm_ob, RIGHT_TABLE, "_R")
    snap("0_table")

    # 2. Начало анимации «Класс»: рука поднимается со стола
    reset_all_bones(arm_ob)
    set_arm(arm_ob, LEFT_FAN, "_L"); set_arm(arm_ob, mix_spec(RIGHT_TABLE, IDLE_TEST_R, 0.85), "_R")
    snap("1_lift")

    # 3. Середина анимации «Класс»: пальцы сгибаются на глазах
    reset_all_bones(arm_ob)
    set_arm(arm_ob, LEFT_FAN, "_L"); set_arm(arm_ob, mix_spec(IDLE_TEST_R, THUMBS_UP_R, 0.5), "_R")
    snap("2_mid")

    # 4. Пик «Класса» в игре (с ThumbsUpSmooth = 1.0, точно как в Unity!)
    reset_all_bones(arm_ob)
    set_arm(arm_ob, LEFT_FAN, "_L"); set_arm(arm_ob, THUMBS_UP_R, "_R")
    arm_ob["thumbsup_smooth_R"] = 1.0; bpy.context.view_layer.update()
    snap("3_thumbsup")

    # 5. Крупный план «Класса» с места игрока (зум прямо на 4 пальца в кулаке, как на скрине автора!)
    set_eye_cam(Vector((0.0, 0.0, 0.0)), THUMBS_UP_R["wrist"] + Vector((-0.025, 0.055, 0.045)), Vector((0, 0, 1)), fov=32)
    snap("4_closeup")

    # 6. Крупный план рукава от манжеты до локтя (проверка бесшовной вязки без полос!)
    reset_all_bones(arm_ob)
    set_arm(arm_ob, LEFT_FAN, "_L"); set_arm(arm_ob, RIGHT_TABLE, "_R")
    set_eye_cam(Vector((0.02, 0.08, -0.05)), RIGHT_TABLE["wrist"] + Vector((0.05, -0.10, -0.02)), Vector((0, 0, 1)), fov=42)
    snap("5_thumbsdown")

    # 7. «Кулак» в игре
    set_eye_cam()
    reset_all_bones(arm_ob)
    set_arm(arm_ob, LEFT_FAN, "_L"); set_arm(arm_ob, FIST_R, "_R")
    snap("6_fist")

    # 8. Проверка U (обе руки показывают «Класс» с ThumbsUpSmooth = 1.0, веер спрятан)
    for c in cards:
        c.hide_render = True
    reset_all_bones(arm_ob)
    set_arm(arm_ob, mirror_spec(THUMBS_UP_R), "_L"); set_arm(arm_ob, THUMBS_UP_R, "_R")
    arm_ob["thumbsup_smooth_L"] = 1.0; arm_ob["thumbsup_smooth_R"] = 1.0; bpy.context.view_layer.update()
    snap("7_both_thumbsup")

    # Склейка 2x4 (2240 x 630)
    rows = [
        ["0_table", "1_lift", "2_mid", "3_thumbsup"],
        ["4_closeup", "5_thumbsdown", "6_fist", "7_both_thumbsup"],
    ]
    tw, th = 560, 315
    sheet = np.zeros((th * 2, tw * 4, 4), dtype=np.float32)
    for r_i, row in enumerate(rows):
        for c_i, tag in enumerate(row):
            im = bpy.data.images.load(os.path.join(scratch, f"ti_{tag}.png"))
            px = np.empty(tw * th * 4, dtype=np.float32)
            im.pixels.foreach_get(px)
            px = px.reshape(th, tw, 4)
            bpy.data.images.remove(im)
            yr = (1 - r_i) * th
            sheet[yr:yr + th, c_i * tw:(c_i + 1) * tw] = px
    out = bpy.data.images.new("InGameTripoPreview", tw * 4, th * 2)
    out.pixels.foreach_set(sheet.ravel())
    out.filepath_raw = os.path.join(scratch, "tripo_ingame_preview.png")
    out.file_format = "PNG"
    out.save()
    print("=== SAVED INGAME PREVIEW:", out.filepath_raw, flush=True)


def build_and_export():
    t0 = time.time()
    arm_ob, meshes = build_rig_and_meshes()
    hand_l, hand_r, sleeve_l, sleeve_r = meshes
    place_fan_socket(arm_ob)
    bake_thumbsup_corrective_shape(arm_ob, hand_r, hand_l)
    build_all_animations(arm_ob)
    bpy.ops.wm.save_as_mainfile(filepath=OUT_BLEND)
    print(f"=== SAVED BLEND: {OUT_BLEND} ({round(os.path.getsize(OUT_BLEND)/1024/1024, 2)} MB)", flush=True)

    # Экспорт FBX (формат и оси 1-в-1 как scripts/3d/hands/export.py!)
    bpy.ops.object.select_all(action="DESELECT")
    arm_ob.select_set(True)
    for m in meshes:
        m.select_set(True)
    bpy.context.view_layer.objects.active = arm_ob
    arm_ob.data.pose_position = "POSE"
    reset_all_bones(arm_ob)
    arm_ob.animation_data.action = None
    bpy.context.view_layer.update()

    bpy.ops.export_scene.fbx(
        filepath=OUT_FBX, use_selection=True, object_types={"ARMATURE", "MESH"},
        axis_forward="-Z", axis_up="Y",
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL", bake_space_transform=False,
        use_mesh_modifiers=False, mesh_smooth_type="FACE", use_tspace=True, add_leaf_bones=False,
        primary_bone_axis="Y", secondary_bone_axis="X", armature_nodetype="NULL",
        bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False, bake_anim_use_all_actions=True,
        bake_anim_force_startend_keying=True, bake_anim_step=1.0, bake_anim_simplify_factor=0.0,
        path_mode="RELATIVE", embed_textures=False,
    )
    print(f"=== EXPORTED FBX: {OUT_FBX} ({round(os.path.getsize(OUT_FBX)/1024/1024, 2)} MB) in {time.time()-t0:.1f}s", flush=True)


if __name__ == "__main__":
    cmd = (sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else ["preview"])[0]
    {"preview": render_preview, "build": build_and_export}[cmd]()
