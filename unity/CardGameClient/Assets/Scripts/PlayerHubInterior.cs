using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace CardGameClient
{
    /// <summary>
    /// Здания изнутри — этажи с номера <see cref="FirstInterior"/> (картинки — в FloorArt): вход — нырок
    /// в двери здания, выход — нажать на дверь изнутри, «Выйти», «Назад», Esc или S/↓. Первый — зал
    /// Клуба (`UI/Hub/club-hall`, 21:9): четыре стола в пятнах света — четыре игры зала, названия на ковре
    /// перед столами, на афише у входа — «ИГРЫ» (все игры). Второй зал и проход временно выключены
    /// через ClubSecondHallEnabled: их фон, столы и настройки сохранены для возвращения позже.
    /// Свайп, A/D и стрелки пока ограничены стенами первого зала.
    /// </summary>
    public sealed partial class PlayerHubController
    {
        // Этажи с этого номера — здания изнутри, а не части города.
        private const int FirstInterior = 2;
        private const int FortuneLevel = 5;
        private const string ClubHall = "club-hall";
        private const string ClubHall2 = "club-hall-2", ClubBridge = "club-bridge-1-2";
        // Временно убран по просьбе автора (2026-10-05). Вернуть второй зал и проход можно одним
        // флагом: код панорамы, места, вывески и исходные картинки остаются в проекте.
        private static readonly bool ClubSecondHallEnabled = false;
        // Совпадающие края готовых картинок: мост начинается в x=1105 первого зала,
        // второй зал — в x=1342 моста. Всё измерено в общей системе 1916×821.
        private const float ClubPictureWidth = 1916, ClubPictureHeight = 821, ClubBridgeX = 1105, ClubHall2X = 2447;

        private RectTransform _interiorControls;
        // Сдвиг вида по этажу вбок — в пикселях его картинки (0 — середина). Зал 21:9 на экране 16:9 или 4:3
        // шире экрана: его ведут пальцем, A/D и стрелками, но не дальше стен. Этаж по ширине экрана не сдвигается.
        private float _floorPan, _floorPanTo, _floorPanFrom, _floorPanLast, _floorPanVelocity;
        private bool _floorPanning, _floorDragging;
        // Проезд по залу — как по улице (PlayerHubCity.cs): та же кривая и те же времена. Цель, с которой
        // проезд задуман (если окно сузилось и цель прижало к стене — проезд пересчитывается с ходу),
        // старт, скорость на старте, длительность, прошедшее время и клавиша, которая его ведёт.
        private float _floorGlideTarget, _floorGlideFrom, _floorGlideSpeed, _floorGlideLength, _floorGlideTime;
        private int _floorGlideKey;
        // Палец: где был в прошлом кадре, когда и с какой скоростью (для прокатывания после отпускания).
        private float _floorDragX, _floorDragTime, _floorDragSpeed;
        private int _clubHallSide;

        private static bool IsInterior(int level) => level >= FirstInterior;
        private static bool IsClubArt(string art) => art == ClubHall || art == ClubBridge || art == ClubHall2;

        /// <summary>Клуб — один этаж, три картинки и отдельный верхний слой мебели и вывесок.</summary>
        private bool BuildClubPanorama(RectTransform floor, Sprite first)
        {
            // EnsureFloor соберёт только первый зал с его обычной шириной и границами камеры.
            if (!ClubSecondHallEnabled) return false;
            var bridge = Resources.Load<Sprite>("UI/Hub/" + ClubBridge);
            var second = Resources.Load<Sprite>("UI/Hub/" + ClubHall2);
            if (bridge == null || second == null) return false;
            var unit = CityHeight / ClubPictureHeight;
            var width = ClubPictureWidth * unit;
            var total = (ClubHall2X + ClubPictureWidth) * unit;
            _floorWidths[FirstInterior] = total;
            floor.sizeDelta = new Vector2(total, CityHeight);
            var pieces = new[] { (Art: first, X: 0f, Fade: 0f),
                (Art: bridge, X: ClubBridgeX, Fade: 500f), (Art: second, X: ClubHall2X, Fade: 500f) };
            RectTransform Page(string name, float x)
            {
                var page = MakeRect(floor, name, 0, 0, 0, 0);
                page.anchorMin = page.anchorMax = page.pivot = new Vector2(.5f, .5f);
                page.anchoredPosition = new Vector2((x + ClubPictureWidth / 2) * unit - total / 2, 0);
                page.sizeDelta = new Vector2(width, CityHeight);
                return page;
            }
            foreach (var piece in pieces)
            {
                var art = Page(piece.Art.name, piece.X).gameObject.AddComponent<HubCityArt>();
                art.Art = piece.Art;
                // На первом стыке дальше 560 px уже другая арка: склейка заканчивается раньше.
                art.FadeLeft = piece.Fade / ClubPictureWidth;
                art.raycastTarget = false;
                art.SetAllDirty();
            }
            // Последний стол первого зала пересекает стык: фон перехода не должен его перекрывать.
            foreach (var piece in pieces)
            {
                var overlay = Page(piece.Art.name + " places", piece.X);
                AddInteriorFurniture(overlay, piece.Art.name, width);
                AddSigns(overlay, piece.Art.name, width);
                RegisterPlacePage(piece.Art.name, overlay, piece.X * unit, width);
            }
            return true;
        }

        /// <summary>Нажатие на арку — плавно пройти в другой зал, оставаясь внутри клуба.</summary>
        private void CrossClubHall()
        {
            if (!ClubSecondHallEnabled || _cityLevel != FirstInterior || _visitPhase != 0 || _diveTarget >= 0
                || !_placePages.TryGetValue(ClubHall, out var first) || !_placePages.TryGetValue(ClubHall2, out var second)) return;
            var middle = _floorWidths[FirstInterior] / 2;
            var a = first.X + first.Width / 2 - middle;
            var b = second.X + second.Width / 2 - middle;
            // В самом проходе сохраняем зал, откуда пришли: по одной и той же арке можно вернуться.
            var target = Mathf.Clamp(_clubHallSide == 0 ? b : a, -FloorPanLimit(FirstInterior), FloorPanLimit(FirstInterior));
            GlideFloor(target, GlideBase + GlidePerScreen * Mathf.Abs(target - _floorPan) / FloorView());
        }

        private void TrackClubHall()
        {
            if (!ClubSecondHallEnabled || _cityLevel != FirstInterior || !_placePages.ContainsKey(ClubHall2)) return;
            var center = (_floorPan + _floorWidths[FirstInterior] / 2) * ClubPictureHeight / CityHeight;
            if (center <= ClubPictureWidth) _clubHallSide = 0;
            else if (center >= ClubHall2X) _clubHallSide = 1;
        }

        /// <summary>Сдвиг вида этажа (у этажа, где не стоим, — ноль).</summary>
        private float FloorPan(int level) => level != 0 && level == _cityLevel ? _floorPan : 0;

        /// <summary>Насколько вид этажа может уйти от середины в каждую сторону (в пикселях картинки).</summary>
        private float FloorPanLimit(int level)
        {
            // Шатёр гадалки всегда по центру: стол с картами совпадает со страницей расклада.
            if (level == 0 || level == FortuneLevel || Floor(level) == null) return 0;
            return Mathf.Max(0, (_floorWidths[level] * FloorScale(level) - _cityArea.rect.width) / 2 / FloorScale(level));
        }

        /// <summary>Сразу показать место этажа как можно ближе к середине экрана.</summary>
        private void LookAtPlace(CityPlace place)
        {
            var view = _placePages[place.Art];
            LookAtPicture((view.X + place.Door.x / PictureSize(place.Art).x * view.Width) / _floorWidths[_cityLevel]);
        }

        /// <summary>Сразу сдвинуть вид этажа так, чтобы доля <paramref name="u"/> его ширины была ближе к середине.</summary>
        private void LookAtPicture(float u)
        {
            var limit = FloorPanLimit(_cityLevel);
            _floorPan = _floorPanTo = Mathf.Clamp((u - .5f) * _floorWidths[_cityLevel], -limit, limit);
            _floorPanning = false;
            _floorPanVelocity = 0;
            PlaceFloors();
        }

        /// <summary>
        /// Кнопка в городе — в стиле вывесок: тёмная доска в золотой рамке (`UI/Hub/sign-plaque`) и золотые
        /// буквы узким шрифтом вывесок. Для «Выйти» в зданиях и «Отойти» у фонтана.
        /// </summary>
        // Кнопки в верхних углах (ВЫЙТИ, НАЗАД, ОТОЙТИ, ЗАКРЫТЬ, счёт подарка; решение автора, 2026-10-07): на вытянутом
        // экране — у краёв экрана, а не у краёв сцены 1600×900 посередине. Сторона: −1 — левый угол, 1 — правый.
        private readonly List<(RectTransform Rect, Vector2 Home, int Side)> _cornerPins = new List<(RectTransform, Vector2, int)>();

        private RectTransform PinToCorner(RectTransform rect, int side)
        {
            _cornerPins.Add((rect, rect.anchoredPosition, side));
            PinCorners();
            return rect;
        }

        /// <summary>Сдвинуть кнопки углов на запас экрана за сценой (после смены размера окна).</summary>
        private void PinCorners()
        {
            if (_stage == null) return;
            var size = ((RectTransform)transform).rect.size / Mathf.Max(.01f, _stage.localScale.x);
            var spare = new Vector2(Mathf.Max(0, size.x - 1600) / 2, Mathf.Max(0, size.y - 900) / 2);
            foreach (var pin in _cornerPins)
                if (pin.Rect != null) pin.Rect.anchoredPosition = pin.Home + new Vector2(pin.Side * spare.x, spare.y);
        }

        private Button CityButton(Transform parent, string text, float x, float y, float w, float h, System.Action action)
        {
            var plaque = s_signPlaque != null ? s_signPlaque : s_signPlaque = Resources.Load<Sprite>("UI/Hub/sign-plaque");
            var font = s_signNarrow != null ? s_signNarrow : s_signNarrow = Resources.Load<Font>("Fonts/SignNarrow");
            var rect = MakeRect(parent, text, x, y, w, h);
            // Опора в центре: нажатая кнопка проседает к своей середине.
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(x + w / 2, -y - h / 2);
            var board = rect.gameObject.AddComponent<Image>();
            board.sprite = plaque;
            board.type = Image.Type.Sliced;
            if (plaque != null) board.pixelsPerUnitMultiplier = plaque.border.y / (h * .3f);
            rect.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2, -3);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = board;
            var colors = button.colors;
            // Под мышью доска светлеет, нажатая — темнеет.
            colors.normalColor = new Color(.88f, .88f, .88f);
            colors.highlightedColor = Color.white;
            colors.selectedColor = new Color(.88f, .88f, .88f);
            colors.pressedColor = new Color(.7f, .7f, .7f);
            button.colors = colors;
            button.onClick.AddListener(() => action());
            rect.gameObject.AddComponent<HubPress>();
            var label = MakeLabel(rect, text, 0, 0, w, h, Mathf.RoundToInt(h * .5f));
            label.font = font != null ? font : _font;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            var look = label.gameObject.AddComponent<HubSignText>();
            look.Top = SignGoldTop;
            look.Bottom = SignGoldBottom;
            var shade = label.gameObject.AddComponent<Shadow>();
            shade.effectColor = new Color(.06f, .03f, 0, .85f);
            shade.effectDistance = new Vector2(1, -2);
            return button;
        }

        /// <summary>Ширина экрана в единицах этажа.</summary>
        private float FloorView() => _cityArea.rect.width / FloorScale(_cityLevel);

        /// <summary>Где зал встаёт по A/D и куда доводится после свайпа: обе стены и середины залов Клуба.</summary>
        private List<float> FloorStops(float limit)
        {
            var stops = new List<float> { -limit, limit };
            if (_cityLevel != FirstInterior) return stops;
            var middle = _floorWidths[FirstInterior] / 2;
            foreach (var art in new[] { ClubHall, ClubHall2 })
                if (_placePages.TryGetValue(art, out var page))
                    stops.Add(Mathf.Clamp(page.X + page.Width / 2 - middle, -limit, limit));
            return stops;
        }

        /// <summary>A/D и стрелки в зале — как на улице: к соседней остановке (середине зала или стене).</summary>
        private void StepFloor(int step)
        {
            var limit = FloorPanLimit(_cityLevel);
            if (limit <= 0) return;
            var view = FloorView();
            var from = _floorPanning ? _floorPanTo : _floorPan;
            var next = float.NaN;
            foreach (var stop in FloorStops(limit))
                if (step * (stop - from) > .05f * view && (float.IsNaN(next) || Mathf.Abs(stop - from) < Mathf.Abs(next - from)))
                    next = stop;
            if (float.IsNaN(next))
            {
                _floorGlideKey = 0;
                return;
            }
            GlideFloor(next, GlideBase + GlidePerScreen * Mathf.Abs(next - _floorPan) / view);
            _floorGlideKey = step;
        }

        /// <summary>
        /// Плавный проезд зала в <paramref name="to"/> — как <see cref="GlideCity"/>: трогается с той скоростью,
        /// с которой уже ехал (без рывка), и мягко тормозит к цели; встречная скорость гасится.
        /// </summary>
        private void GlideFloor(float to, float duration)
        {
            var distance = to - _floorPan;
            var speed = _floorPanVelocity;
            if (speed * distance <= 0) speed = 0;
            else duration = Mathf.Min(duration, Mathf.Max(.25f, 2.5f * distance / speed));
            _floorGlideFrom = _floorPan;
            _floorPanTo = _floorGlideTarget = to;
            _floorGlideSpeed = speed;
            _floorGlideLength = Mathf.Max(.05f, duration);
            _floorGlideTime = 0;
            _floorGlideKey = 0;
            _floorDragging = false;
            _floorPanning = true;
        }

        /// <summary>Шаг проезда зала на <paramref name="deltaTime"/> секунд: та же кривая Эрмита, что у города.</summary>
        private void MoveFloorPan(float deltaTime)
        {
            if (!_floorPanning || _floorDragging) return;
            // Окно сузили посреди проезда — стены сдвинулись и прижали цель: едем к новой цели с текущей скоростью.
            if (_floorPanTo != _floorGlideTarget)
            {
                var key = _floorGlideKey;
                GlideFloor(_floorPanTo, Mathf.Max(.25f, _floorGlideLength - _floorGlideTime));
                _floorGlideKey = key;
            }
            _floorGlideTime += deltaTime;
            var s = Mathf.Clamp01(_floorGlideTime / _floorGlideLength);
            if (s >= 1)
            {
                _floorPan = _floorPanTo;
                _floorPanning = false;
                _floorGlideKey = 0;
                _floorPanVelocity = 0;
            }
            else
            {
                var s2 = s * s;
                var s3 = s2 * s;
                _floorPan = (2 * s3 - 3 * s2 + 1) * _floorGlideFrom + (s3 - 2 * s2 + s) * _floorGlideLength * _floorGlideSpeed
                    + (3 * s2 - 2 * s3) * _floorPanTo;
                _floorPanVelocity = 6 * (s - s2) * (_floorPanTo - _floorGlideFrom) / _floorGlideLength + (3 * s2 - 4 * s + 1) * _floorGlideSpeed;
            }
            PlaceFloors();
        }

        private void BeginFloorDrag(UnityEngine.EventSystems.PointerEventData data)
        {
            if (_page != "home" || _diveTarget >= 0 || _visitPhase != 0 || FloorPanLimit(_cityLevel) <= 0) return;
            // Зал ловится на ходу: проезд обрывается там, где его взяли.
            _floorDragging = true;
            _floorPanning = false;
            _floorGlideKey = 0;
            _floorPanVelocity = 0;
            _floorPanFrom = _floorPan;
            _floorPanLast = _floorDragX = FloorX(data);
            _floorDragTime = Time.unscaledTime;
            _floorDragSpeed = 0;
        }

        private void DragFloor(UnityEngine.EventSystems.PointerEventData data)
        {
            if (!_floorDragging) return;
            var x = FloorX(data);
            var elapsed = Time.unscaledTime - _floorDragTime;
            if (elapsed > 0) _floorDragSpeed = Mathf.Lerp(_floorDragSpeed, (_floorDragX - x) / elapsed, .6f);
            _floorDragX = x;
            _floorDragTime = Time.unscaledTime;
            var limit = FloorPanLimit(_cityLevel);
            _floorPan = Mathf.Clamp(_floorPanFrom + _floorPanLast - x, -limit, limit);
            PlaceFloors();
        }

        /// <summary>
        /// Отпустили зал — как улицу: палец замер — зал стоит, иначе прокатывается по инерции; рядом середина
        /// зала или стена — доезжает к ней.
        /// </summary>
        private void EndFloorDrag()
        {
            if (!_floorDragging) return;
            _floorDragging = false;
            var limit = FloorPanLimit(_cityLevel);
            var view = FloorView();
            _floorPanVelocity = Time.unscaledTime - _floorDragTime < .12f
                ? Mathf.Clamp(_floorDragSpeed, -FlingLimit * view, FlingLimit * view) : 0;
            var rest = Mathf.Clamp(_floorPan + _floorPanVelocity * Coast, -limit, limit);
            var nearest = rest;
            var best = float.MaxValue;
            foreach (var stop in FloorStops(limit))
                if (Mathf.Abs(stop - rest) < best)
                {
                    best = Mathf.Abs(stop - rest);
                    nearest = stop;
                }
            if (best < DistrictMagnet * view) rest = nearest;
            GlideFloor(rest, Mathf.Min(1.4f, .45f + .5f * Mathf.Abs(rest - _floorPan) / view));
        }

        /// <summary>Палец — в пикселях картинки этажа.</summary>
        private float FloorX(UnityEngine.EventSystems.PointerEventData data)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_cityArea, data.position, data.pressEventCamera, out var local);
            return local.x / FloorScale(_cityLevel);
        }

        /// <summary>Размер исходной картинки, в пикселях которой заданы места и вывески.</summary>
        private static Vector2 PictureSize(string art) =>
            IsClubArt(art) ? new Vector2(ClubPictureWidth, ClubPictureHeight)
            : art == SixRoom || art == BankHall || art == TavernHall ? new Vector2(SixPictureWidth, SignPictureHeight) : new Vector2(SignPictureWidth, SignPictureHeight);

        // Стол в зале Клуба (пиксели картинки зала): ширина со стульями; середина того, чем он стоит на полу
        // (ножки стульев и подножие), — в середине пятна света на полу. У всех четырёх пятен общая
        // линия опоры y=624. Доли картинки стола: высота к ширине и середина опоры.
        private const float TableWidth = 280, TableFloorY = 624, TableAspect = 560f / 720, TableFeet = 487f / 560;
        // Название игры — крупными золотыми буквами на ковре перед столом, сразу за краем пятна света:
        // верх заглавных и их высота. У «Д», «Ц», «Щ» хвост ниже строки (доля заглавной в шрифте вывесок) —
        // поле букв с таким хвостом ниже, чтобы заглавные всех названий были одной высоты.
        private const float FloorNameTop = 684, FloorNameCap = 36, FloorNameTail = .18f, FloorNameWidth = 360;
        // Ширина буквы названия в долях заглавной — с запасом (мерено по шрифту вывесок).
        private const float FloorNameLetter = .7f;
        private static readonly char[] FloorNameTails = { 'Д', 'Ц', 'Щ' };

        /// <summary>
        /// Общая ось стола, его тени, подписи и области нажатия — центр светового пятна на ковре.
        /// Центры замерены по картинке 1916×821; нарисованные лампы с ними не вполне совпадают.
        /// </summary>
        private static float ClubTableX(string game)
        {
            switch (game)
            {
                case "durak": return 500;
                case "bluff": return 845;
                case "chests": return 1197;
                default: return 1563;
            }
        }

        /// <summary>Верх картинки стола в зале (опора — в середине пятна света).</summary>
        private static float TableTop => TableFloorY - TableWidth * TableAspect * TableFeet;
        private const float SecondTableFloorY = 626;

        /// <summary>Общий контур мебели в обоих залах: заканчивается у ножек, выше названий игр.</summary>
        private static float[] TableOutline(float x, float top)
        {
            var bottom = top + TableWidth * TableAspect;
            var half = TableWidth / 2;
            return new[]
            {
                x - half * .42f, bottom, x - half * .85f, bottom - 30, x - half, top + 40,
                x - half * .7f, top + 2, x + half * .7f, top + 2, x + half, top + 40,
                x + half * .85f, bottom - 30, x + half * .42f, bottom,
            };
        }

        // Доска «ИГРЫ» второго зала нарисована в самой картинке зала (`club-hall-2-v2`, решение автора,
        // 2026-09-30): справа, между столбиками верёвки перед аркой в следующий зал. Всё мерено по картинке
        // 1916×821: середина и полуширина рамки, кончик пики, верх рамки, середина чёрного поля
        // (x 1738–1853, y 347–497), низ рамки и концы ножек.
        private const float BoardX = 1794, BoardHalf = 75, BoardTop = 287, BoardFrameY = 334, BoardFieldY = 422,
            BoardShelfY = 516, BoardFeetY = 609;

        /// <summary>Доска второго зала: нажатие — список всех игр, как на афише первого зала.</summary>
        private static CityPlace HallBoard() =>
            Place("club-board-2", ClubHall2, "Афиша", "Клуб", null, BoardX, BoardFieldY, new[]
            {
                BoardX - BoardHalf, BoardShelfY, BoardX - BoardHalf, BoardFrameY, BoardX - 16, BoardFrameY, BoardX, BoardTop,
                BoardX + 16, BoardFrameY, BoardX + BoardHalf, BoardFrameY, BoardX + BoardHalf, BoardShelfY,
                BoardX + BoardHalf * .92f, BoardFeetY, BoardX - BoardHalf * .92f, BoardFeetY,
            }, "games", direct: true);

        /// <summary>Надпись на доске второго зала — в середине чёрного поля, размером как на афише первого.</summary>
        private static SignDefinition HallBoardTitle(string text) =>
            SignDefinition.Painted(ClubHall2, text, SignLook.Board, true,
                new Vector2(BoardX - 48, BoardFieldY - 24), new Vector2(BoardX + 48, BoardFieldY - 24),
                new Vector2(BoardX - 48, BoardFieldY + 24), new Vector2(BoardX + 48, BoardFieldY + 24));

        /// <summary>Свободный стол второго зала: контур мебели без названия и без действия по нажатию.</summary>
        private static CityPlace EmptyClubTable(int seat, float x)
        {
            var top = SecondTableFloorY - TableWidth * TableAspect * TableFeet;
            return Place("table-club-2-" + seat, ClubHall2, "Свободный стол " + seat, "Клуб", null,
                x, top + TableWidth * TableAspect * .6f, TableOutline(x, top), hoverOnly: true);
        }

        /// <summary>Низ поля букв названия на полу: у названий с хвостом «Д» — ниже на хвост.</summary>
        private static float FloorNameBottom(string text) =>
            FloorNameTop + FloorNameCap * (text.ToUpperInvariant().IndexOfAny(FloorNameTails) >= 0 ? 1 + FloorNameTail : 1);

        /// <summary>
        /// Стол зала Клуба: нажатие на мебель или название — к столу и сразу в игру.
        /// Область нажатия включает буквы на полу, светлый контур — только стол со стульями.
        /// </summary>
        private static CityPlace ClubTable(string game)
        {
            var x = ClubTableX(game);
            var half = TableWidth / 2;
            var top = TableTop;
            var bottom = top + TableWidth * TableAspect;
            var title = GameTitle(game);
            var name = Mathf.Max(half * .6f, title.Length * FloorNameCap * FloorNameLetter / 2 + 12);
            var foot = FloorNameBottom(title) + 10;
            return Place("table-" + game, ClubHall, title, "Клуб", null, x, top + TableWidth * TableAspect * .6f, new[]
            {
                x - name, foot, x - half, bottom - 30, x - half, top + 40, x - half * .7f, top + 2,
                x + half * .7f, top + 2, x + half, top + 40, x + half, bottom - 30, x + name, foot,
            }, "game:" + game, direct: true, outlinePoints: TableOutline(x, top));
        }

        /// <summary>Название по оси стола, на общей линии: прямое симметричное поле без бокового наклона букв.</summary>
        private static SignDefinition FloorName(string game, string text)
        {
            var x = ClubTableX(game);
            var half = FloorNameWidth / 2;
            var bottom = FloorNameBottom(text);
            var sign = SignDefinition.Painted(ClubHall, text, SignLook.Board, false,
                new Vector2(x - half, FloorNameTop), new Vector2(x + half, FloorNameTop),
                new Vector2(x - half, bottom), new Vector2(x + half, bottom));
            sign.Game = game;
            return sign;
        }

        // Первый зал — «живой» (2026-09-30): столы, бармен, пианист и жители уже нарисованы в самой
        // картинке (`screens/references/city/club-hall-live.png`), тени столов и отсвет ламп — тоже. У готовых
        // столов поверх зала лежит живой слой `UI/Hub/club-live-<игра>` — квадрат 360×360 единиц от y 300
        // в двойной чёткости (у стола жители не мылятся; потом на его место встанет видео), и картинка стола
        // только отмечает мебель. Столы без жителей рисуются по-прежнему: они совпадают с нарисованными
        // и остаются чёткими, в том числе там, где зал уходит под картинку прохода.
        private const bool ClubHallPainted = true;
        private const float LiveSquare = 360, LiveTop = 300;

        // Ролики жителей первого зала (2026-10-01): место-житель, ролики `UI/Hub/Video/club-<житель>-<вид>`
        // (play, idle, stop, start — есть хотя бы play) с общей маской `club-<житель>-mask` и их прямоугольник
        // в единицах зала 1916×821 (печатает scripts/club-live/video_clip.py; бармена —
        // prepare_bartender_clips.py, у него ещё редкий жест `club-bartender-action` — наливает себе и пробует).
        // Пианистка играет, пока включена музыка; нажатие на неё — перестаёт и сидит, сложив руки, второе — снова играет.
        private static readonly (string Place, string Clip, float X, float Y, float W, float H)[] ClubVideos =
        {
            ("club-pianist", "pianist", 1330f, 275f, 196.2f, 200f),
            // Бармен заново (2026-10-04): поставлен по листу в фартуке на пустой бар, ролики сняты с этого кадра.
            ("club-bartender", "bartender", 643.75f, 274.375f, 225f, 128.75f),
        };
        private readonly Dictionary<string, HubClubVideo> _clubVideos = new Dictionary<string, HubClubVideo>();

        /// <summary>Нажатие на жителя с роликом: остановить его или запустить снова.</summary>
        private void ToggleResident(string id)
        {
            if (_clubVideos.TryGetValue(id, out var video) && video != null) video.Stopped = !video.Stopped;
        }

        // Жесты жителей за столами (2026-10-06): игра, прямоугольник в единицах зала (печатает
        // scripts/club-live/prepare_table_clips.py); ролик лежит над живым слоем своего стола.
        // Решение автора (2026-10-07): живыми в зале остаются только бармен и пианистка — ролики столов отняли
        // слишком много времени и выходили неровно; вернуться после выхода игры. Готовые ролики «Дурака»
        // (433.75, 375, 227.5, 250.625) и «Сундучков» (1025.125, 381.875, 221.875, 249.375) отложены в
        // `screens/qa/table-videos-shelved/` вместе с .meta: вернуть файлы в Resources/UI/Hub/Video и строки сюда.
        private static readonly (string Game, float X, float Y, float W, float H)[] TableVideos =
        {
        };

        /// <summary>Ролики жителей поверх нарисованного зала — под живыми слоями столов, те ближе к нам.</summary>
        private void AddClubVideos(RectTransform floor, Func<float, float, Vector2> at, Vector2 unit)
        {
            foreach (var item in ClubVideos)
            {
                var video = AddClubVideo(floor, at, unit, item.Place, item.Clip, item.X, item.Y, item.W, item.H);
                if (video == null) continue;
                // Пианистка играет под музыку: музыку выключили — она перестаёт.
                if (item.Place == "club-pianist") video.Allowed = () => PlayerHubRuntime.Current?.Settings.Music ?? true;
            }
        }

        /// <summary>Жест жителей стола — над его живым слоем (зовётся сразу после <see cref="AddLiveTable"/>).</summary>
        private void AddTableVideo(RectTransform floor, CityPlace place, Func<float, float, Vector2> at, Vector2 unit)
        {
            foreach (var item in TableVideos)
                if (place.Route == "game:" + item.Game)
                    AddClubVideo(floor, at, unit, place.Id, item.Game, item.X, item.Y, item.W, item.H);
        }

        /// <summary>Ролик `UI/Hub/Video/club-<paramref name="clip"/>-*` по маске на месте (x, y, w, h) в единицах зала.</summary>
        private HubClubVideo AddClubVideo(RectTransform floor, Func<float, float, Vector2> at, Vector2 unit,
            string place, string clip, float x, float y, float w, float h)
        {
            VideoClip Clip(string kind) => Resources.Load<VideoClip>("UI/Hub/Video/club-" + clip + "-" + kind);
            var play = Clip("play");
            var mask = Resources.Load<Texture2D>("UI/Hub/Video/club-" + clip + "-mask");
            if (play == null || mask == null) return null;
            var rect = MakeRect(floor, "Video " + place, 0, 0, 0, 0);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = at(x, y);
            rect.sizeDelta = new Vector2(w * unit.x, h * unit.y);
            var video = rect.gameObject.AddComponent<HubClubVideo>();
            video.Setup(mask, play, Clip("idle"), Clip("stop"), Clip("start"),
                Resources.Load<TextAsset>("UI/Hub/Video/club-" + clip + "-entry")?.text);
            video.Viewport = _cityArea;
            // Редкий жест между петлями (бармен наливает себе и пробует, жители стола): ждёт случайные 8–28 видимых
            // секунд и начинается в конце петли; стык — 0,31 с: при 24 к/с это 8 одинаковых кадров в петле и в жесте.
            var action = Clip("action");
            if (action != null && !video.HasIdle) video.SetupVariation(action, 8, 28, 15f / 48f);
            _clubVideos[place] = video;
            return video;
        }

        /// <summary>
        /// Мебель здания поверх его картинки — детьми этажа <paramref name="floor"/> шириной <paramref name="width"/>:
        /// в зале Клуба по столу на каждое место-стол. Стол один на все игры (`UI/Hub/club-table`), сукно —
        /// отдельный серый слой (`club-table-felt`, в нём уже свет лампы: середина светлее), крашенный в цвет
        /// игры, как её раздел в меню. Под столом — тень на ковре, над сукном — тёплый отсвет лампы,
        /// под названием на полу — тёмное пятно ковра, чтобы золотые буквы читались. В живом первом зале
        /// столы нарисованы в картинке: остаются пятна под названиями и живые слои столов.
        /// </summary>
        private void AddInteriorFurniture(RectTransform floor, string art, float width)
        {
            if (art == SixRoom) AddSixRoom(floor, width);
            if (art == TavernHall) AddTavernHall(floor, width);
            if (art != ClubHall && art != ClubHall2) return;
            var table = Resources.Load<Sprite>("UI/Hub/club-table");
            var felt = Resources.Load<Sprite>("UI/Hub/club-table-felt");
            if (table == null) return;
            var glow = UiTextures.RadialGlow(256, Color.white);
            var picture = PictureSize(art);
            var unit = new Vector2(width / picture.x, CityHeight / picture.y);
            Vector2 At(float x, float y) => new Vector2((x / picture.x - .5f) * width, (.5f - y / picture.y) * CityHeight);
            void Glow(string name, float x, float y, float w, float h, Color color)
            {
                var image = MakeRect(floor, name, 0, 0, 0, 0).gameObject.AddComponent<Image>();
                image.rectTransform.anchorMin = image.rectTransform.anchorMax = image.rectTransform.pivot = new Vector2(.5f, .5f);
                image.rectTransform.anchoredPosition = At(x, y);
                image.rectTransform.sizeDelta = new Vector2(w * unit.x, h * unit.y);
                image.sprite = glow;
                image.color = color;
                image.raycastTarget = false;
            }
            var height = TableWidth * TableAspect;
            var floorY = art == ClubHall2 ? SecondTableFloorY : TableFloorY;
            var top = floorY - height * TableFeet;
            if (ClubHallPainted && art == ClubHall) AddClubVideos(floor, At, unit);
            foreach (var place in CityPlaces)
            {
                if (place.Art != art || !place.Id.StartsWith("table-", System.StringComparison.Ordinal)) continue;
                var x = place.Door.x;
                var painted = ClubHallPainted && art == ClubHall;
                // Тень стола и стульев в пятне света: лампа прямо над столом, пятно видно кольцом вокруг.
                if (!painted)
                    Glow("Shadow under " + place.Title, x, floorY, TableWidth * .95f, TableWidth * .22f, new Color(0, 0, 0, .75f));
                var live = painted && AddLiveTable(floor, place, At, unit);
                if (live) AddTableVideo(floor, place, At, unit);
                // Тёмный ковёр под названием — буквы видны сразу, а не теряются в узоре.
                if (!place.HoverOnly)
                    Glow("Name shade " + place.Title, x, FloorNameTop + FloorNameCap * .55f,
                        place.Title.Length * FloorNameCap * FloorNameLetter + FloorNameCap * 2.5f, FloorNameCap * 2.6f, new Color(0, 0, 0, .6f));
                var rect = MakeRect(floor, "Table " + place.Title, 0, 0, 0, 0);
                rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
                rect.pivot = new Vector2(.5f, 1);
                rect.anchoredPosition = At(x, top);
                rect.sizeDelta = new Vector2(TableWidth * unit.x, height * unit.y);
                var image = rect.gameObject.AddComponent<Image>();
                image.sprite = table;
                image.raycastTarget = false;
                // Под живым слоем картинка стола только держит место мебели, сама не видна.
                image.enabled = !live;
                if (felt == null) continue;
                var cloth = MakeRect(rect, "Felt", 0, 0, 0, 0).gameObject.AddComponent<Image>();
                Stretch(cloth.rectTransform);
                cloth.sprite = felt;
                cloth.raycastTarget = false;
                // Цвет раздела игры, приглушённый до сукна.
                var accent = place.HoverOnly ? new Color(.3f, .48f, .66f) : Accent(place.Route.Substring(5));
                cloth.color = new Color(accent.r * .7f, accent.g * .7f, accent.b * .7f);
                cloth.enabled = !live;
                if (live) continue;
                // Тёплый отсвет лампы на столешнице.
                var light = MakeRect(rect, "Lamp light", 0, 0, 0, 0).gameObject.AddComponent<Image>();
                light.rectTransform.anchorMin = light.rectTransform.anchorMax = light.rectTransform.pivot = new Vector2(.5f, 1);
                light.rectTransform.anchoredPosition = new Vector2(0, -height * .2f * unit.y);
                light.rectTransform.sizeDelta = new Vector2(TableWidth * .85f * unit.x, height * .42f * unit.y);
                light.sprite = glow;
                light.color = new Color(1, .86f, .55f, .28f);
                light.raycastTarget = false;
            }
            // Нарисованной доске генератор не дал тени на ковре — мягкая тень под ножками.
            if (art == ClubHall2)
                Glow("Shadow under games board", BoardX, BoardFeetY + 2, BoardHalf * 2.1f, BoardHalf * .4f, new Color(0, 0, 0, .45f));
        }

        /// <summary>Живой слой готового стола поверх нарисованного зала: жители и стол в двойной чёткости.</summary>
        private static bool AddLiveTable(RectTransform floor, CityPlace place, Func<float, float, Vector2> at, Vector2 unit)
        {
            if (place.Route == null || !place.Route.StartsWith("game:", StringComparison.Ordinal)) return false;
            var sprite = Resources.Load<Sprite>("UI/Hub/club-live-" + place.Route.Substring(5));
            if (sprite == null) return false;
            var rect = MakeRect(floor, "Live " + place.Title, 0, 0, 0, 0);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = at(place.Door.x - LiveSquare / 2, LiveTop);
            rect.sizeDelta = new Vector2(LiveSquare * unit.x, LiveSquare * unit.y);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            return true;
        }

        /// <summary>«Выйти» в зале — на главной, пока никуда не едем и карта полностью закрыта.</summary>
        private void UpdateInteriorControls()
        {
            // External home routes may return here without a new entrance animation.
            // Keep the usable card table visible, but never interrupt an outbound trip.
            if (_page == "home" && _cityLevel == FortuneLevel && !_busy && _state != null
                && _diveTarget < 0 && !_diveQueued && _visitPhase == 0 && !_gliding && !_floorPanning
                && !_cityDragging && !_floorDragging && !_mapOpen && _mapFade <= 0
                && _mapTripPlace < 0 && _mapTripDistrict < 0 && _tableTrip < 0
                && !GiftShown && !QuestsShown && !BankShown)
                Navigate("fortune");
            var show = _page == "home" && IsInterior(_cityLevel) && _diveTarget < 0 && _visitPhase == 0
                && !_mapOpen && _mapFade <= 0 && !GiftShown && !QuestsShown && !BankShown;
            if (_interiorControls == null)
            {
                if (!show) return;
                _interiorControls = StageLayer("Interior controls");
                PinToCorner((RectTransform)CityButton(_interiorControls, "ВЫЙТИ", 24, 24, 170, 56, () => LeaveInterior()).transform, -1);
            }
            if (show) _interiorControls.SetAsLastSibling();
            if (_interiorControls.gameObject.activeSelf != show) _interiorControls.gameObject.SetActive(show);
        }

        /// <summary>После чтения карты «Назад» выводит из шатра тем же переходом, что и из других зданий.</summary>
        private bool TryLeaveFortuneInterior()
        {
            if (_page != "fortune" || _cityLevel != FortuneLevel) return false;
            // Во время перехода не открываем историю поверх закрывающегося шатра.
            if (_diveTarget >= 0) return true;
            GoHome();
            MatchFortuneInteriorDim();
            StartDive(0);
            return true;
        }

        /// <summary>Из здания — на улицу, к его двери. Не в здании — ничего не делает.</summary>
        private bool LeaveInterior()
        {
            if (!IsInterior(_cityLevel) || _diveTarget >= 0 || _visitPhase != 0 || _mapOpen || _mapFade > 0 || GiftShown || QuestsShown || BankShown) return false;
            StartDive(0);
            return true;
        }
    }
}
