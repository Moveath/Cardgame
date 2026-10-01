using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>A single bright outer contour for the assembled chest, drawn behind its art.</summary>
    public sealed class ChestOutlineGraphic : MaskableGraphic
    {
        private const float Stroke = 7f;
        private const float Halo = 10.5f;
        private Material _ownedMaterial;
        private Sprite[] _lids;
        private int _lidIndex = -1;

        public static ChestOutlineGraphic Create(Transform parent)
        {
            var go = new GameObject("Chest silhouette outline", typeof(RectTransform), typeof(CanvasRenderer), typeof(ChestOutlineGraphic));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var graphic = go.GetComponent<ChestOutlineGraphic>();
            graphic.raycastTarget = false;
            graphic._lids = new[]
            {
                ChestArt.Load("table/lids/lid-00-closed"), ChestArt.Load("table/lids/lid-01-ajar"),
                ChestArt.Load("table/lids/lid-02-half"), ChestArt.Load("table/lids/lid-03-open")
            };
            var shader = Resources.Load<Shader>("Chests/ChestOutline");
            if (shader != null)
            {
                graphic._ownedMaterial = new Material(shader) { name = "Chest outer contour", hideFlags = HideFlags.DontSave };
                graphic._ownedMaterial.SetTexture("_RearTex", ChestArt.Load("table/chest-base-rear").texture);
                graphic._ownedMaterial.SetTexture("_FrontTex", ChestArt.Load("table/chest-base-front").texture);
                // Filling the mouth prevents an outline around the cavity or between body layers.
                graphic._ownedMaterial.SetTexture("_CavityTex", ChestArt.Load("table/chest-cavity-mask").texture);
                graphic.material = graphic._ownedMaterial;
                graphic.SetLid(0, 0f);
            }
            graphic.SetAmount(0f);
            return graphic;
        }

        public void SetAmount(float amount)
        {
            enabled = amount > 0.005f && _ownedMaterial != null;
            color = new Color(0.18f, 0.9f, 1f, Mathf.Clamp01(amount));
        }

        public void SetLid(int lower, float blend)
        {
            // A crossfade has two pictures, but a chest has one outer silhouette. Follow the
            // dominant pose rather than drawing two luminous lid edges over one another.
            var index = Mathf.Clamp(blend < 0.5f ? lower : lower + 1, 0, 3);
            if (_ownedMaterial == null || _lidIndex == index) return;
            _lidIndex = index;
            _ownedMaterial.SetTexture("_LidTex", _lids[index].texture);
            SetMaterialDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect = rectTransform.rect;
            if (_ownedMaterial == null || rect.width <= 0f || rect.height <= 0f) return;
            _ownedMaterial.SetVector("_RadiusUV", new Vector4(Stroke / rect.width, Stroke / rect.height,
                Halo / rect.width, Halo / rect.height));
            var padding = Halo + 1f;
            var left = rect.xMin - padding;
            var right = rect.xMax + padding;
            var bottom = rect.yMin - padding;
            var top = rect.yMax + padding;
            var uvX = padding / rect.width;
            var uvY = padding / rect.height;
            mesh.AddVert(new Vector3(left, bottom), color, new Vector2(-uvX, -uvY));
            mesh.AddVert(new Vector3(left, top), color, new Vector2(-uvX, 1f + uvY));
            mesh.AddVert(new Vector3(right, top), color, new Vector2(1f + uvX, 1f + uvY));
            mesh.AddVert(new Vector3(right, bottom), color, new Vector2(1f + uvX, -uvY));
            mesh.AddTriangle(0, 1, 2);
            mesh.AddTriangle(2, 3, 0);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (_ownedMaterial != null) Destroy(_ownedMaterial);
        }
    }
}
