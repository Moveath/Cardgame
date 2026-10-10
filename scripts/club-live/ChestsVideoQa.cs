// Copy into an isolated Unity project's Assets/Scripts/Editor.
// Run with graphics enabled: -executeMethod ChestsVideoQa.Run (no -quit / -nographics).
// CHESTS_VIDEO_QA_OUTPUT names the artifact directory. Real native decoders; memory-only profile.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CardGame.Core.Meta;
using CardGameClient;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Video;

public static class ChestsVideoQa
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int Width = 2100, Height = 900, SampleWidth = 256;
    private const float Blend = 15f / 48f;
    private static readonly List<string> Report = new List<string>();
    private static readonly List<string> Trace = new List<string>();
    private static readonly List<string> Errors = new List<string>();
    private static readonly List<Color32[]> ContactFrames = new List<Color32[]>();
    private static readonly List<string> ContactTimes = new List<string>();
    private static string output;
    private static PlayerHubController hub;
    private static HubClubVideo chests, bartender, pianist;
    private static VideoClip action, play;
    private static Camera camera;
    private static MemoryStore store;
    private static RectTransform originalViewport, offscreenViewport;
    private static RenderTexture sampleTarget;
    private static Texture2D sampleTexture;
    private static Color32[] previousPixels;
    private static double begun, deadline, stageTime, lastSample, lastTrace, lastFrameChange, lastContact;
    private static double lastLeftChange, lastRightChange, longestHold, largestLeftQuiet, largestRightQuiet;
    private static float productionMin, productionMax, bartenderMin, bartenderMax;
    private static long previousFrame = -1, pausedFrame;
    private static string previousClip, previousShowing;
    private static int stage, checks, screenshots, wraps, samples, leftChanges, rightChanges, sampleHeight;
    private static int actionStarts, actionReturns;
    private static float pausedWait;
    private static bool returned, actionCaptured, finished;
    private static bool previousOptionsEnabled;
    private static EnterPlayModeOptions previousOptions;

    private sealed class MemoryStore : IHubStore
    {
        private HubSnapshot saved;
        public int Writes;
        public Task<HubSnapshot> LoadAsync() => Task.FromResult(saved == null ? null : Clone(saved));
        public Task SaveAsync(HubSnapshot value) { saved = Clone(value); Writes++; return Task.CompletedTask; }
        private static HubSnapshot Clone(HubSnapshot value) => JsonUtility.FromJson<HubSnapshot>(JsonUtility.ToJson(value));
    }

    private sealed class FixedClock : IHubClock { public long UnixSeconds => 1791200000; }

    public static void Run()
    {
        output = Environment.GetEnvironmentVariable("CHESTS_VIDEO_QA_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) throw new InvalidOperationException("Set CHESTS_VIDEO_QA_OUTPUT first.");
        Directory.CreateDirectory(output);
        Application.logMessageReceived += OnLog;
        store = new MemoryStore();
        PlayerHubRuntime.UseService(new LocalPlayerHubService(store, new FixedClock()));
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("Disabled bootstrap for memory-only Chests QA").AddComponent<AppFlowController>().enabled = false;
        previousOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
        previousOptions = EditorSettings.enterPlayModeOptions;
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
        EditorApplication.playModeStateChanged += OnPlayMode;
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayMode(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode) return;
        try
        {
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            var canvas = new GameObject("Chests QA canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(Width, Height);
            camera = new GameObject("Chests QA camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(0, 0, -100);
            camera.orthographic = true;
            camera.orthographicSize = Height / 2f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 200;
            camera.targetTexture = new RenderTexture(Width, Height, 24);
            camera.targetTexture.Create();
            canvas.worldCamera = camera;
            new GameObject("Chests QA events", typeof(EventSystem)).transform.SetParent(canvas.transform, false);
            var root = new GameObject("Actual player hub under test", typeof(RectTransform));
            root.transform.SetParent(canvas.transform, false);
            ((RectTransform)root.transform).sizeDelta = new Vector2(Width, Height);
            hub = root.AddComponent<PlayerHubController>();
            hub.enabled = false;
            hub.Configure(() => { }, () => { }, (game, count) => { });
            Call(hub, "Start");
            begun = stageTime = EditorApplication.timeSinceStartup;
            deadline = begun + 90;
            Trace.Add("seconds,stage,showing,blend,frame,alpha,variationWait,leftChangedPixels,rightChangedPixels");
            EditorApplication.update += Tick;
        }
        catch (Exception error) { Finish(error); }
    }

    private static void Tick()
    {
        if (finished) return;
        try
        {
            var now = EditorApplication.timeSinceStartup;
            var elapsed = now - stageTime;
            if (now > deadline) throw new TimeoutException("Chests QA timed out at stage " + stage);
            if (Errors.Count > 0) throw new InvalidOperationException(string.Join("\n", Errors));
            if (chests != null && now - lastTrace >= .1)
            {
                lastTrace = now;
                AddTrace(now, -1, -1);
            }
            switch (stage)
            {
                case 0:
                    if (Get<HubSnapshot>(hub, "_state") == null || Get<bool>(hub, "_busy")) return;
                    Check(store.Writes > 0, "profile uses the injected memory store");
                    PlayerHubRuntime.Current.Settings.Music = true;
                    Call(hub, "ShowDistrict", 1); Call(hub, "SettleCity");
                    Call(hub, "StartDive", 2); Call(hub, "MoveDive", 10f);
                    Call(hub, "UpdateInteriorControls");
                    Check(Get<int>(hub, "_cityLevel") == 2, "the real hub enters the Club");
                    var residents = Get<Dictionary<string, HubClubVideo>>(hub, "_clubVideos");
                    Check(residents.TryGetValue("table-chests", out chests)
                        && residents.TryGetValue("club-bartender", out bartender)
                        && residents.TryGetValue("club-pianist", out pianist), "all three production animated residents are registered");
                    ValidateIntegration();
                    chests.SetupVariation(action, 1000, 1000, Blend);
                    Call(hub, "LookAtPicture", 0f);
                    Advance(now);
                    break;
                case 1:
                    if (!Ready(chests)) return;
                    Save("hall-live.png");
                    StartTable();
                    Save("chests-close.png");
                    now = EditorApplication.timeSinceStartup;
                    ResetWatch(now);
                    lastLeftChange = lastRightChange = lastSample = lastContact = now;
                    previousPixels = ReadPixels();
                    ContactFrames.Add(previousPixels);
                    ContactTimes.Add("0," + chests.Player.frame);
                    deadline = now + Math.Max(55, play.length * 2 + action.length + 15);
                    Advance(now);
                    break;
                case 2:
                    WatchDecoder(now);
                    if (now - lastSample >= .35) SampleMotion(now);
                    if (elapsed < play.length + .7 || wraps < 1) return;
                    Check(samples >= 15, "idle has at least 15 decoded image samples");
                    Check(leftChanges >= samples / 2 && rightChanges >= samples / 2,
                        "both residents change real pixels in at least half the idle sample intervals");
                    Check(largestLeftQuiet < 3.5 && largestRightQuiet < 3.5,
                        "neither half of the idle picture stays motionless for 3.5 seconds");
                    Check(wraps >= 1 && longestHold < .7, "idle loops without a native decoder hold of 0.7 seconds");
                    Report.Add("Idle: " + wraps + " wraps, " + samples + " image samples; left/right moving intervals "
                        + leftChanges + "/" + rightChanges + "; longest decoder hold " + Number(longestHold) + "s.");
                    WriteContactSheet();
                    chests.SetupVariation(action, 0, 0, Blend);
                    previousShowing = chests.Showing;
                    ResetWatch(EditorApplication.timeSinceStartup);
                    Advance(EditorApplication.timeSinceStartup);
                    break;
                case 3:
                    WatchDecoder(now);
                    ObserveTransition(now);
                    if (!returned || chests.IsBlending || chests.Showing != "play") return;
                    Check(actionStarts == 1 && actionReturns == 1, "one requested gesture returns to the moving idle loop");
                    chests.SetupVariation(action, 1000, 1000, Blend);
                    ResetWatch(now);
                    Advance(now);
                    break;
                case 4:
                    WatchDecoder(now);
                    if (elapsed < 1.2) return;
                    Save("chests-after-action.png");
                    originalViewport = chests.Viewport;
                    offscreenViewport = new GameObject("QA viewport outside the table", typeof(RectTransform)).GetComponent<RectTransform>();
                    offscreenViewport.SetParent(originalViewport.parent, false);
                    offscreenViewport.sizeDelta = Vector2.one;
                    offscreenViewport.anchoredPosition = new Vector2(100000, 100000);
                    chests.Viewport = offscreenViewport;
                    Advance(EditorApplication.timeSinceStartup);
                    break;
                case 5:
                    if (elapsed < .25) return;
                    Check(!chests.Wanted && !chests.Player.isPlaying, "offscreen table pauses its active native decoder");
                    pausedFrame = chests.Player.frame;
                    pausedWait = chests.VariationSecondsRemaining;
                    Advance(now);
                    break;
                case 6:
                    if (elapsed < .5) return;
                    Check(chests.Player.frame == pausedFrame && Mathf.Approximately(pausedWait, chests.VariationSecondsRemaining),
                        "offscreen table holds its frame and gesture timer");
                    chests.Viewport = originalViewport;
                    Advance(now);
                    break;
                case 7:
                    if (!Ready(chests) || elapsed < .4) return;
                    Check(chests.Player.frame != pausedFrame, "visible table resumes decoded motion");
                    Check(!bartender.Stopped && !pianist.Stopped && !pianist.HasVariation && pianist.HasIdle
                        && Mathf.Approximately(bartender.VariationMinDelay, bartenderMin)
                        && Mathf.Approximately(bartender.VariationMaxDelay, bartenderMax),
                        "table checks leave bartender and pianist controls and timing unchanged");
                    chests.SetupVariation(action, productionMin, productionMax, Blend);
                    Check(Errors.Count == 0, "no runtime exceptions or native decoder errors");
                    Report.Add("Play Mode elapsed: " + Number(now - begun) + " seconds; profile writes were memory-only: " + store.Writes + ".");
                    Finish(null);
                    break;
            }
        }
        catch (Exception error) { Finish(error); }
    }

    private static void ValidateIntegration()
    {
        play = Resources.Load<VideoClip>("UI/Hub/Video/club-chests-play");
        action = Resources.Load<VideoClip>("UI/Hub/Video/club-chests-action");
        Check(play != null && action != null && !chests.HasIdle && chests.HasVariation, "table has a baseline loop and one-shot gesture");
        Check(play.width == action.width && play.height == action.height, "baseline and gesture share the same crop resolution");
        Check(play.length > 5 && play.frameCount > 100, "baseline is a sustained authored animation loop");
        Check(chests.Material != null && chests.Material.shader.name == "CardCity/HubVideoMask"
            && chests.Material.GetTexture("_MaskTex") == Resources.Load<Texture2D>("UI/Hub/Video/club-chests-mask"),
            "table uses the production mask shader and mask");
        var live = chests.transform.parent.GetComponentsInChildren<Image>().Single(image => image.name == "Live Сундучки");
        Check(chests.transform.GetSiblingIndex() > live.transform.GetSiblingIndex(), "video is above its static table layer");
        Check(chests.Viewport == Get<RectTransform>(hub, "_cityArea"), "video follows the real city viewport");
        var players = chests.GetComponentsInChildren<VideoPlayer>(true);
        Check(players.Length == 2 && players.All(player => player.audioOutputMode == VideoAudioOutputMode.None), "table uses two silent native decoder layers");
        foreach (var video in new[] { chests, bartender, pianist })
            foreach (var player in video.GetComponentsInChildren<VideoPlayer>(true))
                player.errorReceived += (source, message) => Errors.Add(source.name + ": " + message);
        productionMin = chests.VariationMinDelay; productionMax = chests.VariationMaxDelay;
        bartenderMin = bartender.VariationMinDelay; bartenderMax = bartender.VariationMaxDelay;
        Check(productionMin >= 0 && productionMax > productionMin && Mathf.Approximately(chests.VariationBlendSeconds, Blend),
            "production gestures have randomized intervals and the authored overlap");
        var waits = new HashSet<float>();
        for (var index = 0; index < 6; index++)
        {
            chests.SetupVariation(action, productionMin, productionMax, Blend);
            Check(chests.VariationSecondsRemaining >= productionMin && chests.VariationSecondsRemaining <= productionMax,
                "random wait sample " + index + " stays within production bounds");
            waits.Add(chests.VariationSecondsRemaining);
        }
        Check(waits.Count > 1, "gesture waits are not a fixed repeating delay");
        sampleHeight = Mathf.RoundToInt(SampleWidth * (float)play.height / play.width);
        sampleTarget = new RenderTexture(SampleWidth, sampleHeight, 0, RenderTextureFormat.ARGB32);
        sampleTarget.Create();
        sampleTexture = new Texture2D(SampleWidth, sampleHeight, TextureFormat.RGB24, false);
    }

    private static void ResetWatch(double now)
    {
        previousFrame = chests.Player.frame;
        previousClip = chests.Player.clip != null ? chests.Player.clip.name : "";
        lastFrameChange = now;
    }

    private static void WatchDecoder(double now)
    {
        if (!chests.Wanted || !chests.Player.isPrepared || !chests.Player.isPlaying)
            throw new InvalidOperationException("Chests unexpectedly stopped while visible");
        var frame = chests.Player.frame;
        var clip = chests.Player.clip.name;
        var hold = now - lastFrameChange;
        longestHold = Math.Max(longestHold, hold);
        if (hold >= .7) throw new InvalidOperationException("Chests native frame held for " + Number(hold) + "s");
        if (frame >= 0 && (frame != previousFrame || clip != previousClip))
        {
            if (stage == 2 && clip == previousClip && previousFrame - frame > (long)chests.Player.frameCount / 2) wraps++;
            previousFrame = frame; previousClip = clip; lastFrameChange = now;
        }
        var layers = chests.GetComponentsInChildren<RawImage>(true);
        if (layers.Sum(layer => layer.color.a) < .99f) throw new InvalidOperationException("Table animation faded to its static picture");
    }

    private static Color32[] ReadPixels()
    {
        var previous = RenderTexture.active;
        Graphics.Blit(chests.Player.targetTexture, sampleTarget);
        RenderTexture.active = sampleTarget;
        sampleTexture.ReadPixels(new Rect(0, 0, SampleWidth, sampleHeight), 0, 0);
        sampleTexture.Apply();
        var pixels = sampleTexture.GetPixels32();
        RenderTexture.active = previous;
        return pixels;
    }

    private static void SampleMotion(double now)
    {
        lastSample = now;
        var pixels = ReadPixels();
        var changed = new int[2];
        // Ignore the soft outer mask edge. The two horizontal halves contain the two residents.
        for (var y = sampleHeight / 12; y < sampleHeight * 11 / 12; y++)
            for (var x = SampleWidth / 20; x < SampleWidth * 19 / 20; x++)
            {
                var index = y * SampleWidth + x;
                var a = pixels[index]; var b = previousPixels[index];
                var difference = Math.Max(Math.Abs(a.r - b.r), Math.Max(Math.Abs(a.g - b.g), Math.Abs(a.b - b.b)));
                if (difference >= 4) changed[x < SampleWidth / 2 ? 0 : 1]++;
            }
        samples++;
        if (changed[0] >= 8) { leftChanges++; lastLeftChange = now; }
        if (changed[1] >= 8) { rightChanges++; lastRightChange = now; }
        largestLeftQuiet = Math.Max(largestLeftQuiet, now - lastLeftChange);
        largestRightQuiet = Math.Max(largestRightQuiet, now - lastRightChange);
        if (now - lastContact >= 1 && ContactFrames.Count < 12)
        {
            ContactFrames.Add(pixels);
            ContactTimes.Add(Number(now - stageTime) + "," + chests.Player.frame);
            lastContact = now;
        }
        previousPixels = pixels;
        AddTrace(now, changed[0], changed[1]);
    }

    private static void ObserveTransition(double now)
    {
        var showing = chests.Showing;
        if (showing != previousShowing)
        {
            var back = chests.GetComponentsInChildren<VideoPlayer>(true).Single(player => player != chests.Player);
            Check(chests.IsBlending && back.isPrepared && back.isPlaying, "clip switch uses an already decoded moving crossfade");
            var overlap = Math.Ceiling(Blend * chests.Player.clip.frameRate);
            Check(chests.Player.frame >= (long)chests.Player.frameCount - overlap - 5, "outgoing clip reaches its authored overlap");
            Check(back.frame >= 0 && back.frame <= Math.Ceiling(Blend * back.clip.frameRate) + 5, "incoming clip begins in its authored overlap");
            if (showing == "action")
            {
                Check(back.clip == action && !back.isLooping, "the card exchange plays once");
                actionStarts++;
            }
            else
            {
                Check(showing == "play" && back.clip == play && back.isLooping, "gesture returns to the original live baseline");
                actionReturns++;
                returned = true;
            }
            previousShowing = showing;
        }
        if (!actionCaptured && showing == "action" && !chests.IsBlending && chests.Player.frame > (long)action.frameCount / 3)
        {
            Save("chests-exchange-action.png");
            actionCaptured = true;
            ResetWatch(EditorApplication.timeSinceStartup);
        }
    }

    private static void StartTable()
    {
        var places = ((Array)typeof(PlayerHubController).GetField("CityPlaces", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null)).Cast<object>().ToArray();
        var index = Array.FindIndex(places, place => (string)place.GetType().GetField("Id").GetValue(place) == "table-chests");
        Check(index >= 0, "Chests table has a real place entry");
        Call(hub, "LookAtPlace", places[index]);
        Call(hub, "StartVisit", index);
        for (var frame = 0; frame < 200 && Get<int>(hub, "_visitPhase") == 1; frame++) Call(hub, "MoveVisit", 1 / 60f);
        Call(hub, "UpdateInteriorControls");
    }

    private static void WriteContactSheet()
    {
        const int columns = 4;
        var rows = Mathf.CeilToInt(ContactFrames.Count / (float)columns);
        var sheet = new Texture2D(SampleWidth * columns, sampleHeight * rows, TextureFormat.RGB24, false);
        for (var index = 0; index < ContactFrames.Count; index++)
            sheet.SetPixels32(index % columns * SampleWidth, (rows - 1 - index / columns) * sampleHeight,
                SampleWidth, sampleHeight, ContactFrames[index]);
        sheet.Apply();
        File.WriteAllBytes(Path.Combine(output, "idle-motion-contact-sheet.png"), sheet.EncodeToPNG());
        File.WriteAllLines(Path.Combine(output, "idle-contact-times.csv"), new[] { "seconds,decodedFrame" }.Concat(ContactTimes));
        UnityEngine.Object.DestroyImmediate(sheet);
    }

    private static void Save(string name)
    {
        Canvas.ForceUpdateCanvases();
        camera.Render();
        var previous = RenderTexture.active;
        var capture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        try
        {
            RenderTexture.active = camera.targetTexture;
            capture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            capture.Apply();
            File.WriteAllBytes(Path.Combine(output, name), capture.EncodeToPNG());
            screenshots++;
        }
        finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(capture); }
    }

    private static bool Ready(HubClubVideo video) => video.Wanted && video.Player.isPrepared && video.Player.isPlaying
        && video.Player.frame >= 2 && Get<float>(video, "_alpha") >= .999f;
    private static void AddTrace(double now, int left, int right) => Trace.Add(string.Join(",", Number(now - begun), stage,
        chests.Showing, chests.IsBlending, chests.Player.frame, Number(Get<float>(chests, "_alpha")),
        Number(chests.VariationSecondsRemaining), left, right));
    private static void Advance(double now) { stage++; stageTime = now; }
    private static string Number(double value) => value.ToString("F4", CultureInfo.InvariantCulture);
    private static T Get<T>(object value, string field) => (T)value.GetType().GetField(field, Private).GetValue(value);
    private static object Call(object value, string method, params object[] args) => value.GetType().GetMethod(method, Private).Invoke(value, args);
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        checks++;
        Report.Add("PASS " + label);
    }
    private static void OnLog(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) Errors.Add(message + "\n" + stack);
    }

    private static void Finish(Exception error)
    {
        if (finished) return;
        finished = true;
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= OnPlayMode;
        Application.logMessageReceived -= OnLog;
        if (chests != null && originalViewport != null) chests.Viewport = originalViewport;
        EditorSettings.enterPlayModeOptionsEnabled = previousOptionsEnabled;
        EditorSettings.enterPlayModeOptions = previousOptions;
        File.WriteAllLines(Path.Combine(output, "trace.csv"), Trace);
        File.WriteAllLines(Path.Combine(output, "runtime-errors.txt"), Errors);
        File.WriteAllText(Path.Combine(output, "verification.txt"),
            (error == null ? checks + " assertions passed; " + screenshots + " screenshots.\n" : "FAILED: " + error + "\n") + string.Join("\n", Report));
        if (sampleTarget != null) sampleTarget.Release();
        if (error != null) Debug.LogException(error);
        else Debug.Log("CHESTS VIDEO QA PASSED: " + checks);
        EditorApplication.Exit(error == null ? 0 : 1);
    }
}
