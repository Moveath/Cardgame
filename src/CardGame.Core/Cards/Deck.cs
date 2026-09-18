using System;
using System.Collections.Generic;

namespace CardGame.Core.Cards
{
    public sealed class Deck
    {
        private readonly List<Card> _cards;

        public Deck(IEnumerable<Card> cards)
        {
            _cards = new List<Card>(cards);
        }

        public int Count => _cards.Count;

        public bool IsEmpty => _cards.Count == 0;

        /// <summary>Нижняя карта колоды — в дураке она же козырная и видна всем.</summary>
        public Card? Bottom => _cards.Count == 0 ? (Card?)null : _cards[_cards.Count - 1];

        public static Deck Standard(Rank lowestRank, int seed)
        {
            var cards = new List<Card>();
            for (var rank = lowestRank; rank <= Rank.Ace; rank++)
            {
                for (var suit = Suit.Clubs; suit <= Suit.Spades; suit++)
                {
                    cards.Add(new Card(rank, suit));
                }
            }

            var deck = new Deck(cards);
            deck.Shuffle(seed);
            return deck;
        }

        public static Deck Standard24(int seed) => Standard(Rank.Nine, seed);

        public static Deck Standard36(int seed) => Standard(Rank.Six, seed);

        public static Deck Standard52(int seed) => Standard(Rank.Two, seed);

        /// <summary>52 карты плюс два джокера — джокеры перемешиваются вместе со всеми, а не лежат сверху.</summary>
        public static Deck Standard54(int seed)
        {
            var cards = new List<Card>();
            for (var rank = Rank.Two; rank <= Rank.Ace; rank++)
            {
                for (var suit = Suit.Clubs; suit <= Suit.Spades; suit++)
                {
                    cards.Add(new Card(rank, suit));
                }
            }

            cards.Add(new Card(Rank.Joker, Suit.Clubs));
            cards.Add(new Card(Rank.Joker, Suit.Hearts));

            var deck = new Deck(cards);
            deck.Shuffle(seed);
            return deck;
        }

        public void Shuffle(int seed)
        {
            var random = new Random(seed);
            for (var i = _cards.Count - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                var temp = _cards[i];
                _cards[i] = _cards[j];
                _cards[j] = temp;
            }
        }

        public Card Draw()
        {
            if (_cards.Count == 0)
            {
                throw new InvalidOperationException("Колода пуста.");
            }

            var card = _cards[0];
            _cards.RemoveAt(0);
            return card;
        }
    }
}
