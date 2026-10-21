// Companion partial for PlayerHubQa.cs: the Archive in the Old Town (2026-10-18). Copy with the other player-hub QA files
// to Assets/Scripts/Editor and run -executeMethod PlayerHubQa.RunArchive (HUB_QA_OUTPUT selects the screens subfolder).
// Own entry point and report, so PlayerHubQa.cs itself stays untouched. Memory-only profile and a fixed clock.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CardGame.Core.Meta;
using CardGameClient;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

public static partial class PlayerHubQa
{
    private const int ArchiveFloor = 8;

    public static void RunArchive()
    {
        Directory.CreateDirectory(Output);
        var previousError = Path.Combine(Output, "error.txt");
        if (File.Exists(previousError)) File.Delete(previousError);
        Application.logMessageReceived += OnLog;
        store = new MemoryStore(); clock = new FixedClock();
        PlayerHubRuntime.UseService(new LocalPlayerHubService(store, clock), clock);
        EditorApplication.update += FinishArchiveWhenReady;
        EditorApplication.delayCall += () => operation = RunArchiveChecks();
    }

    private static async Task RunArchiveChecks()
    {
        ArchiveStep("load");
        var loaded = await PlayerHubRuntime.LoadAsync();
        Check(loaded.Success, "profile initializes through the injected memory store");
        Begin(1600, 900);
        var root = new GameObject("Player hub QA", typeof(RectTransform));
        root.transform.SetParent(canvasObject.transform, false);
        ((RectTransform)root.transform).sizeDelta = new Vector2(1600, 900);
        hub = root.AddComponent<PlayerHubController>(); hub.enabled = false;
        hub.Configure(() => playCalls++, () => avatarCalls++, (game, count) => { launchedGame = game; launchedCount = count; },
            game => { quickGame = game; quickCalls++; });
        ArchiveStep("start");
        Call(hub, "Start");
        ArchiveStep("started");
        await WaitFor(() => Get<HubSnapshot>(hub, "_state") != null && !Get<bool>(hub, "_busy"), "initial async hub load");
        await CheckArchive();
        Check(RuntimeErrors.Count == 0, "no archive runtime exceptions: " + string.Join(" | ", RuntimeErrors));
    }

    private static HubSnapshot ArchiveState => Get<HubSnapshot>(hub, "_state");

    private static async Task CheckArchive()
    {
        hub.GoHome(); await NextFrame(); SettleOn(0);
        var area = Get<RectTransform>(hub, "_cityArea");
        var places = ((Array)typeof(PlayerHubController).GetField("CityPlaces", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null))
            .Cast<object>().ToArray();
        object PlaceById(string id) => places.Single(p => (string)Item(p, "Id") == id);
        int IndexOf(string id) => Array.FindIndex(places, p => (string)Item(p, "Id") == id);
        Vector2 ScreenOf(object place) => RectTransformUtility.WorldToScreenPoint(camera,
            area.TransformPoint(area.rect.center + (Vector2)Call(hub, "PlaceScreenPoint", place, Item(place, "Door"))));
        void Frame() { Call(hub, "BuildMapButton"); Call(hub, "UpdateInteriorControls"); Call(hub, "RefreshMapButton"); Call(hub, "UpdateArchive", .1f, false); }
        string[] Texts(string layer) => Get<RectTransform>(hub, layer).GetComponentsInChildren<Text>().Select(t => t.text).ToArray();
        bool Open(string what) => Get<string>(hub, "_archiveOpen") == what && Get<RectTransform>(hub, "_archiveLayer").gameObject.activeSelf;

        // Из Старого города — нырок в дверь бывшего полицейского поста.
        ArchiveStep("door");
        var archive = PlaceById("archive");
        Check((int)Item(archive, "Floor") == ArchiveFloor && Item(archive, "Route") == null, "the Archive door leads into its own floor, not to a page");
        TapScreen(area, ScreenOf(archive), "archive door");
        Check(Get<int>(hub, "_diveTarget") == ArchiveFloor, "a tap on the Archive dives through its door");
        var frames = 0;
        for (; Get<int>(hub, "_diveTarget") >= 0 && frames < 200; frames++)
        {
            Call(hub, "MoveDive", 1 / 60f);
            if (frames == 20) Save("archive-dive.png");
        }
        Check(Get<int>(hub, "_cityLevel") == ArchiveFloor && Get<string>(hub, "_page") == "home", "the dive lands inside the Archive (" + frames + " frames)");
        Frame();
        var exit = Get<RectTransform>(hub, "_interiorControls");
        Check(exit != null && exit.gameObject.activeSelf, "inside the Archive there is the usual leave button");
        var floors = Get<RectTransform[]>(hub, "_floors");
        Check(floors[ArchiveFloor] != null && floors[ArchiveFloor].gameObject.activeSelf && !Get<RectTransform>(hub, "_cityStrip").gameObject.activeSelf,
            "the street is replaced by the Archive hall");
        var bubble = Get<Text>(hub, "_archiveBubbleText");
        Check(bubble.gameObject.activeInHierarchy && bubble.text.Contains("2 уровне"), "the archivist greets and names the level of the next chapter: " + bubble.text);
        Check(places.Count(p => (string)Item(p, "Art") == "archive-hall") == 4, "four things to tap in the hall");
        InspectLayout("archive");
        Save("archive-hall-1600x900.png");
        Call(hub, "ShowPlaceOutline", IndexOf("archive-book"), 1f);
        Check(Get<HubPlaceOutline>(hub, "_placeOutline").gameObject.activeInHierarchy, "the chronicle is outlined like other places");
        Save("archive-hover.png");
        Call(hub, "ShowPlaceOutline", -1, 1f);

        // Туз червей отвечает на нажатие.
        ArchiveStep("ace");
        TapScreen(area, ScreenOf(PlaceById("archive-ace")), "Ace of Hearts");
        Check(bubble.text.StartsWith("Пыль не трогай", StringComparison.Ordinal), "a tap on the archivist gets a line");

        // Летопись: открыта на первой главе, вторая — под сургучом, стрелки и клавиши листают, Esc закрывает только книгу.
        TapScreen(area, ScreenOf(PlaceById("archive-book")), "chronicle");
        Frame();
        Check(Open("book") && Get<int>(hub, "_archiveSpread") == 0, "a tap on the book opens the chronicle at the newest open chapter");
        Check(!exit.gameObject.activeSelf && !Get<RectTransform>(hub, "_mapButtonLayer").gameObject.activeSelf, "leave and map are hidden behind the book");
        var book = Texts("_archiveBook");
        Check(book.Contains("Четыре туза") && book.Any(t => t.StartsWith("Давным-давно", StringComparison.Ordinal)) && book.Contains("ГЛАВА I ИЗ V"),
            "the first chapter is written on the spread");
        var prev = Get<Button>(hub, "_archivePrev");
        var next = Get<Button>(hub, "_archiveNext");
        Check(!prev.gameObject.activeSelf && next.gameObject.activeSelf, "only the forward arrow on the first spread");
        InspectLayout("archive-book");
        Save("archive-book.png");
        await ClickButton(next, "next spread");
        book = Texts("_archiveBook");
        Check(Get<int>(hub, "_archiveSpread") == 1 && book.Contains("Банк Туза треф") && book.Contains("Откроется на 2 уровне")
              && !book.Any(t => t.StartsWith("Туз треф первым", StringComparison.Ordinal)), "a closed chapter shows its title under a seal, not its text");
        Save("archive-book-locked.png");
        var keyboard = InputSystem.AddDevice<Keyboard>("Archive QA keyboard");
        try
        {
            keyboard.MakeCurrent();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.RightArrow)); InputSystem.Update(); Call(hub, "UpdateArchive", .1f, false);
            Call(hub, "UpdateArchive", .1f, false);
            Check(Get<int>(hub, "_archiveSpread") == 2, "the right arrow key turns one spread, holding it does not run through the book");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape)); InputSystem.Update();
            Call(hub, "UpdateCity", false); Call(hub, "UpdateArchive", .1f, false); Call(hub, "UpdateCity", false); Call(hub, "UpdateArchive", .1f, false);
            Check(!Open("book") && Get<int>(hub, "_cityLevel") == ArchiveFloor && Get<int>(hub, "_diveTarget") < 0, "held Escape closes only the book");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update(); Call(hub, "UpdateCity", false);
        }
        finally { InputSystem.RemoveDevice(keyboard); }
        Frame();
        Check(exit.gameObject.activeSelf && Get<RectTransform>(hub, "_mapButtonLayer").gameObject.activeSelf, "leave and map return after the book closes");

        ArchiveStep("torn");
        // На пятом уровне книга открывается на «Великой партии», у неё вырван конец.
        var level = ArchiveState.Profile.Level;
        ArchiveState.Profile.Level = 5;
        TapScreen(area, ScreenOf(PlaceById("archive-book")), "chronicle at level 5");
        Frame();
        book = Texts("_archiveBook");
        Check(Get<int>(hub, "_archiveSpread") == 3 && book.Contains("Великая партия") && book.Contains("Дальше страница вырвана.")
              && book.Any(t => t.EndsWith("И тогда Туз пик…", StringComparison.Ordinal)), "the Great Game breaks off on a torn page");
        Save("archive-book-torn.png");
        hub.Back(); Frame();
        Check(!Open("book") && Get<int>(hub, "_cityLevel") == ArchiveFloor, "Back closes the book first and stays in the Archive");
        ArchiveState.Profile.Level = level;

        ArchiveStep("cabinet");
        // Витрина: все достижения экспонатами, полученное можно забрать прямо тут.
        TapScreen(area, ScreenOf(PlaceById("archive-cabinet")), "cabinet");
        Frame();
        var all = ArchiveState.Achievements.Count;
        var exhibits = Get<RectTransform>(hub, "_archiveExhibits");
        Check(Open("cabinet") && exhibits.Cast<Transform>().Count(t => t.name.StartsWith("Exhibit ", StringComparison.Ordinal)) == Math.Min(all, 10)
              && Texts("_archiveShelf").Contains("Получено 0 из " + all), "the cabinet shows every achievement as an exhibit: open "
              + Get<string>(hub, "_archiveOpen") + ", exhibits " + exhibits.Cast<Transform>().Count(t => t.name.StartsWith("Exhibit ", StringComparison.Ordinal))
              + " of " + all + ", texts " + string.Join(" | ", Texts("_archiveShelf").Take(4)));
        hub.Back(); Frame();
        var skipped = await PlayerHubRuntime.ExecuteAsync(new HubCommand { Action = "tutorial_skip" });
        Check(skipped.Success, "skipping the tutorial unlocks an achievement");
        Set(hub, "_state", skipped.Snapshot); Call(hub, "Render"); Frame();
        Check(bubble.text == "На витрине тебя ждёт награда — забери!", "the archivist points to an unclaimed reward");
        TapScreen(area, ScreenOf(PlaceById("archive-cabinet")), "cabinet with a reward");
        Frame();
        var reward = ArchiveState.Achievements.Single(a => a.Id == "tutorial_skip");
        var claim = exhibits.GetComponentsInChildren<Button>().Single(b => ButtonLabel(b) == "ЗАБРАТЬ +" + reward.RewardCoins + " коинов");
        InspectLayout("archive-cabinet");
        Save("archive-cabinet.png");
        var coins = ArchiveState.Profile.Coins;
        await ClickButton(claim, "claim reward");
        Check(ArchiveState.Profile.Coins == coins + reward.RewardCoins && ArchiveState.Achievements.Single(a => a.Id == "tutorial_skip").Claimed,
            "claiming pays the reward");
        Check(Texts("_archiveShelf").Contains("Награда получена") && bubble.text.StartsWith("Награда твоя", StringComparison.Ordinal),
            "the exhibit is marked as paid and the archivist answers");
        hub.Back(); Frame();

        ArchiveStep("file");
        // Личное дело: из профиля, без выдумок.
        TapScreen(area, ScreenOf(PlaceById("archive-drawers")), "card catalogue");
        Frame();
        var file = Texts("_archiveFile");
        var state = ArchiveState;
        Check(Open("file") && file.Contains("ЛИЧНОЕ ДЕЛО") && file.Contains("«" + state.Profile.DisplayName + "»")
              && file.Contains(state.Achievements.Count(a => a.Unlocked) + " из " + state.Achievements.Count)
              && file.Contains(HubChronicle.OpenCount(state) + " из " + HubChronicle.Chapters.Length + " глав")
              && file.Contains(state.Bank.Contracts.Count == 0 ? "не брал" : "взято " + state.Bank.Contracts.Count),
            "the personal file is filled from the profile");
        InspectLayout("archive-file");
        Save("archive-file.png");
        hub.Back(); Frame();

        ArchiveStep("resize");
        // Широкий и маленький экраны: зал закрывает экран, книга помещается.
        foreach (var size in new[] { new Vector2Int(2000, 900), new Vector2Int(1280, 720) })
        {
            ResizeCanvas(size.x, size.y); ((RectTransform)hub.transform).sizeDelta = size; Call(hub, "Resize"); Frame();
            Canvas.ForceUpdateCanvases();
            var view = new Vector3[4]; ((RectTransform)hub.transform).GetWorldCorners(view);
            var art = new Vector3[4]; floors[ArchiveFloor].GetWorldCorners(art);
            Check(art[0].x <= view[0].x + 1 && art[0].y <= view[0].y + 1 && art[2].x >= view[2].x - 1 && art[2].y >= view[2].y - 1,
                "the Archive covers " + size.x + "x" + size.y);
            InspectLayout("archive-" + size.x + "x" + size.y);
            Save("archive-hall-" + size.x + "x" + size.y + ".png");
        }
        ResizeCanvas(1600, 900); ((RectTransform)hub.transform).sizeDelta = new Vector2(1600, 900); Call(hub, "Resize"); Frame();

        // «ВЫЙТИ» — нырок обратно к двери Архива.
        await ClickButton(exit.GetComponentsInChildren<Button>().Single(b => ButtonLabel(b) == "ВЫЙТИ"), "leave the Archive");
        Check(Get<int>(hub, "_diveTarget") == 0, "leave dives back to the street");
        Call(hub, "MoveDive", 10f); Frame();
        Check(Get<int>(hub, "_cityLevel") == 0 && Get<RectTransform>(hub, "_archiveBubble") != null, "back in the Old Town");

        ArchiveStep("map");
        // С карты: поездка к Архиву заканчивается внутри.
        Set(hub, "_mapTripDistrict", -1); Set(hub, "_mapTripPlace", IndexOf("archive")); SettleOn(3);
        for (var step = 0; step < 40 && Get<int>(hub, "_cityLevel") != ArchiveFloor; step++)
        {
            Call(hub, "ContinueMapTrip"); Call(hub, "MoveCity", .5f); Call(hub, "MoveDive", .5f);
        }
        Check(Get<int>(hub, "_cityLevel") == ArchiveFloor, "a map trip to the Archive ends inside it");
        DiveNow(0);
    }

    // Шаги — в журнал Unity: если проверка встанет, видно где.
    private static void ArchiveStep(string step) => Debug.Log("ARCHIVE QA step: " + step);

    private static void FinishArchiveWhenReady()
    {
        if (operation == null || !operation.IsCompleted) return;
        EditorApplication.update -= FinishArchiveWhenReady; Application.logMessageReceived -= OnLog;
        File.WriteAllLines(Path.Combine(Output, "layout-issues.txt"), LayoutIssues);
        if (operation.IsFaulted)
        {
            File.WriteAllText(Path.Combine(Output, "error.txt"), operation.Exception.ToString());
            Debug.LogException(operation.Exception); EditorApplication.Exit(1); return;
        }
        File.WriteAllText(Path.Combine(Output, "verification.txt"), checks + " assertions passed: the Archive door in the Old Town dives into "
            + "its hall, leave button and street hidden inside, archivist greeting with the next chapter level and a line on tap, outline on "
            + "hover, chronicle opened at the newest chapter, sealed chapter without its text, arrow buttons and keys turn one spread, held "
            + "Escape and Back close only the overlay, torn end of the Great Game, leave and map hidden behind overlays, cabinet with every "
            + "achievement, reward hint and claim with exact payment, personal file from the profile, coverage and layout at "
            + "1600x900/2000x900/1280x720, leave dives back, map trip ends inside. Layout issues: " + LayoutIssues.Count
            + ". Memory-only profile; no real save opened or written.");
        Debug.Log("ARCHIVE QA PASSED: " + checks); EditorApplication.Exit(0);
    }
}
