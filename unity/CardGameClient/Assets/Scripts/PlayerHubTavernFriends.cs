using System;
using System.Collections.Generic;
using System.Linq;
using CardGame.Core.Meta;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>
    /// Друзья — окно таверны «Червовая дама» (2026-10-10, решение автора): страницы `friends` и `friend:<id>`. Открывается
    /// за любым столом зала, вкладкой «Друзья», из почты, рейтинга и из комнаты в Клубе («Пригласить друга»). Тот же ящик,
    /// что у почты (`UI/Hub/tavern-mail-case`), поверх притемнённого зала: слева на коже — закладки «Друзья», «Заявки»,
    /// «Найти» и список бумажными карточками (зовёт за стол или хочет дружить — печать-сердце), справа на пергаменте —
    /// карточка выбранного: лицо, уровень и ранг, что между вами и что можно сделать.
    /// Связка с Клубом: «Позвать играть» — выбор игры, свой закрытый стол без ставки (`friend_play`), друг садится сразу,
    /// и игрок едет к столу этой игры в Клубе (покер — в казино): комната открыта у стола, «Назад» — к табличке стола.
    /// Уже сидишь за столом — «Позвать за стол» (`invite_friend`). Друг зовёт за свой стол — «Сесть за стол»
    /// (`invite_accept`), туда же. Заявки в друзья — принять или отклонить; сосед по столу после партии может
    /// попроситься сам. Живых игроков пока нет: местные боты отвечают сразу.
    /// </summary>
    public sealed partial class PlayerHubController
    {
        private const float FriendsRowHeight = 84, FriendsRowGap = 8;
        // Кожа слева (пиксели ящика): закладки сверху, поиск под ними, список, полоска «Твой стол» внизу.
        private const float FriendsLeft = 96, FriendsRight = 514, FriendsTabsY = 109, FriendsSearchY = 172, FriendsStripY = 537;
        private static readonly string[] FriendsTabKeys = { "friends", "requests", "people" };
        private static readonly string[] FriendsTabNames = { "Друзья", "Заявки", "Найти" };
        private static readonly string[] FriendsGames = { "durak", "bluff", "chests", "govno", "poker" };
        private static readonly Color FriendsPaper = new Color(.9f, .82f, .66f), FriendsAlert = new Color(.62f, .1f, .07f),
            FriendsDark = new Color(.28f, .12f, .035f);

        /// <summary>Строка списка: кто, что под именем, печать (зовёт или просится) и что выбрано на пергаменте.</summary>
        private sealed class FriendsEntry
        {
            public string Key;
            public HubPerson Person;
            public string Line;
            public bool Alert;
            public HubInvite Invite;
            public HubFriendRequest Request;
        }

        private RectTransform _friendsLayer, _friendsCase, _friendsList, _friendsListView, _friendsCard, _friendsStrip, _friendsToastPill;
        private CanvasGroup _friendsGroup;
        private Image _friendsBackdrop;
        private Text _friendsEmpty, _friendsStripText, _friendsToast;
        private InputField _friendsSearch;
        private HubDragScrollbar _friendsScrollbar;
        private readonly List<(Button Button, Text Seal)> _friendsTabs = new List<(Button, Text)>();
        private string _friendsPick, _friendsShownPage, _friendsConfirmRemove;
        private bool _friendsPicking, _friendsEscapeHeld;
        private float _friendsFade, _friendsToastUntil;

        private static bool IsFriendsPage(string page) => page == "friends" || page.StartsWith("friend:", StringComparison.Ordinal);
        private bool FriendsShown => IsFriendsPage(_page) && _friendsLayer != null && _friendsLayer.gameObject.activeSelf;
        private string FriendsTab => Array.IndexOf(FriendsTabKeys, _hubFriendsTab) >= 0 ? _hubFriendsTab : _hubFriendsTab == "invites" ? "requests" : "friends";

        private static string FriendsGameName(string game)
        {
            switch (game)
            {
                case "durak": return "Дурак";
                case "bluff": return "Верю — не верю";
                case "chests": return "Сундучки";
                case "govno": return "Говно";
                case "poker": return "Покер";
                default: return game;
            }
        }

        private HubPerson FriendsPerson(string id) =>
            _state.People.FirstOrDefault(person => person.Id == id) ?? new HubPerson { Id = id, Name = id, Status = "" };

        private HubInvite FriendsInviteFrom(string id) => _state.Invites.FirstOrDefault(invite => invite.Status == "pending"
            && invite.ToId == _state.Profile.Id && invite.FromId == id);

        private HubFriendRequest FriendsRequestWith(string id) => _state.FriendRequests.FirstOrDefault(request => request.Status == "pending"
            && (request.FromId == id && request.ToId == _state.Profile.Id || request.FromId == _state.Profile.Id && request.ToId == id));

        /// <summary>Что ждёт ответа игрока: приглашения за стол и заявки в друзья — печать на закладке «Заявки».</summary>
        private int FriendsWaiting() => _state.Invites.Count(invite => invite.Status == "pending" && invite.ToId == _state.Profile.Id)
            + _state.FriendRequests.Count(request => request.Status == "pending" && request.ToId == _state.Profile.Id);

        private string FriendsTableLine(HubRoom room) => FriendsGameName(room.GameId) + " · «" + room.Name + "» · "
            + room.Members.Count + " из " + room.MaxPlayers + (room.Stake > 0 ? " · ставка " + HubMoney(room.Stake) : "");

        /// <summary>Мой стол, за который ещё можно звать: комната есть и партия не идёт.</summary>
        private HubRoom FriendsMyTable()
        {
            var room = CurrentRoom();
            return room != null && !room.Started ? room : null;
        }

        private List<FriendsEntry> FriendsEntries(string tab)
        {
            var me = _state.Profile.Id;
            var entries = new List<FriendsEntry>();
            if (tab == "requests")
            {
                foreach (var invite in _state.Invites.Where(item => item.Status == "pending" && item.ToId == me))
                {
                    var room = _state.Rooms.FirstOrDefault(item => item.Id == invite.RoomId);
                    entries.Add(new FriendsEntry { Key = invite.Id, Person = FriendsPerson(invite.FromId), Invite = invite, Alert = true,
                        Line = "Зовёт за стол" + (room == null ? "" : " · " + FriendsGameName(room.GameId)) });
                }
                foreach (var request in _state.FriendRequests.Where(item => item.Status == "pending" && item.ToId == me))
                    entries.Add(new FriendsEntry { Key = request.Id, Person = FriendsPerson(request.FromId), Request = request, Alert = true,
                        Line = "Хочет дружить" });
                foreach (var request in _state.FriendRequests.Where(item => item.Status == "pending" && item.FromId == me))
                    entries.Add(new FriendsEntry { Key = request.Id, Person = FriendsPerson(request.ToId), Request = request, Line = "Ждёт ответа" });
                return entries;
            }
            if (tab == "people")
            {
                var query = (_hubFriendSearch ?? "").Trim();
                foreach (var person in _state.People.Where(person => !_state.Friends.Contains(person.Id) && (query.Length == 0
                             || person.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                             || person.Id.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)).OrderBy(person => person.Name))
                {
                    var request = FriendsRequestWith(person.Id);
                    var incoming = request != null && request.ToId == me;
                    entries.Add(new FriendsEntry { Key = person.Id, Person = person, Request = request, Alert = incoming,
                        Line = incoming ? "Хочет дружить" : request != null ? "Заявка отправлена"
                            : "Ур. " + person.Level + " · " + HubRankName(person.Rating) });
                }
                return entries;
            }
            var table = FriendsMyTable();
            foreach (var person in _state.People.Where(person => _state.Friends.Contains(person.Id)))
            {
                var invite = FriendsInviteFrom(person.Id);
                var room = invite == null ? null : _state.Rooms.FirstOrDefault(item => item.Id == invite.RoomId);
                var seated = table != null && table.Members.Any(member => member.PlayerId == person.Id);
                entries.Add(new FriendsEntry { Key = person.Id, Person = person, Invite = invite, Alert = invite != null,
                    Line = invite != null ? "Зовёт за стол" + (room == null ? "" : " · " + FriendsGameName(room.GameId))
                        : seated ? "За твоим столом" : "Ур. " + person.Level + " · " + HubRankName(person.Rating) });
            }
            // Сначала те, кто зовёт, потом сидящие за твоим столом, дальше — по имени.
            return entries.OrderBy(entry => entry.Invite == null).ThenBy(entry => entry.Line != "За твоим столом")
                .ThenBy(entry => entry.Person.Name).ToList();
        }

        private void BuildTavernFriends()
        {
            if (_friendsLayer == null) BuildFriendsScene();
            if (!_friendsLayer.gameObject.activeSelf)
            {
                // Подсел к столу: ящик проявляется поверх зала и чуть наезжает, как почта.
                _friendsEscapeHeld = Keyboard.current != null && Keyboard.current.escapeKey.isPressed;
                _friendsFade = NewsReduced ? 1 : 0;
                _friendsGroup.alpha = _friendsFade;
                _friendsPicking = false;
                _friendsConfirmRemove = null;
                _friendsToastPill.gameObject.SetActive(false);
            }
            // Карточка игрока (`friend:<id>`): друг — на закладке «Друзья», остальные — в «Найти».
            if (_friendsShownPage != _page && _page.StartsWith("friend:", StringComparison.Ordinal))
            {
                var id = _page.Substring(7);
                _hubFriendsTab = _state.Friends.Contains(id) ? "friends" : "people";
                if (_hubFriendsTab == "people") { _hubFriendSearch = ""; _friendsSearch.SetTextWithoutNotify(""); }
                _friendsPick = id;
                _friendsPicking = false;
            }
            _friendsShownPage = _page;
            _friendsLayer.gameObject.SetActive(true);
            _friendsLayer.SetAsLastSibling();
            Title("Друзья", "");
            PaintFriends();
        }

        private void SelectFriendsTab(string tab)
        {
            if (_busy || !FriendsShown) return;
            _hubFriendsTab = tab;
            _friendsPick = null;
            _friendsPicking = false;
            _friendsConfirmRemove = null;
            _friendsList.anchoredPosition = new Vector2(_friendsList.anchoredPosition.x, 0);
            PaintFriends();
        }

        private void SelectFriendsEntry(string key)
        {
            if (_busy || !FriendsShown) return;
            _friendsPick = key;
            _friendsPicking = false;
            _friendsConfirmRemove = null;
            PaintFriends();
        }

        /// <summary>
        /// К своему столу в Клубе: комната открыта у стола (камера едет туда сама, <see cref="ContinueTableTrip"/>), «Назад» —
        /// к табличке стола, оттуда — в зал. Пришли в окно из комнаты — просто вернуться в неё.
        /// </summary>
        private void GoToMyTable()
        {
            var room = CurrentRoom();
            if (_busy || room == null) return;
            if (_history.Count > 0 && _history[_history.Count - 1] == "room") { Back(); return; }
            SeatAtTable(room.GameId);
            Render();
        }

        /// <summary>Страница комнаты у стола игры <paramref name="game"/> с историей «главная → табличка стола».</summary>
        private void SeatAtTable(string game)
        {
            _history.Clear();
            _history.Add("home");
            _history.Add("game:" + game);
            _selectedGame = game;
            _page = "room";
        }

        /// <summary>Каждый кадр: проявление, Esc, сообщение внизу гаснет.</summary>
        private void UpdateTavernFriends(float deltaTime, bool typing)
        {
            var escape = !typing && Keyboard.current != null && Keyboard.current.escapeKey.isPressed;
            if (FriendsShown && escape && !_friendsEscapeHeld && !_busy) Back();
            _friendsEscapeHeld = escape;
            if (!FriendsShown) return;
            _friendsFade = Mathf.MoveTowards(_friendsFade, 1, deltaTime / (NewsReduced ? .01f : .3f));
            _friendsGroup.alpha = Mathf.Clamp01(Mathf.SmoothStep(0, 1, _friendsFade) * 1.4f);
            if (_friendsToastPill.gameObject.activeSelf && Time.unscaledTime > _friendsToastUntil) _friendsToastPill.gameObject.SetActive(false);
            FitFriends();
        }

        /// <summary>Сообщение действия — внутри окна: общая плашка сообщений лежит под ним.</summary>
        private void FriendsToast(string text)
        {
            if (_friendsToastPill == null) return;
            _friendsToast.text = (text ?? "").Replace(" —", " —");
            var width = Mathf.Clamp(_friendsToast.preferredWidth + 56, 300, 1100);
            _friendsToastPill.sizeDelta = new Vector2(width, _friendsToastPill.sizeDelta.y);
            _friendsToast.rectTransform.sizeDelta = new Vector2(width - 32, _friendsToast.rectTransform.sizeDelta.y);
            _friendsToastPill.gameObject.SetActive(!string.IsNullOrEmpty(text));
            _friendsToastPill.SetAsLastSibling();
            _friendsToastUntil = Time.unscaledTime + 4f;
        }

        /// <summary>После каждой смены страницы: окно уходит со своей страницей.</summary>
        private void RefreshFriendsPresentation()
        {
            if (_friendsLayer == null || IsFriendsPage(_page) || !_friendsLayer.gameObject.activeSelf) return;
            _friendsLayer.gameObject.SetActive(false);
            _friendsShownPage = null;
        }

        private void FitFriends()
        {
            var viewport = FortuneViewport;
            var backdrop = _friendsBackdrop.rectTransform;
            backdrop.localScale = Vector3.one * Mathf.Max(viewport.x / backdrop.sizeDelta.x, viewport.y / backdrop.sizeDelta.y);
            var scale = Mathf.Min((viewport.x - 60) / MailCaseWidth, MailCaseMaxHeight / MailCaseHeight);
            _friendsCase.localScale = Vector3.one * scale * Mathf.Lerp(.94f, 1, Mathf.SmoothStep(0, 1, _friendsFade));
        }

        private static Vector2 FriendsAt(float x, float y) => new Vector2(x - MailCaseWidth / 2, MailCaseHeight / 2 - y);

        private void BuildFriendsScene()
        {
            _friendsLayer = StageLayer("Tavern friends");
            _friendsGroup = _friendsLayer.gameObject.AddComponent<CanvasGroup>();
            // Тот же притемнённый зал, что у почты: окно выглядит одинаково, откуда бы его ни открыли.
            _friendsBackdrop = LoanArt(_friendsLayer, "Friends tavern hall", TavernHall, Vector2.zero, PictureSize(TavernHall), new Color(.12f, .06f, .03f));
            _friendsBackdrop.raycastTarget = true;
            DimTavernBackdrop(_friendsBackdrop);
            TavernBackdropCatcher(_friendsLayer, "Friends shade");
            var title = LoanText(_friendsLayer, "Friends title", "ДРУЗЬЯ", new Vector2(0, 398), new Vector2(900, 60), 48, Color.white);
            var gold = title.gameObject.AddComponent<HubSignText>();
            gold.Top = SignGoldTop;
            gold.Bottom = SignGoldBottom;
            var titleShadow = title.gameObject.AddComponent<Shadow>();
            titleShadow.effectColor = new Color(.06f, .03f, 0, .85f);
            titleShadow.effectDistance = new Vector2(2, -3);

            var art = LoanArt(_friendsLayer, "Friends case", "tavern-mail-case", new Vector2(0, MailCaseY), new Vector2(MailCaseWidth, MailCaseHeight),
                new Color(.3f, .16f, .07f));
            art.raycastTarget = true;
            _friendsCase = art.rectTransform;

            // Кожа: закладки, поиск, список, «Твой стол».
            var tabWidth = (FriendsRight - FriendsLeft - 16) / 3;
            for (var i = 0; i < FriendsTabKeys.Length; i++)
            {
                var key = FriendsTabKeys[i];
                var button = LoanPaperButton(_friendsCase, FriendsTabNames[i], FriendsAt(FriendsLeft + tabWidth / 2 + i * (tabWidth + 8), FriendsTabsY),
                    new Vector2(tabWidth, 50), () => SelectFriendsTab(key));
                var seal = MailSeal(button.transform, "Waiting seal", new Vector2(tabWidth / 2 - 6, 22), 40, 23);
                seal.transform.parent.gameObject.SetActive(false);
                _friendsTabs.Add((button, seal));
            }
            BuildFriendsSearch();
            _friendsListView = LoanRect(_friendsCase, "Friends list view", Vector2.zero, Vector2.one);
            _friendsListView.gameObject.AddComponent<RectMask2D>();
            _friendsListView.gameObject.AddComponent<Image>().color = Color.clear;
            _friendsList = LoanRect(_friendsListView, "Friends list", Vector2.zero, Vector2.one);
            _friendsList.anchorMin = _friendsList.anchorMax = _friendsList.pivot = new Vector2(.5f, 1);
            var scroll = _friendsListView.gameObject.AddComponent<HubSmoothScroll>();
            scroll.viewport = _friendsListView;
            scroll.content = _friendsList;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30;
            scroll.WheelSmoothTime = .3f;
            BuildFriendsScrollbar(scroll);
            _friendsEmpty = LoanText(_friendsListView, "Empty list", "", new Vector2(0, 40), new Vector2(380, 120), 30, MailCream);

            _friendsStrip = LoanArt(_friendsCase, "My table strip", null, FriendsAt((FriendsLeft + FriendsRight) / 2, FriendsStripY),
                new Vector2(FriendsRight - FriendsLeft, 52), new Color(.16f, .07f, .03f, .92f)).rectTransform;
            var strip = _friendsStrip.GetComponent<Image>();
            strip.sprite = UiTextures.RoundedRect(64, 64, 12, Color.white);
            strip.type = Image.Type.Sliced;
            const float stripHalf = (FriendsRight - FriendsLeft) / 2;
            _friendsStripText = LoanText(_friendsStrip, "Table", "", new Vector2(-stripHalf + 12 + 142, 0), new Vector2(284, 44), 22, MailCream);
            _friendsStripText.alignment = TextAnchor.MiddleLeft;
            LoanPaperButton(_friendsStrip, "К столу", new Vector2(stripHalf - 10 - 56, 0), new Vector2(112, 40), GoToMyTable);

            // Пергамент: карточка выбранного — перерисовывается целиком.
            _friendsCard = LoanRect(_friendsCase, "Friend card", FriendsAt(MailParchment.center.x, MailParchment.center.y), MailParchment.size);

            var back = (RectTransform)CityButton(_friendsLayer, "НАЗАД", 24, 24, 170, 56, Back).transform;
            PinToCorner(back, -1);
            _friendsToastPill = LoanArt(_friendsLayer, "Friends message", null, new Vector2(0, -392), new Vector2(600, 52),
                new Color(.1f, .045f, .02f, .94f)).rectTransform;
            var pill = _friendsToastPill.GetComponent<Image>();
            pill.sprite = UiTextures.RoundedRect(64, 64, 24, Color.white);
            pill.type = Image.Type.Sliced;
            var pillRim = pill.gameObject.AddComponent<HubOutline>();
            pillRim.effectColor = new Color(SignGoldBottom.r, SignGoldBottom.g, SignGoldBottom.b, .8f);
            pillRim.effectDistance = new Vector2(2, -2);
            _friendsToast = LoanText(_friendsToastPill, "Text", "", Vector2.zero, new Vector2(568, 44), 25, MailCream);
            _friendsToastPill.gameObject.SetActive(false);
            FitFriends();
            _friendsLayer.gameObject.SetActive(false);
        }

        private void BuildFriendsSearch()
        {
            var field = LoanArt(_friendsCase, "Friends search", null, FriendsAt((FriendsLeft + FriendsRight) / 2, FriendsSearchY),
                new Vector2(FriendsRight - FriendsLeft, 48), new Color(1, .97f, .88f, .92f));
            field.sprite = UiTextures.RoundedRect(64, 64, 10, Color.white);
            field.type = Image.Type.Sliced;
            field.raycastTarget = true;
            var size = field.rectTransform.sizeDelta - new Vector2(28, 4);
            var typed = LoanText(field.transform, "Text", "", Vector2.zero, size, 26, SixBubbleInk);
            typed.alignment = TextAnchor.MiddleLeft;
            typed.rectTransform.sizeDelta = size;
            var hint = LoanText(field.transform, "Placeholder", "Имя игрока…", Vector2.zero, size, 26, MailMuted);
            hint.alignment = TextAnchor.MiddleLeft;
            hint.fontStyle = FontStyle.Italic;
            hint.rectTransform.sizeDelta = size;
            _friendsSearch = field.gameObject.AddComponent<InputField>();
            _friendsSearch.targetGraphic = field;
            _friendsSearch.textComponent = typed;
            _friendsSearch.placeholder = hint;
            _friendsSearch.characterLimit = 24;
            _friendsSearch.lineType = InputField.LineType.SingleLine;
            _friendsSearch.customCaretColor = true;
            _friendsSearch.caretColor = SixBubbleInk;
            _friendsSearch.selectionColor = new Color(.75f, .3f, .25f, .3f);
            // Список ищет на ходу; пергамент — первый найденный.
            _friendsSearch.onValueChanged.AddListener(value =>
            {
                _hubFriendSearch = value;
                _friendsPick = null;
                _friendsPicking = false;
                if (FriendsShown) PaintFriends();
            });
        }

        private void BuildFriendsScrollbar(HubSmoothScroll scroll)
        {
            var track = LoanArt(_friendsListView, "Friends scrollbar", null, Vector2.zero, Vector2.zero, Color.clear);
            var rect = track.rectTransform;
            rect.anchorMin = new Vector2(1, 0);
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(1, .5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(22, 0);
            track.raycastTarget = true;
            var rail = LoanArt(track.transform, "Rail", null, Vector2.zero, new Vector2(3, -8), new Color(.25f, .06f, .025f, .55f));
            rail.rectTransform.anchorMin = new Vector2(.5f, 0); rail.rectTransform.anchorMax = new Vector2(.5f, 1);
            var handle = LoanArt(track.transform, "Handle", null, Vector2.zero, Vector2.zero, Color.clear);
            var grip = LoanArt(handle.transform, "Grip", null, Vector2.zero, new Vector2(9, -6), SignGoldBottom);
            grip.rectTransform.anchorMin = new Vector2(.5f, 0); grip.rectTransform.anchorMax = new Vector2(.5f, 1);
            grip.sprite = UiTextures.RoundedRect(32, 64, 5, Color.white); grip.type = Image.Type.Sliced;
            _friendsScrollbar = track.gameObject.AddComponent<HubDragScrollbar>();
            _friendsScrollbar.direction = Scrollbar.Direction.BottomToTop;
            _friendsScrollbar.handleRect = handle.rectTransform;
            _friendsScrollbar.targetGraphic = grip;
            _friendsScrollbar.ScrollView = scroll;
            scroll.verticalScrollbar = _friendsScrollbar;
            // Иначе ScrollRect держит полосу включённой всегда — и над одним найденным игроком.
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        }

        private void PaintFriends()
        {
            if (_friendsLayer == null || _state == null) return;
            FitFriends();
            var tab = FriendsTab;
            var waiting = FriendsWaiting();
            for (var i = 0; i < _friendsTabs.Count; i++)
            {
                var (button, seal) = _friendsTabs[i];
                var on = FriendsTabKeys[i] == tab;
                button.GetComponent<Image>().color = on ? FriendsDark : new Color(.87f, .69f, .38f, .7f);
                button.transform.Find("Caption").GetComponent<Text>().color = on ? new Color(1, .91f, .68f) : LoanInk;
                var count = FriendsTabKeys[i] == "requests" ? waiting : 0;
                seal.text = count.ToString();
                seal.transform.parent.gameObject.SetActive(count > 0);
            }
            var table = FriendsMyTable();
            _friendsStrip.gameObject.SetActive(table != null);
            if (table != null)
                MailFitLine(_friendsStripText, "Твой стол: " + FriendsGameName(table.GameId) + " · " + table.Members.Count + " из " + table.MaxPlayers);
            _friendsSearch.transform.gameObject.SetActive(tab == "people");
            if (tab == "people" && _friendsSearch.text != (_hubFriendSearch ?? "")) _friendsSearch.SetTextWithoutNotify(_hubFriendSearch ?? "");
            var top = tab == "people" ? FriendsSearchY + 32 : FriendsTabsY + 37;
            var bottom = table != null ? FriendsStripY - 34 : MailLeather.yMax - 14;
            _friendsListView.anchoredPosition = FriendsAt((FriendsLeft + FriendsRight) / 2, (top + bottom) / 2);
            _friendsListView.sizeDelta = new Vector2(FriendsRight - FriendsLeft, bottom - top);

            var entries = FriendsEntries(tab);
            if (_friendsPick == null || entries.All(entry => entry.Key != _friendsPick)) { _friendsPick = entries.FirstOrDefault()?.Key; _friendsPicking = false; }
            PaintFriendsList(entries);
            var picked = entries.FirstOrDefault(entry => entry.Key == _friendsPick);
            if (picked != null) PaintFriendsCard(picked, tab);
            else PaintFriendsEmpty(tab);
        }

        private void PaintFriendsList(List<FriendsEntry> entries)
        {
            ClearChildren(_friendsList);
            var tab = FriendsTab;
            _friendsEmpty.gameObject.SetActive(entries.Count == 0);
            _friendsEmpty.text = tab == "requests" ? "Новых заявок нет" : tab == "people" ? "Никого не нашли" : "Друзей пока нет";
            var viewWidth = _friendsListView.sizeDelta.x;
            var width = viewWidth - 24;
            _friendsList.sizeDelta = new Vector2(width, 0);
            var y = 0f;
            foreach (var entry in entries)
            {
                var on = entry.Key == _friendsPick;
                var paper = LoanArt(_friendsList, "Entry " + entry.Key, null, Vector2.zero, new Vector2(width - 12, FriendsRowHeight),
                    on ? SixBubblePaper : FriendsPaper);
                var row = paper.rectTransform;
                row.anchorMin = row.anchorMax = new Vector2(.5f, 1);
                // Выбранная карточка чуть выдвинута к пергаменту и обведена золотом, как в почте.
                row.anchoredPosition = new Vector2(on ? 6 : -6, -y - FriendsRowHeight / 2);
                paper.sprite = UiTextures.RoundedRect(64, 64, 14, Color.white);
                paper.type = Image.Type.Sliced;
                paper.raycastTarget = true;
                var rim = paper.gameObject.AddComponent<HubOutline>();
                rim.effectColor = on ? SignGoldBottom : new Color(SixBubbleInk.r, SixBubbleInk.g, SixBubbleInk.b, .55f);
                rim.effectDistance = on ? new Vector2(3, -3) : new Vector2(1.5f, -1.5f);
                var button = paper.gameObject.AddComponent<Button>();
                button.targetGraphic = paper;
                var key = entry.Key;
                button.onClick.AddListener(() => SelectFriendsEntry(key));

                var rowWidth = width - 12;
                var face = LoanArt(row, "Portrait", null, new Vector2(-rowWidth / 2 + 38, 0), new Vector2(58, 68), Color.clear);
                face.sprite = AvatarCatalog.Find(entry.Person.AvatarId);
                face.color = face.sprite == null ? Color.clear : Color.white;
                face.preserveAspect = true;
                var textWidth = rowWidth - 80 - (entry.Alert ? 60 : 14);
                var textX = -rowWidth / 2 + 76 + textWidth / 2;
                var name = LoanText(row, "Name", "", new Vector2(textX, 16), new Vector2(textWidth, 38), 29, SixBubbleInk, true);
                name.alignment = TextAnchor.MiddleLeft;
                MailFitLine(name, entry.Person.Name);
                var line = LoanText(row, "Line", "", new Vector2(textX, -19), new Vector2(textWidth, 30), 22, entry.Alert ? FriendsAlert : MailMuted);
                line.alignment = TextAnchor.MiddleLeft;
                MailFitLine(line, entry.Line);
                if (entry.Alert) MailSeal(row, "Alert seal", new Vector2(rowWidth / 2 - 32, 0), 50, 30).text = "!";
                y += FriendsRowHeight + FriendsRowGap;
            }
            _friendsList.sizeDelta = new Vector2(width, Mathf.Max(0, y - FriendsRowGap));
            var view = _friendsListView.sizeDelta.y;
            var limit = Mathf.Max(0, _friendsList.sizeDelta.y - view);
            var top = Mathf.Clamp(_friendsList.anchoredPosition.y, 0, limit);
            // Выбранная карточка — в окне списка: открыли на игроке, сменили закладку, а список был прокручен.
            var picked = entries.FindIndex(entry => entry.Key == _friendsPick);
            if (picked >= 0)
            {
                var rowTop = picked * (FriendsRowHeight + FriendsRowGap);
                if (rowTop < top) top = rowTop;
                else if (rowTop + FriendsRowHeight > top + view) top = rowTop + FriendsRowHeight - view;
            }
            _friendsList.anchoredPosition = new Vector2(-12, Mathf.Clamp(top, 0, limit));
            // Ползунок — в согласии со списком: его значение по умолчанию (0 — низ) иначе уводит список в конец.
            _friendsScrollbar.SetValueWithoutNotify(limit > 1 ? 1 - _friendsList.anchoredPosition.y / limit : 1);
            _friendsScrollbar.gameObject.SetActive(limit > 1);
        }

        // ---------- Пергамент ----------

        private static Vector2 FriendsCardAt(float x, float y) => new Vector2(x - MailParchment.width / 2, MailParchment.height / 2 - y);

        private Text FriendsCardText(string name, string text, float x, float y, float width, float height, int size, Color color, bool display = false)
        {
            var label = LoanText(_friendsCard, name, text.Replace(" —", " —"), FriendsCardAt(x, y), new Vector2(width, height), size, color, display);
            return label;
        }

        private Button FriendsCardButton(string text, float x, float y, float width, float height, Action action, bool primary = false)
            => LoanPaperButton(_friendsCard, text, FriendsCardAt(x, y), new Vector2(width, height), action, primary);

        /// <summary>Строка кнопок по центру пергамента: одна, две или три.</summary>
        private void FriendsCardButtons(float y, float height, params (string Text, Action Action, bool Primary)[] buttons)
        {
            var shown = buttons.Where(button => button.Text != null).ToArray();
            if (shown.Length == 0) return;
            const float gap = 16;
            var width = shown.Length == 1 ? 400 : Mathf.Min(320, (MailParchment.width - 2 * MailPad - gap * (shown.Length - 1)) / shown.Length);
            var total = shown.Length * width + (shown.Length - 1) * gap;
            for (var i = 0; i < shown.Length; i++)
                FriendsCardButton(shown[i].Text, MailParchment.width / 2 - total / 2 + width / 2 + i * (width + gap), y, width, height,
                    shown[i].Action, shown[i].Primary);
        }

        /// <summary>Пусто: записка Дамы, как в почте, и куда пойти.</summary>
        private void PaintFriendsEmpty(string tab)
        {
            ClearChildren(_friendsCard);
            var text = tab == "requests" ? "Заявок пока нет. Позовут за стол или захотят дружить — сложу сюда."
                : tab == "people" ? "Никого с таким именем. Попробуй иначе — или загляни в Клуб: за столом и познакомитесь."
                : "Друзей пока нет. Загляни в «Найти» — завсегдатаи будут рады.";
            FriendsCardText("Note", text, MailParchment.width / 2, 170, MailParchment.width - 110, 180, 32, SixBubbleInk);
            FriendsCardText("Signature", "— Дама червей", MailParchment.width - 200, 275, 300, 44, 28, MailMuted).fontStyle = FontStyle.Italic;
            if (tab != "people")
                FriendsCardButton(tab == "requests" ? "К друзьям" : "Найти", MailParchment.width / 2, 370, 300, 54,
                    () => SelectFriendsTab(tab == "requests" ? "friends" : "people"), true);
        }

        private void PaintFriendsCard(FriendsEntry entry, string tab)
        {
            ClearChildren(_friendsCard);
            var person = entry.Person;
            var id = person.Id;
            var me = _state.Profile.Id;
            var friend = _state.Friends.Contains(id);
            // На «Заявках» карточка — про выбранную заявку; в остальных — что сейчас важнее всего между вами.
            var invite = tab == "requests" ? entry.Invite : FriendsInviteFrom(id);
            var request = tab == "requests" ? entry.Request : FriendsRequestWith(id);
            var table = FriendsMyTable();
            var seated = table != null && table.Members.Any(member => member.PlayerId == id);

            var face = LoanArt(_friendsCard, "Portrait", null, FriendsCardAt(MailPad + 70, 20 + 85), new Vector2(140, 170), Color.clear);
            face.sprite = AvatarCatalog.Find(person.AvatarId);
            face.color = face.sprite == null ? Color.clear : Color.white;
            face.preserveAspect = true;
            const float textLeft = MailPad + 160;
            var textWidth = MailParchment.width - MailPad - textLeft;
            var name = FriendsCardText("Name", "", textLeft + textWidth / 2, 50, textWidth, 56, 44, SixBubbleInk, true);
            name.alignment = TextAnchor.MiddleLeft;
            MailFitLine(name, person.Name);
            var info = FriendsCardText("Info", "", textLeft + textWidth / 2, 104, textWidth, 34, 26, MailMuted);
            info.alignment = TextAnchor.MiddleLeft;
            MailFitLine(info, "Уровень " + person.Level + " · " + HubRankName(person.Rating) + " · " + HubMoney(person.Rating));
            var relation = seated ? "Сидит за твоим столом" : friend ? "В друзьях" : "Ещё не друзья";
            var status = FriendsCardText("Relation", "", textLeft + textWidth / 2, 146, textWidth, 34, 26, seated ? FriendsAlert : SixBubbleInk);
            status.alignment = TextAnchor.MiddleLeft;
            MailFitLine(status, relation + (string.IsNullOrEmpty(person.Status) ? "" : " · " + person.Status.ToLowerInvariant()));
            LoanArt(_friendsCard, "Rule", null, FriendsCardAt(MailParchment.width / 2, 204), new Vector2(MailParchment.width - 2 * MailPad, 2),
                new Color(SixBubbleInk.r, SixBubbleInk.g, SixBubbleInk.b, .4f));

            if (_friendsPicking && friend) { PaintFriendsPicker(person); return; }
            string note;
            if (invite != null)
            {
                var room = _state.Rooms.FirstOrDefault(item => item.Id == invite.RoomId);
                var open = room != null && !room.Started && room.Members.Count < room.MaxPlayers && room.Members.All(member => member.PlayerId != me);
                note = open ? "Зовёт тебя за стол:\n" + FriendsTableLine(room) : "Звал тебя за стол, но там уже нет места.";
                FriendsNote(note);
                FriendsCardButtons(352, 56, (open ? "Сесть за стол" : null, () => Send("invite_accept", invite.Id), true),
                    ("Отказать", () => Send("invite_decline", invite.Id), false));
            }
            else if (request != null && request.ToId == me)
            {
                FriendsNote("Хочет с тобой дружить" + (string.IsNullOrEmpty(request.Game) ? "." : " — вы играли в «" + FriendsGameName(request.Game) + "»."));
                FriendsCardButtons(352, 56, ("Принять", () => Send("friend_accept", request.Id), true),
                    ("Отклонить", () => Send("friend_decline", request.Id), false));
            }
            else if (request != null)
            {
                FriendsNote("Заявка отправлена — ждём ответа.");
                FriendsCardButtons(352, 56, ("Отменить заявку", () => Send("friend_cancel", request.Id), false));
            }
            else if (friend)
            {
                if (table != null && seated)
                {
                    FriendsNote("Ждёт тебя за столом:\n" + FriendsTableLine(table));
                    FriendsCardButtons(352, 56, ("К столу", GoToMyTable, true), ("Другая игра", () => PickFriendsGame(true), false));
                }
                else if (table != null && table.Members.Count < table.MaxPlayers)
                {
                    FriendsNote("Позови за свой стол:\n" + FriendsTableLine(table));
                    FriendsCardButtons(352, 56, ("Позвать за стол", () => Send("invite_friend", id), true),
                        ("Другая игра", () => PickFriendsGame(true), false));
                }
                else
                {
                    FriendsNote(table != null ? "За твоим столом нет мест — можно начать другую игру." : "Выбери игру — и вы вместе пойдёте к её столу.");
                    FriendsCardButtons(352, 56, ("Позвать играть", () => PickFriendsGame(true), true));
                }
            }
            else
            {
                FriendsNote("Добавь в друзья — и зови за стол.");
                FriendsCardButtons(352, 56, ("Добавить в друзья", () => Send("friend_request", id), true));
            }
            if (!friend) return;
            var confirm = _friendsConfirmRemove == id;
            FriendsCardButtons(440, 46, ("Написать", () => Navigate("chat:" + id), false),
                (confirm ? "Точно удалить?" : "Удалить из друзей", () =>
                {
                    if (_friendsConfirmRemove != id) { _friendsConfirmRemove = id; PaintFriends(); }
                    else { _friendsConfirmRemove = null; Send("friend_remove", id); }
                }, false));
            if (confirm) _friendsCard.GetComponentsInChildren<Text>().Last().color = FriendsAlert;
        }

        private void FriendsNote(string text)
        {
            FriendsCardText("Note", text, MailParchment.width / 2, 268, MailParchment.width - 2 * MailPad - 20, 96, 28, SixBubbleInk);
        }

        private void PickFriendsGame(bool picking)
        {
            if (_busy || !FriendsShown) return;
            _friendsPicking = picking;
            _friendsConfirmRemove = null;
            PaintFriends();
        }

        /// <summary>Во что сыграть с другом: пять игр, свой стол без ставки; друг садится сразу, игрок едет к столу.</summary>
        private void PaintFriendsPicker(HubPerson person)
        {
            FriendsCardText("Question", "Во что сыграем?", MailParchment.width / 2, 236, MailParchment.width - 2 * MailPad, 44, 32, SixBubbleInk, true);
            const float width = 208, gap = 14;
            var left = MailParchment.width / 2 - (3 * width + 2 * gap) / 2 + width / 2;
            for (var i = 0; i <= FriendsGames.Length; i++)
            {
                var x = left + i % 3 * (width + gap);
                var y = 298 + i / 3 * 68;
                if (i == FriendsGames.Length) { FriendsCardButton("Отмена", x, y, width, 56, () => PickFriendsGame(false)); break; }
                var game = FriendsGames[i];
                FriendsCardButton(FriendsGameName(game), x, y, width, 56, () => Send("friend_play", person.Id, game), true);
            }
            var hint = FriendsCardText("Hint", "", MailParchment.width / 2, 440, MailParchment.width - 2 * MailPad, 34, 23, MailMuted);
            MailFitLine(hint, "Свой стол без ставки, вход по приглашению. " + person.Name + " сядет сразу.");
        }
    }
}
