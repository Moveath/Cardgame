using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>
    /// Этажи города под главной площадью и внутри зданий — отдельные экраны: подполье (лестница на
    /// площади, на ПК S или ↓; одна картинка, не листается) и зал Клуба (PlayerHubInterior.cs).
    /// Обратно — нажать на их лестницу или двери, стрелкой к улице. Переход — «нырок» камерой: площадь
    /// приближается к лестнице, вокруг темнеет; в полной пелене склейка, и новое место отъезжает от своей
    /// лестницы на весь экран. Экрана загрузки нет: картинки грузятся в фоне вместе с городом.
    /// Королевского холма над площадью больше нет (решение автора, 2026-09-30): замок — просто вид,
    /// на щите его ворот — название города CARD CITY.
    /// </summary>
    public sealed partial class PlayerHubController
    {
        // Этажи: 0 — улица (лента районов), 1 — подполье, дальше — здания изнутри (PlayerHubInterior.cs):
        // 2 — зал Клуба, 3 — Дом Шестёрки, 4 — Банк Тузяры, 5 — шатёр гадалки, 6 — таверна «Червовая дама», 7 — Лавка Дамы бубен,
        // 8 — Архив, 9 — Рынок Короля бубен («Бубновые ряды»).
        private static readonly string[] FloorArt = { null, "UI/Hub/card-city-underground", "UI/Hub/club-hall", "UI/Hub/six-room", "UI/Hub/bank-hall", "UI/Hub/fortune-tent", "UI/Hub/tavern-hall", "UI/Hub/shop-counter", "UI/Hub/archive-hall", "UI/Hub/market-hall" };
        private static readonly string[] FloorNames = { null, "Underground", "Club hall", "Six house", "Bank hall", "Fortune tent", "Tavern hall", "Shop counter", "Archive hall", "Market hall" };
        // Район, откуда ведёт лестница вниз. Сами лестницы и двери — места с этажом в таблице CityPlaces
        // (PlayerHubPlaces.cs): их контур нажимается, дверь — точка нырка.
        private const string UndergroundFrom = "Главная площадь";
        // Нырок: секунды въезда в лестницу, секунды отъезда на новом месте, приближение в лестнице.
        private const float DiveIn = .55f, DiveOut = .8f, DiveZoom = 2.6f;
        // Лёгкий отсвет над лестницей: вниз — приглушённый зелёный подвала, наверх — тёплый уличный.
        // Слабый: свет даёт сама картинка.
        private static readonly Color DiveDown = new Color(.62f, .85f, .62f), DiveUp = new Color(1f, .86f, .62f);
        private const float DiveGlowAlpha = .2f;
        // Пелена склейки — тьма.
        private static readonly Color DiveDark = new Color(.01f, .02f, .02f);

        private RectTransform _cityRig;
        private readonly RectTransform[] _floors = new RectTransform[FloorArt.Length];
        private readonly ResourceRequest[] _floorLoads = new ResourceRequest[FloorArt.Length];
        private readonly float[] _floorWidths = new float[FloorArt.Length];
        private float _stairsPageX, _stairsPageWidth;
        private int _stairsStop = -1;
        private Image _diveShade, _diveVignette, _diveGlow;
        private static Sprite s_diveVignette;
        // Где игрок (этаж) и куда идёт нырок (−1 — не идёт); откуда он начался.
        private int _cityLevel, _diveTarget = -1, _diveFrom;
        private float _diveTime;
        // S нажата вдали от площади: сначала доехать до неё, потом нырнуть на _diveQueuedTo.
        private bool _diveQueued, _diveSwapped;
        private int _diveQueuedTo;
        private Vector2 _diveFocus;
        // Какая сторона была зажата в прошлом кадре: +1 — S/↓, −1 — W/↑, 0 — ничего.
        private int _cityVerticalHeld;

        /// <summary>Пелена и свет нырка поверх города; картинки этажей начинают грузиться в фоне.</summary>
        private void BuildDiveLayers(RectTransform background)
        {
            if (_stairsStop < 0) return;
            for (var level = 1; level < FloorArt.Length; level++) _floorLoads[level] = Resources.LoadAsync<Sprite>(FloorArt[level]);
            _diveShade = MakeRect(background, "Dive shade", 0, 0, 0, 0).gameObject.AddComponent<Image>();
            Stretch(_diveShade.rectTransform);
            _diveVignette = DiveLayer(background, "Dive vignette", DiveVignette());
            _diveGlow = DiveLayer(background, "Dive glow", UiTextures.RadialGlow(256, Color.white));
            _diveShade.raycastTarget = false;
            _diveShade.gameObject.SetActive(false);
        }

        private Image DiveLayer(RectTransform background, string name, Sprite sprite)
        {
            var image = MakeRect(background, name, 0, 0, 0, 0).gameObject.AddComponent<Image>();
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = image.rectTransform.pivot = new Vector2(.5f, .5f);
            image.sprite = sprite;
            image.raycastTarget = false;
            image.gameObject.SetActive(false);
            return image;
        }

        private RectTransform Floor(int level) => level > 0 && level < _floors.Length ? _floors[level] : null;

        /// <summary>Этажи, чьи картинки уже догрузились, собираются сразу — нырок потом не ждёт.</summary>
        private void PrepareFloors()
        {
            for (var level = 1; level < FloorArt.Length; level++)
                if (Floor(level) == null && _floorLoads[level] != null && _floorLoads[level].isDone) EnsureFloor(level);
        }

        /// <summary>Этаж готов к показу. Картинка ещё грузится — дождаться её (обычно давно готова).</summary>
        private bool EnsureFloor(int level)
        {
            if (level == 0 || Floor(level) != null) return true;
            var load = _floorLoads[level];
            if (load == null) return false;
            // asset до конца загрузки ждёт её: нырок не начнётся раньше, чем есть что показать.
            var sprite = load.asset as Sprite;
            if (sprite == null)
            {
                _floorLoads[level] = null;
                return false;
            }
            _floorWidths[level] = sprite.rect.width * CityHeight / sprite.rect.height;
            var floor = MakeRect(_cityRig, FloorNames[level], 0, 0, 0, 0);
            floor.anchorMin = floor.anchorMax = floor.pivot = new Vector2(.5f, .5f);
            floor.anchoredPosition = Vector2.zero;
            floor.sizeDelta = new Vector2(_floorWidths[level], CityHeight);
            if (sprite.name != ClubHall || !BuildClubPanorama(floor, sprite))
            {
                var artwork = floor;
                if (level == FortuneLevel)
                {
                    // The approved tent stays entirely visible. Match the native page around its margins.
                    var backing = MakeRect(floor, "Fortune interior backing", 0, 0, _floorWidths[level] * 10, CityHeight * 10);
                    backing.anchorMin = backing.anchorMax = backing.pivot = new Vector2(.5f, .5f);
                    backing.anchoredPosition = Vector2.zero;
                    var fill = backing.gameObject.AddComponent<Image>();
                    fill.color = new Color(.035f, .007f, .06f);
                    fill.raycastTarget = false;
                    artwork = MakeRect(floor, "Fortune interior art", 0, 0, 0, 0);
                    Stretch(artwork);
                }
                var art = artwork.gameObject.AddComponent<HubCityArt>();
                art.Art = sprite;
                art.raycastTarget = false;
                art.SetAllDirty();
                AddInteriorFurniture(floor, sprite.name, _floorWidths[level]);
                AddSigns(floor, sprite.name, _floorWidths[level]);
                RegisterPlacePage(sprite.name, floor, 0, _floorWidths[level]);
            }
            floor.gameObject.SetActive(_cityLevel == level);
            _floors[level] = floor;
            PlaceFloors();
            return true;
        }

        /// <summary>Шатёр совпадает с фоном страницы карт, остальные этажи закрывают экран целиком.</summary>
        private float FloorScale(int level)
        {
            var size = _cityArea.rect.size;
            if (level == FortuneLevel)
                return Mathf.Max(.0001f, FortuneBackdropRect(FortuneViewport, _floorWidths[level] / CityHeight).height
                    * _stage.localScale.x / CityHeight);
            var scale = Mathf.Max(size.x / _floorWidths[level], size.y / CityHeight, .0001f);
            // Bring club residents closer on a phone, as one scene: the art, live video,
            // table labels and hit areas all share this transform. Existing horizontal
            // camera bounds still allow the player to reach every table and the doorway.
            return scale * (level == FirstInterior ? ClubPhoneZoom : 1f);
        }

        private Vector2 FortuneFloorOffset(int level) => level == FortuneLevel
            ? FortuneBackdropRect(FortuneViewport, _floorWidths[level] / CityHeight).center * _stage.localScale.x
            : Vector2.zero;

        private void PlaceFloors()
        {
            for (var level = 1; level < FloorArt.Length; level++)
            {
                var floor = Floor(level);
                if (floor == null) continue;
                var scale = FloorScale(level);
                floor.localScale = new Vector3(scale, scale, 1);
                // Вид сдвинут только у этажа, где стоим; сдвиг не выходит за стены при любом экране.
                var limit = FloorPanLimit(level);
                if (level == _cityLevel)
                {
                    _floorPan = Mathf.Clamp(_floorPan, -limit, limit);
                    // После resize не продолжаем толкать камеру за новую границу.
                    if (_floorPan >= limit && _floorPanVelocity > 0 || _floorPan <= -limit && _floorPanVelocity < 0)
                        _floorPanVelocity = 0;
                    // После изменения размера окна цель тоже должна остаться внутри новых стен.
                    _floorPanTo = Mathf.Clamp(_floorPanTo, -limit, limit);
                }
                floor.anchoredPosition = new Vector2(level == _cityLevel ? -_floorPan * scale : 0, 0) + FortuneFloorOffset(level);
            }
            TrackClubHall();
        }

        /// <summary>Точка картинки (в долях) — на экране, от центра: на этаже — его, на улице — площади.</summary>
        private Vector2 CityScreenPoint(int level, Vector2 uv)
        {
            if (level != 0)
            {
                var scale = FloorScale(level);
                return new Vector2(((uv.x - .5f) * _floorWidths[level] - FloorPan(level)) * scale, (.5f - uv.y) * CityHeight * scale)
                    + FortuneFloorOffset(level);
            }
            var cityScale = CityScale();
            return new Vector2((_stairsPageX + uv.x * _stairsPageWidth - _cityCamera) * cityScale, (.5f - uv.y) * CityHeight * cityScale);
        }

        /// <summary>
        /// Место на этаже <paramref name="level"/>, что ведёт на <paramref name="other"/> (лестница, двери
        /// здания): его дверь — точка нырка. С этажа путь только на улицу.
        /// </summary>
        private static CityPlace DiveDoor(int level, int other)
        {
            foreach (var place in CityPlaces)
                if (place.Floor == (level == 0 ? other : 0) && PlaceLevel(place) == level) return place;
            return null;
        }

        /// <summary>Дверь нырка на экране, от центра; нет двери или её картинки — середина экрана.</summary>
        private Vector2 DiveFocus(int level, int other)
        {
            var door = DiveDoor(level, other);
            return door != null && _placePages.ContainsKey(door.Art) ? PlaceScreenPoint(door, door.Door) : FortuneFloorOffset(level);
        }

        /// <summary>Нажатие по городу (не свайп) — по месту под пальцем (PlayerHubPlaces.cs).</summary>
        private void ClickCity(PointerEventData data)
        {
            if (data.dragging || _page != "home" || _diveTarget >= 0 || _visitPhase != 0) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_cityArea, data.position, data.pressEventCamera, out var local);
            ClickPlace(local - _cityArea.rect.center);
        }

        /// <summary>
        /// Стрелки — этажи города: на улице S/↓ — подполье; из подполья W/↑ — к улице; из здания — S/↓
        /// или Esc. Нажатие ловится по смене «зажата» между кадрами.
        /// </summary>
        private void DiveKeys(Keyboard keyboard)
        {
            var inside = IsInterior(_cityLevel);
            var down = keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed || inside && keyboard.escapeKey.isPressed;
            var up = keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed;
            var vertical = down == up ? 0 : down ? 1 : -1;
            var pressed = vertical != 0 && vertical != _cityVerticalHeld;
            _cityVerticalHeld = vertical;
            if (!pressed || _stairsStop < 0) return;
            var target = _cityLevel == 0 ? (vertical > 0 ? 1 : -1)
                : _cityLevel == 1 && vertical < 0 || inside && vertical > 0 ? 0 : -1;
            if (target < 0) return;
            EventSystem.current?.SetSelectedGameObject(null);
            StartDive(target);
        }

        private void StartDive(int level)
        {
            if (_diveTarget >= 0 || _visitPhase != 0 || level == _cityLevel || _stairsStop < 0) return;
            // Между этажами — только через улицу.
            if (_cityLevel != 0 && level != 0) return;
            // С улицы — только от района, где вход (лестница и Клуб — на площади): город далеко —
            // сначала доехать до него.
            if (_cityLevel == 0)
            {
                var door = DiveDoor(0, level);
                var stop = door != null && _placePages.TryGetValue(door.Art, out var view) ? NearestStop(view.X + view.Width / 2) : _stairsStop;
                if (Mathf.Abs(_cityCamera - _cityStops[stop]) > .35f * CityView())
                {
                    ShowDistrict(stop);
                    _diveQueued = true;
                    _diveQueuedTo = level;
                    return;
                }
            }
            if (!EnsureFloor(level == 0 ? _cityLevel : level)) return;
            _diveQueued = false;
            _gliding = false;
            _glideKey = 0;
            _cityVelocity = 0;
            _cityDragging = false;
            _floorPanning = _floorDragging = false;
            _floorPanVelocity = 0;
            _diveFrom = _cityLevel;
            _diveTarget = level;
            _diveTime = 0;
            _diveSwapped = false;
            _diveFocus = DiveFocus(_cityLevel, level);
            _diveShade.gameObject.SetActive(true);
            _diveVignette.gameObject.SetActive(true);
            _diveGlow.gameObject.SetActive(true);
            FrameDive(0, false);
        }

        private void MatchFortuneInteriorDim()
        {
            if (_cityDim == null) return;
            // Native cards sit above the city dim. Keep the transition background equally bright.
            _cityDim.color = _cityLevel == FortuneLevel ? Color.clear : new Color(.05f, .03f, .14f, .12f);
        }

        /// <summary>Шаг нырка на <paramref name="deltaTime"/> секунд.</summary>
        private void MoveDive(float deltaTime)
        {
            if (_diveTarget < 0) return;
            _diveTime += deltaTime;
            if (_diveTime < DiveIn)
            {
                // С разгоном — будто шагнул на ступеньки и пошёл.
                var p = _diveTime / DiveIn;
                FrameDive(p * p, false);
                return;
            }
            if (!_diveSwapped)
            {
                // Полная пелена — склейка: другое место, приближенное на свою лестницу.
                _diveSwapped = true;
                _cityLevel = _diveTarget;
                if (_cityLevel == FortuneLevel || _diveFrom == FortuneLevel) MatchFortuneInteriorDim();
                // В здание входят через двери: вид — от них; на другие этажи и на улицу — с середины.
                _floorPan = _floorPanTo = 0;
                _floorPanning = _floorDragging = false;
                _floorPanVelocity = 0;
                _cityStrip.gameObject.SetActive(_cityLevel == 0);
                for (var level = 1; level < FloorArt.Length; level++)
                    if (Floor(level) != null) Floor(level).gameObject.SetActive(_cityLevel == level);
                // Комната Шестёрки — меню: вид с середины, где все предметы (дверь видна и так).
                if (IsInterior(_cityLevel) && _cityLevel != SixLevel && DiveDoor(_cityLevel, _diveFrom) is CityPlace entrance)
                    LookAtPlace(entrance);
                else PlaceFloors();
                _diveFocus = DiveFocus(_cityLevel, _diveFrom);
            }
            var q = Mathf.Clamp01((_diveTime - DiveIn) / DiveOut);
            if (q >= 1)
            {
                FrameDive(0, true);
                _diveShade.gameObject.SetActive(false);
                _diveVignette.gameObject.SetActive(false);
                _diveGlow.gameObject.SetActive(false);
                _diveTarget = -1;
                // Карты появляются уже внутри шатра, когда закончился проход через его вход.
                if (_cityLevel == FortuneLevel && _page == "home") Navigate("fortune");
                return;
            }
            // Отъезд с торможением: быстро от лестницы, мягко на весь вид.
            FrameDive((1 - q) * (1 - q) * (1 - q), true, q);
        }

        /// <summary>
        /// Камера нырка и въезда к двери: приближение <paramref name="zoom"/>, точка <paramref name="focus"/>
        /// (от центра экрана) к <paramref name="depth"/> = 1 доезжает до центра. Возвращает, где она на экране.
        /// </summary>
        private Vector2 AimCity(float zoom, float depth, Vector2 focus)
        {
            var offset = focus * (1 - depth) - focus * zoom;
            // Картинка не должна отъехать от края экрана: лестница в подполье у самого угла,
            // и камера упирается в край. Улица по ширине длинная — там только высота.
            var half = _cityArea.rect.size / 2;
            var scale = _cityLevel == 0 ? CityScale() : FloorScale(_cityLevel);
            var reach = new Vector2(_cityLevel == 0 ? 0 : _floorWidths[_cityLevel] * scale * zoom / 2 - half.x,
                CityHeight * scale * zoom / 2 - half.y);
            // Этаж, сдвинутый вбок, упирается в стены со своего места.
            var pan = FloorPan(_cityLevel) * scale * zoom;
            // The tent has deliberate margins and a backing: cover-only bounds would be negative.
            if (_cityLevel != FortuneLevel)
            {
                if (_cityLevel != 0) offset.x = Mathf.Clamp(offset.x, pan - reach.x, pan + reach.x);
                offset.y = Mathf.Clamp(offset.y, -reach.y, reach.y);
            }
            _cityRig.localScale = new Vector3(zoom, zoom, 1);
            _cityRig.anchoredPosition = offset;
            return focus * zoom + offset;
        }

        /// <summary>
        /// Кадр нырка: <paramref name="depth"/> 0 — обычный вид, 1 — камера в лестнице и полная пелена.
        /// Точка лестницы едет к центру экрана, пока камера приближает.
        /// </summary>
        private void FrameDive(float depth, bool arriving, float arrival = 0)
        {
            var screen = AimCity(1 + (DiveZoom - 1) * depth, depth, _diveFocus);
            var half = _cityArea.rect.size / 2;
            // Пелена сначала вокруг лестницы — как будто смотришь в проём, — и только в самом конце
            // закрывает и его: склейка в полной пелене.
            var veil = DiveDark;
            var dark = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.45f, 1, depth));
            // Внизу лампы загораются не сразу: пара коротких провалов света.
            if (arriving && _cityLevel == 1 && (arrival > .2f && arrival < .26f || arrival > .34f && arrival < .38f))
                dark = Mathf.Min(1, dark + .3f);
            _diveShade.color = new Color(veil.r, veil.g, veil.b, dark);
            // Кольцо пелены вокруг проёма: чем глубже, тем уже просвет. Картинка кольца всегда закрывает
            // экран — иначе за её краем было бы светло.
            var cover = 2 * Mathf.Max(Mathf.Abs(screen.x) + half.x, Mathf.Abs(screen.y) + half.y);
            var size = Mathf.Max(cover, _cityArea.rect.height * Mathf.Lerp(9, 2.6f, depth));
            _diveVignette.rectTransform.anchoredPosition = screen;
            _diveVignette.rectTransform.sizeDelta = new Vector2(size, size);
            _diveVignette.color = new Color(veil.r, veil.g, veil.b, Mathf.SmoothStep(0, 1, depth / .6f));
            var light = _diveTarget == 1 ? DiveDown : DiveUp;
            _diveGlow.color = new Color(light.r, light.g, light.b, DiveGlowAlpha * Mathf.Sin(Mathf.PI * depth));
            _diveGlow.rectTransform.anchoredPosition = screen;
            _diveGlow.rectTransform.sizeDelta = Vector2.one * _cityArea.rect.height * (.25f + .5f * depth);
        }

        /// <summary>
        /// Кольцо пелены для нырка: в середине прозрачно (до 4 % стороны), к 16 % стороны — сплошная
        /// пелена и дальше до края. Плавный градиент, поэтому хватает 256 точек.
        /// </summary>
        private static Sprite DiveVignette()
        {
            if (s_diveVignette != null) return s_diveVignette;
            const int size = 256;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Dive vignette" };
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var radius = new Vector2(x + .5f - size / 2f, y + .5f - size / 2f).magnitude / size;
                var alpha = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.04f, .16f, radius));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255));
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return s_diveVignette = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f));
        }
    }
}
