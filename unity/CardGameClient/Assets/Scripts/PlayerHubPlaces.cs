using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>
    /// Места Card City, которые нажимаются. Навёл мышь (на телефоне — прижал палец) — здание обводится
    /// светлым контуром с золотым краем (<see cref="HubPlaceOutline"/>). Нажал — контур становится
    /// золотым, камера въезжает к двери и открывается
    /// экран места «place:&lt;id&gt;»; «Назад» — камера отъезжает на улицу. Лестницы и двери Клуба — тоже места:
    /// они ведут на другой этаж (PlayerHubUnderground.cs). Экраны мест пока — заготовки: название,
    /// хозяин, что тут будет, и кнопка в готовый раздел, если он есть; каждое место потом делается
    /// отдельно. Фонтан с Outdoors оставляет камеру на площади до «Отойти», без экрана места.
    /// Контуры — в пикселях исходной картинки района 1672×941, как у вывесок.
    /// </summary>
    public sealed partial class PlayerHubController
    {
        private sealed class CityPlace
        {
            public string Id, Art, Title, Host, Plan, Route, RouteTitle, RouteDetail;
            // Этаж, куда ведёт место (лестница, замок, двери здания); −1 — подход к объекту.
            public int Floor = -1;
            public bool Outdoors;
            // Подход открывает сразу Route (стол — игру), а не экран места.
            public bool Direct;
            public bool HoverOnly, HallPassage;
            // Житель с роликом (пианистка): нажатие останавливает или запускает его, никуда не ведёт.
            public bool Resident;
            public Vector2 Door;
            // Область нажатия может включать подпись; обводка выделяет только сам объект.
            public Vector2[] Points, Outline;
            public float Area;
            public int[] Fill;
        }

        private static CityPlace Place(string id, string art, string title, string host, string plan, float doorX, float doorY,
            float[] points, string route = null, string routeTitle = null, string routeDetail = null, bool outdoors = false,
            bool direct = false, bool hoverOnly = false, bool hallPassage = false, float[] outlinePoints = null,
            bool resident = false)
        {
            Vector2[] Vertices(float[] coordinates)
            {
                var result = new Vector2[coordinates.Length / 2];
                for (var i = 0; i < result.Length; i++) result[i] = new Vector2(coordinates[i * 2], coordinates[i * 2 + 1]);
                return result;
            }
            var hit = Vertices(points);
            var outline = outlinePoints == null ? hit : Vertices(outlinePoints);
            var area = 0f;
            for (var i = 0; i < hit.Length; i++)
            {
                var a = hit[i];
                var b = hit[(i + 1) % hit.Length];
                area += a.x * b.y - a.y * b.x;
            }
            return new CityPlace { Id = id, Art = art, Title = title, Host = host, Plan = plan, Door = new Vector2(doorX, doorY),
                Points = hit, Outline = outline, Area = Mathf.Abs(area / 2), Fill = HubPlaceOutline.Triangulate(outline),
                Route = route, RouteTitle = routeTitle, RouteDetail = routeDetail, Outdoors = outdoors, Direct = direct,
                HoverOnly = hoverOnly, HallPassage = hallPassage, Resident = resident };
        }

        private static CityPlace Stairs(string id, string art, int floor, float doorX, float doorY, float[] points)
        {
            var place = Place(id, art, null, null, null, doorX, doorY, points);
            place.Floor = floor;
            return place;
        }

        private static readonly CityPlace[] CityPlaces =
        {
            // Архив переехал с холма в здание бывшего полицейского поста (решение автора, 2026-09-30).
            Place("archive", "card-city-old-town", "Архив", "Туз червей — архивариус",
                "Достижения игрока и летопись города — история Тузяры, Шестёрки и других жителей.",
                290, 510, new float[] { 212, 655, 212, 225, 262, 185, 365, 55, 470, 175, 505, 195, 505, 620, 355, 640, 352, 655 },
                "achievements", "Достижения", "Все достижения и награды за них"),
            Place("six", "card-city-old-town", "Дом Шестёрки", "Шестёрка пик — проводник",
                "Проводник по городу: правила игр, подсказки и почтовый ящик — к нему приходят письма и подарки.",
                680, 530, new float[] { 548, 625, 548, 395, 575, 350, 610, 320, 690, 312, 715, 360, 742, 425, 742, 620, 700, 628 },
                "tutorial", "Обучение", "Правила всех игр и подсказки"),
            Place("tavern", "card-city-old-town", "Таверна «Червовая дама»", "Дама червей",
                "Здесь встречаются с друзьями, пишут и зовут за стол. У входа Пятёрка червей играет на аккордеоне.",
                1140, 510, new float[] { 895, 610, 895, 300, 905, 170, 955, 70, 1000, 90, 1090, 25, 1140, 50, 1200, 110, 1250, 190,
                    1300, 250, 1300, 395, 1255, 420, 1255, 600, 1190, 610 },
                "friends", "Друзья и чаты", "Друзья, сообщения и приглашения за стол"),
            Place("fortune", "card-city-old-town", "Шатёр гадалки", "Пиковая дама",
                "Раз в день бесплатный «расклад дня»: открой три карты из девяти — награда и предсказание на день.",
                1405, 520, new float[] { 1285, 600, 1295, 470, 1310, 420, 1340, 380, 1405, 305, 1470, 370, 1500, 400, 1515, 440,
                    1515, 600, 1460, 620, 1330, 620 }),
            Place("shop", "card-city", "Лавка", "Дама бубен",
                "Вся косметика города: рубашки, рамки, сукна и персонажи, товар дня и примерочная.",
                380, 500, new float[] { 230, 605, 230, 300, 300, 245, 390, 195, 455, 165, 470, 175, 520, 260, 548, 330, 552, 605 },
                "shop", "Магазин", "Рубашки, рамки, персонажи и товар дня"),
            Stairs("stairs-down", "card-city", 1, 476, 725,
                new float[] { 268, 792, 268, 610, 300, 592, 345, 615, 455, 605, 575, 605, 600, 595, 650, 615, 652, 752, 598, 762, 560, 792 }),
            // Клуб — здание с залом внутри (PlayerHubInterior.cs): двери ведут на этаж зала.
            Stairs("club", "card-city", FirstInterior, 836, 470,
                new float[] { 558, 560, 558, 275, 610, 265, 640, 195, 700, 200, 710, 180, 760, 190, 785, 160, 790, 100,
                    880, 100, 885, 160, 915, 190, 960, 180, 975, 200, 1030, 200, 1060, 265, 1145, 275, 1145, 560, 1030, 600, 640, 600 }),
            Place("news", "card-city", "Киоск новостей", "Двойка",
                "Новости города, задания дня и объявления о новых местах.",
                1160, 520, new float[] { 1062, 615, 1058, 425, 1095, 408, 1115, 378, 1160, 345, 1205, 378, 1228, 408, 1265, 425, 1262, 615 },
                "quests", "Задания", "Задания дня и награды за них"),
            // Уличный объект: камера подходит к фонтану, но отдельный экран не открывается.
            Place("fountain", "card-city", "Фонтан", "Главная площадь", null,
                1382, 538, new float[] { 1208, 623, 1244, 615, 1245, 588, 1250, 577, 1267, 571, 1299, 568,
                    1306, 537, 1316, 522, 1358, 516, 1369, 498, 1353, 503, 1341, 497, 1335, 485, 1337, 472,
                    1346, 460, 1365, 455, 1360, 441, 1365, 427, 1376, 420, 1391, 417, 1405, 424, 1412, 438,
                    1410, 449, 1405, 458, 1421, 458, 1434, 468, 1439, 482, 1435, 495, 1423, 502, 1407, 500,
                    1398, 495, 1408, 514, 1457, 518, 1470, 535, 1478, 567, 1510, 570, 1526, 578, 1533, 617,
                    1553, 624, 1554, 643, 1520, 651, 1280, 651, 1208, 641 }, outdoors: true),
            Place("bank", "card-city-tuzyara", "Банк Тузяры", "Тузяра",
                "Кредиты с подвохом, поручения и «крыша» Тузяры; у дверей — два вальта-бугая.",
                390, 505, new float[] { 150, 600, 150, 210, 180, 185, 190, 140, 215, 135, 240, 180, 255, 45, 300, 40, 350, 15, 470, 40,
                    560, 60, 595, 120, 600, 300, 620, 270, 640, 290, 655, 340, 655, 620, 600, 690, 460, 690, 450, 640, 300, 640, 280, 600 },
                "bank", "Банк", "Кредиты, погашение и договоры"),
            Place("casino", "card-city-tuzyara", "Казино", "Дама треф и Тузяра",
                "Покер с высокими ставками, VIP-зал и босс-партия с самим Тузярой. Хозяйка — Дама треф, поёт под рояль.",
                905, 560, new float[] { 688, 690, 688, 265, 725, 212, 760, 238, 840, 165, 875, 110, 910, 95, 950, 110, 985, 145,
                    1010, 100, 1050, 120, 1080, 180, 1120, 150, 1160, 95, 1200, 150, 1205, 690 },
                // Покер переехал из Клуба в казино (решение автора, 2026-09-29); свой зал казино — потом.
                "game:poker", direct: true),
            Place("market", "card-city-port", "Рынок", "Король бубен",
                "Рынок игроков: выставляй свои вещи по своей цене, покупай чужие.",
                555, 400, new float[] { 120, 590, 120, 400, 200, 370, 280, 300, 385, 200, 400, 110, 470, 110, 515, 90, 545, 60, 565, 48,
                    585, 60, 615, 90, 650, 120, 700, 150, 720, 210, 740, 330, 800, 395, 895, 450, 895, 590 }),
            Stairs("stairs-up", "card-city-underground", 0, 267, 188,
                new float[] { 0, 480, 0, 120, 120, 20, 160, 0, 310, 0, 335, 60, 420, 105, 500, 130, 400, 200, 300, 260, 230, 310,
                    160, 390, 110, 480 }),
            Place("black-market", "card-city-underground", "Чёрный рынок", "Семёрка треф",
                "Лавка под лестницей: редкие вещи не для всех и не по правилам.",
                380, 470, new float[] { 165, 650, 160, 400, 230, 320, 300, 265, 420, 235, 550, 250, 560, 300, 600, 375, 598, 600,
                    520, 630, 390, 655 }),
            Place("underground-club", "card-city-underground", "Подпольный клуб", "Десятки треф",
                "Подпольные столы с высокими ставками. У двери — вальт-бугай, за столами — десятки треф.",
                768, 430, new float[] { 604, 560, 604, 300, 620, 230, 660, 180, 720, 150, 768, 140, 830, 150, 890, 185, 925, 240,
                    940, 300, 940, 560 }),
            Place("arena", "card-city-underground", "Арена", "Джокер",
                "Яма Джокера: режим «Арена» — очки жизни, модификаторы и спецкарты. Ведёт красный Джокер.",
                1400, 560, new float[] { 1040, 600, 1045, 450, 1075, 400, 1170, 395, 1200, 330, 1400, 330, 1500, 420, 1600, 470,
                    1625, 560, 1600, 660, 1500, 700, 1300, 690, 1200, 650, 1100, 610 }),
            // Зал Клуба (картинка 1916×821): двери наружу, афиша с играми зала и четыре стола.
            Stairs("club-exit", ClubHall, 0, 115, 400,
                new float[] { 0, 665, 0, 60, 60, 20, 150, 15, 220, 40, 268, 120, 268, 600, 210, 612, 150, 650, 60, 668 }),
            Place("club-board", ClubHall, "Афиша", "Клуб", null, 347, 430,
                new float[] { 285, 590, 285, 340, 320, 322, 375, 322, 410, 340, 410, 590 }, "games", direct: true),
            ClubTable("durak"),
            ClubTable("bluff"),
            ClubTable("chests"),
            ClubTable("govno"),
            Place("club-pianist", ClubHall, "Пианистка", "Пятёрка треф", null, 1440, 440,
                new float[] { 1506, 310, 1508, 325, 1494, 401, 1489, 411, 1414, 466, 1384, 462, 1366, 442, 1359, 367,
                    1362, 358, 1406, 312, 1459, 305, 1491, 306 }, resident: true),
            Place("club-passage", ClubBridge, "Переход между залами", "Клуб", null, 850, 350,
                new float[] { 642, 550, 642, 220, 663, 142, 702, 81, 762, 49, 812, 58, 986, 119, 1055, 177,
                    1077, 254, 1077, 552 }, hallPassage: true),
            HallBoard(),
            EmptyClubTable(1, 472),
            EmptyClubTable(2, 830),
            EmptyClubTable(3, 1180),
            EmptyClubTable(4, 1543),
        };

        // Въезд к двери: секунды, приближение и затемнение в конце (дальше страницу приглушает _cityDim).
        private const float VisitIn = .65f, VisitOut = .75f, VisitZoom = 1.8f, VisitShade = .5f;
        // Контур — такой толщины на экране; появляется и гаснет за столько секунд.
        private const float PlaceLine = 5.5f, PlaceGlow = 2.5f, PlaceEdge = 1.25f, PlaceSeparator = 1.25f, PlaceFade = .18f;

        // Куда нарисована картинка места: кусок ленты (x и ширина — в пикселях картинок ленты) или этаж.
        private readonly Dictionary<string, (RectTransform Page, float X, float Width)> _placePages =
            new Dictionary<string, (RectTransform Page, float X, float Width)>();
        private HubCitySwipe _citySwipe;
        private HubPlaceOutline _placeOutline;
        // Место под контуром и чей контур сейчас собран в сетку.
        private int _placeHover = -1, _placeOutlineFor = -1;
        private float _placeGlow;
        // Въезд: 0 — нет, 1 — подход, 2 — у объекта или внутри места, 3 — отходим.
        private int _visit = -1, _visitPhase;
        private float _visitTime;
        private RectTransform _outdoorControls;
        private bool OutdoorVisit => _visit >= 0 && CityPlaces[_visit].Outdoors;

        /// <summary>Этаж картинки места: улица — 0, иначе номер её картинки в FloorArt.</summary>
        private static int PlaceLevel(CityPlace place)
        {
            if (IsClubArt(place.Art)) return FirstInterior;
            for (var level = 1; level < FloorArt.Length; level++)
                if (FloorArt[level].EndsWith("/" + place.Art, System.StringComparison.Ordinal)) return level;
            return 0;
        }

        private void RegisterPlacePage(string art, RectTransform page, float x, float width)
        {
            if (!_placePages.ContainsKey(art)) _placePages[art] = (page, x, width);
        }

        /// <summary>Точка картинки места (в её пикселях, <see cref="PictureSize"/>) — на экране, от центра.</summary>
        private Vector2 PlaceScreenPoint(CityPlace place, Vector2 picture)
        {
            var view = _placePages[place.Art];
            var level = PlaceLevel(place);
            var scale = level == 0 ? CityScale() : FloorScale(level);
            var uv = picture / PictureSize(place.Art);
            var y = (.5f - uv.y) * CityHeight * scale;
            if (level != 0) return new Vector2((view.X + uv.x * view.Width - _floorWidths[level] / 2 - FloorPan(level)) * scale, y);
            return new Vector2((view.X + uv.x * view.Width - _cityCamera) * scale, y);
        }

        /// <summary>Место под точкой экрана (от центра) на нынешнем этаже; −1 — пусто.</summary>
        private int PlaceAt(Vector2 point)
        {
            var found = -1;
            for (var i = 0; i < CityPlaces.Length; i++)
            {
                var place = CityPlaces[i];
                if (PlaceLevel(place) != _cityLevel || !_placePages.TryGetValue(place.Art, out var view)) continue;
                var scale = _cityLevel == 0 ? CityScale() : FloorScale(_cityLevel);
                var x = _cityLevel == 0 ? (point.x / scale + _cityCamera - view.X) / view.Width
                    : (point.x / scale + FloorPan(_cityLevel) + _floorWidths[_cityLevel] / 2 - view.X) / view.Width;
                var picture = new Vector2(x, .5f - point.y / (CityHeight * scale)) * PictureSize(place.Art);
                // Внахлёст — меньшее место: оно ближе и мельче, иначе в него не попасть.
                if (Contains(place.Points, picture) && (found < 0 || place.Area < CityPlaces[found].Area)) found = i;
            }
            return found;
        }

        private static bool Contains(Vector2[] polygon, Vector2 point)
        {
            var inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
                if (polygon[i].y > point.y != polygon[j].y > point.y
                    && point.x < (polygon[j].x - polygon[i].x) * (point.y - polygon[i].y) / (polygon[j].y - polygon[i].y) + polygon[i].x)
                    inside = !inside;
            return inside;
        }

        /// <summary>
        /// Нажатие по городу (не свайп): лестница или замок — на другой этаж, остальные места —
        /// въезд к двери и экран места.
        /// </summary>
        private void ClickPlace(Vector2 point)
        {
            var index = PlaceAt(point);
            if (index < 0) return;
            var place = CityPlaces[index];
            if (place.HoverOnly) return;
            if (place.Resident) ToggleResident(place.Id);
            else if (place.HallPassage) CrossClubHall();
            else if (place.Floor >= 0) StartDive(place.Floor);
            else StartVisit(index);
        }

        private void StartVisit(int index)
        {
            if (_visitPhase != 0 || _diveTarget >= 0 || _cityDragging || _diveShade == null) return;
            _gliding = false;
            _glideKey = 0;
            _cityVelocity = 0;
            _floorPanning = _floorDragging = false;
            _floorPanVelocity = 0;
            _diveQueued = false;
            _visit = index;
            _visitPhase = 1;
            _visitTime = 0;
            _placeHover = index;
            _diveShade.gameObject.SetActive(true);
            _diveVignette.gameObject.SetActive(true);
            FrameVisit(0);
        }

        /// <summary>Шаг въезда или выезда на <paramref name="deltaTime"/> секунд.</summary>
        private void MoveVisit(float deltaTime)
        {
            if (_visitPhase == 0) return;
            _visitTime += deltaTime;
            if (_visitPhase == 1)
            {
                var t = Mathf.Clamp01(_visitTime / VisitIn);
                if (t < 1)
                {
                    FrameVisit(Mathf.SmoothStep(0, 1, t));
                    return;
                }
                // У объекта: уличный остаётся в кадре, здание открывает свой экран.
                _visitPhase = 2;
                _diveShade.gameObject.SetActive(false);
                _diveVignette.gameObject.SetActive(false);
                AimCity(VisitZoom, 1, VisitFocus());
                var place = CityPlaces[_visit];
                if (OutdoorVisit) ShowOutdoorControls();
                else if (!place.Direct) Navigate("place:" + place.Id);
                // Стол по странице его игры: табличка уже открыта — перерисовать её у стола. Ушли
                // к комнатам, пока подходили, — не выдёргивать оттуда.
                else if (_page == "home" || _page == place.Route) Navigate(place.Route);
                return;
            }
            if (_visitPhase == 2)
            {
                AimCity(VisitZoom, 1, VisitFocus());
                // Фонтан остаётся частью площади: держим приближение до «Отойти».
                if (OutdoorVisit) return;
                // Вернулись на главную («Назад», вкладка) — выезжаем на улицу.
                if (_page != "home") return;
                _visitPhase = 3;
                _visitTime = 0;
                _diveShade.gameObject.SetActive(true);
                _diveVignette.gameObject.SetActive(true);
            }
            var q = Mathf.Clamp01(_visitTime / VisitOut);
            if (q < 1)
            {
                FrameVisit(1 - Mathf.SmoothStep(0, 1, q));
                return;
            }
            _cityRig.localScale = Vector3.one;
            _cityRig.anchoredPosition = Vector2.zero;
            _diveShade.gameObject.SetActive(false);
            _diveVignette.gameObject.SetActive(false);
            _visitPhase = 0;
            _visit = -1;
        }

        private Vector2 VisitFocus()
        {
            var place = CityPlaces[_visit];
            var point = PlaceScreenPoint(place, place.Door);
            // У стола с игрой справа встаёт табличка: стол подъезжает левее середины экрана.
            if (IsGameTable(place)) point.x += _cityArea.rect.width * TableAside / VisitZoom;
            return point;
        }

        private void ShowOutdoorControls()
        {
            if (_outdoorControls == null)
            {
                _outdoorControls = StageLayer("Outdoor visit controls");
                CityButton(_outdoorControls, "ОТОЙТИ", 24, 24, 170, 56, CloseOutdoorVisit);
            }
            _outdoorControls.SetAsLastSibling();
            _outdoorControls.gameObject.SetActive(true);
        }

        private void CloseOutdoorVisit()
        {
            if (!OutdoorVisit || _visitPhase != 2) return;
            if (_outdoorControls != null) _outdoorControls.gameObject.SetActive(false);
            _visitPhase = 3;
            _visitTime = 0;
            // Для уличного объекта затемнение не требуется ни при подходе, ни при возврате.
        }

        /// <summary>Кадр въезда: <paramref name="depth"/> 0 — улица, 1 — у самой двери.</summary>
        private void FrameVisit(float depth)
        {
            var screen = AimCity(1 + (VisitZoom - 1) * depth, depth, VisitFocus());
            if (OutdoorVisit)
            {
                _diveShade.gameObject.SetActive(false);
                _diveVignette.gameObject.SetActive(false);
                return;
            }
            _diveShade.color = new Color(DiveDark.r, DiveDark.g, DiveDark.b, VisitShade * Mathf.SmoothStep(0, 1, depth));
            var half = _cityArea.rect.size / 2;
            var cover = 2 * Mathf.Max(Mathf.Abs(screen.x) + half.x, Mathf.Abs(screen.y) + half.y);
            var size = Mathf.Max(cover, _cityArea.rect.height * Mathf.Lerp(9, 4, depth));
            _diveVignette.rectTransform.anchoredPosition = screen;
            _diveVignette.rectTransform.sizeDelta = new Vector2(size, size);
            _diveVignette.color = new Color(DiveDark.r, DiveDark.g, DiveDark.b, .6f * Mathf.SmoothStep(0, 1, depth));
        }

        /// <summary>
        /// Контур места под мышью (на телефоне — под прижатым пальцем) — каждый кадр: город мог
        /// проехать под неподвижной мышью. Во время въезда горит место, к которому едем.
        /// </summary>
        private void UpdatePlaceHover(float deltaTime)
        {
            var hover = -1;
            if (_visitPhase == 1) hover = _visit;
            else if (_visitPhase == 0 && _page == "home" && !_cityDragging && _diveTarget < 0 && _citySwipe != null && _citySwipe.Hovered)
            {
                var pointer = Pointer.current;
                if (pointer != null && (!(pointer is Touchscreen) || pointer.press.isPressed))
                {
                    var canvas = _cityArea.GetComponentInParent<Canvas>();
                    var camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                    if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_cityArea, pointer.position.ReadValue(), camera, out var local))
                        hover = PlaceAt(local - _cityArea.rect.center);
                }
            }
            ShowPlaceOutline(hover, deltaTime);
        }

        private void ShowPlaceOutline(int hover, float deltaTime)
        {
            // Арка остаётся проходом по нажатию, но не выделяется как отдельный объект.
            if (hover >= 0 && CityPlaces[hover].HallPassage) hover = -1;
            if (hover != _placeHover)
            {
                // Другое место — контур переезжает сразу и загорается заново.
                if (hover >= 0) _placeGlow = 0;
                if (hover >= 0 || _placeGlow <= 0) _placeHover = hover;
            }
            _placeGlow = Mathf.MoveTowards(_placeGlow, hover >= 0 ? 1 : 0, deltaTime / PlaceFade);
            var show = _placeHover >= 0 && _placeGlow > 0;
            if (!show)
            {
                if (_placeOutline != null) _placeOutline.gameObject.SetActive(false);
                if (_placeGlow <= 0) _placeHover = -1;
                return;
            }
            var place = CityPlaces[_placeHover];
            var page = _placePages[place.Art].Page;
            if (_placeOutline == null)
            {
                _placeOutline = new GameObject("Place outline", typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<HubPlaceOutline>();
                _placeOutline.raycastTarget = false;
            }
            var rect = _placeOutline.rectTransform;
            if (rect.parent != page.parent) rect.SetParent(page.parent, false);
            // Поверх всех кусков ленты: соседний мост заходит на край района и закрыл бы контур.
            rect.SetAsLastSibling();
            rect.anchorMin = page.anchorMin;
            rect.anchorMax = page.anchorMax;
            rect.pivot = page.pivot;
            rect.anchoredPosition = page.anchoredPosition;
            rect.sizeDelta = page.sizeDelta;
            rect.localScale = page.localScale;
            // Единица контура — пиксель картинки; на экране она — масштаб ленты или этажа.
            var level = PlaceLevel(place);
            var scale = level == 0 ? CityScale() : FloorScale(level);
            if (_placeOutlineFor != _placeHover)
            {
                _placeOutlineFor = _placeHover;
                var points = new Vector2[place.Outline.Length];
                var size = PictureSize(place.Art);
                for (var i = 0; i < points.Length; i++) points[i] = place.Outline[i] / size;
                _placeOutline.Points = points;
                _placeOutline.Fill = place.Fill;
                _placeOutline.SetVerticesDirty();
            }
            // При въезде камера приближает весь город; линия сохраняет свою экранную толщину.
            scale *= Mathf.Max(.001f, Mathf.Abs(_cityRig.localScale.x));
            var line = PlaceLine / scale;
            var selected = _visitPhase == 1 && _visit == _placeHover;
            if (!Mathf.Approximately(_placeOutline.Line, line) || _placeOutline.Selected != selected)
            {
                _placeOutline.Line = line;
                _placeOutline.Glow = PlaceGlow / scale;
                _placeOutline.Edge = PlaceEdge / scale;
                _placeOutline.Separator = PlaceSeparator / scale;
                _placeOutline.Selected = selected;
                _placeOutline.SetVerticesDirty();
            }
            _placeOutline.gameObject.SetActive(true);
            // Плавное появление, затем стабильная яркость: выбранное место не мерцает.
            _placeOutline.canvasRenderer.SetAlpha(Mathf.SmoothStep(0, 1, _placeGlow));
        }

        /// <summary>Экран места: название, хозяин, что тут будет и переход в готовый раздел, если он есть.</summary>
        private void BuildCityPlace(string id)
        {
            var place = System.Array.Find(CityPlaces, item => item.Id == id && item.Floor < 0 && !item.Outdoors && !item.Direct
                && !item.HoverOnly && !item.HallPassage && !item.Resident);
            if (place == null)
            {
                _page = "home";
                BuildHome();
                return;
            }
            Title(place.Title, place.Host);
            HubPanelText("Place plan", "Что здесь будет", place.Plan, 0, 0, 1160, 150);
            if (place.Route != null)
                MenuTile(place.RouteTitle, place.RouteDetail, 0, 180, 1160, 150, () => Navigate(place.Route), SectionOf(place.Route).Key);
            else
                HubPanelText("Place soon", "Скоро", "Место откроется в одном из обновлений — восьмёрки уже ставят леса.", 0, 180, 1160, 120);
        }
    }
}
