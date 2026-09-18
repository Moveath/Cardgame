using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>
    /// Навигация между экранами: главное меню -> выбор игры -> настройка комнаты -> сама партия.
    /// Пока в игре только дурак, поэтому "выбор игры" содержит один пункт.
    /// </summary>
    public sealed class AppFlowController : MonoBehaviour
    {
        private static readonly Color AccentGlow = new Color(1f, 0.85f, 0.3f);
        private static readonly Color PanelColor = new Color(0.36f, 0.22f, 0.12f);
        private static readonly Color TextColor = new Color(0.94f, 0.95f, 0.93f);
        private static readonly Color DimTextColor = new Color(0.94f, 0.95f, 0.93f, 0.35f);

        private Font _font;
        private RectTransform _root;

        private GameObject _background;
        private GameObject _mainMenuScreen;
        private GameObject _modeSelectScreen;
        private GameObject _lobbyScreen;
        private GameObject _gameScreen;

        private DurakMatchSettings _settings = DurakMatchSettings.Default;

        private readonly List<Button> _deckButtons = new List<Button>();
        private readonly List<Image> _deckButtonImages = new List<Image>();
        private readonly List<Button> _playerCountButtons = new List<Button>();
        private readonly List<Image> _playerCountButtonImages = new List<Image>();
        private Text _seatsSummary;

        private void Start()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _root = (RectTransform)transform;

            BuildBackground();
            BuildMainMenu();
            BuildModeSelect();
            BuildLobby();

            ShowScreen(_mainMenuScreen);
        }

        // ---------- Общий фон меню (тот же стиль, что и стол) ----------

        private void BuildBackground()
        {
            _background = new GameObject("MenuBackground", typeof(RectTransform));
            _background.transform.SetParent(_root, false);
            Stretch((RectTransform)_background.transform);

            var room = new GameObject("Room", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            room.transform.SetParent(_background.transform, false);
            Stretch((RectTransform)room.transform);
            room.GetComponent<Image>().color = new Color(0.11f, 0.08f, 0.07f);

            var frame = new GameObject("Frame", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            frame.transform.SetParent(_background.transform, false);
            var frameRect = (RectTransform)frame.transform;
            frameRect.anchorMin = Vector2.zero;
            frameRect.anchorMax = Vector2.one;
            frameRect.offsetMin = new Vector2(24f, 24f);
            frameRect.offsetMax = new Vector2(-24f, -24f);
            var frameImage = frame.GetComponent<Image>();
            frameImage.sprite = UiTextures.RoundedRect(64, 64, 24f, PanelColor);
            frameImage.type = Image.Type.Sliced;

            var felt = new GameObject("Felt", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            felt.transform.SetParent(_background.transform, false);
            var feltRect = (RectTransform)felt.transform;
            feltRect.anchorMin = Vector2.zero;
            feltRect.anchorMax = Vector2.one;
            feltRect.offsetMin = new Vector2(38f, 38f);
            feltRect.offsetMax = new Vector2(-38f, -38f);
            var feltImage = felt.GetComponent<Image>();
            feltImage.sprite = UiTextures.RoundedRect(64, 64, 20f, new Color(0.08f, 0.32f, 0.20f));
            feltImage.type = Image.Type.Sliced;
        }

        // ---------- Главное меню ----------

        private void BuildMainMenu()
        {
            _mainMenuScreen = new GameObject("MainMenu", typeof(RectTransform));
            _mainMenuScreen.transform.SetParent(_root, false);
            Stretch((RectTransform)_mainMenuScreen.transform);

            var title = CreateText(_mainMenuScreen.transform, "Дурак", 96, TextAnchor.MiddleCenter);
            title.fontStyle = FontStyle.Bold;
            Place((RectTransform)title.transform, new Vector2(0.5f, 0.6f), Vector2.zero, new Vector2(900f, 140f));

            CreateButton(_mainMenuScreen.transform, "Играть", new Vector2(0.5f, 0.5f), new Vector2(0f, -60f),
                new Color(0.21f, 0.45f, 0.29f), () => ShowScreen(_modeSelectScreen));
        }

        // ---------- Выбор игры ----------

        private void BuildModeSelect()
        {
            _modeSelectScreen = new GameObject("ModeSelect", typeof(RectTransform));
            _modeSelectScreen.transform.SetParent(_root, false);
            Stretch((RectTransform)_modeSelectScreen.transform);
            _modeSelectScreen.SetActive(false);

            var title = CreateText(_modeSelectScreen.transform, "Выбери игру", 56, TextAnchor.MiddleCenter);
            Place((RectTransform)title.transform, new Vector2(0.5f, 0.8f), Vector2.zero, new Vector2(900f, 90f));

            CreateButton(_modeSelectScreen.transform, "Дурак", new Vector2(0.5f, 0.5f), new Vector2(0f, 0f),
                new Color(0.21f, 0.45f, 0.29f), () => ShowScreen(_lobbyScreen));

            CreateButton(_modeSelectScreen.transform, "Назад", new Vector2(0f, 0f), new Vector2(160f, 90f),
                new Color(0.3f, 0.3f, 0.32f), () => ShowScreen(_mainMenuScreen));
        }

        // ---------- Настройка комнаты ----------

        private void BuildLobby()
        {
            _lobbyScreen = new GameObject("Lobby", typeof(RectTransform));
            _lobbyScreen.transform.SetParent(_root, false);
            Stretch((RectTransform)_lobbyScreen.transform);
            _lobbyScreen.SetActive(false);

            var title = CreateText(_lobbyScreen.transform, "Настройка комнаты — дурак", 44, TextAnchor.MiddleCenter);
            Place((RectTransform)title.transform, new Vector2(0.5f, 0.88f), Vector2.zero, new Vector2(1100f, 70f));

            // Ряд размеров колоды.
            var deckLabel = CreateText(_lobbyScreen.transform, "Колода", 28, TextAnchor.MiddleCenter);
            Place((RectTransform)deckLabel.transform, new Vector2(0.5f, 0.72f), Vector2.zero, new Vector2(400f, 40f));

            var deckOptions = new[] { DeckKind.Twenty4, DeckKind.ThirtySix, DeckKind.FiftyTwo, DeckKind.FiftyFour };
            var deckLabels = new[] { "24", "36", "52", "54" };
            var deckSpacing = 170f;
            var deckStartX = -(deckOptions.Length - 1) * deckSpacing / 2f;

            for (var i = 0; i < deckOptions.Length; i++)
            {
                var option = deckOptions[i];
                var x = deckStartX + i * deckSpacing;
                var image = CreateChoiceButton(_lobbyScreen.transform, deckLabels[i], () => SelectDeck(option), out var button);
                Place((RectTransform)button.transform, new Vector2(0.5f, 0.62f), new Vector2(x, 0f), new Vector2(150f, 90f));
                _deckButtons.Add(button);
                _deckButtonImages.Add(image);
            }

            // Ряд количества игроков.
            var playersLabel = CreateText(_lobbyScreen.transform, "Игроков за столом", 28, TextAnchor.MiddleCenter);
            Place((RectTransform)playersLabel.transform, new Vector2(0.5f, 0.46f), Vector2.zero, new Vector2(500f, 40f));

            var playerSpacing = 100f;
            var playerStartX = -(8 - 2) * playerSpacing / 2f;

            for (var count = 2; count <= 8; count++)
            {
                var captured = count;
                var x = playerStartX + (count - 2) * playerSpacing;
                var image = CreateChoiceButton(_lobbyScreen.transform, count.ToString(), () => SelectPlayerCount(captured), out var button);
                Place((RectTransform)button.transform, new Vector2(0.5f, 0.36f), new Vector2(x, 0f), new Vector2(84f, 84f));
                _playerCountButtons.Add(button);
                _playerCountButtonImages.Add(image);
            }

            _seatsSummary = CreateText(_lobbyScreen.transform, string.Empty, 26, TextAnchor.MiddleCenter);
            _seatsSummary.color = new Color(0.85f, 0.92f, 0.85f, 0.9f);
            Place((RectTransform)_seatsSummary.transform, new Vector2(0.5f, 0.22f), Vector2.zero, new Vector2(700f, 40f));

            CreateButton(_lobbyScreen.transform, "Играть", new Vector2(0.5f, 0f), new Vector2(0f, 110f),
                new Color(0.21f, 0.45f, 0.29f), StartMatch);

            CreateButton(_lobbyScreen.transform, "Назад", new Vector2(0f, 0f), new Vector2(160f, 90f),
                new Color(0.3f, 0.3f, 0.32f), () => ShowScreen(_modeSelectScreen));

            RefreshLobbySelection();
        }

        private void SelectDeck(DeckKind deck)
        {
            _settings.DeckSize = deck;

            var max = DurakMatchSettings.MaxPlayersFor(deck);
            if (_settings.PlayerCount > max)
            {
                _settings.PlayerCount = max;
            }

            RefreshLobbySelection();
        }

        private void SelectPlayerCount(int count)
        {
            _settings.PlayerCount = count;

            // Текущая колода не вмещает столько игроков — переключаемся на минимальную, которая вмещает.
            if (DurakMatchSettings.MaxPlayersFor(_settings.DeckSize) < count)
            {
                if (count <= DurakMatchSettings.MaxPlayersFor(DeckKind.Twenty4))
                {
                    _settings.DeckSize = DeckKind.Twenty4;
                }
                else if (count <= DurakMatchSettings.MaxPlayersFor(DeckKind.ThirtySix))
                {
                    _settings.DeckSize = DeckKind.ThirtySix;
                }
                else
                {
                    _settings.DeckSize = DeckKind.FiftyTwo;
                }
            }

            RefreshLobbySelection();
        }

        private void RefreshLobbySelection()
        {
            var deckOptions = new[] { DeckKind.Twenty4, DeckKind.ThirtySix, DeckKind.FiftyTwo, DeckKind.FiftyFour };
            for (var i = 0; i < deckOptions.Length; i++)
            {
                var isValid = DurakMatchSettings.MaxPlayersFor(deckOptions[i]) >= _settings.PlayerCount;
                var isSelected = _settings.DeckSize == deckOptions[i];
                SetChoiceState(_deckButtons[i], _deckButtonImages[i], isValid, isSelected);
            }

            var max = DurakMatchSettings.MaxPlayersFor(_settings.DeckSize);
            for (var count = 2; count <= 8; count++)
            {
                var index = count - 2;
                var isValid = count <= max;
                var isSelected = _settings.PlayerCount == count;
                SetChoiceState(_playerCountButtons[index], _playerCountButtonImages[index], isValid, isSelected);
            }

            var botCount = _settings.PlayerCount - 1;
            _seatsSummary.text = "За столом: ты + " + botCount + (botCount == 1 ? " бот" : botCount < 5 ? " бота" : " ботов")
                + "  ·  карт в колоде: " + DurakMatchSettings.CardCount(_settings.DeckSize);
        }

        private static void SetChoiceState(Button button, Image image, bool isValid, bool isSelected)
        {
            button.interactable = isValid;

            var outline = button.GetComponent<Outline>();
            outline.enabled = isSelected;

            var color = PanelColor;
            var alpha = isValid ? 1f : 0.35f;
            image.color = new Color(color.r, color.g, color.b, alpha);

            var label = button.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.color = isValid ? TextColor : DimTextColor;
            }
        }

        private void StartMatch()
        {
            BuildGameScreen();
            ShowScreen(_gameScreen);
        }

        private void BuildGameScreen()
        {
            if (_gameScreen != null)
            {
                Destroy(_gameScreen);
            }

            _gameScreen = new GameObject("GameScreen", typeof(RectTransform));
            _gameScreen.transform.SetParent(_root, false);
            Stretch((RectTransform)_gameScreen.transform);
            _gameScreen.SetActive(false);

            var table = _gameScreen.AddComponent<DurakTableController>();
            table.Configure(_settings, () => ShowScreen(_lobbyScreen));
        }

        private void ShowScreen(GameObject screen)
        {
            _background.SetActive(screen != _gameScreen);
            _mainMenuScreen.SetActive(screen == _mainMenuScreen);
            _modeSelectScreen.SetActive(screen == _modeSelectScreen);
            _lobbyScreen.SetActive(screen == _lobbyScreen);

            if (_gameScreen != null)
            {
                _gameScreen.SetActive(screen == _gameScreen);
            }

            if (screen == _lobbyScreen)
            {
                RefreshLobbySelection();
            }
        }

        // ---------- Мелкие переиспользуемые конструкторы UI ----------

        private Image CreateChoiceButton(Transform parent, string label, Action onClick, out Button button)
        {
            var go = new GameObject("Choice", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline), typeof(Button));
            go.transform.SetParent(parent, false);

            // Спрайт белый, а нужный оттенок задаёт Image.color — так SetChoiceState может менять
            // прозрачность без риска умножить цвет сам на себя и получить грязный оттенок.
            var image = go.GetComponent<Image>();
            image.sprite = UiTextures.RoundedRect(64, 64, 14f, Color.white);
            image.color = PanelColor;
            image.type = Image.Type.Sliced;

            var outline = go.GetComponent<Outline>();
            outline.effectColor = AccentGlow;
            outline.effectDistance = new Vector2(3f, 3f);
            outline.enabled = false;

            button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            colors.disabledColor = new Color(1f, 1f, 1f, 1f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.onClick.AddListener(() => onClick());

            var text = CreateText(go.transform, label, 30, TextAnchor.MiddleCenter);
            Stretch((RectTransform)text.transform);

            return image;
        }

        private Button CreateButton(Transform parent, string label, Vector2 anchor, Vector2 position, Color color, Action onClick)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Shadow), typeof(Button));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(280f, 76f);

            var image = go.GetComponent<Image>();
            image.sprite = UiTextures.RoundedRect(140, 76, 18f, color);
            image.type = Image.Type.Sliced;

            var shadow = go.GetComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.4f);
            shadow.effectDistance = new Vector2(3f, -4f);

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.onClick.AddListener(() => onClick());

            var text = CreateText(rect, label, 30, TextAnchor.MiddleCenter);
            Stretch((RectTransform)text.transform);

            return button;
        }

        private Text CreateText(Transform parent, string content, int fontSize, TextAnchor anchor)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);

            var text = go.GetComponent<Text>();
            text.font = _font;
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.color = TextColor;
            text.text = content;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            return text;
        }

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
