"""Собственные позы и анимации кандидата: кадры его действия (клипа) одной камерой, в лист.
Так видно, на что способен риг в руках автора модели, а не только в общем тесте candidate_check.py.

Запуск: blender <файл.blend> --background --disable-autoexec --python scripts/3d/hands/candidate_frames.py -- <имя> <скелет> <сетка> <действие> <кадры через запятую> [вид]
Для .fbx первым аргументом после -- передаётся путь к файлу: blender --background --python ... -- <имя> ... и FRAMES_FBX=<путь>.
Вид: front (по умолчанию) — с тыла кисти, palm — с ладони, side — сбоку. Лист — screens/3d-hands/candidates/<имя>-позы.jpg."""
import os
import re
import sys

import bpy
from mathutils import Matrix, Vector

ARGS = sys.argv[sys.argv.index("--") + 1:]
NAME, ARMATURE, MESH, ACTION, FRAMES = ARGS[:5]
VIEW = ARGS[5] if len(ARGS) > 5 else "front"
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
OUT = os.path.join(ROOT, "screens", "3d-hands", "candidates", NAME + "-позы")

if os.environ.get("FRAMES_FBX"):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=os.environ["FRAMES_FBX"])

sc = bpy.context.scene
armature = bpy.data.objects[ARMATURE]
mesh = bpy.data.objects[MESH]
armature.animation_data_create()
armature.animation_data.action = bpy.data.actions[ACTION]
for ob in sc.objects:
    if ob.type in ("MESH", "CURVE", "FONT", "SURFACE"):
        ob.hide_render = ob != mesh
sc.render.engine = "BLENDER_WORKBENCH"
sc.display.shading.light = "STUDIO"
sc.display.shading.color_type = "MATERIAL"
sc.display.shading.show_cavity = True
sc.render.resolution_x, sc.render.resolution_y = 420, 420
sc.render.film_transparent = False
cam = bpy.data.objects.new("frames cam", bpy.data.cameras.new("frames cam"))
cam.data.type = "ORTHO"
sc.collection.objects.link(cam)
sc.camera = cam
os.makedirs(OUT, exist_ok=True)
frames = [int(f) for f in FRAMES.split(",")]
paths = []
for f in frames:
    sc.frame_set(f)
    bpy.context.view_layer.update()
    smooth = [m for m in mesh.modifiers if m.type == "SUBSURF" and m.show_viewport]
    for m in smooth:
        m.show_viewport = False
    ev = mesh.evaluated_get(bpy.context.evaluated_depsgraph_get())
    data = ev.to_mesh()
    pts = [mesh.matrix_world @ v.co for v in data.vertices]
    ev.to_mesh_clear()
    for m in smooth:
        m.show_viewport = True
    focus = os.environ.get("FRAMES_FOCUS")
    if focus and len(pts) == len(mesh.data.vertices):
        # Только кисть: вершины, у которых главная кость подходит под FRAMES_FOCUS (в сетке может быть вся рука до плеча).
        pattern = re.compile(focus)
        names = {g.index: g.name for g in mesh.vertex_groups}
        keep = []
        for v, p in zip(mesh.data.vertices, pts):
            g = max(v.groups, key=lambda g: g.weight, default=None)
            if g and pattern.search(names[g.group]):
                keep.append(p)
        pts = keep or pts
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    center, size = (lo + hi) / 2, (hi - lo).length
    # Кисть, а не всё предплечье: центр сдвигается к пальцам (дальше всего от локтя по оси кисти).
    hand = armature.pose.bones
    direction = {"front": Vector((0, -1, .35)), "palm": Vector((0, 1, .35)), "side": Vector((1, 0, .2))}[VIEW]
    cam.data.ortho_scale = size * .9
    cam.data.clip_end = size * 20
    eye = center + direction.normalized() * size * 4
    forward = (center - eye).normalized()
    right = forward.cross(Vector((0, 0, 1))).normalized()
    up = right.cross(forward)
    cam.matrix_world = Matrix((right.to_4d(), up.to_4d(), (-forward).to_4d(), Vector((0, 0, 0, 1)))).transposed()
    cam.matrix_world.translation = eye
    path = os.path.join(OUT, "%03d.png" % f)
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    paths.append((f, path))
print("=== FRAMES", NAME, len(paths), OUT)
