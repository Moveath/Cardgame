using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>
    /// Card City за меню — одна панорама из кусков слева направо: районы (остановки) и мосты между
    /// ними — дорисованные переходы, которые краями вливаются в соседей, так что улица идёт без шва.
    /// Старт на главной площади. На главной город листается свайпом: идёт за пальцем, после отпускания
    /// катится по инерции и мягко тормозит; остановился рядом с районом — доезжает до его середины.
    /// На ПК — A/D и стрелки: плавный проезд к соседнему району, зажатая клавиша ведёт дальше.
    /// За крайними районами — окраины в дымке: заглянуть можно, дойти нельзя. Вся лента — в пикселях
    /// картинок и масштабируется целиком, поэтому стыки держатся на экране любой ширины.
    /// Места нажимаются и подсвечиваются контуром (PlayerHubPlaces.cs), лестницы и замок ведут
    /// на этажи (PlayerHubUnderground.cs), у мест — вывески (PlayerHubSigns.cs). Нет ни одной
    /// картинки — фон остаётся градиентом с мастями.
    /// </summary>
    public sealed partial class PlayerHubController
    {
        // Лента слева направо. Title есть — район-остановка, нет — мост или конец города. Overlap —
        // сколько пикселей кусок заходит на предыдущий и плавно в него вливается. Новый район —
        // картинка, мост к нему (дорисовка заготовки: край | пусто | край) и две строки здесь.
        // Город перерисован в новом стиле (2026-09-29): от вокзала до моря за маяком порта.
        private static readonly (string Art, string Title, float Overlap)[] CityPieces =
        {
            ("UI/Hub/city-edge-left", null, 0),
            ("UI/Hub/card-city-old-town", "Старый город", 240),
            ("UI/Hub/city-bridge-oldtown-square", null, 240),
            ("UI/Hub/card-city", "Главная площадь", 240),
            ("UI/Hub/city-bridge-square-tuzyara", null, 240),
            ("UI/Hub/card-city-tuzyara", "Район Тузяры", 240),
            ("UI/Hub/city-bridge-tuzyara-port", null, 240),
            ("UI/Hub/card-city-port", "Порт", 240),
            ("UI/Hub/city-edge-right", null, 240),
        };

        // С этого района город начинается при входе в игру.
        private const string CityStart = "Главная площадь";
        // Все куски приводятся к этой высоте, а она — к высоте экрана.
        private const float CityHeight = 941;
        // Насколько резинка уводит камеру за крайний район самое большее (в ширинах района — так на
        // широком экране за нарисованный конец не выглядывает зеркало): зеркальной окраины — треть,
        // она уходит в дымку за эту долю своей ширины; нарисованного конца города (вокзал, море) — больше.
        private const float OutskirtsGlimpse = .3f;
        private const float OutskirtsHaze = .34f;
        private const float CityEndGlimpse = .55f;
        // Без моста соседние районы просто заходят друг на друга на эту долю ширины.
        private const float DistrictOverlap = .22f;
        // Проезд к району: столько секунд плюс столько на каждый экран пути — от соседа около 1,35 с.
        private const float GlideBase = .6f, GlidePerScreen = .5f;
        // После отпускания город прокатывается примерно на скорость × Coast; скорость — не больше
        // FlingLimit экранов в секунду, чтобы сильный взмах не проносил через весь город.
        private const float Coast = .4f, FlingLimit = 2.2f;
        // Остановился ближе этого (в экранах) к середине района — доезжает до неё.
        private const float DistrictMagnet = .3f;

        private readonly List<string> _districtTitles = new List<string>();
        private float[] _cityStops = new float[0];
        private RectTransform _cityArea, _cityStrip;
        private float _cityWidth, _cityGlimpseLeft, _cityGlimpseRight;
        // Район, к которому город едет или у которого стоит (ближайший к цели).
        private int _district;
        // Положение и скорость «камеры» — в пикселях картинок.
        private float _cityCamera, _cityVelocity, _cityPlaced = float.NaN;
        private Vector2 _cityPlacedSize;
        private bool _cityDragging;
        private float _dragFrom, _dragStartX, _dragLastX, _dragLastTime, _dragSpeed;
        // Текущий проезд: откуда, куда, с какой начальной скоростью, сколько длится и прошло.
        // _glideKey — куда ведёт зажатая клавиша (0 — проезд не с клавиатуры).
        private bool _gliding;
        private float _glideFrom, _glideTo, _glideSpeed, _glideLength, _glideTime;
        private int _glideKey;
        // Какая сторона была зажата в прошлом кадре: +1 — D/→, −1 — A/←, 0 — ничего.
        private int _cityKeyHeld;

        private bool BuildCityBackdrop(RectTransform background)
        {
            var pieces = new List<(Sprite Art, string Title, float Overlap, float Width)>();
            foreach (var (art, title, overlap) in CityPieces)
            {
                var sprite = Resources.Load<Sprite>(art);
                if (sprite != null) pieces.Add((sprite, title, overlap, sprite.rect.width * CityHeight / sprite.rect.height));
            }
            if (!pieces.Exists(piece => piece.Title != null)) return false;

            _cityArea = background;
            // «Камера» нырка в подполье: приближает улицу и подполье целиком; в покое — ровно экран.
            _cityRig = MakeRect(background, "City camera", 0, 0, 0, 0);
            Stretch(_cityRig);
            _cityRig.pivot = new Vector2(.5f, .5f);
            _cityRig.anchoredPosition = Vector2.zero;
            // Лента: точка (0, середина) — у центра экрана; её сдвиг и масштаб задаёт PlaceCity.
            _cityStrip = MakeRect(_cityRig, "Card City", 0, 0, 0, CityHeight);
            _cityStrip.anchorMin = _cityStrip.anchorMax = _cityStrip.pivot = new Vector2(.5f, .5f);
            var first = pieces[0];
            var last = pieces[pieces.Count - 1];
            // Ширина района — мера резинки у концов города; концы и мосты бывают уже.
            _cityWidth = pieces.Find(piece => piece.Title != null).Width;
            // Окраины — край крайнего куска зеркально: зеркало продолжает картинку без шва и не занимает
            // памяти (та же текстура). Дымка густеет от города и полной становится там, где кончается резинка.
            var stops = new List<float>();
            var x = 0f;
            var previousTitle = (string)null;
            for (var i = 0; i < pieces.Count; i++)
            {
                var piece = pieces[i];
                // Мост пропал — районы заходят друг на друга и смешиваются сами.
                var overlap = i == 0 ? 0 : piece.Title != null && previousTitle != null ? piece.Width * DistrictOverlap : piece.Overlap;
                x -= overlap;
                if (i == 0) CityPage(piece.Art, "Outskirts", -piece.Width, piece.Width, true, 0, -OutskirtsHaze);
                CityPage(piece.Art, piece.Title ?? piece.Art.name, x, piece.Width, false, overlap / piece.Width, 0);
                if (piece.Title != null)
                {
                    stops.Add(x + piece.Width / 2);
                    _districtTitles.Add(piece.Title);
                }
                if (piece.Title == UndergroundFrom)
                {
                    _stairsStop = stops.Count - 1;
                    _stairsPageX = x;
                    _stairsPageWidth = piece.Width;
                }
                previousTitle = piece.Title;
                x += piece.Width;
            }
            CityPage(last.Art, "Outskirts", x, last.Width, true, 0, OutskirtsHaze);
            _cityStops = stops.ToArray();
            _cityGlimpseLeft = first.Title == null ? CityEndGlimpse : OutskirtsGlimpse;
            _cityGlimpseRight = last.Title == null ? CityEndGlimpse : OutskirtsGlimpse;
            _district = Mathf.Max(0, _districtTitles.IndexOf(CityStart));
            _cityCamera = _cityStops[_district];
            PlaceCity();

            _cityDim = MakeRect(background, "City dim", 0, 0, 0, 0).gameObject.AddComponent<Image>();
            Stretch(_cityDim.rectTransform);
            _cityDim.raycastTarget = false;
            BuildDiveLayers(background);
            if (_cityStops.Length > 1 || _stairsStop >= 0)
            {
                var swipe = MakeRect(background, "City swipe", 0, 0, 0, 0).gameObject.AddComponent<Image>();
                Stretch(swipe.rectTransform);
                swipe.color = new Color(0, 0, 0, 0);
                swipe.canvasRenderer.cullTransparentMesh = true;
                var catcher = swipe.gameObject.AddComponent<HubCitySwipe>();
                catcher.Began = BeginCityDrag;
                catcher.Moved = DragCity;
                catcher.Ended = EndCityDrag;
                catcher.Clicked = ClickCity;
                _citySwipe = catcher;
            }
            return true;
        }

        /// <summary>Кусок ленты: левый край <paramref name="x"/> и ширина — в пикселях картинок.</summary>
        private void CityPage(Sprite sprite, string name, float x, float width, bool mirrored, float fadeLeft, float hazeReach)
        {
            var page = MakeRect(_cityStrip, name, 0, 0, 0, 0);
            page.anchorMin = page.anchorMax = new Vector2(.5f, .5f);
            page.pivot = new Vector2(0, .5f);
            page.anchoredPosition = new Vector2(x, 0);
            page.sizeDelta = new Vector2(width, CityHeight);
            var art = page.gameObject.AddComponent<HubCityArt>();
            art.Street = true;
            art.Art = sprite;
            art.Mirrored = mirrored;
            art.FadeLeft = fadeLeft;
            art.HazeReach = hazeReach;
            art.raycastTarget = false;
            art.SetAllDirty();
            // В зеркальной окраине вывесок и мест нет — надписи читались бы задом наперёд.
            if (mirrored) return;
            AddSigns(page, sprite.name, width);
            RegisterPlacePage(sprite.name, page, x, width);
        }

        /// <summary>Клавиши на главной и движение города — каждый кадр.</summary>
        private void UpdateCity(bool typing)
        {
            if (_cityStrip == null) return;
            // Отпускание мыши или пальца может не дойти (окно потеряло фокус посреди свайпа). Тогда город
            // считался бы «в руке»: не слушал клавиши и не доезжал. Ничего не нажато — свайп окончен.
            var pointer = Pointer.current;
            if (_cityDragging && pointer != null && !pointer.press.isPressed) EndCityDrag(null);
            if (_floorDragging && pointer != null && !pointer.press.isPressed) EndFloorDrag();
            PrepareFloors();
            // Под открытой картой город стоит: клавиши — карте (PlayerHubMap.cs).
            if (_page == "home" && !typing && !_cityDragging && !_mapOpen && _mapFade <= 0 && !GiftShown && !QuestsShown && !BankShown && !ShopCardShown && !ArchiveShown && !MarketShown)
                CityKeys(Keyboard.current);
            ClubKeys(typing);
            UpdateMap(Time.unscaledDeltaTime, typing);
            UpdateSixHouse(Time.unscaledDeltaTime);
            UpdateTavern(Time.unscaledDeltaTime);
            UpdateSixGift(Time.unscaledDeltaTime, typing);
            UpdateSixQuests(Time.unscaledDeltaTime, typing);
            UpdateBankLoan(Time.unscaledDeltaTime, typing);
            ContinueTableTrip();
            ContinueMapTrip();
            MoveCity(Time.unscaledDeltaTime);
            MoveDive(Time.unscaledDeltaTime);
            MoveFloorPan(Time.unscaledDeltaTime);
            MoveVisit(Time.unscaledDeltaTime);
            UpdatePlaceHover(Time.unscaledDeltaTime);
            UpdateInteriorControls();
        }

        private void CityKeys(Keyboard keyboard)
        {
            // Закрытие меняет фазу, поэтому удержание Esc не вызывает повторных действий.
            if (keyboard != null && OutdoorVisit && _visitPhase == 2 && keyboard.escapeKey.isPressed)
            {
                CloseOutdoorVisit();
                return;
            }
            // У двери места клавиши ждут: камера уже едет.
            if (keyboard == null || _visitPhase != 0) return;
            DiveKeys(keyboard);
            // Нажатие ловим сами — клавиша зажата сейчас, а в прошлом кадре нет. wasPressedThisFrame
            // бывает глух: зависит от того, кто спросил его первым и какое обновление ввода прошло последним.
            var right = keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed;
            var left = keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed;
            var held = right == left ? 0 : right ? 1 : -1;
            var pressed = held != 0 && held != _cityKeyHeld;
            _cityKeyHeld = held;
            // Зал шире экрана — A/D и стрелки ведут его вбок, как улицу: от зала к залу, удержание — без остановок.
            if (IsInterior(_cityLevel) && _diveTarget < 0)
            {
                if (pressed) StepFloor(held);
                else if (held != 0 && held == _floorGlideKey && _floorPanning && _floorGlideTime > .7f * _floorGlideLength) StepFloor(held);
                return;
            }
            // В подполье и во время нырка улица не листается.
            if (_cityStops.Length < 2 || _cityLevel != 0 || _diveTarget >= 0) return;
            if (pressed)
            {
                // Стрелки ещё и переводят выделение между кнопками — снимаем его, чтобы кнопки не мигали.
                EventSystem.current?.SetSelectedGameObject(null);
                StepCity(held, false);
            }
            // Клавишу держат — город не встаёт у каждого района, а без остановки едет к следующему.
            else if (held != 0 && held == _glideKey && _gliding && _glideTime > .7f * _glideLength) StepCity(held, true);
        }

        /// <summary>Проезд к соседнему району в сторону <paramref name="step"/> (+1 вправо, −1 влево).</summary>
        private void StepCity(int step, bool held)
        {
            _diveQueued = false;
            var view = CityView();
            var from = _gliding ? _glideTo : _cityCamera;
            var next = -1;
            for (var i = 0; i < _cityStops.Length; i++)
                if (step * (_cityStops[i] - from) > .05f * view
                    && (next < 0 || Mathf.Abs(_cityStops[i] - from) < Mathf.Abs(_cityStops[next] - from))) next = i;
            if (next < 0)
            {
                // Дальше города нет — лёгкий толчок к краю и назад: видно, что клавиша сработала.
                if (!held && !_gliding)
                {
                    _cityVelocity = step * .8f * view;
                    GlideCity(_cityCamera, .55f, true);
                }
                _glideKey = 0;
                return;
            }
            _district = next;
            GlideCity(_cityStops[next], GlideBase + GlidePerScreen * Mathf.Abs(_cityStops[next] - _cityCamera) / view);
            _glideKey = step;
        }

        private void ShowDistrict(int index)
        {
            if (_cityStrip == null) return;
            _district = Mathf.Clamp(index, 0, _cityStops.Length - 1);
            GlideCity(_cityStops[_district], GlideBase + GlidePerScreen * Mathf.Abs(_cityStops[_district] - _cityCamera) / CityView());
        }

        /// <summary>
        /// Плавный проезд в <paramref name="to"/>: город трогается с той скоростью, с которой уже ехал
        /// (без рывка), и мягко тормозит к цели. Встречная скорость гасится, попутная — не даёт проскочить.
        /// <paramref name="bounce"/> — скорость сохраняется любая: толчок от края и возврат.
        /// </summary>
        private void GlideCity(float to, float duration, bool bounce = false)
        {
            var distance = to - _cityCamera;
            var speed = _cityVelocity;
            if (!bounce)
            {
                if (speed * distance <= 0) speed = 0;
                else duration = Mathf.Min(duration, Mathf.Max(.25f, 2.5f * distance / speed));
            }
            _glideFrom = _cityCamera;
            _glideTo = to;
            _glideSpeed = speed;
            _glideLength = Mathf.Max(.05f, duration);
            _glideTime = 0;
            _glideKey = 0;
            _gliding = true;
        }

        /// <summary>Шаг проезда на <paramref name="deltaTime"/> секунд: кубическая кривая Эрмита.</summary>
        private void MoveCity(float deltaTime)
        {
            if (_gliding && !_cityDragging)
            {
                _glideTime += deltaTime;
                var s = Mathf.Clamp01(_glideTime / _glideLength);
                if (s >= 1)
                {
                    _gliding = false;
                    _glideKey = 0;
                    _cityCamera = _glideTo;
                    _cityVelocity = 0;
                    // Доехали до площади по S или W — теперь на этаж.
                    if (_diveQueued) StartDive(_diveQueuedTo);
                }
                else
                {
                    var s2 = s * s;
                    var s3 = s2 * s;
                    _cityCamera = (2 * s3 - 3 * s2 + 1) * _glideFrom + (s3 - 2 * s2 + s) * _glideLength * _glideSpeed
                        + (3 * s2 - 2 * s3) * _glideTo;
                    _cityVelocity = 6 * (s - s2) * (_glideTo - _glideFrom) / _glideLength + (3 * s2 - 4 * s + 1) * _glideSpeed;
                }
            }
            PlaceCity();
        }

        /// <summary>Сразу закончить проезд: город встаёт туда, куда ехал.</summary>
        private void SettleCity()
        {
            if (_gliding) _cityCamera = _glideTo;
            _gliding = false;
            _glideKey = 0;
            _cityVelocity = 0;
            PlaceCity();
        }

        /// <summary>
        /// Масштаб: город — по высоте экрана, верх и низ картинок видны целиком. Экран шире района
        /// (телефоны 19,5:9–21:9) — по бокам видно начало мостов, лента сплошная, поэтому полей нет.
        /// </summary>
        private float CityScale() => Mathf.Max(_cityArea.rect.height / CityHeight, .0001f);

        /// <summary>Ширина экрана в пикселях картинок.</summary>
        private float CityView() => _cityArea.rect.width / CityScale();

        /// <summary>Ставит ленту так, чтобы точка <see cref="_cityCamera"/> была в центре экрана.</summary>
        private void PlaceCity()
        {
            if (_cityStrip == null) return;
            var size = _cityArea.rect.size;
            if (_cityCamera == _cityPlaced && size == _cityPlacedSize) return;
            _cityPlaced = _cityCamera;
            _cityPlacedSize = size;
            var scale = CityScale();
            _cityStrip.localScale = new Vector3(scale, scale, 1);
            _cityStrip.anchoredPosition = new Vector2(-_cityCamera * scale, 0);
            PlaceFloors();
        }

        private void BeginCityDrag(PointerEventData data)
        {
            // В зале палец ведёт сам зал, а не улицу (PlayerHubInterior.cs).
            if (IsInterior(_cityLevel))
            {
                BeginFloorDrag(data);
                return;
            }
            if (_page != "home" || _cityStops.Length < 2 || _cityLevel != 0 || _diveTarget >= 0 || _visitPhase != 0) return;
            _diveQueued = false;
            // Город ловится на ходу: проезд обрывается там, где его взяли.
            _cityDragging = true;
            _gliding = false;
            _glideKey = 0;
            _dragFrom = _cityCamera;
            _dragStartX = _dragLastX = CityX(data);
            _dragLastTime = Time.unscaledTime;
            _dragSpeed = 0;
        }

        private void DragCity(PointerEventData data)
        {
            DragFloor(data);
            if (!_cityDragging) return;
            var x = CityX(data);
            var elapsed = Time.unscaledTime - _dragLastTime;
            if (elapsed > 0) _dragSpeed = Mathf.Lerp(_dragSpeed, (_dragLastX - x) / elapsed, .6f);
            _dragLastX = x;
            _dragLastTime = Time.unscaledTime;
            var camera = _dragFrom + (_dragStartX - x);
            // За крайним районом город тянется туго, как резинка, и возвращается обратно: конец города
            // видно, но не дальше предела, как ни тяни.
            var first = _cityStops[0];
            var last = _cityStops[_cityStops.Length - 1];
            if (camera < first) camera = first - Overscroll((first - camera) / _cityWidth, _cityGlimpseLeft) * _cityWidth;
            else if (camera > last) camera = last + Overscroll((camera - last) / _cityWidth, _cityGlimpseRight) * _cityWidth;
            _cityCamera = camera;
            PlaceCity();
        }

        private void EndCityDrag(PointerEventData data)
        {
            EndFloorDrag();
            if (!_cityDragging) return;
            _cityDragging = false;
            // Палец замер перед отпусканием — город не катится; иначе прокатывается по инерции.
            var view = CityView();
            _cityVelocity = Time.unscaledTime - _dragLastTime < .12f
                ? Mathf.Clamp(_dragSpeed, -FlingLimit * view, FlingLimit * view) : 0;
            var rest = Mathf.Clamp(_cityCamera + _cityVelocity * Coast, _cityStops[0], _cityStops[_cityStops.Length - 1]);
            var nearest = NearestStop(rest);
            if (Mathf.Abs(_cityStops[nearest] - rest) < DistrictMagnet * view) rest = _cityStops[nearest];
            _district = nearest;
            GlideCity(rest, Mathf.Min(1.4f, .45f + .5f * Mathf.Abs(rest - _cityCamera) / view));
        }

        private int NearestStop(float camera)
        {
            var nearest = 0;
            for (var i = 1; i < _cityStops.Length; i++)
                if (Mathf.Abs(_cityStops[i] - camera) < Mathf.Abs(_cityStops[nearest] - camera)) nearest = i;
            return nearest;
        }

        /// <summary>Сколько видно за краем (в ширинах экрана) при перетягивании на <paramref name="excess"/>.</summary>
        private static float Overscroll(float excess, float glimpse) => glimpse * (1 - Mathf.Exp(-1.5f * excess));

        /// <summary>Положение пальца в пикселях картинок.</summary>
        private float CityX(PointerEventData data)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_cityArea, data.position, data.pressEventCamera, out var local);
            return local.x / CityScale();
        }
    }
}
