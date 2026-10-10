// Copy into an isolated Unity project's Assets/Scripts/Editor.
// Run with graphics enabled: -executeMethod BartenderVideoQa.Run (do not pass -quit or -nographics).
// BARTENDER_VIDEO_QA_OUTPUT must name an artifact directory. The harness exits Unity when finished.
// Uses the actual club, native decoders and memory-only persistence; no real profile is read/written.
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

public static class BartenderVideoQa
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int Width = 2100, Height = 900;
    private static readonly List<string> Report = new List<string>();
    private static readonly List<string> Trace = new List<string>();
    private static readonly List<string> Errors = new List<string>();
    private static string output;
    private static PlayerHubController hub;
    private static HubClubVideo bartender, pianist;
    private static Camera camera;
    private static MemoryStore store;
    private static LoopWatch[] watches;
    private static object[] places;
    private static int stage, checks, shots;
    private static double begun, stageTime, lastTrace, loopSeconds, deadline;
    private static VideoClip variation;
    private static float savedVariationDelay;
    private static int variationStarts, variationReturns;
    private static string lastVariationShowing;
    private static bool waitingForStableReturn;
    private static double variationReturnedAt, variationMotionAt;
    private static long[] variationFrames;
    private static int[] variationClips;
    private static bool capturedVariation;
    private const float TestVariationMin = 2, TestVariationMax = 3, VariationBlend = 15f / 48f;
    private static bool finished;
    private static EnterPlayModeOptions previousPlayOptions;
    private static bool previousPlayOptionsEnabled;

    private sealed class MemoryStore : IHubStore
    {
        private HubSnapshot saved;
        public int Writes;
        public Task<HubSnapshot> LoadAsync() => Task.FromResult(saved == null ? null : Clone(saved));
        public Task SaveAsync(HubSnapshot snapshot) { saved = Clone(snapshot); Writes++; return Task.CompletedTask; }
        private static HubSnapshot Clone(HubSnapshot value) => JsonUtility.FromJson<HubSnapshot>(JsonUtility.ToJson(value));
    }

    private sealed class FixedClock : IHubClock
    {
        public long UnixSeconds => 1790500000;
    }

    private sealed class LoopWatch
    {
        public readonly HubClubVideo Video;
        public long PreviousFrame = -1;
        public double LastChange, LongestHold;
        public int Loops;

        public LoopWatch(HubClubVideo video, double now)
        {
            Video = video;
            PreviousFrame = video.Player.frame;
            LastChange = now;
        }

        public void Observe(double now)
        {
            var player = Video.Player;
            if (!Video.Wanted || !player.isPrepared || !player.isPlaying)
                throw new InvalidOperationException(Video.name + " stopped during the simultaneous loop measurement");
            if (Get<float>(Video, "_alpha") < .999f)
                throw new InvalidOperationException(Video.name + " lost opacity during a loop");
            var frame = player.frame;
            // A native loop can briefly report -1 while the last decoded texture remains visible.
            // Keep timing that interval: it must not conceal a frozen decoder.
            var hold = now - LastChange;
            LongestHold = Math.Max(LongestHold, hold);
            if (hold > .5)
                throw new InvalidOperationException(Video.name + " held frame " + PreviousFrame + " for " + Number(hold) + "s");
            if (frame < 0 || frame == PreviousFrame) return;
            if (PreviousFrame >= 0 && PreviousFrame - frame > (long)player.frameCount / 2) Loops++;
            PreviousFrame = frame;
            LastChange = now;
        }
    }

    public static void Run()
    {
        output = Environment.GetEnvironmentVariable("BARTENDER_VIDEO_QA_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) throw new InvalidOperationException("Set BARTENDER_VIDEO_QA_OUTPUT first.");
        Directory.CreateDirectory(output);
        Application.logMessageReceived += OnLog;
        // Inject before creating any hub, profile adapter or app-flow component.
        store = new MemoryStore();
        PlayerHubRuntime.UseService(new LocalPlayerHubService(store, new FixedClock()));
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("Disabled bootstrap for memory-only bartender QA").AddComponent<AppFlowController>().enabled = false;
        previousPlayOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
        previousPlayOptions = EditorSettings.enterPlayModeOptions;
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
            var canvas = new GameObject("Bartender QA canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(Width, Height);
            camera = new GameObject("Bartender QA camera").AddComponent<Camera>();
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
            new GameObject("Bartender QA events", typeof(EventSystem)).transform.SetParent(canvas.transform, false);
            var root = new GameObject("Actual player hub under test", typeof(RectTransform));
            root.transform.SetParent(canvas.transform, false);
            ((RectTransform)root.transform).sizeDelta = new Vector2(Width, Height);
            hub = root.AddComponent<PlayerHubController>();
            // Drive only camera navigation manually; the resident VideoPlayers run normal Play Mode Updates.
            hub.enabled = false;
            hub.Configure(() => { }, () => { }, (game, count) => { });
            Call(hub, "Start");
            begun = stageTime = EditorApplication.timeSinceStartup;
            deadline = begun + 120;
            Trace.Add("seconds,stage,resident,wanted,inView,playing,frame,alpha,showing,blending,variationRemaining,layerAClip,layerAFrame,layerAAlpha,layerBClip,layerBFrame,layerBAlpha");
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
            if (now > deadline) throw new TimeoutException("Bartender QA timed out at stage " + stage);
            if (Errors.Count > 0) throw new InvalidOperationException(string.Join("\n", Errors));
            if (bartender != null && now - lastTrace >= 1.0 / 30)
            {
                lastTrace = now;
                Canvas.ForceUpdateCanvases();
                foreach (var video in new[] { bartender, pianist })
                {
                    var visible = InView(video);
                    if (video.Wanted && !visible) throw new InvalidOperationException(video.name + " is Wanted outside the viewport");
                    var layers = video.GetComponentsInChildren<VideoPlayer>(true);
                    Trace.Add(string.Join(",", Number(now - begun), stage, video.name, video.Wanted, visible,
                        video.Player.isPlaying, video.Player.frame, Number(Get<float>(video, "_alpha")), video.Showing,
                        Get<bool>(video, "_switching"), Number(Property<float>(video, "VariationSecondsRemaining")),
                        LayerTrace(layers[0]), LayerTrace(layers[1])));
                }
            }
            switch (stage)
            {
                case 0:
                    if (Get<HubSnapshot>(hub, "_state") == null || Get<bool>(hub, "_busy")) return;
                    Check(store.Writes > 0, "profile initializes in the injected memory store");
                    PlayerHubRuntime.Current.Settings.Music = true;
                    Call(hub, "ShowDistrict", 1); Call(hub, "SettleCity");
                    Call(hub, "StartDive", 2); Call(hub, "MoveDive", 10f);
                    Call(hub, "UpdateInteriorControls");
                    Check(Get<int>(hub, "_cityLevel") == 2, "real hub enters the Club panorama");
                    var videos = Get<Dictionary<string, HubClubVideo>>(hub, "_clubVideos");
                    Check(videos.TryGetValue("club-bartender", out bartender) && videos.TryGetValue("club-pianist", out pianist),
                        "both residents are registered through the production Club integration");
                    places = ((Array)typeof(PlayerHubController).GetField("CityPlaces", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null)).Cast<object>().ToArray();
                    ValidateResident(bartender, "bartender", false);
                    ValidateResident(pianist, "pianist", true);
                    ValidateVariationDefaults();
                    // Preserve the original multi-loop baseline check independently of random action timing.
                    ConfigureVariation(1000, 1000);
                    Call(hub, "LookAtPicture", 0f);
                    Advance(now);
                    break;
                case 1:
                    if (!Ready(bartender) || !Ready(pianist)) return;
                    Check(bartender.Wanted && pianist.Wanted, "bartender and pianist play together in the first hall");
                    loopSeconds = Math.Max(bartender.Player.clip.length, pianist.Player.clip.length);
                    deadline = now + Math.Max(120, loopSeconds * 3 + 50);
                    Save("hall-live.png");
                    now = EditorApplication.timeSinceStartup;
                    watches = new[] { new LoopWatch(bartender, now), new LoopWatch(pianist, now) };
                    Advance(now);
                    break;
                case 2:
                    foreach (var watch in watches) watch.Observe(now);
                    if (elapsed < loopSeconds * 2 + .5 || watches.Any(watch => watch.Loops < 2)) return;
                    foreach (var watch in watches)
                    {
                        Check(watch.Loops >= 2 && watch.LongestHold <= .5, watch.Video.name + " completes at least two loops without holds over 0.5s");
                        Report.Add(watch.Video.name + ": " + watch.Loops + " wraps; longest frame hold " + Number(watch.LongestHold) + "s");
                    }
                    Save("hall-after-loops.png");
                    Call(hub, "LookAtPicture", 1f);
                    Advance(EditorApplication.timeSinceStartup);
                    break;
                case 3:
                    if (elapsed < .75) return;
                    Check(!InView(bartender) && !InView(pianist) && !bartender.Wanted && !pianist.Wanted,
                        "both residents are unwanted when the camera reaches the second hall");
                    Check(!bartender.Player.isPlaying && !pianist.Player.isPlaying, "offscreen front decoders pause");
                    Save("second-hall-paused.png");
                    Call(hub, "LookAtPicture", 0f);
                    Advance(EditorApplication.timeSinceStartup);
                    break;
                case 4:
                    if (elapsed < .5 || !Ready(bartender) || !Ready(pianist)) return;
                    Check(bartender.Wanted && pianist.Wanted, "both residents resume on returning to the first hall");
                    Call(hub, "ToggleResident", "club-bartender");
                    Advance(now);
                    break;
                case 5:
                    if (elapsed < .75) return;
                    Check(bartender.Stopped && !bartender.Wanted && !bartender.Player.isPlaying
                        && Get<float>(bartender, "_alpha") <= .001f, "a bartender tap fades to the painted hall and pauses the play-only clip");
                    Check(pianist.Wanted && pianist.Player.isPlaying, "stopping the bartender leaves the pianist playing");
                    Save("hall-bartender-stopped.png");
                    Call(hub, "ToggleResident", "club-bartender");
                    Advance(EditorApplication.timeSinceStartup);
                    break;
                case 6:
                    if (elapsed < .5 || !Ready(bartender)) return;
                    Check(!bartender.Stopped && bartender.Wanted, "a second bartender tap resumes the clip");
                    PlayerHubRuntime.Current.Settings.Music = false;
                    Check(!pianist.Wanted && bartender.Wanted, "music setting controls the pianist independently of the bartender");
                    PlayerHubRuntime.Current.Settings.Music = true;
                    StartTable("table-durak");
                    Advance(now);
                    break;
                case 7:
                    if (elapsed < .75) return;
                    Check(Get<int>(hub, "_visitPhase") == 2 && Get<string>(hub, "_page") == "game:durak"
                        && Get<RectTransform>(hub, "_cityRig").localScale.x > 1, "actual table navigation reaches its zoomed view");
                    Save("table-durak-live.png");
                    LeaveTable();
                    StartTable("table-govno");
                    Advance(EditorApplication.timeSinceStartup);
                    break;
                case 8:
                    if (elapsed < .75) return;
                    Save("table-govno-live.png");
                    LeaveTable();
                    Call(hub, "LookAtPicture", 0f);
                    Advance(EditorApplication.timeSinceStartup);
                    break;
                case 9:
                    if (elapsed < .5 || !Ready(bartender) || !Ready(pianist)) return;
                    Check(bartender.Wanted && pianist.Wanted, "returning from both table zooms resumes the visible residents");
                    ConfigureVariation(TestVariationMin, TestVariationMax);
                    savedVariationDelay = Property<float>(bartender, "VariationSecondsRemaining");
                    deadline = now + Math.Max(120, 3 * (variation.length + loopSeconds + TestVariationMax));
                    Advance(now);
                    break;
                case 10:
                    if (elapsed < .25) return;
                    var countdown = Property<float>(bartender, "VariationSecondsRemaining");
                    Check(countdown < savedVariationDelay && countdown > savedVariationDelay - .6f,
                        "random wait counts active visible baseline time");
                    savedVariationDelay = countdown;
                    Call(hub, "LookAtPicture", 1f);
                    Advance(now);
                    break;
                case 11:
                    if (elapsed < .8) return;
                    Check(Mathf.Abs(Property<float>(bartender, "VariationSecondsRemaining") - savedVariationDelay) < .001f,
                        "offscreen time does not consume the random wait");
                    Check(bartender.Showing == "play" && !bartender.Player.isPlaying,
                        "hiding the bartender cannot start a random action");
                    Call(hub, "LookAtPicture", 0f);
                    Advance(now);
                    break;
                case 12:
                    if (elapsed < .25 || !Ready(bartender)) return;
                    savedVariationDelay = Property<float>(bartender, "VariationSecondsRemaining");
                    Check(savedVariationDelay > .8f, "returning to view preserves the remaining active wait");
                    Call(hub, "ToggleResident", "club-bartender");
                    Advance(now);
                    break;
                case 13:
                    if (elapsed < .8) return;
                    Check(Mathf.Abs(Property<float>(bartender, "VariationSecondsRemaining") - savedVariationDelay) < .001f,
                        "manual stop freezes the random wait throughout its fade");
                    Check(bartender.Showing == "play" && !bartender.Player.isPlaying,
                        "manual stop does not queue an action burst");
                    Call(hub, "ToggleResident", "club-bartender");
                    Advance(now);
                    break;
                case 14:
                    if (elapsed < .3 || !Ready(bartender)) return;
                    Check(bartender.Showing == "play" && Property<float>(bartender, "VariationSecondsRemaining") > .2f,
                        "resuming starts with the baseline and the saved wait, without an immediate action");
                    lastVariationShowing = bartender.Showing;
                    ResetVariationMotion(now);
                    Advance(now);
                    break;
                case 15:
                    ObserveVariation(now);
                    if (variationReturns < 2 || elapsed < 1) return;
                    Check(variationStarts == 2, "two random requests each run exactly one action and return to baseline");
                    Check(pianist.Showing == "play" && pianist.Player.isLooping && !Get<bool>(pianist, "_switching"),
                        "bartender action cycles never switch or interrupt the pianist");
                    Save("hall-after-two-actions.png");
                    Check(Errors.Count == 0, "no runtime exceptions or native decoder errors");
                    Report.Add("All profile operations used the memory store (writes: " + store.Writes + ").");
                    Finish(null);
                    break;
            }
        }
        catch (Exception error) { Finish(error); }
    }

    private static void ValidateVariationDefaults()
    {
        Check(Property<bool>(bartender, "HasVariation") && !Property<bool>(pianist, "HasVariation"),
            "only the bartender has an optional action configured by the real integration");
        Check(Mathf.Approximately(Property<float>(bartender, "VariationMinDelay"), 8)
            && Mathf.Approximately(Property<float>(bartender, "VariationMaxDelay"), 28),
            "production random waits range from 8 to 28 active visible seconds before a matched phase");
        Check(Mathf.Approximately(Property<float>(bartender, "VariationBlendSeconds"), VariationBlend),
            "production transition duration matches the authored 8-frame overlap at 24fps");
        var clips = Get<VideoClip[]>(bartender, "_clips");
        variation = clips.Skip(1).SingleOrDefault(clip => clip != null);
        Check(variation != null, "production bartender action clip is loaded");
        var observed = new HashSet<float>();
        for (var sample = 0; sample < 12; sample++)
        {
            ConfigureVariation(8, 28);
            var delay = Property<float>(bartender, "VariationSecondsRemaining");
            Check(delay >= 8 && delay <= 28, "sampled production wait stays within bounds: " + Number(delay));
            observed.Add(delay);
        }
        Check(observed.Count > 1, "successive waits are randomized rather than a fixed timer");
    }

    private static void ConfigureVariation(float minimum, float maximum)
    {
        Call(bartender, "SetupVariation", variation, minimum, maximum, VariationBlend);
    }

    private static void ObserveVariation(double now)
    {
        if (!bartender.Wanted || !bartender.Player.isPlaying)
            throw new InvalidOperationException("bartender unexpectedly stopped during a random action cycle");
        if (!pianist.Wanted || !pianist.Player.isPlaying || pianist.Showing != "play" || Get<bool>(pianist, "_switching"))
            throw new InvalidOperationException("bartender action affected the pianist");
        var players = bartender.GetComponentsInChildren<VideoPlayer>(true);
        var front = bartender.Player;
        var back = players.Single(player => player != front);
        var showing = bartender.Showing;
        if (showing != lastVariationShowing)
        {
            Check(Get<bool>(bartender, "_switching"), "clip changes use a prepared crossfade");
            Check(back.isPrepared && back.isPlaying, "incoming transition layer is already decoded and moving");
            var overlap = (long)Math.Ceiling(VariationBlend * front.clip.frameRate);
            Check(front.frame >= (long)front.frameCount - overlap - 5,
                "transition begins in the authored closing overlap of " + front.clip.name);
            Check(back.frame >= 0 && back.frame <= (long)Math.Ceiling(VariationBlend * back.clip.frameRate) + 5,
                "incoming clip starts at its authored opening overlap");
            if (showing == "action")
            {
                Check(!back.isLooping && back.clip == variation, "random action is a non-looping one-shot");
                if (variationStarts > 0)
                    Check(now - variationReturnedAt >= TestVariationMin - .1,
                        "next action waits a fresh active interval after the previous return");
                variationStarts++;
                Check(variationStarts <= 2, "no accumulated requests burst into extra actions");
                Report.Add("Action " + variationStarts + " entered at " + Number(now - begun) + "s; baseline frame " + front.frame);
            }
            else
            {
                Check(showing == "play" && back.isLooping && front.clip == variation,
                    "one-shot action returns to the original looping baseline");
                waitingForStableReturn = true;
            }
            lastVariationShowing = showing;
        }
        if (waitingForStableReturn && !Get<bool>(bartender, "_switching"))
        {
            var remaining = Property<float>(bartender, "VariationSecondsRemaining");
            Check(remaining >= TestVariationMin - .1f && remaining <= TestVariationMax,
                "a new random wait starts only after the baseline has returned");
            variationReturns++;
            variationReturnedAt = now;
            waitingForStableReturn = false;
            Report.Add("Action " + variationReturns + " returned at " + Number(now - begun) + "s; next wait " + Number(remaining));
        }
        var changed = false;
        var coverage = 0f;
        for (var index = 0; index < players.Length; index++)
        {
            var player = players[index];
            var alpha = player.GetComponent<RawImage>().color.a;
            coverage += alpha;
            var clip = player.clip != null ? player.clip.name.GetHashCode() : 0;
            if (alpha > .01f && player.frame >= 0 && (player.frame != variationFrames[index] || clip != variationClips[index])) changed = true;
            variationFrames[index] = player.frame;
            variationClips[index] = clip;
        }
        if (coverage < .99f) throw new InvalidOperationException("both bartender layers became transparent during an action");
        if (changed) variationMotionAt = now;
        if (now - variationMotionAt > .5) throw new InvalidOperationException("bartender froze for over 0.5s during an action transition");
        if (!capturedVariation && showing == "action" && !Get<bool>(bartender, "_switching")
            && front.frame > (long)front.frameCount / 3)
        {
            Save("hall-pour-action.png");
            capturedVariation = true;
            // Synchronous PNG writing is harness work, not evidence of a native decoding hold.
            ResetVariationMotion(EditorApplication.timeSinceStartup);
        }
    }

    private static void ResetVariationMotion(double now)
    {
        var players = bartender.GetComponentsInChildren<VideoPlayer>(true);
        variationFrames = players.Select(player => player.frame).ToArray();
        variationClips = players.Select(player => player.clip != null ? player.clip.name.GetHashCode() : 0).ToArray();
        variationMotionAt = now;
    }

    private static string LayerTrace(VideoPlayer player) => string.Join(",", player.clip != null ? player.clip.name : "none",
        player.frame, Number(player.GetComponent<RawImage>().color.a));

    private static T Property<T>(object value, string property) => (T)value.GetType().GetProperty(property, Fields).GetValue(value);

    private static void ValidateResident(HubClubVideo video, string id, bool hasIdle)
    {
        Check(video.HasIdle == hasIdle && video.Viewport == Get<RectTransform>(hub, "_cityArea"), id + " uses the expected clip set and real hub viewport");
        Check(video.Material != null && video.Material.shader.name == "CardCity/HubVideoMask"
            && video.Material.GetTexture("_MaskTex") == Resources.Load<Texture2D>("UI/Hub/Video/club-" + id + "-mask"), id + " uses its production mask");
        var layers = video.GetComponentsInChildren<VideoPlayer>(true);
        Check(layers.Length == 2 && layers.All(player => player.audioOutputMode == VideoAudioOutputMode.None), id + " has two silent decoder layers");
        foreach (var player in layers) player.errorReceived += (source, message) => Errors.Add(video.name + ": " + message);
        var live = video.transform.parent.GetComponentsInChildren<Image>().First(image => image.name.StartsWith("Live "));
        Check(video.transform.GetSiblingIndex() < live.transform.GetSiblingIndex(), id + " renders beneath foreground table residents");
    }

    private static bool Ready(HubClubVideo video) => video.Player.isPrepared && video.Player.isPlaying
        && video.Player.frame >= 2 && Get<float>(video, "_alpha") >= .999f;

    private static bool InView(HubClubVideo video)
    {
        if (!video.isActiveAndEnabled) return false;
        var resident = new Vector3[4]; var viewport = new Vector3[4];
        ((RectTransform)video.transform).GetWorldCorners(resident);
        video.Viewport.GetWorldCorners(viewport);
        return resident[2].x > viewport[0].x && resident[0].x < viewport[2].x
            && resident[2].y > viewport[0].y && resident[0].y < viewport[2].y;
    }

    private static void StartTable(string id)
    {
        var index = Array.FindIndex(places, place => (string)place.GetType().GetField("Id").GetValue(place) == id);
        Check(index >= 0, "table exists: " + id);
        Call(hub, "LookAtPlace", places[index]);
        Call(hub, "StartVisit", index);
        for (var frame = 0; frame < 200 && Get<int>(hub, "_visitPhase") == 1; frame++) Call(hub, "MoveVisit", 1 / 60f);
        Call(hub, "UpdateInteriorControls");
    }

    private static void LeaveTable()
    {
        hub.GoHome();
        for (var frame = 0; frame < 200 && Get<int>(hub, "_visitPhase") != 0; frame++) Call(hub, "MoveVisit", 1 / 60f);
        Call(hub, "UpdateInteriorControls");
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
            shots++;
        }
        finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(capture); }
    }

    private static void Advance(double now) { stage++; stageTime = now; }
    private static string Number(double value) => value.ToString("F4", CultureInfo.InvariantCulture);
    private static T Get<T>(object value, string field) => (T)value.GetType().GetField(field, Fields).GetValue(value);
    private static object Call(object value, string method, params object[] args) => value.GetType().GetMethod(method, Fields).Invoke(value, args);
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
        EditorSettings.enterPlayModeOptionsEnabled = previousPlayOptionsEnabled;
        EditorSettings.enterPlayModeOptions = previousPlayOptions;
        File.WriteAllLines(Path.Combine(output, "trace.csv"), Trace);
        File.WriteAllLines(Path.Combine(output, "runtime-errors.txt"), Errors);
        File.WriteAllText(Path.Combine(output, "verification.txt"),
            (error == null ? checks + " assertions passed; " + shots + " screenshots.\n" : "FAILED: " + error + "\n") + string.Join("\n", Report));
        if (error != null) Debug.LogException(error);
        else Debug.Log("BARTENDER VIDEO QA PASSED: " + checks);
        EditorApplication.Exit(error == null ? 0 : 1);
    }
}
