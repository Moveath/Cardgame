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
        // Руки в блефе большие, поэтому вееру нужно заметно больше места, чем в дураке:
        // иначе рубашки слипаются в одно пятно.
        private const float SeatFanMaxWidth = 230f;

        /// <summary>Больше этого числа рубашек в веере не рисуем — дальше он только слипается.</summary>
        private const int MaxSeatFanCards = 12;
        private const float SeatFanGap = 12f;
        private const float SeatArcBiasSmall = 1.3f;
        private const float SeatArcBiasBig = 1.55f;

        private const float HandRowBottom = 130f;
        private const float RankRowBottom = 300f;

        // Выбранная карта поднимается — и не должна доставать до ряда рангов над рукой.
        private const float SelectedCardLift = 34f;

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
        private const float RevealHold = 1.6f;
        private const float FourHold = 1.1f;
        private const float FourFade = 0.35f;
        private const float FlightDuration = 0.5f;

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
        private Button _restartButton;

        private readonly List<Card> _selected = new List<Card>();
        private readonly List<Button> _rankButtons = new List<Button>();
        private readonly List<Rank> _rankButtonRanks = new List<Rank>();

        private Rank? _declaredRank;
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
                yield return new WaitForSeconds(BotThinkDelay);

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

        /// <summary>
        /// Каре ход не передаёт и решения не требует — собралось, значит уходит. Кнопку
        /// поэтому убрали: она только спрашивала очевидное. Но не молча: четыре карты
        /// сначала ложатся на стол лицом вверх, как это и делают за настоящим столом.
        /// </summary>
        private IEnumerator CollectFours()
        {
            if (!_game.Options.AllowFourOfAKind)
            {
                yield break;
            }

            for (var guard = 0; guard < 16 && !_game.IsFinished; guard++)
            {
                var owner = -1;
                var rank = Rank.Two;

                for (var i = 0; i < _game.Players.Count; i++)
                {
                    var fours = _game.AvailableFours(i);
                    if (fours.Count > 0)
                    {
                        owner = i;
                        rank = fours[0];
                        break;
                    }
                }

                if (owner < 0)
                {
                    yield break;
                }

                yield return ShowFourLeaving(owner, rank);

                _game.TryApplyMove(owner, BluffMove.DiscardFour(rank), out _);
                ResetChoice();
                Refresh();
            }
        }

        private IEnumerator ShowFourLeaving(int owner, Rank rank)
        {
            Clear(_revealArea);

            var caption = CreateText(_revealArea,
                _game.Players[owner].Name + ": каре " + RankPluralGenitive(rank) + " — из игры",
                30, TextAnchor.MiddleCenter);
            caption.color = AccentGlow;
            Place((RectTransform)caption.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, 150f), new Vector2(800f, 44f));

            const float scale = 1.05f;
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
                view.SetRestTransform(new Vector2(start + shown * step, 0f), 0f);
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
                    rect.anchoredPosition = new Vector2(start + i * step, k * 90f);
                    rect.localScale = Vector3.one * (1f - k * 0.25f);
                }

                yield return null;
            }

            Clear(_revealArea);
        }

        private void ResetChoice()
        {
            _selected.Clear();
            _declaredRank = null;
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

            var avatar = SeatFigure.Create(container.transform, _font, player.Name.Substring(0, 1),
                color, isActing, PortraitFor(player.Index), scale);
            avatar.anchorMin = avatar.anchorMax = new Vector2(0.5f, 1f);
            avatar.pivot = new Vector2(0.5f, 1f);
            avatar.anchoredPosition = Vector2.zero;

            var label = CreateText(container.transform, player.Name, Mathf.RoundToInt(24 * scale), TextAnchor.MiddleCenter);
            label.color = isActing ? AccentGlow : TextColor;
            PlaceUnderAvatar((RectTransform)label.transform, (SeatFigure.Height + SeatFigure.NameGap) * scale,
                new Vector2(220f * scale, SeatFigure.NameHeight * scale));

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

            _rankBadge.text = _game.CurrentRank.HasValue
                ? "сейчас ходят " + RankPluralNominative(_game.CurrentRank.Value)
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

            for (var i = 0; i < sorted.Count; i++)
            {
                var card = sorted[i];
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
            const float comfortable = 1.05f;
            if (count <= 1)
            {
                return comfortable;
            }

            var needed = CardView.Width * (MinHandStride * (count - 1) + 1f);
            if (needed * comfortable <= available)
            {
                return comfortable;
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

            return "Кидаю " + ThrowPhrase(rank.Value, _selected.Count);
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
            Rank.Jack => "валета",
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
            Rank.Jack => "валеты",
            Rank.Queen => "дамы",
            Rank.King => "короли",
            _ => "тузы"
        };

        private static string RankPluralGenitive(Rank rank) => BluffRules.RankPlural(rank);

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
            Place((RectTransform)_status.transform, new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(1200f, 56f));

            _lastAction = CreateText(root, string.Empty, 26, TextAnchor.MiddleCenter);
            _lastAction.color = DimColor;
            Place((RectTransform)_lastAction.transform, new Vector2(0.5f, 1f), new Vector2(0f, -100f), new Vector2(1100f, 36f));

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

            // Всё управление одной колонкой справа. Внизу слева стоит своё место, и каре
            // там налезало прямо на фигуру.
            _throwButton = CreateButton(root, "Кинуть", new Vector2(1f, 0f), new Vector2(-190f, 170f), new Vector2(260f, 76f),
                new Color(0.21f, 0.45f, 0.29f), Throw);

            // Выше нельзя: крайние места дуги спускаются до 360 от низа экрана.
            _challengeButton = CreateButton(root, "Не верю", new Vector2(1f, 0f), new Vector2(-190f, 310f), new Vector2(260f, 76f),
                new Color(0.62f, 0.26f, 0.22f), () => _pendingMove = BluffMove.Challenge());

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

            _pendingMove = _game.Phase == BluffPhase.RoundStart
                ? BluffMove.Declare(_declaredRank!.Value, cards)
                : BluffMove.Follow(cards);
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
