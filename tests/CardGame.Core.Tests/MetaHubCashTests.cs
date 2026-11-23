using CardGame.Core.Meta;

namespace CardGame.Core.Tests;

public sealed partial class MetaHubTests
{
    [Fact]
    public async Task CashBuyGoldSpendsOnlyFreeSilverNotTheBankLoan()
    {
        var f = new Fixture();
        // 500 на руках, из них 400 — долг Тузяре: свободно 100.
        await SetCoins(f, 500, debt: 400);
        Assert.Equal("debt", (await f.Service.ExecuteAsync(Cmd("cash_buy_gold", amount: 10))).Error);
        var s = await f.Ok("cash_buy_gold", amount: 100 / HubCash.SilverPerGold);
        Assert.Equal(500 - 100 / HubCash.SilverPerGold * HubCash.SilverPerGold, s.Profile.Coins);
        Assert.Equal(100 / HubCash.SilverPerGold, s.Profile.Gold);
    }

    [Fact]
    public async Task CashAdCreditsSilverFiveTimesADayAndResetsAtMidnight()
    {
        var f = new Fixture(); var s = (await f.Service.LoadAsync()).Snapshot;
        Assert.Equal(HubCash.AdsPerDay, HubCash.AdsLeft(s, f.Clock.Now));
        for (var i = 1; i <= HubCash.AdsPerDay; i++)
        {
            var next = await f.Ok("cash_ad");
            Assert.Equal(s.Profile.Coins + i * HubCash.AdReward, next.Profile.Coins);
            Assert.Equal(HubCash.AdsPerDay - i, HubCash.AdsLeft(next, f.Clock.Now));
        }
        Assert.Equal("ads", (await f.Service.ExecuteAsync(Cmd("cash_ad"))).Error);
        // Тестовая кнопка обнуляет лимиты дня.
        Assert.Equal(HubCash.AdsPerDay, HubCash.AdsLeft(await f.Ok("cash_debug_reset"), f.Clock.Now));
        await f.Ok("cash_ad");

        f.Clock.Now = HubShop.NextShowcaseAt(f.Clock.Now);
        var tomorrow = (await f.Service.LoadAsync()).Snapshot;
        Assert.Equal(HubCash.AdsPerDay, HubCash.AdsLeft(tomorrow, f.Clock.Now));
        await f.Ok("cash_ad");
    }

    [Fact]
    public async Task CashBuyAddsSilverOrGoldAndNoAdsOnlyOnce()
    {
        var f = new Fixture(); var s = (await f.Service.LoadAsync()).Snapshot;
        Assert.Equal(0, s.Profile.Gold);
        var silver = HubCash.PacksOf(HubCash.Silver).Last();
        var gold = HubCash.PacksOf(HubCash.Gold).First();

        var afterSilver = await f.Ok("cash_buy", silver.Id);
        Assert.Equal(s.Profile.Coins + silver.Amount, afterSilver.Profile.Coins);
        Assert.Equal(0, afterSilver.Profile.Gold);
        // Купленное серебро — не заработок: кредитный лимит от него не растёт.
        Assert.Equal(s.Profile.CoinsEarned, afterSilver.Profile.CoinsEarned);

        var afterGold = await f.Ok("cash_buy", gold.Id);
        Assert.Equal(gold.Amount, afterGold.Profile.Gold);
        Assert.Equal(afterSilver.Profile.Coins, afterGold.Profile.Coins);
        Assert.Equal(2, afterGold.Cash.Purchases);

        Assert.Equal("pack", (await f.Service.ExecuteAsync(Cmd("cash_buy", "silver_free"))).Error);
        Assert.True((await f.Ok("cash_buy", HubCash.NoAdsId)).Cash.NoAds);
        Assert.Equal("owned", (await f.Service.ExecuteAsync(Cmd("cash_buy", HubCash.NoAdsId))).Error);

        // Обмен только в одну сторону: серебро в золото по курсу, не больше GoldPerDay в день; к полуночи лимит обновляется.
        var bought = await f.Ok("cash_buy_gold", amount: 5);
        Assert.Equal(gold.Amount + 5, bought.Profile.Gold);
        Assert.Equal(afterGold.Profile.Coins - 5 * HubCash.SilverPerGold, bought.Profile.Coins);
        Assert.Equal(HubCash.GoldPerDay - 5, HubCash.GoldLeft(bought, f.Clock.Now));
        Assert.Equal("gold_limit", (await f.Service.ExecuteAsync(Cmd("cash_buy_gold", amount: HubCash.GoldPerDay))).Error);
        Assert.Equal("amount", (await f.Service.ExecuteAsync(Cmd("cash_buy_gold", amount: 0))).Error);
        await f.Ok("cash_buy_gold", amount: 5);
        Assert.Equal("unknown_action", (await f.Service.ExecuteAsync(Cmd("cash_exchange", amount: 10))).Error);

        // Золото и «Без рекламы» переживают перезапуск.
        f.Service = new LocalPlayerHubService(f.Store, f.Clock);
        var reloaded = (await f.Service.LoadAsync()).Snapshot;
        Assert.Equal(gold.Amount + 10, reloaded.Profile.Gold);
        Assert.Equal(HubCash.GoldPerDay - 10, HubCash.GoldLeft(reloaded, f.Clock.Now));
        f.Clock.Now = HubShop.NextShowcaseAt(f.Clock.Now);
        Assert.Equal(HubCash.GoldPerDay, HubCash.GoldLeft((await f.Service.LoadAsync()).Snapshot, f.Clock.Now));
        Assert.True(reloaded.Cash.NoAds);
    }
}
