using CardGame.Core.Cards;
using UnityEngine;

namespace CardGameClient.ThreeD
{
    /// <summary>
    /// Место соперника: клубное бархатное кресло, ригнутый Humanoid-персонаж (Mixamo 65-bone)
    /// с полностью рассинхронизированным естественным дыханием, редкими сменами позы и взгляда,
    /// анатомичным хватом веера левой рукой (без выворота запястья) и сокетами под головы/шапки/очки.
    /// </summary>
    public sealed class PlayerSeat3DView : MonoBehaviour
    {
        // Веер всегда одного размера: сколько карт у соперника, не видно (автор, 2026-11-05).
        private const int FanCards = 5;
        private static readonly Color[] FallbackSkin =
        {
            new Color(.82f, .62f, .48f), new Color(.74f, .54f, .41f), new Color(.86f, .68f, .56f),
            new Color(.66f, .48f, .36f), new Color(.8f, .6f, .5f)
        };
        private static readonly Color[] FallbackHair =
        {
            new Color(.07f, .05f, .04f), new Color(.12f, .08f, .06f), new Color(.05f, .05f, .05f),
            new Color(.22f, .15f, .09f), new Color(.09f, .07f, .06f)
        };

        /// <summary>Индивидуальный характер посадки, дыхания, пауз и взгляда у каждого места за столом.</summary>
        private readonly struct SeatPersonality
        {
            public readonly float AnimSpeed, StartPhase;
            public readonly float BaseSpinePitch, BaseSpineYaw;
            public readonly float BreathHz, BreathAmp;
            public readonly float HeadPitchBase, GlanceSpan, GlanceTilt;
            public readonly float GlanceHoldMin, GlanceHoldMax;
            public readonly float ShiftHoldMin, ShiftHoldMax, ShiftSpan;
            public readonly Vector3 HandOffset;

            public SeatPersonality(
                float animSpeed, float startPhase, float baseSpinePitch, float baseSpineYaw,
                float breathHz, float breathAmp, float headPitchBase, float glanceSpan, float glanceTilt,
                float glanceHoldMin, float glanceHoldMax, float shiftHoldMin, float shiftHoldMax, float shiftSpan,
                Vector3 handOffset)
            {
                AnimSpeed = animSpeed;
                StartPhase = startPhase;
                BaseSpinePitch = baseSpinePitch;
                BaseSpineYaw = baseSpineYaw;
                BreathHz = breathHz;
                BreathAmp = breathAmp;
                HeadPitchBase = headPitchBase;
                GlanceSpan = glanceSpan;
                GlanceTilt = glanceTilt;
                GlanceHoldMin = glanceHoldMin;
                GlanceHoldMax = glanceHoldMax;
                ShiftHoldMin = shiftHoldMin;
                ShiftHoldMax = shiftHoldMax;
                ShiftSpan = shiftSpan;
                HandOffset = handOffset;
            }
        }

        // 5 разных типажей за столом — полностью разные темпы дыхания, осанки, пауз и амплитуды взгляда.
        private static readonly SeatPersonality[] Personalities =
        {
            new SeatPersonality(.64f, .07f, -2.5f,  2.5f, .19f, .95f,  7f,  9f, 2.2f, 2.6f, 5.4f, 7.0f, 13.0f, 2.2f, new Vector3( .000f,  .000f,  .000f)),
            new SeatPersonality(.88f, .41f,  2.0f, -3.0f, .24f, .80f, 10f, 13f, 3.4f, 1.8f, 3.8f, 5.5f, 11.0f, 3.0f, new Vector3(-.010f,  .012f,  .006f)),
            new SeatPersonality(.58f, .73f, -4.0f, -1.8f, .17f, 1.10f, 5f,  7f, 1.8f, 3.4f, 6.6f, 8.5f, 15.5f, 1.8f, new Vector3( .010f, -.008f, -.006f)),
            new SeatPersonality(.77f, .23f,  0.5f,  3.2f, .22f, .85f,  8f, 11f, 2.8f, 2.2f, 4.7f, 6.2f, 12.5f, 2.6f, new Vector3(-.006f, -.005f,  .008f)),
            new SeatPersonality(.97f, .59f,  1.5f,  0.8f, .26f, .75f,  9f, 14f, 3.6f, 1.6f, 3.4f, 5.0f, 10.0f, 3.2f, new Vector3( .008f,  .010f, -.004f)),
        };

        private enum ActionKind { None, ThrowCard, TakeReach, Gesture }

        private GameObject _fan;
        private GameObject _mountedHead;
        private Material _neckSkinMat;
        private Transform _bodyRoot;
        private Vector3 _bodyBaseLocalPos = new Vector3(0f, .058f, -.112f);
        private float _elderPosturePitch;
        private float _labelHeight = 1.42f;
        private Animator _animator;
        private Transform _spineBone, _chestBone, _upperChestBone, _neckBone, _headBone;
        private Transform _leftShoulder, _rightShoulder;
        private Transform _leftUpperArm, _leftLowerArm, _leftHand;
        private Transform _rightUpperArm, _rightLowerArm, _rightHand;
        private Transform _leftIndex1, _leftMiddle1, _leftMiddle3, _leftRing1, _leftLittle1, _leftThumb1, _leftThumb2;
        private Transform _rightIndex1, _rightMiddle1, _rightLittle1, _rightThumb1, _rightThumb2;
        private Transform[] _curlFingers;
        private Quaternion[] _fingerRestLocal;
        private Transform[] _allLeftFingerBones;
        private Quaternion[] _allLeftFingerRestLocal;
        private Transform[] _allRightFingerBones;
        private Quaternion[] _allRightFingerRestLocal;
        private Transform[] _rightFingerTipCheck;
        private Transform[] _backFingerBones;
        private Quaternion _spineNeutral, _chestNeutral, _upperChestNeutral, _neckNeutral, _headNeutral;
        private Quaternion _rightUpperNeutral, _rightLowerNeutral, _rightHandNeutral;
        private Vector3 _rightElbowBendLocal = Vector3.forward;
        private bool _hasSmoothRightArm;
        private float _smoothRightWeight;
        private Quaternion _smoothRightUpper, _smoothRightLower, _smoothRightHand;
        private Quaternion[] _smoothRightFingers;
        private bool _holding = true;
        private float _holdWeight = 1f;
        private SeatPersonality _persona;
        private float _breathPhase;
        private float _nextGlanceAt, _curYaw, _targetYaw, _curPitch, _targetPitch, _curRoll, _targetRoll;
        private float _nextShiftAt, _curShiftPitch, _targetShiftPitch, _curShiftYaw, _targetShiftYaw, _curShiftRoll, _targetShiftRoll;
        private uint _rngState;

        // Живые действия правой руки, наклон корпуса, взгляды на события стола и сетевой ввод головы для мультиплеера.
        private ActionKind _actionKind;
        private float _actionTime, _actionDuration;
        private Card3DView _thrownCard;
        private Vector3 _thrownStartPos, _thrownTargetPos, _lastCardWorldPos;
        private Quaternion _thrownStartRot, _thrownTargetRot;
        private float _thrownTargetScale;
        private bool _thrownPicked, _thrownReleased;
        private Vector3 _reachWorldTarget;
        private string _gestureId;
        private Vector3? _gesturePointWorld;
        private float _curLeanPitch, _curLeanYaw, _curShrug;
        private float _curBodySlideZ, _curBodyLiftY, _curShoulderReach;

        private bool _hasLookOverride, _hasRemoteLook;
        private Vector3 _lookWorldTarget;
        private float _lookUntil, _remoteYaw, _remotePitch;
        private float _headMotionTime, _headMotionDuration;
        private bool _headMotionIsNod;

        public int PlayerIndex { get; private set; }
        public Transform HandAnchor { get; private set; }
        public GameObject Glass { get; private set; }
        public Transform HeadSocket { get; private set; }
        public Transform HatSocket { get; private set; }
        public Transform GlassesSocket { get; private set; }
        public bool IsActing => _actionKind != ActionKind.None;
        public bool IsHoldingThrownCard => _actionKind == ActionKind.ThrowCard && _thrownCard != null && !_thrownReleased;
        public Vector3 HeadWorldPosition => _headBone != null ? _headBone.position : transform.position + Vector3.up * 1.18f;

        // Плашка аккуратно прямо над причёской сидящего игрока (подстраивается под рост мужского/женского/пожилого типажа).
        public Vector3 LabelPoint => transform.position + Vector3.up * _labelHeight;

        public static PlayerSeat3DView Create(Transform parent, int playerIndex, float angle, string headId = null)
        {
            var at = Table3DLayout.SeatPosition(angle);
            var root = new GameObject("Seat " + playerIndex);
            root.transform.SetParent(parent, false);
            // Вперёд — внутрь стола с учётом формы стадиона (вдоль прямых и торцов).
            root.transform.SetPositionAndRotation(at, Table3DLayout.SeatRotation(angle));
            var seat = root.AddComponent<PlayerSeat3DView>();
            seat.PlayerIndex = playerIndex;
            seat._persona = Personalities[Mathf.Abs(playerIndex) % Personalities.Length];
            seat._rngState = (uint)(playerIndex * 747796405 + 2891336453);
            seat._breathPhase = seat.Next01() * Mathf.PI * 2f;
            seat.PickNextGlance(0f, immediate: true);
            seat.PickNextShift(0f, immediate: true);
            seat.BuildFigure(playerIndex, headId);
            var glassAt = Table3DLayout.GlassPosition(angle);
            seat.Glass = Table3DArt.SpawnGlass(root.transform, glassAt, Table3DLayout.GlassRotation(angle, glassAt), "Glass Seat " + playerIndex);
            return seat;
        }

        public void SetHolding(bool holding)
        {
            _holding = holding;
            if (_fan != null) _fan.SetActive(holding);
        }

        /// <summary>Надевает 3D-голову друга (например, "Head_Marat") на сокет Socket_Head без перерига тела.</summary>
        public bool MountHead(string headId)
        {
            if (_mountedHead != null)
            {
                _mountedHead.SetActive(false);
                Destroy(_mountedHead);
                _mountedHead = null;
            }
            if (string.IsNullOrEmpty(headId) || HeadSocket == null) return false;
            var headPrefab = Resources.Load<GameObject>("ThreeD/Characters/Heads/" + headId);
            if (headPrefab == null) return false;
            _mountedHead = Instantiate(headPrefab, HeadSocket, false);
            _mountedHead.name = headId;
            _mountedHead.transform.localPosition = Vector3.zero;
            _mountedHead.transform.localRotation = Quaternion.identity;
            _mountedHead.transform.localScale = Vector3.one;
            var mat = Table3DArt.Template(headId);
            foreach (var r in _mountedHead.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = r.sharedMaterials;
                for (var i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
            }
            ApplyHandSkinTone(headId);
            ApplyNeckSkinTone(headId, mat);
            return true;
        }

        private static Material LoadHandsMaterial(string headId)
        {
            var tone = AvatarCatalog.HandSkinToneOf(headId);
            var mat = Resources.Load<Material>(AvatarCatalog.OpponentHandsMaterialFor(tone))
                ?? Resources.Load<Material>("ThreeD/Materials/OpponentHands");
            if (mat != null) return mat;
            var handsMap = Resources.Load<Texture2D>(AvatarCatalog.HandSkinTextureResourceFor(tone))
                ?? Resources.Load<Texture2D>("ThreeD/Hands/TripoHands_BaseColor");
            return handsMap != null
                ? Table3DArt.Lit("tripo hand skin " + tone, Color.white, tone == AvatarCatalog.HandSkinTone.Aged ? .12f : .16f, 0f, handsMap)
                : Table3DArt.Template("ArmSkin_Smooth");
        }

        private void ApplyHandSkinTone(string headId)
        {
            if (_bodyRoot == null) return;
            var handsMat = LoadHandsMaterial(headId);
            foreach (var smr in _bodyRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mats = smr.sharedMaterials;
                var changed = false;
                for (var i = 0; i < mats.Length; i++)
                {
                    var slotName = mats[i] != null ? mats[i].name : "";
                    if (i == 1 || slotName.Contains("HandsSkin"))
                    {
                        mats[i] = handsMat;
                        changed = true;
                    }
                }
                if (changed) smr.sharedMaterials = mats;
            }
        }

        private static Color FriendNeckSkinColor(string headId)
        {
            // Точные оттенки нижней шеи каждого друга с поправкой на верхнюю лампу над столом (ключицы смотрят вверх и ловят больше света):
            // тон моста ключиц и шеи совпадает 1-в-1 без видимой ступеньки яркости!
            switch (headId)
            {
                case "Head_Ali":    return new Color(.51f, .35f, .28f, 1f);
                case "Head_Aslan":  return new Color(.44f, .28f, .22f, 1f);
                case "Head_Danil":  return new Color(.50f, .32f, .25f, 1f);
                case "Head_Marat":  return new Color(.52f, .33f, .25f, 1f);
                case "Head_Yegor":  return new Color(.59f, .40f, .32f, 1f);
                case "Head_Yelnur": return new Color(.51f, .32f, .25f, 1f);
                default:            return new Color(.52f, .34f, .27f, 1f);
            }
        }

        private void ApplyNeckSkinTone(string headId, Material headMat)
        {
            if (_bodyRoot == null) return;
            var skinColor = FriendNeckSkinColor(headId);
            if (_neckSkinMat == null)
            {
                var skinShader = headMat != null ? headMat.shader : (Shader.Find("CardCity/Skin") ?? Shader.Find("Universal Render Pipeline/Lit"));
                _neckSkinMat = new Material(skinShader) { name = "NeckBridge_" + (headId ?? "Default") };
                _neckSkinMat.SetTexture("_BaseMap", Texture2D.whiteTexture);
                _neckSkinMat.SetTexture("_BumpMap", null);
                _neckSkinMat.DisableKeyword("_NORMALMAP");
                _neckSkinMat.SetFloat("_Smoothness", .20f);
                _neckSkinMat.SetFloat("_Wrap", .58f);
                _neckSkinMat.SetFloat("_Scatter", .32f);
                _neckSkinMat.SetColor("_SubsurfaceColor", new Color(.96f, .48f, .36f, 1f));
                _neckSkinMat.SetFloat("_DiffuseSoftness", .68f);
                _neckSkinMat.SetFloat("_NailTint", 0f);
                _neckSkinMat.SetFloat("_CreaseRoughness", 0f);
                _neckSkinMat.SetFloat("_EnvironmentReflections", 0f);
                _neckSkinMat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            }
            _neckSkinMat.SetColor("_BaseColor", skinColor);
            foreach (var smr in _bodyRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mats = smr.sharedMaterials;
                if (mats.Length > 2)
                {
                    mats[2] = _neckSkinMat;
                    smr.sharedMaterials = mats;
                }
            }
        }

        /// <summary>Переключает сидячую эмоцию соперника (SitIdle, SitTalk, SitDisbelief или любой будущий клип Mixamo).</summary>
        public void PlayReaction(string stateName, float fadeSeconds = .22f)
        {
            if (_animator != null && !string.IsNullOrEmpty(stateName))
                _animator.CrossFadeInFixedTime(stateName, fadeSeconds, 0);
        }

        /// <summary>Плавно переводит взгляд персонажа на событие (игрока, карту на столе, твои жесты).</summary>
        public void LookAtWorld(Vector3 worldPos, float duration = 1.6f)
        {
            _lookWorldTarget = worldPos;
            _hasLookOverride = true;
            _lookUntil = Time.time + Mathf.Max(.2f, duration);
        }

        /// <summary>Сетевой API под мультиплеер: задаёт углы головы удалённого игрока в реальном времени.</summary>
        public void SetRemoteHeadLook(float yawDeg, float pitchDeg)
        {
            _hasRemoteLook = true;
            _remoteYaw = Mathf.Clamp(yawDeg, -54f, 54f);
            _remotePitch = Mathf.Clamp(pitchDeg, -18f, 28f);
        }

        public void ClearRemoteHeadLook() => _hasRemoteLook = false;

        /// <summary>Плавный живой кивок головой (nod=true) или покачивание (nod=false).</summary>
        public void PlayHeadMotion(bool nod, float duration = 1.15f)
        {
            _headMotionIsNod = nod;
            _headMotionTime = 0f;
            _headMotionDuration = Mathf.Max(.55f, duration);
        }

        /// <summary>
        /// Бот (или удалённый игрок) берёт карту из своего веера правой рукой, пригибается корпусом вперёд,
        /// дотягивается до слота на столе и кладёт карту прямо на сукно!
        /// </summary>
        public void PlayCardThrow(Card3DView card, Vector3 targetWorldPos, Quaternion targetWorldRot, float targetScale)
        {
            if (card == null || _rightUpperArm == null || _rightLowerArm == null || _rightHand == null) return;
            ReleaseHeldCardIfAny();
            _actionKind = ActionKind.ThrowCard;
            _actionTime = 0f;
            _actionDuration = 1.48f;
            _thrownCard = card;
            _thrownTargetPos = targetWorldPos;
            _thrownTargetRot = targetWorldRot;
            _thrownTargetScale = targetScale;
            _thrownPicked = false;
            _thrownReleased = false;
            _thrownStartPos = HandAnchor != null ? HandAnchor.position : transform.TransformPoint(new Vector3(-.11f, .88f, .20f));
            _thrownStartRot = HandAnchor != null ? HandAnchor.rotation : transform.rotation;
            _lastCardWorldPos = _thrownStartPos;

            card.Hold();
            card.transform.SetParent(null, true);
            card.transform.position = _thrownStartPos;
            card.transform.rotation = _thrownStartRot;
            card.transform.localScale = Vector3.one * 1.05f;
            LookAtWorld(targetWorldPos, 1.95f);
        }

        /// <summary>Пригибается вперёд и сгребающим движением правой руки притягивает карты со стола к своему вееру.</summary>
        public void PlayTakeReach(Vector3 gatherWorldPos)
        {
            if (_rightUpperArm == null || _rightLowerArm == null || _rightHand == null) return;
            ReleaseHeldCardIfAny();
            _actionKind = ActionKind.TakeReach;
            _actionTime = 0f;
            _actionDuration = 1.44f;
            _reachWorldTarget = gatherWorldPos;
            LookAtWorld(gatherWorldPos, 1.85f);
        }

        /// <summary>
        /// Показывает живой жест правой рукой (поддерживает все 7 жестов меню игрока: ThumbsUp, ThumbsDown,
        /// Fist, Point, TapTable, Shrug, MiddleFinger + Nod, ShakeHead, Disbelief). Готово к мультиплееру!
        /// </summary>
        public void PlayGesture(string gestureId, Vector3? targetWorld = null)
        {
            if (string.IsNullOrEmpty(gestureId)) return;
            if (gestureId == "Nod") { PlayHeadMotion(true, 1.15f); return; }
            if (gestureId == "ShakeHead") { PlayHeadMotion(false, 1.30f); return; }
            if (_actionKind == ActionKind.ThrowCard && !_thrownReleased) return;
            if (_rightUpperArm == null || _rightLowerArm == null || _rightHand == null) return;

            _actionKind = ActionKind.Gesture;
            _actionTime = 0f;
            _gestureId = gestureId;
            _gesturePointWorld = targetWorld;
            _actionDuration = gestureId == "TapTable" ? 3.20f
                : gestureId == "Shrug" ? 3.10f
                : gestureId == "Disbelief" ? 3.15f
                : 3.35f;

            if (gestureId == "ThumbsUp") PlayHeadMotion(true, 1.30f);
            else if (gestureId == "ThumbsDown") PlayHeadMotion(false, 1.45f);
            else if (gestureId == "Disbelief") PlayHeadMotion(false, 1.65f);
            if (targetWorld.HasValue) LookAtWorld(targetWorld.Value, _actionDuration + .35f);
        }

        public void CancelAction()
        {
            ReleaseHeldCardIfAny();
            _actionKind = ActionKind.None;
            _actionTime = 0f;
            _gestureId = null;
        }

        private void ReleaseHeldCardIfAny()
        {
            if (_thrownCard != null && !_thrownReleased)
            {
                _thrownReleased = true;
                _thrownCard.LetGo();
            }
            _thrownCard = null;
        }

        private void BuildFigure(int index, string headId)
        {
            var chair = Table3DArt.Spawn("chair", transform);
            // Точно та же геометрия посадки, что у игрока (chair.z=-0.085, body.y=+0.058, body.z=-0.112, scale=0.92):
            // бёдра и голени лежат СТРОГО поверх бархатной подушки кресла и сгибаются за её передним краем — 0 провалов ног в стул!
            chair.transform.localPosition = new Vector3(0f, 0f, -.085f);
            chair.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            var hasCustomHead = !string.IsNullOrEmpty(headId)
                && Resources.Load<GameObject>("ThreeD/Characters/Heads/" + headId) != null;
            var archetype = AvatarCatalog.BodyArchetypeOf(headId);
            GameObject bodyPrefab = null;
            if (hasCustomHead)
            {
                bodyPrefab = Resources.Load<GameObject>(AvatarCatalog.HeadlessBodyResourceFor(archetype))
                    ?? Resources.Load<GameObject>("ThreeD/Characters/BaseMale_Headless");
            }
            if (bodyPrefab == null) bodyPrefab = Resources.Load<GameObject>("ThreeD/Characters/BaseMale");

            if (bodyPrefab != null)
            {
                BuildRiggedCharacter(bodyPrefab, archetype, headId);
                if (hasCustomHead) MountHead(headId);
            }
            else BuildFallbackMannequin(index % FallbackSkin.Length);

            var anchor = new GameObject("Opponent hand anchor").transform;
            anchor.SetParent(transform, false);
            anchor.localPosition = new Vector3(-.11f, .86f, .24f);
            HandAnchor = anchor;
            _fan = new GameObject("Opponent fan");
            _fan.transform.SetParent(anchor, false);
            for (var i = 0; i < FanCards; i++)
            {
                var card = Card3DView.Create(_fan.transform, new Card(Rank.Nine, Suit.Clubs), false, false);
                var spin = Quaternion.Euler(0f, 0f, (FanCards - 1) / 2f * 10.5f - i * 10.5f);
                // Шаг 1.15 мм между картами веера (> 2x толщины карты): 0 Z-fighting и аккуратная плотная пачка при виде сбоку!
                card.Place(_fan.transform, spin * new Vector3(0f, .048f, 0f) + new Vector3(0f, 0f, i * .00115f), spin, 1.05f);
            }
        }

        private void BuildRiggedCharacter(GameObject prefab, AvatarCatalog.BodyArchetype archetype, string headId)
        {
            var body = Instantiate(prefab, transform, false);
            body.name = "Character_" + archetype;
            _bodyRoot = body.transform;

            // Точные пропорции и посадка под каждый типаж (мужской, плотный, женский, пожилой):
            // бёдра всегда лежат СТРОГО поверх бархатной подушки, колени чисто за передним краем стула!
            float bodyScale;
            switch (archetype)
            {
                case AvatarCatalog.BodyArchetype.Female:
                    bodyScale = .885f;
                    _bodyBaseLocalPos = new Vector3(0f, .062f, -.108f);
                    _elderPosturePitch = 0f;
                    _labelHeight = 1.44f;
                    break;
                case AvatarCatalog.BodyArchetype.FemaleElder:
                    bodyScale = .865f;
                    _bodyBaseLocalPos = new Vector3(0f, .064f, -.106f);
                    _elderPosturePitch = 3.5f;
                    _labelHeight = 1.42f;
                    break;
                case AvatarCatalog.BodyArchetype.MaleHeavy:
                    bodyScale = .945f;
                    _bodyBaseLocalPos = new Vector3(0f, .056f, -.116f);
                    _elderPosturePitch = 0f;
                    _labelHeight = 1.50f;
                    break;
                case AvatarCatalog.BodyArchetype.MaleElder:
                    bodyScale = .91f;
                    _bodyBaseLocalPos = new Vector3(0f, .058f, -.112f);
                    _elderPosturePitch = 3.0f;
                    _labelHeight = 1.47f;
                    break;
                default:
                    bodyScale = .92f;
                    _bodyBaseLocalPos = new Vector3(0f, .058f, -.112f);
                    _elderPosturePitch = 0f;
                    _labelHeight = 1.48f;
                    break;
            }

            body.transform.localPosition = _bodyBaseLocalPos;
            body.transform.localRotation = Quaternion.identity;
            body.transform.localScale = Vector3.one * bodyScale;

            var outfitMat = Table3DArt.Template("BodyMale_Outfit");
            var handsMat = LoadHandsMaterial(headId);

            foreach (var smr in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mats = smr.sharedMaterials;
                for (var i = 0; i < mats.Length; i++)
                {
                    var slotName = mats[i] != null ? mats[i].name : "";
                    if (i == 2 || slotName.Contains("NeckSkin"))
                    {
                        if (_neckSkinMat == null) ApplyNeckSkinTone(null, null);
                        mats[i] = _neckSkinMat;
                    }
                    else
                    {
                        mats[i] = (i == 1 || slotName.Contains("HandsSkin")) ? handsMat : outfitMat;
                    }
                }
                smr.sharedMaterials = mats;
                smr.updateWhenOffscreen = true;
            }

            _animator = body.GetComponent<Animator>();
            if (_animator == null) _animator = body.AddComponent<Animator>();
            if (_animator.avatar == null || !_animator.avatar.isHuman)
            {
                var headlessPrefab = Resources.Load<GameObject>("ThreeD/Characters/BaseMale_Headless");
                var headlessAnim = headlessPrefab != null ? headlessPrefab.GetComponent<Animator>() : null;
                if (headlessAnim != null && headlessAnim.avatar != null) _animator.avatar = headlessAnim.avatar;
            }

            _spineBone = _animator.GetBoneTransform(HumanBodyBones.Spine);
            _chestBone = _animator.GetBoneTransform(HumanBodyBones.Chest);
            _upperChestBone = _animator.GetBoneTransform(HumanBodyBones.UpperChest);
            _neckBone = _animator.GetBoneTransform(HumanBodyBones.Neck);
            _headBone = _animator.GetBoneTransform(HumanBodyBones.Head) ?? FindDeep(body.transform, "mixamorig:Head");
            _leftShoulder = _animator.GetBoneTransform(HumanBodyBones.LeftShoulder);
            _rightShoulder = _animator.GetBoneTransform(HumanBodyBones.RightShoulder);

            // Сохраняем идеально гладкие исходные углы ВСЕХ фаланг левой кисти ДО запуска SitIdle,
            // чтобы переразгиб пальцев из клипа SitIdle никогда не складывал кожу на суставах!
            _leftUpperArm = _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            _leftLowerArm = _animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            _leftHand = _animator.GetBoneTransform(HumanBodyBones.LeftHand);
            _leftIndex1 = _animator.GetBoneTransform(HumanBodyBones.LeftIndexProximal);
            _leftMiddle1 = _animator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
            _leftRing1 = _animator.GetBoneTransform(HumanBodyBones.LeftRingProximal);
            _leftLittle1 = _animator.GetBoneTransform(HumanBodyBones.LeftLittleProximal);
            _leftThumb1 = _animator.GetBoneTransform(HumanBodyBones.LeftThumbProximal);
            _leftThumb2 = _animator.GetBoneTransform(HumanBodyBones.LeftThumbIntermediate);
            var t3 = _animator.GetBoneTransform(HumanBodyBones.LeftThumbDistal);
            var i2 = _animator.GetBoneTransform(HumanBodyBones.LeftIndexIntermediate);
            var i3 = _animator.GetBoneTransform(HumanBodyBones.LeftIndexDistal);
            var m2 = _animator.GetBoneTransform(HumanBodyBones.LeftMiddleIntermediate);
            var m3 = _animator.GetBoneTransform(HumanBodyBones.LeftMiddleDistal);
            _leftMiddle3 = m3;
            var r2 = _animator.GetBoneTransform(HumanBodyBones.LeftRingIntermediate);
            var r3 = _animator.GetBoneTransform(HumanBodyBones.LeftRingDistal);
            var l2 = _animator.GetBoneTransform(HumanBodyBones.LeftLittleIntermediate);
            var l3 = _animator.GetBoneTransform(HumanBodyBones.LeftLittleDistal);
            _allLeftFingerBones = new[]
            {
                _leftThumb1, _leftThumb2, t3,
                _leftIndex1, i2, i3,
                _leftMiddle1, m2, m3,
                _leftRing1, r2, r3,
                _leftLittle1, l2, l3,
            };
            _allLeftFingerRestLocal = new Quaternion[_allLeftFingerBones.Length];
            for (var f = 0; f < _allLeftFingerBones.Length; f++)
                _allLeftFingerRestLocal[f] = _allLeftFingerBones[f] != null ? _allLeftFingerBones[f].localRotation : Quaternion.identity;

            _curlFingers = new[] { _leftIndex1, i2, _leftMiddle1, m2, _leftRing1, r2, _leftLittle1, l2 };
            _fingerRestLocal = new Quaternion[_curlFingers.Length];
            for (var f = 0; f < _curlFingers.Length; f++)
                _fingerRestLocal[f] = _curlFingers[f] != null ? _curlFingers[f].localRotation : Quaternion.identity;

            _backFingerBones = new[]
            {
                _leftIndex1, i2, i3, i3 != null && i3.childCount > 0 ? i3.GetChild(0) : null,
                _leftMiddle1, m2, m3, m3 != null && m3.childCount > 0 ? m3.GetChild(0) : null,
                _leftRing1, r2, r3, r3 != null && r3.childCount > 0 ? r3.GetChild(0) : null,
                _leftLittle1, l2, l3, l3 != null && l3.childCount > 0 ? l3.GetChild(0) : null,
            };

            // Сохраняем правую руку и исходные гладкие углы всех 15 фаланг правой кисти ДО запуска SitIdle
            // (для броска карт на стол, сгребания и всех 7 жестов правой рукой!).
            _rightUpperArm = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            _rightLowerArm = _animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            _rightHand = _animator.GetBoneTransform(HumanBodyBones.RightHand);
            _rightIndex1 = _animator.GetBoneTransform(HumanBodyBones.RightIndexProximal);
            _rightMiddle1 = _animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            _rightLittle1 = _animator.GetBoneTransform(HumanBodyBones.RightLittleProximal);
            _rightThumb1 = _animator.GetBoneTransform(HumanBodyBones.RightThumbProximal);
            _rightThumb2 = _animator.GetBoneTransform(HumanBodyBones.RightThumbIntermediate);
            var rt3 = _animator.GetBoneTransform(HumanBodyBones.RightThumbDistal);
            var ri2 = _animator.GetBoneTransform(HumanBodyBones.RightIndexIntermediate);
            var ri3 = _animator.GetBoneTransform(HumanBodyBones.RightIndexDistal);
            var rm2 = _animator.GetBoneTransform(HumanBodyBones.RightMiddleIntermediate);
            var rm3 = _animator.GetBoneTransform(HumanBodyBones.RightMiddleDistal);
            var rr1 = _animator.GetBoneTransform(HumanBodyBones.RightRingProximal);
            var rr2 = _animator.GetBoneTransform(HumanBodyBones.RightRingIntermediate);
            var rr3 = _animator.GetBoneTransform(HumanBodyBones.RightRingDistal);
            var rl2 = _animator.GetBoneTransform(HumanBodyBones.RightLittleIntermediate);
            var rl3 = _animator.GetBoneTransform(HumanBodyBones.RightLittleDistal);
            _allRightFingerBones = new[]
            {
                _rightThumb1, _rightThumb2, rt3,
                _rightIndex1, ri2, ri3,
                _rightMiddle1, rm2, rm3,
                rr1, rr2, rr3,
                _rightLittle1, rl2, rl3,
            };
            _allRightFingerRestLocal = new Quaternion[_allRightFingerBones.Length];
            _smoothRightFingers = new Quaternion[_allRightFingerBones.Length];
            for (var f = 0; f < _allRightFingerBones.Length; f++)
            {
                _allRightFingerRestLocal[f] = _allRightFingerBones[f] != null ? _allRightFingerBones[f].localRotation : Quaternion.identity;
                _smoothRightFingers[f] = _allRightFingerRestLocal[f];
            }
            _rightFingerTipCheck = new[]
            {
                rt3 != null && rt3.childCount > 0 ? rt3.GetChild(0) : rt3,
                ri3 != null && ri3.childCount > 0 ? ri3.GetChild(0) : ri3,
                rm3 != null && rm3.childCount > 0 ? rm3.GetChild(0) : rm3,
                rr3 != null && rr3.childCount > 0 ? rr3.GetChild(0) : rr3,
                rl3 != null && rl3.childCount > 0 ? rl3.GetChild(0) : rl3,
            };

            var ctrl = Resources.Load<RuntimeAnimatorController>("ThreeD/Characters/OpponentAnimator");
            if (ctrl != null)
            {
                _animator.runtimeAnimatorController = ctrl;
                _animator.applyRootMotion = false;
                _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                // Сначала фиксируем чистую ровную сидячую позу (кадр 0 SitIdle) как эталон для гашения «завала» корпуса вперёд!
                _animator.Play("SitIdle", 0, 0f);
                _animator.Update(0f);
                if (_spineBone != null) _spineNeutral = _spineBone.localRotation;
                if (_chestBone != null) _chestNeutral = _chestBone.localRotation;
                if (_upperChestBone != null) _upperChestNeutral = _upperChestBone.localRotation;
                if (_neckBone != null) _neckNeutral = _neckBone.localRotation;
                if (_headBone != null) _headNeutral = _headBone.localRotation;
                if (_rightUpperArm != null) _rightUpperNeutral = _rightUpperArm.localRotation;
                if (_rightLowerArm != null) _rightLowerNeutral = _rightLowerArm.localRotation;
                if (_rightHand != null) _rightHandNeutral = _rightHand.localRotation;
                if (_rightUpperArm != null && _rightLowerArm != null && _rightHand != null)
                {
                    var uDir = (_rightLowerArm.position - _rightUpperArm.position).normalized;
                    var bendWorld = Vector3.ProjectOnPlane(_rightHand.position - _rightLowerArm.position, uDir);
                    _rightElbowBendLocal = bendWorld.sqrMagnitude > 1e-4f
                        ? _rightUpperArm.InverseTransformDirection(bendWorld.normalized).normalized
                        : _rightUpperArm.InverseTransformDirection(transform.forward).normalized;
                }

                // Затем запускаем каждого соперника со своей фазой и темпом.
                _animator.speed = _persona.AnimSpeed;
                _animator.Play("SitIdle", 0, _persona.StartPhase);
                _animator.Update(0f);
            }

            if (_headBone != null)
            {
                HeadSocket = CreateSocket(_headBone, "Socket_Head", Vector3.zero);
                GlassesSocket = CreateSocket(_headBone, "Socket_Glasses", new Vector3(0f, .095f, .09f));
                var headTop = FindDeep(_headBone, "mixamorig:HeadTop_End");
                HatSocket = CreateSocket(headTop != null ? headTop : _headBone, "Socket_Hat", headTop != null ? Vector3.zero : new Vector3(0f, .20f, 0f));
            }
        }

        private void LateUpdate()
        {
            var t = Time.time;
            var dt = Time.deltaTime;
            var isIdle = _animator == null || _animator.GetCurrentAnimatorStateInfo(0).IsName("SitIdle");

            // Асимметричное человеческое дыхание (вдох чуть короче, выдох плавнее) + медленная модуляция глубины.
            var phase = t * Mathf.PI * 2f * _persona.BreathHz + _breathPhase;
            var breathWave = Mathf.Sin(phase) + .22f * Mathf.Sin(phase * 2f - .6f);
            var breathDepth = .85f + .15f * Mathf.Sin(t * .17f + _breathPhase * 1.7f);
            var breath = breathWave * breathDepth;

            // Обновляем таймер текущего действия (бросок карты / взятие / жест) и считаем наклон корпуса + подачу плеча вперёд.
            var wantLeanPitch = 0f;
            var wantLeanYaw = 0f;
            var wantShrug = 0f;
            var wantBodySlideZ = 0f;
            var wantBodyLiftY = 0f;
            var wantShoulderReach = 0f;
            var actionHeadRoll = 0f;
            if (_actionKind != ActionKind.None)
            {
                _actionTime += dt;
                if (_actionTime >= _actionDuration)
                {
                    ReleaseHeldCardIfAny();
                    _actionKind = ActionKind.None;
                    _gestureId = null;
                }
                else
                {
                    ComputeActionTorso(out wantLeanPitch, out wantLeanYaw, out wantShrug, out wantBodySlideZ, out wantBodyLiftY, out wantShoulderReach, out actionHeadRoll);
                }
            }
            var leanSmooth = 1f - Mathf.Exp(-dt * 5.4f);
            _curLeanPitch = Mathf.Lerp(_curLeanPitch, wantLeanPitch, leanSmooth);
            _curLeanYaw = Mathf.Lerp(_curLeanYaw, wantLeanYaw, leanSmooth);
            _curShrug = Mathf.Lerp(_curShrug, wantShrug, leanSmooth);
            _curBodySlideZ = Mathf.Lerp(_curBodySlideZ, wantBodySlideZ, leanSmooth);
            _curBodyLiftY = Mathf.Lerp(_curBodyLiftY, wantBodyLiftY, leanSmooth);
            _curShoulderReach = Mathf.Lerp(_curShoulderReach, wantShoulderReach, leanSmooth);
            if (_bodyRoot != null)
                _bodyRoot.localPosition = _bodyBaseLocalPos + new Vector3(0f, _curBodyLiftY, _curBodySlideZ);

            // Редкие полностью независимые смены осанки (раз в 5–15 сек каждый сам по себе).
            if (t >= _nextShiftAt) PickNextShift(t, immediate: false);
            var shiftSmooth = 1f - Mathf.Exp(-dt * 1.1f);
            _curShiftPitch = Mathf.Lerp(_curShiftPitch, _targetShiftPitch, shiftSmooth);
            _curShiftYaw = Mathf.Lerp(_curShiftYaw, _targetShiftYaw, shiftSmooth);
            _curShiftRoll = Mathf.Lerp(_curShiftRoll, _targetShiftRoll, shiftSmooth);

            // Взгляд: приоритет 1 — сетевой ввод головы (мультиплеер), 2 — взгляд на событие стола, 3 — естественные паузы.
            float goalYaw, goalPitch, goalRoll;
            if (_hasRemoteLook)
            {
                goalYaw = _remoteYaw;
                goalPitch = _remotePitch;
                goalRoll = 0f;
            }
            else if (_hasLookOverride && t < _lookUntil)
            {
                var localDir = transform.InverseTransformPoint(_lookWorldTarget) - new Vector3(0f, 1.18f, .05f);
                goalYaw = Mathf.Clamp(Mathf.Atan2(localDir.x, Mathf.Max(.10f, localDir.z)) * Mathf.Rad2Deg, -54f, 54f);
                var horiz = Mathf.Sqrt(localDir.x * localDir.x + localDir.z * localDir.z);
                goalPitch = Mathf.Clamp(-Mathf.Atan2(localDir.y, Mathf.Max(.18f, horiz)) * Mathf.Rad2Deg - _persona.HeadPitchBase, -12f, 22f);
                goalRoll = actionHeadRoll;
            }
            else
            {
                _hasLookOverride = false;
                if (t >= _nextGlanceAt) PickNextGlance(t, immediate: false);
                goalYaw = _targetYaw;
                goalPitch = _targetPitch;
                goalRoll = _targetRoll + actionHeadRoll;
            }

            var lookRate = (_hasRemoteLook || _hasLookOverride) ? 4.4f : 2.1f;
            var glanceSmooth = 1f - Mathf.Exp(-dt * lookRate);
            _curYaw = Mathf.Lerp(_curYaw, goalYaw, glanceSmooth);
            _curPitch = Mathf.Lerp(_curPitch, goalPitch, glanceSmooth);
            _curRoll = Mathf.Lerp(_curRoll, goalRoll, glanceSmooth);

            // Плавный живой кивок или покачивание головой поверх текущего взгляда.
            var extraHeadPitch = 0f;
            var extraHeadYaw = 0f;
            if (_headMotionDuration > 0f)
            {
                _headMotionTime += dt;
                if (_headMotionTime >= _headMotionDuration)
                {
                    _headMotionDuration = 0f;
                }
                else
                {
                    var u = _headMotionTime / _headMotionDuration;
                    var env = SmoothPulse(u, 0f, .25f, .75f, 1f);
                    if (_headMotionIsNod) extraHeadPitch = Mathf.Sin(u * Mathf.PI * 2f) * 8.5f * env;
                    else extraHeadYaw = Mathf.Sin(u * Mathf.PI * 2.5f) * 10.5f * env;
                }
            }

            if (isIdle)
            {
                // Гасим цикличный «завал корпуса вперёд» из Mixamo SitIdle на 82% и добавляем живое дыхание,
                // индивидуальную посадку + реальный наклон корпуса при броске/взятии карт!
                if (_spineBone != null)
                {
                    _spineBone.localRotation = Quaternion.Slerp(_spineNeutral, _spineBone.localRotation, .18f);
                    _spineBone.localRotation *= Quaternion.Euler(
                        _persona.BaseSpinePitch + _elderPosturePitch + _curShiftPitch * .55f + _curLeanPitch * .45f,
                        _persona.BaseSpineYaw + _curShiftYaw * .60f + _curLeanYaw * .45f,
                        _curShiftRoll * .55f);
                }
                if (_chestBone != null)
                {
                    _chestBone.localRotation = Quaternion.Slerp(_chestNeutral, _chestBone.localRotation, .18f);
                    _chestBone.localRotation *= Quaternion.Euler(
                        -breath * _persona.BreathAmp + _curShiftPitch * .45f + _curLeanPitch * .35f,
                        _curShiftYaw * .40f + _curLeanYaw * .35f,
                        _curShiftRoll * .45f);
                }
                if (_upperChestBone != null)
                {
                    _upperChestBone.localRotation = Quaternion.Slerp(_upperChestNeutral, _upperChestBone.localRotation, .18f);
                    _upperChestBone.localRotation *= Quaternion.Euler(
                        -breath * _persona.BreathAmp * .55f + _curLeanPitch * .20f,
                        _curLeanYaw * .20f,
                        0f);
                }
                // Лёгкий подъём плеч на вдохе + явный подъём при Shrug + вынос правого плеча вперёд при выкладывании карт и жестах!
                var shoulderLift = breath * _persona.BreathAmp * .75f + _curShrug;
                if (_leftShoulder != null) _leftShoulder.localRotation *= Quaternion.Euler(0f, 0f, -shoulderLift);
                if (_rightShoulder != null)
                {
                    _rightShoulder.localRotation *= Quaternion.Euler(0f, 0f, shoulderLift);
                    if (Mathf.Abs(_curShoulderReach) > .05f)
                        _rightShoulder.rotation = Quaternion.AngleAxis(-_curShoulderReach, transform.up) * _rightShoulder.rotation;
                }

                var totalPitch = _curPitch + extraHeadPitch - _curLeanPitch * .45f;
                var totalYaw = _curYaw + extraHeadYaw - _curLeanYaw * .40f;
                if (_neckBone != null)
                {
                    _neckBone.localRotation = Quaternion.Slerp(_neckNeutral, _neckBone.localRotation, .15f);
                    _neckBone.localRotation *= Quaternion.Euler(totalPitch * .35f, totalYaw * .35f, 0f);
                }
                if (_headBone != null)
                {
                    _headBone.localRotation = Quaternion.Slerp(_headNeutral, _headBone.localRotation, .15f);
                    _headBone.localRotation *= Quaternion.Euler(_persona.HeadPitchBase + totalPitch * .65f, totalYaw * .65f, _curRoll);
                }
            }

            // Пока у соперника есть карты (_holding), левая рука ВСЕГДА держит веер — карты никогда не зависают в воздухе!
            var targetHold = _holding ? 1f : 0f;
            _holdWeight = Mathf.MoveTowards(_holdWeight, targetHold, dt * 3.5f);
            if (_holdWeight > .001f && _leftUpperArm != null && _leftLowerArm != null && _leftHand != null)
                PoseLeftHandHoldingFan(t, breath, _holdWeight);

            if (isIdle && _rightUpperArm != null && _rightLowerArm != null && _rightHand != null)
            {
                if (_actionKind != ActionKind.None)
                    UpdateRightArmAction(dt);
                else if (_hasSmoothRightArm)
                    DecayRightArmToIdle(dt);
            }
        }

        /// <summary>
        /// Двухкостная аналитическая IK левой руки: предплечье, запястье и кисть идут по одной плавной
        /// анатомической линии (излом в запястье ~10° вместо резкого выворота), а веер естественно лежит в ладони.
        /// </summary>
        private void PoseLeftHandHoldingFan(float t, float breath, float weight)
        {
            var breathLift = breath * .004f;
            var sway = Mathf.Sin(t * .47f + _breathPhase) * .005f;
            // Оптимальный карточный хват: при сильном наклоне к столу левая рука мягко уходит чуть ниже и левее,
            // чтобы веер карт никогда не упирался в подбородок/лицо и не цеплял золотой бортик!
            var leanFrac = Mathf.Clamp01(_curLeanPitch / 32f);
            var leanFwdShift = Mathf.Min(leanFrac * .065f + _curBodySlideZ * .62f, .10f);
            var leanDownShift = leanFrac * .055f - _curBodyLiftY * .35f;
            var leanOutLeft = leanFrac * .062f;
            var targetLocal = new Vector3(-.115f - leanOutLeft + sway, .895f + breathLift - leanDownShift, .185f + leanFwdShift) + _persona.HandOffset;
            var poleLocal = new Vector3(-.255f - leanOutLeft * .5f, .670f, .065f + _curBodySlideZ * .65f);

            var shoulder = _leftUpperArm.position;
            var elbow = _leftLowerArm.position;
            var wrist = _leftHand.position;
            var len1 = Vector3.Distance(shoulder, elbow);
            var len2 = Vector3.Distance(elbow, wrist);
            if (len1 < .05f || len2 < .05f) return;

            var targetWorld = Vector3.Lerp(wrist, transform.TransformPoint(targetLocal), weight);
            var poleWorld = transform.TransformPoint(poleLocal);

            var toTarget = targetWorld - shoulder;
            var dist = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(len1 - len2) + .01f, len1 + len2 - .005f);
            var dir = toTarget.normalized;
            var cosA = Mathf.Clamp((len1 * len1 + dist * dist - len2 * len2) / (2f * len1 * dist), -1f, 1f);
            var sinA = Mathf.Sqrt(Mathf.Max(0f, 1f - cosA * cosA));
            var bendAxis = Vector3.ProjectOnPlane(poleWorld - shoulder, dir);
            bendAxis = bendAxis.sqrMagnitude > 1e-5f ? bendAxis.normalized : -transform.up;

            var desiredElbow = shoulder + dir * (len1 * cosA) + bendAxis * (len1 * sinA);

            var curUpper = (elbow - shoulder).normalized;
            var newUpper = (desiredElbow - shoulder).normalized;
            _leftUpperArm.rotation = Quaternion.FromToRotation(curUpper, newUpper) * _leftUpperArm.rotation;

            var curLower = (_leftHand.position - _leftLowerArm.position).normalized;
            var newLower = (targetWorld - _leftLowerArm.position).normalized;
            _leftLowerArm.rotation = Quaternion.FromToRotation(curLower, newLower) * _leftLowerArm.rotation;

            // Естественный наклон кисти: предплечье мягко уходит в чашу ладони, слегка развёрнутую к центру груди
            // (так при согнутых на ~50° пальцах сам веер карт встаёт ровно и вертикально к глазам соперника!).
            var forearmDir = (_leftHand.position - _leftLowerArm.position).normalized;
            var wantFwd = (forearmDir * .62f + transform.up * .38f).normalized;
            var rawPalm = transform.TransformDirection(new Vector3(.34f, .18f, -.92f)).normalized;
            var wantPalm = Vector3.ProjectOnPlane(rawPalm, wantFwd).normalized;

            if (_leftMiddle1 != null && _leftThumb1 != null && _leftLittle1 != null)
            {
                var handPos = _leftHand.position;
                var curFwd = (_leftMiddle1.position - handPos).normalized;
                var curAcross = (_leftThumb1.position - _leftLittle1.position).normalized;
                var curPalm = Vector3.Cross(curAcross, curFwd).normalized;
                var curRot = Quaternion.LookRotation(curFwd, curPalm);
                var wantRot = Quaternion.LookRotation(wantFwd, wantPalm);
                _leftHand.rotation = Quaternion.Slerp(_leftHand.rotation, wantRot * Quaternion.Inverse(curRot) * _leftHand.rotation, weight);

                // 1. Сбрасываем все 15 фаланг левой кисти в чистую исходную позу (до переразгиба SitIdle!)
                if (_allLeftFingerBones != null)
                {
                    for (var i = 0; i < _allLeftFingerBones.Length; i++)
                    {
                        if (_allLeftFingerBones[i] != null)
                            _allLeftFingerBones[i].localRotation = Quaternion.Slerp(_allLeftFingerBones[i].localRotation, _allLeftFingerRestLocal[i], weight);
                    }
                }

                // 2. Плоский анатомичный прижим ВСЕМИ 4 пальцами вдоль рубашки веера:
                //    главный сгиб идёт в костяшках (MCP), а средние и ногтевые фаланги остаются почти прямыми;
                //    безымянный и мизинец чуть активнее выходят на единую плоскость со средним и указательным!
                if (_allLeftFingerBones != null && _allLeftFingerBones.Length >= 15)
                {
                    var curlAxis = Vector3.Cross(wantFwd, wantPalm).normalized;
                    // Индексы 3..14: Index(1,2,3), Middle(1,2,3), Ring(1,2,3), Little(1,2,3)
                    float[] curlDeg =
                    {
                        21f,  6f,  2f,   // указательный
                        21f,  6f,  2f,   // средний
                        22f,  7f,  2f,   // безымянный
                        20f,  9f,  3f,   // мизинец — выводится в единую плоскость со всеми пальцами
                    };
                    for (var i = 0; i < curlDeg.Length; i++)
                    {
                        var bone = _allLeftFingerBones[3 + i];
                        if (bone != null)
                            bone.rotation = Quaternion.AngleAxis(curlDeg[i] * weight, curlAxis) * bone.rotation;
                    }
                }
            }

            if (HandAnchor != null)
            {
                // Плоскость веера строится строго по РЕАЛЬНОЙ 3D-плоскости 4 опорных пальцев:
                // ось вверх = (средняя костяшка -> кончик среднего пальца), поперечная ось = (мизинец -> указательный).
                // Так веер НЕ перекошен на 22° относительно ладони и ВСЕ 4 пальца ложатся на рубашку вплотную без воздушного зазора!
                var handPos = _leftHand.position;
                var knucklePos = _leftMiddle1 != null ? _leftMiddle1.position : handPos + wantFwd * .075f;
                var tipAnchor = _leftMiddle3 != null
                    ? (_leftMiddle3.childCount > 0 ? _leftMiddle3.GetChild(0).position : _leftMiddle3.position)
                    : handPos + wantFwd * .145f;
                var fanUp = (tipAnchor - knucklePos).normalized;
                Vector3 fanPalm;
                if (_leftIndex1 != null && _leftLittle1 != null)
                {
                    var across = Vector3.ProjectOnPlane(_leftIndex1.position - _leftLittle1.position, fanUp);
                    fanPalm = across.sqrMagnitude > 1e-5f
                        ? Vector3.Cross(across.normalized, fanUp).normalized
                        : Vector3.ProjectOnPlane(wantPalm, fanUp).normalized;
                    if (Vector3.Dot(fanPalm, wantPalm) < 0f) fanPalm = -fanPalm;
                }
                else
                {
                    fanPalm = Vector3.ProjectOnPlane(wantPalm, fanUp).normalized;
                }
                var thumbSide = (_leftThumb1 != null && _leftLittle1 != null)
                    ? Vector3.ProjectOnPlane(Vector3.ProjectOnPlane(_leftThumb1.position - _leftLittle1.position, fanUp), fanPalm).normalized
                    : Vector3.Cross(fanUp, fanPalm).normalized;

                // Измеряем самую выступающую точку всех 4 опорных пальцев вдоль ИСТИННОЙ нормали fanPalm:
                // теперь все 4 пальца лежат на одной глубине (±2 мм), а задняя карта касается кожи подушечек (5.4 мм) — 0 зазора и 0 проколов!
                var maxFingerPalm = -1e5f;
                if (_backFingerBones != null)
                {
                    for (var i = 0; i < _backFingerBones.Length; i++)
                    {
                        var fb = _backFingerBones[i];
                        if (fb == null) continue;
                        var proj = Vector3.Dot(fb.position - knucklePos, fanPalm);
                        if (proj > maxFingerPalm) maxFingerPalm = proj;
                    }
                }
                if (maxFingerPalm < -1e4f) maxFingerPalm = .003f;

                var stackThickness = (FanCards - 1) * .00115f;
                const float fingerPadRadius = .0054f;
                var card0Shift = maxFingerPalm + stackThickness + fingerPadRadius;
                HandAnchor.position = knucklePos - fanUp * .006f + fanPalm * card0Shift + thumbSide * .003f;
                HandAnchor.rotation = Quaternion.LookRotation(-fanPalm, fanUp);

                // 3. Точный зажим БОЛЬШИМ ПАЛЬЦЕМ прямо по ЛИЦЕВОЙ стороне передней карты (HandAnchor.position + fanPalm * 6.2 мм):
                //    большой палец обхватывает край веера и плотно зажимает пачку навстречу 4 пальцам сзади!
                if (_leftThumb1 != null && _leftThumb2 != null)
                {
                    var t1Target = HandAnchor.position + fanUp * .018f + thumbSide * .022f + fanPalm * .015f;
                    var curT1 = (_leftThumb2.position - _leftThumb1.position).normalized;
                    var wantT1 = (t1Target - _leftThumb1.position).normalized;
                    if (wantT1.sqrMagnitude > 1e-4f)
                        _leftThumb1.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(curT1, wantT1), weight) * _leftThumb1.rotation;

                    var t3 = _allLeftFingerBones != null && _allLeftFingerBones.Length > 2 ? _allLeftFingerBones[2] : null;
                    if (t3 != null)
                    {
                        var t2Target = HandAnchor.position + fanUp * .034f - thumbSide * .004f + fanPalm * .0062f;
                        var curT2 = (t3.position - _leftThumb2.position).normalized;
                        var wantT2 = (t2Target - _leftThumb2.position).normalized;
                        if (wantT2.sqrMagnitude > 1e-4f)
                            _leftThumb2.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(curT2, wantT2), weight * .95f) * _leftThumb2.rotation;

                        if (t3.childCount > 0)
                        {
                            var t3Target = HandAnchor.position + fanUp * .046f - thumbSide * .024f + fanPalm * .0058f;
                            var curT3 = (t3.GetChild(0).position - t3.position).normalized;
                            var wantT3 = (t3Target - t3.position).normalized;
                            if (wantT3.sqrMagnitude > 1e-4f)
                                t3.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(curT3, wantT3), weight * .92f) * t3.rotation;
                        }
                        else
                        {
                            var tipAxis = Vector3.Cross(wantT2, -thumbSide).normalized;
                            if (tipAxis.sqrMagnitude > 1e-4f)
                                t3.rotation = Quaternion.AngleAxis(18f * weight, tipAxis) * t3.rotation;
                        }
                    }
                }
            }
        }

        private void ComputeActionTorso(out float leanPitch, out float leanYaw, out float shrug, out float bodySlideZ, out float bodyLiftY, out float shoulderReach, out float headRoll)
        {
            leanPitch = 0f;
            leanYaw = 0f;
            shrug = 0f;
            bodySlideZ = 0f;
            bodyLiftY = 0f;
            shoulderReach = 0f;
            headRoll = 0f;
            var u = _actionDuration > 1e-4f ? Mathf.Clamp01(_actionTime / _actionDuration) : 1f;

            if (_actionKind == ActionKind.ThrowCard)
            {
                // Глубокий естественный наклон вперёд к столу + подача корпуса и правого плеча,
                // чтобы бот почти дотягивался рукой до самого слота и реально КЛАЛ карту на сукно!
                var reachEnv = SmoothPulse(u, .10f, .46f, .70f, .98f);
                var localTgt = transform.InverseTransformPoint(_thrownTargetPos);
                var dist = Mathf.Clamp01((localTgt.z - .32f) / .65f);
                leanPitch = Mathf.Lerp(26f, 36f, dist) * reachEnv;
                leanYaw = Mathf.Clamp(localTgt.x * 22f, -14f, 14f) * reachEnv;
                bodySlideZ = Mathf.Lerp(.115f, .185f, dist) * reachEnv;
                bodyLiftY = .040f * reachEnv;
                shoulderReach = Mathf.Lerp(16f, 26f, dist) * reachEnv;
            }
            else if (_actionKind == ActionKind.TakeReach)
            {
                var reachEnv = SmoothPulse(u, .08f, .38f, .62f, .96f);
                var localTgt = transform.InverseTransformPoint(_reachWorldTarget);
                leanPitch = 24f * reachEnv;
                leanYaw = Mathf.Clamp(localTgt.x * 18f, -11f, 11f) * reachEnv;
                bodySlideZ = .11f * reachEnv;
                bodyLiftY = .020f * reachEnv;
                shoulderReach = 16f * reachEnv;
            }
            else if (_actionKind == ActionKind.Gesture)
            {
                var env = SmoothPulse(u, .00f, .24f, .76f, 1.00f);
                // Корпус мягко разворачивается в сторону взгляда — плечи, голова и жест всегда смотрят на одного и того же человека!
                if (_gestureId != "TapTable")
                    leanYaw = Mathf.Clamp(_curYaw * .35f, -14f, 14f) * env;
                if (_gestureId == "TapTable")
                {
                    leanPitch = 14f * env;
                    bodySlideZ = .105f * env;
                    shoulderReach = 12f * env;
                }
                else if (_gestureId == "Shrug")
                {
                    leanPitch = 5.5f * env;
                    shrug = 11.5f * env;
                    headRoll = 8.5f * env;
                    bodySlideZ = .060f * env;
                    shoulderReach = 9f * env;
                }
                else if (_gestureId == "Disbelief")
                {
                    leanPitch = 2.0f * env;
                    shrug = 6.5f * env;
                    bodySlideZ = .065f * env;
                    shoulderReach = 10f * env;
                    headRoll = Mathf.Sin(u * Mathf.PI * 2f) * 5.5f * env;
                }
                else if (_gestureId == "MiddleFinger")
                {
                    leanPitch = 12.5f * env;
                    bodySlideZ = .092f * env;
                    shoulderReach = 16f * env;
                    headRoll = -3.5f * env;
                }
                else if (_gestureId == "ThumbsUp" || _gestureId == "ThumbsDown" || _gestureId == "Fist")
                {
                    leanPitch = 11.5f * env;
                    bodySlideZ = .088f * env;
                    shoulderReach = 15f * env;
                }
                else if (_gestureId == "Point")
                {
                    leanPitch = 13.5f * env;
                    bodySlideZ = .098f * env;
                    shoulderReach = 18f * env;
                }
            }
        }

        private void UpdateRightArmAction(float dt)
        {
            var u = _actionDuration > 1e-4f ? Mathf.Clamp01(_actionTime / _actionDuration) : 1f;
            if (_actionKind == ActionKind.ThrowCard) PoseRightCardThrow(u, dt);
            else if (_actionKind == ActionKind.TakeReach) PoseRightTakeReach(u, dt);
            else if (_actionKind == ActionKind.Gesture) PoseRightGesture(u, dt);
        }

        private void DecayRightArmToIdle(float dt)
        {
            var idleUpper = _rightUpperArm.localRotation;
            var idleLower = _rightLowerArm.localRotation;
            var idleHand = _rightHand.localRotation;
            var k = 1f - Mathf.Exp(-dt * 7.5f);
            _smoothRightUpper = Quaternion.Slerp(_smoothRightUpper, idleUpper, k);
            _smoothRightLower = Quaternion.Slerp(_smoothRightLower, idleLower, k);
            _smoothRightHand = Quaternion.Slerp(_smoothRightHand, idleHand, k);
            _rightUpperArm.localRotation = _smoothRightUpper;
            _rightLowerArm.localRotation = _smoothRightLower;
            _rightHand.localRotation = _smoothRightHand;

            var stillMoving = Quaternion.Angle(_smoothRightUpper, idleUpper) > .4f
                || Quaternion.Angle(_smoothRightLower, idleLower) > .4f
                || Quaternion.Angle(_smoothRightHand, idleHand) > .4f;
            if (_allRightFingerBones != null && _smoothRightFingers != null)
            {
                for (var i = 0; i < _allRightFingerBones.Length; i++)
                {
                    var b = _allRightFingerBones[i];
                    if (b == null) continue;
                    _smoothRightFingers[i] = Quaternion.Slerp(_smoothRightFingers[i], b.localRotation, k);
                    b.localRotation = _smoothRightFingers[i];
                }
            }
            _smoothRightWeight = Mathf.MoveTowards(_smoothRightWeight, 0f, dt * 3.0f);
            if (_smoothRightWeight <= .001f && !stillMoving)
                _hasSmoothRightArm = false;
        }

        private void PoseRightCardThrow(float u, float dt)
        {
            var shoulder = _rightUpperArm.position;
            var len1 = Vector3.Distance(shoulder, _rightLowerArm.position);
            var len2 = Vector3.Distance(_rightLowerArm.position, _rightHand.position);
            if (len1 < .05f || len2 < .05f) return;

            // Точка захвата чуть спереди и правее верхнего края веера, чтобы правая рука не врезалась в грудь и левую кисть!
            var fanTopWorld = HandAnchor != null
                ? HandAnchor.position + transform.up * .076f + transform.right * .050f + transform.forward * .046f
                : transform.TransformPoint(new Vector3(-.05f, .93f, .27f));

            // Запястье доходит почти до самого слота (на 5 см ближе к игроку, чтобы пальцы и карта ложились точно над целью на высоте ~3.5 см!).
            var toSlotHoriz = Vector3.ProjectOnPlane(_thrownTargetPos - shoulder, Vector3.up);
            toSlotHoriz = toSlotHoriz.sqrMagnitude > 1e-4f ? toSlotHoriz.normalized : transform.forward;
            var desiredDropWorld = _thrownTargetPos + Vector3.up * .072f - toSlotHoriz * .056f;
            var toDrop = desiredDropWorld - shoulder;
            var maxReach = (len1 + len2) * .98f;
            var reachDropWorld = toDrop.magnitude > maxReach
                ? shoulder + toDrop.normalized * maxReach
                : desiredDropWorld;
            reachDropWorld.y = Mathf.Max(reachDropWorld.y, Table3DLayout.TableY + .074f);

            var restWorld = _rightHand.position;
            // Контрольная точка возврата строго ПОЗАДИ и ВЫШЕ золотого бортика (z=0.20, y=+16 см над столом) — 0 прохождений сквозь стол и стакан!
            var clearRimWorld = transform.TransformPoint(new Vector3(.14f, Table3DLayout.TableY + .165f, .20f));
            var liftEndWorld = reachDropWorld + Vector3.up * .085f - toSlotHoriz * .055f;
            var poleAtFan = transform.TransformPoint(new Vector3(.36f, .68f, .16f));
            var poleAtTable = transform.TransformPoint(new Vector3(.42f, .88f, .36f));
            float armWeight;
            Vector3 wristWorld, poleWorld;
            Vector3 wantFwd, wantPalm;
            var pinching = false;
            var allowLowDrop = false;

            var fanFwd = (transform.forward * .72f - transform.right * .65f + Vector3.up * .20f).normalized;
            var fanPalm = (-transform.right * .52f + Vector3.down * .82f - transform.forward * .22f).normalized;
            var dropFwd = (toSlotHoriz * .92f - Vector3.up * .18f).normalized;
            var dropPalm = (Vector3.down * .96f + toSlotHoriz * .18f).normalized;

            if (u < .25f)
            {
                // Фаза 1: спокойный плавный подъём правой руки к вееру левой руки.
                var k = Smooth01(u / .25f);
                armWeight = k;
                wristWorld = Vector3.Lerp(restWorld, fanTopWorld, k) + Vector3.up * (Mathf.Sin(k * Mathf.PI) * .045f);
                poleWorld = poleAtFan;
                wantFwd = fanFwd;
                wantPalm = fanPalm;
                pinching = u >= .14f;
            }
            else if (u < .66f)
            {
                // Фаза 2: вытягиваем карту строго вверх из веера, несём по высокой дуге НАД всеми картами на столе и мягко опускаем на слот!
                var k = Smooth01((u - .25f) / .41f);
                armWeight = 1f;
                wristWorld = Vector3.Lerp(fanTopWorld, reachDropWorld, k) + Vector3.up * (Mathf.Sin(k * Mathf.PI) * .125f);
                poleWorld = Vector3.Lerp(poleAtFan, poleAtTable, k);
                wantFwd = Vector3.Slerp(fanFwd, dropFwd, k).normalized;
                wantPalm = Vector3.Slerp(fanPalm, dropPalm, k).normalized;
                pinching = true;
                allowLowDrop = u >= .52f;
            }
            else if (u < .78f)
            {
                // Фаза 3: мягкое разжатие пальцев и чистый подъём кисти ВВЕРХ от положенной карты и бортика (0 задеваний карт и стола!).
                var k = Smooth01((u - .66f) / .12f);
                armWeight = 1f;
                wristWorld = Vector3.Lerp(reachDropWorld, liftEndWorld, k);
                poleWorld = poleAtTable;
                wantFwd = dropFwd;
                wantPalm = dropPalm;
                pinching = false;
            }
            else
            {
                // Фаза 4: возврат по дуге Безье СНАЧАЛА НАД бортиком к clearRimWorld (z=0.20), и только потом вниз на колени (0 прохождений сквозь стол!).
                var k = Smooth01((u - .78f) / .22f);
                var inv = 1f - k;
                armWeight = 1f - Smooth01((k - .45f) / .55f);
                wristWorld = inv * inv * liftEndWorld + 2f * inv * k * clearRimWorld + k * k * restWorld;
                poleWorld = Vector3.Lerp(poleAtTable, poleAtFan, k);
                wantFwd = Vector3.Slerp(dropFwd, (wristWorld - shoulder).normalized, k).normalized;
                wantPalm = (Vector3.down * .86f - transform.right * .42f).normalized;
                pinching = false;
            }

            var curl = pinching
                ? new[] { 22f, 24f, 14f, 32f, 34f, 16f, 40f, 40f, 18f, 46f, 44f, 20f }
                : new[] { 12f, 12f, 6f, 16f, 16f, 8f, 20f, 18f, 8f, 24f, 20f, 10f };
            ApplyRightArmPose(wristWorld, poleWorld, wantFwd, wantPalm, armWeight, curl, pinching ? ThumbPose.Pinch : ThumbPose.Relaxed, dt, Table3DLayout.TableY + .020f, allowLowDrop);

            // Карта мягко выезжает вверх из веера прямо в щипок пальцев (u=0.16..0.25), после чего ВСЕГДА держится
            // строго ВПЕРЕДИ кончиков пальцев вдоль handFwd (0 проколов запястья/рукава и 0 задеваний карт на столе!).
            if (_thrownCard != null && !_thrownReleased)
            {
                var pinchWorld = GetRightPinchWorld();
                var handFwd = (_rightHand != null && _rightMiddle1 != null)
                    ? (_rightMiddle1.position - _rightHand.position).normalized
                    : toSlotHoriz;
                if (u < .16f)
                {
                    _thrownCard.transform.position = _thrownStartPos;
                    _thrownCard.transform.rotation = _thrownStartRot;
                }
                else if (u < .66f)
                {
                    if (!_thrownPicked)
                    {
                        _thrownPicked = true;
                        _lastCardWorldPos = _thrownStartPos;
                    }
                    var gripK = Smooth01((u - .16f) / .11f);
                    var carryK = Smooth01((u - .27f) / .37f);
                    // Карта всегда строго ВПЕРЕДИ подушечек (+4.4 см по handFwd), чтобы её кромка никогда не заходила в запястье или рукав!
                    var carrySpot = pinchWorld + handFwd * .044f - Vector3.up * .008f;
                    if (carryK > .12f) carrySpot.y = Mathf.Max(carrySpot.y, Table3DLayout.TableY + .042f);
                    var cardPos = Vector3.Lerp(_thrownStartPos, carrySpot, gripK);
                    var cardRot = Quaternion.Slerp(_thrownStartRot, _thrownTargetRot, Smooth01((u - .27f) / .35f));
                    var scaleK = Smooth01((u - .34f) / .30f);
                    var cardScale = Mathf.Lerp(1.05f, _thrownTargetScale * .88f, scaleK);
                    var vel = dt > 1e-4f ? (cardPos - _lastCardWorldPos) / dt : Vector3.zero;
                    _lastCardWorldPos = cardPos;
                    _thrownCard.transform.SetPositionAndRotation(cardPos, cardRot);
                    _thrownCard.transform.localScale = Vector3.one * cardScale;
                    if (u >= .64f)
                    {
                        _thrownReleased = true;
                        _thrownCard.ReleaseToTable(Vector3.ClampMagnitude(vel, .42f));
                        _thrownCard = null;
                    }
                }
                else
                {
                    _thrownReleased = true;
                    _thrownCard.ReleaseToTable(Vector3.zero);
                    _thrownCard = null;
                }
            }
        }

        private void PoseRightTakeReach(float u, float dt)
        {
            var shoulder = _rightUpperArm.position;
            var len1 = Vector3.Distance(shoulder, _rightLowerArm.position);
            var len2 = Vector3.Distance(_rightLowerArm.position, _rightHand.position);
            if (len1 < .05f || len2 < .05f) return;

            var desiredGather = _reachWorldTarget + Vector3.up * .090f;
            var toGather = desiredGather - shoulder;
            var maxReach = (len1 + len2) * .92f;
            var gatherWorld = toGather.magnitude > maxReach ? shoulder + toGather.normalized * maxReach : desiredGather;
            gatherWorld.y = Mathf.Max(gatherWorld.y, Table3DLayout.TableY + .096f);

            var fanWorld = HandAnchor != null
                ? HandAnchor.position + transform.right * .07f + transform.forward * .04f
                : transform.TransformPoint(new Vector3(-.02f, .88f, .28f));
            var clearRimWorld = transform.TransformPoint(new Vector3(.14f, Table3DLayout.TableY + .155f, .20f));
            var restWorld = _rightHand.position;
            var poleAtFan = transform.TransformPoint(new Vector3(.36f, .68f, .16f));
            var poleAtTable = transform.TransformPoint(new Vector3(.42f, .86f, .32f));

            float armWeight;
            Vector3 wristWorld, poleWorld;
            if (u < .34f)
            {
                var k = Smooth01(u / .34f);
                var inv = 1f - k;
                armWeight = Smooth01(k / .55f);
                wristWorld = inv * inv * restWorld + 2f * inv * k * clearRimWorld + k * k * gatherWorld;
                poleWorld = Vector3.Lerp(poleAtFan, poleAtTable, k);
            }
            else if (u < .70f)
            {
                var k = Smooth01((u - .34f) / .36f);
                armWeight = 1f;
                wristWorld = Vector3.Lerp(gatherWorld, fanWorld, k) + Vector3.up * (Mathf.Sin(k * Mathf.PI) * .065f);
                poleWorld = Vector3.Lerp(poleAtTable, poleAtFan, k);
            }
            else
            {
                var k = Smooth01((u - .70f) / .30f);
                var inv = 1f - k;
                armWeight = 1f - Smooth01((k - .35f) / .65f);
                wristWorld = inv * inv * fanWorld + 2f * inv * k * clearRimWorld + k * k * restWorld;
                poleWorld = poleAtFan;
            }

            var wantFwd = (wristWorld - shoulder).normalized;
            var wantPalm = (Vector3.down * .86f - transform.right * .32f).normalized;
            ApplyRightArmPose(wristWorld, poleWorld, wantFwd, wantPalm, armWeight,
                new[] { 30f, 34f, 18f, 32f, 36f, 20f, 34f, 36f, 20f, 36f, 38f, 20f }, ThumbPose.Relaxed, dt, Table3DLayout.TableY + .018f, allowLowDrop: false);
        }

        private enum ThumbPose { Relaxed, Pinch, Up, Down, FistWrap }

        private void PoseRightGesture(float u, float dt)
        {
            // Плавный неторопливый подъём (24%), выразительная выдержка (52% ≈ 1.75 с) и плавное опускание (24%).
            var env = SmoothPulse(u, .00f, .24f, .76f, 1.00f);
            if (env <= .001f)
            {
                if (_hasSmoothRightArm) DecayRightArmToIdle(dt);
                return;
            }

            var holdU = Mathf.Clamp01((u - .22f) / .56f);
            var restWorld = _rightHand.position;
            Vector3 targetLocal, poleLocal;
            Vector3 wantFwd, wantPalm;
            float[] curl;
            ThumbPose thumb;
            var floorY = Table3DLayout.TableY + .016f;
            var followGaze = _gestureId != "TapTable";

            switch (_gestureId)
            {
                case "ThumbsUp":
                {
                    var bob = Mathf.Sin(holdU * Mathf.PI * 2f) * .012f * env;
                    // Глубже внутрь стола над красным сукном (z=0.58), чтобы жест чётко читался со всех сторон стола!
                    targetLocal = new Vector3(.20f, 1.05f + bob, .58f);
                    poleLocal = new Vector3(.38f, .76f, .34f);
                    wantFwd = (transform.forward * .62f - transform.right * .72f + transform.up * .24f).normalized;
                    wantPalm = Vector3.Cross(wantFwd, transform.up).normalized;
                    curl = new[] { 96f, 106f, 68f, 98f, 108f, 68f, 98f, 108f, 68f, 100f, 108f, 68f };
                    thumb = ThumbPose.Up;
                    break;
                }
                case "ThumbsDown":
                {
                    var bob = Mathf.Sin(holdU * Mathf.PI * 2f) * .012f * env;
                    targetLocal = new Vector3(.19f, 1.06f - bob, .58f);
                    poleLocal = new Vector3(.46f, .98f, .40f);
                    wantFwd = (transform.forward * .48f - transform.right * .82f - transform.up * .26f).normalized;
                    wantPalm = Vector3.Cross(wantFwd, -transform.up).normalized;
                    curl = new[] { 96f, 106f, 68f, 98f, 108f, 68f, 98f, 108f, 68f, 100f, 108f, 68f };
                    thumb = ThumbPose.Down;
                    break;
                }
                case "Fist":
                {
                    var pump = Mathf.Sin(holdU * Mathf.PI * 2.5f) * .020f * env;
                    targetLocal = new Vector3(.20f, 1.05f + pump, .56f);
                    poleLocal = new Vector3(.36f, .72f, .36f);
                    wantFwd = (transform.up * .82f + transform.forward * .56f).normalized;
                    wantPalm = (-transform.forward * .82f + transform.up * .56f).normalized;
                    curl = new[] { 88f, 98f, 60f, 90f, 100f, 62f, 90f, 100f, 62f, 92f, 100f, 62f };
                    thumb = ThumbPose.FistWrap;
                    break;
                }
                case "Point":
                {
                    // Указательный палец и вся кисть вынесены глубоко над столом и живьём следуют за взглядом!
                    targetLocal = new Vector3(.18f, 1.04f, .64f);
                    poleLocal = new Vector3(.38f, .80f, .36f);
                    wantFwd = (transform.forward + Vector3.up * .06f).normalized;
                    wantPalm = Vector3.ProjectOnPlane(-transform.right * .78f + Vector3.down * .42f, wantFwd).normalized;
                    curl = new[] { 2f, 2f, 0f, 86f, 96f, 56f, 88f, 98f, 58f, 90f, 98f, 58f };
                    thumb = ThumbPose.FistWrap;
                    break;
                }
                case "TapTable":
                {
                    // 3 спокойных мягких постукивания строго по открытому красному сукну дальше бортика и левее стакана!
                    var tapPhase = Mathf.Clamp01((u - .22f) / .56f);
                    var tapWave = .5f - .5f * Mathf.Cos(tapPhase * Mathf.PI * 6f);
                    var tapLift = (1f - tapWave) * .036f;
                    var tableLocalY = transform.InverseTransformPoint(new Vector3(0f, Table3DLayout.TableY + .094f, 0f)).y;
                    targetLocal = new Vector3(.04f, tableLocalY + tapLift, .61f);
                    poleLocal = new Vector3(.38f, .82f, .34f);
                    wantFwd = (transform.forward * .96f - Vector3.up * .20f).normalized;
                    wantPalm = (Vector3.down * .96f - transform.forward * .20f).normalized;
                    curl = new[] { 28f, 34f, 18f, 30f, 36f, 18f, 34f, 38f, 20f, 38f, 40f, 20f };
                    thumb = ThumbPose.Relaxed;
                    floorY = Table3DLayout.TableY + .008f;
                    break;
                }
                case "Shrug":
                {
                    targetLocal = new Vector3(.35f, .99f, .48f);
                    poleLocal = new Vector3(.44f, .72f, .24f);
                    wantFwd = (transform.forward * .64f + transform.right * .74f + Vector3.up * .12f).normalized;
                    wantPalm = (Vector3.up * .94f + transform.forward * .30f).normalized;
                    curl = new[] { 14f, 16f, 8f, 18f, 18f, 8f, 22f, 20f, 10f, 26f, 22f, 10f };
                    thumb = ThumbPose.Relaxed;
                    break;
                }
                case "Disbelief":
                {
                    var sway = Mathf.Sin(holdU * Mathf.PI * 2f) * .026f * env;
                    targetLocal = new Vector3(.32f + sway, 1.02f, .52f);
                    poleLocal = new Vector3(.44f, .74f, .28f);
                    wantFwd = (transform.forward * .74f + transform.right * .62f + Vector3.up * .20f).normalized;
                    wantPalm = (Vector3.up * .82f - transform.right * .42f + transform.forward * .35f).normalized;
                    curl = new[] { 16f, 18f, 8f, 20f, 20f, 10f, 24f, 22f, 10f, 28f, 24f, 12f };
                    thumb = ThumbPose.Relaxed;
                    break;
                }
                case "MiddleFinger":
                {
                    var bob = Mathf.Sin(holdU * Mathf.PI * 2f) * .012f * env;
                    // Вынесен глубоко вперёд над красным сукном тыльной стороной к адресату — чётко виден с любого места за столом!
                    targetLocal = new Vector3(.20f, 1.06f + bob, .57f);
                    poleLocal = new Vector3(.32f, .70f, .42f);
                    wantFwd = (transform.up * .96f + transform.forward * .18f).normalized;
                    wantPalm = (-transform.forward * .96f + transform.up * .18f).normalized;
                    curl = new[] { 90f, 100f, 62f, 2f, 3f, 0f, 92f, 102f, 64f, 94f, 102f, 64f };
                    thumb = ThumbPose.FistWrap;
                    break;
                }
                default:
                    return;
            }

            // Жест следует за живым взглядом персонажа (_curYaw/_curPitch — как от LookAtWorld ботов, так и от камеры игрока в мультиплеере!).
            if (followGaze)
            {
                var armGazeYaw = Mathf.Clamp(_curYaw, -40f, 46f);
                var armGazePitch = Mathf.Clamp(_curPitch * .38f, -10f, 10f);
                var chestPivot = new Vector3(0f, .95f, .04f);
                var gazeLocalQ = Quaternion.Euler(armGazePitch, armGazeYaw, 0f);
                targetLocal = chestPivot + gazeLocalQ * (targetLocal - chestPivot);
                // Поднятый жест находится заметно выше и дальше левого веера, поэтому спокойно разворачивается и влево до -12 см!
                if (targetLocal.x < -.12f) targetLocal.x = -.12f;
                poleLocal = chestPivot + Quaternion.Euler(0f, armGazeYaw * .70f, 0f) * (poleLocal - chestPivot);
                var gazeWorldQ = Quaternion.AngleAxis(armGazeYaw, transform.up) * Quaternion.AngleAxis(armGazePitch * .55f, transform.right);
                wantFwd = (gazeWorldQ * wantFwd).normalized;
                wantPalm = (gazeWorldQ * wantPalm).normalized;
            }

            // Плавная дуга через точку над бортиком при подъёме/опускании жестов (особенно TapTable), чтобы рука никогда не резала борт!
            var goalWorld = transform.TransformPoint(targetLocal);
            var clearWorld = transform.TransformPoint(new Vector3(Mathf.Clamp(targetLocal.x, .10f, .34f), Table3DLayout.TableY + .16f, .20f));
            var invE = 1f - env;
            var targetWorld = targetLocal.z > .30f
                ? (invE * invE * restWorld + 2f * invE * env * clearWorld + env * env * goalWorld)
                : Vector3.Lerp(restWorld, goalWorld, env);
            ApplyRightArmPose(targetWorld, transform.TransformPoint(poleLocal), wantFwd, wantPalm, env, curl, thumb, dt, floorY, allowLowDrop: _gestureId == "TapTable" && env > .85f);
        }

        /// <summary>
        /// Не даёт правой кисти и предплечью проходить сквозь золотой бортик, стол или стакан виски при любых действиях.
        /// </summary>
        private void ApplyTableAndGlassClearance(ref Vector3 wristWorld, ref Vector3 poleWorld, bool allowLowDrop)
        {
            var lp = transform.InverseTransformPoint(wristWorld);
            if (lp.z > .28f)
            {
                // Зона поднятого золотого бортика (z = 0.28..0.49 при SeatGap=0.37): держим запястье гарантированно выше борта.
                var minY = lp.z < .49f
                    ? Table3DLayout.TableY + .115f
                    : Table3DLayout.TableY + (allowLowDrop ? .074f : .096f);
                if (wristWorld.y < minY) wristWorld.y = minY;
                var minPoleY = Table3DLayout.TableY + .095f;
                if (poleWorld.y < minPoleY && lp.z > .41f) poleWorld.y = Mathf.Lerp(poleWorld.y, minPoleY, .55f);
            }

            if (Glass != null)
            {
                var gPos = Glass.transform.position;
                var horiz = Vector3.ProjectOnPlane(wristWorld - gPos, Vector3.up);
                var dist = horiz.magnitude;
                const float glassClearR = .115f;
                if (dist < glassClearR)
                {
                    var k = Smooth01(1f - dist / glassClearR);
                    var overGlassY = Table3DLayout.TableY + .160f;
                    if (wristWorld.y < overGlassY)
                        wristWorld.y = Mathf.Lerp(wristWorld.y, overGlassY, k);
                    // Мягкий обход стакана чуть левее к центру, чтобы рука не задевала ободок стакана сбоку.
                    var pushDir = dist > 1e-4f ? horiz.normalized : -transform.right;
                    wristWorld += pushDir * ((glassClearR - dist) * .45f * k);
                }
            }
        }

        /// <summary>
        /// Решает IK правой руки с естественным распределением скрутки по предплечью (без «сломанного» запястья),
        /// автоматической защитой от стола, бортика, стакана и плавным инерционным сглаживанием между кадрами.
        /// </summary>
        private void ApplyRightArmPose(Vector3 targetWorld, Vector3 poleWorld, Vector3 wantFwd, Vector3 wantPalm, float weight, float[] curlDeg, ThumbPose thumbPose, float dt, float minTipFloorY, bool allowLowDrop = false)
        {
            if (weight <= .001f) return;
            ApplyTableAndGlassClearance(ref targetWorld, ref poleWorld, allowLowDrop);

            var idleUpper = _rightUpperArm.localRotation;
            var idleLower = _rightLowerArm.localRotation;
            var idleHand = _rightHand.localRotation;
            if (!_hasSmoothRightArm)
            {
                _smoothRightUpper = idleUpper;
                _smoothRightLower = idleLower;
                _smoothRightHand = idleHand;
                if (_allRightFingerBones != null && _smoothRightFingers != null)
                {
                    for (var i = 0; i < _allRightFingerBones.Length; i++)
                        _smoothRightFingers[i] = _allRightFingerBones[i] != null ? _allRightFingerBones[i].localRotation : Quaternion.identity;
                }
                _hasSmoothRightArm = true;
            }
            _smoothRightWeight = Mathf.Max(_smoothRightWeight, weight);

            SolveRightArmIK(targetWorld, poleWorld, wantFwd, wantPalm, weight, idleUpper, idleLower, idleHand);
            PoseRightFingers(weight, curlDeg, thumbPose);

            // Гарантированный зазор над столом: если хоть одна подушечка пальца ушла ниже сукна, мягко доподнимаем запястье!
            if (minTipFloorY > 0f && _rightFingerTipCheck != null)
            {
                var lowestY = float.MaxValue;
                for (var i = 0; i < _rightFingerTipCheck.Length; i++)
                {
                    var tip = _rightFingerTipCheck[i];
                    if (tip != null && tip.position.y < lowestY) lowestY = tip.position.y;
                }
                if (lowestY < minTipFloorY)
                {
                    var lift = Mathf.Min(minTipFloorY - lowestY, .075f);
                    SolveRightArmIK(targetWorld + Vector3.up * lift, poleWorld + Vector3.up * (lift * .6f), wantFwd, wantPalm, weight, idleUpper, idleLower, idleHand);
                    PoseRightFingers(weight, curlDeg, thumbPose);
                }
            }

            // Инерционное сглаживание по всем суставам правой руки: плавные человеческие переходы без рывков!
            var rotSmooth = dt > 1e-4f ? 1f - Mathf.Exp(-dt * 11.5f) : 1f;
            _smoothRightUpper = Quaternion.Slerp(_smoothRightUpper, _rightUpperArm.localRotation, rotSmooth);
            _smoothRightLower = Quaternion.Slerp(_smoothRightLower, _rightLowerArm.localRotation, rotSmooth);
            _smoothRightHand = Quaternion.Slerp(_smoothRightHand, _rightHand.localRotation, rotSmooth);
            _rightUpperArm.localRotation = _smoothRightUpper;
            _rightLowerArm.localRotation = _smoothRightLower;
            _rightHand.localRotation = _smoothRightHand;

            if (_allRightFingerBones != null && _smoothRightFingers != null)
            {
                var fingerSmooth = dt > 1e-4f ? 1f - Mathf.Exp(-dt * 13.5f) : 1f;
                for (var i = 0; i < _allRightFingerBones.Length; i++)
                {
                    var b = _allRightFingerBones[i];
                    if (b == null) continue;
                    _smoothRightFingers[i] = Quaternion.Slerp(_smoothRightFingers[i], b.localRotation, fingerSmooth);
                    b.localRotation = _smoothRightFingers[i];
                }
            }
        }

        private void SolveRightArmIK(Vector3 targetWorld, Vector3 poleWorld, Vector3 wantFwd, Vector3 wantPalm, float weight, Quaternion idleUpper, Quaternion idleLower, Quaternion idleHand)
        {
            // Стартуем от стабильного нейтрального кадра 0, чтобы скрутка плеча и предплечья никогда не плавала и не закручивалась в штопор!
            _rightUpperArm.localRotation = _rightUpperNeutral;
            _rightLowerArm.localRotation = _rightLowerNeutral;
            _rightHand.localRotation = _rightHandNeutral;

            var shoulder = _rightUpperArm.position;
            var elbow = _rightLowerArm.position;
            var wrist = _rightHand.position;
            var len1 = Vector3.Distance(shoulder, elbow);
            var len2 = Vector3.Distance(elbow, wrist);
            if (len1 < .05f || len2 < .05f) return;

            var toTarget = targetWorld - shoulder;
            var dist = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(len1 - len2) + .01f, len1 + len2 - .006f);
            var dir = toTarget.normalized;
            var cosA = Mathf.Clamp((len1 * len1 + dist * dist - len2 * len2) / (2f * len1 * dist), -1f, 1f);
            var sinA = Mathf.Sqrt(Mathf.Max(0f, 1f - cosA * cosA));
            var bendAxis = Vector3.ProjectOnPlane(poleWorld - shoulder, dir);
            bendAxis = bendAxis.sqrMagnitude > 1e-5f ? bendAxis.normalized : -transform.up;

            var desiredElbow = shoulder + dir * (len1 * cosA) + bendAxis * (len1 * sinA);

            // 1. Наводим плечо и строго выравниваем шарнир локтя по плоскости IK (0 выворотов локтя вбок!).
            var curUpper = (elbow - shoulder).normalized;
            var newUpper = (desiredElbow - shoulder).normalized;
            _rightUpperArm.rotation = Quaternion.FromToRotation(curUpper, newUpper) * _rightUpperArm.rotation;

            var curBend = Vector3.ProjectOnPlane(_rightUpperArm.TransformDirection(_rightElbowBendLocal), newUpper);
            var wantBend = Vector3.ProjectOnPlane(targetWorld - desiredElbow, newUpper);
            if (wantBend.sqrMagnitude < 1e-4f) wantBend = Vector3.ProjectOnPlane(-bendAxis, newUpper);
            if (curBend.sqrMagnitude > 1e-4f && wantBend.sqrMagnitude > 1e-4f)
            {
                var hingeTwist = Vector3.SignedAngle(curBend.normalized, wantBend.normalized, newUpper);
                _rightUpperArm.rotation = Quaternion.AngleAxis(hingeTwist, newUpper) * _rightUpperArm.rotation;
            }

            // 2. Наводим предплечье к целевому запястью.
            var curLower = (_rightHand.position - _rightLowerArm.position).normalized;
            var newLower = (targetWorld - _rightLowerArm.position).normalized;
            _rightLowerArm.rotation = Quaternion.FromToRotation(curLower, newLower) * _rightLowerArm.rotation;

            // 3. Распределяем осевой поворот ладони (пронацию/супинацию) на 78% по предплечью — запястье не скручивается в «фантик»!
            if (_rightMiddle1 != null && _rightThumb1 != null && _rightLittle1 != null)
            {
                var cleanFwd = wantFwd.normalized;
                var handPos = _rightHand.position;
                var curFwd = (_rightMiddle1.position - handPos).normalized;
                var curAcross = (_rightThumb1.position - _rightLittle1.position).normalized;
                var curPalm = Vector3.Cross(curFwd, curAcross).normalized;
                var cleanPalm = Vector3.ProjectOnPlane(wantPalm, cleanFwd);
                if (cleanPalm.sqrMagnitude < 1e-4f) cleanPalm = curPalm;
                cleanPalm.Normalize();

                var swing = Quaternion.FromToRotation(curFwd, cleanFwd);
                var swungPalm = (swing * curPalm).normalized;
                var rollDeg = Vector3.SignedAngle(swungPalm, cleanPalm, cleanFwd);
                var forearmRoll = Mathf.Clamp(rollDeg * .78f, -125f, 125f);
                _rightLowerArm.rotation = Quaternion.AngleAxis(forearmRoll, newLower) * _rightLowerArm.rotation;

                // 4. Финально доводим саму кисть (остаётся лёгкий естественный дожим ~22% без залома манжеты).
                handPos = _rightHand.position;
                curFwd = (_rightMiddle1.position - handPos).normalized;
                curAcross = (_rightThumb1.position - _rightLittle1.position).normalized;
                curPalm = Vector3.Cross(curFwd, curAcross).normalized;
                var curRot = Quaternion.LookRotation(curFwd, curPalm);
                var wantRot = Quaternion.LookRotation(cleanFwd, cleanPalm);
                _rightHand.rotation = wantRot * Quaternion.Inverse(curRot) * _rightHand.rotation;
            }

            // 5. Плавно смешиваем результат с живой позой SitIdle по весу weight.
            _rightUpperArm.localRotation = Quaternion.Slerp(idleUpper, _rightUpperArm.localRotation, weight);
            _rightLowerArm.localRotation = Quaternion.Slerp(idleLower, _rightLowerArm.localRotation, weight);
            _rightHand.localRotation = Quaternion.Slerp(idleHand, _rightHand.localRotation, weight);
        }

        private void PoseRightFingers(float weight, float[] curlDeg, ThumbPose thumbPose)
        {
            if (_allRightFingerBones == null || _allRightFingerBones.Length < 15 || weight <= .001f) return;
            for (var i = 0; i < _allRightFingerBones.Length; i++)
            {
                if (_allRightFingerBones[i] != null)
                    _allRightFingerBones[i].localRotation = Quaternion.Slerp(_allRightFingerBones[i].localRotation, _allRightFingerRestLocal[i], weight);
            }

            if (_rightMiddle1 == null || _rightThumb1 == null || _rightLittle1 == null) return;
            var handPos = _rightHand.position;
            var fwd = (_rightMiddle1.position - handPos).normalized;
            var across = (_rightThumb1.position - _rightLittle1.position).normalized;
            var palm = Vector3.Cross(fwd, across).normalized;
            var thumbSide = Vector3.ProjectOnPlane(across, fwd);
            thumbSide = Vector3.ProjectOnPlane(thumbSide, palm).normalized;
            var curlAxis = Vector3.Cross(fwd, palm).normalized;

            for (var i = 0; i < 12 && i < curlDeg.Length; i++)
            {
                var bone = _allRightFingerBones[3 + i];
                if (bone != null && curlDeg[i] > .1f)
                    bone.rotation = Quaternion.AngleAxis(curlDeg[i] * weight, curlAxis) * bone.rotation;
            }

            if (_rightThumb1 != null && _rightThumb2 != null)
            {
                Vector3 wantT1, wantT2;
                switch (thumbPose)
                {
                    case ThumbPose.Up:
                    case ThumbPose.Down:
                        // Естественный анатомический угол пястной кости (~45° вместо вытянутых 90°), без растяжения перепонки у запястья!
                        wantT1 = (thumbSide * .74f + fwd * .62f + palm * .14f).normalized;
                        wantT2 = (thumbSide * .92f + fwd * .36f + palm * .04f).normalized;
                        break;
                    case ThumbPose.Pinch:
                        wantT1 = (fwd * .62f + palm * .72f + thumbSide * .18f).normalized;
                        wantT2 = (fwd * .72f + palm * .45f - thumbSide * .52f).normalized;
                        break;
                    case ThumbPose.FistWrap:
                        wantT1 = (fwd * .82f + palm * .48f + thumbSide * .24f).normalized;
                        wantT2 = (fwd * .56f + palm * .34f - thumbSide * .75f).normalized;
                        break;
                    default:
                        wantT1 = (fwd * .62f + palm * .42f + thumbSide * .65f).normalized;
                        wantT2 = (fwd * .70f + palm * .32f + thumbSide * .55f).normalized;
                        break;
                }

                var curT1 = (_rightThumb2.position - _rightThumb1.position).normalized;
                _rightThumb1.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(curT1, wantT1), weight) * _rightThumb1.rotation;
                var rt3 = _allRightFingerBones[2];
                if (rt3 != null)
                {
                    var curT2 = (rt3.position - _rightThumb2.position).normalized;
                    _rightThumb2.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(curT2, wantT2), weight * .92f) * _rightThumb2.rotation;
                    if (thumbPose == ThumbPose.FistWrap)
                    {
                        var wantT3 = (fwd * .26f - palm * .48f - thumbSide * .84f).normalized;
                        rt3.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(wantT2, wantT3), weight * .85f) * rt3.rotation;
                    }
                    else if (thumbPose == ThumbPose.Up || thumbPose == ThumbPose.Down)
                    {
                        var wantT3 = (thumbSide * .97f + fwd * .12f - palm * .06f).normalized;
                        rt3.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(wantT2, wantT3), weight * .75f) * rt3.rotation;
                    }
                }
            }
        }

        private Vector3 GetRightPinchWorld()
        {
            if (_allRightFingerBones != null && _allRightFingerBones.Length >= 6)
            {
                var rt3 = _allRightFingerBones[2];
                var ri3 = _allRightFingerBones[5];
                if (rt3 != null && ri3 != null)
                    return Vector3.Lerp(ri3.position, rt3.position, .45f) - transform.up * .008f;
            }
            return _rightHand != null ? _rightHand.position + _rightHand.forward * .06f : transform.position + Vector3.up * .9f;
        }

        // Пятая степень (Perlin smootherstep): нулевая скорость и нулевое ускорение на краях — никаких резких рывков!
        private static float Smooth01(float x)
        {
            var t = Mathf.Clamp01(x);
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        private static float SmoothPulse(float u, float riseStart, float riseEnd, float fallStart, float fallEnd)
        {
            if (u <= riseStart || u >= fallEnd) return 0f;
            if (u < riseEnd) return Smooth01((u - riseStart) / Mathf.Max(1e-4f, riseEnd - riseStart));
            if (u > fallStart) return 1f - Smooth01((u - fallStart) / Mathf.Max(1e-4f, fallEnd - fallStart));
            return 1f;
        }

        private void PickNextGlance(float now, bool immediate)
        {
            var hold = Mathf.Lerp(_persona.GlanceHoldMin, _persona.GlanceHoldMax, Next01());
            _nextGlanceAt = now + hold;
            var mode = Next01();
            if (mode < .42f)
            {
                _targetYaw = (Next01() * 2f - 1f) * 3.5f;
                _targetPitch = (Next01() * 2f - 1f) * 2.2f;
                _targetRoll = (Next01() * 2f - 1f) * 1.2f;
            }
            else
            {
                _targetYaw = (Next01() * 2f - 1f) * _persona.GlanceSpan;
                _targetPitch = (Next01() * 2f - 1f) * 4.2f;
                _targetRoll = (Next01() * 2f - 1f) * _persona.GlanceTilt;
            }
            if (immediate)
            {
                _curYaw = _targetYaw;
                _curPitch = _targetPitch;
                _curRoll = _targetRoll;
            }
        }

        private void PickNextShift(float now, bool immediate)
        {
            var hold = Mathf.Lerp(_persona.ShiftHoldMin, _persona.ShiftHoldMax, Next01());
            _nextShiftAt = now + hold;
            _targetShiftPitch = (Next01() * 2f - 1f) * _persona.ShiftSpan;
            _targetShiftYaw = (Next01() * 2f - 1f) * _persona.ShiftSpan * 1.15f;
            _targetShiftRoll = (Next01() * 2f - 1f) * _persona.ShiftSpan * .65f;
            if (immediate)
            {
                _curShiftPitch = _targetShiftPitch;
                _curShiftYaw = _targetShiftYaw;
                _curShiftRoll = _targetShiftRoll;
            }
        }

        private float Next01()
        {
            var x = _rngState;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _rngState = x;
            return (x & 0x00FFFFFFu) / 16777215f;
        }

        private void BuildFallbackMannequin(int look)
        {
            var sweater = Table3DArt.Sweater;
            var skin = Table3DArt.Lit("skin " + look, FallbackSkin[look], .25f);
            var hair = Table3DArt.Lit("hair " + look, FallbackHair[look], .15f);
            var root = transform;
            Table3DArt.Ball(root, new Vector3(0f, .9f, 0f), new Vector3(.4f, .56f, .26f), sweater);
            Table3DArt.Limb(root, new Vector3(0f, 1.08f, 0f), new Vector3(0f, 1.16f, 0f), .05f, skin);
            Table3DArt.Ball(root, new Vector3(0f, 1.24f, 0f), new Vector3(.19f, .236f, .21f), skin);
            Table3DArt.Ball(root, new Vector3(0f, 1.27f, -.018f), new Vector3(.2f, .21f, .208f), hair);
            var hands = new Vector3(0f, .9f, .3f);
            for (var side = -1; side <= 1; side += 2)
            {
                var shoulder = new Vector3(.2f * side, 1.03f, 0f);
                var elbow = new Vector3(.25f * side, .84f, .1f);
                var wrist = hands + new Vector3(.06f * side, 0f, -.03f);
                Table3DArt.Limb(root, shoulder, elbow, .05f, sweater);
                Table3DArt.Limb(root, elbow, wrist, .042f, sweater);
                Table3DArt.Ball(root, wrist + new Vector3(0f, 0f, .03f), new Vector3(.084f, .1f, .084f), skin);
            }
        }

        private static Transform CreateSocket(Transform parent, string name, Vector3 localOffset)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localOffset;
            return go.transform;
        }

        private static Transform FindDeep(Transform root, string exactName)
        {
            if (root.name == exactName) return root;
            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindDeep(root.GetChild(i), exactName);
                if (found != null) return found;
            }
            return null;
        }
    }
}
