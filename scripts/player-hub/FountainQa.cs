// Companion partial for PlayerHubQa.cs: walking up to the fountain (2026-10-24). Copy with the other player-hub QA files
// to Assets/Scripts/Editor and run -executeMethod PlayerHubQa.RunFountain (HUB_QA_OUTPUT selects the screens subfolder).
// Saves every frame of the walk in and out (30 fps) to <output>/frames for a preview video. Memory-only profile.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CardGame.Core.Meta;
using CardGameClient;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static partial class PlayerHubQa
{
    public static void RunFountain()
    {
        Directory.CreateDirectory(Output);
        var previousError = Path.Combine(Output, "error.txt");
        if (File.Exists(previousError)) File.Delete(previousError);
        Application.logMessageReceived += OnLog;
        store = new MemoryStore(); clock = new FixedClock();
        PlayerHubRuntime.UseService(new LocalPlayerHubService(store, clock), clock);
        EditorApplication.update += FinishFountainWhenReady;
        EditorApplication.delayCall += () => operation = RunFountainChecks();
    }

    private static async Task RunFountainChecks()
    {
        var loaded = await PlayerHubRuntime.LoadAsync();
        Check(loaded.Success, "profile initializes through the injected memory store");
        Begin(1600, 900);
        var root = new GameObject("Player hub QA", typeof(RectTransform));
        root.transform.SetParent(canvasObject.transform, false);
        ((RectTransform)root.transform).sizeDelta = new Vector2(1600, 900);
        hub = root.AddComponent<PlayerHubController>(); hub.enabled = false;
        hub.Configure(() => playCalls++, () => avatarCalls++, (game, count) => { launchedGame = game; launchedCount = count; },
            game => { quickGame = game; quickCalls++; });
        Call(hub, "Start");
        await WaitFor(() => Get<HubSnapshot>(hub, "_state") != null && !Get<bool>(hub, "_busy"), "initial async hub load");
        await CheckFountain();
        Check(RuntimeErrors.Count == 0, "no fountain runtime exceptions: " + string.Join(" | ", RuntimeErrors));
    }

    private static async Task CheckFountain()
    {
        hub.GoHome(); await NextFrame(); SettleOn(1);
        // Картинки города грузятся в фоне: дать им догрузиться, иначе первые кадры белые.
        for (var i = 0; i < 120; i++) await NextFrame();
        var area = Get<RectTransform>(hub, "_cityArea");
        Check(Get<Image>(hub, "_fountainClose") == null,
            "the original square has no close-up overlay before the first visit");
        Check(!Get<RectTransform>(hub, "_cityStrip").GetComponentsInChildren<Image>(true)
            .Any(i => i.sprite != null && i.sprite.name == "fountain-close"),
            "no enlarged fountain is pasted into the city panorama");
        var places = ((Array)typeof(PlayerHubController).GetField("CityPlaces", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null))
            .Cast<object>().ToArray();
        var fountain = places.Single(p => (string)Item(p, "Id") == "fountain");
        foreach (var point in new[] { new Vector2(100, 640), new Vector2(300, 660), new Vector2(182, 465), new Vector2(182, 665) })
        {
            var target = (int)Call(hub, "PlaceAt", Call(hub, "PlaceScreenPoint", fountain, point));
            Check(target >= 0 && (string)Item(places[target], "Id") == "fountain", "the visible fountain accepts taps at " + point);
        }
        var shop = places.Single(p => (string)Item(p, "Id") == "shop");
        var shopTarget = (int)Call(hub, "PlaceAt", Call(hub, "PlaceScreenPoint", shop, Item(shop, "Door")));
        Check(shopTarget >= 0 && (string)Item(places[shopTarget], "Id") == "shop", "the fountain leaves the shop entrance accessible");
        var screen = RectTransformUtility.WorldToScreenPoint(camera,
            area.TransformPoint(area.rect.center + (Vector2)Call(hub, "PlaceScreenPoint", fountain, Item(fountain, "Door"))));
        var frames = Path.Combine(Output, "frames");
        Directory.CreateDirectory(frames);
        foreach (var old in Directory.GetFiles(frames, "*.png")) File.Delete(old);
        var frame = 0;
        void Shot() => Save(Path.Combine("frames", "f" + (frame++).ToString("000") + ".png"));
        for (var i = 0; i < 8; i++) Shot();

        TapScreen(area, screen, "fountain");
        Check(Get<int>(hub, "_visitPhase") == 1, "a tap on the fountain starts walking up to it");
        var detail = Get<Image>(hub, "_fountainClose");
        Check(detail != null && detail.color.a == 0 && detail.transform.parent == area,
            "close-up starts transparent in its own background layer");
        var previousAlpha = 0f;
        for (var i = 0; i < 120 && Get<int>(hub, "_visitPhase") == 1; i++)
        {
            Call(hub, "MoveVisit", 1 / 30f); Call(hub, "UpdateFountain"); Shot(); await NextFrame();
            Check(detail.color.a >= previousAlpha, "entry dissolves towards the close-up without flashing backwards");
            previousAlpha = detail.color.a;
        }
        Check(Get<int>(hub, "_visitPhase") == 2, "the walk ends at the fountain");
        for (var i = 0; i < 20; i++) { Call(hub, "MoveVisit", 1 / 30f); Call(hub, "UpdateFountain"); Shot(); await NextFrame(); }
        var rig = Get<RectTransform>(hub, "_cityRig");
        Check(rig.localScale.x <= 1.31f && Mathf.Approximately(rig.localScale.x, rig.localScale.y),
            "the street approach stays modest and does not stretch its artwork");
        Check(detail.gameObject.activeInHierarchy && detail.color.a == 1 && detail.GetComponent<HubWalkFeather>() == null,
            "at the rim the complete close-up background replaces the street view");
        var restingPosition = rig.anchoredPosition;
        var restingScale = rig.localScale;
        Call(hub, "MoveVisit", .1f);
        Check(Vector2.Distance(restingPosition, rig.anchoredPosition) < .01f && Vector3.Distance(restingScale, rig.localScale) < .001f,
            "idle holds the arrival pose without a second update correcting it");
        foreach (var progress in new[] { 0f, .1f, .35f, .6f, .85f, 1f })
        {
            typeof(PlayerHubController).GetField("_visitPhase", Fields).SetValue(hub, 1);
            Call(hub, "FrameFountainWalk", progress);
            var position = rig.anchoredPosition;
            var scale = rig.localScale;
            var closeAlpha = detail.color.a;
            var closeScale = detail.rectTransform.localScale;
            typeof(PlayerHubController).GetField("_visitPhase", Fields).SetValue(hub, 3);
            Call(hub, "FrameFountainWalk", progress);
            Check(Vector2.Distance(position, rig.anchoredPosition) < .01f && Vector3.Distance(scale, rig.localScale) < .001f
                && Mathf.Approximately(closeAlpha, detail.color.a) && Vector3.Distance(closeScale, detail.rectTransform.localScale) < .001f,
                "entry and exit use the same dissolve and pose at " + progress);
        }
        typeof(PlayerHubController).GetField("_visitPhase", Fields).SetValue(hub, 2);
        Call(hub, "FrameFountainWalk", 1f);
        Save("fountain-close-1600x900.png");

        foreach (var size in new[] { new Vector2Int(2000, 900), new Vector2Int(1280, 720), new Vector2Int(2400, 1080) })
        {
            ResizeCanvas(size.x, size.y); ((RectTransform)hub.transform).sizeDelta = size; Call(hub, "Resize"); Call(hub, "UpdateFountain");
            Canvas.ForceUpdateCanvases();
            var view = new Vector3[4]; area.GetWorldCorners(view);
            var art = new Vector3[4]; detail.rectTransform.GetWorldCorners(art);
            var innerLeft = art[0].x;
            var innerRight = art[2].x;
            var innerBottom = art[0].y;
            var innerTop = art[2].y;
            Check(innerLeft <= view[0].x && innerRight >= view[2].x && innerBottom <= view[0].y && innerTop >= view[2].y,
                "the close-up background covers the viewport at " + size.x + "x" + size.y);
            var statueTop = Vector3.Lerp(art[0], art[2], .88f).y;
            var waterFront = Vector3.Lerp(art[0], art[2], .18f).y;
            Check(statueTop < view[2].y && waterFront > view[0].y,
                "statue and foreground water remain visible at " + size.x + "x" + size.y);
            Save("fountain-close-" + size.x + "x" + size.y + ".png");
        }
        ResizeCanvas(1600, 900); ((RectTransform)hub.transform).sizeDelta = new Vector2(1600, 900); Call(hub, "Resize"); Call(hub, "UpdateFountain");

        await Click("ОТОЙТИ");
        Check(Get<int>(hub, "_visitPhase") == 3, "step back starts walking away");
        Call(hub, "MoveVisit", 0f);
        Check(Vector2.Distance(restingPosition, rig.anchoredPosition) < .01f && Vector3.Distance(restingScale, rig.localScale) < .001f,
            "starting retreat does not jump away from the resting pose");
        previousAlpha = 1;
        for (var i = 0; i < 120 && Get<int>(hub, "_visitPhase") != 0; i++)
        {
            Call(hub, "MoveVisit", 1 / 30f); Call(hub, "UpdateFountain"); Shot(); await NextFrame();
            Check(detail.color.a <= previousAlpha, "exit dissolves towards the square without flashing backwards");
            previousAlpha = detail.color.a;
        }
        for (var i = 0; i < 8; i++) Shot();
        Check(Get<int>(hub, "_visitPhase") == 0 && rig.anchoredPosition == Vector2.zero && rig.localScale == Vector3.one,
            "retreat restores the original square exactly");
        Check(!detail.gameObject.activeSelf && detail.color.a == 0,
            "after retreat no close-up artwork remains over the original square");

        var fountainIndex = Array.IndexOf(places, fountain);
        Call(hub, "StartVisit", fountainIndex); Call(hub, "MoveVisit", 10f);
        Call(hub, "EndVisitNow");
        Check(!detail.gameObject.activeSelf && Get<int>(hub, "_visitPhase") == 0,
            "interrupting a visit hides the separate background immediately");
        var settings = Get<HubSnapshot>(hub, "_state").Settings;
        settings.ReducedMotion = true;
        Call(hub, "StartVisit", fountainIndex); Call(hub, "MoveVisit", .09f);
        Check(rig.localScale == Vector3.one && detail.rectTransform.localScale == Vector3.one
            && detail.color.a > 0 && detail.color.a < 1, "reduced motion uses a short dissolve without camera movement");
        Call(hub, "MoveVisit", 1f); hub.Back(); Call(hub, "MoveVisit", 1f);
        Check(!detail.gameObject.activeSelf && Get<int>(hub, "_visitPhase") == 0,
            "Back also returns to the original square in reduced motion mode");
        settings.ReducedMotion = false;
    }

    private static void FinishFountainWhenReady()
    {
        if (operation == null || !operation.IsCompleted) return;
        EditorApplication.update -= FinishFountainWhenReady; Application.logMessageReceived -= OnLog;
        if (operation.IsFaulted)
        {
            File.WriteAllText(Path.Combine(Output, "error.txt"), operation.Exception.ToString());
            Debug.LogException(operation.Exception); EditorApplication.Exit(1); return;
        }
        File.WriteAllText(Path.Combine(Output, "verification.txt"), checks + " assertions passed: tap on the fountain walks up to it, "
            + "original square preserved, separate close-up background, monotonic reversible dissolve, rim and water at "
            + "1600x900/2000x900/1280x720/2400x1080, original hit area, accessible shop, clean return and interrupted exit, "
            + "reduced motion. Frames in frames/.");
        Debug.Log("FOUNTAIN QA PASSED: " + checks); EditorApplication.Exit(0);
    }
}
