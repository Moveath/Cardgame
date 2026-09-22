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
        DiscardFour,

        /// <summary>Сдаться: признать проигрыш и не тянуть решённую партию.</summary>
        Concede
    }

    /// <summary>
    /// Ход игрока. Количество отдельным полем не задаётся: сколько карт положил, столько
    /// и объявил. Врут в этой игре только рангом.
    /// </summary>
    public sealed class BluffMove
    {
        private static readonly Card[] NoCards = new Card[0];

        private BluffMove(BluffMoveType type, Rank rank, IReadOnlyList<Card> cards, bool opensShootout = false)
        {
            Type = type;
            Rank = rank;
            Cards = cards;
            OpensShootout = opensShootout;
        }

        public BluffMoveType Type { get; }

        /// <summary>Для Declare — заявленный ранг, для DiscardFour — ранг каре. Иначе не используется.</summary>
        public Rank Rank { get; }

        /// <summary>Сколько карт объявлено вслух — всегда столько же, сколько положено.</summary>
        public int DeclaredCount => Cards.Count;

        /// <summary>Что реально ушло в котёл. Скрытая информация: показывать её нельзя до вскрытия.</summary>
        public IReadOnlyList<Card> Cards { get; }

        /// <summary>
        /// Этот бросок открывает перестрелку. Отдельным типом хода делать нечего: правила
        /// круга те же самые, меняется только то, чем обернётся проверка.
        /// </summary>
        public bool OpensShootout { get; }

        public static BluffMove Declare(Rank rank, IReadOnlyList<Card> cards) =>
            new BluffMove(BluffMoveType.Declare, rank, Copy(cards));

        public static BluffMove Declare(Rank rank, params Card[] cards) =>
            new BluffMove(BluffMoveType.Declare, rank, Copy(cards));

        public static BluffMove Follow(IReadOnlyList<Card> cards) =>
            new BluffMove(BluffMoveType.Follow, Rank.Two, Copy(cards));

        public static BluffMove Follow(params Card[] cards) =>
            new BluffMove(BluffMoveType.Follow, Rank.Two, Copy(cards));

        public static BluffMove DeclareShootout(Rank rank, IReadOnlyList<Card> cards) =>
            new BluffMove(BluffMoveType.Declare, rank, Copy(cards), opensShootout: true);

        public static BluffMove DeclareShootout(Rank rank, params Card[] cards) =>
            new BluffMove(BluffMoveType.Declare, rank, Copy(cards), opensShootout: true);

        public static BluffMove Challenge() =>
            new BluffMove(BluffMoveType.Challenge, Rank.Two, NoCards);

        public static BluffMove DiscardFour(Rank rank) =>
            new BluffMove(BluffMoveType.DiscardFour, rank, NoCards);

        public static BluffMove Concede() =>
            new BluffMove(BluffMoveType.Concede, Rank.Two, NoCards);

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
            BluffMoveType.Declare => (OpensShootout ? "перестрелка! " : string.Empty) + Describe(Rank),
            BluffMoveType.Follow => Describe(null),
            BluffMoveType.Challenge => "не верю",
            BluffMoveType.Concede => "сдаюсь",
            _ => "каре " + BluffRules.RankPlural(Rank)
        };

        private string Describe(Rank? rank)
        {
            var text = "скинул " + DeclaredCount + " " + BluffRules.CardsWord(DeclaredCount);
            return rank.HasValue ? text + " под видом " + BluffRules.RankPlural(rank.Value) : text;
        }
    }
}
