using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>
    /// Мягкий край картинки (2026-10-24, подход к фонтану): прямоугольник Image заменяется сеткой 8×8, у которой
    /// прозрачность плавно падает к краям на долю <see cref="Feather"/> стороны. Так крупный план проявляется поверх
    /// улицы без резкой рамки. Ноль — обычный прямоугольник, сетка не строится.
    /// </summary>
    public sealed class HubWalkFeather : BaseMeshEffect
    {
        private static readonly List<UIVertex> Quad = new List<UIVertex>();
        private static readonly float[] Steps = new float[8];
        private float _feather;

        public float Feather
        {
            get => _feather;
            set
            {
                if (Mathf.Approximately(_feather, value)) return;
                _feather = value;
                if (graphic != null) graphic.SetVerticesDirty();
            }
        }

        public override void ModifyMesh(VertexHelper mesh)
        {
            if (!IsActive() || _feather <= .001f) return;
            Quad.Clear();
            mesh.GetUIVertexStream(Quad);
            if (Quad.Count == 0) return;
            UIVertex low = Quad[0], high = Quad[0];
            foreach (var vertex in Quad)
            {
                if (vertex.position.x + vertex.position.y < low.position.x + low.position.y) low = vertex;
                if (vertex.position.x + vertex.position.y > high.position.x + high.position.y) high = vertex;
            }
            var f = Mathf.Clamp(_feather, .001f, .49f);
            Steps[0] = 0; Steps[1] = f / 3; Steps[2] = 2 * f / 3; Steps[3] = f;
            Steps[4] = 1 - f; Steps[5] = 1 - 2 * f / 3; Steps[6] = 1 - f / 3; Steps[7] = 1;
            mesh.Clear();
            var color = (Color)low.color;
            for (var y = 0; y < Steps.Length; y++)
            for (var x = 0; x < Steps.Length; x++)
            {
                var sx = Steps[x];
                var sy = Steps[y];
                var vertex = low;
                vertex.position = new Vector3(Mathf.Lerp(low.position.x, high.position.x, sx), Mathf.Lerp(low.position.y, high.position.y, sy), low.position.z);
                vertex.uv0 = new Vector4(Mathf.Lerp(low.uv0.x, high.uv0.x, sx), Mathf.Lerp(low.uv0.y, high.uv0.y, sy), low.uv0.z, low.uv0.w);
                var edge = Mathf.SmoothStep(0, 1, Mathf.Min(sx, 1 - sx) / f) * Mathf.SmoothStep(0, 1, Mathf.Min(sy, 1 - sy) / f);
                vertex.color = new Color(color.r, color.g, color.b, color.a * edge);
                mesh.AddVert(vertex);
            }
            for (var y = 0; y < Steps.Length - 1; y++)
            for (var x = 0; x < Steps.Length - 1; x++)
            {
                var i = y * Steps.Length + x;
                mesh.AddTriangle(i, i + Steps.Length, i + Steps.Length + 1);
                mesh.AddTriangle(i, i + Steps.Length + 1, i + 1);
            }
        }
    }
}
