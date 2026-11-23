"""Сглаженные сгибы, запечённые в обычные веса кожи: пальцы гнутся мягко, как живые, а не ломаются углом.

Модификатор Corrective Smooth (дельта-маш) в Blender убирает излом кожи на суставе и держит объём, но в Unity его нет.
Поэтому, как Bake Deformer в Maya, здесь берётся набор поз (каждый сустав по отдельности, их сочетания и позы игры), для
каждой поза считается сглаженная форма, и для каждой вершины подбираются веса костей, при которых обычный скиннинг
(до четырёх костей, как в Unity) повторяет её с наименьшей ошибкой. Кандидаты — нынешние кости вершины и их соседи по
цепочке пальца, веса неотрицательны и в сумме дают единицу. В игре ничего не добавляется: ни скриптов, ни форм.

Веса подбираются на мужской сетке каждой руки и переносятся на женскую (сетки совпадают по номерам вершин, как в build.py).
Предплечье и рукав не трогаются: подбираются только вершины, на которые влияют кисть, пальцы и суставы.
Запуск: blender art3d/hands/CardCity_Hands.blend --background --disable-autoexec --python scripts/3d/hands/bake_smooth_weights.py
DRY=1 — только посчитать и напечатать ошибки, файл не сохранять."""
import math
import os
import sys

import bpy
import numpy as np

sys.path.insert(0, os.path.dirname(__file__))
import animations
import in_game
import joints as J
import poses

FACTOR, ITERATIONS = .5, 20          # Corrective Smooth: сила и число проходов сглаживания.
MAX_INFLUENCES = 4
SUM_WEIGHT = 30.                     # Насколько строго сумма весов равна единице.
KEEP = 2e-4                          # Тяга к нынешним весам (м на единицу веса): где сглаживать нечего, веса не меняются.
HAND_BONES = ("Hand", "Meta", "Joint", "Thumb", "Index", "Middle", "Ring", "Pinky")


def zero_pose():
    return {"fingers": {f: (0, 0, 0, 0) for f in J.FINGERS}, "thumb": ((0, 0, 0), 0, 0), "wrist": (0, 0)}


def training_poses():
    """Позы для подбора: каждый сустав отдельно (на полпути и до упора), сочетания, большой палец, запястье, позы игры."""
    result = [zero_pose()]
    single = [(0, 45, 0, 0), (0, 90, 0, 0), (0, -15, 0, 0), (0, 0, 50, 0), (0, 0, 100, 0), (0, 0, 0, 40), (0, 0, 0, 75),
              (0, 45, 60, 30), (0, 85, 100, 55), (15, 30, 20, 10), (-15, 30, 20, 10), (0, 20, 90, 60)]
    for finger in J.FINGERS:
        for values in single:
            pose = zero_pose()
            pose["fingers"][finger] = values
            result.append(pose)
    for root, second, third in (((-40, 30, -10), 30, 30), ((30, 0, 0), 0, 0), ((0, 30, 0), 0, 0), ((0, 0, 30), 0, 0),
                                ((-30, 0, -30), 40, 40), ((0, 0, 0), 60, 0), ((0, 0, 0), 0, 70), ((12, 12, -50), 0, 0),
                                ((-38, 12, -36), 22, 24)):
        pose = zero_pose()
        pose["thumb"] = (root, second, third)
        result.append(pose)
    for wrist in ((40, 0), (-40, 0), (0, 20), (0, -20)):
        pose = zero_pose()
        pose["wrist"] = wrist
        result.append(pose)
    for pose in (poses.POSES[n] for n in ("Fist", "Point", "TakeCard", "PlayCard", "HoldCard", "TapTable", "Relaxed")):
        result.append(pose)
    result.append(animations.hand(animations.FIST, animations.STRAIGHT_THUMB))
    result.append(animations.hand(animations.TUCKED_FIST, animations.TUCKED_THUMB))
    result.append(in_game.LEFT["pose"])
    result.append(in_game.RIGHT["pose"])
    curled = zero_pose()
    curled["fingers"] = {f: (0, 85, 100, 55) for f in J.FINGERS}
    result.append(curled)
    return result


def reset(armature):
    for bone in armature.pose.bones:
        bone.rotation_mode = "XYZ"
        bone.rotation_euler = (0, 0, 0)
        bone.location = (0, 0, 0)


def evaluated(ob):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    mesh = ob.evaluated_get(depsgraph).to_mesh()
    co = np.zeros(len(mesh.vertices) * 3)
    mesh.vertices.foreach_get("co", co)
    ob.evaluated_get(depsgraph).to_mesh_clear()
    return co.reshape(-1, 3)


def read_weights(ob):
    names = [g.name for g in ob.vertex_groups]
    table = []
    for v in ob.data.vertices:
        table.append({names[g.group]: g.weight for g in v.groups if g.weight > 1e-5})
    return table


def deform_graph(armature, suffix):
    """Соседи каждой деформирующей кости одной руки: родитель и дети среди деформирующих (через недеформирующие насквозь)."""
    bones = armature.data.bones
    deform = {b.name for b in bones if b.use_deform and b.name.endswith(suffix)}
    near = {name: set() for name in deform}

    def deforming_parent(bone):
        parent = bone.parent
        while parent is not None and parent.name not in deform:
            parent = parent.parent
        return parent

    for name in deform:
        parent = deforming_parent(bones[name])
        if parent is not None:
            near[name].add(parent.name)
            near[parent.name].add(name)
    return near


def solve(columns, target, current):
    """Неотрицательные веса с суммой 1: МНК с отбрасыванием отрицательных, плюс тяга к нынешним весам."""
    active = list(range(columns.shape[1]))
    while True:
        a = columns[:, active]
        rows = [a, SUM_WEIGHT * np.ones((1, len(active))), KEEP * np.eye(len(active))]
        rhs = [target, [SUM_WEIGHT], KEEP * current[active]]
        w, *_ = np.linalg.lstsq(np.vstack(rows), np.concatenate([np.ravel(r) for r in rhs]), rcond=None)
        if (w >= 0).all() or len(active) == 1:
            result = np.zeros(columns.shape[1])
            result[active] = np.clip(w, 0, None)
            return result / max(result.sum(), 1e-9)
        active.pop(int(np.argmin(w)))


def bake(armature, side):
    ob = bpy.data.objects["Arm_Male" + side]
    to_armature = np.array(armature.matrix_world.inverted() @ ob.matrix_world)
    from_armature = np.linalg.inv(to_armature)
    rest = np.zeros(len(ob.data.vertices) * 3)
    ob.data.vertices.foreach_get("co", rest)
    rest = rest.reshape(-1, 3)
    rest_h = np.c_[rest, np.ones(len(rest))] @ to_armature.T
    weights = read_weights(ob)
    near = deform_graph(armature, side)
    smooth = ob.modifiers.new("BakeSmooth", "CORRECTIVE_SMOOTH")
    smooth.factor, smooth.iterations, smooth.smooth_type = FACTOR, ITERATIONS, "LENGTH_WEIGHTED"
    smooth.rest_source, smooth.use_pin_boundary = "ORCO", True

    train = training_poses()
    targets, matrices = [], []
    for pose in train:
        reset(armature)
        poses.apply_pose(armature, pose, side)
        bpy.context.view_layer.update()
        targets.append(evaluated(ob))
        matrices.append({b.name: np.array(b.matrix @ b.bone.matrix_local.inverted()) for b in armature.pose.bones if b.name in near})
    smooth.show_viewport = False
    plain = []
    for k, pose in enumerate(train):
        reset(armature)
        poses.apply_pose(armature, pose, side)
        bpy.context.view_layer.update()
        plain.append(evaluated(ob))
    ob.modifiers.remove(smooth)
    reset(armature)
    bpy.context.view_layer.update()

    # Куда каждая кость уносит каждую вершину в каждой позе, в осях объекта сетки: (позы, вершины, 3).
    moved = {name: np.stack([(rest_h @ (from_armature @ matrices[p][name]).T)[:, :3] for p in range(len(train))]).astype(np.float32)
             for name in near}
    targets = np.stack(targets).astype(np.float32)
    plain = np.stack(plain)
    # Проверка: обычный скиннинг по нынешним весам из этих матриц совпадает с тем, что считает Blender.
    check = np.zeros_like(plain)
    for v, current in enumerate(weights):
        total = sum(w for n, w in current.items() if n in near)
        for name, w in current.items():
            if name in near:
                check[:, v] += moved[name][:, v] * (w / max(total, 1e-9))
    print(f"CHECK {side}: own skinning vs Blender max {np.abs(check - plain).max() * 1000:.3f} mm")

    new_weights, changed = [], 0
    before, after, worst_before, worst_after = [], [], 0., 0.
    for v, current in enumerate(weights):
        if not current or not any(name.startswith(HAND_BONES) and name.endswith(side) for name in current if name in near):
            new_weights.append(current)
            continue
        cands = set(n for n in current if n in near)
        for name in list(cands):
            cands |= {m for m in near[name] if not m.startswith("Forearm")}
        cands = sorted(cands)
        if len(cands) == 1:
            new_weights.append({cands[0]: 1.})
            continue
        # Колонка кости — куда её матрица уносит вершину во всех позах подряд.
        columns = np.stack([moved[name][:, v].reshape(-1) for name in cands], axis=1).astype(np.float64)
        target = targets[:, v].reshape(-1).astype(np.float64)
        cur = np.array([current.get(n, 0.) for n in cands])
        w = solve(columns, target, cur)
        if (w > 1e-4).sum() > MAX_INFLUENCES:
            keep = np.argsort(-w)[:MAX_INFLUENCES]
            w2 = solve(columns[:, keep], target, cur[keep])
            w = np.zeros(len(cands))
            w[keep] = w2
        err_new = np.linalg.norm((columns @ w - target).reshape(-1, 3), axis=1)
        err_old = np.linalg.norm(plain[:, v] - targets[:, v], axis=1)
        before.append(err_old.mean())
        after.append(err_new.mean())
        worst_before, worst_after = max(worst_before, err_old.max()), max(worst_after, err_new.max())
        result = {cands[i]: float(w[i]) for i in range(len(cands)) if w[i] > 1e-4}
        if result != current:
            changed += 1
        new_weights.append(result)
    print(f"BAKE {side}: {len(train)} poses, {changed} vertices changed; distance to smoothed shape mean "
          f"{np.mean(before) * 1000:.2f} -> {np.mean(after) * 1000:.2f} mm, worst {worst_before * 1000:.1f} -> {worst_after * 1000:.1f} mm")
    return new_weights


def write(ob, table):
    for v, row in zip(ob.data.vertices, table):
        for g in list(v.groups):
            ob.vertex_groups[g.group].remove([v.index])
        for name, weight in row.items():
            group = ob.vertex_groups.get(name) or ob.vertex_groups.new(name=name)
            group.add([v.index], weight, "REPLACE")


def main():
    armature = bpy.data.objects["Rig_Hands"]
    armature.animation_data.action = None
    for side in ("_L", "_R"):
        table = bake(armature, side)
        if os.environ.get("DRY") == "1":
            continue
        for gender in ("Male", "Female"):
            write(bpy.data.objects["Arm_%s%s" % (gender, side)], table)
    if os.environ.get("DRY") != "1":
        bpy.ops.wm.save_mainfile()
        print("=== SMOOTH WEIGHTS SAVED")
    else:
        print("=== DRY RUN")


main()
