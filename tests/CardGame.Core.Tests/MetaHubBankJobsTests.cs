using CardGame.Core.Meta;

namespace CardGame.Core.Tests;

public sealed partial class MetaHubTests
{
    [Fact]
    public async Task BankJobsMigrateWithoutResettingWalletAndOfferThreeDistinctCasesForSevenDays()
    {
        var f = new Fixture(); await f.Service.LoadAsync();
        var saved = f.Store.Read(); saved.BankJobs = null!; saved.Profile.Coins = 1777; f.Store.Write(saved);
        f.Service = new LocalPlayerHubService(f.Store, f.Clock);
        var state = (await f.Service.LoadAsync()).Snapshot;
        Assert.Equal(1777, state.Profile.Coins); Assert.Equal(3, state.BankJobs.Jobs.Count);
        Assert.Equal(HubBankJobs.Templates, state.BankJobs.Jobs.Select(j => j.Template));
        Assert.Equal(f.Clock.Now + 7 * 86400, state.BankJobs.RefreshAt);
        Assert.All(state.BankJobs.Jobs, job => { Assert.False(job.Accepted); Assert.False(job.Claimed); Assert.Equal(0, job.Progress); Assert.Equal(state.BankJobs.RefreshAt, job.ExpiresAt); });
        Assert.Equal(3, HubBankJobs.Notifications(state.BankJobs));
        var seen = await f.Ok("bank_jobs_seen"); Assert.Equal(0, HubBankJobs.Notifications(seen.BankJobs));
    }

    [Fact]
    public async Task BankJobsRequireAcceptanceKeepOneActiveAndSaveIdentityAndRulesInMatch()
    {
        var f = new Fixture(); var state = (await f.Service.LoadAsync()).Snapshot; var job = state.BankJobs.Jobs[1];
        Assert.False((await f.Service.ExecuteAsync(Cmd("bank_job_start", job.Id))).Success);
        var accepted = await f.Ok("bank_job_accept", job.Id);
        Assert.Equal(job.Id, accepted.BankJobs.ActiveJobId);
        Assert.False((await f.Service.ExecuteAsync(Cmd("bank_job_accept", state.BankJobs.Jobs[0].Id))).Success);
        var start = await f.Ok("bank_job_start", job.Id);
        var entry = start.Matches.Single(m => m.Id == start.ActiveMatchId);
        Assert.Equal(job.Id, entry.BankJobId); Assert.Equal(job.BotName, entry.OpponentName); Assert.Equal(job.AvatarId, entry.OpponentAvatarId);
        Assert.Equal("durak", entry.Game); Assert.True(entry.GameSettings!.AllowCheating); Assert.Equal(36, entry.GameSettings.DeckSize);
        Assert.Equal(0, entry.Stake); Assert.Equal(state.Profile.Coins, start.Profile.Coins);
        Assert.False((await f.Service.ExecuteAsync(Cmd("bank_job_start", job.Id))).Success);
        Assert.False((await f.Service.ExecuteAsync(Cmd("begin_match", "poker"))).Success);
    }

    [Fact]
    public async Task NamedBankOpponentRequiresWonDedicatedMatchAndCannotBeFarmedByNormalResults()
    {
        var f = new Fixture(); var state = (await f.Service.LoadAsync()).Snapshot; var job = state.BankJobs.Jobs[0];
        await f.Ok("bank_job_accept", job.Id);
        await f.Ok("report_match", "durak", flag: true);
        var ordinary = await f.Ok("begin_match", "durak");
        state = await f.Ok("report_match", "durak", text: ordinary.ActiveMatchId, flag: true);
        Assert.Equal(0, state.BankJobs.Jobs[0].Progress);
        var start = await f.Ok("bank_job_start", job.Id);
        state = await f.Ok("report_match", "durak", text: start.ActiveMatchId, flag: true, amount: 1);
        Assert.Equal(0, state.BankJobs.Jobs[0].Progress);
        start = await f.Ok("bank_job_start", job.Id);
        Assert.False(start.Matches.Last().GameSettings!.AllowCheating);
        state = await f.Ok("report_match", "durak", text: start.ActiveMatchId, flag: true);
        Assert.True(HubBankJobs.Ready(state.BankJobs.Jobs[0]));
        Assert.False((await f.Service.ExecuteAsync(Cmd("bank_job_start", job.Id))).Success);
    }

    [Fact]
    public async Task BankCatchJobAccumulatesOnlyCompletedDedicatedMatchesAcrossRestartsAndIgnoresDuplicateResult()
    {
        var f = new Fixture(); var state = (await f.Service.LoadAsync()).Snapshot; var job = state.BankJobs.Jobs[1];
        await f.Ok("bank_job_accept", job.Id); var start = await f.Ok("bank_job_start", job.Id);
        var first = Cmd("report_match", "durak", start.ActiveMatchId); first.CaughtCheats = 1;
        var one = await f.Service.ExecuteAsync(first); Assert.True(one.Success); Assert.Equal(1, one.Snapshot.BankJobs.Jobs[1].Progress);
        var duplicate = Cmd("report_match", "durak", start.ActiveMatchId); duplicate.CaughtCheats = 1;
        var repeated = await f.Service.ExecuteAsync(duplicate); Assert.True(repeated.Success); Assert.Equal(1, repeated.Snapshot.BankJobs.Jobs[1].Progress);
        f.Service = new LocalPlayerHubService(f.Store, f.Clock);
        start = await f.Ok("bank_job_start", job.Id);
        var finish = Cmd("report_match", "durak", start.ActiveMatchId); finish.CaughtCheats = 3;
        var done = await f.Service.ExecuteAsync(finish); Assert.True(done.Success); Assert.Equal(2, done.Snapshot.BankJobs.Jobs[1].Progress);
        Assert.Equal(1, HubBankJobs.Notifications((await f.Ok("bank_jobs_seen")).BankJobs));
    }

    [Theory]
    [InlineData(false, false, 1, 0, false)]
    [InlineData(true, true, 1, 0, false)]
    [InlineData(true, false, 0, 0, false)]
    [InlineData(true, false, 1, 1, false)]
    [InlineData(true, false, 4, 1, false)]
    [InlineData(true, false, 1, 0, true)]
    public async Task QuietBankJobNeedsActualCheatAndWinWithoutBeingCaught(bool won, bool draw, int cheats, int caught, bool ready)
    {
        var f = new Fixture(); var state = (await f.Service.LoadAsync()).Snapshot; var job = state.BankJobs.Jobs[2];
        await f.Ok("bank_job_accept", job.Id); var start = await f.Ok("bank_job_start", job.Id);
        var result = Cmd("report_match", "durak", start.ActiveMatchId, flag: won, amount: draw ? 1 : 0);
        result.CheatsPlayed = cheats; result.TimesCaught = caught;
        var completed = await f.Service.ExecuteAsync(result); Assert.True(completed.Success);
        Assert.Equal(ready, HubBankJobs.Ready(completed.Snapshot.BankJobs.Jobs[2]));
    }

    [Fact]
    public async Task AbandonedBankMatchCannotProgressAndCanBeRetriedWithoutDebtOrJobPenalty()
    {
        var f = new Fixture(); var state = (await f.Service.LoadAsync()).Snapshot; var job = state.BankJobs.Jobs[1];
        await f.Ok("bank_job_accept", job.Id); var start = await f.Ok("bank_job_start", job.Id);
        var left = await f.Ok("abandon_match", start.ActiveMatchId);
        Assert.Equal(state.Profile.Coins, left.Profile.Coins); Assert.Equal(0, left.Bank.Debt);
        var late = Cmd("report_match", "durak", start.ActiveMatchId, flag: true); late.CaughtCheats = 2;
        var ignored = await f.Service.ExecuteAsync(late); Assert.True(ignored.Success); Assert.Equal(0, ignored.Snapshot.BankJobs.Jobs[1].Progress);
        Assert.Equal(job.Id, ignored.Snapshot.BankJobs.ActiveJobId);
        Assert.NotEqual(start.ActiveMatchId, (await f.Ok("bank_job_start", job.Id)).ActiveMatchId);
    }

    [Fact]
    public async Task WeeklyBankRefreshPreservesAcceptedProgressAndUnclaimedRewardsAndReplacesExpiredClaimedCase()
    {
        var f = new Fixture(); var initial = (await f.Service.LoadAsync()).Snapshot; var job = initial.BankJobs.Jobs[1];
        await f.Ok("bank_job_accept", job.Id); var start = await f.Ok("bank_job_start", job.Id);
        var result = Cmd("report_match", "durak", start.ActiveMatchId); result.CaughtCheats = 1; Assert.True((await f.Service.ExecuteAsync(result)).Success);
        f.Clock.Now = initial.BankJobs.RefreshAt;
        var refreshed = (await f.Service.LoadAsync()).Snapshot;
        Assert.Equal(job.Id, refreshed.BankJobs.Jobs[1].Id); Assert.Equal(1, refreshed.BankJobs.Jobs[1].Progress);
        Assert.NotEqual(initial.BankJobs.Jobs[0].Id, refreshed.BankJobs.Jobs[0].Id);
        start = await f.Ok("bank_job_start", job.Id); result = Cmd("report_match", "durak", start.ActiveMatchId); result.CaughtCheats = 1;
        Assert.True((await f.Service.ExecuteAsync(result)).Success);
        f.Clock.Now += 30 * 86400; var waiting = (await f.Service.LoadAsync()).Snapshot;
        Assert.True(HubBankJobs.Ready(waiting.BankJobs.Jobs[1])); Assert.Equal(job.Id, waiting.BankJobs.ActiveJobId);
        var command = Cmd("bank_job_claim", job.Id);
        f.Store.FailSave = true; Assert.False((await f.Service.ExecuteAsync(command)).Success); f.Store.FailSave = false;
        var claimed = await f.Service.ExecuteAsync(command); Assert.True(claimed.Success);
        Assert.Equal(waiting.Profile.Coins + job.RewardCoins, claimed.Snapshot.Profile.Coins);
        Assert.Equal(waiting.Profile.XP + job.RewardXP, claimed.Snapshot.Profile.XP);
        Assert.Equal("", claimed.Snapshot.BankJobs.ActiveJobId); Assert.NotEqual(job.Id, claimed.Snapshot.BankJobs.Jobs[1].Id);
        f.Service = new LocalPlayerHubService(f.Store, f.Clock);
        var replay = await f.Service.ExecuteAsync(command); Assert.True(replay.Success); Assert.Equal(claimed.Snapshot.Profile.Coins, replay.Snapshot.Profile.Coins);
        Assert.False((await f.Service.ExecuteAsync(Cmd("bank_job_claim", job.Id))).Success);
    }

    [Fact]
    public async Task BankRewardCannotBeClaimedEarlyOrTwiceUnderConcurrency()
    {
        var f = new Fixture(); var state = (await f.Service.LoadAsync()).Snapshot; var job = state.BankJobs.Jobs[0];
        await f.Ok("bank_job_accept", job.Id);
        Assert.False((await f.Service.ExecuteAsync(Cmd("bank_job_claim", job.Id))).Success);
        var start = await f.Ok("bank_job_start", job.Id);
        state = await f.Ok("report_match", "durak", start.ActiveMatchId, flag: true);
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => f.Service.ExecuteAsync(Cmd("bank_job_claim", job.Id))));
        Assert.Single(results, result => result.Success);
        var final = (await f.Service.LoadAsync()).Snapshot;
        Assert.Equal(state.Profile.Coins + job.RewardCoins, final.Profile.Coins); Assert.Equal(state.Profile.XP + job.RewardXP, final.Profile.XP);
        Assert.True(final.BankJobs.Jobs[0].Claimed); Assert.Equal("", final.BankJobs.ActiveJobId);
        Assert.Equal(state.BankJobs.RefreshAt, final.BankJobs.RefreshAt);
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(1025, 0, 0)]
    [InlineData(0, 0, 1)]
    public async Task InvalidBankEvidenceDoesNotMutateMatchWalletOrProgress(int catches, int cheats, int caught)
    {
        var f = new Fixture(); var state = (await f.Service.LoadAsync()).Snapshot; var job = state.BankJobs.Jobs[1];
        await f.Ok("bank_job_accept", job.Id); var start = await f.Ok("bank_job_start", job.Id);
        var result = Cmd("report_match", "durak", start.ActiveMatchId); result.CaughtCheats = catches; result.CheatsPlayed = cheats; result.TimesCaught = caught;
        Assert.False((await f.Service.ExecuteAsync(result)).Success);
        var loaded = (await f.Service.LoadAsync()).Snapshot;
        Assert.Equal(start.ActiveMatchId, loaded.ActiveMatchId); Assert.Equal(start.Profile.Coins, loaded.Profile.Coins); Assert.Equal(0, loaded.BankJobs.Jobs[1].Progress);
    }

    [Fact]
    public async Task BankMatchEvidenceIsPartOfIdempotencyFingerprint()
    {
        var f = new Fixture(); var state = (await f.Service.LoadAsync()).Snapshot; var job = state.BankJobs.Jobs[1];
        await f.Ok("bank_job_accept", job.Id); var start = await f.Ok("bank_job_start", job.Id);
        var result = Cmd("report_match", "durak", start.ActiveMatchId); result.CaughtCheats = 1;
        Assert.True((await f.Service.ExecuteAsync(result)).Success); result.CaughtCheats = 2;
        var altered = await f.Service.ExecuteAsync(result); Assert.False(altered.Success); Assert.Equal("id_conflict", altered.Error);
    }
}
