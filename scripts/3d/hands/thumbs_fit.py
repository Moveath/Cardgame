"""Подбор большого пальца для жестов «класс» и «фу» (этап A, 2026-11-20) по коже: четыре пальца — кулак gestures.FIST
как есть, большой палец поднят. Файл .blend не сохраняет; печатает найденную позу и пишет её в JSON.

Что ищется (референс автора references/hands/жесты/1-класс-фу-фак-указание.png и живые руки):
- палец направлен от кисти к большому пальцу почти поперёк кисти (угол от оси кисти к большому пальцу в плоскости
  ладони — TF_ANGLE), без завала к ладони или к тылу (TF_TILT — допустимый наклон, доля);
- палец прямой: межфаланговый сустав чуть разогнут (TF_IP, градусы, минус — разгиб), пястно-фаланговый — в пределах
  живой руки, основание отведено не дальше предела модели (hand_anatomy.THUMB_SWING);
- основание пальца лежит на кулаке: основная фаланга касается указательного (зазор не больше TF_REST мм), а не висит
  отдельно от кисти, и кожа в кожу не входит (ни палец в кулак, ни пальцы в него);
- ноготь — к тылу кисти, как у живого пальца в этом жесте.

Запуск: blender --background --factory-startup --python scripts/3d/hands/thumbs_fit.py -- [fit|show] [JSON позы]
  fit  — подбор от стартовой позы (TF_START — JSON {"thumb":…, "thumb_turn":…, "thumb_bend":…}, иначе THUMBS из gestures_extra);
  show — только замер позы. TF_OUT — куда записать найденную позу. Грузятся только скелет и правая рука (fist_smooth.load_light)."""
import copy
import json
import math
import os
import sys
import time

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(__file__))
import fist_smooth  # noqa: E402

if "Rig_Hands" not in bpy.data.objects:
    fist_smooth.load_light()
import gesture_lab  # noqa: E402
import gestures  # noqa: E402
import hand_anatomy  # noqa: E402
from gesture_lab import FOUR, Skin  # noqa: E402

SIDE = "_R"
ANGLE = float(os.environ.get("TF_ANGLE", "62"))
TILT = float(os.environ.get("TF_TILT", ".2"))
IP = float(os.environ.get("TF_IP", "-8"))
MCP = float(os.environ.get("TF_MCP", "12"))
REST = float(os.environ.get("TF_REST", "8"))
ABOVE = float(os.environ.get("TF_ABOVE", ".66"))
# Вес «длины над кулаком» (TF_ABOVE_W): 0 — не мерить.
ABOVE_W = float(os.environ.get("TF_ABOVE_W", "40"))
DIRECTION = Vector(tuple(float(v) for v in os.environ["TF_DIR"].split(","))).normalized() if os.environ.get("TF_DIR") else None
SOFT = .3


def hand_point(armature, point):
    hand = armature.pose.bones["Hand" + SIDE]
    axes = hand_anatomy.hand_axes(hand.matrix, SIDE)
    d = (point - hand.head) * 1000
    return Vector([d.dot(a) for a in axes]), axes


def measure(armature, skin, pose):
    gesture_lab.reset(armature)
    hand_anatomy.apply(armature, pose, SIDE)
    bpy.context.view_layer.update()
    skin.shot()
    bones = armature.pose.bones
    mcp, axes = hand_point(armature, bones["Thumb02" + SIDE].head)
    tip, _ = hand_point(armature, bones["TipThumb" + SIDE].tail)
    direction = (tip - mcp).normalized()
    nail = bones["Thumb03" + SIDE].matrix.to_3x3().col[2].normalized()
    nail = Vector([nail.dot(a) for a in axes])
    thumb_in = max(skin.inside(("Thumb", p), b)[0] for p in (2, 3) for b in FOUR + ("Palm",))
    fingers_in = max(skin.inside((b, p), "Thumb")[0] for p in (1, 2, 3) for b in FOUR)
    rest = skin.gap({("Thumb", 2)}, ("Index",))
    origin = armature.pose.bones["Hand" + SIDE].head.dot(axes[0]) * 1000
    across = [p.dot(axes[0]) * 1000 - origin for p, o in zip(skin.co, skin.owner) if o is not None and o[0] in FOUR]
    top = max(across)
    report = hand_anatomy.thumb_report(armature, SIDE)
    return dict(mcp=mcp, tip=tip, direction=direction, angle=math.degrees(math.atan2(direction.x, direction.y)),
                nail=nail, thumb_in=thumb_in, fingers_in=fingers_in, rest=20. if rest is None else rest,
                above=tip.x - top, height=top - min(across), thumb=report)


def score(m):
    angle = abs(m["angle"] - ANGLE)
    tilt = max(0., abs(m["direction"].z) - TILT)
    # TF_DIR="x,y,z" — направление пальца в осях кисти целиком (2026-11-22: палец строго вверх и вниз при руке как
    # в кулаке F, gesture_test.py need): угол до него вместо угла в плоскости ладони и наклона.
    if DIRECTION is not None:
        angle = math.degrees(m["direction"].angle(DIRECTION))
        tilt = 0.
    # Суставы — настоящие углы между костями (thumb_report): межфаланговый чуть разогнут, пястно-фаланговый около прямого,
    # вбок почти не отклонён.
    ip = abs(m["thumb"]["Thumb03"][0] - IP)
    mcp = max(0., abs(m["thumb"]["Thumb02"][0]) - MCP)
    side = max(0., abs(m["thumb"]["Thumb02"][1]) - 6)
    swing = max(0., m["thumb"]["swing"] - hand_anatomy.THUMB_SWING)
    enter = max(0., m["thumb_in"] - SOFT) + max(0., m["fingers_in"] - SOFT)
    rest = max(0., m["rest"] - REST)
    nail = max(0., .5 - m["nail"].z)
    # Длина над кулаком: доля высоты кулака, как на фото (палец над кулаком примерно на две трети его высоты).
    above = max(0., abs(m["above"] / m["height"] - ABOVE) - .05)
    parts = dict(angle=angle, tilt=tilt, ip=ip, mcp=mcp, side=side, swing=swing, enter=enter, rest=rest, nail=nail, above=above)
    return (.5 * angle + 40 * tilt + .3 * ip + .3 * mcp + .3 * side + 3 * swing + 6 * enter + 1.5 * rest + 10 * nail
            + ABOVE_W * above), parts


def show(name, m):
    print("=== THUMBS %s угол %.0f° (цель %.0f), наклон %.2f, ноготь %s; над кулаком кончик на %.0f мм (%.2f высоты кулака); основная фаланга до "
          "указательного %.1f мм; палец в кулак %.1f мм, пальцы в него %.1f мм; отведён %.0f°, разворот %.0f°, пястно-фаланговый "
          "%.0f° (вбок %.0f°), межфаланговый %.0f°; сустав %s, кончик %s"
          % (name, m["angle"], ANGLE, m["direction"].z, tuple(round(v, 2) for v in m["nail"]), m["above"], m["above"] / m["height"], m["rest"],
             m["thumb_in"], m["fingers_in"], m["thumb"]["swing"], m["thumb"]["turn"], *m["thumb"]["Thumb02"],
             m["thumb"]["Thumb03"][0], tuple(round(v, 1) for v in m["mcp"]), tuple(round(v, 1) for v in m["tip"])))


KEYS = [("thumb", 0), ("thumb", 1), ("thumb", 2), ("thumb_turn", None), ("thumb_bend", 0), ("thumb_bend", 1), ("thumb_bend", 2)]


def get(pose, key):
    return pose[key[0]] if key[1] is None else pose[key[0]][key[1]]


def put(pose, key, value):
    result = copy.deepcopy(pose)
    if key[1] is None:
        result[key[0]] = value
    else:
        values = list(result[key[0]])
        values[key[1]] = value
        result[key[0]] = tuple(values)
    return result


def start_pose():
    path = os.environ.get("TF_START")
    if path:
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
        data = data if "thumb" in data else next(iter(data.values()))
    else:
        import gestures_extra
        data = gestures_extra.THUMBS
    return dict(gestures.FIST, thumb=tuple(data["thumb"]), thumb_turn=data["thumb_turn"], thumb_bend=tuple(data["thumb_bend"]))


def main():
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    command = args[0] if args else "fit"
    armature = bpy.data.objects["Rig_Hands"]
    armature.animation_data_create()
    armature.animation_data.action = None
    armature.matrix_world.identity()
    skin = Skin(armature, SIDE)
    best = start_pose()
    began = time.time()
    m = measure(armature, skin, best)
    best_score, parts = score(m)
    show("start", m)
    print("=== FIT start %.2f %s (%.1f с на замер)" % (best_score, {k: round(v, 2) for k, v in parts.items()}, time.time() - began))
    if command == "fit":
        for round_number in range(int(os.environ.get("TF_ROUNDS", "6"))):
            improved = False
            for key in KEYS:
                for step in (4, -4, 2, -2, 1, -1, .5, -.5):
                    trial = put(best, key, get(best, key) + step)
                    limited = hand_anatomy.limited(trial)
                    if limited["thumb_turn"] != trial["thumb_turn"] or limited["thumb_bend"] != trial["thumb_bend"]:
                        continue
                    trial_m = measure(armature, skin, trial)
                    value, p = score(trial_m)
                    if value < best_score - 1e-4:
                        best, best_score, parts, m, improved = trial, value, p, trial_m, True
                        break
            print("=== FIT round %d: %.2f %s (%.0f с)" % (round_number + 1, best_score, {k: round(v, 2) for k, v in parts.items()}, time.time() - began))
            if not improved:
                break
        show("fit", m)
    found = {"thumb": [round(v, 1) for v in best["thumb"]], "thumb_turn": round(best["thumb_turn"], 1),
             "thumb_bend": [round(v, 1) for v in best["thumb_bend"]]}
    print("=== FIT поза", json.dumps(found))
    out = os.environ.get("TF_OUT")
    if out:
        with open(out, "w", encoding="utf-8") as f:
            json.dump({"Thumbs": found}, f, ensure_ascii=False, indent=1)
        print("=== FIT записано", out)


if __name__ == "__main__":
    main()
