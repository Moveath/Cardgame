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
        /// Проверка строгая и по двум признакам сразу: сходится ли количество и все ли карты
        /// того самого ранга. Соврал хотя бы в одном — соврал целиком.
        /// </summary>
        public static bool IsLie(Rank declaredRank, int declaredCount, IReadOnlyList<Card> cards)
        {
            if (cards.Count != declaredCount)
            {
                return true;
            }

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
