"""Рисованные части стола дурака в Клубе (2026-11-05): UI/Hub/Durak/room.png, slot.png, plate-green.png.
Зал — размытый кадр из club-hall.png: стена с баром сверху, ковёр снизу; контур места на сукне и зелёная табличка кнопок.
Стол, колоды и значки — сгенерированные листы, их режет cut_durak.py."""
import sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[2]
HUB = ROOT / "unity/CardGameClient/Assets/Resources/UI/Hub"
OUT = HUB / "Durak"


def lerp(a, b, t):
    return a + (b - a) * t


def smooth(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def make_room():
    hall = Image.open(HUB / "club-hall.png").convert("RGB")
    # Стена с баром: столы зала ниже этого кадра закрывает стол партии и ковёр.
    wall = hall.crop((760, 0, 2540, 1001)).resize((1280, 720), Image.LANCZOS).filter(ImageFilter.GaussianBlur(5))
    carpet = hall.crop((300, 1190, 2500, 1412)).resize((1280, 360), Image.LANCZOS).filter(ImageFilter.GaussianBlur(3))
    a = np.asarray(wall).astype(np.float32)
    b = np.zeros_like(a)
    b[360:] = np.asarray(carpet).astype(np.float32)
    y = np.linspace(0, 1, 720)[:, None, None]
    a = lerp(a, b, smooth(.5, .6, y))
    # Зал уходит в тень: подписи панели читаются, а взгляд остаётся на столе.
    x = np.linspace(-1, 1, 1280)[None, :, None]
    a *= .64 * (1 - .35 * x ** 2) * lerp(.72, 1, smooth(0, .24, y))
    Image.fromarray(a.clip(0, 255).astype(np.uint8)).save(OUT / "room.png", optimize=True)


def make_slot():
    """Контур места на сукне под колоду и биту: белый, цвет даёт игра."""
    size = (240, 340)
    big = Image.new("L", (size[0] * 4, size[1] * 4), 0)
    d = ImageDraw.Draw(big)
    d.rounded_rectangle((8, 8, big.width - 9, big.height - 9), 56, outline=255, width=14)
    d.rounded_rectangle((44, 44, big.width - 45, big.height - 45), 34, outline=120, width=5)
    a = big.resize(size, Image.LANCZOS)
    out = Image.new("RGBA", size, (255, 255, 255, 0))
    out.putalpha(a)
    out.save(OUT / "slot.png", optimize=True)


def make_green_plate():
    """Красная табличка кнопки «ИГРАТЬ» в зелёном: красное поле становится изумрудным, золото остаётся."""
    plate = np.asarray(Image.open(HUB / "Hud/play-plate.png").convert("RGBA")).astype(np.float32)
    r, g, b = plate[..., 0], plate[..., 1], plate[..., 2]
    red = smooth(1.5, 2.3, r / np.maximum(np.maximum(g, b), 1))[..., None]
    green = np.dstack([g * .55 + r * .03, r * .56 + g * .35, r * .28 + g * .32])
    plate[..., :3] = lerp(plate[..., :3], green, red)
    Image.fromarray(plate.clip(0, 255).astype(np.uint8), "RGBA").save(OUT / "plate-green.png", optimize=True)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    make_room()
    make_slot()
    make_green_plate()
    for name in ("room", "slot", "plate-green"):
        print(name, Image.open(OUT / f"{name}.png").size, (OUT / f"{name}.png").stat().st_size // 1024, "KB")


if __name__ == "__main__":
    sys.exit(main())
