"""Лист готовых поз кисти на руке игры: правая рука в позах из tools/mocap/library/<файл>.json (выход retarget.py),
два вида — с тыла и со стороны ладони. По листу выбирают, какие готовые позы идут в игру (animations.py, in_game.py).
Кадры — в папку SHEET_OUT (по умолчанию screens/3d-hands/library), имя <поза>-<вид>.png.
Запуск: blender art3d/hands/CardCity_Hands.blend --background --disable-autoexec --python scripts/3d/hands/library_sheet.py -- [позы]"""
import json
import os
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(__file__))
os.environ.setdefault("SKIN", "young")
import pose_closeup
import poses
import source

LIBRARY = os.path.join(source.ROOT, "tools", "mocap", "library", os.environ.get("SHEET_POSES", "xr_tools_poses.json"))
OUT = os.environ.get("SHEET_OUT") or os.path.join(source.ROOT, "screens", "3d-hands", "library")
SIZE = int(os.environ.get("SHEET_SIZE", "360"))


def library():
    with open(LIBRARY, encoding="utf-8") as f:
        data = json.load(f)
    result = {}
    for name, found in data.items():
        pose = found[0]["pose"]
        result[name] = {"fingers": {n: tuple(pose[n]) for n in ("Index", "Middle", "Ring", "Pinky")},
                        "thumb": (tuple(pose["Thumb"][0]), pose["Thumb"][1], pose["Thumb"][2]), "wrist": (0, 0)}
    return result


def main():
    found = library()
    only = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else list(found)
    sc, camera = pose_closeup.setup()
    sc.render.resolution_x = sc.render.resolution_y = SIZE
    sc.cycles.samples = 12
    sc.render.use_simplify = True
    sc.cycles.texture_limit_render = "1024"
    armature = bpy.data.objects["Rig_Hands"]
    armature.animation_data.action = None
    armature.matrix_world.identity()
    os.makedirs(OUT, exist_ok=True)
    for name in only:
        for bone in armature.pose.bones:
            bone.rotation_mode = "XYZ"
            bone.rotation_euler = (0, 0, 0)
            bone.location = (0, 0, 0)
        poses.apply_pose(armature, found[name], "_R")
        bpy.context.view_layer.update()
        hand = armature.pose.bones["Hand_R"]
        center = (armature.matrix_world @ hand.head).lerp(armature.matrix_world @ armature.pose.bones["Middle01_R"].tail, .75)
        axes = hand.matrix.to_3x3()
        along, back, side = axes @ Vector((0, 1, 0)), axes @ Vector((0, 0, 1)), axes @ Vector((1, 0, 0))
        views = {"back": back * .30 + along * .04 - side * .10, "palm": -back * .28 + along * .10 - side * .12}
        for view, offset in views.items():
            camera.location = center + offset
            camera.rotation_euler = (center - camera.location).to_track_quat("-Z", "Y").to_euler()
            sc.render.filepath = os.path.join(OUT, "%s-%s.png" % (name.replace(" ", "_"), view))
            bpy.ops.render.render(write_still=True)
        print("=== LIBRARY POSE", name)


if __name__ == "__main__":
    main()
