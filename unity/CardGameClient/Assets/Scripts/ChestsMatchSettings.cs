using CardGame.Core.Cards;

namespace CardGameClient
{
    /// <summary>Настройки одной партии в «Сундучки», собранные в своей комнате.</summary>
    public struct ChestsMatchSettings
    {
        /// <summary>
        /// Меньше трёх не бывает. Вдвоём всё, чего нет у тебя, лежит у соперника: первый
        /// ходящий знает его руку целиком и забирает всё подряд, не ошибившись ни разу.
        /// </summary>
        public const int MinPlayers = 3;

        /// <summary>
        /// Больше шести не бывает. Чем больше народу, тем меньше карт на руках: ввосьмером на
        /// 36 картах выходило по четыре, у каждого оказывалось по сундучку — и всё.
        /// </summary>
        public const int MaxPlayers = 6;

        /// <summary>Меньше шести карт на руки — и спрашивать почти нечего, партия кончается, не начавшись.</summary>
        private const int MinCardsPerPlayer = 6;

        public int PlayerCount;
        public DeckKind DeckSize;

        public static ChestsMatchSettings Default => new ChestsMatchSettings
        {
            PlayerCount = 4,
            DeckSize = DeckKind.ThirtySix
        };

        /// <summary>
        /// Только 36 и 52. На 24 картах шесть рангов на всех — сундучков слишком мало, партия
        /// кончается, толком не начавшись. Джокеров нет: из них сундучок не собрать.
        /// </summary>
        public static DeckKind[] DeckOptions => new[] { DeckKind.ThirtySix, DeckKind.FiftyTwo };

        /// <summary>Сколько игроков тянет колода: и на 36, и на 52 картах — до шести.</summary>
        public static int MaxPlayersFor(DeckKind deck)
        {
            var byCards = CardCount(deck) / MinCardsPerPlayer;
            return byCards > MaxPlayers ? MaxPlayers : byCards;
        }

        public static int CardCount(DeckKind deck)
        {
            switch (deck)
            {
                case DeckKind.Twenty4: return 24;
                case DeckKind.ThirtySix: return 36;
                default: return 52;
            }
        }

        /// <summary>Сколько карт придёт на руки каждому. При неделимом числе первым местам достаётся на одну больше.</summary>
        public int CardsPerPlayer => PlayerCount <= 0 ? 0 : CardCount(DeckSize) / PlayerCount;

        public Deck BuildDeck(int seed)
        {
            switch (DeckSize)
            {
                case DeckKind.Twenty4: return Deck.Standard24(seed);
                case DeckKind.ThirtySix: return Deck.Standard36(seed);
                default: return Deck.Standard52(seed);
            }
        }
    }
}
