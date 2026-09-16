using System;
using System.Collections.Generic;
using System.Linq;
using CardGame.Core.Cards;

namespace CardGame.Core.Games.Durak
{
    /// <summary>
    /// Подкидной дурак на 2-6 игроков. Чистая логика без привязки к движку:
    /// один и тот же класс работает в Unity, на сервере и в тестах.
    /// </summary>
    public sealed class DurakGame
    {
        private readonly List<DurakPlayer> _players;
        private readonly List<TableSlot> _table = new List<TableSlot>();
        private readonly List<Card> _discard = new List<Card>();
        private readonly HashSet<int> _passed = new HashSet<int>();
        private readonly Deck _deck;

        private DurakGame(List<DurakPlayer> players, Deck deck, Card trumpCard)
        {
            _players = players;
            _deck = deck;
            TrumpCard = trumpCard;
            Trump = trumpCard.Suit;
        }

        public Suit Trump { get; }
        public Card TrumpCard { get; }
        public int AttackerIndex { get; private set; }
        public int DefenderIndex { get; private set; }
        public bool DefenderTook { get; private set; }
        public bool IsFinished { get; private set; }

        /// <summary>Индекс проигравшего (дурака). null — ничья.</summary>
        public int? LoserIndex { get; private set; }

        public IReadOnlyList<DurakPlayer> Players => _players;
        public IReadOnlyList<TableSlot> Table => _table;
        public int DeckCount => _deck.Count;
        public int DiscardCount => _discard.Count;

        public DurakPlayer Attacker => _players[AttackerIndex];
        public DurakPlayer Defender => _players[DefenderIndex];

        public static DurakGame Create(IReadOnlyList<string> playerNames, int seed)
        {
            if (playerNames.Count < 2 || playerNames.Count > 6)
            {
                throw new ArgumentException("Дурак поддерживает от 2 до 6 игроков.", nameof(playerNames));
            }

            var deck = Deck.Standard36(seed);
            var trumpCard = deck.Bottom!.Value;
            var players = playerNames.Select((name, i) => new DurakPlayer(i, name)).ToList();

            var game = new DurakGame(players, deck, trumpCard);
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
                default:
                    error = "Неизвестный тип хода.";
                    return false;
            }
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
            ResolveBoutIfReady();
            return true;
        }

        private bool CanAttackWith(int playerIndex, Card card)
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

            if (_table.Count == 0)
            {
                return playerIndex == AttackerIndex;
            }

            return _table.Any(s => s.Attack.Rank == card.Rank || (s.IsDefended && s.Defense!.Value.Rank == card.Rank));
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

            _table.Clear();
            _passed.Clear();
            DefenderTook = false;

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
    }
}
