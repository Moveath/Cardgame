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
            // В Доме Шестёрки: нажатие — реплика Шестёрки в облачке (PlayerHubSixHouse.cs), никуда не ведёт.
            public bool Talk;
            public Vector2 Door;
            // Область нажатия может включать подпись; обводка выделяет только сам объект.
            public Vector2[] Points, Outline;
            public float Area;
            public int[] Fill;
        }

        private static CityPlace Place(string id, string art, string title, string host, string plan, float doorX, float doorY,
            float[] points, string route = null, string routeTitle = null, string routeDetail = null, bool outdoors = false,
            bool direct = false, bool hoverOnly = false, bool hallPassage = false, float[] outlinePoints = null,
            bool resident = false, bool talk = false)
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
                HoverOnly = hoverOnly, HallPassage = hallPassage, Resident = resident, Talk = talk };
        }

        private static CityPlace Stairs(string id, string art, int floor, float doorX, float doorY, float[] points)
            => Entrance(Place(id, art, null, null, null, doorX, doorY, points), floor);

        /// <summary>Место с названием, которое ведёт внутрь здания (этаж <paramref name="floor"/>), а не на свой экран.</summary>
        private static CityPlace Entrance(CityPlace place, int floor)
        {
            place.Floor = floor;
            return place;
        }

        /// <summary>Столик в зале таверны: подход к нему — друзья (поиск, заявки, приглашения, «Позвать за стол»).</summary>
        private static CityPlace TavernTable(int number, float doorX, float doorY, float[] points)
            => Place("tavern-table-" + number, TavernHall, "Столик", "Дама червей", null, doorX, doorY, points,
                "friends", "Друзья", "Друзья, поиск игроков, заявки и приглашения", direct: true);

        // Реплики жителей за столами таверны выключены (2026-10-10, автор: «убери пока диалоги, может потом добавим обратно»):
        // жители — часть своего стола (нажатие — друзья), девятки у угла кланов — просто зал. Тексты, облачка и болтовня
        // в тишине сохранены (PlayerHubTavern.cs): вернуть — включить флаг. Объявлен до CityPlaces — тот читает его при сборке.
        private static readonly bool TavernResidentsTalk = false;

        /// <summary>Жители за столом таверны: с репликами — отвечают, без них — как их стол, друзья.</summary>
        private static CityPlace TavernResidents(string id, string title, string host, float doorX, float doorY, float[] points)
            => TavernResidentsTalk ? Place(id, TavernHall, title, host, null, doorX, doorY, points, talk: true)
                : Place(id, TavernHall, title, host, null, doorX, doorY, points, "friends", "Друзья",
                    "Друзья, поиск игроков, заявки и приглашения", direct: true);

        // Выключенные места — null в списке, их нет (FindAll ниже).
        private static readonly CityPlace[] CityPlaces = System.Array.FindAll(new[]
        {
            // Архив переехал с холма в здание бывшего полицейского поста (решение автора, 2026-09-30).
            // Здание с залом внутри (PlayerHubArchive.cs, 2026-10-18): дверь ведёт на его этаж.
            Entrance(Place("archive", "card-city-old-town", "Архив", "Туз червей — архивариус",
                "Витрина достижений, летопись города и твоё личное дело.",
                290, 510, new float[] { 212, 655, 212, 225, 262, 185, 365, 55, 470, 175, 505, 195, 505, 620, 355, 640, 352, 655 }),
                ArchiveLevel),
            // Дом Шестёрки — здание с комнатой внутри (2026-10-07, PlayerHubSixHouse.cs): дверь ведёт на её этаж.
            // Нажимается и домик, и табличка на кронштейне справа, над проулком; контур — только домик.
            Entrance(Place("six", "card-city-old-town", "Дом Шестёрки", "Шестёрка пик — проводник", null,
                680, 530, new float[] { 548, 625, 548, 395, 575, 350, 610, 320, 690, 312, 715, 360, 735, 395, 860, 395, 860, 482,
                    742, 482, 742, 620, 700, 628 },
                outlinePoints: new float[] { 548, 625, 548, 395, 575, 350, 610, 320, 690, 312, 715, 360, 742, 425, 742, 620, 700, 628 }),
                SixLevel),
            // Таверна — здание с залом внутри (PlayerHubTavern.cs, 2026-10-10): дверь ведёт на его этаж.
            Entrance(Place("tavern", "card-city-old-town", "Таверна", "Дама червей",
                "Здесь встречаются с друзьями, пишут и зовут за стол. На сцене Пятёрка червей играет на аккордеоне.",
                1140, 510, new float[] { 895, 610, 895, 300, 905, 170, 955, 70, 1000, 90, 1090, 25, 1140, 50, 1200, 110, 1250, 190,
                    1300, 250, 1300, 395, 1255, 420, 1255, 600, 1190, 610 }),
                TavernLevel),
            Entrance(Place("fortune", "card-city-old-town", "Шатёр гадалки", "Пиковая дама",
                "Одна из шести карт бесплатно каждые 24 часа. Предсказание меняет обычную награду за несколько партий.",
                1405, 520, new float[] { 1285, 600, 1295, 470, 1310, 420, 1340, 380, 1405, 305, 1470, 370, 1500, 400, 1515, 440,
                    1515, 600, 1460, 620, 1330, 620 }, "fortune", direct: true), FortuneLevel),
            // Лавка Дамы бубен — здание с прилавком внутри (PlayerHubShop.cs, 2026-10-17): дверь ведёт на её этаж.
            Entrance(Place("shop", "card-city", "Лавка", "Дама бубен",
                "Витрина дня: рубашки, рамки, сукна и наряды, товар дня со скидкой.",
                380, 500, new float[] { 230, 605, 230, 300, 300, 245, 390, 195, 455, 165, 470, 175, 520, 260, 548, 330, 552, 605 }),
                ShopLevel),
            // Клуб — здание с залом внутри (PlayerHubInterior.cs): двери ведут на этаж зала.
            Stairs("club", "card-city", FirstInterior, 836, 470,
                new float[] { 558, 560, 558, 275, 610, 265, 640, 195, 700, 200, 710, 180, 760, 190, 785, 160, 790, 100,
                    880, 100, 885, 160, 915, 190, 960, 180, 975, 200, 1030, 200, 1060, 265, 1145, 275, 1145, 560, 1030, 600, 640, 600 }),
            // Киоск «Карточный вестник» (PlayerHubNews.cs, 2026-10-09): подход к окошку и газета.
            Place("news", "card-city", "Киоск новостей", "Двойка",
                "Новости города, хроника и слухи.",
                1160, 520, new float[] { 1062, 615, 1058, 425, 1095, 408, 1115, 378, 1160, 345, 1205, 378, 1228, 408, 1265, 425, 1262, 615 },
                "news", direct: true),
            // Уличный объект: камера подходит к фонтану, но отдельный экран не открывается.
            Place("fountain", "card-city", "Фонтан", "Главная площадь", null,
                182, 582, new float[] { 3, 671, 39, 663, 40, 634, 45, 623, 62, 616, 94, 613,
                    101, 580, 111, 564, 153, 558, 164, 539, 148, 544, 136, 538, 130, 525, 132, 511,
                    141, 499, 160, 493, 155, 478, 160, 464, 171, 456, 186, 453, 200, 460, 207, 475,
                    205, 487, 200, 496, 216, 496, 229, 507, 234, 522, 230, 536, 218, 543, 202, 541,
                    193, 536, 203, 556, 252, 560, 265, 578, 273, 612, 305, 615, 321, 624, 328, 665,
                    348, 672, 349, 693, 315, 701, 75, 701, 3, 690 }, outdoors: true),
            // Банк — здание с залом внутри (PlayerHubBank.cs, 2026-10-08): дверь ведёт на его этаж.
            Entrance(Place("bank", "card-city-tuzyara", "Банк Тузяры", "Тузяра", null,
                390, 505, new float[] { 150, 600, 150, 210, 180, 185, 190, 140, 215, 135, 240, 180, 255, 45, 300, 40, 350, 15, 470, 40,
                    560, 60, 595, 120, 600, 300, 620, 270, 640, 290, 655, 340, 655, 620, 600, 690, 460, 690, 450, 640, 300, 640, 280, 600 }),
                BankLevel),
            Entrance(Place("casino", "card-city-tuzyara", "Казино", "Дама треф и Тузяра",
                "Карточный дворец: Покер, 21, Карточная Рулетка, Кости, Слоты «777», Туз-Ракета и касса золотых жетонов.",
                905, 560, new float[] { 688, 690, 688, 265, 725, 212, 760, 238, 840, 165, 875, 110, 910, 95, 950, 110, 985, 145,
                    1010, 100, 1050, 120, 1080, 180, 1120, 150, 1160, 95, 1200, 150, 1205, 690 }),
                CasinoLevel),
            Entrance(Place("market", "card-city-port", "Рынок", "Король бубен", null,
                555, 400, new float[] { 120, 590, 120, 400, 200, 370, 280, 300, 385, 200, 400, 110, 470, 110, 515, 90, 545, 60, 565, 48,
                    585, 60, 615, 90, 650, 120, 700, 150, 720, 210, 740, 330, 800, 395, 895, 450, 895, 590 }),
                MarketLevel),
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
            // Дом Шестёрки изнутри (картинка 2196×941): дверь наружу, сам Шестёрка и предметы-разделы. Таблички у них
            // автор пока убрал (2026-10-07), сундук — просто фон.
            Stairs("six-exit", SixRoom, 0, 422, 400,
                new float[] { 332, 650, 332, 150, 362, 100, 417, 82, 477, 100, 507, 150, 507, 640 }),
            Place("six-guide", SixRoom, "Шестёрка", "Шестёрка пик", null, SixX, 540, new float[] {
                572, 245, 565, 267, 571, 333, 545, 375, 547, 400, 516, 514, 508, 515, 508, 550, 548, 570, 590, 566, 595, 623,
                625, 630, 626, 648, 598, 752, 535, 783, 519, 817, 536, 834, 589, 834, 590, 826, 622, 806, 653, 798, 639, 712,
                654, 635, 706, 633, 712, 730, 702, 737, 692, 798, 706, 806, 701, 834, 724, 820, 751, 825, 754, 834, 817, 834,
                830, 818, 819, 788, 759, 762, 740, 729, 734, 634, 786, 628, 779, 524, 819, 531, 834, 523, 848, 461, 866, 445,
                866, 431, 847, 431, 839, 416, 813, 406, 796, 417, 771, 409, 743, 269, 629, 254, 628, 245 }, talk: true),
            Place("six-quests", SixRoom, "Задания", "Шестёрка пик", null, 1262, 225,
                new float[] { 1107, 115, 1419, 115, 1419, 392, 1107, 392 },
                outlinePoints: new float[] { 1107, 115, 1419, 115, 1419, 335, 1107, 335 }),
            // Мел — «Обучение»: Шестёрка спрашивает, показать ли город (PlayerHubSixHouse.cs, AskSixTour).
            Place("six-lessons", SixRoom, "Обучение", "Шестёрка пик", null, 1034, 400,
                new float[] { 942, 262, 1127, 262, 1127, 488, 942, 488 }, talk: true,
                outlinePoints: new float[] { 974, 318, 1098, 312, 1098, 488, 974, 488 }),
            // Столик с колодой — просто интерьер (решение автора, 2026-10-07): «Тренировка» повторяла Клуб — все игры и так
            // с ботами; вернуть, когда появится игра с живыми людьми (IDEAS.md).
            // Зал банка (картинка 2196×941, 21:9 — как комната Шестёрки): нажатие на Тузяру открывает стол и договор;
            // сейф и вальты — фон.
            Place("bank-boss", BankHall, "Тузяра", "Тузяра", null, 1100, 520,
                new float[] { 747, 630, 747, 460, 962, 455, 974, 330, 1024, 250, 1167, 250, 1267, 320, 1267, 455, 1452, 460, 1452, 630 }),
            // Зал таверны (картинка 2196×941, 21:9): Дама за стойкой и Пятёрка на сцене говорят; любой стол — друзья,
            // почтовые ячейки за стойкой — чаты (PlayerHubTavern.cs).
            Place("tavern-queen", TavernHall, "Дама червей", "Дама червей", null, 1098, 300,
                new float[] { 1030, 375, 1030, 215, 1060, 182, 1140, 182, 1168, 215, 1168, 375 }, talk: true),
            Place("tavern-five", TavernHall, "Пятёрка червей", "Пятёрка червей", null, 516, 360,
                new float[] { 440, 525, 440, 300, 460, 205, 590, 205, 600, 300, 600, 525 }, talk: true),
            Place("tavern-mail", TavernHall, "Письма", "Дама червей", null, 1310, 260,
                new float[] { 1212, 180, 1410, 180, 1410, 340, 1212, 340 }, "chats", "Чаты", "Письма друзей", direct: true),
            // Угол кланов за верёвкой (закрыт) — у верёвки дежурят девятки пик, правее почтальонов. Без реплик — просто зал.
            TavernResidentsTalk ? Place("tavern-police", TavernHall, "Девятки пик", "Полиция", null, 1745, 400,
                new float[] { 1540, 150, 1930, 150, 1930, 510, 1660, 510, 1660, 360, 1540, 360 }, talk: true) : null,
            // За всеми столами — завсегдатаи (2026-10-10): стол и стулья перед ними — друзья («подсаживайся»), сами жители —
            // реплика или, пока реплики выключены, тоже друзья. Король червей — один у сцены (лицо у левого края: на 16:9
            // зал надо сдвинуть).
            TavernTable(1, 380, 760, new float[] { 165, 911, 165, 640, 250, 585, 450, 580, 670, 610, 670, 911 }),
            TavernResidents("tavern-king", "Король червей", "Мэр", 330, 520,
                new float[] { 165, 580, 165, 425, 380, 425, 380, 580 }),
            TavernTable(2, 840, 600, new float[] { 664, 692, 664, 500, 1030, 500, 1030, 692 }),
            TavernResidents("tavern-builders", "Восьмёрки-строители", "Восьмёрки", 860, 430,
                new float[] { 690, 500, 690, 420, 740, 360, 1000, 360, 1030, 420, 1030, 500 }),
            TavernTable(3, 1440, 600, new float[] { 1290, 681, 1290, 505, 1650, 505, 1650, 681 }),
            TavernResidents("tavern-postmen", "Четвёрки-почтальоны", "Четвёрки", 1460, 440,
                new float[] { 1290, 505, 1290, 420, 1340, 365, 1600, 365, 1650, 420, 1650, 505 }),
            TavernTable(4, 1700, 740, new float[] { 1504, 941, 1504, 620, 1600, 600, 1800, 600, 1889, 620, 1889, 941 }),
            TavernResidents("tavern-sweethearts", "Семёрки", "Свидание", 1690, 540,
                new float[] { 1520, 600, 1520, 470, 1870, 470, 1870, 600 }),
            // Прилавок Лавки (картинка 2196×941, 21:9): Дама — реплика, пять мест витрины — карточка вещи (PlayerHubShop.cs).
            ShopQueenPlace(),
            ShopSlotPlace(0),
            ShopSlotPlace(1),
            ShopSlotPlace(2),
            ShopSlotPlace(3),
            ShopSlotPlace(4),
            // Зал Архива (PlayerHubArchive.cs, 2026-10-18): витрина, летопись, Туз червей, картотека.
            ArchiveCabinetPlace(),
            ArchiveBookPlace(),
            ArchiveAcePlace(),
            ArchiveDrawersPlace(),
            // Зал Рынка Короля бубен (PlayerHubMarket.cs, 2026-10-19): арка на причал/корабль, прилавок с весами, Король бубен, Семёрка треф.
            MarketShipPlace(),
            MarketScalesPlace(),
            MarketKingPlace(),
            MarketSevenPlace(),
            // Зал Казино Тузяры и Дамы треф (PlayerHubCasino.cs, 2026-10-22): двери, карточный стол, рояль, касса Дамы треф, VIP-балкон Тузяры, рулетка с крупье и арка автоматов.
            CasinoExitPlace(),
            CasinoCardsPlace(),
            CasinoPianoPlace(),
            CasinoCashierPlace(),
            CasinoTuzyaraPlace(),
            CasinoRoulettePlace(),
            CasinoArcadePlace(),
            // Подарок — нарисованная коробка на полке (PlayerHubSixGift.cs).
            Place("six-gift", SixRoom, "Подарок", "Шестёрка пик", null, 1780, 108,
                new float[] { 1682, 40, 1847, 40, 1847, 240, 1682, 240 }, talk: true,
                outlinePoints: new float[] { 1732, 148, 1732, 80, 1762, 62, 1807, 62, 1826, 85, 1826, 148 }),
            // Сохранённые места второго зала. Пока ClubSecondHallEnabled выключен, их картинки
            // не регистрируются в _placePages, поэтому переход, доска и столы недоступны.
            Place("club-passage", ClubBridge, "Переход между залами", "Клуб", null, 850, 350,
                new float[] { 642, 550, 642, 220, 663, 142, 702, 81, 762, 49, 812, 58, 986, 119, 1055, 177,
                    1077, 254, 1077, 552 }, hallPassage: true),
            HallBoard(),
            EmptyClubTable(1, 472),
            EmptyClubTable(2, 830),
            EmptyClubTable(3, 1180),
            EmptyClubTable(4, 1543),
        }, place => place != null);

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
                if (FloorArt[level] != null && FloorArt[level].EndsWith("/" + place.Art, System.StringComparison.Ordinal)) return level;
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
            if (place.Id == "bank-boss") ShowBankLoan();
            else if (place.Id == "six-quests") ShowSixQuests();
            else if (place.Art == ShopCounter) ClickShop(place.Id);
            else if (place.Art == ArchiveHall) ClickArchive(place.Id);
            else if (place.Art == MarketHall) ClickMarket(place.Id);
            else if (place.Art == CasinoHall) ClickCasino(place.Id);
            else if (place.Resident) ToggleResident(place.Id);
            else if (place.Talk && place.Art == TavernHall) TavernTalk(place.Id);
            else if (place.Talk) SixTalk(place.Id);
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
                var t = Mathf.Clamp01(_visitTime / VisitInTime);
                if (t < 1)
                {
                    FrameVisit(FountainVisit ? t : Mathf.SmoothStep(0, 1, t));
                    return;
                }
                // У объекта: уличный остаётся в кадре, здание открывает свой экран.
                _visitPhase = 2;
                _diveShade.gameObject.SetActive(false);
                _diveVignette.gameObject.SetActive(false);
                AimCity(CurrentVisitZoom, 1, VisitFocus());
                FrameTableSigns(1);
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
                AimCity(CurrentVisitZoom, 1, VisitFocus());
                // Фонтан остаётся частью площади: держим приближение до «Отойти».
                if (OutdoorVisit) return;
                // Вернулись на главную («Назад», вкладка) — выезжаем на улицу.
                if (_page != "home") return;
                _visitPhase = 3;
                _visitTime = 0;
                _diveShade.gameObject.SetActive(true);
                _diveVignette.gameObject.SetActive(true);
            }
            var q = Mathf.Clamp01(_visitTime / VisitOutTime);
            if (q < 1)
            {
                FrameVisit(FountainVisit ? 1 - q : 1 - Mathf.SmoothStep(0, 1, q));
                return;
            }
            _cityRig.localScale = Vector3.one;
            _cityRig.anchoredPosition = Vector2.zero;
            FrameTableSigns(0);
            _diveShade.gameObject.SetActive(false);
            _diveVignette.gameObject.SetActive(false);
            _visitPhase = 0;
            _visit = -1;
        }

        // The extra phone zoom belongs to the overview; do not compound it when approaching
        // a table, otherwise residents' heads and the tabletop are cropped by the close-up.
        private float CurrentVisitZoom => _visit >= 0 && IsGameTable(CityPlaces[_visit])
            ? TableVisitZoom / (PlaceLevel(CityPlaces[_visit]) == FirstInterior ? ClubPhoneZoom : 1f) : VisitZoom;

        private Vector2 VisitFocus()
        {
            var place = CityPlaces[_visit];
            var point = PlaceScreenPoint(place, place.Door);
            // Стол встаёт рядом с табличкой; у правой стены зала стороны меняются местами.
            if (IsGameTable(place))
            {
                var side = TablePlaqueOnLeft(place.Route.Substring(5)) ? -1 : 1;
                point.x += side * _cityArea.rect.width * TableAside / CurrentVisitZoom;
                point.y -= _cityArea.rect.height * TableLift / CurrentVisitZoom;
            }
            return point;
        }

        private void ShowOutdoorControls()
        {
            if (_outdoorControls == null)
            {
                _outdoorControls = StageLayer("Outdoor visit controls");
                PinToCorner((RectTransform)CityButton(_outdoorControls, "ОТОЙТИ", 24, 24, 170, 56, CloseOutdoorVisit).transform, -1);
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
            // К фонтану — пешком, со своим кадром (PlayerHubFountain.cs).
            if (FountainVisit) { FrameFountainWalk(depth); return; }
            FrameTableSigns(depth);
            var screen = AimCity(1 + (CurrentVisitZoom - 1) * depth, depth, VisitFocus());
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
            if (id == "fortune") { _page = "fortune"; BuildFortunePage(); return; }
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
