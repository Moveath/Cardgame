using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CardGame.Bots;
using CardGame.Core.Cards;
using CardGame.Core.Games.Durak;
using CardGame.Core.Meta;
using CardGameClient;
using CardGameClient.ThreeD;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

// Партия на 3D-столе в Play Mode: шесть игроков, затем трое. Профиль в памяти, сохранение игрока не трогает.
public static partial class MobileLayoutQa
{
    private const int Qa3DWidth = 1822, Qa3DHeight = 820;
    private static readonly List<string> table3DErrors = new List<string>();
    private static readonly List<string> table3DReport = new List<string>();
    private static readonly SimpleDurakBot table3DPolicy = new SimpleDurakBot(new System.Random(41));
    private static CardGame3DPresenter table3D;
    private static CardTable3DHud table3DHud;
    private static double table3DStarted, table3DWait;
    private static int table3DStage, table3DMoves;
    private static bool table3DPairsSaved, table3DPairsPending, table3DTapDone, table3DBigHandSaved, table3DHandPlaySaved;

    public static void RunTable3D()
    {
        Directory.CreateDirectory(Output);
        PlayerHubRuntime.UseService(new LocalPlayerHubService(new Store(), new Clock()));
        PlayerHubRuntime.LoadAsync().GetAwaiter().GetResult();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("CardTable3D Prototype").AddComponent<CardTable3DBootstrap>();
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
        Application.logMessageReceived += Table3DLog;
        EditorApplication.playModeStateChanged += Table3DEntered;
        EditorApplication.EnterPlaymode();
    }

    private static void Table3DLog(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception) table3DErrors.Add(message + "\n" + stack);
    }

    private static void Table3DEntered(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode) return;
        EditorApplication.playModeStateChanged -= Table3DEntered;
        table3DStarted = EditorApplication.timeSinceStartup;
        EditorApplication.update += Table3DTick;
    }

    private static void Table3DTick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup - table3DStarted > 330) throw new Exception("время вышло, стадия " + table3DStage);
            Table3DStep();
        }
        catch (Exception e) { Table3DFinish(e); }
    }

    private static void Table3DStep()
    {
        var now = EditorApplication.timeSinceStartup;
        if (now < table3DWait) return;
        switch (table3DStage)
        {
            case 0:
                table3D = UnityEngine.Object.FindAnyObjectByType<CardGame3DPresenter>();
                if (table3D == null || table3D.Game == null) return;
                table3DHud = UnityEngine.Object.FindAnyObjectByType<CardTable3DHud>();
                camera = table3D.EyeCamera;
                camera.targetTexture = new RenderTexture(Qa3DWidth, Qa3DHeight, 24);
                table3DHud.Canvas.renderMode = RenderMode.ScreenSpaceCamera;
                table3DHud.Canvas.worldCamera = camera;
                table3DHud.Canvas.planeDistance = .12f;
                table3DStage = 1;
                table3DWait = now + 3;
                return;
            case 1:
                // Первый запуск тяжёлый: раздача может идти дольше паузы, а летящая карта ещё не в руке.
                if (Get<Dictionary<Card, Card3DView>>(table3D, "_cards").Values.Any(view => view.IsFlying)) return;
                capturePrefix = "6p-";
                Save("opening.png");
                CheckTable3DModels();
                CheckTable3DHand();
                CheckTable3DHud();
                SaveLook("look-table.png", 38f, 0f, 0f);
                SaveLook("lean-in.png", 38f, 0f, .22f);
                SaveLook("look-left.png", 32f, -32f, 0f);
                SaveLook("look-right.png", 32f, 32f, 0f);
                SaveLook("look-up.png", 12f, 0f, 0f);
                table3DStage = 20;
                return;
            case 20:
                if (WatchGestures()) return;
                table3DStage = 21;
                return;
            case 21:
                if (WatchPoseTest()) return;
                table3DStage = 22;
                return;
            case 22:
                if (WatchGestureTest()) return;
                table3DStage = 23;
                return;
            case 23:
                if (OwnArms3DView.StaticThumbsUp && WatchThumbsModel()) return;
                Time.timeScale = 7f;
                table3DStage = 2;
                return;
            case 2:
                if (WatchHandPlay()) return;
                Table3DPlay();
                return;
            case 3:
                Save("finished.png");
                CheckFanHand("after the 6-player match");
                Check(GameObjectNamed("ЕЩЁ ПАРТИЯ").activeInHierarchy, "after the match the rematch button is shown");
                table3DReport.Add("Partie 6p: moves=" + table3DMoves);
                Time.timeScale = 1f;
                var settings = HubGameSettings.DefaultFor("durak");
                CardTable3DRoomFlow.BeginQuick(PlayerHubRuntime.Current, 3, settings);
                Call(table3D, "StartNewGame");
                Check(Get<PlayerSeat3DView[]>(table3D, "_seats").Length == 2, "three players seat two opponents");
                capturePrefix = "3p-";
                table3DMoves = 0;
                table3DPairsSaved = true;
                table3DHandPlaySaved = false;
                table3DWait = now + 3;
                table3DStage = 4;
                return;
            case 4:
                CheckTable3DHand();
                CheckTable3DHud();
                Save("opening.png");
                Time.timeScale = 7f;
                table3DStage = 5;
                return;
            case 5:
                if (WatchHandPlay()) return;
                Table3DPlay();
                return;
            default:
                Save("finished.png");
                CheckFanHand("after the 3-player match");
                table3DReport.Add("Partie 3p: moves=" + table3DMoves);
                Table3DFinish(null);
                return;
        }
    }

    /// <summary>Рука с веером держит его, пока в нём есть карты, и лежит на сукне, когда карт нет (bool HoldingCards в HandsAnimator).</summary>
    private static void CheckFanHand(string when)
    {
        var arms = GameObjectNamed("own-arms").GetComponent<OwnArms3DView>();
        var animator = arms.GetComponent<Animator>();
        var holding = arms.FanRoot.childCount > 0;
        var state = animator.GetCurrentAnimatorStateInfo(1);
        var right = holding ? state.IsName("HoldCards") || state.IsName("HoldCards_Regrip") : state.IsName("OnTable");
        Check(animator.GetBool("HoldingCards") == holding && (right || animator.IsInTransition(1)),
            when + ": the fan hand " + (holding ? "holds the fan" : "lies on the cloth without cards") + ", " + arms.FanRoot.childCount + " cards in the fan");
        table3DReport.Add("Fan hand " + when + ": " + (holding ? "holds " + arms.FanRoot.childCount + " cards" : "on the cloth"));
    }

    private static void Table3DPlay()
    {
        var game = table3D.Game;
        if (game.IsFinished)
        {
            Time.timeScale = 1f;
            CheckPhysicalPlay();
            Check(!Get<Dictionary<Card, Card3DView>>(table3D, "_cards").Any(p => game.Players.Skip(1).Any(b => b.Hand.Contains(p.Key)) && !game.Table.Any(s => s.Attack == p.Key)),
                "no hidden card keeps a visible object");
            table3DWait = EditorApplication.timeSinceStartup + .9;
            table3DStage++;
            return;
        }
        if (!table3DPairsSaved && game.Table.Count >= 2 && game.Table.Count(s => s.IsDefended) >= 1)
        {
            table3DPairsSaved = table3DPairsPending = true;
            Time.timeScale = 1f;
            table3DWait = EditorApplication.timeSinceStartup + .6;
            return;
        }
        if (table3DPairsPending)
        {
            table3DPairsPending = false;
            CheckTable3DTable();
            SaveLook("pairs.png", 34f, 0f, 0f);
            Time.timeScale = 7f;
            return;
        }
        if (WatchPoseTestMove()) return;
        if (OwnArms3DView.StaticThumbsUp && WatchThumbsModelMove()) return;
        if (DurakTurnOrder.GetActingPlayer(game) != 0) return;
        if (TryPoseTestMove(game)) return;
        if (OwnArms3DView.StaticThumbsUp && TryThumbsModelMove(game)) return;
        var held = Get<Dictionary<Card, Card3DView>>(table3D, "_cards");
        if (!table3DBigHandSaved && game.Players[0].Hand.Count >= 10 && game.Players[0].Hand.All(card => !held[card].IsFlying))
        {
            table3DBigHandSaved = true;
            CheckTable3DHand();
            Save("big-hand.png");
        }
        if (!table3DTapDone && TryTable3DTap(game)) return;
        var move = table3DPolicy.ChooseMove(game, 0);
        if (!move.HasValue) return;
        table3DMoves++;
        Call(table3D, "ApplyHumanMove", move.Value);
    }

    /// <summary>Настоящее нажатие по карте: луч из точки экрана должен попасть в неё и сыграть ход.</summary>
    private static bool TryTable3DTap(DurakGame game)
    {
        var cards = Get<Dictionary<Card, Card3DView>>(table3D, "_cards");
        var legal = game.GetLegalMoves(0).Where(m => m.Type == DurakMoveType.Attack).ToArray();
        if (legal.Length == 0 || legal.Count(m => m.Card == legal[0].Card) != 1) return false;
        var view = cards[legal[0].Card];
        if (view.IsFlying) return true;
        var hand = game.Players[0].Hand.Count;
        var point = camera.WorldToScreenPoint(view.transform.TransformPoint(Table3DTapPoint));
        Physics.SyncTransforms();
        var struck = Physics.Raycast(camera.ScreenPointToRay(point), out var hit, 10f) ? hit.collider.name : "nothing";
        Call(table3D, "OnScreenTapped", (Vector2)point);
        table3DTapDone = true;
        Check(game.Players[0].Hand.Count == hand - 1, "a tap on a fan card plays it: aimed at " + view.name + ", the ray hit " + struck + ", " + hand + " cards in hand");
        // Сыгранная карта ещё над веером: следующее нажатие в ту же точку не должно достаться ей.
        Check(!ReferenceEquals(Call(table3D, "CardUnder", (Vector2)point), view), "a card flying off the fan does not catch the next tap");
        return true;
    }

    private static void CheckTable3DModels()
    {
        var size = Bounds(GameObjectNamed("table")).size;
        Check(Mathf.Abs(size.x - 1.84f) < .02f && Mathf.Abs(size.z - 1.30f) < .02f, "table is 1.84 by 1.30 metres, got " + size);
        var top = Bounds(GameObjectNamed("table")).max.y;
        Check(Mathf.Abs(top - .775f) < .01f, "table stands at table height, got " + top);
        var chair = GameObjectNamed("chair");
        Check(Bounds(chair).size.y > .9f && Bounds(chair).size.y < 1.03f, "chair is about a metre tall, got " + Bounds(chair).size.y);
        var frustum = GeometryUtility.CalculateFrustumPlanes(camera);
        var arms = GameObjectNamed("own-arms").GetComponentsInChildren<SkinnedMeshRenderer>();
        Check(arms.Length == 4 && arms.All(arm => GeometryUtility.TestPlanesAABB(frustum, arm.bounds)), "both own arms and sleeves are in the frame, got " + arms.Length);
        // Руками управляет Animator Controller HandsAnimator: позы пальцев только из клипов Blender, переходы 0,15–0,2 с,
        // скриптов, которые сами крутят кости пальцев, на руках нет. Веер висит на кости веера той руки, что его держит (FanSide).
        // На руки светит свой заполняющий свет, иначе сторона кисти к игроку тёмная; на вершину 4 кости.
        var ownArms = GameObjectNamed("own-arms");
        var animator = ownArms.GetComponent<Animator>();
        var controller = animator.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
        Check(controller != null && controller.name == "HandsAnimator", "the arms run the HandsAnimator controller, got " + animator.runtimeAnimatorController);
        Check(controller.layers.Select(l => l.name).SequenceEqual(new[] { "Base", "FanHand", "Pinch", "PoseTest", "GestureTest" }),
            "the hands animator has layers Base, FanHand, Pinch, PoseTest, GestureTest, got " + string.Join(", ", controller.layers.Select(l => l.name)));
        // Слой проверки базовых поз (клавиша F) без маски, поверх всех, вес 0: в обычной игре он ничего не меняет.
        var testLayer = controller.layers[3];
        var clenchState = testLayer.stateMachine.defaultState;
        Check(testLayer.avatarMask == null && testLayer.blendingMode == UnityEditor.Animations.AnimatorLayerBlendingMode.Override && animator.GetLayerWeight(3) == 0f
            && clenchState != null && clenchState.name == "Clench" && clenchState.timeParameterActive && clenchState.timeParameter == "TestClench"
            && clenchState.motion != null && clenchState.motion.name.EndsWith("Take_TestClench") && !((AnimationClip)clenchState.motion).isLooping,
            "the PoseTest layer plays Take_TestClench by the TestClench parameter and weighs 0 in play, got " + (clenchState ? clenchState.name + "=" + clenchState.motion : "no state")
            + ", weight " + animator.GetLayerWeight(3));
        var parameters = animator.parameters.Select(p => p.name + ":" + p.type).ToArray();
        foreach (var expected in new[] { "HoldingCards:Bool", "ThumbUp:Trigger", "ThumbDown:Trigger", "Point:Trigger", "Knock:Trigger", "Shrug:Trigger", "Fist:Trigger", "Regrip:Trigger", "TestClench:Float",
                     "TestGesture:Int", "TestGestureTime:Float" })
            Check(parameters.Contains(expected), "the hands animator has parameter " + expected + ", got " + string.Join(", ", parameters));
        // Слой проверки новых жестов (клавиши U, J, H): без маски, поверх PoseTest, вес 0. Состояние на жест и руки (обе, правая,
        // левая) играет клип Take_Test<жест><руки> по времени TestGestureTime.
        var gestureLayer = controller.layers[4];
        var gestureStates = gestureLayer.stateMachine.states.Select(s => s.state).ToArray();
        var gestureNames = OwnArms3DView.TestGestures.SelectMany(g => OwnArms3DView.TestHandsSuffix.Select(h => g + h)).ToArray();
        Check(gestureLayer.avatarMask == null && gestureLayer.blendingMode == UnityEditor.Animations.AnimatorLayerBlendingMode.Override && animator.GetLayerWeight(4) == 0f
            && gestureStates.Select(s => s.name).SequenceEqual(gestureNames)
            && gestureStates.All(s => s.timeParameterActive && s.timeParameter == "TestGestureTime" && s.motion is AnimationClip clip
                && clip.name.EndsWith("Take_Test" + s.name) && !clip.isLooping),
            "the GestureTest layer plays Take_Test<gesture><hands> clips by TestGestureTime and weighs 0 in play, got "
            + string.Join(", ", gestureStates.Select(s => s.name + "=" + (s.motion ? s.motion.name : "none") + "@" + s.timeParameter)));
        // Жесты лежат веткой Gestures (вложенный автомат слоя Base), остальные состояния — в корне слоёв.
        var gestureBranch = controller.layers[0].stateMachine.stateMachines.Select(m => m.stateMachine).FirstOrDefault(m => m.name == "Gestures");
        Check(gestureBranch != null && gestureBranch.states.Length == 6 && gestureBranch.states.All(s => s.state.name.StartsWith("Gesture_")),
            "the Base layer keeps the six gestures in the Gestures branch, got " + (gestureBranch == null ? "no branch" : string.Join(", ", gestureBranch.states.Select(s => s.state.name))));
        var handStates = controller.layers.SelectMany(l => l.stateMachine.states.Concat(l.stateMachine.stateMachines.SelectMany(m => m.stateMachine.states))).Select(s => s.state).ToArray();
        Check(handStates.All(s => s.motion != null) && new[] { "Idle", "HoldCards", "OnTable", "PlayCard", "Gesture_ThumbUp", "Gesture_ThumbDown", "Gesture_Point", "Gesture_Knock", "Gesture_Shrug", "Gesture_Fist", "Clench", "ThumbsUp", "ThumbsDown" }.All(n => handStates.Any(s => s.name == n)),
            "every hands state plays a Blender clip: " + string.Join(", ", handStates.Select(s => s.name + "=" + (s.motion ? s.motion.name : "none"))));
        var handTransitions = controller.layers.SelectMany(l => l.stateMachine.anyStateTransitions).Concat(handStates.SelectMany(s => s.transitions)).ToArray();
        Check(handTransitions.Length > 0 && handTransitions.All(t => t.hasFixedDuration && t.duration >= .15f - 1e-4f && t.duration <= .2f + 1e-4f),
            "every hands transition blends in 0.15-0.2 s, got " + string.Join(", ", handTransitions.Select(t => t.duration.ToString("0.00"))));
        var fanState = animator.GetCurrentAnimatorStateInfo(1);
        Check(animator.GetBool("HoldingCards") && (fanState.IsName("HoldCards") || fanState.IsName("HoldCards_Regrip")), "the fan hand holds the fan while there are cards");
        var scripts = ownArms.GetComponentsInChildren<MonoBehaviour>(true).Where(m => !(m is Card3DView)).Select(m => m.GetType().Name).Distinct().ToArray();
        Check(scripts.SequenceEqual(new[] { "OwnArms3DView" }), "no script but the arms view sits on the arms, finger poses come only from clips, got " + string.Join(", ", scripts));
        Check(GameObjectNamed("Fan").transform.parent.name == "SocketFan" + OwnArms3DView.FanSide, "the fan hangs on the SocketFan" + OwnArms3DView.FanSide + " bone of the fan hand");
        var fill = GameObjectNamed("Arms fill light")?.GetComponent<Light>();
        Check(fill != null && fill.type == LightType.Directional && fill.shadows == LightShadows.None && arms.All(arm => (arm.renderingLayerMask & 2u) != 0),
            "the arms have their own unshadowed fill light from the eyes");
        Check(QualitySettings.skinWeights == SkinWeights.FourBones, "skinning blends four bones per vertex, got " + QualitySettings.skinWeights);
        var thumb = camera.WorldToViewportPoint(GameObjectNamed("Thumb03" + OwnArms3DView.FanSide).transform.position);
        var fan = camera.WorldToViewportPoint(GameObjectNamed("Hand" + OwnArms3DView.FanSide).transform.position);
        Check(thumb.y > 0f && FanHalf(thumb.x, .6f), "the holding pose is playing: the fan thumb lies on the fan, got " + thumb + " wrist " + fan);
        // Свободная кисть лежит на сукне: подушечки пальцев у ткани, а не над ней и не в ней.
        foreach (var tip in new[] { "TipIndex" + OwnArms3DView.FreeSide, "TipMiddle" + OwnArms3DView.FreeSide })
        {
            var above = GameObjectNamed(tip).transform.position.y - .76f;
            Check(above > -.006f && above < .03f, "the free hand rests on the cloth: " + tip + " is " + above.ToString("0.000") + " m above it");
        }
        // Кисть лежит плашмя всеми пальцами: дальний сустав каждого пальца и запястье — над тканью на толщину пальца, не в столе.
        // Раньше мерились только указательный и средний, а безымянный с мизинцем уходили в стол на 2–3 см.
        foreach (var joint in new[] { "Thumb03", "Index03", "Middle03", "Ring03", "Pinky03", "Pinky02", "Hand" })
        {
            var above = GameObjectNamed(joint + OwnArms3DView.FreeSide).transform.position.y - .76f;
            Check(above > .003f && above < .05f, "the free hand lies flat on the cloth: " + joint + " is " + above.ToString("0.000") + " m above it");
        }
        CheckArmSkin(arms);
        CheckFistSmoothClips(arms, controller);
        CheckCardMaterialsSurvive();
        CheckCardBodies();
        var seats = Get<PlayerSeat3DView[]>(table3D, "_seats");
        Check(seats.Length == 5, "five opponents are seated");
        // Камера смотрит на стол, как в Liar's Bar: головы соперников у верхнего края, а таблички с именами и ходом в кадре.
        foreach (var seat in seats)
        {
            var label = camera.WorldToViewportPoint(seat.LabelPoint);
            Check(label.z > 0 && label.x > 0f && label.x < 1f && label.y > .4f && label.y < 1f, "opponent label is in the opening frame, got " + label);
        }
    }

    private static void CheckTable3DHand()
    {
        var cards = Get<Dictionary<Card, Card3DView>>(table3D, "_cards");
        var hand = table3D.Game.Players[0].Hand;
        Check(hand.Count > 0, "the player holds cards");
        foreach (var card in hand)
        {
            var view = cards[card];
            Check(view.transform.IsChildOf(GameObjectNamed("Seated body").transform) && view.transform.parent.name == "Fan", "a held card sits in the fan of the fan hand on the seated body");
            var toCard = (view.transform.position - camera.transform.position).normalized;
            var facing = Vector3.Dot(view.transform.forward, toCard);
            Check(facing > .6f, "a held card faces the player, got " + facing + " at " + view.transform.localPosition + " angles " + view.transform.localEulerAngles);
            var top = camera.WorldToViewportPoint(view.transform.TransformPoint(new Vector3(0f, Table3DLayout3DHalfHeight, 0f)));
            Check(top.x > 0 && top.x < 1 && top.y > 0 && top.y < 1, "the rank corner of a held card is on screen, got " + top);
            var corner = (Vector2)camera.WorldToScreenPoint(view.transform.TransformPoint(Table3DTapPoint));
            Check(ReferenceEquals(Call(table3D, "CardUnder", corner), view), "every held card can be tapped: " + view.name + " of " + hand.Count);
        }
    }

    /// <summary>
    /// Кожа рук — сглаженная молодая (ArmSkin_Smooth, принята автором 2026-11-16): карты T_Hand_Male_Smooth_*, гладкость 0,34,
    /// мягкость рельефа 0,55, остальное как у ArmSkin. Прежний ArmSkin с картами Young лежит рядом без изменений — для отката.
    /// </summary>
    /// <summary>
    /// Корректирующие формы кулака: FistSmooth (scripts/3d/hands/fist_smooth.py, 2026-11-17) скругляет кожу у суставов
    /// в кулаке, FistThumb (fist_thumb.py, 2026-11-20) — кожу у большого пальца, лежащего сбоку от указательного.
    /// Каждая есть у обеих мужских рук, их вес ведёт только клип проверки Take_TestClench: 0 в Idle, 100 в кулаке. Во всех
    /// остальных клипах рук вес 0, поэтому партия, веер и прежние жесты кожу не меняют. У модели без формы — запись в отчёт.
    /// </summary>
    private static readonly string[] FistShapes = { "FistSmooth", "FistThumb" };
    // Корректирующая форма кисти «класса» и «фу» (scripts/3d/hands/gesture_smooth.py, 2026-11-20): её ведут только клипы
    // проверки новых жестов, 0 в Idle, 100 в жесте. С 2026-11-23 у «класса» (по модели автора) своя форма ThumbsUpSmooth:
    // её ведут клипы Take_TestThumbsUp*, а ThumbsSmooth — только «фу» (Take_TestThumbsDown*). В модели без ThumbsUpSmooth
    // ThumbsSmooth ведут оба жеста, как раньше.
    private const string ThumbsShape = "ThumbsSmooth", ThumbsUpShape = "ThumbsUpSmooth";
    private static readonly string[] ThumbsClips = { "Take_TestThumbsUp", "Take_TestThumbsDown" };

    private static void CheckFistSmoothClips(SkinnedMeshRenderer[] parts, UnityEditor.Animations.AnimatorController controller)
    {
        var male = parts.Where(p => p.name.StartsWith("Arm_Male")).ToArray();
        var expected = new List<string>();
        var ownUp = male.Length > 0 && male.All(p => p.sharedMesh.GetBlendShapeIndex(ThumbsUpShape) >= 0);
        foreach (var shape in FistShapes.Concat(new[] { ThumbsShape, ThumbsUpShape }))
        {
            var have = male.Count(p => p.sharedMesh.GetBlendShapeIndex(shape) >= 0);
            if (have == 0)
            {
                table3DReport.Add(shape + ": this model has no such corrective shape");
                continue;
            }
            var thumbs = shape == ThumbsShape || shape == ThumbsUpShape;
            if (shape == ThumbsShape) thumbsShapeExpected = true;
            else if (shape == ThumbsUpShape) thumbsUpShapeExpected = true;
            else expected.Add(shape);
            var drivers = shape == ThumbsUpShape ? new[] { "Take_TestThumbsUp" }
                : shape == ThumbsShape ? (ownUp ? new[] { "Take_TestThumbsDown" } : ThumbsClips) : new[] { "Take_TestClench" };
            Check(have == 2 && male.Length == 2, "both male arms carry the " + shape + " corrective shape, got " + have + " of " + male.Length);
            var others = 0;
            foreach (var clip in controller.animationClips.Distinct())
            {
                var bindings = AnimationUtility.GetCurveBindings(clip).Where(b => b.propertyName == "blendShape." + shape).ToArray();
                var curves = bindings.Select(b => AnimationUtility.GetEditorCurve(clip, b)).ToArray();
                // Клипы жеста одной рукой (Take_TestThumbsUp_R, _L): форму ведёт только эта рука, у второй вес 0.
                var only = thumbs ? new[] { "_R", "_L" }.FirstOrDefault(h => drivers.Any(d => clip.name.EndsWith(d + h))) : null;
                if (only != null)
                {
                    var moving = Enumerable.Range(0, bindings.Length).Where(i => bindings[i].path.EndsWith(only)).ToArray();
                    Check(bindings.Length == 2 && moving.Length == 1
                        && Mathf.Abs(curves[moving[0]].Evaluate(0f)) < .01f && Mathf.Abs(curves[moving[0]].Evaluate(clip.length) - 100f) < .01f
                        && curves[1 - moving[0]].keys.All(k => Mathf.Abs(k.value) < 1e-3f),
                        clip.name + " drives " + shape + " 0->100 only on the " + only + " arm and keeps the other at 0, paths "
                        + string.Join(", ", bindings.Select(b => b.path)));
                    continue;
                }
                if (drivers.Any(d => clip.name.EndsWith(d)))
                {
                    var ends = curves.Select(c => c.Evaluate(0f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "->"
                        + c.Evaluate(clip.length).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                    Check(curves.Length == 2 && curves.All(c => Mathf.Abs(c.Evaluate(0f)) < .01f && Mathf.Abs(c.Evaluate(clip.length) - 100f) < .01f),
                        clip.name + " drives " + shape + " on both arms from 0 (idle) to 100, got " + string.Join(", ", ends));
                    continue;
                }
                others++;
                Check(curves.All(c => c.keys.All(k => Mathf.Abs(k.value) < 1e-3f)), clip.name + " keeps " + shape + " at 0");
            }
            table3DReport.Add(shape + ": both male arms carry it, " + string.Join(" and ", drivers) + " drive it 0->100, " + others + " other clips keep it at 0");
        }
        fistShapesExpected = expected.ToArray();
    }

    /// <summary>
    /// Вес форм кисти «класса» и «фу» на каждой мужской руке сейчас, 0–100: форма жеста gesture (ThumbsUp — ThumbsUpSmooth,
    /// если она есть в модели, ThumbsDown — ThumbsSmooth) весит left на левой и right на правой руке, другая форма — 0;
    /// gesture null — обе формы 0.
    /// </summary>
    private static void CheckThumbsShape(string what, string gesture, float left, float right)
    {
        if (!thumbsShapeExpected) return;
        var active = gesture == null ? null : gesture == "ThumbsUp" && thumbsUpShapeExpected ? ThumbsUpShape : ThumbsShape;
        foreach (var shape in new[] { ThumbsShape, ThumbsUpShape })
        {
            if (shape == ThumbsUpShape && !thumbsUpShapeExpected) continue;
            foreach (var part in Arms.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(p => p.name.StartsWith("Arm_Male")))
            {
                var expected = shape != active ? 0f : part.name.EndsWith("_L") ? left : right;
                var weight = part.GetBlendShapeWeight(part.sharedMesh.GetBlendShapeIndex(shape));
                Check(Mathf.Abs(weight - expected) < .5f, what + ": " + shape + " on " + part.name + " weighs " + expected + ", got "
                    + weight.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
            }
        }
    }

    /// <summary>Вес форм кулака (FistSmooth, FistThumb) на обеих мужских руках сейчас, 0–100.</summary>
    private static void CheckFistSmoothWeight(string what, float expected)
    {
        foreach (var shape in fistShapesExpected)
        {
            var weights = Arms.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(p => p.name.StartsWith("Arm_Male"))
                .Select(p => p.GetBlendShapeWeight(p.sharedMesh.GetBlendShapeIndex(shape))).ToArray();
            Check(weights.Length == 2 && weights.All(w => Mathf.Abs(w - expected) < .5f), what + ": " + shape + " weighs " + expected + " on both arms, got "
                + string.Join(", ", weights.Select(w => w.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))));
        }
    }

    private static void CheckArmSkin(SkinnedMeshRenderer[] parts)
    {
        var tripoMap = Resources.Load<Texture2D>("ThreeD/Hands/TripoHands_BaseColor");
        var sweaterMap = Resources.Load<Texture2D>("ThreeD/Hands/TripoSweater_Tile");
        if (tripoMap != null)
        {
            foreach (var part in parts.Where(p => p.name.StartsWith("Arm_Male") || p.name.StartsWith("Sleeve")))
            {
                var expected = part.name.StartsWith("Sleeve") && sweaterMap != null ? sweaterMap : tripoMap;
                Check(part.sharedMaterial != null && part.sharedMaterial.GetTexture("_BaseMap") == expected,
                    part.name + " wears " + expected.name + ", got " + (part.sharedMaterial ? part.sharedMaterial.name : "none"));
            }
            table3DReport.Add("Skin: TripoHands_BaseColor (" + tripoMap.width + "x" + tripoMap.height + ")");
            return;
        }
        var skin = Resources.Load<Material>("ThreeD/Materials/ArmSkin_Smooth");
        var old = Resources.Load<Material>("ThreeD/Materials/ArmSkin");
        Check(skin != null && skin.shader.name == "CardCity/Skin" && skin.shader.isSupported, "the smooth skin material exists on the CardCity/Skin shader");
        foreach (var part in parts.Where(p => p.name.StartsWith("Arm_Male")))
            Check(part.sharedMaterial == skin, part.name + " wears ArmSkin_Smooth, got " + (part.sharedMaterial ? part.sharedMaterial.name : "none"));
        Check(skin.GetTexture("_BaseMap")?.name == "T_Hand_Male_Smooth_BaseColor" && skin.GetTexture("_BumpMap")?.name == "T_Hand_Male_Smooth_Normal"
            && skin.GetTexture("_NailMask") == old.GetTexture("_NailMask") && skin.IsKeywordEnabled("_NORMALMAP"),
            "the smooth skin uses the smooth colour and relief maps and the same nail mask");
        Check(Mathf.Approximately(skin.GetFloat("_Smoothness"), .34f) && Mathf.Approximately(skin.GetFloat("_DiffuseSoftness"), .55f),
            "the smooth skin has smoothness 0.34 and diffuse softness 0.55, got " + skin.GetFloat("_Smoothness") + " / " + skin.GetFloat("_DiffuseSoftness"));
        var differs = new List<string>();
        for (var i = 0; i < skin.shader.GetPropertyCount(); i++)
        {
            var name = skin.shader.GetPropertyName(i);
            switch (skin.shader.GetPropertyType(i))
            {
                case UnityEngine.Rendering.ShaderPropertyType.Float:
                case UnityEngine.Rendering.ShaderPropertyType.Range:
                    if (!Mathf.Approximately(skin.GetFloat(name), old.GetFloat(name))) differs.Add(name);
                    break;
                case UnityEngine.Rendering.ShaderPropertyType.Color:
                    if (skin.GetColor(name) != old.GetColor(name)) differs.Add(name);
                    break;
                case UnityEngine.Rendering.ShaderPropertyType.Texture:
                    if (skin.GetTexture(name) != old.GetTexture(name)) differs.Add(name);
                    break;
            }
        }
        Check(differs.OrderBy(n => n).SequenceEqual(new[] { "_BaseMap", "_BumpMap", "_DiffuseSoftness", "_Smoothness" }),
            "the smooth skin differs from ArmSkin only in the maps, smoothness and diffuse softness, got " + string.Join(", ", differs));
        Check(old.GetTexture("_BaseMap")?.name == "T_Hand_Male_Young_BaseColor" && Mathf.Approximately(old.GetFloat("_Smoothness"), .3f),
            "the previous ArmSkin is kept unchanged for a rollback");
        table3DReport.Add("Skin: " + skin.name + " (" + skin.GetTexture("_BaseMap").name + ", " + skin.GetTexture("_BumpMap").name + ")");
    }

    /// <summary>Play запускается без перезагрузки скриптов: удалённый материал из общей таблицы не должен дать розовую карту.</summary>
    private static void CheckCardMaterialsSurvive()
    {
        var make = typeof(Card3DView).GetMethod("MaterialFor", BindingFlags.NonPublic | BindingFlags.Static);
        var probe = new Texture2D(2, 2) { name = "qa probe" };
        var first = (Material)make.Invoke(null, new object[] { probe });
        UnityEngine.Object.DestroyImmediate(first);
        var made = (Material)make.Invoke(null, new object[] { probe });
        Check(made != null, "a card material destroyed between Play runs is made again");
        var materials = (Dictionary<Texture, Material>)typeof(Card3DView).GetField("Materials", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        materials.Remove(probe);
        UnityEngine.Object.DestroyImmediate(made);
        UnityEngine.Object.DestroyImmediate(probe);
    }

    /// <summary>
    /// Правая рука несёт сыгранную карту: снимок в этот момент и проверка, что карта зажата между подушечками большого
    /// и указательного пальцев, а не проходит сквозь них. Пока рука в движении, время идёт с обычной скоростью.
    /// </summary>
    private static bool WatchHandPlay()
    {
        if (table3DHandPlaySaved) return false;
        var arms = GameObjectNamed("own-arms").GetComponent<OwnArms3DView>();
        if (!arms.Busy)
        {
            if (Time.timeScale < 7f) Time.timeScale = 7f;
            return false;
        }
        Time.timeScale = 1f;
        var card = Get<Card3DView>(arms, "_card");
        if (card == null || Get<int>(arms, "_stage") != 2 || Get<float>(arms, "_time") < .06f) return true;
        var thumb = GameObjectNamed("TipThumb" + OwnArms3DView.FreeSide).transform.position;
        var index = GameObjectNamed("TipIndex" + OwnArms3DView.FreeSide).transform.position;
        var plane = new Plane(card.transform.forward, card.transform.position);
        float thumbSide = plane.GetDistanceToPoint(thumb), indexSide = plane.GetDistanceToPoint(index);
        var half = CardMesh3DThickness * card.transform.lossyScale.z / 2;
        Check(thumbSide * indexSide < 0f && Mathf.Abs(thumbSide) > half && Mathf.Abs(indexSide) > half,
            "the carried card sits between thumb and index, not through them: " + thumbSide.ToString("0.0000") + " / " + indexSide.ToString("0.0000")
            + "; card " + card.name + " parent " + card.transform.parent.name + " at " + card.transform.position.ToString("0.000")
            + ", pinch " + GameObjectNamed("Pinch").transform.position.ToString("0.000") + ", thumb " + thumb.ToString("0.000") + ", index " + index.ToString("0.000")
            + ", weight " + Get<float>(arms, "_pinchWeight") + ", stage time " + Get<float>(arms, "_time").ToString("0.00")
            + ", forearm " + GameObjectNamed("Forearm" + OwnArms3DView.FreeSide).transform.position.ToString("0.000"));
        Check(card.transform.IsChildOf(GameObjectNamed("Hand" + OwnArms3DView.FreeSide).transform), "the carried card moves with the free hand");
        var body = GameObjectNamed("Seated body").transform;
        var fan = body.InverseTransformPoint(arms.FanRoot.position);
        var layout = typeof(Card3DView).Assembly.GetType("CardGameClient.ThreeD.Table3DLayout");
        var pivot = (Vector3)layout.GetField("HandPivot").GetValue(null);
        var tilt = (float)layout.GetField("HandTilt").GetValue(null);
        var yaw = (float)layout.GetField("HandYaw").GetValue(null);
        var fanTurn = Quaternion.Angle(Quaternion.Inverse(body.rotation) * arms.FanRoot.rotation, Quaternion.Euler(tilt, yaw, 0f));
        var fanHand = body.InverseTransformPoint(GameObjectNamed("Hand" + OwnArms3DView.FanSide).transform.position);
        Check((fan - pivot).magnitude < .006f && fanTurn < 2f, "the fan hand keeps the fan still while the free one plays, got " + fan.ToString("0.000")
            + ", turned by " + fanTurn.ToString("0.0") + " degrees, fan wrist at " + fanHand.ToString("0.000")
            + ", first fan card " + arms.FanRoot.GetComponentsInChildren<Card3DView>().Select(c => c.transform.localPosition.ToString("0.000") + " flying " + c.IsFlying).FirstOrDefault());
        Save("hand-play.png");
        table3DHandPlaySaved = true;
        Time.timeScale = 7f;
        return false;
    }

    private const float CardMesh3DThickness = .0006f;

    /// <summary>Карта объёмная и с телом для физики, стол и руки с коллайдерами.</summary>
    private static void CheckCardBodies()
    {
        var cards = Get<Dictionary<Card, Card3DView>>(table3D, "_cards").Values.ToArray();
        Check(cards.All(c => c.GetComponent<Rigidbody>() != null && c.GetComponent<BoxCollider>() != null), "every game card has a body and a collider");
        Check(cards.All(c => c.GetComponent<Rigidbody>().isKinematic), "resting cards are kinematic: physics only works while a card falls");
        var mesh = cards[0].GetComponent<MeshFilter>().sharedMesh;
        Check(mesh.subMeshCount == 3 && mesh.bounds.size.z > .0005f, "a card is a solid with face, back and edge, depth " + mesh.bounds.size.z);
        Check(GameObjectNamed("Table surface").GetComponent<BoxCollider>() != null, "the cloth has a collider");
        var arms = GameObjectNamed("own-arms").GetComponent<OwnArms3DView>();
        Check(arms.Colliders.Count == 8 && arms.Colliders.All(c => c.bounds.size.magnitude > .01f && c.bounds.size.magnitude < .2f),
            "each hand has palm and fingertip colliders of hand size, got " + string.Join(", ", arms.Colliders.Select(c => c.bounds.size.ToString("0.000"))));
    }

    /// <summary>После партии: карты падали физикой, правая рука играла сама, и все карты остановились точно на местах.</summary>
    private static void CheckPhysicalPlay()
    {
        Check(Card3DView.Landings > 0, "cards fell onto the table by physics: " + Card3DView.Landings);
        Check(OwnArms3DView.Played > 0, "the free hand played cards itself: " + OwnArms3DView.Played);
        var table = table3D.Game.Table;
        var cards = Get<Dictionary<Card, Card3DView>>(table3D, "_cards");
        foreach (var slot in table)
        {
            var view = cards[slot.Attack];
            if (view.IsFlying) continue;
            Check(view.GetComponent<Rigidbody>().isKinematic, "a settled card is back under the game's control");
        }
    }

    private const float Table3DLayout3DHalfHeight = .044f;
    private static readonly Vector3 Table3DTapPoint = new Vector3(-.024f, .036f, 0f); // У левого верхнего угла: остальное закрыто соседней картой.

    private static void CheckTable3DTable()
    {
        var cards = Get<Dictionary<Card, Card3DView>>(table3D, "_cards");
        foreach (var slot in table3D.Game.Table)
        {
            var view = cards[slot.Attack];
            if (view.IsFlying) continue;
            Check(view.transform.forward.y < -.9f, "a played card lies face up");
            var at = camera.WorldToViewportPoint(view.transform.position);
            Check(at.x > 0 && at.x < 1 && at.y > 0 && at.y < 1, "a played card is in the frame, got " + at);
        }
    }

    private static void CheckTable3DHud()
    {
        var labels = table3DHud.Canvas.GetComponentsInChildren<Text>();
        Check(labels.Any(t => t.text == "КОЗЫРЬ"), "trump is labelled");
        Check(!labels.Any(t => t.text.Any(char.IsDigit)), "the table shows no numbers");
    }

    private static void SaveLook(string name, float pitch, float yaw, float zoom)
    {
        var look = camera.GetComponent<TableCameraController>();
        AimAt(look, pitch, yaw, zoom);
        // Кадр снимается до проверок: если проверка не пройдёт, по нему видно, что не так.
        Save(name);
        // Тело поворачивается за взглядом: веер остаётся в нижней части кадра, а срезы рукавов у локтей — за его краем.
        // При наклоне к столу голова опускается, а руки нет, поэтому веер в кадре выше: ему можно до двух третей высоты.
        // Мерится точка в 10 см под веером, где кисть уходит в рукав, а не запястье: где стоит запястье, зависит от хвата
        // (в хвате ребром к игроку оно на 3,5 см выше, чем в прежнем), а веер от хвата не сдвигается.
        if (pitch > 20f)
        {
            var fanRoot = GameObjectNamed("Fan").transform;
            var fan = camera.WorldToViewportPoint(fanRoot.position - fanRoot.up * .1f);
            var top = zoom > 0f ? .67f : .5f;
            Check(fan.z > 0f && FanHalf(fan.x, .75f) && fan.y > -.2f && fan.y < top, "the fan stays low in the frame: " + name + " point under the fan at " + fan);
        }
        foreach (var side in new[] { "_L", "_R" })
        {
            // Рукав продолжается плечом до тела: в кадре не должно быть его конца у плеча.
            var upper = GameObjectNamed("Upper arm" + side).transform;
            var shoulder = camera.WorldToViewportPoint(upper.position + upper.up * upper.localScale.y);
            Check(shoulder.z < 0f || shoulder.x < 0f || shoulder.x > 1f || shoulder.y < 0f || shoulder.y > 1f, "the sleeve ends off screen: " + name + " shoulder" + side + " at " + shoulder);
        }
        AimAt(look, (float)typeof(TableCameraController).GetField("StartPitch", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null), 0f, 0f);
    }

    /// <summary>Точка кадра (по X, 0–1) на стороне веера: при веере слева — от 0 до limit, при веере справа — от 1 - limit до 1.</summary>
    private static bool FanHalf(float x, float limit) =>
        OwnArms3DView.FanSide == "_L" ? x > 0f && x < limit : x > 1f - limit && x < 1f;

    private static int gestureIndex = -1;
    private static float gesturePeak, gestureStarted;
    private static bool gestureMidSaved;
    private static Vector3 gestureRest;

    /// <summary>Каждый жест по очереди: начинается, заметно двигает свободную кисть и возвращает её в покой. Кадр «КЛАСС» снимается в разгаре.</summary>
    private static bool WatchGestures()
    {
        var arms = GameObjectNamed("own-arms").GetComponent<OwnArms3DView>();
        var hand = GameObjectNamed("Hand" + OwnArms3DView.FreeSide).transform;
        var body = GameObjectNamed("Seated body").transform;
        var gestures = OwnArms3DView.Gestures;
        if (gestureIndex < 0)
        {
            gestureIndex = 0;
            gestureRest = body.InverseTransformPoint(hand.position);
            Time.timeScale = 2f;
        }
        var moved = (body.InverseTransformPoint(hand.position) - gestureRest).magnitude;
        if (arms.Gesture != null)
        {
            gesturePeak = Mathf.Max(gesturePeak, moved);
            // «Класс» из меню «ЖЕСТЫ»: при StaticThumbsUp = false — живая анимация Take_ThumbsUp в кадре (снимаем подъём и пик!).
            if (!OwnArms3DView.StaticThumbsUp && arms.Gesture == "ThumbsUp" && gestureStarted > 0f && !gestureMidSaved && Time.time - gestureStarted > .42f)
            {
                gestureMidSaved = true;
                Save("gesture-menu-thumbsup-mid.png");
            }
            var thumbsReady = OwnArms3DView.StaticThumbsUp ? arms.ThumbsModelShown : Time.time - gestureStarted > .82f;
            if (arms.Gesture == "ThumbsUp" && gestureStarted > 0f && thumbsReady)
            {
                gestureStarted = 0f;
                Save("gesture-menu-thumbsup.png");
                if (OwnArms3DView.StaticThumbsUp) CheckThumbsModelInCorner("menu thumbs up");
            }
            return true;
        }
        if (gestureIndex > 0)
        {
            var done = gestures[gestureIndex - 1];
            if (done == "ThumbsUp" && OwnArms3DView.StaticThumbsUp)
            {
                Check(gestureStarted == 0f, "the menu thumbs-up model reached the corner of the frame");
                CheckThumbsModelGone("after the menu thumbs up");
            }
            // Стук — мелкий жест: кисть приподнимается на пару сантиметров, остальные жесты поднимают руку со стола.
            Check(gesturePeak > (done == "TapTable" ? .015f : .04f), "gesture " + done + " moves the free hand: up to " + gesturePeak.ToString("0.000") + " m");
            Check(moved < .012f, "after " + done + " the free hand is back at rest: " + moved.ToString("0.000") + " m away");
        }
        if (gestureIndex >= gestures.Length)
        {
            Check(OwnArms3DView.GesturesDone >= gestures.Length, "every gesture played to the end: " + OwnArms3DView.GesturesDone);
            return false;
        }
        if (arms.Busy) return true;
        Check(arms.PlayGesture(gestures[gestureIndex]), "gesture starts: " + gestures[gestureIndex]);
        gesturePeak = 0f;
        gestureStarted = Time.time;
        gestureIndex++;
        return true;
    }

    // --- Проверка базовых поз новой системы рук: клавиша F (OwnArms3DView, слой PoseTest) ---------------------------------

    [Serializable] private class PoseTestMark { public int frame; public string side; public string bone; public float mm; }
    [Serializable] private class PoseTestFile { public string clip; public int frames; public float fps; public PoseTestMark[] marks; }

    private static int poseTestStep, poseTestPressFrame = -1;
    private static float poseTestSince, poseTestClenchAtG;
    private static bool poseTestKeyDelivered, poseTestExitStraight = true;
    private static string[] fistShapesExpected = new string[0];
    private static bool thumbsShapeExpected, thumbsUpShapeExpected;
    private static Keyboard poseTestKeyboard;
    private static Dictionary<string, Vector3> poseTestRest;
    private static PoseTestFile poseTestFile;
    private static readonly string[] PoseTestTracked = { "Hand_L", "Hand_R", "TipIndex_L", "TipIndex_R", "TipThumb_L", "TipThumb_R" };
    // Самые дальние кости пальцев: кость кончика есть не у всех пальцев, у безымянного и мизинца — дальняя фаланга.
    private static readonly string[] PoseTestTips = { "TipThumb", "TipIndex", "TipMiddle", "Ring03", "Pinky03" };
    private static int poseTestGestures, poseTestPlayed;
    private static bool poseTestMoveDone, poseTestMovePending;
    private static DurakMove? poseTestMove;

    private static OwnArms3DView Arms => GameObjectNamed("own-arms").GetComponent<OwnArms3DView>();

    /// <summary>Нажать клавишу: виртуальная клавиатура Input System, как настоящая (OwnArms3DView читает Keyboard.current).</summary>
    private static void PressKey(Key key)
    {
        if (poseTestKeyboard == null)
        {
            // В пакетном режиме нет окна Game, у которого клавиатура могла бы быть в фокусе: ввод идёт в игру всегда.
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            poseTestKeyboard = InputSystem.AddDevice<Keyboard>("QA keyboard");
        }
        poseTestKeyboard.MakeCurrent();
        InputSystem.QueueStateEvent(poseTestKeyboard, new KeyboardState(key));
        poseTestPressFrame = Time.frameCount;
    }

    /// <summary>Отпустить клавишу через кадр после нажатия.</summary>
    private static bool ReleaseKey()
    {
        if (poseTestPressFrame < 0) return true;
        if (Time.frameCount <= poseTestPressFrame + 1) return false;
        InputSystem.QueueStateEvent(poseTestKeyboard, new KeyboardState());
        poseTestPressFrame = -1;
        return true;
    }

    private static Vector3 InBody(string bone) => GameObjectNamed("Seated body").transform.InverseTransformPoint(GameObjectNamed(bone).transform.position);

    /// <summary>Кости кисти как в Blender: расстояния от запястья до суставов пальцев в кадре frame клипа Take_TestClench.</summary>
    private static void CheckPoseTestBones(int frame, string what)
    {
        if (poseTestFile == null)
        {
            var path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "..", "art3d", "hands", "test", "CardCity_Hands_GameTest.json"));
            poseTestFile = JsonUtility.FromJson<PoseTestFile>(File.ReadAllText(path));
        }
        var bones = GameObjectNamed("own-arms").GetComponentsInChildren<Transform>().ToDictionary(t => t.name);
        var worst = 0f;
        var worstName = "";
        var marks = poseTestFile.marks.Where(m => m.frame == frame).ToArray();
        // На кадр 36 замеров: у каждой руки 15 костей пальцев и 3 кости кончиков (у безымянного и мизинца их нет).
        foreach (var mark in marks)
        {
            var error = Mathf.Abs(Vector3.Distance(bones["Hand" + mark.side].position, bones[mark.bone + mark.side].position) * 1000f - mark.mm);
            if (error <= worst) continue;
            worst = error;
            worstName = mark.bone + mark.side;
        }
        Check(marks.Length == 36 && worst < .3f, what + ": both hands match Blender (frame " + frame + " of " + poseTestFile.clip + "), worst " + worst.ToString("0.000")
            + " mm at " + worstName + " over " + marks.Length + " marks");
        table3DReport.Add("Pose test " + what + ": bones as in Blender within " + worst.ToString("0.000") + " mm (" + marks.Length + " marks)");
    }

    /// <summary>Руки в позе проверки: обе кисти в кадре, ни один кончик пальца не у сукна, веер спрятан.</summary>
    private static void CheckPoseTestFrame(string what)
    {
        var arms = Arms;
        foreach (var side in new[] { "_L", "_R" })
        {
            var hand = camera.WorldToViewportPoint(GameObjectNamed("Hand" + side).transform.position);
            var tip = camera.WorldToViewportPoint(GameObjectNamed("TipMiddle" + side).transform.position);
            Check(hand.z > 0f && hand.x > 0f && hand.x < 1f && hand.y > 0f && hand.y < 1f && tip.x > 0f && tip.x < 1f && tip.y > 0f && tip.y < 1f,
                what + ": the hand" + side + " is in the frame, wrist " + hand.ToString("0.00") + ", middle tip " + tip.ToString("0.00"));
            var low = PoseTestTips.Min(t => GameObjectNamed(t + side).transform.position.y) - .76f;
            Check(low > .05f, what + ": the hand" + side + " is well above the cloth, lowest fingertip " + low.ToString("0.000") + " m");
        }
        var parts = arms.FanRoot.GetComponentsInChildren<Renderer>();
        Check(parts.Length > 0 && parts.All(r => r.forceRenderingOff), what + ": the fan is hidden while the hands are tested, " + parts.Length + " parts");
    }

    private static void SavePoseTestClose(string name, float zoom = .12f)
    {
        // Ближе и ниже: как игрок наклоняется посмотреть на свои руки (zoom .22 — предел приближения камеры).
        var look = camera.GetComponent<TableCameraController>();
        AimAt(look, 38f, 0f, zoom);   // 38° — предел наклона камеры (TableCameraController.MaxPitch).
        Save(name);
        AimAt(look, (float)typeof(TableCameraController).GetField("StartPitch", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null), 0f, 0f);
    }

    /// <summary>
    /// Проверка базовых поз клавишей F, как её будет делать автор. F — кисти встают перед собой и сжимаются в кулак (кадры
    /// середины и кулака), F — разжимаются в Idle (кадр), F и посреди сжатия G — назад к игре: сначала кисть разжимается, потом
    /// руки возвращаются к вееру и на сукно, веер снова виден. Затем жест во время проверки: руки выходят из неё, жест играется.
    /// </summary>
    private static bool WatchPoseTest()
    {
        var arms = Arms;
        var animator = arms.GetComponent<Animator>();
        var now = Time.time;
        switch (poseTestStep)
        {
            case 0:
                if (arms.Busy || arms.Gesture != null) return true;
                Time.timeScale = 1f;
                Check(!arms.PoseTestActive && animator.GetLayerWeight(3) == 0f, "before F the hands play the game, the test layer weighs 0");
                CheckFistSmoothWeight("before F", 0f);
                poseTestRest = PoseTestTracked.ToDictionary(b => b, InBody);
                PressKey(Key.F);
                poseTestSince = now;
                poseTestStep = 1;
                return true;
            case 1:
                if (!ReleaseKey()) return true;
                if (!arms.PoseTestActive && now - poseTestSince < 1f) return true;
                poseTestKeyDelivered = arms.PoseTestActive;
                table3DReport.Add("Pose test: key F " + (poseTestKeyDelivered ? "pressed on a virtual keyboard" : "did not reach the game in batch mode, TogglePoseTest called directly"));
                if (!poseTestKeyDelivered) arms.TogglePoseTest();
                poseTestStep = 2;
                return true;
            case 2:
                // Сначала кисти встают перед собой расслабленными, сжатие идёт, когда слой набрал полный вес.
                if (arms.PoseTestWeight < 1f)
                {
                    Check(arms.Clench == 0f, "the hands clench only once they are in the test pose, clench " + arms.Clench + " at weight " + arms.PoseTestWeight);
                    return true;
                }
                if (arms.Clench < .5f) return true;
                Save("pose-test-mid.png");
                CheckPoseTestFrame("half-clenched");
                poseTestStep = 3;
                return true;
            case 3:
                if (arms.Clench < 1f) return true;
                poseTestSince = now;
                poseTestStep = 4;
                return true;
            case 4:
                // Кадр-другой, чтобы аниматор записал позу кулака в кости.
                if (now - poseTestSince < .1f) return true;
                Check(Mathf.Approximately(animator.GetFloat("TestClench"), 1f) && Mathf.Approximately(animator.GetLayerWeight(3), 1f), "fist: TestClench 1, PoseTest weight 1");
                CheckPoseTestBones(18, "fist");
                CheckPoseTestFrame("fist");
                CheckFistSmoothWeight("fist", 100f);
                Save("pose-test-fist.png");
                SavePoseTestClose("pose-test-fist-close.png");
                SavePoseTestClose("pose-test-fist-zoom.png", .22f);
                if (poseTestKeyDelivered) PressKey(Key.F);
                else arms.TogglePoseTest();
                poseTestStep = 5;
                return true;
            case 5:
                if (!ReleaseKey() || arms.Clench > 0f) return true;
                Check(arms.PoseTestActive && Mathf.Approximately(arms.PoseTestWeight, 1f), "the second F opens the hands and keeps them in the test pose");
                poseTestSince = now;
                poseTestStep = 6;
                return true;
            case 6:
                if (now - poseTestSince < .1f) return true;
                CheckPoseTestBones(0, "idle");
                CheckPoseTestFrame("idle");
                CheckFistSmoothWeight("idle", 0f);
                Save("pose-test-idle.png");
                SavePoseTestClose("pose-test-idle-close.png");
                SavePoseTestClose("pose-test-idle-zoom.png", .22f);
                if (poseTestKeyDelivered) PressKey(Key.F);
                else arms.TogglePoseTest();
                poseTestStep = 7;
                return true;
            case 7:
                // Посреди сжатия — G: назад к игре.
                if (!ReleaseKey() || arms.Clench < .5f) return true;
                poseTestClenchAtG = arms.Clench;
                if (poseTestKeyDelivered) PressKey(Key.G);
                else arms.StopPoseTest();
                poseTestSince = now;
                poseTestStep = 8;
                return true;
            case 8:
                if (!ReleaseKey()) return true;
                // Кулак с позой партии не смешивается: пока кисть не разжалась, слой держит полный вес.
                if (arms.Clench > 0f && arms.PoseTestWeight < 1f) poseTestExitStraight = false;
                if (arms.PoseTestActive) return true;
                Check(poseTestExitStraight, "G opens the hand first and only then brings the hands back (clench was " + poseTestClenchAtG.ToString("0.00") + ")");
                poseTestSince = now;
                poseTestStep = 9;
                return true;
            case 9:
                if (now - poseTestSince < .3f) return true;
                var parts = arms.FanRoot.GetComponentsInChildren<Renderer>();
                Check(parts.Length > 0 && parts.All(r => !r.forceRenderingOff), "after G the fan is shown again, " + parts.Length + " parts");
                Check(animator.GetLayerWeight(3) == 0f && animator.GetFloat("TestClench") == 0f, "after G the test layer weighs 0 again");
                CheckFistSmoothWeight("after G", 0f);
                foreach (var bone in PoseTestTracked)
                {
                    var away = (InBody(bone) - poseTestRest[bone]).magnitude;
                    Check(away < (bone.StartsWith("Hand") ? .008f : .015f), "after G " + bone + " is back where it was before F: " + away.ToString("0.000") + " m away");
                }
                CheckFanHand("after the pose test");
                foreach (var tip in new[] { "TipIndex" + OwnArms3DView.FreeSide, "TipMiddle" + OwnArms3DView.FreeSide })
                {
                    var above = GameObjectNamed(tip).transform.position.y - .76f;
                    Check(above > -.006f && above < .03f, "after the pose test the free hand rests on the cloth again: " + tip + " " + above.ToString("0.000") + " m");
                }
                var thumb = camera.WorldToViewportPoint(GameObjectNamed("Thumb03" + OwnArms3DView.FanSide).transform.position);
                Check(thumb.y > 0f && FanHalf(thumb.x, .6f), "after the pose test the fan thumb lies on the fan again, got " + thumb);
                Save("pose-test-back.png");
                // Жест во время проверки: руки выходят из неё, и только потом играется жест.
                arms.TogglePoseTest();
                poseTestGestures = OwnArms3DView.GesturesDone;
                poseTestStep = 10;
                return true;
            case 10:
                if (arms.PoseTestWeight < .5f) return true;
                Check(arms.PlayGesture("ThumbsUp"), "a gesture asked during the pose test is accepted");
                Check(arms.Gesture == null, "the gesture waits until the hands leave the pose test");
                poseTestStep = 11;
                return true;
            case 11:
                if (arms.Gesture == null) return true;
                Check(!arms.PoseTestActive && animator.GetLayerWeight(3) == 0f, "the gesture starts once the pose test is over");
                poseTestStep = 12;
                return true;
            case 12:
                if (arms.Gesture != null) return true;
                Check(OwnArms3DView.GesturesDone == poseTestGestures + 1, "the gesture after the pose test plays to the end");
                table3DReport.Add("Pose test: F, F, G and a gesture during the test checked");
                return false;
        }
        return false;
    }

    // --- Проверка новых жестов: клавиши U, J, H (OwnArms3DView, слои GestureTest_L / _R; этап A — «класс» и «фу») ---------

    [Serializable] private class GestureTestMark { public string clip; public int frame; public int frames; public string side; public string bone; public float mm; }
    [Serializable] private class GestureTestFile { public float fps; public GestureTestMark[] marks; }

    private static GestureTestFile gestureTestFile;
    private static int gestureTestStep;
    private static float gestureTestSince;
    private static bool gestureTestExitStraight = true;
    private static Dictionary<string, Vector3> gestureTestRest;
    // Последний кадр клипов жестов (scripts/3d/hands/gesture_test.py FRAMES).
    private const int ThumbsUpEnd = 20, ThumbsDownEnd = 24;

    /// <summary>Клавиша проверки: виртуальная клавиатура, если F до игры дошла, иначе прямой вызов (как в WatchPoseTest).</summary>
    private static void PressOrCall(Key key, Action call)
    {
        if (poseTestKeyDelivered) PressKey(key);
        else call();
    }

    /// <summary>Кости руки side (или обеих, null) как в Blender в кадре frame клипа clip (CardCity_Hands_GestureTest.json).</summary>
    private static void CheckGestureBones(string clip, int frame, string side, string what)
    {
        if (gestureTestFile == null)
        {
            var path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "..", "art3d", "hands", "test", "CardCity_Hands_GestureTest.json"));
            gestureTestFile = JsonUtility.FromJson<GestureTestFile>(File.ReadAllText(path));
        }
        var bones = GameObjectNamed("own-arms").GetComponentsInChildren<Transform>().ToDictionary(t => t.name);
        var marks = gestureTestFile.marks.Where(m => m.clip == clip && m.frame == frame && (side == null || m.side == side)).ToArray();
        var worst = 0f;
        var worstName = "";
        foreach (var mark in marks)
        {
            var error = Mathf.Abs(Vector3.Distance(bones["Hand" + mark.side].position, bones[mark.bone + mark.side].position) * 1000f - mark.mm);
            if (error <= worst) continue;
            worst = error;
            worstName = mark.bone + mark.side;
        }
        Check(marks.Length == (side == null ? 36 : 18) && worst < .3f, what + ": " + (side ?? "both hands") + " match Blender (frame " + frame + " of " + clip + "), worst "
            + worst.ToString("0.000") + " mm at " + worstName + " over " + marks.Length + " marks");
        table3DReport.Add("Gesture test " + what + ": bones as in Blender within " + worst.ToString("0.000") + " mm (" + marks.Length + " marks)");
    }

    /// <summary>
    /// Большой палец на экране: от пястно-фалангового сустава к кончику вверх (up) или вниз, а не вбок — не дальше 40° от
    /// вертикали. В «фу» палец намеренно уходит к середине кадра на ~30°: строго вертикальный, он висел за предплечьем
    /// (руки идут снизу из углов кадра, scripts/3d/hands/gesture_test.py).
    /// </summary>
    private static void CheckThumbOnScreen(string side, bool up, string what)
    {
        var tip = camera.WorldToViewportPoint(GameObjectNamed("TipThumb" + side).transform.position);
        var root = camera.WorldToViewportPoint(GameObjectNamed("Thumb02" + side).transform.position);
        var dx = (tip.x - root.x) * Qa3DWidth;
        var dy = (tip.y - root.y) * Qa3DHeight * (up ? 1f : -1f);
        Check(dy > 40f && Mathf.Abs(dx) < dy * .84f, what + ": the thumb" + side + " points " + (up ? "up" : "down") + " on screen, "
            + dy.ToString("0") + " px along, " + dx.ToString("0") + " px aside");
    }

    private static bool AtTime(OwnArms3DView arms, float value) => Mathf.Approximately(arms.TestGestureTime, value);

    /// <summary>Номер состояния слоя GestureTest: жест (порядок OwnArms3DView.TestGestures) × 3 + руки (0 — обе, 1 — правая, 2 — левая) + 1.</summary>
    private static int GestureState(string gesture, int hands) => Array.IndexOf(OwnArms3DView.TestGestures, gesture) * OwnArms3DView.TestHandsSuffix.Length + hands + 1;

    /// <summary>
    /// «Класс» на скелете (клипы Take_TestThumbsUp*). С 2026-11-23 клавиша U показывает статичную модель автора
    /// (WatchThumbsModel), а прежний «класс» оставлен в коде (OwnArms3DView.StaticThumbsUp = false) — проверка зовёт его напрямую.
    /// </summary>
    private static void RigThumbsUp(OwnArms3DView arms) => arms.ShowTestGesture("ThumbsUp");

    /// <summary>
    /// Новые жесты клавишами, как их будет смотреть автор: «класс» на скелете обеими руками (кадр середины перехода и жест, обычный
    /// ракурс, ближе и на пределе приближения), J — через Idle в «фу», H — только правая, только левая, снова обе (каждый раз
    /// через Idle), снова «класс», F — из жеста через Idle в кулак, J — из кулака в «фу», J — назад в Idle, «класс» и посреди
    /// перехода G — назад к игре: сначала кисть уходит в Idle, потом руки к вееру и на сукно.
    /// </summary>
    private static bool WatchGestureTest()
    {
        var arms = Arms;
        var animator = arms.GetComponent<Animator>();
        var now = Time.time;
        bool Wait(float seconds) => now - gestureTestSince < seconds;
        bool Shown(string gesture, int hands) => arms.TestGestureState == GestureState(gesture, hands) && AtTime(arms, 1f);
        switch (gestureTestStep)
        {
            case 0:
                if (arms.Busy || arms.Gesture != null || arms.PoseTestActive) return true;
                Time.timeScale = 1f;
                Check(animator.GetLayerWeight(4) == 0f, "before U the gesture test layer weighs 0");
                CheckThumbsShape("before U", null, 0f, 0f);
                gestureTestRest = PoseTestTracked.ToDictionary(b => b, InBody);
                RigThumbsUp(arms);
                gestureTestStep = 1;
                return true;
            case 1:
                if (!ReleaseKey()) return true;
                Check(arms.TestGesture == "ThumbsUp" && arms.PoseTestActive, "the rig thumbs-up gesture test starts, got " + arms.TestGesture);
                gestureTestStep = 2;
                return true;
            case 2:
                // Сначала руки встают перед собой расслабленными, жест идёт, когда слой проверки набрал полный вес.
                if (arms.PoseTestWeight < 1f)
                {
                    Check(AtTime(arms, 0f) && arms.Clench == 0f, "the gesture starts only once the hands are in the test pose");
                    return true;
                }
                if (arms.TestGestureTime < .5f) return true;
                Save("gesture-thumbsup-mid.png");
                CheckPoseTestFrame("thumbs-up half way");
                gestureTestStep = 3;
                return true;
            case 3:
                if (!AtTime(arms, 1f)) return true;
                gestureTestSince = now;
                gestureTestStep = 4;
                return true;
            case 4:
                if (Wait(.1f)) return true;
                Check(animator.GetInteger("TestGesture") == GestureState("ThumbsUp", 0) && animator.GetLayerWeight(4) == 1f
                    && Mathf.Approximately(animator.GetFloat("TestGestureTime"), 1f) && arms.Clench == 0f,
                    "thumbs up: TestGesture " + GestureState("ThumbsUp", 0) + " and time 1, the gesture layer weighs 1, no clench");
                CheckGestureBones("Take_TestThumbsUp", ThumbsUpEnd, null, "thumbs up");
                CheckPoseTestFrame("thumbs up");
                CheckThumbOnScreen("_L", true, "thumbs up");
                CheckThumbOnScreen("_R", true, "thumbs up");
                CheckThumbsShape("thumbs up", "ThumbsUp", 100f, 100f);
                CheckFistSmoothWeight("thumbs up", 0f);
                Save("gesture-thumbsup.png");
                SavePoseTestClose("gesture-thumbsup-close.png");
                SavePoseTestClose("gesture-thumbsup-zoom.png", .22f);
                PressOrCall(Key.J, () => arms.ShowTestGesture("ThumbsDown"));
                gestureTestStep = 5;
                return true;
            case 5:
                if (!ReleaseKey()) return true;
                // Жест меняется только в Idle: номер состояния в аниматоре сменился — значит, время уже 0.
                if (animator.GetInteger("TestGesture") != GestureState("ThumbsDown", 0)) return true;
                Check(arms.TestGestureTime <= .05f, "J from thumbs up goes through Idle: the gesture switches at time " + arms.TestGestureTime.ToString("0.00"));
                gestureTestStep = 6;
                return true;
            case 6:
                if (arms.TestGestureTime < .5f) return true;
                Save("gesture-thumbsdown-mid.png");
                CheckPoseTestFrame("thumbs-down half way");
                gestureTestStep = 7;
                return true;
            case 7:
                if (!AtTime(arms, 1f)) return true;
                gestureTestSince = now;
                gestureTestStep = 8;
                return true;
            case 8:
                if (Wait(.1f)) return true;
                CheckGestureBones("Take_TestThumbsDown", ThumbsDownEnd, null, "thumbs down");
                CheckPoseTestFrame("thumbs down");
                CheckThumbOnScreen("_L", false, "thumbs down");
                CheckThumbOnScreen("_R", false, "thumbs down");
                CheckThumbsShape("thumbs down", "ThumbsDown", 100f, 100f);
                Save("gesture-thumbsdown.png");
                SavePoseTestClose("gesture-thumbsdown-close.png");
                SavePoseTestClose("gesture-thumbsdown-zoom.png", .22f);
                PressOrCall(Key.H, arms.CycleTestHands);
                gestureTestStep = 9;
                return true;
            case 9:
                // H: только правая — через Idle в «фу» правой рукой, левая остаётся в Idle.
                if (!ReleaseKey() || !Shown("ThumbsDown", 1)) return true;
                gestureTestSince = now;
                gestureTestStep = 10;
                return true;
            case 10:
                if (Wait(.1f)) return true;
                Check(arms.TestHands == 1, "H: only the right hand shows the gesture, got hands " + arms.TestHands);
                CheckGestureBones("Take_TestThumbsDown", ThumbsDownEnd, "_R", "right hand only");
                CheckGestureBones("Take_TestThumbsDown", 0, "_L", "right hand only, left at rest");
                CheckThumbsShape("right hand only", "ThumbsDown", 0f, 100f);
                Save("gesture-right-only.png");
                PressOrCall(Key.H, arms.CycleTestHands);
                gestureTestStep = 11;
                return true;
            case 11:
                // H: только левая.
                if (!ReleaseKey() || !Shown("ThumbsDown", 2)) return true;
                gestureTestSince = now;
                gestureTestStep = 12;
                return true;
            case 12:
                if (Wait(.1f)) return true;
                Check(arms.TestHands == 2, "H again: only the left hand shows the gesture, got hands " + arms.TestHands);
                CheckGestureBones("Take_TestThumbsDown", ThumbsDownEnd, "_L", "left hand only");
                CheckGestureBones("Take_TestThumbsDown", 0, "_R", "left hand only, right at rest");
                CheckThumbsShape("left hand only", "ThumbsDown", 100f, 0f);
                Save("gesture-left-only.png");
                PressOrCall(Key.H, arms.CycleTestHands);
                gestureTestStep = 13;
                return true;
            case 13:
                // H: снова обе, затем U — снова «класс» (через Idle).
                if (!ReleaseKey() || !Shown("ThumbsDown", 0)) return true;
                Check(arms.TestHands == 0, "H a third time: both hands again");
                RigThumbsUp(arms);
                gestureTestStep = 14;
                return true;
            case 14:
                if (!ReleaseKey() || !Shown("ThumbsUp", 0)) return true;
                // F из жеста: кисть уходит в Idle и сжимается в кулак.
                PressOrCall(Key.F, () => arms.TogglePoseTest());
                gestureTestExitStraight = true;
                gestureTestStep = 15;
                return true;
            case 15:
                if (!ReleaseKey()) return true;
                if (arms.Clench > 0f && arms.TestGestureTime > 0f) gestureTestExitStraight = false;
                if (arms.Clench < 1f) return true;
                gestureTestSince = now;
                gestureTestStep = 16;
                return true;
            case 16:
                if (Wait(.1f)) return true;
                Check(gestureTestExitStraight && AtTime(arms, 0f) && animator.GetLayerWeight(4) == 0f,
                    "F from a gesture opens it to Idle first and then clenches the fist, the gesture layer weighs 0");
                CheckPoseTestBones(18, "fist after a gesture");
                CheckFistSmoothWeight("fist after a gesture", 100f);
                CheckThumbsShape("fist after a gesture", null, 0f, 0f);
                // J из кулака: кисть разжимается и показывает «фу».
                PressOrCall(Key.J, () => arms.ShowTestGesture("ThumbsDown"));
                gestureTestStep = 17;
                return true;
            case 17:
                if (!ReleaseKey() || !Shown("ThumbsDown", 0)) return true;
                Check(arms.Clench == 0f && arms.TestGesture == "ThumbsDown", "J from the fist opens it and shows thumbs down");
                CheckGestureBones("Take_TestThumbsDown", ThumbsDownEnd, null, "thumbs down after the fist");
                // J ещё раз: назад в расслабленную кисть.
                PressOrCall(Key.J, () => arms.ShowTestGesture("ThumbsDown"));
                gestureTestStep = 18;
                return true;
            case 18:
                if (!ReleaseKey() || !AtTime(arms, 0f)) return true;
                gestureTestSince = now;
                gestureTestStep = 19;
                return true;
            case 19:
                if (Wait(.1f)) return true;
                Check(arms.TestGesture == null && arms.PoseTestActive && Mathf.Approximately(arms.PoseTestWeight, 1f), "J a second time goes back to the relaxed hand and stays in the test");
                CheckPoseTestBones(0, "idle after a gesture");
                CheckThumbsShape("idle after a gesture", null, 0f, 0f);
                RigThumbsUp(arms);
                gestureTestStep = 20;
                return true;
            case 20:
                // Посреди перехода в «класс» — G: назад к игре.
                if (!ReleaseKey() || arms.TestGestureTime < .5f) return true;
                PressOrCall(Key.G, arms.StopPoseTest);
                gestureTestExitStraight = true;
                gestureTestStep = 21;
                return true;
            case 21:
                if (!ReleaseKey()) return true;
                if (arms.TestGestureTime > 0f && arms.PoseTestWeight < 1f) gestureTestExitStraight = false;
                if (arms.PoseTestActive) return true;
                Check(gestureTestExitStraight, "G during a gesture opens the hand to Idle first and only then brings the hands back");
                gestureTestSince = now;
                gestureTestStep = 22;
                return true;
            case 22:
                if (Wait(.3f)) return true;
                var parts = arms.FanRoot.GetComponentsInChildren<Renderer>();
                Check(parts.Length > 0 && parts.All(r => !r.forceRenderingOff), "after G the fan is shown again, " + parts.Length + " parts");
                Check(animator.GetLayerWeight(3) == 0f && animator.GetLayerWeight(4) == 0f && AtTime(arms, 0f),
                    "after G the pose test and gesture test layers weigh 0 again");
                CheckFistSmoothWeight("after the gesture test", 0f);
                CheckThumbsShape("after the gesture test", null, 0f, 0f);
                foreach (var bone in PoseTestTracked)
                {
                    var away = (InBody(bone) - gestureTestRest[bone]).magnitude;
                    Check(away < (bone.StartsWith("Hand") ? .008f : .015f), "after the gesture test " + bone + " is back where it was before U: " + away.ToString("0.000") + " m away");
                }
                CheckFanHand("after the gesture test");
                foreach (var tip in new[] { "TipIndex" + OwnArms3DView.FreeSide, "TipMiddle" + OwnArms3DView.FreeSide })
                {
                    var above = GameObjectNamed(tip).transform.position.y - .76f;
                    Check(above > -.006f && above < .03f, "after the gesture test the free hand rests on the cloth again: " + tip + " " + above.ToString("0.000") + " m");
                }
                Save("gesture-test-back.png");
                table3DReport.Add("Gesture test: rig thumbs up, J, H (right, left, both), rig thumbs up, F, J, J, rig thumbs up and G checked");
                return false;
        }
        return false;
    }

    // --- «Класс» — статичная модель автора: клавиша U (OwnArms3DView.ToggleThumbsModel, ThumbsUpModel3D; 2026-11-23) ------

    private static int thumbsModelStep;
    private static float thumbsModelSince, thumbsCloth = float.MaxValue, thumbsRim = float.MaxValue;
    private static bool thumbsAwaySaved, thumbsRisingSaved, thumbsLeftFrame;
    private static Vector3 thumbsHandRest;
    private static Vector3[] thumbsModelPoints;
    private static RenderTexture thumbsPhoneFrame;

    /// <summary>
    /// «Класс» клавишей U, как его будет смотреть автор: правая рука быстро уходит вниз за край кадра (над столом — не ниже
    /// сукна и борта) и выключается, модель выезжает снизу в правый нижний угол (кадры: рука уходит, модель выезжает, модель
    /// в углу с обычного места, ближе, на пределе приближения, взгляд влево, вправо, вверх); U ещё раз — модель уезжает, рука
    /// возвращается на сукно туда же, где была. Затем U и F: модель уезжает, рука возвращается, руки встают в проверку; G — к игре.
    /// </summary>
    private static bool WatchThumbsModel()
    {
        var arms = Arms;
        var now = Time.time;
        var free = OwnArms3DView.FreeSide;
        bool Wait(float seconds) => now - thumbsModelSince < seconds;
        switch (thumbsModelStep)
        {
            case 0:
                if (arms.Busy || arms.Gesture != null || arms.PoseTestActive || arms.ThumbsSwapActive) return true;
                Time.timeScale = 1f;
                CheckThumbsModelGone("before U");
                thumbsHandRest = InBody("Hand" + free);
                MeasureArmPath();
                Check(thumbsCloth > -.006f && thumbsRim > -.005f && thumbsRim < float.MaxValue, "on its way off screen the free hand stays above the cloth ("
                    + thumbsCloth.ToString("0.000") + " m) and passes above the rim (" + thumbsRim.ToString("0.000") + " m)");
                PressOrCall(Key.U, arms.ToggleThumbsModel);
                thumbsModelStep = 1;
                return true;
            case 1:
                if (!ReleaseKey()) return true;
                Check(arms.ThumbsSwapActive && arms.Gesture == null, "U starts the thumbs-up model, not a gesture of the rig");
                thumbsModelStep = 2;
                return true;
            case 2:
                // Рука уходит; в конце пути вся кисть за нижним краем кадра.
                if (!thumbsAwaySaved && arms.ThumbsArmAway >= .5f)
                {
                    thumbsAwaySaved = true;
                    Save("thumbs-model-arm-away.png");
                }
                if (arms.ThumbsArmAway < 1f) return true;
                thumbsLeftFrame = new[] { "Hand" }.Concat(PoseTestTips).All(b => OffScreen(GameObjectNamed(b + free).transform.position));
                Check(thumbsLeftFrame, "the free hand leaves the frame before it is switched off");
                thumbsModelStep = 3;
                return true;
            case 3:
                if (!thumbsRisingSaved && arms.ThumbsModelIn >= .5f)
                {
                    thumbsRisingSaved = true;
                    Check(arms.FreeArmParts.All(p => !p.activeSelf), "while the model rises the free arm is off");
                    Save("thumbs-model-rising.png");
                }
                if (!arms.ThumbsModelShown) return true;
                thumbsModelSince = now;
                thumbsModelStep = 4;
                return true;
            case 4:
                if (Wait(.1f)) return true;
                Save("thumbs-model.png");
                CheckThumbsModelInCorner("thumbs model");
                var look = camera.GetComponent<TableCameraController>();
                foreach (var (name, pitch, yaw, zoom) in new[] { ("close", 38f, 0f, .12f), ("zoom", 38f, 0f, .22f), ("look-left", 32f, -32f, 0f), ("look-right", 32f, 32f, 0f), ("look-up", 12f, 0f, 0f) })
                {
                    AimAt(look, pitch, yaw, zoom);
                    Save("thumbs-model-" + name + ".png");
                    CheckThumbsModelInCorner("thumbs model " + name);
                }
                AimAt(look, (float)typeof(TableCameraController).GetField("StartPitch", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null), 0f, 0f);
                // Экран 16:9 (ПК): другой угол обзора и другая раскладка кнопок — модель всё так же в углу, срез рукава за краем.
                thumbsPhoneFrame = camera.targetTexture;
                camera.targetTexture = new RenderTexture(1600, 900, 24);
                thumbsModelSince = now;
                thumbsModelStep = 10;
                return true;
            case 10:
                if (Wait(.15f)) return true;
                Save("thumbs-model-16x9.png");
                CheckThumbsModelInCorner("thumbs model 16:9");
                var wide = camera.targetTexture;
                camera.targetTexture = thumbsPhoneFrame;
                wide.Release();
                thumbsModelSince = now;
                thumbsModelStep = 11;
                return true;
            case 11:
                if (Wait(.15f)) return true;
                PressOrCall(Key.U, arms.ToggleThumbsModel);
                thumbsModelStep = 5;
                return true;
            case 5:
                // U ещё раз: сначала уезжает модель, рука включается, только когда модель ушла под кадр.
                if (!ReleaseKey()) return true;
                if (arms.ThumbsModelIn > 0f)
                {
                    Check(arms.FreeArmParts.All(p => !p.activeSelf), "while the model goes down the free arm stays off");
                    return true;
                }
                if (arms.ThumbsSwapActive) return true;
                thumbsModelSince = now;
                thumbsModelStep = 6;
                return true;
            case 6:
                if (Wait(.3f)) return true;
                Save("thumbs-model-back.png");
                CheckThumbsModelGone("after U a second time");
                var away = (InBody("Hand" + free) - thumbsHandRest).magnitude;
                Check(away < .008f, "after the thumbs-up model the free hand is back where it was: " + away.ToString("0.000") + " m away");
                foreach (var tip in new[] { "TipIndex" + free, "TipMiddle" + free })
                {
                    var above = GameObjectNamed(tip).transform.position.y - .76f;
                    Check(above > -.006f && above < .03f, "after the thumbs-up model the free hand rests on the cloth again: " + tip + " " + above.ToString("0.000") + " m");
                }
                // U и F: модель уезжает, рука возвращается, руки встают в проверку поз.
                PressOrCall(Key.U, arms.ToggleThumbsModel);
                thumbsModelStep = 7;
                return true;
            case 7:
                if (!ReleaseKey() || !arms.ThumbsModelShown) return true;
                PressOrCall(Key.F, () =>
                {
                    arms.ToggleThumbsModel();
                    arms.TogglePoseTest();
                });
                thumbsModelStep = 8;
                return true;
            case 8:
                if (!ReleaseKey()) return true;
                if (arms.ThumbsSwapActive || arms.PoseTestWeight < 1f) return true;
                CheckThumbsModelGone("F during the thumbs-up model");
                Check(arms.PoseTestActive, "F during the thumbs-up model sends the model away and starts the pose test");
                Save("thumbs-model-then-f.png");
                PressOrCall(Key.G, arms.StopPoseTest);
                thumbsModelStep = 9;
                return true;
            case 9:
                if (!ReleaseKey() || arms.PoseTestActive) return true;
                table3DReport.Add("Thumbs-up model: U (arm off screen, model in the corner from six views and at 16:9), U back, U and F checked;"
                    + " the free hand on its way stays " + thumbsCloth.ToString("0.000") + " m above the cloth and " + thumbsRim.ToString("0.000") + " m above the rim");
                return false;
        }
        return false;
    }

    /// <summary>Точка за краем кадра или за камерой.</summary>
    private static bool OffScreen(Vector3 world)
    {
        var p = camera.WorldToViewportPoint(world);
        return p.z <= 0f || p.x < 0f || p.x > 1f || p.y < 0f || p.y > 1f;
    }

    /// <summary>
    /// Весь путь свободной руки за край кадра от её покоя на сукне, с шагом 2 %: рука сдвигается целиком (предплечье —
    /// OwnArms3DView.FreeArmAway), поэтому кисть и кончики пальцев на пути — их покой плюс сдвиг. Над сукном — не ниже
    /// его (thumbsCloth), над бортом — не ниже верха борта (thumbsRim). Запоминается худшее.
    /// </summary>
    private static void MeasureArmPath()
    {
        var arms = Arms;
        var rest = new[] { "Hand" }.Concat(PoseTestTips).Select(b => GameObjectNamed(b + OwnArms3DView.FreeSide).transform.position).ToArray();
        for (var i = 0; i <= 50; i++)
        {
            var shift = (Vector3)Call(arms, "FreeArmAway", i / 50f);
            foreach (var point in rest)
            {
                var p = point + shift;
                if (OverTable(p, TableA - Rim, TableB - Rim)) thumbsCloth = Mathf.Min(thumbsCloth, p.y - ClothY);
                else if (OverTable(p, TableA, TableB)) thumbsRim = Mathf.Min(thumbsRim, p.y - RimTop);
            }
        }
    }

    // Стол — scripts/3d/table_model.py: полуоси внешнего края овала (как Table3DLayout.TableA, TableB — он внутренний для
    // сборки игры), высота сукна, ширина золотого обода и его верх (на 1,5 см выше сукна).
    private const float TableA = .92f, TableB = .65f, ClothY = .76f, Rim = .08f, RimTop = .775f;

    private static bool OverTable(Vector3 p, float a, float b) => Sq(p.x / a) + Sq(p.z / b) < 1f;

    private static float Sq(float x) => x * x;

    /// <summary>Подмены нет: модель выключена, свободная рука (кожа, рукав, плечо, шар на локте) включена.</summary>
    private static void CheckThumbsModelGone(string what)
    {
        var arms = Arms;
        var side = OwnArms3DView.FreeSide;
        Check(!arms.ThumbsSwapActive && !arms.ThumbsModel.gameObject.activeSelf, what + ": the thumbs-up model is off");
        Check(arms.FreeArmParts.Count == 2 && arms.FreeArmParts.All(p => p.activeSelf) && GameObjectNamed("Upper arm" + side).activeSelf
            && GameObjectNamed("Elbow" + side).activeSelf, what + ": the free arm (skin, sleeve, upper arm, elbow) is on, " + arms.FreeArmParts.Count + " skinned parts");
    }

    /// <summary>
    /// Модель «класса» в правом нижнем углу кадра: большой палец (верхняя четверть модели) целиком в кадре, всё видимое — в правой
    /// половине и в нижних двух третях, срез рукава (нижняя пятая часть модели) за краем кадра; ни одна точка не уходит в стол
    /// (над овалом стола — выше его верха); свободная рука выключена.
    /// </summary>
    private static void CheckThumbsModelInCorner(string what)
    {
        var arms = Arms;
        var side = OwnArms3DView.FreeSide;
        Check(arms.ThumbsModel.gameObject.activeInHierarchy && arms.ThumbsModelRenderer.enabled, what + ": the thumbs-up model is shown");
        Check(arms.FreeArmParts.All(p => !p.activeSelf) && !GameObjectNamed("Upper arm" + side).activeSelf && !GameObjectNamed("Elbow" + side).activeSelf,
            what + ": the free arm is off while the model is shown");
        var filter = arms.ThumbsModelRenderer.GetComponent<MeshFilter>();
        var mesh = filter.sharedMesh;
        thumbsModelPoints ??= mesh.vertices;
        var low = mesh.bounds.min.y;
        var height = mesh.bounds.size.y;
        var toWorld = filter.transform.localToWorldMatrix;
        int thumb = 0, thumbIn = 0, cuff = 0, cuffOut = 0;
        float minX = 1f, maxY = 0f, aboveTable = float.MaxValue;
        foreach (var local in thumbsModelPoints)
        {
            var world = toWorld.MultiplyPoint3x4(local);
            var p = camera.WorldToViewportPoint(world);
            var inFrame = p.z > 0f && p.x >= 0f && p.x <= 1f && p.y >= 0f && p.y <= 1f;
            var share = (local.y - low) / height;
            if (share > .75f)
            {
                thumb++;
                if (inFrame) thumbIn++;
            }
            if (share < .2f)
            {
                cuff++;
                if (!inFrame) cuffOut++;
            }
            if (inFrame)
            {
                minX = Mathf.Min(minX, p.x);
                maxY = Mathf.Max(maxY, p.y);
            }
            if (OverTable(world, TableA, TableB)) aboveTable = Mathf.Min(aboveTable, world.y - RimTop);
        }
        Check(thumb > 0 && thumbIn == thumb, what + ": the whole thumb is in the frame, " + thumbIn + " of " + thumb + " points");
        Check(minX > .5f && maxY < .67f, what + ": the model stays in the lower right corner, from x " + minX.ToString("0.00") + " up to y " + maxY.ToString("0.00"));
        Check(cuff > 0 && cuffOut == cuff, what + ": the end of the cuff is off screen, " + cuffOut + " of " + cuff + " points");
        Check(aboveTable > 0f, what + ": the model does not go into the table, lowest point over the table " + (aboveTable == float.MaxValue ? "none" : aboveTable.ToString("0.000") + " m") + " above its top");
        table3DReport.Add("Thumbs-up model " + what + ": left edge at x " + minX.ToString("0.00") + ", top at y " + maxY.ToString("0.00") + ", "
            + (aboveTable == float.MaxValue ? "not over the table" : aboveTable.ToString("0.000") + " m above the table top"));
    }

    private static bool thumbsMoveDone, thumbsMovePending;
    private static int thumbsMovePlayed;

    /// <summary>
    /// Ход картой, пока вместо свободной руки модель «класса» (после хода во время проверки поз): модель выезжает (U), затем
    /// делается ход картой из веера. Модель уезжает, рука возвращается и только потом берёт карту. Ход выбирается, когда
    /// модель уже в углу: карта, которая ещё летит в веер, руке не достаётся (её кладёт не рука) — такой ход, пас и «взять»
    /// делаются как обычно, а проверка ждёт следующего хода игрока.
    /// </summary>
    private static bool TryThumbsModelMove(DurakGame game)
    {
        if (thumbsMoveDone || !poseTestMoveDone || poseTestMovePending) return false;
        var arms = Arms;
        if (!arms.ThumbsModelShown)
        {
            if (arms.Busy) return true;
            if (!arms.ThumbsSwapActive)
            {
                Time.timeScale = 1f;
                arms.ToggleThumbsModel();
            }
            return true;
        }
        var move = table3DPolicy.ChooseMove(game, 0);
        var held = Get<Dictionary<Card, Card3DView>>(table3D, "_cards");
        if (!move.HasValue || (move.Value.Type != DurakMoveType.Attack && move.Value.Type != DurakMoveType.Defend)
            || !held.TryGetValue(move.Value.Card, out var view) || view.IsFlying || view.transform.parent != arms.FanRoot) return false;
        thumbsMoveDone = thumbsMovePending = true;
        thumbsMovePlayed = OwnArms3DView.Played;
        table3DMoves++;
        Call(table3D, "ApplyHumanMove", move.Value);
        return true;
    }

    private static bool WatchThumbsModelMove()
    {
        if (!thumbsMovePending) return false;
        var arms = Arms;
        if (arms.ThumbsSwapActive)
        {
            Check(Get<Card3DView>(arms, "_card") == null, "the free hand takes the card only after the thumbs-up model left and the hand came back");
            return true;
        }
        if (OwnArms3DView.Played <= thumbsMovePlayed) return true;
        thumbsMovePending = false;
        CheckThumbsModelGone("after a card move during the thumbs-up model");
        table3DReport.Add("Thumbs-up model: a card move sent the model away, the free hand came back and played the card");
        Time.timeScale = 7f;
        return false;
    }

    /// <summary>
    /// Ход картой во время проверки поз: при первом ходе игрока картой (после проверки нажатия) руки сначала встают в позу
    /// проверки, потом делается ход. Рука берёт карту только после того, как руки вышли из проверки.
    /// </summary>
    private static bool TryPoseTestMove(DurakGame game)
    {
        if (poseTestMoveDone || !table3DTapDone) return false;
        var arms = Arms;
        if (poseTestMove == null)
        {
            var move = table3DPolicy.ChooseMove(game, 0);
            if (!move.HasValue || (move.Value.Type != DurakMoveType.Attack && move.Value.Type != DurakMoveType.Defend)) return false;
            poseTestMove = move;
            Time.timeScale = 1f;
            arms.TogglePoseTest();
            return true;
        }
        if (arms.Clench < 1f) return true;
        poseTestMoveDone = poseTestMovePending = true;
        poseTestPlayed = OwnArms3DView.Played;
        table3DMoves++;
        Call(table3D, "ApplyHumanMove", poseTestMove.Value);
        return true;
    }

    private static bool WatchPoseTestMove()
    {
        if (!poseTestMovePending) return false;
        var arms = Arms;
        if (Get<Card3DView>(arms, "_card") == null)
        {
            if (OwnArms3DView.Played > poseTestPlayed)
            {
                poseTestMovePending = false;
                table3DReport.Add("Pose test: a card move during the test took the hands out of it and the free hand played the card");
                Time.timeScale = 7f;
                return false;
            }
            return true;
        }
        Check(!arms.PoseTestActive && arms.PoseTestWeight == 0f, "the free hand takes the card only after the hands left the pose test");
        return true;
    }

    private static void AimAt(TableCameraController look, float pitch, float yaw, float zoom)
    {
        Set(look, "_pitch", pitch);
        Set(look, "_targetPitch", pitch);
        Set(look, "_yaw", yaw);
        Set(look, "_targetYaw", yaw);
        Set(look, "_zoom", zoom);
        Set(look, "_targetZoom", zoom);
        Call(look, "Update");
        // Тело догоняет взгляд сразу, как в настройке «меньше движения».
        Call(GameObjectNamed("own-arms").GetComponent<OwnArms3DView>(), "FollowEye", 1f);
    }

    private static GameObject GameObjectNamed(string name)
    {
        var found = Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(t => t.name == name && t.gameObject.scene.IsValid());
        Check(found != null, "object exists: " + name);
        return found.gameObject;
    }

    private static Bounds Bounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        var bounds = renderers[0].bounds;
        foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
        return bounds;
    }

    private static void Table3DFinish(Exception error)
    {
        EditorApplication.update -= Table3DTick;
        Application.logMessageReceived -= Table3DLog;
        File.WriteAllText(Path.Combine(Output, "runtime-errors.txt"), string.Join("\n\n", table3DErrors));
        File.WriteAllText(Path.Combine(Output, "verification.txt"), (error == null ? checks + " checks passed.\n" : "FAILED: " + error + "\n") + string.Join("\n", table3DReport));
        EditorApplication.Exit(error == null && table3DErrors.Count == 0 ? 0 : 1);
    }
}
