using System;
using CardGame.Core.Games.Govno;

namespace CardGame.Bots
{
    /// <summary>Chooses using only its own hand. Timing never reads the human's actions or cards.</summary>
    public sealed class SimpleGovnoBot
    {
        private readonly Random _random;
        private readonly double _reactionPace;
        public SimpleGovnoBot(int seed)
        {
            _random = new Random(seed);
            _reactionPace = 0.45 + _random.NextDouble() * 0.6;
        }

        public int ChooseSlot(GovnoPlayer player)
        {
            if (player.Hand.Count == 0) return -1;
            var bestRank = player.Hand[_random.Next(player.Hand.Count)].Rank;
            var bestCount = 0;
            foreach (var card in player.Hand)
            {
                var count = 0;
                foreach (var other in player.Hand) if (other.Rank == card.Rank) count++;
                if (count > bestCount || (count == bestCount && _random.Next(2) == 0))
                { bestCount = count; bestRank = card.Rank; }
            }
            var start = _random.Next(4);
            for (var i = 0; i < 4; i++)
            {
                var slot = (start + i) % 4;
                if (player.Slots[slot].HasValue && player.Slots[slot]!.Value.Rank != bestRank) return slot;
            }
            for (var i = 0; i < 4; i++) if (player.Slots[i].HasValue) return i;
            return -1;
        }

        public double PickDelay(double pickSeconds = GovnoGame.PickSeconds) => _random.NextDouble() < 0.07
            ? pickSeconds + 0.2
            // Доля окна та же, что была при трёх секундах: 1,1–2,6 из 3 — бот успевает в любом режиме.
            : (1.1 + _random.NextDouble() * 1.5) / 3.0 * pickSeconds;
        public double CallDelay() => 0.75 + _random.NextDouble() * 0.85;
        public double ReactionDelay() => Math.Max(0.22, _reactionPace + (_random.NextDouble() - 0.5) * 1.2);
    }
}
