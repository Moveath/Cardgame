using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>
    /// Рамка меню в духе мобильных игр: сверху профиль и монеты, снизу пять вкладок, главная —
    /// лобби с персонажем и большой кнопкой «Играть». Всё глубже открывается своим экраном
    /// с «Назад». У каждого раздела свой цвет (<see cref="Accent"/>): одинаковый на вкладке,
    /// кнопке лобби, плитке и в заголовке страницы. Иконки — Resources/UI/Hub/Icons
    /// (пока временные, подменяются файлами).
    /// </summary>
    public sealed partial class PlayerHubController
    {
        private static readonly Color HubYellow = new Color(1f, .78f, .22f);
        private static readonly Color HubBlue = new Color(.22f, .55f, 1f);
        private static readonly Color HubRed = new Color(.9f, .27f, .27f);
        private static readonly Color HubNight = new Color(.1f, .07f, .27f);
        private static readonly Color HubInk = new Color(.2f, .13f, .04f);
        private static readonly Color HubTabIdle = new Color(.8f, .78f, .95f);
        private static readonly Color HubSkyTop = new Color(.33f, .22f, .66f);
        private static readonly Color HubSkyBottom = new Color(.1f, .07f, .28f);

        private static readonly string[] TabRoots = { "shop", "inventory", "home", "friends", "progress" };
        private static readonly string[] TabTitles = { "Магазин", "Коллекция", "Главная", "Друзья", "Прогресс" };
        private static readonly string[] TabIcons = { "shop", "collection", "home", "friends", "progress" };

        private readonly Dictionary<string, Sprite> _icons = new Dictionary<string, Sprite>();
        private readonly List<HubTab> _tabs = new List<HubTab>();
        private RectTransform _header, _topBar, _tabShade, _tabBar;
        private Image _pageBadge, _pageIcon;
        private Text _gold;
        private Image _toastPill, _cityDim;
        private float _toastUntil;

        private sealed class HubTab
        {
            public string Root;
            public Color Accent;
            public Image Background, Icon;
            public Text Label;
            public GameObject Badge;
            public Text BadgeCount;
        }

        private void BuildShell()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var background = MakeRect(transform, "Hub background", 0, 0, 0, 0);
            Stretch(background);
            var backdrop = background.gameObject.AddComponent<Image>();
            backdrop.sprite = VerticalGradient(HubSkyTop, HubSkyBottom);
            backdrop.raycastTarget = false;
            if (!BuildCityBackdrop(background)) BuildSuitPattern(background);
            else BuildCityCanvas();
            _stage = MakeRect(transform, "Player hub", 0, 0, 1600, 900);
            _stage.anchorMin = _stage.anchorMax = _stage.pivot = new Vector2(.5f, .5f);
            _stage.anchoredPosition = Vector2.zero;
            _interaction = _stage.gameObject.AddComponent<CanvasGroup>();

            _topBar = StageLayer("Top bar");
            BuildTopBar();

            _header = MakeRect(_stage, "Page header", 0, 92, 1600, 56);
            _backButton = IconButton(_header, "Назад", "back", 24, 6, 150, 46, Back);
            // Значок раздела в его цвете — тот же, что на вкладке или кнопке, которой сюда пришли.
            _pageBadge = MakeRect(_header, "Page icon", 210, 3, 50, 50).gameObject.AddComponent<Image>();
            Round(_pageBadge, 50, 50, 14);
            _pageBadge.raycastTarget = false;
            _pageBadge.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(0, -3);
            _pageIcon = MakeRect(_pageBadge.transform, "Icon", 8, 8, 34, 34).gameObject.AddComponent<Image>();
            _pageIcon.preserveAspect = true;
            _pageIcon.raycastTarget = false;
            _pageTitle = MakeLabel(_header, "Главная", 274, 2, 700, 48, 34);
            _pageTitle.fontStyle = FontStyle.Bold;
            _pageTitle.horizontalOverflow = HorizontalWrapMode.Overflow;
            _pageSubtitle = MakeLabel(_header, "", 274, 14, 1300, 28, 17);
            _pageSubtitle.color = HubMuted;
            _pageSubtitle.resizeTextForBestFit = true;
            _pageSubtitle.resizeTextMinSize = 12;
            _pageSubtitle.resizeTextMaxSize = 17;
            BuildClubHeader();

            _content = MakeRect(_stage, "Page content", 210, 152, 1180, 660);
            BuildTabBar();

            // Сообщение — в пустой середине верхней панели: внизу оно ложилось бы на кнопки страниц.
            _toastPill = MakeRect(_stage, "Toast", 420, 25, 560, 46).gameObject.AddComponent<Image>();
            Round(_toastPill, 560, 46, 23);
            _toastPill.color = new Color(0, 0, 0, .62f);
            _toastPill.raycastTarget = false;
            _toast = MakeLabel(_toastPill.transform, "", 14, 6, 532, 34, 18);
            _toast.color = HubGold;
            _toast.alignment = TextAnchor.MiddleCenter;
            _toast.resizeTextForBestFit = true;
            _toast.resizeTextMinSize = 12;
            _toast.resizeTextMaxSize = 18;
            _toastPill.gameObject.SetActive(false);
        }

        /// <summary>Слой во всю сцену с её же координатами: его части прячутся и показываются вместе.</summary>
        private RectTransform StageLayer(string name) => MakeRect(_stage, name, 0, 0, 1600, 900);

        /// <summary>Косой узор из мастей поверх градиента — фон не пустой и сразу видно, что игра карточная.</summary>
        private void BuildSuitPattern(RectTransform background)
        {
            var suits = new[] { "♠", "♥", "♣", "♦" };
            var text = new StringBuilder();
            for (var row = 0; row < 20; row++)
            {
                // Строки по центру: 12 пробелов сдвигают нечётный ряд на половину клетки.
                if (row % 2 == 1) text.Append("            ");
                for (var column = 0; column < 16; column++) text.Append(suits[(row + column) % 4]).Append("           ");
                text.Append('\n');
            }
            var pattern = MakeLabel(background, text.ToString(), 0, 0, 2600, 2400, 44);
            var rect = pattern.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = Vector2.zero;
            rect.localRotation = Quaternion.Euler(0, 0, 14);
            pattern.alignment = TextAnchor.MiddleCenter;
            pattern.horizontalOverflow = HorizontalWrapMode.Overflow;
            pattern.lineSpacing = 1.6f;
            pattern.color = new Color(1, 1, 1, .055f);
        }

        private void BuildTopBar()
        {
            var bar = MakeRect(_topBar, "Profile and wallet", 16, 8, 1568, 80).gameObject.AddComponent<Image>();
            Round(bar, 1568, 80, 22);
            bar.color = new Color(0, 0, 0, .28f);
            bar.raycastTarget = false;

            var frame = MakeRect(_topBar, "Avatar frame", 30, 14, 68, 68).gameObject.AddComponent<Image>();
            frame.sprite = UiTextures.Circle(128, Color.white);
            frame.color = HubGold;
            frame.raycastTarget = false;
            var disc = MakeRect(frame.transform, "Avatar disc", 4, 4, 60, 60).gameObject.AddComponent<Image>();
            disc.sprite = UiTextures.Circle(128, Color.white);
            disc.color = new Color(.36f, .28f, .7f);
            disc.raycastTarget = false;
            disc.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            // Портрет крупнее круга: в круг попадают голова и плечи, как на аватарках в играх.
            _portrait = CenteredImage(disc.transform, "Current avatar", -12, -4, 84, 96);
            var levelBadge = MakeRect(_topBar, "Level badge", 76, 52, 34, 34).gameObject.AddComponent<Image>();
            levelBadge.sprite = UiTextures.Circle(64, Color.white);
            levelBadge.color = HubBlue;
            levelBadge.raycastTarget = false;
            levelBadge.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(0, -2);
            _level = MakeLabel(levelBadge.transform, "1", 0, 3, 34, 28, 17);
            _level.alignment = TextAnchor.MiddleCenter;
            _level.fontStyle = FontStyle.Bold;

            _identity = MakeLabel(_topBar, "Игрок", 120, 16, 280, 34, 25);
            _identity.fontStyle = FontStyle.Bold;
            _identity.resizeTextForBestFit = true;
            _identity.resizeTextMinSize = 16;
            _identity.resizeTextMaxSize = 25;
            var track = MakeRect(_topBar, "XP", 120, 56, 210, 14).gameObject.AddComponent<Image>();
            Round(track, 210, 14, 7);
            track.color = new Color(0, 0, 0, .45f);
            track.raycastTarget = false;
            _levelFill = MakeRect(track.transform, "Progress", 0, 0, 0, 14).gameObject.AddComponent<Image>();
            Round(_levelFill, 210, 14, 7);
            _levelFill.color = HubGold;
            _levelFill.raycastTarget = false;
            // Весь блок профиля — одна кнопка, без надписи «Профиль».
            OverlayButton(_topBar, "Профиль", 20, 10, 330, 76, () => Navigate("profile"));

            _wallet = CurrencyPill(HubCurrencyArt.Silver, 1012, "Пополнить", () => Navigate("bank"));
            _gold = CurrencyPill(HubCurrencyArt.Gold, 1246, null, null);
            _gold.text = "0";
            IconButton(_topBar, "Настройки", "settings", 1502, 16, 64, 64, () => Navigate("settings"), iconOnly: true);
        }

        private Text CurrencyPill(Sprite coin, float x, string plusName, Action plus)
        {
            var pill = MakeRect(_topBar, "Currency pill", x, 25, 220, 46).gameObject.AddComponent<Image>();
            Round(pill, 220, 46, 23);
            pill.color = new Color(0, 0, 0, .42f);
            pill.raycastTarget = false;
            CurrencyIcon(_topBar, coin, x - 14, 14, 50, 66);
            var amount = MakeLabel(pill.transform, "", 44, 7, plus == null ? 164 : 122, 34, 24);
            amount.fontStyle = FontStyle.Bold;
            amount.alignment = TextAnchor.MiddleLeft;
            amount.resizeTextForBestFit = true;
            amount.resizeTextMinSize = 14;
            amount.resizeTextMaxSize = 24;
            if (plus != null)
            {
                var button = IconButton(pill.transform, plusName, "plus", 176, 5, 36, 36, plus, iconOnly: true);
                button.GetComponent<Image>().color = new Color(.3f, .78f, .36f);
            }
            return amount;
        }

        private void BuildTabBar()
        {
            // Плашки под вкладками нет — город виден до низа экрана. Надписи держит мягкая тень
            // без края; она шире и ниже сцены, чтобы на вытянутом экране дойти до краёв. Лежит
            // под страницей, чтобы не темнить её нижние кнопки.
            _tabShade = StageLayer("Tab shade");
            _tabShade.SetSiblingIndex(_content.GetSiblingIndex());
            var night = new Color(.03f, .02f, .1f);
            var shade = MakeRect(_tabShade, "Shade", -1200, 690, 4000, 210).gameObject.AddComponent<Image>();
            shade.sprite = VerticalGradient(new Color(night.r, night.g, night.b, 0), new Color(night.r, night.g, night.b, .72f));
            shade.raycastTarget = false;
            var below = MakeRect(_tabShade, "Shade below", -1200, 900, 4000, 900).gameObject.AddComponent<Image>();
            below.color = new Color(night.r, night.g, night.b, .72f);
            below.raycastTarget = false;
            _tabBar = StageLayer("Tab bar");
            for (var i = 0; i < TabRoots.Length; i++)
            {
                var root = TabRoots[i];
                var rect = MakeRect(_tabBar, TabTitles[i], 800 - 105 + (i - 2) * 214, 818, 210, 78);
                var background = rect.gameObject.AddComponent<Image>();
                Round(background, 210, 78, 20);
                var button = rect.gameObject.AddComponent<Button>();
                button.targetGraphic = background;
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() => SwitchTab(root));
                rect.gameObject.AddComponent<HubPress>();
                // Надпись — первым потомком: по ней кнопку находят проверки.
                var label = MakeLabel(rect, TabTitles[i], 0, 50, 210, 26, 18);
                label.alignment = TextAnchor.MiddleCenter;
                label.fontStyle = FontStyle.Bold;
                label.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(0, -2);
                var icon = MakeRect(rect, "Icon", 81, 4, 48, 48).gameObject.AddComponent<Image>();
                icon.sprite = Icon(TabIcons[i]);
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                var tab = new HubTab { Root = root, Accent = Accent(root), Background = background, Icon = icon, Label = label };
                (tab.Badge, tab.BadgeCount) = Badge(rect, 124, 0);
                _tabs.Add(tab);
            }
        }

        /// <summary>Шапка, вкладки и значки — после каждой смены страницы или данных.</summary>
        private void RefreshShell()
        {
            _identity.text = _state.Profile.DisplayName;
            _wallet.text = HubMoney(_state.Profile.Coins);
            _level.text = _state.Profile.Level.ToString();
            _levelFill.rectTransform.sizeDelta = new Vector2(210 * Mathf.Clamp01((_state.Profile.XP % 100) / 100f), 14);
            _portrait.sprite = PlayerProfile.Avatar;

            var lobby = _page == "home";
            // Клуб — внутри города: без вкладок и верхней панели, своя шапка; у стола — только табличка.
            var club = IsClubPage(_page);
            var table = IsTablePage(_page);
            // На главной город виден почти целиком, на страницах разделов приглушён под текст; у стола —
            // чуть-чуть, на экранах клуба — тёплой темнотой, как свет зала.
            if (_cityDim != null)
                _cityDim.color = table ? new Color(.03f, .02f, .01f, .18f) : club ? new Color(.04f, .025f, .015f, .7f)
                    : new Color(.05f, .03f, .14f, lobby ? .12f : .62f);
            // Главная — только город, персонаж и «Играть»; панели появляются на страницах разделов.
            var sections = !lobby && !club && _page != "fortune" && _page != "news" && !IsMailPage(_page) && !IsFriendsPage(_page);
            _topBar.gameObject.SetActive(sections);
            _tabShade.gameObject.SetActive(sections);
            _tabBar.gameObject.SetActive(sections);
            _header.gameObject.SetActive(sections);
            _backButton.gameObject.SetActive(sections);
            _clubHeader.gameObject.SetActive(club && !table);
            RefreshMapButton();
            // Сообщение: на экранах клуба сверху шапка-вывеска, а внизу вкладок нет — туда.
            _toastPill.rectTransform.anchoredPosition = club ? new Vector2(520, -836) : new Vector2(420, -25);
            // Табличка стола рисуется в координатах всей сцены.
            _content.anchoredPosition = table ? Vector2.zero : lobby ? new Vector2(20, -92) : new Vector2(210, -152);
            _content.sizeDelta = table ? new Vector2(1600, 900) : lobby ? new Vector2(1560, 716) : new Vector2(1180, 660);
            var section = SectionOf(_page);
            _pageBadge.color = Accent(section.Key);
            _pageIcon.sprite = Icon(section.Icon);
            // Подзаголовок идёт в той же строке сразу за заголовком: высоты на две строки нет.
            var titleWidth = Mathf.Min(_pageTitle.preferredWidth, 700);
            _pageSubtitle.rectTransform.anchoredPosition = new Vector2(274 + titleWidth + 22, -14);
            _pageSubtitle.rectTransform.sizeDelta = new Vector2(1570 - (274 + titleWidth + 22), 28);

            var active = TabOf(_page);
            foreach (var tab in _tabs)
            {
                // Значок вкладки всегда в цвете раздела; выбранная вкладка залита этим цветом целиком.
                var on = tab.Root == active;
                tab.Background.color = on ? tab.Accent : new Color(1, 1, 1, 0);
                tab.Icon.color = on ? Color.white : tab.Accent;
                tab.Label.color = on ? (IsLight(tab.Accent) ? HubInk : Color.white) : HubTabIdle;
                SetBadge(tab.Badge, tab.BadgeCount, BadgeFor(tab.Root));
            }
            RefreshFortunePresentation();
            RefreshNewsPresentation();
            RefreshMailPresentation();
            RefreshFriendsPresentation();
        }

        private void SwitchTab(string root)
        {
            if (_busy) return;
            if (root == "home")
            {
                Navigate("home");
                return;
            }
            // Вкладка — корень своего раздела: «Назад» с неё ведёт на главную.
            _history.Clear();
            _history.Add("home");
            _page = root;
            if (_state != null) Render();
        }

        private string TabOf(string page)
        {
            for (var step = 0; step < 8; step++)
            {
                if (Array.IndexOf(TabRoots, page) >= 0) return page;
                if (IsMailPage(page)) return "friends";
                if (page.StartsWith("throw:", StringComparison.Ordinal)) return "inventory";
                page = ParentPage(page);
            }
            return "home";
        }

        /// <summary>Цвет раздела, игры или отдела магазина. Меняется здесь — и сразу везде.</summary>
        private static Color Accent(string key)
        {
            switch (key)
            {
                case "shop": case "quick": return new Color(1f, .52f, .2f);
                case "inventory": case "shop-character": return new Color(.93f, .38f, .7f);
                case "home": return HubYellow;
                case "friends": return new Color(.2f, .74f, .95f);
                case "chats": return new Color(.36f, .56f, 1f);
                case "progress": case "quests": case "create": return new Color(.3f, .78f, .36f);
                case "achievements": case "bluff": return new Color(.64f, .44f, 1f);
                case "pass": case "profile": case "tutorial": case "shop-frame": return new Color(1f, .74f, .2f);
                case "ranks": return new Color(.45f, .64f, .98f);
                case "bank": case "chests": case "shop-table": return new Color(.1f, .7f, .62f);
                case "settings": return new Color(.55f, .58f, .75f);
                case "durak": case "shop-consumable": return new Color(.9f, .3f, .32f);
                case "govno": return new Color(.8f, .52f, .22f);
                case "poker": return new Color(.28f, .72f, .38f);
                default: return HubBlue;
            }
        }

        /// <summary>Раздел страницы: ключ цвета и имя значка для заголовка.</summary>
        private (string Key, string Icon) SectionOf(string page)
        {
            var game = string.IsNullOrEmpty(_selectedGame) ? "home" : _selectedGame;
            if (page.StartsWith("game:", StringComparison.Ordinal)) return (page.Substring(5), "home");
            if (page.StartsWith("friend:", StringComparison.Ordinal)) return ("friends", "friends");
            if (page.StartsWith("chat:", StringComparison.Ordinal)) return ("chats", "chats");
            if (page.StartsWith("throw:", StringComparison.Ordinal)) return ("inventory", "collection");
            switch (page)
            {
                case "rooms": case "create-room": case "quick-game": case "room-settings": case "room": return (game, "home");
                case "shop": case "shop-catalog": return ("shop", "shop");
                case "inventory": return ("inventory", "collection");
                case "friends": return ("friends", "friends");
                case "chats": return ("chats", "chats");
                case "progress": return ("progress", "progress");
                case "quests": return ("quests", "quests");
                case "achievements": return ("achievements", "progress");
                case "pass": return ("pass", "pass");
                case "ranks": return ("ranks", "progress");
                case "bank": return ("bank", "bank");
                case "settings": case "extras": return ("settings", "settings");
                case "tutorial": return ("tutorial", "quests");
                case "profile": return ("profile", "friends");
                default: return ("home", "home");
            }
        }

        private static string GameSuit(string game)
        {
            switch (game)
            {
                case "bluff": return "♥";
                case "chests": return "♦";
                case "govno": return "♣";
                default: return "♠";
            }
        }

        private int BadgeFor(string root)
        {
            var me = _state.Profile.Id;
            switch (root)
            {
                case "progress":
                    return ReadyQuests()
                        + _state.Achievements.Count(item => item.Unlocked && !item.Claimed)
                        + _state.Pass.Rewards.Count(item => !item.Claimed && item.Level <= _state.Pass.Level
                                                            && (!item.Premium || _state.Pass.Premium));
                case "friends":
                    return _state.FriendRequests.Count(item => item.Status == "pending" && item.ToId == me)
                        + _state.Invites.Count(item => item.Status == "pending" && item.ToId == me)
                        + MailUnreadAll().Count;
                default:
                    return 0;
            }
        }

        private int ReadyQuests() => _state == null ? 0 : CardGame.Core.Meta.HubQuests.Notifications(_state);

        private void BuildHome()
        {
            Title("Главная");
            // Автор работает над фоном (2026-09-28): на главной только город. Готовы к возврату одним
            // вызовом: персонаж на подиуме (BuildLobbyFigure), «Играть» (BuildPlayButton), полоса пасса
            // (BuildPassStrip), круглые кнопки (SideButton), карточка игры (GameCard).
            // Повтор сохранения остаётся: без него результат партии потеряется.
            if (PlayerHubRuntime.HasPendingResult)
            {
                var retry = Button("Повторить сохранение результата", 30, 640, 420, 46, async () =>
                {
                    var result = await PlayerHubRuntime.RetryPendingResultAsync();
                    Render();
                    Toast(result.Success ? "Результат сохранён" : PlayerHubRuntime.ErrorText(result));
                });
                retry.GetComponent<Image>().color = HubRed;
            }
        }

        /// <summary>Персонаж игрока в полный рост на подиуме в центре главной.</summary>
        private void BuildLobbyFigure()
        {
            var glow = MakeRect(_content, "Lobby glow", 780 - 300, 70, 600, 600).gameObject.AddComponent<Image>();
            glow.sprite = UiTextures.RadialGlow(256, new Color(1f, .9f, .75f, .3f));
            glow.raycastTarget = false;
            var podium = MakeRect(_content, "Podium", 780 - 160, 574, 320, 74).gameObject.AddComponent<Image>();
            podium.sprite = UiTextures.Circle(256, Color.white);
            podium.color = new Color(.05f, .03f, .15f, .6f);
            podium.raycastTarget = false;
            var top = MakeRect(_content, "Podium top", 780 - 140, 564, 280, 60).gameObject.AddComponent<Image>();
            top.sprite = UiTextures.Circle(256, Color.white);
            top.color = new Color(.5f, .42f, .95f);
            top.raycastTarget = false;
            var shine = MakeRect(_content, "Podium shine", 780 - 100, 570, 200, 34).gameObject.AddComponent<Image>();
            shine.sprite = UiTextures.Circle(256, Color.white);
            shine.color = new Color(1, 1, 1, .16f);
            shine.raycastTarget = false;
            // Ширина — по пропорциям кадра: узкая фигура стоит ровно по центру подиума.
            var sprite = LobbyFigure();
            var width = sprite == null ? 200f : 480f * sprite.rect.width / sprite.rect.height;
            var figure = CenteredImage(_content, "Lobby character", 780 - width / 2, 116, width, 480);
            figure.sprite = sprite;
        }

        /// <summary>Большая жёлтая «Играть» справа внизу; карточки игры нет, поэтому — к выбору игры.</summary>
        private void BuildPlayButton()
        {
            var play = Button("Играть", 1180, 500, 350, 120, () => Navigate("games"));
            MarkSelected(play);
            play.GetComponentInChildren<Text>().fontSize = 46;
        }

        private void BuildPassStrip()
        {
            var pass = _state.Pass;
            var strip = Button("Брейнрот пасс · ур. " + pass.Level, 780 - 230, 8, 460, 58, () => Navigate("pass"));
            strip.GetComponent<Image>().color = new Color(.12f, .08f, .3f, .88f);
            var title = strip.GetComponentInChildren<Text>();
            title.alignment = TextAnchor.UpperLeft;
            title.fontSize = 19;
            title.rectTransform.anchoredPosition = new Vector2(64, -6);
            title.rectTransform.sizeDelta = new Vector2(260, 26);
            var more = MakeLabel(strip.transform, "Награды ›", 324, 6, 120, 26, 17);
            more.alignment = TextAnchor.UpperRight;
            more.color = HubYellow;
            more.fontStyle = FontStyle.Bold;
            var icon = MakeRect(strip.transform, "Icon", 12, 7, 44, 44).gameObject.AddComponent<Image>();
            icon.sprite = Icon("pass");
            icon.color = HubYellow;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            var track = MakeRect(strip.transform, "Pass track", 64, 36, 380, 12).gameObject.AddComponent<Image>();
            Round(track, 380, 12, 6);
            track.color = new Color(0, 0, 0, .5f);
            track.raycastTarget = false;
            var share = pass.Level >= 10 ? 1f : (pass.XP % 100) / 100f;
            var fill = MakeRect(track.transform, "Pass progress", 0, 0, 380 * share, 12).gameObject.AddComponent<Image>();
            Round(fill, 380, 12, 6);
            fill.color = HubYellow;
            fill.raycastTarget = false;
        }

        /// <summary>Карточка выбранной игры в её цвете; снизу явная плашка «Сменить игру».</summary>
        private void GameCard(string game, float x, float y)
        {
            const float width = 350, height = 230;
            var accent = Accent(game);
            var card = Button("Сменить игру", x, y, width, height, () => Navigate("games"));
            card.GetComponent<Image>().color = Tint(accent, .35f);
            var pill = MakeRect(card.transform, "Change pill", 16, height - 64, width - 32, 48).gameObject.AddComponent<Image>();
            Round(pill, width - 32, 48, 24);
            pill.color = new Color(0, 0, 0, .3f);
            pill.raycastTarget = false;
            pill.transform.SetAsFirstSibling();
            // Первая надпись — действие карточки, по ней её находят проверки.
            var hint = card.GetComponentInChildren<Text>();
            hint.alignment = TextAnchor.MiddleCenter;
            hint.fontSize = 20;
            hint.color = Color.white;
            hint.rectTransform.anchoredPosition = new Vector2(16, -(height - 64));
            hint.rectTransform.sizeDelta = new Vector2(width - 32, 48);
            foreach (var (arrow, left) in new[] { ("‹", 30f), ("›", width - 64) })
            {
                var mark = MakeLabel(card.transform, arrow, left, height - 66, 34, 48, 34);
                mark.alignment = TextAnchor.MiddleCenter;
                mark.color = HubYellow;
            }
            // Масть игры — в правом верхнем углу, над описанием, чтобы не лезть под текст.
            var emblem = MakeLabel(card.transform, GameSuit(game), width - 118, -8, 110, 120, 96);
            emblem.alignment = TextAnchor.MiddleCenter;
            emblem.color = new Color(1, 1, 1, .16f);
            var kind = MakeLabel(card.transform, "Выбранная игра", 20, 16, 200, 24, 17);
            kind.color = new Color(1, 1, 1, .75f);
            var title = MakeLabel(card.transform, GameTitle(game), 20, 42, 310, 48, 38);
            title.fontStyle = FontStyle.Bold;
            title.resizeTextForBestFit = true;
            title.resizeTextMinSize = 20;
            title.resizeTextMaxSize = 38;
            title.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(0, -2);
            MakeLabel(card.transform, GameBlurb(game), 20, 98, 310, 56, 18).color = new Color(1, 1, 1, .85f);
        }

        private static string GameBlurb(string game)
        {
            switch (game)
            {
                case "durak": return "Подкидывай, переводи и избавляйся от карт";
                case "bluff": return "Проверяй чужие слова и рискуй";
                case "chests": return "Собирай четвёрки одного достоинства";
                case "govno": return "Собери каре и первым хлопни по столу";
                case "poker": return "Две карты в руке, пять на столе";
                default: return "";
            }
        }

        /// <summary>Круглая кнопка лобби в цвете своего раздела, с белым ободком и подписью.</summary>
        private void SideButton(string title, string icon, string section, float x, float y, int badge, Action action)
        {
            var rect = MakeRect(_content, title, x, y, 120, 132);
            var button = rect.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => action());
            rect.gameObject.AddComponent<HubPress>();
            var label = MakeLabel(rect, title, 0, 104, 120, 26, 19);
            label.alignment = TextAnchor.MiddleCenter;
            label.fontStyle = FontStyle.Bold;
            label.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(0, -2);
            var ring = MakeRect(rect, "Ring", 10, 0, 100, 100).gameObject.AddComponent<Image>();
            ring.sprite = UiTextures.Circle(128, Color.white);
            ring.color = Color.white;
            ring.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(0, -5);
            button.targetGraphic = ring;
            var disc = MakeRect(ring.transform, "Disc", 5, 5, 90, 90).gameObject.AddComponent<Image>();
            disc.sprite = UiTextures.Circle(128, Color.white);
            disc.color = Accent(section);
            disc.raycastTarget = false;
            var image = MakeRect(disc.transform, "Icon", 17, 17, 56, 56).gameObject.AddComponent<Image>();
            image.sprite = Icon(icon);
            image.preserveAspect = true;
            image.raycastTarget = false;
            var (dot, count) = Badge(rect, 90, 0);
            SetBadge(dot, count, badge);
        }

        private Sprite LobbyFigure()
        {
            var avatar = PlayerProfile.Avatar;
            if (avatar == null) return null;
            return Resources.Load<Sprite>("Govno/Stickers/Figures/" + avatar.name + "/stage-0") ?? avatar;
        }

        /// <summary>
        /// Картинка с сохранением пропорций по центру своей рамки. При опоре в углу, как у остальных
        /// элементов меню, Unity прижимает узкую картинку к левому краю.
        /// </summary>
        private static Image CenteredImage(Transform parent, string name, float x, float y, float w, float h)
        {
            var rect = MakeRect(parent, name, x, y, w, h);
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(x + w / 2, -y - h / 2);
            var image = rect.gameObject.AddComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>Красный кружок с числом; -1 — просто восклицательный знак.</summary>
        private (GameObject, Text) Badge(Transform parent, float x, float y)
        {
            var dot = MakeRect(parent, "Badge", x, y, 30, 30).gameObject.AddComponent<Image>();
            dot.sprite = UiTextures.Circle(64, Color.white);
            dot.color = HubRed;
            dot.raycastTarget = false;
            var count = MakeLabel(dot.transform, "", 0, 3, 30, 24, 16);
            count.alignment = TextAnchor.MiddleCenter;
            count.fontStyle = FontStyle.Bold;
            dot.gameObject.SetActive(false);
            return (dot.gameObject, count);
        }

        private static void SetBadge(GameObject dot, Text count, int value)
        {
            dot.SetActive(value != 0);
            count.text = value < 0 ? "!" : value > 9 ? "9+" : value.ToString();
        }

        private Sprite Icon(string name)
        {
            if (_icons.TryGetValue(name, out var cached)) return cached;
            var texture = Resources.Load<Texture2D>("UI/Hub/Icons/" + name);
            var sprite = texture == null ? null
                : Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 100f);
            _icons[name] = sprite;
            return sprite;
        }

        /// <summary>Кнопка со значком: слева иконка, справа надпись; без надписи — круглая.</summary>
        private Button IconButton(Transform parent, string text, string icon, float x, float y, float w, float h,
            Action action, bool iconOnly = false)
        {
            var button = MakeButton(parent, text, x, y, w, h, action);
            var label = button.GetComponentInChildren<Text>();
            var size = Mathf.Min(h - 14, 40);
            if (iconOnly)
            {
                label.text = "";
                label.gameObject.SetActive(false);
                button.GetComponent<Image>().sprite = UiTextures.Circle(128, Color.white);
                button.GetComponent<Image>().type = Image.Type.Simple;
                button.GetComponent<Image>().color = new Color(0, 0, 0, .42f);
            }
            else
            {
                label.alignment = TextAnchor.MiddleLeft;
                label.rectTransform.anchoredPosition = new Vector2(size + 18, -2);
                label.rectTransform.sizeDelta = new Vector2(w - size - 24, h - 4);
            }
            var image = MakeRect(button.transform, "Icon", iconOnly ? (w - size) / 2 : 10, (h - size) / 2, size, size)
                .gameObject.AddComponent<Image>();
            image.sprite = Icon(icon);
            image.preserveAspect = true;
            image.raycastTarget = false;
            return button;
        }

        /// <summary>Прозрачная кнопка поверх готового блока; имя — для проверок и отладки.</summary>
        private static void OverlayButton(Transform parent, string name, float x, float y, float w, float h, Action action)
        {
            var rect = MakeRect(parent, name, x, y, w, h);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0, 0, 0, 0);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => action());
        }

        /// <summary>Выбранная вкладка или главное действие — жёлтым, надпись тёмная.</summary>
        private static void MarkSelected(Button button) => Paint(button, HubYellow);

        /// <summary>Кнопка в заданный цвет; на светлом цвете надпись тёмная.</summary>
        private static void Paint(Button button, Color color)
        {
            button.GetComponent<Image>().color = color;
            var label = button.GetComponentInChildren<Text>();
            if (label != null) label.color = IsLight(color) ? HubInk : Color.white;
        }

        private static bool IsLight(Color color) => .3f * color.r + .59f * color.g + .11f * color.b > .68f;

        /// <summary>
        /// Тёмная подложка плитки в цвете раздела. Затемнённый жёлтый становится грязно-коричневым,
        /// поэтому светлым цветам — фиолетовая подложка, а цвет остаётся на значке и кнопке.
        /// </summary>
        private static Color Tint(Color accent, float depth) =>
            IsLight(accent) ? new Color(.25f, .17f, .53f) : Color.Lerp(accent, HubNight, depth);

        private void UpdateToast()
        {
            if (_toastPill != null && _toastPill.gameObject.activeSelf && Time.unscaledTime > _toastUntil)
                _toastPill.gameObject.SetActive(false);
        }

        /// <summary>Скругление, которое не ломается на тонких полосках: радиус не больше половины высоты.</summary>
        private static void Round(Image image, float width, float height, float radius)
        {
            image.sprite = UiTextures.RoundedRect(64, 64, 16, Color.white);
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 16f / Mathf.Max(1f, Mathf.Min(radius, Mathf.Min(width, height) / 2f));
        }

        private static readonly Dictionary<string, Sprite> Gradients = new Dictionary<string, Sprite>();

        private static Sprite VerticalGradient(Color top, Color bottom)
        {
            var key = top + "_" + bottom;
            if (Gradients.TryGetValue(key, out var cached) && cached != null) return cached;
            var texture = new Texture2D(1, 64, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            for (var y = 0; y < 64; y++) texture.SetPixel(0, y, Color.Lerp(bottom, top, y / 63f));
            texture.Apply();
            var sprite = Sprite.Create(texture, new Rect(0, 0, 1, 64), new Vector2(.5f, .5f), 100f);
            Gradients[key] = sprite;
            return sprite;
        }
    }
}
