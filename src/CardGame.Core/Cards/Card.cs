using System;

namespace CardGame.Core.Cards
{
    public readonly struct Card : IEquatable<Card>
    {
        public Card(Rank rank, Suit suit)
        {
            Rank = rank;
            Suit = suit;
        }

        public Rank Rank { get; }
        public Suit Suit { get; }

        public bool Equals(Card other) => Rank == other.Rank && Suit == other.Suit;

        public override bool Equals(object? obj) => obj is Card other && Equals(other);

        public override int GetHashCode() => ((int)Rank * 4) + (int)Suit;

        public static bool operator ==(Card left, Card right) => left.Equals(right);

        public static bool operator !=(Card left, Card right) => !left.Equals(right);

        public override string ToString() => RankSymbol() + SuitSymbol();

        private string RankSymbol() => Rank switch
        {
            Rank.Ten => "10",
            Rank.Jack => "J",
            Rank.Queen => "Q",
            Rank.King => "K",
            Rank.Ace => "A",
            _ => ((int)Rank).ToString()
        };

        private string SuitSymbol() => Suit switch
        {
            Suit.Clubs => "♣",
            Suit.Diamonds => "♦",
            Suit.Hearts => "♥",
            _ => "♠"
        };
    }
}
