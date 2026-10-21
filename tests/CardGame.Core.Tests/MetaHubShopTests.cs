using CardGame.Core.Meta;

namespace CardGame.Core.Tests;

public sealed partial class MetaHubTests
{
    [Fact]
    public async Task ShopShowcaseHasRareItemOfTheDayAndFourOthersThatPersistUntilMidnight()
    {
        var f = new Fixture(); var s = (await f.Service.LoadAsync()).Snapshot;
        Assert.Equal(HubShop.SlotCount, s.Shop.Slots.Count);
        Assert.Equal(HubShop.DayOf(f.Clock.Now), s.Shop.Day);
        Assert.Equal(s.Shop.Slots.Count, s.Shop.Slots.Distinct().Count());
        var items = s.Shop.Slots.Select(id => s.Catalog.Single(i => i.Id == id)).ToArray();
        Assert.All(items, item => Assert.True(HubShop.ForSale(item)));
        Assert.All(items, item => Assert.DoesNotContain(s.Inventory, owned => owned.ItemId == item.Id && owned.Quantity > 0));
        Assert.Equal(HubShop.Rare, items[0].Rarity);
        Assert.All(items.Skip(1), item => Assert.NotEqual(HubShop.Rare, item.Rarity));

        f.Clock.Now = HubShop.NextShowcaseAt(f.Clock.Now) - 1;
        f.Service = new LocalPlayerHubService(f.Store, f.Clock);
        Assert.Equal(s.Shop.Slots, (await f.Service.LoadAsync()).Snapshot.Shop.Slots);

        f.Clock.Now += 1;
        var next = (await f.Service.LoadAsync()).Snapshot;
        Assert.Equal(s.Shop.Day + 1, next.Shop.Day);
        Assert.Equal(HubShop.SlotCount, next.Shop.Slots.Count(id => id.Length > 0));
    }

    [Fact]
    public async Task ShopBuyChargesDiscountedOrFullPriceAndKeepsTheSoldItemOnTheShelf()
    {
        var f = new Fixture(); var s = (await f.Service.LoadAsync()).Snapshot;
        await SetCoins(f, 5000);
        var day = s.Catalog.Single(i => i.Id == s.Shop.Slots[0]);
        var regular = s.Catalog.Single(i => i.Id == s.Shop.Slots[1]);

        var bought = await f.Ok("shop_buy", day.Id);
        Assert.Equal(5000 - day.Price * 70 / 100, bought.Profile.Coins);
        Assert.Contains(bought.Inventory, i => i.ItemId == day.Id && i.Quantity == 1);
        Assert.Equal(s.Shop.Slots, bought.Shop.Slots);
        bought = await f.Ok("shop_buy", regular.Id);
        Assert.Equal(5000 - day.Price * 70 / 100 - regular.Price, bought.Profile.Coins);

        Assert.Equal("owned", (await f.Service.ExecuteAsync(Cmd("shop_buy", day.Id))).Error);
        var absent = s.Catalog.First(i => HubShop.ForSale(i) && !s.Shop.Slots.Contains(i.Id));
        Assert.Equal("shop_slot", (await f.Service.ExecuteAsync(Cmd("shop_buy", absent.Id))).Error);
        Assert.Equal("shop_slot", (await f.Service.ExecuteAsync(Cmd("shop_buy", "tomato_pack"))).Error);
    }

    [Fact]
    public async Task ShopRefusesWithoutEnoughSilverOrWhenOnlyBorrowedSilverIsLeft()
    {
        var f = new Fixture(); var s = (await f.Service.LoadAsync()).Snapshot;
        var item = s.Catalog.Single(i => i.Id == s.Shop.Slots[1]);
        await SetCoins(f, item.Price - 1);
        var poor = await f.Service.ExecuteAsync(Cmd("shop_buy", item.Id));
        Assert.Equal("coins", poor.Error); Assert.Equal(item.Price - 1, poor.Snapshot.Profile.Coins);

        await SetCoins(f, item.Price + 50, debt: 100);
        var indebted = await f.Service.ExecuteAsync(Cmd("shop_buy", item.Id));
        Assert.Equal("debt", indebted.Error);
        Assert.DoesNotContain(indebted.Snapshot.Inventory, i => i.ItemId == item.Id);

        await SetCoins(f, item.Price + 100, debt: 100);
        Assert.Equal(100, (await f.Ok("shop_buy", item.Id)).Profile.Coins);
    }

    [Fact]
    public async Task ShopRerollShowsOtherItemsTwiceADayAndNeverOffersOwnedOnes()
    {
        var f = new Fixture(); var s = (await f.Service.LoadAsync()).Snapshot;
        await SetCoins(f, 5000);
        var kept = s.Shop.Slots[1];
        await f.Ok("shop_buy", kept);

        var first = await f.Ok("shop_reroll");
        Assert.Equal(1, first.Shop.Rerolls);
        Assert.NotEqual(s.Shop.Slots, first.Shop.Slots);
        Assert.DoesNotContain(kept, first.Shop.Slots);
        Assert.Equal(HubShop.Rare, first.Catalog.Single(i => i.Id == first.Shop.Slots[0]).Rarity);
        var second = await f.Ok("shop_reroll");
        Assert.Equal(2, second.Shop.Rerolls);
        Assert.Equal("shop_reroll", (await f.Service.ExecuteAsync(Cmd("shop_reroll"))).Error);

        f.Clock.Now = HubShop.NextShowcaseAt(f.Clock.Now);
        var tomorrow = (await f.Service.LoadAsync()).Snapshot;
        Assert.Equal(0, tomorrow.Shop.Rerolls);
        Assert.DoesNotContain(kept, tomorrow.Shop.Slots);
        Assert.Equal(1, (await f.Ok("shop_reroll")).Shop.Rerolls);
    }

    [Fact]
    public async Task ShopLeavesEmptyPlacesWhenEverythingIsOwned()
    {
        var f = new Fixture(); await f.Service.LoadAsync();
        var saved = f.Store.Read();
        foreach (var item in saved.Catalog.Where(HubShop.ForSale).Skip(2))
            saved.Inventory.Add(new HubOwnedItem { ItemId = item.Id });
        saved.Shop = new HubShopState();
        f.Store.Write(saved); f.Service = new LocalPlayerHubService(f.Store, f.Clock);
        var s = (await f.Service.LoadAsync()).Snapshot;
        Assert.Equal(HubShop.SlotCount, s.Shop.Slots.Count);
        Assert.Equal(2, s.Shop.Slots.Count(id => id.Length > 0));
        Assert.Equal("shop_slot", (await f.Service.ExecuteAsync(Cmd("shop_buy", ""))).Error);
    }

    [Fact]
    public async Task OldSavesGetCatalogPricesAndAShowcaseWithoutLosingPurchases()
    {
        var f = new Fixture(); await f.Service.LoadAsync();
        var saved = f.Store.Read();
        foreach (var item in saved.Catalog) { item.Price = 0; item.Rarity = HubShop.Common; item.PriceLabel = "Демо · открыть"; }
        saved.Inventory.Add(new HubOwnedItem { ItemId = "frame_gold" });
        saved.Shop = null!;
        f.Store.Write(saved); f.Service = new LocalPlayerHubService(f.Store, f.Clock);
        var s = (await f.Service.LoadAsync()).Snapshot;
        var gold = s.Catalog.Single(i => i.Id == "frame_gold");
        Assert.Equal(HubShop.Rare, gold.Rarity); Assert.Equal(900, gold.Price); Assert.Equal("900 коинов", gold.PriceLabel);
        Assert.Equal(0, s.Catalog.Single(i => i.Id == "frame_default").Price);
        Assert.Contains(s.Inventory, i => i.ItemId == "frame_gold");
        Assert.DoesNotContain("frame_gold", s.Shop.Slots);
        Assert.Equal(HubShop.SlotCount, s.Shop.Slots.Count);
    }

    private static async Task SetCoins(Fixture f, int coins, int debt = 0)
    {
        await f.Service.LoadAsync(); var saved = f.Store.Read();
        saved.Profile.Coins = coins; saved.Bank.Debt = debt;
        if (debt > 0) { saved.Bank.Status = "approved"; saved.Bank.DueAt = f.Clock.Now + 86400; }
        f.Store.Write(saved); f.Service = new LocalPlayerHubService(f.Store, f.Clock);
    }
}
