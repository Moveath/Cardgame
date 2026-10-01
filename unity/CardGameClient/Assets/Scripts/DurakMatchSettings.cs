using CardGame.Core.Games.Durak;

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
        /// <summary>
        /// Больше шести комната пока не предлагает. Семь и восемь убраны только отсюда:
        /// правила, боты и стол их по-прежнему тянут. Чтобы вернуть — поставить 8.
        /// </summary>
        public const int MaxPlayers = 6;

        public int PlayerCount;
        public DeckKind DeckSize;

        // Необязательные варианты правил — по умолчанию классика без них.
        public bool AllowTransfer;
        public ThrowInMode ThrowInMode;
        public bool AllowCheating;

        public static DurakMatchSettings Default => new DurakMatchSettings
        {
            PlayerCount = 3,
            DeckSize = DeckKind.ThirtySix,
            AllowTransfer = false,
            ThrowInMode = ThrowInMode.All,
            AllowCheating = false
        };

        /// <summary>
        /// Сколько игроков умещается в выбранной колоде без пересдачи по кругу — но не
        /// больше, чем предлагает комната (<see cref="MaxPlayers"/>).
        /// </summary>
        public static int MaxPlayersFor(DeckKind deck)
        {
            int byDeck;
            switch (deck)
            {
                case DeckKind.Twenty4: byDeck = 4; break;
                case DeckKind.ThirtySix: byDeck = 6; break;
                default: byDeck = 8; break;
            }

            return byDeck > MaxPlayers ? MaxPlayers : byDeck;
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

        public DurakOptions BuildOptions() => new DurakOptions(AllowTransfer, ThrowInMode, AllowCheating);
    }
}
