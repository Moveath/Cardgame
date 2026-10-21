// Isolated Unity editor render of phone game layouts. Memory-only profile.
using System;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Threading.Tasks;
using CardGameClient;
using CardGame.Core.Meta;
using CardGame.Core.Games.Chests;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
public static partial class MobileLayoutQa
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly string Output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../",
        Environment.GetEnvironmentVariable("MOBILE_LAYOUT_QA_OUTPUT") ?? "mobile-readability/games"));
    private static string capturePrefix;
    private static GameObject canvasObject;
    private static Camera camera;
    private static int checks;
    private sealed class Store : IHubStore {
        public Task<HubSnapshot> LoadAsync()=>Task.FromResult<HubSnapshot>(null);
        public Task SaveAsync(HubSnapshot state)=>Task.CompletedTask;
    }
    private sealed class Clock : IHubClock { public long UnixSeconds => 1791740000; }
    public static void Run() {
        Directory.CreateDirectory(Output);
        var previousError = Path.Combine(Output, "error.txt");
        if (File.Exists(previousError)) File.Delete(previousError);
        try {
            typeof(AppFlowController).Assembly.GetType("CardGameClient.MobileUi").GetField("TestPhone").SetValue(null,true);
            PlayerHubRuntime.UseService(new LocalPlayerHubService(new Store(), new Clock()));
            PlayerHubRuntime.LoadAsync().GetAwaiter().GetResult();
            foreach(var aspect in new[]{16f/9,20f/9})
            foreach(var playerCount in new[]{3,6})
            foreach(var game in new[]{"durak","chests","bluff","govno","poker"}) {
                // CanvasScaler.Expand keeps at least the reference width AND height.
                var referenceHeight = game == "chests" || game == "bluff" ? 900 : 820;
                var height = Mathf.RoundToInt(Mathf.Max(referenceHeight,1600/aspect));
                var width = Mathf.RoundToInt(height * aspect);
                capturePrefix = game + "-" + playerCount + "p-" + width + "x" + height + "-";
                Begin(width,height);
                var root=new GameObject("Table",typeof(RectTransform)); root.transform.SetParent(canvasObject.transform,false);
                ((RectTransform)root.transform).sizeDelta=new Vector2(width,height);
                MonoBehaviour table;
                switch(game) {
                    case "durak": var d=root.AddComponent<DurakTableController>();d.Configure(new DurakMatchSettings{PlayerCount=playerCount,DeckSize=DeckKind.ThirtySix},()=>{});table=d;break;
                    case "chests": var c=root.AddComponent<ChestsTableController>();c.Configure(new ChestsMatchSettings{PlayerCount=playerCount,DeckSize=DeckKind.ThirtySix},()=>{});table=c;break;
                    case "bluff": var b=root.AddComponent<BluffTableController>();b.Configure(new BluffMatchSettings{PlayerCount=playerCount,DeckSize=DeckKind.FiftyTwo},()=>{});table=b;break;
                    case "govno": var g=root.AddComponent<GovnoTableController>();g.Configure(new GovnoMatchSettings{PlayerCount=playerCount},()=>{});table=g;break;
                    default: var p=root.AddComponent<PokerTableController>();p.Configure(new PokerMatchSettings{PlayerCount=playerCount},()=>{});table=p;break;
                }
                table.enabled=false;Call(table,"Start");table.StopAllCoroutines();
                if(game=="durak"||game=="bluff"||game=="chests") {Set(table,"_waitingForHuman",true);Call(table,"Refresh");}
                if(game=="govno") {Call(table,"StartMatch");Call(table,"UpdatePresentation",3.1f);Call(table,"RefreshForeground",1f);}
                Save(game+".png");
                if(game=="durak") CheckDurakLayout(table);
                else CheckOtherLayout(table,game);
                if(game=="chests") {
                    var state=Get<ChestsGame>(table,"_game");
                    var target=state.Players.Skip(1).First(p=>p.Hand.Any(c=>state.AskableRanks(0).Contains(c.Rank) && state.Players[0].Hand.Count(a=>a.Rank==c.Rank)+p.Hand.Count(a=>a.Rank==c.Rank)<4));
                    var rank=target.Hand.First(c=>state.AskableRanks(0).Contains(c.Rank) && state.Players[0].Hand.Count(a=>a.Rank==c.Rank)+target.Hand.Count(a=>a.Rank==c.Rank)<4).Rank;
                    Check(state.TryApplyMove(0,ChestsMove.Ask(state.Players.ToList().IndexOf(target),rank),out _),"ask rank");
                    Check(state.TryApplyMove(0,ChestsMove.GuessCount(target.Hand.Count(c=>c.Rank==rank)),out _),"guess count");
                    Check(state.Phase == ChestsPhase.GuessingSuits,"actual suit phase");
                    Call(table,"Refresh");Save("chests-suits.png");
                    var symbols = table.GetComponentsInChildren<Text>().Where(t=>t.text.Length==1 && "\u2660\u2665\u2666\u2663".Contains(t.text)).ToArray();
                    Check(symbols.Length==4,"all four suit controls exist");
                    foreach(var text in symbols) {
                        text.font.RequestCharactersInTexture(text.text,text.fontSize,text.fontStyle);
                        text.font.GetCharacterInfo(text.text[0],out var glyph,text.fontSize,text.fontStyle);
                        Check(glyph.glyphWidth>0,"visible suit "+text.text);
                    }
                }
            }
            File.WriteAllText(Path.Combine(Output,"verification.txt"),checks+" checks passed; five phone games with three/six players at 16:9 and 20:9, including occupied Durak tables and actual Chests suit selectors. Memory-only profile.");EditorApplication.Exit(0);
        } catch(Exception e) {File.WriteAllText(Path.Combine(Output,"error.txt"),e.ToString());Debug.LogException(e);EditorApplication.Exit(1);}
    }
    private static T Get<T>(object obj, string name) => (T)obj.GetType().GetField(name, Private).GetValue(obj);
    private static void Set(object obj, string name, object value) => obj.GetType().GetField(name, Private).SetValue(obj, value);
    private static object Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Private).Invoke(obj, args);
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception("FAILED: " + label);
        checks++;
    }

    private static void Begin(int width, int height)
    {
        if (canvasObject != null) UnityEngine.Object.DestroyImmediate(canvasObject);
        if (camera != null)
        {
            var rt = camera.targetTexture;
            UnityEngine.Object.DestroyImmediate(camera.gameObject);
            UnityEngine.Object.DestroyImmediate(rt);
        }
        canvasObject = new GameObject("QA Canvas", typeof(RectTransform), typeof(Canvas));
        ((RectTransform)canvasObject.transform).sizeDelta = new Vector2(width, height);
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        camera = new GameObject("QA Camera", typeof(Camera)).GetComponent<Camera>();
        camera.transform.position = new Vector3(0, 0, -100);
        camera.orthographic = true;
        camera.orthographicSize = height / 2f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.075f, .25f, .18f);
        camera.nearClipPlane = .1f;
        camera.farClipPlane = 200;
        camera.targetTexture = new RenderTexture(width, height, 24);
        camera.targetTexture.Create();
        canvas.worldCamera = camera;
    }

    private static void Save(string name)
    {
        Canvas.ForceUpdateCanvases();
        camera.Render();
        var previous = RenderTexture.active;
        RenderTexture.active = camera.targetTexture;
        var texture = new Texture2D(camera.targetTexture.width, camera.targetTexture.height, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
        texture.Apply();
        File.WriteAllBytes(Path.Combine(Output, capturePrefix + name), texture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(texture);
        RenderTexture.active = previous;
    }
}
