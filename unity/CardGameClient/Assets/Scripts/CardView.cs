using System;
using System.Collections;
using System.Collections.Generic;
using CardGame.Core.Cards;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>
    /// Визуальное представление одной карты. Слой отображения: правил здесь нет,
    /// вся логика живёт в CardGame.Core.
    /// </summary>
    public sealed class CardView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public const float Width = 120f;
        public const float Height = 168f;

        private const float HoverLift = 24f;
        private const float HoverScale = 1.12f;
        private const float HoverDuration = 0.12f;

        private static readonly Color FaceColor = new Color(0.97f, 0.96f, 0.92f);
        private static readonly Color BackColor = new Color(0.16f, 0.27f, 0.5f);
        private static readonly Color RedInk = new Color(0.78f, 0.13f, 0.15f);
        private static readonly Color BlackInk = new Color(0.13f, 0.13f, 0.16f);
        private static readonly Color TrumpGlow = new Color(1f, 0.82f, 0.25f);

        private static readonly Dictionary<string, Sprite> ArtCache = new Dictionary<string, Sprite>();

        private Image _background;
        private Outline _outline;
        private Text _corner;
        private Text _center;
        private Button _button;
        private CanvasGroup _group;

        private Vector2 _restPosition;
        private float _restRotation;
        private bool _hoverEnabled;
        private Coroutine _hoverRoutine;

        public RectTransform Rect { get; private set; }

        public static CardView Create(Transform parent, Font font, float scale = 1f)
        {
            var go = new GameObject("Card", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Shadow), typeof(CanvasGroup));
            go.transform.SetParent(parent, false);

            var view = go.AddComponent<CardView>();
            view.Rect = (RectTransform)go.transform;
            view.Rect.anchorMin = new Vector2(0.5f, 0.5f);
            view.Rect.anchorMax = new Vector2(0.5f, 0.5f);
            view.Rect.pivot = new Vector2(0.5f, 0.5f);
            view.Rect.sizeDelta = new Vector2(Width * scale, Height * scale);

            view._background = go.GetComponent<Image>();
            view._background.sprite = UiTextures.RoundedRect(128, 180, 16f, Color.white);
            view._background.type = Image.Type.Sliced;
            view._background.color = FaceColor;
            view._group = go.GetComponent<CanvasGroup>();

            var shadow = go.GetComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.35f);
            shadow.effectDistance = new Vector2(3f, -5f);

            view._outline = go.AddComponent<Outline>();
            view._outline.effectColor = TrumpGlow;
            view._outline.effectDistance = new Vector2(3f, 3f);
            view._outline.enabled = false;

            view._button = go.AddComponent<Button>();
            view._button.transition = Selectable.Transition.None;
            view._button.targetGraphic = view._background;
            view._button.interactable = false;

            view._corner = CreateLabel(view.Rect, font, Mathf.RoundToInt(26 * scale), TextAnchor.UpperLeft, 10f * scale);
            view._center = CreateLabel(view.Rect, font, Mathf.RoundToInt(60 * scale), TextAnchor.MiddleCenter, 4f * scale);

            view._restPosition = Vector2.zero;
            view._restRotation = 0f;

            return view;
        }

        public void ShowFace(Card card)
        {
            var art = LoadArt(RankToken(card.Rank) + "_" + SuitToken(card.Suit));
            if (art != null)
            {
                _background.sprite = art;
                _background.type = Image.Type.Simple;
                _background.color = Color.white;
                _corner.text = string.Empty;
                _center.text = string.Empty;
                return;
            }

            _background.sprite = UiTextures.RoundedRect(128, 180, 16f, Color.white);
            _background.type = Image.Type.Sliced;
            _background.color = FaceColor;
            var ink = card.Suit == Suit.Hearts || card.Suit == Suit.Diamonds ? RedInk : BlackInk;

            _corner.text = RankText(card.Rank) + SuitText(card.Suit);
            _corner.color = ink;
            _center.text = SuitText(card.Suit);
            _center.color = ink;
        }

        public void ShowBack()
        {
            var art = LoadArt("back");
            if (art != null)
            {
                _background.sprite = art;
                _background.type = Image.Type.Simple;
                _background.color = Color.white;
                _corner.text = string.Empty;
                _center.text = string.Empty;
                return;
            }

            _background.sprite = UiTextures.RoundedRect(128, 180, 16f, Color.white);
            _background.type = Image.Type.Sliced;
            _background.color = BackColor;
            _corner.text = string.Empty;
            _center.text = string.Empty;
        }

        private static Sprite LoadArt(string key)
        {
            if (ArtCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var sprite = Resources.Load<Sprite>("Cards/" + key);
            ArtCache[key] = sprite;
            return sprite;
        }

        private static string RankToken(Rank rank)
        {
            switch (rank)
            {
                case Rank.Jack: return "j";
                case Rank.Queen: return "q";
                case Rank.King: return "k";
                case Rank.Ace: return "a";
                default: return ((int)rank).ToString();
            }
        }

        private static string SuitToken(Suit suit) => suit.ToString().ToLowerInvariant();

        public void SetClickable(Action onClick)
        {
            _button.onClick.RemoveAllListeners();

            if (onClick == null)
            {
                _button.interactable = false;
                _hoverEnabled = false;
                _group.alpha = 0.55f;
                _outline.enabled = false;
                return;
            }

            _button.interactable = true;
            _hoverEnabled = true;
            _group.alpha = 1f;
            _outline.enabled = true;
            _button.onClick.AddListener(() => onClick());
        }

        public void SetPlain()
        {
            _button.onClick.RemoveAllListeners();
            _button.interactable = false;
            _hoverEnabled = false;
            _group.alpha = 1f;
            _outline.enabled = false;
        }

        /// <summary>Задаёт позицию покоя карты (для веерной раскладки руки). Наведение курсором анимируется от неё.</summary>
        public void SetRestTransform(Vector2 position, float rotationDeg)
        {
            _restPosition = position;
            _restRotation = rotationDeg;
            Rect.anchoredPosition = position;
            Rect.localRotation = Quaternion.Euler(0f, 0f, rotationDeg);
            Rect.localScale = Vector3.one;
        }

        /// <summary>Лёгкий "пружинящий" наплыв для карты, только что попавшей на стол.</summary>
        public void PlayAppearAnimation()
        {
            RestartHover(AnimateAppear());
        }

        /// <summary>Короткий перелёт карты к своему месту на столе от края, где сидит сыгравший игрок.</summary>
        public void PlayFlyIn(Vector2 fromOffset)
        {
            RestartHover(AnimateFlyIn(fromOffset));
        }

        private IEnumerator AnimateFlyIn(Vector2 fromOffset)
        {
            const float duration = 0.15f;
            const float startScale = 0.85f;

            var target = Rect.anchoredPosition;
            var start = target + fromOffset;
            var startRotation = fromOffset.x > 0f ? -14f : 14f;

            Rect.anchoredPosition = start;
            Rect.localScale = new Vector3(startScale, startScale, 1f);
            Rect.localRotation = Quaternion.Euler(0f, 0f, startRotation);

            var t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                var k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration));
                Rect.anchoredPosition = Vector2.Lerp(start, target, k);
                var scale = Mathf.Lerp(startScale, 1f, k);
                Rect.localScale = new Vector3(scale, scale, 1f);
                Rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(startRotation, 0f, k));
                yield return null;
            }

            Rect.anchoredPosition = target;
            Rect.localScale = Vector3.one;
            Rect.localRotation = Quaternion.identity;
        }

        private IEnumerator AnimateAppear()
        {
            const float duration = 0.16f;
            const float startScale = 0.7f;

            Rect.localScale = new Vector3(startScale, startScale, 1f);
            var t = 0f;

            while (t < duration)
            {
                t += Time.deltaTime;
                var k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration));
                var scale = Mathf.Lerp(startScale, 1f, k);
                Rect.localScale = new Vector3(scale, scale, 1f);
                yield return null;
            }

            Rect.localScale = Vector3.one;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!_hoverEnabled)
            {
                return;
            }

            RestartHover(AnimateTo(_restPosition + new Vector2(0f, HoverLift), _restRotation, HoverScale));
            transform.SetAsLastSibling();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!_hoverEnabled)
            {
                return;
            }

            RestartHover(AnimateTo(_restPosition, _restRotation, 1f));
        }

        private void RestartHover(IEnumerator routine)
        {
            if (_hoverRoutine != null)
            {
                StopCoroutine(_hoverRoutine);
            }

            _hoverRoutine = StartCoroutine(routine);
        }

        private IEnumerator AnimateTo(Vector2 targetPosition, float targetRotation, float targetScale)
        {
            var startPosition = Rect.anchoredPosition;
            var startRotation = Rect.localEulerAngles.z;
            var startScale = Rect.localScale.x;
            var t = 0f;

            while (t < HoverDuration)
            {
                t += Time.deltaTime;
                var k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / HoverDuration));

                Rect.anchoredPosition = Vector2.LerpUnclamped(startPosition, targetPosition, k);
                var rotation = Mathf.LerpAngle(startRotation, targetRotation, k);
                Rect.localRotation = Quaternion.Euler(0f, 0f, rotation);
                var scale = Mathf.Lerp(startScale, targetScale, k);
                Rect.localScale = new Vector3(scale, scale, 1f);

                yield return null;
            }

            Rect.anchoredPosition = targetPosition;
            Rect.localRotation = Quaternion.Euler(0f, 0f, targetRotation);
            Rect.localScale = new Vector3(targetScale, targetScale, 1f);
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

        public static string SuitText(Suit suit)
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
