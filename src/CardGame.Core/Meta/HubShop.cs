using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CardGame.Core.Meta
{
    /// <summary>
    /// Витрина дня в Лавке Дамы бубен (2026-10-17, IDEAS.md «Лавка Дамы бубен — витрина дня»): каждый день у игрока своя
    /// витрина из <see cref="HubShop.SlotCount"/> мест, обновление в полночь. Место 0 — товар дня (редкая вещь со скидкой),
    /// остальные — обычные и необычные. Того, что уже есть у игрока, на новой витрине не бывает; купленное остаётся на месте
    /// с биркой «Продано» до следующей витрины.
    /// </summary>
    [Serializable]
    public sealed class HubShopState
    {
        public long Day;        // номер дня витрины (HubShop.DayOf); 0 — ещё не собиралась
        public int Rerolls;     // сколько раз сегодня попросили «Покажи другое»
        public int Shuffles;    // сколько витрин собрано всего — часть семени, чтобы каждая была новой
        public List<string> Slots = new List<string>(); // id вещей по местам; "" — место пустое
    }

    public static class HubShop
    {
        public const int SlotCount = 5, DailyRerolls = 2, DayDiscountPercent = 30;
        // Витрина меняется в полночь по Москве (UTC+3) — одна граница дня у всех, как будет на сервере.
        public const long DayOffset = 3 * 3600;
        public const string Common = "common", Uncommon = "uncommon", Rare = "rare";

        public static long DayOf(long now) => (Math.Max(0, now) + DayOffset) / 86400;
        public static long NextShowcaseAt(long now) => (DayOf(now) + 1) * 86400 - DayOffset;

        public static int PriceFor(string rarity) => rarity == Rare ? 900 : rarity == Uncommon ? 350 : 200;

        public static string RarityName(string rarity) => rarity == Rare ? "Редкая" : rarity == Uncommon ? "Необычная" : "Обычная";

        /// <summary>Что вообще продаётся в Лавке: не базовое, не расходник (кидалки — у Семёрки треф), не персонаж.</summary>
        public static bool ForSale(HubCatalogItem item)
            => item != null && !item.IsDefault && item.Price > 0 && item.Category != "consumable" && item.Category != "avatar";

        /// <summary>Цена вещи на месте витрины: у товара дня — со скидкой.</summary>
        public static int PriceAt(HubCatalogItem item, int slot)
            => slot == 0 ? item.Price * (100 - DayDiscountPercent) / 100 : item.Price;

        /// <summary>Сколько можно потратить в Лавке: пока долг Тузяре не закрыт — только «баланс минус долг».</summary>
        public static int Spendable(HubSnapshot snapshot)
            => Math.Max(0, snapshot.Profile.Coins - Math.Max(0, snapshot.Bank?.Debt ?? 0));

        internal static void Refresh(HubSnapshot snapshot, long now)
        {
            if (snapshot.Shop == null) snapshot.Shop = new HubShopState();
            var day = DayOf(now);
            if (snapshot.Shop.Day == day && snapshot.Shop.Slots.Count == SlotCount) return;
            snapshot.Shop.Day = day;
            snapshot.Shop.Rerolls = 0;
            Fill(snapshot);
        }

        /// <summary>
        /// Новая витрина. Семя — игрок, день и номер витрины: перезапуск или неудачное сохранение не перетасуют её заново.
        /// После «Покажи другое» сначала берутся вещи, которых только что не было на полке.
        /// </summary>
        internal static void Fill(HubSnapshot snapshot)
        {
            var shop = snapshot.Shop;
            var shown = new HashSet<string>(shop.Slots.Where(id => id.Length > 0));
            var owned = new HashSet<string>(snapshot.Inventory.Where(i => i.Quantity > 0).Select(i => i.ItemId));
            var pool = snapshot.Catalog.Where(item => ForSale(item) && !owned.Contains(item.Id)).ToList();
            var seed = Hash(snapshot.Profile.Id + "|" + snapshot.Profile.JoinedAt.ToString(CultureInfo.InvariantCulture)
                + "|" + shop.Day.ToString(CultureInfo.InvariantCulture) + "|" + shop.Shuffles.ToString(CultureInfo.InvariantCulture));
            shop.Shuffles = checked(shop.Shuffles + 1);
            shop.Slots = new List<string>();

            string Pick(Func<HubCatalogItem, bool> wanted)
            {
                // Сначала нужная редкость и не показанное только что, потом уступки по очереди.
                foreach (var candidates in new[]
                {
                    pool.Where(item => wanted(item) && !shown.Contains(item.Id)),
                    pool.Where(item => wanted(item)),
                    pool.Where(item => !shown.Contains(item.Id)),
                    pool,
                })
                {
                    var list = candidates.ToList();
                    if (list.Count == 0) continue;
                    seed = Next(seed);
                    var item = list[(int)(seed % (uint)list.Count)];
                    pool.Remove(item);
                    return item.Id;
                }
                return "";
            }

            shop.Slots.Add(Pick(item => item.Rarity == Rare));
            for (var slot = 1; slot < SlotCount; slot++) shop.Slots.Add(Pick(item => item.Rarity != Rare));
            // Рынок помнит витрины за неделю: упущенное можно заказать с корабля (HubMarket.cs).
            HubMarket.Remember(snapshot, shop.Day, shop.Slots);
        }

        private static uint Next(uint value)
        {
            if (value == 0) value = 0x9e3779b9;
            unchecked { value ^= value << 13; value ^= value >> 17; return value ^ (value << 5); }
        }

        private static uint Hash(string value)
        {
            unchecked { uint hash = 2166136261; foreach (var character in value) hash = (hash ^ character) * 16777619; return hash; }
        }
    }

    public sealed partial class LocalPlayerHubService
    {
        private string BuyFromShop(HubSnapshot s, string itemId)
        {
            var slot = s.Shop.Slots.IndexOf(itemId);
            Require(itemId.Length > 0 && slot >= 0, "shop_slot", "Этой вещи сегодня нет на витрине.");
            var item = FindItem(s, itemId);
            Require(!s.Inventory.Any(i => i.ItemId == itemId && i.Quantity > 0), "owned", "Эта вещь уже у тебя.");
            var price = HubShop.PriceAt(item, slot);
            Require(price <= s.Profile.Coins, "coins", "Не хватает серебра: нужно " + price + ", у тебя " + s.Profile.Coins + ".");
            Require(price <= HubShop.Spendable(s), "debt",
                "Пока долг Тузяре не закрыт, Дама продаёт только на свободные деньги: " + HubShop.Spendable(s) + " из " + s.Profile.Coins + ".");
            s.Profile.Coins -= price;
            GrantItem(s, itemId, 1);
            return "Куплено: «" + item.Name + "» · −" + price + " коинов";
        }

        private string RerollShop(HubSnapshot s)
        {
            Require(s.Shop.Rerolls < HubShop.DailyRerolls, "shop_reroll", "На складе на сегодня пусто — новый товар в полночь.");
            s.Shop.Rerolls++;
            HubShop.Fill(s);
            return "Дама показала другое";
        }
    }
}
