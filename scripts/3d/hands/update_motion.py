"""Пересобирает в готовом CardCity_Hands.blend позу партии и анимации из in_game.py и animations.py и сохраняет файл.
Сетки, веса и текстуры не трогает: после правки поз не нужно заново запекать карты.
Запуск: blender art3d/hands/CardCity_Hands.blend --background --disable-autoexec --python scripts/3d/hands/update_motion.py"""
import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(__file__))
import animations
import game_test
import in_game
import poses

armature = bpy.data.objects["Rig_Hands"]
armature.animation_data_create()
old = bpy.data.actions.get(in_game.ACTION)
if old is not None:
    bpy.data.actions.remove(old)
# Ранние анимации назывались Anim_*: они шли раньше Pose_Base и ломали положение покоя в Unity.
for action in [a for a in bpy.data.actions if a.name.startswith("Anim_")]:
    bpy.data.actions.remove(action)
# Позы Pose_* (щипок за карту в Unity берётся из Pose_TakeCard) пересобираются из poses.py.
for action in [a for a in bpy.data.actions if a.name.startswith("Pose_") and a.name != in_game.ACTION]:
    bpy.data.actions.remove(action)
poses.store_actions(armature)
# Точка привязки веера (кость SocketFan_L) пересчитывается под позу партии: веер в Unity крепится к ней.
in_game.place_fan_socket(armature)
in_game.store(armature)
animations.store(armature)
# Временная проверка Idle и кулака в игре (клавиша F): Take_TestClench из чисел gestures.py (game_test.py).
game_test.bake(armature)
bpy.ops.wm.save_mainfile()
print("=== MOTION", sorted(a.name for a in bpy.data.actions))
# Суставы, которые позы просили согнуть дальше пределов живой руки (poses.limited их удержал): числа стоит поправить.
print("=== LIMITS", poses.CLAMPED or "все позы в пределах живой руки")
