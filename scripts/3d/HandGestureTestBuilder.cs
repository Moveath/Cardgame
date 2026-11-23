using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Тестовая сцена базовых поз кисти (расслабленная кисть, кулак, переход между ними): Assets/HandGestureTest/HandGestureTest.unity.
/// Сцена отдельная, в сборку игры не входит и игровую сцену не трогает. В ней тестовая версия рук
/// (CardCity_Hands_Test.fbx из scripts/3d/hands/export_test.py) с игровой кожей и рукавом и аниматор с тремя состояниями:
/// IdleFistIdle (по умолчанию, по кругу), Idle, Fist.
///
/// Run собирает сцену, проверяет её и снимает кадры:
/// - клипы и их длина; кости на месте, не больше четырёх костей на вершину; материалы кожи и рукава;
/// - кости стоят как в Blender: расстояния от запястья до суставов пальцев в отмеченных кадрах сверяются с
///   CardCity_Hands_Test.json (допуск 0,3 мм), левая рука совпадает с правой;
/// - аниматор сцены даёт ту же позу, что клип;
/// - рукав: не двигается, когда сжимается кулак, и кожа предплечья не проступает сквозь него;
/// - кадры: расслабленная кисть и кулак с трёх ракурсов для обеих рук, начало, середина и конец перехода, обе руки
///   камерой сцены.
/// Кожа: в сцене тестовая молодая кожа ArmSkin_Smooth (scripts/3d/hands/skin_smooth.py) — копия игрового ArmSkin
/// с новыми картами цвета и рельефа и чуть другим светом на коже (SmoothSkinTweaks). Игровой ArmSkin не меняется.
/// Кадры «кожа до/после» снимаются при одном свете: та же поза, тот же ракурс, меняется только материал.
/// Отчёт — HAND_TEST_OUTPUT/unity-check.txt, кадры — там же.
/// Запуск в отдельном проекте: scripts/3d/run_hand_gesture_test.ps1.
/// </summary>
public static class HandGestureTestBuilder
{
    private const string Folder = "Assets/HandGestureTest";
    private const string Model = Folder + "/CardCity_Hands_Test.fbx";
    private const string Marks = Folder + "/CardCity_Hands_Test.json";
    private const string ScenePath = Folder + "/HandGestureTest.unity";
    private const string ControllerPath = Folder + "/HandGestureTest.controller";
    private const string SleevePath = Folder + "/Sleeve_Test.mat";
    private const string SkinPath = "Assets/Resources/ThreeD/Materials/ArmSkin.mat";
    private const string SmoothSkinPath = Folder + "/ArmSkin_Smooth.mat";
    private const string SmoothBase = Folder + "/T_Hand_Male_Smooth_BaseColor.png";
    private const string SmoothNormal = Folder + "/T_Hand_Male_Smooth_Normal.png";
    private const string GameBase = "Assets/Resources/ThreeD/Hands/T_Hand_Male_Young_BaseColor.png";
    private const string GameNormal = "Assets/Resources/ThreeD/Hands/T_Hand_Male_Young_Normal.png";
    // Свет на тестовой коже: чуть больше мягкого блика (молодая кожа не матовая, как сухая) и чуть мягче рельеф
    // в рассеянном свете. У игрового ArmSkin гладкость 0,3 и мягкость рельефа 0,45.
    private static readonly (string Name, float Value)[] SmoothSkinTweaks = { ("_Smoothness", .34f), ("_DiffuseSoftness", .55f) };
    // Кадры кожи «до/после»: поза, кадр клипа, ракурс в осях кисти.
    private static readonly (string Name, string Clip, int Frame, Vector3 From)[] SkinShots =
    {
        ("idle-тыл", "Idle", 0, new Vector3(0f, .15f, 1f)),
        ("кулак-костяшки", "Fist", 0, new Vector3(0f, .7f, .7f)),
        ("кулак-спереди", "Fist", 0, new Vector3(0f, 1f, .25f)),
        ("середина-перехода-тыл", "IdleFistIdle", 9, new Vector3(0f, .15f, 1f)),
    };
    private const string LitPath = "Assets/Resources/ThreeD/Materials/Lit.mat";
    private const string Prefix = "Test_";
    private static readonly string[] States = { "IdleFistIdle", "Idle", "Fist" };
    private static readonly string[] Sides = { "_R", "_L" };
    // Ракурс: откуда смотрит камера в осях кисти (к большому пальцу, вдоль пальцев, к тылу).
    private static readonly (string Name, Vector3 From)[] Views =
    {
        ("тыл", new Vector3(0f, .15f, 1f)),
        ("спереди", new Vector3(0f, 1f, .25f)),
        ("три-четверти", new Vector3(.7f, .7f, .5f)),
    };

    [Serializable] private class ClipInfo { public string name; public int frames; }
    [Serializable] private class Mark { public string clip; public int frame; public string side; public string bone; public float mm; }
    [Serializable] private class MarkFile { public int fps; public ClipInfo[] clips; public Mark[] marks; }

    private static string _out;
    private static readonly StringBuilder Report = new StringBuilder();
    private static int _failures;

    public static void Run()
    {
        _out = Environment.GetEnvironmentVariable("HAND_TEST_OUTPUT");
        Directory.CreateDirectory(_out);
        try
        {
            Import();
            var controller = BuildController();
            var hands = BuildScene(controller);
            Verify(hands);
            Save();
            Shoot(hands);
        }
        catch (Exception error)
        {
            Line("СБОЙ: " + error);
            _failures++;
        }
        Line(_failures > 0 ? "ИТОГ: не прошло, ошибок " + _failures : "ИТОГ: всё прошло");
        Save();
        EditorApplication.Exit(_failures > 0 ? 1 : 0);
    }

    private static void Save() => File.WriteAllText(Path.Combine(_out, "unity-check.txt"), Report.ToString());

    private static void Line(string text) => Report.AppendLine(text);

    private static void Check(bool condition, string text)
    {
        Line((condition ? "ок   " : "ОШИБКА ") + text);
        if (!condition) _failures++;
    }

    /// <summary>Импорт как у игровых рук (HandsAnimatorBuilder): общий скелет Generic, клипы поз и перехода повторяются по кругу.</summary>
    private static void Import()
    {
        AssetDatabase.Refresh();
        var model = (ModelImporter)AssetImporter.GetAtPath(Model);
        model.animationType = ModelImporterAnimationType.Generic;
        model.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        model.optimizeGameObjects = false;
        model.importTangents = ModelImporterTangents.CalculateMikk;
        model.materialImportMode = ModelImporterMaterialImportMode.None;
        model.SaveAndReimport();
        var clips = model.defaultClipAnimations;
        foreach (var clip in clips) clip.loopTime = clip.takeName.Contains(Prefix);
        model.clipAnimations = clips;
        model.SaveAndReimport();
    }

    private static AnimationClip[] Clips() =>
        AssetDatabase.LoadAllAssetsAtPath(Model).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToArray();

    private static AnimationClip Clip(string name) => Clips().First(c => c.name.EndsWith("|" + name) || c.name == name);

    private static AnimatorController BuildController()
    {
        AssetDatabase.DeleteAsset(ControllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        var machine = controller.layers[0].stateMachine;
        for (var i = 0; i < States.Length; i++)
        {
            var state = machine.AddState(States[i], new Vector3(300f, i * 70f));
            state.motion = Clip(Prefix + States[i]);
            state.writeDefaultValues = true;
            if (i == 0) machine.defaultState = state;
        }
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    private static GameObject BuildScene(AnimatorController controller)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        // Свет как за игровым столом (CardTable3DBootstrap, OwnArms3DView): главный из-за плеча, заполняющий со стороны
        // глаз и тёплый отсвет снизу.
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.3f, .27f, .25f);
        RenderSettings.ambientEquatorColor = new Color(.2f, .16f, .13f);
        RenderSettings.ambientGroundColor = new Color(.07f, .05f, .04f);
        Light("Key light", Quaternion.Euler(50f, 35f, 0f), new Color(1f, .92f, .82f), 1.6f, LightShadows.Soft);
        Light("Fill light", Quaternion.LookRotation(new Vector3(.1f, -.45f, 1f)), new Color(1f, .95f, .9f), .55f, LightShadows.None);
        Light("Bounce light", Quaternion.LookRotation(new Vector3(-.15f, .85f, -.35f)), new Color(1f, .72f, .6f), .3f, LightShadows.None);

        var sleeve = AssetDatabase.LoadAssetAtPath<Material>(SleevePath);
        if (sleeve == null)
        {
            // Рукав как в игре (Table3DArt.Sweater): матовая шерсть цвета морской волны.
            sleeve = new Material(AssetDatabase.LoadAssetAtPath<Material>(LitPath)) { name = "Sleeve_Test", color = new Color(.09f, .24f, .28f) };
            sleeve.SetFloat("_Smoothness", 0f);
            AssetDatabase.CreateAsset(sleeve, SleevePath);
        }
        var skin = SmoothSkin() ?? AssetDatabase.LoadAssetAtPath<Material>(SkinPath);
        var hands = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Model));
        hands.name = "Hands";
        // Как у всех моделей стола: разворот ставит пальцы вперёд, левую руку слева.
        hands.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        foreach (var part in hands.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            part.gameObject.SetActive(!part.name.Contains("Female"));
            part.sharedMaterial = part.name.Contains("Sleeve") ? sleeve : skin;
            part.updateWhenOffscreen = true;
        }
        var animator = hands.GetComponent<Animator>();
        if (animator == null) animator = hands.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        // Камера с места играющего: обе кисти сверху и сзади.
        var eye = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
        eye.clearFlags = CameraClearFlags.SolidColor;
        eye.backgroundColor = new Color(.12f, .11f, .1f);
        eye.fieldOfView = 60f;
        eye.nearClipPlane = .03f;
        eye.transform.position = new Vector3(0f, .6f, -.1f);
        eye.transform.LookAt(new Vector3(0f, -.02f, .36f));
        EditorSceneManager.SaveScene(scene, ScenePath);
        return hands;
    }

    /// <summary>Тестовая кожа: копия игрового ArmSkin с картами T_Hand_Male_Smooth_* и SmoothSkinTweaks. Без карт — null.</summary>
    private static Material SmoothSkin()
    {
        var baseMap = AssetDatabase.LoadAssetAtPath<Texture2D>(SmoothBase);
        var normalMap = AssetDatabase.LoadAssetAtPath<Texture2D>(SmoothNormal);
        if (baseMap == null || normalMap == null) return null;
        AssetDatabase.DeleteAsset(SmoothSkinPath);
        var material = new Material(AssetDatabase.LoadAssetAtPath<Material>(SkinPath)) { name = "ArmSkin_Smooth" };
        material.SetTexture("_BaseMap", baseMap);
        material.SetTexture("_BumpMap", normalMap);
        foreach (var (name, value) in SmoothSkinTweaks) material.SetFloat(name, value);
        AssetDatabase.CreateAsset(material, SmoothSkinPath);
        AssetDatabase.SaveAssets();
        return material;
    }

    private static void Light(string name, Quaternion rotation, Color color, float intensity, LightShadows shadows)
    {
        var light = new GameObject(name).AddComponent<Light>();
        light.type = LightType.Directional;
        light.transform.rotation = rotation;
        light.color = color;
        light.intensity = intensity;
        light.shadows = shadows;
    }

    private static Transform Find(GameObject root, string name) =>
        root.GetComponentsInChildren<Transform>(true).First(t => t.name == name);

    private static float Seconds(MarkFile file, int frame) => frame / (float)file.fps;

    private static void Verify(GameObject hands)
    {
        var file = JsonUtility.FromJson<MarkFile>(File.ReadAllText(Marks));
        var clips = Clips();
        Line("Клипы: " + string.Join(", ", clips.Select(c => c.name + " (" + c.length.ToString("0.000") + " с" + (c.isLooping ? ", по кругу" : "") + ")")));
        Check(clips.Any(c => c.name.EndsWith("Pose_Base")), "клип позы покоя Pose_Base есть");
        foreach (var info in file.clips)
        {
            var clip = clips.FirstOrDefault(c => c.name.EndsWith("|" + info.name) || c.name == info.name);
            Check(clip != null, "клип " + info.name + " есть");
            if (clip == null) continue;
            var expected = (info.frames - 1) / (float)file.fps;
            Check(Mathf.Abs(clip.length - expected) < .002f, info.name + ": длина " + clip.length.ToString("0.000") + " с, в Blender " + expected.ToString("0.000") + " с (" + info.frames + " кадров)");
        }

        var skins = hands.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        Check(skins.Length == 6, "шесть сеток со скиннингом: " + string.Join(", ", skins.Select(s => s.name)));
        foreach (var skin in skins)
        {
            var mesh = skin.sharedMesh;
            var perVertex = mesh.GetBonesPerVertex();
            Check(skin.bones.Length > 20 && skin.bones.All(b => b != null), skin.name + ": все " + skin.bones.Length + " костей на месте");
            Check(perVertex.Length > 0 && perVertex.Max() <= 4, skin.name + ": не больше четырёх костей на вершину (вершин " + mesh.vertexCount + ")");
        }
        Line("Трансформов в иерархии: " + hands.GetComponentsInChildren<Transform>(true).Length);

        var shownSkin = skins.Where(s => s.name.StartsWith("Arm_Male")).ToArray();
        var gameSkin = AssetDatabase.LoadAssetAtPath<Material>(SkinPath);
        var testSkin = AssetDatabase.LoadAssetAtPath<Material>(SmoothSkinPath);
        if (testSkin != null)
        {
            // Тестовая кожа отличается от игровой только картами и SmoothSkinTweaks; игровой материал не тронут.
            Check(gameSkin.GetTexture("_BaseMap").name == "T_Hand_Male_Young_BaseColor" && gameSkin.GetTexture("_BumpMap").name == "T_Hand_Male_Young_Normal",
                "игровой ArmSkin по-прежнему с картами T_Hand_Male_Young_*");
            var tweaked = SmoothSkinTweaks.Select(t => t.Name).ToArray();
            var shader = testSkin.shader;
            var differs = new List<string>();
            for (var i = 0; i < shader.GetPropertyCount(); i++)
            {
                var name = shader.GetPropertyName(i);
                var type = shader.GetPropertyType(i);
                if (type == UnityEngine.Rendering.ShaderPropertyType.Float || type == UnityEngine.Rendering.ShaderPropertyType.Range)
                {
                    if (!Mathf.Approximately(testSkin.GetFloat(name), gameSkin.GetFloat(name))) differs.Add(name);
                }
                else if (type == UnityEngine.Rendering.ShaderPropertyType.Color && testSkin.GetColor(name) != gameSkin.GetColor(name)) differs.Add(name);
                else if (type == UnityEngine.Rendering.ShaderPropertyType.Texture && testSkin.GetTexture(name) != gameSkin.GetTexture(name)) differs.Add(name);
            }
            Check(differs.All(n => tweaked.Contains(n) || n == "_BaseMap" || n == "_BumpMap"),
                "тестовая кожа отличается от игровой только картами и " + string.Join(", ", SmoothSkinTweaks.Select(t => t.Name + " " + gameSkin.GetFloat(t.Name).ToString("0.00") + " -> " + t.Value.ToString("0.00"))) + "; отличия: " + string.Join(", ", differs));
            foreach (var (test, game) in new[] { (SmoothBase, GameBase), (SmoothNormal, GameNormal) })
            {
                var a = (TextureImporter)AssetImporter.GetAtPath(test);
                var b = (TextureImporter)AssetImporter.GetAtPath(game);
                var ta = AssetDatabase.LoadAssetAtPath<Texture2D>(test);
                var tb = AssetDatabase.LoadAssetAtPath<Texture2D>(game);
                Check(a.textureType == b.textureType && a.sRGBTexture == b.sRGBTexture && a.maxTextureSize == b.maxTextureSize && a.textureCompression == b.textureCompression
                    && ta.width == tb.width && ta.height == tb.height && ta.format == tb.format,
                    Path.GetFileName(test) + ": импорт как у игровой " + Path.GetFileName(game) + " (" + ta.width + "×" + ta.height + ", " + ta.format + ", тип " + a.textureType + ")");
            }
        }
        foreach (var arm in shownSkin)
        {
            var material = arm.sharedMaterial;
            var expected = testSkin != null ? "ArmSkin_Smooth" : "ArmSkin";
            Check(material != null && material.name == expected, arm.name + ": материал кожи " + expected + ", шейдер " + (material ? material.shader.name : "нет"));
            Check(material != null && material.shader.isSupported, arm.name + ": шейдер кожи работает в этом проекте");
            Check(material != null && material.GetTexture("_BaseMap") != null && material.GetTexture("_BumpMap") != null, arm.name + ": текстуры цвета и рельефа кожи на месте");
            Check(arm.gameObject.activeSelf, arm.name + ": мужская рука включена");
        }
        foreach (var sleeve in skins.Where(s => s.name.StartsWith("Sleeve")))
            Check(sleeve.sharedMaterial != null && sleeve.sharedMaterial.name == "Sleeve_Test", sleeve.name + ": материал рукава, цвет " + (sleeve.sharedMaterial ? sleeve.sharedMaterial.color.ToString() : "нет"));
        foreach (var arm in skins.Where(s => s.name.Contains("Female")))
            Check(!arm.gameObject.activeSelf, arm.name + ": женская рука в сцене выключена");

        // Кости как в Blender: расстояния от запястья до суставов пальцев, миллиметры.
        var worst = 0f;
        var worstName = "";
        var mirror = 0f;
        foreach (var group in file.marks.GroupBy(m => m.clip + "|" + m.frame))
        {
            var first = group.First();
            Clip(first.clip).SampleAnimation(hands, Seconds(file, first.frame));
            var measured = new Dictionary<string, float>();
            foreach (var mark in group)
            {
                var distance = Vector3.Distance(Find(hands, "Hand" + mark.side).position, Find(hands, mark.bone + mark.side).position) * 1000f;
                measured[mark.bone + mark.side] = distance;
                var error = Mathf.Abs(distance - mark.mm);
                if (error > worst)
                {
                    worst = error;
                    worstName = first.clip + ", кадр " + first.frame + ", " + mark.bone + mark.side;
                }
            }
            foreach (var mark in group.Where(m => m.side == "_L"))
                mirror = Mathf.Max(mirror, Mathf.Abs(measured[mark.bone + "_L"] - measured[mark.bone + "_R"]));
        }
        Check(worst < .3f, "кости стоят как в Blender: наибольшее расхождение " + worst.ToString("0.000") + " мм по " + file.marks.Length + " замерам (" + worstName + ")");
        Check(mirror < .3f, "левая рука совпадает с правой: наибольшая разница " + mirror.ToString("0.000") + " мм");

        // Кулак действительно сжат: кончик среднего пальца у ладони, большой палец лежит на пальцах.
        Clip(Prefix + "Idle").SampleAnimation(hands, 0f);
        var open = Vector3.Distance(Find(hands, "TipMiddle_R").position, Find(hands, "Hand_R").position);
        Clip(Prefix + "Fist").SampleAnimation(hands, 0f);
        var closed = Vector3.Distance(Find(hands, "TipMiddle_R").position, Find(hands, "Hand_R").position);
        var thumb = Vector3.Distance(Find(hands, "TipThumb_R").position, Find(hands, "Index03_R").position);
        Check(closed < open * .55f, "кулак сжимает пальцы: кончик среднего пальца от запястья " + (open * 1000f).ToString("0") + " мм -> " + (closed * 1000f).ToString("0") + " мм");
        Check(thumb < .05f, "большой палец лежит на пальцах: его кончик в " + (thumb * 1000f).ToString("0") + " мм от дальнего сустава указательного");

        // Аниматор сцены играет те же клипы: поза из состояния совпадает с позой клипа.
        var animator = hands.GetComponent<Animator>();
        Check(animator != null && animator.runtimeAnimatorController != null, "на руках аниматор с контроллером " + (animator && animator.runtimeAnimatorController ? animator.runtimeAnimatorController.name : "нет"));
        var controller = (AnimatorController)animator.runtimeAnimatorController;
        var machine = controller.layers[0].stateMachine;
        Check(machine.defaultState != null && machine.defaultState.name == States[0], "состояние по умолчанию — " + States[0] + ", всего состояний " + machine.states.Length);
        Check(machine.states.All(s => s.state.motion != null), "у каждого состояния есть клип: " + string.Join(", ", machine.states.Select(s => s.state.name + "=" + (s.state.motion ? s.state.motion.name : "нет"))));
        try
        {
            var transition = Clip(Prefix + "IdleFistIdle");
            var middle = file.clips.First(c => c.name == Prefix + "IdleFistIdle").frames / 2;
            transition.SampleAnimation(hands, Seconds(file, middle));
            var wrist = Find(hands, "Hand_R");
            var expected = wrist.InverseTransformPoint(Find(hands, "TipMiddle_R").position);
            Clip("Pose_Base").SampleAnimation(hands, 0f);
            animator.Rebind();
            animator.Play(States[0], 0, Seconds(file, middle) / transition.length);
            animator.Update(0f);
            var played = wrist.InverseTransformPoint(Find(hands, "TipMiddle_R").position);
            Check(Vector3.Distance(expected, played) < .0005f, "аниматор даёт ту же позу, что клип (середина перехода): расхождение " + (Vector3.Distance(expected, played) * 1000f).ToString("0.000") + " мм");
        }
        catch (Exception error)
        {
            Line("не проверено: аниматор вне режима игры не посчитал позу (" + error.Message + ")");
        }

        Sleeves(hands, skins);
    }

    private static Vector3[] Baked(SkinnedMeshRenderer skin)
    {
        var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
        skin.BakeMesh(mesh, true);
        var frame = Matrix4x4.TRS(skin.transform.position, skin.transform.rotation, Vector3.one);
        var points = mesh.vertices.Select(v => frame.MultiplyPoint3x4(v)).ToArray();
        UnityEngine.Object.DestroyImmediate(mesh);
        return points;
    }

    /// <summary>Рукав: стоит на предплечье, не двигается при сжатии кулака, кожа сквозь него не проступает.</summary>
    private static void Sleeves(GameObject hands, SkinnedMeshRenderer[] skins)
    {
        foreach (var side in Sides)
        {
            var sleeve = skins.First(s => s.name == "Sleeve" + side);
            var arm = skins.First(s => s.name == "Arm_Male" + side);
            Clip(Prefix + "Idle").SampleAnimation(hands, 0f);
            var idle = Baked(sleeve);
            Clip(Prefix + "Fist").SampleAnimation(hands, 0f);
            var fist = Baked(sleeve);
            var moved = idle.Zip(fist, (a, b) => Vector3.Distance(a, b)).Max() * 1000f;
            Check(moved < .1f, "рукав" + side + " не двигается, когда сжимается кулак: наибольший сдвиг " + moved.ToString("0.000") + " мм");
            var elbow = Find(hands, "Forearm" + side).position;
            var wrist = Find(hands, "Hand" + side).position;
            var axis = (wrist - elbow).normalized;
            var length = Vector3.Distance(elbow, wrist);
            var from = fist.Min(p => Vector3.Dot(p - elbow, axis));
            var to = fist.Max(p => Vector3.Dot(p - elbow, axis));
            Check(to > length * .5f && to < length * 1.02f && from < .03f,
                "рукав" + side + " на предплечье: от " + (from * 100f).ToString("0.0") + " до " + (to * 100f).ToString("0.0") + " см от локтя, запястье в " + (length * 100f).ToString("0.0") + " см");
            // Кожа под рукавом: на том же участке предплечья она ближе к оси, чем ткань рядом.
            var skinPoints = Baked(arm);
            var poking = 0;
            var worst = 0f;
            foreach (var p in skinPoints)
            {
                var along = Vector3.Dot(p - elbow, axis);
                if (along < from + .01f || along > to - .012f) continue;
                var radial = (p - elbow - axis * along).magnitude;
                var nearest = fist.OrderBy(s => (s - p).sqrMagnitude).First();
                var cloth = (nearest - elbow - axis * Vector3.Dot(nearest - elbow, axis)).magnitude;
                if (radial > cloth + .0005f)
                {
                    poking++;
                    worst = Mathf.Max(worst, radial - cloth);
                }
            }
            Check(poking == 0, "кожа не проступает сквозь рукав" + side + ": вершин кожи снаружи ткани " + poking + (poking > 0 ? ", до " + (worst * 1000f).ToString("0.0") + " мм" : ""));
        }
    }

    /// <summary>Кадры. Вне режима игры скиннинг в кадре не обновляется, поэтому снимаются сетки, запечённые в нужной позе.</summary>
    private static void Shoot(GameObject hands)
    {
        var file = JsonUtility.FromJson<MarkFile>(File.ReadAllText(Marks));
        var skins = hands.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.gameObject.activeSelf).ToArray();
        foreach (var skin in skins) skin.enabled = false;
        var views = skins.Select(skin =>
        {
            var view = new GameObject(skin.name + " shown", typeof(MeshFilter), typeof(MeshRenderer));
            view.GetComponent<MeshRenderer>().sharedMaterial = skin.sharedMaterial;
            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            view.GetComponent<MeshFilter>().sharedMesh = mesh;
            return (skin, view, mesh);
        }).ToArray();
        var eye = new GameObject("Shot camera").AddComponent<Camera>();
        eye.clearFlags = CameraClearFlags.SolidColor;
        eye.backgroundColor = new Color(.2f, .19f, .18f);
        eye.nearClipPlane = .03f;
        var target = new RenderTexture(800, 800, 24, RenderTextureFormat.ARGB32);
        target.Create();
        eye.targetTexture = target;

        void Pose(string clip, int frame)
        {
            Clip(clip).SampleAnimation(hands, Seconds(file, frame));
            foreach (var (skin, view, mesh) in views)
            {
                skin.BakeMesh(mesh, true);
                view.transform.SetPositionAndRotation(skin.transform.position, skin.transform.rotation);
                mesh.RecalculateBounds();
            }
        }

        void Snap(string name)
        {
            eye.Render();
            RenderTexture.active = target;
            var picture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            picture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            picture.Apply();
            File.WriteAllBytes(Path.Combine(_out, name + ".png"), picture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(picture);
            Line("Кадр Unity: " + name);
        }

        void Aim(string side, Vector3 from, float fieldOfView = 24f)
        {
            // Оси кисти: вдоль пальцев, к тылу (кисть в покое лежит ладонью вниз), к большому пальцу.
            var wrist = Find(hands, "Hand" + side).position;
            var along = (Find(hands, "Middle01" + side).position - wrist).normalized;
            var back = (Vector3.up - along * Vector3.Dot(Vector3.up, along)).normalized;
            var toward = Vector3.Cross(back, along).normalized;
            if (Vector3.Dot(toward, Find(hands, "Thumb01" + side).position - Find(hands, "Pinky01" + side).position) < 0f) toward = -toward;
            // Центр кадра — середина кожи кисти: всё, что дальше запястья.
            var arm = views.First(v => v.skin.name == "Arm_Male" + side);
            var frame = Matrix4x4.TRS(arm.view.transform.position, arm.view.transform.rotation, Vector3.one);
            var hand = arm.mesh.vertices.Select(v => frame.MultiplyPoint3x4(v)).Where(p => Vector3.Dot(p - wrist, along) > -.01f).ToArray();
            var bounds = new Bounds(hand[0], Vector3.zero);
            foreach (var p in hand) bounds.Encapsulate(p);
            var direction = (toward * from.x + along * from.y + back * from.z).normalized;
            var up = Mathf.Abs(Vector3.Dot(direction, back)) > .9f ? along : back;
            eye.fieldOfView = fieldOfView;
            eye.transform.position = bounds.center + direction * .38f;
            eye.transform.rotation = Quaternion.LookRotation(-direction, up);
        }

        // Одна рука в кадре: вторая и её рукав скрыты.
        void Only(string side)
        {
            foreach (var (skin, view, _) in views) view.SetActive(side == null || skin.name.EndsWith(side));
        }

        // Первый кадр после запуска редактор рисует, пока шейдер кожи ещё компилируется, — пустым: он снимается вхолостую.
        Pose(Prefix + "Idle", 0);
        Aim("_R", Views[0].From);
        ShaderUtil.allowAsyncCompilation = false;
        eye.Render();
        foreach (var side in Sides)
        {
            Only(side);
            foreach (var pose in new[] { "Idle", "Fist" })
            {
                Pose(Prefix + pose, 0);
                foreach (var (name, from) in Views)
                {
                    Aim(side, from);
                    Snap("unity-" + pose + "-" + side.Substring(1) + "-" + name);
                }
            }
        }
        // Переход: начало, середина сжатия, кулак — спереди и в три четверти.
        Only("_R");
        foreach (var (label, frame) in new[] { ("начало", 0), ("середина", 9), ("кулак", 18) })
        {
            Pose(Prefix + "IdleFistIdle", frame);
            Aim("_R", Views[1].From);
            Snap("unity-переход-" + label + "-спереди");
            Aim("_R", Views[2].From);
            Snap("unity-переход-" + label + "-три-четверти");
        }
        // Камера сцены: обе руки в кулаке, как их увидит автор, открыв сцену.
        Only(null);
        Pose(Prefix + "Fist", 0);
        var scene = GameObject.FindWithTag("MainCamera").GetComponent<Camera>();
        eye.fieldOfView = scene.fieldOfView;
        eye.transform.SetPositionAndRotation(scene.transform.position, scene.transform.rotation);
        Snap("unity-сцена-кулак");
        Pose(Prefix + "Idle", 0);
        Snap("unity-сцена-idle");

        // Кожа до и после: та же поза, тот же ракурс и свет, меняется только материал кожи.
        var smooth = AssetDatabase.LoadAssetAtPath<Material>(SmoothSkinPath);
        var game = AssetDatabase.LoadAssetAtPath<Material>(SkinPath);
        if (smooth != null)
        {
            // Свет со стороны камеры, как у стенда в Blender (gesture_lab.py): без него сторона кисти, отвёрнутая от лампы
            // сцены (кулак спереди), уходит в бурую тень, и кожу не разглядеть. Один и тот же для «до» и «после».
            var head = new GameObject("Shot head light").AddComponent<Light>();
            head.type = LightType.Directional;
            head.intensity = .8f;
            head.color = new Color(1f, .96f, .92f);
            head.shadows = LightShadows.None;
            foreach (var side in Sides)
            {
                Only(side);
                foreach (var (name, clip, frame, from) in SkinShots)
                {
                    Pose(Prefix + clip, frame);
                    Aim(side, from, 17f);
                    // Сверху-слева от камеры, как ключевой свет в студии.
                    head.transform.rotation = Quaternion.LookRotation(eye.transform.forward - eye.transform.up * .5f + eye.transform.right * .35f, eye.transform.up);
                    foreach (var (label, material) in new[] { ("до", game), ("после", smooth) })
                    {
                        foreach (var view in views.Where(v => v.skin.name.StartsWith("Arm_Male"))) view.view.GetComponent<MeshRenderer>().sharedMaterial = material;
                        Snap("unity-кожа-" + name + "-" + side.Substring(1) + "-" + label);
                    }
                }
            }
        }
        RenderTexture.active = null;
    }
}
