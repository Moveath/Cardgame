"""Создаёт безголовое и безрукое (до плечевых суставов) тело игрока BaseMale_FpsBody.fbx из BaseMale_Headless.fbx:
  - Полностью сохраняет вязаный торс с округлыми плечами, джинсы, ноги, ботинки и все 65 костей Mixamo Humanoid.
  - Аккуратно отрезает руки чуть ниже плечевого сустава (|x| > 0.255) и ровно заворачивает край внутрь,
    чтобы собственные руки первого лица (OwnArms3DView) входили прямо в реальные плечи свитера без «двойных рук»
    и без оторванных цилиндров в воздухе!
"""
import os
import shutil

import bmesh
import bpy
import numpy as np
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
HEADLESS_FBX = os.path.join(
    ROOT, "unity", "CardGameClient", "Assets", "Resources", "ThreeD", "Characters", "BaseMale_Headless.fbx",
)
UNITY_CHARS_DIRS = [
    os.path.join(ROOT, "unity", "CardGameClient", "Assets", "Resources", "ThreeD", "Characters"),
    os.path.join(ROOT, "screens", "qa", "shop-project", "Assets", "Resources", "ThreeD", "Characters"),
]


def main():
    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    for m in list(bpy.data.meshes):
        bpy.data.meshes.remove(m)
    for mat in list(bpy.data.materials):
        bpy.data.materials.remove(mat)

    bpy.ops.import_scene.fbx(filepath=HEADLESS_FBX)
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    body = next(o for o in bpy.data.objects if o.type == "MESH")

    mw = body.matrix_world
    mw_inv = mw.inverted()

    bm = bmesh.new()
    bm.from_mesh(body.data)
    bm.verts.ensure_lookup_table()
    bm.edges.ensure_lookup_table()
    bm.faces.ensure_lookup_table()
    uv_lay = bm.loops.layers.uv.active
    deform_lay = bm.verts.layers.deform.active
    vg_idx = {g.name: g.index for g in body.vertex_groups}
    inner_uv = (0.0439, 0.7021)

    # 1. Удаляем обе 5-палые кисти (материал 1: Body_HandsSkin) — у игрока свои кисти первого лица!
    hand_faces = [f for f in bm.faces if f.material_index == 1]
    if hand_faces:
        bmesh.ops.delete(bm, geom=hand_faces, context="FACES")

    # 1b. На теле игрока (без 3D-головы) аккуратно срезаем высокий ворот-стойку (выше z = 1.505 + 0.45*y)
    #     и герметично закрываем низкий ворот тёмно-бирюзовой чашей (чтобы при низкой посадке EyeY=1.37 ворот не лез в камеру!).
    plane_co_w = Vector((0.0, 0.010, 1.504))
    plane_no_w = Vector((0.0, -0.41, 0.912)).normalized()
    collar_faces = [f for f in bm.faces if any((mw @ v.co).z > 1.47 and abs((mw @ v.co).x) < 0.13 for v in f.verts)]
    if collar_faces:
        c_edges = list({e for f in collar_faces for e in f.edges})
        c_verts = list({v for f in collar_faces for v in f.verts})
        cres = bmesh.ops.bisect_plane(
            bm,
            geom=c_verts + c_edges + collar_faces,
            plane_co=mw_inv @ plane_co_w,
            plane_no=(mw_inv.to_3x3() @ plane_no_w).normalized(),
            clear_outer=True,
            clear_inner=False,
        )
        c_cut_e = [g for g in cres["geom_cut"] if isinstance(g, bmesh.types.BMEdge)]
        c_cut_v = [g for g in cres["geom_cut"] if isinstance(g, bmesh.types.BMVert)]
        if c_cut_e and c_cut_v:
            rc_w = Vector(np.array([(mw @ v.co)[:] for v in c_cut_v]).mean(axis=0))
            ex1 = bmesh.ops.extrude_edge_only(bm, edges=c_cut_e)
            ex1_v = [g for g in ex1["geom"] if isinstance(g, bmesh.types.BMVert)]
            ex1_vs = set(ex1_v)
            ex1_e = [g for g in ex1["geom"] if isinstance(g, bmesh.types.BMEdge) and g.verts[0] in ex1_vs and g.verts[1] in ex1_vs]
            ex1_f = [g for g in ex1["geom"] if isinstance(g, bmesh.types.BMFace)]
            for v in ex1_v:
                wp = mw @ v.co
                rad = wp - rc_w
                rad -= plane_no_w * rad.dot(plane_no_w)
                v.co = mw_inv @ (rc_w + rad * 0.78 - plane_no_w * 0.025)
            cap_v = bm.verts.new(mw_inv @ (rc_w - plane_no_w * 0.045))
            cap_f = []
            for e in ex1_e:
                try:
                    f = bm.faces.new((e.verts[0], e.verts[1], cap_v))
                    f.material_index = 0
                    cap_f.append(f)
                except ValueError:
                    pass
            vg_sp2 = vg_idx.get("mixamorig:Spine2")
            vg_nk = vg_idx.get("mixamorig:Neck")
            if deform_lay is not None and vg_sp2 is not None:
                for v in ex1_v + [cap_v]:
                    d = v[deform_lay]
                    d.clear()
                    if vg_nk is not None:
                        d[vg_sp2] = 0.80
                        d[vg_nk] = 0.20
                    else:
                        d[vg_sp2] = 1.0
            for f in ex1_f + cap_f:
                f.material_index = 0
                f.smooth = True
                if uv_lay is not None:
                    for loop in f.loops:
                        loop[uv_lay].uv = inner_uv

    # 2. Ровно срезаем рукава ниже дельт (по плоскости на 6.5 см наружу от сустава LeftArm/RightArm),
    #    оставляя округлые плечи свитера, и герметично закрываем срез внутрь (с весами кости и тёмной UV)!
    for side, bone_name in ((1, "mixamorig:LeftArm"), (-1, "mixamorig:RightArm")):
        b = arm.data.bones[bone_name]
        sh_w = arm.matrix_world @ b.head_local
        el_w = arm.matrix_world @ b.tail_local
        arm_dir_w = (el_w - sh_w).normalized()
        cut_co_w = sh_w + arm_dir_w * 0.065
        cut_co_l = mw_inv @ cut_co_w
        cut_no_l = (mw_inv.to_3x3() @ arm_dir_w).normalized()

        arm_faces = [
            f for f in bm.faces
            if all((mw @ v.co).x * side > 0.205 and (mw @ v.co).z > 1.18 for v in f.verts)
        ]
        arm_edges = list({e for f in arm_faces for e in f.edges})
        arm_verts = list({v for f in arm_faces for v in f.verts})
        res = bmesh.ops.bisect_plane(
            bm,
            geom=arm_verts + arm_edges + arm_faces,
            plane_co=cut_co_l,
            plane_no=cut_no_l,
            clear_outer=True,
            clear_inner=False,
        )
        cut_edges = [g for g in res["geom_cut"] if isinstance(g, bmesh.types.BMEdge)]
        cut_verts = [g for g in res["geom_cut"] if isinstance(g, bmesh.types.BMVert)]
        if cut_edges and cut_verts:
            ring_w = np.array([(mw @ v.co)[:] for v in cut_verts])
            rc_w = Vector(ring_w.mean(axis=0))
            ext = bmesh.ops.extrude_edge_only(bm, edges=cut_edges)
            ext_v = [g for g in ext["geom"] if isinstance(g, bmesh.types.BMVert)]
            ext_v_set = set(ext_v)
            ext_e = [g for g in ext["geom"] if isinstance(g, bmesh.types.BMEdge) and g.verts[0] in ext_v_set and g.verts[1] in ext_v_set]
            ext_f = [g for g in ext["geom"] if isinstance(g, bmesh.types.BMFace)]
            for v in ext_v:
                wp = mw @ v.co
                rad = wp - rc_w
                rad -= arm_dir_w * rad.dot(arm_dir_w)
                v.co = mw_inv @ (rc_w + rad * 0.35 - arm_dir_w * 0.015)
            cap_v = bm.verts.new(mw_inv @ (rc_w - arm_dir_w * 0.022))
            cap_f = []
            for e in ext_e:
                try:
                    f = bm.faces.new((e.verts[0], e.verts[1], cap_v))
                    f.material_index = 0
                    cap_f.append(f)
                except ValueError:
                    pass
            vg_arm = vg_idx.get(bone_name)
            vg_sh = vg_idx.get("mixamorig:LeftShoulder" if side > 0 else "mixamorig:RightShoulder")
            if deform_lay is not None and vg_arm is not None:
                for v in ext_v + [cap_v]:
                    d = v[deform_lay]
                    d.clear()
                    if vg_sh is not None:
                        d[vg_arm] = 0.65
                        d[vg_sh] = 0.35
                    else:
                        d[vg_arm] = 1.0
            bm.normal_update()
            mw_rot = mw.to_3x3()
            for f in ext_f + cap_f:
                f.material_index = 0
                f.smooth = True
                if (mw_rot @ f.normal).dot(arm_dir_w) < 0.0:
                    f.normal_flip()
                if uv_lay is not None:
                    for loop in f.loops:
                        loop[uv_lay].uv = inner_uv
        print(f"=== FPS BODY CUT side={side:+d}: cutCo={tuple(round(c,3) for c in cut_co_w)}, ringVerts={len(cut_verts)}")

    # Удаляем случайные оторванные куски рукавов, если остались далеко по |x| > 0.28
    stray = [f for f in bm.faces if any(abs((mw @ v.co).x) > 0.30 and (mw @ v.co).z > 0.85 for v in f.verts)]
    if stray:
        bmesh.ops.delete(bm, geom=stray, context="FACES")

    ngons = [f for f in bm.faces if len(f.verts) > 4]
    if ngons:
        bmesh.ops.triangulate(bm, faces=ngons)

    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.normal_update()
    bm.to_mesh(body.data)
    bm.free()
    body.data.update()

    # Убираем пустой второй слот материала (кистей больше нет — остаётся один чистый слот Body_Outfit!)
    while len(body.data.materials) > 1:
        body.data.materials.pop(index=-1)

    tmp_fbx = os.path.join(os.environ.get("TEMP", r"C:\Windows\Temp"), "BaseMale_FpsBody.fbx")
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
    print(f"=== EXPORTED BaseMale_FpsBody.fbx: verts={len(body.data.vertices)}, polys={len(body.data.polygons)}, size={os.path.getsize(tmp_fbx)//1024} KB")

    for d in UNITY_CHARS_DIRS:
        shutil.copy2(tmp_fbx, os.path.join(d, "BaseMale_FpsBody.fbx"))
        print(f"=== Synced BaseMale_FpsBody.fbx to {d}")


if __name__ == "__main__":
    main()
