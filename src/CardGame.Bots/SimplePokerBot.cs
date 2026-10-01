using System;
using System.Collections.Generic;
using CardGame.Core.Cards;
using CardGame.Core.Games.Poker;

namespace CardGame.Bots
{
    public readonly struct PokerDecision
    {
        public PokerDecision(PokerAction action, int raiseTo = 0)
        {
            Action = action;
            RaiseTo = raiseTo;
        }
        public PokerAction Action { get; }
        /// <summary>The desired TOTAL street contribution, not the amount added.</summary>
        public int RaiseTo { get; }
    }

    /// <summary>
    /// Casual poker opponent. Reads only its own two cards and public table information;
    /// hand strength is a heuristic, not privileged knowledge of opponents or the deck.
    /// </summary>
    public sealed class SimplePokerBot
    {
        private readonly Random _random;
        private readonly double _aggression;
        private readonly double _looseness;

        public SimplePokerBot(int seed)
        {
            _random = new Random(seed);
            _aggression = 0.18 + _random.NextDouble() * 0.25;
            _looseness = (_random.NextDouble() - 0.5) * 0.12;
        }

        public PokerDecision Choose(PokerGame game, int player)
        {
            if (game == null) throw new ArgumentNullException(nameof(game));
            if (player < 0 || player >= game.Players.Count) throw new ArgumentOutOfRangeException(nameof(player));
            if (game.CurrentPlayer != player) throw new InvalidOperationException("The bot can act only on its own turn.");

            var own = game.Players[player];
            var call = Math.Min(own.Stack, game.ToCall(player));
            var opponents = 0;
            foreach (var other in game.Players)
                if (other.Index != player && other.InHand && !other.Folded) opponents++;

            var strength = EstimateStrength(own.HoleCards, game.Board);
            // Multiway pots require more strength; the small personal variation is fixed for this bot.
            var confidence = strength - Math.Max(0, opponents - 1) * 0.025 + _looseness;
            var potOdds = call / (double)Math.Max(1, game.Pot + call);
            var pressure = call / (double)Math.Max(1, own.Stack);
            var roll = _random.NextDouble();

            if (call > 0)
            {
                // Weak hands sometimes defend a cheap blind, but fold to substantial pressure.
                var threshold = 0.24 + potOdds * 0.40 + pressure * 0.17;
                if (confidence < threshold && roll > 0.06)
                    return new PokerDecision(PokerAction.Fold);
            }

            var bluff = confidence < 0.4 && call == 0 && opponents <= 2 && roll < 0.035;
            var raiseChance = _aggression + Math.Max(0, confidence - 0.65) * 0.55;
            var wantsRaise = bluff || (confidence > (call > 0 ? 0.69 : 0.51) && roll < raiseChance);
            if (wantsRaise && game.CanRaise(player))
            {
                var maximum = own.RoundBet + own.Stack;
                // Bet roughly half to three quarters of the pot, rounded to big-blind chips.
                var fraction = 0.45 + _random.NextDouble() * 0.30;
                var increase = Math.Max(game.BigBlind, (int)Math.Round((game.Pot + call) * fraction));
                increase = Math.Max(game.BigBlind, (increase / game.BigBlind) * game.BigBlind);
                var target = Math.Min(maximum, Math.Max(game.MinimumRaiseTo, game.CurrentBet + increase));
                if (target > game.CurrentBet)
                {
                    if (target == maximum) return new PokerDecision(PokerAction.AllIn);
                    if (target >= game.MinimumRaiseTo) return new PokerDecision(PokerAction.Raise, target);
                }
            }
            return new PokerDecision(call > 0 ? PokerAction.Call : PokerAction.Check);
        }

        private static double EstimateStrength(IReadOnlyList<Card> hole, IReadOnlyList<Card> board)
        {
            if (hole.Count != 2) throw new ArgumentException("Hold'em requires two hole cards.", nameof(hole));
            var high = Math.Max((int)hole[0].Rank, (int)hole[1].Rank);
            var low = Math.Min((int)hole[0].Rank, (int)hole[1].Rank);
            if (board.Count == 0)
            {
                if (high == low) return 0.56 + (high - 2) * 0.033;
                var gap = high - low;
                var strength = 0.16 + (high - 2) * 0.022 + (low - 2) * 0.012;
                if (hole[0].Suit == hole[1].Suit) strength += 0.045;
                if (gap == 1) strength += 0.05;
                else if (gap == 2) strength += 0.02;
                if (high == 14 && low >= 10) strength += 0.10;
                return strength;
            }

            var visible = new List<Card>(hole.Count + board.Count);
            visible.AddRange(hole);
            visible.AddRange(board);
            var value = PokerHandEvaluator.Evaluate(visible);
            var score = value.Category switch
            {
                PokerHandCategory.OnePair => 0.47,
                PokerHandCategory.TwoPair => 0.62,
                PokerHandCategory.ThreeOfAKind => 0.74,
                PokerHandCategory.Straight => 0.82,
                PokerHandCategory.Flush => 0.88,
                PokerHandCategory.FullHouse => 0.94,
                PokerHandCategory.FourOfAKind => 0.985,
                PokerHandCategory.StraightFlush => 0.999,
                _ => 0.18 + (high - 2) * 0.008
            };

            // Distinguish a paired board from our own pair without consulting another player's hand.
            if (value.Category == PokerHandCategory.OnePair)
            {
                var ourPair = hole[0].Rank == hole[1].Rank;
                foreach (var card in board) ourPair |= card.Rank == hole[0].Rank || card.Rank == hole[1].Rank;
                if (!ourPair) score -= 0.18;
                else score += ((int)value.BestFive[0].Rank - 8) * 0.012;
            }
            if (board.Count == 5)
            {
                var boardValue = PokerHandEvaluator.Evaluate(board);
                if (boardValue.IsRoyalFlush) return 0.999; // Everyone shares the unbeatable board.
                if (value == boardValue) score = Math.Min(score, 0.38);
            }
            else
            {
                // A visible four-flush or open-ended straight adds modest drawing value.
                var suits = new int[4];
                var ranks = new bool[15];
                foreach (var card in visible)
                {
                    suits[(int)card.Suit]++;
                    ranks[(int)card.Rank] = true;
                    if (card.Rank == Rank.Ace) ranks[1] = true;
                }
                foreach (var count in suits) if (count == 4) { score += 0.08; break; }
                for (var bottom = 2; bottom <= 10; bottom++)
                {
                    if (ranks[bottom] && ranks[bottom + 1] && ranks[bottom + 2] && ranks[bottom + 3])
                    { score += 0.045; break; }
                }
            }
            return Math.Min(0.999, score);
        }
    }
}
