"""Плоскость сгиба пальцев как у живой руки: согнутые пальцы сходятся к основанию большого пальца, а не уходят к мизинцу.

Замер candidate_check.py (2026-11-15): согнутый отдельно средний палец нашей руки уходит вбок к мизинцу на 20 % своей
длины, указательный на 10 %, и в кулаке средний входит в безымянный. У живой руки согнутые пальцы сходятся к ладьевидной
кости: каждый следующий от указательного к мизинцу наклонён к большому пальцу чуть сильнее.

Палец гнётся в одной плоскости, как у живой руки: оси всех трёх суставов параллельны. Ось идёт вдоль линии костяшек
(от основания мизинца к основанию указательного), а не поперёк самого пальца: в покое пальцы модели разведены веером
(указательный на 11°, мизинец на 28°), и палец, который гнётся поперёк своего направления, складывается поперёк соседей.
У живой руки основной сустав гнётся вдоль линии костяшек, разведённые пальцы при сгибе сводятся. Скрипт перебором подбирает
поворот этой общей оси в плоскости ладони так, чтобы кончик согнутого пальца (основание 85°, средний
сустав 95°, дальний 65° — как в тесте) ушёл поперёк кисти к большому пальцу на TARGET % длины пальца, и ставит каждой фаланге
крен, при котором её ось X идёт по этой общей оси. Без условия одной плоскости перебор попадал в цель, поворачивая средние
и дальние фаланги на 45° относительно основной: кончик на месте, а палец гнётся винтом. Сетка, веса и длины костей не меняются: в покое рука
та же, другим становится только путь пальца при сгибе. Кость-помощник сустава Joint* получает тот же крен, что и фаланга.

Без FLEX_INPLACE основной файл не трогается: копия сохраняется в FLEX_OUT (по умолчанию art3d/hands/test/CardCity_Hands_flex.blend).
FLEX_INPLACE=1 — сохранить сам открытый файл (так оси перенесены в основную модель 2026-11-15, после подтверждения автора).
Каждая кость запоминает, на сколько её довернули: свойство flex_roll (радианы). По нему poses.apply_pose ставит позы,
числа которых подобраны под прежние оси, без изменений: поворот пересчитывается в новые оси кости.
Запуск: blender art3d/hands/CardCity_Hands.blend --background --disable-autoexec --python scripts/3d/hands/flex_converge.py"""
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(__file__))
import source

OUT = os.environ.get("FLEX_OUT") or os.path.join(source.ROOT, "art3d", "hands", "test", "CardCity_Hands_flex.blend")
FINGERS = ("Index", "Middle", "Ring", "Pinky")
# Куда кончик согнутого пальца уходит поперёк кисти, % длины пальца (плюс — к большому пальцу): сходятся к большому,
# мизинец сильнее всех. FLEX_TARGET="2,4,6,8" — указательный, средний, безымянный, мизинец.
TARGET = dict(zip(FINGERS, (float(v) for v in os.environ.get("FLEX_TARGET", "2,4,6,8").split(","))))
BEND = (85, 95, 65)
STEP, LIMIT = 1.5, 45


def tip_offset(rest, lengths, rolls, across):
    """Кончик пальца в позе теста при заданных кренах фаланг: сдвиг поперёк кисти от основания пальца."""
    mats = [m @ Matrix.Rotation(r, 4, "Y") for m, r in zip(rest, rolls)]
    world = mats[0] @ Matrix.Rotation(-math.radians(BEND[0]), 4, "X")
    for i in (1, 2):
        world = world @ (mats[i - 1].inverted() @ mats[i]) @ Matrix.Rotation(-math.radians(BEND[i]), 4, "X")
    tip = world @ Vector((0, lengths[2], 0))
    return (tip - rest[0].translation).dot(across)


def main():
    armature = bpy.data.objects["Rig_Hands"]
    bones = armature.data.bones
    plan = {}
    for side in ("_L", "_R"):
        # Поперёк кисти, к большому пальцу: от основания мизинца к основанию указательного (как в candidate_check.py).
        across = (bones["Index01" + side].head_local - bones["Pinky01" + side].head_local).normalized()
        for finger in FINGERS:
            chain = [bones[finger + n + side] for n in ("01", "02", "03")]
            rest = [b.matrix_local.copy() for b in chain]
            lengths = [b.length for b in chain]
            length = sum(lengths)
            before = 100 * tip_offset(rest, lengths, (0, 0, 0), across) / length
            along = ((bones["Index01" + side].head_local + bones["Pinky01" + side].head_local) / 2 - bones["Hand" + side].head_local).normalized()
            normal = across.cross(along).normalized()
            base_axis = across if across.dot(rest[0].col[0].xyz) > 0 else -across
            best = None
            steps = int(LIMIT / STEP)
            for i in range(-steps, steps + 1):
                phi = math.radians(i * STEP)
                hinge = Matrix.Rotation(phi, 3, normal) @ base_axis
                rolls = []
                for m in rest:
                    x, y, z = (m.col[k].xyz.normalized() for k in range(3))
                    want = (hinge - y * hinge.dot(y)).normalized()
                    rolls.append(math.atan2(-want.dot(z), want.dot(x)))
                off = 100 * tip_offset(rest, lengths, rolls, across) / length
                score = abs(off - TARGET[finger]) + .01 * abs(i) * STEP
                if best is None or score < best[0]:
                    best = (score, rolls, off, phi)
            plan[finger + side] = best[1]
            print("=== FLEX %-8s увод кончика %+5.1f%% -> %+5.1f%% длины пальца, поворот оси от линии костяшек %+5.1f°, крен фаланг %s"
                  % (finger + side, before, best[2], math.degrees(best[3]), ", ".join("%+5.1f°" % math.degrees(r) for r in best[1])))
    bpy.context.view_layer.objects.active = armature
    for ob in bpy.context.selected_objects:
        ob.select_set(False)
    armature.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    edit = armature.data.edit_bones
    for key, rolls in plan.items():
        finger, side = key[:-2], key[-2:]
        for n, r in zip(("01", "02", "03"), rolls):
            for name in (finger + n + side, "Joint" + finger + n + side):
                bone = edit.get(name)
                if bone is not None:
                    # Крен в Blender — поворот вокруг оси кости; знак сверяется ниже по готовой матрице.
                    bone.roll += r
    bpy.ops.object.mode_set(mode="OBJECT")
    for key, rolls in plan.items():
        finger, side = key[:-2], key[-2:]
        for n, r in zip(("01", "02", "03"), rolls):
            for name in (finger + n + side, "Joint" + finger + n + side):
                bone = armature.data.bones.get(name)
                if bone is not None:
                    bone["flex_roll"] = bone.get("flex_roll", 0.) + r
    # Проверка знака: матрица кости после правки должна совпасть с расчётной (rest @ поворот вокруг Y).
    for key in plan:
        side = key[-2:]
        bone = armature.data.bones[key[:-2] + "01" + side]
        across = (bones["Index01" + side].head_local - bones["Pinky01" + side].head_local).normalized()
        chain = [armature.data.bones[key[:-2] + n + side] for n in ("01", "02", "03")]
        off = 100 * tip_offset([b.matrix_local.copy() for b in chain], [b.length for b in chain], (0, 0, 0), across) / sum(b.length for b in chain)
        print("=== CHECK %-8s увод кончика после правки %+5.1f%%" % (key, off))
    if os.environ.get("FLEX_INPLACE") == "1":
        bpy.ops.wm.save_mainfile()
        print("=== FLEX SAVED", bpy.data.filepath)
    else:
        os.makedirs(os.path.dirname(OUT), exist_ok=True)
        bpy.ops.wm.save_as_mainfile(filepath=OUT, copy=True)
        print("=== FLEX SAVED", OUT)


main()
