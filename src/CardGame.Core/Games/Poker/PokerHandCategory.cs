namespace CardGame.Core.Games.Poker
{
    // Ascending poker strength; suits never break a tie.
    public enum PokerHandCategory
    {
        HighCard,
        OnePair,
        TwoPair,
        ThreeOfAKind,
        Straight,
        Flush,
        FullHouse,
        FourOfAKind,
        StraightFlush
    }
}
