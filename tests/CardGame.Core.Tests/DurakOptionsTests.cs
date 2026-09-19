using System.Collections.Generic;
using System.Linq;
using CardGame.Bots;
using CardGame.Core.Cards;
using CardGame.Core.Games.Durak;
using Xunit;

namespace CardGame.Core.Tests;

/// <summary>
/// Необязательные варианты правил: перевод, подкидывание только соседями, шулерство.
/// Раздачи собраны вручную (не по seed), чтобы точно знать, у кого какие карты.
/// </summary>
public class DurakOptionsTests
{
    private static readonly Card Trump = new Card(Rank.Ace, Suit.Spades);

    private static readonly List<Card> Attacker = new()
    {
        new Card(Rank.Six, Suit.Clubs), new Card(Rank.Six, Suit.Spades),
        new Card(Rank.Seven, Suit.Clubs), new Card(Rank.Eight, Suit.Clubs),
        new Card(Rank.Nine, Suit.Clubs), new Card(Rank.Ten, Suit.Clubs)
    };

    // 7♦ не бьёт 6♣ (не та масть, не козырь) — заведомо нелегальный отбой.
    // J♣ бьёт 6♣ по-честному — карта для сценария "обманом назвали честный ход".
    private static readonly List<Card> Defender = new()
    {
        new Card(Rank.Six, Suit.Diamonds), new Card(Rank.Seven, Suit.Diamonds),
        new Card(Rank.Eight, Suit.Diamonds), new Card(Rank.Nine, Suit.Diamonds),
        new Card(Rank.Ten, Suit.Diamonds), new Card(Rank.Jack, Suit.Clubs)
    };

    private static readonly List<Card> Bystander = new()
    {
        new Card(Rank.Seven, Suit.Hearts), new Card(Rank.Eight, Suit.Hearts),
        new Card(Rank.Nine, Suit.Hearts), new Card(Rank.Ten, Suit.Hearts),
        new Card(Rank.Jack, Suit.Hearts), new Card(Rank.Queen, Suit.Hearts)
    };

    private static DurakGame CreateGame(DurakOptions options, params List<Card>[] hands)
    {
        var names = Enumerable.Range(0, hands.Length).Select(i => "P" + i).ToList();
        var deck = BuildDeck(hands, Trump);
        var game = DurakGame.Create(names, deck, options);

        // Только P0 держит козырь — значит первым ходит гарантированно он.
        Assert.Equal(0, game.AttackerIndex);
        Assert.Equal(1, game.DefenderIndex);
        return game;
    }

    private static Deck BuildDeck(IReadOnlyList<IReadOnlyList<Card>> hands, Card trump)
    {
        var cards = new List<Card>();
        for (var round = 0; round < DurakRules.HandSize; round++)
        {
            foreach (var hand in hands)
            {
                cards.Add(hand[round]);
            }
        }

        Assert.Equal(cards.Count, cards.Distinct().Count());
        Assert.DoesNotContain(trump, cards);

        var used = new HashSet<Card>(cards) { trump };
        for (var rank = Rank.Six; rank <= Rank.Ace; rank++)
        {
            for (var suit = Suit.Clubs; suit <= Suit.Spades; suit++)
            {
                var card = new Card(rank, suit);
                if (used.Add(card))
                {
                    cards.Add(card);
                }
            }
        }

        cards.Add(trump);
        return new Deck(cards);
    }

    [Fact]
    public void Transfer_MovesDefenseToNextPlayerAndAddsCard()
    {
        var game = CreateGame(new DurakOptions(true, ThrowInMode.All, false), Attacker, Defender, Bystander);

        Assert.True(game.TryApplyMove(0, DurakMove.Attack(new Card(Rank.Six, Suit.Clubs)), out var e1), e1);

        var transferCard = new Card(Rank.Six, Suit.Diamonds);
        Assert.Contains(game.GetLegalMoves(1), m => m.Type == DurakMoveType.Transfer && m.Card == transferCard);

        Assert.True(game.TryApplyMove(1, DurakMove.Transfer(transferCard), out var e2), e2);

        Assert.Equal(2, game.DefenderIndex);
        Assert.Equal(0, game.AttackerIndex);
        Assert.Equal(2, game.Table.Count);
        Assert.False(game.Table[0].IsDefended);
        Assert.False(game.Table[1].IsDefended);
        Assert.Equal(transferCard, game.Table[1].Attack);
        Assert.DoesNotContain(transferCard, game.Players[1].Hand);
    }

    [Fact]
    public void Transfer_NotOfferedWhenOptionDisabled()
    {
        var game = CreateGame(DurakOptions.Classic, Attacker, Defender, Bystander);

        Assert.True(game.TryApplyMove(0, DurakMove.Attack(new Card(Rank.Six, Suit.Clubs)), out var e1), e1);

        Assert.DoesNotContain(game.GetLegalMoves(1), m => m.Type == DurakMoveType.Transfer);

        var applied = game.TryApplyMove(1, DurakMove.Transfer(new Card(Rank.Six, Suit.Diamonds)), out var error);
        Assert.False(applied);
        Assert.NotNull(error);
    }

    [Fact]
    public void Transfer_BlockedOnceAnyCardInTheBoutIsDefended()
    {
        var game = CreateGame(new DurakOptions(true, ThrowInMode.All, false), Attacker, Defender, Bystander);

        Assert.True(game.TryApplyMove(0, DurakMove.Attack(new Card(Rank.Six, Suit.Clubs)), out var e1), e1);
        Assert.True(game.TryApplyMove(1, DurakMove.Defend(new Card(Rank.Jack, Suit.Clubs), 0), out var e2), e2);

        // У атакующего есть ещё один шестёрка (козырная) — раздача не завершается,
        // и на стол ложится второй, ещё не отбитый слот.
        Assert.True(game.TryApplyMove(0, DurakMove.Attack(new Card(Rank.Six, Suit.Spades)), out var e3), e3);

        Assert.DoesNotContain(game.GetLegalMoves(1), m => m.Type == DurakMoveType.Transfer);
        var applied = game.TryApplyMove(1, DurakMove.Transfer(new Card(Rank.Six, Suit.Diamonds)), out var error);
        Assert.False(applied);
        Assert.NotNull(error);
    }

    [Fact]
    public void Transfer_NeverOfferedInTwoPlayerGame()
    {
        var names = new List<string> { "P0", "P1" };
        var deck = BuildDeck(new IReadOnlyList<Card>[] { Attacker, Defender }, Trump);
        var game = DurakGame.Create(names, deck, new DurakOptions(true, ThrowInMode.All, false));

        Assert.True(game.TryApplyMove(0, DurakMove.Attack(new Card(Rank.Six, Suit.Clubs)), out var e1), e1);

        // Перевести можно только "дальше по кругу" — вдвоём это тот же атакующий, значит некому.
        Assert.DoesNotContain(game.GetLegalMoves(1), m => m.Type == DurakMoveType.Transfer);
    }

    [Fact]
    public void ThrowIn_NeighboursOnly_BlocksNonNeighbourButAllowsNeighbourAndOpener()
    {
        var farPlayer = new List<Card>
        {
            new Card(Rank.Six, Suit.Diamonds), new Card(Rank.Jack, Suit.Diamonds),
            new Card(Rank.Queen, Suit.Diamonds), new Card(Rank.King, Suit.Diamonds),
            new Card(Rank.Ace, Suit.Diamonds), new Card(Rank.King, Suit.Hearts)
        };
        var neighbour = new List<Card>
        {
            new Card(Rank.Six, Suit.Hearts), new Card(Rank.Seven, Suit.Hearts),
            new Card(Rank.Eight, Suit.Hearts), new Card(Rank.Nine, Suit.Hearts),
            new Card(Rank.Ten, Suit.Hearts), new Card(Rank.Jack, Suit.Hearts)
        };
        var uninvolvedDefenderHand = new List<Card>
        {
            new Card(Rank.Seven, Suit.Diamonds), new Card(Rank.Eight, Suit.Diamonds),
            new Card(Rank.Nine, Suit.Diamonds), new Card(Rank.Ten, Suit.Diamonds),
            new Card(Rank.Queen, Suit.Hearts), new Card(Rank.Ace, Suit.Hearts)
        };

        // Порядок: P0 атакует, P1 защищается, P2 — сосед после защитника (можно), P3 — дальний (нельзя).
        var game = CreateGame(
            new DurakOptions(false, ThrowInMode.NeighboursOnly, false),
            Attacker, uninvolvedDefenderHand, neighbour, farPlayer);

        Assert.True(game.TryApplyMove(0, DurakMove.Attack(new Card(Rank.Six, Suit.Clubs)), out var e1), e1);

        var farCard = new Card(Rank.Six, Suit.Diamonds);
        Assert.DoesNotContain(game.GetLegalMoves(3), m => m.Type == DurakMoveType.Attack);
        Assert.False(game.TryApplyMove(3, DurakMove.Attack(farCard), out var farError));
        Assert.NotNull(farError);

        var neighbourCard = new Card(Rank.Six, Suit.Hearts);
        Assert.Contains(game.GetLegalMoves(2), m => m.Type == DurakMoveType.Attack && m.Card == neighbourCard);
        Assert.True(game.TryApplyMove(2, DurakMove.Attack(neighbourCard), out var e2), e2);
    }

    [Fact]
    public void ThrowIn_AllMode_LetsAnyoneAddCards()
    {
        var farPlayer = new List<Card>
        {
            new Card(Rank.Six, Suit.Diamonds), new Card(Rank.Jack, Suit.Diamonds),
            new Card(Rank.Queen, Suit.Diamonds), new Card(Rank.King, Suit.Diamonds),
            new Card(Rank.Ace, Suit.Diamonds), new Card(Rank.King, Suit.Hearts)
        };
        var neighbour = new List<Card>
        {
            new Card(Rank.Six, Suit.Hearts), new Card(Rank.Seven, Suit.Hearts),
            new Card(Rank.Eight, Suit.Hearts), new Card(Rank.Nine, Suit.Hearts),
            new Card(Rank.Ten, Suit.Hearts), new Card(Rank.Jack, Suit.Hearts)
        };
        var uninvolvedDefenderHand = new List<Card>
        {
            new Card(Rank.Seven, Suit.Diamonds), new Card(Rank.Eight, Suit.Diamonds),
            new Card(Rank.Nine, Suit.Diamonds), new Card(Rank.Ten, Suit.Diamonds),
            new Card(Rank.Queen, Suit.Hearts), new Card(Rank.Ace, Suit.Hearts)
        };

        var game = CreateGame(DurakOptions.Classic, Attacker, uninvolvedDefenderHand, neighbour, farPlayer);

        Assert.True(game.TryApplyMove(0, DurakMove.Attack(new Card(Rank.Six, Suit.Clubs)), out var e1), e1);

        var farCard = new Card(Rank.Six, Suit.Diamonds);
        Assert.Contains(game.GetLegalMoves(3), m => m.Type == DurakMoveType.Attack && m.Card == farCard);
        Assert.True(game.TryApplyMove(3, DurakMove.Attack(farCard), out var e2), e2);
    }

    [Fact]
    public void Cheating_Disabled_RejectsCheatMovesAndReportsNoCheatMoves()
    {
        var game = CreateGame(DurakOptions.Classic, Attacker, Defender, Bystander);
        Assert.True(game.TryApplyMove(0, DurakMove.Attack(new Card(Rank.Six, Suit.Clubs)), out var e1), e1);

        Assert.Empty(game.GetCheatMoves(0));
        Assert.Empty(game.GetCheatMoves(1));

        var applied = game.TryApplyMove(1, DurakMove.CheatDefend(new Card(Rank.Seven, Suit.Diamonds), 0), out var error);
        Assert.False(applied);
        Assert.NotNull(error);
    }

    [Fact]
    public void Cheating_GetCheatMoves_OnlyOffersCardsThatWouldActuallyFail()
    {
        var game = CreateGame(new DurakOptions(false, ThrowInMode.All, true), Attacker, Defender, Bystander);
        Assert.True(game.TryApplyMove(0, DurakMove.Attack(new Card(Rank.Six, Suit.Clubs)), out var e1), e1);

        var cheatMoves = game.GetCheatMoves(1);
        Assert.Contains(cheatMoves, m => m.Card == new Card(Rank.Seven, Suit.Diamonds));
        Assert.DoesNotContain(cheatMoves, m => m.Card == new Card(Rank.Jack, Suit.Clubs));
    }

    [Fact]
    public void Cheating_HonestMoveIsAlsoChallengeableButSurvives()
    {
        // Клик по последней сыгранной карте не должен сам по себе выдавать обман — иначе игроки
        // просто искали бы взглядом "подсвеченную" карту вместо того, чтобы читать стол.
        // Значит, честный ход тоже обязан оставаться формально "оспариваемым".
        var game = CreateGame(new DurakOptions(false, ThrowInMode.All, true), Attacker, Defender, Bystander);
        Assert.True(game.TryApplyMove(0, DurakMove.Attack(new Card(Rank.Six, Suit.Clubs)), out var e1), e1);

        Assert.True(game.HasChallengeableMove);
        Assert.True(game.ChallengeableIsAttackCard);

        var applied = game.TryChallenge(1, 0, isAttackCard: true, out var error);

        Assert.False(applied);
        Assert.NotNull(error);
        Assert.Single(game.Table);
        Assert.Equal(new Card(Rank.Six, Suit.Clubs), game.Table[0].Attack);
    }

    [Fact]
    public void Cheating_CheatDefend_CanBeCaughtByAnyBystander()
    {
        var game = CreateGame(new DurakOptions(false, ThrowInMode.All, true), Attacker, Defender, Bystander);
        Assert.True(game.TryApplyMove(0, DurakMove.Attack(new Card(Rank.Six, Suit.Clubs)), out var e1), e1);

        var fakeDefense = new Card(Rank.Seven, Suit.Diamonds);
        Assert.True(game.TryApplyMove(1, DurakMove.CheatDefend(fakeDefense, 0), out var e2), e2);

        Assert.True(game.Table[0].IsDefended);
        Assert.True(game.HasChallengeableMove);
        Assert.Equal(0, game.ChallengeableSlot);
        Assert.False(game.ChallengeableIsAttackCard);
        Assert.Equal(1, game.ChallengeablePlayerIndex);

        // Ловит сторонний игрок (P2), не только тот, против кого сыграли.
        Assert.True(game.TryChallenge(2, 0, isAttackCard: false, out var e3), e3);

        Assert.False(game.Table[0].IsDefended);
        Assert.Contains(fakeDefense, game.Players[1].Hand);
        Assert.False(game.HasChallengeableMove);
    }

    [Fact]
    public void Cheating_CannotChallengeYourOwnMove()
    {
        var game = CreateGame(new DurakOptions(false, ThrowInMode.All, true), Attacker, Defender, Bystander);
        Assert.True(game.TryApplyMove(0, DurakMove.Attack(new Card(Rank.Six, Suit.Clubs)), out var e1), e1);
        Assert.True(game.TryApplyMove(1, DurakMove.CheatDefend(new Card(Rank.Seven, Suit.Diamonds), 0), out var e2), e2);

        var applied = game.TryChallenge(1, 0, isAttackCard: false, out var error);

        Assert.False(applied);
        Assert.NotNull(error);
        Assert.True(game.Table[0].IsDefended);
    }

    [Fact]
    public void Cheating_ChallengingAnHonestlyLegalMoveDoesNothing()
    {
        var game = CreateGame(new DurakOptions(false, ThrowInMode.All, true), Attacker, Defender, Bystander);
        Assert.True(game.TryApplyMove(0, DurakMove.Attack(new Card(Rank.Six, Suit.Clubs)), out var e1), e1);

        // J♣ на самом деле честно бьёт 6♣ — назвали обманом карту, которая им не была.
        var honestCard = new Card(Rank.Jack, Suit.Clubs);
        Assert.True(game.TryApplyMove(1, DurakMove.CheatDefend(honestCard, 0), out var e2), e2);

        var applied = game.TryChallenge(0, 0, isAttackCard: false, out var error);

        Assert.False(applied);
        Assert.NotNull(error);
        Assert.True(game.Table[0].IsDefended);
        Assert.DoesNotContain(honestCard, game.Players[1].Hand);
    }

    [Fact]
    public void Cheating_CheatAttack_CanBeChallengedAndReturnsCard()
    {
        var game = CreateGame(new DurakOptions(false, ThrowInMode.All, true), Attacker, Defender, Bystander);
        Assert.True(game.TryApplyMove(0, DurakMove.Attack(new Card(Rank.Six, Suit.Clubs)), out var e1), e1);

        var fakeAttack = new Card(Rank.Queen, Suit.Hearts);
        Assert.True(game.TryApplyMove(2, DurakMove.CheatAttack(fakeAttack), out var e2), e2);

        Assert.Equal(2, game.Table.Count);
        Assert.True(game.HasChallengeableMove);
        Assert.True(game.ChallengeableIsAttackCard);
        Assert.Equal(1, game.ChallengeableSlot);

        Assert.True(game.TryChallenge(0, 1, isAttackCard: true, out var e3), e3);

        Assert.Single(game.Table);
        Assert.Contains(fakeAttack, game.Players[2].Hand);
    }

    [Fact]
    public void Cheating_WindowClosesForeverOnceNextMoveHappens()
    {
        // Ни шестёрки (масть атаки), ни семёрки (ранг фальшивого отбоя) — иначе сторонний
        // игрок сам мог бы ещё подкинуть, и раздача не закрылась бы этим же пасом.
        var bystanderWithNoMatch = new List<Card>
        {
            new Card(Rank.Eight, Suit.Hearts), new Card(Rank.Nine, Suit.Hearts),
            new Card(Rank.Ten, Suit.Hearts), new Card(Rank.Jack, Suit.Hearts),
            new Card(Rank.Queen, Suit.Hearts), new Card(Rank.King, Suit.Hearts)
        };

        var game = CreateGame(new DurakOptions(false, ThrowInMode.All, true), Attacker, Defender, bystanderWithNoMatch);
        Assert.True(game.TryApplyMove(0, DurakMove.Attack(new Card(Rank.Six, Suit.Clubs)), out var e1), e1);
        Assert.True(game.TryApplyMove(1, DurakMove.CheatDefend(new Card(Rank.Seven, Suit.Diamonds), 0), out var e2), e2);

        Assert.True(game.HasChallengeableMove);

        // Никто не успел нажать — атакующий пасует (у него и у стороннего игрока больше
        // нет карт для подкидывания), раздача закрывается сама, карта уходит в биту навсегда.
        Assert.True(game.TryApplyMove(0, DurakMove.Pass(), out var e3), e3);

        Assert.False(game.HasChallengeableMove);
        Assert.Equal(2, game.DiscardCount);
        var applied = game.TryChallenge(2, 0, isAttackCard: false, out var error);
        Assert.False(applied);
        Assert.NotNull(error);
    }

    [Fact]
    public void FullBotGames_WithAllOptionsOn_FinishWithoutDeadlock()
    {
        var options = new DurakOptions(true, ThrowInMode.NeighboursOnly, true);

        foreach (var playerCount in new[] { 2, 3, 4, 6 })
        {
            var names = Enumerable.Range(0, playerCount).Select(i => "P" + i).ToList();

            for (var seed = 0; seed < 40; seed++)
            {
                var game = DurakGame.Create(names, Deck.Standard36(seed), options);
                PlayToCompletion(game);

                Assert.True(game.IsFinished, $"Партия не завершилась: seed {seed}, игроков {playerCount}");
            }
        }
    }

    private static void PlayToCompletion(DurakGame game)
    {
        var bot = new SimpleDurakBot();
        var guard = 8000;

        while (!game.IsFinished && guard-- > 0)
        {
            var actor = DurakTurnOrder.GetActingPlayer(game);
            Assert.NotNull(actor);

            var move = bot.ChooseMove(game, actor!.Value);
            Assert.NotNull(move);

            Assert.True(game.TryApplyMove(actor.Value, move!.Value, out var error), error);
        }

        Assert.True(guard > 0, "Партия зациклилась.");
    }
}
