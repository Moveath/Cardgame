"""Оси сгиба пальцев как у живой руки в готовом CardCity_Hands.blend, без пересборки сетки, весов и текстур.

Раньше у каждого пальца была одна ось на все фаланги — плоскостью через три сустава почти прямого пальца. Она кренилась:
указательный на 14–16°, средний на 28–32°, безымянный на 8–15°, мизинец на 5–11° от ногтя. В кулаке пальцы шли наискось,
скручивались и входили друг в друга (кулак — 609 пересечений сетки, «класс» — 430).

Теперь оси как у живой руки (rig.palm_hinge и rig.finger_hinge при новой сборке):
- основание пальца (пястная кость и основная фаланга) гнётся по плоскости ладони: ось перпендикулярна фаланге и тылу
  кисти. В кулаке основные фаланги идут к ладони рядком, в порядке костяшек;
- средний и дальний суставы гнутся поперёк ногтя: ось перпендикулярна дальней фаланге и её тылу по ногтю
  (joints.nail_dorsal). У безымянного и мизинца ноготь повёрнут к большому пальцу, поэтому в кулаке их кончики сходятся
  к основанию большого пальца, как у живой руки.
Правая рука — зеркало левой.

Меняется только крен костей пальцев (roll): вершины в покое не двигаются, веса те же. Промежуточные кости суставов
Joint* получают крен своей кости, как в rig.build_armature. После запуска пересобрать позы и анимации (update_motion.py),
проверить пересечения (probe_overlap.py) и выгрузить (export.py).
Запуск: blender art3d/hands/CardCity_Hands.blend --background --disable-autoexec --python scripts/3d/hands/fix_hinges.py"""
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Vector

sys.path.insert(0, os.path.dirname(__file__))
import joints as J

ARMATURE = "Rig_Hands"


def left_hinges(armature):
    """Оси левой руки в осях скелета: {палец: (ось основания, ось среднего и дальнего суставов)}."""
    ob = bpy.data.objects["Arm_Male_L"]
    co, _, verts = J.read_mesh(ob.data)
    to_armature = np.array(armature.matrix_world.inverted() @ ob.matrix_world)
    points = (np.c_[co, np.ones(len(co))] @ to_armature.T)[:, :3]
    bones = armature.data.bones
    back_of_hand = bones["Hand_L"].matrix_local.to_3x3() @ Vector((0, 0, 1))
    result = {}
    for name in J.FINGERS:
        first, far = bones[name + "01_L"], bones[name + "03_L"]
        dip, tip = np.array(far.head_local), np.array(far.tail_local)
        back = np.array(far.matrix_local.to_3x3() @ Vector((0, 0, 1)))
        dorsal = J.nail_dorsal(points, verts, J.FINGER_SETS[name][3], dip, tip, back)
        nail = Vector(np.cross((tip - dip) / np.linalg.norm(tip - dip), dorsal)).normalized()
        palm = (first.tail_local - first.head_local).normalized().cross(back_of_hand).normalized()
        result[name] = (palm, nail)
    return result


def aligned(axis, bone):
    old = bone.matrix_local.to_3x3() @ Vector((1, 0, 0))
    return axis if axis.dot(old) > 0 else -axis


def main():
    armature = bpy.data.objects[ARMATURE]
    bones = armature.data.bones
    plan = {}
    for name, (palm, nail) in left_hinges(armature).items():
        for side in ("_L", "_R"):
            flip = (lambda v: v) if side == "_L" else (lambda v: Vector((-v.x, v.y, v.z)))
            p = aligned(flip(palm), bones[name + "01" + side])
            n = aligned(flip(nail), bones[name + "02" + side])
            for bone_name in ("Meta" + name, name + "01", "SocketRing" + name):
                plan[bone_name + side] = p
            for bone_name in (name + "02", name + "03") + (("Tip" + name,) if name in ("Index", "Middle") else ()):
                plan[bone_name + side] = n
            old01 = bones[name + "01" + side].matrix_local.to_3x3() @ Vector((1, 0, 0))
            old02 = bones[name + "02" + side].matrix_local.to_3x3() @ Vector((1, 0, 0))
            print(f"HINGE {name + side:9s} base turned {math.degrees(p.angle(old01)):5.1f} deg, "
                  f"middle and far joints {math.degrees(n.angle(old02)):5.1f} deg, base vs nail {math.degrees(p.angle(n)):5.1f} deg")

    bpy.context.view_layer.objects.active = armature
    armature.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    edit = armature.data.edit_bones
    for bone_name, axis in plan.items():
        bone = edit[bone_name]
        bone.align_roll(axis.cross(bone.tail - bone.head))
    for side in ("_L", "_R"):
        for name in J.FINGERS:
            for k in ("01", "02", "03"):
                edit["Joint" + name + k + side].roll = edit[name + k + side].roll
    bpy.ops.object.mode_set(mode="OBJECT")
    armature.select_set(False)
    worst = 0.
    for bone_name, axis in plan.items():
        b = bones[bone_name]
        along = (b.tail_local - b.head_local).normalized()
        ideal = axis - along * axis.dot(along)
        worst = max(worst, math.degrees((b.matrix_local.to_3x3() @ Vector((1, 0, 0))).angle(ideal)))
    print(f"CHECK worst axis error after fix {worst:.2f} deg")
    bpy.ops.wm.save_mainfile()
    print("=== HINGES FIXED")


main()
