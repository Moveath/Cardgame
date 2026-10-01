// Copy to the isolated Unity project's Assets/Scripts/Editor, then run
// -executeMethod CitySignQa.Run. CITY_SIGN_QA_OUTPUT selects a screens subfolder;
// CITY_SIGN_QA_ART optionally selects one district for low-memory editor runs.
// Exercises the actual AddSigns implementation without starting the hub or opening a profile store.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CardGameClient;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class CitySignQa
{
    private const int Width = 1672, Height = 941;
    private static readonly string[] Arts = { "card-city-old-town", "card-city", "card-city-tuzyara", "card-city-port", "card-city-underground", "card-city-hill" };
    private static readonly List<string> Report = new List<string>();
    private static readonly List<string> Errors = new List<string>();
    private static Camera camera;
    private static string output;
    private static int checks;

    public static void Run()
    {
        EditorApplication.delayCall += RunAfterStartup;
    }

    private static async void RunAfterStartup()
    {
        output = FindOutput();
        Directory.CreateDirectory(output);
        Application.logMessageReceived += OnLog;
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var canvasObject = new GameObject("City sign QA canvas", typeof(RectTransform), typeof(Canvas));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(Width, Height);
            camera = new GameObject("City sign QA camera", typeof(Camera)).GetComponent<Camera>();
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.magenta;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 200;
            canvas.worldCamera = camera;
            var host = new GameObject("Disabled hub, no profile load", typeof(RectTransform));
            host.SetActive(false);
            host.transform.SetParent(canvas.transform, false);
            var hub = host.AddComponent<PlayerHubController>();
            hub.enabled = false;
            var addSigns = typeof(PlayerHubController).GetMethod("AddSigns", BindingFlags.Instance | BindingFlags.NonPublic);
            Check(addSigns != null, "production AddSigns exists");
            Report.Add("Native Unity render: 1672x941 districts, 2x camera closeups. No runtime hub Start or profile store accessed.");
            foreach (var art in Arts)
            {
                var selectedArt = Environment.GetEnvironmentVariable("CITY_SIGN_QA_ART");
                if (!string.IsNullOrEmpty(selectedArt) && selectedArt != art) continue;
                var sprite = Resources.Load<Sprite>("UI/Hub/" + art);
                Check(sprite != null, "loaded " + art);
                var page = new GameObject(art, typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
                page.SetParent(canvas.transform, false);
                page.anchorMin = page.anchorMax = page.pivot = new Vector2(.5f, .5f);
                page.sizeDelta = new Vector2(Width, Height);
                var background = page.GetComponent<Image>();
                background.sprite = sprite;
                background.raycastTarget = false;
                addSigns.Invoke(hub, new object[] { page, art, (float)Width });
                await NextFrame();
                Save(art + "-full.png", new Rect(-Width / 2f, -Height / 2f, Width, Height), 1);
                var labels = page.GetComponentsInChildren<Text>();
                Check(labels.Length > 0, art + " has signs");
                Check(labels.Select(label => label.text).Distinct().Count() == labels.Length, art + " has no duplicate signs");
                foreach (var label in labels)
                {
                    Check(!label.raycastTarget, label.text + " does not block city clicks");
                    Check(label.GetComponent<HubSignText>() != null, label.text + " has sign mesh effect");
                    var mesh = label.canvasRenderer.GetMesh();
                    Check(mesh != null, label.text + " has a renderer mesh");
                    Check(mesh.vertexCount > 0, label.text + " has visible geometry");
                    Check(mesh.vertices.All(vertex => IsFinite(vertex.x) && IsFinite(vertex.y)), label.text + " has finite vertices");
                    var points = mesh.vertices.Select(vertex => label.transform.TransformPoint(vertex)).ToArray();
                    Check(points.All(point => point.x >= -Width / 2f && point.x <= Width / 2f
                        && point.y >= -Height / 2f && point.y <= Height / 2f), label.text + " remains within the district");
                    Report.Add(art + ": " + label.text + ", glyph mesh " + mesh.vertexCount + " vertices, bounds " + mesh.bounds);
                    var holder = (RectTransform)label.transform.parent;
                    var corners = new Vector3[4];
                    holder.GetWorldCorners(corners);
                    var center = (corners[0] + corners[2]) / 2;
                    var cropWidth = Mathf.Max(220, Vector3.Distance(corners[0], corners[3]) + 120);
                    var cropHeight = Mathf.Max(160, Vector3.Distance(corners[0], corners[1]) + 110);
                    var crop = new Rect(center.x - cropWidth / 2, center.y - cropHeight / 2, cropWidth, cropHeight);
                    Save(art + "-" + label.text.Replace(' ', '-') + ".png", crop, 2);
                    await NextFrame();
                }
                UnityEngine.Object.DestroyImmediate(page.gameObject);
            }
            Check(Errors.Count == 0, "no Unity errors: " + string.Join(" | ", Errors));
            Report.Insert(0, checks + " focused sign assertions passed.");
            var selected = Environment.GetEnvironmentVariable("CITY_SIGN_QA_ART");
            File.WriteAllLines(Path.Combine(output, "verification" + (string.IsNullOrEmpty(selected) ? "" : "-" + selected) + ".txt"), Report);
            Debug.Log("CitySignQa passed " + checks + " assertions: " + output);
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(output, "error.txt"), error.ToString());
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }

    private static void Save(string name, Rect world, int scale)
    {
        camera.transform.position = new Vector3(world.center.x, world.center.y, -100);
        camera.orthographicSize = world.height / 2;
        if (camera.targetTexture != null)
        {
            var old = camera.targetTexture;
            camera.targetTexture = null;
            old.Release();
            UnityEngine.Object.DestroyImmediate(old);
        }
        camera.targetTexture = new RenderTexture(Mathf.RoundToInt(world.width * scale), Mathf.RoundToInt(world.height * scale), 24);
        Check(camera.targetTexture.Create(), "render target allocated: " + name);
        Canvas.ForceUpdateCanvases();
        camera.Render();
        var previous = RenderTexture.active;
        RenderTexture.active = camera.targetTexture;
        var pixels = new Texture2D(camera.targetTexture.width, camera.targetTexture.height, TextureFormat.RGB24, false);
        pixels.ReadPixels(new Rect(0, 0, pixels.width, pixels.height), 0, 0);
        pixels.Apply();
        var sampleColors = new HashSet<Color32>();
        for (var y = 0; y < pixels.height; y += Mathf.Max(1, pixels.height / 20))
            for (var x = 0; x < pixels.width; x += Mathf.Max(1, pixels.width / 20))
                sampleColors.Add(pixels.GetPixel(x, y));
        Check(sampleColors.Count > 16, "rendered image contains actual artwork: " + name);
        File.WriteAllBytes(Path.Combine(output, name), pixels.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(pixels);
        RenderTexture.active = previous;
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static Task NextFrame()
    {
        var next = new TaskCompletionSource<bool>();
        EditorApplication.delayCall += () => next.SetResult(true);
        return next.Task;
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        checks++;
    }
    private static void OnLog(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) Errors.Add(message);
    }
    private static string FindOutput()
    {
        for (var folder = new DirectoryInfo(Application.dataPath); folder != null; folder = folder.Parent)
            if (File.Exists(Path.Combine(folder.FullName, "src", "CardGame.Core", "CardGame.Core.csproj")))
                return Path.Combine(folder.FullName, "screens", Environment.GetEnvironmentVariable("CITY_SIGN_QA_OUTPUT") ?? "city-signs-qa");
        throw new InvalidOperationException("QA project must be inside the cardgame workspace.");
    }
}
