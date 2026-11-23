using System;
using System.Collections.Generic;
using System.Linq;

namespace CardGame.Core.Meta
{
    /// <summary>
    /// Касса банка Тузяры (2026-11-03, решение автора: за деньги продаются и серебряные, и золотые тун-тун коины):
    /// наборы коинов, серебро за ролик рекламы и «Без рекламы». Магазины приложений и рекламная сеть ещё не подключены,
    /// поэтому покупка — локальная демо-команда «cash_buy» (деньги не списываются), ролик — заглушка клиента и команда
    /// «cash_ad». Роликов за награду — не больше <see cref="HubCash.AdsPerDay"/> в день; день — по Москве, как у Лавки.
    /// «Без рекламы» — навсегда: убирает рекламу между партиями, ролики за награду остаются по желанию.
    /// Обмен только в одну сторону (автор, 2026-11-03): «cash_buy_gold» — серебро в золото по
    /// <see cref="HubCash.SilverPerGold"/> за штуку, не больше <see cref="HubCash.GoldPerDay"/> золотых в день и только из
    /// свободного серебра (не заёмного). Золото в серебро не меняется: серебро и так продаётся наборами. Лимит не даёт набить
    /// золото партиями и казино, но путь к золоту без денег есть.
    /// Перед настоящими платежами всё это переезжает на сервер: чек магазина проверяет он, а не телефон.
    /// </summary>
    [Serializable]
    public sealed class HubCashState
    {
        public long AdDay;      // день (HubShop.DayOf), к которому относится AdViews
        public int AdViews;     // сколько роликов за награду посмотрено в этот день
        public bool NoAds;      // куплено «Без рекламы»
        public int Purchases;   // сколько покупок сделано в кассе
        public int GoldBought;  // сколько золота куплено за серебро в день AdDay
    }

    /// <summary>Набор коинов в кассе. Цена — для показа; настоящую, в валюте игрока, даст магазин приложений.</summary>
    public sealed class HubCashPack
    {
        public string Id = "", Currency = "", Price = "";
        public int Amount;      // всего коинов в наборе
        public int Bonus;       // сколько из них — подарок кассы (лента «+10%»)
    }

    public static class HubCash
    {
        public const string Silver = "silver", Gold = "gold", NoAdsId = "no_ads", NoAdsPrice = "299 ₽";
        public const int AdReward = 50, AdsPerDay = 5;
        // Серебро в золото: золото через серебряный набор выходит вдвое дороже золотого набора — те не теряют смысла.
        public const int SilverPerGold = 25, GoldPerDay = 20;

        // Цены — прикидка для рублей (RuStore); у Google Play и App Store будут свои из их консолей.
        public static readonly IReadOnlyList<HubCashPack> Packs = new[]
        {
            Pack("silver_500", Silver, 500, 0, "79 ₽"),
            Pack("silver_1650", Silver, 1650, 150, "199 ₽"),
            Pack("silver_6250", Silver, 6250, 1250, "549 ₽"),
            Pack("gold_50", Gold, 50, 0, "99 ₽"),
            Pack("gold_165", Gold, 165, 15, "279 ₽"),
            Pack("gold_480", Gold, 480, 80, "699 ₽"),
            Pack("gold_1300", Gold, 1300, 300, "1 490 ₽"),
        };

        public static IEnumerable<HubCashPack> PacksOf(string currency) => Packs.Where(pack => pack.Currency == currency);

        /// <summary>Сколько роликов за награду ещё можно посмотреть сегодня.</summary>
        public static int AdsLeft(HubSnapshot snapshot, long now)
            => snapshot.Cash == null || snapshot.Cash.AdDay != HubShop.DayOf(now) ? AdsPerDay : Math.Max(0, AdsPerDay - snapshot.Cash.AdViews);

        /// <summary>Сколько золота ещё можно купить за серебро сегодня.</summary>
        public static int GoldLeft(HubSnapshot snapshot, long now)
            => snapshot.Cash == null || snapshot.Cash.AdDay != HubShop.DayOf(now) ? GoldPerDay : Math.Max(0, GoldPerDay - snapshot.Cash.GoldBought);

        internal static void Normalize(HubSnapshot snapshot, long now)
        {
            if (snapshot.Cash == null) snapshot.Cash = new HubCashState();
            var day = HubShop.DayOf(now);
            if (snapshot.Cash.AdDay == day) return;
            snapshot.Cash.AdDay = day;
            snapshot.Cash.AdViews = 0;
            snapshot.Cash.GoldBought = 0;
        }

        private static HubCashPack Pack(string id, string currency, int amount, int bonus, string price)
            => new HubCashPack { Id = id, Currency = currency, Amount = amount, Bonus = bonus, Price = price };
    }

    public sealed partial class LocalPlayerHubService
    {
        private string BuyCash(HubSnapshot s, string packId)
        {
            if (packId == HubCash.NoAdsId)
            {
                Require(!s.Cash.NoAds, "owned", "«Без рекламы» уже куплено.");
                s.Cash.NoAds = true;
                s.Cash.Purchases++;
                return "Демо-покупка: реклама между партиями отключена. Деньги не списывались.";
            }
            var pack = HubCash.Packs.FirstOrDefault(p => p.Id == packId);
            Require(pack != null, "pack", "Такого набора в кассе нет.");
            // Купленное — не заработок: не идёт в доход для кредита и не удерживается за долг.
            if (pack!.Currency == HubCash.Gold) s.Profile.Gold = checked(s.Profile.Gold + pack.Amount);
            else s.Profile.Coins = checked(s.Profile.Coins + pack.Amount);
            s.Cash.Purchases++;
            return "Демо-покупка: +" + pack.Amount + (pack.Currency == HubCash.Gold ? " золотых" : " серебряных")
                + " тун-тун коинов. Деньги не списывались.";
        }

        private string BuyGoldForSilver(HubSnapshot s, int gold)
        {
            Require(gold > 0, "amount", "Сколько золота купить?");
            var left = HubCash.GoldPerDay - s.Cash.GoldBought;
            Require(gold <= left, "gold_limit", left > 0 ? "Сегодня за серебро можно взять ещё " + left + " золотых — лимит обновится в полночь."
                : "На сегодня золото за серебро кончилось — лимит обновится в полночь.");
            var silver = checked(gold * HubCash.SilverPerGold);
            Require(silver <= s.Profile.Coins, "coins", "Не хватает серебряных тун-тун коинов: нужно " + silver + ", у тебя " + s.Profile.Coins + ".");
            // Заёмное серебро в золото не превращается: иначе долг Тузяре можно спрятать от коллекторов.
            Require(silver <= HubShop.Spendable(s), "debt",
                "Пока долг Тузяре не закрыт, на золото меняют только свободные тун-тун коины: " + HubShop.Spendable(s) + ".");
            s.Profile.Coins -= silver;
            s.Profile.Gold = checked(s.Profile.Gold + gold);
            s.Cash.GoldBought += gold;
            return "Обмен: −" + silver + " серебряных, +" + gold + " золотых тун-тун коинов";
        }

        private string WatchCashAd(HubSnapshot s)
        {
            Require(s.Cash.AdViews < HubCash.AdsPerDay, "ads", "На сегодня ролики кончились — новые в полночь.");
            s.Cash.AdViews++;
            // Серебро за ролик — заработок, как за партию: при долге коллекторам половина уходит Тузяре.
            Credit(s, HubCash.AdReward);
            return "+" + HubCash.AdReward + " серебряных тун-тун коинов за ролик";
        }
    }
}
