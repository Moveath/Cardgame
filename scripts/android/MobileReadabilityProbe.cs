// Copied ONLY to an isolated QA APK. Uses a memory profile; never opens the player's save.
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using CardGame.Core;
using CardGame.Core.Meta;
using CardGame.Core.Games.Chests;
using CardGameClient;
using UnityEngine;
using UnityEngine.UI;

public sealed class MobileReadabilityProbe : MonoBehaviour
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private string last;
    private float checkAt;
    private sealed class Store : IHubStore
    {
        private HubSnapshot value;
        public Task<HubSnapshot> LoadAsync() => Task.FromResult(value);
        public Task SaveAsync(HubSnapshot snapshot) { value = snapshot; return Task.CompletedTask; }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void StartQa()
    {
        PlayerHubRuntime.UseService(new LocalPlayerHubService(new Store(), new UnityHubClock()));
        var go = new GameObject("Mobile UI QA");
        DontDestroyOnLoad(go); go.AddComponent<MobileReadabilityProbe>();
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
    }
    private static object Get(object obj, string field) => obj.GetType().GetField(field, Private).GetValue(obj);
    private static object Call(object obj, string method, params object[] args) => obj.GetType().GetMethod(method, Private).Invoke(obj, args);
    private void Update()
    {
        if (Time.unscaledTime < checkAt) return;
        checkAt = Time.unscaledTime + .5f;
        var file = Path.Combine(Application.persistentDataPath, "mobile-qa.txt");
        if (!File.Exists(file)) return;
        var command = File.ReadAllText(file).Trim();
        if (command == last) return; last = command;
        try
        {
            var args = command.Split(' ');
            var flow = FindAnyObjectByType<AppFlowController>();
            var hub = (PlayerHubController)Get(flow, "_playerHub");
            switch (args[0])
            {
                case "route":
                    Call(flow, "ShowScreen", Get(flow, "_mainMenuScreen"));
                    hub.Navigate(args[1]); break;
                case "play":
                    Call(flow, "ShowScreen", Get(flow, "_mainMenuScreen"));
                    Call(hub, "PrepareGameSetup", "quick", args[1]);
                    Call(hub, "SelectGameSetupPlayerCount", int.Parse(args[2]));
                    Call(flow, "StartHubQuickGame", args[1]); break;
                case "suits": StartCoroutine(Suits()); break;
                case "dump": Dump(); break;
            }
            Debug.Log("MOBILE_QA " + command + " OK");
        }
        catch (Exception ex) { Debug.LogError("MOBILE_QA " + command + " " + ex); }
    }
    private IEnumerator Suits()
    {
        var table = FindAnyObjectByType<ChestsTableController>();
        var game = (ChestsGame)Get(table, "_game");
        // Reach the actual suit selector through the normal question coroutine.
        for (var i = 0; i < 80; i++)
        {
            if ((bool)Get(table, "_waitingForHuman"))
            {
                if (game.Phase == ChestsPhase.Asking)
                {
                    var target = game.Players.Skip(1).First(p => p.Hand.Any(c => game.AskableRanks(0).Contains(c.Rank) && game.Players[0].Hand.Count(a=>a.Rank==c.Rank)+p.Hand.Count(a=>a.Rank==c.Rank)<4));
                    var targetIndex = game.Players.ToList().IndexOf(target);
                    Call(table, "ChooseTarget", targetIndex);
                    Call(table, "ChooseRank", target.Hand.First(c => game.AskableRanks(0).Contains(c.Rank) && game.Players[0].Hand.Count(a=>a.Rank==c.Rank)+target.Hand.Count(a=>a.Rank==c.Rank)<4).Rank);
                }
                else if (game.Phase == ChestsPhase.GuessingCount)
                {
                    var rank = game.AskedRank.Value;
                    Call(table, "ChooseCount", game.Players[game.TargetIndex.Value].Hand.Count(c => c.Rank == rank));
                }
                else if (game.Phase == ChestsPhase.GuessingSuits) { Dump(); yield break; }
            }
            yield return new WaitForSecondsRealtime(.25f);
        }
        Debug.LogError("MOBILE_QA suit selector timeout");
    }
    private static void Dump()
    {
        var sb = new StringBuilder();
        sb.AppendLine("SCREEN " + Screen.width + "x" + Screen.height + " dpi=" + Screen.dpi + " safe=" + Screen.safeArea);
        var font = Resources.Load<Font>("Fonts/GameSans");
        font.RequestCharactersInTexture("♠♥♦♣Ёя", 48);
        foreach (var c in "♠♥♦♣Ёя")
        {
            font.GetCharacterInfo(c, out var glyph, 48);
            sb.AppendLine("GLYPH " + c + " " + font.HasCharacter(c) + " width=" + glyph.glyphWidth);
        }
        var corners = new Vector3[4];
        foreach (var text in FindObjectsByType<Text>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (string.IsNullOrEmpty(text.text)) continue;
            text.rectTransform.GetWorldCorners(corners);
            sb.AppendLine(text.text.Replace('\n', '|') + " size=" + text.fontSize + " bounds=" + corners[0] + "/" + corners[2]);
        }
        File.WriteAllText(Path.Combine(Application.persistentDataPath, "mobile-qa-report.txt"), sb.ToString());
    }
}
