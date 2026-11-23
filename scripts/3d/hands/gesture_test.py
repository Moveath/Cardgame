"""Проверка новых жестов в игре (ТЗ автора 2026-11-20; этап A — «класс» и «фу»): обе кисти перед собой, как в проверке
кулака (game_test.py), из расслабленной кисти Idle показывают жест.

Клипы Take_Test<жест><руки>: первый кадр — ровно Idle проверки кулака (кадр 0 клипа Take_TestClench: та же поза кисти
и то же положение рук), последний — жест. Обратно в Idle — тот же клип назад. Руки: без приписки — жест показывают обе,
_R — только правая, _L — только левая (вторая весь клип стоит в Idle и без формы жеста). Клипы на одну руку нужны потому,
что маска слоя в Unity отделяет кости руки, но не веса форм кожи: слой одной руки писал бы вес формы и второй.
В Unity их играет слой GestureTest аниматора (HandsAnimatorBuilder.cs) по параметру времени. Включаются в игре клавишами
U («класс») и J («фу»), H — какие руки (OwnArms3DView), это временная проверка, как F.

Форма кисти — gestures_extra.py, Idle — gestures.IDLE (принят автором, здесь не меняется). Этот файл ставит руки в кадр
и собирает переход: пальцы сжимаются как в кулаке (gestures.fingers_at, те же кадры), большой палец поднимается из Idle,
предплечье поворачивается вместе с кистью.

Запуск: blender art3d/hands/CardCity_Hands.blend --background --disable-autoexec --python scripts/3d/hands/gesture_test.py -- <команда>
  store  — пересобрать клипы в .blend и сохранить его; рядом пишется art3d/hands/test/CardCity_Hands_GestureTest.json —
           расстояния от запястья до суставов пальцев в начале, середине и конце каждого клипа (по ним Table3DQa.cs сверяет
           кости в Unity), здесь же они сверяются с той же позой на руке в покое скелета.
  frames — без сохранения: кожа каждого кадра каждого клипа на руке в покое скелета (пальцы друг в друге, большой палец
           в пальцах и пальцы в нём, подушечки в ладони), как gesture_lab.py frames.
  aim    — без сохранения: куда смотрит большой палец в кадре игры (угол от вертикали экрана) при разном повороте предплечья.
  orient — без сохранения: кулак F и конец каждого жеста — куда смотрят ладонь (к середине экрана, к игроку), тыл (над
           горизонтом), ребро указательного и большой палец (от вертикали в мире и на экране).
  solve  — без сохранения: перебор поворота предплечья, отклонения и сгиба кисти под цели GS_TARGET (поля orient).
  protrude — без сохранения: насколько большой палец кулака F торчит за контур кулака с места игрока.
  render — без сохранения: кадры с места игрока (свет как в Unity) в screens/3d-hands/жесты/ (GT_OUT), GT_TAG — приписка.
Переменные: GT_CLIPS — какие клипы (через запятую), GT_SHOTS — какие кадры (start, mid, end), GT_FRAMES=N — каждый N-й
кадр перехода, GT_WEIGHT — вес формы кисти жеста в кадрах render вместо клипа; GA_ROLL, GA_ULNAR, GA_EXTEND, GA_WRIST, GA_ELBOW — положение руки жеста без правки файла."""
import json
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(__file__))
import fist_smooth  # noqa: E402
import game_test  # noqa: E402
import gestures  # noqa: E402
import gesture_smooth  # noqa: E402
import gestures_extra  # noqa: E402
import hand_anatomy  # noqa: E402
import in_game  # noqa: E402
import source  # noqa: E402

FOLDER = os.path.join(source.ROOT, "art3d", "hands", "test")
JSON = os.path.join(FOLDER, "CardCity_Hands_GestureTest.json")
# Вес корректирующей формы кисти жеста (gesture_smooth.py) — свойства скелета, своё на каждую руку (thumbs_smooth_L,
# thumbs_smooth_R), ключи только в клипах этого файла. С 2026-11-23 у «класса» своя форма ThumbsUpSmooth со свойствами
# thumbsup_smooth_L и _R (gesture_smooth.GESTURE_SHAPES), «фу» ведёт прежнюю ThumbsSmooth.
PROP = "thumbs_smooth"
PROPS = tuple(sorted({prop for prop, _, _ in gesture_smooth.SHAPES.values()}))


def shape_of(clip):
    return gesture_smooth.GESTURE_SHAPES[clip]


def prop_of(clip):
    return gesture_smooth.SHAPES[shape_of(clip)][0]
SIDES = ("_L", "_R")
# Какие руки показывают жест: приписка к имени клипа — руки.
HANDS_VARIANTS = {"": ("_L", "_R"), "_R": ("_R",), "_L": ("_L",)}

# Рука проверки кулака (game_test.py): из неё начинается каждый жест.
IDLE_ARM = dict(wrist=tuple(game_test.WRIST), elbow=tuple(game_test.ELBOW), roll=game_test.ROLL, ulnar=game_test.ULNAR,
                extend=game_test.EXTEND)

# Положение свободной руки в жесте (раскладка «веер слева», в осях от глаз, как game_test.WRIST): запястье, локоть,
# поворот предплечья от «тыл вверх» (плюс — большой палец вверх), отклонение кисти (плюс — к мизинцу, минус — к большому
# пальцу) и разгиб к тылу (минус — сгиб к ладони).
# С 2026-11-22 (замечание автора) обе руки идут от нижних углов экрана к столу, как в проверке кулака F.
# «Класс»: рука ровно как у кулака F (запястье, локоть, поворот 55°, отклонение и разгиб) — кулак виден с места игрока
# так же, костяшками и тылом, ладони смотрят друг на друга (20° от середины экрана), не вверх и не к игроку, ребро
# указательного вверх (23° от вертикали). Вертикально стоит сам большой палец: на экране строго вверх, в мире — 12°
# от вертикали (gestures_extra.THUMBS_UP). При первом варианте (поворот 92°, палец на 64°) с места игрока была видна
# ладонная сторона с согнутыми пальцами — автор: «ладони смотрят вверх, пальцы вывернуты наружу».
# «Фу»: локоть как у кулака F, запястье на 3 см выше и на 6 см дальше (иначе кисть с пальцем уходила за нижний край
# кадра), предплечье повёрнуто на −50°, кисть согнута к ладони на 20° и отклонена к большому пальцу на 10°. Тыл кисти
# смотрит вверх и к середине экрана — на 43° над горизонтом, а не плашмя; большой палец свисает почти отвесно (в мире 9°
# от вертикали, в сукно) и на экране на 25° уходит к середине — мимо рукава. С пальцем строго вертикальным на экране он
# висел за предплечьем (руки идут снизу из углов кадра) и был виден только кончиком. Наружу тыл правой кисти при пальце
# вниз не повернуть: для этого кисть должна смотреть назад, к игроку. Подбор — перебор положений руки и поворотов
# (кончик в кадре, палец не за рукавом) и кадры с места игрока.
# Первый вариант (2026-11-20, откат art3d/hands/backup/2026-11-22-повороты-жестов-до): «класс» — запястье (.17, .37, −.09),
# локоть (.24, .22, −.26), поворот 92°; «фу» — запястье (.13, .42, −.03), локоть (.33, .36, −.01), поворот −95°, отклонение −14°.
ARMS = {
    # «Класс» по модели автора (2026-11-23, thumbs_model.py arm): кисть повёрнута относительно луча от глаз игрока так же,
    # как модель относительно камеры на картинке автора (оси кисти — в пределах 0,3°; у варианта Gemini — 10°): видна
    # ладонная сторона кулака — средние фаланги стопкой и подушка большого пальца, большой палец вверх. Предплечье идёт
    # из нижнего угла, как у модели на картинке (на экране совпадает), запястье почти как у кулака F: отклонение к мизинцу
    # 7°, разгиб 10° (у F 8° и 10°). Вариант Gemini: локоть (.26, .24, −.22), поворот 74°, отклонение 10°, разгиб 14°
    # (откат art3d/hands/backup/2026-11-23-класс-по-модели-до).
    # «Класс» по 3D-модели автора (2026-11-23): рука чуть ближе в правом переднем плане, кисть повёрнута под вид модели
    # на картинке автора (ошибка <1°), предплечье заходит из нижнего правого угла под тем же углом (0.1°).
    "ThumbsUp": dict(wrist=(.155, .34, -.09), elbow=(.254, .228, -.237), roll=90, ulnar=16, extend=3),
    "ThumbsDown": dict(wrist=(.15, .39, -.05), elbow=(.28, .27, -.15), roll=-94, ulnar=-23, extend=14),
}
# Кисть в жесте: формы из gestures_extra.py (у «класса» и «фу» свой большой палец).
HANDS = {"ThumbsUp": gestures_extra.thumbs_up_pose, "ThumbsDown": gestures_extra.thumbs_down_pose}
# Длина перехода Idle → жест, кадров (30 в секунду). «Фу» дольше: предплечье поворачивается на 105°.
FRAMES = {"ThumbsUp": 20, "ThumbsDown": 24}
CLIPS = tuple(FRAMES)


def action_name(clip):
    return "Take_Test" + clip


def env_float(name, default):
    return float(os.environ.get(name, default))


def arm_params(clip, frame):
    """Положение руки на кадре: от руки проверки кулака к руке жеста, плавно (разгон и торможение)."""
    t = gestures.smooth(frame / FRAMES[clip])
    end = dict(ARMS[clip])
    # Подбор без правки файла (aim, render): GA_ROLL, GA_ULNAR, GA_EXTEND — градусы; GA_WRIST, GA_ELBOW — «x,y,z».
    for key in ("roll", "ulnar", "extend"):
        if os.environ.get("GA_" + key.upper()):
            end[key] = env_float("GA_" + key.upper(), 0)
    for key in ("wrist", "elbow"):
        if os.environ.get("GA_" + key.upper()):
            end[key] = tuple(float(v) for v in os.environ["GA_" + key.upper()].split(","))
    return {key: gestures.lerp(IDLE_ARM[key], end[key], t) for key in IDLE_ARM}


def hand_pose(clip, frame):
    """Форма кисти на кадре перехода. Кадр 0 — ровно Idle проверки кулака (gestures.closing(0)). Четыре пальца сжимаются
    как в кулаке, за те же кадры (gestures.FINGERS_CLOSED); большой палец поднимается из Idle весь переход."""
    if frame == 0:
        pose = gestures.closing(0)
    else:
        target = HANDS[clip]()
        bend = gestures.smooth(frame / gestures.FINGERS_CLOSED)
        lift = gestures.smooth(frame / FRAMES[clip])
        pose = {key: gestures.lerp(gestures.IDLE[key], target[key], lift) for key in ("thumb", "thumb_turn", "thumb_bend")}
        pose.update(fingers=gestures.fingers_at(bend, target.get("fingers")),
                    cup=gestures.lerp(gestures.IDLE["cup"], target.get("cup", gestures.FIST["cup"]), bend))
    return {key: value for key, value in pose.items() if key != "wrist"}


def weight(frame):
    """Вес формы кисти жеста: как у формы кулака — входит к концу сжатия пальцев."""
    return fist_smooth.weight(frame)


def pose_frame(armature, clip, frame, hands=""):
    """Обе руки на кадре клипа; рука, которой нет в варианте hands, стоит в Idle (кадр 0)."""
    game_test.reset(armature)
    moving = HANDS_VARIANTS[hands]
    placed = []
    for side in SIDES:
        at = frame if side in moving else 0
        free = game_test.free_arm(**arm_params(clip, at))
        # free — рука в раскладке «веер слева» для свободной (правой) руки; руке с веером — зеркально.
        arm = in_game.game_arm(free if side == in_game.FREE_HAND else game_test.mirrored(free))
        game_test.place_arm(armature, arm, side)
        placed.append((side, at))
    for side, at in placed:
        hand_anatomy.apply(armature, hand_pose(clip, at), side)
    bpy.context.view_layer.update()


def set_weight(armature, value, hands="", clip=None):
    """Вес формы жеста clip на руках варианта hands (у второй руки 0, у формы другого жеста 0; без clip — у всех форм):
    свойства скелета и сами ключи формы (драйвер от свойства без update_tag в Blender не пересчитывается)."""
    for shape, (prop, _, _) in gesture_smooth.SHAPES.items():
        for side in SIDES:
            side_value = value if side in HANDS_VARIANTS[hands] and (clip is None or shape == shape_of(clip)) else 0.
            if prop + side in armature:
                armature[prop + side] = side_value
            keys = bpy.data.objects["Arm_Male" + side].data.shape_keys
            block = keys.key_blocks.get(shape) if keys else None
            if block is not None:
                block.value = side_value
    armature.update_tag()


# --- Клипы (store) -----------------------------------------------------------------------------------------------------

def marks_of(clip):
    n = FRAMES[clip]
    return (0, n // 2, n)


def bake(armature, clip, hands=""):
    """Ключ на каждый кадр углами Эйлера и положением, как game_test.bake. Вес формы кулака — 0 (его ключ допишет export.py),
    вес формы жеста — ключи свойств prop_of(clip) _L и _R (у руки, которой нет в варианте hands, — 0); формы другого
    жеста в клипе нет — её ключ 0 допишет export.py."""
    prop = prop_of(clip)
    name = action_name(clip) + hands
    old = bpy.data.actions.get(name)
    if old is not None:
        bpy.data.actions.remove(old)
    action = bpy.data.actions.new(name)
    action.use_fake_user = True
    armature.animation_data_create()
    armature.animation_data.action = action
    previous, marks = {}, {}
    for frame in range(FRAMES[clip] + 1):
        pose_frame(armature, clip, frame, hands)
        if frame in marks_of(clip):
            marks[frame] = game_test.distances(armature)
        for bone in armature.pose.bones:
            if bone.name == "Root" or "Socket" in bone.name:
                continue
            euler = bone.matrix_basis.to_euler("XYZ", previous[bone.name]) if bone.name in previous else bone.matrix_basis.to_euler("XYZ")
            previous[bone.name] = euler
            bone.rotation_euler = euler
            bone.keyframe_insert("rotation_euler", frame=frame + 1)
            bone.keyframe_insert("location", frame=frame + 1)
        for side in SIDES:
            if prop + side in armature:
                armature[prop + side] = weight(frame) if side in HANDS_VARIANTS[hands] else 0.
                armature.keyframe_insert('["%s"]' % (prop + side), frame=frame + 1)
    armature.animation_data.action = None
    for side in SIDES:
        if prop + side in armature:
            armature[prop + side] = 0.
    game_test.reset(armature)
    return marks


def rest_marks(armature, clip):
    result = {}
    for frame in marks_of(clip):
        game_test.reset(armature)
        for side in ("_L", "_R"):
            hand_anatomy.apply(armature, hand_pose(clip, frame), side)
        bpy.context.view_layer.update()
        result[frame] = game_test.distances(armature)
    game_test.reset(armature)
    return result


def selected():
    names = os.environ.get("GT_CLIPS")
    return [c for c in CLIPS if not names or c in names.split(",")]


def store():
    armature = bpy.data.objects["Rig_Hands"]
    armature.matrix_world.identity()
    # Прежнее общее свойство веса (до весов по рукам) — убрать, если осталось.
    if PROP in armature:
        del armature[PROP]
    for prop in PROPS:
        for side in SIDES:
            if prop + side not in armature:
                armature[prop + side] = 0.
    # Кадр 0 каждого жеста — тот же Idle, что у проверки кулака: в Unity слой жеста включается поверх неё незаметно.
    game_test.pose_frame(armature, 0)
    idle = {b.name: b.matrix.copy() for b in armature.pose.bones}
    out = []
    for clip in CLIPS:
        # Варианты на одну руку: кости руки жеста — как в варианте обеими руками, второй руки — как в Idle.
        reference = rest_marks(armature, clip)
        for hands in ("_R", "_L"):
            single = bake(armature, clip, hands)
            worst_single = max(abs(single[f][s][b] - (reference[f] if s in HANDS_VARIANTS[hands] else reference[0])[s][b])
                               for f in single for s in SIDES for b in single[f][s])
            print("=== GESTURE TEST %s: рука жеста и рука в Idle против руки в покое %.4f мм" % (action_name(clip) + hands, worst_single))
            if worst_single > .01:
                raise SystemExit("=== GESTURE TEST FAILED: %s%s" % (clip, hands))
        marks = bake(armature, clip)
        pose_frame(armature, clip, 0)
        start = max((b.matrix.translation - idle[b.name].translation).length * 1000 for b in armature.pose.bones)
        worst = max(abs(marks[f][s][b] - reference[f][s][b]) for f in marks for s in ("_L", "_R") for b in marks[f][s])
        print("=== GESTURE TEST %s: %d кадров; кадр 0 от Idle проверки кулака %.4f мм; форма кисти против руки в покое %.4f мм"
              % (action_name(clip), FRAMES[clip] + 1, start, worst))
        if worst > .01 or start > .01:
            raise SystemExit("=== GESTURE TEST FAILED: %s" % clip)
        out += [{"clip": action_name(clip), "frame": f, "frames": FRAMES[clip] + 1, "side": s, "bone": b, "mm": mm}
                for f in marks for s, bones in marks[f].items() for b, mm in bones.items()]
    game_test.reset(armature)
    with open(JSON, "w", encoding="utf-8") as f:
        json.dump({"fps": gestures.FPS, "marks": out}, f)
    bpy.ops.wm.save_mainfile()
    print("=== GESTURE TEST STORED %s (обе руки, _R, _L) -> %s" % (", ".join(action_name(c) for c in CLIPS), JSON))


# --- Кожа на кадрах (frames) -------------------------------------------------------------------------------------------

def frames():
    import gesture_lab
    armature = bpy.data.objects["Rig_Hands"]
    armature.animation_data_create()
    armature.animation_data.action = None
    armature.matrix_world.identity()
    side = os.environ.get("GT_SIDE", "_R")
    skin = gesture_lab.Skin(armature, side)
    for clip in selected():
        worst = dict(share=0., finger=0., thumb=0., tips=0.)
        for frame in range(FRAMES[clip] + 1):
            gesture_lab.reset(armature)
            hand_anatomy.apply(armature, hand_pose(clip, frame), side)
            set_weight(armature, weight(frame), clip=clip)
            bpy.context.view_layer.update()
            m = gesture_lab.measure(armature, skin, "%s:%d" % (clip, frame), quiet=True)
            thumb = max(m["thumb_in"], m["fingers_in_thumb"])
            tips = max(v[0] for v in m["palm"].values())
            print("=== FRAME %s %s %2d вес %.2f: пересечения %4.1f %% (без большого %4.1f %%), палец в пальце %4.1f мм, большой палец %4.1f мм, подушечки в ладонь %4.1f мм"
                  % (clip, side, frame, weight(frame), m["share"], m["share_fingers"], m["finger_depth"], thumb, tips))
            worst = dict(share=max(worst["share"], m["share"]), finger=max(worst["finger"], m["finger_depth"]),
                         thumb=max(worst["thumb"], thumb), tips=max(worst["tips"], tips))
        print("=== FRAMES %s худший кадр: пересечения %.1f %%, палец в пальце %.1f мм, большой палец %.1f мм, подушечки в ладонь %.1f мм"
              % (clip, worst["share"], worst["finger"], worst["thumb"], worst["tips"]))
    set_weight(armature, 0.)
    gesture_lab.reset(armature)


# --- Куда смотрит большой палец в кадре (aim) --------------------------------------------------------------------------

def thumb_on_screen(armature, side):
    """Угол большого пальца (от пястно-фалангового сустава к кончику) от вертикали экрана, градусы: 0 — вверх, 180 — вниз,
    плюс — кончиком наружу от середины кадра. И доля пальца поперёк экрана (1 — палец во всю длину, 0 — смотрит в камеру)."""
    bones = armature.pose.bones
    d = bones["TipThumb" + side].tail - bones["Thumb02" + side].head
    # Оси модели — от глаз: X вправо, Y по взгляду, Z вверх поперёк взгляда.
    outward = 1 if side == "_R" else -1
    angle = math.degrees(math.atan2(d.x * outward, d.z))
    return angle, math.hypot(d.x, d.z) / d.length


def aim():
    armature = bpy.data.objects["Rig_Hands"]
    armature.matrix_world.identity()
    for clip in selected():
        base = ARMS[clip]["roll"]
        for roll in range(int(base) - 30, int(base) + 31, 5):
            os.environ["GA_ROLL"] = str(roll)
            pose_frame(armature, clip, FRAMES[clip])
            row = ["%s %.0f°, во всю длину %.2f" % ((side,) + thumb_on_screen(armature, side)) for side in ("_L", "_R")]
            print("=== AIM %s поворот предплечья %4d°: %s" % (clip, roll, "; ".join(row)))
        del os.environ["GA_ROLL"]
    game_test.reset(armature)


# --- Куда смотрят ладонь, тыл, ребро и большой палец (orient, solve) -------------------------------------------------

# Из осей от глаз в мир: взгляд наклонён к столу на in_game.PITCH (32°), X вправо, Z мира — вверх.
EYE_TO_WORLD = Matrix.Rotation(-in_game.PITCH, 3, "X")
UP = Vector((0, 0, 1))


def deg(a, b):
    return math.degrees(a.angle(b)) if a.length and b.length else 0.


def screen_angle(p1, p2, side):
    """Угол отрезка p1 → p2 на экране от вертикали вверх, градусы (с перспективой): 0 — вверх, 180 — вниз, плюс — наружу
    от середины кадра."""
    d = Vector((p2.x / p2.y - p1.x / p1.y, p2.z / p2.y - p1.z / p1.y))
    outward = 1 if side == "_R" else -1
    return math.degrees(math.atan2(d.x * outward, d.y))


def describe(axes, thumb, side):
    """Числа позы руки по осям кисти (к большому пальцу, вдоль, к тылу) и направлению большого пальца, всё в осях от глаз:
    ладонь — от середины экрана (0° — к середине) и от игрока (0° — к игроку); тыл — над горизонтом в мире (90° — вверх
    плашмя, 0° — вбок) и наружу; ребро указательного — от вертикали мира; ось кисти — над горизонтом мира; большой палец —
    от вертикали мира (0° — вверх, 180° — вниз)."""
    toward, along, back = axes
    inward = Vector((-1, 0, 0)) if side == "_R" else Vector((1, 0, 0))
    palm = -back
    w_back, w_toward, w_along, w_thumb = (EYE_TO_WORLD @ v for v in (back, toward, along, thumb))
    elevation = lambda v: math.degrees(math.asin(max(-1., min(1., v.normalized().z))))
    out = 1 if side == "_R" else -1
    return dict(palm_center=deg(palm, inward), palm_player=deg(palm, Vector((0, -1, 0))),
                back_up=elevation(w_back), back_out=w_back.x * out,
                edge=deg(w_toward, UP), along_up=elevation(w_along), thumb_world=deg(w_thumb, UP),
                # Большой палец в мире: наружу от середины, вперёд от игрока, вверх.
                thumb_out=w_thumb.x * out, thumb_fwd=w_thumb.y, thumb_up=w_thumb.z)


def show_orient(name, side, m, screen=None):
    print("=== ORIENT %-9s %s: ладонь от середины экрана %3.0f°, от игрока %3.0f°; тыл над горизонтом %3.0f° (наружу %.2f); "
          "ребро указательного от вертикали %3.0f°; ось кисти над горизонтом %3.0f°; большой палец от вертикали %3.0f° "
          "(наружу %.2f, вперёд %.2f, вверх %.2f)%s"
          % (name, side, m["palm_center"], m["palm_player"], m["back_up"], m["back_out"], m["edge"], m["along_up"],
             m["thumb_world"], m["thumb_out"], m["thumb_fwd"], m["thumb_up"], "" if screen is None else ", на экране %4.0f°" % screen))


def rig_orient(armature, side):
    bones = armature.pose.bones
    axes = hand_anatomy.hand_axes(bones["Hand" + side].matrix, side)
    thumb = (bones["TipThumb" + side].tail - bones["Thumb02" + side].head).normalized()
    return describe(axes, thumb, side), screen_angle(bones["Thumb02" + side].head, bones["TipThumb" + side].tail, side)


def orient():
    armature = bpy.data.objects["Rig_Hands"]
    armature.matrix_world.identity()
    game_test.pose_frame(armature, gestures.CLOSED)
    for side in SIDES:
        show_orient("Fist", side, *rig_orient(armature, side))
    for clip in selected():
        pose_frame(armature, clip, FRAMES[clip])
        for side in SIDES:
            show_orient(clip, side, *rig_orient(armature, side))
    game_test.reset(armature)


def thumb_in_hand(armature, pose):
    """Направление большого пальца (от пястно-фалангового сустава к кончику) в осях кисти — для позы pose, на руке в покое."""
    game_test.reset(armature)
    hand_anatomy.apply(armature, pose, "_R")
    bpy.context.view_layer.update()
    bones = armature.pose.bones
    axes = hand_anatomy.hand_axes(bones["Hand_R"].matrix, "_R")
    d = (bones["TipThumb_R"].tail - bones["Thumb02_R"].head).normalized()
    game_test.reset(armature)
    return Vector([d.dot(a) for a in axes])


def solve():
    """Перебор поворота предплечья, отклонения и сгиба кисти при предплечье проверки кулака (или GA_WRIST, GA_ELBOW) под
    цели GS_TARGET: thumb_world=<градусы>,palm_center=…,back_up=…,edge=… (поля describe). Печатает лучшие."""
    armature = bpy.data.objects["Rig_Hands"]
    armature.matrix_world.identity()
    clip = selected()[0]
    # GS_THUMB="x,y,z" — направление большого пальца в осях кисти вместо позы из gestures_extra (проба другой формы).
    local = (Vector(tuple(float(v) for v in os.environ["GS_THUMB"].split(","))).normalized() if os.environ.get("GS_THUMB")
             else thumb_in_hand(armature, HANDS[clip]()))
    targets = {k: float(v) for k, v in (p.split("=") for p in os.environ["GS_TARGET"].split(","))}
    weights = {"thumb_world": 3., "thumb_screen": 2.}
    wrist = tuple(float(v) for v in os.environ.get("GA_WRIST", "%f,%f,%f" % tuple(game_test.WRIST)).split(","))
    elbow = tuple(float(v) for v in os.environ.get("GA_ELBOW", "%f,%f,%f" % tuple(game_test.ELBOW)).split(","))
    found = []
    for roll in range(-180, 181, 5):
        for dev in range(-20, 31, 5):
            for ext in range(-60, 41, 5):
                arm = game_test.free_arm(wrist=wrist, elbow=elbow, roll=roll, ulnar=dev, extend=ext)
                frame = in_game.frame(arm["wrist"], arm["along"], arm["back"]).to_3x3()
                axes = hand_anatomy.hand_axes(frame, "_R")
                thumb = axes[0] * local.x + axes[1] * local.y + axes[2] * local.z
                m = describe(axes, thumb, "_R")
                tip = Vector(wrist) + thumb * .08
                m["thumb_screen"] = screen_angle(Vector(wrist), tip, "_R")
                # Тыл к камере: 0° — тыл кисти смотрит прямо в камеру (плашмя к игроку), 90° — ребром.
                m["back_camera"] = deg(axes[2], -Vector(wrist))
                cost = sum(weights.get(k, 1.) * abs((m[k] - v + 180) % 360 - 180) for k, v in targets.items())
                # Живые пределы: отклонение к мизинцу 30°, к большому пальцу 20°, разгиб 40°, сгиб 60° — и чем ближе к покою, тем лучше.
                cost += .05 * (abs(dev) + abs(ext))
                found.append((cost, roll, dev, ext, m))
    found.sort(key=lambda f: f[0])
    for cost, roll, dev, ext, m in found[:12]:
        print("=== SOLVE %s цена %.1f: поворот %4d°, отклонение %3d°, сгиб/разгиб %3d° | %s" % (
            clip, cost, roll, dev, ext, ", ".join("%s %.0f" % (k, v) for k, v in m.items())))
    game_test.reset(armature)


def outline_excess(skin, pick):
    """Насколько вершины pick (фаланги большого пальца) выходят на экране за выпуклый контур остальной кожи кисти, мм на
    глубине кисти (оси от глаз, перспектива), и какая фаланга выходит дальше всех."""
    from mathutils.geometry import convex_hull_2d
    rest = [(p.x / p.y, p.z / p.y) for p, o in zip(skin.co, skin.owner) if o is not None and not pick(o)]
    hull = [rest[i] for i in convex_hull_2d(rest)]
    worst, part = 0., None
    for p, o in zip(skin.co, skin.owner):
        if o is None or not pick(o):
            continue
        u, v = p.x / p.y, p.z / p.y
        # Выпуклый контур против часовой стрелки: снаружи — за любой из рёбер; расстояние — до ближайшего ребра снаружи.
        out = 0.
        for (ax, ay), (bx, by) in zip(hull, hull[1:] + hull[:1]):
            ex, ey = bx - ax, by - ay
            cross = ex * (v - ay) - ey * (u - ax)
            if cross < 0:
                out = max(out, -cross / math.hypot(ex, ey))
        mm = out * p.y * 1000
        if mm > worst:
            worst, part = mm, o
    return worst, part


def protrude():
    """Большой палец в кулаке проверки F с места игрока: насколько он торчит за контур кулака на экране (мм на глубине
    кисти) и где его кончик относительно сгиба указательного (среднего сустава)."""
    import gesture_lab
    armature = bpy.data.objects["Rig_Hands"]
    armature.matrix_world.identity()
    game_test.pose_frame(armature, gestures.CLOSED)
    if fist_smooth.PROP in armature:
        armature[fist_smooth.PROP] = 1.
        armature.update_tag()
        for name in ("Arm_Male_L", "Arm_Male_R"):
            for block in bpy.data.objects[name].data.shape_keys.key_blocks[1:]:
                block.value = 1. if block.name in ("FistSmooth", "FistThumb") else 0.
    bpy.context.view_layer.update()
    for side in SIDES:
        skin = gesture_lab.Skin(armature, side).shot()
        worst, part = outline_excess(skin, lambda o: o[0] == "Thumb" and o[1] in (2, 3))
        bones = armature.pose.bones
        tip = bones["TipThumb" + side].tail
        fold = bones["Index02" + side].head
        print("=== PROTRUDE Fist %s: большой палец за контуром кулака на экране %.1f мм (фаланга %s); кончик от среднего сустава указательного %.0f мм"
              % (side, worst, part, (tip - fold).length * 1000))
    game_test.reset(armature)


# --- Кадры с места игрока (render) -------------------------------------------------------------------------------------

def render():
    os.environ.setdefault("SKIN", "smooth")
    import seat_view
    out = os.environ.get("GT_OUT") or os.path.join(source.ROOT, "screens", "3d-hands", "жесты")
    os.makedirs(out, exist_ok=True)
    sc, armature, to_eye = seat_view.stage(rebuild=False)
    for key in seat_view.FAN:
        bpy.data.objects[key].hide_render = True
    armature.animation_data.action = None
    sc.cycles.samples = int(os.environ.get("GT_SAMPLES", "48"))
    sc.render.resolution_percentage = int(os.environ.get("GT_SCALE", "100"))
    tag = os.environ.get("GT_TAG", "")
    border = os.environ.get("GT_BORDER")
    shots = os.environ.get("GT_SHOTS", "start,mid,end").split(",")
    for clip in selected():
        n = FRAMES[clip]
        frames_list = [(label, frame) for label, frame in (("start", 0), ("mid", n // 2), ("end", n)) if label in shots]
        # GT_FRAMES=5 — каждый пятый кадр перехода и последний (промежуточные кадры), подписи f00, f05…
        if os.environ.get("GT_FRAMES"):
            every = int(os.environ["GT_FRAMES"])
            frames_list = [("f%02d" % f, f) for f in sorted(set(list(range(0, n, every)) + [n]))]
        for label, frame in frames_list:
            armature.matrix_world.identity()
            pose_frame(armature, clip, frame)
            # GT_WEIGHT — вес формы кисти жеста вместо того, что в клипе (сравнить кожу с формой и без).
            set_weight(armature, float(os.environ["GT_WEIGHT"]) if os.environ.get("GT_WEIGHT") else weight(frame), clip=clip)
            armature.matrix_world = to_eye
            bpy.context.view_layer.update()
            seat_view.render(sc, os.path.join(out, "blender-%s-%s%s.png" % (clip, label, tag)),
                             tuple(map(float, border.split(","))) if border else None)
    print("=== GESTURE TEST RENDER", out)


if __name__ == "__main__":
    command = (sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else ["store"])[0]
    {"store": store, "frames": frames, "aim": aim, "render": render, "orient": orient, "solve": solve, "protrude": protrude}[command]()
