using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>
    /// Клуб в стиле вывесок города (2026-09-30). Страница игры «game:&lt;id&gt;» — табличка у её стола:
    /// камера подходит к столу (в зале Клуба, у покера — к казино), стол встаёт левее середины, рядом —
    /// большая доска в золотой рамке: боты, быстрый вход, создание комнаты и код. Правила выбранной
    /// игры доступны сразу, запуск закреплён снизу (PlayerHubTableEntrance.cs). Табличку
    /// открывает и список игр, и возврат из партии: камера сама доезжает до стола, табличка видна сразу.
    /// Остальные экраны клуба (игры, комнаты, комната, правила) — те же доски и золотые буквы, свой
    /// заголовок с «Назад», без вкладок и верхней панели: город остаётся фоном.
    /// </summary>
    public sealed partial class PlayerHubController
    {
        private static readonly Color ClubCream = new Color(.96f, .91f, .8f);
        private static readonly Color ClubMuted = new Color(.8f, .71f, .58f);
        private static readonly Color ClubGold = new Color(1f, .83f, .48f);
        private static readonly Color ClubInk = new Color(.17f, .1f, .04f);
        private static readonly Color ClubDanger = new Color(1f, .62f, .52f);
        // Строка шрифта вывесок — 1,48 кегля, у обычного — 1,15: подписи клуба на пятую часть мельче,
        // чтобы строки влезали в прежние рамки.
        private const float ClubTextScale = .8f;
        // Стол при подходе — левее середины на эту долю ширины экрана: справа встаёт табличка.
        private const float TableAside = .26f;
        // Табличка: ширина, отступ от края сцены 1600×900 и верх.
        private const float PlaqueWidth = 800, PlaqueMargin = 24, PlaqueTop = 40;

        private static Sprite s_signPlaqueGold;
        // Страница строится в стиле клуба: доски, золото, шрифт вывесок.
        private bool _clubStyle;
        private RectTransform _clubHeader;
        private Text _clubTitle, _clubSubtitle;
        // Стол, к которому едет камера по странице его игры; −1 — никуда.
        private int _tableTrip = -1;
        private bool _clubEscapeHeld;

        private enum ClubTone { Normal, Primary, Danger }

        private static bool IsTablePage(string page) => page.StartsWith("game:", StringComparison.Ordinal);

        private static bool IsClubPage(string page) => IsTablePage(page) || IsRulesPage(page) || page == "games" || page == "rooms"
            || page == "create-room" || page == "quick-game" || page == "room-settings" || page == "room";

        private Color MutedInk => _clubStyle ? ClubMuted : HubMuted;
        private Color GoldInk => _clubStyle ? ClubGold : HubGold;

        private Font ClubFont()
        {
            if (s_signNarrow == null) s_signNarrow = Resources.Load<Font>("Fonts/SignNarrow");
            return s_signNarrow != null ? s_signNarrow : _font;
        }

        private Font ClubTitleFont()
        {
            if (s_signDisplay == null) s_signDisplay = Resources.Load<Font>("Fonts/SignDisplay");
            return s_signDisplay != null ? s_signDisplay : ClubFont();
        }

        private static Sprite ClubPlate(bool gold)
        {
            if (gold) return s_signPlaqueGold != null ? s_signPlaqueGold : s_signPlaqueGold = Resources.Load<Sprite>("UI/Hub/sign-plaque-gold");
            return s_signPlaque != null ? s_signPlaque : s_signPlaque = Resources.Load<Sprite>("UI/Hub/sign-plaque");
        }

        /// <summary>Доска вывески (тёмная или золотая) с рамкой <paramref name="frame"/> единиц при любом размере.</summary>
        private static void SetPlate(Image image, bool gold, float frame)
        {
            var sprite = ClubPlate(gold);
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            if (sprite != null) image.pixelsPerUnitMultiplier = sprite.border.y / Mathf.Max(1, frame);
        }

        private static Image ClubBoard(Transform parent, string name, float x, float y, float w, float h, float frame, bool gold = false)
        {
            var image = MakeRect(parent, name, x, y, w, h).gameObject.AddComponent<Image>();
            SetPlate(image, gold, frame);
            image.raycastTarget = false;
            return image;
        }

        /// <summary>Главное действие — золотая доска, «уйти» — красноватые буквы, остальное — тёмная доска с золотом.</summary>
        private static ClubTone ClubToneOf(string text)
        {
            if (Array.Exists(PrimaryWords, word => text.StartsWith(word, StringComparison.Ordinal))
                || text.StartsWith("Сохранить", StringComparison.Ordinal) || text.StartsWith("Сесть", StringComparison.Ordinal))
                return ClubTone.Primary;
            if (Array.Exists(DangerWords, word => text.StartsWith(word, StringComparison.Ordinal))) return ClubTone.Danger;
            return ClubTone.Normal;
        }

        /// <summary>Кнопка-доска клуба; надпись по центру, первым текстом — по ней кнопку находят проверки.</summary>
        private Button ClubButton(Transform parent, string text, float x, float y, float w, float h, Action action,
            ClubTone tone = ClubTone.Normal, int size = 0)
        {
            var rect = MakeRect(parent, text, x, y, w, h);
            // Опора в центре: нажатая доска проседает к своей середине.
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(x + w / 2, -y - h / 2);
            var board = rect.gameObject.AddComponent<Image>();
            SetPlate(board, tone == ClubTone.Primary, Mathf.Clamp(h * .26f, 10, 20));
            var lift = rect.gameObject.AddComponent<Shadow>();
            lift.effectDistance = new Vector2(2, -3);
            lift.effectColor = new Color(0, 0, 0, .5f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = board;
            var colors = button.colors;
            // Под мышью доска светлеет, нажатая — темнеет, недоступная — тусклая.
            colors.normalColor = colors.selectedColor = new Color(.9f, .9f, .9f);
            colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(.72f, .72f, .72f);
            colors.disabledColor = new Color(.5f, .5f, .5f, .7f);
            button.colors = colors;
            button.onClick.AddListener(() => action());
            rect.gameObject.AddComponent<HubPress>();
            var label = MakeLabel(rect, text, 12, 2, w - 24, h - 4, size > 0 ? size : Mathf.RoundToInt(Mathf.Min(h * .5f, 30)));
            label.font = ClubFont();
            label.alignment = TextAnchor.MiddleCenter;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 11;
            label.resizeTextMaxSize = label.fontSize;
            PaintClubLabel(label, tone);
            return button;
        }

        private static void PaintClubLabel(Text label, ClubTone tone)
        {
            // Золото — сверху светлее, как на вывесках; на золотой доске буквы тёмные.
            var look = label.GetComponent<HubSignText>();
            if (look == null) look = label.gameObject.AddComponent<HubSignText>();
            switch (tone)
            {
                case ClubTone.Primary: look.Top = look.Bottom = ClubInk; break;
                case ClubTone.Danger: look.Top = look.Bottom = ClubDanger; break;
                default: look.Top = SignGoldTop; look.Bottom = SignGoldBottom; break;
            }
            var shade = label.GetComponent<Shadow>();
            if (shade == null) shade = label.gameObject.AddComponent<Shadow>();
            shade.effectColor = tone == ClubTone.Primary ? new Color(1, .95f, .8f, .35f) : new Color(.06f, .03f, 0, .85f);
            shade.effectDistance = tone == ClubTone.Primary ? new Vector2(0, -1) : new Vector2(1, -2);
            label.SetVerticesDirty();
        }

        /// <summary>Перекрасить доску клуба: выбранный вариант или главное действие — золотом.</summary>
        private static void ClubPaint(Button button, ClubTone tone)
        {
            var board = button.GetComponent<Image>();
            var frame = board.sprite != null ? board.sprite.border.y / board.pixelsPerUnitMultiplier : 14;
            SetPlate(board, tone == ClubTone.Primary, frame);
            var label = button.GetComponentInChildren<Text>();
            if (label != null) PaintClubLabel(label, tone);
        }

        /// <summary>
        /// Строка-доска таблички: слева значок, заголовок золотом, под ним подробности; справа знак
        /// <paramref name="mark"/> («»» — перейти, «×» — свернуть). Заголовок — первый текст кнопки.
        /// </summary>
        private Button ClubRow(string title, string detail, float x, float y, float w, float h, Action action, string icon = null,
            string mark = "»")
        {
            var rect = MakeRect(_content, title, x, y, w, h);
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(x + w / 2, -y - h / 2);
            var board = rect.gameObject.AddComponent<Image>();
            SetPlate(board, false, 16);
            var lift = rect.gameObject.AddComponent<Shadow>();
            lift.effectDistance = new Vector2(2, -3);
            lift.effectColor = new Color(0, 0, 0, .5f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = board;
            var colors = button.colors;
            colors.normalColor = colors.selectedColor = new Color(.9f, .9f, .9f);
            colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(.72f, .72f, .72f);
            button.colors = colors;
            button.onClick.AddListener(() => action());
            rect.gameObject.AddComponent<HubPress>();
            var indent = icon == null ? 28f : 80f;
            // Заголовок — верхние 55 % строки, подробности — остальное: шрифту вывесок нужна высокая строка.
            var split = h * .55f;
            var heading = MakeLabel(rect, title, indent, 5, w - indent - 64, split - 5, 24);
            heading.font = ClubFont();
            heading.color = ClubGold;
            heading.alignment = TextAnchor.LowerLeft;
            heading.resizeTextForBestFit = true;
            heading.resizeTextMinSize = 12;
            heading.resizeTextMaxSize = 24;
            heading.gameObject.AddComponent<Shadow>().effectColor = new Color(.06f, .03f, 0, .85f);
            var more = MakeLabel(rect, detail, indent, split, w - indent - 64, h - split - 5, 17);
            more.font = ClubFont();
            more.color = ClubMuted;
            more.resizeTextForBestFit = true;
            more.resizeTextMinSize = 13;
            more.resizeTextMaxSize = 17;
            if (icon != null)
            {
                var image = MakeRect(rect, "Icon", 24, (h - 40) / 2, 40, 40).gameObject.AddComponent<Image>();
                image.sprite = Icon(icon);
                image.color = ClubGold;
                image.preserveAspect = true;
                image.raycastTarget = false;
            }
            var chevron = MakeLabel(rect, mark, w - 56, 0, 36, h, 34);
            chevron.font = ClubFont();
            chevron.color = ClubGold;
            chevron.alignment = TextAnchor.MiddleCenter;
            return button;
        }

        /// <summary>Шапка страниц клуба: «НАЗАД» — доской, как «ВЫЙТИ» в зале, заголовок — на доске-вывеске.</summary>
        private void BuildClubHeader()
        {
            _clubHeader = StageLayer("Club header");
            CityButton(_clubHeader, "НАЗАД", 24, 24, 170, 56, Back);
            // Заголовок — на своей доске, как вывеска: город за ним пёстрый, без доски буквы теряются.
            var board = ClubBoard(_clubHeader, "Club title board", 290, 10, 1020, 120, 22);
            board.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2, -4);
            _clubTitle = MakeLabel(_clubHeader, "", 330, 18, 940, 62, 44);
            _clubTitle.font = ClubTitleFont();
            _clubTitle.alignment = TextAnchor.MiddleCenter;
            _clubTitle.resizeTextForBestFit = true;
            _clubTitle.resizeTextMinSize = 24;
            _clubTitle.resizeTextMaxSize = 44;
            var look = _clubTitle.gameObject.AddComponent<HubSignText>();
            look.Top = SignGoldTop;
            look.Bottom = SignGoldBottom;
            var shade = _clubTitle.gameObject.AddComponent<Shadow>();
            shade.effectColor = new Color(.06f, .03f, 0, .85f);
            shade.effectDistance = new Vector2(1.5f, -2.5f);
            _clubSubtitle = MakeLabel(_clubHeader, "", 330, 84, 940, 32, 19);
            _clubSubtitle.font = ClubFont();
            _clubSubtitle.color = ClubMuted;
            _clubSubtitle.alignment = TextAnchor.MiddleCenter;
            _clubSubtitle.resizeTextForBestFit = true;
            _clubSubtitle.resizeTextMinSize = 12;
            _clubSubtitle.resizeTextMaxSize = 19;
            _clubHeader.gameObject.SetActive(false);
        }

        /// <summary>Esc на экранах клуба — «Назад»; на главной та же клавиша не должна тут же вывести из здания.</summary>
        private void ClubKeys(bool typing)
        {
            var keyboard = Keyboard.current;
            var escape = keyboard != null && keyboard.escapeKey.isPressed;
            var pressed = escape && !_clubEscapeHeld;
            _clubEscapeHeld = escape;
            if (!pressed || typing || _busy || !IsClubPage(_page)) return;
            _cityVerticalHeld = 1;
            Back();
        }

        // ---------- Табличка у стола ----------

        /// <summary>Место-стол игры: стол в зале Клуба или казино на улице (подход сразу открывает игру).</summary>
        private static int TablePlace(string game) => Array.FindIndex(CityPlaces, place => place.Direct && place.Route == "game:" + game);

        private bool AtTable(int index) => index >= 0 && _visit == index && _visitPhase == 2;

        private static bool IsGameTable(CityPlace place) =>
            place.Direct && place.Route != null && place.Route.StartsWith("game:", StringComparison.Ordinal);

        /// <summary>Камера — к столу: если она не у него и не подходит к нему, поездка начнётся в <see cref="ContinueTableTrip"/>.</summary>
        private void GoToTable(int index)
        {
            _tableTrip = index < 0 || _visit == index && (_visitPhase == 1 || _visitPhase == 2) ? -1 : index;
        }

        /// <summary>
        /// Каждый кадр, пока открыта страница игры: из другого здания или этажа — через улицу, по улице —
        /// до района, в зале — к столу, и подход. Всё по готовым движениям города: нырок, проезд, подход.
        /// </summary>
        private void ContinueTableTrip()
        {
            if (_tableTrip < 0) return;
            var place = CityPlaces[_tableTrip];
            if (_page != place.Route || _diveTarget >= 0 || _diveQueued || _gliding || _floorPanning || _cityDragging
                || _floorDragging || _visitPhase == 3 || _diveShade == null) return;
            // У другого места — сразу обычный вид, дальше подход к столу.
            if (_visitPhase != 0) EndVisitNow();
            var level = PlaceLevel(place);
            if (_cityLevel != level)
            {
                StartDive(_cityLevel != 0 ? 0 : level);
                return;
            }
            if (!_placePages.TryGetValue(place.Art, out var view)) return;
            if (level == 0)
            {
                var stop = NearestStop(view.X + place.Door.x / PictureSize(place.Art).x * view.Width);
                if (Mathf.Abs(_cityCamera - _cityStops[stop]) > .05f * CityView())
                {
                    ShowDistrict(stop);
                    return;
                }
            }
            else
            {
                // По залу — проездом, как по A/D, а не прыжком: стол ближе к середине, потом подход.
                var limit = FloorPanLimit(level);
                var u = (view.X + place.Door.x / PictureSize(place.Art).x * view.Width) / _floorWidths[level];
                var target = Mathf.Clamp((u - .5f) * _floorWidths[level], -limit, limit);
                if (Mathf.Abs(target - _floorPan) > .05f * FloorView())
                {
                    GlideFloor(target, GlideBase + GlidePerScreen * Mathf.Abs(target - _floorPan) / FloorView());
                    return;
                }
            }
            var index = _tableTrip;
            _tableTrip = -1;
            StartVisit(index);
        }

        /// <summary>Сразу закончить подход к месту: город в обычном виде, без затемнения.</summary>
        private void EndVisitNow()
        {
            if (_visitPhase == 0) return;
            _cityRig.localScale = Vector3.one;
            _cityRig.anchoredPosition = Vector2.zero;
            _diveShade.gameObject.SetActive(false);
            _diveVignette.gameObject.SetActive(false);
            if (_outdoorControls != null) _outdoorControls.gameObject.SetActive(false);
            _visitPhase = 0;
            _visit = -1;
        }

        /// <summary>Подпись расположения стола для списка игр.</summary>
        private static string TableWhere(int index)
        {
            if (index < 0) return "";
            return IsClubArt(CityPlaces[index].Art) ? "Стол в Клубе" : "Стол в казино Тузяры";
        }

        /// <summary>Сесть за стол: быстрая партия с ботами по последним настройкам этой игры.</summary>
        private void SitAtTable(string game)
        {
            PrepareGameSetup("quick", game);
            _tableMode = TableMode.Bots;
            if (_quickGame != null) _quickGame(game);
            else _play?.Invoke();
        }

        /// <summary>
        /// Общая панель входа для всех пяти игр. Стол слева, режимы и настройки справа.
        /// </summary>
        private void BuildTablePlaque(string game) => BuildTableEntrance(game);
    }
}
