using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Шаблоны материалов 3D-стола в Resources/ThreeD/Materials и профиль постобработки Resources/ThreeD/TableLook.
/// Материал, созданный только кодом через Shader.Find, на телефоне может остаться без шейдера: сборка берёт шейдеры и их варианты из материалов-файлов.
/// С профилем то же: URP оставляет в сборке только те эффекты постобработки, что включены в профилях-файлах проекта.
/// Запуск в отдельном проекте: Unity -batchmode -executeMethod Table3DMaterialsBuilder.Build, затем файлы копируются в игру.
/// </summary>
public static class Table3DMaterialsBuilder
{
    private const string Parent = "Assets/Resources/ThreeD";
    private const string Folder = Parent + "/Materials";
    private const string Hands = Parent + "/Hands/";
    private const string Look = Parent + "/TableLook.asset";

    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder(Parent, "Materials");
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        var unlit = Shader.Find("Universal Render Pipeline/Unlit");

        Save(new Material(lit), "Lit");
        Save(new Material(unlit), "Unlit");

        // Лицо и рубашка карты: объём даёт свет стола. Слабая подсветка картинкой только держит значения читаемыми в тени руки,
        // сильная делает карту экраном, а не картоном.
        var card = new Material(lit);
        card.SetColor("_EmissionColor", new Color(.1f, .1f, .1f));
        card.SetTexture("_EmissionMap", Texture2D.whiteTexture);
        card.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        card.EnableKeyword("_EMISSION");
        // Ламинированный картон: блик лампы скользит по карте, когда она поворачивается.
        card.SetFloat("_Smoothness", .52f);
        Save(card, "Card");

        // Свой шейдер кожи (Resources/ThreeD/Shaders/CardCitySkin.shader): свет заходит за край тени тёплым красным,
        // ногти по маске блестят. На стандартном Lit кожа выглядит пластиком.
        var nails = Hands + "T_Hand_Male_Mask.png";
        var mask = (TextureImporter)AssetImporter.GetAtPath(nails);
        if (mask.sRGBTexture)
        {
            mask.sRGBTexture = false;
            mask.SaveAndReimport();
        }
        var skin = new Material(Shader.Find("CardCity/Skin"));
        // Молодая чистая кожа (scripts/3d/hands/skin_young.py): тон и розовые места по листу генерации/12-жесты, без вен и морщин,
        // цвет считается по точкам руки, поэтому на стыках развёртки шва нет. Рельеф — нормали со скульптуры (bake.py).
        // Кожа с фото (T_Hand_Male_Photo_*) осталась в проекте: автору рука с ней казалась старческой.
        // Материал после сборки в отдельном проекте ссылается на текстуры по GUID: их .meta должны совпадать с проектом автора.
        skin.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Hands + "T_Hand_Male_Young_BaseColor.png"));
        skin.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Hands + "T_Hand_Male_Young_Normal.png"));
        skin.SetTexture("_NailMask", AssetDatabase.LoadAssetAtPath<Texture2D>(nails));
        skin.EnableKeyword("_NORMALMAP");
        // Цвет кожи даёт текстура, оттенок не нужен.
        skin.SetColor("_BaseColor", Color.white);
        // Мягкий рассеянный блик; в складках маска (канал G, scripts/3d/hands/skin_maps.py) делает кожу ещё матовее.
        skin.SetFloat("_Smoothness", .3f);
        skin.SetFloat("_BumpScale", 1f);
        skin.SetFloat("_CreaseRoughness", .45f);
        // Ногти светло-розовые и в меру блестят; рассеивание и заход света за тень мягче, иначе край кисти горит красным,
        // а ладонь, повёрнутая к играющему в хвате ребром большого пальца, — красным пятном (было .45 и .4).
        skin.SetColor("_NailColor", new Color(.9f, .74f, .68f));
        skin.SetFloat("_NailSmoothness", .65f);
        skin.SetFloat("_Scatter", .3f);
        // Без отражения комнаты: на пальце вдоль взгляда кожа на скользящем угле отражала тёмный зал серой полосой, как пластик.
        skin.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        skin.SetFloat("_EnvironmentReflections", 0f);
        skin.SetFloat("_Wrap", .3f);
        Save(skin, "ArmSkin");

        // Сглаженная молодая кожа (scripts/3d/hands/skin_smooth.py; автор принял 2026-11-16, в игре — OwnArms3DView.SkinMaterial):
        // та же кожа с картами T_Hand_Male_Smooth_* (меньше продольных борозд, сухожилий и сетки морщин на костяшках), блик
        // чуть шире и рельеф в рассеянном свете мягче. ArmSkin остаётся рядом: вернуть прежнюю кожу — одно имя в OwnArms3DView.
        var smooth = new Material(skin);
        smooth.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Hands + "T_Hand_Male_Smooth_BaseColor.png"));
        smooth.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Hands + "T_Hand_Male_Smooth_Normal.png"));
        smooth.SetFloat("_Smoothness", .34f);
        smooth.SetFloat("_DiffuseSoftness", .55f);
        Save(smooth, "ArmSkin_Smooth");

        BuildLook();

        AssetDatabase.SaveAssets();
        foreach (var name in new[] { "Card", "ArmSkin", "ArmSkin_Smooth" })
        {
            AssetDatabase.ImportAsset(Folder + "/" + name + ".mat", ImportAssetOptions.ForceUpdate);
            var saved = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/" + name + ".mat");
            Debug.Log("KEYWORDS " + name + ": " + string.Join(",", saved.shaderKeywords) + " flags " + saved.globalIlluminationFlags);
        }
        var look = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Look);
        Debug.Log("LOOK: " + (look == null ? "none" : string.Join(",", look.components.ConvertAll(c => c.GetType().Name))));
        Debug.Log("TEMPLATES DONE: " + string.Join(", ", AssetDatabase.FindAssets("t:Material", new[] { Folder })));
        Debug.Log("SKIN: " + skin.shader.name + " supported " + skin.shader.isSupported + " passes " + skin.passCount + " nails " + (skin.GetTexture("_NailMask") != null));
        var ready = skin.shader.name == "CardCity/Skin" && skin.shader.isSupported && skin.GetTexture("_BaseMap") != null
            && skin.GetTexture("_BumpMap") != null && skin.GetTexture("_NailMask") != null
            && smooth.GetTexture("_BaseMap") != null && smooth.GetTexture("_BumpMap") != null;
        EditorApplication.Exit(ready && look != null && look.components.Count == 4 ? 0 : 1);
    }

    /// <summary>Тёмный зал под лампой: мягкая кривая тонов вместо обрезки светлого, лёгкая виньетка, свечение только у ярких бликов.</summary>
    private static void BuildLook()
    {
        AssetDatabase.DeleteAsset(Look);
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = "TableLook";
        AssetDatabase.CreateAsset(profile, Look);

        var tone = Add<Tonemapping>(profile);
        tone.mode.Override(TonemappingMode.Neutral);
        var color = Add<ColorAdjustments>(profile);
        color.postExposure.Override(.25f);
        color.contrast.Override(12f);
        color.saturation.Override(-4f);
        var vignette = Add<Vignette>(profile);
        vignette.intensity.Override(.3f);
        vignette.smoothness.Override(.45f);
        var bloom = Add<Bloom>(profile);
        bloom.threshold.Override(1.05f);
        bloom.intensity.Override(.22f);
        bloom.scatter.Override(.6f);
        EditorUtility.SetDirty(profile);
    }

    private static T Add<T>(VolumeProfile profile) where T : VolumeComponent
    {
        var component = profile.Add<T>();
        component.name = typeof(T).Name;
        // Без этого эффект живёт только до перезапуска: компоненты профиля хранятся внутри его файла.
        AssetDatabase.AddObjectToAsset(component, profile);
        return component;
    }

    private static void Save(Material material, string name)
    {
        var path = Folder + "/" + name + ".mat";
        AssetDatabase.DeleteAsset(path);
        material.name = name;
        AssetDatabase.CreateAsset(material, path);
    }
}
