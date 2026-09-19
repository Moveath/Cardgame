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
    /// Дополнительно умеет переводить (если правило включено) и — в шулерских партиях —
    /// подсовывать нелегальную карту вместо того, чтобы честно забирать или пасовать.
    /// </summary>
    public sealed class SimpleDurakBot
    {
        // Форсированный обман — единственная страховка от "забрать всю раздачу": если поймают,
        // спорная карта просто вернётся в руку, а если нет — вся горка уходит в биту бесплатно.
        // Поэтому шанс высокий, но не 100%, иначе бот обманывал бы как по расписанию.
        private const double CheatInsteadOfTakeChance = 0.65;
        private const double CheatInsteadOfPassChance = 0.4;

        private readonly Random _random;

        public SimpleDurakBot() : this(new Random())
        {
        }

        public SimpleDurakBot(Random random)
        {
            _random = random;
        }

        public DurakMove? ChooseMove(DurakGame game, int playerIndex)
        {
            var moves = game.GetLegalMoves(playerIndex);
            if (playerIndex == game.DefenderIndex)
            {
                return ChooseDefense(game, playerIndex, moves);
            }

            return ChooseAttack(game, playerIndex, moves);
        }

        private DurakMove? ChooseDefense(DurakGame game, int playerIndex, IReadOnlyList<DurakMove> moves)
        {
            var bestDefense = moves
                .Where(m => m.Type == DurakMoveType.Defend)
                .OrderBy(m => Cost(m.Card, game.Trump))
                .ToList();

            // Дешёвую защиту не меняем ни на перевод, ни на обман — она и так ничего не стоит.
            if (bestDefense.Count > 0 && Cost(bestDefense[0].Card, game.Trump) < 100)
            {
                return bestDefense[0];
            }

            var transfers = moves
                .Where(m => m.Type == DurakMoveType.Transfer)
                .OrderBy(m => Cost(m.Card, game.Trump))
                .ToList();

            if (transfers.Count > 0)
            {
                return transfers[0];
            }

            if (bestDefense.Count > 0)
            {
                return bestDefense[0];
            }

            // Обманывать стоит только если не поймали в этой раздаче: GetCheatMoves сам
            // вернёт пусто, если бот уже спалился.
            var cheats = game.GetCheatMoves(playerIndex);
            if (cheats.Count > 0 && _random.NextDouble() < CheatInsteadOfTakeChance)
            {
                // Лже-перевод выгоднее лже-отбоя: уезжаешь от всей раздачи, а не от одной карты.
                var fakeTransfer = cheats
                    .Where(m => m.Type == DurakMoveType.CheatTransfer)
                    .OrderBy(m => Cost(m.Card, game.Trump))
                    .ToList();

                if (fakeTransfer.Count > 0)
                {
                    return fakeTransfer[0];
                }

                var fakeDefense = cheats
                    .Where(m => m.Type == DurakMoveType.CheatDefend)
                    .OrderBy(m => Cost(m.Card, game.Trump))
                    .ToList();

                if (fakeDefense.Count > 0)
                {
                    return fakeDefense[0];
                }
            }

            if (moves.Any(m => m.Type == DurakMoveType.Take))
            {
                return moves.First(m => m.Type == DurakMoveType.Take);
            }

            return null;
        }

        private DurakMove? ChooseAttack(DurakGame game, int playerIndex, IReadOnlyList<DurakMove> moves)
        {
            var attacks = moves
                .Where(m => m.Type == DurakMoveType.Attack)
                .OrderBy(m => Cost(m.Card, game.Trump))
                .ToList();

            var canPass = moves.Any(m => m.Type == DurakMoveType.Pass);

            if (attacks.Count > 0)
            {
                var cheapest = attacks[0];
                if (canPass && cheapest.Card.Suit == game.Trump)
                {
                    return moves.First(m => m.Type == DurakMoveType.Pass);
                }

                return cheapest;
            }

            var cheatAttacks = game.GetCheatMoves(playerIndex)
                .Where(m => m.Type == DurakMoveType.CheatAttack)
                .OrderBy(m => Cost(m.Card, game.Trump))
                .ToList();

            if (cheatAttacks.Count > 0 && _random.NextDouble() < CheatInsteadOfPassChance)
            {
                return cheatAttacks[0];
            }

            return canPass ? moves.First(m => m.Type == DurakMoveType.Pass) : (DurakMove?)null;
        }

        /// <summary>
        /// Вызывается между ходами: бот-свидетель решает, ловить ли последний ход на обмане.
        /// Ловит только настоящий обман — попытки поймать честный ход не тратятся впустую —
        /// но с шансом на невнимательность, иначе шулерство против ботов было бы бессмысленным.
        /// </summary>
        public bool TryChallenge(DurakGame game, int playerIndex, double noticeChance, out string? error)
        {
            error = null;

            if (!game.HasChallengeableMove || game.ChallengeablePlayerIndex == playerIndex)
            {
                return false;
            }

            // Уже промахнулся в этой раздаче — второй попытки нет, и зря тратить её незачем.
            if (!game.CanChallenge(playerIndex))
            {
                return false;
            }

            if (!game.ChallengeTargetIsActuallyIllegal())
            {
                return false;
            }

            if (_random.NextDouble() > noticeChance)
            {
                return false;
            }

            return game.TryChallenge(playerIndex, game.ChallengeableSlot!.Value, game.ChallengeableIsAttackCard!.Value, out error);
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
