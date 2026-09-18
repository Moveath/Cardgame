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
