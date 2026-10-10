using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>A small perspective mesh keeps a card flat on the illustrated tabletop.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Image))]
    public sealed class HubTableCardPerspective : BaseMeshEffect
    {
        private float _depth = .22f;
        private float _verticalScale = .70f;

        public void SetProjection(float depth, float verticalScale)
        {
            depth = Mathf.Clamp(depth, 0, .6f);
            verticalScale = Mathf.Clamp(verticalScale, .25f, 1);
            if (Mathf.Abs(_depth - depth) < .001f && Mathf.Abs(_verticalScale - verticalScale) < .001f) return;
            _depth = depth;
            _verticalScale = verticalScale;
            if (graphic != null) graphic.SetVerticesDirty();
        }

        public override void ModifyMesh(VertexHelper mesh)
        {
            if (!IsActive() || mesh.currentVertCount != 4) return;
            var bottomLeft = UIVertex.simpleVert;
            var topLeft = UIVertex.simpleVert;
            var topRight = UIVertex.simpleVert;
            var bottomRight = UIVertex.simpleVert;
            mesh.PopulateUIVertex(ref bottomLeft, 0);
            mesh.PopulateUIVertex(ref topLeft, 1);
            mesh.PopulateUIVertex(ref topRight, 2);
            mesh.PopulateUIVertex(ref bottomRight, 3);
            var center = (bottomLeft.position + topRight.position) * .5f;
            mesh.Clear();
            // Subdivision makes the illustration follow the projection without a diagonal seam.
            const int columns = 6, rows = 10;
            for (var y = 0; y <= rows; y++)
            {
                var v = y / (float)rows;
                var perspective = 1 + _depth * (v - .5f);
                for (var x = 0; x <= columns; x++)
                {
                    var u = x / (float)columns;
                    var vertex = bottomLeft;
                    var near = Vector3.Lerp(bottomLeft.position, bottomRight.position, u);
                    var far = Vector3.Lerp(topLeft.position, topRight.position, u);
                    var projected = (Vector3.Lerp(near, far, v) - center) / perspective;
                    projected.y *= _verticalScale;
                    vertex.position = center + projected;
                    vertex.uv0 = Vector4.Lerp(Vector4.Lerp(bottomLeft.uv0, bottomRight.uv0, u),
                        Vector4.Lerp(topLeft.uv0, topRight.uv0, u), v);
                    mesh.AddVert(vertex);
                    if (x == 0 || y == 0) continue;
                    var index = y * (columns + 1) + x;
                    mesh.AddTriangle(index - columns - 2, index - 1, index);
                    mesh.AddTriangle(index - columns - 2, index, index - columns - 1);
                }
            }
        }
    }
}
