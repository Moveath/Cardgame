"""Собирает руки: art3d/hands/CardCity_Hands.blend. Запуск:
blender --background --factory-startup --disable-autoexec --python scripts/3d/hands/build.py

Коллекции файла: Hands_Male, Hands_Female — рабочие сетки на одном скелете, Clothes — рукава свитера, Rig — скелет,
Sculpt — исходная детализация для запекания, Test_Props — проверочная карта, в игру не идёт."""
import os
import sys

import bpy
import numpy as np
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(__file__))
import animations
import in_game
import joints as J
import poses
import rig
import sleeves
import source

OUT = os.path.join(source.ROOT, "art3d", "hands", "CardCity_Hands.blend")
CARD_SIZE = (.063, .088, .0004)
ORIGIN = "origin_vertex"        # Номер вершины в целом теле: по нему варианты рук находят свои точки.


def collection(name):
    made = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(made)
    return made


def move_to(ob, group):
    for owner in list(ob.users_collection):
        owner.objects.unlink(ob)
    group.objects.link(ob)


def male_frame(body):
    """Куда развернуть левую руку: тыл кисти считается по большому пальцу и остальным пальцам."""
    mesh = source.evaluated_mesh(body, source.GAME_LEVEL, "frame")
    source.cut(mesh, source.arm_faces(mesh, 1))
    co, _, verts = J.read_mesh(mesh)
    wrist = np.array(source.SOURCE_WRIST)
    fingers = np.mean([co[list(verts[J.FINGER_SETS[n][2]])].mean(axis=0) for n in J.FINGERS], axis=0)
    thumb = co[list(verts[J.THUMB_SETS[2]])].mean(axis=0)
    dorsal = Vector(np.cross(thumb - wrist, fingers - wrist)).normalized()
    bpy.data.meshes.remove(mesh)
    return source.canonical_matrix(dorsal)


def same_mesh(first, second):
    """Веса переносятся по номерам вершин: сетки обязаны совпасть полностью."""
    a, b = np.zeros(len(first.edges) * 2, int), np.zeros(len(second.edges) * 2, int)
    if len(a) != len(b) or len(first.vertices) != len(second.vertices):
        raise ValueError("Разные сетки: %d и %d вершин" % (len(first.vertices), len(second.vertices)))
    first.edges.foreach_get("vertices", a)
    second.edges.foreach_get("vertices", b)
    if (a != b).any():
        raise ValueError("Сетки различаются по рёбрам")


def similarity(points, targets):
    """Масштаб, поворот и сдвиг, переводящие points в targets с наименьшей ошибкой (Umeyama)."""
    centre, target_centre = points.mean(axis=0), targets.mean(axis=0)
    a, b = points - centre, targets - target_centre
    u, s, vt = np.linalg.svd(b.T @ a)
    flip = np.diag((1., 1., np.sign(np.linalg.det(u @ vt))))
    rotation = u @ flip @ vt
    scale = float((s * np.diag(flip)).sum() / (a * a).sum())
    matrix = np.eye(4)
    matrix[:3, :3] = rotation * scale
    matrix[:3, 3] = target_centre - rotation @ centre * scale
    return Matrix(matrix.tolist()), scale


def key_points(data, wrist):
    """Суставы, по которым варианты рук совмещаются со скелетом."""
    points = [wrist]
    for name in J.FINGERS:
        j = data["fingers"][name]
        points += [j["mcp"], j["pip"], j["dip"], j["tip"]]
    points += [data["thumb"][k] for k in ("mcp", "ip", "tip")]
    return np.array(points)


def polygon_keys(mesh):
    return [tuple(sorted(poly.vertices)) for poly in mesh.polygons]


def level_one_order(male, female):
    """Какая вершина уплотнённой женской сетки отвечает каждой вершине мужской.
    Blender кладёт вершины по порядку: исходные, на рёбрах, в центрах граней. Вершины и рёбра у тел общие, грани идут в другом порядке."""
    faces_from = len(male.data.vertices) + len(male.data.edges)
    lookup = {key: index for index, key in enumerate(polygon_keys(female.data))}
    faces = np.array([lookup[key] for key in polygon_keys(male.data)])
    return np.concatenate((np.arange(faces_from), faces_from + faces))


def shape_from(male_body, female_body, arm, matrix):
    """Вариант с той же сеткой: копия рабочей руки, вершины которой берут положение с другого тела."""
    order = level_one_order(male_body, female_body)
    full = source.evaluated_mesh(female_body, source.GAME_LEVEL, "variant")
    if len(full.vertices) != len(order):
        raise ValueError("Уплотнённая сетка не совпала с ожидаемой: %d и %d" % (len(full.vertices), len(order)))
    full_co = np.zeros(len(full.vertices) * 3)
    full.vertices.foreach_get("co", full_co)
    full_co = full_co.reshape(-1, 3)
    bpy.data.meshes.remove(full)
    origin = np.zeros(len(arm.data.vertices), int)
    arm.data.attributes[ORIGIN].data.foreach_get("value", origin)
    mesh = arm.data.copy()
    placed = np.array([matrix @ Vector(p) for p in full_co[order[origin]]])
    mesh.vertices.foreach_set("co", placed.ravel())
    mesh.update()
    return mesh


def sculpt_object(body, side_name, matrix, name):
    """Скульптура нужна только как источник деталей: граница у локтя берётся по самому телу."""
    side = 1 if side_name == "L" else -1
    mesh = source.evaluated_mesh(body, source.SCULPT_LEVEL, name)
    source.cut(mesh, source.arm_faces(mesh, side))
    mesh.transform(matrix)
    mesh.update()
    for poly in mesh.polygons:
        poly.use_smooth = True
    ob = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def arm_object(body, level, side_name, matrix, name):
    side = 1 if side_name == "L" else -1
    mesh = source.evaluated_mesh(body, level, name)
    numbers = mesh.attributes.new(ORIGIN, "INT", "POINT")
    numbers.data.foreach_set("value", np.arange(len(mesh.vertices)))
    source.cut(mesh, source.arm_faces(mesh, side))
    mesh.transform(matrix)
    mesh.update()
    for poly in mesh.polygons:
        poly.use_smooth = True
    ob = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def main():
    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob)
    male_body = source.load_body(source.BODIES["Male"])
    female_body = source.load_body(source.BODIES["Female"])
    frame = male_frame(male_body)

    groups = {name: collection(name) for name in ("Rig", "Hands_Male", "Hands_Female", "Sculpt", "Test_Props")}
    arms = {}
    flip = Matrix.Diagonal((-1, 1, 1, 1))
    placed = {"L": frame, "R": flip @ frame @ flip}
    for side_name in ("L", "R"):
        arm = arm_object(male_body, source.GAME_LEVEL, side_name, placed[side_name], "Arm_Male_" + side_name)
        move_to(arm, groups["Hands_Male"])
        arms["Male", side_name] = arm
        other = bpy.data.objects.new("Arm_Female_" + side_name, shape_from(male_body, female_body, arm, placed[side_name]))
        other.data.name = other.name
        bpy.context.scene.collection.objects.link(other)
        move_to(other, groups["Hands_Female"])
        arms["Female", side_name] = other
        for gender, body in (("Male", male_body), ("Female", female_body)):
            move_to(sculpt_object(body, side_name, placed[side_name], "Sculpt_%s_%s" % (gender, side_name)), groups["Sculpt"])
    bpy.data.objects.remove(male_body)
    bpy.data.objects.remove(female_body)
    groups["Sculpt"].hide_render = True
    groups["Hands_Female"].hide_render = True

    for side_name in ("L", "R"):
        same_mesh(arms["Male", side_name].data, arms["Female", side_name].data)
    co, sets, verts = J.read_mesh(arms["Male", "L"].data)
    wrist = np.array(frame @ source.SOURCE_WRIST)
    elbow = np.array(frame @ source.SOURCE_ELBOW)
    data = J.analyse(co, verts, wrist)
    # Запястье варианта: те же вершины кольца, что у мужской руки, дают ту же точку на его форме.
    wrist_ring = np.nonzero(np.abs((co - wrist) @ rig.unit(wrist - elbow)) < .008)[0]
    female_co, _, female_verts = J.read_mesh(arms["Female", "L"].data)
    female_wrist = female_co[wrist_ring].mean(axis=0) + (wrist - co[wrist_ring].mean(axis=0))
    female_data = J.analyse(female_co, female_verts, female_wrist)
    fit, scale = similarity(key_points(female_data, female_wrist), key_points(data, wrist))
    leftover = np.linalg.norm(key_points(data, wrist) - (np.array(fit)[:3, :3] @ key_points(female_data, female_wrist).T).T - np.array(fit)[:3, 3], axis=1)
    print("FEMALE FIT scale", round(scale, 3), "joint error mm: mean", round(float(leftover.mean() * 1000), 2), "max", round(float(leftover.max() * 1000), 2))
    for side_name, matrix in (("L", fit), ("R", flip @ fit @ flip)):
        for prefix in ("Arm", "Sculpt"):
            mesh = bpy.data.objects["%s_Female_%s" % (prefix, side_name)].data
            mesh.transform(matrix)
            mesh.update()
    for name in J.FINGERS:
        j = data["fingers"][name]
        print("JOINTS", name, {k: tuple(np.round(j[k], 3).tolist()) for k in j})
    print("JOINTS Thumb", {k: tuple(np.round(v, 3).tolist()) for k, v in data["thumb"].items()})

    centre, normal = J.nail_normal(co, verts, J.THUMB_SETS[3])
    outward = rig.unit(centre - data["thumb"]["ip"])
    across = rig.unit(np.cross(outward, normal))
    print("THUMB NAIL", np.round(normal, 3).tolist())
    # Ноготь смотрит от ладони: нормаль разворачивается в сторону от середины кисти.
    palm_centre = (wrist + data["fingers"]["Middle"]["mcp"]) / 2 + np.array((0, 0, -.015))
    thumb_back = normal if normal @ (centre - palm_centre) > 0 else -normal
    armature = rig.build_armature(data, thumb_back, elbow, wrist)
    move_to(armature, groups["Rig"])

    vertex_sets = np.zeros(len(co), int)
    for poly, value in zip(arms["Male", "L"].data.polygons, sets):
        for v in poly.vertices:
            # Вершина на границе относится к дальнему от запястья набору: так суставы делятся по сгибу.
            vertex_sets[v] = max(vertex_sets[v], value)
    table = rig.weights_for(co, vertex_sets, data, elbow, wrist, "_L")
    names, matrix = rig.trim(table)
    table_r = rig.weights_for(*mirrored_inputs(arms["Male", "R"].data, vertex_sets, data, elbow, wrist))
    names_r, matrix_r = rig.trim(table_r)
    for gender in ("Male", "Female"):
        rig.apply_weights(arms[gender, "L"], names, matrix, armature)
        rig.apply_weights(arms[gender, "R"], names_r, matrix_r, armature)
    clothes = collection("Clothes")
    move_to(sleeves.build(arms["Male", "L"], "Sleeve_L", wrist, elbow), clothes)
    move_to(sleeves.build(arms["Male", "R"], "Sleeve_R", rig.mirror(wrist), rig.mirror(elbow)), clothes)

    size = CARD_SIZE
    bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0, -.6))
    card = bpy.context.object
    card.name = "Test_Card"
    card.scale = size
    move_to(card, groups["Test_Props"])

    poses.store_actions(armature)
    in_game.place_fan_socket(armature)
    in_game.store(armature)
    animations.store(armature)
    bpy.context.view_layer.update()
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=OUT)
    print("SAVED", OUT, "bones", len(armature.data.bones), "arm faces", len(arms["Male", "L"].data.polygons))


def mirrored_inputs(mesh, vertex_sets, data, elbow, wrist):
    co_r, _, _ = J.read_mesh(mesh)
    flipped = {"fingers": {n: {k: rig.mirror(v) for k, v in j.items()} for n, j in data["fingers"].items()},
               "thumb": {k: rig.mirror(v) for k, v in data["thumb"].items()}}
    return co_r, vertex_sets_for(mesh), flipped, rig.mirror(elbow), rig.mirror(wrist), "_R"


def vertex_sets_for(mesh):
    _, sets, _ = J.read_mesh(mesh)
    vertex_sets = np.zeros(len(mesh.vertices), int)
    for poly, value in zip(mesh.polygons, sets):
        for v in poly.vertices:
            vertex_sets[v] = max(vertex_sets[v], value)
    return vertex_sets


main()
print("=== BUILD DONE")
