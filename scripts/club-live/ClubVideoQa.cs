// Copy to an isolated Unity project's Assets/Scripts/Editor. Run -executeMethod ClubVideoQa.Run
// with CLUB_VIDEO_QA_OUTPUT set to an artifact directory. Uses real VideoPlayers in Play Mode.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using CardGameClient;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

public static class ClubVideoQa
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static HubClubVideo video;
    private static Camera camera;
    private static RenderTexture target;
    private static Texture2D capture;
    private static string output;
    private static readonly List<string> report = new List<string>();
    private static readonly List<string> trace = new List<string>();
    private static int stage, checks, shot;
    private static double begun, stageTime, lastShot;
    private static bool sawStart, sawSlow, finished;
    private static float previousSpeed;
    private static long observedFrame = -1;
    private static double lastFrameChange, longestFrameHold;
    private static int loopRestarts;

    public static void Run()
    {
        output = Environment.GetEnvironmentVariable("CLUB_VIDEO_QA_OUTPUT");
        Directory.CreateDirectory(output);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        // Prevent the real bootstrap from opening a profile or starting the hub.
        new GameObject("Disabled app for isolated video QA").AddComponent<AppFlowController>().enabled = false;
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
            var canvas = new GameObject("Video QA canvas", typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(628, 640);
            camera = new GameObject("Video QA camera").AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 320;
            camera.transform.position = new Vector3(0, 0, -100);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            target = new RenderTexture(628, 640, 24);
            camera.targetTexture = target;
            canvas.worldCamera = camera;
            capture = new Texture2D(628, 640, TextureFormat.RGB24, false);
            var root = new GameObject("Pianist under test", typeof(RectTransform));
            root.transform.SetParent(canvas.transform, false);
            ((RectTransform)root.transform).sizeDelta = new Vector2(628, 640);
            video = root.AddComponent<HubClubVideo>();
            video.Stopped = true;
            VideoClip Clip(string kind) => Resources.Load<VideoClip>("UI/Hub/Video/club-pianist-" + kind);
            video.Setup(Texture2D.whiteTexture, Clip("play"), Clip("idle"), Clip("stop"), Clip("start"),
                Resources.Load<TextAsset>("UI/Hub/Video/club-pianist-entry").text);
            begun = stageTime = EditorApplication.timeSinceStartup;
            trace.Add("time,stage,showing,frontFrame,frontSpeed,backFrame,backSpeed,frontAlpha,backAlpha");
            EditorApplication.update += Tick;
        }
        catch (Exception error) { Finish(error); }
    }

    private static T Get<T>(string field) => (T)typeof(HubClubVideo).GetField(field, Private).GetValue(video);
    private static void Check(bool ok, string label)
    {
        if (!ok) throw new InvalidOperationException(label);
        checks++;
        report.Add("PASS " + label);
    }

    private static void Tick()
    {
        if (finished || video == null) return;
        try
        {
            var now = EditorApplication.timeSinceStartup;
            var elapsed = now - stageTime;
            if (now - begun > 65) throw new TimeoutException("Video QA timed out in stage " + stage);
            var players = video.GetComponentsInChildren<VideoPlayer>();
            var images = video.GetComponentsInChildren<RawImage>();
            var stable = !Get<bool>("_switching");
            if (Get<bool>("_bakedLanding"))
                foreach (var player in players)
                    if (!Mathf.Approximately(player.playbackSpeed, 1))
                        throw new InvalidOperationException("baked landing changed the native decoder speed");
            // frame can briefly be -1 at a native loop boundary while the last texture remains visible.
            // Check actual layer opacity and the captured pixels rather than that decoder metadata.
            if (stage > 0 && images[0].color.a + images[1].color.a < .99f)
                throw new InvalidOperationException("both video layers became transparent");

            if ((stage == 1 || stage == 3) && now - lastShot >= 1.0 / 30)
            {
                lastShot = now;
                trace.Add(string.Join(",", (now - begun).ToString("F4", CultureInfo.InvariantCulture), stage,
                    video.Showing, players[0].frame, players[0].playbackSpeed.ToString("F4", CultureInfo.InvariantCulture),
                    players[1].frame, players[1].playbackSpeed.ToString("F4", CultureInfo.InvariantCulture),
                    images[0].color.a.ToString("F4", CultureInfo.InvariantCulture), images[1].color.a.ToString("F4", CultureInfo.InvariantCulture)));
                if (stage == 1) Save();
            }
            switch (stage)
            {
                case 0:
                    if (elapsed < 1.5 || video.Player.frame < 2 || Get<float>("_alpha") < .999f) break;
                    Check(video.Showing == "idle", "initial resting clip decodes");
                    Check(video.Player.canSetPlaybackSpeed, "native video decoder supports the tempo ramp");
                    StartAgain(now);
                    break;
                case 1:
                case 3:
                    sawStart |= video.Showing == "start";
                    if (!stable || video.Showing != "play") break;
                    sawSlow |= video.Player.playbackSpeed < .8f;
                    CheckMonotonicSpeed(video.Player.playbackSpeed);
                    if (stage == 1 && video.Player.playbackSpeed >= .999f)
                    {
                        var frame = video.Player.frame;
                        if (frame >= 0 && frame != observedFrame)
                        {
                            if (observedFrame >= 0 && lastFrameChange > 0)
                            {
                                longestFrameHold = Math.Max(longestFrameHold, now - lastFrameChange);
                                if (frame >= 0 && frame < observedFrame) loopRestarts++;
                            }
                            observedFrame = frame;
                            lastFrameChange = now;
                        }
                    }
                    if (elapsed < (stage == 1 ? 14 : 5) || video.Player.playbackSpeed < .999f) break;
                    if (stage == 1)
                    {
                        report.Add("Play loop restarts: " + loopRestarts + "; longest held frame: " + longestFrameHold.ToString("F3", CultureInfo.InvariantCulture) + "s");
                        Check(loopRestarts >= 2, "playing animation completes multiple uninterrupted loops");
                        Check(longestFrameHold < .4, "no long frame hold during play loops");
                    }
                    Check(sawStart && (Get<bool>("_bakedLanding") || sawSlow), "restart " + (stage == 1 ? 1 : 2) + " finishes gesture and enters the play loop");
                    Check(Mathf.Approximately(video.Player.playbackSpeed, 1), "play loop returns to normal tempo");
                    video.Stopped = true;
                    stage++;
                    stageTime = now;
                    break;
                case 2:
                    if (!stable || video.Showing != "idle" || elapsed < 3.5) break;
                    Check(Mathf.Approximately(video.Player.playbackSpeed, 1), "stopping resets speed for the resting loop");
                    StartAgain(now);
                    break;
                case 4:
                    if (!stable || video.Showing != "idle" || elapsed < 8.5) break;
                    Check(!GetEnded(), "loop completion does not mark resting video as finished");
                    video.gameObject.SetActive(false);
                    stage = 5;
                    stageTime = now;
                    break;
                case 5:
                    if (elapsed < .25) break;
                    video.gameObject.SetActive(true);
                    stage = 6;
                    stageTime = now;
                    break;
                case 6:
                    if (elapsed < .5) break;
                    Check(video.Player.isPlaying, "resting animation resumes after hiding and returning");
                    Finish(null);
                    break;
            }
        }
        catch (Exception error) { Finish(error); }
    }

    private static void StartAgain(double now)
    {
        video.Stopped = false;
        sawStart = sawSlow = false;
        previousSpeed = 0;
        stage++;
        stageTime = now;
    }

    private static bool GetEnded()
    {
        var layer = Get<object>("_front");
        return (bool)layer.GetType().GetField("Ended").GetValue(layer);
    }

    private static void CheckMonotonicSpeed(float speed)
    {
        if (speed + .001f < previousSpeed) throw new InvalidOperationException("playback speed fell during acceleration");
        previousSpeed = speed;
    }

    private static void Save()
    {
        Canvas.ForceUpdateCanvases();
        camera.Render();
        var active = RenderTexture.active;
        RenderTexture.active = target;
        capture.ReadPixels(new Rect(0, 0, 628, 640), 0, 0);
        capture.Apply();
        RenderTexture.active = active;
        File.WriteAllBytes(Path.Combine(output, "frame-" + (shot++).ToString("D4") + ".png"), capture.EncodeToPNG());
        long brightness = 0;
        var pixels = capture.GetPixels32();
        foreach (var pixel in pixels) brightness += pixel.r + pixel.g + pixel.b;
        if (brightness < pixels.Length * 20L)
            throw new InvalidOperationException("rendered video became blank during transition");
    }

    private static void Finish(Exception error)
    {
        finished = true;
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= OnPlayMode;
        File.WriteAllLines(Path.Combine(output, "trace.csv"), trace);
        File.WriteAllText(Path.Combine(output, "verification.txt"), error == null
            ? checks + " assertions passed. " + shot + " native Unity frames captured.\n" + string.Join("\n", report)
            : error.ToString());
        if (error != null) Debug.LogException(error);
        EditorApplication.Exit(error == null ? 0 : 1);
    }
}
