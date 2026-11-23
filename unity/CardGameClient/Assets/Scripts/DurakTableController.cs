using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CardGame.Bots;
using CardGame.Core.Cards;
using CardGame.Core.Games.Durak;
using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>
    /// Стол дурака: строит интерфейс и связывает клики игрока с логикой из CardGame.Core.
    /// Правила здесь не дублируются — какие ходы разрешены, решает GetLegalMoves.
    /// </summary>
    public sealed partial class DurakTableController : MonoBehaviour
    {
        private const int HumanIndex = 0;

        private static string HumanName => PlayerProfile.DisplayName;
        private bool _hubMatchReported;
        private bool _hubBankJobMatch;
        private int _hubCaughtCheats, _hubCheatsPlayed, _hubTimesCaught;

        // Пул имён ботов — до 7 штук, чтобы хватило на стол максимального размера (8 игроков).
        private static readonly string[] BotNamePool = { "Борис", "Вера", "Игорь", "Ольга", "Пётр", "Марина", "Сергей" };

        private static readonly Color ActiveGlow = new Color(1f, 0.85f, 0.3f);
        private static readonly Color TransferChipColor = new Color(0.42f, 0.28f, 0.55f);

        // Explicit touch selection and the hand's baseline.
        private const float ChoiceCardLift = 32f;
        private const float HandRowBottom = 158f;

        // Конец раздачи: сначала пауза на прочитать расклад, потом карты уезжают к тому,
        // кто их забрал. Без этого стол просто мгновенно пустел и было непонятно, что было.
        private const float BoutHoldDuration = 0.28f;
        private const float PenaltyFlightDuration = 0.45f;

        // Обычная пауза перед ходом бота; в шулерских партиях длиннее — иначе человек
        // физически не успевает разглядеть карту и нажать на неё.
        private const float BotThinkDelay = 1.1f;
        private const float CheatBotThinkDelay = 1.5f;

        // Не 100%: боты не должны ловить обман с той же надёжностью, что и настоящий рефери,
        // иначе шулерство против ботов было бы бессмысленно.
        private const double BotCheatNoticeChance = 0.6;

        private DurakMatchSettings _settings = DurakMatchSettings.Default;
        private Action _onExit;

        // Responsive centre of the playing surface, measured from the top.
        private float TableRowTop => ((RectTransform)transform).rect.height * .50f;

        private DurakGame _game;
        private SimpleDurakBot _bot;
        private Font _font;

        private RectTransform _tableRow;
        private RectTransform _handRow;
        private RectTransform _opponentsArea;
        private RectTransform _playerSeatArea;
        private RectTransform _deckStackArea;

        // Слой для карт в полёте: они живут вне обычных контейнеров, поэтому им нужен
        // свой дом — иначе после "Новой партии" недолетевшие карты остались бы на экране.
        private RectTransform _flyLayer;
        private CardView _trumpCard;
        private RectTransform _trumpFrame;
        private Text _status;
        private Text _trumpInfo;
        private Text _deckInfo;
        private Text _lastAction;
        private Button _passButton;
        private Button _takeButton;
        private Button _restartButton;

        private DurakMove? _pendingMove;
        private bool _waitingForHuman;

        // Выбранная в руке карта, которая ждёт цель на столе. Отдельных кнопок нет:
        // тап по карте, тап по той карте соперника, которую ею накрываешь.
        private Card? _choiceCard;
        private bool _choiceDirty;

        // Ходы человека на текущий кадр — считаются один раз в Refresh и используются
        // и рукой, и столом (стол должен знать, какую карту куда можно положить).
        private IReadOnlyList<DurakMove> _humanLegalMoves = Array.Empty<DurakMove>();
        private IReadOnlyList<DurakMove> _humanCheatMoves = Array.Empty<DurakMove>();

        // Сколько раздач уже показано — по расхождению со счётчиком в правилах видно,
        // что раздача только что закрылась и пора отправлять карты к их новому владельцу.
        private int _seenBouts;
        private bool _holdTable;
        private CanvasGroup _tableGroup;

        // Пока идёт добор, руки показываются такими, какими были до него: карты должны
        // прилететь из колоды на глазах, а не появиться в руке из ниоткуда.
        private bool _holdHands;
        private int _seenPenalties;

        // Клик по последней сыгранной карте на столе — сигнал "хочу оспорить", а не сам ход:
        // применяется как альтернатива обычному ходу в GameLoop.
        private bool _challengeClicked;
        private int _challengeSlot;
        private bool _challengeIsAttackCard;
        private CardView _challengeableCardView;

        // Rendered hand extent, also used by layout verification.
        private float _handHalfWidth;

        private readonly HashSet<Card> _tableCardsShown = new HashSet<Card>();
        private readonly List<TablePair> _tableSnapshot = new List<TablePair>();
        private string _tableSignature;
        private int? _lastActorIndex;
        private Sprite[] _playerPortraits;

        private struct TablePair
        {
            public Card Attack;
            public Card? Defense;
        }

        /// <summary>Вызывается лобби до включения экрана игры, поэтому Start() уже видит нужные настройки.</summary>
        public void Configure(DurakMatchSettings settings, Action onExit)
        {
            _settings = settings;
            _onExit = onExit;
        }

        private void Start()
        {
            ApplyClubSafeArea();
            // AppFlow меняет масштаб холста при входе за стол. Первый расклад должен
            // использовать уже новый размер, иначе боковые руки оказываются за экраном.
            Canvas.ForceUpdateCanvases();
            _font = UiFonts.Body;
            _bot = new SimpleDurakBot();
            var hub = PlayerHubRuntime.Current;
            _hubBankJobMatch = hub != null && hub.Matches.Any(entry => entry.Id == hub.ActiveMatchId && !string.IsNullOrEmpty(entry.BankJobId));

            BuildUi();
            StartNewGame();
        }

        private void StartNewGame()
        {
            _hubMatchReported = false;
            _hubCaughtCheats = _hubCheatsPlayed = _hubTimesCaught = 0;
            var seed = UnityEngine.Random.Range(0, int.MaxValue);

            // Сначала лица, потом имена: бот с лицом реального человека называется так же,
            // и имя уходит в правила при создании партии — переименовать потом уже нельзя.
            if (_playerPortraits == null || _playerPortraits.Length != _settings.PlayerCount) ShuffleAvatars(_settings.PlayerCount);
            _clubDiscardCount = 0;
            PaintClubDiscard();
            _clubLaunch = null;
            CloseClubPopup();
            if (_clubBubble != null) Destroy(_clubBubble.gameObject);
            var playerNames = BuildPlayerNames(_settings.PlayerCount);
            _game = DurakGame.Create(playerNames, _settings.BuildDeck(seed), _settings.BuildOptions());
            _pendingMove = null;
            _waitingForHuman = false;
            _lastActorIndex = null;
            _tableCardsShown.Clear();
            _tableSnapshot.Clear();
            _tableSignature = null;
            _choiceCard = null;
            _choiceDirty = false;
            _challengeClicked = false;
            _challengeableCardView = null;
            _seenBouts = _game.BoutsFinished;
            _seenPenalties = _game.PenaltyEvents;
            _holdTable = false;
            _holdHands = false;
            _tableRow.anchoredPosition = new Vector2(0f, -TableRowTop);
            _tableRow.localScale = Vector3.one;
            _tableGroup.alpha = 1f;
            Clear(_flyLayer);

            _lastAction.text = string.Empty;

            _trumpCard.ShowFace(_game.TrumpCard);
            _trumpCard.SetPlain();
            _trumpInfo.text = "КОЗЫРЬ";
            _restartButton.gameObject.SetActive(false);

            StopAllCoroutines();
            StartCoroutine(GameLoop());
        }

        private IEnumerator GameLoop()
        {
            while (!_game.IsFinished)
            {
                var actor = DurakTurnOrder.GetActingPlayer(_game);
                if (actor == null)
                {
                    // Ходить некому — значит раздача уже сыграна и правила держат её
                    // только ради последнего окна оспаривания. Даём время среагировать
                    // и закрываем окно сами.
                    if (!_game.HasChallengeableMove)
                    {
                        break;
                    }

                    yield return WaitForChallengeWindow();

                    if (_challengeClicked)
                    {
                        ResolveHumanChallenge();
                        Refresh();
                        yield return PlayPendingPenalty();
                        continue;
                    }

                    if (TryBotBystanderChallenge())
                    {
                        Refresh();
                        yield return PlayPendingPenalty();
                        continue;
                    }

                    _holdTable = !_game.IsFinished;
                    _holdHands = _holdTable;
                    _game.CloseChallengeWindow();
                    yield return FinishBoutIfNeeded();
                    continue;
                }

                if (actor.Value == HumanIndex)
                {
                    _waitingForHuman = true;
                    _pendingMove = null;
                    _challengeClicked = false;
                    _choiceCard = null;
                    _choiceDirty = false;
                    Refresh();

                    // Своя очередь ходить — время на клик по последней карте не ограничено:
                    // человек и так смотрит на стол, прежде чем решить, чем отвечать.
                    // Попутно перерисовываем руку, когда игрок раскрыл выбор над картой.
                    while (!_pendingMove.HasValue && !_challengeClicked)
                    {
                        if (_choiceDirty)
                        {
                            _choiceDirty = false;
                            Refresh();
                        }

                        yield return null;
                    }

                    if (_challengeClicked)
                    {
                        ResolveHumanChallenge();
                        _waitingForHuman = false;
                        Refresh();
                        yield return PlayPendingPenalty();
                        continue;
                    }

                    var move = _pendingMove.Value;
                    _waitingForHuman = false;
                    _pendingMove = null;

                    var wasActualCheat = _game.GetCheatMoves(HumanIndex).Any(candidate => candidate.Type == move.Type
                        && candidate.Card == move.Card && candidate.TargetSlot == move.TargetSlot);
                    if (_game.TryApplyMove(HumanIndex, move, out var error))
                    {
                        if (wasActualCheat) _hubCheatsPlayed++;
                        _lastActorIndex = HumanIndex;
                        RememberLastDefense(move);
                    }
                    else
                    {
                        Debug.LogWarning("Ход отклонён: " + error);
                    }

                    yield return AfterMove();

                    if (TryBotBystanderChallenge())
                    {
                        Refresh();
                        yield return PlayPendingPenalty();
                        continue;
                    }
                }
                else
                {
                    Refresh();

                    // Окно на реакцию перед ходом бота: параллельно можно нажать на карту,
                    // которую сыграли только что (человеком или другим ботом).
                    yield return WaitForChallengeWindow();

                    if (_challengeClicked)
                    {
                        ResolveHumanChallenge();
                        Refresh();
                        yield return PlayPendingPenalty();
                        continue;
                    }

                    var move = _bot.ChooseMove(_game, actor.Value);
                    if (move == null)
                    {
                        break;
                    }

                    if (_game.TryApplyMove(actor.Value, move.Value, out _))
                    {
                        _lastActorIndex = actor.Value;
                        RememberLastDefense(move.Value);
                    }

                    yield return AfterMove();

                    if (TryBotBystanderChallenge())
                    {
                        Refresh();
                        yield return PlayPendingPenalty();
                        continue;
                    }
                }
            }

            Refresh();
            _restartButton.gameObject.SetActive(true);
        }

        /// <summary>
        /// Общий хвост любого хода: показать результат, дать карте долететь и, если раздача
        /// на этом закрылась, отправить стол к тому, кто его забрал.
        /// </summary>
        private IEnumerator AfterMove()
        {
            // Стол и руки уже изменились в правилах, но на экране должны дожить до перелёта.
            _holdTable = _game.BoutsFinished != _seenBouts && !_game.IsFinished;
            _holdHands = _holdTable;

            Refresh();
            yield return new WaitForSeconds(CardView.FlightDuration);
            yield return FinishBoutIfNeeded();
        }

        /// <summary>Пауза, в которую можно успеть нажать на последнюю сыгранную карту.</summary>
        private IEnumerator WaitForChallengeWindow()
        {
            _challengeClicked = false;

            var delay = _settings.AllowCheating ? CheatBotThinkDelay : BotThinkDelay;
            var elapsed = 0f;
            while (elapsed < delay && !_challengeClicked)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        /// <summary>Если раздача закрылась — показываем, куда уехали карты, и только потом чистим стол.</summary>
        private IEnumerator FinishBoutIfNeeded()
        {
            if (_game.BoutsFinished == _seenBouts)
            {
                _holdTable = false;
                _holdHands = false;
                yield break;
            }

            _seenBouts = _game.BoutsFinished;

            // Последнюю раздачу партии не убираем вообще — она должна остаться на виду
            // до нажатия "Новая партия".
            if (_game.IsFinished)
            {
                _holdTable = false;
                _holdHands = false;
                yield break;
            }

            yield return PlayBoutCollection(_game.LastBoutTakenBy);

            _holdTable = false;
            _tableSnapshot.Clear();
            Refresh();

            yield return PlayRefill();
        }

        /// <summary>
        /// Добор в конце раздачи: у каждого, кто брал, из колоды вылетает стопка рубашек.
        /// Пока летят, руки показываются в прежнем размере — иначе карты появлялись бы
        /// в руке раньше, чем видно, откуда они взялись.
        /// </summary>
        private IEnumerator PlayRefill()
        {
            var draws = _game.LastRefillDraws;
            if (draws.Count == 0)
            {
                _holdHands = false;
                yield break;
            }

            var order = new List<int>();
            foreach (var playerIndex in draws)
            {
                if (!order.Contains(playerIndex))
                {
                    order.Add(playerIndex);
                }
            }

            foreach (var playerIndex in order)
            {
                StartCoroutine(FlyFromDeck(playerIndex, RefilledCount(playerIndex)));
                yield return new WaitForSeconds(0.09f);
            }

            yield return new WaitForSeconds(PenaltyFlightDuration);

            _holdHands = false;
            Refresh();
        }

        /// <summary>Сколько карт игрок только что добрал — на это число "укорачивается" его рука, пока они летят.</summary>
        private int RefilledCount(int playerIndex)
        {
            if (!_holdHands)
            {
                return 0;
            }

            var drawn = 0;
            foreach (var index in _game.LastRefillDraws)
            {
                if (index == playerIndex)
                {
                    drawn++;
                }
            }

            return drawn;
        }

        /// <summary>Штрафная карта: тоже видно, что её достали из колоды и отдали конкретному игроку.</summary>
        private IEnumerator PlayPendingPenalty()
        {
            if (_game.PenaltyEvents == _seenPenalties)
            {
                yield break;
            }

            _seenPenalties = _game.PenaltyEvents;

            if (!_game.LastPenaltyPlayer.HasValue)
            {
                yield break;
            }

            var player = _game.Players[_game.LastPenaltyPlayer.Value].Name;

            if (!_game.LastPenaltyWasDrawn)
            {
                SetLastAction(player + ": брать нечего — блефовать больше нельзя до конца партии.");
                yield break;
            }

            // Из биты карта летит со стола: колода в углу пуста, и было бы непонятно,
            // откуда там взялась карта.
            if (_game.LastPenaltyFromDiscard)
            {
                SetLastAction(player + " берёт штрафную из биты.");
                yield return FlyCards(_game.LastPenaltyPlayer.Value, 1, new Vector2(0f, -TableRowTop));
            }
            else
            {
                yield return FlyFromDeck(_game.LastPenaltyPlayer.Value, 1);
            }

            Refresh();
        }

        private IEnumerator FlyFromDeck(int playerIndex, int count)
        {
            return FlyCards(playerIndex, count, ClubTopPoint(_deckStackArea.position));
        }

        /// <summary>Стопка рубашек летит к месту игрока. Больше трёх не рисуем — читается хуже, чем помогает.</summary>
        private IEnumerator FlyCards(int playerIndex, int count, Vector2 from)
        {
            if (count <= 0)
            {
                yield break;
            }

            if (ClubReduced) { yield return new WaitForSeconds(.12f); yield break; }
            var to = SeatAnchorPosition(playerIndex);

            var visible = Mathf.Min(count, 3);
            var views = new List<CardView>();
            for (var i = 0; i < visible; i++)
            {
                var card = CardView.Create(_flyLayer, _font, 0.8f);
                card.ShowBack();
                card.SetPlain();
                card.Rect.anchorMin = card.Rect.anchorMax = new Vector2(0.5f, 1f);
                card.Rect.pivot = new Vector2(0.5f, 0.5f);
                card.Rect.anchoredPosition = from + new Vector2(i * 7f, i * 5f);
                views.Add(card);
            }

            var t = 0f;
            while (t < PenaltyFlightDuration)
            {
                t += Time.deltaTime;
                var k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / PenaltyFlightDuration));

                for (var i = 0; i < views.Count; i++)
                {
                    var stagger = Mathf.SmoothStep(0, 1, Mathf.Clamp01((t - i * .035f) / (PenaltyFlightDuration - .07f)));
                    var offset = new Vector2(i * 7f, i * 5f) * (1f - stagger);
                    views[i].Rect.anchoredPosition = Vector2.Lerp(from, to, stagger) + offset + Vector2.up * Mathf.Sin(stagger * Mathf.PI) * 35;
                    var scale = Mathf.Lerp(0.8f, 0.5f, k);
                    views[i].Rect.localScale = new Vector3(scale, scale, 1f);
                }

                yield return null;
            }

            foreach (var view in views)
            {
                Destroy(view.gameObject);
            }
        }

        /// <summary>
        /// Individual cards travel to the taker's seat or the visible discard pile.
        /// </summary>
        private IEnumerator PlayBoutCollection(int? takenBy)
        {
            return CollectClubCards(takenBy);
        }

        /// <summary>Место игрока в той же системе координат, что и ряд карт стола (отсчёт от верха).</summary>
        private Vector2 SeatAnchorPosition(int playerIndex)
        {
            if (playerIndex == HumanIndex)
                return new Vector2(0, -(((RectTransform)transform).rect.height - HandRowBottom));
            return ClubSeatPosition(playerIndex - 1, _game.Players.Count - 1) + new Vector2(0, -ClubPortraitY);
        }

        /// <summary>Применяет клик человека по последней сыгранной карте — оспаривание, а не ход.</summary>
        private void ResolveHumanChallenge()
        {
            _challengeClicked = false;
            var cheaterIndex = _game.ChallengeablePlayerIndex;
            var slot = _challengeSlot;
            var isAttackCard = _challengeIsAttackCard;

            if (_game.TryChallenge(HumanIndex, slot, isAttackCard, out var error))
            {
                if (cheaterIndex.HasValue && cheaterIndex.Value != HumanIndex) _hubCaughtCheats++;
                var cheaterName = cheaterIndex.HasValue ? _game.Players[cheaterIndex.Value].Name : "?";
                SetLastAction("Обман! " + cheaterName + " забирает карту обратно.");
            }
            else
            {
                SetLastAction(error ?? "Ничего не изменилось.");
            }

            ClearChallengeableCardView();
        }

        /// <summary>
        /// Даёт ботам-свидетелям (не тому, кто ходил, и не человеку) шанс словить обман сразу
        /// после хода — до того, как очередь дойдёт до следующего игрока.
        /// </summary>
        private bool TryBotBystanderChallenge()
        {
            if (!_game.HasChallengeableMove)
            {
                return false;
            }

            var actedPlayer = _game.ChallengeablePlayerIndex;
            foreach (var player in _game.Players)
            {
                if (player.IsOut || player.Index == HumanIndex || player.Index == actedPlayer)
                {
                    continue;
                }

                if (_bot.TryChallenge(_game, player.Index, BotCheatNoticeChance, out _))
                {
                    if (actedPlayer.HasValue && actedPlayer.Value == HumanIndex) _hubTimesCaught++;
                    SetLastAction(player.Name + ": поймал на обмане!");
                    ClearChallengeableCardView();
                    return true;
                }
            }

            return false;
        }

        private void ClearChallengeableCardView()
        {
            if (_challengeableCardView != null)
            {
                _challengeableCardView.SetPlain();
                _challengeableCardView = null;
            }
        }

        private void Refresh()
        {
            if (!_hubMatchReported && _game.IsFinished)
            {
                _hubMatchReported = true;
                HubMatchReporter.ReportDurakEvidence(_hubCaughtCheats, _hubCheatsPlayed, _hubTimesCaught);
                HubMatchReporter.ReportMatchCompleted("durak",
                    _game.LoserIndex.HasValue && _game.LoserIndex.Value != HumanIndex,draw:!_game.LoserIndex.HasValue);
            }
            _humanLegalMoves = _waitingForHuman ? _game.GetLegalMoves(HumanIndex) : Array.Empty<DurakMove>();
            _humanCheatMoves = _waitingForHuman && _settings.AllowCheating
                ? _game.GetCheatMoves(HumanIndex)
                : Array.Empty<DurakMove>();

            RefreshTable();
            RefreshHand();
            RefreshOpponents();
            RefreshPlayerSeat();
            RefreshControls();
            RefreshDeckPile();
        }

        private List<string> BuildPlayerNames(int playerCount)
        {
            var names = new List<string> { HumanName };
            var fromPool = 0;

            for (var player = 1; player < playerCount; player++)
            {
                // Лицо реального человека приносит с собой и имя: иначе за столом сидит
                // «Вера» с лицом Данила. Выдуманным персонажам имя берём из списка.
                var title = AvatarCatalog.PersonalTitleOf(PortraitFor(player));
                if (title != null)
                {
                    // Если друзей меньше, чем мест, один и тот же приходит вторым образом.
                    // Имена всё равно должны различаться, иначе в логе ходов не разобрать,
                    // кто есть кто.
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

            HubRoster.TryApply("durak", names, _playerPortraits);
            return names;
        }

        private void SetLastAction(string line)
        {
            _lastAction.text = line;
            _clubLineUntil = Time.unscaledTime + 2.5f;
        }

        private void RefreshDeckPile()
        {
            PaintClubPiles();
        }

        private void CreateCardBackStack(RectTransform container, int count, Vector2 step, float scale, int maxVisible = 5)
        {
            Clear(container);
            var visible = Mathf.Clamp(count, 0, maxVisible);

            for (var i = 0; i < visible; i++)
            {
                var back = CardView.Create(container, _font, scale);
                back.Rect.anchoredPosition = step * i;
                back.ShowBack();
                back.SetPlain();
            }
        }

        private void RefreshTable()
        {
            var pairs = CollectTablePairs();

            // В подпись входит и выбранная в руке карта: при выборе цели расклад тот же,
            // но столу нужно перевесить обработчики с оспаривания на "положить сюда".
            // Без этого тап по карте стола уходил в ложное обвинение вместо отбоя.
            var signature = TableSignature(pairs) + "|" + (_choiceCard.HasValue ? _choiceCard.Value.ToString() : "-");

            // Экран обновляется несколько раз подряд (после хода и на входе в следующий ход).
            // Если расклад не изменился, ничего не пересоздаём — иначе только что запущенный
            // перелёт карты уничтожается в том же кадре и его никто не видит.
            if (signature == _tableSignature)
            {
                return;
            }

            _tableSignature = signature;
            Clear(_tableRow);
            _challengeableCardView = null;

            var tableCardScale = ClubTableCardScale(pairs.Count);
            var stillOnTable = new HashSet<Card>();
            var phoneArrivals = new List<(CardView View, Card Card)>();

            for (var slotIndex = 0; slotIndex < pairs.Count; slotIndex++)
            {
                var pair = pairs[slotIndex];
                var container = new GameObject("Slot", typeof(RectTransform));
                container.transform.SetParent(_tableRow, false);
                ((RectTransform)container.transform).sizeDelta = new Vector2(160f * tableCardScale, 230f);

                var attackPosition = new Vector2(-16f, 18f);
                CreateCardShadow(container.transform, attackPosition, tableCardScale);

                var attack = CardView.Create(container.transform, _font, tableCardScale);
                // Карты брошены рукой: чуть вразнобой, но у каждой карты свой постоянный наклон.
                attack.SetRestTransform(attackPosition, ClubCardTilt(pair.Attack) - 2);
                attack.ShowFace(pair.Attack);
                attack.SetPlain();
                if (phoneArrivals != null) phoneArrivals.Add((attack, pair.Attack));
                else AnimateArrival(attack, pair.Attack);
                stillOnTable.Add(pair.Attack);

                // Пока в руке выбрана карта, стол работает только как выбор цели: иначе
                // промах мимо нужной карты уходил бы в обвинение и стоил штрафной карты.
                var choosing = _waitingForHuman && _choiceCard.HasValue;
                if (choosing)
                {
                    WireTargetIfChoosing(attack, slotIndex);
                }
                else
                {
                    WireChallengeIfPending(attack, slotIndex, isAttackCard: true);
                }

                if (pair.Defense.HasValue)
                {
                    var defensePosition = new Vector2(18f, -20f);
                    CreateCardShadow(container.transform, defensePosition, tableCardScale);

                    var defense = CardView.Create(container.transform, _font, tableCardScale);
                    defense.SetRestTransform(defensePosition, ClubCardTilt(pair.Defense.Value) + 5);
                    defense.ShowFace(pair.Defense.Value);
                    defense.SetPlain();
                    if (phoneArrivals != null) phoneArrivals.Add((defense, pair.Defense.Value));
                    else AnimateArrival(defense, pair.Defense.Value);
                    stillOnTable.Add(pair.Defense.Value);

                    if (!choosing)
                    {
                        WireChallengeIfPending(defense, slotIndex, isAttackCard: false);
                    }
                }
            }

            if (phoneArrivals != null)
            {
                // Все слоты уже созданы: их центры больше не сдвинутся после начала
                // перелёта, даже если одновременно появились несколько пар.
                Canvas.ForceUpdateCanvases();
                foreach (var arrival in phoneArrivals) AnimateArrival(arrival.View, arrival.Card);
            }

            _tableCardsShown.Clear();
            foreach (var card in stillOnTable)
            {
                _tableCardsShown.Add(card);
            }
        }

        /// <summary>
        /// Ровно одна карта на столе кликабельна — та, что легла последней. Не важно, честный
        /// это был ход или нет: подсветка одинаковая в обоих случаях (см. CardView.SetChallengeable).
        /// </summary>
        /// <summary>
        /// Если в руке выбрана карта, каждая неотбитая карта стола становится целью:
        /// так можно положить восьмёрку именно на ту семёрку, на какую хочешь, — и честно,
        /// и обманом. Возвращает true, если карта стала целью.
        /// </summary>
        private bool WireTargetIfChoosing(CardView view, int slotIndex)
        {
            if (!_waitingForHuman || !_choiceCard.HasValue)
            {
                return false;
            }

            var move = FindTargetMove(_choiceCard.Value, slotIndex);
            if (!move.HasValue)
            {
                return false;
            }

            var captured = move.Value;
            view.SetClickable(() =>
            {
                QueueClubMove(captured);
            });

            return true;
        }

        /// <summary>Каким ходом эта карта ложится на конкретный слот: честным отбоем или подлогом.</summary>
        private DurakMove? FindTargetMove(Card card, int slotIndex)
        {
            foreach (var move in _humanLegalMoves)
            {
                if (move.Type == DurakMoveType.Defend && move.Card == card && move.TargetSlot == slotIndex)
                {
                    return move;
                }
            }

            foreach (var move in _humanCheatMoves)
            {
                if (move.Type == DurakMoveType.CheatDefend && move.Card == card && move.TargetSlot == slotIndex)
                {
                    return move;
                }
            }

            return null;
        }

        /// <summary>Перевод этой картой, честный или поддельный — он всегда один на карту.</summary>
        private DurakMove? FindTransferMove(Card card)
        {
            foreach (var move in _humanLegalMoves)
            {
                if (move.Type == DurakMoveType.Transfer && move.Card == card)
                {
                    return move;
                }
            }

            foreach (var move in _humanCheatMoves)
            {
                if (move.Type == DurakMoveType.CheatTransfer && move.Card == card)
                {
                    return move;
                }
            }

            return null;
        }

        private void WireChallengeIfPending(CardView view, int slotIndex, bool isAttackCard)
        {
            if (!_game.HasChallengeableMove || _game.ChallengeableSlot != slotIndex || _game.ChallengeableIsAttackCard != isAttackCard)
            {
                return;
            }

            _challengeableCardView = view;
            view.SetChallengeable(() =>
            {
                _challengeSlot = slotIndex;
                _challengeIsAttackCard = isAttackCard;
                _challengeClicked = true;
            });
        }

        /// <summary>
        /// Что рисовать на столе. Пока партия идёт — текущий расклад; когда она закончилась,
        /// логика уже сбросила стол в биту, поэтому показываем запомненную последнюю раздачу,
        /// чтобы было видно, чем отбились в конце.
        /// </summary>
        private List<TablePair> CollectTablePairs()
        {
            if (_game.Table.Count > 0)
            {
                _tableSnapshot.Clear();
                foreach (var slot in _game.Table)
                {
                    _tableSnapshot.Add(new TablePair { Attack = slot.Attack, Defense = slot.Defense });
                }

                return _tableSnapshot;
            }

            // _holdTable — раздача уже закрыта в правилах, но карты ещё улетают к владельцу.
            if (!_game.IsFinished && !_holdTable)
            {
                _tableSnapshot.Clear();
            }

            return _tableSnapshot;
        }

        /// <summary>Дописывает в запомненный расклад последний ход — его логика успевает стереть до перерисовки.</summary>
        private void RememberLastDefense(DurakMove move)
        {
            var isDefense = move.Type == DurakMoveType.Defend || move.Type == DurakMoveType.CheatDefend;
            if (!isDefense || move.TargetSlot < 0 || move.TargetSlot >= _tableSnapshot.Count)
            {
                return;
            }

            var pair = _tableSnapshot[move.TargetSlot];
            pair.Defense = move.Card;
            _tableSnapshot[move.TargetSlot] = pair;
        }

        private static string TableSignature(List<TablePair> pairs)
        {
            var builder = new StringBuilder();
            foreach (var pair in pairs)
            {
                builder.Append(pair.Attack).Append('/').Append(pair.Defense).Append('|');
            }

            return builder.ToString();
        }

        private void AnimateArrival(CardView view, Card card)
        {
            if (_tableCardsShown.Contains(card)) return;
            var actor = _lastActorIndex ?? HumanIndex;
            var seat = actor == HumanIndex && _clubLaunch.HasValue ? _clubLaunch.Value : SeatAnchorPosition(actor);
            var root = (RectTransform)transform;
            var world = root.TransformPoint(new Vector3(seat.x, root.rect.yMax + seat.y, 0));
            var local = (Vector2)view.Rect.parent.InverseTransformPoint(world);
            view.PlayClubFlight(local - view.Rect.anchoredPosition, ClubReduced);
            if (actor == HumanIndex) _clubLaunch = null;
        }

        private Vector2 SeatPosition(int seatIndex, int opponentCount) => ClubSeatPosition(seatIndex, opponentCount);

        private static float ClubCardTilt(Card card) => ((int)card.Rank * 7 + (int)card.Suit * 3) % 5 - 2;

        private static void CreateCardShadow(Transform parent, Vector2 position, float scale)
        {
            var go = new GameObject("CardShadow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position + new Vector2(2f, -7f);
            rect.sizeDelta = new Vector2(CardView.Width * scale + 40f, CardView.Height * scale + 40f);

            var image = go.GetComponent<Image>();
            image.sprite = UiTextures.SoftShadow(96, 132, 14f, 20f, 0.45f);
            image.type = Image.Type.Sliced;
            image.raycastTarget = false;
        }

        private void RefreshHand()
        {
            RefreshClubHand();
        }

        /// <summary>Порядок карт в своей руке: обычные, потом козыри, потом джокеры; внутри — по рангу.</summary>
        private int CompareInHand(Card left, Card right)
        {
            var byGroup = HandGroup(left).CompareTo(HandGroup(right));
            if (byGroup != 0)
            {
                return byGroup;
            }

            var byRank = left.Rank.CompareTo(right.Rank);
            return byRank != 0 ? byRank : left.Suit.CompareTo(right.Suit);
        }

        private int HandGroup(Card card) => card.IsJoker ? 2 : card.Suit == _game.Trump ? 1 : 0;

        /// <summary>Сколько карт на столе можно накрыть этой картой — честно или подлогом.</summary>
        private int CountTargets(Card card)
        {
            var targets = 0;
            for (var slot = 0; slot < _game.Table.Count; slot++)
            {
                if (FindTargetMove(card, slot).HasValue)
                {
                    targets++;
                }
            }

            return targets;
        }

        private DurakMove? FirstTargetMove(Card card)
        {
            for (var slot = 0; slot < _game.Table.Count; slot++)
            {
                var move = FindTargetMove(card, slot);
                if (move.HasValue)
                {
                    return move;
                }
            }

            return null;
        }

        /// <summary>Подкидывание этой картой — настоящее или под видом настоящего.</summary>
        private DurakMove? FindAttackMove(Card card)
        {
            foreach (var move in _humanLegalMoves)
            {
                if (move.Type == DurakMoveType.Attack && move.Card == card)
                {
                    return move;
                }
            }

            foreach (var move in _humanCheatMoves)
            {
                if (move.Type == DurakMoveType.CheatAttack && move.Card == card)
                {
                    return move;
                }
            }

            return null;
        }

        /// <summary>Где стоит своё место: вплотную к левому краю веера, но не заезжая на колоду.</summary>
        private float PlayerSeatX()
        {
            return -((RectTransform)transform).rect.width * .5f + 122;
        }

        /// <summary>Клик по карте с двумя вариантами: раскрывает выбор или закрывает его обратно.</summary>
        private void ToggleMoveChoice(Card card)
        {
            _choiceCard = _choiceCard.HasValue && _choiceCard.Value == card ? (Card?)null : card;

            // Rewire table targets in the game loop after this input callback completes.
            _choiceDirty = true;
        }

        private void CreateChoiceChip(string label, float x, float y, Color color, DurakMove move)
        {
            var button = ClubActionButton(_handRow, label, "Durak/plate-green", Vector2.one * .5f, new Vector2(x, y), () => QueueClubMove(move));
            button.name = "MoveChoice";
        }

        private void RefreshOpponents()
        {
            Clear(_opponentsArea);

            var actingIndex = _waitingForHuman ? (int?)HumanIndex : DurakTurnOrder.GetActingPlayer(_game);
            var opponentCount = _game.Players.Count - 1;
            var seat = 0;

            foreach (var player in _game.Players)
            {
                if (player.Index == HumanIndex)
                {
                    continue;
                }

                var seatPosition = SeatPosition(seat, opponentCount);
                seat++;

                var isActing = actingIndex.HasValue && actingIndex.Value == player.Index;
                var containerRect = BuildClubSeat(_opponentsArea, player, isActing);

                // Привязка к верхнему краю и пивот сверху: место задаётся отступом от верха
                // экрана, поэтому расстояние до надписей не зависит от пропорций окна.
                containerRect.anchorMin = containerRect.anchorMax = new Vector2(0.5f, 1f);
                containerRect.pivot = new Vector2(0.5f, 1f);
                containerRect.anchoredPosition = seatPosition;
            }
        }

        /// <summary>
        /// Own portrait has a stable dock, independent of hand size.
        /// </summary>
        private void RefreshPlayerSeat()
        {
            Clear(_playerSeatArea);

            _playerSeatArea.anchoredPosition = new Vector2(PlayerSeatX(), 124);

            var block = BuildClubSeat(_playerSeatArea, _game.Players[HumanIndex], _waitingForHuman);
            block.anchorMin = block.anchorMax = new Vector2(0.5f, 0.5f);
            block.pivot = new Vector2(0.5f, 0.5f);
            block.anchoredPosition = Vector2.zero;
        }

        /// <summary>
        /// Портреты: себе игрок выбирает аватар сам (экран выбора), за соперников садятся
        /// друзья — правила набора в AvatarCatalog.OpponentPortraits.
        /// Пока картинок нет, всё работает по-старому — цветной кружок с буквой.
        /// </summary>
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

        private Sprite PortraitFor(int playerIndex)
        {
            return _playerPortraits != null && playerIndex < _playerPortraits.Length
                ? _playerPortraits[playerIndex]
                : null;
        }



        private void RefreshControls()
        {
            RefreshClubControls();
        }

        private void BuildUi()
        {
            BuildClubUi();
        }

        private Button CreateButton(Transform parent, string label, Vector2 anchor, Vector2 position, Color color, Action onClick)
        {
            return ClubButton(parent, label, anchor, position, new Vector2(260, 86), onClick);
        }

        private Text CreateText(Transform parent, string content, int fontSize, TextAnchor anchor)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);

            var text = go.GetComponent<Text>();
            text.font = _font;
            text.fontSize = MobileUi.TextSize(fontSize);
            text.alignment = anchor;
            text.color = new Color(0.94f, 0.95f, 0.93f);
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
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            return rect;
        }

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
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
