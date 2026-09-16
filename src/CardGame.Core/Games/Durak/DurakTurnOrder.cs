using System.Linq;

namespace CardGame.Core.Games.Durak
{
    /// <summary>
    /// Определяет, чей сейчас ход. Сервер и клиент используют это, чтобы не гадать,
    /// кому показывать активные кнопки.
    /// </summary>
    public static class DurakTurnOrder
    {
        public static int? GetActingPlayer(DurakGame game)
        {
            if (game.IsFinished)
            {
                return null;
            }

            if (!game.DefenderTook && game.Table.Any(slot => !slot.IsDefended))
            {
                if (game.GetLegalMoves(game.DefenderIndex).Count > 0)
                {
                    return game.DefenderIndex;
                }
            }

            for (var offset = 0; offset < game.Players.Count; offset++)
            {
                var index = (game.AttackerIndex + offset) % game.Players.Count;
                if (index == game.DefenderIndex || game.Players[index].IsOut)
                {
                    continue;
                }

                if (game.GetLegalMoves(index).Count > 0)
                {
                    return index;
                }
            }

            return null;
        }
    }
}
