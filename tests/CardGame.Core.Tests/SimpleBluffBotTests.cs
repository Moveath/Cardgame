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
    /// своей руке плюс три заявленные в колоду не помещаются, значит это ложь. Замечает
    /// бот её не каждый раз — идеальный арифметик за столом убивает игру, — но заметно
    /// чаще, чем просто подозревает.
    /// </summary>
    [Fact]
    public void Bot_CatchesAnImpossibleClaimAlmostAlways()
    {
        var game = BluffGame.Create(new[] { "Врун", "Бот" }, BuildDeck(
            new[] { C(Rank.Six, Suit.Spades), C(Rank.King, Suit.Hearts), C(Rank.Queen, Suit.Clubs) },
            new[] { C(Rank.Six, Suit.Hearts), C(Rank.Six, Suit.Diamonds), C(Rank.Six, Suit.Clubs) }));

        Assert.True(game.TryApplyMove(0, BluffMove.Declare(Rank.Six,
            C(Rank.Six, Suit.Spades), C(Rank.King, Suit.Hearts), C(Rank.Queen, Suit.Clubs)), out var error), error);

        var caught = 0;
        for (var seed = 0; seed < 200; seed++)
        {
            var bot = new SimpleBluffBot(new Random(seed));
            if (bot.ShouldChallenge(game, 1))
            {
                caught++;
            }
        }

        Assert.InRange(caught, 150, 200);
    }

    /// <summary>
    /// Обратная сторона: скромную заявку, которую нечем опровергнуть, бот в основном
    /// пропускает. Иначе с руки не уходит ни один туз и играть не во что.
    /// </summary>
    [Fact]
    public void Bot_UsuallyBelievesAModestClaim()
    {
        var game = BluffGame.Create(new[] { "Игрок", "Бот" }, BuildDeck(
            new[] { C(Rank.Ace, Suit.Spades), C(Rank.King, Suit.Hearts), C(Rank.Queen, Suit.Clubs) },
            new[] { C(Rank.Nine, Suit.Hearts), C(Rank.Eight, Suit.Diamonds), C(Rank.Seven, Suit.Clubs) }));

        // Кладём туза под видом шестёрок: у бота шестёрок нет, опровергнуть нечем.
        Assert.True(game.TryApplyMove(0, BluffMove.Declare(Rank.Six,
            C(Rank.Ace, Suit.Spades)), out var error), error);

        var challenged = 0;
        for (var seed = 0; seed < 200; seed++)
        {
            var bot = new SimpleBluffBot(new Random(seed));
            if (bot.ShouldChallenge(game, 1))
            {
                challenged++;
            }
        }

        Assert.InRange(challenged, 1, 50);
    }

    /// <summary>
    /// Кого прижимать, бот выбирает по положению за столом. Вскрыть того, кто вот-вот
    /// выйдет, — это отбросить его назад. Отстающему лишняя пачка карт почти ничего не
    /// меняет, и тратить на него проверку незачем, даже поймав на вранье.
    /// </summary>
    [Fact]
    public void Bot_ChallengesTheLeaderMuchMoreOftenThanTheLaggard()
    {
        var leaderGame = BluffGame.Create(new[] { "Лидер", "Бот" }, BuildDeck(
            new[] { C(Rank.Ace, Suit.Spades), C(Rank.Ace, Suit.Hearts) },
            new[] { C(Rank.Nine, Suit.Hearts), C(Rank.Eight, Suit.Diamonds) }));

        // Скидывает последние две карты под видом шестёрок и выходит.
        Assert.True(leaderGame.TryApplyMove(0, BluffMove.Declare(Rank.Six,
            C(Rank.Ace, Suit.Spades), C(Rank.Ace, Suit.Hearts)), out var leaderError), leaderError);

        var laggardGame = BluffGame.Create(new[] { "Отстающий", "Бот" }, BuildDeck(
            new[] { C(Rank.Ace, Suit.Spades), C(Rank.King, Suit.Hearts), C(Rank.Queen, Suit.Diamonds), C(Rank.Jack, Suit.Clubs) },
            new[] { C(Rank.Nine, Suit.Hearts), C(Rank.Eight, Suit.Diamonds), C(Rank.Seven, Suit.Clubs), C(Rank.Six, Suit.Spades) }));

        // Такая же ложь, но карт у него полная рука.
        Assert.True(laggardGame.TryApplyMove(0, BluffMove.Declare(Rank.Ten,
            C(Rank.Ace, Suit.Spades)), out var laggardError), laggardError);

        var againstLeader = 0;
        var againstLaggard = 0;

        for (var seed = 0; seed < 300; seed++)
        {
            if (new SimpleBluffBot(new Random(seed)).ShouldChallenge(leaderGame, 1))
            {
                againstLeader++;
            }

            if (new SimpleBluffBot(new Random(seed)).ShouldChallenge(laggardGame, 1))
            {
                againstLaggard++;
            }
        }

        Assert.True(againstLeader > againstLaggard * 3,
            "лидера вскрывают " + againstLeader + ", отстающего " + againstLaggard);
    }

    /// <summary>
    /// Настоящая карта круга — это право ответить честно, когда очередь вернётся. Поэтому
    /// бот не обязан выкладывать её сразу: иногда врёт сейчас, а её придерживает.
    /// Проверяем, что встречается и то и другое.
    /// </summary>
    [Fact]
    public void Bot_SometimesKeepsItsOnlyHonestCardAndLiesInstead()
    {
        var game = BluffGame.Create(new[] { "Сосед", "Бот" }, BuildDeck(
            new[] { C(Rank.Seven, Suit.Clubs), C(Rank.Seven, Suit.Diamonds), C(Rank.Seven, Suit.Hearts) },
            new[] { C(Rank.Six, Suit.Spades), C(Rank.King, Suit.Hearts), C(Rank.Queen, Suit.Diamonds) }));

        Assert.True(game.TryApplyMove(0, BluffMove.Declare(Rank.Six,
            C(Rank.Seven, Suit.Clubs)), out var error), error);

        var playedHonest = 0;
        var keptItBack = 0;

        for (var seed = 0; seed < 300; seed++)
        {
            var move = new SimpleBluffBot(new Random(seed)).ChooseMove(game, 1);
            if (move == null || move.Type != BluffMoveType.Follow)
            {
                continue;
            }

            var hasSix = false;
            foreach (var card in move.Cards)
            {
                if (card.Rank == Rank.Six)
                {
                    hasSix = true;
                }
            }

            if (hasSix)
            {
                playedHonest++;
            }
            else
            {
                keptItBack++;
            }
        }

        Assert.True(playedHonest > 30, "честно кладёт всего " + playedHonest);
        Assert.True(keptItBack > 30, "придерживает всего " + keptItBack);
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
