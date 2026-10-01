using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>
    /// Вывески Card City — названия мест по-русски. Где в картинке нарисована пустая табличка или
    /// щит с лампочками, текст ложится прямо на неё; где таблички нет — своя плашка у входа: тёмная
    /// доска в золотой рамке, как нарисованные (`UI/Hub/sign-plaque`). Большие щиты — шрифтом
    /// Playfair Display SC, маленькие таблички и плашки — узким Oswald, чтобы и они читались
    /// на телефоне. Вывески — дети куска города или этажа: едут, масштабируются и приближаются
    /// в нырке вместе с картинкой. Нажимать на места — следующий шаг, пока только названия.
    /// </summary>
    public sealed partial class PlayerHubController
    {
        private enum SignLook
        {
            // Кремовый щит с лампочками — тёмно-красные буквы.
            Marquee,
            // Нарисованная тёмная табличка — золотые буквы.
            Board,
            // Своя плашка — там, где таблички в картинке нет.
            Plaque,
        }

        // Координаты в общей системе района 1672×941, Y вниз. У нарисованных вывесок заданы
        // углы безопасного поля букв с отступами от рамки. Rise — подъём середины дуги.
        private const float SignPictureWidth = 1672, SignPictureHeight = 941;
        private sealed class SignDefinition
        {
            public string Art, Text;
            public float X, Y, Width, Height;
            public SignLook Look;
            public bool Grand, Surface;
            public Vector2 TopLeft, TopRight, BottomLeft, BottomRight;
            public float TopRise, BottomRise;

            public static SignDefinition Plaque(string art, string text, float x, float y, float height)
                => new SignDefinition { Art = art, Text = text, X = x, Y = y, Height = height, Look = SignLook.Plaque };

            public static SignDefinition Painted(string art, string text, SignLook look, bool grand,
                Vector2 topLeft, Vector2 topRight, Vector2 bottomLeft, Vector2 bottomRight,
                float topRise = 0, float bottomRise = 0)
            {
                var min = Vector2.Min(Vector2.Min(topLeft, topRight), Vector2.Min(bottomLeft, bottomRight));
                var max = Vector2.Max(Vector2.Max(topLeft, topRight), Vector2.Max(bottomLeft, bottomRight));
                // У наклонной дуги вершина не посередине: учитываем её, чтобы рамка Text
                // действительно включала всю надпись при отсечении на краю экрана.
                IncludeArcExtreme(topLeft, topRight, topRise, ref min, ref max);
                IncludeArcExtreme(bottomLeft, bottomRight, bottomRise, ref min, ref max);
                var center = (min + max) / 2;
                return new SignDefinition { Art = art, Text = text, X = center.x, Y = center.y,
                    Width = max.x - min.x, Height = max.y - min.y, Look = look, Grand = grand, Surface = true,
                    TopLeft = topLeft, TopRight = topRight, BottomLeft = bottomLeft, BottomRight = bottomRight,
                    TopRise = topRise, BottomRise = bottomRise };
            }

            private static void IncludeArcExtreme(Vector2 left, Vector2 right, float rise, ref Vector2 min, ref Vector2 max)
            {
                if (Mathf.Abs(rise) < .001f) return;
                var u = .5f - (right.y - left.y) / (8 * rise);
                if (u <= 0 || u >= 1) return;
                var point = Vector2.Lerp(left, right, u) - Vector2.up * (rise * 4 * u * (1 - u));
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
        }

        private static readonly SignDefinition[] CitySigns =
        {
            // Архив — в здании бывшего полицейского поста (решение автора, 2026-09-30): холма больше нет,
            // у полиции своего здания нет — девятки пик ходят патрулём.
            SignDefinition.Painted("card-city-old-town", "АРХИВ", SignLook.Board, false,
                new Vector2(392, 348), new Vector2(475, 360), new Vector2(392, 373), new Vector2(474, 385)),
            SignDefinition.Plaque("card-city-old-town", "ДОМ ШЕСТЁРКИ", 650, 420, 50),
            SignDefinition.Plaque("card-city-old-town", "ТАВЕРНА", 1090, 336, 50),
            SignDefinition.Plaque("card-city-old-town", "ГАДАЛКА", 1405, 430, 50),
            SignDefinition.Painted("card-city", "ЛАВКА", SignLook.Board, true,
                new Vector2(333, 309), new Vector2(487, 330), new Vector2(333, 339), new Vector2(487, 359)),
            SignDefinition.Painted("card-city", "ПОДПОЛЬЕ", SignLook.Board, false,
                new Vector2(473, 626), new Vector2(551, 638), new Vector2(469, 645), new Vector2(549, 657)),
            SignDefinition.Painted("card-city", "КЛУБ", SignLook.Marquee, true,
                new Vector2(715, 328), new Vector2(955, 328), new Vector2(715, 362), new Vector2(955, 363), 16, 10),
            SignDefinition.Painted("card-city", "НОВОСТИ", SignLook.Board, false,
                new Vector2(1123, 430), new Vector2(1228, 431), new Vector2(1123, 450), new Vector2(1228, 451)),
            // Вместо «ХОЛМ» на щите ворот замка — название города (решение автора, 2026-09-30): замок теперь
            // просто вид, не место — ни обводки, ни перехода. Поле щита чуть выгнуто дугой сверху и снизу.
            SignDefinition.Painted("card-city", "CARD CITY", SignLook.Marquee, false,
                new Vector2(787, 67), new Vector2(889, 67), new Vector2(787, 89), new Vector2(889, 89), 5, 4),
            SignDefinition.Plaque("card-city-tuzyara", "БАНК ТУЗЯРЫ", 428, 410, 50),
            SignDefinition.Painted("card-city-tuzyara", "КАЗИНО", SignLook.Marquee, true,
                new Vector2(778, 344), new Vector2(1056, 305), new Vector2(778, 371), new Vector2(1056, 340), 24, 13),
            SignDefinition.Painted("card-city-port", "РЫНОК", SignLook.Marquee, true,
                new Vector2(457, 256), new Vector2(667, 278), new Vector2(457, 292), new Vector2(667, 314), 15, 5),
            SignDefinition.Plaque("card-city-underground", "ЧЁРНЫЙ РЫНОК", 445, 300, 50),
            SignDefinition.Plaque("card-city-underground", "ПОДПОЛЬНЫЙ КЛУБ", 768, 284, 46),
            // Табличка у трибуны ведущего для надписи мала — плашка над самой Ямой.
            SignDefinition.Plaque("card-city-underground", "АРЕНА", 1335, 440, 50),
            // Афиша у входа в зал Клуба (картинка 1916×821): одно слово, нажатие открывает все игры
            // (решение автора, 2026-09-29), по центру чёрного поля, как на доске второго зала.
            // Названия игр — крупно на ковре перед столами (решение автора): фон зала открыт.
            FirstHallBoardTitle("ИГРЫ"),
            FloorName("durak", "ДУРАК"),
            FloorName("bluff", "ВЕРЮ — НЕ ВЕРЮ"),
            FloorName("chests", "СУНДУЧКИ"),
            FloorName("govno", "ГОВНО"),
            // Во втором зале своя доска — справа, у арки в следующий зал (решение автора, 2026-09-30).
            HallBoardTitle("ИГРЫ"),
        };

        /// <summary>Центр чёрного поля первой афиши; буквы следуют небольшому наклону доски.</summary>
        private static SignDefinition FirstHallBoardTitle(string text) =>
            SignDefinition.Painted(ClubHall, text, SignLook.Board, true,
                new Vector2(298, 407), new Vector2(391, 405), new Vector2(301, 454), new Vector2(394, 452));

        // Буквы растрируются вдвое крупнее и уменьшаются — чётко и на телефоне, и в приближении нырка.
        private const int SignOversample = 2;
        // Высота заглавной буквы в долях размера шрифта (мерено по файлам шрифтов).
        private const float SignDisplayCap = .71f, SignNarrowCap = .81f;
        private static readonly Color SignGoldTop = new Color(1f, .92f, .64f), SignGoldBottom = new Color(.92f, .64f, .28f),
            SignMarqueeInk = new Color(.47f, .07f, .09f), SignGlow = new Color(1f, .72f, .3f, .22f);

        private static Font s_signDisplay, s_signNarrow;
        private static Sprite s_signPlaque;
        private static bool s_signsLoaded;

        /// <summary>Вывески картинки <paramref name="art"/> — детьми её куска или этажа шириной <paramref name="width"/>.</summary>
        private void AddSigns(RectTransform page, string art, float width)
        {
            if (!s_signsLoaded)
            {
                s_signDisplay = Resources.Load<Font>("Fonts/SignDisplay");
                s_signNarrow = Resources.Load<Font>("Fonts/SignNarrow");
                s_signPlaque = Resources.Load<Sprite>("UI/Hub/sign-plaque");
                s_signsLoaded = true;
            }
            var picture = PictureSize(art);
            var unit = new Vector2(width / picture.x, CityHeight / picture.y);
            foreach (var sign in CitySigns)
            {
                if (sign.Art != art) continue;
                var holder = MakeRect(page, "Sign " + sign.Text, 0, 0, 0, 0);
                holder.pivot = new Vector2(.5f, .5f);
                holder.anchoredPosition = new Vector2(sign.X * unit.x, -sign.Y * unit.y);
                var height = sign.Height * unit.y;
                if (sign.Look == SignLook.Plaque)
                {
                    var text = SignText(holder, sign.Text, sign.Grand, height * .44f, float.MaxValue, SignLook.Plaque);
                    holder.sizeDelta = new Vector2(text.preferredWidth / SignOversample + height * 1.1f, height);
                    if (s_signPlaque == null) continue;
                    var plaque = holder.gameObject.AddComponent<Image>();
                    plaque.sprite = s_signPlaque;
                    plaque.type = Image.Type.Sliced;
                    // Рамка плашки — около трети её высоты при любой высоте.
                    plaque.pixelsPerUnitMultiplier = s_signPlaque.border.y / (height * .3f);
                    plaque.raycastTarget = false;
                    var lift = holder.gameObject.AddComponent<Shadow>();
                    lift.effectDistance = new Vector2(2, -3);
                    lift.effectColor = new Color(0, 0, 0, .5f);
                    // Надпись — поверх доски: плашка рисуется раньше своих детей.
                }
                else
                {
                    // Небольшой запас только для геометрии тени/свечения, не для размера букв.
                    holder.sizeDelta = new Vector2((sign.Width + 12) * unit.x, height + 12 * unit.y);
                    var top = (sign.TopLeft + sign.TopRight) / 2 - Vector2.up * sign.TopRise;
                    var bottom = (sign.BottomLeft + sign.BottomRight) / 2 - Vector2.up * sign.BottomRise;
                    var cap = Vector2.Distance(top, bottom) * unit.y;
                    var label = SignText(holder, sign.Text, sign.Grand, cap, sign.Width * unit.x, sign.Look);
                    // Прямоугольник охватывает деформированные буквы для корректного отсечения
                    // на краях экрана при проезде города и приближении к зданию.
                    label.rectTransform.sizeDelta = holder.sizeDelta * SignOversample;
                    var look = label.GetComponent<HubSignText>();
                    Vector2 Local(Vector2 point) => new Vector2((point.x - sign.X) * unit.x,
                        (sign.Y - point.y) * unit.y) * SignOversample;
                    look.FitToSurface = sign.Surface;
                    look.TopLeft = Local(sign.TopLeft);
                    look.TopRight = Local(sign.TopRight);
                    look.BottomLeft = Local(sign.BottomLeft);
                    look.BottomRight = Local(sign.BottomRight);
                    look.TopRise = sign.TopRise * unit.y * SignOversample;
                    look.BottomRise = sign.BottomRise * unit.y * SignOversample;
                    label.SetVerticesDirty();
                }
            }
        }

        /// <summary>
        /// Надпись заглавными высотой <paramref name="cap"/> (не шире <paramref name="maxWidth"/> — тогда
        /// мельче) в середине <paramref name="holder"/>.
        /// </summary>
        private Text SignText(RectTransform holder, string value, bool grand, float cap, float maxWidth, SignLook kind)
        {
            var marquee = kind == SignLook.Marquee;
            var font = (grand ? s_signDisplay : s_signNarrow) ?? _font;
            var label = MakeRect(holder, "Text", 0, 0, 0, 0).gameObject.AddComponent<Text>();
            var rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = Vector2.zero;
            label.font = font;
            label.text = value;
            label.raycastTarget = false;
            label.supportRichText = false;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.fontSize = 100;
            var size = cap / (font == s_signDisplay ? SignDisplayCap : SignNarrowCap);
            if (label.preferredWidth > 0) size = Mathf.Min(size, maxWidth * 100 / label.preferredWidth);
            label.fontSize = Mathf.Max(1, Mathf.RoundToInt(size * SignOversample));
            rect.localScale = Vector3.one / SignOversample;
            rect.sizeDelta = new Vector2(label.preferredWidth, label.preferredHeight);
            var look = label.gameObject.AddComponent<HubSignText>();
            look.Top = marquee ? SignMarqueeInk : SignGoldTop;
            look.Bottom = marquee ? SignMarqueeInk : SignGoldBottom;
            var depth = size * SignOversample;
            if (!marquee)
            {
                // Золото чуть светится в темноте.
                var glow = label.gameObject.AddComponent<Outline>();
                glow.effectColor = SignGlow;
                glow.effectDistance = new Vector2(.035f, -.035f) * depth;
            }
            var shadow = label.gameObject.AddComponent<Shadow>();
            shadow.effectColor = marquee ? new Color(.25f, .04f, .04f, .4f) : new Color(.06f, .03f, 0, .85f);
            shadow.effectDistance = new Vector2(.03f, -.05f) * depth;
            return label;
        }
    }
}
