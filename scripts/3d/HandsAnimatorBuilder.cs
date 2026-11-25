using System.Linq;
using CardGameClient.ThreeD;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Animator Controller своих рук: Resources/ThreeD/Hands/HandsAnimator.controller и маски слоёв Mask_FanArm, Mask_FreeHand.
/// Все позы и жесты — ключевые кадры из Blender (scripts/3d/hands/animations.py, poses.py) в CardCity_Hands.fbx: код кости
/// пальцев не крутит, он только ставит параметры (SetTrigger, SetBool) и вес слоя щипка.
///
/// Слои:
/// Веер держит левая рука, жесты показывает и карты берёт правая (OwnArms3DView.FanSide / FreeSide, in_game.FAN_HAND).
/// - Base (всё тело). Idle — свободная кисть лежит на сукне (клип Take_Idle). Ветка Gestures (вложенный автомат) — жесты
///   Gesture_*: по триггерам из любого состояния, вход за 0,15 с; когда клип доигран, возврат в Idle за 0,2 с. Новый жест —
///   строка в списке Gestures ниже и клип с тем же именем.
/// - FanHand (маска — рука с веером от локтя, поверх Base). HoldCards — держит веер (Take_Idle); HoldCards_Regrip — перехватывает
///   веер по триггеру Regrip (Take_HoldCards); OnTable — лежит на сукне, когда карт нет (Take_TableRest). Переключает bool HoldingCards.
/// - Pinch (маска — свободная кисть с пальцами, поверх, вес 0). Состояние PlayCard — поза щипка Pose_TakeCard: её вес ставит
///   OwnArms3DView, пока рука берёт и несёт карту.
/// - PoseTest (без маски, поверх всего, вес 0) — временная проверка базовых поз новой системы рук (клавиша F в OwnArms3DView).
///   Состояние Clench играет клип Take_TestClench (scripts/3d/hands/game_test.py): обе кисти перед собой, первый кадр —
///   расслабленная кисть Idle, последний — кулак. Время клипа ставит параметр TestClench (0 — Idle, 1 — кулак, Motion Time),
///   разжатие — тот же клип назад. Вес слоя ставит OwnArms3DView: 0 — игра как обычно, 1 — кисти в позе проверки.
/// - GestureTest (без маски, поверх PoseTest, вес 0) — временная проверка новых жестов (клавиши U, J, H в OwnArms3DView,
///   scripts/3d/hands/gesture_test.py). Состояние на жест и руки: <жест> — обе руки, <жест>_R — только правая, <жест>_L —
///   только левая (OwnArms3DView.TestGestures, TestHandsSuffix), клипы Take_Test<жест><руки>: первый кадр — Idle проверки
///   кулака, последний — жест. Какой — параметр TestGesture (номер с 1: жест × 3 + руки), время клипа — TestGestureTime
///   (0 — Idle, 1 — жест). Переход между состояниями — только из Idle (время 0), где все клипы совпадают. Клипы на одну руку,
///   а не слой с маской на руку: маска отделяет кости, но не веса форм кожи (ThumbsSmooth), и слой одной руки писал бы вес
///   формы второй.
///
/// Свои клипы вместо клипов из FBX (анимации с видео руки, купленные или скачанные): клип .anim для этого скелета с именем
/// клипа из Blender (Take_ThumbsUp.anim, Take_Idle.anim, Pose_TakeCard.anim и так далее) кладётся в папку
/// Resources/ThreeD/Hands/Clips, после чего контроллер пересобирается. Состояния, переходы и код при этом не меняются.
///
/// Клипы покоя (Take_Idle, Take_TableRest) повторяются по кругу: это включается здесь в настройках импорта FBX.
/// Запуск в отдельном проекте: Unity -batchmode -executeMethod HandsAnimatorBuilder.Build, затем в игру копируются
/// HandsAnimator.controller, маски Mask_FanArm и Mask_FreeHand и CardCity_Hands.fbx.meta (с повтором клипов) вместе с .meta.
/// </summary>
public static class HandsAnimatorBuilder
{
    private const string Folder = "Assets/Resources/ThreeD/Hands";
    private const string Model = Folder + "/CardCity_Hands.fbx";
    private const string ControllerPath = Folder + "/HandsAnimator.controller";
    private const string OwnClips = Folder + "/Clips";
    // Вход в жест и в перехват веера; выход из жеста, переключение «держит карты / на столе».
    private const float BlendIn = .16f, BlendOut = .2f;
    private static readonly string[] Loops = { "Take_Idle", "Take_TableRest" };
    // Проверка базовых поз: слой, его состояние, клип и параметр времени клипа (OwnArms3DView.PoseTestLayer, TestClench).
    private const string TestLayer = "PoseTest", TestState = "Clench", TestClip = "Take_TestClench", TestTime = "TestClench";
    // Проверка новых жестов: слой, номер состояния (жест и руки) и время клипа (OwnArms3DView.TestGestures).
    private const string GestureLayer = "GestureTest", GestureId = "TestGesture", GestureTime = "TestGestureTime";

    /// <summary>Жесты: триггер, состояние слоя Base, клип из Blender. Состояние — Gesture_ + триггер.</summary>
    private static readonly (string Trigger, string Clip)[] Gestures =
    {
        ("ThumbUp", "Take_ThumbsUp"),
        ("ThumbDown", "Take_ThumbsDown"),
        ("Point", "Take_Point"),
        ("Knock", "Take_TapTable"),
        ("Shrug", "Take_Shrug"),
        ("Fist", "Take_Fist"),
        ("MiddleFinger", "Take_MiddleFinger"),
    };

    public static void Build()
    {
        LoopRestClips();
        var clips = AssetDatabase.LoadAllAssetsAtPath(Model).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToArray();
        var replaced = new System.Collections.Generic.List<string>();
        AnimationClip Clip(string name)
        {
            var own = AssetDatabase.LoadAssetAtPath<AnimationClip>(OwnClips + "/" + name + ".anim");
            if (own == null) return clips.First(c => c.name.EndsWith("|" + name) || c.name == name);
            if (!replaced.Contains(name)) replaced.Add(name);
            return own;
        }
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
        var fanArm = Mask("Mask_FanArm", model, "Forearm" + OwnArms3DView.FanSide);
        var freeHand = Mask("Mask_FreeHand", model, "ForearmTwist" + OwnArms3DView.FreeSide);
        foreach (var old in new[] { "Mask_LeftArm", "Mask_RightHand" })
            AssetDatabase.DeleteAsset(Folder + "/" + old + ".mask");

        AssetDatabase.DeleteAsset(ControllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter(new AnimatorControllerParameter { name = "HoldingCards", type = AnimatorControllerParameterType.Bool, defaultBool = true });
        foreach (var gesture in Gestures) controller.AddParameter(gesture.Trigger, AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Regrip", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("IdleTap", AnimatorControllerParameterType.Trigger);
        controller.AddParameter(TestTime, AnimatorControllerParameterType.Float);
        controller.AddParameter(GestureId, AnimatorControllerParameterType.Int);
        controller.AddParameter(GestureTime, AnimatorControllerParameterType.Float);
        controller.AddLayer("FanHand");
        controller.AddLayer("Pinch");
        controller.AddLayer(TestLayer);
        controller.AddLayer(GestureLayer);
        var layers = controller.layers;
        layers[0].name = "Base";
        layers[1].avatarMask = fanArm;
        layers[1].blendingMode = AnimatorLayerBlendingMode.Override;
        layers[1].defaultWeight = 1f;
        layers[2].avatarMask = freeHand;
        layers[2].blendingMode = AnimatorLayerBlendingMode.Override;
        layers[2].defaultWeight = 0f;
        layers[3].blendingMode = AnimatorLayerBlendingMode.Override;
        layers[3].defaultWeight = 0f;
        layers[4].blendingMode = AnimatorLayerBlendingMode.Override;
        layers[4].defaultWeight = 0f;
        controller.layers = layers;

        // Base: покой свободной руки, фоновое постукивание пальцами в ожидании и жесты.
        var body = controller.layers[0].stateMachine;
        var idle = State(body, "Idle", Clip("Take_Idle"), new Vector3(300f, 0f));
        body.defaultState = idle;
        var idleTapClip = Clip("Take_IdleFingerTap");
        var idleTap = State(body, "IdleFingerTap", idleTapClip, new Vector3(300f, 120f));
        var toIdleTap = idle.AddTransition(idleTap);
        toIdleTap.AddCondition(AnimatorConditionMode.If, 0f, "IdleTap");
        Blend(toIdleTap, BlendIn);
        Blend(idleTap.AddTransition(idle), BlendOut, idleTapClip);
        var gestures = body.AddStateMachine("Gestures", new Vector3(620f, 0f));
        for (var i = 0; i < Gestures.Length; i++)
        {
            var clip = Clip(Gestures[i].Clip);
            var state = State(gestures, "Gesture_" + Gestures[i].Trigger, clip, new Vector3(300f, i * 60f));
            var start = body.AddAnyStateTransition(state);
            start.AddCondition(AnimatorConditionMode.If, 0f, Gestures[i].Trigger);
            start.canTransitionToSelf = false;
            Blend(start, BlendIn);
            Blend(state.AddTransition(idle), BlendOut, clip);
        }

        // FanHand: держит веер, перехватывает его, лежит на сукне без карт.
        var left = controller.layers[1].stateMachine;
        var hold = State(left, "HoldCards", Clip("Take_Idle"), new Vector3(300f, 0f));
        var regripClip = Clip("Take_HoldCards");
        var regrip = State(left, "HoldCards_Regrip", regripClip, new Vector3(300f, 120f));
        var onTable = State(left, "OnTable", Clip("Take_TableRest"), new Vector3(620f, 0f));
        left.defaultState = hold;
        var toRegrip = hold.AddTransition(regrip);
        toRegrip.AddCondition(AnimatorConditionMode.If, 0f, "Regrip");
        Blend(toRegrip, BlendIn);
        Blend(regrip.AddTransition(hold), BlendOut, regripClip);
        foreach (var from in new[] { hold, regrip })
        {
            var down = from.AddTransition(onTable);
            down.AddCondition(AnimatorConditionMode.IfNot, 0f, "HoldingCards");
            Blend(down, BlendOut);
        }
        var up = onTable.AddTransition(hold);
        up.AddCondition(AnimatorConditionMode.If, 0f, "HoldingCards");
        Blend(up, BlendOut);

        // Pinch: свободная кисть берёт и несёт карту — поза щипка, вес слоя у кода.
        var pinch = controller.layers[2].stateMachine;
        pinch.defaultState = State(pinch, "PlayCard", Clip("Pose_TakeCard"), new Vector3(300f, 0f));

        // PoseTest: Idle — кулак одним клипом, время клипа — параметр TestClench. Переходов нет: вход и выход — вес слоя.
        var test = controller.layers[3].stateMachine;
        var clench = State(test, TestState, Clip(TestClip), new Vector3(300f, 0f));
        clench.timeParameterActive = true;
        clench.timeParameter = TestTime;
        test.defaultState = clench;

        // GestureTest: жест и руки — состояние, время клипа — параметр. Из любого состояния в нужное по номеру; OwnArms3DView
        // меняет номер, только когда время 0 (Idle), поэтому переход между состояниями не виден.
        var gestureMachine = controller.layers[4].stateMachine;
        for (var g = 0; g < OwnArms3DView.TestGestures.Length; g++)
        {
            for (var h = 0; h < OwnArms3DView.TestHandsSuffix.Length; h++)
            {
                var name = OwnArms3DView.TestGestures[g] + OwnArms3DView.TestHandsSuffix[h];
                var state = State(gestureMachine, name, Clip("Take_Test" + name), new Vector3(300f + h * 260f, g * 60f));
                state.timeParameterActive = true;
                state.timeParameter = GestureTime;
                if (g == 0 && h == 0) gestureMachine.defaultState = state;
                var start = gestureMachine.AddAnyStateTransition(state);
                start.AddCondition(AnimatorConditionMode.Equals, g * OwnArms3DView.TestHandsSuffix.Length + h + 1, GestureId);
                start.canTransitionToSelf = false;
                Blend(start, BlendIn);
            }
        }
        // Маски рук прежнего варианта проверки (слой на руку) больше не нужны.
        foreach (var old in new[] { "Mask_TestArm_L", "Mask_TestArm_R" })
            AssetDatabase.DeleteAsset(Folder + "/" + old + ".mask");

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        var states = controller.layers.SelectMany(l => States(l.stateMachine)).ToArray();
        var transitions = controller.layers.SelectMany(l => l.stateMachine.anyStateTransitions).Concat(states.SelectMany(s => s.transitions)).ToArray();
        var ready = states.All(s => s.motion != null) && transitions.All(t => t.duration >= BlendIn && t.duration <= BlendOut);
        Debug.Log("HANDS ANIMATOR: layers " + string.Join(", ", controller.layers.Select(l => l.name + (l.avatarMask ? " (" + l.avatarMask.name + ")" : "")))
            + "; states " + string.Join(", ", states.Select(s => s.name + "=" + (s.motion ? s.motion.name : "none")))
            + "; transitions " + transitions.Length + "; parameters " + string.Join(", ", controller.parameters.Select(p => p.name + ":" + p.type))
            + "; own clips " + (replaced.Count > 0 ? string.Join(", ", replaced) : "none"));
        Debug.Log("=== HANDS ANIMATOR " + (ready ? "DONE" : "FAILED"));
        if (Application.isBatchMode) EditorApplication.Exit(ready ? 0 : 1);
    }

    /// <summary>Клипы покоя повторяются по кругу, остальные играют один раз. Список клипов берётся из файла заново: новые клипы из Blender тоже попадут.</summary>
    private static void LoopRestClips()
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(Model);
        // Корректирующие BlendShapes заданы обратной LBS-дельтой в позе покоя: расчёт нормалей по позе покоя
        // (Calculate) давал ложные тёмные заломы 50–66° на всех согнутых фалангах. Режим None сохраняет гладкие нормали!
        importer.importBlendShapeNormals = ModelImporterNormals.None;
        // Плотные допуски сжатия кривых сохраняют плавность 60-кадровых сплайнов Blender без ступенчатой аппроксимации.
        importer.animationCompression = ModelImporterAnimationCompression.Optimal;
        importer.animationRotationError = .08f;
        importer.animationPositionError = .08f;
        importer.animationScaleError = .08f;
        var clips = importer.defaultClipAnimations;
        foreach (var clip in clips)
            clip.loopTime = Loops.Any(name => clip.takeName.EndsWith("|" + name) || clip.takeName == name);
        importer.clipAnimations = clips;
        importer.SaveAndReimport();
    }

    /// <summary>Маска слоя: только кость bone и всё, что под ней. Пути — от корня модели, как их видит Animator.</summary>
    private static AvatarMask Mask(string name, GameObject model, string bone)
    {
        var path = Folder + "/" + name + ".mask";
        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(path);
        if (mask == null)
        {
            mask = new AvatarMask();
            AssetDatabase.CreateAsset(mask, path);
        }
        mask.transformCount = 0;
        mask.AddTransformPath(model.transform, true);
        var active = 0;
        for (var i = 0; i < mask.transformCount; i++)
        {
            var on = ("/" + mask.GetTransformPath(i) + "/").Contains("/" + bone + "/");
            mask.SetTransformActive(i, on);
            if (on) active++;
        }
        EditorUtility.SetDirty(mask);
        Debug.Log("MASK " + name + ": " + active + " of " + mask.transformCount + " transforms");
        return mask;
    }

    /// <summary>Все состояния автомата вместе с вложенными (ветка Gestures).</summary>
    private static System.Collections.Generic.IEnumerable<AnimatorState> States(AnimatorStateMachine machine) =>
        machine.states.Select(s => s.state).Concat(machine.stateMachines.SelectMany(m => States(m.stateMachine)));

    private static AnimatorState State(AnimatorStateMachine machine, string name, Motion motion, Vector3 position)
    {
        var state = machine.AddState(name, position);
        state.motion = motion;
        state.writeDefaultValues = true;
        return state;
    }

    /// <summary>Плавный переход за seconds. С клипом — по его окончании: переход заканчивается ровно на последнем кадре.</summary>
    private static void Blend(AnimatorStateTransition transition, float seconds, AnimationClip finish = null)
    {
        transition.hasFixedDuration = true;
        transition.duration = seconds;
        transition.offset = 0f;
        transition.hasExitTime = finish != null;
        if (finish != null) transition.exitTime = Mathf.Clamp01(1f - seconds / finish.length);
    }
}
