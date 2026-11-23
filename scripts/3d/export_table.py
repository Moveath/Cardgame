"""Выгружает стол и стул в Unity: Assets/Resources/ThreeD/table.fbx, chair.fbx и текстуру верха.
Запуск: blender --background --factory-startup --python scripts/3d/export_table.py"""
import os
import shutil
import sys

import bpy

sys.path.insert(0, os.path.dirname(__file__))
import table_model

OUT = os.path.join(table_model.ROOT, "unity", "CardGameClient", "Assets", "Resources", "ThreeD")


os.makedirs(OUT, exist_ok=True)
for ob in list(bpy.data.objects):
    bpy.data.objects.remove(ob)
table = table_model.build_table()
table_model.export_fbx([table] + list(table.children), os.path.join(OUT, "table.fbx"))
for ob in list(bpy.data.objects):
    bpy.data.objects.remove(ob)
table_model.export_fbx([table_model.build_chair()], os.path.join(OUT, "chair.fbx"))
shutil.copyfile(table_model.TOP_TEXTURE, os.path.join(OUT, "table-top.png"))
print("EXPORT DONE")
