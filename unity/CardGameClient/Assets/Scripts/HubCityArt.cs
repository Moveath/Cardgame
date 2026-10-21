using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>
    /// Картинка района на весь свой прямоугольник с обрезкой лишнего, без полей и масок.
    /// Левый край может проступать поверх соседа (<see cref="FadeLeft"/>, доля ширины) — так
    /// районы сливаются в одну улицу. <see cref="HazeReach"/> уводит картинку в ночную дымку
    /// (окраины): дымка густеет от одного края и за эту долю ширины становится полной; больше
    /// нуля — от левого края вправо, меньше — от правого влево. Стыки и дымка — цветом вершин;
    /// материал отдельно смягчает свет и блики, сохраняя прозрачность стыков.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class HubCityArt : MaskableGraphic
    {
        private const int Columns = 48;
        private static readonly Color Night = new Color(.16f, .12f, .3f);

        public Sprite Art;
        public bool Mirrored;
        private bool _street;
        public bool Street
        {
            get => _street;
            set { _street = value; HubScenery.Apply(this, value); }
        }
        public float FadeLeft, HazeReach;

        public override Texture mainTexture => Art != null ? Art.texture : s_WhiteTexture;

        protected override void OnEnable()
        {
            base.OnEnable();
            HubScenery.Apply(this, Street);
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var rect = GetPixelAdjustedRect();
            if (Art == null || rect.width <= 0 || rect.height <= 0) return;
            var texture = Art.texture;
            var source = Art.textureRect;
            var uv = new Rect(source.x / texture.width, source.y / texture.height,
                source.width / texture.width, source.height / texture.height);
            var sourceUv = uv;
            // Заполнить прямоугольник: лишнее по высоте или ширине срезается поровну с двух сторон.
            var excess = rect.width / rect.height / (source.width / source.height);
            if (excess > 1)
            {
                uv.y += uv.height * (1 - 1 / excess) / 2;
                uv.height /= excess;
            }
            else
            {
                uv.x += uv.width * (1 - excess) / 2;
                uv.width *= excess;
            }
            for (var i = 0; i <= Columns; i++)
            {
                var t = i / (float)Columns;
                var x = Mathf.Lerp(rect.xMin, rect.xMax, t);
                var u = Mathf.Lerp(uv.xMin, uv.xMax, Mirrored ? 1 - t : t);
                var haze = HazeReach == 0 ? 0 : Mathf.SmoothStep(0, 1, (HazeReach > 0 ? t : 1 - t) / Mathf.Abs(HazeReach));
                var tint = color * Color.Lerp(Color.white, Night, haze);
                tint.a = color.a * (FadeLeft > 0 ? Mathf.SmoothStep(0, 1, t / FadeLeft) : 1);
                vh.AddVert(new Vector3(x, rect.yMin), tint, new Vector4(u, uv.yMin, (uv.yMin - sourceUv.yMin) / sourceUv.height, 0));
                vh.AddVert(new Vector3(x, rect.yMax), tint, new Vector4(u, uv.yMax, (uv.yMax - sourceUv.yMin) / sourceUv.height, 0));
                if (i == 0) continue;
                var k = i * 2;
                vh.AddTriangle(k - 2, k - 1, k + 1);
                vh.AddTriangle(k - 2, k + 1, k);
            }
        }
    }
}
