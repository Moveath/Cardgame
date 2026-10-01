using System.Reflection;
using CardGame.Bots;
using CardGame.Core.Cards;
using CardGame.Core.Games.Poker;

namespace CardGame.Core.Tests;

public class SimplePokerBotTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(6)]
    public void BotsMakeLegalMovesThroughManyHandsAndPreserveAllChips(int players)
    {
        var names = Enumerable.Range(0, players).Select(i => "Игрок " + i).ToArray();
        for (var seed = 0; seed < 12; seed++)
        {
            var game = new PokerGame(names, seed, 300);
            var bots = Enumerable.Range(0, players).Select(i => new SimplePokerBot(seed * 31 + i)).ToArray();
            for (var hand = 0; hand < 50; hand++)
            {
                var actions = 0;
                while (game.CurrentPlayer >= 0)
                {
                    Assert.True(actions++ < 200, $"Hand did not finish: {players} players, seed {seed}, hand {hand}");
                    var actor = game.CurrentPlayer;
                    var beforeRevision = game.Revision;
                    var move = bots[actor].Choose(game, actor);
                    Assert.Equal(beforeRevision, game.Revision);
                    Assert.True(game.TryAct(actor, move.Action, move.RaiseTo),
                        $"Illegal {move.Action} to {move.RaiseTo}; players={players}, seed={seed}, hand={hand}, phase={game.Phase}");
                    Assert.Equal(players * 300, game.Players.Sum(p => p.Stack) + game.Pot);
                    Assert.All(game.Players, p => Assert.True(p.Stack >= 0));
                }
                Assert.Contains(game.Phase, new[] { PokerPhase.HandComplete, PokerPhase.MatchComplete });
                if (!game.StartNextHand()) break;
            }
        }
    }

    [Theory]
    [InlineData(21)]
    [InlineData(30)]
    [InlineData(45)]
    public void BotsHandleVeryShortStacksAndPartialAllIns(int startingStack)
    {
        var names = new[] { "А", "Б", "В", "Г", "Д", "Е" };
        for (var seed = 0; seed < 30; seed++)
        {
            var game = new PokerGame(names, seed, startingStack);
            var bots = Enumerable.Range(0, 6).Select(i => new SimplePokerBot(seed + i)).ToArray();
            for (var actions = 0; game.CurrentPlayer >= 0 && actions < 100; actions++)
            {
                var actor = game.CurrentPlayer;
                var move = bots[actor].Choose(game, actor);
                Assert.True(game.TryAct(actor, move.Action, move.RaiseTo));
            }
            Assert.Equal(-1, game.CurrentPlayer);
            Assert.Equal(startingStack * 6, game.Players.Sum(p => p.Stack));
        }
    }

    [Theory]
    [InlineData(PokerPhase.Preflop)]
    [InlineData(PokerPhase.Flop)]
    [InlineData(PokerPhase.Turn)]
    [InlineData(PokerPhase.River)]
    public void HiddenOpponentCardsAndFutureDeckCannotChangeDecision(PokerPhase phase)
    {
        for (var seed = 0; seed < 30; seed++)
        {
            var original = new PokerGame(new[] { "А", "Б", "В", "Г", "Д", "Е" }, seed);
            var changed = new PokerGame(new[] { "А", "Б", "В", "Г", "Д", "Е" }, seed);
            CallToPhase(original, phase);
            CallToPhase(changed, phase);
            var actor = original.CurrentPlayer;
            ReplaceHiddenInformation(changed, actor, seed + 1000);
            var one = new SimplePokerBot(seed).Choose(original, actor);
            var two = new SimplePokerBot(seed).Choose(changed, actor);
            Assert.Equal(one.Action, two.Action);
            Assert.Equal(one.RaiseTo, two.RaiseTo);
        }
    }

    [Fact]
    public void NeverFoldsWhenCheckingIsFree()
    {
        for (var seed = 0; seed < 100; seed++)
        {
            var game = new PokerGame(new[] { "А", "Б", "В" }, seed);
            CallToPhase(game, PokerPhase.Flop);
            var actor = game.CurrentPlayer;
            Assert.Equal(0, game.ToCall(actor));
            Assert.NotEqual(PokerAction.Fold, new SimplePokerBot(seed).Choose(game, actor).Action);
        }
    }

    [Fact]
    public void SeededBotIsRepeatableAndCanOnlyChooseOnItsTurn()
    {
        var game = new PokerGame(new[] { "А", "Б", "В" }, 7);
        var actor = game.CurrentPlayer;
        var first = new SimplePokerBot(19).Choose(game, actor);
        var second = new SimplePokerBot(19).Choose(game, actor);
        Assert.Equal(first.Action, second.Action);
        Assert.Equal(first.RaiseTo, second.RaiseTo);
        Assert.Throws<InvalidOperationException>(() => new SimplePokerBot(1).Choose(game, (actor + 1) % 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SimplePokerBot(1).Choose(game, -1));
        Assert.Throws<ArgumentNullException>(() => new SimplePokerBot(1).Choose(null!, 0));
    }

    private static void CallToPhase(PokerGame game, PokerPhase phase)
    {
        for (var steps = 0; game.Phase != phase && steps < 100; steps++)
        {
            var actor = game.CurrentPlayer;
            Assert.True(actor >= 0);
            Assert.True(game.TryAct(actor, game.ToCall(actor) == 0 ? PokerAction.Check : PokerAction.Call));
        }
        Assert.Equal(phase, game.Phase);
    }

    private static void ReplaceHiddenInformation(PokerGame game, int observer, int seed)
    {
        // Deliberately replace every hidden card, keeping the complete public state and own hand identical.
        // Reflection is confined to this test; production bots have no deck or opponent-card access.
        var known = game.Board.Concat(game.Players[observer].HoleCards).ToHashSet();
        var deck = Deck.Standard52(seed);
        var hidden = new Queue<Card>();
        while (!deck.IsEmpty)
        {
            var card = deck.Draw();
            if (!known.Contains(card)) hidden.Enqueue(card);
        }
        var holeField = typeof(PokerPlayer).GetField("_holeCards", BindingFlags.NonPublic | BindingFlags.Instance)!;
        foreach (var other in game.Players.Where(p => p.Index != observer))
        {
            var hole = (List<Card>)holeField.GetValue(other)!;
            hole.Clear();
            hole.Add(hidden.Dequeue());
            hole.Add(hidden.Dequeue());
        }
        typeof(PokerGame).GetField("_deck", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(game, new Deck(hidden));
    }
}
