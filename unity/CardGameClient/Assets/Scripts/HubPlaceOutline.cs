using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>
    /// Выбор места в городе: чёткий светлый контур с золотым краем и тёмной окантовкой.
    /// Внутри силуэта нет заливки — детали и цвета здания остаются исходными. При входе контур
    /// золотой. Точки задаются в долях прямоугольника (x слева, y сверху).
    /// </summary>
    public sealed class HubPlaceOutline : MaskableGraphic
    {
        private static readonly Color Core = new Color(1f, .985f, .92f), Gold = new Color(1f, .78f, .3f),
            Dark = new Color(.075f, .045f, .02f, .92f), ClearGold = new Color(1f, .78f, .3f, 0);

        public Vector2[] Points;
        // Триангуляция нужна для проверки силуэта; сам объект цветом не закрашиваем.
        public int[] Fill;
        // Все толщины — в единицах прямоугольника. Контроллер компенсирует масштаб города.
        public float Line = 5.5f, Glow = 2.5f, Edge = 1.25f, Separator = 1.25f;
        public bool Selected;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (Points == null || Points.Length < 3) return;
            var rect = rectTransform.rect;
            var count = Points.Length;
            var points = new Vector2[count];
            for (var i = 0; i < count; i++)
                points[i] = new Vector2(rect.xMin + Points[i].x * rect.width, rect.yMax - Points[i].y * rect.height);

            // Наружу — по нормали к рёбрам; в углах — по биссектрисе, удлинённой, чтобы линия не худела.
            var area = 0f;
            for (var i = 0; i < count; i++) area += Cross(points[i], points[(i + 1) % count]);
            var outward = new Vector2[count];
            for (var i = 0; i < count; i++)
            {
                var before = Normal(points[(i + count - 1) % count], points[i], area);
                var after = Normal(points[i], points[(i + 1) % count], area);
                var bisector = (before + after).normalized;
                if (bisector == Vector2.zero) bisector = after;
                // Острый угол не выстреливает шипом: удлинение не больше чем вдвое.
                outward[i] = bisector / Mathf.Max(.5f, Vector2.Dot(bisector, after));
            }
            var half = Mathf.Max(0, Line) / 2;
            var edge = half + Mathf.Max(0, Edge);
            var border = edge + Mathf.Max(0, Separator);
            // Тёмная подложка отделяет светлую линию даже от золотых крыш и ярких окон.
            // Свечение узкое и только снаружи, не закрывает фактуру самого здания.
            Band(vh, points, outward, border, border + Mathf.Max(0, Glow),
                new Color(Gold.r, Gold.g, Gold.b, .2f), ClearGold);
            Band(vh, points, outward, -half - Separator - 1, -half - Separator,
                new Color(Dark.r, Dark.g, Dark.b, 0), Dark);
            Band(vh, points, outward, -half - Separator, border, Dark, Dark);
            Band(vh, points, outward, half, edge, Gold, Gold);
            var ink = Selected ? Gold : Core;
            Band(vh, points, outward, -half, half, ink, ink);
        }

        /// <summary>Замкнутая полоса вдоль контура от сдвига <paramref name="from"/> до <paramref name="to"/>.</summary>
        private static void Band(VertexHelper vh, IReadOnlyList<Vector2> points, IReadOnlyList<Vector2> outward,
            float from, float to, Color inner, Color outer)
        {
            var start = vh.currentVertCount;
            var count = points.Count;
            for (var i = 0; i < count; i++)
            {
                vh.AddVert(points[i] + outward[i] * from, inner, Vector2.zero);
                vh.AddVert(points[i] + outward[i] * to, outer, Vector2.zero);
            }
            for (var i = 0; i < count; i++)
            {
                var a = start + i * 2;
                var b = start + (i + 1) % count * 2;
                vh.AddTriangle(a, b, b + 1);
                vh.AddTriangle(a, b + 1, a + 1);
            }
        }

        private static Vector2 Normal(Vector2 from, Vector2 to, float area)
        {
            var edge = (to - from).normalized;
            // Обход против часовой — внутренность слева, наружу — направо; по часовой — наоборот.
            return area > 0 ? new Vector2(edge.y, -edge.x) : new Vector2(-edge.y, edge.x);
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        /// <summary>Треугольники многоугольника без самопересечений (отсечение «ушей»).</summary>
        public static int[] Triangulate(IReadOnlyList<Vector2> points)
        {
            var count = points.Count;
            var area = 0f;
            for (var i = 0; i < count; i++) area += Cross(points[i], points[(i + 1) % count]);
            var left = new List<int>();
            for (var i = 0; i < count; i++) left.Add(i);
            var triangles = new List<int>();
            while (left.Count > 3)
            {
                var clipped = false;
                for (var i = 0; i < left.Count && !clipped; i++)
                {
                    int a = left[(i + left.Count - 1) % left.Count], b = left[i], c = left[(i + 1) % left.Count];
                    // Вогнутая вершина — не ухо.
                    if (Cross(points[b] - points[a], points[c] - points[b]) * area <= 0) continue;
                    var blocked = false;
                    foreach (var other in left)
                        if (other != a && other != b && other != c && Inside(points[other], points[a], points[b], points[c]))
                        {
                            blocked = true;
                            break;
                        }
                    if (blocked) continue;
                    triangles.Add(a);
                    triangles.Add(b);
                    triangles.Add(c);
                    left.RemoveAt(i);
                    clipped = true;
                }
                // Контур с самопересечением — заливки нет, только линия.
                if (!clipped) return null;
            }
            triangles.AddRange(left);
            return triangles.ToArray();
        }

        private static bool Inside(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
        {
            var ab = Cross(b - a, point - a);
            var bc = Cross(c - b, point - b);
            var ca = Cross(a - c, point - c);
            return ab > 0 && bc > 0 && ca > 0 || ab < 0 && bc < 0 && ca < 0;
        }
    }
}
