using System;
using System.Collections.Generic;
using CardGame.Core.Cards;

namespace CardGame.Core.Games.Govno
{
    public sealed class GovnoPlayer
    {
        private readonly Card?[] _slots = new Card?[4];
        private readonly List<Card> _hand = new List<Card>();
        internal GovnoPlayer(int index, string name)
        {
            Index = index;
            Name = name;
            Slots = Array.AsReadOnly(_slots);
            Hand = _hand.AsReadOnly();
        }

        public int Index { get; }
        public string Name { get; }
        public int Lives { get; internal set; } = 4;
        public int Warnings { get; internal set; }
        public bool IsAlive => Lives > 0;
        public bool HasPassed => Outgoing.HasValue;
        public double BlockedUntil { get; internal set; }
        public IReadOnlyList<Card?> Slots { get; }
        public IReadOnlyList<Card> Hand { get; }
        internal Card? Outgoing { get; private set; }

        public bool HasFourOfAKind => _hand.Count == 4 && _hand[0].Rank == _hand[1].Rank
            && _hand[0].Rank == _hand[2].Rank && _hand[0].Rank == _hand[3].Rank;

        internal void ClearCards()
        {
            Array.Clear(_slots, 0, _slots.Length);
            Outgoing = null;
            _hand.Clear();
        }

        internal void Put(int slot, Card card)
        {
            _slots[slot] = card;
            RefreshHand();
        }

        internal void Pass(int slot)
        {
            Outgoing = _slots[slot];
            _slots[slot] = null;
            RefreshHand();
        }

        internal void Receive(Card card, Random random, bool chaos)
        {
            for (var i = 0; i < 4; i++)
                if (!_slots[i].HasValue) { _slots[i] = card; break; }
            Outgoing = null;
            if (chaos)
                for (var i = 3; i > 0; i--)
                {
                    var j = random.Next(i + 1);
                    var temp = _slots[i]; _slots[i] = _slots[j]; _slots[j] = temp;
                }
            RefreshHand();
        }

        private void RefreshHand()
        {
            _hand.Clear();
            foreach (var card in _slots) if (card.HasValue) _hand.Add(card.Value);
        }
    }
}
