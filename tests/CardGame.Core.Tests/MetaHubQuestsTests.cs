using CardGame.Core.Meta;

namespace CardGame.Core.Tests;

public sealed partial class MetaHubTests
{
    [Fact]
    public async Task ResultHintChoosesClosestUnclaimedQuestAndUpdatesAfterReporting()
    {
        var f = await QuestFixture(easy: "easy-matches", medium: "medium-games", hard: "hard-wins");
        var first = await f.Ok("report_match", "durak");
        Assert.Contains("1 / 2", HubQuests.ResultHint(first));
        var completed = await f.Ok("report_match", "bluff");
        Assert.Equal("Поручение выполнено — забери у Шестёрки", HubQuests.ResultHint(completed));
        Assert.Equal("", HubQuests.ResultHint(null));
        foreach (var q in completed.Quests) q.Claimed = true;
        Assert.Equal("", HubQuests.ResultHint(completed));
    }

    [Theory]
    [InlineData("durak")]
    [InlineData("bluff")]
    [InlineData("govno")]
    [InlineData("poker")]
    public async Task GameSpecificWinQuestIgnoresOtherGamesAndDraws(string game)
    {
        var f = await QuestFixture(hard: "hard-" + game);
        var other = game == "durak" ? "bluff" : "durak";
        await f.Ok("report_match", other, flag: true);
        var draw = await f.Ok("report_match", game, flag: true, amount: 1);
        Assert.Equal(0, draw.Quests.Single(q => q.Tier == "hard").Progress);
        var win = await f.Ok("report_match", game, flag: true);
        Assert.True(HubQuests.Ready(win.Quests.Single(q => q.Tier == "hard")));
    }

    [Fact]
    public async Task QuestCatalogAndSeededRotationHaveFiveSlotsAndNoRecentRepeats()
    {
        Assert.Equal(32, HubQuests.Templates.Count);
        Assert.Equal(32, HubQuests.Templates.Select(t => t.Id).Distinct().Count());
        Assert.Equal(new[] { 7, 7, 8, 10 }, new[] { "easy", "medium", "hard", "weekly" }.Select(t => HubQuests.Templates.Count(q => q.Tier == t)));
        var a = new Fixture(); var b = new Fixture();
        var previous = new Dictionary<string, List<string>>();
        var ids = new HashSet<string>();
        for (var i = 0; i < 35; i++)
        {
            var one = await a.Ok("quests_debug_refresh"); var two = await b.Ok("quests_debug_refresh");
            Assert.Equal(one.Quests.Select(q => q.Template), two.Quests.Select(q => q.Template));
            Assert.Equal(5, one.Quests.Select(q => q.Template).Distinct().Count());
            Assert.Equal(HubQuests.Slots, one.Quests.Select(q => q.Slot));
            foreach (var q in one.Quests)
            {
                Assert.True(ids.Add(q.Id)); // Also unique for multiple refreshes in the same second.
                if (!previous.TryGetValue(q.Tier, out var history)) previous[q.Tier] = history = new();
                Assert.DoesNotContain(q.Template, history.TakeLast(5)); history.Add(q.Template);
                Assert.False(q.Seen); Assert.Equal(0, q.Progress);
            }
            // History and the deterministic selection survive serialization and a service restart.
            a.Service = new LocalPlayerHubService(a.Store, a.Clock);
        }
    }

    [Theory]
    [InlineData("easy")]
    [InlineData("weekly-0")]
    public async Task QuestDeadlinesReplaceIncompleteButKeepClaimedUntilDeadline(string slot)
    {
        var f = await QuestFixture(); var before = (await f.Service.LoadAsync()).Snapshot;
        var note = before.Quests.Single(q => q.Slot == slot);
        f.Clock.Now = note.ExpiresAt - 1;
        Assert.Contains((await f.Service.LoadAsync()).Snapshot.Quests, q => q.Id == note.Id);
        f.Clock.Now++;
        var after = (await f.Service.LoadAsync()).Snapshot;
        Assert.DoesNotContain(after.Quests, q => q.Id == note.Id);
        Assert.Equal(0, after.Quests.Single(q => q.Slot == slot).Progress);

        var saved = f.Store.Read(); note = saved.Quests.Single(q => q.Slot == slot); note.Progress = note.Goal;
        f.Store.Write(saved); f.Service = new LocalPlayerHubService(f.Store, f.Clock);
        var claimed = await f.Ok("quest_claim", note.Id);
        Assert.True(claimed.Quests.Single(q => q.Id == note.Id).Claimed);
        f.Clock.Now = note.ExpiresAt - 1;
        Assert.Contains((await f.Service.LoadAsync()).Snapshot.Quests, q => q.Id == note.Id && q.Claimed);
        f.Clock.Now++;
        Assert.DoesNotContain((await f.Service.LoadAsync()).Snapshot.Quests, q => q.Id == note.Id);
    }

    [Theory]
    [InlineData("easy")]
    [InlineData("weekly-0")]
    public async Task CompletedUnclaimedQuestSurvivesDeadlineAndClaimReplacesItAtomically(string slot)
    {
        var f = await QuestFixture(); var saved = f.Store.Read(); var note = saved.Quests.Single(q => q.Slot == slot);
        note.Progress = note.Goal; f.Store.Write(saved); f.Service = new LocalPlayerHubService(f.Store, f.Clock);
        f.Clock.Now = note.ExpiresAt + 8 * 86400;
        var waiting = (await f.Service.LoadAsync()).Snapshot;
        Assert.Contains(waiting.Quests, q => q.Id == note.Id && HubQuests.Ready(q));
        var command = Cmd("quest_claim", note.Id);
        f.Store.FailSave = true;
        Assert.False((await f.Service.ExecuteAsync(command)).Success);
        f.Store.FailSave = false;
        var result = await f.Service.ExecuteAsync(command); Assert.True(result.Success);
        Assert.Equal(waiting.Profile.Coins + note.RewardCoins, result.Snapshot.Profile.Coins);
        Assert.Equal(waiting.Profile.XP + note.RewardXP, result.Snapshot.Profile.XP);
        Assert.DoesNotContain(result.Snapshot.Quests, q => q.Id == note.Id);
        Assert.False(result.Snapshot.Quests.Single(q => q.Slot == slot).Seen);
        var replay = await new LocalPlayerHubService(f.Store, f.Clock).ExecuteAsync(command);
        Assert.Equal(result.Snapshot.Profile.Coins, replay.Snapshot.Profile.Coins);
        Assert.Equal(result.Snapshot.Quests.Select(q => q.Id), replay.Snapshot.Quests.Select(q => q.Id));
        Assert.False((await f.Service.ExecuteAsync(Cmd("quest_claim", note.Id))).Success);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(-7)]
    public async Task WeeklyQuestsLastSevenDaysEvenWhenIssuedJustBeforeMonday(int offset)
    {
        var f = new Fixture();
        f.Clock.Now = new DateTimeOffset(2026, 10, 4, 23, 59, 59, TimeSpan.FromHours(offset)).ToUnixTimeSeconds();
        var first = (await f.Service.LoadAsync()).Snapshot;
        var issuedAt = f.Clock.Now;
        Assert.All(first.Quests.Where(q => q.Period == "weekly"), q => Assert.Equal(issuedAt + HubQuest.WeeklySeconds, q.ExpiresAt));
        f.Clock.Now++;
        var monday = (await f.Service.LoadAsync()).Snapshot;
        Assert.All(monday.Quests.Where(q => q.Period == "weekly"), q => Assert.Contains(first.Quests, old => old.Id == q.Id));
        f.Clock.Now = issuedAt + HubQuest.WeeklySeconds - 1;
        Assert.All((await f.Service.LoadAsync()).Snapshot.Quests.Where(q => q.Period == "weekly"), q => Assert.Contains(first.Quests, old => old.Id == q.Id));
        f.Clock.Now++;
        var next = (await f.Service.LoadAsync()).Snapshot;
        Assert.All(next.Quests.Where(q => q.Period == "weekly"), q => Assert.Equal(f.Clock.Now + HubQuest.WeeklySeconds, q.ExpiresAt));
        Assert.DoesNotContain(next.Quests, q => q.Period == "weekly" && first.Quests.Any(old => old.Id == q.Id));
        Assert.All(next.Quests.Where(q => q.Period == "daily"), q => Assert.Equal(q.IssuedAt + HubQuest.DailySeconds, q.ExpiresAt));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ExistingWeeklyDeadlineExtendsFromIssueWithoutResettingProgressOrRewards(int state)
    {
        var f = await QuestFixture(); var saved = f.Store.Read();
        var note = saved.Quests.Single(q => q.Slot == "weekly-0");
        note.ExpiresAt = note.IssuedAt + 4 * 86400;
        note.Progress = state == 0 ? 3 : note.Goal;
        note.Claimed = state == 2; note.Seen = true;
        f.Clock.Now = note.ExpiresAt + 1;
        f.Store.Write(saved); f.Service = new LocalPlayerHubService(f.Store, f.Clock);
        var loaded = (await f.Service.LoadAsync()).Snapshot;
        var migrated = loaded.Quests.Single(q => q.Id == note.Id);
        note.ExpiresAt = note.IssuedAt + HubQuest.WeeklySeconds;
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(note), System.Text.Json.JsonSerializer.Serialize(migrated));
        Assert.Equal(saved.Profile.Coins, loaded.Profile.Coins);
        Assert.Equal(saved.Profile.XP, loaded.Profile.XP);
        f.Clock.Now += 3600;
        f.Service = new LocalPlayerHubService(f.Store, f.Clock);
        Assert.Equal(note.ExpiresAt, (await f.Service.LoadAsync()).Snapshot.Quests.Single(q => q.Id == note.Id).ExpiresAt);
        f.Clock.Now = note.ExpiresAt;
        var expired = (await f.Service.LoadAsync()).Snapshot;
        if (state == 1)
        {
            Assert.Contains(expired.Quests, q => q.Id == note.Id && HubQuests.Ready(q));
            var claimed = await f.Ok("quest_claim", note.Id);
            Assert.Equal(loaded.Profile.Coins + note.RewardCoins, claimed.Profile.Coins);
            Assert.Equal(f.Clock.Now + HubQuest.WeeklySeconds, claimed.Quests.Single(q => q.Slot == note.Slot).ExpiresAt);
        }
        else Assert.DoesNotContain(expired.Quests, q => q.Id == note.Id);
    }

    [Fact]
    public async Task QuestNotificationsSeenAndDebugCommandsUseEachNoteOnlyOnce()
    {
        var f = new Fixture(); var first = (await f.Service.LoadAsync()).Snapshot;
        Assert.Equal(5, HubQuests.Notifications(first));
        var complete = await f.Ok("quests_debug_complete"); Assert.Equal(5, HubQuests.Notifications(complete));
        Assert.All(complete.Quests.Where(q => q.Period == "weekly"), q => Assert.Equal(0, q.Progress));
        var seen = await f.Ok("quests_seen"); Assert.Equal(3, HubQuests.Notifications(seen));
        Assert.All(seen.Quests, q => Assert.True(q.Seen));
        foreach (var note in seen.Quests.Where(q => q.Period == "daily")) await f.Ok("quest_claim", note.Id);
        Assert.Equal(0, HubQuests.Notifications((await f.Service.LoadAsync()).Snapshot));
        var refreshed = await f.Ok("quests_debug_refresh"); Assert.Equal(5, HubQuests.Notifications(refreshed));
        Assert.All(refreshed.Quests, q => Assert.Equal(0, q.Progress));
        Assert.Equal(first.Profile.Coins + HubQuest.EasyCoins + HubQuest.MediumCoins + HubQuest.HardCoins, refreshed.Profile.Coins);
    }

    [Fact]
    public async Task LegacyQuestSaveMigratesWithoutLosingWalletOrOtherProgress()
    {
        var f = new Fixture(); await f.Service.LoadAsync(); var saved = f.Store.Read();
        saved.Quests = new() { new HubQuest { Id = "daily-matches-1", Progress = 2 }, new HubQuest { Id = "weekly-chests-1", Claimed = true } };
        saved.QuestHistory = null!; saved.Profile.Coins = 1234; saved.Profile.XP = 678;
        f.Store.Write(saved); f.Service = new LocalPlayerHubService(f.Store, f.Clock);
        var migrated = await f.Service.LoadAsync(); Assert.True(migrated.Success);
        Assert.Equal(1234, migrated.Snapshot.Profile.Coins); Assert.Equal(678, migrated.Snapshot.Profile.XP);
        Assert.Equal(5, migrated.Snapshot.Quests.Count);
        Assert.All(migrated.Snapshot.Quests, q => { Assert.NotEmpty(q.Template); Assert.NotNull(q.Games); Assert.False(q.Seen); });
        Assert.Equal(4, migrated.Snapshot.QuestHistory.Count);
    }

    [Fact]
    public async Task SavedQuestNamesUpdateWithoutResettingProgressOrRewards()
    {
        var f = await QuestFixture(easy: "easy-coins", medium: "medium-coins", weekly1: "week-coins");
        var saved = f.Store.Read();
        foreach (var q in saved.Quests.Where(q => q.Event == "match_coins"))
        {
            q.Name = q.Name.Replace("тун-тун ", "");
            q.Progress = q.Tier == "easy" ? 43 : q.Goal;
            q.Claimed = q.Tier == "weekly"; q.Seen = true;
        }
        f.Store.Write(saved); f.Service = new LocalPlayerHubService(f.Store, f.Clock);
        var loaded = (await f.Service.LoadAsync()).Snapshot;
        foreach (var expected in saved.Quests)
        {
            expected.Name = HubQuests.Templates.Single(t => t.Id == expected.Template).Name;
            var actual = loaded.Quests.Single(q => q.Id == expected.Id);
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(expected), System.Text.Json.JsonSerializer.Serialize(actual));
            if (actual.Event == "match_coins") Assert.Contains("тун-тун коинов", actual.Name);
        }
        Assert.Equal(saved.Profile.Coins, loaded.Profile.Coins);
        Assert.Equal(saved.Profile.XP, loaded.Profile.XP);
    }

    [Fact]
    public async Task MatchCoinsExcludeGiftsQuestClaimsAndDrawStakeRefunds()
    {
        var f = await QuestFixture(easy: "easy-coins", medium: "medium-coins", weekly1: "week-coins");
        await f.Ok("gift_open"); await f.Ok("tutorial_skip"); await f.Ok("achievement_claim", "tutorial_skip");
        var before = (await f.Service.LoadAsync()).Snapshot;
        Assert.All(before.Quests.Where(q => q.Event == "match_coins"), q => Assert.Equal(0, q.Progress));
        var begin = await f.Ok("begin_match", "durak", amount: 67);
        var draw = await f.Ok("report_match", "durak", text: begin.ActiveMatchId, amount: 1);
        Assert.All(draw.Quests.Where(q => q.Event == "match_coins"), q => Assert.Equal(20, q.Progress));
        begin = await f.Ok("begin_match", "durak", amount: 67);
        var won = await f.Ok("report_match", "durak", text: begin.ActiveMatchId, flag: true);
        Assert.Equal(234, won.Quests.Single(q => q.Tier == "medium").Progress); // 20 + 134 + 80 gross.
        Assert.Equal(100, won.Quests.Single(q => q.Tier == "easy").Progress);
        var claimed = await f.Ok("quest_claim", won.Quests.Single(q => q.Tier == "easy").Id);
        Assert.Equal(234, claimed.Quests.Single(q => q.Tier == "medium").Progress);
        var duplicate = await f.Ok("report_match", "durak", text: begin.ActiveMatchId, flag: true);
        Assert.Equal(234, duplicate.Quests.Single(q => q.Tier == "medium").Progress);
    }

    [Fact]
    public async Task StreakDrawLossAbandonAndDifferentGamesHaveIndependentProgress()
    {
        var f = await QuestFixture(medium: "medium-games");
        await f.Ok("report_match", "durak", flag: true);
        var draw = await f.Ok("report_match", "durak", amount: 1);
        Assert.Equal(1, draw.Quests.Single(q => q.Event == "streak").Progress);
        Assert.Single(draw.Quests.Single(q => q.Event == "different_games").Games);
        var loss = await f.Ok("report_match", "bluff");
        Assert.Equal(0, loss.Quests.Single(q => q.Event == "streak").Streak);
        Assert.Equal(2, loss.Quests.Single(q => q.Event == "different_games").Progress);
        await f.Ok("report_match", "durak", flag: true);
        var started = await f.Ok("begin_match", "poker"); var abandoned = await f.Ok("abandon_match", started.ActiveMatchId);
        Assert.Equal(0, abandoned.Quests.Single(q => q.Event == "streak").Progress);
        await f.Ok("report_match", "bluff", flag: true); var win = await f.Ok("report_match", "poker", flag: true);
        Assert.Equal(2, win.Quests.Single(q => q.Event == "streak").Progress);
        Assert.Equal(3, win.Quests.Single(q => q.Event == "different_wins").Progress);
        var frozen = await f.Ok("report_match", "chests");
        Assert.Equal(2, frozen.Quests.Single(q => q.Event == "streak").Progress);
        Assert.Equal(2, frozen.Quests.Single(q => q.Event == "different_games").Games.Count);
    }

    [Fact]
    public async Task StakedMatchesAndChestCountsUseCompletedGamesAndClampAtGoal()
    {
        var f = await QuestFixture(medium: "medium-stake", hard: "hard-chests", weekly1: "week-stake", weekly2: "week-chests");
        var started = await f.Ok("begin_match", "chests", amount: 10);
        var after = await f.Ok("report_match", "chests", text: started.ActiveMatchId, value: 13);
        Assert.Equal(1, after.Quests.Single(q => q.Tier == "medium").Progress);
        Assert.Equal(10, after.Quests.Single(q => q.Tier == "hard").Progress);
        Assert.Equal(13, after.Quests.Single(q => q.Template == "week-chests").Progress);
        started = await f.Ok("begin_match", "durak", amount: 10); after = await f.Ok("abandon_match", started.ActiveMatchId);
        Assert.Equal(1, after.Quests.Single(q => q.Template == "week-stake").Progress);
        await f.Ok("report_chests", amount: 13); after = await f.Ok("report_chests", amount: 13);
        Assert.Equal(30, after.Quests.Single(q => q.Template == "week-chests").Progress);
    }

    [Fact]
    public async Task TenDailyClaimsCountOnceAndConcurrentClaimsCannotDuplicateRewards()
    {
        var f = await QuestFixture();
        for (var i = 0; i < 10; i++)
        {
            var complete = await f.Ok("quests_debug_complete"); var easy = complete.Quests.Single(q => q.Tier == "easy");
            var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => f.Service.ExecuteAsync(Cmd("quest_claim", easy.Id))));
            Assert.Single(results, r => r.Success);
            Assert.Equal(i + 1, results.Single(r => r.Success).Snapshot.Quests.Single(q => q.Template == "week-dailies").Progress);
            f.Clock.Now = easy.ExpiresAt;
        }
        var final = (await f.Service.LoadAsync()).Snapshot;
        Assert.Equal(300 + 10 * HubQuest.EasyCoins, final.Profile.Coins);
        Assert.True(HubQuests.Ready(final.Quests.Single(q => q.Template == "week-dailies")));
    }

    private static async Task<Fixture> QuestFixture(string easy = "easy-durak", string medium = "medium-chests",
        string hard = "hard-streak", string weekly1 = "week-dailies", string weekly2 = "week-win-games")
    {
        var f = new Fixture(); await f.Service.LoadAsync(); var s = f.Store.Read();
        var templates = new[] { easy, medium, hard, weekly1, weekly2 };
        for (var i = 0; i < templates.Length; i++)
        {
            var t = HubQuests.Templates.Single(q => q.Id == templates[i]); var q = s.Quests.Single(q => q.Slot == HubQuests.Slots[i]);
            q.Template = t.Id; q.Name = t.Name; q.Event = t.Event; q.Game = t.Game; q.Goal = t.Goal;
            q.IssuedAt = f.Clock.Now; q.ExpiresAt = f.Clock.Now + (i < 3 ? HubQuest.DailySeconds : HubQuest.WeeklySeconds);
        }
        f.Store.Write(s); f.Service = new LocalPlayerHubService(f.Store, f.Clock); return f;
    }
}
