// Companion partial for PlayerHubQa.cs. PLAYER_HUB_QA_SCOPE=quests; memory-only profile.
using System;
using System.Linq;
using System.Threading.Tasks;
using CardGame.Core.Meta;
using CardGameClient;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

public static partial class PlayerHubQa
{
    private static async Task CheckSixQuests()
    {
        hub.GoHome(); await NextFrame(); DiveNow(0); DiveNow(3);
        var refreshed = await PlayerHubRuntime.ExecuteAsync(new HubCommand { Action = "quests_debug_refresh" });
        Check(refreshed.Success, "test refresh succeeds");
        Check(ReferenceEquals(PlayerHubRuntime.Clock, clock), "quest service and UI share the injected fixed clock");
        var refreshedWeekly = refreshed.Snapshot.Quests.Where(q => q.Period == "weekly").ToArray();
        Check(refreshedWeekly.Length == 2 && refreshedWeekly.All(q => q.IssuedAt == clock.Now
            && q.ExpiresAt == clock.Now + HubQuest.WeeklySeconds), "both new weekly notes receive seven full days from issue");
        Call(hub, "RefreshSixQuestBadge"); Call(hub, "UpdateSixGift", 0f, false);
        Check(Get<Text>(hub, "_sixQuestBadgeText").text == "5", "room badge includes the five unseen notes");
        var gift = (bool)Call(hub, "GiftReady") ? 1 : 0;
        Check(Get<Text>(hub, "_giftStreetCount").text == (5 + gift).ToString(), "street badge combines unseen notes with the gift once");
        var area = Get<RectTransform>(hub, "_cityArea");
        var place = ((Array)typeof(PlayerHubController).GetField("CityPlaces", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null))
            .Cast<object>().Single(p => (string)Item(p, "Id") == "six-quests");
        var point = (Vector2)Call(hub, "PlaceScreenPoint", place, Item(place, "Door"));
        TapScreen(area, RectTransformUtility.WorldToScreenPoint(camera, area.TransformPoint(area.rect.center + point)), "Six quest board");
        await WaitFor(() => Get<HubSnapshot>(hub, "_state").Quests.All(q => q.Seen), "opening marks notes seen");
        Call(hub, "UpdateSixQuests", .3f, false);
        var layer = Get<RectTransform>(hub, "_questsLayer");
        var notes = ((Array)typeof(PlayerHubController).GetField("_questNotes", Fields).GetValue(hub)).Cast<object>().ToArray();
        Check(notes.Length == 5 && notes.Count(n => !(bool)Item(n, "Weekly")) == 3, "board has three daily and two weekly notes");
        var weeklyTitles = layer.GetComponentsInChildren<Text>(true).Where(t => t.name == "Weekly title").ToArray();
        Check(weeklyTitles.Length == 1 && weeklyTitles[0].text == "НЕДЕЛЬНЫЕ ПОРУЧЕНИЯ", "weekly row has one clear title");
        Check(!layer.GetComponentsInChildren<Transform>(true).Any(t => t.name == "Weekly deadline"), "weekly row has no shared countdown subtitle");
        Check(notes.Where(n => (bool)Item(n, "Weekly")).All(n => ((Text)Item(n, "Timer")).text == "Осталось 7 д 0 ч"),
            "weekly notes display seven days from the same fixed clock");
        Check(Get<bool>(hub, "_questsShown") && Get<string>(hub, "_page") == "home" && Get<int>(hub, "_cityLevel") == 3,
            "board is a close-up over the room");
        Check(notes.All(n => ((GameObject)Item(n, "New")).activeSelf), "first opening keeps the red NEW tags visible");
        Check(!Get<RectTransform>(hub, "_sixQuestBadge").gameObject.activeSelf, "viewed incomplete notes no longer light the room badge");
        Check(Get<Text>(hub, "_giftStreetCount").text == gift.ToString(), "viewing updates the street count");
        Call(hub, "UpdateInteriorControls"); Call(hub, "RefreshMapButton");
        Check(!ActiveButtons().Any(b => ButtonLabel(b).Contains("ВЫЙТИ")), "leave is hidden behind the board");
        var mapButton = Get<RectTransform>(hub, "_mapButtonLayer");
        Check(mapButton == null || !mapButton.gameObject.activeSelf, "map is hidden behind the board");
        InspectQuestNotes(layer, notes);
        Save("hub-six-quests-new.png");
        CheckAllQuestText(notes);
        var completed = await PlayerHubRuntime.ExecuteAsync(new HubCommand { Action = "quests_debug_complete" });
        Check(completed.Success, "debug complete succeeds");
        Call(hub, "UpdateSixQuests", .01f, false); Call(hub, "RefreshSixQuestBadge"); Call(hub, "UpdateGiftMarks", gift > 0);
        Check(Get<Text>(hub, "_sixQuestBadgeText").text == "3" && Get<Text>(hub, "_giftStreetCount").text == (3 + gift).ToString(), "badges count ready notes once");
        var before = completed.Snapshot.Profile.Coins; var xp = completed.Snapshot.Profile.XP;
        await ClickButton((Button)Item(notes[0], "Claim"), "claim easy note");
        Check(Get<HubSnapshot>(hub, "_state").Profile.Coins == before + HubQuest.EasyCoins
            && Get<HubSnapshot>(hub, "_state").Profile.XP == xp + HubQuest.EasyXP, "claim credits the exact coins and XP");
        Call(hub, "UpdateSixQuests", .20f, false);
        Check(((RectTransform)Item(notes[0], "Stamp")).localScale.x > 1, "completion stamp falls from larger scale");
        var coins = Get<RectTransform[]>(hub, "_questCoins");
        Check(coins.Any(c => c.gameObject.activeSelf), "coins fly from the note");
        var noteAt = (Vector2)Get<RectTransform>(hub, "_questsLayer").InverseTransformPoint(((RectTransform)Item(notes[0], "Rect")).position)
            - layer.rect.center;
        Check(Vector2.Distance(Get<Vector2>(hub, "_questsClaimOrigin"), noteAt) < 10, "coin flight starts on the actual note, not the corner of the stage");
        Save("hub-six-quests-claim.png");
        Call(hub, "UpdateSixQuests", 1.5f, false); Call(hub, "UpdateSixQuests", .01f, false);
        Check(Get<Text>(hub, "_questsWalletText").text == (before + HubQuest.EasyCoins).ToString("N0").Replace(',', ' ').Replace('\u00a0', ' ')
            || Get<Text>(hub, "_questsWalletText").text.Replace(" ", "").Replace("\u00a0", "") == (before + HubQuest.EasyCoins).ToString(), "wallet finishes at the authoritative balance");
        Check(((RectTransform)Item(notes[0], "Stamp")).gameObject.activeSelf && !((Button)Item(notes[0], "Claim")).gameObject.activeSelf, "claimed note shows a stamp and cannot be claimed again");
        var timer = ((Text)Item(notes[0], "Timer")).text;
        clock.Now += 61; Call(hub, "UpdateSixQuests", .01f, false);
        Check(((Text)Item(notes[0], "Timer")).text != timer, "note countdown follows the service clock");
        ResizeCanvas(2000, 900); ((RectTransform)hub.transform).sizeDelta = new Vector2(2000, 900); Call(hub, "Resize");
        InspectQuestNotes(layer, notes); Save("hub-six-quests-2000x900.png");
        var oldId = ((HubQuest)Item(notes[0], "Model")).Id;
        var waitingId = ((HubQuest)Item(notes[1], "Model")).Id;
        clock.Now = ((HubQuest)Item(notes[0], "Model")).ExpiresAt;
        await PlayerHubRuntime.RefreshAsync();
        Call(hub, "UpdateSixQuests", .12f, false);
        Check(((RectTransform)Item(notes[0], "Rect")).anchoredPosition.y < ((Vector2)Item(notes[0], "Position")).y, "expired claimed note flies down");
        Save("hub-six-quests-refresh.png");
        Call(hub, "UpdateSixQuests", .6f, false);
        Check(((HubQuest)Item(notes[0], "Model")).Id != oldId && ((HubQuest)Item(notes[1], "Model")).Id == waitingId, "new note replaces expired claim while unclaimed reward remains");
        await ClickButton((Button)Item(notes[1], "Claim"), "claim overdue medium note");
        Call(hub, "UpdateSixQuests", 1.5f, false); Call(hub, "UpdateSixQuests", .01f, false); Call(hub, "UpdateSixQuests", .6f, false);
        Check(((HubQuest)Item(notes[1], "Model")).Id != waitingId, "overdue claim immediately issues a new note after its reward animation");
        var keyboard = InputSystem.AddDevice<Keyboard>("Quest QA keyboard");
        try
        {
            keyboard.MakeCurrent(); InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape)); InputSystem.Update();
            Call(hub, "UpdateCity", false); Call(hub, "UpdateSixQuests", .3f, false); Call(hub, "UpdateCity", false);
            Check(!Get<bool>(hub, "_questsShown") && Get<int>(hub, "_cityLevel") == 3 && Get<int>(hub, "_diveTarget") < 0, "held Escape closes only the board");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update(); Call(hub, "UpdateCity", false);
        }
        finally { InputSystem.RemoveDevice(keyboard); }
        Call(hub, "ShowSixQuests"); await NextFrame(); Call(hub, "UpdateSixQuests", .3f, false);
        await ClickButton(layer.GetComponentsInChildren<Button>().Single(b => ButtonLabel(b) == "НАЗАД"), "board Back");
        Call(hub, "UpdateSixQuests", .3f, false);
        Check(!Get<bool>(hub, "_questsShown") && Get<int>(hub, "_cityLevel") == 3 && Get<int>(hub, "_diveTarget") < 0, "Back closes only the board");
        ResizeCanvas(1600, 900); ((RectTransform)hub.transform).sizeDelta = new Vector2(1600, 900); Call(hub, "Resize");
        Call(hub, "UpdateInteriorControls"); Call(hub, "RefreshMapButton");
        Check(Get<RectTransform>(hub, "_mapButtonLayer").gameObject.activeSelf, "map returns after closing the board");
        DiveNow(0);
        await CheckQuestResultLine();
    }

    private static async Task CheckQuestResultLine()
    {
        var parent = new GameObject("Quest result hint fixture", typeof(RectTransform), typeof(Text));
        parent.transform.SetParent(canvasObject.transform, false);
        var text = parent.GetComponent<Text>(); text.font = Get<Font>(hub, "_font");
        var show = false;
        HubQuestResultLine.Attach(text, () => show);
        var line = parent.GetComponentInChildren<HubQuestResultLine>();
        var label = line.GetComponent<Text>();
        // Edit-mode QA invokes the runtime lifecycle explicitly, like Start on the hub.
        Call(line, "OnEnable");
        Call(line, "Update"); Check(!label.enabled, "quest result hint stays hidden during a match");
        show = true; Call(line, "Update"); Check(label.enabled, "quest hint appears on the result");
        var refreshed = await PlayerHubRuntime.ExecuteAsync(new HubCommand { Action = "quests_debug_refresh" });
        Check(label.text == HubQuests.ResultHint(refreshed.Snapshot) && label.text.Contains("0 / "), "result hint follows an asynchronous snapshot change");
        await PlayerHubRuntime.ExecuteAsync(new HubCommand { Action = "quests_debug_complete" });
        Check(label.text == "Поручение выполнено — забери у Шестёрки", "completion hint updates after the service commits");
        show = false; Call(line, "Update"); Check(!label.enabled, "new match hides the previous result hint");
        Call(line, "OnDisable");
        UnityEngine.Object.DestroyImmediate(parent);
    }

    private static void InspectQuestNotes(RectTransform layer, object[] notes)
    {
        Canvas.ForceUpdateCanvases();
        var canvas = (RectTransform)canvasObject.transform;
        var corners = new Vector3[4];
        var weeklyTitle = layer.GetComponentsInChildren<Text>().Single(t => t.name == "Weekly title");
        Check(weeklyTitle.canvasRenderer.GetMesh().vertexCount > 0, "weekly title has visible glyphs");
        foreach (var rect in notes.Select(n => (RectTransform)Item(n, "Rect")).Concat(new[] { Get<RectTransform>(hub, "_questsWallet"), Get<RectTransform>(hub, "_questsBoard"), weeklyTitle.rectTransform }))
        {
            rect.GetWorldCorners(corners);
            Check(corners.All(c => canvas.rect.Contains(canvas.InverseTransformPoint(c))), "quest content fits viewport: " + rect.name);
        }
        foreach (var note in notes)
        {
            var title = (Text)Item(note, "Title"); var progress = (Text)Item(note, "Progress");
            Check(!string.IsNullOrEmpty(title.text) && title.canvasRenderer.GetMesh().vertexCount > 0, "task title has visible glyphs");
            Check(progress.text.Contains(" / ") && progress.canvasRenderer.GetMesh().vertexCount > 0, "x / y progress is rendered");
        }
    }

    private static void CheckAllQuestText(object[] notes)
    {
        var saved = Get<HubSnapshot>(hub, "_state");
        var sample = JsonUtility.FromJson<HubSnapshot>(JsonUtility.ToJson(saved));
        typeof(PlayerHubController).GetField("_state", Fields).SetValue(hub, sample);
        foreach (var template in HubQuests.Templates)
        {
            var index = template.Tier == "easy" ? 0 : template.Tier == "medium" ? 1 : template.Tier == "hard" ? 2 : 3;
            var q = sample.Quests.Single(item => item.Slot == HubQuests.Slots[index]);
            q.Name = template.Name; q.Goal = template.Goal; q.Progress = q.Goal - 1;
            Call(hub, "BindQuestNote", notes[index], q); Call(hub, "PaintSixQuests", 0f);
            Canvas.ForceUpdateCanvases();
            foreach (var field in new[] { "Title", "Progress" })
            {
                var label = (Text)Item(notes[index], field); var mesh = label.canvasRenderer.GetMesh(); var bounds = mesh.bounds;
                var rect = label.rectTransform.rect;
                Check(mesh.vertexCount > 0 && bounds.min.x >= rect.xMin - 1 && bounds.max.x <= rect.xMax + 1
                    && bounds.min.y >= rect.yMin - 1 && bounds.max.y <= rect.yMax + 1,
                    "all template text fits its note: " + template.Id + " " + field);
                if (field == "Progress")
                {
                    var paper = (RectTransform)Item(notes[index], "Rect");
                    var x = label.rectTransform.anchoredPosition.x;
                    Check(x + bounds.min.x >= -paper.rect.width / 2 + 26 && x + bounds.max.x <= paper.rect.width / 2 - 26,
                        "counter stays inside the uneven paper edge: " + template.Id);
                }
            }
            if (template.Id == "week-coins")
            {
                foreach (var note in sample.Quests) note.Progress = 0;
                Call(hub, "PaintSixQuests", 0f);
                Save("hub-six-quests-currency.png");
            }
        }
        Save("hub-six-quests-text-coverage.png");
        typeof(PlayerHubController).GetField("_state", Fields).SetValue(hub, saved);
        for (var i = 0; i < notes.Length; i++) Call(hub, "BindQuestNote", notes[i], saved.Quests.Single(q => q.Slot == HubQuests.Slots[i]));
        Call(hub, "PaintSixQuests", 0f);
    }
}
