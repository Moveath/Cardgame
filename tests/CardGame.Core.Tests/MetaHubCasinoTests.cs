using CardGame.Core.Meta;

namespace CardGame.Core.Tests;

public sealed partial class MetaHubTests
{
    [Fact]
    public async Task CasinoCashierAwardsDailyFreeChipsOnceADayAndExchangesChipsAndPrizes()
    {
        var f = new Fixture();
        var s = (await f.Service.LoadAsync()).Snapshot;
        await SetCoins(f, 2000);

        Assert.True(HubCasino.CanClaimDaily(s, f.Clock.Now));
        var afterDaily = await f.Ok("casino_daily");
        Assert.Equal(HubCasino.DailyFreeChips, afterDaily.Casino.Chips);
        Assert.False(HubCasino.CanClaimDaily(afterDaily, f.Clock.Now));
        Assert.Equal("casino_daily", (await f.Service.ExecuteAsync(Cmd("casino_daily"))).Error);

        var bought = await f.Ok("casino_buy_chips", amount: 25);
        Assert.Equal(HubCasino.DailyFreeChips + 25, bought.Casino.Chips);
        Assert.Equal(2000 - 25 * HubCasino.ChipBuyCoins, bought.Profile.Coins);

        var cashed = await f.Ok("casino_cashout_chips", amount: 5);
        Assert.Equal(HubCasino.DailyFreeChips + 20, cashed.Casino.Chips);
        Assert.Equal(bought.Profile.Coins + 5 * HubCasino.ChipCashoutCoins, cashed.Profile.Coins);

        var prize = HubCasino.HighRollerPrizes(cashed)[0];
        var needed = HubCasino.PrizeChipCost(prize);
        if (cashed.Casino.Chips < needed)
            cashed = await f.Ok("casino_buy_chips", amount: needed - cashed.Casino.Chips);

        var redeemed = await f.Ok("casino_redeem", prize.Id);
        Assert.Contains(redeemed.Inventory, i => i.ItemId == prize.Id && i.Quantity == 1);
    }

    [Fact]
    public async Task CasinoBlackjackRouletteDiceSlotsAndRocketOperateOnCasinoChips()
    {
        var f = new Fixture();
        await f.Service.LoadAsync();
        await SetCoins(f, 5000);
        var s = await f.Ok("casino_buy_chips", amount: 50);

        // Blackjack 21
        var bj = await f.Ok("casino_blackjack", "start", amount: 5);
        if (bj.Casino.BjActive)
        {
            bj = await f.Ok("casino_blackjack", "stand");
        }
        Assert.False(bj.Casino.BjActive);
        Assert.NotEmpty(bj.Casino.BjOutcome);

        // Roulette & Dice
        var roul = await f.Ok("casino_roulette", "red", amount: 2);
        Assert.InRange(roul.Casino.RouletteCard, 0, 52);

        var dice = await f.Ok("casino_dice", "over7", amount: 2);
        Assert.InRange(dice.Casino.DiceA, 1, 6);
        Assert.InRange(dice.Casino.DiceB, 1, 6);

        // Slots 777
        var slots = await f.Ok("casino_slots", amount: 2);
        Assert.Equal(3, slots.Casino.SlotReels.Count);

        // Crash Rocket
        var rocket = await f.Ok("casino_rocket", "launch", amount: 3);
        Assert.True(rocket.Casino.RocketActive);
        rocket = await f.Ok("casino_rocket", "step");
        if (rocket.Casino.RocketActive)
        {
            rocket = await f.Ok("casino_rocket", "cashout");
            Assert.False(rocket.Casino.RocketActive);
            Assert.True(rocket.Casino.RocketLastWin > 3);
        }
        else
        {
            Assert.True(rocket.Casino.RocketCrashed);
        }
    }
}
