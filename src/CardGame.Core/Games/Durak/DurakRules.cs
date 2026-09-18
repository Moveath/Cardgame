using CardGame.Core.Cards;

namespace CardGame.Core.Games.Durak
{
    public static class DurakRules
    {
        public const int HandSize = 6;
        public const int MaxTableSlots = 6;

        public static bool Beats(Card defense, Card attack, Suit trump)
        {
            // Джокер отбивается от чего угодно, включая козыря; отбить сам джокер может только другой джокер.
            if (defense.IsJoker)
            {
                return true;
            }

            if (attack.IsJoker)
            {
                return false;
            }

            if (defense.Suit == attack.Suit)
            {
                return defense.Rank > attack.Rank;
            }

            return defense.Suit == trump;
        }
    }
}
