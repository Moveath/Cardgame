"""Большой палец в кулаке по фото автора (references/hands/кулак-фото, 3 и 4; 2026-11-17): палец лежит сверху на
кулаке, поверх указательного, кончиком вперёд к его среднему суставу; не обёрнут вокруг низа кулака и не торчит вперёд.

Прежний кулак (fist_fit.py) клал большой палец поперёк средних фаланг указательного и среднего снизу, как в учебнике
и на фото 1: основание уведено к ладони на предел (45°), межфаланговый сустав на 13 мм ниже средних фаланг, кончик у
среднего пальца. Автор показал свой кулак: палец выше, на стороне большого пальца у указательного.

Подбираются только числа большого пальца (точка основания, разворот, сгибы), четыре пальца не меняются. Цель задаётся
точками в осях кисти (мм от запястья: к большому пальцу, вдоль кисти, к тылу), снятыми с фото относительно костей
указательного: межфаланговый сустав сбоку от указательного над его средней фалангой, кончик у переднего нижнего края
среднего сустава указательного. Кожа: подушечка касается указательного (щель до 0,5 мм), кожа в кожу не входит
(до 0,3 мм), межфаланговый сустав согнут на 15–35°, отведение основания не дальше предела.

TF_MODE=side — большой палец как на фото 5 (references/hands/кулак-фото/5, 2026-11-20; автор: «сбоку от указательного,
а не поверх него»). Кулак со стороны ладони, большой палец вверху: указательный сверху открыт, большой палец ниже него,
основная фаланга вдоль согнутых пальцев, у переднего края межфаланговый сустав, кончик загнут вниз. Цели — в ракурсе
фото (TF_VIEW — откуда смотрит камера в осях кисти): над большим пальцем видна полоса указательного не уже TF_BAND мм,
основная фаланга в кадре почти горизонтальна, дальняя смотрит вниз; межфаланговый сустав у переднего края кулака.
TF_SWING — предел отведения основания (по умолчанию живой, 45°).

Запуск: blender [<.blend>] --background --disable-autoexec --python scripts/3d/hands/thumb_fit.py
Без .blend грузятся только скелет и правая рука (можно при открытом Unity). FIT_OUT — куда записать позу (JSON).
TF_IP, TF_TIP — свои цели "x,y,z"."""
import copy
import json
import math
import os
import sys
import time

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(__file__))
import fist_fit  # noqa: E402
import fist_smooth  # noqa: E402
import gesture_lab  # noqa: E402
import gestures  # noqa: E402
import hand_anatomy  # noqa: E402
from gesture_lab import Skin  # noqa: E402

SIDE = "_R"
IP = Vector(tuple(map(float, os.environ.get("TF_IP", "36,62,-50").split(","))))
TIP = Vector(tuple(map(float, os.environ.get("TF_TIP", "20,80,-62").split(","))))
LIE, TARGET = float(os.environ.get("TF_LIE", "1")), float(os.environ.get("TF_TARGET", "1"))
# Дыра со стороны большого пальца (fist_smooth.cavity): вес в подборе, 0 — не мерить.
CAVITY = float(os.environ.get("TF_CAVITY", "0"))
MODE = os.environ.get("TF_MODE", "top")
PHOTO_VIEW = Vector(tuple(map(float, os.environ.get("TF_VIEW", ".4,.15,-1").split(",")))).normalized()
BAND = float(os.environ.get("TF_BAND", "10"))
BAND_FROM = float(os.environ.get("TF_BAND_FROM", "0"))
# Межфаланговый сустав у переднего края, но не впереди кулака: вдоль кисти не ближе FRONT[0] и не дальше FRONT[1] мм,
# кончик не дальше FRONT[2] (передняя кожа средних суставов пальцев — 93–95 мм).
FRONT = tuple(map(float, os.environ.get("TF_FRONT", "80,200,98").split(",")))
# Кожа большого пальца впереди кожи пальцев не больше чем на AHEAD мм (на фото межфаланговый сустав выступает на 2–3 мм).
AHEAD = float(os.environ.get("TF_AHEAD", "100"))
# Вес «основная фаланга вдоль пальцев» (в кадре фото почти горизонтальна).
FLAT_W = float(os.environ.get("TF_FLAT_W", "1"))
# Кожа ладони и основания большого пальца в пальцах — не глубже PALM мм.
PALM = float(os.environ.get("TF_PALM", "1"))
SWING = float(os.environ.get("TF_SWING", str(hand_anatomy.THUMB_SWING)))
# Отведение основания дальше живого предела (45°) — штраф за каждый градус сверху; TF_BASE=1 — подбирать и сдвиг
# основания (thumb_base, мм), штраф за каждый мм сдвига TF_BASE_COST.
OVER = float(os.environ.get("TF_OVER", "0.2"))
BASE = os.environ.get("TF_BASE") == "1"
BASE_COST = float(os.environ.get("TF_BASE_COST", "0.3"))
BASE_MAX = float(os.environ.get("TF_BASE_MAX", "12"))
KEYS = [("thumb", 0), ("thumb", 1), ("thumb", 2), ("thumb_turn",), ("thumb_bend", 0), ("thumb_bend", 1), ("thumb_bend", 2)]
if BASE:
    KEYS += [("thumb_base", 0), ("thumb_base", 1), ("thumb_base", 2)]


def get(pose, key):
    return pose[key[0]][key[1]] if len(key) == 2 else pose[key[0]]


def put(pose, key, value):
    out = copy.deepcopy(pose)
    if len(key) == 2:
        values = list(out[key[0]])
        values[key[1]] = value
        out[key[0]] = tuple(values)
    else:
        out[key[0]] = value
    return out


def points(armature):
    bones = armature.pose.bones
    hand = bones["Hand" + SIDE]
    axes = hand_anatomy.hand_axes(hand.matrix, SIDE)

    def local(p):
        d = (p - hand.head) * 1000
        return Vector(tuple(d.dot(a) for a in axes))
    return local(bones["Thumb03" + SIDE].head), local(bones["Thumb03" + SIDE].tail), local(bones["Thumb02" + SIDE].head)


def photo_numbers(armature, skin, mcp, ip, tip):
    """Большой палец в ракурсе фото 5 (кадр: верх — к большому пальцу): насколько его верх ниже верха средней фаланги
    указательного по всей её длине (мм, наименьшее по полосам кадра шириной 3 мм), наклон основной фаланги к горизонтали
    кадра и дальней — к вертикали вниз (градусы)."""
    hand = armature.pose.bones["Hand" + SIDE]
    axes = hand_anatomy.hand_axes(hand.matrix, SIDE)
    v = PHOTO_VIEW
    up = (Vector((1., 0., 0.)) - v * v.x).normalized()
    across = v.cross(up)
    tops = {"thumb": {}, "index": {}}
    front = {"thumb": -1e9, "fingers": -1e9}
    for p, owner in zip(skin.co, skin.owner):
        if owner is None:
            continue
        d = (p - hand.head) * 1000
        q = Vector(tuple(d.dot(a) for a in axes))
        if owner[0] == "Thumb" and owner[1] in (2, 3):
            front["thumb"] = max(front["thumb"], q.y)
        elif owner[0] in ("Index", "Middle", "Ring", "Pinky"):
            front["fingers"] = max(front["fingers"], q.y)
        key = "thumb" if owner in (("Thumb", 2), ("Thumb", 3)) else "index" if owner == ("Index", 2) else None
        if key is None:
            continue
        # Полоса меряется у средней фаланги указательного от BAND_FROM мм вдоль кисти (передняя часть, к среднему суставу):
        # у её заднего конца на фото поднимается основание большого пальца.
        if key == "index" and q.y < BAND_FROM:
            continue
        column = int(math.floor(q.dot(across) / 3))
        tops[key][column] = max(tops[key].get(column, -1e9), q.dot(up))
    shared = [c for c in tops["index"] if c in tops["thumb"]]
    band = min((tops["index"][c] - tops["thumb"][c] for c in shared), default=30.)
    prox, dist = ip - mcp, tip - ip
    flat = math.degrees(math.atan2(prox.dot(up), abs(prox.dot(across)) + 1e-9))
    down = math.degrees(math.atan2(abs(dist.dot(across)), -dist.dot(up)))
    return band, flat, down, front["thumb"] - front["fingers"]


def evaluate(armature, skin, pose):
    m = fist_fit.measure(armature, skin, pose)
    ip, tip, mcp = points(armature)
    report = m["thumb"]
    # Основная фаланга большого пальца лежит на указательном, а не висит над ним: иначе под ней со стороны большого
    # пальца видно внутрь кольца указательного (дыра в кулаке, которой у автора нет).
    lie = skin.gap({("Thumb", 2)}, ("Index",))
    m["lie"] = 20. if lie is None else lie
    parts = dict(
        enter=max(0., m["thumb_in"] - .3) + max(0., m["fingers_in"] - .3),
        contact=max(0., m["across"]["Index"] - .5),
        lie=max(0., m["lie"] - 1.),
        target=((ip - IP).length + (tip - TIP).length) / 10,
        ip=max(0., report["Thumb03"][0] - 35) + max(0., 15 - report["Thumb03"][0]),
        swing=max(0., report["swing"] - hand_anatomy.THUMB_SWING),
        # Сверху на кулаке: межфаланговый сустав со стороны большого пальца у указательного, кончик не дальше указательного
        # к среднему пальцу и не впереди кулака.
        place=max(0., 30 - ip.x) / 2 + max(0., 15 - tip.x) / 2 + max(0., tip.y - 90) / 2,
        cavity=0.,
    )
    if MODE == "side":
        # Подушечка лежит на указательном или среднем, основная фаланга — на них же (без щели, сквозь которую видно
        # внутрь кулака). Межфаланговый сгиб — сколько нужно, чтобы кончик смотрел вниз (в пределах сустава).
        tip_gap = skin.gap({("Thumb", 3)}, ("Index", "Middle"))
        lie = skin.gap({("Thumb", 2)}, ("Index", "Middle"))
        m["lie"] = 20. if lie is None else lie
        band, flat, down, ahead = photo_numbers(armature, skin, mcp, ip, tip)
        m["photo"] = (band, flat, down, ahead)
        # Основание большого пальца уходит к ладони: его подушка (кожа ладони) не должна входить в кончики пальцев.
        m["palm"] = max(fist_smooth.palm_into(skin, body) for body in ("Index", "Middle"))
        m["tip_palm"] = max(d for d, _ in m["tips"].values()) if "tips" in m else 0.
        parts["enter"] += max(0., m["palm"] - PALM) + max(0., m["tip_palm"] - .5)
        parts.update(
            contact=max(0., (20. if tip_gap is None else tip_gap) - .5),
            lie=max(0., m["lie"] - 1.),
            ip=0.,
            swing=max(0., report["swing"] - SWING) + OVER / 5 * max(0., report["swing"] - hand_anatomy.THUMB_SWING)
            + BASE_COST / 5 * Vector(pose.get("thumb_base", (0, 0, 0))).length
            + max(0., Vector(pose.get("thumb_base", (0, 0, 0))).length - BASE_MAX),
            place=max(0., BAND - band) + FLAT_W * max(0., abs(flat) - 15) / 5 + max(0., down - 30) / 5
            + max(0., FRONT[0] - ip.y) / 2 + max(0., ip.y - FRONT[1]) + max(0., tip.y - FRONT[2])
            + max(0., ahead - AHEAD),
        )
    if CAVITY:
        areas = fist_smooth.cavity(armature, bpy.data.objects["Arm_Male" + SIDE], skin, step=.0025)
        m["cavity"] = areas
        parts["cavity"] = sum(areas.values()) / 300
    total = (4 * parts["enter"] + 2 * parts["contact"] + LIE * parts["lie"] + TARGET * parts["target"] + .3 * parts["ip"]
             + 5 * parts["swing"] + 2 * parts["place"] + CAVITY * parts["cavity"])
    return total, parts, m, (ip, tip, mcp)


def show(tag, result):
    total, parts, m, (ip, tip, mcp) = result
    r = m["thumb"]
    print("=== THUMB FIT %s: %.2f %s%s" % (tag, total, {k: round(v, 2) for k, v in parts.items()},
                                        " дыра " + str({k: round(v) for k, v in m["cavity"].items()}) if "cavity" in m else ""))
    print("=== THUMB FIT %s основание %s, межфаланговый %s, кончик %s мм; до указательного %.1f (основной фалангой %.1f), "
          "до среднего %.1f мм; входит %.1f, пальцы в нём %.1f мм; отведён %.0f°, разворот %.0f°, сгибы %.0f° / %.0f°"
          % (tag, tuple(round(x) for x in mcp), tuple(round(x) for x in ip), tuple(round(x) for x in tip), m["across"]["Index"],
             m["lie"], m["across"]["Middle"], m["thumb_in"], m["fingers_in"], r["swing"], r["turn"], r["Thumb02"][0], r["Thumb03"][0]))
    if "photo" in m:
        print("=== THUMB FIT %s ракурс фото: указательный виден над большим пальцем на %.1f мм, основная фаланга к горизонтали "
              "%.0f°, кончик от вертикали %.0f°; впереди пальцев на %.1f мм; ладонь в пальцах %.1f мм, кончики в ладони %.1f мм"
              % ((tag,) + m["photo"] + (m["palm"], m["tip_palm"])))


def main():
    if "Rig_Hands" not in bpy.data.objects:
        fist_smooth.load_light()
    armature = bpy.data.objects["Rig_Hands"]
    armature.animation_data_create()
    armature.animation_data.action = None
    armature.matrix_world.identity()
    if fist_smooth.PROP in armature:
        armature[fist_smooth.PROP] = 0.
    start = copy.deepcopy(gestures.FIST)
    seed = os.environ.get("TF_SEED")
    if seed:
        values = json.loads(seed)
        start.update({k: tuple(v) if isinstance(v, list) else v for k, v in values.items()})
    if BASE:
        start.setdefault("thumb_base", (0., 0., 0.))
    # TF_SHAPE=1 — подбирать по коже с формой кулака (fist_smooth.py, снята в начальной позе): сглаживание скругляет
    # угол среднего сустава указательного, на который ложится подушечка большого пальца.
    if os.environ.get("TF_SHAPE") == "1":
        gestures.FIST.update({k: start[k] for k in ("thumb", "thumb_turn", "thumb_bend", "thumb_base") if k in start})
        fist_smooth.bake(armature, bpy.data.objects["Arm_Male" + SIDE], SIDE).value = 1.
    # TF_KEEP_SHAPE=1 — подбирать по коже с той формой FistSmooth, что уже в .blend (не пересчитывая её): она скругляет
    # углы средних суставов пальцев, на которые ложится большой палец.
    if os.environ.get("TF_KEEP_SHAPE") == "1":
        armature[fist_smooth.PROP] = 1.
        bpy.data.objects["Arm_Male" + SIDE].data.shape_keys.key_blocks[fist_smooth.KEY].value = 1.
    skin = Skin(armature, SIDE)
    best = start
    best_result = evaluate(armature, skin, best)
    show("start", best_result)
    began = time.time()
    # TF_RANDOM=N — сначала N случайных поз большого пальца в пределах живого сустава: локальный подбор застревает,
    # когда к цели надо обойти указательный палец.
    import random
    rng = random.Random(int(os.environ.get("TF_RANDOM_SEED", "1")))
    ranges = {("thumb", 0): (35, 62), ("thumb", 1): (38, 64), ("thumb", 2): (-48, -4), ("thumb_turn",): (-15, 50),
              ("thumb_bend", 0): (-10, 45), ("thumb_bend", 1): (-12, 12), ("thumb_bend", 2): (0, 70)}
    if MODE == "side":
        # Основание уводится к ладони и к среднему пальцу дальше, чем для пальца сверху; межфаланговый сгиб — до предела.
        ranges.update({("thumb", 0): (10, 62), ("thumb", 1): (30, 70), ("thumb", 2): (-70, -4), ("thumb_bend", 2): (20, 85)})
    if BASE:
        ranges.update({("thumb_base", 0): (-14, 4), ("thumb_base", 1): (-4, 14), ("thumb_base", 2): (-18, 2)})
    for n in range(int(os.environ.get("TF_RANDOM", "0"))):
        trial = best
        for key, (low, high) in ranges.items():
            trial = put(trial, key, round(rng.uniform(low, high), 1))
        if hand_anatomy.limited(trial)["thumb_bend"] != trial["thumb_bend"]:
            continue
        result = evaluate(armature, skin, trial)
        if result[0] < best_result[0]:
            best, best_result = trial, result
            show("random %d" % n, best_result)
    print("=== THUMB FIT перебор: %.0f с" % (time.time() - began))
    for round_number in range(int(os.environ.get("FIT_ROUNDS", "8"))):
        improved = False
        for key in KEYS:
            for step in (6, -6, 3, -3, 1.5, -1.5, .5, -.5):
                trial = put(best, key, get(best, key) + step)
                limited = hand_anatomy.limited(trial)
                if any(limited[k] != trial[k] for k in ("thumb_turn", "thumb_bend")):
                    continue
                result = evaluate(armature, skin, trial)
                if result[0] < best_result[0] - 1e-4:
                    best, best_result, improved = trial, result, True
                    break
        show("round %d (%.0f с)" % (round_number + 1, time.time() - began), best_result)
        if not improved:
            break
    show("fit", best_result)
    print("=== THUMB FIT thumb %s turn %s bend %s base %s" % (tuple(round(x, 1) for x in best["thumb"]), round(best["thumb_turn"], 1),
                                                             tuple(round(x, 1) for x in best["thumb_bend"]),
                                                             tuple(round(x, 1) for x in best.get("thumb_base", (0, 0, 0)))))
    fist_fit.show("fit", best_result[2])
    out = os.environ.get("FIT_OUT")
    if out:
        with open(out, "w", encoding="utf-8") as f:
            json.dump({"FistThumbSide" if MODE == "side" else "FistThumbTop": best}, f, ensure_ascii=False, indent=1)
        print("=== THUMB FIT записано", out)


if __name__ == "__main__":
    main()
