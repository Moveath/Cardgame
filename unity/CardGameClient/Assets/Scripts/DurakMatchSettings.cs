namespace CardGameClient
{
    public enum DeckKind
    {
        Twenty4,
        ThirtySix,
        FiftyTwo,
        FiftyFour
    }

    /// <summary>Настройки одной партии, собранные в лобби перед стартом игры.</summary>
    public struct DurakMatchSettings
    {
        public int PlayerCount;
        public DeckKind DeckSize;

        public static DurakMatchSettings Default => new DurakMatchSettings
        {
            PlayerCount = 3,
            DeckSize = DeckKind.ThirtySix
        };

        /// <summary>Сколько игроков умещается в выбранной колоде без пересдачи по кругу.</summary>
        public static int MaxPlayersFor(DeckKind deck)
        {
            switch (deck)
            {
                case DeckKind.Twenty4: return 4;
                case DeckKind.ThirtySix: return 6;
                default: return 8;
            }
        }

        public static int CardCount(DeckKind deck)
        {
            switch (deck)
            {
                case DeckKind.Twenty4: return 24;
                case DeckKind.ThirtySix: return 36;
                case DeckKind.FiftyTwo: return 52;
                default: return 54;
            }
        }

        public CardGame.Core.Cards.Deck BuildDeck(int seed)
        {
            switch (DeckSize)
            {
                case DeckKind.Twenty4: return CardGame.Core.Cards.Deck.Standard24(seed);
                case DeckKind.ThirtySix: return CardGame.Core.Cards.Deck.Standard36(seed);
                case DeckKind.FiftyTwo: return CardGame.Core.Cards.Deck.Standard52(seed);
                default: return CardGame.Core.Cards.Deck.Standard54(seed);
            }
        }
    }
}
