using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Диагностика кадров на телефоне (2026-10-10, только в пробной сборке: `ANDROID_PROBE=1 sh scripts/android/build-test-apk.sh`).
/// Раз в 2 секунды пишет в logcat (метка PROBE) кадры в секунду и время кадра: главный поток, поток отрисовки, видеокарта.
/// Подозреваемых выключает файл `probe.txt` в папке данных игры (`adb shell` → `/sdcard/Android/data/<пакет>/files`),
/// строка вида `pp=0 post=0 hdr=0 scale=0.67 off=City camera|Tavern mail`: выравнивание холстов по пикселям,
/// пост-обработка камеры, HDR, доля разрешения экрана, слои по имени, которые спрятать (сколько стоит их отрисовка);
/// `fx=0` — без обводок и теней (Outline/Shadow), `fx=img` — без них только у картинок, `rs=` — масштаб отрисовки URP.
/// Пустая строка ничего не трогает. Читается на лету — пересобирать не нужно.
/// </summary>
public sealed class FrameProbe : MonoBehaviour
{
    private readonly FrameTiming[] _timing = new FrameTiming[1];
    private float _elapsed, _frames;
    private double _main, _render, _gpu;
    private int _samples;
    private string _mode = "";
    private int _nativeWidth, _nativeHeight;
    private readonly System.Collections.Generic.List<GameObject> _hidden = new System.Collections.Generic.List<GameObject>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        var probe = new GameObject("Frame probe");
        DontDestroyOnLoad(probe);
        probe.AddComponent<FrameProbe>();
    }

    private void Start()
    {
        // Замеры идут через кабель: экран не гаснет, пока игра открыта (только в диагностической сборке).
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
        _nativeWidth = Screen.width;
        _nativeHeight = Screen.height;
        Debug.Log("PROBE start " + _nativeWidth + "x" + _nativeHeight + " · " + SystemInfo.graphicsDeviceType + " · " + SystemInfo.graphicsDeviceName
            + " · " + SystemInfo.processorType + " · RAM " + SystemInfo.systemMemorySize + " MB · data " + Application.persistentDataPath);
    }

    private void Update()
    {
        FrameTimingManager.CaptureFrameTimings();
        if (FrameTimingManager.GetLatestTimings(1, _timing) > 0)
        {
            _main += _timing[0].cpuMainThreadFrameTime;
            _render += _timing[0].cpuRenderThreadFrameTime;
            _gpu += _timing[0].gpuFrameTime;
            _samples++;
        }
        _frames++;
        _elapsed += Time.unscaledDeltaTime;
        if (_elapsed < 2) return;
        var n = Mathf.Max(1, _samples);
        Debug.Log("PROBE [" + _mode + "] fps " + (_frames / _elapsed).ToString("0.0") + " · main " + (_main / n).ToString("0.0")
            + " ms · render " + (_render / n).ToString("0.0") + " ms · gpu " + (_gpu / n).ToString("0.0") + " ms · "
            + Screen.width + "x" + Screen.height);
        _elapsed = _frames = 0;
        _main = _render = _gpu = 0;
        _samples = 0;
        ApplyMode();
    }

    private void ApplyMode()
    {
        var path = Path.Combine(Application.persistentDataPath, "probe.txt");
        var mode = File.Exists(path) ? File.ReadAllText(path).Trim() : "";
        if (mode == _mode) return;
        _mode = mode;
        foreach (var hidden in _hidden) if (hidden != null) hidden.SetActive(true);
        _hidden.Clear();
        if (mode.Length == 0) { Debug.Log("PROBE mode []"); return; }
        var off = mode.IndexOf("off=", System.StringComparison.Ordinal);
        if (off >= 0)
        {
            var names = mode.Substring(off + 4).Split('|');
            mode = mode.Substring(0, off).Trim();
            foreach (var item in FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (System.Array.IndexOf(names, item.name) >= 0) { item.gameObject.SetActive(false); _hidden.Add(item.gameObject); }
        }
        // Меняется только то, что названо в строке: остальное — как задала игра (MobilePerformance.cs).
        string Value(string key)
        {
            foreach (var part in mode.Split(' '))
                if (part.StartsWith(key + "=")) return part.Substring(key.Length + 1);
            return null;
        }
        if (Value("pp") is string pp)
            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (canvas.isRootCanvas) canvas.pixelPerfect = pp != "0";
        if (Value("post") is string post)
            foreach (var camera in Camera.allCameras)
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = post != "0";
        if (Value("fx") is string fx)
            foreach (var effect in FindObjectsByType<UnityEngine.UI.BaseMeshEffect>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (!(effect.GetComponent<UnityEngine.UI.Text>() != null && fx == "img")) effect.enabled = fx != "0" && fx != "img";
        if (Value("hdr") is string hdr && GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp) urp.supportsHDR = hdr != "0";
        if (Value("rs") is string rs && GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset asset
            && float.TryParse(rs, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var renderScale))
            asset.renderScale = renderScale;
        if (Value("scale") is string text && float.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var scale))
        {
            // Доля родного разрешения экрана.
            scale = Mathf.Clamp(scale, .3f, 1);
            Screen.SetResolution(Mathf.RoundToInt(Display.main.systemWidth * scale), Mathf.RoundToInt(Display.main.systemHeight * scale), true);
        }
        Debug.Log("PROBE mode [" + _mode + "] · hidden " + _hidden.Count);
    }
}
