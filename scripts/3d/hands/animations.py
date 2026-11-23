"""Анимации рук в партии: ключевые кадры поверх позы партии (in_game.py). Каждая начинается и кончается позой партии,
поэтому в Unity её можно включить в любой момент без рывка. Действия называются Take_*, в FBX это клипы Rig_Hands|Take_*: по алфавиту они идут после Pose_Base, а Unity берёт положение покоя из первого клипа файла.
Ключ — кадр и поза обеих рук в тех же числах, что поза партии: где запястье, куда смотрят кисть и тыл, сгибы пальцев."""
import copy

import bpy

import game_test
import gesture_test
import gestures
import gestures_extra
import in_game
import poses
from in_game import FAN_RIGHT, FAN_UP, LEFT, LEFT_ON_TABLE, RIGHT, on_fan, fan_dir, on_table, table_dir

FPS = 30
PREFIX = "Take_"


def hand(fingers, thumb):
    return {"fingers": fingers, "thumb": thumb, "wrist": (0, 0)}


def arm(base, **changes):
    result = copy.deepcopy(base)
    result.update(changes)
    return result


def curl(base, degrees, thumb=None):
    """Та же поза, пальцы сильнее (или слабее) согнуты на degrees в каждом суставе."""
    fingers = {k: (v[0], v[1] + degrees, v[2] + degrees, v[3] + degrees / 2) for k, v in base["pose"]["fingers"].items()}
    return arm(base, pose=hand(fingers, thumb or base["pose"]["thumb"]))


def squeezed(base, degrees):
    """Рука с веером поджимает пальцы за картами: указательный, средний и мизинец сильнее согнуты в основании, безымянный
    остаётся на месте. В хвате он лежит вплотную между средним и мизинцем, и если согнуть его вместе со всеми (curl),
    его кончик входит в кончик среднего: 8 пересечений уже при 2° (замер по сетке, probe_overlap.py)."""
    fingers = {k: (v[0], v[1] + (0 if k == "Ring" else degrees), v[2], v[3]) for k, v in base["pose"]["fingers"].items()}
    return arm(base, pose=hand(fingers, base["pose"]["thumb"]))


def moved(base, offset):
    """Та же рука, сдвинутая целиком, вместе с точками большого пальца."""
    result = arm(base, wrist=base["wrist"] + offset)
    if "thumb" in base:
        result["thumb"] = tuple(t + offset for t in base["thumb"])
    return result


# Кулак по видео-образцам автора (references/hands/видео-образцы, «класс» и удар кулаком): основания пальцев согнуты на
# прямой угол, средние фаланги лежат поперёк, а дальний сустав согнут до предела (75°) — подушечки упираются в ладонь.
# Раньше дальний сустав гнулся на 50°: кончики пальцев с ногтями висели над ладонью и торчали из кулака.
FIST = {"Index": (0, 92, 94, 75), "Middle": (0, 92, 98, 75), "Ring": (0, 90, 94, 70), "Pinky": (0, 86, 90, 60)}
# «Класс» и «фу»: большой палец прямой и отведён от кулака почти под прямым углом, а не вдоль пальцев.
STRAIGHT_THUMB = ((12, 12, -50), 0, 0)
TUCKED_FIST = FIST
# Кулак: большой палец лежит сверху вдоль указательного, как на видео-образце (положение снято с него библиотекой).
TUCKED_THUMB = ((-30, 30, -14), 18, 45)

def anatomy_arm(free_spec, anatomy_pose, weight=1.):
    """Свободная рука с анатомической позой кисти (hand_anatomy.py): при weight=0 стоит точно в позе партии RIGHT,
    при weight=1 — в анатомической позе anatomy_pose."""
    return dict(free_spec, pose=RIGHT["pose"], anatomy={k: v for k, v in anatomy_pose.items() if k != "wrist"}, anatomy_weight=weight)


# Указательный показывает через стол на соперника: кисть поднята, палец вперёд и чуть вверх.
POINT = {
    "wrist": on_table(.15, .44, .15), "along": table_dir(-.15, .97, .2), "back": table_dir(.25, -.05, 1),
    "elbow_from_wrist": table_dir(.25, -.9, -.35), "pose": hand(poses.POSES["Point"]["fingers"], poses.POSES["Point"]["thumb"]),
}
# Три переработанных жеста на анатомической системе кисти (hand_anatomy.py, gestures.py, gestures_extra.py):
# рука поднимается с сукна в позу перед игроком (game_test.free_arm / gesture_test.ARMS) и возвращается обратно на сукно.
RIGHT_ANATOMY_BASE = anatomy_arm(RIGHT, gestures.IDLE, 0.)
HOVER_ANATOMY = anatomy_arm(game_test.free_arm(), gestures.IDLE, 1.)
THUMBS_UP = anatomy_arm(game_test.free_arm(**gesture_test.ARMS["ThumbsUp"]), gestures_extra.thumbs_up_pose(), 1.)
# «Класс» по модели автора (2026-11-23): из HOVER_ANATOMY в THUMBS_UP рука и кисть идут ключом на каждый кадр тем же путём,
# что в проверке U (gesture_test.arm_params, hand_pose): пальцы сжимаются через промежуточные плоскости сгиба
# (gestures.fingers_at) и не скрещиваются. По прямой между позами (mix) средние фаланги на полпути входили друг в друга
# на 4,2 мм. HOVER_ANATOMY — ровно кадр 0 проверки (рука кулака F, кисть Idle). Путь — 21 кадр проверки за 10 кадров клипа.
THUMBS_UP_PATH = [anatomy_arm(game_test.free_arm(**gesture_test.arm_params("ThumbsUp", f)), gesture_test.hand_pose("ThumbsUp", f), 1.)
                  for f in range(0, gesture_test.FRAMES["ThumbsUp"] + 1, 2)]
THUMBS_DOWN = anatomy_arm(game_test.free_arm(**gesture_test.ARMS["ThumbsDown"]), gestures_extra.thumbs_down_pose(), 1.)
FIST_READY = anatomy_arm(game_test.free_arm(), gestures.closing(gestures.THUMB_READY_AT), 1.)
FIST_UP = anatomy_arm(game_test.free_arm(), gestures.FIST, 1.)
FIST_DOWN = moved(FIST_UP, table_dir(0, .01, .014))
# Кисть раскрыта над сукном перед тем, как лечь: из кулака она разворачивается в воздухе, а не сквозь стол.
HOVER = curl(moved(RIGHT, table_dir(0, 0, .04)), 14)
# Перед кулаком кисть поднимается выше: пока она поворачивается на ребро, пальцы ещё не сжаты и свисают вниз.
# С высоты HOVER мизинец на этом повороте уходил в сукно на 10 мм (замер по коже, probe_table.py).
HOVER_HIGH = curl(moved(RIGHT, table_dir(0, 0, .075)), 14)
# Стук по столу: кисть приподнимается от запястья (пальцы расслабленно свисают) и падает подушечками на сукно.
# Все четыре пальца идут вместе, как у живой руки: раньше стучали два, а безымянный с мизинцем были согнуты отдельно.
TAP_FINGERS_UP = {"Index": (-7, 5, 4, 2), "Middle": (0, 7, 4, 2), "Ring": (4, 7, 4, 2), "Pinky": (10, 9, 4, 2)}
TAP_FINGERS_DOWN = {"Index": (-7, 4.3, -2, 0), "Middle": (0, 2.1, -2, 0), "Ring": (4, -2.2, -3, 0), "Pinky": (10, .2, -2, 0)}
TAP_UP = arm(RIGHT, wrist=on_table(.19, .47, .061), along=table_dir(-.38, .92, .10), pose=hand(TAP_FINGERS_UP, RIGHT["pose"]["thumb"]))
TAP_DOWN = arm(RIGHT, wrist=on_table(.19, .47, .046), along=table_dir(-.38, .92, -.06), pose=hand(TAP_FINGERS_DOWN, RIGHT["pose"]["thumb"]))
SHRUG_RIGHT = {
    "wrist": on_table(.22, .52, .13), "along": table_dir(-.45, .85, .2), "back": table_dir(.1, .15, -1),
    "elbow_from_wrist": table_dir(.3, -.8, -.45),
    "pose": hand({"Index": (8, 4, 6, 2), "Middle": (2, 6, 8, 3), "Ring": (-4, 8, 10, 4), "Pinky": (-10, 10, 12, 4)}, ((-20, 0, -20), 4, 4)),
}
SHRUG_LEFT = moved(LEFT, FAN_UP * .03 - FAN_RIGHT * .012)
# Правая рука берёт верхний край крайней правой карты веера: большой палец перед картой, указательный за ней.
AT_FAN = {
    "wrist": on_fan(.13, .0, -.035), "along": fan_dir(-.55, .8, .2), "back": fan_dir(.35, .15, -1),
    "elbow_from_wrist": table_dir(.4, -.7, -.6), "pose": hand(poses.POSES["TakeCard"]["fingers"], poses.POSES["TakeCard"]["thumb"]),
}
OVER_TABLE = {
    "wrist": on_table(.08, .74, .09), "along": table_dir(-.3, .9, -.2), "back": table_dir(.2, .1, 1),
    "elbow_from_wrist": table_dir(.3, -.9, -.2), "pose": hand(poses.POSES["PlayCard"]["fingers"], poses.POSES["PlayCard"]["thumb"]),
}
RELEASED = curl(OVER_TABLE, -14)
TOWARD_DECK = {
    "wrist": on_table(-.02, .8, .09), "along": table_dir(-.45, .88, -.1), "back": table_dir(.1, .1, 1),
    "elbow_from_wrist": table_dir(.4, -.9, -.15), "pose": hand(poses.POSES["TakeCard"]["fingers"], poses.POSES["TakeCard"]["thumb"]),
}
LEFT_THUMB_SLID = arm(LEFT, thumb=tuple(t + FAN_RIGHT * .004 + FAN_UP * .005 for t in LEFT["thumb"]))

# Клип: (кадр, левая рука, правая рука). Последний ключ — снова поза партии.
CLIPS = {
    # Дыхание и мелкие движения пальцев, по кругу: начало и конец совпадают.
    "Idle": [
        (0, LEFT, RIGHT),
        (40, squeezed(moved(LEFT, FAN_UP * .002 + FAN_RIGHT * .001), 2), curl(moved(RIGHT, table_dir(0, 0, .003)), 1)),
        (80, moved(LEFT, FAN_UP * -.0015), curl(RIGHT, -2)),
        (120, LEFT, RIGHT),
    ],
    # Карт в руке нет: обе кисти лежат на сукне и дышат, как в Idle. По кругу: начало и конец совпадают.
    "TableRest": [
        (0, LEFT_ON_TABLE, RIGHT),
        (40, curl(moved(LEFT_ON_TABLE, table_dir(0, 0, .003)), 1), curl(moved(RIGHT, table_dir(0, 0, .003)), 1)),
        (80, curl(LEFT_ON_TABLE, -2), curl(RIGHT, -2)),
        (120, LEFT_ON_TABLE, RIGHT),
    ],
    # Рука с веером перехватывает его: большой палец съезжает и снова прижимает карты, пальцы за картами поджимаются.
    "HoldCards": [
        (0, LEFT, RIGHT),
        (18, LEFT_THUMB_SLID, RIGHT),
        (34, squeezed(LEFT_THUMB_SLID, 5), RIGHT),
        (52, squeezed(LEFT, 2), RIGHT),
        (72, LEFT, RIGHT),
    ],
    "DrawCard": [
        (0, LEFT, RIGHT),
        (16, LEFT, TOWARD_DECK),
        (22, LEFT, curl(TOWARD_DECK, 8)),
        (40, LEFT, AT_FAN),
        (48, LEFT, curl(AT_FAN, -10)),
        (64, LEFT, RIGHT),
    ],
    "PlayCard": [
        (0, LEFT, RIGHT),
        (14, LEFT, AT_FAN),
        (18, LEFT, curl(AT_FAN, 6)),
        (32, LEFT, OVER_TABLE),
        (36, LEFT, RELEASED),
        (54, LEFT, RIGHT),
    ],
    "Point": [
        (0, LEFT, RIGHT),
        (5, LEFT, HOVER),
        (14, LEFT, POINT),
        (22, LEFT, moved(POINT, table_dir(-.004, .03, .006))),
        (30, LEFT, POINT),
        (38, LEFT, moved(POINT, table_dir(-.004, .03, .006))),
        (50, LEFT, POINT),
        (60, LEFT, HOVER),
        (68, LEFT, RIGHT),
    ],
    "TapTable": [
        (0, LEFT, RIGHT),
        (6, LEFT, TAP_UP), (10, LEFT, TAP_DOWN),
        (16, LEFT, TAP_UP), (20, LEFT, TAP_DOWN),
        (26, LEFT, TAP_UP), (30, LEFT, TAP_DOWN),
        (44, LEFT, RIGHT),
    ],
    "ThumbsUp": [
        (0, LEFT, RIGHT_ANATOMY_BASE),
        *[(8 + i, LEFT, pose) for i, pose in enumerate(THUMBS_UP_PATH)],
        (26, LEFT, moved(THUMBS_UP, table_dir(0, .01, .012))),
        (56, LEFT, THUMBS_UP),
        (74, LEFT, moved(THUMBS_UP, table_dir(0, .006, .008))),
        *[(90 + i, LEFT, pose) for i, pose in enumerate(THUMBS_UP_PATH[::-1])],
        (110, LEFT, RIGHT_ANATOMY_BASE),
    ],
    "ThumbsDown": [
        (0, LEFT, RIGHT_ANATOMY_BASE),
        (8, LEFT, HOVER_ANATOMY),
        (18, LEFT, THUMBS_DOWN),
        (26, LEFT, moved(THUMBS_DOWN, table_dir(0, .01, -.012))),
        (54, LEFT, THUMBS_DOWN),
        (72, LEFT, moved(THUMBS_DOWN, table_dir(0, .006, -.008))),
        (88, LEFT, THUMBS_DOWN),
        (98, LEFT, HOVER_ANATOMY),
        (108, LEFT, RIGHT_ANATOMY_BASE),
    ],
    "Shrug": [
        (0, LEFT, RIGHT),
        (14, SHRUG_LEFT, SHRUG_RIGHT),
        (38, SHRUG_LEFT, curl(SHRUG_RIGHT, -4)),
        (54, LEFT, RIGHT),
    ],
    "Fist": [
        (0, LEFT, RIGHT_ANATOMY_BASE),
        (7, LEFT, HOVER_ANATOMY),
        (12, LEFT, FIST_READY),
        (18, LEFT, FIST_UP),
        (25, LEFT, FIST_DOWN),
        (52, LEFT, FIST_UP),
        (70, LEFT, moved(FIST_UP, table_dir(0, .006, .009))),
        (86, LEFT, FIST_UP),
        (92, LEFT, FIST_READY),
        (98, LEFT, HOVER_ANATOMY),
        (106, LEFT, RIGHT_ANATOMY_BASE),
    ],
}
# Кадр, по которому видно жест: для листа проверки.
PEAKS = {"Idle": 40, "TableRest": 40, "HoldCards": 34, "DrawCard": 22, "PlayCard": 32, "Point": 22, "TapTable": 10,
         "ThumbsUp": 26, "ThumbsDown": 26, "Shrug": 30, "Fist": 25}


def mix(a, b, t):
    """Поза руки между двумя ключами: точки и направления смешиваются сами, а не углы костей. Так кисть идёт прямым путём,
    а не проваливается в стол, когда ладонь переворачивается."""
    def lerp(x, y):
        if isinstance(x, (int, float)):
            return x + (y - x) * t
        if isinstance(x, tuple):
            return tuple(lerp(p, q) for p, q in zip(x, y))
        if isinstance(x, dict):
            return {k: lerp(x[k], y[k]) for k in x}
        return x.lerp(y, t)
    result = {key: lerp(a[key], b[key]) for key in a}
    for key in ("along", "back", "elbow_from_wrist"):
        result[key] = result[key].normalized()
    return result


def frames(keys):
    """Каждый кадр клипа с плавным разгоном и торможением между ключами."""
    for (f0, l0, r0), (f1, l1, r1) in zip(keys, keys[1:]):
        for frame in range(f0, f1):
            t = (frame - f0) / (f1 - f0)
            t = t * t * (3 - 2 * t)
            yield frame, mix(l0, l1, t), mix(r0, r1, t)
    yield keys[-1]


def bake(armature, name, keys):
    """Ключ на каждый кадр, углами Эйлера, как у поз: при выгрузке FBX все действия читаются в одном режиме поворота.
    Каждый угол берётся ближайшим к предыдущему кадру, чтобы рука не прокручивалась на 360°."""
    action = bpy.data.actions.new(PREFIX + name)
    action.use_fake_user = True
    armature.animation_data.action = action
    previous = {}
    for frame, left, right in frames(keys):
        for bone in armature.pose.bones:
            bone.rotation_mode = "XYZ"
        # left — рука с веером, right — свободная (жесты, взятие карты); на какую сторону они встанут, решает in_game.FAN_HAND.
        in_game.pose_both(armature, left, right)
        for bone in armature.pose.bones:
            if bone.name == "Root" or "Socket" in bone.name:
                continue
            euler = bone.matrix_basis.to_euler("XYZ", previous[bone.name]) if bone.name in previous else bone.matrix_basis.to_euler("XYZ")
            previous[bone.name] = euler
            bone.rotation_euler = euler
            bone.keyframe_insert("rotation_euler", frame=frame + 1)
            bone.keyframe_insert("location", frame=frame + 1)
    armature.animation_data.action = None


def store(armature):
    bpy.context.scene.render.fps = FPS
    armature.animation_data_create()
    for name, keys in CLIPS.items():
        old = bpy.data.actions.get(PREFIX + name)
        if old is not None:
            bpy.data.actions.remove(old)
        bake(armature, name, keys)
    for bone in armature.pose.bones:
        bone.rotation_euler = (0, 0, 0)
        bone.location = (0, 0, 0)
    bpy.context.view_layer.update()
