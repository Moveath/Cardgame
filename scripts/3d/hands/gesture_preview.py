"""Быстрый предпросмотр жестов с места игрока: пик каждого жеста из animations.CLIPS, кадр 16:9 как в игре.
Позы ставятся прямо из чисел animations.py и in_game.py, без пересборки действий и без сохранения .blend: правишь
числа — сразу видишь кадр. Rest — поза партии (веер в левой руке, правая на сукне).
CROP=1 — только область вокруг руки (свободной в жестах, с веером в Rest) в разрешении 3840×2160: видно пальцы.
Рендер Cycles на процессоре, 12 сэмплов, текстуры не больше 2048: можно при открытом Unity. При нехватке памяти
снимать по одному жесту за запуск (второй кадр в том же процессе может не получить память).
Кадры — screens/3d-hands/gesture-<жест>.png (с CROP — gesture-<жест>-crop.png).
Запуск: blender art3d/hands/CardCity_Hands.blend --background --disable-autoexec --python scripts/3d/hands/gesture_preview.py -- [жесты]"""
import math
import os
import sys

import bpy
from bpy_extras.object_utils import world_to_camera_view

sys.path.insert(0, os.path.dirname(__file__))
os.environ.setdefault("SKIN", "young")
import animations
import in_game
import seat_view
import source

OUT = os.path.join(source.ROOT, "screens", "3d-hands")
GESTURES = ("Point", "ThumbsUp", "ThumbsDown", "TapTable", "Shrug", "Fist")
CROP = os.environ.get("CROP") == "1"


def peak_pose(name):
    """Левая и правая рука в кадре пика жеста — так же, как их запекает animations.bake."""
    peak = animations.PEAKS[name]
    for frame, left, right in animations.frames(animations.CLIPS[name]):
        if frame == peak:
            return left, right
    raise KeyError(name)


def pose(armature, left, right):
    for bone in armature.pose.bones:
        bone.rotation_mode = "XYZ"
        bone.rotation_euler = (0, 0, 0)
        bone.location = (0, 0, 0)
    in_game.pose_both(armature, left, right)
    bpy.context.view_layer.update()


def hand_border(sc, armature, suffix, margin=.35):
    """Прямоугольник кадра вокруг кисти: запястье, суставы и кончики всех пальцев, с запасом."""
    bones = armature.pose.bones
    names = ["Hand" + suffix] + [f + k + suffix for f in ("Thumb", "Index", "Middle", "Ring", "Pinky") for k in ("01", "02", "03")]
    points = [armature.matrix_world @ bones[n].head for n in names] + [armature.matrix_world @ bones[n].tail for n in names]
    view = [world_to_camera_view(sc, sc.camera, p) for p in points]
    x0, x1 = min(v.x for v in view), max(v.x for v in view)
    y0, y1 = min(v.y for v in view), max(v.y for v in view)
    side = max(x1 - x0, (y1 - y0) * 9 / 16) * (1 + margin)
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    w, h = side / 2, side * 16 / 9 / 2
    return max(0., cx - w), min(1., cx + w), max(0., cy - h), min(1., cy + h)


def main():
    only = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    sc, armature, to_eye = seat_view.stage(rebuild=False)
    armature.animation_data.action = None
    # Cycles на процессоре с малым числом сэмплов: EEVEE и Workbench в фоне падают, когда видеокарту и память держит Unity.
    sc.cycles.device = "CPU"
    sc.cycles.denoiser = "OPENIMAGEDENOISE"
    sc.cycles.samples = 12 if not CROP else 24
    sc.render.use_simplify = True
    sc.cycles.texture_limit_render = "2048"
    # Обрезка — в четыре раза крупнее экрана 1080p: на экране кисть занимает 150–200 точек, пальцы в ней не разглядеть.
    width, height = (3840, 2160) if CROP else (960, 540)
    sc.render.resolution_x, sc.render.resolution_y = width, height
    sc.render.resolution_percentage = 100
    sc.camera.data.angle_y = math.radians(seat_view.field_of_view(width / height))
    names = [n for n in (only or ("Rest",) + GESTURES)]
    for name in names:
        if name == "Rest":
            left, right = in_game.LEFT, in_game.RIGHT
        else:
            left, right = peak_pose(name)
        pose(armature, left, right)
        border = hand_border(sc, armature, in_game.FAN_HAND if name == "Rest" else in_game.FREE_HAND) if CROP else None
        seat_view.render(sc, os.path.join(OUT, "gesture-" + name + ("-crop" if CROP else "") + ".png"), border)
    print("=== GESTURES", names)


if __name__ == "__main__":
    main()
