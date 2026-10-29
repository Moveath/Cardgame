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
    /// Рынок Короля бубен изнутри — «Бубновые ряды» (2026-10-21, IDEAS.md «Рынок Короля бубен») — этаж <see cref="MarketLevel"/>,
    /// картинка `UI/Hub/market-hall` (2196×941, 21:9; исходник `screens/references/city/market-hall-v2-wide.png`).
    /// Вход — нырок в здание рынка в порту (и с карты). Выход — «ВЫЙТИ», «Назад», Esc или S.
    /// Корабль в арке причала зависит ТОЛЬКО от состояния заказа (<see cref="HubMarket.Ship"/>):
    /// - заказа нет (<see cref="HubShipStatus.None"/>) или он ещё в пути (<see cref="HubShipStatus.Sailing"/>) —
    ///   поверх арки лежит накладка `UI/Hub/market-arch-empty` (пустой причал и лунное море);
    /// - заказ прибыл (<see cref="HubShipStatus.Docked"/>) — накладка скрывается, в арке стоит корабль Туза бубен,
    ///   а на левом краю прилавка Короля появляется перевязанный лентой свёрток.
    /// По клику картинка арки никогда не переключается.
    /// </summary>
    public sealed partial class PlayerHubController
    {
        private const string MarketHall = "market-hall";
        private const int MarketLevel = 9;

        private const string MarketKingId = "market-king";
        private const string MarketScalesId = "market-scales";
        private const string MarketSevenId = "market-seven";
        private const string MarketShipId = "market-ship";
        private const int MarketCardsPerPage = 4;

        // Позиции облачка реплик и свёртка на прилавке (пиксели игровой картинки 2196×941)
        private const float MarketBubbleY = 175, MarketBubbleWidth = 480, MarketBubbleHeight = 112;
        private const float MarketKingBubbleX = 1150, MarketKingHeadX = 1175;
        private const float MarketSevenBubbleX = 1710, MarketSevenHeadX = 1710;
        private static readonly Rect MarketArchRect = new Rect(262, 30, 620, 620);
        private static readonly Rect MarketParcelRect = new Rect(835, 480, 124, 92);

        private static readonly string[] MarketKingTalk =
        {
            "Скупаю честно: 40 % цены серебром. Мне долги Тузяре отдавать — как всем.",
            "Упустил вещь в Лавке? Закажи у причала — корабль Туза бубен привезёт через два дня.",
            "Пока заказа нет или корабль в море — у причала пусто. Придёт товар — увидишь паруса в арке.",
            "Торговаться с Семёркой вздумал? Три карты угадаешь — матросы уступят четверть цены.",
        };

        private RectTransform _marketBubble, _marketParcel, _marketControls, _marketWallet;
        private RectTransform _marketLayer, _marketShipPanel, _marketScalesPanel, _marketSevenPanel;
        private RectTransform _marketShipContent, _marketScalesContent, _marketSevenContent;
        private Text _marketBubbleText, _marketWalletText;
        private Button _marketPrev, _marketNext;
        private Image _marketArchEmpty;
        private string _marketOpen, _marketLine;
        private int _marketTalkIndex, _marketPage;
        private bool _marketGreeted, _marketEscapeHeld, _marketArrowHeld;
        private float _marketRefreshAt;
        private HubSnapshot _marketPaintedFor;
        private HubShipStatus _marketLastShipStatus;

        private bool MarketInside => _cityLevel == MarketLevel && _page == "home" && _diveTarget < 0 && _visitPhase == 0;
        /// <summary>Открыто окно заказа, скупки или торга поверх зала Рынка.</summary>
        private bool MarketShown => _marketOpen != null && _cityLevel == MarketLevel && _page == "home";

        private static CityPlace MarketShipPlace() => Place(MarketShipId, MarketHall, "Причал заказов", "Корабль Туза бубен", null,
            530, 350, new float[] {
                300, 620, 300, 240, 350, 140, 430, 85, 530, 65, 630, 85, 710, 140, 760, 240, 760, 620
            });

        private static CityPlace MarketScalesPlace() => Place(MarketScalesId, MarketHall, "Скупка и посылки", "Король бубен", null,
            1140, 670, new float[] {
                720, 795, 720, 565, 1280, 575, 1280, 515, 1345, 420, 1385, 375, 1430, 420, 1495, 515, 1495, 580, 1525, 585, 1525, 860, 1365, 870
            });

        private static CityPlace MarketKingPlace() => Place(MarketKingId, MarketHall, "Король бубен", "Хозяин рынка", null,
            1150, 420, new float[] {
                965, 515, 965, 445, 1040, 405, 1080, 305, 1175, 265, 1270, 330, 1255, 430, 1290, 515, 1270, 570, 1025, 565
            }, talk: true);

        private static CityPlace MarketSevenPlace() => Place(MarketSevenId, MarketHall, "Торг «Больше-меньше»", "Семёрка треф", null,
            1740, 560, new float[] {
                1555, 910, 1555, 610, 1575, 445, 1615, 355, 1695, 300, 1795, 335, 1855, 455, 1910, 515, 1970, 635, 1970, 880
            });

        /// <summary>Накладка пустой арки, свёрток на прилавке и облачко Короля — дети этажа (зовётся из AddInteriorFurniture).</summary>
        private void AddMarketHall(RectTransform floor, float width)
        {
            var picture = PictureSize(MarketHall);
            var unit = new Vector2(width / picture.x, CityHeight / picture.y);
            Vector2 At(Rect area) => new Vector2((area.center.x / picture.x - .5f) * width, (.5f - area.center.y / picture.y) * CityHeight);

            // Накладка пустой арки: видна всегда, кроме случая, когда корабль доставил заказ (HubShipStatus.Docked).
            _marketArchEmpty = LoanArt(floor, "Market arch empty", "market-arch-empty", At(MarketArchRect),
                Vector2.Scale(MarketArchRect.size, unit), Color.white);
            if (_marketArchEmpty != null) _marketArchEmpty.raycastTarget = false;

            // Свёрток с заказом на левой стороне прилавка Короля — виден только когда корабль стоит у причала.
            _marketParcel = BuildMarketParcel(floor, At(MarketParcelRect), Vector2.Scale(MarketParcelRect.size, unit));

            var bubbleAt = new Vector2((MarketKingBubbleX / picture.x - .5f) * width, (.5f - MarketBubbleY / picture.y) * CityHeight);
            var (bubble, line, _) = SpeechBubble(floor, "Market bubble", bubbleAt, MarketBubbleWidth, MarketBubbleHeight,
                MarketKingHeadX - MarketKingBubbleX, unit);
            _marketBubble = bubble;
            _marketBubbleText = line;

            PaintMarket();
        }

        private RectTransform BuildMarketParcel(RectTransform floor, Vector2 at, Vector2 size)
        {
            var box = LoanArt(floor, "Market arrived parcel", null, at, size, new Color(.94f, .86f, .7f));
            box.sprite = UiTextures.RoundedRect(64, 64, 14, Color.white);
            box.type = Image.Type.Sliced;
            box.raycastTarget = false;
            var rim = box.gameObject.AddComponent<HubOutline>();
            rim.effectColor = new Color(.38f, .24f, .1f);
            rim.effectDistance = new Vector2(2, -2);
            var root = box.rectTransform;
            var hBand = LoanArt(root, "Ribbon H", null, Vector2.zero, new Vector2(size.x, size.y * .22f), new Color(.72f, .14f, .12f));
            hBand.raycastTarget = false;
            var vBand = LoanArt(root, "Ribbon V", null, Vector2.zero, new Vector2(size.x * .18f, size.y), new Color(.72f, .14f, .12f));
            vBand.raycastTarget = false;
            var seal = LoanArt(root, "Diamond charm", null, Vector2.zero, new Vector2(size.y * .36f, size.y * .36f), new Color(.92f, .72f, .28f));
            seal.sprite = UiTextures.RoundedRect(32, 32, 8, Color.white);
            seal.type = Image.Type.Sliced;
            seal.raycastTarget = false;
            seal.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
            root.gameObject.SetActive(false);
            return root;
        }

        /// <summary>Нажатие на место в зале Рынка.</summary>
        private void ClickMarket(string id)
        {
            if (!MarketInside || MarketShown || _state == null) return;
            if (id == MarketKingId)
            {
                _marketLine = MarketKingTalk[_marketTalkIndex++ % MarketKingTalk.Length];
                MoveMarketBubble(MarketKingBubbleX);
                PaintMarket();
                return;
            }
            _marketPage = 0;
            if (id == MarketShipId) OpenMarket("ship");
            else if (id == MarketScalesId) OpenMarket("scales");
            else if (id == MarketSevenId) OpenMarket("seven");
        }

        private void OpenMarket(string section)
        {
            if (_marketControls == null) BuildMarketControls();
            _marketOpen = section;
            _marketLayer.gameObject.SetActive(true);
            PaintMarket();
            _marketControls.SetAsLastSibling();
            _marketLayer.SetAsLastSibling();
        }

        /// <summary>«Назад» сначала закрывает открытое окно Рынка.</summary>
        private bool TryMarketBack()
        {
            if (!MarketShown) return false;
            _marketOpen = null;
            _marketLine = null;
            MoveMarketBubble(MarketKingBubbleX);
            PaintMarket();
            return true;
        }

        /// <summary>Каждый кадр: проверка прибытия корабля по времени, таймер в окне заказа, клавиши Esc и стрелок.</summary>
        private void UpdateMarket(float deltaTime, bool typing)
        {
            if (_cityLevel != MarketLevel)
            {
                _marketGreeted = false;
                _marketOpen = null;
            }
            var inside = MarketInside && _state != null;
            var controls = inside && !_mapOpen && _mapFade <= 0;
            if (_marketControls != null && _marketControls.gameObject.activeSelf != controls)
                _marketControls.gameObject.SetActive(controls);
            var showLayer = controls && _marketOpen != null;
            if (_marketLayer != null && _marketLayer.gameObject.activeSelf != showLayer)
                _marketLayer.gameObject.SetActive(showLayer);

            var keyboard = typing ? null : Keyboard.current;
            var escape = keyboard != null && keyboard.escapeKey.isPressed;
            if (MarketShown && escape && !_marketEscapeHeld) { _cityVerticalHeld = 1; TryMarketBack(); }
            _marketEscapeHeld = escape;

            var arrow = keyboard == null ? 0 : keyboard.leftArrowKey.isPressed || keyboard.aKey.isPressed ? -1
                : keyboard.rightArrowKey.isPressed || keyboard.dKey.isPressed ? 1 : 0;
            if (MarketShown && arrow != 0 && !_marketArrowHeld) TurnMarketPage(arrow);
            _marketArrowHeld = arrow != 0;

            if (!inside || _marketArchEmpty == null) return;
            if (!_marketGreeted)
            {
                _marketGreeted = true;
                _marketLine = null;
                MoveMarketBubble(MarketKingBubbleX);
                PaintMarket();
            }
            if (controls)
            {
                if (_marketControls == null) BuildMarketControls();
                _marketControls.SetAsLastSibling();
                if (showLayer) _marketLayer.SetAsLastSibling();
            }

            var now = PlayerHubRuntime.Clock.UnixSeconds;
            var shipStatus = HubMarket.Ship(_state, now);
            if (_marketPaintedFor != _state || shipStatus != _marketLastShipStatus)
            {
                PaintMarket();
            }
            else if (_marketOpen == "ship" && shipStatus == HubShipStatus.Sailing && Time.unscaledTime >= _marketRefreshAt)
            {
                // Живой отсчёт времени до прибытия корабля.
                _marketRefreshAt = Time.unscaledTime + 1f;
                PaintMarketShip();
            }
        }

        private void TurnMarketPage(int step)
        {
            var pages = MarketPageCount();
            if (pages <= 1) return;
            _marketPage = Mathf.Clamp(_marketPage + step, 0, pages - 1);
            PaintMarket();
        }

        private int MarketPageCount()
        {
            if (_state == null) return 1;
            var now = PlayerHubRuntime.Clock.UnixSeconds;
            if (_marketOpen == "ship" && HubMarket.Ship(_state, now) == HubShipStatus.None)
            {
                var count = HubMarket.Missed(_state, now).Count;
                return Math.Max(1, (count + MarketCardsPerPage - 1) / MarketCardsPerPage);
            }
            if (_marketOpen == "scales")
            {
                var count = HubMarket.OwnedForSale(_state).Count;
                return Math.Max(1, (count + MarketCardsPerPage - 1) / MarketCardsPerPage);
            }
            return 1;
        }

        private void MoveMarketBubble(float bubbleX)
        {
            if (_marketBubble == null) return;
            var picture = PictureSize(MarketHall);
            var width = _floorWidths[MarketLevel];
            if (width <= 0) return;
            _marketBubble.anchoredPosition = new Vector2((bubbleX / picture.x - .5f) * width, (.5f - MarketBubbleY / picture.y) * CityHeight);
        }

        private void BuildMarketControls()
        {
            _marketControls = StageLayer("Market controls");
            _marketWallet = LoanArt(_marketControls, "Market wallet", null, new Vector2(646, 396), new Vector2(260, 60), new Color(.08f, .045f, .025f, .95f)).rectTransform;
            var wallet = _marketWallet.GetComponent<Image>();
            wallet.sprite = UiTextures.RoundedRect(64, 64, 26, Color.white);
            wallet.type = Image.Type.Sliced;
            var coin = LoanArt(_marketWallet, "Coin", null, new Vector2(-104, 0), new Vector2(64, 64), Color.white);
            coin.sprite = HubCurrencyArt.Silver;
            coin.preserveAspect = true;
            _marketWalletText = LoanText(_marketWallet, "Balance", "", new Vector2(28, 0), new Vector2(184, 50), 35, Color.white);
            PinToCorner(_marketWallet, 1);

            _marketLayer = StageLayer("Market overlay");
            var dim = LoanArt(_marketLayer, "Market shade", null, Vector2.zero, new Vector2(6000, 6000), new Color(0, 0, 0, .68f));
            dim.raycastTarget = true;

            _marketShipPanel = BuildMarketWindow(_marketLayer, "Market ship window", "КОРАБЛЬ ТУЗА БУБЕН · ЗАКАЗ", out _marketShipContent);
            _marketScalesPanel = BuildMarketWindow(_marketLayer, "Market scales window", "КОРОЛЬ БУБЕН · СКУПКА (40 %)", out _marketScalesContent);
            _marketSevenPanel = BuildMarketWindow(_marketLayer, "Market seven window", "СЕМЁРКА ТРЕФ · ТОРГ ПО ЗАКОНУ СТОЛА", out _marketSevenContent);

            _marketPrev = CityButton(_marketLayer, "‹", 24, 380, 80, 120, () => TurnMarketPage(-1));
            _marketNext = CityButton(_marketLayer, "›", 1600 - 24 - 80, 380, 80, 120, () => TurnMarketPage(1));
            CityButton(_marketLayer, "ЗАКРЫТЬ", (1600 - 260) / 2f, 900 - 22 - 58, 260, 58, () => TryMarketBack());
            _marketLayer.gameObject.SetActive(false);
        }

        private RectTransform BuildMarketWindow(RectTransform parent, string name, string title, out RectTransform content)
        {
            var root = LoanRect(parent, name, Vector2.zero, new Vector2(1600, 900));
            var frame = LoanArt(root, "Window frame", null, new Vector2(0, 16), new Vector2(1360, 710), ArchiveWood);
            frame.sprite = UiTextures.RoundedRect(64, 64, 24, Color.white);
            frame.type = Image.Type.Sliced;
            frame.raycastTarget = true;
            var rim = frame.gameObject.AddComponent<HubOutline>();
            rim.effectColor = new Color(.62f, .45f, .18f);
            rim.effectDistance = new Vector2(3, -3);

            var inner = LoanArt(root, "Window paper", null, new Vector2(0, -10), new Vector2(1316, 616), ShopPaper);
            inner.sprite = UiTextures.RoundedRect(64, 64, 16, Color.white);
            inner.type = Image.Type.Sliced;

            LoanText(root, "Window title", title, new Vector2(0, 340), new Vector2(1100, 44), 34, ArchiveGold, true);
            content = LoanRect(root, "Content", new Vector2(0, -10), new Vector2(1290, 596));
            return root;
        }

        private void PaintMarket()
        {
            if (_state == null) return;
            _marketPaintedFor = _state;
            var now = PlayerHubRuntime.Clock.UnixSeconds;
            var ship = HubMarket.Ship(_state, now);
            _marketLastShipStatus = ship;

            // Корабль появляется ТОЛЬКО тогда, когда заказ прибыл (HubShipStatus.Docked).
            if (_marketArchEmpty != null) _marketArchEmpty.gameObject.SetActive(ship != HubShipStatus.Docked);
            if (_marketParcel != null) _marketParcel.gameObject.SetActive(ship == HubShipStatus.Docked);

            if (_marketBubble != null)
            {
                _marketBubbleText.text = _marketLine ?? MarketGreeting(ship);
                _marketBubble.gameObject.SetActive(true);
            }

            if (_marketControls != null)
                _marketWalletText.text = HubMoney(_state.Profile.Coins);

            if (_marketLayer == null) return;
            _marketShipPanel.gameObject.SetActive(_marketOpen == "ship");
            _marketScalesPanel.gameObject.SetActive(_marketOpen == "scales");
            _marketSevenPanel.gameObject.SetActive(_marketOpen == "seven");

            var pages = MarketPageCount();
            _marketPage = Mathf.Clamp(_marketPage, 0, pages - 1);
            _marketPrev.gameObject.SetActive(_marketOpen != null && pages > 1 && _marketPage > 0);
            _marketNext.gameObject.SetActive(_marketOpen != null && pages > 1 && _marketPage < pages - 1);

            if (_marketOpen == "ship") PaintMarketShip();
            else if (_marketOpen == "scales") PaintMarketScales();
            else if (_marketOpen == "seven") PaintMarketSeven();
        }

        private string MarketGreeting(HubShipStatus ship)
        {
            if (ship == HubShipStatus.Docked)
            {
                var item = _state.Catalog.FirstOrDefault(i => i.Id == _state.Market.Order.ItemId);
                return "Корабль Туза бубен пришёл! Забирай свёрток" + (item != null ? " с «" + item.Name + "»" : "") + " у причала или на прилавке.";
            }
            if (ship == HubShipStatus.Sailing)
            {
                var item = _state.Catalog.FirstOrDefault(i => i.Id == _state.Market.Order.ItemId);
                return "Корабль ещё в море — везёт твой заказ" + (item != null ? " «" + item.Name + "»" : "") + ". У причала пока пусто.";
            }
            if (_state.Market?.HaggleDiscount == true)
                return "Семёрка договорился с матросами! На заказ у причала сейчас скидка 25 %.";
            return "Добро пожаловать в Бубновые ряды! Слева — заказ с корабля, на весах — скупка, справа — Семёрка.";
        }

        // ── 1. Корабль Туза бубен («Упустил — закажи») ─────────────────────────────────

        private void PaintMarketShip()
        {
            ClearShopChildren(_marketShipContent);
            var now = PlayerHubRuntime.Clock.UnixSeconds;
            var status = HubMarket.Ship(_state, now);

            if (status == HubShipStatus.Docked)
            {
                var item = _state.Catalog.FirstOrDefault(i => i.Id == _state.Market.Order.ItemId);
                LoanText(_marketShipContent, "Arrived header", "КОРАБЛЬ В ПОРТУ — ЗАКАЗ ДОСТАВЛЕН!", new Vector2(0, 235), new Vector2(1180, 44), 34, ShopRed, true);
                LoanText(_marketShipContent, "Arrived sub", "Парусник Туза бубен встал у причала в арке. Забери доставленную вещь, и корабль снова уйдёт в рейс.",
                    new Vector2(0, 188), new Vector2(1180, 36), 24, ShopMuted);
                if (item != null)
                {
                    ShopItemArt(_marketShipContent, item, new Vector2(0, 28), 210);
                    NewsFit(LoanText(_marketShipContent, "Item name", "«" + item.Name + "»", new Vector2(0, -108), new Vector2(760, 44), 34, LoanInk, true));
                    LoanText(_marketShipContent, "Item kind", HubCategoryName(item.Category) + " · " + HubShop.RarityName(item.Rarity),
                        new Vector2(0, -148), new Vector2(760, 32), 24, ShopMuted);
                }
                LoanPaperButton(_marketShipContent, "ЗАБРАТЬ ЗАКАЗ", new Vector2(0, -225), new Vector2(380, 64), PickUpMarketOrder, true);
                return;
            }

            if (status == HubShipStatus.Sailing)
            {
                var item = _state.Catalog.FirstOrDefault(i => i.Id == _state.Market.Order.ItemId);
                var left = TimeSpan.FromSeconds(Math.Max(0, HubMarket.SecondsLeft(_state, now)));
                var timer = (int)left.TotalHours + " ч " + left.Minutes.ToString("00") + " мин " + left.Seconds.ToString("00") + " с";
                LoanText(_marketShipContent, "Sailing header", "КОРАБЛЬ В ПУТИ — У ПРИЧАЛА ПУСТО", new Vector2(0, 235), new Vector2(1180, 44), 34, LoanInk, true);
                LoanText(_marketShipContent, "Sailing sub", "Корабль появится в арке причала только тогда, когда привезёт твой заказ.",
                    new Vector2(0, 188), new Vector2(1180, 36), 24, ShopMuted);
                if (item != null)
                {
                    ShopItemArt(_marketShipContent, item, new Vector2(0, 36), 195);
                    NewsFit(LoanText(_marketShipContent, "Item name", "В трюме: «" + item.Name + "»", new Vector2(0, -96), new Vector2(760, 42), 32, LoanInk, true));
                }
                LoanText(_marketShipContent, "Timer", "Прибытие через: " + timer, new Vector2(0, -160), new Vector2(820, 42), 30, ShopRed, true);
                LoanText(_marketShipContent, "Hint", "Один заказ за рейс · оплачено " + _state.Market.Order.Price + " коинов",
                    new Vector2(0, -210), new Vector2(820, 34), 24, ShopMuted);
                return;
            }

            var discount = _state.Market?.HaggleDiscount == true;
            var subtitle = discount
                ? "Упущенные вещи из Лавки за 7 дней · Скидка Семёрки −25 % активна! Корабль привезёт заказ через 2 дня."
                : "Вещи, бывшие на витрине Лавки за последние 7 дней (цена ×1,5 · доставка кораблём за 2 дня · у причала пусто, пока не прибудет).";
            LoanText(_marketShipContent, "Order intro", subtitle, new Vector2(0, 252), new Vector2(1240, 38), 23, discount ? ShopRed : ShopMuted, discount);

            var missed = HubMarket.Missed(_state, now);
            if (missed.Count == 0)
            {
                LoanText(_marketShipContent, "Empty missed", "За последнюю неделю упущенных вещей нет — загляни в Лавку Дамы бубен.",
                    Vector2.zero, new Vector2(960, 80), 28, ShopMuted);
                return;
            }

            for (var i = 0; i < MarketCardsPerPage; i++)
            {
                var index = _marketPage * MarketCardsPerPage + i;
                if (index >= missed.Count) break;
                var x = (i - (MarketCardsPerPage - 1) / 2f) * 308f;
                PaintMarketOrderCard(missed[index], new Vector2(x, -22), discount);
            }
        }

        private void PaintMarketOrderCard(HubCatalogItem item, Vector2 at, bool discount)
        {
            var card = LoanArt(_marketShipContent, "Order card " + item.Id, null, at, new Vector2(288, 490), new Color(1f, .96f, .88f));
            card.sprite = UiTextures.RoundedRect(64, 64, 16, Color.white);
            card.type = Image.Type.Sliced;
            var edge = card.gameObject.AddComponent<HubOutline>();
            edge.effectColor = new Color(.55f, .38f, .14f);
            edge.effectDistance = new Vector2(2, -2);
            var box = card.rectTransform;

            ShopItemArt(box, item, new Vector2(0, 118), 156);
            NewsFit(LoanText(box, "Name", item.Name, new Vector2(0, 12), new Vector2(260, 38), 26, LoanInk, true));
            LoanText(box, "Kind", HubCategoryName(item.Category) + " · " + HubShop.RarityName(item.Rarity),
                new Vector2(0, -22), new Vector2(260, 28), 20, ShopMuted);

            var about = LoanText(box, "About", item.Description, new Vector2(0, -76), new Vector2(256, 68), 19, LoanInk);
            about.rectTransform.sizeDelta = new Vector2(256, 68);
            about.verticalOverflow = VerticalWrapMode.Truncate;
            about.resizeTextForBestFit = true;
            about.resizeTextMinSize = 14;
            about.resizeTextMaxSize = 19;

            var price = HubMarket.OrderPrice(item, discount);
            var basePrice = HubMarket.BaseOrderPrice(item);
            var priceLabel = LoanText(box, "Price", discount
                ? price + " коинов  <size=18><color=#8A6A44>" + basePrice + "</color></size>"
                : price + " коинов", new Vector2(0, -138), new Vector2(260, 34), 25, discount ? ShopRed : LoanInk, true);
            priceLabel.supportRichText = true;

            var itemId = item.Id;
            LoanPaperButton(box, "ЗАКАЗАТЬ", new Vector2(0, -196), new Vector2(244, 52), () => OrderMarketItem(itemId), true);
        }

        // ── 2. Король бубен — скупка (40 % серебром) ───────────────────────────────────

        private void PaintMarketScales()
        {
            ClearShopChildren(_marketScalesContent);
            var now = PlayerHubRuntime.Clock.UnixSeconds;
            var docked = HubMarket.Ship(_state, now) == HubShipStatus.Docked;

            var intro = docked
                ? "На прилавке ждёт свёрток с корабля! А ненужные вещи Король принимает на весы за 40 % цены."
                : "Сдай ненужную вещь на медные весы — Король заплатит 40 % её цены серебром (базовые и надетые не берёт).";
            LoanText(_marketScalesContent, "Scales intro", intro, new Vector2(docked ? -150 : 0, 252),
                new Vector2(docked ? 920 : 1240, 38), 23, docked ? ShopRed : ShopMuted, docked);
            if (docked)
                LoanPaperButton(_marketScalesContent, "ЗАБРАТЬ ПОСЫЛКУ", new Vector2(465, 252), new Vector2(290, 48), PickUpMarketOrder, true);

            var sellable = HubMarket.OwnedForSale(_state);
            if (sellable.Count == 0)
            {
                LoanText(_marketScalesContent, "Empty sellable",
                    "Пока нечего сдать на весы.\nКупленные в Лавке или привезённые кораблём вещи можно продать Королю за 40 % их цены.",
                    Vector2.zero, new Vector2(980, 110), 27, ShopMuted);
                return;
            }

            for (var i = 0; i < MarketCardsPerPage; i++)
            {
                var index = _marketPage * MarketCardsPerPage + i;
                if (index >= sellable.Count) break;
                var x = (i - (MarketCardsPerPage - 1) / 2f) * 308f;
                PaintMarketSellCard(sellable[index], new Vector2(x, -22));
            }
        }

        private void PaintMarketSellCard(HubCatalogItem item, Vector2 at)
        {
            var card = LoanArt(_marketScalesContent, "Sell card " + item.Id, null, at, new Vector2(288, 490), new Color(1f, .96f, .88f));
            card.sprite = UiTextures.RoundedRect(64, 64, 16, Color.white);
            card.type = Image.Type.Sliced;
            var edge = card.gameObject.AddComponent<HubOutline>();
            edge.effectColor = new Color(.55f, .38f, .14f);
            edge.effectDistance = new Vector2(2, -2);
            var box = card.rectTransform;

            ShopItemArt(box, item, new Vector2(0, 118), 156);
            NewsFit(LoanText(box, "Name", item.Name, new Vector2(0, 12), new Vector2(260, 38), 26, LoanInk, true));
            LoanText(box, "Kind", HubCategoryName(item.Category) + " · " + HubShop.RarityName(item.Rarity),
                new Vector2(0, -22), new Vector2(260, 28), 20, ShopMuted);

            var equipped = HubMarket.IsEquipped(_state, item.Id);
            var sellPrice = HubMarket.SellPrice(item);
            LoanText(box, "Buy price", "Цена в Лавке: " + item.Price, new Vector2(0, -76), new Vector2(260, 30), 21, ShopMuted);
            LoanText(box, "Sell price", "Выкуп Короля: +" + sellPrice + " коинов", new Vector2(0, -122), new Vector2(260, 36), 25, LoanInk, true);

            var itemId = item.Id;
            var btn = LoanPaperButton(box, equipped ? "СНАЧАЛА СНИМИ" : "ПРОДАТЬ ЗА +" + sellPrice,
                new Vector2(0, -196), new Vector2(252, 52), () => SellMarketItem(itemId), !equipped);
            btn.interactable = !equipped;
        }

        // ── 3. Семёрка треф — торг «Больше-меньше» ────────────────────────────────────

        private void PaintMarketSeven()
        {
            ClearShopChildren(_marketSevenContent);
            var market = _state.Market;
            LoanText(_marketSevenContent, "Seven intro",
                "Угадай три раза подряд, старше или младше следующая карта — Семёрка договорится с матросами на −25 % к заказу. Ставок нет.",
                new Vector2(0, 250), new Vector2(1220, 40), 23, ShopMuted);

            // Открытая карта Семёрки
            var cardBox = LoanArt(_marketSevenContent, "Open card", null, new Vector2(-170, 35), new Vector2(210, 290), new Color(.98f, .95f, .88f));
            cardBox.sprite = UiTextures.RoundedRect(64, 64, 18, Color.white);
            cardBox.type = Image.Type.Sliced;
            var rim = cardBox.gameObject.AddComponent<HubOutline>();
            rim.effectColor = new Color(.25f, .18f, .1f);
            rim.effectDistance = new Vector2(3, -3);

            var red = HubMarket.IsRedSuit(market.HaggleSuit);
            var label = LoanText(cardBox.rectTransform, "Card rank", HubMarket.CardLabel(market.HaggleCard, market.HaggleSuit),
                Vector2.zero, new Vector2(190, 220), 68, red ? ShopRed : new Color(.14f, .12f, .1f), true);
            label.font = _font;
            LoanText(_marketSevenContent, "Card caption", "Карта на ящике", new Vector2(-170, -135), new Vector2(260, 32), 22, ShopMuted);

            // Правая часть — счёт серии и кнопки выбора
            var statusX = 185f;
            LoanText(_marketSevenContent, "Streak", "Угадано подряд: " + market.HaggleStreak + " из " + HubMarket.HaggleTarget,
                new Vector2(statusX, 125), new Vector2(640, 44), 34, LoanInk, true);

            if (market.HagglePrevCard >= 2)
            {
                LoanText(_marketSevenContent, "Previous card",
                    "Предыдущая карта: " + HubMarket.CardLabel(market.HagglePrevCard, market.HagglePrevSuit),
                    new Vector2(statusX, 72), new Vector2(640, 34), 24, ShopMuted);
            }

            if (market.HaggleDiscount)
            {
                LoanText(_marketSevenContent, "Discount ready",
                    "Скидка −25 % на следующий заказ с корабля активна!\nИди к арке причала и заказывай любую упущенную вещь.",
                    new Vector2(statusX, -20), new Vector2(660, 96), 27, ShopRed, true);
                return;
            }

            if (market.HaggleDone)
            {
                LoanText(_marketSevenContent, "Done today",
                    "На сегодня торг окончен — промахнулся.\nНичего не потеряно! Загляни к Семёрке завтра.",
                    new Vector2(statusX, -20), new Vector2(660, 96), 27, ShopMuted, true);
                return;
            }

            LoanPaperButton(_marketSevenContent, "СТАРШЕ ▲", new Vector2(statusX - 155, -45), new Vector2(280, 66),
                () => GuessMarketHaggle("higher"), true);
            LoanPaperButton(_marketSevenContent, "МЛАДШЕ ▼", new Vector2(statusX + 155, -45), new Vector2(280, 66),
                () => GuessMarketHaggle("lower"), true);
            LoanText(_marketSevenContent, "Rank hint", "По старшинству: от 2 до Туза (В = 11, Д = 12, К = 13, Т = 14)",
                new Vector2(statusX, -135), new Vector2(680, 32), 21, ShopMuted);
        }

        // ── Команды к сервису ──────────────────────────────────────────────────────────

        private async Task<bool> MarketRun(string action, string target, string reply, float bubbleX)
        {
            if (_busy) return false;
            SetBusy(true);
            var result = await PlayerHubRuntime.ExecuteAsync(new HubCommand { Action = action, Target = target });
            if (this == null) return false;
            SetBusy(false);
            if (result.Success) { _state = result.Snapshot; Render(); }
            _marketLine = result.Success ? (string.IsNullOrEmpty(reply) ? result.Message : reply) : PlayerHubRuntime.ErrorText(result);
            MoveMarketBubble(bubbleX);
            PaintMarket();
            return result.Success;
        }

        private async void OrderMarketItem(string itemId)
            => await MarketRun("market_order", itemId,
                "Заказ оформлен! Корабль Туза бубен привезёт вещь через 2 дня — тогда и встанет у причала.", MarketKingBubbleX);

        private async void PickUpMarketOrder()
            => await MarketRun("market_pickup", "",
                "Носи на здоровье! Корабль разгрузился и снова ушёл в море.", MarketKingBubbleX);

        private async void SellMarketItem(string itemId)
            => await MarketRun("market_sell", itemId,
                "Честный вес! Вот твои 40 % серебром.", MarketKingBubbleX);

        private async void GuessMarketHaggle(string guess)
            => await MarketRun("market_haggle", guess, "", MarketSevenBubbleX);
    }
}
