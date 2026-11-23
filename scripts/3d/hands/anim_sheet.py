"""Лист проверки анимаций: из каждой Take_* снимается кадр, по которому виден жест, с места игрока. Лист — screens/3d-hands/animations.png.
Запуск после update_motion.py: blender art3d/hands/CardCity_Hands.blend --background --disable-autoexec --python scripts/3d/hands/anim_sheet.py"""
import math
import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(__file__))
import animations
import seat_view
import source

OUT = os.path.join(source.ROOT, "screens", "3d-hands")


def main():
    sc, armature, to_eye = seat_view.stage(rebuild=False)
    sc.render.resolution_x, sc.render.resolution_y = 800, 450
    sc.camera.data.angle_y = math.radians(seat_view.field_of_view(800 / 450))
    sc.cycles.samples = 24
    frames = []
    only = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    for name, peak in animations.PEAKS.items():
        if only and name not in only:
            continue
        armature.animation_data.action = bpy.data.actions[animations.PREFIX + name]
        sc.frame_set(peak + 1)
        path = os.path.join(OUT, "anim-" + name + ".png")
        seat_view.render(sc, path)
        frames.append((name, path))
    print("=== SHEET", [n for n, _ in frames])


main()
