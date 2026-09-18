using System;
using System.Collections.Generic;
using System.Linq;
using CardGame.Core.Cards;
using CardGame.Core.Games.Durak;

namespace CardGame.Bots
{
    /// <summary>
    /// Эвристический бот: бьёт и ходит самой дешёвой картой, бережёт козыри.
    /// Нужен для автотестов, добора комнат и режима игры с компьютером.
    /// </summary>
    public sealed class SimpleDurakBot
    {
        public DurakMove? ChooseMove(DurakGame game, int playerIndex)
        {
            var moves = game.GetLegalMoves(playerIndex);
            if (moves.Count == 0)
            {
                return null;
            }

            if (playerIndex == game.DefenderIndex)
            {
                var bestDefense = moves
                    .Where(m => m.Type == DurakMoveType.Defend)
                    .OrderBy(m => Cost(m.Card, game.Trump))
                    .ToList();

                if (bestDefense.Count > 0)
                {
                    return bestDefense[0];
                }

                return moves.First(m => m.Type == DurakMoveType.Take);
            }

            var attacks = moves
                .Where(m => m.Type == DurakMoveType.Attack)
                .OrderBy(m => Cost(m.Card, game.Trump))
                .ToList();

            var canPass = moves.Any(m => m.Type == DurakMoveType.Pass);

            if (attacks.Count == 0)
            {
                return canPass ? moves.First(m => m.Type == DurakMoveType.Pass) : (DurakMove?)null;
            }

            var cheapest = attacks[0];
            if (canPass && cheapest.Card.Suit == game.Trump)
            {
                return moves.First(m => m.Type == DurakMoveType.Pass);
            }

            return cheapest;
        }

        private static int Cost(Card card, Suit trump)
        {
            // Джокер — самая сильная карта в игре, а не "дешёвая" из-за случайно низкого ранга.
            // Без этого бот радостно скидывал бы его на первом же ходу.
            if (card.IsJoker)
            {
                return 1000;
            }

            return (card.Suit == trump ? 100 : 0) + (int)card.Rank;
        }
    }
}
