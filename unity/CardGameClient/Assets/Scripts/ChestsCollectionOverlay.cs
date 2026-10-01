using System;
using System.Collections;
using System.Collections.Generic;
using CardGame.Core.Cards;
using CardGame.Core.Games.Bluff;
using CardGame.Core.Games.Chests;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>
    /// Personal inspection of public, already revealed sets. Opening this view never
    /// pauses the match and never receives the opponents' hidden cards.
    /// </summary>
    public sealed class ChestsCollectionOverlay : MonoBehaviour, ICancelHandler
    {
        private const int MaxSets = 13;
        private const float OpenDuration = 0.32f;
        private const float CloseDuration = 0.25f;
        private static readonly Color Gold = new Color(1f, 0.84f, 0.42f);
        private static readonly Color Ivory = new Color(0.99f, 0.98f, 0.93f);
        // Sets are appended in collection order. Alternate red/black without changing
        // existing top cards when reopening, selecting a pile, or collecting another set.
        private static readonly Suit[] DisplaySuits = { Suit.Hearts, Suit.Spades, Suit.Diamonds, Suit.Clubs };
        private readonly List<Rank> _ranks = new List<Rank>();
        private readonly List<ChestCardStackView> _stacks = new List<ChestCardStackView>();
        private readonly List<Text> _rankLabels = new List<Text>();
        private readonly List<Button> _rankButtons = new List<Button>();
        private readonly List<CanvasGroup> _stackGroups = new List<CanvasGroup>();
        private readonly PilePose[] _shownPoses = new PilePose[MaxSets];
        private readonly PilePose[] _startPoses = new PilePose[MaxSets];
        private readonly PilePose[] _targetPoses = new PilePose[MaxSets];
        private readonly PilePose[] _restingPoses = new PilePose[MaxSets];
        private readonly PilePose[] _slotPoses = new PilePose[MaxSets];
        private readonly FaceQuad[] _slotFaces = new FaceQuad[MaxSets];
        private readonly int[] _pileDrawOrder = new int[MaxSets];
        private readonly System.Random _layoutRandom = new System.Random();
        private int _layoutCount = -1;
        private readonly bool[] _freshPiles = new bool[MaxSets];
        private readonly float[] _startAlphas = new float[MaxSets];
        private readonly List<Image> _particles = new List<Image>();
        private RectTransform _emptyDecoration;
        private ChestFlyGraphic _fly;
        private ChestSpider _spider;
        private float _openingFxTime = -1f;
        private bool _flyForThisOpening;
        private CanvasGroup _group;
        private CanvasGroup _contentGroup;
        private RectTransform _panel;
        private RectTransform _interior;
        private RectTransform _pileLayer;
        private RectTransform _rankBar;
        private Text _owner;
        private Text _total;
        private Text _empty;
        private Text _focusedRank;
        private Button _closeButton;
        private Font _font;
        private Action _onClosed;
        private GameObject _previousSelection;
        private Vector3 _sourceWorldPosition;
        private Vector2 _lastSize;
        private int _selectedIndex = -1;
        private bool _settling;
        private float _settleTime;
        private bool _closing;
        private float _fxTime;
        private Coroutine _transition;

        private struct PilePose
        {
            public Vector2 Position;
            public float Width;
            public float Angle;
            public float Elevation;

            public PilePose(float x, float y, float width, float angle, float elevation = 0f)
            {
                Position = new Vector2(x, y);
                Width = width;
                Angle = angle;
                Elevation = elevation;
            }
        }

        private struct FaceQuad
        {
            public Vector2 A, B, C, D;

            public bool Contains(Vector2 p) => Side(A, B, p) >= 0f && Side(B, C, p) >= 0f
                && Side(C, D, p) >= 0f && Side(D, A, p) >= 0f;

            public bool Overlaps(FaceQuad other) => !Outside(A, B, other) && !Outside(B, C, other)
                && !Outside(C, D, other) && !Outside(D, A, other)
                && !Outside(other.A, other.B, this) && !Outside(other.B, other.C, this)
                && !Outside(other.C, other.D, this) && !Outside(other.D, other.A, this);

            private static bool Outside(Vector2 a, Vector2 b, FaceQuad quad) => Side(a, b, quad.A) < 0f
                && Side(a, b, quad.B) < 0f && Side(a, b, quad.C) < 0f && Side(a, b, quad.D) < 0f;

            private static float Side(Vector2 a, Vector2 b, Vector2 p) =>
                (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
        }

        private static PilePose BasePose(int slot, int count)
        {
            if (count == 1) return new PilePose(0f, -30f, 430f, 0f);
            if (count == 2) return new PilePose(slot == 0 ? -235f : 235f, -35f, 380f, 0f);
            var rows = count <= 8 ? 2 : count <= 12 ? 3 : 4;
            var row = 0;
            var columns = 0;
            for (; row < rows; row++)
            {
                columns = count / rows + (row < count % rows ? 1 : 0);
                if (slot < columns) break;
                slot -= columns;
            }
            var halfSpan = columns <= 2 ? 235f : columns == 3 ? 330f : 365f;
            var x = columns == 1 ? 0f : Mathf.Lerp(-halfSpan, halfSpan, slot / (float)(columns - 1));
            var y = Mathf.Lerp(count <= 8 ? 70f : count <= 12 ? 120f : 145f,
                count <= 5 ? -105f : -130f, row / (float)(rows - 1));
            var width = count <= 4 ? 340f : count == 5 ? 320f : count == 6 ? 305f
                : count <= 8 ? 270f : count == 9 ? 290f : count <= 12 ? 265f : 245f;
            return new PilePose(x, y, width, 0f);
        }

        private void EnsurePileLayout(int count)
        {
            if (_layoutCount == count) return;
            _layoutCount = count;
            for (var i = 0; i < count; i++) _pileDrawOrder[i] = i;
            for (var i = count - 1; i > 0; i--)
            {
                var other = _layoutRandom.Next(i + 1);
                var value = _pileDrawOrder[i];
                _pileDrawOrder[i] = _pileDrawOrder[other];
                _pileDrawOrder[other] = value;
            }
            for (var slot = 0; slot < count; slot++)
            {
                var basis = BasePose(slot, count);
                var pose = basis;
                var placed = false;
                for (var attempt = 0; attempt < 20; attempt++)
                {
                    pose = basis;
                    pose.Position += new Vector2(Scatter(count == 1 ? 130f : count <= 5 ? 35f : 14f),
                        Scatter(count <= 2 ? 45f : count <= 5 ? 22f : 9f));
                    pose.Angle = Scatter(count <= 5 ? 18f : 14f);
                    pose.Width *= 1f + Scatter(0.025f);
                    KeepOnCloth(ref pose);
                    if (LeavesFacesVisible(pose, slot)) { placed = true; break; }
                }
                if (!placed)
                {
                    // Reset the whole arrangement to safe spacing: a fallback for just
                    // one pile could still collide with earlier randomized neighbours.
                    // Rank-to-slot order stays shuffled, even in this rare fallback.
                    for (var safeSlot = 0; safeSlot < count; safeSlot++)
                    {
                        var safe = BasePose(safeSlot, count);
                        KeepOnCloth(ref safe);
                        _slotPoses[safeSlot] = safe;
                        _slotFaces[safeSlot] = FaceBounds(safe);
                        _restingPoses[_pileDrawOrder[safeSlot]] = safe;
                    }
                    return;
                }
                _slotPoses[slot] = pose;
                _slotFaces[slot] = FaceBounds(pose);
                _restingPoses[_pileDrawOrder[slot]] = pose;
            }
        }

        private float Scatter(float reach) => ((float)_layoutRandom.NextDouble() * 2f - 1f) * reach;

        private static Vector2 FacePoint(PilePose pose, float u, float v) => pose.Position
            + ChestPileGraphic.ProjectFacePoint(pose.Width, pose.Angle, pose.Elevation, u, v);

        private static FaceQuad FaceBounds(PilePose pose) => new FaceQuad
        {
            A = FacePoint(pose, 0f, 0f), B = FacePoint(pose, 1f, 0f),
            C = FacePoint(pose, 1f, 1f), D = FacePoint(pose, 0f, 1f)
        };

        private static void KeepOnCloth(ref PilePose pose)
        {
            var quad = FaceBounds(pose);
            var min = Vector2.Min(Vector2.Min(quad.A, quad.B), Vector2.Min(quad.C, quad.D));
            var max = Vector2.Max(Vector2.Max(quad.A, quad.B), Vector2.Max(quad.C, quad.D));
            pose.Position.x += Mathf.Max(0f, -520f - min.x) - Mathf.Max(0f, max.x - 520f);
            pose.Position.y += Mathf.Max(0f, -245f - min.y) - Mathf.Max(0f, max.y - 210f);
        }

        private bool LeavesFacesVisible(PilePose candidate, int slot)
        {
            var face = FaceBounds(candidate);
            for (var previous = 0; previous < slot; previous++)
            {
                // Protect the whole upright rank/suit area, not just isolated pixels.
                var earlier = _slotPoses[previous];
                var rankCorner = new FaceQuad
                {
                    A = FacePoint(earlier, 0.04f, 0.8f), B = FacePoint(earlier, 0.22f, 0.8f),
                    C = FacePoint(earlier, 0.22f, 0.98f), D = FacePoint(earlier, 0.04f, 0.98f)
                };
                if (face.Overlaps(rankCorner)) return false;
                var visibleCentre = 0;
                for (var y = 0; y < 3; y++)
                for (var x = 0; x < 3; x++)
                {
                    var point = FacePoint(_slotPoses[previous], 0.3f + x * 0.2f, 0.4f + y * 0.15f);
                    var covered = face.Contains(point);
                    for (var above = previous + 1; above < slot && !covered; above++)
                        covered = _slotFaces[above].Contains(point);
                    if (!covered) visibleCentre++;
                }
                if (visibleCentre < 3) return false;
            }
            return true;
        }

        private void ApplyPilePoses(float t)
        {
            var ease = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);
            for (var i = 0; i < Mathf.Min(_ranks.Count, MaxSets); i++)
            {
                var start = _startPoses[i];
                var target = _targetPoses[i];
                var pose = new PilePose
                {
                    Position = Vector2.LerpUnclamped(start.Position, target.Position, ease),
                    Width = Mathf.Lerp(start.Width, target.Width, ease),
                    Angle = Mathf.Lerp(start.Angle, target.Angle, ease),
                    Elevation = Mathf.Lerp(start.Elevation, target.Elevation, ease)
                };
                _shownPoses[i] = pose;
                _stacks[i].Rect.anchoredPosition = pose.Position;
                _stacks[i].Rect.sizeDelta = new Vector2(pose.Width, pose.Width * 1.5f);
                _stacks[i].Rect.localScale = Vector3.one;
                _stacks[i].SetTreasurePose(pose.Angle, pose.Elevation);
                _stackGroups[i].alpha = Mathf.Lerp(_startAlphas[i], 1f, ease);
            }
        }

        private static string RankToken(Rank rank)
        {
            switch (rank)
            {
                case Rank.Jack: return "В";
                case Rank.Queen: return "Д";
                case Rank.King: return "К";
                case Rank.Ace: return "Т";
                default: return ((int)rank).ToString();
            }
        }

        public RectTransform Rect { get; private set; }
        public bool IsOpen { get; private set; }

        public static ChestsCollectionOverlay Create(Transform parent, Font font)
        {
            var go = new GameObject("ChestCollection", typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<ChestsCollectionOverlay>();
            view.Rect = (RectTransform)go.transform;
            Stretch(view.Rect);
            view._font = font;
            view.BuildUi();
            go.SetActive(false);
            return view;
        }

        public void Show(string ownerName, IReadOnlyList<Rank> visibleRanks, Vector3 sourceWorldPosition,
            Action onClosed)
        {
            // Replacing the same view is also safe during its opening/closing transition.
            CloseImmediate();
            _onClosed = onClosed;
            _sourceWorldPosition = sourceWorldPosition;
            _selectedIndex = -1;
            _layoutCount = -1; // One new arrangement per opening, never during idle rendering.
            _flyForThisOpening = visibleRanks.Count == 0;
            _closing = false;
            IsOpen = true;
            gameObject.SetActive(true);
            Rect.SetAsLastSibling();
            _group.alpha = 0f;
            _group.blocksRaycasts = true;
            _group.interactable = true;
            _contentGroup.interactable = false;
            _previousSelection = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(_closeButton.gameObject);
            UpdateCollection(ownerName, visibleRanks);
            RefreshCollection(false);
            Layout();
            _panel.anchoredPosition = SourcePosition();
            _panel.localScale = Vector3.one * 0.15f;
            _fxTime = -1f;
            foreach (var particle in _particles) particle.color = Color.clear;
            _transition = StartCoroutine(Open());
        }

        public void UpdateCollection(string ownerName, IReadOnlyList<Rank> visibleRanks)
        {
            _owner.text = "Сундук " + BluffRules.NameGenitive(ownerName);
            var changed = visibleRanks.Count != _ranks.Count;
            if (!changed)
            {
                for (var i = 0; i < visibleRanks.Count; i++)
                    if (_ranks[i] != visibleRanks[i]) { changed = true; break; }
            }
            if (!changed) return;
            _ranks.Clear();
            for (var i = 0; i < visibleRanks.Count; i++) _ranks.Add(visibleRanks[i]);
            if (_selectedIndex >= _ranks.Count) _selectedIndex = -1;
            RefreshCollection(IsOpen && _transition == null && !_closing);
            if (IsOpen && !_closing) _fxTime = 0f;
        }

        /// <summary>Keep the return destination attached to a chest after table relayout.</summary>
        public void UpdateSourcePosition(Vector3 sourceWorldPosition)
        {
            _sourceWorldPosition = sourceWorldPosition;
        }

        public void Close()
        {
            if (!IsOpen || _closing) return;
            _closing = true;
            StopOpeningEffects();
            _contentGroup.interactable = false;
            if (_transition != null) StopCoroutine(_transition);
            _transition = StartCoroutine(AnimateClose());
        }

        /// <summary>For table teardown/restart: releases resources without invoking callbacks.</summary>
        public void CloseImmediate()
        {
            if (_transition != null) StopCoroutine(_transition);
            _transition = null;
            _onClosed = null;
            IsOpen = false;
            _closing = false;
            _settling = false;
            StopOpeningEffects();
            if (_emptyDecoration != null) _emptyDecoration.gameObject.SetActive(false);
            if (_group != null)
            {
                _group.alpha = 0f;
                _group.blocksRaycasts = false;
                _group.interactable = false;
            }
            RestoreSelection();
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        public void OnCancel(BaseEventData eventData)
        {
            if (!IsOpen) return;
            eventData.Use();
            Close();
        }

        private void BuildUi()
        {
            _group = GetComponent<CanvasGroup>();
            // A translucent layer leaves the actual, continuously updating table visible.
            var dim = Image("Outside", Rect, null, new Color(0.01f, 0.015f, 0.02f, 0.76f));
            Stretch(dim.rectTransform);
            dim.raycastTarget = true;
            var outside = dim.gameObject.AddComponent<Button>();
            outside.transition = Selectable.Transition.None;
            outside.navigation = new Navigation { mode = Navigation.Mode.None };
            outside.onClick.AddListener(Close);

            var panelImage = Image("InteriorPanel", Rect, null, Color.clear);
            panelImage.raycastTarget = false;
            _panel = panelImage.rectTransform;
            _contentGroup = _panel.gameObject.AddComponent<CanvasGroup>();
            _owner = Text("Owner", _panel, 52, Ivory);
            _owner.fontStyle = FontStyle.Bold;
            ContrastOnWood(_owner, 2f);
            _total = Text("CollectedCount", _panel, 36, Ivory);
            ContrastOnWood(_total, 1.3f);
            _interior = NewRect("Interior", _panel);
            _interior.sizeDelta = new Vector2(1536f, 1024f);
            // Only the upper portion remains above the back rim, as when looking down inside.
            var lid = Image("VisibleOpenLid", _interior, ChestArt.InspectionLid, Color.white);
            var lidHeight = 1280f * lid.sprite.rect.height / lid.sprite.rect.width;
            Place(lid.rectTransform, new Vector2(0f, 492f), new Vector2(1280f, lidHeight));
            lid.preserveAspect = true;
            lid.raycastTarget = true;
            ChestArtworkHitArea.AddLid(lid);
            var rear = Image("WoodAndVelvet", _interior, ChestArt.Load("interior/interior-rear"), Color.white);
            Stretch(rear.rectTransform);
            rear.raycastTarget = true; // The chest absorbs clicks; empty space around it closes the view.
            ChestArtworkHitArea.AddBody(rear);
            // These fittings must straddle the seam in front of both artwork layers:
            // the original lid's hinges otherwise disappear behind the back wooden rim.
            AddInspectionHinge("HingeLeft", -395f);
            AddInspectionHinge("HingeRight", 395f);
            // Settled dust on the cloth and old corner cobwebs: prepared images, one layer.
            _emptyDecoration = NewRect("Dust and cobwebs in empty chest", _interior);
            Stretch(_emptyDecoration);
            Stretch(Image("Dust", _emptyDecoration, ChestArt.Load("fx/dust-layer"), Color.white).rectTransform);
            // Cobweb frames cover the upper corners, 232 x 220 interior-art pixels each.
            Place(Image("CobwebLeft", _emptyDecoration, ChestArt.Load("fx/cobweb-left"), Color.white).rectTransform,
                new Vector2(-420f, 179f), new Vector2(348f, 330f));
            Place(Image("CobwebRight", _emptyDecoration, ChestArt.Load("fx/cobweb-right"), Color.white).rectTransform,
                new Vector2(420f, 179f), new Vector2(348f, 330f));
            _emptyDecoration.gameObject.SetActive(false);
            // Runs on the floor below the front rim artwork, which hides its entry and exit.
            _spider = ChestSpider.Create(_interior);
            _pileLayer = NewRect("Piles resting on velvet", _interior);
            _pileLayer.sizeDelta = _interior.sizeDelta;
            for (var i = 0; i < MaxSets; i++)
            {
                var stack = ChestCardStackView.Create(_pileLayer, 330f, ChestArt.GreenBack);
                stack.SetTreasurePose(0f);
                _stacks.Add(stack);
                _stackGroups.Add(stack.gameObject.AddComponent<CanvasGroup>());
                stack.gameObject.SetActive(false);
            }
            Stretch(Image("FrontRim", _interior, ChestArt.Load("interior/interior-front"), Color.white).rectTransform);
            _rankBar = NewRect("Collected ranks", _interior);
            Place(_rankBar, new Vector2(0f, 305f), new Vector2(1060f, 34f));
            for (var i = 0; i < MaxSets; i++)
            {
                var index = i;
                var label = Text("Rank" + i, _rankBar, 29, Gold);
                ContrastOnWood(label, 1.3f);
                label.rectTransform.sizeDelta = new Vector2(58f, 34f);
                label.raycastTarget = true;
                var button = label.gameObject.AddComponent<Button>();
                button.targetGraphic = label;
                button.onClick.AddListener(() => SelectPile(index));
                var colors = button.colors;
                colors.highlightedColor = Color.white;
                colors.selectedColor = Color.white;
                button.colors = colors;
                _rankLabels.Add(label);
                _rankButtons.Add(button);
            }
            _focusedRank = Text("Inspected rank", _interior, 30, Gold);
            Place(_focusedRank.rectTransform, new Vector2(-350f, -367f), new Vector2(470f, 52f));
            _empty = Text("Empty", _interior, 50, Gold);
            _empty.text = "Пока пусто";
            Place(_empty.rectTransform, new Vector2(0f, -12f), new Vector2(700f, 68f));
            for (var i = 0; i < 8; i++)
            {
                var particle = Image("CollectionGlint" + i, _interior, ChestArt.Load("fx/glint"), Color.clear);
                particle.rectTransform.sizeDelta = Vector2.one * 28f;
                _particles.Add(particle);
            }
            var flyRect = NewRect("Fly from empty chest", _interior);
            flyRect.sizeDelta = new Vector2(52f, 44f);
            _fly = flyRect.gameObject.AddComponent<ChestFlyGraphic>();
            _fly.raycastTarget = false;
            flyRect.gameObject.SetActive(false);

            _closeButton = Button("Close", "×", _panel, new Vector2(60f, 60f), Close);
            _owner.rectTransform.SetParent(_interior, false);
            _total.rectTransform.SetParent(_interior, false);
            _owner.rectTransform.SetAsLastSibling();
            _total.rectTransform.SetAsLastSibling();
            RefreshCollection(false);
        }

        private void AddInspectionHinge(string name, float x)
        {
            var hinge = Image(name, _interior, ChestArt.InspectionHinge, Color.white);
            const float width = 150f;
            var height = width * hinge.sprite.rect.height / hinge.sprite.rect.width;
            Place(hinge.rectTransform, new Vector2(x, 473f), new Vector2(width, height));
            hinge.preserveAspect = true;
            hinge.useSpriteMesh = true;
            var shadow = hinge.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0.05f, 0.02f, 0.01f, 0.6f);
            shadow.effectDistance = new Vector2(0f, -3f);
        }

        private static void ContrastOnWood(Text text, float width)
        {
            var outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.09f, 0.035f, 0.015f, 0.96f);
            outline.effectDistance = new Vector2(width, -width);
        }

        private void RefreshCollection(bool animate)
        {
            _total.text = "Собрано: " + _ranks.Count + " " + ChestsRules.ChestsWord(_ranks.Count);
            _empty.gameObject.SetActive(_ranks.Count == 0);
            // Dust and cobwebs belong to an empty interior, not to an opening timer.
            _emptyDecoration.gameObject.SetActive(IsOpen && _ranks.Count == 0);
            if (_ranks.Count > 0) StopOpeningEffects();
            var count = Mathf.Min(MaxSets, _ranks.Count);
            EnsurePileLayout(count);
            for (var i = 0; i < MaxSets; i++)
            {
                var visible = i < count;
                _freshPiles[i] = visible && !_stacks[i].gameObject.activeSelf;
                _stacks[i].gameObject.SetActive(visible);
                _rankLabels[i].gameObject.SetActive(visible);
                if (visible)
                {
                    _startAlphas[i] = _freshPiles[i] ? 0f : _stackGroups[i].alpha;
                    _stacks[i].SetFace(_ranks[i], DisplaySuits[i % DisplaySuits.Length]);
                    _targetPoses[i] = _restingPoses[i];
                    if (i == _selectedIndex)
                    {
                        _targetPoses[i].Position += new Vector2(0f, 22f);
                        _targetPoses[i].Elevation += 22f;
                    }
                    _startPoses[i] = _shownPoses[i];
                    if (_freshPiles[i])
                    {
                        _startPoses[i] = _targetPoses[i];
                        _startPoses[i].Position += new Vector2(0f, 56f);
                        _startPoses[i].Angle -= 4f;
                    }
                    var fullNames = count <= 5;
                    _rankLabels[i].text = fullNames
                        ? ChestsRules.Capitalize(ChestsRules.RankPluralNominative(_ranks[i]))
                        : RankToken(_ranks[i]);
                    _rankLabels[i].color = i == _selectedIndex ? Color.white : Gold;
                    _rankLabels[i].fontStyle = i == _selectedIndex ? FontStyle.Bold : FontStyle.Normal;
                    _rankLabels[i].rectTransform.sizeDelta = new Vector2(fullNames ? 172f : 58f, 34f);
                    _rankLabels[i].rectTransform.anchoredPosition = new Vector2((i - (count - 1) * 0.5f) * (fullNames ? 182f : 62f), 0f);
                    _rankLabels[i].gameObject.name = "Осмотреть: " + ChestsRules.RankPluralNominative(_ranks[i]);
                    _rankButtons[i].navigation = new Navigation
                    {
                        mode = Navigation.Mode.Explicit, selectOnUp = _closeButton,
                        selectOnLeft = i > 0 ? _rankButtons[i - 1] : _closeButton,
                        selectOnRight = i + 1 < count ? _rankButtons[i + 1] : _closeButton
                    };
                }
            }
            // Shuffled ranks keep their labels/suits, while front piles cover rear piles.
            for (var slot = 0; slot < count; slot++)
                _stacks[_pileDrawOrder[slot]].Rect.SetSiblingIndex(slot);
            if (_selectedIndex >= 0) _stacks[_selectedIndex].Rect.SetAsLastSibling();
            _focusedRank.text = _selectedIndex >= 0
                ? ChestsRules.Capitalize(ChestsRules.RankPluralNominative(_ranks[_selectedIndex])) + " · 4 карты"
                : string.Empty;
            _closeButton.navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnDown = count > 0 ? _rankButtons[0] : _closeButton
            };
            _settling = animate;
            _settleTime = 0f;
            ApplyPilePoses(animate ? 0f : 1f);
        }

        private void SelectPile(int index)
        {
            if (_closing || index < 0 || index >= _ranks.Count) return;
            _selectedIndex = _selectedIndex == index ? -1 : index;
            RefreshCollection(true);
        }

        private void Layout()
        {
            Canvas.ForceUpdateCanvases();
            _lastSize = Rect.rect.size;
            var availableWidth = Mathf.Max(280f, _lastSize.x - 56f);
            var availableHeight = Mathf.Max(300f, _lastSize.y - 56f);
            const float artHeight = 1200f;
            var imageWidth = Mathf.Max(180f, Mathf.Min(1480f, availableWidth,
                availableHeight * 1536f / artHeight));
            var imageScale = imageWidth / 1536f;
            var panelWidth = imageWidth;
            var panelHeight = artHeight * imageScale;
            _panel.sizeDelta = new Vector2(panelWidth, panelHeight);
            _interior.localScale = Vector3.one * imageScale;
            var top = panelHeight * 0.5f;
            _interior.anchoredPosition = new Vector2(0f, -100f * imageScale);
            // Headings live on the back wooden wall; live table text never shows through them.
            Place(_owner.rectTransform, new Vector2(0f, 412f), new Vector2(1040f, 68f));
            _owner.resizeTextForBestFit = true;
            _owner.resizeTextMinSize = 32;
            _owner.resizeTextMaxSize = 52;
            Place(_total.rectTransform, new Vector2(0f, 352f), new Vector2(1040f, 46f));
            ((RectTransform)_closeButton.transform).anchoredPosition = new Vector2(panelWidth * 0.5f - 36f, top - 34f);
        }

        private IEnumerator Open()
        {
            var start = SourcePosition();
            for (var elapsed = 0f; elapsed < OpenDuration; elapsed += Time.unscaledDeltaTime)
            {
                var t = Mathf.Clamp01(elapsed / OpenDuration);
                var ease = 1f - Mathf.Pow(1f - t, 3f);
                _group.alpha = ease;
                _panel.anchoredPosition = Vector2.LerpUnclamped(start, Vector2.zero, ease);
                _panel.localScale = Vector3.one * Mathf.Lerp(0.15f, 1f, ease);
                yield return null;
            }
            _group.alpha = 1f;
            _panel.anchoredPosition = Vector2.zero;
            _panel.localScale = Vector3.one;
            _contentGroup.interactable = true;
            BeginOpeningEffects();
            _fxTime = 0f;
            _transition = null;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(_closeButton.gameObject);
        }

        private IEnumerator AnimateClose()
        {
            var start = _panel.anchoredPosition;
            var scale = _panel.localScale.x;
            var alpha = _group.alpha;
            for (var elapsed = 0f; elapsed < CloseDuration; elapsed += Time.unscaledDeltaTime)
            {
                var t = Mathf.SmoothStep(0f, 1f, elapsed / CloseDuration);
                _panel.anchoredPosition = Vector2.LerpUnclamped(start, SourcePosition(), t);
                _panel.localScale = Vector3.one * Mathf.Lerp(scale, 0.15f, t);
                _group.alpha = alpha * (1f - t);
                yield return null;
            }
            var callback = _onClosed;
            _transition = null;
            CloseImmediate();
            callback?.Invoke();
        }

        private Vector2 SourcePosition()
        {
            return Rect.InverseTransformPoint(_sourceWorldPosition);
        }

        private void Update()
        {
            if (!IsOpen) return;
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) Close();
            if (_lastSize != Rect.rect.size) Layout();
            if (!_closing && EventSystem.current != null)
            {
                var selected = EventSystem.current.currentSelectedGameObject;
                if (selected != null && !selected.transform.IsChildOf(Rect))
                    EventSystem.current.SetSelectedGameObject(_closeButton.gameObject);
            }
            if (_settling)
            {
                _settleTime += Time.unscaledDeltaTime;
                ApplyPilePoses(Mathf.Clamp01(_settleTime / 0.42f));
                if (_settleTime >= 0.42f) _settling = false;
            }
            AnimateParticles();
            AnimateOpeningEffects();
        }

        private void BeginOpeningEffects()
        {
            if (!_flyForThisOpening || _ranks.Count > 0) { StopOpeningEffects(); return; }
            _openingFxTime = 0f;
            _fly.color = Color.clear;
            _fly.SetWingFrame(0);
            _fly.gameObject.SetActive(_flyForThisOpening);
            _spider.Play();
        }

        private void StopOpeningEffects()
        {
            _openingFxTime = -1f;
            if (_fly != null) _fly.gameObject.SetActive(false);
            if (_spider != null) _spider.Stop();
        }

        private void AnimateOpeningEffects()
        {
            if (_openingFxTime < 0f) return;
            _openingFxTime += Time.unscaledDeltaTime;
            RenderOpeningEffects();
        }

        private void RenderOpeningEffects()
        {
            if (_openingFxTime < 0f) return;

            const float flyDelay = 0.18f;
            const float flyDuration = 2.3f;
            if (_flyForThisOpening && _fly.gameObject.activeSelf)
            {
                var t = Mathf.Clamp01((_openingFxTime - flyDelay) / flyDuration);
                if (t >= 1f) _fly.gameObject.SetActive(false);
                else
                {
                    var position = FlyPosition(t);
                    var tangent = FlyPosition(Mathf.Min(1f, t + 0.005f)) - position;
                    _fly.rectTransform.anchoredPosition = position;
                    _fly.rectTransform.localRotation = Quaternion.Euler(0f, 0f,
                        Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg);
                    _fly.SetWingFrame(Mathf.FloorToInt(Mathf.Max(0f, _openingFxTime - flyDelay) * 24f) % 2);
                    _fly.color = new Color(1f, 1f, 1f,
                        Mathf.SmoothStep(0f, 1f, t / 0.08f)
                        * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.85f, 1f, t))));
                }
            }
            // The fly is done; the spider finishes its own route and hides itself.
            if (_openingFxTime >= (_flyForThisOpening ? flyDelay + flyDuration : 1f))
            {
                _openingFxTime = -1f;
                _fly.gameObject.SetActive(false);
            }
        }

        private static Vector2 FlyPosition(float t)
        {
            var first = t < 0.5f;
            var u = first ? t * 2f : (t - 0.5f) * 2f;
            var a = first ? new Vector2(-150f, -130f) : new Vector2(-50f, 170f);
            var b = first ? new Vector2(-360f, 20f) : new Vector2(160f, 110f);
            var c = first ? new Vector2(-260f, 230f) : new Vector2(310f, 350f);
            var d = first ? new Vector2(-50f, 170f) : new Vector2(630f, 500f);
            var v = 1f - u;
            var position = v * v * v * a + 3f * v * v * u * b + 3f * v * u * u * c + u * u * u * d;
            position.y += Mathf.Sin(t * Mathf.PI * 12f) * Mathf.Sin(t * Mathf.PI) * 7f;
            return position;
        }

        private void AnimateParticles()
        {
            if (_fxTime < 0f || _fxTime > 1.05f) return;
            _fxTime += Time.unscaledDeltaTime;
            if (_ranks.Count == 0)
            {
                foreach (var particle in _particles) particle.color = Color.clear;
                _fxTime = -1f;
                return;
            }
            for (var i = 0; i < _particles.Count; i++)
            {
                var particle = _particles[i];
                var t = Mathf.Clamp01((_fxTime - i * 0.025f) / 0.8f);
                var pose = _shownPoses[i % Mathf.Min(_ranks.Count, MaxSets)];
                var x = pose.Position.x + (i % 2 == 0 ? -0.24f : 0.28f) * pose.Width;
                var y = pose.Position.y + pose.Elevation + (i % 2 == 0 ? 0.19f : -0.2f) * pose.Width;
                particle.rectTransform.anchoredPosition = new Vector2(x, y);
                particle.rectTransform.sizeDelta = Vector2.one * 38f;
                particle.color = new Color(1f, 0.86f, 0.5f, Mathf.Sin(t * Mathf.PI) * 0.48f);
            }
        }

        private void RestoreSelection()
        {
            if (EventSystem.current != null && _previousSelection != null && _previousSelection.activeInHierarchy)
                EventSystem.current.SetSelectedGameObject(_previousSelection);
            _previousSelection = null;
        }

        private void OnDisable()
        {
            StopOpeningEffects();
            if (_emptyDecoration != null) _emptyDecoration.gameObject.SetActive(false);
            StopAllCoroutines();
            _transition = null;
            IsOpen = false;
            _onClosed = null;
            _settling = false;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            return rect;
        }

        private static Image Image(string name, Transform parent, Sprite sprite, Color color)
        {
            var rect = NewRect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private Text Text(string name, Transform parent, int size, Color color)
        {
            var rect = NewRect(name, parent);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = _font;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private Button Button(string name, string title, Transform parent, Vector2 size, Action click)
        {
            var image = Image(name, parent, UiTextures.RoundedRect(64, 64, 12f, Color.white), new Color(0.2f, 0.3f, 0.33f));
            image.type = UnityEngine.UI.Image.Type.Sliced;
            image.raycastTarget = true;
            image.rectTransform.sizeDelta = size;
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => click());
            var colors = button.colors;
            colors.highlightedColor = new Color(1f, 0.88f, 0.6f);
            colors.selectedColor = colors.highlightedColor;
            colors.pressedColor = new Color(0.76f, 0.65f, 0.4f);
            colors.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.35f);
            button.colors = colors;
            var label = Text("Label", image.transform, 38, Gold);
            label.text = title;
            Stretch(label.rectTransform);
            return button;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static void Place(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }
}
