using CardGame.Core.Meta;

namespace CardGame.Core.Tests;

public sealed partial class MetaHubTests
{
    [Fact]
    public async Task SignedLoanCapturesImmutableContractAndKeepsItAfterProfileChanges()
    {
        var (f, signed) = await NewTermsLoan(); var contract = signed.Bank.Contract!;
        Assert.Equal(2, signed.Bank.TermsVersion); Assert.Equal(20, signed.Bank.InterestPercent);
        Assert.Equal(1200, signed.Bank.Debt); Assert.Equal(1200, contract.TotalDebt);
        Assert.Equal(1, contract.Number); Assert.StartsWith("loan-1-", contract.Id);
        Assert.Equal(signed.Bank.SignedAt, contract.SignedAt); Assert.Equal(f.Clock.Now, contract.SignedAt);
        Assert.Equal(1000, contract.Principal); Assert.Equal(2100, contract.CreditLimit);
        Assert.Equal(signed.Bank.DueAt, contract.OriginalDueAt); Assert.Equal(2, contract.TermsVersion);
        Assert.Equal(signed.Profile.DisplayName, contract.Borrower); Assert.Single(signed.Bank.Contracts);
        Assert.Equal(contract.Id, signed.Bank.Contracts[0].Id);
        await f.Ok("profile_update", text: "Другое имя");
        f.Clock.Now += 8 * 86400;
        var loaded = await new LocalPlayerHubService(f.Store, f.Clock).LoadAsync();
        Assert.True(loaded.Success); Assert.Equal(100, loaded.Snapshot.Bank.CreditLimit);
        var persisted = loaded.Snapshot.Bank.Contract!;
        Assert.Equal(contract.Borrower, persisted.Borrower); Assert.Equal(contract.CreditLimit, persisted.CreditLimit);
        Assert.Equal(contract.SignedAt, persisted.SignedAt); Assert.Equal(contract.OriginalDueAt, persisted.OriginalDueAt);
        Assert.Equal(contract.TotalDebt, persisted.TotalDebt); Assert.Equal("collection", persisted.Status);
    }

    [Theory]
    [InlineData(86399, true, 1099, 100)]
    [InlineData(86400, false, 1199, 0)]
    public async Task EarlyPayoffUsesExactTwentyFourHourBoundaryAfterPartialPayment(long elapsed, bool early, int amount, int discount)
    {
        var (f, signed) = await NewTermsLoan();
        var paid = await f.Ok("bank_repay", amount: 1);
        Assert.Equal(1199, paid.Bank.Debt); Assert.Equal(1, paid.Bank.Repaid);
        Assert.Equal(1299, paid.Profile.Coins);
        f.Clock.Now = signed.Bank.SignedAt + elapsed;
        var quote = HubLoans.PayoffQuote((await f.Service.LoadAsync()).Snapshot.Bank, f.Clock.Now);
        Assert.Equal(early, quote.Early); Assert.Equal(amount, quote.Amount); Assert.Equal(discount, quote.Discount);
    }

    [Fact]
    public async Task EarlyFinalPaymentRecordsActualPaidAmountDiscountAndSurvivesRetryAndReload()
    {
        var (f, signed) = await NewTermsLoan(principal: 347);
        await f.Ok("bank_repay", amount: 1);
        var quote = HubLoans.PayoffQuote((await f.Service.LoadAsync()).Snapshot.Bank, f.Clock.Now);
        Assert.Equal(381, quote.Amount); Assert.Equal(35, quote.Discount); Assert.True(quote.Early);
        var command = Cmd("bank_repay", amount: quote.Amount, id: "early-close");
        f.Store.FailSave = true;
        var failed = await f.Service.ExecuteAsync(command);
        Assert.False(failed.Success); Assert.Equal(416, failed.Snapshot.Bank.Debt);
        Assert.Single(failed.Snapshot.Bank.Payments);
        f.Store.FailSave = false;
        var completed = await f.Service.ExecuteAsync(command); Assert.True(completed.Success);
        var bank = completed.Snapshot.Bank; var receipt = bank.Payments.Last();
        Assert.Equal(0, bank.Debt); Assert.Equal(382, bank.Repaid); Assert.Equal("paid", bank.Status);
        Assert.Equal(265, completed.Snapshot.Profile.Coins);
        Assert.Equal(command.Id, receipt.Id); Assert.Equal(signed.Bank.Contract!.Id, receipt.ContractId);
        Assert.Equal(381, receipt.Amount); Assert.Equal(1, receipt.PaidBefore); Assert.Equal(382, receipt.PaidAfter);
        Assert.Equal(416, receipt.DebtBefore); Assert.Equal(0, receipt.DebtAfter); Assert.Equal(35, receipt.Discount);
        Assert.True(receipt.Early); Assert.True(receipt.Closed); Assert.Equal("payment", receipt.Source);
        Assert.Equal(f.Clock.Now, receipt.At); Assert.Equal("paid", bank.Contract!.Status);
        Assert.Equal(417, bank.Contract.TotalDebt); Assert.Equal(signed.Bank.Contract.OriginalDueAt, bank.Contract.OriginalDueAt);
        f.Service = new LocalPlayerHubService(f.Store, f.Clock);
        var retried = await f.Service.ExecuteAsync(command); Assert.True(retried.Success);
        Assert.Equal(265, retried.Snapshot.Profile.Coins); Assert.Equal(2, retried.Snapshot.Bank.Payments.Count);
        var offer = (await f.Ok("bank_offer")).Bank.Offer!;
        await f.Ok("bank_sign", text: offer.Id, amount: 1000000);
        var read = (await f.Service.LoadAsync()).Snapshot;
        Assert.Equal("paid", read.Bank.Contract!.Status); Assert.Equal(417, read.Bank.Contract.TotalDebt);
    }

    [Theory]
    [InlineData("rate", 1400, 300)]
    [InlineData("fee", 1400, 300)]
    [InlineData("daily", 1600, 500)]
    [InlineData("short", 1200, 100)]
    [InlineData("double", 1200, 100)]
    public async Task EarlyTenPercentClosureAppliesToEveryFinePrintVariant(string trick, int ordinary, int discount)
    {
        var (f, signed) = await NewTermsLoan(trick);
        Assert.Equal(ordinary, signed.Bank.Debt);
        var quote = HubLoans.PayoffQuote(signed.Bank, f.Clock.Now);
        Assert.True(quote.Early); Assert.Equal(1100, quote.Amount); Assert.Equal(discount, quote.Discount);
        var paid = await f.Ok("bank_repay", amount: 1100);
        Assert.Equal(0, paid.Bank.Debt); Assert.Equal(200, paid.Profile.Coins);
        Assert.Equal(discount, paid.Bank.Payments.Last().Discount);
        Assert.Equal(trick, paid.Bank.Contract!.Trick); Assert.Equal(ordinary, paid.Bank.Contract.TotalDebt);
    }

    [Fact]
    public async Task FullRepaymentAfterEarlyWindowUsesTwentyPercentAndDoesNotSpendExtraBalance()
    {
        var (f, signed) = await NewTermsLoan(); f.Clock.Now += 86400;
        var quote = HubLoans.PayoffQuote(signed.Bank, f.Clock.Now);
        Assert.False(quote.Early); Assert.Equal(1200, quote.Amount); Assert.Equal(0, quote.Discount);
        var paid = await f.Ok("bank_repay", amount: 0);
        Assert.Equal(0, paid.Bank.Debt); Assert.Equal(100, paid.Profile.Coins);
        Assert.Equal(1200, paid.Bank.Payments.Last().Amount); Assert.False(paid.Bank.Payments.Last().Early);
        Assert.Equal(0, paid.Bank.Payments.Last().Discount);
    }

    [Fact]
    public async Task RepaymentRejectsOverpaymentAndNeverSilentlyChargesMoreThanTheQuote()
    {
        var (f, signed) = await NewTermsLoan();
        var failed = await f.Service.ExecuteAsync(Cmd("bank_repay", amount: 1101));
        Assert.False(failed.Success); Assert.Equal("amount", failed.Error);
        Assert.Equal(1200, failed.Snapshot.Bank.Debt); Assert.Equal(1300, failed.Snapshot.Profile.Coins);
        Assert.Empty(failed.Snapshot.Bank.Payments);
        Assert.False((await f.Service.ExecuteAsync(Cmd("bank_repay", amount: -1))).Success);
        var one = await f.Ok("bank_repay", amount: 1);
        Assert.Equal(1199, one.Bank.Debt); Assert.Equal(1, one.Bank.Payments.Single().Amount);
    }

    [Fact]
    public async Task BeforeDeadlineAllIncomeIsKeptAndStakeIsAllowed()
    {
        var (f, signed) = await NewTermsLoan();
        Assert.False(HubLoans.HasStakeRestriction(signed.Bank, f.Clock.Now));
        var gift = await f.Ok("gift_open"); Assert.Equal(1350, gift.Profile.Coins); Assert.Equal(1200, gift.Bank.Debt);
        await f.Ok("tutorial_skip"); var reward = await f.Ok("achievement_claim", "tutorial_skip");
        Assert.Equal(1375, reward.Profile.Coins); Assert.Equal(1200, reward.Bank.Debt);
        var started = await f.Ok("begin_match", "durak", amount: 67);
        Assert.Equal(1308, started.Profile.Coins);
        var win = await f.Ok("report_match", "durak", text: started.ActiveMatchId, flag: true);
        Assert.Equal(1522, win.Profile.Coins); Assert.Equal(1200, win.Bank.Debt);
        Assert.Empty(win.Bank.Payments); Assert.False(win.Bank.CollectionActive);
    }

    [Fact]
    public async Task DeadlineImmediatelyCallsCollectorsButDoesNotYetWithholdAndBlocksOnlyPaidEntries()
    {
        var (f, signed) = await NewTermsLoan(); f.Clock.Now = signed.Bank.DueAt;
        var due = (await f.Service.LoadAsync()).Snapshot;
        Assert.Equal("collection", due.Bank.Status); Assert.True(due.Bank.CollectorPending);
        Assert.Equal(signed.Bank.DueAt, due.Bank.CollectorAt); Assert.False(due.Bank.CollectionActive);
        Assert.True(HubLoans.HasStakeRestriction(due.Bank, f.Clock.Now));
        var blocked = await f.Service.ExecuteAsync(Cmd("begin_match", "durak", amount: 1));
        Assert.False(blocked.Success); Assert.Equal("debt", blocked.Error);
        var free = await f.Ok("begin_match", "durak");
        var played = await f.Ok("report_match", "durak", text: free.ActiveMatchId, flag: true);
        Assert.Equal(1380, played.Profile.Coins); Assert.Equal(1200, played.Bank.Debt);
        Assert.True(played.Bank.CollectorPending); Assert.Empty(played.Bank.Payments);
    }

    [Fact]
    public async Task ReadyPaidRoomRechecksDeadlineButCanStillStartForFree()
    {
        var (f, signed) = await NewTermsLoan(); await ReadyRoom(f, 10);
        f.Clock.Now = signed.Bank.DueAt;
        var blocked = await f.Service.ExecuteAsync(Cmd("start_room"));
        Assert.False(blocked.Success); Assert.Equal("debt", blocked.Error);
        Assert.Empty(blocked.Snapshot.ActiveMatchId); Assert.Equal(1300, blocked.Snapshot.Profile.Coins);
        await f.Ok("set_room_stake", amount: 0); await f.Ok("room_ready", flag: true);
        var free = await f.Ok("start_room");
        Assert.NotEmpty(free.ActiveMatchId); Assert.Equal(1300, free.Profile.Coins);
        Assert.True(free.Bank.CollectorPending);
    }

    [Fact]
    public async Task FailedCollectorVictorySaveDoesNotConsumeTheExtensionOrAwardCoinsTwice()
    {
        var (f, signed) = await NewTermsLoan(); f.Clock.Now = signed.Bank.DueAt;
        var started = await f.Ok("play_collectors");
        var command = Cmd("report_match", started.Bank.CollectorGame, text: started.ActiveMatchId, flag: true);
        f.Store.FailSave = true;
        var failed = await f.Service.ExecuteAsync(command);
        Assert.False(failed.Success); Assert.False(failed.Snapshot.Bank.ExtensionUsed);
        Assert.Equal(1300, failed.Snapshot.Profile.Coins); Assert.Equal(signed.Bank.DueAt, failed.Snapshot.Bank.DueAt);
        f.Store.FailSave = false;
        var saved = await f.Service.ExecuteAsync(command); Assert.True(saved.Success);
        Assert.True(saved.Snapshot.Bank.ExtensionUsed); Assert.Equal(1380, saved.Snapshot.Profile.Coins);
        var replay = await new LocalPlayerHubService(f.Store, f.Clock).ExecuteAsync(command);
        Assert.True(replay.Success); Assert.Equal(saved.Snapshot.Bank.DueAt, replay.Snapshot.Bank.DueAt);
        Assert.Equal(1380, replay.Snapshot.Profile.Coins); Assert.Equal(1200, replay.Snapshot.Bank.Debt);
    }

    [Fact]
    public async Task CollectorVictoryExtendsExactlyOneDayWithoutForgivenessAndExpiryStartsWithholding()
    {
        var (f, signed) = await NewTermsLoan(); f.Clock.Now = signed.Bank.DueAt + 300;
        var started = await f.Ok("play_collectors");
        var wonAt = f.Clock.Now;
        var won = await f.Ok("report_match", started.Bank.CollectorGame, text: started.ActiveMatchId, flag: true);
        Assert.Equal(1200, won.Bank.Debt); Assert.Equal(1380, won.Profile.Coins);
        Assert.True(won.Bank.ExtensionUsed); Assert.False(won.Bank.CollectorPending); Assert.False(won.Bank.CollectionActive);
        Assert.Equal(wonAt + 86400, won.Bank.DueAt); Assert.Equal("extended", won.Bank.Status);
        Assert.Equal(signed.Bank.Contract!.OriginalDueAt, won.Bank.Contract!.OriginalDueAt);
        Assert.Equal(1200, won.Bank.Contract.TotalDebt); Assert.False(HubLoans.HasStakeRestriction(won.Bank, f.Clock.Now));
        Assert.False((await f.Service.ExecuteAsync(Cmd("play_collectors"))).Success);
        var game = await f.Ok("begin_match", "poker", amount: 1); await f.Ok("abandon_match", game.ActiveMatchId);
        f.Clock.Now = won.Bank.DueAt - 1;
        var lastSecond = (await f.Service.LoadAsync()).Snapshot;
        Assert.False(lastSecond.Bank.CollectionActive); Assert.False(HubLoans.HasStakeRestriction(lastSecond.Bank, f.Clock.Now));
        f.Clock.Now++;
        var expired = (await new LocalPlayerHubService(f.Store, f.Clock).LoadAsync()).Snapshot;
        Assert.True(expired.Bank.CollectionActive); Assert.False(expired.Bank.CollectorPending);
        Assert.Equal("repaying", expired.Bank.Status); Assert.Equal(1200, expired.Bank.Debt);
        Assert.True(HubLoans.HasStakeRestriction(expired.Bank, f.Clock.Now));
        var gift = await f.Ok("gift_open");
        Assert.Equal(1175, gift.Bank.Debt); Assert.Equal(1404, gift.Profile.Coins);
        Assert.Equal("income", gift.Bank.Payments.Last().Source); Assert.Equal(25, gift.Bank.Payments.Last().Amount);
        Assert.False((await f.Service.ExecuteAsync(Cmd("play_collectors"))).Success);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CollectorLossOrAbandonKeepsWalletAndActivatesHalfOfFutureIncome(bool abandon)
    {
        var (f, signed) = await NewTermsLoan(); f.Clock.Now = signed.Bank.DueAt;
        var started = await f.Ok("play_collectors");
        var lost = abandon ? await f.Ok("abandon_match", started.ActiveMatchId)
            : await f.Ok("report_match", started.Bank.CollectorGame, text: started.ActiveMatchId);
        Assert.Equal(abandon ? 1300 : 1310, lost.Profile.Coins);
        Assert.Equal(abandon ? 1200 : 1190, lost.Bank.Debt);
        Assert.True(lost.Bank.CollectionActive); Assert.False(lost.Bank.CollectorPending);
        Assert.False(lost.Bank.ExtensionUsed); Assert.Empty(lost.ActiveMatchId);
        await f.Ok("tutorial_skip");
        var first = await f.Ok("achievement_claim", "tutorial_skip");
        Assert.Equal(lost.Profile.Coins + 13, first.Profile.Coins); Assert.Equal(lost.Bank.Debt - 12, first.Bank.Debt);
        Assert.Equal(1, first.Bank.IncomeRemainder);
        var second = await f.Ok("pass_claim", "free-1");
        Assert.Equal(first.Profile.Coins + 17, second.Profile.Coins); Assert.Equal(first.Bank.Debt - 18, second.Bank.Debt);
        Assert.Equal(0, second.Bank.IncomeRemainder);
    }

    [Fact]
    public async Task CollectorDrawDoesNotConsumeTheSingleExtension()
    {
        var (f, signed) = await NewTermsLoan(); f.Clock.Now = signed.Bank.DueAt;
        var started = await f.Ok("play_collectors");
        var draw = await f.Ok("report_match", started.Bank.CollectorGame, text: started.ActiveMatchId, amount: 1);
        Assert.Equal(1320, draw.Profile.Coins); Assert.Equal(1200, draw.Bank.Debt);
        Assert.True(draw.Bank.CollectorPending); Assert.False(draw.Bank.ExtensionUsed); Assert.False(draw.Bank.CollectionActive);
        var again = await f.Ok("play_collectors");
        Assert.NotEqual(started.ActiveMatchId, again.ActiveMatchId);
    }

    [Fact]
    public async Task DoublePenaltyDoesNotRepeatWhenTheExtendedDeadlineExpires()
    {
        var (f, signed) = await NewTermsLoan("double");
        await f.Ok("bank_repay", amount: 1);
        f.Clock.Now = signed.Bank.DueAt;
        var started = await f.Ok("play_collectors");
        Assert.Equal(2398, started.Bank.Debt); Assert.True(started.Bank.OverdueDoubled);
        var won = await f.Ok("report_match", started.Bank.CollectorGame, text: started.ActiveMatchId, flag: true);
        Assert.Equal(2398, won.Bank.Debt);
        f.Clock.Now = won.Bank.DueAt;
        var expired = await new LocalPlayerHubService(f.Store, f.Clock).LoadAsync();
        Assert.Equal(2398, expired.Snapshot.Bank.Debt); Assert.True(expired.Snapshot.Bank.CollectionActive);
        Assert.Equal(1200, expired.Snapshot.Bank.Contract!.TotalDebt);
    }

    [Fact]
    public async Task PaymentsAndContractsKeepTheLastTenAndClaimReplayDoesNotDuplicateReceipts()
    {
        var (f, signed) = await NewTermsLoan();
        for (var i = 1; i <= 12; i++) await f.Ok("bank_repay", amount: 1, id: "piece-" + i);
        var pieces = (await f.Service.LoadAsync()).Snapshot;
        Assert.Equal(10, pieces.Bank.Payments.Count); Assert.Equal("piece-3", pieces.Bank.Payments[0].Id);
        Assert.Equal(12, pieces.Bank.Repaid); Assert.Equal(1188, pieces.Bank.Debt);
        await f.Ok("bank_repay", amount: 0);
        for (var i = 0; i < 11; i++)
        {
            var next = await f.Ok("bank_offer");
            await f.Ok("bank_sign", text: next.Bank.Offer!.Id, amount: 100);
            await f.Ok("bank_repay", amount: 0);
        }
        var history = await new LocalPlayerHubService(f.Store, f.Clock).LoadAsync();
        Assert.True(history.Success); Assert.Equal(10, history.Snapshot.Bank.Contracts.Count);
        Assert.Equal(10, history.Snapshot.Bank.Payments.Count);
        Assert.Equal(12, history.Snapshot.Bank.Contract!.Number);
        Assert.All(history.Snapshot.Bank.Contracts, c => Assert.Equal("paid", c.Status));
        Assert.Equal(history.Snapshot.Bank.Contract.Id, history.Snapshot.Bank.Payments.Last().ContractId);
    }

    [Fact]
    public async Task LegacyLoanMigrationPreservesTenPercentDeadlineRestrictionsAndNoEarlyDiscount()
    {
        var f = new Fixture(); await f.Service.LoadAsync(); var saved = f.Store.Read();
        saved.Bank.Principal = 100; saved.Bank.Debt = 110; saved.Bank.Status = "approved";
        saved.Bank.ReviewAt = f.Clock.Now; saved.Bank.DueAt = f.Clock.Now + 3 * 86400;
        saved.Bank.CollectorAt = saved.Bank.DueAt + 43200; saved.Bank.Contract = new HubLoanContract();
        saved.Bank.Contracts = null!; saved.Bank.Payments = null!;
        f.Store.Write(saved); f.Service = new LocalPlayerHubService(f.Store, f.Clock);
        var migrated = (await f.Service.LoadAsync()).Snapshot;
        Assert.Equal(1, migrated.Bank.TermsVersion); Assert.Equal(1, migrated.Bank.Contract!.TermsVersion);
        Assert.Equal(110, migrated.Bank.Debt); Assert.Equal(110, migrated.Bank.Contract.TotalDebt);
        Assert.Equal(saved.Bank.CollectorAt, migrated.Bank.CollectorAt);
        Assert.True(HubLoans.HasStakeRestriction(migrated.Bank, f.Clock.Now));
        var quote = HubLoans.PayoffQuote(migrated.Bank, f.Clock.Now);
        Assert.False(quote.Early); Assert.Equal(110, quote.Amount); Assert.Equal(0, quote.Discount);
        f.Clock.Now = saved.Bank.DueAt;
        var overdue = (await f.Service.LoadAsync()).Snapshot;
        Assert.Equal("overdue", overdue.Bank.Status); Assert.False(overdue.Bank.CollectorPending);
        f.Clock.Now = saved.Bank.CollectorAt;
        var started = await f.Ok("play_collectors");
        var won = await f.Ok("report_match", started.Bank.CollectorGame, text: started.ActiveMatchId, flag: true);
        Assert.Equal(43, won.Bank.Debt); // Legacy forgiveness27 and40 withheld from normal80.
        Assert.Equal(saved.Bank.DueAt, won.Bank.DueAt); Assert.False(won.Bank.ExtensionUsed);
    }

    [Theory]
    [InlineData("rate", 40, 0)]
    [InlineData("fee", 20, 20)]
    public async Task UnchallengedNewTermsPersistWithoutChangingAfterReopen(string trick, int interest, int fee)
    {
        var (f, signed) = await NewTermsLoan(trick, 1000);
        Assert.Equal(1400, signed.Bank.Debt); Assert.Equal(interest, signed.Bank.InterestPercent);
        Assert.Equal(fee, HubLoans.FeePercent(trick));
        Assert.Equal(trick, signed.Bank.Contract!.Trick); Assert.Equal(trick, signed.Bank.Contract.OriginalTrick);
        Assert.False(signed.Bank.Contract.TrickChallenged);
        var reread = (await new LocalPlayerHubService(f.Store, f.Clock).LoadAsync()).Snapshot;
        Assert.Equal(1400, reread.Bank.Contract!.TotalDebt); Assert.Equal(1400, reread.Bank.Debt);
        Assert.Equal(trick, reread.Bank.Contract.OriginalTrick); Assert.Equal(signed.Bank.DueAt, reread.Bank.DueAt);
        Assert.Equal(1100, HubLoans.PayoffQuote(reread.Bank, f.Clock.Now).Amount);
        f.Clock.Now += 86400;
        Assert.Equal(1400, HubLoans.PayoffQuote(reread.Bank, f.Clock.Now).Amount);
    }

    [Fact]
    public async Task ExistingLegacyDailyContractKeepsItsThirtyPercentTermsAfterMigration()
    {
        var f = new Fixture(); await f.Service.LoadAsync(); var saved = f.Store.Read();
        saved.Bank.Principal = 100; saved.Bank.Debt = 130; saved.Bank.InterestPercent = 30;
        saved.Bank.Trick = "daily"; saved.Bank.Status = "approved"; saved.Bank.ReviewAt = f.Clock.Now;
        saved.Bank.DueAt = f.Clock.Now + 3 * 86400; saved.Bank.CollectorAt = saved.Bank.DueAt + 43200;
        f.Store.Write(saved);
        var loaded = (await new LocalPlayerHubService(f.Store, f.Clock).LoadAsync()).Snapshot;
        Assert.Equal(1, loaded.Bank.TermsVersion); Assert.Equal(130, loaded.Bank.Debt);
        Assert.Equal(130, loaded.Bank.Contract!.TotalDebt); Assert.Equal(30, loaded.Bank.Contract.InterestPercent);
        Assert.Equal("daily", loaded.Bank.Contract.Trick); Assert.Equal(saved.Bank.DueAt, loaded.Bank.DueAt);
        Assert.False(HubLoans.PayoffQuote(loaded.Bank, f.Clock.Now).Early);
    }

    private static async Task<(Fixture f, HubSnapshot signed)> NewTermsLoan(string trick = "", int principal = 1000)
    {
        var f = new Fixture(); await f.Service.LoadAsync(); var saved = f.Store.Read();
        saved.Profile.Matches = 2; saved.Profile.Level = 20; saved.Profile.XP = 1900;
        saved.IncomeDays.Single().Gross = 5000;
        if (trick.Length > 0)
        {
            saved.Bank.OfferSequence = 3;
            saved.Bank.Offer = new HubLoanOffer { Id = "terms-v2-trick-" + trick, Number = 3, Trick = trick };
        }
        f.Store.Write(saved); f.Service = new LocalPlayerHubService(f.Store, f.Clock);
        var offered = await f.Ok("bank_offer");
        var signed = await f.Ok("bank_sign", text: offered.Bank.Offer!.Id, amount: principal);
        return (f, signed);
    }
}
