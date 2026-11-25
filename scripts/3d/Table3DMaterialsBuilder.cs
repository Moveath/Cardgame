using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
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

    /// <summary>Быстрая сборка только материала клубного стула (Resources/ThreeD/Materials/Chair.mat) и его 2K PBR-карт.</summary>
    public static void BuildChairOnly()
    {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder(Parent, "Materials");
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        var chairBasePath = Parent + "/Chair_BaseColor.png";
        var chairNormalPath = Parent + "/Chair_Normal.png";
        var chairMgPath = Parent + "/Chair_MetallicGloss.png";
        var chairBaseImp = (TextureImporter)AssetImporter.GetAtPath(chairBasePath);
        if (chairBaseImp != null)
        {
            chairBaseImp.maxTextureSize = 2048;
            chairBaseImp.mipmapEnabled = true;
            chairBaseImp.filterMode = FilterMode.Trilinear;
            chairBaseImp.anisoLevel = 8;
            chairBaseImp.SaveAndReimport();
        }
        var chairNormImp = (TextureImporter)AssetImporter.GetAtPath(chairNormalPath);
        if (chairNormImp != null)
        {
            chairNormImp.textureType = TextureImporterType.NormalMap;
            chairNormImp.maxTextureSize = 2048;
            chairNormImp.mipmapEnabled = true;
            chairNormImp.filterMode = FilterMode.Trilinear;
            chairNormImp.anisoLevel = 8;
            chairNormImp.SaveAndReimport();
        }
        var chairMgImp = (TextureImporter)AssetImporter.GetAtPath(chairMgPath);
        if (chairMgImp != null)
        {
            chairMgImp.sRGBTexture = false;
            chairMgImp.alphaIsTransparency = false;
            chairMgImp.maxTextureSize = 2048;
            chairMgImp.mipmapEnabled = true;
            chairMgImp.filterMode = FilterMode.Trilinear;
            chairMgImp.SaveAndReimport();
        }
        var chairAlbedo = AssetDatabase.LoadAssetAtPath<Texture2D>(chairBasePath);
        var chairNormal = AssetDatabase.LoadAssetAtPath<Texture2D>(chairNormalPath);
        var chairMg = AssetDatabase.LoadAssetAtPath<Texture2D>(chairMgPath);
        var chairMat = new Material(lit);
        chairMat.SetColor("_BaseColor", Color.white);
        chairMat.SetTexture("_BaseMap", chairAlbedo);
        chairMat.SetTexture("_BumpMap", chairNormal);
        chairMat.SetFloat("_BumpScale", 1f);
        chairMat.EnableKeyword("_NORMALMAP");
        chairMat.SetTexture("_MetallicGlossMap", chairMg);
        chairMat.SetFloat("_Metallic", 1f);
        chairMat.SetFloat("_Smoothness", .92f);
        chairMat.EnableKeyword("_METALLICSPECGLOSSMAP");
        chairMat.SetColor("_EmissionColor", new Color(.045f, .038f, .034f));
        chairMat.SetTexture("_EmissionMap", chairAlbedo);
        chairMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        chairMat.EnableKeyword("_EMISSION");
        Save(chairMat, "Chair");
        AssetDatabase.SaveAssets();
        var ok = chairAlbedo != null && chairNormal != null && chairMg != null;
        Debug.Log($"=== CHAIR MAT BUILT: ok={ok}, albedo={chairAlbedo != null}, normal={chairNormal != null}, mg={chairMg != null}");
        EditorApplication.Exit(ok ? 0 : 1);
    }

    /// <summary>
    /// Сборка Unity Humanoid-аватара базового персонажа (Resources/ThreeD/Characters/BaseMale.fbx),
    /// его сидячих клипов Mixamo, 2K PBR-материала одежды (Materials/BodyMale_Outfit.mat) и AnimatorController.
    /// </summary>
    public static void BuildCharacterOnly()
    {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder(Parent, "Materials");
        const string charsDir = Parent + "/Characters";
        var lit = Shader.Find("Universal Render Pipeline/Lit");

        // 1. Импорт 2K PBR-текстур одежды (свитер + джинсы + ботинки)
        var basePath = charsDir + "/BodyMale_BaseColor.png";
        var normPath = charsDir + "/BodyMale_Normal.png";
        var mgPath = charsDir + "/BodyMale_MetallicGloss.png";
        var baseImp = (TextureImporter)AssetImporter.GetAtPath(basePath);
        if (baseImp != null)
        {
            baseImp.maxTextureSize = 2048;
            baseImp.mipmapEnabled = true;
            baseImp.filterMode = FilterMode.Trilinear;
            baseImp.anisoLevel = 8;
            baseImp.SaveAndReimport();
        }
        var normImp = (TextureImporter)AssetImporter.GetAtPath(normPath);
        if (normImp != null)
        {
            normImp.textureType = TextureImporterType.NormalMap;
            normImp.maxTextureSize = 2048;
            normImp.mipmapEnabled = true;
            normImp.filterMode = FilterMode.Trilinear;
            normImp.anisoLevel = 8;
            normImp.SaveAndReimport();
        }
        var mgImp = (TextureImporter)AssetImporter.GetAtPath(mgPath);
        if (mgImp != null)
        {
            mgImp.sRGBTexture = false;
            mgImp.alphaIsTransparency = false;
            mgImp.maxTextureSize = 2048;
            mgImp.mipmapEnabled = true;
            mgImp.filterMode = FilterMode.Trilinear;
            mgImp.SaveAndReimport();
        }

        var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(basePath);
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normPath);
        var mg = AssetDatabase.LoadAssetAtPath<Texture2D>(mgPath);
        var outfitMat = new Material(lit);
        outfitMat.SetColor("_BaseColor", Color.white);
        outfitMat.SetTexture("_BaseMap", albedo);
        outfitMat.SetTexture("_BumpMap", normal);
        outfitMat.SetFloat("_BumpScale", 1.15f);
        outfitMat.EnableKeyword("_NORMALMAP");
        // Вязаный свитер и джинсы должны быть благородно матовыми без пластикового блика верхней лампы!
        outfitMat.SetFloat("_Metallic", 0f);
        outfitMat.SetFloat("_Smoothness", .08f);
        outfitMat.SetFloat("_SpecularHighlights", 0f);
        outfitMat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        outfitMat.SetFloat("_EnvironmentReflections", 0f);
        outfitMat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        // Мягкий 4.5% отсвет, чтобы фактура вязки свитера и джинсы оставались читаемыми в тёмном зале.
        outfitMat.SetColor("_EmissionColor", new Color(.048f, .048f, .052f));
        outfitMat.SetTexture("_EmissionMap", albedo);
        outfitMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        outfitMat.EnableKeyword("_EMISSION");
        Save(outfitMat, "BodyMale_Outfit");

        // 2. Настройка Humanoid-рига на базовой модели BaseMale.fbx (из Mixamo 65-bone Standard Skeleton)
        var baseFbxPath = charsDir + "/BaseMale.fbx";
        var baseModelImp = (ModelImporter)AssetImporter.GetAtPath(baseFbxPath);
        if (baseModelImp != null)
        {
            baseModelImp.animationType = ModelImporterAnimationType.Human;
            baseModelImp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            baseModelImp.importAnimation = false;
            baseModelImp.materialImportMode = ModelImporterMaterialImportMode.None;
            baseModelImp.SaveAndReimport();
        }
        var baseAvatar = AssetDatabase.LoadAllAssetsAtPath(baseFbxPath).OfType<Avatar>().FirstOrDefault();

        // 3. Импорт сидячих клипов Mixamo как Humanoid со СВОИМ T-Pose Avatar (CreateFromThisModel!),
        //    чтобы Unity корректно пересчитал мышечные кривые из T-Pose клипа в A-Pose bind-позу BaseMale!
        AnimationClip ConfigureClip(string fileName, bool loop)
        {
            var clipPath = charsDir + "/" + fileName;
            var imp = (ModelImporter)AssetImporter.GetAtPath(clipPath);
            if (imp == null) return null;
            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.importAnimation = true;
            imp.materialImportMode = ModelImporterMaterialImportMode.None;
            imp.SaveAndReimport();

            // После первого реимпорта берём реальные defaultClipAnimations и включаем loop + lockRoot
            var clips = imp.defaultClipAnimations;
            for (var i = 0; i < clips.Length; i++)
            {
                clips[i].loopTime = loop;
                clips[i].loopPose = loop;
                clips[i].lockRootRotation = true;
                clips[i].lockRootHeightY = true;
                clips[i].lockRootPositionXZ = true;
                clips[i].keepOriginalOrientation = true;
                clips[i].keepOriginalPositionY = true;
                clips[i].keepOriginalPositionXZ = true;
            }
            imp.clipAnimations = clips;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAllAssetsAtPath(clipPath)
                .OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        }

        var clipIdle = ConfigureClip("BaseMale_SitIdle.fbx", true);
        var clipTalk = ConfigureClip("BaseMale_SitTalk.fbx", true);
        var clipDisbelief = ConfigureClip("BaseMale_SitDisbelief.fbx", false);

        // 4. Сборка AnimatorController для соперников за столом
        var ctrlPath = charsDir + "/OpponentAnimator.controller";
        AssetDatabase.DeleteAsset(ctrlPath);
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
        var sm = ctrl.layers[0].stateMachine;
        var stIdle = sm.AddState("SitIdle");
        stIdle.motion = clipIdle;
        sm.defaultState = stIdle;
        if (clipTalk != null)
        {
            var stTalk = sm.AddState("SitTalk");
            stTalk.motion = clipTalk;
        }
        if (clipDisbelief != null)
        {
            var stDis = sm.AddState("SitDisbelief");
            stDis.motion = clipDisbelief;
            var tr = stDis.AddTransition(stIdle);
            tr.hasExitTime = true;
            tr.exitTime = 0.92f;
            tr.duration = 0.25f;
        }

        AssetDatabase.SaveAssets();
        var ok = albedo != null && normal != null && mg != null
            && baseAvatar != null && baseAvatar.isValid && baseAvatar.isHuman
            && clipIdle != null && clipTalk != null && clipDisbelief != null;
        Debug.Log($"=== CHARACTER BUILT: ok={ok}, avatarValid={baseAvatar?.isValid}, human={baseAvatar?.isHuman}, idle={clipIdle?.name}({clipIdle?.length:F2}s), talk={clipTalk?.name}({clipTalk?.length:F2}s), disbelief={clipDisbelief?.name}({clipDisbelief?.length:F2}s)");
        EditorApplication.Exit(ok ? 0 : 1);
    }

    /// <summary>
    /// Сборка безголового тела BaseMale_Headless.fbx (Humanoid Avatar) и всех 3D-голов друзей из
    /// Resources/ThreeD/Characters/Heads/Head_*.fbx + их 2K URP PBR-материалов (Materials/Head_*.mat).
    /// </summary>
    public static void BuildHeadsAndHeadless()
    {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder(Parent, "Materials");
        const string charsDir = Parent + "/Characters";
        const string headsDir = charsDir + "/Heads";
        var lit = Shader.Find("Universal Render Pipeline/Lit");

        // 1. Импорт BaseMale_Headless.fbx (эталонный Blender Humanoid Avatar) и BaseMale.fbx (тот же скелет Armature)
        //    без сжатия меша с родными гладкими нормалями из Blender
        var headlessPath = charsDir + "/BaseMale_Headless.fbx";
        var headlessImp = (ModelImporter)AssetImporter.GetAtPath(headlessPath);
        if (headlessImp != null)
        {
            headlessImp.animationType = ModelImporterAnimationType.Human;
            headlessImp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            headlessImp.importAnimation = false;
            headlessImp.materialImportMode = ModelImporterMaterialImportMode.None;
            headlessImp.meshCompression = ModelImporterMeshCompression.Off;
            headlessImp.importNormals = ModelImporterNormals.Import;
            headlessImp.SaveAndReimport();
        }
        var headlessAvatar = AssetDatabase.LoadAllAssetsAtPath(headlessPath).OfType<Avatar>().FirstOrDefault();

        var baseMaleAvatar = ConfigureCopyBody(charsDir + "/BaseMale.fbx", headlessAvatar);
        Debug.Log($"=== BASE MALE AVATAR: valid={baseMaleAvatar?.isValid}, human={baseMaleAvatar?.isHuman}, copyFromHeadless={headlessAvatar != null}");

        // 1a. Скульптурные варианты тел (женское, плотное мужское + их FPS-версии первого лица) — все на одном скелете Armature!
        foreach (var variant in new[] { "BaseMale_FpsBody.fbx", "BaseFemale_Headless.fbx", "BaseFemale_FpsBody.fbx", "BaseMaleHeavy_Headless.fbx", "BaseMaleHeavy_FpsBody.fbx" })
        {
            var av = ConfigureCopyBody(charsDir + "/" + variant, headlessAvatar);
            Debug.Log($"=== VARIANT BODY AVATAR ({variant}): valid={av?.isValid}, human={av?.isHuman}");
        }

        // 1b. Чёткие кожные материалы рук (стандарт, смуглая avatar_08, зрелая 03/06, светлая 02/04/10/12)
        //     на CardCity/Skin + BC7 HQ + Trilinear + Aniso 16 — для соперников и текстур рук первого лица!
        var oppHandsShader = Shader.Find("CardCity/Skin") ?? lit;
        BuildHandSkinVariant(Hands + "TripoHands_BaseColor.jpg", "OpponentHands", oppHandsShader, .16f, .68f, .30f, new Color(.96f, .52f, .40f, 1f));
        BuildHandSkinVariant(Hands + "TripoHands_Dark_BaseColor.png", "OpponentHands_Dark", oppHandsShader, .17f, .58f, .14f, new Color(.48f, .24f, .16f, 1f));
        BuildHandSkinVariant(Hands + "TripoHands_Aged_BaseColor.png", "OpponentHands_Aged", oppHandsShader, .12f, .65f, .24f, new Color(.92f, .50f, .40f, 1f));
        BuildHandSkinVariant(Hands + "TripoHands_Fair_BaseColor.png", "OpponentHands_Fair", oppHandsShader, .18f, .68f, .32f, new Color(.98f, .54f, .44f, 1f));

        // 2. Автоматически собираем все Head_*.fbx с МАКСИМАЛЬНЫМ качеством (без сжатия меша, 4K BC7 HQ текстуры,
        //    мягкий кожный шейдер CardCity/Skin с подповерхностным светом — без грубых серых теней и воскового блеска!).
        var skinShader = Shader.Find("CardCity/Skin") ?? lit;
        var builtHeads = 0;
        var fbxGuids = AssetDatabase.FindAssets("Head_ t:Model", new[] { headsDir });
        foreach (var guid in fbxGuids)
        {
            var fbxPath = AssetDatabase.GUIDToAssetPath(guid);
            var headName = System.IO.Path.GetFileNameWithoutExtension(fbxPath);
            var mImp = (ModelImporter)AssetImporter.GetAtPath(fbxPath);
            if (mImp != null)
            {
                mImp.animationType = ModelImporterAnimationType.None;
                mImp.importAnimation = false;
                mImp.materialImportMode = ModelImporterMaterialImportMode.None;
                // Отключаем квантование вершин — сохраняем 100% чёткую скульптуру век, носа и губ из Tripo3D!
                mImp.meshCompression = ModelImporterMeshCompression.Off;
                mImp.optimizeMeshPolygons = false;
                mImp.optimizeMeshVertices = false;
                mImp.SaveAndReimport();
            }

            var bPath = headsDir + "/" + headName + "_BaseColor.png";
            var nPath = headsDir + "/" + headName + "_Normal.png";
            var mgPath = headsDir + "/" + headName + "_MetallicGloss.png";
            var bImp = (TextureImporter)AssetImporter.GetAtPath(bPath);
            if (bImp != null)
            {
                // 4K + CompressedHQ (BC7 на ПК / ASTC на мобильном) + отрицательный mipBias -> глаза и брови как бритва!
                bImp.maxTextureSize = 4096;
                bImp.textureCompression = TextureImporterCompression.CompressedHQ;
                bImp.mipmapEnabled = true;
                bImp.mipMapBias = -.75f;
                bImp.filterMode = FilterMode.Trilinear;
                bImp.anisoLevel = 16;
                bImp.SaveAndReimport();
            }
            var nImp = (TextureImporter)AssetImporter.GetAtPath(nPath);
            if (nImp != null)
            {
                nImp.textureType = TextureImporterType.NormalMap;
                nImp.maxTextureSize = 2048;
                nImp.textureCompression = TextureImporterCompression.CompressedHQ;
                nImp.mipmapEnabled = true;
                nImp.filterMode = FilterMode.Trilinear;
                nImp.anisoLevel = 8;
                nImp.SaveAndReimport();
            }
            var mgImp = (TextureImporter)AssetImporter.GetAtPath(mgPath);
            if (mgImp != null)
            {
                mgImp.sRGBTexture = false;
                mgImp.alphaIsTransparency = false;
                mgImp.maxTextureSize = 2048;
                mgImp.mipmapEnabled = true;
                mgImp.filterMode = FilterMode.Trilinear;
                mgImp.SaveAndReimport();
            }

            var col = AssetDatabase.LoadAssetAtPath<Texture2D>(bPath);
            var nrm = AssetDatabase.LoadAssetAtPath<Texture2D>(nPath);
            if (col == null) continue;

            var isDarkHead = headName == "Head_Avatar08";
            var isAgedHead = headName == "Head_Avatar03" || headName == "Head_Avatar06";
            var mat = new Material(skinShader);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetTexture("_BaseMap", col);
            if (nrm != null)
            {
                mat.SetTexture("_BumpMap", nrm);
                // Для пожилых персонажей (03/06) чуть выразительнее рельеф морщин (0.48), для молодых мягкий 0.35.
                mat.SetFloat("_BumpScale", isAgedHead ? .48f : .35f);
                mat.EnableKeyword("_NORMALMAP");
            }
            mat.SetFloat("_Smoothness", isAgedHead ? .14f : .20f);
            mat.SetFloat("_Wrap", isDarkHead ? .52f : .58f);
            mat.SetFloat("_Scatter", isDarkHead ? .22f : isAgedHead ? .25f : .32f);
            mat.SetColor("_SubsurfaceColor", isDarkHead ? new Color(.68f, .30f, .20f, 1f) : new Color(.96f, .48f, .36f, 1f));
            mat.SetFloat("_DiffuseSoftness", .68f);
            mat.SetFloat("_NailTint", 0f);
            mat.SetFloat("_CreaseRoughness", 0f);
            mat.SetFloat("_EnvironmentReflections", 0f);
            mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            Save(mat, headName);
            builtHeads++;
            Debug.Log($"=== HEAD MAT BUILT (HQ Skin): {headName} (shader={skinShader.name}, col={col.width}x{col.height}, nrm={nrm != null})");
        }

        AssetDatabase.SaveAssets();
        var ok = headlessAvatar != null && headlessAvatar.isValid && headlessAvatar.isHuman && builtHeads > 0;
        Debug.Log($"=== HEADS+HEADLESS BUILT: ok={ok}, headlessValid={headlessAvatar?.isValid}, human={headlessAvatar?.isHuman}, heads={builtHeads}");
        EditorApplication.Exit(ok ? 0 : 1);
    }

    private static Avatar ConfigureCopyBody(string fbxPath, Avatar sourceAvatar)
    {
        var imp = (ModelImporter)AssetImporter.GetAtPath(fbxPath);
        if (imp == null) return null;
        imp.animationType = ModelImporterAnimationType.Human;
        imp.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
        imp.sourceAvatar = sourceAvatar;
        imp.importAnimation = false;
        imp.materialImportMode = ModelImporterMaterialImportMode.None;
        imp.meshCompression = ModelImporterMeshCompression.Off;
        imp.importNormals = ModelImporterNormals.Import;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<Avatar>().FirstOrDefault() ?? sourceAvatar;
    }

    private static void BuildHandSkinVariant(string texPath, string matName, Shader shader, float smoothness, float wrap, float scatter, Color sss)
    {
        var imp = (TextureImporter)AssetImporter.GetAtPath(texPath);
        if (imp != null)
        {
            imp.maxTextureSize = 2048;
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.mipmapEnabled = true;
            imp.mipMapBias = -.4f;
            imp.filterMode = FilterMode.Trilinear;
            imp.anisoLevel = 16;
            imp.SaveAndReimport();
        }
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        if (tex == null) return;
        var mat = new Material(shader);
        mat.SetColor("_BaseColor", Color.white);
        mat.SetTexture("_BaseMap", tex);
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_Wrap", wrap);
        mat.SetFloat("_Scatter", scatter);
        mat.SetColor("_SubsurfaceColor", sss);
        mat.SetFloat("_DiffuseSoftness", .85f);
        mat.SetFloat("_NailTint", 0f);
        mat.SetFloat("_CreaseRoughness", 0f);
        mat.SetFloat("_EnvironmentReflections", 0f);
        mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        Save(mat, matName);
        Debug.Log($"=== HAND SKIN MAT BUILT: {matName} ({tex.width}x{tex.height})");
    }

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
        // Сатиновый картон без резкого блика верхней лампы: карты на центре стола остаются контрастными и читаемыми.
        card.SetFloat("_Smoothness", .26f);
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

        // Клубный хрустальный стакан для виски (Resources/ThreeD/whiskey_glass.fbx, scripts/3d/build_whiskey_glass.py):
        // прозрачный гранёный хрусталь, античный золотой ободок и объёмная гравировка трефы с картой рельефа.
        var crystalShader = Shader.Find("CardCity/CrystalGlass");
        var crystal = new Material(crystalShader);
        Save(crystal, "CrystalGlass");

        // В тёмном зале без Reflection Probe высокая металличность (0.95) даёт почти чёрный металл: 0.55 + мягкая подсветка
        // держит тёплый благородный блеск античного золота со всех мест за столом (как у золотого обода стола).
        var glassGold = new Material(lit);
        glassGold.SetColor("_BaseColor", new Color(.94f, .76f, .38f));
        glassGold.SetFloat("_Metallic", .55f);
        glassGold.SetFloat("_Smoothness", .74f);
        glassGold.SetColor("_EmissionColor", new Color(.05f, .038f, .015f));
        glassGold.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        glassGold.EnableKeyword("_EMISSION");
        Save(glassGold, "GlassGold");

        var clubNormalPath = Parent + "/GlassClub_Normal.png";
        var clubNormalImporter = (TextureImporter)AssetImporter.GetAtPath(clubNormalPath);
        if (clubNormalImporter != null && clubNormalImporter.textureType != TextureImporterType.NormalMap)
        {
            clubNormalImporter.textureType = TextureImporterType.NormalMap;
            clubNormalImporter.SaveAndReimport();
        }
        var clubAlbedo = AssetDatabase.LoadAssetAtPath<Texture2D>(Parent + "/GlassClub_BaseColor.png");
        var clubNormal = AssetDatabase.LoadAssetAtPath<Texture2D>(clubNormalPath);
        var glassClub = new Material(lit);
        glassClub.SetColor("_BaseColor", new Color(1f, .95f, .84f));
        glassClub.SetTexture("_BaseMap", clubAlbedo);
        glassClub.SetTexture("_BumpMap", clubNormal);
        glassClub.SetFloat("_BumpScale", 1.25f);
        glassClub.EnableKeyword("_NORMALMAP");
        glassClub.SetFloat("_Metallic", .50f);
        glassClub.SetFloat("_Smoothness", .62f);
        glassClub.SetColor("_EmissionColor", new Color(.065f, .048f, .018f));
        glassClub.SetTexture("_EmissionMap", clubAlbedo);
        glassClub.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        glassClub.EnableKeyword("_EMISSION");
        Save(glassClub, "GlassClub");

        // Клубный стул с каретной стяжкой и золотым медальоном трефы (Resources/ThreeD/chair.fbx, scripts/3d/build_chair.py):
        // 2K PBR-карты (BaseColor, OpenGL NormalMap, MetallicGlossMap: R = Metallic, A = Smoothness).
        var chairBasePath = Parent + "/Chair_BaseColor.png";
        var chairNormalPath = Parent + "/Chair_Normal.png";
        var chairMgPath = Parent + "/Chair_MetallicGloss.png";
        var chairBaseImp = (TextureImporter)AssetImporter.GetAtPath(chairBasePath);
        if (chairBaseImp != null)
        {
            chairBaseImp.maxTextureSize = 2048;
            chairBaseImp.mipmapEnabled = true;
            chairBaseImp.filterMode = FilterMode.Trilinear;
            chairBaseImp.anisoLevel = 8;
            chairBaseImp.SaveAndReimport();
        }
        var chairNormImp = (TextureImporter)AssetImporter.GetAtPath(chairNormalPath);
        if (chairNormImp != null)
        {
            chairNormImp.textureType = TextureImporterType.NormalMap;
            chairNormImp.maxTextureSize = 2048;
            chairNormImp.mipmapEnabled = true;
            chairNormImp.filterMode = FilterMode.Trilinear;
            chairNormImp.anisoLevel = 8;
            chairNormImp.SaveAndReimport();
        }
        var chairMgImp = (TextureImporter)AssetImporter.GetAtPath(chairMgPath);
        if (chairMgImp != null)
        {
            chairMgImp.sRGBTexture = false;
            chairMgImp.alphaIsTransparency = false;
            chairMgImp.maxTextureSize = 2048;
            chairMgImp.mipmapEnabled = true;
            chairMgImp.filterMode = FilterMode.Trilinear;
            chairMgImp.SaveAndReimport();
        }
        var chairAlbedo = AssetDatabase.LoadAssetAtPath<Texture2D>(chairBasePath);
        var chairNormal = AssetDatabase.LoadAssetAtPath<Texture2D>(chairNormalPath);
        var chairMg = AssetDatabase.LoadAssetAtPath<Texture2D>(chairMgPath);
        var chairMat = new Material(lit);
        chairMat.SetColor("_BaseColor", Color.white);
        chairMat.SetTexture("_BaseMap", chairAlbedo);
        chairMat.SetTexture("_BumpMap", chairNormal);
        chairMat.SetFloat("_BumpScale", 1f);
        chairMat.EnableKeyword("_NORMALMAP");
        chairMat.SetTexture("_MetallicGlossMap", chairMg);
        chairMat.SetFloat("_Metallic", 1f);
        chairMat.SetFloat("_Smoothness", .92f);
        chairMat.EnableKeyword("_METALLICSPECGLOSSMAP");
        // Мягкий 4% отсвет, чтобы тёмное дерево и глубокий бордовый бархат оставались читаемыми на дальних местах зала.
        chairMat.SetColor("_EmissionColor", new Color(.045f, .038f, .034f));
        chairMat.SetTexture("_EmissionMap", chairAlbedo);
        chairMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        chairMat.EnableKeyword("_EMISSION");
        Save(chairMat, "Chair");

        BuildLook();

        AssetDatabase.SaveAssets();
        foreach (var name in new[] { "Card", "ArmSkin", "ArmSkin_Smooth", "CrystalGlass", "GlassGold", "GlassClub", "Chair" })
        {
            AssetDatabase.ImportAsset(Folder + "/" + name + ".mat", ImportAssetOptions.ForceUpdate);
            var saved = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/" + name + ".mat");
            Debug.Log("KEYWORDS " + name + ": " + string.Join(",", saved.shaderKeywords) + " flags " + saved.globalIlluminationFlags);
        }
        var look = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Look);
        Debug.Log("LOOK: " + (look == null ? "none" : string.Join(",", look.components.ConvertAll(c => c.GetType().Name))));
        Debug.Log("TEMPLATES DONE: " + string.Join(", ", AssetDatabase.FindAssets("t:Material", new[] { Folder })));
        Debug.Log("SKIN: " + skin.shader.name + " supported " + skin.shader.isSupported + " passes " + skin.passCount + " nails " + (skin.GetTexture("_NailMask") != null));
        Debug.Log("GLASS: " + crystal.shader.name + " supported " + crystal.shader.isSupported + " passes " + crystal.passCount
            + " clubAlbedo=" + (clubAlbedo != null) + " clubNormal=" + (clubNormal != null));
        var ready = skin.shader.name == "CardCity/Skin" && skin.shader.isSupported && skin.GetTexture("_BaseMap") != null
            && skin.GetTexture("_BumpMap") != null && skin.GetTexture("_NailMask") != null
            && smooth.GetTexture("_BaseMap") != null && smooth.GetTexture("_BumpMap") != null
            && crystal.shader.name == "CardCity/CrystalGlass" && crystal.shader.isSupported && crystal.passCount == 2
            && clubAlbedo != null && clubNormal != null
            && chairAlbedo != null && chairNormal != null && chairMg != null;
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
