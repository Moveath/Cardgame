using System;
using System.Collections.Generic;
using CardGame.Core.Cards;
using UnityEngine;

namespace CardGameClient.ThreeD
{
    /// <summary>
    /// Карта в мире: объёмная, с лицом, рубашкой и ребром. Перелетает плавно, на стол падает физикой, после падения встаёт точно на своё место.
    /// Куда карте надо, решает только презентер по DurakGame: физика показывает дорогу и ничего не меняет в партии.
    /// </summary>
    public sealed class Card3DView : MonoBehaviour
    {
        private const float FlightTime = .50f, FlightArc = .075f, ReducedFlightTime = .1f;
        private const float DropHeight = .065f;                 // Карта отпускается над своим местом и мягко ложится на сукно.
        private const float DropSpeed = .32f;
        private const float SettleTime = .19f, SettleLimit = 1.2f, MinFall = .2f, RestSpeed = .04f;
        private const float BodyDepth = .002f;                  // Коллайдер толще самой карты: тонкий проскакивает стол.
        private const float CardMass = .005f, AirDrag = 2f, AirSpin = 3f, Contact = .0008f;
        // Слабая подсветка картинки: значения читаются в тени руки, но карта остаётся картоном под лампой, а не экраном.
        // На центре стола прямо под лампой картон слегка приглушён (TableFaceTint), чтобы белый фон не выгорал и масти были контрастнее.
        private static readonly Color Glow = new Color(.1f, .1f, .1f);
        private static readonly Color TableFaceTint = new Color(.84f, .82f, .78f);
        private static readonly Color Dimmed = new Color(.66f, .63f, .6f);
        private static readonly Dictionary<Texture, Material> Materials = new Dictionary<Texture, Material>();
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private static Material _sideMaterial, _opponentSideMaterial, _highlightMaterial;
        private static PhysicsMaterial _cloth;
        private static MaterialPropertyBlock _tint;

        private enum Motion { Still, Flying, Held, Falling }

        private struct Target
        {
            public Transform Parent;
            public Vector3 Position;
            public Quaternion Rotation;
            public float Scale;
            public bool Land;
            public Action Arrived;
        }

        private MeshRenderer _renderer;
        private GameObject _highlight;
        private Rigidbody _body;
        private Motion _motion;
        private Target _target;
        private bool _hasTarget;
        private Vector3 _fromPosition, _toPosition, _fromLocalVel, _toLocalVel, _fromScale, _toScale;
        private Quaternion _fromRotation, _toRotation;
        private Transform _flyTargetParent;
        private float _time, _duration, _arc, _fallTime;
        private bool _useHermite, _clampFloorY, _flyWorldSpace;
        private Action _arrived;

        /// <summary>Сколько раз карты падали на стол физикой: для проверки.</summary>
        public static int Landings { get; private set; }

        public Card Card { get; private set; }

        /// <summary>Карта в движении: летит, в руке или падает. Нажатия по ней не принимаются.</summary>
        public bool IsFlying => _motion != Motion.Still;

        public bool IsHeld => _motion == Motion.Held;

        /// <summary>physical — карта игры: у неё есть тело для падения и коллайдер для нажатий. Рубашки в руках соперников и верх колоды без него.</summary>
        public static Card3DView Create(Transform parent, Card card, bool faceUp, bool physical)
        {
            var go = new GameObject("Card3D " + card);
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<Card3DView>();
            view.Card = card;
            go.AddComponent<MeshFilter>().sharedMesh = CardMesh3D.Card;
            view._renderer = go.AddComponent<MeshRenderer>();
            // У карт в руках соперников (не physical) ставим тёплый матовый обрез и убираем самотень между картами в 1.6 мм,
            // чтобы верхняя кромка веера на дистанции 2 м не рябила и не давала «белых пикселей»!
            var sideMat = physical ? SideMaterial : OpponentSideMaterial;
            view._renderer.sharedMaterials = new[] { MaterialFor(LoadTexture(faceUp ? FaceKey(card) : "back")), MaterialFor(LoadTexture("back")), sideMat };
            if (!physical) view._renderer.receiveShadows = false;

            view._highlight = new GameObject("Selected edge", typeof(MeshFilter), typeof(MeshRenderer));
            view._highlight.transform.SetParent(go.transform, false);
            view._highlight.GetComponent<MeshFilter>().sharedMesh = CardMesh3D.Highlight;
            var edge = view._highlight.GetComponent<MeshRenderer>();
            edge.sharedMaterial = HighlightMaterial;
            edge.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            view._highlight.SetActive(false);

            if (physical)
            {
                var box = go.AddComponent<BoxCollider>();
                box.size = new Vector3(CardMesh3D.Width, CardMesh3D.Height, BodyDepth);
                box.contactOffset = Contact;
                box.sharedMaterial = Cloth;
                view._body = go.AddComponent<Rigidbody>();
                view._body.isKinematic = true;
                view._body.mass = CardMass;
                view._body.linearDamping = AirDrag;
                view._body.angularDamping = AirSpin;
                view._body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            }
            return view;
        }

        private bool _dimmed, _onTable;

        public void SetSelected(bool selected) => _highlight.SetActive(selected);

        /// <summary>Карта лежит лицом вверх прямо под яркой лампой стола: убираем лишнее свечение и засвет белого фона.</summary>
        public void SetOnTable(bool onTable)
        {
            if (_onTable == onTable) return;
            _onTable = onTable;
            ApplyFaceTint();
        }

        /// <summary>Карта, которой сейчас нельзя ходить. Прозрачность не годится: в веере проступили бы карты под ней.</summary>
        public void SetDimmed(bool dimmed)
        {
            _dimmed = dimmed;
            ApplyFaceTint();
        }

        private void ApplyFaceTint()
        {
            _tint = _tint ?? new MaterialPropertyBlock();
            var baseColor = _dimmed ? Dimmed : (_onTable ? TableFaceTint : Color.white);
            var emission = _onTable ? Color.black : (_dimmed ? Glow * Dimmed : Glow);
            _tint.SetColor(BaseColor, baseColor);
            _tint.SetColor(EmissionColor, emission);
            _renderer.SetPropertyBlock(_tint, 0);
        }

        /// <summary>Сразу на место, без полёта.</summary>
        public void Place(Transform parent, Vector3 position, Quaternion rotation, float scale)
        {
            StopBody();
            transform.SetParent(parent, false);
            transform.localPosition = position;
            transform.localRotation = rotation;
            transform.localScale = Vector3.one * scale;
            _motion = Motion.Still;
            _target = new Target { Parent = parent, Position = position, Rotation = rotation, Scale = scale };
            _hasTarget = true;
        }

        /// <summary>Перелёт на новое место; смена родителя сохраняет положение в мире, поэтому карта вылетает оттуда, где была.</summary>
        public void MoveTo(Transform parent, Vector3 position, Quaternion rotation, float scale, Action arrived = null) =>
            Request(new Target { Parent = parent, Position = position, Rotation = rotation, Scale = scale, Arrived = arrived });

        /// <summary>На стол: карта прилетает чуть выше своего места и падает на сукно, потом встаёт ровно на место.</summary>
        public void Land(Transform parent, Vector3 position, Quaternion rotation, float scale) =>
            Request(new Target { Parent = parent, Position = position, Rotation = rotation, Scale = scale, Land = true });

        /// <summary>Уход в биту: коллайдер выключается, карта чистой дугой перелетает сверху на стопку и ложится рубашкой вверх без протаскивания сквозь нижние карты.</summary>
        public void SendToDiscard(Transform parent, Vector3 position, Quaternion rotation, float scale, float arc = .085f) =>
            GlideTo(parent, position, rotation, scale, FlightTime, arc, null, disableCollider: true);

        /// <summary>Управляемый перелёт с заданной длительностью и высотой дуги (сбор кучки на столе, уход в биту/руки, раздача с колоды).</summary>
        public void GlideTo(Transform parent, Vector3 position, Quaternion rotation, float scale, float duration, float arc = 0f, Action arrived = null, bool disableCollider = false)
        {
            StopBody();
            if (disableCollider)
            {
                var col = GetComponent<Collider>();
                if (col != null) col.enabled = false;
            }
            _target = new Target { Parent = parent, Position = position, Rotation = rotation, Scale = scale, Land = false, Arrived = arrived };
            _hasTarget = true;
            Fly(parent, position, rotation, scale, arc, Duration(duration), arrived);
        }

        /// <summary>
        /// Плавно сдвигает целевой слот уже летящей в веер карты без сброса её текущей дуги и времени полёта:
        /// когда с колоды или со стола прилетают карты одна за другой, веер раскрывается мягко без дёргания.
        /// </summary>
        public bool TryRetargetFanSlot(Transform fanRoot, Vector3 position, Quaternion rotation, float scale)
        {
            if (_motion != Motion.Flying) return false;
            if (transform.parent != fanRoot && (!_flyWorldSpace || _flyTargetParent != fanRoot)) return false;
            _toPosition = position;
            _toRotation = rotation;
            _toScale = Vector3.one * scale;
            _target = new Target { Parent = fanRoot, Position = position, Rotation = rotation, Scale = scale, Land = false, Arrived = _target.Arrived };
            _hasTarget = true;
            return true;
        }

        /// <summary>Рука забирает карту: полёты и падение замирают, новые места запоминаются до LetGo.</summary>
        public void Hold()
        {
            StopBody();
            _motion = Motion.Held;
            _arrived = null;
        }

        public void Grip(Transform anchor) => transform.SetParent(anchor, true);

        /// <summary>Рука отпустила карту: она идёт к последнему месту, которое ей назначили.</summary>
        public void LetGo()
        {
            _motion = Motion.Still;
            if (_hasTarget) GoToTarget();
        }

        /// <summary>Рука донесла карту к сукну и мягко отпускает её на ходу: карта плавно и строго сверху опускается на слот (никогда не ныряя сквозь нижнюю карту).</summary>
        public void ReleaseToTable(Vector3 releaseWorldVelocity = default)
        {
            _motion = Motion.Still;
            if (!_hasTarget) return;
            var target = _target;
            var worldGoal = target.Parent != null ? target.Parent.TransformPoint(target.Position) : target.Position;
            var dist = Vector3.Distance(transform.position, worldGoal);
            var dur = Duration(Mathf.Clamp(.20f + dist * .34f, .21f, .32f));
            var localStart = target.Parent != null ? target.Parent.InverseTransformPoint(transform.position) : transform.position;
            var dy = Mathf.Max(0f, localStart.y - target.Position.y);
            var localVel = target.Parent != null ? target.Parent.InverseTransformVector(releaseWorldVelocity) : releaseWorldVelocity;
            // Ограничение вертикальной скорости гарантирует строго монотонный спуск без нырка ниже _toPosition.y!
            localVel.y = Mathf.Clamp(localVel.y, -(dy / dur) * 1.35f, .06f);
            FlyHermite(target.Parent, target.Position, target.Rotation, target.Scale, localVel, Vector3.zero, dur, target.Arrived, 0f, true);
        }

        private void Request(Target target)
        {
            if (_hasTarget && Same(_target, target) && target.Arrived == null) return; // Тот же приказ приходит при каждом обновлении стола.
            // Уже лежит на столе, а место сдвинулось: карта скользит по сукну без нового падения.
            var slide = target.Land && _hasTarget && _target.Land && _target.Parent == target.Parent && _motion == Motion.Still;
            _target = target;
            _hasTarget = true;
            if (_motion == Motion.Held || _motion == Motion.Falling) return;
            if (slide) Fly(target.Parent, target.Position, target.Rotation, target.Scale, 0f, SettleTime, null);
            else GoToTarget();
        }

        private static bool Same(Target a, Target b) =>
            a.Parent == b.Parent && a.Land == b.Land && (a.Position - b.Position).sqrMagnitude < 1e-7f
            && Quaternion.Angle(a.Rotation, b.Rotation) < .1f && Mathf.Abs(a.Scale - b.Scale) < 1e-4f;

        private void GoToTarget()
        {
            var target = _target;
            // Дуга — только при перелёте с места на место: внутри веера карты перестраиваются скольжением, а не подпрыгивают.
            var arc = transform.parent == target.Parent ? 0f : FlightArc;
            if (!target.Land || _body == null)
            {
                Fly(target.Parent, target.Position, target.Rotation, target.Scale, arc, Duration(FlightTime), target.Arrived);
                return;
            }
            var above = target.Position + target.Parent.InverseTransformDirection(Vector3.up) * DropHeight;
            var endDownVel = target.Parent != null ? target.Parent.InverseTransformVector(Vector3.down * (DropSpeed * .7f)) : Vector3.down * (DropSpeed * .7f);
            FlyHermite(target.Parent, above, target.Rotation, target.Scale, Vector3.zero, endDownVel, Duration(FlightTime), Drop, FlightArc);
        }

        private void Fly(Transform parent, Vector3 position, Quaternion rotation, float scale, float arc, float duration, Action arrived)
        {
            StopBody();
            // При перелёте между разными родителями (например, со стола/колоды в подвижный веер руки) летим в мировых координатах
            // к актуальной точке цели и привязываемся к родителю только при приземлении: поворот руки или головы не швыряет карту в потолок!
            _flyWorldSpace = transform.parent != parent;
            _flyTargetParent = parent;
            if (_flyWorldSpace)
            {
                transform.SetParent(null, true);
                _fromPosition = transform.position;
                _fromRotation = transform.rotation;
                _fromScale = transform.lossyScale;
            }
            else
            {
                _fromPosition = transform.localPosition;
                _fromRotation = transform.localRotation;
                _fromScale = transform.localScale;
            }
            _toPosition = position;
            _toRotation = rotation;
            _toScale = Vector3.one * scale;
            _fromLocalVel = Vector3.zero;
            _toLocalVel = Vector3.zero;
            _useHermite = false;
            _time = 0f;
            _duration = Mathf.Max(.01f, duration);
            _arc = arc;
            _arrived = arrived;
            _motion = Motion.Flying;
        }

        private void FlyHermite(Transform parent, Vector3 position, Quaternion rotation, float scale, Vector3 startLocalVel, Vector3 endLocalVel, float duration, Action arrived, float arc = 0f, bool clampFloorY = false)
        {
            StopBody();
            _flyWorldSpace = false;
            _flyTargetParent = parent;
            if (transform.parent != parent) transform.SetParent(parent, true);
            _fromPosition = transform.localPosition;
            _fromRotation = transform.localRotation;
            _fromScale = transform.localScale;
            _toPosition = position;
            _toRotation = rotation;
            _toScale = Vector3.one * scale;
            _fromLocalVel = startLocalVel;
            _toLocalVel = endLocalVel;
            _useHermite = true;
            _clampFloorY = clampFloorY;
            _time = 0f;
            _duration = Mathf.Max(.01f, duration);
            _arc = arc;
            _arrived = arrived;
            _motion = Motion.Flying;
        }

        private void Drop()
        {
            if (ReducedMotion)
            {
                Fly(_target.Parent, _target.Position, _target.Rotation, _target.Scale, 0f, Duration(SettleTime), _target.Arrived);
                return;
            }
            _body.isKinematic = false;
            _body.interpolation = RigidbodyInterpolation.Interpolate;
            _body.linearVelocity = Vector3.down * DropSpeed;
            _body.angularVelocity = Vector3.zero;
            _fallTime = 0f;
            _motion = Motion.Falling;
            Landings++;
        }

        private void Update()
        {
            if (_motion == Motion.Falling)
            {
                _fallTime += Time.deltaTime;
                var resting = _fallTime > MinFall && _body.linearVelocity.sqrMagnitude < RestSpeed * RestSpeed && _body.angularVelocity.sqrMagnitude < 1f;
                if (resting || _fallTime > SettleLimit) Settle();
                return;
            }
            if (_motion != Motion.Flying) return;
            _time += Time.deltaTime;
            var t = Mathf.Clamp01(_time / _duration);
            var k = t * t * t * (t * (t * 6f - 15f) + 10f);
            if (_useHermite)
            {
                var t2 = t * t;
                var t3 = t2 * t;
                var h00 = 2f * t3 - 3f * t2 + 1f;
                var h10 = t3 - 2f * t2 + t;
                var h01 = -2f * t3 + 3f * t2;
                var h11 = t3 - t2;
                var bell = Mathf.Sin(t * Mathf.PI);
                var pos = h00 * _fromPosition + h10 * (_fromLocalVel * _duration) + h01 * _toPosition + h11 * (_toLocalVel * _duration)
                    + Vector3.up * (bell * bell * _arc);
                if (_clampFloorY) pos.y = Mathf.Max(pos.y, _toPosition.y);
                transform.localPosition = pos;
                // При подхвате с начальной скоростью вращение и масштаб идут мягко с первой миллисекунды без «ступеньки» на старте.
                var rotK = _fromLocalVel.sqrMagnitude > 1e-4f ? 1f - (1f - t) * (1f - t) : k;
                transform.localRotation = Quaternion.Slerp(_fromRotation, _toRotation, rotK);
                transform.localScale = Vector3.Lerp(_fromScale, _toScale, rotK);
            }
            else if (_flyWorldSpace)
            {
                var goalPos = _flyTargetParent != null ? _flyTargetParent.TransformPoint(_toPosition) : _toPosition;
                var goalRot = _flyTargetParent != null ? _flyTargetParent.rotation * _toRotation : _toRotation;
                var goalScale = _flyTargetParent != null ? Vector3.Scale(_flyTargetParent.lossyScale, _toScale) : _toScale;
                transform.position = Vector3.Lerp(_fromPosition, goalPos, k) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * _arc);
                transform.rotation = Quaternion.Slerp(_fromRotation, goalRot, k);
                transform.localScale = Vector3.Lerp(_fromScale, goalScale, k);
            }
            else
            {
                transform.localPosition = Vector3.Lerp(_fromPosition, _toPosition, k) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * _arc);
                transform.localRotation = Quaternion.Slerp(_fromRotation, _toRotation, k);
                transform.localScale = Vector3.Lerp(_fromScale, _toScale, k);
            }
            if (t < 1f) return;
            if (_flyWorldSpace && _flyTargetParent != null)
            {
                transform.SetParent(_flyTargetParent, false);
                transform.localPosition = _toPosition;
                transform.localRotation = _toRotation;
                transform.localScale = _toScale;
                _flyWorldSpace = false;
            }
            _motion = Motion.Still;
            var arrived = _arrived;
            _arrived = null;
            arrived?.Invoke();
        }

        /// <summary>Карта легла: физика выключается, и карта короткой доводкой встаёт на место, которое ей дала партия.</summary>
        private void Settle()
        {
            StopBody();
            _motion = Motion.Still;
            if (!_target.Land)
            {
                GoToTarget();
                return;
            }
            Fly(_target.Parent, _target.Position, _target.Rotation, _target.Scale, 0f, Duration(SettleTime), _target.Arrived);
        }

        private void StopBody()
        {
            if (_body == null || _body.isKinematic) return;
            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;
            _body.interpolation = RigidbodyInterpolation.None;
            _body.isKinematic = true;
        }

        private static bool ReducedMotion => PlayerHubRuntime.Current?.Settings.ReducedMotion ?? false;

        private static float Duration(float normal) => ReducedMotion ? Mathf.Min(normal, ReducedFlightTime) : normal;

        private static Material SideMaterial => _sideMaterial != null ? _sideMaterial : _sideMaterial = Table3DArt.Lit("card side", new Color(.84f, .79f, .68f), 0f);
        private static Material OpponentSideMaterial => _opponentSideMaterial != null ? _opponentSideMaterial : _opponentSideMaterial = Table3DArt.Lit("card opp side", new Color(.28f, .29f, .19f), 0f);

        private static Material HighlightMaterial => _highlightMaterial != null ? _highlightMaterial : _highlightMaterial = Table3DArt.Unlit("card highlight", new Color(1f, .79f, .32f));

        private static PhysicsMaterial Cloth => _cloth != null ? _cloth : _cloth = new PhysicsMaterial("card on cloth")
        {
            dynamicFriction = .5f, staticFriction = .6f, bounciness = 0f, bounceCombine = PhysicsMaterialCombine.Minimum
        };

        private static Material MaterialFor(Texture texture)
        {
            // Play запускается без перезагрузки скриптов: таблица переживает остановку игры, а материалы в ней уже удалены.
            if (Materials.TryGetValue(texture, out var material) && material != null) return material;
            // Трилинейная + анизотропная фильтрация убирают рябь мелкого золотого узора рубашки и мастей под острым углом к столу.
            texture.filterMode = FilterMode.Trilinear;
            texture.anisoLevel = 8;
            material = new Material(Table3DArt.Template("Card")) { name = "card " + texture.name };
            material.SetTexture("_BaseMap", texture);
            material.SetTexture("_EmissionMap", texture);
            material.SetColor(EmissionColor, Glow);
            Materials[texture] = material;
            return material;
        }

        private static Texture LoadTexture(string key) => Resources.Load<Sprite>("Cards/" + key).texture;

        private static string FaceKey(Card card)
        {
            if (card.IsJoker) return card.Suit == Suit.Hearts ? "joker_red" : "joker_black";
            var rank = card.Rank switch
            {
                Rank.Ten => "10", Rank.Jack => "j", Rank.Queen => "q", Rank.King => "k", Rank.Ace => "a",
                _ => ((int)card.Rank).ToString()
            };
            var suit = card.Suit switch
            {
                Suit.Clubs => "clubs", Suit.Diamonds => "diamonds", Suit.Hearts => "hearts", _ => "spades"
            };
            return rank + "_" + suit;
        }
    }
}
