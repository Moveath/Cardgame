using System.Collections.Generic;
using UnityEngine;

namespace CardGameClient.ThreeD
{
    /// <summary>
    /// Сетка карты со скруглёнными углами и толщиной: лицо, рубашка и ребро — три части с разными материалами.
    /// Углы вырезаны геометрией, поэтому прозрачность картинки не нужна и карты не просвечивают друг сквозь друга.
    /// </summary>
    internal static class CardMesh3D
    {
        public const float Thickness = .0006f;           // Настоящая карта тоньше миллиметра.
        private const float CornerShare = .057f;        // Радиус угла от ширины: замерено по прозрачности картинок карт.
        private const int CornerSteps = 6;
        private const float TextureInset = 1.5f / 262f; // Край картинки полупрозрачный: развёртка отступает на полтора пикселя.
        private const float HighlightMargin = .0035f;

        private static Mesh _card, _highlight;

        public static float Width => Table3DLayout.CardHeight * Table3DLayout.CardAspect;
        public static float Height => Table3DLayout.CardHeight;

        public static Mesh Card => _card != null ? _card : _card = Build();

        /// <summary>Золотая кромка выбранной карты: плоская фигура чуть больше карты, видна только по краю.</summary>
        public static Mesh Highlight => _highlight != null ? _highlight : _highlight = BuildHighlight();

        private static List<Vector2> Outline(float width, float height)
        {
            var radius = CornerShare * Width;
            var points = new List<Vector2>();
            var corners = new[]
            {
                new Vector2(width / 2 - radius, -height / 2 + radius), new Vector2(width / 2 - radius, height / 2 - radius),
                new Vector2(-width / 2 + radius, height / 2 - radius), new Vector2(-width / 2 + radius, -height / 2 + radius)
            };
            // Против часовой стрелки, если смотреть на лицо карты.
            for (var corner = 0; corner < 4; corner++)
            {
                var start = -90f + corner * 90f;
                for (var step = 0; step <= CornerSteps; step++)
                {
                    var angle = (start + 90f * step / CornerSteps) * Mathf.Deg2Rad;
                    points.Add(corners[corner] + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
                }
            }
            return points;
        }

        private static Vector2 TextureAt(Vector2 point, bool mirror)
        {
            var u = point.x / Width + .5f;
            var v = point.y / Height + .5f;
            if (mirror) u = 1f - u;
            return new Vector2(Mathf.Lerp(TextureInset, 1f - TextureInset, u), Mathf.Lerp(TextureInset, 1f - TextureInset, v));
        }

        private static Mesh Build()
        {
            var outline = Outline(Width, Height);
            var count = outline.Count;
            var half = Thickness / 2;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var face = new List<int>();
            var back = new List<int>();
            var side = new List<int>();

            // Лицо смотрит в -Z: карта без поворота обращена к камере, которая смотрит в +Z.
            var faceCentre = vertices.Count;
            vertices.Add(new Vector3(0f, 0f, -half));
            normals.Add(Vector3.back);
            uvs.Add(TextureAt(Vector2.zero, false));
            foreach (var point in outline)
            {
                vertices.Add(new Vector3(point.x, point.y, -half));
                normals.Add(Vector3.back);
                uvs.Add(TextureAt(point, false));
            }
            for (var i = 0; i < count; i++) face.AddRange(new[] { faceCentre, faceCentre + 1 + (i + 1) % count, faceCentre + 1 + i });

            // Рубашка зеркалится по ширине: со спины картинка не выглядит отражённой.
            var backCentre = vertices.Count;
            vertices.Add(new Vector3(0f, 0f, half));
            normals.Add(Vector3.forward);
            uvs.Add(TextureAt(Vector2.zero, true));
            foreach (var point in outline)
            {
                vertices.Add(new Vector3(point.x, point.y, half));
                normals.Add(Vector3.forward);
                uvs.Add(TextureAt(point, true));
            }
            for (var i = 0; i < count; i++) back.AddRange(new[] { backCentre, backCentre + 1 + i, backCentre + 1 + (i + 1) % count });

            for (var i = 0; i < count; i++)
            {
                var a = outline[i];
                var b = outline[(i + 1) % count];
                var outward = new Vector3(b.y - a.y, a.x - b.x, 0f).normalized;
                var start = vertices.Count;
                vertices.AddRange(new[] { new Vector3(a.x, a.y, -half), new Vector3(b.x, b.y, -half), new Vector3(b.x, b.y, half), new Vector3(a.x, a.y, half) });
                normals.AddRange(new[] { outward, outward, outward, outward });
                uvs.AddRange(new[] { Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero });
                side.AddRange(new[] { start, start + 1, start + 3, start + 1, start + 2, start + 3 });
            }

            var mesh = new Mesh { name = "Card 3D" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 3;
            mesh.SetTriangles(face, 0);
            mesh.SetTriangles(back, 1);
            mesh.SetTriangles(side, 2);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh BuildHighlight()
        {
            var outline = Outline(Width + HighlightMargin * 2, Height + HighlightMargin * 2);
            var vertices = new List<Vector3> { Vector3.zero };
            var triangles = new List<int>();
            foreach (var point in outline) vertices.Add(point);
            for (var i = 0; i < outline.Count; i++)
            {
                var a = 1 + i;
                var b = 1 + (i + 1) % outline.Count;
                // Обе стороны: кромка видна и с лица, и со спины.
                triangles.AddRange(new[] { 0, b, a, 0, a, b });
            }
            var mesh = new Mesh { name = "Card highlight" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
