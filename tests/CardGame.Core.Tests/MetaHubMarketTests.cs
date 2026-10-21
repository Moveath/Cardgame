using CardGame.Core.Meta;

namespace CardGame.Core.Tests;

public sealed partial class MetaHubTests
{
    [Fact]
    public async Task MarketPierIsEmptyUntilAnOrderedItemArrivesOnTheShipAndBecomesEmptyAgainAfterPickup()
    {
        var f = new Fixture();
        var s = (await f.Service.LoadAsync()).Snapshot;
        await SetCoins(f, 5000);

        // By default, no ship is at the pier.
        Assert.Equal(HubShipStatus.None, HubMarket.Ship(s, f.Clock.Now));

        var orderable = HubMarket.Missed(s, f.Clock.Now);
        Assert.NotEmpty(orderable);
        var target = orderable[0];
        var expectedPrice = HubMarket.OrderPrice(target, discount: false);
        Assert.Equal(target.Price * 150 / 100, expectedPrice);

        var ordered = await f.Ok("market_order", target.Id);
        Assert.Equal(5000 - expectedPrice, ordered.Profile.Coins);
        Assert.Equal(target.Id, ordered.Market.Order.ItemId);
        Assert.Equal(HubShipStatus.Sailing, HubMarket.Ship(ordered, f.Clock.Now));
        Assert.Equal("market_order", (await f.Service.ExecuteAsync(Cmd("market_order", target.Id))).Error);

        // One second before arrival: still sailing, pier still empty, cannot pick up yet.
        f.Clock.Now = ordered.Market.Order.ArrivesAt - 1;
        var stillSailing = (await f.Service.LoadAsync()).Snapshot;
        Assert.Equal(HubShipStatus.Sailing, HubMarket.Ship(stillSailing, f.Clock.Now));
        Assert.Equal("market_sailing", (await f.Service.ExecuteAsync(Cmd("market_pickup"))).Error);

        // At arrival: ship is docked in the arch!
        f.Clock.Now = ordered.Market.Order.ArrivesAt;
        var docked = (await f.Service.LoadAsync()).Snapshot;
        Assert.Equal(HubShipStatus.Docked, HubMarket.Ship(docked, f.Clock.Now));

        // Picking up the parcel moves the item into inventory and the ship leaves the pier.
        var picked = await f.Ok("market_pickup");
        Assert.Contains(picked.Inventory, i => i.ItemId == target.Id && i.Quantity == 1);
        Assert.Equal(HubShipStatus.None, HubMarket.Ship(picked, f.Clock.Now));
        Assert.Empty(picked.Market.Order.ItemId);
    }

    [Fact]
    public async Task MarketOrderExcludesCurrentShowcaseAndOwnedItemsAndPrunesAfterSevenDays()
    {
        var f = new Fixture();
        var s = (await f.Service.LoadAsync()).Snapshot;
        var orderable = HubMarket.Missed(s, f.Clock.Now).Select(i => i.Id).ToHashSet();

        Assert.All(s.Shop.Slots.Where(id => id.Length > 0), id => Assert.DoesNotContain(id, orderable));
        Assert.Equal("market_missed", (await f.Service.ExecuteAsync(Cmd("market_order", s.Shop.Slots[0]))).Error);

        var todaySlot = s.Shop.Slots[1];
        f.Clock.Now += 86400;
        var nextDay = (await f.Service.LoadAsync()).Snapshot;
        if (!nextDay.Shop.Slots.Contains(todaySlot))
            Assert.Contains(HubMarket.Missed(nextDay, f.Clock.Now), i => i.Id == todaySlot);

        // Advance past the 7-day window: old days are pruned from Seen.
        f.Clock.Now += 7 * 86400;
        var weekLater = (await f.Service.LoadAsync()).Snapshot;
        Assert.All(weekLater.Market.Seen, e => Assert.True(e.Day >= weekLater.Shop.Day - (HubMarket.SeenDays - 1)));
    }

    [Fact]
    public async Task MarketKingBuysBackUnequippedItemsForFortyPercentAndRefusesEquippedOrFreeItems()
    {
        var f = new Fixture();
        var s = (await f.Service.LoadAsync()).Snapshot;
        await SetCoins(f, 5000);

        var item = s.Catalog.Single(i => i.Id == s.Shop.Slots[1]);
        var prevEquipped = s.Equipped.FirstOrDefault(e => e.Slot == item.Category)?.ItemId;
        var afterBuy = await f.Ok("shop_buy", item.Id);
        Assert.Contains(HubMarket.OwnedForSale(afterBuy), i => i.Id == item.Id);

        // Equipping prevents selling until unequipped.
        await f.Ok("equip", item.Id);
        Assert.Equal("equipped", (await f.Service.ExecuteAsync(Cmd("market_sell", item.Id))).Error);
        if (!string.IsNullOrEmpty(prevEquipped))
        {
            await f.Ok("equip", prevEquipped!);
        }
        else
        {
            var saved = f.Store.Read();
            saved.Equipped.RemoveAll(e => e.ItemId == item.Id);
            f.Store.Write(saved);
            f.Service = new LocalPlayerHubService(f.Store, f.Clock);
        }

        var beforeSell = (await f.Service.LoadAsync()).Snapshot;
        var coinsBeforeSell = beforeSell.Profile.Coins;
        var expectedSell = item.Price * 40 / 100;
        Assert.Equal(expectedSell, HubMarket.SellPrice(item));

        var afterSell = await f.Ok("market_sell", item.Id);
        Assert.Equal(coinsBeforeSell + expectedSell, afterSell.Profile.Coins);
        Assert.DoesNotContain(afterSell.Inventory, i => i.ItemId == item.Id && i.Quantity > 0);

        // Starter free items cannot be sold.
        Assert.Equal("unsellable", (await f.Service.ExecuteAsync(Cmd("market_sell", "frame_default"))).Error);
    }

    [Fact]
    public async Task MarketSevenHaggleAwardsTwentyFivePercentDiscountAfterThreeCorrectGuessesOncePerDay()
    {
        var f = new Fixture();
        var s = (await f.Service.LoadAsync()).Snapshot;
        await SetCoins(f, 5000);

        Assert.InRange(s.Market.HaggleCard, 4, 12);
        Assert.Equal(0, s.Market.HaggleStreak);
        Assert.False(s.Market.HaggleDone);

        var current = s;
        while (!current.Market.HaggleDiscount)
        {
            if (current.Market.HaggleDone)
            {
                Assert.Equal("haggle_done", (await f.Service.ExecuteAsync(Cmd("market_haggle", "higher"))).Error);
                f.Clock.Now += 86400;
                current = (await f.Service.LoadAsync()).Snapshot;
                continue;
            }
            var guess = current.Market.HaggleCard <= 8 ? "higher" : "lower";
            current = await f.Ok("market_haggle", guess);
        }

        Assert.True(current.Market.HaggleDiscount);
        Assert.True(current.Market.HaggleDone);
        Assert.Equal(HubMarket.HaggleTarget, current.Market.HaggleStreak);

        var target = HubMarket.Missed(current, f.Clock.Now)[0];
        var discountedPrice = HubMarket.OrderPrice(target, discount: true);
        var fullPrice = HubMarket.OrderPrice(target, discount: false);
        Assert.True(discountedPrice < fullPrice);

        var coinsBefore = current.Profile.Coins;
        var ordered = await f.Ok("market_order", target.Id);
        Assert.Equal(coinsBefore - discountedPrice, ordered.Profile.Coins);
        Assert.False(ordered.Market.HaggleDiscount);
    }
}
