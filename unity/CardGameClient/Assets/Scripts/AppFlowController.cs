using System;
using System.Collections.Generic;
using CardGame.Core.Games.Durak;
using CardGame.Core.Meta;
using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>
    /// Запускает игровые столы из общей оболочки. Настройки комнаты и быстрой партии
    /// редактируются одним экраном PlayerHubGameSetup и передаются в игровой контроллер.
    /// </summary>
    public sealed class AppFlowController : MonoBehaviour
    {
        private static readonly Color AccentGlow = new Color(1f, 0.85f, 0.3f);
        private static readonly Color PanelColor = new Color(0.36f, 0.22f, 0.12f);
        private static readonly Color TextColor = new Color(0.94f, 0.95f, 0.93f);

        // Подложка ячейки: чуть светлее сукна, чтобы граница читалась, но не спорила с портретом.
        private static readonly Color CellColor = new Color(0.10f, 0.26f, 0.18f);

        private Font _font;
        private RectTransform _root;

        private GameObject _background;
        private GameObject _mainMenuScreen;
        private GameObject _avatarScreen;
        private GameObject _gameScreen;
        private PlayerHubController _playerHub;
        private bool _launchingHubRoom;
        private string _lastHubGame;
        private bool _returnToBank;
        private bool _returnToBankJobs;

        // Галерея аватаров: первый уровень — наборы, второй — образы внутри набора.
        private RectTransform _avatarGroupsPanel;
        private RectTransform _avatarItemsPanel;
        private readonly List<Button> _avatarGroupButtons = new List<Button>();
        private readonly List<string> _avatarGroupIds = new List<string>();
        private readonly List<Button> _avatarButtons = new List<Button>();
        private readonly List<string> _avatarButtonNames = new List<string>();

        private DurakMatchSettings _settings = DurakMatchSettings.Default;
        private BluffMatchSettings _bluffSettings = BluffMatchSettings.Default;
        private ChestsMatchSettings _chestsSettings = ChestsMatchSettings.Default;
        private GovnoMatchSettings _govnoSettings = GovnoMatchSettings.Default;
        private PokerMatchSettings _pokerSettings = PokerMatchSettings.Default;

        private void Start()
        {
            _font = UiFonts.Body;
            _root = (RectTransform)transform;
            MobileUi.ConfigureTableCanvas(GetComponent<CanvasScaler>());

            BuildBackground();
            BuildMainMenu();
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

            _playerHub = _mainMenuScreen.AddComponent<PlayerHubController>();
            _playerHub.Configure(() => _playerHub.Navigate("games"),
                () => ShowScreen(_avatarScreen), LaunchHubGame, StartHubQuickGame);
        }

        private void StartHubQuickGame(string game)
        {
            if(_startingHubGame)return;
            ApplyGameSettings(game,_playerHub.GetGameSetupPlayerCount(),_playerHub.GetGameSetupSettings());
            StartConfiguredGame(game);
        }

        private void ApplyGameSettings(string game,int players,HubGameSettings rules)
        {
            rules=rules??HubGameSettings.DefaultFor(game);
            players=Mathf.Clamp(players,HubGameRules.MinimumPlayers(game),HubGameRules.MaximumPlayers(game,rules));
            var deck=rules.DeckSize==24?DeckKind.Twenty4:rules.DeckSize==36?DeckKind.ThirtySix:
                rules.DeckSize==54?DeckKind.FiftyFour:DeckKind.FiftyTwo;
            switch(game)
            {
                case "durak":_settings=new DurakMatchSettings{PlayerCount=players,DeckSize=deck,
                    AllowTransfer=rules.AllowTransfer,AllowCheating=rules.AllowCheating,
                    ThrowInMode=rules.NeighboursOnly?ThrowInMode.NeighboursOnly:ThrowInMode.All};break;
                case "bluff":_bluffSettings=new BluffMatchSettings{PlayerCount=players,DeckSize=deck,Shootout=rules.Shootout};break;
                case "chests":_chestsSettings=new ChestsMatchSettings{PlayerCount=players,DeckSize=deck};break;
                case "govno":_govnoSettings=new GovnoMatchSettings{PlayerCount=players,Chaos=rules.Chaos};break;
                case "poker":_pokerSettings=new PokerMatchSettings{PlayerCount=players};break;
            }
        }

        private void StartConfiguredGame(string game)
        {
            switch(game)
            {
                case "durak":StartMatch();break;
                case "bluff":StartBluffMatch();break;
                case "chests":StartChestsMatch();break;
                case "govno":StartGovnoMatch();break;
                case "poker":StartPokerMatch();break;
                default:ShowScreen(_mainMenuScreen);_playerHub.Navigate("games");break;
            }
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
                // Один образ — выбирается сразу, без второго экрана с единственной картинкой.
                var button = CreateAvatarCell(_avatarGroupsPanel, previews[i], position, grid.CellSize,
                    grid.PortraitHeight, async () =>
                    {
                        if (captured.Sprites.Count == 1)
                        {
                            if (await PlayerProfile.SetAvatarAsync(captured.Sprites[0].name))
                                RefreshGroupSelection();
                        }
                        else
                        {
                            OpenAvatarGroup(captured);
                        }
                    });

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
                    grid.PortraitHeight, () => SelectAvatar(sprite.name));

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
            public AvatarGrid(int columns, Vector2 cellSize, float step, float pitch, float portraitHeight)
            {
                Columns = columns;
                CellSize = cellSize;
                Step = step;
                Pitch = pitch;
                PortraitHeight = portraitHeight;
            }

            public int Columns { get; }
            public Vector2 CellSize { get; }

            /// <summary>Высота портрета на экране — одна на всю сетку. Портреты нарезаны в одном
            /// кадре (подбородок на 48% высоты), поэтому одна высота — это головы одного размера,
            /// хотя в пикселях картинки общие персонажи мельче друзей.</summary>
            public float PortraitHeight { get; }

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

            // Высота одна на всю сетку, чтобы головы стояли вровень; широким плечам разрешаем
            // вылезти за ячейку — их подрежет маска.
            var height = cellHeight - PortraitInsetY;
            foreach (var sprite in sprites)
            {
                height = Mathf.Min(height,
                    (cellWidth - PortraitInsetX) * MaxPortraitOverflow * sprite.rect.height / sprite.rect.width);
            }

            return new AvatarGrid(columns, new Vector2(cellWidth, cellHeight), step, pitch, height);
        }

        /// <summary>
        /// Ячейка сетки: подложка-рамка, а внутри портрет, посаженный на нижний край.
        /// Именно нижний: портреты разной высоты, и если центрировать, в сетке они
        /// «плавают» — кто выше, кто ниже. По общей линии плеч ряд читается ровным.
        /// </summary>
        private Button CreateAvatarCell(Transform parent, Sprite sprite, Vector2 topLeft, Vector2 cellSize,
            float portraitHeight, Action onClick)
        {
            var go = new GameObject("AvatarCell", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(HubOutline), typeof(Button));
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

            // Портрет живёт в маске: при общей высоте самый широкий персонаж шире ячейки,
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
            var portraitScale = portraitHeight / sprite.rect.height;
            portraitRect.sizeDelta = new Vector2(sprite.rect.width * portraitScale, portraitHeight);

            var portraitImage = portrait.GetComponent<Image>();
            portraitImage.sprite = sprite;
            portraitImage.raycastTarget = false;

            return button;
        }

        private async void SelectAvatar(string spriteName)
        {
            if (await PlayerProfile.SetAvatarAsync(spriteName))RefreshAvatarSelection();
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

        // ---------- Запуск игровых столов ----------

        private async void StartBluffMatch()
        {
            if(!await BeginHubMatch("bluff"))return;
            BuildBluffGameScreen();
            ShowScreen(_gameScreen);
        }

        private void BuildBluffGameScreen()
        {
            if (_gameScreen != null)
            {
                RemoveGameScreen();
            }

            _gameScreen = new GameObject("BluffGameScreen", typeof(RectTransform));
            _gameScreen.transform.SetParent(_root, false);
            Stretch((RectTransform)_gameScreen.transform);
            _gameScreen.SetActive(false);

            var table = _gameScreen.AddComponent<BluffTableController>();
            table.Configure(_bluffSettings, ExitHubGame);
        }

        // ---------- Сундучки ----------

        private async void StartChestsMatch()
        {
            if(!await BeginHubMatch("chests"))return;
            BuildChestsGameScreen();
            ShowScreen(_gameScreen);
        }

        private void BuildChestsGameScreen()
        {
            if (_gameScreen != null)
            {
                RemoveGameScreen();
            }

            _gameScreen = new GameObject("ChestsGameScreen", typeof(RectTransform));
            _gameScreen.transform.SetParent(_root, false);
            Stretch((RectTransform)_gameScreen.transform);
            _gameScreen.SetActive(false);

            var table = _gameScreen.AddComponent<ChestsTableController>();
            table.Configure(_chestsSettings, ExitHubGame);
        }

        private async void StartGovnoMatch()
        {
            if(!await BeginHubMatch("govno"))return;
            if (_gameScreen != null) RemoveGameScreen();
            _gameScreen = new GameObject("GovnoGameScreen", typeof(RectTransform));
            _gameScreen.transform.SetParent(_root, false);
            Stretch((RectTransform)_gameScreen.transform);
            _gameScreen.SetActive(false);
            _gameScreen.AddComponent<GovnoTableController>().Configure(_govnoSettings, ExitHubGame);
            ShowScreen(_gameScreen);
        }

        private async void StartPokerMatch()
        {
            if(!await BeginHubMatch("poker"))return;
            if (_gameScreen != null) RemoveGameScreen();
            _gameScreen = new GameObject("PokerGameScreen", typeof(RectTransform));
            _gameScreen.transform.SetParent(_root, false);
            Stretch((RectTransform)_gameScreen.transform);
            _gameScreen.SetActive(false);
            _gameScreen.AddComponent<PokerTableController>().Configure(_pokerSettings, ExitHubGame);
            ShowScreen(_gameScreen);
        }

        private async void StartMatch()
        {
            if(!await BeginHubMatch("durak"))return;
            BuildGameScreen();
            ShowScreen(_gameScreen);
        }

        private void BuildGameScreen()
        {
            if (_gameScreen != null)
            {
                RemoveGameScreen();
            }

            _gameScreen = new GameObject("GameScreen", typeof(RectTransform));
            _gameScreen.transform.SetParent(_root, false);
            Stretch((RectTransform)_gameScreen.transform);
            _gameScreen.SetActive(false);

            var table = _gameScreen.AddComponent<DurakTableController>();
            table.Configure(_settings, ExitHubGame);
        }

        private bool _startingHubGame;
        private void RemoveGameScreen()
        {if(Application.isPlaying)Destroy(_gameScreen);else DestroyImmediate(_gameScreen);}
        private async System.Threading.Tasks.Task<bool> BeginHubMatch(string game)
        {
            _lastHubGame=game;
            if(_launchingHubRoom)return true;
            if(_startingHubGame)return false;
            _returnToBank=false; _returnToBankJobs=false;
            _startingHubGame=true;
            try
            {
                var result=await PlayerHubRuntime.BeginGameAsync(game);
                if(this==null)return false;
                if(result.Success)return true;
                ShowScreen(_mainMenuScreen);
                _playerHub.Navigate(result.Error=="collector" || result.Snapshot.Bank.CollectorPending?"bank":"home");
                _playerHub.ShowMessage(PlayerHubRuntime.ErrorText(result));
                return false;
            }
            finally{_startingHubGame=false;}
        }

        private void LaunchHubGame(string game,int players)
        {
            _lastHubGame=game;
            var snapshot=PlayerHubRuntime.Current;
            var entry=snapshot?.Matches.Find(match=>match.Id==snapshot.ActiveMatchId);
            _returnToBank=entry?.IsCollector==true;
            _returnToBankJobs=!string.IsNullOrEmpty(entry?.BankJobId);
            var room=_returnToBank || _returnToBankJobs?null:snapshot?.Rooms.Find(item=>item.Id==snapshot.CurrentRoomId && item.GameId==game);
            ApplyGameSettings(game,players,_returnToBankJobs?entry.GameSettings:room?.GameSettings??HubGameSettings.DefaultFor(game));
            _launchingHubRoom=true;
            try
            {
                StartConfiguredGame(game);
            }
            finally{_launchingHubRoom=false;}
        }

        private async void ExitHubGame()
        {
            if(!await PlayerHubRuntime.ExitGameAsync())
            {Debug.LogWarning(PlayerHubRuntime.LastMessage);return;}
            if(this==null)return;
            ShowScreen(_mainMenuScreen);
            if (_returnToBankJobs) { _playerHub.ReturnToBankJobs(); return; }
            _playerHub.Navigate(_returnToBank?"bank":!string.IsNullOrEmpty(PlayerHubRuntime.Current?.CurrentRoomId)?"room":
                string.IsNullOrEmpty(_lastHubGame)?"games":"game:"+_lastHubGame);
        }

        private void ShowScreen(GameObject screen)
        {
            _background.SetActive(screen != _gameScreen);
            _mainMenuScreen.SetActive(screen == _mainMenuScreen);
            _avatarScreen.SetActive(screen == _avatarScreen);

            if (_gameScreen != null)
            {
                if (screen == _gameScreen)
                    MobileUi.ConfigureTableCanvas(GetComponent<CanvasScaler>(),
                        _gameScreen.GetComponent<ChestsTableController>() != null ||
                        _gameScreen.GetComponent<BluffTableController>() != null ? 900 : 820);
                _gameScreen.SetActive(screen == _gameScreen);
            }
            // В галерею всегда заходим с первого уровня — со списка, кто это будет, — и
            // собираем его заново под нынешнюю ширину экрана.
            if (screen == _avatarScreen)
            {
                ShowAvatarGroups();
            }
        }

        // ---------- Мелкие переиспользуемые конструкторы UI ----------

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
