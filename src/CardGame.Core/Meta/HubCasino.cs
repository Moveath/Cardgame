using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CardGame.Core.Meta
{
    /// <summary>
    /// Казино Тузяры и Дамы треф — «Карточный дворец» (2026-10-22, IDEAS.md «Казино Тузяры»):
    /// - Внутренняя валюта казино: Золотые жетоны Тузяры (<see cref="HubCasinoState.Chips"/>).
    ///   В кассе у Дамы треф: раз в день бесплатный комплимент (+3 жетона), покупка жетонов за свободные коины
    ///   (1 жетон = 10 коинов), обратный обмен жетонов в серебро (1 жетон = 9 коинов, комиссия заведения 10 %)
    ///   и выкуп редких призов Хайроллера за жетоны.
    /// - Зона 1 (Карточные столы слева): Покер (открывает стол покера) и «21 / Блэкджек» против крупье Десятки треф.
    /// - Зона 2 (Стол Крупье и Рулетка справа): Карточная Рулетка (Красное/Чёрное ×2, Масть ×4, Картинка ×3, Джокер ×14)
    ///   и Кости Тузяры (Меньше 7 ×2, Больше 7 ×2, Ровно 7 ×5).
    /// - Зона 3 (Арка Автоматов справа в глубине): Слоты «777» (3 барабана) и Крэш-игра «Туз-Ракета» (множитель растёт,
    ///   забрать куш до взрыва).
    /// </summary>
    [Serializable]
    public sealed class HubCasinoState
    {
        public int Chips;
        public long DailyDay;
        public int Nonce;

        // 21 / Блэкджек
        public bool BjActive;
        public int BjBet;
        public List<int> BjPlayer = new List<int>();
        public List<int> BjDealer = new List<int>();
        public string BjOutcome = "";
        public string BjSummary = "";

        // Рулетка и Кости
        public int RouletteCard = -1; // -1 — ещё не крутили, 0..51 — карта, 52 — Джокер
        public int RouletteLastWin;
        public string RouletteSummary = "";
        public int DiceA;
        public int DiceB;
        public int DiceLastWin;
        public string DiceSummary = "";

        // Слоты «777»
        public List<int> SlotReels = new List<int> { 6, 6, 6 };
        public int SlotLastWin;
        public string SlotSummary = "";

        // Крэш-игра «Туз-Ракета»
        public bool RocketActive;
        public bool RocketCrashed;
        public int RocketBet;
        public int RocketStep;
        public int RocketCrashStep;
        public int RocketMultPermille = 1000;
        public int RocketLastWin;
        public string RocketSummary = "";
    }

    public static class HubCasino
    {
        public const int ChipBuyCoins = 10;
        public const int ChipCashoutCoins = 9;
        public const int DailyFreeChips = 3;

        public static readonly int[] RocketStepsPermille = { 1000, 1300, 1700, 2200, 3000, 4200, 6000, 9000, 15000 };
        public static readonly string[] SlotSymbols = { "🍒", "♦", "♠", "♣", "👑", "💎", "777" };
        public static readonly string[] SlotNames = { "Вишня", "Бубны", "Пики", "Трефы", "Корона", "Алмаз", "Семёрки 777" };

        public static bool CanClaimDaily(HubSnapshot s, long now)
            => s.Casino == null || s.Casino.DailyDay != HubShop.DayOf(now);

        public static int PrizeChipCost(HubCatalogItem item)
            => Math.Max(15, (item.Price + ChipBuyCoins - 1) / ChipBuyCoins);

        public static List<HubCatalogItem> HighRollerPrizes(HubSnapshot s)
        {
            var owned = new HashSet<string>(s.Inventory.Where(i => i.Quantity > 0).Select(i => i.ItemId));
            return s.Catalog
                .Where(i => HubShop.ForSale(i) && !owned.Contains(i.Id) && (i.Rarity == HubShop.Rare || i.Price >= 350))
                .OrderByDescending(i => i.Price)
                .Take(6)
                .ToList();
        }

        public static int HandScore(IEnumerable<int> cards)
        {
            var total = 0;
            var aces = 0;
            foreach (var code in cards)
            {
                var rank = CardRank(code);
                if (rank == 14) { aces++; total += 11; }
                else if (rank >= 10) total += 10;
                else total += rank;
            }
            while (total > 21 && aces > 0)
            {
                total -= 10;
                aces--;
            }
            return total;
        }

        public static int CardRank(int code) => code == 52 ? 15 : 2 + (code / 4);
        public static int CardSuit(int code) => code & 3;

        public static string CardLabel(int code)
        {
            if (code < 0) return "—";
            if (code >= 52) return "🃏 Джокер";
            return HubMarket.CardLabel(CardRank(code), CardSuit(code));
        }

        public static bool IsRedCard(int code)
            => code >= 0 && code < 52 && HubMarket.IsRedSuit(CardSuit(code));

        public static string FormatMultiplier(int permille)
            => (permille / 1000.0).ToString("0.00", CultureInfo.InvariantCulture) + "x";

        internal static void Normalize(HubSnapshot s)
        {
            if (s.Casino == null) s.Casino = new HubCasinoState();
            if (s.Casino.BjPlayer == null) s.Casino.BjPlayer = new List<int>();
            if (s.Casino.BjDealer == null) s.Casino.BjDealer = new List<int>();
            if (s.Casino.SlotReels == null || s.Casino.SlotReels.Count != 3)
                s.Casino.SlotReels = new List<int> { 6, 6, 6 };
            if (s.Casino.RocketMultPermille <= 0) s.Casino.RocketMultPermille = 1000;
        }

        internal static uint NextSeed(HubSnapshot s, long now, string salt)
        {
            s.Casino.Nonce = checked(s.Casino.Nonce + 1);
            var text = s.Profile.Id + "|casino|" + now.ToString(CultureInfo.InvariantCulture) + "|"
                       + s.Casino.Nonce.ToString(CultureInfo.InvariantCulture) + "|" + salt;
            unchecked
            {
                uint hash = 2166136261;
                foreach (var ch in text) hash = (hash ^ ch) * 16777619;
                if (hash == 0) hash = 0x9e3779b9;
                hash ^= hash << 13;
                hash ^= hash >> 17;
                hash ^= hash << 5;
                return hash;
            }
        }
    }

    public sealed partial class LocalPlayerHubService
    {
        private string ClaimCasinoDaily(HubSnapshot s)
        {
            HubCasino.Normalize(s);
            var today = HubShop.DayOf(Now);
            Require(s.Casino.DailyDay != today, "casino_daily", "Сегодня ты уже получал комплимент от Дамы треф. Загляни завтра!");
            s.Casino.DailyDay = today;
            s.Casino.Chips = checked(s.Casino.Chips + HubCasino.DailyFreeChips);
            return "Комплимент от Дамы треф: +" + HubCasino.DailyFreeChips + " золотых жетона!";
        }

        private string BuyCasinoChips(HubSnapshot s, int chips)
        {
            HubCasino.Normalize(s);
            Require(chips > 0 && chips <= 1000, "amount", "Выбери количество жетонов.");
            var cost = checked(chips * HubCasino.ChipBuyCoins);
            Require(cost <= s.Profile.Coins, "coins", "Не хватает коинов: нужно " + cost + ", у тебя " + s.Profile.Coins + ".");
            Require(cost <= HubShop.Spendable(s), "debt",
                "Заёмные деньги Банка в кассе Казино не принимают: свободно " + HubShop.Spendable(s) + " коинов.");
            s.Profile.Coins -= cost;
            s.Casino.Chips = checked(s.Casino.Chips + chips);
            return "Куплено " + chips + " жетонов · −" + cost + " коинов";
        }

        private string CashoutCasinoChips(HubSnapshot s, int chips)
        {
            HubCasino.Normalize(s);
            Require(chips > 0 && chips <= s.Casino.Chips, "chips", "Недостаточно жетонов для обмена.");
            var coins = checked(chips * HubCasino.ChipCashoutCoins);
            s.Casino.Chips -= chips;
            s.Profile.Coins = checked(s.Profile.Coins + coins);
            return "Обменяно " + chips + " жетонов · +" + coins + " коинов";
        }

        private string RedeemCasinoPrize(HubSnapshot s, string itemId)
        {
            HubCasino.Normalize(s);
            var item = HubCasino.HighRollerPrizes(s).FirstOrDefault(i => i.Id == itemId);
            Require(item != null, "prize", "Этот приз сейчас недоступен в витрине Дамы треф.");
            var cost = HubCasino.PrizeChipCost(item!);
            Require(s.Casino.Chips >= cost, "chips", "Нужно " + cost + " жетонов, у тебя " + s.Casino.Chips + ".");
            s.Casino.Chips -= cost;
            GrantItem(s, item!.Id, 1);
            return "Приз Хайроллера получен: «" + item.Name + "»!";
        }

        private string PlayCasinoBlackjack(HubSnapshot s, string subAction, int bet)
        {
            HubCasino.Normalize(s);
            var c = s.Casino;
            if (subAction == "start")
            {
                Require(!c.BjActive, "bj_active", "Сначала доиграй текущую раздачу 21.");
                Require(bet > 0 && bet <= 100, "bet", "Выбери ставку от 1 до 100 жетонов.");
                Require(c.Chips >= bet, "chips", "Не хватает жетонов казино: нужно " + bet + ".");
                c.Chips -= bet;
                c.BjBet = bet;
                c.BjActive = true;
                c.BjOutcome = "";
                c.BjPlayer.Clear();
                c.BjDealer.Clear();
                c.BjPlayer.Add(DrawBjCard(s));
                c.BjDealer.Add(DrawBjCard(s));
                c.BjPlayer.Add(DrawBjCard(s));
                c.BjDealer.Add(DrawBjCard(s));

                var pScore = HubCasino.HandScore(c.BjPlayer);
                if (pScore == 21)
                {
                    return FinishBlackjack(s, natural: true);
                }
                c.BjSummary = "Раздача! У тебя " + pScore + " очк. Крупье открыл " + HubCasino.CardLabel(c.BjDealer[0]) + ".";
                return c.BjSummary;
            }

            Require(c.BjActive, "bj_inactive", "Сначала начни раздачу 21.");
            if (subAction == "double")
            {
                Require(c.BjPlayer.Count == 2, "bj_double", "Удвоить ставку можно только на первых двух картах.");
                Require(c.Chips >= c.BjBet, "chips", "Не хватает жетонов для удвоения ставки.");
                c.Chips -= c.BjBet;
                c.BjBet *= 2;
                c.BjPlayer.Add(DrawBjCard(s));
                if (HubCasino.HandScore(c.BjPlayer) > 21)
                {
                    c.BjActive = false;
                    c.BjOutcome = "lose";
                    c.BjSummary = "Перебор (" + HubCasino.HandScore(c.BjPlayer) + ") после удвоения! Ставка уходит заведению.";
                    return c.BjSummary;
                }
                return FinishBlackjack(s, natural: false);
            }

            if (subAction == "hit")
            {
                c.BjPlayer.Add(DrawBjCard(s));
                var score = HubCasino.HandScore(c.BjPlayer);
                if (score > 21)
                {
                    c.BjActive = false;
                    c.BjOutcome = "lose";
                    c.BjSummary = "Перебор (" + score + " очк.)! Десятка треф забирает фишки.";
                    return c.BjSummary;
                }
                if (score == 21)
                {
                    return FinishBlackjack(s, natural: false);
                }
                c.BjSummary = "Взята карта " + HubCasino.CardLabel(c.BjPlayer[c.BjPlayer.Count - 1]) + " · у тебя " + score + " очк.";
                return c.BjSummary;
            }

            if (subAction == "stand")
            {
                return FinishBlackjack(s, natural: false);
            }

            throw new HubFailure("bj_action", "Неизвестное действие в 21.");
        }

        private int DrawBjCard(HubSnapshot s)
        {
            var used = new HashSet<int>(s.Casino.BjPlayer.Concat(s.Casino.BjDealer));
            for (var attempt = 0; attempt < 30; attempt++)
            {
                var card = (int)(HubCasino.NextSeed(s, Now, "bj" + attempt) % 52);
                if (!used.Contains(card)) return card;
            }
            return (int)(HubCasino.NextSeed(s, Now, "bj_fallback") % 52);
        }

        private string FinishBlackjack(HubSnapshot s, bool natural)
        {
            var c = s.Casino;
            c.BjActive = false;
            while (HubCasino.HandScore(c.BjDealer) < 17)
                c.BjDealer.Add(DrawBjCard(s));

            var p = HubCasino.HandScore(c.BjPlayer);
            var d = HubCasino.HandScore(c.BjDealer);
            if (natural && d != 21)
            {
                var payout = c.BjBet * 5 / 2;
                if (payout <= c.BjBet * 2) payout = c.BjBet * 2 + 1;
                c.Chips = checked(c.Chips + payout);
                c.BjOutcome = "blackjack";
                c.BjSummary = "БЛЭКДЖЕК 21! Выплата +" + payout + " жетонов!";
                return c.BjSummary;
            }
            if (d > 21 || p > d)
            {
                var payout = c.BjBet * 2;
                c.Chips = checked(c.Chips + payout);
                c.BjOutcome = "win";
                c.BjSummary = "Победа! " + p + " против " + d + " у крупье · +" + payout + " жетонов!";
                return c.BjSummary;
            }
            if (p == d)
            {
                c.Chips = checked(c.Chips + c.BjBet);
                c.BjOutcome = "push";
                c.BjSummary = "Ровно " + p + " на " + d + " — ничья, ставка (" + c.BjBet + " жет.) возвращена.";
                return c.BjSummary;
            }
            c.BjOutcome = "lose";
            c.BjSummary = "У крупье " + d + " против твоих " + p + " — выиграло заведение.";
            return c.BjSummary;
        }

        private string SpinCasinoRoulette(HubSnapshot s, string betType, int bet)
        {
            HubCasino.Normalize(s);
            var c = s.Casino;
            Require(bet > 0 && bet <= 100, "bet", "Выбери ставку от 1 до 100 жетонов.");
            Require(c.Chips >= bet, "chips", "Не хватает жетонов: нужно " + bet + ".");
            c.Chips -= bet;

            // 0..51 — колода, 52 — сектор Джокера (зелёное зеро)
            var pocket = (int)(HubCasino.NextSeed(s, Now, "roulette|" + betType) % 53);
            c.RouletteCard = pocket;
            var mult = 0;
            switch (betType)
            {
                case "red":
                    if (pocket < 52 && HubCasino.IsRedCard(pocket)) mult = 2;
                    break;
                case "black":
                    if (pocket < 52 && !HubCasino.IsRedCard(pocket)) mult = 2;
                    break;
                case "clubs":
                    if (pocket < 52 && HubCasino.CardSuit(pocket) == 0) mult = 4;
                    break;
                case "diamonds":
                    if (pocket < 52 && HubCasino.CardSuit(pocket) == 1) mult = 4;
                    break;
                case "hearts":
                    if (pocket < 52 && HubCasino.CardSuit(pocket) == 2) mult = 4;
                    break;
                case "spades":
                    if (pocket < 52 && HubCasino.CardSuit(pocket) == 3) mult = 4;
                    break;
                case "face":
                    if (pocket < 52 && HubCasino.CardRank(pocket) >= 11) mult = 3;
                    break;
                case "joker":
                    if (pocket == 52) mult = 14;
                    break;
                default:
                    throw new HubFailure("bet_type", "Выбери сектор рулетки.");
            }

            var label = HubCasino.CardLabel(pocket);
            if (mult > 0)
            {
                var win = checked(bet * mult);
                c.Chips = checked(c.Chips + win);
                c.RouletteLastWin = win;
                c.RouletteSummary = "Выпало " + label + " — ПОБЕДА ×" + mult + " (+" + win + " жетонов)!";
            }
            else
            {
                c.RouletteLastWin = 0;
                c.RouletteSummary = "Выпало " + label + " — мимо.";
            }
            return c.RouletteSummary;
        }

        private string RollCasinoDice(HubSnapshot s, string choice, int bet)
        {
            HubCasino.Normalize(s);
            var c = s.Casino;
            Require(bet > 0 && bet <= 100, "bet", "Выбери ставку от 1 до 100 жетонов.");
            Require(c.Chips >= bet, "chips", "Не хватает жетонов: нужно " + bet + ".");
            c.Chips -= bet;

            c.DiceA = 1 + (int)(HubCasino.NextSeed(s, Now, "diceA") % 6);
            c.DiceB = 1 + (int)(HubCasino.NextSeed(s, Now, "diceB") % 6);
            var sum = c.DiceA + c.DiceB;
            var mult = choice switch
            {
                "under7" => sum < 7 ? 2 : 0,
                "seven" => sum == 7 ? 5 : 0,
                "over7" => sum > 7 ? 2 : 0,
                _ => throw new HubFailure("dice_choice", "Выбери исход броска костей."),
            };
            if (mult > 0)
            {
                var win = checked(bet * mult);
                c.Chips = checked(c.Chips + win);
                c.DiceLastWin = win;
                c.DiceSummary = "Кости: " + c.DiceA + " + " + c.DiceB + " = " + sum + " · ПОБЕДА ×" + mult + " (+" + win + " жет.)!";
            }
            else
            {
                c.DiceLastWin = 0;
                c.DiceSummary = "Кости: " + c.DiceA + " + " + c.DiceB + " = " + sum + " · мимо.";
            }
            return c.DiceSummary;
        }

        private string SpinCasinoSlots(HubSnapshot s, int bet)
        {
            HubCasino.Normalize(s);
            var c = s.Casino;
            Require(bet > 0 && bet <= 100, "bet", "Выбери ставку от 1 до 100 жетонов.");
            Require(c.Chips >= bet, "chips", "Не хватает жетонов: нужно " + bet + ".");
            c.Chips -= bet;

            var r0 = (int)(HubCasino.NextSeed(s, Now, "slot0") % 7);
            var r1 = (int)(HubCasino.NextSeed(s, Now, "slot1") % 7);
            var r2 = (int)(HubCasino.NextSeed(s, Now, "slot2") % 7);
            c.SlotReels = new List<int> { r0, r1, r2 };

            var mult = 0;
            if (r0 == r1 && r1 == r2)
            {
                mult = r0 == 6 ? 25 : r0 >= 4 ? 12 : 6;
            }
            else if (r0 == r1 || r1 == r2 || r0 == r2)
            {
                mult = 2;
            }

            var line = HubCasino.SlotSymbols[r0] + " · " + HubCasino.SlotSymbols[r1] + " · " + HubCasino.SlotSymbols[r2];
            if (mult > 0)
            {
                var win = checked(bet * mult);
                c.Chips = checked(c.Chips + win);
                c.SlotLastWin = win;
                c.SlotSummary = line + " — ВЫИГРЫШ ×" + mult + " (+" + win + " жетонов)!";
            }
            else
            {
                c.SlotLastWin = 0;
                c.SlotSummary = line + " — без совпадений.";
            }
            return c.SlotSummary;
        }

        private string PlayCasinoRocket(HubSnapshot s, string subAction, int bet)
        {
            HubCasino.Normalize(s);
            var c = s.Casino;
            if (subAction == "launch")
            {
                Require(!c.RocketActive, "rocket_active", "Ракета уже в полёте!");
                Require(bet > 0 && bet <= 100, "bet", "Выбери ставку от 1 до 100 жетонов.");
                Require(c.Chips >= bet, "chips", "Не хватает жетонов: нужно " + bet + ".");
                c.Chips -= bet;
                c.RocketBet = bet;
                c.RocketActive = true;
                c.RocketCrashed = false;
                c.RocketStep = 0;
                c.RocketMultPermille = 1000;
                c.RocketLastWin = 0;
                // Взрыв на шаге 1..8 (взвешенно)
                var roll = (int)(HubCasino.NextSeed(s, Now, "rocket") % 100);
                c.RocketCrashStep = roll < 22 ? 1 : roll < 45 ? 2 : roll < 65 ? 3 : roll < 80 ? 4 : roll < 90 ? 5 : roll < 96 ? 6 : roll < 99 ? 7 : 8;
                c.RocketSummary = "Ракета Тузяры стартовала (1.00x)! Жми «ЕЩЁ ВЫШЕ» или «ЗАБРАТЬ КУШ».";
                return c.RocketSummary;
            }

            Require(c.RocketActive, "rocket_inactive", "Сначала запусти ракету.");
            if (subAction == "step")
            {
                var nextStep = c.RocketStep + 1;
                if (nextStep >= c.RocketCrashStep)
                {
                    c.RocketActive = false;
                    c.RocketCrashed = true;
                    c.RocketStep = nextStep;
                    c.RocketLastWin = 0;
                    c.RocketSummary = "БУМ! Ракета Тузяры взорвалась на взлёте! Ставка сгорела.";
                    return c.RocketSummary;
                }
                c.RocketStep = nextStep;
                c.RocketMultPermille = HubCasino.RocketStepsPermille[Math.Min(nextStep, HubCasino.RocketStepsPermille.Length - 1)];
                if (nextStep >= HubCasino.RocketStepsPermille.Length - 1)
                {
                    return CashoutRocket(s);
                }
                c.RocketSummary = "Высота растёт: " + HubCasino.FormatMultiplier(c.RocketMultPermille) + "! Забрать сейчас или рискнуть выше?";
                return c.RocketSummary;
            }

            if (subAction == "cashout")
            {
                Require(c.RocketStep > 0, "rocket_step", "Сделай хотя бы один рывок вверх перед выводом!");
                return CashoutRocket(s);
            }

            throw new HubFailure("rocket_action", "Неизвестная команда ракеты.");
        }

        private string CashoutRocket(HubSnapshot s)
        {
            var c = s.Casino;
            c.RocketActive = false;
            c.RocketCrashed = false;
            var win = Math.Max(c.RocketBet + 1, c.RocketBet * c.RocketMultPermille / 1000);
            c.Chips = checked(c.Chips + win);
            c.RocketLastWin = win;
            c.RocketSummary = "Успел выпрыгнуть на " + HubCasino.FormatMultiplier(c.RocketMultPermille) + " · +" + win + " жетонов!";
            return c.RocketSummary;
        }
    }
}
