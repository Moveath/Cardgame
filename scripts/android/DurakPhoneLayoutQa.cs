using System;
using System.Collections.Generic;
using System.Linq;
using CardGameClient;
using CardGame.Core.Cards;
using CardGame.Core.Games.Durak;
using UnityEngine;
using UnityEngine.UI;

public static partial class MobileLayoutQa
{
    private static void CheckDurakLayout(MonoBehaviour table)
    {
        Canvas.ForceUpdateCanvases();
        var original = Get<DurakGame>(table, "_game");
        var root = (RectTransform)table.transform;
        var suffix = Mathf.RoundToInt(root.rect.width) + "x" + Mathf.RoundToInt(root.rect.height) + "-" + original.Players.Count + "p";
        DurakCheckSeats(table);
        DurakCheckHand(table, false);
        Save("durak-" + suffix + "-opening.png");

        // Legal, reproducible six-pair bout: the human keeps a full hand while two
        // opponents play five defenses and a sixth attack. No UI coordinates mocked.
        var names = original.Players.Select(p => p.Name).ToArray();
        var fixture = DurakOccupiedFixture(names, false);
        Check(fixture.Table.Count == 6 && fixture.Table.Count(s => s.IsDefended) == 5,
            "durak actual six-pair bout with five defenses");
        DurakShowFixture(table, fixture);
        DurakCheckSeats(table);
        DurakCheckHand(table, true);
        Save("durak-" + suffix + "-six-pairs.png");

        // Select a real card and exercise the controller's raised-card layout.
        Set(table, "_choiceCard", (Card?)fixture.Players[0].Hand[fixture.Players[0].Hand.Count / 2]);
        Call(table, "RefreshHand");
        Canvas.ForceUpdateCanvases();
        DurakCheckHand(table, true);
        Save("durak-" + suffix + "-raised-card.png");
        Set(table, "_choiceCard", null);

        // Taking the very same legal bout gives the human twelve cards. This checks
        // the real fan for a large hand rather than artificially spreading transforms.
        var largeHand = DurakOccupiedFixture(names, true);
        Check(largeHand.TryApplyMove(0, DurakMove.Take(), out _), "durak human takes occupied bout");
        for (var pass = 0; largeHand.BoutsFinished == 0 && pass < names.Length * 2; pass++)
        {
            var actor = DurakTurnOrder.GetActingPlayer(largeHand);
            Check(actor.HasValue, "durak taken bout has a next action");
            Check(largeHand.GetLegalMoves(actor.Value).Any(m => m.Type == DurakMoveType.Pass) &&
                largeHand.TryApplyMove(actor.Value, DurakMove.Pass(), out _), "durak pass after take");
        }
        Check(largeHand.Players[0].Hand.Count >= 12, "durak real hand grows to twelve cards");
        DurakShowFixture(table, largeHand);
        DurakCheckSeats(table);
        DurakCheckHand(table, false);
        Save("durak-" + suffix + "-large-hand.png");
    }

    private static DurakGame DurakOccupiedFixture(string[] names, bool humanDefends)
    {
        var attacker = humanDefends ? names.Length - 1 : 1;
        var defender = (attacker + 1) % names.Length;
        var attacks = Enumerable.Range(6, 6).Select(r => new Card((Rank)r, r == 6 ? Suit.Spades : Suit.Clubs)).ToArray();
        var defenses = Enumerable.Range(7, 6).Select(r => new Card((Rank)r, Suit.Spades)).ToArray();
        var all = new List<Card>();
        for (var rank = Rank.Two; rank <= Rank.Ace; rank++)
            for (var suit = Suit.Clubs; suit <= Suit.Spades; suit++) all.Add(new Card(rank, suit));
        var trump = new Card(Rank.Ace, Suit.Spades);
        var reserved = new HashSet<Card>(attacks.Concat(defenses)) { trump };
        // Undealt low trumps keep the prescribed attacker the first legal attacker.
        var pool = all.Where(c => !reserved.Contains(c) && !(c.Suit == Suit.Spades && c.Rank < Rank.Six)).ToList();
        var hands = new List<Card>[names.Length];
        for (var player = 0; player < names.Length; player++)
        {
            hands[player] = player == attacker ? attacks.ToList() : player == defender ? defenses.ToList() : pool.Take(6).ToList();
            foreach (var card in hands[player]) pool.Remove(card);
        }
        var deck = new List<Card>();
        for (var round = 0; round < 6; round++)
            for (var player = 0; player < names.Length; player++) deck.Add(hands[player][round]);
        var dealt = new HashSet<Card>(deck);
        deck.AddRange(all.Where(c => !dealt.Contains(c) && c != trump));
        deck.Add(trump);
        var game = DurakGame.Create(names, new Deck(deck), DurakOptions.Classic);
        Check(game.AttackerIndex == attacker && game.DefenderIndex == defender, "durak deterministic first attacker");
        for (var slot = 0; slot < 6; slot++)
        {
            Check(game.TryApplyMove(attacker, DurakMove.Attack(attacks[slot]), out _), "durak legal fixture attack " + slot);
            if (slot < 5) Check(game.TryApplyMove(defender, DurakMove.Defend(defenses[slot], slot), out _), "durak legal fixture defense " + slot);
        }
        return game;
    }

    private static void DurakShowFixture(MonoBehaviour table, DurakGame game)
    {
        table.StopAllCoroutines();
        Set(table, "_game", game);
        Set(table, "_waitingForHuman", true);
        Set(table, "_holdHands", false);
        Set(table, "_holdTable", false);
        Set(table, "_tableSignature", null);
        var shown = Get<HashSet<Card>>(table, "_tableCardsShown");
        shown.Clear();
        foreach (var slot in game.Table)
        {
            shown.Add(slot.Attack);
            if (slot.Defense.HasValue) shown.Add(slot.Defense.Value);
        }
        Get<CardView>(table, "_trumpCard").ShowFace(game.TrumpCard);
        Get<Text>(table, "_trumpInfo").text = "козырь " + CardView.SuitText(game.Trump);
        Call(table, "Refresh");
        Canvas.ForceUpdateCanvases();
    }

    private static void DurakCheckSeats(MonoBehaviour table)
    {
        var root = (RectTransform)table.transform;
        var opponents = Get<RectTransform>(table, "_opponentsArea");
        var deck = root.Find("Trump").GetComponentsInChildren<CardView>();
        foreach (var back in opponents.GetComponentsInChildren<CardView>())
        {
            var rect = DurakBounds(root, back.Rect);
            Check(DurakInside(root.rect, rect, 12), "durak opponent fan stays inside screen");
            foreach (var pile in deck)
                Check(!rect.Overlaps(DurakBounds(root, pile.Rect)), "durak opponent fan clears deck");
        }
        var game = Get<DurakGame>(table, "_game");
        for (var player = 0; player < game.Players.Count; player++)
        {
            var anchor = (Vector2)Call(table, "SeatAnchorPosition", player);
            var origin = new Vector2(anchor.x, root.rect.yMax + anchor.y);
            Check(root.rect.Contains(origin), "durak animated card endpoint remains on screen");
        }
    }

    private static void DurakCheckHand(MonoBehaviour table, bool occupied)
    {
        var root = (RectTransform)table.transform;
        var hand = Get<RectTransform>(table, "_handRow").GetComponentsInChildren<CardView>();
        var cards = Get<RectTransform>(table, "_tableRow").GetComponentsInChildren<CardView>();
        if (occupied) Check(cards.Length == 11, "durak all eleven played cards rendered");
        foreach (var view in hand)
        {
            var bounds = DurakBounds(root, view.Rect);
            Check(DurakInside(root.rect, bounds, 12), "durak hand remains inside screen");
            foreach (var played in cards)
                Check(!bounds.Overlaps(DurakBounds(root, played.Rect)), "durak hand does not cover played cards");
            foreach (var name in new[] { "_takeButton", "_passButton" })
                Check(!bounds.Overlaps(DurakBounds(root, Get<Button>(table, name).GetComponent<RectTransform>())),
                    "durak hand clears " + name);
        }
        foreach (var played in cards)
        {
            var bounds = DurakBounds(root, played.Rect);
            Check(DurakInside(root.rect, bounds, 12), "durak played cards stay on screen");
            foreach (var opponent in Get<RectTransform>(table, "_opponentsArea").GetComponentsInChildren<CardView>())
                Check(!bounds.Overlaps(DurakBounds(root, opponent.Rect)), "durak played cards clear opponent fans");
            foreach (var pile in root.Find("Trump").GetComponentsInChildren<CardView>())
                Check(!bounds.Overlaps(DurakBounds(root, pile.Rect)), "durak played cards clear trump and deck");
            foreach (var field in new[] { "_takeButton", "_passButton" })
                Check(!bounds.Overlaps(DurakBounds(root, Get<Button>(table, field).GetComponent<RectTransform>())),
                    "durak played cards clear " + field);
        }
        foreach (var field in new[] { "_takeButton", "_passButton" })
        {
            var rect = Get<Button>(table, field).GetComponent<RectTransform>();
            Check(rect.rect.height >= 82 && rect.rect.width >= 240, "durak comfortable action target " + field);
        }
    }

    private static Rect DurakBounds(RectTransform root, RectTransform item)
    {
        var corners = new Vector3[4]; item.GetWorldCorners(corners);
        var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        foreach (var corner in corners)
        {
            var point = (Vector2)root.InverseTransformPoint(corner);
            min = Vector2.Min(min, point); max = Vector2.Max(max, point);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private static bool DurakInside(Rect screen, Rect item, float inset) => item.xMin >= screen.xMin + inset &&
        item.xMax <= screen.xMax - inset && item.yMin >= screen.yMin + inset && item.yMax <= screen.yMax - inset;
}
