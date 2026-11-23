"""Подбор поз по сетке: пальцы не входят друг в друга и в ладонь, как у настоящей руки, а поза остаётся как можно ближе к задуманной.
Сгибы и развод пальцев (и повороты большого пальца, где он задан углами) двигаются понемногу, пока число пересекающихся
треугольников разных пальцев не станет нулём (probe_overlap.py). Для хвата веера пальцы ещё и не должны входить в карты (probe_fan.py).
Печатает найденные числа — их переносят в poses.py, animations.py и in_game.py.
Запуск: blender art3d/hands/CardCity_Hands.blend --background --disable-autoexec --python scripts/3d/hands/fit_poses.py -- [имена поз]"""
import copy
import math
import os
import sys

import bpy
from mathutils import Euler

sys.path.insert(0, os.path.dirname(__file__))
import animations
import in_game
import poses
import probe_fan
from probe_overlap import Probe

FINGERS = ("Index", "Middle", "Ring", "Pinky")
STEPS = (8, -8, 4, -4, 2, -2)
# Пределы живой руки, градусы: развод, основание, средний сустав, дальний сустав; большой палец — основание и сгибы.
# Основание большого пальца дальше ±45° рвёт сетку на тыле кисти: скиннинг не рассчитан на такой поворот.
LIMITS = {"fingers": ((-20, 20), (-15, 92), (-5, 108), (-5, 75)), "root": (-45, 45), "bend": (-15, 75)}
# Сколько стоит отход от задуманной позы: градус весит как 0,03 пересечения — сначала убрать пересечения, потом держать форму.
DRIFT = .03
PALM = .15


def right(pose):
    return lambda armature: poses.apply_pose(armature, pose, "_R")


def left(pose):
    def place(armature):
        arm = copy.deepcopy(in_game.LEFT)
        arm["pose"] = pose
        in_game.set_arm(armature, in_game.game_arm(arm), in_game.FAN_HAND)
    return place


def fan_penetration(armature):
    bones = armature.pose.bones
    worst = 0.
    for finger, chain in probe_fan.CHAINS:
        names = [n for n in chain if n]
        joints = [bones[n + in_game.FAN_HAND].head for n in names] + ([] if chain[-1] else [bones[names[-1] + in_game.FAN_HAND].tail])
        for start, end in zip(joints, joints[1:]):
            for k in range(6):
                worst = max(worst, probe_fan.inside(*probe_fan.fan_coords(start.lerp(end, k / 5)), probe_fan.RADIUS[finger]))
    return worst


# Кулак свободнее, чем был: в среднем суставе около 85°, пальцы чуть расходятся веером. Туже сетка пальцев раздувается в стороны.
LOOSE_FIST = {"Index": (8, 80, 84, 34), "Middle": (2, 82, 86, 36), "Ring": (-4, 84, 86, 36), "Pinky": (-10, 86, 84, 34)}

# Позы: как поставить, на какой руке смотреть, можно ли двигать большой палец.
TARGETS = {
    "Fist": (animations.hand(animations.TUCKED_FIST, ((-40, 40, -30), 40, 42)), right, "_R", True),
    "ThumbsUp": (animations.hand(animations.FIST, animations.STRAIGHT_THUMB), right, "_R", True),
    "Point": (copy.deepcopy(poses.POSES["Point"]), right, "_R", True),
    "TakeCard": (copy.deepcopy(poses.POSES["TakeCard"]), right, "_R", True),
    "Grip": (copy.deepcopy(in_game.LEFT["pose"]), left, in_game.FAN_HAND, False),
}


def parameters(pose, thumb):
    names = [("fingers", f, i) for f in FINGERS for i in range(4)]
    if thumb:
        names += [("thumb", 0, i) for i in range(3)] + [("thumb", 1, None), ("thumb", 2, None)]
    return names


def get(pose, key):
    kind, a, b = key
    if kind == "fingers":
        return pose["fingers"][a][b]
    value = pose["thumb"][a]
    return value[b] if b is not None else value


def put(pose, key, value):
    kind, a, b = key
    result = copy.deepcopy(pose)
    if kind == "fingers":
        row = list(result["fingers"][a])
        row[b] = value
        result["fingers"][a] = tuple(row)
    else:
        thumb = list(result["thumb"])
        if b is None:
            thumb[a] = value
        else:
            root = list(thumb[a])
            root[b] = value
            thumb[a] = tuple(root)
        result["thumb"] = tuple(thumb)
    return result


def allowed(key, value):
    kind, a, b = key
    if kind == "fingers":
        low, high = LIMITS["fingers"][b]
    else:
        low, high = LIMITS["root"] if b is not None else LIMITS["bend"]
    return low <= value <= high


def fit(name, probe, armature):
    start, make, side, thumb = TARGETS[name]
    keys = parameters(start, thumb)

    def score(pose):
        for bone in armature.pose.bones:
            bone.rotation_mode = "XYZ"
        make(pose)(armature)
        bpy.context.view_layer.update()
        found = probe.overlaps(side)
        # Палец в пальце видно снаружи — это главное; кончик в ладони прячется внутри кулака, как у живой руки, — он дешевле.
        hits = sum(n * (PALM if "Palm" in (a[0], b[0]) else 1.) for (a, b), n in found.items())
        drift = sum(abs(get(pose, k) - get(start, k)) for k in keys)
        cards = fan_penetration(armature) * 4000 if name == "Grip" else 0.
        return hits + cards + drift * DRIFT, hits, cards

    best = start
    best_score, hits, cards = score(best)
    print("=== %-9s start: %.0f overlaps, cards %.1f" % (name, hits, cards))
    for _ in range(10):
        improved = False
        for key in keys:
            for step in STEPS:
                if not allowed(key, get(best, key) + step):
                    continue
                trial = put(best, key, get(best, key) + step)
                value, h, c = score(trial)
                if value < best_score - 1e-6:
                    best, best_score, hits, cards, improved = trial, value, h, c, True
                    break
        if not improved:
            break
    print("=== %-9s fitted: %.0f overlaps (palm counts %.2f), cards %.1f" % (name, hits, PALM, cards))
    score(best)
    left_over = sorted(probe.overlaps(side).items(), key=lambda kv: -kv[1])[:6]
    print("=== %-9s left: %s" % (name, ", ".join("%s%d-%s%d:%d" % (a[0], a[1], b[0], b[1], n) for (a, b), n in left_over)))
    print("=== %-9s fingers %s" % (name, {f: best["fingers"][f] for f in FINGERS}))
    if thumb:
        print("=== %-9s thumb %s" % (name, best["thumb"]))
    return best


def main():
    probe = Probe()
    armature = probe.armature
    armature.matrix_world.identity()
    armature.animation_data.action = None
    only = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else list(TARGETS)
    for name in only:
        fit(name, probe, armature)


if __name__ == "__main__":
    main()
