using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>
    /// Дом Шестёрки (2026-10-07) — второе здание изнутри после Клуба: этаж <see cref="SixLevel"/>, картинка
    /// `UI/Hub/six-room` (2196×941, 21:9: `six-room-v1.png` 16:9, дорисованная по 262 px с боков, чтобы на телефоне 20:9
    /// комната влезала по высоте; исходник — `screens/references/city/six-room-v2-wide.png`, `scripts/six-house/widen_room.py`). Вход — нырок
    /// в дверь домика в Старом городе или поездка с карты, выход — дверь изнутри, «Выйти», «Назад», Esc или S.
    /// Комната сама — меню, таблички у предметов автор пока убрал (2026-10-07): что где, говорит Шестёрка при первом
    /// входе. Доска с записками — «Задания» (красный кружок в её углу — сколько наград ждут), меловая доска —
    /// «Обучение», подарок на полке — серебряные коины раз в 12 часов (PlayerHubSixGift.cs); сундук и столик с колодой —
    /// просто фон. Сам Шестёрка (лист `screens/references/characters/six-of-spades-guide.png`, вырезан
    /// `scripts/six-house/cut_six.py`) стоит у двери: при входе здоровается, нажатие на него — совет или шутка
    /// в облачке над головой. По лору он переодетый Туз пик (IDEAS.md, «Лор и сюжет»): пара реплик — намёки.
    /// </summary>
    public sealed partial class PlayerHubController
    {
        private const string SixRoom = "six-room";
        private const int SixLevel = 3;
        // Ширина картинки комнаты: 21:9 — на 20:9 видна вся высота и почти вся ширина, на 16:9 видна середина
        // (прежняя картинка 16:9), края — свайпом.
        private const float SixPictureWidth = 2196;
        // Шестёрка на картинке комнаты (её пиксели): середина, линия ботинок и рост.
        private const float SixX = 687, SixFeet = 835, SixHeight = 590;
        // Облачко реплики над головой: середина, размер, хвостик над правым краем головы; сколько секунд висит и за
        // сколько гаснет. Облачко правее головы — подальше от кнопки «ВЫЙТИ».
        private const float SixBubbleX = 902, SixBubbleY = 168, SixBubbleWidth = 480, SixBubbleHeight = 116, SixBubbleTailX = 732;
        private const float SixBubbleSeconds = 4.5f, SixBubbleFade = .3f;
        // Красный кружок «Заданий» — у правого верхнего угла пробковой доски.
        private const float SixBadgeX = 1408, SixBadgeY = 126, SixBadgeSize = 40;
        private static readonly Color SixBubbleInk = new Color(.17f, .1f, .05f), SixBubblePaper = new Color(1f, .98f, .92f),
            SixBadgeRed = new Color(.84f, .16f, .16f);

        private const string SixGreetingFirst = "Заходи! Записки на стене — поручения, мел — обучение, а на полке бывает подарок.";
        private static readonly string[] SixGreetings =
        {
            "О, снова ты! Заходи, грейся.",
            "Чайник как раз закипел. Садись.",
            "Заходи. Поручений сегодня — гора.",
        };
        private static readonly string[] SixTips =
        {
            "В «Дураке» козыри береги до конца — пригодятся.",
            "В «Верю — не верю» не ври каждый ход. Ври, когда никто не ждёт.",
            "Тузяре в долг не бери. Проценты у него… живые.",
            "Каждый день на доске новые поручения. Заглядывай.",
            "Я тут всех знаю. Даже тех, кто сам себя забыл.",
            "Говорят, Туз пик пропал. Ерунда, наверно.",
            "Шарф? Подарок. Давний.",
        };

        // Мел — «Обучение» (решение автора, 2026-10-07): обучение — экскурсия по городу с Шестёркой (IDEAS.md); её пока нет,
        // поэтому «Сначала» и «С того места» отвечают репликой. Награда за неё — один раз за игру, даже при пропуске.
        private const string SixTourAsk = "Показать тебе город? Могу с самого начала, а могу с того места, где мы остановились.";
        private const string SixTourSoon = "Экскурсию по городу я ещё готовлю — скоро покажу!";
        private const string SixTourLater = "Как скажешь. Мел никуда не денется.";
        // Кнопки выбора — под облачком, по середине; ширина каждой и зазор (пиксели картинки комнаты).
        private const float SixChoiceGap = 14, SixChoiceHeight = 50;
        private static readonly (string Text, float Width)[] SixTourChoices = { ("СНАЧАЛА", 150), ("С ТОГО МЕСТА", 190), ("НЕ СЕЙЧАС", 160) };

        private RectTransform _sixChoices;
        private RectTransform _sixBubble;
        private Text _sixBubbleText;
        private CanvasGroup _sixBubbleGroup;
        private float _sixBubbleTime = -1;
        private int _sixTip, _sixGreeting;
        private bool _sixGreeted, _sixMet;
        private RectTransform _sixQuestBadge;
        private Text _sixQuestBadgeText;
        private int _sixQuestsShown = -1;

        /// <summary>Шестёрка, тень под ним, облачко и кружок «Заданий» — детьми этажа комнаты (зовётся из AddInteriorFurniture).</summary>
        private void AddSixRoom(RectTransform floor, float width)
        {
            var picture = PictureSize(SixRoom);
            var unit = new Vector2(width / picture.x, CityHeight / picture.y);
            Vector2 At(float x, float y) => new Vector2((x / picture.x - .5f) * width, (.5f - y / picture.y) * CityHeight);
            RectTransform Centered(Transform parent, string name, Vector2 pivot, Vector2 position, Vector2 size)
            {
                var rect = MakeRect(parent, name, 0, 0, 0, 0);
                rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
                rect.pivot = pivot;
                rect.anchoredPosition = position;
                rect.sizeDelta = size;
                return rect;
            }
            var shade = Centered(floor, "Six shadow", new Vector2(.5f, .5f), At(SixX, SixFeet - 6), new Vector2(330 * unit.x, 46 * unit.y))
                .gameObject.AddComponent<Image>();
            shade.sprite = UiTextures.RadialGlow(256, Color.white);
            shade.color = new Color(0, 0, 0, .55f);
            shade.raycastTarget = false;
            var sprite = Resources.Load<Sprite>("UI/Hub/six-guide");
            if (sprite != null)
            {
                var aspect = sprite.rect.width / sprite.rect.height;
                var six = Centered(floor, "Six guide", new Vector2(.5f, 0), At(SixX, SixFeet),
                    new Vector2(SixHeight * aspect * unit.x, SixHeight * unit.y)).gameObject.AddComponent<Image>();
                six.sprite = sprite;
                six.raycastTarget = false;
            }
            (_sixBubble, _sixBubbleText, _sixBubbleGroup) = SpeechBubble(floor, "Six bubble", At(SixBubbleX, SixBubbleY),
                SixBubbleWidth, SixBubbleHeight, SixBubbleTailX - SixBubbleX, unit);

            // Ответы на вопрос Шестёрки — вывесочные кнопки под облачком.
            var row = 0f;
            foreach (var choice in SixTourChoices) row += choice.Width + SixChoiceGap;
            row -= SixChoiceGap;
            _sixChoices = Centered(floor, "Six choices", new Vector2(.5f, 1),
                At(SixBubbleX, SixBubbleY + SixBubbleHeight / 2 + 22), new Vector2(row, SixChoiceHeight) * unit.y);
            var left = 0f;
            foreach (var choice in SixTourChoices)
            {
                var text = choice.Text;
                CityButton(_sixChoices, text, left * unit.y, 0, choice.Width * unit.y, SixChoiceHeight * unit.y, () => AnswerSixTour(text));
                left += choice.Width + SixChoiceGap;
            }
            _sixChoices.gameObject.SetActive(false);
            AddSixGiftShelf(floor, At, unit);

            _sixQuestBadge = Centered(floor, "Six quest badge", new Vector2(.5f, .5f), At(SixBadgeX, SixBadgeY),
                Vector2.one * SixBadgeSize * unit.y);
            var circle = _sixQuestBadge.gameObject.AddComponent<Image>();
            circle.sprite = UiTextures.Circle(64, Color.white);
            circle.color = SixBadgeRed;
            circle.raycastTarget = false;
            var rim = _sixQuestBadge.gameObject.AddComponent<HubOutline>();
            rim.effectColor = Color.white;
            rim.effectDistance = new Vector2(2, -2);
            _sixQuestBadgeText = MakeRect(_sixQuestBadge, "Count", 0, 0, 0, 0).gameObject.AddComponent<Text>();
            Stretch(_sixQuestBadgeText.rectTransform);
            _sixQuestBadgeText.font = Resources.Load<Font>("Fonts/SignNarrow") ?? _font;
            _sixQuestBadgeText.fontSize = Mathf.RoundToInt(26 * unit.y);
            _sixQuestBadgeText.alignment = TextAnchor.MiddleCenter;
            _sixQuestBadgeText.color = Color.white;
            _sixQuestBadgeText.raycastTarget = false;
            _sixQuestBadge.gameObject.SetActive(false);
            _sixQuestsShown = -1;
        }

        /// <summary>
        /// Облачко реплики жителя на этаже (Шестёрка, Дама червей, Пятёрка): тёмный край — тот же скруглённый прямоугольник
        /// побольше; хвостик — повёрнутый квадрат к голове. Размеры и сдвиг хвостика от середины — в пикселях картинки этажа.
        /// Создаётся скрытым.
        /// </summary>
        private (RectTransform Bubble, Text Line, CanvasGroup Group) SpeechBubble(RectTransform floor, string name, Vector2 at,
            float width, float height, float tailOffset, Vector2 unit)
        {
            RectTransform Centered(Transform parent, string part, Vector2 position, Vector2 size)
            {
                var rect = MakeRect(parent, part, 0, 0, 0, 0);
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
                rect.anchoredPosition = position;
                rect.sizeDelta = size;
                return rect;
            }
            Image Paper(RectTransform rect, Color color)
            {
                var image = rect.gameObject.AddComponent<Image>();
                image.sprite = UiTextures.RoundedRect(64, 64, 22, Color.white);
                image.type = Image.Type.Sliced;
                image.color = color;
                image.raycastTarget = false;
                return image;
            }
            var bubble = Centered(floor, name, at, new Vector2(width * unit.x, height * unit.y));
            var group = bubble.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = group.interactable = false;
            var tail = Centered(bubble, "Tail", new Vector2(tailOffset * unit.x, -height * unit.y / 2 - 4), new Vector2(30, 30) * unit.y);
            tail.localRotation = Quaternion.Euler(0, 0, 45);
            Paper(Centered(bubble, "Edge", Vector2.zero, bubble.sizeDelta + Vector2.one * 8), SixBubbleInk);
            var tailEdge = tail.gameObject.AddComponent<Image>();
            tailEdge.color = SixBubbleInk;
            tailEdge.raycastTarget = false;
            var tailPaper = Centered(tail, "Paper", Vector2.zero, tail.sizeDelta - Vector2.one * 8 * unit.y).gameObject.AddComponent<Image>();
            tailPaper.color = SixBubblePaper;
            tailPaper.raycastTarget = false;
            Paper(Centered(bubble, "Paper", Vector2.zero, bubble.sizeDelta), SixBubblePaper);
            var line = Centered(bubble, "Line", Vector2.zero, bubble.sizeDelta - new Vector2(36, 20) * unit.y).gameObject.AddComponent<Text>();
            line.font = Resources.Load<Font>("Fonts/SignNarrow") ?? _font;
            line.color = SixBubbleInk;
            line.alignment = TextAnchor.MiddleCenter;
            line.resizeTextForBestFit = true;
            line.resizeTextMinSize = 14;
            line.resizeTextMaxSize = Mathf.RoundToInt(30 * unit.y);
            line.fontSize = line.resizeTextMaxSize;
            line.raycastTarget = false;
            bubble.gameObject.SetActive(false);
            return (bubble, line, group);
        }

        /// <summary>
        /// Нажатие на Шестёрку — совет в облачке; на подарок — его крупный план (PlayerHubSixGift.cs); на мел — вопрос,
        /// показать ли город.
        /// </summary>
        private void SixTalk(string id)
        {
            if (id == "six-gift") ShowSixGift();
            else if (id == "six-lessons") AskSixTour();
            else SixSay(SixTips[_sixTip++ % SixTips.Length]);
        }

        /// <summary>Шестёрка спрашивает, показать ли город; облачко висит, пока не выбран ответ.</summary>
        private void AskSixTour()
        {
            SixSay(SixTourAsk);
            if (_sixChoices == null) return;
            _sixBubbleTime = -1;
            _sixChoices.gameObject.SetActive(true);
            _sixChoices.SetAsLastSibling();
        }

        private void AnswerSixTour(string choice)
        {
            // Экскурсии пока нет (IDEAS.md): «Сначала» начнёт её с площади, «С того места» — с остановки, где прервались.
            SixSay(choice == "НЕ СЕЙЧАС" ? SixTourLater : SixTourSoon);
        }

        private void SixSay(string line)
        {
            if (_sixBubble == null) return;
            if (_sixChoices != null && _sixChoices.gameObject.activeSelf) _sixChoices.gameObject.SetActive(false);
            // Неразрывный пробел перед тире: перенос не уводит «—» в начало строки.
            _sixBubbleText.text = line.Replace(" —", "\u00A0—");
            _sixBubble.gameObject.SetActive(true);
            _sixBubbleGroup.alpha = 1;
            _sixBubbleTime = 0;
        }

        /// <summary>
        /// Каждый кадр: войдя в комнату, Шестёрка здоровается (впервые за игру — говорит, что где);
        /// облачко висит и гаснет; кружок у «Заданий» — сколько наград ждут.
        /// </summary>
        private void UpdateSixHouse(float deltaTime)
        {
            if (_cityLevel != SixLevel)
            {
                _sixGreeted = false;
                if (_sixBubble != null && _sixBubble.gameObject.activeSelf) _sixBubble.gameObject.SetActive(false);
                if (_sixChoices != null && _sixChoices.gameObject.activeSelf) _sixChoices.gameObject.SetActive(false);
                return;
            }
            if (!_sixGreeted && _diveTarget < 0 && _page == "home" && _sixBubble != null)
            {
                _sixGreeted = true;
                SixSay(ReadyQuests() > 0 ? "Есть поручения — глянь на доску" : !_sixMet ? SixGreetingFirst : GiftReady() ? SixGiftReadyLine : SixGreetings[_sixGreeting++ % SixGreetings.Length]);
                _sixMet = true;
            }
            if (_sixBubble != null && _sixBubbleTime >= 0)
            {
                _sixBubbleTime += deltaTime;
                var fade = Mathf.Clamp01((_sixBubbleTime - SixBubbleSeconds) / SixBubbleFade);
                _sixBubbleGroup.alpha = 1 - fade;
                if (fade >= 1)
                {
                    _sixBubbleTime = -1;
                    _sixBubble.gameObject.SetActive(false);
                }
            }
            RefreshSixQuestBadge();
        }

        /// <summary>Красный кружок в углу доски «Заданий»: сколько новых поручений или наград ждут; ноль — не виден.</summary>
        private void RefreshSixQuestBadge()
        {
            if (_sixQuestBadge == null) return;
            var ready = ReadyQuests();
            if (ready == _sixQuestsShown) return;
            _sixQuestsShown = ready;
            _sixQuestBadgeText.text = ready > 9 ? "9+" : ready.ToString();
            _sixQuestBadge.gameObject.SetActive(ready > 0);
        }
    }
}
