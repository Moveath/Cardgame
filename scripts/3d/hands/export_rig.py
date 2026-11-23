"""Скелет рук в покое для переноса движений с видео (tools/mocap/retarget.py): матрицы костей кисти обеих рук.
Пишет tools/mocap/rig_rest.json. Запускать после каждого изменения скелета (fix_hinges.py, build.py).
Запуск: blender art3d/hands/CardCity_Hands.blend --background --disable-autoexec --python scripts/3d/hands/export_rig.py"""
import json
import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(__file__))
import source

OUT = os.path.join(source.ROOT, "tools", "mocap", "rig_rest.json")
KEEP = ("Hand", "Meta", "Thumb", "Index", "Middle", "Ring", "Pinky")

armature = bpy.data.objects["Rig_Hands"]
bones = {}
for bone in armature.data.bones:
    base = bone.name.rsplit("_", 1)[0]
    if not base.startswith(KEEP) or base.startswith("Joint"):
        continue
    bones[bone.name] = {"parent": bone.parent.name if bone.parent else None,
                        "matrix": [list(row) for row in bone.matrix_local],
                        "head": list(bone.head_local), "tail": list(bone.tail_local)}
with open(OUT, "w", encoding="utf-8") as f:
    json.dump({"bones": bones}, f, indent=1)
print("=== RIG", OUT, len(bones), "bones")
