using CardGame.Core.Cards;

namespace CardGame.Core.Games.Durak
{
    public enum DurakMoveType
    {
        Attack,
        Defend,
        Take,
        Pass,

        /// <summary>Перевод: защищающийся кладёт карту того же достоинства и передаёт бой дальше.</summary>
        Transfer,

        /// <summary>Подкинутая карта не обязана подходить по достоинству — легальность решает не движок, а то, поймают ли на этом.</summary>
        CheatAttack,

        /// <summary>Отбой заведомо не той картой — легальность решает не движок, а то, поймают ли на этом.</summary>
        CheatDefend,

        /// <summary>Перевод картой не того достоинства — самый выгодный обман: уезжаешь от всей раздачи.</summary>
        CheatTransfer
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

        public static DurakMove Transfer(Card card) => new DurakMove(DurakMoveType.Transfer, card, -1);

        public static DurakMove CheatAttack(Card card) => new DurakMove(DurakMoveType.CheatAttack, card, -1);

        public static DurakMove CheatDefend(Card card, int targetSlot) => new DurakMove(DurakMoveType.CheatDefend, card, targetSlot);

        public static DurakMove CheatTransfer(Card card) => new DurakMove(DurakMoveType.CheatTransfer, card, -1);

        /// <summary>
        /// Текст хода не выдаёт обман — иначе шулерство теряет смысл: соперник должен
        /// заподозрить подмену по самой карте на столе, а не по подписи хода в логе.
        /// </summary>
        public override string ToString() => Type switch
        {
            DurakMoveType.Attack => "подкинул " + Card,
            DurakMoveType.CheatAttack => "подкинул " + Card,
            DurakMoveType.Defend => "отбил " + Card,
            DurakMoveType.CheatDefend => "отбил " + Card,
            DurakMoveType.Transfer => "перевёл " + Card,
            DurakMoveType.CheatTransfer => "перевёл " + Card,
            DurakMoveType.Take => "забрал карты",
            _ => "пас"
        };
    }
}
