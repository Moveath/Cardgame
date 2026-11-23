"""Корректирующая форма кулака FistSmooth: скругляет кожу у суставов пальцев, когда кисть сжата.

Автор посмотрел кулак в игре (2026-11-17): «края какие-то не очень». В кулаке пальцы у основания согнуты на 80–86°,
средний сустав — на 110–115°, а кожа у суставов гнётся узкой полосой (sharpen_joints.py, против пальцев «сосисками»).
При таком сгибе линейный скиннинг кладёт тыл кисти плоской доской с острыми углами у костяшек указательного и мизинца,
а у среднего сустава кожа загибается тонким лепестком. Позой это не убрать.

Форма считается сама, без лепки руками: в позе кулака на кожу накладывается сглаживание с поправкой (Corrective Smooth —
оно сглаживает только то, что исказил скелет, свои складки и бугры кожи сохраняет), и разница с обычной кожей
переводится в покой скелета как ключ формы (shape key). Сглаживается тыл и бока пальцев вокруг суставов: у костяшек
(основание пальцев) в радиусе 14–28 мм, у средних и дальних суставов в радиусе 9–18 мм. Ладонная сторона (складки
внутри кулака) не трогается: иначе кожа уходила бы в соседние пальцы.

С 2026-11-19 (большой палец сверху на кулаке, по фото автора) в ту же форму входят сглаживание у пястно-фалангового
сустава большого пальца (там кожа сминалась в лепесток) и объём 4 мм между большим и указательным пальцами (web):
основание большого пальца объёмное, переход к тылу кисти плавный. FS_WEB=0 и FS_THUMB_JOINTS=0 — форма как 2026-11-17.

Вес формы ведёт свойство скелета fist_smooth (драйвер): его ключи есть только в клипе проверки Take_TestClench
(game_test.bake), в остальных клипах оно 0 (export.py дописывает им ключ 0, чтобы значение не осталось от прошлого
клипа при выгрузке FBX). Поэтому партия, веер и прежние жесты кожу не меняют.

Запуск: blender <.blend> --background --disable-autoexec --python scripts/3d/hands/fist_smooth.py -- <команда>
  measure — без сохранения: замеры кулака (fist_fit.measure) с формой и без, переход Idle → кулак с весом формы по кадрам.
            Можно запускать без .blend (пустой Blender): тогда грузятся только скелет и правая мужская рука.
  store   — добавить форму обеим мужским рукам и драйвер, сохранить .blend.
Переменные: FS_ITER — проходов сглаживания (30), FS_FACTOR — сила (0.5)."""
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(__file__))
import gestures  # noqa: E402

KEY = "FistSmooth"
PROP = "fist_smooth"
ZONE = "FistSmoothZone"
FOUR = ("Index", "Middle", "Ring", "Pinky")
ARMS = ("Arm_Male_L", "Arm_Male_R")
ITERATIONS = int(os.environ.get("FS_ITER", "30"))
FACTOR = float(os.environ.get("FS_FACTOR", "0.5"))
# Объём между большим и указательным пальцами (web): на сколько мм выдвинуть кожу, радиусы зоны, проходы сглаживания.
WEB_MM = float(os.environ.get("FS_WEB", "4"))
WEB_NEAR, WEB_FAR, WEB_BLUR = 8., float(os.environ.get("FS_WEB_FAR", "26")), 6
# Суставы большого пальца в зоне сглаживания: (кость, радиус полной поправки, радиус, где она сходит на нет), мм.
# С 2026-11-19 (большой палец сверху на кулаке) включено; FS_THUMB_JOINTS=0 — как раньше, без него.
THUMB_JOINTS = (("Thumb02", 12, 24),) if os.environ.get("FS_THUMB_JOINTS", "1") == "1" else ()
# Где кожа может выдвигаться: ладонь, пястные кости и основание большого пальца; фаланги пальцев — нет.
WEB_PARTS = {"Hand", "JointHand", "MetaIndex", "MetaMiddle", "Thumb01", "JointThumb01"}


def patch_fist_pose():
    """FS_POSE — JSON с числами большого пальца (thumb, thumb_turn, thumb_bend): поверх gestures.FIST в памяти, чтобы
    проверить новую позу, не меняя gestures.py."""
    path = os.environ.get("FS_POSE")
    if not path:
        return
    import json
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    data = data if "thumb" in data else next(iter(data.values()))
    keys = ("thumb", "thumb_turn", "thumb_bend", "thumb_base")
    gestures.FIST.update({k: tuple(v) if isinstance(v, list) else v for k, v in data.items() if k in keys})
    print("=== FIST SMOOTH поза большого пальца из", path, {k: gestures.FIST[k] for k in keys if k in gestures.FIST})


patch_fist_pose()
BLEND = os.path.join(os.path.dirname(__file__), "..", "..", "..", "art3d", "hands", "CardCity_Hands.blend")


def smoothstep(a, b, x):
    t = min(1., max(0., (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def weight(frame):
    """Вес формы на кадре сжатия gestures.closing: входит к концу сжатия пальцев (квадрат доли сжатия) — при
    полусогнутых пальцах суставы ещё не ломаются углом, а форма, посчитанная для кулака, там была бы не к месту."""
    return gestures.smooth(frame / gestures.FINGERS_CLOSED) ** 2


def reset(armature):
    for bone in armature.pose.bones:
        bone.rotation_mode = "XYZ"
        bone.rotation_euler = (0, 0, 0)
        bone.location = (0, 0, 0)
        bone.scale = (1, 1, 1)
    bpy.context.view_layer.update()


def zone(ob, armature, side, name=ZONE, thumb_joints=None, facing=(-.5, .1)):
    """Вес сглаживания по вершинам, в покое скелета: тыл и бока пальцев у суставов, плавно сходит на нет.
    name и thumb_joints — для формы другой позы (gesture_smooth.py): своя группа и свои суставы большого пальца.
    facing — с какого наклона кожи к тылу кисти (косинус между нормалью и тылом) сглаживание входит и где оно полное:
    по умолчанию с боков пальцев; «класс» (2026-11-23) сглаживает только тыл — см. gesture_smooth.UP_FACING."""
    group = ob.vertex_groups.get(name) or ob.vertex_groups.new(name=name)
    hand = {g.index for g in ob.vertex_groups if g.name.endswith(side) and not g.name.startswith("Forearm")}
    bones, mw = armature.data.bones, armature.matrix_world
    joints = [(mw @ bones[f + "01" + side].head_local, 14, 28) for f in FOUR]
    joints += [(mw @ bones[f + n + side].head_local, 9, 18) for f in FOUR for n in ("02", "03")]
    # Суставы большого пальца — со всех сторон: при большом пальце сверху на кулаке (2026-11-17) под его
    # пястно-фаланговым суставом кожа ладони сминается в лепесток.
    thumb = [(mw @ bones[n + side].head_local, r0, r1) for n, r0, r1 in (THUMB_JOINTS if thumb_joints is None else thumb_joints)]
    back = (mw.to_3x3() @ bones["Hand" + side].matrix_local.to_3x3()).col[2].normalized()
    obw = ob.matrix_world
    for v in ob.data.vertices:
        w = min(1., sum(g.weight for g in v.groups if g.group in hand))
        if w > 0:
            p = obw @ v.co
            n = (obw.to_3x3() @ v.normal).normalized()
            fingers = max(1 - smoothstep(r0, r1, (p - h).length * 1000) for h, r0, r1 in joints) * smoothstep(facing[0], facing[1], n.dot(back))
            own = max((1 - smoothstep(r0, r1, (p - h).length * 1000) for h, r0, r1 in thumb), default=0.)
            w *= max(fingers, own)
        if w > 1e-3:
            group.add([v.index], w, "REPLACE")
        else:
            group.remove([v.index])
    return group


def coords(ob, normals=False):
    bpy.context.view_layer.update()
    ev = ob.evaluated_get(bpy.context.evaluated_depsgraph_get())
    mesh = ev.to_mesh()
    out = [v.co.copy() for v in mesh.vertices]
    nor = [v.normal.copy() for v in mesh.vertices]
    ev.to_mesh_clear()
    return (out, nor) if normals else out


def owner_names(ob):
    """Кость с наибольшим весом у каждой вершины (без стороны). Только группы костей (…_L/_R): группа зоны
    сглаживания ZONE в них не входит, иначе у суставов она перебивала бы кости."""
    side = ob.name[-2:]
    names = {g.index: g.name for g in ob.vertex_groups if g.name.endswith(side)}
    out = []
    for v in ob.data.vertices:
        best = max((g for g in v.groups if g.group in names), key=lambda g: g.weight, default=None)
        out.append(names[best.group][:-2] if best is not None else "")
    return out


def web(ob, armature, side, co, nor):
    """Объём между большим и указательным пальцами в кулаке, сдвиг каждой вершины в позе (в осях объекта).

    У живой руки в кулаке мышцы между пястными костями большого и указательного пальцев и основание большого пальца
    сжаты и выпирают: с тыла — плавный бугор от большого пальца к тылу кисти, со стороны большого пальца — сплошная
    стенка до кольца указательного. У сетки этого объёма нет: в кулаке кожа там проваливается, и сбоку видно внутрь
    кольца указательного (автор, 2026-11-17, фото references/hands/кулак-фото/3, 4). Кожа выдвигается по нормали на
    WEB_MM у треугольника «основание большого пальца — его пястно-фаланговый сустав — основание указательного» и сходит
    на нет к WEB_FAR мм от него. Фаланги пальцев и большого пальца не сдвигаются: их форма и длина прежние."""
    if WEB_MM <= 0:
        return [Vector() for _ in co]
    from mathutils.geometry import closest_point_on_tri
    bones = armature.pose.bones
    to_local = ob.matrix_world.inverted() @ armature.matrix_world
    a, b, c = (to_local @ bones[n + side].head for n in ("Thumb01", "Thumb02", "Index01"))
    owners = owner_names(ob)
    allowed = {g.index for g in ob.vertex_groups if g.name.endswith(side) and g.name[:-2] in WEB_PARTS}
    shift = []
    for v, p, n, owner in zip(ob.data.vertices, co, nor, owners):
        share = min(1., sum(g.weight for g in v.groups if g.group in allowed)) if owner in WEB_PARTS else 0.
        if share <= 0:
            shift.append(Vector())
            continue
        distance = (p - closest_point_on_tri(p, a, b, c)).length * 1000
        w = share * (1 - smoothstep(WEB_NEAR, WEB_FAR, distance))
        shift.append(n.normalized() * WEB_MM / 1000 * w)
    # Сглаживание сдвига по соседям: без него на границе зоны и у складок выходят бугры.
    neighbours = [[] for _ in co]
    for e in ob.data.edges:
        i, j = e.vertices
        neighbours[i].append(j)
        neighbours[j].append(i)
    movable = [owner in WEB_PARTS for owner in owners]
    for _ in range(WEB_BLUR):
        shift = [(s * .5 + sum((shift[j] for j in near), Vector()) * (.5 / len(near))) if ok and near else s
                 for s, near, ok in zip(shift, neighbours, movable)]
    return shift


def unfold(ob, plain, smooth, limit=90., rounds=30):
    """Сглаживание не должно выворачивать грани: если грань после сглаживания развёрнута от себя в позе без него больше
    чем на limit градусов, сдвиг её вершин уменьшается вдвое — по кругу, пока таких граней нет. Вывернутая грань видна
    тёмным треугольником (изнанка): у «класса» по модели автора (2026-11-23) — в складках пальцев с ладонной стороны,
    которую теперь видно с места игрока. Возвращает новые точки и число вершин, у которых сдвиг уменьшен."""
    from mathutils.geometry import normal
    polys = [tuple(p.vertices) for p in ob.data.polygons]
    smooth = list(smooth)
    touched = set()
    cos_limit = math.cos(math.radians(limit))
    for _ in range(rounds):
        bad = set()
        for f in polys:
            if all((smooth[i] - plain[i]).length_squared < 1e-14 for i in f):
                continue
            a, b = normal([plain[i] for i in f]), normal([smooth[i] for i in f])
            if a.length_squared and b.length_squared and a.dot(b) < cos_limit:
                bad.update(f)
        if not bad:
            break
        for i in bad:
            smooth[i] = plain[i] + (smooth[i] - plain[i]) * .5
        touched |= bad
    return smooth, len(touched)


def bake(armature, ob, side, key_name=KEY, pose=None, zone_name=ZONE, thumb_joints=None, extra=None, facing=(-.5, .1), guard=0.):
    """Ключ формы KEY на руке ob: в позе кулака кожа со сглаживанием минус кожа без него, переведённая в покой скелета.
    Скиннинг линеен по положению вершины в покое, поэтому сдвиг в покое — обратная матрица скиннинга вершины,
    умноженная на нужный сдвиг в позе; матрица снимается тремя пробными сдвигами по осям.
    Форма другой позы (gesture_smooth.py, 2026-11-20): key_name, pose, zone_name, thumb_joints и extra — свой сдвиг кожи
    вместо объёма web кулака (функция с теми же аргументами, что web). Без них всё как прежде."""
    if ob.data.shape_keys is None:
        ob.shape_key_add(name="Basis", from_mix=False)
    # Прежняя форма перезаписывается на месте, а не удаляется: удаление сдвигает следующие ключи, и драйвер следующего
    # (FistThumb после ThumbsSmooth) падал в Blender при пересчёте (2026-11-22). На время подсчёта её вес 0.
    old = ob.data.shape_keys.key_blocks.get(key_name)
    driver = None
    if old is not None:
        if ob.data.shape_keys.animation_data:
            driver = ob.data.shape_keys.animation_data.drivers.find('key_blocks["%s"].value' % key_name)
        if driver is not None:
            driver.mute = True
        old.value = 0.
    basis = [k.co.copy() for k in ob.data.shape_keys.key_blocks[0].data]
    reset(armature)
    group = zone(ob, armature, side, zone_name, thumb_joints, facing)
    import hand_anatomy
    hand_anatomy.apply(armature, gestures.FIST if pose is None else pose, side)
    plain = coords(ob)
    mod = ob.modifiers.new("FistSmoothBake", "CORRECTIVE_SMOOTH")
    mod.smooth_type, mod.rest_source, mod.use_pin_boundary = "LENGTH_WEIGHTED", "ORCO", False
    mod.iterations, mod.factor, mod.vertex_group = ITERATIONS, FACTOR, group.name
    smooth, normals = coords(ob, normals=True)
    ob.modifiers.remove(mod)
    smooth = [p + s for p, s in zip(smooth, (extra or web)(ob, armature, side, smooth, normals))]
    # guard — без вывернутых граней (unfold): грань не поворачивается от себя без сглаживания больше чем на guard градусов;
    # прежние формы считаются без этого (0), как раньше.
    if guard:
        smooth, guarded = unfold(ob, plain, smooth, guard)
        print("=== FIST SMOOTH %s: вывернутых граней не осталось, сдвиг уменьшен у %d вершин" % (ob.name, guarded))
    step = .001
    columns = []
    for axis in range(3):
        probe = ob.shape_key_add(name="probe", from_mix=False)
        shift = Vector((0, 0, 0))
        shift[axis] = step
        for k, co in zip(probe.data, basis):
            k.co = co + shift
        probe.value = 1.
        columns.append([(a - b) / step for a, b in zip(coords(ob), plain)])
        ob.shape_key_remove(probe)
    key = old if old is not None else ob.shape_key_add(name=key_name, from_mix=False)
    for k, co in zip(key.data, basis):
        k.co = co
    moved = 0
    for i, (co, a, b) in enumerate(zip(basis, plain, smooth)):
        delta = b - a
        if delta.length < 1e-7:
            continue
        m = Matrix((columns[0][i], columns[1][i], columns[2][i])).transposed()
        key.data[i].co = co + m.inverted_safe() @ delta
        moved += 1
    key.value = 1.
    error = max((a - b).length for a, b in zip(coords(ob), smooth)) * 1000
    key.value = 0.
    print("=== FIST SMOOTH %s: вершин сдвинуто %d, наибольший сдвиг %.1f мм, расхождение формы со сглаживанием %.3f мм"
          % (ob.name, moved, max((a - b).length for a, b in zip(plain, smooth)) * 1000, error))
    if driver is not None:
        driver.mute = False
    reset(armature)
    return key


def drive(armature, key, prop=PROP):
    """Вес формы — свойство скелета PROP: его ключи в клипах выгружаются в FBX как кривая веса формы."""
    armature[prop] = 0.
    key.driver_remove("value")
    driver = key.driver_add("value").driver
    driver.type = "AVERAGE"
    var = driver.variables.new()
    var.name, var.type = "fist", "SINGLE_PROP"
    var.targets[0].id_type, var.targets[0].id, var.targets[0].data_path = "OBJECT", armature, '["%s"]' % prop


def load_light():
    """Пустой Blender: только скелет и правая рука, без скульптур — так хватает памяти при открытом Unity."""
    with bpy.data.libraries.load(os.path.abspath(BLEND), link=False) as (src, dst):
        dst.objects = ["Rig_Hands", "Arm_Male_R"]
    for o in dst.objects:
        bpy.context.scene.collection.objects.link(o)
    for img in list(bpy.data.images):
        bpy.data.images.remove(img)


def cavity(armature, ob, skin, deep=10., size=.11, step=.0015):
    """Дыра в кулаке со стороны большого пальца: площадь (мм²), где взгляд упирается в кожу кисти глубже её выпуклой
    оболочки на deep мм. Ракурсы в осях кисти: фото автора, сбоку от большого пальца, с места игрока в проверке F."""
    import bmesh
    from mathutils.bvhtree import BVHTree
    co, _ = coords(ob, normals=True)
    co = [ob.matrix_world @ p for p in co]
    polys = [tuple(p.vertices) for p in ob.data.polygons]
    skin.shot()
    hand_idx = [i for i, o in enumerate(skin.owner) if o is not None]
    hand_set = set(hand_idx)
    hand_faces = {k for k, f in enumerate(polys) if all(i in hand_set for i in f)}
    tree = BVHTree.FromPolygons(co, polys)
    bm = bmesh.new()
    bmesh.ops.convex_hull(bm, input=[bm.verts.new(co[i]) for i in hand_idx])
    hull = BVHTree.FromBMesh(bm)
    bm.free()
    center = sum((co[i] for i in hand_idx), Vector()) / len(hand_idx)
    import hand_anatomy
    toward, along, back = hand_anatomy.hand_axes(armature.pose.bones["Hand_R"].matrix, "_R")
    result = {}
    seen = {}
    for name, (x, y, z) in (("фото", (.9, .3, .4)), ("сбоку", (1., .1, .1)), ("игра", (.6, -.4, .7))):
        d = (toward * x + along * y + back * z).normalized()
        u = d.orthogonal().normalized()
        v = d.cross(u).normalized()
        n, area = int(size / step), 0.
        for i in range(-n, n + 1):
            for j in range(-n, n + 1):
                origin = center + u * i * step + v * j * step + d * .2
                outer = hull.ray_cast(origin, -d, .5)
                inner = tree.ray_cast(origin, -d, .5)
                if outer[0] is None or inner[0] is None or inner[2] not in hand_faces:
                    continue
                if (inner[0] - outer[0]).length * 1000 > deep:
                    area += (step * 1000) ** 2
                    owner = skin.owner[polys[inner[2]][0]]
                    seen[(name, owner)] = seen.get((name, owner), 0) + 1
        result[name] = area
    if os.environ.get("FS_CAVITY_PARTS"):
        for name in result:
            parts = sorted(((c, o) for (nm, o), c in seen.items() if nm == name), reverse=True)[:8]
            print("=== CAVITY %s в глубине видно: %s" % (name, ", ".join("%s %d%%" % ("%s%s" % o, 100 * c // max(1, sum(x for x, _ in parts))) for c, o in parts)))
    return result


def palm_into(skin, body):
    """На сколько кожа ладони и основания большого пальца уходит внутрь пальца body (мм): объём между большим и
    указательным пальцами не должен входить в указательный."""
    from gesture_lab import body_of
    tree, worst = skin.trees[body], 0.
    for point, owner in zip(skin.co, skin.owner):
        if owner is None or body_of(owner) != "Palm" or owner == ("Thumb", 1) and body == "Thumb":
            continue
        location, _, _, distance = tree.find_nearest(point, .02)
        if location is not None and skin.is_inside(point, body):
            worst = max(worst, distance * 1000)
    return worst


def measure():
    import fist_fit
    import gesture_lab
    from gesture_lab import Skin
    if "Rig_Hands" not in bpy.data.objects:
        load_light()
    armature = bpy.data.objects["Rig_Hands"]
    armature.animation_data_create()
    armature.animation_data.action = None
    armature.matrix_world.identity()
    ob = bpy.data.objects["Arm_Male_R"]
    key = bake(armature, ob, "_R")
    skin = Skin(armature, "_R")
    for value in (0., 1.):
        key.value = value
        m = fist_fit.measure(armature, skin, gestures.FIST)
        fist_fit.show("FIST w=%.0f" % value, m)
        into = {body: palm_into(skin, body) for body in ("Index", "Middle", "Thumb")}
        print("=== PALM w=%.0f кожа ладони и основания большого пальца в пальцах: %s"
              % (value, ", ".join("%s %.1f мм" % kv for kv in into.items())))
        print("=== CAVITY w=%.0f дыра со стороны большого пальца глубже 10 мм: %s" % (value, ", ".join(
            "%s %.0f мм²" % (name, area) for name, area in cavity(armature, ob, skin).items())))
    # Переход: кожа палец в палец и большой палец в пальцах на кадрах сжатия, с весом формы этого кадра и без.
    for frame in range(5, gestures.CLOSED + 1):
        row = []
        for value in (0., weight(frame)):
            key.value = value
            m = fist_fit.measure(armature, skin, gestures.closing(frame))
            row.append((max(m["deep"].values()), m["thumb_in"], m["fingers_in"]))
        print("=== FRAME %2d вес %.2f: палец в палец %.1f → %.1f мм, большой в пальцах %.1f → %.1f, пальцы в большом %.1f → %.1f"
              % (frame, weight(frame), row[0][0], row[1][0], row[0][1], row[1][1], row[0][2], row[1][2]))
    key.value = 0.
    gesture_lab.reset(armature)


def store():
    armature = bpy.data.objects["Rig_Hands"]
    saved_action = armature.animation_data.action if armature.animation_data else None
    saved_matrix = armature.matrix_world.copy()
    armature.animation_data_create()
    armature.animation_data.action = None
    for name in ARMS:
        key = bake(armature, bpy.data.objects[name], name[-2:])
        drive(armature, key)
    armature.animation_data.action = saved_action
    armature.matrix_world = saved_matrix
    bpy.ops.wm.save_mainfile()
    print("=== FIST SMOOTH сохранено", bpy.data.filepath)


if __name__ == "__main__":
    command = (sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else ["measure"])[0]
    {"measure": measure, "store": store}[command]()
