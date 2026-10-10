// Copy into an isolated Unity project's Assets/Scripts/Editor.
// Run with graphics enabled: -executeMethod ClubTablesVideoQa.Run (no -quit / -nographics).
// CLUB_TABLES_QA_OUTPUT is required. CLUB_TABLES_QA_GAMES defaults to durak,chests.
// Set durak,bluff,chests,govno once all four tables have production video assets.
// Actual Club UI, native decoders and memory-only profile. No changes to the player's save.
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

public static class ClubTablesVideoQa
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int Width = 2100, Height = 900, SampleWidth = 256;
    private static readonly Dictionary<string, string> Names = new Dictionary<string, string>
    {
        { "durak", "Дурак" }, { "bluff", "Верю — не верю" }, { "chests", "Сундучки" }, { "govno", "Говно" }
    };
    private static readonly List<string> Report = new List<string>();
    private static readonly List<string> Trace = new List<string>();
    private static readonly List<string> Errors = new List<string>();
    private static readonly List<ResidentState> Residents = new List<ResidentState>();
    private static readonly List<Color32[]> ContactFrames = new List<Color32[]>();
    private static readonly List<string> ContactTimes = new List<string>();
    private static readonly List<double> SeamDifferences = new List<double>();
    private static string output, game, gameOutput;
    private static string[] games;
    private static int gameIndex;
    private static PlayerHubController hub;
    private static Dictionary<string, HubClubVideo> videos;
    private static HubClubVideo video;
    private static ResidentState settings;
    private static VideoClip play, action;
    private static Camera camera;
    private static MemoryStore store;
    private static RectTransform originalViewport, offscreenViewport;
    private static RenderTexture sampleTarget;
    private static Texture2D sampleTexture;
    private static Color32[] previousPixels, loopTail;
    private static double begun, deadline, stageTime, lastSample, lastTrace, lastFrameChange, lastContact;
    private static double lastLeftChange, lastRightChange, longestHold, largestLeftQuiet, largestRightQuiet;
    private static long previousFrame = -1, pausedFrame, tailFrame;
    private static int checks, screenshots, wraps, samples, leftChanges, rightChanges, sampleHeight;
    private static int actionStarts, actionReturns;
    private static string previousClip, previousShowing;
    private static float pausedWait;
    private static bool returned, actionCaptured, finished, previousOptionsEnabled;
    private static EnterPlayModeOptions previousOptions;
    private enum Stage { EnterClub, Ready, IdleLoop, Gesture, AfterGesture, Pause, Paused, Resume }
    private static Stage stage;

    private sealed class ResidentState
    {
        public string Id;
        public HubClubVideo Video;
        public bool Stopped, HasIdle, HasVariation;
        public float Min, Max, Blend;
        public VideoClip Action;
    }

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
        output = Environment.GetEnvironmentVariable("CLUB_TABLES_QA_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) throw new InvalidOperationException("Set CLUB_TABLES_QA_OUTPUT first.");
        var requested = Environment.GetEnvironmentVariable("CLUB_TABLES_QA_GAMES");
        games = (string.IsNullOrWhiteSpace(requested) ? "durak,chests" : requested)
            .Split(',').Select(value => value.Trim().ToLowerInvariant()).Where(value => value.Length > 0).Distinct().ToArray();
        if (games.Length == 0 || games.Any(value => !Names.ContainsKey(value)))
            throw new ArgumentException("CLUB_TABLES_QA_GAMES must contain durak,bluff,chests,govno IDs separated by commas.");
        Directory.CreateDirectory(output);
        Application.logMessageReceived += OnLog;
        store = new MemoryStore();
        PlayerHubRuntime.UseService(new LocalPlayerHubService(store, new FixedClock()));
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("Disabled bootstrap for memory-only Club tables QA").AddComponent<AppFlowController>().enabled = false;
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
            var canvas = new GameObject("Club tables QA canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(Width, Height);
            camera = new GameObject("Club tables QA camera").AddComponent<Camera>();
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
            new GameObject("Club tables QA events", typeof(EventSystem)).transform.SetParent(canvas.transform, false);
            var root = new GameObject("Actual player hub under test", typeof(RectTransform));
            root.transform.SetParent(canvas.transform, false);
            ((RectTransform)root.transform).sizeDelta = new Vector2(Width, Height);
            hub = root.AddComponent<PlayerHubController>();
            hub.enabled = false;
            hub.Configure(() => { }, () => { }, (id, count) => { });
            Call(hub, "Start");
            begun = stageTime = EditorApplication.timeSinceStartup;
            deadline = begun + 90;
            Trace.Add("seconds,game,stage,showing,blend,frame,alpha,variationWait,leftChangedPixels,rightChangedPixels");
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
            if (now > deadline) throw new TimeoutException("Club tables QA timed out at " + game + " / " + stage);
            if (Errors.Count > 0) throw new InvalidOperationException(string.Join("\n", Errors));
            if (video != null && now - lastTrace >= .1) { lastTrace = now; AddTrace(now, -1, -1); }
            switch (stage)
            {
                case Stage.EnterClub:
                    if (Get<HubSnapshot>(hub, "_state") == null || Get<bool>(hub, "_busy")) return;
                    Check(store.Writes > 0, "profile uses the injected memory store");
                    PlayerHubRuntime.Current.Settings.Music = true;
                    Call(hub, "ShowDistrict", 1); Call(hub, "SettleCity");
                    Call(hub, "StartDive", 2); Call(hub, "MoveDive", 10f);
                    Call(hub, "UpdateInteriorControls");
                    Check(Get<int>(hub, "_cityLevel") == 2, "the real hub enters the Club");
                    videos = Get<Dictionary<string, HubClubVideo>>(hub, "_clubVideos");
                    Check(games.All(id => videos.ContainsKey("table-" + id)), "all requested animated tables are registered");
                    Check(videos.ContainsKey("club-bartender") && videos.ContainsKey("club-pianist"), "bartender and pianist remain registered");
                    CaptureResidents();
                    BeginGame();
                    break;
                case Stage.Ready:
                    if (!Ready(video) || video.IsBlending || video.Showing != "play") return;
                    Save("close.png");
                    now = EditorApplication.timeSinceStartup;
                    ResetWatch(now);
                    lastLeftChange = lastRightChange = lastSample = lastContact = now;
                    previousPixels = ReadPixels();
                    ContactFrames.Add(previousPixels);
                    ContactTimes.Add("0," + video.Player.frame);
                    SetStage(Stage.IdleLoop, now);
                    break;
                case Stage.IdleLoop:
                    WatchDecoder(now);
                    CheckBaseline();
                    if (now - lastSample >= .35) SampleMotion(now);
                    if (elapsed < play.length + .4 || wraps < 1) return;
                    Check(samples >= 6, "idle has at least six decoded image samples over a complete loop");
                    Check(leftChanges >= samples / 2 && rightChanges >= samples / 2,
                        "both character halves change real pixels in at least half the idle sample intervals");
                    Check(largestLeftQuiet < 3.5 && largestRightQuiet < 3.5,
                        "neither character half stays visually motionless for 3.5 seconds");
                    Check(wraps >= 1 && longestHold < .7, "native idle loop wraps without a 0.7-second decoder hold");
                    Check(SeamDifferences.Count >= 1 && SeamDifferences.All(value => value < 12),
                        "decoded native loop seam differences stay below 12/255 MAE");
                    Report.Add(game + " idle: " + wraps + " wraps, " + samples + " samples, moving left/right "
                        + leftChanges + "/" + rightChanges + ", longest decoder hold " + Number(longestHold) + "s; seam MAE "
                        + string.Join(",", SeamDifferences.Select(Number)) + ".");
                    WriteContactSheet();
                    Save("after-loop.png");
                    if (video.HasVariation)
                    {
                        video.SetupVariation(action, 0, 0, settings.Blend);
                        previousShowing = video.Showing;
                        SetStage(Stage.Gesture, EditorApplication.timeSinceStartup);
                    }
                    else SetStage(Stage.AfterGesture, EditorApplication.timeSinceStartup);
                    ResetWatch(EditorApplication.timeSinceStartup);
                    break;
                case Stage.Gesture:
                    WatchDecoder(now);
                    ObserveTransition(now);
                    if (!returned || video.IsBlending || video.Showing != "play") return;
                    Check(actionStarts == 1 && actionReturns == 1, "one requested gesture returns to the moving idle loop");
                    Check(actionCaptured, "gesture is rendered and captured after its entry transition");
                    video.SetupVariation(action, 1000, 1000, settings.Blend);
                    ResetWatch(now);
                    SetStage(Stage.AfterGesture, now);
                    break;
                case Stage.AfterGesture:
                    WatchDecoder(now);
                    CheckBaseline();
                    if (elapsed < .7) return;
                    if (video.HasVariation) Save("after-action.png");
                    originalViewport = video.Viewport;
                    offscreenViewport = new GameObject("QA viewport outside " + game, typeof(RectTransform)).GetComponent<RectTransform>();
                    offscreenViewport.SetParent(originalViewport.parent, false);
                    offscreenViewport.sizeDelta = Vector2.one;
                    offscreenViewport.anchoredPosition = new Vector2(100000, 100000);
                    video.Viewport = offscreenViewport;
                    SetStage(Stage.Pause, EditorApplication.timeSinceStartup);
                    break;
                case Stage.Pause:
                    if (elapsed < .25) return;
                    Check(!video.Wanted && !video.Player.isPlaying, "offscreen table pauses its active native decoder");
                    pausedFrame = video.Player.frame;
                    pausedWait = video.VariationSecondsRemaining;
                    SetStage(Stage.Paused, now);
                    break;
                case Stage.Paused:
                    if (elapsed < .5) return;
                    Check(video.Player.frame == pausedFrame, "offscreen table holds its exact decoded frame");
                    if (video.HasVariation) Check(Mathf.Approximately(pausedWait, video.VariationSecondsRemaining), "offscreen table freezes the gesture timer");
                    video.Viewport = originalViewport;
                    SetStage(Stage.Resume, now);
                    break;
                case Stage.Resume:
                    if (!Ready(video) || elapsed < .4) return;
                    Check(video.Player.frame != pausedFrame, "visible table resumes decoded motion");
                    Check(!video.HasIdle && video.HasVariation == settings.HasVariation && video.Showing == "play" && video.Player.clip == play,
                        "resumed table keeps its baseline and configured animation mode");
                    Check(Errors.Count == 0, "no runtime exceptions or native decoder errors");
                    UnityEngine.Object.DestroyImmediate(offscreenViewport.gameObject);
                    offscreenViewport = null; originalViewport = null;
                    Debug.Log("CLUB TABLE VIDEO QA COMPLETE: " + game);
                    gameIndex++;
                    if (gameIndex < games.Length) { BeginGame(); return; }
                    RestoreAndValidateResidents();
                    Report.Add("Play Mode elapsed: " + Number(now - begun) + " seconds; memory-only profile writes: " + store.Writes + ".");
                    Finish(null);
                    break;
            }
        }
        catch (Exception error) { Finish(error); }
    }

    private static void CaptureResidents()
    {
        foreach (var entry in videos)
        {
            var resident = entry.Value;
            foreach (var player in resident.GetComponentsInChildren<VideoPlayer>(true))
                player.errorReceived += (source, message) => Errors.Add(source.name + ": " + message);
            var value = new ResidentState
            {
                Id = entry.Key, Video = resident, Stopped = resident.Stopped, HasIdle = resident.HasIdle,
                HasVariation = resident.HasVariation, Min = resident.VariationMinDelay, Max = resident.VariationMaxDelay,
                Blend = resident.VariationBlendSeconds
            };
            if (entry.Key.StartsWith("table-") && resident.HasVariation)
            {
                value.Action = Resources.Load<VideoClip>("UI/Hub/Video/club-" + entry.Key.Substring(6) + "-action");
                Check(value.Action != null, entry.Key + " has its configured action asset");
                resident.SetupVariation(value.Action, 1000, 1000, value.Blend);
            }
            Residents.Add(value);
        }
    }

    private static void BeginGame()
    {
        game = games[gameIndex];
        gameOutput = Path.Combine(output, game);
        Directory.CreateDirectory(gameOutput);
        video = videos["table-" + game];
        settings = Residents.Single(value => value.Id == "table-" + game);
        play = Resources.Load<VideoClip>("UI/Hub/Video/club-" + game + "-play");
        action = settings.Action;
        wraps = samples = leftChanges = rightChanges = actionStarts = actionReturns = 0;
        longestHold = largestLeftQuiet = largestRightQuiet = 0;
        returned = actionCaptured = false;
        loopTail = previousPixels = null;
        ContactFrames.Clear(); ContactTimes.Clear(); SeamDifferences.Clear();
        ReleaseSamples();
        ValidateIntegration();
        sampleHeight = Mathf.RoundToInt(SampleWidth * (float)play.height / play.width);
        sampleTarget = new RenderTexture(SampleWidth, sampleHeight, 0, RenderTextureFormat.ARGB32);
        sampleTarget.Create();
        sampleTexture = new Texture2D(SampleWidth, sampleHeight, TextureFormat.RGB24, false);
        Call(hub, "EndVisitNow");
        Call(hub, "Navigate", "home");
        var places = ((Array)typeof(PlayerHubController).GetField("CityPlaces", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null)).Cast<object>().ToArray();
        var index = Array.FindIndex(places, place => (string)place.GetType().GetField("Id").GetValue(place) == "table-" + game);
        Check(index >= 0, "table has a real place entry");
        Call(hub, "LookAtPlace", places[index]);
        Call(hub, "StartVisit", index);
        for (var frame = 0; frame < 200 && Get<int>(hub, "_visitPhase") == 1; frame++) Call(hub, "MoveVisit", 1 / 60f);
        Call(hub, "UpdateInteriorControls");
        Check(Get<int>(hub, "_visitPhase") == 2, "the real table approach reaches its close view");
        var now = EditorApplication.timeSinceStartup;
        deadline = now + Math.Max(75, play.length * 3 + (action == null ? 0 : action.length) + 20);
        SetStage(Stage.Ready, now);
    }

    private static void ValidateIntegration()
    {
        var mask = Resources.Load<Texture2D>("UI/Hub/Video/club-" + game + "-mask");
        Check(play != null && mask != null && !video.HasIdle, "table has a baseline video and mask without a static idle mode");
        Check(play.length > 2 && play.frameCount > 60, "baseline is a sustained authored animation loop");
        Check(video.Material != null && video.Material.shader.name == "CardCity/HubVideoMask"
            && video.Material.GetTexture("_MaskTex") == mask, "table uses its production mask shader and mask");
        Check(mask.width == play.width && mask.height == play.height, "mask and video have matching crop pixel dimensions");
        var rect = (RectTransform)video.transform;
        Check(Mathf.Abs(rect.rect.width / rect.rect.height - (float)play.width / play.height) < .01f, "placement preserves video crop aspect ratio");
        var live = video.transform.parent.GetComponentsInChildren<Image>().Single(image => image.name == "Live " + Names[game]);
        Check(video.transform.GetSiblingIndex() > live.transform.GetSiblingIndex(), "video sits above its static table layer");
        Check(video.Viewport == Get<RectTransform>(hub, "_cityArea"), "video follows the real city viewport");
        var players = video.GetComponentsInChildren<VideoPlayer>(true);
        Check(players.Length == 2 && players.All(player => player.audioOutputMode == VideoAudioOutputMode.None), "table uses two silent native decoder layers");
        if (!video.HasVariation) { Report.Add(game + ": continuous baseline; no action clip configured."); return; }
        Check(action != null && action.width == play.width && action.height == play.height, "baseline and gesture have matching crop dimensions");
        Check(settings.Min >= 0 && settings.Max > settings.Min && settings.Blend > 0, "production gestures have randomized intervals and a positive overlap");
        var waits = new HashSet<float>();
        for (var index = 0; index < 6; index++)
        {
            video.SetupVariation(action, settings.Min, settings.Max, settings.Blend);
            Check(video.VariationSecondsRemaining >= settings.Min && video.VariationSecondsRemaining <= settings.Max,
                "random wait sample " + index + " stays within production bounds");
            waits.Add(video.VariationSecondsRemaining);
        }
        Check(waits.Count > 1, "gesture waits are not a fixed repeating delay");
        video.SetupVariation(action, 1000, 1000, settings.Blend);
    }

    private static void RestoreAndValidateResidents()
    {
        foreach (var value in Residents)
        {
            var resident = value.Video;
            if (value.Action != null) resident.SetupVariation(value.Action, value.Min, value.Max, value.Blend);
            Check(resident.Stopped == value.Stopped && resident.HasIdle == value.HasIdle && resident.HasVariation == value.HasVariation
                && Mathf.Approximately(resident.VariationMinDelay, value.Min) && Mathf.Approximately(resident.VariationMaxDelay, value.Max)
                && Mathf.Approximately(resident.VariationBlendSeconds, value.Blend), value.Id + " keeps its production controls and timing");
        }
    }

    private static void ResetWatch(double now)
    {
        previousFrame = video.Player.frame;
        previousClip = video.Player.clip != null ? video.Player.clip.name : "";
        lastFrameChange = now;
    }

    private static void CheckBaseline()
    {
        if (video.Player.clip != play || video.Showing != "play" || !video.Player.isLooping)
            throw new InvalidOperationException(game + " changed away from the baseline loop");
    }

    private static void WatchDecoder(double now)
    {
        if (!video.Wanted || !video.Player.isPrepared || !video.Player.isPlaying)
            throw new InvalidOperationException(game + " unexpectedly stopped while visible");
        var frame = video.Player.frame;
        var clip = video.Player.clip.name;
        var hold = now - lastFrameChange;
        longestHold = Math.Max(longestHold, hold);
        if (hold >= .7) throw new InvalidOperationException(game + " native frame held for " + Number(hold) + "s");
        if (frame >= 0 && (frame != previousFrame || clip != previousClip))
        {
            if (stage == Stage.IdleLoop && clip == previousClip && previousFrame - frame > (long)video.Player.frameCount / 2)
            {
                wraps++;
                if (loopTail != null)
                {
                    var head = ReadPixels();
                    double difference = 0;
                    for (var i = 0; i < head.Length; i++)
                        difference += Math.Abs(head[i].r - loopTail[i].r) + Math.Abs(head[i].g - loopTail[i].g) + Math.Abs(head[i].b - loopTail[i].b);
                    SeamDifferences.Add(difference / (head.Length * 3));
                    Report.Add(game + " loop " + wraps + " decoded seam frames " + tailFrame + " -> " + frame + ".");
                    if (wraps == 1) WritePair(loopTail, head);
                }
                loopTail = null;
            }
            if (stage == Stage.IdleLoop && frame >= (long)video.Player.frameCount - 8) { loopTail = ReadPixels(); tailFrame = frame; }
            previousFrame = frame; previousClip = clip; lastFrameChange = now;
        }
        if (video.GetComponentsInChildren<RawImage>(true).Sum(layer => layer.color.a) < .99f)
            throw new InvalidOperationException(game + " animation faded to its static picture");
    }

    private static Color32[] ReadPixels()
    {
        var previous = RenderTexture.active;
        Graphics.Blit(video.Player.targetTexture, sampleTarget);
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
        // Two horizontal halves contain the two seated players; ignore the outer soft mask edge.
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
        if (now - lastContact >= 1 && ContactFrames.Count < 16)
        {
            ContactFrames.Add(pixels);
            ContactTimes.Add(Number(now - stageTime) + "," + video.Player.frame);
            lastContact = now;
        }
        previousPixels = pixels;
        AddTrace(now, changed[0], changed[1]);
    }

    private static void ObserveTransition(double now)
    {
        var showing = video.Showing;
        if (showing != previousShowing)
        {
            var back = video.GetComponentsInChildren<VideoPlayer>(true).Single(player => player != video.Player);
            Check(video.IsBlending && back.isPrepared && back.isPlaying, "clip switch uses an already decoded moving crossfade");
            var overlap = Math.Ceiling(settings.Blend * video.Player.clip.frameRate);
            Check(video.Player.frame >= (long)video.Player.frameCount - overlap - 5, "outgoing clip reaches its authored overlap");
            Check(back.frame >= 0 && back.frame <= Math.Ceiling(settings.Blend * back.clip.frameRate) + 5, "incoming clip begins in its authored overlap");
            if (showing == "action")
            {
                Check(back.clip == action && !back.isLooping, "gesture plays once");
                actionStarts++;
            }
            else
            {
                Check(showing == "play" && back.clip == play && back.isLooping, "gesture returns to the original live baseline");
                actionReturns++; returned = true;
            }
            previousShowing = showing;
        }
        if (!actionCaptured && showing == "action" && !video.IsBlending && video.Player.frame > (long)action.frameCount / 3)
        {
            Save("action.png");
            actionCaptured = true;
            ResetWatch(EditorApplication.timeSinceStartup);
        }
    }

    private static void WritePair(Color32[] tail, Color32[] head)
    {
        var image = new Texture2D(SampleWidth * 2, sampleHeight, TextureFormat.RGB24, false);
        image.SetPixels32(0, 0, SampleWidth, sampleHeight, tail);
        image.SetPixels32(SampleWidth, 0, SampleWidth, sampleHeight, head);
        image.Apply();
        File.WriteAllBytes(Path.Combine(gameOutput, "native-loop-seam-tail-head.png"), image.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(image);
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
        File.WriteAllBytes(Path.Combine(gameOutput, "idle-motion-contact-sheet.png"), sheet.EncodeToPNG());
        File.WriteAllLines(Path.Combine(gameOutput, "idle-contact-times.csv"), new[] { "seconds,decodedFrame" }.Concat(ContactTimes));
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
            File.WriteAllBytes(Path.Combine(gameOutput, name), capture.EncodeToPNG());
            screenshots++;
        }
        finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(capture); }
    }

    private static void ReleaseSamples()
    {
        if (sampleTarget != null) { sampleTarget.Release(); UnityEngine.Object.DestroyImmediate(sampleTarget); sampleTarget = null; }
        if (sampleTexture != null) { UnityEngine.Object.DestroyImmediate(sampleTexture); sampleTexture = null; }
    }
    private static bool Ready(HubClubVideo value) => value.Wanted && value.Player.isPrepared && value.Player.isPlaying
        && value.Player.frame >= 2 && Get<float>(value, "_alpha") >= .999f;
    private static void AddTrace(double now, int left, int right) => Trace.Add(string.Join(",", Number(now - begun), game, stage,
        video.Showing, video.IsBlending, video.Player.frame, Number(Get<float>(video, "_alpha")), Number(video.VariationSecondsRemaining), left, right));
    private static void SetStage(Stage value, double now) { stage = value; stageTime = now; }
    private static string Number(double value) => value.ToString("F4", CultureInfo.InvariantCulture);
    private static T Get<T>(object value, string field) => (T)value.GetType().GetField(field, Private).GetValue(value);
    private static object Call(object value, string method, params object[] args) => value.GetType().GetMethod(method, Private).Invoke(value, args);
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException((game == null ? "" : game + ": ") + label);
        checks++;
        Report.Add("PASS " + (game == null ? "" : game + ": ") + label);
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
        if (video != null && originalViewport != null) video.Viewport = originalViewport;
        foreach (var value in Residents)
            if (value.Video != null && value.Action != null) value.Video.SetupVariation(value.Action, value.Min, value.Max, value.Blend);
        EditorSettings.enterPlayModeOptionsEnabled = previousOptionsEnabled;
        EditorSettings.enterPlayModeOptions = previousOptions;
        File.WriteAllLines(Path.Combine(output, "trace.csv"), Trace);
        File.WriteAllLines(Path.Combine(output, "runtime-errors.txt"), Errors);
        File.WriteAllText(Path.Combine(output, "verification.txt"),
            (error == null ? checks + " assertions passed; " + screenshots + " screenshots.\n" : "FAILED: " + error + "\n") + string.Join("\n", Report));
        ReleaseSamples();
        if (error != null) Debug.LogException(error);
        else Debug.Log("CLUB TABLES VIDEO QA PASSED: " + checks);
        EditorApplication.Exit(error == null ? 0 : 1);
    }
}
