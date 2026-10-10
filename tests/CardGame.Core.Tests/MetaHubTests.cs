using System.Text.Json;
using CardGame.Core.Meta;

namespace CardGame.Core.Tests;

public sealed partial class MetaHubTests
{
    [Fact]
    public async Task FreshProfileIsSerializableAndReturnedSnapshotsCannotMutateService()
    {
        var f = new Fixture(); var loaded = await f.Service.LoadAsync();
        Assert.True(loaded.Success); Assert.Equal(300, loaded.Snapshot.Profile.Coins);
        Assert.Equal(18, loaded.Snapshot.Catalog.Count(i => i.Category == "avatar"));
        Assert.All(loaded.Snapshot.People, p => Assert.True(p.IsBot));
        loaded.Snapshot.Profile.Coins = 999999;
        loaded.Snapshot.Inventory.Clear();
        var again = await f.Service.LoadAsync();
        Assert.Equal(300, again.Snapshot.Profile.Coins); Assert.NotEmpty(again.Snapshot.Inventory);
        var second = new LocalPlayerHubService(f.Store, f.Clock);
        var roundtrip = await second.LoadAsync();
        Assert.Equal(again.Snapshot.Profile.Coins, roundtrip.Snapshot.Profile.Coins);
        Assert.Equal(again.Snapshot.Catalog.Count, roundtrip.Snapshot.Catalog.Count);
        Assert.Equal(again.Snapshot.Quests.Select(q => q.Id), roundtrip.Snapshot.Quests.Select(q => q.Id));
    }

    [Fact]
    public async Task IdempotentClaimsSurviveReloadAndIdCannotChangeMeaning()
    {
        var f = new Fixture(); await f.Ok("tutorial_skip");
        var command = Cmd("achievement_claim", "tutorial_skip", id: "claim-once");
        var first = await f.Service.ExecuteAsync(command); Assert.True(first.Success);
        var retry = await new LocalPlayerHubService(f.Store, f.Clock).ExecuteAsync(command);
        Assert.True(retry.Success); Assert.Equal(first.Snapshot.Profile.Coins, retry.Snapshot.Profile.Coins);
        Assert.Equal(first.Snapshot.Profile.XP, retry.Snapshot.Profile.XP);
        command.Target = "first_win";
        var conflict = await f.Service.ExecuteAsync(command);
        Assert.False(conflict.Success); Assert.Equal("id_conflict", conflict.Error);
        Assert.False((await f.Service.ExecuteAsync(Cmd("achievement_claim", "tutorial_skip"))).Success);
    }

    [Fact]
    public async Task FailedSaveRollsBackRewardsAndRequestCanBeSafelyRetried()
    {
        var f = new Fixture(); await f.Ok("tutorial_skip");
        var command = Cmd("achievement_claim", "tutorial_skip", id: "save-retry");
        f.Store.FailSave = true;
        var failed = await f.Service.ExecuteAsync(command);
        Assert.False(failed.Success); Assert.Equal(300, failed.Snapshot.Profile.Coins);
        Assert.False(failed.Snapshot.Achievements.Single(a => a.Id == "tutorial_skip").Claimed);
        f.Store.FailSave = false;
        var saved = await f.Service.ExecuteAsync(command);
        Assert.True(saved.Success); Assert.Equal(325, saved.Snapshot.Profile.Coins);
    }

    [Fact]
    public async Task ConcurrentSameRewardRequestsOnlyGrantOnce()
    {
        var f = new Fixture(); await f.Ok("tutorial_skip");
        var commands = Enumerable.Range(0, 12).Select(_ => f.Service.ExecuteAsync(Cmd("achievement_claim", "tutorial_skip"))).ToArray();
        var results = await Task.WhenAll(commands);
        Assert.Single(results, r => r.Success);
        Assert.Equal(325, (await f.Service.LoadAsync()).Snapshot.Profile.Coins);
    }

    [Fact]
    public async Task CosmeticsDoNotSellCoinsAndPremiumRewardsAreCosmeticsOnly()
    {
        var f = new Fixture(); await f.Ok("cosmetic_unlock", "frame_gold");
        var equipped = await f.Ok("equip", "frame_gold");
        Assert.Equal(300, equipped.Profile.Coins);
        Assert.Equal("frame_gold", equipped.Equipped.Single(e => e.Slot == "frame").ItemId);
        Assert.False((await f.Service.ExecuteAsync(Cmd("equip", "table_blue"))).Success);
        await f.Ok("pass_premium"); var pass = await f.Ok("pass_claim", "premium-1");
        Assert.Equal(300, pass.Profile.Coins);
        Assert.All(pass.Pass.Rewards.Where(r => r.Premium), r => { Assert.Equal(0, r.Coins); Assert.NotEmpty(r.ItemId); });
        Assert.False((await f.Service.ExecuteAsync(Cmd("buy_coins", amount: 100000))).Success);
    }

    [Fact]
    public async Task RoomStakeIsReservedOnceAndOutcomeCannotBeReportedTwice()
    {
        var f = new Fixture(); var room = await ReadyRoom(f, 67);
        var start = await f.Ok("start_room", id: "room-start");
        Assert.Equal(233, start.Profile.Coins);
        Assert.Equal(start.ActiveMatchId, start.Rooms.Single(r => r.Id == room).LocalMatchId);
        var secondStart = await f.Ok("start_room"); Assert.Equal(233, secondStart.Profile.Coins);
        var report = await f.Ok("report_match", "durak", text: start.ActiveMatchId, flag: true);
        Assert.Equal(447, report.Profile.Coins); //233 +134 stake payout +80 earned match reward.
        Assert.Equal(1, report.Profile.Matches);
        Assert.True(report.Achievements.Single(a => a.Id == "six_seven").Unlocked);
        var duplicate = await f.Ok("report_match", "durak", text: start.ActiveMatchId, flag: true);
        Assert.Equal(447, duplicate.Profile.Coins); Assert.Equal(1, duplicate.Profile.Matches);
        Assert.Empty(duplicate.ActiveMatchId); Assert.False(duplicate.Rooms.Single(r => r.Id == room).Started);
    }

    [Fact]
    public async Task AbandonedStakeIsForfeitedWithoutExperienceAndRoomCanBeLeft()
    {
        var f = new Fixture(); await ReadyRoom(f, 67); var started = await f.Ok("start_room");
        var abandoned = await f.Ok("abandon_match", started.ActiveMatchId);
        Assert.Equal(233, abandoned.Profile.Coins); Assert.Equal(0, abandoned.Profile.Matches); Assert.Equal(0, abandoned.Profile.XP);
        Assert.True(abandoned.Matches.Single().Abandoned);
        var staleWin = await f.Ok("report_match", "durak", text: started.ActiveMatchId, flag: true);
        Assert.Equal(233, staleWin.Profile.Coins);
        var left = await f.Ok("leave_room"); Assert.Empty(left.CurrentRoomId);
    }

    [Fact]
    public async Task NotReadyAndInsufficientWalletNeverPartiallyStartOrCharge()
    {
        var f = new Fixture(); await f.Ok("create_room", "durak", amount: 4, value: 400); await f.Ok("add_room_bot");
        Assert.Equal("ready", (await f.Service.ExecuteAsync(Cmd("start_room"))).Error);
        await f.Ok("room_ready", flag: true);
        var failed = await f.Service.ExecuteAsync(Cmd("start_room"));
        Assert.False(failed.Success); Assert.Equal("coins", failed.Error); Assert.Equal(300, failed.Snapshot.Profile.Coins);
        Assert.Empty(failed.Snapshot.ActiveMatchId); Assert.Empty(failed.Snapshot.Matches);
    }

    [Fact]
    public async Task LegacyLoanRequestDecidesImmediatelyAndCanBeRejectedWithoutPayingOut()
    {
        var f = new Fixture(); var rejected = await f.Ok("bank_request", amount: 1000);
        Assert.Equal("rejected", rejected.Bank.Status); Assert.Equal("limit", rejected.Bank.DecisionCode);
        Assert.Equal(300, rejected.Profile.Coins); Assert.Equal(0, rejected.Bank.Debt);
        Assert.Equal("limit", (await f.Ok("bank_review")).Bank.DecisionCode);
        var approved = await f.Ok("bank_request", amount: 100);
        Assert.Equal("approved", approved.Bank.Status);
        Assert.Equal(400, approved.Profile.Coins); Assert.Equal(120, approved.Bank.Debt); Assert.Equal(300, approved.Profile.CoinsEarned);
        var paid = await f.Ok("bank_repay", amount: 110);
        Assert.Equal(290, paid.Profile.Coins); Assert.Equal(0, paid.Bank.Debt); Assert.Equal("paid", paid.Bank.Status);
        Assert.False((await f.Service.ExecuteAsync(Cmd("bank_repay", amount: 1))).Success);
    }

    [Fact]
    public async Task OverdueCollectorsPreserveDebtAndWithholdHalfOfEveryIncomeSource()
    {
        var f = new Fixture(); await f.Service.LoadAsync();
        // A saved, valid low-balance indebted profile models money already spent on entry stakes.
        var saved = f.Store.Read(); saved.Profile.Coins = 0;
        saved.Bank.Status = "approved"; saved.Bank.Principal = 100; saved.Bank.Debt = 110;
        saved.Bank.DueAt = f.Clock.Now - 100; saved.Bank.CollectorAt = f.Clock.Now - 1;
        f.Store.Write(saved); f.Service = new LocalPlayerHubService(f.Store, f.Clock);
        var refreshed = (await f.Service.LoadAsync()).Snapshot;
        Assert.True(refreshed.Bank.CollectorPending);
        Assert.False((await f.Service.ExecuteAsync(Cmd("create_room", "durak", amount: 2, value: 1))).Success);
        var started = await f.Ok("play_collectors");
        var loss = await f.Ok("report_match", started.Bank.CollectorGame, text: started.ActiveMatchId, flag: false);
        Assert.Equal(0, loss.Profile.Coins); Assert.Equal(110, loss.Bank.Debt); Assert.True(loss.Bank.CollectionActive);
        await f.Ok("tutorial_skip"); var achievement = await f.Ok("achievement_claim", "tutorial_skip");
        Assert.Equal(13, achievement.Profile.Coins); Assert.Equal(98, achievement.Bank.Debt); //25 =>12 bank,13 player;carry1.
        var pass = await f.Ok("pass_claim", "free-1");
        Assert.Equal(30, pass.Profile.Coins); Assert.Equal(80, pass.Bank.Debt); //35 +remainder =>18 bank,17 player.
        Assert.False((await f.Service.ExecuteAsync(Cmd("bank_request", amount: 100))).Success);
    }

    [Fact]
    public async Task CollectorVictoryOnlyForgivesPartAndAbandonCountsAsLoss()
    {
        var f = new Fixture(); await f.Service.LoadAsync();
        var saved = f.Store.Read(); saved.Profile.Coins = 5; saved.Bank.Debt = 100; saved.Bank.Status = "collection";
        saved.Bank.CollectorPending = true; saved.Bank.DueAt = f.Clock.Now - 10; saved.Bank.CollectorAt = f.Clock.Now - 5;
        f.Store.Write(saved); f.Service = new LocalPlayerHubService(f.Store, f.Clock);
        var started = await f.Ok("play_collectors");
        var won = await f.Ok("report_match", started.Bank.CollectorGame, text: started.ActiveMatchId, flag: true);
        Assert.Equal(35, won.Bank.Debt); //100 -25 forgiven -40 from normal80 match income.
        Assert.Equal(45, won.Profile.Coins); Assert.True(won.Bank.CollectionActive);
        Assert.False(won.Bank.CollectorPending);
        Assert.Equal(1, won.Profile.Wins);
    }

    [Fact]
    public async Task ChestsEventsQuestsAndPeriodsUseClockAndDoNotLeakPreviousDayClaims()
    {
        var f = await QuestFixture(); var one = await f.Ok("report_match", "chests", value: 5);
        Assert.True(one.Achievements.Single(a => a.Id == "mentalist").Unlocked);
        Assert.Equal(5, one.Quests.Single(q => q.Event == "chests").Progress);
        await f.Ok("report_match", "durak"); var three = await f.Ok("report_match", "durak");
        var daily = three.Quests.Single(q => q.Tier == "easy");
        Assert.Equal(daily.Goal, daily.Progress);
        var claimed = await f.Ok("quest_claim", daily.Id); Assert.True(claimed.Quests.Single(q => q.Id == daily.Id).Claimed);
        f.Clock.Now = daily.ExpiresAt;
        var next = (await f.Service.LoadAsync()).Snapshot;
        Assert.DoesNotContain(next.Quests, q => q.Id == daily.Id);
        Assert.Equal(0, next.Quests.Single(q => q.Tier == "easy").Progress);
        Assert.False((await f.Service.ExecuteAsync(Cmd("quest_claim", daily.Id))).Success);
    }

    [Fact]
    public async Task SixGiftOpensRightAwayThenEveryTwelveHours()
    {
        var f = new Fixture();
        Assert.Equal(0, (await f.Service.LoadAsync()).Snapshot.Gift.ReadyAt);
        var opened = await f.Ok("gift_open");
        Assert.Equal(300 + HubGift.Coins, opened.Profile.Coins);
        Assert.Equal(300 + HubGift.Coins, opened.Profile.CoinsEarned);
        Assert.Equal(f.Clock.Now + HubGift.Cooldown, opened.Gift.ReadyAt);
        Assert.Equal(1, opened.Gift.Opened); Assert.Equal(HubGift.Coins, opened.Gift.LastCoins);
        var early = await f.Service.ExecuteAsync(Cmd("gift_open"));
        Assert.False(early.Success); Assert.Equal("gift", early.Error);
        Assert.Equal(300 + HubGift.Coins, early.Snapshot.Profile.Coins);
        f.Clock.Now += HubGift.Cooldown - 1;
        Assert.False((await f.Service.ExecuteAsync(Cmd("gift_open"))).Success);
        f.Clock.Now += 1;
        var again = await f.Ok("gift_open");
        Assert.Equal(300 + 2 * HubGift.Coins, again.Profile.Coins); Assert.Equal(2, again.Gift.Opened);
    }

    [Fact]
    public async Task NewsKeepsTheNewestReadIssue()
    {
        var f = new Fixture();
        Assert.Equal(0, (await f.Service.LoadAsync()).Snapshot.NewsSeen);
        Assert.Equal(3, (await f.Ok("news_read", amount: 3)).NewsSeen);
        Assert.Equal(3, (await f.Ok("news_read", amount: 2)).NewsSeen);
        Assert.False((await f.Service.ExecuteAsync(Cmd("news_read"))).Success);
    }

    [Fact]
    public async Task FriendLettersStayUnreadUntilTheConversationIsOpened()
    {
        var f = new Fixture();
        Assert.True((await f.Service.LoadAsync()).Snapshot.Chats.Single(m => m.Id == "welcome-chat").Unread);
        var sent = await f.Ok("chat_send", "bot-yegor", "Привет");
        Assert.False(sent.Chats.Single(m => m.FromId == sent.Profile.Id).Unread);
        Assert.Equal(2, sent.Chats.Count(m => m.ConversationId == "bot-yegor" && m.Unread));
        var again = await f.Ok("chat_send", "bot-yegor", "Ещё");
        var replies = again.Chats.Where(m => m.FromId == "bot-yegor" && m.Id.StartsWith("reply-")).Select(m => m.Text).ToArray();
        Assert.Equal(2, replies.Distinct().Count());
        Assert.DoesNotContain(replies, text => text.Contains("[Локальный бот]"));
        var read = await f.Ok("chat_read", "bot-yegor");
        Assert.DoesNotContain(read.Chats, m => m.Unread);
        Assert.False((await f.Service.ExecuteAsync(Cmd("chat_read"))).Success);
    }

    [Fact]
    public async Task TestCommandMakesTheOpenedGiftReadyAgain()
    {
        var f = new Fixture(); await f.Ok("gift_open");
        Assert.False((await f.Service.ExecuteAsync(Cmd("gift_open"))).Success);
        await f.Ok("gift_debug_ready");
        Assert.Equal(300 + 2 * HubGift.Coins, (await f.Ok("gift_open")).Profile.Coins);
    }

    [Fact]
    public async Task SaveFromBeforeTheGiftLoadsWithTheGiftReady()
    {
        var f = new Fixture(); await f.Service.LoadAsync();
        var old = System.Text.Json.Nodes.JsonNode.Parse(f.Store.Json!)!.AsObject();
        Assert.True(old.Remove("Gift"));
        f.Store.Json = old.ToJsonString();
        var loaded = await new LocalPlayerHubService(f.Store, f.Clock).LoadAsync();
        Assert.True(loaded.Success); Assert.NotNull(loaded.Snapshot.Gift); Assert.Equal(0, loaded.Snapshot.Gift.ReadyAt);
    }

    [Fact]
    public async Task PendalistOpensOnlyForFinishedChestsMatchWithoutChests()
    {
        var f = new Fixture();
        Assert.False((await f.Ok("report_match", "chests", value: 2)).Achievements.Single(a => a.Id == "pendalist").Unlocked);
        Assert.False((await f.Ok("report_match", "durak")).Achievements.Single(a => a.Id == "pendalist").Unlocked);
        var none = await f.Ok("report_match", "chests", value: 0);
        Assert.True(none.Achievements.Single(a => a.Id == "pendalist").Unlocked);
    }

    [Fact]
    public async Task AbandonedChestsMatchDoesNotOpenPendalist()
    {
        var f = new Fixture();
        await f.Ok("create_room", "chests", text: "Тест", amount: 4);
        await f.Ok("add_room_bot"); await f.Ok("add_room_bot"); await f.Ok("room_ready", flag: true);
        var started = await f.Ok("start_room");
        await f.Ok("abandon_match", started.ActiveMatchId);
        var late = await f.Ok("report_match", "chests", text: started.ActiveMatchId, value: 0);
        Assert.False(late.Achievements.Single(a => a.Id == "pendalist").Unlocked);
    }

    [Fact]
    public async Task SavesFromBeforeNewAchievementsReceiveThemOnLoad()
    {
        var f = new Fixture(); await f.Service.LoadAsync();
        var saved = f.Store.Read(); saved.Achievements.RemoveAll(a => a.Id == "pendalist"); f.Store.Write(saved);
        f.Service = new LocalPlayerHubService(f.Store, f.Clock);
        var none = await f.Ok("report_match", "chests", value: 0);
        Assert.True(none.Achievements.Single(a => a.Id == "pendalist").Unlocked);
    }

    [Fact]
    public async Task LocalSocialRequestsInvitesChatAndConsumablesAreFunctional()
    {
        var f = new Fixture(); var accepted = await f.Ok("friend_accept", "request-danil"); Assert.Contains("bot-danil", accepted.Friends);
        // Местный бот принимает заявку сразу — живых игроков пока нет.
        var response = await f.Ok("friend_request", "bot-aslan", id: "request-aslan");
        Assert.Contains("bot-aslan", response.Friends);
        Assert.Equal("accepted", response.FriendRequests.Single(r => r.Id == "friend-request-aslan").Status);
        var chat = await f.Ok("chat_send", "bot-aslan", "Привет");
        Assert.Equal(2, chat.Chats.Count(m => m.ConversationId == "bot-aslan"));
        Assert.All(chat.Chats, m => Assert.True(m.IsLocalMock));
        await f.Ok("create_room", "poker", amount: 6);
        // Приглашённый бот-друг садится за стол сразу, готовым.
        var joined = await f.Ok("invite_friend", "bot-aslan", id: "invite-aslan");
        Assert.Contains(joined.Rooms.Single(r => r.Id == joined.CurrentRoomId).Members, m => m.PlayerId == "bot-aslan" && m.Ready);
        Assert.Equal("accepted", joined.Invites.Single(i => i.Id == "invite-invite-aslan").Status);
        Assert.False((await f.Service.ExecuteAsync(Cmd("invite_friend", "bot-aslan"))).Success);
        var thrown = await f.Ok("tomato_throw", "bot-aslan"); Assert.Equal(2, thrown.Inventory.Single(i => i.ItemId == "tomato_pack").Quantity);
        Assert.Equal("cooldown", (await f.Service.ExecuteAsync(Cmd("tomato_throw", "bot-aslan"))).Error);
        f.Clock.Now += 2; var second = await f.Ok("tomato_throw", "bot-aslan"); Assert.Equal(1, second.Inventory.Single(i => i.ItemId == "tomato_pack").Quantity);
    }

    [Fact]
    public async Task PlayingWithAFriendSeatsThemAtANewTableAndTablemateAsksForFriendship()
    {
        var f = new Fixture();
        await f.Ok("create_room", "durak", amount: 4, id: "old");
        // «Позвать играть»: свой закрытый стол выбранной игры, друг садится сразу; прежний стол покинут.
        var durak = await f.Ok("friend_play", "bot-yegor", "durak", id: "play");
        var table = durak.Rooms.Single(r => r.Id == durak.CurrentRoomId);
        Assert.Equal("room-play", table.Id);
        Assert.True(table.Private && table.Stake == 0 && table.GameId == "durak" && table.HostId == durak.Profile.Id);
        Assert.Equal(new[] { durak.Profile.Id, "bot-yegor" }, table.Members.Select(m => m.PlayerId));
        Assert.DoesNotContain(durak.Rooms, r => r.Id == "room-old");
        // В играх от трёх свободное место до минимума занимает местный бот, лучше — ещё не друг.
        var chests = await f.Ok("friend_play", "bot-ali", "chests", id: "chests");
        var chestsTable = chests.Rooms.Single(r => r.Id == chests.CurrentRoomId);
        Assert.Equal(3, chestsTable.Members.Count);
        var stranger = chestsTable.Members.Last().PlayerId;
        Assert.DoesNotContain(stranger, chests.Friends);
        Assert.False((await f.Service.ExecuteAsync(Cmd("friend_play", "bot-aslan", "durak"))).Success);
        Assert.False((await f.Service.ExecuteAsync(Cmd("friend_play", "bot-ali", "checkers"))).Success);
        // Сыграли — сосед по столу, ещё не друг, просится в друзья; один раз за партию.
        await f.Ok("room_ready", flag: true);
        var started = await f.Ok("start_room", id: "chests-game");
        var finished = await f.Ok("report_match", "chests", text: started.ActiveMatchId, value: 2);
        var request = finished.FriendRequests.Single(r => r.FromId == stranger && r.Status == "pending");
        Assert.Equal("chests", request.Game);
        Assert.Equal(1, finished.FriendRequests.Count(r => r.FromId == stranger));
        await f.Ok("friend_decline", request.Id);
        // Отказал — после следующей партии не просится снова.
        await f.Ok("room_ready", flag: true);
        var again = await f.Ok("start_room", id: "chests-again");
        var after = await f.Ok("report_match", "chests", text: again.ActiveMatchId, value: 1);
        Assert.Equal(1, after.FriendRequests.Count(r => r.FromId == stranger));
    }

    [Fact]
    public async Task OutgoingFriendRequestCanBeCancelled()
    {
        var f = new Fixture(); await f.Service.LoadAsync();
        var saved = f.Store.Read();
        saved.People.Single(p => p.Id == "bot-marat").IsBot = false;
        f.Store.Write(saved);
        f.Service = new LocalPlayerHubService(f.Store, f.Clock);
        // Живой игрок (позже, по сети) отвечает не сразу: заявка ждёт, её можно отменить.
        var sent = await f.Ok("friend_request", "bot-marat", id: "marat");
        Assert.Equal("pending", sent.FriendRequests.Single(r => r.Id == "friend-marat").Status);
        Assert.DoesNotContain("bot-marat", sent.Friends);
        var cancelled = await f.Ok("friend_cancel", "friend-marat");
        Assert.Equal("cancelled", cancelled.FriendRequests.Single(r => r.Id == "friend-marat").Status);
        Assert.False((await f.Service.ExecuteAsync(Cmd("friend_cancel", "request-danil"))).Success);
    }

    [Fact]
    public async Task QuickMatchEntryPreventsDuplicateCompletionAndCannotBypassDebtStakeRules()
    {
        var f = new Fixture(); var started = await f.Ok("begin_match", "poker", id: "quick-game");
        Assert.Equal("quick-game", started.ActiveMatchId);
        Assert.False((await f.Service.ExecuteAsync(Cmd("begin_match", "durak"))).Success);
        var finished = await f.Ok("report_match", "poker", text: "quick-game", flag: true);
        Assert.Equal(1, finished.Profile.Wins);
        var retry = await f.Ok("report_match", "poker", text: "quick-game", flag: true);
        Assert.Equal(finished.Profile.Coins, retry.Profile.Coins);
        Assert.False((await f.Service.ExecuteAsync(Cmd("report_match", "durak", text: "quick-game"))).Success);
    }

    [Fact]
    public async Task TutorialSwitchResetsLessonAndClockDoesNotExtendOfflineLoanDueDate()
    {
        var f = new Fixture(); await f.Ok("tutorial_next"); await f.Ok("tutorial_next");
        var changed = await f.Ok("tutorial_select", "poker");
        Assert.Equal("poker", changed.Tutorial.ActiveGame); Assert.Equal(0, changed.Tutorial.Step);
        await f.Ok("tutorial_next"); var completed = await f.Ok("tutorial_complete", "poker");
        Assert.Contains("poker", completed.Tutorial.CompletedGames); Assert.Equal(0, completed.Tutorial.Step);
        var request = await f.Ok("bank_request", amount: 100);
        f.Clock.Now += 4 * 86400;
        var overdue = (await f.Service.LoadAsync()).Snapshot;
        Assert.Equal(request.Bank.ReviewAt + 3 * 86400, overdue.Bank.DueAt);
        Assert.True(overdue.Bank.CollectorPending); Assert.Equal(120, overdue.Bank.Debt);
    }

    [Fact]
    public async Task ExistingVersionOneProfilesGainOnlyMissingCatalogDefaults()
    {
        var f = new Fixture(); await f.Ok("cosmetic_unlock", "frame_gold"); await f.Ok("equip", "frame_gold");
        await f.Ok("unequip", "eyes");
        var saved = f.Store.Read(); var categories = new[] { "cardface", "chest", "trail", "emote" };
        var removedIds = saved.Catalog.Where(i => categories.Contains(i.Category)).Select(i => i.Id).ToArray();
        saved.Catalog.RemoveAll(i => removedIds.Contains(i.Id));
        saved.Inventory.RemoveAll(i => removedIds.Contains(i.ItemId));
        saved.Equipped.RemoveAll(i => categories.Contains(i.Slot));
        saved.Profile.Coins = 432; saved.Profile.DisplayName = "Сохранённое имя";
        saved.Inventory.Single(i => i.ItemId == "tomato_pack").Quantity = 17;
        f.Store.Write(saved);
        f.Service = new LocalPlayerHubService(f.Store, f.Clock);
        var migrated = (await f.Service.LoadAsync()).Snapshot;
        Assert.Equal(432, migrated.Profile.Coins); Assert.Equal("Сохранённое имя", migrated.Profile.DisplayName);
        Assert.Equal(17, migrated.Inventory.Single(i => i.ItemId == "tomato_pack").Quantity);
        Assert.Equal("frame_gold", migrated.Equipped.Single(e => e.Slot == "frame").ItemId);
        Assert.Equal("", migrated.Equipped.Single(e => e.Slot == "eyes").ItemId);
        foreach (var category in categories)
        {
            Assert.Equal(2, migrated.Catalog.Count(i => i.Category == category));
            var basic = migrated.Catalog.Single(i => i.Category == category && i.IsDefault);
            Assert.Equal(basic.Id, migrated.Equipped.Single(e => e.Slot == category).ItemId);
            Assert.Equal(1, migrated.Inventory.Single(i => i.ItemId == basic.Id).Quantity);
            Assert.DoesNotContain(migrated.Inventory, i => i.ItemId == migrated.Catalog.Single(c => c.Category == category && !c.IsDefault).Id);
        }
        var roundtrip = (await new LocalPlayerHubService(f.Store, f.Clock).LoadAsync()).Snapshot;
        Assert.Equal(migrated.Catalog.Count, roundtrip.Catalog.Count);
        Assert.Equal(migrated.Inventory.Count, roundtrip.Inventory.Count);
    }

    [Fact]
    public async Task JoiningBotHostedDemoRoomCanReadyAndStart()
    {
        var f = new Fixture(); await f.Ok("join_room", "room-demo-durak");
        await f.Ok("room_ready", flag: true);
        var started = await f.Ok("start_room");
        var room = started.Rooms.Single(r => r.Id == started.CurrentRoomId);
        Assert.Equal("bot-yegor", room.HostId); Assert.True(room.Started);
        Assert.NotEmpty(started.ActiveMatchId); Assert.Equal(300, started.Profile.Coins);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PayingEntireDebtDuringCollectorGameNeverStrandsActiveMatch(bool abandon)
    {
        var f = new Fixture(); await SetCollector(f, 150, 100);
        var started = await f.Ok("play_collectors");
        await f.Ok("bank_repay", amount: 100);
        var done = abandon ? await f.Ok("abandon_match", started.ActiveMatchId)
            : await f.Ok("report_match", started.Bank.CollectorGame, text: started.ActiveMatchId);
        Assert.Empty(done.ActiveMatchId); Assert.Equal(0, done.Bank.Debt);
        Assert.False(done.Bank.CollectorPending); Assert.False(done.Bank.CollectionActive);
        Assert.True(done.Matches.Single(m => m.Id == started.ActiveMatchId).Completed);
        Assert.Equal(50, done.Profile.Coins);
    }

    [Fact]
    public async Task DrawRefundsReservedStakeWithoutWinLossOrRatingChange()
    {
        var f = new Fixture(); await ReadyRoom(f, 67); var started = await f.Ok("start_room");
        var draw = await f.Ok("report_match", "durak", text: started.ActiveMatchId, amount: 1);
        Assert.Equal(320, draw.Profile.Coins); Assert.Equal(1, draw.Profile.Matches);
        Assert.Equal(0, draw.Profile.Wins); Assert.Equal(0, draw.Profile.Losses); Assert.Equal(1000, draw.Profile.Rating);
        Assert.Equal(20, draw.Profile.XP); Assert.Empty(draw.ActiveMatchId);
        Assert.All(draw.Quests.Where(q => q.Event == "win" || q.Event == "different_wins" || q.Event == "streak"), q => Assert.Equal(0, q.Progress));
        var retry = await f.Ok("report_match", "durak", text: started.ActiveMatchId, amount: 1);
        Assert.Equal(320, retry.Profile.Coins); Assert.Equal(1, retry.Profile.Matches);
    }

    [Fact]
    public async Task CollectorDrawKeepsRematchAndWithholdsOnlyNormalParticipationIncome()
    {
        var f = new Fixture(); await SetCollector(f, 15, 100);
        var started = await f.Ok("play_collectors");
        var draw = await f.Ok("report_match", started.Bank.CollectorGame, text: started.ActiveMatchId, amount: 1);
        Assert.Equal(90, draw.Bank.Debt); Assert.Equal(25, draw.Profile.Coins); // Half of the ordinary 20-coin participation payout repays debt.
        Assert.True(draw.Bank.CollectorPending); Assert.False(draw.Bank.CollectionActive); Assert.Empty(draw.ActiveMatchId);
        Assert.Equal(0, draw.Profile.Wins); Assert.Equal(0, draw.Profile.Losses);
        var refreshed = (await f.Service.LoadAsync()).Snapshot;
        Assert.Equal(started.Bank.CollectorGame, refreshed.Bank.CollectorGame);
        var rematch = await f.Ok("play_collectors");
        Assert.NotEqual(started.ActiveMatchId, rematch.ActiveMatchId);
        Assert.Equal(started.Bank.CollectorGame, rematch.Matches.Single(m => m.Id == rematch.ActiveMatchId).Game);
    }

    [Theory]
    [InlineData("durak", 36, false, 2)]
    [InlineData("bluff", 36, true, 2)]
    [InlineData("chests", 36, false, 3)]
    [InlineData("govno", 0, false, 3)]
    [InlineData("poker", 52, false, 2)]
    public async Task RoomDefaultsMatchExistingGamesAndPersist(string game, int deck, bool shootout, int minimum)
    {
        var f = new Fixture(); var created = await f.Ok("create_room", game, amount: minimum);
        var rules = created.Rooms.Single(r => r.Id == created.CurrentRoomId).GameSettings!;
        Assert.Equal(deck, rules.DeckSize); Assert.Equal(shootout, rules.Shootout);
        Assert.False(rules.AllowTransfer); Assert.False(rules.NeighboursOnly); Assert.False(rules.AllowCheating); Assert.False(rules.Chaos);
        Assert.Equal(minimum, HubGameRules.MinimumPlayers(game));
        var roundtrip = (await new LocalPlayerHubService(f.Store, f.Clock).LoadAsync()).Snapshot;
        Assert.Equal(JsonSerializer.Serialize(rules, new JsonSerializerOptions { IncludeFields = true }),
            JsonSerializer.Serialize(roundtrip.Rooms.Single(r => r.Id == created.CurrentRoomId).GameSettings, new JsonSerializerOptions { IncludeFields = true }));
    }

    [Fact]
    public async Task CustomRulesAreCopiedAtCommandSnapshotAndStoreBoundaries()
    {
        var f = new Fixture(); var settings = new HubGameSettings { DeckSize = 54, AllowTransfer = true, NeighboursOnly = true, AllowCheating = true };
        var created = await f.Ok("create_room", "durak", amount: 6, settings: settings);
        var returnedRules = created.Rooms.Single(r => r.Id == created.CurrentRoomId).GameSettings!;
        Assert.True(returnedRules.AllowTransfer); Assert.True(returnedRules.NeighboursOnly); Assert.True(returnedRules.AllowCheating);
        settings.DeckSize = 24; settings.AllowTransfer = false;
        returnedRules.DeckSize = 36; returnedRules.AllowCheating = false;
        var stored = (await f.Service.LoadAsync()).Snapshot.Rooms.Single(r => r.Id == created.CurrentRoomId).GameSettings!;
        Assert.Equal(54, stored.DeckSize); Assert.True(stored.AllowTransfer); Assert.True(stored.AllowCheating);
        var copied = stored.Copy(); copied.NeighboursOnly = false;
        Assert.True(stored.NeighboursOnly);
        var roundtrip = (await new LocalPlayerHubService(f.Store, f.Clock).LoadAsync()).Snapshot;
        Assert.Equal(54, roundtrip.Rooms.Single(r => r.Id == created.CurrentRoomId).GameSettings!.DeckSize);
    }

    [Theory]
    [InlineData("durak", 24, 5, "capacity")]
    [InlineData("durak", 24, 6, "capacity")]
    [InlineData("durak", 35, 4, "deck")]
    [InlineData("bluff", 54, 4, "deck")]
    [InlineData("bluff", 36, 1, "capacity")]
    [InlineData("chests", 24, 3, "deck")]
    [InlineData("chests", 54, 3, "deck")]
    [InlineData("chests", 36, 2, "capacity")]
    [InlineData("govno", 36, 4, "deck")]
    [InlineData("govno", 0, 2, "capacity")]
    [InlineData("poker", 54, 4, "deck")]
    [InlineData("poker", 52, 7, "capacity")]
    public async Task InvalidRulesCannotReplaceTheCurrentRoom(string game, int deck, int capacity, string error)
    {
        var f = new Fixture(); var before = await f.Ok("create_room", "durak", amount: 4);
        var rules = HubGameSettings.DefaultFor(game); rules.DeckSize = deck;
        var failed = await f.Service.ExecuteAsync(Cmd("create_room", game, amount: capacity, settings: rules));
        Assert.False(failed.Success); Assert.Equal(error, failed.Error);
        Assert.Equal(before.CurrentRoomId, failed.Snapshot.CurrentRoomId);
        Assert.Equal(before.Rooms.Count, failed.Snapshot.Rooms.Count);
        Assert.Equal(before.Profile.Coins, failed.Snapshot.Profile.Coins);
        Assert.Equal(before.Revision, failed.Snapshot.Revision);
    }

    [Theory]
    [InlineData("durak", 24, 4)]
    [InlineData("durak", 54, 6)]
    [InlineData("bluff", 24, 6)]
    [InlineData("chests", 52, 6)]
    [InlineData("govno", 0, 6)]
    [InlineData("poker", 52, 6)]
    public async Task SupportedDeckAndCapacityCombinationsAreAccepted(string game, int deck, int count)
    {
        var f = new Fixture(); var rules = HubGameSettings.DefaultFor(game); rules.DeckSize = deck;
        var created = await f.Ok("create_room", game, amount: count, settings: rules);
        Assert.Equal(count, created.Rooms.Single(r => r.Id == created.CurrentRoomId).MaxPlayers);
        Assert.Contains(deck, HubGameRules.DeckSizes(game));
        Assert.Equal(count, HubGameRules.MaximumPlayers(game, rules));
    }

    [Fact]
    public async Task RulesFromAnotherGameAreRejectedInsteadOfSilentlyIgnored()
    {
        var f = new Fixture(); var rules = HubGameSettings.DefaultFor("bluff"); rules.AllowTransfer = true;
        Assert.Equal("rules", (await f.Service.ExecuteAsync(Cmd("create_room", "bluff", amount: 4, settings: rules))).Error);
        rules = HubGameSettings.DefaultFor("durak"); rules.Shootout = true;
        Assert.Equal("rules", (await f.Service.ExecuteAsync(Cmd("create_room", "durak", amount: 4, settings: rules))).Error);
        rules.Shootout = false; rules.Chaos = true;
        Assert.Equal("rules", (await f.Service.ExecuteAsync(Cmd("create_room", "durak", amount: 4, settings: rules))).Error);
    }

    [Fact]
    public async Task EditingRoomPersistsRulesAndResetsReadinessBeforeBotsCanReconfirm()
    {
        var f = new Fixture(); await ReadyRoom(f, 0);
        var rules = HubGameSettings.DefaultFor("durak"); rules.AllowTransfer = true; rules.DeckSize = 24;
        var changed = await f.Ok("update_room_settings", text: "Новые правила", amount: 3, flag: true, value: 20, settings: rules);
        var room = changed.Rooms.Single(r => r.Id == changed.CurrentRoomId);
        Assert.Equal("Новые правила", room.Name); Assert.Equal(3, room.MaxPlayers); Assert.True(room.Private); Assert.Equal(20, room.Stake);
        Assert.True(room.GameSettings!.AllowTransfer); Assert.Equal(24, room.GameSettings.DeckSize);
        Assert.All(room.Members, member => Assert.False(member.Ready));
        Assert.Equal("ready", (await f.Service.ExecuteAsync(Cmd("start_room"))).Error);
        var ready = await f.Ok("room_ready", flag: true);
        Assert.All(ready.Rooms.Single(r => r.Id == ready.CurrentRoomId).Members, member => Assert.True(member.Ready));
        var started = await f.Ok("start_room"); Assert.Equal(280, started.Profile.Coins);
        Assert.Equal(24, started.Rooms.Single(r => r.Id == started.CurrentRoomId).GameSettings!.DeckSize);
        Assert.Equal("started", (await f.Service.ExecuteAsync(Cmd("update_room_settings", amount: 4, settings: rules))).Error);
    }

    [Fact]
    public async Task EditingOnlyRoomNameAndAccessPreservesReadiness()
    {
        var f = new Fixture(); await ReadyRoom(f, 0);
        var changed = await f.Ok("update_room_settings", text: "Другая вывеска", amount: 4, flag: true);
        var room = changed.Rooms.Single(r => r.Id == changed.CurrentRoomId);
        Assert.All(room.Members, member => Assert.True(member.Ready));
        Assert.Equal("Другая вывеска", room.Name); Assert.True(room.Private);
    }

    [Fact]
    public async Task EditingRoomRequiresHostAndCannotRemoveOccupiedSeats()
    {
        var f = new Fixture(); await f.Ok("join_room", "room-demo-durak");
        var denied = await f.Service.ExecuteAsync(Cmd("update_room_settings", text: "Чужой стол", amount: 4));
        Assert.Equal("host", denied.Error);
        await f.Ok("create_room", "durak", amount: 4); await f.Ok("add_room_bot"); await f.Ok("add_room_bot");
        var failed = await f.Service.ExecuteAsync(Cmd("update_room_settings", amount: 2));
        Assert.Equal("capacity", failed.Error);
        var room = failed.Snapshot.Rooms.Single(r => r.Id == failed.Snapshot.CurrentRoomId);
        Assert.Equal(4, room.MaxPlayers); Assert.Equal(3, room.Members.Count);
    }

    [Fact]
    public async Task FailedRoomEditSaveKeepsRulesReadinessAndReceiptUnchanged()
    {
        var f = new Fixture(); await ReadyRoom(f, 0);
        var rules = HubGameSettings.DefaultFor("durak"); rules.AllowCheating = true;
        var command = Cmd("update_room_settings", amount: 4, settings: rules, id: "settings-save-retry");
        f.Store.FailSave = true;
        var failed = await f.Service.ExecuteAsync(command); Assert.Equal("storage", failed.Error);
        var room = failed.Snapshot.Rooms.Single(r => r.Id == failed.Snapshot.CurrentRoomId);
        Assert.False(room.GameSettings!.AllowCheating); Assert.All(room.Members, member => Assert.True(member.Ready));
        Assert.DoesNotContain(failed.Snapshot.Receipts, receipt => receipt.Id == command.Id);
        f.Store.FailSave = false;
        var retried = await f.Service.ExecuteAsync(command); Assert.True(retried.Success);
        Assert.True(retried.Snapshot.Rooms.Single(r => r.Id == retried.Snapshot.CurrentRoomId).GameSettings!.AllowCheating);
    }

    [Fact]
    public async Task ChangingGameRestoresItsDefaultsAndRemovesOldGameRules()
    {
        var f = new Fixture(); var rules = HubGameSettings.DefaultFor("durak"); rules.AllowCheating = rules.AllowTransfer = true;
        await f.Ok("create_room", "durak", amount: 4, settings: rules); await f.Ok("add_room_bot"); await f.Ok("room_ready", flag: true);
        var changed = await f.Ok("set_room_game", "bluff");
        var room = changed.Rooms.Single(r => r.Id == changed.CurrentRoomId);
        Assert.Equal("bluff", room.GameId); Assert.Equal(36, room.GameSettings!.DeckSize); Assert.True(room.GameSettings.Shootout);
        Assert.False(room.GameSettings.AllowCheating); Assert.False(room.GameSettings.AllowTransfer);
        Assert.All(room.Members, member => Assert.False(member.Ready));
        await f.Ok("create_room", "durak", amount: 2);
        Assert.Equal("capacity", (await f.Service.ExecuteAsync(Cmd("set_room_game", "chests"))).Error);
    }

    [Fact]
    public async Task LegacyRoomsGainDefaultsAndChestsCapacityWithoutDroppingMembers()
    {
        var f = new Fixture(); await ReadyRoom(f, 0); var saved = f.Store.Read();
        var oldRoom = saved.Rooms.Single(r => r.Id == saved.CurrentRoomId);
        oldRoom.GameId = "chests"; oldRoom.GameSettings = null; oldRoom.MaxPlayers = 2;
        foreach (var room in saved.Rooms) room.GameSettings = null;
        f.Store.Write(saved); f.Service = new LocalPlayerHubService(f.Store, f.Clock);
        var loaded = await f.Service.LoadAsync(); Assert.True(loaded.Success);
        var migrated = loaded.Snapshot.Rooms.Single(r => r.Id == loaded.Snapshot.CurrentRoomId);
        Assert.Equal(3, migrated.MaxPlayers); Assert.Equal(36, migrated.GameSettings!.DeckSize);
        Assert.Equal(oldRoom.Members.Select(m => m.PlayerId), migrated.Members.Select(m => m.PlayerId));
        Assert.All(loaded.Snapshot.Rooms, room => Assert.NotNull(room.GameSettings));
        Assert.Equal("players", (await f.Service.ExecuteAsync(Cmd("start_room"))).Error);
        await f.Ok("add_room_bot"); Assert.NotEmpty((await f.Ok("start_room")).ActiveMatchId);
        Assert.Equal(3, f.Store.Read().Rooms.Single(r => r.Id == saved.CurrentRoomId).MaxPlayers);
    }

    [Fact]
    public async Task StartRejectsInvalidPersistedRulesBeforeReservingAnyStake()
    {
        var f = new Fixture(); await ReadyRoom(f, 67); var saved = f.Store.Read();
        saved.Rooms.Single(r => r.Id == saved.CurrentRoomId).GameSettings!.DeckSize = 25;
        f.Store.Write(saved); f.Service = new LocalPlayerHubService(f.Store, f.Clock);
        var failed = await f.Service.ExecuteAsync(Cmd("start_room"));
        Assert.Equal("deck", failed.Error); Assert.Equal(300, failed.Snapshot.Profile.Coins);
        Assert.Empty(failed.Snapshot.ActiveMatchId); Assert.Empty(failed.Snapshot.Matches);
    }

    [Fact]
    public async Task SettingsParticipateInIdempotencyAndLegacyReceiptsStillReplay()
    {
        var f = new Fixture(); var command = Cmd("create_room", "bluff", amount: 4, id: "custom-room", settings: HubGameSettings.DefaultFor("bluff"));
        var first = await f.Service.ExecuteAsync(command); Assert.True(first.Success);
        var replay = await new LocalPlayerHubService(f.Store, f.Clock).ExecuteAsync(command); Assert.True(replay.Success);
        command.GameSettings!.Shootout = false;
        Assert.Equal("id_conflict", (await f.Service.ExecuteAsync(command)).Error);

        var legacy = Cmd("create_room", "poker", amount: 4, id: "legacy-room");
        var created = await f.Service.ExecuteAsync(legacy); Assert.True(created.Success);
        var receipt = created.Snapshot.Receipts.Single(r => r.Id == legacy.Id);
        static string Part(string value) => value.Length + ":" + value;
        Assert.Equal(Part(legacy.Action) + Part(legacy.Target) + Part(legacy.Text) + Part(legacy.Amount.ToString()) + Part(legacy.Flag.ToString()) + Part(legacy.Value.ToString()), receipt.Fingerprint);
        Assert.True((await new LocalPlayerHubService(f.Store, f.Clock).ExecuteAsync(legacy)).Success);
    }

    private static async Task SetCollector(Fixture f, int coins, int debt)
    {
        await f.Service.LoadAsync(); var saved = f.Store.Read();
        saved.Profile.Coins = coins; saved.Bank.Debt = debt; saved.Bank.Status = "collection";
        saved.Bank.CollectorPending = true; saved.Bank.CollectorGame = "poker";
        saved.Bank.DueAt = f.Clock.Now - 10; saved.Bank.CollectorAt = f.Clock.Now - 5;
        f.Store.Write(saved); f.Service = new LocalPlayerHubService(f.Store, f.Clock);
    }

    private static async Task<string> ReadyRoom(Fixture f, int stake)
    {
        var created = await f.Ok("create_room", "durak", text: "Тест", amount: 4, value: stake);
        await f.Ok("add_room_bot"); await f.Ok("room_ready", flag: true); return created.CurrentRoomId;
    }

    private static HubCommand Cmd(string action, string target = "", string text = "", int amount = 0, bool flag = false, int value = 0, string? id = null, HubGameSettings? settings = null)
        => new() { Id = id ?? Guid.NewGuid().ToString("N"), Action = action, Target = target, Text = text, Amount = amount, Flag = flag, Value = value, GameSettings = settings };

    private sealed class Fixture
    {
        internal readonly MemoryStore Store = new(); internal readonly FakeClock Clock = new(); internal LocalPlayerHubService Service;
        internal Fixture() => Service = new LocalPlayerHubService(Store, Clock);
        internal async Task<HubSnapshot> Ok(string action, string target = "", string text = "", int amount = 0, bool flag = false, int value = 0, string? id = null, HubGameSettings? settings = null)
        {
            var result = await Service.ExecuteAsync(Cmd(action, target, text, amount, flag, value, id, settings));
            Assert.True(result.Success, action + ": " + result.Error + " " + result.Message); return result.Snapshot;
        }
    }

    private sealed class FakeClock : IHubClock { internal long Now = 1801000000; public long UnixSeconds => Now; }
    private sealed class MemoryStore : IHubStore
    {
        private static readonly JsonSerializerOptions Options = new() { IncludeFields = true };
        internal string? Json; internal bool FailSave;
        internal HubSnapshot Read() => JsonSerializer.Deserialize<HubSnapshot>(Json!, Options)!;
        internal void Write(HubSnapshot snapshot) => Json = JsonSerializer.Serialize(snapshot, Options);
        public Task<HubSnapshot?> LoadAsync() => Task.FromResult(Json == null ? null : Read());
        public Task SaveAsync(HubSnapshot snapshot) { if (FailSave) throw new IOException("Simulated failed write"); Write(snapshot); return Task.CompletedTask; }
    }
}
