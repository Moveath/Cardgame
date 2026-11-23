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
        private const string HoldingCards = "HoldingCards", Regrip = "Regrip", GestureState = "Gesture_";
        private static readonly int IdleState = Animator.StringToHash("Idle");
        private const int ReachStage = 0, GrabStage = 1, CarryStage = 2, ReturnStage = 3;
        private const float ReachTime = .25f, GrabTime = .12f, CarryTime = .25f, ReturnTime = .3f;
        // Насколько карта выходит из веера, пока её берут: за верхний край вверх.
        private const float GrabPull = .03f;
        // Жест, перебитый картой, гаснет за GestureCut. Если аниматор не начал жест за GestureStart, запрос снимается.
        private const float GestureCut = .2f, GestureStart = .5f;
        // Тело догоняет взгляд по горизонтали: доля угла и скорость (90 % пути примерно за 0,3 с).
        private const float BodyYawShare = .6f, BodyFollow = 8f;
        // Дыхание: тело с руками поднимается и опускается на 1,2 мм раз в четыре секунды.
        private const float BreathLift = .0012f, BreathRate = .25f;
        private const float GripShare = .32f;                         // За какую высоту от середины карты берёт щипок.
        private const float FingerRadius = .0085f, PalmRadius = .026f;
        // Рука несёт карту от веера вперёд к столу, к середине, и там отпускает, в осях тела.
        private static readonly Vector3 TossShift = new Vector3(.06f * Outward, -.02f, .13f);
        // Откуда тянется предплечье, когда рука берёт карту из веера: снизу и снаружи, от локтя, в осях тела.
        private static readonly Vector3 ReachElbow = new Vector3(.2f * Outward, -.42f, .1f);
        private static readonly string[] Fingertips = { "Thumb", "Index", "Middle" };
        // Плечо — рукав свитера от локтя к плечу, на локте шар той же толщины: срез рукава модели у локтя не попадает в кадр
        // ни при каком повороте головы. Правое плечо в осях тела по горизонтали (без наклона взгляда): на 0,19 м вбок,
        // на 0,27 м ниже глаз и чуть позади; левое — зеркально. Толщина — как у рукава у локтя.
        private static readonly Vector3 Shoulder = new Vector3(.19f, -.27f, -.04f);
        // Рукав модели у локтя ~5,2 см: плечо и шар на локте вровень с ним, чтобы стык у локтя был плавным.
        private const float UpperArmRadius = .053f;
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

        /// <summary>Жесты, которые можно показать соперникам: клипы Take_* из scripts/3d/hands/animations.py.</summary>
        public static readonly string[] Gestures = { "ThumbsUp", "ThumbsDown", "Fist" };

        // Жест для HUD → триггер HandsAnimator; его состояние в слое Base — Gesture_ + триггер.
        private static readonly Dictionary<string, string> Triggers = new Dictionary<string, string>
        {
            ["Point"] = "Point", ["ThumbsUp"] = "ThumbUp", ["ThumbsDown"] = "ThumbDown",
            ["TapTable"] = "Knock", ["Shrug"] = "Shrug", ["Fist"] = "Fist",
        };

        private Animator _animator;
        private int _gestureState;
        private bool _gestureSeen, _gestureCut;
        private float _gestureWait;
        private float _fidgetIn = 9f;
        private float _pinchWeight;
        private Transform _eye, _body, _forearm, _pinch;
        private Vector3 _eyeBase, _bodyBase;
        private float _yawBase, _bodyYaw;
        private Vector3 _restPosition, _pinchInForearm, _pinchAxisInForearm, _wristInForearm;
        private Quaternion _restRotation;
        private readonly Queue<Card3DView> _queue = new Queue<Card3DView>();
        private readonly List<Collider> _colliders = new List<Collider>();
        private Card3DView _card;
        private int _stage;
        private float _time;
        private Vector3 _fromPosition, _grip, _pull;
        private Quaternion _fromRotation;
        private readonly Transform[] _elbows = new Transform[2], _upperArms = new Transform[2], _elbowCaps = new Transform[2];
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
        private float _inGameShapeWeight, _gestureLiftWeight;
        // Подмена «класса»: модель, части свободной руки (кожа, рукав, плечо, шар на локте), хотят ли модель в кадре, где рука
        // (0 — где её ставит аниматор, 1 — за краем кадра) и модель (0 — под кадром, 1 — в углу), сколько ещё держать (меню),
        // «класс» ли это из меню.
        private ThumbsUpModel3D _thumbsModel;
        private readonly List<GameObject> _freeArmParts = new List<GameObject>();
        private bool _thumbsWanted, _thumbsMenu;
        private float _armAway, _modelIn, _thumbsHold;

        public Transform FanRoot { get; private set; }

        public IReadOnlyList<Collider> Colliders => _colliders;

        /// <summary>Сколько карт правая рука сыграла сама: для проверки.</summary>
        public static int Played { get; private set; }

        /// <summary>Сколько жестов доиграно до конца: для проверки.</summary>
        public static int GesturesDone { get; private set; }

        public bool Busy => _card != null || _queue.Count > 0;

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
            // Тело сидит на стуле с тем же наклоном, что взгляд при посадке: поза рук и веер заданы в этих осях.
            var body = new GameObject("Seated body").transform;
            body.SetPositionAndRotation(eye.position, Quaternion.Euler(TableCameraController.StartPitch, eye.eulerAngles.y, 0f));
            var arms = Instantiate(Resources.Load<GameObject>(Model), body, false);
            arms.name = "own-arms";
            // Как у всех моделей стола: разворот ставит пальцы вперёд, левую руку слева.
            arms.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var view = arms.AddComponent<OwnArms3DView>();
            view._eye = eye;
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

        /// <summary>Правая рука сыграет эту карту. Пока рука до неё не дотянулась, карта ждёт в веере и не принимает нажатий.</summary>
        public void PlayCard(Card3DView card)
        {
            card.Hold();
            // Сначала руки выходят из проверки поз; карта ждёт в веере, пока они не вернутся (PlayStep).
            StopPoseTest();
            // Карта важнее жеста: жест быстро гаснет, рука идёт за картой. Модель «класса» уезжает, карта ждёт руку (PlayStep).
            if (Gesture != null) StopGesture();
            HideThumbsModel();
            if (ReducedMotion)
            {
                card.LetGo();
                return;
            }
            _queue.Enqueue(card);
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
                    // С 2026-11-23 у «класса» своя форма кожи ThumbsUpSmooth (объёмные суставы без коллизий), «фу» — ThumbsSmooth.
                    _thumbsUpSmoothIdx = part.sharedMesh.GetBlendShapeIndex("ThumbsUpSmooth");
                }
            }
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
            var thumb = Bone("TipThumb" + FreeSide);
            var index = Bone("TipIndex" + FreeSide);
            var hand = Bone("Hand" + FreeSide);
            // Само предплечье ставит этот скрипт, а точка щипка мерится в той же позе, в которой рука потом берёт карту.
            Sample(clips, PinchPose);
            var pinch = (thumb.position + index.position) / 2;
            _pinchInForearm = _forearm.InverseTransformPoint(pinch);
            _pinchAxisInForearm = _forearm.InverseTransformDirection(index.position - thumb.position).normalized;
            _wristInForearm = _forearm.InverseTransformDirection(hand.position - _forearm.position).normalized;
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
            // Живая рука не стоит как кукла: время от времени левая перехватывает веер.
            if (Gesture != null || Busy || ReducedMotion || !holding || PoseTestActive) return;
            _fidgetIn -= Time.deltaTime;
            if (_fidgetIn > 0f) return;
            _fidgetIn = UnityEngine.Random.Range(10f, 18f);
            _animator.SetTrigger(Regrip);
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
            PlayStep();
            UpdateThumbsModel();
            ApplyGestureGazeLift();
            PlaceUpperArms();
            ApplyInGameGestureShape();
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
            var extraYaw = Mathf.Clamp(Mathf.DeltaAngle(_yawBase + _bodyYaw, _eye.eulerAngles.y), -24f, 24f);
            if (upPitch <= .05f && Mathf.Abs(extraYaw) <= .05f) return;
            var w = _gestureLiftWeight;
            var pivot = _eye.position + Vector3.down * .18f;
            // Позицию руки плавно поднимаем вслед за взглядом вверх на соперников (на 68% угла),
            // а наклон самого кулака меняем мягко (22%), чтобы большой палец оставался строго вертикальным (~90°)!
            var posRot = Quaternion.AngleAxis(extraYaw * .45f * w, Vector3.up) * Quaternion.AngleAxis(-upPitch * .68f * w, _eye.right);
            var handRot = Quaternion.AngleAxis(extraYaw * .45f * w, Vector3.up) * Quaternion.AngleAxis(-upPitch * .22f * w, _eye.right);
            _forearm.position = pivot + posRot * (_forearm.position - pivot);
            _forearm.rotation = handRot * _forearm.rotation;
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
                _elbowCaps[free].gameObject.SetActive(show);
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
            // «Класс» ведёт свою форму ThumbsUpSmooth (в модели без неё — общую ThumbsSmooth, как раньше), «фу» — ThumbsSmooth.
            var thumbsUpOwn = Gesture == "ThumbsUp" && _thumbsUpSmoothIdx >= 0;
            var isThumbs = (Gesture == "ThumbsUp" && !thumbsUpOwn) || Gesture == "ThumbsDown";
            if (_fistSmoothIdx >= 0) _freeArmMesh.SetBlendShapeWeight(_fistSmoothIdx, isFist ? _inGameShapeWeight : 0f);
            if (_fistThumbIdx >= 0) _freeArmMesh.SetBlendShapeWeight(_fistThumbIdx, isFist ? _inGameShapeWeight : 0f);
            if (_thumbsSmoothIdx >= 0) _freeArmMesh.SetBlendShapeWeight(_thumbsSmoothIdx, isThumbs ? _inGameShapeWeight : 0f);
            if (_thumbsUpSmoothIdx >= 0) _freeArmMesh.SetBlendShapeWeight(_thumbsUpSmoothIdx, thumbsUpOwn ? _inGameShapeWeight : 0f);
        }

        private void BuildUpperArms()
        {
            // Плечо и локоть одеты в ту же бесшовную вязаную текстуру свитера TripoSweater_Tile, что и предплечье!
            var upperMat = MakeSweaterTile("tripo upper sweater", 3f, 2.8f);
            var elbowMat = MakeSweaterTile("tripo elbow sweater", 3f, 1.5f);
            for (var i = 0; i < 2; i++)
            {
                var side = i == 0 ? "_L" : "_R";
                _elbows[i] = Bone("Forearm" + side);
                _upperArms[i] = Table3DArt.Limb(_body, Vector3.zero, Vector3.up, UpperArmRadius, upperMat).transform;
                _upperArms[i].name = "Upper arm" + side;
                _elbowCaps[i] = Table3DArt.Ball(_body, Vector3.zero, Vector3.one * UpperArmRadius * 2, elbowMat).transform;
                _elbowCaps[i].name = "Elbow" + side;
                _upperArms[i].GetComponent<Renderer>().renderingLayerMask |= ArmsLayer;
                _elbowCaps[i].GetComponent<Renderer>().renderingLayerMask |= ArmsLayer;
            }
            PlaceUpperArms();
        }

        /// <summary>Плечо тянется от локтя, где его сейчас поставили клип или рука с картой, к плечу тела.</summary>
        private void PlaceUpperArms()
        {
            if (_upperArms[0] == null) return;
            var turn = Quaternion.Euler(0f, _yawBase + _bodyYaw, 0f);
            for (var i = 0; i < 2; i++)
            {
                var shoulder = _body.position + turn * new Vector3(i == 0 ? -Shoulder.x : Shoulder.x, Shoulder.y, Shoulder.z);
                var elbow = _elbows[i].position;
                var along = shoulder - elbow;
                _upperArms[i].SetPositionAndRotation((elbow + shoulder) / 2, Quaternion.FromToRotation(Vector3.up, along.normalized));
                _upperArms[i].localScale = new Vector3(UpperArmRadius * 2, along.magnitude / 2, UpperArmRadius * 2);
                _elbowCaps[i].position = elbow;
            }
        }

        /// <summary>Сыгранная карта: Reach → Grab → Carry → Return.</summary>
        private void PlayStep()
        {
            if (_card == null && _stage != ReachStage) Clear();
            if (_card == null)
            {
                // Пока руки в позе проверки или выходят из неё, карта ждёт: рука идёт за ней из обычного покоя. Так же, пока
                // вместо свободной руки модель «класса»: рука сначала возвращается.
                if (_queue.Count == 0 || PoseTestActive || ThumbsSwapActive) return;
                _card = _queue.Dequeue();
                if (_card == null) return;
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
            var turn = Mathf.DeltaAngle(_yawBase, _eye.eulerAngles.y) * BodyYawShare;
            _bodyYaw = Mathf.LerpAngle(_bodyYaw, turn, k);
            var breath = ReducedMotion ? 0f : Mathf.Sin(Time.time * BreathRate * 2f * Mathf.PI) * BreathLift;
            _body.SetPositionAndRotation(
                _bodyBase + Vector3.ProjectOnPlane(_eye.position - _eyeBase, Vector3.up) + Vector3.up * breath,
                Quaternion.Euler(TableCameraController.StartPitch, _yawBase + _bodyYaw, 0f));
            PlaceUpperArms();
        }

        /// <summary>Щипок свободной кисти: вес слоя Pinch в HandsAnimator (поза Pose_TakeCard из Blender), пальцы сводит аниматор.</summary>
        private void Pinch(float weight)
        {
            _pinchWeight = weight;
            _animator.SetLayerWeight(PinchLayer, weight);
        }

        private void Begin(int stage)
        {
            _stage = stage;
            _time = 0f;
            _fromPosition = _forearm.position;
            _fromRotation = _forearm.rotation;
        }

        private Quaternion RestRotation => _forearm.parent.rotation * _restRotation;

        private Vector3 RestPosition => _forearm.parent.TransformPoint(_restPosition);

        /// <summary>Предплечье с поворотом rotation так, чтобы щипок оказался в point.</summary>
        private void Aim(Vector3 point, Quaternion rotation, float k)
        {
            var targetPosition = point - rotation * Vector3.Scale(_forearm.lossyScale, _pinchInForearm);
            _forearm.SetPositionAndRotation(Vector3.Lerp(_fromPosition, targetPosition, k), Quaternion.Slerp(_fromRotation, rotation, k));
        }

        private void Reach()
        {
            var k = Mathf.SmoothStep(0f, 1f, _time / ReachTime);
            // Карту берут за верх, у видимой полоски: большой палец перед картой, указательный за ней.
            // Предплечье тянется к ней снизу и снаружи, поэтому ладонь разворачивается к игроку.
            var grip = _card.transform.TransformPoint(new Vector3(0f, CardMesh3D.Height * GripShare, 0f));
            var reach = (grip - _body.TransformPoint(ReachElbow)).normalized;
            var restReach = RestRotation * _wristInForearm;
            var restAxis = RestRotation * _pinchAxisInForearm;
            var turn = Quaternion.LookRotation(reach, Vector3.ProjectOnPlane(_card.transform.forward, reach))
                * Quaternion.Inverse(Quaternion.LookRotation(restReach, Vector3.ProjectOnPlane(restAxis, restReach)));
            var rotation = turn * RestRotation;
            // Пальцы сводит слой щипка, предплечье ставится так, чтобы точка щипка пришла на карту.
            Pinch(k);
            Aim(grip, rotation, k);
            if (_time < ReachTime) return;
            _pull = _card.transform.up * GrabPull;
            _card.Grip(_pinch);
            _grip = grip;
            Begin(GrabStage);
        }

        /// <summary>Щипок закрыт на верхнем крае карты: рука вытягивает её из веера вверх, вдоль самой карты.</summary>
        private void Grab()
        {
            var k = Mathf.SmoothStep(0f, 1f, _time / GrabTime);
            Pinch(1f);
            Aim(_grip + _pull, _fromRotation, k);
            if (_time < GrabTime) return;
            _grip += _pull;
            Begin(CarryStage);
        }

        private void Carry()
        {
            var k = Mathf.SmoothStep(0f, 1f, _time / CarryTime);
            Pinch(1f);
            Aim(_grip + _body.TransformDirection(TossShift), _fromRotation, k);
            if (_time < CarryTime) return;
            _card.LetGo();
            Played++;
            Begin(ReturnStage);
        }

        private void Return()
        {
            var k = Mathf.SmoothStep(0f, 1f, _time / ReturnTime);
            Pinch(1f - k);
            _forearm.SetPositionAndRotation(Vector3.Lerp(_fromPosition, RestPosition, k), Quaternion.Slerp(_fromRotation, RestRotation, k));
            if (_time < ReturnTime) return;
            _card = null;
            _stage = ReachStage;
            _forearm.localPosition = _restPosition;
            _forearm.localRotation = _restRotation;
        }

        private void OnDestroy()
        {
            if (_body != null) Destroy(_body.gameObject);
            if (_thumbsModel != null) Destroy(_thumbsModel.gameObject);
        }
    }
}
