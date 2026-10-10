"""Письма в почтовых ячейках зала таверны — заплатки того же зала (2026-10-10, идея автора).

Писем нет — ячейки пустые, как на картинке зала; есть непрочитанные — в ячейках несколько конвертов; много — ячейки
забиты. Кусок зала `screens/references/city/canvases/tavern-mail-rack-crop.png` (1140,70 420×280 из `UI/Hub/tavern-hall`,
увеличен ×3) → ChatGPT кладёт письма → `tavern-mail-rack-few.png` / `tavern-mail-rack-many.png` рядом (промпты —
`screens/references/city/tavern-mail-prompt.txt`).

Генератор перекадрировал кусок (1391×1131, крупнее и ниже), а в «много» ещё и дорисовал пятый ряд ячеек вместо нижней
полки. Поэтому ответ кладётся на зал по сетке: шесть столбцов и четыре верхних ряда ответа — каждый на свой столбец и ряд
зала (кусочно-линейно, границы — внутренние края рамки и середины перекладин, мерены по профилю «рыжести» дерева).
Из ответа берётся только то, что стало светлее (конверты, свитки, печати) и только внутри стойки: перекладины, мелочь
в ячейках и пятый ряд остаются от зала. → `Resources/UI/Hub/tavern-mail-rack-few.png`, `-many.png`; в игре ложатся
левым верхним углом в (TavernRackX, TavernRackY) = AREA[:2] (PlayerHubTavern.cs).

    python scripts/tavern/mail_rack_patch.py
"""
from pathlib import Path

import cv2
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
HALL = ROOT / 'screens/references/city/tavern-hall-v2-wide.png'  # чистый зал, без завсегдатаев (patrons_patch.py)
CANVASES = ROOT / 'screens/references/city/canvases'
OUT = ROOT / 'unity/CardGameClient/Assets/Resources/UI/Hub'
# Сетка ячеек: левый внутренний край рамки, середины пяти перекладин, правый внутренний край; верх — низ верхней рамки,
# середины трёх перекладин, середина нижней. Зал — в его пикселях, ответ — в пикселях ответа 1391×1131.
HALL_X = [1211, 1239.5, 1270.5, 1302, 1333.5, 1365.5, 1393.5]
HALL_Y = [188, 213.5, 242, 271, 303]
ANSWER_X = [260, 360, 470, 584, 704, 820, 926]
ANSWER_Y = [425, 516, 619, 722, 837]
AREA = (1206, 183, 193, 126)   # x, y, ширина, высота заплатки: стойка с рамкой
INSIDE = (1209, 186, 1396, 307)  # где письма могут быть: ячейки и перекладины между ними
BRIGHTER = (14, 36)            # насколько светлее (из 255) пиксель ответа: от — до полностью из ответа
CORE, GROW, SOFT = 20, 2, 2     # пятна не меньше CORE px; расширение и размытие маски, px


def read(path):
    return cv2.imdecode(np.fromfile(str(path), np.uint8), cv2.IMREAD_COLOR)


def axis_map(points, hall, answer):
    """Координата ответа для каждой координаты зала: кусочно-линейно, за краями — по крайнему отрезку."""
    points = np.asarray(points, np.float32)
    out = np.interp(points, hall, answer)
    low, high = points < hall[0], points > hall[-1]
    out[low] = answer[0] + (points[low] - hall[0]) * (answer[1] - answer[0]) / (hall[1] - hall[0])
    out[high] = answer[-1] + (points[high] - hall[-1]) * (answer[-1] - answer[-2]) / (hall[-1] - hall[-2])
    return out


def patch(edited_path, out_path):
    ax, ay, aw, ah = AREA
    original = read(HALL)[ay:ay + ah, ax:ax + aw].astype(np.float32)
    edited = read(edited_path)
    xs = axis_map(np.arange(ax, ax + aw) + .5, HALL_X, ANSWER_X) - .5
    ys = axis_map(np.arange(ay, ay + ah) + .5, HALL_Y, ANSWER_Y) - .5
    map_x, map_y = np.meshgrid(xs, ys)
    # Ответ крупнее зала в ~3,5 раза: сначала размыть, чтобы при выборке не было ряби.
    smooth = cv2.GaussianBlur(edited, (0, 0), 1.2)
    part = cv2.remap(smooth, map_x.astype(np.float32), map_y.astype(np.float32), cv2.INTER_LINEAR,
                     borderMode=cv2.BORDER_REFLECT).astype(np.float32)
    # Яркость — по дереву стойки (рыжее и в зале, и в ответе на тех же местах); одна на все каналы: в тёмном дереве
    # синего почти нет, и поканальная подгонка уводила кремовые конверты в сирень.
    wood = (original[..., 2] - original[..., 0] > 45) & (part[..., 2] - part[..., 0] > 45)
    gain = float(np.clip(np.median(original[wood].mean(1)) / max(1.0, np.median(part[wood].mean(1))), .8, 1.25))
    part = np.clip(part * gain, 0, 255)
    brighter = np.clip(part - original, 0, None).max(axis=2)
    brighter = cv2.GaussianBlur(brighter, (3, 3), 0)
    count, labels, stats, _ = cv2.connectedComponentsWithStats((brighter >= BRIGHTER[1]).astype(np.uint8))
    core = np.isin(labels, [i for i in range(1, count) if stats[i, cv2.CC_STAT_AREA] >= CORE]).astype(np.uint8)
    near = cv2.dilate(core, np.ones((7, 7), np.uint8)).astype(np.float32)
    alpha = np.clip((brighter - BRIGHTER[0]) / (BRIGHTER[1] - BRIGHTER[0]), 0, 1) * near
    alpha = cv2.dilate(alpha, np.ones((2 * GROW + 1, 2 * GROW + 1), np.uint8))
    alpha = cv2.GaussianBlur(alpha, (2 * SOFT + 1, 2 * SOFT + 1), 0)
    inside = np.zeros_like(alpha)
    x0, y0, x1, y1 = INSIDE
    inside[y0 - ay:y1 - ay, x0 - ax:x1 - ax] = 1
    alpha *= cv2.GaussianBlur(inside, (5, 5), 0)
    rgba = np.dstack([part, alpha * 255]).round().astype(np.uint8)
    cv2.imencode('.png', rgba)[1].tofile(str(out_path))
    print(edited_path.name, '-> colour gain', round(gain, 3), '; letters share', round(float(alpha.mean()), 3), '->', out_path.name)


def main():
    for kind in ('few', 'many'):
        answer = CANVASES / ('tavern-mail-rack-' + kind + '.png')
        if answer.exists(): patch(answer, OUT / ('tavern-mail-rack-' + kind + '.png'))
        else: print('нет ответа:', answer.name)


if __name__ == '__main__':
    main()
