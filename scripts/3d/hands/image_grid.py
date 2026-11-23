"""Лист из готовых кадров: сетка подписанных картинок.
Запуск: tools/mocap/.venv/Scripts/python.exe -I scripts/3d/hands/image_grid.py <лист.jpg> <столбцов> <картинка>=<подпись> ... [--size 420] [--title "..."]
Пути — от корня репозитория или полные. Картинка «-» оставляет клетку пустой."""
import os
import sys

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))


def main():
    args = sys.argv[1:]
    size, title = 420, None
    for flag in ("--size", "--title"):
        if flag in args:
            i = args.index(flag)
            value = args[i + 1]
            del args[i:i + 2]
            if flag == "--size":
                size = int(value)
            else:
                title = value
    out, columns, cells = args[0], int(args[1]), [a.split("=", 1) if "=" in a else [a, ""] for a in args[2:]]
    rows = (len(cells) + columns - 1) // columns
    bold = ImageFont.truetype(r"C:\Windows\Fonts\arialbd.ttf", 22)
    plain = ImageFont.truetype(r"C:\Windows\Fonts\arial.ttf", 19)
    top, label = (46 if title else 0), 30
    sheet = Image.new("RGB", (size * columns, top + (size + label) * rows), (24, 24, 24))
    draw = ImageDraw.Draw(sheet)
    if title:
        draw.text((12, 10), title, fill=(240, 230, 200), font=bold)
    for i, (path, text) in enumerate(cells):
        x, y = (i % columns) * size, top + (i // columns) * (size + label)
        draw.text((x + 10, y + 5), text, fill=(240, 230, 200), font=plain)
        if path == "-":
            continue
        path = path if os.path.isabs(path) else os.path.join(ROOT, path)
        if os.path.exists(path):
            sheet.paste(Image.open(path).convert("RGB").resize((size, size), Image.LANCZOS), (x, y + label))
        else:
            draw.text((x + 20, y + size // 2), "нет кадра", fill=(200, 80, 80), font=plain)
    out = out if os.path.isabs(out) else os.path.join(ROOT, out)
    os.makedirs(os.path.dirname(out), exist_ok=True)
    sheet.save(out, quality=90)
    print("sheet", sheet.size, out)


main()
