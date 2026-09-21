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
        private static readonly Color TruthColor = new Color(0.25f, 0.55f, 0.32f);

        // Вертикаль строится сверху вниз фиксированными отступами: на широком окне Unity
        // ужимает высоту холста, и доли экрана начинают наезжать друг на друга.
        private const float SeatTopMargin = 130f;
        private const float PotTop = 460f;
        private const float PotHalfHeight = 110f;

        /// <summary>Котёл — одна стопка, а не ряд пар, поэтому мёртвая зона под местами узкая.</summary>
        private const float PotHalfWidth = 200f;

        private const float SeatToPotGap = 40f;
        private const float FeltEdgeMargin = 56f;
        private const float SeatFanMaxWidth = 150f;
        private const float SeatFanGap = 12f;
        private const float SeatArcBiasSmall = 1.3f;
        private const float SeatArcBiasBig = 1.55f;

        private const float HandRowBottom = 130f;
        private const float RankRowBottom = 270f;
        private const float SelectedCardLift = 42f;

        private const float BotThinkDelay = 1.1f;
        private const float ThrowPause = 0.45f;
        private const float RevealHold = 1.6f;
        private const float FlightDuration = 0.5f;

        private BluffMatchSettings _settings = BluffMatchSettings.Default;
        private Action _onExit;

        private Font _font;
        private BluffGame _game;
        private SimpleBluffBot _bot;

        private Text _status;
        private Text _lastAction;
        private Text _potCaption;
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
        private Button _fourButton;
        private Button _restartButton;

        private readonly List<Card> _selected = new List<Card>();
        private readonly List<Button> _rankButtons = new List<Button>();
        private readonly List<Rank> _rankButtonRanks = new List<Rank>();
        private readonly List<Button> _countButtons = new List<Button>();

        private Rank? _declaredRank;
        private int _declaredCount;
        private bool _waitingForHuman;
        private BluffMove _pendingMove;

        private Sprite[] _playerPortraits;
        private float _handHalfWidth;

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
            _declaredCount = 0;
            _pendingMove = null;
            _waitingForHuman = false;
            _restartButton.gameObject.SetActive(false);
            Clear(_flyLayer);
            Clear(_revealArea);

            StopAllCoroutines();
            StartCoroutine(GameLoop());
        }

        // ---------- игровой цикл ----------

        private IEnumerator GameLoop()
        {
            while (!_game.IsFinished)
            {
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
                yield return new WaitForSeconds(BotThinkDelay);

                // Каре бот выкладывает отдельно: оно не занимает ход, поэтому после него
                // он тут же ходит по-настоящему.
                var four = _bot.ChooseFourOfAKind(_game, actor);
                if (four != null)
                {
                    yield return ApplyMove(actor, four);
                    continue;
                }

                var botMove = _bot.ChooseMove(_game, actor);
                if (botMove == null)
                {
                    break;
                }

                yield return ApplyMove(actor, botMove);
            }

            _waitingForHuman = false;
            Refresh();
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
            Refresh();

            if (wasChallenge)
            {
                yield return PlayReveal();
            }
            else
            {
                yield return new WaitForSeconds(ThrowPause);
            }
        }

        /// <summary>Каре ход не передаёт, поэтому применяется мимо очереди и мимо цикла.</summary>
        private void PlayFourOfAKind()
        {
            var fours = _game.AvailableFours(HumanIndex);
            if (fours.Count == 0)
            {
                return;
            }

            if (_game.TryApplyMove(HumanIndex, BluffMove.DiscardFour(fours[0]), out _))
            {
                ResetChoice();
                Refresh();
            }
        }

        private void ResetChoice()
        {
            _selected.Clear();
            _declaredRank = null;
            _declaredCount = 0;
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

            var lied = entry.WasLie == true;
            var verdict = CreateText(_revealArea, lied ? "Соврал!" : "Правда!", 44, TextAnchor.MiddleCenter);
            verdict.color = lied ? LieColor : TruthColor;
            verdict.fontStyle = FontStyle.Bold;
            Place((RectTransform)verdict.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, 150f), new Vector2(600f, 60f));

            var claimed = entry.DeclaredCount ?? 0;
            var claim = CreateText(_revealArea,
                "заявлено: " + claimed + " " + BluffRules.CardsWord(claimed)
                + " · " + BluffRules.RankPlural(entry.DeclaredRank!.Value),
                26, TextAnchor.MiddleCenter);
            claim.color = DimColor;
            Place((RectTransform)claim.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, 104f), new Vector2(700f, 40f));

            const float revealScale = 1.15f;
            var step = CardView.Width * revealScale + 18f;
            var start = -(entry.RevealedCards.Count - 1) * step / 2f;

            for (var i = 0; i < entry.RevealedCards.Count; i++)
            {
                var view = CardView.Create(_revealArea, _font, revealScale);
                view.ShowFace(entry.RevealedCards[i]);
                view.SetPlain();
                view.SetRestTransform(new Vector2(start + i * step, 0f), 0f);
                view.PlayAppearAnimation();
            }

            yield return new WaitForSeconds(RevealHold);

            Clear(_revealArea);

            if (entry.TakenBy.HasValue && entry.TakenCards.HasValue && entry.TakenCards.Value > 0)
            {
                yield return FlyPotTo(entry.TakenBy.Value, entry.TakenCards.Value);
            }

            Refresh();
        }

        private IEnumerator FlyPotTo(int playerIndex, int count)
        {
            var from = PotCenter();
            var to = SeatAnchor(playerIndex);

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
            RefreshOpponents();
            RefreshPlayerSeat();
            RefreshPot();
            RefreshHand();
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
            if (_game.Phase == BluffPhase.RoundStart && !_declaredRank.HasValue)
            {
                return "Твой круг: назови ранг";
            }

            if (_selected.Count == 0)
            {
                return _game.Phase == BluffPhase.RoundStart
                    ? "Выбери в руке от одной до трёх карт — любых"
                    : "Верить или нет? Положи свои карты или скажи «не верю»";
            }

            return "Заявить можно и не то число, что кладёшь";
        }

        private void RefreshOpponents()
        {
            Clear(_opponentsArea);

            var opponents = _game.Players.Count - 1;
            var layout = BuildSeatLayout(opponents);

            for (var seat = 0; seat < opponents; seat++)
            {
                var player = _game.Players[seat + 1];
                var container = BuildSeatBlock(_opponentsArea, player, layout.Scale);
                container.anchorMin = container.anchorMax = new Vector2(0.5f, 1f);
                container.pivot = new Vector2(0.5f, 0f);
                container.anchoredPosition = SeatPosition(seat, opponents, layout);
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

        private RectTransform BuildSeatBlock(Transform parent, BluffPlayer player, float scale)
        {
            var container = new GameObject("Seat", typeof(RectTransform));
            container.transform.SetParent(parent, false);

            var containerRect = (RectTransform)container.transform;
            containerRect.sizeDelta = new Vector2(SeatFigure.Width * scale, SeatFigure.BlockHeight * scale);

            var isActing = !_game.IsFinished && player.Index == _game.CurrentPlayerIndex;
            var color = player.Index == HumanIndex ? HumanColor : ChipColor;

            var avatar = SeatFigure.Create(container.transform, _font, player.Name.Substring(0, 1),
                color, isActing, PortraitFor(player.Index), scale);
            avatar.anchorMin = avatar.anchorMax = new Vector2(0.5f, 1f);
            avatar.pivot = new Vector2(0.5f, 1f);
            avatar.anchoredPosition = Vector2.zero;

            var label = CreateText(container.transform, player.Name, Mathf.RoundToInt(24 * scale), TextAnchor.MiddleCenter);
            label.color = isActing ? AccentGlow : TextColor;
            PlaceUnderAvatar((RectTransform)label.transform, (SeatFigure.Height + SeatFigure.NameGap) * scale,
                new Vector2(220f * scale, SeatFigure.NameHeight * scale));

            // Сколько карт на руках — информация публичная: за столом это видно и так.
            var count = player.IsOut ? "вышел" : player.Hand.Count + " " + BluffRules.CardsWord(player.Hand.Count);
            var countText = CreateText(container.transform, count, Mathf.RoundToInt(20 * scale), TextAnchor.MiddleCenter);
            countText.color = DimColor;
            PlaceUnderAvatar((RectTransform)countText.transform,
                (SeatFigure.Height + SeatFigure.NameGap + SeatFigure.NameHeight) * scale,
                new Vector2(200f * scale, 26f * scale));

            return containerRect;
        }

        private void RefreshPot()
        {
            Clear(_potArea);

            // Показываем ровно то, что слышали за столом: сумму заявленных пакетов.
            // Настоящий размер котла — скрытая информация, и подсматривать её незачем.
            var declared = _game.DeclaredPotCount;
            var visible = Mathf.Clamp(_game.Packets.Count, 0, 6);

            for (var i = 0; i < visible; i++)
            {
                var view = CardView.Create(_potArea, _font, 1.1f);
                view.ShowBack();
                view.SetPlain();
                view.SetRestTransform(new Vector2(i * 7f - visible * 3f, i * -5f + visible * 2f), (i - visible * 0.5f) * 3f);
            }

            _potCaption.text = declared == 0
                ? "котёл пуст"
                : "в котле по заявкам: " + declared + " " + BluffRules.CardsWord(declared);

            _rankBadge.text = _game.CurrentRank.HasValue
                ? "круг идёт под " + BluffRules.RankPlural(_game.CurrentRank.Value)
                : "ранг ещё не назван";
        }

        private void RefreshHand()
        {
            Clear(_handRow);

            var hand = _game.Players[HumanIndex].Hand;
            _handHalfWidth = 0f;
            if (hand.Count == 0)
            {
                return;
            }

            const float handCardScale = 1.05f;
            var cardWidth = CardView.Width * handCardScale;
            var angleStep = Mathf.Min(5f, 22f / Mathf.Max(hand.Count - 1, 1));

            const int comfortable = 8;
            var available = Mathf.Min(1500f, ((RectTransform)transform).rect.width - 1200f);
            if (hand.Count > comfortable)
            {
                available *= Mathf.Sqrt(comfortable / (float)hand.Count);
            }

            var spacing = Mathf.Min(cardWidth * 0.82f, available / hand.Count);
            var mid = (hand.Count - 1) / 2f;
            _handHalfWidth = spacing * mid + cardWidth * 0.5f + 18f;

            for (var i = 0; i < hand.Count; i++)
            {
                var card = hand[i];
                var view = CardView.Create(_handRow, _font, handCardScale);
                view.ShowFace(card);

                var offset = i - mid;
                var isSelected = _selected.Contains(card);
                var position = new Vector2(offset * spacing, -Mathf.Abs(offset) * 6f + (isSelected ? SelectedCardLift : 0f));
                view.SetRestTransform(position, isSelected ? 0f : -offset * angleStep);

                if (!_waitingForHuman)
                {
                    view.SetPlain();
                    continue;
                }

                var captured = card;
                view.SetClickable(() => ToggleCard(captured));
            }
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

            // Заявленное число по умолчанию честное — врать им нужно осознанно, отдельным тапом.
            _declaredCount = _selected.Count;
            Refresh();
        }

        private void RefreshRankRow()
        {
            var declaring = _waitingForHuman && _game.Phase == BluffPhase.RoundStart;
            _rankRow.gameObject.SetActive(declaring);

            for (var i = 0; i < _rankButtons.Count; i++)
            {
                var selected = _declaredRank.HasValue && _declaredRank.Value == _rankButtonRanks[i];
                _rankButtons[i].GetComponent<Outline>().enabled = selected;
            }

            for (var i = 0; i < _countButtons.Count; i++)
            {
                _countButtons[i].GetComponent<Outline>().enabled = _declaredCount == i + 1;
                _countButtons[i].gameObject.SetActive(_waitingForHuman);
            }
        }

        private void RefreshControls()
        {
            var canThrow = _waitingForHuman
                           && _selected.Count > 0
                           && _declaredCount > 0
                           && (_game.Phase != BluffPhase.RoundStart || _declaredRank.HasValue);

            _throwButton.gameObject.SetActive(_waitingForHuman);
            _throwButton.interactable = canThrow;

            _challengeButton.gameObject.SetActive(_waitingForHuman && _game.Phase == BluffPhase.Chain);

            var fours = _game.IsFinished ? new Rank[0] : _game.AvailableFours(HumanIndex);
            _fourButton.gameObject.SetActive(fours.Count > 0);
            if (fours.Count > 0)
            {
                _fourButton.GetComponentInChildren<Text>().text = "Каре " + BluffRules.RankPlural(fours[0]);
            }

        }

        private void ShowResult()
        {
            _status.color = AccentGlow;

            if (_game.Losers.Count == 0)
            {
                _status.text = "Партия окончена.";
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
                _status.text = "Поровну тузов — проигрыш на всех.";
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

            var corner = ((RectTransform)transform).rect.height - 360f - SeatTopMargin - layout.BlockHeight;
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

            _status = CreateText(root, string.Empty, 36, TextAnchor.MiddleCenter);
            Place((RectTransform)_status.transform, new Vector2(0.5f, 1f), new Vector2(0f, -54f), new Vector2(1200f, 66f));

            _lastAction = CreateText(root, string.Empty, 26, TextAnchor.MiddleCenter);
            _lastAction.color = DimColor;
            Place((RectTransform)_lastAction.transform, new Vector2(0.5f, 1f), new Vector2(0f, -102f), new Vector2(1100f, 38f));

            _opponentsArea = new GameObject("Opponents", typeof(RectTransform)).GetComponent<RectTransform>();
            _opponentsArea.SetParent(root, false);
            Stretch(_opponentsArea);

            _potArea = CreatePanel("Pot", root, new Vector2(0.5f, 1f), new Vector2(0f, -PotTop), new Vector2(260f, 240f));

            _rankBadge = CreateText(root, string.Empty, 28, TextAnchor.MiddleCenter);
            _rankBadge.color = AccentGlow;
            Place((RectTransform)_rankBadge.transform, new Vector2(0.5f, 1f), new Vector2(0f, -(PotTop + 140f)), new Vector2(700f, 38f));

            _potCaption = CreateText(root, string.Empty, 24, TextAnchor.MiddleCenter);
            _potCaption.color = DimColor;
            Place((RectTransform)_potCaption.transform, new Vector2(0.5f, 1f), new Vector2(0f, -(PotTop + 180f)), new Vector2(700f, 34f));

            _playerSeatArea = CreatePanel("PlayerSeat", root, new Vector2(0.5f, 0f), new Vector2(-520f, 150f), new Vector2(180f, 220f));
            _handRow = CreatePanel("Hand", root, new Vector2(0.5f, 0f), new Vector2(0f, HandRowBottom), new Vector2(1600f, 200f));

            BuildRankRow(root);
            BuildControls(root);

            _revealArea = CreatePanel("Reveal", root, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1200f, 400f));
            _flyLayer = CreatePanel("Fly", root, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(10f, 10f));
        }

        private void BuildRankRow(RectTransform root)
        {
            _rankRow = CreatePanel("Ranks", root, new Vector2(0.5f, 0f), new Vector2(0f, RankRowBottom), new Vector2(1600f, 60f));

            var ranks = AvailableRanks();
            var spacing = Mathf.Min(96f, 1400f / Mathf.Max(ranks.Count, 1));
            var start = -(ranks.Count - 1) * spacing / 2f;

            for (var i = 0; i < ranks.Count; i++)
            {
                var rank = ranks[i];
                var button = CreateChip(_rankRow, RankShort(rank), new Vector2(start + i * spacing, 0f),
                    new Vector2(spacing - 12f, 56f), () => SelectRank(rank));
                _rankButtons.Add(button);
                _rankButtonRanks.Add(rank);
            }
        }

        private void BuildControls(RectTransform root)
        {
            CreateButton(root, "В меню", new Vector2(0f, 1f), new Vector2(120f, -60f), new Vector2(180f, 64f),
                new Color(0.3f, 0.3f, 0.32f), () =>
                {
                    StopAllCoroutines();
                    _onExit?.Invoke();
                });

            _throwButton = CreateButton(root, "Кинуть", new Vector2(1f, 0f), new Vector2(-190f, 170f), new Vector2(260f, 76f),
                new Color(0.21f, 0.45f, 0.29f), Throw);

            _challengeButton = CreateButton(root, "Не верю", new Vector2(1f, 0f), new Vector2(-190f, 262f), new Vector2(260f, 76f),
                new Color(0.62f, 0.26f, 0.22f), () => _pendingMove = BluffMove.Challenge());

            _fourButton = CreateButton(root, "Каре", new Vector2(0f, 0f), new Vector2(200f, 170f), new Vector2(260f, 68f),
                new Color(0.42f, 0.26f, 0.45f), PlayFourOfAKind);

            _restartButton = CreateButton(root, "Ещё партия", new Vector2(0.5f, 0.5f), new Vector2(0f, -120f), new Vector2(300f, 80f),
                new Color(0.21f, 0.45f, 0.29f), StartNewGame);
            _restartButton.gameObject.SetActive(false);

            // Заявленное количество отдельно от выбранных карт: в этом вся вторая ось блефа.
            var countLabel = CreateText(root, "заявить", 22, TextAnchor.MiddleCenter);
            countLabel.color = DimColor;
            Place((RectTransform)countLabel.transform, new Vector2(1f, 0f), new Vector2(-190f, 390f), new Vector2(200f, 28f));

            for (var count = 1; count <= 3; count++)
            {
                var captured = count;
                var button = CreateChip(root, count.ToString(), new Vector2(-270f + (count - 1) * 80f, 348f),
                    new Vector2(70f, 56f), () => SelectCount(captured));
                var rect = (RectTransform)button.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
                _countButtons.Add(button);
            }

        }

        private void SelectRank(Rank rank)
        {
            _declaredRank = rank;
            Refresh();
        }

        private void SelectCount(int count)
        {
            _declaredCount = count;
            Refresh();
        }

        private void Throw()
        {
            if (_selected.Count == 0 || _declaredCount <= 0)
            {
                return;
            }

            var cards = new List<Card>(_selected);

            _pendingMove = _game.Phase == BluffPhase.RoundStart
                ? BluffMove.Declare(_declaredRank!.Value, _declaredCount, cards)
                : BluffMove.Follow(_declaredCount, cards);
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
