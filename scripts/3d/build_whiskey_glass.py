"""Строит точную полую хрустальную 3D-модель клубного стакана для виски (Old Fashioned / Rocks glass)
по референс-фото автора (3д обьекты/стакан/Gemini_Generated_Image_n5mb0nn5mb0nn5mb.jpg):
  - GlassBody  : полый гранёный хрусталь (32 вертикальные грани на массивном дне + 8 острых хрустальных «корон»
                 в стиле Ар-деко, толстое дно 2,1 см, гладкая верхняя стенка и чистая внутренняя полость под будущие напитки)
  - GoldRim    : тонкий закруглённый золотой/латунный ободок по верхнему краю
  - ClubEmblem : изогнутый по стенке объёмный золотой медальон трефы (♣) с рельефной гравировкой с референса
  - LiquidAnchor: точка дна внутренней полости для будущей анимации наливания и питья

Запуск:
  blender --background --factory-startup --disable-autoexec --python-exit-code 1 --python scripts/3d/build_whiskey_glass.py -- build
  blender --background --factory-startup --disable-autoexec --python-exit-code 1 --python scripts/3d/build_whiskey_glass.py -- preview
"""
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
REF_IMG = os.path.join(ROOT, "3д обьекты", "стакан", "Gemini_Generated_Image_n5mb0nn5mb0nn5mb.jpg")
OUT_BLEND = os.path.join(ROOT, "art3d", "props", "whiskey_glass.blend")
OUT_DIR = os.path.join(ROOT, "unity", "CardGameClient", "Assets", "Resources", "ThreeD")
OUT_FBX = os.path.join(OUT_DIR, "whiskey_glass.fbx")
CLUB_ALBEDO = os.path.join(OUT_DIR, "GlassClub_BaseColor.png")
CLUB_NORMAL = os.path.join(OUT_DIR, "GlassClub_Normal.png")

# Реальные размеры клубного стакана для виски (метры): приземистый, массивный, как на референсе
H = 0.092          # Высота 9,2 см
R_BOT = 0.0385     # Нижний радиус 3,85 см (диаметр 7,7 см)
R_TOP = 0.0430     # Верхний радиус 4,30 см (диаметр 8,6 см)
Z_FLOOR = 0.0215   # Толстое хрустальное дно 2,15 см
WALL_TOP = 0.0034  # Толщина стенки у края 3,4 мм
R_IN_TOP = R_TOP - WALL_TOP
R_IN_BOT = 0.0342


def smoothstep(e0, e1, x):
    t = max(0.0, min(1.0, (x - e0) / max(e1 - e0, 1e-9)))
    return t * t * (3.0 - 2.0 * t)


def outer_base_r(z):
    t = max(0.0, min(1.0, z / H))
    # Лёгкое благородное расширение кверху как на фото
    return R_BOT + (R_TOP - R_BOT) * (t ** 0.92)


def facet_displacement(theta, z):
    """Точный рельеф хрустальной огранки по фото:
    - 32 чётких вертикальных вертикально-гранёных ребра на массивном дне (z = 0..24 мм)
    - 8 высоких треугольных/стрельчатых хрустальных пиков «короны» (z = 18..48 мм) с двойной V-огранкой!
    Возвращает (dr в метрах, facet_mask в [0..1] для блеска граней в шейдере)."""
    # 1. Нижний пояс: 32 вертикальные грани
    ph32 = (theta / (2.0 * math.pi)) * 32.0
    f32 = abs(2.0 * (ph32 - math.floor(ph32)) - 1.0)  # 0 в желобке, 1 на гребне
    # Острый хрустальный профиль грани (призматическая фаска)
    cut32 = -(1.0 - f32 ** 0.68) * 0.00175
    # Затухание у самого низа (фаска дна) и переход вверх на z=20..25 мм
    w_bot = smoothstep(0.0012, 0.0042, z) * (1.0 - smoothstep(0.0205, 0.0255, z))

    # 2. 8 высоких хрустальных пиков (корона Ар-деко, z = 18..48 мм)
    # Сдвиг +0.5 ставит долину ровно под трефой (-Y), а два высоких острых пика — симметрично слева и справа от неё, как на фото!
    ph8 = (theta / (2.0 * math.pi)) * 8.0 + 0.5
    u8 = abs(2.0 * (ph8 - math.floor(ph8)) - 1.0)  # 1 в вершине пика, 0 в долине между пиками
    peak_z = 0.019 + 0.030 * (u8 ** 1.05)          # треугольный пик до 49 мм!
    # Внутри каждого пика — чёткая V-образная огранка и внутренний двойной шеврон (как на фото!)
    inside_peak = (1.0 - smoothstep(peak_z - 0.0026, peak_z + 0.0014, z)) * smoothstep(0.014, 0.020, z)
    # Расстояние от наклонного ребра пика
    edge_dist = max(0.0, peak_z - z)
    # Главная ступенька/гребень по ребру пика + внутренний V-вырез
    ridge_outer = math.sin(min(1.0, edge_dist / 0.0068) * math.pi) * 0.00185
    inner_peak_z = 0.018 + 0.020 * (u8 ** 1.08)
    edge_inner = max(0.0, inner_peak_z - z)
    ridge_inner = math.sin(min(1.0, edge_inner / 0.0055) * math.pi) * 0.00125 if z < inner_peak_z else 0.0
    # Внутри пиков также продолжаются мягкие вертикальные ребра (16 шт, по 2 на пик)
    ph16 = (theta / (2.0 * math.pi)) * 16.0
    f16 = abs(2.0 * (ph16 - math.floor(ph16)) - 1.0)
    flute_mid = -(1.0 - f16 ** 0.75) * 0.00110

    dr_peak = inside_peak * (flute_mid + ridge_outer - ridge_inner)
    dr = w_bot * cut32 + dr_peak

    # Маска огранки (0 на гладком верхнем стекле, 0.5..1.0 на нижних хрустальных гранях)
    # для усиления хрустальных бликов и отражений в Unity-шейдере!
    ridge_accent = abs(dr) / 0.0017
    mask = max(w_bot * (0.68 + 0.32 * (1.0 - f32)), inside_peak * (0.58 + 0.42 * min(1.0, ridge_accent)))
    # Усиление у массивного хрустального дна (z < 20 мм)
    mask = max(mask, (1.0 - smoothstep(0.016, 0.024, z)) * 0.88)
    return dr, min(1.0, mask)


def extract_club_textures():
    """Вырезает с референса викторианскую гравировку трефы (♣) в чистые 256x256 Albedo + Normal карты."""
    os.makedirs(OUT_DIR, exist_ok=True)
    if not os.path.exists(REF_IMG):
        print("WARN: reference image missing, skipping club texture extract")
        return
    im = bpy.data.images.load(REF_IMG)
    W, H_im = im.size[0], im.size[1]
    px = np.empty(W * H_im * 4, dtype=np.float32)
    im.pixels.foreach_get(px)
    arr = px.reshape((H_im, W, 4))
    bpy.data.images.remove(im)

    # В Blender строка 0 — низ картинки! Ищем золотую трефу в центре стакана (x ~ 42..58%, y_from_bottom ~ 42..68%)
    x0, x1 = int(W * 0.43), int(W * 0.57)
    y0, y1 = int(H_im * 0.42), int(H_im * 0.68)
    sub = arr[y0:y1, x0:x1, :3]
    # Золотая трефа заметно желтее и насыщеннее серого/тёплого стекла (R - B высокое, и контраст)
    sat = (sub[:, :, 0] - sub[:, :, 2]) + 0.5 * (sub[:, :, 1] - sub[:, :, 2])
    # Фон стекла в этой зоне имеет sat ~ 0.06..0.14; медальон — > 0.22 и темнее по контурам
    lum = 0.299 * sub[:, :, 0] + 0.587 * sub[:, :, 1] + 0.114 * sub[:, :, 2]
    med_mask = (sat > 0.20) & (lum < 0.62)
    ys, xs = np.where(med_mask)
    if len(xs) > 50:
        cx = x0 + int((xs.min() + xs.max()) * 0.5)
        cy = y0 + int((ys.min() + ys.max()) * 0.5)
        half = int(max(xs.max() - xs.min(), ys.max() - ys.min()) * 0.56)
    else:
        cx, cy, half = W // 2, int(H_im * 0.54), int(H_im * 0.095)

    crop = arr[cy - half:cy + half, cx - half:cx + half, :3]
    # Билинейный ресайз в 256x256
    SZ = 256
    ch, cw, _ = crop.shape
    gy = np.linspace(0, ch - 1, SZ)
    gx = np.linspace(0, cw - 1, SZ)
    yy, xx = np.meshgrid(gy, gx, indexing="ij")
    y_i = np.clip(yy.astype(np.int32), 0, ch - 2)
    x_i = np.clip(xx.astype(np.int32), 0, cw - 2)
    wy = (yy - y_i)[..., None]
    wx = (xx - x_i)[..., None]
    res = (
        crop[y_i, x_i] * (1 - wy) * (1 - wx)
        + crop[y_i, x_i + 1] * (1 - wy) * wx
        + crop[y_i + 1, x_i] * wy * (1 - wx)
        + crop[y_i + 1, x_i + 1] * wy * wx
    )
    # Убираем серый фон за краями трефы, заливая тёплой античной бронзой (чтобы по краям UV-развёртки не лез серый!)
    sat_r = (res[:, :, 0] - res[:, :, 2]) + 0.4 * (res[:, :, 1] - res[:, :, 2])
    fg = np.clip((sat_r - 0.13) / 0.09, 0.0, 1.0)[..., None]
    bronze_bg = np.array([0.46, 0.34, 0.15], dtype=np.float32)
    # Чуть осветляем золото под PBR-металличность (металлик темнит диффуз)
    gold_fg = np.clip(res * np.array([1.28, 1.24, 1.08], dtype=np.float32), 0.0, 1.0)
    albedo = bronze_bg * (1.0 - fg) + gold_fg * fg

    rgba = np.ones((SZ, SZ, 4), dtype=np.float32)
    rgba[:, :, :3] = albedo
    out_a = bpy.data.images.new("GlassClub_BaseColor", width=SZ, height=SZ, alpha=False)
    out_a.pixels.foreach_set(rgba.ravel())
    out_a.filepath_raw = CLUB_ALBEDO
    out_a.file_format = "PNG"
    out_a.save()
    bpy.data.images.remove(out_a)

    # Строим чёткую карту нормалей филиграни по яркости рельефа (Sobel)
    lum_r = 0.299 * albedo[:, :, 0] + 0.587 * albedo[:, :, 1] + 0.114 * albedo[:, :, 2]
    # Умеренное усиление рельефа гравировки
    gx_n = np.zeros_like(lum_r)
    gy_n = np.zeros_like(lum_r)
    gx_n[:, 1:-1] = (lum_r[:, 2:] - lum_r[:, :-2]) * 3.8
    gy_n[1:-1, :] = (lum_r[2:, :] - lum_r[:-2, :]) * 3.8
    nx = -gx_n
    ny = -gy_n
    nz = np.ones_like(lum_r)
    ln = np.sqrt(nx * nx + ny * ny + nz * nz)
    n_rgba = np.ones((SZ, SZ, 4), dtype=np.float32)
    n_rgba[:, :, 0] = 0.5 + 0.5 * (nx / ln)
    n_rgba[:, :, 1] = 0.5 + 0.5 * (ny / ln)
    n_rgba[:, :, 2] = 0.5 + 0.5 * (nz / ln)
    out_n = bpy.data.images.new("GlassClub_Normal", width=SZ, height=SZ, alpha=False)
    out_n.pixels.foreach_set(n_rgba.ravel())
    out_n.filepath_raw = CLUB_NORMAL
    out_n.file_format = "PNG"
    out_n.save()
    bpy.data.images.remove(out_n)
    print(f"=== EXTRACTED CLUB TEXTURES: center=({cx},{cy}) half={half} -> {CLUB_ALBEDO}", flush=True)


def make_materials():
    # 1. Прозрачный гранёный хрусталь (для превью в Cycles и слот glass_crystal для Unity)
    m_glass = bpy.data.materials.new("glass_crystal")
    m_glass.use_nodes = True
    nt = m_glass.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (0.96, 0.92, 0.87, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.04
    bsdf.inputs["IOR"].default_value = 1.54
    if "Transmission Weight" in bsdf.inputs:
        bsdf.inputs["Transmission Weight"].default_value = 0.96

    # 2. Античное шлифованное золото для верхнего ободка (glass_gold)
    m_gold = bpy.data.materials.new("glass_gold")
    m_gold.use_nodes = True
    bsdf_g = m_gold.node_tree.nodes["Principled BSDF"]
    bsdf_g.inputs["Base Color"].default_value = (0.86, 0.69, 0.36, 1.0)
    bsdf_g.inputs["Metallic"].default_value = 0.94
    bsdf_g.inputs["Roughness"].default_value = 0.24

    # 3. Рельефная античная бронзово-золотая трефа (glass_club)
    m_club = bpy.data.materials.new("glass_club")
    m_club.use_nodes = True
    nt_c = m_club.node_tree
    bsdf_c = nt_c.nodes["Principled BSDF"]
    bsdf_c.inputs["Base Color"].default_value = (0.78, 0.60, 0.28, 1.0)
    bsdf_c.inputs["Metallic"].default_value = 0.88
    bsdf_c.inputs["Roughness"].default_value = 0.34
    if os.path.exists(CLUB_ALBEDO):
        tex_a = nt_c.nodes.new("ShaderNodeTexImage")
        tex_a.image = bpy.data.images.load(CLUB_ALBEDO)
        nt_c.links.new(tex_a.outputs["Color"], bsdf_c.inputs["Base Color"])
    if os.path.exists(CLUB_NORMAL):
        tex_n = nt_c.nodes.new("ShaderNodeTexImage")
        tex_n.image = bpy.data.images.load(CLUB_NORMAL)
        tex_n.image.colorspace_settings.name = "Non-Color"
        nmap = nt_c.nodes.new("ShaderNodeNormalMap")
        nmap.inputs["Strength"].default_value = 1.0
        nt_c.links.new(tex_n.outputs["Color"], nmap.inputs["Color"])
        nt_c.links.new(nmap.outputs["Normal"], bsdf_c.inputs["Normal"])

    return m_glass, m_gold, m_club


def build_glass_body(mat_glass):
    """Строит замкнутый полый гранёный хрустальный корпус стакана с толстым дном и ровной внутренней полостью."""
    N = 128  # кратно 32 граням дна и 8 пикам короны — все ребра ложатся точно на рёбра сетки!
    bm = bmesh.new()
    uv_layer = bm.loops.layers.uv.verify()
    col_layer = bm.loops.layers.color.verify()

    # Профиль (z, is_outer, r_override, extra_mask)
    # От центра наружного дна -> край дна -> наружная стенка вверх до 89.2 мм -> закруглённый край -> внутренняя стенка вниз -> внутреннее дно
    outer_zs = (
        [0.0000, 0.0008, 0.0022]
        + list(np.linspace(0.0045, 0.0215, 9))
        + list(np.linspace(0.0230, 0.0510, 20))
        + list(np.linspace(0.0550, 0.0892, 9))
    )
    # Внутренняя полость (чистая, гладкая — готова под наливку виски!)
    inner_zs = (
        [0.0894, 0.0875]
        + list(np.linspace(0.0820, 0.0260, 10))
        + [0.0235, 0.0220, 0.0214]
    )

    rings = []
    ring_meta = []  # (is_outer, z_norm, base_mask)

    # Наружное дно — два концентрических кольца для чистого плоского дна
    for r_frac, z_b in ((0.35, 0.0), (0.78, 0.0)):
        r = R_BOT * r_frac
        ring = [bm.verts.new((r * math.cos(i / N * math.tau), r * math.sin(i / N * math.tau), z_b)) for i in range(N)]
        rings.append(ring)
        ring_meta.append((True, 0.0, [0.90] * N))

    # Наружная стенка (с точной огранкой!)
    for z in outer_zs:
        r0 = outer_base_r(z)
        if z < 0.0022:
            # Нижняя фаска дна
            chamf = (0.0022 - z) * 0.75
            r0 -= chamf
        ring = []
        masks = []
        for i in range(N):
            th = (i / N) * math.tau
            dr, fm = facet_displacement(th, z)
            r = r0 + dr
            ring.append(bm.verts.new((r * math.cos(th), r * math.sin(th), z)))
            masks.append(fm)
        rings.append(ring)
        ring_meta.append((True, z / H, masks))

    # Верхний торец под золотым ободком (гладкий)
    z_lip = 0.0906
    r_mid = 0.5 * (R_TOP + R_IN_TOP)
    ring_lip = [bm.verts.new((r_mid * math.cos(i / N * math.tau), r_mid * math.sin(i / N * math.tau), z_lip)) for i in range(N)]
    rings.append(ring_lip)
    ring_meta.append((True, 1.0, [0.25] * N))

    # Внутренняя стенка сверху вниз до внутреннего дна
    for z in inner_zs:
        t_in = max(0.0, min(1.0, (z - Z_FLOOR) / (0.090 - Z_FLOOR)))
        r = R_IN_BOT + (R_IN_TOP - R_IN_BOT) * (t_in ** 0.92)
        if z < 0.024:
            # Мягкий внутренний радиус скругления у дна
            r -= (0.024 - z) * 0.85
        ring = [bm.verts.new((r * math.cos(i / N * math.tau), r * math.sin(i / N * math.tau), z)) for i in range(N)]
        rings.append(ring)
        # На внутренней стенке внизу чуть выше маска для внутреннего блеска толстого дна
        m_in = 0.65 * (1.0 - smoothstep(0.021, 0.036, z))
        ring_meta.append((False, z / H, [m_in] * N))

    # Внутреннее дно с лёгким классическим хрустальным куполом (punt +1.5 мм)
    for r_frac, z_f in ((0.62, Z_FLOOR + 0.0005), (0.28, Z_FLOOR + 0.0012)):
        r = R_IN_BOT * r_frac
        ring = [bm.verts.new((r * math.cos(i / N * math.tau), r * math.sin(i / N * math.tau), z_f)) for i in range(N)]
        rings.append(ring)
        ring_meta.append((False, Z_FLOOR / H, [0.75] * N))

    # Соединяем кольца полигонами с правильными нормалями
    for ri in range(len(rings) - 1):
        rA, rB = rings[ri], rings[ri + 1]
        is_outA, vA, mA = ring_meta[ri]
        is_outB, vB, mB = ring_meta[ri + 1]
        for i in range(N):
            j = (i + 1) % N
            f = bm.faces.new((rA[i], rA[j], rB[j], rB[i]))
            # Нижняя хрустальная огранка (наружная стенка, z < 48 мм) с плоскими гранями?
            # Лучше smooth + чёткие двойные рёбра / AutoSmooth, чтобы грани и верх не имели лесенок!
            f.smooth = True
            u0, u1 = i / N, (i + 1) / N
            for loop, u_val, v_val, fm, is_o in (
                (f.loops[0], u0, vA, mA[i], is_outA),
                (f.loops[1], u1, vA, mA[j], is_outA),
                (f.loops[2], u1, vB, mB[j], is_outB),
                (f.loops[3], u0, vB, mB[i], is_outB),
            ):
                loop[uv_layer].uv = (u_val, v_val)
                # R = маска хрустальных граней, G = 1 снаружи / 0 внутри, B = высота v
                loop[col_layer] = (fm, 1.0 if is_o else 0.0, v_val, 1.0)

    # Закрываем центры наружного и внутреннего дна
    c_bot = bm.verts.new((0.0, 0.0, 0.0))
    for i in range(N):
        j = (i + 1) % N
        f = bm.faces.new((c_bot, rings[0][j], rings[0][i]))
        f.smooth = True
        for loop in f.loops:
            loop[uv_layer].uv = (0.5, 0.0)
            loop[col_layer] = (0.90, 1.0, 0.0, 1.0)

    c_in = bm.verts.new((0.0, 0.0, Z_FLOOR + 0.0016))
    for i in range(N):
        j = (i + 1) % N
        f = bm.faces.new((c_in, rings[-1][i], rings[-1][j]))
        f.smooth = True
        for loop in f.loops:
            loop[uv_layer].uv = (0.5, Z_FLOOR / H)
            loop[col_layer] = (0.75, 0.0, Z_FLOOR / H, 1.0)

    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    mesh = bpy.data.meshes.new("GlassBody")
    bm.to_mesh(mesh)
    bm.free()
    mesh.materials.append(mat_glass)

    ob = bpy.data.objects.new("GlassBody", mesh)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def build_gold_rim(mat_gold):
    """Тонкий закруглённый ободок античного золота по верхнему краю стакана (как на фото)."""
    N = 96
    bm = bmesh.new()
    uv_layer = bm.loops.layers.uv.verify()

    # Сечение ободка: охватывает верхний край стекла снаружи, сверху и чуть внутри
    # (r, z) по замкнутому контуру (против часовой в (r, z))
    r_out = R_TOP + 0.00035
    r_in = R_IN_TOP - 0.00028
    r_mid = 0.5 * (r_out + r_in)
    profile = [
        (r_out - 0.0004, 0.0888),
        (r_out,          0.0894),
        (r_out + 0.0001, 0.0908),
        (r_out - 0.0004, 0.0920),
        (r_mid,          0.0923),
        (r_in + 0.0004,  0.0920),
        (r_in - 0.0001,  0.0908),
        (r_in,           0.0894),
        (r_in + 0.0003,  0.0888),
    ]
    P = len(profile)
    rings = []
    for r, z in profile:
        rings.append([bm.verts.new((r * math.cos(i / N * math.tau), r * math.sin(i / N * math.tau), z)) for i in range(N)])
    for pi in range(P):
        rA, rB = rings[pi], rings[(pi + 1) % P]
        for i in range(N):
            j = (i + 1) % N
            f = bm.faces.new((rA[i], rA[j], rB[j], rB[i]))
            f.smooth = True
            for lp, u, v in ((f.loops[0], i / N, pi / P), (f.loops[1], (i + 1) / N, pi / P),
                             (f.loops[2], (i + 1) / N, (pi + 1) / P), (f.loops[3], i / N, (pi + 1) / P)):
                lp[uv_layer].uv = (u, v)

    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    mesh = bpy.data.meshes.new("GoldRim")
    bm.to_mesh(mesh)
    bm.free()
    mesh.materials.append(mat_gold)
    ob = bpy.data.objects.new("GoldRim", mesh)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def build_club_emblem(mat_club):
    """Строит настоящий объёмный золотой медальон трефы (♣) с фаской, изогнутый точно по наружной стенке стакана (+Y)."""
    # Координаты контура трефы в 2D (x, y) в [-0.5..+0.5], затем масштабируем до ~19 мм и заворачиваем на цилиндр!
    # Создаём 3 чистых круга (верхний и 2 боковых лепестка) + плавную ножку трефы через плотный контур
    pts = []
    # Точный контур классической трефы ♣ (обход против часовой от правого нижнего угла ножки)
    # 1. Основание ножки (слева направо) -> правая вогнутая стенка ножки
    pts += [(-0.22, -0.46), (0.22, -0.46), (0.16, -0.40), (0.09, -0.28), (0.06, -0.14)]
    # 2. Правый лепесток (центр (0.23, -0.02), радиус 0.215)
    c_r = (0.23, -0.02); rad_s = 0.215
    for k in range(20):
        a = math.radians(-128 + k * (248 / 19))
        pts.append((c_r[0] + rad_s * math.cos(a), c_r[1] + rad_s * math.sin(a)))
    # 3. Верхний лепесток (центр (0.0, 0.27), радиус 0.22)
    c_t = (0.0, 0.27); rad_t = 0.22
    for k in range(22):
        a = math.radians(-52 + k * (284 / 21))
        pts.append((c_t[0] + rad_t * math.cos(a), c_t[1] + rad_t * math.sin(a)))
    # 4. Левый лепесток (центр (-0.23, -0.02), радиус 0.215)
    c_l = (-0.23, -0.02)
    for k in range(20):
        a = math.radians(60 + k * (248 / 19))
        pts.append((c_l[0] + rad_s * math.cos(a), c_l[1] + rad_s * math.sin(a)))
    # 5. Левая вогнутая стенка ножки
    pts += [(-0.06, -0.14), (-0.09, -0.28), (-0.16, -0.40)]

    # Удаляем слишком близкие дубликаты
    clean = [pts[0]]
    for p in pts[1:]:
        if math.hypot(p[0] - clean[-1][0], p[1] - clean[-1][1]) > 0.018:
            clean.append(p)

    # Размер медальона на стакане: 19,5 мм в высоту, центр на z = 54,5 мм (передняя сторона -Y -> в Unity +Z после 180°!)
    SIZE = 0.0195
    Z_CTR = 0.0555
    THICK = 0.0017

    bm = bmesh.new()
    uv_layer = bm.loops.layers.uv.verify()

    # Создаём 3 слоя контура: задний (в стекле -0.3мм), передняя фаска (+1.1мм), передняя грань (+1.7мм, чуть сужена внутрь)
    def wrap_to_glass(px, py, dr):
        z = Z_CTR + py * SIZE
        r = outer_base_r(z) + dr
        # Эмблема на стороне -Y (чтобы в Unity после разворота на 180° смотрела вперёд, а в камере прямо на нас!)
        th = -math.pi * 0.5 + (px * SIZE) / r
        return Vector((r * math.cos(th), r * math.sin(th), z))

    M = len(clean)
    # Центр медальона для веера передней грани (и чуть выпуклый в середине!)
    ring_back = [bm.verts.new(wrap_to_glass(x, y, -0.0003)) for x, y in clean]
    ring_mid = [bm.verts.new(wrap_to_glass(x, y, THICK * 0.65)) for x, y in clean]
    ring_front = [bm.verts.new(wrap_to_glass(x * 0.90, y * 0.90, THICK)) for x, y in clean]
    v_ctr = bm.verts.new(wrap_to_glass(0.0, 0.03, THICK + 0.00045))

    def set_uv(loop, x, y):
        loop[uv_layer].uv = (0.5 + x * 0.92, 0.5 + y * 0.92)

    # Боковая стенка и фаска
    for rA, rB in ((ring_back, ring_mid), (ring_mid, ring_front)):
        for i in range(M):
            j = (i + 1) % M
            f = bm.faces.new((rA[i], rA[j], rB[j], rB[i]))
            f.smooth = True
            for lp, (x, y) in zip(f.loops, (clean[i], clean[j], clean[j], clean[i])):
                set_uv(lp, x, y)

    # Лицевая поверхность трефы (веер от центра, ровная с мягким куполом + UV с референса!)
    for i in range(M):
        j = (i + 1) % M
        f = bm.faces.new((ring_front[i], ring_front[j], v_ctr))
        f.smooth = True
        set_uv(f.loops[0], clean[i][0] * 0.90, clean[i][1] * 0.90)
        set_uv(f.loops[1], clean[j][0] * 0.90, clean[j][1] * 0.90)
        set_uv(f.loops[2], 0.0, 0.03)

    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    mesh = bpy.data.meshes.new("ClubEmblem")
    bm.to_mesh(mesh)
    bm.free()
    mesh.materials.append(mat_club)
    ob = bpy.data.objects.new("ClubEmblem", mesh)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def build_all():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    extract_club_textures()
    m_glass, m_gold, m_club = make_materials()

    root = bpy.data.objects.new("WhiskeyGlass", None)
    bpy.context.scene.collection.objects.link(root)

    body = build_glass_body(m_glass)
    rim = build_gold_rim(m_gold)
    club = build_club_emblem(m_club)
    liquid = bpy.data.objects.new("LiquidAnchor", None)
    liquid.location = (0.0, 0.0, Z_FLOOR + 0.001)
    bpy.context.scene.collection.objects.link(liquid)

    for child in (body, rim, club, liquid):
        child.parent = root

    total_tris = sum(len(o.data.polygons) for o in (body, rim, club))
    print(f"=== BUILT WHISKEY GLASS: GlassBody={len(body.data.polygons)}p, GoldRim={len(rim.data.polygons)}p, Club={len(club.data.polygons)}p (total={total_tris}p)", flush=True)
    return root, (body, rim, club, liquid)


def cmd_build():
    root, children = build_all()
    os.makedirs(os.path.dirname(OUT_BLEND), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=OUT_BLEND)
    print(f"=== SAVED BLEND: {OUT_BLEND}", flush=True)

    bpy.ops.object.select_all(action="DESELECT")
    root.select_set(True)
    for c in children:
        c.select_set(True)
    bpy.context.view_layer.objects.active = root
    os.makedirs(OUT_DIR, exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=OUT_FBX, use_selection=True, object_types={"MESH", "EMPTY"},
        axis_forward="-Z", axis_up="Y",
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
        bake_space_transform=True, use_mesh_modifiers=True,
        mesh_smooth_type="FACE", use_tspace=True, bake_anim=False,
        path_mode="RELATIVE", embed_textures=False,
    )
    print(f"=== EXPORTED FBX: {OUT_FBX} ({round(os.path.getsize(OUT_FBX)/1024, 1)} KB)", flush=True)


def cmd_preview():
    root, children = build_all()
    scratch = r"C:\Users\Пользователь\.gemini\antigravity\brain\1bf10b84-65f0-44e9-9cd5-d85e00545270\scratch"
    os.makedirs(scratch, exist_ok=True)

    # Тёмно-бордовый стол для проверки прозрачности + студийный серый для сравнения 1-в-1 с референсом!
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 48
    scene.cycles.max_bounces = 8
    scene.cycles.transmission_bounces = 8
    scene.cycles.use_denoising = True
    scene.render.threads_mode = "FIXED"
    scene.render.threads = 4
    scene.render.resolution_x = 640
    scene.render.resolution_y = 480
    scene.view_settings.view_transform = "Standard"

    scene.world = bpy.data.worlds.new("W")
    scene.world.use_nodes = True
    scene.world.node_tree.nodes["Background"].inputs[0].default_value = (0.18, 0.17, 0.17, 1.0)
    scene.world.node_tree.nodes["Background"].inputs[1].default_value = 0.7

    for name, loc, rot, energy, sz in [
        ("Key",  (0.18, -0.25, 0.35), (0.7, 0.1, 0.4),  8.0, 0.25),
        ("Fill", (-0.24, -0.18, 0.22), (1.0, 0.0, -0.6), 5.0, 0.30),
        ("Rim",  (0.0,   0.30, 0.25), (-1.0, 0.0, 3.14), 6.0, 0.35),
    ]:
        ld = bpy.data.lights.new(name, "AREA"); ld.energy = energy; ld.size = sz
        lo = bpy.data.objects.new(name, ld); lo.location = loc; lo.rotation_euler = rot
        scene.collection.objects.link(lo)

    # Стол под стаканом (бордовое сукно + золотая полоска сзади, чтобы было видно прозрачность!)
    bpy.ops.mesh.primitive_plane_add(size=0.8, location=(0, 0, -0.0002))
    plane = bpy.context.active_object
    m_felt = bpy.data.materials.new("Felt"); m_felt.use_nodes = True
    m_felt.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.36, 0.04, 0.06, 1.0)
    m_felt.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.88
    plane.data.materials.append(m_felt)

    cam_d = bpy.data.cameras.new("Cam"); cam_d.lens = 68.0
    cam = bpy.data.objects.new("Cam", cam_d); scene.collection.objects.link(cam)
    scene.camera = cam

    def aim(loc, tgt):
        cam.location = loc
        cam.rotation_euler = (Vector(tgt) - Vector(loc)).normalized().to_track_quat("-Z", "Y").to_euler()

    # Shot 1: Точно ракурс референса (3/4 сверху 25°, видно пустую полость, золотой ободок, трефу и грани!)
    plane.hide_render = True
    aim((0.0, -0.25, 0.145), (0.0, 0.0, 0.044))
    p1 = os.path.join(scratch, "_glass_studio.png")
    scene.render.filepath = p1
    bpy.ops.render.render(write_still=True)

    # Shot 2: На бордовом сукне стола!
    plane.hide_render = False
    scene.world.node_tree.nodes["Background"].inputs[0].default_value = (0.10, 0.03, 0.04, 1.0)
    aim((0.07, -0.24, 0.16), (0.0, 0.0, 0.044))
    p2 = os.path.join(scratch, "_glass_felt.png")
    scene.render.filepath = p2
    bpy.ops.render.render(write_still=True)

    # Склейка 2x1
    W, H_r = 640, 480
    canvas = np.zeros((H_r, W * 2, 4), dtype=np.float32)
    for i, p in enumerate((p1, p2)):
        im = bpy.data.images.load(p)
        arr = np.array(im.pixels[:], dtype=np.float32).reshape((H_r, W, 4))
        bpy.data.images.remove(im)
        canvas[:, i * W:(i + 1) * W, :] = arr
    out = bpy.data.images.new("whiskey_glass_preview.png", width=W * 2, height=H_r, alpha=False)
    out.pixels.foreach_set(canvas.ravel())
    out.filepath_raw = os.path.join(scratch, "whiskey_glass_preview.png")
    out.file_format = "PNG"
    out.save()
    print("=== SAVED GLASS PREVIEW:", out.filepath_raw, flush=True)


if __name__ == "__main__":
    cmd = (sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else ["preview"])[0]
    {"preview": cmd_preview, "build": cmd_build}[cmd]()
