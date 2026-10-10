using CardGame.Core.Meta;

namespace CardGame.Core.Tests;

public sealed partial class MetaHubTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task UnityEmptyNestedOfferIsIssuedAsTheNextRealContract(int previousSequence)
    {
        var f = new Fixture(); await f.Service.LoadAsync(); var saved = f.Store.Read();
        saved.Bank.OfferSequence = previousSequence;
        // JsonUtility reconstructs serialized null nested classes with default field values.
        saved.Bank.Offer = new HubLoanOffer();
        f.Store.Write(saved); f.Service = new LocalPlayerHubService(f.Store, f.Clock);
        var offered = await f.Ok("bank_offer"); var offer = offered.Bank.Offer!;
        Assert.False(string.IsNullOrWhiteSpace(offer.Id));
        Assert.Equal(previousSequence + 1, offer.Number);
        Assert.Equal(previousSequence + 1, offered.Bank.OfferSequence);
        if (offer.Number == 1) Assert.Empty(offer.Trick);
        Assert.Contains(offer.Trick, new[] { "", "rate", "fee", "short" });
        var signed = await f.Ok("bank_sign", text: offer.Id, amount: 100);
        Assert.Equal("approved", signed.Bank.DecisionCode); Assert.Equal(400, signed.Profile.Coins);
    }

    [Fact]
    public async Task ExactLoanAmountSurvivesReloadAndStillRespectsThePreciseLimit()
    {
        var f = new Fixture(); await f.Service.LoadAsync(); var saved = f.Store.Read();
        saved.Profile.Matches = 2; saved.Profile.Level = 3; saved.Profile.XP = 200;
        saved.IncomeDays.Single().Gross = 116; // 116 x 3 = 348, below the level cap.
        f.Store.Write(saved); f.Service = new LocalPlayerHubService(f.Store, f.Clock);
        var offered = await f.Ok("bank_offer");
        Assert.Equal(348, offered.Bank.CreditLimit);
        var signed = await f.Ok("bank_sign", text: offered.Bank.Offer!.Id, amount: 347);
        Assert.Equal("approved", signed.Bank.DecisionCode);
        Assert.Equal(347, signed.Bank.Principal); Assert.Equal(347, signed.Bank.RequestedAmount);
        Assert.Equal(417, signed.Bank.Debt); // Ordinary 20% rounds upward: 69.4 becomes 70.
        Assert.Equal(382, HubLoans.PayoffQuote(signed.Bank, f.Clock.Now).Amount);
        Assert.Equal(647, signed.Profile.Coins); Assert.Equal(300, signed.Profile.CoinsEarned);
        f.Service = new LocalPlayerHubService(f.Store, f.Clock);
        var reloaded = await f.Service.LoadAsync(); Assert.True(reloaded.Success);
        Assert.Equal(347, reloaded.Snapshot.Bank.Principal); Assert.Equal(417, reloaded.Snapshot.Bank.Debt);
        Assert.Equal(647, reloaded.Snapshot.Profile.Coins);
        await f.Ok("bank_repay", amount: 382);
        var next = await f.Ok("bank_offer");
        var rejected = await f.Ok("bank_sign", text: next.Bank.Offer!.Id, amount: 349);
        Assert.Equal(348, rejected.Bank.CreditLimit); Assert.Equal("limit", rejected.Bank.DecisionCode);
        Assert.Equal(0, rejected.Bank.Debt); Assert.Equal(265, rejected.Profile.Coins);
    }

    [Fact]
    public async Task LoanSignsImmediatelyAndPrincipalIsNotAnEarningOrQuestEvent()
    {
        var f = new Fixture(); var offered = await f.Ok("bank_offer");
        var before = offered.Profile.Coins;
        var earned = offered.Profile.CoinsEarned; var xp = offered.Profile.XP;
        var questProgress = offered.Quests.Select(q => q.Progress).ToArray();
        var result = await f.Service.ExecuteAsync(Cmd("bank_sign", text: offered.Bank.Offer!.Id, amount: 100));
        Assert.True(result.Success); Assert.Empty(result.Error);
        var s = result.Snapshot;
        Assert.Equal("approved", s.Bank.Status); Assert.Equal("approved", s.Bank.DecisionCode);
        Assert.Equal(before + 100, s.Profile.Coins); Assert.Equal(earned, s.Profile.CoinsEarned);
        Assert.Equal(xp, s.Profile.XP); Assert.Equal(questProgress, s.Quests.Select(q => q.Progress));
        Assert.Equal(100, s.Bank.Principal); Assert.Equal(120, s.Bank.Debt);
        Assert.Equal(f.Clock.Now + 3 * 86400, s.Bank.DueAt);
        Assert.Equal(s.Bank.DueAt, s.Bank.CollectorAt);
        Assert.Null(s.Bank.Offer); Assert.Empty(s.Bank.Trick);
    }

    [Theory]
    [InlineData(201, "limit")]
    [InlineData(200, "history")]
    public async Task LoanRefusalsArePersistedDecisionsAndDoNotPayOut(int amount, string code)
    {
        var f = new Fixture(); var offered = await f.Ok("bank_offer");
        var result = await f.Service.ExecuteAsync(Cmd("bank_sign", text: offered.Bank.Offer!.Id, amount: amount));
        Assert.True(result.Success); Assert.Empty(result.Error);
        Assert.Equal(code, result.Snapshot.Bank.DecisionCode);
        Assert.Equal("rejected", result.Snapshot.Bank.Status);
        Assert.Equal(300, result.Snapshot.Profile.Coins); Assert.Equal(0, result.Snapshot.Bank.Debt);
        Assert.Null(result.Snapshot.Bank.Offer); Assert.NotEmpty(result.Snapshot.Bank.Decision);
        var reloaded = await new LocalPlayerHubService(f.Store, f.Clock).LoadAsync();
        Assert.Equal(code, reloaded.Snapshot.Bank.DecisionCode);
        Assert.Equal(result.Snapshot.Bank.Decision, reloaded.Snapshot.Bank.Decision);
    }

    [Theory]
    [InlineData(false, "debt")]
    [InlineData(true, "collector")]
    public async Task ExistingDebtRefusalPreservesTheActiveLoan(bool collector, string code)
    {
        var f = new Fixture(); var offered = await f.Ok("bank_offer");
        var existing = offered.Bank;
        existing.Principal = 100; existing.Debt = 110; existing.Trick = "short";
        existing.DueAt = f.Clock.Now + (collector ? -86400 : 86400);
        existing.CollectorAt = existing.DueAt + 43200;
        existing.CollectorPending = collector;
        existing.Status = collector ? "collection" : "approved";
        f.Store.Write(offered); f.Service = new LocalPlayerHubService(f.Store, f.Clock);
        var result = await f.Service.ExecuteAsync(Cmd("bank_sign", text: existing.Offer!.Id, amount: 100));
        Assert.True(result.Success); Assert.Equal(code, result.Snapshot.Bank.DecisionCode);
        Assert.Equal(110, result.Snapshot.Bank.Debt); Assert.Equal(100, result.Snapshot.Bank.Principal);
        Assert.Equal("short", result.Snapshot.Bank.Trick);
        Assert.Equal(existing.DueAt, result.Snapshot.Bank.DueAt);
        Assert.Equal(existing.Status, result.Snapshot.Bank.Status);
        Assert.Equal(300, result.Snapshot.Profile.Coins);
    }

    [Fact]
    public async Task TwoCompletedMatchesAllowTheWholeCreditLimit()
    {
        var f = new Fixture(); await f.Ok("report_match", "durak"); await f.Ok("report_match", "poker");
        var offer = await f.Ok("bank_offer");
        var approved = await f.Ok("bank_sign", text: offer.Bank.Offer!.Id, amount: offer.Bank.CreditLimit);
        Assert.Equal("approved", approved.Bank.DecisionCode);
        Assert.Equal(offer.Bank.CreditLimit, approved.Bank.Principal);
        Assert.Equal(offer.Profile.Coins + offer.Bank.CreditLimit, approved.Profile.Coins);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-50)]
    [InlineData(99)]
    public async Task InvalidLoanAmountLeavesTheOfferAvailable(int amount)
    {
        var f = new Fixture(); var offer = await f.Ok("bank_offer");
        var failed = await f.Service.ExecuteAsync(Cmd("bank_sign", text: offer.Bank.Offer!.Id, amount: amount));
        Assert.False(failed.Success); Assert.Equal("amount", failed.Error);
        Assert.Equal(offer.Bank.Offer.Id, failed.Snapshot.Bank.Offer!.Id);
        Assert.Equal(offer.Revision, failed.Snapshot.Revision); Assert.Equal(300, failed.Snapshot.Profile.Coins);
    }

    [Fact]
    public async Task OffersPersistAcrossReopensWithVariedBoundedTricksAndAnHonestFirstContract()
    {
        var first = await LoanOfferSequence("local-player", 60);
        var second = await LoanOfferSequence("local-player", 60);
        Assert.Equal(first, second);
        Assert.Equal(60, first.Select(x => x.id).Distinct().Count());
        Assert.Empty(first[0].trick);
        var tricky = first.Where(x => x.trick.Length > 0).ToArray();
        Assert.Equal(new[] { "fee", "rate", "short" }, tricky.Select(x => x.trick).Distinct().OrderBy(x => x));
        Assert.InRange(tricky[0].number, 2, 4);
        var gaps = tricky.Skip(1).Select((x, i) => x.number - tricky[i].number).ToArray();
        Assert.All(gaps, gap => Assert.InRange(gap, 2, 4));
        Assert.Equal(new[] { 2, 3, 4 }, gaps.Distinct().OrderBy(x => x));
        foreach (var item in first) Assert.Contains(item.trick, new[] { "", "rate", "fee", "short" });
        var other = await LoanOfferSequence("another-player", 30);
        Assert.NotEqual(first[0].id, other[0].id);
        Assert.NotEqual(first.Take(30).Select(x => x.trick), other.Select(x => x.trick));
        Assert.Empty(other[0].trick);
    }

    private static async Task<List<(string id, int number, string trick)>> LoanOfferSequence(string playerId, int count)
    {
        var f = new Fixture(); await f.Service.LoadAsync();
        var saved = f.Store.Read(); saved.Profile.Id = playerId;
        f.Store.Write(saved); f.Service = new LocalPlayerHubService(f.Store, f.Clock);
        var sequence = new List<(string, int, string)>();
        for (var number = 1; number <= count; number++)
        {
            var offered = await f.Ok("bank_offer"); var offer = offered.Bank.Offer!;
            Assert.Equal(number, offer.Number); Assert.Equal(number, offered.Bank.OfferSequence);
            var reopened = await new LocalPlayerHubService(f.Store, f.Clock).ExecuteAsync(Cmd("bank_offer"));
            Assert.True(reopened.Success); Assert.Equal(offer.Id, reopened.Snapshot.Bank.Offer!.Id);
            Assert.Equal(offer.Trick, reopened.Snapshot.Bank.Offer.Trick);
            sequence.Add((offer.Id, offer.Number, offer.Trick));
            await f.Ok("bank_sign", text: offer.Id, amount: 1000000); // A persisted refusal consumes the paper.
        }
        return sequence;
    }

    [Fact]
    public async Task StaleOfferCannotBeSignedOrReadAndDoubleClicksPayOnlyOnce()
    {
        var f = new Fixture(); var offer = (await f.Ok("bank_offer")).Bank.Offer!;
        var command = Cmd("bank_sign", text: offer.Id, amount: 100, id: "loan-once");
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => f.Service.ExecuteAsync(command)));
        Assert.All(results, r => { Assert.True(r.Success); Assert.Equal(400, r.Snapshot.Profile.Coins); });
        var reload = new LocalPlayerHubService(f.Store, f.Clock);
        Assert.Equal(400, (await reload.ExecuteAsync(command)).Snapshot.Profile.Coins);
        foreach (var action in new[] { "bank_sign", "bank_read_fine", "bank_challenge_fine" })
        {
            var invalid = await reload.ExecuteAsync(Cmd(action, text: offer.Id, amount: 100));
            Assert.False(invalid.Success); Assert.Equal("offer", invalid.Error);
            Assert.Equal(400, invalid.Snapshot.Profile.Coins); Assert.Equal(120, invalid.Snapshot.Bank.Debt);
        }
        var conflict = await reload.ExecuteAsync(Cmd("bank_sign", text: offer.Id, amount: 200, id: "loan-once"));
        Assert.False(conflict.Success); Assert.Equal("id_conflict", conflict.Error);
    }

    [Fact]
    public async Task FailedLoanSaveCanRetryWithoutLosingOfferOrDuplicatingCoins()
    {
        var f = new Fixture(); var offer = (await f.Ok("bank_offer")).Bank.Offer!;
        var command = Cmd("bank_sign", text: offer.Id, amount: 100);
        f.Store.FailSave = true;
        var failed = await f.Service.ExecuteAsync(command);
        Assert.False(failed.Success); Assert.Equal("storage", failed.Error);
        Assert.Equal(300, failed.Snapshot.Profile.Coins); Assert.Equal(0, failed.Snapshot.Bank.Debt);
        Assert.Equal(offer.Id, failed.Snapshot.Bank.Offer!.Id);
        f.Store.FailSave = false;
        var retried = await f.Service.ExecuteAsync(command);
        Assert.True(retried.Success); Assert.Equal(400, retried.Snapshot.Profile.Coins);
        Assert.Equal(400, (await f.Service.ExecuteAsync(command)).Snapshot.Profile.Coins);
    }

    [Theory]
    [InlineData("rate", 40, 3, 142)]
    [InlineData("fee", 20, 3, 142)]
    [InlineData("daily", 60, 3, 162)]
    [InlineData("double", 20, 3, 122)]
    [InlineData("short", 20, 2, 122)]
    public async Task SignedFinePrintChangesExactlyTheAgreedTerms(string trick, int interest, int days, int debt)
    {
        var f = new Fixture(); var offer = await PrepareLoanTrick(f, trick);
        var signed = await f.Ok("bank_sign", text: offer.Id, amount: 101);
        Assert.Equal("approved", signed.Bank.Status); Assert.Equal(trick, signed.Bank.Trick);
        Assert.Equal(interest, signed.Bank.InterestPercent); Assert.Equal(debt, signed.Bank.Debt);
        Assert.Equal(401, signed.Profile.Coins); Assert.Equal(101, signed.Bank.Principal);
        Assert.Equal(f.Clock.Now + days * 86400L, signed.Bank.DueAt);
        Assert.Equal(signed.Bank.DueAt, signed.Bank.CollectorAt);
    }

    [Fact]
    public async Task DoubleFinePrintDoublesRemainingDebtExactlyOnceAtTheDeadline()
    {
        var f = new Fixture(); var offer = await PrepareLoanTrick(f, "double");
        var signed = await f.Ok("bank_sign", text: offer.Id, amount: 100);
        await f.Ok("bank_repay", amount: 10);
        f.Clock.Now = signed.Bank.DueAt - 1;
        Assert.Equal(110, (await f.Service.LoadAsync()).Snapshot.Bank.Debt);
        f.Clock.Now++;
        f.Store.FailSave = true;
        var failed = await f.Service.LoadAsync();
        Assert.False(failed.Success); Assert.Equal(110, failed.Snapshot.Bank.Debt);
        f.Store.FailSave = false;
        var overdue = (await f.Service.LoadAsync()).Snapshot;
        Assert.Equal(220, overdue.Bank.Debt); Assert.True(overdue.Bank.OverdueDoubled);
        Assert.Equal("collection", overdue.Bank.Status);
        f.Clock.Now = signed.Bank.CollectorAt;
        var reloaded = (await new LocalPlayerHubService(f.Store, f.Clock).LoadAsync()).Snapshot;
        Assert.Equal(220, reloaded.Bank.Debt); Assert.True(reloaded.Bank.CollectorPending);
        await f.Ok("bank_repay", amount: 220);
        f.Clock.Now += 86400;
        Assert.Equal(0, (await f.Service.LoadAsync()).Snapshot.Bank.Debt);
    }

    [Theory]
    [InlineData("rate")]
    [InlineData("fee")]
    [InlineData("short")]
    [InlineData("daily")]
    [InlineData("double")]
    public async Task ReadingOnlyInspectsAndSpecificChallengePersistsAndUnlocksOneStandardReward(string trick)
    {
        var f = new Fixture(); var offer = await PrepareLoanTrick(f, trick);
        var unread = await f.Service.ExecuteAsync(Cmd("bank_challenge_fine", trick, text: offer.Id));
        Assert.False(unread.Success); Assert.Equal("fine_print", unread.Error);
        var read = await f.Ok("bank_read_fine", text: offer.Id);
        Assert.True(read.Bank.Offer!.FineRead); Assert.False(read.Bank.Offer.Revealed);
        Assert.Equal(trick, read.Bank.Offer.Trick); Assert.Equal(trick, read.Bank.Offer.OriginalTrick);
        Assert.False(read.Achievements.Single(a => a.Id == "fine_print").Unlocked);
        foreach (var reason in new[] { "", "honest", trick == "short" ? "fee" : "short" })
        {
            var wrong = await f.Service.ExecuteAsync(Cmd("bank_challenge_fine", reason, text: offer.Id));
            Assert.False(wrong.Success); Assert.Equal("fine_print", wrong.Error);
            Assert.Equal(trick, wrong.Snapshot.Bank.Offer!.Trick);
            Assert.False(wrong.Snapshot.Bank.Offer.Revealed);
        }
        var challenged = await f.Ok("bank_challenge_fine", trick, text: offer.Id);
        Assert.True(challenged.Bank.Offer!.Revealed); Assert.Empty(challenged.Bank.Offer.Trick);
        Assert.Equal(trick, challenged.Bank.Offer.OriginalTrick);
        var achievement = challenged.Achievements.Single(a => a.Id == "fine_print");
        Assert.True(achievement.Unlocked); Assert.False(achievement.Claimed);
        Assert.Equal(50, achievement.RewardCoins); Assert.Equal(30, achievement.RewardXP);
        Assert.Equal(300, challenged.Profile.Coins); // Standard achievement reward is claimed separately.
        f.Service = new LocalPlayerHubService(f.Store, f.Clock);
        var reopened = await f.Ok("bank_offer");
        Assert.True(reopened.Bank.Offer!.Revealed); Assert.Equal(offer.Id, reopened.Bank.Offer.Id);
        Assert.Equal(trick, reopened.Bank.Offer.OriginalTrick);
        var claimed = await f.Ok("achievement_claim", "fine_print");
        Assert.Equal(350, claimed.Profile.Coins); Assert.Equal(30, claimed.Profile.XP);
        await f.Ok("bank_read_fine", text: offer.Id);
        var repeated = await f.Ok("bank_challenge_fine", trick, text: offer.Id);
        Assert.Equal(350, repeated.Profile.Coins); Assert.Equal(1, repeated.Achievements.Single(a => a.Id == "fine_print").Progress);
        var signed = await f.Ok("bank_sign", text: offer.Id, amount: 100);
        Assert.Equal(120, signed.Bank.Debt); Assert.Empty(signed.Bank.Trick);
        Assert.Equal(trick, signed.Bank.Contract!.OriginalTrick); Assert.True(signed.Bank.Contract.TrickChallenged);
        Assert.Equal(f.Clock.Now + 3 * 86400L, signed.Bank.DueAt);
        await f.Ok("bank_repay", amount: 110);
        var next = await f.Ok("bank_offer");
        Assert.Empty(next.Bank.Offer!.Trick); // An exposed trick still counts; never trick on the very next offer.
        var later = await PrepareLoanTrick(f, "short");
        await f.Ok("bank_read_fine", text: later.Id);
        await f.Ok("bank_challenge_fine", "short", text: later.Id);
        Assert.False((await f.Service.ExecuteAsync(Cmd("achievement_claim", "fine_print"))).Success);
    }

    [Fact]
    public async Task HonestNotesCanBeReadButCannotFarmFinePrintAchievement()
    {
        var f = new Fixture(); var offer = (await f.Ok("bank_offer")).Bank.Offer!;
        var read = await f.Ok("bank_read_fine", text: offer.Id);
        Assert.True(read.Bank.Offer!.FineRead); Assert.Empty(read.Bank.Offer.Trick);
        var challenge = await f.Service.ExecuteAsync(Cmd("bank_challenge_fine", "rate", text: offer.Id));
        Assert.False(challenge.Success); Assert.Equal("fine_print", challenge.Error);
        Assert.False(challenge.Snapshot.Achievements.Single(a => a.Id == "fine_print").Unlocked);
        Assert.False(challenge.Snapshot.Bank.Offer!.Revealed); Assert.Equal(300, challenge.Snapshot.Profile.Coins);
    }

    [Fact]
    public async Task FailedChallengeSaveKeepsOriginalTermsAndRetryCorrectsExactlyOnce()
    {
        var f = new Fixture(); var offer = await PrepareLoanTrick(f, "fee");
        await f.Ok("bank_read_fine", text: offer.Id);
        var command = Cmd("bank_challenge_fine", "fee", text: offer.Id, id: "fix-fee");
        f.Store.FailSave = true;
        var failed = await f.Service.ExecuteAsync(command);
        Assert.False(failed.Success); Assert.Equal("storage", failed.Error);
        Assert.Equal("fee", failed.Snapshot.Bank.Offer!.Trick); Assert.False(failed.Snapshot.Bank.Offer.Revealed);
        Assert.False(failed.Snapshot.Achievements.Single(a => a.Id == "fine_print").Unlocked);
        f.Store.FailSave = false;
        var fixedOffer = await f.Service.ExecuteAsync(command); Assert.True(fixedOffer.Success);
        Assert.True(fixedOffer.Snapshot.Bank.Offer!.Revealed);
        var reloaded = new LocalPlayerHubService(f.Store, f.Clock);
        var retry = await reloaded.ExecuteAsync(command); Assert.True(retry.Success);
        Assert.Equal(1, retry.Snapshot.Achievements.Single(a => a.Id == "fine_print").Progress);
        Assert.Equal(300, retry.Snapshot.Profile.Coins);
    }

    [Theory]
    [InlineData(-4 * 86400, 100, "approved")]
    [InlineData(30, 100, "approved")]
    [InlineData(30, 200, "history")]
    [InlineData(30, 1000, "limit")]
    public async Task OldPendingSaveResolvesOnLoadAndKeepsElapsedReviewDate(long reviewOffset, int amount, string decision)
    {
        var f = new Fixture(); await f.Service.LoadAsync(); var saved = f.Store.Read();
        saved.Bank.Status = "pending"; saved.Bank.RequestedAmount = amount;
        saved.Bank.ReviewAt = f.Clock.Now + reviewOffset;
        saved.Achievements.RemoveAll(a => a.Id == "fine_print");
        f.Store.Write(saved); f.Service = new LocalPlayerHubService(f.Store, f.Clock);
        var result = await f.Service.LoadAsync(); Assert.True(result.Success);
        Assert.NotEqual("pending", result.Snapshot.Bank.Status);
        Assert.Equal(decision, result.Snapshot.Bank.DecisionCode);
        Assert.Single(result.Snapshot.Achievements, a => a.Id == "fine_print");
        Assert.Equal(decision == "approved" ? 400 : 300, result.Snapshot.Profile.Coins);
        if (decision == "approved")
        {
            Assert.Equal(f.Clock.Now + Math.Min(0, reviewOffset) + 3 * 86400, result.Snapshot.Bank.DueAt);
            Assert.Equal(reviewOffset < -3 * 86400, result.Snapshot.Bank.CollectorPending);
            Assert.Equal(110, result.Snapshot.Bank.Debt);
        }
        var again = await new LocalPlayerHubService(f.Store, f.Clock).LoadAsync();
        Assert.Equal(result.Snapshot.Profile.Coins, again.Snapshot.Profile.Coins);
    }

    [Fact]
    public async Task LegacyLoanWithholdsHalfOfEveryEarningBeforeCollectorsAndCarriesOddCoins()
    {
        var f = new Fixture(); await f.Service.LoadAsync(); var legacy = f.Store.Read();
        legacy.Profile.Coins = 400; legacy.Bank.Principal = 100; legacy.Bank.Debt = 110;
        legacy.Bank.Status = "approved"; legacy.Bank.ReviewAt = f.Clock.Now;
        legacy.Bank.DueAt = f.Clock.Now + 3 * 86400; legacy.Bank.CollectorAt = legacy.Bank.DueAt + 43200;
        f.Store.Write(legacy); f.Service = new LocalPlayerHubService(f.Store, f.Clock);
        var signed = (await f.Service.LoadAsync()).Snapshot;
        Assert.Equal(1, signed.Bank.TermsVersion);
        Assert.Equal(400, signed.Profile.Coins); Assert.False(signed.Bank.CollectionActive);
        await f.Ok("tutorial_skip");
        var first = await f.Ok("achievement_claim", "tutorial_skip");
        Assert.Equal(413, first.Profile.Coins); Assert.Equal(98, first.Bank.Debt);
        Assert.Equal(1, first.Bank.IncomeRemainder); Assert.Equal(325, first.Profile.CoinsEarned);
        var second = await f.Ok("pass_claim", "free-1");
        Assert.Equal(430, second.Profile.Coins); Assert.Equal(80, second.Bank.Debt);
        Assert.Equal(0, second.Bank.IncomeRemainder);
        var gift = await f.Ok("gift_open");
        Assert.Equal(455, gift.Profile.Coins); Assert.Equal(55, gift.Bank.Debt);
        Assert.False(gift.Bank.CollectorPending); Assert.False(gift.Bank.CollectionActive);
        var game = await f.Ok("report_match", "durak", flag: true);
        Assert.Equal(495, game.Profile.Coins); Assert.Equal(15, game.Bank.Debt);
        var paid = await f.Ok("report_match", "durak", flag: true);
        Assert.Equal(560, paid.Profile.Coins); Assert.Equal(0, paid.Bank.Debt);
        Assert.Equal("paid", paid.Bank.Status); Assert.Equal(0, paid.Bank.IncomeRemainder);
        Assert.Equal(110, paid.Bank.Repaid);
    }

    private static async Task<HubLoanOffer> PrepareLoanTrick(Fixture f, string trick)
    {
        await f.Service.LoadAsync(); var saved = f.Store.Read();
        saved.Profile.Matches = 2;
        saved.Bank.OfferSequence = Math.Max(3, saved.Bank.OfferSequence + 1);
        saved.Bank.Offer = new HubLoanOffer { Id = "fixture-" + saved.Bank.OfferSequence, Number = saved.Bank.OfferSequence, Trick = trick };
        f.Store.Write(saved); f.Service = new LocalPlayerHubService(f.Store, f.Clock);
        return (await f.Service.LoadAsync()).Snapshot.Bank.Offer!;
    }
}
