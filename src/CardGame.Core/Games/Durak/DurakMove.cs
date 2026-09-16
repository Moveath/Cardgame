using CardGame.Core.Cards;

namespace CardGame.Core.Games.Durak
{
    public enum DurakMoveType
    {
        Attack,
        Defend,
        Take,
        Pass
    }

    public readonly struct DurakMove
    {
        private DurakMove(DurakMoveType type, Card card, int targetSlot)
        {
            Type = type;
            Card = card;
            TargetSlot = targetSlot;
        }

        public DurakMoveType Type { get; }
        public Card Card { get; }
        public int TargetSlot { get; }

        public static DurakMove Attack(Card card) => new DurakMove(DurakMoveType.Attack, card, -1);

        public static DurakMove Defend(Card card, int targetSlot) => new DurakMove(DurakMoveType.Defend, card, targetSlot);

        public static DurakMove Take() => new DurakMove(DurakMoveType.Take, default, -1);

        public static DurakMove Pass() => new DurakMove(DurakMoveType.Pass, default, -1);

        public override string ToString() => Type switch
        {
            DurakMoveType.Attack => "подкинул " + Card,
            DurakMoveType.Defend => "отбил " + Card,
            DurakMoveType.Take => "забрал карты",
            _ => "пас"
        };
    }
}
