using System.Collections.Generic;
using System.Linq;
using CardGame.Core.Cards;
using Xunit;

namespace CardGame.Core.Tests;

public class DeckTests
{
    [Fact]
    public void Standard36_Contains36UniqueCards()
    {
        var deck = Deck.Standard36(1);
        var drawn = new List<Card>();
        while (!deck.IsEmpty)
        {
            drawn.Add(deck.Draw());
        }

        Assert.Equal(36, drawn.Count);
        Assert.Equal(36, drawn.Distinct().Count());
        Assert.All(drawn, card => Assert.True(card.Rank >= Rank.Six));
    }

    [Fact]
    public void SameSeed_ProducesSameOrder()
    {
        var first = DrawAll(Deck.Standard36(42));
        var second = DrawAll(Deck.Standard36(42));

        Assert.Equal(first, second);
    }

    [Fact]
    public void DifferentSeed_ProducesDifferentOrder()
    {
        var first = DrawAll(Deck.Standard36(1));
        var second = DrawAll(Deck.Standard36(2));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Standard52_ContainsFullDeck()
    {
        Assert.Equal(52, DrawAll(Deck.Standard52(7)).Count);
    }

    [Fact]
    public void Standard24_ContainsOnlyNineAndAbove()
    {
        var drawn = DrawAll(Deck.Standard24(3));

        Assert.Equal(24, drawn.Count);
        Assert.Equal(24, drawn.Distinct().Count());
        Assert.All(drawn, card => Assert.True(card.Rank >= Rank.Nine));
    }

    [Fact]
    public void Standard54_Contains52CardsAndTwoDistinctJokers()
    {
        var drawn = DrawAll(Deck.Standard54(5));

        Assert.Equal(54, drawn.Count);
        Assert.Equal(54, drawn.Distinct().Count());
        Assert.Equal(2, drawn.Count(c => c.IsJoker));
    }

    [Fact]
    public void Standard54_JokersAreShuffledAmongOtherCards()
    {
        // Джокеры не должны всегда оседать на одной и той же позиции (например в конце) —
        // это означало бы, что они не участвуют в общем перемешивании.
        var positions = new HashSet<int>();
        for (var seed = 0; seed < 20; seed++)
        {
            var drawn = DrawAll(Deck.Standard54(seed));
            for (var i = 0; i < drawn.Count; i++)
            {
                if (drawn[i].IsJoker)
                {
                    positions.Add(i);
                }
            }
        }

        Assert.True(positions.Count > 5);
    }

    private static List<Card> DrawAll(Deck deck)
    {
        var cards = new List<Card>();
        while (!deck.IsEmpty)
        {
            cards.Add(deck.Draw());
        }

        return cards;
    }
}
