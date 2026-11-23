"""Стол и стул клуба для 3D-режима, размеры в метрах по листу генерации (генерации/2-стол-и-стул/готово/лист.jpg).
Модуль для Blender: build_table и build_chair строят объекты, верх стола берёт art3d/table/table-top.png."""
import math
import os

import bmesh
import bpy
from mathutils import Matrix, Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
TOP_TEXTURE = os.path.join(ROOT, "art3d", "table", "table-top.png")

TABLE_A, TABLE_B, TABLE_Z = .92, .65, .76  # Полуоси овала и высота сукна.
RIM = .08                                  # Ширина золотого обода.
CHAIR_SEAT_Z = .48


def principled(m):
    """Узел шейдера ищется по типу: его имя зависит от языка Blender."""
    return next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")


def material(name, color, rough, metal=0., sheen=0.):
    m = bpy.data.materials.get(name)
    if m:
        return m
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    p = principled(m)
    p.inputs["Base Color"].default_value = (*color, 1)
    p.inputs["Roughness"].default_value = rough
    p.inputs["Metallic"].default_value = metal
    if "Sheen Weight" in p.inputs:
        p.inputs["Sheen Weight"].default_value = sheen
    return m


def gold():
    return material("gold", (.86, .54, .16), .3, .95)


def wood():
    return material("wood", (.075, .024, .012), .3)


def velvet():
    return material("velvet", (.19, .005, .02), .7, sheen=.2)


def table_top_material():
    m = bpy.data.materials.get("table top")
    if m:
        return m
    m = bpy.data.materials.new("table top")
    m.use_nodes = True
    nodes, links = m.node_tree.nodes, m.node_tree.links
    tex = nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(TOP_TEXTURE, check_existing=True)
    tex.extension = "EXTEND"
    p = principled(m)
    links.new(tex.outputs["Color"], p.inputs["Base Color"])
    p.inputs["Roughness"].default_value = .62
    return m


def finish(bm, name, materials, smooth=True, split=38):
    me = bpy.data.meshes.new(name)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    for m in materials:
        me.materials.append(m)
    for poly in me.polygons:
        poly.use_smooth = smooth
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    if split:
        mod = ob.modifiers.new("Edge Split", "EDGE_SPLIT")
        mod.split_angle = math.radians(split)
    return ob


def oval_rings(bm, rings, n=96, top_uv=False):
    """Кольца овала (a, b, z, материал полосы до следующего кольца); материал первого кольца — у заливки середины."""
    uv = bm.loops.layers.uv.verify()
    all_rings = []
    for a, b, z, _ in rings:
        all_rings.append([bm.verts.new((a * math.cos(i / n * math.tau), b * math.sin(i / n * math.tau), z)) for i in range(n)])
    for r in range(len(rings) - 1):
        for i in range(n):
            j = (i + 1) % n
            f = bm.faces.new((all_rings[r][i], all_rings[r][j], all_rings[r + 1][j], all_rings[r + 1][i]))
            f.material_index = rings[r][3]
    for ring, cap in ((all_rings[0], rings[0]), (all_rings[-1], rings[-1])):
        centre = bm.verts.new((0, 0, cap[2]))
        for i in range(n):
            f = bm.faces.new((centre, ring[i], ring[(i + 1) % n]))
            f.material_index = cap[3]
    if top_uv:
        # Верх закрыт картинкой сверху: достаточно плоской проекции по овалу.
        for f in bm.faces:
            for loop in f.loops:
                x, y, _ = loop.vert.co
                loop[uv].uv = (.5 + x / (2 * TABLE_A), .5 + y / (2 * TABLE_B))


def add_box(bm, centre, size, index, bevel=0., segments=2):
    made = bmesh.ops.create_cube(bm, size=1, matrix=Matrix.Translation(centre) @ Matrix.Diagonal((*size, 1)))
    verts = made["verts"]
    faces = {f for v in verts for f in v.link_faces}
    for f in faces:
        f.material_index = index
    if bevel:
        edges = list({e for v in verts for e in v.link_edges})
        result = bmesh.ops.bevel(bm, geom=list(verts) + edges, offset=bevel, offset_type="OFFSET", segments=segments, profile=.5, affect="EDGES")
        for f in result["faces"]:
            f.material_index = index


def add_tube(bm, points, radii, index, sides=10):
    """Трубка по точкам: круглое сечение поперёк направления; подходит и для ножек с точёным профилем."""
    points = [Vector(p) for p in points]
    rings = []
    for i, p in enumerate(points):
        d = (points[min(i + 1, len(points) - 1)] - points[max(i - 1, 0)]).normalized()
        x = d.cross(Vector((1, 0, 0)) if abs(d.x) < .9 else Vector((0, 1, 0))).normalized()
        y = d.cross(x)
        rings.append([bm.verts.new(p + (x * math.cos(k / sides * math.tau) + y * math.sin(k / sides * math.tau)) * radii[i]) for k in range(sides)])
    for r in range(len(rings) - 1):
        for k in range(sides):
            m = (k + 1) % sides
            f = bm.faces.new((rings[r][k], rings[r][m], rings[r + 1][m], rings[r + 1][k]))
            f.material_index = index
    for ring in (rings[0], rings[-1]):
        f = bm.faces.new(ring)
        f.material_index = index


def export_fbx(objects, path):
    """Выгрузка для Unity. В игре модель разворачивается на 180° вокруг вертикали, тогда +Y Blender смотрит вперёд."""
    bpy.context.view_layer.update()
    bpy.ops.object.select_all(action="DESELECT")
    for ob in objects:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH"},
                             axis_forward="-Z", axis_up="Y", apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
                             bake_space_transform=True, use_mesh_modifiers=True, mesh_smooth_type="FACE", bake_anim=False)
    print("EXPORTED", os.path.basename(path), sum(len(ob.data.polygons) for ob in objects), "faces")


def build_table():
    a, b, z = TABLE_A, TABLE_B, TABLE_Z
    inset = lambda d, h, m: (a - d, b - d, h, m)
    # 0 — верх с картинкой, 1 — золото, 2 — дерево. Профиль снят с вида сбоку на листе.
    rings = [inset(RIM, z, 0), inset(.074, z + .01, 0), inset(.045, z + .015, 0), inset(.012, z + .01, 0),
             inset(0, z - .002, 1), inset(0, z - .033, 1), inset(.006, z - .033, 2), inset(.006, z - .089, 1),
             inset(0, z - .089, 1), inset(0, z - .105, 2), (.58, .41, z - .105, 2), (.58, .41, z - .136, 2),
             (.25, .25, z - .136, 2), (.25, .25, z - .175, 2), (.22, .22, z - .19, 2)]
    bm = bmesh.new()
    oval_rings(bm, rings, top_uv=True)
    top = finish(bm, "Table", [table_top_material(), gold(), wood()])

    bm = bmesh.new()
    add_box(bm, (0, 0, .33), (.36, .36, .5), 1, .006)
    for side in (-1, 1):
        add_box(bm, (side * .14, .181, .33), (.012, .004, .46), 0)
        add_box(bm, (side * .14, -.181, .33), (.012, .004, .46), 0)
        add_box(bm, (.181, side * .14, .33), (.004, .012, .46), 0)
        add_box(bm, (-.181, side * .14, .33), (.004, .012, .46), 0)
    oval_rings(bm, [(.3, .3, .1, 1), (.3, .3, .075, 1), (.42, .32, .07, 1), (.44, .34, .05, 0), (.46, .36, .03, 0), (.46, .36, 0, 0)], n=48)
    base = finish(bm, "Table base", [gold(), wood()])
    base.parent = top
    return top


def back_outline():
    """Половина спинки по виду сзади: (ширина от середины, высота). Низ у́же, самая широкая часть — чуть выше середины."""
    half = [(.165, .36), (.165, .48), (.185, .58), (.215, .70), (.228, .78), (.215, .85), (.175, .9), (.1, .925), (0, .935)]
    keys = [(x, z) for x, z in half] + [(-x, z) for x, z in reversed(half[:-1])]
    # Сглаживание замкнутым сплайном Катмулла — Рома: иначе край спинки виден гранями.
    n, steps, smooth = len(keys), 5, []
    for i in range(n):
        p0, p1, p2, p3 = (keys[(i + k) % n] for k in (-1, 0, 1, 2))
        for step in range(steps):
            t = step / steps
            smooth.append(tuple(.5 * (2 * p1[c] + (p2[c] - p0[c]) * t + (2 * p0[c] - 5 * p1[c] + 4 * p2[c] - p3[c]) * t * t
                                      + (3 * p1[c] - p0[c] - 3 * p2[c] + p3[c]) * t * t * t) for c in (0, 1)))
    return smooth


def back_point(x, z, forward):
    """Спинка наклонена назад примерно на 8°: сиденью нужна опора под углом."""
    return (x, -.17 - (z - .36) * .14 + forward, z)


def add_back(bm):
    outline = back_outline()
    front = [bm.verts.new(back_point(x, z, 0)) for x, z in outline]
    rear = [bm.verts.new(back_point(x, z, -.04)) for x, z in outline]
    n = len(outline)
    bm.faces.new(front).material_index = 1
    bm.faces.new(list(reversed(rear))).material_index = 1
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((front[i], front[j], rear[j], rear[i])).material_index = 1
    # Мягкая часть: кольца от края к середине, бархат вздувается, как подушка.
    pad = [(x, z) for x, z in outline if z > .45]
    cx, cz = 0, sum(z for _, z in pad) / len(pad)
    bulge = ((.83, .004, 2), (.8, .016, 2), (.6, .03, 2), (.3, .036, 2))
    trim = [bm.verts.new(back_point(x * .88, cz + (z - cz) * .88, .004)) for x, z in pad]
    rings = []
    for s, y, _ in bulge:
        rings.append([bm.verts.new(back_point(x * s, cz + (z - cz) * s, y)) for x, z in pad])
    m = len(pad)
    for i in range(m):
        j = (i + 1) % m
        bm.faces.new((trim[i], trim[j], rings[0][j], rings[0][i])).material_index = 0
    for r in range(len(rings) - 1):
        for i in range(m):
            j = (i + 1) % m
            bm.faces.new((rings[r][i], rings[r][j], rings[r + 1][j], rings[r + 1][i])).material_index = 2
    bm.faces.new(rings[-1]).material_index = 2
    # Стяжка на семь пуговиц, как на листе: ряды 2, 3 и 2.
    for x, z in ((-.06, .73), (.06, .73), (-.11, .655), (0, .655), (.11, .655), (-.06, .58), (.06, .58)):
        made = bmesh.ops.create_uvsphere(bm, u_segments=8, v_segments=6, radius=.01, matrix=Matrix.Translation(back_point(x, z, .03)))
        for f in {f for v in made["verts"] for f in v.link_faces}:
            f.material_index = 2
    # Герб на верхушке спинки: золотой круг, тёмная середина, треф.
    upright = Matrix.Rotation(math.radians(-90), 4, "X")
    for radius, y, depth, index in ((.042, -.016, .05, 0), (.033, .012, .006, 1)):
        made = bmesh.ops.create_cone(bm, cap_ends=True, segments=20, radius1=radius, radius2=radius, depth=depth,
                                     matrix=Matrix.Translation(back_point(0, .945, y)) @ upright)
        for f in {f for v in made["verts"] for f in v.link_faces}:
            f.material_index = index
    for x, z, radius in ((0, .958, .0095), (-.0115, .94, .0095), (.0115, .94, .0095), (0, .93, .0045)):
        made = bmesh.ops.create_cone(bm, cap_ends=True, segments=10, radius1=radius, radius2=radius, depth=.004,
                                     matrix=Matrix.Translation(back_point(x, z, .017)) @ upright)
        for f in {f for v in made["verts"] for f in v.link_faces}:
            f.material_index = 0


def build_chair(name="Chair"):
    # 0 — золото, 1 — дерево, 2 — бархат.
    bm = bmesh.new()
    add_box(bm, (0, .01, .44), (.47, .41, .075), 2, .03, 3)
    add_box(bm, (0, .01, .38), (.46, .39, .05), 1, .004)
    add_box(bm, (0, .01, .4), (.468, .398, .009), 0)
    for side in (-1, 1):
        add_box(bm, (side * .21, .19, .378), (.06, .06, .058), 0, .003, 1)
        add_tube(bm, ((side * .21, .19, .35), (side * .21, .19, .335), (side * .21, .19, .32), (side * .21, .19, .3), (side * .213, .195, .24), (side * .216, .2, .12), (side * .216, .2, .05)),
                 (.027, .027, .02, .025, .022, .018, .015), 1)
        add_tube(bm, ((side * .216, .2, .05), (side * .216, .2, .04), (side * .216, .2, .022), (side * .216, .2, .018), (side * .216, .2, 0)),
                 (.019, .019, .014, .018, .016), 0)
        add_tube(bm, ((side * .195, -.17, .37), (side * .2, -.2, .26), (side * .205, -.24, .14), (side * .208, -.28, .03)), (.024, .022, .018, .015), 1)
        add_tube(bm, ((side * .208, -.28, .03), (side * .209, -.292, 0)), (.016, .015), 0)
    add_back(bm)
    return finish(bm, name, [gold(), wood(), velvet()], split=50)
