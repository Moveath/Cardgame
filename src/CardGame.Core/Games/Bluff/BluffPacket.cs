using System.Collections.Generic;
using CardGame.Core.Cards;

namespace CardGame.Core.Games.Bluff
{
    /// <summary>
    /// Один бросок в котёл: что заявили вслух и что легло на самом деле. Проверка вскрывает
    /// ровно один такой пакет — последний, — а не весь котёл.
    /// </summary>
    public sealed class BluffPacket
    {
        public BluffPacket(int playerIndex, Rank declaredRank, int declaredCount, IReadOnlyList<Card> cards)
        {
            PlayerIndex = playerIndex;
            DeclaredRank = declaredRank;
            DeclaredCount = declaredCount;
            Cards = cards;
        }

        public int PlayerIndex { get; }

        /// <summary>Публичная информация: ранг круга.</summary>
        public Rank DeclaredRank { get; }

        /// <summary>Публичная информация: сколько карт игрок назвал.</summary>
        public int DeclaredCount { get; }

        /// <summary>Скрытая информация до вскрытия. Клиент не показывает, боты не подсматривают.</summary>
        public IReadOnlyList<Card> Cards { get; }

        public bool IsLie => BluffRules.IsLie(DeclaredRank, DeclaredCount, Cards);
    }
}
