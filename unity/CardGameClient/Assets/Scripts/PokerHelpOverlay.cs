using System;
using CardGame.Core.Cards;
using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>Справочник, который занимает свой слой поверх стола и перехватывает нажатия.</summary>
    public sealed class PokerHelpOverlay : MonoBehaviour
    {
        private static readonly Color Gold = new Color(0.89f, 0.74f, 0.40f);
        private static readonly Color Cream = new Color(0.96f, 0.93f, 0.83f);
        private static readonly Color Muted = new Color(0.68f, 0.78f, 0.74f);
        private RectTransform _root;
        private RectTransform _panel;
        private Font _font;
        private Action _onClose;
        private Vector2 _lastSize;

        public bool IsOpen => gameObject.activeSelf;

        public static PokerHelpOverlay Create(Transform parent, Font font, Action onClose)
        {
            var go = new GameObject("PokerHelpOverlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var overlay = go.AddComponent<PokerHelpOverlay>();
            overlay._root = (RectTransform)go.transform;
            overlay._root.anchorMin = Vector2.zero;
            overlay._root.anchorMax = Vector2.one;
            overlay._root.offsetMin = overlay._root.offsetMax = Vector2.zero;
            // This full-screen raycast surface also catches clicks outside the panel.
            go.GetComponent<Image>().color = new Color(0.015f, 0.026f, 0.026f, 0.90f);
            overlay._font = font;
            overlay._onClose = onClose;
            overlay.Build();
            overlay.Hide();
            return overlay;
        }

        public void Show()
        {
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            Resize();
        }

        public void Hide() => gameObject.SetActive(false);

        private void Close()
        {
            Hide();
            _onClose?.Invoke();
        }

        private void Update()
        {
            if (_root.rect.size != _lastSize) Resize();
#if ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) Close();
#endif
        }

        private void Resize()
        {
            _lastSize = _root.rect.size;
            var scale = Mathf.Min(_lastSize.x / 1600f, _lastSize.y / 900f);
            _panel.localScale = Vector3.one * Mathf.Max(0.05f, scale);
        }

        private void Build()
        {
            _panel = Box(_root, "Poker guide", Vector2.zero, new Vector2(1510, 852), new Color(0.045f, 0.13f, 0.12f));
            var outline = _panel.gameObject.AddComponent<HubOutline>();
            outline.effectColor = new Color(Gold.r, Gold.g, Gold.b, 0.45f);
            outline.effectDistance = new Vector2(1, 1);
            Label(_panel, "КОМБИНАЦИИ ПОКЕРА", 34, new Vector2(-115, 365), new Vector2(1160, 48), Gold, TextAnchor.MiddleLeft);
            Label(_panel, "От сильнейшей к слабейшей · лучшие 5 карт из 7 · можно использовать 0, 1 или 2 свои карты",
                22, new Vector2(0, 312), new Vector2(1390, 38), Muted, TextAnchor.MiddleLeft);
            CloseButton();

            var titles = new[] { "Роял-флеш", "Стрит-флеш", "Каре", "Фулл-хаус", "Флеш", "Стрит", "Тройка", "Две пары", "Пара", "Старшая карта" };
            var details = new[]
            {
                "10, J, Q, K, A одной масти",
                "Пять подряд одной масти",
                "Четыре карты одного ранга",
                "Тройка и пара",
                "Пять одной масти, не подряд",
                "Пять подряд, разных мастей",
                "Три карты одного ранга",
                "Две разные пары",
                "Две карты одного ранга",
                "Если нет другой комбинации"
            };
            var samples = new[]
            {
                new[] { C(10, Suit.Spades), C(11, Suit.Spades), C(12, Suit.Spades), C(13, Suit.Spades), C(14, Suit.Spades) },
                new[] { C(5, Suit.Hearts), C(6, Suit.Hearts), C(7, Suit.Hearts), C(8, Suit.Hearts), C(9, Suit.Hearts) },
                new[] { C(13, Suit.Spades), C(13, Suit.Hearts), C(13, Suit.Clubs), C(13, Suit.Diamonds), C(4, Suit.Clubs) },
                new[] { C(12, Suit.Hearts), C(12, Suit.Spades), C(12, Suit.Clubs), C(7, Suit.Diamonds), C(7, Suit.Spades) },
                new[] { C(14, Suit.Diamonds), C(11, Suit.Diamonds), C(8, Suit.Diamonds), C(5, Suit.Diamonds), C(2, Suit.Diamonds) },
                new[] { C(6, Suit.Spades), C(7, Suit.Hearts), C(8, Suit.Clubs), C(9, Suit.Diamonds), C(10, Suit.Spades) },
                new[] { C(9, Suit.Spades), C(9, Suit.Hearts), C(9, Suit.Clubs), C(14, Suit.Diamonds), C(4, Suit.Clubs) },
                new[] { C(11, Suit.Spades), C(11, Suit.Hearts), C(6, Suit.Clubs), C(6, Suit.Diamonds), C(13, Suit.Spades) },
                new[] { C(10, Suit.Spades), C(10, Suit.Hearts), C(14, Suit.Clubs), C(8, Suit.Diamonds), C(3, Suit.Spades) },
                new[] { C(14, Suit.Spades), C(11, Suit.Hearts), C(8, Suit.Clubs), C(5, Suit.Diamonds), C(2, Suit.Spades) }
            };

            for (var i = 0; i < titles.Length; i++)
            {
                var column = i / 5;
                var row = i % 5;
                var position = new Vector2(column == 0 ? -356 : 356, 225 - row * 116);
                var cell = Box(_panel, "Combination " + (i + 1), position, new Vector2(694, 105), new Color(0.075f, 0.185f, 0.166f));
                Label(cell, (i + 1).ToString("00"), 17, new Vector2(-327, 0), new Vector2(32, 38), Gold, TextAnchor.MiddleCenter);
                for (var j = 0; j < 5; j++)
                {
                    var card = CardView.Create(cell, _font, 0.45f);
                    card.Rect.anchoredPosition = new Vector2(-274 + j * 49, 0);
                    card.ShowFace(samples[i][j]);
                    card.SetPlain();
                    foreach (var graphic in card.GetComponentsInChildren<Graphic>()) graphic.raycastTarget = false;
                }
                Label(cell, titles[i], 25, new Vector2(145, 17), new Vector2(366, 35), Cream, TextAnchor.MiddleLeft, FontStyle.Bold);
                Label(cell, details[i], 19, new Vector2(145, -18), new Vector2(366, 40), Muted, TextAnchor.MiddleLeft);
            }

            Label(_panel, "Туз: старший в 10–J–Q–K–A, младший в A–2–3–4–5. Масти равны. При равной пятёрке банк делится.",
                21, new Vector2(0, -326), new Vector2(1390, 34), Cream, TextAnchor.MiddleLeft);
            Label(_panel, "Фолд — сбросить · Чек — без ставки · Колл — уравнять · Бет — поставить · Рейз / ререйз — повысить · Олл-ин — все фишки",
                20, new Vector2(0, -365), new Vector2(1390, 35), Muted, TextAnchor.MiddleLeft);
            Label(_panel, "Блайнды 10 / 20 перед раздачей → префлоп → флоп (3 карты) → тёрн (1) → ривер (1) → вскрытие",
                19, new Vector2(0, -396), new Vector2(1390, 30), Muted, TextAnchor.MiddleLeft);
            Resize();
        }

        private void CloseButton()
        {
            var rect = Box(_panel, "Close poker guide", new Vector2(604, 365), new Vector2(185, 52), new Color(0.21f, 0.31f, 0.26f));
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();
            button.onClick.AddListener(Close);
            Label(rect, "Закрыть", 23, Vector2.zero, new Vector2(185, 52), Cream, TextAnchor.MiddleCenter);
        }

        private static Card C(int rank, Suit suit) => new Card((Rank)rank, suit);

        private static RectTransform Box(Transform parent, string name, Vector2 position, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var image = go.GetComponent<Image>();
            image.sprite = UiTextures.RoundedRect(64, 64, 12, Color.white);
            image.type = Image.Type.Sliced;
            image.color = color;
            return rect;
        }

        private Text Label(Transform parent, string value, int size, Vector2 position, Vector2 bounds, Color color,
            TextAnchor alignment, FontStyle style = FontStyle.Normal)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = bounds;
            var text = go.GetComponent<Text>();
            text.font = _font;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.text = value;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }
    }
}
