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
        var places = ((Array)typeof(PlayerHubController).GetField("CityPlaces", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null))
            .Cast<object>().ToArray();
        var fountain = places.Single(p => (string)Item(p, "Id") == "fountain");
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
        for (var i = 0; i < 120 && Get<int>(hub, "_visitPhase") == 1; i++)
        {
            Call(hub, "MoveVisit", 1 / 30f); Call(hub, "UpdateFountain"); Shot(); await NextFrame();
        }
        Check(Get<int>(hub, "_visitPhase") == 2, "the walk ends at the fountain");
        for (var i = 0; i < 20; i++) { Call(hub, "MoveVisit", 1 / 30f); Call(hub, "UpdateFountain"); Shot(); await NextFrame(); }
        var close = Get<Image>(hub, "_fountainClose");
        Check(close != null && close.gameObject.activeInHierarchy && Mathf.Approximately(close.color.a, 1)
              && Mathf.Approximately(close.rectTransform.localScale.x, 1), "standing at the fountain shows the close-up at rest");
        Save("fountain-close-1600x900.png");

        foreach (var size in new[] { new Vector2Int(2000, 900), new Vector2Int(1280, 720) })
        {
            ResizeCanvas(size.x, size.y); ((RectTransform)hub.transform).sizeDelta = size; Call(hub, "Resize"); Call(hub, "UpdateFountain");
            Canvas.ForceUpdateCanvases();
            var view = new Vector3[4]; ((RectTransform)hub.transform).GetWorldCorners(view);
            var art = new Vector3[4]; close.rectTransform.GetWorldCorners(art);
            Check(art[0].x <= view[0].x + 1 && art[0].y <= view[0].y + 1 && art[2].x >= view[2].x - 1 && art[2].y >= view[2].y - 1,
                "the close-up covers " + size.x + "x" + size.y);
            Save("fountain-close-" + size.x + "x" + size.y + ".png");
        }
        ResizeCanvas(1600, 900); ((RectTransform)hub.transform).sizeDelta = new Vector2(1600, 900); Call(hub, "Resize"); Call(hub, "UpdateFountain");

        await Click("ОТОЙТИ");
        Check(Get<int>(hub, "_visitPhase") == 3, "step back starts walking away");
        for (var i = 0; i < 120 && Get<int>(hub, "_visitPhase") != 0; i++)
        {
            Call(hub, "MoveVisit", 1 / 30f); Call(hub, "UpdateFountain"); Shot(); await NextFrame();
        }
        for (var i = 0; i < 8; i++) Shot();
        Check(Get<int>(hub, "_visitPhase") == 0 && !Get<RectTransform>(hub, "_fountainLayer").gameObject.activeSelf,
            "back on the square, the close-up is gone");
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
            + "close-up at rest covers 1600x900/2000x900/1280x720, step back walks away and hides the close-up. Frames in frames/.");
        Debug.Log("FOUNTAIN QA PASSED: " + checks); EditorApplication.Exit(0);
    }
}
