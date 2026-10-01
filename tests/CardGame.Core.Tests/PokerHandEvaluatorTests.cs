using CardGame.Core.Cards;
using CardGame.Core.Games.Poker;

namespace CardGame.Core.Tests;

public class PokerHandEvaluatorTests
{
    [Theory]
    [InlineData("As Kd 9c 7h 3s", PokerHandCategory.HighCard)]
    [InlineData("As Ad 9c 7h 3s", PokerHandCategory.OnePair)]
    [InlineData("As Ad 9c 9h 3s", PokerHandCategory.TwoPair)]
    [InlineData("As Ad Ac 7h 3s", PokerHandCategory.ThreeOfAKind)]
    [InlineData("8s 7d 6c 5h 4s", PokerHandCategory.Straight)]
    [InlineData("As Js 9s 7s 3s", PokerHandCategory.Flush)]
    [InlineData("As Ad Ac 7h 7s", PokerHandCategory.FullHouse)]
    [InlineData("As Ad Ac Ah 3s", PokerHandCategory.FourOfAKind)]
    [InlineData("9s 8s 7s 6s 5s", PokerHandCategory.StraightFlush)]
    public void RecognizesEveryCategory(string cards, PokerHandCategory expected)
    {
        var result = PokerHandEvaluator.Evaluate(Cards(cards));
        Assert.Equal(expected, result.Category);
        Assert.Equal(5, result.BestFive.Count);
        Assert.False(string.IsNullOrWhiteSpace(result.Name));
    }

    [Fact]
    public void RoyalFlushIsAceHighStraightFlush()
    {
        var royal = Value("Ah Kh Qh Jh Th 3c 2s");
        Assert.True(royal.IsRoyalFlush);
        Assert.Equal("Роял-флеш", royal.Name);
        Assert.True(royal > Value("Ks Qs Js Ts 9s"));
        Assert.False(Value("As 5s 4s 3s 2s").IsRoyalFlush);
    }

    [Fact]
    public void WheelHasFiveHighAndLosesToSixHigh()
    {
        var wheel = Value("As 2d 3c 4h 5s Kh Qc");
        Assert.Equal(PokerHandCategory.Straight, wheel.Category);
        Assert.Equal(new[] { Rank.Five, Rank.Four, Rank.Three, Rank.Two, Rank.Ace }, wheel.BestFive.Select(c => c.Rank));
        Assert.True(wheel < Value("6s 5d 4c 3h 2s"));
        Assert.Equal(PokerHandCategory.HighCard, Value("Qs Kd Ac 2h 3s").Category);
    }

    [Theory]
    [InlineData("As Kd Qc Jh 9s", "Ah Kc Qd Js 8h")]
    [InlineData("As Ad Kc Qh 9s", "Ah Ac Ks Qd 8h")]
    [InlineData("Qs Qd Jc Jh 2s", "Qh Qc Ts Td Ah")]
    [InlineData("Qs Qd Jc Jh As", "Qh Qc Js Jd Kh")]
    [InlineData("Qs Qd Qc Jh 9s", "Qh Qc Qs Jd 8h")]
    [InlineData("As Js 9s 7s 4s", "Ah Jh 9h 7h 3h")]
    [InlineData("Qs Qd Qc Jh Js", "Jh Jc Js Ad Ah")]
    [InlineData("Qs Qd Qc Jh Js", "Qh Qc Qs Td Th")]
    [InlineData("Qs Qd Qc Qh As", "Qs Qd Qc Qh Kh")]
    public void ComparesEveryKickerInOrder(string stronger, string weaker)
    {
        Assert.True(Value(stronger) > Value(weaker));
        Assert.True(Value(weaker) < Value(stronger));
    }

    [Fact]
    public void CategoriesAlwaysOutrankLowerCategories()
    {
        var weakToStrong = new[]
        {
            "As Kd Qc Jh 9s", "2s 2d 3c 4h 5s", "2s 2d 3c 3h 4s", "2s 2d 2c 3h 4s",
            "As 2d 3c 4h 5s", "2s 4s 6s 8s Ts", "2s 2d 2c 3h 3s", "2s 2d 2c 2h 3s", "As 2s 3s 4s 5s"
        };
        for (var i = 1; i < weakToStrong.Length; i++) Assert.True(Value(weakToStrong[i]) > Value(weakToStrong[i - 1]));
    }

    [Fact]
    public void ChoosesHigherTripAndBestPairWhenSevenCardsContainTwoTrips()
    {
        var value = Value("As Ad Ac Ks Kd Kc Qh");
        Assert.Equal(PokerHandCategory.FullHouse, value.Category);
        Assert.Equal(new[] { Rank.Ace, Rank.Ace, Rank.Ace, Rank.King, Rank.King }, value.BestFive.Select(c => c.Rank));
    }

    [Fact]
    public void ThreePairsUseTwoHighestAndBestRemainingKicker()
    {
        Assert.Equal(Value("As Ad Ks Kd Qs"), Value("As Ad Ks Kd Qs Qd 2c"));
    }

    [Fact]
    public void SixCardFlushDiscardsItsLowestCard()
    {
        var value = Value("As Ks 9s 7s 5s 2s Qd");
        Assert.Equal(Value("As Ks 9s 7s 5s"), value);
        Assert.DoesNotContain(Cards("2s")[0], value.BestFive);
    }

    [Fact]
    public void SeparateFlushAndStraightDoNotCountAsStraightFlush()
    {
        Assert.Equal(PokerHandCategory.Flush, Value("As Ks Qs Js 9s Tc 2d").Category);
    }

    [Fact]
    public void IdenticalBestFiveSplitEvenWithDifferentHoleCardsAndSuits()
    {
        var one = Value("As Kd Qc Jh Ts 2c 3c");
        var two = Value("As Kd Qc Jh Ts 8h 9h");
        Assert.Equal(0, one.CompareTo(two));
        Assert.Equal(one, two);
        Assert.Equal(one.GetHashCode(), two.GetHashCode());
        Assert.Equal(Value("As Ad Kc Qh 9s"), Value("Ah Ac Ks Qd 9h"));
    }

    [Fact]
    public void DoesNotRequireEitherHoleCardForBestFive()
    {
        var board = Cards("5s 6s 7s 8s 9s");
        var value = PokerHandEvaluator.Evaluate(board.Concat(Cards("As Ad")).ToArray());
        Assert.Equal(PokerHandCategory.StraightFlush, value.Category);
        Assert.All(value.BestFive, card => Assert.Contains(card, board));
    }

    [Fact]
    public void ReorderingCardsDoesNotChangeValueAndInputIsNotModified()
    {
        var hand = Cards("As Ad Ac Ks Kd Qh 2c");
        var original = hand.ToArray();
        var value = PokerHandEvaluator.Evaluate(hand);
        Assert.Equal(original, hand);
        Assert.Equal(value, PokerHandEvaluator.Evaluate(hand.Reverse().ToArray()));
    }

    [Fact]
    public void RejectsInvalidDeckCardsAndCounts()
    {
        Assert.Throws<ArgumentNullException>(() => PokerHandEvaluator.Evaluate(null!));
        Assert.Throws<ArgumentException>(() => Value("As Kd Qc Jh"));
        Assert.Throws<ArgumentException>(() => Value("As Kd Qc Jh 9s 8h 7d 6c"));
        Assert.Throws<ArgumentException>(() => Value("As As Qc Jh 9s"));
        Assert.Throws<ArgumentException>(() => PokerHandEvaluator.Evaluate(Cards("As Kd Qc Jh").Append(new Card(Rank.Joker, Suit.Clubs)).ToArray()));
        Assert.Throws<ArgumentException>(() => PokerHandEvaluator.Evaluate(Cards("As Kd Qc Jh").Append(new Card(Rank.Two, (Suit)4)).ToArray()));
    }

    internal static PokerHandValue Value(string cards) => PokerHandEvaluator.Evaluate(Cards(cards));
    internal static Card[] Cards(string cards) => cards.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(token => new Card(
        token[0] switch { 'T' => Rank.Ten, 'J' => Rank.Jack, 'Q' => Rank.Queen, 'K' => Rank.King, 'A' => Rank.Ace, _ => (Rank)(token[0] - '0') },
        token[1] switch { 's' => Suit.Spades, 'h' => Suit.Hearts, 'd' => Suit.Diamonds, 'c' => Suit.Clubs, _ => throw new ArgumentException(token) })).ToArray();
}
