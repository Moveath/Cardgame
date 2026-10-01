using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>
    /// A player's portrait and shared clothing form one miniature figure. Inside the scene only
    /// the figure sinks; the surface and clipping line stay together. The table positions the
    /// whole scene using SeatedLift. Tick is owned by the table's visual clock.
    /// </summary>
    public sealed class GovnoLifeSticker : MonoBehaviour
    {
        private const float Waterline = GovnoAvatarBody.GroundY;
        // The figure mask reaches well above the head: shivers and the new-face start stay unclipped.
        private const float MaskTop = GovnoAvatarBody.TopY + 90f;
        public const float SinkSeconds = 0.9f;
        private static readonly Color MudColor = new Color(0.27f, 0.155f, 0.06f);
        private static readonly Color BubbleColor = new Color(0.44f, 0.30f, 0.13f);
        // Stink rises behind the figure; flies circle in front of it.
        private static readonly float[] StinkX = { -50, 50, 6 };

        public RectTransform Root { get; private set; }

        // The visible figure loses height from its top as it sinks. Lifting the whole seated
        // scene by half that loss keeps its visual centre steady, including the pool and hat.
        // FullDepth includes 12 extra units to hide the hair; those are not visible height.
        public float SeatedLift => 0.5f * Mathf.Clamp(_depth, 0, Mathf.Max(0, _depths[0] - 12f));

        private RectTransform _figure, _poolBack, _poolFront, _poolFrontMask, _floatingHat;
        private CanvasGroup _mudGroup, _shadowGroup, _sweatGroup, _hatGroup;
        private RectTransform[] _sweat;
        private Image[] _bubbles;
        private RectTransform[] _stink;
        private CanvasGroup[] _stinkGroups;
        private Image[] _flies;
        private CanvasGroup _flyGroup;
        private Sprite[] _flyFrames;
        private float _flyPhase;
        private readonly Splash[] _splashes = new Splash[12];
        private readonly float[] _depths = new float[5];
        private int _lives = -1;
        private float _depth, _fromDepth, _targetDepth, _elapsed = SinkSeconds, _clock;
        private float _faceY, _faceHalfWidth, _hatHeight;
        private bool _splashPending;
        private Material _cutoutMaterial;
        private Sprite _hatSprite;
        // Whole-figure frames with a face per stage; null keeps the portrait-and-clothes figure.
        private GovnoAvatarBody.Layout _layout;
        private Image _stageImage, _faceImage;
        private int _shownStage = -1, _pendingStage = -1;
        private float _pop, _maskCenterY = 26;
        private bool _noDryShadow;

        private sealed class Splash
        {
            public Image Image;
            public Vector2 Origin, Velocity;
            public float Age, Duration, Diameter;
            public bool Active;
        }

        public static GovnoLifeSticker Create(Transform parent, Sprite portrait, Font font, Color fallbackColor)
        {
            var root = Rect(parent, "LifeSticker", Vector2.zero, new Vector2(180, 220));
            var group = root.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            var sticker = root.gameObject.AddComponent<GovnoLifeSticker>();
            sticker.Root = root;
            sticker.Build(portrait, font, fallbackColor);
            sticker.SetLives(4, false);
            return sticker;
        }

        private void Build(Sprite portrait, Font font, Color fallbackColor)
        {
            var layout = GovnoAvatarBody.Resolve(portrait);
            var cutout = Resources.Load<Shader>("Govno/Stickers/StickerCutout");
            if (cutout != null) _cutoutMaterial = new Material(cutout) { name = "GovnoStickerCutout" };
            _depths[4] = 0;
            _depths[3] = Mathf.Max(0, layout.KneeDepth);
            _depths[2] = Mathf.Max(_depths[3], layout.WaistDepth);
            _depths[1] = Mathf.Max(_depths[2], layout.NeckDepth);
            _depths[0] = Mathf.Max(_depths[1], layout.FullDepth);

            var shadow = Picture(Root, "DryGroundShadow", UiTextures.Circle(64, Color.white),
                new Vector2(0, Waterline - 1), new Vector2(92, 15), new Color(0, 0, 0, 0.22f));
            _shadowGroup = shadow.gameObject.AddComponent<CanvasGroup>();

            var mudRoot = Rect(Root, "Mud", Vector2.zero, Root.sizeDelta);
            _mudGroup = mudRoot.gameObject.AddComponent<CanvasGroup>();
            var mud = Resources.Load<Sprite>("Govno/Stickers/mud-pool");
            var mudHasArtwork = mud != null;
            var mudTint = mud == null ? MudColor : Color.white;
            // The ellipse is only a missing-resource fallback; the shipped asset supplies the artwork.
            if (mud == null) mud = UiTextures.Circle(64, Color.white);
            _poolBack = Picture(mudRoot, "MudBack", mud, new Vector2(0, Waterline),
                new Vector2(154, 38), mudTint).rectTransform;
            if (mudHasArtwork) _poolBack.GetComponent<Image>().material = _cutoutMaterial;

            var stinkSprite = Resources.Load<Sprite>("Govno/Stickers/stink");
            _stink = new RectTransform[stinkSprite == null ? 0 : StinkX.Length];
            _stinkGroups = new CanvasGroup[_stink.Length];
            for (var i = 0; i < _stink.Length; i++)
            {
                _stink[i] = Picture(mudRoot, "Stink" + i, stinkSprite, Vector2.zero, new Vector2(14, 48), Color.white).rectTransform;
                _stinkGroups[i] = _stink[i].gameObject.AddComponent<CanvasGroup>();
            }

            // A zero-sized figure origin preserves the catalogue's positions in seat coordinates.
            // A waist-up figure fades out at the bottom edge; its mask reaches far above the head
            // so the same softness never touches the hair.
            _maskCenterY = (Waterline + MaskTop) / 2;
            var waterMask = Rect(Root, "AboveMud", new Vector2(0, _maskCenterY), new Vector2(180, MaskTop - Waterline));
            var clip = waterMask.gameObject.AddComponent<RectMask2D>();
            if (layout.CroppedBelow) clip.softness = new Vector2Int(0, 14);
            _noDryShadow = layout.CroppedBelow;
            _figure = Rect(waterMask, "Figure", new Vector2(0, -_maskCenterY), Vector2.zero);
            _layout = layout;
            if (layout.StageFrames != null)
            {
                _stageImage = Picture(_figure, "StageFigure", layout.StageFrames[0], layout.StagePositions[0],
                    layout.StageSizes[0], Color.white);
                _faceHalfWidth = layout.FaceHalfWidth;
            }
            else if (layout.FullBody != null)
            {
                Picture(_figure, "FullBody", layout.FullBody, layout.FullBodyPosition,
                    layout.FullBodySize, Color.white);
                _faceHalfWidth = Mathf.Min(28, layout.FullBodySize.x * 0.24f);
            }
            else if (layout.Body != null && portrait != null)
            {
                Picture(_figure, "GeneratedBody", layout.Body, layout.BodyPosition, layout.BodySize, Color.white);
                Picture(_figure, "OriginalPortrait", portrait, layout.PortraitPosition, layout.PortraitSize, Color.white);
                if (layout.FacePatches != null)
                {
                    _faceImage = Picture(_figure, "FrightenedFace", layout.FacePatches[0], layout.FacePatchPosition,
                        layout.FacePatchSize, Color.white);
                    _faceImage.gameObject.SetActive(false);
                }
                _faceHalfWidth = layout.FaceHalfWidth;
            }
            else
            {
                if (layout.LowerBody != null)
                {
                    // Shared clothes continue below the original portrait's cut. Keep their upper
                    // shoulders/collar hidden even when a costume module is wider than this avatar.
                    var seamY = layout.PortraitPosition.y - layout.PortraitSize.y * 0.5f + 2;
                    var bottomY = Waterline - 8;
                    var clipHeight = Mathf.Max(1, seamY - bottomY);
                    var clipCenter = new Vector2(0, bottomY + clipHeight * 0.5f);
                    var lowerClip = Rect(_figure, "LowerBodyClip", clipCenter, new Vector2(180, clipHeight));
                    lowerClip.gameObject.AddComponent<RectMask2D>();
                    var lower = Picture(lowerClip, "ClothingAndLegs", layout.LowerBody, layout.LowerPosition - clipCenter,
                        layout.LowerSize, layout.BodyTint);
                    lower.material = _cutoutMaterial;
                }
                if (portrait != null)
                    Picture(_figure, "OriginalPortrait", portrait, layout.PortraitPosition,
                        layout.PortraitSize, Color.white);
                else
                {
                    var placeholder = Picture(_figure, "MissingPortrait", UiTextures.Circle(64, Color.white),
                        new Vector2(0, 55), new Vector2(60, 60), fallbackColor);
                    var label = Rect(placeholder.transform, "Initial", Vector2.zero, new Vector2(60, 60)).gameObject.AddComponent<Text>();
                    label.font = font;
                    label.text = "?";
                    label.fontSize = 30;
                    label.alignment = TextAnchor.MiddleCenter;
                    label.color = Color.white;
                    label.raycastTarget = false;
                }
                _faceHalfWidth = portrait == null ? 29 : Mathf.Min(30, layout.PortraitSize.x * 0.27f);
            }
            _faceY = portrait == null && layout.FullBody == null && layout.StageFrames == null ? 59 : layout.FaceY;

            var sweatRoot = Rect(_figure, "Sweat", Vector2.zero, Vector2.zero);
            _sweatGroup = sweatRoot.gameObject.AddComponent<CanvasGroup>();
            _sweat = new RectTransform[2];
            for (var i = 0; i < _sweat.Length; i++)
                _sweat[i] = Picture(sweatRoot, "SweatDrop" + i, UiTextures.Circle(32, Color.white),
                    Vector2.zero, new Vector2(4, 8), new Color(0.55f, 0.85f, 1f, 0.9f)).rectTransform;

            // The foreground half of the same pool hides the seam exactly at the clipping line.
            var frontRoot = Rect(Root, "MudForeground", Vector2.zero, Root.sizeDelta);
            var frontGroup = frontRoot.gameObject.AddComponent<CanvasGroup>();
            var frontMask = Rect(frontRoot, "BelowSurface", new Vector2(0, Waterline - 10), new Vector2(170, 22));
            _poolFrontMask = frontMask;
            frontMask.gameObject.AddComponent<RectMask2D>();
            _poolFront = Picture(frontMask, "MudFront", mud, new Vector2(0, 10),
                new Vector2(154, 38), mudTint).rectTransform;
            if (mudHasArtwork) _poolFront.GetComponent<Image>().material = _cutoutMaterial;
            // Foreground and background deliberately share alpha without parenting the masked figure.
            _foregroundGroup = frontGroup;

            _bubbles = new Image[5];
            for (var i = 0; i < _bubbles.Length; i++)
            {
                var bubble = Picture(frontRoot, "Bubble" + i, UiTextures.Circle(32, Color.white),
                    Vector2.zero, Vector2.one * 8, BubbleColor);
                Picture(bubble.transform, "BubbleShine", UiTextures.Circle(16, Color.white),
                    new Vector2(-1.5f, 1.5f), new Vector2(2, 2), new Color(1, 0.88f, 0.55f, 0.7f));
                bubble.gameObject.SetActive(false);
                _bubbles[i] = bubble;
            }

            if (layout.HatSprite != null)
            {
                _hatHeight = layout.HatSize.y;
                _floatingHat = Rect(Root, "FloatingHat", Vector2.zero, layout.HatSize);
                _floatingHat.localScale = Vector3.one * 0.82f;
                _hatGroup = _floatingHat.gameObject.AddComponent<CanvasGroup>();
                _hatGroup.blocksRaycasts = false;
                _hatGroup.interactable = false;
                Picture(_floatingHat, "FigureHat", layout.HatSprite, Vector2.zero, layout.HatSize, Color.white);
            }
            // A portrait crop would not match a separately drawn figure's hat.
            else if (portrait != null && layout.HatFraction > 0 && layout.StageFrames == null)
            {
                var fraction = Mathf.Clamp(layout.HatFraction, 0, 0.65f);
                var source = portrait.rect;
                var cropHeight = Mathf.Clamp(Mathf.Round(source.height * fraction), 1, source.height);
                // Sprite coordinates start at the texture's bottom; retain the selected sprite's
                // own x/y offset instead of assuming its source rectangle begins at (0, 0).
                var hatRect = new Rect(source.x, source.yMax - cropHeight, source.width, cropHeight);
                _hatSprite = Sprite.Create(portrait.texture, hatRect, new Vector2(0.5f, 0.5f),
                    portrait.pixelsPerUnit, 0, SpriteMeshType.FullRect);
                _hatSprite.name = portrait.name + "_FloatingHat";
                _hatHeight = layout.PortraitSize.y * cropHeight / source.height;
                _floatingHat = Rect(Root, "FloatingHat", Vector2.zero,
                    new Vector2(layout.PortraitSize.x, _hatHeight));
                _floatingHat.localScale = Vector3.one * 0.82f;
                _hatGroup = _floatingHat.gameObject.AddComponent<CanvasGroup>();
                _hatGroup.blocksRaycasts = false;
                _hatGroup.interactable = false;
                // Reuse the original texture without changing its pixels. A UV-only sprite crop
                // stays exact while tilted; rotating RectMask2D could expose eyes below the brim.
                Picture(_floatingHat, "PortraitHat", _hatSprite, Vector2.zero,
                    new Vector2(layout.PortraitSize.x, _hatHeight), Color.white);
            }

            for (var i = 0; i < _splashes.Length; i++)
            {
                var splash = Picture(Root, "MudSplash" + i, UiTextures.Circle(32, Color.white),
                    Vector2.zero, Vector2.one * 7, MudColor);
                splash.gameObject.SetActive(false);
                _splashes[i] = new Splash { Image = splash };
            }

            _flyFrames = new[] { Resources.Load<Sprite>("Govno/Stickers/fly-0"), Resources.Load<Sprite>("Govno/Stickers/fly-1") };
            var flyRoot = Rect(Root, "Flies", Vector2.zero, Vector2.zero);
            _flyGroup = flyRoot.gameObject.AddComponent<CanvasGroup>();
            _flies = new Image[_flyFrames[0] == null ? 0 : 3];
            for (var i = 0; i < _flies.Length; i++)
                _flies[i] = Picture(flyRoot, "Fly" + i, _flyFrames[0], Vector2.zero, new Vector2(15, 12.5f), Color.white);
            // Neighbouring seats must not buzz in sync.
            _flyPhase = Random.Range(0f, 100f);
        }

        private CanvasGroup _foregroundGroup;

        /// <summary>Supply the equipped hat's standalone sprite when customization becomes available.</summary>
        public void SetFloatingHeadwear(Sprite sprite)
        {
            if (_floatingHat != null)
            {
                _floatingHat.gameObject.SetActive(false);
                Destroy(_floatingHat.gameObject);
                _floatingHat = null;
                _hatGroup = null;
            }
            if (sprite == null) return;
            var scale = Mathf.Min(80f / sprite.rect.width, 55f / sprite.rect.height);
            var size = sprite.rect.size * scale;
            _hatHeight = size.y;
            _floatingHat = Rect(Root, "FloatingHat", Vector2.zero, size);
            _floatingHat.localScale = Vector3.one * 0.82f;
            _hatGroup = _floatingHat.gameObject.AddComponent<CanvasGroup>();
            _hatGroup.interactable = _hatGroup.blocksRaycasts = false;
            Picture(_floatingHat, "EquippedHat", sprite, Vector2.zero, size, Color.white);
            Render();
        }

        /// <summary>Repeated refreshes do not restart a loss animation; false snaps without splashes.</summary>
        public void SetLives(int lives, bool animate)
        {
            lives = Mathf.Clamp(lives, 0, 4);
            if (lives == _lives) return;
            var previous = _lives;
            _lives = lives;
            _fromDepth = _depth;
            _targetDepth = _depths[lives];
            _splashPending = animate && previous >= 0 && lives < previous;
            _elapsed = animate && previous >= 0 ? 0 : SinkSeconds;
            if (_elapsed >= SinkSeconds)
            {
                _depth = _targetDepth;
                ClearSplashes();
            }

            // The new face appears once the figure has settled: a frame cut at the new mud line
            // would otherwise hover above the surface while the figure is still going down.
            var stage = Mathf.Min(3, 4 - lives);
            if (_splashPending) _pendingStage = stage;
            else
            {
                _pendingStage = -1;
                ShowStage(stage);
            }
            Render();
        }

        private void ShowStage(int stage)
        {
            if (stage == _shownStage) return;
            _shownStage = stage;
            if (_stageImage != null)
            {
                _stageImage.sprite = _layout.StageFrames[stage];
                _stageImage.rectTransform.anchoredPosition = _layout.StagePositions[stage];
                _stageImage.rectTransform.sizeDelta = _layout.StageSizes[stage];
            }
            if (_faceImage != null)
            {
                // На суше — родное лицо; по колено — тревога; дальше — испуг.
                _faceImage.gameObject.SetActive(stage > 0);
                if (stage > 0) _faceImage.sprite = _layout.FacePatches[stage == 1 ? 0 : 1];
            }
        }

        /// <summary>Pass zero while paused. No Update or wall-clock animation runs on this object.</summary>
        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0 || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;
            _clock += deltaTime;
            if (_elapsed < SinkSeconds)
            {
                _elapsed = Mathf.Min(SinkSeconds, _elapsed + deltaTime);
                _depth = Mathf.Lerp(_fromDepth, _targetDepth, Mathf.SmoothStep(0, 1, _elapsed / SinkSeconds));
                if (_splashPending && _elapsed >= 0.14f)
                {
                    _splashPending = false;
                    StartSplashes();
                    // A face patch has no cut edge, so the fright shows with the splash itself.
                    if (_stageImage == null && _pendingStage >= 0)
                    {
                        ShowStage(_pendingStage);
                        _pendingStage = -1;
                    }
                }
            }
            if (_pendingStage >= 0 && _elapsed >= SinkSeconds)
            {
                if (_pendingStage != _shownStage) _pop = 1;
                ShowStage(_pendingStage);
                _pendingStage = -1;
            }
            _pop = Mathf.Max(0, _pop - deltaTime / 0.2f);
            TickSplashes(deltaTime);
            Render();
        }

        private void Render()
        {
            var transition = Mathf.Clamp01(_elapsed / SinkSeconds);
            var impact = _targetDepth > _fromDepth ? Mathf.Sin(transition * Mathf.PI) : 0;
            var worried = _lives == 1 || _lives == 2;
            var shiver = worried ? Mathf.Pow(Mathf.Max(0, Mathf.Sin(_clock * 1.7f)), 8) : 0;
            _figure.anchoredPosition = new Vector2(Mathf.Sin(_clock * 35) * (impact * 1.8f + shiver * 0.7f), -_maskCenterY - _depth);
            _figure.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(_clock * 22) * (impact * 1.8f + shiver * 0.6f));
            // A short start as the new face appears; the frame grows from its centre, so the cut
            // edge only moves under the surface.
            if (_stageImage != null) _stageImage.rectTransform.localScale = Vector3.one * (1 + 0.07f * _pop * _pop);

            var wetness = Mathf.Clamp01(_depth / Mathf.Max(1, _depths[3] * 0.75f));
            _mudGroup.alpha = _foregroundGroup.alpha = wetness;
            _shadowGroup.alpha = _noDryShadow ? 0 : 1 - wetness;
            var ripple = 1 + impact * 0.055f * Mathf.Sin(transition * Mathf.PI * 5);
            // The final plunge leaves a wider pool for the rest of the match.
            var poolSize = _lives == 0 ? Mathf.Lerp(1, 1.28f, Mathf.SmoothStep(0, 1, transition)) : 1;
            _poolBack.localScale = _poolFront.localScale = new Vector3(ripple * poolSize, poolSize, 1);
            _poolFrontMask.sizeDelta = new Vector2(170, 22) * poolSize;
            _poolFrontMask.anchoredPosition = new Vector2(0, Waterline - 10 * poolSize);
            _poolFront.anchoredPosition = new Vector2(0, 10 * poolSize);

            if (_floatingHat != null)
            {
                _hatGroup.alpha = _lives == 0 ? Mathf.SmoothStep(0, 1, Mathf.Clamp01((transition - 0.72f) / 0.28f)) : 0;
                _floatingHat.anchoredPosition = new Vector2(4 + Mathf.Sin(_clock * 1.3f),
                    Waterline + _hatHeight * 0.41f - 4 + Mathf.Sin(_clock * 2.1f) * 2);
                _floatingHat.localRotation = Quaternion.Euler(0, 0, 8 + Mathf.Sin(_clock * 1.7f) * 1.5f);
            }

            _sweatGroup.alpha = worried ? 1 : 0;
            for (var i = 0; i < _sweat.Length; i++)
            {
                var t = Mathf.Repeat(_clock * 0.62f + i * 0.49f, 1);
                _sweat[i].anchoredPosition = new Vector2((i == 0 ? -1 : 1) * _faceHalfWidth, _faceY - t * 14);
                _sweat[i].localScale = Vector3.one * Mathf.Sin(t * Mathf.PI);
            }

            for (var i = 0; i < _bubbles.Length; i++)
            {
                var show = wetness > 0.95f && (_lives == 0 || (impact > 0.1f && i < 3));
                _bubbles[i].gameObject.SetActive(show);
                if (!show) continue;
                var t = Mathf.Repeat(_clock * 0.51f + i * 0.217f, 1);
                var size = (5 + i % 3 * 2) * Mathf.Sin(t * Mathf.PI);
                _bubbles[i].rectTransform.sizeDelta = Vector2.one * size;
                _bubbles[i].rectTransform.anchoredPosition = new Vector2(-41 + i * 20, Waterline - 2 + t * 5);
                _bubbles[i].color = new Color(BubbleColor.r, BubbleColor.g, BubbleColor.b, 1 - t * 0.6f);
            }

            // Wavy lines rise and fade; flipping scale.x swings the wave from side to side.
            for (var i = 0; i < _stink.Length; i++)
            {
                var t = Mathf.Repeat(_clock * 0.38f + i * 0.37f, 1);
                _stink[i].anchoredPosition = new Vector2(StinkX[i] + Mathf.Sin(_clock * 1.3f + i) * 2, Waterline + 12 + t * 34);
                _stink[i].localScale = new Vector3(Mathf.Sin(_clock * 3.1f + i * 2.1f), 0.8f + t * 0.35f, 1);
                _stinkGroups[i].alpha = Mathf.Sin(t * Mathf.PI) * (_lives == 0 ? 1f : 0.85f);
            }

            _flyGroup.alpha = Mathf.Clamp01((wetness - 0.5f) * 2);
            for (var i = 0; i < _flies.Length; i++)
            {
                var show = wetness > 0.5f && (i < 2 || _lives == 0);
                _flies[i].gameObject.SetActive(show);
                if (!show) continue;
                // Lissajous loops with a nervous jitter; over a drowned player they circle the hat.
                var t = _clock + _flyPhase + i * 2.7f;
                var centerY = Waterline + (_lives == 0 ? 16 : 34);
                var radiusY = _lives == 0 ? 9 : 18;
                var x = Mathf.Sin(t * (1.9f + i * 0.35f)) * (44 - i * 6) + Mathf.Sin(t * 17) * 1.5f;
                var y = centerY + Mathf.Sin(t * (2.7f + i * 0.5f) + i) * radiusY + Mathf.Sin(t * 23 + i) * 1.5f;
                var dx = Mathf.Cos(t * (1.9f + i * 0.35f));
                var fly = _flies[i];
                fly.rectTransform.anchoredPosition = new Vector2(x, y);
                fly.rectTransform.localScale = new Vector3(dx < 0 ? -1 : 1, 1, 1);
                fly.rectTransform.localRotation = Quaternion.Euler(0, 0, (dx < 0 ? 1 : -1) * 18 * Mathf.Sin(t * 2.7f));
                fly.sprite = _flyFrames[(int)(t * 30) % 2];
            }
        }

        private void StartSplashes()
        {
            for (var i = 0; i < _splashes.Length; i++)
            {
                var splash = _splashes[i];
                var side = i % 2 == 0 ? -1 : 1;
                splash.Origin = new Vector2(side * (12 + i * 2), Waterline + 1);
                splash.Velocity = new Vector2(side * (28 + i * 3.5f), 62 + i % 4 * 17);
                splash.Age = 0;
                splash.Duration = 0.43f + i % 3 * 0.055f;
                splash.Diameter = 4 + i % 4;
                splash.Active = true;
                splash.Image.gameObject.SetActive(true);
                splash.Image.color = MudColor;
            }
        }

        private void TickSplashes(float deltaTime)
        {
            foreach (var splash in _splashes)
            {
                if (!splash.Active) continue;
                splash.Age += deltaTime;
                if (splash.Age >= splash.Duration)
                {
                    splash.Active = false;
                    splash.Image.gameObject.SetActive(false);
                    continue;
                }
                var t = splash.Age;
                var progress = t / splash.Duration;
                splash.Image.rectTransform.anchoredPosition = splash.Origin + splash.Velocity * t + Vector2.down * (190 * t * t);
                splash.Image.rectTransform.sizeDelta = new Vector2(splash.Diameter, splash.Diameter * (1.3f - progress * 0.5f));
                splash.Image.rectTransform.localRotation = Quaternion.Euler(0, 0, -splash.Velocity.x * 0.4f);
                var color = MudColor;
                color.a = 1 - Mathf.Clamp01((progress - 0.65f) / 0.35f);
                splash.Image.color = color;
            }
        }

        private void ClearSplashes()
        {
            foreach (var splash in _splashes)
            {
                splash.Active = false;
                splash.Image.gameObject.SetActive(false);
            }
        }

        private static Image Picture(Transform parent, string name, Sprite sprite, Vector2 position, Vector2 size, Color color)
        {
            var image = Rect(parent, name, position, size).gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            // Catalogue sizes already include the sprite's aspect ratio and intentional clothing fit.
            image.preserveAspect = false;
            return image;
        }

        private static RectTransform Rect(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private void OnDestroy()
        {
            if (_cutoutMaterial != null) Destroy(_cutoutMaterial);
            if (_hatSprite != null) Destroy(_hatSprite);
        }

        // Other sprites and all textures are resource assets or UiTextures' shared cache.
    }
}
