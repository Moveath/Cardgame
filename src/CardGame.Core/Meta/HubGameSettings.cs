using System;

namespace CardGame.Core.Meta
{
    /// <summary>Serializable game rules shared by room creation, lobby and match launch.</summary>
    [Serializable]
    public sealed class HubGameSettings
    {
        public int DeckSize;
        public bool AllowTransfer;
        public bool NeighboursOnly;
        public bool AllowCheating;
        public bool Shootout;
        public bool Chaos;

        public static HubGameSettings DefaultFor(string game)
        {
            switch (game)
            {
                case "durak": case "chests": return new HubGameSettings { DeckSize = 36 };
                case "bluff": return new HubGameSettings { DeckSize = 36, Shootout = true };
                case "govno": return new HubGameSettings();
                case "poker": return new HubGameSettings { DeckSize = 52 };
                default: throw new ArgumentException("Unknown game.", nameof(game));
            }
        }

        public HubGameSettings Copy() => new HubGameSettings
        {
            DeckSize = DeckSize, AllowTransfer = AllowTransfer, NeighboursOnly = NeighboursOnly,
            AllowCheating = AllowCheating, Shootout = Shootout, Chaos = Chaos
        };

        internal bool HasSameRules(HubGameSettings other) => DeckSize == other.DeckSize
            && AllowTransfer == other.AllowTransfer && NeighboursOnly == other.NeighboursOnly
            && AllowCheating == other.AllowCheating && Shootout == other.Shootout && Chaos == other.Chaos;
    }

    public static class HubGameRules
    {
        public static int MinimumPlayers(string game) => game == "chests" || game == "govno" ? 3 : 2;
        public static int MaximumPlayers(string game, HubGameSettings? settings) =>
            game == "durak" && settings != null && settings.DeckSize == 24 ? 4 : 6;

        public static int[] DeckSizes(string game)
        {
            switch (game)
            {
                case "durak": return new[] { 24, 36, 52, 54 };
                case "bluff": return new[] { 24, 36, 52 };
                case "chests": return new[] { 36, 52 };
                case "govno": return new[] { 0 };
                case "poker": return new[] { 52 };
                default: return Array.Empty<int>();
            }
        }
    }
}
