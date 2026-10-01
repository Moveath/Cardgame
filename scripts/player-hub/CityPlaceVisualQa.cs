// Focused native preview. Copy to screens/city-signs-project/Assets/Scripts/Editor.
// Run one place per Unity process: -executeMethod CityPlaceVisualQa.Run.
// CITY_PLACE_QA_ID selects a production CityPlaces polygon; CITY_PLACE_QA_OUTPUT
// selects the screens subfolder. CITY_PLACE_QA_BASELINE=1 reproduces the old style.
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CardGameClient;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class CityPlaceVisualQa
{
    private const int Width = 1672, Height = 941;
    private static Camera camera;
    private static string output;

    public static void Run() => EditorApplication.delayCall += RunAfterStartup;

    private static async void RunAfterStartup()
    {
        var root = FindRoot();
        output = Path.Combine(root, "screens", Environment.GetEnvironmentVariable("CITY_PLACE_QA_OUTPUT") ?? "city-places-preview");
        Directory.CreateDirectory(output);
        var id = Environment.GetEnvironmentVariable("CITY_PLACE_QA_ID") ?? "club";
        try
        {
            // Parse exact production points rather than maintaining copied geometry fixtures.
            var source = File.ReadAllText(Path.Combine(root, "unity", "CardGameClient", "Assets", "Scripts", "PlayerHubPlaces.cs"));
            var expression = @"(?:Place|Stairs)\(""" + Regex.Escape(id)
                + @""",\s*""(?<art>[^""]+)"".*?new float\[\]\s*\{(?<points>[^}]+)\}";
            var match = Regex.Match(source, expression, RegexOptions.Singleline);
            Require(match.Success, "production place exists: " + id);
            var art = match.Groups["art"].Value;
            var coordinates = Regex.Matches(match.Groups["points"].Value, @"-?\d+(?:\.\d+)?")
                .Cast<Match>().Select(value => float.Parse(value.Value, CultureInfo.InvariantCulture)).ToArray();
            Require(coordinates.Length >= 6 && coordinates.Length % 2 == 0, "valid coordinate pairs");
            var points = Enumerable.Range(0, coordinates.Length / 2)
                .Select(index => new Vector2(coordinates[index * 2] / Width, coordinates[index * 2 + 1] / Height)).ToArray();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var canvas = new GameObject("Place visual QA", typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(Width, Height);
            camera = new GameObject("Place preview camera", typeof(Camera)).GetComponent<Camera>();
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.magenta;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 200;
            canvas.worldCamera = camera;
            var host = new GameObject("Inactive sign host", typeof(RectTransform));
            host.SetActive(false);
            host.transform.SetParent(canvas.transform, false);
            var hub = host.AddComponent<PlayerHubController>();
            hub.enabled = false;
            var sprite = Resources.Load<Sprite>("UI/Hub/" + art);
            Require(sprite != null, "art loaded: " + art);
            var page = new GameObject(art, typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            page.SetParent(canvas.transform, false);
            page.anchorMin = page.anchorMax = page.pivot = new Vector2(.5f, .5f);
            page.sizeDelta = new Vector2(Width, Height);
            page.GetComponent<Image>().sprite = sprite;
            page.GetComponent<Image>().raycastTarget = false;
            typeof(PlayerHubController).GetMethod("AddSigns", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(hub, new object[] { page, art, (float)Width });
            var outline = new GameObject("Production outline", typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<HubPlaceOutline>();
            outline.rectTransform.SetParent(canvas.transform, false);
            outline.rectTransform.anchorMin = outline.rectTransform.anchorMax = outline.rectTransform.pivot = new Vector2(.5f, .5f);
            outline.rectTransform.sizeDelta = new Vector2(Width, Height);
            outline.raycastTarget = false;
            outline.Points = points;
            outline.Fill = HubPlaceOutline.Triangulate(points);
            Require(outline.Fill != null, "simple production polygon");
            if (Environment.GetEnvironmentVariable("CITY_PLACE_QA_BASELINE") == "1")
            {
                outline.Line = 3.5f;
                outline.Glow = 16f;
            }
            outline.SetAllDirty();
            await NextFrame();
            await NextFrame();
            var full = new Rect(-Width / 2f, -Height / 2f, Width, Height);
            Save(id + "-1672.png", full, Width, Height);
            var left = points.Min(point => point.x) * Width;
            var right = points.Max(point => point.x) * Width;
            var top = points.Min(point => point.y) * Height;
            var bottom = points.Max(point => point.y) * Height;
            var crop = Rect.MinMaxRect(Mathf.Max(-Width / 2f, left - Width / 2f - 32),
                Mathf.Max(-Height / 2f, Height / 2f - bottom - 32),
                Mathf.Min(Width / 2f, right - Width / 2f + 32),
                Mathf.Min(Height / 2f, Height / 2f - top + 32));
            Save(id + "-detail.png", crop, Mathf.RoundToInt(crop.width), Mathf.RoundToInt(crop.height));
            if (id == "club")
            {
                Save(id + "-1280.png", full, 1280, 720);
                var selected = typeof(HubPlaceOutline).GetField("Selected");
                if (selected != null)
                {
                    selected.SetValue(outline, true);
                    outline.SetVerticesDirty();
                    await NextFrame();
                    Save(id + "-selected.png", full, Width, Height);
                }
            }
            var mesh = outline.canvasRenderer.GetMesh();
            Require(mesh != null && mesh.vertexCount > 0, "outline rendered");
            Require(mesh.vertices.All(vertex => !float.IsNaN(vertex.x) && !float.IsNaN(vertex.y)
                && !float.IsInfinity(vertex.x) && !float.IsInfinity(vertex.y)), "finite outline geometry");
            File.WriteAllText(Path.Combine(output, id + "-verification.txt"),
                "Native Unity preview from production artwork, AddSigns and HubPlaceOutline. Exact " + points.Length
                + "-point polygon parsed from PlayerHubPlaces. No profile store opened.\nArt: " + art
                + "\nOutline vertices: " + mesh.vertexCount + "; line: " + outline.Line + "; glow: " + outline.Glow);
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(output, id + "-error.txt"), error.ToString());
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }

    private static void Save(string name, Rect world, int width, int height)
    {
        camera.transform.position = new Vector3(world.center.x, world.center.y, -100);
        camera.orthographicSize = world.height / 2;
        if (camera.targetTexture != null)
        {
            var previousTarget = camera.targetTexture;
            camera.targetTexture = null;
            previousTarget.Release();
            UnityEngine.Object.DestroyImmediate(previousTarget);
        }
        camera.targetTexture = new RenderTexture(width, height, 24);
        Require(camera.targetTexture.Create(), "render texture allocated");
        Canvas.ForceUpdateCanvases();
        camera.Render();
        var previous = RenderTexture.active;
        RenderTexture.active = camera.targetTexture;
        var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
        pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        pixels.Apply();
        File.WriteAllBytes(Path.Combine(output, name), pixels.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(pixels);
        RenderTexture.active = previous;
    }

    private static Task NextFrame()
    {
        var next = new TaskCompletionSource<bool>();
        EditorApplication.delayCall += () => next.SetResult(true);
        return next.Task;
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static string FindRoot()
    {
        for (var folder = new DirectoryInfo(Application.dataPath); folder != null; folder = folder.Parent)
            if (File.Exists(Path.Combine(folder.FullName, "src", "CardGame.Core", "CardGame.Core.csproj"))) return folder.FullName;
        throw new InvalidOperationException("Preview project must be inside cardgame workspace.");
    }
}
