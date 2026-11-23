"""Нарезка листов интерфейса (ChatGPT, 2026-10-31) на картинки панели города: UI/Hub/Hud/*.png.
Исходники — screens/references/ui/hud-kit-sheet.png, hud-icons-sheet.png, hud-settings-sheet.png (значки окна
настроек, 2026-11-01), hud-tasks-sheet.png (окно заданий, 2026-11-02), hud-cash-coins-sheet.png и hud-cash-icons-sheet.png
(касса банка, 2026-11-03), hud-cash-trade-sheet.png (стрелка обмена и ручка ползунка, 2026-11-03); фон прозрачный. Что откуда — README.md там же.
Каждая деталь обрезается по своей непрозрачной части и уменьшается до ~2× размера на экране."""
import sys
from pathlib import Path
import cv2
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
REF = ROOT / "screens/references/ui"
OUT = ROOT / "unity/CardGameClient/Assets/Resources/UI/Hub/Hud"

# имя: (лист, x0, y0, x1, y1, наибольшая сторона)
PARTS = {
    "avatar-frame":   ("kit", 10, 30, 565, 585, 256),
    "level-badge":    ("kit", 540, 90, 805, 352, 128),
    "button-wood":    ("kit", 812, 115, 1018, 338, 160),
    "button-red":     ("kit", 1018, 115, 1223, 338, 160),
    "button-green":   ("kit", 1223, 115, 1440, 338, 160),
    "xp-bar":         ("kit", 530, 380, 1428, 495, 512),
    "currency-plate": ("kit", 486, 497, 1400, 702, 512),
    "play-plate":     ("kit", 34, 694, 1414, 1022, 768),
    "icon-settings":  ("icons", 8, 165, 366, 520, 192),
    "icon-mail":      ("icons", 372, 212, 732, 485, 192),
    "icon-quests":    ("icons", 735, 160, 1102, 537, 192),
    "icon-friends":   ("icons", 1096, 202, 1447, 508, 192),
    "icon-map":       ("icons", 10, 606, 382, 939, 192),
    "icon-collection":("icons", 391, 578, 749, 948, 192),
    "icon-plus":      ("icons", 759, 587, 1112, 942, 192),
    "icon-back":      ("icons", 1114, 613, 1447, 935, 192),
    "set-sound":         ("settings", 20, 181, 378, 499, 160),
    "set-music":         ("settings", 386, 169, 674, 494, 160),
    "set-vibration":     ("settings", 711, 158, 1073, 505, 160),
    "set-motion":        ("settings", 1071, 183, 1442, 506, 160),
    "set-notifications": ("settings", 19, 573, 373, 948, 160),
    "set-private":       ("settings", 410, 567, 711, 946, 160),
    "set-language":      ("settings", 731, 566, 1082, 962, 160),
    "set-lab":           ("settings", 1121, 571, 1429, 961, 160),
    "task-easy":         ("tasks", 113, 81, 485, 543, 160),
    "task-medium":       ("tasks", 534, 81, 911, 543, 160),
    "task-hard":         ("tasks", 958, 81, 1334, 543, 160),
    "task-weekly":       ("tasks", 16, 606, 363, 972, 160),
    "task-xp":           ("tasks", 363, 624, 701, 951, 96),
    "task-done":         ("tasks", 690, 621, 1033, 965, 160),
    "task-trophy":       ("tasks", 1019, 606, 1438, 982, 160),
    # Касса: горки коинов по размеру набора (стопка, кучка, гора, сундук) и значки ролика, «без рекламы», кредита.
    "cash-silver-1":     ("cash-coins", 34, 75, 341, 420, 384),
    "cash-silver-2":     ("cash-coins", 356, 71, 807, 402, 384),
    "cash-silver-3":     ("cash-coins", 803, 35, 1337, 412, 384),
    "cash-silver-4":     ("cash-coins", 1331, 1, 1773, 432, 384),
    "cash-gold-1":       ("cash-coins", 34, 510, 342, 866, 384),
    "cash-gold-2":       ("cash-coins", 353, 510, 807, 850, 384),
    "cash-gold-3":       ("cash-coins", 802, 471, 1342, 862, 384),
    "cash-gold-4":       ("cash-coins", 1332, 441, 1773, 878, 384),
    "cash-ad":           ("cash-icons", 77, 43, 653, 662, 320),
    "cash-noads":        ("cash-icons", 774, 39, 1380, 675, 160),
    "cash-loan":         ("cash-icons", 1481, 69, 2125, 666, 160),
    # Обмен в кассе: стрелка «туда» и ручка ползунка (шкала обмена и громкость в настройках).
    "cash-arrow":        ("cash-trade", 250, 120, 1080, 620, 480),
    "slider-knob":       ("cash-trade", 1400, 110, 1820, 630, 128),
}
# У значков настроек есть отдельные части (волны у динамика и телефона): склеивать шире.
JOIN = {"kit": 5, "icons": 5, "settings": 25, "tasks": 15, "cash-coins": 9, "cash-icons": 15, "cash-trade": 9}

def main():
    OUT.mkdir(parents=True, exist_ok=True)
    sheets = {k: np.array(Image.open(REF / f"hud-{k}-sheet.png").convert("RGBA")) for k in JOIN}
    for name, (sheet, x0, y0, x1, y1, side) in PARTS.items():
        a = sheets[sheet][y0:y1, x0:x1].copy()
        a[a[..., 3] < 24] = 0  # убрать едва видимый ореол вокруг деталей
        # Оставить только саму деталь: в рамку могут попасть края соседних.
        mask = cv2.dilate((a[..., 3] > 0).astype(np.uint8), np.ones((JOIN[sheet], JOIN[sheet]), np.uint8))
        n, labels, stats, _ = cv2.connectedComponentsWithStats(mask)
        keep = 1 + int(np.argmax(stats[1:, cv2.CC_STAT_AREA]))
        a[labels != keep] = 0
        ys, xs = np.nonzero(a[..., 3])
        a = a[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
        im = Image.fromarray(a)
        k = side / max(im.size)
        if k < 1: im = im.resize((round(im.width * k), round(im.height * k)), Image.LANCZOS)
        pad = Image.new("RGBA", (im.width + 4, im.height + 4), (0, 0, 0, 0))
        pad.paste(im, (2, 2))
        pad.save(OUT / f"{name}.png")
        print(name, pad.size)

if __name__ == "__main__":
    sys.exit(main())
