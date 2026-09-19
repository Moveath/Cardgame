using System;
using System.Collections.Generic;
using System.Linq;
using CardGame.Core.Cards;

namespace CardGame.Core.Games.Durak
{
    /// <summary>
    /// Подкидной дурак на 2-8 игроков. Чистая логика без привязки к движку:
    /// один и тот же класс работает в Unity, на сервере и в тестах.
    /// </summary>
    public sealed class DurakGame
    {
        private readonly List<DurakPlayer> _players;
        private readonly List<TableSlot> _table = new List<TableSlot>();
        private readonly List<Card> _discard = new List<Card>();
        private readonly HashSet<int> _passed = new HashSet<int>();
        private readonly Deck _deck;
        private readonly DurakOptions _options;
        private PendingChallenge? _pendingChallenge;

        // Попался на обмане или зря обвинил — до конца раздачи сидишь тихо. Именно это
        // убивает бесконечный повтор: карту-то вернули, но сыграть её снова уже нельзя.
        private readonly HashSet<int> _cheatBlocked = new HashSet<int>();
        private readonly HashSet<int> _challengeBlocked = new HashSet<int>();
        private readonly List<Card> _lastBoutCards = new List<Card>();

        // Раздача, готовая закрыться, но подвешенная до конца окна оспаривания. Без этого
        // обман последней картой был бы неуязвим: стол уходил бы в биту в тот же миг.
        private bool _boutResolutionPending;

        private DurakGame(List<DurakPlayer> players, Deck deck, Card trumpCard, DurakOptions options)
        {
            _players = players;
            _deck = deck;
            TrumpCard = trumpCard;
            Trump = trumpCard.Suit;
            _options = options;
        }

        public Suit Trump { get; }
        public Card TrumpCard { get; }
        public int AttackerIndex { get; private set; }
        public int DefenderIndex { get; private set; }
        public bool DefenderTook { get; private set; }
        public bool IsFinished { get; private set; }
        public DurakOptions Options => _options;

        /// <summary>Индекс проигравшего (дурака). null — ничья.</summary>
        public int? LoserIndex { get; private set; }

        public IReadOnlyList<DurakPlayer> Players => _players;
        public IReadOnlyList<TableSlot> Table => _table;
        public int DeckCount => _deck.Count;
        public int DiscardCount => _discard.Count;

        public DurakPlayer Attacker => _players[AttackerIndex];
        public DurakPlayer Defender => _players[DefenderIndex];

        /// <summary>
        /// Последний ход, который ещё можно оспорить: окно закрывается автоматически, как только
        /// применяется следующий ход (любой, не обязательно к этой же карте). Само по себе не
        /// говорит, был ли это обман, — иначе шулерство теряло бы смысл, выдавая себя в интерфейсе.
        /// </summary>
        public bool HasChallengeableMove => _pendingChallenge.HasValue;
        public int? ChallengeableSlot => _pendingChallenge?.SlotIndex;
        public bool? ChallengeableIsAttackCard => _pendingChallenge?.WasAttack;
        public int? ChallengeablePlayerIndex => _pendingChallenge?.PlayerIndex;

        /// <summary>
        /// Только для ИИ соперников: было ли последнее оспариваемое действие обманом на самом деле.
        /// Человеку это показывать нельзя — экран должен заставлять смотреть на саму карту,
        /// а не подсвечивать обман заранее, иначе шулерство теряет смысл.
        /// </summary>
        public bool ChallengeTargetIsActuallyIllegal() => _pendingChallenge is { WasActuallyIllegal: true };

        /// <summary>Не попадался ли игрок на обмане в этой раздаче — пойманному шулерить больше нельзя.</summary>
        public bool CanCheat(int playerIndex) => _options.AllowCheating && !_cheatBlocked.Contains(playerIndex);

        /// <summary>Не ошибся ли игрок с обвинением в этой раздаче — после промаха оспаривать больше нельзя.</summary>
        public bool CanChallenge(int playerIndex) => _options.AllowCheating && !_challengeBlocked.Contains(playerIndex);

        /// <summary>
        /// Счётчик сыгранных раздач и куда ушли карты последней. Нужно клиенту, чтобы показать
        /// перелёт карт к тому, кто забрал, вместо мгновенного исчезновения стола.
        /// </summary>
        public int BoutsFinished { get; private set; }
        public int? LastBoutTakenBy { get; private set; }
        public IReadOnlyList<Card> LastBoutCards => _lastBoutCards;

        public static DurakGame Create(IReadOnlyList<string> playerNames, int seed) =>
            Create(playerNames, Deck.Standard36(seed), DurakOptions.Classic);

        /// <summary>Та же партия, но с колодой на выбор (24/36/52/54 карты) — например из настроек комнаты.</summary>
        public static DurakGame Create(IReadOnlyList<string> playerNames, Deck deck) =>
            Create(playerNames, deck, DurakOptions.Classic);

        /// <summary>Полный контроль: колода и необязательные варианты правил (перевод, соседи, шулерство).</summary>
        public static DurakGame Create(IReadOnlyList<string> playerNames, Deck deck, DurakOptions options)
        {
            if (playerNames.Count < 2 || playerNames.Count > 8)
            {
                throw new ArgumentException("Дурак поддерживает от 2 до 8 игроков.", nameof(playerNames));
            }

            var trumpCard = deck.Bottom!.Value;
            var players = playerNames.Select((name, i) => new DurakPlayer(i, name)).ToList();

            var game = new DurakGame(players, deck, trumpCard, options);
            game.DealOpeningHands();
            game.AttackerIndex = game.FindFirstAttacker();
            game.DefenderIndex = game.NextActive(game.AttackerIndex);
            return game;
        }

        public IReadOnlyList<DurakMove> GetLegalMoves(int playerIndex)
        {
            var moves = new List<DurakMove>();
            if (IsFinished)
            {
                return moves;
            }

            var player = _players[playerIndex];
            if (player.IsOut)
            {
                return moves;
            }

            if (playerIndex == DefenderIndex)
            {
                if (DefenderTook)
                {
                    return moves;
                }

                for (var slot = 0; slot < _table.Count; slot++)
                {
                    if (_table[slot].IsDefended)
                    {
                        continue;
                    }

                    foreach (var card in player.Hand)
                    {
                        if (DurakRules.Beats(card, _table[slot].Attack, Trump))
                        {
                            moves.Add(DurakMove.Defend(card, slot));
                        }
                    }
                }

                if (CanTransferStructurally())
                {
                    var ranksOnTable = _table.Select(s => s.Attack.Rank).ToHashSet();
                    foreach (var card in player.Hand)
                    {
                        if (ranksOnTable.Contains(card.Rank))
                        {
                            moves.Add(DurakMove.Transfer(card));
                        }
                    }
                }

                if (_table.Any(s => !s.IsDefended))
                {
                    moves.Add(DurakMove.Take());
                }

                return moves;
            }

            foreach (var card in player.Hand)
            {
                if (CanAttackWith(playerIndex, card))
                {
                    moves.Add(DurakMove.Attack(card));
                }
            }

            if (_table.Count > 0 && !_passed.Contains(playerIndex))
            {
                moves.Add(DurakMove.Pass());
            }

            return moves;
        }

        /// <summary>
        /// Шулерские ходы: те же карты, что и обычно, но именно те, которые НЕ подходят по правилам —
        /// честную карту незачем подсовывать под видом обмана. Отдельный список, а не часть
        /// GetLegalMoves, — иначе бот и обычный интерфейс начали бы принимать обман за легальный ход.
        /// </summary>
        public IReadOnlyList<DurakMove> GetCheatMoves(int playerIndex)
        {
            var moves = new List<DurakMove>();
            if (!CanCheat(playerIndex) || IsFinished)
            {
                return moves;
            }

            var player = _players[playerIndex];
            if (player.IsOut)
            {
                return moves;
            }

            if (playerIndex == DefenderIndex)
            {
                if (DefenderTook)
                {
                    return moves;
                }

                for (var slot = 0; slot < _table.Count; slot++)
                {
                    if (_table[slot].IsDefended)
                    {
                        continue;
                    }

                    foreach (var card in player.Hand)
                    {
                        if (!DurakRules.Beats(card, _table[slot].Attack, Trump))
                        {
                            moves.Add(DurakMove.CheatDefend(card, slot));
                        }
                    }
                }

                // Перевод "не той" картой: структурные условия те же, врём только про достоинство.
                if (CanTransferStructurally())
                {
                    foreach (var card in player.Hand)
                    {
                        if (!MatchesTableRank(card))
                        {
                            moves.Add(DurakMove.CheatTransfer(card));
                        }
                    }
                }

                return moves;
            }

            if (_table.Count == 0 || !CanAttackStructurally(playerIndex))
            {
                return moves;
            }

            foreach (var card in player.Hand)
            {
                if (!MatchesTableRank(card))
                {
                    moves.Add(DurakMove.CheatAttack(card));
                }
            }

            return moves;
        }

        /// <summary>
        /// Условия перевода, не зависящие от самой карты: правило включено, в раздаче ничего
        /// не отбито, есть кому переводить и у него хватит карт. Обман врёт только про
        /// достоинство карты, поэтому всё остальное держится и для шулерского перевода.
        /// </summary>
        private bool CanTransferStructurally()
        {
            if (!_options.AllowTransfer || DefenderTook || _table.Count == 0)
            {
                return false;
            }

            if (_table.Count >= DurakRules.MaxTableSlots)
            {
                return false;
            }

            if (_table.Any(s => s.IsDefended))
            {
                return false;
            }

            // Вдвоём перевод тоже работает: бой возвращается атакующему, и уже он отбивается.
            // Не работает только если за столом реально не осталось второго живого игрока.
            var next = NextActive(DefenderIndex);
            if (next == DefenderIndex)
            {
                return false;
            }

            return _players[next].Hand.Count > _table.Count;
        }

        public bool TryApplyMove(int playerIndex, DurakMove move, out string? error)
        {
            error = null;
            if (IsFinished)
            {
                error = "Партия уже завершена.";
                return false;
            }

            var player = _players[playerIndex];
            if (player.IsOut)
            {
                error = "Игрок уже вышел из игры.";
                return false;
            }

            switch (move.Type)
            {
                case DurakMoveType.Attack:
                    return TryAttack(playerIndex, move.Card, ref error);
                case DurakMoveType.Defend:
                    return TryDefend(playerIndex, move.Card, move.TargetSlot, ref error);
                case DurakMoveType.Take:
                    return TryTake(playerIndex, ref error);
                case DurakMoveType.Pass:
                    return TryPass(playerIndex, ref error);
                case DurakMoveType.Transfer:
                    return TryTransfer(playerIndex, move.Card, ref error);
                case DurakMoveType.CheatAttack:
                    return TryCheatAttack(playerIndex, move.Card, ref error);
                case DurakMoveType.CheatDefend:
                    return TryCheatDefend(playerIndex, move.Card, move.TargetSlot, ref error);
                case DurakMoveType.CheatTransfer:
                    return TryCheatTransfer(playerIndex, move.Card, ref error);
                default:
                    error = "Неизвестный тип хода.";
                    return false;
            }
        }

        /// <summary>
        /// Оспорить последний ход: если он и правда оказался обманом — карта возвращается
        /// шулеру и ход переигрывается по-настоящему; если ход был честным — ничего не
        /// происходит (штрафа за ложное обвинение нет, можно пробовать снова и снова).
        /// </summary>
        public bool TryChallenge(int challengerIndex, int slotIndex, bool isAttackCard, out string? error)
        {
            error = null;

            if (challengerIndex < 0 || challengerIndex >= _players.Count || _players[challengerIndex].IsOut)
            {
                error = "Такой игрок не может оспаривать ходы.";
                return false;
            }

            if (!CanChallenge(challengerIndex))
            {
                error = "В этой раздаче ты уже ошибся с обвинением.";
                return false;
            }

            if (_pendingChallenge is not { } pending || pending.SlotIndex != slotIndex || pending.WasAttack != isAttackCard)
            {
                error = "Эту карту уже нельзя оспорить.";
                return false;
            }

            if (pending.PlayerIndex == challengerIndex)
            {
                error = "Нельзя оспорить свой же ход.";
                return false;
            }

            // Обвинение стоит ровно столько же, сколько обман: ошибся — берёшь карту и
            // до конца раздачи молчишь. Иначе выгодно тыкать в каждую карту подряд.
            if (!pending.WasActuallyIllegal)
            {
                _challengeBlocked.Add(challengerIndex);
                DrawPenalty(challengerIndex);

                // Обвинение было последним, что держало раздачу открытой.
                ClosePendingChallenge();
                error = "Ход был честным — штрафная карта.";
                return false;
            }

            var cheater = _players[pending.PlayerIndex];
            if (pending.WasAttack)
            {
                var card = _table[slotIndex].Attack;
                _table.RemoveAt(slotIndex);
                cheater.Add(card);
            }
            else
            {
                var slot = _table[slotIndex];
                var card = slot.Defense!.Value;
                slot.Defense = null;
                cheater.Add(card);
            }

            // Разоблачённый перевод возвращает не только карту, но и роли: бой снова у того,
            // кто пытался от него уехать.
            if (pending.WasTransfer)
            {
                AttackerIndex = pending.PreviousAttacker;
                DefenderIndex = pending.PreviousDefender;
            }

            _cheatBlocked.Add(pending.PlayerIndex);
            DrawPenalty(pending.PlayerIndex);

            _passed.Clear();
            _pendingChallenge = null;

            // Стол изменился — раздача, которая вот-вот закрывалась, уже не закрывается.
            // Но проверить заново надо: отмена подкида могла, наоборот, её завершить.
            _boutResolutionPending = false;
            ResolveBoutIfReady();
            return true;
        }

        /// <summary>
        /// Время на реакцию вышло: последний ход больше не оспорить, и если раздача
        /// на нём заканчивалась — она наконец закрывается. Зовётся клиентом, потому что
        /// только он знает, сколько секунд дать людям на то, чтобы заметить обман.
        /// </summary>
        public void CloseChallengeWindow() => ClosePendingChallenge();

        private void ClosePendingChallenge()
        {
            _pendingChallenge = null;

            if (!_boutResolutionPending)
            {
                return;
            }

            _boutResolutionPending = false;
            ResolveBoutIfReady();
        }

        /// <summary>Штрафная карта из колоды. Колода пуста — штраф просто не выдаётся.</summary>
        private void DrawPenalty(int playerIndex)
        {
            if (_deck.IsEmpty)
            {
                return;
            }

            _players[playerIndex].Add(_deck.Draw());
        }

        private bool TryAttack(int playerIndex, Card card, ref string? error)
        {
            if (playerIndex == DefenderIndex)
            {
                error = "Защищающийся не может подкидывать.";
                return false;
            }

            if (!_players[playerIndex].Hand.Contains(card))
            {
                error = "Карты нет на руке.";
                return false;
            }

            if (!CanAttackWith(playerIndex, card))
            {
                error = "Такой картой сейчас ходить нельзя.";
                return false;
            }

            _players[playerIndex].Remove(card);
            _table.Add(new TableSlot(card));
            _passed.Clear();

            // Честный ход тоже помечается "оспариваемым" (заведомо не обманом) — иначе сама
            // возможность оспорить именно эту карту выдавала бы, что где-то есть подвох.
            _pendingChallenge = new PendingChallenge(playerIndex, _table.Count - 1, wasAttack: true, wasActuallyIllegal: false);
            ResolveBoutIfReady();
            return true;
        }

        private bool TryDefend(int playerIndex, Card card, int slotIndex, ref string? error)
        {
            if (playerIndex != DefenderIndex)
            {
                error = "Отбиваться может только защищающийся.";
                return false;
            }

            if (DefenderTook)
            {
                error = "Карты уже забраны, отбиваться поздно.";
                return false;
            }

            if (slotIndex < 0 || slotIndex >= _table.Count)
            {
                error = "Нет такой карты на столе.";
                return false;
            }

            var slot = _table[slotIndex];
            if (slot.IsDefended)
            {
                error = "Эта карта уже отбита.";
                return false;
            }

            if (!_players[playerIndex].Hand.Contains(card))
            {
                error = "Карты нет на руке.";
                return false;
            }

            if (!DurakRules.Beats(card, slot.Attack, Trump))
            {
                error = card + " не бьёт " + slot.Attack + ".";
                return false;
            }

            _players[playerIndex].Remove(card);
            slot.Defense = card;
            _pendingChallenge = new PendingChallenge(playerIndex, slotIndex, wasAttack: false, wasActuallyIllegal: false);
            ResolveBoutIfReady();
            return true;
        }

        private bool TryTake(int playerIndex, ref string? error)
        {
            if (playerIndex != DefenderIndex)
            {
                error = "Забрать карты может только защищающийся.";
                return false;
            }

            if (DefenderTook)
            {
                error = "Карты уже забраны.";
                return false;
            }

            if (!_table.Any(s => !s.IsDefended))
            {
                error = "Нечего забирать.";
                return false;
            }

            DefenderTook = true;
            _passed.Clear();
            _pendingChallenge = null;
            ResolveBoutIfReady();
            return true;
        }

        private bool TryPass(int playerIndex, ref string? error)
        {
            if (playerIndex == DefenderIndex)
            {
                error = "Защищающийся не может пасовать.";
                return false;
            }

            if (_table.Count == 0)
            {
                error = "Атакующий обязан сделать первый ход.";
                return false;
            }

            _passed.Add(playerIndex);
            _pendingChallenge = null;
            ResolveBoutIfReady();
            return true;
        }

        private bool TryTransfer(int playerIndex, Card card, ref string? error)
        {
            if (!_options.AllowTransfer)
            {
                error = "Перевод выключен в этой партии.";
                return false;
            }

            if (playerIndex != DefenderIndex)
            {
                error = "Переводить может только защищающийся.";
                return false;
            }

            if (DefenderTook)
            {
                error = "Карты уже забраны, переводить поздно.";
                return false;
            }

            if (_table.Count == 0 || _table.Any(s => s.IsDefended))
            {
                error = "Переводить можно, только пока в этой раздаче ничего не отбито.";
                return false;
            }

            if (_table.Count >= DurakRules.MaxTableSlots)
            {
                error = "На столе больше нет места.";
                return false;
            }

            if (!_players[playerIndex].Hand.Contains(card))
            {
                error = "Карты нет на руке.";
                return false;
            }

            if (!MatchesTableRank(card))
            {
                error = "Нет карты такого достоинства на столе.";
                return false;
            }

            var nextDefender = NextActive(DefenderIndex);
            if (nextDefender == DefenderIndex)
            {
                error = "Переводить некому — за столом больше никого нет.";
                return false;
            }

            if (_players[nextDefender].Hand.Count <= _table.Count)
            {
                error = "У следующего игрока не хватит карт принять перевод.";
                return false;
            }

            ApplyTransfer(playerIndex, card, nextDefender, actuallyIllegal: false);
            return true;
        }

        private bool TryCheatTransfer(int playerIndex, Card card, ref string? error)
        {
            if (!CanCheat(playerIndex))
            {
                error = _options.AllowCheating
                    ? "В этой раздаче тебя уже поймали на обмане."
                    : "Шулерство выключено в этой партии.";
                return false;
            }

            if (playerIndex != DefenderIndex)
            {
                error = "Переводить, пусть и обманом, может только защищающийся.";
                return false;
            }

            if (!_players[playerIndex].Hand.Contains(card))
            {
                error = "Карты нет на руке.";
                return false;
            }

            // Врём только про достоинство карты: всё остальное — настоящие ограничения стола.
            if (!CanTransferStructurally())
            {
                error = "Переводить сейчас нельзя, даже обманом.";
                return false;
            }

            var nextDefender = NextActive(DefenderIndex);
            ApplyTransfer(playerIndex, card, nextDefender, actuallyIllegal: !MatchesTableRank(card));
            return true;
        }

        private void ApplyTransfer(int playerIndex, Card card, int nextDefender, bool actuallyIllegal)
        {
            var previousAttacker = AttackerIndex;
            var previousDefender = DefenderIndex;

            _players[playerIndex].Remove(card);
            _table.Add(new TableSlot(card));
            _passed.Clear();
            _pendingChallenge = new PendingChallenge(playerIndex, _table.Count - 1, wasAttack: true,
                wasActuallyIllegal: actuallyIllegal, wasTransfer: true,
                previousAttacker: previousAttacker, previousDefender: previousDefender);

            // Переведший сам становится атакующим. Это важно не только для порядка добора:
            // вдвоём бой возвращается прежнему атакующему, и без этой строки один и тот же
            // игрок оказался бы одновременно и атакующим, и защищающимся.
            AttackerIndex = playerIndex;
            DefenderIndex = nextDefender;
            ResolveBoutIfReady();
        }

        private bool TryCheatAttack(int playerIndex, Card card, ref string? error)
        {
            if (!CanCheat(playerIndex))
            {
                error = _options.AllowCheating
                    ? "В этой раздаче тебя уже поймали на обмане."
                    : "Шулерство выключено в этой партии.";
                return false;
            }

            if (!_players[playerIndex].Hand.Contains(card))
            {
                error = "Карты нет на руке.";
                return false;
            }

            if (_table.Count == 0 || !CanAttackStructurally(playerIndex))
            {
                error = "Так подкинуть сейчас нельзя, даже обманом.";
                return false;
            }

            var actuallyLegal = MatchesTableRank(card);

            _players[playerIndex].Remove(card);
            _table.Add(new TableSlot(card));
            _passed.Clear();
            _pendingChallenge = new PendingChallenge(playerIndex, _table.Count - 1, wasAttack: true, wasActuallyIllegal: !actuallyLegal);
            ResolveBoutIfReady();
            return true;
        }

        private bool TryCheatDefend(int playerIndex, Card card, int slotIndex, ref string? error)
        {
            if (!CanCheat(playerIndex))
            {
                error = _options.AllowCheating
                    ? "В этой раздаче тебя уже поймали на обмане."
                    : "Шулерство выключено в этой партии.";
                return false;
            }

            if (playerIndex != DefenderIndex)
            {
                error = "Отбиваться, пусть и обманом, может только защищающийся.";
                return false;
            }

            if (DefenderTook)
            {
                error = "Карты уже забраны, отбиваться поздно.";
                return false;
            }

            if (slotIndex < 0 || slotIndex >= _table.Count)
            {
                error = "Нет такой карты на столе.";
                return false;
            }

            var slot = _table[slotIndex];
            if (slot.IsDefended)
            {
                error = "Эта карта уже отбита.";
                return false;
            }

            if (!_players[playerIndex].Hand.Contains(card))
            {
                error = "Карты нет на руке.";
                return false;
            }

            var actuallyLegal = DurakRules.Beats(card, slot.Attack, Trump);

            _players[playerIndex].Remove(card);
            slot.Defense = card;
            _pendingChallenge = new PendingChallenge(playerIndex, slotIndex, wasAttack: false, wasActuallyIllegal: !actuallyLegal);
            ResolveBoutIfReady();
            return true;
        }

        private bool MatchesTableRank(Card card) =>
            _table.Any(s => s.Attack.Rank == card.Rank || (s.IsDefended && s.Defense!.Value.Rank == card.Rank));

        private bool CanAttackWith(int playerIndex, Card card)
        {
            if (!CanAttackStructurally(playerIndex))
            {
                return false;
            }

            if (_table.Count == 0)
            {
                return playerIndex == AttackerIndex;
            }

            return MatchesTableRank(card);
        }

        /// <summary>Проверки, не зависящие от конкретной карты: место на столе, очередь, право подкидывать вообще.</summary>
        private bool CanAttackStructurally(int playerIndex)
        {
            if (playerIndex == DefenderIndex || _players[playerIndex].IsOut)
            {
                return false;
            }

            if (_passed.Contains(playerIndex))
            {
                return false;
            }

            if (_table.Count >= DurakRules.MaxTableSlots)
            {
                return false;
            }

            var undefended = _table.Count(s => !s.IsDefended);
            if (undefended >= Defender.Hand.Count)
            {
                return false;
            }

            // Открывать раздачу может только атакующий вне зависимости от режима подкидывания —
            // ограничение "только соседи" касается только добавочных карт.
            if (_table.Count > 0 && _options.ThrowInMode == ThrowInMode.NeighboursOnly)
            {
                var nextAfterDefender = NextActive(DefenderIndex);
                if (playerIndex != AttackerIndex && playerIndex != nextAfterDefender)
                {
                    return false;
                }
            }

            return true;
        }

        private bool AllAttackersDone()
        {
            foreach (var player in _players)
            {
                if (player.IsOut || player.Index == DefenderIndex)
                {
                    continue;
                }

                if (_passed.Contains(player.Index))
                {
                    continue;
                }

                if (player.Hand.Any(card => CanAttackWith(player.Index, card)))
                {
                    return false;
                }
            }

            return true;
        }

        private void ResolveBoutIfReady()
        {
            if (!AllAttackersDone())
            {
                return;
            }

            // Пока последний ход можно оспорить, раздачу не закрываем: иначе шулерский
            // отбой последней карты уносил бы стол в биту раньше, чем кто-то успеет
            // сказать "стоп". Клиент закроет окно сам, когда время на реакцию выйдет.
            if (_options.AllowCheating && _pendingChallenge.HasValue)
            {
                _boutResolutionPending = true;
                return;
            }

            if (DefenderTook)
            {
                EndBout(true);
                return;
            }

            if (_table.Count > 0 && _table.All(s => s.IsDefended))
            {
                EndBout(false);
            }
            else if (_table.Count == 0)
            {
                // Атаковать некому и нечем — стол уходит защищающемуся как ход.
                EndBout(false);
            }
        }

        private void EndBout(bool defenderTakes)
        {
            var previousAttacker = AttackerIndex;
            var previousDefender = DefenderIndex;

            var tableCards = new List<Card>();
            foreach (var slot in _table)
            {
                tableCards.Add(slot.Attack);
                if (slot.IsDefended)
                {
                    tableCards.Add(slot.Defense!.Value);
                }
            }

            if (defenderTakes)
            {
                _players[previousDefender].AddRange(tableCards);
            }
            else
            {
                _discard.AddRange(tableCards);
            }

            // Куда ушла раздача — клиент покажет перелёт карт вместо мгновенного исчезновения.
            _lastBoutCards.Clear();
            _lastBoutCards.AddRange(tableCards);
            LastBoutTakenBy = defenderTakes ? previousDefender : (int?)null;
            BoutsFinished++;

            _table.Clear();
            _passed.Clear();
            DefenderTook = false;

            // Новая раздача — всем прощаются и пойманный обман, и промазанное обвинение.
            _cheatBlocked.Clear();
            _challengeBlocked.Clear();
            _pendingChallenge = null;
            _boutResolutionPending = false;

            RefillHands(previousAttacker, previousDefender);
            MarkFinishedPlayers();

            if (CheckGameOver())
            {
                return;
            }

            var nextAttacker = defenderTakes ? NextActive(previousDefender) : previousDefender;
            if (_players[nextAttacker].IsOut)
            {
                nextAttacker = NextActive(nextAttacker);
            }

            AttackerIndex = nextAttacker;
            DefenderIndex = NextActive(AttackerIndex);
        }

        private void RefillHands(int previousAttacker, int previousDefender)
        {
            var order = new List<int>();
            for (var offset = 0; offset < _players.Count; offset++)
            {
                var index = (previousAttacker + offset) % _players.Count;
                if (index != previousDefender)
                {
                    order.Add(index);
                }
            }

            order.Add(previousDefender);

            foreach (var index in order)
            {
                var player = _players[index];
                while (!player.IsOut && player.Hand.Count < DurakRules.HandSize && !_deck.IsEmpty)
                {
                    player.Add(_deck.Draw());
                }
            }
        }

        private void MarkFinishedPlayers()
        {
            if (!_deck.IsEmpty)
            {
                return;
            }

            foreach (var player in _players)
            {
                if (player.Hand.Count == 0)
                {
                    player.IsOut = true;
                }
            }
        }

        private bool CheckGameOver()
        {
            var active = _players.Where(p => !p.IsOut).ToList();
            if (active.Count > 1)
            {
                return false;
            }

            IsFinished = true;
            LoserIndex = active.Count == 1 ? active[0].Index : (int?)null;
            return true;
        }

        private int NextActive(int from)
        {
            for (var offset = 1; offset <= _players.Count; offset++)
            {
                var index = (from + offset) % _players.Count;
                if (!_players[index].IsOut)
                {
                    return index;
                }
            }

            return from;
        }

        private void DealOpeningHands()
        {
            for (var round = 0; round < DurakRules.HandSize; round++)
            {
                foreach (var player in _players)
                {
                    player.Add(_deck.Draw());
                }
            }
        }

        private int FindFirstAttacker()
        {
            var bestPlayer = -1;
            var bestRank = Rank.Ace + 1;

            foreach (var player in _players)
            {
                foreach (var card in player.Hand)
                {
                    if (card.Suit == Trump && card.Rank < bestRank)
                    {
                        bestRank = card.Rank;
                        bestPlayer = player.Index;
                    }
                }
            }

            return bestPlayer < 0 ? 0 : bestPlayer;
        }

        /// <summary>
        /// Единственный ход, который ещё можно оспорить. WasActuallyIllegal нарочно не
        /// публикуется наружу отдельным свойством — иначе интерфейс мог бы подсветить
        /// обман до того, как игрок сам его заметит, и шулерство потеряло бы смысл.
        /// </summary>
        private readonly struct PendingChallenge
        {
            public PendingChallenge(int playerIndex, int slotIndex, bool wasAttack, bool wasActuallyIllegal,
                bool wasTransfer = false, int previousAttacker = -1, int previousDefender = -1)
            {
                PlayerIndex = playerIndex;
                SlotIndex = slotIndex;
                WasAttack = wasAttack;
                WasActuallyIllegal = wasActuallyIllegal;
                WasTransfer = wasTransfer;
                PreviousAttacker = previousAttacker;
                PreviousDefender = previousDefender;
            }

            public int PlayerIndex { get; }
            public int SlotIndex { get; }
            public bool WasAttack { get; }
            public bool WasActuallyIllegal { get; }

            /// <summary>Перевод сдвигает роли, поэтому для отката их надо помнить.</summary>
            public bool WasTransfer { get; }
            public int PreviousAttacker { get; }
            public int PreviousDefender { get; }
        }
    }
}
