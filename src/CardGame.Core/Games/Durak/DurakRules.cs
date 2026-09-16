using CardGame.Core.Cards;

namespace CardGame.Core.Games.Durak
{
    public static class DurakRules
    {
        public const int HandSize = 6;
        public const int MaxTableSlots = 6;

        public static bool Beats(Card defense, Card attack, Suit trump)
        {
            if (defense.Suit == attack.Suit)
            {
                return defense.Rank > attack.Rank;
            }

            return defense.Suit == trump;
        }
    }
}
