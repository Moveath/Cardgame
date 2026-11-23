"""Руки из свободной базы Blender (Human Base Meshes, CC0): art3d/hands/source/human_base_meshes_bundle.blend.
Мужское и женское реалистичные тела совпадают вершина в вершину, поэтому варианты рук делят скелет и веса."""
import os

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
SOURCE = os.path.join(ROOT, "art3d", "hands", "source", "human_base_meshes_bundle.blend")
BODIES = {"Male": "GEO-body_male_realistic", "Female": "GEO-body_female_realistic"}

# Замеры левой руки мужского тела относительно начала тела: локоть и середина запястья.
SOURCE_ELBOW = Vector((.262, .0, 1.13))
SOURCE_WRIST = Vector((.377, -.065, .88))
ELBOW_MARGIN = .02            # Сколько оставить за локтем: край сетки уйдёт в рукав.
ARM_SIDE_X = .2               # Левее этой линии на теле уже не рука.
ARM_REACH, ARM_RADIUS = .52, .17   # Дальше кончиков пальцев и в стороне от оси руки лежат бёдра и ноги.
PLACE_X = .22                 # На таком расстоянии от середины стоят локти.
GAME_LEVEL, SCULPT_LEVEL = 1, 3


def load_body(name):
    with bpy.data.libraries.load(SOURCE, link=False) as (data_from, data_to):
        data_to.objects = [name]
    ob = data_to.objects[0]
    bpy.context.scene.collection.objects.link(ob)
    ob.matrix_world = Matrix.Identity(4)
    return ob


def evaluated_mesh(ob, level, name):
    modifier = ob.modifiers["Multires"]
    modifier.levels = level
    bpy.context.view_layer.update()
    mesh = bpy.data.meshes.new_from_object(ob.evaluated_get(bpy.context.evaluated_depsgraph_get()))
    mesh.name = name
    return mesh


def canonical_matrix(dorsal):
    """Левая рука перед игроком: предплечье вдоль +Y, ладонь вниз, большой палец к середине (+X). Локоть в (-PLACE_X, 0, 0)."""
    forward = (SOURCE_WRIST - SOURCE_ELBOW).normalized()
    up = (dorsal - forward * dorsal.dot(forward)).normalized()
    right = forward.cross(up)
    rotation = Matrix((right, forward, up))           # Строки: новые оси в старых координатах.
    return Matrix.Translation((-PLACE_X, 0, 0)) @ rotation.to_4x4() @ Matrix.Translation(-SOURCE_ELBOW)


def arm_faces(mesh, side):
    """Номера граней руки: всё по эту сторону плоскости у локтя."""
    elbow = np.array(SOURCE_ELBOW) * (side, 1, 1)
    forward = np.array((SOURCE_WRIST - SOURCE_ELBOW).normalized()) * (side, 1, 1)
    centres = np.zeros(len(mesh.polygons) * 3)
    mesh.polygons.foreach_get("center", centres)
    centres = centres.reshape(-1, 3)
    along = (centres - elbow) @ forward
    aside = np.linalg.norm(centres - elbow - np.outer(along, forward), axis=1)
    keep = (along > -ELBOW_MARGIN) & (along < ARM_REACH) & (aside < ARM_RADIUS) & (centres[:, 0] * side > ARM_SIDE_X)
    return np.nonzero(keep)[0]


def cut(mesh, faces):
    """Оставляет только перечисленные грани, порядок оставшихся вершин одинаков у всех вариантов."""
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bm.faces.ensure_lookup_table()
    keep = set(int(f) for f in faces)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.index not in keep], context="FACES")
    # Отвязанные вершины не деформируются и сдвинули бы нумерацию, а с ней и веса.
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()


def transform(mesh, matrix):
    mesh.transform(matrix)
    mesh.update()
