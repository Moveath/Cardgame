using System;
using System.Collections.Generic;
using CardGame.Core.Cards;
using CardGame.Core.Games.Chests;

namespace CardGame.Bots
{
    /// <summary>
    /// Бот для «Сундучков». Смотрит в свою руку, в открытые сундучки, в лог вопросов и в то,
    /// у кого рука пуста, — это видно всем за столом. Ни чужих карт, ни их числа он не читает.
    ///
    /// Думает перебором. Карт одного ранга всего четыре, свои бот видит, а остальные
    /// раскладывает по соперникам всеми способами — их не больше 7⁴ — и выбрасывает те
    /// раскладки, что спорят с услышанным: «нет» на вопрос, неверное число, неверные масти,
    /// переданные у всех на глазах карты. Что осталось, то и возможно; спрашивает он там,
    /// где угадать вернее всего.
    ///
    /// Память у него не идеальная: свои вопросы и свежие ответы он помнит всегда, а чужие
    /// старые — не все. Иначе за столом сидел бы арифметик, которого не обмануть нарочно
    /// неверной догадкой.
    /// </summary>
    public sealed class SimpleChestsBot
    {
        /// <summary>Какую долю чужих старых ответов бот помнит.</summary>
        public const double DefaultRecall = 0.75;

        /// <summary>Сколько последних строк лога бот помнит целиком: их только что слышали все.</summary>
        private const int FreshMemory = 6;

        /// <summary>
        /// Насколько вопрос, который закрывает сундучок, ценнее обычного. Карты сами по себе
        /// очков не дают: очко — только готовое каре.
        /// </summary>
        private const double ChestBonus = 0.5;

        /// <summary>
        /// Разброс в оценке вопросов. Без него бот при равных шансах всегда спрашивает одного
        /// и того же соперника, и его ход читается наперёд.
        /// </summary>
        private const double Jitter = 0.2;

        private const int AllSuits = 0xF;

        private readonly Random _random;
        private readonly double _recall;

        /// <summary>Что именно бот забыл, решает эта соль: одна и та же строка лога либо помнится, либо нет.</summary>
        private readonly int _salt;

        public SimpleChestsBot() : this(new Random())
        {
        }

        public SimpleChestsBot(Random random) : this(random, DefaultRecall)
        {
        }

        public SimpleChestsBot(Random random, double recall)
        {
            _random = random;
            _recall = recall;
            _salt = random.Next();
        }

        /// <summary>Ход бота на той ступеньке вопроса, где он стоит. Null — если сейчас не его ход.</summary>
        public ChestsMove? ChooseMove(ChestsGame game, int playerIndex)
        {
            if (game.IsFinished || playerIndex != game.CurrentPlayerIndex)
            {
                return null;
            }

            var facts = ReadLog(game, playerIndex);

            switch (game.Phase)
            {
                case ChestsPhase.Asking:
                    return ChooseQuestion(game, playerIndex, facts);
                case ChestsPhase.GuessingCount:
                    return ChooseCount(game, playerIndex, facts);
                case ChestsPhase.GuessingSuits:
                    return ChooseSuits(game, playerIndex, facts);
                default:
                    return null;
            }
        }

        // ---------- решения ----------

        private ChestsMove? ChooseQuestion(ChestsGame game, int self, Dictionary<(int, Rank), Facts> facts)
        {
            var hand = game.Players[self];
            ChestsMove? best = null;
            var bestWorth = double.MinValue;

            foreach (var rank in game.AskableRanks(self))
            {
                var picture = Imagine(game, self, rank, facts);
                var own = hand.CountOf(rank);
                var options = ChestsRules.CountOptions(own);

                foreach (var target in game.AvailableTargets(self))
                {
                    var count = BestCount(picture, target, options);
                    var closes = ChestsRules.SuitsAreKnown(own, count);
                    var chance = closes ? picture.CountChance(target, count) : picture.BestSetChance(target, count);
                    var worth = chance * (closes ? 1.0 + ChestBonus : 1.0) * (1.0 + Jitter * (_random.NextDouble() * 2.0 - 1.0));

                    if (worth > bestWorth)
                    {
                        bestWorth = worth;
                        best = ChestsMove.Ask(target, rank);
                    }
                }
            }

            return best;
        }

        private ChestsMove ChooseCount(ChestsGame game, int self, Dictionary<(int, Rank), Facts> facts)
        {
            var picture = Imagine(game, self, game.AskedRank!.Value, facts);
            return ChestsMove.GuessCount(BestCount(picture, game.TargetIndex!.Value, game.CountOptions(self)));
        }

        private ChestsMove ChooseSuits(ChestsGame game, int self, Dictionary<(int, Rank), Facts> facts)
        {
            var rank = game.AskedRank!.Value;
            var picture = Imagine(game, self, rank, facts);
            var mask = picture.BestSet(game.TargetIndex!.Value, game.GuessedCount!.Value, _random);

            // Картина пуста только если лог противоречит сам себе. Тогда хотя бы не называем свои масти.
            if (mask == 0)
            {
                mask = RandomSet(AllSuits & ~OwnMask(game.Players[self], rank), game.GuessedCount.Value);
            }

            return ChestsMove.GuessSuits(SuitsOf(mask));
        }

        /// <summary>Самое вероятное число. При равенстве — любое из лучших, а не всегда меньшее.</summary>
        private int BestCount(Picture picture, int target, IReadOnlyList<int> options)
        {
            var best = options[0];
            var bestChance = double.MinValue;
            foreach (var count in options)
            {
                var chance = picture.CountChance(target, count) + _random.NextDouble() * 1e-9;
                if (chance > bestChance)
                {
                    bestChance = chance;
                    best = count;
                }
            }

            return best;
        }

        // ---------- память ----------

        /// <summary>
        /// Что бот знает о картах одного ранга у одного соперника. Всё это правда на момент,
        /// когда её услышали, и остаётся правдой, пока карты этого ранга у него не сменились —
        /// а сменяются они только передачей, которую видят все.
        /// </summary>
        private sealed class Facts
        {
            public bool None;
            public bool AtLeastOne;
            public int? Exact;
            public int Known;
            public readonly HashSet<int> NotCounts = new HashSet<int>();
            public readonly HashSet<int> NotSets = new HashSet<int>();

            /// <summary>
            /// Не меньше стольких — но не наверняка. Спросивший ранг обычно держит его сам,
            /// а вот игрок с пустой рукой спрашивает что угодно, и пустая ли рука, не видно.
            /// </summary>
            public int Likely;

            public bool Fits(int mask, bool withLikely)
            {
                var count = Bits(mask);
                if (None && mask != 0)
                {
                    return false;
                }

                if (AtLeastOne && mask == 0)
                {
                    return false;
                }

                if (Exact.HasValue && count != Exact.Value)
                {
                    return false;
                }

                if ((mask & Known) != Known)
                {
                    return false;
                }

                if (NotCounts.Contains(count) || NotSets.Contains(mask))
                {
                    return false;
                }

                return !withLikely || count >= Likely;
            }
        }

        private Dictionary<(int, Rank), Facts> ReadLog(ChestsGame game, int self)
        {
            var facts = new Dictionary<(int, Rank), Facts>();
            var log = game.Log;

            for (var i = 0; i < log.Count; i++)
            {
                var entry = log[i];
                if (!entry.Rank.HasValue || !entry.TargetIndex.HasValue)
                {
                    continue;
                }

                var rank = entry.Rank.Value;
                var asker = entry.PlayerIndex;
                var target = entry.TargetIndex.Value;

                // Передачу видят все и забыть её нельзя: карты переезжают у всех на глазах.
                if (entry.Kind == ChestsLogKind.Transfer)
                {
                    var moved = MaskOf(entry.Cards);
                    facts.TryGetValue((asker, rank), out var before);

                    var after = new Facts
                    {
                        AtLeastOne = true,
                        Known = moved | (before?.Known ?? 0),
                        Likely = Bits(moved) + (before != null && before.Likely > 0 ? 1 : 0)
                    };

                    if (before != null && before.None)
                    {
                        after.Exact = Bits(moved);
                    }
                    else if (before?.Exact != null)
                    {
                        after.Exact = before.Exact.Value + Bits(moved);
                    }

                    facts[(asker, rank)] = after;
                    facts[(target, rank)] = new Facts { None = true };
                    continue;
                }

                if (!Remembers(game, self, i))
                {
                    continue;
                }

                switch (entry.Kind)
                {
                    case ChestsLogKind.Ask:
                        var asking = Get(facts, asker, rank);
                        asking.Likely = Math.Max(asking.Likely, 1);

                        if (entry.Success == true)
                        {
                            Get(facts, target, rank).AtLeastOne = true;
                        }
                        else
                        {
                            facts[(target, rank)] = new Facts { None = true };
                        }

                        break;

                    case ChestsLogKind.CountGuess:
                        if (entry.Success == true)
                        {
                            Get(facts, target, rank).Exact = entry.Count;
                        }
                        else if (entry.Count.HasValue)
                        {
                            Get(facts, target, rank).NotCounts.Add(entry.Count.Value);
                        }

                        break;

                    case ChestsLogKind.SuitsGuess:
                        if (entry.Success == false)
                        {
                            Get(facts, target, rank).NotSets.Add(MaskOf(entry.Suits));
                        }

                        break;
                }
            }

            return facts;
        }

        /// <summary>
        /// Свои вопросы и вопросы к себе бот помнит всегда, свежие ответы — тоже. Чужие
        /// старые — через раз: что именно забыто, решено раз и навсегда для каждой строки,
        /// иначе бот от хода к ходу то помнил бы ответ, то нет.
        /// </summary>
        private bool Remembers(ChestsGame game, int self, int index)
        {
            var entry = game.Log[index];
            if (entry.PlayerIndex == self || entry.TargetIndex == self)
            {
                return true;
            }

            if (game.Log.Count - 1 - index < FreshMemory)
            {
                return true;
            }

            return Unit(_salt ^ (self * 7919), index) < _recall;
        }

        private static double Unit(int salt, int index)
        {
            unchecked
            {
                var hash = (uint)salt * 2654435761u ^ (uint)index * 2246822519u;
                hash ^= hash >> 15;
                hash *= 2246822519u;
                hash ^= hash >> 13;
                hash *= 3266489917u;
                hash ^= hash >> 16;
                return (hash & 0xFFFFFF) / (double)0x1000000;
            }
        }

        private static Facts Get(Dictionary<(int, Rank), Facts> facts, int player, Rank rank)
        {
            if (!facts.TryGetValue((player, rank), out var known))
            {
                known = new Facts();
                facts[(player, rank)] = known;
            }

            return known;
        }

        // ---------- картина ----------

        /// <summary>
        /// Все раскладки чужих карт одного ранга, которые не спорят с услышанным. Для каждого
        /// соперника — сколько раскладок дают ему каждый набор мастей.
        /// </summary>
        private sealed class Picture
        {
            public Picture(int players)
            {
                Weight = new int[players, AllSuits + 1];
            }

            public int[,] Weight { get; }
            public int Total { get; set; }

            public double CountChance(int seat, int count)
            {
                if (Total == 0)
                {
                    return 0.0;
                }

                var sum = 0;
                for (var mask = 1; mask <= AllSuits; mask++)
                {
                    if (Bits(mask) == count)
                    {
                        sum += Weight[seat, mask];
                    }
                }

                return sum / (double)Total;
            }

            public double BestSetChance(int seat, int count)
            {
                if (Total == 0)
                {
                    return 0.0;
                }

                var best = 0;
                for (var mask = 1; mask <= AllSuits; mask++)
                {
                    if (Bits(mask) == count && Weight[seat, mask] > best)
                    {
                        best = Weight[seat, mask];
                    }
                }

                return best / (double)Total;
            }

            /// <summary>Самый вероятный набор нужного размера; 0 — если возможных нет.</summary>
            public int BestSet(int seat, int count, Random random)
            {
                var best = 0;
                var bestWeight = 0;
                var ties = 0;
                for (var mask = 1; mask <= AllSuits; mask++)
                {
                    if (Bits(mask) != count || Weight[seat, mask] == 0)
                    {
                        continue;
                    }

                    if (Weight[seat, mask] > bestWeight)
                    {
                        best = mask;
                        bestWeight = Weight[seat, mask];
                        ties = 1;
                    }
                    else if (Weight[seat, mask] == bestWeight && random.Next(++ties) == 0)
                    {
                        best = mask;
                    }
                }

                return best;
            }
        }

        /// <summary>
        /// Сначала со всеми догадками, потом без «скорее всего», потом совсем без памяти.
        /// Догадка может оказаться неверной — спрашивал игрок с пустой рукой, — и тогда
        /// её нужно отпустить, а не остаться с пустой картиной.
        /// </summary>
        private static Picture Imagine(ChestsGame game, int self, Rank rank, Dictionary<(int, Rank), Facts> facts)
        {
            var picture = Imagine(game, self, rank, facts, withLikely: true);
            if (picture.Total == 0)
            {
                picture = Imagine(game, self, rank, facts, withLikely: false);
            }

            if (picture.Total == 0)
            {
                picture = Imagine(game, self, rank, new Dictionary<(int, Rank), Facts>(), withLikely: false);
            }

            return picture;
        }

        private static Picture Imagine(
            ChestsGame game, int self, Rank rank, Dictionary<(int, Rank), Facts> facts, bool withLikely)
        {
            var players = game.Players.Count;
            var picture = new Picture(players);

            var own = OwnMask(game.Players[self], rank);
            var hidden = new List<int>();
            for (var suit = 0; suit < 4; suit++)
            {
                if ((own & (1 << suit)) == 0)
                {
                    hidden.Add(suit);
                }
            }

            // Тот, у кого рука пуста, сидит за столом без карт — его в раскладку не берём.
            var others = new List<int>();
            for (var seat = 0; seat < players; seat++)
            {
                if (seat != self && !game.Players[seat].HasEmptyHand)
                {
                    others.Add(seat);
                }
            }

            var seatFacts = new Facts?[players];
            foreach (var seat in others)
            {
                facts.TryGetValue((seat, rank), out var known);
                seatFacts[seat] = known;
            }

            var layouts = 1;
            for (var i = 0; i < hidden.Count; i++)
            {
                layouts *= others.Count;
            }

            var masks = new int[players];
            for (var layout = 0; layout < layouts; layout++)
            {
                Array.Clear(masks, 0, players);

                var code = layout;
                foreach (var suit in hidden)
                {
                    masks[others[code % others.Count]] |= 1 << suit;
                    code /= others.Count;
                }

                var fits = true;
                foreach (var seat in others)
                {
                    var known = seatFacts[seat];
                    if (known != null && !known.Fits(masks[seat], withLikely))
                    {
                        fits = false;
                        break;
                    }
                }

                if (!fits)
                {
                    continue;
                }

                picture.Total++;
                foreach (var seat in others)
                {
                    picture.Weight[seat, masks[seat]]++;
                }
            }

            return picture;
        }

        // ---------- маски мастей ----------

        private static int OwnMask(ChestsPlayer player, Rank rank) => MaskOf(player.CardsOf(rank));

        private static int MaskOf(IReadOnlyList<Card> cards)
        {
            var mask = 0;
            foreach (var card in cards)
            {
                mask |= 1 << (int)card.Suit;
            }

            return mask;
        }

        private static int MaskOf(IReadOnlyList<Suit> suits)
        {
            var mask = 0;
            foreach (var suit in suits)
            {
                mask |= 1 << (int)suit;
            }

            return mask;
        }

        private static Suit[] SuitsOf(int mask)
        {
            var suits = new List<Suit>();
            for (var suit = Suit.Clubs; suit <= Suit.Spades; suit++)
            {
                if ((mask & (1 << (int)suit)) != 0)
                {
                    suits.Add(suit);
                }
            }

            return suits.ToArray();
        }

        /// <summary>Случайные масти из допустимых; если их не хватает — добираем любыми.</summary>
        private int RandomSet(int allowed, int count)
        {
            var mask = 0;
            for (var pass = 0; pass < 2 && Bits(mask) < count; pass++)
            {
                var pool = new List<int>();
                for (var suit = 0; suit < 4; suit++)
                {
                    var fits = pass == 0 ? ((allowed >> suit) & 1) == 1 : ((mask >> suit) & 1) == 0;
                    if (fits)
                    {
                        pool.Add(suit);
                    }
                }

                while (pool.Count > 0 && Bits(mask) < count)
                {
                    var pick = _random.Next(pool.Count);
                    mask |= 1 << pool[pick];
                    pool.RemoveAt(pick);
                }
            }

            return mask;
        }

        private static int Bits(int mask)
        {
            var count = 0;
            while (mask != 0)
            {
                count += mask & 1;
                mask >>= 1;
            }

            return count;
        }
    }
}
