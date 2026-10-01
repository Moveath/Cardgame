using CardGame.Core.Cards;
using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>Four paper edges with a separate back or rank face, usable in a chest or in flight.</summary>
    public sealed class ChestCardStackView : MonoBehaviour
    {
        private Image _back;
        private Image _compactEdges;
        private RectTransform _expandedDepth;
        private RectTransform _faceMask;
        private RectTransform _treasureLayer;
        private ChestPileGraphic _treasureShadow;
        private ChestPileGraphic _treasurePaper;
        private ChestPileGraphic _treasureSurface;
        private bool _treasureMode;
        private float _treasureAngle;
        private float _treasureElevation;
        public RectTransform Rect { get; private set; }

        public static ChestCardStackView Create(Transform parent, float width, Sprite back = null)
        {
            var go = new GameObject("Four-card pack", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<ChestCardStackView>();
            view.Rect = (RectTransform)go.transform;
            view.Rect.anchorMin = view.Rect.anchorMax = new Vector2(0.5f, 0.5f);
            view.Rect.sizeDelta = new Vector2(width, width * 1.5f);

            view._compactEdges = AddImage(view.Rect, "Paper edges", ChestArt.Load("cards/stack-edges"));
            Stretch(view._compactEdges.rectTransform);

            var maskImage = AddImage(view.Rect, "Rounded skin mask", UiTextures.RoundedRect(240, 340, 12f, Color.white));
            var maskRect = maskImage.rectTransform;
            view._faceMask = maskRect;
            maskRect.anchorMin = new Vector2(8f / 256f, 36f / 384f);
            maskRect.anchorMax = new Vector2(248f / 256f, 376f / 384f);
            maskRect.offsetMin = maskRect.offsetMax = Vector2.zero;
            var mask = maskImage.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;
            view._back = AddImage(maskRect, "Owner card back", back != null ? back : ChestArt.GreenBack);
            Stretch(view._back.rectTransform);
            return view;
        }

        public void SetBack(Sprite back)
        {
            _back.sprite = back != null ? back : ChestArt.GreenBack;
            RefreshTreasureSurface();
        }

        public void SetFace(Rank rank, Suit suit = Suit.Spades)
        {
            _back.sprite = ChestArt.Face(rank, suit);
            RefreshTreasureSurface();
        }

        /// <summary>
        /// A close-up set contains exactly four cards: three offset sheets and the top face.
        /// The default compact sprite remains available for flying packs and compact previews.
        /// </summary>
        public void SetDisplayDepth(bool expanded)
        {
            _treasureMode = false;
            if (_treasureLayer != null) _treasureLayer.gameObject.SetActive(false);
            _faceMask.gameObject.SetActive(true);
            if (expanded && _expandedDepth == null) BuildExpandedDepth();
            _compactEdges.enabled = !expanded;
            if (_expandedDepth != null) _expandedDepth.gameObject.SetActive(expanded);
        }

        /// <summary>
        /// Lay the set on the chest floor. Rotation is within the floor plane; elevation is
        /// a lift in local UI units. The Rect itself stays unrotated and keeps width x 1.5 width.
        /// </summary>
        public void SetTreasurePose(float rotationDegrees, float elevation = 0f)
        {
            if (_treasureLayer == null)
            {
                _treasureLayer = (RectTransform)new GameObject("Treasure perspective", typeof(RectTransform)).transform;
                _treasureLayer.SetParent(Rect, false);
                Stretch(_treasureLayer);
                _treasureShadow = AddPileGraphic(_treasureLayer, "Contact shadow mesh");
                _treasurePaper = AddPileGraphic(_treasureLayer, "Four paper cards mesh");
                _treasureSurface = AddPileGraphic(_treasureLayer, "Projected face mesh");
            }
            _treasureMode = true;
            _treasureAngle = rotationDegrees;
            _treasureElevation = elevation;
            _compactEdges.enabled = false;
            if (_expandedDepth != null) _expandedDepth.gameObject.SetActive(false);
            // Preserve _back and its sprite for callers, while the projected mesh renders it.
            _faceMask.gameObject.SetActive(false);
            _treasureLayer.gameObject.SetActive(true);
            _treasureShadow.Configure(ChestPileGraphic.Layer.ContactShadow, ChestArt.Load("fx/stack-shadow"), rotationDegrees, elevation);
            _treasurePaper.Configure(ChestPileGraphic.Layer.Paper, null, rotationDegrees, elevation);
            RefreshTreasureSurface();
        }

        private void RefreshTreasureSurface()
        {
            if (_treasureMode)
                _treasureSurface.Configure(ChestPileGraphic.Layer.Surface, _back.sprite, _treasureAngle, _treasureElevation);
        }

        private static ChestPileGraphic AddPileGraphic(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(ChestPileGraphic));
            go.transform.SetParent(parent, false);
            Stretch((RectTransform)go.transform);
            return go.GetComponent<ChestPileGraphic>();
        }

        private void BuildExpandedDepth()
        {
            _expandedDepth = (RectTransform)new GameObject("Four-card paper depth", typeof(RectTransform)).transform;
            _expandedDepth.SetParent(Rect, false);
            Stretch(_expandedDepth);
            // All depth is behind the single current face/back. It is allocated only for
            // inspection, so small table chests keep their existing hierarchy and footprint.
            _expandedDepth.SetSiblingIndex(0);

            var shadow = AddImage(_expandedDepth, "Soft contact shadow", ChestArt.Load("fx/stack-shadow"));
            shadow.color = new Color(0.06f, 0.035f, 0.01f, 0.94f);
            // The ellipse touches the bottom sheet and extends at most 8% of the card width.
            PlaceNormalized(shadow.rectTransform, new Vector2(0.505f, 0.06f), new Vector2(1.07f, 0.32f / 1.5f));

            var roundedPaper = UiTextures.RoundedRect(240, 340, 12f, Color.white);
            // Draw the lowest sheet first. A narrow dark outline separates each warm ivory
            // edge; three small offsets make the four-card count legible without a deck effect.
            for (var layer = 3; layer >= 1; layer--)
            {
                var paper = AddImage(_expandedDepth, "Paper card " + (4 - layer), roundedPaper);
                paper.color = new Color(0.40f, 0.31f, 0.20f, 1f);
                SetCardRect(paper.rectTransform, 0.024f * layer, -0.029f * layer);
                paper.rectTransform.localRotation = Quaternion.Euler(0f, 0f,
                    layer == 3 ? 1.15f : layer == 2 ? -0.7f : 0.3f);
                var surface = AddImage(paper.rectTransform, "Ivory surface", roundedPaper);
                surface.color = layer == 3 ? new Color(0.81f, 0.75f, 0.62f)
                    : layer == 2 ? new Color(0.9f, 0.85f, 0.73f)
                    : new Color(0.97f, 0.93f, 0.84f);
                surface.rectTransform.anchorMin = new Vector2(0.011f, 0.008f);
                surface.rectTransform.anchorMax = new Vector2(0.989f, 0.992f);
                surface.rectTransform.offsetMin = surface.rectTransform.offsetMax = Vector2.zero;
            }

            // This border is the fourth/top card, not another sheet. The existing face image
            // sits directly over it, so SetFace and SetBack always change the top of the set.
            var topEdge = AddImage(_expandedDepth, "Top card edge", roundedPaper);
            topEdge.color = new Color(0.99f, 0.95f, 0.85f);
            SetCardRect(topEdge.rectTransform, 0f, 0f);
            topEdge.rectTransform.anchorMin -= new Vector2(0.009f, 0.006f);
            topEdge.rectTransform.anchorMax += new Vector2(0.009f, 0.006f);
        }

        private static void SetCardRect(RectTransform rect, float shiftX, float shiftY)
        {
            var shift = new Vector2(shiftX, shiftY / 1.5f);
            rect.anchorMin = new Vector2(8f / 256f, 36f / 384f) + shift;
            rect.anchorMax = new Vector2(248f / 256f, 376f / 384f) + shift;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static void PlaceNormalized(RectTransform rect, Vector2 center, Vector2 size)
        {
            rect.anchorMin = center - size * 0.5f;
            rect.anchorMax = center + size * 0.5f;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static Image AddImage(Transform parent, string name, Sprite sprite)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            return image;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
