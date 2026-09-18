using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
        private static readonly string[] PlayerNames = { "Ты", "Борис", "Вера" };
        private static readonly Color[] OpponentColors = { new Color(0.30f, 0.52f, 0.86f), new Color(0.82f, 0.42f, 0.62f) };
        private static readonly Color ActiveGlow = new Color(1f, 0.85f, 0.3f);

        private DurakGame _game;
        private SimpleDurakBot _bot;
        private Font _font;

        private RectTransform _tableRow;
        private RectTransform _handRow;
        private RectTransform[] _opponentSlots;
        private RectTransform _deckStackArea;
        private RectTransform _discardStackArea;
        private CardView _trumpCard;
        private RectTransform _trumpFrame;
        private Text _status;
        private Text _trumpInfo;
        private Text _deckInfo;
        private Text _discardInfo;
        private Text _lastAction;
        private Button _passButton;
        private Button _takeButton;
        private Button _restartButton;

        private DurakMove? _pendingMove;
        private bool _waitingForHuman;

        private void Start()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _bot = new SimpleDurakBot();

            BuildUi();
            StartNewGame();
        }

        private void StartNewGame()
        {
            _game = DurakGame.Create(PlayerNames, UnityEngine.Random.Range(0, int.MaxValue));
            _pendingMove = null;
            _waitingForHuman = false;

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
                        SetLastAction(_game.Players[HumanIndex].Name + ": " + move);
                    }
                    else
                    {
                        Debug.LogWarning("Ход отклонён: " + error);
                    }

                    Refresh();
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
                        SetLastAction(moverName + ": " + move.Value);
                    }

                    Refresh();
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
            RefreshDiscardPile();
        }

        private void SetLastAction(string line)
        {
            _lastAction.text = line;
        }

        private void RefreshDeckPile()
        {
            CreateCardBackStack(_deckStackArea, _game.DeckCount, new Vector2(5f, 6f), 1.05f);
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

        private void RefreshDiscardPile()
        {
            CreateCardBackStack(_discardStackArea, _game.DiscardCount, new Vector2(4f, -5f), 1.05f);
            _discardInfo.text = _game.DiscardCount > 0 ? "бита: " + _game.DiscardCount : "бита пуста";
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
            Clear(_tableRow);

            foreach (var slot in _game.Table)
            {
                var container = new GameObject("Slot", typeof(RectTransform));
                container.transform.SetParent(_tableRow, false);
                ((RectTransform)container.transform).sizeDelta = new Vector2(160f, 230f);

                var attack = CardView.Create(container.transform, _font, 0.9f);
                attack.Rect.anchoredPosition = new Vector2(-14f, 16f);
                attack.ShowFace(slot.Attack);
                attack.SetPlain();
                attack.PlayAppearAnimation();

                if (slot.Defense.HasValue)
                {
                    var defense = CardView.Create(container.transform, _font, 0.9f);
                    defense.Rect.anchoredPosition = new Vector2(16f, -18f);
                    defense.ShowFace(slot.Defense.Value);
                    defense.SetPlain();
                    defense.PlayAppearAnimation();
                }
            }
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
            var spacing = Mathf.Min(cardWidth * 0.82f, 1500f / count);
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
            var actingIndex = _waitingForHuman ? (int?)HumanIndex : DurakTurnOrder.GetActingPlayer(_game);
            var slotIndex = 0;

            foreach (var player in _game.Players)
            {
                if (player.Index == HumanIndex || slotIndex >= _opponentSlots.Length)
                {
                    continue;
                }

                var container = _opponentSlots[slotIndex];
                var colorIndex = slotIndex;
                slotIndex++;
                Clear(container);

                var isActing = actingIndex.HasValue && actingIndex.Value == player.Index;

                var header = new GameObject("Header", typeof(RectTransform), typeof(HorizontalLayoutGroup));
                header.transform.SetParent(container, false);
                ((RectTransform)header.transform).sizeDelta = new Vector2(340f, 64f);

                var headerLayout = header.GetComponent<HorizontalLayoutGroup>();
                headerLayout.spacing = 10f;
                headerLayout.childAlignment = TextAnchor.MiddleLeft;
                headerLayout.childControlWidth = false;
                headerLayout.childControlHeight = false;
                headerLayout.childForceExpandWidth = false;
                headerLayout.childForceExpandHeight = false;

                CreateAvatar(header.transform, player.Name.Substring(0, 1), OpponentColors[colorIndex % OpponentColors.Length], isActing);

                var role = string.Empty;
                if (player.Index == _game.DefenderIndex)
                {
                    role = " — защищается";
                }
                else if (player.Index == _game.AttackerIndex)
                {
                    role = " — атакует";
                }

                var label = CreateText(header.transform, player.Name + role, 26, TextAnchor.MiddleLeft);
                label.color = isActing ? ActiveGlow : new Color(0.94f, 0.95f, 0.93f);
                label.GetComponent<RectTransform>().sizeDelta = new Vector2(260f, 60f);

                if (player.IsOut)
                {
                    var outText = CreateText(container, "вышел", 22, TextAnchor.UpperLeft);
                    outText.GetComponent<RectTransform>().sizeDelta = new Vector2(160f, 30f);
                    continue;
                }

                var row = new GameObject("Cards", typeof(RectTransform));
                row.transform.SetParent(container, false);
                ((RectTransform)row.transform).sizeDelta = new Vector2(380f, 110f);

                var oppCount = player.Hand.Count;
                if (oppCount > 0)
                {
                    const float oppScale = 0.6f;
                    var oppWidth = CardView.Width * oppScale;
                    var oppAngleStep = Mathf.Min(6f, 24f / Mathf.Max(oppCount - 1, 1));
                    var oppSpacing = Mathf.Min(oppWidth * 0.55f, 360f / oppCount);
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

        private void CreateAvatar(Transform parent, string initial, Color color, bool isActing)
        {
            const float ringSize = 58f;
            const float avatarSize = 48f;

            var wrapper = new GameObject("Avatar", typeof(RectTransform));
            wrapper.transform.SetParent(parent, false);
            ((RectTransform)wrapper.transform).sizeDelta = new Vector2(ringSize, ringSize);

            var ring = new GameObject("Ring", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            ring.transform.SetParent(wrapper.transform, false);
            var ringRect = (RectTransform)ring.transform;
            ringRect.anchorMin = ringRect.anchorMax = ringRect.pivot = new Vector2(0.5f, 0.5f);
            ringRect.sizeDelta = new Vector2(ringSize, ringSize);
            var ringImage = ring.GetComponent<Image>();
            ringImage.sprite = UiTextures.Circle(64, Color.white);
            ringImage.type = Image.Type.Sliced;
            ringImage.color = isActing ? ActiveGlow : new Color(0f, 0f, 0f, 0f);

            var circle = new GameObject("Circle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Shadow));
            circle.transform.SetParent(wrapper.transform, false);
            var circleRect = (RectTransform)circle.transform;
            circleRect.anchorMin = circleRect.anchorMax = circleRect.pivot = new Vector2(0.5f, 0.5f);
            circleRect.sizeDelta = new Vector2(avatarSize, avatarSize);

            var circleShadow = circle.GetComponent<Shadow>();
            circleShadow.effectColor = new Color(0f, 0f, 0f, 0.45f);
            circleShadow.effectDistance = new Vector2(2f, -3f);

            var circleImage = circle.GetComponent<Image>();
            circleImage.sprite = UiTextures.Circle(64, color);
            circleImage.type = Image.Type.Sliced;

            var label = CreateText(circle.transform, initial.ToUpperInvariant(), 24, TextAnchor.MiddleCenter);
            Stretch((RectTransform)label.transform);
            label.color = Color.white;
            label.fontStyle = FontStyle.Bold;
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

            _opponentSlots = new RectTransform[2];

            _opponentSlots[0] = CreatePanel("OpponentLeft", root, new Vector2(0f, 1f), new Vector2(40f, -140f), new Vector2(360f, 150f));
            _opponentSlots[0].pivot = new Vector2(0f, 1f);

            _opponentSlots[1] = CreatePanel("OpponentRight", root, new Vector2(1f, 1f), new Vector2(-40f, -140f), new Vector2(360f, 150f));
            _opponentSlots[1].pivot = new Vector2(1f, 1f);

            foreach (var slot in _opponentSlots)
            {
                var layout = slot.gameObject.AddComponent<VerticalLayoutGroup>();
                layout.spacing = 8f;
                layout.childAlignment = TextAnchor.UpperLeft;
                layout.childControlWidth = false;
                layout.childControlHeight = false;
                layout.childForceExpandWidth = false;
                layout.childForceExpandHeight = false;
            }

            _tableRow = CreatePanel("Table", root, new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(1500f, 240f));
            var tableLayout = _tableRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            tableLayout.spacing = 16f;
            tableLayout.childAlignment = TextAnchor.MiddleCenter;
            tableLayout.childControlWidth = false;
            tableLayout.childControlHeight = false;
            tableLayout.childForceExpandWidth = false;
            tableLayout.childForceExpandHeight = false;

            _handRow = CreatePanel("Hand", root, new Vector2(0.5f, 0f), new Vector2(0f, 195f), new Vector2(1600f, 200f));

            var trumpArea = CreatePanel("Trump", root, new Vector2(0f, 0.5f), new Vector2(150f, -20f), new Vector2(220f, 420f));

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

            var discardArea = CreatePanel("Discard", root, new Vector2(1f, 0.5f), new Vector2(-150f, -20f), new Vector2(220f, 320f));
            _discardStackArea = CreatePanel("DiscardStack", discardArea, new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(100f, 100f));

            _discardInfo = CreateText(discardArea, string.Empty, 26, TextAnchor.MiddleCenter);
            Place((RectTransform)_discardInfo.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, -110f), new Vector2(220f, 50f));

            _lastAction = CreateText(root, string.Empty, 26, TextAnchor.MiddleCenter);
            _lastAction.color = new Color(0.85f, 0.92f, 0.85f, 0.9f);
            Place((RectTransform)_lastAction.transform, new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(900f, 40f));

            _passButton = CreateButton(root, "Бито / Пас", new Vector2(1f, 0f), new Vector2(-190f, 250f),
                new Color(0.21f, 0.45f, 0.29f), () => _pendingMove = DurakMove.Pass());

            _takeButton = CreateButton(root, "Забрать", new Vector2(1f, 0f), new Vector2(-190f, 150f),
                new Color(0.55f, 0.28f, 0.18f), () => _pendingMove = DurakMove.Take());

            _restartButton = CreateButton(root, "Новая партия", new Vector2(0.5f, 0.5f), new Vector2(0f, -190f),
                new Color(0.22f, 0.32f, 0.55f), StartNewGame);
        }

        private void CreateCenterEmblem(Transform parent, Color feltColor)
        {
            var ring = new GameObject("Emblem", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            ring.transform.SetParent(parent, false);
            var ringRect = (RectTransform)ring.transform;
            ringRect.anchorMin = ringRect.anchorMax = ringRect.pivot = new Vector2(0.5f, 0.5f);
            ringRect.anchoredPosition = new Vector2(0f, 20f);
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
