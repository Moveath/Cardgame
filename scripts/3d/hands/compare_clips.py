"""Одинаково ли ложится кожа в клипах двух файлов рук: положение каждой вершины в каждом втором кадре каждого клипа.
Нужна после правок скелета, которые не должны менять игру (оси сгиба, крен костей): клипы игры пересобраны, а рука
в кадре обязана остаться той же.

Снять образец:  blender <старый.blend> --background --disable-autoexec --python scripts/3d/hands/compare_clips.py -- dump <образец.npz>
Сравнить:       blender <новый.blend>  --background --disable-autoexec --python scripts/3d/hands/compare_clips.py -- check <образец.npz>
Печатает наибольшее расхождение по каждому клипу в миллиметрах. Клипы с именами не на Pose_ и не на Take_ пропускает:
это клипы игры, остальные в игру не идут."""
import sys

import bpy
import numpy as np

MESHES = ("Arm_Male_L", "Arm_Male_R", "Sleeve_L", "Sleeve_R")
STEP = 2


def frames(armature):
    sc = bpy.context.scene
    for action in sorted(bpy.data.actions, key=lambda a: a.name):
        if not action.name.startswith(("Pose_", "Take_")):
            continue
        armature.animation_data.action = action
        first, last = (int(v) for v in action.frame_range)
        for frame in range(first, last + 1, STEP):
            sc.frame_set(frame)
            depsgraph = bpy.context.evaluated_depsgraph_get()
            parts = []
            for name in MESHES:
                ob = bpy.data.objects[name].evaluated_get(depsgraph)
                mesh = ob.to_mesh()
                co = np.zeros(len(mesh.vertices) * 3, dtype=np.float32)
                mesh.vertices.foreach_get("co", co)
                ob.to_mesh_clear()
                parts.append(co)
            yield action.name, frame, np.concatenate(parts)


def main():
    mode, path = sys.argv[sys.argv.index("--") + 1:][:2]
    armature = bpy.data.objects["Rig_Hands"]
    armature.animation_data_create()
    if mode == "dump":
        data = {"%s|%d" % (name, frame): co for name, frame, co in frames(armature)}
        np.savez_compressed(path, **data)
        print("=== CLIPS DUMP %d кадров -> %s" % (len(data), path))
        return
    ref = np.load(path)
    worst, seen = {}, set()
    for name, frame, co in frames(armature):
        key = "%s|%d" % (name, frame)
        seen.add(key)
        if key not in ref:
            worst[name] = float("inf")
            continue
        worst[name] = max(worst.get(name, 0.), float(np.abs(co - ref[key]).max()) * 1000)
    for name in sorted(worst):
        print("=== CLIP %-18s наибольшее расхождение %.4f мм" % (name, worst[name]))
    missing = sorted({k.split("|")[0] for k in ref.files if k not in seen})
    print("=== CLIPS CHECK клипов %d, наибольшее расхождение %.4f мм, нет в новом файле: %s" % (len(worst), max(worst.values()), missing or "нет"))


main()
