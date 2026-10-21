using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CardGame.Core.Meta
{
    /// <summary>
    /// Рынок Короля бубен — «Бубновые ряды» (2026-10-21, IDEAS.md «Рынок Короля бубен»):
    /// 1. Корабль Туза бубен («Упустил — закажи»): вещи, которые были на витрине Лавки за последние <see cref="HubMarket.SeenDays"/>
    ///    дней и не куплены, можно заказать за ×1,5 цены (со скидкой торга — −25 %). Корабль привозит заказ через 2 дня.
    ///    Пока заказа нет или он плывёт — у причала в арке пусто; когда заказ пришёл — в арке стоит корабль, а на прилавке
    ///    Короля ждёт свёрток. По клику картинка арки не переключается — только по событию прибытия/получения заказа.
    /// 2. Король бубен — скупка: принимает ненужные ненадетые вещи из инвентаря за 40 % их цены серебром.
    /// 3. Семёрка треф — торг по Закону стола («Больше-меньше»): раз в день три угаданные карты подряд дают скидку 25 %
    ///    на следующий заказ с корабля.
    /// </summary>
    [Serializable]
    public sealed class HubMarketState
    {
        // Что было на витринах Лавки: вещь и день витрины (HubShop.DayOf). Старше SeenDays — забывается.
        public List<HubShopSeen> Seen = new List<HubShopSeen>();
        public HubMarketOrder Order = new HubMarketOrder();
        // Торг с Семёркой треф («Больше-меньше» раз в день)
        public long HaggleDay;
        public int HaggleCard;      // текущий ранг 2..14 (0 — раздача ещё не начата сегодня)
        public int HaggleSuit;      // масть 0..3
        public int HagglePrevCard;  // предыдущая карта (чтобы показать, чем закончился шаг)
        public int HagglePrevSuit;
        public int HaggleStreak;    // сколько угадано подряд (0..3)
        public int HaggleSteps;     // сколько ходов сделано в сегодняшнем торге
        public bool HaggleDone;     // сегодняшний торг завершён (победой или промахом)
        public bool HaggleWonToday; // выиграл ли торг сегодня
        public bool HaggleDiscount; // активна ли скидка −25% на следующий заказ с корабля
    }

    [Serializable]
    public sealed class HubShopSeen { public string ItemId = ""; public long Day; }

    /// <summary>Заказ с корабля. ItemId "" — заказа нет.</summary>
    [Serializable]
    public sealed class HubMarketOrder
    {
        public string ItemId = "";
        public int Price;
        public long OrderedAt;
        public long ArrivesAt;
    }

    public enum HubShipStatus
    {
        /// <summary>Заказа нет — у причала пусто.</summary>
        None,
        /// <summary>Заказ плывёт — у причала пусто.</summary>
        Sailing,
        /// <summary>Заказ пришёл — корабль стоит у причала, свёрток ждёт на прилавке.</summary>
        Docked,
    }

    public static class HubMarket
    {
        public const int SeenDays = 7, OrderPercent = 150, SellPercent = 40, DiscountPercent = 25, HaggleTarget = 3;
        public const long TravelSeconds = 2 * 86400;

        /// <summary>Базовая цена заказа с корабля (×1,5 от цены Лавки).</summary>
        public static int BaseOrderPrice(HubCatalogItem item) => item.Price * OrderPercent / 100;

        /// <summary>Цена заказа с учётом скидки Семёрки треф (−25 %, если выторгована).</summary>
        public static int OrderPrice(HubCatalogItem item, bool discount = false)
        {
            var basePrice = BaseOrderPrice(item);
            return discount ? basePrice * (100 - DiscountPercent) / 100 : basePrice;
        }

        public static int OrderPrice(HubSnapshot s, HubCatalogItem item)
            => OrderPrice(item, s.Market?.HaggleDiscount == true);

        /// <summary>Цена скупки у Короля бубен (40 % от цены Лавки).</summary>
        public static int SellPrice(HubCatalogItem item) => item.Price * SellPercent / 100;

        public static bool HasOrder(HubSnapshot s) => s.Market?.Order != null && s.Market.Order.ItemId.Length > 0;

        public static HubShipStatus Ship(HubSnapshot s, long now)
            => !HasOrder(s) ? HubShipStatus.None : Math.Max(0, now) >= s.Market.Order.ArrivesAt ? HubShipStatus.Docked : HubShipStatus.Sailing;

        /// <summary>Сколько секунд кораблю ещё плыть (0 — пришёл или заказа нет).</summary>
        public static long SecondsLeft(HubSnapshot s, long now)
            => Ship(s, now) == HubShipStatus.Sailing ? s.Market.Order.ArrivesAt - Math.Max(0, now) : 0;

        /// <summary>
        /// Что можно заказать: было на витрине Лавки за последние SeenDays дней, не куплено и сейчас не стоит
        /// на сегодняшней витрине (его и так можно купить у Дамы). Самое свежее — первым.
        /// </summary>
        public static List<HubCatalogItem> Missed(HubSnapshot s, long now)
        {
            if (s.Market == null) return new List<HubCatalogItem>();
            var from = HubShop.DayOf(now) - (SeenDays - 1);
            var owned = new HashSet<string>(s.Inventory.Where(i => i.Quantity > 0).Select(i => i.ItemId));
            var onShelf = new HashSet<string>(s.Shop?.Slots ?? new List<string>());
            return s.Market.Seen
                .Where(seen => seen.Day >= from && !owned.Contains(seen.ItemId) && !onShelf.Contains(seen.ItemId))
                .OrderByDescending(seen => seen.Day)
                .Select(seen => seen.ItemId)
                .Distinct()
                .Select(id => s.Catalog.FirstOrDefault(item => item.Id == id))
                .Where(item => item != null && HubShop.ForSale(item))
                .Select(item => item!)
                .ToList();
        }

        /// <summary>Что вообще может принять Король бубен в скупку (не бесплатное, не расходник, не персонаж).</summary>
        public static bool CanEverSell(HubCatalogItem item) => HubShop.ForSale(item) && !item.IsPremium;

        /// <summary>Собственные вещи игрока, которые подходят для скупки (включая надетые — их попросят сначала снять).</summary>
        public static List<HubCatalogItem> OwnedForSale(HubSnapshot s)
        {
            var owned = new HashSet<string>(s.Inventory.Where(i => i.Quantity > 0).Select(i => i.ItemId));
            return s.Catalog.Where(item => owned.Contains(item.Id) && CanEverSell(item)).ToList();
        }

        public static bool IsEquipped(HubSnapshot s, string itemId)
            => s.Equipped.Any(e => e.ItemId == itemId);

        internal static void Normalize(HubSnapshot s, long now)
        {
            if (s.Market == null) s.Market = new HubMarketState();
            if (s.Market.Seen == null) s.Market.Seen = new List<HubShopSeen>();
            if (s.Market.Order == null) s.Market.Order = new HubMarketOrder();
            var today = HubShop.DayOf(now);
            if (s.Market.HaggleDay != today)
            {
                s.Market.HaggleDay = today;
                s.Market.HaggleCard = 0;
                s.Market.HaggleSuit = 0;
                s.Market.HagglePrevCard = 0;
                s.Market.HagglePrevSuit = 0;
                s.Market.HaggleStreak = 0;
                s.Market.HaggleSteps = 0;
                s.Market.HaggleDone = false;
                s.Market.HaggleWonToday = false;
                EnsureHaggleCard(s, today);
            }
            else if (s.Market.HaggleCard == 0 && !s.Market.HaggleDone)
            {
                EnsureHaggleCard(s, today);
            }
        }

        /// <summary>Запомнить витрину Лавки и, если в памяти ещё нет прошлых дней, засеять вчерашнюю витрину.</summary>
        internal static void Remember(HubSnapshot s, long day, IEnumerable<string> slots)
        {
            if (s.Market == null) s.Market = new HubMarketState();
            if (s.Market.Seen == null) s.Market.Seen = new List<HubShopSeen>();
            // Если у игрока ещё нет ни одной записи за прошлые дни — добавим вчерашнюю витрину, чтобы в порту
            // сразу было что заказать («вчера была в Лавке, сегодня уплыла»).
            if (!s.Market.Seen.Any(seen => seen.Day < day))
                SeedYesterdayShowcase(s, day - 1, slots);
            foreach (var id in slots.Where(id => id.Length > 0))
                if (!s.Market.Seen.Any(seen => seen.ItemId == id && seen.Day == day))
                    s.Market.Seen.Add(new HubShopSeen { ItemId = id, Day = day });
            s.Market.Seen.RemoveAll(seen => seen.Day < day - (SeenDays - 1));
        }

        private static void SeedYesterdayShowcase(HubSnapshot s, long yesterday, IEnumerable<string> todaySlots)
        {
            var owned = new HashSet<string>(s.Inventory.Where(i => i.Quantity > 0).Select(i => i.ItemId));
            var skip = new HashSet<string>(todaySlots.Where(id => id.Length > 0));
            var pool = s.Catalog.Where(item => HubShop.ForSale(item) && !owned.Contains(item.Id) && !skip.Contains(item.Id)).ToList();
            if (pool.Count == 0) return;
            var seed = Hash(s.Profile.Id + "|yesterday|" + yesterday.ToString(CultureInfo.InvariantCulture));
            var rares = pool.Where(i => i.Rarity == HubShop.Rare).ToList();
            if (rares.Count > 0)
            {
                seed = Next(seed);
                var rare = rares[(int)(seed % (uint)rares.Count)];
                s.Market.Seen.Add(new HubShopSeen { ItemId = rare.Id, Day = yesterday });
                pool.Remove(rare);
            }
            for (var i = 1; i < HubShop.SlotCount && pool.Count > 0; i++)
            {
                var others = pool.Where(item => item.Rarity != HubShop.Rare).ToList();
                var list = others.Count > 0 ? others : pool;
                seed = Next(seed);
                var pick = list[(int)(seed % (uint)list.Count)];
                s.Market.Seen.Add(new HubShopSeen { ItemId = pick.Id, Day = yesterday });
                pool.Remove(pick);
            }
        }

        internal static void EnsureHaggleCard(HubSnapshot s, long today)
        {
            if (s.Market.HaggleCard >= 2 && s.Market.HaggleCard <= 14) return;
            var seed = Hash(s.Profile.Id + "|haggle|" + today.ToString(CultureInfo.InvariantCulture) + "|0");
            seed = Next(seed);
            // Первая карта — от 4 до 12, чтобы у первого выбора всегда были шансы в обе стороны.
            s.Market.HaggleCard = 4 + (int)(seed % 9);
            seed = Next(seed);
            s.Market.HaggleSuit = (int)(seed % 4);
        }

        internal static (int rank, int suit) NextHaggleCard(HubSnapshot s, long today, int step, int currentRank)
        {
            var seed = Hash(s.Profile.Id + "|haggle|" + today.ToString(CultureInfo.InvariantCulture) + "|" + step.ToString(CultureInfo.InvariantCulture));
            seed = Next(seed);
            // Следующая карта отличается по достоинству от текущей (2..14, всего 12 вариантов кроме currentRank).
            var pick = (int)(seed % 12);
            var rank = 2 + pick;
            if (rank >= currentRank) rank++;
            seed = Next(seed);
            var suit = (int)(seed % 4);
            return (rank, suit);
        }

        public static string CardLabel(int rank, int suit)
        {
            if (rank < 2 || rank > 14) return "?";
            var r = rank switch
            {
                11 => "В",
                12 => "Д",
                13 => "К",
                14 => "Т",
                _ => rank.ToString(CultureInfo.InvariantCulture),
            };
            var s = (suit & 3) switch
            {
                0 => "♣",
                1 => "♦",
                2 => "♥",
                _ => "♠",
            };
            return r + s;
        }

        public static bool IsRedSuit(int suit) => (suit & 3) == 1 || (suit & 3) == 2;

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
        private string OrderFromShip(HubSnapshot s, string itemId)
        {
            HubMarket.Normalize(s, Now);
            Require(!HubMarket.HasOrder(s), "market_order", "Корабль уже везёт твой заказ — один за раз.");
            var item = HubMarket.Missed(s, Now).FirstOrDefault(i => i.Id == itemId);
            Require(item != null, "market_missed", "Эту вещь заказать нельзя: её не было на твоей витрине за неделю.");
            var price = HubMarket.OrderPrice(s, item!);
            Require(price <= s.Profile.Coins, "coins", "Не хватает серебра: нужно " + price + ", у тебя " + s.Profile.Coins + ".");
            Require(price <= HubShop.Spendable(s), "debt",
                "Пока долг Тузяре не закрыт, заказывать можно только на свободные деньги: " + HubShop.Spendable(s) + " из " + s.Profile.Coins + ".");
            s.Profile.Coins -= price;
            s.Market.HaggleDiscount = false;
            s.Market.Order = new HubMarketOrder { ItemId = item!.Id, Price = price, OrderedAt = Now, ArrivesAt = Now + HubMarket.TravelSeconds };
            return "Заказано: «" + item.Name + "» · −" + price + " коинов · корабль придёт через 2 дня";
        }

        private string PickUpOrder(HubSnapshot s)
        {
            HubMarket.Normalize(s, Now);
            Require(HubMarket.HasOrder(s), "market_order", "Заказа нет.");
            Require(HubMarket.Ship(s, Now) == HubShipStatus.Docked, "market_sailing", "Корабль ещё в пути.");
            var item = FindItem(s, s.Market.Order.ItemId);
            var price = s.Market.Order.Price;
            s.Market.Order = new HubMarketOrder();
            // Пока корабль плыл, вещь успели купить в Лавке — Король возвращает серебро, а не второй экземпляр.
            if (s.Inventory.Any(i => i.ItemId == item.Id && i.Quantity > 0))
            {
                s.Profile.Coins = checked(s.Profile.Coins + price);
                return "«" + item.Name + "» у тебя уже есть — Король вернул " + price + " коинов";
            }
            GrantItem(s, item.Id, 1);
            return "Получено с корабля: «" + item.Name + "»";
        }

        private string SellToKing(HubSnapshot s, string itemId)
        {
            HubMarket.Normalize(s, Now);
            var item = FindItem(s, itemId);
            var owned = s.Inventory.FirstOrDefault(i => i.ItemId == itemId && i.Quantity > 0);
            Require(owned != null, "not_owned", "Этой вещи у тебя нет.");
            Require(HubMarket.CanEverSell(item), "unsellable", "Базовые, подарочные и золотые вещи Король не скупает.");
            Require(!HubMarket.IsEquipped(s, itemId), "equipped", "Сначала сними вещь, потом неси на весы.");
            var price = HubMarket.SellPrice(item);
            s.Inventory.RemoveAll(i => i.ItemId == itemId);
            s.Profile.Coins = checked(s.Profile.Coins + price);
            return "Продано Королю: «" + item.Name + "» · +" + price + " коинов";
        }

        private string HaggleWithSeven(HubSnapshot s, string guess)
        {
            HubMarket.Normalize(s, Now);
            Require(!s.Market.HaggleDiscount, "haggle_discount", "У тебя уже есть скидка 25 % на заказ — сначала используй её.");
            Require(!s.Market.HaggleDone, "haggle_done", "Сегодня вы с Семёркой уже торговались. Загляни завтра.");
            Require(guess == "higher" || guess == "lower", "haggle_guess", "Выбери «старше» или «младше».");
            var today = HubShop.DayOf(Now);
            HubMarket.EnsureHaggleCard(s, today);
            var current = s.Market.HaggleCard;
            s.Market.HagglePrevCard = current;
            s.Market.HagglePrevSuit = s.Market.HaggleSuit;
            s.Market.HaggleSteps++;
            var (nextRank, nextSuit) = HubMarket.NextHaggleCard(s, today, s.Market.HaggleSteps, current);
            s.Market.HaggleCard = nextRank;
            s.Market.HaggleSuit = nextSuit;
            var right = guess == "higher" ? nextRank > current : nextRank < current;
            if (!right)
            {
                s.Market.HaggleDone = true;
                s.Market.HaggleWonToday = false;
                return "Выпало " + HubMarket.CardLabel(nextRank, nextSuit) + " — мимо! Цена осталась прежней, приходи завтра.";
            }
            s.Market.HaggleStreak++;
            if (s.Market.HaggleStreak >= HubMarket.HaggleTarget)
            {
                s.Market.HaggleDone = true;
                s.Market.HaggleWonToday = true;
                s.Market.HaggleDiscount = true;
                return "Три из трёх! Семёрка договорился с матросами: −25 % на следующий заказ с корабля.";
            }
            return "Верно: " + HubMarket.CardLabel(nextRank, nextSuit) + "! Угадано " + s.Market.HaggleStreak + " из " + HubMarket.HaggleTarget + ".";
        }
    }
}
