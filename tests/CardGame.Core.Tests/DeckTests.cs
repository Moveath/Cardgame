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
