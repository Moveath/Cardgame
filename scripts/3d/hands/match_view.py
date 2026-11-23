"""Наша правая рука в пробной позе с ракурса видео-образца (references/hands/видео-образцы): по этим кадрам поза
сверяется с роликом глазами. Числа с видео через MediaPipe с одного ракурса неточны, поэтому итог всегда сверяется так.

Виды: palm — со стороны ладони, большой палец вверху (как «класс» и кулак на роликах); back — с тыла; radial — со
стороны большого пальца; top — сверху сзади. Задания — JSON в переменной JOBS:
  [{"name": "fist", "pose": {"fingers": {"Index": [0, 92, 94, 75], ...}, "thumb": [[-30, 30, -14], 18, 45]}, "views": ["palm", "back"]}]
Кадры — в папку MATCH_OUT, размер MATCH_SIZE (по умолчанию 520).
Запуск: blender art3d/hands/CardCity_Hands.blend --background --disable-autoexec --python scripts/3d/hands/match_view.py"""
import json
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(__file__))
os.environ.setdefault("SKIN", "young")
import pose_closeup
import poses

OUT = os.environ["MATCH_OUT"]
SIZE = int(os.environ.get("MATCH_SIZE", "520"))


def main():
    jobs = json.loads(os.environ["JOBS"])
    sc, camera = pose_closeup.setup()
    sc.render.resolution_x = SIZE
    sc.render.resolution_y = int(SIZE * .85)
    sc.cycles.samples = 20
    sc.render.use_simplify = True
    sc.cycles.texture_limit_render = "1024"
    camera.data.lens = 50
    # Свет от камеры: ладонная сторона иначе остаётся в тени.
    fill = bpy.data.objects.new("match fill", bpy.data.lights.new("match fill", "SUN"))
    fill.data.energy = 2.2
    sc.collection.objects.link(fill)
    armature = bpy.data.objects["Rig_Hands"]
    armature.animation_data.action = None
    armature.matrix_world.identity()
    os.makedirs(OUT, exist_ok=True)
    for job in jobs:
        for bone in armature.pose.bones:
            bone.rotation_mode = "XYZ"
            bone.rotation_euler = (0, 0, 0)
            bone.location = (0, 0, 0)
        p = job["pose"]
        pose = {"fingers": {k: tuple(v) for k, v in p["fingers"].items()}, "thumb": (tuple(p["thumb"][0]), p["thumb"][1], p["thumb"][2]),
                "wrist": tuple(p.get("wrist", (0, 0)))}
        poses.apply_pose(armature, pose, "_R")
        bpy.context.view_layer.update()
        bones = armature.pose.bones
        hand = bones["Hand_R"]
        axes = hand.matrix.to_3x3()
        along, back = (axes @ Vector((0, 1, 0))).normalized(), (axes @ Vector((0, 0, 1))).normalized()
        radial = (bones["Index01_R"].head - bones["Pinky01_R"].head).normalized()
        center = bones["Middle01_R"].head + along * .01
        for view in job.get("views", ["palm"]):
            if view == "palm":
                eye, up = -back * .36 + radial * .05 - along * .02, radial
            elif view == "back":
                eye, up = back * .34 + radial * .02 + along * .02, radial
            elif view == "radial":
                eye, up = radial * .34 + back * .04, back
            else:
                eye, up = back * .30 - along * .16, along
            forward = (-eye).normalized()
            right = forward.cross(up).normalized()
            true_up = right.cross(forward)
            roll = math.radians(job.get("roll", 0))
            true_up = true_up * math.cos(roll) + right * math.sin(roll)
            right = forward.cross(true_up).normalized()
            camera.matrix_world = Matrix((right.to_4d(), true_up.to_4d(), (-forward).to_4d(), Vector((0, 0, 0, 1)))).transposed()
            camera.matrix_world.translation = center + eye
            fill.rotation_euler = camera.matrix_world.to_euler()
            sc.render.filepath = os.path.join(OUT, "%s-%s.png" % (job["name"], view))
            bpy.ops.render.render(write_still=True)
        print("=== MATCH", job["name"])


if __name__ == "__main__":
    main()
