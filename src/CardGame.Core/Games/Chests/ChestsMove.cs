using System;
using System.Collections.Generic;
using CardGame.Core.Cards;

namespace CardGame.Core.Games.Chests
{
    public enum ChestsMoveType
    {
        /// <summary>Выбрать соперника и назвать ранг.</summary>
        Ask,

        /// <summary>Назвать, сколько карт этого ранга у адресата.</summary>
        GuessCount,

        /// <summary>Назвать весь набор мастей разом.</summary>
        GuessSuits
    }

    /// <summary>
    /// Шаг вопроса. Весь вопрос одним ходом не задаётся: после ранга спрашивающий узнаёт
    /// «есть» или «нет» и только потом называет число, а после числа — масти. Адресат при
    /// этом ничего не решает: отвечает за него сама игра, и соврать о своей руке нельзя.
    /// </summary>
    public sealed class ChestsMove
    {
        private static readonly Suit[] NoSuits = new Suit[0];

        private ChestsMove(ChestsMoveType type, int targetIndex, Rank rank, int count, IReadOnlyList<Suit> suits)
        {
            Type = type;
            TargetIndex = targetIndex;
            Rank = rank;
            Count = count;
            Suits = suits;
        }

        public ChestsMoveType Type { get; }

        /// <summary>Кого спрашивают. Только у <see cref="ChestsMoveType.Ask"/>.</summary>
        public int TargetIndex { get; }

        /// <summary>Какой ранг спрашивают. Только у <see cref="ChestsMoveType.Ask"/>.</summary>
        public Rank Rank { get; }

        /// <summary>Названное число карт. Только у <see cref="ChestsMoveType.GuessCount"/>.</summary>
        public int Count { get; }

        /// <summary>Названные масти. Только у <see cref="ChestsMoveType.GuessSuits"/>.</summary>
        public IReadOnlyList<Suit> Suits { get; }

        public static ChestsMove Ask(int targetIndex, Rank rank) =>
            new ChestsMove(ChestsMoveType.Ask, targetIndex, rank, 0, NoSuits);

        public static ChestsMove GuessCount(int count) =>
            new ChestsMove(ChestsMoveType.GuessCount, -1, Rank.Two, count, NoSuits);

        public static ChestsMove GuessSuits(params Suit[] suits) => GuessSuits((IReadOnlyList<Suit>)suits);

        public static ChestsMove GuessSuits(IReadOnlyList<Suit> suits)
        {
            if (suits == null)
            {
                throw new ArgumentNullException(nameof(suits));
            }

            var copy = new Suit[suits.Count];
            for (var i = 0; i < suits.Count; i++)
            {
                copy[i] = suits[i];
            }

            return new ChestsMove(ChestsMoveType.GuessSuits, -1, Rank.Two, 0, copy);
        }

        public override string ToString() => Type switch
        {
            ChestsMoveType.Ask => "есть " + ChestsRules.RankPluralNominative(Rank) + "? (игрок " + TargetIndex + ")",
            ChestsMoveType.GuessCount => "сколько: " + Count,
            _ => "масти: " + ChestsRules.SuitsPhrase(Suits)
        };
    }
}
