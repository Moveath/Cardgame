"""Тестовые позы и задел под анимации. Поза задаётся числами, которые читаются как жест: углы сгиба фаланг в градусах.
Сгиб пальца: (развод, основание, средний сустав, дальний сустав). Развод положителен к большому пальцу.

Оси костей. 2026-11-15 крен костей пальцев изменён (flex_converge.py), а числа всех поз здесь, в in_game.py и
animations.py подобраны по сетке под прежние оси. Поэтому поворот каждой фаланги пересчитывается из прежних осей кости
в нынешние (in_bone_axes): в пространстве это тот же поворот, кожа ложится как раньше. Проверка — compare_clips.py.
Базовые позы новой системы рук (gestures.py) от крена костей не зависят: они ставят фаланги по анатомии пальца."""
import math
import os

import bpy
from mathutils import Euler, Matrix, Vector

# Модель в покое и так слегка согнута: углы считаются от неё.
RELAXED_FINGERS = {"Index": (1, 6, 8, 4), "Middle": (0, 8, 10, 5), "Ring": (-1, 10, 12, 6), "Pinky": (-2, 12, 14, 6)}

# thumb: повороты основания вокруг X, Y, Z, затем сгиб двух дальних суставов.
# «Указательный» и «Взять карту» подобраны по сетке (fit_poses.py): пальцы не входят друг в друга.
POSES = {
    "Relaxed": {
        "fingers": RELAXED_FINGERS,
        "thumb": ((-10, 0, -8), 10, 12),
        "wrist": (0, 0),
    },
    "HoldCard": {
        "fingers": {"Index": (3, 34, 30, 16), "Middle": (0, 40, 34, 18), "Ring": (-2, 48, 40, 20), "Pinky": (-5, 56, 46, 22)},
        "thumb": ((-34, 10, -32), 24, 18),
        "wrist": (6, 0),
        "twist": -62,
        "prop": True,
    },
    "TakeCard": {
        "fingers": {"Index": (3, 44, 34, 18), "Middle": (0, 34, 24, 12), "Ring": (-3, 32, 24, 12), "Pinky": (-7, 30, 24, 12)},
        "thumb": ((-38, 12, -36), 22, 24),
        "wrist": (-22, 0),
        "prop": True,
    },
    "PlayCard": {
        "fingers": {"Index": (3, 30, 22, 12), "Middle": (0, 22, 18, 10), "Ring": (-3, 22, 18, 10), "Pinky": (-7, 22, 18, 10)},
        "thumb": ((-38, 10, -30), 14, 14),
        "wrist": (-30, 0),
        "prop": True,
    },
    "Point": {
        "fingers": {"Index": (1, 2, 4, 2), "Middle": (0, 92, 98, 75), "Ring": (0, 90, 94, 70), "Pinky": (0, 86, 90, 60)},
        "thumb": ((-24, 22, -24), 20, 34),
        "wrist": (4, 0),
    },
    "TapTable": {
        "fingers": {"Index": (2, -8, 14, 8), "Middle": (0, 26, 26, 12), "Ring": (-2, 30, 30, 14), "Pinky": (-5, 34, 32, 16)},
        "thumb": ((-16, 2, -12), 10, 10),
        "wrist": (-18, 0),
    },
    "Fist": {
        "fingers": {"Index": (0, 92, 94, 75), "Middle": (0, 92, 98, 75), "Ring": (0, 90, 94, 70), "Pinky": (0, 86, 90, 60)},
        "thumb": ((-30, 30, -14), 18, 45),
        "wrist": (0, 0),
    },
}

# Жёсткие пределы суставов, градусы от позы покоя модели (те же, что в tools/mocap/retarget.py). Через них проходит любая
# поза, откуда бы она ни пришла — числа, готовая библиотека поз, видео руки: фаланга гнётся только к ладони, назад и вбок
# сустав не выворачивается. Основание пальца до 92°, средний сустав до 108°, дальний до 75° — как у живой руки; общий
# предел 85° не дал бы сжать кулак. Назад — лёгкий переразгиб основания (15°), у среднего и дальнего суставов 5° от покоя,
# а в покое модели они уже согнуты на 9–25°: дальше прямого палец не идёт. Развод ±20°.
FINGER_LIMITS = ((-20, 20), (-15, 92), (-5, 108), (-5, 75))
# Согнутый в основании палец вбок почти не отводится (держат боковые связки): при 90° остаётся четверть развода.
SPREAD_WHEN_BENT = .75
# Большой палец: повороты основания и два сгиба.
THUMB_ROOT_LIMIT, THUMB_JOINT_LIMITS = 55, (-15, 75)
# Насколько поза вышла за пределы: имя сустава → градусы. Печатает update_motion: числа позы стоит поправить.
CLAMPED = {}


def clamp(name, value, low, high):
    result = min(high, max(low, value))
    if abs(result - value) > CLAMPED.get(name, 3):
        CLAMPED[name] = round(abs(result - value), 1)
    return result


def limited(pose):
    """Та же поза в пределах живой руки."""
    fingers = {}
    for name, (spread, base, middle, far) in pose["fingers"].items():
        base = clamp(name + " base", base, *FINGER_LIMITS[1])
        room = FINGER_LIMITS[0][1] * (1 - SPREAD_WHEN_BENT * max(0., base) / 90)
        fingers[name] = (clamp(name + " spread", spread, -room, room), base, clamp(name + " middle", middle, *FINGER_LIMITS[2]),
                         clamp(name + " far", far, *FINGER_LIMITS[3]))
    root, second, third = pose["thumb"]
    thumb = (tuple(clamp("Thumb root", v, -THUMB_ROOT_LIMIT, THUMB_ROOT_LIMIT) for v in root),
             clamp("Thumb second", second, *THUMB_JOINT_LIMITS), clamp("Thumb third", third, *THUMB_JOINT_LIMITS))
    return dict(pose, fingers=fingers, thumb=thumb)


# Костяшки. Промежуточная кость сустава (Joint*) поворачивается на половину угла и держит кожу сустава на дуге вокруг
# него (зону сгиба сужает sharpen_joints.py). У живой руки при сгибе кость ещё и упирается в кожу: костяшка выступает,
# сильнее всего у основания пальца (головка пястной кости в кулаке), а складка с ладонной стороны уходит вглубь.
# Поэтому промежуточная кость сдвигается к тылу по биссектрисе сгиба на долю разницы между острым углом двух фаланг
# (r / cos(угол / 2) от центра сустава) и дугой (r). Сдвиг — обычный ключ положения кости в клипе, скриптов в игре нет.
KNUCKLE_JOINTS = [finger + number for finger in ("Index", "Middle", "Ring", "Pinky") for number in ("01", "02", "03")] + ["Thumb02", "Thumb03"]
# Доля расчётного выступа по суставам: основание пальца, средний, дальний.
KNUCKLE_GAIN = {"01": .55, "02": .2, "03": .25}
KNUCKLE_MAX_BEND = math.radians(120)
KNUCKLES_ON = os.environ.get("KNUCKLES", "1") != "0"
_knuckle_rest = {}


def flex_turn(bone):
    """На сколько кость довернули вокруг её оси при правке крена (flex_converge.py пишет это в свойство flex_roll):
    поворот из прежних осей кости в нынешние. У костей, которых правка не касалась, поворота нет."""
    return Matrix.Rotation(bone.get("flex_roll", 0.), 3, "Y")


def in_bone_axes(pose_bone, rotation):
    """Поворот кости, заданный углами Эйлера в её прежних осях, — в нынешних осях кости."""
    turn = flex_turn(pose_bone.bone)
    return (turn.inverted() @ rotation.to_matrix() @ turn).to_euler("XYZ")


def knuckle_rest(armature, name, suffix):
    """Сустав в покое: расстояние от центра до кожи с тыла и угол, на который кость за суставом уже согнута.
    Тыл — ось Z кости до правки крена: она смотрит от ногтя."""
    key = (armature.name, name + suffix)
    if key in _knuckle_rest:
        return _knuckle_rest[key]
    bone = armature.data.bones[name + suffix]
    frame = bone.matrix_local @ flex_turn(bone).inverted().to_4x4()
    inverse = frame.inverted()
    parent = (inverse @ bone.parent.matrix_local).to_3x3() @ Vector((0, 1, 0))
    rest_bend = math.atan2(parent.z, parent.y)
    radius = 0.
    mesh = bpy.data.objects.get("Arm_Male" + suffix)
    if mesh is not None:
        to_bone = inverse @ armature.matrix_world.inverted() @ mesh.matrix_world
        for vertex in mesh.data.vertices:
            local = to_bone @ vertex.co
            # Кожа прямо над суставом: узкий столбик вдоль тыльной оси кости.
            if abs(local.y) < .003 and abs(local.x) < .004 and 0 < local.z < .02:
                radius = max(radius, local.z)
    _knuckle_rest[key] = (radius, rest_bend)
    return _knuckle_rest[key]


def knuckles(armature, suffix):
    """Сдвигает промежуточные кости суставов по текущей позе пальцев. Вызывать после того, как пальцы поставлены."""
    bones = armature.pose.bones
    for name in KNUCKLE_JOINTS:
        helper = bones.get("Joint" + name + suffix)
        if helper is None:
            continue
        helper.location = (0, 0, 0)
        if not KNUCKLES_ON:
            continue
        radius, rest_bend = knuckle_rest(armature, name, suffix)
        # Сгиб кости считается в её прежних осях: в них заданы числа поз.
        turn = flex_turn(bones[name + suffix].bone)
        along = turn @ bones[name + suffix].matrix_basis.to_3x3() @ turn.inverted() @ Vector((0, 1, 0))
        bend = math.atan2(-along.z, along.y)
        if radius <= 0 or bend <= 0:
            continue
        total = min(KNUCKLE_MAX_BEND, max(0., rest_bend) + bend)
        offset = radius * (1 / math.cos(total / 2) - 1 / math.cos(max(0., rest_bend) / 2)) * KNUCKLE_GAIN[name[-2:]]
        direction = (bend - rest_bend) / 2
        helper.location = flex_turn(helper.bone).inverted() @ Vector((0, offset * math.sin(direction), offset * math.cos(direction)))


ZERO_POSE = "Pose_Base"
ANIMATIONS_TO_COME = ("Idle", "HoldCards", "TakeCard", "PlayCard", "ArrangeCards", "Point", "TapTable", "React", "Victory")


def apply_pose(armature, pose, suffix):
    """Ставит позу на одну руку в пределах живых суставов (limited). Сторона руки учтена: развод и боковые повороты зеркальны."""
    pose = limited(pose)
    bones = armature.pose.bones
    side = 1 if suffix == "_L" else -1
    for bone in bones:
        if bone.name.endswith(suffix):
            bone.rotation_mode = "XYZ"
            bone.rotation_euler = (0, 0, 0)
    for name, (spread, base, middle, far) in pose["fingers"].items():
        turns = (Euler((-math.radians(base), 0, -math.radians(spread) * side)), Euler((-math.radians(middle), 0, 0)), Euler((-math.radians(far), 0, 0)))
        for number, turn in zip(("01", "02", "03"), turns):
            bone = bones[name + number + suffix]
            bone.rotation_euler = in_bone_axes(bone, turn)
    root, second, third = pose["thumb"]
    bones["Thumb01" + suffix].rotation_euler = Euler((math.radians(root[0]), math.radians(root[1]) * side, math.radians(root[2]) * side))
    bones["Thumb02" + suffix].rotation_euler = Euler((-math.radians(second), 0, 0))
    bones["Thumb03" + suffix].rotation_euler = Euler((-math.radians(third), 0, 0))
    flex, deviate = pose["wrist"]
    bones["Hand" + suffix].rotation_euler = Euler((-math.radians(flex), 0, math.radians(deviate) * side))
    twist = pose.get("twist", 0)
    bones["ForearmTwist" + suffix].rotation_euler = Euler((0, math.radians(twist) * side, 0))
    # Кулак и хват чуть сводят ладонь: мизинец и безымянный уходят вперёд.
    cup = max(0., (pose["fingers"]["Pinky"][1] - 30) / 60)
    bones["MetaPinky" + suffix].rotation_euler = Euler((-math.radians(12 * cup), 0, 0))
    bones["MetaRing" + suffix].rotation_euler = Euler((-math.radians(6 * cup), 0, 0))
    knuckles(armature, suffix)


def store_actions(armature):
    """Каждая поза — отдельное действие из одного кадра: в Unity это отдельный клип."""
    armature.animation_data_create()
    for name, pose in POSES.items():
        for suffix in ("_L", "_R"):
            apply_pose(armature, pose, suffix)
        action = bpy.data.actions.new("Pose_" + name)
        action.use_fake_user = True
        armature.animation_data.action = action
        for bone in armature.pose.bones:
            if bone.name == "Root" or "Socket" in bone.name:
                continue
            # Два одинаковых кадра: клип нулевой длины Unity воспринимает плохо.
            bone.keyframe_insert("rotation_euler", frame=1)
            bone.keyframe_insert("rotation_euler", frame=2)
            # Промежуточные кости суставов сдвинуты под костяшки (knuckles): их положение тоже идёт в клип.
            if bone.name.startswith("Joint"):
                bone.keyframe_insert("location", frame=1)
                bone.keyframe_insert("location", frame=2)
    # Нулевая поза идёт первой по алфавиту: Unity берёт положение костей в покое из первого клипа файла, иначе рука была бы сжата в кулак.
    for bone in armature.pose.bones:
        bone.rotation_euler = (0, 0, 0)
        if bone.name.startswith("Joint"):
            bone.location = (0, 0, 0)
    action = bpy.data.actions.new(ZERO_POSE)
    action.use_fake_user = True
    armature.animation_data.action = action
    for bone in armature.pose.bones:
        if bone.name != "Root" and "Socket" not in bone.name:
            bone.keyframe_insert("rotation_euler", frame=1)
            bone.keyframe_insert("rotation_euler", frame=2)
            if bone.name.startswith("Joint"):
                bone.keyframe_insert("location", frame=1)
                bone.keyframe_insert("location", frame=2)
    armature.animation_data.action = None
