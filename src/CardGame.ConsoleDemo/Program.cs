using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CardGame.Bots;
using CardGame.Core.Games.Durak;

Console.OutputEncoding = Encoding.UTF8;

var autoMode = args.Any(a => a.Equals("auto", StringComparison.OrdinalIgnoreCase));
var seed = args.Select(a => int.TryParse(a, out var parsed) ? parsed : (int?)null)
    .FirstOrDefault(v => v.HasValue) ?? Environment.TickCount;

var names = new List<string> { "Ты", "Борис", "Вера" };
var humanIndex = autoMode ? -1 : 0;
if (autoMode)
{
    names[0] = "Аня";
}

var game = DurakGame.Create(names, seed);
var bot = new SimpleDurakBot();

Console.WriteLine($"Раздача (seed {seed}). Козырь: {game.TrumpCard}");
Console.WriteLine();

var guard = 5000;
while (!game.IsFinished && guard-- > 0)
{
    var actor = DurakTurnOrder.GetActingPlayer(game);
    if (actor == null)
    {
        Console.WriteLine("Ходить некому — остановка.");
        break;
    }

    var move = actor.Value == humanIndex ? AskHuman(actor.Value) : bot.ChooseMove(game, actor.Value);
    if (move == null)
    {
        break;
    }

    var player = game.Players[actor.Value];
    if (!game.TryApplyMove(actor.Value, move.Value, out var error))
    {
        Console.WriteLine($"ОШИБКА: {player.Name} — {move.Value}: {error}");
        break;
    }

    if (actor.Value != humanIndex)
    {
        Console.WriteLine($"{player.Name,-7} {move.Value,-18} стол: {DescribeTable()}");
    }
}

Console.WriteLine();
if (game.IsFinished)
{
    Console.WriteLine(game.LoserIndex.HasValue
        ? $"Партия окончена. Дурак — {game.Players[game.LoserIndex.Value].Name}."
        : "Партия окончена. Ничья.");
}

DurakMove? AskHuman(int playerIndex)
{
    var moves = game.GetLegalMoves(playerIndex);
    if (moves.Count == 0)
    {
        return null;
    }

    while (true)
    {
        PrintBoard(playerIndex);

        Console.WriteLine("  Твои ходы:");
        for (var i = 0; i < moves.Count; i++)
        {
            Console.WriteLine($"   {i + 1,2}) {Describe(moves[i])}");
        }

        Console.Write("  Номер хода: ");
        var input = Console.ReadLine();
        if (input == null)
        {
            return null;
        }

        if (int.TryParse(input.Trim(), out var choice) && choice >= 1 && choice <= moves.Count)
        {
            Console.WriteLine();
            return moves[choice - 1];
        }

        Console.WriteLine("  Нужен номер из списка.");
        Console.WriteLine();
    }
}

void PrintBoard(int playerIndex)
{
    var role = playerIndex == game.DefenderIndex ? "защищаешься" : "атакуешь";

    Console.WriteLine(new string('-', 60));
    Console.WriteLine($"  Козырь {game.TrumpCard}   в колоде {game.DeckCount}   ты {role}");

    foreach (var other in game.Players.Where(p => p.Index != playerIndex))
    {
        var marker = other.Index == game.DefenderIndex ? " (защищается)" : string.Empty;
        var status = other.IsOut ? "вышел" : other.Hand.Count + " карт";
        Console.WriteLine($"  {other.Name,-7} {status}{marker}");
    }

    Console.WriteLine($"  Стол:  {DescribeTable()}");
    Console.WriteLine($"  Рука:  {string.Join(" ", game.Players[playerIndex].Hand)}");
}

string Describe(DurakMove move) => move.Type switch
{
    DurakMoveType.Attack => "подкинуть " + move.Card,
    DurakMoveType.Defend => $"отбить {game.Table[move.TargetSlot].Attack} картой {move.Card}",
    DurakMoveType.Take => "забрать карты",
    _ => "пас / бито"
};

string DescribeTable() => game.Table.Count == 0
    ? "(пусто)"
    : string.Join("  ", game.Table.Select(slot => slot.ToString()));
