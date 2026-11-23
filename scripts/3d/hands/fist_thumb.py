"""Корректирующая форма FistThumb: кожа у большого пальца в кулаке с большим пальцем сбоку от указательного (фото автора
references/hands/кулак-фото/5, 2026-11-20). Только для кулака и отдельно от FistSmooth (просьба автора: «если потребуется
корректировка геометрии, сделай её отдельно и только для кулака»; FistSmooth не трогать).

Зачем. Форма FistSmooth (fist_smooth.py) снята 2026-11-19 в позе «большой палец сверху на кулаке»: кроме краёв четырёх
пальцев в ней сглаживание у пястно-фалангового сустава большого пальца и объём 4 мм в развилке — под ту позу. В новой позе
та же часть формы отводит кожу большого пальца от пальцев на 3,4 мм, а в развилке и на межфаланговом суставе (сгиб 50°)
кожа заламывается до 157°.

Что делает. В позе кулака (gestures.FIST, большой палец — FS_POSE) кожа вокруг суставов большого пальца (FT_JOINTS:
пястно-фаланговый и межфаланговый, радиусы полной поправки и затухания) сглаживается с поправкой (Corrective Smooth, как
у FistSmooth) заново, в этой позе; в этой зоне она заменяет прежний FistSmooth, к краю зоны плавно переходит в него.
Объёма развилки (fist_smooth.web) здесь нет: в новой позе он вдавливал подушку под большим пальцем в кончик указательного.
С 2026-11-20 (автор: «тень неудобно легла, будто нет полруки») — скат от подушки большого пальца к середине ладони
(ramp, FT_RAMP — уклон, 0 — без ската): без него под отведённым на 62° основанием был уступ 20 мм и тень.
FistThumb — разница этой кожи и кожи с FistSmooth, переведённая в покой скелета. У четырёх пальцев (их фаланги и
промежуточные кости суставов) разница ноль: их форма остаётся ровно такой, как в FistSmooth. Вес — то же свойство скелета
fist_smooth (драйвер), поэтому FistThumb входит и выходит вместе с FistSmooth и только в клипе проверки Take_TestClench.

Запуск: blender <.blend> --background --disable-autoexec --python scripts/3d/hands/fist_thumb.py -- <команда>
  measure — без сохранения (можно без .blend): кожа в кулаке и на кадрах сжатия с FistSmooth и FistThumb.
  store   — добавить FistThumb обеим мужским рукам, сохранить .blend.
FT_JOINTS — суставы большого пальца в зоне сглаживания "кость:r0:r1,…" (мм)."""
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(__file__))
import fist_smooth  # noqa: E402  (FS_POSE подменяет большой палец в gestures.FIST)
import gestures  # noqa: E402
import hand_anatomy  # noqa: E402

KEY = "FistThumb"
ZONE = "FistThumbZone"
JOINTS = tuple((name, float(r0), float(r1)) for name, r0, r1 in
               (part.split(":") for part in os.environ.get("FT_JOINTS", "Thumb01~:22:42,Thumb02:18:34,Thumb03:10:20").split(",")))
FOUR = tuple(f + n for f in fist_smooth.FOUR for n in ("01", "02", "03"))
# Проходов сглаживания (FT_ITER): у подушки большого пальца перепад до 20 мм, его надо разнести шире, чем у суставов.
# С 2026-11-22 большой палец прижат к сгибу указательного: основание отведено на 68°, пястно-фаланговый сустав согнут
# на 63°. С прежней зоной (пястно-фаланговый 12–24 мм, межфаланговый 8–16 мм, 30 проходов) у основания было 6 загибов
# кожи до 163°; с зоной вдоль всей пястной кости и шире у суставов, 50 проходов — ни одного (круче всего 100°).
ITERATIONS = int(os.environ.get("FT_ITER", "75"))
# Скат от подушки большого пальца к середине ладони (ramp): уклон (мм высоты на мм по ладони, 0 — без ската), проходов
# сглаживания сдвига, какая кожа ладони двигается.
RAMP_SLOPE = float(os.environ.get("FT_RAMP", "0.75"))
RAMP_BLUR = int(os.environ.get("FT_RAMP_BLUR", "6"))
RAMP_Y = tuple(float(v) for v in os.environ.get("FT_RAMP_Y", "38,50").split(","))
RAMP_PARTS = {"Hand", "JointHand", "MetaIndex", "MetaMiddle", "MetaRing", "MetaPinky"}


def is_finger(owner):
    """Вершина четырёх пальцев: фаланги и промежуточные кости их суставов (JointIndex02 …)."""
    name = owner[5:] if owner.startswith("Joint") else owner
    return name in FOUR


def evaluate(armature, weight):
    armature[fist_smooth.PROP] = weight
    for name in fist_smooth.ARMS:
        ob = bpy.data.objects.get(name)
        if ob is not None and ob.data.shape_keys is not None:
            for key in (fist_smooth.KEY, KEY):
                block = ob.data.shape_keys.key_blocks.get(key)
                if block is not None:
                    block.value = weight
    bpy.context.view_layer.update()


def zone(ob, armature, side):
    """Вес новой поправки по вершинам, в покое скелета: вокруг суставов большого пальца JOINTS, плавно сходит на нет;
    у вершин четырёх пальцев — ноль."""
    from mathutils.geometry import intersect_point_line
    group = ob.vertex_groups.get(ZONE) or ob.vertex_groups.new(name=ZONE)
    hand = {g.index for g in ob.vertex_groups if g.name.endswith(side) and not g.name.startswith("Forearm")}
    bones, mw, obw = armature.data.bones, armature.matrix_world, ob.matrix_world
    # «Кость» — шар у её начала (сустава); «Кость~» — вдоль всей кости (от начала до конца).
    joints = [(mw @ bones[n.rstrip("~") + side].head_local, mw @ bones[n.rstrip("~") + side].tail_local if n.endswith("~") else None, r0, r1)
              for n, r0, r1 in JOINTS]

    def distance(p, head, tail):
        if tail is None:
            return (p - head).length
        point, t = intersect_point_line(p, head, tail)
        return (p - (head if t < 0 else tail if t > 1 else point)).length
    owners = fist_smooth.owner_names(ob)
    weights = []
    for v, owner in zip(ob.data.vertices, owners):
        w = min(1., sum(g.weight for g in v.groups if g.group in hand)) if not is_finger(owner) else 0.
        if w > 0:
            p = obw @ v.co
            w *= max(1 - fist_smooth.smoothstep(r0, r1, distance(p, h, t) * 1000) for h, t, r0, r1 in joints)
        weights.append(w)
        if w > 1e-3:
            group.add([v.index], w, "REPLACE")
        else:
            group.remove([v.index])
    return group, weights


def ramp(ob, armature, side, co):
    """Подтягивает выпирающий снизу-сбоку мешок ладонной пятки (Hand, JointHand, Thumb01) за прямую линию от запястья
    к пястно-фаланговому суставу большого пальца с места игрока (2026-11-23: «где кулак там или тень или что не так,
    как будто там кости в этом месте нет»): убирает провал и ступеньку тени под пястной костью большого пальца."""
    if RAMP_SLOPE <= 0:
        return [Vector() for _ in co]
    import numpy as np
    owners = fist_smooth.owner_names(ob)
    mw, obw = armature.matrix_world, ob.matrix_world
    hand = armature.pose.bones["Hand" + side]
    to_local = obw.inverted().to_3x3() @ mw.to_3x3()
    toward, along, back = ((to_local @ a).normalized() for a in hand_anatomy.hand_axes(hand.matrix, side))
    origin = obw.inverted() @ (mw @ hand.head)
    mm = obw.to_3x3().to_scale().x * 1000
    q = np.array([[(p - origin).dot(a) * mm for a in (toward, along, back)] for p in co])
    inward = (back * .86 - toward * .51).normalized()
    parts = {"Hand", "JointHand", "Thumb01", "JointThumb01", "MetaIndex", "MetaMiddle", "MetaRing", "MetaPinky", "ForearmTwist"}
    movable = np.array([o in parts for o in owners])
    pts = [p.copy() for p in co]
    for i in np.nonzero(movable)[0]:
        tw, al, bk = q[i]
        if al < -28 or al > 55 or bk > -4:
            continue
        w_al = fist_smooth.smoothstep(-28, -4, al) * (1 - fist_smooth.smoothstep(44, 55, al))
        w_bk = fist_smooth.smoothstep(-4, -10, bk)
        px = 170. - .328 * tw - .065 * al + .942 * bk
        pz = -110. + .660 * tw + .698 * al + .279 * bk
        excess = (px - 151.) * (-.839) + (pz + 124.) * (-.545)
        if excess > -14.5:
            push = (excess + 14.5) * .96 * w_al * w_bk
            pts[i] += inward * (push / mm)
    neighbours = [[] for _ in co]
    for e in ob.data.edges:
        a, b = e.vertices
        neighbours[a].append(b)
        neighbours[b].append(a)
    for _ in range(20):
        pts = [(p * .5 + sum((pts[j] for j in near), Vector()) * (.5 / len(near))) if ok and near else p
               for p, near, ok in zip(pts, neighbours, movable)]
    return [p - c for p, c in zip(pts, co)]


def bake(armature, ob, side):
    keys = ob.data.shape_keys.key_blocks
    old = keys.get(KEY)
    driver = None
    if old is not None:
        if ob.data.shape_keys.animation_data:
            driver = ob.data.shape_keys.animation_data.drivers.find('key_blocks["%s"].value' % KEY)
        if driver is not None:
            driver.mute = True
        old.value = 0.
    basis = [k.co.copy() for k in keys[0].data]
    fist_smooth.reset(armature)
    group, weights = zone(ob, armature, side)
    hand_anatomy.apply(armature, gestures.FIST, side)
    evaluate(armature, 1.)
    if old is not None:
        old.value = 0.
        bpy.context.view_layer.update()
    before = fist_smooth.coords(ob)
    evaluate(armature, 0.)
    plain = fist_smooth.coords(ob)
    # Сглаживание с поправкой кожи без форм вокруг суставов большого пальца; в зоне оно заменяет прежний FistSmooth
    # (тот снят в другой позе большого пальца), вне зоны остаётся FistSmooth, переход плавный — по весу зоны.
    mod = ob.modifiers.new("FistThumbBake", "CORRECTIVE_SMOOTH")
    mod.smooth_type, mod.rest_source, mod.use_pin_boundary = "LENGTH_WEIGHTED", "ORCO", False
    mod.iterations, mod.factor, mod.vertex_group = ITERATIONS, fist_smooth.FACTOR, group.name
    fresh = fist_smooth.coords(ob)
    ob.modifiers.remove(mod)
    target = [b.lerp(f, w) for b, f, w in zip(before, fresh, weights)]
    target = [p + s for p, s in zip(target, ramp(ob, armature, side, target))]
    # Скиннинг линеен по положению вершины в покое: матрица снимается тремя пробными сдвигами (как fist_smooth.bake).
    step = .001
    columns = []
    for axis in range(3):
        probe = ob.shape_key_add(name="probe", from_mix=False)
        shift = Vector((0, 0, 0))
        shift[axis] = step
        for k, co in zip(probe.data, basis):
            k.co = co + shift
        probe.value = 1.
        columns.append([(a - b) / step for a, b in zip(fist_smooth.coords(ob), plain)])
        ob.shape_key_remove(probe)
    key = old if old is not None else ob.shape_key_add(name=KEY, from_mix=False)
    for k, co in zip(key.data, basis):
        k.co = co
    moved = 0
    for i, (co, a, b) in enumerate(zip(basis, before, target)):
        delta = b - a
        if delta.length < 1e-7:
            continue
        m = Matrix((columns[0][i], columns[1][i], columns[2][i])).transposed()
        key.data[i].co = co + m.inverted_safe() @ delta
        moved += 1
    evaluate(armature, 1.)
    error = max((a - b).length for a, b in zip(fist_smooth.coords(ob), target)) * 1000
    print("=== FIST THUMB %s: вершин сдвинуто %d, наибольший сдвиг %.1f мм, расхождение %.3f мм"
          % (ob.name, moved, max((a - b).length for a, b in zip(before, target)) * 1000, error))
    evaluate(armature, 0.)
    if driver is not None:
        driver.mute = False
    fist_smooth.reset(armature)
    return key


def folds(ob, owners, edge_faces):
    """Загибы кожи у большого пальца и развилки: рёбра, где соседние грани сходятся круче 100°."""
    near = {"Thumb01", "Thumb02", "Thumb03", "JointThumb01", "JointThumb02", "JointThumb03", "Hand", "JointHand", "MetaIndex"}
    ev = ob.evaluated_get(bpy.context.evaluated_depsgraph_get())
    me = ev.to_mesh()
    normals = [p.normal.copy() for p in me.polygons]
    ev.to_mesh_clear()
    angles = sorted(math.degrees(normals[f[0]].angle(normals[f[1]])) for k, f in edge_faces.items()
                    if len(f) == 2 and owners[k[0]] in near and owners[k[1]] in near)
    return sum(a > 100 for a in angles), angles[-1] if angles else 0.


def measure():
    import fist_fit
    from gesture_lab import Skin
    if "Rig_Hands" not in bpy.data.objects:
        fist_smooth.load_light()
    armature = bpy.data.objects["Rig_Hands"]
    armature.animation_data_create()
    armature.animation_data.action = None
    armature.matrix_world.identity()
    ob = bpy.data.objects["Arm_Male_R"]
    bake(armature, ob, "_R")
    skin = Skin(armature, "_R")
    owners = fist_smooth.owner_names(ob)
    edge_faces = {}
    for p in ob.data.polygons:
        for k in p.edge_keys:
            edge_faces.setdefault(k, []).append(p.index)
    keys = ob.data.shape_keys.key_blocks
    for label, smooth_w, thumb_w in (("без форм", 0., 0.), ("FistSmooth", 1., 0.), ("FistSmooth+FistThumb", 1., 1.)):
        # У FistSmooth вес ведёт драйвер (свойство скелета), у нового FistThumb здесь драйвера ещё нет.
        armature[fist_smooth.PROP] = smooth_w
        keys[fist_smooth.KEY].value, keys[KEY].value = smooth_w, thumb_w
        m = fist_fit.measure(armature, skin, gestures.FIST)
        fist_fit.show(label, m)
        n, worst = folds(ob, owners, edge_faces)
        into = {body: fist_smooth.palm_into(skin, body) for body in ("Index", "Middle", "Thumb")}
        print("=== FIST THUMB %s: загибов кожи у большого пальца %d (круче всего %.0f°); ладонь в пальцах %s; дыра %s"
              % (label, n, worst, ", ".join("%s %.1f" % kv for kv in into.items()),
                 ", ".join("%s %.0f мм²" % kv for kv in fist_smooth.cavity(armature, ob, skin).items())))
    for frame in range(5, gestures.CLOSED + 1):
        evaluate(armature, fist_smooth.weight(frame))
        m = fist_fit.measure(armature, skin, gestures.closing(frame))
        print("=== FRAME %2d вес %.2f: палец в палец %.1f мм, большой в пальцах %.1f, пальцы в большом %.1f"
              % (frame, fist_smooth.weight(frame), max(m["deep"].values()), m["thumb_in"], m["fingers_in"]))
    evaluate(armature, 0.)


def store():
    armature = bpy.data.objects["Rig_Hands"]
    saved_action = armature.animation_data.action if armature.animation_data else None
    saved_matrix = armature.matrix_world.copy()
    armature.animation_data_create()
    armature.animation_data.action = None
    for name in fist_smooth.ARMS:
        key = bake(armature, bpy.data.objects[name], name[-2:])
        fist_smooth.drive(armature, key)
    armature.animation_data.action = saved_action
    armature.matrix_world = saved_matrix
    bpy.ops.wm.save_mainfile()
    print("=== FIST THUMB сохранено", bpy.data.filepath)


if __name__ == "__main__":
    command = (sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else ["measure"])[0]
    {"measure": measure, "store": store}[command]()
