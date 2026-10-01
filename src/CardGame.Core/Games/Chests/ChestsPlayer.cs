using System.Collections.Generic;
using CardGame.Core.Cards;

namespace CardGame.Core.Games.Chests
{
    public sealed class ChestsPlayer
    {
        private readonly List<Card> _hand = new List<Card>();
        private readonly List<Rank> _chests = new List<Rank>();

        public ChestsPlayer(int index, string name)
        {
            Index = index;
            Name = name;
        }

        public int Index { get; }
        public string Name { get; }

        /// <summary>
        /// Рука. В модели она открыта — прятать её обязан клиент, и прятать строже, чем в
        /// блефе: не только карты, но и их число. Под конец партии именно по числу видно,
        /// у кого пара, а у кого одна карта, и угадывать становится нечего. Открыто одно:
        /// пуста ли рука (<see cref="HasEmptyHand"/>). Боты в чужие руки не смотрят вообще.
        /// </summary>
        public IReadOnlyList<Card> Hand => _hand;

        /// <summary>
        /// Собранные сундучки в порядке сбора. Открытая информация: стопки лежат у всех на
        /// виду, и каждый знает, какие ранги уже вышли из рук.
        /// </summary>
        public IReadOnlyList<Rank> Chests => _chests;

        public int ChestCount => _chests.Count;

        /// <summary>
        /// Карт не осталось. Из партии это не выводит: игрок ходит в свою очередь и может
        /// снова получить карты удачным вопросом. Это видно всем — за столом он сидит без
        /// карт, — поэтому и бот может на это опираться.
        /// </summary>
        public bool HasEmptyHand => _hand.Count == 0;

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

        /// <summary>Все карты ранга на руке, по порядку мастей.</summary>
        public IReadOnlyList<Card> CardsOf(Rank rank)
        {
            var cards = new List<Card>();
            foreach (var card in _hand)
            {
                if (card.Rank == rank)
                {
                    cards.Add(card);
                }
            }

            cards.Sort((left, right) => left.Suit.CompareTo(right.Suit));
            return cards;
        }

        internal void Deal(Card card) => _hand.Add(card);

        internal void AddRange(IEnumerable<Card> cards) => _hand.AddRange(cards);

        internal bool Remove(Card card) => _hand.Remove(card);

        internal void AddChest(Rank rank) => _chests.Add(rank);
    }
}
