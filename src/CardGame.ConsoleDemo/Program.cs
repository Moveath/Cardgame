using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CardGame.Bots;
using CardGame.Core.Games.Durak;

Console.OutputEncoding = Encoding.UTF8;

var seed = args.Length > 0 && int.TryParse(args[0], out var parsedSeed) ? parsedSeed : Environment.TickCount;
var names = new List<string> { "Аня", "Борис", "Вера" };

var game = DurakGame.Create(names, seed);
var bot = new SimpleDurakBot();

Console.WriteLine($"Раздача (seed {seed}). Козырь: {game.TrumpCard}");
PrintHands(game);
Console.WriteLine();

var safetyLimit = 5000;
while (!game.IsFinished && safetyLimit-- > 0)
{
    var actor = DurakTurnOrder.GetActingPlayer(game);
    if (actor == null)
    {
        Console.WriteLine("Ходить некому — остановка.");
        break;
    }

    var move = bot.ChooseMove(game, actor.Value);
    if (move == null)
    {
        Console.WriteLine("Бот не нашёл ход — остановка.");
        break;
    }

    var player = game.Players[actor.Value];
    var tableBefore = DescribeTable(game);

    if (!game.TryApplyMove(actor.Value, move.Value, out var error))
    {
        Console.WriteLine($"ОШИБКА: {player.Name} — {move.Value}: {error}");
        break;
    }

    Console.WriteLine($"{player.Name,-7} {move.Value,-18} стол: {tableBefore} -> {DescribeTable(game)}");
}

Console.WriteLine();
if (game.IsFinished)
{
    Console.WriteLine(game.LoserIndex.HasValue
        ? $"Партия окончена. Дурак — {game.Players[game.LoserIndex.Value].Name}."
        : "Партия окончена. Ничья.");
}

static void PrintHands(DurakGame game)
{
    foreach (var player in game.Players)
    {
        Console.WriteLine($"  {player.Name,-7} {string.Join(" ", player.Hand)}");
    }
}

static string DescribeTable(DurakGame game)
{
    if (game.Table.Count == 0)
    {
        return "(пусто)";
    }

    return string.Join(" ", game.Table.Select(slot => slot.ToString()));
}
