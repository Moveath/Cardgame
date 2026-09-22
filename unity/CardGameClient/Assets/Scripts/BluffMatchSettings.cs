using CardGame.Core.Cards;
using CardGame.Core.Games.Bluff;

namespace CardGameClient
{
    /// <summary>Настройки одной партии в «верю — не верю», собранные в своей комнате.</summary>
    public struct BluffMatchSettings
    {
        public int PlayerCount;
        public DeckKind DeckSize;

        /// <summary>Разрешено ли сбрасывать каре вне очереди.</summary>
        public bool AllowFourOfAKind;

        /// <summary>
        /// Бывают ли за столом перестрелки. Домашнее правило: изредка ведущему круга
        /// предлагают её объявить, и тогда круг идёт на нервах — все валят мусор, котёл
        /// растёт, а тот, кто не выдержит и проверит, получает петуха до конца партии.
        /// </summary>
        public bool Shootout;

        public static BluffMatchSettings Default => new BluffMatchSettings
        {
            PlayerCount = 4,
            DeckSize = DeckKind.ThirtySix,
            AllowFourOfAKind = true,
            Shootout = true
        };

        /// <summary>
        /// Колоды с джокерами тут нет намеренно. Заявить джокера нельзя — как и туза, —
        /// значит он превращается во второго «мёртвого» гостя на руках, но в условие конца
        /// партии («у всех только тузы») не входит, и партия зависает.
        /// </summary>
        public static DeckKind[] DeckOptions => new[] { DeckKind.Twenty4, DeckKind.ThirtySix, DeckKind.FiftyTwo };

        /// <summary>
        /// Раздаётся вся колода, поэтому ограничение другое, чем в дураке: важно не то,
        /// хватит ли карт на раздачу, а то, останется ли чем играть. Меньше четырёх карт
        /// на руках — и партия кончается, не начавшись.
        /// </summary>
        public static int MaxPlayersFor(DeckKind deck)
        {
            var byCards = CardCount(deck) / 4;
            return byCards > 8 ? 8 : byCards;
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

        public BluffOptions BuildOptions() => new BluffOptions(3, AllowFourOfAKind, false, Shootout);
    }
}
