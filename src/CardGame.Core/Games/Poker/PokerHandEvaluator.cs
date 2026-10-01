using System;
using System.Collections.Generic;
using CardGame.Core.Cards;

namespace CardGame.Core.Games.Poker
{
    /// <summary>Best five out of five, six or seven cards, including playing the entire board.</summary>
    public static class PokerHandEvaluator
    {
        public static PokerHandValue Evaluate(IReadOnlyList<Card> cards)
        {
            if (cards == null) throw new ArgumentNullException(nameof(cards));
            if (cards.Count < 5 || cards.Count > 7)
                throw new ArgumentException("A poker hand needs between five and seven cards.", nameof(cards));

            var seen = new HashSet<Card>();
            for (var i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                if ((int)card.Rank < 2 || (int)card.Rank > 14 || (int)card.Suit < 0 || (int)card.Suit > 3)
                    throw new ArgumentException("Hold'em uses a standard deck without jokers.", nameof(cards));
                if (!seen.Add(card)) throw new ArgumentException("A card occurs more than once.", nameof(cards));
            }

            var best = default(PokerHandValue);
            var hasBest = false;
            for (var a = 0; a < cards.Count - 4; a++)
            for (var b = a + 1; b < cards.Count - 3; b++)
            for (var c = b + 1; c < cards.Count - 2; c++)
            for (var d = c + 1; d < cards.Count - 1; d++)
            for (var e = d + 1; e < cards.Count; e++)
            {
                var candidate = EvaluateFive(new[] { cards[a], cards[b], cards[c], cards[d], cards[e] });
                if (!hasBest || candidate > best)
                {
                    best = candidate;
                    hasBest = true;
                }
            }
            return best;
        }

        private static PokerHandValue EvaluateFive(Card[] cards)
        {
            var counts = new int[15];
            var flush = true;
            foreach (var card in cards)
            {
                counts[(int)card.Rank]++;
                flush &= card.Suit == cards[0].Suit;
            }

            var groups = new List<int>(5);
            for (var rank = 14; rank >= 2; rank--) if (counts[rank] > 0) groups.Add(rank);
            var straightHigh = 0;
            if (groups.Count == 5)
            {
                if (groups[0] - groups[4] == 4) straightHigh = groups[0];
                else if (groups[0] == 14 && groups[1] == 5 && groups[4] == 2) straightHigh = 5;
            }

            PokerHandCategory category;
            int[] tiebreaks;
            if (straightHigh > 0)
            {
                category = flush ? PokerHandCategory.StraightFlush : PokerHandCategory.Straight;
                tiebreaks = new[] { straightHigh };
                // In the wheel the ace is the bottom card, not the highest kicker.
                Array.Sort(cards, (left, right) => StraightRank(right, straightHigh).CompareTo(StraightRank(left, straightHigh)));
            }
            else
            {
                // Multiplicity first, then rank: trip + pair, both pairs + kicker, etc.
                groups.Sort((left, right) => counts[right] != counts[left]
                    ? counts[right].CompareTo(counts[left]) : right.CompareTo(left));
                var firstCount = counts[groups[0]];
                var secondCount = groups.Count > 1 ? counts[groups[1]] : 0;
                category = firstCount == 4 ? PokerHandCategory.FourOfAKind
                    : firstCount == 3 && secondCount == 2 ? PokerHandCategory.FullHouse
                    : flush ? PokerHandCategory.Flush
                    : firstCount == 3 ? PokerHandCategory.ThreeOfAKind
                    : firstCount == 2 && secondCount == 2 ? PokerHandCategory.TwoPair
                    : firstCount == 2 ? PokerHandCategory.OnePair
                    : PokerHandCategory.HighCard;
                tiebreaks = groups.ToArray();
                Array.Sort(cards, (left, right) =>
                {
                    var leftIndex = groups.IndexOf((int)left.Rank);
                    var rightIndex = groups.IndexOf((int)right.Rank);
                    return leftIndex != rightIndex ? leftIndex.CompareTo(rightIndex) : left.Suit.CompareTo(right.Suit);
                });
            }
            return new PokerHandValue(category, tiebreaks, cards);
        }

        private static int StraightRank(Card card, int high) => high == 5 && card.Rank == Rank.Ace ? 1 : (int)card.Rank;
    }
}
