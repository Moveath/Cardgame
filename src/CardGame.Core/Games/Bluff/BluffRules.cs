using System.Collections.Generic;
using CardGame.Core.Cards;

namespace CardGame.Core.Games.Bluff
{
    /// <summary>
    /// Чистые правила блефа без состояния партии: что можно заявлять, что считается ложью,
    /// как собирается каре. Отдельно от <see cref="BluffGame"/>, чтобы это можно было
    /// проверять тестами и переиспользовать в ботах и подсказках интерфейса.
    /// </summary>
    public static class BluffRules
    {
        /// <summary>
        /// Тузами заявляться нельзя вообще никогда — в этом вся их роль. Туз уходит с руки
        /// только враньём: положил под видом королей и надейся, что не проверят. Джокеров в
        /// блефе нет, но если колода их принесёт, заявлять их тоже нечего.
        /// </summary>
        public static bool CanBeDeclared(Rank rank) => rank != Rank.Ace && rank != Rank.Joker;

        /// <summary>
        /// Ложь — это несовпадение по рангу, и только по нему. Количеством не врут:
        /// пакет кладут на виду, сколько карт легло — столько и объявлено. Достаточно
        /// одной чужой карты в пачке, чтобы весь бросок считался обманом.
        /// </summary>
        public static bool IsLie(Rank declaredRank, IReadOnlyList<Card> cards)
        {
            foreach (var card in cards)
            {
                if (card.Rank != declaredRank)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Ранги, из которых на руке набралось каре и которые разрешено сбросить.</summary>
        public static IReadOnlyList<Rank> FourOfAKind(IReadOnlyList<Card> hand, bool acesCount)
        {
            var counts = new Dictionary<Rank, int>();
            foreach (var card in hand)
            {
                counts.TryGetValue(card.Rank, out var seen);
                counts[card.Rank] = seen + 1;
            }

            var result = new List<Rank>();
            foreach (var pair in counts)
            {
                if (pair.Value < 4)
                {
                    continue;
                }

                if (pair.Key == Rank.Ace && !acesCount)
                {
                    continue;
                }

                result.Add(pair.Key);
            }

            result.Sort();
            return result;
        }

        /// <summary>«валетов», «дам», «шестёрок» — для строки лога «скинул 3 карты под видом валетов».</summary>
        public static string RankPlural(Rank rank) => rank switch
        {
            Rank.Two => "двоек",
            Rank.Three => "троек",
            Rank.Four => "четвёрок",
            Rank.Five => "пятёрок",
            Rank.Six => "шестёрок",
            Rank.Seven => "семёрок",
            Rank.Eight => "восьмёрок",
            Rank.Nine => "девяток",
            Rank.Ten => "десяток",
            Rank.Jack => "валетов",
            Rank.Queen => "дам",
            Rank.King => "королей",
            Rank.Ace => "тузов",
            _ => "джокеров"
        };

        /// <summary>
        /// «на королей», «на шестёрки» — винительный падеж для строки «перестрелка на ...».
        /// У фигур он совпадает с родительным (одушевлённые), у числовых — с именительным.
        /// </summary>
        public static string RankPluralAccusative(Rank rank) => rank switch
        {
            Rank.Two => "двойки",
            Rank.Three => "тройки",
            Rank.Four => "четвёрки",
            Rank.Five => "пятёрки",
            Rank.Six => "шестёрки",
            Rank.Seven => "семёрки",
            Rank.Eight => "восьмёрки",
            Rank.Nine => "девятки",
            Rank.Ten => "десятки",
            _ => RankPlural(rank)
        };

        /// <summary>Согласование «1 карту», «2 карты», «5 карт» — бросок больше трёх карт не бывает, но пусть будет общим.</summary>
        public static string CardsWord(int count)
        {
            var tail = count % 100;
            if (tail >= 11 && tail <= 14)
            {
                return "карт";
            }

            return (count % 10) switch
            {
                1 => "карту",
                2 => "карты",
                3 => "карты",
                4 => "карты",
                _ => "карт"
            };
        }
    }
}
