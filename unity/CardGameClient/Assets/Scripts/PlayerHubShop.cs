using System;
using System.Linq;
using System.Threading.Tasks;
using CardGame.Core.Meta;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>
    /// Лавка Дамы бубен изнутри (2026-10-17, IDEAS.md «Лавка Дамы бубен — витрина дня») — этаж <see cref="ShopLevel"/>,
    /// картинка `UI/Hub/shop-counter` (2196×941, 21:9; исходник `screens/references/city/shop-counter-v3-wide.png`).
    /// Вход — нырок в дверь Лавки на площади, как в банк и таверну (автор: «как будто внутрь заходишь»), и с карты;
    /// вкладка «Магазин» везёт туда же. Выход — «ВЫЙТИ», «Назад», Esc или S. На прилавке витрина дня из ядра (HubShop):
    /// подиум слева — товар дня со скидкой, ниши на стене и бархатные подушки — остальное. Вещи (пока цветные заглушки
    /// по виду предмета), ценники, «−30%» на золотом ромбе, таймер на меловой доске и облачко Дамы — детьми этажа, едут
    /// с ним. Места витрины и Дама — места здания (контур при наведении): нажал вещь — карточка с ценой и «Купить»,
    /// купленное остаётся на месте с биркой «Продано». Поверх зала — счёт серебра и «Покажи другое» (новая полка,
    /// рекламы пока нет).
    /// </summary>
    public sealed partial class PlayerHubController
    {
        private const string ShopCounter = "shop-counter";
        private const int ShopLevel = 7;
        // Места заданы в пикселях утверждённой картинки 16:9 (`shop-counter-v2.png`, 1672×941); игровая шире на столько
        // с каждого бока (`widen_room.py shop`).
        private const float ShopMargin = (2196 - 1672) / 2f;
        private const string ShopQueenId = "shop-queen", ShopSlotPrefix = "shop-slot-";

        // Методы, а не поля: из них собираются места в CityPlaces при инициализации класса, а порядок статических полей
        // разных файлов partial-класса не задан.
        /// <summary>Место витрины: 0 — подиум (товар дня), 1–2 — ниши слева и справа от Дамы, 3–4 — подушки. Средняя ниша — за её головой.</summary>
        private static Rect ShopSpot(int slot)
        {
            switch (slot)
            {
                case 0: return new Rect(50, 336, 160, 130);
                case 1: return new Rect(430, 178, 180, 150);
                case 2: return new Rect(1063, 178, 180, 150);
                case 3: return new Rect(400, 530, 170, 150);
                default: return new Rect(1016, 530, 170, 150);
            }
        }

        /// <summary>Ценник — под местом.</summary>
        private static Rect ShopTag(int slot)
        {
            switch (slot)
            {
                case 0: return new Rect(30, 520, 200, 66);
                case 1: return new Rect(420, 348, 200, 66);
                case 2: return new Rect(1053, 348, 200, 66);
                case 3: return new Rect(385, 745, 200, 66);
                default: return new Rect(1001, 745, 200, 66);
            }
        }

        private static float[] ShopBox(Rect area) => new[]
        {
            area.xMin + ShopMargin, area.yMax, area.xMin + ShopMargin, area.yMin, area.xMax + ShopMargin, area.yMin, area.xMax + ShopMargin, area.yMax,
        };

        /// <summary>Место витрины в зале: нажимается вещь и её ценник, контур — вокруг вещи.</summary>
        private static CityPlace ShopSlotPlace(int slot)
        {
            var spot = ShopSpot(slot);
            var tag = ShopTag(slot);
            var hit = Rect.MinMaxRect(Mathf.Min(spot.xMin, tag.xMin), Mathf.Min(spot.yMin, tag.yMin), Mathf.Max(spot.xMax, tag.xMax), Mathf.Max(spot.yMax, tag.yMax));
            return Place(ShopSlotPrefix + slot, ShopCounter, slot == 0 ? "Товар дня" : "Витрина", "Дама бубен", null,
                spot.center.x + ShopMargin, spot.center.y, ShopBox(hit), outlinePoints: ShopBox(spot));
        }

        /// <summary>Дама за прилавком: нажатие — реплика.</summary>
        private static CityPlace ShopQueenPlace()
        {
            var outline = new float[] { 690, 225, 780, 212, 880, 180, 980, 190, 990, 300, 1005, 380, 1040, 470, 1030, 560, 1000, 645,
                860, 662, 700, 645, 640, 560, 600, 455, 640, 385, 688, 368 };
            for (var i = 0; i < outline.Length; i += 2) outline[i] += ShopMargin;
            return Place(ShopQueenId, ShopCounter, "Дама бубен", "Дама бубен", null, 830 + ShopMargin, 420, outline, talk: true);
        }

        private static readonly Rect ShopPlaque = new Rect(88, 222, 90, 56);      // золотой ромб над подиумом — «−30%»
        private static readonly Rect ShopBoard = new Rect(1268, 525, 142, 175);    // меловая доска — таймер
        private const float ShopBubbleX = 835, ShopBubbleY = 116, ShopBubbleWidth = 470, ShopBubbleHeight = 112, ShopQueenHeadX = 840;
        private static readonly Color ShopPaper = new Color(.97f, .91f, .78f), ShopMuted = new Color(.45f, .33f, .2f),
            ShopRed = new Color(.62f, .1f, .08f), ShopChalk = new Color(.93f, .93f, .88f);

        private static readonly string[] ShopTalk =
        {
            "Новый товар — каждую полночь. Заглядывай!",
            "Не нравится полка? Попроси — покажу, что на складе.",
            "Помидоры — это не ко мне. Кидаться — дурной тон.",
            "Всё, что видишь, — без подвоха. Не то что у Тузяры.",
        };

        private readonly RectTransform[] _shopSlots = new RectTransform[HubShop.SlotCount];
        private RectTransform _shopBubble, _shopControls, _shopWallet, _shopDetail, _shopDetailArt;
        private Text _shopBubbleText, _shopBoardText, _shopWalletText, _shopPlaqueText;
        private Text _shopDetailName, _shopDetailKind, _shopDetailAbout, _shopDetailPrice;
        private Button _shopReroll, _shopDetailAction;
        private string _shopItem, _shopLine;
        private int _shopTalk;
        private bool _shopGreeted, _shopEscapeHeld;
        private long _shopDay;
        private float _shopRefreshAt;
        // Снимок профиля, по которому нарисован прилавок: пришёл новый (покупка, полночь, награда) — перерисовать.
        private HubSnapshot _shopPaintedFor;

        private bool ShopInside => _cityLevel == ShopLevel && _page == "home" && _diveTarget < 0 && _visitPhase == 0;
        /// <summary>Открыта карточка вещи: под ней зал не слушает клавиши, «ВЫЙТИ» и карта спрятаны.</summary>
        private bool ShopCardShown => _shopItem != null && _cityLevel == ShopLevel && _page == "home";
        private static Vector2 ShopAt(Rect area) => new Vector2(area.center.x - 1672 / 2f, 941 / 2f - area.center.y);

        /// <summary>Вкладка «Магазин»: поездка в Лавку, как с карты (уже в ней — просто главная зала).</summary>
        private void GoToShop()
        {
            GoHome();
            if (_cityLevel == ShopLevel) return;
            _mapTripDistrict = -1;
            _mapTripPlace = Array.FindIndex(CityPlaces, place => place.Id == "shop");
            ContinueMapTrip();
        }

        /// <summary>Вещи, ценники, ромб скидки, доска и облачко Дамы — детьми этажа (зовётся из AddInteriorFurniture).</summary>
        private void AddShopCounter(RectTransform floor, float width)
        {
            var picture = PictureSize(ShopCounter);
            var unit = new Vector2(width / picture.x, CityHeight / picture.y);
            Vector2 At(Rect area) => new Vector2(((area.center.x + ShopMargin) / picture.x - .5f) * width, (.5f - area.center.y / picture.y) * CityHeight);
            for (var i = 0; i < _shopSlots.Length; i++)
                _shopSlots[i] = LoanRect(floor, "Shop slot " + i, At(ShopSpot(i)), Vector2.Scale(ShopSpot(i).size, unit));

            _shopPlaqueText = LoanText(floor, "Shop discount", "−" + HubShop.DayDiscountPercent + "%", At(ShopPlaque),
                Vector2.Scale(ShopPlaque.size, unit), Mathf.RoundToInt(30 * unit.y), Color.white, true);
            var gold = _shopPlaqueText.gameObject.AddComponent<HubSignText>();
            gold.Top = SignGoldTop;
            gold.Bottom = SignGoldBottom;
            _shopPlaqueText.gameObject.AddComponent<Shadow>().effectColor = new Color(.1f, .04f, 0, .85f);

            _shopBoardText = LoanText(floor, "Shop board", "", At(ShopBoard), Vector2.Scale(ShopBoard.size, unit), Mathf.RoundToInt(24 * unit.y), ShopChalk);
            _shopBoardText.lineSpacing = .7f;

            // Облачко над Дамой — на месте средней ниши (она за её головой и пустует), хвостик к шляпке.
            var at = At(new Rect(ShopBubbleX, ShopBubbleY, 0, 0));
            var (bubble, line, _) = SpeechBubble(floor, "Shop queen bubble", at, ShopBubbleWidth, ShopBubbleHeight,
                ShopQueenHeadX - ShopBubbleX, unit);
            _shopBubble = bubble;
            _shopBubbleText = line;
            PaintShop();
        }

        /// <summary>Нажатие на место зала Лавки: вещь — её карточка, Дама — реплика.</summary>
        private void ClickShop(string id)
        {
            if (id == ShopQueenId) TalkToShopQueen();
            else if (id.StartsWith(ShopSlotPrefix, StringComparison.Ordinal) && int.TryParse(id.Substring(ShopSlotPrefix.Length), out var slot))
                OpenShopSlot(slot);
        }

        /// <summary>«Назад» сначала закрывает карточку вещи.</summary>
        private bool TryShopBack()
        {
            if (!ShopCardShown) return false;
            _shopItem = null;
            PaintShop();
            return true;
        }

        /// <summary>Каждый кадр: приветствие при входе, кнопки поверх зала, Esc у карточки, таймер, новая витрина в полночь.</summary>
        private void UpdateShop(float deltaTime, bool typing)
        {
            if (_cityLevel != ShopLevel)
            {
                _shopGreeted = false;
                _shopItem = null;
            }
            var inside = ShopInside && _state != null;
            var controls = inside && !_mapOpen && _mapFade <= 0;
            if (_shopControls != null && _shopControls.gameObject.activeSelf != controls) _shopControls.gameObject.SetActive(controls);
            if (_shopDetail != null && _shopDetail.gameObject.activeSelf != (controls && _shopItem != null))
                _shopDetail.gameObject.SetActive(controls && _shopItem != null);
            var escape = !typing && Keyboard.current != null && Keyboard.current.escapeKey.isPressed;
            // Esc закрывает карточку; его удержание — уже учтённое нажатие, из Лавки оно не выводит.
            if (ShopCardShown && escape && !_shopEscapeHeld) { _cityVerticalHeld = 1; TryShopBack(); }
            _shopEscapeHeld = escape;
            if (!inside || _shopSlots[0] == null) return;
            if (!_shopGreeted)
            {
                _shopGreeted = true;
                _shopLine = null;
                PaintShop();
            }
            if (controls) ShowShopControls();
            if (_shopPaintedFor != _state) PaintShop();
            var now = PlayerHubRuntime.Clock.UnixSeconds;
            // Ровно в полночь до следующей — сутки; на доске «23:59:59», а не «24:00:00».
            var left = TimeSpan.FromSeconds(Math.Min(86399, Math.Max(0, HubShop.NextShowcaseAt(now) - now)));
            _shopBoardText.text = "НОВЫЙ\nТОВАР\nЧЕРЕЗ\n" + ((int)left.TotalHours).ToString("00") + ":" + left.Minutes.ToString("00")
                + ":" + left.Seconds.ToString("00");
            // Полночь прошла, пока стоишь у прилавка, — ядро соберёт новую витрину.
            if (_state.Shop.Day < HubShop.DayOf(now) && !_busy && Time.unscaledTime >= _shopRefreshAt)
            {
                _shopRefreshAt = Time.unscaledTime + 5;
                RefreshTimedPage();
            }
        }

        /// <summary>Счёт серебра и «Покажи другое» поверх зала; карточка вещи — над ними.</summary>
        private void ShowShopControls()
        {
            if (_shopControls == null) BuildShopControls();
            _shopControls.SetAsLastSibling();
            if (_shopItem != null) _shopDetail.SetAsLastSibling();
        }

        private void BuildShopControls()
        {
            _shopControls = StageLayer("Shop controls");
            _shopReroll = CityButton(_shopControls, "ПОКАЖИ ДРУГОЕ", (1600 - 380) / 2f, 900 - 24 - 64, 380, 64, AskShopForOther);
            _shopWallet = LoanArt(_shopControls, "Shop wallet", null, new Vector2(646, 396), new Vector2(260, 60), new Color(.08f, .045f, .025f, .95f)).rectTransform;
            var wallet = _shopWallet.GetComponent<Image>();
            wallet.sprite = UiTextures.RoundedRect(64, 64, 26, Color.white);
            wallet.type = Image.Type.Sliced;
            var coin = LoanArt(_shopWallet, "Coin", null, new Vector2(-104, 0), new Vector2(64, 64), Color.white);
            coin.sprite = HubCurrencyArt.Silver;
            coin.preserveAspect = true;
            _shopWalletText = LoanText(_shopWallet, "Balance", "", new Vector2(28, 0), new Vector2(184, 50), 35, Color.white);
            PinToCorner(_shopWallet, 1);
            BuildShopDetail();
            PaintShop();
        }

        /// <summary>Карточка вещи: крупно, вид и редкость, описание, цена и одно главное действие.</summary>
        private void BuildShopDetail()
        {
            _shopDetail = StageLayer("Shop item card");
            var dim = LoanArt(_shopDetail, "Shop item shade", null, Vector2.zero, new Vector2(6000, 6000), new Color(0, 0, 0, .62f));
            dim.raycastTarget = true;
            dim.gameObject.AddComponent<Button>().onClick.AddListener(() => TryShopBack());
            var card = LoanArt(_shopDetail, "Shop item paper", null, new Vector2(0, 10), new Vector2(860, 500), ShopPaper);
            card.sprite = UiTextures.RoundedRect(64, 64, 18, Color.white);
            card.type = Image.Type.Sliced;
            card.raycastTarget = true;
            var rim = card.gameObject.AddComponent<HubOutline>();
            rim.effectColor = new Color(.55f, .38f, .14f);
            rim.effectDistance = new Vector2(3, -3);
            var at = card.rectTransform;
            _shopDetailArt = LoanRect(at, "Shop item preview", new Vector2(-235, 30), new Vector2(300, 300));
            _shopDetailName = NewsFit(LoanText(at, "Shop item name", "", new Vector2(175, 170), new Vector2(440, 64), 44, LoanInk, true));
            _shopDetailKind = NewsFit(LoanText(at, "Shop item kind", "", new Vector2(175, 115), new Vector2(440, 36), 26, ShopMuted));
            _shopDetailAbout = NewsFit(LoanText(at, "Shop item about", "", new Vector2(175, 30), new Vector2(440, 120), 26, LoanInk));
            _shopDetailPrice = LoanText(at, "Shop item price", "", new Vector2(175, -65), new Vector2(440, 48), 34, LoanInk, true);
            _shopDetailPrice.supportRichText = true;
            _shopDetailAction = LoanPaperButton(at, "", new Vector2(175, -165), new Vector2(400, 66), ShopDetailAction, true);
            LoanPaperButton(at, "ЗАКРЫТЬ", new Vector2(-235, -165), new Vector2(240, 58), () => TryShopBack());
            _shopDetail.gameObject.SetActive(false);
        }

        private void PaintShop()
        {
            if (_state == null) return;
            _shopPaintedFor = _state;
            // Новая витрина (полночь) — вчерашняя реплика Дамы к ней уже не относится.
            if (_shopDay != _state.Shop.Day) { _shopDay = _state.Shop.Day; _shopLine = null; }
            var slots = _state.Shop.Slots;
            if (_shopSlots[0] != null)
            {
                for (var i = 0; i < _shopSlots.Length; i++)
                    PaintShopSlot(i, i < slots.Count ? _state.Catalog.FirstOrDefault(item => item.Id == slots[i]) : null);
                _shopPlaqueText.gameObject.SetActive(slots.Count > 0 && slots[0].Length > 0);
                _shopBubbleText.text = (_shopLine ?? ShopGreeting()).Replace(" —", " —");
                _shopBubble.gameObject.SetActive(true);
            }
            if (_shopControls == null) return;
            _shopWalletText.text = HubMoney(_state.Profile.Coins);
            var rerolls = HubShop.DailyRerolls - _state.Shop.Rerolls;
            _shopReroll.gameObject.SetActive(rerolls > 0);
            _shopReroll.GetComponentInChildren<Text>().text = "ПОКАЖИ ДРУГОЕ · " + rerolls;
            PaintShopDetail();
        }

        private void PaintShopSlot(int slot, HubCatalogItem item)
        {
            var root = _shopSlots[slot];
            ClearShopChildren(root);
            if (item == null) return;
            var sold = HubOwned(item.Id);
            var spot = ShopSpot(slot);
            var art = ShopItemArt(root, item, Vector2.zero, Mathf.Min(spot.width, spot.height));
            art.gameObject.AddComponent<CanvasGroup>().alpha = sold ? .45f : 1;

            // Ценник — под местом, в пикселях прилавка; ребёнок места, чтобы перерисовываться вместе с ним.
            var tag = LoanArt(root, "Price tag", null, ShopAt(ShopTag(slot)) - ShopAt(spot), ShopTag(slot).size, ShopPaper);
            tag.sprite = UiTextures.RoundedRect(64, 64, 12, Color.white);
            tag.type = Image.Type.Sliced;
            var rim = tag.gameObject.AddComponent<HubOutline>();
            rim.effectColor = new Color(.48f, .3f, .1f);
            rim.effectDistance = new Vector2(2, -2);
            NewsFit(LoanText(tag.rectTransform, "Name", item.Name, new Vector2(0, 15), new Vector2(186, 28), 22, LoanInk));
            if (sold)
            {
                LoanText(tag.rectTransform, "Sold", "ПРОДАНО", new Vector2(0, -15), new Vector2(186, 30), 26, ShopRed, true);
                return;
            }
            var coin = LoanArt(tag.rectTransform, "Coin", null, new Vector2(-58, -15), new Vector2(28, 28), Color.white);
            coin.sprite = HubCurrencyArt.Silver;
            coin.preserveAspect = true;
            var price = LoanText(tag.rectTransform, "Price", ShopPriceText(item, slot), new Vector2(18, -15), new Vector2(140, 30), 26,
                slot == 0 ? ShopRed : LoanInk, true);
            price.supportRichText = true;
        }

        // Вещи и ценники перерисовываются с каждым снимком профиля; в проверке без Play Mode — сразу.
        private static void ClearShopChildren(Transform root)
        {
            for (var i = root.childCount - 1; i >= 0; i--)
            {
                var child = root.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
        }

        /// <summary>Цена на ценнике: у товара дня — со скидкой и прежней ценой мельче.</summary>
        private static string ShopPriceText(HubCatalogItem item, int slot)
            => slot == 0 ? HubShop.PriceAt(item, slot) + "  <size=18><color=#8A6A44>" + item.Price + "</color></size>" : item.Price.ToString();

        /// <summary>Заглушка вещи до настоящего арта: форма по виду предмета в его цвете.</summary>
        private RectTransform ShopItemArt(Transform parent, HubCatalogItem item, Vector2 at, float size)
        {
            var root = LoanRect(parent, "Item " + item.Id, at, new Vector2(size, size));
            if (!ColorUtility.TryParseHtmlString(item.Color, out var color)) color = new Color(.85f, .72f, .4f);
            Image Shape(string name, Vector2 box, float radius, Color tint, Color edge)
            {
                var image = LoanArt(root, name, null, Vector2.zero, box, tint);
                image.sprite = UiTextures.RoundedRect(128, 128, radius, Color.white);
                image.type = Image.Type.Sliced;
                var outline = image.gameObject.AddComponent<HubOutline>();
                outline.effectColor = edge;
                outline.effectDistance = new Vector2(3, -3);
                return image;
            }
            void Glyph(string text, float fontSize, Color tint)
            {
                var label = LoanText(root, "Glyph", text, Vector2.zero, new Vector2(size, size), Mathf.RoundToInt(fontSize), tint);
                label.font = _font;
                label.fontStyle = FontStyle.Bold;
            }
            var gold = new Color(.86f, .66f, .28f);
            var light = Color.Lerp(color, Color.white, .45f);
            switch (item.Category)
            {
                case "cardback":
                    Shape("Card", new Vector2(size * .62f, size * .9f), 18, color, new Color(.96f, .92f, .82f));
                    Glyph("♦", size * .34f, light);
                    break;
                case "cardface":
                    Shape("Card", new Vector2(size * .62f, size * .9f), 18, color, new Color(.3f, .25f, .2f));
                    Glyph("A\n♠", size * .24f, new Color(.15f, .12f, .1f));
                    break;
                case "table": case "background":
                    Shape("Cloth", new Vector2(size * .98f, size * .66f), 40, color, gold);
                    Glyph("♣", size * .26f, light);
                    break;
                case "frame":
                    Shape("Ring", new Vector2(size * .86f, size * .86f), 64, color, gold);
                    Shape("Inside", new Vector2(size * .58f, size * .58f), 64, new Color(.16f, .1f, .06f), Color.Lerp(color, Color.black, .4f));
                    break;
                default:
                    Shape("Medallion", new Vector2(size * .86f, size * .86f), 64, color, gold);
                    Glyph("♦", size * .3f, light);
                    break;
            }
            return root;
        }

        private string ShopGreeting()
        {
            var slots = _state.Shop.Slots;
            if (_state.Bank.Debt > 0) return "Пока долг Тузяре не закрыт, продаю только на свободные деньги.";
            if (!slots.Any(id => id.Length > 0 && !HubOwned(id))) return "Всё раскупили! Новое привезут в полночь.";
            var day = slots.Count > 0 ? _state.Catalog.FirstOrDefault(item => item.Id == slots[0]) : null;
            if (day != null && !HubOwned(day.Id)) return "Сегодня на подиуме — «" + day.Name + "». Скидка " + HubShop.DayDiscountPercent + "%!";
            return "Выбирай, дорогуша. Новое — каждую полночь.";
        }

        private void TalkToShopQueen()
        {
            if (!ShopInside || ShopCardShown) return;
            _shopLine = ShopTalk[_shopTalk++ % ShopTalk.Length];
            PaintShop();
        }

        private void OpenShopSlot(int slot)
        {
            if (!ShopInside || _busy || _state == null || slot < 0 || slot >= _state.Shop.Slots.Count || _state.Shop.Slots[slot].Length == 0) return;
            if (_shopControls == null) BuildShopControls();
            _shopItem = _state.Shop.Slots[slot];
            PaintShop();
            ShowShopControls();
        }

        private void PaintShopDetail()
        {
            var slot = _shopItem == null ? -1 : _state.Shop.Slots.IndexOf(_shopItem);
            var item = slot < 0 ? null : _state.Catalog.FirstOrDefault(entry => entry.Id == _shopItem);
            if (item == null) _shopItem = null;
            _shopDetail.gameObject.SetActive(item != null && ShopInside);
            if (item == null) return;
            ClearShopChildren(_shopDetailArt);
            ShopItemArt(_shopDetailArt, item, Vector2.zero, 280);
            _shopDetailName.text = item.Name;
            _shopDetailKind.text = HubCategoryName(item.Category) + " · " + HubShop.RarityName(item.Rarity) + (slot == 0 ? " · товар дня" : "");
            _shopDetailAbout.text = item.Description;
            var owned = HubOwned(item.Id);
            var equipped = _state.Equipped.Any(entry => entry.ItemId == item.Id);
            var price = HubShop.PriceAt(item, slot);
            _shopDetailPrice.text = owned ? "Уже твоя" : price + " коинов"
                + (slot == 0 ? "  <size=24><color=#8A6A44>вместо " + item.Price + "</color></size>" : "");
            _shopDetailPrice.color = owned ? ShopMuted : slot == 0 ? ShopRed : LoanInk;
            _shopDetailAction.GetComponentInChildren<Text>().text = owned ? equipped ? "НАДЕТО" : "НАДЕТЬ" : "КУПИТЬ ЗА " + price;
            _shopDetailAction.interactable = !(owned && equipped);
        }

        private void ShopDetailAction()
        {
            if (_shopItem == null || _busy) return;
            if (HubOwned(_shopItem)) WearShopItem(_shopItem);
            else BuyShopItem(_shopItem);
        }

        /// <summary>Действие у прилавка. Отвечает Дама в пузыре — и на успех, и на отказ; общая плашка
        /// сверху ушла бы под пузырь, поэтому без неё (как подарок Шестёрки и гадалка).</summary>
        private async Task<bool> ShopRun(string action, string target, string reply)
        {
            if (_busy) return false;
            SetBusy(true);
            var result = await PlayerHubRuntime.ExecuteAsync(new HubCommand { Action = action, Target = target });
            if (this == null) return false;
            SetBusy(false);
            if (result.Success) { _state = result.Snapshot; Render(); }
            _shopLine = result.Success ? reply : PlayerHubRuntime.ErrorText(result);
            PaintShop();
            return result.Success;
        }

        private async void WearShopItem(string id) => await ShopRun("equip", id, "Тебе идёт!");

        private async void BuyShopItem(string id) => await ShopRun("shop_buy", id, "Носи на здоровье! Надеть можно прямо сейчас.");

        /// <summary>«Покажи другое»: позже — после ролика рекламы, пока — сразу.</summary>
        private async void AskShopForOther()
        {
            if (!ShopInside || ShopCardShown) return;
            await ShopRun("shop_reroll", "", "Есть кое-что на складе… Вот, смотри.");
        }
    }
}
