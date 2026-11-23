"""Проверка хвата веера: не проходит ли левая рука сквозь карты и касается ли их там, где держит.
Для шести карт, как в seat_view.py (другое число — FAN_CARDS), каждая кость пальца делится на точки; точка внутри карты по площади и ближе её толщины пальца
к плоскости карты — палец в карте. Печатает координаты суставов в осях веера (вправо, вверх, вглубь от играющего).
Запуск: blender art3d/hands/CardCity_Hands.blend --background --disable-autoexec --python scripts/3d/hands/probe_fan.py"""
import math
import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(__file__))
import in_game

# Сколько карт в веере: по умолчанию шесть, FAN_CARDS=12 — проверка широкого веера.
CARDS = int(os.environ.get("FAN_CARDS", "6"))
WIDTH = .088 * .714 * in_game.CARD_SCALE
HEIGHT = .088 * in_game.CARD_SCALE
# Толщина пальца у кости: кость идёт по середине пальца.
RADIUS = {"Thumb": .0085, "Index": .0075, "Middle": .0078, "Ring": .0072, "Pinky": .0064, "Meta": .012}
CHAINS = [("Thumb", ("Thumb01", "Thumb02", "Thumb03", "TipThumb"))] + [
    (f, (f + "01", f + "02", f + "03", "Tip" + f if f in ("Index", "Middle") else None)) for f in ("Index", "Middle", "Ring", "Pinky")]


def fan_coords(point):
    pivot, right, up, deep = in_game.fan_axes()
    v = point - pivot
    return v.dot(right), v.dot(up), v.dot(deep)


def cards():
    step = min(in_game.FAN_STEP, in_game.FAN_LIMIT / (CARDS - 1)) if CARDS > 1 else 0.
    for i in range(CARDS):
        yield math.radians((CARDS - 1) / 2 * step - i * step), in_game.card_depth(i, CARDS)


def inside(a, b, d, radius):
    """Насколько точка (a, b, d) в осях веера ушла в карту; 0 — не задевает."""
    worst = 0.
    for spin, depth in cards():
        x = a * math.cos(spin) + b * math.sin(spin)
        y = -a * math.sin(spin) + b * math.cos(spin)
        if abs(x) < WIDTH / 2 and abs(y - in_game.FAN_RADIUS) < HEIGHT / 2:
            worst = max(worst, radius - abs(d - depth))
    return worst


def main():
    armature = bpy.data.objects["Rig_Hands"]
    armature.matrix_world.identity()
    for bone in armature.pose.bones:
        bone.rotation_mode = "XYZ"
    in_game.set_arm(armature, in_game.game_arm(in_game.LEFT), in_game.FAN_HAND)
    bones = armature.pose.bones
    for finger, chain in CHAINS:
        names = [n for n in chain if n]
        joints = [bones[n + in_game.FAN_HAND].head for n in names] + ([] if chain[-1] else [bones[names[-1] + in_game.FAN_HAND].tail])
        worst, where = 0., ""
        for name, start, end in zip(names, joints, joints[1:]):
            for k in range(6):
                a, b, d = fan_coords(start.lerp(end, k / 5))
                depth = inside(a, b, d, RADIUS[finger])
                if depth > worst:
                    worst, where = depth, name
        shown = " ".join("(%.3f %.3f %.3f)" % fan_coords(j) for j in joints)
        print("=== %-6s %s  in cards %.4f %s" % (finger, shown, worst, where))
    for name in (n + in_game.FAN_HAND for n in ("Hand", "MetaIndex", "MetaMiddle", "MetaRing", "MetaPinky")):
        print("=== %-12s head (%.3f %.3f %.3f)" % ((name,) + fan_coords(bones[name].head)), "tail (%.3f %.3f %.3f)" % fan_coords(bones[name].tail))


if __name__ == "__main__":
    main()
