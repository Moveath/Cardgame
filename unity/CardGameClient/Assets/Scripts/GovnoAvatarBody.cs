using System.Collections.Generic;
using UnityEngine;

namespace CardGameClient
{
    /// <summary>
    /// Соединяет неизменённый выбранный портрет с общим низом его костюма.
    /// У всех фигур одна поверхность под ногами; глубины считаются от неё.
    /// </summary>
    public static class GovnoAvatarBody
    {
        private const string ResourceRoot = "Govno/Stickers/";
        // Фигура в полный рост высотой 290: макушка на 140, ступни и поверхность жижи на -150.
        public const float GroundY = -150f;
        // У сгенерированных нижних модулей под подошвами есть небольшой отступ.
        private const float LowerCanvasBottom = GroundY - 4f;
        public const float TopY = 140f;
        // Доля роста от ступней, скрытая за нижним краем кадра на суше; 0 — видно в полный рост.
        private const float HiddenBelow = 0f;

        public struct Layout
        {
            public Sprite LowerBody;
            public Color BodyTint;
            public Vector2 PortraitSize;
            public Vector2 LowerSize;
            public Vector2 PortraitPosition;
            public Vector2 LowerPosition;
            public float KneeDepth;
            public float WaistDepth;
            public float NeckDepth;
            public float FullDepth;
            public float FaceY;
            // Доля исходного портрета сверху: прямоугольная маска отделяет убор
            // без копирования текстуры. Ноль — подходящего отдельного убора нет.
            public float HatFraction;

            // Необязательная индивидуальная фигурка заменяет оба составных слоя.
            public Sprite FullBody;
            public Vector2 FullBodySize;
            public Vector2 FullBodyPosition;

            // Цельная фигурка скина со своим лицом на каждой стадии: 0 — на суше … 3 — по шею.
            // Кадр 0 — весь рост; остальные обрезаны ровно по линии жижи своей стадии.
            public Sprite[] StageFrames;
            public Vector2[] StageSizes;
            public Vector2[] StagePositions;
            public Sprite HatSprite;
            public Vector2 HatSize;
            // Ноги ниже края кадра обрезаны уже на суше: тень под ногами не нужна.
            public bool CroppedBelow;

            // Тело, дорисованное к самому портрету (scripts/govno-figures/gen_bodies.py): шва нет,
            // потому что туловище продолжает одежду с нижнего края портрета.
            public Sprite Body;
            public Vector2 BodySize;
            public Vector2 BodyPosition;
            // Общая эмоция, вписанная в лицо скина: [0] — тревога, [1] — испуг.
            public Sprite[] FacePatches;
            public Vector2 FacePatchSize;
            public Vector2 FacePatchPosition;
            public float FaceHalfWidth;
        }

        private readonly struct Costume
        {
            public Costume(string asset, float height = 114f, float neckFromTop = 0.54f, float hatFraction = 0f)
                : this(asset, Color.white, height, neckFromTop, hatFraction) { }

            public Costume(string asset, Color tint, float height = 114f, float neckFromTop = 0.54f, float hatFraction = 0f)
            {
                Asset = asset;
                Tint = tint;
                PortraitHeight = height;
                NeckFromTop = neckFromTop;
                HatFraction = hatFraction;
            }

            public readonly string Asset;
            public readonly Color Tint;
            public readonly float PortraitHeight;
            public readonly float NeckFromTop;
            public readonly float HatFraction;
        }

        private static readonly Color RoyalRed = Rgb(143, 34, 40);
        private static readonly Color Burgundy = Rgb(105, 33, 60);
        private static readonly Color WizardBlue = Rgb(41, 72, 126);
        private static readonly Color Teal = Rgb(38, 81, 87);
        private static readonly Color Purple = Rgb(91, 55, 86);
        private static readonly Color DressBlue = Rgb(31, 63, 112);
        private static readonly Color DressGreen = Rgb(51, 85, 57);
        private static readonly Color Cream = Rgb(222, 205, 165);
        private static readonly Color KimonoBrown = Rgb(148, 123, 78);

        private static readonly Costume Suit = new Costume("suit");
        private static readonly Costume Brown = new Costume("brown");
        private static readonly Costume Detective = new Costume("brown", 124f, 0.59f, 0.25f);
        private static readonly Costume Captain = new Costume("blue", 124f, 0.62f, 0.24f);
        private static readonly Costume King = new Costume("robe", RoyalRed, 128f, 0.63f, 0.22f);
        private static readonly Costume Wizard = new Costume("robe", WizardBlue, 130f, 0.68f, 0.40f);
        private static readonly Costume Apron = new Costume("apron");
        private static readonly Costume Cowboy = new Costume("brown", 126f, 0.61f, 0.32f);
        private static readonly Costume Pirate = new Costume("pirate", 128f, 0.63f, 0.29f);
        private static readonly Costume Roman = new Costume("roman", 128f, 0.65f);
        private static readonly Costume Doctor = new Costume("coat");
        private static readonly Costume Winter = new Costume("brown", 124f, 0.63f, 0.32f);

        // Проверено по всем 84 исходным PNG. Порядок у Марата, Ельнура и общего
        // набора отличается: номер скина сам по себе не определяет одежду.
        private static readonly Dictionary<string, Costume[]> Costumes = new Dictionary<string, Costume[]>
        {
            ["yegor"] = new[]
            {
                Detective, Captain, King, Wizard, Apron, new Costume("robe", Teal),
                Suit, Cowboy, Brown, Pirate, Roman, Doctor
            },
            ["danil"] = new[]
            {
                Detective, new Costume("blue", 124f, 0.62f, 0.28f), King, Wizard, Apron, new Costume("robe", Burgundy),
                Suit, Cowboy, new Costume("apron", 124f, 0.60f, 0.30f), Pirate, Roman, Doctor
            },
            ["ali"] = new[]
            {
                Detective, Captain, new Costume("robe", RoyalRed, 128f, 0.63f, 0.26f), Wizard, Apron, new Costume("robe", Purple),
                Suit, Cowboy, Brown, Pirate, Roman, Doctor
            },
            ["aslan"] = new[]
            {
                Detective, Captain, King, Wizard, Apron, new Costume("robe", Teal),
                Suit, Cowboy, Brown, Pirate, Roman, Doctor
            },
            ["marat"] = new[]
            {
                Detective, Captain, new Costume("robe", Burgundy, 128f, 0.63f, 0.26f), Wizard,
                new Costume("robe", Burgundy), Suit, Apron, Cowboy, Winter, Brown, Roman, Doctor
            },
            ["yelnur"] = new[]
            {
                Suit, Detective, new Costume("blue", 124f, 0.62f, 0.28f), Brown, new Costume("robe", Burgundy),
                new Costume("robe", KimonoBrown), Suit, new Costume("robe", Cream, 124f, 0.62f),
                Winter, Brown, Brown, Suit
            },
            ["avatar"] = new[]
            {
                Detective, new Costume("robe", DressBlue, 122f, 0.57f), Captain, Suit,
                new Costume("robe", Burgundy), new Costume("robe", DressGreen, 122f, 0.60f),
                Suit, new Costume("robe", Burgundy, 122f, 0.58f), Winter, Suit,
                Brown, new Costume("robe", Burgundy)
            }
        };

        private static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();

        public static Layout Resolve(Sprite portrait)
        {
            var name = NormalizeName(portrait == null ? null : portrait.name);
            var costume = CostumeFor(name);
            var height = costume.PortraitHeight;
            var portraitSize = SizeAtHeight(portrait, height, 128f);
            var lower = Load("lower-" + costume.Asset) ?? Load("lower-suit");
            var lowerSize = ClothingSize(costume.Asset, portraitSize);
            var result = new Layout
            {
                LowerBody = lower,
                BodyTint = costume.Tint,
                PortraitSize = portraitSize,
                PortraitPosition = new Vector2(0f, TopY - portraitSize.y * 0.5f),
                LowerSize = lowerSize,
                LowerPosition = new Vector2(0f, LowerCanvasBottom + lowerSize.y * 0.5f),
                KneeDepth = Mathf.Max(14f, LowerCanvasBottom - GroundY
                    + lowerSize.y * (costume.Asset == "roman" ? 0.46f : 0.35f)),
                WaistDepth = TopY - portraitSize.y - GroundY - 5f,
                NeckDepth = TopY - portraitSize.y * costume.NeckFromTop - GroundY,
                FullDepth = TopY - GroundY + 12f,
                HatFraction = costume.HatFraction
            };

            if (!string.IsNullOrEmpty(name)) ApplyGeneratedBody(name, portrait, ref result);

            // Новый точный рисунок добавляется без таблиц или новых анимаций:
            // Resources/Govno/Stickers/Avatars/yegor_01.png, например.
            if (!string.IsNullOrEmpty(name))
            {
                result.FullBody = Load("Avatars/" + name);
                if (result.FullBody != null)
                {
                    var size = SizeAtHeight(result.FullBody, TopY - GroundY, 140f);
                    result.FullBodySize = size;
                    result.FullBodyPosition = new Vector2(0f, GroundY + size.y * 0.5f);
                    result.KneeDepth = size.y * 0.22f;
                    result.WaistDepth = size.y * 0.48f;
                    result.NeckDepth = size.y * 0.78f;
                    result.FullDepth = size.y + 12f;
                }
            }

            if (result.Body == null || result.FullBody != null) result.FaceY = result.NeckDepth + GroundY + 18f;
            if (!string.IsNullOrEmpty(name)) ApplyStageFrames(name, ref result);
            return result;
        }

        private static Dictionary<string, float[]> _bodyTable;

        // bodies.txt: имя, от макушки до земли, пояс и колено от земли, шея от макушки,
        // левый верхний угол заплатки лица, линия глаз, расстояние между глазами — в пикселях портрета.
        private static Dictionary<string, float[]> BodyTable()
        {
            if (_bodyTable != null) return _bodyTable;
            _bodyTable = new Dictionary<string, float[]>();
            var text = Resources.Load<TextAsset>(ResourceRoot + "Bodies/bodies");
            if (text == null) return _bodyTable;
            foreach (var line in text.text.Split('\n'))
            {
                var parts = line.Trim().Split(' ');
                if (parts.Length != 9) continue;
                var row = new float[8];
                var ok = true;
                for (var i = 0; i < row.Length && ok; i++)
                    ok = float.TryParse(parts[i + 1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out row[i]);
                if (ok) _bodyTable[parts[0]] = row;
            }
            return _bodyTable;
        }

        private static void ApplyGeneratedBody(string name, Sprite portrait, ref Layout result)
        {
            if (portrait == null) return;
            var body = Load("Bodies/" + name);
            if (body == null || !BodyTable().TryGetValue(name, out var row)) return;

            var total = row[0];
            var unitsPerPixel = (TopY - GroundY) / total;
            if (body.rect.width * unitsPerPixel > 170f) unitsPerPixel = 170f / body.rect.width;
            var top = GroundY + total * unitsPerPixel;
            var portraitPixels = portrait.rect.size;

            result.LowerBody = null;
            result.Body = body;
            // Холст тела начинается с макушки портрета и отцентрован по нему.
            result.BodySize = body.rect.size * unitsPerPixel;
            result.BodyPosition = new Vector2(0f, top - result.BodySize.y * 0.5f);
            result.PortraitSize = portraitPixels * unitsPerPixel;
            result.PortraitPosition = new Vector2(0f, top - result.PortraitSize.y * 0.5f);
            result.WaistDepth = row[1] * unitsPerPixel;
            result.KneeDepth = row[2] * unitsPerPixel;
            result.NeckDepth = (total - row[3]) * unitsPerPixel;
            result.FullDepth = total * unitsPerPixel + 12f;
            result.FaceY = top - row[6] * unitsPerPixel;
            result.FaceHalfWidth = row[7] * unitsPerPixel * 0.62f;

            if (row[4] < 0) return;
            var worried = Load("Faces/" + name + "-1");
            var scared = Load("Faces/" + name + "-2");
            if (worried == null || scared == null) return;
            result.FacePatches = new[] { worried, scared };
            result.FacePatchSize = worried.rect.size * unitsPerPixel;
            result.FacePatchPosition = new Vector2(
                (row[4] + worried.rect.width * 0.5f - portraitPixels.x * 0.5f) * unitsPerPixel,
                top - (row[5] + worried.rect.height * 0.5f) * unitsPerPixel);
        }

        // Resources/Govno/Stickers/Figures/yegor_01/stage-0..3.png и hat.png. Все кадры одного
        // масштаба: глубина стадии — это сколько роста срезано у её кадра.
        private static void ApplyStageFrames(string name, ref Layout result)
        {
            var frames = new Sprite[4];
            for (var i = 0; i < frames.Length; i++)
            {
                frames[i] = Load("Figures/" + name + "/stage-" + i);
                if (frames[i] == null) return;
            }

            // Кадр поясной: на суше ноги ниже середины бёдер уходят за нижний край, зато фигура
            // крупнее и лицо хорошо видно. Жижа поднимается от этого края.
            var unitsPerPixel = (TopY - GroundY) / (frames[0].rect.height * (1f - HiddenBelow));
            var widest = 0f;
            foreach (var frame in frames) widest = Mathf.Max(widest, frame.rect.width);
            if (widest * unitsPerPixel > 170f) unitsPerPixel = 170f / widest;
            var fullHeight = frames[0].rect.height * unitsPerPixel;

            var top = GroundY + fullHeight * (1f - HiddenBelow);
            var depths = new float[frames.Length];
            result.StageFrames = frames;
            result.StageSizes = new Vector2[frames.Length];
            result.StagePositions = new Vector2[frames.Length];
            for (var i = 0; i < frames.Length; i++)
            {
                var size = frames[i].rect.size * unitsPerPixel;
                result.StageSizes[i] = size;
                result.StagePositions[i] = new Vector2(0f, top - size.y * 0.5f);
                // Сколько опустить, чтобы срез кадра лёг ровно на жижу.
                depths[i] = top - size.y - GroundY;
            }

            // Колени и пояс на поясном кадре близко к краю: фигура всё равно заметно оседает.
            var step = fullHeight * 0.05f;
            result.KneeDepth = Mathf.Max(depths[1], step);
            result.WaistDepth = Mathf.Max(depths[2], result.KneeDepth + step);
            result.NeckDepth = Mathf.Max(depths[3], result.WaistDepth + step);
            result.FullDepth = top - GroundY + 12f;
            result.CroppedBelow = HiddenBelow > 0f;
            // Кадр «по шею» — это голова от макушки до шеи: по нему видно, где лицо и какой
            // оно ширины, и у мультяшной большой головы, и у фигуры обычных пропорций.
            var head = result.StageSizes[3].y;
            result.FaceY = top - head * 0.47f;
            result.FaceHalfWidth = head * 0.3f;
            result.HatSprite = Load("Figures/" + name + "/hat");
            if (result.HatSprite != null) result.HatSize = result.HatSprite.rect.size * unitsPerPixel;
        }

        private static Costume CostumeFor(string name)
        {
            var separator = name.LastIndexOf('_');
            if (separator <= 0 || !int.TryParse(name.Substring(separator + 1), out var index))
            {
                return Suit;
            }

            var group = name.Substring(0, separator);
            if (Costumes.TryGetValue(group, out var costumes) && index > 0 && index <= costumes.Length)
            {
                return costumes[index - 1];
            }

            // Будущий неизвестный скин остаётся узнаваемым благодаря исходному
            // портрету; нейтральный низ работает до появления его точной фигурки.
            return Suit;
        }

        private static string NormalizeName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            name = name.Trim().ToLowerInvariant();
            if (name.EndsWith(".png"))
            {
                name = name.Substring(0, name.Length - 4);
            }

            // В метаданных встречаются и yegor_01, и yegor_01_0. Удаляем
            // только индекс нарезки после номера костюма, не сам номер 01.
            var last = name.LastIndexOf('_');
            if (last > 0 && int.TryParse(name.Substring(last + 1), out _))
            {
                var previous = name.LastIndexOf('_', last - 1);
                if (previous > 0 && int.TryParse(name.Substring(previous + 1, last - previous - 1), out _))
                {
                    name = name.Substring(0, last);
                }
            }

            return name;
        }

        private static Vector2 SizeAtHeight(Sprite sprite, float height, float maxWidth)
        {
            var ratio = sprite == null || sprite.rect.height <= 0f ? 1f : sprite.rect.width / sprite.rect.height;
            var width = height * ratio;
            if (width > maxWidth)
            {
                height *= maxWidth / width;
                width = maxWidth;
            }

            return new Vector2(width, height);
        }

        private static Vector2 ClothingSize(string asset, Vector2 portraitSize)
        {
            // Высота подола в готовом PNG, долей от его верхней кромки. Не тянем
            // целиком квадратный файл до высоты бюста: тогда его верх торчит за
            // плечами, а сам пиджак скрывается под портретом вплоть до колен.
            var hemFromTop = 0.31f;
            var width = 145f;
            switch (asset)
            {
                case "suit": hemFromTop = 0.26f; width = 160f; break;
                case "blue": hemFromTop = 0.35f; width = 151f; break;
                case "coat": hemFromTop = 0.43f; break;
                case "apron": hemFromTop = 0.49f; width = 148f; break;
                case "pirate": hemFromTop = 0.43f; width = 148f; break;
                case "roman": hemFromTop = 0.43f; width = 148f; break;
                case "robe": hemFromTop = 0.75f; width = 135f; break;
            }

            var portraitBottom = TopY - portraitSize.y;
            var desiredHem = portraitBottom - 10f;
            var height = Mathf.Clamp((desiredHem - LowerCanvasBottom) / (1f - hemFromTop), 64f, 90f);
            // Короткие ноги и чуть увеличенная обувь — сознательная пропорция
            // маленькой фигурки. Широкий персонаж сохраняет свой силуэт.
            width *= Mathf.Clamp(portraitSize.x / 105f, 0.88f, 1.12f);
            return new Vector2(width, height);
        }

        private static Sprite Load(string relativePath)
        {
            if (Sprites.TryGetValue(relativePath, out var sprite))
            {
                return sprite;
            }

            sprite = Resources.Load<Sprite>(ResourceRoot + relativePath);
            if (sprite != null)
            {
                Sprites[relativePath] = sprite;
            }

            return sprite;
        }

        private static Color Rgb(byte r, byte g, byte b) => new Color32(r, g, b, 255);
    }
}
