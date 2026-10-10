using System;
using System.Globalization;

namespace CardGame.Core.Meta
{
    /// <summary>Versioned signed terms shared by the service, contract and repayment screen.</summary>
    public static class HubLoans
    {
        public const int LegacyTermsVersion = 1, CurrentTermsVersion = 2;
        public const int HonestInterestPercent = 20, EarlyInterestPercent = 10, HonestDays = 3;
        public const long EarlySeconds = 24 * 3600, ExtensionSeconds = 24 * 3600;
        public const long CollectorDelay = 0, LegacyCollectorDelay = 12 * 3600;

        public static int Version(HubBank bank) => bank.TermsVersion >= CurrentTermsVersion ? CurrentTermsVersion : LegacyTermsVersion;
        public static int InterestPercent(string trick) => InterestPercent(trick, CurrentTermsVersion);
        public static int InterestPercent(string trick, int version)
        {
            var normal = version >= CurrentTermsVersion ? HonestInterestPercent : 10;
            if (trick == "daily") return normal * HonestDays; // Existing signed daily-rate clauses retain their terms.
            return version >= CurrentTermsVersion && trick == "rate" ? 40 : normal;
        }
        public static int FeePercent(string trick) => FeePercent(trick, CurrentTermsVersion);
        public static int FeePercent(string trick, int version) => version >= CurrentTermsVersion && trick == "fee" ? 20 : 0;
        public static int Days(string trick) => trick == "short" ? 2 : HonestDays;
        public static int Debt(int principal, string trick) => Debt(principal, trick, CurrentTermsVersion);
        public static int Debt(int principal, string trick, int version) => Total(principal, InterestPercent(trick, version) + FeePercent(trick, version));
        private static int Total(int principal, int interest)
        {
            if (principal < 0) throw new ArgumentOutOfRangeException(nameof(principal));
            return checked((int)(principal + ((long)principal * interest + 99) / 100));
        }

        public static HubLoanPayoff PayoffQuote(HubBank bank, long now)
        {
            var amount = Math.Max(0, bank.Debt);
            var early = amount > 0 && Version(bank) >= CurrentTermsVersion && now >= bank.SignedAt
                && now < bank.SignedAt + EarlySeconds && now < bank.DueAt && !bank.OverdueDoubled;
            if (!early) return new HubLoanPayoff { Amount = amount };
            var earlyAmount = Math.Max(0, Total(bank.Principal, EarlyInterestPercent) - bank.Repaid);
            var payable = Math.Min(amount, earlyAmount);
            return new HubLoanPayoff { Amount = payable, Discount = amount - payable, Early = true };
        }

        public static bool HasStakeRestriction(HubBank bank, long now)
            => bank.Debt > 0 && (Version(bank) < CurrentTermsVersion || bank.CollectorPending
                || bank.CollectionActive || now >= bank.DueAt);

        public static bool WithholdsIncome(HubBank bank)
            => bank.Debt > 0 && (Version(bank) < CurrentTermsVersion || bank.CollectionActive);

        internal static HubLoanOffer Offer(string playerId, int number, int lastTrickNumber)
        {
            var hash = OfferHash(playerId + ":loan:" + number.ToString(CultureInfo.InvariantCulture));
            // A persisted last attempt makes correction/refusal unable to reroll the next offer.
            // Gaps of 2–4 offers keep the first contract honest and forbid adjacent tricks.
            var gapHash = OfferHash(playerId + ":loan-gap:" + lastTrickNumber.ToString(CultureInfo.InvariantCulture));
            var nextTrick = (long)lastTrickNumber + 2 + gapHash % 3;
            var tricks = new[] { "rate", "fee", "short" };
            var trick = number > 1 && number >= nextTrick ? tricks[hash % 3] : "";
            return new HubLoanOffer
            {
                Id = "loan-" + number.ToString(CultureInfo.InvariantCulture) + "-" + hash.ToString("x8", CultureInfo.InvariantCulture),
                Number = number, Trick = trick, OriginalTrick = trick
            };
        }

        private static uint OfferHash(string seed)
        {
            uint hash = 2166136261;
            unchecked
            {
                foreach (var letter in seed) hash = (hash ^ letter) * 16777619;
                hash ^= hash >> 16; hash *= 0x7feb352d; hash ^= hash >> 15;
                hash *= 0x846ca68b; return hash ^ (hash >> 16);
            }
        }
    }
}
