using System;
using System.Collections.Generic;

namespace CardGame.Core.Games.Poker
{
    /// <summary>A main or side pot. Folded contributions count towards Amount, never eligibility.</summary>
    public sealed class PokerPot
    {
        internal PokerPot(int amount, int[] eligiblePlayers, int[] winners)
        {
            Amount = amount;
            EligiblePlayers = Array.AsReadOnly(eligiblePlayers);
            Winners = Array.AsReadOnly(winners);
        }

        public int Amount { get; }
        public IReadOnlyList<int> EligiblePlayers { get; }
        public IReadOnlyList<int> Winners { get; }
    }
}
