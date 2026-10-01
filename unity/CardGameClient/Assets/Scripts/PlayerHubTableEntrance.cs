using System;
using System.Linq;
using CardGame.Core.Meta;
using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    public sealed partial class PlayerHubController
    {
        private enum TableMode { Bots, Quick, Create, Code }
        private TableMode _tableMode;
        private string _tableModeGame;

        private void ChooseTableMode(TableMode mode)
        {
            _tableMode = mode;
            Toast("");
            Render();
        }

        private Text TableText(string text, float x, float y, float width, float height, int size = 21, bool muted = false)
        {
            var label = MakeLabel(_content, text, x, y, width, height, size);
            label.color = muted ? ClubMuted : ClubCream;
            label.alignment = TextAnchor.MiddleLeft;
            return label;
        }

        private HubRoom[] AvailableTables(string game) => _state.Rooms.Where(room => room.GameId == game
            && !room.Private && !room.Started && room.Stake == 0 && room.Members.Count < room.MaxPlayers
            && room.Id != _state.CurrentRoomId).ToArray();

        private void JoinAvailableTable(string game)
        {
            var room = AvailableTables(game).FirstOrDefault();
            if (room == null) { Toast("Свободных столов пока нет. Создай комнату или сыграй с ботами."); Render(); return; }
            Send("join_room", room.Id);
        }

        private void JoinTableCode(string game)
        {
            var code = (_roomCode ?? "").Trim().ToUpperInvariant();
            var room = _state.Rooms.FirstOrDefault(candidate => string.Equals(candidate.Code, code, StringComparison.OrdinalIgnoreCase));
            if (room != null && room.GameId != game)
            { Toast("Этот код для игры «" + GameTitle(room.GameId) + "». Выбери её стол."); return; }
            Send("join_room", text: code);
        }

        /// <summary>Shared entrance: live table on the left, rules and four explicit ways to play on the right.</summary>
        private void BuildTableEntrance(string game)
        {
            if (_tableModeGame != game) { _tableModeGame = game; _tableMode = TableMode.Bots; }
            PrepareGameSetup(_tableMode == TableMode.Create ? "create" : "quick", game);
            var settings = ActiveGameSetup();
            const float x = 1600 - PlaqueMargin - PlaqueWidth, pad = 36, inner = PlaqueWidth - pad * 2;
            var left = x + pad;
            var board = ClubBoard(_content, "Table plaque", x, PlaqueTop, PlaqueWidth, 820, 24);
            board.raycastTarget = true;
            var shadow = board.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, .65f);
            shadow.effectDistance = new Vector2(-8, -6);

            // Both controls stay beside the table instead of opposite corners of the screen.
            CityButton(_content, "ОТОЙТИ", x - 208, PlaqueTop + 8, 184, 56, Back);
            var title = TableText(GameTitle(game), left, 64, inner - 126, 62, 42);
            title.font = ClubTitleFont(); title.color = ClubGold;
            title.resizeTextForBestFit = true; title.resizeTextMinSize = 26; title.resizeTextMaxSize = 42;
            ClubButton(_content, "Правила", x + PlaqueWidth - pad - 116, 74, 116, 48,
                () => Navigate("rules:" + game), size: 23);
            TableText(GameBlurb(game), left, 128, inner, 44, 20, true);
            var modes = new[] { "С ботами", "Быстрая игра", "Своя комната", "По коду" };
            var tabWidth = (inner - 24) / 4;
            for (var i = 0; i < modes.Length; i++)
            {
                var mode = (TableMode)i;
                ClubButton(_content, modes[i], left + i * (tabWidth + 8), 186, tabWidth, 60,
                    () => ChooseTableMode(mode), _tableMode == mode ? ClubTone.Primary : ClubTone.Normal, 25);
            }

            var page = _content;
            var key = "table-" + game + "-" + _tableMode;
            var viewport = MakeRect(page, "Scroll " + key, left, 264, inner, 442);
            viewport.gameObject.AddComponent<Image>().color = Color.clear;
            viewport.gameObject.AddComponent<RectMask2D>();
            var body = MakeRect(viewport, "Table settings", 0, 0, inner - 20, 442);
            var scroll = viewport.gameObject.AddComponent<HubSmoothScroll>();
            scroll.viewport = viewport; scroll.content = body; scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28; scroll.decelerationRate = .12f;
            var height = 0f;
            try
            {
                _content = body;
                height = BuildTableMode(game, settings, inner - 20);
            }
            finally { _content = page; }
            body.sizeDelta = new Vector2(inner - 20, Mathf.Max(442, height));
            _scrollMemory.TryGetValue(key, out var saved);
            body.anchoredPosition = new Vector2(0, Mathf.Clamp(saved, 0, Mathf.Max(0, height - 442)));
            if (height > 442)
            {
                var track = MakeRect(viewport, "Scrollbar", inner - 8, 0, 6, 442);
                track.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, .35f);
                var handle = MakeRect(track, "Handle", 0, 0, 6, 80);
                var graphic = handle.gameObject.AddComponent<Image>(); graphic.color = ClubGold;
                var bar = track.gameObject.AddComponent<Scrollbar>();
                bar.handleRect = handle; bar.targetGraphic = graphic;
                bar.direction = Scrollbar.Direction.BottomToTop;
                bar.SetValueWithoutNotify(1 - Mathf.Clamp01(saved / (height - 442)));
                scroll.verticalScrollbar = bar;
            }
            var divider = MakeRect(page, "Plaque divider", left, 720, inner, 2).gameObject.AddComponent<Image>();
            divider.color = new Color(.85f, .64f, .3f, .6f); divider.raycastTarget = false;
            TableText(_tableMode == TableMode.Bots || _tableMode == TableMode.Create
                ? TableSummary(game, settings, _roomCapacity)
                : "Комнаты пока локальные · онлайн появится позже", left, 730, inner, 40, 19, true);
            var action = _tableMode == TableMode.Bots ? "Начать с ботами" : _tableMode == TableMode.Quick
                ? "Найти свободный стол" : _tableMode == TableMode.Create ? "Создать комнату" : "Войти по коду";
            var primary = ClubButton(page, action, left, 780, inner, 56, () =>
            {
                switch (_tableMode)
                {
                    case TableMode.Bots: SitAtTable(game); break;
                    case TableMode.Quick: JoinAvailableTable(game); break;
                    case TableMode.Create: CreateRoom(); break;
                    case TableMode.Code: JoinTableCode(game); break;
                }
            }, ClubTone.Primary, 30);
            if (_tableMode == TableMode.Quick) primary.interactable = AvailableTables(game).Length > 0;
            if (_tableMode == TableMode.Code)
            {
                primary.interactable = !string.IsNullOrWhiteSpace(_roomCode);
                body.GetComponentInChildren<InputField>().onValueChanged.AddListener(value => primary.interactable = !string.IsNullOrWhiteSpace(value));
            }
            if (_tableMode == TableMode.Create)
            {
                primary.interactable = !string.IsNullOrWhiteSpace(_roomName);
                body.GetComponentsInChildren<InputField>()[0].onValueChanged.AddListener(value => primary.interactable = !string.IsNullOrWhiteSpace(value));
            }
        }

        private float BuildTableMode(string game, HubGameSettings settings, float width)
        {
            if (_tableMode == TableMode.Bots)
            {
                TableText("Ты и " + Numbered(_roomCapacity - 1, "бот", "бота", "ботов") + ". Выбери правила и начинай игру.", 0, 0, width, 48);
                return BuildGameRuleControls(settings, 0, 0, width, 64, true) + 8;
            }
            if (_tableMode == TableMode.Create)
            {
                TableText("1. Настрой комнату   2. Получи код   3. Собери игроков", 0, 0, width, 48, 20);
                TableText("Название комнаты", 0, 56, width, 28, 20, true);
                var name = Input(_roomName, 0, 92, width); name.characterLimit = 32;
                name.onValueChanged.AddListener(value => _roomName = value);
                TableText("Кто может войти", 0, 152, width, 28, 20, true);
                var half = (width - 12) / 2;
                ClubButton(_content, "Открытая", 0, 188, half, 48, () => { _roomPrivate = false; Render(); },
                    !_roomPrivate ? ClubTone.Primary : ClubTone.Normal, 24);
                ClubButton(_content, "Только по коду", half + 12, 188, half, 48, () => { _roomPrivate = true; Render(); },
                    _roomPrivate ? ClubTone.Primary : ClubTone.Normal, 24);
                TableText(_roomPrivate ? "Не видна в списке. Вход по приглашению или коду." : "Видна в списке комнат. Войти можно и по коду.", 0, 242, width, 42, 18, true);
                TableText("Ставка · 0 — без ставки", 0, 298, width - 200, 36, 20);
                var stake = Input(_roomStake, width - 180, 294, 180); stake.contentType = InputField.ContentType.IntegerNumber;
                stake.onValueChanged.AddListener(value => _roomStake = value);
                TableText("Тун-тун коины. Фишки покера считаются отдельно.", 0, 344, width, 34, 18, true);
                TableText("Правила комнаты", 0, 392, width, 36, 25);
                return BuildGameRuleControls(settings, 0, 0, width, 444, true) + 12;
            }
            if (_tableMode == TableMode.Code)
            {
                TableText("Войти в комнату друга", 0, 4, width, 42, 27);
                TableText("Попроси код у создателя комнаты и введи его ниже.\nКод должен быть от этой игры.", 0, 60, width, 76, 21, true);
                TableText("Код комнаты", 0, 158, width, 30, 20);
                var code = Input(_roomCode, 0, 202, width); code.characterLimit = 16;
                code.onValueChanged.AddListener(value => _roomCode = value);
                var placeholder = MakeLabel(code.transform, "Например: DURAK1", 14, 4, width - 28, 36, 21);
                placeholder.color = ClubMuted; placeholder.alignment = TextAnchor.MiddleLeft; code.placeholder = placeholder;
                TableText("Сейчас работают коды комнат, созданных в этом профиле.", 0, 268, width, 64, 19, true);
                AddCurrentTableLink(game, width, 350);
                return 432;
            }
            var rooms = AvailableTables(game);
            TableText("Присоединиться к открытому столу", 0, 4, width, 42, 26);
            TableText("Подберём комнату без ставки, где есть свободное место.\nПравила задаёт хозяин комнаты.", 0, 60, width, 74, 21, true);
            TableText(rooms.Length > 0 ? "Доступно столов: " + rooms.Length : "Свободных столов пока нет", 0, 156, width, 42, 26);
            TableText(rooms.Length > 0 ? "После входа проверь правила, отметь готовность и дождись начала партии."
                : "Создай свою комнату или начни партию с ботами — ждать игроков не нужно.", 0, 218, width, 76, 21, true);
            ClubButton(_content, "Список комнат", 0, 312, (width - 12) / 2, 54,
                () => { _roomListPage = 0; Navigate("rooms"); }, size: 25);
            ClubButton(_content, "Создать свою", (width + 12) / 2, 312, (width - 12) / 2, 54,
                () => ChooseTableMode(TableMode.Create), size: 25);
            AddCurrentTableLink(game, width, 378);
            return CurrentRoom() != null ? 460 : 380;
        }

        private void AddCurrentTableLink(string game, float width, float y)
        {
            var room = CurrentRoom();
            if (room == null) return;
            ClubButton(_content, "Моя комната · " + room.Name, 0, y, width, 60, () => Navigate("room"), size: 25);
        }
    }
}
