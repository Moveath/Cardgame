"""Развёртка руки для переноса фотографии кожи: маска кусков развёртки и точки-ориентиры на ней.
Куски развёртки (острова) нумеруются по связности UV; для каждого острова кисти определяется, тыл это или ладонь.
Ориентиры — кончики пальцев, костяшки, перепонки и края запястья — берутся у вершин сетки рядом с костями, на нужной стороне.
Пишет art3d/hands/textures/uv_landmarks.json: ориентиры и многоугольники развёртки с номерами островов.
Запуск: blender art3d/hands/CardCity_Hands.blend --background --disable-autoexec --python scripts/3d/hands/uv_landmarks.py"""
import json
import os
import sys

import bmesh
import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(__file__))
import source

OUT = os.path.join(source.ROOT, "art3d", "hands", "textures")
SIZE = (2048, 1024)


def islands(bm, uv):
    """Номер острова для каждой грани: соседние грани в одном острове, если у общего ребра одинаковые UV с обеих сторон."""
    parent = list(range(len(bm.faces)))

    def root(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    for edge in bm.edges:
        faces = edge.link_faces
        if len(faces) != 2:
            continue
        a, b = faces
        same = True
        for vert in edge.verts:
            ua = next(l[uv].uv for l in a.loops if l.vert == vert)
            ub = next(l[uv].uv for l in b.loops if l.vert == vert)
            if (ua - ub).length > 1e-5:
                same = False
        if same:
            parent[root(a.index)] = root(b.index)
    return [root(f.index) for f in bm.faces]


def main():
    ob = bpy.data.objects["Arm_Male_L"]
    armature = bpy.data.objects["Rig_Hands"]
    bones = armature.data.bones
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bm.faces.ensure_lookup_table()
    uv = bm.loops.layers.uv.active
    owner = islands(bm, uv)
    ids = sorted(set(owner))
    numbered = {old: n + 1 for n, old in enumerate(ids)}

    hand = bones["Hand_L"]
    back = (hand.matrix_local.to_3x3() @ Vector((0, 0, 1))).normalized()  # Z кости кисти — к тылу.
    along = (hand.matrix_local.to_3x3() @ Vector((0, 1, 0))).normalized()
    radial = (hand.matrix_local.to_3x3() @ Vector((1, 0, 0))).normalized()  # X — к большому пальцу.
    wrist = hand.head_local

    # Сторона и площадь каждого острова по граням кисти (дальше запястья вдоль кисти).
    info = {}
    for face in bm.faces:
        center = face.calc_center_median()
        if (center - wrist).dot(along) < .01:
            continue
        n = numbered[owner[face.index]]
        entry = info.setdefault(n, {"back": 0., "area": 0.})
        entry["back"] += face.normal.dot(back) * face.calc_area()
        entry["area"] += face.calc_area()
    hand_islands = sorted(info, key=lambda n: -info[n]["area"])[:2]
    side = {n: ("back" if info[n]["back"] > 0 else "palm") for n in hand_islands}

    # Многоугольники развёртки с номером острова: маску рисует skin_photo.py (в Python Blender нет PIL).
    faces = [[numbered[owner[face.index]], [[l[uv].uv.x * SIZE[0], (1 - l[uv].uv.y) * SIZE[1]] for l in face.loops]] for face in bm.faces]

    def uv_near(point, island, toward=None):
        """UV вершины острова, ближайшей к точке; toward — предпочесть вершины, выдвинутые в этом направлении."""
        best, best_value = None, None
        for face in bm.faces:
            if numbered[owner[face.index]] != island:
                continue
            for loop in face.loops:
                co = loop.vert.co
                value = (co - point).length - (co - point).dot(toward) * .5 if toward is not None else (co - point).length
                if best_value is None or value < best_value:
                    best, best_value = loop[uv].uv, value
        return [best.x * SIZE[0], (1 - best.y) * SIZE[1]]

    marks = {}
    for n in hand_islands:
        facing = back if side[n] == "back" else -back
        points = {}
        for finger in ("Thumb", "Index", "Middle", "Ring", "Pinky"):
            last = bones[finger + "03_L"]
            tip = last.tail_local
            points["tip_" + finger] = uv_near(tip + facing * .004, n, (last.tail_local - last.head_local).normalized())
            if finger != "Thumb":
                points["knuckle_" + finger] = uv_near(bones[finger + "01_L"].head_local + facing * .012, n)
        for a, b in (("Thumb", "Index"), ("Index", "Middle"), ("Middle", "Ring"), ("Ring", "Pinky")):
            if a == "Thumb":
                web = bones["Thumb02_L"].head_local.lerp(bones["Index01_L"].head_local, .5)
            else:
                ba, bb = bones[a + "01_L"], bones[b + "01_L"]
                web = (ba.head_local.lerp(ba.tail_local, .4) + bb.head_local.lerp(bb.tail_local, .4)) / 2
            points["web_" + a + "_" + b] = uv_near(web + facing * .006, n)
        points["wrist_radial"] = uv_near(wrist + radial * .03 + facing * .01, n)
        points["wrist_ulnar"] = uv_near(wrist - radial * .03 + facing * .01, n)
        marks[side[n]] = {"island": n, "points": points}
    with open(os.path.join(OUT, "uv_landmarks.json"), "w", encoding="utf-8") as f:
        json.dump({"marks": marks, "faces": faces, "size": SIZE}, f)
    print("=== ISLANDS", len(ids), "hand", {n: side[n] for n in hand_islands}, {n: round(info[n]["area"], 5) for n in hand_islands})
    print("=== MARKS", json.dumps(marks))


main()
