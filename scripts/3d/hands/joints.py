"""Суставы кисти берутся из разметки базы: у каждого пальца свои наборы граней на фаланги, их границы лежат на сгибах."""
import numpy as np

FINGERS = ("Index", "Middle", "Ring", "Pinky")
HAND_SET, FOREARM_SET, UPPER_ARM_SET = 9, 12, 21
# Проксимальная, средняя, дистальная фаланга и середина ногтя.
FINGER_SETS = {"Pinky": (64, 65, 66, 67), "Ring": (68, 69, 70, 71), "Middle": (72, 73, 74, 75), "Index": (76, 77, 78, 79)}
THUMB_SETS = (80, 81, 82, 83)   # Основание, проксимальная, дистальная фаланга, середина ногтя.

MCP_DEPTH = .024                # Сустав основания пальца лежит глубже границы набора, в ладони.
META_BASE_SHARE = .16           # Где пястная кость начинается на отрезке от запястья к основанию пальца.
THUMB_CMC_OFFSET = (.032, .017, -.012)   # Основание большого пальца от запястья: к большому пальцу, к пальцам, к ладони.


def read_mesh(mesh):
    """Координаты вершин и наборы вершин по разметке граней."""
    co = np.zeros(len(mesh.vertices) * 3)
    mesh.vertices.foreach_get("co", co)
    sets = np.zeros(len(mesh.polygons), int)
    mesh.attributes[".sculpt_face_set"].data.foreach_get("value", sets)
    verts = {}
    for poly, value in zip(mesh.polygons, sets):
        verts.setdefault(int(value), set()).update(poly.vertices)
    return co.reshape(-1, 3), sets, verts


def ring(verts, a, b):
    """Середина кольца на границе двух наборов."""
    shared = list(verts[a] & verts[b])
    return shared


def farthest(co, indices, origin):
    indices = list(indices)
    distance = np.linalg.norm(co[indices] - origin, axis=1)
    return co[indices[int(np.argmax(distance))]]


def analyse(co, verts, wrist):
    """Суставы левой руки перед игроком: кости идут вдоль +Y, тыл кисти смотрит в +Z, большой палец в +X."""
    fingers = {}
    for name in FINGERS:
        first, second, third, nail = FINGER_SETS[name]
        base = co[ring(verts, HAND_SET, first)].mean(axis=0)
        pip = co[ring(verts, first, second)].mean(axis=0)
        dip = co[ring(verts, second, third)].mean(axis=0)
        tip = farthest(co, verts[third] | verts[nail], dip)
        along = (base - pip) / np.linalg.norm(base - pip)
        mcp = base + along * MCP_DEPTH
        fingers[name] = {"base": wrist + (mcp - wrist) * META_BASE_SHARE, "mcp": mcp, "pip": pip, "dip": dip, "tip": tip,
                         "nail": nail_dorsal(co, verts, nail, dip, tip, np.array((0., 0., 1.)))}
    first, second, third, nail = THUMB_SETS
    root = co[ring(verts, HAND_SET, first)].mean(axis=0)
    mcp = co[ring(verts, first, second)].mean(axis=0)
    ip = co[ring(verts, second, third)].mean(axis=0)
    tip = farthest(co, verts[third] | verts[nail], ip)
    thumb = {"cmc": wrist + np.array(THUMB_CMC_OFFSET), "root": root, "mcp": mcp, "ip": ip, "tip": tip}
    return {"fingers": fingers, "thumb": thumb}


def nail_normal(co, verts, nail_set):
    """Куда смотрит ноготь: нормаль плоскости его вершин, наружу от кончика пальца."""
    points = co[list(verts[nail_set])]
    centre = points.mean(axis=0)
    _, _, axes = np.linalg.svd(points - centre)
    return centre, axes[-1]


def nail_dorsal(co, verts, nail_set, dip, tip, back):
    """Тыл дальней фаланги по ногтю: среднее двух независимых оценок — нормали ногтя и того, куда ноготь смещён от оси
    фаланги (он всегда на тыле). Они сходятся в пределах 2–6°. back — примерно куда смотрит тыл, чтобы выбрать знак."""
    centre, normal = nail_normal(co, verts, nail_set)
    along = (tip - dip) / np.linalg.norm(tip - dip)
    offset = centre - dip
    offset = offset - along * (offset @ along)
    normal = normal if normal @ back > 0 else -normal
    offset = offset if offset @ back > 0 else -offset
    dorsal = normal / np.linalg.norm(normal) + offset / np.linalg.norm(offset)
    dorsal = dorsal - along * (dorsal @ along)
    return dorsal / np.linalg.norm(dorsal)
