// Copy to the isolated project's Assets/Scripts/Editor; run -executeMethod PlayerHubQa.Run.
// Injects memory-only persistence BEFORE touching any hub, profile or app-flow component.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CardGame.Core.Meta;
using CardGameClient;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

public static class PlayerHubQa
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly string Output = FindOutput();
    private static readonly bool PlacesOnly = string.Equals(Environment.GetEnvironmentVariable("PLAYER_HUB_QA_SCOPE"), "places", StringComparison.OrdinalIgnoreCase);
    private static readonly bool ClubOnly = string.Equals(Environment.GetEnvironmentVariable("PLAYER_HUB_QA_SCOPE"), "club", StringComparison.OrdinalIgnoreCase);
    private static readonly bool GamesOnly = string.Equals(Environment.GetEnvironmentVariable("PLAYER_HUB_QA_SCOPE"), "games", StringComparison.OrdinalIgnoreCase);
    private static readonly bool EntranceOnly = string.Equals(Environment.GetEnvironmentVariable("PLAYER_HUB_QA_SCOPE"), "entrance", StringComparison.OrdinalIgnoreCase);
    private static readonly HashSet<string> LayoutIssues = new HashSet<string>();
    private static readonly List<string> RuntimeErrors = new List<string>();
    private static readonly string[] Routes = { "home", "games", "game:durak", "game:bluff", "game:chests", "game:govno", "game:poker",
        "rooms", "create-room", "quick-game", "profile", "friends", "chats", "shop", "shop-catalog", "inventory", "progress", "achievements",
        "quests", "pass", "bank", "ranks", "tutorial", "settings", "extras",
        "rules:durak", "rules:bluff", "rules:chests", "rules:govno", "rules:poker" };
    private static GameObject canvasObject;
    private static Camera camera;
    private static EventSystem events;
    private static PlayerHubController hub;
    private static MemoryStore store;
    private static FixedClock clock;
    private static Task operation;
    private static int checks, playCalls, quickCalls, avatarCalls, launchedCount;
    private static string launchedGame, quickGame;

    private sealed class MemoryStore : IHubStore
    {
        private HubSnapshot saved;
        public int Writes;
        public bool FailNextSave;
        public Task<HubSnapshot> LoadAsync() => Task.FromResult(saved == null ? null : Clone(saved));
        public Task SaveAsync(HubSnapshot snapshot)
        {
            if (FailNextSave) { FailNextSave = false; throw new IOException("QA injected save failure"); }
            saved = Clone(snapshot); Writes++; return Task.CompletedTask;
        }
        private static HubSnapshot Clone(HubSnapshot snapshot) => JsonUtility.FromJson<HubSnapshot>(JsonUtility.ToJson(snapshot));
    }
    private sealed class FixedClock : IHubClock
    {
        public long Now = 1790500000;
        public long UnixSeconds => Now;
    }

    public static void Run()
    {
        Directory.CreateDirectory(Output);
        var previousError=Path.Combine(Output,"error.txt");
        if(File.Exists(previousError))File.Delete(previousError);
        Application.logMessageReceived += OnLog;
        // This is deliberately the FIRST call involving any hub/profile/app-flow object.
        store = new MemoryStore(); clock = new FixedClock();
        PlayerHubRuntime.UseService(new LocalPlayerHubService(store, clock));
        EditorApplication.update += FinishWhenReady;
        // -executeMethod can run before the renderer has completed its first editor update.
        EditorApplication.delayCall += () => operation = RunChecks();
    }

    private static async Task RunChecks()
    {
        var loaded = await PlayerHubRuntime.LoadAsync();
        Check(loaded.Success && store.Writes > 0, "profile initializes through the injected memory store");
        if (!PlacesOnly && !ClubOnly && !GamesOnly && !EntranceOnly) CheckCanvasDensity();
        Begin(1600, 900);
        var root = new GameObject("Player hub QA", typeof(RectTransform));
        root.transform.SetParent(canvasObject.transform, false);
        ((RectTransform)root.transform).sizeDelta = new Vector2(1600, 900);
        hub = root.AddComponent<PlayerHubController>(); hub.enabled = false;
        hub.Configure(() => playCalls++, () => avatarCalls++, (game, count) => { launchedGame = game; launchedCount = count; },
            game => { quickGame = game; quickCalls++; });
        Call(hub, "Start");
        await WaitFor(() => Get<HubSnapshot>(hub, "_state") != null && !Get<bool>(hub, "_busy"), "initial async hub load");
        Check(Get<string>(hub, "_page") == "home", "new profile opens the uncluttered main menu");
        if (EntranceOnly)
        {
            await CheckTableEntrance();
            Check(LayoutIssues.Count == 0, "table entrances fit all tested screen sizes");
            Check(RuntimeErrors.Count == 0, "no runtime exceptions");
            return;
        }
        if (GamesOnly)
        {
            await CheckGameRules();
            await CheckTablePlaques();
            Check(LayoutIssues.Count == 0, "games and rules fit their boards");
            Check(RuntimeErrors.Count == 0, "no runtime exceptions: " + string.Join(" | ", RuntimeErrors));
            return;
        }
        if (PlacesOnly || ClubOnly)
        {
            if (ClubOnly)
            {
                await CheckClubHall();
                await CheckTablePlaques();
                await CheckGameRules();
                await CheckCityPlaces("tavern", "fountain");
            }
            else await CheckCityPlaces();
            Check(LayoutIssues.Count == 0, "place controls fit and receive their own hits; see layout-issues.txt");
            Check(RuntimeErrors.Count == 0, "no runtime exceptions: " + string.Join(" | ", RuntimeErrors));
            return;
        }
        // Монеты — в верхней панели, а на главной её нет: смотрим на странице раздела.
        hub.Navigate("shop"); await NextFrame();
        CheckCurrencyArt();
        hub.GoHome(); await NextFrame();

        foreach (var route in Routes)
        {
            hub.Navigate(route); await NextFrame();
            Check(Get<string>(hub, "_page") == route, "route resolves: " + route);
            Check(!string.IsNullOrWhiteSpace(Get<Text>(hub, "_pageTitle").text), "page title: " + route);
            InspectLayout(route); Save("hub-" + route.Replace(':', '-') + ".png");
            if (route == "achievements") Save("hub-progress.png");
            if (route == "progress") Save("hub-progress-section.png");
        }

        await CheckCityDistricts();
        await CheckUnderground();
        await CheckNoHill();
        await CheckClubHall();
        await CheckTablePlaques();
        await CheckGameRules();
        await CheckCitySigns();
        await CheckCityPlaces();
        await CheckSectionNavigation();
        await CheckGameSetupDrafts();
        hub.Navigate("profile"); await NextFrame();
        var name = Get<RectTransform>(hub, "_content").GetComponentsInChildren<InputField>().Single();
        name.text = "Тестовый игрок";
        await Click("Сохранить имя");
        Check(PlayerHubRuntime.Current.Profile.DisplayName == "Тестовый игрок" && PlayerProfile.DisplayName == "Тестовый игрок",
            "saved nickname reaches the game-facing profile adapter");
        await Click("Выбрать аватар"); Check(avatarCalls == 1, "avatar button reaches callback");
        Save("hub-profile.png");

        Set(hub, "_hubFriendsTab", "requests"); hub.Navigate("friends"); await NextFrame();
        await Click("Принять");
        Check(PlayerHubRuntime.Current.Friends.Contains("bot-danil"), "accept incoming friend from visible UI");
        Set(hub, "_hubFriendsTab", "friends"); hub.Navigate("friends"); await NextFrame(); Save("hub-friends.png");
        Set(hub, "_hubFriendsTab", "invites"); hub.Navigate("friends"); await NextFrame();
        InspectLayout("incoming-invitations"); Save("hub-invitations.png");
        await Click("Принять");
        Check(CurrentRoom() != null && CurrentRoom().Id == "room-demo-durak"
            && PlayerHubRuntime.Current.Invites.Single(invite => invite.Id == "invite-yegor").Status == "accepted",
            "incoming invitation button joins the advertised room");
        await Dispatch("leave_room");
        await CheckLongFriendChat();
        await Dispatch("cosmetic_unlock", "frame_gold");
        await Dispatch("equip", "frame_gold");
        Check(PlayerHubRuntime.Current.Equipped.Any(item => item.Slot == "frame" && item.ItemId == "frame_gold"), "owned cosmetic equips");
        hub.Navigate("inventory"); await NextFrame(); InspectLayout("inventory-equipped"); Save("hub-inventory.png");

        hub.Navigate("game:poker"); await NextFrame();
        Set(hub, "_roomName", "QA покер"); Set(hub, "_roomCapacity", 4); Set(hub, "_roomStake", "0");
        hub.Navigate("create-room"); await NextFrame(); await Click("Создать");
        var room = CurrentRoom();
        Check(room != null && room.GameId == "poker" && room.MaxPlayers == 4, "created room stores game and capacity");
        await Dispatch("invite_friend", "bot-danil", room.Id);
        Set(hub, "_hubFriendsTab", "invites"); hub.Navigate("friends"); await NextFrame();
        await Click("Демо: принял");
        Check(CurrentRoom().Members.Any(member => member.PlayerId == "bot-danil" && member.Ready && member.IsBot),
            "outgoing invitation local acceptance adds a ready bot");
        while (CurrentRoom().Members.Count < 4) await Click("Добавить бота");
        var longRoomMessage = LongMessage();
        for (var i = 0; i < 3; i++)
        {
            var roomInput = Get<RectTransform>(hub, "_content").GetComponentsInChildren<InputField>().Single();
            roomInput.text = longRoomMessage;
            await Click("Отправить");
        }
        Check(PlayerHubRuntime.Current.Chats.Any(message => message.ConversationId == room.Id && message.Text == longRoomMessage),
            "room chat accepts the complete 400-character message");
        InspectLayout("room-long-chat");
        Check(Get<RectTransform>(hub, "_content").GetComponentsInChildren<ScrollRect>().Any(scroll =>
            scroll.viewport.GetComponent<RectMask2D>() != null && scroll.content.rect.height > scroll.viewport.rect.height),
            "long room chat expands inside a masked scroll viewport");
        Save("hub-room-chat.png");
        await Click("Я готов");
        Check(CurrentRoom().Members.Count == 4 && CurrentRoom().Members.All(member => member.Ready), "room participants ready");
        InspectLayout("room"); Save("hub-room.png");
        var matches = PlayerHubRuntime.Current.Profile.Matches;
        var wins = PlayerHubRuntime.Current.Profile.Wins;
        var xp = PlayerHubRuntime.Current.Profile.XP;
        await Click("Начать игру");
        Check(launchedGame == "poker" && launchedCount == 4, "launch callback carries game and count");
        CheckRoomRoster();
        var token = PlayerHubRuntime.Current.ActiveMatchId;
        Check(!string.IsNullOrEmpty(token), "launch reserves match token");
        HubMatchReporter.ReportMatchCompleted("poker", true);
        await WaitFor(() => PlayerHubRuntime.Current.Profile.Matches == matches + 1, "completion callback");
        Check(PlayerHubRuntime.Current.Profile.Wins == wins + 1 && PlayerHubRuntime.Current.Profile.XP > xp, "completion advances wins and XP");
        var coinsAfter = PlayerHubRuntime.Current.Profile.Coins;
        await PlayerHubRuntime.ExecuteAsync(new HubCommand { Id = "match-result:" + token, Action = "report_match", Target = "poker", Text = token, Flag = true });
        Check(PlayerHubRuntime.Current.Profile.Matches == matches + 1 && PlayerHubRuntime.Current.Profile.Coins == coinsAfter, "replayed result cannot pay twice");
        await Dispatch("leave_room");

        Set(hub, "_roomCode", "DURAK1"); hub.Navigate("rooms"); await NextFrame();
        await Click("Войти по коду");
        Check(CurrentRoom() != null && CurrentRoom().Code == "DURAK1", "join code selects correct room");
        await Dispatch("leave_room");
        hub.Navigate("rooms"); await NextFrame(); Save("hub-rooms.png");
        PlayerHubRuntime.BeginGame("chests");
        await WaitFor(() => !string.IsNullOrEmpty(PlayerHubRuntime.Current.ActiveMatchId), "quick-game token creation");
        HubMatchReporter.ReportMatchCompleted("chests", true, 5);
        await WaitFor(() => PlayerHubRuntime.Current.Profile.Matches == matches + 2, "quick-game completion");
        Check(PlayerHubRuntime.Current.Achievements.Any(a => a.Id == "mentalist" && a.Unlocked), "completion carries five collected chests");
        HubMatchReporter.ReportSpiderKilled();
        await WaitFor(() => PlayerHubRuntime.Current.Achievements.Any(a => a.Id == "arachnophobe" && a.Unlocked), "spider achievement");
        hub.Navigate("achievements"); await NextFrame(); Save("hub-progress.png");
        await Dispatch("tutorial_skip", "durak");
        Check(PlayerHubRuntime.Current.Tutorial.Skipped, "tutorial skip persists");

        var limit = PlayerHubRuntime.Current.Bank.CreditLimit;
        if (limit > 0)
        {
            await Dispatch("bank_request", amount: Math.Min(100, limit));
            Check(PlayerHubRuntime.Current.Bank.Status == "pending", "loan enters pending state");
            clock.Now = Math.Max(clock.Now + 60, PlayerHubRuntime.Current.Bank.ReviewAt + 1);
            await PlayerHubRuntime.RefreshAsync();
            hub.Navigate("bank"); await NextFrame(); InspectLayout("bank-reviewed"); Save("hub-bank.png");
            Check(PlayerHubRuntime.Current.Bank.Status != "pending", "bank review responds to fake clock");
        }

        foreach (var size in new[] { new Vector2Int(2040, 900), new Vector2Int(1280, 720) })
        {
            ResizeCanvas(size.x, size.y);
            ((RectTransform)hub.transform).sizeDelta = new Vector2(size.x, size.y);
            Call(hub, "Resize");
            foreach (var route in Routes)
            { hub.Navigate(route); await NextFrame(); InspectLayout(route + "-" + size.x); }
            hub.Navigate("home"); await NextFrame(); Save("hub-home-" + size.x + ".png");
            CheckCityExtremes(size.x + "x" + size.y);
        }
        // Планшет 4:3: город режется по бокам, но всё равно закрывает экран; потом обратно 1280×720.
        ResizeCanvas(1200, 900); ((RectTransform)hub.transform).sizeDelta = new Vector2(1200, 900); Call(hub, "Resize");
        hub.Navigate("home"); await NextFrame(); CheckCityExtremes("1200x900");
        ResizeCanvas(1280, 720); ((RectTransform)hub.transform).sizeDelta = new Vector2(1280, 720); Call(hub, "Resize");
        Check(store.Writes > 8, "all mutations stayed in injected memory store");
        var stored = await store.LoadAsync();
        Check(stored.Profile.DisplayName == "Тестовый игрок" && stored.Profile.Matches == matches + 2, "isolated store retains changes");
        await CheckAppFlow();
        await CheckInterruptedRecovery();
        await CheckResultSaveRetry();
        File.WriteAllLines(Path.Combine(Output, "layout-issues.txt"), LayoutIssues);
        Check(LayoutIssues.Count == 0, "pages fit, text is readable and controls get their own hit; see layout-issues.txt");
        Check(RuntimeErrors.Count == 0, "no runtime exceptions: " + string.Join(" | ", RuntimeErrors));
    }

    private static void CheckCurrencyArt()
    {
        var silver = HubCurrencyArt.Silver;
        var gold = HubCurrencyArt.Gold;
        Check(silver != null && gold != null, "both currency sprites import from Resources");
        Check(silver != gold && silver.name != gold.name, "silver and gold have distinct sprite identities");
        foreach (var sprite in new[] { silver, gold })
        {
            Check(sprite.texture != null && sprite.rect.width > 0 && sprite.rect.height > 0
                && sprite.rect.xMin >= 0 && sprite.rect.yMin >= 0
                && sprite.rect.xMax <= sprite.texture.width && sprite.rect.yMax <= sprite.texture.height,
                "currency sprite has valid texture bounds: " + sprite.name);
            var visible = hub.GetComponentsInChildren<Image>().Where(image => image.gameObject.activeInHierarchy
                && image.sprite == sprite && !image.transform.IsChildOf(Get<RectTransform>(hub, "_content"))).ToArray();
            Check(visible.Length == 1 && visible[0].preserveAspect && visible[0].rectTransform.rect.width > 0
                && visible[0].rectTransform.rect.height > 0 && visible[0].color.a > 0,
                "header visibly renders the distinct currency artwork: " + sprite.name);
        }
    }

    private static async Task CheckCityDistricts()
    {
        hub.GoHome(); await NextFrame();
        Check(Get<List<string>>(hub, "_districtTitles").SequenceEqual(new[] { "Старый город", "Главная площадь", "Район Тузяры", "Порт" }),
            "city strip holds the Old Town, the square and Tuzyara's district");
        Check(Get<int>(hub, "_district") == 1, "city starts on the main square");
        Canvas.ForceUpdateCanvases();
        var names = hub.GetComponentsInChildren<HubCityArt>().Select(art => art.name).ToArray();
        Check(names.SequenceEqual(new[] { "Outskirts", "city-edge-left", "Старый город", "city-bridge-oldtown-square",
                "Главная площадь", "city-bridge-square-tuzyara", "Район Тузяры", "city-bridge-tuzyara-port", "Порт",
                "city-edge-right", "Outskirts" })
              && hub.GetComponentsInChildren<HubCityArt>().All(art => art.GetComponent<CanvasRenderer>() != null
                  && art.mainTexture is Texture2D && art.rectTransform.rect.width > 0),
            "station end, Old Town, bridge, square, bridge, Tuzyara's district, quay, port and sea end render in order: " + string.Join(", ", names));
        var stops = Get<float[]>(hub, "_cityStops");
        var view = (float)Call(hub, "CityView");
        // Мост — промежуток 40 % картинки и по 240 px смешивания: между районами около 1,4 экрана.
        Check(stops.Length == 4 && Mathf.Abs(stops[3] - stops[2] - 1.4f * view) < .1f * view,
            "the quay makes the walk from Tuzyara to the port about 1.4 screens");
        Check(Mathf.Abs(stops[2] - stops[1] - 1.4f * view) < .1f * view,
            "the bridge makes the walk from the square to Tuzyara about 1.4 screens");
        Check(Mathf.Abs(stops[1] - stops[0] - 1.4f * view) < .1f * view,
            "the bridge makes the walk from the Old Town to the square about 1.4 screens");
        // Автор работает над фоном: на главной только город, ни одной кнопки.
        Check(!ActiveButtons().Any(), "lobby shows only the city");
        CheckCityCovers("square");
        // Проезды: ShowDistrict едет плавно, SettleCity сразу ставит в цель. Update у хаба в проверке
        // выключен — кадры проезда проверка крутит сама (Ride).
        SettleOn(2);
        Check(Get<int>(hub, "_district") == 2, "the next district to the right is Tuzyara's");
        CheckCityCovers("tuzyara");
        InspectLayout("home-tuzyara"); Save("hub-home-tuzyara.png");
        Call(hub, "ShowDistrict", 4);
        Check(Get<int>(hub, "_district") == 3, "the port is the last district: nowhere further right");
        // Кадры на мосту: площадь переходит в район Тузяры одной улицей.
        Set(hub, "_cityCamera", Mathf.Lerp(stops[1], stops[2], .35f)); Call(hub, "PlaceCity");
        CheckCityCovers("bridge"); Save("hub-home-bridge.png");
        Set(hub, "_cityCamera", Mathf.Lerp(stops[1], stops[2], .5f)); Call(hub, "PlaceCity"); Save("hub-home-between.png");
        // Через площадь в Старый город.
        SettleOn(0);
        Check(Get<int>(hub, "_district") == 0, "the district to the left of the square is the Old Town");
        CheckCityCovers("old town");
        InspectLayout("home-old-town"); Save("hub-home-old-town.png");
        Set(hub, "_cityCamera", Mathf.Lerp(stops[0], stops[1], .5f)); Call(hub, "PlaceCity");
        CheckCityCovers("old town to square"); Save("hub-home-old-town-square.png");
        SettleOn(1);
        Check(Get<int>(hub, "_district") == 1, "the city returns to the square");
        CheckCityKeys(stops, view);

        var area = Get<RectTransform>(hub, "_cityArea");
        var middle = RectTransformUtility.WorldToScreenPoint(camera, area.TransformPoint(area.rect.center));
        var hits = new List<RaycastResult>();
        canvasObject.GetComponent<GraphicRaycaster>().Raycast(new PointerEventData(events) { position = middle }, hits);
        Check(hits.Count > 0 && hits[0].gameObject.GetComponent<HubCitySwipe>() != null,
            "empty middle of the lobby passes drags to the city");
        // Медленный свайп: город остаётся там, где его отпустили; рядом с районом — доезжает до середины.
        SettleOn(1);
        Swipe(area, -.2f);
        Check(Get<int>(hub, "_district") == 1 && Mathf.Abs(Get<float>(hub, "_glideTo") - stops[1]) < 1,
            "a short slow drag near the square glides back into it");
        CheckRide(Ride(), stops[1], "back into the square");
        SettleOn(1);
        var dropped = Swipe(area, -.6f);
        Check(Mathf.Abs(Get<float>(hub, "_glideTo") - dropped) < 1 && Get<int>(hub, "_district") == 1,
            "a slow drag leaves the city where the finger let go, even on the bridge — no jump to a district");
        SettleOn(1);
        Swipe(area, .9f);
        Check(Get<int>(hub, "_district") == 0 && Get<float>(hub, "_glideTo") > stops[0] + .4f * view,
            "dragging the city right heads toward the Old Town without leaping into it");
        // Взмах: город катится по инерции и мягко доезжает до района, если остановился рядом.
        SettleOn(1);
        Swipe(area, -.6f, 1.8f);
        Check(Get<int>(hub, "_district") == 2 && Mathf.Abs(Get<float>(hub, "_glideTo") - stops[2]) < 1,
            "a flick from the square rolls on into Tuzyara's district");
        CheckRide(Ride(), stops[2], "flick into Tuzyara", fromMotion: true);
        SettleOn(1);
        Swipe(area, -.2f, 50f);
        Check(Get<float>(hub, "_glideTo") - stops[1] < 1.2f * view,
            "even a violent flick rolls on by about a screen, not through the whole city");
        // Проезд ловится пальцем на ходу.
        SettleOn(1);
        Call(hub, "ShowDistrict", 2);
        for (var frame = 0; frame < 40; frame++) Call(hub, "MoveCity", 1 / 60f);
        var caught = Get<float>(hub, "_cityCamera");
        Swipe(area, 0);
        Check(!Get<bool>(hub, "_gliding") || Mathf.Abs(Get<float>(hub, "_glideTo") - caught) < 1,
            "a finger catches the city mid-ride and it stays there");
        CheckLostRelease(area);

        var last = stops.Length - 1;
        SettleOn(last);
        var glimpseLeft = Get<float>(hub, "_cityGlimpseLeft");
        var glimpseRight = Get<float>(hub, "_cityGlimpseRight");
        Check(glimpseLeft == glimpseRight && glimpseRight > .3f,
            "both painted city ends (station and sea) let you look further than mirrored outskirts would");
        var stretched = (Swipe(area, -.6f) - stops[last]) / view;
        Check(Get<int>(hub, "_district") == last && stretched > 0 && stretched < glimpseRight
              && Mathf.Abs(Get<float>(hub, "_glideTo") - stops[last]) < 1,
            "past the last district the city stretches like rubber and glides back");
        CheckRide(Ride(), stops[last], "rubber band back from the sea");
        var farthest = (Swipe(area, -5f) - stops[last]) / view;
        Check(Get<int>(hub, "_district") == last && farthest < glimpseRight, "however far you pull, only a glimpse of the city end shows");
        // Кадры концов города на пределе: слева вокзал, справа море за маяком, а не пустой фон.
        Set(hub, "_cityCamera", stops[last] + farthest * view); Call(hub, "PlaceCity");
        CheckCityCovers("right edge"); Save("hub-home-edge-right.png");
        Set(hub, "_cityCamera", stops[0] - glimpseLeft * view); Call(hub, "PlaceCity");
        CheckCityCovers("left edge"); Save("hub-home-edge-left.png");
        SettleOn(2);
        Swipe(area, .9f);
        Check(Get<int>(hub, "_district") == 1, "dragging right walks back toward the square");
        SettleOn(1);
        hub.Navigate("shop"); await NextFrame();
        var still = Get<float>(hub, "_cityCamera");
        Swipe(area, -.9f);
        Check(Get<int>(hub, "_district") == 1 && Get<float>(hub, "_cityCamera") == still, "section pages do not scroll the city");
        hub.GoHome(); await NextFrame();
    }

    /// <summary>
    /// Подполье под площадью: лестница на площади нажимается, нырок вниз и наверх склеивается в полной
    /// темноте, S/↓ и W/↑ работают (S вдали от площади сначала доводит до неё), подполье не листается.
    /// </summary>
    private static async Task CheckUnderground()
    {
        hub.GoHome(); await NextFrame();
        var stops = Get<float[]>(hub, "_cityStops");
        Check(Get<int>(hub, "_stairsStop") == 1 && Resources.Load<Sprite>("UI/Hub/card-city-underground") != null,
            "the stairs down are on the main square and the underground art imports");
        var area = Get<RectTransform>(hub, "_cityArea");
        SettleOn(1);
        TapCity(area, new Vector2(.6f, .8f));
        Check(Get<int>(hub, "_diveTarget") == -1, "a tap on the square away from the stairs goes nowhere");
        TapCity(area, new Vector2(.285f, .77f), dragged: true);
        Check(Get<int>(hub, "_diveTarget") == -1, "the end of a drag over the stairs is not a tap");
        TapCity(area, new Vector2(.285f, .77f));
        Check(Get<int>(hub, "_diveTarget") == 1, "a tap on the stairs on the square starts the way down");
        Swipe(area, -.4f);
        Check(!Get<bool>(hub, "_cityDragging") && Get<float>(hub, "_cityCamera") == stops[1], "the city cannot be dragged mid-dive");
        CheckDive(1, "down");
        Check(!Get<RectTransform>(hub, "_cityStrip").gameObject.activeSelf && FloorRect(1).gameObject.activeSelf,
            "down below only the underground is drawn");
        CheckCityCovers("underground");
        InspectLayout("home-underground"); Save("hub-underground.png");
        // Подполье — один экран: не листается ни пальцем, ни клавишами.
        Swipe(area, -.6f);
        Check(!Get<bool>(hub, "_cityDragging") && Get<float>(hub, "_cityCamera") == stops[1], "the underground does not scroll by drag");
        TapCity(area, new Vector2(.5f, .75f));
        Check(Get<int>(hub, "_diveTarget") == -1, "a tap on the underground floor goes nowhere");
        TapCity(area, new Vector2(.16f, .2f));
        Check(Get<int>(hub, "_diveTarget") == 0, "a tap on the underground stairs starts the way up");
        CheckDive(0, "up");
        Check(Get<RectTransform>(hub, "_cityStrip").gameObject.activeSelf && !FloorRect(1).gameObject.activeSelf
              && Get<float>(hub, "_cityCamera") == stops[1], "back up on the square where the stairs are");
        CheckCityCovers("square after the underground");

        var keyboard = InputSystem.AddDevice<Keyboard>("QA stairs keyboard");
        try
        {
            SettleOn(1);
            Press(keyboard, Key.DownArrow);
            Check(Get<int>(hub, "_diveTarget") == 1, "the down arrow on the square goes down at once");
            CheckDive(1, "down arrow");
            Press(keyboard, Key.D);
            Check(!Get<bool>(hub, "_gliding") && Get<int>(hub, "_district") == 1, "D does not walk the street from the underground");
            Press(keyboard, Key.UpArrow);
            Check(Get<int>(hub, "_diveTarget") == 0, "the up arrow climbs back up");
            CheckDive(0, "up arrow");
            // S из района Тузяры: сначала проезд до площади, потом вниз.
            SettleOn(2);
            Press(keyboard, Key.S);
            Check(Get<bool>(hub, "_diveQueued") && Get<bool>(hub, "_gliding") && Get<int>(hub, "_diveTarget") == -1,
                "S far from the square first rides to the stairs");
            CheckRide(Ride(), stops[1], "S rides to the square");
            Check(Get<int>(hub, "_diveTarget") == 1, "arriving at the square by S goes straight down");
            CheckDive(1, "S from Tuzyara");
            Press(keyboard, Key.W);
            CheckDive(0, "W");
            // Передумал по дороге: A/D отменяют спуск.
            SettleOn(2);
            Press(keyboard, Key.S);
            Press(keyboard, Key.D);
            Ride();
            Check(Get<int>(hub, "_diveTarget") == -1 && Get<int>(hub, "_cityLevel") == 0, "a walk key on the way cancels going down");
        }
        finally
        {
            InputSystem.RemoveDevice(keyboard);
        }
        SettleOn(1);
    }

    /// <summary>
    /// Холма нет (решение автора, 2026-09-30): замок за Клубом — только вид, на щите его ворот название
    /// города CARD CITY; нажатие на замок и W/↑ на площади никуда не ведут. Архив — в здании
    /// бывшего полицейского поста в Старом городе, своего поста у полиции нет. Из подполья в зал Клуба
    /// напрямую нельзя — этажи связаны только через улицу.
    /// </summary>
    private static async Task CheckNoHill()
    {
        hub.GoHome(); await NextFrame();
        var area = Get<RectTransform>(hub, "_cityArea");
        var places = ((Array)typeof(PlayerHubController).GetField("CityPlaces", BindingFlags.Static | BindingFlags.NonPublic)
            .GetValue(null)).Cast<object>().ToArray();
        Check(places.All(place => (string)Item(place, "Art") != "card-city-hill" && (string)Item(place, "Id") != "castle"
                                  && (string)Item(place, "Id") != "police"),
            "no hill places, no castle entrance and no police post");
        Check(places.Count(place => (string)Item(place, "Id") == "archive" && (string)Item(place, "Art") == "card-city-old-town") == 1,
            "the Archive stands in the Old Town, in the former police building");
        SettleOn(1);
        Check(PlacePage("card-city").GetComponentsInChildren<RectTransform>().Count(rect => rect.name == "Sign CARD CITY") == 1
              && !PlacePage("card-city").GetComponentsInChildren<RectTransform>().Any(rect => rect.name == "Sign ХОЛМ"),
            "the castle gate board shows the city name instead of the hill");
        TapCity(area, new Vector2(.5f, .5f));
        Check(Get<int>(hub, "_diveTarget") == 2, "a tap on the Club doors dives into the Club hall");
        Call(hub, "MoveDive", 10f);
        DiveNow(0);
        SettleOn(1);
        TapCity(area, new Vector2(.4f, .16f));
        Check(Get<int>(hub, "_diveTarget") == -1 && Get<int>(hub, "_visitPhase") == 0 && Get<string>(hub, "_page") == "home",
            "a tap on the castle or the city name leads nowhere");
        DiveNow(1);
        Call(hub, "StartDive", 2);
        Check(Get<int>(hub, "_diveTarget") == -1 && Get<int>(hub, "_cityLevel") == 1, "floors connect only through the street");
        DiveNow(0);
        var keyboard = InputSystem.AddDevice<Keyboard>("QA no-hill keyboard");
        try
        {
            SettleOn(1);
            Press(keyboard, Key.W);
            Check(Get<int>(hub, "_diveTarget") == -1 && Get<int>(hub, "_cityLevel") == 0, "W on the square no longer climbs anywhere");
        }
        finally
        {
            InputSystem.RemoveDevice(keyboard);
        }
        SettleOn(1);
    }

    /// <summary>
    /// Прокрутить нырок кадрами по 1/60 с и проверить: склейка — в полной темноте, в конце камера
    /// на месте и затемнение снято, весь переход около полутора секунд.
    /// </summary>
    private static void CheckDive(int level, string label)
    {
        var rig = Get<RectTransform>(hub, "_cityRig");
        var shade = Get<Image>(hub, "_diveShade");
        var frames = 0;
        var darkAtCut = 1f;
        var swapped = false;
        var deepest = 1f;
        var gaps = 0;
        while (Get<int>(hub, "_diveTarget") >= 0 && frames < 600)
        {
            var before = shade.color.a;
            Call(hub, "MoveDive", 1 / 60f);
            frames++;
            if (!CityCovered()) gaps++;
            deepest = Mathf.Max(deepest, rig.localScale.x);
            if (!swapped && Get<bool>(hub, "_diveSwapped"))
            {
                swapped = true;
                darkAtCut = Mathf.Min(before, shade.color.a);
            }
            if (frames == 20) Save("hub-dive-" + label.Replace(' ', '-') + "-1.png");
            if (frames == 28) Save("hub-dive-" + label.Replace(' ', '-') + "-deep.png");
            if (frames == 50) Save("hub-dive-" + label.Replace(' ', '-') + "-2.png");
        }
        Check(Get<int>(hub, "_cityLevel") == level && swapped, "dive arrives: " + label);
        Check(gaps == 0, "no frame of the dive shows the backdrop past the picture edge: " + label + " (" + gaps + " frames)");
        Check(darkAtCut > .95f, "the cut happens in full darkness: " + label + " (" + darkAtCut.ToString("0.00") + ")");
        Check(deepest > 2 && frames / 60f < 1.5f, "the camera dives into the stairs and the whole way takes under 1.5 s: " + label);
        Check(rig.localScale == Vector3.one && rig.anchoredPosition.sqrMagnitude < .01f && !shade.gameObject.activeSelf,
            "after the dive the camera is back to normal: " + label);
    }

    /// <summary>
    /// Нажать по месту картинки текущего уровня (доли от левого верхнего угла). Сначала — что на этом
    /// месте никакая кнопка не перекрывает город. <paramref name="dragged"/> — отпускание после свайпа.
    /// </summary>
    private static void TapCity(RectTransform area, Vector2 uv, bool dragged = false)
    {
        var offset = (Vector2)Call(hub, "CityScreenPoint", Get<int>(hub, "_cityLevel"), uv);
        TapScreen(area, RectTransformUtility.WorldToScreenPoint(camera, area.TransformPoint(area.rect.center + offset)), uv.ToString(), dragged);
    }

    private static void TapScreen(RectTransform area, Vector2 screen, string label, bool dragged = false)
    {
        Canvas.ForceUpdateCanvases(); camera.Render();
        var raycaster = canvasObject.GetComponent<GraphicRaycaster>();
        var hits = new List<RaycastResult>();
        raycaster.Raycast(new PointerEventData(events) { position = screen }, hits);
        Check(hits.Count > 0 && hits[0].gameObject.GetComponent<HubCitySwipe>() != null,
            "nothing covers the city at " + label + " (hits: " + string.Join(", ", hits.Select(hit => hit.gameObject.name)) + ")");
        var data = new PointerEventData(events) { position = screen, dragging = dragged,
            pointerPressRaycast = new RaycastResult { module = raycaster } };
        area.GetComponentInChildren<HubCitySwipe>().OnPointerClick(data);
    }

    /// <summary>
    /// Вывески: у каждого места из таблицы своя надпись нужным шрифтом, на своём районе или этаже
    /// видна целиком, заглавные не мельче читаемого на телефоне, надпись на нарисованной табличке
    /// в неё помещается, в зеркальных окраинах вывесок нет. Кадры районов и этажей с вывесками.
    /// </summary>
    private static async Task CheckCitySigns()
    {
        hub.GoHome(); await NextFrame();
        var table = ((Array)typeof(PlayerHubController).GetField("CitySigns", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null)).Cast<object>()
            .Select(row => (Art: (string)Item(row, "Art"), Text: (string)Item(row, "Text"), Width: (float)Item(row, "Width"),
                Height: (float)Item(row, "Height"), Look: Item(row, "Look").ToString(), Grand: (bool)Item(row, "Grand"),
                X: (float)Item(row, "X")))
            .ToArray();
        var signs = hub.GetComponentsInChildren<RectTransform>(true).Where(rect => rect.name.StartsWith("Sign ")).ToArray();
        Check(signs.Length == table.Length && table.All(row => PlacePage(row.Art).GetComponentsInChildren<RectTransform>(true)
                  .Count(sign => sign.name == "Sign " + row.Text) == 1),
            "every place of the sign table has exactly one sign: " + string.Join(", ", signs.Select(sign => sign.name)));
        Check(signs.All(sign => sign.parent.name != "Outskirts"), "no signs in the mirrored outskirts");
        var smallest = float.MaxValue;
        var smallestName = "";
        foreach (var (art, district, level, shot) in new[] { ("card-city-old-town", 0, 0, "old-town"), ("card-city", 1, 0, "square"),
                     ("card-city-tuzyara", 2, 0, "tuzyara"), ("card-city-port", 3, 0, "port"),
                     ("card-city-underground", 1, 1, "underground"),
                     ("club-hall", 1, 2, "club-hall"), ("club-bridge-1-2", 1, 2, "club-bridge"), ("club-hall-2", 1, 2, "club-hall-2") })
        {
            // Этажи связаны только через улицу: сперва наверх, потом куда надо.
            DiveNow(0);
            if (level == 0) SettleOn(district); else DiveNow(level);
            Canvas.ForceUpdateCanvases(); camera.Render();
            foreach (var row in table.Where(row => row.Art == art))
            {
                var holder = PlacePage(art).GetComponentsInChildren<RectTransform>(true).Single(sign => sign.name == "Sign " + row.Text);
                // Зал 21:9 на 16:9 шире экрана — вид подводится к каждой вывеске.
                if (level >= 2) { LookAtArtX(art, row.X); Canvas.ForceUpdateCanvases(); camera.Render(); }
                var label = holder.GetComponentInChildren<Text>();
                Check(holder.gameObject.activeInHierarchy && label != null && label.text == row.Text, "sign is shown: " + row.Text);
                Check(label.font != null && label.font.name == (row.Grand ? "SignDisplay" : "SignNarrow"),
                    "sign uses its sign font: " + row.Text + " (" + label.font?.name + ")");
                var corners = new Vector3[4]; holder.GetWorldCorners(corners);
                var low = camera.WorldToViewportPoint(corners[0]); var high = camera.WorldToViewportPoint(corners[2]);
                Check(low.x > 0 && high.x < 1 && low.y > 0 && high.y < 1, "sign is whole on screen at its place: " + row.Text);
                var textCorners = new Vector3[4]; label.rectTransform.GetWorldCorners(textCorners);
                var pixelsPerUnit = (camera.WorldToScreenPoint(textCorners[1]).y - camera.WorldToScreenPoint(textCorners[0]).y) / label.rectTransform.rect.height;
                var cap = label.fontSize * (row.Grand ? .71f : .81f) * pixelsPerUnit;
                if (cap < smallest) { smallest = cap; smallestName = row.Text; }
                Check(cap >= 14, "sign letters are readable, caps " + cap.ToString("0.0") + " px of 900: " + row.Text);
                if (row.Look != "Plaque")
                {
                    // The drawn glyphs now follow the board's perspective and curvature, so
                    // preferredWidth describes the unwarped input and cannot check the result.
                    var mesh = label.canvasRenderer.GetMesh();
                    var points = mesh.vertices.Select(vertex => holder.InverseTransformPoint(label.transform.TransformPoint(vertex))).ToArray();
                    var margin = 4f; // allow the deliberately drawn small shadow and gold glow
                    Check(points.Length > 0 && points.All(point => !float.IsNaN(point.x) && !float.IsNaN(point.y)
                        && point.x >= holder.rect.xMin - margin && point.x <= holder.rect.xMax + margin
                        && point.y >= holder.rect.yMin - margin && point.y <= holder.rect.yMax + margin),
                        "projected sign glyphs remain inside the board holder: " + row.Text);
                }
            }
            Save("hub-signs-" + shot + ".png");
        }
        Check(true, "smallest sign caps " + smallest.ToString("0.0") + " px: " + smallestName);
        DiveNow(0); SettleOn(1);
    }

    /// <summary>
    /// Места города: у каждого — картинка в городе, светлый контур и точка подхода внутри контура.
    /// Мышь над дверью обводит именно это место, мимо — ничего; над кнопкой и на страницах разделов
    /// контура нет. Нажатие — въезд к двери (город закрывает экран в каждом кадре) и экран места
    /// с названием и кнопкой в готовый раздел; «Назад» — выезд, камера и город там же, где были.
    /// Лестницы и замок — места с этажом: нажатие ведёт на этаж. Фонтан остаётся на улице:
    /// камера приближается, появляются местные кнопки, возврат отводит её назад без смены страницы.
    /// </summary>
    private static async Task CheckCityPlaces(params string[] selectedIds)
    {
        hub.GoHome(); await NextFrame();
        var places = ((Array)typeof(PlayerHubController).GetField("CityPlaces", BindingFlags.Static | BindingFlags.NonPublic)
            .GetValue(null)).Cast<object>().ToArray();
        var contains = typeof(PlayerHubController).GetMethod("Contains", BindingFlags.Static | BindingFlags.NonPublic);
        var placeLevel = typeof(PlayerHubController).GetMethod("PlaceLevel", BindingFlags.Static | BindingFlags.NonPublic);
        var ids = places.Select(place => (string)Item(place, "Id")).ToArray();
        // Без холма (2026-09-30): замка, Турнирного зала, аллеи славы и лестницы холма нет, пост полиции стал Архивом;
        // пианистка в зале Клуба — место-житель (2026-10-01).
        Check(places.Length == 29 && ids.Distinct().Count() == ids.Length, "twenty-nine city places with unique ids: " + string.Join(", ", ids));
        Check(places.Count(place => (bool)Item(place, "Outdoors")) == 1
              && (bool)Item(places.Single(place => (string)Item(place, "Id") == "fountain"), "Outdoors"),
            "the fountain is the city's outdoor interaction");
        var districts = new Dictionary<string, int> { { "card-city-old-town", 0 }, { "card-city", 1 }, { "card-city-tuzyara", 2 }, { "card-city-port", 3 } };
        var area = Get<RectTransform>(hub, "_cityArea");
        var rig = Get<RectTransform>(hub, "_cityRig");
        var swipe = area.GetComponentInChildren<HubCitySwipe>();
        var mouse = InputSystem.AddDevice<Mouse>("QA hover mouse");
        try
        {
            mouse.MakeCurrent();
            swipe.OnPointerEnter(new PointerEventData(events));
            for (var index = 0; index < places.Length; index++)
            {
                var place = places[index];
                var id = ids[index];
                if (selectedIds.Length > 0 && !selectedIds.Contains(id)) continue;
                var art = (string)Item(place, "Art");
                var floor = (int)Item(place, "Floor");
                var outdoors = (bool)Item(place, "Outdoors");
                var door = (Vector2)Item(place, "Door");
                Check(Item(place, "Fill") != null && (bool)contains.Invoke(null, new object[] { Item(place, "Points"), door }),
                    "the place outline is a simple polygon around its door: " + id);
                var level = (int)placeLevel.Invoke(null, new[] { place });
                var direct = (bool)Item(place, "Direct");
                var route = (string)Item(place, "Route");
                DiveNow(0);
                if (level == 0) SettleOn(districts[art]); else DiveNow(level);
                // Зал шире экрана 16:9 — сперва подвести вид к месту.
                if (level >= 2) Call(hub, "LookAtPlace", place);
                var camera0 = Get<float>(hub, "_cityCamera");
                var offset = (Vector2)Call(hub, "PlaceScreenPoint", place, door);
                var screen = RectTransformUtility.WorldToScreenPoint(camera, area.TransformPoint(area.rect.center + offset));
                Hover(mouse, screen);
                var outline = Get<HubPlaceOutline>(hub, "_placeOutline");
                Check((int)Call(hub, "PlaceAt", offset) == index, "the place remains a hit target: " + id);
                if ((bool)Item(place, "HallPassage"))
                    Check(Get<int>(hub, "_placeHover") == -1 && (outline == null || !outline.gameObject.activeInHierarchy),
                        "the arch has no visible hover outline");
                else
                    Check(Get<int>(hub, "_placeHover") == index && outline != null && outline.gameObject.activeInHierarchy
                          && outline.Points.Length == ((Vector2[])Item(place, "Outline")).Length,
                        "the mouse over the door outlines this very place: " + id + " (" + Get<int>(hub, "_placeHover") + ")");
                if (id == "club" || id == "tavern" || id == "bank" || id == "arena" || id == "archive" || id == "market" || outdoors
                    || id == "table-durak" || id == "club-board")
                    Save("hub-place-hover-" + id + ".png");
                var writesBeforeVisit = store.Writes;
                var panBeforeVisit = Get<float>(hub, "_floorPan");
                TapScreen(area, screen, id);
                if ((bool)Item(place, "HoverOnly"))
                {
                    Check(Get<string>(hub, "_page") == "home" && Get<int>(hub, "_visitPhase") == 0
                          && Get<int>(hub, "_diveTarget") == -1 && Get<float>(hub, "_floorPan") == panBeforeVisit
                          && store.Writes == writesBeforeVisit, "an unassigned table only highlights; its click does nothing: " + id);
                    continue;
                }
                if ((bool)Item(place, "Resident"))
                {
                    // Житель с роликом (пианистка): нажатие только останавливает его, никуда не ведёт; второе — запускает.
                    var video = Get<Dictionary<string, HubClubVideo>>(hub, "_clubVideos")[id];
                    Check(Get<string>(hub, "_page") == "home" && Get<int>(hub, "_visitPhase") == 0
                          && Get<int>(hub, "_diveTarget") == -1 && video.Stopped && store.Writes == writesBeforeVisit,
                        "a tap on a resident stops it and leads nowhere: " + id);
                    Call(hub, "ToggleResident", id);
                    continue;
                }
                if ((bool)Item(place, "HallPassage"))
                {
                    Check(Get<string>(hub, "_page") == "home" && Get<int>(hub, "_visitPhase") == 0
                          && Get<int>(hub, "_diveTarget") == -1 && Get<bool>(hub, "_floorPanning"), "the passage pans within the club: " + id);
                    FinishClubPan("passage from place catalog");
                    Check(Mathf.Abs(Get<float>(hub, "_floorPan") - panBeforeVisit) > 50 && store.Writes == writesBeforeVisit,
                        "the passage changes the hall view without touching the profile");
                    continue;
                }
                if (floor >= 0)
                {
                    Check(Get<int>(hub, "_diveTarget") == floor && Get<int>(hub, "_visitPhase") == 0, "stairs lead to their floor: " + id);
                    Call(hub, "MoveDive", 10f);
                    DiveNow(0);
                    continue;
                }
                Check(Get<int>(hub, "_visitPhase") == 1 && Get<int>(hub, "_diveTarget") == -1, "a tap on the place walks to its door: " + id);
                Swipe(area, -.4f);
                Check(!Get<bool>(hub, "_cityDragging"), "the city cannot be dragged on the way to a door: " + id);
                var frames = 0;
                var gaps = 0;
                var deepest = 1f;
                while (Get<int>(hub, "_visitPhase") == 1 && frames < 200)
                {
                    Call(hub, "MoveVisit", 1 / 60f);
                    frames++;
                    if (!CityCovered()) gaps++;
                    deepest = Mathf.Max(deepest, rig.localScale.x);
                    if (frames == 16 && id == "table-durak") Save("hub-place-visit-table.png");
                }
                Call(hub, "UpdateInteriorControls");
                Check(Get<string>(hub, "_page") == (outdoors ? "home" : direct ? route : "place:" + id)
                      && frames / 60f < .85f && deepest > 1.5f && gaps == 0,
                    "the camera approaches smoothly in under 0.85 s and reaches the place: " + id + " (" + frames + " frames, " + gaps + " gaps)");
                if (outdoors)
                {
                    var controls = Get<RectTransform>(hub, "_outdoorControls");
                    Check(Get<int>(hub, "_visitPhase") == 2 && controls != null && controls.gameObject.activeInHierarchy,
                        "the fountain stays outdoors with its local controls");
                    Check(!Get<Image>(hub, "_diveShade").gameObject.activeSelf
                          && !Get<Image>(hub, "_diveVignette").gameObject.activeSelf,
                        "the close fountain view is not covered by the transition shade");
                    for (var frame = 0; frame < 60; frame++) Call(hub, "MoveVisit", 1 / 60f);
                    Check(Get<int>(hub, "_visitPhase") == 2 && Get<string>(hub, "_page") == "home"
                          && Get<float>(hub, "_cityCamera") == camera0 && CityCovered(),
                        "the fountain close view persists without navigating or drifting");
                    Swipe(area, -.4f);
                    Check(!Get<bool>(hub, "_cityDragging"), "the city cannot be dragged while standing by the fountain");
                    Call(hub, "UpdatePlaceHover", 1f);
                    Check(!Get<HubPlaceOutline>(hub, "_placeOutline").gameObject.activeInHierarchy,
                        "hover does not target other places while standing by the fountain");
                    Check(store.Writes == writesBeforeVisit, "approaching the fountain does not change the saved profile or award an achievement");
                    InspectLayout("place-" + id);
                    Save("hub-place-" + id + ".png");
                    var leave = controls.GetComponentsInChildren<Button>().Single();
                    Check(leave.interactable && ButtonLabel(leave).Contains("ОТОЙТИ"), "the fountain has an enabled leave button");
                    leave.onClick.Invoke();
                    Check(Get<int>(hub, "_visitPhase") == 3 && Get<string>(hub, "_page") == "home"
                          && (controls == null || !controls.gameObject.activeInHierarchy),
                        "leaving the fountain starts the walk back and hides its local controls");
                }
                else if (direct)
                {
                    InspectLayout("place-" + id);
                    if (id == "table-durak") Save("hub-place-table.png");
                    hub.Back();
                    Check(Get<string>(hub, "_page") == "home", "back from the game goes out to the hall or the street: " + id);
                }
                else
                {
                    Check(Get<Text>(hub, "_pageTitle").text == (string)Item(place, "Title"), "the place screen is titled with the place: " + id);
                    Call(hub, "UpdatePlaceHover", 1f);
                    Check(!Get<HubPlaceOutline>(hub, "_placeOutline").gameObject.activeInHierarchy, "no outline on a place screen: " + id);
                    InspectLayout("place-" + id);
                    if (route != null)
                        Check(Get<RectTransform>(hub, "_content").GetComponentsInChildren<Button>().Count(button => button.interactable && ButtonLabel(button) == (string)Item(place, "RouteTitle")) == 1,
                            "the place screen leads to its section: " + id);
                    if (id == "fortune") Save("hub-place-soon.png");
                    hub.Back();
                    Check(Get<string>(hub, "_page") == "home", "back from the place goes to the street: " + id);
                }
                frames = 0;
                gaps = 0;
                while (Get<int>(hub, "_visitPhase") != 0 && frames < 200)
                {
                    Call(hub, "MoveVisit", 1 / 60f);
                    frames++;
                    if (!CityCovered()) gaps++;
                }
                Check(frames / 60f < .95f && gaps == 0 && rig.localScale == Vector3.one && rig.anchoredPosition.sqrMagnitude < .01f
                      && !Get<Image>(hub, "_diveShade").gameObject.activeSelf && Get<float>(hub, "_cityCamera") == camera0
                      && Get<int>(hub, "_cityLevel") == level,
                    "the camera walks back out to where it was: " + id);
            }
            DiveNow(0);
            SettleOn(1);
            var fountainIndex = Array.IndexOf(ids, "fountain");
            var exitKeyboard = InputSystem.AddDevice<Keyboard>("QA fountain exit keyboard");
            try
            {
                exitKeyboard.MakeCurrent();
                foreach (var escape in new[] { false, true })
                {
                    Call(hub, "StartVisit", fountainIndex);
                    Call(hub, "MoveVisit", 10f);
                    Check(Get<int>(hub, "_visitPhase") == 2 && Get<string>(hub, "_page") == "home",
                        "the fountain can be approached again before leaving with " + (escape ? "Escape" : "Back"));
                    if (escape) Press(exitKeyboard, Key.Escape); else hub.Back();
                    Check(Get<int>(hub, "_visitPhase") == 3 && !Get<RectTransform>(hub, "_outdoorControls").gameObject.activeInHierarchy,
                        "leaving the fountain with " + (escape ? "Escape" : "Back") + " hides its controls and starts the return");
                    Call(hub, "MoveVisit", 10f);
                    Check(Get<int>(hub, "_visitPhase") == 0 && Get<string>(hub, "_page") == "home" && rig.localScale == Vector3.one,
                        "the street is restored after leaving the fountain with " + (escape ? "Escape" : "Back"));
                }
            }
            finally { InputSystem.RemoveDevice(exitKeyboard); }
            var cobbles = RectTransformUtility.WorldToScreenPoint(camera,
                area.TransformPoint(area.rect.center + (Vector2)Call(hub, "CityScreenPoint", 0, new Vector2(.6f, .85f))));
            Hover(mouse, cobbles);
            Check(Get<int>(hub, "_placeHover") == -1 && !Get<HubPlaceOutline>(hub, "_placeOutline").gameObject.activeSelf,
                "the mouse over the cobblestones outlines nothing");
            var club = Array.IndexOf(ids, "club");
            var clubDoor = RectTransformUtility.WorldToScreenPoint(camera, area.TransformPoint(area.rect.center
                + (Vector2)Call(hub, "PlaceScreenPoint", places[club], Item(places[club], "Door"))));
            swipe.OnPointerExit(new PointerEventData(events));
            Hover(mouse, clubDoor);
            Check(Get<int>(hub, "_placeHover") == -1, "a button over the city takes the mouse: no outline under it");
            swipe.OnPointerEnter(new PointerEventData(events));
            Hover(mouse, clubDoor);
            Check(Get<int>(hub, "_placeHover") == club, "back over the city the Club lights up again");
            hub.Navigate("shop"); await NextFrame();
            Hover(mouse, clubDoor);
            Check(Get<int>(hub, "_placeHover") == -1, "on a section page the city under it lights nothing");
            hub.GoHome(); await NextFrame();
        }
        finally
        {
            InputSystem.RemoveDevice(mouse);
        }
        SettleOn(1);
    }

    /// <summary>
    /// Одна панорама Клуба: первый зал, проход со стендом и второй зал. Проверяем перемещение,
    /// активные и незанятые столы, наведение, переходы в игры и возврат на улицу.
    /// </summary>
    private static async Task CheckClubHall()
    {
        hub.GoHome(); await NextFrame();
        var area = Get<RectTransform>(hub, "_cityArea");
        var stops = Get<float[]>(hub, "_cityStops");
        SettleOn(1);
        TapCity(area, new Vector2(.5f, .5f));
        Check(Get<int>(hub, "_diveTarget") == 2, "the Club doors lead into its hall");
        CheckDive(2, "into the Club");
        Check(FloorRect(2).gameObject.activeSelf && !Get<RectTransform>(hub, "_cityStrip").gameObject.activeSelf,
            "inside the Club only its panorama is drawn");
        CheckCityCovers("club panorama");
        var places = ((Array)typeof(PlayerHubController).GetField("CityPlaces", BindingFlags.Static | BindingFlags.NonPublic)
            .GetValue(null)).Cast<object>().ToArray();
        var placeLevel = typeof(PlayerHubController).GetMethod("PlaceLevel", BindingFlags.Static | BindingFlags.NonPublic);
        var clubPlaces = places.Where(place => (int)placeLevel.Invoke(null, new[] { place }) == 2).ToArray();
        var passive = clubPlaces.Where(place => (bool)Item(place, "HoverOnly")).ToArray();
        Check(passive.Length == 4 && passive.All(place => ((string)Item(place, "Id")).StartsWith("table-club-2-")),
            "the four unassigned tables belong to the second hall");
        Check(passive.All(place => Item(place, "Route") == null && !(bool)Item(place, "Direct") && (int)Item(place, "Floor") == -1),
            "unassigned tables have no game or floor navigation");
        var tables = FloorRect(2).GetComponentsInChildren<Image>().Where(image => image.name.StartsWith("Table ")).ToArray();
        Check(tables.Length == 8 && tables.All(table => table.sprite != null && table.sprite.name == "club-table"),
            "both halls keep all eight Club table places");
        var firstTables = PlacePage("club-hall").GetComponentsInChildren<Image>().Where(image => image.name.StartsWith("Table ")).ToArray();
        var nextTables = PlacePage("club-hall-2").GetComponentsInChildren<Image>().Where(image => image.name.StartsWith("Table ")).ToArray();
        Check(firstTables.Length == 4 && nextTables.Length == 4, "each hall owns its four table images");
        // Первый зал живой: у всех четырёх его столов живой слой, картинка стола только держит место мебели;
        // столы второго зала рисуются кодом.
        var liveTables = new[] { "Дурак", "Верю — не верю", "Сундучки", "Говно" };
        Check(firstTables.Where(table => !table.enabled).Select(table => table.name).OrderBy(name => name)
                  .SequenceEqual(liveTables.Select(title => "Table " + title).OrderBy(name => name))
              && nextTables.All(table => table.enabled),
            "only the first hall's finished tables hide their furniture images under the live layers");
        Check(liveTables.All(title => PlacePage("club-hall").GetComponentsInChildren<Image>()
                .Any(image => image.name == "Live " + title && image.sprite != null)),
            "all four finished tables of the first hall lie over it as sharp live layers");
        // Пианистка — ролики поверх зала: под живыми слоями столов, с маской, без звука; нажатие — перестаёт и
        // сидит, сложив руки, второе — снова играет; выключенная музыка тоже её останавливает.
        var pianist = PlacePage("club-hall").GetComponentsInChildren<HubClubVideo>(true).SingleOrDefault(video => video.name == "Video club-pianist");
        Check(pianist != null && pianist.Player != null && pianist.HasIdle
              && pianist.GetComponentsInChildren<UnityEngine.Video.VideoPlayer>(true).Length == 2
              && pianist.GetComponentsInChildren<UnityEngine.Video.VideoPlayer>(true).All(player =>
                  player.audioOutputMode == UnityEngine.Video.VideoAudioOutputMode.None)
              && pianist.GetComponentsInChildren<RawImage>(true).All(image => image.material == pianist.Material)
              && pianist.Material.shader.name == "CardCity/HubVideoMask" && pianist.Material.GetTexture("_MaskTex") != null,
            "the pianist plays as silent masked videos over the painted hall, with a resting pose");
        if (pianist != null)
        {
            var liveIndex = PlacePage("club-hall").GetComponentsInChildren<Image>().First(image => image.name.StartsWith("Live ")).transform.GetSiblingIndex();
            Check(pianist.transform.GetSiblingIndex() < liveIndex, "the pianist video lies under the live table layers");
            var pianistPlace = Array.IndexOf(places, places.Single(place => (string)Item(place, "Id") == "club-pianist"));
            Check((bool)Item(places[pianistPlace], "Resident"), "the pianist is a resident place: a tap toggles her, it leads nowhere");
            var page = Get<string>(hub, "_page");
            Call(hub, "ToggleResident", "club-pianist");
            Check(pianist.Stopped && !pianist.Wanted && Get<string>(hub, "_page") == page, "a tap stops the pianist");
            Call(hub, "ToggleResident", "club-pianist");
            Check(!pianist.Stopped, "a second tap makes the pianist play again");
            var music = PlayerHubRuntime.Current.Settings.Music;
            PlayerHubRuntime.Current.Settings.Music = false;
            Check(!pianist.Wanted, "with the music off the pianist does not play");
            PlayerHubRuntime.Current.Settings.Music = music;
        }
        Check(firstTables.Select(table => table.transform.Find("Felt").GetComponent<Image>().color).Distinct().Count() == 4,
            "the first hall retains its four game colours");
        var nextFelts = nextTables.Select(table => table.transform.Find("Felt").GetComponent<Image>().color).ToArray();
        Check(nextFelts.Distinct().Count() == 1 && nextFelts[0].b > nextFelts[0].r,
            "all four unassigned tables share blue felt");
        Call(hub, "UpdateInteriorControls");
        var leave = ActiveButtons().Where(button => ButtonLabel(button).Contains("ВЫЙТИ")).ToArray();
        Check(leave.Length == 1 && leave[0].GetComponent<Image>().sprite != null && leave[0].GetComponent<Image>().sprite.name == "sign-plaque",
            "the hall has one leave button in the city sign style");
        var limit = (float)Call(hub, "FloorPanLimit", 2);
        Check(limit > area.rect.width * .5f && Get<float>(hub, "_floorPan") <= -limit + .5f,
            "the joined halls open at the first hall's entrance and extend beyond one screen");
        InspectLayout("club-hall-1"); Save("hub-club-hall-1.png");
        Swipe(area, -.6f);
        Check(!Get<bool>(hub, "_cityDragging") && Get<int>(hub, "_cityLevel") == 2 && Get<float>(hub, "_floorPan") > -limit + 50,
            "a drag pans the hall, not the street");
        Swipe(area, -10f);
        Check(Mathf.Abs(Get<float>(hub, "_floorPan") - limit) < .5f && CityCovered(), "a long swipe reaches the second hall's far wall");
        InspectLayout("club-hall-2"); Save("hub-club-hall-2.png");
        var streetCamera = Get<float>(hub, "_cityCamera");
        var keyboard = InputSystem.AddDevice<Keyboard>("QA hall keyboard");
        var mouse = InputSystem.AddDevice<Mouse>("QA club hover mouse");
        try
        {
            keyboard.MakeCurrent(); mouse.MakeCurrent();
            area.GetComponentInChildren<HubCitySwipe>().OnPointerEnter(new PointerEventData(events));
            foreach (var place in clubPlaces.Where(item => ((string)Item(item, "Id")).StartsWith("table-")))
                CheckTableFurnitureOutline(place);
            foreach (var place in clubPlaces.Where(item => (string)Item(item, "Art") == "club-hall"
                         && ((string)Item(item, "Id")).StartsWith("table-")))
            {
                HoverClubPlace(place, places, mouse);
                var nameScreen = ClubTableNameScreen(place);
                Hover(mouse, nameScreen);
                Check(Get<int>(hub, "_placeHover") == Array.IndexOf(places, place)
                      && Get<HubPlaceOutline>(hub, "_placeOutline").gameObject.activeInHierarchy,
                    "hovering the game name selects its table furniture: " + (string)Item(place, "Id"));
                CheckRenderedFurnitureOutline(place);
                if ((string)Item(place, "Id") == "table-durak") Save("hub-club-table-furniture-hover.png");
            }
            foreach (var place in passive)
            {
                var id = (string)Item(place, "Id");
                var screen = HoverClubPlace(place, places, mouse);
                var beforeProfile = JsonUtility.ToJson(PlayerHubRuntime.Current.Profile);
                var beforeWrites = store.Writes;
                var beforePan = Get<float>(hub, "_floorPan");
                TapScreen(area, screen, id);
                Call(hub, "MoveVisit", 1f);
                Check(Get<string>(hub, "_page") == "home" && Get<int>(hub, "_visitPhase") == 0
                      && Get<int>(hub, "_diveTarget") == -1 && Get<int>(hub, "_cityLevel") == 2
                      && Get<float>(hub, "_floorPan") == beforePan && !Get<bool>(hub, "_floorPanning"),
                    "unassigned table highlights but its click starts no movement or navigation: " + id);
                Check(store.Writes == beforeWrites && JsonUtility.ToJson(PlayerHubRuntime.Current.Profile) == beforeProfile,
                    "unassigned table click leaves the profile unchanged: " + id);
                if (id == "table-club-2-2") Save("hub-club-empty-table-hover.png");
            }

            // Return by moving through the same panorama, then travel to the second hall with keys.
            Swipe(area, 10f);
            Check(Mathf.Abs(Get<float>(hub, "_floorPan") + limit) < .5f && Get<int>(hub, "_cityLevel") == 2,
                "swiping back returns to the first hall without a floor transition");
            for (var step = 0; step < 20 && Get<float>(hub, "_floorPan") < limit - .5f; step++)
            { Press(keyboard, Key.D); FinishClubPan("D through club"); }
            Check(Mathf.Abs(Get<float>(hub, "_floorPan") - limit) < .5f && Get<float>(hub, "_cityCamera") == streetCamera,
                "D reaches the second hall without moving the street camera");
            for (var step = 0; step < 20 && Get<float>(hub, "_floorPan") > -limit + .5f; step++)
            { Press(keyboard, Key.A); FinishClubPan("A through club"); }
            Check(Mathf.Abs(Get<float>(hub, "_floorPan") + limit) < .5f, "A returns all the way to the first hall");

            var passage = clubPlaces.Single(place => (string)Item(place, "Id") == "club-passage");
            var passageScreen = HoverClubPlace(passage, places, mouse);
            var passagePan = Get<float>(hub, "_floorPan");
            var writesBeforePassage = store.Writes;
            Save("hub-club-bridge-hover.png");
            TapScreen(area, passageScreen, "club-passage");
            Check((bool)Item(passage, "HallPassage") && Get<bool>(hub, "_floorPanning")
                  && Get<string>(hub, "_page") == "home" && Get<int>(hub, "_visitPhase") == 0 && Get<int>(hub, "_diveTarget") == -1,
                "the arch starts a pan between halls, not a page or a visit");
            FinishClubPan("arch crossing");
            Check(Get<float>(hub, "_floorPan") > 0 && Mathf.Abs(Get<float>(hub, "_floorPan") - passagePan) > 100 && store.Writes == writesBeforePassage,
                "crossing the arch moves within the panorama without changing the profile");
            passageScreen = HoverClubPlace(passage, places, mouse);
            TapScreen(area, passageScreen, "club-passage return");
            FinishClubPan("arch crossing back");
            Check(Get<float>(hub, "_floorPan") < 0 && store.Writes == writesBeforePassage,
                "the same arch returns from the second hall to the first");

            foreach (var id in new[] { "club-board-2", "table-durak" })
            {
                var place = clubPlaces.Single(item => (string)Item(item, "Id") == id);
                var screen = HoverClubPlace(place, places, mouse);
                if (id == "table-durak")
                {
                    screen = ClubTableNameScreen(place);
                    Hover(mouse, screen);
                    CheckRenderedFurnitureOutline(place);
                }
                if (id == "club-board-2")
                {
                    Check(!PlacePage("club-bridge-1-2").GetComponentsInChildren<Text>().Any(text => text.text == "ИГРЫ"),
                        "the passage between the halls carries no games sign");
                    Check(!PlacePage("club-hall-2").GetComponentsInChildren<Image>().Any(image => image.name == "Games board"),
                        "the second hall's board is painted into the hall, not pasted over it");
                    var sign = PlacePage("club-hall-2").GetComponentsInChildren<Text>().Single(text => text.text == "ИГРЫ");
                    Check(sign.GetComponent<HubSignText>() != null && sign.font != null && sign.font.name == "SignDisplay",
                        "the second hall's board visibly carries the games sign in the sign font");
                    var mesh = sign.canvasRenderer.GetMesh();
                    Check(mesh != null && mesh.vertexCount > 0, "the second hall's games sign has a rendered text mesh");
                    Save("hub-club-games-board-hover.png");
                }
                var savedPan = Get<float>(hub, "_floorPan");
                TapScreen(area, screen, id);
                Check(Get<int>(hub, "_visitPhase") == 1, "an assigned place still approaches after panning: " + id);
                FinishClubVisit(true, id);
                Check(Get<string>(hub, "_page") == (id == "club-board-2" ? "games" : "game:durak"),
                    "the assigned place opens its existing destination: " + id);
                InspectLayout(id);
                hub.Back();
                FinishClubVisit(false, id);
                Check(Get<string>(hub, "_page") == "home" && Get<int>(hub, "_cityLevel") == 2
                      && Mathf.Abs(Get<float>(hub, "_floorPan") - savedPan) < .5f,
                    "back from the page restores the same place in the joined halls: " + id);
            }

            foreach (var size in new[] { new Vector2Int(2100, 900), new Vector2Int(1600, 900), new Vector2Int(1280, 720) })
            {
                ResizeCanvas(size.x, size.y); ((RectTransform)hub.transform).sizeDelta = new Vector2(size.x, size.y); Call(hub, "Resize");
                foreach (var art in new[] { "club-hall", "club-bridge-1-2", "club-hall-2" })
                {
                    LookAtArtX(art, ArtPictureSize(art).x / 2);
                    CheckCityCovers(art + " " + size.x + "x" + size.y);
                    InspectLayout(art + "-" + size.x);
                    Save("hub-" + art + "-" + size.x + "x" + size.y + ".png");
                }
            }
            foreach (var cross in new[] { true, false })
            {
                ResizeCanvas(1280, 720); ((RectTransform)hub.transform).sizeDelta = new Vector2(1280, 720); Call(hub, "Resize");
                var startArt = cross ? "club-hall" : "club-hall-2";
                LookAtArtX(startArt, ArtPictureSize(startArt).x / 2);
                if (cross) Call(hub, "CrossClubHall"); else Press(keyboard, Key.D);
                Call(hub, "MoveFloorPan", 1 / 60f);
                Check(Get<bool>(hub, "_floorPanning"), "a pan is in progress before widening the window");
                ResizeCanvas(2100, 900); ((RectTransform)hub.transform).sizeDelta = new Vector2(2100, 900); Call(hub, "Resize");
                CheckCityCovers("resize during " + (cross ? "arch crossing" : "keyboard pan"));
                FinishClubPan("resized during " + (cross ? "arch crossing" : "keyboard pan"), alreadyMoving: true);
                Check(Mathf.Abs(Get<float>(hub, "_floorPan")) <= (float)Call(hub, "FloorPanLimit", 2) + .5f,
                    "a resized pan settles at a reachable point within the walls");
            }
            ResizeCanvas(1600, 900); ((RectTransform)hub.transform).sizeDelta = new Vector2(1600, 900); Call(hub, "Resize");
            LookAtArtX("club-hall-2", ArtPictureSize("club-hall-2").x / 2);
            Call(hub, "UpdateInteriorControls");
            leave[0].onClick.Invoke();
            Check(Get<int>(hub, "_diveTarget") == 0, "the leave button in the second hall leads outside");
            CheckDive(0, "out of second Club hall");
            Check(Get<int>(hub, "_cityLevel") == 0 && Get<float>(hub, "_cityCamera") == stops[1], "leaving either hall returns to the Club on the square");
            Call(hub, "UpdateInteriorControls");
            Check(!ActiveButtons().Any(button => ButtonLabel(button).Contains("ВЫЙТИ")), "no Club leave button remains on the street");
            foreach (var exit in new[] { "Back", "Escape", "S" })
            {
                DiveNow(2);
                LookAtArtX("club-hall-2", ArtPictureSize("club-hall-2").x / 2);
                if (exit == "Back") hub.Back(); else Press(keyboard, exit == "Escape" ? Key.Escape : Key.S);
                Check(Get<int>(hub, "_diveTarget") == 0, exit + " in the second hall starts the street exit");
                Call(hub, "MoveDive", 10f);
                Check(Get<int>(hub, "_cityLevel") == 0 && Get<float>(hub, "_cityCamera") == stops[1], exit + " returns to the correct street entrance");
            }
        }
        finally
        {
            InputSystem.RemoveDevice(keyboard);
            InputSystem.RemoveDevice(mouse);
        }
        SettleOn(1);
    }

    /// <summary>
    /// Табличка у стола: из списка игр камера сама едет к столу, табличка встаёт сбоку и не закрывает его;
    /// настройки раскрываются в ней же; покер — у казино, из Клуба камера выходит на улицу; Esc — «Отойти».
    /// </summary>
    private static async Task CheckTableEntrance()
    {
        foreach (var game in new[] { "durak", "bluff", "chests", "govno", "poker" })
        {
            hub.Navigate("game:" + game); await NextFrame();
            DriveTableTrip(game, "new entrance " + game); await NextFrame();
            CheckPlaqueBesideTable(game);
            var pane = Get<RectTransform>(hub, "_content").Find("Table plaque") as RectTransform;
            Check(pane.rect.width == 800 && pane.rect.height == 820, "large fixed table panel: " + game);
            await ChooseNonDefaultRules(game);
            var before = quickCalls;
            await Click("Начать с ботами");
            Check(quickCalls == before + 1 && quickGame == game, "bot launch uses selected game: " + game);
            InspectLayout("entrance-bots-" + game); Save("entrance-" + game + ".png");
            var draft = hub.GetGameSetupSettings(); var count = hub.GetGameSetupPlayerCount();
            await Click("Своя комната");
            Check(hub.GetGameSetupPlayerCount() == count && SameRules(hub.GetGameSetupSettings(), draft),
                "create and bots share the same rules: " + game);
            InspectLayout("entrance-create-top-" + game);
            Check(Get<RectTransform>(hub, "_content").GetComponentInChildren<ScrollRect>().content.anchoredPosition.y < 1,
                "new create form starts at its name and access fields");
            if (game == "durak") Save("entrance-create-top.png");
            var scroll = Get<RectTransform>(hub, "_content").GetComponentInChildren<ScrollRect>();
            scroll.verticalNormalizedPosition = 0; await NextFrame();
            InspectLayout("entrance-create-bottom-" + game); Save("entrance-create-" + game + ".png");
            Check(ActiveButtons().Any(button => ButtonLabel(button) == "Создать комнату"), "create stays visible while scrolling");
            await Click("По коду");
            var input = Get<RectTransform>(hub, "_content").GetComponentInChildren<InputField>();
            input.text = "";
            Check(!ActiveButtons().Any(button => ButtonLabel(button) == "Войти по коду"), "empty code cannot be submitted");
            if (game == "durak") Save("entrance-code.png");
            input.text = "MISSING"; await Click("Войти по коду");
            Check(Get<string>(hub, "_page") == "game:" + game, "invalid code stays at selected table");
            InspectLayout("entrance-code-" + game);
            await Click("Быстрая игра"); InspectLayout("entrance-quick-" + game);
            Save("entrance-quick-" + game + ".png");
            await Click("С ботами");
            await Click("Правила");
            Check(Get<string>(hub,"_page") == "rules:" + game, "rules open for selected game");
            await Click("Вернуться к столу");
            foreach (var size in new[] { new Vector2Int(1280,720), new Vector2Int(2100,900) })
            {
                ResizeCanvas(size.x,size.y); ((RectTransform)hub.transform).sizeDelta = new Vector2(size.x,size.y);
                Call(hub,"Resize"); Call(hub,"FrameVisit",1f); await NextFrame();
                CheckPlaqueBesideTable(game); InspectLayout("entrance-" + game + "-" + size.x);
                if (game == "durak") Save("entrance-durak-" + size.x + ".png");
            }
            ResizeCanvas(1600,900); ((RectTransform)hub.transform).sizeDelta = new Vector2(1600,900);
            Call(hub,"Resize"); Call(hub,"FrameVisit",1f); await NextFrame();
        }
        hub.Navigate("game:bluff"); await NextFrame();
        await Click("Своя комната");
        Get<RectTransform>(hub,"_content").GetComponentInChildren<ScrollRect>().verticalNormalizedPosition = 1;
        await NextFrame();
        var fields = Get<RectTransform>(hub,"_content").GetComponentsInChildren<InputField>();
        fields[0].text="QA friends"; fields[1].text="0";
        await Click("Только по коду"); await Click("Создать комнату");
        var created = CurrentRoom();
        Check(created != null && created.GameId == "bluff" && created.Private && created.Name == "QA friends", "inline creation reaches a real room with selected access and game");
        var code = created.Code;
        await Click("Добавить бота"); await Click("Покинуть комнату");
        hub.Navigate("game:bluff"); await NextFrame(); await Click("По коду");
        Get<RectTransform>(hub,"_content").GetComponentInChildren<InputField>().text="  " + code.ToLowerInvariant() + "  ";
        await Click("Войти по коду");
        Check(CurrentRoom()?.Code == code && Get<string>(hub,"_page") == "room", "trimmed case-insensitive code rejoins the room");
        await Click("Покинуть комнату");
        hub.Navigate("game:durak"); await NextFrame(); await Click("Быстрая игра");
        var available = PlayerHubRuntime.Current.Rooms.Any(room => room.GameId == "durak" && !room.Private && !room.Started && room.Stake==0 && room.Members.Count<room.MaxPlayers);
        if (available)
        {
            await Click("Найти свободный стол");
            Check(CurrentRoom()?.GameId == "durak" && CurrentRoom().Stake == 0, "quick join selects a free open room for the correct game");
        }
    }

    private static async Task CheckTablePlaques()
    {
        var keyboard = InputSystem.AddDevice<Keyboard>("QA plaque keyboard");
        try
        {
            keyboard.MakeCurrent();
            hub.GoHome(); await NextFrame();
            DiveNow(0); DiveNow(2);
            hub.Navigate("games"); await NextFrame();
            await ClickGameRow("chests", "Выбрать игру");
            Check(Get<string>(hub, "_page") == "game:chests" && ActiveButtons().Any(button => ButtonLabel(button) == "Начать с ботами"),
                "the plaque is shown at once while the camera heads to the table");
            DriveTableTrip("chests", "chests from the games list");
            await NextFrame();
            CheckPlaqueBesideTable("chests");
            InspectLayout("table-plaque-chests"); Save("hub-table-plaque-chests.png");
            await Click("С ботами");
            var labels = Get<RectTransform>(hub, "_content").GetComponentsInChildren<Text>().Select(text => text.text).ToArray();
            Check(labels.Contains("Игроков за столом") && labels.Contains("Колода") && ActiveButtons().Any(button => ButtonLabel(button) == "Начать с ботами"),
                "table settings open inside the plaque next to the sit-down action");
            CheckPlaqueBesideTable("chests");
            InspectLayout("table-plaque-settings"); Save("hub-table-plaque-settings.png");
            await Click("С ботами");
            Check(Get<RectTransform>(hub, "_content").GetComponentsInChildren<Text>().Any(text => text.text == "Колода"),
                "bot mode keeps the rules visible");

            hub.Navigate("game:poker"); await NextFrame();
            DriveTableTrip("poker", "the casino from inside the Club");
            Check(Get<int>(hub, "_cityLevel") == 0, "poker leads out of the Club to the casino on the street");
            await NextFrame();
            CheckPlaqueBesideTable("poker");
            InspectLayout("table-plaque-poker"); Save("hub-table-plaque-poker.png");

            // Esc — как «Отойти»: табличка закрывается, на улице камера отъезжает от казино.
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape)); InputSystem.Update();
            Call(hub, "ClubKeys", false);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update();
            Call(hub, "ClubKeys", false);
            Check(Get<string>(hub, "_page") != "game:poker", "Escape steps away from the table plaque");
            hub.GoHome(); await NextFrame();
            for (var frame = 0; frame < 200 && Get<int>(hub, "_visitPhase") != 0; frame++) Call(hub, "MoveVisit", 1 / 60f);
            Check(Get<int>(hub, "_visitPhase") == 0 && Get<int>(hub, "_tableTrip") == -1, "home leaves the table and cancels any trip");
        }
        finally
        {
            InputSystem.RemoveDevice(keyboard);
        }
        SettleOn(1);
    }

    private static int TableIndex(string game) => (int)typeof(PlayerHubController)
        .GetMethod("TablePlace", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { game });

    /// <summary>
    /// Список четырёх игр Клуба листается, у каждой — превью, «Выбрать игру» и «?»; покер доступен в казино. «?» ведёт к правилам
    /// и из списка, и с таблички у стола, а из правил — к столу или обратно к нему.
    /// </summary>
    private static async Task CheckGameRules()
    {
        hub.GoHome(); await NextFrame();
        hub.Navigate("games"); await NextFrame();
        var content = Get<RectTransform>(hub, "_content");
        var rows = content.GetComponentsInChildren<RectTransform>().Where(rect => rect.name.StartsWith("Game row ", StringComparison.Ordinal)).ToArray();
        Check(rows.Length == 4 && rows.All(row => row.name != "Game row poker")
            && rows.All(row => row.GetComponentsInChildren<Button>().Select(ButtonLabel).OrderBy(label => label, StringComparer.Ordinal)
                .SequenceEqual(new[] { "?", "Выбрать игру" }))
            && rows.All(row => row.GetComponentsInChildren<RectTransform>().Any(rect => rect.name == "Preview")),
            "every game in the list has a preview, a choose action and a rules sign");
        Check(new[] { "Дурак", "Верю — не верю", "Сундучки", "Говно" }.All(title => rows.Any(row =>
                row.GetComponentsInChildren<Text>().Any(text => text.text == title)
                && row.GetComponentsInChildren<Image>().Any(image => image.name == "Preview picture" && image.sprite != null))),
            "the four Club games show their painted previews");
        var list = content.GetComponentInChildren<ScrollRect>();
        Check(list is HubSmoothScroll && list.viewport.GetComponent<RectMask2D>() != null && list.content.rect.height > list.viewport.rect.height + 300,
            "the games list scrolls down inside a masked viewport");
        await ClickGameRow("govno", "?");
        Check(Get<string>(hub, "_page") == "rules:govno", "the last Club game's rules sign is reachable after scrolling");
        await Click("НАЗАД");
        Check(Get<string>(hub, "_page") == "games" && content.GetComponentInChildren<ScrollRect>().content.anchoredPosition.y > 100,
            "back from the rules returns to the same place in the list");
        InspectLayout("games-scrolled"); Save("hub-games-scrolled.png");

        await ClickGameRow("bluff", "?");
        Check(Get<string>(hub, "_page") == "rules:bluff", "the rules sign opens that game's rules");
        var sheet = content.GetComponentsInChildren<ScrollRect>().FirstOrDefault(scroll => scroll.name == "Scroll rules:bluff");
        var texts = sheet == null ? new string[0] : sheet.content.GetComponentsInChildren<Text>().Select(text => text.text).ToArray();
        Check(sheet != null && texts.Contains("Цель") && texts.Contains("Перестрелка") && sheet.content.rect.height > sheet.viewport.rect.height,
            "the rules sheet lists every section and scrolls");
        sheet.content.anchoredPosition = new Vector2(0, sheet.content.rect.height - sheet.viewport.rect.height);
        await NextFrame(); Save("hub-rules-bluff-end.png");
        await Click("Выбрать игру");
        Check(Get<string>(hub, "_page") == "game:bluff", "choosing the game from its rules leads to its table");
        DriveTableTrip("bluff", "bluff from its rules");
        await NextFrame();
        await Click("Правила");
        Check(Get<string>(hub, "_page") == "rules:bluff" && ActiveButtons().Any(button => ButtonLabel(button) == "Вернуться к столу"),
            "the plaque's rules sign opens the rules with a way back to the table");
        InspectLayout("rules-from-table"); Save("hub-rules-from-table.png");
        await Click("Вернуться к столу");
        Check(Get<string>(hub, "_page") == "game:bluff" && (bool)Call(hub, "AtTable", TableIndex("bluff"))
            && ActiveButtons().Any(button => ButtonLabel(button) == "Начать с ботами"),
            "back from the rules the player still stands at the same table");
        // Дальше проверки ждут улицу: отойти от стола и выйти из Клуба.
        hub.GoHome(); await NextFrame();
        for (var frame = 0; frame < 200 && Get<int>(hub, "_visitPhase") != 0; frame++) Call(hub, "MoveVisit", 1 / 60f);
        DiveNow(0);
        SettleOn(1);
    }

    /// <summary>Кнопка в строке листаемого списка игр: список сначала прокручивается к этой строке.</summary>
    private static async Task ClickGameRow(string game, string label)
    {
        var row = Get<RectTransform>(hub, "_content").GetComponentsInChildren<RectTransform>().FirstOrDefault(rect => rect.name == "Game row " + game);
        Check(row != null, "games list row: " + game);
        if (row == null) return;
        var scroll = row.GetComponentInParent<ScrollRect>();
        var limit = Mathf.Max(0, scroll.content.rect.height - scroll.viewport.rect.height);
        scroll.StopMovement();
        scroll.content.anchoredPosition = new Vector2(0, Mathf.Clamp(-row.anchoredPosition.y - 4, 0, limit));
        var button = row.GetComponentsInChildren<Button>().FirstOrDefault(item => string.Equals(ButtonLabel(item), label, StringComparison.OrdinalIgnoreCase));
        Check(button != null, "games list row has " + label + ": " + game);
        if (button != null) await ClickButton(button, label + " · " + game);
    }

    /// <summary>Кадры, пока камера не встанет у стола игры: нырки, проезд по улице, подход.</summary>
    private static void DriveTableTrip(string game, string label)
    {
        var index = TableIndex(game);
        for (var frame = 0; frame < 1200 && !(bool)Call(hub, "AtTable", index); frame++)
        {
            Call(hub, "ContinueTableTrip");
            Call(hub, "MoveCity", 1 / 30f);
            Call(hub, "MoveDive", 1 / 30f);
            Call(hub, "MoveFloorPan", 1 / 30f);
            Call(hub, "MoveVisit", 1 / 30f);
        }
        Check((bool)Call(hub, "AtTable", index) && Get<string>(hub, "_page") == "game:" + game,
            "the camera travels to the table: " + label);
    }

    /// <summary>Стол на экране — по одну сторону от середины, табличка — по другую и его не закрывает.</summary>
    private static void CheckPlaqueBesideTable(string game)
    {
        Canvas.ForceUpdateCanvases();
        var place = ((Array)typeof(PlayerHubController).GetField("CityPlaces", BindingFlags.Static | BindingFlags.NonPublic)
            .GetValue(null)).GetValue(TableIndex(game));
        var rig = Get<RectTransform>(hub, "_cityRig");
        var area = Get<RectTransform>(hub, "_cityArea");
        var door = (Vector2)Call(hub, "PlaceScreenPoint", place, place.GetType().GetField("Door").GetValue(place)) * rig.localScale.x + rig.anchoredPosition;
        var doorWorld = area.TransformPoint(area.rect.center + door);
        var plaque = Get<RectTransform>(hub, "_content").Find("Table plaque") as RectTransform;
        var corners = new Vector3[4]; plaque.GetWorldCorners(corners);
        Check(Mathf.Abs(door.x) > area.rect.width * .1f && (doorWorld.x < corners[0].x || doorWorld.x > corners[2].x),
            "the table stands aside and the plaque does not cover it: " + game);
    }

    private static RectTransform PlacePage(string art)
        => Get<Dictionary<string, (RectTransform Page, float X, float Width)>>(hub, "_placePages")[art].Page;

    private static Vector2 ArtPictureSize(string art) => (Vector2)typeof(PlayerHubController)
        .GetMethod("PictureSize", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { art });

    private static void LookAtArtX(string art, float x)
    {
        var view = Get<Dictionary<string, (RectTransform Page, float X, float Width)>>(hub, "_placePages")[art];
        Call(hub, "LookAtPicture", (view.X + x / ArtPictureSize(art).x * view.Width) / Get<float[]>(hub, "_floorWidths")[2]);
    }

    private static Vector2 HoverClubPlace(object place, object[] places, Mouse mouse)
    {
        Call(hub, "LookAtPlace", place);
        var area = Get<RectTransform>(hub, "_cityArea");
        var point = (Vector2)Call(hub, "PlaceScreenPoint", place, Item(place, "Door"));
        Canvas.ForceUpdateCanvases(); camera.Render();
        var screen = RectTransformUtility.WorldToScreenPoint(camera, area.TransformPoint(area.rect.center + point));
        Hover(mouse, screen);
        var outline = Get<HubPlaceOutline>(hub, "_placeOutline");
        Check((int)Call(hub, "PlaceAt", point) == Array.IndexOf(places, place),
            "the correct object remains clickable after panning: " + (string)Item(place, "Id"));
        if ((bool)Item(place, "HallPassage"))
            Check(Get<int>(hub, "_placeHover") == -1 && (outline == null || !outline.gameObject.activeInHierarchy),
                "the clickable arch stays unoutlined after panning");
        else
            Check(Get<int>(hub, "_placeHover") == Array.IndexOf(places, place) && outline != null && outline.gameObject.activeInHierarchy,
                "the correct object outlines after panning: " + (string)Item(place, "Id"));
        return screen;
    }

    private static void CheckTableFurnitureOutline(object place)
    {
        var art = (string)Item(place, "Art");
        var page = PlacePage(art);
        var picture = ArtPictureSize(art);
        var table = page.GetComponentsInChildren<Image>().Single(image => image.name == "Table " + (string)Item(place, "Title")).rectTransform;
        var points = (Vector2[])Item(place, "Outline");
        Check(points.Length >= 3 && points.All(point =>
        {
            var local = new Vector3((point.x / picture.x - .5f) * page.rect.width, (.5f - point.y / picture.y) * page.rect.height, 0);
            var furniture = table.InverseTransformPoint(page.TransformPoint(local));
            return furniture.x >= table.rect.xMin - 1 && furniture.x <= table.rect.xMax + 1
                && furniture.y >= table.rect.yMin - 1 && furniture.y <= table.rect.yMax + 1;
        }), "the table outline stays inside its furniture image: " + (string)Item(place, "Id"));
        if ((bool)Item(place, "HoverOnly")) return;
        var name = ClubTableName(place);
        var center = page.InverseTransformPoint(name.TransformPoint(name.rect.center));
        var pixel = new Vector2((center.x / page.rect.width + .5f) * picture.x, (.5f - center.y / page.rect.height) * picture.y);
        var contains = typeof(PlayerHubController).GetMethod("Contains", BindingFlags.Static | BindingFlags.NonPublic);
        Check((bool)contains.Invoke(null, new object[] { Item(place, "Points"), pixel })
              && !(bool)contains.Invoke(null, new object[] { points, pixel }),
            "the game name remains in the click region but outside the furniture outline: " + (string)Item(place, "Id"));
    }

    private static RectTransform ClubTableName(object place) => PlacePage((string)Item(place, "Art")).GetComponentsInChildren<Text>()
        .Single(text => string.Equals(text.text, (string)Item(place, "Title"), StringComparison.OrdinalIgnoreCase)).rectTransform;

    private static Vector2 ClubTableNameScreen(object place)
    {
        var name = ClubTableName(place);
        return RectTransformUtility.WorldToScreenPoint(camera, name.TransformPoint(name.rect.center));
    }

    private static void CheckRenderedFurnitureOutline(object place)
    {
        var outline = Get<HubPlaceOutline>(hub, "_placeOutline");
        var expected = ((Vector2[])Item(place, "Outline")).Select(point => point / ArtPictureSize((string)Item(place, "Art"))).ToArray();
        Check(outline.Points.Length == expected.Length && outline.Points.Zip(expected, Vector2.Distance).All(distance => distance < .00001f),
            "the displayed contour uses the furniture boundary instead of the name's click area: " + (string)Item(place, "Id"));
    }

    private static void FinishClubPan(string label, bool alreadyMoving = false)
    {
        var gaps = 0;
        var positions = new List<float> { Get<float>(hub, "_floorPan") };
        var view = Get<RectTransform>(hub, "_cityArea").rect.width / (float)Call(hub, "FloorScale", 2);
        for (var frame = 0; frame < 360 && Get<bool>(hub, "_floorPanning"); frame++)
        {
            Call(hub, "MoveFloorPan", 1 / 60f);
            positions.Add(Get<float>(hub, "_floorPan"));
            if (!CityCovered()) gaps++;
        }
        Check(!Get<bool>(hub, "_floorPanning") && gaps == 0 && Get<int>(hub, "_cityLevel") == 2
              && Get<int>(hub, "_diveTarget") == -1 && Get<int>(hub, "_visitPhase") == 0,
            "pan settles within the Club without exposing a background gap: " + label);
        if (positions.Count < 4 || Mathf.Abs(positions.Last() - positions[0]) < 50) return;
        var direction = Mathf.Sign(positions.Last() - positions[0]);
        var speeds = positions.Skip(1).Zip(positions, (to, from) => (to - from) * direction * 60 / view).ToArray();
        // Зал едет, как улица (GlideCity): пик кривой — 1,5 × путь / (0,6 + 0,5 × путь) — всегда меньше 3 экранов в секунду.
        Check(speeds.All(speed => speed >= -.001f && speed <= 3f) && speeds.Last() < .05f,
            "pan travels without reversal, overspeed or an abrupt stop: " + label);
        if (!alreadyMoving)
            Check(speeds[0] < speeds.Max() * .45f, "pan eases into movement from rest: " + label);
    }

    private static void FinishClubVisit(bool entering, string label)
    {
        var gaps = 0;
        var scales = new List<float> { Get<RectTransform>(hub, "_cityRig").localScale.x };
        for (var frame = 0; frame < 200 && (entering ? Get<int>(hub, "_visitPhase") == 1 : Get<int>(hub, "_visitPhase") != 0); frame++)
        {
            Call(hub, "MoveVisit", 1 / 60f);
            scales.Add(Get<RectTransform>(hub, "_cityRig").localScale.x);
            if (!CityCovered()) gaps++;
        }
        // The harness disables Update; reproduce its final control-visibility step after navigation.
        Call(hub, "UpdateInteriorControls");
        Check(Get<int>(hub, "_visitPhase") == (entering ? 2 : 0) && gaps == 0,
            "the joined panorama covers every frame of the " + (entering ? "approach: " : "return: ") + label);
        var steps = scales.Skip(1).Zip(scales, (to, from) => (to - from) * (entering ? 1 : -1)).ToArray();
        Check(steps.Length >= 30 && steps.Length <= 57 && steps.All(step => step >= -.0001f)
              && steps[0] < steps.Max() * .25f && steps.Last() < steps.Max() * .25f,
            "the visit camera eases in and out without reversal or a sudden zoom step: " + label);
    }

    private static RectTransform FloorRect(int level) => (RectTransform)Call(hub, "Floor", level);

    /// <summary>Мышь в точку экрана и кадр подсветки (контур успевает загореться или погаснуть).</summary>
    private static void Hover(Mouse mouse, Vector2 screen)
    {
        InputSystem.QueueStateEvent(mouse, new MouseState { position = screen });
        InputSystem.Update();
        Call(hub, "UpdatePlaceHover", 1f);
    }

    /// <summary>Доехать до двери начатого въезда, вернуться на улицу и дождаться выезда.</summary>
    private static void LeavePlace()
    {
        for (var frame = 0; frame < 200 && Get<int>(hub, "_visitPhase") == 1; frame++) Call(hub, "MoveVisit", 1 / 60f);
        hub.GoHome();
        for (var frame = 0; frame < 200 && Get<int>(hub, "_visitPhase") != 0; frame++) Call(hub, "MoveVisit", 1 / 60f);
        Check(Get<int>(hub, "_visitPhase") == 0 && Get<string>(hub, "_page") == "home", "left the place and back on the street");
    }

    private static object Item(object tuple, string name) => tuple.GetType().GetField(name).GetValue(tuple);

    /// <summary>Сразу оказаться на этаже: 0 — улица, 1 — подполье, 2 — холм; без кадров нырка.</summary>
    private static void DiveNow(int level)
    {
        if (level != 0) SettleOn(1);
        Call(hub, "StartDive", level);
        Call(hub, "MoveDive", 10f);
    }

    /// <summary>Настоящие нажатия на виртуальной клавиатуре: D/→ и A/← плавно ведут по районам.</summary>
    private static void CheckCityKeys(float[] stops, float view)
    {
        var keyboard = InputSystem.AddDevice<Keyboard>("QA keyboard");
        try
        {
            // Как в игре: кадр хаба читает текущую клавиатуру; в поле ввода клавиши печатают, а не листают.
            keyboard.MakeCurrent();
            SettleOn(1);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D)); InputSystem.Update();
            Call(hub, "UpdateCity", true);
            Check(Get<int>(hub, "_district") == 1, "typing in a text field does not scroll the city");
            Call(hub, "UpdateCity", false);
            Check(Get<int>(hub, "_district") == 2, "the hub's frame update hears D on the current keyboard");
            Release(keyboard);
            SettleOn(1);
            Press(keyboard, Key.D);
            Check(Get<int>(hub, "_district") == 2 && Get<bool>(hub, "_gliding"), "D starts a ride from the square to Tuzyara's district");
            CheckRide(Ride(), stops[2], "D to Tuzyara");
            Press(keyboard, Key.A);
            Check(Get<int>(hub, "_district") == 1, "A rides back to the square");
            CheckRide(Ride(), stops[1], "A to the square");
            Press(keyboard, Key.LeftArrow);
            Check(Get<int>(hub, "_district") == 0, "the left arrow rides to the Old Town");
            CheckRide(Ride(), stops[0], "left arrow to the Old Town");
            Press(keyboard, Key.RightArrow);
            Check(Get<int>(hub, "_district") == 1, "the right arrow rides to the square");
            // Второе нажатие в пути — ещё на район дальше, без остановки.
            for (var frame = 0; frame < 30; frame++) Call(hub, "MoveCity", 1 / 60f);
            Press(keyboard, Key.RightArrow);
            Check(Get<int>(hub, "_district") == 2 && Mathf.Abs(Get<float>(hub, "_glideTo") - stops[2]) < 1,
                "a second press on the way rides one district further");
            CheckRide(Ride(), stops[2], "second press to Tuzyara", fromMotion: true);
            // У края города — лёгкий толчок и назад.
            var end = stops[stops.Length - 1];
            SettleOn(stops.Length - 1);
            Press(keyboard, Key.D);
            var nudge = Ride();
            Check(nudge.Max() > end + .02f * view && nudge.Max() < end + .1f * view
                  && Mathf.Abs(nudge.Last() - end) < .5f, "at the city end D nudges toward the sea and comes back");
            // Зажатая клавиша: из Старого города через площадь и Тузяру в порт, не вставая по дороге.
            SettleOn(0);
            Press(keyboard, Key.D, hold: true);
            var run = Ride(keyboard);
            Release(keyboard);
            var speeds = run.Zip(run.Skip(1), (a, b) => (b - a) * 60 / view).ToArray();
            var atSquare = run.FindIndex(x => x >= stops[1]);
            var atTuzyara = run.FindIndex(x => x >= stops[2]);
            Check(Mathf.Abs(run.Last() - end) < .5f && atSquare > 0 && atTuzyara > 0
                  && speeds[Mathf.Min(atSquare, speeds.Length - 1)] > .5f && speeds[Mathf.Min(atTuzyara, speeds.Length - 1)] > .5f,
                "holding D rides through the square and Tuzyara's district to the port without stopping");
            Check(speeds.Zip(speeds.Skip(1), (a, b) => Mathf.Abs(b - a)).Max() < .12f, "the held ride has no jolts");
        }
        finally
        {
            InputSystem.RemoveDevice(keyboard);
        }
    }

    /// <summary>Отпускание мыши потерялось: город не должен остаться «в руке» и глухим к клавишам.</summary>
    private static void CheckLostRelease(RectTransform area)
    {
        var mouse = InputSystem.AddDevice<Mouse>("QA mouse");
        try
        {
            mouse.MakeCurrent();
            SettleOn(1);
            var swipe = area.GetComponentInChildren<HubCitySwipe>();
            var from = RectTransformUtility.WorldToScreenPoint(camera, area.TransformPoint(area.rect.center));
            var data = new PointerEventData(events) { position = from,
                pointerPressRaycast = new RaycastResult { module = canvasObject.GetComponent<GraphicRaycaster>() } };
            swipe.OnBeginDrag(data);
            data.position = from + new Vector2(-40, 0); swipe.OnDrag(data);
            Check(Get<bool>(hub, "_cityDragging"), "the city is held while dragging");
            Call(hub, "UpdateCity", false);
            Check(!Get<bool>(hub, "_cityDragging"), "a lost release frees the city once no button is held");
            SettleOn(1);
        }
        finally
        {
            InputSystem.RemoveDevice(mouse);
        }
    }

    private static void Press(Keyboard keyboard, Key key, bool hold = false)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
        InputSystem.Update();
        Call(hub, "CityKeys", keyboard);
        if (!hold) Release(keyboard);
    }

    private static void Release(Keyboard keyboard)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        InputSystem.Update();
        Call(hub, "CityKeys", keyboard);
    }

    /// <summary>Поставить город на район сразу, без проезда.</summary>
    private static void SettleOn(int district)
    {
        Call(hub, "ShowDistrict", district); Call(hub, "SettleCity");
    }

    /// <summary>Прокрутить проезд кадрами по 1/60 с, пока город не встанет; вернуть положения по кадрам.</summary>
    private static List<float> Ride(Keyboard held = null)
    {
        var path = new List<float> { Get<float>(hub, "_cityCamera") };
        for (var frame = 0; frame < 600 && Get<bool>(hub, "_gliding"); frame++)
        {
            if (held != null) { InputSystem.Update(); Call(hub, "CityKeys", held); }
            Call(hub, "MoveCity", 1 / 60f);
            path.Add(Get<float>(hub, "_cityCamera"));
        }
        return path;
    }

    /// <summary>
    /// Проезд плавный: приходит точно в цель, не проскакивает и не ездит туда-обратно, скорость не
    /// выше двух экранов в секунду и без рывков; с места трогается мягко (если не был уже в движении).
    /// </summary>
    private static void CheckRide(List<float> path, float target, string label, bool fromMotion = false)
    {
        var view = (float)Call(hub, "CityView");
        var speeds = path.Zip(path.Skip(1), (a, b) => (b - a) * 60 / view).ToArray();
        var direction = Mathf.Sign(target - path[0]);
        var seconds = speeds.Length / 60f;
        Check(Mathf.Abs(path.Last() - target) < .5f && !Get<bool>(hub, "_gliding"), "ride arrives exactly: " + label);
        Check(speeds.All(v => v * direction >= -.0001f), "ride never backs up or overshoots: " + label);
        Check(speeds.Max(Mathf.Abs) < 2f && seconds < .7f + .5f * Mathf.Abs(target - path[0]) / view,
            "ride is calm, under two screens a second: " + label
            + " (top " + speeds.Max(Mathf.Abs).ToString("0.00") + ", " + seconds.ToString("0.00") + " s)");
        Check(speeds.Zip(speeds.Skip(1), (a, b) => Mathf.Abs(b - a)).DefaultIfEmpty(0).Max() < .12f, "ride has no jolts: " + label);
        Check(Mathf.Abs(speeds.Last()) < .1f && (fromMotion || Mathf.Abs(speeds[0]) < .1f), "ride starts and stops softly: " + label);
    }

    /// <summary>Город закрывает экран целиком: куски идут без щелей и по высоте не короче экрана.</summary>
    private static void CheckCityCovers(string label)
    {
        Check(CityCovered(), "city covers the whole screen, no purple backdrop: " + label);
    }

    private static bool CityCovered()
    {
        Canvas.ForceUpdateCanvases();
        var spans = hub.GetComponentsInChildren<HubCityArt>().Select(art =>
        {
            var corners = new Vector3[4]; art.rectTransform.GetWorldCorners(corners);
            var low = camera.WorldToViewportPoint(corners[0]); var high = camera.WorldToViewportPoint(corners[2]);
            return (Left: low.x, Right: high.x, Bottom: low.y, Top: high.y);
        }).OrderBy(span => span.Left).ToArray();
        var reach = spans.Length == 0 ? -1f : spans[0].Left;
        var covered = reach <= .001f;
        foreach (var span in spans)
        {
            if (span.Left > reach + .001f && reach < .999f) covered = false;
            reach = Mathf.Max(reach, span.Right);
            if (span.Bottom > .001f || span.Top < .999f) covered = false;
        }
        return covered && reach >= .999f;
    }

    /// <summary>
    /// На остановках и на пределе резинки у обоих краёв город закрывает экран этого размера, картинки
    /// по высоте видны целиком (не срезаны сверху и снизу), а на широком экране за нарисованным концом
    /// города не выглядывает зеркальная окраина.
    /// </summary>
    private static void CheckCityExtremes(string size)
    {
        var stops = Get<float[]>(hub, "_cityStops");
        var reach = Get<float>(hub, "_cityWidth");
        foreach (var (spot, name) in new[] { (stops[0], "first district"), (stops[stops.Length - 1], "last district"),
                     (stops[0] - Get<float>(hub, "_cityGlimpseLeft") * reach, "left edge"),
                     (stops[stops.Length - 1] + Get<float>(hub, "_cityGlimpseRight") * reach, "right edge") })
        {
            Set(hub, "_cityCamera", spot); Call(hub, "PlaceCity");
            CheckCityCovers(name + " " + size);
            Canvas.ForceUpdateCanvases();
            var arts = hub.GetComponentsInChildren<HubCityArt>();
            Check(arts.All(art =>
                {
                    var corners = new Vector3[4]; art.rectTransform.GetWorldCorners(corners);
                    return camera.WorldToViewportPoint(corners[0]).y > -.001f && camera.WorldToViewportPoint(corners[2]).y < 1.001f;
                }), "the city shows its full height, nothing cut at the top or bottom: " + name + " " + size);
            Check(arts.Where(art => art.name == "Outskirts").All(art =>
                {
                    var corners = new Vector3[4]; art.rectTransform.GetWorldCorners(corners);
                    return camera.WorldToViewportPoint(corners[2]).x < .001f || camera.WorldToViewportPoint(corners[0]).x > .999f;
                }), "no mirrored outskirts peek past the painted city ends: " + name + " " + size);
        }
        Save("hub-home-edge-right-" + size + ".png");
        // Подполье — отдельный экран: тоже закрывает экран этого размера.
        DiveNow(1); CheckCityCovers("underground " + size); Save("hub-underground-" + size + ".png"); DiveNow(0);
        DiveNow(2); CheckCityCovers("club hall " + size); Save("hub-club-hall-" + size + ".png"); DiveNow(0);
        SettleOn(Get<int>(hub, "_district"));
    }

    /// <summary>
    /// Провести пальцем от середины экрана на долю его ширины; вернуть положение города до отпускания.
    /// <paramref name="speed"/> — с какой скоростью (экранов в секунду) палец ещё ехал в момент отпускания.
    /// </summary>
    private static float Swipe(RectTransform area, float share, float speed = 0)
    {
        var swipe = area.GetComponentInChildren<HubCitySwipe>();
        var from = RectTransformUtility.WorldToScreenPoint(camera, area.TransformPoint(area.rect.center));
        var to = RectTransformUtility.WorldToScreenPoint(camera,
            area.TransformPoint(area.rect.center + new Vector2(area.rect.width * share, 0)));
        var data = new PointerEventData(events) { position = from,
            pointerPressRaycast = new RaycastResult { module = canvasObject.GetComponent<GraphicRaycaster>() } };
        swipe.OnBeginDrag(data);
        data.position = to; swipe.OnDrag(data);
        var moved = Get<float>(hub, "_cityCamera");
        if (Get<bool>(hub, "_cityDragging"))
        {
            // Палец влево — город едет вправо: скорость «камеры» обратна движению пальца.
            Set(hub, "_dragSpeed", -Mathf.Sign(share) * speed * (float)Call(hub, "CityView"));
            Set(hub, "_dragLastTime", Time.unscaledTime);
        }
        swipe.OnEndDrag(data);
        return moved;
    }

    private static async Task CheckSectionNavigation()
    {
        hub.GoHome(); await NextFrame();
        // На главной сейчас только город; вкладки, профиль, настройки и монеты — на страницах разделов.
        Check(!ActiveButtons().Any(), "lobby has no controls while the author works on the background");
        hub.Navigate("friends"); await NextFrame();
        var entries = new[] { "Магазин", "Коллекция", "Главная", "Друзья", "Прогресс", "Профиль", "Настройки", "Пополнить" };
        Check(entries.All(entry => ActiveButtons().Any(button => ButtonLabel(button) == entry)),
            "section pages reach the five tabs, profile, settings and bank");
        hub.Navigate("games"); await NextFrame();
        Check(Get<string>(hub, "_page") == "games" && playCalls == 0, "game selection is its own page");
        // Клуб — внутри города: на его экранах нет вкладок и верхней панели, «Назад» — доской вывески.
        Check(!ActiveButtons().Any(button => entries.Contains(ButtonLabel(button))) && ActiveButtons().Count(button => ButtonLabel(button) == "НАЗАД") == 1,
            "club pages hide the section tabs and keep one sign-style back");
        var gameIds = new[] { "durak", "bluff", "chests", "govno", "poker" };
        for (var i = 0; i < gameIds.Length; i++)
        {
            if (gameIds[i] == "poker") { hub.Navigate("game:poker"); await NextFrame(); }
            else await ClickGameRow(gameIds[i], "Выбрать игру");
            Check(Get<string>(hub, "_page") == "game:" + gameIds[i], "game tile opens the plaque at its table: " + gameIds[i]);
            await Click("Начать с ботами");
            Check(quickGame == gameIds[i] && quickCalls == i + 1 && Get<string>(hub, "_page") == "game:" + gameIds[i],
                "sitting down starts a quick game of the selected game at once: " + gameIds[i]);
            await Click("ОТОЙТИ");
            Check(Get<string>(hub, "_page") == "games", "stepping away from a table returns to game selection");
        }
        hub.Navigate("game:poker"); await NextFrame();
        await Click("Быстрая игра"); await Click("Список комнат");
        Check(Get<string>(hub, "_page") == "rooms" && Get<string>(hub, "_selectedGame") == "poker",
            "game-specific room browser keeps the selected game filter");
        var roomLabels = Get<RectTransform>(hub, "_content").GetComponentsInChildren<Text>().Select(label => label.text).ToArray();
        var unrelatedRooms = PlayerHubRuntime.Current.Rooms.Where(room => room.GameId != "poker").Select(room => room.Name).ToArray();
        Check(!unrelatedRooms.Any(name => roomLabels.Any(text => text.Contains(name))),
            "poker room browser does not show rooms belonging to other games");
        await Click("Создать комнату");
        await Click("К списку");
        Check(Get<string>(hub, "_page") == "rooms", "creation's explicit list action returns to the room browser");
        await Click("Назад");
        Check(Get<string>(hub, "_page") == "game:poker", "room-list back after creation does not reopen the form");
        await Click("Быстрая игра"); await Click("Список комнат"); await Click("Создать комнату");
        Check(Get<string>(hub, "_page") == "create-room" && Get<string>(hub, "_roomGame") == "poker",
            "room creation inherits the chosen game");
        await Click("К списку");
        Check(Get<string>(hub, "_page") == "rooms", "direct creation can return to the selected game's room browser");
        await Click("Назад");
        Check(Get<string>(hub, "_page") == "game:poker", "direct-creation list back returns to the game without a stale form");
        hub.GoHome(); await NextFrame();
        Check(Get<string>(hub, "_page") == "home" && Get<List<string>>(hub, "_history").Count == 0,
            "home action clears the section's navigation history");
        hub.Navigate("friends"); await NextFrame(); await Click("Магазин");
        Check(Get<string>(hub, "_page") == "shop", "shop opens a separate category landing");
        await Click("Персонаж и костюмы");
        Check(Get<string>(hub, "_page") == "shop-catalog", "shop category opens its own catalog page");
        await Click("Назад");
        Check(Get<string>(hub, "_page") == "shop", "catalog back returns to shop categories");
        await Click("Назад");
        Check(Get<string>(hub, "_page") == "home", "shop back returns to home");
        hub.Navigate("friends"); await NextFrame(); await Click("Прогресс");
        Check(Get<string>(hub, "_page") == "progress", "progress systems have their own landing");
        await Click("Пополнить");
        Check(Get<string>(hub, "_page") == "bank", "bank is a separate destination");
        var persistentButtons = ActiveButtons().Where(button => !button.transform.IsChildOf(Get<RectTransform>(hub, "_content")))
            .Select(ButtonLabel).ToArray();
        var shell = new[] { "Назад", "Профиль", "Настройки", "Пополнить", "Магазин", "Коллекция", "Главная", "Друзья", "Прогресс" };
        Check(persistentButtons.All(label => shell.Contains(label)),
            "section pages keep only the compact shell: back, profile, settings, wallet and five tabs");
        InspectLayout("navigation-bank"); Save("hub-bank-navigation.png");
        await Click("Главная");
    }

    private static async Task CheckGameSetupDrafts()
    {
        var gameIds = new[] { "durak", "bluff", "chests", "govno", "poker" };
        foreach (var game in gameIds)
        {
            hub.Navigate("game:" + game); await NextFrame();
            await Click("С ботами");
            var labels = Get<RectTransform>(hub, "_content").GetComponentsInChildren<Text>().Select(text => text.text).ToArray();
            Check(labels.Contains("Игроков за столом"), "shared setup exposes player count: " + game);
            Check(labels.Contains("Колода") == (game == "durak" || game == "bluff" || game == "chests"),
                "only configurable card games show deck choices: " + game);
            Check(labels.Contains("Перестрелка") == (game == "bluff")
                && labels.Contains("Перевод") == (game == "durak")
                && labels.Contains("Карты в руке") == (game == "govno"), "setup exposes only the selected game's rules: " + game);
            await ChooseNonDefaultRules(game);
            if (game == "durak")
            {
                await ClickChoice("6");
                Check(hub.GetGameSetupPlayerCount() == 6 && hub.GetGameSetupSettings().DeckSize == 36,
                    "raising player count chooses a deck with enough cards");
                await ClickChoice("24");
                Check(hub.GetGameSetupPlayerCount() == 4 && hub.GetGameSetupSettings().DeckSize == 24,
                    "choosing 24 cards reduces the table to four seats");
            }
            InspectLayout("setup-quick-" + game); Save("hub-setup-" + game + ".png");
            var quickRules = hub.GetGameSetupSettings();
            var copy = hub.GetGameSetupSettings(); copy.DeckSize = 999; copy.Chaos = !copy.Chaos;
            Check(hub.GetGameSetupSettings().DeckSize != 999 && hub.GetGameSetupSettings().Chaos == quickRules.Chaos,
                "setup returns an isolated settings copy: " + game);
            await Click("С ботами"); await Click("Быстрая игра"); await Click("Список комнат"); await Click("Создать комнату");
            Check(SameRules(quickRules, hub.GetGameSetupSettings()) && hub.GetGameSetupPlayerCount() == 4,
                "the table and room creation share the selected game's draft: " + game);
            InspectLayout("setup-create-" + game); Save("hub-create-" + game + ".png");
            await Click("Назад"); await Click("Назад");
            Check(Get<string>(hub, "_page") == "game:" + game, "room creation steps back to the table plaque: " + game);
        }

        hub.Navigate("game:bluff"); await NextFrame(); await Click("Быстрая игра"); await Click("Список комнат"); await Click("Создать комнату");
        Set(hub, "_roomStake", "0");
        await Click("Создать");
        Check(CurrentRoom() != null && CurrentRoom().GameSettings.DeckSize == 52 && !CurrentRoom().GameSettings.Shootout,
            "created bluff room stores the shared 52-card and disabled-shootout rules");
        var saved = await store.LoadAsync();
        var savedRoom = saved.Rooms.Single(room => room.Id == CurrentRoom().Id);
        Check(savedRoom.GameSettings.DeckSize == 52 && !savedRoom.GameSettings.Shootout,
            "room rules survive the actual persistence boundary");
        savedRoom.GameSettings.DeckSize = 24;
        Check(CurrentRoom().GameSettings.DeckSize == 52, "loaded snapshot rule edits cannot mutate the live room");
        await Click("Я готов");
        await Click("Правила комнаты");
        Check(Get<string>(hub, "_page") == "room-settings", "host rules open in the same hub interface");
        Check(hub.GetGameSetupSettings().DeckSize == 52 && !hub.GetGameSetupSettings().Shootout,
            "editing starts with the saved room rules");
        await ClickChoice("Включена");
        Check(!CurrentRoom().GameSettings.Shootout, "unsaved host changes remain a draft");
        InspectLayout("room-settings"); Save("hub-room-settings.png");
        foreach (var size in new[] { new Vector2Int(2040, 900), new Vector2Int(1280, 720) })
        {
            ResizeCanvas(size.x, size.y); ((RectTransform)hub.transform).sizeDelta = new Vector2(size.x, size.y);
            Call(hub, "Resize"); await NextFrame(); InspectLayout("room-settings-" + size.x);
        }
        ResizeCanvas(1600, 900); ((RectTransform)hub.transform).sizeDelta = new Vector2(1600, 900);
        Call(hub, "Resize"); await NextFrame();
        await Click("Сохранить настройки");
        Check(Get<string>(hub, "_page") == "room" && CurrentRoom().GameSettings.Shootout,
            "saving host rules returns to the room and updates the stored rules");
        Check(CurrentRoom().Members.Where(member => !member.IsBot).All(member => !member.Ready),
            "changed room rules require human readiness again");
        await Click("Правила комнаты"); await ClickChoice("Без"); await Click("Отмена");
        Check(CurrentRoom().GameSettings.Shootout, "cancelling host settings leaves saved rules unchanged");
        await Dispatch("leave_room"); hub.GoHome(); await NextFrame();
    }

    private static async Task ChooseNonDefaultRules(string game)
    {
        await ClickChoice("4");
        if (game == "durak")
        {
            await ClickChoice("24"); await ClickChoice("Переводной");
            await ClickChoice("Соседи"); await ClickChoice("Включено");
        }
        else if (game == "bluff") { await ClickChoice("52"); await ClickChoice("Без"); }
        else if (game == "chests") await ClickChoice("52");
        else if (game == "govno") await ClickChoice("Хаос");
    }

    private static bool SameRules(HubGameSettings a, HubGameSettings b) => a.DeckSize == b.DeckSize
        && a.AllowTransfer == b.AllowTransfer && a.NeighboursOnly == b.NeighboursOnly
        && a.AllowCheating == b.AllowCheating && a.Shootout == b.Shootout && a.Chaos == b.Chaos;

    private static async Task ClickChoice(string label)
    {
        var choices = ActiveButtons().Where(entry => ButtonLabel(entry).Replace("✓ ", "") == label).ToArray();
        Check(choices.Length == 1, "unique enabled setup choice: " + label + " (found " + choices.Length + ")");
        await ClickButton(choices[0], label);
    }

    private static void CheckCanvasDensity()
    {
        var root=new GameObject("Screen-space density QA",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
        var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;
        var scaler=root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution=new Vector2(1920,1080);
        scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight=.5f;
        var child=new GameObject("Density menu",typeof(RectTransform));child.transform.SetParent(root.transform,false);
        var rect=(RectTransform)child.transform;
        rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
        var menu=child.AddComponent<PlayerHubController>();
        Call(menu,"UseMenuCanvasScaling");Call(scaler,"Handle");Canvas.ForceUpdateCanvases();
        Call(menu,"BuildShell");Call(menu,"Resize");
        Check(Mathf.Abs(Get<RectTransform>(menu,"_stage").localScale.x-1f)<.002f,
            "screen-space menu has no second transform enlargement of rasterized glyphs");
        Check(canvas.pixelPerfect,"screen-space UI uses pixel alignment");
        Call(menu,"OnDisable");Call(scaler,"Handle");
        Check(scaler.referenceResolution==new Vector2(1920,1080)
            && scaler.screenMatchMode==CanvasScaler.ScreenMatchMode.MatchWidthOrHeight
            && Mathf.Approximately(scaler.matchWidthOrHeight,.5f) && !canvas.pixelPerfect,
            "leaving menu restores original table canvas settings");
        Call(menu,"OnEnable");Call(scaler,"Handle");
        Check(scaler.referenceResolution==new Vector2(1600,900),"returning to menu reapplies native text density");
        UnityEngine.Object.DestroyImmediate(root);
    }

    private static string LongMessage()
    {
        var text = string.Concat(Enumerable.Repeat("Проверка длинного сообщения в чате. ", 20));
        return text.Substring(0, 399) + "!";
    }

    private static async Task CheckLongFriendChat()
    {
        hub.GoHome(); await NextFrame(); hub.Navigate("chats"); await NextFrame();
        Check(Get<string>(hub, "_page") == "chats", "chats have a dedicated conversation-list page");
        var conversation = (RectTransform)Get<RectTransform>(hub, "_content").Find("Conversation bot-yegor");
        Check(conversation != null, "existing friend has a visible conversation entry");
        var open = ActiveButtons().Single(button => ButtonLabel(button) == "Открыть чат" && conversation.rect.Contains(
            conversation.InverseTransformPoint(button.transform.TransformPoint(((RectTransform)button.transform).rect.center))));
        await ClickButton(open, "open Yegor conversation");
        Check(Get<string>(hub, "_page") == "chat:bot-yegor", "conversation entry opens the corresponding friend's messages");
        var input = Get<RectTransform>(hub, "_content").GetComponentsInChildren<InputField>().Single();
        var text = LongMessage(); input.text = text;
        await Click("Отправить");
        var message = PlayerHubRuntime.Current.Chats.Last(entry => entry.ConversationId == "bot-yegor"
            && entry.FromId == PlayerHubRuntime.Current.Profile.Id);
        Check(message.Text == text, "friend chat preserves all 400 characters");
        InspectLayout("friend-chat-long-preview"); Save("hub-friend-chat.png");
        hub.Navigate("message:" + message.Id); await NextFrame();
        InspectLayout("friend-full-message");
        var scroll = Get<RectTransform>(hub, "_content").GetComponentInChildren<ScrollRect>();
        Check(scroll != null && scroll.viewport.GetComponent<RectMask2D>() != null,
            "full friend message is displayed inside a masked scroll viewport");
        Check(scroll.content.GetComponentInChildren<Text>().text == text, "full-message page does not truncate the saved text");
        Save("hub-friend-message.png");
        await Click("Назад в переписку");
        Check(Get<string>(hub, "_page") == "chat:bot-yegor", "full-message back button returns to the correct conversation");
        hub.Navigate("message:" + message.Id); await NextFrame();
        await Click("Назад");
        Check(Get<string>(hub, "_page") == "chat:bot-yegor", "global back from a message returns to its conversation");
        await Click("Назад");
        Check(Get<string>(hub, "_page") == "chats", "message drill-down does not duplicate the conversation in history");
        await Click("Назад");
        Check(Get<string>(hub, "_page") == "home", "chat list returns to the main menu");
    }

    private static void CheckRoomRoster()
    {
        var source = PlayerHubRuntime.Current;
        var room = CurrentRoom();
        var ids = new[] { source.Profile.Id }.Concat(room.Members.Where(member => member.PlayerId != source.Profile.Id)
            .Select(member => member.PlayerId)).ToArray();
        var expectedNames = ids.Select(id => id == source.Profile.Id ? source.Profile.DisplayName : source.People.Single(person => person.Id == id).Name).ToArray();
        var expectedPortraits = ids.Select(id => AvatarCatalog.Find(id == source.Profile.Id ? source.Profile.AvatarName
            : source.People.Single(person => person.Id == id).AvatarId)).ToArray();
        var names = Enumerable.Repeat("untouched", ids.Length).ToArray();
        var portraits = new Sprite[ids.Length];
        Check(HubRoster.TryApply(room.GameId, names, portraits) && names.SequenceEqual(expectedNames)
            && portraits.SequenceEqual(expectedPortraits) && portraits.Skip(1).All(sprite => sprite != null),
            "room launch captures actual local-first names and avatar portraits");

        var copied = JsonUtility.FromJson<HubSnapshot>(JsonUtility.ToJson(source));
        var copiedRoom = copied.Rooms.Single(entry => entry.Id == room.Id);
        var local = copiedRoom.Members.Single(member => member.PlayerId == copied.Profile.Id);
        copiedRoom.Members.Remove(local); copiedRoom.Members.Add(local);
        HubRoster.Configure(copied, room.Id);
        copied.Profile.DisplayName = "changed after capture";
        foreach (var person in copied.People) { person.Name = "changed after capture"; person.AvatarId = "missing-after-capture"; }
        Check(HubRoster.TryApply(room.GameId, names, portraits) && names.SequenceEqual(expectedNames) && portraits.SequenceEqual(expectedPortraits),
            "roster remains local-first and is isolated from later source mutations");
        var untouched = Enumerable.Repeat("untouched", ids.Length).ToArray();
        var empty = new Sprite[ids.Length];
        Check(!HubRoster.TryApply("durak", untouched, empty) && untouched.All(name => name == "untouched") && empty.All(sprite => sprite == null),
            "a different game cannot consume this room roster");
        Check(!HubRoster.TryApply(room.GameId, new string[ids.Length - 1], new Sprite[ids.Length - 1]), "a different participant count cannot consume the roster");
        HubRoster.Clear();
        Check(!HubRoster.TryApply(room.GameId, names, portraits), "cleared room identities cannot leak into another game");
        HubRoster.Configure(source, room.Id);
    }

    private static async Task CheckAppFlow()
    {
        // Discard only the QA canvas, then provide a fresh in-memory account to actual AppFlow.
        store = new MemoryStore(); clock = new FixedClock();
        PlayerHubRuntime.UseService(new LocalPlayerHubService(store, clock));
        Check((await PlayerHubRuntime.LoadAsync()).Success, "fresh AppFlow memory profile loaded");
        Begin(1600, 900);
        var flow = canvasObject.AddComponent<AppFlowController>(); flow.enabled = false;
        Call(flow, "Start");
        hub = Get<PlayerHubController>(flow, "_playerHub"); hub.enabled = false;
        Call(hub, "Start");
        await WaitFor(() => Get<HubSnapshot>(hub, "_state") != null && !Get<bool>(hub, "_busy"), "AppFlow hub initialization");
        var games = new[] { "durak", "bluff", "chests", "govno", "poker" };
        var tables = new[] { typeof(DurakTableController), typeof(BluffTableController), typeof(ChestsTableController),
            typeof(GovnoTableController), typeof(PokerTableController) };
        for (var i = 0; i < games.Length; i++)
        {
            hub.Navigate("game:" + games[i]); await NextFrame();
            Check(Get<string>(hub, "_page") == "game:" + games[i] && Get<GameObject>(flow, "_mainMenuScreen").activeSelf,
                "the table plaque opens in AppFlow: " + games[i]);
            await Click("С ботами"); await ChooseNonDefaultRules(games[i]);
            var previous = Get<GameObject>(flow, "_gameScreen");
            await Click("Начать с ботами");
            await WaitFor(() => Get<GameObject>(flow, "_gameScreen") != null
                && Get<GameObject>(flow, "_gameScreen") != previous, "AppFlow launches " + games[i]);
            var gameScreen = Get<GameObject>(flow, "_gameScreen");
            var table = (MonoBehaviour)gameScreen.GetComponent(tables[i]);
            Check(table != null && gameScreen.activeInHierarchy, "correct active table component for " + games[i]);
            // Layout/gameplay has its own suites. Do not build five full tables or run their loops here.
            table.enabled = false;
            CheckTableRules(table, games[i], "quick play");
            var token = PlayerHubRuntime.Current.ActiveMatchId;
            Check(!string.IsNullOrEmpty(token) && PlayerHubRuntime.Current.Matches.Any(entry => entry.Id == token
                && entry.Game == games[i] && !entry.Completed), "launch registers one live token for " + games[i]);
            Check(!Get<GameObject>(flow, "_mainMenuScreen").activeSelf, "launch hides hub for " + games[i]);
            Get<Action>(table, "_onExit")();
            await WaitFor(() => Get<GameObject>(flow, "_mainMenuScreen").activeSelf
                && string.IsNullOrEmpty(PlayerHubRuntime.Current.ActiveMatchId), "AppFlow exits " + games[i]);
            Check(!gameScreen.activeSelf && PlayerHubRuntime.Current.Matches.Single(entry => entry.Id == token).Abandoned,
                "exit hides table and forfeits its live token for " + games[i]);
            Check(Get<string>(hub, "_page") == "game:" + games[i] && ActiveButtons().Any(button => ButtonLabel(button) == "Начать с ботами"),
                "after the match the player is back at the same table and can sit down again: " + games[i]);
        }
        Check(PlayerHubRuntime.Current.Profile.Matches == 0, "exiting unfinished tables grants no match progress");

        for (var i = 0; i < games.Length; i++)
        {
            hub.Navigate("game:" + games[i]); await NextFrame(); await Click("Быстрая игра"); await Click("Список комнат"); await Click("Создать комнату");
            await ChooseNonDefaultRules(games[i]); Set(hub, "_roomStake", "0");
            var expected = hub.GetGameSetupSettings();
            await Click("Создать");
            Check(CurrentRoom() != null && SameRules(CurrentRoom().GameSettings, expected),
                "AppFlow room captures the chosen rules: " + games[i]);
            // A later draft must not replace the rules agreed in the room.
            Get<Dictionary<string, HubGameSettings>>(hub, "_gameSetupDrafts")[games[i]] = HubGameSettings.DefaultFor(games[i]);
            while (CurrentRoom().Members.Count < 4) await Click("Добавить бота");
            await Click("Я готов");
            var previous = Get<GameObject>(flow, "_gameScreen");
            await Click("Начать игру");
            await WaitFor(() => Get<GameObject>(flow, "_gameScreen") != null
                && Get<GameObject>(flow, "_gameScreen") != previous, "AppFlow room launches " + games[i]);
            var gameScreen = Get<GameObject>(flow, "_gameScreen");
            var table = (MonoBehaviour)gameScreen.GetComponent(tables[i]);
            Check(table != null && gameScreen.activeInHierarchy, "room builds the correct table: " + games[i]);
            table.enabled = false; CheckTableRules(table, games[i], "room launch");
            var token = PlayerHubRuntime.Current.ActiveMatchId;
            Get<Action>(table, "_onExit")();
            await WaitFor(() => Get<GameObject>(flow, "_mainMenuScreen").activeSelf
                && string.IsNullOrEmpty(PlayerHubRuntime.Current.ActiveMatchId), "room exits " + games[i]);
            Check(PlayerHubRuntime.Current.Matches.Single(entry => entry.Id == token).Abandoned
                && Get<string>(hub, "_page") == "room", "unfinished room game returns to its room: " + games[i]);
            await Dispatch("leave_room");
        }

        // The real application must redirect a blocked quick-game launch to the bank.
        var blocked = await store.LoadAsync();
        blocked.Bank.Debt = 110; blocked.Bank.Principal = 100; blocked.Bank.Status = "collection";
        blocked.Bank.DueAt = clock.Now - 86400; blocked.Bank.CollectorAt = clock.Now - 1;
        blocked.Bank.CollectorPending = true; blocked.Bank.CollectorGame = "durak";
        await store.SaveAsync(blocked);
        PlayerHubRuntime.UseService(new LocalPlayerHubService(store, clock));
        Check((await PlayerHubRuntime.LoadAsync()).Success && PlayerHubRuntime.Current.Bank.CollectorPending,
            "collector fixture persists entirely in memory");
        var oldTable = Get<GameObject>(flow, "_gameScreen");
        var entries = PlayerHubRuntime.Current.Matches.Count;
        Call(flow, "StartPokerMatch");
        await WaitFor(() => !Get<bool>(flow, "_startingHubGame"), "collector launch decision");
        Check(Get<GameObject>(flow, "_gameScreen") == oldTable && !oldTable.activeSelf
            && PlayerHubRuntime.Current.Matches.Count == entries, "collector block does not construct or register a quick table");
        Check(Get<GameObject>(flow, "_mainMenuScreen").activeSelf && Get<string>(hub, "_page") == "bank",
            "collector block redirects to bank");
        InspectLayout("appflow-collector-bank"); Save("hub-collector-block.png");
        await Click("Сыграть с коллекторами");
        var collectorScreen = Get<GameObject>(flow, "_gameScreen");
        var collectorTable = collectorScreen.GetComponent<DurakTableController>();
        Check(collectorScreen != oldTable && collectorScreen.activeSelf && collectorTable != null,
            "bank launches the configured collector game through actual AppFlow");
        collectorTable.enabled = false;
        var collectorToken = PlayerHubRuntime.Current.ActiveMatchId;
        Check(!string.IsNullOrEmpty(collectorToken) && PlayerHubRuntime.Current.Matches.Single(entry => entry.Id == collectorToken).IsCollector,
            "collector launch registers its bank-owned match token");
        HubMatchReporter.ReportMatchCompleted("durak", true);
        await WaitFor(() => string.IsNullOrEmpty(PlayerHubRuntime.Current.ActiveMatchId), "collector result settles");
        Check(PlayerHubRuntime.Current.Matches.Single(entry => entry.Id == collectorToken).Completed,
            "collector result is saved before leaving the table");
        Get<Action>(collectorTable, "_onExit")();
        await WaitFor(() => Get<GameObject>(flow, "_mainMenuScreen").activeSelf, "collector returns to hub");
        Check(!collectorScreen.activeSelf && Get<string>(hub, "_page") == "bank",
            "completed collector game returns to the bank after its live token has been cleared");
        InspectLayout("appflow-collector-return"); Save("hub-collector-return.png");
    }

    private static void CheckTableRules(MonoBehaviour table, string game, string entry)
    {
        if (game == "durak")
        {
            var settings = Get<DurakMatchSettings>(table, "_settings");
            Check(settings.PlayerCount == 4 && settings.DeckSize == DeckKind.Twenty4 && settings.AllowTransfer
                && settings.ThrowInMode == CardGame.Core.Games.Durak.ThrowInMode.NeighboursOnly && settings.AllowCheating,
                entry + " applies all selected Durak rules rather than defaults");
        }
        else if (game == "bluff")
        {
            var settings = Get<BluffMatchSettings>(table, "_settings");
            Check(settings.PlayerCount == 4 && settings.DeckSize == DeckKind.FiftyTwo && !settings.Shootout,
                entry + " applies the 52-card bluff game without shootout");
        }
        else if (game == "chests")
        {
            var settings = Get<ChestsMatchSettings>(table, "_settings");
            Check(settings.PlayerCount == 4 && settings.DeckSize == DeckKind.FiftyTwo,
                entry + " applies the 52-card chests game");
        }
        else if (game == "govno")
        {
            var settings = Get<GovnoMatchSettings>(table, "_settings");
            Check(settings.PlayerCount == 4 && settings.Chaos, entry + " applies Govno chaos mode");
        }
        else
        {
            var settings = Get<PokerMatchSettings>(table, "_settings");
            Check(settings.PlayerCount == 4, entry + " applies the selected poker participant count");
        }
    }

    private static async Task CheckInterruptedRecovery()
    {
        var persistence = new MemoryStore();
        var backend = new LocalPlayerHubService(persistence, clock);
        Check((await backend.LoadAsync()).Success, "recovery fixture initialized in memory");
        var began = await backend.ExecuteAsync(new HubCommand { Id = "qa-interrupted", Action = "begin_match", Target = "durak", Amount = 67 });
        Check(began.Success && began.Snapshot.ActiveMatchId == "qa-interrupted", "persisted interrupted match fixture");
        var balance = began.Snapshot.Profile.Coins;
        PlayerHubRuntime.UseService(new LocalPlayerHubService(persistence, clock));
        var recovered = await PlayerHubRuntime.LoadAsync();
        Check(recovered.Success && string.IsNullOrEmpty(recovered.Snapshot.ActiveMatchId)
            && recovered.Snapshot.Matches.Single(entry => entry.Id == "qa-interrupted").Abandoned, "cold load forfeits interrupted match");
        Check(recovered.Snapshot.Profile.Coins == balance && recovered.Snapshot.Profile.Matches == 0,
            "recovery neither refunds stake nor awards completed-match progress");
        var recoveryCount = recovered.Snapshot.Receipts.Count(receipt => receipt.Id == "recover:qa-interrupted");
        PlayerHubRuntime.UseService(new LocalPlayerHubService(persistence, clock));
        var twice = await PlayerHubRuntime.LoadAsync();
        Check(twice.Success && recoveryCount == 1 && twice.Snapshot.Receipts.Count(receipt => receipt.Id == "recover:qa-interrupted") == 1
            && twice.Snapshot.Profile.Coins == balance, "reloading again cannot apply a second forfeit");
    }

    private static async Task CheckResultSaveRetry()
    {
        var persistence = new MemoryStore();
        PlayerHubRuntime.UseService(new LocalPlayerHubService(persistence, clock));
        Check((await PlayerHubRuntime.LoadAsync()).Success && (await PlayerHubRuntime.BeginGameAsync("poker")).Success,
            "failed-save fixture registers its match in memory");
        var before = PlayerHubRuntime.Current.Profile.Matches;
        var token = PlayerHubRuntime.Current.ActiveMatchId;
        var balance = PlayerHubRuntime.Current.Profile.Coins;
        persistence.FailNextSave = true;
        HubMatchReporter.ReportMatchCompleted("poker", true);
        await WaitFor(() => PlayerHubRuntime.HasPendingResult, "failed result remains pending");
        Check(PlayerHubRuntime.Current.ActiveMatchId == token && PlayerHubRuntime.Current.Profile.Matches == before
            && PlayerHubRuntime.Current.Profile.Coins == balance, "failed save retains token and grants no reward");
        Check(!(await PlayerHubRuntime.BeginGameAsync("durak")).Success && PlayerHubRuntime.Current.ActiveMatchId == token,
            "pending result blocks a new quick game");
        Check((await PlayerHubRuntime.RetryPendingResultAsync()).Success && !PlayerHubRuntime.HasPendingResult
            && string.IsNullOrEmpty(PlayerHubRuntime.Current.ActiveMatchId) && PlayerHubRuntime.Current.Profile.Matches == before + 1,
            "retry saves the original result and clears the token");
        var paid = PlayerHubRuntime.Current.Profile.Coins;
        Check((await PlayerHubRuntime.RetryPendingResultAsync()).Success && PlayerHubRuntime.Current.Profile.Matches == before + 1
            && PlayerHubRuntime.Current.Profile.Coins == paid, "retry after success cannot award twice");
    }

    private static async Task<HubResult> Dispatch(string action, string target = "", string text = "", int amount = 0, bool flag = false, int value = 0)
    {
        var result = await (Task<HubResult>)Call(hub, "Dispatch", action, target, text, amount, flag, value, null);
        Check(result.Success, action + " succeeds: " + result.Error);
        await NextFrame(); return result;
    }
    private static HubRoom CurrentRoom() => PlayerHubRuntime.Current.Rooms.FirstOrDefault(room => room.Id == PlayerHubRuntime.Current.CurrentRoomId);
    private static IEnumerable<Button> ActiveButtons() => hub.GetComponentsInChildren<Button>()
        .Where(button => button.gameObject.activeInHierarchy && button.IsInteractable());
    private static string ButtonLabel(Button button) => button.GetComponentInChildren<Text>()?.text ?? button.name;
    private static async Task Click(string label)
    {
        // Навигация в стиле вывесок — заглавными («НАЗАД», «ОТОЙТИ»): надпись сравнивается без учёта регистра.
        var choices = ActiveButtons().Where(button => string.Equals(ButtonLabel(button), label, StringComparison.OrdinalIgnoreCase)).ToArray();
        Check(choices.Length == 1, "unique enabled button: " + label + " (found " + choices.Length + ")");
        await ClickButton(choices[0], label);
    }
    private static async Task ClickButton(Button button, string label)
    {
        var hit = Hit((RectTransform)button.transform);
        Check(hit.gameObject != null && (hit.gameObject == button.gameObject || hit.gameObject.transform.IsChildOf(button.transform)), "click reaches " + label);
        var pointer = new PointerEventData(events) { button = PointerEventData.InputButton.Left, position = hit.screenPosition,
            pointerCurrentRaycast = hit, pointerPressRaycast = hit };
        ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        await WaitFor(() => !Get<bool>(hub, "_busy"), "button request: " + label);
        await NextFrame();
    }

    private static void InspectLayout(string route)
    {
        Canvas.ForceUpdateCanvases(); camera.Render();
        foreach (var control in hub.GetComponentsInChildren<Selectable>().Where(item => item.gameObject.activeInHierarchy && item.IsInteractable()))
        {
            var rect = (RectTransform)control.transform;
            var name = control is Button button ? ButtonLabel(button) : control.name;
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            // В листаемом списке проверяется то, что видно: строки за краем срезаны маской и ждут прокрутки.
            var mask = control.GetComponentInParent<RectMask2D>();
            if (mask != null && !InsideMask(corners, mask)) continue;
            if (corners.Select(corner => camera.WorldToViewportPoint(corner)).Any(point => point.x < -.002f || point.x > 1.002f || point.y < -.002f || point.y > 1.002f))
                LayoutIssues.Add(route + ": off-screen control " + name);
            var hit = Hit(rect);
            if (hit.gameObject == null || (hit.gameObject != control.gameObject && !hit.gameObject.transform.IsChildOf(control.transform)))
                LayoutIssues.Add(route + ": blocked control " + name + " by " + (hit.gameObject == null ? "nothing" : hit.gameObject.name));
        }
        foreach (var label in hub.GetComponentsInChildren<Text>().Where(text => text.gameObject.activeInHierarchy && !string.IsNullOrWhiteSpace(text.text)))
        {
            if (label.GetComponentInParent<InputField>() != null || label.resizeTextForBestFit) continue;
            // Custom sign meshes fit their painted surfaces after Text generation; CitySignQa checks
            // that geometry. Text's unwarped preferred height is not their displayed height.
            if (label.GetComponent<HubSignText>() != null) continue;
            var settings = label.GetGenerationSettings(new Vector2(label.rectTransform.rect.width, 0));
            var needed = label.cachedTextGeneratorForLayout.GetPreferredHeight(label.text, settings) / label.pixelsPerUnit;
            if (needed > label.rectTransform.rect.height + 2f)
                LayoutIssues.Add(route + ": clipped text (" + needed.ToString("F1") + ">" + label.rectTransform.rect.height + "): " + label.text.Replace('\n', ' '));
        }
        Check(true, "checked controls and text: " + route);
    }

    private static bool InsideMask(Vector3[] corners, RectMask2D mask)
    {
        var area = new Vector3[4]; mask.rectTransform.GetWorldCorners(area);
        return corners.All(corner => corner.x >= area[0].x - .5f && corner.x <= area[2].x + .5f
            && corner.y >= area[0].y - .5f && corner.y <= area[2].y + .5f);
    }

    private static RaycastResult Hit(RectTransform target)
    {
        Canvas.ForceUpdateCanvases(); camera.Render();
        var position = RectTransformUtility.WorldToScreenPoint(camera, target.TransformPoint(target.rect.center));
        var pointer = new PointerEventData(events) { position = position };
        var hits = new List<RaycastResult>(); canvasObject.GetComponent<GraphicRaycaster>().Raycast(pointer, hits);
        return hits.Count == 0 ? default : hits[0];
    }
    private static Task NextFrame()
    {
        var next = new TaskCompletionSource<bool>();
        EditorApplication.delayCall += () => next.SetResult(true);
        return next.Task;
    }
    private static async Task WaitFor(Func<bool> predicate, string label)
    {
        var deadline = EditorApplication.timeSinceStartup + 20;
        while (!predicate())
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Timeout: " + label);
            await NextFrame();
        }
    }
    private static T Get<T>(object value, string name) => (T)value.GetType().GetField(name, Fields).GetValue(value);
    private static void Set(object value, string name, object field) => value.GetType().GetField(name, Fields).SetValue(value, field);
    private static object Call(object value, string name, params object[] arguments) => value.GetType().GetMethod(name, Fields).Invoke(value, arguments);
    private static void Check(bool value, string message)
    { if (!value) throw new Exception("FAILED: " + message); checks++; }
    private static void OnLog(string message, string stack, LogType type)
    { if (type == LogType.Exception || type == LogType.Assert || type == LogType.Error) RuntimeErrors.Add(message); }
    private static void FinishWhenReady()
    {
        if (operation == null || !operation.IsCompleted) return;
        EditorApplication.update -= FinishWhenReady; Application.logMessageReceived -= OnLog;
        File.WriteAllLines(Path.Combine(Output, "layout-issues.txt"), LayoutIssues);
        if (operation.IsFaulted)
        {
            File.WriteAllText(Path.Combine(Output, "error.txt"), operation.Exception.ToString());
            Debug.LogException(operation.Exception); EditorApplication.Exit(1); return;
        }
        File.WriteAllText(Path.Combine(Output, "verification.txt"), EntranceOnly
            ? checks + " assertions passed: five table entrances, four modes, shared rules, bot launch callbacks, create and join by code, free quick join, camera separation, scroll and text at 1600x900/1280x720/2100x900. Memory-only profile."
            : GamesOnly
            ? checks + " assertions passed: focused games/rules QA at 1600x900, four Club rows without poker, masked smooth-scroll component, last row reachable, scroll position restored, rules and table navigation, casino poker entrance, settings plaque and controls layout. No real profile store was opened or written."
            : ClubOnly
            ? checks + " assertions passed: focused Club panorama QA, eight tables, four inactive table hover/no-op clicks, bridge games board and two-way arch, swipe/A-D movement, old game route after panning, all street exit paths, full-screen coverage during pan/visit/dive and at 2100x900/1600x900/1280x720. No real profile store was opened or written."
            : PlacesOnly
            ? checks + " assertions passed: focused city-places QA at 1600x900, 33 unique interactive objects, outline hit targets, camera approach/return and coverage, floor transitions, place routes, inactive table clicks, hall passage, outdoor fountain stays on home with local controls and returns without profile changes. No real profile store was opened or written."
            : checks + " assertions passed: memory-only async service, " + Routes.Length
            + " routes at 1600x900/2040x900/1280x720, UI raycasts/text fit, lobby of only the city (the author works on the background) with the shell on section pages, Card City street in the v2 style (station end, Old Town, square, Tuzyara's district with his portrait, harbor quay, port market, sea end) joined by painted bridges, smooth rides on D/A and arrows (hold to keep going, nudge at the ends), drag with inertia that rests where released and settles into a nearby district, rubber-band ends, a lost pointer release that cannot freeze the city, the underground under the square (tap on the stairs or S/down dives in through darkness and pulls back from its own stairs, W/up or its stairs climb back, S far from the square rides there first, A/D on the way cancel, the underground neither scrolls nor walks, full-screen at every size), no Royal Hill any more (the castle behind the Club is scenery with the city name CARD CITY on its gate board; tapping it or W/up leads nowhere; floors connect only through the street; the Archive moved into the former police building in the Old Town), the Club hall inside the Club doors (four game tables and the games board, leave by button, Back, Escape or S), clickable places (the mouse outlines a building in gold, a tap walks the camera to its door and opens its screen, Back walks out; stairs and the Club doors lead to floors), Russian signs on every place (text on the painted blank boards, gold-framed plaques where there is none; right font, fits its board, readable size, whole on screen, none in the mirrored outskirts), full-screen coverage at 16:9/20:9/4:3, distinct silver/gold currency artwork, game-selection/room filters/shared setup, per-game options and deck-capacity synchronization, quick/create draft synchronization, saved room rules and isolated copies, host edit/save/cancel and readiness reset, nested shop/chat history and room-creation return paths, nickname/friends/cosmetics, incoming/outgoing invitations, long room/friend chats, create/join/ready/bot-room launch with captured roster, completion/replay idempotence, spider achievement, fake-clock bank, actual quick and room AppFlow launch/exit of all five games with selected nondefault rules, collector redirection and return to bank, exactly-once cold-load forfeit and failed-result retry. No real profile store was opened or written.");
        Debug.Log("PLAYER HUB QA PASSED: " + checks); EditorApplication.Exit(0);
    }

    private static void Begin(int width, int height)
    {
        if (canvasObject != null) UnityEngine.Object.DestroyImmediate(canvasObject);
        if (camera != null)
        {
            var target = camera.targetTexture;
            UnityEngine.Object.DestroyImmediate(camera.gameObject);
            if (target != null) UnityEngine.Object.DestroyImmediate(target);
        }
        canvasObject = new GameObject("Player hub QA canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
        var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
        camera = new GameObject("Player hub QA camera", typeof(Camera)).GetComponent<Camera>();
        camera.transform.position = new Vector3(0, 0, -100); camera.orthographic = true;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.07f, .27f, .18f);
        camera.nearClipPlane = .1f; camera.farClipPlane = 200; canvas.worldCamera = camera;
        var eventObject = new GameObject("Player hub QA events", typeof(EventSystem)); eventObject.transform.SetParent(canvasObject.transform, false);
        events = eventObject.GetComponent<EventSystem>(); ResizeCanvas(width, height);
    }
    private static void ResizeCanvas(int width, int height)
    {
        ((RectTransform)canvasObject.transform).sizeDelta = new Vector2(width, height);
        camera.orthographicSize = height / 2f;
        if (camera.targetTexture != null) { var old = camera.targetTexture; camera.targetTexture = null; UnityEngine.Object.DestroyImmediate(old); }
        camera.targetTexture = new RenderTexture(width, height, 24); camera.targetTexture.Create();
    }
    private static void Save(string name)
    {
        Canvas.ForceUpdateCanvases(); camera.Render(); var previous = RenderTexture.active;
        RenderTexture.active = camera.targetTexture;
        var texture = new Texture2D(camera.targetTexture.width, camera.targetTexture.height, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); texture.Apply();
        File.WriteAllBytes(Path.Combine(Output, name), texture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(texture); RenderTexture.active = previous;
    }
    private static string FindOutput()
    {
        for (var folder = new DirectoryInfo(Application.dataPath); folder != null; folder = folder.Parent)
            if (File.Exists(Path.Combine(folder.FullName, "src", "CardGame.Core", "CardGame.Core.csproj")))
                return Path.Combine(folder.FullName, "screens",
                    Environment.GetEnvironmentVariable("HUB_QA_OUTPUT") ?? "player-hub");
        throw new InvalidOperationException("QA project must be below the cardgame workspace.");
    }
}
