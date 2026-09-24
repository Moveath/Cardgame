using System;
using System.Collections.Generic;
using CardGame.Core.Games.Durak;
using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>
    /// Навигация между экранами: главное меню -> выбор игры -> настройка комнаты -> сама партия.
    /// Игр две, и у каждой своя комната: наборы настроек не пересекаются совсем, поэтому
    /// общий экран с половиной погашенных строк был бы только путаницей.
    /// </summary>
    public sealed class AppFlowController : MonoBehaviour
    {
        private static readonly Color AccentGlow = new Color(1f, 0.85f, 0.3f);
        private static readonly Color PanelColor = new Color(0.36f, 0.22f, 0.12f);
        private static readonly Color TextColor = new Color(0.94f, 0.95f, 0.93f);
        private static readonly Color DimTextColor = new Color(0.94f, 0.95f, 0.93f, 0.35f);

        // Подложка ячейки: чуть светлее сукна, чтобы граница читалась, но не спорила с портретом.
        private static readonly Color CellColor = new Color(0.10f, 0.26f, 0.18f);

        private Font _font;
        private RectTransform _root;

        private GameObject _background;
        private GameObject _mainMenuScreen;
        private GameObject _modeSelectScreen;
        private GameObject _durakLobbyScreen;
        private GameObject _bluffLobbyScreen;
        private GameObject _avatarScreen;
        private GameObject _gameScreen;

        // Галерея аватаров: первый уровень — наборы, второй — образы внутри набора.
        private RectTransform _avatarGroupsPanel;
        private RectTransform _avatarItemsPanel;
        private readonly List<Button> _avatarGroupButtons = new List<Button>();
        private readonly List<string> _avatarGroupIds = new List<string>();
        private readonly List<Button> _avatarButtons = new List<Button>();
        private readonly List<string> _avatarButtonNames = new List<string>();

        private DurakMatchSettings _settings = DurakMatchSettings.Default;
        private BluffMatchSettings _bluffSettings = BluffMatchSettings.Default;

        private readonly List<Button> _deckButtons = new List<Button>();
        private readonly List<Image> _deckButtonImages = new List<Image>();
        private readonly List<Button> _playerCountButtons = new List<Button>();
        private readonly List<Image> _playerCountButtonImages = new List<Image>();

        private readonly List<Button> _bluffDeckButtons = new List<Button>();
        private readonly List<Image> _bluffDeckButtonImages = new List<Image>();
        private readonly List<Button> _bluffPlayerButtons = new List<Button>();
        private readonly List<Image> _bluffPlayerButtonImages = new List<Image>();
        private readonly Button[] _bluffShootoutButtons = new Button[2];
        private readonly Image[] _bluffShootoutButtonImages = new Image[2];

        // Каждый переключатель — ровно два варианта, поэтому пара кнопок и пара картинок на опцию.
        private readonly Button[] _transferButtons = new Button[2];
        private readonly Image[] _transferButtonImages = new Image[2];
        private readonly Button[] _throwInButtons = new Button[2];
        private readonly Image[] _throwInButtonImages = new Image[2];
        private readonly Button[] _cheatButtons = new Button[2];
        private readonly Image[] _cheatButtonImages = new Image[2];

        private Text _seatsSummary;
        private Text _bluffSeatsSummary;

        private void Start()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _root = (RectTransform)transform;

            BuildBackground();
            BuildMainMenu();
            BuildModeSelect();
            BuildLobby();
            BuildBluffLobby();
            BuildAvatarPicker();

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

            // Игр стало две, поэтому на главной больше не название одной из них.
            // Имя проекта временное — меняется этой строкой.
            var title = CreateText(_mainMenuScreen.transform, "Карточный стол", 96, TextAnchor.MiddleCenter);
            title.fontStyle = FontStyle.Bold;
            Place((RectTransform)title.transform, new Vector2(0.5f, 0.6f), Vector2.zero, new Vector2(900f, 140f));

            CreateButton(_mainMenuScreen.transform, "Играть", new Vector2(0.5f, 0.5f), new Vector2(0f, -60f),
                new Color(0.21f, 0.45f, 0.29f), () => ShowScreen(_modeSelectScreen));

            CreateButton(_mainMenuScreen.transform, "Аватар", new Vector2(0.5f, 0.5f), new Vector2(0f, -160f),
                new Color(0.3f, 0.3f, 0.32f), () => ShowScreen(_avatarScreen));
        }

        // ---------- Выбор аватара ----------

        /// <summary>
        /// Галерея в два шага: сначала выбираешь, кто это будет, потом — в каком образе.
        /// Одним экраном не обойтись: людей уже пятеро, и если вывалить все их костюмы
        /// сразу, портреты ужимаются до размера, на котором лица не различить.
        /// </summary>
        private void BuildAvatarPicker()
        {
            _avatarScreen = new GameObject("AvatarPicker", typeof(RectTransform));
            _avatarScreen.transform.SetParent(_root, false);
            Stretch((RectTransform)_avatarScreen.transform);
            _avatarScreen.SetActive(false);

            _avatarGroupsPanel = new GameObject("Groups", typeof(RectTransform)).GetComponent<RectTransform>();
            _avatarGroupsPanel.SetParent(_avatarScreen.transform, false);
            Stretch(_avatarGroupsPanel);

            _avatarItemsPanel = new GameObject("Items", typeof(RectTransform)).GetComponent<RectTransform>();
            _avatarItemsPanel.SetParent(_avatarScreen.transform, false);
            Stretch(_avatarItemsPanel);
            _avatarItemsPanel.gameObject.SetActive(false);

            // Сетку наборов здесь не строим: в Start холст ещё не знает своей настоящей
            // ширины, и ячейки выходили мелкими до первого возврата из набора. Её собирает
            // ShowScreen при каждом входе в галерею.
        }

        private void BuildAvatarGroups()
        {
            var title = CreateText(_avatarGroupsPanel, "Выбери аватар", 40, TextAnchor.MiddleCenter);
            PlaceFromTop((RectTransform)title.transform, 0f, 56f, new Vector2(1100f, 60f));

            var groups = AvatarCatalog.Groups;
            if (groups.Count == 0)
            {
                var empty = CreateText(_avatarGroupsPanel, "Картинок нет — положи их в Resources/Avatars", 26, TextAnchor.MiddleCenter);
                PlaceFromTop((RectTransform)empty.transform, 0f, 220f, new Vector2(1100f, 40f));
            }

            var previews = new List<Sprite>();
            var shown = new List<AvatarCatalog.AvatarGroup>();
            foreach (var group in groups)
            {
                if (group.Sprites.Count == 0)
                {
                    continue;
                }

                previews.Add(PreviewOf(group));
                shown.Add(group);
            }

            var grid = MeasureGrid(previews, captionHeight: 40f);

            for (var i = 0; i < shown.Count; i++)
            {
                var group = shown[i];
                var captured = group;
                var position = grid.CellTopLeft(i);
                var button = CreateAvatarCell(_avatarGroupsPanel, previews[i], position, grid.CellSize,
                    grid.PortraitScale, () => OpenAvatarGroup(captured));

                var caption = CreateText(_avatarGroupsPanel, group.Title, 24, TextAnchor.MiddleCenter);
                PlaceFromTop((RectTransform)caption.transform, position.x, position.y + grid.CellSize.y + 6f,
                    new Vector2(grid.CellSize.x + 12f, 30f));

                _avatarGroupButtons.Add(button);
                _avatarGroupIds.Add(group.Id);
            }

            CreateButton(_avatarGroupsPanel, "Назад", new Vector2(0f, 0f), new Vector2(160f, 90f),
                new Color(0.3f, 0.3f, 0.32f), () => ShowScreen(_mainMenuScreen));

            RefreshGroupSelection();
        }

        /// <summary>Превью набора — уже выбранный аватар, если он отсюда, иначе просто первый.</summary>
        private static Sprite PreviewOf(AvatarCatalog.AvatarGroup group)
        {
            var chosen = PlayerProfile.AvatarName;
            foreach (var sprite in group.Sprites)
            {
                if (sprite.name == chosen)
                {
                    return sprite;
                }
            }

            return group.Sprites[0];
        }

        private void OpenAvatarGroup(AvatarCatalog.AvatarGroup group)
        {
            Clear(_avatarItemsPanel);
            _avatarButtons.Clear();
            _avatarButtonNames.Clear();

            var title = CreateText(_avatarItemsPanel, group.Title, 40, TextAnchor.MiddleCenter);
            PlaceFromTop((RectTransform)title.transform, 0f, 56f, new Vector2(1100f, 60f));

            var grid = MeasureGrid(group.Sprites, captionHeight: 0f);

            for (var i = 0; i < group.Sprites.Count; i++)
            {
                var sprite = group.Sprites[i];
                var button = CreateAvatarCell(_avatarItemsPanel, sprite, grid.CellTopLeft(i), grid.CellSize,
                    grid.PortraitScale, () => SelectAvatar(sprite.name));

                _avatarButtons.Add(button);
                _avatarButtonNames.Add(sprite.name);
            }

            CreateButton(_avatarItemsPanel, "Назад", new Vector2(0f, 0f), new Vector2(160f, 90f),
                new Color(0.3f, 0.3f, 0.32f), ShowAvatarGroups);

            RefreshAvatarSelection();

            _avatarGroupsPanel.gameObject.SetActive(false);
            _avatarItemsPanel.gameObject.SetActive(true);
        }

        private void ShowAvatarGroups()
        {
            // Превью набора могло смениться вместе с выбором — пересобираем список.
            Clear(_avatarGroupsPanel);
            _avatarGroupButtons.Clear();
            _avatarGroupIds.Clear();
            BuildAvatarGroups();

            _avatarItemsPanel.gameObject.SetActive(false);
            _avatarGroupsPanel.gameObject.SetActive(true);
        }

        private void RefreshGroupSelection()
        {
            var chosen = PlayerProfile.AvatarName;
            for (var i = 0; i < _avatarGroupButtons.Count; i++)
            {
                var owns = false;
                foreach (var group in AvatarCatalog.Groups)
                {
                    if (group.Id != _avatarGroupIds[i])
                    {
                        continue;
                    }

                    foreach (var sprite in group.Sprites)
                    {
                        if (sprite.name == chosen)
                        {
                            owns = true;
                            break;
                        }
                    }
                }

                _avatarGroupButtons[i].GetComponent<Outline>().enabled = owns;
            }
        }

        /// <summary>Разметка сетки: сколько столбцов, какого размера ячейка и где какая стоит.</summary>
        private readonly struct AvatarGrid
        {
            public AvatarGrid(int columns, Vector2 cellSize, float step, float pitch, float portraitScale)
            {
                Columns = columns;
                CellSize = cellSize;
                Step = step;
                Pitch = pitch;
                PortraitScale = portraitScale;
            }

            public int Columns { get; }
            public Vector2 CellSize { get; }

            /// <summary>Сколько экранных пикселей приходится на пиксель картинки. Общий для
            /// всей сетки: только так головы у всех получаются одного размера.</summary>
            public float PortraitScale { get; }

            private float Step { get; }
            private float Pitch { get; }

            public Vector2 CellTopLeft(int index)
            {
                var column = index % Columns;
                var row = index / Columns;
                var startX = -(Columns - 1) * Step / 2f;
                return new Vector2(startX + column * Step, GridTop + row * Pitch);
            }
        }

        private const float GridTop = 150f;
        private const float GridBottomLimit = 720f;
        private const float PortraitInsetX = 16f;
        private const float PortraitInsetY = 12f;

        /// <summary>Насколько плечам позволено вылезти за ячейку, прежде чем масштаб
        /// придётся уменьшить. Немного подрезать самого широкого дешевле, чем измельчить всех.</summary>
        private const float MaxPortraitOverflow = 1.3f;

        private AvatarGrid MeasureGrid(List<Sprite> sprites, float captionHeight)
        {
            var count = sprites.Count;
            var columns = Mathf.Max(1, Mathf.Min(6, count));
            var rows = Mathf.Max(1, Mathf.CeilToInt(count / (float)columns));

            var available = Mathf.Min(1500f, _root.rect.width - 240f);
            var step = Mathf.Min(230f, available / columns);
            var cellWidth = step - 14f;

            // Высота подгоняется так, чтобы все ряды вместе с подписями влезли между
            // заголовком и кнопкой "Назад" — иначе при десятке наборов сетка уезжает вниз.
            var pitch = Mathf.Min(cellWidth * 1.15f + captionHeight + 16f, (GridBottomLimit - GridTop) / rows);
            var cellHeight = pitch - captionHeight - 16f;

            // Масштаб один на всю сетку. Если вписывать каждый портрет в ячейку по
            // отдельности, широкий персонаж ужимается и выглядит мельче остальных —
            // именно так и получилось с Ельнуром. Общий масштаб держит головы вровень,
            // а широким плечам разрешаем вылезти за ячейку: их подрежет маска.
            var tallest = 1f;
            var widest = 1f;
            foreach (var sprite in sprites)
            {
                tallest = Mathf.Max(tallest, sprite.rect.height);
                widest = Mathf.Max(widest, sprite.rect.width);
            }

            var scale = (cellHeight - PortraitInsetY) / tallest;
            scale = Mathf.Min(scale, (cellWidth - PortraitInsetX) * MaxPortraitOverflow / widest);

            return new AvatarGrid(columns, new Vector2(cellWidth, cellHeight), step, pitch, scale);
        }

        /// <summary>
        /// Ячейка сетки: подложка-рамка, а внутри портрет, посаженный на нижний край.
        /// Именно нижний: портреты разной высоты, и если центрировать, в сетке они
        /// «плавают» — кто выше, кто ниже. По общей линии плеч ряд читается ровным.
        /// </summary>
        private Button CreateAvatarCell(Transform parent, Sprite sprite, Vector2 topLeft, Vector2 cellSize,
            float portraitScale, Action onClick)
        {
            var go = new GameObject("AvatarCell", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline), typeof(Button));
            go.transform.SetParent(parent, false);
            PlaceFromTop((RectTransform)go.transform, topLeft.x, topLeft.y, cellSize);

            var frame = go.GetComponent<Image>();
            frame.sprite = UiTextures.RoundedRect(64, 64, 16f, Color.white);
            frame.type = Image.Type.Sliced;
            frame.color = CellColor;

            var outline = go.GetComponent<Outline>();
            outline.effectColor = AccentGlow;
            outline.effectDistance = new Vector2(4f, 4f);
            outline.enabled = false;

            var button = go.GetComponent<Button>();
            button.targetGraphic = frame;
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.onClick.AddListener(() => onClick());

            // Портрет живёт в маске: при общем масштабе самый широкий персонаж шире ячейки,
            // и без маски его плечи залезли бы на соседей. Маска висит на отдельном объекте,
            // а не на самой ячейке, иначе она срезала бы и золотую обводку выбора.
            var clip = new GameObject("Clip", typeof(RectTransform), typeof(RectMask2D));
            clip.transform.SetParent(go.transform, false);

            var clipRect = (RectTransform)clip.transform;
            clipRect.anchorMin = Vector2.zero;
            clipRect.anchorMax = Vector2.one;
            clipRect.offsetMin = new Vector2(PortraitInsetX / 2f, 6f);
            clipRect.offsetMax = new Vector2(-PortraitInsetX / 2f, -PortraitInsetY / 2f);

            var portrait = new GameObject("Portrait", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            portrait.transform.SetParent(clip.transform, false);

            // Размер считаем сами: preserveAspect центрировал бы картинку в ячейке,
            // а нам нужно прижать её к низу, по общей линии плеч.
            var portraitRect = (RectTransform)portrait.transform;
            portraitRect.anchorMin = portraitRect.anchorMax = new Vector2(0.5f, 0f);
            portraitRect.pivot = new Vector2(0.5f, 0f);
            portraitRect.anchoredPosition = Vector2.zero;
            portraitRect.sizeDelta = new Vector2(sprite.rect.width * portraitScale, sprite.rect.height * portraitScale);

            var portraitImage = portrait.GetComponent<Image>();
            portraitImage.sprite = sprite;
            portraitImage.raycastTarget = false;

            return button;
        }

        private void SelectAvatar(string spriteName)
        {
            PlayerProfile.AvatarName = spriteName;
            RefreshAvatarSelection();
        }

        private static void Clear(Transform parent)
        {
            for (var i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i);
                child.SetParent(null, false);
                Destroy(child.gameObject);
            }
        }

        private void RefreshAvatarSelection()
        {
            var chosen = PlayerProfile.AvatarName;
            for (var i = 0; i < _avatarButtons.Count; i++)
            {
                var isChosen = _avatarButtonNames[i] == chosen;
                _avatarButtons[i].GetComponent<Outline>().enabled = isChosen;

                var colors = _avatarButtons[i].colors;
                colors.normalColor = isChosen ? Color.white : new Color(0.72f, 0.72f, 0.72f, 1f);
                _avatarButtons[i].colors = colors;
            }
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

            // Две игры рядом, у каждой своя комната. Кнопка ведёт не в партию, а в настройку:
            // колода и число игроков у них разные, и выбирать их надо до раздачи.
            CreateButton(_modeSelectScreen.transform, "Дурак", new Vector2(0.5f, 0.5f), new Vector2(-165f, 0f),
                new Color(0.21f, 0.45f, 0.29f), () => ShowScreen(_durakLobbyScreen));

            CreateButton(_modeSelectScreen.transform, "Верю — не верю", new Vector2(0.5f, 0.5f), new Vector2(165f, 0f),
                new Color(0.42f, 0.26f, 0.45f), () => ShowScreen(_bluffLobbyScreen));

            var durakHint = CreateText(_modeSelectScreen.transform, "классика на 2–8", 22, TextAnchor.MiddleCenter);
            durakHint.color = new Color(0.85f, 0.92f, 0.85f, 0.75f);
            Place((RectTransform)durakHint.transform, new Vector2(0.5f, 0.5f), new Vector2(-165f, -62f), new Vector2(300f, 30f));

            var bluffHint = CreateText(_modeSelectScreen.transform, "блеф и проверки", 22, TextAnchor.MiddleCenter);
            bluffHint.color = new Color(0.85f, 0.92f, 0.85f, 0.75f);
            Place((RectTransform)bluffHint.transform, new Vector2(0.5f, 0.5f), new Vector2(165f, -62f), new Vector2(300f, 30f));

            CreateButton(_modeSelectScreen.transform, "Назад", new Vector2(0f, 0f), new Vector2(160f, 90f),
                new Color(0.3f, 0.3f, 0.32f), () => ShowScreen(_mainMenuScreen));
        }

        // ---------- Настройка комнаты ----------

        private void BuildLobby()
        {
            _durakLobbyScreen = new GameObject("Lobby", typeof(RectTransform));
            _durakLobbyScreen.transform.SetParent(_root, false);
            Stretch((RectTransform)_durakLobbyScreen.transform);
            _durakLobbyScreen.SetActive(false);

            // Весь список привязан к верхнему краю фиксированными отступами, а не долями
            // экрана: на широком окне Unity ужимает высоту холста, и доли начинают
            // наезжать друг на друга (та же ловушка, что была со столом).
            var title = CreateText(_durakLobbyScreen.transform, "Настройка комнаты — дурак", 40, TextAnchor.MiddleCenter);
            PlaceFromTop((RectTransform)title.transform, 0f, 56f, new Vector2(1100f, 60f));

            // Ряд размеров колоды.
            var deckLabel = CreateText(_durakLobbyScreen.transform, "Колода", 26, TextAnchor.MiddleCenter);
            PlaceFromTop((RectTransform)deckLabel.transform, 0f, 168f, new Vector2(400f, 34f));

            var deckOptions = new[] { DeckKind.Twenty4, DeckKind.ThirtySix, DeckKind.FiftyTwo, DeckKind.FiftyFour };
            var deckLabels = new[] { "24", "36", "52", "54" };
            var deckSpacing = 170f;
            var deckStartX = -(deckOptions.Length - 1) * deckSpacing / 2f;

            for (var i = 0; i < deckOptions.Length; i++)
            {
                var option = deckOptions[i];
                var x = deckStartX + i * deckSpacing;
                var image = CreateChoiceButton(_durakLobbyScreen.transform, deckLabels[i], () => SelectDeck(option), out var button);
                PlaceFromTop((RectTransform)button.transform, x, 210f, new Vector2(150f, 84f));
                _deckButtons.Add(button);
                _deckButtonImages.Add(image);
            }

            // Ряд количества игроков.
            var playersLabel = CreateText(_durakLobbyScreen.transform, "Игроков за столом", 26, TextAnchor.MiddleCenter);
            PlaceFromTop((RectTransform)playersLabel.transform, 0f, 330f, new Vector2(500f, 34f));

            var playerSpacing = 100f;
            var playerStartX = -(8 - 2) * playerSpacing / 2f;

            for (var count = 2; count <= 8; count++)
            {
                var captured = count;
                var x = playerStartX + (count - 2) * playerSpacing;
                var image = CreateChoiceButton(_durakLobbyScreen.transform, count.ToString(), () => SelectPlayerCount(captured), out var button);
                PlaceFromTop((RectTransform)button.transform, x, 372f, new Vector2(80f, 80f));
                _playerCountButtons.Add(button);
                _playerCountButtonImages.Add(image);
            }

            // Необязательные варианты правил — компактные переключатели "туда-сюда", без затемнения
            // (в отличие от колоды и числа игроков тут нет невозможных сочетаний).
            BuildBinaryToggle(_durakLobbyScreen.transform, 484f, "Переводной", "Нет", "Да", _transferButtons, _transferButtonImages,
                () => SelectTransfer(false), () => SelectTransfer(true));

            BuildBinaryToggle(_durakLobbyScreen.transform, 544f, "Подкидывают", "Все", "Соседи", _throwInButtons, _throwInButtonImages,
                () => SelectThrowIn(ThrowInMode.All), () => SelectThrowIn(ThrowInMode.NeighboursOnly));

            BuildBinaryToggle(_durakLobbyScreen.transform, 604f, "Шулерство", "Без", "С шулером", _cheatButtons, _cheatButtonImages,
                () => SelectCheating(false), () => SelectCheating(true));

            _seatsSummary = CreateText(_durakLobbyScreen.transform, string.Empty, 24, TextAnchor.MiddleCenter);
            _seatsSummary.color = new Color(0.85f, 0.92f, 0.85f, 0.9f);
            PlaceFromTop((RectTransform)_seatsSummary.transform, 0f, 674f, new Vector2(760f, 32f));

            CreateButton(_durakLobbyScreen.transform, "Играть", new Vector2(0.5f, 0f), new Vector2(0f, 110f),
                new Color(0.21f, 0.45f, 0.29f), StartMatch);

            CreateButton(_durakLobbyScreen.transform, "Назад", new Vector2(0f, 0f), new Vector2(160f, 90f),
                new Color(0.3f, 0.3f, 0.32f), () => ShowScreen(_modeSelectScreen));

            RefreshLobbySelection();
        }

        // ---------- Настройка комнаты: верю — не верю ----------

        private void BuildBluffLobby()
        {
            _bluffLobbyScreen = new GameObject("BluffLobby", typeof(RectTransform));
            _bluffLobbyScreen.transform.SetParent(_root, false);
            Stretch((RectTransform)_bluffLobbyScreen.transform);
            _bluffLobbyScreen.SetActive(false);

            var title = CreateText(_bluffLobbyScreen.transform, "Настройка комнаты — верю не верю", 40, TextAnchor.MiddleCenter);
            PlaceFromTop((RectTransform)title.transform, 0f, 56f, new Vector2(1100f, 60f));

            var deckLabel = CreateText(_bluffLobbyScreen.transform, "Колода", 26, TextAnchor.MiddleCenter);
            PlaceFromTop((RectTransform)deckLabel.transform, 0f, 168f, new Vector2(400f, 34f));

            var deckOptions = BluffMatchSettings.DeckOptions;
            var deckLabels = new[] { "24", "36", "52" };
            var deckSpacing = 170f;
            var deckStartX = -(deckOptions.Length - 1) * deckSpacing / 2f;

            for (var i = 0; i < deckOptions.Length; i++)
            {
                var option = deckOptions[i];
                var x = deckStartX + i * deckSpacing;
                var image = CreateChoiceButton(_bluffLobbyScreen.transform, deckLabels[i],
                    () => SelectBluffDeck(option), out var button);
                PlaceFromTop((RectTransform)button.transform, x, 210f, new Vector2(150f, 84f));
                _bluffDeckButtons.Add(button);
                _bluffDeckButtonImages.Add(image);
            }

            var playersLabel = CreateText(_bluffLobbyScreen.transform, "Игроков за столом", 26, TextAnchor.MiddleCenter);
            PlaceFromTop((RectTransform)playersLabel.transform, 0f, 330f, new Vector2(500f, 34f));

            var playerSpacing = 100f;
            var playerStartX = -(8 - 2) * playerSpacing / 2f;

            for (var count = 2; count <= 8; count++)
            {
                var captured = count;
                var x = playerStartX + (count - 2) * playerSpacing;
                var image = CreateChoiceButton(_bluffLobbyScreen.transform, count.ToString(),
                    () => SelectBluffPlayerCount(captured), out var button);
                PlaceFromTop((RectTransform)button.transform, x, 372f, new Vector2(80f, 80f));
                _bluffPlayerButtons.Add(button);
                _bluffPlayerButtonImages.Add(image);
            }

            BuildBinaryToggle(_bluffLobbyScreen.transform, 476f, "Перестрелка", "Без", "Включена",
                _bluffShootoutButtons, _bluffShootoutButtonImages,
                () => SelectBluffShootout(false), () => SelectBluffShootout(true));

            var shootoutHint = CreateText(_bluffLobbyScreen.transform,
                "изредка круг идёт на нервах: кидают до последнего, а кто первым проверит — тот петух",
                21, TextAnchor.UpperCenter);
            shootoutHint.color = new Color(1f, 0.78f, 0.6f, 0.8f);
            PlaceFromTop((RectTransform)shootoutHint.transform, 0f, 532f, new Vector2(920f, 28f));

            // Правила, которые не настраиваются: они держат всю игру и переключателями не бывают.
            var rules = CreateText(_bluffLobbyScreen.transform,
                "Каре сбрасывается автоматически.\n"
                + "Тузами заявляться нельзя — их можно только подкладывать втихую.\n"
                + "Партия идёт, пока у всех на руках не останутся одни тузы.\n"
                + "Проигрывает тот, у кого их больше всех.",
                22, TextAnchor.UpperCenter);
            rules.color = new Color(0.85f, 0.92f, 0.85f, 0.75f);
            PlaceFromTop((RectTransform)rules.transform, 0f, 574f, new Vector2(900f, 120f));

            _bluffSeatsSummary = CreateText(_bluffLobbyScreen.transform, string.Empty, 24, TextAnchor.MiddleCenter);
            _bluffSeatsSummary.color = new Color(0.85f, 0.92f, 0.85f, 0.9f);
            PlaceFromTop((RectTransform)_bluffSeatsSummary.transform, 0f, 734f, new Vector2(760f, 32f));

            CreateButton(_bluffLobbyScreen.transform, "Играть", new Vector2(0.5f, 0f), new Vector2(0f, 110f),
                new Color(0.42f, 0.26f, 0.45f), StartBluffMatch);

            CreateButton(_bluffLobbyScreen.transform, "Назад", new Vector2(0f, 0f), new Vector2(160f, 90f),
                new Color(0.3f, 0.3f, 0.32f), () => ShowScreen(_modeSelectScreen));

            RefreshBluffLobbySelection();
        }

        private void SelectBluffDeck(DeckKind deck)
        {
            _bluffSettings.DeckSize = deck;

            var max = BluffMatchSettings.MaxPlayersFor(deck);
            if (_bluffSettings.PlayerCount > max)
            {
                _bluffSettings.PlayerCount = max;
            }

            RefreshBluffLobbySelection();
        }

        private void SelectBluffPlayerCount(int count)
        {
            _bluffSettings.PlayerCount = count;

            // Выбранная колода не тянет столько игроков — берём самую маленькую, которая тянет.
            if (BluffMatchSettings.MaxPlayersFor(_bluffSettings.DeckSize) < count)
            {
                foreach (var deck in BluffMatchSettings.DeckOptions)
                {
                    if (BluffMatchSettings.MaxPlayersFor(deck) >= count)
                    {
                        _bluffSettings.DeckSize = deck;
                        break;
                    }
                }
            }

            RefreshBluffLobbySelection();
        }

        private void SelectBluffShootout(bool allow)
        {
            _bluffSettings.Shootout = allow;
            RefreshBluffLobbySelection();
        }

        private void RefreshBluffLobbySelection()
        {
            var deckOptions = BluffMatchSettings.DeckOptions;
            for (var i = 0; i < deckOptions.Length; i++)
            {
                var isValid = BluffMatchSettings.MaxPlayersFor(deckOptions[i]) >= _bluffSettings.PlayerCount;
                var isSelected = _bluffSettings.DeckSize == deckOptions[i];
                SetChoiceState(_bluffDeckButtons[i], _bluffDeckButtonImages[i], isValid, isSelected);
            }

            var max = BluffMatchSettings.MaxPlayersFor(_bluffSettings.DeckSize);
            for (var count = 2; count <= 8; count++)
            {
                var index = count - 2;
                var isValid = count <= max;
                var isSelected = _bluffSettings.PlayerCount == count;
                SetChoiceState(_bluffPlayerButtons[index], _bluffPlayerButtonImages[index], isValid, isSelected);
            }

            SetChoiceState(_bluffShootoutButtons[0], _bluffShootoutButtonImages[0], true, !_bluffSettings.Shootout);
            SetChoiceState(_bluffShootoutButtons[1], _bluffShootoutButtonImages[1], true, _bluffSettings.Shootout);

            var botCount = _bluffSettings.PlayerCount - 1;
            _bluffSeatsSummary.text = "За столом: ты + " + botCount
                + (botCount == 1 ? " бот" : botCount < 5 ? " бота" : " ботов")
                + "  ·  на руки по " + _bluffSettings.CardsPerPlayer + " карт";
        }

        private void StartBluffMatch()
        {
            BuildBluffGameScreen();
            ShowScreen(_gameScreen);
        }

        private void BuildBluffGameScreen()
        {
            if (_gameScreen != null)
            {
                Destroy(_gameScreen);
            }

            _gameScreen = new GameObject("BluffGameScreen", typeof(RectTransform));
            _gameScreen.transform.SetParent(_root, false);
            Stretch((RectTransform)_gameScreen.transform);
            _gameScreen.SetActive(false);

            var table = _gameScreen.AddComponent<BluffTableController>();
            table.Configure(_bluffSettings, () => ShowScreen(_bluffLobbyScreen));
        }

        /// <summary>Одна строка с подписью слева и двумя вариантами справа — компактнее, чем подпись сверху.</summary>
        private void BuildBinaryToggle(Transform parent, float offsetFromTop, string label, string optionA, string optionB,
            Button[] buttons, Image[] images, Action onSelectA, Action onSelectB)
        {
            var labelText = CreateText(parent, label, 26, TextAnchor.MiddleLeft);
            PlaceFromTop((RectTransform)labelText.transform, -260f, offsetFromTop, new Vector2(280f, 52f));

            images[0] = CreateChoiceButton(parent, optionA, onSelectA, out buttons[0]);
            PlaceFromTop((RectTransform)buttons[0].transform, 40f, offsetFromTop, new Vector2(170f, 52f));

            images[1] = CreateChoiceButton(parent, optionB, onSelectB, out buttons[1]);
            PlaceFromTop((RectTransform)buttons[1].transform, 220f, offsetFromTop, new Vector2(170f, 52f));
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

        private void SelectTransfer(bool allow)
        {
            _settings.AllowTransfer = allow;
            RefreshLobbySelection();
        }

        private void SelectThrowIn(ThrowInMode mode)
        {
            _settings.ThrowInMode = mode;
            RefreshLobbySelection();
        }

        private void SelectCheating(bool allow)
        {
            _settings.AllowCheating = allow;
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

            SetChoiceState(_transferButtons[0], _transferButtonImages[0], true, !_settings.AllowTransfer);
            SetChoiceState(_transferButtons[1], _transferButtonImages[1], true, _settings.AllowTransfer);

            SetChoiceState(_throwInButtons[0], _throwInButtonImages[0], true, _settings.ThrowInMode == ThrowInMode.All);
            SetChoiceState(_throwInButtons[1], _throwInButtonImages[1], true, _settings.ThrowInMode == ThrowInMode.NeighboursOnly);

            SetChoiceState(_cheatButtons[0], _cheatButtonImages[0], true, !_settings.AllowCheating);
            SetChoiceState(_cheatButtons[1], _cheatButtonImages[1], true, _settings.AllowCheating);

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
            table.Configure(_settings, () => ShowScreen(_durakLobbyScreen));
        }

        private void ShowScreen(GameObject screen)
        {
            _background.SetActive(screen != _gameScreen);
            _mainMenuScreen.SetActive(screen == _mainMenuScreen);
            _modeSelectScreen.SetActive(screen == _modeSelectScreen);
            _durakLobbyScreen.SetActive(screen == _durakLobbyScreen);
            _bluffLobbyScreen.SetActive(screen == _bluffLobbyScreen);
            _avatarScreen.SetActive(screen == _avatarScreen);

            if (_gameScreen != null)
            {
                _gameScreen.SetActive(screen == _gameScreen);
            }

            if (screen == _durakLobbyScreen)
            {
                RefreshLobbySelection();
            }

            // В галерею всегда заходим с первого уровня — со списка, кто это будет, — и
            // собираем его заново под нынешнюю ширину экрана.
            if (screen == _avatarScreen)
            {
                ShowAvatarGroups();
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
            ((RectTransform)text.transform).offsetMin = new Vector2(10f, 4f);
            ((RectTransform)text.transform).offsetMax = new Vector2(-10f, -4f);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 18;
            text.resizeTextMaxSize = 30;

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

        /// <summary>Отступ от верхнего края холста фиксированными пикселями — не сдвигается,
        /// когда Unity ужимает высоту холста на широких окнах.</summary>
        private static void PlaceFromTop(RectTransform rect, float x, float offsetFromTop, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(x, -offsetFromTop);
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
