using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using CardGame.Core.Cards;
using CardGame.Core.Games.Chests;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>A persistent layered chest. Rules and hidden cards never enter this view.</summary>
    public sealed class ChestView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        private const float SourceSize = 1254f;
        private static readonly float[] LidKeys = { 0f, 0.22f, 0.65f, 1f };
        private static readonly string[] LidPaths = { "lid-00-closed", "lid-01-ajar", "lid-02-half", "lid-03-open" };
        // Positions use the original 1254-pixel art. The cloth starts near y=624 and
        // the front rim near y=780: packs rest down in that cavity, not on its back wall.
        // x/y = floor centre, z = projected pack width, w = rotation within the floor.
        private static readonly Vector4[] SomeSlots = {
            new Vector4(461, 749, 284, -11), new Vector4(785, 755, 282, 12)
        };
        private static readonly Vector4[] RichSlots = {
            new Vector4(415, 729, 256, -16), new Vector4(634, 721, 258, 4), new Vector4(853, 735, 250, 14),
            new Vector4(507, 778, 280, -9), new Vector4(787, 784, 276, 11)
        };

        private RectTransform _art;
        private RectTransform _effects;
        private CanvasGroup _contentsAlpha;
        private Image _hitArea;
        private Text _caption;
        private Text _ranksLabel;
        private Action _onClick;
        private readonly Image[] _lids = new Image[4];
        private readonly ChestCardStackView[] _packs = new ChestCardStackView[5];
        private ChestOutlineGraphic _outline;
        private readonly List<Rank> _ranks = new List<Rank>();
        private readonly Image[] _particles = new Image[2];
        private readonly Vector2[] _particleStarts = new Vector2[2];
        private readonly Vector2[] _particleVelocities = new Vector2[2];
        private readonly float[] _particleAges = new float[2];
        private bool _interactive = true;
        private bool _hovered;
        private bool _inspected;
        private bool _depositActive;
        private bool _built;
        private bool _needsLayout;
        private int _visibleCount;
        private int _depositVersion;
        private float _depositDeadline;
        private float _openness;
        private float _hoverAmount;

        public RectTransform Rect { get; private set; }
        public RectTransform ContentsLayer { get; private set; }
        public bool IsBusy => _depositActive;
        public Vector3 DepositWorldPosition => _art.TransformPoint(new Vector3(0f, -0.045f * _art.rect.height, 0f));

        private float DesiredOpenness => _depositActive || _inspected ? 1f : _visibleCount == 0 ? 0f : _visibleCount < 3 ? 0.22f : 0.65f;

        public static ChestView Create(Transform parent, Font font, Action onClick)
        {
            var go = new GameObject("Collection chest", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<ChestView>();
            view.Rect = (RectTransform)go.transform;
            view.Rect.anchorMin = view.Rect.anchorMax = new Vector2(0.5f, 0.5f);
            view.Rect.sizeDelta = new Vector2(190f, 190f);
            view._hitArea = go.GetComponent<Image>();
            view._hitArea.color = Color.clear;
            view._hitArea.raycastTarget = true;
            view._onClick = onClick;

            view._art = NewLayer(view.Rect, "Chest artwork");
            view._outline = ChestOutlineGraphic.Create(view._art);
            view.AddArt(view._art, "Rear", "table/chest-base-rear");
            view.ContentsLayer = NewLayer(view._art, "Contents and incoming cards");
            view._contentsAlpha = view.ContentsLayer.gameObject.AddComponent<CanvasGroup>();
            view._contentsAlpha.blocksRaycasts = false;
            view._contentsAlpha.interactable = false;
            for (var i = 0; i < view._packs.Length; i++)
            {
                view._packs[i] = ChestCardStackView.Create(view.ContentsLayer, 35f);
                view._packs[i].gameObject.SetActive(false);
            }
            view.AddArt(view._art, "Front rim", "table/chest-base-front");
            for (var i = 0; i < view._lids.Length; i++)
                view._lids[i] = view.AddArt(view._art, "Lid pose " + i, "table/lids/" + LidPaths[i]);

            view._effects = NewLayer(view._art, "Collection glints");
            for (var i = 0; i < view._particles.Length; i++)
            {
                var particle = view.AddArt(view._effects, "Glint", "fx/glint");
                particle.rectTransform.anchorMin = particle.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                particle.rectTransform.sizeDelta = Vector2.one * 18f;
                particle.gameObject.SetActive(false);
                view._particles[i] = particle;
            }

            view._caption = CreateLabel(view.Rect, font, "Collection count", 18, 0f, 22f);
            view._caption.resizeTextForBestFit = true;
            view._caption.resizeTextMinSize = 14;
            view._caption.resizeTextMaxSize = 18;
            view._caption.horizontalOverflow = HorizontalWrapMode.Wrap;
            view._caption.color = new Color(1f, 0.84f, 0.4f);
            view._ranksLabel = CreateLabel(view.Rect, font, "Completed ranks", 14, -23f, 20f);
            view._ranksLabel.color = new Color(0.9f, 0.9f, 0.86f);
            view._built = true;
            view.SetCollection(Array.Empty<Rank>(), 0);
            view.ApplyPose();
            return view;
        }

        public void SetCollection(IReadOnlyList<Rank> ranks, int visibleCount)
        {
            _visibleCount = Mathf.Clamp(visibleCount, 0, ranks == null ? 0 : ranks.Count);
            _ranks.Clear();
            for (var i = 0; i < _visibleCount; i++) _ranks.Add(ranks[i]);
            _needsLayout = true;
        }

        private void RefreshCaptions()
        {
            _caption.text = _visibleCount == 0 ? "Пусто" : _visibleCount + " " + ChestsRules.ChestsWord(_visibleCount);
            var captionSettings = _caption.GetGenerationSettings(Vector2.zero);
            captionSettings.fontSize = _caption.resizeTextMinSize;
            captionSettings.resizeTextForBestFit = false;
            captionSettings.horizontalOverflow = HorizontalWrapMode.Overflow;
            var minimumCaptionWidth = _caption.cachedTextGeneratorForLayout.GetPreferredWidth(_caption.text, captionSettings)
                                      / _caption.pixelsPerUnit + 4f;
            // On narrow seats allow a few pixels of horizontal padding rather than cutting off the noun.
            _caption.rectTransform.sizeDelta = new Vector2(Mathf.Max(0f, minimumCaptionWidth - Rect.rect.width), 22f);
            var available = Mathf.Max(0f, Rect.rect.width - 6f);
            var settings = _ranksLabel.GetGenerationSettings(Vector2.zero);
            settings.horizontalOverflow = HorizontalWrapMode.Overflow;
            var total = Mathf.Min(_visibleCount, _ranks.Count);
            // Keep the font readable: fewer tokens and +N fit crowded seats without
            // spilling over their portraits. The shared table line and inspector list all ranks.
            for (var shown = Mathf.Min(total, 6); shown >= 0; shown--)
            {
                var text = new StringBuilder();
                for (var i = 0; i < shown; i++)
                {
                    if (i > 0) text.Append(" · ");
                    text.Append(RankToken(_ranks[i]));
                }
                if (total > shown)
                {
                    if (shown > 0) text.Append(' ');
                    text.Append('+').Append(total - shown);
                }
                var candidate = text.ToString();
                var width = _ranksLabel.cachedTextGeneratorForLayout.GetPreferredWidth(candidate, settings)
                            / _ranksLabel.pixelsPerUnit;
                if (width > available && shown > 0) continue;
                _ranksLabel.text = candidate;
                break;
            }
        }

        public void SetInteractive(bool value)
        {
            _interactive = value;
            _hitArea.raycastTarget = value;
            if (!value) _hovered = false;
        }

        public void SetInspected(bool value)
        {
            _inspected = value;
        }

        public IEnumerator OpenForDeposit()
        {
            _depositActive = true;
            _depositDeadline = Time.unscaledTime + 8f;
            var version = ++_depositVersion;
            while (isActiveAndEnabled && version == _depositVersion && _openness < 0.99f) yield return null;
        }

        public IEnumerator FinishDeposit(int visibleCount)
        {
            // SetCollection supplies the public ranks before this routine; never invent ranks here.
            _visibleCount = Mathf.Clamp(visibleCount, 0, _ranks.Count);
            _depositActive = false;
            _depositVersion++;
            _needsLayout = true;
            BurstGlints();
            while (isActiveAndEnabled && Mathf.Abs(_openness - DesiredOpenness) > 0.01f) yield return null;
        }

        public void CancelDeposit()
        {
            _depositVersion++;
            _depositActive = false;
        }

        public void OnPointerEnter(PointerEventData eventData) { if (_interactive) _hovered = true; }
        public void OnPointerExit(PointerEventData eventData) => _hovered = false;
        public void OnPointerClick(PointerEventData eventData)
        {
            if (_interactive && eventData.button == PointerEventData.InputButton.Left) _onClick?.Invoke();
        }

        private void Update()
        {
            if (!_built) return;
            if (_depositActive && Time.unscaledTime >= _depositDeadline) CancelDeposit();
            var layoutChanged = _needsLayout;
            if (layoutChanged) LayoutPacks();
            var oldOpenness = _openness;
            var oldHover = _hoverAmount;
            var duration = DesiredOpenness > _openness ? 0.46f : 0.38f;
            _openness = Mathf.MoveTowards(_openness, DesiredOpenness, Time.unscaledDeltaTime / duration);
            _hoverAmount = Mathf.MoveTowards(_hoverAmount, _hovered || _inspected ? 1f : 0f, Time.unscaledDeltaTime * 8f);
            if (oldOpenness != _openness || layoutChanged) ApplyPose();
            if (oldHover != _hoverAmount)
            {
                _art.anchoredPosition = new Vector2(0f, 3f * _hoverAmount);
                _outline.SetAmount(_hoverAmount);
            }
            UpdateParticles();
        }

        private void ApplyPose()
        {
            var lower = 0;
            while (lower < LidKeys.Length - 2 && _openness > LidKeys[lower + 1]) lower++;
            var fraction = Mathf.InverseLerp(LidKeys[lower], LidKeys[lower + 1], _openness);
            fraction = Mathf.SmoothStep(0f, 1f, fraction);
            _outline.SetLid(lower, fraction);
            for (var i = 0; i < _lids.Length; i++)
            {
                var alpha = i == lower ? 1f - fraction : i == lower + 1 ? fraction : 0f;
                _lids[i].color = new Color(1f, 1f, 1f, alpha);
                _lids[i].enabled = alpha > 0.001f;
            }
            _contentsAlpha.alpha = Mathf.Clamp01(_openness / 0.1f);
            ContentsLayer.anchoredPosition = new Vector2(0f, -(1f - Mathf.Clamp01(_openness / 0.22f)) * 150f / SourceSize * Rect.rect.height);
        }

        private void LayoutPacks()
        {
            _needsLayout = false;
            RefreshCaptions();
            var rich = _visibleCount >= 3;
            var slots = rich ? RichSlots : SomeSlots;
            for (var i = 0; i < _packs.Length; i++)
            {
                var visible = i < Mathf.Min(_visibleCount, slots.Length);
                _packs[i].gameObject.SetActive(visible);
                if (!visible) continue;
                var slot = slots[i];
                var rect = _packs[i].Rect;
                var width = slot.z / SourceSize * Rect.rect.width;
                rect.sizeDelta = new Vector2(width, width * 1.5f);
                rect.anchoredPosition = new Vector2((slot.x / SourceSize - 0.5f) * Rect.rect.width, (0.5f - slot.y / SourceSize) * Rect.rect.height);
                rect.localScale = Vector3.one;
                rect.localRotation = Quaternion.identity;
                // Reuse the inspector's floor projection with a green back. The paper
                // edges and contact shadow now follow the same plane as the chest floor.
                // Front packs lie slightly atop the rear row; the wooden rim still draws
                // later and hides their lower edges, including during a deposit animation.
                var elevation = rich && i >= 3 ? 7f / SourceSize * Rect.rect.height : 0f;
                _packs[i].SetTreasurePose(slot.w, elevation);
            }
        }

        private void BurstGlints()
        {
            if (!_built) return;
            for (var i = 0; i < _particles.Length; i++)
            {
                var fraction = (i + 0.5f) / _particles.Length;
                _particleStarts[i] = new Vector2(Mathf.Lerp(-0.2f, 0.2f, fraction) * Rect.rect.width, -0.06f * Rect.rect.height);
                _particleVelocities[i] = new Vector2((fraction - 0.5f) * 22f, 13f + ((i + 8) % 3) * 6f);
                _particleAges[i] = 0f;
                _particles[i].rectTransform.anchoredPosition = _particleStarts[i];
                _particles[i].color = new Color(1f, 0.85f, 0.48f, 0f);
                _particles[i].gameObject.SetActive(true);
            }
        }

        private void UpdateParticles()
        {
            for (var i = 0; i < _particles.Length; i++)
            {
                var particle = _particles[i];
                if (!particle.gameObject.activeSelf) continue;
                _particleAges[i] += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(_particleAges[i] / 0.4f);
                if (t >= 1f) { particle.gameObject.SetActive(false); continue; }
                particle.rectTransform.anchoredPosition = _particleStarts[i] + _particleVelocities[i] * _particleAges[i];
                particle.color = new Color(1f, 0.88f, 0.62f, Mathf.Sin(t * Mathf.PI) * 0.9f);
                particle.rectTransform.localScale = Vector3.one * Mathf.Sin(t * Mathf.PI);
            }
        }

        private Image AddArt(Transform parent, string name, string path)
        {
            var rect = NewLayer(parent, name);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = ChestArt.Load(path);
            image.raycastTarget = false;
            return image;
        }

        private static RectTransform NewLayer(Transform parent, string name)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        private static Text CreateLabel(Transform parent, Font font, string name, int fontSize, float top, float height)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(0f, height);
            rect.anchoredPosition = new Vector2(0f, top);
            var text = rect.GetComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
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

        private void OnRectTransformDimensionsChange() => _needsLayout = true;
        private void OnDisable()
        {
            _depositActive = false;
            _inspected = false;
            _hovered = false;
            _depositVersion++;
        }
    }
}
