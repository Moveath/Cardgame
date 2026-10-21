using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>
    /// Карта города (автор, 2026-10-05): кнопка в углу главной — пять карт-районов из настоящих картинок города
    /// (Старый город, Главная площадь, Район Тузяры, Порт, Подполье) с меткой «Сейчас здесь». Нажал карту — она
    /// переворачивается и крупно встаёт посередине: на обороте рубашка и кнопки «Весь район» и мест района.
    /// Нажал кнопку — карта закрывается, камера едет туда готовыми движениями города (нырок на улицу, проезд,
    /// подход к двери или нырок в Клуб и подполье), как поездка к столу. Рисованную карту сверху автор отверг:
    /// на телефоне 16 мест выходят по 4–5 мм, а генератор путает места и лица.
    /// </summary>
    public sealed partial class PlayerHubController
    {
        private sealed class MapDistrict
        {
            public string Title, Suit, Art;
            public Color SuitColor;
            // Остановка на улице (номер в _cityStops) или этаж — для подполья.
            public int Stop = -1, Level;
            public readonly List<int> Places = new List<int>();
            public RectTransform Card;
        }

        // Масти районов: Старый город — черви (таверна), площадь — пики (замок над Клубом),
        // район Тузяры — трефы, порт — бубны (рынок Короля бубен); подполье — Джокер.
        private static readonly Dictionary<string, (string Suit, bool Red)> MapSuits = new Dictionary<string, (string, bool)>
        {
            { "Старый город", ("♥", true) }, { "Главная площадь", ("♠", false) },
            { "Район Тузяры", ("♣", false) }, { "Порт", ("♦", true) },
        };
        private const string MapUnderground = "Подполье";
        // Карты в ряд, оборот — посередине; всё в координатах сцены 1600×900. Кнопки оборота высотой 88:
        // на телефоне это около 6 мм — в них попадает палец.
        private const float MapCardW = 288, MapCardH = 412, MapCardGap = 26, MapCardsTop = 232;
        private const float MapBackW = 560, MapBackH = 740, MapBackTop = 112, MapRowH = 88, MapRowGap = 14;
        private const float MapFade = .2f, MapFlipSeconds = .32f, MapTripGlide = 1.8f;
        private static readonly Color MapFace = new Color(.035f, .105f, .09f);

        private readonly List<MapDistrict> _mapDistricts = new List<MapDistrict>();
        private RectTransform _mapLayer, _mapBack, _mapPin, _mapButtonLayer, _mapHeading, _mapHint;
        private CanvasGroup _mapGroup;
        private bool _mapOpen, _mapEscapeHeld, _mapKeyHeld;
        private float _mapFade, _mapFlip;
        // Чей оборот открыт (−1 — карты в ряд) и чья карта переворачивается.
        private int _mapBackFor = -1, _mapFlipCard = -1;
        // Поездка с карты: место (номер в CityPlaces) или район (номер в _mapDistricts); −1 — нет.
        private int _mapTripPlace = -1, _mapTripDistrict = -1;

        public bool MapOpen => _mapOpen;

        /// <summary>Районы карты: остановки улицы и подполье, у каждого — его места слева направо.</summary>
        private void BuildMapDistricts()
        {
            _mapDistricts.Clear();
            var stop = 0;
            foreach (var (art, title, _) in CityPieces)
            {
                if (title == null) continue;
                var suit = MapSuits.TryGetValue(title, out var found) ? found : ("♠", false);
                _mapDistricts.Add(new MapDistrict
                {
                    Title = title, Art = art, Suit = suit.Item1, Stop = stop++,
                    SuitColor = suit.Item2 ? new Color(.96f, .4f, .34f) : new Color(.94f, .77f, .46f),
                });
            }
            if (_stairsStop >= 0)
                _mapDistricts.Add(new MapDistrict { Title = MapUnderground, Art = FloorArt[1], Suit = "J", Level = 1,
                    SuitColor = new Color(.76f, .57f, .94f) });
            for (var i = 0; i < CityPlaces.Length; i++)
            {
                var place = CityPlaces[i];
                if (MapPlaceName(place) == null) continue;
                var level = PlaceLevel(place);
                var district = _mapDistricts.Find(item => item.Level == level && item.Art.EndsWith("/" + place.Art, System.StringComparison.Ordinal));
                district?.Places.Add(i);
            }
            foreach (var district in _mapDistricts) district.Places.Sort((a, b) => CityPlaces[a].Door.x.CompareTo(CityPlaces[b].Door.x));
        }

        /// <summary>Название места на карте: места с экраном и двери Клуба; лестницы и жители — нет.</summary>
        private static string MapPlaceName(CityPlace place)
        {
            if (place.Id == "club") return "Клуб";
            if (place.Title == null || place.HoverOnly || place.Resident || place.HallPassage || IsInterior(PlaceLevel(place))) return null;
            return place.Title;
        }

        /// <summary>
        /// Кнопка карты в углу — на главной, на улице, в подполье и в зале: значок `UI/Hub/map-icon` (сложенная
        /// карта с булавкой-пикой, ChatGPT, 2026-10-05) без подписи; нет значка — табличка «КАРТА».
        /// </summary>
        private void BuildMapButton()
        {
            if (_mapButtonLayer != null || _cityStrip == null) return;
            BuildMapDistricts();
            _mapButtonLayer = StageLayer("City map button");
            var icon = Resources.Load<Sprite>("UI/Hub/map-icon");
            if (icon == null)
            {
                CityButton(_mapButtonLayer, "КАРТА", 24, 820, 170, 56, OpenMap);
                return;
            }
            const float size = 192, left = 24, top = 900 - 24 - size;
            // Мягкая тень вокруг значка: за ним яркие дома, без неё он в них теряется.
            var glow = MakeRect(_mapButtonLayer, "Map icon shade", left - 20, top - 24, size + 40, size + 52)
                .gameObject.AddComponent<Image>();
            glow.sprite = UiTextures.RadialGlow(256, Color.white);
            glow.color = new Color(0, 0, 0, .65f);
            glow.raycastTarget = false;
            var rect = MakeRect(_mapButtonLayer, "КАРТА", left, top, size, size);
            // Опора в центре: нажатый значок проседает к своей середине.
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(left + size / 2, -top - size / 2);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = icon;
            image.preserveAspect = true;
            var shadow = rect.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, .55f);
            shadow.effectDistance = new Vector2(2, -4);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            // Под мышью значок светлеет, нажатый — темнеет (как кнопки-вывески).
            colors.normalColor = new Color(.9f, .9f, .9f);
            colors.highlightedColor = Color.white;
            colors.selectedColor = new Color(.9f, .9f, .9f);
            colors.pressedColor = new Color(.7f, .7f, .7f);
            button.colors = colors;
            button.onClick.AddListener(OpenMap);
            rect.gameObject.AddComponent<HubPress>();
        }

        /// <summary>Кнопка — пока игрок стоит на главной: не во время нырка, подхода к двери и не на страницах.</summary>
        private void RefreshMapButton()
        {
            if (_mapButtonLayer == null) return;
            // На широком экране край сцены 1600x900 не совпадает с краем окна.
            var size = ((RectTransform)transform).rect.size / Mathf.Max(.01f, _stage.localScale.x);
            _mapButtonLayer.anchoredPosition = new Vector2((1600 - size.x) / 2, (900 - size.y) / 2);
            var button = !_mapOpen && !GiftShown && !QuestsShown && !BankShown && !ShopCardShown && !ArchiveShown && !MarketShown && _page == "home" && _diveTarget < 0 && _visitPhase == 0;
            if (_mapButtonLayer.gameObject.activeSelf == button) return;
            _mapButtonLayer.gameObject.SetActive(button);
            if (button) _mapButtonLayer.SetAsLastSibling();
        }

        private void OpenMap()
        {
            if (_mapOpen || GiftShown || QuestsShown || BankShown || ShopCardShown || ArchiveShown || MarketShown || _page != "home" || _diveTarget >= 0 || _visitPhase == 1 || _visitPhase == 3) return;
            if (_mapLayer == null) BuildMap();
            _mapOpen = true;
            _mapBackFor = _mapFlipCard = -1;
            _mapFlip = 0;
            _mapLayer.gameObject.SetActive(true);
            _mapLayer.SetAsLastSibling();
            UpdateInteriorControls();
            PlaceMapPin();
            PaintMap();
        }

        private void CloseMap()
        {
            if (!_mapOpen) return;
            _mapOpen = false;
        }

        private void BuildMap()
        {
            _mapLayer = StageLayer("City map");
            _mapGroup = _mapLayer.gameObject.AddComponent<CanvasGroup>();
            // Затемнение шире сцены (вытянутый экран) и ловит нажатия: город под картой не листается.
            var dim = MakeRect(_mapLayer, "Map dim", -1400, -700, 4400, 2300).gameObject.AddComponent<Image>();
            dim.color = new Color(.02f, .015f, .05f, .82f);
            var dimButton = dim.gameObject.AddComponent<Button>();
            dimButton.transition = Selectable.Transition.None;
            dimButton.onClick.AddListener(() => { if (_mapBackFor >= 0) _mapBackFor = -1; });
            _mapHeading = MapPlaque(_mapLayer, "КАРТА ГОРОДА", 560, 96, 480, 76);
            PinToCorner((RectTransform)CityButton(_mapLayer, "ЗАКРЫТЬ", 1600 - 24 - 210, 24, 210, 56, CloseMap).transform, 1);
            var hint = MakeLabel(_mapLayer, "Нажми на район", 0, MapCardsTop + MapCardH + 28, 1600, 44, 30);
            _mapHint = hint.rectTransform;
            var font = s_signNarrow != null ? s_signNarrow : s_signNarrow = Resources.Load<Font>("Fonts/SignNarrow");
            hint.font = font != null ? font : _font;
            hint.alignment = TextAnchor.MiddleCenter;
            hint.verticalOverflow = VerticalWrapMode.Overflow;
            var hintLook = hint.gameObject.AddComponent<HubSignText>();
            hintLook.Top = SignGoldTop;
            hintLook.Bottom = SignGoldBottom;
            var hintShadow = hint.gameObject.AddComponent<Shadow>();
            hintShadow.effectColor = new Color(.06f, .03f, 0, .9f);
            hintShadow.effectDistance = new Vector2(1, -2);
            var left = (1600 - _mapDistricts.Count * MapCardW - (_mapDistricts.Count - 1) * MapCardGap) / 2;
            for (var i = 0; i < _mapDistricts.Count; i++)
                _mapDistricts[i].Card = MapCard(_mapDistricts[i], i, left + i * (MapCardW + MapCardGap), MapCardsTop);
            _mapPin = MapPin(_mapLayer);
            _mapBack = MakeRect(_mapLayer, "Map card back", (1600 - MapBackW) / 2, MapBackTop, MapBackW, MapBackH);
            // Опора в центре: оборот разворачивается от середины.
            _mapBack.pivot = new Vector2(.5f, .5f);
            _mapBack.anchoredPosition = new Vector2(800, -MapBackTop - MapBackH / 2);
            _mapBack.gameObject.SetActive(false);
            _mapLayer.gameObject.SetActive(false);
        }

        /// <summary>Лицо карты-района: тёмное сукно, тонкий золотой край, настоящие вывески города.</summary>
        private RectTransform MapCard(MapDistrict district, int index, float x, float y)
        {
            var card = MakeRect(_mapLayer, "Map card " + district.Title, x, y, MapCardW, MapCardH);
            card.pivot = new Vector2(.5f, .5f);
            card.anchoredPosition = new Vector2(x + MapCardW / 2, -y - MapCardH / 2);
            var rim = card.gameObject.AddComponent<Image>();
            Round(rim, MapCardW, MapCardH, 22);
            rim.color = SignGoldBottom;
            card.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(3, -5);
            var face = MakeRect(card, "Face", 2, 2, MapCardW - 4, MapCardH - 4).gameObject.AddComponent<Image>();
            Round(face, MapCardW - 4, MapCardH - 4, 20);
            face.color = MapFace;
            face.raycastTarget = false;
            var window = MakeRect(card, "Window", 14, 48, MapCardW - 28, MapCardH - 130).gameObject.AddComponent<Image>();
            Round(window, MapCardW - 28, MapCardH - 130, 14);
            window.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            AddMapPreview(window.rectTransform, district, MapCardW - 28, MapCardH - 130);
            foreach (var corner in new[] { 12f, MapCardW - 48 })
            {
                var suit = MakeLabel(card, district.Suit, corner, 4, 36, 40, 32);
                suit.alignment = TextAnchor.MiddleCenter;
                suit.fontStyle = FontStyle.Bold;
                suit.color = district.SuitColor;
            }
            MapPlaque(card, district.Title, 12, MapCardH - 72, MapCardW - 24, 58);
            var button = card.gameObject.AddComponent<Button>();
            button.targetGraphic = rim;
            var colors = button.colors;
            colors.highlightedColor = new Color(1, .96f, .86f);
            colors.pressedColor = new Color(.8f, .8f, .8f);
            button.colors = colors;
            button.onClick.AddListener(() => ShowMapBack(index));
            card.gameObject.AddComponent<HubPress>();
            return card;
        }

        /// <summary>Табличка-вывеска без нажатия: доска в золотой рамке и золотые буквы, как у кнопок города.</summary>
        private RectTransform MapPlaque(Transform parent, string text, float x, float y, float w, float h)
        {
            var button = CityButton(parent, text, x, y, w, h, () => { });
            button.enabled = false;
            button.GetComponent<Image>().raycastTarget = false;
            var label = button.GetComponentInChildren<Text>();
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 14;
            label.resizeTextMaxSize = label.fontSize;
            label.rectTransform.sizeDelta = new Vector2(w - 20, h);
            label.rectTransform.anchoredPosition = new Vector2(10, 0);
            return (RectTransform)button.transform;
        }

        /// <summary>Текущий район: небольшая золотая плашка между мастями, не поверх здания.</summary>
        private RectTransform MapPin(Transform parent)
        {
            var pin = MakeRect(parent, "You are here", 0, 0, 174, 30);
            var rim = pin.gameObject.AddComponent<Image>();
            Round(rim, 174, 30, 15);
            rim.color = SignGoldBottom;
            rim.raycastTarget = false;
            var inset = MakeRect(pin, "Current district", 1.5f, 1.5f, 171, 27).gameObject.AddComponent<Image>();
            Round(inset, 171, 27, 13);
            inset.color = new Color(.13f, .18f, .12f);
            inset.raycastTarget = false;
            var dot = MakeRect(pin, "Location dot", 13, 11, 8, 8).gameObject.AddComponent<Image>();
            dot.sprite = UiTextures.Circle(32, Color.white);
            dot.color = SignGoldTop;
            dot.raycastTarget = false;
            var label = MakeLabel(pin, "Сейчас здесь", 27, 0, 137, 30, 18);
            label.alignment = TextAnchor.MiddleCenter;
            label.fontStyle = FontStyle.Bold;
            label.color = SignGoldTop;
            return pin;
        }

        /// <summary>Район, где игрок: подполье, Клуб (он на площади) или ближайшая остановка улицы.</summary>
        private int MapHere()
        {
            if (_cityLevel == 1) return _mapDistricts.FindIndex(item => item.Level == 1);
            var stop = NearestStop(_cityCamera);
            if (IsInterior(_cityLevel) && DiveDoor(0, _cityLevel) is CityPlace door && _placePages.TryGetValue(door.Art, out var view))
                stop = NearestStop(view.X + door.Door.x / PictureSize(door.Art).x * view.Width);
            return _mapDistricts.FindIndex(item => item.Level == 0 && item.Stop == stop);
        }

        private void PlaceMapPin()
        {
            var here = MapHere();
            _mapPin.gameObject.SetActive(here >= 0);
            if (here < 0) return;
            var card = _mapDistricts[here].Card;
            _mapPin.SetParent(card, false);
            _mapPin.anchorMin = _mapPin.anchorMax = _mapPin.pivot = new Vector2(.5f, 1);
            _mapPin.anchoredPosition = new Vector2(0, -10);
        }

        /// <summary>Перевернуть карту района: оборот с рубашкой и кнопками мест встаёт посередине.</summary>
        private void ShowMapBack(int index)
        {
            if (!_mapOpen || _mapFlip > 0 && _mapBackFor < 0) return;
            _mapBackFor = index;
            _mapFlipCard = index;
            BuildMapBack(_mapDistricts[index]);
        }

        private void BuildMapBack(MapDistrict district)
        {
            // Прежний оборот снимаем сразу: Destroy удалит его только в конце кадра.
            for (var i = _mapBack.childCount - 1; i >= 0; i--)
            {
                var child = _mapBack.GetChild(i);
                child.SetParent(null, false);
                if (Application.isPlaying) Destroy(child.gameObject);
                else DestroyImmediate(child.gameObject);
            }
            var rim = _mapBack.GetComponent<Image>();
            if (rim == null) rim = _mapBack.gameObject.AddComponent<Image>();
            Round(rim, MapBackW, MapBackH, 30);
            rim.color = SignGoldBottom;
            var back = MakeRect(_mapBack, "Card back", 6, 6, MapBackW - 12, MapBackH - 12).gameObject.AddComponent<Image>();
            Round(back, MapBackW - 12, MapBackH - 12, 26);
            back.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            back.color = new Color(.35f, .08f, .1f);
            var pattern = MakeRect(back.transform, "Pattern", 0, 0, MapBackW - 12, MapBackH - 12).gameObject.AddComponent<Image>();
            // Рубашка колоды (её выбор в косметике — потом); поля картинки обрезаны.
            pattern.sprite = ChestArt.GreenBack;
            pattern.enabled = pattern.sprite != null;
            pattern.raycastTarget = false;
            var shade = MakeRect(back.transform, "Shade", 0, 0, MapBackW - 12, MapBackH - 12).gameObject.AddComponent<Image>();
            shade.color = new Color(.02f, .01f, .03f, .55f);
            shade.raycastTarget = false;
            MapPlaque(_mapBack, district.Title, 40, 34, MapBackW - 80, 76);
            var rows = new List<(string Text, System.Action Go)>
            {
                (district.Level == 0 ? "Весь район" : "Спуститься", () => StartMapTrip(-1, _mapDistricts.IndexOf(district))),
            };
            foreach (var index in district.Places)
            {
                var place = index;
                rows.Add((MapPlaceName(CityPlaces[index]), () => StartMapTrip(place, -1)));
            }
            const float bodyTop = 144, bodyBottom = MapBackH - 42;
            var rowsHeight = rows.Count * MapRowH + (rows.Count - 1) * MapRowGap;
            var top = bodyTop + (bodyBottom - bodyTop - rowsHeight) / 2;
            foreach (var (text, go) in rows)
            {
                var button = CityButton(_mapBack, text, 50, top, MapBackW - 100, MapRowH, go);
                var label = button.GetComponentInChildren<Text>();
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 20;
                label.resizeTextMaxSize = label.fontSize;
                label.rectTransform.sizeDelta = new Vector2(MapBackW - 130, MapRowH);
                label.rectTransform.anchoredPosition = new Vector2(15, 0);
                top += MapRowH + MapRowGap;
            }
        }

        /// <summary>Поехать с карты: к месту (подход к двери) или в район; карта закрывается.</summary>
        private void StartMapTrip(int place, int district)
        {
            if (!_mapOpen || _mapFlip < 1) return;
            _mapTripPlace = place;
            _mapTripDistrict = place >= 0 ? -1 : district;
            CloseMap();
        }

        /// <summary>Открытие и закрытие карты, переворот карты-района, Esc и M — каждый кадр (из UpdateCity).</summary>
        private void UpdateMap(float deltaTime, bool typing)
        {
            if (_mapButtonLayer == null) BuildMapButton();
            if (_mapButtonLayer == null) return;
            RefreshMapButton();
            MapKeys(typing);
            if (_mapLayer == null) return;
            // Ушли с главной (вкладка, место) — карта закрывается.
            if (_mapOpen && _page != "home") _mapOpen = false;
            _mapFade = Mathf.MoveTowards(_mapFade, _mapOpen ? 1 : 0, deltaTime / MapFade);
            _mapFlip = Mathf.MoveTowards(_mapFlip, _mapOpen && _mapBackFor >= 0 ? 1 : 0, deltaTime / MapFlipSeconds);
            if (_mapFlip <= 0) _mapFlipCard = -1;
            if (!_mapOpen && _mapFade <= 0)
            {
                if (_mapLayer.gameObject.activeSelf) _mapLayer.gameObject.SetActive(false);
                return;
            }
            PaintMap();
        }

        /// <summary>
        /// Кадр карты: проявление (карты чуть поднимаются) и переворот — первая половина сжимает лицо карты
        /// к ребру, вторая разворачивает оборот посередине из её места.
        /// </summary>
        private void PaintMap()
        {
            var fade = Mathf.SmoothStep(0, 1, _mapFade);
            _mapGroup.alpha = fade;
            _mapGroup.blocksRaycasts = _mapOpen || _mapFade > 0;
            const float half = .45f;
            var squeeze = _mapFlip < half ? 1 - Mathf.SmoothStep(0, 1, _mapFlip / half) : 0;
            var open = _mapFlip <= half ? 0 : Mathf.SmoothStep(0, 1, (_mapFlip - half) / (1 - half));
            for (var i = 0; i < _mapDistricts.Count; i++)
            {
                var card = _mapDistricts[i].Card;
                var flipping = i == _mapFlipCard;
                card.localScale = new Vector3(flipping ? squeeze : 1, 1, 1);
                card.anchoredPosition = new Vector2(card.anchoredPosition.x, -MapCardsTop - MapCardH / 2 - 30 * (1 - fade));
            }
            var showBack = _mapFlipCard >= 0 && open > 0;
            _mapHeading.gameObject.SetActive(!showBack);
            _mapHint.gameObject.SetActive(!showBack);
            if (_mapBack.gameObject.activeSelf != showBack)
            {
                _mapBack.gameObject.SetActive(showBack);
                if (showBack) _mapBack.SetAsLastSibling();
            }
            if (!showBack) return;
            // Оборот вырастает из места своей карты к середине экрана.
            var from = _mapDistricts[_mapFlipCard].Card.anchoredPosition;
            var to = new Vector2(800, -MapBackTop - MapBackH / 2);
            _mapBack.anchoredPosition = Vector2.Lerp(from, to, open);
            var size = Mathf.Lerp(MapCardH / MapBackH, 1, open);
            _mapBack.localScale = new Vector3(size * open, size, 1);
        }

        private void MapKeys(bool typing)
        {
            // В поле ввода (чат, ник) буква М — это буква, а не карта.
            var keyboard = typing ? null : Keyboard.current;
            var escape = keyboard != null && keyboard.escapeKey.isPressed;
            var escapePressed = escape && !_mapEscapeHeld;
            _mapEscapeHeld = escape;
            var key = keyboard != null && keyboard.mKey.isPressed;
            var keyPressed = key && !_mapKeyHeld;
            _mapKeyHeld = key;
            if (_mapOpen && escapePressed)
            {
                // То же удержание Esc после закрытия карты не должно выводить игрока из здания.
                _cityVerticalHeld = 1;
                if (_mapBackFor >= 0) _mapBackFor = -1;
                else CloseMap();
            }
            else if (keyPressed)
            {
                if (_mapOpen) CloseMap();
                else OpenMap();
            }
        }

        /// <summary>
        /// Поездка с карты — каждый кадр, как к столу (<see cref="ContinueTableTrip"/>): с этажа — на улицу,
        /// по улице — до района, на этаже — проездом к месту, и подход к двери; Клуб и подполье — нырком.
        /// </summary>
        private void ContinueMapTrip()
        {
            if (_mapTripPlace < 0 && _mapTripDistrict < 0) return;
            // Ушли с главной — поездка отменяется.
            if (_page != "home")
            {
                _mapTripPlace = _mapTripDistrict = -1;
                return;
            }
            if (_mapOpen || _diveTarget >= 0 || _diveQueued || _gliding || _floorPanning || _cityDragging || _floorDragging
                || _visitPhase == 3 || _diveShade == null) return;
            if (_visitPhase != 0) EndVisitNow();
            if (_mapTripDistrict >= 0)
            {
                var district = _mapDistricts[_mapTripDistrict];
                if (_cityLevel != district.Level)
                {
                    StartDive(_cityLevel != 0 ? 0 : district.Level);
                    return;
                }
                if (district.Level == 0 && Mathf.Abs(_cityCamera - _cityStops[district.Stop]) > .05f * CityView())
                {
                    MapGlide(district.Stop);
                    return;
                }
                _mapTripDistrict = -1;
                return;
            }
            var place = CityPlaces[_mapTripPlace];
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
                    MapGlide(stop);
                    return;
                }
            }
            else
            {
                var limit = FloorPanLimit(level);
                var u = (view.X + place.Door.x / PictureSize(place.Art).x * view.Width) / _floorWidths[level];
                var target = Mathf.Clamp((u - .5f) * _floorWidths[level], -limit, limit);
                if (Mathf.Abs(target - _floorPan) > .05f * FloorView())
                {
                    GlideFloor(target, GlideBase + GlidePerScreen * Mathf.Abs(target - _floorPan) / FloorView());
                    return;
                }
            }
            var index = _mapTripPlace;
            _mapTripPlace = -1;
            if (place.Id == "bank-boss") ShowBankLoan();
            else if (place.Floor >= 0) StartDive(place.Floor);
            else StartVisit(index);
        }

        /// <summary>Проезд к району с карты: как стрелкой, но дальний путь — не дольше <see cref="MapTripGlide"/> с.</summary>
        private void MapGlide(int stop)
        {
            _district = stop;
            GlideCity(_cityStops[stop], Mathf.Min(MapTripGlide, GlideBase + GlidePerScreen * Mathf.Abs(_cityStops[stop] - _cityCamera) / CityView()));
        }
    }
}
