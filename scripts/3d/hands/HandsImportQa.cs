using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Проверка рук в настоящем Unity: импорт FBX, кости, клипы, ориентация, деформация и кадр рендера.
/// Работает в отдельном проекте, папка Assets/HandsQa. Отчёт: screens/3d-hands/unity-import.txt.
/// </summary>
public static class HandsImportQa
{
    private const string Folder = "Assets/HandsQa";
    private const string Model = Folder + "/CardCity_Hands.fbx";
    private static readonly string[] Poses = { "Base", "Relaxed", "HoldCard", "TakeCard", "PlayCard", "Point", "TapTable", "Fist", "InGame" };
    private static string _out;
    private static readonly StringBuilder Report = new StringBuilder();
    private static int _failures;

    public static void Run()
    {
        _out = Environment.GetEnvironmentVariable("HANDS_QA_OUTPUT");
        var code = 0;
        try
        {
            Prepare();
            Verify();
        }
        catch (Exception error)
        {
            Line("СБОЙ: " + error);
            _failures++;
        }
        code = _failures > 0 ? 1 : 0;
        Line(_failures > 0 ? "ИТОГ: не прошло, ошибок " + _failures : "ИТОГ: всё прошло");
        Save();
        EditorApplication.Exit(code);
    }

    private static void Save() => File.WriteAllText(Path.Combine(_out, "unity-import.txt"), Report.ToString());

    private static void Line(string text) => Report.AppendLine(text);

    private static void Check(bool condition, string text)
    {
        Line((condition ? "ок   " : "ОШИБКА ") + text);
        if (!condition) _failures++;
    }

    private static void Prepare()
    {
        AssetDatabase.Refresh();
        foreach (var path in Directory.GetFiles(Folder, "*.png"))
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path.Replace('\\', '/'));
            if (path.Contains("_Normal")) importer.textureType = TextureImporterType.NormalMap;
            if (path.Contains("_Mask")) importer.sRGBTexture = false;
            importer.SaveAndReimport();
        }
        var model = (ModelImporter)AssetImporter.GetAtPath(Model);
        model.animationType = ModelImporterAnimationType.Generic;
        model.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        model.optimizeGameObjects = false;
        model.importTangents = ModelImporterTangents.CalculateMikk;
        model.materialImportMode = ModelImporterMaterialImportMode.None;
        model.SaveAndReimport();
        Line("Импортёр: " + model.GetType().Name + ", единицы файла " + model.fileScale + ", масштаб " + model.globalScale);
    }

    private static Transform Find(GameObject root, string name) =>
        root.GetComponentsInChildren<Transform>(true).First(t => t.name == name);

    private static void Verify()
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
        Check(asset != null, "модель загружается");
        var clips = AssetDatabase.LoadAllAssetsAtPath(Model).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToArray();
        Line("Клипы: " + string.Join(", ", clips.Select(c => c.name + " (" + c.length.ToString("0.000") + " с, кривых " + AnimationUtility.GetCurveBindings(c).Length + ")")));
        foreach (var pose in Poses) Check(clips.Any(c => c.name.Contains("Pose_" + pose)), "клип позы " + pose);

        // Как все модели стола: разворот на 180° вокруг вертикали ставит руки перед игроком.
        var hands = (GameObject)UnityEngine.Object.Instantiate(asset);
        hands.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        Line("Первая кривая клипа: " + AnimationUtility.GetCurveBindings(clips[0])[0].path);
        var skins = hands.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        Check(skins.Length == 6, "шесть сеток со скиннингом: " + string.Join(", ", skins.Select(s => s.name)));
        foreach (var skin in skins)
        {
            var mesh = skin.sharedMesh;
            var influences = mesh.GetAllBoneWeights().Max(w => (int)w.boneIndex >= 0 ? 1 : 0);
            Line(skin.name + ": вершин " + mesh.vertexCount + ", треугольников " + mesh.triangles.Length / 3 + ", костей " + skin.bones.Length + ", нормали " + (mesh.normals.Length > 0) + ", тангенты " + (mesh.tangents.Length > 0) + ", UV " + (mesh.uv.Length > 0));
            Check(skin.bones.Length > 20 && skin.bones.All(b => b != null), skin.name + ": все кости на месте");
            var perVertex = mesh.GetBonesPerVertex();
            Check(perVertex.Length > 0 && perVertex.Max() <= 4, skin.name + ": не больше четырёх костей на вершину");
        }

        Check(hands.GetComponent<Animator>() != null, "на модели есть Animator: без него поза партии не запустится");
        var bones = hands.GetComponentsInChildren<Transform>(true);
        Line("Трансформов в иерархии: " + bones.Length);
        foreach (var name in new[] { "Root", "Forearm_L", "ForearmTwist_L", "Hand_L", "Thumb03_L", "Index03_R", "SocketWatch_L", "SocketRingIndex_L", "SocketCard_R" })
            Check(bones.Any(t => t.name == name), "кость " + name);

        var forearm = Find(hands, "Forearm_L").position;
        var wrist = Find(hands, "Hand_L").position;
        var thumb = Find(hands, "Thumb03_L").position;
        var pinky = Find(hands, "Pinky03_L").position;
        var rightWrist = Find(hands, "Hand_R").position;
        Line("Покой: локоть " + forearm + ", запястье " + wrist + ", правое запястье " + rightWrist);
        Check(wrist.z > forearm.z + .2f, "пальцы смотрят вперёд, в +Z");
        Check(wrist.x < 0f && rightWrist.x > 0f, "левая рука слева, правая справа");
        Check(thumb.x > pinky.x, "большой палец левой руки к середине");
        Check(Mathf.Abs(Vector3.Distance(forearm, wrist) - .283f) < .01f, "масштаб: предплечье 28 см, вышло " + Vector3.Distance(forearm, wrist));

        var tip = Find(hands, "Middle03_L");
        Line("Покой: дальний сустав среднего пальца " + tip.position);
        var palm = Find(hands, "SocketCard_L");
        var restGap = Vector3.Distance(tip.position, palm.position);
        foreach (var clip in clips)
        {
            clip.SampleAnimation(hands, 0f);
            Line("Кончик до ладони в " + clip.name + ": " + Vector3.Distance(tip.position, palm.position).ToString("0.000"));
        }
        var fist = clips.First(c => c.name.Contains("Pose_Fist"));
        fist.SampleAnimation(hands, 0f);
        var fistGap = Vector3.Distance(tip.position, palm.position);
        Check(Mathf.Abs(Vector3.Distance(Find(hands, "Forearm_L").position, Find(hands, "Hand_L").position) - .283f) < .01f, "клип не меняет длину костей");
        Check(fistGap < restGap * .6f, "кулак сгибает пальцы: кончик до ладони " + restGap.ToString("0.000") + " -> " + fistGap.ToString("0.000"));
        var male = skins.First(s => s.name == "Arm_Male_L");
        var elbow = Find(hands, "Forearm_L").position;
        var fistReach = Reach(male, elbow);
        clips.First(c => c.name.Contains("Pose_Base")).SampleAnimation(hands, 0f);
        var baseReach = Reach(male, elbow);
        Line("Самая дальняя вершина от локтя: покой " + baseReach.ToString("0.000") + ", кулак " + fistReach.ToString("0.000"));
        Check(Mathf.Abs(baseReach - .47f) < .01f, "сетка в покое совпадает со скелетом: кончик пальца в 47 см от локтя, как в Blender");
        Check(fistReach < baseReach - .03f, "вершины идут за костями: в кулаке рука короче");
        // Отчёт пишется до съёмки: рендер в пакетном режиме может уронить редактор при нехватке памяти.
        Save();
        Shoot(hands, clips);
    }

    /// <summary>Как далеко от локтя уходит сетка в текущей позе: считается по вершинам, а не по костям.</summary>
    private static float Reach(SkinnedMeshRenderer skin, Vector3 elbow)
    {
        var baked = new Mesh { indexFormat = IndexFormat.UInt32 };
        skin.BakeMesh(baked, true);
        var frame = Matrix4x4.TRS(skin.transform.position, skin.transform.rotation, Vector3.one);
        return baked.vertices.Max(v => Vector3.Distance(frame.MultiplyPoint3x4(v), elbow));
    }

    private static void Shoot(GameObject hands, AnimationClip[] clips)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            Line("Шейдер URP не найден, кадр пропущен");
            return;
        }
        var skin = new Material(shader);
        skin.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/T_Hand_Male_BaseColor.png"));
        skin.SetColor("_BaseColor", new Color(.8f, .55f, .42f));
        skin.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/T_Hand_Male_Normal.png"));
        skin.EnableKeyword("_NORMALMAP");
        skin.SetFloat("_Smoothness", .42f);
        // Вне режима игры скиннинг в кадре не обновляется: снимается сетка, запечённая в нужной позе.
        var arms = hands.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var shown = arms.Where(a => a.name.Contains("Male") && !a.name.Contains("Female")).ToArray();
        foreach (var arm in arms) arm.enabled = false;
        var poseMeshes = shown.Select(a =>
        {
            var view = new GameObject(a.name + " shown", typeof(MeshFilter), typeof(MeshRenderer));
            view.GetComponent<MeshRenderer>().sharedMaterial = skin;
            var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            view.GetComponent<MeshFilter>().sharedMesh = mesh;
            return new { arm = a, view = view.transform, mesh };
        }).ToArray();
        var light = new GameObject("QA light").AddComponent<Light>();
        light.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(60f, 20f, 0f);
        light.intensity = 1.4f;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.5f, .46f, .44f);
        var eye = new GameObject("QA camera").AddComponent<Camera>();
        eye.clearFlags = CameraClearFlags.SolidColor;
        eye.backgroundColor = new Color(.24f, .23f, .23f);
        eye.fieldOfView = 40f;
        eye.nearClipPlane = .02f;
        eye.transform.position = new Vector3(0f, .27f, .1f);
        eye.transform.rotation = Quaternion.Euler(38f, 0f, 0f);
        var target = new RenderTexture(1280, 640, 24, RenderTextureFormat.ARGB32);
        target.Create();
        Line("Цель рендера создана: " + target.IsCreated());
        Save();
        eye.targetTexture = target;
        foreach (var pose in new[] { "Relaxed", "Fist", "Point", "HoldCard" })
        {
            clips.First(c => c.name.Contains("Pose_" + pose)).SampleAnimation(hands, 0f);
            foreach (var item in poseMeshes)
            {
                item.arm.BakeMesh(item.mesh, true);
                item.view.SetPositionAndRotation(item.arm.transform.position, item.arm.transform.rotation);
                item.mesh.RecalculateBounds();
            }
            eye.Render();
            RenderTexture.active = target;
            var picture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            picture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            picture.Apply();
            File.WriteAllBytes(Path.Combine(_out, "unity-" + pose + ".png"), picture.EncodeToPNG());
            Line("Кадр Unity: " + pose);
        }
        RenderTexture.active = null;
    }
}
