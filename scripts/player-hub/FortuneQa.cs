// Native Unity integration QA for the Queen of Spades. Uses injected memory persistence only.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CardGame.Core.Meta;
using CardGameClient;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

public static partial class PlayerHubQa
{
    private static readonly List<string> FortuneLayoutIssues = new List<string>();

    private static async Task CheckFortune()
    {
        var oldStore = store; var oldClock = clock;
        var previousEvents = EventSystem.current; var previousModule = events.currentInputModule;
        var registeredEvents = EventSystem.current != events;
        if (registeredEvents) Call(events, "OnEnable");
        EventSystem.current = events;
        var input = events.gameObject.AddComponent<BankLoanQaInput>();
        var module = events.gameObject.AddComponent<BankLoanQaInputModule>();
        module.inputOverride = input; Call(module, "OnEnable"); Call(events, "ChangeEventModule", module);
        FortuneLayoutIssues.Clear();
        try
        {
            await BankLoanProfile(2, 20);
            hub.GoHome(); await NextFrame(); DiveNow(0); SettleOn(0);
            var area = Get<RectTransform>(hub, "_cityArea");
            var places = ((Array)typeof(PlayerHubController).GetField("CityPlaces", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null)).Cast<object>().ToArray();
            var place = places.Single(p => (string)Item(p, "Id") == "fortune");
            var point = (Vector2)Call(hub, "PlaceScreenPoint", place, Item(place, "Door"));
            TapScreen(area, RectTransformUtility.WorldToScreenPoint(camera, area.TransformPoint(area.rect.center + point)), "Queen of Spades tent");
            Check(Get<int>(hub, "_diveTarget") == 5 && Get<int>(hub, "_visitPhase") == 0 && Get<string>(hub, "_page") == "home",
                "the street tent starts an interior dive without a separate place visit");
            for (var i = 0; i < 70; i++) Call(hub, "MoveDive", .02f);
            FortuneStep(.1f);
            Check(Get<string>(hub, "_page") == "fortune" && Get<int>(hub, "_cityLevel") == 5 && Get<int>(hub, "_diveTarget") < 0,
                "the completed tent entrance reveals its own interior and six-card table");
            Check(Get<int>(hub, "_visitPhase") == 0 && Vector3.Distance(Get<RectTransform>(hub, "_cityRig").localScale, Vector3.one) < .001f,
                "the tent entrance finishes without retaining the city-place zoom");
            Check(Get<Image[]>(hub, "_fortuneCards").Length == 6, "the table contains six individual face-down cards");
            var back = Resources.Load<Sprite>("UI/Hub/fortune-card-back");
            Check(back != null && Get<Image[]>(hub, "_fortuneCards").All(c => c.sprite == back)
                && Get<Button[]>(hub, "_fortuneCardButtons").All(b => b.IsInteractable()), "all six first-draw backs are visible and selectable");
            foreach (var size in FortuneSizes())
            {
                FortuneResize(size); InspectFortuneLayout(Get<RectTransform>(hub, "_fortuneLayer"), "spread " + size.x + "x" + size.y);
                if (size.x > size.y) InspectFortuneTabletop(size.x + "x" + size.y);
                Save("hub-fortune-spread-" + size.x + "x" + size.y + ".png");
            }
            FortuneResize(new Vector2Int(1600, 900));
            var money = PlayerHubRuntime.Current.Profile.Coins; var at = clock.Now;
            await ClickButton(Get<Button[]>(hub, "_fortuneCardButtons")[2], "choose the third fortune card");
            var drawn = PlayerHubRuntime.Current.Fortune;
            var effect = drawn.EffectId; var count = drawn.DrawCount;
            Check(count == 1 && drawn.SelectedCardIndex == 2 && drawn.DrawnAt == at && drawn.ReadyAt == at + HubFortunes.Cooldown,
                "one real click persists its selected card and exact 24-hour cooldown");
            Check(PlayerHubRuntime.Current.Profile.Coins == money, "the daily prediction is free");
            Check(!Get<bool>(hub, "_fortuneReadingShown") && Get<float>(hub, "_fortuneFlipTime") >= 0,
                "the saved choice begins a visible card flip before reading");
            FortuneStep(.28f);
            Check(Get<Image[]>(hub, "_fortuneCards")[2].rectTransform.localScale.x < 1,
                "the chosen card narrows during its flip");
            Save("hub-fortune-flip.png"); FortuneStep(1f);
            Check(Get<bool>(hub, "_fortuneReadingShown") && Get<float>(hub, "_fortuneFlipTime") < 0,
                "the flip ends on the readable saved prediction");
            Check(Get<Image>(hub, "_fortuneFace").sprite == Resources.Load<Sprite>(HubFortunes.Find(effect).Resource),
                "the revealed artwork matches the service-owned effect");
            await ClickButton(Get<Button>(hub, "_fortuneReadingBackButton"), "return to the six cards"); FortuneStep(.1f);
            Check(Get<Button[]>(hub, "_fortuneCardButtons").Count(b => b.IsInteractable()) == 1,
                "the saved card can be reread while five other choices remain locked");
            var denied = await PlayerHubRuntime.ExecuteAsync(new HubCommand { Action = "fortune_draw", Amount = 4 });
            Check(!denied.Success && PlayerHubRuntime.Current.Fortune.DrawCount == count && PlayerHubRuntime.Current.Fortune.EffectId == effect,
                "another command during cooldown cannot replace the chosen card");
            await ReloadFortuneFixture();
            hub.Navigate("place:fortune"); FortuneStep(.1f);
            Check(Get<string>(hub, "_page") == "fortune" && PlayerHubRuntime.Current.Fortune.EffectId == effect,
                "the old place route rereads the same saved card after a fresh Unity-JSON load");
            Call(hub, "ShowFortuneReading");
            await ClickButton(Get<Button>(hub, "_fortuneReadingBackButton"), "close the persisted reading");
            await ClickButton(Get<Button>(hub, "_fortuneReadLastButton"), "read My Card"); FortuneStep(.1f);
            Check(Get<bool>(hub, "_fortuneReadingShown"), "My Card opens the persisted prediction without drawing again");
            await CheckFortuneReturnFromGames();
            await CheckFortuneEscape();
            hub.GoHome(); Call(hub, "EndVisitNow"); FortuneStep(.1f);
            // The author removed the city badge (2026-10-09). CheckFortuneBadgeLayout stays for when it returns.
            Check(HubFortunes.Active(PlayerHubRuntime.Current.Fortune) && !FortuneBadgeShown(), "an active fortune shows no city badge");
            Call(hub, "OpenFortuneReadingRoute"); FortuneStep(.1f);
            Check(Get<string>(hub, "_page") == "fortune" && Get<bool>(hub, "_fortuneReadingShown"), "the reading route opens the same active reading");
            clock.Now = drawn.ReadyAt - 1; FortuneStep(.1f);
            Check(!HubFortunes.CanDraw(PlayerHubRuntime.Current.Fortune, clock.Now), "the final cooldown second still forbids a redraw");
            clock.Now++; Call(hub, "CloseFortuneReading"); FortuneStep(.1f);
            Check(Get<Button[]>(hub, "_fortuneCardButtons").All(b => b.IsInteractable()), "all six choices reopen at the exact 24-hour deadline");
            await ClickButton(Get<Button[]>(hub, "_fortuneCardButtons")[4], "take the next free daily card"); FortuneStep(1f);
            Check(PlayerHubRuntime.Current.Fortune.DrawCount == 2 && PlayerHubRuntime.Current.Fortune.SelectedCardIndex == 4,
                "the new daily card replaces the prior effect once");
            Call(hub, "CloseFortuneReading"); FortuneStep(.1f);
            var test = Get<Button>(hub, "_fortuneTestButton");
            Check(test.gameObject.activeInHierarchy && test.IsInteractable(), "editor test button offers a new card during the cooldown");
            InspectFortuneLayout(Get<RectTransform>(hub, "_fortuneLayer"), "test button 1600x900");
            Save("hub-fortune-test-button.png");
            await ClickButton(test, "test: new card now"); FortuneStep(.1f);
            await WaitFor(() => HubFortunes.CanDraw(PlayerHubRuntime.Current.Fortune, clock.Now) && !Get<bool>(hub, "_busy"), "test cooldown reset");
            FortuneStep(.1f);
            Check(!test.gameObject.activeInHierarchy && Get<Button[]>(hub, "_fortuneCardButtons").All(b => b.IsInteractable()),
                "after the test reset all six cards can be chosen again");

            foreach (var definition in HubFortunes.Definitions)
            {
                await SetFortuneFixture(definition.Id);
                foreach (var size in FortuneSizes())
                {
                    FortuneResize(size); Call(hub, "ShowFortuneReading"); FortuneStep(.05f);
                    Check(Get<Text>(hub, "_fortuneEffectText").text == HubFortunes.EffectText(PlayerHubRuntime.Current.Fortune),
                        "reading gives the exact saved rules for " + definition.Id);
                    Check(Get<Image>(hub, "_fortuneFace").sprite != null, "approved face imports for " + definition.Id);
                    InspectFortuneLayout(Get<RectTransform>(hub, "_fortuneReading"), definition.Id + " top " + size.x + "x" + size.y);
                    if (size.x == 960 || size.x == 2000) CheckFortuneImportantVisible(definition.Id + " initial " + size.x + "x" + size.y);
                    if (definition.Id == "helping-hand" || definition.Id == "golden-diamond") Save("hub-fortune-" + definition.Id + "-" + size.x + "x" + size.y + ".png");
                    var scroll = Get<HubSmoothScroll>(hub, "_fortuneReadingScroll");
                    scroll.StopMovement(); scroll.verticalNormalizedPosition = 0; Call(scroll, "LateUpdate");
                    InspectFortuneLayout(Get<RectTransform>(hub, "_fortuneReading"), definition.Id + " bottom " + size.x + "x" + size.y);
                    if (definition.Id == "helping-hand" && size.x == 960) await CheckFortuneScroll();
                }
                if (definition.Id == "new-road")
                {
                    FortuneResize(new Vector2Int(1600, 900));
                    foreach (var target in new[] { "durak", "bluff", "chests", "govno", "poker" })
                    {
                        await SetFortuneFixture("new-road", target);
                        await ClickButton(Get<Button>(hub, "_fortuneGamesButton"), "go to the new-road target " + target);
                        Check(Get<string>(hub, "_page") == "game:" + target, "new-road link reaches " + target);
                    }
                }
            }
            FortuneResize(new Vector2Int(1600, 900));
            await CheckFortunePayout();
            Check(FortuneLayoutIssues.Count == 0, "fortune screens have no clipping, blocked controls or overlap:\n" + string.Join("\n", FortuneLayoutIssues));
        }
        finally
        {
            hub.GoHome(); FortuneStep(.1f);
            store = oldStore; clock = oldClock;
            PlayerHubRuntime.UseService(new LocalPlayerHubService(store, clock), clock);
            var restored = await PlayerHubRuntime.LoadAsync(); Check(restored.Success, "fortune QA restores the previous injected memory profile");
            Call(hub, "OnSnapshot", restored.Snapshot);
            FortuneResize(new Vector2Int(1600, 900)); hub.GoHome(); DiveNow(0);
            System.IO.File.WriteAllLines(System.IO.Path.Combine(Output, "fortune-layout-issues.txt"), FortuneLayoutIssues);
            events.SetSelectedGameObject(null); Call(events, "ChangeEventModule", previousModule);
            UnityEngine.Object.DestroyImmediate(module); UnityEngine.Object.DestroyImmediate(input); events.UpdateModules();
            if (registeredEvents) Call(events, "OnDisable");
            if (previousEvents != null) EventSystem.current = previousEvents;
        }
    }

    private static Vector2Int[] FortuneSizes() => new[] { new Vector2Int(1600, 900), new Vector2Int(2000, 900), new Vector2Int(960, 540), new Vector2Int(600, 1000) };
    private static void FortuneResize(Vector2Int size)
    { ResizeCanvas(size.x, size.y); ((RectTransform)hub.transform).sizeDelta = size; Call(hub, "Resize"); FortuneStep(.05f); }
    private static void FortuneStep(float seconds)
    {
        for (var t = 0f; t < seconds; t += .02f) Call(hub, "UpdateFortune", Mathf.Min(.02f, seconds - t), false);
        Canvas.ForceUpdateCanvases();
    }
    private static async Task ReloadFortuneFixture()
    {
        PlayerHubRuntime.UseService(new LocalPlayerHubService(store, clock), clock);
        var loaded = await PlayerHubRuntime.LoadAsync(); Check(loaded.Success, "fortune fixture loads using Unity JSON in memory");
        Call(hub, "OnSnapshot", loaded.Snapshot); FortuneStep(.05f);
    }
    private static async Task SetFortuneFixture(string id, string target = "poker")
    {
        var copy = JsonUtility.FromJson<HubSnapshot>(JsonUtility.ToJson(PlayerHubRuntime.Current));
        var definition = HubFortunes.Find(id);
        copy.Fortune = new HubFortuneState { EffectId = id, DrawCount = 1, SelectedCardIndex = 2,
            DrawnAt = clock.Now, ReadyAt = clock.Now + HubFortunes.Cooldown, RemainingMatches = definition.Matches,
            TargetGame = id == "new-road" ? target : "" };
        await store.SaveAsync(copy); await ReloadFortuneFixture();
        hub.Navigate("fortune"); Call(hub, "ShowFortuneReading"); FortuneStep(.1f);
    }
    private static bool FortuneBadgeShown()
    { var layer = Get<RectTransform>(hub, "_fortuneBadgeLayer"); return layer != null && layer.gameObject.activeInHierarchy; }
    private static void CheckFortuneBadgeLayout()
    {
        foreach (var size in new[] { new Vector2Int(600, 1000), new Vector2Int(960, 540) })
        {
            FortuneResize(size); Call(hub, "RefreshMapButton"); Canvas.ForceUpdateCanvases();
            var badge = Get<Button>(hub, "_fortuneBadgeButton"); var rect = (RectTransform)badge.transform;
            var context = "city badge " + size.x + "x" + size.y;
            InspectFortuneLayout(Get<RectTransform>(hub, "_fortuneBadgeLayer"), context);
            var corners = new Vector3[4];
            foreach (var text in badge.GetComponentsInChildren<Text>().Where(t => !string.IsNullOrWhiteSpace(t.text)))
            {
                text.rectTransform.GetWorldCorners(corners);
                FortuneLayoutCheck(corners.All(c => rect.rect.Contains(rect.InverseTransformPoint(c))),
                    context + ": measured badge fully contains " + text.name);
            }
            foreach (var other in ActiveButtons().Where(b => b != badge && !b.transform.IsChildOf(badge.transform)))
                FortuneLayoutCheck(!FortuneWorldRect(rect).Overlaps(FortuneWorldRect((RectTransform)other.transform)),
                    context + ": badge does not collide with home action " + other.name);
            if (size.x == 600) Save("hub-fortune-city-badge-600x1000.png");
        }
        FortuneResize(new Vector2Int(1600, 900));
    }

    private static async Task CheckFortuneReturnFromGames()
    {
        var effect = PlayerHubRuntime.Current.Fortune.EffectId; var count = PlayerHubRuntime.Current.Fortune.DrawCount;
        Check(Get<int>(hub, "_cityLevel") == 5, "return regression starts in the physically entered fortune interior");
        await ClickButton(Get<Button>(hub, "_fortuneGamesButton"), "open games from the physical fortune interior"); FortuneStep(.05f);
        Check(Get<string>(hub, "_page") == "games" || Get<string>(hub, "_page").StartsWith("game:", StringComparison.Ordinal),
            "fortune reading opens its game destination");
        hub.Navigate("game:durak"); FortuneStep(.05f);
        Check(!Get<RectTransform>(hub, "_fortuneLayer").gameObject.activeSelf, "a game page hides the fortune interface");
        hub.GoHome(); Call(hub, "UpdateInteriorControls"); FortuneStep(.05f);
        Check(Get<string>(hub, "_page") == "fortune" && Get<int>(hub, "_cityLevel") == 5 && Get<int>(hub, "_diveTarget") < 0
            && Get<RectTransform>(hub, "_fortuneLayer").gameObject.activeInHierarchy,
            "GoHome after games restores the usable fortune interior instead of an empty room");
        Check(PlayerHubRuntime.Current.Fortune.EffectId == effect && PlayerHubRuntime.Current.Fortune.DrawCount == count,
            "returning from games preserves the saved prediction without a new draw");
        Call(hub, "ShowFortuneReading"); FortuneStep(.05f);
    }

    private static async Task CheckFortuneEscape()
    {
        var keyboard = InputSystem.AddDevice<Keyboard>("Fortune QA keyboard");
        try
        {
            keyboard.MakeCurrent(); InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape)); InputSystem.Update();
            FortuneStep(.1f);
            Check(Get<string>(hub, "_page") == "fortune" && !Get<bool>(hub, "_fortuneReadingShown")
                && Get<int>(hub, "_cityLevel") == 5 && Get<int>(hub, "_diveTarget") < 0, "Escape closes the reading while staying inside the tent");
            FortuneStep(.2f); Check(Get<string>(hub, "_page") == "fortune", "holding Escape does not also leave the tent");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update(); FortuneStep(.02f);
            await ClickButton(Get<Button>(hub, "_fortuneBackButton"), "leave the fortune table"); FortuneStep(.05f);
            Check(Get<string>(hub, "_page") == "home" && Get<int>(hub, "_cityLevel") == 5 && Get<int>(hub, "_diveTarget") == 0,
                "Back from the fortune table begins the normal interior exit");
            Call(hub, "UpdateInteriorControls");
            Check(Get<string>(hub, "_page") == "home" && Get<int>(hub, "_diveTarget") == 0,
                "idle-interior recovery does not interrupt an outbound fortune exit");
            for (var i = 0; i < 70; i++) Call(hub, "MoveDive", .02f);
            FortuneStep(.05f);
            Check(Get<int>(hub, "_cityLevel") == 0 && Get<int>(hub, "_diveTarget") < 0 && Get<int>(hub, "_visitPhase") == 0
                && Vector3.Distance(Get<RectTransform>(hub, "_cityRig").localScale, Vector3.one) < .001f,
                "the tent exit returns to the unzoomed city with no leftover transition");
        }
        finally { InputSystem.RemoveDevice(keyboard); }
    }

    private static async Task CheckFortunePayout()
    {
        await SetFortuneFixture("golden-diamond"); hub.GoHome(); FortuneStep(.1f);
        var before = PlayerHubRuntime.Current.Profile.Coins; var xp = PlayerHubRuntime.Current.Profile.XP;
        var start = await PlayerHubRuntime.ExecuteAsync(new HubCommand { Id = "fortune-qa-staked-match", Action = "begin_match", Target = "durak", Amount = 100 });
        Check(start.Success, "native fortune test reserves a real 100-coin match");
        var token = start.Snapshot.ActiveMatchId; PlayerHubRuntime.BeginGame("durak", token);
        HubMatchReporter.ReportMatchCompleted("durak", true);
        await WaitFor(() => !PlayerHubRuntime.HasPendingResult && PlayerHubRuntime.Current.Matches.Any(m => m.Id == token && m.Completed), "fortune match result commits");
        Check(PlayerHubRuntime.Current.Profile.Coins == before - 100 + 200 + 96 && PlayerHubRuntime.Current.Profile.XP == xp + 40,
            "golden diamond raises ordinary reward to 96 without increasing the 200-coin wager payout");
        Check(PlayerHubRuntime.Current.Fortune.RemainingMatches == 2 && PlayerHubRuntime.Current.Fortune.LastCoinsAdjustment == 16,
            "the real result consumes one use and records the exact +16 adjustment");
        var resultHost = new GameObject("Fortune result QA", typeof(RectTransform), typeof(Text));
        resultHost.transform.SetParent(canvasObject.transform, false); var text = resultHost.GetComponent<Text>(); text.font = Get<Font>(hub, "_font");
        HubQuestResultLine.Attach(text, () => true); var line = resultHost.GetComponentInChildren<HubQuestResultLine>(); var label = line.GetComponent<Text>();
        Call(line, "OnEnable"); Call(line, "Update");
        try
        {
            Check(label.text.Contains("Золотая бубна") && label.text.Contains("+16 тун-тун коинов"), "saved result line displays the actual fortune adjustment");
            var total = PlayerHubRuntime.Current.Profile.Coins;
            var replay = await PlayerHubRuntime.ExecuteAsync(new HubCommand { Action = "report_match", Target = "durak", Text = token, Flag = true });
            Check(replay.Success && PlayerHubRuntime.Current.Profile.Coins == total && PlayerHubRuntime.Current.Fortune.RemainingMatches == 2,
                "replayed match result cannot pay or consume fortune twice");
            Check(label.text.Contains("+16 тун-тун коинов"), "duplicate result retains the same truthful last-match line");
            Check(await PlayerHubRuntime.ExitGameAsync(), "settled fortune match exits cleanly");
            for (var i = 0; i < 2; i++)
            {
                var begun = await PlayerHubRuntime.BeginGameAsync("durak"); Check(begun.Success, "next fortune match begins");
                Call(line, "Refresh", PlayerHubRuntime.Current);
                Check(!label.text.Contains("Золотая бубна"), "starting another match hides the previous fortune payout");
                HubMatchReporter.ReportMatchCompleted("durak", false);
                await WaitFor(() => string.IsNullOrEmpty(PlayerHubRuntime.Current.ActiveMatchId) && !PlayerHubRuntime.HasPendingResult, "remaining fortune match settles");
                Check(await PlayerHubRuntime.ExitGameAsync(), "next settled fortune match exits");
            }
            FortuneStep(.1f);
            Check(!HubFortunes.Active(PlayerHubRuntime.Current.Fortune) && !FortuneBadgeShown(),
                "the third completed game exhausts the effect and no city badge appears");
            var stale = JsonUtility.FromJson<HubSnapshot>(JsonUtility.ToJson(PlayerHubRuntime.Current));
            stale.Matches.Add(new HubMatchEntry { Id = "fortune-qa-collector", Game = "durak", Completed = true, IsCollector = true });
            Call(line, "Refresh", stale);
            Check(!label.text.Contains("Золотая бубна"), "a collector result never repeats an older fortune payout");
        }
        finally { Call(line, "OnDisable"); UnityEngine.Object.DestroyImmediate(resultHost); }
        await SetFortuneFixture("stingy-fate"); hub.GoHome(); FortuneStep(.1f);
        var balance = PlayerHubRuntime.Current.Profile.Coins;
        Check((await PlayerHubRuntime.BeginGameAsync("chests")).Success, "negative-fortune match begins in another integrated game");
        HubMatchReporter.ReportMatchCompleted("chests", true, 1);
        await WaitFor(() => string.IsNullOrEmpty(PlayerHubRuntime.Current.ActiveMatchId) && !PlayerHubRuntime.HasPendingResult, "negative fortune result commits");
        Check(PlayerHubRuntime.Current.Profile.Coins == balance + 64 && PlayerHubRuntime.Current.Fortune.LastCoinsAdjustment == -16
            && PlayerHubRuntime.Current.Fortune.RemainingMatches == 0, "negative prediction reduces only the ordinary win reward and expires after one game");
        Check(await PlayerHubRuntime.ExitGameAsync(), "negative prediction game exits cleanly");
    }

    private static async Task CheckFortuneScroll()
    {
        var scroll = Get<HubSmoothScroll>(hub, "_fortuneReadingScroll");
        var bar = Get<HubDragScrollbar>(hub, "_fortuneReadingScrollbar");
        scroll.StopMovement(); scroll.verticalNormalizedPosition = 1; Call(scroll, "LateUpdate");
        var origin = scroll.content.anchoredPosition.y;
        var travel = Mathf.Max(0, scroll.content.rect.height - scroll.viewport.rect.height);
        var expectedWheel = Mathf.Min(scroll.scrollSensitivity, travel);
        Check(travel > 2, "scroll fixture has actual overflow: content=" + scroll.content.rect.height + ", viewport=" + scroll.viewport.rect.height);
        var wheel = new PointerEventData(events) { scrollDelta = new Vector2(0, -1) };
        ExecuteEvents.Execute(scroll.gameObject, wheel, ExecuteEvents.scrollHandler);
        Check(Mathf.Abs(scroll.content.anchoredPosition.y - origin) < .01f, "fortune wheel starts without an abrupt jump");
        for (var i = 0; i < 30; i++) Call(scroll, "AdvanceWheel", .02f);
        var afterWheel = scroll.content.anchoredPosition.y;
        var measurement = "content=" + scroll.content.rect.height.ToString("F2") + ", viewport=" + scroll.viewport.rect.height.ToString("F2")
            + ", travel=" + travel.ToString("F2") + ", before=" + origin.ToString("F2") + ", after=" + afterWheel.ToString("F2")
            + ", wheel target distance=" + expectedWheel.ToString("F2");
        System.IO.File.WriteAllText(System.IO.Path.Combine(Output, "fortune-scroll-metrics.txt"), measurement);
        Check(afterWheel > origin + expectedWheel * .4f && afterWheel <= origin + expectedWheel + .5f,
            "fortune wheel moves smoothly within the available content: " + measurement);
        scroll.StopMovement(); scroll.verticalNormalizedPosition = 1; Call(scroll, "LateUpdate");
        var start = RectTransformUtility.WorldToScreenPoint(camera, scroll.viewport.TransformPoint(scroll.viewport.rect.center));
        var pointer = new PointerEventData(events) { button = PointerEventData.InputButton.Left, position = start, pressPosition = start,
            pointerPressRaycast = new RaycastResult { module = canvasObject.GetComponent<GraphicRaycaster>() } };
        ExecuteEvents.Execute(scroll.gameObject, pointer, ExecuteEvents.initializePotentialDrag);
        ExecuteEvents.Execute(scroll.gameObject, pointer, ExecuteEvents.beginDragHandler);
        pointer.position += Vector2.up * 150;
        ExecuteEvents.Execute(scroll.gameObject, pointer, ExecuteEvents.dragHandler);
        ExecuteEvents.Execute(scroll.gameObject, pointer, ExecuteEvents.endDragHandler);
        scroll.StopMovement(); Check(scroll.content.anchoredPosition.y > origin + Mathf.Min(50, travel * .5f),
            "finger dragging reaches later fortune text within the available travel: " + travel.ToString("F2"));
        scroll.verticalNormalizedPosition = 1; Call(scroll, "LateUpdate"); Canvas.ForceUpdateCanvases();
        Check(bar.gameObject.activeInHierarchy && bar.IsInteractable() && scroll.verticalScrollbar == bar,
            "long fortune reading has a live right scrollbar");
        var handle = bar.handleRect; var hit = Hit(handle);
        Check(hit.gameObject != null && ExecuteEvents.GetEventHandler<IDragHandler>(hit.gameObject) == bar.gameObject,
            "pointer hits the scrollbar handle");
        var grab = new PointerEventData(events) { button = PointerEventData.InputButton.Left, position = hit.screenPosition,
            pressPosition = hit.screenPosition, pointerCurrentRaycast = hit, pointerPressRaycast = hit };
        ExecuteEvents.Execute(bar.gameObject, grab, ExecuteEvents.pointerDownHandler);
        ExecuteEvents.Execute(bar.gameObject, grab, ExecuteEvents.initializePotentialDrag);
        ExecuteEvents.Execute(bar.gameObject, grab, ExecuteEvents.beginDragHandler);
        grab.position -= Vector2.up * 140;
        ExecuteEvents.Execute(bar.gameObject, grab, ExecuteEvents.dragHandler);
        ExecuteEvents.Execute(bar.gameObject, grab, ExecuteEvents.pointerUpHandler);
        ExecuteEvents.Execute(bar.gameObject, grab, ExecuteEvents.endDragHandler);
        Check(scroll.verticalNormalizedPosition < .8f, "dragging the right handle scrolls toward the end");
        var stopped = scroll.content.anchoredPosition.y;
        for (var i = 0; i < 20; i++) Call(scroll, "AdvanceWheel", .02f);
        Check(Mathf.Abs(scroll.content.anchoredPosition.y - stopped) < .1f, "releasing the fortune handle does not snap back");
        Save("hub-fortune-reading-scrollbar.png"); await NextFrame();
    }

    private static void InspectFortuneTabletop(string context)
    {
        Canvas.ForceUpdateCanvases(); camera.Render();
        var tent = Get<RectTransform>(hub, "_fortuneTent"); var image = tent.GetComponent<Image>();
        var canvas = (RectTransform)canvasObject.transform; var corners = new Vector3[4]; tent.GetWorldCorners(corners);
        var screen = canvas.rect; screen.xMin -= 1; screen.yMin -= 1; screen.xMax += 1; screen.yMax += 1;
        var local = corners.Select(c => (Vector2)canvas.InverseTransformPoint(c)).ToArray();
        FortuneLayoutCheck(local.Min(c => c.x) <= screen.xMin + 2 && local.Max(c => c.x) >= screen.xMax - 2
            && local.Min(c => c.y) <= screen.yMin + 2 && local.Max(c => c.y) >= screen.yMax - 2,
            context + ": the tent fills the whole viewport without empty bands");
        var captions = new[] { "_fortuneInstruction", "_fortuneStatusText" }.Select(field => Get<Text>(hub, field))
            .Where(label => label.gameObject.activeInHierarchy && !string.IsNullOrWhiteSpace(label.text))
            .Select(label => { var b = label.canvasRenderer.GetMesh().bounds; var a = label.transform.TransformPoint(b.min); var z = label.transform.TransformPoint(b.max);
                return (Label: label, Bounds: Rect.MinMaxRect(Mathf.Min(a.x, z.x), Mathf.Min(a.y, z.y), Mathf.Max(a.x, z.x), Mathf.Max(a.y, z.y))); }).ToArray();
        var sourceAspect = image.sprite.rect.width / image.sprite.rect.height;
        FortuneLayoutCheck(Mathf.Abs(tent.rect.width / tent.rect.height - sourceAspect) < .002f,
            context + ": the tent keeps the original artwork proportions");
        var cards = Get<Image[]>(hub, "_fortuneCards");
        // Velvet bounds are measured in the approved picture, which is a part of the zoomed-out tent.
        var old = (Rect)typeof(PlayerHubController).GetField("FortuneOldArt", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null);
        foreach (var card in cards)
        {
            var mesh = card.canvasRenderer.GetMesh();
            FortuneLayoutCheck(mesh.vertexCount > 4, context + ": card has a rendered tabletop mesh: " + card.name);
            var outside = mesh.vertices.Select(vertex => tent.InverseTransformPoint(card.transform.TransformPoint(vertex)))
                .Select(local => new Vector2((local.x - tent.rect.xMin) / tent.rect.width, (tent.rect.yMax - local.y) / tent.rect.height))
                .Select(uv => new Vector2((uv.x - old.x) / old.width, (uv.y - old.y) / old.height))
                .Where(uv => uv.x < .13f || uv.x > .87f || uv.y < .64f || uv.y > .95f).ToArray();
            FortuneLayoutCheck(outside.Length == 0, context + ": projected card and contact shadow stay on the velvet: " + card.name
                + (outside.Length == 0 ? "" : " outside at " + string.Join(", ", outside.Take(3))));
            var world = mesh.vertices.Select(vertex => card.transform.TransformPoint(vertex)).ToArray();
            var cardBounds = Rect.MinMaxRect(world.Min(v => v.x), world.Min(v => v.y), world.Max(v => v.x), world.Max(v => v.y));
            FortuneLayoutCheck(world.All(v => screen.Contains(canvas.InverseTransformPoint(v))), context + ": projected card stays on screen: " + card.name);
            foreach (var caption in captions)
                FortuneLayoutCheck(!caption.Bounds.Overlaps(cardBounds), context + ": caption stays clear of the card: " + caption.Label.name + " / " + card.name);
            var rect = (RectTransform)card.transform; var button = card.GetComponent<Button>(); var hit = Hit(rect);
            FortuneLayoutCheck(button != null && hit.gameObject != null && (hit.gameObject == button.gameObject || hit.gameObject.transform.IsChildOf(button.transform)),
                context + ": projected card receives a real tap: " + card.name);
        }
    }

    private static void CheckFortuneImportantVisible(string context)
    {
        var viewport = Get<RectTransform>(hub, "_fortuneReadingViewport");
        var bounds = viewport.rect; bounds.xMin -= 1; bounds.yMin -= 1; bounds.xMax += 1; bounds.yMax += 1;
        var corners = new Vector3[4];
        foreach (var field in new[] { "_fortuneReadingTitle", "_fortuneEffectText", "_fortuneRemainingText" })
        {
            var label = Get<Text>(hub, field); label.rectTransform.GetWorldCorners(corners);
            FortuneLayoutCheck(corners.All(c => bounds.Contains(viewport.InverseTransformPoint(c))),
                context + ": essential prediction is immediately readable without scrolling: " + label.name);
        }
    }

    private static void FortuneLayoutCheck(bool valid, string description)
    {
        checks++; if (valid) return;
        FortuneLayoutIssues.Add(description); LayoutIssues.Add(description);
        Debug.LogWarning("Fortune layout: " + description);
    }
    private static Rect FortuneWorldRect(RectTransform rect)
    { var corners = new Vector3[4]; rect.GetWorldCorners(corners); return Rect.MinMaxRect(corners.Min(c => c.x), corners.Min(c => c.y), corners.Max(c => c.x), corners.Max(c => c.y)); }
    private static void InspectFortuneLayout(RectTransform layer, string context)
    {
        Canvas.ForceUpdateCanvases(); camera.Render();
        var canvas = (RectTransform)canvasObject.transform; var corners = new Vector3[4];
        var controls = layer.GetComponentsInChildren<Button>().Where(b => b.gameObject.activeInHierarchy && b.IsInteractable()).ToArray();
        foreach (var control in controls)
        {
            var rect = (RectTransform)control.transform; rect.GetWorldCorners(corners);
            FortuneLayoutCheck(corners.All(c => canvas.rect.Contains(canvas.InverseTransformPoint(c))), context + ": on-screen action " + control.name);
            var hit = Hit(rect);
            FortuneLayoutCheck(hit.gameObject != null && (hit.gameObject == control.gameObject || hit.gameObject.transform.IsChildOf(control.transform)),
                context + ": unblocked action " + control.name);
        }
        for (var i = 0; i < controls.Length; i++) for (var j = i + 1; j < controls.Length; j++)
            FortuneLayoutCheck(!FortuneWorldRect((RectTransform)controls[i].transform).Overlaps(FortuneWorldRect((RectTransform)controls[j].transform)),
                context + ": separated controls " + controls[i].name + " / " + controls[j].name);
        var glyphs = new List<(Text Label, Rect Bounds)>();
        foreach (var label in layer.GetComponentsInChildren<Text>().Where(t => t.gameObject.activeInHierarchy && !string.IsNullOrWhiteSpace(t.text)))
        {
            var rect = label.rectTransform.rect; var settings = label.GetGenerationSettings(new Vector2(rect.width, 0));
            var height = label.cachedTextGeneratorForLayout.GetPreferredHeight(label.text, settings) / label.pixelsPerUnit;
            FortuneLayoutCheck(height <= rect.height + 2, context + ": text fits height " + label.name);
            var mask = label.GetComponentInParent<RectMask2D>();
            if (mask != null && !mask.rectTransform.rect.Contains(mask.rectTransform.InverseTransformPoint(label.rectTransform.TransformPoint(rect.center)))) continue;
            var mesh = label.canvasRenderer.GetMesh(); var bounds = mesh.bounds;
            FortuneLayoutCheck(mesh.vertexCount > 0 && bounds.min.x >= rect.xMin - 3 && bounds.max.x <= rect.xMax + 3
                && bounds.min.y >= rect.yMin - 3 && bounds.max.y <= rect.yMax + 3, context + ": glyphs fit " + label.name);
            if (mask == null)
            {
                label.rectTransform.GetWorldCorners(corners);
                FortuneLayoutCheck(corners.All(c => canvas.rect.Contains(canvas.InverseTransformPoint(c))), context + ": on-screen text " + label.name);
            }
            var min = label.transform.TransformPoint(bounds.min); var max = label.transform.TransformPoint(bounds.max);
            glyphs.Add((label, Rect.MinMaxRect(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y), Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y))));
            if (label.name == "Exact fortune effect")
            {
                var em = Vector2.Distance(RectTransformUtility.WorldToScreenPoint(camera, label.transform.position),
                    RectTransformUtility.WorldToScreenPoint(camera, label.transform.TransformPoint(Vector3.up * label.fontSize)));
                FortuneLayoutCheck(em >= 19.9f, context + ": effect keeps at least 20 physical pixels");
            }
        }
        for (var i = 0; i < glyphs.Count; i++) for (var j = i + 1; j < glyphs.Count; j++)
            FortuneLayoutCheck(!glyphs[i].Bounds.Overlaps(glyphs[j].Bounds), context + ": separated text " + glyphs[i].Label.name + " / " + glyphs[j].Label.name);
    }
}
