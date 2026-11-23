"""Проверка базовых поз в игре: обе кисти перед собой сжимаются из расслабленной кисти (Idle) в кулак (Fist) и обратно.

Клип Take_TestClench: первый кадр — Idle, последний — кулак, между ними — сжатие gestures.closing (пальцы закрываются,
большой палец сначала отводится и потом ложится на них), то есть первая половина клипа IdleFistIdle тестовой сцены.
В Unity его играет слой PoseTest аниматора (HandsAnimatorBuilder.cs), а время клипа ставит параметр TestClench:
0 — Idle, 1 — кулак. Разжатие — то же сжатие в обратную сторону, как во второй половине IdleFistIdle.
Включается в игре клавишей F (OwnArms3DView), это временная проверка: веер на это время прячется.

Форма кисти — ровно числа gestures.py (приняты автором 2026-11-16), здесь они не меняются. Этот файл только ставит руки
в кадр: кисти подняты перед собой, где обычно держат веер, большими пальцами вверх (WRIST, ALONG, ROLL ниже).

Запуск: blender art3d/hands/CardCity_Hands.blend --background --disable-autoexec --python scripts/3d/hands/game_test.py -- <команда>
  store  — пересобрать клип в файле .blend и сохранить его; рядом пишется art3d/hands/test/CardCity_Hands_GameTest.json —
           расстояния от запястья до суставов пальцев в кадрах Idle, середины сжатия и кулака: по нему Table3DQa.cs сверяет
           кости в Unity, а здесь они сверяются с той же позой на руке в покое скелета: положение в кадре форму не меняет.
  probe  — без сохранения: кожа кистей над сукном в каждом кадре клипа и на переходах между позой партии (и руками без
           карт) и Idle проверки — слой аниматора смешивает их по костям. Кулак с позой партии напрямую не смешивается:
           OwnArms3DView сначала разжимает кисть.
  render — без сохранения: кадры с места игрока (свет как в Unity, кожа T_Hand_Male_Smooth_*) в screens/3d-hands/в-игре/,
           GT_TAG — приписка к именам кадров.
"""
import json
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(__file__))
import fist_smooth
import gestures
import hand_anatomy
import in_game
import source

ACTION = "Take_TestClench"
FOLDER = os.path.join(source.ROOT, "art3d", "hands", "test")
JSON = os.path.join(FOLDER, "CardCity_Hands_GameTest.json")
# Кадры сжатия: 0 — Idle, CLOSED — кулак (в gestures.py), 30 кадров в секунду — 0,6 с на сжатие.
FRAMES = list(range(gestures.CLOSED + 1))
MARKS = (0, gestures.THUMB_WRAP_FROM, gestures.CLOSED)
MARKED = [f + n for f in ("Thumb", "Index", "Middle", "Ring", "Pinky") for n in ("01", "02", "03")] + ["Tip" + f for f in ("Thumb", "Index", "Middle", "Ring", "Pinky")]

# Положение свободной кисти (раскладка «веер слева», как in_game.RIGHT) в осях модели — от глаз: X вправо, Y по взгляду,
# Z вверх поперёк взгляда. Кисть поднята перед собой, где держат веер, и повёрнута большим пальцем вверх: так с места
# игрока видно, как пальцы ложатся в ладонь и как большой палец ложится на них. Ниже, над сукном, кисть видна сверху
# и мелко, а согнутые пальцы Idle уходили в ткань (на 26 мм при запястье в 7,5 см над ней, замер probe).
# Кисть продолжает предплечье: ось кисти — это направление от локтя к запястью, отклонённое к мизинцу на ULNAR и
# разогнутое к тылу на EXTEND, как у живого спокойного кулака. До 2026-11-17 ось кисти задавалась отдельно от локтя,
# и кисть была отклонена к мизинцу на 56° (предел живого запястья около 30–35°): с места игрока кисти выглядели
# вывернутыми внутрь.
WRIST = Vector((.17, .36, -.11))
# Локоть ниже запястья и чуть снаружи, у тела: предплечье почти горизонтально (кисть на 9° вверх), предплечья почти
# параллельны (сведены к середине на 13°), кулаки смотрят вперёд. При локте дальше наружу (x .29) кулаки смотрели друг на друга.
ELBOW = Vector((.23, .22, -.27))
ROLL = 55                                          # Поворот вокруг предплечья от «тыл вверх», плюс — большой палец вверх.
ULNAR, EXTEND = 8, 10                              # Отклонение кисти к мизинцу и разгиб к тылу, градусы.
# Подбор без правки файла (только probe и render): GT_WRIST, GT_ELBOW — точки «x,y,z»; GT_ROLL, GT_ULNAR, GT_EXTEND — градусы.
ROLL = float(os.environ.get("GT_ROLL", ROLL))
ULNAR = float(os.environ.get("GT_ULNAR", ULNAR))
EXTEND = float(os.environ.get("GT_EXTEND", EXTEND))


def _vector(name, default):
    value = os.environ.get(name)
    return Vector(tuple(float(x) for x in value.split(","))) if value else default.copy()


def free_arm(wrist=None, elbow=None, roll=None, ulnar=None, extend=None):
    """Свободная рука: запястье, локоть, поворот предплечья и угол запястья. Возвращает позу руки, как in_game.RIGHT.
    Без аргументов — рука проверки кулака (WRIST, ELBOW, ROLL, ULNAR, EXTEND); с ними — другое положение руки (жесты
    gesture_test.py), числа те же по смыслу."""
    wrist = _vector("GT_WRIST", WRIST) if wrist is None else Vector(wrist)
    elbow = _vector("GT_ELBOW", ELBOW) if elbow is None else Vector(elbow)
    roll = ROLL if roll is None else roll
    ulnar = ULNAR if ulnar is None else ulnar
    extend = EXTEND if extend is None else extend
    forward = (wrist - elbow).normalized()
    up = Vector((0, 0, 1))
    back = (up - forward * up.dot(forward)).normalized()
    back = (Matrix.Rotation(math.radians(roll), 3, forward) @ back).normalized()
    # У правой кисти большой палец — в сторону back × forward; к мизинцу — обратно. Разгиб — к тылу.
    thumb_side = back.cross(forward).normalized()
    along = (Matrix.Rotation(math.radians(ulnar), 3, back) @ forward).normalized()
    if ulnar >= 0:
        if along.dot(thumb_side) > forward.dot(thumb_side):
            along = (Matrix.Rotation(math.radians(-ulnar), 3, back) @ forward).normalized()
    # Минус — кисть отклонена к большому пальцу (лучевое отведение живой руки до 20°): так в «фу» палец смотрит вниз, а не
    # заваливается внутрь (gesture_test.py). Проверка кулака отклоняет кисть только к мизинцу.
    elif along.dot(thumb_side) < forward.dot(thumb_side):
        along = (Matrix.Rotation(math.radians(-ulnar), 3, back) @ forward).normalized()
    side = along.cross(back).normalized()
    along = (Matrix.Rotation(math.radians(extend), 3, side) @ along).normalized()
    if extend >= 0:
        if along.dot(back) < 0:
            along = (Matrix.Rotation(math.radians(-2 * extend), 3, side) @ along).normalized()
    # Минус — кисть согнута к ладони (сгиб живого запястья до 70–80°): так в «фу» большой палец смотрит в сукно при
    # предплечье, поднятом к столу (gesture_test.py). Проверка кулака кисть только разгибает.
    elif along.dot(back) > 0:
        along = (Matrix.Rotation(math.radians(-2 * extend), 3, side) @ along).normalized()
    return {"wrist": wrist, "along": along, "back": back, "elbow_from_wrist": elbow - wrist}


def mirrored(arm):
    """Та же рука зеркально поперёк тела."""
    flip = lambda v: Vector((-v.x, v.y, v.z))
    return {key: flip(arm[key]) for key in ("wrist", "along", "back", "elbow_from_wrist")}


def reset(armature):
    for bone in armature.pose.bones:
        bone.rotation_mode = "XYZ"
        bone.rotation_euler = (0, 0, 0)
        bone.location = (0, 0, 0)
        bone.scale = (1, 1, 1)
    bpy.context.view_layer.update()


def place_arm(armature, arm, suffix):
    """Предплечье и кисть в кадр, как в in_game.set_arm, но без поз пальцев прежней системы: их ставит hand_anatomy."""
    bones = armature.pose.bones
    length = bones["Forearm" + suffix].length + bones["ForearmTwist" + suffix].length
    wrist = arm["wrist"]
    elbow = wrist + arm["elbow_from_wrist"].normalized() * length
    forward = (wrist - elbow).normalized()
    back = arm["back"] - forward * arm["back"].dot(forward)
    in_game.place(armature, "Forearm" + suffix, in_game.frame(elbow, forward, back), keep_position=False)
    middle = elbow + forward * bones["Forearm" + suffix].length
    in_game.place(armature, "ForearmTwist" + suffix, in_game.frame(middle, forward, back))
    in_game.place(armature, "Hand" + suffix, in_game.frame(wrist, arm["along"], arm["back"]))


def hand_pose(frame):
    """Форма кисти на кадре сжатия из gestures.py. Запястье не трогается: кисть уже поставлена в кадр (place_arm)."""
    pose = gestures.closing(frame)
    return {key: value for key, value in pose.items() if key != "wrist"}


def pose_frame(armature, frame):
    reset(armature)
    free = free_arm()
    sides = ((in_game.game_arm(mirrored(free)), in_game.FAN_HAND), (in_game.game_arm(free), in_game.FREE_HAND))
    for arm, suffix in sides:
        place_arm(armature, arm, suffix)
    for _, suffix in sides:
        hand_anatomy.apply(armature, hand_pose(frame), suffix)
    bpy.context.view_layer.update()


def distances(armature):
    """Расстояния от запястья до начала каждой кости пальцев, миллиметры (как в export_test.py)."""
    bones = armature.pose.bones
    result = {}
    for side in ("_L", "_R"):
        wrist = bones["Hand" + side].head
        result[side] = {name: round((bones[name + side].head - wrist).length * 1000, 3) for name in MARKED if name + side in bones}
    return result


def bake(armature):
    """Ключ на каждый кадр углами Эйлера и положением, как animations.bake: при выгрузке FBX все действия в одном режиме."""
    old = bpy.data.actions.get(ACTION)
    if old is not None:
        bpy.data.actions.remove(old)
    action = bpy.data.actions.new(ACTION)
    action.use_fake_user = True
    armature.animation_data_create()
    armature.animation_data.action = action
    previous, marks = {}, {}
    for frame in FRAMES:
        pose_frame(armature, frame)
        if frame in MARKS:
            marks[frame] = distances(armature)
        for bone in armature.pose.bones:
            if bone.name == "Root" or "Socket" in bone.name:
                continue
            euler = bone.matrix_basis.to_euler("XYZ", previous[bone.name]) if bone.name in previous else bone.matrix_basis.to_euler("XYZ")
            previous[bone.name] = euler
            bone.rotation_euler = euler
            bone.keyframe_insert("rotation_euler", frame=frame + 1)
            bone.keyframe_insert("location", frame=frame + 1)
        # Вес корректирующей формы кулака (fist_smooth.py), если она есть: в FBX — кривая веса формы этого клипа.
        if fist_smooth.PROP in armature:
            armature[fist_smooth.PROP] = fist_smooth.weight(frame)
            armature.keyframe_insert('["%s"]' % fist_smooth.PROP, frame=frame + 1)
    armature.animation_data.action = None
    if fist_smooth.PROP in armature:
        armature[fist_smooth.PROP] = 0.
    reset(armature)
    return marks


def rest_marks(armature):
    """Та же форма кисти на руке в покое скелета (как в export_test.py, сцена HandGestureTest): Idle, кадр 9 и кулак."""
    result = {}
    for frame in MARKS:
        reset(armature)
        for side in ("_L", "_R"):
            hand_anatomy.apply(armature, gestures.closing(frame), side)
        bpy.context.view_layer.update()
        result[frame] = distances(armature)
    reset(armature)
    return result


def store():
    armature = bpy.data.objects["Rig_Hands"]
    armature.matrix_world.identity()
    marks = bake(armature)
    # Та же форма, что на руке в покое скелета: положение кисти в кадре на расстояния от запястья не влияет.
    reference = rest_marks(armature)
    worst = max(abs(marks[f][s][b] - reference[f][s][b]) for f in MARKS for s in ("_L", "_R") for b in marks[f][s])
    print("=== GAME TEST форма кисти против той же позы на руке в покое: наибольшее расхождение %.4f мм в кадрах %s" % (worst, MARKS))
    if worst > .01:
        raise SystemExit("=== GAME TEST FAILED: положение руки в кадре изменило форму кисти")
    flat = [{"frame": f, "side": s, "bone": b, "mm": mm} for f in MARKS for s, bones in marks[f].items() for b, mm in bones.items()]
    with open(JSON, "w", encoding="utf-8") as f:
        json.dump({"clip": ACTION, "frames": len(FRAMES), "fps": gestures.FPS, "marks": flat}, f)
    bpy.ops.wm.save_mainfile()
    print("=== GAME TEST STORED %s: %d кадров, замеры -> %s" % (ACTION, len(FRAMES), JSON))


# --- Замеры кожи (probe) ---------------------------------------------------------------------------------------------

def basis(armature):
    return {bone.name: (bone.matrix_basis.to_quaternion(), bone.matrix_basis.to_translation()) for bone in armature.pose.bones}


def put_basis(armature, pose):
    for bone in armature.pose.bones:
        rotation, location = pose[bone.name]
        bone.rotation_mode = "QUATERNION"
        bone.rotation_quaternion = rotation
        bone.location = location
    bpy.context.view_layer.update()


def blend(a, b, t):
    """Смесь двух поз по костям, как слой аниматора Unity: повороты — сферически, положения — по прямой."""
    result = {}
    for name in a:
        qa, la = a[name]
        qb, lb = b[name]
        if qa.dot(qb) < 0:
            qb = -qb
        result[name] = (qa.slerp(qb, t), la.lerp(lb, t))
    return result


def game_pose(armature, name="Take_Idle", frame=1):
    # Клипы ключуют углы Эйлера: после смешивания (put_basis) кости в режиме кватернионов, и клип бы их не сдвинул.
    reset(armature)
    armature.animation_data_create()
    armature.animation_data.action = bpy.data.actions[name]
    bpy.context.scene.frame_set(frame)
    pose = basis(armature)
    armature.animation_data.action = None
    return pose


def probe():
    import probe_table
    armature = bpy.data.objects["Rig_Hands"]
    armature.matrix_world.identity()
    skins = [("свободная", probe_table.Skin(in_game.FREE_HAND)), ("с веером", probe_table.Skin(in_game.FAN_HAND))]

    def lowest():
        return {label: skin.lowest() for label, skin in skins}

    probe_variant(armature, lowest)
    reset(armature)


def probe_variant(armature, lowest):
    worst = {}
    poses_at = {}
    for frame in FRAMES:
        pose_frame(armature, frame)
        poses_at[frame] = basis(armature)
        for label, (height, part, margin) in lowest().items():
            if label not in worst or height < worst[label][0]:
                worst[label] = (height, part, frame)
    print("=== PROBE сжатие: %s" % (
          "; ".join("%s ниже всего %.1f мм над сукном (%s, кадр %d)" % ((label,) + worst[label]) for label in worst)))
    # Вход в проверку и выход из неё: слой PoseTest плавно набирает вес над позой партии (и над руками без карт).
    for source_name in ("Take_Idle", "Take_TableRest"):
        game = game_pose(armature, source_name)
        # Выход из кулака идёт через Idle (OwnArms3DView сначала разжимает кисть): смешивается только Idle с позой партии.
        for frame, label in ((0, "Idle"),):
            low = {}
            for step in range(11):
                put_basis(armature, blend(game, poses_at[frame], step / 10))
                for hand, (height, part, margin) in lowest().items():
                    if hand not in low or height < low[hand][0]:
                        low[hand] = (height, part, step / 10)
            print("=== PROBE переход %s <-> %s: %s" % (source_name, label,
                  "; ".join("%s ниже всего %.1f мм (%s, вес %.1f)" % ((hand,) + low[hand]) for hand in low)))


# --- Кадры с места игрока (render) -------------------------------------------------------------------------------------

def render():
    os.environ.setdefault("SKIN", "smooth")
    import seat_view
    out = os.environ.get("GT_OUT") or os.path.join(source.ROOT, "screens", "3d-hands", "в-игре")
    os.makedirs(out, exist_ok=True)
    sc, armature, to_eye = seat_view.stage(rebuild=False)
    # Веер в проверке спрятан (OwnArms3DView): кисть с веером ушла на сукно.
    for key in seat_view.FAN:
        bpy.data.objects[key].hide_render = True
    armature.animation_data.action = None
    sc.cycles.samples = int(os.environ.get("GT_SAMPLES", "48"))
    sc.render.resolution_percentage = int(os.environ.get("GT_SCALE", "100"))
    tag = os.environ.get("GT_TAG", "")
    shots = ((0, "idle"), (gestures.THUMB_WRAP_FROM, "середина"), (gestures.CLOSED, "кулак"))
    # GT_ONLY=кулак — только эти кадры (через запятую).
    only = os.environ.get("GT_ONLY")
    for frame, label in [s for s in shots if not only or s[1] in only.split(",")]:
        pose_frame(armature, frame)
        # Форма кулака с весом, как в клипе проверки (game_test.bake).
        if fist_smooth.PROP in armature:
            armature[fist_smooth.PROP] = fist_smooth.weight(frame)
        armature.matrix_world = to_eye
        bpy.context.view_layer.update()
        # GT_BORDER="x0,x1,y0,y1" — только эта часть кадра (доли, y снизу), например обе кисти крупно с GT_SCALE=200.
        border = os.environ.get("GT_BORDER")
        seat_view.render(sc, os.path.join(out, "blender-%s%s.png" % (label, tag)),
                         tuple(map(float, border.split(","))) if border else None)
    print("=== GAME TEST RENDER", out)


if __name__ == "__main__":
    command = (sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else ["store"])[0]
    {"store": store, "probe": probe, "render": render}[command]()
