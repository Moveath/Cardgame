"""Стол и стул отдельно, студийный свет: проверка формы против листа генерации. Кадр — screens/3d-blockout/furniture.png."""
import math
import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(__file__))
import table_model

for ob in list(bpy.data.objects):
    bpy.data.objects.remove(ob)
sc = bpy.context.scene
prefs = bpy.context.preferences.addons["cycles"].preferences
prefs.compute_device_type = "OPTIX"
prefs.get_devices()
for device in prefs.devices:
    device.use = device.type == "OPTIX"
sc.render.engine = "CYCLES"
sc.cycles.device = "GPU"
sc.cycles.samples = 64
sc.cycles.use_denoising = True
sc.render.resolution_x, sc.render.resolution_y = 1600, 900
sc.view_settings.view_transform = "Standard"
sc.world = bpy.data.worlds.new("world")
sc.world.use_nodes = True
sc.world.node_tree.nodes["Background"].inputs[0].default_value = (.32, .31, .3, 1)
table_model.build_table()
chair = table_model.build_chair()
chair.location = (1.5, -.5, 0)
chair.rotation_euler = (0, 0, math.radians(35))
behind = table_model.build_chair("Chair back view")
behind.location = (2.25, -.4, 0)
behind.rotation_euler = (0, 0, math.radians(200))
bpy.ops.mesh.primitive_plane_add(size=20)
bpy.context.object.data.materials.append(table_model.material("floor", (.34, .33, .32), .8))
light = bpy.data.lights.new("key", "SUN")
light.energy, light.angle = 3, math.radians(20)
ob = bpy.data.objects.new("key", light)
ob.rotation_euler = (math.radians(50), 0, math.radians(30))
sc.collection.objects.link(ob)
data = bpy.data.cameras.new("c")
data.lens = 42
cam = bpy.data.objects.new("c", data)
cam.location = (1.05, -3.7, 1.75)
cam.rotation_euler = (math.radians(70), 0, 0)
sc.collection.objects.link(cam)
sc.camera = cam
sc.render.filepath = os.path.join(table_model.ROOT, "screens", "3d-blockout", "furniture.png")
bpy.ops.render.render(write_still=True)
print("FURNITURE DONE")
