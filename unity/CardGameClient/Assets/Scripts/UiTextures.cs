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
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        public static Sprite RoundedRect(int width, int height, float radius, Color color)
        {
            var key = width + "_" + height + "_" + radius + "_" + color;
            if (Cache.TryGetValue(key, out var cached))
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
            if (Cache.TryGetValue(key, out var cached))
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
            if (Cache.TryGetValue(key, out var cached))
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

        /// <summary>Доли гребешка слева направо. Передняя выше задних — как у петуха.</summary>
        private static readonly float[] CombPeaks = { 0.66f, 0.92f, 1f, 0.74f };

        private const int CombWidth = 240;
        private const int CombHeight = 92;
        private const float CombLeft = 18f;
        private const float CombRight = 222f;
        private const float CombBase = 26f;
        private const float CombRise = 58f;

        /// <summary>
        /// Гребешок петуха — клеймо для того, кто оборвал перестрелку. Рисуем процедурно,
        /// как и весь остальной интерфейс. Доли заданы не кругами, а профилем высоты:
        /// круги при любом расположении читаются как три шарика на плашке, а нужно, чтобы
        /// вырезы между зубцами доходили до самого основания. Снизу фигура срезана ровно —
        /// гребешок растёт из головы, а не висит над ней. По краю тёмный кант, иначе на
        /// светлом портрете силуэт пропадает.
        /// </summary>
        public static Sprite RoosterComb(Color color)
        {
            var key = "comb_" + color;
            if (Cache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            const float rim = 6f;
            var edge = new Color(color.r * 0.5f, color.g * 0.42f, color.b * 0.42f, color.a);

            var texture = new Texture2D(CombWidth, CombHeight, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color[CombWidth * CombHeight];
            for (var y = 0; y < CombHeight; y++)
            {
                for (var x = 0; x < CombWidth; x++)
                {
                    var px = x + 0.5f;
                    var py = y + 0.5f;

                    var shape = Mathf.Min(
                        RoundedBoxSdf(px, py, CombLeft, -30f, CombRight, CombBase, 13f),
                        CombCrestSdf(px, py));

                    var coverage = Mathf.Clamp01(0.5f - shape);
                    if (coverage <= 0f)
                    {
                        continue;
                    }

                    var tone = shape > -rim ? edge : color;
                    pixels[y * CombWidth + x] = new Color(tone.r, tone.g, tone.b, tone.a * coverage);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();

            var sprite = Sprite.Create(texture, new Rect(0, 0, CombWidth, CombHeight), new Vector2(0.5f, 0.5f), 100f);
            Cache[key] = sprite;
            return sprite;
        }

        /// <summary>Верхняя граница гребешка в точке x: основание плюс купол текущей доли.</summary>
        private static float CombTopAt(float px)
        {
            var t = (px - CombLeft) / (CombRight - CombLeft) * CombPeaks.Length;
            var index = Mathf.Clamp(Mathf.FloorToInt(t), 0, CombPeaks.Length - 1);
            var u = Mathf.Clamp01(t - index);

            return CombBase + CombRise * CombPeaks[index] * Mathf.Pow(Mathf.Sin(Mathf.PI * u), 0.8f);
        }

        /// <summary>
        /// Расстояние до профиля высоты. Делим на длину наклона, иначе на крутых боках
        /// зубцов сглаживание размазывается и край выглядит рваным.
        /// </summary>
        private static float CombCrestSdf(float px, float py)
        {
            if (px < CombLeft || px > CombRight)
            {
                return 1000f;
            }

            var slope = (CombTopAt(px + 1f) - CombTopAt(px - 1f)) * 0.5f;
            return (py - CombTopAt(px)) / Mathf.Sqrt(1f + slope * slope);
        }

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
