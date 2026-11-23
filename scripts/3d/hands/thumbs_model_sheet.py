"""Лист сравнения «модель автора — наша кисть» из кадров thumbs_model.py render: по ракурсу в строку — модель, наша
кисть, наложение (наша кисть, поверх — контур модели красным).
Запуск: python scripts/3d/hands/thumbs_model_sheet.py <папка кадров> <лист.jpg> [подпись]"""
import os
import sys

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

folder, out = sys.argv[1], sys.argv[2]
title = sys.argv[3] if len(sys.argv) > 3 else ""
VIEWS = ("картинка", "справа", "сверху", "сзади", "три-четверти")
BG = (40, 38, 38)
FONT = ImageFont.truetype(r"C:\Windows\Fonts\arial.ttf", 22)
TITLE = ImageFont.truetype(r"C:\Windows\Fonts\arialbd.ttf", 26)
S = 380


def flat(im):
    t = Image.new("RGB", im.size, BG)
    t.paste(im, mask=im.split()[3])
    return t


rows = [v for v in VIEWS if os.path.exists(os.path.join(folder, v + "-model.png"))]
sheet = Image.new("RGB", (3 * S + 40, 50 + len(rows) * (S + 36)), BG)
d = ImageDraw.Draw(sheet)
d.text((10, 10), title or "Модель автора — наша кисть — наложение (красный — контур модели)", font=TITLE, fill=(255, 225, 160))
for r, view in enumerate(rows):
    model = Image.open(os.path.join(folder, view + "-model.png")).convert("RGBA")
    ours = Image.open(os.path.join(folder, view + "-ours.png")).convert("RGBA")
    edge = model.split()[3].point(lambda a: 255 if a > 128 else 0)
    outline = ImageChops.difference(edge.filter(ImageFilter.MaxFilter(5)), edge)
    over = flat(ours)
    red = Image.new("RGB", over.size, (255, 40, 40))
    over.paste(red, mask=outline)
    y = 50 + r * (S + 36)
    for c, (label, im) in enumerate((("модель: " + view, flat(model)), ("наша кисть", flat(ours)), ("наложение", over))):
        x = 10 + c * (S + 10)
        d.text((x + 4, y), label, font=FONT, fill=(255, 235, 190))
        sheet.paste(im.resize((S, S), Image.LANCZOS), (x, y + 30))
sheet.save(out, quality=90)
print("saved", out, sheet.size)
