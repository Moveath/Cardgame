"""Скелет обеих рук и веса вершин. Варианты рук с той же сеткой получают те же веса и те же анимации.

Оси костей: Y вдоль кости, Z к тылу пальца. Сгибание к ладони — поворот вокруг X в минус, одинаково у левой и правой руки.

У каждого сустава кисти есть промежуточная кость Joint*: она стоит в суставе и поворачивается на половину угла следующей кости.
Кожа сустава переходит с кости на промежуточную, затем на следующую, и на каждом стыке поворот вдвое меньше. Без этого
линейный скиннинг при сгибе на 90–100° стягивает сустав в плоскую ленту: так было в кулаке."""
import bpy
import numpy as np
from mathutils import Vector

import joints as J

FINGER_NAMES = J.FINGERS
SPAN_DIP, SPAN_PIP, SPAN_MCP = .006, .008, .012     # Полуширина зоны смешивания весов у суставов: шире — мягче складка.
DORSAL_STRETCH = .25                                 # С тыла зона шире (кожа тянется над костяшкой), с ладони уже (складка).
HELPER_PREFIX, HELPER_SHARE = "Joint", .5
SPAN_WRIST = .016
THUMB_REACH_NEAR, THUMB_REACH_FAR = .012, .034      # Как далеко от пястной кости кожа тенара идёт за большим пальцем.
SOCKET_RING_SHARE = .52                              # Кольцо сидит посередине проксимальной фаланги.
MAX_INFLUENCES = 4
PAD_INSET, PAD_DEPTH = .007, .0045                    # Подушечка: чуть назад от кончика и к ладонной стороне.
WEB_SHARE = .42                                      # До этой доли проксимальной фаланги кожа делится с соседним пальцем.


def mirror(p):
    return np.array((-p[0], p[1], p[2]))


def unit(v):
    return v / np.linalg.norm(v)


def smooth(x):
    x = np.clip(x, 0., 1.)
    return x * x * (3 - 2 * x)


def finger_hinge(j):
    """Ось сгиба пальца: одна на все фаланги, иначе кулак собирается криво. Идёт поперёк ногтя: перпендикулярно дальней
    фаланге и нормали ногтя, как у живого пальца. Раньше ось бралась плоскостью через три сустава, но в покое палец почти
    прямой, и ошибка сустава на 1–2 мм вбок кренила ось на 5–29° (у среднего пальца 28°): в кулаке пальцы шли наискось,
    скручивались и входили друг в друга."""
    hand_side = np.array((1., 0., 0.))
    if "nail" in j:
        normal = np.cross(unit(j["tip"] - j["dip"]), j["nail"])
    else:
        normal = np.cross(j["pip"] - j["mcp"], j["dip"] - j["pip"])
    if np.linalg.norm(normal) < 1e-5:
        return hand_side
    normal = unit(normal)
    return normal if normal @ hand_side > 0 else -normal


def palm_hinge(j):
    """Ось основания пальца (пястная кость и основная фаланга): перпендикулярно основной фаланге и тылу кисти (+Z).
    Палец гнётся к ладони по её плоскости, и в кулаке основные фаланги идут рядком, в порядке костяшек. Средний и
    дальний суставы гнутся поперёк ногтя (finger_hinge): у безымянного и мизинца ноготь повёрнут к большому пальцу,
    поэтому кончики сходятся к его основанию, как у живой руки."""
    hand_side = np.array((1., 0., 0.))
    normal = np.cross(unit(j["pip"] - j["mcp"]), np.array((0., 0., 1.)))
    if np.linalg.norm(normal) < 1e-5:
        return hand_side
    normal = unit(normal)
    return normal if normal @ hand_side > 0 else -normal


def helper_bones():
    """Промежуточные кости суставов: имя, родитель, кость за суставом. Стоят в начале кости за суставом и повторяют её
    положение покоя; в позе поворачиваются на HELPER_SHARE её поворота (ограничение Copy Rotation в Blender,
    HandPoseConstraint в Unity)."""
    result = [(HELPER_PREFIX + "Hand", "ForearmTwist", "Hand")]
    for name in FINGER_NAMES:
        result.append((HELPER_PREFIX + name + "01", "Meta" + name, name + "01"))
        result.append((HELPER_PREFIX + name + "02", name + "01", name + "02"))
        result.append((HELPER_PREFIX + name + "03", name + "02", name + "03"))
    for number, parent in (("01", "Hand"), ("02", "Thumb01"), ("03", "Thumb02")):
        result.append((HELPER_PREFIX + "Thumb" + number, parent, "Thumb" + number))
    return result


def bone_list(data, thumb_back, elbow, wrist):
    """Кости левой руки: имя, родитель, начало, конец, направление тыла кости, деформирует ли кость."""
    up = np.array((0., 0., 1.))
    middle = data["fingers"]["Middle"]
    mid_forearm = (elbow + wrist) / 2
    bones = [
        ("Forearm", None, elbow, mid_forearm, up, True),
        ("ForearmTwist", "Forearm", mid_forearm, wrist, up, True),
        ("Hand", "ForearmTwist", wrist, middle["mcp"], up, True),
    ]
    for name in FINGER_NAMES:
        j = data["fingers"][name]
        hinge, base_hinge = finger_hinge(j), palm_hinge(j)
        back = lambda head, tail, axis=hinge: np.cross(axis, unit(tail - head))
        bones.append(("Meta" + name, "Hand", j["base"], j["mcp"], back(j["base"], j["mcp"], base_hinge), True))
        bones.append((name + "01", "Meta" + name, j["mcp"], j["pip"], back(j["mcp"], j["pip"], base_hinge), True))
        bones.append((name + "02", name + "01", j["pip"], j["dip"], back(j["pip"], j["dip"]), True))
        bones.append((name + "03", name + "02", j["dip"], j["tip"], back(j["dip"], j["tip"]), True))
    t = data["thumb"]
    bones.append(("Thumb01", "Hand", t["cmc"], t["mcp"], thumb_back, True))
    bones.append(("Thumb02", "Thumb01", t["mcp"], t["ip"], thumb_back, True))
    bones.append(("Thumb03", "Thumb02", t["ip"], t["tip"], thumb_back, True))
    # Подушечки большого, указательного и среднего пальцев: между ними зажимается карта, на них же сидят коллайдеры.
    pads = [("TipThumb", "Thumb03", t["ip"], t["tip"], np.asarray(thumb_back, float))]
    for name in ("Index", "Middle"):
        j = data["fingers"][name]
        pads.append(("Tip" + name, name + "03", j["dip"], j["tip"], np.cross(finger_hinge(j), unit(j["tip"] - j["dip"]))))
    for name, parent, joint, tip, back in pads:
        direction = unit(tip - joint)
        pad = tip - direction * PAD_INSET - unit(back) * PAD_DEPTH
        bones.append((name, parent, pad, pad + direction * .012, back, False))
    # Точки крепления: на них надеваются часы, кольца и карта, скелет от этого не меняется.
    length = .03
    watch = wrist + (elbow - wrist) * .09 + up * .03
    bones.append(("SocketWatch", "ForearmTwist", watch, watch + unit(wrist - elbow) * length, up, False))
    palm = (middle["mcp"] + wrist) / 2 - up * .02
    bones.append(("SocketCard", "Hand", palm, palm + unit(middle["mcp"] - wrist) * length, up, False))
    for name in FINGER_NAMES:
        j = data["fingers"][name]
        at = j["mcp"] + (j["pip"] - j["mcp"]) * SOCKET_RING_SHARE
        direction = unit(j["pip"] - j["mcp"])
        bones.append(("SocketRing" + name, name + "01", at, at + direction * length, np.cross(palm_hinge(j), direction), False))
    return bones


def build_armature(data, thumb_back, elbow, wrist):
    arm = bpy.data.armatures.new("Rig_Hands")
    arm.display_type = "OCTAHEDRAL"
    ob = bpy.data.objects.new("Rig_Hands", arm)
    bpy.context.scene.collection.objects.link(ob)
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    root = arm.edit_bones.new("Root")
    root.head, root.tail = (0, 0, 0), (0, .1, 0)
    root.roll = 0
    root.use_deform = False
    for suffix, place in (("_L", lambda p: p), ("_R", mirror)):
        created = {}
        for name, parent, head, tail, back, deform in bone_list(data, thumb_back, elbow, wrist):
            bone = arm.edit_bones.new(name + suffix)
            bone.head, bone.tail = Vector(place(head)), Vector(place(tail))
            bone.align_roll(Vector(place(np.asarray(back, float))))
            bone.parent = created[parent] if parent else root
            bone.use_deform = deform
            created[name] = bone
        for name, parent, child in helper_bones():
            source = created[child]
            bone = arm.edit_bones.new(name + suffix)
            bone.head = source.head.copy()
            bone.tail = source.head + (source.tail - source.head) * .5
            bone.roll = source.roll
            bone.parent = created[parent]
            bone.use_deform = True
    bpy.ops.object.mode_set(mode="OBJECT")
    ob.select_set(False)
    for suffix in ("_L", "_R"):
        for name, parent, child in helper_bones():
            constraint = ob.pose.bones[name + suffix].constraints.new("COPY_ROTATION")
            constraint.target = ob
            constraint.subtarget = child + suffix
            constraint.owner_space = constraint.target_space = "LOCAL"
            constraint.mix_mode = "REPLACE"
            constraint.influence = HELPER_SHARE
    return ob


def segment_param(points, starts, ends):
    """Для каждой точки: положение проекции на отрезке (0..1) и расстояние до него."""
    axis = ends - starts
    t = np.clip(((points - starts) * axis).sum(axis=-1) / (axis * axis).sum(axis=-1), 0., 1.)
    nearest = starts + axis * t[..., None]
    return t, np.linalg.norm(points - nearest, axis=-1)


def along_chain(points, chain):
    """Длина пути вдоль цепочки суставов до ближайшей точки на ней и расстояние до цепочки."""
    best = np.full(len(points), 1e9)
    arc = np.zeros(len(points))
    walked = 0.
    for a, b in zip(chain[:-1], chain[1:]):
        t, d = segment_param(points, a[None, :], b[None, :])
        better = d < best
        best[better] = d[better]
        arc[better] = walked + t[better] * np.linalg.norm(b - a)
        walked += np.linalg.norm(b - a)
    return arc, best


def split_chain(arc, marks, spans):
    """Доли костей цепочки по положению вдоль неё: [кость 0, сустав 1, кость 1, сустав 2, кость 2, ...].
    У каждого сустава кость передаёт кожу промежуточной кости сустава (до его середины), та — следующей кости.
    spans — полуширины зон, числа или массивы на каждую вершину."""
    ramps = []
    for m, s in zip(marks, spans):
        ramps.append(smooth((arc - (m - s)) / s))
        ramps.append(smooth((arc - m) / s))
    shares = [1 - ramps[0]]
    for i in range(len(ramps) - 1):
        shares.append(ramps[i] - ramps[i + 1])
    shares.append(ramps[-1])
    # Зоны соседних суставов не должны залезать друг на друга; если залезли на коротком пальце — без отрицательных долей.
    shares = np.clip(np.array(shares, float), 0., None)
    return list(shares / np.maximum(shares.sum(axis=0), 1e-6))


def dorsal_side(points, chain, back):
    """Где вершина на обхвате пальца: +1 на тыле, -1 на ладонной стороне."""
    best = np.full(len(points), 1e9)
    offset = np.zeros_like(points)
    for a, b in zip(chain[:-1], chain[1:]):
        t, d = segment_param(points, a[None, :], b[None, :])
        nearest = a[None, :] + (b - a)[None, :] * t[:, None]
        better = d < best
        best[better] = d[better]
        offset[better] = (points - nearest)[better]
    return np.clip(offset @ back / np.maximum(best, 1e-4), -1., 1.)


def weights_for(co, sets_of_vert, data, elbow, wrist, suffix):
    """Веса одной руки. На вход — точки руки в её собственном положении: суставы уже приведены к этой стороне."""
    count = len(co)
    table = {}

    def add(bone, values):
        table[bone + suffix] = table.get(bone + suffix, np.zeros(count)) + values

    forearm_dir = unit(wrist - elbow)
    along_wrist = (co - wrist) @ forearm_dir
    into_wrist = smooth((along_wrist + SPAN_WRIST) / SPAN_WRIST)
    hand_share = smooth(along_wrist / SPAN_WRIST)
    forearm_t = np.clip(((co - elbow) @ forearm_dir) / np.linalg.norm(wrist - elbow), 0., 1.)
    twist_share = smooth((forearm_t - .3) / .55)
    add("Forearm", (1 - into_wrist) * (1 - twist_share))
    add("ForearmTwist", (1 - into_wrist) * twist_share)
    add(HELPER_PREFIX + "Hand", into_wrist - hand_share)

    thumb = data["thumb"]
    thumb_chain = [thumb["cmc"], thumb["mcp"], thumb["ip"], thumb["tip"]]
    thumb_arc, thumb_gap = along_chain(co, thumb_chain)
    thumb_marks = [np.linalg.norm(thumb["mcp"] - thumb["cmc"]), np.linalg.norm(thumb["mcp"] - thumb["cmc"]) + np.linalg.norm(thumb["ip"] - thumb["mcp"])]
    thumb_only = np.isin(sets_of_vert, J.THUMB_SETS[1:])
    thumb_near = 1 - smooth((thumb_gap - THUMB_REACH_NEAR) / (THUMB_REACH_FAR - THUMB_REACH_NEAR))
    thumb_near = np.where(thumb_only, 1., thumb_near)
    # Кожа предплечья и пальцев за большим пальцем не идёт.
    thumb_near = np.where(np.isin(sets_of_vert, [s for f in J.FINGERS for s in J.FINGER_SETS[f]]), 0., thumb_near)
    # Тенар: от кисти кожа переходит к промежуточной кости основания большого пальца, ближе к пальцу — к нему самому.
    thumb_joint_in = smooth(2 * thumb_near)
    thumb_share = smooth(2 * thumb_near - 1) * hand_share
    add(HELPER_PREFIX + "Thumb01", thumb_joint_in * hand_share - thumb_share)
    shares = split_chain(thumb_arc, thumb_marks, (.009, .006))
    names = ("Thumb01", HELPER_PREFIX + "Thumb02", "Thumb02", HELPER_PREFIX + "Thumb03", "Thumb03")
    for bone, share in zip(names, shares):
        add(bone, thumb_share * share)

    rest = hand_share * (1 - thumb_joint_in)
    fingers = data["fingers"]
    gaps = []
    for name in FINGER_NAMES:
        j = fingers[name]
        _, gap = along_chain(co, [j["mcp"], j["pip"], j["dip"], j["tip"]])
        gaps.append(gap)
    gaps = np.maximum(np.array(gaps), 1e-4)
    owner = gaps ** -6.
    owner = owner / owner.sum(axis=0)
    for index, name in enumerate(FINGER_NAMES):
        # У самой перепонки кожа общая для соседних пальцев: жёсткая граница дала бы лоскут, когда один палец согнут, а другой нет.
        j = fingers[name]
        arc, _ = along_chain(co, [j["mcp"], j["pip"]])
        share = arc / np.linalg.norm(j["pip"] - j["mcp"])
        mine = np.isin(sets_of_vert, J.FINGER_SETS[name]) & (share > WEB_SHARE)
        owner[:, mine] = 0.
        owner[index, mine] = 1.
    for index, name in enumerate(FINGER_NAMES):
        j = fingers[name]
        chain = [j["base"], j["mcp"], j["pip"], j["dip"], j["tip"]]
        arc, _ = along_chain(co, chain)
        lengths = np.linalg.norm(np.diff(np.array(chain), axis=0), axis=1)
        marks = np.cumsum(lengths)[:3]
        back = np.cross(finger_hinge(j), unit(j["tip"] - j["mcp"]))
        stretch = 1 + DORSAL_STRETCH * dorsal_side(co, chain[1:], back)
        shares = split_chain(arc, marks, (SPAN_MCP * stretch, SPAN_PIP * stretch, SPAN_DIP * stretch))
        # Пястная кость держит только дальнюю часть ладони: у запястья кисть жёсткая.
        meta_t = smooth((arc / lengths[0] - .25) / .55)
        part = rest * owner[index]
        add("Hand", part * shares[0] * (1 - meta_t))
        add("Meta" + name, part * shares[0] * meta_t)
        names = (HELPER_PREFIX + name + "01", name + "01", HELPER_PREFIX + name + "02", name + "02", HELPER_PREFIX + name + "03", name + "03")
        for bone, share in zip(names, shares[1:]):
            add(bone, part * share)
    return table


def trim(table):
    """Не больше четырёх костей на вершину: столько берёт Unity по умолчанию."""
    names = list(table)
    matrix = np.array([table[n] for n in names])
    order = np.argsort(-matrix, axis=0)
    limit = np.take_along_axis(matrix, order[MAX_INFLUENCES - 1:MAX_INFLUENCES], axis=0)
    matrix = np.where(matrix >= limit, matrix, 0.)
    matrix = matrix / np.maximum(matrix.sum(axis=0), 1e-6)
    return names, matrix


def apply_weights(ob, names, matrix, armature):
    for index, name in enumerate(names):
        group = ob.vertex_groups.new(name=name)
        row = matrix[index]
        for vertex in np.nonzero(row > 1e-4)[0]:
            group.add([int(vertex)], float(row[vertex]), "REPLACE")
    modifier = ob.modifiers.new("Armature", "ARMATURE")
    modifier.object = armature
    ob.parent = armature
