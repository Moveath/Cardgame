using CardGame.Bots;
using CardGame.Core.Cards;
using CardGame.Core.Games.Bluff;
using Xunit;

namespace CardGame.Core.Tests;

public class SimpleBluffBotTests
{
    private static Card C(Rank rank, Suit suit) => new(rank, suit);

    private static Deck BuildDeck(params Card[][] hands)
    {
        var longest = 0;
        foreach (var hand in hands)
        {
            if (hand.Length > longest)
            {
                longest = hand.Length;
            }
        }

        var cards = new List<Card>();
        for (var round = 0; round < longest; round++)
        {
            foreach (var hand in hands)
            {
                if (round < hand.Length)
                {
                    cards.Add(hand[round]);
                }
            }
        }

        return new Deck(cards);
    }

    /// <summary>
    /// Главный приём бота — арифметика: каждого ранга в колоде четыре. Три шестёрки на
    /// своей руке плюс три заявленные не помещаются в колоду, значит это ложь, и никакой
    /// случайности тут быть не должно.
    /// </summary>
    [Fact]
    public void Bot_AlwaysCatchesAnImpossibleClaim()
    {
        var game = BluffGame.Create(new[] { "Врун", "Бот" }, BuildDeck(
            new[] { C(Rank.Six, Suit.Spades), C(Rank.King, Suit.Hearts), C(Rank.Queen, Suit.Clubs) },
            new[] { C(Rank.Six, Suit.Hearts), C(Rank.Six, Suit.Diamonds), C(Rank.Six, Suit.Clubs) }));

        Assert.True(game.TryApplyMove(0, BluffMove.Declare(Rank.Six, 3,
            new[] { C(Rank.Six, Suit.Spades), C(Rank.King, Suit.Hearts), C(Rank.Queen, Suit.Clubs) }), out var error), error);

        // Даже с самым «доверчивым» генератором бот обязан вскрыть: сходится арифметика.
        for (var seed = 0; seed < 20; seed++)
        {
            var bot = new SimpleBluffBot(new Random(seed));
            Assert.True(bot.ShouldChallenge(game, 1));
        }
    }

    [Fact]
    public void Bot_DeclaresTheRankItHoldsMostOf()
    {
        var game = BluffGame.Create(new[] { "Бот", "Сосед" }, BuildDeck(
            new[]
            {
                C(Rank.Nine, Suit.Spades), C(Rank.Nine, Suit.Hearts), C(Rank.Nine, Suit.Diamonds),
                C(Rank.King, Suit.Clubs)
            },
            new[]
            {
                C(Rank.Seven, Suit.Spades), C(Rank.Seven, Suit.Hearts), C(Rank.Eight, Suit.Diamonds),
                C(Rank.Eight, Suit.Clubs)
            }));

        var bot = new SimpleBluffBot(new Random(1));
        var move = bot.ChooseMove(game, 0);

        Assert.NotNull(move);
        Assert.Equal(BluffMoveType.Declare, move!.Type);
        Assert.Equal(Rank.Nine, move.Rank);
        Assert.Contains(move.Cards, card => card.Rank == Rank.Nine);
    }

    [Fact]
    public void Bot_NeverDeclaresAces_EvenHoldingNothingElse()
    {
        var game = BluffGame.Create(new[] { "Бот", "Сосед" }, BuildDeck(
            new[] { C(Rank.Ace, Suit.Spades), C(Rank.Ace, Suit.Hearts) },
            new[] { C(Rank.Seven, Suit.Spades), C(Rank.Eight, Suit.Hearts) }));

        // Партия ещё идёт: у соседа есть обычные карты.
        Assert.False(game.IsFinished);

        for (var seed = 0; seed < 20; seed++)
        {
            var bot = new SimpleBluffBot(new Random(seed));
            var move = bot.ChooseMove(game, 0);

            Assert.NotNull(move);
            Assert.NotEqual(Rank.Ace, move!.Rank);
            Assert.True(BluffRules.CanBeDeclared(move.Rank));
        }
    }

    [Fact]
    public void Bot_DiscardsFourOfAKindWhenItHasOne()
    {
        var game = BluffGame.Create(new[] { "Бот", "Сосед" }, BuildDeck(
            new[]
            {
                C(Rank.King, Suit.Spades), C(Rank.King, Suit.Hearts),
                C(Rank.King, Suit.Diamonds), C(Rank.King, Suit.Clubs), C(Rank.Two, Suit.Spades)
            },
            new[]
            {
                C(Rank.Seven, Suit.Spades), C(Rank.Seven, Suit.Hearts),
                C(Rank.Eight, Suit.Diamonds), C(Rank.Eight, Suit.Clubs), C(Rank.Nine, Suit.Spades)
            }));

        var bot = new SimpleBluffBot(new Random(5));
        var move = bot.ChooseFourOfAKind(game, 0);

        Assert.NotNull(move);
        Assert.Equal(BluffMoveType.DiscardFour, move!.Type);
        Assert.Equal(Rank.King, move.Rank);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    public void BotGames_Finish_WithoutDeadlockOrLostCards(int playerCount)
    {
        var names = new List<string>();
        for (var i = 0; i < playerCount; i++)
        {
            names.Add("Бот" + i);
        }

        for (var seed = 0; seed < 40; seed++)
        {
            var game = BluffGame.Create(names, Deck.Standard36(seed));
            var bot = new SimpleBluffBot(new Random(seed * 17 + playerCount));

            var guard = 0;
            while (!game.IsFinished)
            {
                Assert.True(guard++ < 4000, "Боты зациклились: seed " + seed);

                var actor = game.CurrentPlayerIndex;

                var four = bot.ChooseFourOfAKind(game, actor);
                if (four != null)
                {
                    Assert.True(game.TryApplyMove(actor, four, out var fourError), fourError);
                    continue;
                }

                var move = bot.ChooseMove(game, actor);
                Assert.NotNull(move);
                Assert.True(game.TryApplyMove(actor, move!, out var error), error);

                AssertNoCardIsLost(game);
            }

            AssertNoCardIsLost(game);
            Assert.Equal(playerCount, game.Standings.Count);
        }
    }

    private static void AssertNoCardIsLost(BluffGame game)
    {
        var total = game.PotCount + game.RemovedCount;
        foreach (var player in game.Players)
        {
            total += player.Hand.Count;
        }

        Assert.Equal(36, total);
    }
}
