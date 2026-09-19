namespace CardGame.Core.Games.Durak
{
    /// <summary>Кто, кроме атакующего, может подкидывать карты в раздачу.</summary>
    public enum ThrowInMode
    {
        /// <summary>Классика: подкинуть может любой игрок, кроме защищающегося.</summary>
        All,

        /// <summary>Только сосед-атакующий и игрок сразу после защищающегося — не весь стол.</summary>
        NeighboursOnly
    }

    /// <summary>
    /// Необязательные варианты правил поверх классического дурака — задаются один раз
    /// при создании партии (например, из настроек комнаты) и не меняются на ходу.
    /// </summary>
    public readonly struct DurakOptions
    {
        public DurakOptions(bool allowTransfer, ThrowInMode throwInMode, bool allowCheating)
        {
            AllowTransfer = allowTransfer;
            ThrowInMode = throwInMode;
            AllowCheating = allowCheating;
        }

        /// <summary>Переводной: защищающийся может отдать атаку следующему игроку картой той же масти... то есть достоинства.</summary>
        public bool AllowTransfer { get; }

        public ThrowInMode ThrowInMode { get; }

        /// <summary>Шулерство: игрокам разрешено подсовывать заведомо нелегальные карты под видом честных.</summary>
        public bool AllowCheating { get; }

        public static DurakOptions Classic => new DurakOptions(false, ThrowInMode.All, false);
    }
}
