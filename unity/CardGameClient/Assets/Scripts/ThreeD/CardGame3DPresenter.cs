using System.Collections.Generic;
using System.Linq;
using CardGame.Bots;
using CardGame.Core.Cards;
using CardGame.Core.Games.Durak;
using CardGame.Core.Meta;
using UnityEngine;

namespace CardGameClient.ThreeD
{
    /// <summary>
    /// Связывает DurakGame с 3D-столом: правила только в ядре, здесь — где лежит каждая видимая карта.
    /// Открытая карта живёт как один объект: перелетает из руки на стол, со стола в биту или к взявшему.
    /// </summary>
    public sealed class CardGame3DPresenter : MonoBehaviour
    {
        private const int HumanIndex = 0;
        private const float BotDelay = .95f;
        private const int DiscardShown = 24;
        private static readonly string[] BotNames = { "Борис", "Вера", "Игорь", "Ольга", "Пётр" };

        private const float FanGap = .0009f;   // Шаг карт веера к камере: больше толщины карты, чтобы они не прорастали друг в друга.
        // Хват подобран под веер из шести карт (scripts/3d/hands/in_game.py, FAN_FITTED): при другом числе карт передняя карта
        // остаётся на той же глубине, под большим пальцем, а стопка растёт от игрока, за карты. Раньше она росла к игроку,
        // и при 9+ картах большой палец тонул в стопке.
        private const int FanFitted = 6;

        private Camera _camera;
        private TableCameraController _look;
        private OwnArms3DView _arms;
        private CardTable3DHud _hud;
        private Transform _world;
        private PlayerSeat3DView[] _seats = new PlayerSeat3DView[0];
        private readonly Dictionary<Card, Card3DView> _cards = new Dictionary<Card, Card3DView>();
        private readonly List<Card3DView> _discard = new List<Card3DView>();
        private GameObject _deck;
        private Transform _deckStack;
        private DurakGame _game;
        private SimpleDurakBot _bot;
        private Card? _selected;
        private float _nextBotMove;
        private int _discardSeed;
        private bool _reported;

        public DurakGame Game => _game;
        public Camera EyeCamera => _camera;
        public IReadOnlyDictionary<Card, Card3DView> Cards => _cards;

        public void Configure(Camera camera, TableCameraController look, CardTable3DHud hud, Transform world, OwnArms3DView arms)
        {
            _camera = camera;
            _arms = arms;
            _look = look;
            _hud = hud;
            _world = world;
            _look.Tapped += OnScreenTapped;
            _hud.SetCallbacks(TakeCards, PassBout, TransferSelected, StartNewGame, CardTable3DRoomFlow.ReturnToLobby);
            BuildDeck();
            StartNewGame();
        }

        private void Update()
        {
            if (_game == null || _game.IsFinished) return;
            var actor = DurakTurnOrder.GetActingPlayer(_game);
            if (actor.HasValue && actor.Value != HumanIndex && Time.time >= _nextBotMove) PlayBotMove(actor.Value);
            else if (!actor.HasValue && _game.HasChallengeableMove)
            {
                _game.CloseChallengeWindow();
                RefreshViews();
            }
        }

        private void LateUpdate()
        {
            if (_game == null) return;
            for (var i = 0; i < _seats.Length; i++) _hud.PlaceSeat(i, _camera, _seats[i].LabelPoint);
        }

        private void StartNewGame()
        {
            var room = CardTable3DRoomFlow.Current;
            var names = room?.PlayerNames != null && room.PlayerNames.Length > 0
                ? room.PlayerNames
                : new[] { PlayerHubRuntime.DisplayName, BotNames[0], BotNames[1], BotNames[2], BotNames[3], BotNames[4] };
            var settings = room?.Settings ?? HubGameSettings.DefaultFor("durak");
            var deck = settings.DeckSize == 24 ? DeckKind.Twenty4 : settings.DeckSize == 52 ? DeckKind.FiftyTwo :
                settings.DeckSize == 54 ? DeckKind.FiftyFour : DeckKind.ThirtySix;
            var match = new DurakMatchSettings
            {
                PlayerCount = names.Length,
                DeckSize = deck,
                AllowTransfer = settings.AllowTransfer,
                ThrowInMode = settings.NeighboursOnly ? ThrowInMode.NeighboursOnly : ThrowInMode.All,
                AllowCheating = settings.AllowCheating
            };
            ClearTable();
            _game = DurakGame.Create(names, match.BuildDeck(Random.Range(1, int.MaxValue)), match.BuildOptions());
            _bot = new SimpleDurakBot(new System.Random(Random.Range(1, int.MaxValue)));
            _selected = null;
            _reported = false;
            _nextBotMove = Time.time + 1.2f;
            BuildSeats();
            _hud.SetTrump(_game.Trump);
            RefreshViews();
        }

        private void ClearTable()
        {
            _arms.Clear();
            foreach (var card in _cards.Values) Destroy(card.gameObject);
            foreach (var card in _discard) Destroy(card.gameObject);
            _cards.Clear();
            _discard.Clear();
        }

        private void BuildSeats()
        {
            var opponents = _game.Players.Count - 1;
            if (_seats.Length == opponents) return;
            foreach (var seat in _seats) Destroy(seat.gameObject);
            var angles = Table3DLayout.SeatAngles(opponents);
            _seats = new PlayerSeat3DView[opponents];
            for (var i = 0; i < opponents; i++) _seats[i] = PlayerSeat3DView.Create(_world, i + 1, angles[i]);
            _hud.BuildSeats(opponents);
        }

        private void BuildDeck()
        {
            _deck = new GameObject("Deck");
            _deck.transform.SetParent(_world, false);
            _deck.transform.position = Table3DLayout.DeckAt;
            var width = Table3DLayout.CardHeight * Table3DLayout.CardAspect * Table3DLayout.TableCardScale;
            var height = Table3DLayout.CardHeight * Table3DLayout.TableCardScale;
            _deckStack = Table3DArt.Block(_deck.transform, Vector3.zero, new Vector3(width, .01f, height),
                Table3DArt.Lit("deck edge", new Color(.86f, .8f, .68f), .1f)).transform;
            var top = Card3DView.Create(_deck.transform, new Card(Rank.Nine, Suit.Clubs), false, false);
            top.name = "Deck top";
        }

        private void PaintDeck()
        {
            var count = _game.DeckCount;
            _deck.SetActive(count > 0);
            // Толщина ступенями: точного числа карт в колоде не видно.
            var height = count <= 6 ? .012f : count <= 18 ? .024f : .036f;
            _deckStack.localScale = new Vector3(_deckStack.localScale.x, height, _deckStack.localScale.z);
            _deckStack.localPosition = new Vector3(0f, height / 2 + .012f, 0f);
            var top = _deck.transform.Find("Deck top").GetComponent<Card3DView>();
            top.Place(_deck.transform, new Vector3(0f, height + .0125f, 0f), Quaternion.Euler(-90f, 0f, 0f), Table3DLayout.TableCardScale);
        }

        private void PlayBotMove(int actor)
        {
            _nextBotMove = Time.time + BotDelay;
            var move = _bot.ChooseMove(_game, actor);
            if (!move.HasValue) return;
            if (!_game.TryApplyMove(actor, move.Value, out var error))
            {
                Debug.LogWarning("3D-дурак: ход бота отклонён: " + error);
                return;
            }
            RefreshViews(actor);
        }

        private void ApplyHumanMove(DurakMove move)
        {
            if (!_game.TryApplyMove(HumanIndex, move, out var error))
            {
                Debug.LogWarning("3D-дурак: ход отклонён: " + error);
                return;
            }
            _selected = null;
            _nextBotMove = Time.time + BotDelay;
            RefreshViews();
        }

        private void TakeCards()
        {
            if (HumanMay(DurakMoveType.Take)) ApplyHumanMove(DurakMove.Take());
        }

        private void PassBout()
        {
            if (HumanMay(DurakMoveType.Pass)) ApplyHumanMove(DurakMove.Pass());
        }

        private void TransferSelected()
        {
            var moves = HumanMoves(_selected).Where(m => m.Type == DurakMoveType.Transfer).ToArray();
            if (moves.Length > 0) ApplyHumanMove(moves[0]);
        }

        private bool HumanMay(DurakMoveType type) => _game != null && HumanActs && _game.GetLegalMoves(HumanIndex).Any(m => m.Type == type);

        private bool HumanActs => DurakTurnOrder.GetActingPlayer(_game) == HumanIndex;

        private IEnumerable<DurakMove> HumanMoves(Card? card)
        {
            if (!card.HasValue || !HumanActs) return Enumerable.Empty<DurakMove>();
            return _game.GetLegalMoves(HumanIndex).Where(m => m.Card == card.Value &&
                (m.Type == DurakMoveType.Attack || m.Type == DurakMoveType.Defend || m.Type == DurakMoveType.Transfer));
        }

        private void OnScreenTapped(Vector2 screen)
        {
            if (_game == null || !HumanActs) return;
            var view = CardUnder(screen);
            if (view == null) return;
            if (_game.Players[HumanIndex].Hand.Contains(view.Card)) ChooseCard(view.Card);
            else if (_selected.HasValue) ChooseTarget(view.Card);
        }

        /// <summary>Верхняя карта под пальцем. Сыгранная карта ещё пролетает над веером: нажатие достаётся картам под ней.</summary>
        private Card3DView CardUnder(Vector2 screen)
        {
            Physics.SyncTransforms(); // Карты двигаются не физикой: без этого луч попал бы туда, где они были на прошлом шаге.
            var hand = _game.Players[HumanIndex].Hand;
            foreach (var hit in Physics.RaycastAll(_camera.ScreenPointToRay(screen), 10f).OrderBy(h => h.distance))
            {
                var view = hit.collider.GetComponentInParent<Card3DView>();
                if (view != null && (!view.IsFlying || hand.Contains(view.Card))) return view;
            }
            return null;
        }

        /// <summary>Один вариант хода — играется сразу; несколько — карта поднимается и ждёт цель на столе или «ПЕРЕВЕСТИ».</summary>
        public void ChooseCard(Card card)
        {
            var moves = HumanMoves(card).ToArray();
            if (moves.Length == 0) return;
            if (moves.Length == 1)
            {
                ApplyHumanMove(moves[0]);
                return;
            }
            _selected = _selected == card ? (Card?)null : card;
            RefreshViews();
        }

        private void ChooseTarget(Card tableCard)
        {
            for (var slot = 0; slot < _game.Table.Count; slot++)
            {
                if (_game.Table[slot].Attack != tableCard) continue;
                var moves = HumanMoves(_selected).Where(m => m.Type == DurakMoveType.Defend && m.TargetSlot == slot).ToArray();
                if (moves.Length > 0) ApplyHumanMove(moves[0]);
                return;
            }
        }

        private void RefreshViews(int? moved = null)
        {
            var shown = new HashSet<Card>();
            PlaceTable(shown, moved);
            PlaceHand(shown);
            PlaceTrump(shown);
            SweepGone(shown);
            PaintDeck();
            for (var i = 0; i < _seats.Length; i++)
            {
                var player = _game.Players[i + 1];
                _seats[i].SetHolding(!player.IsOut && player.Hand.Count > 0);
            }
            PaintHud();
            ReportIfFinished();
        }

        private void PlaceTable(HashSet<Card> shown, int? moved)
        {
            var table = _game.Table;
            for (var slot = 0; slot < table.Count; slot++)
            {
                var at = Table3DLayout.PairPosition(slot, table.Count);
                PutOnTable(table[slot].Attack, at + new Vector3(-.016f, 0f, .03f), shown, moved, 0f);
                if (table[slot].Defense.HasValue)
                    PutOnTable(table[slot].Defense.Value, at + new Vector3(.024f, .001f, -.024f), shown, moved, 6f);
            }
        }

        private void PutOnTable(Card card, Vector3 at, HashSet<Card> shown, int? moved, float turn)
        {
            shown.Add(card);
            var view = Find(card, moved.HasValue ? _seats[moved.Value - 1].HandAnchor.position : Table3DLayout.DeckAt);
            // Карты брошены рукой: у каждой свой постоянный небольшой наклон.
            var tilt = ((int)card.Rank * 7 + (int)card.Suit * 3) % 7 - 3 + turn;
            view.SetSelected(false);
            view.SetDimmed(false);
            // Карта только что ушла из веера игрока: её забирает правая рука, а падает она уже на это место.
            if (view.transform.parent == _arms.FanRoot && !view.IsHeld) _arms.PlayCard(view);
            view.Land(_world, at, Quaternion.Euler(90f, tilt, 0f), Table3DLayout.TableCardScale);
        }

        private void PlaceHand(HashSet<Card> shown)
        {
            var hand = _game.Players[HumanIndex].Hand.ToList();
            hand.Sort(CompareInHand);
            var step = hand.Count > 1 ? Mathf.Min(Table3DLayout.HandFanStep, Table3DLayout.HandFanLimit / (hand.Count - 1)) : 0f;
            var legal = HumanActs ? _game.GetLegalMoves(HumanIndex) : null;
            for (var i = 0; i < hand.Count; i++)
            {
                var card = hand[i];
                shown.Add(card);
                var view = Find(card, Table3DLayout.DeckAt);
                var spin = Quaternion.Euler(0f, 0f, (hand.Count - 1) / 2f * step - i * step);
                var selected = _selected == card;
                var along = spin * Vector3.up * (Table3DLayout.HandFanRadius + (selected ? Table3DLayout.HandLift : 0f));
                view.SetSelected(selected);
                view.SetDimmed(legal != null && !legal.Any(m => m.Card == card));
                // Веер держит левая кисть: его середина и наклон заданы точкой Fan на ней.
                // Передняя карта всегда на одной глубине, под большим пальцем; стопка растёт от игрока, за карты (FanFitted).
                view.MoveTo(_arms.FanRoot, along + new Vector3(0f, 0f, (hand.Count - FanFitted - i) * FanGap), spin, Table3DLayout.HandCardScale);
            }
        }

        private void PlaceTrump(HashSet<Card> shown)
        {
            if (_game.DeckCount == 0) return;
            var card = _game.TrumpCard;
            shown.Add(card);
            var view = Find(card, Table3DLayout.DeckAt);
            view.SetSelected(false);
            view.SetDimmed(false);
            // Колода у правого края стола: козырь выглядывает из-под неё к середине.
            view.MoveTo(_world, Table3DLayout.DeckAt + new Vector3(-.088f, .001f, -.012f), Quaternion.Euler(90f, -84f, 0f), Table3DLayout.TableCardScale);
        }

        /// <summary>Карта ушла со стола: к боту, у которого теперь в руке, иначе в биту.</summary>
        private void SweepGone(HashSet<Card> shown)
        {
            foreach (var card in _cards.Keys.Where(c => !shown.Contains(c)).ToArray())
            {
                var view = _cards[card];
                _cards.Remove(card);
                var holder = _game.Players.FirstOrDefault(p => p.Index != HumanIndex && p.Hand.Contains(card));
                if (holder != null)
                {
                    view.MoveTo(_world, _world.InverseTransformPoint(_seats[holder.Index - 1].HandAnchor.position), Quaternion.identity, 1f,
                        () => Destroy(view.gameObject));
                    continue;
                }
                var random = new System.Random(_discardSeed++);
                var at = Table3DLayout.DiscardAt + new Vector3((float)random.NextDouble() * .1f - .05f, .002f + _discard.Count * .0006f, (float)random.NextDouble() * .08f - .04f);
                view.Land(_world, at, Quaternion.Euler(-90f, (float)random.NextDouble() * 80f - 40f, 0f), Table3DLayout.TableCardScale);
                _discard.Add(view);
                if (_discard.Count <= DiscardShown) continue;
                Destroy(_discard[0].gameObject);
                _discard.RemoveAt(0);
            }
        }

        private Card3DView Find(Card card, Vector3 from)
        {
            if (_cards.TryGetValue(card, out var view)) return view;
            view = Card3DView.Create(_world, card, true, true);
            view.Place(_world, _world.InverseTransformPoint(from), Quaternion.Euler(-90f, 0f, 0f), Table3DLayout.TableCardScale);
            _cards[card] = view;
            return view;
        }

        private int CompareInHand(Card left, Card right)
        {
            int Group(Card card) => card.IsJoker ? 2 : card.Suit == _game.Trump ? 1 : 0;
            var byGroup = Group(left).CompareTo(Group(right));
            if (byGroup != 0) return byGroup;
            var byRank = left.Rank.CompareTo(right.Rank);
            return byRank != 0 ? byRank : left.Suit.CompareTo(right.Suit);
        }

        private void PaintHud()
        {
            var actor = DurakTurnOrder.GetActingPlayer(_game);
            var yours = actor == HumanIndex && !_game.IsFinished;
            string status;
            if (_game.IsFinished) status = !_game.LoserIndex.HasValue ? "НИЧЬЯ" : _game.LoserIndex == HumanIndex ? "РЕВАНШ?" : "ПОБЕДА!";
            else if (yours) status = _game.DefenderIndex == HumanIndex ? "ВАША ЗАЩИТА" : "ВАШ ХОД";
            else status = actor.HasValue ? "ХОДИТ " + _game.Players[actor.Value].Name.ToUpperInvariant() : "ЗАВЕРШАЕМ РОЗЫГРЫШ";
            _hud.SetStatus(status, yours);
            var legal = yours ? _game.GetLegalMoves(HumanIndex) : null;
            _hud.SetActions(legal != null && legal.Any(m => m.Type == DurakMoveType.Take), legal != null && legal.Any(m => m.Type == DurakMoveType.Pass),
                _game.Table.All(slot => slot.IsDefended) ? "БИТО" : "ПАС", HumanMoves(_selected).Any(m => m.Type == DurakMoveType.Transfer), _game.IsFinished);
            for (var i = 0; i < _seats.Length; i++)
            {
                var player = _game.Players[i + 1];
                _hud.SetSeat(i, player.Name, RoleOf(player), Fighting(player), actor == player.Index);
            }
        }

        private int Fighting(DurakPlayer player)
        {
            if (player.IsOut || _game.IsFinished) return 0;
            return player.Index == _game.DefenderIndex ? 2 : player.Index == _game.AttackerIndex ? 1 : 0;
        }

        private string RoleOf(DurakPlayer player)
        {
            if (player.IsOut) return "Вышел";
            if (_game.IsFinished) return _game.LoserIndex == player.Index ? "Остался с картами" : "";
            if (player.Index == _game.DefenderIndex) return "Защищается";
            if (player.Index == _game.AttackerIndex) return "Атакует";
            return DurakTurnOrder.GetActingPlayer(_game) == player.Index ? "Подкидывает" : "";
        }

        private void ReportIfFinished()
        {
            if (_reported || !_game.IsFinished) return;
            _reported = true;
            HubMatchReporter.ReportMatchCompleted("durak", _game.LoserIndex.HasValue && _game.LoserIndex.Value != HumanIndex, draw: !_game.LoserIndex.HasValue);
        }
    }
}
