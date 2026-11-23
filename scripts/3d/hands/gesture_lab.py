"""Стенд базовых поз кисти: ставит позу из gestures.py на руки (рука в положении покоя скелета), меряет кожу
и снимает кисть с шести сторон. Файл .blend не сохраняет.

Замеры (measure):
- пересечения пальцев — та же доля треугольников, что в сравнении моделей (candidate_check.overlaps), с большим пальцем и без;
- глубина: на сколько миллиметров кожа одного пальца уходит в другой палец и в ладонь;
- большой палец: зазор до пальцев, на которых он лежит, и глубина входа в них;
- кончики пальцев: зазор до ладони (в кулаке подушечки касаются ладони).

Запуск: blender art3d/hands/CardCity_Hands.blend --background --disable-autoexec --python scripts/3d/hands/gesture_lab.py -- <команда> [позы]
Команды: measure — замеры; render — кадры в screens/3d-hands/stage2/<поза>/; both — и то и другое;
rest — напечатать позу покоя модели числами новых поз.
Позы — имена из gestures.POSES; «Rest» — поза покоя скелета; «Old:<имя>» — поза из poses.POSES (прежняя система);
«Idle>Fist:0.5» — поза между двумя позами; «Clip:IdleFistIdle:9» — кадр клипа из gestures.CLIPS.
Команда frames <клип> меряет каждый кадр клипа и печатает таблицу: так проверяются промежуточные кадры.
Переменные: LAB_JSON — файл с позами {имя: поза} поверх gestures.POSES (для проб без правки gestures.py);
LAB_SIDES=R|L|LR — какие руки снимать (по умолчанию R); LAB_VIEWS — список ракурсов через запятую;
LAB_OUT — папка кадров; LAB_CPU=1 — рендер на процессоре (когда видеокарту держит Unity); LAB_SIZE — сторона кадра;
LAB_DISTANCE — расстояние камеры, м.
LAB_SKIN — подмена слоёв кожи для сравнения, через «;»: base=<картинка цвета> (или flat — ровный тон),
normal=<карта рельефа> (или flat — без рельефа), strength=<сила рельефа>, rough=<шероховатость>,
mask=<маска шероховатости: G — складки>, crease=<насколько складки матовее>. Пути — от art3d/hands/textures или полные."""
import json
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

sys.path.insert(0, os.path.dirname(__file__))
os.environ.setdefault("SKIN", "young")
import candidate_check
import gestures
import hand_anatomy
import poses
import seat_view
import source
from probe_overlap import part_of

FINGERS = ("Thumb", "Index", "Middle", "Ring", "Pinky")
FOUR = ("Index", "Middle", "Ring", "Pinky")
# Путь — абсолютный: относительный путь кадра Blender отсчитывает не от рабочей папки, а от корня диска.
OUT = os.path.abspath(os.environ.get("LAB_OUT") or os.path.join(source.ROOT, "screens", "3d-hands", "stage2"))
# Ракурс: откуда смотрит камера в осях кисти (к большому пальцу, вдоль пальцев, к тылу) и где у кадра верх.
VIEWS = {
    "тыл": ((0, .15, 1), (0, 1, 0)),
    "спереди": ((0, 1, .25), (0, 0, 1)),
    "ладонь": ((0, .15, -1), (0, 1, 0)),
    "большой": ((1, .1, .2), (0, 0, 1)),
    "мизинец": ((-1, .1, .2), (0, 0, 1)),
    "три-четверти": ((.7, .7, .5), (0, 0, 1)),
    # Как видит свою руку играющий: из-за запястья и сверху.
    "от-игрока": ((.15, -.75, .65), (0, 0, 1)),
    # Ряд костяшек и передняя грань кулака сверху-спереди; низ кулака со средними фалангами.
    "костяшки": ((0, .7, .7), (0, 0, 1)),
    "низ": ((.25, .7, -.65), (0, 1, 0)),
}
# Ракурсы по умолчанию — первые шесть; остальные снимаются, когда названы в LAB_VIEWS.
DEFAULT_VIEWS = ("тыл", "спереди", "ладонь", "большой", "мизинец", "три-четверти")
# Камера в 38 см от кисти, объектив 85 мм: в кадре около 16 см, кисть целиком и запястье.
DISTANCE, LENS = float(os.environ.get("LAB_DISTANCE", ".38")), 85


def load_pose(pose):
    pose = dict(pose)
    pose["fingers"] = {k: tuple(v) for k, v in pose["fingers"].items()}
    pose["thumb"] = tuple(pose["thumb"])
    pose["thumb_bend"] = tuple(pose.get("thumb_bend", (0, 0, 0)))
    pose["wrist"] = tuple(pose.get("wrist", (0, 0)))
    return pose


def all_poses():
    table = dict(gestures.POSES)
    path = os.environ.get("LAB_JSON")
    if path:
        with open(path, encoding="utf-8") as f:
            for name, pose in json.load(f).items():
                table[name] = load_pose(pose)
    return table


def find_pose(name):
    """Поза по имени: (старая ли это система, сама поза)."""
    if name == "Rest":
        return False, None
    if name.startswith("Old:"):
        return True, poses.POSES[name[4:]]
    if name.startswith("Clip:"):
        _, clip, frame = name.split(":")
        return False, gestures.clip_pose(clip, int(frame))
    if name.startswith("Bend:"):
        return False, gestures.half_closed(float(name[5:]))
    table = all_poses()
    if ">" in name:
        pair, share = name.split(":")
        a, b = pair.split(">")
        return False, hand_anatomy.mix(table[a], table[b], float(share))
    return False, table[name]


def reset(armature):
    for bone in armature.pose.bones:
        bone.rotation_mode = "XYZ"
        bone.rotation_euler = (0, 0, 0)
        bone.location = (0, 0, 0)
        bone.scale = (1, 1, 1)
    bpy.context.view_layer.update()


def put(armature, name, sides=("_L", "_R")):
    old, pose = find_pose(name)
    reset(armature)
    if pose is not None:
        for side in sides:
            if old:
                poses.apply_pose(armature, pose, side)
            else:
                hand_anatomy.apply(armature, pose, side)
    bpy.context.view_layer.update()


def body_of(owner):
    """К какому телу относится вершина: палец целиком, большой палец без основания, ладонь с основанием большого пальца."""
    if owner is None:
        return None
    if owner[0] == "Palm" or owner == ("Thumb", 1):
        return "Palm"
    return owner[0]


class Skin:
    """Кожа одной руки по частям: фаланги пальцев и тела (пальцы, ладонь)."""

    def __init__(self, armature, side):
        self.armature, self.side = armature, side
        self.ob = bpy.data.objects["Arm_Male" + side]
        groups = {g.index: part_of(g.name) for g in self.ob.vertex_groups if g.name.endswith(side)}
        self.owner = []
        for v in self.ob.data.vertices:
            best, weight = None, 0.
            for g in v.groups:
                if g.group in groups and g.weight > weight:
                    best, weight = groups[g.group], g.weight
            self.owner.append(best)
        self.body_faces = {}
        for poly in self.ob.data.polygons:
            bodies = {body_of(self.owner[i]) for i in poly.vertices}
            if len(bodies) == 1 and None not in bodies:
                self.body_faces.setdefault(bodies.pop(), []).append(poly.index)
            # Основная фаланга пальца отдельным телом «Index:base»: в кулаке кончик пальца упирается в неё и в ладонь рядом.
            owners = {self.owner[i] for i in poly.vertices}
            if len(owners) == 1 and None not in owners:
                owner = next(iter(owners))
                if owner[1] == 1 and owner[0] in FOUR:
                    self.body_faces.setdefault(owner[0] + ":base", []).append(poly.index)

    def shot(self):
        """Кожа в текущей позе: точки в мире, деревья поиска по телам, оси костей для проверки «внутри»."""
        depsgraph = bpy.context.evaluated_depsgraph_get()
        ev = self.ob.evaluated_get(depsgraph)
        mesh = ev.to_mesh()
        world = self.ob.matrix_world
        self.co = [world @ v.co for v in mesh.vertices]
        polys = [tuple(p.vertices) for p in mesh.polygons]
        ev.to_mesh_clear()
        self.trees = {}
        for name, faces in self.body_faces.items():
            used = sorted({i for f in faces for i in polys[f]})
            remap = {old: new for new, old in enumerate(used)}
            self.trees[name] = BVHTree.FromPolygons([self.co[i] for i in used], [tuple(remap[i] for i in polys[f]) for f in faces])
        bones, mw = self.armature.pose.bones, self.armature.matrix_world
        self.segments = {}
        for name in FINGERS:
            numbers = ("02", "03") if name == "Thumb" else ("01", "02", "03")
            self.segments[name] = [(mw @ bones[name + n + self.side].head, mw @ bones[name + n + self.side].tail,
                                    (mw.to_3x3() @ bones[name + n + self.side].matrix.to_3x3())) for n in numbers]
        self.hand_back = (mw.to_3x3() @ bones["Hand" + self.side].matrix.to_3x3()).col[2].normalized()
        return self

    def is_inside(self, point, body):
        """Точка внутри тела, если два встречных луча поперёк его кости оба выходят изнутри наружу (попадают в изнанку кожи).
        Тела — открытые трубки и ладонь, поэтому по ближайшей точке кожи «внутри» надёжно не определить."""
        tree = self.trees[body]
        if body == "Palm":
            pairs, reach = [self.hand_back], .05
        elif body.endswith(":base"):
            axes = self.segments[body[:-5]][0][2]
            pairs, reach = [axes.col[0].normalized(), axes.col[2].normalized()], .03
        else:
            start, end, axes = min(self.segments[body], key=lambda s: ((point - s[0]).length + (point - s[1]).length))
            pairs, reach = [axes.col[0].normalized(), axes.col[2].normalized()], .03
        for direction in pairs:
            hits = 0
            for d in (direction, -direction):
                location, normal, _, _ = tree.ray_cast(point, d, reach)
                if location is not None and normal.dot(d) > 0:
                    hits += 1
            if hits == 2:
                return True
        return False

    def inside(self, part, body):
        """На сколько вершины фаланги part уходят внутрь тела body: (наибольшая глубина, мм; сколько вершин)."""
        tree = self.trees[body]
        worst, count = 0., 0
        for i, owner in enumerate(self.owner):
            if owner != part:
                continue
            location, _, _, distance = tree.find_nearest(self.co[i], .02)
            if location is None or not self.is_inside(self.co[i], body):
                continue
            worst, count = max(worst, distance * 1000), count + 1
        return worst, count

    def gap(self, parts, bodies):
        """Наименьшее расстояние от кожи фаланг parts до кожи тел bodies, мм (ноль, если кожа внутри)."""
        best = None
        for i, owner in enumerate(self.owner):
            if owner not in parts:
                continue
            for body in bodies:
                location, _, _, distance = self.trees[body].find_nearest(self.co[i], .06)
                if location is None:
                    continue
                d = 0. if distance < .02 and self.is_inside(self.co[i], body) else distance * 1000
                best = d if best is None else min(best, d)
        return best


def chains(armature, side):
    """Цепочки пальцев одной руки в виде, который берёт candidate_check.overlaps."""
    letter = side[-1]
    return {(letter, f.lower()): [armature.data.bones[f + n + side] for n in ("01", "02", "03")] for f in FINGERS}


def measure(armature, skin, name, quiet=False):
    """Замеры позы. Возвращает словарь чисел; печатает строки === LAB."""
    side = skin.side
    skin.shot()
    total, share, share_fingers, faces, top = candidate_check.overlaps(skin.ob, armature, chains(armature, side))
    result = {"share": share, "share_fingers": share_fingers, "pairs": total}
    # Глубина: фаланги против соседних пальцев целиком; основные фаланги между собой не меряются (у основания перепонка).
    depths = []
    for a in FOUR:
        for b in FOUR:
            if a == b:
                continue
            for phalanx in (2, 3):
                depth, count = skin.inside((a, phalanx), b)
                if count:
                    depths.append((depth, "%s%d в %s" % (a, phalanx, b), count))
    depths.sort(reverse=True)
    result["finger_depth"] = depths[0][0] if depths else 0.
    palm = {}
    for a in FOUR:
        # Кончик упирается в ладонь или в основание своего же пальца (у мизинца оно сложено вместе с краем ладони).
        # Средняя фаланга сюда не входит: её кожа у складки сустава всегда заходит за основную, это складка, а не упор.
        depth = max(skin.inside((a, 3), body)[0] for body in ("Palm", a + ":base"))
        gap = skin.gap({(a, 3)}, ("Palm", a + ":base"))
        palm[a] = (depth, gap if gap is not None else -1)
    result["palm"] = palm
    thumb_gap = skin.gap({("Thumb", 2), ("Thumb", 3)}, FOUR)
    thumb_in = max((skin.inside(("Thumb", p), b) for p in (2, 3) for b in FOUR + ("Palm",)), key=lambda r: r[0])
    fingers_in = max((skin.inside((b, p), "Thumb") for p in (1, 2, 3) for b in FOUR), key=lambda r: r[0])
    result.update(thumb_gap=thumb_gap if thumb_gap is not None else -1, thumb_in=thumb_in[0], fingers_in_thumb=fingers_in[0])
    if not quiet:
        print("=== LAB %-16s %s пересечения: %4.1f %% треугольников пальцев (без большого %4.1f %%), пар %d %s"
              % (name, side, share, share_fingers, total, top))
        print("=== LAB %-16s %s глубина палец в пальце: %s" % (name, side, ", ".join("%s %.1f мм (%d верш.)" % (w[1], w[0], w[2]) for w in depths[:6]) or "нет"))
        print("=== LAB %-16s %s ладонь: %s" % (name, side, "; ".join("%s в ладонь %.1f мм, зазор кончика %.1f мм" % (a, *palm[a]) for a in FOUR)))
        print("=== LAB %-16s %s большой палец: зазор до пальцев %.1f мм, входит в пальцы или ладонь на %.1f мм, пальцы в него на %.1f мм"
              % (name, side, result["thumb_gap"], result["thumb_in"], result["fingers_in_thumb"]))
        thumb = hand_anatomy.thumb_report(armature, side)
        print("=== LAB %-16s %s большой палец, суставы: основание отведено от покоя на %.0f° и развёрнуто на %.0f°; пястно-фаланговый сгиб %.0f° (вбок %.0f°), межфаланговый %.0f° (вбок %.0f°)"
              % (name, side, thumb["swing"], thumb["turn"], *thumb["Thumb02"], *thumb["Thumb03"]))
    return result


def stage():
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.samples = int(os.environ.get("LAB_SAMPLES", "32"))
    sc.cycles.use_denoising = True
    if os.environ.get("LAB_CPU") == "1":
        sc.cycles.device = "CPU"
        sc.cycles.denoiser = "OPENIMAGEDENOISE"
    else:
        prefs = bpy.context.preferences.addons["cycles"].preferences
        prefs.compute_device_type = "OPTIX"
        prefs.get_devices()
        for device in prefs.devices:
            device.use = device.type == "OPTIX"
        sc.cycles.device = "GPU"
        sc.cycles.denoiser = "OPTIX"
    size = int(os.environ.get("LAB_SIZE", "560"))
    sc.render.resolution_x = sc.render.resolution_y = size
    sc.render.resolution_percentage = 100
    sc.view_settings.view_transform = "AgX"
    world = bpy.data.worlds.new("lab")
    world.use_nodes = False
    world.color = (.2, .19, .18)
    sc.world = world
    for name, energy, angle in (("lab key", 3.2, (50, 0, 30)), ("lab fill", 1.1, (60, 0, -140))):
        light = bpy.data.objects.new(name, bpy.data.lights.new(name, "SUN"))
        light.data.energy = energy
        light.data.angle = math.radians(8)
        light.rotation_euler = tuple(math.radians(a) for a in angle)
        sc.collection.objects.link(light)
    for name in ("Hands_Female", "Sculpt"):
        bpy.data.collections[name].hide_render = True
    bpy.data.objects["Test_Card"].hide_render = True
    seat_view.game_skin()
    skin_override()
    camera = bpy.data.objects.new("lab", bpy.data.cameras.new("lab"))
    camera.data.lens = LENS
    camera.data.clip_start = .05
    sc.collection.objects.link(camera)
    sc.camera = camera
    # Свет от камеры: без него ладонь и нижняя сторона кулака уходят в тень, и пальцы не разглядеть.
    head = bpy.data.objects.new("lab head", bpy.data.lights.new("lab head", "SUN"))
    head.data.energy = 1.4
    head.data.angle = math.radians(20)
    head.parent = camera
    sc.collection.objects.link(head)
    return sc, camera


# Ровный тон кожи для сравнения без карты цвета: средний тон тыла кисти (skin_young.BASE, sRGB 226, 180, 158).
FLAT_TONE = (.76, .46, .34, 1.)


def skin_override():
    """Подмена слоёв кожи по LAB_SKIN: по очереди убирать цвет, рельеф, шероховатость и видеть, что даёт каждый."""
    spec = dict(part.split("=", 1) for part in os.environ.get("LAB_SKIN", "").split(";") if "=" in part)
    if not spec:
        return
    textures = os.path.join(source.ROOT, "art3d", "hands", "textures")

    def image(value, colour):
        path = value if os.path.isabs(value) else os.path.join(textures, value)
        picture = bpy.data.images.load(path, check_existing=True)
        picture.colorspace_settings.name = "sRGB" if colour else "Non-Color"
        return picture

    flat = None
    if spec.get("base") == "flat":
        flat = bpy.data.images.new("flat tone", 4, 4)
        flat.generated_color = FLAT_TONE
    for name in ("Arm_Male_L", "Arm_Male_R"):
        for material in bpy.data.objects[name].data.materials:
            nodes, links = material.node_tree.nodes, material.node_tree.links
            if "BaseMap" not in nodes:
                continue
            if "base" in spec:
                nodes["BaseMap"].image = flat if flat is not None else image(spec["base"], True)
            normal = next(n for n in nodes if n.type == "NORMAL_MAP")
            if spec.get("normal") == "flat":
                normal.inputs["Strength"].default_value = 0.
            elif "normal" in spec:
                nodes["NormalMap"].image = image(spec["normal"], False)
            if "strength" in spec:
                normal.inputs["Strength"].default_value = float(spec["strength"])
            principled = next(n for n in nodes if n.type == "BSDF_PRINCIPLED")
            if "rough" in spec:
                principled.inputs["Roughness"].default_value = float(spec["rough"])
            if "mask" in spec:
                # Как в шейдере игры: в складках (зелёный канал маски) кожа матовее.
                mask = nodes.new("ShaderNodeTexImage")
                mask.image = image(spec["mask"], False)
                split = nodes.new("ShaderNodeSeparateColor")
                links.new(mask.outputs["Color"], split.inputs["Color"])
                smoothness = 1 - principled.inputs["Roughness"].default_value
                ramp = nodes.new("ShaderNodeMapRange")
                ramp.inputs["From Min"].default_value, ramp.inputs["From Max"].default_value = 0., 1.
                ramp.inputs["To Min"].default_value = 1 - smoothness
                ramp.inputs["To Max"].default_value = 1 - smoothness * (1 - float(spec.get("crease", ".45")))
                links.new(split.outputs["Green"], ramp.inputs["Value"])
                links.new(ramp.outputs["Result"], principled.inputs["Roughness"])
    print("=== LAB SKIN", spec)


def frame_hand(armature, skin, side):
    """Центр кисти (по коже пальцев и ладони) и её оси: к большому пальцу, вдоль пальцев, к тылу."""
    skin.shot()
    points = [p for p, owner in zip(skin.co, skin.owner) if owner is not None]
    low = Vector((min(p.x for p in points), min(p.y for p in points), min(p.z for p in points)))
    high = Vector((max(p.x for p in points), max(p.y for p in points), max(p.z for p in points)))
    axes = (armature.matrix_world @ armature.pose.bones["Hand" + side].matrix).to_3x3()
    toward_thumb = axes @ Vector((1, 0, 0)) * (1 if side == "_L" else -1)
    return (low + high) / 2, toward_thumb, axes @ Vector((0, 1, 0)), axes @ Vector((0, 0, 1))


def shoot(sc, camera, center, axes, view, path):
    toward_thumb, along, back = axes
    (a, b, c), (ua, ub, uc) = VIEWS[view]
    direction = (toward_thumb * a + along * b + back * c).normalized()
    up = (toward_thumb * ua + along * ub + back * uc).normalized()
    eye = center + direction * DISTANCE
    forward = (center - eye).normalized()
    right = forward.cross(up).normalized()
    true_up = right.cross(forward)
    camera.matrix_world = Matrix((right.to_4d(), true_up.to_4d(), (-forward).to_4d(), Vector((0, 0, 0, 1)))).transposed()
    camera.matrix_world.translation = eye
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)


def only_hand(side):
    """В кадре одна рука: вторая и её рукав скрыты."""
    for other in ("_L", "_R"):
        for prefix in ("Arm_Male", "Sleeve"):
            bpy.data.objects[prefix + other].hide_render = other != side


def main():
    args = sys.argv[sys.argv.index("--") + 1:]
    command, names = args[0], args[1:] or list(gestures.POSES)
    armature = bpy.data.objects["Rig_Hands"]
    armature.animation_data_create()
    armature.animation_data.action = None
    armature.matrix_world.identity()
    if command == "rest":
        for side in ("_R", "_L"):
            rest = hand_anatomy.rest_pose(armature, side)
            print("=== REST %s пальцы (основание, средний, дальний, курс, наклон): %s" % (side, rest["fingers"]))
            print("=== REST %s большой палец: пястно-фаланговый сустав в точке %s" % (side, rest["thumb"]))
        return
    sides = ["_" + s for s in os.environ.get("LAB_SIDES", "R")]
    skins = {side: Skin(armature, side) for side in sides}
    if command == "frames":
        for clip in names:
            poses_ = gestures.CLIPS[clip]()
            worst = dict(share=0., finger_depth=0., thumb=0.)
            for side in sides:
                for frame, pose in enumerate(poses_):
                    reset(armature)
                    hand_anatomy.apply(armature, pose, side)
                    bpy.context.view_layer.update()
                    m = measure(armature, skins[side], "%s:%d" % (clip, frame), quiet=True)
                    thumb = max(m["thumb_in"], m["fingers_in_thumb"])
                    tips = max(v[0] for v in m["palm"].values())
                    print("=== FRAME %s %s %3d: пересечения %4.1f %% (без большого %4.1f %%), палец в пальце %4.1f мм, большой палец %4.1f мм, подушечки в ладонь %4.1f мм"
                          % (clip, side, frame, m["share"], m["share_fingers"], m["finger_depth"], thumb, tips))
                    worst = dict(share=max(worst["share"], m["share"]), finger_depth=max(worst["finger_depth"], m["finger_depth"]), thumb=max(worst["thumb"], thumb))
            print("=== FRAMES %s кадров %d, худший кадр: пересечения %.1f %%, палец в пальце %.1f мм, большой палец %.1f мм"
                  % (clip, len(poses_), worst["share"], worst["finger_depth"], worst["thumb"]))
        return
    views = [v for v in os.environ.get("LAB_VIEWS", ",".join(DEFAULT_VIEWS)).split(",") if v]
    sc = camera = None
    if command in ("render", "both"):
        sc, camera = stage()
    for name in names:
        put(armature, name)
        for side in sides:
            if command in ("measure", "both"):
                measure(armature, skins[side], name)
            if sc is not None:
                only_hand(side)
                center, *axes = frame_hand(armature, skins[side], side)
                folder = os.path.join(OUT, name.replace(":", "-").replace(">", "-"))
                os.makedirs(folder, exist_ok=True)
                for view in views:
                    shoot(sc, camera, center, axes, view, os.path.join(folder, "%s-%s.png" % (side[-1], view)))
        print("=== LAB DONE", name)


if __name__ == "__main__":
    main()
