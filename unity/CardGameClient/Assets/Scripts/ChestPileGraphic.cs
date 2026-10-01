using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>
    /// A card set projected onto the cloth plane. Meshes share the original textures;
    /// no camera, render texture, or per-pack raster copy is required.
    /// </summary>
    public sealed class ChestPileGraphic : MaskableGraphic
    {
        public enum Layer { ContactShadow, Paper, Surface }

        private static readonly float[] Rows =
            { 0f, 0.006f, 0.018f, 0.04f, 0.10f, 0.20f, 0.30f, 0.40f, 0.50f,
              0.60f, 0.70f, 0.80f, 0.90f, 0.96f, 0.982f, 0.994f, 1f };
        private const int Columns = 12;
        private Sprite _sprite;
        private Layer _layer;
        private float _angle;
        private float _elevation;
        private float _width;
        private float _cos;
        private float _sin;

        public override Texture mainTexture => _sprite != null ? _sprite.texture : Texture2D.whiteTexture;

        public void Configure(Layer layer, Sprite sprite, float rotationDegrees, float elevation)
        {
            var materialChanged = _sprite != sprite;
            var changed = materialChanged || _layer != layer || _angle != rotationDegrees || _elevation != elevation;
            _sprite = sprite;
            _layer = layer;
            _angle = rotationDegrees;
            _elevation = elevation;
            raycastTarget = false;
            if (materialChanged) SetMaterialDirty();
            if (changed) SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            _width = rectTransform.rect.width;
            if (_width <= 0f) return;
            var radians = _angle * Mathf.Deg2Rad;
            _cos = Mathf.Cos(radians);
            _sin = Mathf.Sin(radians);
            if (_layer == Layer.Paper) DrawPaper(mesh);
            else if (_sprite != null) DrawTexturedPlane(mesh, _layer == Layer.ContactShadow);
        }

        private Vector2 Project(Vector2 point, float lift, Vector2 screenOffset)
        {
            return Project(point, _width, _cos, _sin, lift) + screenOffset;
        }

        /// <summary>The same projection used by the mesh, for safe pile placement on the cloth.</summary>
        public static Vector2 ProjectFacePoint(float width, float angle, float elevation, float u, float v)
        {
            var radians = angle * Mathf.Deg2Rad;
            return Project(new Vector2((u * 2f - 1f) * width * 0.423f,
                (v * 2f - 1f) * width * 0.593f), width, Mathf.Cos(radians), Mathf.Sin(radians),
                elevation + width * 0.003f);
        }

        private static Vector2 Project(Vector2 point, float width, float cos, float sin, float lift)
        {
            var x = point.x * cos - point.y * sin;
            var depth = point.x * sin + point.y * cos;
            // Far points recede and narrow. Turning the card happens on the floor plane,
            // before projection, rather than rotating a flat screen-space icon.
            var perspective = 1f / (1f + 0.28f * depth / width);
            return new Vector2(x * perspective, depth * 0.5f * perspective + lift);
        }

        private void DrawTexturedPlane(VertexHelper mesh, bool shadow)
        {
            var halfX = _width * (shadow ? 0.49f : 0.423f);
            var halfY = _width * (shadow ? 0.68f : 0.593f);
            var radius = shadow ? 0f : _width * 0.038f;
            var uv = DataUtility.GetOuterUV(_sprite);
            var lift = shadow ? 0f : _elevation + _width * 0.003f;
            var offset = shadow ? new Vector2(_width * 0.018f, -_width * 0.034f) : Vector2.zero;
            for (var row = 0; row < Rows.Length; row++)
            {
                var v = Rows[row];
                var y = Mathf.Lerp(-halfY, halfY, v);
                var corner = Mathf.Max(0f, Mathf.Abs(y) - (halfY - radius));
                var inset = radius > 0f ? radius - Mathf.Sqrt(Mathf.Max(0f, radius * radius - corner * corner)) : 0f;
                for (var column = 0; column <= Columns; column++)
                {
                    var x = Mathf.Lerp(-halfX + inset, halfX - inset, column / (float)Columns);
                    var u = (x + halfX) / (2f * halfX);
                    var point = Project(new Vector2(x, y), lift, offset);
                    Color tint;
                    if (shadow)
                    {
                        // The existing feathered ellipse stretches under the entire pack.
                        // Higher piles still retain a dark contact area instead of floating.
                        tint = new Color(0.2f, 0.13f, 0.055f, 0.92f);
                    }
                    else
                    {
                        tint = Color.Lerp(new Color(0.94f, 0.90f, 0.82f), new Color(1f, 0.985f, 0.945f), v);
                        var sideLight = Mathf.Lerp(1f, 0.97f, u);
                        tint.r *= sideLight;
                        tint.g *= sideLight;
                        tint.b *= sideLight;
                    }
                    mesh.AddVert(point, tint * color, new Vector2(Mathf.Lerp(uv.x, uv.z, u), Mathf.Lerp(uv.y, uv.w, v)));
                }
            }
            for (var row = 0; row < Rows.Length - 1; row++)
            {
                for (var column = 0; column < Columns; column++)
                {
                    var a = row * (Columns + 1) + column;
                    var b = a + Columns + 1;
                    mesh.AddTriangle(a, b, a + 1);
                    mesh.AddTriangle(a + 1, b, b + 1);
                }
            }
        }

        private void DrawPaper(VertexHelper mesh)
        {
            // Three sheets underneath the face plus its own border: exactly four cards.
            for (var layer = 3; layer >= 0; layer--)
            {
                var lift = _elevation - layer * _width * 0.012f;
                var offset = new Vector2(layer * _width * 0.003f, -layer * _width * 0.003f);
                var outer = RoundedPlane(_width * 0.43f, _width * 0.60f, _width * 0.045f);
                DrawEdgeBand(mesh, outer, lift, offset);
                DrawPaperFace(mesh, outer, lift, offset, new Color(0.47f, 0.36f, 0.23f));
                var inner = RoundedPlane(_width * 0.427f, _width * 0.597f, _width * 0.042f);
                var ivory = layer == 3 ? new Color(0.79f, 0.71f, 0.55f)
                    : layer == 2 ? new Color(0.85f, 0.79f, 0.64f)
                    : layer == 1 ? new Color(0.93f, 0.87f, 0.74f)
                    : new Color(0.99f, 0.95f, 0.85f);
                DrawPaperFace(mesh, inner, lift + _width * 0.001f, offset, ivory);
            }
        }

        private void DrawEdgeBand(VertexHelper mesh, Vector2[] outline, float lift, Vector2 offset)
        {
            var thickness = _width * 0.006f;
            for (var i = 0; i < outline.Length; i++)
            {
                var next = (i + 1) % outline.Length;
                var upperA = Project(outline[i], lift, offset);
                var upperB = Project(outline[next], lift, offset);
                var first = mesh.currentVertCount;
                mesh.AddVert(upperA, new Color(0.76f, 0.65f, 0.47f) * color, Vector2.zero);
                mesh.AddVert(upperB, new Color(0.76f, 0.65f, 0.47f) * color, Vector2.zero);
                mesh.AddVert(upperB - Vector2.up * thickness, new Color(0.35f, 0.25f, 0.14f) * color, Vector2.zero);
                mesh.AddVert(upperA - Vector2.up * thickness, new Color(0.35f, 0.25f, 0.14f) * color, Vector2.zero);
                mesh.AddTriangle(first, first + 1, first + 2);
                mesh.AddTriangle(first, first + 2, first + 3);
            }
        }

        private void DrawPaperFace(VertexHelper mesh, Vector2[] outline, float lift, Vector2 offset, Color ivory)
        {
            var first = mesh.currentVertCount;
            mesh.AddVert(Project(Vector2.zero, lift, offset), ivory * color, Vector2.zero);
            for (var i = 0; i < outline.Length; i++)
            {
                var light = Mathf.Lerp(0.90f, 1f, (outline[i].y / _width + 0.6f) / 1.2f);
                var tinted = new Color(ivory.r * light, ivory.g * light, ivory.b * light, ivory.a);
                mesh.AddVert(Project(outline[i], lift, offset), tinted * color, Vector2.zero);
            }
            for (var i = 0; i < outline.Length; i++)
                mesh.AddTriangle(first, first + 1 + i, first + 1 + (i + 1) % outline.Length);
        }

        private static Vector2[] RoundedPlane(float halfX, float halfY, float radius)
        {
            const int pointsPerCorner = 6;
            var result = new Vector2[4 * pointsPerCorner];
            for (var corner = 0; corner < 4; corner++)
            {
                var center = new Vector2(corner == 0 || corner == 3 ? -halfX + radius : halfX - radius,
                    corner < 2 ? -halfY + radius : halfY - radius);
                var start = 180f + corner * 90f;
                for (var point = 0; point < pointsPerCorner; point++)
                {
                    var angle = (start + 90f * point / (pointsPerCorner - 1)) * Mathf.Deg2Rad;
                    result[corner * pointsPerCorner + point] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                }
            }
            return result;
        }
    }
}
