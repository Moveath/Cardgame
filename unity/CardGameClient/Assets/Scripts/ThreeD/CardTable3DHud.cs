using System;
using System.Collections.Generic;
using CardGame.Core.Cards;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace CardGameClient.ThreeD
{
    /// <summary>Интерфейс поверх стола в том же виде, что у 2D-дурака: в шапке только чей ход, чисел и подсказок нет.</summary>
    public sealed class CardTable3DHud : MonoBehaviour
    {
        private static readonly Vector2 Middle = Vector2.one * .5f;
        private static readonly Color Cream = new Color(1f, .91f, .73f);
        private static readonly Color Active = new Color(1f, .85f, .3f);

        private sealed class SeatMark
        {
            public RectTransform Root, Chip;
            public Text Name, Role;
            public Image Fill, Icon;
        }

        private Canvas _canvas;
        private RectTransform _root;
        private Font _sign;
        private Text _status, _trumpName;
        private Image _trumpMedal;
        private Button _take, _pass, _transfer, _restart, _leave;
        private Text _passLabel;
        private SeatMark[] _seats = Array.Empty<SeatMark>();
        private RectTransform _gestureRow;
        private IReadOnlyList<string> _gestures = Array.Empty<string>();
        private Func<string, bool> _playGesture;

        public Canvas Canvas => _canvas;

        public void Build()
        {
            if (EventSystem.current == null)
                new GameObject("3D Table Event System", typeof(EventSystem)).AddComponent<InputSystemUIInputModule>().AssignDefaultActions();

            var canvasObject = new GameObject("3D Table HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            _canvas = canvasObject.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 30;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 820f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            _root = (RectTransform)canvasObject.transform;
            _sign = Resources.Load<Font>("Fonts/SignNarrow") ?? UiFonts.Body;

            var top = new Vector2(.5f, 1f);
            _leave = PlaqueButton(string.IsNullOrEmpty(CardTable3DRoomFlow.Current?.RoomId) ? "В КЛУБ" : "В ЛОББИ");

            var trump = Panel(_root, "Trump emblem", new Vector2(0f, 1f), new Vector2(278f, -47f), new Vector2(92f, 92f));
            var glow = Graphic(trump, "Trump glow", Middle, Vector2.zero, new Vector2(152f, 152f));
            glow.sprite = UiTextures.RadialGlow(128, new Color(1f, .8f, .35f));
            glow.color = new Color(1f, 1f, 1f, .6f);
            _trumpMedal = Graphic(trump, "Trump medal", Middle, Vector2.zero, new Vector2(92f, 92f));
            Label(trump, "КОЗЫРЬ", Middle, new Vector2(112f, 18f), new Vector2(124f, 30f), 25, Cream);
            _trumpName = Label(trump, "", Middle, new Vector2(112f, -16f), new Vector2(124f, 40f), 37, new Color(1f, .84f, .4f));

            var plate = Art("UI/Hub/Hud/plaque-hd", _root, top, new Vector2(0f, -36f), new Vector2(470f, 62f), 80f / 14f);
            plate.name = "Status plate";
            _status = Label(_root, "", top, new Vector2(0f, -35f), new Vector2(426f, 42f), 32, Cream);

            // Кнопки хода — в нижнем углу со стороны свободной руки: с другой стороны их закрывал бы веер (OwnArms3DView.FanSide).
            var corner = new Vector2(OwnArms3DView.FreeSide == "_R" ? 1f : 0f, 0f);
            var inward = OwnArms3DView.FreeSide == "_R" ? -170f : 170f;
            _pass = ActionButton("БИТО", "UI/Hub/Durak/plate-green", corner, new Vector2(inward, 204f));
            _passLabel = _pass.GetComponentInChildren<Text>();
            _take = ActionButton("БЕРУ", "UI/Hub/Hud/play-plate", corner, new Vector2(inward, 100f));
            _transfer = ActionButton("ПЕРЕВЕСТИ", "UI/Hub/Durak/plate-green", corner, new Vector2(inward, 204f));
            // Над серединой стола: ниже она ложилась прямо на кисти игрока, лежащие на сукне.
            _restart = ActionButton("ЕЩЁ ПАРТИЯ", "UI/Hub/Durak/plate-green", Middle, new Vector2(0f, 30f));
            SetActions(false, false, "БИТО", false, false);
        }

        /// <summary>Плашка «ЖЕСТЫ» справа сверху: открывает столбик жестов. На компьютере жесты идут и клавишами 1–6.</summary>
        public void BuildGestures(IReadOnlyList<string> gestures, Func<string, bool> play)
        {
            _gestures = gestures;
            _playGesture = play;
            var open = PlaqueButton("ЖЕСТЫ", new Vector2(1f, 1f), new Vector2(-123f, -46f));
            open.name = "Gestures";
            _gestureRow = Panel(_root, "Gesture list", new Vector2(1f, 1f), new Vector2(-123f, -100f - gestures.Count * 31f), new Vector2(190f, gestures.Count * 62f));
            for (var i = 0; i < gestures.Count; i++)
            {
                var gesture = gestures[i];
                var button = PlaqueButton(GestureCaption(gesture), new Vector2(.5f, 1f), new Vector2(0f, -31f - i * 62f), _gestureRow, new Vector2(190f, 58f), 26);
                button.name = "Gesture " + gesture;
                Bind(button, () => Gesture(gesture));
            }
            _gestureRow.gameObject.SetActive(false);
            Bind(open, () => _gestureRow.gameObject.SetActive(!_gestureRow.gameObject.activeSelf));
        }

        private void Gesture(string gesture)
        {
            if (_playGesture?.Invoke(gesture) ?? false) _gestureRow.gameObject.SetActive(false);
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || _gestures.Count == 0) return;
            for (var i = 0; i < Math.Min(6, _gestures.Count); i++)
                if (keyboard[Key.Digit1 + i].wasPressedThisFrame) Gesture(_gestures[i]);
        }

        private static string GestureCaption(string gesture)
        {
            switch (gesture)
            {
                case "Point": return "НА ТЕБЯ";
                case "ThumbsUp": return "КЛАСС";
                case "ThumbsDown": return "ФУ";
                case "TapTable": return "СТУК";
                case "Shrug": return "НЕ ЗНАЮ";
                case "Fist": return "КУЛАК";
                default: return gesture.ToUpperInvariant();
            }
        }

        public void SetCallbacks(Action take, Action pass, Action transfer, Action restart, Action leave)
        {
            Bind(_take, take);
            Bind(_pass, pass);
            Bind(_transfer, transfer);
            Bind(_restart, restart);
            Bind(_leave, leave);
        }

        public void SetStatus(string status, bool yours)
        {
            _status.text = status;
            _status.color = yours ? Active : Cream;
        }

        public void SetTrump(Suit suit)
        {
            _trumpMedal.sprite = Resources.Load<Sprite>("UI/Hub/Durak/trump-" + suit.ToString().ToLowerInvariant());
            _trumpName.text = suit == Suit.Spades ? "ПИКИ" : suit == Suit.Hearts ? "ЧЕРВИ" : suit == Suit.Diamonds ? "БУБНЫ" : "ТРЕФЫ";
        }

        public void SetActions(bool canTake, bool canPass, string passLabel, bool canTransfer, bool finished)
        {
            _take.gameObject.SetActive(canTake && !finished);
            // Перевод бывает только у защиты: «БИТО» в этот момент нет, кнопка встаёт на её место.
            _pass.gameObject.SetActive(canPass && !finished);
            _passLabel.text = passLabel;
            _transfer.gameObject.SetActive(canTransfer && !finished);
            _restart.gameObject.SetActive(finished);
        }

        public void BuildSeats(int count)
        {
            foreach (var seat in _seats) Destroy(seat.Root.gameObject);
            _seats = new SeatMark[count];
            for (var i = 0; i < count; i++)
            {
                var root = Panel(_root, "Seat mark " + i, Middle, Vector2.zero, new Vector2(176f, 76f));
                var plate = Art("UI/Hub/Hud/currency-plate", root, new Vector2(.5f, 1f), new Vector2(0f, -20f), new Vector2(168f, 40f), 109f / 40f);
                var name = Label(plate.transform, "", Middle, new Vector2(0f, 1f), new Vector2(138f, 32f), 28, Cream);
                name.resizeTextForBestFit = true; name.resizeTextMinSize = 18; name.resizeTextMaxSize = 28;
                var chip = Shape(root, "Role chip", new Vector2(.5f, 1f), new Vector2(0f, -57f), new Vector2(172f, 34f));
                var role = Label(chip.transform, "", Middle, new Vector2(15f, 0f), new Vector2(134f, 30f), 24, Color.white);
                role.font = UiFonts.Body;
                role.fontStyle = FontStyle.Bold;
                role.resizeTextForBestFit = true; role.resizeTextMinSize = 17; role.resizeTextMaxSize = 24;
                var icon = Graphic(chip.transform, "Role icon", new Vector2(0f, .5f), new Vector2(2f, 2f), new Vector2(56f, 56f));
                icon.preserveAspect = true;
                _seats[i] = new SeatMark { Root = root, Chip = chip.rectTransform, Name = name, Role = role, Fill = chip, Icon = icon };
            }
        }

        /// <summary>Плашка над головой соперника. kind: 0 — без значка, 1 — атака, 2 — защита.</summary>
        public void SetSeat(int index, string name, string role, int kind, bool acting)
        {
            var seat = _seats[index];
            seat.Name.text = name;
            seat.Name.color = acting ? Active : Cream;
            seat.Chip.gameObject.SetActive(role.Length > 0);
            seat.Role.text = role;
            seat.Fill.color = kind == 2 ? new Color(.09f, .35f, .74f) : kind == 1 ? new Color(.78f, .2f, .05f) : new Color(.06f, .03f, .025f, .92f);
            seat.Icon.enabled = kind != 0;
            if (kind != 0) seat.Icon.sprite = Resources.Load<Sprite>("UI/Hub/Durak/" + (kind == 2 ? "role-defend" : "role-attack"));
        }

        private const float LabelTop = .85f;

        public void PlaceSeat(int index, Camera camera, Vector3 world)
        {
            var seat = _seats[index];
            var point = camera.WorldToViewportPoint(world);
            var visible = point.z > 0f && point.x > -.05f && point.x < 1.05f;
            seat.Root.gameObject.SetActive(visible);
            // Камера смотрит на стол, головы соперников у верхнего края: табличка не поднимается выше LabelTop, под плашки «В КЛУБ» и «ВАШ ХОД».
            if (visible) seat.Root.anchoredPosition = new Vector2((point.x - .5f) * _root.rect.width, (Mathf.Min(point.y, LabelTop) - .5f) * _root.rect.height);
        }

        private Button PlaqueButton(string caption) => PlaqueButton(caption, new Vector2(0f, 1f), new Vector2(123f, -46f));

        private Button PlaqueButton(string caption, Vector2 anchor, Vector2 at, Transform parent = null, Vector2? size = null, int fontSize = 28)
        {
            var box = size ?? new Vector2(190f, 72f);
            var art = Art("UI/Hub/Hud/plaque-hd", parent ?? _root, anchor, at, box, 80f / 14f);
            art.raycastTarget = true;
            var button = art.gameObject.AddComponent<Button>();
            button.targetGraphic = art;
            Label(art.transform, caption, Middle, Vector2.zero, box - new Vector2(28f, 12f), fontSize, Cream);
            return button;
        }

        private Button ActionButton(string caption, string art, Vector2 anchor, Vector2 at)
        {
            var size = new Vector2(270f, 96f);
            var plate = Art(art, _root, anchor, at, size, 176f / size.y);
            plate.name = caption;
            plate.raycastTarget = true;
            var button = plate.gameObject.AddComponent<Button>();
            button.targetGraphic = plate;
            var text = Label(plate.transform, caption, Middle, new Vector2(0f, 1f), size - new Vector2(48f, 20f), 40, new Color(1f, .95f, .8f));
            text.gameObject.AddComponent<Outline>().effectColor = new Color(.12f, .05f, .02f, .8f);
            return button;
        }

        private static void Bind(Button button, Action action)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => action?.Invoke());
        }

        private static RectTransform Panel(Transform parent, string name, Vector2 anchor, Vector2 at, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.anchoredPosition = at;
            rect.sizeDelta = size;
            return rect;
        }

        private static Image Graphic(Transform parent, string name, Vector2 anchor, Vector2 at, Vector2 size)
        {
            var image = Panel(parent, name, anchor, at, size).gameObject.AddComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        private static Image Shape(Transform parent, string name, Vector2 anchor, Vector2 at, Vector2 size)
        {
            var image = Graphic(parent, name, anchor, at, size);
            image.sprite = UiTextures.RoundedRect(64, 64, 18, Color.white);
            image.type = Image.Type.Sliced;
            return image;
        }

        private static Image Art(string key, Transform parent, Vector2 anchor, Vector2 at, Vector2 size, float density)
        {
            var image = Graphic(parent, key, anchor, at, size);
            image.sprite = Resources.Load<Sprite>(key);
            image.type = image.sprite != null && image.sprite.border.sqrMagnitude > 0 ? Image.Type.Sliced : Image.Type.Simple;
            image.pixelsPerUnitMultiplier = density;
            return image;
        }

        private Text Label(Transform parent, string value, Vector2 anchor, Vector2 at, Vector2 size, int fontSize, Color color)
        {
            var go = Panel(parent, "Label", anchor, at, size).gameObject;
            var text = go.AddComponent<Text>();
            text.font = _sign;
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = color;
            text.text = value;
            text.supportRichText = false;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }
    }
}
