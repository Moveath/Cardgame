using System;
using System.Collections.Generic;
using CardGame.Core.Cards;

namespace CardGame.Core.Games.Bluff
{
    /// <summary>
    /// «Верю — не верю». Вся партия — это чередование двух состояний: начало круга, где
    /// ведущий сам называет ранг, и цепочка, где сосед по часовой либо проверяет последний
    /// пакет, либо подкладывает свой под тем же рангом. Круг обрывается только проверкой.
    ///
    /// Скрытая информация живёт прямо в модели (руки, котёл, содержимое пакетов). Прятать
    /// её обязан тот, кто модель показывает: клиент рисует рубашки, бот смотрит только в
    /// свою руку и в лог. Так же устроен и дурак с его шулерством.
    /// </summary>
    public sealed class BluffGame
    {
        /// <summary>
        /// Как часто ведущему круга предлагают объявить перестрелку. Вместе с паузой ниже
        /// даёт предложение примерно раз в два круга.
        /// </summary>
        private const double ShootoutChance = 0.8;

        /// <summary>
        /// Сколько кругов после состоявшейся перестрелки её точно не предложат. Пауза нужна
        /// затем, чтобы две перестрелки не шли подряд: круг в перестрелке и так вдвое
        /// длиннее обычного.
        /// </summary>
        private const int ShootoutCooldownRounds = 1;

        /// <summary>
        /// Сколько кругов носят петуха после сорванной перестрелки. Считаются круги, которые
        /// начались уже после неё: тот, где перестрелку сорвали, этой же проверкой и кончился.
        /// </summary>
        public const int RoosterRounds = 2;

        private readonly List<BluffPlayer> _players = new List<BluffPlayer>();
        private readonly BluffOptions _options;

        /// <summary>
        /// Кубик, решающий, предложить ли перестрелку. Засеян самой раздачей: одна и та же
        /// колода даёт одну и ту же партию целиком, так что любую жалобу можно повторить.
        /// </summary>
        private readonly Random _fate;

        private bool _shootoutOffered;
        private int _roundsSinceShootout = ShootoutCooldownRounds + 1;

        /// <summary>Кто сдался. Сдавшийся и есть проигравший, тузы уже ничего не решают.</summary>
        private int? _conceded;

        private readonly List<Card> _pot = new List<Card>();
        private readonly List<BluffPacket> _packets = new List<BluffPacket>();

        /// <summary>Каре уходит сюда и в партию больше не возвращается.</summary>
        private readonly List<Card> _removed = new List<Card>();

        private readonly List<BluffLogEntry> _log = new List<BluffLogEntry>();
        private readonly List<int> _finishOrder = new List<int>();
        private readonly List<int> _standings = new List<int>();
        private readonly List<int> _losers = new List<int>();

        private BluffGame(IReadOnlyList<string> playerNames, Deck deck, BluffOptions options)
        {
            if (playerNames == null)
            {
                throw new ArgumentNullException(nameof(playerNames));
            }

            if (playerNames.Count < 2)
            {
                throw new ArgumentException("Блеф начинается с двух игроков.", nameof(playerNames));
            }

            _options = options;

            for (var i = 0; i < playerNames.Count; i++)
            {
                _players.Add(new BluffPlayer(i, playerNames[i]));
            }

            // Раздаём по кругу, пока колода не кончится: при неделимом числе карт кому-то
            // достанется на одну больше, и это нормально.
            var seat = 0;
            while (!deck.IsEmpty)
            {
                _players[seat % _players.Count].Deal(deck.Draw());
                seat++;
            }

            _fate = new Random(FateSeed());

            StartRound(0);
        }

        public static BluffGame Create(IReadOnlyList<string> playerNames, int seed) =>
            Create(playerNames, Deck.Standard36(seed), BluffOptions.Classic);

        public static BluffGame Create(IReadOnlyList<string> playerNames, Deck deck) =>
            Create(playerNames, deck, BluffOptions.Classic);

        public static BluffGame Create(IReadOnlyList<string> playerNames, Deck deck, BluffOptions options) =>
            new BluffGame(playerNames, deck, options);

        public IReadOnlyList<BluffPlayer> Players => _players;
        public BluffOptions Options => _options;

        public BluffPhase Phase { get; private set; }
        public bool IsFinished => Phase == BluffPhase.Finished;

        /// <summary>Чей сейчас ход: в начале круга — ведущего, в цепочке — того, кто отвечает.</summary>
        public int CurrentPlayerIndex { get; private set; }

        /// <summary>Кто начал текущий круг.</summary>
        public int RoundLeaderIndex { get; private set; }

        /// <summary>Ранг круга. Пока круг не начался — null. Менять его по ходу цепочки нельзя.</summary>
        public Rank? CurrentRank { get; private set; }

        /// <summary>
        /// Круг идёт как перестрелка. Правила те же самые — разница в цене проверки:
        /// кто оборвал перестрелку, тот петух до конца партии.
        /// </summary>
        public bool IsShootout { get; private set; }

        /// <summary>Кто объявил текущую перестрелку.</summary>
        public int? ShootoutStarterIndex { get; private set; }

        /// <summary>
        /// Ведущему прямо сейчас предлагают объявить перестрелку. Предложение живёт ровно
        /// до его броска: не воспользовался — круг пошёл обычным, и просить второй раз
        /// в этом круге уже нечего.
        /// </summary>
        public bool ShootoutOffered => _shootoutOffered && Phase == BluffPhase.RoundStart;

        /// <summary>Настоящее содержимое котла. Скрытая информация.</summary>
        public IReadOnlyList<Card> Pot => _pot;

        /// <summary>Сколько карт в котле на самом деле. Скрытая информация: пакеты кладут рубашкой вверх.</summary>
        public int PotCount => _pot.Count;

        /// <summary>
        /// Сколько карт в котле по заявлениям. Совпадает с настоящим числом, потому что
        /// количеством не врут, — но считается по пакетам, а не по содержимому.
        /// </summary>
        public int DeclaredPotCount
        {
            get
            {
                var total = 0;
                foreach (var packet in _packets)
                {
                    total += packet.DeclaredCount;
                }

                return total;
            }
        }

        /// <summary>Пакеты текущего круга снизу вверх. Скрытая информация.</summary>
        public IReadOnlyList<BluffPacket> Packets => _packets;

        /// <summary>Последний брошенный пакет — именно его вскрывает проверка.</summary>
        public BluffPacket? LastPacket => _packets.Count == 0 ? null : _packets[_packets.Count - 1];

        public int? LastThrowerIndex => LastPacket?.PlayerIndex;

        /// <summary>Сколько карт ушло из партии через каре.</summary>
        public int RemovedCount => _removed.Count;

        public IReadOnlyList<BluffLogEntry> Log => _log;

        /// <summary>Кто и в каком порядке избавился от карт по ходу партии.</summary>
        public IReadOnlyList<int> FinishOrder => _finishOrder;

        /// <summary>Итоговые места, от первого к последнему. Заполняется в конце партии.</summary>
        public IReadOnlyList<int> Standings => _standings;

        /// <summary>
        /// Проигравшие: у кого из оставшихся с картами больше всего тузов. Список, потому что
        /// тузы могут разделиться поровну, и выдумывать победителя из воздуха мы не будем.
        /// </summary>
        public IReadOnlyList<int> Losers => _losers;

        public int? LoserIndex => _losers.Count == 1 ? _losers[0] : (int?)null;

        /// <summary>
        /// Тузы разделились поровну, и добить партию нечем — это ничья, а не проигрыш
        /// на всех: кидать больше некому и нечем, так что победителя тут просто нет.
        /// </summary>
        public bool IsDraw => _losers.Count > 1;

        /// <summary>Кто сдался сам, если партия кончилась так.</summary>
        public int? ConcededIndex => _conceded;

        /// <summary>
        /// Может ли игрок сдаться. Кнопка появляется только тогда, когда партия для него
        /// уже решена: честно ходить нечем — на руках одни тузы, — и тузов у него не
        /// меньше, чем у любого другого за столом. Тянуть такое до конца незачем, а
        /// раньше времени кнопка сдачи только путала бы.
        /// </summary>
        public bool CanConcede(int playerIndex)
        {
            if (IsFinished || !IsSeated(playerIndex) || HoldersCount < 2)
            {
                return false;
            }

            var player = _players[playerIndex];
            if (player.IsOut || !player.HoldsOnlyAces)
            {
                return false;
            }

            foreach (var other in _players)
            {
                if (other.IsOut || other.Index == playerIndex)
                {
                    continue;
                }

                if (other.AceCount > player.AceCount)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Сколько игроков ещё держат карты.</summary>
        public int HoldersCount
        {
            get
            {
                var count = 0;
                foreach (var player in _players)
                {
                    if (!player.IsOut)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>Ранги, каре из которых игрок может выложить прямо сейчас.</summary>
        public IReadOnlyList<Rank> AvailableFours(int playerIndex)
        {
            if (!_options.AllowFourOfAKind || IsFinished || !IsSeated(playerIndex))
            {
                return new Rank[0];
            }

            return BluffRules.FourOfAKind(_players[playerIndex].Hand, _options.AcesCanFormFour);
        }

        /// <summary>
        /// Что игрок может сделать сейчас. Полный перебор ходов тут не имеет смысла: выбор
        /// карт — это сотни комбинаций, и собирать их должен тот, кто ходит, а не правила.
        /// </summary>
        public IReadOnlyList<BluffMoveType> AvailableMoveTypes(int playerIndex)
        {
            var result = new List<BluffMoveType>();
            if (IsFinished || !IsSeated(playerIndex) || _players[playerIndex].IsOut)
            {
                return result;
            }

            if (playerIndex == CurrentPlayerIndex)
            {
                if (Phase == BluffPhase.RoundStart)
                {
                    result.Add(BluffMoveType.Declare);
                }
                else
                {
                    result.Add(BluffMoveType.Follow);
                    result.Add(BluffMoveType.Challenge);
                }
            }

            if (AvailableFours(playerIndex).Count > 0)
            {
                result.Add(BluffMoveType.DiscardFour);
            }

            if (CanConcede(playerIndex))
            {
                result.Add(BluffMoveType.Concede);
            }

            return result;
        }

        public bool TryApplyMove(int playerIndex, BluffMove move, out string? error)
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

            switch (move.Type)
            {
                case BluffMoveType.Declare:
                    return TryThrow(playerIndex, move, declaring: true, out error);
                case BluffMoveType.Follow:
                    return TryThrow(playerIndex, move, declaring: false, out error);
                case BluffMoveType.Challenge:
                    return TryChallenge(playerIndex, out error);
                case BluffMoveType.DiscardFour:
                    return TryDiscardFour(playerIndex, move.Rank, out error);
                case BluffMoveType.Concede:
                    return TryConcede(playerIndex, out error);
                default:
                    error = "Неизвестный ход.";
                    return false;
            }
        }

        // ---------- броски ----------

        private bool TryThrow(int playerIndex, BluffMove move, bool declaring, out string? error)
        {
            if (declaring && Phase != BluffPhase.RoundStart)
            {
                error = "Ранг уже назван, менять его нельзя.";
                return false;
            }

            if (!declaring && Phase != BluffPhase.Chain)
            {
                error = "Круг не начат — сначала назови ранг.";
                return false;
            }

            if (move.OpensShootout && !ShootoutOffered)
            {
                error = "Перестрелку сейчас не объявляют.";
                return false;
            }

            if (playerIndex != CurrentPlayerIndex)
            {
                error = "Сейчас не твой ход.";
                return false;
            }

            var player = _players[playerIndex];
            if (player.IsOut)
            {
                error = "Карт на руках нет.";
                return false;
            }

            if (move.Cards.Count < 1 || move.Cards.Count > _options.MaxCardsPerThrow)
            {
                error = "За раз кладут от 1 до " + _options.MaxCardsPerThrow + " карт.";
                return false;
            }

            var rank = declaring ? move.Rank : CurrentRank!.Value;
            if (declaring && !BluffRules.CanBeDeclared(rank))
            {
                error = "Тузами заявляться нельзя — их можно только подкладывать втихую.";
                return false;
            }

            if (!CanTakeFromHand(player, move.Cards))
            {
                error = "Таких карт на руках нет.";
                return false;
            }

            foreach (var card in move.Cards)
            {
                player.Remove(card);
            }

            _pot.AddRange(move.Cards);
            _packets.Add(new BluffPacket(playerIndex, rank, move.Cards));
            _log.Add(BluffLogEntry.Throw(player, rank, move.DeclaredCount));

            CurrentRank = rank;
            Phase = BluffPhase.Chain;

            // Объявление вслух идёт после броска, а не до него: пока карты не легли,
            // называть перестрелку не на что — ранг выбирают вместе с картами.
            if (move.OpensShootout)
            {
                IsShootout = true;
                ShootoutStarterIndex = playerIndex;
                _roundsSinceShootout = 0;
                _log.Add(BluffLogEntry.Shootout(player, rank));
            }

            if (player.IsOut)
            {
                RecordOut(player);
            }

            // Отвечать должен кто-то другой: сам себе цепочку не продолжишь. Если ответить
            // некому, круг обрывается и партия вместе с ним — котёл так и остаётся лежать.
            var responder = NextHolderAfter(playerIndex);
            if (responder == null)
            {
                Finish("Отвечать больше некому.");
                error = null;
                return true;
            }

            CurrentPlayerIndex = responder.Value;
            error = null;
            return true;
        }

        // ---------- проверка ----------

        private bool TryChallenge(int challengerIndex, out string? error)
        {
            if (Phase != BluffPhase.Chain)
            {
                error = "Проверять нечего.";
                return false;
            }

            if (challengerIndex != CurrentPlayerIndex)
            {
                error = "Проверяет только следующий по кругу.";
                return false;
            }

            var packet = LastPacket;
            if (packet == null)
            {
                error = "Проверять нечего.";
                return false;
            }

            var challenger = _players[challengerIndex];
            var target = _players[packet.PlayerIndex];

            // Проверкой круг кончается: у прежних петухов срок идёт. Проверять в перестрелке
            // не запрещено — хоть на первом же броске, — но кто решился, тот петух на два
            // следующих круга. Уже был в шапке — срок начинается заново.
            foreach (var player in _players)
            {
                player.RoundPassed();
            }

            var brokeShootout = IsShootout;
            if (brokeShootout)
            {
                challenger.MarkRooster(RoosterRounds);
            }

            // Врал — забирает сам, сказал правду — забирает тот, кто не поверил.
            var takerIndex = packet.IsLie ? target.Index : challenger.Index;
            var taker = _players[takerIndex];
            var taken = _pot.Count;

            taker.AddRange(_pot);

            // Забрав котёл, вышедший возвращается за стол: место освобождается обратно.
            _finishOrder.Remove(takerIndex);

            _log.Add(BluffLogEntry.Challenge(challenger, target, packet, takerIndex, taken, brokeShootout));

            _pot.Clear();
            _packets.Clear();
            CurrentRank = null;
            IsShootout = false;
            ShootoutStarterIndex = null;

            // Новый круг начинает следующий за тем, кто забрал: взял карты — ещё и ход пропустил.
            var leader = NextHolderAfter(takerIndex);
            if (leader == null)
            {
                Finish("За столом больше некому ходить.");
                error = null;
                return true;
            }

            StartRound(leader.Value);
            error = null;
            return true;
        }

        // ---------- каре ----------

        private bool TryDiscardFour(int playerIndex, Rank rank, out string? error)
        {
            if (!_options.AllowFourOfAKind)
            {
                error = "Каре в этой партии не сбрасывают.";
                return false;
            }

            var player = _players[playerIndex];
            if (player.IsOut)
            {
                error = "Карт на руках нет.";
                return false;
            }

            if (rank == Rank.Ace && !_options.AcesCanFormFour)
            {
                error = "Каре из тузов не считается.";
                return false;
            }

            if (player.CountOf(rank) < 4)
            {
                error = "Каре не собрано.";
                return false;
            }

            for (var i = 0; i < 4; i++)
            {
                foreach (var card in player.Hand)
                {
                    if (card.Rank == rank)
                    {
                        player.Remove(card);
                        _removed.Add(card);
                        break;
                    }
                }
            }

            _log.Add(BluffLogEntry.FourOfAKind(player, rank));

            if (!player.IsOut)
            {
                error = null;
                return true;
            }

            RecordOut(player);

            // Каре сбрасывают вне очереди, поэтому ход оно не передаёт. Но если рука
            // опустела, ходить этому игроку уже нечем — очередь двигаем за него.
            if (playerIndex == CurrentPlayerIndex)
            {
                var next = NextHolderAfter(playerIndex);
                if (next == null)
                {
                    Finish("За столом больше некому ходить.");
                    error = null;
                    return true;
                }

                if (Phase == BluffPhase.RoundStart)
                {
                    StartRound(next.Value);
                }
                else
                {
                    CurrentPlayerIndex = next.Value;
                }
            }
            else if (Phase == BluffPhase.RoundStart)
            {
                CheckRoundStart();
            }

            error = null;
            return true;
        }

        // ---------- сдача ----------

        private bool TryConcede(int playerIndex, out string? error)
        {
            if (!CanConcede(playerIndex))
            {
                error = "Сдаваться пока рано.";
                return false;
            }

            _conceded = playerIndex;
            _log.Add(BluffLogEntry.Concede(_players[playerIndex]));

            Finish("Партию не доиграли.");
            error = null;
            return true;
        }

        // ---------- состояние круга ----------

        private void StartRound(int leaderIndex)
        {
            RoundLeaderIndex = leaderIndex;
            CurrentPlayerIndex = leaderIndex;
            CurrentRank = null;
            IsShootout = false;
            ShootoutStarterIndex = null;
            Phase = BluffPhase.RoundStart;

            _roundsSinceShootout++;
            _shootoutOffered = _options.AllowShootout
                               && _roundsSinceShootout > ShootoutCooldownRounds
                               && _fate.NextDouble() < ShootoutChance;

            CheckRoundStart();
        }

        /// <summary>
        /// Партия заканчивается на границе кругов, а не посреди цепочки: иначе у кого-то
        /// отобрали бы право проверить последний пакет.
        /// </summary>
        private void CheckRoundStart()
        {
            if (HoldersCount < 2)
            {
                Finish("Карты остались у одного игрока.");
                return;
            }

            foreach (var player in _players)
            {
                if (!player.IsOut && !player.HoldsOnlyAces)
                {
                    return;
                }
            }

            Finish("На руках у всех только тузы — честно ходить нечем.");
        }

        private void RecordOut(BluffPlayer player)
        {
            if (_finishOrder.Contains(player.Index))
            {
                return;
            }

            _finishOrder.Add(player.Index);
            _log.Add(BluffLogEntry.PlayerOut(player, _finishOrder.Count));
        }

        private int? NextHolderAfter(int index)
        {
            for (var step = 1; step < _players.Count; step++)
            {
                var candidate = (index + step) % _players.Count;
                if (!_players[candidate].IsOut)
                {
                    return candidate;
                }
            }

            return null;
        }

        private bool IsSeated(int playerIndex) => playerIndex >= 0 && playerIndex < _players.Count;

        /// <summary>
        /// Зерно для кубика перестрелки, посчитанное по розданным рукам. Отдельного
        /// параметра нет намеренно: партия должна полностью определяться колодой.
        /// </summary>
        private int FateSeed()
        {
            unchecked
            {
                var seed = 17;
                foreach (var player in _players)
                {
                    foreach (var card in player.Hand)
                    {
                        seed = seed * 31 + (int)card.Rank * 8 + (int)card.Suit;
                    }

                    seed = seed * 31 + 7;
                }

                return seed == int.MinValue ? 0 : Math.Abs(seed);
            }
        }

        private static bool CanTakeFromHand(BluffPlayer player, IReadOnlyList<Card> cards)
        {
            var rest = new List<Card>(player.Hand);
            foreach (var card in cards)
            {
                if (!rest.Remove(card))
                {
                    return false;
                }
            }

            return true;
        }

        // ---------- финал ----------

        private void Finish(string reason)
        {
            Phase = BluffPhase.Finished;
            IsShootout = false;
            ShootoutStarterIndex = null;
            _shootoutOffered = false;

            ReclaimPot();

            var holders = new List<BluffPlayer>();
            foreach (var player in _players)
            {
                if (!player.IsOut)
                {
                    holders.Add(player);
                }
            }

            // Сначала те, кто успел избавиться от карт, в порядке выхода. Потом остальные:
            // чем меньше тузов, тем выше место, при равенстве — у кого меньше карт.
            holders.Sort((left, right) =>
            {
                var byAces = left.AceCount.CompareTo(right.AceCount);
                if (byAces != 0)
                {
                    return byAces;
                }

                var byCards = left.Hand.Count.CompareTo(right.Hand.Count);
                return byCards != 0 ? byCards : left.Index.CompareTo(right.Index);
            });

            _standings.Clear();
            _standings.AddRange(_finishOrder);
            foreach (var player in holders)
            {
                _standings.Add(player.Index);
            }

            _losers.Clear();

            // Сдавшийся проигрывает сам по себе: он это и признал.
            if (_conceded.HasValue)
            {
                _losers.Add(_conceded.Value);
            }
            else if (holders.Count > 0)
            {
                var most = 0;
                foreach (var player in holders)
                {
                    if (player.AceCount > most)
                    {
                        most = player.AceCount;
                    }
                }

                foreach (var player in holders)
                {
                    if (player.AceCount == most)
                    {
                        _losers.Add(player.Index);
                    }
                }
            }

            _log.Add(BluffLogEntry.GameOver(reason + " " + LoserText()));
        }

        /// <summary>
        /// Котёл не растворяется в воздухе. Если партия кончилась, не дождавшись проверки,
        /// лежащие в нём карты забирает тот, кто бросал последним. Иначе вместе с котлом из
        /// игры уходят тузы — а они и решают, кто проиграл, и без них партия заканчивалась
        /// ничем: все скинули всё, победителя нет, проигравшего тоже.
        /// </summary>
        private void ReclaimPot()
        {
            if (_pot.Count == 0 || !LastThrowerIndex.HasValue)
            {
                return;
            }

            var taker = _players[LastThrowerIndex.Value];
            taker.AddRange(_pot);
            _finishOrder.Remove(taker.Index);

            _pot.Clear();
            _packets.Clear();
            CurrentRank = null;
        }

        private string LoserText()
        {
            if (_conceded.HasValue)
            {
                return _players[_conceded.Value].Name + " сдался.";
            }

            if (_losers.Count == 0)
            {
                return "Проигравших нет.";
            }

            var aces = _players[_losers[0]].AceCount;
            if (_losers.Count == 1)
            {
                return "Проиграл " + _players[_losers[0]].Name + ": тузов " + aces + ".";
            }

            var names = new List<string>();
            foreach (var index in _losers)
            {
                names.Add(_players[index].Name);
            }

            return "Ничья: по " + aces + " туза у " + string.Join(", ", names) + ", добивать нечем.";
        }
    }
}
