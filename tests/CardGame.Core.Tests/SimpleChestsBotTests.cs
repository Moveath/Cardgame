using CardGame.Bots;
using CardGame.Core.Cards;
using CardGame.Core.Games.Chests;
using Xunit;

namespace CardGame.Core.Tests;

public class SimpleChestsBotTests
{
    private static readonly string[] Names = { "Егор", "Марат", "Бот" };

    private static Deck BuildDeck(params Card[][] hands)
    {
        var cards = new List<Card>();
        for (var round = 0; round < hands[0].Length; round++)
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

    private static Card C(Rank rank, Suit suit) => new(rank, suit);

    private static void Apply(ChestsGame game, int player, ChestsMove move)
    {
        Assert.True(game.TryApplyMove(player, move, out var error), error);
    }

    /// <summary>
    /// Марат сказал «нет» про девятки — значит, все чужие девятки у Егора. Спрашивать
    /// Марата ещё раз — выбросить ход, и бот так не делает.
    /// </summary>
    [Fact]
    public void Bot_RemembersWhoSaidNo()
    {
        for (var seed = 0; seed < 40; seed++)
        {
            var game = ChestsGame.Create(Names, BuildDeck(
                new[] { C(Rank.Nine, Suit.Clubs), C(Rank.Nine, Suit.Hearts), C(Rank.Nine, Suit.Spades),
                        C(Rank.Jack, Suit.Clubs), C(Rank.Jack, Suit.Diamonds), C(Rank.Queen, Suit.Clubs) },
                new[] { C(Rank.Jack, Suit.Hearts), C(Rank.Jack, Suit.Spades), C(Rank.Queen, Suit.Diamonds),
                        C(Rank.Queen, Suit.Hearts), C(Rank.Queen, Suit.Spades) },
                new[] { C(Rank.Nine, Suit.Diamonds), C(Rank.Ten, Suit.Clubs), C(Rank.Ten, Suit.Diamonds),
                        C(Rank.Ten, Suit.Hearts), C(Rank.Ten, Suit.Spades) }));

            // Десятки у бота ушли в сундучок на раздаче: на руке одна девятка.
            Assert.Equal(new[] { Rank.Nine }, ChestsRules.AskableRanks(game.Players[2].Hand, game.OpenRanks));

            Apply(game, 0, ChestsMove.Ask(1, Rank.Nine));
            Apply(game, 1, ChestsMove.Ask(0, Rank.Jack));
            Apply(game, 1, ChestsMove.GuessCount(1));
            Assert.Equal(2, game.CurrentPlayerIndex);

            var bot = new SimpleChestsBot(new Random(seed));
            var ask = bot.ChooseMove(game, 2)!;
            Assert.Equal(ChestsMoveType.Ask, ask.Type);
            Assert.Equal(0, ask.TargetIndex);
            Assert.Equal(Rank.Nine, ask.Rank);
            Apply(game, 2, ask);

            // Все три чужие девятки у Егора — бот называет три и забирает сундучок.
            var count = bot.ChooseMove(game, 2)!;
            Assert.Equal(3, count.Count);
            Apply(game, 2, count);
            Assert.Contains(Rank.Nine, game.Players[2].Chests);
        }
    }

    /// <summary>
    /// Егор у всех на глазах забрал у Марата двух дам. Бот держит четвёртую и знает, у кого
    /// остальные три, — идёт за сундучком, а не гадает по другим рангам.
    /// </summary>
    [Fact]
    public void Bot_FollowsCardsItSawChangeHands()
    {
        for (var seed = 0; seed < 40; seed++)
        {
            var game = ChestsGame.Create(Names, BuildDeck(
                new[] { C(Rank.Queen, Suit.Clubs), C(Rank.Nine, Suit.Clubs), C(Rank.Nine, Suit.Diamonds),
                        C(Rank.Ten, Suit.Clubs), C(Rank.Ten, Suit.Diamonds), C(Rank.Jack, Suit.Clubs) },
                new[] { C(Rank.Queen, Suit.Diamonds), C(Rank.Queen, Suit.Hearts), C(Rank.Nine, Suit.Hearts),
                        C(Rank.Ten, Suit.Hearts), C(Rank.Jack, Suit.Diamonds) },
                new[] { C(Rank.Queen, Suit.Spades), C(Rank.Nine, Suit.Spades), C(Rank.Ten, Suit.Spades),
                        C(Rank.Jack, Suit.Hearts), C(Rank.Jack, Suit.Spades) }));

            Apply(game, 0, ChestsMove.Ask(1, Rank.Queen));
            Apply(game, 0, ChestsMove.GuessCount(2));
            Apply(game, 0, ChestsMove.GuessSuits(Suit.Diamonds, Suit.Hearts));
            Assert.Equal(3, game.Players[0].CountOf(Rank.Queen));

            Apply(game, 0, ChestsMove.Ask(1, Rank.Nine));
            Apply(game, 0, ChestsMove.GuessCount(2));
            Apply(game, 1, ChestsMove.Ask(0, Rank.Ten));
            Apply(game, 1, ChestsMove.GuessCount(1));
            Assert.Equal(2, game.CurrentPlayerIndex);

            var bot = new SimpleChestsBot(new Random(seed));
            var ask = bot.ChooseMove(game, 2)!;
            Assert.Equal(0, ask.TargetIndex);
            Assert.Equal(Rank.Queen, ask.Rank);
            Apply(game, 2, ask);

            var count = bot.ChooseMove(game, 2)!;
            Assert.Equal(3, count.Count);
            Apply(game, 2, count);
            Assert.Contains(Rank.Queen, game.Players[2].Chests);
        }
    }

    /// <summary>
    /// Бот не подсматривает. Две раздачи, где у бота одна и та же рука, а руки соперников
    /// поменяны местами, для него неразличимы: с одним и тем же кубиком он задаёт один и
    /// тот же вопрос.
    /// </summary>
    [Fact]
    public void Bot_DecidesOnlyFromWhatTheTableKnows()
    {
        var botHand = new[] { C(Rank.Nine, Suit.Clubs), C(Rank.Ten, Suit.Clubs), C(Rank.Jack, Suit.Clubs), C(Rank.Queen, Suit.Clubs) };
        var left = new[] { C(Rank.Nine, Suit.Diamonds), C(Rank.Nine, Suit.Hearts), C(Rank.Nine, Suit.Spades), C(Rank.Ten, Suit.Diamonds) };
        var right = new[] { C(Rank.Ten, Suit.Hearts), C(Rank.Ten, Suit.Spades), C(Rank.Jack, Suit.Diamonds), C(Rank.Jack, Suit.Hearts) };
        var rest = new[] { C(Rank.Jack, Suit.Spades), C(Rank.Queen, Suit.Diamonds), C(Rank.Queen, Suit.Hearts), C(Rank.Queen, Suit.Spades) };

        var names = new[] { "Бот", "Марат", "Данил", "Ельнур" };
        var first = ChestsGame.Create(names, BuildDeck(botHand, left, right, rest));
        var second = ChestsGame.Create(names, BuildDeck(botHand, rest, left, right));

        for (var seed = 0; seed < 40; seed++)
        {
            var one = new SimpleChestsBot(new Random(seed)).ChooseMove(first, 0)!;
            var two = new SimpleChestsBot(new Random(seed)).ChooseMove(second, 0)!;

            Assert.Equal(one.TargetIndex, two.TargetIndex);
            Assert.Equal(one.Rank, two.Rank);
        }
    }

    /// <summary>
    /// У Марата каре пришло на раздаче, и рука опустела — это видно всем, он сидит без
    /// карт. Бот не тратит на него вопрос.
    /// </summary>
    [Fact]
    public void Bot_DoesNotAskSomeoneWithAnEmptyHand()
    {
        var game = ChestsGame.Create(new[] { "Бот", "Марат", "Данил", "Ельнур" }, BuildDeck(
            new[] { C(Rank.Queen, Suit.Clubs), C(Rank.King, Suit.Clubs), C(Rank.Ace, Suit.Clubs), C(Rank.Ace, Suit.Diamonds) },
            new[] { C(Rank.Jack, Suit.Clubs), C(Rank.Jack, Suit.Diamonds), C(Rank.Jack, Suit.Hearts), C(Rank.Jack, Suit.Spades) },
            new[] { C(Rank.Queen, Suit.Diamonds), C(Rank.Queen, Suit.Hearts), C(Rank.King, Suit.Diamonds), C(Rank.Ace, Suit.Hearts) },
            new[] { C(Rank.Queen, Suit.Spades), C(Rank.King, Suit.Hearts), C(Rank.King, Suit.Spades), C(Rank.Ace, Suit.Spades) }));
        Assert.True(game.Players[1].HasEmptyHand);

        for (var seed = 0; seed < 60; seed++)
        {
            var move = new SimpleChestsBot(new Random(seed)).ChooseMove(game, 0)!;
            Assert.NotEqual(1, move.TargetIndex);
        }
    }

    /// <summary>Без подсказок бот спрашивает кого попало, а не всегда одного и того же.</summary>
    [Fact]
    public void Bot_SpreadsQuestionsWhenNothingIsKnown()
    {
        var game = ChestsGame.Create(new[] { "Бот", "Марат", "Данил", "Ельнур" }, Deck.Standard36(5));

        var targets = new HashSet<int>();
        for (var seed = 0; seed < 60; seed++)
        {
            var move = new SimpleChestsBot(new Random(seed)).ChooseMove(game, 0)!;
            targets.Add(move.TargetIndex);
        }

        Assert.Equal(3, targets.Count);
    }
}
