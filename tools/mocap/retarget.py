"""Перенос позы пальцев с точек MediaPipe (hand_pose.py) на руку игры: числа поз как в scripts/3d/hands/poses.py.

Для каждого пальца подбираются развод, основание, средний и дальний сустав (для большого — повороты основания и два
сгиба) так, чтобы фаланги нашей руки смотрели туда же, куда на кадре, в осях кисти. Каждый угол держится в пределах
живого сустава (fit_poses.LIMITS), средний и дальний суставы гнутся только вокруг своей оси: даже кривые точки не дают
вывернутый палец. Кости считаются по скелету в покое (rig_rest.json из scripts/3d/hands/export_rig.py) теми же
поворотами, что poses.apply_pose.

Правая и левая рука в числах поз симметричны: снятую правую руку можно поставить на левую как есть (отражение).
Вход — точки с фото и видео (hand_pose.py) или библиотека готовых поз ({"poses": {имя: {"side", "world"}}},
её пишет scripts/3d/hands/library_points.py): один и тот же перенос для живой руки и для готовых поз.
Запуск: tools/mocap/.venv/Scripts/python.exe tools/mocap/retarget.py <точки.json> [...] [--out поза.json]"""
import argparse
import json
import math
import os

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
FINGERS = {"Index": (5, 6, 7, 8), "Middle": (9, 10, 11, 12), "Ring": (13, 14, 15, 16), "Pinky": (17, 18, 19, 20)}
THUMB = (1, 2, 3, 4)
# Пределы живой руки, градусы (как fit_poses.LIMITS): развод, основание, средний сустав, дальний сустав.
FINGER_LIMITS = ((-20, 20), (-15, 92), (-5, 108), (-5, 75))
THUMB_LIMITS = ((-45, 45), (-45, 45), (-45, 45), (-15, 75), (-15, 75))
# Вес фаланг в подборе: основная важнее, кончик меньше — у него точки шумнее всего.
SEGMENT_WEIGHT = (1., 1., .7)
STEPS = (16, 8, 4, 2, 1, .5)


def unit(v):
    n = np.linalg.norm(v)
    return v / n if n > 1e-12 else v


def euler(x, y, z):
    """Матрица поворота Blender для Euler XYZ (градусы): сначала X, потом Y, потом Z."""
    x, y, z = (math.radians(a) for a in (x, y, z))
    cx, sx, cy, sy, cz, sz = math.cos(x), math.sin(x), math.cos(y), math.sin(y), math.cos(z), math.sin(z)
    rx = np.array([[1, 0, 0], [0, cx, -sx], [0, sx, cx]])
    ry = np.array([[cy, 0, sy], [0, 1, 0], [-sy, 0, cy]])
    rz = np.array([[cz, -sz, 0], [sz, cz, 0], [0, 0, 1]])
    m = np.eye(4)
    m[:3, :3] = rz @ ry @ rx
    return m


class Rig:
    def __init__(self, path=os.path.join(HERE, "rig_rest.json")):
        with open(path, encoding="utf-8") as f:
            self.bones = {name: {"parent": b["parent"], "rest": np.array(b["matrix"]), "head": np.array(b["head"])}
                          for name, b in json.load(f)["bones"].items()}

    def frame(self, suffix):
        """Оси кисти по суставам, как у MediaPipe: вдоль — запястье→основание среднего, к большому — мизинец→указательный."""
        b = self.bones
        wrist, index, middle, pinky = (b[n + suffix]["head"] for n in ("Hand", "Index01", "Middle01", "Pinky01"))
        along = unit(middle - wrist)
        side = index - pinky
        side = unit(side - along * (side @ along))
        back = np.cross(along, side) * (1 if suffix == "_R" else -1)
        return along, side, back

    def pose(self, suffix, rotations):
        """Матрицы костей в позе: rotations — {кость: матрица поворота 4×4 в её осях}; кисть стоит в покое."""
        done = {}

        def matrix(name):
            if name in done:
                return done[name]
            bone = self.bones[name]
            local = rotations.get(name, np.eye(4))
            if bone["parent"] is None or bone["parent"] not in self.bones:
                result = bone["rest"] @ local
            else:
                parent = self.bones[bone["parent"]]
                result = matrix(bone["parent"]) @ np.linalg.inv(parent["rest"]) @ bone["rest"] @ local
            done[name] = result
            return result

        return matrix

    def finger_dirs(self, suffix, name, values, pinky_base):
        side = 1 if suffix == "_L" else -1
        spread, base, mid, far = values
        cup = max(0., (pinky_base - 30) / 60)
        rotations = {name + "01" + suffix: euler(-base, 0, -spread * side), name + "02" + suffix: euler(-mid, 0, 0),
                     name + "03" + suffix: euler(-far, 0, 0),
                     "MetaPinky" + suffix: euler(-12 * cup, 0, 0), "MetaRing" + suffix: euler(-6 * cup, 0, 0)}
        matrix = self.pose(suffix, rotations)
        return [unit(matrix(name + k + suffix)[:3, 1]) for k in ("01", "02", "03")]

    def thumb_dirs(self, suffix, values):
        side = 1 if suffix == "_L" else -1
        rx, ry, rz, second, third = values
        rotations = {"Thumb01" + suffix: euler(rx, ry * side, rz * side), "Thumb02" + suffix: euler(-second, 0, 0),
                     "Thumb03" + suffix: euler(-third, 0, 0)}
        matrix = self.pose(suffix, rotations)
        return [unit(matrix("Thumb" + k + suffix)[:3, 1]) for k in ("01", "02", "03")]


def hand_frame(points, right):
    along = unit(points[9] - points[0])
    side = points[5] - points[17]
    side = unit(side - along * (side @ along))
    back = np.cross(along, side) * (1 if right else -1)
    return along, side, back


def targets(points, right, rig_frame):
    """Направления фаланг с кадра, перенесённые в оси кисти нашей руки."""
    along, side, back = hand_frame(points, right)
    r_along, r_side, r_back = rig_frame

    def carry(v):
        v = unit(v)
        return unit(r_side * (v @ side) + r_along * (v @ along) + r_back * (v @ back))

    result = {name: [carry(points[b] - points[a]) for a, b in zip(chain, chain[1:])] for name, chain in FINGERS.items()}
    result["Thumb"] = [carry(points[b] - points[a]) for a, b in zip(THUMB, THUMB[1:])]
    return result


def descend(score, start, limits):
    """Покоординатный спуск с убывающим шагом в пределах суставов."""
    best = list(start)
    best_score = score(best)
    for step in STEPS:
        improved = True
        while improved:
            improved = False
            for k in range(len(best)):
                for sign in (1, -1):
                    trial = list(best)
                    trial[k] = min(limits[k][1], max(limits[k][0], trial[k] + sign * step))
                    if trial[k] == best[k]:
                        continue
                    value = score(trial)
                    if value < best_score - 1e-9:
                        best, best_score, improved = trial, value, True
    return best, best_score


def error(dirs, wanted):
    return sum(w * (1 - float(np.clip(d @ t, -1, 1))) for w, d, t in zip(SEGMENT_WEIGHT, dirs, wanted))


# Привычки живого пальца, которые точки с одного ракурса не видят: дальний сустав сгибается примерно на две трети
# среднего (их ведёт одно сухожилие), согнутый в основании палец вбок почти не отводится (держат боковые связки).
COUPLING, SPREAD_WHEN_BENT = .08, .06


def natural(values):
    spread, base, mid, far = values
    return COUPLING * ((far - .67 * mid) / 90) ** 2 + SPREAD_WHEN_BENT * (spread / 20) ** 2 * max(0., base) / 90


def degrees(dirs, wanted):
    return [round(math.degrees(math.acos(float(np.clip(d @ t, -1, 1)))), 1) for d, t in zip(dirs, wanted)]


def retarget(rig, points, right):
    suffix = "_R" if right else "_L"
    wanted = targets(np.array(points), right, rig.frame(suffix))
    result, misses = {}, {}
    starts = ((0, 0, 0, 0), (0, 45, 50, 25), (0, 85, 100, 55))
    pinky_base = 0.
    for name in ("Pinky", "Ring", "Middle", "Index"):
        fits = [descend(lambda v: error(rig.finger_dirs(suffix, name, v, v[1] if name == "Pinky" else pinky_base), wanted[name]) + natural(v),
                        s, FINGER_LIMITS) for s in starts]
        values, _ = min(fits, key=lambda f: f[1])
        if name == "Pinky":
            pinky_base = values[1]
        result[name] = tuple(round(v, 1) for v in values)
        misses[name] = degrees(rig.finger_dirs(suffix, name, values, pinky_base), wanted[name])
    thumb_starts = ((0, 0, 0, 0, 0), (-30, 10, -30, 20, 20), (12, 12, -50, 0, 0), (-40, 30, -10, 30, 30))
    fits = [descend(lambda v: error(rig.thumb_dirs(suffix, v), wanted["Thumb"]), s, THUMB_LIMITS) for s in thumb_starts]
    values, _ = min(fits, key=lambda f: f[1])
    result["Thumb"] = (tuple(round(v, 1) for v in values[:3]), round(values[3], 1), round(values[4], 1))
    misses["Thumb"] = degrees(rig.thumb_dirs(suffix, values), wanted["Thumb"])
    return result, misses


def pose_text(result):
    fingers = ", ".join(f'"{n}": {tuple(result[n])}' for n in ("Index", "Middle", "Ring", "Pinky"))
    return '{"fingers": {%s}, "thumb": %s, "wrist": (0, 0)}' % (fingers, result["Thumb"])


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("files", nargs="+")
    parser.add_argument("--out")
    parser.add_argument("--hand", choices=("Right", "Left"), help="какая рука на кадре, если подпись MediaPipe неверна")
    args = parser.parse_args()
    rig = Rig()
    found = {}
    for path in args.files:
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
        name = os.path.splitext(os.path.basename(path))[0]
        if "poses" in data:
            entries = [(pose, None, [hand]) for pose, hand in data["poses"].items()]
        elif "hands" in data:
            entries = [(name, None, data["hands"])]
        else:
            entries = [(name, fr["frame"], fr["hands"]) for fr in data["frames"]]
        for name, frame, hands in entries:
            for hand in hands:
                right = (args.hand or hand["side"]) == "Right"
                result, misses = retarget(rig, hand["world"], right)
                found.setdefault(name, []).append({"frame": frame, "hand": "Right" if right else "Left", "pose": result, "miss_deg": misses})
                if frame is None:
                    worst = max(max(m) for m in misses.values())
                    print(f"POSE {name} {'Right' if right else 'Left'} (worst miss {worst:.0f} deg): {pose_text(result)}")
    if args.out:
        with open(args.out, "w", encoding="utf-8") as f:
            json.dump(found, f, ensure_ascii=False, indent=1)


if __name__ == "__main__":
    main()
