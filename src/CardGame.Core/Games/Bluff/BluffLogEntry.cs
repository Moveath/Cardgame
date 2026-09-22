using System.Collections.Generic;
using CardGame.Core.Cards;

namespace CardGame.Core.Games.Bluff
{
    public enum BluffLogKind
    {
        Throw,

        /// <summary>Объявлена перестрелка. Видно всем — в этом весь смысл объявления.</summary>
        Shootout,

        Challenge,
        FourOfAKind,

        /// <summary>Игрок сдался сам.</summary>
        Concede,

        PlayerOut,
        GameOver
    }

    /// <summary>
    /// Строка общего лога. Сюда попадает только то, что за столом видно всем: заявленный
    /// ранг и заявленное количество. Реальные карты появляются единственным способом —
    /// через <see cref="RevealedCards"/> после вскрытия, когда их и правда все увидели.
    /// </summary>
    public sealed class BluffLogEntry
    {
        private static readonly Card[] NoCards = new Card[0];

        private BluffLogEntry(BluffLogKind kind, int playerIndex, string text)
        {
            Kind = kind;
            PlayerIndex = playerIndex;
            Text = text;
            RevealedCards = NoCards;
        }

        public BluffLogKind Kind { get; }

        /// <summary>Кто действовал.</summary>
        public int PlayerIndex { get; }

        /// <summary>Готовая строка по-русски — её можно показывать как есть.</summary>
        public string Text { get; }

        /// <summary>Кого проверяли. Заполнено только у <see cref="BluffLogKind.Challenge"/>.</summary>
        public int? TargetIndex { get; private set; }

        /// <summary>Заявленный ранг броска. У проверки — ранг вскрытого пакета.</summary>
        public Rank? DeclaredRank { get; private set; }

        public int? DeclaredCount { get; private set; }

        /// <summary>Соврал ли проверенный игрок. Заполнено только у проверки.</summary>
        public bool? WasLie { get; private set; }

        /// <summary>Проверка оборвала перестрелку — проверяющему за это достался петух.</summary>
        public bool BrokeShootout { get; private set; }

        /// <summary>Кто забрал котёл.</summary>
        public int? TakenBy { get; private set; }

        public int? TakenCards { get; private set; }

        /// <summary>Вскрытый пакет. Пуст везде, кроме проверки: до вскрытия карты не разглашаются.</summary>
        public IReadOnlyList<Card> RevealedCards { get; private set; }

        internal static BluffLogEntry Throw(BluffPlayer player, Rank rank, int declaredCount)
        {
            var text = player.Name + " скинул " + declaredCount + " " + BluffRules.CardsWord(declaredCount)
                       + " под видом " + BluffRules.RankPlural(rank);

            return new BluffLogEntry(BluffLogKind.Throw, player.Index, text)
            {
                DeclaredRank = rank,
                DeclaredCount = declaredCount
            };
        }

        internal static BluffLogEntry Shootout(BluffPlayer player, Rank rank) =>
            new BluffLogEntry(BluffLogKind.Shootout, player.Index,
                player.Name + " объявил перестрелку на " + BluffRules.RankPluralAccusative(rank) + "!")
            {
                DeclaredRank = rank
            };

        internal static BluffLogEntry Challenge(
            BluffPlayer challenger, BluffPlayer target, BluffPacket packet, int takenBy, int takenCards,
            bool brokeShootout)
        {
            var lie = packet.IsLie;
            var text = challenger.Name + ": не верю! " + target.Name
                       + (lie ? " соврал — забирает котёл (" : " сказал правду — котёл забирает проверяющий (")
                       + takenCards + " " + BluffRules.CardsWord(takenCards) + ")";

            if (brokeShootout)
            {
                text += ". Перестрелку сорвал " + challenger.Name + " — петух!";
            }

            return new BluffLogEntry(BluffLogKind.Challenge, challenger.Index, text)
            {
                TargetIndex = target.Index,
                DeclaredRank = packet.DeclaredRank,
                DeclaredCount = packet.DeclaredCount,
                WasLie = lie,
                TakenBy = takenBy,
                TakenCards = takenCards,
                BrokeShootout = brokeShootout,
                RevealedCards = packet.Cards
            };
        }

        internal static BluffLogEntry FourOfAKind(BluffPlayer player, Rank rank) =>
            new BluffLogEntry(BluffLogKind.FourOfAKind, player.Index,
                player.Name + " выложил каре " + BluffRules.RankPlural(rank))
            {
                DeclaredRank = rank,
                DeclaredCount = 4
            };

        internal static BluffLogEntry Concede(BluffPlayer player) =>
            new BluffLogEntry(BluffLogKind.Concede, player.Index,
                player.Name + " сдался: честно ходить нечем, одни тузы");

        internal static BluffLogEntry PlayerOut(BluffPlayer player, int place) =>
            new BluffLogEntry(BluffLogKind.PlayerOut, player.Index,
                player.Name + " избавился от карт — место " + place);

        internal static BluffLogEntry GameOver(string text) =>
            new BluffLogEntry(BluffLogKind.GameOver, -1, text);
    }
}
