"""Нарезка листов стола дурака (ChatGPT, 2026-11-05) в UI/Hub/Durak и UI/Hub/Hud.
Исходники на пурпурном фоне: python scripts/durak/cut_durak.py <стол> <стопки> <значки> кладёт их без фона в
screens/references/ui/hud-durak-*-sheet.png; без аргументов режет уже лежащие там. Что откуда — README.md там же.
Из стопок берётся только нижний ряд, колоды: рубашка у них меняется скином поверх картинки (DurakCardQuad), бита
собирается из настоящих карт, поэтому верхний ряд в игру не идёт."""
import sys
from pathlib import Path
import cv2
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
REF = ROOT / "screens/references/ui"
HUB = ROOT / "unity/CardGameClient/Assets/Resources/UI/Hub"
SHEETS = ("table", "piles", "icons")

# Отдельные детали листа по порядку: ряды сверху вниз, в ряду слева направо; наибольшая сторона на выходе.
ICONS = (("Durak/trump-spades", 200), ("Durak/trump-hearts", 200), ("Durak/trump-diamonds", 200), ("Durak/trump-clubs", 200),
         ("Durak/role-attack", 128), ("Durak/role-defend", 128), ("Hud/icon-chat", 128))
DECKS = (("Durak/deck-1", 320), ("Durak/deck-2", 340), ("Durak/deck-3", 360))
# Углы верхней карты, доли от левого нижнего угла: те же числа — ClubDeckTop в DurakClubDetails.cs.
DECK_TOPS = (((.025, .309), (.382, .981), (.978, .895), (.755, .207)),
             ((.026, .378), (.311, .983), (.977, .901), (.774, .297)),
             ((.025, .445), (.299, .984), (.979, .907), (.75, .363)))


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def key(path):
    """Тот же ключ, что в scripts/govno-figures/slice_figures.py: пурпур — min(R, B) − G, плюс очистка каймы."""
    c = np.asarray(Image.open(path).convert("RGB")).astype(np.float64) / 255
    mag = np.clip(np.minimum(c[..., 0], c[..., 2]) - c[..., 1], 0, 1)
    alpha = 1 - smoothstep(.05, .78, mag)
    clean = np.clip((c - np.stack([mag, np.zeros_like(mag), mag], -1)) / np.maximum(1 - mag, 1e-3)[..., None], 0, 1)
    c = c + (clean - c) * smoothstep(.01, .1, mag)[..., None]
    return np.dstack([c * 255, alpha * 255]).round().clip(0, 255).astype(np.uint8)


def parts(sheet):
    """Детали листа: связные области непрозрачного, ряд за рядом, слева направо."""
    mask = cv2.dilate((sheet[..., 3] > 40).astype(np.uint8), np.ones((9, 9), np.uint8))
    n, labels, stats, _ = cv2.connectedComponentsWithStats(mask)
    boxes = [tuple(stats[i][:4]) + (i,) for i in range(1, n) if stats[i][4] > 2000]
    rows = {}
    for box in sorted(boxes, key=lambda b: b[1]):
        row = next((r for r in rows if abs(r - (box[1] + box[3] / 2)) < sheet.shape[0] / 4), None)
        rows.setdefault(box[1] + box[3] / 2 if row is None else row, []).append(box)
    return [[b for b in sorted(rows[r])] for r in sorted(rows)], labels


def cut(sheet, labels, box):
    x, y, w, h, label = box
    a = sheet[y:y + h, x:x + w].copy()
    a[labels[y:y + h, x:x + w] != label] = 0
    return a


def save(a, name, side):
    im = Image.fromarray(a)
    k = side / max(im.size)
    if k < 1: im = im.resize((round(im.width * k), round(im.height * k)), Image.LANCZOS)
    pad = Image.new("RGBA", (im.width + 4, im.height + 4), (0, 0, 0, 0))
    pad.paste(im, (2, 2))
    return pad


def neutral_deck(deck, top):
    """Колода без своей рубашки: зелень срезов становится тёмной, верх закрашен — на него ляжет рубашка из игры, и скин
    не оставит зелёной каймы."""
    a = np.array(deck)
    r, g, b = [a[..., i].astype(np.float32) for i in range(3)]
    green = g > r * 1.02
    gray = (r + g + b) / 3 * .62
    a[green, 0], a[green, 1], a[green, 2] = gray[green], (gray * .88)[green], (gray * .76)[green]
    h, w = a.shape[:2]
    middle = np.mean(top, axis=0)
    # Чуть шире углов рубашки: из-под её скруглений не выглядывает старый рисунок.
    points = np.array([[(x + (x - middle[0]) * .03) * w, (1 - (y + (y - middle[1]) * .03)) * h] for x, y in top], np.int32)
    paint = np.zeros((h, w), np.uint8)
    cv2.fillConvexPoly(paint, points, 1)
    a[(paint > 0) & (a[..., 3] > 0), :3] = (26, 22, 18)
    return Image.fromarray(a)


def main():
    for name, path in zip(SHEETS, sys.argv[1:]):
        Image.fromarray(key(path)).save(REF / f"hud-durak-{name}-sheet.png", optimize=True)
    table = np.asarray(Image.open(REF / "hud-durak-table-sheet.png"))
    ys, xs = np.nonzero(table[..., 3] > 40)
    # Стол вплотную к краям картинки: эллипс обода считается от них (DurakClubPresentation.cs).
    Image.fromarray(table[ys.min():ys.max() + 1, xs.min():xs.max() + 1]).save(HUB / "Durak/table.png", optimize=True)

    piles = np.asarray(Image.open(REF / "hud-durak-piles-sheet.png"))
    rows, labels = parts(piles)
    for box, (name, side), top in zip(rows[-1], DECKS, DECK_TOPS):
        deck = neutral_deck(save(cut(piles, labels, box), name, side), top)
        deck.save(HUB / f"{name}.png", optimize=True)
        print(name, deck.size)

    icons = np.asarray(Image.open(REF / "hud-durak-icons-sheet.png"))
    rows, labels = parts(icons)
    for box, (name, side) in zip([b for row in rows for b in row], ICONS):
        icon = save(cut(icons, labels, box), name, side)
        icon.save(HUB / f"{name}.png", optimize=True)
        print(name, icon.size)


if __name__ == "__main__":
    sys.exit(main())
