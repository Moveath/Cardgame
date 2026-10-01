using System.Reflection;
using CardGame.Core.Cards;
using CardGame.Core.Games.Poker;

namespace CardGame.Core.Tests;

public sealed class PokerGameTests
{
    private static PokerGame Game(int count = 6, int seed = 417, int stack = 2000, int small = 10, int big = 20)
        => new(Enumerable.Range(0, count).Select(i => "Игрок " + i).ToArray(), seed, stack, small, big);

    [Theory]
    [InlineData(2, 0, 1, 0)]
    [InlineData(3, 1, 2, 0)]
    [InlineData(6, 1, 2, 3)]
    public void BlindsAndPreflopFirstActor(int count, int sb, int bb, int actor)
    {
        var game = Game(count);
        Assert.Equal(0, game.Dealer);
        Assert.Equal(sb, game.SmallBlindPlayer);
        Assert.Equal(bb, game.BigBlindPlayer);
        Assert.Equal(actor, game.CurrentPlayer);
        Assert.Equal(30, game.Pot);
        Assert.Equal(40, game.MinimumRaiseTo);
        Assert.Equal(count * 2, game.Players.SelectMany(p => p.HoleCards).Distinct().Count());
        Assert.All(game.Players, p => Assert.Equal(2, p.HoleCards.Count));
    }

    [Fact]
    public void CheckCallRoundKeepsBigBlindOptionAndMovesThroughAllStreets()
    {
        var game = Game(3);
        Act(game, PokerAction.Call);
        Act(game, PokerAction.Call);
        Assert.Equal(2, game.CurrentPlayer);
        Assert.Equal(PokerPhase.Preflop, game.Phase);
        Assert.True(game.CanRaise(2));
        Act(game, PokerAction.Check);
        Assert.Equal(PokerPhase.Flop, game.Phase);
        Assert.Equal(3, game.Board.Count);
        Assert.Equal(1, game.CurrentPlayer);
        Assert.Equal(0, game.CurrentBet);
        Assert.Equal(20, game.MinimumRaiseTo);
        Assert.All(game.Players, p => Assert.Equal(0, p.RoundBet));
        CheckStreet(game);
        Assert.Equal(PokerPhase.Turn, game.Phase);
        CheckStreet(game);
        Assert.Equal(PokerPhase.River, game.Phase);
        CheckStreet(game);
        Assert.Equal(PokerPhase.HandComplete, game.Phase);
        Assert.Equal(-1, game.CurrentPlayer);
        Assert.Equal(60, game.Winnings.Sum());
        Assert.Equal(6000, game.Players.Sum(p => p.Stack));
        Assert.Equal(11, game.Players.SelectMany(p => p.HoleCards).Concat(game.Board).Distinct().Count());
    }

    [Fact]
    public void HeadsUpDealerActsFirstPreflopAndLastPostflop()
    {
        var game = Game(2);
        Assert.Equal(game.Dealer, game.CurrentPlayer);
        Act(game, PokerAction.Call);
        Act(game, PokerAction.Check);
        Assert.Equal(game.BigBlindPlayer, game.CurrentPlayer);
        for (var street = 0; street < 3; street++) CheckStreet(game);
        Assert.True(game.StartNextHand());
        Assert.Equal(1, game.Dealer);
        Assert.Equal(1, game.SmallBlindPlayer);
        Assert.Equal(0, game.BigBlindPlayer);
        Assert.Equal(1, game.CurrentPlayer);
    }

    [Fact]
    public void InvalidActionsAreAtomicAndMinimumReraiseUsesLastFullRaise()
    {
        var game = Game();
        var initial = State(game);
        Assert.False(game.TryAct(4, PokerAction.Fold));
        Assert.False(game.TryAct(3, PokerAction.Check));
        Assert.False(game.TryAct(3, PokerAction.Raise, 39));
        Assert.False(game.TryAct(3, PokerAction.Raise, 2001));
        Assert.Equal(initial, State(game));
        Act(game, PokerAction.Raise, 100);
        Assert.Equal(180, game.MinimumRaiseTo);
        Act(game, PokerAction.Raise, 240);
        Assert.Equal(380, game.MinimumRaiseTo);
        Assert.False(game.TryAct(5, PokerAction.Raise, 379));
        Act(game, PokerAction.Raise, 380);
        Assert.Equal(520, game.MinimumRaiseTo);
    }

    [Fact]
    public void ShortAllInDoesNotReopenAnAlreadyActedPlayer()
    {
        var game = Game(3);
        SetStartStacks(game, 2000, 50, 2000);
        Act(game, PokerAction.Raise, 40); // A raises20, B can only add10.
        Act(game, PokerAction.AllIn);
        Assert.Equal(50, game.CurrentBet);
        Assert.Equal(70, game.MinimumRaiseTo);
        Act(game, PokerAction.Call);
        Assert.Equal(0, game.CurrentPlayer);
        Assert.False(game.CanRaise(0));
        var before = State(game);
        Assert.False(game.TryAct(0, PokerAction.Raise, 70));
        Assert.False(game.TryAct(0, PokerAction.AllIn));
        Assert.Equal(before, State(game));
        Act(game, PokerAction.Call);
        Assert.Equal(PokerPhase.Flop, game.Phase);
        Assert.Equal(150, game.Pot);
    }

    [Fact]
    public void CumulativeShortAllInsReopenAfterFullRaiseThreshold()
    {
        var game = Game(4);
        SetStartStacks(game, 50, 60, 2000, 2000);
        Act(game, PokerAction.Raise, 40); // player3, minimum increment20
        Act(game, PokerAction.AllIn); // player0 to50: short+10
        Act(game, PokerAction.AllIn); // player1 to60: short+10
        Act(game, PokerAction.Call); // player2
        Assert.Equal(3, game.CurrentPlayer);
        Assert.True(game.CanRaise(3));
        Assert.Equal(80, game.MinimumRaiseTo);
        Act(game, PokerAction.Raise, 80);
        Assert.Equal(2, game.CurrentPlayer);
        Assert.True(game.CanRaise(2));
    }

    [Fact]
    public void ShortOpeningAllInDoesNotReopenEarlierCheckAndCannotBeCompleted()
    {
        var game = Game(4);
        SetStartStacks(game, 2000, 2000, 30, 2000);
        Act(game, PokerAction.Call); Act(game, PokerAction.Call);
        Act(game, PokerAction.Call); Act(game, PokerAction.Check);
        Assert.Equal(PokerPhase.Flop, game.Phase);
        Assert.Equal(1, game.CurrentPlayer);
        Act(game, PokerAction.Check);
        Act(game, PokerAction.AllIn); // player2 has only10 left, below minimum opening20.
        Assert.Equal(10, game.CurrentBet);
        Assert.Equal(30, game.MinimumRaiseTo);
        Assert.False(game.TryAct(3, PokerAction.Raise, 20)); // no limit has no completion.
        Act(game, PokerAction.Call); Act(game, PokerAction.Call);
        Assert.Equal(1, game.CurrentPlayer);
        Assert.False(game.CanRaise(1));
        Act(game, PokerAction.Call);
        Assert.Equal(PokerPhase.Turn, game.Phase);
    }

    [Fact]
    public void LonePlayerCannotRaiseAgainstOnlyAllInOpponentsAndExcessIsReturned()
    {
        var game = Game(2);
        SetStartStacks(game, 2000, 100);
        Act(game, PokerAction.Raise, 500);
        Act(game, PokerAction.AllIn);
        Assert.True(game.Phase == PokerPhase.HandComplete || game.Phase == PokerPhase.MatchComplete);
        Assert.Equal(5, game.Board.Count);
        Assert.Equal(200, game.Pots.Sum(p => p.Amount));
        Assert.Equal(200, game.Winnings.Sum());
        Assert.Equal(100, game.Players[0].TotalBet);
        Assert.Equal(2100, game.Players.Sum(p => p.Stack));
        Assert.True(game.Players[0].Stack >= 1900);
    }

    [Fact]
    public void SoleActorStillHasCallOrFoldDecisionBeforeRunout()
    {
        var game = Game(2);
        SetStartStacks(game, 100, 2000);
        Act(game, PokerAction.AllIn);
        Assert.Equal(1, game.CurrentPlayer);
        Assert.Equal(80, game.ToCall(1));
        Assert.False(game.CanRaise(1));
        Assert.False(game.TryAct(1, PokerAction.AllIn));
        Act(game, PokerAction.Call);
        Assert.Equal(5, game.Board.Count);
        Assert.Equal(200, game.Winnings.Sum());
    }

    [Fact]
    public void FoldWinReturnsUncalledRaiseAndDoesNotDealBoard()
    {
        var game = Game(3);
        Act(game, PokerAction.Raise, 500);
        Act(game, PokerAction.Fold);
        Act(game, PokerAction.Fold);
        Assert.Equal(PokerPhase.HandComplete, game.Phase);
        Assert.Empty(game.Board);
        Assert.Equal(50, game.Winnings[0]);
        Assert.Equal(2030, game.Players[0].Stack);
        Assert.Equal(20, game.Players[0].TotalBet);
        Assert.Equal(6000, game.Players.Sum(p => p.Stack));
        Assert.All(game.Pots, pot => Assert.Equal(new[] { 0 }, pot.Winners));
    }

    [Fact]
    public void MainAndSidePotsHaveIndependentEligibilityAndWinners()
    {
        var game = Game(3);
        SetStartStacks(game, 100, 200, 300);
        SetCards(game, 0, C(Rank.Ace, Suit.Spades), C(Rank.Ace, Suit.Hearts));
        SetCards(game, 1, C(Rank.King, Suit.Spades), C(Rank.King, Suit.Hearts));
        SetCards(game, 2, C(Rank.Queen, Suit.Spades), C(Rank.Queen, Suit.Hearts));
        RigBoard(game, C(Rank.Two, Suit.Clubs), C(Rank.Five, Suit.Diamonds), C(Rank.Seven, Suit.Hearts), C(Rank.Nine, Suit.Spades), C(Rank.Jack, Suit.Clubs));
        Act(game, PokerAction.AllIn);
        Act(game, PokerAction.AllIn);
        Assert.False(game.CanRaise(2));
        Act(game, PokerAction.Call);
        Assert.Equal(new[] { 300, 200, 0 }, game.Winnings);
        Assert.Equal(new[] { 300, 200, 100 }, game.Players.Select(p => p.Stack));
        Assert.Equal(2, game.Pots.Count);
        Assert.Equal(new[] { 0, 1, 2 }, game.Pots[0].EligiblePlayers);
        Assert.Equal(new[] { 1, 2 }, game.Pots[1].EligiblePlayers);
        Assert.Equal(new[] { 0 }, game.Pots[0].Winners);
        Assert.Equal(new[] { 1 }, game.Pots[1].Winners);
    }

    [Fact]
    public void FoldedMoneyStaysInPotAndOddChipGoesClockwiseAfterButton()
    {
        var game = Game(3, small: 1, big: 2);
        SetCards(game, 0, C(Rank.Two, Suit.Clubs), C(Rank.Three, Suit.Hearts));
        SetCards(game, 1, C(Rank.Four, Suit.Clubs), C(Rank.Five, Suit.Hearts));
        SetCards(game, 2, C(Rank.Six, Suit.Clubs), C(Rank.Seven, Suit.Hearts));
        RigBoard(game, C(Rank.Ten, Suit.Spades), C(Rank.Jack, Suit.Spades), C(Rank.Queen, Suit.Spades), C(Rank.King, Suit.Spades), C(Rank.Ace, Suit.Spades));
        Act(game, PokerAction.Call); //2
        Act(game, PokerAction.Fold); //folded SB contributes1
        Act(game, PokerAction.Check);
        CheckStreet(game); CheckStreet(game); CheckStreet(game);
        Assert.Equal(new[] { 2, 0, 3 }, game.Winnings);
        Assert.Equal(6000, game.Players.Sum(p => p.Stack));
        Assert.All(game.Pots, pot => Assert.DoesNotContain(1, pot.EligiblePlayers));
    }

    [Fact]
    public void ShortBlindsRunOutImmediatelyAndNeverCreateChips()
    {
        var game = Game(2, stack: 5);
        Assert.Equal(-1, game.CurrentPlayer);
        Assert.Equal(5, game.Board.Count);
        Assert.Equal(10, game.Winnings.Sum());
        Assert.Equal(10, game.Players.Sum(p => p.Stack));
    }

    [Fact]
    public void ShortBigBlindDoesNotForceArtificialCallFromLoneSmallBlind()
    {
        var game = Game(3, stack: 100);
        // Prepare next hand with player2 the dealer/SB, player0 a short5 BB; player1 busted.
        typeof(PokerPlayer).GetProperty(nameof(PokerPlayer.Stack))!.SetValue(game.Players[0], 5);
        typeof(PokerPlayer).GetProperty(nameof(PokerPlayer.Stack))!.SetValue(game.Players[1], 0);
        typeof(PokerPlayer).GetProperty(nameof(PokerPlayer.Stack))!.SetValue(game.Players[2], 100);
        typeof(PokerGame).GetProperty(nameof(PokerGame.Phase))!.SetValue(game, PokerPhase.HandComplete);
        Assert.True(game.StartNextHand());
        Assert.Equal(0, game.BigBlindPlayer);
        Assert.Equal(-1, game.CurrentPlayer);
        Assert.Equal(5, game.Board.Count);
        Assert.Equal(10, game.Winnings.Sum());
        Assert.Equal(105, game.Players.Sum(p => p.Stack));
    }

    [Fact]
    public void BustOutSkipsPlayerAndHeadsUpBigBlindAdvances()
    {
        var game = Game(3);
        SetStartStacks(game, 100, 100, 200);
        SetCards(game, 0, C(Rank.Ace, Suit.Hearts), C(Rank.Ace, Suit.Spades));
        SetCards(game, 1, C(Rank.King, Suit.Hearts), C(Rank.King, Suit.Spades));
        SetCards(game, 2, C(Rank.Queen, Suit.Hearts), C(Rank.Queen, Suit.Spades));
        RigBoard(game, C(Rank.Two, Suit.Clubs), C(Rank.Five, Suit.Diamonds), C(Rank.Seven, Suit.Hearts), C(Rank.Nine, Suit.Spades), C(Rank.Jack, Suit.Clubs));
        Act(game, PokerAction.AllIn); Act(game, PokerAction.AllIn); Act(game, PokerAction.Call);
        Assert.Equal(0, game.Players[1].Stack);
        Assert.True(game.StartNextHand());
        Assert.Equal(0, game.BigBlindPlayer); // oldBB2 -> next alive0
        Assert.Equal(2, game.Dealer);
        Assert.False(game.Players[1].InHand);
        Assert.Empty(game.Players[1].HoleCards);
        Assert.Equal(400, game.Players.Sum(p => p.Stack) + game.Pot);
        Assert.Empty(game.Pots);
        Assert.All(game.Winnings, amount => Assert.Equal(0, amount));
    }

    [Fact]
    public void LastSurvivorEndsMatchWithoutAnotherDeal()
    {
        var game = Game(2, stack: 100);
        SetCards(game, 0, C(Rank.Ace, Suit.Hearts), C(Rank.Ace, Suit.Spades));
        SetCards(game, 1, C(Rank.King, Suit.Hearts), C(Rank.King, Suit.Spades));
        RigBoard(game, C(Rank.Two, Suit.Clubs), C(Rank.Five, Suit.Diamonds), C(Rank.Seven, Suit.Hearts), C(Rank.Nine, Suit.Spades), C(Rank.Jack, Suit.Clubs));
        Act(game, PokerAction.AllIn); Act(game, PokerAction.Call);
        Assert.Equal(PokerPhase.MatchComplete, game.Phase);
        Assert.False(game.StartNextHand());
        Assert.False(game.TryAct(0, PokerAction.Check));
        Assert.Equal(200, game.Players[0].Stack);
    }

    [Fact]
    public void BustedBigBlindLeavesDeadSmallBlindThenDeadButton()
    {
        var game = Game(6);
        FinishFixtureWithStacks(game, 2000, 2000, 0, 2000, 2000, 2000);
        Assert.True(game.StartNextHand());
        Assert.Equal(1, game.Dealer);
        Assert.Equal(-1, game.SmallBlindPlayer);
        Assert.Equal(3, game.BigBlindPlayer);
        Assert.Equal(20, game.Pot);
        Assert.Equal(4, game.CurrentPlayer);
        CompletePassiveHand(game);
        Assert.True(game.StartNextHand());
        Assert.Equal(2, game.Dealer); // Physical marker stays on the busted seat for this hand.
        Assert.Equal(3, game.SmallBlindPlayer);
        Assert.Equal(4, game.BigBlindPlayer);
        Assert.Equal(30, game.Pot);
        Assert.Empty(game.Players[2].HoleCards);
        CompletePassiveHand(game);
        Assert.True(game.StartNextHand());
        Assert.Equal(3, game.Dealer);
        Assert.Equal(4, game.SmallBlindPlayer);
        Assert.Equal(5, game.BigBlindPlayer);
    }

    [Fact]
    public void BustedSmallBlindLeavesDeadButtonWithoutSkippingNextBigBlind()
    {
        var game = Game(5);
        FinishFixtureWithStacks(game, 2000, 0, 2000, 2000, 2000);
        Assert.True(game.StartNextHand());
        Assert.Equal(1, game.Dealer);
        Assert.Equal(2, game.SmallBlindPlayer);
        Assert.Equal(3, game.BigBlindPlayer);
        while (game.Phase == PokerPhase.Preflop)
            Act(game, game.ToCall(game.CurrentPlayer) > 0 ? PokerAction.Call : PokerAction.Check);
        Assert.Equal(2, game.CurrentPlayer); // SB acts first after the dead button.
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConsecutiveEmptySeatsDoNotShiftOrDuplicateBigBlind(bool bothBlindsBust)
    {
        var game = Game(6);
        FinishFixtureWithStacks(game, 2000, bothBlindsBust ? 0 : 2000, 0, 0, 2000, 2000);
        Assert.True(game.StartNextHand());
        Assert.Equal(1, game.Dealer);
        Assert.Equal(-1, game.SmallBlindPlayer);
        Assert.Equal(4, game.BigBlindPlayer); // First surviving seat after the previous BB2.
        CompletePassiveHand(game);
        Assert.True(game.StartNextHand());
        Assert.Equal(2, game.Dealer);
        Assert.Equal(4, game.SmallBlindPlayer);
        Assert.Equal(5, game.BigBlindPlayer);
        CompletePassiveHand(game);
        Assert.True(game.StartNextHand());
        Assert.Equal(4, game.Dealer); // Already-empty seat3 does not cost a blind orbit.
        Assert.Equal(5, game.SmallBlindPlayer);
        Assert.Equal(0, game.BigBlindPlayer);
    }

    [Theory]
    [InlineData(0, 1, 2)]
    [InlineData(1, 0, 2)]
    [InlineData(2, 0, 1)]
    public void HeadsUpTransitionAfterAnyThreeHandedBustKeepsCorrectBigBlind(int busted, int bb, int dealer)
    {
        var game = Game(3);
        var stacks = new[] { 2000, 2000, 2000 };
        stacks[busted] = 0;
        FinishFixtureWithStacks(game, stacks);
        Assert.True(game.StartNextHand());
        Assert.Equal(bb, game.BigBlindPlayer);
        Assert.Equal(dealer, game.Dealer);
        Assert.Equal(dealer, game.SmallBlindPlayer);
        Assert.Equal(dealer, game.CurrentPlayer);
        CompletePassiveHand(game);
        Assert.True(game.StartNextHand());
        Assert.Equal(dealer, game.BigBlindPlayer);
        Assert.Equal(bb, game.SmallBlindPlayer);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(6)]
    public void SeededLongMatchesConserveChipsAndAlwaysAdvance(int count)
    {
        for (var seed = 0; seed < 15; seed++)
        {
            var game = Game(count, seed);
            var random = new Random(seed + 99);
            var actions = 0;
            while (game.HandNumber <= 40 && game.Phase != PokerPhase.MatchComplete)
            {
                Assert.Equal(count * 2000, game.Players.Sum(p => p.Stack) + game.Pot);
                Assert.All(game.Players, p => Assert.True(p.Stack >= 0 && p.TotalBet >= 0 && p.RoundBet >= 0));
                if (game.Phase == PokerPhase.HandComplete)
                {
                    Assert.Equal(game.Pots.Sum(p => p.Amount), game.Winnings.Sum());
                    Assert.True(game.StartNextHand());
                    continue;
                }
                Assert.InRange(game.CurrentPlayer, 0, count - 1);
                var actor = game.CurrentPlayer;
                var roll = random.Next(12);
                if (roll == 0) Act(game, PokerAction.Fold);
                else if (roll == 1 && game.CanRaise(actor)) Act(game, PokerAction.AllIn);
                else if (roll < 5 && game.CanRaise(actor) && game.Players[actor].Stack + game.Players[actor].RoundBet >= game.MinimumRaiseTo)
                    Act(game, PokerAction.Raise, game.MinimumRaiseTo);
                else Act(game, game.ToCall(actor) > 0 ? PokerAction.Call : PokerAction.Check);
                Assert.True(++actions < 20000, "Betting did not terminate.");
            }
            Assert.Equal(count * 2000, game.Players.Sum(p => p.Stack) + game.Pot);
        }
    }

    private static void Act(PokerGame game, PokerAction action, int raiseTo = 0)
        => Assert.True(game.TryAct(game.CurrentPlayer, action, raiseTo), $"{game.Phase} player {game.CurrentPlayer}: {action} to{raiseTo}; current{game.CurrentBet}, minimum{game.MinimumRaiseTo}");

    private static void CheckStreet(PokerGame game)
    {
        var phase = game.Phase;
        var guard = 0;
        while (game.Phase == phase)
        {
            Assert.True(++guard <= 6);
            Act(game, PokerAction.Check);
        }
    }

    private static void CompletePassiveHand(PokerGame game)
    {
        var actions = 0;
        while (game.CurrentPlayer >= 0)
        {
            Assert.True(++actions <= 30);
            Act(game, game.ToCall(game.CurrentPlayer) > 0 ? PokerAction.Call : PokerAction.Check);
        }
    }

    private static void FinishFixtureWithStacks(PokerGame game, params int[] stacks)
    {
        for (var i = 0; i < stacks.Length; i++)
            typeof(PokerPlayer).GetProperty(nameof(PokerPlayer.Stack))!.SetValue(game.Players[i], stacks[i]);
        typeof(PokerGame).GetProperty(nameof(PokerGame.Phase))!.SetValue(game, PokerPhase.HandComplete);
    }

    private static string State(PokerGame game)
        => $"{game.Revision},{game.Phase},{game.CurrentPlayer},{game.CurrentBet},{game.Pot},{game.LastAction}:" + string.Join(";", game.Players.Select(p => $"{p.Stack}/{p.RoundBet}/{p.TotalBet}/{p.Folded}/{p.AllIn}"));

    // Fixtures replace only private deal data / starting stacks; every betting step uses the public API.
    private static void SetStartStacks(PokerGame game, params int[] startingStacks)
    {
        for (var i = 0; i < startingStacks.Length; i++)
        {
            typeof(PokerPlayer).GetProperty(nameof(PokerPlayer.Stack))!.SetValue(game.Players[i], startingStacks[i] - game.Players[i].TotalBet);
            typeof(PokerPlayer).GetProperty(nameof(PokerPlayer.AllIn))!.SetValue(game.Players[i], startingStacks[i] == game.Players[i].TotalBet);
        }
    }

    private static void SetCards(PokerGame game, int player, params Card[] cards)
    {
        var hand = (List<Card>)typeof(PokerPlayer).GetField("_holeCards", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game.Players[player])!;
        hand.Clear(); hand.AddRange(cards);
    }

    private static void RigBoard(PokerGame game, params Card[] board)
    {
        var used = game.Players.SelectMany(p => p.HoleCards).Concat(board).ToHashSet();
        var burns = Enumerable.Range(2, 13).SelectMany(rank => Enumerable.Range(0, 4).Select(suit => C((Rank)rank, (Suit)suit))).Where(card => !used.Contains(card)).Take(3).ToArray();
        var deck = new Deck(new[] { burns[0], board[0], board[1], board[2], burns[1], board[3], burns[2], board[4] });
        typeof(PokerGame).GetField("_deck", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(game, deck);
    }

    private static Card C(Rank rank, Suit suit) => new(rank, suit);
}
