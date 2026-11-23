"""Кулак с ракурсов фото автора (references/hands/кулак-фото): по этим кадрам большой палец сверяется с его кулаком.

Ракурсы (кисть в своих осях: к большому пальцу, вдоль пальцев, к тылу):
  сверху  — как фото 3 и 4 (левая рука, с тыла и со стороны большого пальца, большой палец вверху, костяшки влево);
  сбоку   — как фото 2 (правая рука со стороны ладони, большой палец вверху, костяшки влево);
  спереди — как фото 1 (правая рука, на камеру смотрят костяшки, тыл вверху).
Кожа игры (SKIN=smooth), вес формы кулака fist_smooth — TP_SMOOTH (1 — как в игре).
TP_JSON — файл с позой кулака вместо gestures.FIST (поля как в gestures.py). TP_TAG — приписка к именам кадров.
TP_VIEW_<ракурс>="dx,dy,dz;ux,uy,uz;расстояние" — свой ракурс для подгонки под фото.
Кадры — screens/3d-hands/большой-палец/кадры/<ракурс><TAG>.png.
Запуск: blender <.blend> --background --disable-autoexec --python scripts/3d/hands/thumb_photo.py -- [ракурсы]"""
import json
import os
import sys

import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(__file__))
os.environ.setdefault("SKIN", "smooth")
import gesture_lab  # noqa: E402
import gestures  # noqa: E402
import hand_anatomy  # noqa: E402
import source  # noqa: E402
from gesture_lab import Skin  # noqa: E402

OUT = os.environ.get("TP_OUT") or os.path.join(source.ROOT, "screens", "3d-hands", "большой-палец", "кадры")
# Ракурс: рука, откуда смотрит камера (в осях кисти), где у кадра верх, расстояние до центра кисти (м). Объектив 85 мм.
VIEWS = {
    "сверху": ("_L", (.9, .3, .4), (1., 0., 0.), .34),
    "сбоку": ("_R", (.25, .1, -1.), (1., 0., 0.), .36),
    "спереди": ("_R", (0., 1., .3), (0., 0., 1.), .36),
}


def view(name):
    """Ракурс по имени; TP_VIEW_<имя>="L|R;dx,dy,dz;ux,uy,uz;расстояние" задаёт свой (и новый, которого нет в VIEWS)."""
    custom = os.environ.get("TP_VIEW_" + name)
    if not custom:
        return VIEWS[name]
    parts = custom.split(";")
    return ("_" + parts[0], tuple(map(float, parts[1].split(","))), tuple(map(float, parts[2].split(","))),
            float(parts[3]) if len(parts) > 3 else .36)


def fist_pose():
    path = os.environ.get("TP_JSON")
    if not path:
        return gestures.FIST
    with open(path, encoding="utf-8") as f:
        pose = json.load(f)
    pose = next(iter(pose.values())) if "fingers" not in pose else pose
    pose["fingers"] = {k: tuple(v) for k, v in pose["fingers"].items()}
    for key in ("thumb", "thumb_bend", "wrist"):
        if key in pose:
            pose[key] = tuple(pose[key])
    return pose


def shoot(sc, camera, center, axes, direction, up, distance, path):
    toward_thumb, along, back = axes
    a, b, c = direction
    ua, ub, uc = up
    look = (toward_thumb * a + along * b + back * c).normalized()
    top = (toward_thumb * ua + along * ub + back * uc).normalized()
    eye = center + look * distance
    forward = (center - eye).normalized()
    right = forward.cross(top).normalized()
    true_up = right.cross(forward)
    camera.matrix_world = Matrix((right.to_4d(), true_up.to_4d(), (-forward).to_4d(), Vector((0, 0, 0, 1)))).transposed()
    camera.matrix_world.translation = eye
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)


def main():
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    names = args or list(VIEWS)
    os.makedirs(OUT, exist_ok=True)
    armature = bpy.data.objects["Rig_Hands"]
    armature.animation_data_create()
    armature.animation_data.action = None
    armature.matrix_world.identity()
    sc, camera = gesture_lab.stage()
    # Текстуры не больше 1024: так кадр считается и при открытом Unity (с LAB_CPU=1 — на процессоре).
    sc.render.use_simplify = True
    sc.cycles.texture_limit_render = "1024"
    pose = fist_pose()
    smooth = float(os.environ.get("TP_SMOOTH", "1"))
    if "fist_smooth" in armature:
        armature["fist_smooth"] = smooth
    tag = os.environ.get("TP_TAG", "")
    skins = {}
    gesture_lab.reset(armature)
    for side in ("_L", "_R"):
        hand_anatomy.apply(armature, pose, side)
    bpy.context.view_layer.update()
    for name in names:
        side, direction, up, distance = view(name)
        skins.setdefault(side, Skin(armature, side))
        gesture_lab.only_hand(side)
        center, *axes = gesture_lab.frame_hand(armature, skins[side], side)
        shoot(sc, camera, center, axes, direction, up, distance, os.path.join(OUT, "%s%s.png" % (name, tag)))
    print("=== THUMB PHOTO", names, tag)


if __name__ == "__main__":
    main()
