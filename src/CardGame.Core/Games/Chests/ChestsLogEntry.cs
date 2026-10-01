using System.Collections.Generic;
using CardGame.Core.Cards;
using CardGame.Core.Games.Bluff;

namespace CardGame.Core.Games.Chests
{
    public enum ChestsLogKind
    {
        /// <summary>«Марат, есть вальты?» — и ответ «есть» или «нет».</summary>
        Ask,

        /// <summary>«Два вальта?» — «да» или «нет».</summary>
        CountGuess,

        /// <summary>«Пики и черви?» — «да» или «нет».</summary>
        SuitsGuess,

        /// <summary>Угадано всё: карты перешли от адресата к спрашивающему.</summary>
        Transfer,

        /// <summary>Каре ушло в сундучок — на раздаче или после передачи.</summary>
        Chest,

        GameOver
    }

    /// <summary>
    /// Строка общего лога. Сюда попадает ровно то, что слышат все за столом: кто кого
    /// спросил, что назвал и какой получил ответ. Правильного числа и правильных мастей
    /// после ошибки здесь нет — только «нет». Иначе запоминать ответы и нарочно называть
    /// не то было бы бессмысленно. Карты появляются только в передаче: их угадали, значит
    /// их и так все услышали.
    /// </summary>
    public sealed class ChestsLogEntry
    {
        private static readonly Suit[] NoSuits = new Suit[0];
        private static readonly Card[] NoCards = new Card[0];

        private ChestsLogEntry(ChestsLogKind kind, int playerIndex, string text)
        {
            Kind = kind;
            PlayerIndex = playerIndex;
            Text = text;
            Suits = NoSuits;
            Cards = NoCards;
        }

        public ChestsLogKind Kind { get; }

        /// <summary>Кто спрашивал, а у сундучка — чей он.</summary>
        public int PlayerIndex { get; }

        /// <summary>Готовая строка по-русски — её можно показывать как есть.</summary>
        public string Text { get; }

        /// <summary>Кого спрашивали. У передачи — кто отдал.</summary>
        public int? TargetIndex { get; private set; }

        public Rank? Rank { get; private set; }

        /// <summary>Названное число. У передачи — сколько карт перешло.</summary>
        public int? Count { get; private set; }

        /// <summary>Названные масти. Пусто везде, кроме вопроса о мастях.</summary>
        public IReadOnlyList<Suit> Suits { get; private set; }

        /// <summary>Ответ: есть ли ранг, угадано ли число, совпал ли набор.</summary>
        public bool? Success { get; private set; }

        /// <summary>Карты, которые все увидели: переданные или ушедшие в сундучок.</summary>
        public IReadOnlyList<Card> Cards { get; private set; }

        /// <summary>Сундучок пришёл готовым прямо на раздаче.</summary>
        public bool AtDeal { get; private set; }

        internal static ChestsLogEntry Ask(ChestsPlayer asker, ChestsPlayer target, Rank rank, bool has) =>
            new ChestsLogEntry(ChestsLogKind.Ask, asker.Index,
                asker.Name + ": «" + target.Name + ", есть " + ChestsRules.RankPluralNominative(rank) + "?» — "
                + (has ? "«Есть»" : "«Нет»"))
            {
                TargetIndex = target.Index,
                Rank = rank,
                Success = has
            };

        internal static ChestsLogEntry CountGuess(ChestsPlayer asker, ChestsPlayer target, Rank rank, int count, bool right) =>
            new ChestsLogEntry(ChestsLogKind.CountGuess, asker.Index,
                asker.Name + ": «" + ChestsRules.Capitalize(ChestsRules.CountPhrase(rank, count)) + "?» — "
                + (right ? "«Да»" : "«Нет»"))
            {
                TargetIndex = target.Index,
                Rank = rank,
                Count = count,
                Success = right
            };

        internal static ChestsLogEntry SuitsGuess(
            ChestsPlayer asker, ChestsPlayer target, Rank rank, IReadOnlyList<Suit> suits, bool right) =>
            new ChestsLogEntry(ChestsLogKind.SuitsGuess, asker.Index,
                asker.Name + ": «" + ChestsRules.Capitalize(ChestsRules.SuitsPhrase(suits)) + "?» — "
                + (right ? "«Да»" : "«Нет»"))
            {
                TargetIndex = target.Index,
                Rank = rank,
                Count = suits.Count,
                Suits = suits,
                Success = right
            };

        internal static ChestsLogEntry Transfer(ChestsPlayer asker, ChestsPlayer target, Rank rank, IReadOnlyList<Card> cards) =>
            new ChestsLogEntry(ChestsLogKind.Transfer, asker.Index,
                target.Name + " отдаёт " + BluffRules.NameDative(asker.Name) + " "
                + ChestsRules.CardsAccusative(rank, cards.Count))
            {
                TargetIndex = target.Index,
                Rank = rank,
                Count = cards.Count,
                Cards = cards,
                Success = true
            };

        internal static ChestsLogEntry Chest(ChestsPlayer owner, Rank rank, IReadOnlyList<Card> cards, bool atDeal) =>
            new ChestsLogEntry(ChestsLogKind.Chest, owner.Index,
                BluffRules.NameDative(owner.Name) + " — сундучок: " + ChestsRules.RankPluralNominative(rank)
                + (atDeal ? ", прямо с раздачи" : string.Empty))
            {
                Rank = rank,
                Count = cards.Count,
                Cards = cards,
                AtDeal = atDeal
            };

        internal static ChestsLogEntry GameOver(string text) =>
            new ChestsLogEntry(ChestsLogKind.GameOver, -1, text);
    }
}
