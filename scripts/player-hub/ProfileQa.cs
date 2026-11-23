// Companion to PlayerHubQa.cs. Run PlayerHubQa.RunProfile in the isolated QA project, with HUB_QA_OUTPUT=profile.
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CardGame.Core.Meta;
using CardGameClient;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public static partial class PlayerHubQa
{
    public static void RunProfile()
    {
        Directory.CreateDirectory(Output);
        var error = Path.Combine(Output, "error.txt");
        if (File.Exists(error)) File.Delete(error);
        Application.logMessageReceived += OnLog;
        if (PhoneUi) typeof(AppFlowController).Assembly.GetType("CardGameClient.MobileUi").GetField("TestPhone").SetValue(null, true);
        store = new MemoryStore(); clock = new FixedClock();
        PlayerHubRuntime.UseService(new LocalPlayerHubService(store, clock), clock);
        EditorApplication.update += FinishProfileWhenReady;
        EditorApplication.delayCall += () => operation = RunProfileChecks();
    }

    private static async Task RunProfileChecks()
    {
        Check((await PlayerHubRuntime.LoadAsync()).Success, "memory profile loads");
        Begin(1600, 900);
        // These checks render in edit mode, where EventSystem does not receive its runtime OnEnable.
        Call(events, "OnEnable");
        UnityEngine.EventSystems.EventSystem.current = events;
        var root = new GameObject("Profile QA", typeof(RectTransform));
        root.transform.SetParent(canvasObject.transform, false);
        ((RectTransform)root.transform).sizeDelta = new Vector2(1600, 900);
        hub = root.AddComponent<PlayerHubController>(); hub.enabled = false;
        hub.Configure(() => playCalls++, () => avatarCalls++, (game, count) => {});
        Call(hub, "Start");
        await WaitFor(() => Get<HubSnapshot>(hub, "_state") != null && !Get<bool>(hub, "_busy"), "hub loaded");
        hub.GoHome(); await NextFrame(); SettleOn(1);
        for (var i = 0; i < 60; i++) await NextFrame();
        Call(hub, "UpdateCityHud");
        var hud = Get<RectTransform>(hub, "_cityHud");
        await ClickButton(hud.GetComponentsInChildren<Button>().Single(b => b.name == "Профиль"), "city profile");
        Call(hub, "UpdateProfileWindow", 1f, false); Call(hub, "UpdateCityHud");
        Check(Get<string>(hub, "_page") == "home" && Get<bool>(hub, "_profileShown"), "profile opens over the current city");
        Check(!hud.gameObject.activeSelf, "HUD hides beneath the profile");
        var layer = Get<RectTransform>(hub, "_profileLayer");
        var field = Get<InputField>(hub, "_profileNameInput");
        var save = Get<Button>(hub, "_profileSave");
        Check(!save.interactable, "unchanged name cannot be saved");
        await ClickButton(layer.GetComponentsInChildren<Button>().Single(b => b.name == "СМЕНИТЬ АВАТАР"), "avatar picker");
        Check(avatarCalls == 1, "profile opens the existing avatar picker");

        foreach (var size in new[] { new Vector2Int(1600, 900), new Vector2Int(2400, 1080), new Vector2Int(1280, 960), new Vector2Int(960, 540) })
        {
            ResizeCanvas(size.x, size.y); ((RectTransform)hub.transform).sizeDelta = size; Call(hub, "Resize");
            await NextFrame(); await NextFrame(); Canvas.ForceUpdateCanvases();
            var view = new Vector3[4]; ((RectTransform)hub.transform).GetWorldCorners(view);
            var board = new Vector3[4]; Get<RectTransform>(hub, "_profileWindow").GetWorldCorners(board);
            Check(board[0].x >= view[0].x - 1 && board[2].x <= view[2].x + 1 && board[0].y >= view[0].y - 1 && board[2].y <= view[2].y + 1,
                "profile fits " + size);
            Save("profile-" + size.x + "x" + size.y + ".png");
        }
        ResizeCanvas(1600, 900); ((RectTransform)hub.transform).sizeDelta = new Vector2(1600, 900); Call(hub, "Resize");
        field.text = " "; Call(hub, "PaintProfileWindow"); Check(!save.interactable, "blank name is disabled");
        field.text = "Я"; Call(hub, "PaintProfileWindow"); Check(!save.interactable, "one-letter name is disabled");
        field.text = "  Картёжник  "; Call(hub, "PaintProfileWindow");
        await ClickButton(save, "save name"); await WaitFor(() => !Get<bool>(hub, "_busy"), "name saved");
        Check(Get<HubSnapshot>(hub, "_state").Profile.DisplayName == "Картёжник" && field.text == "Картёжник", "trimmed name persists and field updates");
        Check(Get<Text>(hub, "_profileName").text == "Картёжник", "portrait name updates");
        Check(Get<bool>(hub, "_profileShown"), "saving does not close profile");

        field.text = "Несохранённое имя";
        Call(hub, "PaintProfileWindow");
        Check(field.text == "Несохранённое имя", "render leaves an unsaved draft intact");
        store.FailNextSave = true;
        await ClickButton(save, "failed name save"); await WaitFor(() => !Get<bool>(hub, "_busy"), "failed save completed");
        Check(Get<HubSnapshot>(hub, "_state").Profile.DisplayName == "Картёжник" && field.text == "Несохранённое имя", "failed save preserves old profile and draft");

        field.text = "ШирокоплечийКарточныйМаг";
        Call(hub, "PaintProfileWindow"); await ClickButton(save, "long name");
        await WaitFor(() => !Get<bool>(hub, "_busy"), "long name saved");
        var state = Get<HubSnapshot>(hub, "_state");
        state.Profile.XP = 1257; state.Profile.Level = 13; state.Profile.Matches = 128; state.Profile.Wins = 91; state.Profile.Losses = 37;
        state.Profile.Coins = 123456789; state.Profile.Gold = 1234567; state.Bank.Debt = 4200;
        Call(hub, "PaintProfileWindow");
        Check(Get<Text>(hub, "_profileWinRate").text.StartsWith("71%"), "win rate uses actual matches");
        Check(Get<Image>(hub, "_profileXpFill").rectTransform.sizeDelta.x > 140, "experience bar reflects progress within level");
        Save("profile-populated-1600x900.png");

        await ClickButton(layer.GetComponentsInChildren<Button>().Single(b => b.name == "КОЛЛЕКЦИЯ"), "collection from profile");
        Call(hub, "UpdateCollectionWindow", 1f, false);
        Check(!layer.gameObject.activeSelf && Get<bool>(hub, "_collectionShown"), "collection replaces profile without stacked boards");
        hub.Back(); Call(hub, "UpdateCollectionWindow", 1f, false);
        hub.Navigate("profile"); Call(hub, "UpdateProfileWindow", 1f, false);
        await ClickButton(layer.GetComponentsInChildren<Button>().Single(b => b.name == "НАСТРОЙКИ"), "settings from profile");
        Call(hub, "UpdateSettingsWindow", 1f, false);
        Check(!layer.gameObject.activeSelf && Get<bool>(hub, "_settingsShown"), "settings replaces profile");
        hub.Back(); Call(hub, "UpdateSettingsWindow", 1f, false);

        foreach (var link in new[] { ("ОТКРЫТЬ", "achievements"), ("В БАНК", "bank"), ("Profile stat РЕЙТИНГ", "ranks") })
        {
            hub.GoHome(); hub.Navigate("profile"); Call(hub, "UpdateProfileWindow", 1f, false);
            await ClickButton(layer.GetComponentsInChildren<Button>().Single(b => b.name == link.Item1), link.Item2);
            Check(Get<string>(hub, "_page") == link.Item2 && !layer.gameObject.activeSelf, "profile link opens " + link.Item2);
        }
        hub.Navigate("progress"); hub.Navigate("profile"); Call(hub, "UpdateProfileWindow", 1f, false);
        Check(Get<string>(hub, "_page") == "progress", "profile also overlays a section page");
        hub.Back(); Call(hub, "UpdateProfileWindow", 1f, false);
        Check(Get<string>(hub, "_page") == "progress" && !layer.gameObject.activeSelf, "Back returns to the underlying section");
        hub.GoHome(); hub.Navigate("profile"); Call(hub, "UpdateProfileWindow", 1f, false);
        await ClickButton(layer.GetComponentsInChildren<Button>().Single(b => b.name == "ЗАКРЫТЬ"), "close profile");
        Call(hub, "UpdateProfileWindow", 1f, false); Call(hub, "UpdateCityHud");
        Check(!layer.gameObject.activeSelf && hud.gameObject.activeSelf, "close restores city HUD");
        var keyboard = InputSystem.AddDevice<Keyboard>("QA profile keyboard");
        try
        {
            keyboard.MakeCurrent();
            hub.Navigate("profile"); Call(hub, "UpdateProfileWindow", 1f, false);
            events.SetSelectedGameObject(field.gameObject); field.ActivateInputField(); await NextFrame();
            field.text = "Черновик";
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape)); InputSystem.Update();
            keyboard.MakeCurrent();
            UnityEngine.EventSystems.EventSystem.current = events;
            Call(hub, "UpdateProfileWindow", .1f, true);
            Check(Get<bool>(hub, "_profileShown") && field.text == Get<HubSnapshot>(hub, "_state").Profile.DisplayName,
                "first Escape cancels editing (shown=" + Get<bool>(hub, "_profileShown") + ", text=" + field.text + ", key=" + keyboard.escapeKey.isPressed + ")");
            Check(events.currentSelectedGameObject == null, "Escape releases keyboard focus (current="
                + UnityEngine.EventSystems.EventSystem.current?.name + ", selected=" + events.currentSelectedGameObject?.name
                + ", key=" + keyboard.escapeKey.isPressed + ", held=" + Get<bool>(hub, "_profileEscapeHeld") + ")");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update(); Call(hub, "UpdateProfileWindow", .1f, false);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape)); InputSystem.Update(); keyboard.MakeCurrent(); Call(hub, "UpdateProfileWindow", 1f, false);
            Check(!layer.gameObject.activeSelf && Get<string>(hub, "_page") == "home", "second Escape closes only the profile");
        }
        finally { InputSystem.RemoveDevice(keyboard); }
        Check(RuntimeErrors.Count == 0, "no runtime errors: " + string.Join(" | ", RuntimeErrors));
    }

    private static void FinishProfileWhenReady()
    {
        if (operation == null || !operation.IsCompleted) return;
        EditorApplication.update -= FinishProfileWhenReady; Application.logMessageReceived -= OnLog;
        if (operation.IsFaulted)
        {
            File.WriteAllText(Path.Combine(Output, "error.txt"), operation.Exception.ToString());
            Debug.LogException(operation.Exception); EditorApplication.Exit(1); return;
        }
        File.WriteAllText(Path.Combine(Output, "verification.txt"), checks + " assertions passed: profile window, 4 aspect/size variants, "
            + "name save/validation/failure/draft, avatar picker, statistics, collection/settings/navigation, close and Back. Memory-only profile.");
        Debug.Log("PROFILE QA PASSED: " + checks); EditorApplication.Exit(0);
    }
}
