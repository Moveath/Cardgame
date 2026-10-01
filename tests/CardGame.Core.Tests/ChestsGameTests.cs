using CardGame.Bots;
using CardGame.Core.Cards;
using CardGame.Core.Games.Chests;
using Xunit;

namespace CardGame.Core.Tests;

public class ChestsGameTests
{
    private static readonly string[] Three = { "Егор", "Марат", "Данил" };

    /// <summary>
    /// Раздача идёт по кругу, поэтому карты в колоде лежат вперемешку по местам. Руки
    /// должны идти от длинной к короткой и отличаться не больше чем на карту — иначе
    /// лишняя карта уедет не тому игроку.
    /// </summary>
    private static Deck BuildDeck(params Card[][] hands)
    {
        for (var i = 1; i < hands.Length; i++)
        {
            Assert.True(hands[i].Length <= hands[i - 1].Length, "Руки должны идти от длинной к короткой.");
            Assert.True(hands[0].Length - hands[i].Length <= 1, "Руки не могут отличаться больше чем на карту.");
        }

        var cards = new List<Card>();
        for (var round = 0; round < hands[0].Length; round++)
        {
            foreach (var hand in hands)
            {
                if (round < hand.Length)
                {
                    cards.Add(hand[round]);
                }
            }
        }

        return new Deck(cards);
    }

    private static Card C(Rank rank, Suit suit) => new(rank, suit);

    private static void Apply(ChestsGame game, int player, ChestsMove move)
    {
        Assert.True(game.TryApplyMove(player, move, out var error), error);
    }

    private static ChestsLogEntry Last(ChestsGame game) => game.Log[game.Log.Count - 1];

    /// <summary>Егор — вальт и дама, у Марата три вальта, у Данила три дамы; короли поровну.</summary>
    private static ChestsGame ThreeJacksAtMarat() => ChestsGame.Create(Three, BuildDeck(
        new[] { C(Rank.Jack, Suit.Clubs), C(Rank.Queen, Suit.Clubs), C(Rank.King, Suit.Clubs), C(Rank.King, Suit.Diamonds) },
        new[] { C(Rank.Jack, Suit.Diamonds), C(Rank.Jack, Suit.Hearts), C(Rank.Jack, Suit.Spades), C(Rank.King, Suit.Hearts) },
        new[] { C(Rank.Queen, Suit.Diamonds), C(Rank.Queen, Suit.Hearts), C(Rank.Queen, Suit.Spades), C(Rank.King, Suit.Spades) }));

    /// <summary>У Егора один вальт, у Марата два, у Данила один: масти придётся угадывать.</summary>
    private static ChestsGame TwoJacksAtMarat() => ChestsGame.Create(Three, BuildDeck(
        new[] { C(Rank.Jack, Suit.Clubs), C(Rank.Queen, Suit.Clubs), C(Rank.King, Suit.Clubs), C(Rank.King, Suit.Diamonds) },
        new[] { C(Rank.Jack, Suit.Diamonds), C(Rank.Jack, Suit.Hearts), C(Rank.Queen, Suit.Diamonds), C(Rank.King, Suit.Hearts) },
        new[] { C(Rank.Jack, Suit.Spades), C(Rank.Queen, Suit.Hearts), C(Rank.Queen, Suit.Spades), C(Rank.King, Suit.Spades) }));

    // ---------- раздача ----------

    [Fact]
    public void Deal_SplitsTheWholeDeck()
    {
        var game = ChestsGame.Create(Three, Deck.Standard36(7));

        var cards = 0;
        foreach (var player in game.Players)
        {
            cards += player.Hand.Count + player.ChestCount * ChestsRules.ChestSize;
        }

        Assert.Equal(36, cards);
        Assert.Equal(9, game.ChestsTotal);
        Assert.Equal(ChestsPhase.Asking, game.Phase);
        Assert.Equal(0, game.CurrentPlayerIndex);
    }

    [Fact]
    public void Deal_FourOfAKindBecomesAChestRightAway()
    {
        var game = ChestsGame.Create(Three, BuildDeck(
            new[] { C(Rank.Jack, Suit.Clubs), C(Rank.Jack, Suit.Diamonds), C(Rank.Jack, Suit.Hearts), C(Rank.Jack, Suit.Spades) },
            new[] { C(Rank.Queen, Suit.Clubs), C(Rank.Queen, Suit.Diamonds), C(Rank.King, Suit.Clubs), C(Rank.King, Suit.Diamonds) },
            new[] { C(Rank.Queen, Suit.Hearts), C(Rank.Queen, Suit.Spades), C(Rank.King, Suit.Hearts), C(Rank.King, Suit.Spades) }));

        var egor = game.Players[0];
        Assert.Equal(new[] { Rank.Jack }, egor.Chests);
        Assert.True(egor.HasEmptyHand);
        Assert.Equal(new[] { Rank.Jack }, game.CollectedRanks);

        var chest = game.Log[0];
        Assert.Equal(ChestsLogKind.Chest, chest.Kind);
        Assert.True(chest.AtDeal);
        Assert.Equal(4, chest.Cards.Count);

        // Пустая рука не выбывает: ход по-прежнему у Егора.
        Assert.False(game.IsFinished);
        Assert.Equal(0, game.CurrentPlayerIndex);
    }

    [Fact]
    public void Deck_WithJokersOrBrokenRanks_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => ChestsGame.Create(Three, Deck.Standard54(1)));

        Assert.Throws<ArgumentException>(() => ChestsGame.Create(Three, BuildDeck(
            new[] { C(Rank.Jack, Suit.Clubs) },
            new[] { C(Rank.Jack, Suit.Diamonds) },
            new[] { C(Rank.Jack, Suit.Hearts) })));
    }

    /// <summary>Вдвоём не играют: всё, чего нет у тебя, лежит у соперника, и угадывать нечего.</summary>
    [Fact]
    public void TwoPlayers_AreNotEnough()
    {
        Assert.Throws<ArgumentException>(() => ChestsGame.Create(new[] { "Егор", "Марат" }, Deck.Standard36(1)));
    }

    // ---------- вопрос о ранге ----------

    [Fact]
    public void Ask_OnlyRanksFromOwnHand()
    {
        var game = ThreeJacksAtMarat();

        Assert.Equal(new[] { Rank.Jack, Rank.Queen, Rank.King }, game.AskableRanks(0));
        Assert.Equal(new[] { 1, 2 }, game.AvailableTargets(0));

        // Тузов нет у самого Егора — такой вопрос не задать. Себя тоже не спросишь.
        Assert.False(game.TryApplyMove(0, ChestsMove.Ask(1, Rank.Ace), out var error));
        Assert.NotNull(error);
        Assert.False(game.TryApplyMove(0, ChestsMove.Ask(0, Rank.Jack), out _));
        Assert.Empty(game.Log);
    }

    [Fact]
    public void Ask_WhenTargetHasNone_TurnGoesToTheNextAfterTheAsker()
    {
        var game = ThreeJacksAtMarat();

        // Спросили Данила, через одного, — а ходит после этого Марат, следующий за Егором.
        Apply(game, 0, ChestsMove.Ask(2, Rank.Jack));

        var entry = Last(game);
        Assert.Equal(ChestsLogKind.Ask, entry.Kind);
        Assert.False(entry.Success);
        Assert.Equal(1, game.CurrentPlayerIndex);
        Assert.Equal(ChestsPhase.Asking, game.Phase);
        Assert.Null(game.TargetIndex);
    }

    [Fact]
    public void MovesOutOfTurnOrOutOfStep_AreRejected()
    {
        var game = ThreeJacksAtMarat();

        Assert.False(game.TryApplyMove(1, ChestsMove.Ask(0, Rank.Jack), out _));
        Assert.False(game.TryApplyMove(0, ChestsMove.GuessCount(1), out _));
        Assert.False(game.TryApplyMove(0, ChestsMove.GuessSuits(Suit.Hearts), out _));

        Apply(game, 0, ChestsMove.Ask(1, Rank.Jack));
        Assert.False(game.TryApplyMove(0, ChestsMove.Ask(2, Rank.Queen), out _));
        Assert.Equal(ChestsPhase.GuessingCount, game.Phase);
    }

    // ---------- количество ----------

    [Fact]
    public void CountOptions_LeaveOnlyWhatIsPossible()
    {
        Assert.Equal(new[] { 1, 2, 3 }, ChestsRules.CountOptions(0));
        Assert.Equal(new[] { 1, 2, 3 }, ChestsRules.CountOptions(1));
        Assert.Equal(new[] { 1, 2 }, ChestsRules.CountOptions(2));
        Assert.Equal(new[] { 1 }, ChestsRules.CountOptions(3));
    }

    [Fact]
    public void WrongCount_PassesTheTurn_AndDoesNotTellTheRightOne()
    {
        var game = ThreeJacksAtMarat();
        Apply(game, 0, ChestsMove.Ask(1, Rank.Jack));
        Assert.Equal(new[] { 1, 2, 3 }, game.CountOptions(0));

        Assert.False(game.TryApplyMove(0, ChestsMove.GuessCount(4), out _));

        Apply(game, 0, ChestsMove.GuessCount(2));

        var entry = Last(game);
        Assert.Equal(ChestsLogKind.CountGuess, entry.Kind);
        Assert.False(entry.Success);
        Assert.Equal(2, entry.Count);
        Assert.Empty(entry.Cards);
        Assert.DoesNotContain("три", entry.Text);

        Assert.Equal(1, game.CurrentPlayerIndex);
        Assert.Equal(3, game.Players[1].CountOf(Rank.Jack));
    }

    [Fact]
    public void RightCount_ThatCompletesFour_SkipsTheSuits()
    {
        var game = ThreeJacksAtMarat();
        Apply(game, 0, ChestsMove.Ask(1, Rank.Jack));
        Apply(game, 0, ChestsMove.GuessCount(3));

        var egor = game.Players[0];
        Assert.Equal(new[] { Rank.Jack }, egor.Chests);
        Assert.Equal(0, egor.CountOf(Rank.Jack));
        Assert.Equal(0, game.Players[1].CountOf(Rank.Jack));

        var transfer = game.Log[game.Log.Count - 2];
        Assert.Equal(ChestsLogKind.Transfer, transfer.Kind);
        Assert.Equal("Марат отдаёт Егору трёх вальтов", transfer.Text);

        var chest = Last(game);
        Assert.Equal(ChestsLogKind.Chest, chest.Kind);
        Assert.False(chest.AtDeal);
        Assert.Equal("Егору — сундучок: вальты", chest.Text);

        // Угадал — спрашивает снова.
        Assert.Equal(0, game.CurrentPlayerIndex);
        Assert.Equal(ChestsPhase.Asking, game.Phase);
    }

    /// <summary>
    /// Три вальта на руке — у Марата может быть только последний. Число и масти уже
    /// известны, поэтому после «есть» он уходит сразу, без вопроса «один валет?».
    /// </summary>
    [Fact]
    public void Ask_WithThreeOfTheRank_TakesTheLastOneRightAway()
    {
        var game = ChestsGame.Create(Three, BuildDeck(
            new[] { C(Rank.Jack, Suit.Clubs), C(Rank.Jack, Suit.Diamonds), C(Rank.Jack, Suit.Hearts), C(Rank.King, Suit.Clubs) },
            new[] { C(Rank.Queen, Suit.Clubs), C(Rank.Queen, Suit.Diamonds), C(Rank.Jack, Suit.Spades), C(Rank.King, Suit.Diamonds) },
            new[] { C(Rank.Queen, Suit.Hearts), C(Rank.Queen, Suit.Spades), C(Rank.King, Suit.Hearts), C(Rank.King, Suit.Spades) }));

        Apply(game, 0, ChestsMove.Ask(1, Rank.Jack));

        Assert.Equal(new[] { Rank.Jack }, game.Players[0].Chests);
        Assert.Equal(0, game.Players[1].CountOf(Rank.Jack));
        Assert.Equal(0, game.CurrentPlayerIndex);
        Assert.Equal(ChestsPhase.Asking, game.Phase);

        var kinds = game.Log.Select(entry => entry.Kind).ToArray();
        Assert.Equal(new[] { ChestsLogKind.Ask, ChestsLogKind.Transfer, ChestsLogKind.Chest }, kinds);
        Assert.Equal("Марат отдаёт Егору одного вальта", game.Log[1].Text);
    }

    /// <summary>А если последнего у адресата нет — обычное «нет», и ход уходит дальше.</summary>
    [Fact]
    public void Ask_WithThreeOfTheRank_StillMissesOnNo()
    {
        var game = ChestsGame.Create(Three, BuildDeck(
            new[] { C(Rank.Jack, Suit.Clubs), C(Rank.Jack, Suit.Diamonds), C(Rank.Jack, Suit.Hearts), C(Rank.King, Suit.Clubs) },
            new[] { C(Rank.Queen, Suit.Clubs), C(Rank.Queen, Suit.Diamonds), C(Rank.Jack, Suit.Spades), C(Rank.King, Suit.Diamonds) },
            new[] { C(Rank.Queen, Suit.Hearts), C(Rank.Queen, Suit.Spades), C(Rank.King, Suit.Hearts), C(Rank.King, Suit.Spades) }));

        Apply(game, 0, ChestsMove.Ask(2, Rank.Jack));

        Assert.False(game.Log[0].Success);
        Assert.Empty(game.Players[0].Chests);
        Assert.Equal(1, game.CurrentPlayerIndex);
    }

    // ---------- масти ----------

    [Fact]
    public void Suits_PartialMatchIsAMiss()
    {
        var game = TwoJacksAtMarat();
        Apply(game, 0, ChestsMove.Ask(1, Rank.Jack));
        Apply(game, 0, ChestsMove.GuessCount(2));
        Assert.Equal(ChestsPhase.GuessingSuits, game.Phase);
        Assert.Equal(2, game.GuessedCount);

        // Бубны угаданы, пики — нет: половина набора ничего не даёт и не подсказывается.
        Apply(game, 0, ChestsMove.GuessSuits(Suit.Diamonds, Suit.Spades));

        var entry = Last(game);
        Assert.Equal(ChestsLogKind.SuitsGuess, entry.Kind);
        Assert.False(entry.Success);
        Assert.Empty(entry.Cards);
        Assert.Equal(1, game.CurrentPlayerIndex);
        Assert.Equal(2, game.Players[1].CountOf(Rank.Jack));
    }

    [Fact]
    public void Suits_FullMatchTakesTheCards_AndKeepsTheTurn()
    {
        var game = TwoJacksAtMarat();
        Apply(game, 0, ChestsMove.Ask(1, Rank.Jack));
        Apply(game, 0, ChestsMove.GuessCount(2));
        Apply(game, 0, ChestsMove.GuessSuits(Suit.Hearts, Suit.Diamonds));

        Assert.Equal(3, game.Players[0].CountOf(Rank.Jack));
        Assert.Equal(0, game.Players[1].CountOf(Rank.Jack));
        Assert.Equal(ChestsLogKind.Transfer, Last(game).Kind);
        Assert.Equal(2, Last(game).Cards.Count);
        Assert.Equal(0, game.CurrentPlayerIndex);
        Assert.Equal(ChestsPhase.Asking, game.Phase);
    }

    [Fact]
    public void Suits_NamingYourOwnSuitIsLegal_ButMisses()
    {
        var game = TwoJacksAtMarat();
        Apply(game, 0, ChestsMove.Ask(1, Rank.Jack));
        Apply(game, 0, ChestsMove.GuessCount(2));

        // Трефовый вальт у Егора на руке: назвать его можно, чтобы запутать стол.
        Apply(game, 0, ChestsMove.GuessSuits(Suit.Clubs, Suit.Hearts));

        Assert.False(Last(game).Success);
        Assert.Equal(1, game.CurrentPlayerIndex);
    }

    [Fact]
    public void Suits_WrongSizeOrRepeats_AreRejectedNotMissed()
    {
        var game = TwoJacksAtMarat();
        Apply(game, 0, ChestsMove.Ask(1, Rank.Jack));
        Apply(game, 0, ChestsMove.GuessCount(2));

        Assert.False(game.TryApplyMove(0, ChestsMove.GuessSuits(Suit.Hearts), out _));
        Assert.False(game.TryApplyMove(0, ChestsMove.GuessSuits(Suit.Hearts, Suit.Hearts), out _));
        Assert.Equal(ChestsPhase.GuessingSuits, game.Phase);
        Assert.Equal(0, game.CurrentPlayerIndex);
    }

    // ---------- пустая рука ----------

    [Fact]
    public void EmptyHand_AsksAnyOpenRank_AndIsAnsweredNo()
    {
        var game = ChestsGame.Create(Three, BuildDeck(
            new[] { C(Rank.Jack, Suit.Clubs), C(Rank.Jack, Suit.Diamonds), C(Rank.Jack, Suit.Hearts), C(Rank.Jack, Suit.Spades) },
            new[] { C(Rank.Queen, Suit.Clubs), C(Rank.Queen, Suit.Diamonds), C(Rank.King, Suit.Clubs), C(Rank.King, Suit.Diamonds) },
            new[] { C(Rank.Queen, Suit.Hearts), C(Rank.Queen, Suit.Spades), C(Rank.King, Suit.Hearts), C(Rank.King, Suit.Spades) }));

        // Вальты уже в сундучке, остальное спрашивать можно — хоть карт и нет.
        Assert.Equal(new[] { Rank.Queen, Rank.King }, game.AskableRanks(0));
        Assert.False(game.TryApplyMove(0, ChestsMove.Ask(1, Rank.Jack), out _));

        Apply(game, 0, ChestsMove.Ask(1, Rank.Queen));
        Assert.Equal(new[] { 1, 2, 3 }, game.CountOptions(0));
        Apply(game, 0, ChestsMove.GuessCount(1));
        Assert.Equal(1, game.CurrentPlayerIndex);

        // Марат спрашивает Егора, у которого пусто: обычное «нет».
        Apply(game, 1, ChestsMove.Ask(0, Rank.King));
        Assert.False(Last(game).Success);
        Assert.Equal(2, game.CurrentPlayerIndex);

        // Данил промахивается — и очередь снова доходит до Егора, пустая рука её не пропускает.
        Apply(game, 2, ChestsMove.Ask(1, Rank.Queen));
        Apply(game, 2, ChestsMove.GuessCount(1));
        Assert.Equal(0, game.CurrentPlayerIndex);
        Assert.Equal(new[] { 1, 2 }, game.AvailableTargets(0));
    }

    // ---------- финал ----------

    [Fact]
    public void Game_EndsWhenEveryChestIsCollected()
    {
        var game = ChestsGame.Create(Three, BuildDeck(
            new[] { C(Rank.Jack, Suit.Clubs), C(Rank.Jack, Suit.Diamonds), C(Rank.Jack, Suit.Hearts) },
            new[] { C(Rank.Jack, Suit.Spades), C(Rank.Queen, Suit.Clubs), C(Rank.Queen, Suit.Diamonds) },
            new[] { C(Rank.Queen, Suit.Hearts), C(Rank.Queen, Suit.Spades) }));

        Apply(game, 0, ChestsMove.Ask(1, Rank.Jack));
        Apply(game, 0, ChestsMove.Ask(1, Rank.Queen));
        Apply(game, 0, ChestsMove.GuessCount(2));
        Apply(game, 0, ChestsMove.GuessSuits(Suit.Clubs, Suit.Diamonds));
        Apply(game, 0, ChestsMove.Ask(2, Rank.Queen));
        Assert.Equal(new[] { 1, 2 }, game.CountOptions(0));
        Apply(game, 0, ChestsMove.GuessCount(2));

        Assert.True(game.IsFinished);
        Assert.False(game.IsDraw);
        Assert.Equal(new[] { 0 }, game.Winners);
        Assert.Equal(new[] { 1, 2 }, game.Losers);
        Assert.Equal(new[] { 0, 1, 2 }, game.Standings);
        Assert.Equal(ChestsLogKind.GameOver, Last(game).Kind);
        Assert.Equal("Все сундучки собраны. Победитель — Егор: 2 сундучка. Меньше всех — Марат, Данил: по 0 сундучков.",
            Last(game).Text);
    }

    /// <summary>Каждому пришло по каре прямо на раздаче — партия кончается, не начавшись, и это ничья.</summary>
    [Fact]
    public void Game_WithEqualChests_IsADraw()
    {
        var game = ChestsGame.Create(Three, BuildDeck(
            new[] { C(Rank.Jack, Suit.Clubs), C(Rank.Jack, Suit.Diamonds), C(Rank.Jack, Suit.Hearts), C(Rank.Jack, Suit.Spades) },
            new[] { C(Rank.Queen, Suit.Clubs), C(Rank.Queen, Suit.Diamonds), C(Rank.Queen, Suit.Hearts), C(Rank.Queen, Suit.Spades) },
            new[] { C(Rank.King, Suit.Clubs), C(Rank.King, Suit.Diamonds), C(Rank.King, Suit.Hearts), C(Rank.King, Suit.Spades) }));

        Assert.True(game.IsFinished);
        Assert.True(game.IsDraw);
        Assert.Empty(game.Winners);
        Assert.Empty(game.Losers);
        Assert.Equal("Все сундучки собраны. Ничья: у всех по 1 сундучок.", Last(game).Text);
    }

    [Fact]
    public void Game_PartialTie_HasSeveralWinners()
    {
        // Двое собирают по сундучку, третий — ни одного.
        var game = ChestsGame.Create(Three, BuildDeck(
            new[] { C(Rank.Jack, Suit.Clubs), C(Rank.Jack, Suit.Diamonds), C(Rank.Jack, Suit.Hearts) },
            new[] { C(Rank.Jack, Suit.Spades), C(Rank.Queen, Suit.Clubs), C(Rank.Queen, Suit.Diamonds) },
            new[] { C(Rank.Queen, Suit.Hearts), C(Rank.Queen, Suit.Spades) }));

        Apply(game, 0, ChestsMove.Ask(1, Rank.Jack));
        Assert.True(game.Players[0].HasEmptyHand);

        Apply(game, 0, ChestsMove.Ask(2, Rank.Queen));
        Apply(game, 0, ChestsMove.GuessCount(1));
        Assert.Equal(1, game.CurrentPlayerIndex);

        Apply(game, 1, ChestsMove.Ask(2, Rank.Queen));
        Apply(game, 1, ChestsMove.GuessCount(2));

        Assert.True(game.IsFinished);
        Assert.Equal(new[] { 0, 1 }, game.Winners);
        Assert.Equal(new[] { 2 }, game.Losers);
        Assert.Contains("Победители", Last(game).Text);
    }

    // ---------- слова ----------

    [Fact]
    public void Words_AgreeWithNumbers()
    {
        Assert.Equal("сундучок", ChestsRules.ChestsWord(1));
        Assert.Equal("сундучка", ChestsRules.ChestsWord(3));
        Assert.Equal("сундучков", ChestsRules.ChestsWord(5));
        Assert.Equal("сундучков", ChestsRules.ChestsWord(11));
        Assert.Equal("сундучок", ChestsRules.ChestsWord(21));

        Assert.Equal("один валет", ChestsRules.CountPhrase(Rank.Jack, 1));
        Assert.Equal("два вальта", ChestsRules.CountPhrase(Rank.Jack, 2));
        Assert.Equal("одна дама", ChestsRules.CountPhrase(Rank.Queen, 1));
        Assert.Equal("три восьмёрки", ChestsRules.CountPhrase(Rank.Eight, 3));

        Assert.Equal("одного короля", ChestsRules.CardsAccusative(Rank.King, 1));
        Assert.Equal("двух вальтов", ChestsRules.CardsAccusative(Rank.Jack, 2));
        Assert.Equal("одну даму", ChestsRules.CardsAccusative(Rank.Queen, 1));
        Assert.Equal("две шестёрки", ChestsRules.CardsAccusative(Rank.Six, 2));

        Assert.Equal("пики", ChestsRules.SuitsPhrase(new[] { Suit.Spades }));
        Assert.Equal("черви и пики", ChestsRules.SuitsPhrase(new[] { Suit.Spades, Suit.Hearts }));
        Assert.Equal("крести, буби и черви", ChestsRules.SuitsPhrase(new[] { Suit.Hearts, Suit.Clubs, Suit.Diamonds }));
    }

    // ---------- прогон ботами ----------

    /// <summary>
    /// Человек жмёт что попало из того, что ему предлагает стол: любого соперника, любой
    /// ранг из полосы, любое число, любые масти, хоть свои. Каждое такое нажатие должно
    /// быть законным ходом, а партия — доходить до конца.
    /// </summary>
    [Fact]
    public void AnythingTheTableOffers_IsALegalMove()
    {
        var names = new[] { "Егор", "Марат", "Данил", "Вера" };
        for (var seed = 0; seed < 40; seed++)
        {
            var game = ChestsGame.Create(names, Deck.Standard36(seed));
            var bot = new SimpleChestsBot(new Random(seed));
            var clicks = new Random(seed + 1000);

            var moves = 0;
            while (!game.IsFinished && moves < 20000)
            {
                var actor = game.CurrentPlayerIndex;
                var move = actor == 0 ? RandomClick(game, clicks) : bot.ChooseMove(game, actor)!;
                Assert.True(game.TryApplyMove(actor, move, out var error), error);
                moves++;
            }

            Assert.True(game.IsFinished, "seed " + seed);
        }
    }

    private static ChestsMove RandomClick(ChestsGame game, Random clicks)
    {
        switch (game.Phase)
        {
            case ChestsPhase.Asking:
                var targets = game.AvailableTargets(0);
                var ranks = game.AskableRanks(0);
                Assert.NotEmpty(targets);
                Assert.NotEmpty(ranks);
                return ChestsMove.Ask(targets[clicks.Next(targets.Count)], ranks[clicks.Next(ranks.Count)]);

            case ChestsPhase.GuessingCount:
                var options = game.CountOptions(0);
                Assert.NotEmpty(options);
                return ChestsMove.GuessCount(options[clicks.Next(options.Count)]);

            default:
                var suits = new List<Suit> { Suit.Clubs, Suit.Diamonds, Suit.Hearts, Suit.Spades };
                var picked = new List<Suit>();
                while (picked.Count < game.GuessedCount)
                {
                    var suit = suits[clicks.Next(suits.Count)];
                    suits.Remove(suit);
                    picked.Add(suit);
                }

                return ChestsMove.GuessSuits(picked);
        }
    }

    /// <summary>
    /// Партия ботов всегда доходит до конца, каждый их ход законен, и в итоге все ранги
    /// лежат в сундучках. Ходов у бота бесконечно много не бывает: память у него неполная,
    /// но каждый вопрос хоть с каким-то шансом удачен.
    /// </summary>
    [Theory]
    [InlineData(3, 24)]
    [InlineData(3, 36)]
    [InlineData(4, 36)]
    [InlineData(5, 52)]
    [InlineData(8, 36)]
    [InlineData(6, 24)]
    public void BotGames_AlwaysFinish(int players, int deckSize)
    {
        var names = new List<string>();
        for (var i = 0; i < players; i++)
        {
            names.Add("Бот " + (i + 1));
        }

        for (var seed = 0; seed < 25; seed++)
        {
            var deck = deckSize switch
            {
                24 => Deck.Standard24(seed),
                36 => Deck.Standard36(seed),
                _ => Deck.Standard52(seed)
            };

            var game = ChestsGame.Create(names, deck);
            var bot = new SimpleChestsBot(new Random(seed));

            var moves = 0;
            while (!game.IsFinished && moves < 20000)
            {
                var actor = game.CurrentPlayerIndex;
                var move = bot.ChooseMove(game, actor);
                Assert.NotNull(move);
                Assert.True(game.TryApplyMove(actor, move!, out var error), error);
                moves++;
            }

            Assert.True(game.IsFinished, "seed " + seed + ": партия не закончилась за " + moves + " ходов");

            var chests = 0;
            foreach (var player in game.Players)
            {
                Assert.True(player.HasEmptyHand);
                chests += player.ChestCount;
            }

            Assert.Equal(game.ChestsTotal, chests);
            Assert.Equal(game.ChestsTotal, game.CollectedRanks.Count);
        }
    }
}
