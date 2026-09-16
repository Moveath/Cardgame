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
