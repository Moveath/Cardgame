using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>
    /// Таверна «Червовая дама» изнутри (2026-10-10, решение автора) — этаж <see cref="TavernLevel"/>, картинка
    /// `UI/Hub/tavern-hall` (2196×941, 21:9: `screens/references/city/tavern-hall-v1.png` 16:9, дорисованная по 262 px
    /// с боков — `tavern-hall-v2-wide.png`, `scripts/six-house/widen_room.py tavern`; персонажи — лист
    /// `screens/references/characters/hearts-tavern-sheet.png`, промпты — `screens/references/city/tavern-prompts.txt`).
    /// Вход — нырок в дверь таверны в Старом городе или поездка с карты, выход — «ВЫЙТИ», «Назад» или Esc.
    /// За стойкой Дама червей: при входе здоровается и говорит, если ждут заявки в друзья или приглашения за стол;
    /// нажатие — совет. Стол — друзья (страница «Друзья»: «подсаживайся»), почтовые ячейки за стойкой — чаты,
    /// Пятёрка червей на сцене у камина — реплика (музыка — позже, IDEAS.md), угол кланов за бархатной верёвкой — «скоро».
    /// За средними столами — завсегдатаи (2026-10-10, нарисованы в самом зале: `scripts/tavern/patrons_patch.py`):
    /// восьмёрки-строители играют в карты, четвёрки-почтальоны травят байки; у бархатной верёвки дежурят девятки пик —
    /// угол кланов закрыт; за передними столами — Король червей, мэр (один, у сцены, репетирует речь) и свидание семёрок
    /// червей и бубен. Их реплики пока выключены (<see cref="TavernResidentsTalk"/>, решение автора): жители — часть
    /// своего стола, нажатие — друзья. С репликами: нажал на жителей — отвечают, в тишине перекидываются репликой сами.
    /// Говорит кто-то один.
    /// Ячейки открывают почту таверны (PlayerHubTavernMail.cs); непрочитанные письма лежат в самих ячейках: несколько
    /// конвертов или полные ячейки (заплатки зала `UI/Hub/tavern-mail-rack-few` / `-many`, `scripts/tavern/mail_rack_patch.py`).
    /// Доски чемпионов нет: рейтинг автор пока не хочет.
    /// </summary>
    public sealed partial class PlayerHubController
    {
        private const string TavernHall = "tavern-hall";
        private const int TavernLevel = 6;
        // Облачка (пиксели картинки зала): над Дамой — между люстрами, хвостик к её голове; над Пятёркой — правее кепки
        // и ниже кнопки «ВЫЙТИ». Говорит кто-то один: новое облачко гасит другое.
        private const float TavernQueenBubbleX = 1100, TavernQueenBubbleY = 110, TavernQueenHeadX = 1098;
        private const float TavernFiveBubbleX = 640, TavernFiveBubbleY = 148, TavernFiveHeadX = 530;
        private const float TavernBubbleWidth = 480, TavernBubbleHeight = 116;
        // Над завсегдатаями — над касками и фуражками, хвостик к рассказчику; сами заговаривают после стольких секунд тишины.
        private const float TavernBuildersBubbleX = 860, TavernBuildersBubbleY = 290, TavernBuildersHeadX = 975;
        private const float TavernPostmenBubbleX = 1460, TavernPostmenBubbleY = 290, TavernPostmenHeadX = 1385;
        private const float TavernPoliceBubbleX = 1680, TavernPoliceBubbleY = 195, TavernPoliceHeadX = 1785;
        // Король у самого левого края: облачко правее, чтобы на 16:9 (видно с 262) не уходило за экран; хвостик — к руке.
        private const float TavernKingBubbleX = 515, TavernKingBubbleY = 345, TavernKingHeadX = 335;
        private const float TavernSweetheartsBubbleX = 1690, TavernSweetheartsBubbleY = 400, TavernSweetheartsHeadX = 1620;
        private const float TavernChatterSeconds = 11;
        // Письма в почтовых ячейках — заплатка зала левым верхним углом сюда (пиксели картинки), размер — её; с этого
        // числа непрочитанных ячейки забиты, меньше — несколько конвертов.
        private const float TavernRackX = 1206, TavernRackY = 183, TavernRackWidth = 193, TavernRackHeight = 126;
        private const int TavernMailMany = 3;

        private const string TavernGreetingFirst =
            "Добро пожаловать в «Червовую даму»! Подсаживайся к любому столу — там и друзья найдутся. Письма — в ячейках за моей спиной.";
        private static readonly string[] TavernGreetings =
        {
            "Заходи, грейся! Местечко всегда найдётся.",
            "О, гость! Друзья уже заждались.",
            "Заходи. Пятёрка сегодня в ударе.",
            "Заходи! Строители опять проигрывают почтальонам.",
            "Позови друга за стол — я провожу вас в Клуб.",
        };
        private static readonly string[] TavernQueenTips =
        {
            "Подсаживайся к любому столу: там друзья, заявки и «Позвать играть».",
            "Ищешь, с кем сыграть? За столом есть «Найти».",
            "Сыграешь в комнате с новыми лицами — кто-то может попроситься в друзья. Заявки — за столом.",
            "Письма друзей — в ячейках за моей спиной.",
            "В моей таверне не ругаются. Ну, разве что проигравший в дурака.",
            "Тузяра сюда не заходит. И правильно.",
            "Угол кланов я держу для кланов. Скоро соберутся — а пока там дежурят девятки.",
        };
        private static readonly string[] TavernFiveLines =
        {
            "Музыку заказывали? Скоро сыграю что-нибудь душевное!",
            "Аккордеон настроен. Осталось выучить ноты.",
            "Под мою музыку и дурак играет как король!",
        };
        // Восьмёрки-строители (8♣ и 8♦) после смены режутся в дурака; четвёрки-почтальоны (4♥ и 4♣) отдыхают после разноски.
        private static readonly string[] TavernBuildersLines =
        {
            "Козыри — бубны! Кто бы сомневался.",
            "Ха! Опять дурак! Ставь кружку!",
            "Завтра достраиваем мост. А сегодня — отдыхаем!",
            "Ещё по кружке — и я отыграюсь!",
            "Мэр режет ленточки, а строили-то мы!",
        };
        private static readonly string[] TavernPostmenLines =
        {
            "…и тут пёс как прыгнет — а письмо-то было ему!",
            "Ха-ха-ха! Ну ты даёшь!",
            "Сорок писем за утро — и все Тузяре. Ни одного ответа.",
            "Не волнуйся, твоё письмо уже в ячейках у Дамы!",
            "Почта Card City — быстрее голубя!",
        };
        // Девятки пик у бархатной верёвки: строгий со скрещёнными руками, добряк с чаем и печеньем — говорит добряк.
        private static readonly string[] TavernPoliceLines =
        {
            "Угол кланов закрыт до особого распоряжения Дамы!",
            "Мы не на службе. Почти. Печенье будешь?",
            "Напарник у меня строгий: раз закрыто — значит, закрыто.",
            "Тройки сюда не суются. Знают, кто тут стоит.",
            "Соберутся кланы — пропустим. А пока — порядок!",
        };
        // Король червей — мэр: добродушный, любит речи, перед Тузярой робеет (IDEAS.md, «Жители по достоинствам»).
        private static readonly string[] TavernKingLines =
        {
            "Дорогие горожане!.. Кхм. Репетирую речь к празднику.",
            "Пятёрка, сыграй потише — я сочиняю речь!",
            "Скоро праздник! Будут ленточки, фонарики и я с речью.",
            "Мэр я, конечно, мэр… Но Тузяре об этом не говорите.",
            "Восьмёрки строят — я открываю. Разделение труда!",
        };
        // Семёрки червей и бубен — свидание.
        private static readonly string[] TavernSweetheartsLines =
        {
            "Ой… На нас смотрят!",
            "Тише, Пятёрка играет нашу песню!",
            "Последний кусочек торта — тебе.",
            "Это наше третье свидание. Или четвёртое?",
            "Конфеты — тебе. И сердечко — тоже.",
        };

        /// <summary>Житель с облачком реплики: облачко, строка в нём и сколько оно уже висит (−1 — не видно).</summary>
        private sealed class TavernSpeaker
        {
            public RectTransform Bubble;
            public Text Line;
            public CanvasGroup Group;
            public Vector2 Home, Unit;
            public float HeadX, Bottom, Hold;
            public float Time = -1;
        }

        private TavernSpeaker _tavernQueen, _tavernFive, _tavernBuilders, _tavernPostmen, _tavernPolice, _tavernKing, _tavernSweethearts;
        private int _tavernTip, _tavernGreeting, _tavernFiveLine, _tavernBuildersLine, _tavernPostmenLine, _tavernPoliceLine,
            _tavernKingLine, _tavernSweetheartsLine, _tavernChatter;
        private bool _tavernGreeted, _tavernMet;
        private float _tavernQuiet;

        private TavernSpeaker[] TavernSpeakers => new[] { _tavernQueen, _tavernFive, _tavernBuilders, _tavernPostmen, _tavernPolice,
            _tavernKing, _tavernSweethearts };

        /// <summary>Облачка жителей и письма в ячейках — детьми этажа зала (зовётся из AddInteriorFurniture).</summary>
        private void AddTavernHall(RectTransform floor, float width)
        {
            var picture = PictureSize(TavernHall);
            var unit = new Vector2(width / picture.x, CityHeight / picture.y);
            Vector2 At(float x, float y) => new Vector2((x / picture.x - .5f) * width, (.5f - y / picture.y) * CityHeight);
            Image Letters(string kind)
            {
                var image = LoanArt(floor, "Mail letters " + kind, "tavern-mail-rack-" + kind,
                    At(TavernRackX + TavernRackWidth / 2, TavernRackY + TavernRackHeight / 2),
                    new Vector2(TavernRackWidth * unit.x, TavernRackHeight * unit.y), Color.clear);
                image.gameObject.SetActive(false);
                return image;
            }
            _tavernMailFew = Letters("few");
            _tavernMailMany = Letters("many");
            TavernSpeaker Speaker(string name, float x, float y, float headX)
            {
                var (bubble, line, group) = SpeechBubble(floor, name, At(x, y), TavernBubbleWidth, TavernBubbleHeight, headX - x, unit);
                if (MobileUi.Phone)
                {
                    // Long greetings grow the paper instead of shrinking to tiny print.
                    line.resizeTextForBestFit = false;
                    line.fontSize = Mathf.RoundToInt(44 * unit.y);
                    line.horizontalOverflow = HorizontalWrapMode.Wrap;
                    line.verticalOverflow = VerticalWrapMode.Truncate;
                }
                return new TavernSpeaker { Bubble = bubble, Line = line, Group = group,
                    Home = bubble.anchoredPosition, Unit = unit, HeadX = At(headX, y).x,
                    Bottom = bubble.anchoredPosition.y - TavernBubbleHeight * unit.y / 2 };
            }
            _tavernQueen = Speaker("Queen of Hearts bubble", TavernQueenBubbleX, TavernQueenBubbleY, TavernQueenHeadX);
            _tavernFive = Speaker("Five of Hearts bubble", TavernFiveBubbleX, TavernFiveBubbleY, TavernFiveHeadX);
            _tavernBuilders = Speaker("Builders bubble", TavernBuildersBubbleX, TavernBuildersBubbleY, TavernBuildersHeadX);
            _tavernPostmen = Speaker("Postmen bubble", TavernPostmenBubbleX, TavernPostmenBubbleY, TavernPostmenHeadX);
            _tavernPolice = Speaker("Police bubble", TavernPoliceBubbleX, TavernPoliceBubbleY, TavernPoliceHeadX);
            _tavernKing = Speaker("King of Hearts bubble", TavernKingBubbleX, TavernKingBubbleY, TavernKingHeadX);
            _tavernSweethearts = Speaker("Sweethearts bubble", TavernSweetheartsBubbleX, TavernSweetheartsBubbleY, TavernSweetheartsHeadX);
            RefreshMailPresentation();
        }

        /// <summary>
        /// Нажатие на Даму — совет, на Пятёрку — его реплика, на завсегдатаев и полицию у угла кланов — их.
        /// </summary>
        private void TavernTalk(string id)
        {
            if (id == "tavern-five") TavernSay(_tavernFive, TavernFiveLines[_tavernFiveLine++ % TavernFiveLines.Length]);
            else if (id == "tavern-builders") TavernSay(_tavernBuilders, TavernBuildersLines[_tavernBuildersLine++ % TavernBuildersLines.Length]);
            else if (id == "tavern-postmen") TavernSay(_tavernPostmen, TavernPostmenLines[_tavernPostmenLine++ % TavernPostmenLines.Length]);
            else if (id == "tavern-police") TavernSay(_tavernPolice, TavernPoliceLines[_tavernPoliceLine++ % TavernPoliceLines.Length]);
            else if (id == "tavern-king") TavernSay(_tavernKing, TavernKingLines[_tavernKingLine++ % TavernKingLines.Length]);
            else if (id == "tavern-sweethearts")
                TavernSay(_tavernSweethearts, TavernSweetheartsLines[_tavernSweetheartsLine++ % TavernSweetheartsLines.Length]);
            else TavernSay(_tavernQueen, TavernQueenTips[_tavernTip++ % TavernQueenTips.Length]);
        }

        /// <summary>Строка облачка, в которое говорит житель места <paramref name="id"/> (для QA и подсказок).</summary>
        private Text TalkLine(string id)
        {
            if (!id.StartsWith("tavern-", System.StringComparison.Ordinal)) return _sixBubbleText;
            var speaker = id == "tavern-five" ? _tavernFive : id == "tavern-builders" ? _tavernBuilders
                : id == "tavern-postmen" ? _tavernPostmen : id == "tavern-police" ? _tavernPolice
                : id == "tavern-king" ? _tavernKing : id == "tavern-sweethearts" ? _tavernSweethearts : _tavernQueen;
            return speaker?.Line;
        }

        // Кто заговаривает в тишине — по очереди.
        private static readonly string[] TavernChatterOrder =
            { "tavern-builders", "tavern-postmen", "tavern-king", "tavern-police", "tavern-sweethearts" };

        /// <summary>Тишина в зале: завсегдатаи сами перекидываются репликой — по очереди.</summary>
        private void TavernChatter() => TavernTalk(TavernChatterOrder[_tavernChatter++ % TavernChatterOrder.Length]);

        private void TavernSay(TavernSpeaker speaker, string line)
        {
            if (speaker == null) return;
            foreach (var other in TavernSpeakers)
                if (other != null && other != speaker && other.Bubble.gameObject.activeSelf) { other.Bubble.gameObject.SetActive(false); other.Time = -1; }
            // Неразрывный пробел перед тире: перенос не уводит «—» в начало строки.
            speaker.Line.text = line.Replace(" —", " —");
            speaker.Bubble.gameObject.SetActive(true);
            speaker.Bubble.SetAsLastSibling();
            speaker.Group.alpha = 1;
            speaker.Time = 0;
            speaker.Hold = MobileUi.Phone ? Mathf.Clamp(2.5f + line.Length / 15f, 6, 13) : SixBubbleSeconds;
            LayoutTavernSpeech(speaker);
        }

        /// <summary>Readable phone dialogue follows its speaker but stays inside the visible hall.</summary>
        private void LayoutTavernSpeech(TavernSpeaker speaker)
        {
            if (!MobileUi.Phone || speaker == null || _cityArea == null || Floor(TavernLevel) == null) return;
            var scale = FloorScale(TavernLevel);
            var view = _cityArea.rect.size / scale;
            var unit = speaker.Unit;
            var width = Mathf.Min(780 * unit.x, view.x - 64 * unit.x);
            var line = speaker.Line.rectTransform;
            line.sizeDelta = new Vector2(width - 48 * unit.x, line.sizeDelta.y);
            var height = Mathf.Max(126 * unit.y, speaker.Line.preferredHeight + 32 * unit.y);
            var size = new Vector2(width, height);
            speaker.Bubble.sizeDelta = size;
            ((RectTransform)speaker.Bubble.Find("Paper")).sizeDelta = size;
            ((RectTransform)speaker.Bubble.Find("Edge")).sizeDelta = size + Vector2.one * 8;
            line.sizeDelta = size - new Vector2(48 * unit.x, 32 * unit.y);
            var inset = 24 * unit.x;
            var y = Mathf.Clamp(speaker.Bottom + height / 2, -view.y / 2 + height / 2 + inset,
                view.y / 2 - height / 2 - inset);
            var left = _floorPan - view.x / 2 + width / 2 + inset;
            var right = _floorPan + view.x / 2 - width / 2 - inset;
            // A panned, upper-left speaker must not put paper under the enlarged exit button.
            var controlUnit = _stage != null ? _stage.localScale.x / scale : unit.x;
            if (y + height / 2 > view.y / 2 - 132 * controlUnit)
                left = Mathf.Min(right, Mathf.Max(left, _floorPan - view.x / 2 + width / 2 + 288 * controlUnit));
            var x = Mathf.Clamp(speaker.Home.x, left, right);
            speaker.Bubble.anchoredPosition = new Vector2(x, y);
            var tail = (RectTransform)speaker.Bubble.Find("Tail");
            tail.anchoredPosition = new Vector2(Mathf.Clamp(speaker.HeadX - x, -width / 2 + 36 * unit.x,
                width / 2 - 36 * unit.x), -height / 2 - 4);
        }

        /// <summary>Что ждёт игрока в таверне: входящие заявки в друзья и приглашения за стол.</summary>
        private (int Requests, int Invites) TavernNews()
        {
            if (_state == null) return (0, 0);
            var me = _state.Profile.Id;
            return (_state.FriendRequests.Count(item => item.Status == "pending" && item.ToId == me),
                _state.Invites.Count(item => item.Status == "pending" && item.ToId == me));
        }

        private string TavernGreeting()
        {
            var (requests, invites) = TavernNews();
            if (!_tavernMet) return TavernGreetingFirst;
            var letters = MailUnreadAll();
            var sender = letters.From.Length == 1 ? _state.People.FirstOrDefault(person => person.Id == letters.From[0])?.Name ?? "друга" : null;
            if (sender != null && letters.Count == 1) return "Тебе письмо от " + sender + "! Оно в ячейках за моей спиной.";
            if (sender != null) return "Тебе письма от " + sender + ": " + letters.Count + ". Они в ячейках за моей спиной.";
            if (letters.From.Length > 1) return "Тебе письма от друзей: " + letters.Count + ". Они в ячейках за моей спиной.";
            if (requests > 0) return "Тебя ждут заявки в друзья: " + requests + ". Присаживайся за стол!";
            if (invites > 0) return "Тебя зовут за стол! Приглашений: " + invites + " — загляни к друзьям.";
            return TavernGreetings[_tavernGreeting++ % TavernGreetings.Length];
        }

        /// <summary>
        /// Каждый кадр: войдя в зал, Дама здоровается; облачка висят и гаснут, как у Шестёрки; в тишине завсегдатаи
        /// заговаривают сами.
        /// </summary>
        private void UpdateTavern(float deltaTime)
        {
            if (_cityLevel != TavernLevel)
            {
                _tavernGreeted = false;
                _tavernQuiet = 0;
                foreach (var speaker in TavernSpeakers)
                    if (speaker != null && speaker.Bubble.gameObject.activeSelf) { speaker.Bubble.gameObject.SetActive(false); speaker.Time = -1; }
                return;
            }
            if (!_tavernGreeted && _diveTarget < 0 && _page == "home" && _tavernQueen != null)
            {
                _tavernGreeted = true;
                TavernSay(_tavernQueen, TavernGreeting());
                _tavernMet = true;
            }
            var talking = false;
            foreach (var speaker in TavernSpeakers)
            {
                if (speaker == null || speaker.Time < 0) continue;
                LayoutTavernSpeech(speaker);
                speaker.Time += deltaTime;
                var fade = Mathf.Clamp01((speaker.Time - speaker.Hold) / SixBubbleFade);
                speaker.Group.alpha = 1 - fade;
                if (fade < 1) { talking = true; continue; }
                speaker.Time = -1;
                speaker.Bubble.gameObject.SetActive(false);
            }
            // Тишина считается только на главной зала, когда никто не говорит и камера стоит (и жители с репликами).
            if (talking || !TavernResidentsTalk || _page != "home" || _diveTarget >= 0 || _visitPhase != 0 || _mapOpen) _tavernQuiet = 0;
            else if ((_tavernQuiet += deltaTime) >= TavernChatterSeconds)
            {
                _tavernQuiet = 0;
                TavernChatter();
            }
        }
    }
}
