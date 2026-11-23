"""Лист из кадров стенда gesture_lab.py: строки — папки с кадрами, столбцы — ракурсы.
Запуск: tools/mocap/.venv/Scripts/python.exe -I scripts/3d/hands/gesture_sheet.py <лист.jpg> <рука R|L> <папка>=<подпись> ... [--views тыл,спереди] [--size 420]
Папка — путь от screens/3d-hands/stage2/ или полный путь. Подпись строки можно не давать."""
import os
import sys

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
BASE = os.path.join(ROOT, "screens", "3d-hands", "stage2")
VIEWS = ["тыл", "спереди", "ладонь", "большой", "мизинец", "три-четверти"]


def main():
    args = sys.argv[1:]
    size = 420
    views = VIEWS
    if "--views" in args:
        i = args.index("--views")
        views = args[i + 1].split(",")
        del args[i:i + 2]
    if "--size" in args:
        i = args.index("--size")
        size = int(args[i + 1])
        del args[i:i + 2]
    out, side, rows = args[0], args[1], [a.split("=", 1) if "=" in a else [a, a] for a in args[2:]]
    left, top = 210, 40
    bold = ImageFont.truetype(r"C:\Windows\Fonts\arialbd.ttf", 20)
    plain = ImageFont.truetype(r"C:\Windows\Fonts\arial.ttf", 19)
    sheet = Image.new("RGB", (left + size * len(views), top + size * len(rows)), (24, 24, 24))
    draw = ImageDraw.Draw(sheet)
    for c, view in enumerate(views):
        draw.text((left + c * size + 12, 10), view, fill=(240, 230, 200), font=bold)
    for r, (folder, label) in enumerate(rows):
        y = top + r * size
        words, line, lines = label.split(), "", []
        for word in words:
            if len(line + " " + word) > 17:
                lines.append(line)
                line = word
            else:
                line = (line + " " + word).strip()
        lines.append(line)
        for i, text in enumerate(lines):
            draw.text((12, y + size // 2 - 12 * len(lines) + i * 24), text, fill=(240, 230, 200), font=plain)
        # Имя папки может нести свою руку: «Fist@L» — кадры левой руки в строке листа правой.
        row_side = side
        if "@" in folder:
            folder, row_side = folder.rsplit("@", 1)
        folder = folder if os.path.isabs(folder) else os.path.join(BASE, folder)
        for c, view in enumerate(views):
            path = os.path.join(folder, "%s-%s.png" % (row_side, view))
            if os.path.exists(path):
                sheet.paste(Image.open(path).convert("RGB").resize((size, size), Image.LANCZOS), (left + c * size, y))
            else:
                draw.text((left + c * size + 20, y + size // 2), "нет кадра", fill=(200, 80, 80), font=plain)
    out = out if os.path.isabs(out) else os.path.join(BASE, out)
    sheet.save(out, quality=90)
    print("sheet", sheet.size, out)


main()
