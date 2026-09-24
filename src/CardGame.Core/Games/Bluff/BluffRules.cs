using System;
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

        /// <summary>«вальтов», «дам», «шестёрок» — для строки лога «скинул 3 карты под видом вальтов».</summary>
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
            Rank.Jack => "вальтов",
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

        /// <summary>Имена с беглой гласной: по общему правилу вышло бы «Пётру» и «Павелу».</summary>
        private static readonly Dictionary<string, string> DativeExceptions = new Dictionary<string, string>
        {
            { "Пётр", "Петру" },
            { "Павел", "Павлу" },
            { "Лев", "Льву" },
        };

        /// <summary>
        /// Имя в дательном падеже — для «Аслану порвали туз». Мужские на согласную получают
        /// «-у» (Егору), на «-й» и «-ь» — «-ю» (Сергею, Игорю), на «-а» и «-я» — «-е» (Вере,
        /// Илье), на «-ия» — «-ии» (Марии). Имена на гласную вроде «Али» не склоняются. Номер
        /// повтора остаётся как был: «Егор 2» — «Егору 2».
        /// </summary>
        public static string NameDative(string name)
        {
            var space = name.IndexOf(' ');
            if (space > 0)
            {
                return NameDative(name.Substring(0, space)) + name.Substring(space);
            }

            if (DativeExceptions.TryGetValue(name, out var exception))
            {
                return exception;
            }

            if (name.Length < 2)
            {
                return name;
            }

            var stem = name.Substring(0, name.Length - 1);
            var last = char.ToLowerInvariant(name[name.Length - 1]);

            if (name.EndsWith("ия", StringComparison.OrdinalIgnoreCase))
            {
                return stem + "и";
            }

            switch (last)
            {
                case 'а':
                case 'я':
                    return stem + "е";
                case 'й':
                case 'ь':
                    return stem + "ю";
            }

            return "бвгджзклмнпрстфхцчшщ".IndexOf(last) >= 0 ? name + "у" : name;
        }

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
