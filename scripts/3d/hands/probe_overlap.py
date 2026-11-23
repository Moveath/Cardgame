"""Пальцы друг в друге: проверка по настоящей сетке руки после скелета, а не по костям.
Каждая вершина относится к кости с наибольшим весом, грань — к фаланге, если все её вершины на одной фаланге.
Пересекающиеся треугольники ищутся между фалангами разных пальцев и между средними и дальними фалангами и ладонью.
Основные фаланги соседних пальцев между собой не сравниваются: у основания их сетка сшита перепонкой.
Печатает число пересечений в позе партии и в кадре разгара каждого клипа Take_*.
Запуск: blender art3d/hands/CardCity_Hands.blend --background --disable-autoexec --python scripts/3d/hands/probe_overlap.py"""
import os
import sys

import bpy
from mathutils.bvhtree import BVHTree

sys.path.insert(0, os.path.dirname(__file__))

FINGERS = ("Thumb", "Index", "Middle", "Ring", "Pinky")


def part_of(group):
    """Фаланга по имени кости: ('Index', 2), ладонь — ('Palm', 0), остальное — None."""
    name = group.rsplit("_", 1)[0]
    # Промежуточная кость сустава относится к фаланге за суставом.
    if name.startswith("Joint") and name != "JointHand":
        name = name[len("Joint"):]
    for finger in FINGERS:
        if name.startswith(finger) and name[len(finger):].isdigit():
            return finger, int(name[len(finger):])
        if name == "Tip" + finger:
            return finger, 3
    if name == "Hand" or name.startswith("Meta"):
        return "Palm", 0
    return None


def parts(ob, side):
    """Грани сетки по фалангам: {('Index', 2): [индексы граней]}."""
    groups = {g.index: part_of(g.name) for g in ob.vertex_groups if g.name.endswith(side)}
    owner = []
    for v in ob.data.vertices:
        best, weight = None, 0.
        for g in v.groups:
            if g.group in groups and g.weight > weight:
                best, weight = groups[g.group], g.weight
        owner.append(best)
    result = {}
    for poly in ob.data.polygons:
        owners = {owner[i] for i in poly.vertices}
        if len(owners) == 1 and None not in owners:
            result.setdefault(owners.pop(), []).append(poly.index)
    return result


def checked_pairs(keys):
    for a in keys:
        for b in keys:
            if a >= b:
                continue
            (fa, pa), (fb, pb) = a, b
            if fa == fb:
                continue
            if "Palm" in (fa, fb):
                finger, phalanx = (fb, pb) if fa == "Palm" else (fa, pa)
                if phalanx >= 2 or finger == "Thumb" and phalanx >= 3:
                    yield a, b
                continue
            if pa == 1 and pb == 1 and "Thumb" not in (fa, fb):
                continue
            yield a, b


class Probe:
    def __init__(self, armature_name="Rig_Hands"):
        self.armature = bpy.data.objects[armature_name]
        self.meshes = {side: bpy.data.objects["Arm_Male" + side] for side in ("_L", "_R")}
        self.parts = {side: parts(ob, side) for side, ob in self.meshes.items()}

    def overlaps(self, side):
        """Сколько пар треугольников разных пальцев пересекаются и где больше всего: {(часть, часть): число}."""
        depsgraph = bpy.context.evaluated_depsgraph_get()
        ob = self.meshes[side].evaluated_get(depsgraph)
        mesh = ob.to_mesh()
        verts = [v.co.copy() for v in mesh.vertices]
        polys = [tuple(p.vertices) for p in mesh.polygons]
        trees = {}
        for key, faces in self.parts[side].items():
            used = sorted({i for f in faces for i in polys[f]})
            remap = {old: new for new, old in enumerate(used)}
            trees[key] = BVHTree.FromPolygons([verts[i] for i in used], [tuple(remap[i] for i in polys[f]) for f in faces])
        ob.to_mesh_clear()
        found = {}
        for a, b in checked_pairs(sorted(trees)):
            hits = trees[a].overlap(trees[b])
            if hits:
                found[(a, b)] = len(hits)
        return found


def report(name, found):
    total = sum(found.values())
    worst = sorted(found.items(), key=lambda kv: -kv[1])[:4]
    text = ", ".join("%s%d-%s%d:%d" % (a[0], a[1], b[0], b[1], n) for (a, b), n in worst)
    print("=== %-22s %4d  %s" % (name, total, text))


def main():
    import animations
    import in_game
    probe = Probe()
    armature = probe.armature
    armature.matrix_world.identity()
    sc = bpy.context.scene
    armature.animation_data.action = bpy.data.actions[in_game.ACTION]
    sc.frame_set(1)
    for side in ("_L", "_R"):
        report("InGame" + side, probe.overlaps(side))
    for name, peak in animations.PEAKS.items():
        armature.animation_data.action = bpy.data.actions[animations.PREFIX + name]
        sc.frame_set(peak + 1)
        report(name + "_R", probe.overlaps("_R"))
        if name in ("Shrug", "HoldCards"):
            report(name + "_L", probe.overlaps("_L"))


if __name__ == "__main__":
    main()
