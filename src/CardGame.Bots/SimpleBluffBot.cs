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
        /// Доказанную ложь бот ловит не всегда, и дело не только в невнимательности.
        /// Проверка — это подарок котла тому, кого вскрыли, а дарить его игроку, который
        /// и так тонет в картах, незачем. Живые игроки ровно так и делают: пропускают
        /// очевидный обман, если сейчас выгоднее прижать кого-то другого.
        /// Нижняя граница — когда врёт отстающий, верхняя — когда врёт лидер.
        /// </summary>
        private const double NoticeCertainLieMin = 0.55;

        private const double NoticeCertainLieMax = 0.95;

        /// <summary>Насколько сильнее хочется вскрыть того, кто вот-вот выйдет из игры.</summary>
        private const double SuspicionPerPressure = 0.5;

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

        /// <summary>Готовность сдаться за каждый туз сверх первого, когда кроме тузов ничего нет.</summary>
        private const double ConcedePerSpareAce = 0.35;

        /// <summary>
        /// Шанс приберечь последнюю настоящую карту круга и соврать вместо неё. Смысл в
        /// том, что круг вернётся: тогда будет чем ответить честно, не рискуя котлом.
        /// Кто вывалил все свои шестёрки сразу, на втором заходе врёт вслепую.
        /// </summary>
        private const double KeepLastHonestChance = 0.4;

        /// <summary>То же самое, когда настоящих карт несколько: одну оставляем в запасе.</summary>
        private const double KeepOneHonestChance = 0.5;

        /// <summary>Котёл, при котором проверка уже кажется большим делом.</summary>
        private const double HugePot = 30.0;

        /// <summary>Базовое желание объявить перестрелку, когда её предлагают.</summary>
        private const double ShootoutOpenBase = 0.25;

        /// <summary>Добавка к этому желанию для того, кто тонет в картах: ему терять нечего.</summary>
        private const double ShootoutOpenPerSpareCard = 0.5;

        /// <summary>
        /// Осторожность в начале перестрелки, пока нет явных причин её прервать.
        /// </summary>
        private const double ShootoutShame = 0.65;

        /// <summary>Но жадность пересиливает стыд: слагаемое за толстый котёл и близкого к выходу вруна.</summary>
        private const double ShootoutNerve = 0.35;

        /// <summary>
        /// Какая часть этой жадности держится на одном размере котла, без оглядки на то,
        /// кто врёт. Нужна, чтобы стол нервничал и без явного лидера: чем выше гора, тем
        /// заманчивее вручить её кому угодно.
        /// </summary>
        private const double GreedFromPot = 0.4;

        /// <summary>
        /// Насколько упорно бот бережёт настоящую карту круга, пока перестрелка только
        /// разгоняется. В этом весь приём: валить мусор, а честную карту держать про
        /// запас — чтобы, когда наконец проверят, оказаться чистым и не взять гору.
        /// </summary>
        private const double ShootoutReserveChance = 0.85;

        /// <summary>
        /// Разброс того, какой котёл бот считает опасным, в долях <see cref="HugePot"/>.
        /// Порог берётся заново на каждое решение, и это важнее самой величины: с одним
        /// общим порогом получилось бы расписание — настоящая карта всегда всплывает на
        /// третьем заходе. Такое запоминается за пару партий, и приём перестаёт работать.
        /// </summary>
        private const double ShootoutDangerMin = 0.3;

        private const double ShootoutDangerMax = 1.6;

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

            // Сдаваться бот не спешит: пока туз на руках один, его ещё можно подсунуть под
            // чужой ранг и уйти чистым. А с тремя тузами проскочить трижды подряд почти
            // невозможно, и тянуть решённую партию — только мучить стол.
            if (game.CanConcede(playerIndex) && _random.NextDouble() < ConcedePerSpareAce * (hand.Count - 1))
            {
                return BluffMove.Concede();
            }

            if (game.Phase == BluffPhase.RoundStart)
            {
                return ChooseDeclaration(game, playerIndex, hand);
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

        private BluffMove ChooseDeclaration(BluffGame game, int playerIndex, IReadOnlyList<Card> hand)
        {
            var move = BuildDeclaration(game, hand);

            if (!game.ShootoutOffered || !WantsShootout(game, playerIndex))
            {
                return move;
            }

            return BluffMove.DeclareShootout(move.Rank, move.Cards);
        }

        /// <summary>
        /// Перестрелка — это огромный котёл и лотерея. Тому, кто вот-вот выйдет, она невыгодна:
        /// терять больше, чем выигрывать. Тому, кто тонет в картах, наоборот — терять нечего,
        /// зато под общий шум можно сбросить весь мусор разом.
        /// </summary>
        private bool WantsShootout(BluffGame game, int playerIndex)
        {
            var pressure = LeaderPressure(game, playerIndex);
            return _random.NextDouble() < ShootoutOpenBase + ShootoutOpenPerSpareCard * (1.0 - pressure);
        }

        private BluffMove BuildDeclaration(BluffGame game, IReadOnlyList<Card> hand)
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

            return BluffMove.Declare(best, honest);
        }

        // ---------- поддержать цепочку ----------

        private BluffMove ChooseFollow(BluffGame game, IReadOnlyList<Card> hand)
        {
            var rank = game.CurrentRank!.Value;
            var matching = Take(hand, rank, game.Options.MaxCardsPerThrow);

            if (matching.Count == 0)
            {
                // Своих карт этого ранга нет. Это не повод вскрывать соседа — можно просто
                // подложить мусор и надеяться, что дальше по кругу не проверят.
                return LieUnder(game, hand, rank);
            }

            // Настоящая карта круга — это право ответить честно, когда очередь вернётся.
            // Поэтому иногда бот врёт сейчас, а её придерживает: LieUnder честную карту
            // не тронет, у неё худший приоритет.
            if (matching.Count == 1)
            {
                if (hand.Count > 1 && _random.NextDouble() < KeepHonestChance(game, lastOne: true))
                {
                    return LieUnder(game, hand, rank);
                }
            }
            else if (_random.NextDouble() < KeepHonestChance(game, lastOne: false))
            {
                matching.RemoveAt(matching.Count - 1);
            }

            var ace = FindAce(hand);
            if (ace.HasValue && matching.Count < game.Options.MaxCardsPerThrow && _random.NextDouble() < DumpAceChance)
            {
                matching.Add(ace.Value);
            }

            return BluffMove.Follow(matching);
        }

        /// <summary>
        /// В перестрелке настоящая карта круга — это страховка от проверки, и держат её
        /// тем крепче, чем меньше котёл: вскрывать в самом начале никто не станет. Когда
        /// котёл вырос и проверка вот-вот случится, страховку пускают в ход.
        ///
        /// Но без расписания. Момент, с которого котёл кажется опасным, бот берёт заново
        /// каждый раз: один выложит настоящую карту сразу, другой протянет до последнего,
        /// у третьего её вовсе не окажется. Именно этого за столом и ждут — что понять,
        /// врут сейчас или нет, нельзя ни по счёту кругов, ни по размеру котла.
        /// </summary>
        private double KeepHonestChance(BluffGame game, bool lastOne)
        {
            var normal = lastOne ? KeepLastHonestChance : KeepOneHonestChance;
            if (!game.IsShootout)
            {
                return normal;
            }

            var danger = HugePot * (ShootoutDangerMin + _random.NextDouble() * (ShootoutDangerMax - ShootoutDangerMin));
            var filled = game.DeclaredPotCount / danger;
            var reserve = ShootoutReserveChance * (1.0 - (filled > 1.0 ? 1.0 : filled));

            return reserve > normal ? reserve : normal;
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
                ? BluffMove.Declare(declaredRank, cards)
                : BluffMove.Follow(cards);
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

            var pressure = LeaderPressure(game, packet.PlayerIndex);

            // Столько карт этого ранга в колоде просто нет — соврал наверняка. Ловим тем
            // охотнее, чем ближе врун к выходу: вскрыть лидера выгодно, а отстающему
            // лишняя пачка карт почти ничего не меняет — на него проверку можно и не тратить.
            // Клеймо не должно заставлять бота пропускать доказанную ложь.
            var restraint = ShootoutRestraint(game, pressure);

            // Кроме одного случая: соперник только что выложил последние карты. Промолчать
            // сейчас — значит отпустить его с первым местом, а вдвоём ещё и закончить партию,
            // потому что отвечать станет некому. Тут уже не до петуха.
            if (game.Players[packet.PlayerIndex].Hand.Count == 0)
            {
                restraint = 1.0;
            }

            if (mine + packet.DeclaredCount > CardsPerRank)
            {
                var notice = NoticeCertainLieMin + (NoticeCertainLieMax - NoticeCertainLieMin) * pressure;
                return _random.NextDouble() < notice;
            }

            var suspicion = BaseSuspicion;

            // Каждая карта заявленного ранга на своей руке — это карта, которой у соперника
            // точно нет. Три заявленные шестёрки при двух своих уже почти ложь.
            suspicion += SuspicionPerKnownCard * mine;
            suspicion += SuspicionPerExtraCard * (packet.DeclaredCount - 1);

            // Та же мысль, но плавно: чем меньше карт у бросавшего, тем нужнее его вскрыть.
            suspicion += SuspicionPerPressure * pressure;

            if (game.IsShootout)
            {
                // Большой котёл заманчиво вручить вруну. Проверка возможна с первого
                // пакета; нарастающий риск не превращает перестрелку в бесплатный сброс.
                suspicion += 0.12 + 0.3 * PotWeight(game);
                var remaining = game.Players[packet.PlayerIndex].Hand.Count;
                if (remaining <= game.Options.MaxCardsPerThrow)
                {
                    suspicion += 0.22;
                    restraint = 1.0;
                }
            }
            else
            {
                suspicion -= CalmPerPotCard * game.DeclaredPotCount;
            }

            // Чем меньше своих карт, тем обиднее забирать котёл.
            suspicion -= CalmPerMissingCard * Math.Max(0, 6 - hand.Count);

            return _random.NextDouble() < Math.Min(0.95, Math.Max(0.04, suspicion) * restraint);
        }

        /// <summary>
        /// Во сколько раз слабее желание проверить, пока идёт перестрелка. Единица — вне
        /// перестрелки. Внутри неё осторожность ослабевает по мере роста котла.
        /// </summary>
        private static double ShootoutRestraint(BluffGame game, double pressure)
        {
            if (!game.IsShootout)
            {
                return 1.0;
            }

            var greed = PotWeight(game) * (GreedFromPot + (1.0 - GreedFromPot) * pressure);
            return ShootoutShame + ShootoutNerve * greed;
        }

        private static double PotWeight(BluffGame game)
        {
            var weight = game.DeclaredPotCount / HugePot;
            return weight > 1.0 ? 1.0 : weight;
        }

        /// <summary>
        /// Насколько игрок опережает стол: 1 — у него карт почти не осталось, 0 — их у него
        /// больше среднего. По этой величине бот и решает, кого стоит прижать, а кого можно
        /// спокойно пропустить, даже поймав на очевидном вранье.
        /// </summary>
        private static double LeaderPressure(BluffGame game, int playerIndex)
        {
            var total = 0;
            var holders = 0;

            foreach (var player in game.Players)
            {
                if (player.IsOut)
                {
                    continue;
                }

                total += player.Hand.Count;
                holders++;
            }

            if (holders == 0 || total == 0)
            {
                return 1.0;
            }

            var average = total / (double)holders;
            var mine = game.Players[playerIndex].Hand.Count;
            var pressure = (average - mine) / average;

            return pressure < 0.0 ? 0.0 : (pressure > 1.0 ? 1.0 : pressure);
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
