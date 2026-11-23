"""Листы для проверки: screens/3d-hands/review-*.png. Запуск:
blender art3d/hands/CardCity_Hands.blend --background --disable-autoexec --python scripts/3d/hands/sheets.py"""
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(__file__))
import poses
import review
import source

OUT = review.OUT
SIZE = (640, 520)
TONES = ("Light", "Medium", "Tan", "Dark")
POSE_ORDER = ("Relaxed", "HoldCard", "TakeCard", "PlayCard", "Point", "TapTable", "Fist")


def show(gender):
    for other, visible in (("Male", gender == "Male"), ("Female", gender == "Female")):
        bpy.data.collections["Hands_" + other].hide_render = not visible
        for side in "LR":
            bpy.data.objects["Arm_%s_%s" % (other, side)].hide_render = not visible


def wear(gender, tone):
    suffix = "" if gender == "Male" else "_Female"
    material = bpy.data.materials["M_Skin_%s%s" % (tone, suffix)]
    for side in "LR":
        mesh = bpy.data.objects["Arm_%s_%s" % (gender, side)].data
        mesh.materials.clear()
        mesh.materials.append(material)


def shoot(camera, sun, armature, suffix, offset, name, target=None):
    side = 1 if suffix == "_L" else -1
    bones = armature.pose.bones
    world = armature.matrix_world
    points = [world @ bones["Hand" + suffix].head, world @ bones["Middle03" + suffix].tail,
              world @ bones["Thumb03" + suffix].tail, world @ bones["Pinky03" + suffix].tail]
    centre = target if target is not None else sum(points, Vector()) / len(points)
    camera.location = centre + Vector((offset[0] * side, offset[1], offset[2]))
    direction = centre - camera.location
    camera.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    sun.rotation_euler = (direction + Vector((.2, .24, -.2))).to_track_quat("-Z", "Y").to_euler()
    path = os.path.join(OUT, "tmp-%s.png" % name)
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    return path


def sheet(paths, columns, name):
    images = [bpy.data.images.load(p) for p in paths]
    width, height = images[0].size
    rows = (len(images) + columns - 1) // columns
    canvas = bpy.data.images.new(name, width * columns, height * rows, alpha=False)
    import numpy as np
    pixels = np.ones((height * rows, width * columns, 4))
    for index, image in enumerate(images):
        data = np.array(image.pixels[:]).reshape(height, width, 4)
        row = rows - 1 - index // columns
        column = index % columns
        pixels[row * height:(row + 1) * height, column * width:(column + 1) * width] = data
    canvas.pixels = pixels.ravel()
    canvas.filepath_raw = os.path.join(OUT, name + ".png")
    canvas.file_format = "PNG"
    canvas.save()
    for path in paths:
        os.remove(path)
    print("SHEET", name, canvas.size[0], canvas.size[1])


def pose_all(armature, name):
    for suffix in ("_L", "_R"):
        poses.apply_pose(armature, poses.POSES[name], suffix)
    bpy.context.view_layer.update()


def main():
    os.makedirs(OUT, exist_ok=True)
    camera, sun = review.setup(*SIZE)
    armature = bpy.data.objects["Rig_Hands"]
    card = bpy.data.objects["Test_Card"]

    show("Male")
    wear("Male", "Medium")
    shots = []
    for name in POSE_ORDER:
        pose_all(armature, name)
        review.place_card(armature, poses.POSES[name], "_R")
        shots.append(shoot(camera, sun, armature, "_R", (.1, -.26, .22), name + "-a"))
        shots.append(shoot(camera, sun, armature, "_R", (.34, .02, .04), name + "-b"))
    sheet(shots, 4, "review-poses")

    # Сгибы крупно: кулак с тыла и с ладони, средний сустав сбоку.
    card.hide_render = True
    pose_all(armature, "Fist")
    camera.data.lens = 105
    world = armature.matrix_world
    knuckles = world @ armature.pose.bones["Index01_L"].head
    middle = world @ armature.pose.bones["Middle02_L"].head
    shots = [shoot(camera, sun, armature, "_L", (.06, .14, .3), "deform-a", knuckles),
             shoot(camera, sun, armature, "_L", (.2, .28, .0), "deform-b", middle),
             shoot(camera, sun, armature, "_L", (.3, .0, .02), "deform-c"),
             shoot(camera, sun, armature, "_L", (.06, .12, -.3), "deform-d")]
    pose_all(armature, "Point")
    shots.append(shoot(camera, sun, armature, "_L", (.3, .02, .02), "deform-e"))
    pose_all(armature, "HoldCard")
    shots.append(shoot(camera, sun, armature, "_L", (.1, -.22, .22), "deform-f"))
    camera.data.lens = 70
    sheet(shots, 3, "review-deform")

    shots = []
    for gender in ("Male", "Female"):
        show(gender)
        for tone in TONES:
            wear(gender, tone)
            pose_all(armature, "Relaxed")
            shots.append(shoot(camera, sun, armature, "_R", (.1, -.26, .22), "%s-%s" % (gender, tone)))
    sheet(shots, 4, "review-variants")

    show("Female")
    wear("Female", "Medium")
    shots = []
    for name in ("HoldCard", "Point", "TakeCard", "Fist"):
        pose_all(armature, name)
        review.place_card(armature, poses.POSES[name], "_R")
        shots.append(shoot(camera, sun, armature, "_R", (.34, .02, .04), "female-" + name))
    sheet(shots, 4, "review-female")
    print("=== SHEETS DONE")


main()
