// Companion partial for PlayerHubQa.cs: the Queen of Diamonds' shop (2026-10-17). Copy with the other player-hub QA files
// to Assets/Scripts/Editor and run -executeMethod PlayerHubQa.RunShop (HUB_QA_OUTPUT selects the screens subfolder).
// Own entry point and report, so PlayerHubQa.cs itself stays untouched. Memory-only profile and a fixed clock.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CardGame.Core.Meta;
using CardGameClient;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

public static partial class PlayerHubQa
{
    private const int ShopFloor = 7;

    public static void RunShop()
    {
        Directory.CreateDirectory(Output);
        var previousError = Path.Combine(Output, "error.txt");
        if (File.Exists(previousError)) File.Delete(previousError);
        Application.logMessageReceived += OnLog;
        store = new MemoryStore(); clock = new FixedClock();
        PlayerHubRuntime.UseService(new LocalPlayerHubService(store, clock), clock);
        EditorApplication.update += FinishShopWhenReady;
        EditorApplication.delayCall += () => operation = RunShopChecks();
    }

    private static async Task RunShopChecks()
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
        await CheckShop();
        Check(RuntimeErrors.Count == 0, "no shop runtime exceptions: " + string.Join(" | ", RuntimeErrors));
    }

    private static HubSnapshot ShopState => Get<HubSnapshot>(hub, "_state");

    private static async Task CheckShop()
    {
        hub.GoHome(); await NextFrame(); SettleOn(1);
        var area = Get<RectTransform>(hub, "_cityArea");
        var places = ((Array)typeof(PlayerHubController).GetField("CityPlaces", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null))
            .Cast<object>().ToArray();
        object PlaceById(string id) => places.Single(p => (string)Item(p, "Id") == id);
        int IndexOf(string id) => Array.FindIndex(places, p => (string)Item(p, "Id") == id);
        Vector2 ScreenOf(object place) => RectTransformUtility.WorldToScreenPoint(camera,
            area.TransformPoint(area.rect.center + (Vector2)Call(hub, "PlaceScreenPoint", place, Item(place, "Door"))));
        // Кнопка карты строится лениво из UpdateMap — здесь её строим сами, чтобы проверять настоящую.
        void Frame() { Call(hub, "BuildMapButton"); Call(hub, "UpdateInteriorControls"); Call(hub, "RefreshMapButton"); Call(hub, "UpdateShop", .1f, false); }

        // С площади — нырок в дверь, как в Клуб, банк и таверну.
        var shop = PlaceById("shop");
        Check((int)Item(shop, "Floor") == ShopFloor, "the shop on the square is a door into its own floor");
        TapScreen(area, ScreenOf(shop), "shop door");
        Check(Get<int>(hub, "_diveTarget") == ShopFloor && Get<int>(hub, "_visitPhase") == 0, "a tap on the shop dives through its door");
        var frames = 0;
        for (; Get<int>(hub, "_diveTarget") >= 0 && frames < 200; frames++)
        {
            Call(hub, "MoveDive", 1 / 60f);
            if (frames == 20) Save("shop-dive.png");
        }
        Check(Get<int>(hub, "_cityLevel") == ShopFloor && Get<string>(hub, "_page") == "home" && frames / 60f < 1.5f,
            "the dive lands inside the shop (" + frames + " frames)");
        Frame();
        var exit = Get<RectTransform>(hub, "_interiorControls");
        Check(exit != null && exit.gameObject.activeSelf, "inside the shop there is the usual leave button");
        Check(Get<RectTransform>(hub, "_shopControls").gameObject.activeSelf, "the wallet and the ask-for-other button are over the hall");
        var floors = Get<RectTransform[]>(hub, "_floors");
        Check(floors[ShopFloor] != null && floors[ShopFloor].gameObject.activeSelf && !Get<RectTransform>(hub, "_cityStrip").gameObject.activeSelf,
            "the street is replaced by the shop floor");

        // Витрина на картинке этажа: пять мест, у каждого вещь и ценник; товар дня — редкий, со скидкой.
        var state = ShopState;
        var slots = Get<RectTransform[]>(hub, "_shopSlots");
        Check(state.Shop.Slots.Count == HubShop.SlotCount && slots.All(s => s != null && s.IsChildOf(floors[ShopFloor])),
            "five showcase places live on the floor picture");
        for (var i = 0; i < slots.Length; i++)
        {
            var item = state.Catalog.Single(entry => entry.Id == state.Shop.Slots[i]);
            var texts = slots[i].GetComponentsInChildren<Text>().Select(t => t.text).ToArray();
            Check(texts.Contains(item.Name) && texts.Any(t => t.StartsWith(HubShop.PriceAt(item, i).ToString(), StringComparison.Ordinal)),
                "place " + i + " shows its item and price: " + item.Name);
        }
        Check(state.Catalog.Single(entry => entry.Id == state.Shop.Slots[0]).Rarity == HubShop.Rare, "the podium holds a rare item of the day");
        Check(Get<Text>(hub, "_shopBoardText").text.StartsWith("НОВЫЙ\nТОВАР\nЧЕРЕЗ\n", StringComparison.Ordinal), "the blackboard counts down to midnight");
        var bubble = Get<Text>(hub, "_shopBubbleText");
        Check(bubble.gameObject.activeInHierarchy && bubble.text.Contains(state.Catalog.Single(entry => entry.Id == state.Shop.Slots[0]).Name),
            "the Queen greets with today's podium item");
        InspectLayout("shop");
        Save("shop-counter-1600x900.png");
        Call(hub, "ShowPlaceOutline", IndexOf("shop-slot-1"), 1f);
        Check(Get<HubPlaceOutline>(hub, "_placeOutline").gameObject.activeInHierarchy, "a showcase place is outlined like other places");
        Save("shop-hover.png");
        Call(hub, "ShowPlaceOutline", -1, 1f);

        // Дама отвечает на нажатие прямо в зале.
        var greeting = bubble.text;
        TapScreen(area, ScreenOf(PlaceById("shop-queen")), "Queen of Diamonds");
        Check(bubble.text != greeting, "a tap on the Queen gets a new line");

        // Вещь, на которую хватает: карточка, покупка, «Продано», «Надеть».
        var coins = state.Profile.Coins;
        var affordable = Enumerable.Range(1, HubShop.SlotCount - 1)
            .First(i => state.Catalog.Single(entry => entry.Id == state.Shop.Slots[i]).Price <= coins);
        var bought = state.Catalog.Single(entry => entry.Id == state.Shop.Slots[affordable]);
        TapScreen(area, ScreenOf(PlaceById("shop-slot-" + affordable)), "showcase place " + affordable);
        Frame();
        Check(Get<RectTransform>(hub, "_shopDetail").gameObject.activeInHierarchy && Get<Text>(hub, "_shopDetailName").text == bought.Name,
            "a tap on a place opens the item card");
        Check(!exit.gameObject.activeSelf && !Get<RectTransform>(hub, "_mapButtonLayer").gameObject.activeSelf,
            "leave and map are hidden behind the card");
        var action = Get<Button>(hub, "_shopDetailAction");
        Check(ButtonLabel(action) == "КУПИТЬ ЗА " + bought.Price, "the card offers to buy at the shelf price");
        Save("shop-item-card.png");
        await ClickButton(action, "buy");
        state = ShopState;
        Check(state.Profile.Coins == coins - bought.Price && state.Inventory.Any(i => i.ItemId == bought.Id), "buying charges the price and adds the item");
        Check(ButtonLabel(action) == "НАДЕТЬ" && bubble.text.StartsWith("Носи", StringComparison.Ordinal),
            "after buying the card offers to wear it and the Queen thanks");
        Check(slots[affordable].GetComponentsInChildren<Text>().Any(t => t.text == "ПРОДАНО"), "the bought item stays on the shelf marked sold");
        await ClickButton(action, "wear");
        Check(ShopState.Equipped.Any(e => e.ItemId == bought.Id) && ButtonLabel(Get<Button>(hub, "_shopDetailAction")) == "НАДЕТО",
            "wearing equips it from the card");
        Check(bubble.text == "Тебе идёт!" && !Get<Image>(hub, "_toastPill").gameObject.activeSelf,
            "the Queen answers the purchase and the fitting herself; no top notice hides under her bubble");
        Save("shop-item-bought.png");

        // Esc закрывает только карточку: удержание не выводит из Лавки.
        var keyboard = InputSystem.AddDevice<Keyboard>("Shop QA keyboard");
        try
        {
            keyboard.MakeCurrent(); InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape)); InputSystem.Update();
            Call(hub, "UpdateCity", false); Call(hub, "UpdateShop", .1f, false); Call(hub, "UpdateCity", false); Call(hub, "UpdateShop", .1f, false);
            Check(!Get<RectTransform>(hub, "_shopDetail").gameObject.activeSelf && Get<int>(hub, "_cityLevel") == ShopFloor
                  && Get<int>(hub, "_diveTarget") < 0, "held Escape closes only the card");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update(); Call(hub, "UpdateCity", false);
        }
        finally { InputSystem.RemoveDevice(keyboard); }
        Frame();
        Check(exit.gameObject.activeSelf && Get<RectTransform>(hub, "_mapButtonLayer").gameObject.activeSelf, "leave and map return after the card closes");

        // Товар дня дороже, чем осталось: отказ, ничего не списано; «Назад» закрывает карточку, а не выводит.
        var day = ShopState.Catalog.Single(entry => entry.Id == ShopState.Shop.Slots[0]);
        coins = ShopState.Profile.Coins;
        TapScreen(area, ScreenOf(PlaceById("shop-slot-0")), "podium");
        if (HubShop.PriceAt(day, 0) > coins)
        {
            await ClickButton(Get<Button>(hub, "_shopDetailAction"), "buy too expensive");
            Check(ShopState.Profile.Coins == coins && !ShopState.Inventory.Any(i => i.ItemId == day.Id), "not enough silver changes nothing");
            Check(bubble.text.StartsWith("Не хватает серебра", StringComparison.Ordinal) && !Get<Image>(hub, "_toastPill").gameObject.activeSelf,
                "the Queen herself says there is not enough silver");
        }
        hub.Back(); Frame();
        Check(!Get<RectTransform>(hub, "_shopDetail").gameObject.activeSelf && Get<int>(hub, "_cityLevel") == ShopFloor && Get<int>(hub, "_diveTarget") < 0,
            "Back closes the card first and stays in the shop");

        // «Покажи другое»: новая полка, остаток на кнопке, после двух раз кнопки нет.
        var before = ShopState.Shop.Slots.ToArray();
        var reroll = Get<Button>(hub, "_shopReroll");
        Check(ButtonLabel(reroll) == "ПОКАЖИ ДРУГОЕ · 2", "the ask-for-other button shows two tries");
        await ClickButton(reroll, "show other");
        Check(!ShopState.Shop.Slots.SequenceEqual(before) && !ShopState.Shop.Slots.Contains(bought.Id), "the shelf changes and has no owned item");
        var shelfName = ShopState.Catalog.Single(e => e.Id == ShopState.Shop.Slots[1]).Name;
        Check(ButtonLabel(reroll) == "ПОКАЖИ ДРУГОЕ · 1" && slots[1].GetComponentsInChildren<Text>().Any(t => t.text == shelfName),
            "one try left and the shelf is repainted");
        await ClickButton(reroll, "show other again");
        Check(!reroll.gameObject.activeInHierarchy, "no more tries today");

        // Полночь у прилавка: ядро собирает новую витрину, попытки снова есть, вчерашняя реплика уходит.
        var today = ShopState.Shop.Day;
        clock.Now = HubShop.NextShowcaseAt(clock.Now);
        Call(hub, "UpdateShop", .1f, false);
        await WaitFor(() => ShopState.Shop.Day == today + 1, "midnight showcase");
        Call(hub, "UpdateShop", .1f, false);
        Check(reroll.gameObject.activeInHierarchy && ShopState.Shop.Rerolls == 0, "a new day brings a new showcase and new tries");
        Check(bubble.text.StartsWith("Сегодня на подиуме", StringComparison.Ordinal), "yesterday's line is dropped for the new podium item");

        // Широкий и маленький экраны: зал закрывает экран, кнопки на месте.
        foreach (var size in new[] { new Vector2Int(2000, 900), new Vector2Int(1280, 720) })
        {
            ResizeCanvas(size.x, size.y); ((RectTransform)hub.transform).sizeDelta = size; Call(hub, "Resize"); Frame();
            Canvas.ForceUpdateCanvases();
            var view = new Vector3[4]; ((RectTransform)hub.transform).GetWorldCorners(view);
            var art = new Vector3[4]; floors[ShopFloor].GetWorldCorners(art);
            Check(art[0].x <= view[0].x + 1 && art[0].y <= view[0].y + 1 && art[2].x >= view[2].x - 1 && art[2].y >= view[2].y - 1,
                "the shop covers " + size.x + "x" + size.y);
            InspectLayout("shop-" + size.x + "x" + size.y);
            Save("shop-counter-" + size.x + "x" + size.y + ".png");
        }
        ResizeCanvas(1600, 900); ((RectTransform)hub.transform).sizeDelta = new Vector2(1600, 900); Call(hub, "Resize"); Frame();

        // «ВЫЙТИ» — нырок обратно на площадь к двери Лавки.
        await ClickButton(exit.GetComponentsInChildren<Button>().Single(b => ButtonLabel(b) == "ВЫЙТИ"), "leave the shop");
        Check(Get<int>(hub, "_diveTarget") == 0, "leave dives back to the street");
        Call(hub, "MoveDive", 10f); Frame();
        Check(Get<int>(hub, "_cityLevel") == 0 && !Get<RectTransform>(hub, "_shopControls").gameObject.activeSelf, "back on the square without shop controls");

        // Вкладка «Магазин» с другой страницы — поездка в Лавку и нырок.
        hub.Navigate("inventory"); await NextFrame();
        hub.Navigate("shop");
        for (var step = 0; step < 40 && Get<int>(hub, "_cityLevel") != ShopFloor; step++)
        {
            Call(hub, "ContinueMapTrip"); Call(hub, "MoveCity", .5f); Call(hub, "MoveDive", .5f);
        }
        Frame();
        Check(Get<int>(hub, "_cityLevel") == ShopFloor && Get<string>(hub, "_page") == "home", "the Shop tab takes the player inside the shop");
        DiveNow(0);

        // Помидоры, пока нет чёрного рынка, докупаются в тренировке броска.
        var tomatoes = ShopState.Inventory.Where(i => i.ItemId == "tomato_pack").Sum(i => i.Quantity);
        hub.Navigate("throw:tomato_pack"); await NextFrame();
        await Click("Ещё 10 помидоров");
        Check(ShopState.Inventory.Where(i => i.ItemId == "tomato_pack").Sum(i => i.Quantity) == tomatoes + 10, "ten more tomatoes in the throw practice");
    }

    private static void FinishShopWhenReady()
    {
        if (operation == null || !operation.IsCompleted) return;
        EditorApplication.update -= FinishShopWhenReady; Application.logMessageReceived -= OnLog;
        File.WriteAllLines(Path.Combine(Output, "layout-issues.txt"), LayoutIssues);
        if (operation.IsFaulted)
        {
            File.WriteAllText(Path.Combine(Output, "error.txt"), operation.Exception.ToString());
            Debug.LogException(operation.Exception); EditorApplication.Exit(1); return;
        }
        File.WriteAllText(Path.Combine(Output, "verification.txt"), checks + " assertions passed: the shop door on the square dives into "
            + "the shop floor like the Club and the bank, leave button and street hidden inside, five showcase places on the floor picture with "
            + "names and prices, rare item of the day with discount plaque, midnight countdown, Queen greeting and tap, outline on hover, item card "
            + "from a tap in the hall, leave and map hidden behind it, purchase with exact charge, sold tag, wear, held Escape and Back close only "
            + "the card, refused purchase without enough silver, two rerolls a day, new showcase at midnight, coverage and layout at "
            + "1600x900/2000x900/1280x720, leave dives back to the square, Shop tab travels inside, tomato top-up in throw practice. "
            + "Layout issues: " + LayoutIssues.Count + ". Memory-only profile; no real save opened or written.");
        Debug.Log("SHOP QA PASSED: " + checks); EditorApplication.Exit(0);
    }
}
