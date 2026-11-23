"""Лист сравнения кандидатов: строки — варианты основы рук, столбцы — пробные позы candidate_check.py.
Запуск: tools/mocap/.venv/Scripts/python.exe -I scripts/3d/hands/candidates_sheet.py"""
import os

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
DIR = os.path.join(ROOT, "screens", "3d-hands", "candidates")
ROWS = [("card-city", "Card City сейчас"), ("card-city-flex", "Card City, ось сгиба вдоль костяшек"), ("elena", "Rigged hand (Elena)"),
        ("hand-rig", "Hand Rig: сетка не привязана к скелету"), ("handz-free", "Handz Free")]
COLS = [("Ropen-front", "открытая"), ("Rhalf-angle", "полусогнутые"), ("Rfist-front", "кулак спереди"), ("Rfist-side", "кулак сбоку"),
        ("Rfist-angle", "кулак под углом"), ("Ronly-middle", "только средний"), ("Ronly-pinky", "только мизинец")]
W, H, LEFT, TOP = 300, 262, 230, 44

font = ImageFont.truetype(r"C:\Windows\Fonts\arialbd.ttf", 20)
small = ImageFont.truetype(r"C:\Windows\Fonts\arial.ttf", 19)
sheet = Image.new("RGB", (LEFT + W * len(COLS), TOP + H * len(ROWS)), (24, 24, 24))
draw = ImageDraw.Draw(sheet)
for c, (_, title) in enumerate(COLS):
    draw.text((LEFT + c * W + 12, 12), title, fill=(240, 230, 200), font=font)
for r, (name, title) in enumerate(ROWS):
    y = TOP + r * H
    words, line, lines = title.split(), "", []
    for word in words:
        if len(line + " " + word) > 18:
            lines.append(line)
            line = word
        else:
            line = (line + " " + word).strip()
    lines.append(line)
    for i, text in enumerate(lines):
        draw.text((12, y + 90 + i * 24), text, fill=(240, 230, 200), font=small)
    for c, (shot, _) in enumerate(COLS):
        path = os.path.join(DIR, name, shot + ".png")
        if os.path.exists(path):
            sheet.paste(Image.open(path).convert("RGB").resize((W, H)), (LEFT + c * W, y))
        else:
            draw.text((LEFT + c * W + 20, y + 110), "нет кадра", fill=(200, 80, 80), font=small)
out = os.path.join(ROOT, "screens", "3d-hands", "candidates", "сравнение-кандидатов.jpg")
sheet.save(out, quality=88)
print("sheet", sheet.size, out)
