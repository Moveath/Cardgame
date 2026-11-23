"""Палец как у живой руки: поза кисти задаётся анатомией, а не углами костей скелета.

Зачем. В покое пальцы модели разведены веером (указательный на 7° к большому, мизинец на 34° от него), каждый чуть
повёрнут вокруг себя, ладонь сложена лодочкой. Если просто гнуть кости вокруг их осей, палец складывается туда, куда
смотрел в покое: в кулаке пальцы расходятся, входят друг в друга или всем рядом уезжают вбок. У живой руки иначе:
- три сустава пальца гнутся в одной плоскости, ось сгиба идёт поперёк ногтя — ноготь остаётся снаружи сгиба;
- при сгибе основания палец сводится к соседям (боковые связки натягиваются): в кулаке плоскости пальцев почти
  параллельны, развода уже нет;
- большой палец поворачивается вокруг себя только в основании (седловидный сустав), два остальных сустава — простые
  шарниры.

Поэтому поза пальца здесь — пять чисел в осях кисти:
  (основание, средний сустав, дальний сустав, курс, наклон), градусы.
  Сгибы — настоящие углы между фалангами: 0 — палец прямой, 90 в основании — прямой угол к ладони.
  Курс — куда смотрит прямой палец в плоскости ладони: 0 — вдоль кисти, плюс — к большому пальцу.
  Наклон — поворот плоскости сгиба вокруг пальца: 0 — палец гнётся прямо к ладони, плюс — кончиком к большому пальцу.
Большой палец:
  thumb — точка в осях кисти (миллиметры от запястья: к большому пальцу, вдоль кисти, к тылу), куда приходит его
          пястно-фаланговый сустав: так задаётся, куда отведено основание;
  thumb_turn — разворот основания вокруг себя подушечкой к пальцам, градусы;
  thumb_bend — (сгиб пястно-фалангового сустава, его отклонение вбок, сгиб межфалангового сустава), градусы от позы
          покоя модели. Межфаланговый сустав вбок не гнётся совсем.
  Раньше большой палец наводили по трём точкам с заданным направлением ногтя: основание скручивалось на 110–130°
  (кожа у запястья шла зубцами), а межфаланговый сустав гнулся вбок на 19°.
cup — поворот пястной кости мизинца к ладони (безымянного — вдвое меньше); минус — от ладони.

Кости ставятся матрицами (как большой палец в in_game.py), в клип идут обычные ключи поворота. От крена костей
результат не зависит: у каждой фаланги свои анатомические оси (Y вдоль кости, X — ось сгиба поперёк ногтя, Z — тыл),
снятые с модели в покое."""
import math

import bpy
from mathutils import Euler, Matrix, Vector

import poses

FINGERS = ("Index", "Middle", "Ring", "Pinky")
NUMBERS = ("01", "02", "03")
THUMB = ("Thumb01", "Thumb02", "Thumb03")
# В какую сторону вокруг оси кости разворот основания большого пальца ведёт подушечку к пальцам (для левой руки).
THUMB_TURN_SIGN = 1
# Доля расчётного выступа костяшки по суставам: основание пальца, средний, дальний. Средний сустав выступает сильнее,
# чем в прежних позах (poses.KNUCKLE_GAIN, 0,2): в кулаке средние суставы — нижний край кулака, с меньшим выступом они
# выглядели скруглёнными, как торцы трубок.
KNUCKLE_GAIN = {"01": .55, "02": .35, "03": .25}
# Пределы живой руки для новых поз: сгибы пальцев — настоящие углы между фалангами (в покое модели они уже 5–25°).
# Разворот основания большого пальца — до 50°, как даёт седловидный сустав; отведение основания от покоя дальше 45°
# рвёт кожу на тыле кисти (THUMB_SWING) — об этом предупреждает apply. Сгибы большого пальца — от позы покоя.
LIMITS = {"mcp": (-20, 108), "pip": (0, 115), "dip": (0, 80), "heading": (-32, 20), "tilt": (-25, 25), "cup": (-10, 25),
          "thumb_turn": (-15, 50), "thumb_mcp": (-25, 45), "thumb_side": (-12, 12), "thumb_ip": (-15, 85)}
THUMB_SWING = 45
# Позы, вышедшие за предел отведения основания большого пальца: {сторона: градусы}. Печатают стенд и выгрузка.
OVER_SWING = {}
_rest = {}


def side_sign(side):
    return 1 if side == "_L" else -1


def frame(x, y, z):
    return Matrix((x, y, z)).transposed()


def rest_frames(armature, finger, side):
    """Анатомические оси трёх фаланг в покое, в осях скелета. У средней и дальней фаланг это оси кости до правки крена
    (ось сгиба поперёк ногтя, fix_hinges.py). У основной фаланги своей метки нет: палец вокруг себя жёсткий, поэтому
    её ось сгиба — ось средней фаланги, перенесённая на неё."""
    key = (armature.name, finger, side)
    if key not in _rest:
        bones = armature.data.bones
        second, third = (bones[finger + n + side].matrix_local.to_3x3() @ poses.flex_turn(bones[finger + n + side]).inverted() for n in ("02", "03"))
        y = bones[finger + "01" + side].matrix_local.to_3x3().col[1].normalized()
        x = second.col[0] - y * second.col[0].dot(y)
        x.normalize()
        _rest[key] = (frame(x, y, x.cross(y)), second, third)
    return _rest[key]


def hand_axes(matrix, side):
    """Оси кисти по матрице кости Hand: к большому пальцу, вдоль кисти, к тылу."""
    m = matrix.to_3x3()
    return m.col[0].normalized() * side_sign(side), m.col[1].normalized(), m.col[2].normalized()


def finger_frames(axes, mcp, pip, dip, heading, tilt):
    """Оси трёх фаланг пальца в позе: одна плоскость сгиба на все три сустава."""
    toward, along, back = axes
    h, t = math.radians(heading), math.radians(tilt)
    straight = along * math.cos(h) + toward * math.sin(h)
    aside = toward * math.cos(h) - along * math.sin(h)
    dorsal = back * math.cos(t) - aside * math.sin(t)
    frames, total = [], 0.
    for angle in (mcp, pip, dip):
        total += math.radians(angle)
        y = straight * math.cos(total) - dorsal * math.sin(total)
        z = dorsal * math.cos(total) + straight * math.sin(total)
        frames.append(frame(y.cross(z), y, z))
    return frames


def rest_pose(armature, side):
    """Поза покоя модели теми же числами, что позы: по ней видно, от чего считать расслабленную кисть."""
    bones = armature.data.bones
    hand = bones["Hand" + side].matrix_local
    toward, along, back = hand_axes(hand, side)
    fingers = {}
    for finger in FINGERS:
        frames = rest_frames(armature, finger, side)
        hinge = sum((f.col[0] for f in frames), Vector()).normalized()
        straight = back.cross(hinge).normalized()
        if straight.dot(along) < 0:
            straight = -straight
        dorsal = hinge.cross(straight).normalized()
        if dorsal.dot(back) < 0:
            dorsal, hinge = -dorsal, -hinge
        heading = math.atan2(straight.dot(toward), straight.dot(along))
        aside = toward * math.cos(heading) - along * math.sin(heading)
        tilt = math.atan2(-dorsal.dot(aside), dorsal.dot(back))
        totals = [math.atan2(-f.col[1].dot(dorsal), f.col[1].dot(straight)) for f in frames]
        angles = (totals[0], totals[1] - totals[0], totals[2] - totals[1])
        fingers[finger] = tuple(round(math.degrees(a), 1) for a in angles + (heading, tilt))
    point = bones["Thumb01" + side].tail_local - hand.translation
    thumb = tuple(round(point.dot(axis) * 1000, 1) for axis in (toward, along, back))
    return {"fingers": fingers, "thumb": thumb, "thumb_turn": 0, "thumb_bend": (0, 0, 0), "cup": 0, "wrist": (0, 0)}


def clamp(value, key):
    low, high = LIMITS[key]
    return min(high, max(low, value))


def limited(pose):
    """Та же поза в пределах живой руки."""
    fingers = {name: (clamp(v[0], "mcp"), clamp(v[1], "pip"), clamp(v[2], "dip"), clamp(v[3], "heading"), clamp(v[4], "tilt"))
               for name, v in pose["fingers"].items()}
    bend = pose.get("thumb_bend", (0, 0, 0))
    return dict(pose, fingers=fingers, cup=clamp(pose.get("cup", 0), "cup"), thumb_turn=clamp(pose.get("thumb_turn", 0), "thumb_turn"),
                thumb_bend=(clamp(bend[0], "thumb_mcp"), clamp(bend[1], "thumb_side"), clamp(bend[2], "thumb_ip")))


def place(bone, rotation):
    """Ставит кость по осям в пространстве скелета: начало остаётся там, куда его привели родители."""
    matrix = rotation.to_4x4()
    matrix.translation = bone.head.copy()
    bone.matrix = matrix
    bone.location = (0, 0, 0)
    bpy.context.view_layer.update()


def natural_axes(bone):
    """Оси кости, какими они были бы без её собственного поворота (родители уже в позе)."""
    return (bone.parent.matrix @ bone.parent.bone.matrix_local.inverted() @ bone.bone.matrix_local).to_3x3()


def knuckles(armature, side):
    """Костяшки: промежуточная кость сустава Joint* сдвигается наружу сгиба, как в poses.knuckles, но по настоящему углу
    между фалангами и по биссектрисе угла — поза здесь задана не поворотом вокруг оси кости."""
    bones = armature.pose.bones
    for name in poses.KNUCKLE_JOINTS:
        helper = bones.get("Joint" + name + side)
        if helper is None:
            continue
        helper.location = (0, 0, 0)
        if not poses.KNUCKLES_ON:
            continue
        bone = bones[name + side]
        radius, rest_bend = poses.knuckle_rest(armature, name, side)
        before = (bone.parent.matrix.to_3x3().col[1]).normalized()
        after = (bone.matrix.to_3x3().col[1]).normalized()
        bend = before.angle(after)
        rest_bend = max(0., rest_bend)
        if radius <= 0 or bend <= rest_bend + 1e-4:
            continue
        total = min(poses.KNUCKLE_MAX_BEND, bend)
        offset = radius * (1 / math.cos(total / 2) - 1 / math.cos(rest_bend / 2)) * KNUCKLE_GAIN[name[-2:]]
        outward = (before - after).normalized()
        # Оси промежуточной кости без её собственного сдвига: в них задаётся положение кости в клипе.
        helper.location = natural_axes(helper).inverted() @ (outward * offset)
    bpy.context.view_layer.update()


def apply(armature, pose, side):
    """Ставит позу на одну руку. Предплечье и кисть остаются как есть (их ставит in_game.py или поза покоя):
    меняются запястье (если задано), пястные кости, пальцы и большой палец."""
    pose = limited(pose)
    bones = armature.pose.bones
    sign = side_sign(side)
    for bone in bones:
        if bone.name.endswith(side):
            bone.rotation_mode = "XYZ"
    flex, deviate = pose.get("wrist", (0, 0))
    if "wrist" in pose:
        bones["Hand" + side].rotation_euler = Euler((-math.radians(flex), 0, math.radians(deviate) * sign))
    cup = pose.get("cup", 0)
    bones["MetaPinky" + side].rotation_euler = Euler((-math.radians(cup), 0, 0))
    bones["MetaRing" + side].rotation_euler = Euler((-math.radians(cup / 2), 0, 0))
    bpy.context.view_layer.update()
    hand = bones["Hand" + side]
    axes = hand_axes(hand.matrix, side)
    for finger, values in pose["fingers"].items():
        frames = finger_frames(axes, *values)
        for number, target, rest in zip(NUMBERS, frames, rest_frames(armature, finger, side)):
            bone = bones[finger + number + side]
            # Из анатомических осей фаланги в оси её кости: постоянный поворот, снятый с покоя.
            place(bone, target @ rest.inverted() @ bone.bone.matrix_local.to_3x3())
    # Большой палец. Основание наводится на точку кратчайшим поворотом и разворачивается вокруг себя.
    toward, along, back = axes
    point = pose["thumb"]
    target = hand.head + (toward * point[0] + along * point[1] + back * point[2]) / 1000
    base = bones["Thumb01" + side]
    natural = natural_axes(base)
    # thumb_base — сдвиг основания большого пальца (мм в осях кисти), только в кулаке (gestures.FIST): кости пальца
    # прежней длины, но основание с мышцами под ним уходит к ладони, как у живой руки в плотном кулаке. Без сдвига
    # кость ставится как прежде, до бита.
    shift = pose.get("thumb_base", (0, 0, 0))
    if any(shift):
        base.location = (0, 0, 0)
        bpy.context.view_layer.update()
        head = base.head + (toward * shift[0] + along * shift[1] + back * shift[2]) / 1000
    else:
        head = base.head
    aim = (target - head).normalized()
    swing = natural.col[1].normalized().rotation_difference(aim).to_matrix()
    turn = Matrix.Rotation(math.radians(pose.get("thumb_turn", 0)) * sign * THUMB_TURN_SIGN, 3, aim)
    if any(shift):
        matrix = (turn @ swing @ natural).to_4x4()
        matrix.translation = head
        base.matrix = matrix
        bpy.context.view_layer.update()
    else:
        place(base, turn @ swing @ natural)
    angle = math.degrees(natural.col[1].angle(aim))
    if angle > THUMB_SWING + .5:
        OVER_SWING[side] = max(OVER_SWING.get(side, 0.), round(angle, 1))
    # Два сустава-шарнира: сгиб вокруг оси X кости; пястно-фаланговый ещё немного отклоняется вбок (вокруг Z).
    bend, aside, tip = pose.get("thumb_bend", (0, 0, 0))
    second = bones["Thumb02" + side]
    place(second, natural_axes(second) @ Euler((-math.radians(bend), 0, math.radians(aside) * sign), "XYZ").to_matrix())
    third = bones["Thumb03" + side]
    place(third, natural_axes(third) @ Euler((-math.radians(tip), 0, 0), "XYZ").to_matrix())
    knuckles(armature, side)


def thumb_report(armature, side):
    """Большой палец в текущей позе числами: на сколько основание отведено от покоя и развёрнуто вокруг себя, сгиб двух
    суставов (настоящий угол между костями) и их отклонение вбок."""
    bones = armature.pose.bones
    result = {}
    base = bones["Thumb01" + side]
    natural = natural_axes(base)
    posed = base.matrix.to_3x3()
    result["swing"] = math.degrees(natural.col[1].angle(posed.col[1]))
    swing = natural.col[1].normalized().rotation_difference(posed.col[1].normalized()).to_matrix() @ natural
    result["turn"] = math.degrees(swing.col[0].angle(posed.col[0]))
    for name in ("Thumb02", "Thumb03"):
        bone = bones[name + side]
        # Поворот сустава в осях кости: вокруг X — сгиб, вокруг Z — отклонение вбок.
        local = bone.matrix_basis.to_3x3() @ Vector((0, 1, 0))
        rest = bone.bone.matrix_local.to_3x3().inverted() @ bone.bone.parent.matrix_local.to_3x3() @ Vector((0, 1, 0))
        result[name] = (math.degrees(math.atan2(-local.z, local.y)) + math.degrees(math.atan2(rest.z, rest.y)),
                        -math.degrees(math.asin(max(-1., min(1., local.x)))) * side_sign(side))
    return result


def mix(a, b, t):
    """Поза между двумя позами: числа пальцев и большого пальца смешиваются сами."""
    def lerp(x, y):
        if isinstance(x, (int, float)):
            return x + (y - x) * t
        if isinstance(x, tuple):
            return tuple(lerp(p, q) for p, q in zip(x, y))
        return {k: lerp(x[k], y[k]) for k in x}
    return lerp(a, b)
