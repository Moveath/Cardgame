using CardGame.Core.Cards;

namespace CardGame.Core.Games.Durak
{
    public sealed class TableSlot
    {
        public TableSlot(Card attack)
        {
            Attack = attack;
        }

        public Card Attack { get; }
        public Card? Defense { get; internal set; }
        public bool IsDefended => Defense.HasValue;

        public override string ToString() => IsDefended ? Attack + "/" + Defense : Attack.ToString();
    }
}
