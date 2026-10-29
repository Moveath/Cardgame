using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CardGame.Core.Meta;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>
    /// Казино Тузяры и Дамы треф изнутри — «Карточный дворец» (2026-10-22, IDEAS.md «Казино Тузяры») —
    /// этаж <see cref="CasinoLevel"/>, картинка `UI/Hub/casino-hall` (2196×941, 21:9; исходник
    /// `screens/references/city/casino-hall-v2-wide.png`).
    /// Вход — нырок в здание казино в районе Тузяры (и с карты). Выход — парадные двери слева, «ВЫЙТИ», «Назад», Esc или S.
    /// Зоны зала по категориям азарта:
    /// 1. Карточный стол слева (`casino-cards`) — Покер и «21 / Блэкджек» (масштабируется под новые карточные игры);
    /// 2. Касса и витрина Дамы треф в центре (`casino-cashier`) — бесплатные жетоны дня, обмен коинов ↔ жетонов и призы Хайроллера;
    /// 3. VIP-балкон Тузяры наверху (`casino-tuzyara`) — реплики хозяина казино сверху вниз;
    /// 4. Стол Крупье Десятки треф справа (`casino-roulette`) — Карточная Рулетка и Кости Тузяры;
    /// 5. Арки Автоматов и Ракеты справа в глубине (`casino-arcade`) — Слоты «777» и Крэш-игра «Туз-Ракета»;
    /// 6. Рояль с Пятёркой треф слева в глубине (`casino-piano`) — атмосфера и реплики пианиста.
    /// </summary>
    public sealed partial class PlayerHubController
    {
        private const string CasinoHall = "casino-hall";
        private const int CasinoLevel = 10;

        private const string CasinoExitId = "casino-exit";
        private const string CasinoCardsId = "casino-cards";
        private const string CasinoPianoId = "casino-piano";
        private const string CasinoCashierId = "casino-cashier";
        private const string CasinoTuzyaraId = "casino-tuzyara";
        private const string CasinoRouletteId = "casino-roulette";
        private const string CasinoArcadeId = "casino-arcade";

        private static readonly Color CasinoGold = new Color(.96f, .78f, .34f);
        private static readonly Color CasinoDark = new Color(.06f, .05f, .04f, .97f);
        private static readonly Color CasinoEmerald = new Color(.06f, .24f, .16f, .98f);
        private static readonly Color CasinoCrimson = new Color(.48f, .09f, .12f, 1f);

        private static readonly string[] CasinoTuzyaraTalk =
        {
            "Добро пожаловать в мой Карточный дворец! Бери жетоны у Дамы треф — и покажи, на что способен.",
            "Слева — карты: Покер и 21. Справа у Десятки — рулетка и кости. В арке — автоматы и моя золотая ракета!",
            "Проигрался до нитки? Не беда: через стенку мой Банк — выпишем кредит по-дружески.",
            "Заведение всегда в плюсе, но смелым я иногда позволяю унести джекпот.",
        };

        private static readonly string[] CasinoPianoTalk =
        {
            "Пятёрка треф не отрываясь от клавиш берёт джазовый аккорд — в зале пахнет золотом и азартом.",
            "«Для тех, кто идёт ва-банк — особенная мелодия!»",
        };

        private RectTransform _casinoBubble;
        private RectTransform _casinoLayer, _casinoCardsPanel, _casinoCashierPanel, _casinoRoulettePanel, _casinoArcadePanel;
        private RectTransform _casinoCardsContent, _casinoCashierContent, _casinoRouletteContent, _casinoArcadeContent;
        private Text _casinoBubbleText;
        private string _casinoOpen, _casinoLine;
        private int _casinoTalkIndex, _casinoBet = 3;
        private string _casinoRouletteTab = "roulette";
        private string _casinoArcadeTab = "slots";
        private bool _casinoGreeted, _casinoEscapeHeld, _casinoBusy;
        private HubSnapshot _casinoPaintedFor;

        private bool CasinoInside => _cityLevel == CasinoLevel && _page == "home" && _diveTarget < 0 && _visitPhase == 0;
        /// <summary>Открыто окно зоны поверх зала Казино.</summary>
        private bool CasinoShown => _casinoOpen != null && _cityLevel == CasinoLevel && _page == "home";

        private static CityPlace CasinoExitPlace() => Stairs(CasinoExitId, CasinoHall, 0, 415, 420,
            new float[] { 290, 625, 290, 210, 345, 145, 415, 130, 485, 145, 540, 210, 540, 625 });

        private static CityPlace CasinoCardsPlace() => Place(CasinoCardsId, CasinoHall, "Карточные столы (Покер · 21)", "Казино", null,
            485, 740, new float[] {
                85, 935, 85, 650, 220, 615, 730, 615, 890, 650, 890, 935
            }, "game:poker", direct: true);

        private static CityPlace CasinoPianoPlace() => Place(CasinoPianoId, CasinoHall, "Рояль", "Пятёрка треф", null,
            700, 495, new float[] {
                605, 605, 605, 420, 675, 385, 795, 385, 795, 605
            }, talk: true);

        private static CityPlace CasinoCashierPlace() => Place(CasinoCashierId, CasinoHall, "Касса жетонов и VIP-призы", "Дама треф", null,
            1100, 520, new float[] {
                915, 705, 915, 480, 965, 340, 1100, 310, 1235, 340, 1285, 480, 1285, 705
            });

        private static CityPlace CasinoTuzyaraPlace() => Place(CasinoTuzyaraId, CasinoHall, "VIP-балкон Босса", "Тузяра", null,
            1095, 135, new float[] {
                850, 255, 850, 110, 960, 20, 1230, 20, 1345, 110, 1345, 255
            }, talk: true);

        private static CityPlace CasinoRoulettePlace() => Place(CasinoRouletteId, CasinoHall, "Рулетка и Кости", "Десятка треф — крупье", null,
            1720, 720, new float[] {
                1330, 935, 1330, 650, 1480, 620, 1525, 475, 1690, 475, 1795, 620, 2190, 650, 2190, 935
            });

        private static CityPlace CasinoArcadePlace() => Place(CasinoArcadeId, CasinoHall, "Слоты «777» и Туз-Ракета", "Зал автоматов", null,
            1860, 340, new float[] {
                1560, 465, 1560, 165, 1690, 95, 1860, 95, 2190, 135, 2190, 580, 1810, 580
            });

        private void AddCasinoHall(RectTransform floor, float width)
        {
            var picture = PictureSize(CasinoHall);
            var unit = new Vector2(width / picture.x, CityHeight / picture.y);
            var bubbleAt = new Vector2((1100f / picture.x - .5f) * width, (.5f - 255f / picture.y) * CityHeight);
            var (bubble, line, _) = SpeechBubble(floor, "Casino bubble", bubbleAt, 520, 112, 0, unit);
            _casinoBubble = bubble;
            _casinoBubbleText = line;
            PaintCasino();
        }

        private void MoveCasinoBubble(float x, float y)
        {
            if (_casinoBubble == null) return;
            var picture = PictureSize(CasinoHall);
            var width = _floorWidths[CasinoLevel];
            if (width <= 0) return;
            _casinoBubble.anchoredPosition = new Vector2((x / picture.x - .5f) * width, (.5f - y / picture.y) * CityHeight);
        }

        private void ClickCasino(string id)
        {
            if (!CasinoInside || CasinoShown || _state == null) return;
            if (id == CasinoTuzyaraId)
            {
                _casinoLine = CasinoTuzyaraTalk[_casinoTalkIndex++ % CasinoTuzyaraTalk.Length];
                MoveCasinoBubble(1095, 255);
                PaintCasino();
                return;
            }
            if (id == CasinoPianoId)
            {
                _casinoLine = CasinoPianoTalk[_casinoTalkIndex++ % CasinoPianoTalk.Length];
                MoveCasinoBubble(700, 340);
                PaintCasino();
                return;
            }
            if (id == CasinoCardsId) OpenCasino("cards");
            else if (id == CasinoCashierId) OpenCasino("cashier");
            else if (id == CasinoRouletteId) OpenCasino("roulette");
            else if (id == CasinoArcadeId) OpenCasino("arcade");
        }

        private void OpenCasino(string section)
        {
            if (_casinoLayer == null) BuildCasinoLayer();
            _casinoOpen = section;
            _casinoLayer.gameObject.SetActive(true);
            PaintCasino();
            _casinoLayer.SetAsLastSibling();
        }

        private bool TryCasinoBack()
        {
            if (!CasinoShown) return false;
            _casinoOpen = null;
            PaintCasino();
            return true;
        }

        private void UpdateCasino(float deltaTime, bool typing)
        {
            if (_cityLevel != CasinoLevel)
            {
                _casinoGreeted = false;
                _casinoOpen = null;
            }
            var inside = CasinoInside && _state != null;
            var controls = inside && !_mapOpen && _mapFade <= 0;
            var showLayer = controls && _casinoOpen != null;
            if (_casinoLayer != null && _casinoLayer.gameObject.activeSelf != showLayer)
                _casinoLayer.gameObject.SetActive(showLayer);

            var keyboard = typing ? null : Keyboard.current;
            var escape = keyboard != null && keyboard.escapeKey.isPressed;
            if (CasinoShown && escape && !_casinoEscapeHeld) { _cityVerticalHeld = 1; TryCasinoBack(); }
            _casinoEscapeHeld = escape;

            if (!inside) return;
            if (!_casinoGreeted)
            {
                _casinoGreeted = true;
                var now = PlayerHubRuntime.Clock.UnixSeconds;
                _casinoLine = HubCasino.CanClaimDaily(_state, now)
                    ? "Дама треф: «Добро пожаловать! Загляни ко мне в кассу за бесплатными жетонами дня!»"
                    : "Дама треф: «Ставки открыты! Слева — Покер и 21, справа — Рулетка, в арке — Слоты и Ракета.»";
                MoveCasinoBubble(1100, 275);
                PaintCasino();
            }
            if (showLayer) _casinoLayer.SetAsLastSibling();
            if (_casinoPaintedFor != _state) PaintCasino();
        }

        private void BuildCasinoLayer()
        {
            _casinoLayer = StageLayer("Casino overlay");
            var dim = LoanArt(_casinoLayer, "Casino shade", null, Vector2.zero, new Vector2(6000, 6000), new Color(0, 0, 0, .72f));
            dim.raycastTarget = true;

            _casinoCardsPanel = BuildCasinoWindow(_casinoLayer, "Casino cards window", "КАРТОЧНЫЕ СТОЛЫ КАЗИНО · ПОКЕР И 21", out _casinoCardsContent);
            _casinoCashierPanel = BuildCasinoWindow(_casinoLayer, "Casino cashier window", "КАССА ДАМЫ ТРЕФ · ЖЕТОНЫ И VIP-ПРИЗЫ", out _casinoCashierContent);
            _casinoRoulettePanel = BuildCasinoWindow(_casinoLayer, "Casino roulette window", "СТОЛ ДЕВЯТКИ И ДЕСЯТКИ ТРЕФ · РУЛЕТКА И КОСТИ", out _casinoRouletteContent);
            _casinoArcadePanel = BuildCasinoWindow(_casinoLayer, "Casino arcade window", "ЗАЛ АВТОМАТОВ · СЛОТЫ «777» И ТУЗ-РАКЕТА", out _casinoArcadeContent);

            CityButton(_casinoLayer, "ЗАКРЫТЬ", (1600 - 260) / 2f, 900 - 22 - 58, 260, 58, () => TryCasinoBack());
            _casinoLayer.gameObject.SetActive(false);
        }

        private RectTransform BuildCasinoWindow(RectTransform parent, string name, string title, out RectTransform content)
        {
            var root = LoanRect(parent, name, Vector2.zero, new Vector2(1600, 900));
            var frame = LoanArt(root, "Window frame", null, new Vector2(0, 16), new Vector2(1380, 720), CasinoDark);
            frame.sprite = UiTextures.RoundedRect(64, 64, 24, Color.white);
            frame.type = Image.Type.Sliced;
            frame.raycastTarget = true;
            var rim = frame.gameObject.AddComponent<HubOutline>();
            rim.effectColor = CasinoGold;
            rim.effectDistance = new Vector2(3, -3);

            var inner = LoanArt(root, "Window felt", null, new Vector2(0, -10), new Vector2(1332, 624), CasinoEmerald);
            inner.sprite = UiTextures.RoundedRect(64, 64, 16, Color.white);
            inner.type = Image.Type.Sliced;

            LoanText(root, "Window title", title, new Vector2(0, 342), new Vector2(1240, 44), 32, CasinoGold, true);
            content = LoanRect(root, "Content", new Vector2(0, -10), new Vector2(1300, 604));
            return root;
        }

        private void PaintCasino()
        {
            if (_state == null) return;
            _casinoPaintedFor = _state;

            if (_casinoBubble != null)
            {
                var showBubble = !string.IsNullOrEmpty(_casinoLine) && !CasinoShown;
                _casinoBubble.gameObject.SetActive(showBubble);
                if (showBubble) _casinoBubbleText.text = _casinoLine;
            }

            if (_casinoLayer == null) return;
            _casinoCardsPanel.gameObject.SetActive(_casinoOpen == "cards");
            _casinoCashierPanel.gameObject.SetActive(_casinoOpen == "cashier");
            _casinoRoulettePanel.gameObject.SetActive(_casinoOpen == "roulette");
            _casinoArcadePanel.gameObject.SetActive(_casinoOpen == "arcade");

            if (_casinoOpen == "cards") PaintCasinoCards();
            else if (_casinoOpen == "cashier") PaintCasinoCashier();
            else if (_casinoOpen == "roulette") PaintCasinoRoulette();
            else if (_casinoOpen == "arcade") PaintCasinoArcade();
        }

        private void AddCasinoBetBar(RectTransform parent, float y)
        {
            var chips = _state.Casino?.Chips ?? 0;
            LoanText(parent, "Bet label", "Твои жетоны: 🪙 " + chips + "   |   Ставка:", new Vector2(-280, y), new Vector2(520, 40), 25, Color.white, true);
            var bets = new[] { 1, 3, 5, 10 };
            for (var i = 0; i < bets.Length; i++)
            {
                var b = bets[i];
                var active = _casinoBet == b;
                MakeCasinoButton(parent, b + " жет.", new Vector2(60 + i * 120, y), new Vector2(108, 44), active, () =>
                {
                    _casinoBet = b;
                    PaintCasino();
                });
            }
            MakeCasinoButton(parent, "КАССА", new Vector2(560, y), new Vector2(120, 44), false, () => OpenCasino("cashier"));
        }

        private void PaintCasinoCards()
        {
            ClearShopChildren(_casinoCardsContent);
            var c = _state.Casino ?? new HubCasinoState();

            // Левая половина — стол Покера (переход к полному столу покера)
            var leftCard = LoanArt(_casinoCardsContent, "Poker card", null, new Vector2(-395, 10), new Vector2(460, 540), new Color(.04f, .14f, .10f, .96f)).rectTransform;
            leftCard.GetComponent<Image>().sprite = UiTextures.RoundedRect(64, 64, 18, Color.white);
            leftCard.GetComponent<Image>().type = Image.Type.Sliced;
            var lRim = leftCard.gameObject.AddComponent<HubOutline>();
            lRim.effectColor = CasinoGold;
            lRim.effectDistance = new Vector2(2, -2);

            LoanText(leftCard, "Title", "♠ ТЕХАССКИЙ ПОКЕР ♣", new Vector2(0, 215), new Vector2(420, 40), 28, CasinoGold, true);
            LoanText(leftCard, "Sub", "Главный карточный стол казино.\nКлассический Холдем за большим столом с блайндами, рейзами и ва-банком.",
                new Vector2(0, 120), new Vector2(400, 100), 21, new Color(.92f, .9f, .84f), true);

            var emblem = LoanArt(leftCard, "Poker emblem", null, new Vector2(0, -10), new Vector2(150, 150), new Color(.12f, .38f, .24f));
            emblem.sprite = UiTextures.RoundedRect(64, 64, 24, Color.white);
            emblem.type = Image.Type.Sliced;
            LoanText(emblem.rectTransform, "Sym", "♠ ♥\n♣ ♦", Vector2.zero, new Vector2(140, 130), 42, CasinoGold, true);

            MakeCasinoButton(leftCard, "СЕСТЬ ЗА ПОКЕР", new Vector2(0, -195), new Vector2(340, 62), true, () =>
            {
                _casinoOpen = null;
                Navigate("game:poker");
            });

            // Правая половина — интерактивный стол «21 / ОЧКО (Блэкджек)»
            var rightCard = LoanArt(_casinoCardsContent, "Blackjack card", null, new Vector2(225, 10), new Vector2(730, 540), new Color(.04f, .14f, .10f, .96f)).rectTransform;
            rightCard.GetComponent<Image>().sprite = UiTextures.RoundedRect(64, 64, 18, Color.white);
            rightCard.GetComponent<Image>().type = Image.Type.Sliced;
            var rRim = rightCard.gameObject.AddComponent<HubOutline>();
            rRim.effectColor = CasinoGold;
            rRim.effectDistance = new Vector2(2, -2);

            LoanText(rightCard, "BJ Title", "🃏 21 · ОЧКО (БЛЭКДЖЕК С КРУПЬЕ)", new Vector2(0, 222), new Vector2(680, 38), 27, CasinoGold, true);

            // Карты крупье
            var dealerLabel = c.BjDealer.Count == 0 ? "Крупье (Десятка треф): ждёт ставки"
                : c.BjActive ? "Крупье: " + HubCasino.CardLabel(c.BjDealer[0]) + " + [?]"
                : "Крупье (" + HubCasino.HandScore(c.BjDealer) + " очк.): " + string.Join("  ", c.BjDealer.Select(HubCasino.CardLabel));
            LoanText(rightCard, "Dealer hand", dealerLabel, new Vector2(0, 162), new Vector2(680, 36), 24, Color.white, true);

            // Визуальные карты крупье и игрока
            DrawMiniHand(rightCard, c.BjDealer, c.BjActive, new Vector2(0, 96));

            var playerLabel = c.BjPlayer.Count == 0 ? "Твоя рука: нажми «РАЗДАТЬ КАРТЫ»"
                : "Твоя рука (" + HubCasino.HandScore(c.BjPlayer) + " очк.): " + string.Join("  ", c.BjPlayer.Select(HubCasino.CardLabel));
            LoanText(rightCard, "Player hand", playerLabel, new Vector2(0, 18), new Vector2(680, 36), 24, CasinoGold, true);
            DrawMiniHand(rightCard, c.BjPlayer, false, new Vector2(0, -48));

            var summary = string.IsNullOrEmpty(c.BjSummary)
                ? "Набери ближе к 21, чем Десятка треф, но не больше 21. Победа ×2, Блэкджек ×2.5!"
                : c.BjSummary;
            LoanText(rightCard, "Summary", summary, new Vector2(0, -118), new Vector2(680, 48), 21, new Color(.96f, .92f, .78f), true);

            if (!c.BjActive)
            {
                var bets = new[] { 1, 3, 5, 10 };
                for (var i = 0; i < bets.Length; i++)
                {
                    var b = bets[i];
                    MakeCasinoButton(rightCard, b + " жет.", new Vector2(-240 + i * 105, -185), new Vector2(96, 48), _casinoBet == b, () =>
                    {
                        _casinoBet = b;
                        PaintCasino();
                    });
                }
                MakeCasinoButton(rightCard, "РАЗДАТЬ (" + _casinoBet + " ЖЕТ.)", new Vector2(185, -185), new Vector2(280, 54), true,
                    () => _ = RunCasinoCommand("casino_blackjack", "start", _casinoBet));
            }
            else
            {
                MakeCasinoButton(rightCard, "ЕЩЁ КАРТУ", new Vector2(-210, -185), new Vector2(200, 54), true,
                    () => _ = RunCasinoCommand("casino_blackjack", "hit", 0));
                MakeCasinoButton(rightCard, "ХВАТИТ", new Vector2(10, -185), new Vector2(200, 54), false,
                    () => _ = RunCasinoCommand("casino_blackjack", "stand", 0));
                var canDouble = c.BjPlayer.Count == 2 && c.Chips >= c.BjBet;
                var dbl = MakeCasinoButton(rightCard, "УДВОИТЬ ×2", new Vector2(225, -185), new Vector2(200, 54), false,
                    () => _ = RunCasinoCommand("casino_blackjack", "double", 0));
                dbl.interactable = canDouble && !_casinoBusy;
            }
        }

        private void DrawMiniHand(RectTransform parent, List<int> cards, bool hideSecond, Vector2 center)
        {
            if (cards == null || cards.Count == 0) return;
            const float spacing = 84f;
            var startX = center.x - (cards.Count - 1) * spacing * .5f;
            for (var i = 0; i < cards.Count; i++)
            {
                var hidden = hideSecond && i == 1;
                var code = cards[i];
                var bg = hidden ? new Color(.18f, .12f, .08f) : new Color(.97f, .95f, .9f);
                var card = LoanArt(parent, "Card " + i, null, new Vector2(startX + i * spacing, center.y), new Vector2(72, 96), bg);
                card.sprite = UiTextures.RoundedRect(32, 32, 10, Color.white);
                card.type = Image.Type.Sliced;
                var rim = card.gameObject.AddComponent<HubOutline>();
                rim.effectColor = CasinoGold;
                rim.effectDistance = new Vector2(2, -2);
                var textColor = hidden ? CasinoGold : HubCasino.IsRedCard(code) ? new Color(.78f, .14f, .14f) : new Color(.1f, .1f, .12f);
                LoanText(card.rectTransform, "Lbl", hidden ? "♣" : HubCasino.CardLabel(code), Vector2.zero, new Vector2(68, 86), 24, textColor, true);
            }
        }

        private void PaintCasinoCashier()
        {
            ClearShopChildren(_casinoCashierContent);
            var now = PlayerHubRuntime.Clock.UnixSeconds;
            var chips = _state.Casino?.Chips ?? 0;
            var spendable = HubShop.Spendable(_state);

            // Верхняя плашка: баланс и ежедневный комплимент от Дамы треф
            var topBar = LoanArt(_casinoCashierContent, "Cashier top", null, new Vector2(0, 215), new Vector2(1240, 120), new Color(.04f, .14f, .10f, .96f)).rectTransform;
            topBar.GetComponent<Image>().sprite = UiTextures.RoundedRect(64, 64, 16, Color.white);
            topBar.GetComponent<Image>().type = Image.Type.Sliced;

            LoanText(topBar, "Info",
                "Баланс жетонов: 🪙 " + chips + " жет.  |  Свободно коинов: " + spendable + " к.\n"
                + "Курс кассы: покупка 1 жетон = " + HubCasino.ChipBuyCoins + " коинов · вывод 1 жетон = " + HubCasino.ChipCashoutCoins + " коинов",
                new Vector2(-210, 0), new Vector2(760, 96), 22, Color.white);

            var canDaily = HubCasino.CanClaimDaily(_state, now);
            var dailyBtn = MakeCasinoButton(topBar,
                canDaily ? "🎁 БЕСПЛАТНО +" + HubCasino.DailyFreeChips + " ЖЕТ." : "ПОЛУЧЕНО СЕГОДНЯ",
                new Vector2(390, 0), new Vector2(380, 64), canDaily,
                () => _ = RunCasinoCommand("casino_daily", "", 0));
            dailyBtn.interactable = canDaily && !_casinoBusy;

            // Средняя плашка: покупка и продажа жетонов
            var exchBar = LoanArt(_casinoCashierContent, "Exchange bar", null, new Vector2(0, 72), new Vector2(1240, 130), new Color(.04f, .14f, .10f, .96f)).rectTransform;
            exchBar.GetComponent<Image>().sprite = UiTextures.RoundedRect(64, 64, 16, Color.white);
            exchBar.GetComponent<Image>().type = Image.Type.Sliced;

            LoanText(exchBar, "Buy title", "КУПИТЬ ЖЕТОНЫ:", new Vector2(-480, 28), new Vector2(240, 34), 22, CasinoGold, true);
            MakeCasinoButton(exchBar, "+1 ЖЕТ. (10 к.)", new Vector2(-210, 28), new Vector2(210, 48), true,
                () => _ = RunCasinoCommand("casino_buy_chips", "", 1));
            MakeCasinoButton(exchBar, "+5 ЖЕТ. (50 к.)", new Vector2(20, 28), new Vector2(210, 48), true,
                () => _ = RunCasinoCommand("casino_buy_chips", "", 5));
            MakeCasinoButton(exchBar, "+25 ЖЕТ. (250 к.)", new Vector2(255, 28), new Vector2(230, 48), true,
                () => _ = RunCasinoCommand("casino_buy_chips", "", 25));

            LoanText(exchBar, "Sell title", "ОБМЕНЯТЬ В КОИНЫ:", new Vector2(-480, -30), new Vector2(240, 34), 22, new Color(.85f, .88f, .82f), true);
            MakeCasinoButton(exchBar, "−1 ЖЕТ. (+9 к.)", new Vector2(-210, -30), new Vector2(210, 46), false,
                () => _ = RunCasinoCommand("casino_cashout_chips", "", 1));
            MakeCasinoButton(exchBar, "−5 ЖЕТ. (+45 к.)", new Vector2(20, -30), new Vector2(210, 46), false,
                () => _ = RunCasinoCommand("casino_cashout_chips", "", 5));
            if (chips > 0)
            {
                MakeCasinoButton(exchBar, "ВСЁ (" + chips + " → +" + (chips * HubCasino.ChipCashoutCoins) + " к.)",
                    new Vector2(285, -30), new Vector2(290, 46), false,
                    () => _ = RunCasinoCommand("casino_cashout_chips", "", chips));
            }

            // Нижняя витрина: Призы Хайроллера за жетоны
            LoanText(_casinoCashierContent, "Prizes header", "💎 ВИТРИНА ХАЙРОЛЛЕРА ДАМЫ ТРЕФ (ВЫКУП ЗА ЖЕТОНЫ КАЗИНО)",
                new Vector2(0, -24), new Vector2(1180, 36), 24, CasinoGold, true);

            var prizes = HubCasino.HighRollerPrizes(_state).Take(3).ToList();
            if (prizes.Count == 0)
            {
                LoanText(_casinoCashierContent, "All owned", "Ты уже скупил все редкие сокровища витрины Дамы треф!",
                    new Vector2(0, -150), new Vector2(960, 50), 26, Color.white, true);
                return;
            }
            for (var i = 0; i < prizes.Count; i++)
            {
                var item = prizes[i];
                var cost = HubCasino.PrizeChipCost(item);
                var x = (i - (prizes.Count - 1) * .5f) * 410f;
                var card = LoanArt(_casinoCashierContent, "Prize " + item.Id, null, new Vector2(x, -165), new Vector2(390, 210), new Color(.04f, .14f, .10f, .96f)).rectTransform;
                card.GetComponent<Image>().sprite = UiTextures.RoundedRect(64, 64, 16, Color.white);
                card.GetComponent<Image>().type = Image.Type.Sliced;
                var rim = card.gameObject.AddComponent<HubOutline>();
                rim.effectColor = CasinoGold;
                rim.effectDistance = new Vector2(2, -2);

                LoanText(card, "Name", item.Name, new Vector2(0, 62), new Vector2(360, 36), 23, CasinoGold, true);
                LoanText(card, "Desc", item.Description, new Vector2(0, 10), new Vector2(350, 54), 18, new Color(.9f, .9f, .86f), true);
                var btn = MakeCasinoButton(card, "ЗАБРАТЬ ЗА " + cost + " ЖЕТ.", new Vector2(0, -62), new Vector2(320, 50), chips >= cost,
                    () => _ = RunCasinoCommand("casino_redeem", item.Id, 0));
                btn.interactable = chips >= cost && !_casinoBusy;
            }
        }

        private void PaintCasinoRoulette()
        {
            ClearShopChildren(_casinoRouletteContent);
            var c = _state.Casino ?? new HubCasinoState();

            MakeCasinoButton(_casinoRouletteContent, "🎡 КАРТОЧНАЯ РУЛЕТКА", new Vector2(-220, 252), new Vector2(380, 50),
                _casinoRouletteTab == "roulette", () => { _casinoRouletteTab = "roulette"; PaintCasino(); });
            MakeCasinoButton(_casinoRouletteContent, "🎲 КОСТИ КРУПЬЕ", new Vector2(220, 252), new Vector2(380, 50),
                _casinoRouletteTab == "dice", () => { _casinoRouletteTab = "dice"; PaintCasino(); });

            AddCasinoBetBar(_casinoRouletteContent, 182);

            if (_casinoRouletteTab == "roulette")
            {
                var wheelBox = LoanArt(_casinoRouletteContent, "Wheel result", null, new Vector2(0, 62), new Vector2(1180, 130), new Color(.04f, .14f, .10f, .96f)).rectTransform;
                wheelBox.GetComponent<Image>().sprite = UiTextures.RoundedRect(64, 64, 16, Color.white);
                wheelBox.GetComponent<Image>().type = Image.Type.Sliced;

                var lastLbl = c.RouletteCard < 0 ? "Крупье Десятка треф готов запустить колесо!" : "Последний сектор: " + HubCasino.CardLabel(c.RouletteCard);
                LoanText(wheelBox, "Last", lastLbl, new Vector2(0, 26), new Vector2(1100, 42), 30, CasinoGold, true);
                var sumLbl = string.IsNullOrEmpty(c.RouletteSummary)
                    ? "Выбери сектор: Красное/Чёрное ×2 · Масть ×4 · Картинка (В, Д, К, Т) ×3 · Джокер ×14"
                    : c.RouletteSummary;
                LoanText(wheelBox, "Sum", sumLbl, new Vector2(0, -26), new Vector2(1120, 40), 22, Color.white, true);

                // Кнопки секторов рулетки
                var row1 = new (string id, string title)[]
                {
                    ("red", "♦ ♥ КРАСНОЕ (×2)"),
                    ("black", "♠ ♣ ЧЁРНОЕ (×2)"),
                    ("face", "👑 КАРТИНКА В/Д/К/Т (×3)"),
                    ("joker", "🃏 ЗЕРО-ДЖОКЕР (×14)"),
                };
                for (var i = 0; i < row1.Length; i++)
                {
                    var sector = row1[i];
                    var x = (i - 1.5f) * 305f;
                    MakeCasinoButton(_casinoRouletteContent, sector.title, new Vector2(x, -68), new Vector2(288, 66), true,
                        () => _ = RunCasinoCommand("casino_roulette", sector.id, _casinoBet));
                }

                var row2 = new (string id, string title)[]
                {
                    ("clubs", "♣ ТРЕФЫ (×4)"),
                    ("diamonds", "♦ БУБНЫ (×4)"),
                    ("hearts", "♥ ЧЕРВИ (×4)"),
                    ("spades", "♠ ПИКИ (×4)"),
                };
                for (var i = 0; i < row2.Length; i++)
                {
                    var sector = row2[i];
                    var x = (i - 1.5f) * 305f;
                    MakeCasinoButton(_casinoRouletteContent, sector.title, new Vector2(x, -162), new Vector2(288, 66), false,
                        () => _ = RunCasinoCommand("casino_roulette", sector.id, _casinoBet));
                }
            }
            else
            {
                var diceBox = LoanArt(_casinoRouletteContent, "Dice result", null, new Vector2(0, 40), new Vector2(1180, 170), new Color(.04f, .14f, .10f, .96f)).rectTransform;
                diceBox.GetComponent<Image>().sprite = UiTextures.RoundedRect(64, 64, 16, Color.white);
                diceBox.GetComponent<Image>().type = Image.Type.Sliced;

                var diceLine = c.DiceA > 0 ? "[ " + c.DiceA + " ]  +  [ " + c.DiceB + " ]  =  " + (c.DiceA + c.DiceB) : "[ ? ]  +  [ ? ]";
                LoanText(diceBox, "Dice", diceLine, new Vector2(0, 32), new Vector2(1000, 52), 38, CasinoGold, true);
                var sum = string.IsNullOrEmpty(c.DiceSummary)
                    ? "Две золотые кости Тузяры: угадай сумму броска!"
                    : c.DiceSummary;
                LoanText(diceBox, "Sum", sum, new Vector2(0, -34), new Vector2(1100, 42), 23, Color.white, true);

                MakeCasinoButton(_casinoRouletteContent, "МЕНЬШЕ 7 (2..6) · ×2", new Vector2(-390, -130), new Vector2(360, 74), true,
                    () => _ = RunCasinoCommand("casino_dice", "under7", _casinoBet));
                MakeCasinoButton(_casinoRouletteContent, "РОВНО 7 · ×5", new Vector2(0, -130), new Vector2(360, 74), true,
                    () => _ = RunCasinoCommand("casino_dice", "seven", _casinoBet));
                MakeCasinoButton(_casinoRouletteContent, "БОЛЬШЕ 7 (8..12) · ×2", new Vector2(390, -130), new Vector2(360, 74), true,
                    () => _ = RunCasinoCommand("casino_dice", "over7", _casinoBet));
            }
        }

        private void PaintCasinoArcade()
        {
            ClearShopChildren(_casinoArcadeContent);
            var c = _state.Casino ?? new HubCasinoState();

            MakeCasinoButton(_casinoArcadeContent, "🎰 СЛОТЫ «777» (3 В РЯД)", new Vector2(-230, 252), new Vector2(410, 50),
                _casinoArcadeTab == "slots", () => { _casinoArcadeTab = "slots"; PaintCasino(); });
            MakeCasinoButton(_casinoArcadeContent, "🚀 КРЭШ «ТУЗ-РАКЕТА»", new Vector2(230, 252), new Vector2(410, 50),
                _casinoArcadeTab == "rocket", () => { _casinoArcadeTab = "rocket"; PaintCasino(); });

            AddCasinoBetBar(_casinoArcadeContent, 182);

            if (_casinoArcadeTab == "slots")
            {
                var reels = c.SlotReels != null && c.SlotReels.Count == 3 ? c.SlotReels : new List<int> { 6, 6, 6 };
                for (var i = 0; i < 3; i++)
                {
                    var symIndex = Mathf.Clamp(reels[i], 0, HubCasino.SlotSymbols.Length - 1);
                    var reel = LoanArt(_casinoArcadeContent, "Reel " + i, null, new Vector2((i - 1) * 250f, 35), new Vector2(210, 175), new Color(.97f, .94f, .86f)).rectTransform;
                    reel.GetComponent<Image>().sprite = UiTextures.RoundedRect(64, 64, 22, Color.white);
                    reel.GetComponent<Image>().type = Image.Type.Sliced;
                    var rim = reel.gameObject.AddComponent<HubOutline>();
                    rim.effectColor = CasinoGold;
                    rim.effectDistance = new Vector2(4, -4);

                    LoanText(reel, "Sym", HubCasino.SlotSymbols[symIndex], new Vector2(0, 14), new Vector2(190, 96), 54, CasinoCrimson, true);
                    LoanText(reel, "Name", HubCasino.SlotNames[symIndex], new Vector2(0, -56), new Vector2(190, 34), 20, new Color(.22f, .16f, .1f), true);
                }

                var sum = string.IsNullOrEmpty(c.SlotSummary)
                    ? "Три 777 — ×25! Три короны/алмаза — ×12! Три масти — ×6! Пара — ×2!"
                    : c.SlotSummary;
                LoanText(_casinoArcadeContent, "Slot summary", sum, new Vector2(0, -92), new Vector2(1160, 44), 24, CasinoGold, true);

                MakeCasinoButton(_casinoArcadeContent, "🎰 ДЁРНУТЬ РЫЧАГ (" + _casinoBet + " ЖЕТ.)", new Vector2(0, -175), new Vector2(460, 68), true,
                    () => _ = RunCasinoCommand("casino_slots", "", _casinoBet));
            }
            else
            {
                var sky = LoanArt(_casinoArcadeContent, "Rocket sky", null, new Vector2(0, 32), new Vector2(1140, 190), new Color(.03f, .06f, .12f, .98f)).rectTransform;
                sky.GetComponent<Image>().sprite = UiTextures.RoundedRect(64, 64, 20, Color.white);
                sky.GetComponent<Image>().type = Image.Type.Sliced;
                var rim = sky.gameObject.AddComponent<HubOutline>();
                rim.effectColor = CasinoGold;
                rim.effectDistance = new Vector2(3, -3);

                var statusTitle = c.RocketCrashed
                    ? "💥 БУМ! РАКЕТА ВЗОРВАЛАСЬ!"
                    : c.RocketActive
                        ? "🚀 В ПОЛЁТЕ · " + HubCasino.FormatMultiplier(c.RocketMultPermille)
                        : "🚀 ЗОЛОТАЯ РАКЕТА ТУЗЯРЫ · " + HubCasino.FormatMultiplier(c.RocketMultPermille);
                var titleColor = c.RocketCrashed ? new Color(1f, .38f, .32f) : CasinoGold;
                LoanText(sky, "Multiplier", statusTitle, new Vector2(0, 34), new Vector2(1060, 64), 42, titleColor, true);

                var sum = string.IsNullOrEmpty(c.RocketSummary)
                    ? "Запусти ракету: с каждым рывком вверх иксы растут (до 15.00x), но ракета может рвануть в любой миг!"
                    : c.RocketSummary;
                LoanText(sky, "Rocket summary", sum, new Vector2(0, -38), new Vector2(1080, 48), 22, Color.white, true);

                if (!c.RocketActive)
                {
                    MakeCasinoButton(_casinoArcadeContent, "🚀 ЗАПУСТИТЬ РАКЕТУ (" + _casinoBet + " ЖЕТ.)", new Vector2(0, -165), new Vector2(480, 68), true,
                        () => _ = RunCasinoCommand("casino_rocket", "launch", _casinoBet));
                }
                else
                {
                    MakeCasinoButton(_casinoArcadeContent, "▲ ЛЕТЕТЬ ЕЩЁ ВЫШЕ!", new Vector2(-230, -165), new Vector2(410, 68), true,
                        () => _ = RunCasinoCommand("casino_rocket", "step", 0));
                    var potential = Math.Max(c.RocketBet + 1, c.RocketBet * c.RocketMultPermille / 1000);
                    var cashBtn = MakeCasinoButton(_casinoArcadeContent,
                        c.RocketStep > 0 ? "💰 ЗАБРАТЬ КУШ (+" + potential + " ЖЕТ.)" : "СДЕЛАЙ 1-Й ШАГ ВВЕРХ",
                        new Vector2(230, -165), new Vector2(410, 68), c.RocketStep > 0,
                        () => _ = RunCasinoCommand("casino_rocket", "cashout", 0));
                    cashBtn.interactable = c.RocketStep > 0 && !_casinoBusy;
                }
            }
        }

        private Button MakeCasinoButton(RectTransform parent, string label, Vector2 anchored, Vector2 size, bool gold, Action onClick)
        {
            var bg = gold ? new Color(.78f, .56f, .16f, 1f) : CasinoCrimson;
            var img = LoanArt(parent, "Btn " + label, null, anchored, size, bg);
            img.sprite = UiTextures.RoundedRect(64, 64, 16, Color.white);
            img.type = Image.Type.Sliced;
            img.raycastTarget = true;
            var rim = img.gameObject.AddComponent<HubOutline>();
            rim.effectColor = CasinoGold;
            rim.effectDistance = new Vector2(2, -2);
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => { if (!_casinoBusy) onClick?.Invoke(); });
            LoanText(img.rectTransform, "Text", label, Vector2.zero, size - new Vector2(12, 6), 21, Color.white, true);
            return btn;
        }

        private async Task RunCasinoCommand(string action, string target, int amount)
        {
            if (_casinoBusy || _busy || _state == null) return;
            _casinoBusy = true;
            SetBusy(true);
            try
            {
                var result = await PlayerHubRuntime.ExecuteAsync(new HubCommand
                {
                    Action = action,
                    Target = target,
                    Amount = amount,
                });
                if (this == null) return;
                if (result.Success) { _state = result.Snapshot; Render(); }
                else Toast(PlayerHubRuntime.ErrorText(result));
                _casinoLine = result.Success ? result.Message : PlayerHubRuntime.ErrorText(result);
                PaintCasino();
            }
            finally
            {
                if (this != null)
                {
                    SetBusy(false);
                    _casinoBusy = false;
                    PaintCasino();
                }
            }
        }
    }
}
