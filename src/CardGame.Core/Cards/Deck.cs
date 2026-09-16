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

        public static Deck Standard36(int seed) => Standard(Rank.Six, seed);

        public static Deck Standard52(int seed) => Standard(Rank.Two, seed);

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
