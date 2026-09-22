using System;
using System.Collections.Generic;
using CardGame.Core.Cards;
using CardGame.Core.Games.Bluff;

namespace CardGame.Bots
{
    /// <summary>
    /// Бот для «верю — не верю». Смотрит только в свою руку и в публичные заявления —
    /// подглядывать в котёл и в чужие руки нельзя, иначе игра теряет смысл.
    ///
    /// Главный приём — счёт: в колоде каждого ранга ровно четыре. Если бот держит три
    /// шестёрки, а сосед заявил три шестёрки, врун пойман арифметикой, а не чутьём.
    /// Всё остальное — вероятности.
    /// </summary>
    public sealed class SimpleBluffBot
    {
        /// <summary>Сколько карт одного ранга в колоде. На этом строится весь подсчёт лжи.</summary>
        private const int CardsPerRank = 4;

        /// <summary>
        /// Даже доказанную ложь бот замечает не всегда. Идеальный арифметик за столом
        /// убивает игру: соврать нельзя вообще, а туз с руки не уходит никогда. Живой
        /// игрок тоже не пересчитывает чужие заявки каждый ход.
        /// </summary>
        private const double NoticeCertainLieChance = 0.8;

        /// <summary>Базовая подозрительность, когда арифметика ничего не доказывает.</summary>
        private const double BaseSuspicion = 0.08;

        /// <summary>Прибавка за каждую свою карту заявленного ранга — её у соперника точно нет.</summary>
        private const double SuspicionPerKnownCard = 0.13;

        /// <summary>Прибавка за размер броска: честных троек одного ранга мало.</summary>
        private const double SuspicionPerExtraCard = 0.07;

        /// <summary>Скидка за размер котла: ошибиться на большом котле дорого.</summary>
        private const double CalmPerPotCard = 0.015;

        /// <summary>Насколько осторожнее бот, когда рука почти пуста и котёл обидно брать.</summary>
        private const double CalmPerMissingCard = 0.05;

        /// <summary>Шанс избавиться от туза враньём, когда честный ход тоже возможен.</summary>
        private const double DumpAceChance = 0.45;

        private readonly Random _random;

        public SimpleBluffBot() : this(new Random())
        {
        }

        public SimpleBluffBot(Random random)
        {
            _random = random;
        }

        /// <summary>
        /// Ход бота. Возвращает null, если ходить нечем — такого в нормальном течении партии
        /// не бывает, но пусть вызывающий не падает.
        /// </summary>
        public BluffMove? ChooseMove(BluffGame game, int playerIndex)
        {
            if (game.IsFinished || playerIndex != game.CurrentPlayerIndex)
            {
                return null;
            }

            var hand = game.Players[playerIndex].Hand;
            if (hand.Count == 0)
            {
                return null;
            }

            if (game.Phase == BluffPhase.RoundStart)
            {
                return ChooseDeclaration(game, hand);
            }

            return ShouldChallenge(game, playerIndex) ? BluffMove.Challenge() : ChooseFollow(game, hand);
        }

        /// <summary>Каре бот сбрасывает всегда: рука легче, а риска никакого.</summary>
        public BluffMove? ChooseFourOfAKind(BluffGame game, int playerIndex)
        {
            var fours = game.AvailableFours(playerIndex);
            return fours.Count == 0 ? null : BluffMove.DiscardFour(fours[0]);
        }

        // ---------- начало круга ----------

        private BluffMove ChooseDeclaration(BluffGame game, IReadOnlyList<Card> hand)
        {
            // Ранг выбираем тот, которого больше всего на руках: так за один заход уходит
            // больше карт и врать не приходится вовсе.
            var best = Rank.Two;
            var bestCount = 0;

            foreach (var rank in RanksIn(hand))
            {
                if (!BluffRules.CanBeDeclared(rank))
                {
                    continue;
                }

                var count = CountOf(hand, rank);
                if (count > bestCount)
                {
                    bestCount = count;
                    best = rank;
                }
            }

            if (bestCount == 0)
            {
                // На руках одни тузы: честного хода нет, остаётся наглость.
                return LieUnder(game, hand, PickRankToLieAbout(hand));
            }

            var honest = Take(hand, best, Math.Min(bestCount, game.Options.MaxCardsPerThrow));

            // Если под честный ход есть место, иногда подкладываем туза сверху — момент
            // избавиться от него удобнее не станет.
            var ace = FindAce(hand);
            if (ace.HasValue && honest.Count < game.Options.MaxCardsPerThrow && _random.NextDouble() < DumpAceChance)
            {
                honest.Add(ace.Value);
            }

            return BluffMove.Declare(best, honest.Count, honest);
        }

        // ---------- поддержать цепочку ----------

        private BluffMove ChooseFollow(BluffGame game, IReadOnlyList<Card> hand)
        {
            var rank = game.CurrentRank!.Value;
            var matching = Take(hand, rank, game.Options.MaxCardsPerThrow);

            if (matching.Count > 0)
            {
                var ace = FindAce(hand);
                if (ace.HasValue && matching.Count < game.Options.MaxCardsPerThrow && _random.NextDouble() < DumpAceChance)
                {
                    matching.Add(ace.Value);
                }

                return BluffMove.Follow(matching.Count, matching);
            }

            return LieUnder(game, hand, rank);
        }

        /// <summary>
        /// Врать выгоднее мусором: сначала тузы, потом самые бесполезные карты — те, чей ранг
        /// на руках представлен хуже всего, потому что собрать из них каре уже не выйдет.
        /// </summary>
        private BluffMove LieUnder(BluffGame game, IReadOnlyList<Card> hand, Rank declaredRank)
        {
            var junk = new List<Card>(hand);
            junk.Sort((left, right) =>
            {
                var leftScore = JunkScore(hand, left, declaredRank);
                var rightScore = JunkScore(hand, right, declaredRank);
                return rightScore.CompareTo(leftScore);
            });

            var take = Math.Min(game.Options.MaxCardsPerThrow, junk.Count);
            take = 1 + _random.Next(take);

            var cards = junk.GetRange(0, take);

            return game.Phase == BluffPhase.RoundStart
                ? BluffMove.Declare(declaredRank, cards.Count, cards)
                : BluffMove.Follow(cards.Count, cards);
        }

        private static int JunkScore(IReadOnlyList<Card> hand, Card card, Rank declaredRank)
        {
            if (card.Rank == Rank.Ace)
            {
                return 100;
            }

            if (card.Rank == declaredRank)
            {
                // Такую карту врать не надо — она честная, ей место в правдивом броске.
                return -100;
            }

            // Чем меньше собратьев по рангу, тем меньше от карты пользы.
            return 10 - CountOf(hand, card.Rank);
        }

        private Rank PickRankToLieAbout(IReadOnlyList<Card> hand)
        {
            // Заявляем ранг, которого у нас нет вовсе: меньше шансов, что сосед держит
            // остальные четыре и поймает нас на арифметике.
            for (var rank = Rank.Two; rank <= Rank.King; rank++)
            {
                if (BluffRules.CanBeDeclared(rank) && CountOf(hand, rank) == 0)
                {
                    return rank;
                }
            }

            return Rank.King;
        }

        // ---------- проверять или нет ----------

        /// <summary>
        /// Сначала арифметика: если заявленных карт вместе с теми, что бот держит сам,
        /// набирается больше четырёх — соперник соврал наверняка. Иначе гадаем.
        /// </summary>
        public bool ShouldChallenge(BluffGame game, int playerIndex)
        {
            var packet = game.LastPacket;
            if (packet == null)
            {
                return false;
            }

            var hand = game.Players[playerIndex].Hand;
            var mine = CountOf(hand, packet.DeclaredRank);

            // Столько карт этого ранга в колоде просто нет — соврал наверняка. Но замечаем
            // не каждый раз, иначе блефовать против ботов было бы невозможно в принципе.
            if (mine + packet.DeclaredCount > CardsPerRank)
            {
                return _random.NextDouble() < NoticeCertainLieChance;
            }

            var suspicion = BaseSuspicion;

            // Каждая карта заявленного ранга на своей руке — это карта, которой у соперника
            // точно нет. Три заявленные шестёрки при двух своих уже почти ложь.
            suspicion += SuspicionPerKnownCard * mine;
            suspicion += SuspicionPerExtraCard * (packet.DeclaredCount - 1);

            // Когда у соперника вот-вот кончатся карты, проверка — последний шанс его вернуть.
            if (game.Players[packet.PlayerIndex].Hand.Count == 0)
            {
                suspicion += 0.5;
            }

            // Чем больше котёл, тем дороже ошибиться — и тем спокойнее бот смотрит на чужие
            // заявки. Из этого сама собой получается нарастающая по кругу тишина.
            suspicion -= CalmPerPotCard * game.DeclaredPotCount;

            // Чем меньше своих карт, тем обиднее забирать котёл.
            suspicion -= CalmPerMissingCard * Math.Max(0, 6 - hand.Count);

            return _random.NextDouble() < Math.Min(0.95, Math.Max(0.04, suspicion));
        }

        // ---------- мелочи ----------

        private static IEnumerable<Rank> RanksIn(IReadOnlyList<Card> hand)
        {
            var seen = new HashSet<Rank>();
            foreach (var card in hand)
            {
                if (seen.Add(card.Rank))
                {
                    yield return card.Rank;
                }
            }
        }

        private static int CountOf(IReadOnlyList<Card> hand, Rank rank)
        {
            var count = 0;
            foreach (var card in hand)
            {
                if (card.Rank == rank)
                {
                    count++;
                }
            }

            return count;
        }

        private static List<Card> Take(IReadOnlyList<Card> hand, Rank rank, int limit)
        {
            var result = new List<Card>();
            foreach (var card in hand)
            {
                if (card.Rank == rank && result.Count < limit)
                {
                    result.Add(card);
                }
            }

            return result;
        }

        private static Card? FindAce(IReadOnlyList<Card> hand)
        {
            foreach (var card in hand)
            {
                if (card.Rank == Rank.Ace)
                {
                    return card;
                }
            }

            return null;
        }
    }
}
