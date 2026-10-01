using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>A small two-frame fly built from shared UI geometry; no texture or per-frame work.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ChestFlyGraphic : MaskableGraphic
    {
        private int _wingFrame;
        private Vector2 _meshCenter;
        private float _meshScale;

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        public void SetWingFrame(int frame)
        {
            frame &= 1;
            if (_wingFrame == frame) return;
            _wingFrame = frame;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect = rectTransform.rect;
            _meshCenter = rect.center;
            _meshScale = Mathf.Min(rect.width / 48f, rect.height / 40f);
            if (_meshScale <= 0f) return;

            var rim = new Color(0.64f, 0.70f, 0.71f, 0.85f);
            var dark = new Color(0.10f, 0.12f, 0.13f);
            var legs = new Color(0.53f, 0.57f, 0.56f, 0.95f);

            // The six bent legs and antennae remain visible against the dark velvet.
            for (var side = -1; side <= 1; side += 2)
            {
                Line(mesh, new Vector2(-3f, side * 2f), new Vector2(-10f, side * 6f), 1.1f, legs);
                Line(mesh, new Vector2(-10f, side * 6f), new Vector2(-14f, side * 8f), 1.1f, legs);
                Line(mesh, new Vector2(1f, side * 3f), new Vector2(-1f, side * 8f), 1.1f, legs);
                Line(mesh, new Vector2(-1f, side * 8f), new Vector2(-5f, side * 11f), 1.1f, legs);
                Line(mesh, new Vector2(5f, side * 2f), new Vector2(10f, side * 6f), 1.1f, legs);
                Line(mesh, new Vector2(10f, side * 6f), new Vector2(14f, side * 7f), 1.1f, legs);
                Line(mesh, new Vector2(12f, side * 2f), new Vector2(17f, side * 4f), 0.9f, rim);
            }

            // Broad/open and swept/back wings alternate without rebuilding a texture.
            var wingY = _wingFrame == 0 ? 10f : 5.6f;
            var wingX = _wingFrame == 0 ? -4f : -7f;
            var wingAngle = _wingFrame == 0 ? 31f : 11f;
            var wingRadius = _wingFrame == 0 ? new Vector2(10.5f, 4.3f) : new Vector2(11.5f, 2.9f);
            for (var side = -1; side <= 1; side += 2)
            {
                var center = new Vector2(wingX, side * wingY);
                var angle = -side * wingAngle;
                Ellipse(mesh, center, wingRadius, angle, new Color(0.78f, 0.88f, 0.91f, 0.60f));
                Ellipse(mesh, center, wingRadius - Vector2.one * 0.75f, angle, new Color(0.92f, 0.97f, 1f, 0.56f));
                Line(mesh, new Vector2(3f, side * 2f), new Vector2(-10f, side * wingY), 0.7f,
                    new Color(0.67f, 0.80f, 0.85f, 0.65f));
            }

            Ellipse(mesh, new Vector2(-6f, 0f), new Vector2(9.2f, 5.0f), 0f, rim);
            Ellipse(mesh, new Vector2(-6f, 0f), new Vector2(8.3f, 4.2f), 0f, dark);
            Line(mesh, new Vector2(-9f, -3f), new Vector2(-9f, 3f), 1f, new Color(0.25f, 0.29f, 0.29f));
            Line(mesh, new Vector2(-5f, -3.6f), new Vector2(-5f, 3.6f), 1f, new Color(0.25f, 0.29f, 0.29f));
            Ellipse(mesh, new Vector2(2f, 0f), new Vector2(6.0f, 5.5f), 0f, rim);
            Ellipse(mesh, new Vector2(2f, 0f), new Vector2(5.2f, 4.7f), 0f, new Color(0.15f, 0.19f, 0.19f));
            Ellipse(mesh, new Vector2(10f, 0f), new Vector2(4.6f, 4.2f), 0f, rim, 10);
            Ellipse(mesh, new Vector2(10f, 0f), new Vector2(3.9f, 3.5f), 0f, dark, 10);
            for (var side = -1; side <= 1; side += 2)
            {
                Ellipse(mesh, new Vector2(11.5f, side * 2f), new Vector2(2f, 1.9f), 0f,
                    new Color(0.66f, 0.30f, 0.23f), 8);
                Ellipse(mesh, new Vector2(12f, side * 2f + 0.5f), new Vector2(0.65f, 0.65f), 0f,
                    new Color(0.98f, 0.86f, 0.67f, 0.95f), 6);
            }
        }

        private void Ellipse(VertexHelper mesh, Vector2 center, Vector2 radius, float degrees, Color tint, int segments = 12)
        {
            var first = mesh.currentVertCount;
            var radians = degrees * Mathf.Deg2Rad;
            var cos = Mathf.Cos(radians);
            var sin = Mathf.Sin(radians);
            Vertex(mesh, center, tint);
            for (var i = 0; i < segments; i++)
            {
                var angle = i * Mathf.PI * 2f / segments;
                var x = Mathf.Cos(angle) * radius.x;
                var y = Mathf.Sin(angle) * radius.y;
                Vertex(mesh, center + new Vector2(x * cos - y * sin, x * sin + y * cos), tint);
            }
            for (var i = 0; i < segments; i++)
                mesh.AddTriangle(first, first + 1 + i, first + 1 + (i + 1) % segments);
        }

        private void Line(VertexHelper mesh, Vector2 from, Vector2 to, float thickness, Color tint)
        {
            var direction = (to - from).normalized;
            var offset = new Vector2(-direction.y, direction.x) * (thickness * 0.5f);
            var first = mesh.currentVertCount;
            Vertex(mesh, from - offset, tint);
            Vertex(mesh, from + offset, tint);
            Vertex(mesh, to + offset, tint);
            Vertex(mesh, to - offset, tint);
            mesh.AddTriangle(first, first + 1, first + 2);
            mesh.AddTriangle(first, first + 2, first + 3);
        }

        private void Vertex(VertexHelper mesh, Vector2 point, Color tint) =>
            mesh.AddVert(_meshCenter + point * _meshScale, tint * color, Vector2.zero);
    }
}
