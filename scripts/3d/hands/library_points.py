"""Готовые позы кисти из Godot XR Tools (CC0, art3d/hands/source/godot-xr-tools-hands) → точки суставов для переноса на руку игры.
В исходнике 35 действий по одному кадру на скелете левой кисти (запястье, пястные, три фаланги и кончик каждого пальца).
Для каждой позы пишутся 21 точка в порядке MediaPipe — тот же вход, что у видео живой руки (tools/mocap/retarget.py):
запястье, большой палец (основание, два сустава, кончик), затем указательный, средний, безымянный, мизинец
(основание, средний сустав, дальний сустав, кончик).

Запуск: blender art3d/hands/source/godot-xr-tools-hands/animations_hand_l.blend --background --factory-startup --disable-autoexec
        --python scripts/3d/hands/library_points.py"""
import json
import os

import bpy

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))
OUT = os.path.join(ROOT, "tools", "mocap", "library", "xr_tools.json")
POINTS = ["Wrist", "Thumb_Metacarpal", "Thumb_Proximal", "Thumb_Distal", "Thumb_Tip"] + [
    finger + "_" + joint for finger in ("Index", "Middle", "Ring", "Little") for joint in ("Proximal", "Intermediate", "Distal", "Tip")]


def main():
    armature = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    armature.animation_data_create()
    poses = {}
    for action in sorted(bpy.data.actions, key=lambda a: a.name):
        for bone in armature.pose.bones:
            bone.location, bone.rotation_quaternion, bone.rotation_euler, bone.scale = (0, 0, 0), (1, 0, 0, 0), (0, 0, 0), (1, 1, 1)
        armature.animation_data.action = action
        if getattr(action, "slots", None):
            armature.animation_data.action_slot = action.slots[0]
        bpy.context.scene.frame_set(int(action.frame_range[0]))
        bpy.context.view_layer.update()
        world = [[round(v, 5) for v in armature.pose.bones[name + "_L"].head] for name in POINTS]
        poses[action.name] = {"side": "Left", "world": world}
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump({"source": "Godot XR Tools hands, CC0 (DigitalN8m4r3 aka Miodrag Sejic)", "poses": poses}, f, ensure_ascii=False, indent=1)
    print("=== LIBRARY", len(poses), "poses ->", OUT)


if __name__ == "__main__":
    main()
