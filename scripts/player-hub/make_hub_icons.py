"""Временные иконки меню: белый значок с тёмной обводкой на прозрачном фоне, 128×128.

Меню красит их само (активная вкладка — золотом), поэтому значки белые. Настоящие иконки
положить в ту же папку под теми же именами — код менять не нужно.

    python scripts/player-hub/make_hub_icons.py
"""
import math
import os
import uuid

from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
RES = os.path.join(ROOT, "unity", "CardGameClient", "Assets", "Resources")
OUT = os.path.join(RES, "UI", "Hub", "Icons")
META = os.path.join(RES, "Govno", "Stickers", "mud-pool.png.meta")
S = 512                      # рисуем крупно и уменьшаем — края гладкие
SIZE = 128
OUTLINE = 22
DARK = (22, 34, 28, 255)


def canvas():
    return Image.new("L", (S, S), 0)


def rot(points, angle, cx=S / 2, cy=S / 2):
    a = math.radians(angle)
    return [(cx + (x - cx) * math.cos(a) - (y - cy) * math.sin(a),
             cy + (x - cx) * math.sin(a) + (y - cy) * math.cos(a)) for x, y in points]


def rounded(d, box, r, fill=255):
    d.rounded_rectangle(box, r, fill=fill)


def star(cx, cy, outer, inner, n=5, start=-90):
    return [(cx + (outer if i % 2 == 0 else inner) * math.cos(math.radians(start + i * 180 / n)),
             cy + (outer if i % 2 == 0 else inner) * math.sin(math.radians(start + i * 180 / n))) for i in range(2 * n)]


def shop():
    m = canvas(); d = ImageDraw.Draw(m)
    rounded(d, (120, 190, 392, 440), 36)
    d.ellipse((180, 90, 332, 290), outline=255, width=34)
    d.rectangle((150, 190, 362, 300), fill=255)
    return m, [("line", [(200, 250), (200, 270)]), ("line", [(312, 250), (312, 270)])]


def collection():
    m = canvas(); d = ImageDraw.Draw(m)
    d.polygon([(170, 110), (215, 110), (256, 155), (297, 110), (342, 110), (438, 200), (385, 262),
               (350, 230), (350, 430), (162, 430), (162, 230), (127, 262), (74, 200)], fill=255)
    return m, []


def home():
    m = canvas(); d = ImageDraw.Draw(m)
    back = rot([(150, 110), (320, 110), (320, 380), (150, 380)], -14)
    front = rot([(200, 140), (370, 140), (370, 410), (200, 410)], 12)
    d.polygon(back, fill=255)
    d.polygon(front, fill=255)
    cx, cy = sum(p[0] for p in front) / 4, sum(p[1] for p in front) / 4
    return m, [("poly", star(cx, cy, 70, 30)), ("poly_outline", back)]


def friends():
    m = canvas(); d = ImageDraw.Draw(m)
    d.ellipse((265, 100, 375, 210), fill=255)
    d.pieslice((215, 215, 425, 425), 180, 360, fill=255)
    d.ellipse((140, 140, 260, 260), fill=255)
    d.pieslice((85, 265, 315, 495), 180, 360, fill=255)
    return m, [("arc", (140, 140, 260, 260))]


def progress():
    m = canvas(); d = ImageDraw.Draw(m)
    d.polygon(star(256, 270, 190, 82), fill=255)
    return m, []


def bank():
    m = canvas(); d = ImageDraw.Draw(m)
    d.polygon([(256, 80), (440, 190), (72, 190)], fill=255)
    for x in (120, 200, 280, 360):
        d.rectangle((x - 22, 210, x + 22, 380), fill=255)
    d.rectangle((80, 390, 432, 440), fill=255)
    return m, []


def quests():
    m = canvas(); d = ImageDraw.Draw(m)
    rounded(d, (130, 100, 382, 440), 30)
    return m, [("check", [(190, 280), (240, 335), (330, 215)])]


def chats():
    m = canvas(); d = ImageDraw.Draw(m)
    d.ellipse((70, 100, 442, 360), fill=255)
    d.polygon([(130, 300), (200, 330), (110, 430)], fill=255)
    return m, [("dot", (170, 230)), ("dot", (256, 230)), ("dot", (342, 230))]


def settings():
    m = canvas(); d = ImageDraw.Draw(m)
    d.ellipse((126, 126, 386, 386), fill=255)
    for i in range(8):
        tooth = rot([(226, 60), (286, 60), (296, 150), (216, 150)], i * 45)
        d.polygon(tooth, fill=255)
    d.ellipse((206, 206, 306, 306), fill=0)
    return m, []


def back():
    m = canvas(); d = ImageDraw.Draw(m)
    d.line([(320, 100), (170, 256), (320, 412)], fill=255, width=70, joint="curve")
    for x, y in [(320, 100), (320, 412), (170, 256)]:
        d.ellipse((x - 35, y - 35, x + 35, y + 35), fill=255)
    return m, []


def plus():
    m = canvas(); d = ImageDraw.Draw(m)
    rounded(d, (216, 96, 296, 416), 40)
    rounded(d, (96, 216, 416, 296), 40)
    return m, []


def pass_ticket():
    m = canvas(); d = ImageDraw.Draw(m)
    rounded(d, (60, 150, 452, 362), 30)
    for x in (60, 452):
        d.ellipse((x - 42, 214, x + 42, 298), fill=0)
    return m, [("poly", star(256, 256, 70, 30))]


def render(mask, marks):
    """Белый значок с тёмной обводкой; метки — тёмные детали внутри."""
    outline = mask.filter(ImageFilter.MaxFilter(OUTLINE * 2 + 1))
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    img.paste(DARK, (0, 0), outline)
    img.paste((255, 255, 255, 255), (0, 0), mask)
    d = ImageDraw.Draw(img)
    for kind, data in marks:
        if kind == "line":
            d.line(data, fill=DARK, width=24)
        elif kind == "poly":
            d.polygon(data, fill=DARK)
        elif kind == "poly_outline":
            d.line(data + [data[0]], fill=DARK, width=16)
        elif kind == "check":
            d.line(data, fill=DARK, width=40, joint="curve")
        elif kind == "dot":
            x, y = data
            d.ellipse((x - 30, y - 30, x + 30, y + 30), fill=DARK)
        elif kind == "arc":
            d.ellipse(data, outline=DARK, width=16)
    return img.resize((SIZE, SIZE), Image.LANCZOS)


def write(img, name):
    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, name + ".png")
    img.save(path)
    meta = path + ".meta"
    if not os.path.exists(meta):
        text = open(META, encoding="utf-8").read()
        lines = [("guid: " + uuid.uuid4().hex) if line.startswith("guid:") else line for line in text.split("\n")]
        open(meta, "w", encoding="utf-8", newline="\n").write("\n".join(lines))


ICONS = {"shop": shop, "collection": collection, "home": home, "friends": friends, "progress": progress,
         "bank": bank, "quests": quests, "chats": chats, "settings": settings, "back": back, "plus": plus,
         "pass": pass_ticket}

if __name__ == "__main__":
    sheet = Image.new("RGBA", (SIZE * len(ICONS), SIZE), (40, 90, 60, 255))
    for i, (name, draw) in enumerate(ICONS.items()):
        mask, marks = draw()
        icon = render(mask, marks)
        write(icon, name)
        sheet.alpha_composite(icon, (i * SIZE, 0))
    preview = os.path.join(ROOT, "screens", "player-hub-lobby")
    os.makedirs(preview, exist_ok=True)
    sheet.save(os.path.join(preview, "icons.png"))
    print("ok", len(ICONS))
