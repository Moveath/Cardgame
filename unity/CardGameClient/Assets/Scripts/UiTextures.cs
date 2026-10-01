using System.Collections.Generic;
using UnityEngine;

namespace CardGameClient
{
    /// <summary>
    /// Процедурные спрайты для UI: скруглённые прямоугольники и круги.
    /// Не тащим внешние ассеты на этапе прототипа — этого достаточно для приличного вида
    /// и не создаёт вопросов с лицензиями.
    /// </summary>
    public static class UiTextures
    {
        // Кэш переживает выход из Play (перезагрузка домена в проекте выключена), а сами текстуры Unity при
        // выходе удаляет: удалённую запись рисуем заново, иначе картинка выходит сплошным прямоугольником.
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        public static Sprite RoundedRect(int width, int height, float radius, Color color)
        {
            var key = width + "_" + height + "_" + radius + "_" + color;
            if (Cache.TryGetValue(key, out var cached) && cached != null)
            {
                return cached;
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color[width * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var coverage = Coverage(x, y, width, height, radius);
                    pixels[y * width + x] = new Color(color.r, color.g, color.b, color.a * coverage);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();

            var border = new Vector4(radius, radius, radius, radius);
            var sprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);

            Cache[key] = sprite;
            return sprite;
        }

        public static Sprite Circle(int diameter, Color color) => RoundedRect(diameter, diameter, diameter / 2f, color);

        /// <summary>
        /// Размытая тень под карту. Компонент Shadow в Unity даёт только жёсткую копию со сдвигом,
        /// поэтому мягкое пятно рисуем текстурой: альфа плавно гаснет на протяжении feather пикселей.
        /// </summary>
        public static Sprite SoftShadow(int width, int height, float radius, float feather, float strength)
        {
            var key = "soft_" + width + "_" + height + "_" + radius + "_" + feather + "_" + strength;
            if (Cache.TryGetValue(key, out var cached) && cached != null)
            {
                return cached;
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color[width * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var distance = DistanceOutside(x + 0.5f, y + 0.5f, feather, width - feather, height - feather, radius);
                    var falloff = 1f - Mathf.Clamp01(distance / Mathf.Max(feather, 0.001f));
                    pixels[y * width + x] = new Color(0f, 0f, 0f, strength * falloff * falloff);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();

            var edge = radius + feather;
            var border = new Vector4(edge, edge, edge, edge);
            var sprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);

            Cache[key] = sprite;
            return sprite;
        }

        /// <summary>Мягкое пятно света: в центре плотное, к краю плавно уходит в ноль.</summary>
        public static Sprite RadialGlow(int diameter, Color color)
        {
            var key = "glow_" + diameter + "_" + color;
            if (Cache.TryGetValue(key, out var cached) && cached != null)
            {
                return cached;
            }

            var texture = new Texture2D(diameter, diameter, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var center = diameter / 2f;
            var pixels = new Color[diameter * diameter];
            for (var y = 0; y < diameter; y++)
            {
                for (var x = 0; x < diameter; x++)
                {
                    var distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(center, center)) / center;
                    var falloff = Mathf.Clamp01(1f - distance);
                    pixels[y * diameter + x] = new Color(color.r, color.g, color.b, color.a * falloff * falloff);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();

            var sprite = Sprite.Create(texture, new Rect(0, 0, diameter, diameter), new Vector2(0.5f, 0.5f), 100f);
            Cache[key] = sprite;
            return sprite;
        }

        /// <summary>
        /// Поля вокруг карты в текстурах разрыва, в единицах карты. В них помещается тень
        /// половинки: маска режет всё, что в неё не попало, и тень тоже.
        /// </summary>
        public const int TearMargin = 12;

        private const int TearWidth = 120 + 2 * TearMargin;
        private const int TearHeight = 168 + 2 * TearMargin;

        /// <summary>
        /// Половина карты для разрыва — маска с рваным краем. Левая и правая половины одного
        /// варианта режутся по одной и той же линии, поэтому в покое складываются в целую
        /// карту без щели. Линия не прямая — карты рвут руками, а не ножницами: она гуляет
        /// из стороны в сторону и идёт мелкими зубцами. Варианты разные, чтобы тузы не
        /// рвались под копирку.
        /// </summary>
        public static Sprite TearMask(int variant, bool left)
        {
            var key = "tear_" + variant + (left ? "_left" : "_right");
            if (Cache.TryGetValue(key, out var cached) && cached != null)
            {
                return cached;
            }

            var line = TearLine(variant);
            var pixels = new Color32[TearWidth * TearHeight];
            var solid = new Color32(255, 255, 255, 255);
            var empty = new Color32(255, 255, 255, 0);
            for (var y = 0; y < TearHeight; y++)
            {
                for (var x = 0; x < TearWidth; x++)
                {
                    var onLeft = x + 0.5f < line[y];
                    pixels[y * TearWidth + x] = onLeft == left ? solid : empty;
                }
            }

            return Cache[key] = TearSprite(pixels);
        }

        /// <summary>
        /// Белые волокна по линии разрыва: у рваного картона край светлый, а не цветной.
        /// Одна текстура на обе половинки — каждая маска оставляет только свою сторону.
        /// Волокна лежат только в пределах самой карты, в поля не выходят.
        /// </summary>
        public static Sprite TearFibers(int variant)
        {
            var key = "tear_" + variant + "_fibers";
            if (Cache.TryGetValue(key, out var cached) && cached != null)
            {
                return cached;
            }

            var line = TearLine(variant);
            var random = new System.Random(31 * (variant + 1));
            var phase = (float)(random.NextDouble() * Mathf.PI * 2f);
            var pixels = new Color32[TearWidth * TearHeight];
            for (var y = TearMargin; y < TearHeight - TearMargin; y++)
            {
                // Толщина волокон неровная: где-то нитка, где-то клок.
                var leftWidth = 1.3f + 1.6f * (0.5f + 0.5f * Mathf.Sin(y * 0.55f + phase)) + (float)random.NextDouble() * 0.8f;
                var rightWidth = 1.3f + 1.6f * (0.5f + 0.5f * Mathf.Sin(y * 0.43f + phase * 1.7f)) + (float)random.NextDouble() * 0.8f;
                for (var x = TearMargin; x < TearWidth - TearMargin; x++)
                {
                    var offset = x + 0.5f - line[y];
                    var width = offset < 0f ? leftWidth : rightWidth;
                    var coverage = Mathf.Clamp01(width - Mathf.Abs(offset) + 0.5f);
                    if (coverage > 0f)
                    {
                        pixels[y * TearWidth + x] = new Color32(255, 250, 238, (byte)(coverage * 255f));
                    }
                }
            }

            return Cache[key] = TearSprite(pixels);
        }

        private static float[] TearLine(int variant)
        {
            var random = new System.Random(7919 * (variant + 1));
            var line = new float[TearHeight];
            var center = TearWidth * 0.5f + (float)(random.NextDouble() - 0.5) * 8f;
            var phaseA = (float)(random.NextDouble() * Mathf.PI * 2f);
            var phaseB = (float)(random.NextDouble() * Mathf.PI * 2f);

            var tooth = 0;
            var toothLength = 7;
            var toothHeight = 2f;
            var side = 1f;
            for (var y = 0; y < TearHeight; y++)
            {
                if (tooth >= toothLength)
                {
                    tooth = 0;
                    toothLength = random.Next(5, 11);
                    toothHeight = 1f + (float)random.NextDouble() * 2.2f;
                    side = -side;
                }

                // Плавный изгиб всей линии плюс зубцы поверх: треугольник туда и обратно.
                var t = y / (float)TearHeight;
                var wander = 5f * Mathf.Sin(t * Mathf.PI * 1.4f + phaseA) + 2.5f * Mathf.Sin(t * Mathf.PI * 3.8f + phaseB);
                var k = tooth / (float)toothLength;
                var teeth = side * toothHeight * (1f - Mathf.Abs(2f * k - 1f));
                line[y] = center + wander + teeth;
                tooth++;
            }

            return line;
        }

        private static Sprite TearSprite(Color32[] pixels)
        {
            var texture = new Texture2D(TearWidth, TearHeight, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, TearWidth, TearHeight), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }

        /// <summary>Доли гребешка слева направо. Средняя выше — как у петуха.</summary>
        private static readonly float[] CombPeaks = { 0.72f, 1f, 0.78f };

        private const int HatWidth = 220;
        private const int HatHeight = 200;

        /// <summary>Отворот шапки: между этими долями высоты идёт светлая полоса.</summary>
        private const float HatBandBottom = 0.20f;

        private const float HatBandTop = 0.36f;

        /// <summary>Докуда поднимается тулья, дальше только гребешок.</summary>
        private const float HatCrownTop = 0.76f;

        /// <summary>
        /// Шапка петуха — клеймо для того, кто оборвал перестрелку. Именно шапка, а не
        /// гребешок сам по себе: гребешок ложится на волосы и теряется на любом костюме
        /// со шляпой, а шапка надевается поверх всего, по уши, и читается на всех.
        /// Рисуем процедурно, как и весь остальной интерфейс: тулья, светлый отворот,
        /// уши по бокам и гребешок сверху, всё через расстояния до фигур.
        /// </summary>
        public static Sprite RoosterHat(Color color)
        {
            var key = "hat_" + color;
            if (Cache.TryGetValue(key, out var cached) && cached != null)
            {
                return cached;
            }

            const float rim = HatWidth * 0.03f;
            var bandBottom = HatHeight * HatBandBottom;
            var bandTop = HatHeight * HatBandTop;
            var crownTop = HatHeight * HatCrownTop;

            var dark = new Color(color.r * 0.58f, color.g * 0.45f, color.b * 0.45f, color.a);
            var band = new Color(0.93f, 0.87f, 0.80f, color.a);
            var bandDark = new Color(0.69f, 0.62f, 0.55f, color.a);

            var texture = new Texture2D(HatWidth, HatHeight, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color[HatWidth * HatHeight];
            for (var y = 0; y < HatHeight; y++)
            {
                for (var x = 0; x < HatWidth; x++)
                {
                    var px = x + 0.5f;
                    var py = y + 0.5f;

                    var crown = RoundedBoxSdf(px, py, HatWidth * 0.17f, bandBottom,
                        HatWidth * 0.83f, crownTop, HatWidth * 0.24f);

                    // Уши свисают по бокам от отворота вниз.
                    var earLeft = RoundedBoxSdf(px, py, HatWidth * 0.02f, HatHeight * 0.03f,
                        HatWidth * 0.25f, bandTop, HatWidth * 0.10f);
                    var earRight = RoundedBoxSdf(px, py, HatWidth * 0.75f, HatHeight * 0.03f,
                        HatWidth * 0.98f, bandTop, HatWidth * 0.10f);

                    // Гребешок живёт только над тульей. Без нижней отсечки профиль высоты
                    // считает своим всё, что под ним, и заливает шапку красным столбом.
                    var crest = Mathf.Max(
                        CombCrestSdf(px, py, crownTop - HatHeight * 0.06f, HatHeight * 0.28f),
                        crownTop - HatHeight * 0.12f - py);

                    var bandShape = RoundedBoxSdf(px, py, HatWidth * 0.04f, bandBottom,
                        HatWidth * 0.96f, bandTop, HatWidth * 0.07f);

                    var redShape = Mathf.Min(Mathf.Min(crown, crest), Mathf.Min(earLeft, earRight));
                    var shape = Mathf.Min(redShape, bandShape);

                    var coverage = Mathf.Clamp01(0.5f - shape);
                    if (coverage <= 0f)
                    {
                        continue;
                    }

                    var onBand = bandShape < redShape;
                    var body = onBand ? band : color;
                    var edge = onBand ? bandDark : dark;
                    var tone = shape > -rim ? edge : body;

                    pixels[y * HatWidth + x] = new Color(tone.r, tone.g, tone.b, tone.a * coverage);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();

            var sprite = Sprite.Create(texture, new Rect(0, 0, HatWidth, HatHeight), new Vector2(0.5f, 0.5f), 100f);
            Cache[key] = sprite;
            return sprite;
        }

        /// <summary>Верхняя граница гребешка в точке x: подножие плюс купол текущей доли.</summary>
        private static float CombTopAt(float px, float baseTop, float rise)
        {
            var left = HatWidth * 0.26f;
            var right = HatWidth * 0.74f;

            var t = (px - left) / (right - left) * CombPeaks.Length;
            var index = Mathf.Clamp(Mathf.FloorToInt(t), 0, CombPeaks.Length - 1);
            var u = Mathf.Clamp01(t - index);

            return baseTop + rise * CombPeaks[index] * Mathf.Pow(Mathf.Sin(Mathf.PI * u), 0.5f);
        }

        /// <summary>
        /// Расстояние до профиля высоты гребешка. Делим на длину наклона, иначе на крутых
        /// боках зубцов сглаживание размазывается и край выглядит рваным.
        /// </summary>
        private static float CombCrestSdf(float px, float py, float baseTop, float rise)
        {
            if (px < HatWidth * 0.26f || px > HatWidth * 0.74f)
            {
                return 1000f;
            }

            var slope = (CombTopAt(px + 1f, baseTop, rise) - CombTopAt(px - 1f, baseTop, rise)) * 0.5f;
            return (py - CombTopAt(px, baseTop, rise)) / Mathf.Sqrt(1f + slope * slope);
        }

        /// <summary>Расстояние до скруглённого прямоугольника: внутри отрицательное.</summary>
        private static float RoundedBoxSdf(float px, float py, float minX, float minY, float maxX, float maxY, float radius)
        {
            var halfX = (maxX - minX) * 0.5f - radius;
            var halfY = (maxY - minY) * 0.5f - radius;

            var dx = Mathf.Abs(px - (minX + maxX) * 0.5f) - halfX;
            var dy = Mathf.Abs(py - (minY + maxY) * 0.5f) - halfY;

            var outside = Mathf.Sqrt(Mathf.Max(dx, 0f) * Mathf.Max(dx, 0f) + Mathf.Max(dy, 0f) * Mathf.Max(dy, 0f));
            return outside + Mathf.Min(Mathf.Max(dx, dy), 0f) - radius;
        }

        private static float DistanceOutside(float px, float py, float inset, float maxX, float maxY, float radius)
        {
            var cx = Mathf.Clamp(px, inset + radius, maxX - radius);
            var cy = Mathf.Clamp(py, inset + radius, maxY - radius);
            return Mathf.Max(0f, Vector2.Distance(new Vector2(px, py), new Vector2(cx, cy)) - radius);
        }

        private static float Coverage(int x, int y, int width, int height, float radius)
        {
            var px = x + 0.5f;
            var py = y + 0.5f;
            var cx = Mathf.Clamp(px, radius, width - radius);
            var cy = Mathf.Clamp(py, radius, height - radius);
            var dist = Vector2.Distance(new Vector2(px, py), new Vector2(cx, cy)) - radius;
            return Mathf.Clamp01(0.5f - dist);
        }
    }
}
