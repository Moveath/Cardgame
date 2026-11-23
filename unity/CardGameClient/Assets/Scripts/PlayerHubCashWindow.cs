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
    /// Касса банка Тузяры (2026-11-03, HubCash.cs): «+» у серебра и золота на панели города, «Пополнить» в шапке разделов
    /// и открытый сейф за спиной Тузяры в банке открывают поверх текущего экрана доску как у настроек. Вкладки «СЕРЕБРО»
    /// и «ЗОЛОТО», «ОБМЕН» (только серебро в золото, HubCash.cs), справа сверху оба баланса. Четыре карточки: у серебра первая —
    /// ролик за награду (+50, пять в день), дальше наборы; у золота — четыре набора. На карточке лента выгоды, горка коинов
    /// (стопка, кучка, гора, сундук — Hud/cash-silver-*, cash-gold-*), число и кнопка. Обмен — вместо карточек
    /// (<see cref="BuildCashTrade"/>): «ОТДАЁШЬ → ПОЛУЧАЕШЬ», шкала «СКОЛЬКО ВЗЯТЬ» и дневной лимит монетками.
    /// Внизу — «КРЕДИТ ТУЗЯРЫ ›» (проезд к его столу), «ЗАКРЫТЬ» и «БЕЗ РЕКЛАМЫ». Магазины приложений и рекламная сеть
    /// ещё не подключены: покупка начисляет коины без списания денег, ролик — заставка с отсчётом.
    /// </summary>
    public sealed partial class PlayerHubController
    {
        private sealed class CashCard
        {
            public RectTransform Root, Art;
            public Image Ribbon;
            public Text RibbonText, Amount, Kind, Price;
            public Button Buy;
            // Набор из HubCash.Packs; пусто — ролик за награду.
            public string PackId = "";
            public bool IsAd => PackId.Length == 0;
        }

        // Касса шире и выше остальных окон (автор, 2026-11-03): карточки и горки коинов крупнее. Поля по краям — те же 35.
        private const float CashWindowW = 1460, CashWindowH = 792, CashContentW = CashWindowW - 70;
        private const float CashCardW = 328, CashCardH = 440, CashAdSeconds = 5;
        // Обмен: окошки «ОТДАЁШЬ» и «ПОЛУЧАЕШЬ» по краям, строки шкалы и лимита во всю ширину.
        private const float CashTradeBoxW = 470, CashTradeBoxH = 280, CashTradeRowH = 84, CashPickTrackW = 640;
        private static readonly Color CashRed = new Color(.78f, .16f, .1f), CashGreen = new Color(.24f, .6f, .2f);
        private static readonly Color CashSteel = new Color(.32f, .4f, .52f), CashGoldRibbon = new Color(.74f, .52f, .1f);
        private static readonly Color CashArrowDim = new Color(.4f, .28f, .12f);
        private RectTransform _cashLayer, _cashWindow, _cashAd;
        private CanvasGroup _cashGroup;
        private Text _cashSilver, _cashGold, _cashAdCount;
        private Button _cashNoAds;
        private readonly List<(CanvasGroup Group, Image Glow)> _cashTabs = new List<(CanvasGroup, Image)>();
        private readonly List<CashCard> _cashCards = new List<CashCard>();
        private int _cashTab, _cashTabShown = -1;
        private bool _cashShown, _cashEscapeHeld;
        // Экран, поверх которого открыто окно: ушли с него — окно закрывается.
        private string _cashPage;
        // Балансы на экране догоняют настоящие; −1 у ролика — заставки нет.
        private float _cashFade, _cashSilverShown, _cashGoldShown, _cashAdTime = -1;
        private bool CashShown => _cashShown || _cashFade > 0;
        // Обмен: сколько золота взять (−1 — при открытии 5), какие горки и деления шкалы нарисованы.
        private RectTransform _cashTrade, _cashGivePile, _cashGetPile, _cashPickFill, _cashPickKnob, _cashPickTicks;
        private Text _cashGive, _cashGiveNote, _cashGet, _cashGetNote, _cashTradeNote, _cashLimitLeft, _cashLimitNote;
        private Button _cashTradeButton, _cashPickMinus, _cashPickPlus, _cashPickMax;
        private CanvasGroup[] _cashTradeBoxes;
        private readonly List<Image> _cashArrows = new List<Image>(), _cashLimitPips = new List<Image>();
        private int _cashPick = -1, _cashPileLevel = -1, _cashTicksFor = -1;
        private float _cashTime;
        private bool _cashArrowArt;

        /// <summary>Касса на вкладке <paramref name="tab"/>: 0 — серебро, 1 — золото, 2 — обмен.</summary>
        private void ShowCashWindow(int tab)
        {
            if (_state == null || _busy) return;
            if (_cashLayer == null) BuildCashWindow();
            _cashTab = Mathf.Clamp(tab, 0, 2);
            _cashShown = true;
            _cashPage = _page;
            _cashLayer.gameObject.SetActive(true);
            _cashLayer.SetAsLastSibling();
            _cashSilverShown = _state.Profile.Coins;
            _cashGoldShown = _state.Profile.Gold;
            _cashPick = -1;
            PaintCashWindow(0);
            RefreshMapButton();
        }

        private void CloseCashWindow() => _cashShown = false;

        private void BuildCashWindow()
        {
            _cashLayer = CityWindow("Cash window", "КАССА ТУЗЯРЫ", out _cashGroup, out _cashWindow, new Vector2(CashWindowW, CashWindowH));

            // Вкладки — слева сверху, по линии строк; оба баланса — справа сверху.
            const float tabW = 230, tabStep = 242;
            var tabs = new[] { "СЕРЕБРО", "ЗОЛОТО", "ОБМЕН" };
            for (var i = 0; i < tabs.Length; i++)
            {
                var title = tabs[i];
                var index = i;
                var at = new Vector2(-CashContentW / 2 + tabW / 2 + i * tabStep, 306);
                var glow = CollectionGlow(_cashWindow, at, new Vector2(tabW, 76));
                var plate = QuestArt(_cashWindow, "Tab " + title, null, at, new Vector2(tabW, 76), new Color(.2f, .12f, .07f));
                HdPlaque(plate);
                plate.raycastTarget = true;
                var button = plate.gameObject.AddComponent<Button>();
                button.targetGraphic = plate;
                button.onClick.AddListener(() => _cashTab = index);
                plate.gameObject.AddComponent<HubPress>();
                // У обмена — золотой и серебряный внахлёст.
                foreach (var (coin, shift, tilt) in i == 0 ? new[] { (HubCurrencyArt.Silver, 0f, 0f) } : i == 1 ? new[] { (HubCurrencyArt.Gold, 0f, 0f) }
                    : new[] { (HubCurrencyArt.Gold, -9f, 12f), (HubCurrencyArt.Silver, 9f, -12f) })
                {
                    var icon = QuestArt(plate.transform, "Icon", null, new Vector2(-tabW / 2 + 46 + shift, 0), new Vector2(44, 58), Color.white);
                    icon.sprite = coin;
                    icon.preserveAspect = true;
                    icon.rectTransform.localRotation = Quaternion.Euler(0, 0, tilt);
                }
                var label = SettingsSign(plate.transform, title, new Vector2(32, 0), new Vector2(tabW - 96, 40), 30);
                if (label.preferredWidth > tabW - 96) label.fontSize = Mathf.FloorToInt(30 * (tabW - 96) / label.preferredWidth);
                _cashTabs.Add((plate.gameObject.AddComponent<CanvasGroup>(), glow));
            }
            const float balanceW = 240;
            _cashGold = CashBalance(HubCurrencyArt.Gold, new Vector2(CashContentW / 2 - balanceW / 2, 306), balanceW);
            _cashSilver = CashBalance(HubCurrencyArt.Silver, new Vector2(CashContentW / 2 - balanceW * 1.5f - 36, 306), balanceW);

            // Карточки — посередине между вкладками и нижним рядом: ленты выгоды не задевают вкладки (строки под ними нет).
            for (var i = 0; i < 4; i++)
                _cashCards.Add(BuildCashCard(new Vector2(-CashContentW / 2 + CashCardW / 2 + i * (CashCardW + 26), -16)));
            BuildCashTrade();

            // Внизу: слева — к Тузяре за кредитом, посередине «ЗАКРЫТЬ», справа — «Без рекламы».
            const float sideW = 400;
            var bottom = CashWindowH - 40 - 64;
            var loan = CityButton(_cashWindow, "КРЕДИТ ТУЗЯРЫ ›", (CashWindowW - CashContentW) / 2 + TaskInset, bottom, sideW, 64,
                () => { CloseCashWindow(); GoToBankDesk(); });
            HdPlaque(loan.GetComponent<Image>());
            loan.GetComponentInChildren<Text>().fontSize = 28;
            CashButtonIcon(loan, "cash-loan");
            WindowCloseButton(_cashWindow, bottom, CloseCashWindow);
            _cashNoAds = CityButton(_cashWindow, "БЕЗ РЕКЛАМЫ", (CashWindowW + CashContentW) / 2 - TaskInset - sideW, bottom, sideW, 64,
                () => _ = Dispatch("cash_buy", HubCash.NoAdsId));
            HdPlaque(_cashNoAds.GetComponent<Image>());
            // В шрифте вывесок нет «₽» — цена шрифтом игры.
            var noAds = _cashNoAds.GetComponentInChildren<Text>();
            noAds.font = _font;
            noAds.fontStyle = FontStyle.Bold;
            noAds.fontSize = 26;
            CashButtonIcon(_cashNoAds, "cash-noads");

            BuildCashAd();
        }

        /// <summary>Значок слева на нижней кнопке, как монета у «В ЛАВКУ» в коллекции: надпись — посередине остального места.</summary>
        private static void CashButtonIcon(Button button, string art)
        {
            var sprite = HudArt(art);
            if (sprite == null) return;
            CenteredImage(button.transform, "Icon", 14, 4, 56, 56).sprite = sprite;
            var caption = button.GetComponentInChildren<Text>().rectTransform;
            caption.sizeDelta = new Vector2(caption.sizeDelta.x - 70, caption.sizeDelta.y);
            caption.anchoredPosition += new Vector2(64, 0);
        }

        /// <summary>Баланс сверху справа: плашка монет, коин на левом конце, число посередине остального.</summary>
        private Text CashBalance(Sprite coin, Vector2 at, float width)
        {
            var plate = QuestArt(_cashWindow, "Balance", null, at, new Vector2(width, 60), Color.white);
            plate.sprite = HudArt("currency-plate");
            plate.type = Image.Type.Sliced;
            plate.pixelsPerUnitMultiplier = 109f / 60;
            var icon = QuestArt(plate.transform, "Coin", null, new Vector2(-width / 2, 0), new Vector2(48, 64), Color.white);
            icon.sprite = coin;
            icon.preserveAspect = true;
            var amount = QuestText(plate.transform, "Amount", "", new Vector2(7, 0), new Vector2(width - 80, 46), 30, Color.white);
            amount.font = _font;
            amount.fontStyle = FontStyle.Bold;
            return amount;
        }

        /// <summary>Карточка кассы: лента выгоды над верхним краем, горка коинов, число, пояснение и кнопка с ценой.</summary>
        private CashCard BuildCashCard(Vector2 at)
        {
            var card = new CashCard();
            var plate = CashPlate(_cashWindow, "Cash card", at, new Vector2(CashCardW, CashCardH));
            card.Root = plate.rectTransform;
            card.Art = QuestRect(plate.transform, "Art", new Vector2(0, 86), new Vector2(270, 200));
            card.Amount = SettingsSign(plate.transform, "", new Vector2(0, -54), new Vector2(290, 64), 56);
            card.Kind = NewsFit(QuestText(plate.transform, "Kind", "", new Vector2(0, -100), new Vector2(286, 30), 21, SettingsCream));
            card.Kind.font = _font;
            const float buyW = 240, buyH = 68;
            card.Buy = CityButton(plate.transform, "", (CashCardW - buyW) / 2, CashCardH - 32 - buyH, buyW, buyH, () => PressCashCard(card));
            HdPlaque(card.Buy.GetComponent<Image>());
            card.Price = card.Buy.GetComponentInChildren<Text>();
            card.Ribbon = CashRibbon(plate.transform, CashCardH, 180, CashRed, out card.RibbonText);
            return card;
        }

        /// <summary>Тёмное поле в золотой рамке (Hud/plaque-hd) — карточки и окошки обмена.</summary>
        private Image CashPlate(Transform parent, string name, Vector2 at, Vector2 size)
        {
            var plate = QuestArt(parent, name, null, at, size, new Color(.2f, .12f, .07f));
            var plaque = HudArt("plaque-hd");
            if (plaque == null) return plate;
            plate.sprite = plaque;
            plate.type = Image.Type.Sliced;
            plate.pixelsPerUnitMultiplier = plaque.border.y / 34f;
            return plate;
        }

        /// <summary>Лента на верхней рамке, выступает над ней на треть: не дотягивается до вкладок.</summary>
        private Image CashRibbon(Transform plate, float plateHeight, float width, Color color, out Text text)
        {
            var ribbon = QuestArt(plate, "Ribbon", null, new Vector2(0, plateHeight / 2 - 8), new Vector2(width, 44), color);
            ribbon.sprite = UiTextures.RoundedRect(96, 48, 18, Color.white);
            ribbon.type = Image.Type.Sliced;
            var rim = ribbon.gameObject.AddComponent<Outline>();
            rim.effectColor = new Color(1f, .8f, .35f);
            rim.effectDistance = new Vector2(2, -2);
            text = QuestText(ribbon.transform, "Bonus", "", Vector2.zero, new Vector2(width - 4, 40), 24, Color.white);
            text.font = _font;
            text.fontStyle = FontStyle.Bold;
            return ribbon;
        }

        /// <summary>
        /// Обмен вместо карточек: слева «ОТДАЁШЬ» (горка серебра и сколько), справа «ПОЛУЧАЕШЬ» (горка золота), между ними
        /// курс, бегущая стрелка и «ОБМЕНЯТЬ». Ниже — шкала «СКОЛЬКО ВЗЯТЬ» (ползунок с делениями, − и +, «МАКС») и «ЛИМИТ
        /// НА ДЕНЬ»: монетка на каждый золотой дня — взятые сегодня тусклые, выбранные сейчас крупнее и мерцают.
        /// </summary>
        private void BuildCashTrade()
        {
            _cashTrade = QuestRect(_cashWindow, "Cash trade", Vector2.zero, new Vector2(CashContentW, CashWindowH));
            (_cashGivePile, _cashGive, _cashGiveNote) = CashTradeBox("ОТДАЁШЬ", CashSteel, -1, "серебряных\nтун-тун коинов");
            (_cashGetPile, _cashGet, _cashGetNote) = CashTradeBox("ПОЛУЧАЕШЬ", CashGoldRibbon, 1, "золотых\nтун-тун коинов");
            _cashTradeBoxes = new[] { _cashGivePile.parent.gameObject.AddComponent<CanvasGroup>(), _cashGetPile.parent.gameObject.AddComponent<CanvasGroup>() };

            // Курс — плашка как у балансов: серебряный коин, «25 = 1», золотой.
            var rate = QuestArt(_cashTrade, "Rate", null, new Vector2(0, 200), new Vector2(300, 58), Color.white);
            rate.sprite = HudArt("currency-plate");
            rate.type = Image.Type.Sliced;
            rate.pixelsPerUnitMultiplier = 109f / 58;
            foreach (var (coin, side) in new[] { (HubCurrencyArt.Silver, -1f), (HubCurrencyArt.Gold, 1f) })
            {
                var icon = QuestArt(rate.transform, "Coin", null, new Vector2(side * 150, 0), new Vector2(44, 58), Color.white);
                icon.sprite = coin;
                icon.preserveAspect = true;
            }
            SettingsSign(rate.transform, HubCash.SilverPerGold + "  =  1", Vector2.zero, new Vector2(200, 46), 36);

            // Стрелка «туда»: картинка Hud/cash-arrow, а пока её нет — три шеврона из скруглённых палочек; огонёк бежит по ним.
            var arrow = HudArt("cash-arrow");
            if (arrow != null)
            {
                var image = QuestArt(_cashTrade, "Arrow", null, new Vector2(0, 118), new Vector2(200, 100), Color.white);
                image.sprite = arrow;
                image.preserveAspect = true;
                _cashArrows.Add(image);
                _cashArrowArt = true;
            }
            else
            {
                var bar = UiTextures.RoundedRect(72, 24, 11, Color.white);
                for (var i = 0; i < 3; i++)
                {
                    var chevron = QuestRect(_cashTrade, "Chevron", new Vector2(-60 + i * 60, 118), new Vector2(60, 80));
                    // Палочки сходятся правыми концами в острие «›».
                    foreach (var side in new[] { 1, -1 })
                    {
                        var stick = QuestArt(chevron, "Stick", null, new Vector2(0, side * 17), new Vector2(70, 22), CashArrowDim);
                        stick.sprite = bar;
                        stick.type = Image.Type.Sliced;
                        stick.rectTransform.localRotation = Quaternion.Euler(0, 0, -45 * side);
                        stick.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(1.5f, -2.5f);
                        _cashArrows.Add(stick);
                    }
                }
            }

            _cashTradeButton = CityButton(_cashTrade, "ОБМЕНЯТЬ", (CashContentW - 300) / 2, CashWindowH / 2 - 18 - 40, 300, 80, PressCashTrade);
            HdPlaque(_cashTradeButton.GetComponent<Image>());
            _cashTradeButton.GetComponentInChildren<Text>().fontSize = 42;
            _cashTradeNote = NewsFit(QuestText(_cashTrade, "Trade note", "", new Vector2(0, -40), new Vector2(430, 28), 19, SettingsCream));
            _cashTradeNote.font = _font;

            // Шкала: жёлоб громкости из настроек, деления — на каждый золотой; − и + по краям, «МАКС» справа.
            var pick = CashTradeRow("СКОЛЬКО ВЗЯТЬ", HubCurrencyArt.Gold, -104, out var how);
            how.text = "веди по шкале или жми − и +";
            var track = QuestArt(pick, "Pick track", null, new Vector2(120, 4), new Vector2(CashPickTrackW, 34), Color.white);
            track.sprite = HudArt("xp-bar");
            track.type = Image.Type.Sliced;
            track.pixelsPerUnitMultiplier = 58f / 34;
            _cashPickTicks = QuestRect(track.transform, "Ticks", Vector2.zero, new Vector2(CashPickTrackW - 52, 34));
            var hit = QuestArt(pick, "Pick slider", null, track.rectTransform.anchoredPosition, new Vector2(CashPickTrackW + 50, 72), new Color(0, 0, 0, 0));
            hit.raycastTarget = true;
            var slider = hit.gameObject.AddComponent<HubSlider>();
            slider.Track = track.rectTransform;
            slider.Inset = 26;
            slider.Moved = slider.Released = SlideCashPick;
            var fill = MakeRect(track.transform, "Pick fill", 26, 10, 0, 14).gameObject.AddComponent<Image>();
            Round(fill, CashPickTrackW - 52, 14, 7);
            fill.color = new Color(1f, .8f, .25f);
            fill.raycastTarget = false;
            _cashPickFill = fill.rectTransform;
            var knob = QuestArt(track.transform, "Pick knob", null, Vector2.zero, new Vector2(40, 52), Color.white);
            knob.sprite = HudArt("slider-knob") ?? HubCurrencyArt.Gold;
            knob.preserveAspect = true;
            knob.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(1, -3);
            _cashPickKnob = knob.rectTransform;
            // Кнопки — после полосы ползунка: она шире жёлоба и иначе перехватила бы край «−» и «+».
            _cashPickMinus = SettingsRoundSign(pick, "Меньше", "−", new Vector2(-240, 0), () => _cashPick = Math.Max(1, CashPickNow() - 1));
            _cashPickPlus = SettingsRoundSign(pick, "Больше", "+", new Vector2(480, 0), () => _cashPick = CashPickNow() + 1);
            _cashPickMax = CityButton(pick, "МАКС", CashContentW - 30 - 130, (CashTradeRowH - 58) / 2, 130, 58, () => _cashPick = CashGoldMax());
            HdPlaque(_cashPickMax.GetComponent<Image>());

            // Лимит: монетка на каждый золотой дня — по ширине от «−» до «+» шкалы над ней.
            var limit = CashTradeRow("ЛИМИТ НА ДЕНЬ", HudArt("task-weekly") ?? HubCurrencyArt.Gold, -200, out _cashLimitNote);
            for (var i = 0; i < HubCash.GoldPerDay; i++)
            {
                var pip = QuestArt(limit, "Pip", null, new Vector2(-240 + i * 720f / (HubCash.GoldPerDay - 1), 0), new Vector2(28, 37), Color.white);
                pip.sprite = HubCurrencyArt.Gold;
                pip.preserveAspect = true;
                _cashLimitPips.Add(pip);
            }
            _cashLimitLeft = SettingsSign(limit, "", new Vector2(CashContentW / 2 - 30 - 65, 0), new Vector2(150, 46), 32);
            // ТЕСТ (2026-11-03, по просьбе автора): только в редакторе и тестовых сборках; убрать вместе с «cash_debug_reset».
            if (Application.isEditor || Debug.isDebugBuild)
                QuestButton(_cashTrade, "Тест: сбросить лимит", new Vector2(0, -264), new Vector2(340, 32), () => { if (!_busy) Send("cash_debug_reset"); }, 19);
        }

        /// <summary>Окошко обмена у края: лента сверху, горка коинов слева, справа крупное число и «каких», снизу пояснение.</summary>
        private (RectTransform Pile, Text Amount, Text Note) CashTradeBox(string title, Color ribbon, int side, string kind)
        {
            var plate = CashPlate(_cashTrade, "Trade " + title, new Vector2(side * (CashContentW - CashTradeBoxW) / 2, 96),
                new Vector2(CashTradeBoxW, CashTradeBoxH));
            // Горка — выше пояснения внизу: большая (сундук) не наезжает на «у тебя …» / «станет …».
            var pile = QuestRect(plate.transform, "Pile", new Vector2(-108, 16), new Vector2(210, 140));
            var amount = SettingsSign(plate.transform, "", new Vector2(108, 32), new Vector2(220, 70), 66);
            var what = QuestText(plate.transform, "Kind", kind, new Vector2(108, -28), new Vector2(220, 50), 20, SettingsCream);
            what.font = _font;
            var note = NewsFit(QuestText(plate.transform, "Note", "", new Vector2(0, -84), new Vector2(CashTradeBoxW - 80, 36), 25, SettingsCream));
            note.font = _font;
            note.fontStyle = FontStyle.Bold;
            CashRibbon(plate.transform, CashTradeBoxH, 220, ribbon, out var label);
            label.text = title;
            return (pile, amount, note);
        }

        /// <summary>Строка обмена во всю ширину — как строка настроек: плашка монет, значок, золотое название и пояснение.</summary>
        private RectTransform CashTradeRow(string title, Sprite icon, float y, out Text detail)
        {
            var plate = QuestArt(_cashTrade, "Row " + title, null, new Vector2(0, y), new Vector2(CashContentW, CashTradeRowH), Color.white);
            plate.sprite = HudArt("currency-plate");
            plate.type = Image.Type.Sliced;
            plate.pixelsPerUnitMultiplier = 109f / CashTradeRowH;
            var image = QuestArt(plate.transform, "Icon", null, new Vector2(-CashContentW / 2 + 64, 0), new Vector2(58, 58), Color.white);
            image.sprite = icon;
            image.preserveAspect = true;
            const float left = 108, textWidth = 290;
            var name = SettingsSign(plate.transform, title, Vector2.zero, new Vector2(textWidth, 40), 30);
            if (name.preferredWidth > textWidth) name.fontSize = Mathf.FloorToInt(name.fontSize * textWidth / name.preferredWidth);
            var nameWidth = Mathf.Min(textWidth, name.preferredWidth + 4);
            name.rectTransform.sizeDelta = new Vector2(nameWidth, 40);
            name.rectTransform.anchoredPosition = new Vector2(-CashContentW / 2 + left + nameWidth / 2, 13);
            detail = QuestText(plate.transform, "Detail", "", new Vector2(-CashContentW / 2 + left + textWidth / 2, -20), new Vector2(textWidth, 24), 17, SettingsCream);
            detail.font = _font;
            detail.alignment = TextAnchor.MiddleLeft;
            detail.resizeTextForBestFit = true;
            detail.resizeTextMinSize = 12;
            detail.resizeTextMaxSize = 17;
            detail.verticalOverflow = VerticalWrapMode.Truncate;
            return plate.rectTransform;
        }

        /// <summary>Заставка ролика поверх всего окна: «РЕКЛАМА», что будет, и отсчёт до награды.</summary>
        private void BuildCashAd()
        {
            _cashAd = QuestRect(_cashWindow.parent, "Cash ad", Vector2.zero, Vector2.zero);
            // Непрозрачная: в линейном цвете даже 96 % чёрного оставляют окно заметным.
            var dim = QuestArt(_cashAd, "Ad screen", null, Vector2.zero, new Vector2(4800, 2400), new Color(.01f, .008f, .006f));
            dim.raycastTarget = true;
            SettingsSign(_cashAd, "РЕКЛАМА", new Vector2(0, 150), new Vector2(600, 70), 60);
            QuestText(_cashAd, "Ad about", "Здесь будет ролик рекламной сети.\nДосмотри — и Тузяра заплатит " + HubCash.AdReward + " серебра.",
                new Vector2(0, 50), new Vector2(900, 80), 26, SettingsCream).font = _font;
            _cashAdCount = SettingsSign(_cashAd, "", new Vector2(0, -90), new Vector2(200, 120), 110);
            _cashAd.gameObject.SetActive(false);
        }

        /// <summary>
        /// Горка коинов размера <paramref name="level"/> 0–3: картинка Hud/cash-silver-N или cash-gold-N (стопка, кучка, гора,
        /// сундук); пока её нет — 1, 3, 6 или 10 монет рядами, чуть вразнобой.
        /// </summary>
        private void CashPile(RectTransform parent, bool gold, int level)
        {
            ClearShopChildren(parent);
            var picture = HudArt((gold ? "cash-gold-" : "cash-silver-") + (level + 1));
            if (picture != null)
            {
                var image = QuestArt(parent, "Pile", null, Vector2.zero, parent.sizeDelta, Color.white);
                image.sprite = picture;
                image.preserveAspect = true;
                return;
            }
            var coin = gold ? HubCurrencyArt.Gold : HubCurrencyArt.Silver;
            var width = new[] { 92f, 72f, 60f, 50f }[level];
            var height = width * 76 / 58;
            var rows = level + 1;
            var bottom = -parent.sizeDelta.y / 2 + height / 2;
            for (var row = 0; row < rows; row++)
            {
                var count = rows - row;
                for (var i = 0; i < count; i++)
                {
                    var x = (i - (count - 1) / 2f) * width * .82f;
                    var image = QuestArt(parent, "Coin", null, new Vector2(x, bottom + row * height * .5f), new Vector2(width, height), Color.white);
                    image.sprite = coin;
                    image.preserveAspect = true;
                    image.rectTransform.localRotation = Quaternion.Euler(0, 0, ((row + i) % 2 == 0 ? 1 : -1) * 7);
                }
            }
        }

        /// <summary>Ролик вместо горки: телевизор с «▶» и коином (Hud/cash-ad); пока его нет — тёмный экран в золотой рамке.</summary>
        private void CashAdArt(RectTransform parent)
        {
            ClearShopChildren(parent);
            var tv = HudArt("cash-ad");
            if (tv != null)
            {
                var image = QuestArt(parent, "TV", null, Vector2.zero, parent.sizeDelta, Color.white);
                image.sprite = tv;
                image.preserveAspect = true;
                return;
            }
            var screen = QuestArt(parent, "Screen", null, new Vector2(0, 10), new Vector2(180, 120), new Color(.08f, .05f, .04f));
            screen.sprite = UiTextures.RoundedRect(96, 64, 16, Color.white);
            screen.type = Image.Type.Sliced;
            var rim = screen.gameObject.AddComponent<Outline>();
            rim.effectColor = new Color(1f, .78f, .3f);
            rim.effectDistance = new Vector2(3, -3);
            var play = QuestText(screen.transform, "Play", "▶", new Vector2(4, 0), new Vector2(120, 100), 64, Color.white);
            play.font = _font;
            var coin = QuestArt(parent, "Coin", null, new Vector2(78, -48), new Vector2(56, 74), Color.white);
            coin.sprite = HubCurrencyArt.Silver;
            coin.preserveAspect = true;
            coin.rectTransform.localRotation = Quaternion.Euler(0, 0, -10);
        }

        private void PressCashCard(CashCard card)
        {
            if (_state == null || _busy || _cashAdTime >= 0) return;
            if (card.PackId.Length > 0) { _ = Dispatch("cash_buy", card.PackId); return; }
            if (HubCash.AdsLeft(_state, GiftNow) <= 0) return;
            _cashAdTime = 0;
            _cashAd.gameObject.SetActive(true);
            _cashAd.SetAsLastSibling();
        }

        private void PressCashTrade()
        {
            if (_state == null || _busy || _cashAdTime >= 0) return;
            var pick = CashPickNow();
            if (pick > 0) _ = Dispatch("cash_buy_gold", amount: pick);
        }

        /// <summary>Палец на шкале: от 1 золотого до того, что позволяют лимит дня и свободное серебро.</summary>
        private void SlideCashPick(float share)
        {
            var max = CashGoldMax();
            if (max > 0) _cashPick = 1 + Mathf.RoundToInt(Mathf.Clamp01(share) * (max - 1));
        }

        /// <summary>Сколько золота можно взять прямо сейчас: остаток лимита дня и свободное (не заёмное) серебро.</summary>
        private int CashGoldMax()
            => _state == null ? 0 : Math.Min(HubCash.GoldLeft(_state, GiftNow), HubShop.Spendable(_state) / HubCash.SilverPerGold);

        /// <summary>Выбранное на шкале, прижатое к тому, что можно сейчас; 0 — нельзя ничего.</summary>
        private int CashPickNow()
        {
            var max = CashGoldMax();
            return max == 0 ? 0 : Mathf.Clamp(_cashPick, 1, max);
        }

        /// <summary>Досмотрел — серебро; окно закрыли раньше — ролик пропал без награды.</summary>
        private void FinishCashAd(bool watched)
        {
            _cashAdTime = -1;
            _cashAd.gameObject.SetActive(false);
            if (watched) _ = Dispatch("cash_ad");
        }

        /// <summary>Каждый кадр: Esc, проявление, ролик, карточки и балансы.</summary>
        private void UpdateCashWindow(float deltaTime, bool typing)
        {
            if (_cashLayer == null) return;
            if (_cashShown && _page != _cashPage) _cashShown = false;
            var escape = !typing && Keyboard.current != null && Keyboard.current.escapeKey.isPressed;
            if (_cashShown && escape && !_cashEscapeHeld)
            {
                // Esc на ролике — только прервать ролик.
                if (_cashAdTime >= 0) FinishCashAd(false);
                else CloseCashWindow();
            }
            _cashEscapeHeld = escape;
            if (!_cashShown && _cashAdTime >= 0) FinishCashAd(false);
            _cashFade = Mathf.MoveTowards(_cashFade, _cashShown ? 1 : 0, deltaTime / .18f);
            _cashGroup.alpha = Mathf.SmoothStep(0, 1, _cashFade);
            _cashGroup.blocksRaycasts = _cashShown;
            if (!CashShown)
            {
                // Кнопка карты обновляется не каждый кадр: вернуть её, как только окно погасло.
                if (_cashLayer.gameObject.activeSelf) { _cashLayer.gameObject.SetActive(false); RefreshMapButton(); }
                return;
            }
            var reduced = _state != null && _state.Settings.ReducedMotion;
            _cashWindow.localScale = Vector3.one * (reduced ? 1 : Mathf.Lerp(.94f, 1, _cashGroup.alpha));
            _cashLayer.SetAsLastSibling();
            if (_cashAdTime >= 0)
            {
                _cashAdTime += deltaTime;
                if (_cashAdTime >= CashAdSeconds) FinishCashAd(true);
            }
            PaintCashWindow(deltaTime);
        }

        private void PaintCashWindow(float deltaTime)
        {
            if (_state == null) return;
            for (var i = 0; i < _cashTabs.Count; i++)
            {
                _cashTabs[i].Group.alpha = i == _cashTab ? 1 : .55f;
                _cashTabs[i].Glow.gameObject.SetActive(i == _cashTab);
            }
            var silver = _cashTab == 0;
            var trade = _cashTab == 2;
            if (_cashTabShown != _cashTab)
            {
                _cashTabShown = _cashTab;
                _cashTrade.gameObject.SetActive(trade);
                foreach (var card in _cashCards) card.Root.gameObject.SetActive(!trade);
                // У серебра первая карточка — ролик, у золота все четыре — наборы.
                var packs = trade ? new List<HubCashPack>() : HubCash.PacksOf(silver ? HubCash.Silver : HubCash.Gold).ToList();
                var offset = silver ? 1 : 0;
                for (var i = 0; i < _cashCards.Count && !trade; i++)
                {
                    var card = _cashCards[i];
                    var pack = i - offset >= 0 && i - offset < packs.Count ? packs[i - offset] : null;
                    card.PackId = pack?.Id ?? "";
                    if (pack == null)
                    {
                        CashAdArt(card.Art);
                        card.Amount.text = "+" + HubCash.AdReward;
                        card.Price.font = SignFont();
                        continue;
                    }
                    CashPile(card.Art, !silver, i);
                    card.Amount.text = HubMoney(pack.Amount);
                    card.Kind.text = pack.Bonus > 0 ? HubMoney(pack.Amount - pack.Bonus) + " + " + HubMoney(pack.Bonus) + " в подарок"
                        : silver ? "серебряных тун-тун коинов" : "золотых тун-тун коинов";
                    card.Ribbon.gameObject.SetActive(pack.Bonus > 0);
                    card.Ribbon.color = CashRed;
                    card.RibbonText.text = "+" + Mathf.RoundToInt(100f * pack.Bonus / (pack.Amount - pack.Bonus)) + "%";
                    // В шрифте вывесок нет «₽» — цена шрифтом игры.
                    card.Price.font = _font;
                    card.Price.fontStyle = FontStyle.Bold;
                    card.Price.fontSize = 32;
                    card.Price.text = pack.Price;
                    card.Price.color = Color.white;
                    card.Buy.interactable = true;
                }
            }
            if (trade) PaintCashTrade(deltaTime);
            else foreach (var card in _cashCards.Where(card => card.IsAd))
            {
                var left = HubCash.AdsLeft(_state, GiftNow);
                card.Kind.text = left > 0 ? "ещё " + left + " из " + HubCash.AdsPerDay + " сегодня"
                    : "новые ролики через " + QuestDuration(Math.Max(0, HubShop.NextShowcaseAt(GiftNow) - GiftNow));
                card.Ribbon.gameObject.SetActive(true);
                card.Ribbon.color = CashGreen;
                card.RibbonText.text = "БЕСПЛАТНО";
                var label = left > 0 ? "СМОТРЕТЬ" : "ЗАВТРА";
                if (card.Price.text != label) { card.Price.fontSize = 34; card.Price.fontStyle = FontStyle.Normal; card.Price.text = label; }
                card.Buy.interactable = left > 0 && !_busy && _cashAdTime < 0;
                card.Price.color = new Color(1, 1, 1, left > 0 ? 1 : .55f);
            }
            if (_cashAdTime >= 0) _cashAdCount.text = Mathf.CeilToInt(CashAdSeconds - _cashAdTime).ToString();

            var noAds = _state.Cash != null && _state.Cash.NoAds;
            var noAdsLabel = _cashNoAds.GetComponentInChildren<Text>();
            var noAdsText = noAds ? "БЕЗ РЕКЛАМЫ ✓" : "БЕЗ РЕКЛАМЫ · " + HubCash.NoAdsPrice;
            if (noAdsLabel.text != noAdsText) noAdsLabel.text = noAdsText;
            _cashNoAds.interactable = !noAds && !_busy;

            // Купленное «натекает» в балансы за полсекунды, как награда в окне заданий.
            var reduced = _state.Settings.ReducedMotion;
            _cashSilverShown = CashCount(_cashSilverShown, _state.Profile.Coins, deltaTime, reduced);
            _cashGoldShown = CashCount(_cashGoldShown, _state.Profile.Gold, deltaTime, reduced);
            _cashSilver.text = HubMoney(Mathf.RoundToInt(_cashSilverShown));
            _cashGold.text = HubMoney(Mathf.RoundToInt(_cashGoldShown));
        }

        // Быстро вначале и не медленнее 2000 в секунду в конце: и «+50», и «+6 250» досчитываются примерно за полсекунды.
        private static float CashCount(float shown, int real, float deltaTime, bool reduced)
            => reduced || deltaTime <= 0 ? real : Mathf.MoveTowards(shown, real, Mathf.Max(Mathf.Abs(real - shown) * deltaTime / .15f, 2000 * deltaTime));

        /// <summary>
        /// Обмен каждый кадр: горки растут с выбранным (стопка до 2, кучка до 6, гора до 12, дальше сундук), числа, шкала с
        /// делениями под нынешний предел, монетки лимита и огонёк по стрелке; почему нельзя — строкой под кнопкой.
        /// </summary>
        private void PaintCashTrade(float deltaTime)
        {
            var reduced = _state.Settings.ReducedMotion;
            _cashTime += deltaTime;
            var left = HubCash.GoldLeft(_state, GiftNow);
            var max = CashGoldMax();
            if (_cashPick < 0) _cashPick = Math.Min(5, max);
            var pick = CashPickNow();
            var shown = Math.Max(1, pick);
            var level = shown <= 2 ? 0 : shown <= 6 ? 1 : shown <= 12 ? 2 : 3;
            if (level != _cashPileLevel)
            {
                _cashPileLevel = level;
                CashPile(_cashGivePile, false, level);
                CashPile(_cashGetPile, true, level);
            }
            _cashGive.text = HubMoney(shown * HubCash.SilverPerGold);
            _cashGet.text = "+" + shown;
            var free = HubShop.Spendable(_state);
            _cashGiveNote.text = free < _state.Profile.Coins ? "свободных " + HubMoney(free) + " · заёмные не меняются" : "у тебя " + HubMoney(_state.Profile.Coins);
            _cashGetNote.text = "станет " + HubMoney(_state.Profile.Gold + shown);
            _cashTradeNote.text = left == 0 ? "на сегодня всё — лимит обновится в полночь"
                : max == 0 ? "нужно хотя бы " + HubCash.SilverPerGold + " свободных серебряных" : "";
            _cashTradeButton.interactable = pick > 0 && !_busy;
            // Нельзя ничего — окошки гаснут: числа в них только показывают курс.
            foreach (var box in _cashTradeBoxes) box.alpha = pick > 0 ? 1 : .45f;
            _cashTradeButton.GetComponentInChildren<Text>().color = new Color(1, 1, 1, pick > 0 ? 1 : .55f);

            // Шкала: деления на каждый золотой до нынешнего предела, кончики и каждый пятый — длиннее.
            var inner = CashPickTrackW - 52;
            if (_cashTicksFor != max)
            {
                _cashTicksFor = max;
                ClearShopChildren(_cashPickTicks);
                for (var v = 1; v <= max && max > 1; v++)
                {
                    var tall = v == 1 || v == max || v % 5 == 0 ? 14f : 8f;
                    QuestArt(_cashPickTicks, "Tick", null, new Vector2(-inner / 2 + inner * (v - 1f) / (max - 1), -21 - tall / 2), new Vector2(3, tall),
                        new Color(1f, .8f, .3f, .6f));
                }
            }
            var share = max > 1 ? (pick - 1f) / (max - 1) : pick > 0 ? 1 : 0;
            _cashPickFill.sizeDelta = new Vector2(inner * share, 14);
            _cashPickFill.GetComponent<Image>().enabled = share > 0;
            _cashPickKnob.anchoredPosition = new Vector2(-inner / 2 + inner * share, 2);
            _cashPickKnob.GetComponent<Image>().color = new Color(1, 1, 1, max > 0 ? 1 : .4f);
            _cashPickMinus.interactable = pick > 1;
            _cashPickPlus.interactable = pick < max;
            _cashPickMax.interactable = pick < max;

            // Лимит: взятые сегодня — тусклые и мельче, выбранные сейчас — крупнее и мерцают, остальные — чуть приглушены.
            var used = HubCash.GoldPerDay - left;
            for (var i = 0; i < _cashLimitPips.Count; i++)
            {
                var chosen = i >= used && i < used + pick;
                _cashLimitPips[i].color = i < used ? new Color(.3f, .28f, .26f, .7f) : chosen ? Color.white : new Color(.55f, .49f, .4f);
                var pulse = chosen && !reduced ? .06f * Mathf.Sin(_cashTime * 6 - i * .6f) : 0;
                _cashLimitPips[i].rectTransform.localScale = Vector3.one * (i < used ? .8f : chosen ? 1.2f + pulse : .9f);
            }
            _cashLimitLeft.text = left > 0 ? "ЕЩЁ " + left : "ВСЁ";
            _cashLimitNote.text = used == 0 ? "до " + HubCash.GoldPerDay + " золотых за серебро в день"
                : "новые через " + QuestDuration(Math.Max(0, HubShop.NextShowcaseAt(GiftNow) - GiftNow));

            // Картинка стрелки (Hud/cash-arrow) вспыхивает и подаётся вперёд; нельзя обменять — серая.
            if (_cashArrowArt)
            {
                var phase = Mathf.Repeat(_cashTime * 1.1f, 1.4f);
                var glow = reduced ? 0 : Mathf.Clamp01(1 - Mathf.Abs(phase - .4f) * 3);
                var gray = new Color(.45f, .45f, .45f);
                _cashArrows[0].color = pick > 0 ? Color.Lerp(new Color(.86f, .86f, .86f), Color.white, glow) : gray;
                _cashArrows[0].rectTransform.anchoredPosition = new Vector2(pick > 0 ? 8 * glow : 0, 118);
                return;
            }
            // Огонёк бежит по шеврону за шевроном и замирает; нельзя обменять — стрелка гаснет.
            for (var i = 0; i < _cashArrows.Count; i++)
            {
                var phase = Mathf.Repeat(_cashTime * 1.1f - i / 2 * .22f, 1.4f);
                var glow = reduced ? 1 : Mathf.Clamp01(1 - Mathf.Abs(phase - .4f) * 3);
                _cashArrows[i].color = pick > 0 ? Color.Lerp(CashArrowDim, SignGoldTop, .35f + .65f * glow) : CashArrowDim;
            }
        }

        private static Font SignFont() => Resources.Load<Font>("Fonts/SignNarrow");
    }
}
