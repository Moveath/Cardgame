"""«Класс» по модели автора (2026-11-23): references/hands/жесты/класс-модель/Hand_Right_ThumbsUp.fbx — правая кисть
в жесте, которую автор сделал сам (картинка Gemini → 3D-модель). Это сетка без скелета, одним куском, со своей кожей
и манжетой свитера: пальцы в ней слиты с ладонью, раскрыть её в расслабленную кисть нельзя. Поэтому в игру идёт наша
рука, а модель задаёт ей позу: наша правая кисть совмещается с кожей модели (поворот, сдвиг и масштаб — по ближайшим
точкам кожи в обе стороны) и суставы подбираются, пока кожа не ляжет на кожу модели. Модель смотрит на камеру так,
как на картинке автора (вид спереди, −Y): так же кисть должна быть видна с места игрока.

Запуск: blender --background --factory-startup --disable-autoexec --python scripts/3d/hands/thumbs_model.py -- <команда>
  show   — замер позы (TM_POSE — JSON с полями позы, иначе gestures_extra.thumbs_up_pose()): расхождение с моделью по частям
           кисти, куда смотрят оси кисти и предплечье модели.
  fit    — подбор позы от стартовой (TM_POSE или gestures_extra): большой палец (точка основания, разворот, сгибы),
           сгибы четырёх пальцев (TM_FINGERS=1); пределы суставов — живая рука и без разгиба большого пальца назад
           (TM_MCP_MIN, TM_IP_MIN). Пишет JSON (TM_OUT): позу и совмещение с моделью.
  arm    — положение руки «класса» в кадре (gesture_test.ARMS) под поворот кисти модели: TM_POSE — JSON fit.
  render — модель и наша кисть в позе TM_POSE с одних ракурсов, рядом и наложением: папка TM_RENDER (лист —
           thumbs_model_sheet.py).
TM_SMOOTH=1 — с формой кожи жеста (TM_SHAPE, по умолчанию ThumbsUpSmooth, если она есть в файле) — так кисть выглядит
в конце жеста в игре.
Грузятся только скелет и правая рука (fist_smooth.load_light) из TM_BLEND (по умолчанию рабочая копия
art3d/hands/test/CardCity_Hands_ThumbsModel.blend). Файл .blend не сохраняется."""
import json
import math
import os
import sys
import time

import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

sys.path.insert(0, os.path.dirname(__file__))
import fist_smooth  # noqa: E402
import source  # noqa: E402

fist_smooth.BLEND = os.environ.get("TM_BLEND") or os.path.join(source.ROOT, "art3d", "hands", "test", "CardCity_Hands_ThumbsModel.blend")
if "Rig_Hands" not in bpy.data.objects:
    fist_smooth.load_light()
import gesture_lab  # noqa: E402
import gestures_extra  # noqa: E402
import hand_anatomy  # noqa: E402
from gesture_lab import FOUR  # noqa: E402

SIDE = "_R"
REF = os.path.join(source.ROOT, "references", "hands", "жесты", "класс-модель", "Hand_Right_ThumbsUp.fbx")
# Кожа модели за запястьем нашей кисти (к локтю) в сравнение не идёт: у нашей кисти там предплечье в рукаве.
CROP = .004
TRIM = .9


# --- Модель автора ---------------------------------------------------------------------------------------------------

class Reference:
    """Кожа модели (без манжеты — по цвету текстуры: свитер синий), её поиск ближайших точек и манжета."""

    def __init__(self):
        before = set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=REF)
        self.ob = next(o for o in bpy.data.objects if o not in before and o.type == "MESH")
        mesh, world = self.ob.data, self.ob.matrix_world
        co = np.array([tuple(world @ v.co) for v in mesh.vertices])
        image = next(n.image for n in self.ob.active_material.node_tree.nodes if n.type == "TEX_IMAGE" and n.image)
        w, h = image.size
        px = np.empty(w * h * 4, dtype=np.float32)
        image.pixels.foreach_get(px)
        px = px.reshape(h, w, 4)
        uv = np.zeros((len(mesh.vertices), 2))
        layer = mesh.uv_layers.active.data
        for loop in mesh.loops:
            uv[loop.vertex_index] = layer[loop.index].uv
        rgb = px[np.clip((uv[:, 1] * h).astype(int), 0, h - 1), np.clip((uv[:, 0] * w).astype(int), 0, w - 1), :3]
        self.sleeve = rgb[:, 2] > rgb[:, 0]
        skin = ~self.sleeve
        faces = [tuple(p.vertices) for p in mesh.polygons]
        skin_faces = [f for f in faces if all(skin[i] for i in f)]
        self.co, self.skin = co, skin
        self.points = co[skin]
        self.tree = BVHTree.FromPolygons([Vector(p) for p in co], skin_faces)
        # Край манжеты: вершины кожи по рёбрам рядом со свитером; ось предплечья — нормаль кольца, от свитера к кисти.
        ring = set()
        for e in mesh.edges:
            a, b = e.vertices
            if skin[a] and self.sleeve[b]:
                ring.add(a)
            elif skin[b] and self.sleeve[a]:
                ring.add(b)
        ring = co[sorted(ring)]
        self.cuff = ring.mean(0)
        normal = np.linalg.svd(ring - self.cuff)[2][2]
        if np.dot(self.points.mean(0) - self.cuff, normal) < 0:
            normal = -normal
        self.forearm = normal
        self.thumb_tip = self.points[np.argmax(self.points[:, 2])]
        self.knuckles = self.points[np.argmin(self.points[:, 0])]
        print("=== REF вершин %d, кожа %d, свитер %d; манжета (%.3f, %.3f, %.3f), предплечье (%.2f, %.2f, %.2f)"
              % (len(co), skin.sum(), self.sleeve.sum(), *self.cuff, *self.forearm))

    def nearest(self, points):
        out = np.empty_like(points)
        for i, p in enumerate(points):
            out[i] = self.tree.find_nearest(Vector(p))[0]
        return out


# --- Наша кисть ------------------------------------------------------------------------------------------------------

class Hand:
    """Кожа нашей правой кисти (вершины кисти и пальцев, без предплечья) в позе: точки, поиск ближайших, оси кисти."""

    def __init__(self, armature):
        self.armature = armature
        self.ob = bpy.data.objects["Arm_Male" + SIDE]
        self.owner = gesture_lab.Skin(armature, SIDE).owner
        self.idx = np.array([i for i, o in enumerate(self.owner) if o is not None])
        local = {old: new for new, old in enumerate(self.idx)}
        self.faces = [tuple(local[i] for i in p.vertices) for p in self.ob.data.polygons
                      if all(self.owner[i] is not None for i in p.vertices)]
        self.part = [self.owner[i][0] for i in self.idx]
        step = max(1, len(self.idx) // 3000)
        self.sample = np.arange(0, len(self.idx), step)

    def pose(self, pose, smooth=0.):
        """smooth — вес формы кисти жеста: TM_SHAPE (по умолчанию ThumbsUpSmooth, если она уже есть в файле, иначе
        ThumbsSmooth — форма Gemini)."""
        gesture_lab.reset(self.armature)
        keys = self.ob.data.shape_keys
        shape = os.environ.get("TM_SHAPE") or ("ThumbsUpSmooth" if keys.key_blocks.get("ThumbsUpSmooth") else "ThumbsSmooth")
        for block in keys.key_blocks[1:]:
            block.value = smooth if block.name == shape else 0.
        prop = {"ThumbsSmooth": "thumbs_smooth", "ThumbsUpSmooth": "thumbsup_smooth"}[shape] + SIDE
        for name in list(self.armature.keys()):
            if name.startswith(("fist_smooth", "thumbs_smooth", "thumbsup_smooth")):
                self.armature[name] = smooth if name == prop else 0.
        self.armature.update_tag()
        hand_anatomy.apply(self.armature, pose, SIDE)
        bpy.context.view_layer.update()
        depsgraph = bpy.context.evaluated_depsgraph_get()
        ev = self.ob.evaluated_get(depsgraph)
        mesh = ev.to_mesh()
        co = np.zeros(len(mesh.vertices) * 3)
        mesh.vertices.foreach_get("co", co)
        ev.to_mesh_clear()
        self.co = co.reshape(-1, 3)[self.idx]
        self.tree = BVHTree.FromPolygons([Vector(p) for p in self.co], self.faces)
        bones = self.armature.pose.bones
        self.head = np.array(bones["Hand" + SIDE].head)
        self.axes = [np.array(a) for a in hand_anatomy.hand_axes(bones["Hand" + SIDE].matrix, SIDE)]
        self.thumb_tip = np.array(bones["TipThumb" + SIDE].tail)
        self.knuckles = np.array([bones[f + "02" + SIDE].head for f in ("Index", "Middle")]).mean(0)
        return self

    def nearest(self, points):
        out = np.empty_like(points)
        for i, p in enumerate(points):
            out[i] = self.tree.find_nearest(Vector(p))[0]
        return out


# --- Совмещение: поворот, сдвиг, масштаб -----------------------------------------------------------------------------

def umeyama(src, dst, w):
    w = w / w.sum()
    ms, md = (w[:, None] * src).sum(0), (w[:, None] * dst).sum(0)
    s_, d_ = src - ms, dst - md
    cov = (w[:, None] * d_).T @ s_
    u, sig, vt = np.linalg.svd(cov)
    e = np.diag([1., 1., np.sign(np.linalg.det(u) * np.linalg.det(vt))])
    r = u @ e @ vt
    scale = (sig * np.diag(e)).sum() / (w * (s_ ** 2).sum(1)).sum()
    return scale, r, md - scale * r @ ms


def landmarks(hand, ref):
    """Начальное совмещение по трём точкам: запястье — середина манжеты, кончик большого пальца, костяшки кулака."""
    src = np.array([hand.head, hand.thumb_tip, hand.knuckles])
    dst = np.array([ref.cuff, ref.thumb_tip, ref.knuckles])
    return umeyama(src, dst, np.ones(3))


def trimmed(d, share=TRIM):
    d = np.sort(d)
    return d[:max(1, int(len(d) * share))].mean()


def distances(hand, ref, fit):
    """Расстояния кожа — кожа в мм нашей кисти: от нашей кисти до модели и от модели (без кожи за запястьем) до нашей."""
    scale, r, t = fit
    ours = hand.co[hand.sample]
    there = ours @ r.T * scale + t
    a = np.linalg.norm(ref.nearest(there) - there, axis=1) / scale * 1000
    back = (ref.points - t) @ r / scale
    keep = (back - hand.head) @ hand.axes[1] > -CROP
    b_pts = back[keep]
    b = np.linalg.norm(hand.nearest(b_pts) - b_pts, axis=1) * 1000
    return a, b, keep


def icp(hand, ref, fit, rounds=20):
    scale, r, t = fit
    ours = hand.co[hand.sample]
    for _ in range(rounds):
        there = ours @ r.T * scale + t
        q = ref.nearest(there)
        back = (ref.points - t) @ r / scale
        keep = (back - hand.head) @ hand.axes[1] > -CROP
        p = hand.nearest(back[keep])
        src = np.vstack([ours, p])
        dst = np.vstack([q, ref.points[keep]])
        d = np.linalg.norm(src @ r.T * scale + t - dst, axis=1)
        c = 1.5 * np.median(d) + 1e-9
        w = np.where(d < c, 1., c / d)
        # Кожа модели за краем нашей выборки и наша кожа между слитыми пальцами модели — мимо: худшие 10 % не учитываются.
        w[d > np.quantile(d, TRIM)] = 0.
        scale, r, t = umeyama(src, dst, w)
    return scale, r, t


def best_start(hand, ref):
    base = landmarks(hand, ref)
    found = []
    for axis in ((1, 0, 0), (0, 1, 0), (0, 0, 1)):
        for angle in (-30, 0, 30):
            if angle == 0 and axis != (1, 0, 0):
                continue
            turn = np.array(Matrix.Rotation(math.radians(angle), 3, axis))
            scale, r, t = base
            center = hand.co.mean(0)
            # Поворот вокруг середины кисти в её осях.
            r2 = r @ turn
            t2 = t + scale * (r @ center - r2 @ center)
            fit = icp(hand, ref, (scale, r2, t2), 25)
            a, b, _ = distances(hand, ref, fit)
            found.append((trimmed(a) + trimmed(b), fit))
    found.sort(key=lambda f: f[0])
    return found[0][1]


def report(hand, ref, fit, name):
    a, b, keep = distances(hand, ref, fit)
    scale, r, t = fit
    parts = np.array(hand.part)[hand.sample]
    by_part = ", ".join("%s %.1f" % (p, a[parts == p].mean()) for p in ("Thumb",) + FOUR + ("Palm",))
    print("=== FIT %s: наша кожа до модели %.1f мм (90 %% — %.1f, худшая %.1f), модель до нашей %.1f мм (90 %% — %.1f, худшая %.1f); по частям: %s; масштаб модели %.4f"
          % (name, a.mean(), trimmed(a), a.max(), b.mean(), trimmed(b), b.max(), by_part, scale))
    # Оси нашей кисти в осях модели (камера модели смотрит по +Y, вверх +Z — как на картинке автора).
    toward, along, back = (r @ v for v in hand.axes)
    tip = r @ (hand.thumb_tip - hand.co.mean(0))
    fore = ref.forearm
    bones = hand.armature.pose.bones
    d = r @ (np.array(bones["TipThumb" + SIDE].tail) - np.array(bones["Thumb02" + SIDE].head))
    # Большой палец на картинке (вид спереди, без перспективы): от вертикали, плюс — вправо (наружу для правой руки).
    print("=== THUMB %s: большой палец на картинке модели %.0f° от вертикали (плюс — наружу), к камере %.2f"
          % (name, math.degrees(math.atan2(d[0], d[2])), -d[1] / np.linalg.norm(d)))
    print("=== AXES %s (в осях модели: X вправо, Y от камеры, Z вверх): к большому пальцу (%.2f, %.2f, %.2f), вдоль кисти (%.2f, %.2f, %.2f), "
          "к тылу (%.2f, %.2f, %.2f); предплечье модели (%.2f, %.2f, %.2f)"
          % (name, *toward, *along, *back, *fore))
    return a, b


# --- Поза ------------------------------------------------------------------------------------------------------------

def start_pose():
    if os.environ.get("TM_POSE"):
        with open(os.environ["TM_POSE"], encoding="utf-8") as f:
            data = json.load(f)
        pose = gestures_extra.thumbs_up_pose()
        pose.update({k: (tuple(v) if isinstance(v, list) else v) for k, v in data.items() if k in pose or k == "fingers"})
        if "fingers" in data:
            pose["fingers"] = {k: tuple(v) for k, v in data["fingers"].items()}
        return pose
    return gestures_extra.thumbs_up_pose()


def show():
    armature = bpy.data.objects["Rig_Hands"]
    armature.matrix_world.identity()
    ref = Reference()
    hand = Hand(armature).pose(start_pose(), float(os.environ.get("TM_SMOOTH", "0")))
    fit = best_start(hand, ref)
    report(hand, ref, fit, "show")
    save_fit(fit, start_pose())


def save_fit(fit, pose, cost=None):
    out = os.environ.get("TM_OUT")
    if not out:
        return
    scale, r, t = fit
    data = {"thumb": list(pose["thumb"]), "thumb_turn": pose["thumb_turn"], "thumb_bend": list(pose["thumb_bend"]),
            "fingers": {k: list(v) for k, v in pose["fingers"].items()}, "cup": pose.get("cup", 0),
            "scale": scale, "rotation": r.tolist(), "translation": t.tolist(), "cost": cost}
    with open(out, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=1)
    print("=== SAVED", out)


# --- Подбор позы (fit) -----------------------------------------------------------------------------------------------

def params(with_fingers):
    """Что подбирается: (ключ позы, индекс, шаг). Большой палец всегда; четыре пальца — с TM_FINGERS=1."""
    out = [("thumb", 0, 3.), ("thumb", 1, 3.), ("thumb", 2, 3.), ("thumb_turn", None, 5.),
           ("thumb_bend", 0, 5.), ("thumb_bend", 1, 3.), ("thumb_bend", 2, 5.)]
    if with_fingers:
        for f in FOUR:
            out += [(("fingers", f), 0, 4.), (("fingers", f), 1, 4.), (("fingers", f), 3, 3.)]
        out.append(("cup", None, 3.))
    return out


def get(pose, key, index):
    if isinstance(key, tuple):
        return pose["fingers"][key[1]][index]
    return pose[key] if index is None else pose[key][index]


def put(pose, key, index, value):
    pose = dict(pose, fingers=dict(pose["fingers"]))
    if isinstance(key, tuple):
        values = list(pose["fingers"][key[1]])
        values[index] = value
        pose["fingers"][key[1]] = tuple(values)
    elif index is None:
        pose[key] = value
    else:
        values = list(pose[key])
        values[index] = value
        pose[key] = tuple(values)
    return clamp_pose(pose)


def clamp_pose(pose):
    """Поза в пределах живой руки (hand_anatomy.limited) и без разгиба большого пальца назад (MCP_MIN, IP_MIN)."""
    pose = hand_anatomy.limited(pose)
    bend = pose["thumb_bend"]
    return dict(pose, thumb_bend=(max(MCP_MIN, bend[0]), bend[1], max(IP_MIN, bend[2])))


# Суставы большого пальца не разгибаются назад: автор 2026-11-22 — «строго вверх, без переразгибания назад». Сгибы позы
# отсчитываются от покоя модели, а в покое пястно-фаланговый согнут на 18°, межфаланговый разогнут назад на 16°
# (hand_anatomy.thumb_report), поэтому настоящий угол 0° — это −18 и +16. Без предела подбор по модели упирал
# пястно-фаланговый в −25° (TM_MCP_MIN, TM_IP_MIN — проба).
MCP_MIN = float(os.environ.get("TM_MCP_MIN", "-18"))
IP_MIN = float(os.environ.get("TM_IP_MIN", "16"))


def fit():
    armature = bpy.data.objects["Rig_Hands"]
    armature.matrix_world.identity()
    ref = Reference()
    smooth = float(os.environ.get("TM_SMOOTH", "0"))
    hand = Hand(armature)
    pose = clamp_pose(start_pose())
    state = {"fit": best_start(hand.pose(pose, smooth), ref)}

    def cost(p):
        hand.pose(p, smooth)
        fitted = icp(hand, ref, state["fit"], 8)
        a, b, _ = distances(hand, ref, fitted)
        swing = hand_anatomy.thumb_report(armature, SIDE)["swing"]
        value = trimmed(a) + trimmed(b) + .2 * max(0., swing - hand_anatomy.THUMB_SWING)
        return value, fitted

    best, state["fit"] = cost(pose)
    print("=== FIT старт %.2f" % best)
    steps = params(os.environ.get("TM_FINGERS") == "1")
    scale = 1.
    started = time.time()
    for round_ in range(int(os.environ.get("TM_ROUNDS", "12"))):
        moved = False
        for key, index, step in steps:
            for sign in (1, -1):
                trial = put(pose, key, index, get(pose, key, index) + sign * step * scale)
                if trial == pose:
                    continue
                value, fitted = cost(trial)
                if value < best - 1e-3:
                    best, pose, state["fit"], moved = value, trial, fitted, True
                    break
        print("=== FIT круг %d шаг %.2f: %.2f (%.0f с)" % (round_, scale, best, time.time() - started))
        if not moved:
            scale /= 2
            if scale < .12:
                break
    hand.pose(pose, smooth)
    state["fit"] = icp(hand, ref, state["fit"], 25)
    report(hand, ref, state["fit"], "fit")
    print("=== POSE", json.dumps({k: v for k, v in pose.items() if k in ("thumb", "thumb_turn", "thumb_bend", "fingers", "cup")}))
    save_fit(state["fit"], pose, best)


# --- Рука в кадре (arm) ----------------------------------------------------------------------------------------------

def view_frame(point):
    """Из осей модели (камера смотрит по +Y, вверх +Z) в оси от глаз игрока для точки point: +Y модели — луч от глаз
    к точке, +Z — вверх по экрану поперёк луча. Так кисть видна с места игрока, как модель на картинке автора."""
    y = Vector(point).normalized()
    z = (Vector((0, 0, 1)) - y * y.z).normalized()
    x = y.cross(z)
    return Matrix((x, y, z)).transposed()


def screen_dir(v, at):
    """Направление v в точке at на экране: (вправо, вверх), с перспективой."""
    at, tip = Vector(at), Vector(at) + Vector(v) * .05
    d = Vector((tip.x / tip.y - at.x / at.y, tip.z / tip.y - at.z / at.y))
    return d.normalized()


def arm():
    """Поворот предплечья, отклонение и разгиб кисти «класса» (gesture_test.ARMS) под поворот кисти модели: печатает
    лучшие. Поза кисти и совмещение — TM_POSE (JSON fit). Запястье — GA_WRIST или нынешнее, предплечье перебирается
    около нынешнего (от нижнего угла экрана); TM_FORE_W — вес совпадения предплечья на экране с предплечьем модели."""
    import game_test
    import gesture_test
    import in_game
    with open(os.environ["TM_POSE"], encoding="utf-8") as f:
        data = json.load(f)
    r = np.array(data["rotation"])
    armature = bpy.data.objects["Rig_Hands"]
    armature.matrix_world.identity()
    hand = Hand(armature).pose(start_pose())
    target = [Vector(r @ v) for v in hand.axes]                   # к большому пальцу, вдоль, к тылу — в осях модели
    ref_fore = Vector(Reference().forearm)
    now = gesture_test.ARMS["ThumbsUp"]
    wrist = Vector(tuple(float(v) for v in os.environ["GA_WRIST"].split(","))) if os.environ.get("GA_WRIST") else Vector(now["wrist"])
    base_fore = (wrist - Vector(now["elbow"]))
    length = base_fore.length
    fore_w = float(os.environ.get("TM_FORE_W", ".3"))
    ref_screen = Vector((ref_fore.x, ref_fore.z)).normalized()

    def evaluate(yaw, pitch, roll, dev, ext):
        fore = (Matrix.Rotation(math.radians(yaw), 3, "Z") @ Matrix.Rotation(math.radians(pitch), 3, "X") @ base_fore).normalized()
        elbow = wrist - fore * length
        a = game_test.free_arm(wrist=tuple(wrist), elbow=tuple(elbow), roll=roll, ulnar=dev, extend=ext)
        axes = hand_anatomy.hand_axes(in_game.frame(a["wrist"], a["along"], a["back"]).to_3x3(), SIDE)
        # Кисть видна у кулака, а не у запястья: луч — к точке на 6 см дальше по кисти.
        v = view_frame(wrist + axes[1] * .06)
        err = sum(math.degrees(x.angle(v @ t)) for x, t in zip(axes, target)) / 3
        fore_err = math.degrees(screen_dir(fore, wrist).angle(ref_screen))
        cost = err + fore_w * fore_err + .05 * (abs(dev) + abs(ext))
        return (cost, err, fore_err, yaw, pitch, roll, dev, ext, tuple(round(c, 3) for c in elbow))

    # Грубо, потом мельче около лучших. Пределы живой руки: отклонение к большому пальцу 20°, к мизинцу 30°, разгиб 40°, сгиб 60°.
    found = [evaluate(yaw, pitch, roll, dev, ext) for yaw in (-30, -15, 0, 15, 30) for pitch in (-30, -15, 0, 15, 30)
             for roll in range(-180, 180, 6) for dev in range(-20, 31, 5) for ext in range(-60, 41, 5)]
    found.sort()
    fine = {}
    for row in found[:6]:
        _, _, _, yaw, pitch, roll, dev, ext, _ = row
        for y2 in range(yaw - 6, yaw + 7, 3):
            for p2 in range(pitch - 6, pitch + 7, 3):
                for r2 in range(roll - 4, roll + 5, 2):
                    for d2 in range(max(-20, dev - 4), min(30, dev + 4) + 1, 2):
                        for e2 in range(max(-60, ext - 4), min(40, ext + 4) + 1, 2):
                            fine[(y2, p2, r2, d2, e2)] = None
    found = sorted(evaluate(*k) for k in fine)
    # Нынешняя рука «класса» (gesture_test.ARMS) тем же замером: предплечье как есть (поворот 0°/0°).
    current = evaluate(0, 0, now["roll"], now["ulnar"], now["extend"])
    print("=== ARM сейчас: оси кисти от модели %.1f°, предплечье на экране от модели %.0f° (поворот %d°, отклонение %d°, сгиб/разгиб %d°)"
          % (current[1], current[2], now["roll"], now["ulnar"], now["extend"]))
    for row in found[:10]:
        print("=== ARM цена %.1f: оси кисти от модели %.1f°, предплечье на экране от модели %.0f°; предплечье повёрнуто %d°/%d° -> "
              "локоть %s, поворот %d°, отклонение %d°, сгиб/разгиб %d°" % (row[:5] + (row[8],) + row[5:8]))
    print("=== ARM предплечье модели на экране (вправо, вверх): (%.2f, %.2f)" % tuple(Vector((ref_fore.x, ref_fore.z)).normalized()))


# --- Кадры: модель и наша кисть с одних ракурсов -----------------------------------------------------------------------

# Ракурсы в осях модели: откуда смотрит камера. «Как на картинке» — вид спереди, по нему автор делал модель.
VIEWS = {"картинка": (0, -1, 0), "справа": (1, 0, 0), "сверху": (0, 0, 1), "сзади": (0, 1, 0), "три-четверти": (.7, -.7, .3)}


def ours_object(hand, fit, name="ours"):
    scale, r, t = fit
    verts = hand.co @ r.T * scale + t
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([tuple(v) for v in verts], [], hand.faces)
    mesh.shade_smooth()
    ob = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def render():
    """Кадры в папку TM_RENDER: для каждого ракурса модель (<ракурс>-model.png) и наша кисть (<ракурс>-ours.png), серым,
    с прозрачным фоном — склейку и наложение контуров делает thumbs_model_sheet.py."""
    out = os.environ.get("TM_RENDER") or os.path.join(source.ROOT, "screens", "3d-hands", "жесты", "модель")
    os.makedirs(out, exist_ok=True)
    armature = bpy.data.objects["Rig_Hands"]
    armature.matrix_world.identity()
    ref = Reference()
    pose = start_pose()
    hand = Hand(armature).pose(pose, float(os.environ.get("TM_SMOOTH", "0")))
    fit = best_start(hand, ref)
    report(hand, ref, fit, "render")
    ours = ours_object(hand, fit)
    for ob in (armature, bpy.data.objects["Arm_Male" + SIDE]):
        ob.hide_render = True
    # Пустая сцена Blender (--factory-startup) приходит с кубом, лампой и камерой.
    for name in ("Cube", "Light", "Camera"):
        if name in bpy.data.objects:
            bpy.data.objects.remove(bpy.data.objects[name])
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "STUDIO"
    sc.display.shading.color_type = "SINGLE"
    sc.display.shading.single_color = (.8, .62, .52)
    sc.display.shading.show_cavity = True
    sc.render.film_transparent = True
    sc.render.resolution_x = sc.render.resolution_y = int(os.environ.get("TM_SIZE", "700"))
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    sc.collection.objects.link(cam)
    sc.camera = cam
    cam.data.type = "ORTHO"
    lo, hi = ref.co.min(0), ref.co.max(0)
    center, size = Vector((lo + hi) / 2), float((hi - lo).max())
    cam.data.ortho_scale = size * 1.1
    for view, d in VIEWS.items():
        d = Vector(d).normalized()
        cam.location = center + d * size * 3
        up = "Y" if abs(d.z) < .9 else "Z"
        cam.rotation_euler = (-d).to_track_quat("-Z", "Y" if up == "Y" else "Y").to_euler()
        if abs(d.z) > .9:
            cam.rotation_euler = Matrix(((1, 0, 0), (0, 1, 0), (0, 0, 1))).to_euler()
        for who, shown in (("model", ref.ob), ("ours", ours)):
            ref.ob.hide_render = shown is not ref.ob
            ours.hide_render = shown is not ours
            sc.render.filepath = os.path.join(out, "%s-%s.png" % (view, who))
            bpy.ops.render.render(write_still=True)
    print("=== RENDER", out)


if __name__ == "__main__":
    command = (sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else ["show"])[0]
    {"show": show, "fit": fit, "arm": arm, "render": render}[command]()
