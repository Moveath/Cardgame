using System.Collections.Generic;
using CardGame.Core.Cards;

namespace CardGame.Core.Games.Durak
{
    public sealed class DurakPlayer
    {
        private readonly List<Card> _hand = new List<Card>();

        public DurakPlayer(int index, string name)
        {
            Index = index;
            Name = name;
        }

        public int Index { get; }
        public string Name { get; }
        public IReadOnlyList<Card> Hand => _hand;
        public bool IsOut { get; internal set; }

        internal void Add(Card card) => _hand.Add(card);

        internal void AddRange(IEnumerable<Card> cards) => _hand.AddRange(cards);

        internal bool Remove(Card card) => _hand.Remove(card);
    }
}
