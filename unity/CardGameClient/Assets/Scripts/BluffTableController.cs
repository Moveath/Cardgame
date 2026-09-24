using System;
using System.Collections;
using System.Collections.Generic;
using CardGame.Bots;
using CardGame.Core.Cards;
using CardGame.Core.Games.Bluff;
using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>
    /// Стол «верю — не верю». Правила живут в CardGame.Core, здесь только показ и ввод.
    ///
    /// Главное отличие от дурака: почти всё на столе — скрытая информация. Котёл лежит
    /// рубашкой вверх, размер пакета не виден, и единственная публичная цифра — та, что
    /// игрок назвал вслух. Поэтому тут нигде не показывается настоящее содержимое котла,
    /// даже когда оно есть под рукой в модели.
    /// </summary>
    public sealed class BluffTableController : MonoBehaviour
    {
        private const int HumanIndex = 0;
        private const string HumanName = "Егор";

        private static readonly string[] BotNamePool = { "Борис", "Вера", "Игорь", "Ольга", "Пётр", "Марина", "Сергей" };

        private static readonly Color TextColor = new Color(0.94f, 0.95f, 0.93f);
        private static readonly Color DimColor = new Color(0.94f, 0.95f, 0.93f, 0.55f);
        private static readonly Color AccentGlow = new Color(1f, 0.85f, 0.3f);
        private static readonly Color HumanColor = new Color(0.24f, 0.45f, 0.78f);
        private static readonly Color ChipColor = new Color(0.36f, 0.22f, 0.12f);
        private static readonly Color LieColor = new Color(0.78f, 0.28f, 0.22f);

        /// <summary>Цвет перестрелки. Нарочно горячий: круг обязан выглядеть иначе обычного.</summary>
        private static readonly Color ShootoutColor = new Color(1f, 0.44f, 0.22f);

        private static readonly Color RoosterColor = new Color(0.84f, 0.20f, 0.17f);

        /// <summary>Готовый арт клейма. Нет файла — рисуем шапку процедурно.</summary>
        private const string RoosterArtPath = "Badges/rooster";

        /// <summary>
        /// Ширина шапки при портрете обычного размера. Голова на портретах около сорока
        /// пяти пикселей; уши шапки закрывают свои и выходят чуть шире головы.
        /// </summary>
        private const float RoosterWidth = 62f;

        /// <summary>
        /// Где у картинки передний край шапки, в долях её высоты сверху. Он ложится на
        /// брови, уши свисают ниже. У рисованной ушанки край — низ отворота.
        /// </summary>
        private const float RoosterArtBrim = 0.772f;

        private const float RoosterDrawnBrim = 0.8f;

        /// <summary>
        /// Как шапка сидит на голове. Передний край на 11 пикселей выше глаз — на бровях,
        /// шапка надвинута. Свою шляпу костюма срезаем на 6 выше края над лицом, а по
        /// бокам, дальше 31 от середины лица, — на 21 ниже края, там, где кончаются уши
        /// шапки: иначе из-под неё торчали бы поля ковбойской шляпы и колпака.
        /// </summary>
        private static readonly SeatFigure.HeadwearFit RoosterFit = new SeatFigure.HeadwearFit(11f, 6f, 31f, 21f);

        /// <summary>
        /// Сколько шапке можно подняться над рамкой места: выше идёт строка с последним
        /// действием. Обычно шапка встаёт на 33, в упор упираются только самые высокие лица
        /// (глаза на 124) — там шапка надвигается на пару пикселей ниже, но в текст не лезет.
        /// </summary>
        private const float RoosterHeadRoom = 46f;

        private static Sprite _roosterArt;
        private static bool _roosterArtChecked;
        private static readonly Color TruthColor = new Color(0.25f, 0.55f, 0.32f);

        // Вертикаль строится сверху вниз фиксированными отступами: на широком окне Unity
        // ужимает высоту холста, и доли экрана начинают наезжать друг на друга.
        private const float SeatTopMargin = 146f;

        /// <summary>
        /// Зазор между местом соперника и вскрытием. Под подписью места бывает ещё строка
        /// «вышел», и вердикт не должен в неё упираться.
        /// </summary>
        private const float RevealTopGap = 40f;
        private const float PotTop = 460f;
        private const float PotHalfHeight = 110f;

        /// <summary>Котёл — одна стопка, а не ряд пар, поэтому мёртвая зона под местами узкая.</summary>
        private const float PotHalfWidth = 200f;

        private const float SeatToPotGap = 40f;
        private const float FeltEdgeMargin = 56f;
        // Руки в блефе большие, поэтому вееру нужно заметно больше места, чем в дураке:
        // иначе рубашки слипаются в одно пятно.
        private const float SeatFanMaxWidth = 230f;

        /// <summary>Больше этого числа рубашек в веере не рисуем — дальше он только слипается.</summary>
        private const int MaxSeatFanCards = 12;
        private const float SeatFanGap = 12f;
        private const float SeatArcBiasSmall = 1.3f;
        private const float SeatArcBiasBig = 1.55f;

        private const float HandRowBottom = 130f;

        /// <summary>Размер карты в руке, пока веер помещается без тесноты.</summary>
        private const float ComfortableHandScale = 1.05f;

        /// <summary>Выбранная карта приподнимается над остальными.</summary>
        private const float SelectedCardLift = 26f;

        /// <summary>
        /// Насколько веер выгибается книзу по краям. Ограничиваем прогиб целиком, а не шаг:
        /// у большой руки крайние карты иначе уезжают за нижний край экрана.
        /// </summary>
        private const float HandFanDrop = 30f;

        private const float RankRowHeight = 56f;
        private const float RankRowGap = 14f;

        /// <summary>
        /// Докуда дотягивается рука в самом высоком своём состоянии: выбранная карта уже
        /// приподнята, курсор поднимает её ещё раз и увеличивает, сверху добавляется
        /// обводка. Ряд рангов считаем от этой величины, иначе карта лезет ему под ноги.
        /// </summary>
        private const float HandTopReach = SelectedCardLift + CardView.HoverLift
                                           + CardView.Height * ComfortableHandScale * CardView.HoverScale * 0.5f
                                           + CardView.SelectedGlowMargin;

        private const float RankRowBottom = HandRowBottom + HandTopReach + RankRowGap + RankRowHeight * 0.5f;

        /// <summary>Ниже этой черты места соперников не опускаются: там уже ранги и рука.</summary>
        private const float SeatFloor = RankRowBottom + RankRowHeight * 0.5f + 18f;

        /// <summary>
        /// Сколько от карты обязано остаться видно из-под соседней. Угол с рангом и мастью
        /// занимает примерно треть ширины, так что меньше сорока процентов — уже гадание.
        /// </summary>
        private const float MinHandStride = 0.42f;

        /// <summary>Место под своё место слева и кнопки справа. Остальное отдаём вееру.</summary>
        private const float HandSideReserve = 820f;

        /// <summary>Сколько рубашек рисуем в котле. Дальше стопка растёт только в высоту зря.</summary>
        private const int MaxPotCards = 14;

        private const float BotThinkDelay = 1.1f;
        private const float ThrowPause = 0.45f;

        /// <summary>
        /// Даже в перестрелке оставляем время заметить, кто сейчас ходит.
        /// </summary>
        private const float ShootoutThinkDelay = 0.95f;

        private const float ShootoutThrowPause = 0.55f;
        private const float RevealHold = 2.2f;
        private const float FourHold = 1.7f;
        private const float FourFade = 0.55f;

        /// <summary>Пауза перед каре, пришедшим прямо на раздаче: стол успевают увидеть.</summary>
        private const float DealSettle = 0.7f;

        // Концовка: тузы проигравшего выносят вперёд и рвут. Крупно — это главный кадр
        // партии, — но четыре штуки обязаны влезть в ширину экрана вместе с обрывками.
        private const float TornAceScale = 1.6f;

        /// <summary>
        /// Промежуток между тузами, в единицах карты. С запасом: порванные обрывки ложатся
        /// шире самой карты, и соседние не должны наезжать друг на друга.
        /// </summary>
        private const float TornAceGap = 80f;

        // Темп спокойный: вынести, дать рассмотреть, рвать по одному.
        private const float TornFlight = 0.7f;
        private const float TornHold = 1f;

        /// <summary>Тузы рвут по очереди, а не разом: каждый разрыв должно быть видно.</summary>
        private const float TornStagger = 0.5f;

        private const float TornScrimAlpha = 0.62f;

        // Объявление перестрелки: короткий наплыв, пауза, чтобы успели прочитать все, и уход.
        private const float ShootoutFlashIn = 0.2f;
        private const float ShootoutHold = 1.9f;
        private const float ShootoutFlashOut = 0.4f;
        private const float FlightDuration = 0.7f;

        private BluffMatchSettings _settings = BluffMatchSettings.Default;
        private Action _onExit;

        private Font _font;
        private BluffGame _game;
        private SimpleBluffBot _bot;

        private Text _status;
        private Text _lastAction;
        private Text _rankBadge;

        private RectTransform _opponentsArea;
        private RectTransform _playerSeatArea;
        private RectTransform _potArea;
        private RectTransform _handRow;
        private RectTransform _rankRow;
        private RectTransform _flyLayer;
        private RectTransform _revealArea;

        private Button _throwButton;
        private Button _challengeButton;
        private Button _shootoutButton;
        private Outline _shootoutGlow;
        private Button _concedeButton;
        private Button _restartButton;
        private Button _menuButton;

        private readonly List<Card> _selected = new List<Card>();
        private readonly List<Button> _rankButtons = new List<Button>();
        private readonly List<Rank> _rankButtonRanks = new List<Rank>();

        private Rank? _declaredRank;
        private bool _waitingForHuman;

        /// <summary>Человек нажал «перестрелка» и теперь выбирает, чем её открыть.</summary>
        private bool _shootoutArmed;

        /// <summary>Сдача спрошена один раз и ждёт подтверждения: партию так просто не бросают.</summary>
        private bool _concedeAsked;

        private BluffMove _pendingMove;

        private Sprite[] _playerPortraits;
        private float _handHalfWidth;

        /// <summary>Чьи тузы унесли рвать: у него в руке и в веере их больше не рисуем.</summary>
        private int? _tornFrom;

        private RectTransform _tornStage;

        /// <summary>Нижний край порванных тузов: под ними встаёт кнопка новой партии.</summary>
        private float _tornBottom;

        public void Configure(BluffMatchSettings settings, Action onExit)
        {
            _settings = settings;
            _onExit = onExit;
        }

        private void Start()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _bot = new SimpleBluffBot();

            BuildUi();
            StartNewGame();
        }

        private void StartNewGame()
        {
            var seed = UnityEngine.Random.Range(0, int.MaxValue);

            // Сначала лица, потом имена: бот с лицом реального человека называется так же,
            // а имя уходит в правила при создании партии и потом не меняется.
            ShuffleAvatars(_settings.PlayerCount);
            var names = BuildPlayerNames(_settings.PlayerCount);
            _game = BluffGame.Create(names, _settings.BuildDeck(seed), _settings.BuildOptions());

            _selected.Clear();
            _declaredRank = null;
            _shootoutArmed = false;
            _concedeAsked = false;
            _pendingMove = null;
            _waitingForHuman = false;
            _tornFrom = null;
            _restartButton.gameObject.SetActive(false);
            Clear(_flyLayer);
            Clear(_revealArea);
            if (_tornStage != null)
            {
                Destroy(_tornStage.gameObject);
                _tornStage = null;
            }

            StopAllCoroutines();
            SetPotVisible(true);
            StartCoroutine(GameLoop());
        }

        // ---------- игровой цикл ----------

        private IEnumerator GameLoop()
        {
            // Сначала стол, потом каре. Каре, пришедшее прямо на раздаче, иначе уходило
            // раньше, чем построены места: четыре карты растворялись над пустым сукном, а
            // после «Ещё раз» — над игроками прошлой партии.
            Refresh();
            if (FindFour(out _, out _))
            {
                yield return new WaitForSeconds(DealSettle);
            }

            while (!_game.IsFinished)
            {
                // Каре разбираем до хода: оно очереди не занимает, но руку меняет.
                yield return CollectFours();
                if (_game.IsFinished)
                {
                    break;
                }

                var actor = _game.CurrentPlayerIndex;

                if (actor == HumanIndex)
                {
                    _waitingForHuman = true;
                    Refresh();

                    // Каре сбрасывается мимо этого цикла и ход не передаёт — но если оно
                    // опустошило руку, ходить человеку уже нечем, и очередь уходит дальше.
                    // Без проверки на смену игрока цикл ждал бы ход, которого не будет.
                    while (_pendingMove == null && !_game.IsFinished && _game.CurrentPlayerIndex == HumanIndex)
                    {
                        yield return null;
                    }

                    _waitingForHuman = false;

                    if (_game.IsFinished)
                    {
                        break;
                    }

                    if (_pendingMove == null)
                    {
                        continue;
                    }

                    var move = _pendingMove;
                    _pendingMove = null;
                    yield return ApplyMove(actor, move);
                    continue;
                }

                Refresh();
                yield return new WaitForSeconds(_game.IsShootout ? ShootoutThinkDelay : BotThinkDelay);

                var botMove = _bot.ChooseMove(_game, actor);
                if (botMove == null)
                {
                    break;
                }

                yield return ApplyMove(actor, botMove);
            }

            _waitingForHuman = false;
            Refresh();
            yield return FinishGame();
        }

        /// <summary>Тузы проигравшего рвут у всех на глазах, и только потом итог партии.</summary>
        private IEnumerator FinishGame()
        {
            yield return TearLoserAces();
            ShowResult();
        }

        private IEnumerator ApplyMove(int actor, BluffMove move)
        {
            var wasChallenge = move.Type == BluffMoveType.Challenge;

            if (!_game.TryApplyMove(actor, move, out var error))
            {
                // Сюда попадать не должны: ходы собираются по тем же правилам, что и проверяются.
                _lastAction.text = error;
                yield break;
            }

            ResetChoice();
            RefreshControls();
            _rankRow.gameObject.SetActive(false);

            if (wasChallenge)
            {
                _status.text = _game.Players[actor].Name + " проверяет";
                yield return PlayReveal();
                yield break;
            }

            if (move.Type == BluffMoveType.Declare || move.Type == BluffMoveType.Follow)
            {
                _status.text = _game.Players[actor].Name + " кладёт карты";
                yield return FlyThrowFrom(actor, move.Cards.Count);
            }
            Refresh();

            // Объявление идёт после броска: пока карты не легли, называть перестрелку не на что.
            if (move.OpensShootout)
            {
                yield return ShowShootoutBanner(actor, _game.CurrentRank!.Value);
                yield break;
            }

            yield return new WaitForSeconds(_game.IsShootout ? ShootoutThrowPause : ThrowPause);
        }

        /// <summary>
        /// Объявление во весь стол. Механики в перестрелке нет никакой — кроме петуха, —
        /// поэтому весь её вес держится на том, что все разом это увидели.
        /// </summary>
        private IEnumerator ShowShootoutBanner(int starter, Rank rank)
        {
            Clear(_revealArea);
            LayoutReveal(1000f, 250f);
            _potArea.gameObject.SetActive(false);
            _rankBadge.gameObject.SetActive(false);

            var banner = new GameObject("Shootout", typeof(RectTransform), typeof(CanvasGroup));
            banner.transform.SetParent(_revealArea, false);

            var bannerRect = (RectTransform)banner.transform;
            bannerRect.anchorMin = bannerRect.anchorMax = bannerRect.pivot = new Vector2(0.5f, 0.5f);
            bannerRect.anchoredPosition = Vector2.zero;
            bannerRect.sizeDelta = new Vector2(1200f, 260f);

            var group = banner.GetComponent<CanvasGroup>();

            var title = CreateText(bannerRect, "ПЕРЕСТРЕЛКА", 84, TextAnchor.MiddleCenter);
            title.color = ShootoutColor;
            title.fontStyle = FontStyle.Bold;
            Place((RectTransform)title.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, 54f), new Vector2(1160f, 104f));

            var who = CreateText(bannerRect,
                _game.Players[starter].Name + " — на " + BluffRules.RankPluralAccusative(rank),
                36, TextAnchor.MiddleCenter);
            who.color = TextColor;
            Place((RectTransform)who.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, -22f), new Vector2(1160f, 46f));

            var hint = CreateText(bannerRect, "кидают все и до последнего · кто первым проверит, тот петух",
                26, TextAnchor.MiddleCenter);
            hint.color = DimColor;
            Place((RectTransform)hint.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, -78f), new Vector2(1160f, 36f));

            yield return AnimateBanner(bannerRect, group, ShootoutFlashIn, 0.6f, 1f);
            yield return new WaitForSeconds(ShootoutHold);
            yield return AnimateBanner(bannerRect, group, ShootoutFlashOut, 1f, 1f, fadeOut: true);

            Clear(_revealArea);
            _potArea.gameObject.SetActive(true);
            _rankBadge.gameObject.SetActive(true);
        }

        private static IEnumerator AnimateBanner(RectTransform rect, CanvasGroup group, float duration,
            float fromScale, float toScale, bool fadeOut = false)
        {
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));

                rect.localScale = Vector3.one * Mathf.Lerp(fromScale, toScale, k);
                group.alpha = fadeOut ? 1f - k : k;
                yield return null;
            }

            rect.localScale = Vector3.one * toScale;
            group.alpha = fadeOut ? 0f : 1f;
        }

        /// <summary>
        /// Каре ход не передаёт и решения не требует — собралось, значит уходит. Кнопку
        /// поэтому убрали: она только спрашивала очевидное. Но не молча: четыре карты
        /// сначала ложатся на стол лицом вверх, как это и делают за настоящим столом.
        /// </summary>
        private IEnumerator CollectFours()
        {
            for (var guard = 0; guard < 16 && !_game.IsFinished; guard++)
            {
                if (!FindFour(out var owner, out var rank))
                {
                    yield break;
                }

                yield return ShowFourLeaving(owner, rank);

                _game.TryApplyMove(owner, BluffMove.DiscardFour(rank), out _);
                ResetChoice();
                Refresh();
            }
        }

        /// <summary>Первое каре, которое можно сбросить. Нет правила — нет и каре.</summary>
        private bool FindFour(out int owner, out Rank rank)
        {
            owner = -1;
            rank = Rank.Two;

            if (!_game.Options.AllowFourOfAKind)
            {
                return false;
            }

            for (var i = 0; i < _game.Players.Count; i++)
            {
                var fours = _game.AvailableFours(i);
                if (fours.Count > 0)
                {
                    owner = i;
                    rank = fours[0];
                    return true;
                }
            }

            return false;
        }

        private IEnumerator ShowFourLeaving(int owner, Rank rank)
        {
            Clear(_revealArea);
            SetPotVisible(false);
            var revealHeight = LayoutReveal();
            _status.text = _game.Players[owner].Name + " сбрасывает каре";

            var caption = CreateText(_revealArea,
                _game.Players[owner].Name + ": каре " + RankPluralGenitive(rank) + " — из игры",
                30, TextAnchor.MiddleCenter);
            caption.color = AccentGlow;
            Place((RectTransform)caption.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, revealHeight * 0.5f - 22f), new Vector2(800f, 44f));

            var scale = Mathf.Min(1.05f, (revealHeight - 100f) / CardView.Height);
            var step = CardView.Width * scale + 16f;
            var start = -3f * step / 2f;

            var views = new List<CardView>();
            var shown = 0;
            foreach (var card in _game.Players[owner].Hand)
            {
                if (card.Rank != rank || shown >= 4)
                {
                    continue;
                }

                var view = CardView.Create(_revealArea, _font, scale);
                view.ShowFace(card);
                view.SetPlain();
                view.SetRestTransform(new Vector2(start + shown * step, -12f), 0f);
                view.PlayAppearAnimation();
                views.Add(view);
                shown++;
            }

            yield return new WaitForSeconds(FourHold);

            // Не исчезают разом: поднимаются и растворяются, чтобы было видно, что ушло.
            var elapsed = 0f;
            while (elapsed < FourFade)
            {
                elapsed += Time.deltaTime;
                var k = Mathf.Clamp01(elapsed / FourFade);

                for (var i = 0; i < views.Count; i++)
                {
                    var rect = views[i].Rect;
                    rect.anchoredPosition = new Vector2(start + i * step, -12f - k * 18f);
                    rect.localScale = Vector3.one * (1f - k * 0.25f);
                    views[i].GetComponent<CanvasGroup>().alpha = 1f - k;
                }

                yield return null;
            }

            Clear(_revealArea);
            SetPotVisible(true);
        }

        private void ResetChoice()
        {
            _selected.Clear();
            _declaredRank = null;
            _shootoutArmed = false;
            _concedeAsked = false;
        }

        // ---------- вскрытие ----------

        /// <summary>
        /// Единственный момент, когда карты показываются: пакет уже вскрыт за столом, и
        /// прятать его дальше незачем. Берём его из лога, потому что правила к этому
        /// моменту котёл уже разгребли.
        /// </summary>
        private IEnumerator PlayReveal()
        {
            BluffLogEntry entry = null;
            for (var i = _game.Log.Count - 1; i >= 0; i--)
            {
                if (_game.Log[i].Kind == BluffLogKind.Challenge)
                {
                    entry = _game.Log[i];
                    break;
                }
            }

            if (entry == null)
            {
                yield break;
            }

            Clear(_revealArea);

            SetPotVisible(false);
            var revealHeight = LayoutReveal();

            // Столбик по центру свободной полосы: вердикт над картами, заявка под ними, петух
            // ещё ниже. Раньше обе надписи стояли над картами и съедали сто семьдесят
            // пикселей: на широком экране, где полоса узкая, карта ужималась вдвое, а
            // вердикт упирался в подпись соседа сверху.
            const float verdictHeight = 58f;
            const float claimHeight = 36f;
            const float brandHeight = 42f;
            const float gap = 8f;
            var overhead = verdictHeight + claimHeight + 2f * gap + (entry.BrokeShootout ? brandHeight + gap : 0f);
            var revealScale = Mathf.Clamp((revealHeight - overhead) / CardView.Height, 0.6f, 1.35f);
            var cardHeight = CardView.Height * revealScale;
            var y = (overhead + cardHeight) * 0.5f;

            var lied = entry.WasLie == true;
            var verdict = CreateText(_revealArea, lied ? "Соврал!" : "Правда!", 50, TextAnchor.MiddleCenter);
            verdict.color = lied ? LieColor : TruthColor;
            verdict.fontStyle = FontStyle.Bold;
            Place((RectTransform)verdict.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, y - verdictHeight * 0.5f),
                new Vector2(600f, verdictHeight));
            y -= verdictHeight + gap;

            var cardsY = y - cardHeight * 0.5f;
            y -= cardHeight + gap;

            var claimed = entry.DeclaredCount ?? 0;
            var claim = CreateText(_revealArea,
                "заявлено: " + claimed + " " + BluffRules.CardsWord(claimed)
                + " · " + BluffRules.RankPlural(entry.DeclaredRank!.Value),
                28, TextAnchor.MiddleCenter);
            claim.color = DimColor;
            Place((RectTransform)claim.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, y - claimHeight * 0.5f),
                new Vector2(800f, claimHeight));
            y -= claimHeight + gap;

            // Кто оборвал перестрелку, узнаёт об этом там же, где вскрылись карты: следующие
            // два круга он ходит в шапке петуха.
            if (entry.BrokeShootout)
            {
                var brand = CreateText(_revealArea,
                    _game.Players[entry.PlayerIndex].Name + " сорвал перестрелку — петух!",
                    32, TextAnchor.MiddleCenter);
                brand.color = RoosterColor;
                brand.fontStyle = FontStyle.Bold;
                Place((RectTransform)brand.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, y - brandHeight * 0.5f),
                    new Vector2(900f, brandHeight));
            }

            var step = CardView.Width * revealScale + 18f;
            var start = -(entry.RevealedCards.Count - 1) * step / 2f;

            for (var i = 0; i < entry.RevealedCards.Count; i++)
            {
                var view = CardView.Create(_revealArea, _font, revealScale);
                view.ShowFace(entry.RevealedCards[i]);
                view.SetPlain();
                view.SetRestTransform(new Vector2(start + i * step, cardsY), 0f);
                view.PlayAppearAnimation();
            }

            yield return new WaitForSeconds(RevealHold);

            Clear(_revealArea);

            if (entry.TakenBy.HasValue && entry.TakenCards.HasValue && entry.TakenCards.Value > 0)
            {
                yield return FlyPotTo(entry.TakenBy.Value, entry.TakenCards.Value);
            }

            Refresh();
            SetPotVisible(true);
        }

        private void SetPotVisible(bool visible)
        {
            _potArea.gameObject.SetActive(visible);
            _rankBadge.gameObject.SetActive(visible);
        }

        // Временные карты и подписи помещаются между местами соперников и рукой.
        // Учитываем только места, пересекающие центральную область по горизонтали.
        private float LayoutReveal(float width = 900f, float height = 380f)
        {
            var root = (RectTransform)transform;
            var layout = BuildSeatLayout(_game.Players.Count - 1);
            // Выбор ранга во время показа скрыт: свободное место идёт до самой руки.
            var bottom = root.rect.height - HandRowBottom - HandTopReach - 16f;
            var top = SeatTopMargin + layout.BlockHeight + RevealTopGap;
            for (var i = 0; i < _game.Players.Count - 1; i++)
            {
                var seat = SeatPosition(i, _game.Players.Count - 1, layout);
                if (Mathf.Abs(seat.x) < width * 0.5f + 110f * layout.Scale)
                {
                    top = Mathf.Max(top, -seat.y + layout.BlockHeight + RevealTopGap);
                }
            }
            var availableHeight = Mathf.Min(height, bottom - top);
            _revealArea.anchorMin = _revealArea.anchorMax = new Vector2(0.5f, 1f);
            _revealArea.anchoredPosition = new Vector2(0f, -(top + bottom) * 0.5f);
            // Размер шрифта больше не зависит от свободной высоты: ужимаются только карты.
            _revealArea.localScale = Vector3.one;
            return availableHeight;
        }

        private IEnumerator FlyThrowFrom(int actor, int count)
        {
            var from = SeatAnchor(actor);
            var to = PotCenter();
            for (var i = 0; i < count; i++)
            {
                var view = CardView.Create(_flyLayer, _font, 1.1f);
                view.ShowBack();
                view.SetPlain();
                var landing = to + new Vector2(i * 5f, -i * 3f);
                view.SetRestTransform(landing, 0f);
                // Та же анимация, которой пользуется стол дурака.
                view.PlayFlyIn(from - landing);
                yield return new WaitForSeconds(0.1f);
            }
            yield return new WaitForSeconds(CardView.FlightDuration);
            Clear(_flyLayer);
        }

        private IEnumerator FlyPotTo(int playerIndex, int count)
        {
            Vector2 from = _flyLayer.InverseTransformPoint(_revealArea.position);
            var to = SeatAnchor(playerIndex);
            _status.text = _game.Players[playerIndex].Name + " забирает стопку";

            var views = new List<CardView>();
            var flying = Mathf.Min(count, 5);
            for (var i = 0; i < flying; i++)
            {
                var view = CardView.Create(_flyLayer, _font, 1f);
                view.ShowBack();
                view.SetPlain();
                view.Rect.anchorMin = view.Rect.anchorMax = view.Rect.pivot = new Vector2(0.5f, 0.5f);
                view.Rect.anchoredPosition = from + new Vector2(i * 6f, i * -4f);
                views.Add(view);
            }

            var elapsed = 0f;
            while (elapsed < FlightDuration)
            {
                elapsed += Time.deltaTime;
                var k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / FlightDuration));
                for (var i = 0; i < views.Count; i++)
                {
                    views[i].Rect.anchoredPosition = Vector2.Lerp(from + new Vector2(i * 6f, i * -4f), to, k);
                }

                yield return null;
            }

            Clear(_flyLayer);
        }

        // ---------- отрисовка ----------

        private void Refresh()
        {
            RefreshStatus();
            RefreshPot();

            // Рука считается раньше мест: своё место встаёт вплотную к её левому краю,
            // и без свежей ширины веера оно уезжало под карты.
            RefreshHand();
            RefreshOpponents();
            RefreshPlayerSeat();
            RefreshRankRow();
            RefreshControls();
        }

        private void RefreshStatus()
        {
            if (_game.IsFinished)
            {
                return;
            }

            if (_waitingForHuman)
            {
                _status.text = NextStepPrompt();
                _status.color = AccentGlow;
            }
            else
            {
                _status.text = "Ходит " + _game.Players[_game.CurrentPlayerIndex].Name + "...";
                _status.color = TextColor;
            }

            _lastAction.text = _game.Log.Count == 0 ? string.Empty : _game.Log[_game.Log.Count - 1].Text;
        }

        /// <summary>
        /// Подсказка идёт строкой статуса, а не отдельной надписью: полоса между котлом и
        /// рукой уже занята рангом круга, счётом котла и рядом рангов.
        /// </summary>
        private string NextStepPrompt()
        {
            if (_game.CanConcede(HumanIndex))
            {
                return "На руках одни тузы — честно ходить нечем. Врать или сдаться";
            }

            if (_game.Phase == BluffPhase.RoundStart && !_declaredRank.HasValue)
            {
                return "Твой круг: назови ранг";
            }

            if (_selected.Count == 0)
            {
                if (_game.Phase == BluffPhase.RoundStart)
                {
                    return _shootoutArmed
                        ? "Перестрелка: выбери карты, с которых она начнётся"
                        : "Выбери в руке от одной до трёх карт — любых";
                }

                return _game.IsShootout
                    ? "Перестрелка: кидай что угодно. Проверишь — станешь петухом"
                    : "Верить или нет? Положи свои карты или скажи «не верю»";
            }

            return "Кладёшь столько, сколько сказал: врут здесь только рангом";
        }

        private void RefreshOpponents()
        {
            Clear(_opponentsArea);

            var opponents = _game.Players.Count - 1;
            var layout = BuildSeatLayout(opponents);

            for (var seat = 0; seat < opponents; seat++)
            {
                var player = _game.Players[seat + 1];
                var position = SeatPosition(seat, opponents, layout);

                // Веер уходит в свою сторону экрана: у левых соперников влево, у правых вправо.
                var fanSide = position.x < 0f ? -1f : 1f;
                var container = BuildSeatBlock(_opponentsArea, player, layout.Scale, layout.FanWidth, fanSide);

                // Привязка и пивот сверху: место задаётся отступом от верха экрана, иначе
                // блок растёт вверх и наезжает на строку статуса.
                container.anchorMin = container.anchorMax = new Vector2(0.5f, 1f);
                container.pivot = new Vector2(0.5f, 1f);
                container.anchoredPosition = position;
            }
        }

        private void RefreshPlayerSeat()
        {
            Clear(_playerSeatArea);

            var canvasHalf = ((RectTransform)transform).rect.width * 0.5f;
            var x = Mathf.Max(-(_handHalfWidth + SeatFigure.Width * 0.5f + 30f), -canvasHalf + 220f);
            _playerSeatArea.anchoredPosition = new Vector2(x, 150f);

            var block = BuildSeatBlock(_playerSeatArea, _game.Players[HumanIndex], 1f);
            block.anchorMin = block.anchorMax = new Vector2(0.5f, 0.5f);
            block.pivot = new Vector2(0.5f, 0.5f);
            block.anchoredPosition = Vector2.zero;
        }

        private RectTransform BuildSeatBlock(Transform parent, BluffPlayer player, float scale,
            float fanWidth = 0f, float fanSide = 1f)
        {
            var container = new GameObject("Seat", typeof(RectTransform));
            container.transform.SetParent(parent, false);

            var containerRect = (RectTransform)container.transform;
            containerRect.sizeDelta = new Vector2(SeatFigure.Width * scale, SeatFigure.BlockHeight * scale);

            var isActing = !_game.IsFinished && player.Index == _game.CurrentPlayerIndex;
            var color = player.Index == HumanIndex ? HumanColor : ChipColor;

            // Петуху шапку надевают по-настоящему: своя шляпа костюма стирается, а шапка
            // садится по глазам именно этого портрета.
            var portrait = PortraitFor(player.Index);
            var headwear = player.IsRooster ? SeatFigure.PutOnHead(portrait, RoosterFit) : null;

            var avatar = SeatFigure.Create(container.transform, _font, player.Name.Substring(0, 1),
                color, isActing, headwear?.Portrait ?? portrait, scale);
            avatar.anchorMin = avatar.anchorMax = new Vector2(0.5f, 1f);
            avatar.pivot = new Vector2(0.5f, 1f);
            avatar.anchoredPosition = Vector2.zero;

            // Подписи «петух» у имени нет: шапка и так видна, а петух теперь на время.
            var label = CreateText(container.transform, player.Name, Mathf.RoundToInt(24 * scale), TextAnchor.MiddleCenter);
            label.color = isActing ? AccentGlow : TextColor;
            PlaceUnderAvatar((RectTransform)label.transform, (SeatFigure.Height + SeatFigure.NameGap) * scale,
                new Vector2(220f * scale, SeatFigure.NameHeight * scale));

            if (headwear != null)
            {
                BuildRoosterHat(containerRect, scale, headwear);
            }

            if (player.IsOut)
            {
                var outText = CreateText(container.transform, "вышел", Mathf.RoundToInt(20 * scale), TextAnchor.MiddleCenter);
                outText.color = DimColor;
                PlaceUnderAvatar((RectTransform)outText.transform,
                    (SeatFigure.Height + SeatFigure.NameGap + SeatFigure.NameHeight) * scale,
                    new Vector2(200f * scale, 26f * scale));

                return containerRect;
            }

            // Сколько карт на руках, видно по вееру рубашек — цифрой это писать не надо,
            // за настоящим столом её тоже никто не объявляет.
            var shown = ShownHand(player).Count;
            if (fanWidth > 0f && shown > 0)
            {
                BuildSeatFan(containerRect, shown, scale, fanWidth, fanSide);
            }

            return containerRect;
        }

        /// <summary>
        /// Шапка петуха тому, кто оборвал перестрелку. Носится два круга; сорвал ещё одну,
        /// пока она на голове, — срок заново. Срок вышел — шапку снимают, и портрет
        /// возвращается в свой костюм: срезанная копия нужна только пока шапка надета.
        /// </summary>
        private void BuildRoosterHat(RectTransform container, float scale, SeatFigure.Headwear headwear)
        {
            var go = new GameObject("Rooster", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Shadow));
            go.transform.SetParent(container, false);

            var sprite = RoosterSprite();

            // Размер задаём шириной, высоту берём из пропорций картинки: готовый арт
            // может оказаться каким угодно, и подгонять его под чужой прямоугольник —
            // верный способ получить растянутую кашу.
            var aspect = sprite.rect.height <= 0f ? 1.1f : sprite.rect.width / sprite.rect.height;
            var width = RoosterWidth * headwear.Size * scale;
            var height = width / Mathf.Max(aspect, 0.2f);

            // Передний край — туда, где его ждёт портрет: по глазам, а не по верху картинки.
            // Верх у костюмов со шляпой — это шляпа, а лица за столом стоят на разной высоте.
            var brim = _roosterArt != null ? RoosterArtBrim : RoosterDrawnBrim;
            var rim = (SeatFigure.Height - headwear.Rim) * scale;
            var top = Mathf.Max(rim - height * brim, -RoosterHeadRoom * scale);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(headwear.CenterX * scale, -top);
            rect.sizeDelta = new Vector2(width, height);

            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;

            var shadow = go.GetComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.45f);
            shadow.effectDistance = new Vector2(2f, -3f);
        }

        /// <summary>
        /// Клеймо берём картинкой из Resources, если она там лежит, и рисуем процедурно,
        /// если нет. Так нарисованный кодом гребешок заменяется готовым артом без единой
        /// правки: достаточно положить файл на место.
        /// </summary>
        private static Sprite RoosterSprite()
        {
            if (!_roosterArtChecked)
            {
                _roosterArtChecked = true;
                _roosterArt = Resources.Load<Sprite>(RoosterArtPath);
            }

            return _roosterArt != null ? _roosterArt : UiTextures.RoosterHat(RoosterColor);
        }

        /// <summary>
        /// Веер рубашек сбоку от фигуры, на высоте рук. Ширина задана снаружи, поэтому
        /// с ростом руки карты не разъезжаются вширь, а плотнее ложатся друг на друга.
        /// </summary>
        private void BuildSeatFan(RectTransform container, int handCount, float scale, float fanWidth, float fanSide)
        {
            // Рисуем не больше дюжины рубашек: дальше веер всё равно читается как «много»,
            // а карты начинают сливаться в сплошное пятно.
            var visible = Mathf.Min(handCount, MaxSeatFanCards);

            var backScale = 0.55f * scale;
            var backWidth = CardView.Width * backScale;
            var angleStep = Mathf.Min(5f, 20f / Mathf.Max(visible - 1, 1));

            // Нижняя граница промежутка важнее верхней: пусть веер вылезет чуть шире,
            // чем превратится в стопку без единой видимой карты.
            var spacing = Mathf.Clamp(
                Mathf.Max(fanWidth - backWidth, 0f) / Mathf.Max(visible - 1, 1),
                backWidth * 0.24f,
                backWidth * 0.55f);

            var row = new GameObject("Cards", typeof(RectTransform));
            row.transform.SetParent(container, false);

            var rowRect = (RectTransform)row.transform;
            rowRect.anchorMin = rowRect.anchorMax = new Vector2(0.5f, 1f);
            // Пивот на той стороне, которой веер примыкает к фигуре, — так он растёт наружу.
            rowRect.pivot = new Vector2(fanSide < 0f ? 1f : 0f, 0.5f);
            rowRect.anchoredPosition = new Vector2(
                fanSide * (SeatFigure.Width * 0.5f + SeatFanGap) * scale,
                -SeatFigure.Height * 0.58f * scale);
            rowRect.sizeDelta = new Vector2(spacing * (visible - 1) + backWidth, CardView.Height * backScale + 16f * scale);

            var mid = (visible - 1) / 2f;
            for (var h = 0; h < visible; h++)
            {
                var back = CardView.Create(row.transform, _font, backScale);
                back.ShowBack();
                back.SetPlain();

                var offset = h - mid;
                back.SetRestTransform(new Vector2(offset * spacing, -Mathf.Abs(offset) * 3f), -offset * angleStep);
            }
        }

        private void RefreshPot()
        {
            Clear(_potArea);

            // Стопка растёт на то, что игроки назвали вслух, а не на то, что легло на самом
            // деле: пересчитать котёл со стороны нельзя, и глазом тоже не должно получаться.
            var visible = Mathf.Clamp(_game.DeclaredPotCount, 0, MaxPotCards);

            for (var i = 0; i < visible; i++)
            {
                var view = CardView.Create(_potArea, _font, 1.1f);
                view.ShowBack();
                view.SetPlain();
                view.SetRestTransform(
                    new Vector2(i * 3.5f - visible * 1.75f, i * -2.5f + visible * 1.25f),
                    Mathf.Sin(i * 2.3f) * 4f);
            }

            if (_game.IsShootout && _game.CurrentRank.HasValue)
            {
                _rankBadge.text = "ПЕРЕСТРЕЛКА · " + RankPluralNominative(_game.CurrentRank.Value);
                _rankBadge.color = ShootoutColor;
                _rankBadge.fontStyle = FontStyle.Bold;
                return;
            }

            _rankBadge.color = AccentGlow;
            _rankBadge.fontStyle = FontStyle.Normal;
            _rankBadge.text = _game.CurrentRank.HasValue
                ? "сейчас ходят " + RankPluralNominative(_game.CurrentRank.Value)
                : "ранг ещё не назван";
        }

        private void RefreshHand()
        {
            Clear(_handRow);

            var hand = ShownHand(_game.Players[HumanIndex]);
            _handHalfWidth = 0f;
            if (hand.Count == 0)
            {
                return;
            }

            // Показываем руку по рангам, а не в порядке раздачи: в блефе весь смысл в том,
            // сколько у тебя карт одного номинала, и глазами это должно читаться сразу.
            var sorted = new List<Card>(hand);
            sorted.Sort((left, right) =>
            {
                var byRank = left.Rank.CompareTo(right.Rank);
                return byRank != 0 ? byRank : left.Suit.CompareTo(right.Suit);
            });

            var available = Mathf.Min(1600f, ((RectTransform)transform).rect.width - HandSideReserve);
            var handCardScale = HandScale(sorted.Count, available);

            var cardWidth = CardView.Width * handCardScale;
            var angleStep = Mathf.Min(5f, 22f / Mathf.Max(sorted.Count - 1, 1));

            var spacing = Mathf.Min(
                cardWidth * 0.82f,
                (available - cardWidth) / Mathf.Max(sorted.Count - 1, 1));

            var mid = (sorted.Count - 1) / 2f;
            _handHalfWidth = spacing * mid + cardWidth * 0.5f + 18f;

            var dropStep = Mathf.Min(6f, HandFanDrop / Mathf.Max(mid, 1f));

            for (var i = 0; i < sorted.Count; i++)
            {
                var card = sorted[i];
                var view = CardView.Create(_handRow, _font, handCardScale);
                view.ShowFace(card);

                var offset = i - mid;
                var isSelected = _selected.Contains(card);
                var position = new Vector2(
                    offset * spacing,
                    -Mathf.Abs(offset) * dropStep + (isSelected ? SelectedCardLift : 0f));
                view.SetRestTransform(position, isSelected ? 0f : -offset * angleStep);

                if (!_waitingForHuman)
                {
                    view.SetPlain();
                    continue;
                }

                var captured = card;
                view.SetClickable(() => ToggleCard(captured));
                view.SetSelected(isSelected);
            }
        }

        /// <summary>
        /// Пока карты помещаются с читаемым нахлёстом, размер не трогаем. Когда перестают —
        /// уменьшаем весь веер, а не ужимаем промежутки дальше: из-под соседней карты
        /// должен торчать угол с рангом, иначе руку невозможно прочитать.
        /// </summary>
        private static float HandScale(int count, float available)
        {
            if (count <= 1)
            {
                return ComfortableHandScale;
            }

            var needed = CardView.Width * (MinHandStride * (count - 1) + 1f);
            if (needed * ComfortableHandScale <= available)
            {
                return ComfortableHandScale;
            }

            return Mathf.Max(0.62f, available / needed);
        }

        private void ToggleCard(Card card)
        {
            if (_selected.Contains(card))
            {
                _selected.Remove(card);
            }
            else if (_selected.Count < _game.Options.MaxCardsPerThrow)
            {
                _selected.Add(card);
            }

            Refresh();
        }

        private void RefreshRankRow()
        {
            var declaring = _waitingForHuman && _game.Phase == BluffPhase.RoundStart;
            _rankRow.gameObject.SetActive(declaring);

            for (var i = 0; i < _rankButtons.Count; i++)
            {
                SetChipSelected(_rankButtons[i], _declaredRank.HasValue && _declaredRank.Value == _rankButtonRanks[i]);
            }

        }

        /// <summary>
        /// Выбранный ранг перекрашивается целиком, а не обводится: одна обводка на тёмной
        /// фишке почти не видна, и было непонятно, что вообще выбрано.
        /// </summary>
        private static void SetChipSelected(Button chip, bool selected)
        {
            chip.GetComponent<Outline>().enabled = selected;
            chip.GetComponent<Image>().sprite =
                UiTextures.RoundedRect(48, 48, 12f, selected ? AccentGlow : ChipColor);

            var label = chip.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.color = selected ? new Color(0.16f, 0.12f, 0.06f) : TextColor;
            }

            chip.transform.localScale = selected ? new Vector3(1.12f, 1.12f, 1f) : Vector3.one;
        }

        private void RefreshControls()
        {
            var canThrow = _waitingForHuman
                           && _selected.Count > 0
                           && (_game.Phase != BluffPhase.RoundStart || _declaredRank.HasValue);

            _throwButton.gameObject.SetActive(_waitingForHuman);
            _throwButton.interactable = canThrow;
            _throwButton.GetComponentInChildren<Text>().text = ThrowLabel();

            _challengeButton.gameObject.SetActive(_waitingForHuman && _game.Phase == BluffPhase.Chain);

            var offered = _waitingForHuman && _game.ShootoutOffered;
            _shootoutButton.gameObject.SetActive(offered);
            _shootoutGlow.enabled = offered && _shootoutArmed;
            _shootoutButton.GetComponentInChildren<Text>().text = _shootoutArmed ? "ПЕРЕСТРЕЛКА" : "Перестрелка";

            // Сдача не привязана к своему ходу: она доступна ровно тогда, когда партия
            // для тебя уже решена, а решается это не очередью.
            var canConcede = !_game.IsFinished && _game.CanConcede(HumanIndex);
            _concedeButton.gameObject.SetActive(canConcede);
            _concedeButton.GetComponentInChildren<Text>().text = _concedeAsked ? "Точно сдаться?" : "Сдаться";

            if (!canConcede)
            {
                _concedeAsked = false;
            }
        }

        /// <summary>
        /// Сдача заканчивает партию, поэтому спрашиваем дважды. Одного промаха по кнопке
        /// хватило бы, чтобы выкинуть игрока из партии, которую он ещё не проиграл.
        /// </summary>
        private void Concede()
        {
            if (!_concedeAsked)
            {
                _concedeAsked = true;
                Refresh();
                return;
            }

            _concedeAsked = false;

            // Сдаться можно и не в свой ход, так что цикл через _pendingMove тут не поможет:
            // он ждёт хода только когда очередь наша. Применяем сразу.
            if (!_game.TryApplyMove(HumanIndex, BluffMove.Concede(), out var error))
            {
                _lastAction.text = error;
                return;
            }

            StopAllCoroutines();
            _waitingForHuman = false;
            Clear(_revealArea);
            Refresh();
            StartCoroutine(FinishGame());
        }

        /// <summary>
        /// Нажатие только взводит объявление: ранг и карты выбираются как обычно, а вслух
        /// перестрелка называется в тот момент, когда пакет уже лёг на стол. Передумать
        /// можно до броска — вторым нажатием.
        /// </summary>
        private void ToggleShootout()
        {
            _shootoutArmed = !_shootoutArmed;
            Refresh();
        }

        /// <summary>
        /// Кнопка проговаривает заявку так, как её сказали бы вслух: «Кидаю одну семёрку»,
        /// «Кидаю двух королей». Ради этого числительное склоняется вместе с картой —
        /// «1 семёрка» звучит как строка из таблицы, а не как игрок за столом.
        /// </summary>
        private string ThrowLabel()
        {
            var rank = _game.Phase == BluffPhase.RoundStart ? _declaredRank : _game.CurrentRank;
            if (!rank.HasValue || _selected.Count == 0)
            {
                return "Кинуть";
            }

            var phrase = "Кидаю " + ThrowPhrase(rank.Value, _selected.Count);
            return _shootoutArmed ? "Перестрелка\n" + phrase : phrase;
        }

        /// <summary>Мужские карты — валет, король, туз, — остальные женские. От этого и форма.</summary>
        private static bool IsMasculine(Rank rank) =>
            rank == Rank.Jack || rank == Rank.King || rank == Rank.Ace;

        /// <summary>«одну семёрку», «двух королей», «три девятки» — винительный падеж.</summary>
        private static string ThrowPhrase(Rank rank, int count)
        {
            if (IsMasculine(rank))
            {
                // Одушевлённые: винительный совпадает с родительным.
                var numeral = count == 1 ? "одного" : count == 2 ? "двух" : "трёх";
                var noun = count == 1 ? MasculineSingular(rank) : RankPluralGenitive(rank);
                return numeral + " " + noun;
            }

            var femaleNumeral = count == 1 ? "одну" : count == 2 ? "две" : "три";
            var femaleNoun = count == 1 ? FeminineAccusative(rank) : RankPluralNominative(rank);
            return femaleNumeral + " " + femaleNoun;
        }

        private static string MasculineSingular(Rank rank) => rank switch
        {
            Rank.Jack => "вальта",
            Rank.King => "короля",
            _ => "туза"
        };

        private static string FeminineAccusative(Rank rank) => rank switch
        {
            Rank.Two => "двойку",
            Rank.Three => "тройку",
            Rank.Four => "четвёрку",
            Rank.Five => "пятёрку",
            Rank.Six => "шестёрку",
            Rank.Seven => "семёрку",
            Rank.Eight => "восьмёрку",
            Rank.Nine => "девятку",
            Rank.Ten => "десятку",
            _ => "даму"
        };

        /// <summary>«семёрки», «короли» — для строки «сейчас ходят короли».</summary>
        private static string RankPluralNominative(Rank rank) => rank switch
        {
            Rank.Two => "двойки",
            Rank.Three => "тройки",
            Rank.Four => "четвёрки",
            Rank.Five => "пятёрки",
            Rank.Six => "шестёрки",
            Rank.Seven => "семёрки",
            Rank.Eight => "восьмёрки",
            Rank.Nine => "девятки",
            Rank.Ten => "десятки",
            Rank.Jack => "вальты",
            Rank.Queen => "дамы",
            Rank.King => "короли",
            _ => "тузы"
        };

        private static string RankPluralGenitive(Rank rank) => BluffRules.RankPlural(rank);

        /// <summary>
        /// Концовка проигравшего: все его тузы — сколько осталось, от одного до четырёх, —
        /// выносят вперёд крупно и рвут пополам, по одному. Стол уходит в тень: это главный
        /// кадр партии. При ничьей рвать некому — проигравшего нет.
        /// </summary>
        private IEnumerator TearLoserAces()
        {
            if (!_game.LoserIndex.HasValue)
            {
                yield break;
            }

            var loser = _game.Players[_game.LoserIndex.Value];
            var aces = new List<Card>();
            foreach (var card in loser.Hand)
            {
                if (card.Rank == Rank.Ace)
                {
                    aces.Add(card);
                }
            }

            if (aces.Count == 0)
            {
                yield break;
            }

            aces.Sort((left, right) => left.Suit.CompareTo(right.Suit));

            // Тузы забирают прямо у игрока: из руки, если проиграл ты, или от места соперника.
            var fromWorld = loser.Index == HumanIndex
                ? _handRow.position
                : _flyLayer.TransformPoint(SeatAnchor(loser.Index));

            _tornFrom = loser.Index;
            Refresh();

            var root = (RectTransform)transform;
            _tornStage = CreatePanel("TornAces", root, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            _tornStage.anchorMin = Vector2.zero;
            _tornStage.anchorMax = Vector2.one;
            _tornStage.offsetMin = Vector2.zero;
            _tornStage.offsetMax = Vector2.zero;
            _tornStage.SetAsLastSibling();

            // Тень на весь стол. Заодно глушит клики, пока идёт сцена.
            var scrimGo = new GameObject("Scrim", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            scrimGo.transform.SetParent(_tornStage, false);
            var scrimRect = (RectTransform)scrimGo.transform;
            scrimRect.anchorMin = Vector2.zero;
            scrimRect.anchorMax = Vector2.one;
            scrimRect.offsetMin = Vector2.zero;
            scrimRect.offsetMax = Vector2.zero;
            var scrim = scrimGo.GetComponent<Image>();
            scrim.color = new Color(0f, 0f, 0f, 0f);

            var room = root.rect.width - 240f;
            var scale = Mathf.Min(TornAceScale, room / (aces.Count * CardView.Width + (aces.Count - 1) * TornAceGap));
            var step = (CardView.Width + TornAceGap) * scale;
            var start = -(aces.Count - 1) * step * 0.5f;
            const float row = -20f;

            var captionGo = new GameObject("Caption", typeof(RectTransform), typeof(CanvasGroup));
            captionGo.transform.SetParent(_tornStage, false);
            var captionRect = (RectTransform)captionGo.transform;
            captionRect.anchorMin = captionRect.anchorMax = captionRect.pivot = new Vector2(0.5f, 0.5f);
            captionRect.sizeDelta = new Vector2(1400f, 80f);
            captionRect.anchoredPosition = new Vector2(0f, row + CardView.Height * scale * 0.5f + 70f);
            var caption = captionGo.GetComponent<CanvasGroup>();
            caption.alpha = 0f;

            // Одна строка: «Аслану порвали туз». Сколько тузов, не говорим — их и так видно.
            // Имя красным и в дательном падеже: склоняет его ядро, чтобы «Али» не стал «Алиу».
            var title = CreateText(captionRect,
                "<color=#" + ColorUtility.ToHtmlStringRGB(LieColor) + ">" + BluffRules.NameDative(loser.Name)
                + "</color> порвали туз",
                58, TextAnchor.MiddleCenter);
            title.color = TextColor;
            title.fontStyle = FontStyle.Bold;
            title.supportRichText = true;
            Place((RectTransform)title.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1400f, 80f));

            Vector2 from = _tornStage.InverseTransformPoint(fromWorld);
            var cards = new List<TornCard>();
            for (var i = 0; i < aces.Count; i++)
            {
                var torn = TornCard.Create(_tornStage, _font, aces[i], scale, i);
                torn.StartCoroutine(torn.FlyIn(from, new Vector2(start + i * step, row), TornFlight, i * 0.08f));
                cards.Add(torn);
            }

            var arrive = TornFlight + (aces.Count - 1) * 0.08f;
            yield return FadeStage(scrim, caption, 0f, 1f, arrive);
            yield return new WaitForSeconds(TornHold);

            for (var i = 0; i < cards.Count; i++)
            {
                cards[i].StartCoroutine(cards[i].Tear());
                if (i < cards.Count - 1)
                {
                    yield return new WaitForSeconds(TornStagger);
                }
            }

            yield return new WaitForSeconds(TornCard.TearDuration);

            // Сцена не исчезает: порванные тузы и подпись остаются до новой раздачи, а итог
            // и кнопки встают поверх тени (это делает ShowResult).
            _tornBottom = row - (CardView.Height * 0.5f + TornCard.SettledDrop) * scale;
        }

        private static IEnumerator FadeStage(Image scrim, CanvasGroup caption, float from, float to, float duration)
        {
            var t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                var k = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration)));
                scrim.color = new Color(0f, 0f, 0f, TornScrimAlpha * k);
                caption.alpha = k;
                yield return null;
            }

            scrim.color = new Color(0f, 0f, 0f, TornScrimAlpha * to);
            caption.alpha = to;
        }

        /// <summary>Рука, как её рисуем. Унесённые рвать тузы в ней уже не показываем.</summary>
        private List<Card> ShownHand(BluffPlayer player)
        {
            var hand = new List<Card>(player.Hand);
            if (_tornFrom == player.Index)
            {
                hand.RemoveAll(card => card.Rank == Rank.Ace);
            }

            return hand;
        }

        private void ShowResult()
        {
            _status.color = AccentGlow;

            if (_game.ConcededIndex.HasValue)
            {
                var quitter = _game.Players[_game.ConcededIndex.Value];
                _status.text = quitter.Index == HumanIndex
                    ? "Ты сдался: тузов " + quitter.AceCount
                    : quitter.Name + " сдался: тузов " + quitter.AceCount;
            }
            else if (_game.IsDraw)
            {
                _status.text = "Ничья: тузов поровну, добивать нечем";
            }
            else if (_game.LoserIndex.HasValue)
            {
                var loser = _game.Players[_game.LoserIndex.Value];
                _status.text = loser.Index == HumanIndex
                    ? "Ты проиграл: тузов " + loser.AceCount
                    : "Проиграл " + loser.Name + ": тузов " + loser.AceCount;
            }
            else
            {
                _status.text = "Партия окончена.";
            }

            _concedeButton.gameObject.SetActive(false);

            // Порванные тузы остаются на экране, поэтому итог, новая партия и выход в меню
            // встают поверх их тени. Кнопка — под обрывками, а не на них.
            var restart = (RectTransform)_restartButton.transform;
            if (_tornStage != null)
            {
                restart.anchoredPosition = new Vector2(0f, _tornBottom - 80f);
                _status.transform.SetAsLastSibling();
                _menuButton.transform.SetAsLastSibling();
                _restartButton.transform.SetAsLastSibling();
            }
            else
            {
                restart.anchoredPosition = new Vector2(0f, -120f);
            }

            _restartButton.gameObject.SetActive(true);
        }

        // ---------- рассадка ----------

        private struct SeatLayout
        {
            public float Scale;
            public float BlockHeight;
            public float ArchDepth;
            public float FanWidth;
            public float MaxX;
            public float Bias;
        }

        private static float OpponentVisualScale(int opponentCount)
        {
            if (opponentCount <= 2) return 1f;
            if (opponentCount <= 4) return 0.9f;
            if (opponentCount <= 6) return 0.8f;
            return 0.7f;
        }

        private SeatLayout BuildSeatLayout(int opponentCount)
        {
            var scale = OpponentVisualScale(opponentCount);
            var canvasHalf = ((RectTransform)transform).rect.width * 0.5f;
            var fanOffset = (SeatFigure.Width * 0.5f + SeatFanGap) * scale;
            var fanWidth = SeatFanMaxWidth * scale;
            var room = canvasHalf - FeltEdgeMargin - fanOffset;

            if (opponentCount > 1)
            {
                var clearance = (SeatFigure.Width * SeatFigure.PortraitWidthAllowance + SeatFanGap) * scale + 24f;
                var fits = (2f * room - clearance * (opponentCount - 1)) / (opponentCount + 1);
                if (fanWidth > fits)
                {
                    fanWidth = Mathf.Max(CardView.Width * 0.5f * scale, fits);
                }
            }

            return new SeatLayout
            {
                Scale = scale,
                BlockHeight = SeatFigure.BlockHeight * scale,
                ArchDepth = opponentCount <= 2 ? 150f : (opponentCount == 3 ? 220f : 290f),
                FanWidth = fanWidth,
                MaxX = room - fanWidth,
                Bias = opponentCount >= 6 ? SeatArcBiasBig : SeatArcBiasSmall
            };
        }

        /// <summary>
        /// Дуга та же, что за столом дурака: углы разведены не поровну, а со сдвигом к краям,
        /// иначе средние места сбиваются в кучу и ряд выглядит прямым. Разница только в
        /// мёртвой зоне — здесь по центру лежит узкая стопка, а не ряд пар.
        /// </summary>
        private Vector2 SeatPosition(int seatIndex, int opponentCount, SeatLayout layout)
        {
            if (opponentCount <= 1)
            {
                return new Vector2(0f, -SeatTopMargin);
            }

            var spread = seatIndex / (float)(opponentCount - 1) * 2f - 1f;
            var eased = Mathf.Sign(spread) * Mathf.Pow(Mathf.Abs(spread), layout.Bias);
            var angle = eased * Mathf.PI * 0.5f;

            var x = layout.MaxX * Mathf.Sin(angle);
            var depth = layout.ArchDepth * (1f - Mathf.Cos(angle));

            var figureHalf = SeatFigure.Width * 0.5f * layout.Scale;
            if (Mathf.Abs(x) - figureHalf < PotHalfWidth)
            {
                var overPot = PotTop - PotHalfHeight - SeatToPotGap - SeatTopMargin - layout.BlockHeight;
                depth = Mathf.Min(depth, Mathf.Max(0f, overPot));
            }

            var corner = ((RectTransform)transform).rect.height - SeatFloor - SeatTopMargin - layout.BlockHeight;
            depth = Mathf.Min(depth, Mathf.Max(0f, corner));

            return new Vector2(x, -(SeatTopMargin + depth));
        }

        /// <summary>Куда летят карты, когда их забирает игрок.</summary>
        private Vector2 SeatAnchor(int playerIndex)
        {
            var canvasHeight = ((RectTransform)transform).rect.height;

            if (playerIndex == HumanIndex)
            {
                return new Vector2(_playerSeatArea.anchoredPosition.x, -(canvasHeight - 150f));
            }

            var opponents = _game.Players.Count - 1;
            var layout = BuildSeatLayout(opponents);
            var seat = SeatPosition(playerIndex - 1, opponents, layout);
            return new Vector2(seat.x, seat.y - layout.BlockHeight * 0.5f);
        }

        private Vector2 PotCenter() => new Vector2(0f, -PotTop);

        // ---------- лица и имена ----------

        private void ShuffleAvatars(int playerCount)
        {
            _playerPortraits = new Sprite[playerCount];
            _playerPortraits[HumanIndex] = PlayerProfile.Avatar;

            var pool = AvatarCatalog.OpponentPortraits(_playerPortraits[HumanIndex], playerCount - 1);
            if (pool.Count == 0)
            {
                return;
            }

            var taken = 0;
            for (var player = 0; player < _playerPortraits.Length; player++)
            {
                if (_playerPortraits[player] != null)
                {
                    continue;
                }

                _playerPortraits[player] = pool[taken % pool.Count];
                taken++;
            }
        }

        private List<string> BuildPlayerNames(int playerCount)
        {
            var names = new List<string> { HumanName };
            var fromPool = 0;

            for (var player = 1; player < playerCount; player++)
            {
                var title = AvatarCatalog.PersonalTitleOf(PortraitFor(player));
                if (title != null)
                {
                    var personal = title;
                    for (var repeat = 2; names.Contains(personal); repeat++)
                    {
                        personal = title + " " + repeat;
                    }

                    names.Add(personal);
                    continue;
                }

                var name = BotNamePool[fromPool % BotNamePool.Length];
                while (names.Contains(name) && fromPool < BotNamePool.Length)
                {
                    fromPool++;
                    name = BotNamePool[fromPool % BotNamePool.Length];
                }

                fromPool++;
                names.Add(name);
            }

            return names;
        }

        private Sprite PortraitFor(int playerIndex) =>
            _playerPortraits != null && playerIndex < _playerPortraits.Length ? _playerPortraits[playerIndex] : null;

        // ---------- сборка экрана ----------

        private void BuildUi()
        {
            var root = (RectTransform)transform;

            var room = new GameObject("Room", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            room.transform.SetParent(root, false);
            Stretch((RectTransform)room.transform);
            room.GetComponent<Image>().color = new Color(0.11f, 0.08f, 0.07f);

            var frame = new GameObject("Frame", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            frame.transform.SetParent(root, false);
            var frameRect = (RectTransform)frame.transform;
            frameRect.anchorMin = Vector2.zero;
            frameRect.anchorMax = Vector2.one;
            frameRect.offsetMin = new Vector2(24f, 24f);
            frameRect.offsetMax = new Vector2(-24f, -24f);
            var frameImage = frame.GetComponent<Image>();
            frameImage.sprite = UiTextures.RoundedRect(64, 64, 24f, new Color(0.36f, 0.22f, 0.12f));
            frameImage.type = Image.Type.Sliced;

            var felt = new GameObject("Felt", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            felt.transform.SetParent(root, false);
            var feltRect = (RectTransform)felt.transform;
            feltRect.anchorMin = Vector2.zero;
            feltRect.anchorMax = Vector2.one;
            feltRect.offsetMin = new Vector2(38f, 38f);
            feltRect.offsetMax = new Vector2(-38f, -38f);
            var feltImage = felt.GetComponent<Image>();
            feltImage.sprite = UiTextures.RoundedRect(64, 64, 20f, new Color(0.10f, 0.26f, 0.34f));
            feltImage.type = Image.Type.Sliced;

            // Обе строки внутри сукна; ниже оставлено место для шапок соперников.
            _status = CreateText(root, string.Empty, 34, TextAnchor.MiddleCenter);
            Place((RectTransform)_status.transform, new Vector2(0.5f, 1f), new Vector2(0f, -56f), new Vector2(1200f, 44f));

            _lastAction = CreateText(root, string.Empty, 25, TextAnchor.MiddleCenter);
            _lastAction.color = DimColor;
            Place((RectTransform)_lastAction.transform, new Vector2(0.5f, 1f), new Vector2(0f, -88f), new Vector2(1100f, 30f));

            _opponentsArea = new GameObject("Opponents", typeof(RectTransform)).GetComponent<RectTransform>();
            _opponentsArea.SetParent(root, false);
            Stretch(_opponentsArea);

            _potArea = CreatePanel("Pot", root, new Vector2(0.5f, 1f), new Vector2(0f, -PotTop), new Vector2(260f, 240f));

            _rankBadge = CreateText(root, string.Empty, 28, TextAnchor.MiddleCenter);
            _rankBadge.color = AccentGlow;
            Place((RectTransform)_rankBadge.transform, new Vector2(0.5f, 1f), new Vector2(0f, -(PotTop + 130f)), new Vector2(700f, 38f));

            _playerSeatArea = CreatePanel("PlayerSeat", root, new Vector2(0.5f, 0f), new Vector2(-520f, 150f), new Vector2(180f, 220f));
            _handRow = CreatePanel("Hand", root, new Vector2(0.5f, 0f), new Vector2(0f, HandRowBottom), new Vector2(1600f, 200f));

            BuildRankRow(root);
            BuildControls(root);

            _revealArea = CreatePanel("Reveal", root, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1200f, 400f));
            _flyLayer = CreatePanel("Fly", root, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(10f, 10f));
        }

        private void BuildRankRow(RectTransform root)
        {
            _rankRow = CreatePanel("Ranks", root, new Vector2(0.5f, 0f), new Vector2(0f, RankRowBottom),
                new Vector2(1600f, RankRowHeight));

            var ranks = AvailableRanks();
            var spacing = Mathf.Min(96f, 1400f / Mathf.Max(ranks.Count, 1));
            var start = -(ranks.Count - 1) * spacing / 2f;

            for (var i = 0; i < ranks.Count; i++)
            {
                var rank = ranks[i];
                var button = CreateChip(_rankRow, RankShort(rank), new Vector2(start + i * spacing, 0f),
                    new Vector2(spacing - 12f, RankRowHeight), () => SelectRank(rank));
                _rankButtons.Add(button);
                _rankButtonRanks.Add(rank);
            }
        }

        private void BuildControls(RectTransform root)
        {
            _menuButton = CreateButton(root, "В меню", new Vector2(0f, 1f), new Vector2(120f, -60f), new Vector2(180f, 64f),
                new Color(0.3f, 0.3f, 0.32f), () =>
                {
                    StopAllCoroutines();
                    _onExit?.Invoke();
                });

            // Всё управление одной колонкой справа. Внизу слева стоит своё место, и каре
            // там налезало прямо на фигуру.
            _throwButton = CreateButton(root, "Кинуть", new Vector2(1f, 0f), new Vector2(-190f, 170f), new Vector2(260f, 76f),
                new Color(0.21f, 0.45f, 0.29f), Throw);

            // Выше нельзя: крайние места дуги спускаются до SeatFloor от низа экрана.
            _challengeButton = CreateButton(root, "Не верю", new Vector2(1f, 0f), new Vector2(-190f, 310f), new Vector2(260f, 76f),
                new Color(0.62f, 0.26f, 0.22f), () => _pendingMove = BluffMove.Challenge());

            // Та же клетка, что у «не верю»: перестрелку объявляют только в начале круга,
            // а проверяют только в цепочке — вместе эти две кнопки не появляются никогда.
            _shootoutButton = CreateButton(root, "Перестрелка", new Vector2(1f, 0f), new Vector2(-190f, 310f),
                new Vector2(260f, 76f), new Color(0.58f, 0.3f, 0.14f), ToggleShootout);

            _shootoutGlow = _shootoutButton.gameObject.AddComponent<Outline>();
            _shootoutGlow.effectColor = ShootoutColor;
            _shootoutGlow.effectDistance = new Vector2(5f, 5f);
            _shootoutGlow.enabled = false;

            // Под «в меню», в углу: кнопка появляется редко и ни с чем не соседствует.
            // В правую колонку её ставить нельзя — там кончилось место над дугой мест.
            _concedeButton = CreateButton(root, "Сдаться", new Vector2(0f, 1f), new Vector2(120f, -136f),
                new Vector2(180f, 60f), new Color(0.5f, 0.2f, 0.18f), Concede);
            _concedeButton.gameObject.SetActive(false);

            _restartButton = CreateButton(root, "Ещё партия", new Vector2(0.5f, 0.5f), new Vector2(0f, -120f), new Vector2(300f, 80f),
                new Color(0.21f, 0.45f, 0.29f), StartNewGame);
            _restartButton.gameObject.SetActive(false);

        }

        private void SelectRank(Rank rank)
        {
            _declaredRank = rank;
            Refresh();
        }

        private void Throw()
        {
            if (_selected.Count == 0)
            {
                return;
            }

            var cards = new List<Card>(_selected);

            if (_game.Phase != BluffPhase.RoundStart)
            {
                _pendingMove = BluffMove.Follow(cards);
                return;
            }

            _pendingMove = _shootoutArmed && _game.ShootoutOffered
                ? BluffMove.DeclareShootout(_declaredRank!.Value, cards)
                : BluffMove.Declare(_declaredRank!.Value, cards);
        }

        private List<Rank> AvailableRanks()
        {
            var lowest = _settings.DeckSize switch
            {
                DeckKind.Twenty4 => Rank.Nine,
                DeckKind.ThirtySix => Rank.Six,
                _ => Rank.Two
            };

            var ranks = new List<Rank>();
            for (var rank = lowest; rank <= Rank.King; rank++)
            {
                if (BluffRules.CanBeDeclared(rank))
                {
                    ranks.Add(rank);
                }
            }

            return ranks;
        }

        private static string RankShort(Rank rank) => rank switch
        {
            Rank.Ten => "10",
            Rank.Jack => "В",
            Rank.Queen => "Д",
            Rank.King => "К",
            _ => ((int)rank).ToString()
        };

        // ---------- мелкие фабрики ----------

        private Button CreateChip(Transform parent, string label, Vector2 position, Vector2 size, Action onClick)
        {
            var go = new GameObject("Chip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline), typeof(Button));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            var image = go.GetComponent<Image>();
            image.sprite = UiTextures.RoundedRect(48, 48, 12f, ChipColor);
            image.type = Image.Type.Sliced;

            var outline = go.GetComponent<Outline>();
            outline.effectColor = AccentGlow;
            outline.effectDistance = new Vector2(3f, 3f);
            outline.enabled = false;

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick());

            var text = CreateText(rect, label, 26, TextAnchor.MiddleCenter);
            Stretch((RectTransform)text.transform);
            text.fontStyle = FontStyle.Bold;

            return button;
        }

        private Button CreateButton(Transform parent, string label, Vector2 anchor, Vector2 position, Vector2 size,
            Color color, Action onClick)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Shadow), typeof(Button));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            var image = go.GetComponent<Image>();
            image.sprite = UiTextures.RoundedRect(140, 76, 18f, color);
            image.type = Image.Type.Sliced;

            var shadow = go.GetComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.4f);
            shadow.effectDistance = new Vector2(3f, -4f);

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick());

            var text = CreateText(rect, label, 28, TextAnchor.MiddleCenter);
            Stretch((RectTransform)text.transform);
            ((RectTransform)text.transform).offsetMin = new Vector2(12f, 6f);
            ((RectTransform)text.transform).offsetMax = new Vector2(-12f, -6f);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 18;
            text.resizeTextMaxSize = 28;

            return button;
        }

        private Text CreateText(Transform parent, string content, int fontSize, TextAnchor anchor)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);

            var text = go.GetComponent<Text>();
            text.font = _font;
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.color = TextColor;
            text.text = content;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            return text;
        }

        private static RectTransform CreatePanel(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            return rect;
        }

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void PlaceUnderAvatar(RectTransform rect, float offsetFromTop, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -offsetFromTop);
            rect.sizeDelta = size;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void Clear(Transform parent)
        {
            for (var i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i);
                child.SetParent(null, false);
                Destroy(child.gameObject);
            }
        }
    }
}
