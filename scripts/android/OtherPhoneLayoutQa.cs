using System;
using System.Collections.Generic;
using System.Linq;
using CardGameClient;
using CardGame.Core.Cards;
using CardGame.Core.Games.Bluff;
using CardGame.Core.Games.Chests;
using CardGame.Core.Games.Poker;
using UnityEngine;
using UnityEngine.UI;

public static partial class MobileLayoutQa
{
    private static void CheckOtherLayout(MonoBehaviour table, string game)
    {
        if (game == "bluff") CheckPhoneBluff(table);
        if (game == "chests") CheckPhoneChests(table);
        if (game == "poker") CheckPhonePoker(table);
        if (game == "govno")
        {
            var names = Get<Text[]>(table, "_seatLabels");
            foreach (var name in names) OtherTextFits(name, "Govno player name");
            var depth = Get<Text[]>(table, "_seatDepth");
            var ready = Get<Text[]>(table, "_seatReady");
            for (var i = 0; i < names.Length; i++)
            {
                OtherSeparate(names[i].rectTransform, depth[i].rectTransform, "Govno name/depth");
                OtherSeparate(depth[i].rectTransform, ready[i].rectTransform, "Govno depth/readiness");
                OtherInside(ready[i].rectTransform, "Govno readiness caption");
            }
        }
    }

    private static void CheckPhoneBluff(MonoBehaviour table)
    {
        var game = Get<BluffGame>(table, "_game");
        Check(game.Phase == BluffPhase.RoundStart, "Bluff starts with rank choice");
        var card = game.Players[0].Hand.First(c => c.Rank != Rank.Ace);
        Get<List<Card>>(table, "_selected").Add(card);
        Set(table, "_declaredRank", card.Rank);
        Call(table, "Refresh");
        var row = Get<RectTransform>(table, "_rankRow");
        var buttons = row.GetComponentsInChildren<Button>();
        Check(buttons.Length == 12, "Full-deck Bluff offers all twelve ranks");
        var hand = Get<RectTransform>(table, "_handRow").GetComponentsInChildren<CardView>();
        var actions = new[] { "_throwButton", "_challengeButton", "_shootoutButton" }
            .Select(n => Get<Button>(table, n)).Where(b => b.gameObject.activeInHierarchy).ToArray();
        for (var i = 0; i < buttons.Length; i++)
        {
            var rect = (RectTransform)buttons[i].transform;
            OtherInside(rect, "Bluff rank");
            Check(rect.rect.height >= 64, "Bluff rank touch height");
            for (var j = i + 1; j < buttons.Length; j++) OtherSeparate(rect, (RectTransform)buttons[j].transform, "Bluff ranks");
            foreach (var view in hand) OtherSeparate(rect, view.Rect, "Bluff rank/selected hand");
            foreach (var button in actions) OtherSeparate(rect, (RectTransform)button.transform, "Bluff rank/action");
        }
        var badge = Get<Text>(table, "_rankBadge");
        Check(string.IsNullOrEmpty(badge.text), "Bluff rank picker has no obscured duplicate caption");
        OtherSeparate(Get<Text>(table, "_status").rectTransform, (RectTransform)Get<Button>(table, "_menuButton").transform, "Bluff prompt/navigation");
        OtherTextFits(Get<Text>(table, "_status"), "Bluff selected-card prompt");
        foreach (var back in Get<RectTransform>(table, "_opponentsArea").GetComponentsInChildren<CardView>())
        {
            OtherInside(back.Rect, "Bluff opponent fan");
            var bounds = OtherBounds(back.Rect); var screen = ((RectTransform)canvasObject.transform).rect;
            Check(bounds.xMin >= screen.xMin + 38 && bounds.xMax <= screen.xMax - 38, "Bluff opponent fan inside felt");
            var owner = back.transform.parent.parent;
            foreach (Transform seat in Get<RectTransform>(table, "_opponentsArea"))
            {
                if (seat == owner || !seat.gameObject.activeInHierarchy) continue;
                var portrait = seat.Find("Avatar/Portrait") as RectTransform;
                if (portrait != null) OtherSeparate(back.Rect, portrait, "Bluff fan/neighbour portrait");
            }
        }
        Save("bluff-ranks-selected.png");

        var actor = game.CurrentPlayerIndex;
        var played = game.Players[actor].Hand.First(c => c.Rank != Rank.Ace);
        Check(game.TryApplyMove(actor, BluffMove.Declare(played.Rank, played), out _), "Bluff actual declaration");
        Set(table, "_waitingForHuman", game.CurrentPlayerIndex == 0);
        Call(table, "Refresh");
        Check(!row.gameObject.activeInHierarchy && !string.IsNullOrEmpty(badge.text), "Bluff replaces selector with declared rank");
        foreach (var view in Get<RectTransform>(table, "_potArea").GetComponentsInChildren<CardView>())
            OtherSeparate(badge.rectTransform, view.Rect, "Bluff pot/declared rank");
        foreach (var view in Get<RectTransform>(table, "_handRow").GetComponentsInChildren<CardView>())
            OtherSeparate(badge.rectTransform, view.Rect, "Bluff declared rank/hand");
        Save("bluff-declared-pot.png");
    }

    private static void CheckPhoneChests(MonoBehaviour table)
    {
        var game = Get<ChestsGame>(table, "_game");
        Check(game.Phase == ChestsPhase.Asking, "Chests asking phase");
        Set(table, "_chosenTarget", 1);
        Call(table, "RefreshChoices");
        var buttons = Get<RectTransform>(table, "_choiceArea").GetComponentsInChildren<Button>();
        Check(buttons.Length == game.AskableRanks(0).Count, "Chests all available ranks shown");
        foreach (var button in buttons)
        {
            var rect = (RectTransform)button.transform;
            OtherInside(rect, "Chests rank");
            Check(rect.rect.width >= 72 && rect.rect.height >= 64, "Chests rank touch target");
            foreach (var view in Get<RectTransform>(table, "_handRow").GetComponentsInChildren<CardView>())
                OtherSeparate(rect, view.Rect, "Chests rank/hand");
        }
        OtherSeparate(Get<Text>(table, "_progress").rectTransform, Get<Text>(table, "_collectedRanks").rectTransform, "Chests progress lines");
        Save("chests-ranks.png");
        Set(table, "_chosenTarget", null);
        Call(table, "RefreshChoices");
    }

    private static void CheckPhonePoker(MonoBehaviour table)
    {
        var intro = Get<RectTransform>(table, "_intro");
        foreach (var text in intro.GetComponentsInChildren<Text>()) OtherTextFits(text, "Poker introduction");
        intro.gameObject.SetActive(false);
        var game = Get<PokerGame>(table, "_game");
        for (var i = 0; i < 12 && game.CurrentPlayer > 0; i++)
        {
            var actor = game.CurrentPlayer;
            Check(game.TryAct(actor, game.ToCall(actor) > 0 ? PokerAction.Call : PokerAction.Check), "Poker bots reach human turn");
        }
        Check(game.CurrentPlayer == 0, "Poker actual human turn");
        Set(table, "_clock", 30f);
        Set(table, "_displayBoardCount", game.Board.Count);
        Call(table, "Render");
        var controls = Get<RectTransform>(table, "_controls");
        var buttons = controls.GetComponentsInChildren<Button>();
        Check(buttons.Length == 7, "Poker four actions and three raise presets");
        for (var i = 0; i < buttons.Length; i++)
        {
            var rect = (RectTransform)buttons[i].transform;
            OtherInside(rect, "Poker action");
            Check(rect.rect.height >= 64, "Poker action touch height");
            OtherTextFits(buttons[i].GetComponentInChildren<Text>(), "Poker action text");
            for (var j = i + 1; j < buttons.Length; j++) OtherSeparate(rect, (RectTransform)buttons[j].transform, "Poker action targets");
            foreach (var card in Get<CardView[]>(table, "_humanCards")) OtherSeparate(rect, card.Rect, "Poker action/hole cards");
        }
        Check(Get<Button>(table, "_raiseButton").interactable, "Poker raise controls actionable");
        var input = Get<InputField>(table, "_raiseInput");
        OtherInside((RectTransform)input.transform, "Poker custom raise input");
        foreach (var button in buttons) OtherSeparate((RectTransform)input.transform, (RectTransform)button.transform, "Poker input/action");
        // Numeric editing still updates the wager model when the desktop slider is hidden.
        input.onEndEdit.Invoke("100");
        Check(input.text == "100" && Mathf.Approximately(Get<Slider>(table, "_raiseSlider").value, 100), "Poker custom amount remains editable");
        foreach (var seat in Get<Array>(table, "_seats"))
        {
            var fields = seat.GetType();
            foreach (var field in new[] { "Name", "Stack" })
                OtherTextFits((Text)fields.GetField(field).GetValue(seat), "Poker visible seat " + field);
            var cards = (CardView[])fields.GetField("Cards").GetValue(seat);
            if (cards != null)
                foreach (var card in cards)
                {
                    OtherSeparate(card.Rect, Get<Text>(table, "_potText").rectTransform, "Poker opponent cards/pot total");
                    foreach (var boardCard in Get<CardView[]>(table, "_boardCards"))
                        OtherSeparate(card.Rect, boardCard.Rect, "Poker opponent cards/board");
                }
        }
        Save("poker-human-actions.png");
    }

    private static Rect OtherBounds(RectTransform rect)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        foreach (var corner in corners)
        {
            Vector2 local = canvasObject.transform.InverseTransformPoint(corner);
            min = Vector2.Min(min, local); max = Vector2.Max(max, local);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private static void OtherSeparate(RectTransform a, RectTransform b, string label)
    {
        var first = OtherBounds(a); var second = OtherBounds(b);
        // A half-unit tolerance ignores floating point touching edges, not visible overlap.
        first.xMin += .5f; first.xMax -= .5f; first.yMin += .5f; first.yMax -= .5f;
        Check(!first.Overlaps(second), label + " do not overlap");
    }

    private static void OtherInside(RectTransform rect, string label)
    {
        var bounds = OtherBounds(rect); var screen = ((RectTransform)canvasObject.transform).rect;
        Check(bounds.xMin >= screen.xMin - 1 && bounds.xMax <= screen.xMax + 1 &&
            bounds.yMin >= screen.yMin - 1 && bounds.yMax <= screen.yMax + 1, label + " stays on screen");
    }

    private static void OtherTextFits(Text text, string label)
    {
        Canvas.ForceUpdateCanvases();
        Check(!string.IsNullOrEmpty(text.text), label + " populated");
        var settings = text.GetGenerationSettings(text.rectTransform.rect.size);
        if (text.resizeTextForBestFit)
        {
            // Match the rendered best-fit size; the desired unshrunk size is not
            // the size Unity uses for an intentionally bounded long prompt.
            text.cachedTextGenerator.Populate(text.text, settings);
            settings.fontSize = text.cachedTextGenerator.fontSizeUsedForBestFit;
            settings.resizeTextForBestFit = false;
        }
        var height = text.cachedTextGeneratorForLayout.GetPreferredHeight(text.text, settings) / text.pixelsPerUnit;
        Check(height <= text.rectTransform.rect.height + 1, label + " is not clipped");
    }
}
