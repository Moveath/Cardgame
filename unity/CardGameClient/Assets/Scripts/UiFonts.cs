using UnityEngine;

namespace CardGameClient
{
    internal static class UiFonts
    {
        private static Font _symbols;
        // Embedded font data: Android must not depend on the desktop OS font fallback for ♠♥♦♣.
        public static Font Symbols => _symbols != null ? _symbols : _symbols = Resources.Load<Font>("Fonts/GameSans");
        public static Font Body => Application.isMobilePlatform || MobileUi.Phone
            ? Symbols : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }
}
