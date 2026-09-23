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
        /// Высота бровей от низа фигуры. Портреты выровнены по линии плеч, поэтому лица у
        /// всех образов на одной высоте: глаза в полосе 104–120, подбородок около 90. Всё,
        /// что надевается на голову, считается отсюда. Верх арта для этого не годится —
        /// у костюмов со шляпой это верх шляпы, и предмет уезжал бы на её макушку.
        /// </summary>
        public const float BrowLine = 122f;

        private static float PortraitScale(Sprite portrait, float scale) => Mathf.Min(
            Height * scale / ReferencePortraitHeight,
            Height * scale * MaxPortraitStretch / portrait.rect.height);

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
