using System;
using System.Collections.Generic;
using CardGame.Core.Cards;

namespace CardGame.Core.Games.Bluff
{
    public enum BluffMoveType
    {
        /// <summary>Первый бросок круга: игрок сам называет ранг.</summary>
        Declare,

        /// <summary>Поддержать цепочку: ранг уже назван и менять его нельзя.</summary>
        Follow,

        /// <summary>«Не верю»: вскрыть последний пакет.</summary>
        Challenge,

        /// <summary>Каре вне очереди.</summary>
        DiscardFour
    }

    /// <summary>
    /// Ход игрока. Заявленное количество отделено от реально положенных карт намеренно:
    /// пакет уходит в котёл рубашкой вверх, пересчитать его со стороны нельзя, и соврать
    /// про число карт — такой же законный блеф, как соврать про ранг.
    /// </summary>
    public sealed class BluffMove
    {
        private static readonly Card[] NoCards = new Card[0];

        private BluffMove(BluffMoveType type, Rank rank, int declaredCount, IReadOnlyList<Card> cards)
        {
            Type = type;
            Rank = rank;
            DeclaredCount = declaredCount;
            Cards = cards;
        }

        public BluffMoveType Type { get; }

        /// <summary>Для Declare — заявленный ранг, для DiscardFour — ранг каре. Иначе не используется.</summary>
        public Rank Rank { get; }

        /// <summary>Сколько карт игрок назвал вслух. У Challenge и DiscardFour равно нулю.</summary>
        public int DeclaredCount { get; }

        /// <summary>Что реально ушло в котёл. Скрытая информация: показывать её нельзя до вскрытия.</summary>
        public IReadOnlyList<Card> Cards { get; }

        public static BluffMove Declare(Rank rank, int declaredCount, IReadOnlyList<Card> cards) =>
            new BluffMove(BluffMoveType.Declare, rank, declaredCount, Copy(cards));

        /// <summary>Честное количество: сколько положил, столько и назвал. Врать при этом рангом никто не мешает.</summary>
        public static BluffMove Declare(Rank rank, params Card[] cards) =>
            Declare(rank, cards.Length, cards);

        public static BluffMove Follow(int declaredCount, IReadOnlyList<Card> cards) =>
            new BluffMove(BluffMoveType.Follow, Rank.Two, declaredCount, Copy(cards));

        public static BluffMove Follow(params Card[] cards) =>
            Follow(cards.Length, cards);

        public static BluffMove Challenge() =>
            new BluffMove(BluffMoveType.Challenge, Rank.Two, 0, NoCards);

        public static BluffMove DiscardFour(Rank rank) =>
            new BluffMove(BluffMoveType.DiscardFour, rank, 0, NoCards);

        private static IReadOnlyList<Card> Copy(IReadOnlyList<Card> cards)
        {
            if (cards == null)
            {
                throw new ArgumentNullException(nameof(cards));
            }

            var copy = new Card[cards.Count];
            for (var i = 0; i < cards.Count; i++)
            {
                copy[i] = cards[i];
            }

            return copy;
        }

        /// <summary>
        /// Только то, что видно за столом. Реальные карты сюда не попадают — иначе лог
        /// раскрывал бы блеф, а он и есть вся игра.
        /// </summary>
        public override string ToString() => Type switch
        {
            BluffMoveType.Declare => Describe(Rank),
            BluffMoveType.Follow => Describe(null),
            BluffMoveType.Challenge => "не верю",
            _ => "каре " + BluffRules.RankPlural(Rank)
        };

        private string Describe(Rank? rank)
        {
            var text = "скинул " + DeclaredCount + " " + BluffRules.CardsWord(DeclaredCount);
            return rank.HasValue ? text + " под видом " + BluffRules.RankPlural(rank.Value) : text;
        }
    }
}
