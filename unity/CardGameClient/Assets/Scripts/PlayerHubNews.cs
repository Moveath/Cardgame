using System;
using CardGame.Core.Meta;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>
    /// Киоск «Карточный вестник» на площади (2026-10-09, решение автора): нажатие на киоск — подход к нему и крупный план
    /// окошка (`UI/Hub/news-kiosk`, исходник `screens/references/city/news-kiosk-v2.png`): за прилавком Двойка червей
    /// протягивает газету, на табличке — «КАРТОЧНЫЙ ВЕСТНИК». Газета (`UI/Hub/news-paper`, `news-paper-sheet.png`) —
    /// во весь экран: главная новость с картинкой места и кнопкой «Посмотреть», хроника из дел игрока (долг, предсказание,
    /// подарок, поручения), слух дня из лора. Выпуски — таблица <see cref="NewsIssues"/>: новый выпуск — с обновлением
    /// игры; прочитанный — команда `news_read` (HubSnapshot.NewsSeen), пока новый не прочитан — красный кружок у вывески
    /// «НОВОСТИ». Нарезка картинок — `scripts/news/cut_news.py`.
    /// </summary>
    public sealed partial class PlayerHubController
    {
        // Утверждённая картинка киоска 16:9 (её пиксели): размер, поле таблички над окном, газета в руке Двойки, облачко
        // у его головы. Игровая шире по бокам до 21:9 (`scripts/six-house/widen_room.py kiosk`) поровну с обеих сторон,
        // поэтому точки считаются от середины и не меняются; ширину берём у самой картинки.
        private const float NewsKioskWidth = 1672, NewsKioskHeight = 941;
        private static readonly Rect NewsSignField = new Rect(470, 60, 730, 88);
        private static readonly Rect NewsHandPaper = new Rect(522, 500, 300, 255);
        private static readonly Rect NewsBubble = new Rect(1000, 250, 330, 120);
        // Газета — разворот (пиксели `news-paper`, 1426×979): шапка через обе страницы, слева рамка главной новости,
        // справа рамки хроники и слухов. Одна страница на экране была мелкой — автор попросил две (2026-10-09).
        private const float NewsPaperWidth = 1426, NewsPaperHeight = 979, NewsPaperScreenHeight = 860;
        private static readonly Color NewsInk = new Color(.2f, .12f, .06f), NewsMuted = new Color(.38f, .27f, .16f);

        private sealed class NewsIssue
        {
            public int Number;
            public string Headline, Story, Picture, PlaceId;
        }

        // Выпуски — новые дописывать в конец; в газете — последний. Новость — то, что игрок может пойти и попробовать.
        private static readonly NewsIssue[] NewsIssues =
        {
            new NewsIssue
            {
                Number = 1, Picture = "fortune-tent", PlaceId = "fortune",
                Headline = "ПИКОВАЯ ДАМА РАСКИНУЛА ШАТЁР",
                Story = "В переулке Старого города открылся шатёр гадалки. Раз в сутки Пиковая дама открывает одну карту "
                    + "из шести — и судьба на ближайшие партии меняется. Говорят, её карты не врут. Почти.",
            },
        };

        // Слух дня — по номеру дня; лор по капле (IDEAS.md, «Лор и сюжет»).
        private static readonly string[] NewsRumors =
        {
            "Старожилы шепчутся: Туз пик пропал в ночь Великой партии. Ерунда, наверно.",
            "Говорят, сейф в Банке Тузяры не закрывается никогда. Некому оттуда брать.",
            "Вальты Бубен — родные братья, и оба должны банку. Кто кому должен — не спрашивайте.",
            "Шестёрка пик штопает свой шарф сам. Откуда шарф — не говорит.",
            "Король пик каждое утро вычёркивает имя в какой-то тетради. Пока — не ваше.",
            "Пиковая дама и Шестёрка пик — одной масти. Совпадение? В этом городе — нет.",
        };

        private RectTransform _newsLayer, _newsKiosk, _newsPaperLayer, _newsPaper, _newsStreetBadge;
        private Image _newsHandGlow, _newsPicture;
        private Text _newsBubbleText, _newsIssueLine, _newsHeadline, _newsStory, _newsChronicle, _newsRumor;
        private Button _newsLookButton;
        private RectTransform _newsBackButton;
        private CanvasGroup _newsGroup, _newsPaperGroup;
        private bool _newsPaperShown, _newsEscapeHeld;
        private float _newsClock, _newsFade, _newsPaperFade;

        private static NewsIssue CurrentNews => NewsIssues[NewsIssues.Length - 1];
        // Разворот — почти во весь экран: по высоте 860 из 900, на узком экране — по ширине.
        private float NewsPaperScale => Mathf.Min(NewsPaperScreenHeight / NewsPaperHeight, (FortuneViewport.x - 60) / NewsPaperWidth);
        private bool NewsShown => _page == "news" && _newsLayer != null && _newsLayer.gameObject.activeSelf;
        private bool NewsUnread => _state != null && _state.NewsSeen < CurrentNews.Number;
        private bool NewsReduced => _state != null && _state.Settings.ReducedMotion;
        // Точка картинки киоска (её пиксели) — от середины киоска, в его координатах.
        private static Vector2 NewsKioskAt(Rect area) => new Vector2(area.center.x - NewsKioskWidth / 2, NewsKioskHeight / 2 - area.center.y);

        private void BuildNewsPage()
        {
            if (_newsLayer == null) BuildNewsScene();
            if (!_newsLayer.gameObject.activeSelf)
            {
                _newsPaperShown = false;
                _newsEscapeHeld = Keyboard.current != null && Keyboard.current.escapeKey.isPressed;
                // Подход к окошку: крупный план проявляется поверх улицы и чуть наезжает, продолжая приближение.
                _newsFade = NewsReduced ? 1 : 0;
                _newsPaperFade = 0;
                _newsGroup.alpha = _newsFade;
            }
            _newsLayer.gameObject.SetActive(true);
            _newsLayer.SetAsLastSibling();
            Title("Карточный вестник", "Киоск на площади");
            PaintNews();
        }

        private bool TryNewsBack()
        {
            if (_page != "news" || !_newsPaperShown) return false;
            _newsPaperShown = false;
            PaintNews();
            return true;
        }

        private void OpenNewsPaper()
        {
            if (!NewsShown || _busy) return;
            _newsPaperShown = true;
            PaintNews();
            if (NewsUnread) Send("news_read", amount: CurrentNews.Number);
        }

        /// <summary>«Посмотреть» в главной новости — поездка к её месту, как с карты.</summary>
        private void VisitNewsPlace()
        {
            var index = Array.FindIndex(CityPlaces, place => place.Id == CurrentNews.PlaceId);
            if (_busy || index < 0) return;
            GoHome();
            _mapTripDistrict = -1;
            _mapTripPlace = index;
            ContinueMapTrip();
        }

        /// <summary>Каждый кадр: Esc, свет газеты в руке, кружок у вывески «НОВОСТИ».</summary>
        private void UpdateNews(float deltaTime, bool typing)
        {
            RefreshNewsPresentation();
            var escape = !typing && Keyboard.current != null && Keyboard.current.escapeKey.isPressed;
            if (NewsShown && escape && !_newsEscapeHeld) Back();
            _newsEscapeHeld = escape;
            if (!NewsShown) return;
            _newsClock += deltaTime;
            var pulse = .5f + .5f * Mathf.Sin(_newsClock * 3f);
            _newsHandGlow.color = new Color(1, .86f, .5f, _newsPaperShown ? 0 : .1f + .14f * pulse);
            _newsFade = Mathf.MoveTowards(_newsFade, 1, deltaTime / (NewsReduced ? .01f : .4f));
            _newsGroup.alpha = Mathf.SmoothStep(0, 1, Mathf.Clamp01(_newsFade * 1.6f));
            FitNewsKiosk();
            // Газета вылетает из руки Двойки и раскрывается; назад — так же в руку.
            _newsPaperFade = Mathf.MoveTowards(_newsPaperFade, _newsPaperShown ? 1 : 0, deltaTime / (NewsReduced ? .01f : .3f));
            if (_newsPaperLayer.gameObject.activeSelf != _newsPaperFade > 0) _newsPaperLayer.gameObject.SetActive(_newsPaperFade > 0);
            var open = Mathf.SmoothStep(0, 1, _newsPaperFade);
            _newsPaperGroup.alpha = Mathf.Clamp01(open * 1.5f);
            _newsPaperGroup.blocksRaycasts = _newsPaperShown;
            _newsPaper.anchoredPosition = Vector2.Lerp(NewsKioskAt(NewsHandPaper) * _newsKiosk.localScale.x, Vector2.zero, open);
            _newsPaper.localScale = Vector3.one * NewsPaperScale * Mathf.Lerp(.28f, 1, open);
            _newsPaper.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(-12, 0, open));
        }

        private void RefreshNewsPresentation()
        {
            if (_newsLayer != null && _page != "news" && _newsLayer.gameObject.activeSelf)
            {
                _newsLayer.gameObject.SetActive(false);
                _newsPaperShown = false;
                _newsPaperFade = 0;
                _newsPaperLayer.gameObject.SetActive(false);
            }
            if (_newsStreetBadge == null)
            {
                if (_state == null || !_placePages.TryGetValue("card-city", out var square)) return;
                if (!(square.Page.Find("Sign НОВОСТИ") is RectTransform sign)) return;
                _newsStreetBadge = MakeRect(sign, "News badge", 0, 0, 0, 0);
                _newsStreetBadge.anchorMin = _newsStreetBadge.anchorMax = Vector2.one;
                _newsStreetBadge.pivot = new Vector2(.5f, .5f);
                _newsStreetBadge.anchoredPosition = new Vector2(4, 2);
                _newsStreetBadge.sizeDelta = Vector2.one * 26;
                RedDot(_newsStreetBadge, 18).text = "1";
                _newsStreetBadge.GetComponent<Image>().color = new Color(.73f, .12f, .10f);
                _newsStreetBadge.GetComponent<Outline>().effectColor = new Color(1f, .9f, .67f);
            }
            if (_newsStreetBadge.gameObject.activeSelf != NewsUnread) _newsStreetBadge.gameObject.SetActive(NewsUnread);
        }

        /// <summary>Киоск закрывает экран целиком (cover), как этаж здания.</summary>
        private void FitNewsKiosk()
        {
            var viewport = FortuneViewport;
            var cover = Mathf.Max(viewport.x / _newsKiosk.sizeDelta.x, viewport.y / _newsKiosk.sizeDelta.y);
            _newsKiosk.localScale = Vector3.one * cover * Mathf.Lerp(.93f, 1, Mathf.SmoothStep(0, 1, _newsFade));
        }

        /// <summary>Строка шрифтом меньше, если не влезает в своё поле (а не перенос).</summary>
        private static Text NewsFit(Text label)
        {
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 12;
            label.resizeTextMaxSize = label.fontSize;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            return label;
        }

        private void BuildNewsScene()
        {
            _newsLayer = StageLayer("News kiosk");
            _newsGroup = _newsLayer.gameObject.AddComponent<CanvasGroup>();
            // Прозрачный: пока киоск проявляется и наезжает, по краям видна улица; принимает нажатия мимо киоска.
            var backdrop = LoanArt(_newsLayer, "News backdrop", null, Vector2.zero, new Vector2(6000, 6000), Color.clear);
            backdrop.raycastTarget = true;
            var kiosk = LoanArt(_newsLayer, "News kiosk art", "news-kiosk", Vector2.zero, new Vector2(NewsKioskWidth, NewsKioskHeight),
                new Color(.08f, .16f, .11f));
            _newsKiosk = kiosk.rectTransform;
            if (kiosk.sprite != null)
                _newsKiosk.sizeDelta = new Vector2(NewsKioskHeight * kiosk.sprite.rect.width / kiosk.sprite.rect.height, NewsKioskHeight);
            Vector2 KioskAt(Rect area) => NewsKioskAt(area);

            // Название на пустой табличке над окном — золотом, как вывески города.
            var sign = NewsFit(LoanText(_newsKiosk, "News kiosk sign", "КАРТОЧНЫЙ ВЕСТНИК", KioskAt(NewsSignField), NewsSignField.size, 58,
                Color.white, true));
            var gold = sign.gameObject.AddComponent<HubSignText>();
            gold.Top = SignGoldTop;
            gold.Bottom = SignGoldBottom;
            var signShadow = sign.gameObject.AddComponent<Shadow>();
            signShadow.effectColor = new Color(.06f, .03f, 0, .85f);
            signShadow.effectDistance = new Vector2(2, -3);

            // Газета в руке Двойки светится и нажимается.
            _newsHandGlow = LoanArt(_newsKiosk, "News hand glow", null, KioskAt(NewsHandPaper), NewsHandPaper.size * 1.4f, Color.clear);
            _newsHandGlow.sprite = UiTextures.RadialGlow(128, Color.white);
            var hand = LoanArt(_newsKiosk, "News hand paper", null, KioskAt(NewsHandPaper), NewsHandPaper.size, Color.clear);
            hand.raycastTarget = true;
            hand.gameObject.AddComponent<Button>().onClick.AddListener(OpenNewsPaper);

            var bubble = LoanArt(_newsKiosk, "News bubble", null, KioskAt(NewsBubble), NewsBubble.size, SixBubblePaper);
            bubble.sprite = UiTextures.RoundedRect(64, 64, 22, Color.white);
            bubble.type = Image.Type.Sliced;
            var rim = bubble.gameObject.AddComponent<HubOutline>();
            rim.effectColor = SixBubbleInk;
            rim.effectDistance = new Vector2(3, -3);
            _newsBubbleText = LoanText(bubble.rectTransform, "News bubble line", "", Vector2.zero, NewsBubble.size - new Vector2(30, 16), 30, SixBubbleInk);

            CityButton(_newsLayer, "ЧИТАТЬ", (1600 - 260) / 2f, 900 - 24 - 64, 260, 64, OpenNewsPaper);
            BuildNewsPaper();
            _newsBackButton = (RectTransform)CityButton(_newsLayer, "НАЗАД", 24, 24, 170, 56, Back).transform;
            PinToCorner(_newsBackButton, -1);
            FitNewsKiosk();
            _newsLayer.gameObject.SetActive(false);
        }

        private void BuildNewsPaper()
        {
            _newsPaperLayer = LoanRect(_newsLayer, "News paper layer", Vector2.zero, new Vector2(1600, 900));
            _newsPaperGroup = _newsPaperLayer.gameObject.AddComponent<CanvasGroup>();
            _newsPaperGroup.alpha = 0;
            var dim = LoanArt(_newsPaperLayer, "News paper shade", null, Vector2.zero, new Vector2(6000, 6000), new Color(0, 0, 0, .72f));
            dim.raycastTarget = true;
            dim.gameObject.AddComponent<Button>().onClick.AddListener(() => TryNewsBack());
            var paper = LoanArt(_newsPaperLayer, "News paper", "news-paper", Vector2.zero, new Vector2(NewsPaperWidth, NewsPaperHeight),
                new Color(.96f, .89f, .74f));
            paper.raycastTarget = true;
            _newsPaper = paper.rectTransform;
            _newsPaper.localScale = Vector3.one * NewsPaperScale;
            Vector2 At(float x, float y) => new Vector2(x - NewsPaperWidth / 2, NewsPaperHeight / 2 - y);

            // Шапка через обе страницы (тёмная полоса 60–165 между трефами), название ложится на сгиб.
            var masthead = NewsFit(LoanText(_newsPaper, "News masthead", "КАРТОЧНЫЙ ВЕСТНИК", At(713, 91), new Vector2(1000, 68), 62, Color.white, true));
            var gold = masthead.gameObject.AddComponent<HubSignText>();
            gold.Top = SignGoldTop;
            gold.Bottom = SignGoldBottom;
            _newsIssueLine = NewsFit(LoanText(_newsPaper, "News issue line", "", At(713, 148), new Vector2(1000, 30), 26, new Color(1, .9f, .7f)));

            // Левая страница (рамка 58–677 × 231–908): заголовок, картинка места, текст, «Посмотреть».
            _newsHeadline = NewsFit(LoanText(_newsPaper, "News headline", "", At(368, 290), new Vector2(570, 104), 46, NewsInk, true));
            _newsPicture = LoanArt(_newsPaper, "News picture", null, At(368, 482), new Vector2(570, 262), Color.clear);
            _newsPicture.preserveAspect = true;
            var frame = _newsPicture.gameObject.AddComponent<HubOutline>();
            frame.effectColor = NewsInk;
            frame.effectDistance = new Vector2(3, -3);
            _newsStory = NewsFit(LoanText(_newsPaper, "News story", "", At(368, 720), new Vector2(570, 196), 33, NewsInk));
            _newsLookButton = LoanPaperButton(_newsPaper, "ПОСМОТРЕТЬ", At(368, 862), new Vector2(340, 64), VisitNewsPlace, true);
            _newsLookButton.GetComponentInChildren<Text>().fontSize = 36;

            // Правая страница: «Хроника» (рамка 231–592) и «Слухи» (627–908), обе 748–1368 по ширине.
            LoanText(_newsPaper, "News chronicle heading", "ХРОНИКА", At(1058, 272), new Vector2(560, 56), 42, NewsInk, true);
            _newsChronicle = NewsFit(LoanText(_newsPaper, "News chronicle", "", At(1058, 445), new Vector2(570, 270), 32, NewsInk));
            _newsChronicle.alignment = TextAnchor.UpperLeft;
            LoanText(_newsPaper, "News rumors heading", "СЛУХИ", At(1058, 668), new Vector2(560, 56), 42, NewsInk, true);
            _newsRumor = NewsFit(LoanText(_newsPaper, "News rumor", "", At(1058, 795), new Vector2(570, 180), 34, NewsMuted));
            _newsRumor.alignment = TextAnchor.UpperLeft;
            _newsRumor.fontStyle = FontStyle.Italic;
            _newsPaperLayer.gameObject.SetActive(false);
        }

        private void PaintNews()
        {
            if (_newsLayer == null || _state == null) return;
            FitNewsKiosk();
            _newsBubbleText.text = NewsUnread ? "Свежий выпуск! Читай, пока горячий!" : "Новых выпусков пока нет. Перечитай этот!";
            if (!_newsPaperShown) return;
            _newsPaperLayer.gameObject.SetActive(true);
            _newsPaperLayer.SetAsLastSibling();
            // На 16:9 край разворота заходит под «НАЗАД» — кнопка остаётся сверху.
            _newsBackButton.SetAsLastSibling();
            var issue = CurrentNews;
            var today = DateTimeOffset.FromUnixTimeSeconds(PlayerHubRuntime.Clock.UnixSeconds).ToLocalTime();
            _newsIssueLine.text = "Выпуск № " + issue.Number + " · " + today.ToString("d MMMM yyyy", new System.Globalization.CultureInfo("ru-RU"))
                + " · Цена — 0 коинов";
            _newsHeadline.text = issue.Headline;
            _newsStory.text = issue.Story.Replace(" —", " —");
            _newsPicture.sprite = string.IsNullOrEmpty(issue.Picture) ? null : Resources.Load<Sprite>("UI/Hub/" + issue.Picture);
            _newsPicture.color = _newsPicture.sprite == null ? Color.clear : Color.white;
            _newsLookButton.gameObject.SetActive(Array.Exists(CityPlaces, place => place.Id == issue.PlaceId));
            _newsChronicle.text = NewsChronicle();
            _newsRumor.text = NewsRumors[(int)(today.ToUnixTimeSeconds() / 86400 % NewsRumors.Length)].Replace(" —", " —");
        }

        /// <summary>Хроника — из дел самого игрока: что ждёт и что идёт; не больше четырёх заметок.</summary>
        private string NewsChronicle()
        {
            var lines = new System.Collections.Generic.List<string>();
            var bank = _state.Bank;
            if (bank != null && bank.Debt > 0)
                lines.Add("• Банк Тузяры напоминает: долг " + bank.Debt + " коинов, вернуть до "
                    + DateTimeOffset.FromUnixTimeSeconds(bank.DueAt).ToLocalTime().ToString("dd.MM, HH:mm") + ".");
            var fortune = HubFortunes.Find(_state.Fortune?.EffectId);
            if (fortune != null && HubFortunes.Active(_state.Fortune))
                lines.Add("• Пиковая дама: «" + fortune.Name + "» — ещё партий: " + _state.Fortune.RemainingMatches + ".");
            if (GiftReady()) lines.Add("• У Шестёрки на полке ждёт подарок.");
            var quests = ReadyQuests();
            if (quests > 0) lines.Add("• На доске Шестёрки ждут поручения: " + quests + ".");
            var profile = _state.Profile;
            lines.Add(profile.Matches == 0 ? "• В городе новичок — " + profile.DisplayName + ". Добро пожаловать!"
                : "• " + profile.DisplayName + ": партий — " + profile.Matches + ", побед — " + profile.Wins + ".");
            if (lines.Count > 4) lines.RemoveRange(4, lines.Count - 4);
            return string.Join("\n", lines).Replace(" —", " —");
        }
    }
}
