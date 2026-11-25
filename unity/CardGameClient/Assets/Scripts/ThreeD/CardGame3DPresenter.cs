using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CardGame.Bots;
using CardGame.Core.Cards;
using CardGame.Core.Games.Durak;
using CardGame.Core.Meta;
using UnityEngine;

namespace CardGameClient.ThreeD
{
    /// <summary>
    /// Связывает DurakGame с 3D-столом: правила только в ядре, здесь — где лежит каждая видимая карта.
    /// Открытая карта живёт как один объект: перелетает из руки на стол, со стола в биту или к взявшему.
    /// </summary>
    public sealed class CardGame3DPresenter : MonoBehaviour
    {
        private const int HumanIndex = 0;
        private const float BotDelay = .95f;
        private const int DiscardShown = 60;

        private const float FanGap = .00115f;  // Шаг карт веера к камере (~0,5 мм чистого воздуха сверх толщины 0,65 мм, чтобы соседние карты не цепляли друг друга).
        // Хват подобран под веер из шести карт (scripts/3d/hands/in_game.py, FAN_FITTED): при другом числе карт передняя карта
        // остаётся на той же глубине, под большим пальцем, а стопка растёт от игрока, за карты. Раньше она росла к игроку,
        // и при 9+ картах большой палец тонул в стопке.
        private const int FanFitted = 6;

        private Camera _camera;
        private TableCameraController _look;
        private OwnArms3DView _arms;
        private CardTable3DHud _hud;
        private Transform _world;
        private Transform _playerSeatRoot;
        private GameObject _playerFpsBody;
        private string _playerAvatarId;
        private PlayerSeat3DView[] _seats = new PlayerSeat3DView[0];
        private string[] _seatHeads = new string[0];
        private readonly Dictionary<Card, Card3DView> _cards = new Dictionary<Card, Card3DView>();
        private readonly List<Card3DView> _discard = new List<Card3DView>();
        private readonly List<Card3DView> _transientCards = new List<Card3DView>();
        private readonly HashSet<Card> _pendingHumanCards = new HashSet<Card>();
        private GameObject _deck;
        private Card3DView[] _deckLayers = new Card3DView[0];
        private GameObject _deckShadow;
        private DurakGame _game;
        private SimpleDurakBot _bot;
        private Card? _selected;
        private float _nextBotMove;
        private float _nextAmbientEmoteAt = 6f;
        private string _lastHumanGesture;
        private float _lastEmptyTapTime = -10f;
        private int _discardSeed, _discardLayer, _lastBoutsFinished, _prevDeckCount;
        private int? _visualDeckCount;
        private bool _reported, _choreoBusy;
        private string _choreoStatus;

        public DurakGame Game => _game;
        public Camera EyeCamera => _camera;
        public IReadOnlyDictionary<Card, Card3DView> Cards => _cards;
        public GameObject PlayerGlass { get; private set; }
        private bool AnyOpponentHoldingCard => _seats.Any(s => s != null && s.IsHoldingThrownCard);
        public bool IsBusy => _choreoBusy || AnyOpponentHoldingCard || (_arms != null && _arms.Busy) || _cards.Values.Any(v => v.IsFlying);

        public void Configure(Camera camera, TableCameraController look, CardTable3DHud hud, Transform world, OwnArms3DView arms)
        {
            _camera = camera;
            _arms = arms;
            _arms.CardPulledFromFan += OnCardPulledFromFan;
            _look = look;
            _hud = hud;
            _world = world;
            _look.Tapped += OnScreenTapped;
            _look.TableZoomChanged += _hud.SetTableZoom;
            _hud.SetCallbacks(TakeCards, PassBout, TransferSelected, StartNewGame, CardTable3DRoomFlow.ReturnToLobby, _look.ToggleTableZoom);
            _hud.SetTableZoom(_look.IsTableZoomed);
            BuildDeck();
            BuildPlayerGlass();
            BuildPlayerSeatAndBody(ResolveOwnAvatarId(CardTable3DRoomFlow.Current));
            StartNewGame();
        }

        private void Update()
        {
            if (_game == null || _game.IsFinished) return;
            TickHumanGestureReactions();
            // Ждём, пока своя рука или рука соперника полностью положит карту, завершится сбор/уход кучки и раздача:
            // соперник не нажимает «БИТО» и не ходит поверх незавершённой анимации!
            if (_choreoBusy || AnyOpponentHoldingCard || _arms.Busy || _cards.Values.Any(v => v.IsFlying))
            {
                _nextBotMove = Mathf.Max(_nextBotMove, Time.time + .42f);
                return;
            }
            TickAmbientBotEmotes();
            var actor = DurakTurnOrder.GetActingPlayer(_game);
            if (actor.HasValue && actor.Value != HumanIndex && Time.time >= _nextBotMove) PlayBotMove(actor.Value);
            else if (!actor.HasValue && _game.HasChallengeableMove)
            {
                _game.CloseChallengeWindow();
                RefreshViews();
            }
        }

        private void LateUpdate()
        {
            if (_game == null) return;
            for (var i = 0; i < _seats.Length; i++) _hud.PlaceSeat(i, _camera, _seats[i].LabelPoint);
        }

        private void StartNewGame()
        {
            var room = CardTable3DRoomFlow.Current;
            ApplyPlayerAvatar(ResolveOwnAvatarId(room));
            BuildSeatLineup(room, out var names, out var seatHeads);
            var settings = room?.Settings ?? HubGameSettings.DefaultFor("durak");
            var deck = settings.DeckSize == 24 ? DeckKind.Twenty4 : settings.DeckSize == 52 ? DeckKind.FiftyTwo :
                settings.DeckSize == 54 ? DeckKind.FiftyFour : DeckKind.ThirtySix;
            var match = new DurakMatchSettings
            {
                PlayerCount = names.Length,
                DeckSize = deck,
                AllowTransfer = settings.AllowTransfer,
                ThrowInMode = settings.NeighboursOnly ? ThrowInMode.NeighboursOnly : ThrowInMode.All,
                AllowCheating = settings.AllowCheating
            };
            ClearTable();
            _game = DurakGame.Create(names, match.BuildDeck(Random.Range(1, int.MaxValue)), match.BuildOptions());
            _bot = new SimpleDurakBot(new System.Random(Random.Range(1, int.MaxValue)));
            _selected = null;
            _reported = false;
            _lastBoutsFinished = _game.BoutsFinished;
            _prevDeckCount = _game.DeckCount;
            _nextBotMove = Time.time + 1.2f;
            BuildSeats(seatHeads);
            _hud.SetTrump(_game.Trump);
            if (ReducedMotion) RefreshViews();
            else BeginOpeningDeal();
        }

        private void ClearTable()
        {
            StopAllCoroutines();
            _choreoBusy = false;
            _choreoStatus = null;
            _visualDeckCount = null;
            _pendingHumanCards.Clear();
            _arms.Clear();
            foreach (var seat in _seats) seat?.CancelAction();
            _nextAmbientEmoteAt = Time.time + Random.Range(6.5f, 11f);
            _lastHumanGesture = null;
            foreach (var card in _cards.Values) if (card != null) Destroy(card.gameObject);
            foreach (var card in _discard) if (card != null) Destroy(card.gameObject);
            foreach (var card in _transientCards) if (card != null) Destroy(card.gameObject);
            _cards.Clear();
            _discard.Clear();
            _transientCards.Clear();
            _discardLayer = 0;
            _lastBoutsFinished = 0;
            _prevDeckCount = 0;
        }

        private static bool ReducedMotion => PlayerHubRuntime.Current?.Settings.ReducedMotion ?? false;

        /// <summary>
        /// Согласует 3D-голову, аватар и имя на плашке для каждого соперника:
        /// - если выбран друг (Марат, Аслан, Али, Данил, Егор, Ельнур) — на плашке ВСЕГДА его имя и его 3D-голова (если уже готова);
        /// - для стартовых персонажей имя подбирается строго по полу персонажа (мужчинам мужские, женщинам женские).
        /// </summary>
        private static string ResolveOwnAvatarId(CardTable3DRoomFlow.RoomContext room)
        {
            var fromRoom = room?.AvatarIds != null && room.AvatarIds.Length > 0 ? room.AvatarIds[0] : null;
            if (!string.IsNullOrEmpty(fromRoom)) return fromRoom;
            var saved = PlayerHubRuntime.AvatarName;
            return !string.IsNullOrEmpty(saved) ? saved : "danil_01";
        }

        private static void BuildSeatLineup(CardTable3DRoomFlow.RoomContext room, out string[] names, out string[] seatHeads)
        {
            var humanName = room?.PlayerNames != null && room.PlayerNames.Length > 0
                ? room.PlayerNames[0]
                : PlayerHubRuntime.DisplayName;
            var ownAvatarId = ResolveOwnAvatarId(room);

            var nameList = new List<string> { humanName };

            if (room?.PlayerNames != null && room.PlayerNames.Length > 1)
            {
                var opponents = room.PlayerNames.Length - 1;
                seatHeads = new string[opponents];
                for (var i = 0; i < opponents; i++)
                {
                    var idx = i + 1;
                    var rawName = room.PlayerNames[idx];
                    var avId = room.AvatarIds != null && idx < room.AvatarIds.Length ? room.AvatarIds[idx] : null;
                    var headId = AvatarCatalog.Available3DHeadForAvatar(avId);
                    seatHeads[i] = headId;

                    var personal = AvatarCatalog.PersonalTitleOfId(headId ?? avId);
                    if (!string.IsNullOrEmpty(personal))
                    {
                        nameList.Add(AvatarCatalog.NameForAvatar(headId ?? avId, nameList));
                    }
                    else if (!string.IsNullOrEmpty(rawName) && !rawName.StartsWith("Бот"))
                    {
                        nameList.Add(rawName);
                    }
                    else
                    {
                        nameList.Add(AvatarCatalog.NameForAvatar(avId, nameList));
                    }
                }
                names = nameList.ToArray();
                return;
            }

            // Прямой запуск 3D-сцены (Play Mode): ставим все готовые 3D-головы друзей начиная с центрального места напротив игрока,
            // подписывая каждого друга ЕГО именем, а свободным мужским местам выдаём случайные мужские имена.
            const int defaultOpponents = 5;
            seatHeads = new string[defaultOpponents];
            var availableFriends = AvatarCatalog.AvailableFriend3DHeads(ownAvatarId);
            for (var i = availableFriends.Count - 1; i > 0; i--)
            {
                var j = Random.Range(0, i + 1);
                (availableFriends[i], availableFriends[j]) = (availableFriends[j], availableFriends[i]);
            }
            var centerSeat = defaultOpponents / 2;
            for (var i = 0; i < defaultOpponents; i++)
            {
                var order = (i - centerSeat + defaultOpponents) % defaultOpponents;
                seatHeads[i] = order < availableFriends.Count ? availableFriends[order].HeadId : null;
            }
            for (var i = 0; i < defaultOpponents; i++)
            {
                nameList.Add(AvatarCatalog.NameForAvatar(seatHeads[i], nameList));
            }
            names = nameList.ToArray();
        }

        private void BuildSeats(string[] seatHeads)
        {
            var opponents = _game.Players.Count - 1;
            var sameLayout = _seats.Length == opponents && _seatHeads.Length == opponents;
            for (var i = 0; sameLayout && i < opponents; i++)
            {
                var wantHead = seatHeads != null && i < seatHeads.Length ? seatHeads[i] : null;
                if (!string.Equals(_seatHeads[i], wantHead, System.StringComparison.Ordinal))
                    sameLayout = false;
            }
            if (sameLayout) return;

            foreach (var seat in _seats) Destroy(seat.gameObject);
            var angles = Table3DLayout.SeatAngles(opponents);
            _seats = new PlayerSeat3DView[opponents];
            _seatHeads = new string[opponents];
            for (var i = 0; i < opponents; i++)
            {
                var headId = seatHeads != null && i < seatHeads.Length ? seatHeads[i] : null;
                _seatHeads[i] = headId;
                _seats[i] = PlayerSeat3DView.Create(_world, i + 1, angles[i], headId);
            }
            _hud.BuildSeats(opponents);
        }

        private const int DeckLayerCount = 20;
        private static readonly int EdgeColorProp = Shader.PropertyToID("_BaseColor");

        private void BuildDeck()
        {
            _deck = new GameObject("Deck");
            _deck.transform.SetParent(_world, false);
            _deck.transform.position = Table3DLayout.DeckAt;
            // Живой лёгкий разворот всей стопки, как в реальной партии (не строго по линейке).
            _deck.transform.rotation = Quaternion.Euler(0f, -7.5f, 0f);

            // Мягкая контактная тень под стопкой на сукне, чтобы колода выглядела тяжёлой и плотно лежащей на ткани.
            var scale = Table3DLayout.DeckCardScale;
            var cardW = CardMesh3D.Width * scale;
            var cardH = CardMesh3D.Height * scale;
            _deckShadow = new GameObject("Deck shadow", typeof(SpriteRenderer));
            _deckShadow.transform.SetParent(_deck.transform, false);
            _deckShadow.transform.localPosition = new Vector3(.004f, .0005f, -.005f);
            _deckShadow.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            _deckShadow.transform.localScale = new Vector3(cardW * 1.38f, cardH * 1.28f, 1f);
            var sr = _deckShadow.GetComponent<SpriteRenderer>();
            sr.sprite = BuildSoftRectShadowSprite(64);
            sr.color = new Color(.06f, .008f, .012f, .56f);

            // 20 реальных 3D-карт в стопке с естественным живым разбросом, чуть выпирающими краями и слоями картона.
            _deckLayers = new Card3DView[DeckLayerCount];
            var rng = new System.Random(4217);
            var edgeBlock = new MaterialPropertyBlock();
            const float baseLift = .0028f;
            const float layerStep = .00148f;
            for (var i = 0; i < DeckLayerCount; i++)
            {
                var card = Card3DView.Create(_deck.transform, new Card(Rank.Nine, Suit.Clubs), false, false);
                card.name = "Deck card " + i;
                var slipped = i == 3 || i == 7 || i == 11 || i == 15 || i == 18;
                var jitterX = slipped ? .0062f : .0026f;
                var jitterZ = slipped ? .0052f : .0028f;
                var jitterYaw = slipped ? 6.2f : 2.8f;
                var dx = ((float)rng.NextDouble() * 2f - 1f) * jitterX;
                var dz = ((float)rng.NextDouble() * 2f - 1f) * jitterZ;
                var yaw = ((float)rng.NextDouble() * 2f - 1f) * jitterYaw;
                // Верхняя карта чуть аккуратнее, но всё равно с живым ручным сдвигом.
                if (i == DeckLayerCount - 1) { dx *= .45f; dz *= .45f; yaw *= .55f; }
                card.Place(_deck.transform, new Vector3(dx, baseLift + i * layerStep, dz), Quaternion.Euler(-90f, yaw, 0f), scale);
                card.transform.localScale = new Vector3(scale, scale, scale * 1.16f);

                // Чередование оттенка торца картона (слоистость бумаги) + мягкие тени между выпирающими картами.
                var mr = card.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    var shade = .78f + .16f * ((i * 37 % 11) / 10f);
                    edgeBlock.SetColor(EdgeColorProp, new Color(.95f * shade, .90f * shade, .80f * shade, 1f));
                    mr.SetPropertyBlock(edgeBlock, 2);
                }
                _deckLayers[i] = card;
            }
        }

        private static Sprite BuildSoftRectShadowSprite(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++)
            {
                var ny = (y + .5f) / size * 2f - 1f;
                for (var x = 0; x < size; x++)
                {
                    var nx = (x + .5f) / size * 2f - 1f;
                    // Мягкий скруглённый прямоугольник тени: плотный под стопкой, плавно тает к краям.
                    var qx = Mathf.Max(0f, Mathf.Abs(nx) - .42f) / .58f;
                    var qy = Mathf.Max(0f, Mathf.Abs(ny) - .46f) / .54f;
                    var d = Mathf.Clamp01(Mathf.Sqrt(qx * qx + qy * qy));
                    var alpha = (1f - d) * (1f - d);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(.5f, .5f), size);
        }

        private void BuildPlayerGlass()
        {
            var at = Table3DLayout.GlassPosition(0f);
            PlayerGlass = Table3DArt.SpawnGlass(_world, at, Table3DLayout.GlassRotation(0f, at), "Glass Player");
        }

        /// <summary>
        /// Ставит бархатное клубное кресло прямо под игроком и сажает в него тело первого лица (женское, плотное
        /// или стандартное под выбранного персонажа), пристыковывает верхние рукава OwnArms3DView к плечам и
        /// выставляет соответствующий тон кожи кистей!
        /// </summary>
        private void BuildPlayerSeatAndBody(string ownAvatarId)
        {
            if (_playerSeatRoot == null)
            {
                _playerSeatRoot = new GameObject("Player Seat").transform;
                _playerSeatRoot.SetParent(_world, false);
                _playerSeatRoot.SetPositionAndRotation(Table3DLayout.SeatPosition(0f), Table3DLayout.SeatRotation(0f));

                var chair = Table3DArt.Spawn("chair", _playerSeatRoot);
                chair.transform.localPosition = new Vector3(0f, 0f, -.08f);
                chair.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            }
            ApplyPlayerAvatar(ownAvatarId);
        }

        public void ApplyPlayerAvatar(string ownAvatarId)
        {
            if (string.IsNullOrEmpty(ownAvatarId)) ownAvatarId = "danil_01";
            if (_playerFpsBody != null && string.Equals(_playerAvatarId, ownAvatarId, System.StringComparison.OrdinalIgnoreCase))
                return;
            _playerAvatarId = ownAvatarId;
            if (_playerFpsBody != null)
            {
                Destroy(_playerFpsBody);
                _playerFpsBody = null;
            }
            if (_playerSeatRoot == null) return;

            var archetype = AvatarCatalog.BodyArchetypeOf(ownAvatarId);
            var bodyRes = AvatarCatalog.FpsBodyResourceFor(archetype);
            var bodyPrefab = Resources.Load<GameObject>(bodyRes) ?? Resources.Load<GameObject>("ThreeD/Characters/BaseMale_FpsBody");
            if (bodyPrefab == null)
            {
                if (_arms != null) _arms.ApplyCharacterStyle(ownAvatarId);
                return;
            }

            var body = Instantiate(bodyPrefab, _playerSeatRoot, false);
            body.name = "Player_FpsBody_" + archetype;
            _playerFpsBody = body;
            var bodyScale = archetype == AvatarCatalog.BodyArchetype.MaleHeavy ? .96f
                : (archetype == AvatarCatalog.BodyArchetype.Female || archetype == AvatarCatalog.BodyArchetype.FemaleElder) ? .90f
                : .94f;
            var bodyY = archetype == AvatarCatalog.BodyArchetype.MaleHeavy ? .052f
                : (archetype == AvatarCatalog.BodyArchetype.Female || archetype == AvatarCatalog.BodyArchetype.FemaleElder) ? .060f
                : .055f;
            // Бёдра в джинсах лежат СТРОГО поверх бархатной подушки, спина у спинки кресла,
            // ворот свитера ниже и позади камеры глаз, а плечи на идеальной высоте для пристыковки рукавов!
            body.transform.localPosition = new Vector3(0f, bodyY, -.145f);
            body.transform.localRotation = Quaternion.identity;
            body.transform.localScale = Vector3.one * bodyScale;

            var outfitMat = Table3DArt.Template("BodyMale_Outfit");
            foreach (var smr in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mats = smr.sharedMaterials;
                for (var i = 0; i < mats.Length; i++) mats[i] = outfitMat;
                smr.sharedMaterials = mats;
                smr.updateWhenOffscreen = true;
            }

            var anim = body.GetComponent<Animator>();
            if (anim == null) anim = body.AddComponent<Animator>();
            if (anim.avatar == null || !anim.avatar.isHuman)
            {
                var refPrefab = Resources.Load<GameObject>("ThreeD/Characters/BaseMale_Headless")
                    ?? Resources.Load<GameObject>("ThreeD/Characters/BaseMale");
                if (refPrefab != null) anim.avatar = refPrefab.GetComponent<Animator>()?.avatar;
            }
            var ctrl = Resources.Load<RuntimeAnimatorController>("ThreeD/Characters/OpponentAnimator");
            if (ctrl != null)
            {
                anim.runtimeAnimatorController = ctrl;
                anim.applyRootMotion = false;
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                anim.speed = .65f;
                anim.Play("SitIdle", 0, 0f);
                anim.Update(0f);
                body.AddComponent<PlayerFpsBodyBreath>().Bind(anim, _camera != null ? _camera.transform : null, _playerSeatRoot);
            }

            // Сам меш FPS-тела герметичен (глубокая тёмно-бирюзовая чаша внутри ворота + 0 дыр),
            // плюс широкая внутренняя страховка в тон изнанки свитера внутри верхней груди.
            var chestBone = anim.GetBoneTransform(HumanBodyBones.UpperChest) ?? anim.GetBoneTransform(HumanBodyBones.Neck);
            if (chestBone != null)
            {
                var collarPlug = Table3DArt.Ball(chestBone, new Vector3(0f, .105f, .012f), new Vector3(.205f, .095f, .175f), Table3DArt.Lit("fps collar plug", new Color(.018f, .052f, .062f), 0f));
                collarPlug.name = "CollarInnerPlug";
            }

            var leftArm = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            var rightArm = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
            if (_arms != null)
            {
                if (leftArm != null && rightArm != null)
                    _arms.AttachBodyShoulders(leftArm, rightArm);
                _arms.ApplyCharacterStyle(ownAvatarId);
            }
        }

        /// <summary>
        /// Плавно разворачивает верх корпуса игрока (поясницу, грудь и плечи) вслед за поворотом камеры глаз
        /// (до ±48°), чтобы всё тело двигалось едино с руками, плюс живое дыхание груди.
        /// </summary>
        private sealed class PlayerFpsBodyBreath : MonoBehaviour
        {
            private Transform _spine, _chest, _upperChest, _eye, _seatRoot;
            private Quaternion _spineNeutral, _chestNeutral, _upperChestNeutral;
            private float _curTorsoYaw;

            public void Bind(Animator anim, Transform eye, Transform seatRoot)
            {
                _eye = eye;
                _seatRoot = seatRoot;
                _spine = anim.GetBoneTransform(HumanBodyBones.Spine);
                _chest = anim.GetBoneTransform(HumanBodyBones.Chest);
                _upperChest = anim.GetBoneTransform(HumanBodyBones.UpperChest);
                if (_spine != null) _spineNeutral = _spine.localRotation;
                if (_chest != null) _chestNeutral = _chest.localRotation;
                if (_upperChest != null) _upperChestNeutral = _upperChest.localRotation;
            }

            private void LateUpdate()
            {
                var breath = Mathf.Sin(Time.time * 1.25f) * .75f;
                var wantYaw = 0f;
                if (_eye != null && _seatRoot != null)
                {
                    var camYaw = Mathf.DeltaAngle(_seatRoot.eulerAngles.y, _eye.eulerAngles.y);
                    wantYaw = Mathf.Clamp(camYaw * .56f, -48f, 48f);
                }
                _curTorsoYaw = Mathf.Lerp(_curTorsoYaw, wantYaw, 1f - Mathf.Exp(-8f * Time.deltaTime));

                if (_spine != null)
                {
                    _spine.localRotation = Quaternion.Slerp(_spineNeutral, _spine.localRotation, .12f);
                    if (Mathf.Abs(_curTorsoYaw) > .05f)
                        _spine.rotation = Quaternion.AngleAxis(_curTorsoYaw * .34f, Vector3.up) * _spine.rotation;
                }
                if (_chest != null)
                {
                    _chest.localRotation = Quaternion.Slerp(_chestNeutral, _chest.localRotation, .12f);
                    _chest.localRotation *= Quaternion.Euler(-breath, 0f, 0f);
                    if (Mathf.Abs(_curTorsoYaw) > .05f)
                        _chest.rotation = Quaternion.AngleAxis(_curTorsoYaw * .40f, Vector3.up) * _chest.rotation;
                }
                if (_upperChest != null)
                {
                    _upperChest.localRotation = Quaternion.Slerp(_upperChestNeutral, _upperChest.localRotation, .12f);
                    if (Mathf.Abs(_curTorsoYaw) > .05f)
                        _upperChest.rotation = Quaternion.AngleAxis(_curTorsoYaw * .26f, Vector3.up) * _upperChest.rotation;
                }
            }
        }

        private void PaintDeck()
        {
            var count = _visualDeckCount ?? _game.DeckCount;
            // Когда в колоде остаётся только открытый козырь (count == 1), рубашек сверху уже нет.
            var visible = count <= 1 ? 0 : Mathf.Clamp(Mathf.CeilToInt((count - 1) * (DeckLayerCount / 26f)), 1, DeckLayerCount);
            _deck.SetActive(visible > 0);
            for (var i = 0; i < _deckLayers.Length; i++)
                _deckLayers[i].gameObject.SetActive(i < visible);
        }

        private static Vector3 DeckTopPos(int deckCount)
        {
            var visible = deckCount <= 1 ? 0 : Mathf.Clamp(Mathf.CeilToInt((deckCount - 1) * (DeckLayerCount / 26f)), 1, DeckLayerCount);
            var topY = .0028f + Mathf.Max(0, visible - 1) * .00148f + .0015f;
            return Table3DLayout.DeckAt + new Vector3(0f, topY, 0f);
        }

        private void DestroyTransient(Card3DView view)
        {
            if (view == null) return;
            _transientCards.Remove(view);
            Destroy(view.gameObject);
        }

        private void PlayBotMove(int actor)
        {
            if (_choreoBusy || AnyOpponentHoldingCard) return;
            _nextBotMove = Time.time + BotDelay;
            var move = _bot.ChooseMove(_game, actor);
            if (!move.HasValue) return;
            if (!_game.TryApplyMove(actor, move.Value, out var error))
            {
                Debug.LogWarning("3D-дурак: ход бота отклонён: " + error);
                return;
            }
            var seat = actor - 1 >= 0 && actor - 1 < _seats.Length ? _seats[actor - 1] : null;
            if (seat != null && !ReducedMotion)
            {
                if (move.Value.Type == DurakMoveType.Pass)
                {
                    seat.PlayGesture(Random.value < .55f ? "TapTable" : "Nod");
                    NotifySeatsLookAt(seat.HeadWorldPosition, excludePlayer: actor, duration: 1.6f);
                }
                else if (move.Value.Type == DurakMoveType.Transfer && _game.DefenderIndex > 0 && _game.DefenderIndex - 1 < _seats.Length)
                {
                    var nextDef = _seats[_game.DefenderIndex - 1];
                    if (nextDef != null && !nextDef.IsActing && Random.value < .65f)
                    {
                        var r = Random.value;
                        nextDef.PlayGesture(r < .35f ? "MiddleFinger" : r < .68f ? "Shrug" : "ShakeHead", seat.HeadWorldPosition);
                    }
                }
            }
            RefreshViews(actor);
        }

        private static readonly string[] AmbientGestures = { "TapTable", "Shrug", "ThumbsUp", "Point", "Fist", "Nod", "ThumbsDown", "MiddleFinger" };

        private void TickAmbientBotEmotes()
        {
            if (ReducedMotion || _seats.Length == 0 || Time.time < _nextAmbientEmoteAt) return;
            _nextAmbientEmoteAt = Time.time + Random.Range(9.5f, 16.5f);
            if (_seats.Any(s => s != null && s.IsActing)) return;

            var candidates = new List<int>();
            var acting = DurakTurnOrder.GetActingPlayer(_game);
            for (var i = 0; i < _seats.Length; i++)
            {
                var pIndex = i + 1;
                if (_seats[i] == null || _game.Players[pIndex].IsOut || pIndex == acting) continue;
                candidates.Add(i);
            }
            if (candidates.Count == 0) return;

            var pick = candidates[Random.Range(0, candidates.Count)];
            var seat = _seats[pick];
            var gesture = AmbientGestures[Random.Range(0, AmbientGestures.Length)];
            // Каждый жест бота адресован конкретному живому человеку за столом (тебе или другому игроку):
            // бот поворачивает голову, плечи и сам жест прямо на него!
            Vector3? target = null;
            if (gesture != "TapTable")
            {
                var otherSeats = new List<PlayerSeat3DView>();
                for (var i = 0; i < _seats.Length; i++)
                    if (i != pick && _seats[i] != null && !_game.Players[i + 1].IsOut) otherSeats.Add(_seats[i]);
                if (acting.HasValue && acting.Value > 0 && acting.Value - 1 < _seats.Length && acting.Value - 1 != pick && Random.value < .45f)
                    target = _seats[acting.Value - 1].HeadWorldPosition;
                else if (otherSeats.Count > 0 && Random.value < .38f)
                    target = otherSeats[Random.Range(0, otherSeats.Count)].HeadWorldPosition;
                else if (_camera != null)
                    target = _camera.transform.position;
            }
            seat.PlayGesture(gesture, target);
        }

        private void TickHumanGestureReactions()
        {
            if (_arms == null || ReducedMotion) return;
            var cur = _arms.Gesture;
            if (cur == _lastHumanGesture) return;
            _lastHumanGesture = cur;
            if (string.IsNullOrEmpty(cur) || _camera == null) return;

            var playerPos = _camera.transform.position;
            NotifySeatsLookAt(playerPos, excludePlayer: HumanIndex, duration: 2.0f);

            // Один из свободных друзей отвечает живым жестом на твой жест!
            var free = new List<PlayerSeat3DView>();
            for (var i = 0; i < _seats.Length; i++)
                if (_seats[i] != null && !_seats[i].IsActing && !_game.Players[i + 1].IsOut) free.Add(_seats[i]);
            if (free.Count == 0) return;

            var responder = free[Random.Range(0, free.Count)];
            string reply;
            switch (cur)
            {
                case "ThumbsUp": reply = Random.value < .65f ? "ThumbsUp" : "Nod"; break;
                case "ThumbsDown": reply = Random.value < .30f ? "MiddleFinger" : Random.value < .65f ? "Shrug" : "ThumbsDown"; break;
                case "MiddleFinger": reply = Random.value < .72f ? "MiddleFinger" : Random.value < .88f ? "Disbelief" : "ThumbsDown"; break;
                case "TapTable": reply = "TapTable"; break;
                case "Fist": reply = Random.value < .55f ? "Fist" : "ThumbsUp"; break;
                default: reply = Random.value < .55f ? "Shrug" : "Nod"; break;
            }
            responder.PlayGesture(reply, playerPos);
        }

        private void NotifySeatsLookAt(Vector3 worldPos, int excludePlayer, float duration)
        {
            if (ReducedMotion) return;
            for (var i = 0; i < _seats.Length; i++)
            {
                if (i + 1 == excludePlayer || _seats[i] == null) continue;
                _seats[i].LookAtWorld(worldPos, duration + (i % 3) * .18f);
            }
        }

        private void ApplyHumanMove(DurakMove move)
        {
            if (_choreoBusy) return;
            if (!_game.TryApplyMove(HumanIndex, move, out var error))
            {
                Debug.LogWarning("3D-дурак: ход отклонён: " + error);
                return;
            }
            _selected = null;
            _nextBotMove = Time.time + BotDelay;
            RefreshViews();
        }

        private void TakeCards()
        {
            if (HumanMay(DurakMoveType.Take)) ApplyHumanMove(DurakMove.Take());
        }

        private void PassBout()
        {
            if (HumanMay(DurakMoveType.Pass)) ApplyHumanMove(DurakMove.Pass());
        }

        private void TransferSelected()
        {
            var moves = HumanMoves(_selected).Where(m => m.Type == DurakMoveType.Transfer).ToArray();
            if (moves.Length > 0) ApplyHumanMove(moves[0]);
        }

        private bool HumanMay(DurakMoveType type) => _game != null && HumanActs && _game.GetLegalMoves(HumanIndex).Any(m => m.Type == type);

        private bool HumanActs => !_choreoBusy && DurakTurnOrder.GetActingPlayer(_game) == HumanIndex;

        private IEnumerable<DurakMove> HumanMoves(Card? card)
        {
            if (!card.HasValue || !HumanActs) return Enumerable.Empty<DurakMove>();
            return _game.GetLegalMoves(HumanIndex).Where(m => m.Card == card.Value &&
                (m.Type == DurakMoveType.Attack || m.Type == DurakMoveType.Defend || m.Type == DurakMoveType.Transfer));
        }

        private void OnScreenTapped(Vector2 screen)
        {
            if (_game == null) return;
            var view = CardUnder(screen);
            if (view == null)
            {
                // Двойной тап по пустому сукну (в любой момент, даже во время хода ботов) приближает/отдаляет центр стола.
                var now = Time.unscaledTime;
                if (now - _lastEmptyTapTime <= .34f)
                {
                    _lastEmptyTapTime = -10f;
                    _look.ToggleTableZoom();
                }
                else _lastEmptyTapTime = now;
                return;
            }
            _lastEmptyTapTime = -10f;
            if (!HumanActs) return;
            if (_game.Players[HumanIndex].Hand.Contains(view.Card)) ChooseCard(view.Card);
            else if (_selected.HasValue) ChooseTarget(view.Card);
        }

        /// <summary>Верхняя карта под пальцем. Сыгранная карта ещё пролетает над веером: нажатие достаётся картам под ней.</summary>
        private Card3DView CardUnder(Vector2 screen)
        {
            Physics.SyncTransforms(); // Карты двигаются не физикой: без этого луч попал бы туда, где они были на прошлом шаге.
            var hand = _game.Players[HumanIndex].Hand;
            foreach (var hit in Physics.RaycastAll(_camera.ScreenPointToRay(screen), 10f).OrderBy(h => h.distance))
            {
                var view = hit.collider.GetComponentInParent<Card3DView>();
                if (view != null && (!view.IsFlying || hand.Contains(view.Card))) return view;
            }
            return null;
        }

        /// <summary>Один вариант хода — играется сразу; несколько — карта поднимается и ждёт цель на столе или «ПЕРЕВЕСТИ».</summary>
        public void ChooseCard(Card card)
        {
            var moves = HumanMoves(card).ToArray();
            if (moves.Length == 0) return;
            if (moves.Length == 1)
            {
                ApplyHumanMove(moves[0]);
                return;
            }
            _selected = _selected == card ? (Card?)null : card;
            RefreshViews();
        }

        private void ChooseTarget(Card tableCard)
        {
            for (var slot = 0; slot < _game.Table.Count; slot++)
            {
                if (_game.Table[slot].Attack != tableCard) continue;
                var moves = HumanMoves(_selected).Where(m => m.Type == DurakMoveType.Defend && m.TargetSlot == slot).ToArray();
                if (moves.Length > 0) ApplyHumanMove(moves[0]);
                return;
            }
        }

        private void RefreshViews(int? moved = null)
        {
            if (_game.BoutsFinished > _lastBoutsFinished)
            {
                _lastBoutsFinished = _game.BoutsFinished;
                if (!ReducedMotion)
                {
                    BeginBoutResolution();
                    return;
                }
            }
            var shown = new HashSet<Card>();
            PlaceTable(shown, moved);
            PlaceHand(shown);
            PlaceTrump(shown);
            SweepGone(shown);
            PaintDeck();
            for (var i = 0; i < _seats.Length; i++)
            {
                var player = _game.Players[i + 1];
                _seats[i].SetHolding(!player.IsOut && player.Hand.Count > 0);
            }
            _prevDeckCount = _game.DeckCount;
            _arms.KeepFanRaised = _game.Players[HumanIndex].Hand.Count > 0;
            PaintHud();
            ReportIfFinished();
        }

        /// <summary>Начальная раздача партии: колода полная, руки соперников сначала пусты, карты по кругу летят с верха колоды каждому игроку.</summary>
        private void BeginOpeningDeal()
        {
            var draws = new List<int>();
            for (var round = 0; round < DurakRules.HandSize; round++)
                for (var p = 0; p < _game.Players.Count; p++)
                    if (round < _game.Players[p].Hand.Count)
                        draws.Add(p);

            var humanDraws = new Queue<Card>(_game.Players[HumanIndex].Hand);
            _pendingHumanCards.Clear();
            foreach (var c in humanDraws) _pendingHumanCards.Add(c);
            foreach (var seat in _seats) seat.SetHolding(false);

            _visualDeckCount = _game.DeckCount + draws.Count;
            _arms.KeepFanRaised = _game.Players[HumanIndex].Hand.Count > 0;
            PlaceTrump(null);
            PaintDeck();
            _choreoBusy = true;
            _choreoStatus = "РАЗДАЧА КАРТ";
            PaintHud();
            StartCoroutine(RunOpeningDeal(draws, humanDraws));
        }

        private IEnumerator RunOpeningDeal(List<int> draws, Queue<Card> humanDraws)
        {
            yield return new WaitForSeconds(.12f);
            var interval = draws.Count > 18 ? .085f : .125f;
            yield return DealSequence(draws, humanDraws, trumpDrawnAtEnd: false, interval);
            _visualDeckCount = null;
            _pendingHumanCards.Clear();
            _choreoBusy = false;
            _choreoStatus = null;
            _nextBotMove = Time.time + .75f;
            RefreshViews();
        }

        /// <summary>
        /// Завершение розыгрыша (бито или взятие): карты со стола сначала собираются в аккуратную кучку,
        /// затем плавно летят в биту или в руки взявшего, а после этого с колоды по одной летят карты добора каждому игроку.
        /// </summary>
        private void BeginBoutResolution()
        {
            var boutCards = _game.LastBoutCards.ToList();
            var taker = _game.LastBoutTakenBy;
            var draws = _game.LastRefillDraws.ToList();
            var trumpWasUnderDeck = _prevDeckCount > 0 && _cards.ContainsKey(_game.TrumpCard) && !boutCards.Contains(_game.TrumpCard);
            var trumpDrawnNow = trumpWasUnderDeck && _game.DeckCount == 0;

            var boutPairs = new List<(Card card, Card3DView view)>();
            foreach (var c in boutCards)
            {
                if (!_cards.TryGetValue(c, out var v) || v == null) continue;
                boutPairs.Add((c, v));
                if (taker == HumanIndex)
                {
                    _pendingHumanCards.Add(c);
                }
                else
                {
                    _cards.Remove(c);
                    _transientCards.Add(v);
                }
            }

            var newHumanDraws = new Queue<Card>();
            foreach (var c in _game.Players[HumanIndex].Hand)
            {
                if (!_cards.ContainsKey(c))
                {
                    _pendingHumanCards.Add(c);
                    newHumanDraws.Enqueue(c);
                }
                else if (trumpDrawnNow && c == _game.TrumpCard)
                {
                    // Открытый козырь остаётся под колодой, пока до него не дойдёт очередь на последнем доборе.
                    _pendingHumanCards.Add(c);
                }
            }

            _visualDeckCount = _game.DeckCount + draws.Count;
            PaintDeck();
            _choreoBusy = true;
            _choreoStatus = !taker.HasValue
                ? "БИТО"
                : taker.Value == HumanIndex
                    ? "БЕРЁТЕ КАРТЫ"
                    : _game.Players[taker.Value].Name.ToUpperInvariant() + " БЕРЁТ";
            PaintHud();
            StartCoroutine(RunBoutResolution(boutPairs, taker, draws, newHumanDraws, trumpDrawnNow));
        }

        private IEnumerator RunBoutResolution(
            List<(Card card, Card3DView view)> boutPairs,
            int? taker,
            List<int> draws,
            Queue<Card> newHumanDraws,
            bool trumpDrawnNow)
        {
            // Дожидаемся, пока последняя сыгранная карта и рука (игрока или бота) полностью опустятся на стол.
            while (AnyOpponentHoldingCard || (_arms != null && _arms.Busy) || boutPairs.Any(p => p.view != null && p.view.IsFlying))
                yield return null;
            yield return new WaitForSeconds(.14f);

            // Фаза 1: карты со стола плавно стягиваются в одну аккуратную стопку в центре (без переворота сквозь сукно!).
            if (boutPairs.Count > 0)
            {
                boutPairs.Sort((a, b) => a.view.transform.position.y.CompareTo(b.view.transform.position.y));
                var sum = Vector3.zero;
                for (var i = 0; i < boutPairs.Count; i++) sum += boutPairs[i].view.transform.localPosition;
                var avg = sum / boutPairs.Count;
                var gatherCenter = Vector3.Lerp(avg, new Vector3(Table3DLayout.PairShift, Table3DLayout.TableY, -.02f), .45f);
                gatherCenter.y = Table3DLayout.TableY + .028f;
                var gatherStep = CardMesh3D.Thickness * Table3DLayout.DiscardCardScale + .00045f;

                for (var i = 0; i < boutPairs.Count; i++)
                {
                    var v = boutPairs[i].view;
                    v.SetSelected(false);
                    v.SetDimmed(false);
                    v.SetOnTable(false);
                    var stackPos = gatherCenter + Vector3.up * (i * gatherStep);
                    var stackYaw = (i - (boutPairs.Count - 1) * .5f) * 3.6f;
                    // Все карты стягиваются лицом вверх ровной стопкой, а переворачиваются уже высоко в воздухе на дуге перелёта.
                    var stackRot = Quaternion.Euler(90f, stackYaw, 0f);
                    v.GlideTo(_world, stackPos, stackRot, Table3DLayout.DiscardCardScale, .42f, .020f + i * .003f, disableCollider: taker != HumanIndex);
                }
                yield return new WaitForSeconds(.58f);

                // Фаза 2: собранная кучка плавно поднимается высокой дугой (не касаясь сукна при перевороте!) и летит в биту или в руки.
                if (!taker.HasValue)
                {
                    var discardStep = CardMesh3D.Thickness * Table3DLayout.DiscardCardScale + .00045f;
                    for (var i = 0; i < boutPairs.Count; i++)
                    {
                        var v = boutPairs[i].view;
                        _transientCards.Remove(v);
                        var random = new System.Random(_discardSeed++);
                        var layer = _discardLayer++;
                        var dx = (float)random.NextDouble() * .056f - .028f;
                        var dz = (float)random.NextDouble() * .048f - .024f;
                        var yaw = (float)random.NextDouble() * 64f - 32f;
                        var at = Table3DLayout.DiscardAt + new Vector3(dx, .0022f + layer * discardStep, dz);
                        v.GlideTo(_world, at, Quaternion.Euler(-90f, yaw, 0f), Table3DLayout.DiscardCardScale, .72f, .130f + i * .005f, disableCollider: true);
                        _discard.Add(v);
                        if (_discard.Count > DiscardShown)
                        {
                            Destroy(_discard[0].gameObject);
                            _discard.RemoveAt(0);
                        }
                        if (i + 1 < boutPairs.Count) yield return new WaitForSeconds(.048f);
                    }
                    yield return new WaitForSeconds(.74f);
                }
                else if (taker.Value == HumanIndex)
                {
                    for (var i = 0; i < boutPairs.Count; i++)
                    {
                        var c = boutPairs[i].card;
                        _pendingHumanCards.Remove(c);
                        // Мягкая дуга над сукном прямо в веер игрока.
                        PlaceHand(null, flyingIntoFan: c, flyDuration: .62f, flyArc: .048f);
                        if (i + 1 < boutPairs.Count) yield return new WaitForSeconds(.065f);
                    }
                    yield return new WaitForSeconds(.64f);
                }
                else
                {
                    var seat = _seats[taker.Value - 1];
                    seat.PlayTakeReach(_world.TransformPoint(gatherCenter));
                    NotifySeatsLookAt(_world.TransformPoint(gatherCenter), excludePlayer: taker.Value, duration: 1.4f);
                    var toPos = _world.InverseTransformPoint(seat.HandAnchor.position);
                    var toRot = Quaternion.Inverse(_world.rotation) * seat.HandAnchor.rotation;
                    for (var i = 0; i < boutPairs.Count; i++)
                    {
                        var v = boutPairs[i].view;
                        v.GlideTo(_world, toPos, toRot, 1.05f, .66f, .145f + i * .005f,
                            arrived: () => { seat.SetHolding(true); DestroyTransient(v); }, disableCollider: true);
                        if (i + 1 < boutPairs.Count) yield return new WaitForSeconds(.055f);
                    }
                    yield return new WaitForSeconds(.68f);
                    // Если бот набрал много карт — живая реакция досады (иногда в сердцах показывает фак атакующему!)
                    if (!ReducedMotion && boutPairs.Count >= 3 && Random.value < .70f)
                    {
                        var r = Random.value;
                        var react = r < .32f ? "MiddleFinger" : r < .58f ? "Disbelief" : r < .80f ? "ShakeHead" : "Shrug";
                        seat.PlayGesture(react, _camera != null ? _camera.transform.position : (Vector3?)null);
                    }
                }
            }

            // Фаза 3: добор с колоды — ровно столько карт, сколько нужно, летит по одной каждому игроку в руки.
            if (draws.Count > 0)
            {
                _choreoStatus = "ДОБОР КАРТ";
                PaintHud();
                yield return new WaitForSeconds(.22f);
                yield return DealSequence(draws, newHumanDraws, trumpDrawnNow, .175f);
            }

            _visualDeckCount = null;
            _pendingHumanCards.Clear();
            _choreoBusy = false;
            _choreoStatus = null;
            _prevDeckCount = _game.DeckCount;
            _nextBotMove = Time.time + .60f;
            RefreshViews();
        }

        /// <summary>Раздаёт или добирает карты по одной прямо с верхней карты колоды (и последним — открытый козырь из-под неё).</summary>
        private IEnumerator DealSequence(List<int> draws, Queue<Card> humanDraws, bool trumpDrawnAtEnd, float interval)
        {
            for (var k = 0; k < draws.Count; k++)
            {
                var p = draws[k];
                var deckBefore = _visualDeckCount ?? _game.DeckCount;
                var takingTrump = trumpDrawnAtEnd && k == draws.Count - 1 && _cards.TryGetValue(_game.TrumpCard, out var trumpView) && trumpView != null;
                _visualDeckCount = Mathf.Max(0, deckBefore - 1);
                PaintDeck();

                if (takingTrump)
                {
                    var tv = _cards[_game.TrumpCard];
                    tv.SetOnTable(false);
                    if (p == HumanIndex)
                    {
                        _pendingHumanCards.Remove(_game.TrumpCard);
                        PlaceHand(null, flyingIntoFan: _game.TrumpCard, flyDuration: .62f, flyArc: .052f);
                    }
                    else
                    {
                        _cards.Remove(_game.TrumpCard);
                        _transientCards.Add(tv);
                        var seat = _seats[p - 1];
                        var toPos = _world.InverseTransformPoint(seat.HandAnchor.position);
                        var toRot = Quaternion.Inverse(_world.rotation) * seat.HandAnchor.rotation;
                        tv.GlideTo(_world, toPos, toRot, 1.05f, .62f, .145f,
                            arrived: () => { seat.SetHolding(true); DestroyTransient(tv); }, disableCollider: true);
                    }
                }
                else
                {
                    var startPos = DeckTopPos(deckBefore);
                    var startRot = Quaternion.Euler(-90f, -7.5f, 0f);
                    if (p == HumanIndex)
                    {
                        if (humanDraws.Count > 0)
                        {
                            var c = humanDraws.Dequeue();
                            var v = Card3DView.Create(_world, c, true, true);
                            v.Place(_world, startPos, startRot, Table3DLayout.DeckCardScale);
                            _cards[c] = v;
                            _pendingHumanCards.Remove(c);
                            // Мягкая дуга в мировых координатах прямо с колоды над сукном в левую руку.
                            PlaceHand(null, flyingIntoFan: c, flyDuration: .56f, flyArc: .052f);
                        }
                    }
                    else
                    {
                        var seat = _seats[p - 1];
                        var toPos = _world.InverseTransformPoint(seat.HandAnchor.position);
                        var toRot = Quaternion.Inverse(_world.rotation) * seat.HandAnchor.rotation;
                        var v = Card3DView.Create(_world, new Card(Rank.Nine, Suit.Clubs), false, false);
                        v.Place(_world, startPos, startRot, Table3DLayout.DeckCardScale);
                        _transientCards.Add(v);
                        v.GlideTo(_world, toPos, toRot, 1.05f, .56f, .135f,
                            arrived: () => { seat.SetHolding(true); DestroyTransient(v); }, disableCollider: true);
                    }
                }

                if (k + 1 < draws.Count) yield return new WaitForSeconds(interval);
            }
            yield return new WaitForSeconds(.56f);
        }

        private void PlaceTable(HashSet<Card> shown, int? moved)
        {
            var table = _game.Table;
            var scale = Table3DLayout.TablePairScale(table.Count);
            // Смещение бьющей карты масштабируется вместе с размером карт, чтобы угол с номиналом нижней карты всегда был открыт.
            var stagger = scale / 2f;
            // Зазор по высоте строго больше реальной 3D-толщины карты (Thickness * scale ≈ 1,65 мм), чтобы бьющая карта никогда не прорастала сквозь нижнюю.
            var defenseLift = CardMesh3D.Thickness * scale + .0014f;
            for (var slot = 0; slot < table.Count; slot++)
            {
                var at = Table3DLayout.PairPosition(slot, table.Count);
                PutOnTable(table[slot].Attack, at + new Vector3(-.024f * stagger, 0f, .034f * stagger), scale, shown, moved, 0f);
                if (table[slot].Defense.HasValue)
                    PutOnTable(table[slot].Defense.Value, at + new Vector3(.032f * stagger, defenseLift, -.034f * stagger), scale, shown, moved, 6f);
            }
        }

        private void PutOnTable(Card card, Vector3 at, float scale, HashSet<Card> shown, int? moved, float turn)
        {
            shown.Add(card);
            var wasNew = !_cards.ContainsKey(card);
            var view = Find(card, moved.HasValue ? _seats[moved.Value - 1].HandAnchor.position : Table3DLayout.DeckAt);
            // Карты брошены рукой: у каждой свой постоянный небольшой наклон.
            var tilt = ((int)card.Rank * 7 + (int)card.Suit * 3) % 7 - 3 + turn;
            view.SetSelected(false);
            view.SetOnTable(true);
            view.SetDimmed(false);
            var rot = Quaternion.Euler(90f, tilt, 0f);
            var worldAt = _world.TransformPoint(at);
            var worldRot = _world.rotation * rot;
            // Карта только что ушла из веера игрока: её забирает правая рука игрока и несёт к слоту на столе.
            if (view.transform.parent == _arms.FanRoot && !view.IsHeld)
            {
                _arms.PlayCard(view, worldAt, worldRot, scale);
                NotifySeatsLookAt(worldAt, excludePlayer: HumanIndex, duration: 1.55f);
            }
            // Карта только что сыграна ботом/другом: он пригибается корпусом, вытягивает карту правой рукой и кладёт её на сукно!
            else if (wasNew && moved.HasValue && moved.Value != HumanIndex && !ReducedMotion && !view.IsHeld)
            {
                var seatIdx = moved.Value - 1;
                if (seatIdx >= 0 && seatIdx < _seats.Length && _seats[seatIdx] != null)
                {
                    _seats[seatIdx].PlayCardThrow(view, worldAt, worldRot, scale);
                    NotifySeatsLookAt(worldAt, excludePlayer: moved.Value, duration: 1.55f);
                }
            }
            view.Land(_world, at, rot, scale);
        }

        /// <summary>Когда правая рука полностью вытянула карту выше верхнего края веера, оставшиеся карты плавно смыкают просвет.</summary>
        private void OnCardPulledFromFan()
        {
            if (_game == null) return;
            PlaceHand(null);
        }

        private void PlaceHand(HashSet<Card> shown, Card? flyingIntoFan = null, float flyDuration = .38f, float flyArc = .088f)
        {
            var hand = _game.Players[HumanIndex].Hand.Where(c => !_pendingHumanCards.Contains(c)).ToList();
            // Карты, к которым правая рука ещё тянется или вытягивает их из веера, сохраняют свой слот и свою Z-глубину:
            // остальные карты НЕ схлопываются сквозь них, пока вытягиваемая карта полностью не выйдет выше веера!
            var reserved = new HashSet<Card>();
            foreach (var pair in _cards)
            {
                if (_arms.IsStillInFan(pair.Value) && !hand.Contains(pair.Key))
                {
                    hand.Add(pair.Key);
                    reserved.Add(pair.Key);
                }
            }
            hand.Sort(CompareInHand);
            var step = hand.Count > 1 ? Mathf.Min(Table3DLayout.HandFanStep, Table3DLayout.HandFanLimit / (hand.Count - 1)) : 0f;
            // Когда в руке больше 7 карт, добавляем лёгкий горизонтальный разнос веера, чтобы углы с мастями не слипались.
            var extraSpread = hand.Count > 7 ? Mathf.Min(.0042f, .036f / (hand.Count - 1)) : 0f;
            var legal = HumanActs ? _game.GetLegalMoves(HumanIndex) : null;
            for (var i = 0; i < hand.Count; i++)
            {
                var card = hand[i];
                if (reserved.Contains(card)) continue;
                shown?.Add(card);
                var view = Find(card, Table3DLayout.DeckAt);
                var offsetFromMid = i - (hand.Count - 1) / 2f;
                var spin = Quaternion.Euler(0f, 0f, -offsetFromMid * step);
                var selected = _selected == card;
                var along = spin * Vector3.up * (Table3DLayout.HandFanRadius + (selected ? Table3DLayout.HandLift : 0f));
                var slotPos = along + new Vector3(offsetFromMid * extraSpread, 0f, (hand.Count - FanFitted - i) * FanGap);
                view.SetSelected(selected);
                view.SetOnTable(false);
                view.SetDimmed(legal != null && !legal.Any(m => m.Card == card));
                if (flyingIntoFan.HasValue && card == flyingIntoFan.Value)
                {
                    view.GlideTo(_arms.FanRoot, slotPos, spin, Table3DLayout.HandCardScale, flyDuration, flyArc);
                }
                else if (!view.TryRetargetFanSlot(_arms.FanRoot, slotPos, spin, Table3DLayout.HandCardScale))
                {
                    // Веер держит левая кисть: его середина и наклон заданы точкой Fan на ней.
                    // Передняя карта всегда на одной глубине, под большим пальцем; стопка растёт от игрока, за карты (FanFitted).
                    view.MoveTo(_arms.FanRoot, slotPos, spin, Table3DLayout.HandCardScale);
                }
            }
        }

        private void PlaceTrump(HashSet<Card> shown)
        {
            if (_game.DeckCount == 0) return;
            var card = _game.TrumpCard;
            shown?.Add(card);
            var view = Find(card, Table3DLayout.DeckAt);
            view.SetSelected(false);
            view.SetOnTable(true);
            view.SetDimmed(false);
            // Колода у правого края стола: козырь выглядывает из-под неё к середине.
            view.MoveTo(_world, Table3DLayout.DeckAt + new Vector3(-.118f, .0012f, -.014f), Quaternion.Euler(90f, -81f, 0f), Table3DLayout.DeckCardScale);
        }

        /// <summary>Карта ушла со стола: к боту, у которого теперь в руке, иначе в биту.</summary>
        private void SweepGone(HashSet<Card> shown)
        {
            var discardOrder = 0;
            var discardStep = CardMesh3D.Thickness * Table3DLayout.DiscardCardScale + .00045f;
            foreach (var card in _cards.Keys.Where(c => !shown.Contains(c)).ToArray())
            {
                var view = _cards[card];
                _cards.Remove(card);
                var holder = _game.Players.FirstOrDefault(p => p.Index != HumanIndex && p.Hand.Contains(card));
                if (holder != null)
                {
                    view.SetOnTable(false);
                    view.MoveTo(_world, _world.InverseTransformPoint(_seats[holder.Index - 1].HandAnchor.position), Quaternion.identity, 1f,
                        () => Destroy(view.gameObject));
                    continue;
                }
                var random = new System.Random(_discardSeed++);
                // Строго монотонный счётчик слоя (_discardLayer): ни две карты в бите никогда не ложатся на одну высоту Y,
                // даже если в бите 30–54 карты (раньше после 24-й карты _discard.Count переставал расти и верхние карты Z-файтились!).
                var layer = _discardLayer++;
                var dx = (float)random.NextDouble() * .056f - .028f;
                var dz = (float)random.NextDouble() * .048f - .024f;
                var yaw = (float)random.NextDouble() * 64f - 32f;
                var at = Table3DLayout.DiscardAt + new Vector3(dx, .0022f + layer * discardStep, dz);
                view.SetOnTable(false);
                view.SendToDiscard(_world, at, Quaternion.Euler(-90f, yaw, 0f), Table3DLayout.DiscardCardScale, .075f + (discardOrder++) * .005f);
                _discard.Add(view);
                if (_discard.Count <= DiscardShown) continue;
                Destroy(_discard[0].gameObject);
                _discard.RemoveAt(0);
            }
        }

        private Card3DView Find(Card card, Vector3 from)
        {
            if (_cards.TryGetValue(card, out var view)) return view;
            view = Card3DView.Create(_world, card, true, true);
            view.Place(_world, _world.InverseTransformPoint(from), Quaternion.Euler(-90f, 0f, 0f), Table3DLayout.DeckCardScale);
            _cards[card] = view;
            return view;
        }

        private int CompareInHand(Card left, Card right)
        {
            int Group(Card card) => card.IsJoker ? 2 : card.Suit == _game.Trump ? 1 : 0;
            var byGroup = Group(left).CompareTo(Group(right));
            if (byGroup != 0) return byGroup;
            var byRank = left.Rank.CompareTo(right.Rank);
            return byRank != 0 ? byRank : left.Suit.CompareTo(right.Suit);
        }

        private void PaintHud()
        {
            var actor = DurakTurnOrder.GetActingPlayer(_game);
            var yours = !_choreoBusy && actor == HumanIndex && !_game.IsFinished;
            string status;
            if (!string.IsNullOrEmpty(_choreoStatus)) status = _choreoStatus;
            else if (_game.IsFinished) status = !_game.LoserIndex.HasValue ? "НИЧЬЯ" : _game.LoserIndex == HumanIndex ? "РЕВАНШ?" : "ПОБЕДА!";
            else if (yours) status = _game.DefenderIndex == HumanIndex ? "ВАША ЗАЩИТА" : "ВАШ ХОД";
            else status = actor.HasValue ? "ХОДИТ " + _game.Players[actor.Value].Name.ToUpperInvariant() : "ЗАВЕРШАЕМ РОЗЫГРЫШ";
            _hud.SetStatus(status, yours);
            var legal = yours ? _game.GetLegalMoves(HumanIndex) : null;
            _hud.SetActions(legal != null && legal.Any(m => m.Type == DurakMoveType.Take), legal != null && legal.Any(m => m.Type == DurakMoveType.Pass),
                _game.Table.All(slot => slot.IsDefended) ? "БИТО" : "ПАС", HumanMoves(_selected).Any(m => m.Type == DurakMoveType.Transfer), _game.IsFinished && !_choreoBusy);
            for (var i = 0; i < _seats.Length; i++)
            {
                var player = _game.Players[i + 1];
                _hud.SetSeat(i, player.Name, RoleOf(player), Fighting(player), actor == player.Index);
            }
        }

        private int Fighting(DurakPlayer player)
        {
            if (player.IsOut || _game.IsFinished) return 0;
            return player.Index == _game.DefenderIndex ? 2 : player.Index == _game.AttackerIndex ? 1 : 0;
        }

        private string RoleOf(DurakPlayer player)
        {
            if (player.IsOut) return "Вышел";
            if (_game.IsFinished) return _game.LoserIndex == player.Index ? "Остался с картами" : "";
            if (player.Index == _game.DefenderIndex) return "Защищается";
            if (player.Index == _game.AttackerIndex) return "Атакует";
            return DurakTurnOrder.GetActingPlayer(_game) == player.Index ? "Подкидывает" : "";
        }

        private void ReportIfFinished()
        {
            if (_reported || !_game.IsFinished) return;
            _reported = true;
            HubMatchReporter.ReportMatchCompleted("durak", _game.LoserIndex.HasValue && _game.LoserIndex.Value != HumanIndex, draw: !_game.LoserIndex.HasValue);
        }
    }
}
