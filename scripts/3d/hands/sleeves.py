"""Рукава свитера: оболочка над предплечьем с теми же весами, что у кожи, поэтому рукав сгибается вместе с рукой.
Так же делаются будущие перчатки и другая одежда: копия части сетки руки, а скелет и анимации остаются общими."""
import bmesh
import bpy
import numpy as np

CUFF_GAP = .03                  # На таком расстоянии от запястья кончается манжета.
CUFF_LENGTH = .034
SHELL = .0048                   # Толщина рукава над кожей.
CUFF_EXTRA = .0022              # Манжета плотнее и чуть толще.
CUFF_RAMP = .008                # Толщина манжеты нарастает плавно: ступенька читалась как шов.
SMOOTHING = 8                   # Ткань не повторяет вены и сухожилия.
SWEATER_COLOR = (.03, .13, .16)


def sweater_material():
    material = bpy.data.materials.get("M_Sweater") or bpy.data.materials.new("M_Sweater")
    material.use_nodes = True
    material.use_fake_user = True
    shader = next(n for n in material.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    shader.inputs["Base Color"].default_value = (*SWEATER_COLOR, 1)
    shader.inputs["Roughness"].default_value = .85
    return material


def build(arm, name, wrist, elbow):
    """Рукав от локтя до манжеты. arm уже с весами: они переезжают вместе с вершинами."""
    forward = (wrist - elbow) / np.linalg.norm(wrist - elbow)
    end = float(np.linalg.norm(wrist - elbow)) - CUFF_GAP
    bm = bmesh.new()
    bm.from_mesh(arm.data)
    along = lambda point: float((np.array(point) - elbow) @ forward)
    # Ровный срез поперёк предплечья: грани сетки идут наискосок, и край по ним выходил лесенкой.
    bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], plane_co=tuple(elbow + forward * end),
                           plane_no=tuple(forward), clear_outer=True)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    for _ in range(SMOOTHING):
        bmesh.ops.smooth_vert(bm, verts=[v for v in bm.verts if not v.is_boundary], factor=.5, use_axis_x=True, use_axis_y=True, use_axis_z=True)
    bm.normal_update()
    for vert in bm.verts:
        ramp = np.clip((CUFF_LENGTH + CUFF_RAMP - (end - along(vert.co))) / CUFF_RAMP, 0., 1.)
        cuff = ramp * ramp * (3 - 2 * ramp)
        vert.co += vert.normal * (SHELL + CUFF_EXTRA * cuff)
    # Край манжеты заворачивается к коже: иначе сквозь рукав видна пустота.
    rim = [e for e in bm.edges if e.is_boundary and along(e.verts[0].co) > end - .02]
    made = bmesh.ops.extrude_edge_only(bm, edges=rim)
    inner = [g for g in made["geom"] if isinstance(g, bmesh.types.BMVert)]
    for vert in inner:
        point = np.array(vert.co)
        centre = elbow + forward * along(point)
        inward = centre - point
        moved = point + inward / np.linalg.norm(inward) * (SHELL + CUFF_EXTRA)
        vert.co = tuple(moved)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    mesh.materials.clear()
    mesh.materials.append(sweater_material())
    for poly in mesh.polygons:
        poly.use_smooth = True
    ob = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(ob)
    for group in arm.vertex_groups:
        ob.vertex_groups.new(name=group.name)
    modifier = ob.modifiers.new("Armature", "ARMATURE")
    modifier.object = arm.parent
    ob.parent = arm.parent
    return ob
