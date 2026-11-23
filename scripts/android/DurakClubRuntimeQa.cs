using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CardGame.Bots;
using CardGameClient;
using CardGame.Core.Games.Durak;
using CardGame.Core.Meta;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static partial class MobileLayoutQa
{
    private static DurakTableController runtimeTable;
    private static readonly List<string> runtimeErrors = new List<string>();
    private static readonly List<string> runtimeReport = new List<string>();
    private static double runtimeStarted, runtimeStageAt;
    private static int runtimeStage, runtimeMatch, runtimeHumanMoves;
    private static Vector2 runtimeTablePosition;
    private static Vector2[] runtimeFlightStarts;
    private static bool runtimeSawMovement;
    private static Image runtimeSeatLight;
    private static float runtimeLightMin = 1, runtimeLightMax, runtimeReducedAlpha;
    private static bool runtimeReducedSet, runtimeReducedSampled;
    private static bool runtimeDone, runtimeOldOptionsEnabled;
    private static EnterPlayModeOptions runtimeOldOptions;
    private static readonly SimpleDurakBot runtimePolicy = new SimpleDurakBot(new System.Random(83));

    public static void RunDurakClubRuntime()
    {
        Directory.CreateDirectory(Output);
        PlayerHubRuntime.UseService(new LocalPlayerHubService(new Store(), new Clock()));
        PlayerHubRuntime.LoadAsync().GetAwaiter().GetResult();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("Disabled bootstrap").AddComponent<AppFlowController>().enabled = false;
        runtimeOldOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
        runtimeOldOptions = EditorSettings.enterPlayModeOptions;
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
        Application.logMessageReceived += ClubRuntimeLog;
        EditorApplication.playModeStateChanged += ClubRuntimeEntered;
        EditorApplication.EnterPlaymode();
    }

    private static void ClubRuntimeEntered(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode) return;
        try
        {
            typeof(AppFlowController).Assembly.GetType("CardGameClient.MobileUi").GetField("TestPhone").SetValue(null, true);
            Application.targetFrameRate = 60; QualitySettings.vSyncCount = 0;
            Begin(1600, 900); capturePrefix = "runtime-";
            var root = new GameObject("Runtime Durak", typeof(RectTransform)); root.transform.SetParent(canvasObject.transform, false);
            ((RectTransform)root.transform).sizeDelta = new Vector2(1600, 900);
            runtimeTable = root.AddComponent<DurakTableController>(); runtimeTable.enabled = false;
            runtimeTable.Configure(DurakMatchSettings.Default, () => { });
            Call(runtimeTable, "Start"); runtimeTable.StopAllCoroutines();
            var names = Get<DurakGame>(runtimeTable, "_game").Players.Select(p => p.Name).ToArray();
            DurakShowFixture(runtimeTable, DurakOccupiedFixture(names, false));
            runtimeTablePosition = Get<RectTransform>(runtimeTable, "_tableRow").anchoredPosition;
            Save("collection-before.png");
            runtimeTable.StartCoroutine((IEnumerator)Call(runtimeTable, "PlayBoutCollection", new object[] { null }));
            runtimeStarted = runtimeStageAt = EditorApplication.timeSinceStartup;
            EditorApplication.update += ClubRuntimeTick;
        }
        catch (Exception e) { FinishClubRuntime(e); }
    }

    private static void ClubRuntimeTick()
    {
        if (runtimeDone) return;
        try
        {
            var now = EditorApplication.timeSinceStartup;
            EditorApplication.QueuePlayerLoopUpdate();
            if (now - runtimeStarted > 220) throw new TimeoutException("Runtime stage " + runtimeStage + ", match " + runtimeMatch);
            if (runtimeErrors.Count > 0) throw new Exception(string.Join("\n", runtimeErrors));
            var elapsed = now - runtimeStageAt;
            if (runtimeStage == 0)
            {
                var flights = Get<RectTransform>(runtimeTable, "_flyLayer").GetComponentsInChildren<CardView>();
                if (flights.Length == 0) return;
                Check(flights.Length == 11, "runtime collects eleven individual card views");
                Check(Get<RectTransform>(runtimeTable, "_tableRow").anchoredPosition == runtimeTablePosition, "collection keeps table layout stationary");
                Check(flights.Select(c => c.Rect.anchoredPosition).Distinct().Count() > 1, "collection preserves staggered trajectories");
                runtimeFlightStarts = flights.Select(c => c.Rect.anchoredPosition).ToArray();
                runtimeStage = 1;
            }
            else if (runtimeStage == 1)
            {
                var flights = Get<RectTransform>(runtimeTable, "_flyLayer").GetComponentsInChildren<CardView>();
                if (flights.Length > 0)
                {
                    if (!runtimeSawMovement && (flights[0].Rect.anchoredPosition - runtimeFlightStarts[0]).sqrMagnitude > 25)
                    { runtimeSawMovement = true; Save("collection-flight.png"); }
                    return;
                }
                Check(runtimeSawMovement, "collection advances across actual play-mode frames");
                Check(Get<int>(runtimeTable, "_clubDiscardCount") == 11, "collected cards increment discard count");
                Check(Get<RectTransform>(runtimeTable, "_flyLayer").childCount == 0, "completed flights leave no orphan cards");
                Check(Get<RectTransform>(runtimeTable, "_clubDiscardCards").childCount >= 3, "discard stack is visible after collection");
                Save("collection-after.png");
                runtimeReport.Add("Individual card collection, stationary table, discard stack and cleanup passed.");
                Set(runtimeTable, "_clubReactionAt", -10f);
                Call(runtimeTable, "SendClubReaction", "Проверка общего чата");
                Call(runtimeTable, "ShowClubSocial");
                Check(Get<List<Button>>(runtimeTable, "_clubSocialButtons").All(b => !b.interactable), "chat cooldown starts immediately");
                runtimeSeatLight = runtimeTable.GetComponentsInChildren<MonoBehaviour>().First(c => c.GetType().Name == "DurakSeatLight").GetComponent<Image>();
                runtimeStageAt = now; runtimeStage = 15;
            }
            else if (runtimeStage == 15)
            {
                Call(runtimeTable, "RefreshClubSocialState");
                if (!runtimeReducedSet)
                {
                    runtimeLightMin = Mathf.Min(runtimeLightMin, runtimeSeatLight.color.a);
                    runtimeLightMax = Mathf.Max(runtimeLightMax, runtimeSeatLight.color.a);
                    if (elapsed > .7)
                    {
                        Check(runtimeLightMax - runtimeLightMin > .03f, "active seat softly pulses across real frames");
                        PlayerHubRuntime.Current.Settings.ReducedMotion = true; runtimeReducedSet = true;
                    }
                }
                else if (!runtimeReducedSampled && elapsed > 1.1) { runtimeReducedAlpha = runtimeSeatLight.color.a; runtimeReducedSampled = true; }
                if (elapsed < 2.2) return;
                Check(Mathf.Abs(runtimeReducedAlpha - runtimeSeatLight.color.a) < .001f, "reduced motion keeps the turn light steady");
                Check(Get<List<Button>>(runtimeTable, "_clubSocialButtons").All(b => b.interactable), "public chat cooldown unlocks without reopening");
                Check(Get<Text>(runtimeTable, "_clubChatLog").text.Contains("Проверка общего чата"), "broadcast remains in shared history");
                Call(runtimeTable, "CloseClubPopup");
                runtimeReport.Add("Public chat cooldown/history and active-seat pulse/reduced motion passed.");
                runtimeStage = 2; StartClubRuntimeMatch();
            }
            else if (runtimeStage == 2)
            {
                var game = Get<DurakGame>(runtimeTable, "_game");
                if (game.IsFinished && Get<Button>(runtimeTable, "_restartButton").gameObject.activeSelf)
                {
                    Check(runtimeHumanMoves > 0, "runtime human controller submitted moves");
                    Check(Get<RectTransform>(runtimeTable, "_flyLayer").childCount == 0, "match ends without active flights");
                    runtimeReport.Add("Completed match " + runtimeMatch + ": players=" + game.Players.Count + ", bouts=" + game.BoutsFinished + ", human moves=" + runtimeHumanMoves + ".");
                    Save("match-" + runtimeMatch + "-finished.png");
                    runtimeMatch++;
                    if (runtimeMatch == 3) { FinishClubRuntime(null); return; }
                    StartClubRuntimeMatch();
                    return;
                }
                if (Get<bool>(runtimeTable, "_waitingForHuman") && !Get<DurakMove?>(runtimeTable, "_pendingMove").HasValue)
                {
                    var move = runtimePolicy.ChooseMove(game, 0);
                    Check(move.HasValue, "human prompt has an available action");
                    Call(runtimeTable, "QueueClubMove", move.Value); runtimeHumanMoves++;
                }
                foreach (var view in runtimeTable.GetComponentsInChildren<CardView>())
                    if (float.IsNaN(view.Rect.anchoredPosition.x) || float.IsNaN(view.Rect.anchoredPosition.y)) throw new Exception("Non-finite card position");
            }
        }
        catch (Exception e) { FinishClubRuntime(e); }
    }

    private static void StartClubRuntimeMatch()
    {
        runtimeTable.StopAllCoroutines();
        PlayerHubRuntime.Current.Settings.ReducedMotion = runtimeMatch == 2;
        runtimeTable.Configure(new DurakMatchSettings { PlayerCount = runtimeMatch == 1 ? 6 : 3, DeckSize = DeckKind.ThirtySix,
            AllowTransfer = runtimeMatch == 1, AllowCheating = runtimeMatch == 1, ThrowInMode = ThrowInMode.All }, () => { });
        UnityEngine.Random.InitState(709 + runtimeMatch);
        Time.timeScale = 18;
        runtimeHumanMoves = 0;
        Call(runtimeTable, "StartNewGame");
    }

    private static void ClubRuntimeLog(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert || (type == LogType.Warning && message.Contains("Ход отклонён"))) runtimeErrors.Add(message + "\n" + stack); }

    private static void FinishClubRuntime(Exception error)
    {
        if (runtimeDone) return;
        runtimeDone = true; Time.timeScale = 1;
        EditorApplication.update -= ClubRuntimeTick;
        EditorApplication.playModeStateChanged -= ClubRuntimeEntered;
        Application.logMessageReceived -= ClubRuntimeLog;
        EditorSettings.enterPlayModeOptionsEnabled = runtimeOldOptionsEnabled;
        EditorSettings.enterPlayModeOptions = runtimeOldOptions;
        File.WriteAllText(Path.Combine(Output, "runtime-verification.txt"), (error == null ? checks + " checks passed.\n" : "FAILED: " + error + "\n") + string.Join("\n", runtimeReport));
        File.WriteAllLines(Path.Combine(Output, "runtime-errors.txt"), runtimeErrors);
        if (error != null) Debug.LogException(error);
        EditorApplication.Exit(error == null ? 0 : 1);
    }
}
