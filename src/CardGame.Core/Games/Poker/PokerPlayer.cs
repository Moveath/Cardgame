using System.Collections.Generic;
using CardGame.Core.Cards;

namespace CardGame.Core.Games.Poker
{
    public sealed class PokerPlayer
    {
        private readonly List<Card> _holeCards = new List<Card>(2);

        internal PokerPlayer(int index, string name, int stack)
        {
            Index = index;
            Name = name;
            Stack = stack;
            HoleCards = _holeCards.AsReadOnly();
        }

        public int Index { get; }
        public string Name { get; }
        public int Stack { get; internal set; }
        public int RoundBet { get; internal set; }
        public int TotalBet { get; internal set; }
        public bool Folded { get; internal set; }
        public bool AllIn { get; internal set; }
        public bool InHand { get; internal set; }
        public IReadOnlyList<Card> HoleCards { get; }

        internal void ResetHand()
        {
            _holeCards.Clear();
            RoundBet = TotalBet = 0;
            Folded = AllIn = false;
            InHand = Stack > 0;
        }

        internal void Deal(Card card) => _holeCards.Add(card);
    }
}
