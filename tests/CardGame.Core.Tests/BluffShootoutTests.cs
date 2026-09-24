using CardGame.Core.Cards;
using CardGame.Core.Games.Bluff;
using Xunit;

namespace CardGame.Core.Tests;

/// <summary>
/// Перестрелка — домашнее правило поверх обычного круга. Правила бросков в ней те же
/// самые, и проверять в ней не запрещено: меняется только цена проверки. Тот, кто
/// оборвал перестрелку, получает петуха на два круга — ни очков, ни карт это не
/// стоит, и в этом весь смысл. Тесты стерегут границу: перестрелка не должна начать
/// влиять на сами правила.
/// </summary>
public class BluffShootoutTests
{
    private static readonly string[] Three = { "Егор", "Марат", "Данил" };
    private static readonly string[] Two = { "Егор", "Марат" };

    private static BluffOptions Shootouts => new(3, true, false, true);

    /// <summary>
    /// Предложение приходит от самой раздачи, поэтому «удобной» колоды под рукой нет:
    /// перебираем зёрна, пока не попадётся партия, где ведущему предлагают сразу.
    /// </summary>
    private static int SeedWithOfferOnTheFirstRound(IReadOnlyList<string> names)
    {
        for (var seed = 0; seed < 500; seed++)
        {
            if (BluffGame.Create(names, Deck.Standard36(seed), Shootouts).ShootoutOffered)
            {
                return seed;
            }
        }

        Assert.Fail("Ни на одной из 500 раздач перестрелку не предложили.");
        return 0;
    }

    /// <summary>Любая законная заявка текущего игрока — содержимое здесь не важно.</summary>
    private static BluffMove SomeDeclaration(BluffGame game, bool openShootout)
    {
        var hand = game.Players[game.CurrentPlayerIndex].Hand;
        var card = hand[0];
        var rank = Rank.King;

        foreach (var candidate in hand)
        {
            if (BluffRules.CanBeDeclared(candidate.Rank))
            {
                card = candidate;
                rank = candidate.Rank;
                break;
            }
        }

        // Если на руках одни тузы, заявиться ими нельзя — но подложить их под чужим
        // рангом можно, в этом вся их роль. Тогда ходим тузом под видом королей.
        return openShootout
            ? BluffMove.DeclareShootout(rank, card)
            : BluffMove.Declare(rank, card);
    }

    private static void Apply(BluffGame game, int player, BluffMove move)
    {
        Assert.True(game.TryApplyMove(player, move, out var error), error);
    }

    /// <summary>Круг из двух ходов: ведущий кладёт карту, сосед сразу вскрывает.</summary>
    private static void PlayWholeRound(BluffGame game, bool openShootout)
    {
        var leader = game.CurrentPlayerIndex;
        Apply(game, leader, SomeDeclaration(game, openShootout));
        Apply(game, game.CurrentPlayerIndex, BluffMove.Challenge());
    }

    // ---------- предложение ----------

    [Fact]
    public void Shootout_IsNeverOfferedWhenTheTableDidNotAgreeToIt()
    {
        for (var seed = 0; seed < 60; seed++)
        {
            var game = BluffGame.Create(Two, Deck.Standard36(seed), BluffOptions.Classic);

            for (var round = 0; round < 6 && !game.IsFinished; round++)
            {
                Assert.False(game.ShootoutOffered);
                PlayWholeRound(game, openShootout: false);
            }
        }
    }

    /// <summary>
    /// Примерно раз в два круга — но именно примерно. Пауза после состоявшейся
    /// перестрелки даёт один круг гарантированной тишины, кубик добавляет второй; если
    /// сделать предложение постоянным, перестрелка перестанет быть событием.
    /// </summary>
    [Fact]
    public void Shootout_IsOfferedOnMostDealsButNotAll()
    {
        var offered = 0;
        for (var seed = 0; seed < 300; seed++)
        {
            if (BluffGame.Create(Three, Deck.Standard36(seed), Shootouts).ShootoutOffered)
            {
                offered++;
            }
        }

        Assert.InRange(offered, 195, 285);
    }

    /// <summary>Одна и та же колода — одна и та же партия, включая момент перестрелки.</summary>
    [Fact]
    public void Shootout_OfferIsDecidedByTheDealAndNothingElse()
    {
        for (var seed = 0; seed < 30; seed++)
        {
            var first = BluffGame.Create(Three, Deck.Standard36(seed), Shootouts);
            var second = BluffGame.Create(Three, Deck.Standard36(seed), Shootouts);

            Assert.Equal(first.ShootoutOffered, second.ShootoutOffered);
        }
    }

    [Fact]
    public void Shootout_CannotBeOpenedWhenItWasNotOffered()
    {
        var game = BluffGame.Create(Two, Deck.Standard36(SeedWithOfferOnTheFirstRound(Two)), BluffOptions.Classic);

        Assert.False(game.ShootoutOffered);
        Assert.False(game.TryApplyMove(0, SomeDeclaration(game, openShootout: true), out var error));
        Assert.Equal("Перестрелку сейчас не объявляют.", error);
    }

    /// <summary>Не воспользовался — круг пошёл обычным, и просить второй раз нечего.</summary>
    [Fact]
    public void Shootout_OfferExpiresAsSoonAsTheLeaderThrows()
    {
        var game = BluffGame.Create(Two, Deck.Standard36(SeedWithOfferOnTheFirstRound(Two)), Shootouts);
        Assert.True(game.ShootoutOffered);

        Apply(game, 0, SomeDeclaration(game, openShootout: false));

        Assert.False(game.ShootoutOffered);
        Assert.False(game.IsShootout);
    }

    /// <summary>Посреди цепочки перестрелку не объявляют: ранг уже назван, объявлять нечего.</summary>
    [Fact]
    public void Shootout_CannotBeOpenedInTheMiddleOfAChain()
    {
        var game = BluffGame.Create(Two, Deck.Standard36(SeedWithOfferOnTheFirstRound(Two)), Shootouts);
        Apply(game, 0, SomeDeclaration(game, openShootout: false));

        var card = game.Players[1].Hand[0];
        Assert.False(game.TryApplyMove(1, BluffMove.DeclareShootout(Rank.King, card), out var error));
        Assert.Equal("Ранг уже назван, менять его нельзя.", error);
    }

    // ---------- объявление ----------

    [Fact]
    public void Shootout_IsAnnouncedToEveryoneRightAfterTheCardsLand()
    {
        var game = BluffGame.Create(Three, Deck.Standard36(SeedWithOfferOnTheFirstRound(Three)), Shootouts);

        var before = game.Log.Count;
        Apply(game, 0, SomeDeclaration(game, openShootout: true));

        Assert.True(game.IsShootout);
        Assert.Equal(0, game.ShootoutStarterIndex);

        // Сначала сам бросок, потом объявление: пока карты не легли, называть нечего.
        Assert.Equal(BluffLogKind.Throw, game.Log[before].Kind);

        var announcement = game.Log[before + 1];
        Assert.Equal(BluffLogKind.Shootout, announcement.Kind);
        Assert.Equal(0, announcement.PlayerIndex);
        Assert.Contains("объявил перестрелку", announcement.Text);
        Assert.Empty(announcement.RevealedCards);
    }

    /// <summary>Объявление — это только объявление: сам бросок оно не меняет.</summary>
    [Fact]
    public void Shootout_DoesNotChangeWhatTheThrowDoes()
    {
        var seed = SeedWithOfferOnTheFirstRound(Three);

        var plain = BluffGame.Create(Three, Deck.Standard36(seed), BluffOptions.Classic);
        var loud = BluffGame.Create(Three, Deck.Standard36(seed), Shootouts);

        var card = loud.Players[0].Hand[0];
        card = BluffRules.CanBeDeclared(card.Rank) ? card : loud.Players[0].Hand[^1];

        Apply(plain, 0, BluffMove.Declare(card.Rank, card));
        Apply(loud, 0, BluffMove.DeclareShootout(card.Rank, card));

        Assert.Equal(plain.CurrentRank, loud.CurrentRank);
        Assert.Equal(plain.CurrentPlayerIndex, loud.CurrentPlayerIndex);
        Assert.Equal(plain.PotCount, loud.PotCount);
        Assert.Equal(plain.Players[0].Hand.Count, loud.Players[0].Hand.Count);
    }

    // ---------- петух ----------

    [Fact]
    public void Challenging_AShootout_PutsTheHatOnForTwoRounds()
    {
        var game = BluffGame.Create(Three, Deck.Standard36(SeedWithOfferOnTheFirstRound(Three)), Shootouts);

        Apply(game, 0, SomeDeclaration(game, openShootout: true));
        Apply(game, 1, BluffMove.Challenge());

        Assert.True(game.Players[1].IsRooster);
        Assert.Equal(1, game.Players[1].RoosterMarks);
        Assert.Equal(BluffGame.RoosterRounds, game.Players[1].RoosterRoundsLeft);
        Assert.False(game.Players[0].IsRooster);
        Assert.False(game.Players[2].IsRooster);

        var entry = game.Log[^1];
        Assert.Equal(BluffLogKind.Challenge, entry.Kind);
        Assert.True(entry.BrokeShootout);
        Assert.Contains("петух", entry.Text);

        // Круг кончился, а шапка осталась.
        Assert.False(game.IsShootout);
        Assert.Null(game.ShootoutStarterIndex);

        // Первый круг после перестрелки — ещё в шапке.
        PlayWholeRound(game, openShootout: false);
        Assert.True(game.Players[1].IsRooster);
        Assert.Equal(1, game.Players[1].RoosterRoundsLeft);

        // Второй — последний: после него шапку снимают. Счёт сорванных остаётся.
        PlayWholeRound(game, openShootout: false);
        Assert.False(game.IsFinished);
        Assert.False(game.Players[1].IsRooster);
        Assert.Equal(0, game.Players[1].RoosterRoundsLeft);
        Assert.Equal(1, game.Players[1].RoosterMarks);
    }

    /// <summary>
    /// Сорвал ещё одну перестрелку, пока шапка на голове, — срок начинается заново, а не
    /// дотикивает старый. Для этого нужно, чтобы перестрелку предложили, пока петух ещё в
    /// шапке, и вскрывать выпало именно ему, — такое бывает не на каждой раздаче, поэтому
    /// перебираем, пока не найдём.
    /// </summary>
    [Fact]
    public void BreakingAnotherShootout_WhileStillARooster_StartsTheTermAgain()
    {
        for (var seed = 0; seed < 400; seed++)
        {
            var game = BluffGame.Create(Two, Deck.Standard36(seed), Shootouts);
            if (!game.ShootoutOffered)
            {
                continue;
            }

            PlayWholeRound(game, openShootout: true);
            var rooster = game.Players[0].IsRooster ? 0 : 1;
            Assert.True(game.Players[rooster].IsRooster);

            for (var round = 0; round < BluffGame.RoosterRounds && !game.IsFinished; round++)
            {
                var leader = game.CurrentPlayerIndex;

                // Вдвоём вскрывает тот, кто не ведёт.
                if (game.ShootoutOffered && leader != rooster)
                {
                    Assert.Equal(BluffGame.RoosterRounds - round, game.Players[rooster].RoosterRoundsLeft);

                    Apply(game, leader, SomeDeclaration(game, openShootout: true));
                    Assert.Equal(rooster, game.CurrentPlayerIndex);
                    Apply(game, rooster, BluffMove.Challenge());

                    Assert.Equal(BluffGame.RoosterRounds, game.Players[rooster].RoosterRoundsLeft);
                    Assert.Equal(2, game.Players[rooster].RoosterMarks);
                    return;
                }

                PlayWholeRound(game, openShootout: false);
            }
        }

        Assert.Fail("Ни на одной из 400 раздач петуху не выпало сорвать вторую перестрелку, пока шапка на нём.");
    }

    [Fact]
    public void Challenging_AnOrdinaryRound_LeavesNoMark()
    {
        var game = BluffGame.Create(Three, Deck.Standard36(SeedWithOfferOnTheFirstRound(Three)), Shootouts);

        Apply(game, 0, SomeDeclaration(game, openShootout: false));
        Apply(game, 1, BluffMove.Challenge());

        Assert.False(game.Players[1].IsRooster);
        Assert.False(game.Log[^1].BrokeShootout);
        Assert.DoesNotContain("петух", game.Log[^1].Text);
    }

    /// <summary>Сорвал вторую перестрелку — счёт два, даже если шапку за это время уже сняли.</summary>
    [Fact]
    public void RoosterMarks_AddUp()
    {
        var game = BluffGame.Create(Two, Deck.Standard36(SeedWithOfferOnTheFirstRound(Two)), Shootouts);
        var marked = 0;

        for (var round = 0; round < 80 && marked < 2 && !game.IsFinished; round++)
        {
            var offered = game.ShootoutOffered;
            PlayWholeRound(game, offered);

            if (offered)
            {
                marked++;
            }
        }

        Assert.Equal(2, marked);

        // Вскрывает всегда сосед ведущего, а ведущими они становятся по очереди — так что
        // клеймо могло достаться любому из двоих. Важно, что счёт у него именно два.
        var total = game.Players[0].RoosterMarks + game.Players[1].RoosterMarks;
        Assert.Equal(2, total);
    }

    /// <summary>
    /// Пауза после перестрелки ровно в один круг: подряд они не идут — круг в перестрелке
    /// и так вдвое длиннее обычного, — но и ждать её долго не приходится.
    /// </summary>
    [Fact]
    public void Shootout_IsNotOfferedTwoRoundsInARow()
    {
        var cameBack = 0;
        var checked_ = 0;

        for (var seed = 0; seed < 150; seed++)
        {
            var game = BluffGame.Create(Two, Deck.Standard36(seed), Shootouts);
            if (!game.ShootoutOffered)
            {
                continue;
            }

            Apply(game, 0, SomeDeclaration(game, openShootout: true));
            Apply(game, 1, BluffMove.Challenge());
            if (game.IsFinished)
            {
                continue;
            }

            checked_++;
            Assert.False(game.ShootoutOffered, "перестрелку предложили сразу следом");

            PlayWholeRound(game, openShootout: false);
            if (game.IsFinished)
            {
                continue;
            }

            if (game.ShootoutOffered)
            {
                cameBack++;
            }
        }

        Assert.True(checked_ > 20, "подходящих партий всего " + checked_);
        Assert.True(cameBack > checked_ / 2,
            "через круг перестрелку предложили лишь " + cameBack + " раз из " + checked_);
    }

    // ---------- грамматика объявления ----------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Shootout_OffersAreSpacedTwoOrThreeRoundsApart(bool accept)
    {
        var intervals = new HashSet<int>();
        for (var seed = 0; seed < 100; seed++)
        {
            var game = BluffGame.Create(Two, Deck.Standard36(seed), Shootouts);
            int? lastOffer = null;
            for (var round = 0; round < 30 && !game.IsFinished; round++)
            {
                if (game.ShootoutOffered)
                {
                    if (lastOffer.HasValue)
                    {
                        var gap = round - lastOffer.Value;
                        Assert.InRange(gap, 2, 3);
                        intervals.Add(gap);
                    }
                    lastOffer = round;
                }
                else if (lastOffer.HasValue)
                {
                    Assert.True(round - lastOffer.Value < 3);
                }
                PlayWholeRound(game, accept && game.ShootoutOffered);
            }
        }
        Assert.Contains(2, intervals);
        Assert.Contains(3, intervals);
    }

    [Theory]
    [InlineData(Rank.Six, "перестрелку на шестёрки")]
    [InlineData(Rank.Ten, "перестрелку на десятки")]
    [InlineData(Rank.Jack, "перестрелку на вальтов")]
    [InlineData(Rank.Queen, "перестрелку на дам")]
    [InlineData(Rank.King, "перестрелку на королей")]
    public void Announcement_PutsTheRankIntoTheAccusative(Rank rank, string expected)
    {
        Assert.Equal(expected, "перестрелку на " + BluffRules.RankPluralAccusative(rank));
    }

    // ---------- сходимость ----------

    /// <summary>
    /// Перестрелка удлиняет круг, но не ломает партию: карты не теряются, игра кончается.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public void RandomGamesWithShootouts_StillFinish(int playerCount)
    {
        var names = new List<string>();
        for (var i = 0; i < playerCount; i++)
        {
            names.Add("И" + i);
        }

        var shootoutsSeen = 0;

        for (var seed = 0; seed < 60; seed++)
        {
            var game = BluffGame.Create(names, Deck.Standard36(seed), Shootouts);
            var random = new Random(seed * 17 + playerCount);

            var guard = 0;
            while (!game.IsFinished)
            {
                Assert.True(guard++ < 600, "Партия не сходится: seed " + seed);

                if (game.ShootoutOffered)
                {
                    shootoutsSeen++;
                    Apply(game, game.CurrentPlayerIndex, SomeDeclaration(game, openShootout: true));
                    continue;
                }

                PlayRandomMove(game, random);
                AssertNoCardIsLost(game, 36);
            }

            AssertNoCardIsLost(game, 36);
            Assert.Equal(playerCount, game.Standings.Count);
        }

        Assert.True(shootoutsSeen > 10, "Перестрелок за 60 партий всего " + shootoutsSeen);
    }

    private static void PlayRandomMove(BluffGame game, Random random)
    {
        var actor = game.CurrentPlayerIndex;

        if (game.Phase == BluffPhase.Chain && random.Next(3) == 0)
        {
            Apply(game, actor, BluffMove.Challenge());
            return;
        }

        var hand = game.Players[actor].Hand;
        var take = random.Next(1, Math.Min(3, hand.Count) + 1);

        var pool = new List<Card>(hand);
        var cards = new List<Card>();
        for (var i = 0; i < take; i++)
        {
            var pick = random.Next(pool.Count);
            cards.Add(pool[pick]);
            pool.RemoveAt(pick);
        }

        if (game.Phase != BluffPhase.RoundStart)
        {
            Apply(game, actor, BluffMove.Follow(cards));
            return;
        }

        var rank = Rank.King;
        foreach (var card in cards)
        {
            if (BluffRules.CanBeDeclared(card.Rank))
            {
                rank = card.Rank;
                break;
            }
        }

        Apply(game, actor, BluffMove.Declare(rank, cards));
    }

    private static void AssertNoCardIsLost(BluffGame game, int deckSize)
    {
        var total = game.PotCount + game.RemovedCount;
        foreach (var player in game.Players)
        {
            total += player.Hand.Count;
        }

        Assert.Equal(deckSize, total);
    }
}
