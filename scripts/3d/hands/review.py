"""Кадры для проверки поз: screens/3d-hands. Запуск: blender art3d/hands/CardCity_Hands.blend --background --python scripts/3d/hands/review.py [-- имя позы ...]"""
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(__file__))
import poses
import source

OUT = os.path.join(source.ROOT, "screens", "3d-hands")
# Откуда смотрит камера относительно кисти: глазами игрока сверху и сбоку, ладонью к нам.
CARD_HALF_HEIGHT, CARD_GRIP = .044, .012       # Карта зажата на 12 мм от края.
SIDES = ("_L",) if "left-only" in sys.argv else ("_L", "_R")
VIEWS = (("player", (.1, -.26, .22)), ("side", (.34, .02, .04)), ("palm", (.06, .05, -.34)))


def setup(width, height):
    sc = bpy.context.scene
    prefs = bpy.context.preferences.addons["cycles"].preferences
    prefs.compute_device_type = "OPTIX"
    prefs.get_devices()
    for device in prefs.devices:
        device.use = device.type == "OPTIX"
    sc.render.engine = "CYCLES"
    sc.cycles.device = "GPU"
    sc.cycles.samples = 20
    sc.cycles.use_denoising = False
    sc.render.resolution_x, sc.render.resolution_y = width, height
    sc.view_settings.view_transform = "Standard"
    world = bpy.data.worlds.new("review")
    world.use_nodes = False
    world.color = (.62, .58, .55)
    sc.world = world
    sun = bpy.data.objects.new("Review_Sun", bpy.data.lights.new("Review_Sun", "SUN"))
    sun.data.energy = 1.9
    sun.data.angle = .7
    sc.collection.objects.link(sun)
    camera = bpy.data.objects.new("Review_Camera", bpy.data.cameras.new("Review_Camera"))
    camera.data.lens = 70
    sc.collection.objects.link(camera)
    sc.camera = camera
    return camera, sun


def place_card(armature, pose, suffix):
    """Проверочная карта между большим и указательным пальцами. В кадре она только на позах с картой."""
    card = bpy.data.objects["Test_Card"]
    card.hide_render = not pose.get("prop")
    if card.hide_render:
        return
    bones = armature.pose.bones
    world = armature.matrix_world
    thumb = world @ bones["Thumb03" + suffix].tail
    fingers = world @ bones["Index03" + suffix].tail
    palm = world @ bones["SocketCard" + suffix].head
    normal = (thumb - fingers).normalized()
    pinch = (thumb + fingers) / 2
    outward = (pinch - palm)
    outward = (outward - normal * outward.dot(normal)).normalized()
    across = outward.cross(normal)
    centre = pinch + outward * (CARD_HALF_HEIGHT - CARD_GRIP)
    card.matrix_world = Matrix((across, outward, normal)).transposed().to_4x4() @ Matrix.Diagonal((.063, .088, .0004, 1))
    card.matrix_world.translation = centre


def main():
    wanted = [a for a in sys.argv[sys.argv.index("--") + 1:] if a in poses.POSES] if "--" in sys.argv else list(poses.POSES)
    os.makedirs(OUT, exist_ok=True)
    camera, sun = setup(760, 620)
    armature = bpy.data.objects["Rig_Hands"]
    for pose_name in wanted:
        for suffix in ("_L", "_R"):
            poses.apply_pose(armature, poses.POSES[pose_name], suffix)
        bpy.context.view_layer.update()
        for suffix in SIDES:
            side = 1 if suffix == "_L" else -1
            bones = armature.pose.bones
            points = [armature.matrix_world @ bones["Hand" + suffix].head, armature.matrix_world @ bones["Middle03" + suffix].tail,
                      armature.matrix_world @ bones["Thumb03" + suffix].tail, armature.matrix_world @ bones["Pinky03" + suffix].tail]
            centre = sum(points, Vector()) / len(points)
            place_card(armature, poses.POSES[pose_name], suffix)
            for view, offset in VIEWS:
                camera.location = centre + Vector((offset[0] * side, offset[1], offset[2]))
                direction = centre - camera.location
                camera.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
                sun.rotation_euler = (direction + Vector((.2, .24, -.2))).to_track_quat("-Z", "Y").to_euler()
                bpy.context.scene.render.filepath = os.path.join(OUT, "%s-%s-%s.png" % (pose_name, suffix[1], view))
                bpy.ops.render.render(write_still=True)
    print("=== REVIEW DONE")


if __name__ == "__main__":
    main()
