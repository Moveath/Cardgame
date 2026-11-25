"""Создаёт скульптурные варианты тел и текстуры кожи кистей прямо на базе ригнутого BaseMale_Headless.fbx
(сохраняя 100% скелета Mixamo 65-bone, все сидячие анимации, хват карт, жесты и бесшовный ворот-стойку!):
  1. BaseFemale_Headless.fbx + BaseFemale_FpsBody.fbx — женский силуэт (узкие плечи, изящная талия,
     естественная линия груди, мягкие бёдра, утончённый ворот-стойка и изящные кисти/предплечья).
  2. BaseMaleHeavy_Headless.fbx + BaseMaleHeavy_FpsBody.fbx — плотное телосложение (округлый живот,
     широкая грудь, полные плечи/рукава/бёдра и расширенный ворот-стойка под avatar_05).
  3. Варианты текстур кожи кистей для соперников и первого лица:
     - Dark (богатый тёплый смуглый тон точно под avatar_08_женщина_смуглая)
     - Aged (более светлая зрелая кожа с лёгкой возрастной фактурой под avatar_03 дедушку и avatar_06 бабушку)
     - Fair (нежный светлый тон под светлокожих женских персонажей avatar_02/04/10/12).
"""
import math
import os
import shutil

import bmesh
import bpy
import numpy as np
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
UNITY_CHARS_DIRS = [
    os.path.join(ROOT, "unity", "CardGameClient", "Assets", "Resources", "ThreeD", "Characters"),
    os.path.join(ROOT, "screens", "qa", "shop-project", "Assets", "Resources", "ThreeD", "Characters"),
]
UNITY_HANDS_DIRS = [
    os.path.join(ROOT, "unity", "CardGameClient", "Assets", "Resources", "ThreeD", "Hands"),
    os.path.join(ROOT, "screens", "qa", "shop-project", "Assets", "Resources", "ThreeD", "Hands"),
]
HEADLESS_SRC = os.path.join(UNITY_CHARS_DIRS[0], "BaseMale_Headless.fbx")
FPS_SRC = os.path.join(UNITY_CHARS_DIRS[0], "BaseMale_FpsBody.fbx")


def smoothstep(edge0, edge1, x):
    t = max(0.0, min(1.0, (x - edge0) / (edge1 - edge0)))
    return t * t * (3.0 - 2.0 * t)


def gauss(x, mu, sigma):
    d = (x - mu) / sigma
    return math.exp(-0.5 * d * d)


def clear_scene():
    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    for m in list(bpy.data.meshes):
        bpy.data.meshes.remove(m)
    for a in list(bpy.data.armatures):
        bpy.data.armatures.remove(a)
    for mat in list(bpy.data.materials):
        bpy.data.materials.remove(mat)


def export_rigged(arm, body, filename):
    arm.name = "Armature"
    if arm.data is not None:
        arm.data.name = "Armature"
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
        print(f"=== Synced {filename} ({os.path.getsize(tmp_fbx)//1024} KB) -> {d}")


def build_bone_segments(arm):
    """Возвращает отрезки костей рук и ног в мировых координатах для аккуратного радиального утончения/утолщения."""
    segs = {}
    for b in arm.data.bones:
        p0 = arm.matrix_world @ b.head_local
        p1 = arm.matrix_world @ b.tail_local
        segs[b.name] = (p0, p1)
    return segs


def radial_scale_near_bone(wp, p0, p1, scale_factor):
    ab = p1 - p0
    l2 = ab.length_squared
    if l2 < 1e-8:
        return wp
    t = max(0.0, min(1.0, (wp - p0).dot(ab) / l2))
    proj = p0 + ab * t
    return proj + (wp - proj) * scale_factor


def sculpt_female(arm, body):
    mw = body.matrix_world
    mw_inv = mw.inverted()
    vgs = {g.index: g.name for g in body.vertex_groups}
    segs = build_bone_segments(arm)
    hand_bone_prefixes = ("mixamorig:LeftHand", "mixamorig:RightHand", "mixamorig:LeftForeArm", "mixamorig:RightForeArm")

    for v in body.data.vertices:
        wp = mw @ v.co
        x, y, z = wp.x, wp.y, wp.z

        # Определяем доминирующую кость вершины (для изящного утончения предплечий/кистей без искажения длины пальцев)
        top_bone = None
        top_w = 0.0
        for g in v.groups:
            if g.weight > top_w:
                top_w = g.weight
                top_bone = vgs.get(g.group)

        # 1. Торс и плечи (|x| < 0.255 и выше бёдер)
        torso_mask = 1.0 - smoothstep(0.195, 0.285, abs(x))

        # Сужение плеч и грудной клетки
        shoulder_band = smoothstep(1.16, 1.36, z) * (1.0 - smoothstep(1.48, 1.56, z))
        x_scale = 1.0 - 0.125 * shoulder_band * torso_mask

        # Изящная талия (z = 0.98..1.20)
        waist_band = gauss(z, 1.11, 0.085)
        x_scale -= 0.145 * waist_band * torso_mask
        y_waist = 1.0 - 0.10 * waist_band * torso_mask

        # Мягкое округление женских бёдер (z = 0.78..0.98)
        hip_band = gauss(z, 0.89, 0.085)
        x_scale += 0.055 * hip_band * torso_mask

        # Утончение ворота-стойки под женскую шею (z > 1.495)
        neck_band = smoothstep(1.485, 1.520, z) * (1.0 - smoothstep(0.095, 0.135, abs(x)))
        if neck_band > 0.0:
            dx = x
            dy = y - 0.005
            x = dx * (1.0 - 0.085 * neck_band)
            y = 0.005 + dy * (1.0 - 0.085 * neck_band)
        else:
            x = x * x_scale
            y = 0.02 + (y - 0.02) * y_waist

        # 2. Естественная женская линия груди (мягкие парные доли впереди -Y на уровне z=1.285)
        if y < 0.02 and 1.16 < z < 1.43 and abs(x) < 0.19:
            front_w = smoothstep(0.02, -0.055, y)
            lobe_l = gauss(x, -0.072, 0.048)
            lobe_r = gauss(x, +0.072, 0.048)
            bust = (lobe_l + lobe_r) * gauss(z, 1.285, 0.052) * front_w
            y -= 0.032 * bust
            z -= 0.006 * bust

        # 3. Плавное утончение рук и бёдер СТРОГО по мягким весам костей (0 ступенек на плечевом шве!)
        wp_new = Vector((x, y, z))
        arm_w = 0.0
        arm_bone = None
        hand_w = 0.0
        hand_bone = None
        leg_w = 0.0
        leg_bone = None
        for g in v.groups:
            bn = vgs.get(g.group, "")
            if bn in ("mixamorig:LeftArm", "mixamorig:RightArm"):
                arm_w += g.weight
                arm_bone = bn
            elif bn.startswith(hand_bone_prefixes):
                hand_w += g.weight
                if hand_bone is None or g.weight > 0.35:
                    hand_bone = bn
            elif bn in ("mixamorig:LeftUpLeg", "mixamorig:RightUpLeg"):
                leg_w += g.weight
                leg_bone = bn

        if arm_w > 0.01 and arm_bone in segs:
            wp_new = radial_scale_near_bone(wp_new, *segs[arm_bone], 1.0 - 0.10 * min(1.0, arm_w))
        if hand_w > 0.01 and hand_bone in segs:
            wp_new = radial_scale_near_bone(wp_new, *segs[hand_bone], 1.0 - 0.09 * min(1.0, hand_w))
        if leg_w > 0.01 and leg_bone in segs:
            wp_new = radial_scale_near_bone(wp_new, *segs[leg_bone], 1.0 - 0.05 * min(1.0, leg_w))

        v.co = mw_inv @ wp_new

    body.data.update()


def sculpt_heavy(arm, body):
    mw = body.matrix_world
    mw_inv = mw.inverted()
    vgs = {g.index: g.name for g in body.vertex_groups}
    segs = build_bone_segments(arm)

    for v in body.data.vertices:
        wp = mw @ v.co
        x, y, z = wp.x, wp.y, wp.z

        top_bone = None
        top_w = 0.0
        for g in v.groups:
            if g.weight > top_w:
                top_w = g.weight
                top_bone = vgs.get(g.group)

        torso_mask = 1.0 - smoothstep(0.21, 0.30, abs(x))

        # 1. Выраженный округлый живот спереди (-Y) и широкая талия/бока (z = 0.88..1.38)
        belly_z = gauss(z, 1.12, 0.135)
        belly_x = gauss(x, 0.0, 0.145)
        front_w = smoothstep(0.06, -0.04, y)
        y -= 0.066 * belly_z * belly_x * front_w * torso_mask

        # Общее расширение торса и грудной клетки
        mid_band = gauss(z, 1.15, 0.18)
        chest_band = gauss(z, 1.35, 0.11)
        x_scale = 1.0 + (0.165 * mid_band + 0.085 * chest_band) * torso_mask
        x *= x_scale
        # Чуть полнее грудь и спина
        if torso_mask > 0.05 and 1.18 < z < 1.47:
            y -= 0.024 * chest_band * front_w * torso_mask
            back_w = smoothstep(0.02, 0.10, y)
            y += 0.018 * chest_band * back_w * torso_mask

        # 2. Более широкий ворот-стойка под полную шею и второй подбородок avatar_05 (z > 1.49)
        neck_band = smoothstep(1.485, 1.520, z) * (1.0 - smoothstep(0.095, 0.14, abs(x)))
        if neck_band > 0.0:
            x = x * (1.0 + 0.115 * neck_band)
            y = 0.005 + (y - 0.005) * (1.0 + 0.11 * neck_band)

        # 3. Плавное утолщение рук и бёдер по мягким весам костей (0 ступенек на плечах!)
        wp_new = Vector((x, y, z))
        arm_w = 0.0
        arm_bone = None
        fore_w = 0.0
        fore_bone = None
        leg_w = 0.0
        leg_bone = None
        for g in v.groups:
            bn = vgs.get(g.group, "")
            if bn in ("mixamorig:LeftArm", "mixamorig:RightArm"):
                arm_w += g.weight
                arm_bone = bn
            elif bn in ("mixamorig:LeftForeArm", "mixamorig:RightForeArm"):
                fore_w += g.weight
                fore_bone = bn
            elif bn in ("mixamorig:LeftUpLeg", "mixamorig:RightUpLeg"):
                leg_w += g.weight
                leg_bone = bn

        if arm_w > 0.01 and arm_bone in segs:
            wp_new = radial_scale_near_bone(wp_new, *segs[arm_bone], 1.0 + 0.11 * min(1.0, arm_w))
        if fore_w > 0.01 and fore_bone in segs:
            wp_new = radial_scale_near_bone(wp_new, *segs[fore_bone], 1.0 + 0.07 * min(1.0, fore_w))
        if leg_w > 0.01 and leg_bone in segs:
            wp_new = radial_scale_near_bone(wp_new, *segs[leg_bone], 1.0 + 0.12 * min(1.0, leg_w))

        v.co = mw_inv @ wp_new

    body.data.update()


def build_variant(src_fbx, out_name, sculpt_fn):
    clear_scene()
    bpy.ops.import_scene.fbx(filepath=src_fbx)
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    body = next(o for o in bpy.data.objects if o.type == "MESH")
    sculpt_fn(arm, body)
    export_rigged(arm, body, out_name)


def save_png(arr, path):
    h, w, _ = arr.shape
    img = bpy.data.images.new(os.path.basename(path), width=w, height=h, alpha=True)
    img.colorspace_settings.name = "sRGB"
    img.pixels.foreach_set(np.clip(arr, 0.0, 1.0).astype(np.float32).ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


def generate_hand_skin_textures():
    """Создаёт реалистичные варианты кожи кистей (Dark для смуглой, Aged для пожилых, Fair для светлокожих)
    для рук соперников И рук игрока от первого лица, полностью сохраняя рельеф, ногти и складки кожи!"""
    sources = [
        ("TripoHands_BaseColor.jpg", "TripoHands"),
    ]
    for src_name, prefix in sources:
        src_path = os.path.join(UNITY_HANDS_DIRS[0], src_name)
        if not os.path.exists(src_path):
            print(f"WARN: Missing {src_path}")
            continue
        img = bpy.data.images.load(src_path)
        w, h = img.size[0], img.size[1]
        buf = np.empty(w * h * 4, dtype=np.float32)
        img.pixels.foreach_get(buf)
        base = buf.reshape((h, w, 4)).copy()
        bpy.data.images.remove(img)

        rgb = base[:, :, :3]
        # 1. Dark (натуральный тёплый оливково-бронзовый тон точно под лицо avatar_08_женщина_смуглая — без красно-рыжей перенасыщенности!)
        dark = np.empty_like(base)
        dark_raw = np.power(np.clip(rgb, 1e-4, 1.0), np.array([1.58, 1.66, 1.82], dtype=np.float32)) * np.array([0.74, 0.67, 0.60], dtype=np.float32)
        dark_lum = dark_raw.mean(axis=-1, keepdims=True)
        dark_rgb = dark_raw * 0.88 + dark_lum * 0.12
        dark[:, :, :3] = np.clip(dark_rgb, 0.0, 1.0)
        dark[:, :, 3] = 1.0

        # 2. Aged (зрелая кожа для дедушки и бабушки: чуть бледнее/прохладнее, мягче насыщенность + тонкие возрастные пятнышки/фактура)
        yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
        # Мелкий процедурный органический шум кожи (бесшовные синусоидальные октавы — мягкие веснушки/неровность тона)
        grain = (
            np.sin(xx * 0.045 + yy * 0.031) * np.cos(xx * 0.027 - yy * 0.053) * 0.5
            + np.sin(xx * 0.11 - yy * 0.087) * np.cos(xx * 0.073 + yy * 0.13) * 0.3
            + np.sin(xx * 0.23 + yy * 0.19) * 0.2
        )
        # Нежные возрастные пятнышки на тыльной стороне + деликатное снижение насыщенности кожи
        spots = np.clip((grain - 0.52) * 2.2, 0.0, 1.0)
        lum = rgb.mean(axis=-1, keepdims=True)
        desat = rgb * 0.80 + lum * 0.20
        aged_rgb = desat * np.array([0.97, 0.96, 0.97], dtype=np.float32)
        aged_rgb = aged_rgb * (1.0 - 0.11 * spots[:, :, None]) + np.array([0.42, 0.30, 0.23], dtype=np.float32) * (0.07 * spots[:, :, None])
        aged = np.empty_like(base)
        aged[:, :, :3] = np.clip(aged_rgb, 0.0, 1.0)
        aged[:, :, 3] = 1.0

        # 3. Fair (чистая светлая женская кожа для avatar_02/04/10/12)
        fair = np.empty_like(base)
        fair_rgb = np.power(np.clip(rgb, 1e-4, 1.0), np.array([0.84, 0.87, 0.89], dtype=np.float32)) * np.array([1.03, 1.01, 1.00], dtype=np.float32)
        fair[:, :, :3] = np.clip(fair_rgb, 0.0, 1.0)
        fair[:, :, 3] = 1.0

        for tag, arr in (("Dark", dark), ("Aged", aged), ("Fair", fair)):
            out_fn = f"{prefix}_{tag}_BaseColor.png"
            for d in UNITY_HANDS_DIRS:
                os.makedirs(d, exist_ok=True)
                save_png(arr, os.path.join(d, out_fn))
            print(f"=== Synced hand skin texture {out_fn} ({w}x{h}) to both projects")


def main():
    build_variant(HEADLESS_SRC, "BaseFemale_Headless.fbx", sculpt_female)
    build_variant(FPS_SRC, "BaseFemale_FpsBody.fbx", sculpt_female)
    build_variant(HEADLESS_SRC, "BaseMaleHeavy_Headless.fbx", sculpt_heavy)
    build_variant(FPS_SRC, "BaseMaleHeavy_FpsBody.fbx", sculpt_heavy)
    generate_hand_skin_textures()
    print("=== ALL BODY VARIANTS AND HAND SKINS BUILT ===")


if __name__ == "__main__":
    main()
