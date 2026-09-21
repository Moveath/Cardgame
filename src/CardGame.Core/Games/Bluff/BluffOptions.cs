namespace CardGame.Core.Games.Bluff
{
    /// <summary>
    /// Настройки партии «Верю — не верю». Задаются один раз при создании и дальше не меняются:
    /// правила посреди партии не переобуваются.
    /// </summary>
    public readonly struct BluffOptions
    {
        public BluffOptions(int maxCardsPerThrow, bool allowFourOfAKind, bool acesCanFormFour)
        {
            MaxCardsPerThrow = maxCardsPerThrow < 1 ? 1 : maxCardsPerThrow;
            AllowFourOfAKind = allowFourOfAKind;
            AcesCanFormFour = acesCanFormFour;
        }

        /// <summary>Сколько карт разрешено положить за один бросок. По умолчанию три.</summary>
        public int MaxCardsPerThrow { get; }

        /// <summary>Можно ли сбрасывать каре вне очереди.</summary>
        public bool AllowFourOfAKind { get; }

        /// <summary>
        /// Считается ли каре из тузов. По умолчанию нет: иначе четыре туза уходят законно,
        /// и главный риск игры — остаться с тузами на руках — просто исчезает.
        /// </summary>
        public bool AcesCanFormFour { get; }

        public static BluffOptions Classic => new BluffOptions(3, true, false);
    }
}
