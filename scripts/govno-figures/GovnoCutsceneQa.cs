// Copy into the isolated QA project's Assets/Scripts/Editor and run via -executeMethod.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CardGameClient;
using CardGame.Core.Cards;
using CardGame.Core.Games.Govno;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class GovnoCutsceneQa
{
    private static readonly string Output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../cutscene-qa"));
    private static GameObject canvasObject;
    private static Camera camera;
    private static int checks;
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        Directory.CreateDirectory(Output);
        try
        {
            foreach (var count in new[] { 3, 4, 6 }) CheckTable(count);
            CheckIncomingReveal(false, false);
            CheckIncomingReveal(true, false);
            CheckIncomingReveal(false, true);
            File.WriteAllText(Path.Combine(Output, "verification.txt"), checks + " assertions passed: 3/4/6 players, all five seated sink stages and aligned labels, fixed cards, central timer visibility and clearance, zoom/sink order, pause, resize, persistent pools and hats, replay, final winners, and hidden incoming cards until timeout countdown ends in normal/chaos modes.");
            Debug.Log("GOVNO CUTSCENE QA PASSED: " + checks);
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            File.WriteAllText(Path.Combine(Output, "error.txt"), e.ToString());
            EditorApplication.Exit(1);
        }
    }

    private static void CheckTable(int count)
    {
        Begin(1600, 900);
        var tableRoot = new GameObject("Table", typeof(RectTransform));
        tableRoot.transform.SetParent(canvasObject.transform, false);
        var tableRect = (RectTransform)tableRoot.transform;
        tableRect.sizeDelta = new Vector2(1600, 900);
        var table = tableRoot.AddComponent<GovnoTableController>();
        table.enabled = false;
        table.Configure(new GovnoMatchSettings { PlayerCount = count }, () => { });
        Call(table, "Start");
        Check(!Get<RectTransform>(table, "_timerGroup").gameObject.activeSelf, "intro hides picking timer");
        Call(table, "StartMatch");
        Check(!Get<RectTransform>(table, "_timerGroup").gameObject.activeSelf, "start countdown has one number only");
        var ids = new[] { "yegor", "ali", "aslan", "danil", "marat", "yelnur" };
        var names = new[] { "Егор", "Али", "Аслан", "Данил", "Марат", "Ельнур" };
        var portraits = Get<Sprite[]>(table, "_portraits");
        for (var i = 0; i < count; i++)
        {
            portraits[i] = Resources.Load<Sprite>("PlayerAvatars/" + ids[i] + "/" + ids[i] + "_01");
            Check(portraits[i] != null, "current portrait " + ids[i]);
        }
        Set(table, "_game", new GovnoGame(new List<string>(names).GetRange(0, count), 712));
        Call(table, "BuildSeats");
        Call(table, "UpdatePresentation", 3.1f);
        Call(table, "RefreshForeground", 1f);
        var stickers = Get<GovnoLifeSticker[]>(table, "_seatStickers");
        var original = stickers[1];
        var anchor = Get<RectTransform[]>(table, "_seatAnchors")[1];
        Check(Mathf.Approximately(original.Root.localScale.x, .7f), "compact figure scale");
        CheckTimerLayout(table);
        foreach (var viewport in new[] { new Vector2(1280, 900), new Vector2(1920, 900) })
        {
            tableRect.sizeDelta = viewport;
            Call(table, "Resize");
            CheckTimerLayout(table);
        }
        tableRect.sizeDelta = new Vector2(1600, 900);
        Call(table, "Resize");
        CheckTimerPhases(table, names, count);
        Save("table-" + count + ".png");
        CheckSeatStageAlignment(table, tableRect, count);

        var game = Get<GovnoGame>(table, "_game");
        var oldRound = game.RoundNumber;
        var now = game.Now;
        Lose(table, 1, true);
        Check(!Get<RectTransform>(table, "_timerGroup").gameObject.activeSelf, "loss presentation hides picking timer");
        var lead = Get<float>(table, "_lossLeadIn");
        Check(lead > 1f, "hand order remains visible first");
        Check(Get<int>(original, "_lives") == 4, "loss deferred until close-up");
        Call(table, "UpdatePresentation", lead + .18f);
        Check(original.Root.parent == Get<RectTransform>(table, "_lossLayer"), "same sticker brought forward");
        Check(original.Root.localScale.x > .7f && original.Root.localScale.x < 1.6f, "smooth zoom between sizes");
        Check(Get<float>(original, "_depth") == 0, "no sinking during approach");
        Call(table, "UpdatePresentation", .4f);
        Check(Get<int>(original, "_lives") == 3 && Get<float>(original, "_depth") > 0, "sinking after zoom-in");
        var elapsed = Get<float>(table, "_lossElapsed");
        var depth = Get<float>(original, "_depth");
        Call(table, "SetPaused", true);
        Call(table, "Update");
        Check(Get<float>(table, "_lossElapsed") == elapsed && Get<float>(original, "_depth") == depth, "pause freezes zoom and sink");
        tableRect.sizeDelta = new Vector2(1280, 900);
        Call(table, "Resize");
        Check(ReferenceEquals(stickers[1], original), "resize preserves focused sticker");
        Call(table, "SetPaused", false);
        tableRect.sizeDelta = new Vector2(1600, 900);
        Call(table, "Resize");
        Call(table, "UpdatePresentation", .9f);
        Call(table, "RefreshForeground", 1f);
        Check(Mathf.Approximately(original.Root.localScale.x, 1.6f), "held close-up size");
        Check(original.Root.anchoredPosition == new Vector2(0, 30), "seat compensation does not shift cinematic centre");
        Check(Get<CanvasGroup>(table, "_cardGroup").alpha == 0, "cards leave room for cinematic caption");
        Check(!Get<RectTransform>(table, "_timerGroup").gameObject.activeSelf, "close-up reserves centre without timer");
        Save("closeup-" + count + ".png");
        Call(table, "UpdatePresentation", Get<float>(table, "_presentationRemaining") - .005f);
        var justBeforeReturn = original.Root.position;
        FinishPresentation(table);
        Check(Vector3.Distance(original.Root.position, justBeforeReturn) < 1f,
            "return finishes at raised seat without a position jump");
        Check(game.Now == now, "rules clock frozen throughout cutscene");
        Check(game.RoundNumber == oldRound + 1, "next round follows return");
        Check(original.Root.parent == anchor && original.Root.anchoredPosition == Vector2.up * original.SeatedLift * .7f,
            "return to exact lifted seat");
        Check(Get<CanvasGroup>(table, "_cardGroup").alpha == 1, "cards return after close-up");
        Check(ReferenceEquals(Get<GovnoLifeSticker[]>(table, "_seatStickers")[1], original), "same actor survives round change");
        Check(game.Players[1].Lives == 3 && Get<int>(original, "_lives") == 3, "life state carried into next round");
        Check(!Get<RectTransform>(table, "_timerGroup").gameObject.activeSelf, "new-round countdown hides picking timer");
        FinishPresentation(table);
        Check(Get<RectTransform>(table, "_timerGroup").gameObject.activeSelf, "picking timer returns after countdown");

        // No hand stack: two missed passes still receive the same visible plunge.
        for (var remaining = 2; remaining >= 0; remaining--)
        {
            Lose(table, 1, false);
            Check(Get<float>(table, "_lossLeadIn") < .2f, "timeout skips hand-stack wait");
            if (remaining == 0)
            {
                original.SetFloatingHeadwear(UiTextures.Circle(32, Color.white));
                Call(table, "UpdatePresentation", 1.65f);
                Check(Get<CanvasGroup>(original, "_hatGroup").alpha > .99f, "equipped hat floats after final plunge");
                original.SetFloatingHeadwear(null);
                Call(table, "RefreshForeground", 1f);
                Save("drowning-" + count + ".png");
                Check(!Get<RectTransform>(table, "_result").gameObject.activeSelf, "winner panel waits for final plunge");
            }
            FinishPresentation(table);
            if (remaining > 0 || !game.IsFinished) FinishPresentation(table);
        }
        Check(Get<float>(original, "_depth") >= Get<float>(original, "_targetDepth"), "drowned actor submerged");
        var pool = (RectTransform)original.Root.Find("Mud/MudBack");
        Check(Mathf.Abs(pool.localScale.x - 1.28f) < .01f, "final pool wider by 28 percent");
        Check(original.Root.parent == anchor && anchor.gameObject.activeSelf, "pool retains seat after elimination");
        Check(original.Root.anchoredPosition == Vector2.up * original.SeatedLift * .7f,
            "persistent grave keeps its raised seat position");
        original.SetFloatingHeadwear(UiTextures.Circle(32, Color.white));
        original.Tick(20f);
        Check(Get<CanvasGroup>(original, "_hatGroup").alpha > .99f, "hat does not expire");
        if (!game.IsFinished)
        {
            Lose(table, 2, false);
            FinishPresentation(table);
            FinishPresentation(table);
            Check(ReferenceEquals(Get<GovnoLifeSticker[]>(table, "_seatStickers")[1], original), "pool persists after another player loses");
            Check(Get<CanvasGroup>(original, "_hatGroup").alpha > .99f, "equipped hat persists across later rounds");
        }
        else Check(Get<RectTransform>(table, "_result").gameObject.activeSelf, "winners appear after cutscene");
        original.SetFloatingHeadwear(null);
        Call(table, "RefreshForeground", 1f);
        Save("pool-" + count + ".png");
        Call(table, "StartMatch");
        var reset = Get<GovnoLifeSticker[]>(table, "_seatStickers")[1];
        Check(!ReferenceEquals(reset, original) && Get<int>(reset, "_lives") == 4, "replay resets old grave and lives");
        Check(reset.Root.anchoredPosition == Vector2.zero && reset.SeatedLift == 0, "replay removes seated lift");
        Check(!Get<RectTransform>(table, "_lossLayer").gameObject.activeSelf, "replay clears cinematic layer");
    }

    private static void CheckIncomingReveal(bool chaos, bool humanTimeout)
    {
        var names = new[] { "Егор", "Али", "Аслан", "Данил", "Марат", "Ельнур" };
        // Find an ordinary deal where the incoming card completes the human's four-of-a-kind.
        // The real timeout auto-pass chooses its card, including when the human misses.
        var seed = 0;
        for (var candidate = 1; candidate <= 20000; candidate++)
        {
            var probe = new GovnoGame(names, candidate, chaos);
            if (!PassUntilFirstTimeout(probe, humanTimeout)) continue;
            probe.CompleteExchange();
            if (probe.Players[0].HasFourOfAKind) { seed = candidate; break; }
        }
        Check(seed != 0, "find real timeout deal completing human four-of-a-kind");

        Begin(1600, 900);
        var root = new GameObject("Incoming QA Table", typeof(RectTransform));
        root.transform.SetParent(canvasObject.transform, false);
        var rect = (RectTransform)root.transform;
        rect.sizeDelta = new Vector2(1600, 900);
        var table = root.AddComponent<GovnoTableController>();
        table.enabled = false;
        table.Configure(new GovnoMatchSettings { PlayerCount = names.Length }, () => { });
        Call(table, "Start");
        Call(table, "StartMatch");
        FinishPresentation(table);
        var game = new GovnoGame(names, seed, chaos);
        Set(table, "_game", game);
        // The isolated QA cache can contain archived costumes; use the shipped base figures.
        var ids = new[] { "yegor", "ali", "aslan", "danil", "marat", "yelnur" };
        var portraits = Get<Sprite[]>(table, "_portraits");
        for (var i = 0; i < ids.Length; i++)
            portraits[i] = Resources.Load<Sprite>("PlayerAvatars/" + ids[i] + "/" + ids[i] + "_01");
        Call(table, "BuildSeats");
        Set(table, "_shownExchange", -1);
        Set(table, "_scheduledExchange", -1);
        Get<System.Collections.IList>(table, "_actions").Clear();
        Call(table, "Render");
        Check(PassUntilFirstTimeout(game, humanTimeout), "prepare first human or bot timeout");
        Set(table, "_clock", game.Now);
        Call(table, "Render");
        Check(game.Players[humanTimeout ? 0 : 1].Warnings == 1, "first missed pass receives one warning");
        Check(Get<bool>(table, "_presentationActive") && !Get<bool>(table, "_countingDown"), "timeout notice precedes countdown");
        var slots = new Card?[4];
        var sprites = new Sprite[4];
        var cards = Get<CardView[]>(table, "_cards");
        for (var n = 0; n < 4; n++)
        {
            slots[n] = game.Players[0].Slots[n];
            sprites[n] = Get<Image>(cards[n], "_background").sprite;
        }
        Check(game.Players[0].Hand.Count == 3 && !game.Players[0].HasFourOfAKind,
            "fourth card remains unavailable during timeout notice");
        var revision = game.Revision;
        Call(table, "PressCentre");
        Call(table, "PassCard", 0);
        Check(game.Revision == revision, "notice blocks centre and card input without a false-start penalty");
        FinishPresentation(table);
        Set(table, "_clock", game.Now + .8);
        game.AdvanceTo(Get<double>(table, "_clock"));
        Call(table, "FinishExchange");
        Check(Get<bool>(table, "_presentationActive") && Get<bool>(table, "_countingDown"), "timeout starts 3-2-1 before receiving cards");
        Check(Get<bool>(table, "_countdownAfterExchange"), "incoming cards stay held for the whole countdown");
        var exchange = game.ExchangeNumber;
        var now = game.Now;
        var clock = Get<double>(table, "_clock");
        var flights = Get<System.Collections.IList>(table, "_flights");
        Check(flights.Count == names.Length, "each pending transfer retains its face-down table card");
        var flightSprites = new List<Sprite>();
        foreach (var flight in flights) flightSprites.Add(Get<Image>(Public<CardView>(flight, "View"), "_background").sprite);

        for (var step = 0; step < 3; step++)
        {
            Set(table, "_visualClock", Get<double>(table, "_visualClock") + 1);
            Call(table, "Animate");
            Check(game.Phase == GovnoPhase.Exchanging && game.ExchangeNumber == exchange,
                "countdown keeps the exchange pending at digit " + (3 - step));
            Check(game.Players[0].Hand.Count == 3 && !game.Players[0].HasFourOfAKind,
                "no early fourth-card knowledge at digit " + (3 - step));
            for (var n = 0; n < 4; n++)
                Check(Nullable.Equals(game.Players[0].Slots[n], slots[n])
                    && cards[n].gameObject.activeSelf == slots[n].HasValue
                    && Get<Image>(cards[n], "_background").sprite == sprites[n],
                    "existing three cards retain their faces and slots during countdown");
            for (var n = 0; n < flights.Count; n++)
            {
                var flight = flights[n];
                var view = Public<CardView>(flight, "View");
                var incoming = (Vector2)Call(table, "IncomingPosition", Public<int>(flight, "Receiver"));
                Check(Vector2.Distance(view.Rect.anchoredPosition, incoming) < .01f,
                    "pending card waits at incoming position instead of travelling to hand");
                Check(Get<Image>(view, "_background").sprite == flightSprites[n], "pending card stays face-down");
            }
            revision = game.Revision;
            Call(table, "PressCentre");
            Call(table, "PassCard", 0);
            Call(table, "ScheduleBots");
            Call(table, "FinishExchange");
            Check(game.Revision == revision && Get<System.Collections.IList>(table, "_actions").Count == 0,
                "countdown blocks human input, repeated completion, and bot scheduling");
            if (step == 1)
            {
                var remaining = Get<float>(table, "_presentationRemaining");
                Call(table, "SetPaused", true);
                Call(table, "Update");
                Check(Get<float>(table, "_presentationRemaining") == remaining && game.Now == now,
                    "pause freezes countdown and incoming exchange");
                rect.sizeDelta = new Vector2(1280, 900);
                Call(table, "Resize");
                Call(table, "SetPaused", false);
            }
            if (step < 2) Call(table, "UpdatePresentation", 1f);
        }
        rect.sizeDelta = new Vector2(1600, 900);
        Call(table, "Resize");
        Call(table, "Animate");
        Call(table, "RefreshForeground", 1f);
        if (!chaos && !humanTimeout) Save("incoming-countdown.png");
        Check(game.Now == now && Get<double>(table, "_clock") == clock, "countdown does not consume choice time");
        Call(table, "UpdatePresentation", .999f);
        Check(game.Players[0].Hand.Count == 3, "card remains hidden just before countdown ends");
        Call(table, "UpdatePresentation", .01f);
        Check(!Get<bool>(table, "_presentationActive") && !Get<bool>(table, "_countdownAfterExchange"), "countdown fully releases exchange hold");
        Check(game.Phase == GovnoPhase.Picking && game.ExchangeNumber == exchange + 1
            && game.Players[0].HasFourOfAKind, "four-of-a-kind becomes visible only when countdown finishes");
        Check(Math.Abs(game.Deadline - game.Now - game.PickDurationSeconds) < .00001,
            "revealed cards receive the full mode-specific choice deadline");
        foreach (var card in cards) Check(card.gameObject.activeSelf, "all four received cards now visible");
        Check(flights.Count == 0, "pending table cards cleared at reveal");
        foreach (var action in Get<System.Collections.IList>(table, "_actions"))
            Check(Public<double>(action, "At") > now && Public<int>(action, "Token") == game.ExchangeNumber,
                "bots get new decisions after reveal with their ordinary reaction delay");
        revision = game.Revision;
        Call(table, "FinishExchange");
        Check(game.Revision == revision, "countdown completes exchange exactly once");

        foreach (var id in game.ActivePlayers) game.TryPass(id, 0);
        Call(table, "Render");
        Call(table, "FinishExchange");
        Check(game.Phase == GovnoPhase.Picking && game.ExchangeNumber == exchange + 2
            && !Get<bool>(table, "_presentationActive"), "ordinary following exchange has no countdown or delayed reveal");
    }

    private static bool PassUntilFirstTimeout(GovnoGame game, bool humanTimeout)
    {
        var hand = game.Players[0].Slots;
        var odd = -1;
        var incomingSlot = -1;
        for (var n = 0; n < 4; n++)
        {
            var rank = hand[n].Value.Rank;
            var same = 0;
            for (var j = 0; j < 4; j++) if (hand[j].Value.Rank == rank) same++; else odd = j;
            if (same != 3) continue;
            var incoming = game.Players[game.RightOf(0)].Slots;
            for (var j = 0; j < 4; j++) if (incoming[j].Value.Rank == rank) incomingSlot = j;
            break;
        }
        if (incomingSlot < 0) return false;
        foreach (var id in game.ActivePlayers)
        {
            if (id == (humanTimeout ? 0 : 1)) continue;
            game.TryPass(id, id == 0 ? odd : id == game.RightOf(0) ? incomingSlot : 0);
        }
        game.AdvanceTo(game.Deadline);
        return game.Phase == GovnoPhase.Exchanging;
    }

    private static T Public<T>(object obj, string name) => (T)obj.GetType().GetField(name, Private | BindingFlags.Public).GetValue(obj);

    private static void CheckSeatStageAlignment(GovnoTableController table, RectTransform tableRect, int count)
    {
        var stickers = Get<GovnoLifeSticker[]>(table, "_seatStickers");
        var anchors = Get<RectTransform[]>(table, "_seatAnchors");
        var names = Get<Text[]>(table, "_seatLabels");
        var depths = Get<Text[]>(table, "_seatDepth");
        var warnings = Get<Text[]>(table, "_seatReady");
        var cards = Get<CardView[][]>(table, "_backs");
        var baseAnchors = new Vector2[count];
        var baseLabels = new Vector2[count];
        var baseDepths = new Vector2[count];
        var baseWarnings = new Vector2[count];
        var baseCentres = new float[count];
        var baseCardPositions = new Vector2[count][];
        var previousLift = new float[count];
        Canvas.ForceUpdateCanvases();
        for (var id = 0; id < count; id++)
        {
            baseAnchors[id] = anchors[id].anchoredPosition;
            baseLabels[id] = names[id].rectTransform.anchoredPosition;
            baseDepths[id] = depths[id].rectTransform.anchoredPosition;
            baseWarnings[id] = warnings[id].rectTransform.anchoredPosition;
            baseCentres[id] = WorldBounds(Get<Image>(stickers[id], "_stageImage").rectTransform).center.y;
            if (cards[id] == null) continue;
            baseCardPositions[id] = new Vector2[cards[id].Length];
            for (var n = 0; n < cards[id].Length; n++) baseCardPositions[id][n] = cards[id][n].Rect.anchoredPosition;
        }

        for (var lives = 4; lives >= 0; lives--)
        {
            foreach (var sticker in stickers) sticker.SetLives(lives, false);
            Call(table, "RepositionSeats");
            Canvas.ForceUpdateCanvases();
            for (var id = 0; id < count; id++)
            {
                var root = stickers[id].Root;
                var offset = root.anchoredPosition;
                Check(root.parent == anchors[id] && anchors[id].anchoredPosition == baseAnchors[id],
                    "sink keeps original seat anchor at stage " + lives);
                Check(offset.x == 0 && (lives == 4 ? offset.y == 0 : offset.y > previousLift[id]),
                    "whole scene rises progressively without a horizontal shift at stage " + lives);
                Check(names[id].rectTransform.anchoredPosition == baseLabels[id] + offset
                    && depths[id].rectTransform.anchoredPosition == baseDepths[id] + offset
                    && warnings[id].rectTransform.anchoredPosition == baseWarnings[id] + offset,
                    "name, depth and warning preserve spacing below raised pool at stage " + lives);
                var visibleCentre = lives > 0
                    ? WorldBounds(Get<Image>(stickers[id], "_stageImage").rectTransform).center.y
                    : Get<RectTransform>(stickers[id], "_poolBack").position.y;
                Check(Mathf.Abs(visibleCentre - baseCentres[id]) < 1f,
                    "visible figure or final pool stays at its original visual centre at stage " + lives);
                if (cards[id] != null)
                    for (var n = 0; n < cards[id].Length; n++)
                        Check(cards[id][n].Rect.anchoredPosition == baseCardPositions[id][n],
                            "opponent cards retain layout as scene rises");
                previousLift[id] = offset.y;
            }
        }

        // A partial seated transition follows the animated depth, not the new life count.
        var actor = stickers[1];
        actor.SetLives(4, false);
        Call(table, "RepositionSeats");
        actor.SetLives(3, true);
        Call(table, "RepositionSeats");
        Check(actor.Root.anchoredPosition == Vector2.zero, "new sink does not snap scene upward immediately");
        actor.Tick(GovnoLifeSticker.SinkSeconds / 2);
        Call(table, "RepositionSeats");
        var halfLift = actor.Root.anchoredPosition.y;
        Check(halfLift > 0, "seated lift follows an in-progress plunge");
        Call(table, "SetPaused", true);
        Call(table, "Update");
        Check(actor.Root.anchoredPosition.y == halfLift, "pause freezes seated lift");
        tableRect.sizeDelta = new Vector2(1280, 900);
        Call(table, "Resize");
        Check(actor.Root.anchoredPosition.y == halfLift, "resize retains partial lift without completing plunge");
        Call(table, "SetPaused", false);
        tableRect.sizeDelta = new Vector2(1600, 900);
        Call(table, "Resize");
        actor.Tick(GovnoLifeSticker.SinkSeconds / 2);
        Call(table, "RepositionSeats");
        Check(actor.Root.anchoredPosition.y > halfLift, "seated lift resumes smoothly after pause");

        var mixedStages = count == 3 ? new[] { 4, 2, 1 }
            : count == 4 ? new[] { 4, 2, 1, 0 } : new[] { 4, 3, 2, 1, 0, 2 };
        var captions = new[] { "Утонул", "По шею", "По пояс", "По колено", "На суше" };
        for (var id = 0; id < count; id++)
        {
            stickers[id].SetLives(mixedStages[id], false);
            depths[id].text = captions[mixedStages[id]];
            if (cards[id] != null)
                foreach (var card in cards[id]) card.gameObject.SetActive(mixedStages[id] > 0);
        }
        Call(table, "RepositionSeats");
        Save("sunk-seat-" + count + ".png");
        foreach (var sticker in stickers) sticker.SetLives(4, false);
        Call(table, "Render");
    }

    private static void CheckTimerLayout(GovnoTableController table)
    {
        Canvas.ForceUpdateCanvases();
        var timer = Get<RectTransform>(table, "_timerGroup");
        Check(timer.gameObject.activeSelf, "picking shows central timer");
        Check(timer.anchoredPosition == Vector2.zero, "timer remains at table centre on resize");
        Check(Get<Text>(table, "_timer").fontSize >= 70, "picking time has a prominent number");
        foreach (var graphic in timer.GetComponentsInChildren<Graphic>(true))
            Check(!graphic.raycastTarget, "timer graphic passes centre taps through");
        var zone = Get<RectTransform>(table, "_reactionZone");
        Check(WorldBounds(zone).Contains(WorldBounds(timer).center), "timer lies inside centre clap target");
        Check(zone.GetComponent<Image>().raycastTarget && Get<GovnoTapTarget>(table, "_zoneTap").Enabled,
            "centre clap target stays enabled while time counts down");
        foreach (var field in new[] { "_seatLabels", "_seatDepth", "_seatReady" })
            foreach (var label in Get<Text[]>(table, field))
                Check(!WorldBounds(timer).Overlaps(WorldBounds(label.rectTransform)), "timer clears seat text " + field);
        foreach (var sticker in Get<GovnoLifeSticker[]>(table, "_seatStickers"))
            Check(!WorldBounds(timer).Overlaps(WorldBounds(sticker.Root)), "timer clears compact seat figure");
        var positions = Get<Vector2[]>(table, "_seatPositions");
        if (positions.Length == 4 || positions.Length == 6)
            Check(positions[positions.Length / 2].y >= Get<RectTransform>(table, "_board").rect.height / 2 - 151,
                "upper middle player has extra centre clearance");
    }

    private static void CheckTimerPhases(GovnoTableController table, string[] names, int count)
    {
        var original = Get<GovnoGame>(table, "_game");
        var timer = Get<RectTransform>(table, "_timerGroup");
        foreach (var chaos in new[] { false, true })
        {
            var game = new GovnoGame(new List<string>(names).GetRange(0, count), 713, chaos);
            Set(table, "_game", game);
            Call(table, "RefreshClock");
            var fullBar = Get<Image>(table, "_timerFill").rectTransform.sizeDelta.x;
            Check(timer.gameObject.activeSelf && fullBar > 0, "timer starts full in both game speeds");
            game.AdvanceTo(game.PickDurationSeconds / 2);
            Call(table, "RefreshClock");
            Check(Mathf.Abs(Get<Image>(table, "_timerFill").rectTransform.sizeDelta.x - fullBar / 2) < .01f,
                "progress line follows remaining deadline in both game speeds");
            Call(table, "SetPaused", true);
            Check(!timer.gameObject.activeSelf, "pause menu hides timer immediately");
            Call(table, "SetPaused", false);
            Check(timer.gameObject.activeSelf, "resume restores timer immediately");
            game.AdvanceTo(game.Deadline - .25);
            Call(table, "RefreshClock");
            Check(Get<Text>(table, "_timer").color == Get<Image>(table, "_timerFill").color,
                "last fraction of a second highlights number and bar together");
            foreach (var player in game.ActivePlayers) game.TryPass(player, 0);
            Call(table, "RefreshClock");
            Check(game.Phase == GovnoPhase.Exchanging && !timer.gameObject.activeSelf, "exchange hides expired picking timer");
            game.CompleteExchange();
            Call(table, "RefreshClock");
            Check(timer.gameObject.activeSelf, "new exchange restores picking timer");
            game.AdvanceTo(game.Deadline);
            game.CompleteExchange();
            game.AdvanceTo(game.Deadline);
            Call(table, "RefreshClock");
            Check(game.Phase == GovnoPhase.ReactionCountdown && !timer.gameObject.activeSelf,
                "reaction countdown hides picking timer");
            game.AdvanceTo(game.Deadline);
            Call(table, "RefreshClock");
            Check(game.Phase == GovnoPhase.Reacting && !timer.gameObject.activeSelf,
                "reaction leaves centre free for hands");
        }
        Set(table, "_game", original);
        Call(table, "RefreshClock");
        var results = Get<RectTransform>(table, "_result");
        results.gameObject.SetActive(true);
        Call(table, "RefreshForeground", 0f);
        Check(!timer.gameObject.activeSelf, "results menu hides timer independently of phase");
        results.gameObject.SetActive(false);
        Call(table, "RefreshForeground", 0f);
    }

    private static Rect WorldBounds(RectTransform rect)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
    }

    private static void Lose(GovnoTableController table, int player, bool hands)
    {
        var game = Get<GovnoGame>(table, "_game");
        if (hands)
        {
            var order = Get<List<int>>(game, "_handOrder");
            order.Clear();
            for (var i = 0; i < game.Players.Count; i++) if (i != player) order.Add(i);
            order.Add(player);
        }
        Call(game, "LoseLife", player, hands ? GovnoLossReason.LastHand : GovnoLossReason.SecondTimeout);
        Call(table, "Render");
    }

    private static void FinishPresentation(GovnoTableController table)
    {
        if (Get<bool>(table, "_presentationActive"))
            Call(table, "UpdatePresentation", Get<float>(table, "_presentationRemaining") + .01f);
    }

    private static T Get<T>(object obj, string name) => (T)obj.GetType().GetField(name, Private).GetValue(obj);
    private static void Set(object obj, string name, object value) => obj.GetType().GetField(name, Private).SetValue(obj, value);
    private static object Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Private).Invoke(obj, args);
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
            var rt = camera.targetTexture;
            UnityEngine.Object.DestroyImmediate(camera.gameObject);
            UnityEngine.Object.DestroyImmediate(rt);
        }
        canvasObject = new GameObject("QA Canvas", typeof(RectTransform), typeof(Canvas));
        ((RectTransform)canvasObject.transform).sizeDelta = new Vector2(width, height);
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        camera = new GameObject("QA Camera", typeof(Camera)).GetComponent<Camera>();
        camera.transform.position = new Vector3(0, 0, -100);
        camera.orthographic = true;
        camera.orthographicSize = height / 2f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.075f, .25f, .18f);
        camera.nearClipPlane = .1f;
        camera.farClipPlane = 200;
        camera.targetTexture = new RenderTexture(width, height, 24);
        camera.targetTexture.Create();
        canvas.worldCamera = camera;
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
