using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>
    /// Фигура игрока за столом: портрет без подложки, подсветка ходящего и запасной кружок
    /// с буквой, если картинок нет. Общая для всех игр — размеры и масштаб портрета тут
    /// настраивались долго, и держать их в двух местах значит чинить каждую правку дважды.
    /// </summary>
    public static class SeatFigure
    {
        public const float Width = 136f;
        public const float Height = 158f;
        public const float NameHeight = 30f;
        public const float NameGap = 12f;

        /// <summary>Фигура плюс подпись под ней. Веер карт лежит сбоку и высоту места не ест.</summary>
        public const float BlockHeight = Height + NameGap + NameHeight;

        /// <summary>
        /// Портреты нарезаны с одного листа, поэтому персонаж на всех примерно одной высоты
        /// в пикселях картинки. От неё и считаем масштаб — общий для всех мест. Подгонять
        /// каждый портрет под рамку по отдельности нельзя: широкий персонаж тогда ужимается
        /// и выглядит мельче остальных, а голова у всех должна быть одного размера.
        /// </summary>
        public const float ReferencePortraitHeight = 290f;

        /// <summary>Шляпе волшебника и короне разрешено торчать выше рамки, но немного.</summary>
        public const float MaxPortraitStretch = 1.05f;

        /// <summary>Самый широкий персонаж шире рамки примерно на четверть — это учитывают промежутки между местами.</summary>
        public const float PortraitWidthAllowance = 1.25f;

        private static readonly Color Glow = new Color(1f, 0.85f, 0.3f);

        /// <summary>
        /// Высота глаз, если на портрете их не нашли (маска Зорро закрывает кожу вокруг
        /// глаз) или портрета нет вовсе. Середина того, что находится на остальных.
        /// </summary>
        public const float FallbackEyeLine = 104f;

        private static float PortraitScale(Sprite portrait, float scale) => Mathf.Min(
            Height * scale / ReferencePortraitHeight,
            Height * scale * MaxPortraitStretch / portrait.rect.height);

        /// <summary>
        /// Как надевается головной убор, в пикселях стола при масштабе 1 и обычном размере
        /// портрета: где край над глазами, насколько срез костюма выше края над лицом,
        /// полуширина убора и насколько ниже края срез по бокам.
        /// </summary>
        public sealed class HeadwearFit
        {
            public HeadwearFit(float rimAboveEyes, float crownAboveRim, float halfWidth, float sideBelowRim)
            {
                RimAboveEyes = rimAboveEyes;
                CrownAboveRim = crownAboveRim;
                HalfWidth = halfWidth;
                SideBelowRim = sideBelowRim;
            }

            public float RimAboveEyes { get; }
            public float CrownAboveRim { get; }
            public float HalfWidth { get; }
            public float SideBelowRim { get; }
        }

        /// <summary>Портрет, на который надели убор, и где этот убор сидит.</summary>
        public sealed class Headwear
        {
            /// <summary>Портрет без своей шляпы: всё, что под убором и вокруг него, стёрто.</summary>
            public Sprite Portrait { get; set; }

            /// <summary>Высота переднего края над низом фигуры, при масштабе 1.</summary>
            public float Rim { get; set; }

            /// <summary>Середина лица от центра фигуры: головы стоят не строго посередине.</summary>
            public float CenterX { get; set; }

            /// <summary>
            /// Во сколько раз этот портрет мельче обычного. Слишком высокие картинки
            /// ужимаются, чтобы влезть в рамку, и убор ужимается вместе с головой.
            /// </summary>
            public float Size { get; set; } = 1f;
        }

        private static readonly Dictionary<(Sprite, HeadwearFit), Headwear> HeadwearCache =
            new Dictionary<(Sprite, HeadwearFit), Headwear>();

        /// <summary>
        /// Надевает убор по-настоящему, а не кладёт сверху. Садится он по глазам самого
        /// портрета: нейросеть кадрирует каждый лист по-своему, и лица за столом стоят на
        /// разной высоте, от 82 до 124. А собственная шляпа костюма стирается: над лицом —
        /// сразу над краем убора, по бокам — ниже, до низа ушей, иначе из-под него торчали
        /// бы поля ковбойской шляпы и колпака. Считается один раз на портрет.
        /// </summary>
        public static Headwear PutOnHead(Sprite portrait, HeadwearFit fit)
        {
            if (portrait == null)
            {
                // Кружок с буквой: убор просто садится на него сверху.
                return new Headwear { Rim = FallbackEyeLine + fit.RimAboveEyes };
            }

            if (HeadwearCache.TryGetValue((portrait, fit), out var cached))
            {
                return cached;
            }

            var pixels = ReadPixels(portrait, out var width, out var height);
            var scale = PortraitScale(portrait, 1f);
            var size = scale / (Height / ReferencePortraitHeight);

            if (!PortraitFace.TryFindEyes(pixels, width, height, scale, out var eyes, out var centerX, out var bothEyes))
            {
                eyes = FallbackEyeLine;
                centerX = 0f;
            }
            else if (!bothEyes)
            {
                // Один глаз под повязкой: высота верная, а середина лица — нет.
                centerX = 0f;
            }

            var rim = eyes + fit.RimAboveEyes * size;
            PortraitFace.CutAbove(pixels, width, height, scale, centerX,
                rim + fit.CrownAboveRim * size, fit.HalfWidth * size, rim - fit.SideBelowRim * size,
                3f * size, 3f);

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            texture.SetPixelData(pixels, 0);
            texture.Apply(true, true);

            var result = new Headwear
            {
                Portrait = Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0f),
                    portrait.pixelsPerUnit, 0, SpriteMeshType.FullRect),
                Rim = rim,
                CenterX = centerX,
                Size = size,
            };

            HeadwearCache[(portrait, fit)] = result;
            return result;
        }

        /// <summary>
        /// Пиксели портрета, строками снизу вверх. Текстуры аватаров импортируются без
        /// чтения с процессора — так они вдвое легче в памяти, — поэтому копию снимаем
        /// через видеокарту: рисуем в свою текстуру и читаем уже её.
        /// </summary>
        private static byte[] ReadPixels(Sprite sprite, out int width, out int height)
        {
            var rect = sprite.textureRect;
            width = Mathf.RoundToInt(rect.width);
            height = Mathf.RoundToInt(rect.height);

            var source = sprite.texture;
            var target = RenderTexture.GetTemporary(source.width, source.height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            var previous = RenderTexture.active;

            Graphics.Blit(source, target);
            RenderTexture.active = target;

            var copy = new Texture2D(width, height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(rect.x, rect.y, width, height), 0, 0, false);
            copy.Apply(false);

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);

            var pixels = copy.GetRawTextureData();
            Object.Destroy(copy);
            return pixels;
        }

        public static RectTransform Create(Transform parent, Font font, string initial, Color color,
            bool isActing, Sprite portrait, float scale)
        {
            var width = Width * scale;
            var height = Height * scale;

            var wrapper = new GameObject("Avatar", typeof(RectTransform));
            wrapper.transform.SetParent(parent, false);
            var wrapperRect = (RectTransform)wrapper.transform;
            wrapperRect.sizeDelta = new Vector2(width, height);

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
                glowImage.sprite = UiTextures.RadialGlow(96, Glow);
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

                var label = CreateLabel(circle.transform, font, initial.ToUpperInvariant(),
                    Mathf.RoundToInt(36 * scale));
                label.color = Color.white;
                label.fontStyle = FontStyle.Bold;
                return wrapperRect;
            }

            // Никакой подложки: персонаж вырезан по контуру и сидит прямо на сукне.
            var portraitGo = new GameObject("Portrait", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Shadow));
            portraitGo.transform.SetParent(wrapper.transform, false);

            // Размер от масштаба, общего для всех мест, а не подгонкой под рамку. Сажаем на
            // нижний край: тогда у всех совпадает линия плеч, а шляпа уходит вверх, а не
            // выталкивает фигуру вниз на подпись с именем.
            var portraitScale = PortraitScale(portrait, scale);

            var portraitRect = (RectTransform)portraitGo.transform;
            portraitRect.anchorMin = portraitRect.anchorMax = new Vector2(0.5f, 0f);
            portraitRect.pivot = new Vector2(0.5f, 0f);
            portraitRect.anchoredPosition = Vector2.zero;
            portraitRect.sizeDelta = new Vector2(portrait.rect.width * portraitScale, portrait.rect.height * portraitScale);

            var portraitShadow = portraitGo.GetComponent<Shadow>();
            portraitShadow.effectColor = new Color(0f, 0f, 0f, 0.4f);
            portraitShadow.effectDistance = new Vector2(3f, -4f);

            var portraitImage = portraitGo.GetComponent<Image>();
            portraitImage.sprite = portrait;
            portraitImage.raycastTarget = false;

            return wrapperRect;
        }

        private static Text CreateLabel(Transform parent, Font font, string content, int fontSize)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.text = content;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            return text;
        }
    }
}
