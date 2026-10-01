using System;
using System.Collections.Generic;
using CardGame.Core.Cards;
using CardGame.Core.Games.Bluff;

namespace CardGame.Core.Games.Chests
{
    /// <summary>
    /// Чистые правила «Сундучков» без состояния партии: что можно спрашивать, какие числа
    /// предлагать, когда масти уже известны. Отдельно от <see cref="ChestsGame"/>, чтобы ими
    /// пользовались боты и интерфейс, а тесты проверяли их по отдельности. Здесь же слова для
    /// лога: «два вальта», «пики и черви», «три сундучка».
    /// </summary>
    public static class ChestsRules
    {
        /// <summary>Сундучок — четыре карты одного ранга, по одной каждой масти.</summary>
        public const int ChestSize = 4;

        /// <summary>
        /// Какие ранги можно спрашивать. С картами на руках — только свои: спросить вальтов
        /// можно, лишь держа хотя бы одного. Пустая рука — исключение: такой игрок не выбывает
        /// и спрашивает любой ранг, который ещё не ушёл в сундучки.
        /// </summary>
        public static IReadOnlyList<Rank> AskableRanks(IReadOnlyList<Card> hand, IReadOnlyList<Rank> openRanks)
        {
            if (hand.Count == 0)
            {
                return new List<Rank>(openRanks);
            }

            var result = new List<Rank>();
            foreach (var card in hand)
            {
                if (!result.Contains(card.Rank))
                {
                    result.Add(card.Rank);
                }
            }

            result.Sort();
            return result;
        }

        /// <summary>
        /// Какие числа можно назвать. Четырёх у адресата не бывает — такое каре уже стало бы
        /// его сундучком, — а вместе со своими картами больше четырёх не наберётся. Один
        /// валет на руке — 1, 2, 3; два — 1, 2; три — только 1, и тогда число даже не
        /// спрашивают (см. <see cref="OnlyOneLeft"/>). Пустая рука — 1, 2, 3.
        /// </summary>
        public static IReadOnlyList<int> CountOptions(int ownCount)
        {
            var max = Math.Min(ChestSize - 1, ChestSize - ownCount);
            var result = new List<int>();
            for (var count = 1; count <= max; count++)
            {
                result.Add(count);
            }

            return result;
        }

        /// <summary>
        /// Масти называть не нужно, если свои карты вместе с угаданным числом дают все четыре:
        /// 2 + 2, 3 + 1, 1 + 3. Каких мастей не хватает, спрашивающий и так видит в своей руке.
        /// </summary>
        public static bool SuitsAreKnown(int ownCount, int guessedCount) => ownCount + guessedCount == ChestSize;

        /// <summary>
        /// Три карты ранга на руке — у адресата может быть только последняя, четвёртая, и какой
        /// она масти, тоже видно по своей руке. Спрашивать «один?» незачем: после «есть» карта
        /// уходит сразу.
        /// </summary>
        public static bool OnlyOneLeft(int ownCount) => ownCount == ChestSize - 1;

        /// <summary>
        /// Совпал ли набор целиком. Частичное совпадение не считается и не подсказывается:
        /// ответ один на весь набор, иначе масти перебирались бы по одной.
        /// </summary>
        public static bool SameSuits(IReadOnlyList<Suit> guess, IReadOnlyList<Card> cards)
        {
            if (guess.Count != cards.Count)
            {
                return false;
            }

            foreach (var card in cards)
            {
                if (!Contains(guess, card.Suit))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Нет ли одинаковых мастей в наборе: две пики у одного ранга не бывает.</summary>
        public static bool AreDistinct(IReadOnlyList<Suit> suits)
        {
            for (var i = 0; i < suits.Count; i++)
            {
                for (var j = i + 1; j < suits.Count; j++)
                {
                    if (suits[i] == suits[j])
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool Contains(IReadOnlyList<Suit> suits, Suit suit)
        {
            foreach (var candidate in suits)
            {
                if (candidate == suit)
                {
                    return true;
                }
            }

            return false;
        }

        // ---------- слова ----------

        /// <summary>«вальты», «восьмёрки» — для вопроса «есть вальты?» и подписи сундучка.</summary>
        public static string RankPluralNominative(Rank rank) => rank switch
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
            Rank.Jack => "вальты",
            Rank.Queen => "дамы",
            Rank.King => "короли",
            Rank.Ace => "тузы",
            _ => "джокеры"
        };

        /// <summary>
        /// «два вальта», «одна дама», «три восьмёрки» — число вместе с картой, как его
        /// спрашивают вслух. «2 вальта» звучало бы строкой из таблицы.
        /// </summary>
        public static string CountPhrase(Rank rank, int count)
        {
            if (IsMasculine(rank))
            {
                var numeral = count switch { 1 => "один", 2 => "два", 3 => "три", _ => "четыре" };
                return numeral + " " + (count == 1 ? SingularNominative(rank) : MasculineGenitive(rank));
            }

            var female = count switch { 1 => "одна", 2 => "две", 3 => "три", _ => "четыре" };
            return female + " " + (count == 1 ? SingularNominative(rank) : RankPluralNominative(rank));
        }

        /// <summary>
        /// «одного вальта», «двух королей», «две дамы» — винительный, для «отдаёт Егору двух
        /// вальтов». Фигуры за столом одушевлённые, так же говорят и в блефе.
        /// </summary>
        public static string CardsAccusative(Rank rank, int count)
        {
            if (IsMasculine(rank))
            {
                var numeral = count switch { 1 => "одного", 2 => "двух", 3 => "трёх", _ => "четырёх" };
                return numeral + " " + (count == 1 ? MasculineGenitive(rank) : BluffRules.RankPlural(rank));
            }

            var female = count switch { 1 => "одну", 2 => "две", 3 => "три", _ => "четыре" };
            return female + " " + (count == 1 ? FeminineAccusative(rank) : RankPluralNominative(rank));
        }

        /// <summary>«пики», «крести», «буби» — масть так, как её называют за столом, а не по учебнику.</summary>
        public static string SuitName(Suit suit) => suit switch
        {
            Suit.Clubs => "крести",
            Suit.Diamonds => "буби",
            Suit.Hearts => "черви",
            _ => "пики"
        };

        /// <summary>«пики», «пики и черви», «крести, буби и черви» — в порядке мастей, а не выбора.</summary>
        public static string SuitsPhrase(IReadOnlyList<Suit> suits)
        {
            var sorted = new List<Suit>(suits);
            sorted.Sort();

            var names = new List<string>();
            foreach (var suit in sorted)
            {
                names.Add(SuitName(suit));
            }

            if (names.Count <= 1)
            {
                return names.Count == 0 ? string.Empty : names[0];
            }

            return string.Join(", ", names.GetRange(0, names.Count - 1)) + " и " + names[names.Count - 1];
        }

        /// <summary>«1 сундучок», «3 сундучка», «5 сундучков», «11 сундучков».</summary>
        public static string ChestsWord(int count)
        {
            var tail = count % 100;
            if (tail >= 11 && tail <= 14)
            {
                return "сундучков";
            }

            return (count % 10) switch
            {
                1 => "сундучок",
                2 => "сундучка",
                3 => "сундучка",
                4 => "сундучка",
                _ => "сундучков"
            };
        }

        /// <summary>Первая буква заглавная — для реплики, которая начинает фразу: «Два вальта?»</summary>
        public static string Capitalize(string text) =>
            string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);

        /// <summary>Валет, король и туз — мужского рода, остальные карты женского. От этого и формы.</summary>
        private static bool IsMasculine(Rank rank) => rank == Rank.Jack || rank == Rank.King || rank == Rank.Ace;

        private static string SingularNominative(Rank rank) => rank switch
        {
            Rank.Two => "двойка",
            Rank.Three => "тройка",
            Rank.Four => "четвёрка",
            Rank.Five => "пятёрка",
            Rank.Six => "шестёрка",
            Rank.Seven => "семёрка",
            Rank.Eight => "восьмёрка",
            Rank.Nine => "девятка",
            Rank.Ten => "десятка",
            Rank.Jack => "валет",
            Rank.Queen => "дама",
            Rank.King => "король",
            Rank.Ace => "туз",
            _ => "джокер"
        };

        /// <summary>«вальта», «короля» — после «два» и в винительном у одного.</summary>
        private static string MasculineGenitive(Rank rank) => rank switch
        {
            Rank.Jack => "вальта",
            Rank.King => "короля",
            _ => "туза"
        };

        private static string FeminineAccusative(Rank rank) => rank switch
        {
            Rank.Two => "двойку",
            Rank.Three => "тройку",
            Rank.Four => "четвёрку",
            Rank.Five => "пятёрку",
            Rank.Six => "шестёрку",
            Rank.Seven => "семёрку",
            Rank.Eight => "восьмёрку",
            Rank.Nine => "девятку",
            Rank.Ten => "десятку",
            _ => "даму"
        };
    }
}
