// Companion partial for PlayerHubQa.cs: the city HUD (2026-10-31). Copy with the other player-hub QA files
// to Assets/Scripts/Editor and run -executeMethod PlayerHubQa.RunHud (HUB_QA_OUTPUT selects the screens subfolder).
// The QA hub has Update disabled, so the HUD is refreshed by calling UpdateCityHud directly. Memory-only profile.
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CardGame.Core.Meta;
using CardGameClient;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static partial class PlayerHubQa
{
    public static void RunHud()
    {
        Directory.CreateDirectory(Output);
        var previousError = Path.Combine(Output, "error.txt");
        if (File.Exists(previousError)) File.Delete(previousError);
        Application.logMessageReceived += OnLog;
        // PLAYER_HUB_QA_PHONE=1 — the same checks with the phone density override (bigger HUD).
        if (PhoneUi) typeof(AppFlowController).Assembly.GetType("CardGameClient.MobileUi").GetField("TestPhone").SetValue(null, true);
        store = new MemoryStore(); clock = new FixedClock();
        PlayerHubRuntime.UseService(new LocalPlayerHubService(store, clock), clock);
        EditorApplication.update += FinishHudWhenReady;
        EditorApplication.delayCall += () => operation = RunHudChecks();
    }

    private static async Task RunHudChecks()
    {
        var loaded = await PlayerHubRuntime.LoadAsync();
        Check(loaded.Success, "profile initializes through the injected memory store");
        Begin(1600, 900);
        var root = new GameObject("Player hub QA", typeof(RectTransform));
        root.transform.SetParent(canvasObject.transform, false);
        ((RectTransform)root.transform).sizeDelta = new Vector2(1600, 900);
        hub = root.AddComponent<PlayerHubController>(); hub.enabled = false;
        hub.Configure(() => playCalls++, () => avatarCalls++, (game, count) => { launchedGame = game; launchedCount = count; },
            game => { quickGame = game; quickCalls++; });
        Call(hub, "Start");
        await WaitFor(() => Get<HubSnapshot>(hub, "_state") != null && !Get<bool>(hub, "_busy"), "initial async hub load");
        hub.GoHome(); await NextFrame(); SettleOn(1);
        for (var i = 0; i < 120; i++) await NextFrame();

        Call(hub, "UpdateCityHud");
        var hud = Get<RectTransform>(hub, "_cityHud");
        Check(hud != null && hud.gameObject.activeSelf, "the city HUD is shown on the street");
        foreach (var name in new[] { "Профиль", "Настройки", "Задания", "Друзья", "Почта", "Коллекция" })
            Check(hud.GetComponentsInChildren<Button>().Count(b => b.name == name) == 1, "HUD has one button " + name);
        Check(hud.GetComponentsInChildren<Button>().Count(b => b.name == "Пополнить") == 2, "both coin plates have a plus");
        Check(Get<Text>(hub, "_hudSilver").text == Get<HubSnapshot>(hub, "_state").Profile.Coins.ToString("N0",
            System.Globalization.CultureInfo.GetCultureInfo("ru-RU")), "silver coins match the profile");

        foreach (var size in new[] { new Vector2Int(1600, 900), new Vector2Int(2400, 1080), new Vector2Int(1280, 960) })
        {
            ResizeCanvas(size.x, size.y); ((RectTransform)hub.transform).sizeDelta = size; Call(hub, "Resize");
            Call(hub, "UpdateCityHud"); Canvas.ForceUpdateCanvases();
            var view = new Vector3[4]; ((RectTransform)hub.transform).GetWorldCorners(view);
            foreach (var part in new[] { "_hudProfile", "_hudWallet", "_hudSideLeft", "_hudSideRight", "_hudPlay" })
            {
                var corners = new Vector3[4]; Get<RectTransform>(hub, part).GetWorldCorners(corners);
                Check(corners[0].x >= view[0].x - 1 && corners[2].x <= view[2].x + 1 && corners[0].y >= view[0].y - 1 && corners[2].y <= view[2].y + 1,
                    part + " stays on screen at " + size.x + "x" + size.y);
            }
            Save("hud-" + size.x + "x" + size.y + ".png");
        }
        ResizeCanvas(1600, 900); ((RectTransform)hub.transform).sizeDelta = new Vector2(1600, 900); Call(hub, "Resize");

        await Click("Настройки"); await NextFrame();
        Call(hub, "UpdateSettingsWindow", 1f, false); Call(hub, "UpdateCityHud");
        Check(Get<string>(hub, "_page") == "home" && Get<bool>(hub, "_settingsShown"), "the gear opens the settings window over the city");
        Check(!hud.gameObject.activeSelf, "the city HUD hides under the settings window");
        Save("settings-" + (PhoneUi ? "phone-" : "") + "1600x900.png");
        var settings = Get<HubSnapshot>(hub, "_state").Settings;
        var sound = settings.Sound;
        var window = Get<RectTransform>(hub, "_settingsLayer");
        await ClickButton(window.GetComponentsInChildren<Button>().Single(b => b.name == "Toggle sound"), "sound switch");
        await WaitFor(() => !Get<bool>(hub, "_busy"), "sound setting saved");
        Check(Get<HubSnapshot>(hub, "_state").Settings.Sound == !sound, "the sound switch flips the saved setting");
        var volume = Get<HubSnapshot>(hub, "_state").Settings.Volume;
        await ClickButton(window.GetComponentsInChildren<Button>().Single(b => b.name == "Тише"), "volume down");
        await WaitFor(() => !Get<bool>(hub, "_busy"), "volume saved");
        Check(Get<HubSnapshot>(hub, "_state").Settings.Volume == volume - 10, "volume down lowers the volume by 10");
        // Ползунок: нажатие на 67 %, ведение до 52 %, отпускание — сохраняется ровно 52.
        var slider = window.GetComponentsInChildren<HubSlider>().Single();
        Vector2 TrackPoint(float share)
        {
            var rect = slider.Track.rect;
            var local = new Vector2(Mathf.Lerp(rect.xMin + slider.Inset, rect.xMax - slider.Inset, share), rect.center.y);
            return RectTransformUtility.WorldToScreenPoint(camera, slider.Track.TransformPoint(local));
        }
        var finger = new PointerEventData(events) { button = PointerEventData.InputButton.Left, position = TrackPoint(.67f) };
        ExecuteEvents.Execute(slider.gameObject, finger, ExecuteEvents.pointerDownHandler);
        Check(Get<int>(hub, "_settingsDrag") == 67, "a press on the volume track jumps to 67");
        finger.position = TrackPoint(.52f);
        ExecuteEvents.Execute(slider.gameObject, finger, ExecuteEvents.dragHandler);
        Check(Get<int>(hub, "_settingsDrag") == 52, "dragging the volume moves it to 52");
        ExecuteEvents.Execute(slider.gameObject, finger, ExecuteEvents.pointerUpHandler);
        await WaitFor(() => !Get<bool>(hub, "_busy") && Get<int>(hub, "_settingsDrag") < 0, "volume slider saved");
        Check(Get<HubSnapshot>(hub, "_state").Settings.Volume == 52, "releasing the slider saves 52, not a multiple of 10");
        for (var i = 0; i < 20; i++) Call(hub, "UpdateSettingsWindow", .05f, false);
        Save("settings-changed-" + (PhoneUi ? "phone-" : "") + "1600x900.png");
        foreach (var size in new[] { new Vector2Int(2400, 1080), new Vector2Int(1280, 960) })
        {
            ResizeCanvas(size.x, size.y); ((RectTransform)hub.transform).sizeDelta = size; Call(hub, "Resize");
            Canvas.ForceUpdateCanvases();
            var view = new Vector3[4]; ((RectTransform)hub.transform).GetWorldCorners(view);
            var board = new Vector3[4]; Get<RectTransform>(hub, "_settingsWindow").GetWorldCorners(board);
            Check(board[0].x >= view[0].x && board[2].x <= view[2].x && board[0].y >= view[0].y - 1 && board[2].y <= view[2].y + 1,
                "the settings board fits the screen at " + size.x + "x" + size.y);
            Save("settings-" + (PhoneUi ? "phone-" : "") + size.x + "x" + size.y + ".png");
        }
        ResizeCanvas(1600, 900); ((RectTransform)hub.transform).sizeDelta = new Vector2(1600, 900); Call(hub, "Resize");
        await Click("ЗАКРЫТЬ");
        Call(hub, "UpdateSettingsWindow", 1f, false); Call(hub, "UpdateCityHud");
        Check(!Get<bool>(hub, "_settingsShown") && !window.gameObject.activeSelf, "ЗАКРЫТЬ closes the settings window");
        Check(hud.gameObject.activeSelf, "the city HUD returns after the settings window");
        // Окно заданий: открывается кнопкой «Задания», выполненное поручение забирается, серебро приходит.
        var tag = PhoneUi ? "phone-" : "";
        // У кнопки раздела первая надпись — число на красной точке, поэтому ищем по имени.
        await ClickButton(hud.GetComponentsInChildren<Button>().Single(b => b.name == "Задания"), "Задания"); await NextFrame();
        Call(hub, "UpdateTasksWindow", 1f, false); Call(hub, "UpdateCityHud");
        Check(Get<string>(hub, "_page") == "home" && Get<bool>(hub, "_tasksShown"), "Задания opens the tasks window over the city");
        Check(!hud.gameObject.activeSelf, "the city HUD hides under the tasks window");
        await WaitFor(() => !Get<bool>(hub, "_busy"), "quests marked seen");
        Save("tasks-" + tag + "1600x900.png");
        Call(hub, "Send", "quests_debug_complete", "", "", 0, false);
        await WaitFor(() => !Get<bool>(hub, "_busy"), "daily quests completed");
        for (var i = 0; i < 10; i++) Call(hub, "UpdateTasksWindow", .05f, false);
        var tasks = Get<RectTransform>(hub, "_tasksLayer");
        var claims = tasks.GetComponentsInChildren<Button>().Where(b => b.name == "ЗАБРАТЬ").ToArray();
        Check(claims.Length == 3, "three finished daily quests offer ЗАБРАТЬ (found " + claims.Length + ")");
        Save("tasks-ready-" + tag + "1600x900.png");
        var before = Get<HubSnapshot>(hub, "_state").Profile.Coins;
        await ClickButton(claims[0], "claim a quest");
        await WaitFor(() => !Get<bool>(hub, "_busy"), "quest reward saved");
        var after = Get<HubSnapshot>(hub, "_state");
        Check(after.Profile.Coins > before && after.Quests.Count(q => q.Claimed) == 1, "ЗАБРАТЬ credits the reward and marks the quest done");
        for (var i = 0; i < 30; i++) Call(hub, "UpdateTasksWindow", .05f, false);
        Check(Get<Text>(hub, "_tasksWallet").text == after.Profile.Coins.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("ru-RU")),
            "the window wallet counts up to the new balance");
        Save("tasks-claimed-" + tag + "1600x900.png");
        foreach (var size in new[] { new Vector2Int(2400, 1080), new Vector2Int(1280, 960) })
        {
            ResizeCanvas(size.x, size.y); ((RectTransform)hub.transform).sizeDelta = size; Call(hub, "Resize");
            Canvas.ForceUpdateCanvases();
            var view = new Vector3[4]; ((RectTransform)hub.transform).GetWorldCorners(view);
            var board = new Vector3[4]; Get<RectTransform>(hub, "_tasksWindow").GetWorldCorners(board);
            Check(board[0].x >= view[0].x && board[2].x <= view[2].x && board[0].y >= view[0].y - 1 && board[2].y <= view[2].y + 1,
                "the tasks board fits the screen at " + size.x + "x" + size.y);
            Save("tasks-" + tag + size.x + "x" + size.y + ".png");
        }
        ResizeCanvas(1600, 900); ((RectTransform)hub.transform).sizeDelta = new Vector2(1600, 900); Call(hub, "Resize");
        await Click("ЗАКРЫТЬ");
        Call(hub, "UpdateTasksWindow", 1f, false); Call(hub, "UpdateCityHud");
        Check(!Get<bool>(hub, "_tasksShown") && !tasks.gameObject.activeSelf && hud.gameObject.activeSelf, "ЗАКРЫТЬ closes the tasks window and the HUD returns");

        // Окно коллекции: вкладки, чужая вещь — «В ЛАВКУ», открытая — «ВЫБРАТЬ» и печать, помидор — к броску.
        await ClickButton(hud.GetComponentsInChildren<Button>().Single(b => b.name == "Коллекция"), "Коллекция"); await NextFrame();
        Call(hub, "UpdateCollectionWindow", 1f, false); Call(hub, "UpdateCityHud");
        Check(Get<string>(hub, "_page") == "home" && Get<bool>(hub, "_collectionShown"), "Коллекция opens the collection window over the city");
        Check(!hud.gameObject.activeSelf, "the city HUD hides under the collection window");
        var collection = Get<RectTransform>(hub, "_collectionLayer");
        for (var tab = 0; tab < 6; tab++)
        {
            var count = ((System.Collections.ICollection)Call(hub, "CollectionItems", tab)).Count;
            Check(count > 0 && count <= 8, "collection tab " + tab + " fits its 4×2 shelf (" + count + " items)");
        }
        Save("collection-" + tag + "1600x900.png");
        await ClickButton(collection.GetComponentsInChildren<Button>().Single(b => b.name == "Tab РАМКИ"), "frames tab");
        Set(hub, "_collectionItem", "frame_gold"); Call(hub, "UpdateCollectionWindow", .05f, false);
        var action = Get<Button>(hub, "_collectionAction");
        Check(action.GetComponentInChildren<Text>().text == "В ЛАВКУ", "a locked item offers В ЛАВКУ");
        Save("collection-locked-" + tag + "1600x900.png");
        Call(hub, "Send", "cosmetic_unlock", "frame_gold", "", 0, false);
        await WaitFor(() => !Get<bool>(hub, "_busy"), "frame unlocked");
        Call(hub, "UpdateCollectionWindow", .05f, false);
        Check(action.GetComponentInChildren<Text>().text == "ВЫБРАТЬ", "an owned item offers ВЫБРАТЬ");
        await ClickButton(action, "equip the gold frame");
        await WaitFor(() => !Get<bool>(hub, "_busy"), "frame equipped");
        Call(hub, "UpdateCollectionWindow", .05f, false);
        Check(Get<HubSnapshot>(hub, "_state").Equipped.Any(slot => slot.Slot == "frame" && slot.ItemId == "frame_gold"), "ВЫБРАТЬ equips the item");
        Check(action.GetComponentInChildren<Text>().text == "ВЫБРАНО" && !action.interactable, "the equipped item shows ВЫБРАНО");
        Save("collection-equipped-" + tag + "1600x900.png");
        var avatarsBefore = avatarCalls;
        await ClickButton(collection.GetComponentsInChildren<Button>().Single(b => b.name == "СМЕНИТЬ ПЕРСОНАЖА"), "change character");
        Check(avatarCalls == avatarsBefore + 1, "СМЕНИТЬ ПЕРСОНАЖА opens the character picker");
        foreach (var size in new[] { new Vector2Int(2400, 1080), new Vector2Int(1280, 960) })
        {
            ResizeCanvas(size.x, size.y); ((RectTransform)hub.transform).sizeDelta = size; Call(hub, "Resize");
            Canvas.ForceUpdateCanvases();
            var view = new Vector3[4]; ((RectTransform)hub.transform).GetWorldCorners(view);
            var board = new Vector3[4]; Get<RectTransform>(hub, "_collectionWindow").GetWorldCorners(board);
            Check(board[0].x >= view[0].x && board[2].x <= view[2].x && board[0].y >= view[0].y - 1 && board[2].y <= view[2].y + 1,
                "the collection board fits the screen at " + size.x + "x" + size.y);
            Save("collection-" + tag + size.x + "x" + size.y + ".png");
        }
        ResizeCanvas(1600, 900); ((RectTransform)hub.transform).sizeDelta = new Vector2(1600, 900); Call(hub, "Resize");
        await ClickButton(collection.GetComponentsInChildren<Button>().Single(b => b.name == "Tab ЭМОЦИИ"), "emotes tab");
        Set(hub, "_collectionItem", "tomato_pack"); Call(hub, "UpdateCollectionWindow", .05f, false);
        Check(action.GetComponentInChildren<Text>().text == "БРОСИТЬ", "tomatoes offer БРОСИТЬ");
        await ClickButton(action, "throw tomatoes");
        Call(hub, "UpdateCollectionWindow", 1f, false);
        Check(Get<string>(hub, "_page") == "throw:tomato_pack" && !Get<bool>(hub, "_collectionShown"), "БРОСИТЬ closes the window and opens the throw page");
        hub.Back(); await NextFrame();
        Check(Get<string>(hub, "_page") == "home", "Back from the throw page returns home");

        // Откуда бы ни открыли — те же окна: со страницы прогресса «Настройки» и «Задания» ложатся поверх неё.
        hub.Navigate("progress"); await NextFrame();
        hub.Navigate("settings"); Call(hub, "UpdateSettingsWindow", 1f, false);
        Check(Get<string>(hub, "_page") == "progress" && Get<bool>(hub, "_settingsShown"), "settings from a section page opens the same window over it");
        hub.Back(); Call(hub, "UpdateSettingsWindow", 1f, false);
        Check(Get<string>(hub, "_page") == "progress" && !Get<bool>(hub, "_settingsShown"), "Back closes the window and keeps the page");
        hub.Navigate("quests"); Call(hub, "UpdateTasksWindow", 1f, false);
        Check(Get<string>(hub, "_page") == "progress" && Get<bool>(hub, "_tasksShown"), "quests from a section page open the tasks window over it");
        Save("tasks-over-progress-" + tag + "1600x900.png");
        hub.Back(); Call(hub, "UpdateTasksWindow", 1f, false);
        Check(!Get<bool>(hub, "_tasksShown"), "Back closes the tasks window");
        hub.Navigate("inventory"); Call(hub, "UpdateCollectionWindow", 1f, false);
        Check(Get<string>(hub, "_page") == "progress" && Get<bool>(hub, "_collectionShown"), "inventory from a section page opens the collection window over it");
        hub.Back(); Call(hub, "UpdateCollectionWindow", 1f, false);
        Check(Get<string>(hub, "_page") == "progress" && !Get<bool>(hub, "_collectionShown"), "Back closes the collection window and keeps the page");
        hub.Navigate("cash:gold"); Call(hub, "UpdateCashWindow", 1f, false);
        Check(Get<string>(hub, "_page") == "progress" && Get<bool>(hub, "_cashShown") && Get<int>(hub, "_cashTab") == 1,
            "Пополнить from a section page opens the cash window over it");
        hub.Back(); Call(hub, "UpdateCashWindow", 1f, false);
        Check(Get<string>(hub, "_page") == "progress" && !Get<bool>(hub, "_cashShown"), "Back closes the cash window and keeps the page");

        // «ДОСКА ШЕСТЁРКИ ›»: доехать до его дома, войти и открыть пробковую доску с теми же поручениями.
        hub.GoHome(); await NextFrame();
        hub.Navigate("quests"); Call(hub, "UpdateTasksWindow", 1f, false);
        await ClickButton(tasks.GetComponentsInChildren<Button>().Single(b => b.name == "ДОСКА ШЕСТЁРКИ ›"), "to the Six board");
        Check(!Get<bool>(hub, "_tasksShown") && (Get<int>(hub, "_mapTripPlace") >= 0 || Get<bool>(hub, "_questsShown")),
            "the Six board button closes the window and starts the trip");
        for (var i = 0; i < 900 && !Get<bool>(hub, "_questsShown"); i++)
        {
            Call(hub, "PrepareFloors"); Call(hub, "ContinueMapTrip");
            Call(hub, "MoveCity", 1 / 30f); Call(hub, "MoveDive", 1 / 30f); Call(hub, "MoveFloorPan", 1 / 30f);
            Call(hub, "UpdateTasksWindow", 1 / 30f, false);
            if (i % 10 == 0) await NextFrame();
        }
        Check(Get<bool>(hub, "_questsShown"), "the trip ends at the Six's cork board, opened");
        Call(hub, "UpdateSixQuests", 1f, false);
        Save("six-board-" + tag + "1600x900.png");
        Call(hub, "CloseSixQuests"); Call(hub, "UpdateSixQuests", 1f, false);
        await CheckCashWindow(hud, tag);
        Check(RuntimeErrors.Count == 0, "no HUD runtime exceptions: " + string.Join(" | ", RuntimeErrors));
    }

    /// <summary>Касса банка Тузяры: «+» у монет, наборы (демо), ролик за награду, «Без рекламы», проезд к кредиту.</summary>
    private static async Task CheckCashWindow(RectTransform hud, string tag)
    {
        hub.GoHome(); await NextFrame();
        Call(hub, "UpdateCityHud");
        var tabs = new System.Collections.Generic.List<int>();
        foreach (var plus in hud.GetComponentsInChildren<Button>().Where(b => b.name == "Пополнить").ToArray())
        {
            await ClickButton(plus, "coin plus"); Call(hub, "UpdateCashWindow", 1f, false);
            Check(Get<bool>(hub, "_cashShown"), "a coin plus opens the cash window");
            tabs.Add(Get<int>(hub, "_cashTab"));
            Call(hub, "CloseCashWindow"); Call(hub, "UpdateCashWindow", 1f, false); Call(hub, "UpdateCityHud");
        }
        Check(tabs.OrderBy(t => t).SequenceEqual(new[] { 0, 1 }), "the silver plus opens silver, the gold plus opens gold");

        Call(hub, "ShowCashWindow", 0); Call(hub, "UpdateCashWindow", 1f, false); Call(hub, "UpdateCityHud");
        Check(!hud.gameObject.activeSelf, "the city HUD hides under the cash window");
        var cash = Get<RectTransform>(hub, "_cashLayer");
        Button Labelled(string text) => cash.GetComponentsInChildren<Button>().Single(b => b.GetComponentInChildren<Text>().text == text);
        Save("cash-" + tag + "1600x900.png");
        var pack = HubCash.PacksOf(HubCash.Silver).ElementAt(1);
        var before = Get<HubSnapshot>(hub, "_state").Profile.Coins;
        // Полёт монет (PlayerHubCoinFlow.cs) ведёт LateUpdate; в проверке его кадры — вызовами. Сначала — запомнить баланс.
        Call(hub, "UpdateCoinFlow", 0f); Call(hub, "UpdateCoinFlow", 5f);
        await ClickButton(Labelled(pack.Price), "buy a silver pack");
        await WaitFor(() => !Get<bool>(hub, "_busy"), "silver pack bought");
        Check(Get<HubSnapshot>(hub, "_state").Profile.Coins == before + pack.Amount, "a silver pack adds " + pack.Amount + " silver");
        for (var i = 0; i < 30; i++) Call(hub, "UpdateCashWindow", .05f, false);
        var money = System.Globalization.CultureInfo.GetCultureInfo("ru-RU");
        Check(Get<Text>(hub, "_cashSilver").text == (before + pack.Amount).ToString("N0", money), "the silver balance counts up to the new amount");
        Call(hub, "UpdateCoinFlow", .3f);
        var flows = Get<System.Collections.IList>(hub, "_flows");
        Check(flows.Count == 1 && (Text)flows[0].GetType().GetField("Counter").GetValue(flows[0]) == Get<Text>(hub, "_cashSilver")
            && Get<Text>(hub, "_cashSilver").text != (before + pack.Amount).ToString("N0", money),
            "bought silver flies into the cash balance, which counts up as the coins land");
        Save("cash-flow-" + tag + "1600x900.png");
        for (var i = 0; i < 20; i++) Call(hub, "UpdateCoinFlow", .1f);
        Check(flows.Count == 0 && Get<Text>(hub, "_cashSilver").text == (before + pack.Amount).ToString("N0", money), "the flight ends on the new balance");

        before = Get<HubSnapshot>(hub, "_state").Profile.Coins;
        await ClickButton(Labelled("СМОТРЕТЬ"), "watch an ad");
        Check(Get<float>(hub, "_cashAdTime") >= 0 && Get<RectTransform>(hub, "_cashAd").gameObject.activeSelf, "СМОТРЕТЬ starts the ad screen");
        Call(hub, "UpdateCashWindow", 2f, false);
        Save("cash-ad-" + tag + "1600x900.png");
        Check(Get<HubSnapshot>(hub, "_state").Profile.Coins == before, "no reward before the ad ends");
        for (var i = 0; i < 4; i++) Call(hub, "UpdateCashWindow", 1f, false);
        await WaitFor(() => !Get<bool>(hub, "_busy"), "ad reward saved");
        var state = Get<HubSnapshot>(hub, "_state");
        Check(state.Profile.Coins == before + HubCash.AdReward && HubCash.AdsLeft(state, PlayerHubRuntime.Clock.UnixSeconds) == HubCash.AdsPerDay - 1,
            "a watched ad pays " + HubCash.AdReward + " silver and counts against the day");

        await ClickButton(cash.GetComponentsInChildren<Button>().Single(b => b.name == "Tab ЗОЛОТО"), "gold tab");
        Call(hub, "UpdateCashWindow", .05f, false);
        var gold = HubCash.PacksOf(HubCash.Gold).First();
        await ClickButton(Labelled(gold.Price), "buy a gold pack");
        await WaitFor(() => !Get<bool>(hub, "_busy"), "gold pack bought");
        Check(Get<HubSnapshot>(hub, "_state").Profile.Gold == gold.Amount, "a gold pack adds " + gold.Amount + " gold");
        Check(Get<Text>(hub, "_hudGold").text == gold.Amount.ToString("N0", money), "the city HUD shows the gold balance");
        for (var i = 0; i < 30; i++) Call(hub, "UpdateCashWindow", .05f, false);
        Call(hub, "UpdateCityHud");
        Check(Get<Image>(hub, "_toastPill").rectTransform.anchoredPosition.y < -800, "the purchase message shows under the window board");
        Save("cash-gold-" + tag + "1600x900.png");
        await ClickButton(cash.GetComponentsInChildren<Button>().Single(b => b.name == "Tab ОБМЕН"), "exchange tab");
        Call(hub, "UpdateCashWindow", .05f, false);
        // Обмен только серебра в золото: вместо карточек — «ОТДАЁШЬ → ПОЛУЧАЕШЬ», шкала и лимит дня.
        Check(Get<RectTransform>(hub, "_cashTrade").gameObject.activeSelf && cash.GetComponentsInChildren<Image>().All(i => i.name != "Cash card"),
            "the exchange tab hides the cards");
        var trade = Get<Button>(hub, "_cashTradeButton");
        Text Give() => Get<Text>(hub, "_cashGive");
        Text Take() => Get<Text>(hub, "_cashGet");
        Check(trade.interactable && Take().text == "+5" && Give().text == (5 * HubCash.SilverPerGold).ToString("N0", money),
            "the exchange starts at 5 gold for " + 5 * HubCash.SilverPerGold + " silver");
        await ClickButton(Get<Button>(hub, "_cashPickPlus"), "one more gold"); Call(hub, "UpdateCashWindow", .05f, false);
        Check(Take().text == "+6", "+ adds a gold");
        await ClickButton(Get<Button>(hub, "_cashPickMax"), "max gold"); Call(hub, "UpdateCashWindow", .05f, false);
        Check(Take().text == "+" + HubCash.GoldPerDay && !Get<Button>(hub, "_cashPickPlus").interactable, "МАКС takes the whole daily limit");
        Call(hub, "SlideCashPick", 0f); Call(hub, "UpdateCashWindow", .05f, false);
        Check(Take().text == "+1", "the left end of the scale is one gold");
        Call(hub, "SlideCashPick", 4f / (HubCash.GoldPerDay - 1)); Call(hub, "UpdateCashWindow", .05f, false);
        Check(Take().text == "+5", "the scale picks whole gold coins");
        state = Get<HubSnapshot>(hub, "_state");
        await ClickButton(trade, "buy 5 gold for silver");
        await WaitFor(() => !Get<bool>(hub, "_busy"), "gold bought for silver");
        var afterBuy = Get<HubSnapshot>(hub, "_state");
        Check(afterBuy.Profile.Gold == state.Profile.Gold + 5 && afterBuy.Profile.Coins == state.Profile.Coins - 5 * HubCash.SilverPerGold
            && HubCash.GoldLeft(afterBuy, PlayerHubRuntime.Clock.UnixSeconds) == HubCash.GoldPerDay - 5,
            "silver buys 5 gold at " + HubCash.SilverPerGold + " each and counts against the daily limit");
        for (var i = 0; i < 30; i++) Call(hub, "UpdateCashWindow", .05f, false);
        var pips = Get<System.Collections.Generic.List<Image>>(hub, "_cashLimitPips");
        Check(pips.Count == HubCash.GoldPerDay && pips.Take(5).All(p => p.color.r < .5f) && pips.Skip(5).Take(5).All(p => p.color == Color.white),
            "the limit shows 5 coins taken today and the next 5 picked");
        Check(Get<Text>(hub, "_cashLimitLeft").text == "ЕЩЁ " + (HubCash.GoldPerDay - 5), "the limit counts what is left today");
        Call(hub, "UpdateCityHud");
        Save("cash-exchange-" + tag + "1600x900.png");
        await ClickButton(Get<Button>(hub, "_cashPickMax"), "the rest of the limit"); Call(hub, "UpdateCashWindow", .05f, false);
        await ClickButton(trade, "buy the rest of the limit");
        await WaitFor(() => !Get<bool>(hub, "_busy"), "limit bought");
        for (var i = 0; i < 30; i++) Call(hub, "UpdateCashWindow", .05f, false);
        Check(HubCash.GoldLeft(Get<HubSnapshot>(hub, "_state"), PlayerHubRuntime.Clock.UnixSeconds) == 0 && !trade.interactable
            && Get<Text>(hub, "_cashTradeNote").text.Contains("полночь"), "the exchange stops at the daily limit and says when it comes back");
        Call(hub, "UpdateCityHud");
        Save("cash-exchange-limit-" + tag + "1600x900.png");
        await ClickButton(cash.GetComponentsInChildren<Button>().Single(b => b.name == "Тест: сбросить лимит"), "test limit reset");
        await WaitFor(() => !Get<bool>(hub, "_busy"), "limit reset");
        for (var i = 0; i < 3; i++) Call(hub, "UpdateCashWindow", .05f, false);
        Check(HubCash.GoldLeft(Get<HubSnapshot>(hub, "_state"), PlayerHubRuntime.Clock.UnixSeconds) == HubCash.GoldPerDay && trade.interactable,
            "the test button resets the daily limit");
        await ClickButton(cash.GetComponentsInChildren<Button>().Single(b => b.name == "БЕЗ РЕКЛАМЫ"), "no ads");
        await WaitFor(() => !Get<bool>(hub, "_busy"), "no ads bought");
        Call(hub, "UpdateCashWindow", .05f, false);
        var noAds = cash.GetComponentsInChildren<Button>().Single(b => b.name == "БЕЗ РЕКЛАМЫ");
        Check(Get<HubSnapshot>(hub, "_state").Cash.NoAds && !noAds.interactable, "БЕЗ РЕКЛАМЫ is bought once");
        foreach (var size in new[] { new Vector2Int(2400, 1080), new Vector2Int(1280, 960) })
        {
            ResizeCanvas(size.x, size.y); ((RectTransform)hub.transform).sizeDelta = size; Call(hub, "Resize");
            Canvas.ForceUpdateCanvases();
            var view = new Vector3[4]; ((RectTransform)hub.transform).GetWorldCorners(view);
            var board = new Vector3[4]; Get<RectTransform>(hub, "_cashWindow").GetWorldCorners(board);
            Check(board[0].x >= view[0].x && board[2].x <= view[2].x && board[0].y >= view[0].y - 1 && board[2].y <= view[2].y + 1,
                "the cash board fits the screen at " + size.x + "x" + size.y);
            Save("cash-" + tag + size.x + "x" + size.y + ".png");
        }
        ResizeCanvas(1600, 900); ((RectTransform)hub.transform).sizeDelta = new Vector2(1600, 900); Call(hub, "Resize");

        // Нет счётчика на экране (настройки) — монеты летят в кошелёк, который проявляется в углу.
        // Перемены баланса с прошлых шагов долетают сразу: в игре их кадры шли бы сами.
        Call(hub, "UpdateCoinFlow", 0f); Call(hub, "UpdateCoinFlow", 5f); Call(hub, "UpdateCoinFlow", 5f);
        Call(hub, "CloseCashWindow"); Call(hub, "UpdateCashWindow", 1f, false);
        Call(hub, "ShowSettingsWindow"); Call(hub, "UpdateSettingsWindow", 1f, false); Call(hub, "UpdateCityHud");
        var coinsNow = Get<HubSnapshot>(hub, "_state").Profile.Coins;
        Call(hub, "StartCoinFlow", false, coinsNow - 40, coinsNow);
        Call(hub, "UpdateCoinFlow", .35f);
        Check(Get<RectTransform>(hub, "_flowWallet").gameObject.activeSelf, "with no coin counter on screen, a wallet shows up in the corner");
        Save("coin-flow-corner-" + tag + "1600x900.png");
        for (var i = 0; i < 30; i++) Call(hub, "UpdateCoinFlow", .1f);
        Check(!Get<RectTransform>(hub, "_flowWallet").gameObject.activeSelf, "the corner wallet fades once the coins land");
        Call(hub, "CloseSettingsWindow"); Call(hub, "UpdateSettingsWindow", 1f, false);
        Call(hub, "ShowCashWindow", 2); Call(hub, "UpdateCashWindow", 1f, false);

        // Сейф за спиной Тузяры — та же касса; «КРЕДИТ ТУЗЯРЫ ›» — к его столу.
        var places = (System.Array)typeof(PlayerHubController).GetField("CityPlaces", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null);
        Check(places.Cast<object>().Any(place => (string)place.GetType().GetField("Id").GetValue(place) == "bank-vault"), "the bank vault is a place in the bank hall");
        await ClickButton(cash.GetComponentsInChildren<Button>().Single(b => b.name == "КРЕДИТ ТУЗЯРЫ ›"), "to the bank desk");
        Call(hub, "UpdateCashWindow", 1f, false);
        Check(!Get<bool>(hub, "_cashShown"), "КРЕДИТ ТУЗЯРЫ closes the cash window");
        for (var i = 0; i < 900 && !Get<bool>(hub, "_bankLoanShown"); i++)
        {
            Call(hub, "PrepareFloors"); Call(hub, "ContinueMapTrip");
            Call(hub, "MoveCity", 1 / 30f); Call(hub, "MoveDive", 1 / 30f); Call(hub, "MoveFloorPan", 1 / 30f);
            if (i % 10 == 0) await NextFrame();
        }
        Check(Get<bool>(hub, "_bankLoanShown"), "the trip ends at the bank desk, opened");
        Save("cash-bank-desk-" + tag + "1600x900.png");
        hub.Back(); await NextFrame();
    }

    private static void FinishHudWhenReady()
    {
        if (operation == null || !operation.IsCompleted) return;
        EditorApplication.update -= FinishHudWhenReady; Application.logMessageReceived -= OnLog;
        if (operation.IsFaulted)
        {
            File.WriteAllText(Path.Combine(Output, "error.txt"), operation.Exception.ToString());
            Debug.LogException(operation.Exception); EditorApplication.Exit(1); return;
        }
        File.WriteAllText(Path.Combine(Output, "verification.txt"), checks + " assertions passed: city HUD on the street, "
            + "buttons, coins, on screen at 1600x900/2400x1080/1280x960, settings window over the city (switch, volume, fits the screen, closes), tasks window (claim, wallet, fits, closes), "
            + "collection window (tabs, locked item, equip, character picker, throw, fits, Back), "
            + "cash window (pluses, silver and gold packs, ad reward, exchange, no ads, fits, bank desk trip).");
        Debug.Log("HUD QA PASSED: " + checks); EditorApplication.Exit(0);
    }
}
