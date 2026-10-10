using CardGame.Core.Meta;

namespace CardGame.Core.Tests;

public sealed partial class MetaHubTests
{
    [Fact]
    public async Task FortuneSixConcealedSlotsContainSixUniqueCardsAndPersistOneFreeDraw()
    {
        var effects = new HashSet<string>();
        for (var slot = 0; slot < 6; slot++)
        {
            var f = new Fixture(); var before = (await f.Service.LoadAsync()).Snapshot;
            var draw = await f.Ok("fortune_draw", amount: slot);
            Assert.True(effects.Add(draw.Fortune.EffectId));
            Assert.Equal(slot, draw.Fortune.SelectedCardIndex); Assert.Equal(1, draw.Fortune.DrawCount);
            Assert.Equal(f.Clock.Now, draw.Fortune.DrawnAt); Assert.Equal(f.Clock.Now + 86400, draw.Fortune.ReadyAt);
            Assert.Equal(HubFortunes.Find(draw.Fortune.EffectId)!.Matches, draw.Fortune.RemainingMatches);
            Assert.Equal(before.Profile.Coins, draw.Profile.Coins); Assert.Equal(before.Profile.XP, draw.Profile.XP);
            f.Service = new LocalPlayerHubService(f.Store, f.Clock);
            var loaded = (await f.Service.LoadAsync()).Snapshot;
            Assert.Equal(draw.Fortune.EffectId, loaded.Fortune.EffectId); Assert.Equal(slot, loaded.Fortune.SelectedCardIndex);
            Assert.False((await f.Service.ExecuteAsync(Cmd("fortune_draw", amount: (slot + 1) % 6))).Success);
        }
        Assert.Equal(HubFortunes.Definitions.Select(card => card.Id).Order(), effects.Order());
    }

    [Fact]
    public async Task FortuneDebugReadyAllowsTheNextDrawRightAway()
    {
        var f = new Fixture(); await f.Service.LoadAsync();
        await f.Ok("fortune_draw", amount: 1);
        Assert.False((await f.Service.ExecuteAsync(Cmd("fortune_draw", amount: 2))).Success);
        var ready = await f.Ok("fortune_debug_ready");
        Assert.True(HubFortunes.CanDraw(ready.Fortune, f.Clock.Now));
        var again = await f.Ok("fortune_draw", amount: 2);
        Assert.Equal(2, again.Fortune.DrawCount); Assert.Equal(2, again.Fortune.SelectedCardIndex);
    }

    [Fact]
    public async Task FortuneDrawIsAtomicIdempotentAndFailedPersistenceDoesNotRerollAcrossClockBoundary()
    {
        var f = new Fixture(); await f.Service.LoadAsync();
        var control = new Fixture(); control.Store.Json = f.Store.Json;
        var expected = await control.Ok("fortune_draw", amount: 3);
        var command = Cmd("fortune_draw", amount: 3, id: "one-fortune");
        f.Store.FailSave = true; var failed = await f.Service.ExecuteAsync(command);
        Assert.False(failed.Success); Assert.Equal("", failed.Snapshot.Fortune.EffectId); Assert.Equal(0, failed.Snapshot.Fortune.DrawCount);
        f.Store.FailSave = false; f.Clock.Now += 2 * 86400;
        f.Service = new LocalPlayerHubService(f.Store, f.Clock);
        var drawn = await f.Service.ExecuteAsync(command); Assert.True(drawn.Success);
        Assert.Equal(expected.Fortune.EffectId, drawn.Snapshot.Fortune.EffectId);
        var remaining = drawn.Snapshot.Fortune.RemainingMatches;
        f.Service = new LocalPlayerHubService(f.Store, f.Clock);
        var replay = await f.Service.ExecuteAsync(command); Assert.True(replay.Success);
        Assert.Equal(drawn.Snapshot.Fortune.DrawnAt, replay.Snapshot.Fortune.DrawnAt); Assert.Equal(1, replay.Snapshot.Fortune.DrawCount);
        command.Amount = 4;
        Assert.Equal("id_conflict", (await f.Service.ExecuteAsync(command)).Error);
        Assert.Equal(remaining, (await f.Service.LoadAsync()).Snapshot.Fortune.RemainingMatches);
    }

    [Fact]
    public async Task ConcurrentFortuneChoicesAllowExactlyOneDrawAndCooldownUsesExactRollingTwentyFourHours()
    {
        var f = new Fixture(); await f.Service.LoadAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(slot => f.Service.ExecuteAsync(Cmd("fortune_draw", amount: slot))));
        Assert.Single(results, result => result.Success);
        var first = results.Single(result => result.Success).Snapshot.Fortune;
        f.Clock.Now = first.ReadyAt - 1;
        Assert.False(HubFortunes.CanDraw((await f.Service.LoadAsync()).Snapshot.Fortune, f.Clock.Now));
        Assert.False((await f.Service.ExecuteAsync(Cmd("fortune_draw"))).Success);
        f.Clock.Now++;
        var second = await f.Ok("fortune_draw", amount: 1);
        Assert.Equal(2, second.Fortune.DrawCount); Assert.Equal(f.Clock.Now + 86400, second.Fortune.ReadyAt);
        Assert.Equal(HubFortunes.Find(second.Fortune.EffectId)!.Matches, second.Fortune.RemainingMatches);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    public async Task FortuneRejectsInvalidSlotWithoutUsingDailyChoice(int slot)
    {
        var f = new Fixture(); await f.Service.LoadAsync();
        var failed = await f.Service.ExecuteAsync(Cmd("fortune_draw", amount: slot));
        Assert.False(failed.Success); Assert.Equal("fortune_card", failed.Error); Assert.Equal(0, failed.Snapshot.Fortune.DrawCount);
    }

    [Fact]
    public async Task FortuneCannotBeChangedDuringAStartedMatchAndOldSavesInitializeWithoutChangingWallet()
    {
        var f = new Fixture(); await f.Service.LoadAsync(); var saved = f.Store.Read();
        saved.Fortune = null!; saved.Profile.Coins = 1444; f.Store.Write(saved); f.Service = new LocalPlayerHubService(f.Store, f.Clock);
        var loaded = (await f.Service.LoadAsync()).Snapshot; Assert.Equal(1444, loaded.Profile.Coins); Assert.NotNull(loaded.Fortune);
        Assert.Equal(-1, loaded.Fortune.SelectedCardIndex); Assert.True(HubFortunes.CanDraw(loaded.Fortune, f.Clock.Now));
        var match = await f.Ok("begin_match", "durak");
        Assert.Equal("active_match", (await f.Service.ExecuteAsync(Cmd("fortune_draw"))).Error);
        await f.Ok("abandon_match", match.ActiveMatchId); Assert.Equal(1, (await f.Ok("fortune_draw")).Fortune.DrawCount);
    }

    [Theory]
    [InlineData("golden-diamond", true, false, 96, 40, 2)]
    [InlineData("golden-diamond", false, false, 24, 20, 2)]
    [InlineData("golden-diamond", true, true, 24, 20, 2)]
    [InlineData("wise-king", true, false, 80, 50, 2)]
    [InlineData("wise-king", false, false, 20, 25, 2)]
    [InlineData("black-cat", true, false, 72, 40, 1)]
    [InlineData("black-cat", false, true, 18, 20, 1)]
    [InlineData("stingy-fate", true, false, 64, 40, 0)]
    [InlineData("stingy-fate", false, false, 16, 20, 0)]
    public async Task FortunePercentEffectsChangeOnlyTheNormalMatchRewardAndPersistRemainingCount(string effect, bool won, bool draw, int coins, int xp, int remaining)
    {
        var f = await FortuneFixture(effect); var before = (await f.Service.LoadAsync()).Snapshot;
        var result = await FinishFortuneMatch(f, "durak", won, draw); var state = result.Snapshot;
        Assert.Equal(before.Profile.Coins + coins, state.Profile.Coins); Assert.Equal(before.Profile.XP + xp, state.Profile.XP);
        Assert.Equal(before.Pass.XP + xp, state.Pass.XP); Assert.Equal(remaining, state.Fortune.RemainingMatches);
        Assert.Equal(coins - (won && !draw ? 80 : 20), state.Fortune.LastCoinsAdjustment);
        Assert.Equal(xp - (won && !draw ? 40 : 20), state.Fortune.LastXPAdjustment);
        Assert.Contains("+" + coins + " тун-тун коинов и " + xp + " опыта", result.Message);
        Assert.Contains(HubFortunes.Find(effect)!.Name, result.Message);
        f.Service = new LocalPlayerHubService(f.Store, f.Clock);
        Assert.Equal(remaining, (await f.Service.LoadAsync()).Snapshot.Fortune.RemainingMatches);
    }

    [Fact]
    public async Task FortuneDoesNotMultiplyWonStakeOrReturnedDrawStakeAndExpiresAfterItsMatchCount()
    {
        var f = await FortuneFixture("golden-diamond");
        var won = (await FinishFortuneMatch(f, "poker", true, stake: 137)).Snapshot;
        Assert.Equal(300 - 137 + 274 + 96, won.Profile.Coins);
        var drawn = (await FinishFortuneMatch(f, "poker", true, draw: true, stake: 137)).Snapshot;
        Assert.Equal(won.Profile.Coins + 24, drawn.Profile.Coins);
        var third = (await FinishFortuneMatch(f, "durak", true)).Snapshot; Assert.Equal(0, third.Fortune.RemainingMatches);
        f.Clock.Now += 10 * 86400; // The shown prediction persists even after its benefit is exhausted.
        var fourth = (await FinishFortuneMatch(f, "durak", true)).Snapshot;
        Assert.Equal(third.Profile.Coins + 80, fourth.Profile.Coins); Assert.Equal("golden-diamond", fourth.Fortune.EffectId);
    }

    [Fact]
    public async Task HelpingHandTriggersOnThirdMatchLossButNotDrawAndPaysOnlyOnce()
    {
        var f = await FortuneFixture("helping-hand");
        var first = (await FinishFortuneMatch(f, "durak", false, draw: true)).Snapshot;
        Assert.False(first.Fortune.Triggered); Assert.Equal(2, first.Fortune.RemainingMatches);
        var second = (await FinishFortuneMatch(f, "durak", true)).Snapshot;
        var third = (await FinishFortuneMatch(f, "durak", false)).Snapshot;
        Assert.Equal(second.Profile.Coins + 60, third.Profile.Coins); Assert.True(third.Fortune.Triggered); Assert.Equal(0, third.Fortune.RemainingMatches);
        var fourth = (await FinishFortuneMatch(f, "durak", false)).Snapshot; Assert.Equal(third.Profile.Coins + 20, fourth.Profile.Coins);
    }

    [Fact]
    public async Task UnusedConditionalFortunesExpireAfterThreeCompletedMatches()
    {
        foreach (var effect in new[] { "helping-hand", "new-road" })
        {
            var f = await FortuneFixture(effect, "poker");
            for (var i = 0; i < 3; i++) await FinishFortuneMatch(f, "durak", true);
            var expired = (await f.Service.LoadAsync()).Snapshot; Assert.False(expired.Fortune.Triggered); Assert.Equal(0, expired.Fortune.RemainingMatches);
            var next = (await FinishFortuneMatch(f, "poker", effect == "new-road")).Snapshot;
            Assert.Equal(expired.Profile.Coins + (effect == "new-road" ? 80 : 20), next.Profile.Coins);
        }
    }

    [Fact]
    public async Task NewRoadChoosesAnIntegratedGameOtherThanTheLastAndDescribesItsExactTarget()
    {
        var gotRoad = false;
        for (var slot = 0; slot < 6; slot++)
        {
            var f = new Fixture(); await f.Service.LoadAsync(); await FinishFortuneMatch(f, "poker", true);
            var drawn = await f.Ok("fortune_draw", amount: slot);
            if (drawn.Fortune.EffectId != "new-road") continue;
            gotRoad = true; Assert.NotEqual("poker", drawn.Fortune.TargetGame);
            Assert.Contains(drawn.Fortune.TargetGame, new[] { "durak", "bluff", "chests", "govno" });
            Assert.Contains(HubFortunes.GameName(drawn.Fortune.TargetGame), HubFortunes.EffectText(drawn.Fortune));
        }
        Assert.True(gotRoad);
    }

    [Fact]
    public async Task NewRoadRequiresVictoryInTargetGameAndCanTriggerOnLastAvailableMatch()
    {
        var f = await FortuneFixture("new-road", "poker");
        var wrong = (await FinishFortuneMatch(f, "durak", true)).Snapshot; Assert.False(wrong.Fortune.Triggered);
        var draw = (await FinishFortuneMatch(f, "poker", true, draw: true)).Snapshot; Assert.False(draw.Fortune.Triggered);
        var win = (await FinishFortuneMatch(f, "poker", true)).Snapshot;
        Assert.Equal(draw.Profile.Coins + 140, win.Profile.Coins); Assert.True(win.Fortune.Triggered); Assert.Equal(0, win.Fortune.RemainingMatches);
    }

    [Fact]
    public async Task FortuneRewardsKeepDebtWithholdingAndGrossIncomeAccountingConsistent()
    {
        var f = await FortuneFixture("golden-diamond"); var saved = f.Store.Read();
        saved.Bank.Principal = 100; saved.Bank.Debt = 110; saved.Bank.Status = "approved";
        saved.Bank.DueAt = f.Clock.Now + 86400; saved.Bank.CollectorAt = saved.Bank.DueAt + 43200;
        f.Store.Write(saved); f.Service = new LocalPlayerHubService(f.Store, f.Clock);
        var result = (await FinishFortuneMatch(f, "durak", true)).Snapshot;
        Assert.Equal(348, result.Profile.Coins); Assert.Equal(62, result.Bank.Debt); Assert.Equal(396, result.Profile.CoinsEarned);
        Assert.Equal(48, result.Bank.Payments.Last().Amount); Assert.Equal(2, result.Fortune.RemainingMatches);
    }

    [Fact]
    public async Task GiftLoanQuestAchievementPassAndJobClaimsDoNotApplyOrConsumeFortune()
    {
        var f = await FortuneFixture("golden-diamond"); var before = (await f.Service.LoadAsync()).Snapshot;
        var gift = await f.Ok("gift_open"); Assert.Equal(before.Profile.Coins + HubGift.Coins, gift.Profile.Coins);
        await f.Ok("tutorial_skip"); var achievement = await f.Ok("achievement_claim", "tutorial_skip");
        Assert.Equal(gift.Profile.Coins + 25, achievement.Profile.Coins);
        var pass = await f.Ok("pass_claim", "free-1"); Assert.Equal(achievement.Profile.Coins + 35, pass.Profile.Coins);
        var loan = await f.Ok("bank_request", amount: 100); Assert.Equal(pass.Profile.Coins + 100, loan.Profile.Coins);
        var saved = f.Store.Read(); var quest = saved.Quests[0]; quest.Progress = quest.Goal;
        var job = saved.BankJobs.Jobs[0]; job.Accepted = true; job.Progress = job.Goal; saved.BankJobs.ActiveJobId = job.Id;
        f.Store.Write(saved); f.Service = new LocalPlayerHubService(f.Store, f.Clock);
        var claimedQuest = await f.Ok("quest_claim", quest.Id); Assert.Equal(loan.Profile.Coins + quest.RewardCoins, claimedQuest.Profile.Coins);
        var claimedJob = await f.Ok("bank_job_claim", job.Id); Assert.Equal(claimedQuest.Profile.Coins + job.RewardCoins, claimedJob.Profile.Coins);
        Assert.Equal(3, claimedJob.Fortune.RemainingMatches); Assert.Equal("", claimedJob.Fortune.LastMatchId);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task CollectorEncountersDoNotApplyOrConsumeFortune(bool won, bool draw)
    {
        var (f, signed) = await NewTermsLoan(); await SetFortune(f, "helping-hand");
        f.Clock.Now = signed.Bank.DueAt;
        var start = await f.Ok("play_collectors");
        var result = await f.Ok("report_match", start.Bank.CollectorGame, start.ActiveMatchId, flag: won, amount: draw ? 1 : 0);
        Assert.Equal(3, result.Fortune.RemainingMatches); Assert.False(result.Fortune.Triggered); Assert.Equal("", result.Fortune.LastPlayedGame);
    }

    [Fact]
    public async Task LegacyCollectorLossStillSuppressesCoinsAndKeepsPredictionForNormalGames()
    {
        var f = await FortuneFixture("helping-hand"); var saved = f.Store.Read();
        saved.Bank.Principal = 100; saved.Bank.Debt = 110; saved.Bank.Status = "approved";
        saved.Bank.DueAt = f.Clock.Now - 43200; saved.Bank.CollectorAt = f.Clock.Now;
        f.Store.Write(saved); f.Service = new LocalPlayerHubService(f.Store, f.Clock);
        var start = await f.Ok("play_collectors");
        var result = await f.Ok("report_match", start.Bank.CollectorGame, start.ActiveMatchId);
        Assert.Equal(0, result.Profile.Coins); Assert.Equal(3, result.Fortune.RemainingMatches); Assert.False(result.Fortune.Triggered);
    }

    [Fact]
    public async Task DedicatedBankJobMatchHasNormalRewardButSeparateClaimIsUnmodified()
    {
        var f = await FortuneFixture("golden-diamond"); var state = (await f.Service.LoadAsync()).Snapshot; var job = state.BankJobs.Jobs[0];
        await f.Ok("bank_job_accept", job.Id); var start = await f.Ok("bank_job_start", job.Id);
        var won = await f.Ok("report_match", "durak", start.ActiveMatchId, flag: true);
        Assert.Equal(state.Profile.Coins + 96, won.Profile.Coins); Assert.Equal(2, won.Fortune.RemainingMatches);
        var claimed = await f.Ok("bank_job_claim", job.Id);
        Assert.Equal(won.Profile.Coins + job.RewardCoins, claimed.Profile.Coins); Assert.Equal(2, claimed.Fortune.RemainingMatches);
    }

    [Fact]
    public async Task AbandonedDuplicateFailedAndTokenlessReportsCannotSpendAnExtraFortuneCharge()
    {
        var f = await FortuneFixture("black-cat");
        var tokenless = await f.Ok("report_match", "durak", flag: true); Assert.Equal(2, tokenless.Fortune.RemainingMatches);
        var start = await f.Ok("begin_match", "durak"); await f.Ok("abandon_match", start.ActiveMatchId);
        var late = await f.Ok("report_match", "durak", start.ActiveMatchId, flag: true); Assert.Equal(2, late.Fortune.RemainingMatches);
        start = await f.Ok("begin_match", "durak"); var command = Cmd("report_match", "durak", start.ActiveMatchId, flag: true);
        f.Store.FailSave = true; var failed = await f.Service.ExecuteAsync(command); Assert.False(failed.Success); Assert.Equal(2, failed.Snapshot.Fortune.RemainingMatches);
        f.Store.FailSave = false; var complete = await f.Service.ExecuteAsync(command); Assert.True(complete.Success); Assert.Equal(1, complete.Snapshot.Fortune.RemainingMatches);
        var replay = await f.Service.ExecuteAsync(command); Assert.Equal(complete.Snapshot.Profile.Coins, replay.Snapshot.Profile.Coins); Assert.Equal(1, replay.Snapshot.Fortune.RemainingMatches);
        var duplicate = await f.Ok("report_match", "durak", start.ActiveMatchId, flag: true); Assert.Equal(1, duplicate.Fortune.RemainingMatches);
    }

    [Fact]
    public async Task FortuneRemainsUntilPlayedAndNewDailyDrawReplacesRatherThanStacksEffects()
    {
        var f = await FortuneFixture("golden-diamond"); f.Clock.Now += 2 * 86400;
        var active = (await f.Service.LoadAsync()).Snapshot;
        Assert.Equal(3, active.Fortune.RemainingMatches); Assert.True(HubFortunes.Active(active.Fortune));
        var foundReplacement = false;
        for (var slot = 0; slot < 6; slot++)
        {
            var copy = new Fixture(); copy.Clock.Now = f.Clock.Now; copy.Store.Json = f.Store.Json;
            var drawn = await copy.Ok("fortune_draw", amount: slot);
            if (drawn.Fortune.EffectId != "wise-king") continue;
            foundReplacement = true;
            var result = (await FinishFortuneMatch(copy, "durak", true)).Snapshot;
            Assert.Equal(drawn.Profile.Coins + 80, result.Profile.Coins);
            Assert.Equal(drawn.Profile.XP + 50, result.Profile.XP); Assert.Equal(2, result.Fortune.RemainingMatches);
        }
        Assert.True(foundReplacement);
    }

    [Fact]
    public async Task PreviousFortuneModifierCannotLeakIntoLaterMatchOrDuplicateResult()
    {
        var f = await FortuneFixture("stingy-fate");
        var first = await FinishFortuneMatch(f, "durak", true);
        var oldMatchId = first.Snapshot.Fortune.LastMatchId;
        Assert.Equal(-16, first.Snapshot.Fortune.LastCoinsAdjustment);
        var next = await FinishFortuneMatch(f, "durak", true);
        Assert.NotEqual(oldMatchId, next.Snapshot.Fortune.LastMatchId);
        Assert.Equal(0, next.Snapshot.Fortune.LastCoinsAdjustment);
        var duplicate = await f.Ok("report_match", "durak", oldMatchId, flag: true);
        Assert.Equal(next.Snapshot.Fortune.LastMatchId, duplicate.Fortune.LastMatchId);
        Assert.Equal(0, duplicate.Fortune.LastCoinsAdjustment); Assert.Equal(next.Snapshot.Profile.Coins, duplicate.Profile.Coins);
    }

    private static async Task<Fixture> FortuneFixture(string effect, string target = "poker")
    {
        var f = new Fixture(); await f.Service.LoadAsync(); await SetFortune(f, effect, target); return f;
    }
    private static async Task SetFortune(Fixture f, string effect, string target = "poker")
    {
        var saved = f.Store.Read();
        saved.Fortune = new HubFortuneState { EffectId = effect, DrawnAt = f.Clock.Now, ReadyAt = f.Clock.Now + HubFortunes.Cooldown,
            DrawCount = 1, SelectedCardIndex = 0, RemainingMatches = HubFortunes.Find(effect)!.Matches, TargetGame = target };
        f.Store.Write(saved); f.Service = new LocalPlayerHubService(f.Store, f.Clock); await f.Service.LoadAsync();
    }
    private static async Task<HubResult> FinishFortuneMatch(Fixture f, string game, bool won, bool draw = false, int stake = 0)
    {
        var started = await f.Ok("begin_match", game, amount: stake);
        var result = await f.Service.ExecuteAsync(Cmd("report_match", game, started.ActiveMatchId, flag: won, amount: draw ? 1 : 0));
        Assert.True(result.Success, result.Error + " " + result.Message); return result;
    }
}
