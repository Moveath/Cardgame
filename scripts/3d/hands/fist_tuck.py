"""Большой палец кулака ближе к сгибу указательного (ТЗ автора 2026-11-22: «прижми большие пальцы чуть ближе к сгибу
указательных, чтобы они не торчали в стороны»). Файл .blend не сохраняет; печатает найденную позу и пишет её в JSON.

С места игрока в проверке F дальняя фаланга большого пальца выходила за контур кулака на 15 мм, кончик стоял в 24 мм
от среднего сустава указательного (gesture_test.py protrude). Подбор двигает только большой палец кулака (числа
gestures.FIST: thumb, thumb_turn, thumb_bend) и меряет кожу в кадре игры (game_test.pose_frame, кадр кулака):
- выступ большого пальца за контур остального кулака на экране (мм на глубине кисти) — меньше;
- кончик до среднего сустава указательного — ближе;
- большой палец касается пальцев (зазор не больше 1 мм), кожа в кожу не входит (не глубже 0,6 мм);
- палец остаётся сбоку от указательного, как на фото 5 автора: не выше верхней грани указательного, кончик вниз.
Форма FistSmooth на коже (вес 1), FistThumb — 0: она посчитана для прежней позы и пересчитывается после подбора
(fist_thumb.py store).

Запуск: blender art3d/hands/CardCity_Hands.blend --background --disable-autoexec --python scripts/3d/hands/fist_tuck.py -- [fit|show]
Переменные: FT_OUT — куда записать позу (JSON для FS_POSE); FT_ROUNDS — проходов; FT_POSE — JSON позы для show."""
import copy
import json
import math
import os
import sys
import time

import bpy

sys.path.insert(0, os.path.dirname(__file__))
import game_test  # noqa: E402
import gesture_lab  # noqa: E402
import gesture_test  # noqa: E402
import gestures  # noqa: E402
import hand_anatomy  # noqa: E402
from gesture_lab import FOUR  # noqa: E402

SIDE = "_R"
KEYS = [("thumb", 0), ("thumb", 1), ("thumb", 2), ("thumb_turn", None), ("thumb_bend", 0), ("thumb_bend", 1), ("thumb_bend", 2)]
SOFT, CONTACT = float(os.environ.get("FT_SOFT", ".6")), 1.


def shapes(armature, smooth):
    """FistSmooth на вес smooth, FistThumb — 0 (свойство скелета и сами ключи: драйвер без update_tag не пересчитывается)."""
    armature["fist_smooth"] = smooth
    armature.update_tag()
    for name in ("Arm_Male_L", "Arm_Male_R"):
        keys = bpy.data.objects[name].data.shape_keys
        for block in keys.key_blocks[1:]:
            block.value = smooth if block.name == "FistSmooth" else 0.
    bpy.context.view_layer.update()


def apply(armature, pose):
    gestures.FIST.update({k: pose[k] for k in ("thumb", "thumb_turn", "thumb_bend")})
    game_test.pose_frame(armature, gestures.CLOSED)


def measure(armature, skin, pose):
    apply(armature, pose)
    skin.shot()
    bones = armature.pose.bones
    excess, part = gesture_test.outline_excess(skin, lambda o: o[0] == "Thumb" and o[1] in (2, 3))
    tip = bones["TipThumb" + SIDE].tail
    fold = (tip - bones["Index02" + SIDE].head).length * 1000
    thumb_in = max(skin.inside(("Thumb", p), b)[0] for p in (2, 3) for b in FOUR + ("Palm",))
    fingers_in = max(skin.inside((b, p), "Thumb")[0] for p in (1, 2, 3) for b in FOUR)
    gap = skin.gap({("Thumb", 2), ("Thumb", 3)}, ("Index", "Middle"))
    # Сбоку от указательного, как на фото 5: самая высокая точка большого пальца (к большому пальцу в осях кисти) не выше
    # верхней грани указательного, и кончик смотрит вниз (к ладонной стороне), а не вперёд.
    hand = bones["Hand" + SIDE]
    toward, along, back = hand_anatomy.hand_axes(hand.matrix, SIDE)
    thumb_top = max(p.dot(toward) for p, o in zip(skin.co, skin.owner) if o and o[0] == "Thumb" and o[1] in (2, 3))
    index_top = max(p.dot(toward) for p, o in zip(skin.co, skin.owner) if o and o[0] == "Index")
    d = (tip - bones["Thumb03" + SIDE].head).normalized()
    report = hand_anatomy.thumb_report(armature, SIDE)
    return dict(excess=excess, part=part, fold=fold, thumb_in=thumb_in, fingers_in=fingers_in, gap=20. if gap is None else gap,
                above=(thumb_top - index_top) * 1000, down=-d.dot(back), report=report)


def score(m, start):
    parts = dict(excess=m["excess"], fold=max(0., m["fold"] - 12.),
                 enter=max(0., m["thumb_in"] - SOFT) + max(0., m["fingers_in"] - SOFT),
                 gap=max(0., m["gap"] - CONTACT), above=max(0., m["above"] - max(0., start["above"])),
                 down=max(0., start["down"] - .1 - m["down"]),
                 ip=max(0., m["report"]["Thumb03"][0] - 85.))
    return (1. * parts["excess"] + .3 * parts["fold"] + 8 * parts["enter"] + 3 * parts["gap"] + 2 * parts["above"]
            + 20 * parts["down"] + 2 * parts["ip"]), parts


def show(name, m):
    r = m["report"]
    print("=== TUCK %s: за контуром на экране %.1f мм (%s), кончик до сгиба указательного %.0f мм, зазор до пальцев %.1f мм, "
          "палец в пальцах %.1f мм, пальцы в нём %.1f мм, над указательным %.1f мм, кончик вниз %.2f; отведён %.0f°, разворот %.0f°, "
          "пястно-фаланговый %.0f° (вбок %.0f°), межфаланговый %.0f°"
          % (name, m["excess"], m["part"], m["fold"], m["gap"], m["thumb_in"], m["fingers_in"], m["above"], m["down"],
             r["swing"], r["turn"], *r["Thumb02"], r["Thumb03"][0]))


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


def main():
    command = (sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else ["fit"])[0]
    armature = bpy.data.objects["Rig_Hands"]
    armature.animation_data_create()
    armature.animation_data.action = None
    armature.matrix_world.identity()
    shapes(armature, 1.)
    skin = gesture_lab.Skin(armature, SIDE)
    best = {k: gestures.FIST[k] for k in ("thumb", "thumb_turn", "thumb_bend")}
    if os.environ.get("FT_POSE"):
        with open(os.environ["FT_POSE"], encoding="utf-8") as f:
            data = json.load(f)
        data = data if "thumb" in data else next(iter(data.values()))
        best = {k: tuple(v) if isinstance(v, list) else v for k, v in data.items() if k in best}
    began = time.time()
    start = measure(armature, skin, best)
    best_score, parts = score(start, start)
    show("start", start)
    m = start
    if command == "fit":
        for round_number in range(int(os.environ.get("FT_ROUNDS", "8"))):
            improved = False
            for key in KEYS:
                for step in (4, -4, 2, -2, 1, -1):
                    trial = put(best, key, get(best, key) + step)
                    limited = hand_anatomy.limited(dict(gestures.FIST, **trial))
                    if limited["thumb_turn"] != trial["thumb_turn"] or limited["thumb_bend"] != trial["thumb_bend"]:
                        continue
                    trial_m = measure(armature, skin, trial)
                    value, p = score(trial_m, start)
                    if value < best_score - 1e-3:
                        best, best_score, parts, m, improved = trial, value, p, trial_m, True
                        break
            print("=== TUCK round %d: %.2f %s (%.0f с)" % (round_number + 1, best_score, {k: round(v, 2) for k, v in parts.items()}, time.time() - began))
            if not improved:
                break
        show("fit", m)
    found = {"thumb": [round(v, 1) for v in best["thumb"]], "thumb_turn": round(best["thumb_turn"], 1),
             "thumb_bend": [round(v, 1) for v in best["thumb_bend"]]}
    print("=== TUCK поза", json.dumps(found))
    out = os.environ.get("FT_OUT")
    if out:
        with open(out, "w", encoding="utf-8") as f:
            json.dump({"Fist": found}, f, ensure_ascii=False, indent=1)
        print("=== TUCK записано", out)
    game_test.reset(armature)


if __name__ == "__main__":
    main()
