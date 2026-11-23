"""Высота кожи свободной кисти над сукном, по сетке: самая низкая вершина каждой фаланги, ладони, запястья и предплечья.
Кости для этого мало: probe_table.py смотрит три подушечки, а поза, в которой безымянный и мизинец уходили в стол
на 24 и 36 мм, по ним проходила. Касание — около 0 мм, минус — кожа в столе.

Поза берётся прямо из чисел in_game.py, файл .blend не сохраняется. Переменные окружения:
  REST_POSE=TAP_DOWN — вместо позы покоя взять позу свободной руки из animations.py (TAP_UP, HOVER, FIST_DOWN и так далее);
  REST='{"wrist":[0.19,0.47,0.041],"along":[-0.38,0.92,-0.06],"back":[-0.4,0.06,1],"elbow":[0.16,-0.32,0.03],
         "fingers":{"Index":[3,-2.5,-4,0], ...},"thumb":[[2,-28.5,13],-7.5,10]}' — пробные числа покоя без правки файла.
Запуск: blender art3d/hands/CardCity_Hands.blend --background --disable-autoexec --python scripts/3d/hands/probe_rest.py"""
import json
import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(__file__))
import in_game

PARTS = ("Hand", "MetaIndex", "MetaPinky", "Thumb01", "Thumb02", "Thumb03", "Index01", "Index02", "Index03", "Middle01", "Middle02", "Middle03",
         "Ring01", "Ring02", "Ring03", "Pinky01", "Pinky02", "Pinky03", "ForearmTwist", "Forearm")


def trial(rest):
    """Пробные числа покоя из переменной REST."""
    data = json.loads(os.environ["REST"])
    if "wrist" in data:
        rest["wrist"] = in_game.on_table(*data["wrist"])
    for key, name in (("along", "along"), ("back", "back"), ("elbow", "elbow_from_wrist")):
        if key in data:
            rest[name] = in_game.table_dir(*data[key])
    if "fingers" in data:
        rest["pose"]["fingers"] = {finger: tuple(values) for finger, values in data["fingers"].items()}
    if "thumb" in data:
        rest["pose"]["thumb"] = (tuple(data["thumb"][0]), data["thumb"][1], data["thumb"][2])


def above(point):
    return (point.dot(in_game.TABLE_UP) + in_game.EYE_ABOVE_TABLE) * 1000


def main():
    free = in_game.RIGHT
    if os.environ.get("REST"):
        trial(free)
    if os.environ.get("REST_POSE"):
        import animations
        free = getattr(animations, os.environ["REST_POSE"])
    armature = bpy.data.objects["Rig_Hands"]
    armature.animation_data.action = None
    armature.matrix_world.identity()
    for bone in armature.pose.bones:
        bone.rotation_mode = "XYZ"
        bone.rotation_euler = (0, 0, 0)
        bone.location = (0, 0, 0)
    in_game.pose_both(armature, in_game.LEFT, free)
    bpy.context.view_layer.update()
    side = in_game.FREE_HAND
    arm = bpy.data.objects["Arm_Male" + side]
    mesh = arm.evaluated_get(bpy.context.evaluated_depsgraph_get()).to_mesh()
    names = {group.index: group.name for group in arm.vertex_groups}
    low = {}
    for vertex in mesh.vertices:
        height = above(arm.matrix_world @ vertex.co)
        for group in vertex.groups:
            if group.weight > .5:
                low[names[group.group]] = min(low.get(names[group.group], 1e9), height)
    for part in PARTS:
        if part + side in low:
            print("=== LOW %-12s %6.1f mm" % (part, low[part + side]))
    for tip in ("TipThumb", "TipIndex", "TipMiddle"):
        print("=== BONE %-10s %6.1f mm" % (tip, above(armature.matrix_world @ armature.pose.bones[tip + side].head)))


if __name__ == "__main__":
    main()
