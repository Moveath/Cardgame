// Copy into the isolated QA project's Assets/Scripts/Editor; run -executeMethod PokerTableQa.Run.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CardGameClient;
using CardGame.Core.Cards;
using CardGame.Core.Games.Poker;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static class PokerTableQa
{
    private static readonly string Output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../poker-qa"));
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static GameObject canvasObject;
    private static Camera camera;
    private static EventSystem eventSystem;
    private static int checks;
    private static string lastRaycastDetails;

    public static void Run()
    {
        Directory.CreateDirectory(Output);
        try
        {
            CheckChips();
            foreach (var count in new[] { 2, 4, 6 }) CheckTable(count);
            CheckWideViewport();
            CheckLegalControls();
            CheckAllInRunoutAndEliminatedSeat();
            CheckShortAllInRefund();
            CheckMenuEntry();
            File.WriteAllText(Path.Combine(Output, "verification.txt"), checks + " assertions passed: 2/4/6-seat layout with current base portraits, 2040x900 full-width layout and help/raycasts, hole-card secrecy, chips 0..12000, pause/help freeze, legal controls and UI raycasts, four-street hand, staggered all-in runout, unmatched-chip refund, eliminated seats through another showdown, single payout, next hand and menu entry/exit.");
            Debug.Log("POKER TABLE QA PASSED: " + checks);
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            File.WriteAllText(Path.Combine(Output, "error.txt"), error.ToString());
            EditorApplication.Exit(1);
        }
    }

    private static void CheckChips()
    {
        // Exhaust the playable six-seat money range, but report one invariant rather than 12,001 duplicate checks.
        for (var amount = 0; amount <= 12000; amount++)
        {
            var parts = PokerChipPile.Breakdown(amount);
            if (parts.Length != 5 || parts.Any(value => value < 0)
                || parts.Select((count, index) => count * PokerChipPile.Denominations[index]).Sum() != amount)
                throw new Exception("Chip exchange lost money at " + amount);
        }
        Check(true, "chip exchange conserves every integer from zero through total six-player bankroll");
        Check(PokerChipPile.Breakdown(2000).SequenceEqual(new[] { 20, 16, 12, 6, 2 }), "starting stack has all five denominations");
    }

    private static PokerTableController NewTable(int count, int seed = 417)
    {
        Begin(1600, 900);
        var go = new GameObject("Poker QA table", typeof(RectTransform));
        go.transform.SetParent(canvasObject.transform, false);
        ((RectTransform)go.transform).sizeDelta = new Vector2(1600, 900);
        var table = go.AddComponent<PokerTableController>();
        table.enabled = false;
        table.Configure(new PokerMatchSettings { PlayerCount = count }, () => { });
        Call(table, "Start");
        Check(Get<RectTransform>(table, "_intro").gameObject.activeSelf, "table starts behind the introduction");
        var clock = Get<float>(table, "_clock");
        Call(table, "Tick", 3f);
        Check(Get<float>(table, "_clock") == clock, "introduction freezes the whole table");
        Get<RectTransform>(table, "_intro").gameObject.SetActive(false);
        // Old QA projects can still contain archived costumes: explicitly use current production base portraits.
        var ids = new[] { "yegor", "ali", "aslan", "danil", "marat", "yelnur" };
        var names = new[] { "Егор", "Али", "Аслан", "Данил", "Марат", "Ельнур" }.Take(count).ToArray();
        var portraits = Get<Sprite[]>(table, "_portraits");
        for (var i = 0; i < count; i++)
        {
            portraits[i] = Resources.Load<Sprite>("PlayerAvatars/" + ids[i] + "/" + ids[i] + "_01");
            Check(portraits[i] != null, "current portrait " + ids[i]);
        }
        Install(table, new PokerGame(names, seed));
        Drain(table);
        return table;
    }

    private static void Install(PokerTableController table, PokerGame game)
    {
        Call(table, "ClearFlights");
        Set(table, "_game", game);
        Call(table, "BuildSeats");
        Call(table, "ResetHandPresentation");
        Call(table, "Render");
    }

    private static void CheckTable(int count)
    {
        var table = NewTable(count);
        var game = Get<PokerGame>(table, "_game");
        var seats = Get<Array>(table, "_seats");
        Check(seats.Length == count, "requested seats exist");
        Check(game.Pot == 30 && game.Players.Sum(player => player.Stack) == count * 2000 - 30, "blinds and exact money shown from core");
        Check(Get<Text>(table, "_potText").text.Contains("30"), "initial pot label");
        var back = Resources.Load<Sprite>("Cards/back");
        Check(back != null, "card back asset is available");
        foreach (var card in Get<CardView[]>(table, "_humanCards"))
            Check(card.gameObject.activeSelf && Get<Image>(card, "_background").sprite != back, "human sees own card");
        for (var i = 1; i < count; i++)
            foreach (var card in Get<CardView[]>(seats.GetValue(i), "Cards"))
                Check(card.gameObject.activeSelf && Get<Image>(card, "_background").sprite == back, "opponent hole card stays closed");
        Check(Get<CardView[]>(table, "_boardCards").All(card => !card.gameObject.activeSelf), "preflop has only empty board marks");
        CheckControlsAgreeWithCore(table);
        Save("poker-" + count + "-preflop.png");

        var root = (RectTransform)table.transform;
        foreach (var viewport in new[] { new Vector2(1280, 720), new Vector2(1920, 900) })
        {
            root.sizeDelta = viewport;
            Call(table, "Resize");
            var stage = Get<RectTransform>(table, "_stage");
            Check(1600 * stage.localScale.x <= viewport.x + .01f && 900 * stage.localScale.y <= viewport.y + .01f, "entire stage fits resized viewport");
        }
        root.sizeDelta = new Vector2(1600, 900);
        Call(table, "Resize");
        CheckFreeze(table);

        var actions = 0;
        var previousStreet = game.Phase;
        while (game.Phase != PokerPhase.HandComplete && game.Phase != PokerPhase.MatchComplete)
        {
            var player = game.CurrentPlayer;
            var action = game.ToCall(player) == 0 ? PokerAction.Check : PokerAction.Call;
            Check((bool)Call(table, "PlayAction", player, action, 0), "table accepts real call/check action");
            if (game.Phase != previousStreet)
            {
                if (game.Board.Count > 0 && game.Phase != PokerPhase.HandComplete && game.Phase != PokerPhase.MatchComplete)
                    Check(Get<int>(table, "_displayBoardCount") < game.Board.Count, "new street waits for its card reveal");
                previousStreet = game.Phase;
            }
            Drain(table);
            CheckControlsAgreeWithCore(table);
            if (count == 6 && game.Phase == PokerPhase.Flop && game.CurrentPlayer == game.SmallBlindPlayer)
                Save("poker-flop.png");
            if (++actions > 30) throw new Exception("A check/call hand failed to finish");
        }
        Check(actions == count * 4, "all players act on all four streets");
        Check(game.Board.Count == 5 && Get<int>(table, "_displayBoardCount") == 5, "five board cards displayed");
        Check(Get<bool>(table, "_settled"), "payout presentation completed");
        Check(Get<Button>(table, "_nextButton").gameObject.activeSelf && Get<Button>(table, "_nextButton").interactable, "next hand enabled after payout");
        Check(game.Pots.Sum(pot => pot.Amount) == count * 20 && game.Players.Sum(player => player.Stack) == count * 2000, "showdown conserves money");
        for (var i = 1; i < count; i++)
            foreach (var card in Get<CardView[]>(seats.GetValue(i), "Cards"))
                Check(Get<Image>(card, "_background").sprite != back, "showdown reveals participating opponents");
        var result = Get<Text>(table, "_resultText").text;
        Check(!string.IsNullOrEmpty(result), "showdown identifies winners");
        var balances = game.Players.Select(player => player.Stack).ToArray();
        Tick(table, 5f);
        Check(game.Players.Select(player => player.Stack).SequenceEqual(balances)
            && Get<IList>(table, "_chipFlights").Count == 0 && Get<Text>(table, "_resultText").text == result,
            "settled hand never repeats the payout or chip flight");
        if (count == 6) Save("poker-showdown.png");
        var dealer = game.Dealer;
        Call(table, "NextHand");
        Check(game.HandNumber == 2 && game.Dealer != dealer, "next hand advances button");
        Check(game.Board.Count == 0 && Get<int>(table, "_displayBoardCount") == 0 && !Get<bool>(table, "_settled"), "next hand resets board and presentation");
        Check(game.Players.Sum(player => player.Stack) + game.Pot == count * 2000, "balances survive next hand");
    }

    private static void CheckFreeze(PokerTableController table)
    {
        var game = Get<PokerGame>(table, "_game");
        var clock = Get<float>(table, "_clock");
        var revision = game.Revision;
        Call(table, "SetPaused", true);
        Call(table, "Tick", 20f);
        Check(Get<float>(table, "_clock") == clock && game.Revision == revision, "pause freezes table and bot clock");
        Check(!Get<Button>(table, "_callButton").interactable, "pause disables game controls");
        Call(table, "SetPaused", false);
        var help = Get<PokerHelpOverlay>(table, "_help");
        help.Show();
        Call(table, "RefreshControls");
        Call(table, "Tick", 20f);
        Check(help.IsOpen && Get<float>(table, "_clock") == clock && game.Revision == revision, "help freezes table and bot clock");
        Check(help.GetComponentsInChildren<CardView>().Length == 50, "guide illustrates all ten hands with five cards each");
        Check(help.GetComponent<Image>().raycastTarget, "guide blocks clicks through the table");
        var covered = Raycast((RectTransform)Get<Button>(table, "_callButton").transform, Vector2.zero);
        Check(covered.gameObject.transform == help.transform || covered.gameObject.transform.IsChildOf(help.transform),
            "actual help raycast intercepts the call-button position; help depth " + help.GetComponent<Image>().depth + "; " + lastRaycastDetails);
        if (game.Players.Count == 6) Save("poker-help.png");
        help.Hide();
        Call(table, "RefreshControls");
        Check(!help.IsOpen, "help closes");
    }

    private static void CheckLegalControls()
    {
        var table = NewTable(3);
        var game = Get<PokerGame>(table, "_game");
        Check(game.CurrentPlayer == 0 && game.ToCall(0) == 20, "human preflop call fixture");
        Check(Get<Text>(table, "_callText").text.Contains("Колл") && Get<Text>(table, "_callText").text.Contains("20"), "call button names exact debt");
        Check(Get<Slider>(table, "_raiseSlider").minValue == 40 && Get<Button>(table, "_raiseButton").interactable, "minimum preflop raise exposed");
        foreach (var name in new[] { "_foldButton", "_callButton", "_raiseButton", "_allInButton" })
        {
            var button = Get<Button>(table, name);
            var hit = Raycast((RectTransform)button.transform, Vector2.zero);
            Check(hit.gameObject.GetComponentInParent<Button>() == button, "actual pointer reaches " + name);
        }
        var slider = Get<Slider>(table, "_raiseSlider");
        var oldValue = slider.value;
        var trackHit = Raycast((RectTransform)slider.transform, new Vector2(((RectTransform)slider.transform).rect.width * .25f, 0));
        Check(trackHit.gameObject.GetComponentInParent<Slider>() == slider, "slider track accepts a click away from the handle");
        var trackPointer = Pointer(trackHit);
        ExecuteEvents.ExecuteHierarchy(trackHit.gameObject, trackPointer, ExecuteEvents.pointerDownHandler);
        ExecuteEvents.ExecuteHierarchy(trackHit.gameObject, trackPointer, ExecuteEvents.pointerUpHandler);
        Check(slider.value > oldValue + 100 && Get<InputField>(table, "_raiseInput").text == Mathf.RoundToInt(slider.value).ToString(), "actual track click moves amount and updates numeric input");
        var callHit = Raycast((RectTransform)Get<Button>(table, "_callButton").transform, Vector2.zero);
        ExecuteEvents.ExecuteHierarchy(callHit.gameObject, Pointer(callHit), ExecuteEvents.pointerClickHandler);
        Drain(table);
        Check(game.Players[0].RoundBet == 20, "human control commits a legal call");
        Check(!Get<Button>(table, "_callButton").interactable, "controls disabled on bot turn");
        while (game.Phase == PokerPhase.Preflop)
        {
            Check((bool)Call(table, "PlayAction", game.CurrentPlayer, game.ToCall(game.CurrentPlayer) == 0 ? PokerAction.Check : PokerAction.Call, 0), "advance preflop");
            Drain(table);
        }
        while (game.CurrentPlayer != 0)
        {
            Check((bool)Call(table, "PlayAction", game.CurrentPlayer, PokerAction.Check, 0), "postflop checks to human");
            Drain(table);
        }
        Check(Get<Text>(table, "_callText").text == "Чек" && Get<Text>(table, "_raiseText").text == "Ставка", "zero debt offers check and opening bet");
        Check(Get<Slider>(table, "_raiseSlider").minValue == 20, "opening bet minimum is big blind");

        var names = game.Players.Select(player => player.Name).ToArray();
        var shortStack = new PokerGame(names, 8);
        SetProperty(shortStack.Players[0], "Stack", 15);
        Install(table, shortStack); Drain(table);
        Check(Get<Button>(table, "_callButton").interactable && Get<Button>(table, "_allInButton").interactable
            && !Get<Button>(table, "_raiseButton").interactable, "short stack can call all-in but cannot manufacture a raise");
        Check(Get<Text>(table, "_callText").text.Contains("15"), "short all-in call shows actual affordable amount");

        var lockedRaise = new PokerGame(names, 9);
        SetProperty(lockedRaise.Players[1], "Stack", 40); // SB10 + remaining40 = all-in50.
        Check(lockedRaise.TryAct(0, PokerAction.Raise, 40), "reopening fixture initial raise");
        Check(lockedRaise.TryAct(1, PokerAction.AllIn), "reopening fixture short all-in");
        Check(lockedRaise.TryAct(2, PokerAction.Call), "reopening fixture opponent call");
        Install(table, lockedRaise); Drain(table);
        Check(lockedRaise.CurrentPlayer == 0 && !lockedRaise.CanRaise(0), "short all-in does not reopen action");
        Check(Get<Button>(table, "_callButton").interactable && !Get<Button>(table, "_raiseButton").interactable
            && !Get<Button>(table, "_allInButton").interactable, "UI reflects closed raising rights");
    }

    private static void CheckWideViewport()
    {
        var table = NewTable(6);
        const int width = 2040, height = 900;
        ((RectTransform)canvasObject.transform).sizeDelta = new Vector2(width, height);
        ((RectTransform)table.transform).sizeDelta = new Vector2(width, height);
        var oldTarget = camera.targetTexture;
        camera.targetTexture = new RenderTexture(width, height, 24);
        camera.targetTexture.Create();
        UnityEngine.Object.DestroyImmediate(oldTarget);
        camera.orthographicSize = height / 2f;
        Call(table, "Resize");
        Call(table, "Render");
        var stage = Get<RectTransform>(table, "_stage");
        Check(Mathf.Abs(stage.rect.width * stage.localScale.x - width) < 1
            && Mathf.Abs(stage.rect.height * stage.localScale.y - height) < 1,
            "adaptive poker stage fills a 2040x900 viewport");
        Save("poker-6-wide.png");

        // Check actual pointer surfaces after resize, with the same graphic-depth refresh as a rendered frame.
        foreach (var name in new[] { "_callButton", "_raiseButton", "_allInButton" })
        {
            var button = Get<Button>(table, name);
            var hit = Raycast((RectTransform)button.transform, Vector2.zero);
            Check(hit.gameObject.GetComponentInParent<Button>() == button, "wide viewport pointer reaches " + name);
        }
        var slider = Get<Slider>(table, "_raiseSlider");
        var track = Raycast((RectTransform)slider.transform, new Vector2(((RectTransform)slider.transform).rect.width * .25f, 0));
        Check(track.gameObject.GetComponentInParent<Slider>() == slider, "wide viewport slider track stays under its pointer target");

        var help = Get<PokerHelpOverlay>(table, "_help");
        help.Show();
        Call(table, "RefreshControls");
        Check(Mathf.Abs(((RectTransform)help.transform).rect.width - width) < 1,
            "help overlay covers the entire wide viewport");
        Save("poker-help-wide.png");
        var covered = Raycast((RectTransform)Get<Button>(table, "_callButton").transform, Vector2.zero);
        Check(covered.gameObject.transform == help.transform || covered.gameObject.transform.IsChildOf(help.transform),
            "wide help intercepts controls beneath it; " + lastRaycastDetails);
        help.Hide();
    }

    private static void CheckAllInRunoutAndEliminatedSeat()
    {
        var table = NewTable(3);
        var game = Get<PokerGame>(table, "_game");
        SetStartingStacks(game, 100, 100, 200);
        RigAcesKingsQueens(game);
        Call(table, "ResetRaise"); Call(table, "Render");
        Check((bool)Call(table, "PlayAction", 0, PokerAction.AllIn, 0), "first player shoves"); Drain(table);
        Check((bool)Call(table, "PlayAction", 1, PokerAction.AllIn, 0), "second player calls all-in"); Drain(table);
        Check((bool)Call(table, "PlayAction", 2, PokerAction.Call, 0), "covering stack calls");
        Check(game.Phase == PokerPhase.HandComplete && game.Board.Count == 5, "rules resolve the all-in board");
        Check(Get<int>(table, "_displayBoardCount") == 0 && !Get<bool>(table, "_settled"), "presentation initially conceals the resolved runout and payout");
        Check(game.Players.Sum(player => player.Stack) == 400 && game.Winnings.Sum() == 300, "three-way all-in conserves chips");
        foreach (var expected in new[] { 3, 4, 5 })
        {
            var before = Get<int>(table, "_displayBoardCount");
            var untilReveal = Get<float>(table, "_revealAt") - Get<float>(table, "_clock");
            Tick(table, Mathf.Max(0, untilReveal - .01f));
            Check(Get<int>(table, "_displayBoardCount") == before, "all-in street waits for its own reveal time");
            Tick(table, .02f);
            Check(Get<int>(table, "_displayBoardCount") == expected
                && Get<CardView[]>(table, "_boardCards").Count(card => card.gameObject.activeSelf) == expected,
                "all-in reveals exactly " + expected + " board cards");
            Check(!Get<bool>(table, "_settled") && !Get<Button>(table, "_nextButton").gameObject.activeSelf, "payout cannot overtake board reveal");
            if (expected == 3) Save("poker-allin-flop.png");
            if (expected == 5) Save("poker-allin-river.png");
        }
        var untilPayout = Get<float>(table, "_settleAt") - Get<float>(table, "_clock");
        Tick(table, untilPayout - .02f);
        Check(!Get<bool>(table, "_settled"), "river remains readable before chip payout");
        Tick(table, .03f);
        Check(Get<bool>(table, "_settled"), "payout follows the visible river hold");
        Drain(table);
        Check(game.Players[1].Stack == 0 && game.Players[0].Stack == 300 && game.Players[2].Stack == 100, "rigged showdown eliminates only middle seat");
        Call(table, "NextHand");
        Check(game.HandNumber == 2 && !game.Players[1].InHand && game.Players[1].HoleCards.Count == 0, "eliminated player receives no next-hand cards");
        Call(table, "Render"); Drain(table);
        var removed = Get<Array>(table, "_seats").GetValue(1);
        Check(Get<CardView[]>(removed, "Cards").All(card => !card.gameObject.activeSelf), "eliminated seat cards stay hidden without indexing an empty hand");
        Save("poker-eliminated-seat.png");
        var actions = 0;
        while (game.Phase != PokerPhase.HandComplete && game.Phase != PokerPhase.MatchComplete)
        {
            var actor = game.CurrentPlayer;
            Check((bool)Call(table, "PlayAction", actor, game.ToCall(actor) == 0 ? PokerAction.Check : PokerAction.Call, 0), "survivors play another real hand");
            Drain(table);
            if (++actions > 12) throw new Exception("Heads-up survivors did not finish");
        }
        Call(table, "Render");
        Check(Get<CardView[]>(removed, "Cards").All(card => !card.gameObject.activeSelf)
            && game.Players[1].HoleCards.Count == 0, "later showdown also skips eliminated empty hole cards");
        Check(game.Players.Sum(player => player.Stack) == 400, "second-hand survivors retain total bankroll");
    }

    private static void CheckShortAllInRefund()
    {
        var table = NewTable(2);
        var game = Get<PokerGame>(table, "_game");
        SetStartingStacks(game, 2000, 100);
        RigAcesKingsQueens(game);
        Call(table, "ResetRaise"); Call(table, "Render");
        Check((bool)Call(table, "PlayAction", 0, PokerAction.Raise, 500), "large raise commits five hundred"); Drain(table);
        Check((bool)Call(table, "PlayAction", 1, PokerAction.AllIn, 0), "short stack calls only one hundred");
        Check(game.Players[0].TotalBet == 100 && game.Pots.Sum(pot => pot.Amount) == 200, "uncalled four hundred excluded from contested pot");
        Check(game.Players.Sum(player => player.Stack) == 2100, "all-in refund and payout conserve exact core bankroll");
        var flights = Get<IList>(table, "_chipFlights").Cast<object>().ToArray();
        var returned = flights.SingleOrDefault(flight => Get<int>(Get<PokerChipPile>(flight, "Pile"), "_amount") == 400);
        Check(returned != null && Get<Vector2>(returned, "To") == (Vector2)Call(table, "StackPosition", 0), "uncalled chips visibly fly back to their owner");
        Check(flights.Sum(flight => Get<int>(Get<PokerChipPile>(flight, "Pile"), "_amount")) == 600, "collection flights split into two hundred pot plus four hundred refund");
        Check(Get<int>(Get<PokerChipPile>(table, "_humanChips"), "_amount") == 1900
            && Get<int>(Get<PokerChipPile>(table, "_potChips"), "_amount") == 200, "before payout UI shows refunded stack plus only matched pot");
        Drain(table);
        Check(game.Players[0].Stack == 2100 && game.Players[1].Stack == 0
            && Get<int>(Get<PokerChipPile>(table, "_humanChips"), "_amount") == 2100, "displayed final stack includes refund exactly once");
    }

    private static void SetStartingStacks(PokerGame game, params int[] stacks)
    {
        for (var i = 0; i < stacks.Length; i++)
        {
            SetProperty(game.Players[i], "Stack", stacks[i] - game.Players[i].TotalBet);
            SetProperty(game.Players[i], "AllIn", stacks[i] == game.Players[i].TotalBet);
        }
    }

    private static void RigAcesKingsQueens(PokerGame game)
    {
        for (var i = 0; i < game.Players.Count; i++)
        {
            var cards = Get<List<Card>>(game.Players[i], "_holeCards");
            cards.Clear();
            cards.Add(new Card((Rank)(14 - i), Suit.Spades));
            cards.Add(new Card((Rank)(14 - i), Suit.Hearts));
        }
        Set(game, "_deck", new Deck(new[]
        {
            new Card(Rank.Two, Suit.Diamonds), // burn
            new Card(Rank.Two, Suit.Clubs), new Card(Rank.Five, Suit.Diamonds), new Card(Rank.Seven, Suit.Hearts),
            new Card(Rank.Three, Suit.Diamonds), // burn
            new Card(Rank.Nine, Suit.Spades),
            new Card(Rank.Four, Suit.Diamonds), // burn
            new Card(Rank.Jack, Suit.Clubs)
        }));
    }

    private static RaycastResult Raycast(RectTransform target, Vector2 localPoint)
    {
        Canvas.ForceUpdateCanvases();
        // Batch editor calls Show/Hide and raycasts in one frame. Render after rebuilding so a newly
        // enabled overlay has the same canvas depth/order that a player sees in the following frame.
        camera.Render();
        var position = RectTransformUtility.WorldToScreenPoint(camera, target.TransformPoint(localPoint));
        var pointer = new PointerEventData(eventSystem) { position = position, button = PointerEventData.InputButton.Left };
        var hits = new List<RaycastResult>();
        canvasObject.GetComponent<GraphicRaycaster>().Raycast(pointer, hits);
        lastRaycastDetails = "target " + Hierarchy(target) + " at " + position + "; hits "
            + string.Join(" | ", hits.Select(hit => Hierarchy(hit.gameObject.transform) + " depth=" + hit.depth));
        if (hits.Count == 0) throw new Exception("No UI raycast hit: " + lastRaycastDetails);
        return hits[0];
    }

    private static string Hierarchy(Transform target)
    {
        var names = new List<string>();
        for (var node = target; node != null; node = node.parent) names.Add(node.name);
        names.Reverse();
        return string.Join("/", names);
    }

    private static PointerEventData Pointer(RaycastResult hit)
    {
        return new PointerEventData(eventSystem)
        {
            position = hit.screenPosition, button = PointerEventData.InputButton.Left,
            pointerCurrentRaycast = hit, pointerPressRaycast = hit
        };
    }

    private static void CheckControlsAgreeWithCore(PokerTableController table)
    {
        var game = Get<PokerGame>(table, "_game");
        var complete = game.Phase == PokerPhase.HandComplete || game.Phase == PokerPhase.MatchComplete;
        var humanTurn = !complete && game.CurrentPlayer == 0;
        Check(Get<Button>(table, "_callButton").interactable == humanTurn, "call/check availability matches turn");
        Check(Get<Button>(table, "_raiseButton").interactable == (humanTurn && game.CanRaise(0)
            && game.Players[0].Stack + game.Players[0].RoundBet >= game.MinimumRaiseTo), "raise availability matches core");
    }

    private static void CheckMenuEntry()
    {
        Begin(1600, 900);
        var flow = canvasObject.AddComponent<AppFlowController>();
        flow.enabled = false;
        Call(flow, "Start");
        var selection = Get<GameObject>(flow, "_modeSelectScreen");
        Call(flow, "ShowScreen", selection);
        var poker = selection.GetComponentsInChildren<Button>().Single(button => button.GetComponentInChildren<Text>().text == "Покер");
        Save("poker-menu.png");
        poker.onClick.Invoke();
        var lobby = Get<GameObject>(flow, "_pokerLobbyScreen");
        Check(lobby.activeSelf, "menu enters poker lobby");
        var choices = Get<IList>(flow, "_pokerPlayerButtons");
        Check(choices.Count == 5, "lobby offers two through six seats");
        ((Button)choices[4]).onClick.Invoke();
        Check(Get<PokerMatchSettings>(flow, "_pokerSettings").PlayerCount == 6, "lobby selects six seats");
        Save("poker-lobby.png");
        lobby.GetComponentsInChildren<Button>().Single(button => button.GetComponentInChildren<Text>().text == "За стол").onClick.Invoke();
        var gameScreen = Get<GameObject>(flow, "_gameScreen");
        var table = gameScreen.GetComponent<PokerTableController>();
        table.enabled = false;
        Check(gameScreen.activeSelf && Get<PokerMatchSettings>(table, "_settings").PlayerCount == 6, "lobby passes settings to full-screen poker");
        Get<Action>(table, "_onExit")();
        Check(Get<GameObject>(flow, "_mainMenuScreen").activeSelf && !gameScreen.activeSelf, "table exit returns to main menu");
    }

    private static void Drain(PokerTableController table)
    {
        for (var i = 0; i < 16; i++)
        {
            Tick(table, .5f);
            var game = Get<PokerGame>(table, "_game");
            var complete = game.Phase == PokerPhase.HandComplete || game.Phase == PokerPhase.MatchComplete;
            if (Get<float>(table, "_clock") >= Get<float>(table, "_busyUntil")
                && Get<int>(table, "_displayBoardCount") == game.Board.Count && (!complete || Get<bool>(table, "_settled"))) return;
        }
        throw new Exception("Presentation did not drain in eight seconds");
    }

    private static void Tick(PokerTableController table, float delta)
    {
        // Freeze only automated decisions: the actual pausable clock, flights and board presentation still run.
        var game = Get<PokerGame>(table, "_game");
        Set(table, "_scheduledPlayer", game.CurrentPlayer);
        Set(table, "_scheduledRevision", game.Revision);
        Set(table, "_thinkAt", float.PositiveInfinity);
        Call(table, "Tick", delta);
    }

    private static T Get<T>(object value, string name) => (T)value.GetType().GetField(name, Fields).GetValue(value);
    private static void Set(object value, string name, object field) => value.GetType().GetField(name, Fields).SetValue(value, field);
    private static void SetProperty(object value, string name, object field) => value.GetType().GetProperty(name, Fields).SetValue(value, field);
    private static object Call(object value, string name, params object[] arguments) => value.GetType().GetMethod(name, Fields).Invoke(value, arguments);
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception("FAILED: " + label);
        checks++;
    }

    private static void Begin(int width, int height)
    {
        if (canvasObject != null) UnityEngine.Object.DestroyImmediate(canvasObject);
        if (camera != null)
        {
            var oldTarget = camera.targetTexture;
            UnityEngine.Object.DestroyImmediate(camera.gameObject);
            UnityEngine.Object.DestroyImmediate(oldTarget);
        }
        canvasObject = new GameObject("Poker QA canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
        ((RectTransform)canvasObject.transform).sizeDelta = new Vector2(width, height);
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        camera = new GameObject("Poker QA camera", typeof(Camera)).GetComponent<Camera>();
        camera.transform.position = new Vector3(0, 0, -100);
        camera.orthographic = true;
        camera.orthographicSize = height / 2f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.025f, .045f, .037f);
        camera.nearClipPlane = .1f;
        camera.farClipPlane = 200;
        camera.targetTexture = new RenderTexture(width, height, 24);
        camera.targetTexture.Create();
        canvas.worldCamera = camera;
        var events = new GameObject("Poker QA events", typeof(EventSystem));
        events.transform.SetParent(canvasObject.transform, false);
        eventSystem = events.GetComponent<EventSystem>();
    }

    private static void Save(string name)
    {
        Canvas.ForceUpdateCanvases();
        camera.Render();
        var previous = RenderTexture.active;
        RenderTexture.active = camera.targetTexture;
        var texture = new Texture2D(camera.targetTexture.width, camera.targetTexture.height, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
        texture.Apply();
        File.WriteAllBytes(Path.Combine(Output, name), texture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(texture);
        RenderTexture.active = previous;
    }
}
