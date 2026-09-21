using System.Collections.Generic;
using CardGame.Core.Cards;

namespace CardGame.Core.Games.Bluff
{
    public sealed class BluffPlayer
    {
        private readonly List<Card> _hand = new List<Card>();

        public BluffPlayer(int index, string name)
        {
            Index = index;
            Name = name;
        }

        public int Index { get; }
        public string Name { get; }

        /// <summary>
        /// Рука. В модели она открыта всем — прятать её обязан клиент, ровно как в дураке.
        /// Ботам читать чужую руку нельзя, иначе блеф теряет смысл.
        /// </summary>
        public IReadOnlyList<Card> Hand => _hand;

        /// <summary>Карт не осталось. Не окончательно: поймав вруна, можно вернуться с котлом.</summary>
        public bool IsOut => _hand.Count == 0;

        /// <summary>Сколько тузов на руках. По этому числу в конце и определяется проигравший.</summary>
        public int AceCount
        {
            get
            {
                var count = 0;
                foreach (var card in _hand)
                {
                    if (card.Rank == Rank.Ace)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>Рука состоит из одних тузов (или пуста) — честно ходить уже нечем.</summary>
        public bool HoldsOnlyAces
        {
            get
            {
                foreach (var card in _hand)
                {
                    if (card.Rank != Rank.Ace)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        public int CountOf(Rank rank)
        {
            var count = 0;
            foreach (var card in _hand)
            {
                if (card.Rank == rank)
                {
                    count++;
                }
            }

            return count;
        }

        internal void AddRange(IEnumerable<Card> cards) => _hand.AddRange(cards);

        internal bool Remove(Card card) => _hand.Remove(card);

        internal void Deal(Card card) => _hand.Add(card);
    }
}
