using System.Collections.Generic;
using System.Linq;
using CardGame.Bots;
using CardGame.Core.Cards;
using CardGame.Core.Games.Durak;
using Xunit;

namespace CardGame.Core.Tests;

public class DurakGameTests
{
    private static readonly List<string> ThreePlayers = new() { "A", "B", "C" };

    [Fact]
    public void Create_DealsSixCardsToEveryone()
    {
        var game = DurakGame.Create(ThreePlayers, 1);

        Assert.All(game.Players, player => Assert.Equal(6, player.Hand.Count));
        Assert.Equal(36 - 18, game.DeckCount);
    }

    [Fact]
    public void Create_TrumpMatchesBottomCard()
    {
        var game = DurakGame.Create(ThreePlayers, 5);

        Assert.Equal(game.TrumpCard.Suit, game.Trump);
    }

    [Fact]
    public void FirstAttacker_HoldsLowestTrump()
    {
        var game = DurakGame.Create(ThreePlayers, 11);

        var lowestTrumpHolder = game.Players
            .Where(p => p.Hand.Any(c => c.Suit == game.Trump))
            .OrderBy(p => p.Hand.Where(c => c.Suit == game.Trump).Min(c => (int)c.Rank))
            .FirstOrDefault();

        if (lowestTrumpHolder != null)
        {
            Assert.Equal(lowestTrumpHolder.Index, game.AttackerIndex);
        }
    }

    [Fact]
    public void Defender_CannotAttack()
    {
        var game = DurakGame.Create(ThreePlayers, 3);
        var card = game.Defender.Hand[0];

        var applied = game.TryApplyMove(game.DefenderIndex, DurakMove.Attack(card), out var error);

        Assert.False(applied);
        Assert.NotNull(error);
    }

    [Fact]
    public void Attacker_CannotPassBeforeFirstCard()
    {
        var game = DurakGame.Create(ThreePlayers, 3);

        var applied = game.TryApplyMove(game.AttackerIndex, DurakMove.Pass(), out var error);

        Assert.False(applied);
        Assert.NotNull(error);
    }

    [Fact]
    public void CannotPlayCardThatIsNotInHand()
    {
        var game = DurakGame.Create(ThreePlayers, 3);
        var missing = game.Attacker.Hand.Contains(new Card(Rank.Six, Suit.Clubs))
            ? new Card(Rank.Ace, Suit.Spades)
            : new Card(Rank.Six, Suit.Clubs);

        if (game.Attacker.Hand.Contains(missing))
        {
            return;
        }

        var applied = game.TryApplyMove(game.AttackerIndex, DurakMove.Attack(missing), out _);

        Assert.False(applied);
    }

    [Fact]
    public void SecondAttackCard_MustMatchRankOnTable()
    {
        var game = DurakGame.Create(ThreePlayers, 9);
        var attacker = game.AttackerIndex;
        var firstCard = game.Attacker.Hand[0];

        Assert.True(game.TryApplyMove(attacker, DurakMove.Attack(firstCard), out _));

        var mismatched = game.Players[attacker].Hand.FirstOrDefault(c => c.Rank != firstCard.Rank);
        if (mismatched == default)
        {
            return;
        }

        Assert.False(game.TryApplyMove(attacker, DurakMove.Attack(mismatched), out _));
    }

    [Fact]
    public void PassedAttacker_CannotAttackAgainUntilNewCardAppears()
    {
        var game = DurakGame.Create(ThreePlayers, 777);
        var attacker = game.AttackerIndex;

        Assert.True(game.TryApplyMove(attacker, DurakMove.Attack(game.Attacker.Hand[0]), out _));

        var defenderMove = game.GetLegalMoves(game.DefenderIndex).First(m => m.Type == DurakMoveType.Defend);
        Assert.True(game.TryApplyMove(game.DefenderIndex, defenderMove, out _));

        if (game.IsFinished || game.Table.Count == 0)
        {
            return;
        }

        Assert.True(game.TryApplyMove(attacker, DurakMove.Pass(), out _));

        if (game.Table.Count == 0)
        {
            return;
        }

        Assert.DoesNotContain(game.GetLegalMoves(attacker), m => m.Type == DurakMoveType.Attack);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    public void FullBotGame_FinishesWithoutDeadlock(int playerCount)
    {
        var names = Enumerable.Range(0, playerCount).Select(i => "P" + i).ToList();

        for (var seed = 0; seed < 100; seed++)
        {
            var game = DurakGame.Create(names, seed);
            PlayToCompletion(game);

            Assert.True(game.IsFinished, $"Партия не завершилась: seed {seed}, игроков {playerCount}");
        }
    }

    [Fact]
    public void CardsAreNeverLostOrDuplicated()
    {
        for (var seed = 0; seed < 50; seed++)
        {
            var game = DurakGame.Create(ThreePlayers, seed);
            PlayToCompletion(game, assertInvariant: true);
        }
    }

    [Fact]
    public void FinishedGame_RejectsFurtherMoves()
    {
        var game = DurakGame.Create(ThreePlayers, 17);
        PlayToCompletion(game);

        Assert.True(game.IsFinished);
        Assert.False(game.TryApplyMove(0, DurakMove.Pass(), out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void LoserIsTheOnlyPlayerLeftWithCards()
    {
        for (var seed = 0; seed < 30; seed++)
        {
            var game = DurakGame.Create(ThreePlayers, seed);
            PlayToCompletion(game);

            if (game.LoserIndex.HasValue)
            {
                Assert.True(game.Players[game.LoserIndex.Value].Hand.Count > 0);
                Assert.Single(game.Players, p => !p.IsOut);
            }
        }
    }

    private static void PlayToCompletion(DurakGame game, bool assertInvariant = false)
    {
        var bot = new SimpleDurakBot();
        var guard = 5000;

        while (!game.IsFinished && guard-- > 0)
        {
            if (assertInvariant)
            {
                Assert.Equal(36, CountAllCards(game));
            }

            var actor = DurakTurnOrder.GetActingPlayer(game);
            Assert.NotNull(actor);

            var move = bot.ChooseMove(game, actor!.Value);
            Assert.NotNull(move);

            Assert.True(game.TryApplyMove(actor.Value, move!.Value, out var error), error);
        }

        Assert.True(guard > 0, "Партия зациклилась.");
    }

    private static int CountAllCards(DurakGame game)
    {
        var onTable = game.Table.Sum(slot => slot.IsDefended ? 2 : 1);
        var inHands = game.Players.Sum(p => p.Hand.Count);
        return onTable + inHands + game.DeckCount + game.DiscardCount;
    }
}
