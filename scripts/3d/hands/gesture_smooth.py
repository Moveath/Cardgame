"""Корректирующая форма кисти «класса» и «фу» ThumbsSmooth (этап A новых жестов, 2026-11-20).

Четыре пальца в этих жестах — кулак gestures.FIST, и кожа у суставов ломается так же, как в кулаке: тыл кисти доской
с углами у костяшек, лепесток у среднего сустава (fist_smooth.py). Форма кулака FistSmooth здесь не годится: она
посчитана с большим пальцем, лежащим на пальцах (и с формой FistThumb под ним), а в «классе» он поднят. Поэтому у жеста
своя форма, посчитанная тем же способом (fist_smooth.bake: Corrective Smooth в позе, переведённый в покой скелета),
но в позе «класса»: суставы четырёх пальцев как у кулака, плюс пястно-фаланговый и межфаланговый суставы большого
пальца. FistSmooth и FistThumb не меняются.

Вес формы ведут свои свойства скелета, по одному на руку: thumbs_smooth_L и thumbs_smooth_R (gesture_test.PROP + сторона;
в Unity жест можно показать одной рукой, и у второй формы быть не должно). Ключи только в клипах Take_TestThumbsUp
и Take_TestThumbsDown (с вариантами _R, _L), остальным клипам export.py дописывает ключ 0. Партия, веер, прежние жесты и проверка кулака
кожу не меняют.

С 2026-11-23 у «класса» своя форма ThumbsUpSmooth (свойства thumbsup_smooth_L и _R, ключи в клипах Take_TestThumbsUp*):
«класс» сделан по модели автора (gestures_extra.THUMBS_UP, thumbs_model.py), большой палец в нём стоит иначе, и общая форма,
посчитанная в позе «класса» Gemini, вдавливала его в указательный на 3,3 мм. ThumbsSmooth осталась за «фу» (Take_TestThumbsDown*)
и не пересчитывалась: «фу» автор менять не велел. Обе формы считаются одним способом (no_volume ниже — та же правка Gemini),
каждая в позе своего жеста (SHAPES).

Запуск: blender <.blend> --background --disable-autoexec --python scripts/3d/hands/gesture_smooth.py -- <команда>
  store   — добавить формы обеим мужским рукам и драйверы, сохранить .blend; GS_KEYS — какие (через запятую, по умолчанию
            все из SHAPES; пересчёт ThumbsSmooth даёт ту же форму — её поза gestures_extra.thumbs_pose).
  measure — без сохранения: самые острые изломы кожи кисти в позе жеста с формой и без, сдвиг формы (GS_KEY — какая форма).
  render  — без сохранения: кисть жеста крупно с формой и без (screens/3d-hands/жесты/форма, GS_OUT), ракурсы GS_VIEWS."""
import math
import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(__file__))
import fist_smooth  # noqa: E402
import gestures_extra  # noqa: E402

KEY = "ThumbsSmooth"
PROP = "thumbs_smooth"
ZONE = "ThumbsSmoothZone"
# Формы кисти жестов: имя → (свойство веса без стороны, поза, в которой форма считается, зона сглаживания).
SHAPES = {
    "ThumbsSmooth": (PROP, gestures_extra.thumbs_pose, ZONE),                                  # «фу» (и прежний «класс»)
    "ThumbsUpSmooth": ("thumbsup_smooth", gestures_extra.thumbs_up_pose, "ThumbsUpSmoothZone"),  # «класс» по модели автора
}
# Какой жест какую форму ведёт (клипы gesture_test.py, меню «ЖЕСТЫ» в игре — OwnArms3DView.cs).
GESTURE_SHAPES = {"ThumbsUp": "ThumbsUpSmooth", "ThumbsDown": "ThumbsSmooth"}
ARMS = ("Arm_Male_L", "Arm_Male_R")
# Суставы большого пальца в зоне сглаживания: (кость, радиус полной поправки, радиус, где она сходит на нет), мм.
# У «класса» и «фу» одинаковый прямой большой палец (разница 1 мм и 1°), поэтому сглаживаются и его основание,
# пястно-фаланговый и межфаланговый суставы вместе с четырьмя пальцами.
THUMB_JOINTS = (("Thumb01", 18, 36), ("Thumb02", 18, 36), ("Thumb03", 10, 20))


def no_volume(ob, armature, side, co, nor, pull=7.2, tips=True, pinky=True):
    """В «классе» и «фу» (по замечанию автора 2026-11-23):
    1) фаланги большого пальца (Thumb02, Thumb03) укорачиваются вдоль оси пальца на 14 %, чтобы палец не выглядел слишком длинным;
    2) четыре согнутых пальца подтягиваются на pull мм (7,2) ближе к ладони (-along), чтобы плотно прилегать к ладони без щели;
       tips=False — дальние фаланги (с ногтями) не подтягиваются;
    3) наружный край мизинца (Pinky01..02) плавно подбирается внутрь, чтобы в «фу» сверху не выпирал уголок мизинца
       (pinky=False — нет: в «классе» мизинец внизу стопки, и подобранный край входил в безымянный на 3,1 мм)."""
    from mathutils import Vector
    import hand_anatomy
    owners = fist_smooth.owner_names(ob)
    mw, obw = armature.matrix_world, ob.matrix_world
    bones = armature.pose.bones
    to_local = obw.inverted().to_3x3() @ mw.to_3x3()
    mat_local = obw.inverted() @ mw
    mm = obw.to_3x3().to_scale().x * 1000
    t2_head = mat_local @ bones["Thumb02" + side].head
    t_dir = (to_local @ bones["Thumb02" + side].matrix.to_3x3().col[1]).normalized()
    hand = bones["Hand" + side]
    origin = mat_local @ hand.head
    toward, along, back = ((to_local @ a).normalized() for a in hand_anatomy.hand_axes(hand.matrix, side))
    thumb_parts = {"Thumb02", "JointThumb02", "Thumb03", "JointThumb03", "TipThumb"}
    finger_parts = ({f + n for f in ("Index", "Middle", "Ring", "Pinky") for n in ("01", "02", "03")}
                    | {"Joint" + f + n for f in ("Index", "Middle", "Ring", "Pinky") for n in ("01", "02", "03")})
    pinky_outer = {"Pinky01", "JointPinky01", "Pinky02", "JointPinky02", "Pinky03", "JointPinky03", "MetaPinky"}
    shifts = []
    for p, o in zip(co, owners):
        s = Vector()
        rel = (p - origin) * mm
        tw, al, _ = rel.dot(toward), rel.dot(along), rel.dot(back)
        if o in thumb_parts:
            dist = (p - t2_head).dot(t_dir) * mm
            if dist > -5.:
                shorten = max(0., dist + 5.) * .14
                s -= t_dir * (shorten / mm)
        elif o in finger_parts and (tips or not o.endswith("03")):
            s -= along * (fist_smooth.smoothstep(36., 66., al) * pull / mm)
        if pinky and o in pinky_outer and tw < -20. and al > 40.:
            w_p = fist_smooth.smoothstep(-20., -34., tw) * fist_smooth.smoothstep(40., 60., al)
            s += (toward * (5.2 * w_p) - along * (3.5 * w_p)) / mm
        shifts.append(s)
    neighbours = [[] for _ in co]
    for e in ob.data.edges:
        a, b = e.vertices
        neighbours[a].append(b)
        neighbours[b].append(a)
    movable = [o in (thumb_parts | finger_parts | {"Thumb01", "JointThumb01", "Hand", "MetaIndex", "MetaMiddle", "MetaRing", "MetaPinky"}) for o in owners]
    for _ in range(12):
        shifts = [(s * .5 + sum((shifts[j] for j in near), Vector()) * (.5 / len(near))) if ok and near else s
                  for s, near, ok in zip(shifts, neighbours, movable)]
    return shifts


# «Класс» по модели автора: с места игрока теперь видна ладонная сторона кулака. Подтяжка пальцев на 7,2 мм, как у «фу»,
# вдавливала кончики пальцев в подушку большого пальца на 7 мм, а у пальцев кулака F (gestures_extra.THUMBS_UP) кулак
# и так плотный — у формы «класса» подтяжки нет (UP_PULL, мм; GS_UP_PULL — проба; UP_TIPS, GS_UP_TIPS=0 — не тянуть
# дальние фаланги).
UP_PULL = float(os.environ.get("GS_UP_PULL", "0"))
UP_TIPS = os.environ.get("GS_UP_TIPS", "1") == "1"
# Зона сглаживания «класса»: только тыл пальцев (fist_smooth.zone facing). С боков, как у «фу», сглаживание раздувало
# бока соседних пальцев, они входили друг в друга на 3–4 мм, и в складках с места игрока проступала изнанка соседнего
# пальца тёмными треугольниками. GS_UP_FACING="от,до" — проба.
UP_FACING = tuple(float(v) for v in os.environ.get("GS_UP_FACING", "-.5,.1").split(","))
UP_PINKY = os.environ.get("GS_UP_PINKY", "0") == "1"
FACINGS = {"ThumbsSmooth": (-.5, .1), "ThumbsUpSmooth": UP_FACING}
# Форма «класса» считается без вывернутых граней (fist_smooth.unfold): грань от сглаживания поворачивается не больше чем
# на GS_UP_GUARD градусов (0 — без этого, проба).
GUARDS = {"ThumbsSmooth": 0., "ThumbsUpSmooth": float(os.environ.get("GS_UP_GUARD", "45"))}
# Суставы большого пальца в зоне сглаживания «класса»: мягкое сглаживание обоих суставов большого пальца.
JOINTS = {"ThumbsSmooth": THUMB_JOINTS, "ThumbsUpSmooth": (("Thumb02", 12, 24), ("Thumb03", 8, 16))}

_REF_CACHE = {}


def _load_thumbs_ref(mirror=False):
    """Загружает геометрию кожи модели автора (Hand_Right_ThumbsUp.fbx) с уменьшением текстуры до 256×256 в памяти
    и сразу удаляет временные объекты из сцены, чтобы не засорять .blend и не расходовать лишнюю RAM."""
    if mirror in _REF_CACHE:
        return _REF_CACHE[mirror]
    import numpy as np
    from mathutils import Vector
    from mathutils.bvhtree import BVHTree
    import source
    npz_path = os.environ.get("GS_REF_NPZ")
    if npz_path and os.path.exists(npz_path):
        z = np.load(npz_path)
        co = z["co"].astype(np.float64)
        nor = z["nor"].astype(np.float64)
        skin = z["skin"].astype(bool)
        skin_faces = [tuple(int(i) for i in f) for f in z["skin_tris"]]
        cuff = z["cuff"].astype(np.float64)
    else:
        ref_path = os.path.join(source.ROOT, "references", "hands", "жесты", "класс-модель", "Hand_Right_ThumbsUp.fbx")
        tex_override = os.environ.get("GS_REF_TEX")
        before_ob = set(bpy.data.objects)
        before_me = set(bpy.data.meshes)
        before_ma = set(bpy.data.materials)
        before_im = set(bpy.data.images)
        bpy.ops.import_scene.fbx(filepath=ref_path, use_image_search=(not tex_override))
        new_obs = [o for o in bpy.data.objects if o not in before_ob]
        ref_ob = next(o for o in new_obs if o.type == "MESH")
        mesh, world = ref_ob.data, ref_ob.matrix_world
        co = np.array([tuple(world @ v.co) for v in mesh.vertices], dtype=np.float64)
        w3 = world.to_3x3()
        nor = np.array([tuple((w3 @ v.normal).normalized()) for v in mesh.vertices], dtype=np.float64)
        if tex_override and os.path.exists(tex_override):
            img = bpy.data.images.load(tex_override, check_existing=False)
        else:
            img = next(n.image for n in ref_ob.active_material.node_tree.nodes if n.type == "TEX_IMAGE" and n.image)
            img.scale(256, 256)
        w, h = img.size
        px = np.empty(w * h * 4, dtype=np.float32)
        img.pixels.foreach_get(px)
        px = px.reshape(h, w, 4)
        uv = np.zeros((len(mesh.vertices), 2))
        layer = mesh.uv_layers.active.data
        for loop in mesh.loops:
            uv[loop.vertex_index] = layer[loop.index].uv
        rgb = px[np.clip((uv[:, 1] * h).astype(int), 0, h - 1), np.clip((uv[:, 0] * w).astype(int), 0, w - 1), :3]
        sleeve = rgb[:, 2] > rgb[:, 0]
        skin = ~sleeve
        faces = [tuple(p.vertices) for p in mesh.polygons]
        skin_faces = [f for f in faces if all(skin[i] for i in f)]
        ring_idx = set()
        edge_set = set()
        for f in faces:
            n_f = len(f)
            for k in range(n_f):
                a, b = f[k], f[(k + 1) % n_f]
                if (a, b) not in edge_set and (b, a) not in edge_set:
                    edge_set.add((a, b))
                    if skin[a] and sleeve[b]:
                        ring_idx.add(a)
                    elif skin[b] and sleeve[a]:
                        ring_idx.add(b)
        cuff = co[sorted(ring_idx)].mean(0)
        for o in new_obs:
            bpy.data.objects.remove(o, do_unlink=True)
        for m in list(bpy.data.meshes):
            if m not in before_me and m.users == 0:
                bpy.data.meshes.remove(m)
        for ma in list(bpy.data.materials):
            if ma not in before_ma and ma.users == 0:
                bpy.data.materials.remove(ma)
        for im in list(bpy.data.images):
            if im not in before_im and im.users == 0:
                bpy.data.images.remove(im)

    if mirror:
        co = co.copy()
        nor = nor.copy()
        cuff = cuff.copy()
        co[:, 0] *= -1.0
        nor[:, 0] *= -1.0
        cuff[0] *= -1.0
        skin_faces = [f[::-1] for f in skin_faces]

    points = co[skin]
    tree = BVHTree.FromPolygons([Vector(p) for p in co], skin_faces)
    thumb_tip = points[np.argmax(points[:, 2])]
    knuckles = points[np.argmax(points[:, 0]) if mirror else np.argmin(points[:, 0])]
    data = {
        "co": co, "nor": nor, "skin": skin, "skin_faces": skin_faces,
        "points": points, "tree": tree, "cuff": cuff,
        "thumb_tip": thumb_tip, "knuckles": knuckles,
    }
    _REF_CACHE[mirror] = data
    return data


def _umeyama(src, dst, w):
    import numpy as np
    w = w / w.sum()
    ms, md = (w[:, None] * src).sum(0), (w[:, None] * dst).sum(0)
    s_, d_ = src - ms, dst - md
    cov = (w[:, None] * d_).T @ s_
    u, sig, vt = np.linalg.svd(cov)
    e = np.diag([1.0, 1.0, np.sign(np.linalg.det(u) * np.linalg.det(vt))])
    r = u @ e @ vt
    scale = (sig * np.diag(e)).sum() / (w * (s_ ** 2).sum(1)).sum()
    return scale, r, md - scale * r @ ms


def _align_to_ref(hand_co, hand_faces, hand_head, hand_along, thumb_tip, knuckles, ref):
    import numpy as np
    from mathutils import Matrix, Vector
    from mathutils.bvhtree import BVHTree
    step = max(1, len(hand_co) // 2500)
    sample = hand_co[::step]
    h_tree = BVHTree.FromPolygons([Vector(p) for p in hand_co], hand_faces)
    ref_tree = ref["tree"]
    ref_pts = ref["points"]

    def nearest_ref(pts):
        out = np.empty_like(pts)
        for i, p in enumerate(pts):
            out[i] = ref_tree.find_nearest(Vector(p))[0]
        return out

    def nearest_hand(pts):
        out = np.empty_like(pts)
        for i, p in enumerate(pts):
            out[i] = h_tree.find_nearest(Vector(p))[0]
        return out

    def run_icp(fit, rounds):
        scale, r, t = fit
        for _ in range(rounds):
            there = sample @ r.T * scale + t
            q = nearest_ref(there)
            back = (ref_pts - t) @ r / scale
            keep = (back - hand_head) @ hand_along > -0.004
            p = nearest_hand(back[keep])
            src = np.vstack([sample, p])
            dst = np.vstack([q, ref_pts[keep]])
            d = np.linalg.norm(src @ r.T * scale + t - dst, axis=1)
            c = 1.5 * np.median(d) + 1e-9
            w = np.where(d < c, 1.0, c / d)
            w[d > np.quantile(d, 0.9)] = 0.0
            scale, r, t = _umeyama(src, dst, w)
        return scale, r, t

    base = _umeyama(np.array([hand_head, thumb_tip, knuckles]),
                    np.array([ref["cuff"], ref["thumb_tip"], ref["knuckles"]]), np.ones(3))
    center = hand_co.mean(0)
    best = None
    for axis in ((1, 0, 0), (0, 1, 0), (0, 0, 1)):
        for angle in (-30, 0, 30):
            if angle == 0 and axis != (1, 0, 0):
                continue
            turn = np.array(Matrix.Rotation(math.radians(angle), 3, axis))
            scale, r, t = base
            r2 = r @ turn
            t2 = t + scale * (r @ center - r2 @ center)
            fit = run_icp((scale, r2, t2), 20)
            s2, rm, tm = fit
            there = sample @ rm.T * s2 + tm
            a = np.sort(np.linalg.norm(nearest_ref(there) - there, axis=1) / s2)
            back = (ref_pts - tm) @ rm / s2
            keep = (back - hand_head) @ hand_along > -0.004
            b = np.sort(np.linalg.norm(nearest_hand(back[keep]) - back[keep], axis=1))
            score = a[:int(len(a) * 0.9)].mean() + b[:int(len(b) * 0.9)].mean()
            if best is None or score < best[0]:
                best = (score, fit)
    return best[1]


def up_volume(ob, armature, side, co, nor):
    """Доводка формы кожи «класса» (ThumbsUpSmooth) под 3D-модель автора Hand_Right_ThumbsUp.fbx (для _L — зеркально):
    1) плавная анатомическая коррекция пропорций: укорочение и наполнение большого пальца, объём подушки большого пальца,
       непрерывное поджатие мизинца/безымянного и лёгкая округлость четырёх сложенных пальцев без резких границ;
    2) гармоническая (лапласиан-сглаженная) полировка видимой наружной поверхности под поверхность модели автора с плавным
       затуханием у запястья (предплечье и рукав остаются неподвижными)."""
    import numpy as np
    from mathutils import Vector
    from mathutils.bvhtree import BVHTree
    import gesture_lab
    import hand_anatomy

    ref = _load_thumbs_ref(mirror=(side == "_L"))
    obw = ob.matrix_world
    obw_inv = obw.inverted()
    w3 = obw.to_3x3()
    mw = armature.matrix_world
    bones = armature.pose.bones

    co_w = np.array([tuple(obw @ p) for p in co], dtype=np.float64)
    nor_w = np.array([tuple((w3 @ n).normalized()) for n in nor], dtype=np.float64)
    n_verts = len(co_w)
    faces = [tuple(p.vertices) for p in ob.data.polygons]

    ea = np.array([e.vertices[0] for e in ob.data.edges], dtype=np.int32)
    eb = np.array([e.vertices[1] for e in ob.data.edges], dtype=np.int32)
    deg = np.zeros(n_verts, dtype=np.float64)
    np.add.at(deg, ea, 1.0)
    np.add.at(deg, eb, 1.0)
    deg = np.maximum(deg, 1.0)

    def apply_lap(X):
        out = np.zeros_like(X)
        diff = X[ea] - X[eb]
        np.add.at(out, ea, diff)
        np.add.at(out, eb, -diff)
        return out

    def blur_field(F, mask, iters):
        X = F.copy()
        for _ in range(iters):
            s = np.zeros_like(X)
            np.add.at(s, ea, X[eb])
            np.add.at(s, eb, X[ea])
            avg = s / deg[:, None]
            X = np.where(mask[:, None], 0.5 * X + 0.5 * avg, X)
        return X

    tri_idx = []
    for f in faces:
        if len(f) == 3:
            tri_idx.append(f)
        elif len(f) == 4:
            tri_idx.append((f[0], f[1], f[2]))
            tri_idx.append((f[0], f[2], f[3]))
    tri_idx = np.array(tri_idx, dtype=np.int32)

    def calc_normals(P):
        v0, v1, v2 = P[tri_idx[:, 0]], P[tri_idx[:, 1]], P[tri_idx[:, 2]]
        fn = np.cross(v1 - v0, v2 - v0)
        vn = np.zeros_like(P)
        np.add.at(vn, tri_idx[:, 0], fn)
        np.add.at(vn, tri_idx[:, 1], fn)
        np.add.at(vn, tri_idx[:, 2], fn)
        return vn / np.maximum(np.linalg.norm(vn, axis=1, keepdims=True), 1e-12)

    owner_bone = fist_smooth.owner_names(ob)
    owners_full = gesture_lab.Skin(armature, side).owner
    is_hand = np.array([o is not None for o in owners_full], dtype=bool)
    hand_idx = np.where(is_hand)[0]

    hand_head = np.array(mw @ bones["Hand" + side].head, dtype=np.float64)
    toward, along, back = [np.array((mw.to_3x3() @ a).normalized(), dtype=np.float64)
                           for a in hand_anatomy.hand_axes(bones["Hand" + side].matrix, side)]
    t2_head = np.array(mw @ bones["Thumb02" + side].head, dtype=np.float64)
    t_tip = np.array(mw @ bones["TipThumb" + side].tail, dtype=np.float64)
    t_dir = (t_tip - t2_head) / max(np.linalg.norm(t_tip - t2_head), 1e-9)

    thumb_set = {"Thumb02", "JointThumb02", "Thumb03", "JointThumb03", "TipThumb"}
    palm_thenar = {"Thumb01", "JointThumb01", "Hand", "MetaIndex", "MetaMiddle"}

    # Этап 1: плавная анатомическая настройка пропорций кисти под модель автора.
    shift1 = np.zeros_like(co_w)
    for i in hand_idx:
        ow = owner_bone[i]
        part = owners_full[i][0]
        p = co_w[i]
        rel = p - hand_head
        rel_tw = float(np.dot(rel, toward))
        rel_al = float(np.dot(rel, along))
        s = np.zeros(3, dtype=np.float64)

        if ow in thumb_set:
            d = float(np.dot(p - t2_head, t_dir))
            if d > -0.008:
                s -= t_dir * (max(0.0, d + 0.008) * 0.145)
            rad = (p - t2_head) - t_dir * d
            rn = np.linalg.norm(rad)
            if rn > 1e-4:
                s += (rad / rn) * 0.0014 * min(1.0, max(0.0, (d + 0.01) / 0.02))
        elif part in ("Index", "Middle", "Ring", "Pinky") or ow in ("MetaPinky", "MetaRing", "MetaMiddle", "MetaIndex"):
            w_al = fist_smooth.smoothstep(0.020, 0.075, rel_al)
            w_ulnar = fist_smooth.smoothstep(0.005, -0.042, rel_tw)
            s += (toward * (0.0072 * w_ulnar) - along * (0.0022 + 0.0052 * w_ulnar) + back * (0.0010 + 0.0018 * w_ulnar)) * w_al
            if part in ("Index", "Middle", "Ring", "Pinky"):
                exposed = fist_smooth.smoothstep(0.05, 0.60, float(np.dot(nor_w[i], -back))) + 0.6 * fist_smooth.smoothstep(0.20, 0.80, float(np.dot(nor_w[i], along)))
                s += nor_w[i] * (0.0017 * min(1.0, exposed))

        if ow in palm_thenar:
            palm_facing = fist_smooth.smoothstep(0.05, 0.70, float(np.dot(nor_w[i], -back)))
            tw_zone = fist_smooth.smoothstep(0.002, 0.028, rel_tw)
            al_zone = fist_smooth.smoothstep(0.008, 0.030, rel_al) * (1.0 - fist_smooth.smoothstep(0.058, 0.082, rel_al))
            s += nor_w[i] * (0.0045 * palm_facing * tw_zone * al_zone)
        shift1[i] = s

    movable = np.array([not ow.startswith("Forearm") for ow in owner_bone], dtype=bool)
    shift1 = blur_field(shift1, movable, iters=16)
    co1 = co_w + shift1
    nor1 = calc_normals(co1)

    # Совмещение нашей кисти (после этапа 1) с моделью автора.
    local_map = {old: new for new, old in enumerate(hand_idx)}
    h_faces = [tuple(local_map[i] for i in f) for f in faces if all(is_hand[i] for i in f)]
    t_verts = co1[np.array([o is not None and o[0] == "Thumb" for o in owners_full])]
    thumb_tip = t_verts[np.argmax((t_verts - t2_head) @ t_dir)]
    knuckles = np.array([mw @ bones[f + "02" + side].head for f in ("Index", "Middle")]).mean(0)
    scale, r, t = _align_to_ref(co1[hand_idx], h_faces, hand_head, along, thumb_tip, knuckles, ref)

    ref_co_h = (ref["co"] - t) @ r / scale
    ref_tree_h = BVHTree.FromPolygons([Vector(p) for p in ref_co_h], ref["skin_faces"])

    along_h = (co1 - hand_head) @ along
    wrist_w = np.clip((along_h - (-0.008)) / (0.018 - (-0.008)), 0.0, 1.0)
    wrist_w = wrist_w * wrist_w * (3.0 - 2.0 * wrist_w)
    for i, ow in enumerate(owner_bone):
        if ow.startswith("Forearm"):
            wrist_w[i] = 0.0

    # Видимые наружные вершины (не спрятанные внутри щелей между пальцами).
    self_tree = BVHTree.FromPolygons([Vector(p) for p in co1], faces)
    outer = np.zeros(n_verts, dtype=bool)
    for i in hand_idx:
        p = Vector(co1[i])
        n = Vector(nor1[i])
        hit = self_tree.ray_cast(p + n * 0.0008, n, 0.010)
        outer[i] = (hit[0] is None) or (hit[3] > 0.007)

    # Этап 2: гармоническая полировка наружной поверхности под модель автора.
    cur = co1.copy()
    w_lock = np.where(wrist_w <= 1e-4, 500.0, 0.0)
    is_thumb = np.array([o is not None and o[0] == "Thumb" for o in owners_full], dtype=bool)
    is_palm = np.array([o is not None and o[0] == "Palm" for o in owners_full], dtype=bool)
    is_pinky = np.array([o is not None and o[0] == "Pinky" for o in owners_full], dtype=bool)

    for lam_s in (14.0, 10.5, 8.0, 6.5):
        cur_n = calc_normals(cur)
        target_d = np.zeros_like(cur)
        w_data = np.zeros(n_verts, dtype=np.float64)
        for i in hand_idx:
            if not outer[i] or wrist_w[i] <= 1e-4:
                continue
            loc, rn, _, dist = ref_tree_h.find_nearest(Vector(cur[i]))
            loc = np.array(loc, dtype=np.float64)
            rn = np.array(rn, dtype=np.float64)
            dot = float(np.dot(cur_n[i], rn))
            if dist > 0.013 or dot < 0.40:
                continue
            vec = loc - cur[i]
            vlen = np.linalg.norm(vec)
            if is_thumb[i] or is_palm[i]:
                cap, part_w = 0.006, 1.6
            elif is_pinky[i]:
                cap, part_w = 0.0028, 0.45
            else:
                cap, part_w = 0.0035, 0.55
            if vlen > cap:
                vec = vec * (cap / vlen)
            conf = (dot - 0.40) / 0.60
            w = part_w * wrist_w[i] * (0.25 + 0.75 * conf)
            target_d[i] = (cur[i] + vec - co1[i]) * 0.90
            w_data[i] = w

        diag_a = w_data + w_lock + 0.14
        rhs_b = target_d * w_data[:, None]

        def matvec(X):
            return diag_a[:, None] * X + lam_s * apply_lap(X)

        X = cur - co1
        R = rhs_b - matvec(X)
        precond = 1.0 / np.maximum(diag_a + lam_s * deg, 1e-9)
        Z = precond[:, None] * R
        P = Z.copy()
        rz = float(np.sum(R * Z))
        for _ in range(100):
            if rz < 1e-12:
                break
            AP = matvec(P)
            pAp = float(np.sum(P * AP))
            if pAp < 1e-14:
                break
            a_cg = rz / pAp
            X = X + a_cg * P
            R = R - a_cg * AP
            Z = precond[:, None] * R
            rz_new = float(np.sum(R * Z))
            P = Z + (rz_new / rz) * P
            rz = rz_new
        cur = co1 + X

    disp_w = blur_field(cur - co_w, movable, iters=5)
    w2l = obw_inv.to_3x3()
    print("=== THUMBS UP MORPH %s: max_shift %.1f мм (scale %.3f)" % (side, np.linalg.norm(disp_w, axis=1).max() * 1000, scale))
    return [w2l @ Vector(d) for d in disp_w]


# Правка формы поверх сглаживания для каждой формы (fist_smooth.bake extra).
EXTRAS = {"ThumbsSmooth": no_volume, "ThumbsUpSmooth": up_volume}


def quiet(armature):
    """Все формы рук на 0 и их свойства на 0: форма считается от голой кожи, как FistSmooth."""
    props = [fist_smooth.PROP, PROP] + [prop + side for prop, _, _ in SHAPES.values() for side in ("_L", "_R")]
    for prop in props:
        if prop in armature:
            armature[prop] = 0.
    armature.update_tag()
    for name in ARMS:
        keys = bpy.data.objects[name].data.shape_keys
        if keys:
            for block in keys.key_blocks[1:]:
                block.value = 0.
    bpy.context.view_layer.update()


def bake(armature, ob, key=KEY):
    _, pose, zone = SHAPES[key]
    return fist_smooth.bake(armature, ob, ob.name[-2:], key_name=key, pose=pose(), zone_name=zone,
                            thumb_joints=JOINTS[key], extra=EXTRAS[key], facing=FACINGS[key], guard=GUARDS[key])


def set_value(value, key=KEY):
    """Вес формы key: свойство скелета (драйвер) и сам ключ — без update_tag драйвер в Blender не пересчитывается."""
    armature = bpy.data.objects["Rig_Hands"]
    prop = SHAPES[key][0]
    for name in ARMS:
        if prop + name[-2:] in armature:
            armature[prop + name[-2:]] = value
    armature.update_tag()
    for name in ARMS:
        keys = bpy.data.objects[name].data.shape_keys
        block = keys.key_blocks.get(key) if keys else None
        if block is not None:
            block.value = value
    bpy.context.view_layer.update()


def store():
    armature = bpy.data.objects["Rig_Hands"]
    saved_action = armature.animation_data.action if armature.animation_data else None
    saved_matrix = armature.matrix_world.copy()
    armature.animation_data_create()
    armature.animation_data.action = None
    armature.matrix_world.identity()
    quiet(armature)
    wanted = os.environ.get("GS_KEYS", ",".join(SHAPES)).split(",")
    for shape in wanted:
        prop = SHAPES[shape][0]
        for name in ARMS:
            if prop + name[-2:] not in armature:
                armature[prop + name[-2:]] = 0.
            key = bake(armature, bpy.data.objects[name], shape)
            fist_smooth.drive(armature, key, prop + name[-2:])
            quiet(armature)
        print("=== GESTURE SMOOTH форма %s посчитана в позе %s" % (shape, SHAPES[shape][1].__name__))
    # Прежнее общее свойство веса (до весов по рукам) — убрать, если осталось.
    if PROP in armature:
        del armature[PROP]
    armature.animation_data.action = saved_action
    armature.matrix_world = saved_matrix
    bpy.ops.wm.save_mainfile()
    print("=== GESTURE SMOOTH сохранено", bpy.data.filepath)


def ridges(ob, armature, side):
    """Самые острые изломы кожи кисти на тыле и боках: угол между соседними гранями, градусы (как ridge.py для ладони
    кулака). Складки внутри кулака на ладонной стороне — настоящие складки кожи, они не считаются."""
    owners = fist_smooth.owner_names(ob)
    back = (armature.matrix_world.to_3x3() @ armature.pose.bones["Hand" + side].matrix.to_3x3()).col[2].normalized()
    world = ob.matrix_world.to_3x3()
    faces = {}
    for p in ob.data.polygons:
        for k in p.edge_keys:
            faces.setdefault(k, []).append(p.index)
    ev = ob.evaluated_get(bpy.context.evaluated_depsgraph_get())
    mesh = ev.to_mesh()
    normals = [p.normal.copy() for p in mesh.polygons]
    ev.to_mesh_clear()
    found = []
    for k, f in faces.items():
        a, b = owners[k[0]], owners[k[1]]
        if len(f) != 2 or not a or not b or a.startswith("Forearm") or b.startswith("Forearm"):
            continue
        if min((world @ normals[i]).normalized().dot(back) for i in f) < -.2:
            continue
        found.append((math.degrees(normals[f[0]].angle(normals[f[1]])), a if a == b else a + "/" + b))
    found.sort(reverse=True)
    return found


def measure():
    import hand_anatomy
    armature = bpy.data.objects["Rig_Hands"]
    armature.animation_data_create()
    armature.animation_data.action = None
    armature.matrix_world.identity()
    quiet(armature)
    key = os.environ.get("GS_KEY", KEY)
    ob = bpy.data.objects["Arm_Male_R"]
    if not (ob.data.shape_keys and ob.data.shape_keys.key_blocks.get(key)):
        bake(armature, ob, key)
    fist_smooth.reset(armature)
    hand_anatomy.apply(armature, SHAPES[key][1](), "_R")
    for value in (0., 1.):
        set_value(value, key)
        found = ridges(ob, armature, "_R")
        print("=== RIDGES %s w=%.0f изломов >60°: %d, >45°: %d; круче всего: %s" % (
            key, value, sum(a > 60 for a, _ in found), sum(a > 45 for a, _ in found), "; ".join("%.0f° %s" % f for f in found[:6])))
    set_value(0., key)
    fist_smooth.reset(armature)


def render():
    import gesture_lab
    import hand_anatomy
    # Ракурсы фото автора: «класс» со стороны ладони, большой палец вверх; «фу» с тыла, большой палец вниз.
    gesture_lab.VIEWS.update({"фото": ((0, .1, -1), (1, 0, 0)), "тыл-вверх": ((0, .1, 1), (1, 0, 0)),
                              "спереди-вверх": ((0, 1, -.2), (1, 0, 0)), "тыл-вниз": ((0, .1, 1), (-1, 0, 0))})
    out = os.path.abspath(os.environ.get("GS_OUT") or os.path.join(gesture_lab.source.ROOT, "screens", "3d-hands", "жесты", "форма"))
    os.makedirs(out, exist_ok=True)
    armature = bpy.data.objects["Rig_Hands"]
    armature.animation_data_create()
    armature.animation_data.action = None
    armature.matrix_world.identity()
    quiet(armature)
    sc, camera = gesture_lab.stage()
    side = os.environ.get("GS_SIDE", "_L")
    skin = gesture_lab.Skin(armature, side)
    key = os.environ.get("GS_KEY", KEY)
    gesture_lab.reset(armature)
    hand_anatomy.apply(armature, SHAPES[key][1](), side)
    gesture_lab.only_hand(side)
    center, *axes = gesture_lab.frame_hand(armature, skin, side)
    for value in (0., 1.):
        set_value(value, key)
        for view in os.environ.get("GS_VIEWS", "фото,тыл-вверх,костяшки,спереди-вверх").split(","):
            gesture_lab.shoot(sc, camera, center, axes, view, os.path.join(out, "%s-%s-%s-w%d.png" % (key, side[-1], view, value)))
    print("=== GESTURE SMOOTH RENDER", out)


if __name__ == "__main__":
    command = (sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else ["measure"])[0]
    {"store": store, "measure": measure, "render": render}[command]()
