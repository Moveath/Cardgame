using CardGame.Bots;
using CardGame.Core.Cards;
using CardGame.Core.Games.Govno;

namespace CardGame.Core.Tests;

public class GovnoGameTests
{
    private static GovnoGame Game(int count = 4, int seed = 42, bool chaos = false) =>
        new(Enumerable.Range(0, count).Select(i => "Игрок " + i).ToArray(), seed, chaos);

    private static GovnoGame WithQuad()
    {
        for (var seed = 0; seed < 10000; seed++)
        {
            var game = Game(4, seed);
            if (game.Players.Any(p => p.HasFourOfAKind)) return game;
        }
        throw new Exception("No deterministic quad seed found.");
    }

    private static void MissTwice(GovnoGame game, int target)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            foreach (var id in game.ActivePlayers) if (id != target) Assert.True(game.TryPass(id, 0));
            game.AdvanceTo(game.Deadline);
            if (attempt == 0)
            {
                Assert.Equal(1, game.Players[target].Warnings);
                Assert.Equal(GovnoPhase.Exchanging, game.Phase);
                game.AdvanceTo(game.Now + 0.5);
                Assert.True(game.CompleteExchange());
            }
        }
    }

    [Theory]
    [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void DeckMatchesSeatsAndDealsEveryCard(int count)
    {
        var game = Game(count);
        var cards = game.Players.SelectMany(p => p.Hand).ToArray();
        Assert.Equal(count * 4, cards.Length);
        Assert.Equal(cards.Length, cards.Distinct().Count());
        Assert.Equal(count, cards.Select(c => c.Rank).Distinct().Count());
        Assert.Equal((Rank)((int)Rank.Ace - count + 1), cards.Min(c => c.Rank));
        Assert.All(cards.GroupBy(c => c.Rank), group => Assert.Equal(4, group.Count()));
        Assert.All(game.Players, p => { Assert.Equal(4, p.Hand.Count); Assert.Equal(4, p.Lives); });
    }

    [Theory]
    [InlineData(2)] [InlineData(7)]
    public void InvalidPlayerCountRejected(int count) => Assert.Throws<ArgumentException>(() => Game(count));

    [Fact]
    public void PassingRemovesOnlyOneCardAndKeepsIncomingHiddenUntilAllChoose()
    {
        var game = Game();
        var hands = game.Players.Select(p => p.Slots.ToArray()).ToArray();
        Assert.True(game.TryPass(0, 2));
        Assert.Equal(3, game.Players[0].Hand.Count);
        Assert.True(game.HasIncoming(1));
        Assert.Equal(hands[1], game.Players[1].Slots);
        Assert.False(game.CompleteExchange());
        Assert.False(game.TryPass(0, 1));
        Assert.False(game.TryPass(-1, 0));
        Assert.False(game.TryPass(1, 4));
        for (var i = 1; i < 4; i++) Assert.True(game.TryPass(i, 2));
        Assert.Equal(GovnoPhase.Exchanging, game.Phase);
        Assert.All(game.Players, p => Assert.Equal(3, p.Hand.Count));
        game.AdvanceTo(20); // Waiting for the presentation consumes none of the next choice time.
        Assert.True(game.CompleteExchange());
        Assert.Equal(20 + GovnoGame.PickSeconds, game.Deadline);
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(hands[(i + 3) % 4][2], game.Players[i].Slots[2]);
            foreach (var slot in new[] { 0, 1, 3 }) Assert.Equal(hands[i][slot], game.Players[i].Slots[slot]);
            Assert.Equal(4, game.Players[i].Hand.Count);
            Assert.False(game.Players[i].HasPassed);
        }
    }

    [Theory]
    [InlineData(false, false, 2.5)]
    [InlineData(false, true, 2.5)]
    [InlineData(true, false, 1.5)]
    [InlineData(true, true, 1.5)]
    public void ChoiceAllowsFullModeDurationBeforeIssuingTimeoutWarning(bool chaos, bool afterExchange, double pickSeconds)
    {
        var game = Game(chaos: chaos);
        if (afterExchange)
        {
            foreach (var id in game.ActivePlayers) Assert.True(game.TryPass(id, 0));
            game.AdvanceTo(20); // A presentation pause must not use up the next choice window.
            Assert.True(game.CompleteExchange());
        }

        var startedAt = game.Now;
        Assert.Equal(pickSeconds, game.PickDurationSeconds);
        Assert.Equal(startedAt + pickSeconds, game.Deadline);
        foreach (var id in game.ActivePlayers) if (id != 0) Assert.True(game.TryPass(id, 0));

        game.AdvanceTo(startedAt + pickSeconds - 0.001);
        Assert.Equal(GovnoPhase.Picking, game.Phase);
        Assert.Equal(4, game.Players[0].Hand.Count);
        Assert.Equal(0, game.Players[0].Warnings);
        Assert.False(game.Players[0].HasPassed);

        game.AdvanceTo(startedAt + pickSeconds);
        Assert.Equal(GovnoPhase.Exchanging, game.Phase);
        Assert.Equal(1, game.Players[0].Warnings);
        Assert.Equal(3, game.Players[0].Hand.Count);
        Assert.True(game.Players[0].HasPassed);
        Assert.All(game.Players, player => Assert.Equal(4, player.Lives));
    }

    [Theory]
    [InlineData(false, 2.5)]
    [InlineData(true, 1.5)]
    public void FirstTimeoutPassesRandomCardAndSecondEndsRoundWithSingleLoss(bool chaos, double pickSeconds)
    {
        var game = Game(chaos: chaos);
        MissTwice(game, 0);
        Assert.Equal(GovnoPhase.RoundOver, game.Phase);
        Assert.Equal(GovnoLossReason.SecondTimeout, game.LossReason);
        Assert.Equal(3, game.Players[0].Lives);
        Assert.All(game.Players.Skip(1), p => Assert.Equal(4, p.Lives));
        Assert.All(game.Players, p => Assert.Equal(0, p.Warnings));
        Assert.False(game.TrySlap(0));
        game.AdvanceTo(100);
        Assert.Equal(3, game.Players[0].Lives);
        Assert.True(game.StartNextRound());
        Assert.Equal(GovnoPhase.Picking, game.Phase);
        Assert.Equal(100 + pickSeconds, game.Deadline);
        Assert.Equal(pickSeconds, game.PickDurationSeconds);
        Assert.Equal(3, game.Players[0].Lives);
        Assert.All(game.Players, p => Assert.Equal(4, p.Hand.Count));
    }

    [Theory]
    [InlineData(1.5)]
    [InlineData(2.5)]
    public void BotDecisionsFitModeDeadlineExceptForOccasionalIntentionalTimeouts(double pickSeconds)
    {
        var bot = new SimpleGovnoBot(42);
        var delays = Enumerable.Range(0, 1000).Select(_ => bot.PickDelay(pickSeconds)).ToArray();
        var late = delays.Where(delay => delay >= pickSeconds).ToArray();
        Assert.InRange(late.Length, 1, 200);
        Assert.All(late, delay => Assert.Equal(pickSeconds + 0.2, delay));
        Assert.All(delays.Where(delay => delay < pickSeconds), delay => Assert.True(delay > 0));
    }

    [Fact]
    public void BotsHaveDifferentReactionPacesAndVaryTheirSlaps()
    {
        var samples = Enumerable.Range(0, 32).Select(seed =>
        {
            var bot = new SimpleGovnoBot(seed);
            return Enumerable.Range(0, 256).Select(_ => bot.ReactionDelay()).ToArray();
        }).ToArray();

        Assert.All(samples, delays =>
        {
            Assert.All(delays, delay => Assert.InRange(delay, 0.22, 1.65));
            Assert.True(delays.Max() - delays.Min() > 0.5, "Each bot must vary its reaction between slaps.");
        });
        var averages = samples.Select(delays => delays.Average()).ToArray();
        Assert.True(averages.Max() - averages.Min() > 0.3, "Bots must retain different reaction paces across rounds.");
        Assert.Contains(samples.SelectMany(delays => delays), delay => delay < 0.3);
        Assert.Contains(samples.SelectMany(delays => delays), delay => delay > 1.5);
    }

    [Fact]
    public void EliminationRebuildsRanksAndSkipsTheEmptySeatWithoutResettingLives()
    {
        var game = Game();
        MissTwice(game, 0); game.StartNextRound();
        for (var i = 0; i < 4; i++)
        {
            MissTwice(game, 1);
            Assert.True(game.StartNextRound());
        }
        Assert.Equal(new[] { 0, 2, 3 }, game.ActivePlayers);
        Assert.Equal(2, game.LeftOf(0));
        Assert.Equal(0, game.RightOf(2));
        Assert.Equal(-1, game.LeftOf(1));
        Assert.Empty(game.Players[1].Hand);
        Assert.Equal(3, game.Players[0].Lives);
        Assert.Equal(12, game.Players.Sum(p => p.Hand.Count));
        Assert.DoesNotContain(game.Players.SelectMany(p => p.Hand), c => c.Rank == Rank.Jack);
        Assert.False(game.TryPass(1, 0));
        Assert.False(game.TryCall(1));
    }

    [Fact]
    public void TwoSurvivorsBothWinWithDifferentLifeCounts()
    {
        var game = Game(3);
        MissTwice(game, 0); game.StartNextRound();
        for (var i = 0; i < 4; i++)
        {
            MissTwice(game, 1);
            if (i < 3) game.StartNextRound();
        }
        Assert.True(game.IsFinished);
        Assert.Equal(new[] { 0, 2 }, game.Winners);
        Assert.Equal(3, game.Players[0].Lives);
        Assert.Equal(4, game.Players[2].Lives);
        Assert.False(game.StartNextRound());
    }

    [Fact]
    public void QuadMustBeCalledManuallyAndCallerIsFirstHand()
    {
        var game = WithQuad();
        var caller = game.Players.First(p => p.HasFourOfAKind).Index;
        game.AdvanceTo(0.2);
        Assert.Equal(GovnoPhase.Picking, game.Phase);
        Assert.True(game.TryCall(caller));
        Assert.Equal(new[] { caller }, game.HandOrder);
        Assert.False(game.TrySlap(caller));
        var others = game.ActivePlayers.Where(i => i != caller).ToArray();
        Assert.True(game.TrySlap(others[0]));
        Assert.False(game.TrySlap(others[0]));
        Assert.True(game.TrySlap(others[1]));
        Assert.Equal(GovnoPhase.Reacting, game.Phase);
        Assert.Equal(new[] { caller, others[0], others[1] }, game.HandOrder);
        Assert.All(game.Players, player => Assert.Equal(4, player.Lives));
        Assert.Equal(-1, game.LastLoser);
        game.AdvanceTo(0.8);
        Assert.True(game.TrySlap(others[2]));
        Assert.Equal(GovnoPhase.RoundOver, game.Phase);
        Assert.Equal(new[] { caller, others[0], others[1], others[2] }, game.HandOrder);
        Assert.Equal(others[2], game.LastLoser);
        Assert.Equal(GovnoLossReason.LastHand, game.LossReason);
        Assert.Equal(3, game.Players[others[2]].Lives);
        Assert.Equal(4, game.Players[caller].Lives);
        Assert.False(game.TrySlap(others[2]));
        Assert.Equal(3, game.Players[others[2]].Lives);
    }

    [Fact]
    public void PassingOneCardDestroysAnUnannouncedQuadAndFalseCallLocksOneSecond()
    {
        var game = WithQuad();
        var caller = game.Players.First(p => p.HasFourOfAKind).Index;
        Assert.True(game.TryPass(caller, 0));
        Assert.False(game.TryCall(caller));
        Assert.Equal(GovnoPhase.Picking, game.Phase);
        Assert.Equal(1, game.Players[caller].BlockedUntil);
        Assert.Empty(game.HandOrder);
        game.AdvanceTo(0.999);
        Assert.False(game.TryCall(caller));
        Assert.Equal(1, game.Players[caller].BlockedUntil);
        game.AdvanceTo(1);
        Assert.False(game.TryCall(caller));
        Assert.Equal(2, game.Players[caller].BlockedUntil);
        Assert.Equal(GovnoPhase.Picking, game.Phase);
        Assert.All(game.Players, player => Assert.Equal(4, player.Lives));
    }

    [Fact]
    public void FalseStartCarriesIntoReactionButDoesNotBlockCards()
    {
        var game = WithQuad();
        var caller = game.Players.First(p => p.HasFourOfAKind).Index;
        var other = game.ActivePlayers.First(i => i != caller);
        game.FalseStart(other);
        Assert.Equal(1, game.Players[other].BlockedUntil);
        Assert.True(game.TryPass(other, 0));
        game.AdvanceTo(0.2);
        Assert.True(game.TryCall(caller));
        Assert.False(game.TrySlap(other));
        game.AdvanceTo(0.999);
        Assert.False(game.TrySlap(other));
        game.AdvanceTo(1);
        Assert.True(game.TrySlap(other));
    }

    [Fact]
    public void MissesDuringLiveReactionNeverAddOrExtendFalseStartLock()
    {
        var game = WithQuad();
        var caller = game.Players.First(p => p.HasFourOfAKind).Index;
        var others = game.ActivePlayers.Where(i => i != caller).ToArray();
        game.FalseStart(others[0]);
        game.AdvanceTo(0.2);
        Assert.True(game.TryCall(caller));
        var revision = game.Revision;
        game.FalseStart(others[0]);
        game.FalseStart(others[1]);
        Assert.Equal(revision, game.Revision);
        Assert.Equal(1, game.Players[others[0]].BlockedUntil);
        Assert.Equal(0, game.Players[others[1]].BlockedUntil);
        Assert.True(game.TrySlap(others[1]));
        game.AdvanceTo(1);
        Assert.True(game.TrySlap(others[0]));
    }

    [Fact]
    public void EarlySlapDuringExchangeCarriesIntoNewHandWithoutBlockingCardPass()
    {
        var game = Game();
        foreach (var id in game.ActivePlayers) Assert.True(game.TryPass(id, 0));
        Assert.Equal(GovnoPhase.Exchanging, game.Phase);
        game.AdvanceTo(0.1);
        game.FalseStart(0);
        Assert.Equal(1.1, game.Players[0].BlockedUntil);
        game.AdvanceTo(0.2);
        Assert.True(game.CompleteExchange());
        Assert.Equal(1.1, game.Players[0].BlockedUntil);
        Assert.True(game.TryPass(0, 0));
    }

    [Fact]
    public void EarlySlapDuringCountdownIsBlockedForOneSecond()
    {
        var game = Game(3);
        game.AdvanceTo(game.Deadline);
        Assert.True(game.CompleteExchange());
        game.AdvanceTo(game.Deadline);
        Assert.Equal(GovnoPhase.ReactionCountdown, game.Phase);
        game.AdvanceTo(game.Deadline - 0.1);
        game.FalseStart(0);
        var unblockAt = game.Now + 1;
        Assert.Equal(unblockAt, game.Players[0].BlockedUntil);
        game.AdvanceTo(game.Deadline);
        Assert.Equal(GovnoPhase.Reacting, game.Phase);
        Assert.False(game.TrySlap(0));
        game.AdvanceTo(unblockAt - 0.001);
        Assert.False(game.TrySlap(0));
        game.AdvanceTo(unblockAt);
        Assert.True(game.TrySlap(0));
    }

    [Fact]
    public void LastAbsentPlayerLosesOnlyAtReactionDeadlineWithoutInventingTheirHand()
    {
        var game = WithQuad();
        var caller = game.Players.First(p => p.HasFourOfAKind).Index;
        Assert.True(game.TryCall(caller));
        var others = game.ActivePlayers.Where(i => i != caller).ToArray();
        Assert.True(game.TrySlap(others[0]));
        Assert.True(game.TrySlap(others[1]));
        game.AdvanceTo(game.Deadline - 0.001);
        Assert.Equal(GovnoPhase.Reacting, game.Phase);
        Assert.Equal(4, game.Players[others[2]].Lives);
        game.AdvanceTo(game.Deadline);
        Assert.Equal(GovnoPhase.RoundOver, game.Phase);
        Assert.Equal(others[2], game.LastLoser);
        Assert.Equal(GovnoLossReason.NoReaction, game.LossReason);
        Assert.Equal(3, game.Players[others[2]].Lives);
        Assert.Equal(new[] { caller, others[0], others[1] }, game.HandOrder);
        Assert.False(game.TrySlap(others[2]));
    }

    [Fact]
    public void ReactionRetryKeepsRecordedHandsAndWaitsForTheFinalNewTap()
    {
        var game = WithQuad();
        var caller = game.Players.First(p => p.HasFourOfAKind).Index;
        Assert.True(game.TryCall(caller));
        var others = game.ActivePlayers.Where(i => i != caller).ToArray();
        Assert.True(game.TrySlap(others[0]));
        game.AdvanceTo(game.Deadline);
        Assert.Equal(GovnoPhase.ReactionCountdown, game.Phase);
        Assert.Equal(new[] { others[1], others[2] }, game.ReactionPlayers);
        Assert.Equal(new[] { caller, others[0] }, game.HandOrder);
        game.AdvanceTo(game.Deadline);
        Assert.True(game.TrySlap(others[2]));
        Assert.Equal(GovnoPhase.Reacting, game.Phase);
        Assert.Equal(4, game.Players[others[1]].Lives);
        Assert.True(game.TrySlap(others[1]));
        Assert.Equal(GovnoPhase.RoundOver, game.Phase);
        Assert.Equal(others[1], game.LastLoser);
        Assert.Equal(new[] { caller, others[0], others[2], others[1] }, game.HandOrder);
    }

    [Fact]
    public void SimultaneousSecondTimeoutsResolveOneLoserInsteadOfEliminatingSeveral()
    {
        var game = Game(3);
        game.AdvanceTo(game.Deadline); game.CompleteExchange();
        game.AdvanceTo(game.Deadline);
        Assert.Equal(GovnoPhase.ReactionCountdown, game.Phase);
        game.AdvanceTo(game.Deadline);
        Assert.Equal(GovnoPhase.Reacting, game.Phase);
        Assert.True(game.IsPenaltyReaction);
        Assert.True(game.TrySlap(0));
        Assert.True(game.TrySlap(1));
        Assert.Equal(GovnoPhase.Reacting, game.Phase);
        Assert.Equal(12, game.Players.Sum(p => p.Lives));
        Assert.True(game.TrySlap(2));
        Assert.Equal(new[] { 0, 1, 2 }, game.HandOrder);
        Assert.Equal(2, game.LastLoser);
        Assert.Equal(GovnoLossReason.SecondTimeout, game.LossReason);
        Assert.Equal(11, game.Players.Sum(p => p.Lives));
    }

    [Fact]
    public void AbsentPlayersCannotStallOrRemoveBothWinners()
    {
        var game = Game(3);
        for (var step = 0; step < 300 && !game.IsFinished; step++)
        {
            if (game.Phase == GovnoPhase.RoundOver) game.StartNextRound();
            else if (game.Phase == GovnoPhase.Exchanging) game.CompleteExchange();
            else game.AdvanceTo(game.Deadline);
        }
        Assert.True(game.IsFinished);
        Assert.Equal(2, game.Winners.Count);
    }

    [Theory]
    [InlineData(3, false)] [InlineData(4, false)] [InlineData(5, false)] [InlineData(6, false)]
    [InlineData(3, true)] [InlineData(4, true)] [InlineData(5, true)] [InlineData(6, true)]
    public void BotsCompleteMatchesAndEveryExchangeConservesTheDeck(int count, bool chaos)
    {
        for (var seed = 0; seed < 12; seed++)
        {
            var game = Game(count, seed, chaos);
            var bots = Enumerable.Range(0, count).Select(i => new SimpleGovnoBot(seed * 100 + i)).ToArray();
            var shuffle = new Random(seed);
            for (var step = 0; step < 20000 && !game.IsFinished; step++)
            {
                if (game.Phase == GovnoPhase.Picking)
                {
                    foreach (var id in game.ActivePlayers.OrderBy(_ => shuffle.Next()).ToArray())
                    {
                        if (game.Phase != GovnoPhase.Picking) break;
                        if (game.Players[id].HasFourOfAKind) game.TryCall(id);
                        else game.TryPass(id, bots[id].ChooseSlot(game.Players[id]));
                    }
                }
                else if (game.Phase == GovnoPhase.Exchanging)
                {
                    game.CompleteExchange();
                    var cards = game.ActivePlayers.SelectMany(i => game.Players[i].Hand).ToArray();
                    Assert.Equal(game.ActivePlayers.Count * 4, cards.Length);
                    Assert.Equal(cards.Length, cards.Distinct().Count());
                }
                else if (game.Phase == GovnoPhase.Reacting)
                {
                    foreach (var id in game.ReactionPlayers.OrderBy(_ => shuffle.Next()).ToArray()) game.TrySlap(id);
                }
                else if (game.Phase == GovnoPhase.RoundOver) game.StartNextRound();
                else game.AdvanceTo(game.Deadline);
            }
            Assert.True(game.IsFinished, $"Stalled: {count} players, seed {seed}, chaos {chaos}");
            Assert.Equal(2, game.Winners.Count);
            Assert.All(game.Winners, i => Assert.InRange(game.Players[i].Lives, 1, 4));
        }
    }

    [Fact]
    public void BackwardAndInvalidTimeRejected()
    {
        var game = Game(); game.AdvanceTo(1);
        Assert.Throws<ArgumentOutOfRangeException>(() => game.AdvanceTo(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => game.AdvanceTo(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => game.AdvanceTo(double.PositiveInfinity));
    }
}
