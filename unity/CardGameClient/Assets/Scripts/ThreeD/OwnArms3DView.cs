using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.InputSystem;
using UnityEngine.Playables;
using UnityEngine.Rendering.Universal;

namespace CardGameClient.ThreeD
{
    /// <summary>
    /// Свои руки за столом: модель из art3d/hands на скелете. Руки принадлежат телу на стуле: тело поворачивается за взглядом
    /// на BodyYawShare угла, поэтому веер остаётся внизу кадра, а правая кисть не уезжает с сукна. Наклон взгляда тело не повторяет.
    /// Все позы и жесты — ключевые кадры из Blender в Animator Controller Resources/ThreeD/Hands/HandsAnimator (собирает
    /// scripts/3d/HandsAnimatorBuilder.cs). Скрипт только ставит его параметры — SetTrigger на жест, SetBool("HoldingCards"),
    /// триггер Regrip — и вес слоя щипка; кости пальцев сам не крутит. Веер держит рука FanSide (левая: автор так держит
    /// карты), он висит на кости SocketFan_L её кисти: положение и оси заданы в Blender по позе хвата. Жесты показывает и карты
    /// берёт свободная рука FreeSide (правая). Тело дышит.
    /// Сыгранная карта проходит стадии: Reach — свободная рука тянется к вееру, Grab — берёт карту щипком за верхний край и вытягивает,
    /// Carry — выносит над столом и отпускает (дальше карта сама долетает и ложится лицом вверх), Return — рука возвращается в покой.
    /// Временная проверка базовых поз новой системы рук (только в редакторе и отладочной сборке): F — обе кисти встают перед собой
    /// и плавно сжимаются в кулак, F ещё раз — разжимаются в расслабленную кисть (Idle), G — назад к игре. Позы — клип
    /// Take_TestClench в слое PoseTest аниматора; код ставит вес слоя и параметр TestClench (время клипа), кости сам не крутит.
    /// Там же проверка новых жестов (этап A — «класс» и «фу», 2026-11-20): U — «класс», J — «фу» (та же клавиша ещё раз —
    /// назад в расслабленную кисть), H — какие руки показывают жест: обе, правая, левая. Жест — клипы Take_Test<жест> в слоях
    /// GestureTest (свой клип на обе руки, на правую и на левую); код ставит номер клипа, его время и вес слоя. Между жестами, кулаком
    /// и игрой кисть всегда проходит через Idle.
    /// С 2026-11-23 «класс» (U и меню «ЖЕСТЫ») — подмена на статичную модель автора (ThumbsUpModel3D, StaticThumbsUp): свободная
    /// рука целиком (предплечье — как при ходе картой, кости пальцев не трогаются) уходит за нижний край кадра и выключается,
    /// модель выезжает в правый нижний угол; U ещё раз (или конец жеста из меню, ход картой, F, J, G) — назад.
    /// </summary>
    [DefaultExecutionOrder(20)]
    public sealed class OwnArms3DView : MonoBehaviour
    {
        private const string Model = "ThreeD/Hands/CardCity_Hands";
        private const string Controller = "ThreeD/Hands/HandsAnimator";
        // Кожа рук: сглаженная молодая (scripts/3d/hands/skin_smooth.py, автор принял 2026-11-16). Прежняя кожа — "ArmSkin":
        // чтобы вернуть её, достаточно поставить это имя.
        private const string SkinMaterial = "ArmSkin_Smooth";
        // Какая рука держит веер, какая свободна (жесты, взятие карты). Должно совпадать с FAN_HAND в scripts/3d/hands/in_game.py:
        // позы в клипах уже стоят на этих сторонах. Автор держит карты левой (2026-11-13).
        public const string FanSide = "_L", FreeSide = "_R";
        // Свободная рука снаружи: +1 справа, -1 слева. По нему отражаются точки, заданные для правой руки.
        private static readonly float Outward = FreeSide == "_R" ? 1f : -1f;
        // Позы для замеров при создании: щипок за карту и поза партии (по ней стоит покой свободного предплечья).
        private const string PinchPose = "Pose_TakeCard", RestPose = "Pose_InGame";
        // Точка привязки веера на кисти с веером: кость из Blender (scripts/3d/hands/in_game.py, place_fan_socket).
        private const string FanSocket = "SocketFan" + FanSide;
        // Слои HandsAnimator: Base — покой свободной руки и жесты, FanHand — рука с веером, Pinch — щипок свободной кисти.
        private const int BaseLayer = 0, PinchLayer = 2;
        private const string HoldingCards = "HoldingCards", Regrip = "Regrip", IdleTap = "IdleTap", GestureState = "Gesture_";
        private static readonly int IdleState = Animator.StringToHash("Idle");
        private static readonly int IdleTapState = Animator.StringToHash("IdleFingerTap");
        private const int ReachStage = 0, GrabStage = 1, CarryStage = 2, ReleaseStage = 3, ReturnStage = 4;
        private const float ReachTime = .42f, GrabTime = .24f, CarryTime = .52f, ReturnTime = .56f;
        // Полная высота карты в веере = 9,5 см: пред-подъём (2,6 см) + строго плоское вытягивание (7,8 см) = 10,4 см —
        // нижний край карты гарантированно выходит выше всех соседних карт веера ДО начала разворота и выноса!
        private const float GrabPull = .078f;
        // Жест, перебитый картой, гаснет за GestureCut. Если аниматор не начал жест за GestureStart, запрос снимается.
        private const float GestureCut = .2f, GestureStart = .5f;
        // Корпус и левый веер плавно разворачиваются вместе со взглядом и плечами (до ±48°),
        // чтобы при повороте к ближним соседям всё тело двигалось цельно и правая рука с жестом никогда не заходила в левую!
        private const float BodyYawShare = .56f, MaxBodyYaw = 48f, BodyFollow = 8f;
        // Дыхание: тело с руками поднимается и опускается на 1,2 мм раз в четыре секунды.
        private const float BreathLift = .0012f, BreathRate = .25f;
        // Хват за открытую верхнюю кромку карты чуть правее середины (на стороне свободной руки):
        // открывает номинал и масть в левом углу и уводит ладонь с рукавом правее и выше соседних карт веера.
        private const float GripShareY = .46f, GripShareX = .14f;
        private const float FingerRadius = .0085f, PalmRadius = .026f;
        // Максимальный комфортный вынос кисти вперёд от правого плеча над столом при выкладывании карты.
        private const float MaxPlayReach = .80f;
        private static readonly string[] Fingertips = { "Thumb", "Index", "Middle" };
        // Плечо — рукав свитера от локтя к плечу, на локте шар той же толщины: срез рукава модели у локтя не попадает в кадр
        // ни при каком повороте головы. Плечо вынесено шире (0,40 м вбок), чтобы при взятии карты предплечье и плечо
        // шли единой мягкой линией без резкого выворота локтя в кадре.
        private static readonly Vector3 Shoulder = new Vector3(.40f, -.25f, -.06f);
        // Рукав модели у локтя ~5,2 см: плечо и шар на локте полностью перекрывают срез рукава без ступеньки.
        private const float UpperArmRadius = .052f;
        // Слой рендера своих рук (кожа, рукава, плечи): на него светит только заполняющий свет со стороны глаз.
        private const uint ArmsLayer = 1u << 1;
        // Заполняющий свет: слабее главного, чуть тёплый, падает со стороны глаз вперёд и вниз, в осях тела.
        private const float FillIntensity = .55f;
        private static readonly Vector3 FillDirection = new Vector3(.1f, -.45f, 1f);
        // Отсвет сукна: слабый тёплый свет снизу, от стола к рукам, в осях тела. Окружающий свет зала снизу почти чёрный,
        // и ладонь, повёрнутая вниз и вбок (рука с веером в хвате ребром большого пальца к играющему), была тёмным пятном.
        private const float BounceIntensity = .3f;
        private static readonly Vector3 BounceDirection = new Vector3(-.15f, .85f, -.35f);
        // Проверка базовых поз: слой HandsAnimator и параметр времени его клипа (0 — Idle, 1 — кулак), см. HandsAnimatorBuilder.
        private const string PoseTestLayerName = "PoseTest", TestClench = "TestClench";
        // Руки встают в позу проверки и уходят из неё за PoseTestBlend; сжатие от Idle до кулака — ClenchTime, как в клипе
        // (18 кадров при 30 в секунду).
        private const float PoseTestBlend = .5f, ClenchTime = .6f;
        // Проверка новых жестов: слой GestureTest, номер состояния (с 1: жест × 3 + руки, порядок TestGestures и TestHandsSuffix)
        // и время клипа.
        private const string GestureTestLayer = "GestureTest", GestureTestId = "TestGesture", GestureTestTime = "TestGestureTime";
        // Длина перехода Idle → жест, как в клипе (scripts/3d/hands/gesture_test.py, 30 кадров в секунду): «класс» 20 кадров,
        // «фу» 24 — предплечье поворачивается на 105°.
        private static readonly float[] GestureTestSeconds = { 20f / 30f, 24f / 30f };
        // Смена жеста в слое — переход аниматора 0,15 с (HandsAnimatorBuilder); время клипа трогается после него.
        private const float GestureTestSwitch = .2f;

        /// <summary>
        /// «Класс» — живая анимация на скелете с формой ThumbsUpSmooth по 3D-модели автора (StaticThumbsUp = false):
        /// правая рука остаётся в кадре, плавно поднимается со стола, сгибает пальцы и показывает «класс» по модели автора,
        /// затем плавно разжимается и возвращается на сукно. true — прежняя подмена на статичную модель с уводом руки за кадр.
        /// </summary>
        public static bool StaticThumbsUp = false;
        // Подмена: рука уходит за ArmAwayTime, модель выезжает за ModelInTime, уезжает за ModelOutTime, рука возвращается за
        // ArmBackTime. «Класс» из меню держится MenuThumbsHold — как держал прежний клип Take_ThumbsUp (кадры 26–90 при 30 в секунду).
        private const float ArmAwayTime = .25f, ModelInTime = .4f, ModelOutTime = .3f, ArmBackTime = .35f, MenuThumbsHold = 2.1f;
        // Путь предплечья от позы аниматора за край кадра: к себе и наружу, сначала с подъёмом над бортом стола, потом вниз —
        // под сукно, за край стола у груди.
        private const float AwayBack = .45f, AwaySide = .08f, AwayLift = .06f, AwayDown = .35f;

        /// <summary>Новые жесты временной проверки (клавиши U, J): состояния слоя GestureTest и клипы Take_Test<жест><руки>.</summary>
        public static readonly string[] TestGestures = { "ThumbsUp", "ThumbsDown" };

        /// <summary>Какие руки показывают жест в проверке: обе, только правая, только левая (клавиша H по кругу).</summary>
        public static readonly string[] TestHandsNames = { "обе", "правая", "левая" };

        /// <summary>Приписка к имени состояния и клипа жеста проверки: обе руки, только правая, только левая (как TestHandsNames).</summary>
        public static readonly string[] TestHandsSuffix = { "", "_R", "_L" };

        /// <summary>Жесты, которые можно показать соперникам: клипы Take_* из scripts/3d/hands/build_tripo_hands.py.</summary>
        public static readonly string[] Gestures = { "ThumbsUp", "ThumbsDown", "Fist", "Point", "MiddleFinger" };

        // Жест для HUD → триггер HandsAnimator; его состояние в слое Base — Gesture_ + триггер.
        private static readonly Dictionary<string, string> Triggers = new Dictionary<string, string>
        {
            ["Point"] = "Point", ["ThumbsUp"] = "ThumbUp", ["ThumbsDown"] = "ThumbDown",
            ["TapTable"] = "Knock", ["Shrug"] = "Shrug", ["Fist"] = "Fist", ["MiddleFinger"] = "MiddleFinger",
        };

        private Animator _animator;
        private int _gestureState;
        private bool _gestureSeen, _gestureCut;
        private float _gestureWait;
        private float _fidgetIn = 9f;
        private float _idleTapIn = 4.5f;
        private float _currentBreath, _restGroundWeight = 1f, _fanEyeLiftWeight = 1f, _gestureEyeLiftWeight;
        private float _pinchWeight;
        private TableCameraController _look;
        private Transform _eye, _body, _forearm, _fanForearm, _pinch;
        private Vector3 _eyeBase, _bodyBase;
        private float _yawBase, _bodyYaw;
        private struct PlayRequest
        {
            public Card3DView Card;
            public Vector3 TargetPos;
            public Quaternion TargetRot;
            public float TargetScale;
        }

        private Vector3 _restPosition, _pinchInForearm, _handForwardInForearm, _handPalmInForearm;
        private Quaternion _restRotation;
        private readonly Queue<PlayRequest> _queue = new Queue<PlayRequest>();
        private readonly List<Collider> _colliders = new List<Collider>();
        private Card3DView _card;
        private int _stage;
        private float _time, _startPinchWeight, _releaseStartPinch;
        private Vector3 _fromPosition, _fromPinch, _pullTarget, _pullExitVel, _layPinch, _tossVel, _returnStartVel, _cardTargetPos;
        private Quaternion _fromRotation, _pullRot, _layRot, _cardTargetRot, _carryCardStartWorldRot;
        private Vector3 _grabGripInCard, _reachCardStartLocalPos, _reachCardLiftLocal;
        private float _grabCardScale, _cardTargetScale, _reachBuriedRatio, _grabPullDist = GrabPull, _grabDuration = GrabTime, _fanArchTopLocalY;
        private readonly Transform[] _elbows = new Transform[2], _forearmTwists = new Transform[2], _upperArms = new Transform[2], _elbowCaps = new Transform[2], _shoulderCaps = new Transform[2], _bodyShoulders = new Transform[2];
        private readonly Mesh[] _sleeveMeshes = new Mesh[2];
        private readonly Vector3[][] _sleeveVerts = new Vector3[2][];
        private readonly Vector2[][] _sleeveUvs = new Vector2[2][];
        private readonly Vector3[] _sleeveBackCenterLocal = new Vector3[2];
        private readonly float[] _sleeveBackRadius = { .044f, .044f };
        private readonly Vector3[] _sleeveBackAxisU = { Vector3.right, Vector3.right };
        private readonly float[] _sleeveBackRadiusU = { .046f, .046f };
        private readonly float[] _sleeveBackRadiusV = { .044f, .044f };
        private float _sleeveUpperScale = 1f;
        private const int SleeveRings = 17, SleeveRadial = 20;
        private int _testLayer = -1;
        private bool _testOn;
        private float _testWeight, _clench, _clenchTarget;
        private string _pendingGesture;
        private readonly List<Renderer> _fanParts = new List<Renderer>(), _hiddenFan = new List<Renderer>();
        // Проверка новых жестов: какой жест просили (null — Idle), какие руки, слой, номер состояния в аниматоре, время клипа,
        // сколько ещё ждать после смены состояния.
        private string _testGesture;
        private int _testHands;
        private int _gestureLayer = -1, _gestureId;
        private float _gestureTime, _gestureSwitchWait;
        private SkinnedMeshRenderer _freeArmMesh;
        private int _fistSmoothIdx = -1, _fistThumbIdx = -1, _thumbsSmoothIdx = -1, _thumbsUpSmoothIdx = -1;
        private int _pointSmoothIdx = -1, _middleFingerSmoothIdx = -1;
        private float _inGameShapeWeight, _gestureLiftWeight;
        // Подмена «класса»: модель, части свободной руки (кожа, рукав, плечо, шар на локте), хотят ли модель в кадре, где рука
        // (0 — где её ставит аниматор, 1 — за краем кадра) и модель (0 — под кадром, 1 — в углу), сколько ещё держать (меню),
        // «класс» ли это из меню.
        private ThumbsUpModel3D _thumbsModel;
        private readonly List<GameObject> _freeArmParts = new List<GameObject>();
        private bool _thumbsWanted, _thumbsMenu;
        private float _armAway, _modelIn, _thumbsHold;

        public Transform FanRoot { get; private set; }

        /// <summary>Пока в начале партии или при доборе карты летят к левой руке, кисть держится поднятой на позиции веера.</summary>
        public bool KeepFanRaised { get; set; }

        public IReadOnlyList<Collider> Colliders => _colliders;

        /// <summary>Сколько карт правая рука сыграла сама: для проверки.</summary>
        public static int Played { get; private set; }

        /// <summary>Сколько жестов доиграно до конца: для проверки.</summary>
        public static int GesturesDone { get; private set; }

        public bool Busy => _card != null || _stage != ReachStage || _queue.Count > 0;

        /// <summary>Вызывается, как только карта полностью вытянута выше веера: оставшиеся карты могут плавно сомкнуть просвет.</summary>
        public event Action CardPulledFromFan;

        /// <summary>Карта ещё находится внутри веера (ждёт очереди, рука тянется к ней или вытягивает её вверх вдоль плоскости веера).</summary>
        public bool IsStillInFan(Card3DView card)
        {
            if (card == null) return false;
            if (_card == card && (_stage == ReachStage || _stage == GrabStage)) return true;
            foreach (var req in _queue)
                if (req.Card == card) return true;
            return false;
        }

        /// <summary>Жест, который сейчас играет, или null.</summary>
        public string Gesture { get; private set; }

        /// <summary>Клавиши проверки базовых поз (F, G) работают только в редакторе и в отладочной сборке.</summary>
        public static bool PoseTestKeys => Application.isEditor || Debug.isDebugBuild;

        /// <summary>Идёт проверка базовых поз: руки в позе проверки, входят в неё или выходят из неё.</summary>
        public bool PoseTestActive => _testOn || _testWeight > 0f;

        /// <summary>Вес слоя проверки (0 — игра, 1 — кисти перед собой) и доля сжатия (0 — Idle, 1 — кулак): для проверки.</summary>
        public float PoseTestWeight => _testWeight;

        public float Clench => _clench;

        /// <summary>Жест проверки, который просили (U, J), или null — расслабленная кисть / кулак.</summary>
        public string TestGesture => _testGesture;

        /// <summary>Какие руки показывают жест проверки: 0 — обе, 1 — правая, 2 — левая (TestHandsNames).</summary>
        public int TestHands => _testHands;

        /// <summary>Время клипа жеста проверки: 0 — Idle, 1 — жест.</summary>
        public float TestGestureTime => _gestureTime;

        /// <summary>Номер состояния жеста проверки в аниматоре: 0 — ещё не выбран, дальше жест × 3 + руки + 1.</summary>
        public int TestGestureState => _gestureId;

        /// <summary>Идёт подмена «класса» на статичную модель: рука уходит, модель в кадре или рука возвращается.</summary>
        public bool ThumbsSwapActive => _thumbsWanted || _armAway > 0f || _modelIn > 0f;

        /// <summary>Модель «класса» в углу кадра и стоит там (просили и доехала).</summary>
        public bool ThumbsModelShown => _thumbsWanted && _modelIn >= 1f;

        /// <summary>Где свободная рука (0 — где её ставит аниматор, 1 — за краем кадра) и модель (0 — под кадром, 1 — в углу).</summary>
        public float ThumbsArmAway => _armAway;

        public float ThumbsModelIn => _modelIn;

        /// <summary>Корень статичной модели «класса» (на камере) и её сетка: для проверки.</summary>
        public Transform ThumbsModel => _thumbsModel.transform;

        public Renderer ThumbsModelRenderer => _thumbsModel.Renderer;

        /// <summary>Части свободной руки, которые выключаются, пока вместо неё модель: для проверки.</summary>
        public IReadOnlyList<GameObject> FreeArmParts => _freeArmParts;

        public static OwnArms3DView Create(Transform eye)
        {
            // Корпус рук остаётся в базовой посадке над столом (SeatBodyY/Z, SeatBodyPitch = 32°),
            // в которой запечены позы контакта с сукном: правая кисть всегда лежит точно на ткани.
            // Левый веер и поднятые жесты мягко переносятся в более высокую камеру игрока (ApplyRaisedSeatRetarget).
            var body = new GameObject("Seated body").transform;
            body.SetPositionAndRotation(new Vector3(0f, Table3DLayout.SeatBodyY, Table3DLayout.SeatBodyZ),
                Quaternion.Euler(Table3DLayout.SeatBodyPitch, eye.eulerAngles.y, 0f));
            var arms = Instantiate(Resources.Load<GameObject>(Model), body, false);
            arms.name = "own-arms";
            // Как у всех моделей стола: разворот ставит пальцы вперёд, левую руку слева.
            arms.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var view = arms.AddComponent<OwnArms3DView>();
            view._eye = eye;
            view._look = eye.GetComponent<TableCameraController>();
            view._body = body;
            view._eyeBase = eye.position;
            view._bodyBase = body.position;
            view._yawBase = eye.eulerAngles.y;
            AddRimLight(body);
            AddFillLight(body);
            AddBounceLight(body);
            view.Dress();
            view.MeasureHands();
            view.StartAnimator();
            view.AddColliders();
            view.BuildUpperArms();
            view._thumbsModel = ThumbsUpModel3D.Create(eye.GetComponent<Camera>(), ArmsLayer);
            return view;
        }

        /// <summary>Правая рука сама вытащит эту карту из веера, понесёт её к targetWorldPos на столе и положит лицом вверх.</summary>
        public void PlayCard(Card3DView card, Vector3? targetWorldPos = null, Quaternion? targetWorldRot = null, float? targetScale = null)
        {
            card.Hold();
            // Сначала руки выходят из проверки поз; карта ждёт в веере, пока они не вернутся (PlayStep).
            StopPoseTest();
            // Карта важнее жеста: жест быстро гаснет, рука идёт за картой. Модель «класса» уезжает, карта ждёт руку (PlayStep).
            if (Gesture != null) StopGesture();
            CancelIdleTap(true);
            HideThumbsModel();
            if (ReducedMotion)
            {
                card.LetGo();
                return;
            }
            _queue.Enqueue(new PlayRequest
            {
                Card = card,
                TargetPos = targetWorldPos ?? new Vector3(Table3DLayout.PairShift, Table3DLayout.TableY + .004f, -.08f),
                TargetRot = targetWorldRot ?? Quaternion.Euler(90f, 0f, 0f),
                TargetScale = targetScale ?? Table3DLayout.TableCardScale,
            });
        }

        /// <summary>Показать жест соперникам: триггер HandsAnimator. Пока правая рука несёт карту, жест не начинается.</summary>
        public bool PlayGesture(string gesture)
        {
            if (Busy || !Triggers.TryGetValue(gesture, out var trigger)) return false;
            // Во время проверки поз жест ждёт, пока руки вернутся к игре: слой проверки закрыл бы его собой.
            if (PoseTestActive)
            {
                StopPoseTest();
                _pendingGesture = gesture;
                return true;
            }
            if (gesture == Gesture) return true;
            foreach (var other in Triggers.Values) _animator.ResetTrigger(other);
            CancelIdleTap(false);
            _gestureSeen = _gestureCut = false;
            _gestureWait = 0f;
            if (StaticThumbsUp && gesture == "ThumbsUp")
            {
                // Прежний жест аниматора гаснет, рука уходит за край кадра, вместо неё — модель на MenuThumbsHold.
                if (Gesture != null) _animator.CrossFadeInFixedTime(IdleState, GestureCut, BaseLayer);
                Gesture = gesture;
                _gestureState = 0;
                _thumbsMenu = true;
                ShowThumbsModel(MenuThumbsHold);
                return true;
            }
            // Другой жест, пока вместо руки модель «класса» (из меню или U): модель уезжает, рука возвращается уже в новый жест.
            HideThumbsModel();
            _thumbsMenu = false;
            _animator.SetTrigger(trigger);
            Gesture = gesture;
            _gestureState = Animator.StringToHash(GestureState + trigger);
            return true;
        }

        /// <summary>
        /// Модель «класса» выезжает в угол кадра (сначала свободная рука уходит за его край) и стоит там hold секунд
        /// (бесконечность — пока не попросят убрать).
        /// </summary>
        private void ShowThumbsModel(float hold)
        {
            _thumbsWanted = true;
            _thumbsHold = hold;
        }

        /// <summary>Модель «класса» уезжает вниз, потом возвращается свободная рука. Если подмены нет — ничего.</summary>
        private void HideThumbsModel() => _thumbsWanted = false;

        /// <summary>U: модель «класса» выезжает и стоит, пока U не нажмут ещё раз; во время выезда U — назад.</summary>
        public void ToggleThumbsModel()
        {
            if (_thumbsWanted)
            {
                if (_thumbsMenu) _gestureCut = true;
                HideThumbsModel();
                return;
            }
            // Пока рука несёт карту или карта ждёт руку, модель не выезжает.
            if (Busy) return;
            // «Класс» из меню, который уже уезжал, U оставляет в кадре — уже как свой: жест из меню на этом закончен.
            if (_thumbsMenu) EndThumbsGesture();
            ShowThumbsModel(float.PositiveInfinity);
        }

        /// <summary>«Класс» из меню закончен: доигранным считается, если его не перебили.</summary>
        private void EndThumbsGesture()
        {
            if (!_gestureCut) GesturesDone++;
            Gesture = null;
            _thumbsMenu = false;
        }

        /// <summary>Новая партия: рука бросает всё, что держала, и возвращается в покой.</summary>
        public void Clear()
        {
            StopPoseTest();
            _pendingGesture = null;
            _queue.Clear();
            _card = null;
            _stage = ReachStage;
            Pinch(0f);
            StopGesture();
            CancelIdleTap(true);
            _idleTapIn = UnityEngine.Random.Range(3.5f, 6.5f);
            ResetThumbsModel();
            _forearm.localPosition = _restPosition;
            _forearm.localRotation = _restRotation;
        }

        /// <summary>
        /// Контровой свет сверху-сзади (за руками, если смотреть из глаз): он светит навстречу игроку и обводит пальцы и края карт,
        /// поэтому силуэт руки читается на тёмно-красном сукне. Ближняя часть стола за его дальностью и не засвечивается. Без теней.
        /// </summary>
        private static void AddRimLight(Transform body)
        {
            var rim = new GameObject("Rim light").AddComponent<Light>();
            rim.transform.SetParent(body, false);
            rim.transform.localPosition = new Vector3(.02f, .3f, .72f);
            rim.transform.localRotation = Quaternion.LookRotation(new Vector3(-.06f, -.05f, .3f) - rim.transform.localPosition);
            rim.type = LightType.Spot;
            rim.spotAngle = 70f;
            rim.innerSpotAngle = 35f;
            rim.range = .95f;
            rim.intensity = 1.5f;
            rim.color = new Color(1f, .9f, .78f);
            rim.shadows = LightShadows.None;
        }

        /// <summary>
        /// Заполняющий свет со стороны глаз — только на свои руки (слой ArmsLayer). Главный свет падает из-за плеча вперёд,
        /// и всё, что смотрит на игрока — ладонь под веером, тыл кисти в «фу», — без него держал только слабый свет зала:
        /// тёмное, с красной каймой рассеивания. Направленный, поэтому ближняя и дальняя рука освещены ровно. Без теней,
        /// карты и стол он не трогает.
        /// </summary>
        private static void AddFillLight(Transform body)
        {
            var fill = new GameObject("Arms fill light").AddComponent<Light>();
            fill.transform.SetParent(body, false);
            fill.transform.localRotation = Quaternion.LookRotation(FillDirection);
            fill.type = LightType.Directional;
            fill.intensity = FillIntensity;
            fill.color = new Color(1f, .95f, .9f);
            fill.shadows = LightShadows.None;
            fill.GetUniversalAdditionalLightData().renderingLayers = ArmsLayer;
        }

        /// <summary>
        /// Отсвет освещённого сукна снизу — только на свои руки (слой ArmsLayer): у живой руки над столом низ кисти подсвечен
        /// столом. Без теней, слабее заполняющего света; карты и стол он не трогает.
        /// </summary>
        private static void AddBounceLight(Transform body)
        {
            var bounce = new GameObject("Arms table bounce light").AddComponent<Light>();
            bounce.transform.SetParent(body, false);
            bounce.transform.localRotation = Quaternion.LookRotation(BounceDirection);
            bounce.type = LightType.Directional;
            bounce.intensity = BounceIntensity;
            bounce.color = new Color(1f, .72f, .6f);
            bounce.shadows = LightShadows.None;
            bounce.GetUniversalAdditionalLightData().renderingLayers = ArmsLayer;
        }

        private static Material MakeSweaterTile(string key, float scaleX, float scaleY)
        {
            var tile = Resources.Load<Texture2D>("ThreeD/Hands/TripoSweater_Tile");
            if (tile == null) return Table3DArt.Sweater;
            var mat = Table3DArt.Lit(key, Color.white, 0f, 0f, tile);
            mat.SetTextureScale("_BaseMap", new Vector2(scaleX, scaleY));
            return mat;
        }

        private void Dress()
        {
            var tripoMap = Resources.Load<Texture2D>("ThreeD/Hands/TripoHands_BaseColor");
            var skin = tripoMap != null
                ? Table3DArt.Lit("tripo hand skin", Color.white, .16f, 0f, tripoMap)
                : Table3DArt.Template(SkinMaterial);
            var sweater = MakeSweaterTile("tripo hand sweater", 1f, 1f);
            foreach (var part in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                part.gameObject.SetActive(!part.name.Contains("Female"));
                part.sharedMaterial = part.name.Contains("Sleeve") ? sweater : skin;
                // Границы модели считаются по позе покоя: без этого руки пропадают, когда камера поворачивается.
                part.updateWhenOffscreen = true;
                part.renderingLayerMask |= ArmsLayer;
                // Кожа и рукав свободной руки (Arm_Male_R, Sleeve_R) выключаются, пока вместо неё модель «класса».
                if (part.name.EndsWith(FreeSide) && !part.name.Contains("Female")) _freeArmParts.Add(part.gameObject);
                if (part.name == "Arm_Male" + FreeSide && part.sharedMesh != null)
                {
                    _freeArmMesh = part;
                    _fistSmoothIdx = part.sharedMesh.GetBlendShapeIndex("FistSmooth");
                    _fistThumbIdx = part.sharedMesh.GetBlendShapeIndex("FistThumb");
                    _thumbsSmoothIdx = part.sharedMesh.GetBlendShapeIndex("ThumbsSmooth");
                    // С 2026-11-23 у каждого жеста своя корректирующая форма кожи (объёмные суставы без коллизий).
                    _thumbsUpSmoothIdx = part.sharedMesh.GetBlendShapeIndex("ThumbsUpSmooth");
                    _pointSmoothIdx = part.sharedMesh.GetBlendShapeIndex("PointSmooth");
                    _middleFingerSmoothIdx = part.sharedMesh.GetBlendShapeIndex("MiddleFingerSmooth");
                }
            }
        }

        /// <summary>
        /// Подстраивает кожу кистей от первого лица (смуглая для avatar_08, зрелая для 03/06, светлая для женских)
        /// и полноту плеча рукава под выбранного игроком персонажа!
        /// </summary>
        public void ApplyCharacterStyle(string avatarId)
        {
            var tone = AvatarCatalog.HandSkinToneOf(avatarId);
            var arch = AvatarCatalog.BodyArchetypeOf(avatarId);
            var texPath = AvatarCatalog.HandSkinTextureResourceFor(tone);
            var tex = Resources.Load<Texture2D>(texPath) ?? Resources.Load<Texture2D>("ThreeD/Hands/TripoHands_BaseColor");
            if (tex != null)
            {
                var smoothness = tone == AvatarCatalog.HandSkinTone.Aged ? .12f : .16f;
                var skin = Table3DArt.Lit("tripo hand skin " + tone, Color.white, smoothness, 0f, tex);
                foreach (var part in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (!part.name.Contains("Sleeve") && !part.name.Contains("Female"))
                        part.sharedMaterial = skin;
                }
            }
            _sleeveUpperScale = arch == AvatarCatalog.BodyArchetype.MaleHeavy ? 1.15f
                : (arch == AvatarCatalog.BodyArchetype.Female || arch == AvatarCatalog.BodyArchetype.FemaleElder) ? .90f
                : 1f;
            PlaceUpperArms();
        }

        /// <summary>
        /// Замеры до запуска аниматора, по двум позам из Blender: где щипок свободной руки и как он развёрнут относительно предплечья
        /// (поза Pose_TakeCard), где предплечье в покое (поза партии Pose_InGame). Веер вешается на кость веера другой кисти.
        /// </summary>
        private void MeasureHands()
        {
            _animator = GetComponent<Animator>();
            // Пока руки не видны камере, Animator с отсечением по умолчанию не пишет позу в кости.
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var clips = Resources.LoadAll<AnimationClip>(Model);
            _forearm = Bone("Forearm" + FreeSide);
            _fanForearm = Bone("Forearm" + FanSide);
            var thumb = Bone("TipThumb" + FreeSide);
            var index = Bone("TipIndex" + FreeSide);
            var hand = Bone("Hand" + FreeSide);
            var midMcp = Bone("Middle01" + FreeSide);
            var idxMcp = Bone("Index01" + FreeSide);
            var pkyMcp = Bone("Pinky01" + FreeSide);
            // Само предплечье ставит этот скрипт, а точка щипка и точные анатомические оси ладони (вдоль пальцев + нормаль ладони)
            // меряются в позе хвата: рука всегда берёт карту ладонью и большим пальцем спереди, а не тыльной стороной!
            Sample(clips, PinchPose);
            var pinch = (thumb.position + index.position) / 2;
            _pinchInForearm = _forearm.InverseTransformPoint(pinch);
            _handForwardInForearm = _forearm.InverseTransformDirection(midMcp.position - hand.position).normalized;
            var radial = _forearm.InverseTransformDirection(idxMcp.position - pkyMcp.position).normalized;
            var palm = Vector3.Cross(_handForwardInForearm, radial).normalized;
            if (Vector3.Dot(palm, _pinchInForearm - _forearm.InverseTransformPoint(hand.position)) < 0f) palm = -palm;
            _handPalmInForearm = palm;
            _pinch = new GameObject("Pinch").transform;
            _pinch.SetParent(hand, false);
            _pinch.position = pinch;
            Sample(clips, RestPose);
            _restPosition = _forearm.localPosition;
            _restRotation = _forearm.localRotation;

            // Веер — на точке привязки кисти с веером: её положение и оси заданы в Blender по позе хвата, здесь ничего не считается.
            FanRoot = new GameObject("Fan").transform;
            FanRoot.SetParent(Bone(FanSocket), false);
        }

        /// <summary>Ставит руки в позу из клипа на один кадр: граф из одного клипа на Animator модели, потом удаляется.</summary>
        private void Sample(AnimationClip[] clips, string name)
        {
            var clip = clips.First(c => c.name.EndsWith("|" + name) || c.name == name);
            var graph = PlayableGraph.Create("Arms pose sample");
            try
            {
                AnimationPlayableOutput.Create(graph, "Sample", _animator).SetSourcePlayable(AnimationClipPlayable.Create(graph, clip));
                graph.Play();
                graph.Evaluate(0f);
            }
            finally
            {
                graph.Destroy();
            }
        }

        /// <summary>Animator Controller рук: с первого кадра левая рука держит веер, правая лежит на сукне.</summary>
        private void StartAnimator()
        {
            _animator.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>(Controller);
            _animator.SetBool(HoldingCards, true);
            _testLayer = _animator.GetLayerIndex(PoseTestLayerName);
            _gestureLayer = _animator.GetLayerIndex(GestureTestLayer);
            _idleTapIn = UnityEngine.Random.Range(3.5f, 6.5f);
            _animator.Update(0f);
        }

        /// <summary>
        /// F: в первый раз руки встают перед собой в расслабленную кисть и сразу сжимаются в кулак, дальше F по очереди
        /// разжимает и сжимает кисти. Веер на время проверки прячется. Без слоя PoseTest в контроллере — false.
        /// </summary>
        public bool TogglePoseTest()
        {
            if (_testLayer < 0) return false;
            // Из жеста проверки — в кулак: жест сначала уходит в Idle (UpdatePoseTest), потом кисть сжимается.
            if (_testGesture != null)
            {
                _testGesture = null;
                _clenchTarget = 1f;
                return true;
            }
            if (!_testOn)
            {
                _testOn = true;
                _clenchTarget = 1f;
                return true;
            }
            _clenchTarget = _clenchTarget > .5f ? 0f : 1f;
            return true;
        }

        /// <summary>
        /// U, J: руки встают перед собой (если ещё не в проверке) и показывают жест gesture из TestGestures; тот же жест ещё
        /// раз — назад в расслабленную кисть. Из кулака и из другого жеста кисть сначала проходит через Idle. Без слоёв
        /// GestureTest в контроллере — false.
        /// </summary>
        public bool ShowTestGesture(string gesture)
        {
            if (_testLayer < 0 || _gestureLayer < 0 || Array.IndexOf(TestGestures, gesture) < 0) return false;
            _clenchTarget = 0f;
            if (!_testOn)
            {
                _testOn = true;
                _testGesture = gesture;
                return true;
            }
            _testGesture = _testGesture == gesture ? null : gesture;
            return true;
        }

        /// <summary>H: какие руки показывают жест проверки — обе, правая, левая, по кругу.</summary>
        public void CycleTestHands() => _testHands = (_testHands + 1) % TestHandsNames.Length;

        /// <summary>G, ход картой, жест, новая партия: кисти разжимаются в Idle, затем руки возвращаются к вееру и на сукно.</summary>
        public void StopPoseTest()
        {
            _testOn = false;
            _clenchTarget = 0f;
            _testGesture = null;
        }

        /// <summary>
        /// Вес слоя PoseTest и время его клипа. Сжимается кисть только в позе проверки, а уходит из неё через Idle: напрямую
        /// кулак смешивался бы с лежащей на сукне кистью, и пальцы на полпути проходили бы сквозь стол. Так же и жесты
        /// проверки: показываются, только когда руки в позе проверки и кисть разжата, а уходят из неё через Idle.
        /// </summary>
        private void UpdatePoseTest()
        {
            if (_testLayer < 0) return;
            var step = Time.deltaTime;
            var gestureIdle = UpdateTestGestures(step);
            // Кулак — только когда ни одна рука не показывает жест и жест не просили.
            var clenchGoal = _testOn && _testGesture == null && gestureIdle ? _clenchTarget : 0f;
            if (!_testOn || _testWeight >= 1f) _clench = Mathf.MoveTowards(_clench, clenchGoal, step / ClenchTime);
            _testWeight = Mathf.MoveTowards(_testWeight, _testOn || _clench > 0f || !gestureIdle ? 1f : 0f, step / PoseTestBlend);
            _animator.SetFloat(TestClench, _clench);
            _animator.SetLayerWeight(_testLayer, Mathf.SmoothStep(0f, 1f, _testWeight));
            HideFan(_testWeight > 0f);
            if (_testWeight > 0f || _pendingGesture == null) return;
            var gesture = _pendingGesture;
            _pendingGesture = null;
            PlayGesture(gesture);
        }

        /// <summary>
        /// Жест проверки: номер состояния (жест и руки) и время клипа в слое GestureTest. Жест идёт, только когда руки в позе
        /// проверки (вес 1) и кисть разжата; номер меняется только в Idle (время 0), где все клипы совпадают с Idle проверки
        /// кулака, — поэтому и вес слоя там включается и выключается незаметно. Возвращает true, если кисти в Idle.
        /// </summary>
        private bool UpdateTestGestures(float step)
        {
            if (_gestureLayer < 0) return true;
            var ready = _testOn && _testWeight >= 1f && _clench <= 0f;
            var wanted = _testGesture == null ? 0 : Array.IndexOf(TestGestures, _testGesture) * TestHandsSuffix.Length + _testHands + 1;
            var shown = ready && wanted > 0;
            if (shown && _gestureId != wanted && _gestureTime <= 0f)
            {
                _gestureId = wanted;
                _gestureSwitchWait = GestureTestSwitch;
                _animator.SetInteger(GestureTestId, wanted);
            }
            _gestureSwitchWait = Mathf.Max(0f, _gestureSwitchWait - step);
            var goal = shown && _gestureId == wanted && _gestureSwitchWait <= 0f ? 1f : 0f;
            var seconds = GestureTestSeconds[Mathf.Max(0, _gestureId - 1) / TestHandsSuffix.Length];
            _gestureTime = Mathf.MoveTowards(_gestureTime, goal, step / seconds);
            _animator.SetFloat(GestureTestTime, _gestureTime);
            _animator.SetLayerWeight(_gestureLayer, ready || _gestureTime > 0f ? 1f : 0f);
            return _gestureTime <= 0f;
        }

        /// <summary>Веер в проверке не нужен: кисть с ним ушла, карты прячутся (и те, что придут в веер за это время).</summary>
        private void HideFan(bool hide)
        {
            if (hide)
            {
                FanRoot.GetComponentsInChildren(_fanParts);
                foreach (var part in _fanParts)
                {
                    if (part.forceRenderingOff) continue;
                    part.forceRenderingOff = true;
                    _hiddenFan.Add(part);
                }
                return;
            }
            if (_hiddenFan.Count == 0) return;
            foreach (var part in _hiddenFan)
                if (part != null) part.forceRenderingOff = false;
            _hiddenFan.Clear();
        }

        /// <summary>Подсказка, пока идёт проверка поз: только в редакторе и отладочной сборке.</summary>
        private void OnGUI()
        {
            if (!PoseTestKeys || !PoseTestActive) return;
            var size = Mathf.Max(14, Screen.height / 42);
            var style = new GUIStyle(GUI.skin.label) { fontSize = size, normal = { textColor = new Color(1f, .91f, .73f) } };
            GUI.Label(new Rect(size, Screen.height * .36f, size * 30, size * 8),
                "Проверка рук\nF — кулак / расслабленная кисть\nU — «класс», J — «фу» (ещё раз — назад)\nH — руки: "
                + TestHandsNames[_testHands] + "\nG — назад к игре", style);
        }

        /// <summary>Капсулы на подушечках трёх пальцев и на ладони: о них ударяется падающая карта. Двигает их скелет, а не физика.</summary>
        private void AddColliders()
        {
            var rigidbody = gameObject.AddComponent<Rigidbody>();
            rigidbody.isKinematic = true;
            foreach (var side in new[] { "_L", "_R" })
            {
                foreach (var finger in Fingertips)
                    AddCapsule(Bone(finger + "03" + side), Bone("Tip" + finger + side).position, FingerRadius);
                AddCapsule(Bone("Hand" + side), Bone("Middle01" + side).position, PalmRadius);
            }
        }

        private void AddCapsule(Transform bone, Vector3 end, float radius)
        {
            var holder = new GameObject(bone.name + " collider").transform;
            holder.SetParent(bone, false);
            var along = end - bone.position;
            holder.SetPositionAndRotation(bone.position + along / 2, Quaternion.LookRotation(along));
            holder.localScale = Vector3.one;
            var scale = holder.lossyScale.x;
            var capsule = holder.gameObject.AddComponent<CapsuleCollider>();
            capsule.direction = 2;
            capsule.radius = radius / scale;
            capsule.height = (along.magnitude + radius * 2) / scale;
            capsule.contactOffset = .001f;
            _colliders.Add(capsule);
        }

        private Transform Bone(string name) => GetComponentsInChildren<Transform>(true).First(t => t.name == name);

        /// <summary>Параметры аниматора: держит ли левая рука карты, идёт ли ещё жест, пора ли перехватить веер.</summary>
        private void Update()
        {
            // В веере нет ни одной карты — левая кисть ложится на сукно (состояние OnTable), появились — снова держит веер.
            var holding = FanRoot.childCount > 0;
            _animator.SetBool(HoldingCards, holding);
            TrackGesture();
            PoseTestKeysPressed();
            UpdatePoseTest();
            PreviewPinchWeight();
            // Живая рука не стоит как кукла: время от времени левая перехватывает веер, а правая на столе
            // изредка нетерпеливо постукивает пальцами по сукне (если не играет жест и не идёт ход картой).
            if (Gesture != null || Busy || ReducedMotion || PoseTestActive || ThumbsSwapActive) return;
            UpdateIdleFingerTap();
            if (!holding) return;
            _fidgetIn -= Time.deltaTime;
            if (_fidgetIn > 0f) return;
            _fidgetIn = UnityEngine.Random.Range(10f, 18f);
            _animator.SetTrigger(Regrip);
        }

        /// <summary>Выставляет вес слоя щипка ДО шага Animator между Update и LateUpdate, убирая 1-кадровую задержку костей пальцев.</summary>
        private void PreviewPinchWeight()
        {
            if (_card == null && _stage == ReachStage) return;
            var nextTime = _time + Time.deltaTime;
            float w = _pinchWeight;
            if (_stage == ReachStage)
                w = Mathf.Lerp(_startPinchWeight, 1f, Smoother(Mathf.Clamp01((nextTime / ReachTime) / .92f)));
            else if (_stage == GrabStage)
                w = 1f;
            else if (_stage == CarryStage)
            {
                var u = Mathf.Clamp01((Mathf.Clamp01(nextTime / CarryTime) - .80f) / .20f);
                w = Mathf.Lerp(1f, .65f, u * u * (3f - 2f * u));
            }
            else
            {
                var u = Mathf.Clamp01(nextTime / .22f);
                w = Mathf.Lerp(_releaseStartPinch, 0f, u * u * (3f - 2f * u));
            }
            Pinch(w);
        }

        private void UpdateIdleFingerTap()
        {
            if (_animator.IsInTransition(BaseLayer) || _animator.GetCurrentAnimatorStateInfo(BaseLayer).shortNameHash != IdleState) return;
            _idleTapIn -= Time.deltaTime;
            if (_idleTapIn > 0f) return;
            _idleTapIn = UnityEngine.Random.Range(6.5f, 13f);
            _animator.SetTrigger(IdleTap);
        }

        private void CancelIdleTap(bool crossfadeToIdle)
        {
            if (_animator == null) return;
            _animator.ResetTrigger(IdleTap);
            _idleTapIn = UnityEngine.Random.Range(6f, 11f);
            if (!crossfadeToIdle) return;
            var cur = _animator.GetCurrentAnimatorStateInfo(BaseLayer);
            var nxt = _animator.GetNextAnimatorStateInfo(BaseLayer);
            if (cur.shortNameHash == IdleTapState || (_animator.IsInTransition(BaseLayer) && nxt.shortNameHash == IdleTapState))
                _animator.CrossFadeInFixedTime(IdleState, .12f, BaseLayer);
        }

        /// <summary>
        /// F — кулак или расслабленная кисть, U — «класс», J — «фу», H — какие руки, G — назад к игре. В 3D-столе эти клавиши
        /// свободны (жесты партии — 1–6).
        /// </summary>
        private void PoseTestKeysPressed()
        {
            var keyboard = PoseTestKeys ? Keyboard.current : null;
            if (keyboard == null) return;
            // F, J, G — про руки на скелете: модель «класса» сначала уезжает, рука возвращается.
            if (keyboard.fKey.wasPressedThisFrame || keyboard.jKey.wasPressedThisFrame || keyboard.gKey.wasPressedThisFrame) HideThumbsModel();
            if (keyboard.fKey.wasPressedThisFrame) TogglePoseTest();
            if (keyboard.uKey.wasPressedThisFrame)
            {
                if (StaticThumbsUp) ToggleThumbsModel();
                else ShowTestGesture("ThumbsUp");
            }
            if (keyboard.jKey.wasPressedThisFrame) ShowTestGesture("ThumbsDown");
            if (keyboard.hKey.wasPressedThisFrame) CycleTestHands();
            if (keyboard.gKey.wasPressedThisFrame) StopPoseTest();
        }

        /// <summary>Жест закончен, когда слой Base вошёл в его состояние и вернулся из него в покой.</summary>
        private void TrackGesture()
        {
            // «Класс»-модель аниматор не играет: её жест заканчивает UpdateThumbsModel.
            if (Gesture == null || _thumbsMenu) return;
            var current = _animator.GetCurrentAnimatorStateInfo(BaseLayer);
            var next = _animator.GetNextAnimatorStateInfo(BaseLayer);
            if (current.shortNameHash == _gestureState || _animator.IsInTransition(BaseLayer) && next.shortNameHash == _gestureState)
            {
                _gestureSeen = true;
                return;
            }
            _gestureWait += Time.deltaTime;
            if (!_gestureSeen && _gestureWait < GestureStart) return;
            if (_gestureSeen && !_gestureCut) GesturesDone++;
            if (!_gestureSeen) _animator.ResetTrigger(Triggers[Gesture]);
            Gesture = null;
        }

        /// <summary>Жест гаснет за GestureCut: слой Base плавно уходит в покой. Доигранным он не считается.</summary>
        private void StopGesture()
        {
            foreach (var trigger in Triggers.Values) _animator.ResetTrigger(trigger);
            if (Gesture == null) return;
            _gestureCut = true;
            // «Класс»-модель: модель уезжает, рука возвращается; жест закончится, когда она вернётся.
            if (_thumbsMenu)
            {
                HideThumbsModel();
                return;
            }
            _animator.CrossFadeInFixedTime(IdleState, GestureCut, BaseLayer);
        }

        private void LateUpdate()
        {
            FollowEye(Follow(BodyFollow));
            GroundRestingFreeHand();
            ApplyRaisedSeatRetarget();
            TuckFanDuringZoom();
            PlayStep();
            UpdateThumbsModel();
            ApplyGestureGazeLift();
            PlaceUpperArms();
            ApplyInGameGestureShape();
        }

        /// <summary>Пока правая рука спокойно лежит на сукне, дыхание не поднимает её со стола, а сильный поворот корпуса оставляет кисть на сукне.</summary>
        private void GroundRestingFreeHand()
        {
            var resting = Gesture == null && !Busy && !PoseTestActive && !ThumbsSwapActive;
            _restGroundWeight = Mathf.MoveTowards(_restGroundWeight, resting ? 1f : 0f, Time.deltaTime * 6f);
            if (_restGroundWeight > 1e-4f && _forearm != null)
            {
                if (Mathf.Abs(_currentBreath) > 1e-6f)
                    _forearm.position -= Vector3.up * (_currentBreath * _restGroundWeight);
                var excessYaw = _bodyYaw - Mathf.Clamp(_bodyYaw, -12f, 12f);
                if (Mathf.Abs(excessYaw) > .05f)
                {
                    var undo = Quaternion.AngleAxis(-excessYaw * _restGroundWeight, Vector3.up);
                    _forearm.position = _body.position + undo * (_forearm.position - _body.position);
                    _forearm.rotation = undo * _forearm.rotation;
                }
            }
        }

        /// <summary>
        /// Камера игрока посажена выше базового корпуса (EyeY/EyeZ, StartPitch) для лучшего обзора карт на столе.
        /// Левую руку с веером и поднятые жесты мягко переносим в кадр приподнятых глаз, а отдыхающая правая рука
        /// остаётся в координатах стола (weight = 0) — всегда плотно на сукне.
        /// </summary>
        private void ApplyRaisedSeatRetarget()
        {
            var holdingFan = !PoseTestActive && FanRoot != null && (FanRoot.childCount > 0 || KeepFanRaised);
            _fanEyeLiftWeight = Mathf.MoveTowards(_fanEyeLiftWeight, holdingFan ? 1f : 0f, Time.deltaTime * 5f);
            if (_fanForearm != null && _fanEyeLiftWeight > 1e-4f)
            {
                RetargetBoneToRaisedEye(_fanForearm, _fanEyeLiftWeight);
                // Спокойная посадка веера в нижнем левом углу под золотой FOV (62°/57°):
                // все карты чёткие и удобно нажимаются, но рука не лезет на соседа напротив!
                var eyeRestRot = Quaternion.Euler(TableCameraController.StartPitch, _yawBase + _bodyYaw, 0f);
                _fanForearm.position += eyeRestRot * new Vector3(-.030f * Outward, -.058f, .084f) * _fanEyeLiftWeight;
            }

            var gestureTarget = 0f;
            if (Gesture != null && !_gestureCut && !PoseTestActive && !Busy && !ThumbsSwapActive && Gesture != "TapTable")
            {
                var current = _animator.GetCurrentAnimatorStateInfo(BaseLayer);
                if (current.shortNameHash == _gestureState)
                {
                    var t = Mathf.Clamp01(current.normalizedTime);
                    if (t > .02f && t < .96f)
                        gestureTarget = Mathf.SmoothStep(0f, 1f, t < .16f ? (t - .02f) / .14f : t > .82f ? (.96f - t) / .14f : 1f);
                }
            }
            _gestureEyeLiftWeight = Mathf.MoveTowards(_gestureEyeLiftWeight, gestureTarget, Time.deltaTime * 6f);
            if (_forearm != null && _gestureEyeLiftWeight > 1e-4f)
            {
                RetargetBoneToRaisedEye(_forearm, _gestureEyeLiftWeight);
                // Умеренный плотный вынос жеста вперёд (+6.0 см) и чуть ниже линии взгляда (не закрывает лица друзей напротив)!
                var gestureFwd = Quaternion.Euler(TableCameraController.StartPitch * .65f, _yawBase + _bodyYaw, 0f) * Vector3.forward;
                _forearm.position += (gestureFwd * .060f + Vector3.down * .022f) * _gestureEyeLiftWeight;
            }
        }

        private void RetargetBoneToRaisedEye(Transform bone, float weight)
        {
            var oldRot = _body.rotation;
            var oldPos = _body.position;
            var turn = Quaternion.Euler(0f, _yawBase + _bodyYaw, 0f);
            var eyeDelta = new Vector3(0f, Table3DLayout.EyeY - Table3DLayout.SeatBodyY, Table3DLayout.EyeZ - Table3DLayout.SeatBodyZ);
            var newPos = oldPos + turn * eyeDelta;
            var newRot = Quaternion.Euler(TableCameraController.StartPitch, _yawBase + _bodyYaw, 0f);

            var localPos = Quaternion.Inverse(oldRot) * (bone.position - oldPos);
            var localRot = Quaternion.Inverse(oldRot) * bone.rotation;
            var liftedPos = newPos + newRot * localPos;
            var liftedRot = newRot * localRot;
            bone.SetPositionAndRotation(Vector3.Lerp(bone.position, liftedPos, weight), Quaternion.Slerp(bone.rotation, liftedRot, weight));
        }

        /// <summary>При приближении камеры к центру стола («БЛИЖЕ») веер в левой руке мягко опускается в нижний угол,
        /// освобождая обзор всех карт на столе, но оставаясь на экране для выбора и хода.</summary>
        private void TuckFanDuringZoom()
        {
            if (_look == null || _fanForearm == null || PoseTestActive) return;
            var zoom = _look.ZoomRatio;
            if (zoom <= 1e-4f) return;
            var eyeDelta = _eye.position - _eyeBase;
            var bodyHoriz = Vector3.ProjectOnPlane(eyeDelta, Vector3.up) * (1f - zoom);
            var shift = (eyeDelta - bodyHoriz) + _eye.TransformDirection(new Vector3(0f, -.068f, .105f)) * zoom;
            _fanForearm.position += shift;
        }

        /// <summary>
        /// Когда игрок смотрит вверх на соперника (например, на Петра) и показывает жест («Класс» и т. п.),
        /// свободная рука плавно приподнимается вслед за взглядом, чтобы жест оставался в поле зрения, а не обрезался у низа экрана
        /// и не прятался за кнопкой «БЕРУ»/«ПАС». В покое и при взгляде на стол смещение равно 0 — рука лежит точно на сукне.
        /// </summary>
        private void ApplyGestureGazeLift()
        {
            var target = 0f;
            if (Gesture != null && !_gestureCut && !PoseTestActive && !Busy && !ThumbsSwapActive && Gesture != "TapTable")
            {
                var current = _animator.GetCurrentAnimatorStateInfo(BaseLayer);
                if (current.shortNameHash == _gestureState)
                {
                    var t = Mathf.Clamp01(current.normalizedTime);
                    if (t > .04f && t < .94f)
                        target = Mathf.SmoothStep(0f, 1f, t < .18f ? (t - .04f) / .14f : t > .80f ? (.94f - t) / .14f : 1f);
                }
            }
            _gestureLiftWeight = Mathf.MoveTowards(_gestureLiftWeight, target, Time.deltaTime * 5f);
            if (_gestureLiftWeight <= 1e-4f || _forearm == null || _eye == null) return;
            var eyePitch = Mathf.DeltaAngle(0f, _eye.eulerAngles.x);
            var upPitch = Mathf.Clamp(TableCameraController.StartPitch - eyePitch, 0f, 28f);
            // Симметричная доводка жеста в обе стороны (и вправо на Ельнура, и влево на Аслана!):
            // при взгляде влево левый веер чуть мягко сдвигается влево-вниз, открывая чистое место для правой руки прямо напротив соседа!
            var extraYaw = Mathf.Clamp(Mathf.DeltaAngle(_yawBase + _bodyYaw, _eye.eulerAngles.y), -48f, 44f);
            if (upPitch <= .05f && Mathf.Abs(extraYaw) <= .05f) return;
            var w = _gestureLiftWeight;
            var pivot = _eye.position + Vector3.down * .18f;
            var yawShare = extraYaw < 0f ? .82f : .66f;
            var posRot = Quaternion.AngleAxis(extraYaw * yawShare * w, Vector3.up) * Quaternion.AngleAxis(-upPitch * .68f * w, _eye.right);
            var handRot = Quaternion.AngleAxis(extraYaw * .78f * w, Vector3.up) * Quaternion.AngleAxis(-upPitch * .22f * w, _eye.right);
            _forearm.position = pivot + posRot * (_forearm.position - pivot);
            _forearm.rotation = handRot * _forearm.rotation;
            if (extraYaw < -6f && _fanForearm != null)
            {
                var leftFactor = Mathf.Clamp01((-extraYaw - 6f) / 34f) * w;
                // При жесте влево мягко отводим левую руку с веером чуть ниже и левее — правая рука докручивается прямо на Аслана и не задевает карты!
                _fanForearm.position += (-_eye.right * .068f - Vector3.up * .048f) * leftFactor;
                _forearm.position += (-_eye.right * .028f + Vector3.up * .014f) * leftFactor;
            }
        }

        /// <summary>
        /// Подмена «класса» по шагам: просили — свободная рука уходит за край кадра (ArmAwayTime) и выключается, модель
        /// выезжает в угол (ModelInTime) и стоит; не просят — модель уезжает вниз (ModelOutTime) и выключается, рука
        /// включается и возвращается туда, где её ставит аниматор (ArmBackTime). Время в меню вышло — модель уезжает сама.
        /// </summary>
        private void UpdateThumbsModel()
        {
            if (!ThumbsSwapActive) return;
            var step = ReducedMotion ? 1f : Time.deltaTime;
            if (_thumbsWanted)
            {
                if (_armAway < 1f) _armAway = Mathf.MoveTowards(_armAway, 1f, step / ArmAwayTime);
                else if (_modelIn < 1f) _modelIn = Mathf.MoveTowards(_modelIn, 1f, step / ModelInTime);
                else
                {
                    _thumbsHold -= Time.deltaTime;
                    if (_thumbsHold <= 0f) HideThumbsModel();
                }
            }
            else if (_modelIn > 0f) _modelIn = Mathf.MoveTowards(_modelIn, 0f, step / ModelOutTime);
            else _armAway = Mathf.MoveTowards(_armAway, 0f, step / ArmBackTime);

            // Рука выключена, пока она за краем кадра и там же ждёт модель; модель включена, пока она не под кадром.
            ShowFreeArm(_armAway < 1f || !_thumbsWanted && _modelIn <= 0f);
            var model = _modelIn > 0f || _thumbsWanted && _armAway >= 1f;
            if (_thumbsModel.gameObject.activeSelf != model) _thumbsModel.gameObject.SetActive(model);
            if (model) _thumbsModel.Place(_modelIn);
            if (_armAway > 0f) _forearm.position += FreeArmAway(_armAway);
            if (!ThumbsSwapActive && _thumbsMenu) EndThumbsGesture();
        }

        /// <summary>
        /// Сдвиг предплечья свободной руки от позы аниматора, away от 0 до 1: к себе и наружу, в начале с подъёмом над бортом
        /// стола, к концу вниз — за край кадра и под край стола у груди.
        /// </summary>
        private Vector3 FreeArmAway(float away)
        {
            var back = -Vector3.ProjectOnPlane(_body.forward, Vector3.up).normalized;
            var side = Vector3.ProjectOnPlane(_body.right, Vector3.up).normalized * Outward;
            // Вниз — только во второй половине пути, когда кисть уже над краем стола у груди: так она проходит над бортом.
            var along = Mathf.SmoothStep(0f, 1f, away / .7f);
            var lift = Mathf.Sin(Mathf.PI * Mathf.Clamp01(away / .9f));
            var down = Mathf.SmoothStep(0f, 1f, (away - .5f) / .5f);
            return back * (AwayBack * along) + side * (AwaySide * along) + Vector3.up * (AwayLift * lift - AwayDown * down);
        }

        private void ShowFreeArm(bool show)
        {
            foreach (var part in _freeArmParts)
                if (part.activeSelf != show) part.SetActive(show);
            var free = FreeSide == "_L" ? 0 : 1;
            if (_upperArms[free] != null && _upperArms[free].gameObject.activeSelf != show)
            {
                _upperArms[free].gameObject.SetActive(show);
                if (_elbowCaps[free] != null) _elbowCaps[free].gameObject.SetActive(show);
                if (_shoulderCaps[free] != null) _shoulderCaps[free].gameObject.SetActive(show);
            }
        }

        /// <summary>Новая партия: подмены нет сразу — рука на месте и включена, модель выключена.</summary>
        private void ResetThumbsModel()
        {
            _thumbsWanted = false;
            _armAway = _modelIn = 0f;
            ShowFreeArm(true);
            if (_thumbsModel != null) _thumbsModel.gameObject.SetActive(false);
            if (_thumbsMenu) EndThumbsGesture();
        }

        /// <summary>
        /// В обычной партии слой FanHand (вес 1, без маски на формы кожи) перекрывает веса форм слоя Base нулями из Take_Idle.
        /// Поэтому пока свободная рука показывает жест из меню «ЖЕСТЫ» (Take_ThumbsUp, Take_ThumbsDown, Take_Fist), вес
        /// соответствующей корректирующей формы на ней плавно выставляется поверх аниматора. В режиме проверки (PoseTestActive)
        /// веса ведут сами клипы слоёв PoseTest и GestureTest.
        /// </summary>
        private void ApplyInGameGestureShape()
        {
            if (PoseTestActive || _freeArmMesh == null)
            {
                _inGameShapeWeight = 0f;
                return;
            }
            var target = 0f;
            if (Gesture != null && !_gestureCut)
            {
                var current = _animator.GetCurrentAnimatorStateInfo(BaseLayer);
                if (current.shortNameHash == _gestureState)
                {
                    var t = Mathf.Clamp01(current.normalizedTime);
                    if (t > .09f && t < .90f)
                    {
                        var s = Mathf.SmoothStep(0f, 1f, t < .20f ? (t - .09f) / .11f : t > .82f ? (.90f - t) / .08f : 1f);
                        // Квадрат плавного хода — как в клипах проверки: форма входит точно к концу сжатия пальцев (кадр 22).
                        target = 100f * s * s;
                    }
                }
            }
            if (target <= 0f && _inGameShapeWeight <= 0f) return;
            _inGameShapeWeight = Mathf.MoveTowards(_inGameShapeWeight, target, Time.deltaTime * 600f);
            var isFist = Gesture == "Fist";
            var isPoint = Gesture == "Point";
            var isMiddleFinger = Gesture == "MiddleFinger";
            // «Класс» ведёт свою форму ThumbsUpSmooth (в модели без неё — общую ThumbsSmooth, как раньше), «фу» — ThumbsSmooth.
            var thumbsUpOwn = Gesture == "ThumbsUp" && _thumbsUpSmoothIdx >= 0;
            var isThumbs = (Gesture == "ThumbsUp" && !thumbsUpOwn) || Gesture == "ThumbsDown";
            if (_fistSmoothIdx >= 0) _freeArmMesh.SetBlendShapeWeight(_fistSmoothIdx, isFist ? _inGameShapeWeight : 0f);
            if (_fistThumbIdx >= 0) _freeArmMesh.SetBlendShapeWeight(_fistThumbIdx, isFist ? _inGameShapeWeight : 0f);
            if (_thumbsSmoothIdx >= 0) _freeArmMesh.SetBlendShapeWeight(_thumbsSmoothIdx, isThumbs ? _inGameShapeWeight : 0f);
            if (_thumbsUpSmoothIdx >= 0) _freeArmMesh.SetBlendShapeWeight(_thumbsUpSmoothIdx, thumbsUpOwn ? _inGameShapeWeight : 0f);
            if (_pointSmoothIdx >= 0) _freeArmMesh.SetBlendShapeWeight(_pointSmoothIdx, isPoint ? _inGameShapeWeight : 0f);
            if (_middleFingerSmoothIdx >= 0) _freeArmMesh.SetBlendShapeWeight(_middleFingerSmoothIdx, isMiddleFinger ? _inGameShapeWeight : 0f);
        }

        /// <summary>Пристыковывает верхние рукава первого лица прямо к реальным плечам сидящего тела игрока (0 оторванных цилиндров!).</summary>
        public void AttachBodyShoulders(Transform leftShoulder, Transform rightShoulder)
        {
            _bodyShoulders[0] = leftShoulder;
            _bodyShoulders[1] = rightShoulder;
            PlaceUpperArms();
        }

        private void BuildUpperArms()
        {
            // Плавный эластичный вязаный рукав от плеча через мягкую дугу локтя прямо внутрь Sleeve_L/R:
            // тот же материал и точный шаг вязки (u_reps=3, v_scale=5), без жёсткого цилиндра, шара и ступеньки на локте!
            var sleeveMat = MakeSweaterTile("tripo hand sweater", 1f, 1f);
            var elbowCapMat = MakeSweaterTile("tripo elbow sweater", 3f, 1.5f);
            var smrs = GetComponentsInChildren<SkinnedMeshRenderer>(true);

            var tris = new int[(SleeveRings - 1) * SleeveRadial * 6];
            var ti = 0;
            var stride = SleeveRadial + 1;
            for (var r = 0; r < SleeveRings - 1; r++)
            {
                for (var s = 0; s < SleeveRadial; s++)
                {
                    var a = r * stride + s;
                    var b = a + 1;
                    var c = (r + 1) * stride + s;
                    var d = c + 1;
                    tris[ti++] = a; tris[ti++] = b; tris[ti++] = c;
                    tris[ti++] = b; tris[ti++] = d; tris[ti++] = c;
                }
            }

            for (var i = 0; i < 2; i++)
            {
                var side = i == 0 ? "_L" : "_R";
                _elbows[i] = Bone("Forearm" + side);
                _forearmTwists[i] = Bone("ForearmTwist" + side);
                MeasureSleeveBackRing(i, smrs, "Sleeve" + side);

                var mesh = new Mesh { name = "ElasticSleeve" + side };
                mesh.MarkDynamic();
                var vCount = SleeveRings * stride;
                _sleeveVerts[i] = new Vector3[vCount];
                _sleeveUvs[i] = new Vector2[vCount];
                mesh.vertices = _sleeveVerts[i];
                mesh.uv = _sleeveUvs[i];
                mesh.triangles = tris;
                _sleeveMeshes[i] = mesh;

                var go = new GameObject("Upper arm" + side);
                go.transform.SetParent(_body, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = sleeveMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.renderingLayerMask |= ArmsLayer;
                _upperArms[i] = go.transform;
                // Отдельные накладные шары на плечах убраны — эластичный рукав выходит прямо из глубины плечевого сустава свитера!
            }
            PlaceUpperArms();
        }

        /// <summary>Измеряет точный центр и радиус заднего среза Sleeve_L/R в локальных координатах кости Forearm, чтобы стык был бесшовным.</summary>
        private void MeasureSleeveBackRing(int idx, SkinnedMeshRenderer[] smrs, string sleeveName)
        {
            _sleeveBackCenterLocal[idx] = Vector3.zero;
            _sleeveBackRadius[idx] = .044f;
            var elbow = _elbows[idx];
            var twist = _forearmTwists[idx];
            if (elbow == null || twist == null) return;
            var smr = smrs.FirstOrDefault(s => s != null && s.name == sleeveName);
            if (smr == null) return;

            var baked = new Mesh();
            try
            {
                smr.BakeMesh(baked, true);
                var verts = baked.vertices;
                if (verts == null || verts.Length == 0) return;
                var axisLocal = elbow.InverseTransformDirection(twist.position - elbow.position).normalized;
                var minProj = float.MaxValue;
                var locals = new Vector3[verts.Length];
                for (var v = 0; v < verts.Length; v++)
                {
                    var p = elbow.InverseTransformPoint(smr.transform.TransformPoint(verts[v]));
                    locals[v] = p;
                    var proj = Vector3.Dot(p, axisLocal);
                    if (proj < minProj) minProj = proj;
                }
                var sum = Vector3.zero;
                var count = 0;
                for (var v = 0; v < locals.Length; v++)
                {
                    if (Vector3.Dot(locals[v], axisLocal) <= minProj + .016f)
                    {
                        sum += locals[v];
                        count++;
                    }
                }
                if (count < 4) return;
                var center = sum / count;
                var refU = Vector3.ProjectOnPlane(Vector3.right, axisLocal);
                if (refU.sqrMagnitude < 1e-4f) refU = Vector3.ProjectOnPlane(Vector3.up, axisLocal);
                refU.Normalize();
                var refV = Vector3.Cross(axisLocal, refU).normalized;
                float rSum = 0f, uSq = 0f, vSq = 0f;
                for (var v = 0; v < locals.Length; v++)
                {
                    if (Vector3.Dot(locals[v], axisLocal) <= minProj + .016f)
                    {
                        var d = Vector3.ProjectOnPlane(locals[v] - center, axisLocal);
                        rSum += d.magnitude;
                        var du = Vector3.Dot(d, refU);
                        var dv = Vector3.Dot(d, refV);
                        uSq += du * du;
                        vSq += dv * dv;
                    }
                }
                _sleeveBackCenterLocal[idx] = center;
                _sleeveBackRadius[idx] = Mathf.Clamp(rSum / count, .036f, .054f);
                _sleeveBackAxisU[idx] = refU;
                // Для эллипса RMS вдоль главной оси = r / sqrt(2) -> умножаем на sqrt(2) для точного радиуса края рукава!
                _sleeveBackRadiusU[idx] = Mathf.Clamp(Mathf.Sqrt(2f * uSq / count), .036f, .056f);
                _sleeveBackRadiusV[idx] = Mathf.Clamp(Mathf.Sqrt(2f * vSq / count), .036f, .056f);
            }
            finally
            {
                Destroy(baked);
            }
        }

        /// <summary>Эластичный изогнутый рукав плавно перетекает от плеча через естественную дугу локтя прямо по касательной в предплечье.</summary>
        private void PlaceUpperArms()
        {
            if (_upperArms[0] == null) return;
            var turn = Quaternion.Euler(0f, _yawBase + _bodyYaw, 0f);
            var seatLift = new Vector3(0f, Table3DLayout.EyeY - Table3DLayout.SeatBodyY, Table3DLayout.EyeZ - Table3DLayout.SeatBodyZ);
            var stride = SleeveRadial + 1;
            const int curveRings = SleeveRings - 3; // 0..13 — кубическая дуга Безье, 14..16 — плавный заход внутрь Sleeve_L/R

            for (var i = 0; i < 2; i++)
            {
                if (_sleeveMeshes[i] == null || _elbows[i] == null || _forearmTwists[i] == null) continue;
                var isFanArm = (i == 0 && FanSide == "_L") || (i == 1 && FanSide == "_R");
                var elbowBone = _elbows[i];
                var sleeveEndW = elbowBone.TransformPoint(_sleeveBackCenterLocal[i]);
                var forearmDirW = (_forearmTwists[i].position - elbowBone.position).normalized;
                if (forearmDirW.sqrMagnitude < 1e-5f) forearmDirW = elbowBone.forward;

                Vector3 shoulderW;
                Vector3 toChestW = i == 0 ? Vector3.right : Vector3.left;
                if (_bodyShoulders[0] != null && _bodyShoulders[1] != null)
                {
                    // Якорь строго в осях ПОВЁРНУТОГО туловища (к центру груди между плечами), на 5.5 см вглубь дельты свитера:
                    // рука анатомично выходит прямо из туловища без шара-накладки и без отрыва при любом повороте!
                    var midShoulder = (_bodyShoulders[0].position + _bodyShoulders[1].position) * .5f;
                    var rawIn = midShoulder - _bodyShoulders[i].position;
                    if (rawIn.sqrMagnitude > 1e-4f) toChestW = rawIn.normalized;
                    shoulderW = _bodyShoulders[i].position + toChestW * .055f + Vector3.down * .022f;
                }
                else
                {
                    var liftW = isFanArm ? _fanEyeLiftWeight * .85f : _gestureEyeLiftWeight * .65f;
                    if (!isFanArm && _eye != null)
                    {
                        var freeLift = Mathf.InverseLerp(Table3DLayout.TableY + .18f, _eye.position.y - .18f, sleeveEndW.y);
                        liftW = Mathf.Max(liftW, freeLift * .72f);
                    }
                    shoulderW = _body.position + turn * (new Vector3(i == 0 ? -Shoulder.x : Shoulder.x, Shoulder.y, Shoulder.z) + seatLift * liftW);
                }

                // Строим C1-гладкую кривую в локальных координатах _body, касательная в sleeveEnd строго совпадает с forearmDir!
                var p0 = _body.InverseTransformPoint(shoulderW);
                var p3 = _body.InverseTransformPoint(sleeveEndW);
                var fDir = _body.InverseTransformDirection(forearmDirW).normalized;
                var downLocal = _body.InverseTransformDirection(Vector3.down).normalized;
                var outShoulderLocal = -_body.InverseTransformDirection(toChestW).normalized;
                var dist = Vector3.Distance(p0, p3);

                var elbowLead = Mathf.Clamp(dist * .40f, .050f, .145f);
                var p2 = p3 - fDir * elbowLead;
                var shoulderToP2 = p2 - p0;
                var sDir = shoulderToP2.sqrMagnitude > 1e-5f
                    ? (shoulderToP2.normalized * .78f + outShoulderLocal * .14f + downLocal * .18f).normalized
                    : (outShoulderLocal * .5f + downLocal * .5f).normalized;
                var p1 = p0 + sDir * Mathf.Clamp(dist * .34f, .045f, .130f);

                var baseR = _sleeveBackRadius[i];
                var baseRU = _sleeveBackRadiusU[i];
                var baseRV = _sleeveBackRadiusV[i];
                var seamUW = elbowBone.TransformDirection(_sleeveBackAxisU[i]);
                var seamU = Vector3.ProjectOnPlane(_body.InverseTransformDirection(seamUW), fDir);
                seamU = seamU.sqrMagnitude > 1e-4f ? seamU.normalized : Vector3.ProjectOnPlane(Vector3.up, fDir).normalized;

                var centers = new Vector3[SleeveRings];
                var tangents = new Vector3[SleeveRings];
                var radUs = new float[SleeveRings];
                var radVs = new float[SleeveRings];
                var arcLens = new float[SleeveRings];

                for (var r = 0; r < curveRings; r++)
                {
                    var u = r / (float)(curveRings - 1);
                    var inv = 1f - u;
                    centers[r] = inv * inv * inv * p0 + 3f * inv * inv * u * p1 + 3f * inv * u * u * p2 + u * u * u * p3;
                    var tan = 3f * inv * inv * (p1 - p0) + 6f * inv * u * (p2 - p1) + 3f * u * u * (p3 - p2);
                    tangents[r] = tan.sqrMagnitude > 1e-6f ? tan.normalized : fDir;
                    // Плавное перетекание от плотного плеча и наполненного бицепса/локтя к точному эллиптическому профилю края Sleeve_L/R:
                    // рукав выглядит объёмным, вязаным и естественным (не «тонкой трубкой» при жестах!).
                    var s = u * u * (3f - 2f * u);
                    var shoulderScale = Mathf.Lerp(_sleeveUpperScale, 1f, s);
                    var midBulk = Mathf.Sin(u * Mathf.PI) * baseR * .09f * shoulderScale;
                    radUs[r] = Mathf.Lerp(baseR * 1.24f * shoulderScale, baseRU * 1.002f, s) + midBulk;
                    radVs[r] = Mathf.Lerp(baseR * 1.24f * shoulderScale, baseRV * 1.002f, s) + midBulk;
                }
                // 3 кольца плавного перекрытия (3,8 см вдоль предплечья внутрь Sleeve_L/R) — 0 щелей и 0 ступеньки!
                float[] overDist = { .010f, .024f, .038f };
                float[] overMul = { .994f, .945f, .865f };
                for (var k = 0; k < 3; k++)
                {
                    var r = curveRings + k;
                    centers[r] = p3 + fDir * overDist[k];
                    tangents[r] = fDir;
                    radUs[r] = baseRU * overMul[k];
                    radVs[r] = baseRV * overMul[k];
                }

                arcLens[0] = 0f;
                for (var r = 1; r < SleeveRings; r++)
                    arcLens[r] = arcLens[r - 1] + Vector3.Distance(centers[r - 1], centers[r]);
                var totalLen = arcLens[SleeveRings - 1];

                // Параллельный перенос базиса НАЗАД от точного эллиптического среза Sleeve_L/R к плечу — оси эллипса совпадают идеально!
                var ringNormals = new Vector3[SleeveRings];
                ringNormals[curveRings - 1] = seamU;
                for (var r = curveRings; r < SleeveRings; r++) ringNormals[r] = seamU;
                for (var r = curveRings - 2; r >= 0; r--)
                {
                    var n = Quaternion.FromToRotation(tangents[r + 1], tangents[r]) * ringNormals[r + 1];
                    n = Vector3.ProjectOnPlane(n, tangents[r]);
                    ringNormals[r] = n.sqrMagnitude > 1e-5f ? n.normalized : Vector3.Cross(tangents[r], Vector3.up).normalized;
                }

                var verts = _sleeveVerts[i];
                var uvs = _sleeveUvs[i];
                for (var r = 0; r < SleeveRings; r++)
                {
                    var normal = ringNormals[r];
                    var binormal = Vector3.Cross(tangents[r], normal).normalized;
                    var rU = radUs[r];
                    var rV = radVs[r];
                    var vCoord = (totalLen - arcLens[r]) * 5.0f;
                    var row = r * stride;
                    for (var s = 0; s <= SleeveRadial; s++)
                    {
                        var ang = (s / (float)SleeveRadial) * Mathf.PI * 2f;
                        verts[row + s] = centers[r] + normal * (Mathf.Cos(ang) * rU) + binormal * (Mathf.Sin(ang) * rV);
                        uvs[row + s] = new Vector2((s / (float)SleeveRadial) * 3.0f, vCoord);
                    }
                }

                var mesh = _sleeveMeshes[i];
                mesh.vertices = verts;
                mesh.uv = uvs;
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
            }
        }

        /// <summary>Сыгранная карта: непрерывное C1-движение без зависаний (Reach → Grab → Carry → плавный Return).</summary>
        private void PlayStep()
        {
            if (_card == null && _stage != ReachStage && _stage != ReleaseStage && _stage != ReturnStage) Clear();
            if (_card == null && _stage == ReachStage)
            {
                // Пока руки в позе проверки или выходят из неё, карта ждёт: рука идёт за ней из обычного покоя. Так же, пока
                // вместо свободной руки модель «класса»: рука сначала возвращается.
                if (_queue.Count == 0 || PoseTestActive || ThumbsSwapActive) return;
                var req = _queue.Dequeue();
                if (req.Card == null) return;
                _card = req.Card;
                _cardTargetPos = req.TargetPos;
                _cardTargetRot = req.TargetRot;
                _cardTargetScale = req.TargetScale > .1f ? req.TargetScale : Table3DLayout.TableCardScale;
                // Пока правая рука идёт сверху к вееру, большой палец левой мягко подталкивает выбранную карту вверх из веера
                // ровно настолько, чтобы её верхняя кромка, ладонь и рукав прошли гарантированно выше всех соседних карт!
                PrepareReachGeometry();
                _startPinchWeight = _pinchWeight;
                Begin(ReachStage);
            }
            _time += Time.deltaTime;
            switch (_stage)
            {
                case ReachStage: Reach(); break;
                case GrabStage: Grab(); break;
                case CarryStage: Carry(); break;
                default: Return(); break;
            }
        }

        private static bool ReducedMotion => PlayerHubRuntime.Current?.Settings.ReducedMotion ?? false;

        private static float Follow(float speed) => ReducedMotion ? 1f : 1f - Mathf.Exp(-speed * Time.deltaTime);

        /// <summary>
        /// Тело за взглядом на долю k пути: поворот головы по горизонтали — на BodyYawShare, наклон к столу — только сдвиг корпуса
        /// вперёд по горизонтали, чтобы руки не опускались в сукно. Сверху — дыхание.
        /// </summary>
        private void FollowEye(float k)
        {
            var limit = Busy ? 16f : MaxBodyYaw;
            var turn = Mathf.Clamp(Mathf.DeltaAngle(_yawBase, _eye.eulerAngles.y) * BodyYawShare, -limit, limit);
            _bodyYaw = Mathf.LerpAngle(_bodyYaw, turn, k);
            var breath = ReducedMotion ? 0f : Mathf.Sin(Time.time * BreathRate * 2f * Mathf.PI) * BreathLift;
            _currentBreath = breath;
            // При зуме к столу корпус остаётся на месте на стуле, чтобы правая рука не уезжала по сукну в центр карт.
            var zoom = _look != null ? _look.ZoomRatio : 0f;
            var leanOffset = Vector3.ProjectOnPlane(_eye.position - _eyeBase, Vector3.up) * (1f - zoom);
            _body.SetPositionAndRotation(
                _bodyBase + leanOffset + Vector3.up * breath,
                Quaternion.Euler(Table3DLayout.SeatBodyPitch, _yawBase + _bodyYaw, 0f));
            PlaceUpperArms();
        }

        /// <summary>Щипок свободной кисти: вес слоя Pinch в HandsAnimator (поза Pose_TakeCard из Blender), пальцы сводит аниматор.</summary>
        private void Pinch(float weight)
        {
            _pinchWeight = weight;
            _animator.SetLayerWeight(PinchLayer, weight);
        }

        private void Begin(int stage, float carryOver = 0f)
        {
            _stage = stage;
            _time = Mathf.Max(0f, carryOver);
            _fromPosition = _forearm.position;
            _fromRotation = _forearm.rotation;
            _fromPinch = _forearm.position + _forearm.rotation * Vector3.Scale(_forearm.lossyScale, _pinchInForearm);
        }

        private Quaternion RestRotation => _forearm.parent.rotation * _restRotation;

        private Vector3 RestPosition => _forearm.parent.TransformPoint(_restPosition);

        /// <summary>Точное мировое вращение предплечья: пальцы смотрят по wantForward, внутренняя ладонь (щипок) — по wantPalm.</summary>
        private Quaternion ForearmOrientation(Vector3 wantForward, Vector3 wantPalm)
        {
            var wf = wantForward.normalized;
            var wp = Vector3.ProjectOnPlane(wantPalm, wf);
            if (wp.sqrMagnitude < 1e-5f) wp = Vector3.ProjectOnPlane(Vector3.up, wf);
            var lf = _handForwardInForearm;
            var lp = Vector3.ProjectOnPlane(_handPalmInForearm, lf).normalized;
            return Quaternion.LookRotation(wf, wp.normalized) * Quaternion.Inverse(Quaternion.LookRotation(lf, lp));
        }

        private static Vector3 HermitePos(Vector3 p0, Vector3 v0, Vector3 p1, Vector3 v1, float dur, float t)
        {
            var t2 = t * t;
            var t3 = t2 * t;
            return (2f * t3 - 3f * t2 + 1f) * p0
                + (t3 - 2f * t2 + t) * (v0 * dur)
                + (-2f * t3 + 3f * t2) * p1
                + (t3 - t2) * (v1 * dur);
        }

        /// <summary>Ставит предплечье по кубическому сплайну Эрмита с точным сохранением входной и выходной скорости (без остановок на стыках фаз!).</summary>
        private void MovePinchHermite(Vector3 v0, Vector3 targetPinch, Vector3 v1, float dur, Quaternion targetRotation, float posT, float rotK, Vector3 extraArc = default)
        {
            var curPinch = HermitePos(_fromPinch, v0, targetPinch, v1, dur, posT) + extraArc;
            var curRot = Quaternion.Slerp(_fromRotation, targetRotation, rotK);
            var forearmPos = curPinch - curRot * Vector3.Scale(_forearm.lossyScale, _pinchInForearm);
            _forearm.SetPositionAndRotation(forearmPos, curRot);
        }

        /// <summary>Ставит предплечье так, чтобы точка щипка прошла по гладкой квадратичной кривой Безье (обходя веер сверху!).</summary>
        private void MovePinchBezier(Vector3 ctrlPinch, Vector3 targetPinch, Quaternion targetRotation, float k, float rotK = -1f)
        {
            var inv = 1f - k;
            var curPinch = inv * inv * _fromPinch + 2f * inv * k * ctrlPinch + k * k * targetPinch;
            var curRot = Quaternion.Slerp(_fromRotation, targetRotation, rotK >= 0f ? rotK : k);
            var forearmPos = curPinch - curRot * Vector3.Scale(_forearm.lossyScale, _pinchInForearm);
            _forearm.SetPositionAndRotation(forearmPos, curRot);
        }

        private static Vector3 EndGripLocal => new Vector3(CardMesh3D.Width * .28f * Outward, CardMesh3D.Height * .06f, 0f);

        private void ComputeCarryPose(Quaternion turn, out Vector3 layPinch, out Quaternion layRot)
        {
            var shoulder = _body.position + turn * new Vector3(Shoulder.x * Outward, -.20f, Shoulder.z);
            var releaseScale = Mathf.Lerp(_grabCardScale, _cardTargetScale, .90f);
            var gripWorldOffset = _cardTargetRot * (EndGripLocal * releaseScale);
            var ideal = _cardTargetPos + gripWorldOffset + Vector3.up * .054f;
            var toGoal = ideal - shoulder;
            layPinch = toGoal.magnitude > MaxPlayReach ? shoulder + toGoal.normalized * MaxPlayReach : ideal;
            // Запас над сукном и над нижней картой, чтобы при выкладывании ни пальцы, ни плоская карта не цепляли карты на столе.
            layPinch.y = Mathf.Max(layPinch.y, Table3DLayout.TableY + .060f);
            var layFwd = ((_cardTargetPos + Vector3.up * .02f) - (shoulder + Vector3.down * .07f)).normalized;
            // Естественный нейтральный разворот кисти (боком-внутрь, без неестественного выворота ладонью к потолку!):
            // большой палец остаётся сверху-сбоку карты, указательный снизу, а возврат на сукно происходит короткой плавной пронацией.
            var layPalm = (Vector3.up * .40f - turn * Vector3.right * (.84f * Outward) - turn * Vector3.forward * .36f).normalized;
            layRot = ForearmOrientation(layFwd, layPalm);
        }

        /// <summary>Квинтическое сглаживание (нулевое ускорение на старте и финише) — движение руки мягкое, без рывков.</summary>
        private static float Smoother(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        /// <summary>
        /// По реальной геометрии оставшихся карт в веере вычисляет безопасный пред-подъём выбранной карты вдоль её плоскости
        /// и степень её перекрытия правыми/передними картами, чтобы ни пальцы, ни ладонь, ни рукав не проходили сквозь веер.
        /// </summary>
        private void PrepareReachGeometry()
        {
            _reachCardStartLocalPos = _card.transform.localPosition;
            _reachBuriedRatio = 0f;
            _fanArchTopLocalY = _reachCardStartLocalPos.y + CardMesh3D.Height * .5f * _card.transform.localScale.y;
            var cardLocalRot = _card.transform.localRotation;
            var cardUpLocal = cardLocalRot * Vector3.up;
            var minLift = _reachCardStartLocalPos.magnitude > Table3DLayout.HandFanRadius + .010f ? .012f : .020f;
            var extraLift = minLift;

            if (FanRoot != null && _card.transform.parent == FanRoot)
            {
                var scale = _card.transform.localScale.x;
                var gripOffsetLocal = cardLocalRot * new Vector3(
                    CardMesh3D.Width * GripShareX * Outward * scale,
                    CardMesh3D.Height * GripShareY * scale,
                    0f);
                var baseGripLocal = _reachCardStartLocalPos + gripOffsetLocal;

                var fanArchTopY = baseGripLocal.y;
                var blockingRightY = float.NegativeInfinity;
                var maxRightReach = 0f;

                for (var i = 0; i < FanRoot.childCount; i++)
                {
                    var child = FanRoot.GetChild(i);
                    if (child == _card.transform || !child.gameObject.activeInHierarchy) continue;
                    if (child.GetComponent<Card3DView>() == null) continue;
                    var hw = CardMesh3D.Width * .5f * child.localScale.x;
                    var hh = CardMesh3D.Height * .5f * child.localScale.y;
                    var inFront = child.localPosition.z < _reachCardStartLocalPos.z - .0002f;
                    for (var sx = -1; sx <= 1; sx += 2)
                    {
                        var corner = child.localPosition + child.localRotation * new Vector3(sx * hw, hh, 0f);
                        if (corner.y > fanArchTopY) fanArchTopY = corner.y;
                        var rightDelta = (corner.x - baseGripLocal.x) * Outward;
                        if (inFront || rightDelta > -.015f)
                        {
                            if (corner.y > blockingRightY) blockingRightY = corner.y;
                            if (rightDelta > maxRightReach) maxRightReach = rightDelta;
                        }
                    }
                }

                _reachBuriedRatio = Mathf.Clamp01(maxRightReach / .135f);
                if (blockingRightY > float.NegativeInfinity)
                {
                    var clearanceY = Mathf.Lerp(.018f, .044f, _reachBuriedRatio);
                    var neededLift = (blockingRightY + clearanceY - baseGripLocal.y) / Mathf.Max(.55f, cardUpLocal.y);
                    extraLift = Mathf.Clamp(Mathf.Max(minLift, neededLift), minLift, .064f);
                }
                _fanArchTopLocalY = Mathf.Max(fanArchTopY, (baseGripLocal + cardUpLocal * extraLift).y);
            }

            _reachCardLiftLocal = cardUpLocal * extraLift;
            var startOut = Mathf.Max(0f, _reachCardStartLocalPos.magnitude - Table3DLayout.HandFanRadius);
            // Суммарный вытяг из базового слота веера всегда ≥ 10,6 см (> 9,5 см высоты карты) — 0 столкновений карт при выходе!
            _grabPullDist = Mathf.Max(.044f, .106f - (startOut + extraLift));
            _grabDuration = Mathf.Lerp(.19f, .24f, Mathf.InverseLerp(.044f, .084f, _grabPullDist));
        }

        private void Reach()
        {
            if (_card == null) { _releaseStartPinch = _pinchWeight; _returnStartVel = Vector3.zero; Begin(ReturnStage); return; }
            var t = Mathf.Clamp01(_time / ReachTime);
            var k = Smoother(t);
            var rotK = Smoother(Mathf.Clamp01(t / .82f));
            var turn = Quaternion.Euler(0f, _yawBase + _bodyYaw, 0f);
            // Левая рука чуть с опережением плавно подаёт выбранную карту вверх строго вдоль её плоскости (в её же Z-слое!).
            if (_card.transform.parent == FanRoot)
            {
                var uPop = Mathf.Clamp01(t / .68f);
                var popK = uPop * uPop * (3f - 2f * uPop);
                _card.transform.localPosition = _reachCardStartLocalPos + _reachCardLiftLocal * popK;
            }

            // Целимся сразу в итоговую приподнятую кромку карты, чтобы рука на подходе не проваливалась вниз к вееру!
            Vector3 grip;
            if (FanRoot != null && _card.transform.parent == FanRoot)
            {
                var s = _card.transform.localScale.x;
                var localGripOffset = _card.transform.localRotation * new Vector3(
                    CardMesh3D.Width * GripShareX * Outward * s,
                    CardMesh3D.Height * GripShareY * s,
                    0f);
                grip = FanRoot.TransformPoint(_reachCardStartLocalPos + _reachCardLiftLocal + localGripOffset);
            }
            else
            {
                grip = _card.transform.TransformPoint(new Vector3(CardMesh3D.Width * GripShareX * Outward, CardMesh3D.Height * GripShareY, 0f));
            }
            var cardFace = -_card.transform.forward;
            var cardUp = _card.transform.up;
            var cardRight = _card.transform.right;
            // Когда берём левую/среднюю карту за веером, локоть идёт чуть ниже и ближе к естественной дуге плеча,
            // чтобы рукав плавно и мягко заходил в кадр без резкого горизонтального перегиба.
            var elbowY = Mathf.Lerp(-.39f, -.34f, _reachBuriedRatio);
            var elbowZ = Mathf.Lerp(.02f, .06f, _reachBuriedRatio);
            var approachElbow = _eye.position + turn * new Vector3(.36f * Outward, elbowY, elbowZ);
            var grabFwd = (grip - approachElbow).normalized;
            // Уменьшенный наклон ладони вниз убирает согнутые безымянный/мизинец и основание ладони выше соседних карт веера.
            var grabPalm = (cardFace * .84f - cardRight * (.48f * Outward) - cardUp * .14f).normalized;
            var grabRot = ForearmOrientation(grabFwd, grabPalm);

            // Дуга Безье проходит строго выше наивысшей точки дуги веера (обход по воздуху, без касания правых карт!).
            var fanArchWorld = FanRoot != null ? FanRoot.TransformPoint(new Vector3(0f, _fanArchTopLocalY, 0f)) : grip;
            var overFanCtrl = grip + cardUp * .068f + turn * new Vector3(Mathf.Lerp(.095f, .135f, _reachBuriedRatio) * Outward, .032f, .028f);
            overFanCtrl.y = Mathf.Max(overFanCtrl.y, fanArchWorld.y + .062f);
            // Пальцы плавно собираются от 0 до полного щипка без скачка на первом кадре!
            Pinch(Mathf.Lerp(_startPinchWeight, 1f, Smoother(Mathf.Clamp01(t / .92f))));
            MovePinchBezier(overFanCtrl, grip, grabRot, k, rotK);
            if (_time < ReachTime) return;

            var over = _time - ReachTime;
            // Фиксируем карту в щипке строго по её средней плоскости (z=0 между подушечками большого и указательного пальцев!).
            _card.Grip(_pinch);
            _grabGripInCard = _card.transform.InverseTransformPoint(_pinch.position);
            _grabGripInCard.z = 0f;
            _grabCardScale = _card.transform.localScale.x;
            ComputeCarryPose(turn, out _layPinch, out _layRot);
            // КРИТИЧНО против прохождения карты через карту в веере:
            // всё вытягивание Grab идёт СТРОГО по прямой вдоль cardUp без бокового/переднего смещения и без поворота кисти (_pullRot = grabRot),
            // пока нижний край карты полностью не выйдет выше всех остальных карт веера (≥ 10,6 см > 9,5 см)!
            _pullTarget = grip + cardUp * _grabPullDist;
            _pullExitVel = cardUp * (_grabPullDist / _grabDuration * 1.15f);
            _pullRot = grabRot;
            Begin(GrabStage, over);
            if (over > 1e-5f) Grab();
        }

        /// <summary>Щипок сомкнут: рука вытягивает карту строго вдоль её плоскости из веера (0 мм отклонения по глубине!) до полного выхода.</summary>
        private void Grab()
        {
            if (_card == null) { _releaseStartPinch = _pinchWeight; _returnStartVel = Vector3.zero; Begin(ReturnStage); return; }
            var t = Mathf.Clamp01(_time / _grabDuration);
            Pinch(1f);
            // Вращение предплечья на протяжении Grab строго фиксировано (_pullRot == _fromRotation): карта скользит строго в своей Z-плоскости!
            MovePinchHermite(Vector3.zero, _pullTarget, _pullExitVel, _grabDuration, _pullRot, t, 0f);
            if (_time < _grabDuration) return;
            var over = _time - _grabDuration;
            // Карта полностью вышла выше веера — теперь оставшиеся карты в левой руке могут плавно сомкнуть просвет!
            CardPulledFromFan?.Invoke();
            _carryCardStartWorldRot = _card.transform.rotation;
            var turn = Quaternion.Euler(0f, _yawBase + _bodyYaw, 0f);
            var toSlot = (_cardTargetPos + _cardTargetRot * (EndGripLocal * _cardTargetScale)) - _layPinch;
            // Мягкая живая скорость скольжения карты вперёд-вниз к слоту в момент разжатия пальцев (без нырка ниже стола).
            _tossVel = (toSlot / .28f) * .48f + turn * new Vector3(.015f * Outward, 0f, .035f);
            Begin(CarryStage, over);
            if (over > 1e-5f) Carry();
        }

        /// <summary>Рука единой плавной дугой выносит уже вышедшую из веера карту к слоту на столе и мягко отпускает её полностью плоской.</summary>
        private void Carry()
        {
            if (_card == null) { _releaseStartPinch = _pinchWeight; _returnStartVel = Vector3.zero; Begin(ReturnStage); return; }
            var t = Mathf.Clamp01(_time / CarryTime);
            // В конце проноса пальцы начинают мягко приоткрываться, выпуская карту на живом ходу.
            var openU = Mathf.Clamp01((t - .82f) / .18f);
            Pinch(Mathf.Lerp(1f, .62f, openU * openU * (3f - 2f * openU)));

            var bell = Mathf.Sin(Mathf.PI * t);
            var carryArc = Vector3.up * (.032f * bell * bell);
            var rotK = Smoother(t);
            MovePinchHermite(_pullExitVel, _layPinch, _tossVel, CarryTime, _layRot, t, rotK, carryArc);

            // Карта довыравнивается в 100% горизонтальную плоскость ДО отпускания (t = 0.86), чтобы наклонный угол никогда не задел нижнюю карту,
            // а рост масштаба начинается только после отхода от веера (t > 0.16), чтобы не раздуваться рядом с веером!
            var rotU = Mathf.Clamp01((t - .05f) / .81f);
            var rotFlatT = Smoother(rotU);
            var scaleU = Mathf.Clamp01((t - .16f) / .84f);
            var scaleT = Smoother(scaleU);
            var gripT = Smoother(t);

            var curScale = Mathf.Lerp(_grabCardScale, _cardTargetScale, scaleT * .90f);
            _card.transform.rotation = Quaternion.Slerp(_carryCardStartWorldRot, _cardTargetRot, rotFlatT);
            _card.transform.localScale = Vector3.one * curScale;
            var curGripLocal = Vector3.Lerp(_grabGripInCard, EndGripLocal, gripT);
            _card.transform.position = _pinch.position - _card.transform.TransformVector(curGripLocal);

            if (_time < CarryTime) return;
            var over = _time - CarryTime;
            // Карта уже 100% горизонтальная на высоте ~5.5 см прямо над слотом и плавно скользит вниз с живой скоростью руки.
            _card.ReleaseToTable(_tossVel);
            _card = null;
            Played++;

            _releaseStartPinch = _pinchWeight;
            _returnStartVel = _tossVel * .45f + Vector3.up * .075f;
            Begin(ReturnStage, over);
            if (over > 1e-5f) Return();
        }

        /// <summary>Единый непрерывный возврат: кисть сразу мягко доворачивается ладонью вниз, пальцы разжимаются и ложатся на сукно.</summary>
        private void Return()
        {
            var t = Mathf.Clamp01(_time / ReturnTime);
            var openU = Mathf.Clamp01(_time / .20f);
            Pinch(Mathf.Lerp(_releaseStartPinch, 0f, openU * openU * (3f - 2f * openU)));

            var bell = Mathf.Sin(Mathf.PI * t);
            var liftArc = Vector3.up * (.022f * bell * bell);
            var pos = HermitePos(_fromPosition, _returnStartVel, RestPosition, Vector3.zero, ReturnTime, t) + liftArc;
            // Кисть сразу мягко переходит в естественное положение ладонью вниз (не зависает раскрытой в воздухе!).
            var rotK = Smoother(t);
            _forearm.SetPositionAndRotation(pos, Quaternion.Slerp(_fromRotation, RestRotation, rotK));
            if (_time < ReturnTime) return;
            Pinch(0f);
            _card = null;
            _stage = ReachStage;
            _forearm.localPosition = _restPosition;
            _forearm.localRotation = _restRotation;
        }

        private void OnDestroy()
        {
            for (var i = 0; i < _sleeveMeshes.Length; i++)
                if (_sleeveMeshes[i] != null) Destroy(_sleeveMeshes[i]);
            if (_body != null) Destroy(_body.gameObject);
            if (_thumbsModel != null) Destroy(_thumbsModel.gameObject);
        }
    }
}
