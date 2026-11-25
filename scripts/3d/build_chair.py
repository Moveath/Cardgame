"""Конвертирует Tripo3D-модель клубного стула (3д обьекты/стул/652070cc-412b-41b9-bfa9-262c9292e87d.glb)
в чистый игровой ассет для Unity URP:
  - разворот передом в +Y (как прежний процедурный chair.fbx из table_model.py);
  - точная калибровка масштаба по высоте сиденья 0,48 м и опоре ножек строго на Z = 0;
  - упаковка PBR-текстур в 2K (2048x2048) под Unity URP Lit:
      * Chair_BaseColor.png      (sRGB Albedo)
      * Chair_Normal.png         (OpenGL tangent-space NormalMap)
      * Chair_MetallicGloss.png  (Linear RGBA: R = Metallic, A = Smoothness = 1 - Roughness)
  - экспорт unity/CardGameClient/Assets/Resources/ThreeD/chair.fbx + сохранение art3d/props/chair.blend.

Запуск:
  blender --background --factory-startup --python-exit-code 1 --python scripts/3d/build_chair.py
"""
import math
import os

import bpy
import numpy as np
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SRC_GLB = os.path.join(ROOT, "3д обьекты", "стул", "652070cc-412b-41b9-bfa9-262c9292e87d.glb")
OUT_BLEND = os.path.join(ROOT, "art3d", "props", "chair.blend")
OUT_DIR = os.path.join(ROOT, "unity", "CardGameClient", "Assets", "Resources", "ThreeD")
OUT_FBX = os.path.join(OUT_DIR, "chair.fbx")
OUT_BASECOLOR = os.path.join(OUT_DIR, "Chair_BaseColor.png")
OUT_NORMAL = os.path.join(OUT_DIR, "Chair_Normal.png")
OUT_METALLIC_GLOSS = os.path.join(OUT_DIR, "Chair_MetallicGloss.png")

TARGET_HEIGHT = 1.08   # Высота спинки с золотым медальоном трефы (красиво видна за плечами соперников, как на клубном арте).
TEX_SIZE = 2048        # Оптимальный баланс 2K чёткости и малого сжатого веса в Unity (~1-1.5 МБ BC7/ASTC).


def img_to_array(img):
    w, h = img.size[0], img.size[1]
    buf = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(buf)
    return buf.reshape((h, w, 4))


def downsample_box(arr, target_size):
    """Чистый area/box downscale (например 4096 -> 2048) без муара."""
    h, w, c = arr.shape
    if h == target_size and w == target_size:
        return arr.copy()
    if h % target_size == 0 and w % target_size == 0:
        fy, fx = h // target_size, w // target_size
        return arr.reshape(target_size, fy, target_size, fx, c).mean(axis=(1, 3))
    gy = np.linspace(0, h - 1, target_size)
    gx = np.linspace(0, w - 1, target_size)
    yy, xx = np.meshgrid(gy, gx, indexing="ij")
    y0 = np.clip(yy.astype(np.int32), 0, h - 2)
    x0 = np.clip(xx.astype(np.int32), 0, w - 2)
    wy = (yy - y0)[..., None]
    wx = (xx - x0)[..., None]
    return (
        arr[y0, x0] * (1 - wy) * (1 - wx)
        + arr[y0, x0 + 1] * (1 - wy) * wx
        + arr[y0 + 1, x0] * wy * (1 - wx)
        + arr[y0 + 1, x0 + 1] * wy * wx
    )


def save_png(arr, path, is_data=False):
    h, w, _ = arr.shape
    img = bpy.data.images.new(os.path.basename(path), width=w, height=h, alpha=True, float_buffer=False)
    if is_data:
        img.colorspace_settings.name = "Non-Color"
    else:
        img.colorspace_settings.name = "sRGB"
    img.pixels.foreach_set(np.ascontiguousarray(np.clip(arr, 0.0, 1.0), dtype=np.float32).ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)
    print(f"=== SAVED TEXTURE: {os.path.basename(path)} ({w}x{h}, {os.path.getsize(path) // 1024} KB)")


def find_image(substr):
    for img in bpy.data.images:
        if img.size[0] > 0 and substr in img.name.lower():
            return img
    return None


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    os.makedirs(os.path.dirname(OUT_BLEND), exist_ok=True)

    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob)
    for m in list(bpy.data.meshes):
        bpy.data.meshes.remove(m)
    for mat in list(bpy.data.materials):
        bpy.data.materials.remove(mat)

    bpy.ops.import_scene.gltf(filepath=SRC_GLB)
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    assert len(meshes) >= 1, "No mesh found in chair GLB"

    # Объединяем в один чистый объект "Chair", если их несколько, и применяем все трансформации.
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    chair = bpy.context.view_layer.objects.active
    chair.name = "Chair"
    chair.parent = None
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    # Удаляем лишние пустышки от glTF-импорта.
    for ob in list(bpy.data.objects):
        if ob != chair:
            bpy.data.objects.remove(ob, do_unlink=True)

    # ВАЖНО: glTF-импорт в Blender ставит rotation_mode = 'QUATERNION', поэтому ставим 'XYZ' явно!
    # Tripo-стул изначально смотрит передом в -Y (спинка в +Y). Поворачиваем на 180° вокруг Z,
    # чтобы перед смотрел в +Y, а спинка была в -Y — строго как у table_model.build_chair()!
    chair.rotation_mode = "XYZ"
    chair.rotation_euler = (0.0, 0.0, math.pi)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)

    verts = np.array([v.co[:] for v in chair.data.vertices], dtype=np.float64)
    min_x, max_x = verts[:, 0].min(), verts[:, 0].max()
    min_y, max_y = verts[:, 1].min(), verts[:, 1].max()
    min_z, max_z = verts[:, 2].min(), verts[:, 2].max()
    back_y_raw = float(verts[verts[:, 2] > max_z * 0.72, 1].mean())
    print(f"=== RAW BOUNDS (front +Y): X=[{min_x:.3f},{max_x:.3f}], Y=[{min_y:.3f},{max_y:.3f}], Z=[{min_z:.3f},{max_z:.3f}], backY={back_y_raw:.3f}")
    assert back_y_raw < -0.05, f"Backrest must be in -Y, got {back_y_raw:.3f}"

    # Калибруем масштаб строго по полной высоте клубного стула (1,08 м — спинка с трефой видна за плечами!).
    raw_h = max_z - min_z
    scale = TARGET_HEIGHT / raw_h
    print(f"=== CHAIR HEIGHT: raw={raw_h:.3f}m -> scale={scale:.4f} -> target={raw_h * scale:.3f}m")

    # Центрируем X, сдвигаем чуть вперёд по +Y, чтобы спинка (-Y) сидела ровно за спиной соперника, ножки строго на Z = 0.
    cx = 0.5 * (min_x + max_x)
    cy = 0.5 * (min_y + max_y) + 0.03
    chair.location = (-cx * scale, -cy * scale, -min_z * scale)
    chair.scale = (scale, scale, scale)
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=True)

    verts2 = np.array([v.co[:] for v in chair.data.vertices], dtype=np.float64)
    back_y_final = float(verts2[verts2[:, 2] > 0.75, 1].mean())
    print(f"=== FINAL BOUNDS: X=[{verts2[:,0].min():.3f},{verts2[:,0].max():.3f}], Y=[{verts2[:,1].min():.3f},{verts2[:,1].max():.3f}], Z=[{verts2[:,2].min():.3f},{verts2[:,2].max():.3f}], backY={back_y_final:.3f}")

    # Извлекаем и перепаковываем PBR-текстуры в 2K под Unity URP Lit.
    img_base = find_image("basecolor")
    img_norm = find_image("normal")
    img_rm = find_image("_rm")
    assert img_base and img_norm and img_rm, f"Missing textures: base={img_base}, norm={img_norm}, rm={img_rm}"

    # BaseColor: sRGB (в Blender img.pixels хранит значения в sRGB-буфере для 8-bit PNG/JPG — сохраняем в sRGB PNG).
    base_arr = downsample_box(img_to_array(img_base), TEX_SIZE)
    base_arr[..., 3] = 1.0
    save_png(base_arr, OUT_BASECOLOR, is_data=False)

    # Normal: tangent-space OpenGL (+Y up, совпадает с Unity URP!), ренормализация после ресайза.
    norm_arr = downsample_box(img_to_array(img_norm), TEX_SIZE)
    n_xyz = norm_arr[..., :3] * 2.0 - 1.0
    n_len = np.maximum(np.linalg.norm(n_xyz, axis=-1, keepdims=True), 1e-6)
    norm_arr[..., :3] = (n_xyz / n_len) * 0.5 + 0.5
    norm_arr[..., 3] = 1.0
    save_png(norm_arr, OUT_NORMAL, is_data=True)

    # MetallicRoughness (glTF: G = Roughness, B = Metallic) -> Unity URP MetallicGlossMap (R = Metallic, A = Smoothness = 1 - Roughness).
    rm_arr = downsample_box(img_to_array(img_rm), TEX_SIZE)
    rough = np.clip(rm_arr[..., 1], 0.0, 1.0)
    metal = np.clip(rm_arr[..., 2], 0.0, 1.0)
    # В тёмном клубном зале без яркого неба-отражения металличность 0.95 темнит золото — мягко ограничиваем верх на 0.58,
    # чтобы золотые накладки, гвоздики и трефа сияли тёплым благородным блеском со всех мест за столом!
    metal_unity = np.clip(metal * 0.62, 0.0, 0.58)
    smooth_unity = np.clip(1.0 - rough, 0.04, 0.86)
    mg = np.zeros((TEX_SIZE, TEX_SIZE, 4), dtype=np.float32)
    mg[..., 0] = metal_unity
    mg[..., 1] = metal_unity
    mg[..., 2] = metal_unity
    mg[..., 3] = smooth_unity
    save_png(mg, OUT_METALLIC_GLOSS, is_data=True)

    # Назначаем единый чистый слот материала "chair_pbr", который Table3DArt.Slot() подхватит как Template("Chair").
    mat = bpy.data.materials.new("chair_pbr")
    mat.use_nodes = True
    chair.data.materials.clear()
    chair.data.materials.append(mat)

    # Сохраняем .blend и экспортируем chair.fbx строго по конвенции table_model.export_fbx!
    bpy.ops.wm.save_as_mainfile(filepath=OUT_BLEND)
    bpy.ops.object.select_all(action="DESELECT")
    chair.select_set(True)
    bpy.context.view_layer.objects.active = chair
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
    )
    print(f"=== EXPORTED {OUT_FBX} ({os.path.getsize(OUT_FBX) // 1024} KB, {len(chair.data.polygons)} tris)")


if __name__ == "__main__":
    main()
