"""Проверка чужой модели руки перед тем, как брать её основой: что в файле на самом деле есть и как гнутся пальцы.

Открывает .blend, .fbx, .glb/.gltf или .obj в пустой сцене и печатает:
- сетки (вершины, треугольники), скелеты, число костей, цепочки пальцев, найденные по именам;
- веса: сколько групп вершин, сколько костей на вершину (Unity на телефоне берёт 4), вершины без веса;
- действия (клипы): имя, кадры, сколько костей анимировано. Риг без действий — это только скелет, не анимации;
- сгиб: каждый палец по отдельности и кулак; ось и знак сгиба подбираются сами (кончик уходит к стороне ладони,
  где большой палец, и меньше всего вбок). Пересечения пальцев по сетке в кулаке.
Кадры (открытая ладонь, полусогнутые пальцы, кулак, отведённый большой палец, каждый палец отдельно) — спереди,
сбоку и под углом, в папку CANDIDATE_OUT.

Запуск: blender --background --factory-startup --disable-autoexec --python scripts/3d/hands/candidate_check.py -- <файл> [имя]
Переменные: CANDIDATE_OUT — папка кадров (по умолчанию screens/3d-hands/candidates/<имя>), CANDIDATE_RENDER=0 — без кадров.
CANDIDATE_CONFIG — JSON для ригов, где пальцы по именам не найти или сгибаются не деформирующими костями:
  {"armature": "Rig-Hand", "mesh": "geo_hand_male", "hand": "DEF-hand.R", "mute": true, "bind": true,
   "chains": {"R": {"index": ["f_index.01.R", "f_index.02.R", "f_index.03.R"], ...}}}
  mute — выключить ограничения костей (сгибаем деформирующие кости напрямую); bind — добавить сетке модификатор Armature,
  если веса есть, а привязки нет; hand — кость кисти (от неё меряется плоскость кисти). Группа вершин DEF-<кость>
  относится к кости <кость> (риги Rigify)."""
import json
import math
import os
import re
import sys

import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
# Как модуль (overlaps() зовёт gesture_lab.py) скрипт запускается без пути к файлу.
PATH = os.path.abspath(ARGS[0]) if ARGS and __name__ == "__main__" else ""
NAME = ARGS[1] if len(ARGS) > 1 else os.path.splitext(os.path.basename(PATH))[0]
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
OUT = os.environ.get("CANDIDATE_OUT") or os.path.join(ROOT, "screens", "3d-hands", "candidates", NAME)
RENDER = os.environ.get("CANDIDATE_RENDER", "1") != "0"
CONFIG = json.loads(os.environ.get("CANDIDATE_CONFIG", "{}"))
FINGERS = ("thumb", "index", "middle", "ring", "pinky")
# Как пальцы называют в разных ригах: index / pointer / finger1 / f_index / IndexFinger.
ALIASES = {"thumb": ("thumb", "pollex", "finger0"), "index": ("index", "pointer", "finger1", "forefinger"),
           "middle": ("middle", "finger2"), "ring": ("ring", "finger3"), "pinky": ("pinky", "little", "finger4", "small")}


def load():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    ext = os.path.splitext(PATH)[1].lower()
    if ext == ".blend":
        with bpy.data.libraries.load(PATH, link=False) as (src, dst):
            dst.objects = list(src.objects)
            dst.actions = list(src.actions)
        for ob in dst.objects:
            if ob is not None:
                bpy.context.scene.collection.objects.link(ob)
    elif ext == ".fbx":
        bpy.ops.import_scene.fbx(filepath=PATH)
    elif ext in (".glb", ".gltf"):
        bpy.ops.import_scene.gltf(filepath=PATH)
    elif ext == ".obj":
        bpy.ops.wm.obj_import(filepath=PATH)
    else:
        raise SystemExit("неизвестный формат " + ext)


def finger_of(name):
    low = name.lower()
    for finger in FINGERS:
        if any(alias in low for alias in ALIASES[finger]):
            return finger
    return None


def side_of(name):
    low = name.lower()
    if re.search(r"(\.|_|-|\b)l(\b|$|\.|_)|left", low):
        return "L"
    if re.search(r"(\.|_|-|\b)r(\b|$|\.|_)|right", low):
        return "R"
    return ""


def chains(armature):
    """Цепочки пальцев: {(сторона, палец): [кости от основания к кончику]} — только деформирующие кости."""
    result = {}
    if CONFIG.get("chains"):
        if CONFIG.get("armature") and armature.name != CONFIG["armature"]:
            return {}
        for side, fingers in CONFIG["chains"].items():
            for finger, names in fingers.items():
                result[(side, finger)] = [armature.data.bones[n] for n in names]
        return result
    for bone in armature.data.bones:
        finger = finger_of(bone.name)
        if finger is None or not bone.use_deform:
            continue
        low = bone.name.lower()
        # Сгибаются только фаланги: кончики, помощники суставов, точки привязки и пястные кости (кроме большого пальца) — нет.
        if any(word in low for word in ("tip", "end", "nub", "socket", "joint", "twist", "helper", "ik", "pole")):
            continue
        if finger != "thumb" and any(word in low for word in ("metacarpal", "meta", "palm", "carpal")):
            continue
        result.setdefault((side_of(bone.name), finger), []).append(bone)
    for key, bones in result.items():
        # По глубине в иерархии: от основания к кончику.
        bones.sort(key=lambda b: len(b.parent_recursive))
    return result


def curves(action):
    """Кривые действия: в Blender 4.4+ они лежат в слоях и полосах, старое action.fcurves может отсутствовать."""
    found = list(getattr(action, "fcurves", []) or [])
    for layer in getattr(action, "layers", []):
        for strip in layer.strips:
            for bag in getattr(strip, "channelbags", []):
                found.extend(bag.fcurves)
    return found


def report(armatures, meshes):
    print("=== FILE", PATH)
    for ob in meshes:
        tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
        weights = [len([g for g in v.groups if g.weight > 1e-4]) for v in ob.data.vertices]
        unweighted = sum(1 for w in weights if w == 0)
        over4 = sum(1 for w in weights if w > 4)
        arm = ob.find_armature()
        print("=== MESH %-28s verts %6d tris %6d groups %3d | костей на вершину до %d, больше 4 у %d, без веса %d | скелет %s | материалы %s"
              % (ob.name, len(ob.data.vertices), tris, len(ob.vertex_groups), max(weights or [0]), over4, unweighted,
                 arm.name if arm else "нет", [m.name for m in ob.data.materials if m]))
    for armature in armatures:
        deform = [b for b in armature.data.bones if b.use_deform]
        print("=== ARMATURE %s bones %d (deform %d)" % (armature.name, len(armature.data.bones), len(deform)))
        for (side, finger), bones in sorted(chains(armature).items()):
            print("===   %s %-6s %d: %s" % (side or "-", finger, len(bones), ", ".join("%s %.1f мм" % (b.name, b.length * 1000) for b in bones)))
    for action in bpy.data.actions:
        groups = {fc.data_path.split('"')[1] for fc in curves(action) if '"' in fc.data_path}
        print("=== ACTION %-30s кадры %d–%d, костей с ключами %d" % (action.name, *action.frame_range, len(groups)))
    if not bpy.data.actions:
        print("=== ACTION нет: в файле только риг/позы без анимаций")
    markers = [m.name for m in bpy.context.scene.timeline_markers]
    if markers:
        print("=== MARKERS", markers)


def reset(armature):
    for bone in armature.pose.bones:
        bone.rotation_mode = "XYZ"
        bone.rotation_euler = (0, 0, 0)
        bone.rotation_quaternion = (1, 0, 0, 0)
        bone.location = (0, 0, 0)
    bpy.context.view_layer.update()


def tip(armature, bones):
    return armature.matrix_world @ armature.pose.bones[bones[-1].name].tail


def palm_frame(armature, found, side):
    """Плоскость кисти и направление поперёк неё, по положениям оснований пальцев в покое."""
    get = lambda f: found.get((side, f))
    index, pinky, thumb = get("index"), get("pinky"), get("thumb")
    if not (index and pinky):
        return None
    mw = armature.matrix_world
    a, b = mw @ index[0].head_local, mw @ pinky[0].head_local
    hand = armature.data.bones.get(CONFIG.get("hand", ""))
    wrist = mw @ (hand.head_local if hand else index[0].parent.head_local if index[0].parent else index[0].head_local)
    across = (a - b).normalized()
    along = ((a + b) / 2 - wrist).normalized()
    normal = across.cross(along).normalized()
    if thumb:
        # Сторона ладони — та, куда смотрит кончик большого пальца в покое относительно плоскости кисти.
        t = mw @ thumb[-1].tail_local
        if (t - wrist).dot(normal) < 0:
            normal = -normal
    return wrist, across, along, normal


def bend_axis(armature, bones, frame):
    """Ось и знак сгиба цепочки: кончик уходит к ладони и меньше всего вбок."""
    wrist, across, along, normal = frame
    base = armature.matrix_world @ bones[0].head_local
    best = None
    for axis in (0, 2):
        for sign in (1, -1):
            reset(armature)
            for bone in bones:
                rot = [0, 0, 0]
                rot[axis] = sign * math.radians(50)
                armature.pose.bones[bone.name].rotation_euler = rot
            bpy.context.view_layer.update()
            t = tip(armature, bones)
            toward_palm = (t - base).dot(normal)
            sideways = abs((t - base).dot(across))
            score = toward_palm - 2 * sideways
            if best is None or score > best[0]:
                best = (score, axis, sign)
    reset(armature)
    return best[1], best[2]


def pose(armature, found, axes, angles):
    """angles: {палец: (основание, средний, дальний)} в градусах сгиба."""
    reset(armature)
    for key, bones in found.items():
        if key not in axes:
            continue
        degrees = angles.get(key[1])
        if not degrees:
            continue
        axis, sign = axes[key]
        for bone, d in zip(bones, degrees):
            rot = [0, 0, 0]
            rot[axis] = sign * math.radians(d)
            armature.pose.bones[bone.name].rotation_euler = rot
            # Риг Card City: промежуточная кость сустава Joint* в игре поворачивается на половину угла (ключами в клипах).
            helper = armature.pose.bones.get("Joint" + bone.name)
            if helper is not None:
                helper.rotation_mode = "XYZ"
                helper.rotation_euler = [r / 2 for r in rot]
    bpy.context.view_layer.update()


def overlaps(mesh, armature, found):
    """Пересечения разных пальцев по сетке, как она выходит после всех модификаторов (зеркало, сглаживание): пары
    треугольников и доля треугольников пальцев, которые входят в другой палец. Доля от плотности сетки почти не зависит,
    поэтому ей и сравниваются модели. Основные фаланги соседних пальцев не сравниваются: у основания перепонка."""
    owner_of = {}
    for (side, finger), bones in found.items():
        for i, bone in enumerate(bones):
            owner_of[bone.name] = (finger, i)
            owner_of["DEF-" + bone.name] = (finger, i)
            owner_of["Joint" + bone.name] = (finger, i)
    # Сглаживание (Subdivision) теряет привязку новых вершин к костям: пересечения меряются по сетке до сглаживания
    # (зеркало и скелет остаются). Для моделей со сглаживанием это нижняя оценка: сглаженная сетка чуть тоньше.
    smooth = [m for m in mesh.modifiers if m.type == "SUBSURF" and m.show_viewport]
    for m in smooth:
        m.show_viewport = False
    depsgraph = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(depsgraph)
    data = ev.to_mesh(preserve_all_data_layers=True, depsgraph=depsgraph)
    for m in smooth:
        m.show_viewport = True
    names = {g.index: g.name for g in mesh.vertex_groups}
    owner = []
    for v in data.vertices:
        g = max(v.groups, key=lambda g: g.weight, default=None)
        owner.append(owner_of.get(names.get(g.group)) if g else None)
    co = [mesh.matrix_world @ v.co for v in data.vertices]
    parts = {}
    for poly in data.polygons:
        owners = {owner[i] for i in poly.vertices}
        if len(owners) == 1 and None not in owners:
            parts.setdefault(owners.pop(), []).append([co[i] for i in poly.vertices])
    trees = {}
    for key, polys in parts.items():
        verts, faces = [], []
        for p in polys:
            faces.append(list(range(len(verts), len(verts) + len(p))))
            verts.extend(p)
        trees[key] = BVHTree.FromPolygons(verts, faces)
    ev.to_mesh_clear()
    total, worst, hit, hit_fingers = 0, {}, {k: set() for k in trees}, {k: set() for k in trees}
    keys = sorted(trees)
    for i, a in enumerate(keys):
        for b in keys[i + 1:]:
            if a[0] == b[0] or (a[1] == 0 and b[1] == 0 and "thumb" not in (a[0], b[0])):
                continue
            pairs = trees[a].overlap(trees[b])
            if pairs:
                total += len(pairs)
                worst["%s%d-%s%d" % (a[0], a[1] + 1, b[0], b[1] + 1)] = len(pairs)
                hit[a].update(x for x, _ in pairs)
                hit[b].update(y for _, y in pairs)
                if "thumb" not in (a[0], b[0]):
                    hit_fingers[a].update(x for x, _ in pairs)
                    hit_fingers[b].update(y for _, y in pairs)
    finger_faces = sum(len(v) for v in parts.values())
    share = 100 * sum(len(v) for v in hit.values()) / max(1, finger_faces)
    # Без большого пальца: его поза в общем тесте условная, пересечения четырёх пальцев между собой сравнимы честнее.
    share_fingers = 100 * sum(len(v) for v in hit_fingers.values()) / max(1, finger_faces)
    top = sorted(worst.items(), key=lambda kv: -kv[1])[:4]
    return total, share, share_fingers, finger_faces, top


def camera_setup(target, size):
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "STUDIO"
    sc.display.shading.color_type = "MATERIAL"
    sc.display.shading.show_cavity = True
    sc.render.resolution_x, sc.render.resolution_y = 640, 560
    cam = bpy.data.objects.new("check cam", bpy.data.cameras.new("check cam"))
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = size * 1.25
    cam.data.clip_end = size * 40
    sc.collection.objects.link(cam)
    sc.camera = cam
    return cam


def shoot(cam, target, direction, up, size, path):
    eye = target + direction.normalized() * size * 6
    forward = (target - eye).normalized()
    right = forward.cross(up).normalized()
    true_up = right.cross(forward)
    cam.matrix_world = Matrix((right.to_4d(), true_up.to_4d(), (-forward).to_4d(), Vector((0, 0, 0, 1)))).transposed()
    cam.matrix_world.translation = eye
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def main():
    load()
    armatures = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    report(armatures, meshes)
    if CONFIG.get("armature"):
        armatures = [a for a in armatures if a.name == CONFIG["armature"]]
    for armature in armatures:
        armature.animation_data_clear()
        if CONFIG.get("mute"):
            for pb in armature.pose.bones:
                for c in pb.constraints:
                    c.mute = True
        if CONFIG.get("bind"):
            for m in meshes:
                if (m.parent == armature or m.name == CONFIG.get("mesh")) and not any(x.type == "ARMATURE" for x in m.modifiers):
                    mod = m.modifiers.new("check armature", "ARMATURE")
                    mod.object = armature
        found = chains(armature)
        sides = sorted({side for side, _ in found})
        for side in sides:
            frame = palm_frame(armature, found, side)
            if frame is None:
                print("=== SIDE %s: не найдены указательный и мизинец по именам — оси сгиба не подобрать" % (side or "-"))
                continue
            keys = [k for k in found if k[0] == side]
            axes = {k: bend_axis(armature, found[k], frame) for k in keys}
            print("=== SIDE %s оси сгиба: %s" % (side or "-", {k[1]: ("X" if a == 0 else "Z", s) for k, (a, s) in axes.items()}))
            names = {b.name for k in keys for b in found[k]}
            names |= {"DEF-" + n for n in names}
            mesh = bpy.data.objects.get(CONFIG.get("mesh", "")) or next((m for m in meshes if any(
                x.type == "ARMATURE" and x.object == armature for x in m.modifiers) and any(g.name in names for g in m.vertex_groups)), None)
            # В кадре только проверяемая кисть: прочие сетки файла (другие руки, рукава, предметы, виджеты) скрыты.
            for m in bpy.context.scene.objects:
                if m.type in ("MESH", "CURVE", "FONT", "SURFACE"):
                    m.hide_render = m != mesh
            if mesh is None:
                print("=== SIDE %s: сетка, привязанная к этим костям, не найдена" % (side or "-"))
            tests = {
                "open": {},
                "half": {f: (40, 45, 25) for f in FINGERS if f != "thumb"} | {"thumb": (10, 20, 20)},
                "fist": {"index": (85, 95, 65), "middle": (88, 98, 65), "ring": (88, 95, 60), "pinky": (85, 90, 55), "thumb": (20, 40, 45)},
                "thumb-out": {"thumb": (-25, -10, -5)},
            }
            for f in FINGERS:
                if f != "thumb":
                    tests["only-" + f] = {f: (85, 95, 65)}
            wrist, across, along, normal = frame
            size = max((armature.matrix_world @ b.tail_local - wrist).length for k in keys for b in found[k]) * 1.4
            target = wrist + along * size * .35
            cam = camera_setup(target, size) if RENDER else None
            os.makedirs(OUT, exist_ok=True)
            for name, angles in tests.items():
                pose(armature, found, {k: axes[k] for k in keys}, {f: angles.get(f) for f in FINGERS})
                line = "=== POSE %s %-12s" % (side or "-", name)
                if name.startswith("only-"):
                    # Куда уходит кончик согнутого пальца поперёк кисти: у живой руки согнутые пальцы сходятся к основанию
                    # большого (плюс — к большому пальцу), наружу за край кисти не уходят.
                    bones = found[(side, name[5:])]
                    base = armature.matrix_world @ bones[0].head_local
                    t = tip(armature, bones)
                    length = sum(b.length for b in bones) * armature.matrix_world.to_scale().x
                    # В долях длины пальца: модели разного масштаба (сантиметры, метры) сравниваются честно.
                    line += " кончик поперёк кисти %+5.1f%% длины пальца (плюс — к большому), к ладони %5.1f%%" % (
                        100 * (t - base).dot(across) / length, 100 * (t - base).dot(normal) / length)
                if mesh is not None:
                    n, share, share_fingers, faces, top = overlaps(mesh, armature, {k: found[k] for k in keys})
                    line += " пересечений %4d пар, %4.1f%% треугольников пальцев, без большого %4.1f%% (из %d) %s" % (
                        n, share, share_fingers, faces, top)
                print(line)
                if RENDER and name in ("open", "half", "fist", "thumb-out") or RENDER and name.startswith("only-") and False:
                    for view, direction, up in (("front", normal, along), ("side", across, along), ("angle", normal + across * .8 - along * .4, along)):
                        shoot(cam, target, direction, up, size, os.path.join(OUT, "%s%s-%s.png" % (side, name, view)))
                elif RENDER:
                    shoot(cam, target, normal + across * .8, along, size, os.path.join(OUT, "%s%s.png" % (side, name)))
    print("=== CANDIDATE DONE", OUT)


if __name__ == "__main__":
    main()
