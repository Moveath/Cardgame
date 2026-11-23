"""Доводка позы кисти по коже: пальцы лежат вплотную и не входят друг в друга, подушечки упираются в ладонь, большой
палец лежит на пальцах. Числа позы двигаются понемногу (по одному, малыми шагами), пока счёт не перестанет падать.
Счёт — миллиметры: вход кожи в кожу стоит дорого, зазор дешевле, отход от исходных чисел совсем дёшево, чтобы поза
оставалась той, что задумана. Печатает найденную позу — её переносят в gestures.py. Файл .blend не сохраняет.

Запуск: blender art3d/hands/CardCity_Hands.blend --background --disable-autoexec --python scripts/3d/hands/gesture_fit.py -- <поза> [что двигать]
Что двигать: fingers (наклон и курс пальцев), tips (средний и дальний суставы — упор подушечек), thumb (точки большого
пальца); по умолчанию всё. Переменные: LAB_JSON — как у gesture_lab.py; FIT_ROUNDS — число проходов (по умолчанию 3).
FIT_CLEAR=<мм> — режим промежуточной позы («Bend:0.3»): пальцам не нужно касаться друг друга и ладони, зато между
соседними пальцами должен оставаться просвет не меньше заданного."""
import copy
import os
import sys
import time

import bpy

sys.path.insert(0, os.path.dirname(__file__))
import gesture_lab
import hand_anatomy
from gesture_lab import FOUR, Skin

SIDE = "_R"
CLEAR = float(os.environ.get("FIT_CLEAR", "0"))
PAIRS = (("Index", "Middle"), ("Middle", "Ring"), ("Ring", "Pinky"))
INDEX = {"mcp": 0, "pip": 1, "dip": 2, "heading": 3, "tilt": 4}


def score(armature, skin, pose, start, keys):
    gesture_lab.reset(armature)
    hand_anatomy.apply(armature, pose, SIDE)
    bpy.context.view_layer.update()
    skin.shot()
    enter = gap = 0.
    for a, b in PAIRS:
        for x, y in ((a, b), (b, a)):
            enter += max(skin.inside((x, p), y)[0] for p in (2, 3))
        g = skin.gap({(a, 2), (a, 3)} if CLEAR else {(a, 2)}, (b,))
        g = g if g is not None else 20.
        if CLEAR:
            enter += max(0., CLEAR - g)
        else:
            gap += max(0., g - .3)
    tips_in = tips_gap = 0.
    for a in FOUR:
        tips_in += max(0., max(skin.inside((a, 3), body)[0] for body in ("Palm", a + ":base")) - .5)
        g = skin.gap({(a, 3)}, ("Palm", a + ":base"))
        tips_gap += max(0., (g if g is not None else 20.) - .5)
    thumb_in = max(skin.inside(("Thumb", p), b)[0] for p in (2, 3) for b in FOUR + ("Palm",))
    thumb_in += max(skin.inside((b, p), "Thumb")[0] for p in (1, 2, 3) for b in FOUR)
    g = skin.gap({("Thumb", 3)}, FOUR)
    thumb_gap = max(0., (g if g is not None else 20.) - .3)
    drift = sum(abs(get(pose, k) - get(start, k)) for k in keys)
    if CLEAR:
        tips_gap = thumb_gap = 0.
    # Основание большого пальца не отводится от покоя дальше предела: за ним кожа на тыле кисти рвётся.
    swing = max(0., hand_anatomy.thumb_report(armature, SIDE)["swing"] - hand_anatomy.THUMB_SWING)
    total = 4 * enter + gap + 3 * tips_in + tips_gap + 3 * max(0., thumb_in - .6) + 2 * thumb_gap + 5 * swing + .03 * drift
    return total, dict(enter=enter, gap=gap, tips_in=tips_in, tips_gap=tips_gap, thumb_in=thumb_in, thumb_gap=thumb_gap, swing=swing)


def get(pose, key):
    if key[0] in ("thumb", "thumb_bend"):
        return pose[key[0]][key[1]]
    if key[0] == "thumb_turn":
        return pose["thumb_turn"]
    return pose["fingers"][key[0]][INDEX[key[1]]]


def put(pose, key, value):
    result = copy.deepcopy(pose)
    if key[0] in ("thumb", "thumb_bend"):
        values = list(result[key[0]])
        values[key[1]] = value
        result[key[0]] = tuple(values)
    elif key[0] == "thumb_turn":
        result["thumb_turn"] = value
    else:
        row = list(result["fingers"][key[0]])
        row[INDEX[key[1]]] = value
        result["fingers"][key[0]] = tuple(row)
    return result


def main():
    args = sys.argv[sys.argv.index("--") + 1:]
    name, what = args[0], args[1:] or ["fingers", "tips", "thumb"]
    armature = bpy.data.objects["Rig_Hands"]
    armature.animation_data_create()
    armature.animation_data.action = None
    armature.matrix_world.identity()
    skin = Skin(armature, SIDE)
    start = gesture_lab.find_pose(name)[1]
    keys = []
    if "fingers" in what:
        keys += [(f, k) for f in FOUR for k in ("tilt", "heading")]
    if "tips" in what:
        keys += [(f, k) for f in FOUR for k in ("dip", "pip")]
    if "thumb" in what:
        keys += [("thumb", j) for j in range(3)] + [("thumb_bend", j) for j in range(3)] + [("thumb_turn",)]
    began = time.time()
    best = start
    best_score, parts = score(armature, skin, best, start, keys)
    print("=== FIT %s start %.2f %s (%.1f с на замер)" % (name, best_score, {k: round(v, 2) for k, v in parts.items()}, time.time() - began))
    for round_number in range(int(os.environ.get("FIT_ROUNDS", "3"))):
        improved = False
        for key in keys:
            steps = (2, -2, 1, -1) if key[0] in ("thumb",) else (2, -2, 1, -1, .5, -.5)
            for step in steps:
                trial = put(best, key, get(best, key) + step)
                limited = hand_anatomy.limited(trial)
                if any(limited[k] != trial[k] for k in ("fingers", "thumb_turn", "thumb_bend")):
                    continue
                value, p = score(armature, skin, trial, start, keys)
                if value < best_score - 1e-4:
                    best, best_score, parts, improved = trial, value, p, True
                    break
        print("=== FIT %s round %d: %.2f %s (%.0f с)" % (name, round_number + 1, best_score, {k: round(v, 2) for k, v in parts.items()}, time.time() - began))
        if not improved:
            break
    print("=== FIT %s fingers %s" % (name, best["fingers"]))
    print("=== FIT %s thumb %s turn %s bend %s cup %s" % (name, best["thumb"], best["thumb_turn"], best["thumb_bend"], best.get("cup", 0)))
    hand_anatomy.OVER_SWING.clear()
    score(armature, skin, best, start, keys)
    print("=== FIT %s большой палец отведён дальше предела: %s" % (name, hand_anatomy.OVER_SWING or "нет"))


if __name__ == "__main__":
    main()
