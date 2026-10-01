using System;
using System.Collections.Generic;
using CardGame.Core.Cards;

namespace CardGame.Core.Games.Govno
{
    public enum GovnoPhase { Picking, Exchanging, ReactionCountdown, Reacting, RoundOver, Finished }
    public enum GovnoLossReason { LastHand, SecondTimeout, NoReaction }

    /// <summary>
    /// Local authoritative rules. All timestamps use one monotonic clock supplied by the host.
    /// Exchange completion is explicit: the next choice window begins after cards are shown.
    /// Full hands belong to the host; an online client will need a private player projection.
    /// </summary>
    public sealed class GovnoGame
    {
        public const double PickSeconds = 2.5;
        public const double ChaosPickSeconds = 1.5;
        public const double FalseStartSeconds = 1;
        public const double ReactionSeconds = 4;
        private readonly Random _random;
        private readonly List<GovnoPlayer> _players = new List<GovnoPlayer>();
        private readonly List<int> _active = new List<int>();
        private readonly List<int> _reactionPlayers = new List<int>();
        private readonly List<int> _handOrder = new List<int>();
        private readonly List<int> _winners = new List<int>();
        private int _reactionRetries;

        public GovnoGame(IReadOnlyList<string> names, int seed, bool chaos = false)
        {
            if (names == null) throw new ArgumentNullException(nameof(names));
            if (names.Count < 3 || names.Count > 6) throw new ArgumentException("Нужно от 3 до 6 игроков.", nameof(names));
            _random = new Random(seed);
            Chaos = chaos;
            for (var i = 0; i < names.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(names[i])) throw new ArgumentException("Имя игрока не должно быть пустым.", nameof(names));
                _players.Add(new GovnoPlayer(i, names[i]));
                _active.Add(i);
            }
            Players = _players.AsReadOnly();
            ActivePlayers = _active.AsReadOnly();
            ReactionPlayers = _reactionPlayers.AsReadOnly();
            HandOrder = _handOrder.AsReadOnly();
            Winners = _winners.AsReadOnly();
            DealRound();
        }

        public IReadOnlyList<GovnoPlayer> Players { get; }
        public IReadOnlyList<int> ActivePlayers { get; }
        public IReadOnlyList<int> ReactionPlayers { get; }
        public IReadOnlyList<int> HandOrder { get; }
        public IReadOnlyList<int> Winners { get; }
        public bool Chaos { get; }
        public double PickDurationSeconds => Chaos ? ChaosPickSeconds : PickSeconds;
        public GovnoPhase Phase { get; private set; }
        public double Now { get; private set; }
        public double Deadline { get; private set; }
        public double PhaseStartedAt { get; private set; }
        public int Revision { get; private set; }
        public int RoundNumber { get; private set; }
        public int ExchangeNumber { get; private set; }
        public int ReactionNumber { get; private set; }
        public int Caller { get; private set; } = -1;
        public int LastLoser { get; private set; } = -1;
        public GovnoLossReason LossReason { get; private set; }
        public bool IsPenaltyReaction { get; private set; }
        public bool IsFinished => Phase == GovnoPhase.Finished;

        // Seats run from the human toward the visible left side of the table.
        public int LeftOf(int player)
        {
            var index = _active.IndexOf(player);
            return index < 0 ? -1 : _active[(index + 1) % _active.Count];
        }

        public int RightOf(int player)
        {
            var index = _active.IndexOf(player);
            return index < 0 ? -1 : _active[(index + _active.Count - 1) % _active.Count];
        }

        public bool HasIncoming(int player)
        {
            var right = RightOf(player);
            return right >= 0 && _players[right].HasPassed;
        }

        public void AdvanceTo(double now)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < Now)
                throw new ArgumentOutOfRangeException(nameof(now), "Время должно идти вперёд.");
            Now = now;
            if (now < Deadline) return;
            if (Phase == GovnoPhase.Picking) ExpirePicking();
            else if (Phase == GovnoPhase.ReactionCountdown) BeginReaction();
            else if (Phase == GovnoPhase.Reacting) ExpireReaction();
        }

        public bool TryPass(int player, int slot)
        {
            if (Phase != GovnoPhase.Picking || !IsActive(player) || _players[player].HasPassed
                || slot < 0 || slot >= 4 || !_players[player].Slots[slot].HasValue) return false;
            _players[player].Pass(slot);
            Revision++;
            if (AllPassed()) ChangePhase(GovnoPhase.Exchanging);
            return true;
        }

        public bool CompleteExchange()
        {
            if (Phase != GovnoPhase.Exchanging) return false;
            var incoming = new Card[_active.Count];
            for (var i = 0; i < _active.Count; i++)
                incoming[i] = _players[RightOf(_active[i])].Outgoing!.Value;
            for (var i = 0; i < _active.Count; i++)
                _players[_active[i]].Receive(incoming[i], _random, Chaos);
            ExchangeNumber++;
            ChangePhase(GovnoPhase.Picking, PickDurationSeconds);
            return true;
        }

        public bool TryCall(int player)
        {
            if (Phase != GovnoPhase.Picking || !IsActive(player) || Now < _players[player].BlockedUntil) return false;
            if (!_players[player].HasFourOfAKind)
            {
                FalseStart(player);
                return false;
            }
            Caller = player;
            IsPenaltyReaction = false;
            _reactionPlayers.Clear();
            _reactionPlayers.AddRange(_active);
            _handOrder.Clear();
            _handOrder.Add(player);
            _reactionRetries = 0;
            BeginReaction();
            return true;
        }

        public bool TrySlap(int player)
        {
            if (Phase != GovnoPhase.Reacting || !CanReact(player)) return false;
            _handOrder.Add(player);
            Revision++;
            var pending = PendingReaction();
            if (pending.Count == 0)
                LoseLife(player, IsPenaltyReaction ? GovnoLossReason.SecondTimeout : GovnoLossReason.LastHand);
            return true;
        }

        public bool CanReact(int player) => IsActive(player) && _reactionPlayers.Contains(player)
            && !_handOrder.Contains(player) && Now >= _players[player].BlockedUntil;

        public void FalseStart(int player)
        {
            if (!IsActive(player) || (Phase != GovnoPhase.Picking && Phase != GovnoPhase.Exchanging
                && Phase != GovnoPhase.ReactionCountdown)
                || _handOrder.Contains(player)) return;
            _players[player].BlockedUntil = Math.Max(_players[player].BlockedUntil, Now + FalseStartSeconds);
            Revision++;
        }

        public bool StartNextRound()
        {
            if (Phase != GovnoPhase.RoundOver) return false;
            DealRound();
            return true;
        }

        private void DealRound()
        {
            RoundNumber++;
            ExchangeNumber++;
            Caller = -1;
            IsPenaltyReaction = false;
            _handOrder.Clear();
            _reactionPlayers.Clear();
            foreach (var player in _players)
            {
                player.ClearCards();
                player.Warnings = 0;
                player.BlockedUntil = 0;
            }
            // Rebuild all cards after elimination, dropping the lowest no-longer-needed rank.
            var deck = Deck.Standard((Rank)((int)Rank.Ace - _active.Count + 1), _random.Next());
            for (var slot = 0; slot < 4; slot++)
                foreach (var id in _active) _players[id].Put(slot, deck.Draw());
            ChangePhase(GovnoPhase.Picking, PickDurationSeconds);
        }

        private void ExpirePicking()
        {
            var offenders = new List<int>();
            foreach (var id in _active)
            {
                var player = _players[id];
                if (player.HasPassed) continue;
                player.Warnings++;
                if (player.Warnings >= 2) offenders.Add(id);
            }
            Revision++;
            if (offenders.Count == 1) { LoseLife(offenders[0], GovnoLossReason.SecondTimeout); return; }
            if (offenders.Count > 1)
            {
                IsPenaltyReaction = true;
                Caller = -1;
                _handOrder.Clear();
                _reactionPlayers.Clear();
                _reactionPlayers.AddRange(offenders);
                _reactionRetries = 0;
                ChangePhase(GovnoPhase.ReactionCountdown, 0.8);
                return;
            }
            foreach (var id in _active)
                if (!_players[id].HasPassed) _players[id].Pass(_random.Next(4));
            ChangePhase(GovnoPhase.Exchanging);
        }

        private bool AllPassed()
        {
            foreach (var id in _active) if (!_players[id].HasPassed) return false;
            return true;
        }

        private void BeginReaction()
        {
            ReactionNumber++;
            ChangePhase(GovnoPhase.Reacting, ReactionSeconds);
        }

        private List<int> PendingReaction()
        {
            var pending = new List<int>();
            foreach (var id in _reactionPlayers) if (!_handOrder.Contains(id)) pending.Add(id);
            return pending;
        }

        private void ExpireReaction()
        {
            var pending = PendingReaction();
            if (pending.Count == 1) { LoseLife(pending[0], GovnoLossReason.NoReaction); return; }
            if (++_reactionRetries >= 2)
            {
                // Multiple absent players must not kill the whole table or stall the match.
                LoseLife(pending[_random.Next(pending.Count)], GovnoLossReason.NoReaction);
                return;
            }
            _reactionPlayers.Clear();
            _reactionPlayers.AddRange(pending);
            ChangePhase(GovnoPhase.ReactionCountdown, 0.8);
        }

        private void LoseLife(int player, GovnoLossReason reason)
        {
            LastLoser = player;
            LossReason = reason;
            _players[player].Lives--;
            foreach (var participant in _players) participant.Warnings = 0;
            if (!_players[player].IsAlive) _active.Remove(player);
            if (_active.Count == 2)
            {
                _winners.AddRange(_active);
                ChangePhase(GovnoPhase.Finished);
            }
            else ChangePhase(GovnoPhase.RoundOver);
        }

        private bool IsActive(int player) => player >= 0 && player < _players.Count && _players[player].IsAlive;
        private void ChangePhase(GovnoPhase phase, double duration = double.PositiveInfinity)
        {
            Phase = phase;
            PhaseStartedAt = Now;
            Deadline = Now + duration;
            Revision++;
        }
    }
}
