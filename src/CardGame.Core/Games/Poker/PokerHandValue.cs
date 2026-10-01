using System;
using System.Collections.Generic;
using CardGame.Core.Cards;

namespace CardGame.Core.Games.Poker
{
    /// <summary>A five-card poker value. Comparison considers every kicker, never the suit.</summary>
    public readonly struct PokerHandValue : IComparable<PokerHandValue>, IEquatable<PokerHandValue>
    {
        private readonly int _score;
        private readonly int _leadingRank;

        internal PokerHandValue(PokerHandCategory category, int[] ranks, Card[] bestFive)
        {
            Category = category;
            _leadingRank = ranks[0];
            var score = (int)category;
            for (var i = 0; i < 5; i++) score = (score * 15) + (i < ranks.Length ? ranks[i] : 0);
            _score = score;
            BestFive = Array.AsReadOnly(bestFive);
        }

        public PokerHandCategory Category { get; }
        public IReadOnlyList<Card> BestFive { get; }
        public bool IsRoyalFlush => Category == PokerHandCategory.StraightFlush && _leadingRank == 14;
        public string Name => IsRoyalFlush ? "Роял-флеш" : Category switch
        {
            PokerHandCategory.OnePair => "Пара",
            PokerHandCategory.TwoPair => "Две пары",
            PokerHandCategory.ThreeOfAKind => "Тройка",
            PokerHandCategory.Straight => "Стрит",
            PokerHandCategory.Flush => "Флеш",
            PokerHandCategory.FullHouse => "Фулл-хаус",
            PokerHandCategory.FourOfAKind => "Каре",
            PokerHandCategory.StraightFlush => "Стрит-флеш",
            _ => "Старшая карта"
        };

        public int CompareTo(PokerHandValue other) => _score.CompareTo(other._score);
        public bool Equals(PokerHandValue other) => _score == other._score;
        public override bool Equals(object? obj) => obj is PokerHandValue other && Equals(other);
        public override int GetHashCode() => _score;
        public override string ToString() => Name;
        public static bool operator >(PokerHandValue left, PokerHandValue right) => left.CompareTo(right) > 0;
        public static bool operator <(PokerHandValue left, PokerHandValue right) => left.CompareTo(right) < 0;
        public static bool operator >=(PokerHandValue left, PokerHandValue right) => left.CompareTo(right) >= 0;
        public static bool operator <=(PokerHandValue left, PokerHandValue right) => left.CompareTo(right) <= 0;
        public static bool operator ==(PokerHandValue left, PokerHandValue right) => left.Equals(right);
        public static bool operator !=(PokerHandValue left, PokerHandValue right) => !left.Equals(right);
    }
}
