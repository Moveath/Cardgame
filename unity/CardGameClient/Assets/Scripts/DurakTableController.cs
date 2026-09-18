using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CardGame.Bots;
using CardGame.Core.Cards;
using CardGame.Core.Games.Durak;
using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>
    /// Стол дурака: строит интерфейс и связывает клики игрока с логикой из CardGame.Core.
    /// Правила здесь не дублируются — какие ходы разрешены, решает GetLegalMoves.
    /// </summary>
    public sealed class DurakTableController : MonoBehaviour
    {
        private const int HumanIndex = 0;

        // Пул имён ботов — до 7 штук, чтобы хватило на стол максимального размера (8 игроков).
        private static readonly string[] BotNamePool = { "Борис", "Вера", "Игорь", "Ольга", "Пётр", "Марина", "Сергей" };

        private static readonly Color[] OpponentColors =
        {
            new Color(0.30f, 0.52f, 0.86f), new Color(0.82f, 0.42f, 0.62f), new Color(0.36f, 0.62f, 0.42f),
            new Color(0.80f, 0.55f, 0.24f), new Color(0.55f, 0.42f, 0.78f), new Color(0.75f, 0.30f, 0.30f),
            new Color(0.30f, 0.68f, 0.70f)
        };

        private static readonly Color ActiveGlow = new Color(1f, 0.85f, 0.3f);

        private DurakMatchSettings _settings = DurakMatchSettings.Default;
        private Action _onExit;

        private const float AvatarWidth = 126f;
        private const float AvatarHeight = 158f;

        private DurakGame _game;
        private SimpleDurakBot _bot;
        private Font _font;

        private RectTransform _tableRow;
        private RectTransform _handRow;
        private RectTransform _opponentsArea;
        private RectTransform _deckStackArea;
        private CardView _trumpCard;
        private RectTransform _trumpFrame;
        private Text _status;
        private Text _trumpInfo;
        private Text _deckInfo;
        private Text _lastAction;
        private Button _passButton;
        private Button _takeButton;
        private Button _restartButton;

        private DurakMove? _pendingMove;
        private bool _waitingForHuman;

        private readonly HashSet<Card> _tableCardsShown = new HashSet<Card>();
        private readonly List<TablePair> _tableSnapshot = new List<TablePair>();
        private string _tableSignature;
        private int? _lastActorIndex;
        private static Sprite[] _avatarPool;
        private Sprite[] _playerPortraits;

        private struct TablePair
        {
            public Card Attack;
            public Card? Defense;
        }

        /// <summary>Вызывается лобби до включения экрана игры, поэтому Start() уже видит нужные настройки.</summary>
        public void Configure(DurakMatchSettings settings, Action onExit)
        {
            _settings = settings;
            _onExit = onExit;
        }

        private void Start()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _bot = new SimpleDurakBot();

            BuildUi();
            StartNewGame();
        }

        private void StartNewGame()
        {
            var seed = UnityEngine.Random.Range(0, int.MaxValue);
            var playerNames = BuildPlayerNames(_settings.PlayerCount);
            _game = DurakGame.Create(playerNames, _settings.BuildDeck(seed));
            _pendingMove = null;
            _waitingForHuman = false;
            _lastActorIndex = null;
            _tableCardsShown.Clear();
            _tableSnapshot.Clear();
            _tableSignature = null;
            ShuffleAvatars();

            _lastAction.text = string.Empty;

            _trumpCard.ShowFace(_game.TrumpCard);
            _trumpCard.SetPlain();
            _trumpInfo.text = "козырь " + CardView.SuitText(_game.Trump);
            _restartButton.gameObject.SetActive(false);

            StopAllCoroutines();
            StartCoroutine(GameLoop());
        }

        private IEnumerator GameLoop()
        {
            while (!_game.IsFinished)
            {
                var actor = DurakTurnOrder.GetActingPlayer(_game);
                if (actor == null)
                {
                    break;
                }

                if (actor.Value == HumanIndex)
                {
                    _waitingForHuman = true;
                    _pendingMove = null;
                    Refresh();

                    yield return new WaitUntil(() => _pendingMove.HasValue);

                    var move = _pendingMove.Value;
                    _waitingForHuman = false;
                    _pendingMove = null;

                    if (_game.TryApplyMove(HumanIndex, move, out var error))
                    {
                        _lastActorIndex = HumanIndex;
                        RememberLastDefense(move);
                        SetLastAction(_game.Players[HumanIndex].Name + ": " + move);
                    }
                    else
                    {
                        Debug.LogWarning("Ход отклонён: " + error);
                    }

                    Refresh();
                    yield return new WaitForSeconds(CardView.FlightDuration);
                }
                else
                {
                    Refresh();
                    yield return new WaitForSeconds(1.1f);

                    var move = _bot.ChooseMove(_game, actor.Value);
                    if (move == null)
                    {
                        break;
                    }

                    var moverName = _game.Players[actor.Value].Name;
                    if (_game.TryApplyMove(actor.Value, move.Value, out _))
                    {
                        _lastActorIndex = actor.Value;
                        RememberLastDefense(move.Value);
                        SetLastAction(moverName + ": " + move.Value);
                    }

                    Refresh();
                    yield return new WaitForSeconds(CardView.FlightDuration);
                }
            }

            Refresh();
            _restartButton.gameObject.SetActive(true);
        }

        private void Refresh()
        {
            var legalMoves = _waitingForHuman ? _game.GetLegalMoves(HumanIndex) : new List<DurakMove>();

            RefreshTable();
            RefreshHand(legalMoves);
            RefreshOpponents();
            RefreshControls(legalMoves);
            RefreshDeckPile();
        }

        private static List<string> BuildPlayerNames(int playerCount)
        {
            var names = new List<string> { "Ты" };
            for (var i = 0; i < playerCount - 1; i++)
            {
                names.Add(BotNamePool[i % BotNamePool.Length]);
            }

            return names;
        }

        private void SetLastAction(string line)
        {
            _lastAction.text = line;
        }

        private void RefreshDeckPile()
        {
            CreateCardBackStack(_deckStackArea, _game.DeckCount, new Vector2(9f, 0f), 1.05f);
            _deckInfo.text = _game.DeckCount > 0 ? "в колоде: " + _game.DeckCount : "колода пуста";

            // Козырная карта лежит на дне колоды: когда колода кончилась, её уже забрал игрок,
            // поэтому убираем саму карту и оставляем только напоминание о масти.
            var trumpCardOnTable = _game.DeckCount > 0;
            _trumpCard.gameObject.SetActive(trumpCardOnTable);
            _trumpFrame.gameObject.SetActive(trumpCardOnTable);

            _trumpInfo.fontSize = trumpCardOnTable ? 28 : 40;
            _trumpInfo.rectTransform.anchoredPosition = trumpCardOnTable
                ? new Vector2(0f, -128f)
                : new Vector2(0f, -20f);
        }

        private void CreateCardBackStack(RectTransform container, int count, Vector2 step, float scale, int maxVisible = 5)
        {
            Clear(container);
            var visible = Mathf.Clamp(count, 0, maxVisible);

            for (var i = 0; i < visible; i++)
            {
                var back = CardView.Create(container, _font, scale);
                back.Rect.anchoredPosition = step * i;
                back.ShowBack();
                back.SetPlain();
            }
        }

        private void RefreshTable()
        {
            var pairs = CollectTablePairs();
            var signature = TableSignature(pairs);

            // Экран обновляется несколько раз подряд (после хода и на входе в следующий ход).
            // Если расклад не изменился, ничего не пересоздаём — иначе только что запущенный
            // перелёт карты уничтожается в том же кадре и его никто не видит.
            if (signature == _tableSignature)
            {
                return;
            }

            _tableSignature = signature;
            Clear(_tableRow);

            const float tableCardScale = 1.1f;
            var stillOnTable = new HashSet<Card>();

            foreach (var pair in pairs)
            {
                var container = new GameObject("Slot", typeof(RectTransform));
                container.transform.SetParent(_tableRow, false);
                ((RectTransform)container.transform).sizeDelta = new Vector2(190f, 260f);

                var attackPosition = new Vector2(-16f, 18f);
                CreateCardShadow(container.transform, attackPosition, tableCardScale);

                var attack = CardView.Create(container.transform, _font, tableCardScale);
                attack.Rect.anchoredPosition = attackPosition;
                attack.ShowFace(pair.Attack);
                attack.SetPlain();
                AnimateArrival(attack, pair.Attack);
                stillOnTable.Add(pair.Attack);

                if (pair.Defense.HasValue)
                {
                    var defensePosition = new Vector2(18f, -20f);
                    CreateCardShadow(container.transform, defensePosition, tableCardScale);

                    var defense = CardView.Create(container.transform, _font, tableCardScale);
                    defense.Rect.anchoredPosition = defensePosition;
                    defense.ShowFace(pair.Defense.Value);
                    defense.SetPlain();
                    AnimateArrival(defense, pair.Defense.Value);
                    stillOnTable.Add(pair.Defense.Value);
                }
            }

            _tableCardsShown.Clear();
            foreach (var card in stillOnTable)
            {
                _tableCardsShown.Add(card);
            }
        }

        /// <summary>
        /// Что рисовать на столе. Пока партия идёт — текущий расклад; когда она закончилась,
        /// логика уже сбросила стол в биту, поэтому показываем запомненную последнюю раздачу,
        /// чтобы было видно, чем отбились в конце.
        /// </summary>
        private List<TablePair> CollectTablePairs()
        {
            if (_game.Table.Count > 0)
            {
                _tableSnapshot.Clear();
                foreach (var slot in _game.Table)
                {
                    _tableSnapshot.Add(new TablePair { Attack = slot.Attack, Defense = slot.Defense });
                }

                return _tableSnapshot;
            }

            if (!_game.IsFinished)
            {
                _tableSnapshot.Clear();
            }

            return _tableSnapshot;
        }

        /// <summary>Дописывает в запомненный расклад последний ход — его логика успевает стереть до перерисовки.</summary>
        private void RememberLastDefense(DurakMove move)
        {
            if (move.Type != DurakMoveType.Defend || move.TargetSlot < 0 || move.TargetSlot >= _tableSnapshot.Count)
            {
                return;
            }

            var pair = _tableSnapshot[move.TargetSlot];
            pair.Defense = move.Card;
            _tableSnapshot[move.TargetSlot] = pair;
        }

        private static string TableSignature(List<TablePair> pairs)
        {
            var builder = new StringBuilder();
            foreach (var pair in pairs)
            {
                builder.Append(pair.Attack).Append('/').Append(pair.Defense).Append('|');
            }

            return builder.ToString();
        }

        private void AnimateArrival(CardView view, Card card)
        {
            if (_tableCardsShown.Contains(card))
            {
                return;
            }

            view.PlayFlyIn(SeatOffset(_lastActorIndex));
        }

        /// <summary>Откуда карта прилетает на стол: снизу — от игрока, иначе с его места за столом.</summary>
        private Vector2 SeatOffset(int? playerIndex)
        {
            if (!playerIndex.HasValue || playerIndex.Value == HumanIndex)
            {
                return new Vector2(0f, -380f);
            }

            return SeatPosition(playerIndex.Value - 1, _game.Players.Count - 1);
        }

        /// <summary>
        /// Места соперников по дуге вокруг стола: игрок сидит внизу, соперники занимают дугу
        /// от левого края через верх к правому. Чем их больше, тем шире разводится дуга —
        /// один садится строго напротив, двое по верхним углам, четверо и больше обходят стол.
        /// </summary>
        private static Vector2 SeatPosition(int seatIndex, int opponentCount)
        {
            const float topY = 390f;

            if (opponentCount <= 1)
            {
                return new Vector2(0f, topY);
            }

            float maxX, archDepth;
            if (opponentCount == 2)
            {
                maxX = 480f;
                archDepth = 40f;
            }
            else if (opponentCount == 3)
            {
                maxX = 700f;
                archDepth = 100f;
            }
            else
            {
                maxX = 820f;
                archDepth = 180f;
            }

            // По горизонтали места расставлены равномерно, а высота идёт по параболе:
            // равные промежутки читаются как ровная рассадка, изгиб — как край круглого стола.
            var t = seatIndex / (float)(opponentCount - 1);
            var x = Mathf.Lerp(-maxX, maxX, t);
            var normalized = x / maxX;
            return new Vector2(x, topY - archDepth * normalized * normalized);
        }

        private static void CreateCardShadow(Transform parent, Vector2 position, float scale)
        {
            var go = new GameObject("CardShadow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position + new Vector2(2f, -7f);
            rect.sizeDelta = new Vector2(CardView.Width * scale + 40f, CardView.Height * scale + 40f);

            var image = go.GetComponent<Image>();
            image.sprite = UiTextures.SoftShadow(96, 132, 14f, 20f, 0.45f);
            image.type = Image.Type.Sliced;
            image.raycastTarget = false;
        }

        private void RefreshHand(IReadOnlyList<DurakMove> legalMoves)
        {
            Clear(_handRow);

            var hand = _game.Players[HumanIndex].Hand;
            var count = hand.Count;
            if (count == 0)
            {
                return;
            }

            const float handCardScale = 1.15f;
            var cardWidth = CardView.Width * handCardScale;
            var angleStep = Mathf.Min(5f, 22f / Mathf.Max(count - 1, 1));

            // Та же логика, что и у соперников: после "комфортного" числа карт веер сжимается,
            // а не растягивается на весь экран — видно, что карт много, но выглядит компактно.
            const int comfortableHandCount = 8;
            var availableHandWidth = 1500f;
            if (count > comfortableHandCount)
            {
                availableHandWidth *= Mathf.Sqrt(comfortableHandCount / (float)count);
            }

            var spacing = Mathf.Min(cardWidth * 0.82f, availableHandWidth / count);
            var mid = (count - 1) / 2f;

            for (var i = 0; i < count; i++)
            {
                var card = hand[i];
                var view = CardView.Create(_handRow, _font, handCardScale);
                view.ShowFace(card);

                var offset = i - mid;
                var position = new Vector2(offset * spacing, -Mathf.Abs(offset) * 6f);
                view.SetRestTransform(position, -offset * angleStep);

                var move = FindMoveForCard(legalMoves, card);
                if (move.HasValue)
                {
                    var captured = move.Value;
                    view.SetClickable(() => _pendingMove = captured);
                }
                else if (_waitingForHuman)
                {
                    view.SetClickable(null);
                }
                else
                {
                    view.SetPlain();
                }
            }
        }

        private void RefreshOpponents()
        {
            Clear(_opponentsArea);

            var actingIndex = _waitingForHuman ? (int?)HumanIndex : DurakTurnOrder.GetActingPlayer(_game);
            var opponentCount = _game.Players.Count - 1;
            var scale = OpponentVisualScale(opponentCount);
            var seat = 0;

            foreach (var player in _game.Players)
            {
                if (player.Index == HumanIndex)
                {
                    continue;
                }

                var colorIndex = seat;
                var seatPosition = SeatPosition(seat, opponentCount);
                seat++;

                var container = new GameObject("Opponent", typeof(RectTransform), typeof(VerticalLayoutGroup));
                container.transform.SetParent(_opponentsArea, false);

                // Пивот сверху: место задаётся верхней точкой, а содержимое растёт вниз —
                // так нижний край блока предсказуем и не наползает на карты в центре стола.
                var containerRect = (RectTransform)container.transform;
                containerRect.anchorMin = containerRect.anchorMax = new Vector2(0.5f, 0.5f);
                containerRect.pivot = new Vector2(0.5f, 1f);
                containerRect.anchoredPosition = seatPosition;
                containerRect.sizeDelta = new Vector2(200f * scale, 250f * scale);

                var containerLayout = container.GetComponent<VerticalLayoutGroup>();
                containerLayout.spacing = 6f * scale;
                containerLayout.childAlignment = TextAnchor.UpperCenter;
                containerLayout.childControlWidth = false;
                containerLayout.childControlHeight = false;
                containerLayout.childForceExpandWidth = false;
                containerLayout.childForceExpandHeight = false;

                var isActing = actingIndex.HasValue && actingIndex.Value == player.Index;

                CreateAvatar(container.transform, player.Name.Substring(0, 1), OpponentColors[colorIndex % OpponentColors.Length], isActing, PortraitFor(player.Index), scale);

                var label = CreateText(container.transform, player.Name, Mathf.RoundToInt(26 * scale), TextAnchor.MiddleCenter);
                label.color = isActing ? ActiveGlow : new Color(0.94f, 0.95f, 0.93f);
                label.GetComponent<RectTransform>().sizeDelta = new Vector2(200f * scale, 34f * scale);

                if (player.Index == _game.DefenderIndex)
                {
                    CreateRoleChip(container.transform, "защита", new Color(0.24f, 0.45f, 0.78f), scale);
                }
                else if (player.Index == _game.AttackerIndex)
                {
                    CreateRoleChip(container.transform, "атака", new Color(0.72f, 0.31f, 0.20f), scale);
                }

                if (player.IsOut)
                {
                    var outText = CreateText(container.transform, "вышел", Mathf.RoundToInt(20 * scale), TextAnchor.MiddleCenter);
                    outText.GetComponent<RectTransform>().sizeDelta = new Vector2(160f * scale, 28f * scale);
                    continue;
                }

                var row = new GameObject("Cards", typeof(RectTransform));
                row.transform.SetParent(container.transform, false);
                ((RectTransform)row.transform).sizeDelta = new Vector2(200f * scale, 90f * scale);

                var oppCount = player.Hand.Count;
                if (oppCount > 0)
                {
                    var oppScale = 0.6f * scale;
                    var oppWidth = CardView.Width * oppScale;
                    var oppAngleStep = Mathf.Min(6f, 24f / Mathf.Max(oppCount - 1, 1));

                    // Сверх "комфортного" числа карт веер не растёт вширь, а наоборот
                    // сжимается — большая рука выглядит плотной стопкой, а не растянутым рядом.
                    const int comfortableOppCount = 6;
                    var availableWidth = 200f * scale;
                    if (oppCount > comfortableOppCount)
                    {
                        availableWidth *= Mathf.Sqrt(comfortableOppCount / (float)oppCount);
                    }

                    var oppSpacing = Mathf.Min(oppWidth * 0.55f, availableWidth / oppCount);
                    var oppMid = (oppCount - 1) / 2f;

                    for (var h = 0; h < oppCount; h++)
                    {
                        var back = CardView.Create(row.transform, _font, oppScale);
                        back.ShowBack();
                        back.SetPlain();

                        var oppOffset = h - oppMid;
                        var oppPos = new Vector2(oppOffset * oppSpacing, -Mathf.Abs(oppOffset) * 4f);
                        back.SetRestTransform(oppPos, -oppOffset * oppAngleStep);
                    }
                }
            }
        }

        /// <summary>Чем больше соперников за столом, тем меньше места на каждого — ужимаем фигуру и карты.</summary>
        private static float OpponentVisualScale(int opponentCount)
        {
            if (opponentCount <= 2) return 0.85f;
            if (opponentCount <= 4) return 0.75f;
            return 0.65f;
        }

        private void CreateRoleChip(Transform parent, string text, Color color, float scale)
        {
            var go = new GameObject("RoleChip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(108f * scale, 36f * scale);

            var image = go.GetComponent<Image>();
            image.sprite = UiTextures.RoundedRect(64, 40, 14f, color);
            image.type = Image.Type.Sliced;
            image.raycastTarget = false;

            var label = CreateText(rect, text, Mathf.RoundToInt(20 * scale), TextAnchor.MiddleCenter);
            Stretch((RectTransform)label.transform);
            label.fontStyle = FontStyle.Bold;
        }

        /// <summary>
        /// Портреты берутся из Resources/Avatars и раздаются игрокам случайно на партию.
        /// Пока картинок нет, всё работает по-старому — цветной кружок с буквой.
        /// </summary>
        private void ShuffleAvatars()
        {
            if (_avatarPool == null)
            {
                _avatarPool = Resources.LoadAll<Sprite>("Avatars");
            }

            _playerPortraits = new Sprite[_game.Players.Count];
            if (_avatarPool.Length == 0)
            {
                return;
            }

            var order = new List<int>();
            for (var i = 0; i < _avatarPool.Length; i++)
            {
                order.Add(i);
            }

            for (var i = order.Count - 1; i > 0; i--)
            {
                var j = UnityEngine.Random.Range(0, i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            for (var player = 0; player < _playerPortraits.Length; player++)
            {
                _playerPortraits[player] = _avatarPool[order[player % order.Count]];
            }
        }

        private Sprite PortraitFor(int playerIndex)
        {
            return _playerPortraits != null && playerIndex < _playerPortraits.Length
                ? _playerPortraits[playerIndex]
                : null;
        }

        private void CreateAvatar(Transform parent, string initial, Color color, bool isActing, Sprite portrait, float scale)
        {
            var width = AvatarWidth * scale;
            var height = AvatarHeight * scale;

            var wrapper = new GameObject("Avatar", typeof(RectTransform));
            wrapper.transform.SetParent(parent, false);
            ((RectTransform)wrapper.transform).sizeDelta = new Vector2(width, height);

            if (isActing)
            {
                // Подсветка ходящего игрока: мягкое пятно света под персонажем вместо рамки,
                // потому что рамка вернула бы ту самую "картинку в коробочке", от которой ушли.
                var glow = new GameObject("Glow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                glow.transform.SetParent(wrapper.transform, false);
                var glowRect = (RectTransform)glow.transform;
                glowRect.anchorMin = glowRect.anchorMax = glowRect.pivot = new Vector2(0.5f, 0.5f);
                glowRect.sizeDelta = new Vector2(width * 1.35f, height * 0.95f);
                var glowImage = glow.GetComponent<Image>();
                glowImage.sprite = UiTextures.RadialGlow(96, ActiveGlow);
                glowImage.color = new Color(1f, 1f, 1f, 0.55f);
                glowImage.raycastTarget = false;
            }

            if (portrait == null)
            {
                var circle = new GameObject("Circle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Shadow));
                circle.transform.SetParent(wrapper.transform, false);
                var circleRect = (RectTransform)circle.transform;
                circleRect.anchorMin = circleRect.anchorMax = circleRect.pivot = new Vector2(0.5f, 0.5f);
                circleRect.sizeDelta = new Vector2(width * 0.75f, width * 0.75f);

                var circleShadow = circle.GetComponent<Shadow>();
                circleShadow.effectColor = new Color(0f, 0f, 0f, 0.45f);
                circleShadow.effectDistance = new Vector2(2f, -3f);

                var circleImage = circle.GetComponent<Image>();
                circleImage.sprite = UiTextures.Circle(64, color);
                circleImage.type = Image.Type.Sliced;

                var label = CreateText(circle.transform, initial.ToUpperInvariant(), Mathf.RoundToInt(36 * scale), TextAnchor.MiddleCenter);
                Stretch((RectTransform)label.transform);
                label.color = Color.white;
                label.fontStyle = FontStyle.Bold;
                return;
            }

            // Никакой подложки: персонаж вырезан по контуру и сидит прямо на сукне.
            var portraitGo = new GameObject("Portrait", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Shadow));
            portraitGo.transform.SetParent(wrapper.transform, false);

            var portraitRect = (RectTransform)portraitGo.transform;
            portraitRect.anchorMin = portraitRect.anchorMax = portraitRect.pivot = new Vector2(0.5f, 0.5f);
            portraitRect.sizeDelta = new Vector2(width, height);

            var portraitShadow = portraitGo.GetComponent<Shadow>();
            portraitShadow.effectColor = new Color(0f, 0f, 0f, 0.4f);
            portraitShadow.effectDistance = new Vector2(3f, -4f);

            var portraitImage = portraitGo.GetComponent<Image>();
            portraitImage.sprite = portrait;
            portraitImage.preserveAspect = true;
            portraitImage.raycastTarget = false;
        }

        private void RefreshControls(IReadOnlyList<DurakMove> legalMoves)
        {
            var canPass = legalMoves.Any(m => m.Type == DurakMoveType.Pass);
            var canTake = legalMoves.Any(m => m.Type == DurakMoveType.Take);

            _passButton.gameObject.SetActive(canPass);
            _takeButton.gameObject.SetActive(canTake);

            if (_game.IsFinished)
            {
                _status.color = new Color(0.94f, 0.95f, 0.93f);
                _status.text = _game.LoserIndex.HasValue
                    ? (_game.LoserIndex.Value == HumanIndex
                        ? "Ты проиграл — дурак!"
                        : "Дурак — " + _game.Players[_game.LoserIndex.Value].Name + ". Ты выиграл!")
                    : "Ничья.";
                return;
            }

            if (!_waitingForHuman)
            {
                _status.color = new Color(0.94f, 0.95f, 0.93f);
                _status.text = "Ходят соперники...";
                return;
            }

            _status.color = ActiveGlow;
            _status.text = _game.DefenderIndex == HumanIndex
                ? "Отбивайся: выбери карту или забери"
                : "Твой ход: подкинь карту";
        }

        private DurakMove? FindMoveForCard(IReadOnlyList<DurakMove> legalMoves, Card card)
        {
            DurakMove? best = null;

            foreach (var move in legalMoves)
            {
                if (move.Card != card)
                {
                    continue;
                }

                if (move.Type == DurakMoveType.Attack)
                {
                    return move;
                }

                if (move.Type == DurakMoveType.Defend && (!best.HasValue || move.TargetSlot < best.Value.TargetSlot))
                {
                    best = move;
                }
            }

            return best;
        }

        private void BuildUi()
        {
            var root = (RectTransform)transform;

            var room = new GameObject("Room", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            room.transform.SetParent(root, false);
            Stretch((RectTransform)room.transform);
            room.GetComponent<Image>().color = new Color(0.11f, 0.08f, 0.07f);

            var frame = new GameObject("Frame", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            frame.transform.SetParent(root, false);
            var frameRect = (RectTransform)frame.transform;
            frameRect.anchorMin = Vector2.zero;
            frameRect.anchorMax = Vector2.one;
            frameRect.offsetMin = new Vector2(24f, 24f);
            frameRect.offsetMax = new Vector2(-24f, -24f);
            var frameImage = frame.GetComponent<Image>();
            frameImage.sprite = UiTextures.RoundedRect(64, 64, 24f, new Color(0.36f, 0.22f, 0.12f));
            frameImage.type = Image.Type.Sliced;

            var felt = new GameObject("Felt", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            felt.transform.SetParent(root, false);
            var feltRect = (RectTransform)felt.transform;
            feltRect.anchorMin = Vector2.zero;
            feltRect.anchorMax = Vector2.one;
            feltRect.offsetMin = new Vector2(38f, 38f);
            feltRect.offsetMax = new Vector2(-38f, -38f);
            var feltImage = felt.GetComponent<Image>();
            feltImage.sprite = UiTextures.RoundedRect(64, 64, 20f, new Color(0.08f, 0.32f, 0.20f));
            feltImage.type = Image.Type.Sliced;

            CreateCenterEmblem(root, new Color(0.08f, 0.32f, 0.20f));

            _status = CreateText(root, string.Empty, 36, TextAnchor.MiddleCenter);
            Place((RectTransform)_status.transform, new Vector2(0.5f, 1f), new Vector2(0f, -60f), new Vector2(1100f, 70f));

            // Соперники рассаживаются по дуге вокруг стола (см. SeatPosition), поэтому здесь
            // только пустая область на весь экран — каждое место позиционируется само.
            _opponentsArea = new GameObject("Opponents", typeof(RectTransform)).GetComponent<RectTransform>();
            _opponentsArea.SetParent(root, false);
            Stretch(_opponentsArea);

            _tableRow = CreatePanel("Table", root, new Vector2(0.5f, 0.5f), new Vector2(0f, -60f), new Vector2(1500f, 280f));
            var tableLayout = _tableRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            tableLayout.spacing = 16f;
            tableLayout.childAlignment = TextAnchor.MiddleCenter;
            tableLayout.childControlWidth = false;
            tableLayout.childControlHeight = false;
            tableLayout.childForceExpandWidth = false;
            tableLayout.childForceExpandHeight = false;

            _handRow = CreatePanel("Hand", root, new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(1600f, 200f));

            // Колода с козырем ушла в левый нижний угол: середину краёв стола теперь занимают
            // крайние места соперников, а внизу слева пусто — рука игрока идёт по центру.
            var trumpArea = CreatePanel("Trump", root, new Vector2(0f, 0f), new Vector2(170f, 290f), new Vector2(220f, 340f));

            const float trumpScale = 0.9f;
            var trumpFrame = new GameObject("TrumpFrame", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            trumpFrame.transform.SetParent(trumpArea, false);
            var trumpFrameRect = (RectTransform)trumpFrame.transform;
            trumpFrameRect.anchorMin = trumpFrameRect.anchorMax = trumpFrameRect.pivot = new Vector2(0.5f, 0.5f);
            trumpFrameRect.anchoredPosition = new Vector2(0f, -20f);
            trumpFrameRect.sizeDelta = new Vector2(CardView.Width * trumpScale + 18f, CardView.Height * trumpScale + 18f);
            var trumpFrameImage = trumpFrame.GetComponent<Image>();
            trumpFrameImage.sprite = UiTextures.RoundedRect(64, 64, 16f, ActiveGlow);
            trumpFrameImage.type = Image.Type.Sliced;
            trumpFrameImage.raycastTarget = false;
            _trumpFrame = trumpFrameRect;

            _trumpCard = CardView.Create(trumpArea, _font, trumpScale);
            _trumpCard.Rect.anchoredPosition = new Vector2(0f, -20f);

            _deckStackArea = CreatePanel("DeckStack", trumpArea, new Vector2(0.5f, 0.5f), new Vector2(-6f, 60f), new Vector2(100f, 100f));

            _trumpInfo = CreateText(trumpArea, string.Empty, 28, TextAnchor.MiddleCenter);
            _trumpInfo.color = ActiveGlow;
            _trumpInfo.fontStyle = FontStyle.Bold;
            Place((RectTransform)_trumpInfo.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, -128f), new Vector2(220f, 44f));

            _deckInfo = CreateText(trumpArea, string.Empty, 26, TextAnchor.MiddleCenter);
            Place((RectTransform)_deckInfo.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, -178f), new Vector2(220f, 50f));

            _lastAction = CreateText(root, string.Empty, 26, TextAnchor.MiddleCenter);
            _lastAction.color = new Color(0.85f, 0.92f, 0.85f, 0.9f);
            Place((RectTransform)_lastAction.transform, new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(900f, 40f));

            _passButton = CreateButton(root, "Бито / Пас", new Vector2(1f, 0f), new Vector2(-190f, 250f),
                new Color(0.21f, 0.45f, 0.29f), () => _pendingMove = DurakMove.Pass());

            _takeButton = CreateButton(root, "Забрать", new Vector2(1f, 0f), new Vector2(-190f, 150f),
                new Color(0.55f, 0.28f, 0.18f), () => _pendingMove = DurakMove.Take());

            _restartButton = CreateButton(root, "Новая партия", new Vector2(0.5f, 0.5f), new Vector2(0f, -190f),
                new Color(0.22f, 0.32f, 0.55f), StartNewGame);

            // Выход посреди партии: без него любую проверку настроек приходилось бы доигрывать до конца.
            var exitButton = CreateButton(root, "В меню", new Vector2(0f, 1f), new Vector2(150f, -70f),
                new Color(0.3f, 0.3f, 0.32f), () =>
                {
                    StopAllCoroutines();
                    _onExit?.Invoke();
                });
            ((RectTransform)exitButton.transform).sizeDelta = new Vector2(200f, 64f);
        }

        private void CreateCenterEmblem(Transform parent, Color feltColor)
        {
            var ring = new GameObject("Emblem", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            ring.transform.SetParent(parent, false);
            var ringRect = (RectTransform)ring.transform;
            ringRect.anchorMin = ringRect.anchorMax = ringRect.pivot = new Vector2(0.5f, 0.5f);
            ringRect.anchoredPosition = new Vector2(0f, -60f);
            ringRect.sizeDelta = new Vector2(460f, 460f);
            var ringImage = ring.GetComponent<Image>();
            ringImage.sprite = UiTextures.Circle(128, Color.white);
            ringImage.type = Image.Type.Sliced;
            ringImage.color = new Color(1f, 0.93f, 0.7f, 0.08f);
            ringImage.raycastTarget = false;

            var inner = new GameObject("EmblemInner", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            inner.transform.SetParent(ring.transform, false);
            var innerRect = (RectTransform)inner.transform;
            innerRect.anchorMin = innerRect.anchorMax = innerRect.pivot = new Vector2(0.5f, 0.5f);
            innerRect.sizeDelta = new Vector2(446f, 446f);
            var innerImage = inner.GetComponent<Image>();
            innerImage.sprite = UiTextures.Circle(128, feltColor);
            innerImage.type = Image.Type.Sliced;
            innerImage.raycastTarget = false;

            var marks = CreateText(ring.transform, "♠  ♥  ♦  ♣", 76, TextAnchor.MiddleCenter);
            Stretch((RectTransform)marks.transform);
            marks.color = new Color(1f, 0.93f, 0.7f, 0.09f);
        }

        private Button CreateButton(Transform parent, string label, Vector2 anchor, Vector2 position, Color color, Action onClick)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Shadow), typeof(Button));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(280f, 76f);

            var image = go.GetComponent<Image>();
            image.sprite = UiTextures.RoundedRect(140, 76, 18f, color);
            image.type = Image.Type.Sliced;

            var shadow = go.GetComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.4f);
            shadow.effectDistance = new Vector2(3f, -4f);

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.onClick.AddListener(() => onClick());

            var text = CreateText(rect, label, 30, TextAnchor.MiddleCenter);
            Stretch((RectTransform)text.transform);

            return button;
        }

        private Text CreateText(Transform parent, string content, int fontSize, TextAnchor anchor)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);

            var text = go.GetComponent<Text>();
            text.font = _font;
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.color = new Color(0.94f, 0.95f, 0.93f);
            text.text = content;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            return text;
        }

        private static RectTransform CreatePanel(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            return rect;
        }

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void Clear(Transform parent)
        {
            for (var i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i);
                child.SetParent(null, false);
                Destroy(child.gameObject);
            }
        }
    }
}
