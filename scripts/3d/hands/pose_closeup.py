"""Крупный план кисти в позе: правая рука в положении покоя скелета, три камеры вокруг кисти. Видно, не входят ли пальцы друг в друга.
Позы — из fit_poses.TARGETS или словарём в CLOSEUPS ниже. Кадры — screens/3d-hands/closeup-<поза>.png.
Запуск: SKIN=young blender art3d/hands/CardCity_Hands.blend --background --disable-autoexec --python scripts/3d/hands/pose_closeup.py -- [позы]"""
import math
import os
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(__file__))
import animations
import poses
import seat_view
import source

OUT = os.path.join(source.ROOT, "screens", "3d-hands")
CLOSEUPS = {
    "Fist": animations.hand(animations.TUCKED_FIST, animations.TUCKED_THUMB),
    "ThumbsUp": animations.hand(animations.FIST, animations.STRAIGHT_THUMB),
    "Point": poses.POSES["Point"],
    "TakeCard": poses.POSES["TakeCard"],
    "Relaxed": poses.POSES["Relaxed"],
}


def setup():
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.samples = 24
    sc.cycles.use_denoising = True
    sc.cycles.device = "CPU"
    sc.cycles.denoiser = "OPENIMAGEDENOISE"
    sc.render.resolution_x = sc.render.resolution_y = 600
    sc.view_settings.view_transform = "AgX"
    world = bpy.data.worlds.new("closeup")
    world.use_nodes = False
    world.color = (.18, .17, .16)
    sc.world = world
    key = bpy.data.objects.new("key", bpy.data.lights.new("key", "SUN"))
    key.data.energy = 3.
    key.rotation_euler = (math.radians(50), 0, math.radians(30))
    sc.collection.objects.link(key)
    for name in ("Hands_Female", "Sculpt"):
        bpy.data.collections[name].hide_render = True
    bpy.data.objects["Test_Card"].hide_render = True
    bpy.data.objects["Arm_Male_L"].hide_render = True
    bpy.data.objects["Sleeve_L"].hide_render = True
    seat_view.game_skin()
    camera = bpy.data.objects.new("closeup", bpy.data.cameras.new("closeup"))
    camera.data.lens = 60
    sc.collection.objects.link(camera)
    sc.camera = camera
    return sc, camera


def main():
    only = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else list(CLOSEUPS)
    sc, camera = setup()
    armature = bpy.data.objects["Rig_Hands"]
    armature.animation_data.action = None
    armature.matrix_world.identity()
    for name in only:
        for bone in armature.pose.bones:
            bone.rotation_mode = "XYZ"
            bone.rotation_euler = (0, 0, 0)
            bone.location = (0, 0, 0)
        poses.apply_pose(armature, CLOSEUPS[name], "_R")
        bpy.context.view_layer.update()
        hand = armature.pose.bones["Hand_R"]
        middle = armature.matrix_world @ armature.pose.bones["Middle02_R"].head
        center = (armature.matrix_world @ hand.head).lerp(middle, .7)
        axes = hand.matrix.to_3x3()
        along, back, side = axes @ Vector((0, 1, 0)), axes @ Vector((0, 0, 1)), axes @ Vector((1, 0, 0))
        views = {"back": back * .28 + along * .05, "side": side * .28 + back * .08, "palm": -back * .26 + along * .06 + side * .06}
        for view, offset in views.items():
            camera.location = center + offset
            camera.rotation_euler = (center - camera.location).to_track_quat("-Z", "Y").to_euler()
            sc.render.filepath = os.path.join(OUT, "closeup-%s-%s.png" % (name, view))
            bpy.ops.render.render(write_still=True)
        print("=== CLOSEUP", name)


if __name__ == "__main__":
    main()
