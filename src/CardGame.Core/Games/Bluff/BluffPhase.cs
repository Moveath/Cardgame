namespace CardGame.Core.Games.Bluff
{
    public enum BluffPhase
    {
        /// <summary>Начало круга: ведущий называет ранг и кладёт первый пакет.</summary>
        RoundStart,

        /// <summary>Цепочка идёт: сосед либо проверяет последний пакет, либо кладёт свой под тем же рангом.</summary>
        Chain,

        Finished
    }
}
