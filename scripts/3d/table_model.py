"""Стол и стул клуба для 3D-режима, размеры в метрах по листу генерации (генерации/2-стол-и-стул/готово/лист.jpg).
Модуль для Blender: build_table и build_chair строят объекты, верх стола берёт art3d/table/table-top.png."""
import math
import os

import bmesh
import bpy
from mathutils import Matrix, Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
TOP_SOURCE = os.path.join(ROOT, "art3d", "table", "table-top.png")
TOP_TEXTURE = os.path.join(ROOT, "art3d", "table", "table-top-stadium.png")

TABLE_A, TABLE_B, TABLE_Z = 1.16, .72, .76  # Покерный овально-капсульный стол 2,32 × 1,44 м и высота сукна.
RIM = .08                                    # Ширина золотого обода.
CHAIR_SEAT_Z = .48


def principled(m):
    """Узел шейдера ищется по типу: его имя зависит от языка Blender."""
    return next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")


def material(name, color, rough, metal=0., sheen=0.):
    m = bpy.data.materials.get(name)
    if m:
        return m
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    p = principled(m)
    p.inputs["Base Color"].default_value = (*color, 1)
    p.inputs["Roughness"].default_value = rough
    p.inputs["Metallic"].default_value = metal
    if "Sheen Weight" in p.inputs:
        p.inputs["Sheen Weight"].default_value = sheen
    return m


def gold():
    return material("gold", (.86, .54, .16), .3, .95)


def wood():
    return material("wood", (.075, .024, .012), .3)


def velvet():
    return material("velvet", (.19, .005, .02), .7, sheen=.2)


def build_stadium_top_texture():
    """Переносит оригинальное клубное сукно, золотой борт, пунктирную строчку и идеально круглую трефу
    из art3d/table/table-top.png на новый покерный овально-капсульный стол (2,32 × 1,44 м) без растяжения герба."""
    import numpy as np
    src = bpy.data.images.load(TOP_SOURCE, check_existing=True)
    sw, sh = src.size[0], src.size[1]
    buf = np.empty(sw * sh * 4, dtype=np.float32)
    src.pixels.foreach_get(buf)
    src_arr = buf.reshape((sh, sw, 4))

    def sample(u, v):
        ux = np.clip(u, 0.0, 1.0) * (sw - 1)
        vy = np.clip(v, 0.0, 1.0) * (sh - 1)
        x0 = np.clip(ux.astype(np.int32), 0, sw - 2)
        y0 = np.clip(vy.astype(np.int32), 0, sh - 2)
        wx = (ux - x0)[..., None]
        wy = (vy - y0)[..., None]
        return (
            src_arr[y0, x0] * (1.0 - wx) * (1.0 - wy)
            + src_arr[y0, x0 + 1] * wx * (1.0 - wy)
            + src_arr[y0 + 1, x0] * (1.0 - wx) * wy
            + src_arr[y0 + 1, x0 + 1] * wx * wy
        )

    # Итоговая текстура точно под пропорции 2.32 : 1.44 (2048 x 1272)
    ow, oh = 2048, int(round(2048 * (TABLE_B / TABLE_A)))
    xs = np.linspace(-TABLE_A, TABLE_A, ow, dtype=np.float32)
    ys = np.linspace(-TABLE_B, TABLE_B, oh, dtype=np.float32)
    xx, yy = np.meshgrid(xs, ys)

    L = TABLE_A - TABLE_B  # 0.44 м — половина прямой части стола
    xc = np.clip(xx, -L, L)
    rc = np.hypot(xx - xc, yy)
    d_edge = TABLE_B - rc  # 0 на внешнем краю золотого борта, +0.08 начало сукна, +0.105 строчка

    # 1. Базовое бархатное бордовое сукно без полос и без радиальных штрихов:
    # вырезаем чистый участок фактуры бархата из оригинала, вычитаем из него собственный градиент освещения (высокочастотный фильтр),
    # оставляя только подлинный ворс бархата, и накладываем ровную клубную виньетку как в оригинале!
    pu = np.linspace(0.16, 0.32, 320, dtype=np.float32)
    pv = np.linspace(0.32, 0.68, 520, dtype=np.float32)
    pgg_u, pgg_v = np.meshgrid(pu, pv)
    src_patch = sample(pgg_u, pgg_v)[..., :3]
    # Убираем плавный световой градиент внутри патча (оставляем чистую микрофактуру ткани вокруг 1.0)
    col_profile = src_patch.mean(axis=0, keepdims=True)
    row_profile = src_patch.mean(axis=1, keepdims=True)
    patch_avg = src_patch.mean(axis=(0, 1), keepdims=True)
    flat_trend = (col_profile * row_profile) / np.maximum(patch_avg, 1e-4)
    grain_patch = np.clip(src_patch / np.maximum(flat_trend, 1e-4), 0.72, 1.32)

    # Зеркально тайлим чистую микрофактуру ворса в реальных метрах (1:1 масштаб ткани, без видимых швов и полос!)
    tile_w, tile_h = 0.30, 0.48
    tx = np.abs(np.mod(xx / tile_w + 0.5, 2.0) - 1.0) * (grain_patch.shape[1] - 1)
    ty = np.abs(np.mod(yy / tile_h + 0.5, 2.0) - 1.0) * (grain_patch.shape[0] - 1)
    gx0 = np.clip(tx.astype(np.int32), 0, grain_patch.shape[1] - 2)
    gy0 = np.clip(ty.astype(np.int32), 0, grain_patch.shape[0] - 2)
    gwx = (tx - gx0)[..., None]
    gwy = (ty - gy0)[..., None]
    grain = (
        grain_patch[gy0, gx0] * (1.0 - gwx) * (1.0 - gwy)
        + grain_patch[gy0, gx0 + 1] * gwx * (1.0 - gwy)
        + grain_patch[gy0 + 1, gx0] * (1.0 - gwx) * gwy
        + grain_patch[gy0 + 1, gx0 + 1] * gwx * gwy
    )

    # Эталонный цвет сукна из оригинала у центра (рядом с трефой) и у края (внутри строчки)
    col_center = sample(np.array([[0.33, 0.67]], dtype=np.float32), np.array([[0.50, 0.50]], dtype=np.float32))[..., :3].mean(axis=(0, 1))
    col_outer = sample(np.array([[0.15, 0.85]], dtype=np.float32), np.array([[0.50, 0.50]], dtype=np.float32))[..., :3].mean(axis=(0, 1))
    # Плавная овально-стадионная подсветка под центральной клубной лампой
    r_glow = np.clip(np.hypot(xx / (TABLE_A - 0.12), yy / (TABLE_B - 0.12)), 0.0, 1.0)
    t_vig = (r_glow ** 1.45)[..., None]
    base_rgb = col_center[None, None, :] * (1.0 - t_vig) + col_outer[None, None, :] * t_vig
    out = np.ones((oh, ow, 4), dtype=np.float32)
    out[..., :3] = np.clip(base_rgb * grain, 0.0, 1.0)

    # 2. Золотой обод + тонкая пунктирная строчка вдоль всего периметра стадиона (по точной длине дуги и истинной границе обода!)
    perim = 4.0 * L + 2.0 * math.pi * TABLE_B
    dx = xx - xc
    dy = yy
    phi = np.arctan2(dy, dx)  # [-pi, pi]
    phi_pos = np.mod(phi, 2.0 * math.pi)
    s = np.empty_like(xx)
    m_rt = (xx > L) & (yy >= 0.0)
    m_top = (np.abs(xx) <= L) & (yy >= 0.0)
    m_left = xx < -L
    m_bot = (np.abs(xx) <= L) & (yy < 0.0)
    m_rb = (xx > L) & (yy < 0.0)
    s[m_rt] = TABLE_B * phi[m_rt]
    s[m_top] = 0.5 * math.pi * TABLE_B + (L - xx[m_top])
    s[m_left] = 0.5 * math.pi * TABLE_B + 2.0 * L + TABLE_B * (phi_pos[m_left] - 0.5 * math.pi)
    s[m_bot] = 1.5 * math.pi * TABLE_B + 2.0 * L + (xx[m_bot] + L)
    s[m_rb] = 1.5 * math.pi * TABLE_B + 4.0 * L + TABLE_B * (phi[m_rb] + 0.5 * math.pi)
    frac = np.mod(s / perim, 1.0)

    # Переводим долю дуги стадиона в угол старого эллипса (0.92 × 0.65) по его собственной длине дуги
    a_old, b_old = 0.92, 0.65
    th_lut = np.linspace(0.0, 2.0 * math.pi, 4097, dtype=np.float64)
    ds_lut = np.hypot(-a_old * np.sin(th_lut), b_old * np.cos(th_lut))
    cum_s = np.cumsum(0.5 * (ds_lut[:-1] + ds_lut[1:]) * (th_lut[1] - th_lut[0]))
    cum_s = np.concatenate(([0.0], cum_s))
    cum_s /= cum_s[-1]
    th_ell = np.interp(frac, cum_s, th_lut).astype(np.float32)

    # Измеряем внутреннюю кромку золотого борта и аппроксимируем её гладкой 2-гармоникой (только центр и эксцентриситет):
    # так убираются смещение и сжатие исходника, а локальных вмятин и волн на борте быть в принципе не может!
    n_ang = 360
    th_scan = np.linspace(0.0, 2.0 * math.pi, n_ang, endpoint=False, dtype=np.float32)
    d_scan = np.linspace(0.055, 0.095, 161, dtype=np.float32)
    ex_s = a_old * np.cos(th_scan)[:, None]
    ey_s = b_old * np.sin(th_scan)[:, None]
    nx_s = np.cos(th_scan) / a_old
    ny_s = np.sin(th_scan) / b_old
    nl_s = np.hypot(nx_s, ny_s)
    nx_s = (nx_s / nl_s)[:, None]
    ny_s = (ny_s / nl_s)[:, None]
    scan_col = sample(0.5 + (ex_s - d_scan[None, :] * nx_s) / (2.0 * a_old),
                      0.5 + (ey_s - d_scan[None, :] * ny_s) / (2.0 * b_old))
    # Порог по яркости золота (высокий R и G) — берём только надёжные яркие участки борта, отсекая тёмные блики
    is_gold = (scan_col[..., 1] > 0.25) & (scan_col[..., 0] > 0.50)
    valid_th, valid_d = [], []
    for k in range(n_ang):
        row = is_gold[k]
        # Идём от сукна (d=0.090) наружу к золоту; ищем сплошную полосу золота (>= 8 пикселей)
        start_i = int(np.searchsorted(d_scan, 0.088))
        for idx in range(start_i, 8, -1):
            if row[idx] and row[idx - 4] and row[idx - 8]:
                d_val = float(d_scan[idx])
                if 0.072 <= d_val <= 0.085:
                    valid_th.append(float(th_scan[k]))
                    valid_d.append(d_val)
                break
    vth = np.array(valid_th, dtype=np.float64)
    vd = np.array(valid_d, dtype=np.float64)
    # Аппроксимация строго 2 гармониками (C-infinity гладкая коррекция эллипса исходника без единой волны!)
    A_mat = np.column_stack([np.ones_like(vth), np.cos(vth), np.sin(vth), np.cos(2 * vth), np.sin(2 * vth)])
    coef, _, _, _ = np.linalg.lstsq(A_mat, vd, rcond=None)
    d_inner_px = (
        coef[0]
        + coef[1] * np.cos(th_ell)
        + coef[2] * np.sin(th_ell)
        + coef[3] * np.cos(2.0 * th_ell)
        + coef[4] * np.sin(2.0 * th_ell)
    ).astype(np.float32)
    d_src = np.clip(d_edge + (d_inner_px - RIM), 0.006, 0.22)

    ex = a_old * np.cos(th_ell)
    ey = b_old * np.sin(th_ell)
    nx_e = np.cos(th_ell) / a_old
    ny_e = np.sin(th_ell) / b_old
    n_len = np.hypot(nx_e, ny_e)
    nx_e /= n_len
    ny_e /= n_len
    u_rim = 0.5 + (ex - d_src * nx_e) / (2.0 * a_old)
    v_rim = 0.5 + (ey - d_src * ny_e) / (2.0 * b_old)
    rim_col = sample(u_rim, v_rim)
    # Подгоняем яркость краёв базового сукна точно к цвету сукна прямо внутри строчки, чтобы сшивка была 100% невидимой!
    inner_edge_sample = sample(0.5 + (ex - (d_inner_px + 0.055) * nx_e) / (2.0 * a_old),
                               0.5 + (ey - (d_inner_px + 0.055) * ny_e) / (2.0 * b_old))
    edge_mean = inner_edge_sample[..., :3].mean(axis=(0, 1))
    out_edge_mask = (d_edge > 0.125) & (d_edge < 0.165)
    out_edge_mean = out[out_edge_mask, :3].mean(axis=0)
    match_gain = edge_mean / np.maximum(out_edge_mean, 1e-4)
    w_match = np.clip((0.32 - d_edge) / 0.18, 0.0, 1.0)[..., None]
    out[..., :3] = np.clip(out[..., :3] * (1.0 - w_match + w_match * match_gain[None, None, :]), 0.0, 1.0)

    # Плавная сшивка зоны борта+строчки (d_edge <= 0.125м) с полем сукна (0.125..0.165м)
    w_rim = np.clip((0.160 - d_edge) / 0.035, 0.0, 1.0)[..., None]
    out = out * (1.0 - w_rim) + rim_col * w_rim

    # 3. Идеально круглая центральная вышитая золотая трефа (1:1 метры -> без растяжения!)
    emblem_scale = 1.05
    u_emb = 0.5 + xx / (1.84 * emblem_scale)
    v_emb = 0.5 + yy / (1.30 * emblem_scale)
    r_emb = np.hypot(xx, yy)
    emb_col = sample(u_emb, v_emb)
    ring_mask = (r_emb > 0.225) & (r_emb < 0.245)
    emb_ring_mean = emb_col[ring_mask, :3].mean(axis=0)
    out_ring_mean = out[ring_mask, :3].mean(axis=0)
    center_gain = emb_ring_mean / np.maximum(out_ring_mean, 1e-4)
    w_center = np.clip((0.46 - r_emb) / 0.22, 0.0, 1.0)[..., None]
    out[..., :3] = np.clip(out[..., :3] * (1.0 - w_center + w_center * center_gain[None, None, :]), 0.0, 1.0)

    w_emb = np.clip((0.242 - r_emb) / 0.026, 0.0, 1.0)[..., None]
    out = out * (1.0 - w_emb) + emb_col * w_emb
    out[:, :, 3] = 1.0

    # Слегка выровняем sRGB/Linear? Нет: img.pixels в Blender хранит linear, src_arr тоже linear -> save() вернёт точную sRGB!
    img = bpy.data.images.new("table-top-stadium", width=ow, height=oh, alpha=False)
    img.pixels.foreach_set(np.ascontiguousarray(out, dtype=np.float32).ravel())
    img.filepath_raw = TOP_TEXTURE
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)
    print(f"=== BUILT STADIUM TABLE TOP TEXTURE: {TOP_TEXTURE} ({ow}x{oh})", flush=True)


def table_top_material():
    # Всегда пересобираем текстуру стадиона, если изменился скрипт или исходник
    src_mtime = max(os.path.getmtime(TOP_SOURCE), os.path.getmtime(__file__))
    if not os.path.exists(TOP_TEXTURE) or src_mtime > os.path.getmtime(TOP_TEXTURE):
        build_stadium_top_texture()
    m = bpy.data.materials.get("table top")
    if m:
        return m
    m = bpy.data.materials.new("table top")
    m.use_nodes = True
    nodes, links = m.node_tree.nodes, m.node_tree.links
    tex = nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(TOP_TEXTURE, check_existing=True)
    tex.extension = "EXTEND"
    p = principled(m)
    links.new(tex.outputs["Color"], p.inputs["Base Color"])
    p.inputs["Roughness"].default_value = .62
    return m


def finish(bm, name, materials, smooth=True, split=38):
    me = bpy.data.meshes.new(name)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    for m in materials:
        me.materials.append(m)
    for poly in me.polygons:
        poly.use_smooth = smooth
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    if split:
        mod = ob.modifiers.new("Edge Split", "EDGE_SPLIT")
        mod.split_angle = math.radians(split)
    return ob


def stadium_ring_xy(a, b, n=128):
    """Контур покерного овально-капсульного стола: две параллельные прямые стороны и два полукруглых торца,
    равномерно разбитые по длине периметра (при a <= b плавно превращается в круг/овал)."""
    if a <= b + 1e-5:
        return [(a * math.cos(i / n * math.tau), b * math.sin(i / n * math.tau)) for i in range(n)]
    L = a - b
    perim = 4.0 * L + 2.0 * math.pi * b
    pts = []
    for i in range(n):
        s = (i / n) * perim
        # Обход против часовой от правого торца (+a, 0)
        if s < 0.5 * math.pi * b:
            th = s / b
            pts.append((L + b * math.cos(th), b * math.sin(th)))
        elif s < 0.5 * math.pi * b + 2.0 * L:
            u = s - 0.5 * math.pi * b
            pts.append((L - u, b))
        elif s < 1.5 * math.pi * b + 2.0 * L:
            th = 0.5 * math.pi + (s - 0.5 * math.pi * b - 2.0 * L) / b
            pts.append((-L + b * math.cos(th), b * math.sin(th)))
        elif s < 1.5 * math.pi * b + 4.0 * L:
            u = s - 1.5 * math.pi * b - 2.0 * L
            pts.append((-L + u, -b))
        else:
            th = 1.5 * math.pi + (s - 1.5 * math.pi * b - 4.0 * L) / b
            pts.append((L + b * math.cos(th), b * math.sin(th)))
    return pts


def oval_rings(bm, rings, n=128, top_uv=False, stadium=False):
    """Кольца овала или капсульного стола (a, b, z, материал полосы до следующего кольца); материал первого кольца — у заливки середины."""
    uv = bm.loops.layers.uv.verify()
    all_rings = []
    for a, b, z, _ in rings:
        xy = stadium_ring_xy(a, b, n) if stadium else [(a * math.cos(i / n * math.tau), b * math.sin(i / n * math.tau)) for i in range(n)]
        all_rings.append([bm.verts.new((x, y, z)) for x, y in xy])
    for r in range(len(rings) - 1):
        for i in range(n):
            j = (i + 1) % n
            f = bm.faces.new((all_rings[r][i], all_rings[r][j], all_rings[r + 1][j], all_rings[r + 1][i]))
            f.material_index = rings[r][3]
    for ring, cap in ((all_rings[0], rings[0]), (all_rings[-1], rings[-1])):
        centre = bm.verts.new((0, 0, cap[2]))
        for i in range(n):
            f = bm.faces.new((centre, ring[i], ring[(i + 1) % n]))
            f.material_index = cap[3]
    if top_uv:
        # Верх закрыт картинкой сверху: плоская проекция по габариту стола.
        for f in bm.faces:
            for loop in f.loops:
                x, y, _ = loop.vert.co
                loop[uv].uv = (.5 + x / (2 * TABLE_A), .5 + y / (2 * TABLE_B))


def add_box(bm, centre, size, index, bevel=0., segments=2):
    made = bmesh.ops.create_cube(bm, size=1, matrix=Matrix.Translation(centre) @ Matrix.Diagonal((*size, 1)))
    verts = made["verts"]
    faces = {f for v in verts for f in v.link_faces}
    for f in faces:
        f.material_index = index
    if bevel:
        edges = list({e for v in verts for e in v.link_edges})
        result = bmesh.ops.bevel(bm, geom=list(verts) + edges, offset=bevel, offset_type="OFFSET", segments=segments, profile=.5, affect="EDGES")
        for f in result["faces"]:
            f.material_index = index


def add_tube(bm, points, radii, index, sides=10):
    """Трубка по точкам: круглое сечение поперёк направления; подходит и для ножек с точёным профилем."""
    points = [Vector(p) for p in points]
    rings = []
    for i, p in enumerate(points):
        d = (points[min(i + 1, len(points) - 1)] - points[max(i - 1, 0)]).normalized()
        x = d.cross(Vector((1, 0, 0)) if abs(d.x) < .9 else Vector((0, 1, 0))).normalized()
        y = d.cross(x)
        rings.append([bm.verts.new(p + (x * math.cos(k / sides * math.tau) + y * math.sin(k / sides * math.tau)) * radii[i]) for k in range(sides)])
    for r in range(len(rings) - 1):
        for k in range(sides):
            m = (k + 1) % sides
            f = bm.faces.new((rings[r][k], rings[r][m], rings[r + 1][m], rings[r + 1][k]))
            f.material_index = index
    for ring in (rings[0], rings[-1]):
        f = bm.faces.new(ring)
        f.material_index = index


def export_fbx(objects, path):
    """Выгрузка для Unity. В игре модель разворачивается на 180° вокруг вертикали, тогда +Y Blender смотрит вперёд."""
    bpy.context.view_layer.update()
    bpy.ops.object.select_all(action="DESELECT")
    for ob in objects:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH"},
                             axis_forward="-Z", axis_up="Y", apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
                             bake_space_transform=True, use_mesh_modifiers=True, mesh_smooth_type="FACE", bake_anim=False)
    print("EXPORTED", os.path.basename(path), sum(len(ob.data.polygons) for ob in objects), "faces")


def build_table():
    a, b, z = TABLE_A, TABLE_B, TABLE_Z
    inset = lambda d, h, m: (a - d, b - d, h, m)
    # 0 — верх с картинкой (капсульное сукно + строчка + золотой борт), 1 — золото, 2 — дерево.
    rings = [inset(RIM, z, 0), inset(.074, z + .01, 0), inset(.045, z + .015, 0), inset(.012, z + .01, 0),
             inset(0, z - .002, 1), inset(0, z - .033, 1), inset(.006, z - .033, 2), inset(.006, z - .089, 1),
             inset(0, z - .089, 1), inset(0, z - .105, 2), (.82, .45, z - .105, 2), (.82, .45, z - .136, 2),
             (.72, .30, z - .136, 2), (.72, .30, z - .175, 2), (.68, .26, z - .19, 2)]
    bm = bmesh.new()
    oval_rings(bm, rings, n=128, top_uv=True, stadium=True)
    top = finish(bm, "Table", [table_top_material(), gold(), wood()])

    # Парное клубное подстолье для длинного покерного стола: две массивные колонны из красного дерева с золотыми полосами
    # и общее капсульное основание на полу.
    bm = bmesh.new()
    for px in (-0.42, 0.42):
        add_box(bm, (px, 0, .33), (.30, .32, .52), 1, .006)
        for side in (-1, 1):
            add_box(bm, (px + side * .11, .161, .33), (.012, .004, .46), 0)
            add_box(bm, (px + side * .11, -.161, .33), (.012, .004, .46), 0)
            add_box(bm, (px + .151, side * .11, .33), (.004, .012, .46), 0)
            add_box(bm, (px - .151, side * .11, .33), (.004, .012, .46), 0)
    # Соединительная перемычка между колоннами
    add_box(bm, (0, 0, .20), (.56, .14, .10), 1, .005)
    add_box(bm, (0, .071, .20), (.52, .004, .016), 0)
    add_box(bm, (0, -.071, .20), (.52, .004, .016), 0)
    oval_rings(bm, [(.68, .28, .10, 1), (.68, .28, .075, 1), (.76, .34, .07, 1),
                    (.78, .36, .05, 0), (.80, .38, .03, 0), (.80, .38, 0, 0)], n=64, stadium=True)
    base = finish(bm, "Table base", [gold(), wood()])
    base.parent = top
    return top


def back_outline():
    """Половина спинки по виду сзади: (ширина от середины, высота). Низ у́же, самая широкая часть — чуть выше середины."""
    half = [(.165, .36), (.165, .48), (.185, .58), (.215, .70), (.228, .78), (.215, .85), (.175, .9), (.1, .925), (0, .935)]
    keys = [(x, z) for x, z in half] + [(-x, z) for x, z in reversed(half[:-1])]
    # Сглаживание замкнутым сплайном Катмулла — Рома: иначе край спинки виден гранями.
    n, steps, smooth = len(keys), 5, []
    for i in range(n):
        p0, p1, p2, p3 = (keys[(i + k) % n] for k in (-1, 0, 1, 2))
        for step in range(steps):
            t = step / steps
            smooth.append(tuple(.5 * (2 * p1[c] + (p2[c] - p0[c]) * t + (2 * p0[c] - 5 * p1[c] + 4 * p2[c] - p3[c]) * t * t
                                      + (3 * p1[c] - p0[c] - 3 * p2[c] + p3[c]) * t * t * t) for c in (0, 1)))
    return smooth


def back_point(x, z, forward):
    """Спинка наклонена назад примерно на 8°: сиденью нужна опора под углом."""
    return (x, -.17 - (z - .36) * .14 + forward, z)


def add_back(bm):
    outline = back_outline()
    front = [bm.verts.new(back_point(x, z, 0)) for x, z in outline]
    rear = [bm.verts.new(back_point(x, z, -.04)) for x, z in outline]
    n = len(outline)
    bm.faces.new(front).material_index = 1
    bm.faces.new(list(reversed(rear))).material_index = 1
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((front[i], front[j], rear[j], rear[i])).material_index = 1
    # Мягкая часть: кольца от края к середине, бархат вздувается, как подушка.
    pad = [(x, z) for x, z in outline if z > .45]
    cx, cz = 0, sum(z for _, z in pad) / len(pad)
    bulge = ((.83, .004, 2), (.8, .016, 2), (.6, .03, 2), (.3, .036, 2))
    trim = [bm.verts.new(back_point(x * .88, cz + (z - cz) * .88, .004)) for x, z in pad]
    rings = []
    for s, y, _ in bulge:
        rings.append([bm.verts.new(back_point(x * s, cz + (z - cz) * s, y)) for x, z in pad])
    m = len(pad)
    for i in range(m):
        j = (i + 1) % m
        bm.faces.new((trim[i], trim[j], rings[0][j], rings[0][i])).material_index = 0
    for r in range(len(rings) - 1):
        for i in range(m):
            j = (i + 1) % m
            bm.faces.new((rings[r][i], rings[r][j], rings[r + 1][j], rings[r + 1][i])).material_index = 2
    bm.faces.new(rings[-1]).material_index = 2
    # Стяжка на семь пуговиц, как на листе: ряды 2, 3 и 2.
    for x, z in ((-.06, .73), (.06, .73), (-.11, .655), (0, .655), (.11, .655), (-.06, .58), (.06, .58)):
        made = bmesh.ops.create_uvsphere(bm, u_segments=8, v_segments=6, radius=.01, matrix=Matrix.Translation(back_point(x, z, .03)))
        for f in {f for v in made["verts"] for f in v.link_faces}:
            f.material_index = 2
    # Герб на верхушке спинки: золотой круг, тёмная середина, треф.
    upright = Matrix.Rotation(math.radians(-90), 4, "X")
    for radius, y, depth, index in ((.042, -.016, .05, 0), (.033, .012, .006, 1)):
        made = bmesh.ops.create_cone(bm, cap_ends=True, segments=20, radius1=radius, radius2=radius, depth=depth,
                                     matrix=Matrix.Translation(back_point(0, .945, y)) @ upright)
        for f in {f for v in made["verts"] for f in v.link_faces}:
            f.material_index = index
    for x, z, radius in ((0, .958, .0095), (-.0115, .94, .0095), (.0115, .94, .0095), (0, .93, .0045)):
        made = bmesh.ops.create_cone(bm, cap_ends=True, segments=10, radius1=radius, radius2=radius, depth=.004,
                                     matrix=Matrix.Translation(back_point(x, z, .017)) @ upright)
        for f in {f for v in made["verts"] for f in v.link_faces}:
            f.material_index = 0


def build_chair(name="Chair"):
    # 0 — золото, 1 — дерево, 2 — бархат.
    bm = bmesh.new()
    add_box(bm, (0, .01, .44), (.47, .41, .075), 2, .03, 3)
    add_box(bm, (0, .01, .38), (.46, .39, .05), 1, .004)
    add_box(bm, (0, .01, .4), (.468, .398, .009), 0)
    for side in (-1, 1):
        add_box(bm, (side * .21, .19, .378), (.06, .06, .058), 0, .003, 1)
        add_tube(bm, ((side * .21, .19, .35), (side * .21, .19, .335), (side * .21, .19, .32), (side * .21, .19, .3), (side * .213, .195, .24), (side * .216, .2, .12), (side * .216, .2, .05)),
                 (.027, .027, .02, .025, .022, .018, .015), 1)
        add_tube(bm, ((side * .216, .2, .05), (side * .216, .2, .04), (side * .216, .2, .022), (side * .216, .2, .018), (side * .216, .2, 0)),
                 (.019, .019, .014, .018, .016), 0)
        add_tube(bm, ((side * .195, -.17, .37), (side * .2, -.2, .26), (side * .205, -.24, .14), (side * .208, -.28, .03)), (.024, .022, .018, .015), 1)
        add_tube(bm, ((side * .208, -.28, .03), (side * .209, -.292, 0)), (.016, .015), 0)
    add_back(bm)
    return finish(bm, name, [gold(), wood(), velvet()], split=50)
