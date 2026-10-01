using System;
using System.Collections.Generic;
using System.Linq;
using CardGame.Core.Cards;

namespace CardGame.Core.Games.Poker
{
    public enum PokerPhase { Preflop, Flop, Turn, River, HandComplete, MatchComplete }
    public enum PokerAction { Fold, Check, Call, Raise, AllIn }

    /// <summary>No-limit Texas Hold'em; raise amounts are the player's total bet on this street.</summary>
    public sealed class PokerGame
    {
        private readonly List<PokerPlayer> _players;
        private readonly List<Card> _board = new List<Card>(5);
        private readonly List<PokerPot> _pots = new List<PokerPot>();
        private readonly int[] _winnings;
        private readonly bool[] _pending;
        private readonly bool[] _acted;
        private readonly int[] _actedAt;
        private readonly Random _random;
        private Deck _deck = null!;
        private int _lastFullRaise;
        // The physical SB seat is retained even when empty, so the next button can be dead.
        private int _smallBlindSeat;

        public PokerGame(IReadOnlyList<string> names, int seed, int startingStack = 2000,
            int smallBlind = 10, int bigBlind = 20)
        {
            if (names == null) throw new ArgumentNullException(nameof(names));
            if (names.Count < 2 || names.Count > 6) throw new ArgumentOutOfRangeException(nameof(names));
            if (startingStack <= 0 || startingStack > int.MaxValue / names.Count)
                throw new ArgumentOutOfRangeException(nameof(startingStack));
            if (smallBlind <= 0 || bigBlind < smallBlind)
                throw new ArgumentOutOfRangeException(nameof(smallBlind));
            _players = new List<PokerPlayer>(names.Count);
            for (var i = 0; i < names.Count; i++)
                _players.Add(new PokerPlayer(i, names[i] ?? "Игрок " + (i + 1), startingStack));
            Players = _players.AsReadOnly();
            Board = _board.AsReadOnly();
            Pots = _pots.AsReadOnly();
            _winnings = new int[names.Count];
            Winnings = Array.AsReadOnly(_winnings);
            _pending = new bool[names.Count];
            _acted = new bool[names.Count];
            _actedAt = new int[names.Count];
            _random = new Random(seed);
            SmallBlind = smallBlind;
            BigBlind = bigBlind;
            Dealer = 0;
            BeginHand();
        }

        public IReadOnlyList<PokerPlayer> Players { get; }
        public IReadOnlyList<Card> Board { get; }
        public IReadOnlyList<PokerPot> Pots { get; }
        public IReadOnlyList<int> Winnings { get; }
        public PokerPhase Phase { get; private set; }
        public int Dealer { get; private set; }
        public int SmallBlindPlayer { get; private set; }
        public int BigBlindPlayer { get; private set; }
        public int CurrentPlayer { get; private set; } = -1;
        public int CurrentBet { get; private set; }
        public int MinimumRaiseTo => (int)Math.Min(int.MaxValue, (long)CurrentBet + _lastFullRaise);
        public int Pot { get; private set; }
        public int HandNumber { get; private set; }
        public int Revision { get; private set; }
        public int SmallBlind { get; }
        public int BigBlind { get; }
        public string LastAction { get; private set; } = "";
        public int LastActor { get; private set; } = -1;

        public int ToCall(int player)
        {
            if (player < 0 || player >= _players.Count) return 0;
            return Math.Max(0, CurrentBet - _players[player].RoundBet);
        }

        public bool CanRaise(int player)
        {
            if (player < 0 || player >= _players.Count || Finished) return false;
            var p = _players[player];
            if (!CanAct(p) || p.Stack <= ToCall(player)) return false;
            // There is nobody to contest an additional bet against a table of all-in players.
            if (!_players.Any(other => other.Index != player && CanAct(other))) return false;
            // Several short all-ins cumulatively reopen action once a full raise is faced.
            return !_acted[player] || CurrentBet - _actedAt[player] >= _lastFullRaise;
        }

        public bool TryAct(int player, PokerAction action, int raiseTo = 0)
        {
            if (Finished || player != CurrentPlayer || player < 0 || player >= _players.Count) return false;
            var p = _players[player];
            if (!CanAct(p)) return false;
            var owed = ToCall(player);
            var maximum = p.RoundBet + p.Stack;
            var openingBet = CurrentBet == 0;
            var label = "";
            switch (action)
            {
                case PokerAction.Fold:
                    p.Folded = true;
                    label = "Пас";
                    break;
                case PokerAction.Check:
                    if (owed != 0) return false;
                    label = "Чек";
                    break;
                case PokerAction.Call:
                    if (owed == 0) return false;
                    var paid = Math.Min(owed, p.Stack);
                    Commit(p, paid);
                    label = p.AllIn ? "Олл-ин · " + p.RoundBet : "Колл · " + paid;
                    break;
                case PokerAction.Raise:
                    if (!TryRaise(p, raiseTo, maximum)) return false;
                    label = p.AllIn ? "Олл-ин · " + raiseTo : (openingBet ? "Ставка · " : "Рейз до ") + raiseTo;
                    break;
                case PokerAction.AllIn:
                    if (maximum > CurrentBet)
                    {
                        if (!TryRaise(p, maximum, maximum)) return false;
                    }
                    else Commit(p, p.Stack);
                    label = "Олл-ин · " + p.RoundBet;
                    break;
                default: return false;
            }
            _pending[player] = false;
            _acted[player] = true;
            _actedAt[player] = CurrentBet;
            LastActor = player;
            LastAction = p.Name + ": " + label;
            Revision++;
            ContinueAfter(player);
            return true;
        }

        public bool StartNextHand()
        {
            if (Phase != PokerPhase.HandComplete) return false;
            if (_players.Count(p => p.Stack > 0) < 2) return false;
            var previousBigBlind = BigBlindPlayer;
            var nextBigBlind = Next(previousBigBlind, p => p.Stack > 0);
            // The big blind always advances to the next survivor. In heads-up the other
            // player becomes button/SB, preventing a consecutive big blind at transition.
            if (_players.Count(p => p.Stack > 0) == 2)
            {
                Dealer = Next(nextBigBlind, p => p.Stack > 0);
                _smallBlindSeat = SmallBlindPlayer = Dealer;
            }
            else
            {
                // A busted SB leaves a dead button; a busted BB leaves no SB for one hand.
                // Advancing from surviving dealer positions would unfairly skip somebody's BB.
                Dealer = _smallBlindSeat;
                _smallBlindSeat = previousBigBlind;
                SmallBlindPlayer = _players[_smallBlindSeat].Stack > 0 ? _smallBlindSeat : -1;
            }
            BigBlindPlayer = nextBigBlind;
            BeginHand();
            return true;
        }

        private bool Finished => Phase == PokerPhase.HandComplete || Phase == PokerPhase.MatchComplete;
        private static bool CanAct(PokerPlayer p) => p.InHand && !p.Folded && !p.AllIn && p.Stack > 0;

        private void BeginHand()
        {
            if (HandNumber == 0)
            {
                SmallBlindPlayer = _players.Count == 2 ? Dealer : Next(Dealer, p => p.Stack > 0);
                _smallBlindSeat = SmallBlindPlayer;
                BigBlindPlayer = Next(SmallBlindPlayer, p => p.Stack > 0);
            }
            HandNumber++;
            Revision++;
            Phase = PokerPhase.Preflop;
            CurrentPlayer = -1;
            CurrentBet = Pot = 0;
            _lastFullRaise = BigBlind;
            _board.Clear();
            _pots.Clear();
            Array.Clear(_winnings, 0, _winnings.Length);
            Array.Clear(_acted, 0, _acted.Length);
            Array.Clear(_actedAt, 0, _actedAt.Length);
            foreach (var p in _players) p.ResetHand();
            _deck = Deck.Standard52(_random.Next());
            var alive = _players.Count(p => p.InHand);
            for (var card = 0; card < 2; card++)
            {
                var seat = Dealer;
                for (var i = 0; i < alive; i++)
                {
                    seat = Next(seat, p => p.InHand);
                    _players[seat].Deal(_deck.Draw());
                }
            }
            if (SmallBlindPlayer >= 0)
                Commit(_players[SmallBlindPlayer], Math.Min(SmallBlind, _players[SmallBlindPlayer].Stack));
            Commit(_players[BigBlindPlayer], Math.Min(BigBlind, _players[BigBlindPlayer].Stack));
            // A short blind does not reduce the amount other players must call preflop.
            CurrentBet = BigBlind;
            for (var i = 0; i < _players.Count; i++) _pending[i] = CanAct(_players[i]);
            LastActor = -1;
            LastAction = "Раздача " + HandNumber + " · блайнды " + SmallBlind + "/" + BigBlind;
            ContinueAfter(BigBlindPlayer);
        }

        private bool TryRaise(PokerPlayer p, int target, int maximum)
        {
            if (target <= CurrentBet || target > maximum || !CanRaise(p.Index)) return false;
            if (target < MinimumRaiseTo && target != maximum) return false;
            var increase = target - CurrentBet;
            if (increase >= _lastFullRaise) _lastFullRaise = increase;
            Commit(p, target - p.RoundBet);
            CurrentBet = target;
            foreach (var other in _players)
                if (other.Index != p.Index && CanAct(other) && other.RoundBet < CurrentBet)
                    _pending[other.Index] = true;
            return true;
        }

        private void Commit(PokerPlayer p, int amount)
        {
            p.Stack -= amount;
            p.RoundBet += amount;
            p.TotalBet += amount;
            Pot += amount;
            p.AllIn = p.Stack == 0;
        }

        private void ContinueAfter(int player)
        {
            if (_players.Count(p => p.InHand && !p.Folded) == 1)
            {
                RefundUncalled();
                Settle();
                return;
            }
            // A lone player with chips only acts when still owing a call to an all-in bet.
            var actors = _players.Where(CanAct).ToArray();
            if (actors.Length == 1)
            {
                // A nominal full big blind stops applying when the only opponent is a short
                // all-in blind. Never ask somebody to call money nobody actually wagered.
                CurrentBet = _players.Where(p => p.InHand && !p.Folded).Max(p => p.RoundBet);
                if (ToCall(actors[0].Index) == 0) _pending[actors[0].Index] = false;
            }
            var next = Next(player, p => CanAct(p) && _pending[p.Index]);
            if (next >= 0)
            {
                CurrentPlayer = next;
                return;
            }
            RefundUncalled();
            if (Phase == PokerPhase.River)
            {
                Settle();
                return;
            }
            if (_players.Count(CanAct) < 2)
            {
                while (_board.Count < 5) DealStreet();
                Settle();
                return;
            }
            DealStreet();
            foreach (var p in _players) p.RoundBet = 0;
            CurrentBet = 0;
            _lastFullRaise = BigBlind;
            Array.Clear(_acted, 0, _acted.Length);
            Array.Clear(_actedAt, 0, _actedAt.Length);
            for (var i = 0; i < _players.Count; i++) _pending[i] = CanAct(_players[i]);
            CurrentPlayer = Next(Dealer, CanAct);
        }

        private void DealStreet()
        {
            _deck.Draw(); // Burn before flop, turn, and river.
            if (_board.Count == 0)
            {
                for (var i = 0; i < 3; i++) _board.Add(_deck.Draw());
                Phase = PokerPhase.Flop;
            }
            else
            {
                _board.Add(_deck.Draw());
                Phase = _board.Count == 4 ? PokerPhase.Turn : PokerPhase.River;
            }
        }

        private void RefundUncalled()
        {
            var ordered = _players.Where(p => p.InHand).OrderByDescending(p => p.RoundBet).ToArray();
            if (ordered.Length < 2 || ordered[0].Folded) return;
            var excess = ordered[0].RoundBet - ordered[1].RoundBet;
            if (excess <= 0) return;
            var p = ordered[0];
            p.RoundBet -= excess;
            p.TotalBet -= excess;
            p.Stack += excess;
            Pot -= excess;
            p.AllIn = false;
        }

        private void Settle()
        {
            CurrentPlayer = -1;
            _pots.Clear();
            var contenders = _players.Where(p => p.InHand && !p.Folded).ToArray();
            var values = new Dictionary<int, PokerHandValue>();
            if (contenders.Length > 1)
                foreach (var p in contenders)
                    values[p.Index] = PokerHandEvaluator.Evaluate(p.HoleCards.Concat(_board).ToArray());
            var levels = _players.Select(p => p.TotalBet).Where(v => v > 0).Distinct().OrderBy(v => v).ToArray();
            var previous = 0;
            foreach (var level in levels)
            {
                var amount = (level - previous) * _players.Count(p => p.TotalBet >= level);
                previous = level;
                var eligible = contenders.Where(p => p.TotalBet >= level).Select(p => p.Index).ToArray();
                if (eligible.Length == 0) throw new InvalidOperationException("Банк без участников.");
                var winners = eligible;
                if (eligible.Length > 1)
                {
                    var best = values[eligible[0]];
                    foreach (var index in eligible)
                        if (values[index].CompareTo(best) > 0) best = values[index];
                    winners = eligible.Where(index => values[index].CompareTo(best) == 0).ToArray();
                }
                winners = winners.OrderBy(index => (index - Dealer - 1 + _players.Count) % _players.Count).ToArray();
                _pots.Add(new PokerPot(amount, eligible, winners));
                var share = amount / winners.Length;
                var odd = amount % winners.Length;
                for (var i = 0; i < winners.Length; i++)
                {
                    var payout = share + (i < odd ? 1 : 0);
                    _players[winners[i]].Stack += payout;
                    _winnings[winners[i]] += payout;
                }
            }
            Pot = 0;
            CurrentBet = 0;
            Phase = _players.Count(p => p.Stack > 0) == 1 ? PokerPhase.MatchComplete : PokerPhase.HandComplete;
        }

        private int Next(int start, Func<PokerPlayer, bool> predicate)
        {
            for (var offset = 1; offset <= _players.Count; offset++)
            {
                var index = (start + offset + _players.Count) % _players.Count;
                if (predicate(_players[index])) return index;
            }
            return -1;
        }
    }
}
