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

        private DurakGame _game;
        private SimpleDurakBot _bot;
        private Font _font;

        private RectTransform _tableRow;
        private RectTransform _handRow;
        private RectTransform _opponentsPanel;
        private RectTransform _deckStackArea;
        private RectTransform _discardStackArea;
        private CardView _trumpCard;
        private Text _status;
        private Text _deckInfo;
        private Text _discardInfo;
        private Text _log;
        private Button _passButton;
        private Button _takeButton;
        private Button _restartButton;

        private readonly List<string> _logLines = new List<string>();
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

            _logLines.Clear();
            _log.text = string.Empty;

            _trumpCard.ShowFace(_game.TrumpCard);
            _trumpCard.SetPlain();
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
                        AddLog(_game.Players[HumanIndex].Name + " " + move);
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
                        AddLog(moverName + " " + move.Value);
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

        private void AddLog(string line)
        {
            _logLines.Insert(0, line);
            if (_logLines.Count > 5)
            {
                _logLines.RemoveAt(_logLines.Count - 1);
            }

            _log.text = string.Join("\n", _logLines);
        }

        private void RefreshDeckPile()
        {
            CreateCardBackStack(_deckStackArea, _game.DeckCount, new Vector2(4f, 5f), 0.85f);
            _deckInfo.text = _game.DeckCount > 0 ? "в колоде: " + _game.DeckCount : "колода пуста";
        }

        private void RefreshDiscardPile()
        {
            CreateCardBackStack(_discardStackArea, _game.DiscardCount, new Vector2(3f, -4f), 0.85f);
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

                if (slot.Defense.HasValue)
                {
                    var defense = CardView.Create(container.transform, _font, 0.9f);
                    defense.Rect.anchoredPosition = new Vector2(16f, -18f);
                    defense.ShowFace(slot.Defense.Value);
                    defense.SetPlain();
                }
            }
        }

        private void RefreshHand(IReadOnlyList<DurakMove> legalMoves)
        {
            Clear(_handRow);

            foreach (var card in _game.Players[HumanIndex].Hand)
            {
                var view = CardView.Create(_handRow, _font);
                view.ShowFace(card);

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
            Clear(_opponentsPanel);

            foreach (var player in _game.Players)
            {
                if (player.Index == HumanIndex)
                {
                    continue;
                }

                var row = new GameObject("Opponent", typeof(RectTransform), typeof(HorizontalLayoutGroup));
                row.transform.SetParent(_opponentsPanel, false);
                ((RectTransform)row.transform).sizeDelta = new Vector2(700f, 70f);

                var layout = row.GetComponent<HorizontalLayoutGroup>();
                layout.spacing = 4f;
                layout.childAlignment = TextAnchor.MiddleLeft;
                layout.childControlWidth = false;
                layout.childControlHeight = false;
                layout.childForceExpandWidth = false;
                layout.childForceExpandHeight = false;

                var role = string.Empty;
                if (player.Index == _game.DefenderIndex)
                {
                    role = " — защищается";
                }
                else if (player.Index == _game.AttackerIndex)
                {
                    role = " — атакует";
                }

                var label = CreateText(row.transform, player.Name + role, 26, TextAnchor.MiddleLeft);
                label.GetComponent<RectTransform>().sizeDelta = new Vector2(260f, 60f);

                if (player.IsOut)
                {
                    var outText = CreateText(row.transform, "вышел", 24, TextAnchor.MiddleLeft);
                    outText.GetComponent<RectTransform>().sizeDelta = new Vector2(120f, 60f);
                    continue;
                }

                foreach (var unused in player.Hand)
                {
                    var back = CardView.Create(row.transform, _font, 0.32f);
                    back.ShowBack();
                    back.SetPlain();
                }
            }
        }

        private void RefreshControls(IReadOnlyList<DurakMove> legalMoves)
        {
            var canPass = legalMoves.Any(m => m.Type == DurakMoveType.Pass);
            var canTake = legalMoves.Any(m => m.Type == DurakMoveType.Take);

            _passButton.gameObject.SetActive(canPass);
            _takeButton.gameObject.SetActive(canTake);

            if (_game.IsFinished)
            {
                _status.text = _game.LoserIndex.HasValue
                    ? (_game.LoserIndex.Value == HumanIndex
                        ? "Ты проиграл — дурак!"
                        : "Дурак — " + _game.Players[_game.LoserIndex.Value].Name + ". Ты выиграл!")
                    : "Ничья.";
                return;
            }

            if (!_waitingForHuman)
            {
                _status.text = "Ходят соперники...";
                return;
            }

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

            var background = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            background.transform.SetParent(root, false);
            Stretch((RectTransform)background.transform);
            background.GetComponent<Image>().color = new Color(0.07f, 0.26f, 0.17f);

            _status = CreateText(root, string.Empty, 36, TextAnchor.MiddleCenter);
            Place((RectTransform)_status.transform, new Vector2(0.5f, 1f), new Vector2(0f, -60f), new Vector2(1100f, 70f));

            _opponentsPanel = CreatePanel("Opponents", root, new Vector2(0f, 1f), new Vector2(40f, -150f), new Vector2(700f, 220f));
            _opponentsPanel.pivot = new Vector2(0f, 1f);
            var opponentsLayout = _opponentsPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            opponentsLayout.spacing = 10f;
            opponentsLayout.childAlignment = TextAnchor.UpperLeft;
            opponentsLayout.childControlWidth = false;
            opponentsLayout.childControlHeight = false;
            opponentsLayout.childForceExpandWidth = false;
            opponentsLayout.childForceExpandHeight = false;

            _tableRow = CreatePanel("Table", root, new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(1500f, 240f));
            var tableLayout = _tableRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            tableLayout.spacing = 16f;
            tableLayout.childAlignment = TextAnchor.MiddleCenter;
            tableLayout.childControlWidth = false;
            tableLayout.childControlHeight = false;
            tableLayout.childForceExpandWidth = false;
            tableLayout.childForceExpandHeight = false;

            _handRow = CreatePanel("Hand", root, new Vector2(0.5f, 0f), new Vector2(0f, 140f), new Vector2(1600f, 200f));
            var handLayout = _handRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            handLayout.spacing = 14f;
            handLayout.childAlignment = TextAnchor.MiddleCenter;
            handLayout.childControlWidth = false;
            handLayout.childControlHeight = false;
            handLayout.childForceExpandWidth = false;
            handLayout.childForceExpandHeight = false;

            var trumpArea = CreatePanel("Trump", root, new Vector2(0f, 0.5f), new Vector2(150f, 0f), new Vector2(220f, 320f));
            _trumpCard = CardView.Create(trumpArea, _font, 0.9f);
            _trumpCard.Rect.anchoredPosition = new Vector2(0f, 30f);

            _deckStackArea = CreatePanel("DeckStack", trumpArea, new Vector2(0.5f, 0.5f), new Vector2(-6f, 58f), new Vector2(100f, 100f));

            _deckInfo = CreateText(trumpArea, string.Empty, 26, TextAnchor.MiddleCenter);
            Place((RectTransform)_deckInfo.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, -110f), new Vector2(220f, 50f));

            var discardArea = CreatePanel("Discard", root, new Vector2(1f, 0.5f), new Vector2(-150f, 0f), new Vector2(220f, 320f));
            _discardStackArea = CreatePanel("DiscardStack", discardArea, new Vector2(0.5f, 0.5f), new Vector2(0f, 30f), new Vector2(100f, 100f));

            _discardInfo = CreateText(discardArea, string.Empty, 26, TextAnchor.MiddleCenter);
            Place((RectTransform)_discardInfo.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, -110f), new Vector2(220f, 50f));

            var logArea = CreatePanel("Log", root, new Vector2(0f, 1f), new Vector2(40f, -390f), new Vector2(650f, 220f));
            logArea.pivot = new Vector2(0f, 1f);
            _log = CreateText(logArea, string.Empty, 24, TextAnchor.UpperLeft);
            Stretch((RectTransform)_log.transform);
            _log.color = new Color(0.85f, 0.92f, 0.85f, 0.85f);

            _passButton = CreateButton(root, "Бито / Пас", new Vector2(1f, 0f), new Vector2(-190f, 250f),
                new Color(0.21f, 0.45f, 0.29f), () => _pendingMove = DurakMove.Pass());

            _takeButton = CreateButton(root, "Забрать", new Vector2(1f, 0f), new Vector2(-190f, 150f),
                new Color(0.55f, 0.28f, 0.18f), () => _pendingMove = DurakMove.Take());

            _restartButton = CreateButton(root, "Новая партия", new Vector2(0.5f, 0.5f), new Vector2(0f, -190f),
                new Color(0.22f, 0.32f, 0.55f), StartNewGame);
        }

        private Button CreateButton(Transform parent, string label, Vector2 anchor, Vector2 position, Color color, Action onClick)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(280f, 76f);

            var image = go.GetComponent<Image>();
            image.color = color;

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
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
