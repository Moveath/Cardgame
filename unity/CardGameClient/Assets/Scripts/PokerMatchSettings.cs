namespace CardGameClient
{
    /// <summary>Локальный безлимитный холдем с фиксированными стартовыми условиями.</summary>
    public struct PokerMatchSettings
    {
        public int PlayerCount;
        public int StartingStack => 2000;
        public int SmallBlind => 10;
        public int BigBlind => 20;
        public static PokerMatchSettings Default => new PokerMatchSettings { PlayerCount = 4 };
    }
}
