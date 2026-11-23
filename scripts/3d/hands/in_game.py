"""Поза рук в партии. Начало координат модели — глаза игрока при взгляде по умолчанию (наклон камеры PITCH): X вправо, Y вперёд, Z вверх.
Позы описаны в раскладке «веер слева» (LEFT — рука с веером, RIGHT — свободная рука лежит на сукне); в игре веер держит
рука FAN_HAND (сейчас левая), и при веере в правой позы ставятся зеркально (pose_both). Числа веера — те же, что Table3DLayout.HandPivot, HandTilt, HandYaw, HandFanRadius,
HandFanStep и HandCardScale в Unity, PITCH — TableCameraController.StartPitch.
Кадр подогнан под скриншоты Liar's Bar (генерации/13-кадры-liars-bar): веер поднят к середине экрана, кисть под ним видна целиком,
предплечья уходят за нижний край. Хват веера и кисть на сукне — по видео-образцам автора (references/hands/видео-образцы)."""
import math

import bpy
from mathutils import Matrix, Vector

import poses

ACTION = "Pose_InGame"
# Какая рука держит веер. Автор держит карты левой (2026-11-13; на день веер переезжал в правую и вернулся):
# веер в левой, жесты и взятие карты — правой.
# Позы ниже описаны в раскладке «веер слева»: LEFT — рука с веером, RIGHT — свободная рука. При FAN_HAND = "_R"
# pose_both ставит их зеркально, отражая поперёк тела (X); числа пальцев у обеих рук симметричны. Карты не отражаются:
# веер в игре читается слева направо (fan_axes).
FAN_HAND = "_L"
FREE_HAND = "_L" if FAN_HAND == "_R" else "_R"
MIRROR = FAN_HAND == "_R"
FAN_SOCKET = "SocketFan" + FAN_HAND

# Стол в осях модели: глаза на 1,30 м, сукно на 0,76 м, камера по умолчанию наклонена на 32° вниз — стол виден почти до своего края.
PITCH = math.radians(32)
EYE_ABOVE_TABLE = 1.30 - .76
BODY_RIGHT = Vector((1, 0, 0))
TABLE_UP = Vector((0, -math.sin(PITCH), math.cos(PITCH)))
TABLE_AHEAD = Vector((0, math.cos(PITCH), math.sin(PITCH)))

# Веер держат в 32 см от глаз, как в Liar's Bar: на 16:9 он занимает экран примерно от 29 до 58 % высоты, на 20:9 — от 24 до 60 %.
FAN_PIVOT = Vector((-.154, .32, -.031))
# Лицом к глазам: не наклонён относительно взгляда и повёрнут на 15° к середине (в Unity HandTilt = 0, HandYaw = -15).
FAN_TILT = math.radians(0)
FAN_YAW = math.radians(15)
# Карта в руке в 1,35 раза больше настоящей: так она читается с телефона. Карты вращаются вокруг точки
# в 6 мм над нижним краем: там их прижимает большой палец, как в настоящем веере.
CARD_SCALE = 1.35
FAN_RADIUS = .088 * CARD_SCALE / 2 - .0064
FAN_STEP, FAN_LIMIT, FAN_GAP = 13.5, 100., .0009
# Хват подобран под веер из шести карт. При другом числе карт передняя карта остаётся на той же глубине, под большим пальцем,
# а стопка растёт от играющего, за карты: там её не видно. Раньше стопка росла к играющему, и при 9+ картах большой палец
# тонул в ней (до 8 мм при 18 картах). То же в Unity: CardGame3DPresenter.FanFitted.
FAN_FITTED = 6


def card_depth(i, count):
    """Глубина карты i (слева направо) в веере из count карт: вглубь от играющего — плюс."""
    return (count - FAN_FITTED - i) * FAN_GAP

_TURN = Matrix.Rotation(FAN_YAW, 3, "Z") @ Matrix.Rotation(FAN_TILT, 3, "X")
FAN_RIGHT = _TURN @ Vector((1, 0, 0))
FAN_UP = _TURN @ Vector((0, 0, 1))
FAN_DEEP = _TURN @ Vector((0, 1, 0))   # От играющего вглубь: за веером пальцы, перед ним большой палец.


def on_fan(a, b, c):
    return FAN_PIVOT + FAN_RIGHT * a + FAN_UP * b + FAN_DEEP * c


def fan_dir(a, b, c):
    return FAN_RIGHT * a + FAN_UP * b + FAN_DEEP * c


def on_table(x, ahead, lift):
    """Точка над сукном: x вправо от глаз, ahead — вперёд от глаз по столу, lift — высота над сукном.
    Край стола — 0,36 м впереди, золотой обод до 0,44 м."""
    return BODY_RIGHT * x + TABLE_AHEAD * ahead + TABLE_UP * (lift - EYE_ABOVE_TABLE)


def table_dir(x, ahead, up):
    return BODY_RIGHT * x + TABLE_AHEAD * ahead + TABLE_UP * up


def edge_on(wrist, tilt, roll):
    """Кисть у веера: запястье в точке wrist, ось кисти (к основанию среднего пальца) наклонена от вертикали вправо
    на tilt градусов в плоскости веера, а сама кисть повёрнута вокруг этой оси на roll градусов: 0 — ладонь к играющему,
    90 — ребром большого пальца к играющему, ладонь вправо, тыл влево."""
    tilt, roll = math.radians(tilt), math.radians(roll)
    along = fan_dir(math.sin(tilt), math.cos(tilt), 0)
    across = fan_dir(math.cos(tilt), -math.sin(tilt), 0)      # Вправо поперёк оси кисти.
    return {"wrist": wrist, "along": along, "back": FAN_DEEP * math.cos(roll) - across * math.sin(roll)}


# Хват как на видео-образцах автора (references/hands/видео-образцы, 01 и 02; автор выбрал его 2026-11-14 вместо хвата
# по своему фото): кисть повёрнута к играющему ребром большого пальца, как при рукопожатии. Большой палец лежит на лицах
# карт наискось, от нижнего края веера вправо-вверх, и прижимает их подушечкой; остальные четыре пальца уходят за карты
# вправо, собраны вместе и расслаблены — каждый следующий согнут чуть сильнее. С места играющего видны большой палец,
# его основание с ладонью и кончики пальцев, выглядывающие из-за правого нижнего угла веера.
# Прежний хват (до 2026-11-14) держал ладонь к играющему: большой палец стоял столбом, а под веером был виден кулак
# из трёх согнутых пальцев. Он лежит в истории git этого файла.
LEFT = dict(
    edge_on(on_fan(-.064, -.063, .039), 22, 85),
    elbow_from_wrist=Vector((-.4, -.35, -.85)),
    pose={
        "fingers": {"Index": (-8, 71, -4, 0), "Middle": (0, 74, 3, 2), "Ring": (-3, 60, 12, 5), "Pinky": (8, 64, 20, 8)},
        "thumb": ((0, 0, 0), 0, 0),
        "wrist": (0, 0),
    },
    # Куда смотрят кости большого пальца: от основания у запястья он идёт к нижнему краю веера и ложится на лица карт
    # под 35° к горизонтали. Глубина точек подобрана по сетке: кожа пальца касается лицевой карты и не входит в стопку
    # глубже 2 мм, пальцы за картами не задевают рубашки при 6 и 12 картах (probe_fan.py).
    thumb=(on_fan(-.021, -.011, -.020), on_fan(.007, .008, -.0195), on_fan(.030, .024, -.0185)),
)
# Свободная кисть лежит на сукне сразу за золотым ободом, как кисть в начале видео-образца 01: пальцы почти прямые
# и собраны вместе, большой палец отведён и лежит на боку, предплечье лежит на краю стола. Раньше пальцы были разведены
# веером на 48° между указательным и мизинцем, и кисть выглядела растопыренной. Поза подобрана по сетке:
# ладонь, все подушечки и большой палец касаются ткани (подушечки в 1–2 мм над ней), ничего не уходит в стол. Тыл наклонён
# к большому пальцу: у расслабленной кисти костяшка мизинца ниже костяшки указательного, без наклона безымянный и мизинец
# тонули в столе на 24 и 36 мм. Большой палец — из готовой позы Default pose (Godot XR Tools, CC0).
RIGHT = {
    "wrist": on_table(.19, .47, .041),
    "along": table_dir(-.38, .92, -.06),
    "back": table_dir(-.4, .06, 1),
    "elbow_from_wrist": table_dir(.16, -.32, .03),
    "pose": {
        "fingers": {"Index": (-7, 1.5, -4, 0), "Middle": (0, -.5, -4, 0), "Ring": (4, -4.9, -5, 0), "Pinky": (10, -3, -4, 0)},
        "thumb": ((2, -28.5, 13), -7.5, 10),
        "wrist": (0, 0),
    },
}


# Левая кисть на сукне, когда карт в руке нет (клип Take_TableRest, состояние OnTable в Animator Controller):
# зеркальная копия правой.
LEFT_ON_TABLE = {
    "wrist": on_table(-.19, .47, .041),
    "along": table_dir(.38, .92, -.06),
    "back": table_dir(.4, .06, 1),
    "elbow_from_wrist": table_dir(-.16, -.32, .03),
    "pose": RIGHT["pose"],
}


def to_game(v):
    """Точка или направление из раскладки «веер слева» в игру: при веере в правой — отражение поперёк тела."""
    return Vector((-v.x, v.y, v.z)) if MIRROR else v.copy()


def game_arm(arm):
    """Поза руки из раскладки «веер слева» в игре: положения и направления отражены, сгибы пальцев те же."""
    if not MIRROR:
        return arm
    result = dict(arm)
    for key in ("wrist", "along", "back", "elbow_from_wrist"):
        result[key] = to_game(arm[key])
    if "thumb" in arm:
        result["thumb"] = tuple(to_game(p) for p in arm["thumb"])
    return result


def fan_axes():
    """Веер в игре: точка, вправо по вееру (к правому краю экрана), вверх вдоль карт, вглубь от играющего.
    При отражении «вправо» берётся с обратным знаком: карты не отражаются, а тройка осей остаётся правой."""
    if not MIRROR:
        return FAN_PIVOT.copy(), FAN_RIGHT.copy(), FAN_UP.copy(), FAN_DEEP.copy()
    return to_game(FAN_PIVOT), -to_game(FAN_RIGHT), to_game(FAN_UP), to_game(FAN_DEEP)


def pose_both(armature, fan_arm, free_arm):
    """Обе руки в позе: fan_arm — рука с веером (LEFT и её варианты), free_arm — свободная (RIGHT, жесты)."""
    set_arm(armature, game_arm(fan_arm), FAN_HAND)
    set_arm(armature, game_arm(free_arm), FREE_HAND)


def frame(origin, y, z):
    """Матрица кости: Y вдоль неё, Z к тылу."""
    y = y.normalized()
    x = y.cross(z).normalized()
    z = x.cross(y)
    matrix = Matrix((x, y, z)).transposed().to_4x4()
    matrix.translation = origin
    return matrix


def place(armature, name, matrix, keep_position=True):
    bone = armature.pose.bones[name]
    bone.matrix = matrix
    if keep_position:
        bone.location = (0, 0, 0)
    bpy.context.view_layer.update()


def set_arm(armature, arm, suffix):
    poses.apply_pose(armature, arm["pose"], suffix)
    bpy.context.view_layer.update()
    bones = armature.pose.bones
    length = (bones["Forearm" + suffix].length + bones["ForearmTwist" + suffix].length)
    wrist = arm["wrist"]
    elbow = wrist + arm["elbow_from_wrist"].normalized() * length
    forward = (wrist - elbow).normalized()
    back = arm["back"] - forward * arm["back"].dot(forward)
    place(armature, "Forearm" + suffix, frame(elbow, forward, back), keep_position=False)
    middle = elbow + forward * bones["Forearm" + suffix].length
    place(armature, "ForearmTwist" + suffix, frame(middle, forward, back))
    place(armature, "Hand" + suffix, frame(wrist, arm["along"], arm["back"]))
    if "anatomy" in arm and arm.get("anatomy_weight", 1.) > 0.:
        import hand_anatomy
        w = arm.get("anatomy_weight", 1.)
        pose = {k: v for k, v in arm["anatomy"].items() if k != "wrist"}
        if w >= 1. - 1e-6:
            hand_anatomy.apply(armature, pose, suffix)
        else:
            q0 = {b.name: b.matrix_basis.to_quaternion() for b in bones if b.name.endswith(suffix) and not b.name.startswith(("Forearm", "Hand"))}
            l0 = {b.name: b.location.copy() for b in bones if b.name.endswith(suffix) and b.name.startswith("Joint")}
            hand_anatomy.apply(armature, pose, suffix)
            for name, q in q0.items():
                bones[name].rotation_mode = "XYZ"
                bones[name].rotation_euler = q.slerp(bones[name].matrix_basis.to_quaternion(), w).to_euler("XYZ")
            for name, loc in l0.items():
                bones[name].location = loc.lerp(bones[name].location, w)
            bpy.context.view_layer.update()
    elif "thumb" in arm:
        deep = fan_axes()[3]
        for name, target in zip(("Thumb01", "Thumb02", "Thumb03"), arm["thumb"]):
            head = bones[name + suffix].head.copy()
            place(armature, name + suffix, frame(head, target - head, -deep))
        # Большой палец поставлен по точкам: костяшки пересчитываются под его настоящий сгиб.
        poses.knuckles(armature, suffix)


def place_fan_socket(armature):
    """Точка привязки веера на кисти с веером: кость SocketFan_R (или _L), дочь её Hand. В позе партии она стоит ровно в
    точке веера, Y — вдоль карт вверх, Z — от играющего вглубь (fan_axes). В Unity к ней крепится веер без расчётов:
    после импорта FBX локальные оси кости — вправо по вееру, вверх и вглубь, как у веера в Table3DLayout.
    Кость веера на другой руке (от прежней раскладки) удаляется."""
    armature.matrix_world.identity()
    for bone in armature.pose.bones:
        bone.rotation_mode = "XYZ"
        bone.rotation_euler = (0, 0, 0)
        bone.location = (0, 0, 0)
    set_arm(armature, game_arm(LEFT), FAN_HAND)
    hand = armature.pose.bones["Hand" + FAN_HAND]
    pivot, _, up, deep = fan_axes()
    socket = hand.bone.matrix_local @ hand.matrix.inverted() @ frame(pivot, up, deep)
    for bone in armature.pose.bones:
        bone.rotation_euler = (0, 0, 0)
        bone.location = (0, 0, 0)
    bpy.context.view_layer.update()
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = armature
    armature.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bones = armature.data.edit_bones
    for old in ("SocketFan_L", "SocketFan_R"):
        if old != FAN_SOCKET and bones.get(old) is not None:
            bones.remove(bones[old])
    bone = bones.get(FAN_SOCKET) or bones.new(FAN_SOCKET)
    bone.head = socket.translation
    bone.tail = socket.translation + socket.col[1].xyz * .03
    bone.align_roll(socket.col[2].xyz)
    bone.parent = bones["Hand" + FAN_HAND]
    bone.use_connect = False
    bone.use_deform = False
    bpy.ops.object.mode_set(mode="OBJECT")
    armature.select_set(False)


def store(armature):
    """Поза партии — обычный клип, как остальные: его можно смешивать с другими."""
    for bone in armature.pose.bones:
        bone.rotation_mode = "XYZ"
    pose_both(armature, LEFT, RIGHT)
    action = bpy.data.actions.new(ACTION)
    action.use_fake_user = True
    armature.animation_data.action = action
    for bone in armature.pose.bones:
        if bone.name == "Root" or "Socket" in bone.name:
            continue
        for frame_number in (1, 2):
            bone.keyframe_insert("rotation_euler", frame=frame_number)
            bone.keyframe_insert("location", frame=frame_number)
    armature.animation_data.action = None
    for bone in armature.pose.bones:
        bone.rotation_euler = (0, 0, 0)
        bone.location = (0, 0, 0)
    bpy.context.view_layer.update()
