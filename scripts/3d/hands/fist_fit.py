"""Доводка кулака по фото, присланным автором (references/hands/кулак-фото, 2026-11-16): пальцы плотно прижаты друг к другу по всей
длине средних фаланг и нижней части основных, подушечки упираются в ладонь, большой палец почти прямой и лежит поперёк
средних фаланг указательного и среднего пальцев. Кожа в кожу не входит. Файл .blend не сохраняет; печатает найденную позу.

Чем отличается от gesture_fit.py: зазор между соседними пальцами меряется не в одной ближайшей точке, а по всей длине
фаланги (доли длины — в каждой своя наименьшая щель): так видна щель клином, когда пальцы касаются у основания и расходятся
к кончикам. И у большого пальца своя цель — лечь поперёк двух пальцев, а не касаться одного.

Запуск: blender art3d/hands/CardCity_Hands.blend --background --disable-autoexec --python scripts/3d/hands/fist_fit.py -- <поза> [что двигать]
Что двигать: fingers (курс и наклон пальцев), curl (сгибы трёх суставов), thumb (большой палец), cup (пястная мизинца);
по умолчанию всё. Переменные: LAB_JSON — позы поверх gestures.POSES (как у gesture_lab.py); FIT_ROUNDS — число проходов;
FIT_OUT — файл, куда записать найденную позу (JSON {имя: поза}) для gesture_lab.py."""
import copy
import json
import os
import sys
import time

import bpy

sys.path.insert(0, os.path.dirname(__file__))
import gesture_lab
import hand_anatomy
from gesture_lab import FOUR, Skin

SIDE = "_R"
PAIRS = (("Index", "Middle"), ("Middle", "Ring"), ("Ring", "Pinky"))
INDEX = {"mcp": 0, "pip": 1, "dip": 2, "heading": 3, "tilt": 4}
# Щель, которую глаз уже не видит как щель, и вход кожи в кожу, который не виден (кожа мягкая), мм.
CONTACT, SOFT = .5, .3
# Межфаланговый сустав большого пальца в кулаке почти прямой (на фото около 15–20°), больше — палец загибается крючком.
THUMB_IP = 22
BINS = 6
# Кольцо пальца в кулаке: кончик подходит к основанию своего же пальца не дальше LOOP мм. Дальше — внутри кулака пусто,
# и со стороны большого пальца сквозь кулак виден ход, «как будто рука держит невидимый предмет». У живой руки это место
# заполняют подушечки и мышцы ладони, у сетки их объёма нет, поэтому кольцо сжимается сгибами (замер: при 85/105/60
# кончик указательного в 11 мм от основания, при 85/115/80 — в 4,6 мм, кончик при этом ладонь не продавливает).
LOOP = 5.
LOOPED = ("Index", "Middle", "Ring", "Pinky")


def profile(skin, a, b, phalanx, start):
    """Щель между пальцами a и b по длине фаланги phalanx пальца a: в каждой доле длины наименьшее расстояние, мм.
    Вход внутрь — отрицательное число. start — с какой доли длины начинать (у основания основной фаланги перепонка)."""
    head, tail, _ = skin.segments[a][phalanx - 1]
    axis = tail - head
    length = axis.length
    axis.normalize()
    best = [None] * BINS
    tree = skin.trees[b]
    for i, owner in enumerate(skin.owner):
        if owner != (a, phalanx):
            continue
        t = (skin.co[i] - head).dot(axis) / length
        if t < start or t >= 1:
            continue
        k = min(BINS - 1, int((t - start) / (1 - start) * BINS))
        location, _, _, distance = tree.find_nearest(skin.co[i], .03)
        if location is None:
            continue
        d = -distance * 1000 if distance < .02 and skin.is_inside(skin.co[i], b) else distance * 1000
        best[k] = d if best[k] is None else min(best[k], d)
    return [20. if v is None else v for v in best]


def measure(armature, skin, pose):
    gesture_lab.reset(armature)
    hand_anatomy.apply(armature, pose, SIDE)
    bpy.context.view_layer.update()
    skin.shot()
    sides = {}
    for a, b in PAIRS:
        sides[(a, b, 1)] = profile(skin, a, b, 1, .35)
        sides[(a, b, 2)] = profile(skin, a, b, 2, 0.)
    # Вход кожи в кожу соседа в обе стороны, средние и дальние фаланги (как в gesture_lab.measure): щель меряет профиль,
    # а здесь — что ни один палец не входит в соседа и с другой стороны, и кончиками в ладони.
    deep = {}
    for a, b in PAIRS:
        for x, y in ((a, b), (b, a)):
            for phalanx in (2, 3):
                deep[(x, y, phalanx)] = skin.inside((x, phalanx), y)[0]
    tips = {}
    for a in FOUR:
        depth = max(skin.inside((a, 3), body)[0] for body in ("Palm", a + ":base"))
        g = skin.gap({(a, 3)}, ("Palm", a + ":base"))
        tips[a] = (depth, 20. if g is None else g)
    thumb_in = max(skin.inside(("Thumb", p), b)[0] for p in (2, 3) for b in FOUR + ("Palm",))
    fingers_in = max(skin.inside((b, p), "Thumb")[0] for p in (1, 2, 3) for b in FOUR)
    loops = {}
    for a in LOOPED:
        g = skin.gap({(a, 3)}, (a + ":base",))
        loops[a] = 20. if g is None else g
    across = {}
    for target in ("Index", "Middle"):
        g = skin.gap({("Thumb", 3)}, (target,))
        across[target] = 20. if g is None else g
    report = hand_anatomy.thumb_report(armature, SIDE)
    return dict(sides=sides, tips=tips, thumb_in=thumb_in, fingers_in=fingers_in, across=across, thumb=report, loops=loops, deep=deep)


def score(m, pose, start, keys):
    enter = gap = 0.
    for (a, b, phalanx), values in m["sides"].items():
        for v in values:
            enter += max(0., -v - SOFT)
            gap += max(0., v - CONTACT) if phalanx == 2 else .5 * max(0., v - CONTACT)
    enter += sum(max(0., d - SOFT) for d in m["deep"].values())
    tips_in = sum(max(0., d - .5) for d, _ in m["tips"].values())
    tips_gap = sum(max(0., g - .5) for _, g in m["tips"].values())
    thumb_in = max(0., m["thumb_in"] - .6) + max(0., m["fingers_in"] - .6)
    across = sum(max(0., g - CONTACT) for g in m["across"].values())
    hook = max(0., m["thumb"]["Thumb03"][0] - THUMB_IP)
    swing = max(0., m["thumb"]["swing"] - hand_anatomy.THUMB_SWING)
    drift = sum(abs(get(pose, k) - get(start, k)) for k in keys)
    loop = sum(max(0., g - LOOP) for g in m["loops"].values())
    parts = dict(enter=enter, gap=gap, tips_in=tips_in, tips_gap=tips_gap, thumb_in=thumb_in, across=across, hook=hook, swing=swing, loop=loop)
    total = 4 * enter + gap + 3 * tips_in + tips_gap + 3 * thumb_in + 2 * across + .5 * hook + 5 * swing + 2 * loop + .02 * drift
    return total, parts


def get(pose, key):
    if key[0] in ("thumb", "thumb_bend"):
        return pose[key[0]][key[1]]
    if key[0] in ("thumb_turn", "cup"):
        return pose.get(key[0], 0)
    return pose["fingers"][key[0]][INDEX[key[1]]]


def put(pose, key, value):
    result = copy.deepcopy(pose)
    if key[0] in ("thumb", "thumb_bend"):
        values = list(result[key[0]])
        values[key[1]] = value
        result[key[0]] = tuple(values)
    elif key[0] in ("thumb_turn", "cup"):
        result[key[0]] = value
    else:
        row = list(result["fingers"][key[0]])
        row[INDEX[key[1]]] = value
        result["fingers"][key[0]] = tuple(row)
    return result


def show(name, m):
    for (a, b, phalanx), values in m["sides"].items():
        print("=== SIDE %s %s-%s фаланга %d: %s" % (name, a, b, phalanx, " ".join("%.1f" % v for v in values)))
    deep = sorted(((d, k) for k, d in m["deep"].items() if d > 0), reverse=True)
    print("=== DEEP %s палец в соседа: %s" % (name, ", ".join("%s%d в %s %.1f мм" % (k[0], k[2], k[1], d) for d, k in deep[:5]) or "нет"))
    print("=== TIPS %s %s" % (name, "; ".join("%s в ладонь %.1f, зазор %.1f" % (a, d, g) for a, (d, g) in m["tips"].items())))
    print("=== LOOP %s кончик до основания своего пальца: %s" % (name, ", ".join("%s %.1f мм" % (a, g) for a, g in m["loops"].items())))
    print("=== THUMB %s до указательного %.1f мм, до среднего %.1f мм, входит %.1f мм, пальцы в него %.1f мм; отведён %.0f°, разворот %.0f°, "
          "пястно-фаланговый %.0f° (вбок %.0f°), межфаланговый %.0f°"
          % (name, m["across"]["Index"], m["across"]["Middle"], m["thumb_in"], m["fingers_in"], m["thumb"]["swing"], m["thumb"]["turn"],
             m["thumb"]["Thumb02"][0], m["thumb"]["Thumb02"][1], m["thumb"]["Thumb03"][0]))


def main():
    args = sys.argv[sys.argv.index("--") + 1:]
    name, what = args[0], args[1:] or ["fingers", "curl", "thumb", "cup"]
    armature = bpy.data.objects["Rig_Hands"]
    armature.animation_data_create()
    armature.animation_data.action = None
    armature.matrix_world.identity()
    skin = Skin(armature, SIDE)
    start = gesture_lab.find_pose(name)[1]
    keys = []
    if "fingers" in what:
        keys += [(f, k) for f in FOUR for k in ("heading", "tilt")]
    if "curl" in what:
        keys += [(f, k) for f in FOUR for k in ("mcp", "pip", "dip")]
    if "thumb" in what:
        keys += [("thumb", j) for j in range(3)] + [("thumb_bend", j) for j in range(3)] + [("thumb_turn",)]
    if "cup" in what:
        keys += [("cup",)]
    began = time.time()
    best = start
    m = measure(armature, skin, best)
    best_score, parts = score(m, best, start, keys)
    show(name + ":start", m)
    print("=== FIT %s start %.2f %s (%.1f с на замер)" % (name, best_score, {k: round(v, 2) for k, v in parts.items()}, time.time() - began))
    for round_number in range(int(os.environ.get("FIT_ROUNDS", "4"))):
        improved = False
        for key in keys:
            for step in (3, -3, 1.5, -1.5, .5, -.5):
                trial = put(best, key, get(best, key) + step)
                limited = hand_anatomy.limited(trial)
                if any(limited[k] != trial[k] for k in ("fingers", "thumb_turn", "thumb_bend", "cup")):
                    continue
                trial_m = measure(armature, skin, trial)
                value, p = score(trial_m, trial, start, keys)
                if value < best_score - 1e-4:
                    best, best_score, parts, m, improved = trial, value, p, trial_m, True
                    break
        print("=== FIT %s round %d: %.2f %s (%.0f с)" % (name, round_number + 1, best_score, {k: round(v, 2) for k, v in parts.items()}, time.time() - began))
        if not improved:
            break
    show(name + ":fit", measure(armature, skin, best))
    print("=== FIT %s fingers %s" % (name, {f: tuple(round(x, 1) for x in v) for f, v in best["fingers"].items()}))
    print("=== FIT %s thumb %s turn %s bend %s cup %s" % (name, tuple(round(x, 1) for x in best["thumb"]), round(best["thumb_turn"], 1),
                                                         tuple(round(x, 1) for x in best["thumb_bend"]), round(best.get("cup", 0), 1)))
    out = os.environ.get("FIT_OUT")
    if out:
        with open(out, "w", encoding="utf-8") as f:
            json.dump({name + "Fit": best}, f, ensure_ascii=False, indent=1)
        print("=== FIT записано", out)


if __name__ == "__main__":
    main()
