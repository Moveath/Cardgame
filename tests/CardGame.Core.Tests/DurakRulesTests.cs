using CardGame.Core.Cards;
using CardGame.Core.Games.Durak;
using Xunit;

namespace CardGame.Core.Tests;

public class DurakRulesTests
{
    [Fact]
    public void HigherCardOfSameSuit_Beats()
    {
        Assert.True(DurakRules.Beats(
            new Card(Rank.King, Suit.Hearts),
            new Card(Rank.Ten, Suit.Hearts),
            Suit.Spades));
    }

    [Fact]
    public void LowerCardOfSameSuit_DoesNotBeat()
    {
        Assert.False(DurakRules.Beats(
            new Card(Rank.Seven, Suit.Hearts),
            new Card(Rank.Ten, Suit.Hearts),
            Suit.Spades));
    }

    [Fact]
    public void TrumpBeatsAnyPlainSuit()
    {
        Assert.True(DurakRules.Beats(
            new Card(Rank.Six, Suit.Spades),
            new Card(Rank.Ace, Suit.Hearts),
            Suit.Spades));
    }

    [Fact]
    public void PlainSuit_DoesNotBeatTrump()
    {
        Assert.False(DurakRules.Beats(
            new Card(Rank.Ace, Suit.Hearts),
            new Card(Rank.Six, Suit.Spades),
            Suit.Spades));
    }

    [Fact]
    public void OffSuitNonTrump_DoesNotBeat()
    {
        Assert.False(DurakRules.Beats(
            new Card(Rank.Ace, Suit.Hearts),
            new Card(Rank.Six, Suit.Clubs),
            Suit.Spades));
    }

    [Fact]
    public void HigherTrumpBeatsLowerTrump()
    {
        Assert.True(DurakRules.Beats(
            new Card(Rank.Ace, Suit.Spades),
            new Card(Rank.King, Suit.Spades),
            Suit.Spades));
    }
}
