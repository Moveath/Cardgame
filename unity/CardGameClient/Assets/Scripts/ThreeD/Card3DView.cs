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
        private const float FlightTime = .45f, FlightArc = .08f, ReducedFlightTime = .1f;
        private const float DropHeight = .07f;                  // Карта отпускается над своим местом и падает на сукно.
        private const float DropSpeed = .35f;
        private const float SettleTime = .16f, SettleLimit = 1.2f, MinFall = .2f, RestSpeed = .04f;
        private const float BodyDepth = .002f;                  // Коллайдер толще самой карты: тонкий проскакивает стол.
        private const float CardMass = .005f, AirDrag = 2f, AirSpin = 3f, Contact = .0008f;
        // Слабая подсветка картинки: значения читаются в тени руки, но карта остаётся картоном под лампой, а не экраном.
        private static readonly Color Glow = new Color(.1f, .1f, .1f);
        private static readonly Color Dimmed = new Color(.66f, .63f, .6f);
        private static readonly Dictionary<Texture, Material> Materials = new Dictionary<Texture, Material>();
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private static Material _sideMaterial, _highlightMaterial;
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
        private Vector3 _fromPosition, _toPosition, _fromScale, _toScale;
        private Quaternion _fromRotation, _toRotation;
        private float _time, _duration, _arc, _fallTime;
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
            view._renderer.sharedMaterials = new[] { MaterialFor(LoadTexture(faceUp ? FaceKey(card) : "back")), MaterialFor(LoadTexture("back")), SideMaterial };

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

        public void SetSelected(bool selected) => _highlight.SetActive(selected);

        /// <summary>Карта, которой сейчас нельзя ходить. Прозрачность не годится: в веере проступили бы карты под ней.</summary>
        public void SetDimmed(bool dimmed)
        {
            _tint = _tint ?? new MaterialPropertyBlock();
            _tint.SetColor(BaseColor, dimmed ? Dimmed : Color.white);
            _tint.SetColor(EmissionColor, dimmed ? Glow * Dimmed : Glow);
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
            Fly(target.Parent, above, target.Rotation, target.Scale, FlightArc, Duration(FlightTime), Drop);
        }

        private void Fly(Transform parent, Vector3 position, Quaternion rotation, float scale, float arc, float duration, Action arrived)
        {
            StopBody();
            if (transform.parent != parent) transform.SetParent(parent, true);
            _fromPosition = transform.localPosition;
            _fromRotation = transform.localRotation;
            _fromScale = transform.localScale;
            _toPosition = position;
            _toRotation = rotation;
            _toScale = Vector3.one * scale;
            _time = 0f;
            _duration = duration;
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
            var k = Mathf.SmoothStep(0f, 1f, t);
            transform.localPosition = Vector3.Lerp(_fromPosition, _toPosition, k) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * _arc);
            transform.localRotation = Quaternion.Slerp(_fromRotation, _toRotation, k);
            transform.localScale = Vector3.Lerp(_fromScale, _toScale, k);
            if (t < 1f) return;
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

        private static Material SideMaterial => _sideMaterial != null ? _sideMaterial : _sideMaterial = Table3DArt.Lit("card side", new Color(.93f, .89f, .8f), .25f);

        private static Material HighlightMaterial => _highlightMaterial != null ? _highlightMaterial : _highlightMaterial = Table3DArt.Unlit("card highlight", new Color(1f, .79f, .32f));

        private static PhysicsMaterial Cloth => _cloth != null ? _cloth : _cloth = new PhysicsMaterial("card on cloth")
        {
            dynamicFriction = .5f, staticFriction = .6f, bounciness = 0f, bounceCombine = PhysicsMaterialCombine.Minimum
        };

        private static Material MaterialFor(Texture texture)
        {
            // Play запускается без перезагрузки скриптов: таблица переживает остановку игры, а материалы в ней уже удалены.
            if (Materials.TryGetValue(texture, out var material) && material != null) return material;
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
