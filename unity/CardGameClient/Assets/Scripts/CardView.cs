using System;
using CardGame.Core.Cards;
using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>
    /// Визуальное представление одной карты. Слой отображения: правил здесь нет,
    /// вся логика живёт в CardGame.Core.
    /// </summary>
    public sealed class CardView : MonoBehaviour
    {
        public const float Width = 120f;
        public const float Height = 168f;

        private static readonly Color FaceColor = new Color(0.97f, 0.96f, 0.92f);
        private static readonly Color BackColor = new Color(0.15f, 0.26f, 0.49f);
        private static readonly Color RedInk = new Color(0.78f, 0.13f, 0.15f);
        private static readonly Color BlackInk = new Color(0.13f, 0.13f, 0.16f);
        private static readonly Color TrumpGlow = new Color(1f, 0.82f, 0.25f);

        private Image _background;
        private Outline _outline;
        private Text _corner;
        private Text _center;
        private Button _button;
        private CanvasGroup _group;

        public RectTransform Rect { get; private set; }

        public static CardView Create(Transform parent, Font font, float scale = 1f)
        {
            var go = new GameObject("Card", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
            go.transform.SetParent(parent, false);

            var view = go.AddComponent<CardView>();
            view.Rect = (RectTransform)go.transform;
            view.Rect.anchorMin = new Vector2(0.5f, 0.5f);
            view.Rect.anchorMax = new Vector2(0.5f, 0.5f);
            view.Rect.pivot = new Vector2(0.5f, 0.5f);
            view.Rect.sizeDelta = new Vector2(Width * scale, Height * scale);

            view._background = go.GetComponent<Image>();
            view._background.color = FaceColor;
            view._group = go.GetComponent<CanvasGroup>();

            view._outline = go.AddComponent<Outline>();
            view._outline.effectColor = TrumpGlow;
            view._outline.effectDistance = new Vector2(3f, 3f);
            view._outline.enabled = false;

            view._button = go.AddComponent<Button>();
            view._button.transition = Selectable.Transition.None;
            view._button.targetGraphic = view._background;
            view._button.interactable = false;

            view._corner = CreateLabel(view.Rect, font, Mathf.RoundToInt(30 * scale), TextAnchor.UpperLeft, 10f * scale);
            view._center = CreateLabel(view.Rect, font, Mathf.RoundToInt(66 * scale), TextAnchor.MiddleCenter, 4f * scale);

            return view;
        }

        public void ShowFace(Card card)
        {
            _background.color = FaceColor;
            var ink = card.Suit == Suit.Hearts || card.Suit == Suit.Diamonds ? RedInk : BlackInk;

            _corner.text = RankText(card.Rank);
            _corner.color = ink;
            _center.text = SuitText(card.Suit);
            _center.color = ink;
        }

        public void ShowBack()
        {
            _background.color = BackColor;
            _corner.text = string.Empty;
            _center.text = string.Empty;
        }

        public void SetClickable(Action onClick)
        {
            _button.onClick.RemoveAllListeners();

            if (onClick == null)
            {
                _button.interactable = false;
                _group.alpha = 0.55f;
                _outline.enabled = false;
                return;
            }

            _button.interactable = true;
            _group.alpha = 1f;
            _outline.enabled = true;
            _button.onClick.AddListener(() => onClick());
        }

        public void SetPlain()
        {
            _button.onClick.RemoveAllListeners();
            _button.interactable = false;
            _group.alpha = 1f;
            _outline.enabled = false;
        }

        private static Text CreateLabel(RectTransform parent, Font font, int fontSize, TextAnchor anchor, float padding)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);

            var text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            return text;
        }

        private static string RankText(Rank rank)
        {
            switch (rank)
            {
                case Rank.Ten: return "10";
                case Rank.Jack: return "J";
                case Rank.Queen: return "Q";
                case Rank.King: return "K";
                case Rank.Ace: return "A";
                default: return ((int)rank).ToString();
            }
        }

        private static string SuitText(Suit suit)
        {
            switch (suit)
            {
                case Suit.Clubs: return "♣";
                case Suit.Diamonds: return "♦";
                case Suit.Hearts: return "♥";
                default: return "♠";
            }
        }
    }
}
