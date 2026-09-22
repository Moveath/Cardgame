using CardGame.Core.Cards;
using CardGame.Core.Games.Bluff;
using Xunit;

namespace CardGame.Core.Tests;

public class BluffGameTests
{
    private static readonly string[] Three = { "Егор", "Алишер", "Данил" };
    private static readonly string[] Two = { "Егор", "Алишер" };

    /// <summary>
    /// Раздача идёт по кругу, поэтому карты в колоде должны лежать вперемешку по местам.
    /// Руки разной длины допустимы, но длинные обязаны стоять первыми — иначе лишняя карта
    /// уедет не тому игроку, и тест будет проверять не то, что задумано.
    /// </summary>
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

        for (var i = 1; i < hands.Length; i++)
        {
            Assert.True(hands[i].Length <= hands[i - 1].Length,
                "Руки в раздаче должны идти от длинной к короткой.");
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

    private static Card C(Rank rank, Suit suit) => new(rank, suit);

    private static void Apply(BluffGame game, int player, BluffMove move)
    {
        Assert.True(game.TryApplyMove(player, move, out var error), error);
    }

    // ---------- раздача ----------

    [Fact]
    public void Deal_SplitsTheWholeDeck()
    {
        var game = BluffGame.Create(Three, Deck.Standard36(7));

        var total = 0;
        foreach (var player in game.Players)
        {
            Assert.Equal(12, player.Hand.Count);
            total += player.Hand.Count;
        }

        Assert.Equal(36, total);
        Assert.Equal(BluffPhase.RoundStart, game.Phase);
        Assert.Equal(0, game.CurrentPlayerIndex);
        Assert.Null(game.CurrentRank);
    }

    [Fact]
    public void Deal_WithLeftoverCards_GivesTheExtraToTheFirstSeats()
    {
        var game = BluffGame.Create(Three, Deck.Standard52(3));

        Assert.Equal(18, game.Players[0].Hand.Count);
        Assert.Equal(17, game.Players[1].Hand.Count);
        Assert.Equal(17, game.Players[2].Hand.Count);
    }

    // ---------- заявка ----------

    [Fact]
    public void Declare_MovesCardsToThePotAndPassesTheTurn()
    {
        var game = BluffGame.Create(Three, BuildDeck(
            new[] { C(Rank.Jack, Suit.Spades), C(Rank.Jack, Suit.Hearts), C(Rank.Seven, Suit.Clubs) },
            new[] { C(Rank.King, Suit.Spades), C(Rank.King, Suit.Hearts), C(Rank.King, Suit.Diamonds) },
            new[] { C(Rank.Nine, Suit.Spades), C(Rank.Nine, Suit.Hearts), C(Rank.Nine, Suit.Diamonds) }));

        Apply(game, 0, BluffMove.Declare(Rank.Jack, C(Rank.Jack, Suit.Spades), C(Rank.Jack, Suit.Hearts)));

        Assert.Single(game.Players[0].Hand);
        Assert.Equal(2, game.PotCount);
        Assert.Equal(2, game.DeclaredPotCount);
        Assert.Equal(Rank.Jack, game.CurrentRank);
        Assert.Equal(BluffPhase.Chain, game.Phase);
        Assert.Equal(1, game.CurrentPlayerIndex);
    }

    [Fact]
    public void Declare_WithAces_IsRejected()
    {
        var game = BluffGame.Create(Two, BuildDeck(
            new[] { C(Rank.Ace, Suit.Spades), C(Rank.Seven, Suit.Clubs) },
            new[] { C(Rank.Ace, Suit.Hearts), C(Rank.Eight, Suit.Clubs) }));

        Assert.False(game.TryApplyMove(0, BluffMove.Declare(Rank.Ace, C(Rank.Ace, Suit.Spades)), out var error));
        Assert.Contains("Тузами", error);
        Assert.Equal(BluffPhase.RoundStart, game.Phase);
    }

    [Fact]
    public void Ace_CanBeThrownAsALie()
    {
        var game = BluffGame.Create(Two, BuildDeck(
            new[] { C(Rank.Ace, Suit.Spades), C(Rank.Seven, Suit.Clubs) },
            new[] { C(Rank.Ace, Suit.Hearts), C(Rank.Eight, Suit.Clubs) }));

        Apply(game, 0, BluffMove.Declare(Rank.King, C(Rank.Ace, Suit.Spades)));

        Assert.Equal(0, game.Players[0].AceCount);
        Assert.True(game.LastPacket!.IsLie);
    }

    [Fact]
    public void Declare_MidChain_IsRejected_BecauseTheRankIsLocked()
    {
        var game = BluffGame.Create(Two, BuildDeck(
            new[] { C(Rank.Jack, Suit.Spades), C(Rank.Seven, Suit.Clubs) },
            new[] { C(Rank.King, Suit.Spades), C(Rank.Eight, Suit.Clubs) }));

        Apply(game, 0, BluffMove.Declare(Rank.Jack, C(Rank.Jack, Suit.Spades)));

        Assert.False(game.TryApplyMove(1, BluffMove.Declare(Rank.King, C(Rank.King, Suit.Spades)), out var error));
        Assert.Contains("менять", error);
    }

    [Fact]
    public void Follow_KeepsTheDeclaredRank()
    {
        var game = BluffGame.Create(Two, BuildDeck(
            new[] { C(Rank.Jack, Suit.Spades), C(Rank.Seven, Suit.Clubs) },
            new[] { C(Rank.King, Suit.Spades), C(Rank.Eight, Suit.Clubs) }));

        Apply(game, 0, BluffMove.Declare(Rank.Jack, C(Rank.Jack, Suit.Spades)));
        Apply(game, 1, BluffMove.Follow(C(Rank.King, Suit.Spades)));

        Assert.Equal(Rank.Jack, game.LastPacket!.DeclaredRank);
        Assert.True(game.LastPacket.IsLie);
        Assert.Equal(Rank.Jack, game.CurrentRank);
    }

    [Fact]
    public void Throw_OfCardsNotInHand_IsRejected()
    {
        var game = BluffGame.Create(Two, BuildDeck(
            new[] { C(Rank.Jack, Suit.Spades), C(Rank.Seven, Suit.Clubs) },
            new[] { C(Rank.King, Suit.Spades), C(Rank.Eight, Suit.Clubs) }));

        Assert.False(game.TryApplyMove(0, BluffMove.Declare(Rank.Jack, C(Rank.Jack, Suit.Hearts)), out var error));
        Assert.Contains("на руках нет", error);
    }

    // ---------- проверка ----------

    [Fact]
    public void Challenge_CatchesAWrongRank_AndTheLiarTakesThePot()
    {
        var game = BluffGame.Create(Three, BuildDeck(
            new[] { C(Rank.Jack, Suit.Spades), C(Rank.Jack, Suit.Hearts), C(Rank.Seven, Suit.Clubs) },
            new[] { C(Rank.King, Suit.Spades), C(Rank.King, Suit.Hearts), C(Rank.King, Suit.Diamonds) },
            new[] { C(Rank.Nine, Suit.Spades), C(Rank.Nine, Suit.Hearts), C(Rank.Nine, Suit.Diamonds) }));

        Apply(game, 0, BluffMove.Declare(Rank.Jack,
            C(Rank.Jack, Suit.Spades), C(Rank.Jack, Suit.Hearts), C(Rank.Seven, Suit.Clubs)));

        Assert.True(game.Players[0].IsOut);

        Apply(game, 1, BluffMove.Challenge());

        Assert.Equal(3, game.Players[0].Hand.Count);
        Assert.False(game.Players[0].IsOut);
        Assert.Empty(game.FinishOrder);
        Assert.Equal(0, game.PotCount);
        Assert.Equal(BluffPhase.RoundStart, game.Phase);
    }

    /// <summary>
    /// Количеством в этой игре не врут: пакет кладут на виду, и сколько карт легло —
    /// столько и объявлено. Врать можно только рангом.
    /// </summary>
    [Fact]
    public void DeclaredCount_AlwaysMatchesWhatWasThrown()
    {
        var game = BluffGame.Create(Three, BuildDeck(
            new[] { C(Rank.Jack, Suit.Spades), C(Rank.Jack, Suit.Hearts), C(Rank.Jack, Suit.Diamonds) },
            new[] { C(Rank.King, Suit.Spades), C(Rank.King, Suit.Hearts), C(Rank.King, Suit.Diamonds) },
            new[] { C(Rank.Nine, Suit.Spades), C(Rank.Nine, Suit.Hearts), C(Rank.Nine, Suit.Diamonds) }));

        Apply(game, 0, BluffMove.Declare(Rank.Jack,
            C(Rank.Jack, Suit.Spades), C(Rank.Jack, Suit.Hearts)));

        Assert.Equal(2, game.LastPacket!.DeclaredCount);
        Assert.Equal(2, game.DeclaredPotCount);
        Assert.False(game.LastPacket.IsLie);

        // И проверка такой бросок не наказывает: он честный.
        Apply(game, 1, BluffMove.Challenge());
        Assert.Equal(5, game.Players[1].Hand.Count);
    }

    [Fact]
    public void Challenge_OnAnHonestThrow_CostsTheChallengerThePot()
    {
        var game = BluffGame.Create(Three, BuildDeck(
            new[] { C(Rank.Jack, Suit.Spades), C(Rank.Jack, Suit.Hearts), C(Rank.Jack, Suit.Diamonds) },
            new[] { C(Rank.King, Suit.Spades), C(Rank.King, Suit.Hearts), C(Rank.King, Suit.Diamonds) },
            new[] { C(Rank.Nine, Suit.Spades), C(Rank.Nine, Suit.Hearts), C(Rank.Nine, Suit.Diamonds) }));

        Apply(game, 0, BluffMove.Declare(Rank.Jack,
            C(Rank.Jack, Suit.Spades), C(Rank.Jack, Suit.Hearts), C(Rank.Jack, Suit.Diamonds)));

        Apply(game, 1, BluffMove.Challenge());

        Assert.Equal(6, game.Players[1].Hand.Count);
        Assert.True(game.Players[0].IsOut);
        Assert.Equal(new[] { 0 }, game.FinishOrder);
    }

    [Fact]
    public void Challenge_MovesTheWholePot_ThoughOnlyTheLastPacketIsRevealed()
    {
        var game = BluffGame.Create(Three, BuildDeck(
            new[] { C(Rank.Jack, Suit.Spades), C(Rank.Jack, Suit.Hearts), C(Rank.Seven, Suit.Clubs) },
            new[] { C(Rank.King, Suit.Spades), C(Rank.King, Suit.Hearts), C(Rank.King, Suit.Diamonds) },
            new[] { C(Rank.Nine, Suit.Spades), C(Rank.Nine, Suit.Hearts), C(Rank.Nine, Suit.Diamonds) }));

        // Егор честен, Алишер врёт поверх — вскрывают только Алишера, но котёл уходит целиком.
        Apply(game, 0, BluffMove.Declare(Rank.Jack,
            C(Rank.Jack, Suit.Spades), C(Rank.Jack, Suit.Hearts)));
        Apply(game, 1, BluffMove.Follow(C(Rank.King, Suit.Spades), C(Rank.King, Suit.Hearts)));

        Apply(game, 2, BluffMove.Challenge());

        Assert.Equal(5, game.Players[1].Hand.Count);
        Assert.Equal(0, game.PotCount);

        var entry = game.Log[^1];
        Assert.Equal(BluffLogKind.Challenge, entry.Kind);
        Assert.Equal(2, entry.RevealedCards.Count);
        Assert.All(entry.RevealedCards, card => Assert.Equal(Rank.King, card.Rank));
    }

    [Fact]
    public void Challenge_IsOnlyForTheNextPlayer()
    {
        var game = BluffGame.Create(Three, BuildDeck(
            new[] { C(Rank.Jack, Suit.Spades), C(Rank.Jack, Suit.Hearts), C(Rank.Seven, Suit.Clubs) },
            new[] { C(Rank.King, Suit.Spades), C(Rank.King, Suit.Hearts), C(Rank.King, Suit.Diamonds) },
            new[] { C(Rank.Nine, Suit.Spades), C(Rank.Nine, Suit.Hearts), C(Rank.Nine, Suit.Diamonds) }));

        Apply(game, 0, BluffMove.Declare(Rank.Jack, C(Rank.Jack, Suit.Spades)));

        Assert.False(game.TryApplyMove(2, BluffMove.Challenge(), out var error));
        Assert.Contains("следующий по кругу", error);
    }

    [Fact]
    public void AfterAChallenge_TheNewRoundStartsWithThePlayerAfterTheTaker()
    {
        var game = BluffGame.Create(Three, BuildDeck(
            new[] { C(Rank.Jack, Suit.Spades), C(Rank.Jack, Suit.Hearts), C(Rank.Jack, Suit.Diamonds) },
            new[] { C(Rank.King, Suit.Spades), C(Rank.King, Suit.Hearts), C(Rank.King, Suit.Diamonds) },
            new[] { C(Rank.Nine, Suit.Spades), C(Rank.Nine, Suit.Hearts), C(Rank.Nine, Suit.Diamonds) }));

        // Егор честен -> котёл забирает проверяющий Алишер -> круг начинает Данил.
        Apply(game, 0, BluffMove.Declare(Rank.Jack, C(Rank.Jack, Suit.Spades)));
        Apply(game, 1, BluffMove.Challenge());

        Assert.Equal(2, game.RoundLeaderIndex);
        Assert.Equal(2, game.CurrentPlayerIndex);
        Assert.Null(game.CurrentRank);
    }

    // ---------- каре ----------

    [Fact]
    public void FourOfAKind_LeavesTheGameAndDoesNotPassTheTurn()
    {
        var game = BluffGame.Create(Two, BuildDeck(
            new[]
            {
                C(Rank.King, Suit.Spades), C(Rank.King, Suit.Hearts),
                C(Rank.King, Suit.Diamonds), C(Rank.King, Suit.Clubs), C(Rank.Seven, Suit.Spades)
            },
            new[]
            {
                C(Rank.Nine, Suit.Spades), C(Rank.Nine, Suit.Hearts),
                C(Rank.Nine, Suit.Diamonds), C(Rank.Eight, Suit.Clubs), C(Rank.Eight, Suit.Spades)
            }));

        Assert.Equal(new[] { Rank.King }, game.AvailableFours(0));

        Apply(game, 0, BluffMove.DiscardFour(Rank.King));

        Assert.Single(game.Players[0].Hand);
        Assert.Equal(4, game.RemovedCount);
        Assert.Equal(0, game.PotCount);
        Assert.Equal(0, game.CurrentPlayerIndex);
        Assert.Equal(BluffPhase.RoundStart, game.Phase);
    }

    [Fact]
    public void FourOfAKind_OfAces_IsRejected()
    {
        var game = BluffGame.Create(Two, BuildDeck(
            new[]
            {
                C(Rank.Ace, Suit.Spades), C(Rank.Ace, Suit.Hearts),
                C(Rank.Ace, Suit.Diamonds), C(Rank.Ace, Suit.Clubs), C(Rank.Seven, Suit.Spades)
            },
            new[]
            {
                C(Rank.Nine, Suit.Spades), C(Rank.Nine, Suit.Hearts),
                C(Rank.Nine, Suit.Diamonds), C(Rank.Eight, Suit.Clubs), C(Rank.Eight, Suit.Spades)
            }));

        Assert.Empty(game.AvailableFours(0));
        Assert.False(game.TryApplyMove(0, BluffMove.DiscardFour(Rank.Ace), out var error));
        Assert.Contains("тузов", error);
    }

    [Fact]
    public void FourOfAKind_CanBePlayedOutOfTurn()
    {
        var game = BluffGame.Create(Two, BuildDeck(
            new[]
            {
                C(Rank.Seven, Suit.Spades), C(Rank.Seven, Suit.Hearts),
                C(Rank.Six, Suit.Clubs), C(Rank.Six, Suit.Spades), C(Rank.Six, Suit.Hearts)
            },
            new[]
            {
                C(Rank.King, Suit.Spades), C(Rank.King, Suit.Hearts),
                C(Rank.King, Suit.Diamonds), C(Rank.King, Suit.Clubs), C(Rank.Eight, Suit.Spades)
            }));

        Apply(game, 0, BluffMove.Declare(Rank.Seven, C(Rank.Seven, Suit.Spades)));
        Assert.Equal(1, game.CurrentPlayerIndex);

        // Ходит Алишер, но каре выкладывает Егор — очередь от этого не двигается.
        Assert.Contains(BluffMoveType.DiscardFour, game.AvailableMoveTypes(1));
        Apply(game, 1, BluffMove.DiscardFour(Rank.King));

        Assert.Single(game.Players[1].Hand);
        Assert.Equal(1, game.CurrentPlayerIndex);
        Assert.Equal(BluffPhase.Chain, game.Phase);
    }

    // ---------- конец партии ----------

    [Fact]
    public void Game_EndsAtOnce_WhenEveryHandIsAcesOnly()
    {
        var game = BluffGame.Create(Two, BuildDeck(
            new[] { C(Rank.Ace, Suit.Spades), C(Rank.Ace, Suit.Hearts) },
            new[] { C(Rank.Ace, Suit.Diamonds) }));

        Assert.True(game.IsFinished);
        Assert.Equal(0, game.LoserIndex);
        Assert.Equal(new[] { 1, 0 }, game.Standings);
    }

    [Fact]
    public void EqualAces_MeanASharedLoss()
    {
        var game = BluffGame.Create(Two, BuildDeck(
            new[] { C(Rank.Ace, Suit.Spades), C(Rank.Ace, Suit.Hearts) },
            new[] { C(Rank.Ace, Suit.Diamonds), C(Rank.Ace, Suit.Clubs) }));

        Assert.True(game.IsFinished);
        Assert.Null(game.LoserIndex);
        Assert.Equal(new[] { 0, 1 }, game.Losers);
    }

    [Fact]
    public void Game_EndsWhenCardsAreLeftWithOnePlayer()
    {
        var game = BluffGame.Create(Two, BuildDeck(
            new[] { C(Rank.Eight, Suit.Clubs), C(Rank.Eight, Suit.Hearts) },
            new[] { C(Rank.Seven, Suit.Clubs) }));

        Apply(game, 0, BluffMove.Declare(Rank.Eight,
            C(Rank.Eight, Suit.Clubs), C(Rank.Eight, Suit.Hearts)));

        // Егор честен, значит котёл забирает Алишер — и остаётся единственным с картами.
        Apply(game, 1, BluffMove.Challenge());

        Assert.True(game.IsFinished);
        Assert.Equal(3, game.Players[1].Hand.Count);
        Assert.Equal(new[] { 0, 1 }, game.Standings);
        Assert.Equal(1, game.LoserIndex);
    }

    [Fact]
    public void Game_EndsWhenNobodyIsLeftToAnswer()
    {
        var game = BluffGame.Create(Two, BuildDeck(
            new[] { C(Rank.Eight, Suit.Clubs), C(Rank.Eight, Suit.Hearts) },
            new[] { C(Rank.Seven, Suit.Clubs) }));

        Apply(game, 0, BluffMove.Declare(Rank.Eight,
            C(Rank.Eight, Suit.Clubs), C(Rank.Eight, Suit.Hearts)));
        Apply(game, 1, BluffMove.Follow(C(Rank.Seven, Suit.Clubs)));

        Assert.True(game.IsFinished);
        Assert.Empty(game.Losers);
        Assert.Equal(new[] { 0, 1 }, game.Standings);
    }

    // ---------- скрытая информация ----------

    [Fact]
    public void Log_KeepsTheBluffSecretUntilTheReveal()
    {
        var game = BluffGame.Create(Three, BuildDeck(
            new[] { C(Rank.Jack, Suit.Spades), C(Rank.Ace, Suit.Hearts), C(Rank.Seven, Suit.Clubs) },
            new[] { C(Rank.King, Suit.Spades), C(Rank.King, Suit.Hearts), C(Rank.King, Suit.Diamonds) },
            new[] { C(Rank.Nine, Suit.Spades), C(Rank.Nine, Suit.Hearts), C(Rank.Nine, Suit.Diamonds) }));

        Apply(game, 0, BluffMove.Declare(Rank.Jack,
            C(Rank.Jack, Suit.Spades), C(Rank.Ace, Suit.Hearts)));

        var thrown = game.Log[^1];
        Assert.Equal(BluffLogKind.Throw, thrown.Kind);
        Assert.Empty(thrown.RevealedCards);
        Assert.Equal("Егор скинул 2 карты под видом валетов", thrown.Text);

        Apply(game, 1, BluffMove.Challenge());

        var revealed = game.Log[^1];
        Assert.Equal(2, revealed.RevealedCards.Count);
        Assert.True(revealed.WasLie);
    }

    // ---------- сходимость ----------

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    public void RandomGames_AlwaysFinish_AndNeverLoseACard(int playerCount)
    {
        var names = new List<string>();
        for (var i = 0; i < playerCount; i++)
        {
            names.Add("И" + i);
        }

        for (var seed = 0; seed < 40; seed++)
        {
            var game = BluffGame.Create(names, Deck.Standard36(seed));
            var random = new Random(seed * 31 + playerCount);

            var guard = 0;
            while (!game.IsFinished)
            {
                Assert.True(guard++ < 400, "Партия не сходится: seed " + seed);
                PlayRandomMove(game, random);
                AssertNoCardIsLost(game, 36);
            }

            AssertNoCardIsLost(game, 36);
            Assert.Equal(playerCount, game.Standings.Count);
        }
    }

    private static void PlayRandomMove(BluffGame game, Random random)
    {
        var actor = game.CurrentPlayerIndex;

        var fours = game.AvailableFours(actor);
        if (fours.Count > 0 && random.Next(4) == 0)
        {
            Assert.True(game.TryApplyMove(actor, BluffMove.DiscardFour(fours[0]), out var fourError), fourError);
            return;
        }

        if (game.Phase == BluffPhase.Chain && random.Next(2) == 0)
        {
            Assert.True(game.TryApplyMove(actor, BluffMove.Challenge(), out var challengeError), challengeError);
            return;
        }

        var hand = game.Players[actor].Hand;
        var take = random.Next(1, Math.Min(3, hand.Count) + 1);

        var cards = new List<Card>();
        var pool = new List<Card>(hand);
        for (var i = 0; i < take; i++)
        {
            var pick = random.Next(pool.Count);
            cards.Add(pool[pick]);
            pool.RemoveAt(pick);
        }

        if (game.Phase == BluffPhase.RoundStart)
        {
            // Заявлять тузами нельзя, поэтому берём ранг первой не-тузовой карты, а если
            // на руках одни тузы — врём про королей.
            var rank = Rank.King;
            foreach (var card in cards)
            {
                if (BluffRules.CanBeDeclared(card.Rank))
                {
                    rank = card.Rank;
                    break;
                }
            }

            Assert.True(game.TryApplyMove(actor, BluffMove.Declare(rank, cards), out var declareError),
                declareError);
            return;
        }

        Assert.True(game.TryApplyMove(actor, BluffMove.Follow(cards), out var followError), followError);
    }

    /// <summary>
    /// Карты не берутся из ниоткуда и не исчезают: всё, что было в колоде, лежит либо на
    /// руках, либо в котле, либо вышло из игры через каре.
    /// </summary>
    private static void AssertNoCardIsLost(BluffGame game, int deckSize)
    {
        var total = game.PotCount + game.RemovedCount;
        foreach (var player in game.Players)
        {
            total += player.Hand.Count;
        }

        Assert.Equal(deckSize, total);
    }
}
