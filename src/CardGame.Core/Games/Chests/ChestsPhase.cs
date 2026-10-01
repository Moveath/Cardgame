namespace CardGame.Core.Games.Chests
{
    /// <summary>
    /// Один вопрос идёт по ступенькам: ранг, количество, масти. Ошибка на любой ступеньке
    /// передаёт ход, поэтому фаза — это то, на какой ступеньке сейчас стоит спрашивающий.
    /// </summary>
    public enum ChestsPhase
    {
        /// <summary>Выбрать соперника и назвать ранг: «У тебя есть вальты?»</summary>
        Asking,

        /// <summary>Ранг у адресата есть — теперь угадать, сколько именно.</summary>
        GuessingCount,

        /// <summary>Количество угадано — осталось назвать весь набор мастей.</summary>
        GuessingSuits,

        Finished
    }
}
