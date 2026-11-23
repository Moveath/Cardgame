"""Угловатые суставы пальцев вместо «резинового шланга».

У каждого сустава кожа переходит с кости на промежуточную кость Joint* и с неё на следующую кость. В rig.py зона перехода
была широкой: с тыла до 10 мм в каждую сторону от среднего сустава и до 15 мм от основания пальца. Палец гнулся ровной
дугой на всю эту длину, и костяшки не было. У живого пальца кость упирается в кожу: фаланга идёт прямо до самого сустава
и поворачивает на коротком участке, а с ладонной стороны кожа складывается в чёткую складку.

Скрипт сужает зону в готовом файле, не трогая остальные веса: у вершин рядом с суставом сумма весов трёх костей
(кость до сустава, Joint*, кость после) остаётся прежней и делится заново по положению вершины вдоль пальца. С тыла зона
уже, с ладони чуть шире, по бокам — между ними. Женские руки получают те же веса (сетки совпадают вершина в вершину).

После него: update_motion.py и export.py. bake_smooth_weights.py после него запускать нельзя — он снова размягчит сгибы.
DRY=1 — посчитать и напечатать, файл не сохранять.
Запуск: blender art3d/hands/CardCity_Hands.blend --background --disable-autoexec --python scripts/3d/hands/sharpen_joints.py"""
import os

import bpy
import numpy as np

MAX_INFLUENCES = 4
FINGERS = ("Index", "Middle", "Ring", "Pinky")
# Сустав: кость после него, кость до него, полуширина зоны с тыла и с ладони (м), прежняя зона, в которой веса пересчитываются.
JOINTS = ([(finger + "01", "Meta" + finger, .0065, .0070, .017) for finger in FINGERS]
          + [(finger + "02", finger + "01", .0035, .0040, .012) for finger in FINGERS]
          + [(finger + "03", finger + "02", .0025, .0030, .009) for finger in FINGERS]
          + [("Thumb02", "Thumb01", .0035, .0050, .014), ("Thumb03", "Thumb02", .0030, .0035, .010)])


def smooth(x):
    x = np.clip(x, 0., 1.)
    return x * x * (3 - 2 * x)


def unit(v):
    return v / np.linalg.norm(v)


def axes(bone):
    matrix = np.array(bone.matrix_local.to_3x3())
    return matrix[:, 1], matrix[:, 2]


def read_weights(ob):
    names = [group.name for group in ob.vertex_groups]
    return [{names[g.group]: g.weight for g in vertex.groups if g.weight > 1e-5} for vertex in ob.data.vertices]


def write(ob, table):
    for vertex, row in zip(ob.data.vertices, table):
        for g in list(vertex.groups):
            ob.vertex_groups[g.group].remove([vertex.index])
        for name, weight in row.items():
            group = ob.vertex_groups.get(name) or ob.vertex_groups.new(name=name)
            group.add([vertex.index], weight, "REPLACE")


def trim(row):
    """Не больше четырёх костей на вершину, сумма весов — единица."""
    kept = sorted(row.items(), key=lambda item: -item[1])[:MAX_INFLUENCES]
    total = sum(weight for _, weight in kept)
    return {name: weight / total for name, weight in kept if weight / total > 1e-4}


def sharpen(armature, ob, suffix):
    bones = armature.data.bones
    co = np.array([tuple(ob.matrix_world @ vertex.co) for vertex in ob.data.vertices])
    co = co @ np.array(armature.matrix_world.inverted().to_3x3()).T + np.array(armature.matrix_world.inverted().translation)
    table = read_weights(ob)
    changed, moved = set(), 0.
    for after, before, dorsal, palmar, zone in JOINTS:
        child, parent, helper = after + suffix, before + suffix, "Joint" + after + suffix
        along_child, back_child = axes(bones[child])
        along_parent, back_parent = axes(bones[parent])
        along = unit(along_child + along_parent)
        back = back_child + back_parent
        back = unit(back - along * (back @ along))
        side_axis = np.cross(along, back)
        rel = co - np.array(bones[child].head_local)
        y, z, x = rel @ along, rel @ back, rel @ side_axis
        side = z / np.maximum(np.hypot(x, z), 1e-6)
        span = palmar + (dorsal - palmar) * smooth((side + 1) / 2)
        first, second = smooth((y + span) / span), smooth(y / span)
        shares = (1 - first, first - second, second)
        for index, row in enumerate(table):
            total = row.get(parent, 0.) + row.get(helper, 0.) + row.get(child, 0.)
            if total < .02 or abs(y[index]) > zone:
                continue
            new = {parent: total * shares[0][index], helper: total * shares[1][index], child: total * shares[2][index]}
            shift = sum(abs(new[name] - row.get(name, 0.)) for name in new) / 2
            if shift < 1e-4:
                continue
            for name, weight in new.items():
                if weight > 1e-5:
                    row[name] = float(weight)
                else:
                    row.pop(name, None)
            changed.add(index)
            moved += shift
    table = [trim(row) for row in table]
    print("SHARPEN %s: %d vertices changed, mean weight moved %.2f" % (ob.name, len(changed), moved / max(1, len(changed))))
    return table


def main():
    armature = bpy.data.objects["Rig_Hands"]
    armature.animation_data.action = None
    armature.data.pose_position = "REST"
    bpy.context.view_layer.update()
    for suffix in ("_L", "_R"):
        table = sharpen(armature, bpy.data.objects["Arm_Male" + suffix], suffix)
        for gender in ("Male", "Female"):
            write(bpy.data.objects["Arm_%s%s" % (gender, suffix)], table)
    armature.data.pose_position = "POSE"
    if os.environ.get("DRY") == "1":
        print("=== DRY RUN")
        return
    bpy.ops.wm.save_mainfile()
    print("=== JOINTS SHARPENED")


if __name__ == "__main__":
    main()
