"""Читает выгруженный FBX заново, в пустой сцене: проверяет, что доехали кости, веса, клипы и что поза действительно сгибает руку.
Запуск: blender --background --factory-startup --disable-autoexec --python scripts/3d/hands/verify_fbx.py"""
import os
import sys

import bpy
import numpy as np

sys.path.insert(0, os.path.dirname(__file__))
import source

PATH = os.path.join(source.ROOT, "art3d", "hands", "CardCity_Hands.fbx")
for ob in list(bpy.data.objects):
    bpy.data.objects.remove(ob)
bpy.ops.import_scene.fbx(filepath=PATH, use_prepost_rot=True)
meshes = [o for o in bpy.data.objects if o.type == "MESH"]
armatures = [o for o in bpy.data.objects if o.type == "ARMATURE"]
print("OBJECTS", sorted(o.name for o in bpy.data.objects))
print("ARMATURES", len(armatures), "BONES", len(armatures[0].data.bones))
print("BONE NAMES", sorted(b.name for b in armatures[0].data.bones)[:12])
print("ACTIONS", sorted(a.name for a in bpy.data.actions))
for ob in sorted(meshes, key=lambda o: o.name):
    me = ob.data
    weights = np.zeros(len(me.vertices))
    counts = np.zeros(len(me.vertices), int)
    for v in me.vertices:
        weights[v.index] = sum(g.weight for g in v.groups)
        counts[v.index] = len(v.groups)
    sizes = {}
    for poly in me.polygons:
        sizes[poly.loop_total] = sizes.get(poly.loop_total, 0) + 1
    print("MESH", ob.name, "v", len(me.vertices), "polys", sizes, "groups", len(ob.vertex_groups), "weight sum", round(float(weights.min()), 3), round(float(weights.max()), 3),
          "max influences", int(counts.max()), "uv", [u.name for u in me.uv_layers], "mats", [m.name for m in me.materials],
          "size", tuple(round(d, 3) for d in ob.dimensions))
print("=== VERIFY DONE")
