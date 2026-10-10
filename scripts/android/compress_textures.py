"""Сжатие картинок для Android (2026-10-10, «да» автора: «сожми картинки как ты говорил»).

Всем картинкам в `Assets/Resources`, у которых ещё нет своего формата для Android, ставит переопределение Android
в .meta: большие фоны-сцены — ASTC 6×6 (как город с 2026-09-29), всё остальное (лица, карты, наклейки, документы,
мелкий текст, сундуки) — ASTC 4×4: в 4 раза меньше памяти, на глаз не отличить. Разрешение не меняется: потолок
размера для Android — тот же, что у картинки по умолчанию. ПК и редактор не затрагивает. Уже настроенные картинки
(город, залы — ASTC 6×6) не трогает. Повторный запуск ничего не меняет.

Ограничение Unity 6000.6 (проверено 2026-10-10): картинку с мипмапами, у которой стороны — не степени двойки и она
не квадратная (карты 269×384, аватары, монеты), импорт оставляет RGBA32, хотя формат задан и допустим (512×256
и 500×500 сжимаются, сам кодировщик ASTC такие тоже сжимает). Мипмапы им нужны (на экране они в 2–5 раз мельче
исходника), поэтому они остаются несжатыми — на экране маленькие, видеокарту почти не грузят. Рубашке карты судьбы
мипмапы сняты: шесть её соседей того же размера рисуются без них.

    python scripts/android/compress_textures.py          # применить
    python scripts/android/compress_textures.py --check  # только показать
"""
import os
import re
import sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'unity', 'CardGameClient', 'Assets', 'Resources')
ASTC_4x4, ASTC_6x6 = 48, 50
# Фоны во весь экран: мягкость 6×6 на них не видна.
BACKGROUNDS = ('UI/Hub/fortune-tent.png', 'UI/Hub/news-kiosk.png', 'UI/Hub/bank-desk.png',
               'Chests/interior/interior-rear.png', 'Chests/interior/interior-front.png', 'Chests/fx/dust-layer.png')


def block(text, target):
    match = re.search(r'  - serializedVersion: \d+\n    buildTarget: ' + target + r'\n(?:    .*\n)+', text)
    return match


def main():
    check = '--check' in sys.argv
    changed = skipped = 0
    for folder, _, files in os.walk(ROOT):
        for name in sorted(files):
            if not name.lower().endswith('.png'):
                continue
            path = os.path.join(folder, name)
            rel = os.path.relpath(path, ROOT).replace(os.sep, '/')
            meta = path + '.meta'
            text = open(meta, encoding='utf-8').read()
            android = block(text, 'Android')
            if android and re.search(r'overridden: 1', android.group(0)):
                skipped += 1
                continue
            default = block(text, 'DefaultTexturePlatform')
            if not default:
                print('нет платформы по умолчанию:', rel)
                continue
            size = re.search(r'maxTextureSize: (\d+)', default.group(0)).group(1)
            fmt = ASTC_6x6 if rel in BACKGROUNDS else ASTC_4x4
            body = default.group(0).replace('buildTarget: DefaultTexturePlatform', 'buildTarget: Android')
            body = re.sub(r'maxTextureSize: \d+', 'maxTextureSize: ' + size, body)
            body = re.sub(r'textureFormat: -?\d+', 'textureFormat: ' + str(fmt), body)
            body = re.sub(r'compressionQuality: \d+', 'compressionQuality: 100', body)
            body = re.sub(r'overridden: \d', 'overridden: 1', body)
            text = text.replace(android.group(0), body) if android else text.replace(default.group(0), default.group(0) + body)
            print(('6x6 ' if fmt == ASTC_6x6 else '4x4 ') + rel)
            changed += 1
            if not check:
                open(meta, 'w', encoding='utf-8', newline='').write(text)
    print(('будет сжато' if check else 'сжато') + ':', changed, '· уже были настроены:', skipped)


if __name__ == '__main__':
    main()
