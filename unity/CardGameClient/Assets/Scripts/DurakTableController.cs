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
    public sealed class DurakTableController : MonoBehaviour
    {
        private const int HumanIndex = 0;

        // Позже имя придёт из профиля игрока, пока зашито.
        private const string HumanName = "Егор";

        // Пул имён ботов — до 7 штук, чтобы хватило на стол максимального размера (8 игроков).
        private static readonly string[] BotNamePool = { "Борис", "Вера", "Игорь", "Ольга", "Пётр", "Марина", "Сергей" };

        private static readonly Color[] OpponentColors =
        {
            new Color(0.30f, 0.52f, 0.86f), new Color(0.82f, 0.42f, 0.62f), new Color(0.36f, 0.62f, 0.42f),
            new Color(0.80f, 0.55f, 0.24f), new Color(0.55f, 0.42f, 0.78f), new Color(0.75f, 0.30f, 0.30f),
            new Color(0.30f, 0.68f, 0.70f)
        };

        private static readonly Color ActiveGlow = new Color(1f, 0.85f, 0.3f);
        private static readonly Color HumanColor = new Color(0.28f, 0.58f, 0.44f);
        private static readonly Color DefendChipColor = new Color(0.21f, 0.45f, 0.29f);
        private static readonly Color TransferChipColor = new Color(0.42f, 0.28f, 0.55f);

        // Насколько поднимается выбранная карта и какого размера плашки выбора над ней.
        private const float ChoiceCardLift = 40f;
        private const float ChoiceChipHeight = 44f;
        private const float ChoiceChipWidth = 124f;
        private const float HandRowBottom = 138f;

        // Обычная пауза перед ходом бота; в шулерских партиях длиннее — иначе человек
        // физически не успевает разглядеть карту и нажать на неё.
        private const float BotThinkDelay = 1.1f;
        private const float CheatBotThinkDelay = 1.8f;

        // Не 100%: боты не должны ловить обман с той же надёжностью, что и настоящий рефери,
        // иначе шулерство против ботов было бы бессмысленно.
        private const double BotCheatNoticeChance = 0.6;

        private DurakMatchSettings _settings = DurakMatchSettings.Default;
        private Action _onExit;

        private const float AvatarWidth = 112f;
        private const float AvatarHeight = 140f;
        private const float SeatNameHeight = 30f;
        // Только фигура и подпись: веер рубашек лежит сбоку и высоту места не ест.
        // Бейдж роли тоже нарисован поверх фигуры.
        private const float SeatNameOffset = AvatarHeight + 12f;
        private const float SeatBlockHeight = SeatNameOffset + SeatNameHeight;
        private const float SeatFanMaxWidth = 170f;
        private const float SeatFanGap = 12f;

        // Весь верх стола выстроен сверху вниз от края экрана: статус, строка хода, места
        // соперников, карты. Если привязывать их к центру, при широком окне Unity ужимает
        // высоту холста и они наезжают друг на друга.
        private const float SeatTopMargin = 150f;
        private const float TableRowTop = 524f;
        private const float TableCardsHalfWidth = 610f;
        // Насколько карта на столе торчит вверх от центра ряда и сколько воздуха
        // обязательно остаётся между ней и местом соперника.
        private const float TableCardsHalfHeight = 112f;
        private const float SeatToCardsGap = 70f;
        // Отступ от края сукна: дальше начинается деревянная рамка стола, на ней никто не сидит.
        private const float FeltEdgeMargin = 56f;
        // Высота своего места над нижним краем — вровень с веером в руке.
        private const float PlayerSeatBottom = 150f;

        private DurakGame _game;
        private SimpleDurakBot _bot;
        private Font _font;

        private RectTransform _tableRow;
        private RectTransform _handRow;
        private RectTransform _opponentsArea;
        private RectTransform _playerSeatArea;
        private RectTransform _deckStackArea;
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

        // Карта, по которой сейчас показан выбор "отбить / перевести". Отдельной кнопки
        // перевода нет: она нужна в одном редком случае (козырь того же достоинства),
        // а висела бы на экране всегда.
        private Card? _choiceCard;
        private bool _choiceDirty;

        // Клик по последней сыгранной карте на столе — сигнал "хочу оспорить", а не сам ход:
        // применяется как альтернатива обычному ходу в GameLoop.
        private bool _challengeClicked;
        private int _challengeSlot;
        private bool _challengeIsAttackCard;
        private CardView _challengeableCardView;

        // Полуширина веера в руке — по ней своё место встаёт вплотную к картам.
        private float _handHalfWidth;

        private readonly HashSet<Card> _tableCardsShown = new HashSet<Card>();
        private readonly List<TablePair> _tableSnapshot = new List<TablePair>();
        private string _tableSignature;
        private int? _lastActorIndex;
        private static Sprite[] _avatarPool;
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
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _bot = new SimpleDurakBot();

            BuildUi();
            StartNewGame();
        }

        private void StartNewGame()
        {
            var seed = UnityEngine.Random.Range(0, int.MaxValue);
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
            ShuffleAvatars();

            _lastAction.text = string.Empty;

            _trumpCard.ShowFace(_game.TrumpCard);
            _trumpCard.SetPlain();
            _trumpInfo.text = "козырь " + CardView.SuitText(_game.Trump);
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
                    break;
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
                        continue;
                    }

                    var move = _pendingMove.Value;
                    _waitingForHuman = false;
                    _pendingMove = null;

                    if (_game.TryApplyMove(HumanIndex, move, out var error))
                    {
                        _lastActorIndex = HumanIndex;
                        RememberLastDefense(move);
                        SetLastAction(_game.Players[HumanIndex].Name + ": " + move);
                    }
                    else
                    {
                        Debug.LogWarning("Ход отклонён: " + error);
                    }

                    Refresh();
                    yield return new WaitForSeconds(CardView.FlightDuration);

                    if (TryBotBystanderChallenge())
                    {
                        Refresh();
                        continue;
                    }
                }
                else
                {
                    Refresh();

                    // Окно на реакцию перед ходом бота: параллельно можно нажать на карту,
                    // которую сыграли только что (человеком или другим ботом).
                    var thinkDelay = _settings.AllowCheating ? CheatBotThinkDelay : BotThinkDelay;
                    _challengeClicked = false;
                    var elapsed = 0f;
                    while (elapsed < thinkDelay && !_challengeClicked)
                    {
                        elapsed += Time.deltaTime;
                        yield return null;
                    }

                    if (_challengeClicked)
                    {
                        ResolveHumanChallenge();
                        Refresh();
                        continue;
                    }

                    var move = _bot.ChooseMove(_game, actor.Value);
                    if (move == null)
                    {
                        break;
                    }

                    var moverName = _game.Players[actor.Value].Name;
                    if (_game.TryApplyMove(actor.Value, move.Value, out _))
                    {
                        _lastActorIndex = actor.Value;
                        RememberLastDefense(move.Value);
                        SetLastAction(moverName + ": " + move.Value);
                    }

                    Refresh();
                    yield return new WaitForSeconds(CardView.FlightDuration);

                    if (TryBotBystanderChallenge())
                    {
                        Refresh();
                        continue;
                    }
                }
            }

            Refresh();
            _restartButton.gameObject.SetActive(true);
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
            var legalMoves = _waitingForHuman ? _game.GetLegalMoves(HumanIndex) : new List<DurakMove>();

            RefreshTable();
            RefreshHand(legalMoves);
            RefreshOpponents();
            RefreshPlayerSeat();
            RefreshControls(legalMoves);
            RefreshDeckPile();
        }

        private static List<string> BuildPlayerNames(int playerCount)
        {
            var names = new List<string> { HumanName };
            for (var i = 0; i < playerCount - 1; i++)
            {
                names.Add(BotNamePool[i % BotNamePool.Length]);
            }

            return names;
        }

        private void SetLastAction(string line)
        {
            _lastAction.text = line;
        }

        private void RefreshDeckPile()
        {
            CreateCardBackStack(_deckStackArea, _game.DeckCount, new Vector2(9f, 0f), 1.05f);
            _deckInfo.text = _game.DeckCount > 0 ? "в колоде: " + _game.DeckCount : "колода пуста";

            // Козырная карта лежит на дне колоды: когда колода кончилась, её уже забрал игрок,
            // поэтому убираем саму карту и оставляем только напоминание о масти.
            var trumpCardOnTable = _game.DeckCount > 0;
            _trumpCard.gameObject.SetActive(trumpCardOnTable);
            _trumpFrame.gameObject.SetActive(trumpCardOnTable);

            _trumpInfo.fontSize = trumpCardOnTable ? 28 : 40;
            _trumpInfo.rectTransform.anchoredPosition = trumpCardOnTable
                ? new Vector2(0f, -128f)
                : new Vector2(0f, -20f);
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
            var signature = TableSignature(pairs);

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

            const float tableCardScale = 1.1f;
            var stillOnTable = new HashSet<Card>();

            for (var slotIndex = 0; slotIndex < pairs.Count; slotIndex++)
            {
                var pair = pairs[slotIndex];
                var container = new GameObject("Slot", typeof(RectTransform));
                container.transform.SetParent(_tableRow, false);
                ((RectTransform)container.transform).sizeDelta = new Vector2(190f, 260f);

                var attackPosition = new Vector2(-16f, 18f);
                CreateCardShadow(container.transform, attackPosition, tableCardScale);

                var attack = CardView.Create(container.transform, _font, tableCardScale);
                attack.Rect.anchoredPosition = attackPosition;
                attack.ShowFace(pair.Attack);
                attack.SetPlain();
                AnimateArrival(attack, pair.Attack);
                stillOnTable.Add(pair.Attack);
                WireChallengeIfPending(attack, slotIndex, isAttackCard: true);

                if (pair.Defense.HasValue)
                {
                    var defensePosition = new Vector2(18f, -20f);
                    CreateCardShadow(container.transform, defensePosition, tableCardScale);

                    var defense = CardView.Create(container.transform, _font, tableCardScale);
                    defense.Rect.anchoredPosition = defensePosition;
                    defense.ShowFace(pair.Defense.Value);
                    defense.SetPlain();
                    AnimateArrival(defense, pair.Defense.Value);
                    stillOnTable.Add(pair.Defense.Value);
                    WireChallengeIfPending(defense, slotIndex, isAttackCard: false);
                }
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

            if (!_game.IsFinished)
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
            if (_tableCardsShown.Contains(card))
            {
                return;
            }

            view.PlayFlyIn(SeatOffset(_lastActorIndex));
        }

        /// <summary>Откуда карта прилетает на стол: снизу — от игрока, иначе с его места за столом.</summary>
        private Vector2 SeatOffset(int? playerIndex)
        {
            if (!playerIndex.HasValue || playerIndex.Value == HumanIndex)
            {
                return new Vector2(0f, -380f);
            }

            // Место задано отступом от верха экрана, а карта летит к столу — переводим в его систему.
            var seat = SeatPosition(playerIndex.Value - 1, _game.Players.Count - 1);
            return new Vector2(seat.x, seat.y + TableRowTop);
        }

        /// <summary>Разметка дуги: где крайние места, насколько крупные фигуры и какой ширины веер.</summary>
        private struct SeatLayout
        {
            public float Scale;
            public float MaxX;
            public float ArchDepth;
            public float FanWidth;
            public float BlockHeight;
        }

        /// <summary>
        /// Считает дугу под текущий экран: места разводятся до самых краёв сукна, а не по
        /// зашитым числам — иначе на широком экране все кучкуются в середине, а углы пустуют.
        /// </summary>
        private SeatLayout BuildSeatLayout(int opponentCount)
        {
            var scale = OpponentVisualScale(opponentCount);
            var canvasHalf = ((RectTransform)transform).rect.width * 0.5f;
            var fanOffset = (AvatarWidth * 0.5f + SeatFanGap) * scale;
            var fanWidth = SeatFanMaxWidth * scale;
            var minFanWidth = CardView.Width * 0.55f * scale;

            // Веер уходит от фигуры наружу, к своему краю экрана, поэтому крайним местам
            // нужен запас на карты — зато дуга остаётся симметричной, а середина стола
            // не забивается чужими рубашками.
            var room = canvasHalf - FeltEdgeMargin - fanOffset;

            if (opponentCount > 1)
            {
                // Между соседями должны поместиться веер одного и фигура другого.
                // Если мест много, ужимается веер, а не расстояние между людьми.
                var clearance = (AvatarWidth + SeatFanGap) * scale + 24f;
                var fits = (2f * room - clearance * (opponentCount - 1)) / (opponentCount + 1);
                if (fanWidth > fits)
                {
                    fanWidth = Mathf.Max(minFanWidth, fits);
                }
            }

            return new SeatLayout
            {
                Scale = scale,
                BlockHeight = SeatBlockHeight * scale,
                ArchDepth = opponentCount <= 2 ? 80f : (opponentCount == 3 ? 120f : 150f),
                FanWidth = fanWidth,
                MaxX = room - fanWidth
            };
        }

        private Vector2 SeatPosition(int seatIndex, int opponentCount)
        {
            return SeatPosition(seatIndex, opponentCount, BuildSeatLayout(opponentCount));
        }

        /// <summary>
        /// Места соперников по дуге вокруг стола: игрок сидит внизу, соперники занимают дугу
        /// от левого края через верх к правому. Один садится строго напротив, остальные
        /// расходятся до краёв сукна и плавно опускаются к углам.
        /// </summary>
        private Vector2 SeatPosition(int seatIndex, int opponentCount, SeatLayout layout)
        {
            if (opponentCount <= 1)
            {
                return new Vector2(0f, -SeatTopMargin);
            }

            var t = seatIndex / (float)(opponentCount - 1);
            var x = Mathf.Lerp(-layout.MaxX, layout.MaxX, t);

            // Высота падает по квадрату от середины ряда к краям: получается именно дуга,
            // а не ряд с двумя провалившимися углами, как было при четвёртой степени.
            var normalized = layout.MaxX > 0f ? x / layout.MaxX : 0f;
            var depth = layout.ArchDepth * normalized * normalized;

            var canvasRect = ((RectTransform)transform).rect;
            var figureHalf = AvatarWidth * 0.5f * layout.Scale;

            // Если место по горизонтали попадает в зону карт стола (на узком экране развести
            // шире просто некуда), опускать его туда нельзя — над картами нужен воздух,
            // иначе персонаж, его карты и стол сливаются в одну ленту.
            if (Mathf.Abs(x) - figureHalf < TableCardsHalfWidth)
            {
                var overCardsLimit = TableRowTop - TableCardsHalfHeight - SeatToCardsGap - SeatTopMargin - layout.BlockHeight;
                depth = Mathf.Min(depth, Mathf.Max(0f, overCardsLimit));
            }

            // А крайние места не должны сползти на колоду и кнопки в нижних углах.
            var cornerLimit = canvasRect.height - 350f - SeatTopMargin - layout.BlockHeight;
            depth = Mathf.Min(depth, Mathf.Max(0f, cornerLimit));

            return new Vector2(x, -(SeatTopMargin + depth));
        }

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

        private void RefreshHand(IReadOnlyList<DurakMove> legalMoves)
        {
            Clear(_handRow);

            var hand = _game.Players[HumanIndex].Hand;
            var count = hand.Count;
            _handHalfWidth = 0f;
            if (count == 0)
            {
                return;
            }

            const float handCardScale = 1.05f;
            var cardWidth = CardView.Width * handCardScale;
            var angleStep = Mathf.Min(5f, 22f / Mathf.Max(count - 1, 1));

            // Та же логика, что и у соперников: после "комфортного" числа карт веер сжимается,
            // а не растягивается на весь экран — видно, что карт много, но выглядит компактно.
            // Ширина ограничена ещё и экраном: по краям нижнего ряда стоят колода со своим
            // местом слева и кнопки справа, и веер не должен на них наезжать.
            const int comfortableHandCount = 8;
            var availableHandWidth = Mathf.Min(1500f, ((RectTransform)transform).rect.width - 1200f);
            if (count > comfortableHandCount)
            {
                availableHandWidth *= Mathf.Sqrt(comfortableHandCount / (float)count);
            }

            var spacing = Mathf.Min(cardWidth * 0.82f, availableHandWidth / count);
            var mid = (count - 1) / 2f;

            // Крайние карты наклонены, поэтому к половине ширины добавляем запас на наклон:
            // по этой границе рядом встаёт своё место, и карта его не задевает.
            _handHalfWidth = spacing * mid + cardWidth * 0.5f + 18f;

            var cheatMoves = _waitingForHuman && _settings.AllowCheating
                ? _game.GetCheatMoves(HumanIndex)
                : Array.Empty<DurakMove>();

            var choiceX = 0f;
            DurakMove? choiceDefend = null;
            DurakMove? choiceTransfer = null;

            for (var i = 0; i < count; i++)
            {
                var card = hand[i];
                var view = CardView.Create(_handRow, _font, handCardScale);
                view.ShowFace(card);

                var offset = i - mid;
                var isChoosing = _choiceCard.HasValue && _choiceCard.Value == card;
                var position = new Vector2(offset * spacing, -Mathf.Abs(offset) * 6f + (isChoosing ? ChoiceCardLift : 0f));
                view.SetRestTransform(position, isChoosing ? 0f : -offset * angleStep);

                if (!_waitingForHuman)
                {
                    view.SetPlain();
                    continue;
                }

                var direct = FindMoveForCard(legalMoves, card);
                var transfer = FindTransferMoveForCard(legalMoves, card);

                // Единственный неоднозначный случай — козырь того же достоинства: им можно
                // и отбиться, и перевести. Тогда карта не ходит сразу, а раскрывает выбор.
                if (direct.HasValue && transfer.HasValue)
                {
                    var capturedCard = card;
                    view.SetClickable(() => ToggleMoveChoice(capturedCard));

                    if (isChoosing)
                    {
                        choiceX = position.x;
                        choiceDefend = direct;
                        choiceTransfer = transfer;

                        // Уже поднята и стоит вровень с плашками — обычный наплыв курсора
                        // задрал бы её ещё выше и закрыл бы их собой.
                        view.DisableHover();
                    }

                    continue;
                }

                var move = direct ?? transfer;
                if (!move.HasValue && cheatMoves.Count > 0)
                {
                    move = FindCheatMoveForCard(cheatMoves, card);
                }

                if (move.HasValue)
                {
                    var captured = move.Value;
                    view.SetClickable(() =>
                    {
                        _choiceCard = null;
                        _pendingMove = captured;
                    });
                }
                else
                {
                    view.SetClickable(null);
                }
            }

            if (choiceDefend.HasValue && choiceTransfer.HasValue)
            {
                var chipY = ChoiceChipOffset(cardWidth);

                // Пара плашек уезжает вправо целиком, если у крайней левой карты она
                // иначе легла бы на своего же персонажа.
                var minCenter = PlayerSeatX() + AvatarWidth * 0.5f + 8f + ChoiceChipWidth * 0.5f;
                var leftX = Mathf.Max(choiceX - 66f, minCenter);

                CreateChoiceChip("отбить", leftX, chipY, DefendChipColor, choiceDefend.Value);
                CreateChoiceChip("перевести", leftX + 132f, chipY, TransferChipColor, choiceTransfer.Value);
            }
        }

        /// <summary>
        /// Плашки выбора висят ровно посередине полосы между поднятой картой и картами стола.
        /// Считаем от высоты холста: на широком окне Unity ужимает её, и зазор там самый узкий.
        /// </summary>
        private float ChoiceChipOffset(float cardWidth)
        {
            var canvasHeight = ((RectTransform)transform).rect.height;
            var cardHeight = cardWidth * (CardView.Height / CardView.Width);
            var handTop = HandRowBottom + cardHeight * 0.5f + ChoiceCardLift;
            var tableCardsBottom = canvasHeight - (TableRowTop + TableCardsHalfHeight);

            var center = Mathf.Max(handTop + ChoiceChipHeight * 0.5f + 8f, (handTop + tableCardsBottom) * 0.5f);
            return center - HandRowBottom;
        }

        /// <summary>Где стоит своё место: вплотную к левому краю веера, но не заезжая на колоду.</summary>
        private float PlayerSeatX()
        {
            var canvasHalf = ((RectTransform)transform).rect.width * 0.5f;
            return Mathf.Max(-(_handHalfWidth + AvatarWidth * 0.5f + 30f), -canvasHalf + 356f);
        }

        /// <summary>Клик по карте с двумя вариантами: раскрывает выбор или закрывает его обратно.</summary>
        private void ToggleMoveChoice(Card card)
        {
            _choiceCard = _choiceCard.HasValue && _choiceCard.Value == card ? (Card?)null : card;

            // Перерисовываем не здесь, а в игровом цикле: Refresh() удаляет ту самую карту,
            // чей обработчик клика сейчас выполняется.
            _choiceDirty = true;
        }

        private void CreateChoiceChip(string label, float x, float y, Color color, DurakMove move)
        {
            var go = new GameObject("MoveChoice", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Shadow), typeof(Button));
            go.transform.SetParent(_handRow, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(ChoiceChipWidth, ChoiceChipHeight);

            var image = go.GetComponent<Image>();
            image.sprite = UiTextures.RoundedRect(96, 48, 14f, color);
            image.type = Image.Type.Sliced;

            var shadow = go.GetComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.4f);
            shadow.effectDistance = new Vector2(2f, -3f);

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f, 1f);
            colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.onClick.AddListener(() =>
            {
                _choiceCard = null;
                _pendingMove = move;
            });

            var text = CreateText(rect, label, 22, TextAnchor.MiddleCenter);
            Stretch((RectTransform)text.transform);
        }

        private void RefreshOpponents()
        {
            Clear(_opponentsArea);

            var actingIndex = _waitingForHuman ? (int?)HumanIndex : DurakTurnOrder.GetActingPlayer(_game);
            var opponentCount = _game.Players.Count - 1;
            var layout = BuildSeatLayout(opponentCount);
            var seat = 0;

            foreach (var player in _game.Players)
            {
                if (player.Index == HumanIndex)
                {
                    continue;
                }

                var colorIndex = seat;
                var seatPosition = SeatPosition(seat, opponentCount, layout);
                seat++;

                var isActing = actingIndex.HasValue && actingIndex.Value == player.Index;
                var color = OpponentColors[colorIndex % OpponentColors.Length];
                // Веер уходит в свою сторону экрана: у левых соперников влево, у правых вправо.
                var fanSide = seatPosition.x < 0f ? -1f : 1f;
                var containerRect = BuildSeatBlock(_opponentsArea, player, color, isActing, layout.Scale, layout.FanWidth, fanSide);

                // Привязка к верхнему краю и пивот сверху: место задаётся отступом от верха
                // экрана, поэтому расстояние до надписей не зависит от пропорций окна.
                containerRect.anchorMin = containerRect.anchorMax = new Vector2(0.5f, 1f);
                containerRect.pivot = new Vector2(0.5f, 1f);
                containerRect.anchoredPosition = seatPosition;
            }
        }

        /// <summary>
        /// Своё место: тот же блок, что у соперников, но веер не рисуется — карты и так на руке.
        /// Стоит вплотную к левому краю веера и едет вместе с ним, когда карт становится больше:
        /// так сразу видно, что эти карты мои, а не просто лежат внизу экрана.
        /// </summary>
        private void RefreshPlayerSeat()
        {
            Clear(_playerSeatArea);

            _playerSeatArea.anchoredPosition = new Vector2(PlayerSeatX(), PlayerSeatBottom);

            var block = BuildSeatBlock(_playerSeatArea, _game.Players[HumanIndex], HumanColor, _waitingForHuman, 1f, 0f, 1f);
            block.anchorMin = block.anchorMax = new Vector2(0.5f, 0.5f);
            block.pivot = new Vector2(0.5f, 0.5f);
            block.anchoredPosition = Vector2.zero;
        }

        /// <summary>
        /// Место за столом: фигура, имя, бейдж роли и веер рубашек сбоку.
        /// Веер именно сбоку, а не под фигурой: иначе по вертикали всё слипается в ленту
        /// "персонаж — его карты — стол — своя рука", и воздуха между ними не остаётся.
        /// </summary>
        private RectTransform BuildSeatBlock(Transform parent, DurakPlayer player, Color color, bool isActing, float scale, float fanWidth, float fanSide)
        {
            var container = new GameObject("Seat", typeof(RectTransform));
            container.transform.SetParent(parent, false);

            var containerRect = (RectTransform)container.transform;
            containerRect.sizeDelta = new Vector2(AvatarWidth * scale, SeatBlockHeight * scale);

            var avatar = CreateAvatar(container.transform, player.Name.Substring(0, 1), color, isActing, PortraitFor(player.Index), scale);
            avatar.anchorMin = avatar.anchorMax = new Vector2(0.5f, 1f);
            avatar.pivot = new Vector2(0.5f, 1f);
            avatar.anchoredPosition = Vector2.zero;

            // Бейдж роли лежит поверх нижнего края фигуры, а не отдельной строкой —
            // так он не съедает высоту, которой и так впритык.
            if (player.Index == _game.DefenderIndex)
            {
                CreateRoleChip(avatar, "защита", new Color(0.24f, 0.45f, 0.78f), scale);
            }
            else if (player.Index == _game.AttackerIndex)
            {
                CreateRoleChip(avatar, "атака", new Color(0.72f, 0.31f, 0.20f), scale);
            }

            var label = CreateText(container.transform, player.Name, Mathf.RoundToInt(24 * scale), TextAnchor.MiddleCenter);
            label.color = isActing ? ActiveGlow : new Color(0.94f, 0.95f, 0.93f);
            PlaceUnderAvatar((RectTransform)label.transform, SeatNameOffset * scale, new Vector2(200f * scale, SeatNameHeight * scale));

            if (player.IsOut)
            {
                var outText = CreateText(container.transform, "вышел", Mathf.RoundToInt(20 * scale), TextAnchor.MiddleCenter);
                PlaceUnderAvatar((RectTransform)outText.transform, (SeatNameOffset + SeatNameHeight) * scale, new Vector2(160f * scale, 28f * scale));
                return containerRect;
            }

            if (fanWidth > 0f && player.Hand.Count > 0)
            {
                BuildSeatFan(containerRect, player.Hand.Count, scale, fanWidth, fanSide);
            }

            return containerRect;
        }

        /// <summary>
        /// Веер рубашек сбоку от фигуры, на высоте рук. Ширина задана снаружи, поэтому
        /// с ростом руки карты не разъезжаются вширь, а плотнее ложатся друг на друга.
        /// </summary>
        private void BuildSeatFan(RectTransform container, int handCount, float scale, float fanWidth, float fanSide)
        {
            var backScale = 0.55f * scale;
            var backWidth = CardView.Width * backScale;
            var angleStep = Mathf.Min(6f, 24f / Mathf.Max(handCount - 1, 1));
            var spacing = Mathf.Min(backWidth * 0.55f, Mathf.Max(fanWidth - backWidth, 0f) / Mathf.Max(handCount - 1, 1));

            var row = new GameObject("Cards", typeof(RectTransform));
            row.transform.SetParent(container, false);

            var rowRect = (RectTransform)row.transform;
            rowRect.anchorMin = rowRect.anchorMax = new Vector2(0.5f, 1f);
            // Пивот на той стороне, которой веер примыкает к фигуре, — так он растёт наружу.
            rowRect.pivot = new Vector2(fanSide < 0f ? 1f : 0f, 0.5f);
            rowRect.anchoredPosition = new Vector2(
                fanSide * (AvatarWidth * 0.5f + SeatFanGap) * scale,
                -AvatarHeight * 0.58f * scale);
            rowRect.sizeDelta = new Vector2(spacing * (handCount - 1) + backWidth, CardView.Height * backScale + 16f * scale);

            var mid = (handCount - 1) / 2f;
            for (var h = 0; h < handCount; h++)
            {
                var back = CardView.Create(row.transform, _font, backScale);
                back.ShowBack();
                back.SetPlain();

                var offset = h - mid;
                var position = new Vector2(offset * spacing, -Mathf.Abs(offset) * 3f);
                back.SetRestTransform(position, -offset * angleStep);
            }
        }

        private static void PlaceUnderAvatar(RectTransform rect, float offsetFromTop, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -offsetFromTop);
            rect.sizeDelta = size;
        }

        /// <summary>
        /// Чем больше соперников, тем меньше места на каждого. Пока их мало, фигуры крупные —
        /// ужимать приходится только когда стол действительно полный.
        /// </summary>
        private static float OpponentVisualScale(int opponentCount)
        {
            if (opponentCount <= 2) return 1f;
            if (opponentCount <= 4) return 0.9f;
            if (opponentCount <= 6) return 0.8f;
            return 0.7f;
        }

        private void CreateRoleChip(RectTransform avatar, string text, Color color, float scale)
        {
            var go = new GameObject("RoleChip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(avatar, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, 10f * scale);
            rect.sizeDelta = new Vector2(100f * scale, 32f * scale);

            var image = go.GetComponent<Image>();
            image.sprite = UiTextures.RoundedRect(64, 40, 14f, color);
            image.type = Image.Type.Sliced;
            image.raycastTarget = false;

            var label = CreateText(rect, text, Mathf.RoundToInt(18 * scale), TextAnchor.MiddleCenter);
            Stretch((RectTransform)label.transform);
            label.fontStyle = FontStyle.Bold;
        }

        /// <summary>
        /// Портреты берутся из Resources/Avatars и раздаются игрокам случайно на партию.
        /// Пока картинок нет, всё работает по-старому — цветной кружок с буквой.
        /// </summary>
        private void ShuffleAvatars()
        {
            if (_avatarPool == null)
            {
                _avatarPool = Resources.LoadAll<Sprite>("Avatars");
            }

            _playerPortraits = new Sprite[_game.Players.Count];
            if (_avatarPool.Length == 0)
            {
                return;
            }

            var order = new List<int>();
            for (var i = 0; i < _avatarPool.Length; i++)
            {
                order.Add(i);
            }

            for (var i = order.Count - 1; i > 0; i--)
            {
                var j = UnityEngine.Random.Range(0, i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            for (var player = 0; player < _playerPortraits.Length; player++)
            {
                _playerPortraits[player] = _avatarPool[order[player % order.Count]];
            }
        }

        private Sprite PortraitFor(int playerIndex)
        {
            return _playerPortraits != null && playerIndex < _playerPortraits.Length
                ? _playerPortraits[playerIndex]
                : null;
        }

        private RectTransform CreateAvatar(Transform parent, string initial, Color color, bool isActing, Sprite portrait, float scale)
        {
            var width = AvatarWidth * scale;
            var height = AvatarHeight * scale;

            var wrapper = new GameObject("Avatar", typeof(RectTransform));
            wrapper.transform.SetParent(parent, false);
            var wrapperRect = (RectTransform)wrapper.transform;
            wrapperRect.sizeDelta = new Vector2(width, height);

            if (isActing)
            {
                // Подсветка ходящего игрока: мягкое пятно света под персонажем вместо рамки,
                // потому что рамка вернула бы ту самую "картинку в коробочке", от которой ушли.
                var glow = new GameObject("Glow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                glow.transform.SetParent(wrapper.transform, false);
                var glowRect = (RectTransform)glow.transform;
                glowRect.anchorMin = glowRect.anchorMax = glowRect.pivot = new Vector2(0.5f, 0.5f);
                glowRect.sizeDelta = new Vector2(width * 1.35f, height * 0.95f);
                var glowImage = glow.GetComponent<Image>();
                glowImage.sprite = UiTextures.RadialGlow(96, ActiveGlow);
                glowImage.color = new Color(1f, 1f, 1f, 0.55f);
                glowImage.raycastTarget = false;
            }

            if (portrait == null)
            {
                var circle = new GameObject("Circle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Shadow));
                circle.transform.SetParent(wrapper.transform, false);
                var circleRect = (RectTransform)circle.transform;
                circleRect.anchorMin = circleRect.anchorMax = circleRect.pivot = new Vector2(0.5f, 0.5f);
                circleRect.sizeDelta = new Vector2(width * 0.75f, width * 0.75f);

                var circleShadow = circle.GetComponent<Shadow>();
                circleShadow.effectColor = new Color(0f, 0f, 0f, 0.45f);
                circleShadow.effectDistance = new Vector2(2f, -3f);

                var circleImage = circle.GetComponent<Image>();
                circleImage.sprite = UiTextures.Circle(64, color);
                circleImage.type = Image.Type.Sliced;

                var label = CreateText(circle.transform, initial.ToUpperInvariant(), Mathf.RoundToInt(36 * scale), TextAnchor.MiddleCenter);
                Stretch((RectTransform)label.transform);
                label.color = Color.white;
                label.fontStyle = FontStyle.Bold;
                return wrapperRect;
            }

            // Никакой подложки: персонаж вырезан по контуру и сидит прямо на сукне.
            var portraitGo = new GameObject("Portrait", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Shadow));
            portraitGo.transform.SetParent(wrapper.transform, false);

            var portraitRect = (RectTransform)portraitGo.transform;
            portraitRect.anchorMin = portraitRect.anchorMax = portraitRect.pivot = new Vector2(0.5f, 0.5f);
            portraitRect.sizeDelta = new Vector2(width, height);

            var portraitShadow = portraitGo.GetComponent<Shadow>();
            portraitShadow.effectColor = new Color(0f, 0f, 0f, 0.4f);
            portraitShadow.effectDistance = new Vector2(3f, -4f);

            var portraitImage = portraitGo.GetComponent<Image>();
            portraitImage.sprite = portrait;
            portraitImage.preserveAspect = true;
            portraitImage.raycastTarget = false;

            return wrapperRect;
        }

        private void RefreshControls(IReadOnlyList<DurakMove> legalMoves)
        {
            var canPass = legalMoves.Any(m => m.Type == DurakMoveType.Pass);
            var canTake = legalMoves.Any(m => m.Type == DurakMoveType.Take);

            _passButton.gameObject.SetActive(canPass);
            _takeButton.gameObject.SetActive(canTake);

            if (_game.IsFinished)
            {
                _status.color = new Color(0.94f, 0.95f, 0.93f);
                _status.text = _game.LoserIndex.HasValue
                    ? (_game.LoserIndex.Value == HumanIndex
                        ? "Ты проиграл — дурак!"
                        : "Дурак — " + _game.Players[_game.LoserIndex.Value].Name + ". Ты выиграл!")
                    : "Ничья.";
                return;
            }

            if (!_waitingForHuman)
            {
                _status.color = new Color(0.94f, 0.95f, 0.93f);
                _status.text = "Ходят соперники...";
                return;
            }

            _status.color = ActiveGlow;
            if (_game.DefenderIndex != HumanIndex)
            {
                _status.text = "Твой ход: подкинь карту";
                return;
            }

            // Подсказка про перевод появляется только когда он реально возможен.
            _status.text = legalMoves.Any(m => m.Type == DurakMoveType.Transfer)
                ? "Отбивайся, переведи картой того же достоинства или забери"
                : "Отбивайся: выбери карту или забери";
        }

        private DurakMove? FindMoveForCard(IReadOnlyList<DurakMove> legalMoves, Card card)
        {
            DurakMove? best = null;

            foreach (var move in legalMoves)
            {
                if (move.Card != card)
                {
                    continue;
                }

                if (move.Type == DurakMoveType.Attack)
                {
                    return move;
                }

                if (move.Type == DurakMoveType.Defend && (!best.HasValue || move.TargetSlot < best.Value.TargetSlot))
                {
                    best = move;
                }
            }

            return best;
        }

        private static DurakMove? FindTransferMoveForCard(IReadOnlyList<DurakMove> legalMoves, Card card)
        {
            foreach (var move in legalMoves)
            {
                if (move.Type == DurakMoveType.Transfer && move.Card == card)
                {
                    return move;
                }
            }

            return null;
        }

        /// <summary>
        /// Та же логика подбора, что и FindMoveForCard, но по списку из GetCheatMoves —
        /// только для карт, которые честно сыграть сейчас нельзя.
        /// </summary>
        private static DurakMove? FindCheatMoveForCard(IReadOnlyList<DurakMove> cheatMoves, Card card)
        {
            DurakMove? best = null;

            foreach (var move in cheatMoves)
            {
                if (move.Card != card)
                {
                    continue;
                }

                if (move.Type == DurakMoveType.CheatAttack)
                {
                    return move;
                }

                if (move.Type == DurakMoveType.CheatDefend && (!best.HasValue || move.TargetSlot < best.Value.TargetSlot))
                {
                    best = move;
                }
            }

            return best;
        }

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
            feltImage.sprite = UiTextures.RoundedRect(64, 64, 20f, new Color(0.08f, 0.32f, 0.20f));
            feltImage.type = Image.Type.Sliced;

            CreateCenterEmblem(root, new Color(0.08f, 0.32f, 0.20f));

            _status = CreateText(root, string.Empty, 36, TextAnchor.MiddleCenter);
            Place((RectTransform)_status.transform, new Vector2(0.5f, 1f), new Vector2(0f, -60f), new Vector2(1100f, 70f));

            // Соперники рассаживаются по дуге вокруг стола (см. SeatPosition), поэтому здесь
            // только пустая область на весь экран — каждое место позиционируется само.
            _opponentsArea = new GameObject("Opponents", typeof(RectTransform)).GetComponent<RectTransform>();
            _opponentsArea.SetParent(root, false);
            Stretch(_opponentsArea);

            _tableRow = CreatePanel("Table", root, new Vector2(0.5f, 1f), new Vector2(0f, -TableRowTop), new Vector2(1500f, 280f));
            var tableLayout = _tableRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            tableLayout.spacing = 16f;
            tableLayout.childAlignment = TextAnchor.MiddleCenter;
            tableLayout.childControlWidth = false;
            tableLayout.childControlHeight = false;
            tableLayout.childForceExpandWidth = false;
            tableLayout.childForceExpandHeight = false;

            // Своё место создаётся до руки, чтобы карты ложились поверх фигуры, а не наоборот.
            // Позиция по горизонтали считается в RefreshPlayerSeat — она зависит от ширины веера.
            _playerSeatArea = CreatePanel("PlayerSeat", root, new Vector2(0.5f, 0f), new Vector2(-520f, PlayerSeatBottom), new Vector2(180f, 200f));

            _handRow = CreatePanel("Hand", root, new Vector2(0.5f, 0f), new Vector2(0f, HandRowBottom), new Vector2(1600f, 200f));

            // Колода с козырем — в самом левом нижнем углу, ниже дуги мест и левее руки:
            // середину краёв стола теперь занимают крайние соперники.
            var trumpArea = CreatePanel("Trump", root, new Vector2(0f, 0f), new Vector2(170f, 170f), new Vector2(220f, 300f));

            const float trumpScale = 0.9f;
            var trumpFrame = new GameObject("TrumpFrame", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            trumpFrame.transform.SetParent(trumpArea, false);
            var trumpFrameRect = (RectTransform)trumpFrame.transform;
            trumpFrameRect.anchorMin = trumpFrameRect.anchorMax = trumpFrameRect.pivot = new Vector2(0.5f, 0.5f);
            trumpFrameRect.anchoredPosition = new Vector2(0f, -20f);
            trumpFrameRect.sizeDelta = new Vector2(CardView.Width * trumpScale + 18f, CardView.Height * trumpScale + 18f);
            var trumpFrameImage = trumpFrame.GetComponent<Image>();
            trumpFrameImage.sprite = UiTextures.RoundedRect(64, 64, 16f, ActiveGlow);
            trumpFrameImage.type = Image.Type.Sliced;
            trumpFrameImage.raycastTarget = false;
            _trumpFrame = trumpFrameRect;

            _trumpCard = CardView.Create(trumpArea, _font, trumpScale);
            _trumpCard.Rect.anchoredPosition = new Vector2(0f, -20f);

            _deckStackArea = CreatePanel("DeckStack", trumpArea, new Vector2(0.5f, 0.5f), new Vector2(-6f, 60f), new Vector2(100f, 100f));

            _trumpInfo = CreateText(trumpArea, string.Empty, 28, TextAnchor.MiddleCenter);
            _trumpInfo.color = ActiveGlow;
            _trumpInfo.fontStyle = FontStyle.Bold;
            Place((RectTransform)_trumpInfo.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, -100f), new Vector2(220f, 44f));

            _deckInfo = CreateText(trumpArea, string.Empty, 26, TextAnchor.MiddleCenter);
            Place((RectTransform)_deckInfo.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, -145f), new Vector2(220f, 50f));

            _lastAction = CreateText(root, string.Empty, 26, TextAnchor.MiddleCenter);
            _lastAction.color = new Color(0.85f, 0.92f, 0.85f, 0.9f);
            Place((RectTransform)_lastAction.transform, new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(900f, 40f));

            _passButton = CreateButton(root, "Бито / Пас", new Vector2(1f, 0f), new Vector2(-190f, 250f),
                new Color(0.21f, 0.45f, 0.29f), () => _pendingMove = DurakMove.Pass());

            _takeButton = CreateButton(root, "Забрать", new Vector2(1f, 0f), new Vector2(-190f, 150f),
                new Color(0.55f, 0.28f, 0.18f), () => _pendingMove = DurakMove.Take());

            _restartButton = CreateButton(root, "Новая партия", new Vector2(0.5f, 0.5f), new Vector2(0f, -190f),
                new Color(0.22f, 0.32f, 0.55f), StartNewGame);

            // Выход посреди партии: без него любую проверку настроек приходилось бы доигрывать до конца.
            var exitButton = CreateButton(root, "В меню", new Vector2(0f, 1f), new Vector2(150f, -70f),
                new Color(0.3f, 0.3f, 0.32f), () =>
                {
                    StopAllCoroutines();
                    _onExit?.Invoke();
                });
            ((RectTransform)exitButton.transform).sizeDelta = new Vector2(200f, 64f);
        }

        private void CreateCenterEmblem(Transform parent, Color feltColor)
        {
            var ring = new GameObject("Emblem", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            ring.transform.SetParent(parent, false);
            var ringRect = (RectTransform)ring.transform;
            ringRect.anchorMin = ringRect.anchorMax = new Vector2(0.5f, 1f);
            ringRect.pivot = new Vector2(0.5f, 0.5f);
            ringRect.anchoredPosition = new Vector2(0f, -TableRowTop);
            ringRect.sizeDelta = new Vector2(460f, 460f);
            var ringImage = ring.GetComponent<Image>();
            ringImage.sprite = UiTextures.Circle(128, Color.white);
            ringImage.type = Image.Type.Sliced;
            ringImage.color = new Color(1f, 0.93f, 0.7f, 0.08f);
            ringImage.raycastTarget = false;

            var inner = new GameObject("EmblemInner", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            inner.transform.SetParent(ring.transform, false);
            var innerRect = (RectTransform)inner.transform;
            innerRect.anchorMin = innerRect.anchorMax = innerRect.pivot = new Vector2(0.5f, 0.5f);
            innerRect.sizeDelta = new Vector2(446f, 446f);
            var innerImage = inner.GetComponent<Image>();
            innerImage.sprite = UiTextures.Circle(128, feltColor);
            innerImage.type = Image.Type.Sliced;
            innerImage.raycastTarget = false;

            var marks = CreateText(ring.transform, "♠  ♥  ♦  ♣", 76, TextAnchor.MiddleCenter);
            Stretch((RectTransform)marks.transform);
            marks.color = new Color(1f, 0.93f, 0.7f, 0.09f);
        }

        private Button CreateButton(Transform parent, string label, Vector2 anchor, Vector2 position, Color color, Action onClick)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Shadow), typeof(Button));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(280f, 76f);

            var image = go.GetComponent<Image>();
            image.sprite = UiTextures.RoundedRect(140, 76, 18f, color);
            image.type = Image.Type.Sliced;

            var shadow = go.GetComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.4f);
            shadow.effectDistance = new Vector2(3f, -4f);

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.onClick.AddListener(() => onClick());

            var text = CreateText(rect, label, 30, TextAnchor.MiddleCenter);
            Stretch((RectTransform)text.transform);

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
