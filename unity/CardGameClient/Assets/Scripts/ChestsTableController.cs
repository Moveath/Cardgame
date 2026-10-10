using System;
using System.Collections;
using System.Collections.Generic;
using CardGame.Bots;
using CardGame.Core.Cards;
using CardGame.Core.Games.Bluff;
using CardGame.Core.Games.Chests;
using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>
    /// Стол «Сундучков». Правила живут в CardGame.Core, здесь только показ и ввод.
    ///
    /// Главное, чего здесь нельзя показывать, — сколько карт у соперника. В блефе веер
    /// рубашек честно растёт вместе с рукой, а тут он выдал бы, у кого под конец пара, а у
    /// кого одна карта. Поэтому у каждого соперника с картами одна и та же стопка из трёх
    /// рубашек. Открыто только одно: у кого карт не осталось — тот сидит без стопки. И
    /// сундучки, конечно.
    ///
    /// Вопрос идёт по ступенькам: соперник, ранг, число, масти, — и в каждый момент на
    /// столе одна полоса выбора. Над ней реплики вопроса, как их слышит весь стол.
    /// </summary>
    public sealed class ChestsTableController : MonoBehaviour
    {
        private const int HumanIndex = 0;
        private static string HumanName => PlayerProfile.DisplayName;
        private bool _hubMatchReported;

        private static readonly string[] BotNamePool = { "Борис", "Вера", "Игорь", "Ольга", "Пётр", "Марина", "Сергей" };

        private static readonly Color TextColor = new Color(0.94f, 0.95f, 0.93f);
        private static readonly Color DimColor = new Color(0.94f, 0.95f, 0.93f, 0.55f);
        private static readonly Color AccentGlow = new Color(1f, 0.85f, 0.3f);
        private static readonly Color HumanColor = new Color(0.24f, 0.45f, 0.78f);
        private static readonly Color ChipColor = new Color(0.36f, 0.22f, 0.12f);
        private static readonly Color NoColor = new Color(0.95f, 0.42f, 0.36f);
        private static readonly Color RedSuitOnDark = new Color(1f, 0.45f, 0.42f);
        private static readonly Color RedSuitOnGold = new Color(0.7f, 0.08f, 0.08f);
        private static readonly Color ChipInk = new Color(0.16f, 0.12f, 0.06f);

        /// <summary>
        /// Обводка того, кого спрашивают. Голубая, а не золотая: золото уже значит «ходит»,
        /// а голубым горит выбранная карта в руке — это тот же смысл «вот его я выбрал».
        /// </summary>
        private static readonly Color TargetColor = new Color(0.35f, 0.95f, 1f);

        // Вертикаль строится сверху вниз фиксированными отступами: на широком окне Unity
        // ужимает высоту холста, и доли экрана начинают наезжать друг на друга.
        private const float SeatTopMargin = 146f;

        /// <summary>Середина доски посреди стола: полоса выбора по ходу игры, таблица мест в конце.</summary>
        private const float BoardTop = 480f;

        private const float BoardHalfHeight = 145f;
        private const float BoardHalfWidth = 360f;
        private const float BoardWidth = 1000f;
        private const float SeatToBoardGap = 30f;
        private const float FeltEdgeMargin = 56f;

        private const float LineHeight = 38f;
        private const float ChoiceRowHeight = 56f;

        /// <summary>Сколько места у соперника сбоку под сундучки — там, где в блефе лежал веер.</summary>
        private const float SideMaxWidth = 230f;

        private const float SideGap = 12f;
        private const float SeatArcBiasSmall = 1.3f;
        private const float SeatArcBiasBig = 1.55f;

        private const float HandRowBottom = 130f;
        private const float ComfortableHandScale = 1.05f;

        /// <summary>Карты спрошенного ранга приподнимаются в руке: видно, о чём сейчас речь.</summary>
        private const float AskedCardLift = 26f;

        private const float HandTopReach = AskedCardLift + CardView.Height * ComfortableHandScale * 0.5f
                                           + CardView.SelectedGlowMargin;

        /// <summary>Ниже этой черты места соперников не опускаются: там уже рука.</summary>
        private const float SeatFloor = HandRowBottom + HandTopReach + 24f;

        private const float MinHandStride = 0.42f;

        /// <summary>Своё место слева от веера и свои сундучки справа. Остальное — вееру.</summary>
        private const float HandSideReserve = 840f;

        private const float HumanChestsWidth = 220f;
        private const float HumanChestsHeight = 250f;
        private const float MinChestWidth = 90f;

        // Темп спокойный и не настраивается: в сундучки играют памятью, и каждому за столом
        // должно хватать времени увидеть, кто кого спросил, про какой ранг и что ответили.
        private const float BotThinkDelay = 1.5f;
        private const float BotStepDelay = 1.1f;

        /// <summary>Бот сначала «смотрит» на того, кого спросит, — тянется пунктир, — и только потом спрашивает.</summary>
        private const float TargetPause = 0.8f;

        private const float AnswerDelay = 1.1f;
        private const float HumanAnswerDelay = 0.7f;
        private const float AnswerHold = 1.6f;
        private const float TransferFlight = 1f;
        private const float ChestHold = 1.7f;
        private const float ChestFlight = 0.72f;

        /// <summary>Промах: ход уходит дальше не сразу — облачка успевают дочитать.</summary>
        private const float TurnPassHold = 1.3f;

        private const float PointerDuration = 0.35f;

        /// <summary>Облачко реплики: бумага, тёмные чернила, ранг выделен.</summary>
        private static readonly Color BubblePaper = new Color(0.98f, 0.96f, 0.9f);

        private static readonly Color BubbleInk = new Color(0.14f, 0.11f, 0.09f);
        private static readonly Color BubbleAccent = new Color(0.62f, 0.2f, 0.08f);
        private static readonly Color BubbleYes = new Color(0.1f, 0.48f, 0.2f);
        private static readonly Color BubbleNo = new Color(0.72f, 0.14f, 0.1f);

        /// <summary>Пауза перед сундучками, пришедшими прямо на раздаче: стол успевают увидеть.</summary>
        private const float DealSettle = 0.7f;

        private ChestsMatchSettings _settings = ChestsMatchSettings.Default;
        private Action _onExit;

        private Font _font;
        private ChestsGame _game;
        private SimpleChestsBot _bot;

        /// <summary>
        /// Чей ход сейчас показан на столе. Правила передают ход сразу после ошибки,
        /// а свет под фигурой — только после ответа и паузы перед следующим ходом.
        /// </summary>
        private int _visibleTurnPlayerIndex;

        private Text _status;
        private Text _progress;
        private Text _collectedRanks;

        private RectTransform _opponentsArea;
        private RectTransform _playerSeatArea;
        private RectTransform _humanChestsArea;
        private RectTransform _handRow;
        private RectTransform _board;
        private RectTransform _resultsArea;
        private RectTransform _choiceArea;
        private RectTransform _talkLayer;

        /// <summary>
        /// Слой пунктира. Лежит над соперниками, но под доской, рукой и своей фигурой: карты
        /// и фишки всегда поверх него, а не наоборот.
        /// </summary>
        private RectTransform _pointerLayer;
        private RectTransform _flyLayer;
        private RectTransform _chestLayer;
        private ChestView[] _chestViews;
        private ChestsCollectionOverlay _collectionOverlay;
        private int _inspectedPlayer = -1;
        private Coroutine _openingCollection;
        private Vector2 _lastCanvasSize;

        private Button _restartButton;

        /// <summary>
        /// Таблица мест в конце партии. Реплик посреди стола по ходу игры нет: их перекрывал
        /// пунктир, а облачка у голов и так показывают, кто что сказал.
        /// </summary>
        private readonly List<string> _results = new List<string>();

        private readonly List<Suit> _selectedSuits = new List<Suit>();

        private bool _waitingForHuman;
        private ChestsMove _pendingMove;

        /// <summary>Кого выбрал человек, пока ещё не назвал ранг.</summary>
        private int? _chosenTarget;

        /// <summary>Кого сейчас спрашивают на глазах у стола: у него горит обводка.</summary>
        private int? _focusTarget;

        /// <summary>
        /// Ранг, о котором сейчас спрашивают за столом, включая вопросы между соперниками.
        /// Такие карты в своей руке приподняты и обведены, пока показывается вопрос и ответ.
        /// </summary>
        private Rank? _highlightRank;

        /// <summary>
        /// Сколько сундучков у каждого уже показано. Правила кладут каре в сундучок сразу,
        /// а стол — только когда четыре карты долетели до места. Иначе стопка появлялась бы
        /// у игрока раньше, чем её показали.
        /// </summary>
        private int[] _visibleChests;

        /// <summary>
        /// Какую руку рисовать, пока идёт показ хода. Правила меняют руку сразу, а стол —
        /// когда карты долетели: иначе забранные карты появлялись бы в веере раньше полёта.
        /// Null — рисуем настоящую.
        /// </summary>
        private List<Card> _shownHand;

        private Sprite[] _playerPortraits;
        private float _handHalfWidth;

        public void Configure(ChestsMatchSettings settings, Action onExit)
        {
            _settings = settings;
            _onExit = onExit;
        }

        private void Start()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _bot = new SimpleChestsBot();

            BuildUi();
            StartNewGame();
        }

        private void StartNewGame()
        {
            _hubMatchReported = false;
            StopAllCoroutines();
            _collectionOverlay.CloseImmediate();
            _openingCollection = null;
            _inspectedPlayer = -1;
            Clear(_chestLayer);
            _chestViews = null;
            var seed = UnityEngine.Random.Range(0, int.MaxValue);
            var playerCount = Mathf.Max(ChestsMatchSettings.MinPlayers, _settings.PlayerCount);

            // Сначала лица, потом имена: бот с лицом реального человека называется так же.
            ShuffleAvatars(playerCount);
            _game = ChestsGame.Create(BuildPlayerNames(playerCount), _settings.BuildDeck(seed));
            _visibleTurnPlayerIndex = _game.CurrentPlayerIndex;
            _visibleChests = new int[playerCount];

            _results.Clear();
            _selectedSuits.Clear();
            _pendingMove = null;
            _waitingForHuman = false;
            _chosenTarget = null;
            _focusTarget = null;
            _shownHand = null;
            _restartButton.gameObject.SetActive(false);
            Clear(_flyLayer);
            ClearTalk();

            _board.gameObject.SetActive(true);
            StartCoroutine(GameLoop());
        }

        private void LateUpdate()
        {
            if (_game == null) return;
            var size = ((RectTransform)transform).rect.size;
            if ((size - _lastCanvasSize).sqrMagnitude < 1f) return;
            _lastCanvasSize = size;
            Refresh();
        }

        private void OnDestroy()
        {
            if (_collectionOverlay != null) _collectionOverlay.CloseImmediate();
        }

        // ---------- игровой цикл ----------

        private IEnumerator GameLoop()
        {
            // Сначала стол, потом сундучки с раздачи: иначе каре уходило бы над пустым сукном.
            Refresh();
            if (_game.Log.Count > 0)
            {
                yield return Wait(DealSettle);
                yield return PlayLog(0);
            }

            while (!_game.IsFinished)
            {
                var actor = _game.CurrentPlayerIndex;
                // Предыдущий ApplyMove уже закончил показ ответа, полёты и TurnPassHold.
                _visibleTurnPlayerIndex = actor;

                if (actor == HumanIndex)
                {
                    _waitingForHuman = true;
                    Refresh();

                    while (_pendingMove == null)
                    {
                        yield return null;
                    }

                    _waitingForHuman = false;
                    var move = _pendingMove;
                    _pendingMove = null;
                    yield return ApplyMove(actor, move);
                    continue;
                }

                Refresh();
                yield return Wait(_game.Phase == ChestsPhase.Asking ? BotThinkDelay : BotStepDelay);

                var botMove = _bot.ChooseMove(_game, actor);
                if (botMove == null)
                {
                    break;
                }

                if (botMove.Type == ChestsMoveType.Ask)
                {
                    ClearTalk();
                    _focusTarget = botMove.TargetIndex;
                    Refresh();
                    yield return DrawPointer(actor, botMove.TargetIndex, animate: true);
                    yield return Wait(TargetPause);
                }

                yield return ApplyMove(actor, botMove);
            }

            _waitingForHuman = false;
            _focusTarget = null;
            ClearTalk();
            for (var i = 0; i < _visibleChests.Length; i++)
            {
                _visibleChests[i] = _game.Players[i].ChestCount;
            }

            Refresh();
            ShowResult();
        }

        private IEnumerator ApplyMove(int actor, ChestsMove move)
        {
            var from = _game.Log.Count;
            _shownHand = new List<Card>(_game.Players[HumanIndex].Hand);
            if (!_game.TryApplyMove(actor, move, out var error))
            {
                // Сюда попадать не должны: ходы собираются по тем же правилам, что и проверяются.
                _progress.text = error;
                _shownHand = null;
                yield break;
            }

            _chosenTarget = null;
            _selectedSuits.Clear();
            Refresh();

            yield return PlayLog(from);
            _shownHand = null;

            // Вопрос кончился — удачей или промахом. Обводку гасим, реплики на доске
            // оставляем: по ним видно, что сейчас было.
            var questionOver = _game.Phase == ChestsPhase.Asking || _game.IsFinished;
            if (questionOver)
            {
                _focusTarget = null;
            }

            Refresh();

            if (!questionOver || _game.IsFinished)
            {
                yield break;
            }

            // Промах: ход уходит не сразу — облачка ещё висят, пусть стол дочитает. Надписи
            // «ход переходит к ...» нет: кто ходит следующим, видно по свету под фигурой.
            if (_game.CurrentPlayerIndex != actor)
            {
                yield return Wait(TurnPassHold);
            }

            ClearTalk();
        }

        /// <summary>Показывает всё, что случилось за ход, строку лога за строкой.</summary>
        private IEnumerator PlayLog(int from)
        {
            for (var i = from; i < _game.Log.Count; i++)
            {
                var entry = _game.Log[i];
                switch (entry.Kind)
                {
                    case ChestsLogKind.Ask:
                        _focusTarget = entry.TargetIndex;
                        _highlightRank = entry.Rank;

                        yield return SayExchange(entry,
                            _game.Players[entry.TargetIndex!.Value].Name + ", есть {0}?",
                            ChestsRules.RankPluralNominative(entry.Rank!.Value),
                            entry.Success == true ? "Есть!" : "Нет");
                        break;

                    case ChestsLogKind.CountGuess:
                        yield return SayExchange(entry, "{0}?",
                            ChestsRules.Capitalize(ChestsRules.CountPhrase(entry.Rank!.Value, entry.Count ?? 0)),
                            entry.Success == true ? "Да!" : "Нет");
                        break;

                    case ChestsLogKind.SuitsGuess:
                        yield return SayExchange(entry, "{0}?",
                            ChestsRules.Capitalize(ChestsRules.SuitsPhrase(entry.Suits)),
                            entry.Success == true ? "Да!" : "Нет");
                        break;

                    case ChestsLogKind.Transfer:
                        yield return FlyTransfer(entry);
                        break;

                    case ChestsLogKind.Chest:
                        yield return ShowChest(entry);
                        break;
                }
            }
        }

        /// <summary>
        /// Реплика и ответ. Вопрос звучит облачком у того, кто спрашивает, ответ — у того,
        /// кого спросили. Ответ приходит не сразу: стол успевает прочитать вопрос. Главное
        /// слово — ранг, число или масти — выделено: его и надо запомнить.
        /// </summary>
        private IEnumerator SayExchange(ChestsLogEntry entry, string pattern, string key, string answer)
        {
            var asker = entry.PlayerIndex;
            var target = entry.TargetIndex!.Value;
            var yes = entry.Success == true;

            Refresh();

            if (_pointer != (asker, target))
            {
                yield return DrawPointer(asker, target, animate: false);
            }

            ReplaceBubble(ref _question, asker, string.Format(pattern, Paint("<b>" + key + "</b>", BubbleAccent)));
            ReplaceBubble(ref _answer, -1, null);

            yield return Wait(asker == HumanIndex ? HumanAnswerDelay : AnswerDelay);

            ReplaceBubble(ref _answer, target, Paint("<b>" + answer + "</b>", yes ? BubbleYes : BubbleNo));

            yield return Wait(AnswerHold);
        }

        private static WaitForSeconds Wait(float seconds) => new WaitForSeconds(seconds);

        // ---------- облачка и пунктир ----------

        /// <summary>Облачко вопроса и облачко ответа. Одновременно висит не больше одного каждого.</summary>
        private RectTransform _question;

        private RectTransform _answer;

        /// <summary>Между кем сейчас тянется пунктир. Null — пунктира нет.</summary>
        private (int, int)? _pointer;

        private RectTransform _pointerDots;

        private void ClearTalk()
        {
            Clear(_talkLayer);
            Clear(_pointerLayer);
            _highlightRank = null;
            _question = null;
            _answer = null;
            _pointer = null;
            _pointerDots = null;
        }

        private void ReplaceBubble(ref RectTransform bubble, int speaker, string text)
        {
            if (bubble != null)
            {
                Destroy(bubble.gameObject);
                bubble = null;
            }

            if (text != null)
            {
                bubble = CreateBubble(speaker, text);
            }

            MaskPointer();
        }

        /// <summary>
        /// Облачко реплики у говорящего. Где именно — решает <see cref="PlaceBubble"/>: так,
        /// чтобы оно не закрывало ни места, ни сундучки, ни карты.
        /// </summary>
        private RectTransform CreateBubble(int speaker, string content)
        {
            var go = new GameObject("Bubble", typeof(RectTransform));
            go.transform.SetParent(_talkLayer, false);
            var root = (RectTransform)go.transform;
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = Vector2.zero;

            var text = CreateText(root, content, 27, TextAnchor.MiddleCenter);
            text.supportRichText = true;
            text.color = BubbleInk;
            var width = Mathf.Min(text.preferredWidth, 360f) + 36f;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            ((RectTransform)text.transform).sizeDelta = new Vector2(width - 30f, 200f);
            var height = Mathf.Max(52f, text.preferredHeight + 20f);
            ((RectTransform)text.transform).sizeDelta = new Vector2(width - 30f, height);

            var size = new Vector2(width, height);
            root.anchoredPosition = PlaceBubble(speaker, size, out var tailAt);
            root.sizeDelta = size;

            // Хвостик — повёрнутый квадратик у края, обращённого к говорящему. Лежит под
            // телом облачка, наружу торчит только уголок.
            var tail = new GameObject("Tail", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            tail.transform.SetParent(root, false);
            var tailRect = (RectTransform)tail.transform;
            tailRect.anchorMin = tailRect.anchorMax = tailRect.pivot = new Vector2(0.5f, 0.5f);
            tailRect.sizeDelta = new Vector2(20f, 20f);
            tailRect.anchoredPosition = tailAt;
            tailRect.localRotation = Quaternion.Euler(0f, 0f, 45f);
            var tailImage = tail.GetComponent<Image>();
            tailImage.sprite = UiTextures.RoundedRect(24, 24, 3f, BubblePaper);
            tailImage.raycastTarget = false;

            var body = new GameObject("Body", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Shadow));
            body.transform.SetParent(root, false);
            var bodyRect = (RectTransform)body.transform;
            bodyRect.anchorMin = bodyRect.anchorMax = bodyRect.pivot = new Vector2(0.5f, 0.5f);
            bodyRect.sizeDelta = new Vector2(width, height);
            var bodyImage = body.GetComponent<Image>();
            bodyImage.sprite = UiTextures.RoundedRect(64, 64, 18f, BubblePaper);
            bodyImage.type = Image.Type.Sliced;
            bodyImage.raycastTarget = false;
            var shadow = body.GetComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.35f);
            shadow.effectDistance = new Vector2(2f, -3f);

            text.transform.SetAsLastSibling();

            StartCoroutine(PopIn(root));
            return root;
        }

        /// <summary>
        /// Куда поставить облачко, чтобы оно ничего не закрыло. Своё — всегда над рукой, правее
        /// своей фигуры: сбоку от головы оно ложилось на приподнятые крайние карты, а над
        /// головой бывает крайний соперник. У соперника пробуем по очереди: к центру стола на
        /// уровне головы, под местом, наружу, над головой — и берём то, где облачко меньше всего
        /// наезжает на места, сундучки, руку, полосу выбора и второе облачко.
        /// </summary>
        private Vector2 PlaceBubble(int speaker, Vector2 size, out Vector2 tailAt)
        {
            var head = HeadOf(speaker);

            if (speaker == HumanIndex)
            {
                var canvasHeight = ((RectTransform)transform).rect.height;
                var bottom = -(canvasHeight - (HandRowBottom + HandTopReach + 14f));
                var left = head.x + SeatFigure.Width * 0.5f - 18f;
                tailAt = new Vector2(-size.x * 0.5f + 26f, -size.y * 0.5f + 3f);
                return ClampToFelt(new Vector2(left + size.x * 0.5f, bottom + size.y * 0.5f), size);
            }

            var opponents = _game.Players.Count - 1;
            var layout = BuildSeatLayout(opponents);
            var seat = SeatPosition(speaker - 1, opponents, layout);
            var scale = layout.Scale;
            var inward = seat.x > 0f ? -1f : 1f;
            var reach = (SeatFigure.Width * 0.5f + 22f) * scale + 6f + size.x * 0.5f;
            var blockBottom = seat.y - SeatFigure.BlockHeight * scale;

            // Хвостик сбоку смотрит на голову, сверху и снизу — на середину фигуры.
            var candidates = new[]
            {
                (head + new Vector2(inward * reach, 6f), new Vector2(-inward * (size.x * 0.5f - 2f), -4f)),
                (new Vector2(seat.x, blockBottom - 10f - size.y * 0.5f), new Vector2(0f, size.y * 0.5f - 2f)),
                (head + new Vector2(-inward * reach, 6f), new Vector2(inward * (size.x * 0.5f - 2f), -4f)),
                (new Vector2(seat.x, seat.y + 8f + size.y * 0.5f), new Vector2(0f, -size.y * 0.5f + 2f)),
            };

            var occupied = OccupiedRects();
            var best = Vector2.zero;
            tailAt = Vector2.zero;
            var bestOverlap = float.MaxValue;

            foreach (var (wanted, tail) in candidates)
            {
                var position = ClampToFelt(wanted, size);
                var area = new Rect(position - size * 0.5f, size);

                var overlap = 0f;
                foreach (var rect in occupied)
                {
                    overlap += Overlap(area, rect);
                }

                // Равные варианты решает порядок: к центру стола — самый естественный.
                if (overlap < bestOverlap - 1f)
                {
                    bestOverlap = overlap;
                    best = position;
                    tailAt = tail;

                    // Облачко над или под местом могло упереться в край стола — хвостик всё
                    // равно смотрит на говорящего.
                    if (Mathf.Abs(tail.y) > 10f)
                    {
                        tailAt.x = Mathf.Clamp(seat.x - position.x, -size.x * 0.5f + 16f, size.x * 0.5f - 16f);
                    }
                }
            }

            return best;
        }

        /// <summary>Облачко целиком на сукне и ниже строк статуса.</summary>
        private Vector2 ClampToFelt(Vector2 position, Vector2 size)
        {
            var root = (RectTransform)transform;
            var halfWidth = root.rect.width * 0.5f - 50f - size.x * 0.5f;
            position.x = Mathf.Clamp(position.x, -halfWidth, halfWidth);
            position.y = Mathf.Clamp(position.y, -(root.rect.height - 50f - size.y * 0.5f), -(110f + size.y * 0.5f));
            return position;
        }

        /// <summary>
        /// Что на столе закрывать нельзя, в координатах слоя облачков: места соперников вместе
        /// со стопкой рубашек, их сундучки, своё место, рука и свои сундучки, полоса выбора,
        /// строки статуса и уже висящие облачка.
        /// </summary>
        private List<Rect> OccupiedRects()
        {
            var rects = new List<Rect>();
            var opponents = _game.Players.Count - 1;
            var layout = BuildSeatLayout(opponents);
            var scale = layout.Scale;

            for (var seat = 0; seat < opponents; seat++)
            {
                var position = SeatPosition(seat, opponents, layout);
                var halfWidth = (SeatFigure.Width * 0.5f + 22f) * scale;
                var bottom = position.y - SeatFigure.BlockHeight * scale;
                rects.Add(Rect.MinMaxRect(position.x - halfWidth, bottom, position.x + halfWidth, position.y));

            }

            if (_chestViews != null)
                foreach (var chest in _chestViews)
                {
                    if (chest == null) continue;
                    var bounds = LocalRect(chest.Rect);
                    // Count and compact rank labels are just below the art canvas.
                    bounds.yMin -= 46f;
                    rects.Add(bounds);
                }

            rects.Add(LocalRect(_playerSeatArea));
            rects.Add(LocalRect(_humanChestsArea));

            var canvasHeight = ((RectTransform)transform).rect.height;
            rects.Add(Rect.MinMaxRect(-_handHalfWidth, -canvasHeight, _handHalfWidth,
                -(canvasHeight - HandRowBottom - HandTopReach)));

            if (TryChoiceBounds(out var bar))
            {
                rects.Add(bar);
            }

            rects.Add(Rect.MinMaxRect(-10000f, -138f, 10000f, 0f));

            foreach (var bubble in new[] { _question, _answer })
            {
                if (bubble != null)
                {
                    rects.Add(BubbleRect(bubble, 12f));
                }
            }

            return rects;
        }

        private Rect LocalRect(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Vector2 min = _talkLayer.InverseTransformPoint(corners[0]);
            Vector2 max = _talkLayer.InverseTransformPoint(corners[2]);
            return Rect.MinMaxRect(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y), Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
        }

        private static Rect BubbleRect(RectTransform bubble, float margin) =>
            new Rect(bubble.anchoredPosition - bubble.sizeDelta * 0.5f - Vector2.one * margin,
                bubble.sizeDelta + Vector2.one * margin * 2f);

        private static float Overlap(Rect a, Rect b)
        {
            var width = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
            var height = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
            return width > 0f && height > 0f ? width * height : 0f;
        }

        private static IEnumerator PopIn(RectTransform rect)
        {
            const float duration = 0.16f;
            var elapsed = 0f;
            while (elapsed < duration && rect != null)
            {
                elapsed += Time.deltaTime;
                var k = Mathf.Clamp01(elapsed / duration);
                rect.localScale = Vector3.one * Mathf.Lerp(0.7f, 1f, 1f - (1f - k) * (1f - k));
                yield return null;
            }

            if (rect != null)
            {
                rect.localScale = Vector3.one;
            }
        }

        /// <summary>
        /// Пунктир от того, кто спрашивает, к тому, кого спросили: золотой у спрашивающего,
        /// голубой у адресата — теми же цветами, что свет и обводка. Идёт дугой через середину
        /// стола, а не напрямик: напрямик он резал бы лица соседей по дуге мест.
        /// </summary>
        private IEnumerator DrawPointer(int from, int to, bool animate)
        {
            if (_pointerDots != null)
            {
                Destroy(_pointerDots.gameObject);
            }

            _pointerDots = new GameObject("Pointer", typeof(RectTransform)).GetComponent<RectTransform>();
            _pointerDots.SetParent(_pointerLayer, false);
            _pointerDots.anchorMin = _pointerDots.anchorMax = _pointerDots.pivot = new Vector2(0.5f, 0.5f);
            _pointerDots.sizeDelta = Vector2.zero;
            _pointer = (from, to);

            var a = HeadOf(from) + new Vector2(0f, -30f);
            var b = HeadOf(to) + new Vector2(0f, -30f);
            var bend = Vector2.Lerp((a + b) * 0.5f, new Vector2(0f, -BoardTop), 0.55f);

            // Концы отступают от фигур, чтобы точки не ложились на лица.
            var start = Bezier(a, bend, b, 0.12f);
            var end = Bezier(a, bend, b, 0.88f);
            var length = Vector2.Distance(start, bend) + Vector2.Distance(bend, end);
            var count = Mathf.Clamp(Mathf.RoundToInt(length / 30f), 6, 40);

            var dots = new List<GameObject>();
            for (var i = 0; i < count; i++)
            {
                var t = i / (float)(count - 1);
                var dot = new GameObject("Dot", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                dot.transform.SetParent(_pointerDots, false);
                var rect = (RectTransform)dot.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Bezier(start, bend, end, t);
                var size = Mathf.Lerp(8f, 13f, t);
                rect.sizeDelta = new Vector2(size, size);

                var image = dot.GetComponent<Image>();
                image.sprite = UiTextures.Circle(32, Color.white);
                var color = Color.Lerp(AccentGlow, TargetColor, t);
                image.color = new Color(color.r, color.g, color.b, 0.9f);
                image.raycastTarget = false;

                dot.SetActive(!animate);
                dots.Add(dot);
            }

            MaskPointer();

            if (!animate)
            {
                yield break;
            }

            var elapsed = 0f;
            while (elapsed < PointerDuration)
            {
                elapsed += Time.deltaTime;
                var shown = Mathf.CeilToInt(Mathf.Clamp01(elapsed / PointerDuration) * dots.Count);
                for (var i = 0; i < dots.Count; i++)
                {
                    if (dots[i] != null)
                    {
                        dots[i].SetActive(i < shown);
                    }
                }

                yield return null;
            }
        }

        private static Vector2 Bezier(Vector2 a, Vector2 control, Vector2 b, float t)
        {
            var u = 1f - t;
            return u * u * a + 2f * u * t * control + t * t * b;
        }

        /// <summary>Где у игрока голова — от неё тянутся пунктир и облачка.</summary>
        private Vector2 HeadOf(int playerIndex)
        {
            if (playerIndex == HumanIndex)
            {
                Vector2 seat = _flyLayer.InverseTransformPoint(_playerSeatArea.position);
                return seat + new Vector2(0f, SeatFigure.BlockHeight * 0.5f - 55f);
            }

            var opponents = _game.Players.Count - 1;
            var layout = BuildSeatLayout(opponents);
            var position = SeatPosition(playerIndex - 1, opponents, layout);
            return new Vector2(position.x, position.y - 55f * layout.Scale);
        }

        private static string Paint(string text, Color color) =>
            "<color=#" + ColorUtility.ToHtmlStringRGBA(color) + ">" + text + "</color>";

        // ---------- полёты ----------

        /// <summary>
        /// Угаданные карты перелетают лицом вверх: их назвали вслух, прятать нечего. Летят от
        /// того, кто отдаёт, к тому, кто спрашивал, — так видно, кто кому.
        /// </summary>
        private IEnumerator FlyTransfer(ChestsLogEntry entry)
        {
            var from = AnchorOf(entry.TargetIndex!.Value);
            var to = AnchorOf(entry.PlayerIndex);

            // Отдаёшь сам — карты уходят из веера в тот же миг, как взлетели.
            if (entry.TargetIndex == HumanIndex)
            {
                _shownHand = null;
                RefreshHand();
            }

            var views = new List<CardView>();
            for (var i = 0; i < entry.Cards.Count; i++)
            {
                var view = CardView.Create(_flyLayer, _font, 0.8f);
                view.ShowFace(entry.Cards[i]);
                view.SetPlain();
                view.SetRestTransform(from, 0f);
                views.Add(view);
            }

            const float stagger = 0.08f;
            var total = TransferFlight + stagger * (views.Count - 1);
            var elapsed = 0f;
            while (elapsed < total)
            {
                elapsed += Time.deltaTime;
                for (var i = 0; i < views.Count; i++)
                {
                    var k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((elapsed - i * stagger) / TransferFlight));
                    var spread = new Vector2((i - (views.Count - 1) * 0.5f) * 34f, 0f);
                    views[i].Rect.anchoredPosition = Vector2.Lerp(from + spread, to + spread, k)
                                                     + new Vector2(0f, Mathf.Sin(k * Mathf.PI) * 40f);
                    views[i].Rect.localRotation = Quaternion.Euler(0f, 0f, (1f - k) * (i - 1f) * 8f);
                }

                yield return null;
            }

            Clear(_flyLayer);

            // Долетели — теперь и в веере. Если из них сложилось каре, оно уйдёт из руки
            // прямо отсюда: следующий шаг показа его и вынесет на середину стола.
            _shownHand = null;
            RefreshHand();
        }

        /// <summary>
        /// Каре показывается лицом вверх, складывается в пачку и уходит за бортик
        /// настоящего сундука владельца. Счёт меняется только после приземления.
        /// </summary>
        private IEnumerator ShowChest(ChestsLogEntry entry)
        {
            var owner = entry.PlayerIndex;

            // Сундучок показывают посреди стола — пунктир и облачка тут только мешают, а
            // вопрос к этому моменту уже закончен.
            ClearTalk();
            _board.gameObject.SetActive(false);

            var center = new Vector2(0f, -BoardTop);
            var who = owner == HumanIndex ? "Тебе" : BluffRules.NameDative(_game.Players[owner].Name);
            var caption = CreateText(_flyLayer,
                who + " — сундучок: " + ChestsRules.RankPluralNominative(entry.Rank!.Value)
                + (entry.AtDeal ? ", прямо с раздачи" : string.Empty),
                34, TextAnchor.MiddleCenter);
            caption.color = AccentGlow;
            caption.fontStyle = FontStyle.Bold;
            Place((RectTransform)caption.transform, new Vector2(0.5f, 0.5f), center + new Vector2(0f, 120f), new Vector2(1000f, 48f));

            const float scale = 0.9f;
            var step = CardView.Width * scale + 16f;
            var start = -1.5f * step;
            var from = AnchorOf(owner);

            var cards = new List<Card>(entry.Cards);
            cards.Sort((left, right) => left.Suit.CompareTo(right.Suit));

            var views = new List<CardView>();
            for (var i = 0; i < cards.Count; i++)
            {
                var view = CardView.Create(_flyLayer, _font, scale);
                view.ShowFace(cards[i]);
                view.SetPlain();
                var landing = center + new Vector2(start + i * step, -18f);
                view.SetRestTransform(landing, 0f);
                view.PlayFlyIn(from - landing);
                views.Add(view);
            }

            yield return new WaitForSeconds(CardView.FlightDuration);
            yield return Wait(ChestHold);

            Destroy(caption.gameObject);

            var starts = new List<Vector2>();
            foreach (var view in views)
            {
                view.DisableHover();
                starts.Add(view.Rect.anchoredPosition);
            }

            // Сначала видно именно четыре карты, затем они собираются одной пачкой.
            const float gatherDuration = 0.38f;
            var elapsed = 0f;
            while (elapsed < gatherDuration)
            {
                elapsed += Time.deltaTime;
                var k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / gatherDuration));
                for (var i = 0; i < views.Count; i++)
                {
                    var rect = views[i].Rect;
                    rect.anchoredPosition = Vector2.Lerp(starts[i], center + new Vector2(i * 2f, -18f - i * 3f), k);
                    rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(0f, (i - 1.5f) * 2f, k));
                }
                yield return null;
            }

            var chest = _chestViews[owner];
            yield return chest.OpenForDeposit();
            // Turn the completed set over as a single stack; the same green skin is
            // currently shared by everyone, independent of the chest artwork.
            var stack = ChestCardStackView.Create(_flyLayer, CardView.Width * scale);
            stack.Rect.anchoredPosition = center + new Vector2(3f, -22f);
            foreach (var view in views) Destroy(view.gameObject);

            var stackStart = stack.Rect.anchoredPosition;
            var landingScale = Mathf.Clamp(chest.Rect.rect.width * 235f / 1254f / (CardView.Width * scale), 0.12f, 0.48f);
            elapsed = 0f;
            while (elapsed < ChestFlight)
            {
                elapsed += Time.deltaTime;
                var k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / ChestFlight));
                var target = (Vector2)_flyLayer.InverseTransformPoint(chest.DepositWorldPosition);
                stack.Rect.anchoredPosition = Vector2.Lerp(stackStart, target + new Vector2(0f, 28f), k)
                                             + Vector2.up * (Mathf.Sin(k * Mathf.PI) * 70f);
                stack.Rect.localScale = Vector3.one * Mathf.Lerp(1f, landingScale, k);
                stack.Rect.localRotation = Quaternion.Euler(0f, 0f, -12f * Mathf.Sin(k * Mathf.PI));
                yield return null;
            }

            // Reparent before sinking: real front/lid layers occlude the arriving pack.
            stack.Rect.SetParent(chest.ContentsLayer, true);
            var aboveRim = stack.Rect.position;
            elapsed = 0f;
            const float sinkDuration = 0.24f;
            while (elapsed < sinkDuration)
            {
                elapsed += Time.deltaTime;
                var k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / sinkDuration));
                var belowRim = chest.DepositWorldPosition + chest.Rect.TransformVector(new Vector3(0f, -chest.Rect.rect.height * 0.25f, 0f));
                stack.Rect.position = Vector3.Lerp(aboveRim, belowRim, k);
                yield return null;
            }
            Destroy(stack.gameObject);
            Clear(_flyLayer);
            _visibleChests[owner] = Mathf.Min(_visibleChests[owner] + 1, _game.Players[owner].ChestCount);
            chest.SetCollection(_game.Players[owner].Chests, _visibleChests[owner]);
            RefreshStatus();
            RefreshOpenCollection();
            yield return chest.FinishDeposit(_visibleChests[owner]);
            _board.gameObject.SetActive(true);
            Refresh();
        }

        /// <summary>Откуда летят и куда прилетают карты: к руке человека или к месту соперника.</summary>
        private Vector2 AnchorOf(int playerIndex)
        {
            if (playerIndex == HumanIndex)
            {
                return _flyLayer.InverseTransformPoint(_handRow.position);
            }

            var opponents = _game.Players.Count - 1;
            var layout = BuildSeatLayout(opponents);
            var seat = SeatPosition(playerIndex - 1, opponents, layout);
            return new Vector2(seat.x, seat.y - layout.BlockHeight * 0.4f);
        }

        // ---------- выбор человека ----------

        private void ChooseTarget(int playerIndex)
        {
            if (_inspectedPlayer >= 0 || !_waitingForHuman || _game.Phase != ChestsPhase.Asking || playerIndex == HumanIndex)
            {
                return;
            }

            _chosenTarget = playerIndex;
            ClearTalk();
            StartCoroutine(DrawPointer(HumanIndex, playerIndex, animate: false));
            Refresh();
        }

        private void ChooseRank(Rank rank)
        {
            if (_inspectedPlayer >= 0 || !_waitingForHuman || !_chosenTarget.HasValue)
            {
                return;
            }

            _pendingMove = ChestsMove.Ask(_chosenTarget.Value, rank);
        }

        private void ChooseCount(int count)
        {
            if (_inspectedPlayer < 0 && _waitingForHuman && _game.Phase == ChestsPhase.GuessingCount)
            {
                _pendingMove = ChestsMove.GuessCount(count);
            }
        }

        /// <summary>
        /// Масть отмечается и снимается повторным нажатием. Когда нужна одна, новая просто
        /// заменяет старую; когда их уже сколько надо, новая вытесняет самую раннюю — так
        /// не приходится сначала снимать, а потом ставить.
        /// </summary>
        private void ToggleSuit(Suit suit)
        {
            if (_inspectedPlayer >= 0 || !_waitingForHuman || _game.Phase != ChestsPhase.GuessingSuits)
            {
                return;
            }

            if (_selectedSuits.Contains(suit))
            {
                _selectedSuits.Remove(suit);
            }
            else
            {
                if (_selectedSuits.Count >= _game.GuessedCount.GetValueOrDefault())
                {
                    _selectedSuits.RemoveAt(0);
                }

                _selectedSuits.Add(suit);
            }

            Refresh();
        }

        /// <summary>
        /// Набор уходит одной кнопкой, а не по масти: иначе стол узнавал бы, какая масть
        /// совпала, и правило «весь набор или ничего» перестало бы работать.
        /// </summary>
        private void ConfirmSuits()
        {
            if (_inspectedPlayer < 0 && _waitingForHuman && _game.Phase == ChestsPhase.GuessingSuits
                                 && _selectedSuits.Count == _game.GuessedCount.GetValueOrDefault())
            {
                _pendingMove = ChestsMove.GuessSuits(new List<Suit>(_selectedSuits));
            }
        }

        // ---------- отрисовка ----------

        private void Refresh()
        {
            if (!_hubMatchReported && _game.IsFinished)
            {
                _hubMatchReported = true;
                HubMatchReporter.ReportMatchCompleted("chests", Contains(_game.Winners, HumanIndex),
                    _game.Players[HumanIndex].ChestCount);
            }
            RefreshStatus();

            // Рука считается раньше мест: своё место и свои сундучки встают вплотную к её краям.
            RefreshHand();
            RefreshOpponents();
            RefreshPlayerSeat();
            RefreshHumanChests();
            RefreshChests();
            RefreshOpenCollection();
            RefreshResults();
            RefreshChoices();
            MaskPointer();
        }

        /// <summary>
        /// Пунктир виден только на пустом сукне. Точки, попавшие на места с именами, сундучки,
        /// руку, свою фигуру, полосу выбора или облачко, гасим: пунктир как будто уходит под
        /// них и выходит с другой стороны. Гасим картинку, а не сам объект — объектом
        /// управляет анимация, которая тянет пунктир.
        /// </summary>
        private void MaskPointer()
        {
            if (_pointerDots == null)
            {
                return;
            }

            var blocked = OccupiedRects();
            foreach (Transform dot in _pointerDots)
            {
                var point = ((RectTransform)dot).anchoredPosition;
                var inside = false;
                foreach (var rect in blocked)
                {
                    if (rect.Contains(point))
                    {
                        inside = true;
                        break;
                    }
                }

                dot.GetComponent<Image>().enabled = !inside;
            }
        }

        /// <summary>Где на экране полоса выбора — в координатах слоя пунктира, с запасом по краям.</summary>
        private bool TryChoiceBounds(out Rect bounds)
        {
            const float margin = 18f;
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            var corners = new Vector3[4];
            var found = false;

            foreach (Transform child in _choiceArea)
            {
                if (!child.gameObject.activeSelf)
                {
                    continue;
                }

                ((RectTransform)child).GetWorldCorners(corners);
                foreach (var corner in corners)
                {
                    Vector2 local = _pointerLayer.InverseTransformPoint(corner);
                    min = Vector2.Min(min, local);
                    max = Vector2.Max(max, local);
                }

                found = true;
            }

            bounds = found
                ? Rect.MinMaxRect(min.x - margin, min.y - margin, max.x + margin, max.y + margin)
                : default;
            return found;
        }

        private void RefreshStatus()
        {
            // Считаем показанные, а не собранные правилами: счёт растёт, когда каре долетело.
            var collected = 0;
            for (var i = 0; i < _visibleChests.Length; i++)
            {
                collected += Mathf.Min(_visibleChests[i], _game.Players[i].ChestCount);
            }

            _progress.text = "Сундучков собрано: " + collected + " из " + _game.ChestsTotal;
            var ranks = new List<Rank>();
            for (var owner = 0; owner < _game.Players.Count; owner++)
                foreach (var rank in VisibleRanks(owner))
                    if (!ranks.Contains(rank)) ranks.Add(rank);
            ranks.Sort();
            var shortNames = new List<string>();
            foreach (var rank in ranks) shortNames.Add(RankShort(rank));
            _collectedRanks.text = "Собраны: " + (shortNames.Count == 0 ? "—" : string.Join(" · ", shortNames));

            // По ходу партии верхняя строка молчит: ни «ходит такой-то», ни «твой ход», ни пересказа
            // вопросов — всё это перегружало стол. Кто ходит, видно по свету под фигурой, кто
            // кого спросил — по пунктиру и облачкам, что выбирать — по полосе посреди стола.
            // Говорит она только в конце: итог партии.
            if (!_game.IsFinished)
            {
                _status.text = string.Empty;
            }
        }

        private void RefreshResults()
        {
            Clear(_resultsArea);

            for (var i = 0; i < _results.Count; i++)
            {
                var text = CreateText(_resultsArea, _results[i], 28, TextAnchor.MiddleCenter);
                text.supportRichText = true;
                var rect = (RectTransform)text.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = new Vector2(0f, -i * LineHeight);
                rect.sizeDelta = new Vector2(BoardWidth, LineHeight);
            }
        }

        private void RefreshChoices()
        {
            Clear(_choiceArea);
            if (!_waitingForHuman || _game.IsFinished)
            {
                return;
            }

            switch (_game.Phase)
            {
                case ChestsPhase.Asking:
                    if (_chosenTarget.HasValue)
                    {
                        BuildRankChoices();
                    }
                    else
                    {
                        var hint = CreateText(_choiceArea, "Кого спросим?", 34, TextAnchor.MiddleCenter);
                        hint.color = DimColor;
                        Place((RectTransform)hint.transform, new Vector2(0.5f, 0f), new Vector2(0f, ChoiceRowHeight),
                            new Vector2(BoardWidth, 50f));
                    }

                    break;

                case ChestsPhase.GuessingCount:
                    BuildCountChoices();
                    break;

                case ChestsPhase.GuessingSuits:
                    BuildSuitChoices();
                    break;
            }
        }

        /// <summary>Ранги в один ряд, а если их больше девяти — в два, иначе фишки становятся мелкими.</summary>
        private void BuildRankChoices()
        {
            var ranks = _game.AskableRanks(HumanIndex);
            var perRow = ranks.Count <= 9 ? ranks.Count : Mathf.CeilToInt(ranks.Count / 2f);
            const float spacing = 76f;

            for (var i = 0; i < ranks.Count; i++)
            {
                var rank = ranks[i];
                var row = i / perRow;
                var inRow = Mathf.Min(perRow, ranks.Count - row * perRow);
                var column = i % perRow;
                var x = (column - (inRow - 1) * 0.5f) * spacing;
                var y = ChoiceRowHeight * 0.5f + (ranks.Count > perRow ? (1 - row) * (ChoiceRowHeight + 10f) : 0f);

                CreateChip(_choiceArea, RankShort(rank), new Vector2(x, y), new Vector2(spacing - 10f, ChoiceRowHeight),
                    () => ChooseRank(rank));
            }
        }

        /// <summary>Числа подписаны целиком — «два вальта», — как их и говорят вслух.</summary>
        private void BuildCountChoices()
        {
            var options = _game.CountOptions(HumanIndex);
            var rank = _game.AskedRank!.Value;
            const float spacing = 230f;

            for (var i = 0; i < options.Count; i++)
            {
                var count = options[i];
                var x = (i - (options.Count - 1) * 0.5f) * spacing;
                CreateChip(_choiceArea, ChestsRules.CountPhrase(rank, count), new Vector2(x, ChoiceRowHeight * 0.5f),
                    new Vector2(spacing - 16f, ChoiceRowHeight), () => ChooseCount(count));
            }
        }

        private void BuildSuitChoices()
        {
            var need = _game.GuessedCount.GetValueOrDefault();
            const float spacing = 88f;
            var suits = new[] { Suit.Clubs, Suit.Diamonds, Suit.Hearts, Suit.Spades };
            var left = -(suits.Length * spacing + 230f) * 0.5f + spacing * 0.5f;

            for (var i = 0; i < suits.Length; i++)
            {
                var suit = suits[i];
                var chip = CreateChip(_choiceArea, CardView.SuitText(suit), new Vector2(left + i * spacing, ChoiceRowHeight * 0.5f),
                    new Vector2(spacing - 12f, ChoiceRowHeight + 8f), () => ToggleSuit(suit));

                var selected = _selectedSuits.Contains(suit);
                var red = suit == Suit.Diamonds || suit == Suit.Hearts;
                SetChipSelected(chip, selected);

                var label = chip.GetComponentInChildren<Text>();
                label.fontSize = 40;
                label.color = red ? (selected ? RedSuitOnGold : RedSuitOnDark) : selected ? ChipInk : TextColor;
            }

            var ready = _selectedSuits.Count == need;
            var confirm = CreateButton(_choiceArea, "Спросить", new Vector2(0.5f, 0f),
                new Vector2(left + suits.Length * spacing + 100f, ChoiceRowHeight * 0.5f), new Vector2(200f, ChoiceRowHeight + 8f),
                new Color(0.21f, 0.45f, 0.29f), ConfirmSuits);
            confirm.interactable = ready;
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

                // Сундучки уходят в свою сторону экрана: у левых соперников влево, у правых вправо.
                var side = position.x < 0f ? -1f : 1f;
                var container = BuildSeatBlock(_opponentsArea, player, layout.Scale, layout.SideWidth, side);

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

            var block = BuildSeatBlock(_playerSeatArea, _game.Players[HumanIndex], 1f, 0f, 1f);
            block.anchorMin = block.anchorMax = new Vector2(0.5f, 0.5f);
            block.pivot = new Vector2(0.5f, 0.5f);
            block.anchoredPosition = Vector2.zero;
        }

        /// <summary>Свои сундучки лежат справа от руки — слева уже сидит своя фигура.</summary>
        private void RefreshHumanChests()
        {
            var canvasHalf = ((RectTransform)transform).rect.width * 0.5f;
            var x = Mathf.Min(_handHalfWidth + 24f + HumanChestsWidth * 0.5f, canvasHalf - 60f - HumanChestsWidth * 0.5f);
            _humanChestsArea.anchoredPosition = new Vector2(x, 60f + HumanChestsHeight * 0.5f);
        }

        /// <summary>Persistent chest views survive seat/hand refreshes and ongoing animations.</summary>
        private void RefreshChests()
        {
            if (_chestViews == null)
            {
                _chestViews = new ChestView[_game.Players.Count];
                for (var i = 0; i < _chestViews.Length; i++)
                {
                    var owner = i;
                    _chestViews[i] = ChestView.Create(_chestLayer, _font, () => OpenCollection(owner));
                }
            }

            var opponents = _game.Players.Count - 1;
            var layout = BuildSeatLayout(opponents);
            for (var owner = 0; owner < _chestViews.Length; owner++)
            {
                var chest = _chestViews[owner];
                var rect = chest.Rect;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                if (owner == HumanIndex)
                {
                    rect.sizeDelta = Vector2.one * 210f;
                    rect.position = _humanChestsArea.position;
                }
                else
                {
                    var seat = SeatPosition(owner - 1, opponents, layout);
                    var side = seat.x < 0f ? -1f : 1f;
                    // Fully raised lid and both labels remain within the seat's vertical
                    // block, including a centre seat immediately below the rank summary.
                    var size = Mathf.Min(Mathf.Min(190f * layout.Scale, layout.SideWidth), layout.BlockHeight - 46f);
                    rect.sizeDelta = Vector2.one * size;
                    var x = seat.x + side * ((SeatFigure.Width * 0.5f + SideGap) * layout.Scale + size * 0.5f);
                    // Keep the lower edge + two caption lines within the seat block.
                    rect.anchoredPosition = new Vector2(x, seat.y - layout.BlockHeight + size * 0.5f + 42f);
                }
                chest.SetCollection(_game.Players[owner].Chests, _visibleChests[owner]);
            }
        }

        private List<Rank> VisibleRanks(int owner)
        {
            var result = new List<Rank>();
            var player = _game.Players[owner];
            for (var i = 0; i < Mathf.Min(_visibleChests[owner], player.ChestCount); i++)
                result.Add(player.Chests[i]);
            return result;
        }

        private void OpenCollection(int owner)
        {
            if (_openingCollection != null || _collectionOverlay.IsOpen || _chestViews == null) return;
            _inspectedPlayer = owner;
            _chestViews[owner].SetInspected(true);
            _openingCollection = StartCoroutine(OpenCollectionAfterLid(owner));
        }

        private IEnumerator OpenCollectionAfterLid(int owner)
        {
            yield return new WaitForSecondsRealtime(0.25f);
            if (_chestViews == null || owner >= _chestViews.Length) yield break;
            _collectionOverlay.Show(_game.Players[owner].Name, VisibleRanks(owner),
                _chestViews[owner].DepositWorldPosition, () =>
                {
                    if (_chestViews != null && owner < _chestViews.Length && _chestViews[owner] != null)
                        _chestViews[owner].SetInspected(false);
                    _inspectedPlayer = -1;
                });
            _openingCollection = null;
        }

        private void RefreshOpenCollection()
        {
            if (_collectionOverlay.IsOpen && _inspectedPlayer >= 0 && _inspectedPlayer < _game.Players.Count)
            {
                _collectionOverlay.UpdateCollection(_game.Players[_inspectedPlayer].Name, VisibleRanks(_inspectedPlayer));
                _collectionOverlay.UpdateSourcePosition(_chestViews[_inspectedPlayer].DepositWorldPosition);
            }
        }

        private RectTransform BuildSeatBlock(Transform parent, ChestsPlayer player, float scale, float sideWidth, float side)
        {
            var container = new GameObject("Seat", typeof(RectTransform));
            container.transform.SetParent(parent, false);

            var containerRect = (RectTransform)container.transform;
            containerRect.sizeDelta = new Vector2(SeatFigure.Width * scale, SeatFigure.BlockHeight * scale);

            var isHuman = player.Index == HumanIndex;
            var isActing = !_game.IsFinished && player.Index == _visibleTurnPlayerIndex;
            var isWinner = _game.IsFinished && Contains(_game.Winners, player.Index);
            var portrait = PortraitFor(player.Index);

            var avatar = SeatFigure.Create(container.transform, _font, player.Name.Substring(0, 1),
                isHuman ? HumanColor : ChipColor, isActing || isWinner, portrait, scale);
            avatar.anchorMin = avatar.anchorMax = new Vector2(0.5f, 1f);
            avatar.pivot = new Vector2(0.5f, 1f);
            avatar.anchoredPosition = Vector2.zero;

            var label = CreateText(container.transform, player.Name, Mathf.RoundToInt(24 * scale), TextAnchor.MiddleCenter);
            label.color = isActing || isWinner ? AccentGlow : TextColor;
            PlaceUnderAvatar((RectTransform)label.transform, (SeatFigure.Height + SeatFigure.NameGap) * scale,
                new Vector2(220f * scale, SeatFigure.NameHeight * scale));

            if (isHuman)
            {
                return containerRect;
            }

            // Пустую руку видно всем: игрок без карт сидит просто фигурой, без стопки.
            if (!player.HasEmptyHand)
            {
                BuildHiddenHand(containerRect, scale, -side);
            }

            // Обводка и ловушка для курсора — последними, поверх всего места: иначе угол
            // стопки рубашек перехватывал бы клик по фигуре.
            var outline = SeatFigure.AddOutline(avatar, portrait, scale, TargetColor);
            var pinned = player.Index == _chosenTarget || player.Index == _focusTarget
                         || (!_game.IsFinished && player.Index == _game.TargetIndex);

            var hit = new GameObject("Hit", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(SeatTarget));
            hit.transform.SetParent(container.transform, false);
            var hitRect = (RectTransform)hit.transform;
            hitRect.anchorMin = hitRect.anchorMax = new Vector2(0.5f, 1f);
            hitRect.pivot = new Vector2(0.5f, 1f);
            hitRect.sizeDelta = new Vector2(SeatFigure.Width * scale, SeatFigure.BlockHeight * scale);
            hit.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);

            var index = player.Index;
            var selectable = _waitingForHuman && _game.Phase == ChestsPhase.Asking;
            hit.GetComponent<SeatTarget>().Configure(outline, pinned, selectable ? () => ChooseTarget(index) : (Action)null);

            return containerRect;
        }

        /// <summary>
        /// Одинаковая стопка из трёх рубашек у каждого соперника с картами — сколько бы их ни
        /// было, хоть одна: число карт не выдаём. Держится у нижнего угла фигуры, как карты в руке.
        /// </summary>
        private void BuildHiddenHand(RectTransform container, float scale, float side)
        {
            var stack = new GameObject("Hand", typeof(RectTransform)).GetComponent<RectTransform>();
            stack.SetParent(container, false);
            stack.anchorMin = stack.anchorMax = new Vector2(0.5f, 1f);
            stack.pivot = new Vector2(0.5f, 0.5f);
            stack.sizeDelta = Vector2.zero;
            stack.anchoredPosition = new Vector2(side * (SeatFigure.Width * 0.5f - 12f) * scale, -(SeatFigure.Height - 34f) * scale);

            for (var i = 0; i < 3; i++)
            {
                var back = CardView.Create(stack, _font, 0.34f * scale);
                back.ShowBack();
                back.SetPlain();
                back.GetComponent<Image>().raycastTarget = false;
                back.SetRestTransform(new Vector2((i - 1f) * 9f * scale, -Mathf.Abs(i - 1f) * 2f * scale), (1f - i) * 12f);
            }
        }

        private void RefreshHand()
        {
            Clear(_handRow);

            var hand = new List<Card>(_shownHand ?? (IEnumerable<Card>)_game.Players[HumanIndex].Hand);
            _handHalfWidth = 0f;
            if (hand.Count == 0)
            {
                return;
            }

            // По рангам, от младших к старшим: в сундучках весь смысл в том, сколько у тебя
            // карт одного номинала.
            hand.Sort((left, right) =>
            {
                var byRank = left.Rank.CompareTo(right.Rank);
                return byRank != 0 ? byRank : left.Suit.CompareTo(right.Suit);
            });

            // Веер ровный, без зазоров между рангами: слева младшие, справа тузы.
            var available = Mathf.Min(1600f, ((RectTransform)transform).rect.width - HandSideReserve);
            var cardScale = HandScale(hand.Count, available);
            var cardWidth = CardView.Width * cardScale;
            var angleStep = Mathf.Min(5f, 22f / Mathf.Max(hand.Count - 1, 1));
            var spacing = Mathf.Max(0f, Mathf.Min(cardWidth * 0.82f, (available - cardWidth) / Mathf.Max(hand.Count - 1, 1)));

            var mid = (hand.Count - 1) / 2f;
            _handHalfWidth = spacing * mid + cardWidth * 0.5f + 18f;
            var dropStep = Mathf.Min(6f, 30f / Mathf.Max(mid, 1f));

            // Любой публично спрошенный ранг приподнят и обведён в своей руке.
            var asked = _highlightRank ?? (_game.CurrentPlayerIndex == HumanIndex ? _game.AskedRank : null);

            for (var i = 0; i < hand.Count; i++)
            {
                var card = hand[i];
                var lifted = asked.HasValue && card.Rank == asked.Value;
                var offset = i - mid;

                var view = CardView.Create(_handRow, _font, cardScale);
                view.ShowFace(card);
                view.SetPlain();
                view.SetRestTransform(
                    new Vector2(offset * spacing, -Mathf.Abs(offset) * dropStep + (lifted ? AskedCardLift : 0f)),
                    lifted ? 0f : -offset * angleStep);

                if (lifted)
                {
                    view.SetSelected(true);
                }
            }
        }

        /// <summary>Как в блефе: пока карты помещаются с читаемым нахлёстом, размер не трогаем.</summary>
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

        // ---------- итог ----------

        private void ShowResult()
        {
            _status.color = AccentGlow;
            var human = _game.Players[HumanIndex];

            if (_game.IsDraw)
            {
                _status.text = "Ничья: у всех по " + human.ChestCount + " " + ChestsRules.ChestsWord(human.ChestCount);
            }
            else if (Contains(_game.Winners, HumanIndex))
            {
                _status.text = _game.Winners.Count == 1
                    ? "Победа! У тебя больше всех сундучков: " + human.ChestCount
                    : "Победа на двоих и больше: у тебя " + human.ChestCount + " " + ChestsRules.ChestsWord(human.ChestCount);
            }
            else
            {
                var best = _game.Players[_game.Winners[0]];
                var names = new List<string>();
                foreach (var index in _game.Winners)
                {
                    names.Add(_game.Players[index].Name);
                }

                _status.text = (names.Count == 1 ? "Победитель — " : "Победители — ") + string.Join(", ", names) + ": "
                               + best.ChestCount + " " + ChestsRules.ChestsWord(best.ChestCount);
            }

            // Таблица мест на доске вопросов: вопросы кончились, а место посреди стола свободно.
            Clear(_choiceArea);
            _results.Clear();
            var place = 0;
            var previous = -1;
            for (var i = 0; i < _game.Standings.Count; i++)
            {
                var player = _game.Players[_game.Standings[i]];
                if (player.ChestCount != previous)
                {
                    place = i + 1;
                    previous = player.ChestCount;
                }

                var color = Contains(_game.Winners, player.Index) ? AccentGlow
                    : Contains(_game.Losers, player.Index) ? NoColor
                    : TextColor;
                var rankNames = new List<string>();
                foreach (var rank in player.Chests) rankNames.Add(RankShort(rank));
                _results.Add(Paint(place + ". " + (player.Index == HumanIndex ? "Ты" : player.Name) + " — "
                                  + player.ChestCount + " " + ChestsRules.ChestsWord(player.ChestCount)
                                  + (rankNames.Count > 0 ? "  ·  " + string.Join(" · ", rankNames) : string.Empty), color));
            }

            RefreshResults();

            var restart = (RectTransform)_restartButton.transform;
            restart.anchoredPosition = new Vector2(0f,
                -(BoardTop - BoardHalfHeight + Mathf.Max(_results.Count, 1) * LineHeight + 50f));
            _restartButton.transform.SetAsLastSibling();
            _restartButton.gameObject.SetActive(true);
            _collectionOverlay.Rect.SetAsLastSibling();
        }

        // ---------- рассадка ----------

        private struct SeatLayout
        {
            public float Scale;
            public float BlockHeight;
            public float ArchDepth;
            public float SideWidth;
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

        /// <summary>Та же дуга, что за столом блефа, только сбоку от фигур лежат сундучки, а не веер.</summary>
        private SeatLayout BuildSeatLayout(int opponentCount)
        {
            var scale = OpponentVisualScale(opponentCount);
            var canvasHalf = ((RectTransform)transform).rect.width * 0.5f;
            var sideOffset = (SeatFigure.Width * 0.5f + SideGap) * scale;
            var sideWidth = SideMaxWidth * scale;
            var room = canvasHalf - FeltEdgeMargin - sideOffset;

            if (opponentCount > 1)
            {
                var clearance = (SeatFigure.Width * SeatFigure.PortraitWidthAllowance + SideGap) * scale + 24f;
                var fits = (2f * room - clearance * (opponentCount - 1)) / (opponentCount + 1);
                if (sideWidth > fits)
                {
                    sideWidth = Mathf.Max(MinChestWidth * scale, fits);
                }
            }

            return new SeatLayout
            {
                Scale = scale,
                BlockHeight = SeatFigure.BlockHeight * scale,
                ArchDepth = opponentCount <= 2 ? 150f : (opponentCount == 3 ? 220f : 290f),
                SideWidth = sideWidth,
                MaxX = room - sideWidth,
                Bias = opponentCount >= 6 ? SeatArcBiasBig : SeatArcBiasSmall
            };
        }

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

            // Над доской вопросов места не опускаются: реплики и выбор должны оставаться видны.
            var figureHalf = SeatFigure.Width * 0.5f * layout.Scale;
            if (Mathf.Abs(x) - figureHalf < BoardHalfWidth)
            {
                var overBoard = BoardTop - BoardHalfHeight - SeatToBoardGap - SeatTopMargin - layout.BlockHeight;
                depth = Mathf.Min(depth, Mathf.Max(0f, overBoard));
            }

            var corner = ((RectTransform)transform).rect.height - SeatFloor - SeatTopMargin - layout.BlockHeight;
            depth = Mathf.Min(depth, Mathf.Max(0f, corner));

            return new Vector2(x, -(SeatTopMargin + depth));
        }

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

            HubRoster.TryApply("chests", names, _playerPortraits);
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

            // Своё сукно у каждой игры: зелёное у дурака, синее у блефа, винное у сундучков.
            var felt = new GameObject("Felt", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            felt.transform.SetParent(root, false);
            var feltRect = (RectTransform)felt.transform;
            feltRect.anchorMin = Vector2.zero;
            feltRect.anchorMax = Vector2.one;
            feltRect.offsetMin = new Vector2(38f, 38f);
            feltRect.offsetMax = new Vector2(-38f, -38f);
            var feltImage = felt.GetComponent<Image>();
            feltImage.sprite = UiTextures.RoundedRect(64, 64, 20f, new Color(0.30f, 0.12f, 0.16f));
            feltImage.type = Image.Type.Sliced;

            _status = CreateText(root, string.Empty, 34, TextAnchor.MiddleCenter);
            HubQuestResultLine.Attach(_status, () => _game != null && _game.IsFinished);
            Place((RectTransform)_status.transform, new Vector2(0.5f, 1f), new Vector2(0f, -56f), new Vector2(1200f, 44f));

            _progress = CreateText(root, string.Empty, 25, TextAnchor.MiddleCenter);
            _progress.color = DimColor;
            Place((RectTransform)_progress.transform, new Vector2(0.5f, 1f), new Vector2(0f, -88f), new Vector2(1100f, 30f));

            _collectedRanks = CreateText(root, "Собраны: —", 22, TextAnchor.MiddleCenter);
            _collectedRanks.color = AccentGlow;
            Place((RectTransform)_collectedRanks.transform, new Vector2(0.5f, 1f), new Vector2(0f, -120f), new Vector2(1180f, 28f));

            _opponentsArea = new GameObject("Opponents", typeof(RectTransform)).GetComponent<RectTransform>();
            _opponentsArea.SetParent(root, false);
            Stretch(_opponentsArea);

            _pointerLayer = CreatePanel("Pointer", root, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(10f, 10f));
            _chestLayer = CreatePanel("Chests", root, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(10f, 10f));

            _board = CreatePanel("Board", root, new Vector2(0.5f, 1f), new Vector2(0f, -BoardTop),
                new Vector2(BoardWidth, BoardHalfHeight * 2f));

            _resultsArea = CreatePanel("Results", _board, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(BoardWidth, 10f));
            _resultsArea.pivot = new Vector2(0.5f, 1f);
            _resultsArea.anchoredPosition = Vector2.zero;

            _choiceArea = CreatePanel("Choices", _board, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(BoardWidth, 10f));
            _choiceArea.pivot = new Vector2(0.5f, 0f);
            _choiceArea.anchoredPosition = Vector2.zero;

            _playerSeatArea = CreatePanel("PlayerSeat", root, new Vector2(0.5f, 0f), new Vector2(-520f, 150f), new Vector2(180f, 220f));
            _humanChestsArea = CreatePanel("PlayerChests", root, new Vector2(0.5f, 0f), new Vector2(620f, 165f),
                new Vector2(HumanChestsWidth, HumanChestsHeight));
            _handRow = CreatePanel("Hand", root, new Vector2(0.5f, 0f), new Vector2(0f, HandRowBottom), new Vector2(1600f, 200f));

            CreateButton(root, "В меню", new Vector2(0f, 1f), new Vector2(120f, -60f), new Vector2(180f, 64f),
                new Color(0.3f, 0.3f, 0.32f), () =>
                {
                    StopAllCoroutines();
                    _collectionOverlay.CloseImmediate();
                    _onExit?.Invoke();
                });

            _restartButton = CreateButton(root, "Ещё партия", new Vector2(0.5f, 1f), new Vector2(0f, -700f), new Vector2(300f, 80f),
                new Color(0.21f, 0.45f, 0.29f), StartNewGame);
            _restartButton.gameObject.SetActive(false);

            _talkLayer = CreatePanel("Talk", root, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(10f, 10f));
            _flyLayer = CreatePanel("Fly", root, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(10f, 10f));
            _collectionOverlay = ChestsCollectionOverlay.Create(root, _font);
            _lastCanvasSize = root.rect.size;
        }

        private static string RankShort(Rank rank) => rank switch
        {
            Rank.Ten => "10",
            Rank.Jack => "В",
            Rank.Queen => "Д",
            Rank.King => "К",
            Rank.Ace => "Т",
            _ => ((int)rank).ToString()
        };

        private static bool Contains(IReadOnlyList<int> items, int value)
        {
            foreach (var item in items)
            {
                if (item == value)
                {
                    return true;
                }
            }

            return false;
        }

        // ---------- мелкие фабрики ----------

        /// <summary>Выбранная фишка перекрашивается целиком: одна обводка на тёмной фишке почти не видна.</summary>
        private static void SetChipSelected(Button chip, bool selected)
        {
            chip.GetComponent<Outline>().enabled = selected;
            chip.GetComponent<Image>().sprite = UiTextures.RoundedRect(48, 48, 12f, selected ? AccentGlow : ChipColor);

            var label = chip.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.color = selected ? ChipInk : TextColor;
            }

            chip.transform.localScale = selected ? new Vector3(1.1f, 1.1f, 1f) : Vector3.one;
        }

        private Button CreateChip(Transform parent, string label, Vector2 position, Vector2 size, Action onClick)
        {
            var go = new GameObject("Chip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(HubOutline), typeof(Button));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
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
            var colors = button.colors;
            colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f, 1f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
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
            var colors = button.colors;
            colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.6f);
            button.colors = colors;
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
