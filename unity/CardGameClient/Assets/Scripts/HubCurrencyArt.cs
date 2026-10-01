using UnityEngine;

namespace CardGameClient
{
    /// <summary>Visual currency identities only; balances and earning rules belong to the economy service.</summary>
    public static class HubCurrencyArt
    {
        public const string SilverName = "Серебряные тун-тун коины";
        public const string GoldName = "Золотые тун-тун коины";

        private static Sprite _silver;
        private static Sprite _gold;
        private static bool _loaded;

        public static Sprite Silver { get { Load(); return _silver; } }
        public static Sprite Gold { get { Load(); return _gold; } }

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            foreach (var sprite in Resources.LoadAll<Sprite>("Currency/tun-tun-coins"))
            {
                if (sprite.name == "tun-tun-silver") _silver = sprite;
                else if (sprite.name == "tun-tun-gold") _gold = sprite;
            }
            if (_silver == null || _gold == null)
                Debug.LogWarning("Currency artwork is missing: Resources/Currency/tun-tun-coins.");
        }
    }
}
