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
    /// Подарок Шестёрки (2026-10-07) — коробка на полке в его комнате. Раз в 12 часов в ней серебряные тун-тун коины
    /// (<see cref="HubGift"/>, команда `gift_open` в сервисе; отсчёт — от открытия). Арт — три спрайта одного масштаба
    /// (`UI/Hub/six-gift-closed`, `six-gift-open`, `six-gift-lid`; лист `screens/references/city/six-gift-sheet.png`,
    /// вырезаны `scripts/six-house/cut_gift.py`) — на крупном плане. На полке готовый подарок — нарисованная в комнате
    /// коробка (спрайт сверху не сходится с ракурсом комнаты — решение автора), открытый — заплатка той же комнаты с
    /// открытой коробкой (`UI/Hub/six-gift-shelf-open`, `scripts/six-house/shelf_patch.py`); готов — слабое сияние,
    /// искорки и красный «1» (постоянный золотой контур автор убрал), на улице — кружок у таблички «Дом Шестёрки» (подарок и
    /// награды заданий). Нажатие на коробку — крупный план: готовая покачивается; «ОТКРЫТЬ» — она трясётся, крышка
    /// слетает со вспышкой, монеты вылетают веером и по одной летят в счёт в углу, счёт растёт. Потом — открытая коробка
    /// и таймер до следующего. Не готов — сразу открытая коробка и таймер.
    /// </summary>
    public sealed partial class PlayerHubController
    {
        // Нарисованная коробка на полке (пиксели картинки комнаты): середина и место «1» у её угла; левый верхний угол
        // заплатки с открытой коробкой и крышкой рядом (PATCH в shelf_patch.py).
        private const float GiftX = 1780, GiftY = 108, GiftBadgeX = 1832, GiftBadgeY = 66, GiftPatchX = 1600, GiftPatchY = 0;
        private const int GiftCoinCount = 14;
        // Крупный план (от середины сцены, y вверх): где коробка стоит дном, её масштаб (спрайт 481 px → ~340),
        // высота нутра над дном, откуда летят монеты, и счёт в углу.
        private static readonly Vector2 GiftFloor = new Vector2(0, -190),
            GiftWalletAt = new Vector2(646, 396), GiftWalletSize = new Vector2(260, 60);
        private const float GiftScale = .7f, GiftMouth = 250, GiftCoinSize = 64, GiftWalletCoinX = -118;
        // Открытие, секунды от ответа сервиса: тряска, крышка со вспышкой, веер монет, полёт в счёт по одной.
        private const float GiftFade = .2f, GiftShake = .45f, GiftLidFly = .5f, GiftFan = .45f, GiftFanStagger = .02f,
            GiftFlyStart = 1.15f, GiftFlyStagger = .06f, GiftFly = .5f, GiftSettle = .35f;
        private static readonly Color GiftWarm = new Color(1f, .82f, .45f), GiftSilver = new Color(.93f, .95f, .99f);
        private const string SixGiftReadyLine = "Подарок на полке ждёт — открывай!";

        private RectTransform _giftLayer, _giftBox, _giftLid, _giftWallet, _giftAmount;
        private CanvasGroup _giftGroup, _giftLidGroup;
        private Image _giftRays, _giftFlash, _giftGlowInside, _giftClosed, _giftOpen;
        private Text _giftStatus, _giftWalletText;
        private Button _giftOpenButton, _giftTestButton;
        private Vector2 _giftLidRest;
        private readonly RectTransform[] _giftCoins = new RectTransform[GiftCoinCount];
        private readonly Vector2[] _giftCoinPeaks = new Vector2[GiftCoinCount];
        private bool _giftShown, _giftEscapeHeld;
        // −1 — не открываем; иначе секунды анимации открытия.
        private float _giftFade, _giftTime = -1, _giftClock, _giftWalletPulse;
        private int _giftWalletFrom, _giftWalletTo, _giftCoinsLanded, _giftShownCoins;

        // На полке и на улице.
        private RectTransform _giftShelfBadge, _giftStreetBadge;
        private Text _giftStreetCount;
        private Image _giftShelfGlow, _giftShelfOpen;
        private Image[] _giftSparkles;

        /// <summary>Крупный план подарка открыт или ещё гаснет: город под ним не слушает нажатия и клавиши.</summary>
        private bool GiftShown => _giftShown || _giftFade > 0;

        private long GiftNow => PlayerHubRuntime.Clock.UnixSeconds;
        private bool GiftReady() => _state?.Gift != null && GiftNow >= _state.Gift.ReadyAt;

        private static Sprite GiftSprite(string state) => Resources.Load<Sprite>("UI/Hub/six-gift-" + state);

        /// <summary>
        /// Заплатка с открытой коробкой (пока подарок ждёт), сияние, искорки и «1» — детьми этажа комнаты
        /// (зовётся из AddSixRoom).
        /// </summary>
        private void AddSixGiftShelf(RectTransform floor, Func<float, float, Vector2> at, Vector2 unit)
        {
            RectTransform Placed(string name, Vector2 pivot, Vector2 position, Vector2 size)
            {
                var rect = MakeRect(floor, name, 0, 0, 0, 0);
                rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
                rect.pivot = pivot;
                rect.anchoredPosition = position;
                rect.sizeDelta = size;
                return rect;
            }
            // Открытая коробка — кусок той же комнаты, пиксель в пиксель: левым верхним углом в свою точку.
            var patch = GiftSprite("shelf-open");
            if (patch != null)
            {
                _giftShelfOpen = Placed("Gift on shelf open", new Vector2(0, 1), at(GiftPatchX, GiftPatchY), patch.rect.size * unit)
                    .gameObject.AddComponent<Image>();
                _giftShelfOpen.sprite = patch;
                _giftShelfOpen.raycastTarget = false;
                _giftShelfOpen.gameObject.SetActive(false);
            }
            // Свет — поверх коробки, поэтому слабый и широкий: не засвечивает её.
            _giftShelfGlow = Placed("Gift glow", new Vector2(.5f, .5f), at(GiftX, GiftY), new Vector2(320 * unit.x, 260 * unit.y))
                .gameObject.AddComponent<Image>();
            _giftShelfGlow.sprite = UiTextures.RadialGlow(256, Color.white);
            _giftShelfGlow.color = new Color(GiftWarm.r, GiftWarm.g, GiftWarm.b, 0);
            _giftShelfGlow.raycastTarget = false;
            // Искорки вокруг коробки: смещения от её середины, у каждой своя фаза мерцания.
            var spots = new[] { new Vector2(-66, -38), new Vector2(62, -30), new Vector2(68, 32), new Vector2(-64, 36) };
            _giftSparkles = new Image[spots.Length];
            for (var i = 0; i < spots.Length; i++)
            {
                var spark = Placed("Gift sparkle " + i, new Vector2(.5f, .5f), at(GiftX + spots[i].x, GiftY + spots[i].y),
                    new Vector2(26 * unit.x, 26 * unit.y)).gameObject.AddComponent<Image>();
                spark.sprite = UiTextures.RadialGlow(64, Color.white);
                spark.color = new Color(1, .97f, .85f, 0);
                spark.raycastTarget = false;
                _giftSparkles[i] = spark;
            }
            _giftShelfBadge = Placed("Gift badge", new Vector2(.5f, .5f), at(GiftBadgeX, GiftBadgeY), Vector2.one * 40 * unit.y);
            RedDot(_giftShelfBadge, Mathf.RoundToInt(26 * unit.y)).text = "1";
            _giftShelfBadge.gameObject.SetActive(false);
        }

        /// <summary>Красный кружок с белой каймой и числом (как у «Заданий»); возвращает надпись.</summary>
        private Text RedDot(RectTransform rect, int fontSize)
        {
            var circle = rect.gameObject.AddComponent<Image>();
            circle.sprite = UiTextures.Circle(64, Color.white);
            circle.color = SixBadgeRed;
            circle.raycastTarget = false;
            var rim = rect.gameObject.AddComponent<HubOutline>();
            rim.effectColor = Color.white;
            rim.effectDistance = new Vector2(2, -2);
            var count = MakeRect(rect, "Count", 0, 0, 0, 0).gameObject.AddComponent<Text>();
            Stretch(count.rectTransform);
            count.font = Resources.Load<Font>("Fonts/SignNarrow") ?? _font;
            count.fontSize = fontSize;
            count.alignment = TextAnchor.MiddleCenter;
            count.color = Color.white;
            count.raycastTarget = false;
            return count;
        }

        /// <summary>Нажатие на коробку на полке — крупный план подарка.</summary>
        private void ShowSixGift()
        {
            if (_page != "home" || _cityLevel != SixLevel || _diveTarget >= 0 || _visitPhase != 0 || _mapOpen || QuestsShown || BankShown || _state == null) return;
            if (_giftLayer == null) BuildSixGift();
            _giftShown = true;
            _giftTime = -1;
            _giftShownCoins = _state.Profile.Coins;
            _giftLayer.gameObject.SetActive(true);
            _giftLayer.SetAsLastSibling();
            if (_sixBubble != null) _sixBubble.gameObject.SetActive(false);
            _sixBubbleTime = -1;
            PaintSixGift(0);
        }

        private void CloseSixGift()
        {
            // Пока монеты летят, крупный план не закрывается: счёт должен дорасти.
            if (_giftTime >= 0) return;
            _giftShown = false;
        }

        /// <summary>«ОТКРЫТЬ» или нажатие на готовую коробку: сервис начисляет коины, потом — анимация.</summary>
        private async Task OpenSixGiftAsync()
        {
            if (!_giftShown || _giftTime >= 0 || _busy || !GiftReady()) return;
            var before = _state.Profile.Coins;
            SetBusy(true);
            var result = await PlayerHubRuntime.ExecuteAsync(new HubCommand { Action = "gift_open" });
            if (this == null) return;
            SetBusy(false);
            if (!result.Success)
            {
                Toast(PlayerHubRuntime.ErrorText(result));
                return;
            }
            _state = result.Snapshot;
            Render();
            _giftWalletFrom = before;
            _giftWalletTo = _state.Profile.Coins;
            _giftCoinsLanded = 0;
            _giftTime = 0;
            _giftAmount.GetComponent<Text>().text = "+" + HubMoney(_state.Gift.LastCoins);
            var random = new System.Random(_state.Gift.Opened);
            for (var i = 0; i < GiftCoinCount; i++)
            {
                var spread = (i + .5f) / GiftCoinCount * 2 - 1;
                _giftCoinPeaks[i] = new Vector2(spread * 300 + (float)(random.NextDouble() - .5) * 50,
                    GiftFloor.y + 360 + (float)random.NextDouble() * 110);
            }
        }

        private void BuildSixGift()
        {
            _giftLayer = StageLayer("Six gift");
            _giftGroup = _giftLayer.gameObject.AddComponent<CanvasGroup>();
            var font = Resources.Load<Font>("Fonts/SignNarrow") ?? _font;
            RectTransform Placed(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
            {
                var rect = MakeRect(parent, name, 0, 0, 0, 0);
                rect.anchorMin = rect.anchorMax = anchor;
                rect.pivot = pivot;
                rect.anchoredPosition = position;
                rect.sizeDelta = size;
                return rect;
            }
            var middle = new Vector2(.5f, .5f);
            RectTransform Centered(Transform parent, string name, Vector2 position, Vector2 size)
                => Placed(parent, name, middle, middle, position, size);
            Image Shape(Transform parent, string name, Vector2 size, Color color, float radius)
            {
                var image = Centered(parent, name, Vector2.zero, size).gameObject.AddComponent<Image>();
                image.sprite = UiTextures.RoundedRect(64, 64, Mathf.Min(31, radius), Color.white);
                image.type = Image.Type.Sliced;
                image.color = color;
                image.raycastTarget = false;
                return image;
            }
            Image Glow(Transform parent, string name, Vector2 position, Vector2 size, Color color)
            {
                var image = Centered(parent, name, position, size).gameObject.AddComponent<Image>();
                image.sprite = UiTextures.RadialGlow(256, Color.white);
                image.color = color;
                image.raycastTarget = false;
                return image;
            }
            Text Line(Transform parent, string name, Vector2 position, Vector2 size, int fontSize, Color color)
            {
                var text = Centered(parent, name, position, size).gameObject.AddComponent<Text>();
                text.font = font;
                text.fontSize = fontSize;
                text.color = color;
                text.alignment = TextAnchor.MiddleCenter;
                // Без переполнения по высоте Unity прячет строку целиком, если она чуть выше рамки.
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                text.verticalOverflow = VerticalWrapMode.Overflow;
                text.raycastTarget = false;
                var outline = text.gameObject.AddComponent<HubOutline>();
                outline.effectColor = new Color(0, 0, 0, .7f);
                outline.effectDistance = new Vector2(2, -2);
                return text;
            }

            // Затемнение шире сцены (вытянутый экран) ловит нажатия: город под ним стоит; нажатие мимо — закрыть.
            var dim = MakeRect(_giftLayer, "Gift dim", -1400, -700, 4400, 2300).gameObject.AddComponent<Image>();
            dim.color = new Color(.02f, .01f, .03f, .88f);
            var dimButton = dim.gameObject.AddComponent<Button>();
            dimButton.transition = Selectable.Transition.None;
            dimButton.onClick.AddListener(CloseSixGift);

            _giftRays = Glow(_giftLayer, "Gift light", GiftFloor + new Vector2(0, 220), new Vector2(860, 680), GiftWarm);
            Glow(_giftLayer, "Gift shadow", GiftFloor + new Vector2(0, 8), new Vector2(460, 80), new Color(0, 0, 0, .75f));
            var title = Line(_giftLayer, "Gift title", new Vector2(0, 345), new Vector2(900, 80), 54, SignGoldTop);
            title.text = "ПОДАРОК ОТ ШЕСТЁРКИ";
            var look = title.gameObject.AddComponent<HubSignText>();
            look.Top = SignGoldTop;
            look.Bottom = SignGoldBottom;

            // Коробка стоит дном в одной точке: закрытая и открытая сменяют друг друга, крышка лежит там же, где
            // на закрытой (ширина у них одна). Невидимая подложка ловит нажатие; трясётся всё вместе — от дна.
            var closed = GiftSprite("closed");
            var open = GiftSprite("open");
            var lid = GiftSprite("lid");
            var bottom = new Vector2(.5f, 0);
            _giftBox = Placed(_giftLayer, "Gift box", middle, bottom, GiftFloor, closed.rect.size * GiftScale);
            var hit = _giftBox.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            var boxButton = _giftBox.gameObject.AddComponent<Button>();
            boxButton.transition = Selectable.Transition.None;
            boxButton.onClick.AddListener(() => { if (GiftReady()) _ = OpenSixGiftAsync(); });
            Image Art(string name, Sprite sprite, Vector2 pivot, Vector2 position)
            {
                var image = Placed(_giftBox, name, bottom, pivot, position, sprite.rect.size * GiftScale).gameObject.AddComponent<Image>();
                image.sprite = sprite;
                image.raycastTarget = false;
                return image;
            }
            _giftOpen = Art("Open box", open, bottom, Vector2.zero);
            _giftGlowInside = Glow(_giftBox, "Inside light", Vector2.zero, new Vector2(420, 220), GiftWarm);
            var inside = _giftGlowInside.rectTransform;
            inside.anchorMin = inside.anchorMax = bottom;
            inside.anchoredPosition = new Vector2(0, GiftMouth);
            _giftClosed = Art("Closed box", closed, bottom, Vector2.zero);
            _giftLidRest = new Vector2(0, (closed.rect.height - lid.rect.height / 2) * GiftScale);
            _giftLid = Art("Lid", lid, middle, _giftLidRest).rectTransform;
            _giftLidGroup = _giftLid.gameObject.AddComponent<CanvasGroup>();
            _giftLidGroup.blocksRaycasts = false;

            var mouth = GiftFloor + new Vector2(0, GiftMouth);
            _giftFlash = Glow(_giftLayer, "Gift flash", mouth, new Vector2(460, 460), new Color(1, .96f, .8f, 0));
            _giftAmount = Line(_giftLayer, "Gift amount", GiftFloor + new Vector2(0, 410), new Vector2(400, 90), 72, GiftSilver).rectTransform;
            _giftStatus = Line(_giftLayer, "Gift timer", new Vector2(0, -255), new Vector2(1100, 60), 40, new Color(1, .97f, .9f));
            _giftOpenButton = CityButton(_giftLayer, "ОТКРЫТЬ", 800 - 140, 450 + 230, 280, 76, () => _ = OpenSixGiftAsync());
            PinToCorner((RectTransform)CityButton(_giftLayer, "НАЗАД", 24, 24, 170, 56, CloseSixGift).transform, -1);
            // ТЕСТ (2026-10-07, по просьбе автора): подарок готов сразу — только в редакторе и отладочной сборке.
            // Убрать вместе с командой `gift_debug_ready` в сервисе.
            _giftTestButton = MakeButton(_giftLayer, "Тест: подарок сейчас", 800 - 160, 450 + 300, 320, 50,
                () => Send("gift_debug_ready"));

            // Счёт в углу — куда летят монеты (на главной верхней панели нет).
            _giftWallet = Centered(_giftLayer, "Gift wallet", GiftWalletAt, GiftWalletSize);
            Shape(_giftWallet, "Pill", GiftWalletSize, new Color(.06f, .04f, .03f, .92f), 30);
            var coin = Centered(_giftWallet, "Coin", new Vector2(GiftWalletCoinX, 0), Vector2.one * 76).gameObject.AddComponent<Image>();
            coin.sprite = HubCurrencyArt.Silver;
            coin.preserveAspect = true;
            coin.raycastTarget = false;
            _giftWalletText = Line(_giftWallet, "Amount", new Vector2(24, 0), new Vector2(190, 50), 36, Color.white);
            _giftWalletText.alignment = TextAnchor.MiddleLeft;
            PinToCorner(_giftWallet, 1);

            for (var i = 0; i < GiftCoinCount; i++)
            {
                var flying = Centered(_giftLayer, "Coin " + i, mouth, Vector2.one * GiftCoinSize);
                var image = flying.gameObject.AddComponent<Image>();
                image.sprite = HubCurrencyArt.Silver;
                image.preserveAspect = true;
                image.raycastTarget = false;
                flying.gameObject.SetActive(false);
                _giftCoins[i] = flying;
            }
            _giftLayer.gameObject.SetActive(false);
        }

        /// <summary>
        /// Каждый кадр: коробка, сияние и кружки на полке и на улице, крупный план — проявление, покачивание готовой
        /// коробки, анимация открытия, таймер; Esc закрывает крупный план, но не выводит из дома.
        /// </summary>
        private void UpdateSixGift(float deltaTime, bool typing)
        {
            _giftClock += deltaTime;
            var ready = GiftReady();
            UpdateGiftMarks(ready);
            if (_giftLayer == null) return;
            if (_giftShown && (_page != "home" || _cityLevel != SixLevel)) { _giftShown = false; _giftTime = -1; }
            var keyboard = typing ? null : Keyboard.current;
            var escape = keyboard != null && keyboard.escapeKey.isPressed;
            if (_giftShown && escape && !_giftEscapeHeld)
            {
                // То же удержание Esc после закрытия не должно выводить игрока из дома.
                _cityVerticalHeld = 1;
                CloseSixGift();
            }
            _giftEscapeHeld = escape;
            _giftFade = Mathf.MoveTowards(_giftFade, _giftShown ? 1 : 0, deltaTime / GiftFade);
            _giftGroup.alpha = Mathf.SmoothStep(0, 1, _giftFade);
            _giftGroup.blocksRaycasts = _giftShown;
            if (!_giftShown && _giftFade <= 0)
            {
                if (_giftLayer.gameObject.activeSelf) _giftLayer.gameObject.SetActive(false);
                return;
            }
            if (_giftTime >= 0) _giftTime += deltaTime;
            PaintSixGift(deltaTime);
        }

        /// <summary>
        /// На полке: нарисованная коробка, пока подарок готов (со слабым сиянием, искорками и «1»), заплатка
        /// с открытой — пока ждёт; на улице — кружок у таблички дома.
        /// </summary>
        private void UpdateGiftMarks(bool ready)
        {
            if (_giftShelfGlow != null)
            {
                if (_giftShelfOpen != null && _giftShelfOpen.gameObject.activeSelf == ready) _giftShelfOpen.gameObject.SetActive(!ready);
                var pulse = .5f + .5f * Mathf.Sin(_giftClock * 3f);
                _giftShelfGlow.color = new Color(GiftWarm.r, GiftWarm.g, GiftWarm.b, ready ? .05f + .07f * pulse : 0);
                for (var i = 0; i < _giftSparkles.Length; i++)
                {
                    var twinkle = Mathf.Pow(Mathf.Max(0, Mathf.Sin(_giftClock * 2.6f + i * 1.7f)), 3);
                    _giftSparkles[i].color = new Color(1, .97f, .85f, ready ? twinkle : 0);
                    _giftSparkles[i].rectTransform.localScale = Vector3.one * (.6f + .6f * twinkle);
                }
                if (_giftShelfBadge.gameObject.activeSelf != ready) _giftShelfBadge.gameObject.SetActive(ready);
            }
            if (_giftStreetBadge == null)
            {
                if (!_placePages.TryGetValue("card-city-old-town", out var street)) return;
                // Табличка дома — нарисованная, на кронштейне; кружок — на её правом верхнем углу (над строкой «ДОМ»).
                if (!(street.Page.Find("Sign ДОМ") is RectTransform sign)) return;
                _giftStreetBadge = MakeRect(sign, "Six house badge", 0, 0, 0, 0);
                _giftStreetBadge.anchorMin = _giftStreetBadge.anchorMax = Vector2.one;
                _giftStreetBadge.pivot = new Vector2(.5f, .5f);
                _giftStreetBadge.anchoredPosition = new Vector2(13, 5);
                _giftStreetBadge.sizeDelta = Vector2.one * 30;
                _giftStreetBadge.localRotation = Quaternion.Inverse(sign.localRotation);
                _giftStreetCount = RedDot(_giftStreetBadge, 20);
                // Маленькая красная печать на углу вывески, с тёплым краем под её золотую рамку.
                _giftStreetBadge.GetComponent<Image>().color = new Color(.73f, .12f, .10f);
                _giftStreetBadge.GetComponent<Outline>().effectColor = new Color(1f, .9f, .67f);
            }
            // Что ждёт в доме: подарок и награды выполненных заданий.
            var waiting = (ready ? 1 : 0) + ReadyQuests();
            if (_giftStreetBadge.gameObject.activeSelf != waiting > 0) _giftStreetBadge.gameObject.SetActive(waiting > 0);
            var count = waiting > 9 ? "9+" : waiting.ToString();
            if (_giftStreetCount.text != count) _giftStreetCount.text = count;
        }

        /// <summary>Кадр крупного плана: готовая коробка, анимация открытия или открытая коробка с таймером.</summary>
        private void PaintSixGift(float deltaTime)
        {
            // Последняя монета долетела, «+50» погасло — открытие закончено, дальше открытая коробка.
            var flightsEnd = GiftFlyStart + (GiftCoinCount - 1) * GiftFlyStagger + GiftFly;
            if (_giftTime >= flightsEnd + GiftSettle) _giftTime = -1;
            var ready = GiftReady();
            var t = _giftTime;
            var opening = t >= 0;
            var idle = !opening && ready;
            // Готовая коробка покачивается раз в полторы секунды; при открытии трясётся всё сильнее — от дна.
            var angle = 0f;
            if (idle)
            {
                var beat = _giftClock % 1.6f;
                angle = beat < .5f ? Mathf.Sin(beat / .5f * Mathf.PI * 3) * 4 * (1 - beat / .5f) : 0;
            }
            else if (opening && t < GiftShake) angle = Mathf.Sin(t * 55) * 6 * (t / GiftShake);
            _giftBox.localRotation = Quaternion.Euler(0, 0, angle);
            _giftBox.localScale = Vector3.one * (opening && t < GiftShake ? 1 + .05f * (t / GiftShake) : 1);

            // Закрытая — пока готова и пока трясётся; дальше открытая, а крышка слетает вверх и гаснет.
            var open = !idle && (!opening || t >= GiftShake);
            _giftClosed.gameObject.SetActive(!open);
            _giftOpen.gameObject.SetActive(open);
            var flying = opening && t >= GiftShake && t < GiftShake + GiftLidFly;
            _giftLid.gameObject.SetActive(flying);
            if (flying)
            {
                var u = Mathf.Clamp01((t - GiftShake) / GiftLidFly);
                _giftLid.anchoredPosition = _giftLidRest + new Vector2(150 * u, 480 * u - 120 * u * u);
                _giftLid.localRotation = Quaternion.Euler(0, 0, -40 * u);
                _giftLidGroup.alpha = 1 - u * u;
            }
            var burst = opening ? Mathf.Clamp01((t - GiftShake) / .5f) : 1;
            _giftGlowInside.color = new Color(GiftWarm.r, GiftWarm.g, GiftWarm.b, open ? (opening ? 1 - .7f * burst : .3f) : 0);
            _giftFlash.color = new Color(1, .96f, .8f, opening && t >= GiftShake ? 1 - burst : 0);
            _giftFlash.rectTransform.localScale = Vector3.one * (.4f + 1.8f * Mathf.Sqrt(burst));
            var pulse = .5f + .5f * Mathf.Sin(_giftClock * 2.4f);
            _giftRays.color = new Color(GiftWarm.r, GiftWarm.g, GiftWarm.b, idle ? .35f + .2f * pulse : opening ? .6f * (1 - burst) + .3f : .18f);

            // «+50» выскакивает над коробкой и гаснет, когда монеты долетели.
            var amountAt = GiftShake + .15f;
            _giftAmount.gameObject.SetActive(opening && t >= amountAt);
            if (opening && t >= amountAt)
            {
                var pop = Mathf.Clamp01((t - amountAt) / .25f);
                _giftAmount.localScale = Vector3.one * (pop < .7f ? pop / .7f * 1.2f : 1.2f - .2f * (pop - .7f) / .3f);
                var text = _giftAmount.GetComponent<Text>();
                text.color = new Color(GiftSilver.r, GiftSilver.g, GiftSilver.b, 1 - Mathf.Clamp01((t - flightsEnd) / GiftSettle));
            }

            // Монеты: веер из коробки, потом по одной — в счёт.
            var wallet = _giftWallet.anchoredPosition + new Vector2(GiftWalletCoinX, 0);
            var mouth = GiftFloor + new Vector2(0, GiftMouth);
            var landed = 0;
            for (var i = 0; i < GiftCoinCount; i++)
            {
                var coin = _giftCoins[i];
                var start = GiftShake + .05f + i * GiftFanStagger;
                var flyAt = GiftFlyStart + i * GiftFlyStagger;
                var fly = opening ? Mathf.Clamp01((t - flyAt) / GiftFly) : 0;
                if (opening && fly >= 1) landed++;
                var visible = opening && t >= start && fly < 1;
                if (coin.gameObject.activeSelf != visible) coin.gameObject.SetActive(visible);
                if (!visible) continue;
                var fan = Mathf.Clamp01((t - start) / GiftFan);
                var eased = 1 - (1 - fan) * (1 - fan);
                var peak = _giftCoinPeaks[i];
                var at = Vector2.Lerp(mouth, peak, eased) + Vector2.up * Mathf.Sin(fan * Mathf.PI) * 70;
                if (fly > 0)
                {
                    var v = fly * fly;
                    var control = new Vector2((peak.x + wallet.x) / 2, Mathf.Max(peak.y, wallet.y) + 140);
                    at = (1 - v) * (1 - v) * peak + 2 * (1 - v) * v * control + v * v * wallet;
                }
                coin.anchoredPosition = at;
                // Монета крутится: сжимается к ребру и обратно; к счёту чуть уменьшается.
                var spin = .3f + .7f * Mathf.Abs(Mathf.Cos(t * 9 + i * .8f));
                var size = 1 - .3f * fly;
                coin.localScale = new Vector3(spin * size, size, 1);
            }
            if (opening && landed > _giftCoinsLanded) _giftWalletPulse = 1;
            _giftCoinsLanded = opening ? landed : 0;
            _giftWalletPulse = Mathf.MoveTowards(_giftWalletPulse, 0, deltaTime / .18f);
            _giftWallet.localScale = Vector3.one * (1 + .14f * _giftWalletPulse);
            var shown = opening
                ? _giftWalletFrom + (int)((long)(_giftWalletTo - _giftWalletFrom) * _giftCoinsLanded / GiftCoinCount)
                : _state.Profile.Coins;
            if (shown != _giftShownCoins || string.IsNullOrEmpty(_giftWalletText.text))
            {
                _giftShownCoins = shown;
                _giftWalletText.text = HubMoney(shown);
            }

            // Под коробкой: «ОТКРЫТЬ» у готовой, таймер у открытой.
            var waiting = !opening && !ready;
            _giftOpenButton.gameObject.SetActive(idle);
            _giftStatus.gameObject.SetActive(waiting);
            _giftTestButton.gameObject.SetActive(waiting && (Application.isEditor || Debug.isDebugBuild));
            if (waiting)
            {
                var left = TimeSpan.FromSeconds(Math.Max(0, _state.Gift.ReadyAt - GiftNow));
                var line = "Следующий подарок через " + ((int)left.TotalHours).ToString("00") + ":" + left.Minutes.ToString("00")
                    + ":" + left.Seconds.ToString("00");
                if (_giftStatus.text != line) _giftStatus.text = line;
            }
        }
    }
}
