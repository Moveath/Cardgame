"""Суставы кисти по фото или видео: MediaPipe Hand Landmarker (Google) находит 21 точку каждой руки в объёме, в метрах.
По точкам считаются углы пальцев в числах поз игры (scripts/3d/hands/poses.py): развод, основание, средний и дальний
сустав каждого пальца в градусах, пока ещё от прямого пальца (поправку на позу покоя модели делает retarget).
Для каждого файла пишутся <имя>.json (точки и углы) и <имя>-points.jpg (точки поверх кадра, для проверки глазами).
Видео разбирается покадрово: <имя>.json с точками каждого кадра.

Запуск окружением tools/mocap/.venv (Python 3.13, mediapipe, opencv-python):
    tools/mocap/.venv/Scripts/python.exe tools/mocap/hand_pose.py --out <папка> файл1.jpg файл2.mp4 ...
Пути с русскими буквами читаются через буфер: сами OpenCV и MediaPipe таких путей на Windows не понимают."""
import argparse
import json
import os
import shutil
import tempfile

import cv2
import mediapipe as mp
import numpy as np
from mediapipe.tasks import python as mp_tasks
from mediapipe.tasks.python import vision

MODEL = os.path.join(os.path.dirname(os.path.abspath(__file__)), "hand_landmarker.task")
FINGERS = {"Index": (5, 6, 7, 8), "Middle": (9, 10, 11, 12), "Ring": (13, 14, 15, 16), "Pinky": (17, 18, 19, 20)}
THUMB = (1, 2, 3, 4)
VIDEO = (".mp4", ".mov", ".avi", ".mkv", ".webm", ".3gp")
BONES = [(0, 1), (1, 2), (2, 3), (3, 4), (0, 5), (5, 6), (6, 7), (7, 8), (5, 9), (9, 10), (10, 11), (11, 12),
         (9, 13), (13, 14), (14, 15), (15, 16), (13, 17), (0, 17), (17, 18), (18, 19), (19, 20)]


def unit(v):
    n = np.linalg.norm(v)
    return v / n if n > 1e-9 else v


def angle(a, b):
    return float(np.degrees(np.arccos(np.clip(unit(a) @ unit(b), -1., 1.))))


def palm_frame(p, right):
    """Оси кисти по точкам: вдоль — от запястья к основанию среднего, к большому — от мизинца к указательному,
    тыл — их произведение. Знак тыла зависит от руки: у правой и левой ладонь зеркальна."""
    along = unit(p[9] - p[0])
    side = p[5] - p[17]
    side = unit(side - along * (side @ along))
    back = np.cross(along, side) * (1 if right else -1)
    return along, side, back


def finger_angles(p, right):
    """Углы пальцев в числах поз игры: (развод, основание, средний, дальний), градусы, сгиб к ладони положителен."""
    along, side, back = palm_frame(p, right)
    # Сгиб к ладони — поворот вдоль→ладонь, его ось along × (−back); развод к большому пальцу — поворот вдоль→side.
    flex_axis = unit(np.cross(along, -back))
    spread_axis = unit(np.cross(along, side))

    def signed(a, b, axis):
        return angle(a, b) * (1 if np.cross(a, b) @ axis >= 0 else -1)

    result = {}
    for name, (m, q, d, t) in FINGERS.items():
        meta, first, second, third = p[m] - p[0], p[q] - p[m], p[d] - p[q], p[t] - p[d]
        meta_flat = unit(meta - back * (meta @ back))
        flat = first - back * (first @ back)
        spread = signed(meta_flat, flat, spread_axis)
        base = float(np.degrees(np.arctan2(-(first @ back), np.linalg.norm(flat))))
        result[name] = [round(spread, 1), round(base, 1), round(signed(first, second, flex_axis), 1), round(signed(second, third, flex_axis), 1)]
    c, m, i, t = (p[k] for k in THUMB)
    result["Thumb"] = {"cmc_to_mcp": [round(x, 4) for x in unit(m - c) @ np.array([side, along, back]).T],
                       "mcp_to_ip": [round(x, 4) for x in unit(i - m) @ np.array([side, along, back]).T],
                       "ip_to_tip": [round(x, 4) for x in unit(t - i) @ np.array([side, along, back]).T],
                       "mcp": round(angle(m - c, i - m), 1), "ip": round(angle(i - m, t - i), 1)}
    return result


def read_image(path):
    data = np.fromfile(path, dtype=np.uint8)
    return cv2.imdecode(data, cv2.IMREAD_COLOR)


def write_image(path, image):
    ok, buffer = cv2.imencode(os.path.splitext(path)[1] or ".jpg", image)
    if ok:
        buffer.tofile(path)


def draw(image, hands):
    h, w = image.shape[:2]
    for hand in hands:
        points = [(int(x * w), int(y * h)) for x, y, _ in hand["image"]]
        color = (60, 220, 60) if hand["side"] == "Right" else (60, 160, 255)
        for a, b in BONES:
            cv2.line(image, points[a], points[b], color, max(2, w // 400))
        for k, point in enumerate(points):
            cv2.circle(image, point, max(3, w // 250), (255, 255, 255) if k in (4, 8, 12, 16, 20) else color, -1)
    return image


def hands_of(result):
    hands = []
    for image_points, world_points, side in zip(result.hand_landmarks, result.hand_world_landmarks, result.handedness):
        label = side[0].category_name
        world = np.array([[q.x, q.y, q.z] for q in world_points])
        hands.append({"side": label, "score": round(side[0].score, 3),
                      "image": [[round(q.x, 5), round(q.y, 5), round(q.z, 5)] for q in image_points],
                      "world": world.round(5).tolist(),
                      "angles": finger_angles(world, label == "Right")})
    return hands


def detector(mode):
    options = vision.HandLandmarkerOptions(base_options=mp_tasks.BaseOptions(model_asset_buffer=open(MODEL, "rb").read()),
                                           running_mode=mode, num_hands=2, min_hand_detection_confidence=.3,
                                           min_hand_presence_confidence=.3, min_tracking_confidence=.3)
    return vision.HandLandmarker.create_from_options(options)


def run_image(path, out):
    image = read_image(path)
    with detector(vision.RunningMode.IMAGE) as landmarker:
        result = landmarker.detect(mp.Image(image_format=mp.ImageFormat.SRGB, data=cv2.cvtColor(image, cv2.COLOR_BGR2RGB)))
    hands = hands_of(result)
    name = os.path.splitext(os.path.basename(path))[0]
    write_image(os.path.join(out, name + "-points.jpg"), draw(image, hands))
    with open(os.path.join(out, name + ".json"), "w", encoding="utf-8") as f:
        json.dump({"file": path, "hands": hands}, f, ensure_ascii=False, indent=1)
    return hands


def run_video(path, out, step):
    """Видео: копия во временную папку с латинским именем (OpenCV не открывает русские пути), кадры по порядку."""
    temp = os.path.join(tempfile.gettempdir(), "mocap_input" + os.path.splitext(path)[1])
    shutil.copyfile(path, temp)
    capture = cv2.VideoCapture(temp)
    fps = capture.get(cv2.CAP_PROP_FPS) or 30.
    frames, index = [], 0
    name = os.path.splitext(os.path.basename(path))[0]
    preview = None
    with detector(vision.RunningMode.VIDEO) as landmarker:
        while True:
            ok, image = capture.read()
            if not ok:
                break
            if index % step == 0:
                stamp = int(index * 1000 / fps)
                result = landmarker.detect_for_video(mp.Image(image_format=mp.ImageFormat.SRGB, data=cv2.cvtColor(image, cv2.COLOR_BGR2RGB)), stamp)
                hands = hands_of(result)
                frames.append({"frame": index, "time": round(index / fps, 4), "hands": hands})
                if hands and preview is None and index > fps:
                    preview = draw(image.copy(), hands)
            index += 1
    capture.release()
    os.remove(temp)
    if preview is not None:
        write_image(os.path.join(out, name + "-points.jpg"), preview)
    with open(os.path.join(out, name + ".json"), "w", encoding="utf-8") as f:
        json.dump({"file": path, "fps": fps, "step": step, "frames": frames}, f, ensure_ascii=False)
    found = sum(1 for f in frames if f["hands"])
    return f"{found} of {len(frames)} frames with a hand"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", required=True)
    parser.add_argument("--step", type=int, default=1, help="видео: брать каждый N-й кадр")
    parser.add_argument("files", nargs="+")
    args = parser.parse_args()
    os.makedirs(args.out, exist_ok=True)
    for path in args.files:
        if path.lower().endswith(VIDEO):
            print("VIDEO", os.path.basename(path), run_video(path, args.out, args.step))
            continue
        hands = run_image(path, args.out)
        for hand in hands:
            angles = hand["angles"]
            fingers = "  ".join(f"{k} {tuple(angles[k])}" for k in FINGERS)
            print(f"IMAGE {os.path.basename(path)} {hand['side']} {hand['score']}: {fingers}  thumb mcp {angles['Thumb']['mcp']} ip {angles['Thumb']['ip']}")
        if not hands:
            print(f"IMAGE {os.path.basename(path)}: no hand")


if __name__ == "__main__":
    main()
