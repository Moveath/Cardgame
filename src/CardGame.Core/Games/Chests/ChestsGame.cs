using System;
using System.Collections.Generic;
using CardGame.Core.Cards;

namespace CardGame.Core.Games.Chests
{
    /// <summary>
    /// «Сундучки». Вся колода раздаётся, общего добора нет. В свой ход игрок спрашивает
    /// любого соперника про ранг из своей руки, потом угадывает, сколько у того карт этого
    /// ранга, потом — какие у них масти. Угадал всё — забирает карты и спрашивает снова;
    /// ошибся на любой ступеньке — ход переходит следующему по кругу. Каре само уходит в
    /// сундучок, и партия идёт, пока в руках не останется ни одной карты.
    ///
    /// Отвечает за адресата сама игра: соврать о своей руке нельзя. Обман здесь другой —
    /// нарочно неверная догадка, например названная своя масть, чтобы запутать тех, кто
    /// запоминает вопросы.
    ///
    /// Скрытая информация живёт прямо в модели. Прятать её — и карты, и их число — обязан
    /// тот, кто модель показывает: клиент рисует одинаковую стопку у каждого соперника с
    /// картами и никакой — у того, у кого рука пуста; бот смотрит только в свою руку, в лог
    /// и в то, чья рука пуста.
    /// </summary>
    public sealed class ChestsGame
    {
        /// <summary>Меньше трёх не бывает: вдвоём чужая рука видна как на ладони.</summary>
        public const int MinPlayers = 3;

        private readonly List<ChestsPlayer> _players = new List<ChestsPlayer>();
        private readonly List<Rank> _ranksInPlay = new List<Rank>();
        private readonly List<Rank> _collected = new List<Rank>();
        private readonly List<ChestsLogEntry> _log = new List<ChestsLogEntry>();
        private readonly List<int> _standings = new List<int>();
        private readonly List<int> _winners = new List<int>();
        private readonly List<int> _losers = new List<int>();

        private ChestsGame(IReadOnlyList<string> playerNames, Deck deck)
        {
            if (playerNames == null)
            {
                throw new ArgumentNullException(nameof(playerNames));
            }

            // Вдвоём не играют: всё, чего нет у тебя, лежит у единственного соперника, и
            // первый ходящий забирает всё подряд, не ошибившись ни разу.
            if (playerNames.Count < MinPlayers)
            {
                throw new ArgumentException("Сундучки начинаются с трёх игроков.", nameof(playerNames));
            }

            var cards = new List<Card>();
            while (!deck.IsEmpty)
            {
                cards.Add(deck.Draw());
            }

            CollectRanks(cards);

            for (var i = 0; i < playerNames.Count; i++)
            {
                _players.Add(new ChestsPlayer(i, playerNames[i]));
            }

            // Раздаём по кругу, как в блефе: при неделимом числе карт первым местам
            // достаётся на одну больше.
            for (var i = 0; i < cards.Count; i++)
            {
                _players[i % _players.Count].Deal(cards[i]);
            }

            Phase = ChestsPhase.Asking;
            CurrentPlayerIndex = 0;

            // Каре, пришедшее на раздаче, — такой же сундучок, как собранный вопросами.
            foreach (var player in _players)
            {
                foreach (var rank in _ranksInPlay)
                {
                    CollectChest(player, rank, atDeal: true);
                }
            }

            FinishIfEmpty();
        }

        public static ChestsGame Create(IReadOnlyList<string> playerNames, int seed) =>
            Create(playerNames, Deck.Standard36(seed));

        /// <summary>
        /// Колода подходит любая, где у каждого ранга ровно четыре масти: 24, 36, 52. Джокеров
        /// нет — сундучок из них не собрать, и отдельного правила для них пока не придумано.
        /// </summary>
        public static ChestsGame Create(IReadOnlyList<string> playerNames, Deck deck) =>
            new ChestsGame(playerNames, deck);

        public IReadOnlyList<ChestsPlayer> Players => _players;

        public ChestsPhase Phase { get; private set; }
        public bool IsFinished => Phase == ChestsPhase.Finished;

        /// <summary>
        /// Кто спрашивает. После ошибки ход идёт следующему по кругу после спрашивающего, а
        /// не тому, кого спрашивали. Пустая рука очередь не пропускает.
        /// </summary>
        public int CurrentPlayerIndex { get; private set; }

        /// <summary>Кого спрашивают сейчас. Пока ранг не назван — null.</summary>
        public int? TargetIndex { get; private set; }

        /// <summary>Какой ранг спрашивают сейчас.</summary>
        public Rank? AskedRank { get; private set; }

        /// <summary>Угаданное число: столько мастей и надо назвать.</summary>
        public int? GuessedCount { get; private set; }

        /// <summary>Какие ранги есть в колоде этой партии, по возрастанию.</summary>
        public IReadOnlyList<Rank> RanksInPlay => _ranksInPlay;

        /// <summary>Ранги, уже ушедшие в сундучки, в порядке сбора.</summary>
        public IReadOnlyList<Rank> CollectedRanks => _collected;

        /// <summary>Ранги, которые ещё ходят по рукам.</summary>
        public IReadOnlyList<Rank> OpenRanks
        {
            get
            {
                var open = new List<Rank>();
                foreach (var rank in _ranksInPlay)
                {
                    if (!_collected.Contains(rank))
                    {
                        open.Add(rank);
                    }
                }

                return open;
            }
        }

        /// <summary>Сколько сундучков можно собрать за партию — по одному на ранг.</summary>
        public int ChestsTotal => _ranksInPlay.Count;

        public IReadOnlyList<ChestsLogEntry> Log => _log;

        /// <summary>Места от первого к последнему: у кого больше сундучков, тот выше.</summary>
        public IReadOnlyList<int> Standings => _standings;

        /// <summary>
        /// У кого больше всех сундучков. Список: при равенстве победителей несколько, и
        /// выдумывать одного из воздуха мы не будем. Пуст при ничьей.
        /// </summary>
        public IReadOnlyList<int> Winners => _winners;

        /// <summary>У кого меньше всех. Тоже список и тоже пуст при ничьей.</summary>
        public IReadOnlyList<int> Losers => _losers;

        /// <summary>Сундучков у всех поровну — победителя нет.</summary>
        public bool IsDraw => IsFinished && _winners.Count == 0;

        /// <summary>Кого можно спросить: любого соперника, без оглядки на соседство и пустые руки.</summary>
        public IReadOnlyList<int> AvailableTargets(int playerIndex)
        {
            var result = new List<int>();
            if (!CanAct(playerIndex, ChestsPhase.Asking))
            {
                return result;
            }

            foreach (var player in _players)
            {
                if (player.Index != playerIndex)
                {
                    result.Add(player.Index);
                }
            }

            return result;
        }

        /// <summary>Какие ранги игрок может спросить сейчас.</summary>
        public IReadOnlyList<Rank> AskableRanks(int playerIndex)
        {
            if (!CanAct(playerIndex, ChestsPhase.Asking))
            {
                return new Rank[0];
            }

            return ChestsRules.AskableRanks(_players[playerIndex].Hand, OpenRanks);
        }

        /// <summary>Какие числа можно назвать сейчас. Невозможные не предлагаются вовсе.</summary>
        public IReadOnlyList<int> CountOptions(int playerIndex)
        {
            if (!CanAct(playerIndex, ChestsPhase.GuessingCount))
            {
                return new int[0];
            }

            return ChestsRules.CountOptions(_players[playerIndex].CountOf(AskedRank!.Value));
        }

        public bool TryApplyMove(int playerIndex, ChestsMove move, out string? error)
        {
            error = null;

            if (move == null)
            {
                error = "Хода нет.";
                return false;
            }

            if (IsFinished)
            {
                error = "Партия окончена.";
                return false;
            }

            if (!IsSeated(playerIndex))
            {
                error = "Нет такого игрока.";
                return false;
            }

            if (playerIndex != CurrentPlayerIndex)
            {
                error = "Сейчас не твой ход.";
                return false;
            }

            switch (move.Type)
            {
                case ChestsMoveType.Ask:
                    return TryAsk(playerIndex, move.TargetIndex, move.Rank, out error);
                case ChestsMoveType.GuessCount:
                    return TryGuessCount(playerIndex, move.Count, out error);
                case ChestsMoveType.GuessSuits:
                    return TryGuessSuits(playerIndex, move.Suits, out error);
                default:
                    error = "Неизвестный ход.";
                    return false;
            }
        }

        // ---------- ступеньки вопроса ----------

        private bool TryAsk(int askerIndex, int targetIndex, Rank rank, out string? error)
        {
            if (Phase != ChestsPhase.Asking)
            {
                error = "Вопрос уже задан — доведи его до конца.";
                return false;
            }

            if (!IsSeated(targetIndex) || targetIndex == askerIndex)
            {
                error = "Спросить можно только соперника.";
                return false;
            }

            var asker = _players[askerIndex];
            if (!Contains(AskableRanks(askerIndex), rank))
            {
                error = _collected.Contains(rank)
                    ? "Этот ранг уже лежит в сундучке."
                    : asker.HasEmptyHand
                        ? "Такого ранга в этой колоде нет."
                        : "Спрашивать можно только ранг, который есть у тебя на руках.";
                return false;
            }

            var target = _players[targetIndex];
            var has = target.CountOf(rank) > 0;
            _log.Add(ChestsLogEntry.Ask(asker, target, rank, has));

            if (!has)
            {
                PassTurn();
                error = null;
                return true;
            }

            TargetIndex = targetIndex;
            AskedRank = rank;

            // Три своих и «есть» у адресата — значит, у него последняя. Ни число, ни масти
            // спрашивать нечего: карта уходит сразу и собирает сундучок.
            if (ChestsRules.OnlyOneLeft(asker.CountOf(rank)))
            {
                GuessedCount = 1;
                Transfer();
                error = null;
                return true;
            }

            Phase = ChestsPhase.GuessingCount;
            error = null;
            return true;
        }

        private bool TryGuessCount(int askerIndex, int count, out string? error)
        {
            if (Phase != ChestsPhase.GuessingCount)
            {
                error = "Сейчас число не спрашивают.";
                return false;
            }

            if (!Contains(CountOptions(askerIndex), count))
            {
                error = "Столько карт у соперника быть не может.";
                return false;
            }

            var asker = _players[askerIndex];
            var target = _players[TargetIndex!.Value];
            var rank = AskedRank!.Value;
            var right = target.CountOf(rank) == count;
            _log.Add(ChestsLogEntry.CountGuess(asker, target, rank, count, right));

            if (!right)
            {
                PassTurn();
                error = null;
                return true;
            }

            GuessedCount = count;
            if (ChestsRules.SuitsAreKnown(asker.CountOf(rank), count))
            {
                Transfer();
            }
            else
            {
                Phase = ChestsPhase.GuessingSuits;
            }

            error = null;
            return true;
        }

        private bool TryGuessSuits(int askerIndex, IReadOnlyList<Suit> suits, out string? error)
        {
            if (Phase != ChestsPhase.GuessingSuits)
            {
                error = "Сейчас масти не спрашивают.";
                return false;
            }

            if (suits.Count != GuessedCount!.Value)
            {
                error = "Назвать нужно ровно столько мастей, сколько карт: " + GuessedCount.Value + ".";
                return false;
            }

            if (!ChestsRules.AreDistinct(suits))
            {
                error = "Масти в наборе не должны повторяться.";
                return false;
            }

            foreach (var suit in suits)
            {
                if (suit < Suit.Clubs || suit > Suit.Spades)
                {
                    error = "Нет такой масти.";
                    return false;
                }
            }

            // Свою масть назвать можно: это законный способ запутать стол, просто ценой
            // хода. Правила такой набор не отсекают — он честно не совпадёт.
            var asker = _players[askerIndex];
            var target = _players[TargetIndex!.Value];
            var rank = AskedRank!.Value;
            var right = ChestsRules.SameSuits(suits, target.CardsOf(rank));
            _log.Add(ChestsLogEntry.SuitsGuess(asker, target, rank, suits, right));

            if (!right)
            {
                PassTurn();
                error = null;
                return true;
            }

            Transfer();
            error = null;
            return true;
        }

        /// <summary>
        /// Угадано всё: карты ранга уходят к спрашивающему, каре тут же становится сундучком,
        /// а ход остаётся у него — спрашивает, пока угадывает.
        /// </summary>
        private void Transfer()
        {
            var asker = _players[CurrentPlayerIndex];
            var target = _players[TargetIndex!.Value];
            var rank = AskedRank!.Value;

            var cards = target.CardsOf(rank);
            foreach (var card in cards)
            {
                target.Remove(card);
            }

            asker.AddRange(cards);
            _log.Add(ChestsLogEntry.Transfer(asker, target, rank, cards));

            ClearQuestion();
            Phase = ChestsPhase.Asking;

            CollectChest(asker, rank, atDeal: false);
            FinishIfEmpty();
        }

        private void PassTurn()
        {
            ClearQuestion();
            Phase = ChestsPhase.Asking;
            CurrentPlayerIndex = (CurrentPlayerIndex + 1) % _players.Count;
        }

        private void ClearQuestion()
        {
            TargetIndex = null;
            AskedRank = null;
            GuessedCount = null;
        }

        private void CollectChest(ChestsPlayer player, Rank rank, bool atDeal)
        {
            if (player.CountOf(rank) < ChestsRules.ChestSize)
            {
                return;
            }

            var cards = player.CardsOf(rank);
            foreach (var card in cards)
            {
                player.Remove(card);
            }

            player.AddChest(rank);
            _collected.Add(rank);
            _log.Add(ChestsLogEntry.Chest(player, rank, cards, atDeal));
        }

        /// <summary>
        /// Какие ранги в колоде. Сундучок — это все четыре масти ранга, поэтому неполный ранг
        /// партию бы подвесил: четвёртой карты нет ни у кого, и собрать его нельзя.
        /// </summary>
        private void CollectRanks(IReadOnlyList<Card> cards)
        {
            var counts = new Dictionary<Rank, int>();
            foreach (var card in cards)
            {
                if (card.IsJoker)
                {
                    throw new ArgumentException("В сундучках джокеров нет: сундучок из них не собрать.", nameof(cards));
                }

                counts.TryGetValue(card.Rank, out var seen);
                counts[card.Rank] = seen + 1;
            }

            foreach (var pair in counts)
            {
                if (pair.Value != ChestsRules.ChestSize)
                {
                    throw new ArgumentException("Для сундучков нужны полные ранги: по четыре масти каждого.", nameof(cards));
                }

                _ranksInPlay.Add(pair.Key);
            }

            _ranksInPlay.Sort();
        }

        private bool CanAct(int playerIndex, ChestsPhase phase) =>
            !IsFinished && IsSeated(playerIndex) && playerIndex == CurrentPlayerIndex && Phase == phase;

        private bool IsSeated(int playerIndex) => playerIndex >= 0 && playerIndex < _players.Count;

        private static bool Contains<T>(IReadOnlyList<T> items, T value)
        {
            foreach (var item in items)
            {
                if (EqualityComparer<T>.Default.Equals(item, value))
                {
                    return true;
                }
            }

            return false;
        }

        // ---------- финал ----------

        /// <summary>Партия кончается, когда в руках не осталось ни одной карты: все каре собраны.</summary>
        private void FinishIfEmpty()
        {
            foreach (var player in _players)
            {
                if (!player.HasEmptyHand)
                {
                    return;
                }
            }

            Finish();
        }

        private void Finish()
        {
            Phase = ChestsPhase.Finished;
            ClearQuestion();

            var order = new List<ChestsPlayer>(_players);
            order.Sort((left, right) =>
            {
                var byChests = right.ChestCount.CompareTo(left.ChestCount);
                return byChests != 0 ? byChests : left.Index.CompareTo(right.Index);
            });

            _standings.Clear();
            foreach (var player in order)
            {
                _standings.Add(player.Index);
            }

            var most = order[0].ChestCount;
            var least = order[order.Count - 1].ChestCount;

            _winners.Clear();
            _losers.Clear();
            if (most != least)
            {
                foreach (var player in _players)
                {
                    if (player.ChestCount == most)
                    {
                        _winners.Add(player.Index);
                    }
                    else if (player.ChestCount == least)
                    {
                        _losers.Add(player.Index);
                    }
                }
            }

            _log.Add(ChestsLogEntry.GameOver(ResultText(most, least)));
        }

        private string ResultText(int most, int least)
        {
            if (most == least)
            {
                return "Все сундучки собраны. Ничья: у всех по " + most + " " + ChestsRules.ChestsWord(most) + ".";
            }

            return "Все сундучки собраны. " + Group(_winners, "Победитель", "Победители", most)
                   + " " + Group(_losers, "Меньше всех", "Меньше всех", least);
        }

        private string Group(IReadOnlyList<int> indexes, string one, string many, int chests)
        {
            var names = new List<string>();
            foreach (var index in indexes)
            {
                names.Add(_players[index].Name);
            }

            return (names.Count == 1 ? one : many) + " — " + string.Join(", ", names) + ": "
                   + (names.Count == 1 ? string.Empty : "по ") + chests + " " + ChestsRules.ChestsWord(chests) + ".";
        }
    }
}
