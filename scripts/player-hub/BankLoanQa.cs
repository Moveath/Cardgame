// Companion partial for PlayerHubQa.cs. Memory-only bank desk and loan contract QA.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CardGame.Core.Meta;
using CardGameClient;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

public static partial class PlayerHubQa
{
    private static readonly List<string> BankLoanLayoutIssues = new List<string>();

    private static void BankLayoutCheck(bool condition, string message)
    {
        if (condition) { Check(true, message); return; }
        checks++;
        BankLoanLayoutIssues.Add(message); LayoutIssues.Add(message);
        System.IO.File.WriteAllLines(System.IO.Path.Combine(Output, "bank-layout-issues.txt"), BankLoanLayoutIssues);
        Debug.LogWarning("Bank layout: " + message);
    }

    private static async Task CheckBankLoan()
    {
        BankLoanLayoutIssues.Clear();
        System.IO.File.WriteAllText(System.IO.Path.Combine(Output, "bank-layout-issues.txt"), "");
        var previousStore = store; var previousClock = clock;
        var previousEventSystem = EventSystem.current;
        var previousInputModule = events.currentInputModule;
        var registeredEvents = EventSystem.current != events;
        if (registeredEvents) Call(events, "OnEnable"); // Ordinary MonoBehaviour lifecycle is not automatic in EditMode.
        EventSystem.current = events;
        var qaInput = events.gameObject.AddComponent<BankLoanQaInput>();
        var qaModule = events.gameObject.AddComponent<BankLoanQaInputModule>();
        qaModule.inputOverride = qaInput;
        Call(qaModule, "OnEnable");
        Call(events, "ChangeEventModule", qaModule);
        try
        {
            await BankLoanProfile(2, 20);
            hub.GoHome(); await NextFrame(); DiveNow(0); DiveNow(4);
            await OpenLoanDeskFromBoss();
            Check(Get<bool>(hub, "_bankLoanShown") && Get<string>(hub, "_page") == "home" && Get<int>(hub, "_cityLevel") == 4,
                "Tuzyara opens the desk over the bank hall");
            Call(hub, "UpdateInteriorControls"); Call(hub, "RefreshMapButton");
            Check(!InteriorExitVisible(), "bank desk hides the interior exit");
            var map = Get<RectTransform>(hub, "_mapButtonLayer");
            Check(map == null || !map.gameObject.activeSelf, "bank desk hides the map");
            CheckBankDeskActions(false, "before the first loan");
            Save("hub-bank-desk.png");

            await BankLoanClick("_bankLoanTakeButton", "take loan"); BankLoanStep(.5f);
            Check(Get<bool>(hub, "_bankContractShown"), "take loan opens its issued contract");
            var offerId = PlayerHubRuntime.Current.Bank.Offer.Id;
            var sequence = PlayerHubRuntime.Current.Bank.OfferSequence;
            Check(!string.IsNullOrWhiteSpace(offerId) && PlayerHubRuntime.Current.Bank.Offer.Number == 1 && sequence == 1,
                "Unity-serialized empty offer becomes the first numbered contract");
            FocusLoanClause(Get<InputField>(hub, "_bankLoanAmountInput").GetComponent<RectTransform>());
            var amountButtons = Get<List<Button>>(hub, "_bankLoanAmountButtons");
            var amountValues = Get<List<int>>(hub, "_bankLoanAmountValues");
            Button Amount(int value) => amountButtons[amountValues.IndexOf(value)];
            Check(Get<int>(hub, "_bankAmount") == 100 && !Amount(-50).interactable, "loan starts at the 100-coin minimum");
            await ClickButton(Amount(50), "increase loan by fifty");
            Check(Get<int>(hub, "_bankAmount") == 150, "plus button increases amount by fifty");
            await ClickButton(Amount(-50), "decrease loan by fifty");
            Check(Get<int>(hub, "_bankAmount") == 100, "minus button decreases amount by fifty");
            await ClickButton(Amount(500), "choose five hundred");
            Check(Get<int>(hub, "_bankAmount") == 500, "preset amount selects five hundred");
            CheckLoanReturn(500, "");
            await ClickButton(Amount(1000), "choose one thousand");
            Check(Get<int>(hub, "_bankAmount") == 1000, "preset amount selects one thousand");
            await ClickButton(Amount(int.MaxValue), "choose maximum loan");
            Check(Get<int>(hub, "_bankAmount") == PlayerHubRuntime.Current.Bank.CreditLimit && !Amount(50).interactable,
                "maximum uses the authoritative credit limit and disables further increases");
            await ClickButton(Amount(500), "return loan to five hundred");
            var exactAmount = Get<InputField>(hub, "_bankLoanAmountInput");
            Check(exactAmount.text == "500", "preset amount synchronizes the editable amount field");
            Save("hub-bank-contract.png");
            await CheckExactLoanInput(exactAmount);
            Check(exactAmount.textComponent.text == "347", "the rendered exact amount is 347 before photographing the contract");
            CheckLoanReturn(347, "");
            await CheckBankContractScroll("1600x900");
            ResizeCanvas(2000, 900); ((RectTransform)hub.transform).sizeDelta = new Vector2(2000, 900); Call(hub, "Resize");
            BankLoanStep(.01f); await CheckBankContractScroll("2000x900");
            foreach (var compact in new[] { new Vector2Int(1280, 720), new Vector2Int(960, 540) })
            {
                ResizeCanvas(compact.x, compact.y); ((RectTransform)hub.transform).sizeDelta = compact; Call(hub, "Resize");
                BankLoanStep(.02f); await CheckBankContractScroll(compact.x + "x" + compact.y);
            }
            ResizeCanvas(1600, 900); ((RectTransform)hub.transform).sizeDelta = new Vector2(1600, 900); Call(hub, "Resize"); BankLoanStep(.01f);

            await ScrollLoanContract(true);
            await BankLoanClick("_bankLoanDeclineButton", "decline without signing"); BankLoanStep(.5f);
            Check(!Get<bool>(hub, "_bankContractShown") && Get<bool>(hub, "_bankLoanShown"), "declining returns to Tuzyara's desk");
            Check(PlayerHubRuntime.Current.Bank.Offer.Id == offerId, "declining retains the same unsigned offer");
            CheckBankDeskActions(false, "after declining an unsigned offer");
            await BankBack(); await OpenLoanDeskFromBoss();
            await BankLoanClick("_bankLoanTakeButton", "reopen unsigned contract"); BankLoanStep(.5f);
            Check(PlayerHubRuntime.Current.Bank.Offer.Id == offerId && PlayerHubRuntime.Current.Bank.OfferSequence == sequence,
                "reopening the desk cannot reroll its contract");
            Check(Get<int>(hub, "_bankAmount") == 347 && Get<InputField>(hub, "_bankLoanAmountInput").text == "347",
                "reopening the unsigned contract retains its exact selected amount");
            await ScrollLoanContract(true);
            var before = PlayerHubRuntime.Current.Profile.Coins;
            var issued = clock.Now;
            await BankLoanClick("_bankLoanAgreeButton", "sign loan contract");
            var approved = PlayerHubRuntime.Current;
            Check(approved.Bank.DecisionCode == "approved" && approved.Profile.Coins == before + 347 && approved.Bank.Principal == 347,
                "signing immediately credits the full principal once");
            Check(approved.Bank.Debt == 417 && approved.Bank.Debt == HubLoans.Debt(347, "") && approved.Bank.DueAt == issued + HubLoans.HonestDays * 86400L,
                "approved debt and deadline match the printed contract");
            BankLoanStep(.3f);
            var signature = Get<Text>(hub, "_bankLoanSignature").text;
            Check(signature.Length > 0 && signature.Length < approved.Profile.DisplayName.Length,
                "borrower signature is written progressively");
            Check(Get<RectTransform>(hub, "_bankLoanPen").gameObject.activeSelf, "pen follows the signature");
            BankLoanStep(.48f);
            Check(Get<RectTransform>(hub, "_bankLoanStamp").gameObject.activeSelf && Get<Text>(hub, "_bankLoanStampText").text == "ОДОБРЕНО",
                "approval receives its blue decision stamp");
            Save("hub-bank-approved.png");
            BankLoanStep(1.1f);
            Check(Get<RectTransform[]>(hub, "_bankLoanCoins").Any(c => c.gameObject.activeSelf), "loan coins fly toward the wallet");
            Save("hub-bank-coins.png");
            BankLoanStep(3f);
            Check(!Get<bool>(hub, "_bankContractShown") && Get<RectTransform[]>(hub, "_bankLoanCoins").All(c => !c.gameObject.activeSelf),
                "completed approval removes the paper and finishes coin flight");
            Check(LoanDigits(Get<Text>(hub, "_bankLoanWalletText").text) == approved.Profile.Coins.ToString(),
                "animated wallet finishes at the saved balance");
            Check(Get<Button>(hub, "_bankLoanDebtButton").gameObject.activeSelf && !Get<Button>(hub, "_bankLoanTakeButton").gameObject.activeSelf,
                "desk displays debt after issuance");
            CheckBankDeskActions(true, "after signing an active loan");
            await CheckSignedBankContractAndReceipts();
            await CheckBankRepaymentMotionPreferences();
            await CheckLoanNavigation();
            await CheckLoanHistoryRefusal();
            await CheckLoanFinePrint();
            Check(BankLoanLayoutIssues.Count == 0, "all scrolling contract and receipt geometry checks passed:\n" + string.Join("\n", BankLoanLayoutIssues));
        }
        finally
        {
            if (Get<RectTransform>(hub, "_bankLoanLayer") != null)
            {
                Set(hub, "_bankReceiptShown", false);
                Set(hub, "_bankLoanShown", false); Set(hub, "_bankContractShown", false);
                Set(hub, "_bankIssueTime", -1f); Set(hub, "_bankFineTime", -1f);
                BankLoanStep(1f);
            }
            store = previousStore; clock = previousClock;
            PlayerHubRuntime.UseService(new LocalPlayerHubService(store, clock), clock);
            var restored = await PlayerHubRuntime.LoadAsync();
            Check(restored.Success, "bank QA restores the preceding memory-only profile");
            Call(hub, "OnSnapshot", restored.Snapshot);
            ResizeCanvas(1600, 900); ((RectTransform)hub.transform).sizeDelta = new Vector2(1600, 900); Call(hub, "Resize");
            hub.GoHome(); DiveNow(0);
            events.SetSelectedGameObject(null);
            Call(events, "ChangeEventModule", previousInputModule);
            UnityEngine.Object.DestroyImmediate(qaModule); UnityEngine.Object.DestroyImmediate(qaInput);
            events.UpdateModules();
            if (registeredEvents) Call(events, "OnDisable");
            if (previousEventSystem != null) EventSystem.current = previousEventSystem;
        }
    }

    private static async Task BankLoanProfile(int matches, int level = 1, int offerSequence = 0, string offerTrick = null)
    {
        store = new MemoryStore(); clock = new FixedClock();
        var service = new LocalPlayerHubService(store, clock);
        var loaded = await service.LoadAsync(); Check(loaded.Success, "bank fixture creates a memory-only profile");
        var sample = loaded.Snapshot;
        sample.Profile.DisplayName = "Тестовый игрок"; sample.Profile.Matches = matches;
        sample.Profile.Level = level; sample.Profile.XP = (level - 1) * 100;
        sample.IncomeDays[0].Gross = level > 1 ? 5000 : 300;
        sample.Bank.OfferSequence = offerSequence;
        if (offerTrick != null)
        {
            sample.Bank.OfferSequence = Math.Max(2, offerSequence);
            sample.Bank.Offer = new HubLoanOffer { Id = "qa-" + offerTrick, Number = sample.Bank.OfferSequence, Trick = offerTrick };
        }
        await store.SaveAsync(sample);
        PlayerHubRuntime.UseService(new LocalPlayerHubService(store, clock), clock);
        var ready = await PlayerHubRuntime.LoadAsync(); Check(ready.Success, "bank fixture loads through runtime service");
        Call(hub, "OnSnapshot", ready.Snapshot);
    }

    private static async Task OpenLoanDeskFromBoss()
    {
        var area = Get<RectTransform>(hub, "_cityArea");
        var boss = ((Array)typeof(PlayerHubController).GetField("CityPlaces", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null))
            .Cast<object>().Single(p => (string)Item(p, "Id") == "bank-boss");
        var point = (Vector2)Call(hub, "PlaceScreenPoint", boss, Item(boss, "Door"));
        TapScreen(area, RectTransformUtility.WorldToScreenPoint(camera, area.TransformPoint(area.rect.center + point)), "Tuzyara loan desk");
        BankLoanStep(.5f); await NextFrame();
        Check(Get<bool>(hub, "_bankLoanShown"), "a real tap on Tuzyara opens his loan desk");
    }

    private static void BankLoanStep(float duration)
    {
        for (var elapsed = 0f; elapsed < duration; elapsed += .02f)
            Call(hub, "UpdateBankLoan", Mathf.Min(.02f, duration - elapsed), false);
        Canvas.ForceUpdateCanvases();
    }

    private static async Task BankLoanClick(string field, string label)
    {
        var button = Get<Button>(hub, field);
        var content = Get<RectTransform>(hub, "_bankContractContent");
        if (content != null && button.transform.IsChildOf(content)) FocusLoanClause((RectTransform)button.transform);
        await ClickButton(button, label);
    }

    private static async Task BankBack()
    {
        var layer = Get<RectTransform>(hub, "_bankLoanLayer");
        await ClickButton(layer.GetComponentsInChildren<Button>().Single(b => ButtonLabel(b) == "НАЗАД"), "bank desk Back");
        BankLoanStep(.5f);
        Check(!Get<bool>(hub, "_bankLoanShown") && Get<int>(hub, "_cityLevel") == 4 && Get<int>(hub, "_diveTarget") < 0,
            "desk Back closes only the close-up");
    }

    private static async Task CheckLoanNavigation()
    {
        await OpenLoanDeskFromBoss();
        var keyboard = InputSystem.AddDevice<Keyboard>("Loan QA keyboard");
        try
        {
            keyboard.MakeCurrent(); InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape)); InputSystem.Update();
            Call(hub, "UpdateCity", false); BankLoanStep(.5f); Call(hub, "UpdateCity", false);
            Check(!Get<bool>(hub, "_bankLoanShown") && Get<int>(hub, "_cityLevel") == 4 && Get<int>(hub, "_diveTarget") < 0,
                "held Escape closes only Tuzyara's desk");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update(); Call(hub, "UpdateCity", false);
        }
        finally { InputSystem.RemoveDevice(keyboard); }
        await OpenLoanDeskFromBoss(); await BankBack();
        Call(hub, "UpdateInteriorControls"); Call(hub, "RefreshMapButton");
        Check(InteriorExitVisible(), "bank exit returns after closing the desk");
        Check(Get<RectTransform>(hub, "_mapButtonLayer").gameObject.activeSelf, "map returns after closing the desk");
    }

    private static async Task CheckLoanHistoryRefusal()
    {
        await BankLoanProfile(0); await OpenLoanDeskFromBoss();
        await BankLoanClick("_bankLoanTakeButton", "newcomer loan offer"); BankLoanStep(.5f);
        Check((int)Call(hub, "LoanMaximum") == 100 && Get<List<Button>>(hub, "_bankLoanAmountButtons").Last().interactable == false,
            "newcomer UI permits only half the credit limit");
        Check(Get<Text>(hub, "_bankLoanHistoryText").text.Contains("100"), "contract explains the newcomer history limit");
        // Only the view is stale. The real service still owns a fresh zero-match profile.
        var stale = JsonUtility.FromJson<HubSnapshot>(JsonUtility.ToJson(PlayerHubRuntime.Current));
        stale.Profile.Matches = 2; Call(hub, "OnSnapshot", stale); Call(hub, "BindBankContract");
        var plus = Get<List<Button>>(hub, "_bankLoanAmountButtons").Last();
        FocusLoanClause((RectTransform)plus.transform);
        await ClickButton(plus, "stale view selects one hundred fifty"); await ClickButton(plus, "stale view selects two hundred");
        Check(Get<int>(hub, "_bankAmount") == 200, "stale eligibility fixture reaches the service through the real amount buttons");
        await ScrollLoanContract(true);
        await BankLoanClick("_bankLoanAgreeButton", "submit stale eligibility for decision");
        Check(PlayerHubRuntime.Current.Bank.DecisionCode == "history" && PlayerHubRuntime.Current.Profile.Coins == 300 && PlayerHubRuntime.Current.Bank.Debt == 0,
            "service immediately refuses missing history without paying coins");
        BankLoanStep(.78f);
        Check(Get<Text>(hub, "_bankLoanStampText").text == "ОТКАЗАНО" && Get<RectTransform>(hub, "_bankLoanStamp").gameObject.activeSelf,
            "business refusal produces the refusal stamp");
        Save("hub-bank-rejected.png"); BankLoanStep(3f);
        Check(Get<Text>(hub, "_bankLoanBubbleText").text.Contains("истории"), "refusal explains the missing match history");
        Check(Get<RectTransform[]>(hub, "_bankLoanCoins").All(c => !c.gameObject.activeSelf), "a refusal never creates a coin flight");
        await BankBack();
    }

    private static async Task InspectLoanClause(string key)
    {
        var button = Get<Dictionary<string, Button>>(hub, "_bankClauseButtons")[key];
        FocusLoanClause((RectTransform)button.transform);
        await ClickButton(button, "inspect the " + key + " clause"); BankLoanStep(.4f);
        Check(Get<bool>(hub, "_bankFineInspecting") && Get<RectTransform>(hub, "_bankFineReading").gameObject.activeInHierarchy,
            "the selected clause opens a readable inspection overlay: " + key);
        Check(Get<Button>(hub, "_bankFineChallengeButton").gameObject.activeInHierarchy,
            "an unsigned selected clause offers an explicit challenge: " + key);
    }

    private static async Task CloseLoanClause()
    {
        await BankLoanClick("_bankFineCloseButton", "close the inspected condition"); BankLoanStep(.2f);
        Check(!Get<bool>(hub, "_bankFineInspecting"), "closing clause inspection restores the contract");
    }

    private static async Task CheckLoanFinePrint()
    {
        await BankLoanProfile(2, 1, 3, "fee"); await OpenLoanDeskFromBoss();
        await BankLoanClick("_bankLoanTakeButton", "open a saved offer with an extra commission"); BankLoanStep(.5f);
        var offer = PlayerHubRuntime.Current.Bank.Offer;
        Check(offer.Id == "qa-fee" && offer.Trick == "fee", "the UI loads the service-owned pending commission offer");
        CheckLoanReturn(Get<int>(hub, "_bankAmount"), offer.Trick);
        await ScrollLoanContract(true);
        InspectBankFinePrint("1600x900"); Save("hub-bank-contract-fine.png"); InspectLoanPaper("fine print");
        foreach (var compact in new[] { new Vector2Int(1280, 720), new Vector2Int(960, 540) })
        {
            ResizeCanvas(compact.x, compact.y); ((RectTransform)hub.transform).sizeDelta = compact; Call(hub, "Resize"); BankLoanStep(.02f);
            var size = compact.x + "x" + compact.y;
            InspectBankFinePrint(size); Save("hub-bank-contract-fine-" + size + ".png"); InspectLoanPaper("fine print " + size);
        }
        ResizeCanvas(1280, 720); ((RectTransform)hub.transform).sizeDelta = new Vector2(1280, 720); Call(hub, "Resize"); BankLoanStep(.02f);
        FocusLoanClause(Get<Text>(hub, "_bankFineText").rectTransform);
        await BankLoanClick("_bankFineButton", "read the ordinary payment note on a small screen"); BankLoanStep(.4f);
        var read = PlayerHubRuntime.Current;
        Check(read.Bank.Offer.Id == offer.Id && read.Bank.Offer.FineRead && !read.Bank.Offer.Revealed && read.Bank.Offer.Trick == "fee",
            "reading an ordinary note retains the exact dishonest condition");
        Check(!read.Achievements.Single(a => a.Id == "fine_print").Unlocked, "reading alone does not unlock the discovery achievement");
        Check(Get<RectTransform>(hub, "_bankFineReading").gameObject.activeInHierarchy, "reading opens enlarged contract text");
        Check(!Get<Button>(hub, "_bankLoanAgreeButton").IsInteractable(), "signing remains blocked during clause inspection");
        Save("hub-bank-contract-reading-1280x720.png");
        await CloseLoanClause();
        await InspectLoanClause("rate");
        await BankLoanClick("_bankFineChallengeButton", "challenge an unchanged rate in a commission offer"); BankLoanStep(.2f);
        Check(PlayerHubRuntime.Current.Bank.Offer.Trick == "fee" && !PlayerHubRuntime.Current.Achievements.Single(a => a.Id == "fine_print").Unlocked,
            "challenging the wrong condition neither fixes the commission nor rewards guessing");
        await CloseLoanClause();
        await InspectLoanClause("fee");
        Save("hub-bank-contract-reading.png");
        var coins = PlayerHubRuntime.Current.Profile.Coins; var xp = PlayerHubRuntime.Current.Profile.XP;
        await BankLoanClick("_bankFineChallengeButton", "object to the actual hidden commission"); BankLoanStep(.3f);
        var corrected = PlayerHubRuntime.Current;
        Check(corrected.Bank.Offer.Id == offer.Id && corrected.Bank.Offer.Revealed && corrected.Bank.Offer.Trick == "" && corrected.Bank.Offer.OriginalTrick == "fee",
            "the correct explicit challenge fixes the same contract and preserves its original trick");
        var achievement = corrected.Achievements.Single(a => a.Id == "fine_print");
        Check(achievement.Unlocked && !achievement.Claimed && corrected.Profile.Coins == coins,
            "finding the trick unlocks the one-time achievement without silently claiming its reward");
        Check(Get<bool>(hub, "_bankFineInspecting"), "Tuzyara's correction stays visible until the player dismisses it");
        Save("hub-bank-contract-corrected.png");
        await CloseLoanClause();
        CheckLoanReturn(Get<int>(hub, "_bankAmount"), "");
        var reward = await PlayerHubRuntime.ExecuteAsync(new HubCommand { Action = "achievement_claim", Target = "fine_print" });
        Check(reward.Success && PlayerHubRuntime.Current.Profile.Coins == coins + 50 && PlayerHubRuntime.Current.Profile.XP == xp + 30,
            "the discovered trick achievement awards exactly fifty coins and thirty XP once claimed");
        await ScrollLoanContract(true);
        await BankLoanClick("_bankLoanDeclineButton", "put the corrected offer away"); BankLoanStep(.5f);
        await BankBack(); await OpenLoanDeskFromBoss();
        await BankLoanClick("_bankLoanTakeButton", "reopen the corrected saved offer"); BankLoanStep(.5f);
        Check(PlayerHubRuntime.Current.Bank.Offer.Id == offer.Id && PlayerHubRuntime.Current.Bank.Offer.Revealed
            && PlayerHubRuntime.Current.Bank.Offer.Trick == "" && Get<Button>(hub, "_bankFineButton").gameObject.activeSelf,
            "the corrected offer survives closing, while ordinary notes remain available");
        await ScrollLoanContract(true);
        await BankLoanClick("_bankLoanDeclineButton", "close the corrected contract"); BankLoanStep(.5f); await BankBack();
        foreach (var trick in new[] { "rate", "short" })
        {
            await BankLoanProfile(2, 1, 4, trick); await OpenLoanDeskFromBoss();
            await BankLoanClick("_bankLoanTakeButton", "open a saved " + trick + " offer"); BankLoanStep(.5f);
            CheckLoanReturn(Get<int>(hub, "_bankAmount"), trick);
            await InspectLoanClause(trick);
            Check(PlayerHubRuntime.Current.Bank.Offer.Trick == trick, "inspection leaves the " + trick + " condition unchanged");
            await BankLoanClick("_bankFineChallengeButton", "challenge the actual " + trick + " condition"); BankLoanStep(.2f);
            Check(PlayerHubRuntime.Current.Bank.Offer.Trick == "" && PlayerHubRuntime.Current.Bank.Offer.Revealed,
                "an explicit correct challenge repairs the " + trick + " condition");
            await CloseLoanClause(); await ScrollLoanContract(true);
            await BankLoanClick("_bankLoanDeclineButton", "leave the corrected " + trick + " offer"); BankLoanStep(.5f); await BankBack();
        }
        ResizeCanvas(1600, 900); ((RectTransform)hub.transform).sizeDelta = new Vector2(1600, 900); Call(hub, "Resize"); BankLoanStep(.02f);
        await BankLoanProfile(2, 20, 5, "rate"); await OpenLoanDeskFromBoss();
        await BankLoanClick("_bankLoanTakeButton", "open a loan with an unnoticed higher rate"); BankLoanStep(.5f);
        await TypeLoanAmount(Get<InputField>(hub, "_bankLoanAmountInput"), "100");
        await ScrollLoanContract(true);
        await BankLoanClick("_bankLoanAgreeButton", "sign without discovering the higher rate"); BankLoanStep(4.2f);
        Check(PlayerHubRuntime.Current.Bank.Debt == 140 && PlayerHubRuntime.Current.Bank.Contract.Trick == "rate",
            "signing an unnoticed forty-percent rate saves the actual disclosed debt");
        Check(Get<bool>(hub, "_bankFineInspecting") && Get<Text>(hub, "_bankFineReadingTitle").text == "ДОГОВОР УЖЕ ПОДПИСАН"
            && !Get<Button>(hub, "_bankFineChallengeButton").gameObject.activeInHierarchy,
            "Tuzyara immediately reveals the missed trick after issuance without allowing post-signature edits");
        Check(Get<Text>(hub, "_bankFineReadingText").text.Contains("40%") && HubLoans.PayoffQuote(PlayerHubRuntime.Current.Bank, clock.Now).Amount == 110,
            "the reveal names the higher rate while retaining the first-day ten-percent escape");
        Check(!PlayerHubRuntime.Current.Achievements.Single(a => a.Id == "fine_print").Unlocked,
            "the bank's own reveal does not grant the player's discovery achievement");
        Save("hub-bank-contract-missed-trick.png"); await CloseLoanClause(); await BankBack();
    }

    private static void InspectBankFinePrint(string size)
    {
        FocusLoanClause(Get<Text>(hub, "_bankFineText").rectTransform);
        Canvas.ForceUpdateCanvases(); camera.Render();
        var button = Get<Button>(hub, "_bankFineButton");
        var target = (RectTransform)button.transform;
        var fine = Get<Text>(hub, "_bankFineText");
        Check(fine.fontSize >= 22 && !fine.resizeTextForBestFit && target.rect.height >= 50,
            "the footnote keeps a readable font and generous tap height at " + size);
        var screenEm = Vector2.Distance(RectTransformUtility.WorldToScreenPoint(camera, fine.transform.position),
            RectTransformUtility.WorldToScreenPoint(camera, fine.transform.TransformPoint(Vector3.up * fine.fontSize)));
        Check(screenEm >= 13, "footnote letters retain at least thirteen screen pixels at " + size);
        var paper = Get<RectTransform>(hub, "_bankContract");
        Rect OnPaper(RectTransform rect)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            var local = corners.Select(c => paper.InverseTransformPoint(c)).ToArray();
            return Rect.MinMaxRect(local.Min(c => c.x), local.Min(c => c.y), local.Max(c => c.x), local.Max(c => c.y));
        }
        var footnote = OnPaper(target);
        foreach (var field in new[] { "_bankLoanAgreeButton", "_bankLoanDeclineButton" })
        {
            var control = Get<Button>(hub, field); var area = OnPaper((RectTransform)control.transform);
            BankLayoutCheck(!footnote.Overlaps(area) && footnote.yMin - area.yMax >= 8,
                "footnote remains visibly separated from " + field + " at " + size);
        }
        foreach (var fraction in new[] { .12f, .5f, .88f })
        {
            var point = target.TransformPoint(new Vector3(Mathf.Lerp(target.rect.xMin, target.rect.xMax, fraction), target.rect.center.y));
            var pointer = new PointerEventData(events) { position = RectTransformUtility.WorldToScreenPoint(camera, point) };
            var hits = new List<RaycastResult>(); canvasObject.GetComponent<GraphicRaycaster>().Raycast(pointer, hits);
            BankLayoutCheck(hits.Count > 0 && (hits[0].gameObject == button.gameObject || hits[0].gameObject.transform.IsChildOf(button.transform)),
                "footnote tap reaches its text strip without hitting a decision at " + size + " (" + fraction + ")");
        }
    }

    private static async Task CheckExactLoanInput(InputField input)
    {
        Check(input.characterLimit == 7, "exact loan amount input limits the numeric length to seven characters");
        await TypeLoanAmount(input, "347", false);
        Check(input.text == "347" && Get<int>(hub, "_bankAmount") == 347,
            "typing a non-preset amount preserves every coin without rounding");
        CheckLoanReturn(347, "");
        events.SetSelectedGameObject(null);
        await TypeLoanAmount(input, "");
        Check(input.text == "" && Get<int>(hub, "_bankAmount") == 347,
            "empty input is preserved and does not silently replace the last valid amount");
        await CheckInvalidLoanInput("empty amount");
        var presetIndex = Get<List<int>>(hub, "_bankLoanAmountValues").IndexOf(100);
        var hundred = Get<List<Button>>(hub, "_bankLoanAmountButtons")[presetIndex];
        await ClickButton(hundred, "recover empty input with the one-hundred preset");
        Check(input.text == "100" && Get<int>(hub, "_bankAmount") == 100,
            "a preset restores a valid amount after the field was cleared");
        await TypeLoanAmount(input, "");
        Check(hundred.IsInteractable(), "the current-value preset remains available when typed input is invalid");
        await ClickButton(hundred, "recover the same hundred after clearing it");
        Check(input.text == "100" && Get<int>(hub, "_bankAmount") == 100,
            "pressing the same preset repairs invalid input instead of staying disabled");
        await TypeLoanAmount(input, "347");
        await TypeLoanAmount(input, "99");
        Check(input.text == "99" && Get<int>(hub, "_bankAmount") == 347,
            "a below-minimum amount is preserved instead of being rounded to one hundred");
        await CheckInvalidLoanInput("below-minimum amount");
        var aboveLimit = ((int)Call(hub, "LoanMaximum") + 1).ToString();
        await TypeLoanAmount(input, aboveLimit);
        Check(input.text == aboveLimit, "above-limit input is preserved instead of silently clamped");
        await CheckInvalidLoanInput("above-limit amount");
        await TypeLoanAmount(input, "123456789");
        Check(input.text == "1234567", "seven-character limit is applied by the input's normal keyboard handler");
        await CheckInvalidLoanInput("overlong amount");
        await TypeLoanAmount(input, "-3a4b7");
        Check(input.text == "347" && Get<int>(hub, "_bankAmount") == 347,
            "exact amount input accepts only digits through normal input validation");
        CheckLoanReturn(347, "");
        await CheckLoanEscapeAfterInput(input);
    }

    private static async Task CheckLoanEscapeAfterInput(InputField input)
    {
        var offerId = PlayerHubRuntime.Current.Bank.Offer.Id;
        await TypeLoanAmount(input, "347", false);
        // OnUpdateSelected sees KeyPressed(Return) return Finish and then deactivates the field.
        // Keeping EventSystem selection on it reproduces the Enter-then-Escape edge case.
        input.ProcessEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.Return, character = '\n' });
        input.DeactivateInputField(); input.ForceLabelUpdate();
        Check(!input.isFocused && events.currentSelectedGameObject == input.gameObject,
            "Enter finishes exact amount editing while the InputField stays selected");
        var keyboard = InputSystem.AddDevice<Keyboard>("Loan completed input Escape QA");
        try
        {
            keyboard.MakeCurrent(); InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape)); InputSystem.Update();
            Call(hub, "UpdateBankLoan", .02f, true); BankLoanStep(.5f);
            Check(!Get<bool>(hub, "_bankContractShown") && Get<bool>(hub, "_bankLoanShown")
                && Get<int>(hub, "_cityLevel") == 4 && Get<int>(hub, "_diveTarget") < 0,
                "Escape after Enter closes only the contract despite the selected inactive InputField");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update();
            Call(hub, "UpdateBankLoan", .02f, true);
        }
        finally
        {
            events.SetSelectedGameObject(null);
            InputSystem.RemoveDevice(keyboard);
        }
        await BankLoanClick("_bankLoanTakeButton", "reopen contract after Escape from completed input"); BankLoanStep(.5f);
        Check(Get<bool>(hub, "_bankContractShown") && PlayerHubRuntime.Current.Bank.Offer.Id == offerId
            && Get<int>(hub, "_bankAmount") == 347 && input.text == "347" && input.textComponent.text == "347",
            "reopening after Escape preserves both the unsigned offer and the exact visible amount");
    }

    private static async Task CheckInvalidLoanInput(string reason)
    {
        var hint = Get<Text>(hub, "_bankLoanHistoryText");
        Check(!string.IsNullOrWhiteSpace(hint.text) && hint.color.r > hint.color.g && hint.color.r > hint.color.b,
            reason + " displays a red explanation on the document");
        await ScrollLoanContract(true);
        Check(!Get<Button>(hub, "_bankLoanAgreeButton").IsInteractable(), reason + " disables signing at the end of the contract");
        await ScrollLoanContract(false);
    }

    private static async Task TypeLoanAmount(InputField input, string value, bool commit = true)
    {
        if (input.transform.IsChildOf(Get<RectTransform>(hub, "_bankContractContent"))) FocusLoanClause(input.GetComponent<RectTransform>());
        var hit = Hit((RectTransform)input.transform);
        Check(hit.gameObject != null && (hit.gameObject == input.gameObject || hit.gameObject.transform.IsChildOf(input.transform)),
            "a tap reaches the exact loan amount input");
        var pointer = new PointerEventData(events)
        {
            button = PointerEventData.InputButton.Left, position = hit.screenPosition,
            pointerCurrentRaycast = hit, pointerPressRaycast = hit
        };
        ExecuteEvents.Execute(input.gameObject, pointer, ExecuteEvents.pointerDownHandler);
        ExecuteEvents.Execute(input.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        // Edit-mode QA drives the same deferred activation which InputField.LateUpdate uses in play mode.
        input.ActivateInputField(); Call(input, "LateUpdate");
        Check(input.isFocused, "exact loan amount input accepts focus");
        void SendKey(Event key)
        {
            input.ProcessEvent(key);
            // ProcessEvent calls KeyPressed only. The normal OnUpdateSelected loop also updates
            // the rendered label after each event; reproduce that standard step in EditMode.
            input.ForceLabelUpdate();
        }
        SendKey(new Event { type = EventType.KeyDown, keyCode = KeyCode.A, modifiers = EventModifiers.Control });
        foreach (var character in value)
            SendKey(new Event { type = EventType.KeyDown, keyCode = KeyCode.None, character = character });
        if (value.Length == 0) SendKey(new Event { type = EventType.KeyDown, keyCode = KeyCode.Backspace });
        if (commit) events.SetSelectedGameObject(null); // Standard deselection commits onEndEdit.
        Call(input, "LateUpdate"); input.ForceLabelUpdate();
        await NextFrame(); Canvas.ForceUpdateCanvases();
    }

    private static void AdvanceLoanScroll(float duration)
    {
        var scroll = Get<ScrollRect>(hub, "_bankContractScroll");
        for (var elapsed = 0f; elapsed < duration; elapsed += .02f)
        {
            if (scroll is HubSmoothScroll) Call(scroll, "AdvanceWheel", .02f);
            Call(scroll, "LateUpdate");
            BankLoanStep(.02f);
        }
        Canvas.ForceUpdateCanvases();
    }

    private static async Task ScrollLoanContract(bool end)
    {
        var scroll = Get<ScrollRect>(hub, "_bankContractScroll");
        var offerId = PlayerHubRuntime.Current.Bank.Offer?.Id;
        var amount = Get<int>(hub, "_bankAmount");
        var pointer = new PointerEventData(events)
        {
            position = RectTransformUtility.WorldToScreenPoint(camera, scroll.viewport.TransformPoint(scroll.viewport.rect.center)),
            scrollDelta = new Vector2(0, end ? -1000 : 1000)
        };
        ExecuteEvents.Execute(scroll.gameObject, pointer, ExecuteEvents.scrollHandler);
        AdvanceLoanScroll(4f); await NextFrame();
        Check(end ? scroll.verticalNormalizedPosition <= .015f : scroll.verticalNormalizedPosition >= .985f,
            "wheel reaches the " + (end ? "signature" : "beginning") + " of the contract");
        Check(Get<int>(hub, "_bankAmount") == amount && PlayerHubRuntime.Current.Bank.Offer?.Id == offerId,
            "scrolling preserves the selected exact amount and authoritative offer");
    }

    private static void FocusLoanClause(RectTransform target)
    {
        var scroll = Get<ScrollRect>(hub, "_bankContractScroll");
        Canvas.ForceUpdateCanvases();
        var content = scroll.content;
        var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(content, target);
        var distance = content.rect.yMax - bounds.center.y - scroll.viewport.rect.height * .5f;
        var travel = Mathf.Max(1, content.rect.height - scroll.viewport.rect.height);
        scroll.StopMovement(); scroll.verticalNormalizedPosition = 1 - Mathf.Clamp01(distance / travel);
        Call(scroll, "LateUpdate"); BankLoanStep(.05f); Canvas.ForceUpdateCanvases();
    }

    private static async Task CheckContractDrag()
    {
        await ScrollLoanContract(false);
        var scroll = Get<ScrollRect>(hub, "_bankContractScroll");
        var viewport = scroll.viewport;
        var start = RectTransformUtility.WorldToScreenPoint(camera, viewport.TransformPoint(viewport.rect.center));
        var before = scroll.content.anchoredPosition.y;
        var pointer = new PointerEventData(events) { button = PointerEventData.InputButton.Left, position = start, pressPosition = start,
            pointerPressRaycast = new RaycastResult { module = canvasObject.GetComponent<GraphicRaycaster>() } };
        ExecuteEvents.Execute(scroll.gameObject, pointer, ExecuteEvents.initializePotentialDrag);
        ExecuteEvents.Execute(scroll.gameObject, pointer, ExecuteEvents.beginDragHandler);
        pointer.position = start + Vector2.up * 180;
        ExecuteEvents.Execute(scroll.gameObject, pointer, ExecuteEvents.dragHandler);
        ExecuteEvents.Execute(scroll.gameObject, pointer, ExecuteEvents.endDragHandler);
        scroll.StopMovement(); BankLoanStep(.1f);
        Check(scroll.content.anchoredPosition.y > before + 50, "finger drag moves the contract down to later clauses");
        await ScrollLoanContract(false);
    }

    private static void SendContractWheel(float ticks)
    {
        var scroll = Get<ScrollRect>(hub, "_bankContractScroll");
        var pointer = new PointerEventData(events)
        {
            position = RectTransformUtility.WorldToScreenPoint(camera, scroll.viewport.TransformPoint(scroll.viewport.rect.center)),
            scrollDelta = new Vector2(0, ticks)
        };
        ExecuteEvents.Execute(scroll.gameObject, pointer, ExecuteEvents.scrollHandler);
    }

    private static async Task CheckContractGentleWheel()
    {
        await ScrollLoanContract(false);
        var scroll = Get<ScrollRect>(hub, "_bankContractScroll");
        var amount = Get<int>(hub, "_bankAmount");
        var offerId = PlayerHubRuntime.Current.Bank.Offer?.Id;
        var start = scroll.content.anchoredPosition.y;
        void AdvanceTimedWheel(float duration)
        {
            // EditMode's Time.unscaledDeltaTime can include a long editor frame.
            // Exercise the wheel integrator once per controlled time step here;
            // the broader integration checks still run normal LateUpdate.
            for (var elapsed = 0f; elapsed < duration; elapsed += .02f)
            {
                var step = Mathf.Min(.02f, duration - elapsed);
                Call(scroll, "AdvanceWheel", step);
                BankLoanStep(step);
            }
            Canvas.ForceUpdateCanvases();
        }
        SendContractWheel(-1);
        Check(Mathf.Abs(scroll.content.anchoredPosition.y - start) < .01f,
            "one wheel tick starts an animation without jumping the contract immediately");
        AdvanceTimedWheel(.06f);
        var early = scroll.content.anchoredPosition.y - start;
        AdvanceTimedWheel(.16f);
        var later = scroll.content.anchoredPosition.y - start;
        AdvanceTimedWheel(2f);
        var settled = scroll.content.anchoredPosition.y - start;
        var timing = " (early=" + early.ToString("F3") + ", later=" + later.ToString("F3") + ", settled=" + settled.ToString("F3") + ")";
        Check(early > .05f && later > early + .5f && settled > later + .5f,
            "a wheel tick moves progressively instead of completing in its first frames" + timing);
        Check(settled > 10 && settled < 80 && early < settled * .45f,
            "a single wheel tick advances a modest reading distance with a gentle start" + timing);

        SendContractWheel(-4); AdvanceTimedWheel(.08f);
        var reversal = scroll.content.anchoredPosition.y;
        SendContractWheel(1); AdvanceTimedWheel(.08f);
        Check(scroll.content.anchoredPosition.y < reversal - .1f,
            "reversing the wheel changes direction immediately instead of finishing the previous downward target");
        scroll.StopMovement();
        var stopped = scroll.content.anchoredPosition.y;
        AdvanceTimedWheel(.6f);
        Check(Mathf.Abs(scroll.content.anchoredPosition.y - stopped) < .1f,
            "stopping the scroll cancels pending wheel movement");
        Check(!Get<Button>(hub, "_bankLoanAgreeButton").IsInteractable(),
            "small wheel movement does not bypass the signature-at-end gate");
        Check(Get<int>(hub, "_bankAmount") == amount && PlayerHubRuntime.Current.Bank.Offer?.Id == offerId,
            "gentle scrolling preserves the exact principal and issued offer");
        await ScrollLoanContract(false);
    }

    private static async Task CheckContractScrollbar(string size)
    {
        await ScrollLoanContract(false);
        var scroll = Get<ScrollRect>(hub, "_bankContractScroll");
        var bar = Get<Scrollbar>(hub, "_bankContractScrollbar");
        var thumb = Get<RectTransform>(hub, "_bankScrollThumb");
        var amount = Get<int>(hub, "_bankAmount");
        var offerId = PlayerHubRuntime.Current.Bank.Offer?.Id;
        Check(bar != null && bar.gameObject.activeInHierarchy && bar.IsInteractable()
            && scroll.verticalScrollbar == bar && bar.handleRect == thumb,
            "the visible right handle is a live scrollbar controlling the contract at " + size);
        Check(thumb.rect.width >= 35,
            "the narrow-looking scrollbar has a generous pointer target at " + size);

        // Begin while a wheel animation still has an unfinished target. Grabbing the
        // scrollbar must take ownership, including after the pointer is released.
        SendContractWheel(-6); AdvanceLoanScroll(.08f);
        SendContractWheel(-1); // A fresh pending tick cannot finish during a long EditMode LateUpdate.
        Canvas.ForceUpdateCanvases();
        var raycaster = canvasObject.GetComponent<GraphicRaycaster>();
        foreach (var fraction in new[] { .12f, .5f, .88f })
        {
            var local = new Vector3(Mathf.Lerp(thumb.rect.xMin, thumb.rect.xMax, fraction), thumb.rect.center.y);
            var pointer = new PointerEventData(events) { position = RectTransformUtility.WorldToScreenPoint(camera, thumb.TransformPoint(local)) };
            var hits = new List<RaycastResult>(); raycaster.Raycast(pointer, hits);
            Check(hits.Count > 0 && ExecuteEvents.GetEventHandler<IDragHandler>(hits[0].gameObject) == bar.gameObject,
                "pointer reaches the right scrollbar across its hit area at " + size + " (" + fraction + ")");
        }

        PointerEventData GrabHandle()
        {
            Canvas.ForceUpdateCanvases();
            var position = RectTransformUtility.WorldToScreenPoint(camera, thumb.TransformPoint(thumb.rect.center));
            var pointer = new PointerEventData(events)
            {
                button = PointerEventData.InputButton.Left, position = position, pressPosition = position,
                pointerDrag = bar.gameObject, pointerPress = bar.gameObject
            };
            var hits = new List<RaycastResult>(); raycaster.Raycast(pointer, hits);
            Check(hits.Count > 0 && ExecuteEvents.GetEventHandler<IDragHandler>(hits[0].gameObject) == bar.gameObject,
                "the actual handle raycast starts scrollbar dragging at " + size);
            pointer.pointerPressRaycast = pointer.pointerCurrentRaycast = hits[0];
            ExecuteEvents.Execute(bar.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(bar.gameObject, pointer, ExecuteEvents.initializePotentialDrag);
            ExecuteEvents.Execute(bar.gameObject, pointer, ExecuteEvents.beginDragHandler);
            pointer.dragging = true;
            return pointer;
        }
        void DragHandle(PointerEventData pointer, float position)
        {
            var track = (RectTransform)thumb.parent;
            var halfHeight = thumb.rect.height * .5f;
            var center = new Vector3(track.rect.center.x,
                Mathf.Lerp(track.rect.yMin + halfHeight, track.rect.yMax - halfHeight, position));
            var screen = RectTransformUtility.WorldToScreenPoint(camera, track.TransformPoint(center));
            pointer.delta = screen - pointer.position; pointer.position = screen;
            ExecuteEvents.Execute(bar.gameObject, pointer, ExecuteEvents.dragHandler);
            AdvanceLoanScroll(.08f);
        }
        void ReleaseHandle(PointerEventData pointer)
        {
            ExecuteEvents.Execute(bar.gameObject, pointer, ExecuteEvents.endDragHandler);
            ExecuteEvents.Execute(bar.gameObject, pointer, ExecuteEvents.pointerUpHandler);
            pointer.dragging = false;
        }

        var drag = GrabHandle(); DragHandle(drag, .45f);
        Check(scroll.verticalNormalizedPosition > .3f && scroll.verticalNormalizedPosition < .6f,
            "holding and dragging the right handle moves directly to the middle of the contract at " + size);
        Check(!Get<Button>(hub, "_bankLoanAgreeButton").IsInteractable(),
            "a middle scrollbar position keeps signing disabled at " + size);
        if (size == "1600x900") Save("hub-bank-contract-scrollbar-drag.png");
        DragHandle(drag, 0); ReleaseHandle(drag);
        Check(scroll.verticalNormalizedPosition <= .015f && Get<Button>(hub, "_bankLoanAgreeButton").IsInteractable(),
            "continuing the same held drag reaches the signature and enables signing at " + size);
        AdvanceLoanScroll(.8f);
        Check(scroll.verticalNormalizedPosition <= .015f,
            "releasing the handle does not snap back to the previous wheel target at " + size);

        drag = GrabHandle(); DragHandle(drag, 1); ReleaseHandle(drag); AdvanceLoanScroll(.6f);
        Check(scroll.verticalNormalizedPosition >= .985f && !Get<Button>(hub, "_bankLoanAgreeButton").IsInteractable(),
            "dragging the handle upward restores the beginning and closes the signature gate at " + size);
        Check(Get<int>(hub, "_bankAmount") == amount && PlayerHubRuntime.Current.Bank.Offer?.Id == offerId,
            "scrollbar interaction preserves the principal and offer at " + size);
        await NextFrame();
    }

    private static async Task CheckBankContractScroll(string size)
    {
        var suffix = size == "1600x900" ? "" : "-" + size;
        await ScrollLoanContract(false);
        Check(!Get<Button>(hub, "_bankLoanAgreeButton").IsInteractable(), "the contract cannot be signed before scrolling to its end");
        Check(Get<ScrollRect>(hub, "_bankContractScroll").content.rect.height > Get<ScrollRect>(hub, "_bankContractScroll").viewport.rect.height * 2, "the complete contract uses scrollable readable paper");
        if (size == "1600x900")
        {
            await CheckContractDrag();
            await CheckContractGentleWheel();
        }
        await CheckContractScrollbar(size);
        Save("hub-bank-contract-top" + suffix + ".png");
        Save("hub-bank-contract" + suffix + ".png");
        InspectLoanPaper(size + " top");
        var parties = Get<Text>(hub, "_bankLoanPartiesText");
        Check(parties.text.Contains("АО «Банк Тузяры»") && parties.text.Contains("Устава и личного авторитета")
            && parties.text.Contains("Тестовый игрок") && parties.text.Contains("настоящий Договор"), "the complete contracting-party preamble is printed");
        Check(LoanPlain(Get<Text>(hub, "_bankBookRate").text).Contains("20%"), "the new contract states the twenty-percent rate");
        CheckLoanRedInk(Get<Text>(hub, "_bankLoanAmountText"), "principal");
        CheckLoanRedInk(Get<Text>(hub, "_bankLoanReturnText"), "total repayment");
        CheckLoanRedInk(Get<Text>(hub, "_bankLoanDueText"), "repayment deadline");
        var early = Get<Text>(hub, "_bankBookEarly");
        Check(LoanPlain(early.text).Contains("первые сутки") && LoanPlain(early.text).Contains("10%"), "the full first-day ten-percent discount clause is present");
        var circle = Get<RectTransform>(hub, "_bankBookEarlyCircle");
        var ink = circle.GetComponentsInChildren<Image>();
        Check(circle.gameObject.activeInHierarchy && ink.Length >= 40 && ink.All(i => i.color.b > i.color.r * 2 && i.color.a > .5f),
            "the early repayment clause has a visible blue pen circle");
        // Пометку «Гаси сразу!» автор убрал (2026-10-08).
        Check(!hub.GetComponentsInChildren<Text>(true).Any(t => t.text == "Гаси сразу!"), "no handwritten repayment reminder on the contract");
        FocusLoanClause(early.rectTransform);
        Save("hub-bank-contract-terms" + suffix + ".png"); InspectLoanPaper(size + " terms");
        await ScrollLoanContract(true);
        Save("hub-bank-contract-signature" + suffix + ".png");
        InspectLoanPaper(size + " signature");
        Check(Get<Button>(hub, "_bankLoanAgreeButton").IsInteractable(), "the signature at the end offers signing for a valid exact amount");
        Check(Get<Text>(hub, "_bankBookRepayment").text.Contains("от 1") && Get<Text>(hub, "_bankBookFreedom").text.Contains("свободен"),
            "the contract prints partial payments from one coin and freedom of stakes before the deadline");
        Check(Get<Text>(hub, "_bankBookVictory").text.Contains("однократно") && Get<Text>(hub, "_bankBookCollection").text.Contains("50%")
            && Get<Text>(hub, "_bankBookCollection").text.Contains("истечении продления"), "the complete extension and collection terms are printed");
        Check(LoanPlain(Get<Text>(hub, "_bankBookJoke").text) == "4.4. Банк ответственности не несёт. Никогда.", "the bank's closing joke remains in the full contract");
        var content = Get<RectTransform>(hub, "_bankContractContent");
        var underlines = content.GetComponentsInChildren<Image>(true).Where(i => i.name == "Condition underline").ToArray();
        Check(underlines.Length == 3 && underlines.All(line => line.gameObject.activeInHierarchy && line.color.a > .5f && line.rectTransform.rect.width > 200),
            "the three important conditions retain their visible underlines");
        Save("hub-bank-contract-signature" + suffix + ".png");
        await ScrollLoanContract(false);
    }

    private static void CheckLoanRedInk(Text label, string description)
    {
        FocusLoanClause(label.rectTransform);
        Canvas.ForceUpdateCanvases(); camera.Render();
        var colors = label.canvasRenderer.GetMesh().colors32;
        Check(colors.Any(c => c.a > 100 && c.r > 80 && c.r > c.g * 3 && c.r > c.b * 3),
            "rendered " + description + " includes the emphasized red ink");
    }

    private static async Task CheckSignedBankContractAndReceipts()
    {
        var signed = PlayerHubRuntime.Current.Bank.Contract;
        Check(signed != null && signed.Principal == 347 && signed.TotalDebt == 417 && signed.TermsVersion == 2,
            "approval persists the full terms of the signed twenty-percent contract");
        var wallet = PlayerHubRuntime.Current.Profile.Coins;
        var renamed = await PlayerHubRuntime.ExecuteAsync(new HubCommand { Action = "profile_update", Text = "Имя после подписания" });
        Check(renamed.Success, "read-only contract fixture changes the current profile name");
        await BankLoanClick("_bankLoanReadButton", "reread the signed contract"); BankLoanStep(.5f);
        Check(Get<bool>(hub, "_bankContractReadOnly") && Get<ScrollRect>(hub, "_bankContractScroll").verticalNormalizedPosition >= .985f,
            "My contract opens the signed document at its beginning");
        Check(Get<Text>(hub, "_bankLoanPartiesText").text.Contains("Тестовый игрок")
            && !Get<Text>(hub, "_bankLoanPartiesText").text.Contains("Имя после подписания"), "signed contract preserves the original borrower despite later profile changes");
        Check(!Get<InputField>(hub, "_bankLoanAmountInput").interactable && Get<List<Button>>(hub, "_bankLoanAmountButtons").All(b => !b.gameObject.activeInHierarchy),
            "signed principal is read-only and no amount selectors remain");
        Save("hub-bank-contract-signed.png");
        await ScrollLoanContract(true);
        Check(!Get<Button>(hub, "_bankLoanAgreeButton").gameObject.activeInHierarchy
            && Get<Button>(hub, "_bankBookReceiptButton").IsInteractable(), "signed contract end offers its receipt without a second signing action");
        Check(PlayerHubRuntime.Current.Profile.Coins == wallet && PlayerHubRuntime.Current.Bank.Contract.Id == signed.Id,
            "rereading and scrolling a signed contract neither issues money nor replaces the contract");
        Check(Get<Text>(hub, "_bankBookBorrower").text == "ЗАЁМЩИК:" && Get<Text>(hub, "_bankLoanSignature").text == signed.Borrower,
            "the signature area shows the borrower name only once");
        var stamp = Get<RectTransform>(hub, "_bankLoanStamp");
        Check(stamp.localScale.x >= .75f && stamp.anchoredPosition.x > 300, "the signed stamp is larger and placed to the right");
        Save("hub-bank-contract-signed-signature.png"); InspectLoanPaper("signed signature");
        await BankLoanClick("_bankBookReceiptButton", "open repayment receipt from the signed contract"); BankLoanStep(.5f);
        Check(Get<bool>(hub, "_bankReceiptShown") && !Get<bool>(hub, "_bankContractShown") && Get<bool>(hub, "_bankLoanShown"),
            "repayment opens over the desk and closes only the book");
        Check(HubLoans.PayoffQuote(PlayerHubRuntime.Current.Bank, clock.Now).Amount == 382, "first-day payoff quote lowers the 417-coin balance to 382");
        var input = Get<InputField>(hub, "_bankReceiptAmountInput");
        Check(input.text == "382", "repayment initially offers the exact discounted full balance");
        await TypeLoanAmount(input, "0");
        Check(!Get<Button>(hub, "_bankReceiptPayButton").interactable, "a zero-coin repayment is not payable");
        await TypeLoanAmount(input, "9999999");
        Check(!Get<Button>(hub, "_bankReceiptPayButton").interactable, "repayment cannot exceed the available balance or payoff quote");
        await TypeLoanAmount(input, "37");
        Check(input.text == "37" && Get<Button>(hub, "_bankReceiptPayButton").IsInteractable(), "a thirty-seven-coin partial payment can be entered exactly");
        Check(Get<Text>(hub, "_bankReceiptRemaining").text == "380", "partial repayment preview subtracts exactly thirty-seven coins");
        Check(Get<Text>(hub, "_bankReceiptProgressText").text.Contains("После оплаты")
            && Mathf.Abs(Get<Image>(hub, "_bankReceiptProgress").fillAmount - 37f / 417) < .001f,
            "repayment preview shows thirty-seven of 417 coins before the payment is committed");
        Save("hub-bank-repayment.png"); InspectBankReceipt("1600x900");
        ResizeCanvas(2000, 900); ((RectTransform)hub.transform).sizeDelta = new Vector2(2000, 900); Call(hub, "Resize"); BankLoanStep(.02f);
        Save("hub-bank-repayment-2000x900.png"); InspectBankReceipt("2000x900");
        ResizeCanvas(1600, 900); ((RectTransform)hub.transform).sizeDelta = new Vector2(1600, 900); Call(hub, "Resize"); BankLoanStep(.02f);
        await BankLoanClick("_bankReceiptPayButton", "pay exactly thirty-seven coins");
        await CheckBankRepaymentFlight(wallet, 37, "partial");
        var partial = PlayerHubRuntime.Current;
        var first = partial.Bank.Payments.Last();
        Check(partial.Profile.Coins == wallet - 37 && partial.Bank.Repaid == 37 && partial.Bank.Debt == 380,
            "partial payment reduces wallet and debt by exactly its entered amount");
        Check(first.Amount == 37 && first.DebtAfter == 380 && first.Discount == 0 && !first.Closed,
            "partial payment writes a permanent receipt without applying a premature discount");
        Check(Get<Text>(hub, "_bankReceiptStampText").text == "ОПЛАЧЕНО", "partial payment stamps its receipt as paid");
        Save("hub-bank-receipt-partial.png"); InspectBankReceipt("partial receipt");
        await BankLoanClick("_bankReceiptNextPaymentButton", "make a further repayment"); BankLoanStep(.1f);
        Check(input.text == "345" && HubLoans.PayoffQuote(PlayerHubRuntime.Current.Bank, clock.Now).Discount == 35,
            "after the partial payment the early payoff quote is 345 with a 35-coin discount");
        await TypeLoanAmount(input, "0");
        Check(Get<Button>(hub, "_bankReceiptArchiveButton").gameObject.activeInHierarchy, "saved receipts remain accessible while the next payment has an invalid amount");
        ResizeCanvas(960, 540); ((RectTransform)hub.transform).sizeDelta = new Vector2(960, 540); Call(hub, "Resize"); BankLoanStep(.02f);
        Save("hub-bank-repayment-invalid-960x540.png"); InspectBankReceipt("invalid amount 960x540");
        var hint = Get<Text>(hub, "_bankReceiptAfter");
        var receiptPaper = Get<RectTransform>(hub, "_bankReceiptPaper");
        var hintBottom = receiptPaper.InverseTransformPoint(hint.rectTransform.TransformPoint(hint.canvasRenderer.GetMesh().bounds.min)).y;
        var archiveTop = RectTransformUtility.CalculateRelativeRectTransformBounds(receiptPaper, Get<Button>(hub, "_bankReceiptArchiveButton").transform).max.y;
        Check(hintBottom >= archiveTop + 8, "payment validation glyphs leave a visible gap above the receipts button");
        ResizeCanvas(1600, 900); ((RectTransform)hub.transform).sizeDelta = new Vector2(1600, 900); Call(hub, "Resize"); BankLoanStep(.02f);
        Save("hub-bank-repayment-invalid.png");
        await TypeLoanAmount(input, "345");
        await BankLoanClick("_bankReceiptPayButton", "settle the first-day discounted balance");
        await CheckBankRepaymentFlight(wallet - 37, 345, "paid");
        var closed = PlayerHubRuntime.Current;
        var last = closed.Bank.Payments.Last();
        Check(closed.Profile.Coins == wallet - 382 && closed.Bank.Debt == 0 && closed.Bank.Repaid == 382 && closed.Bank.Contract.Status == "paid",
            "full first-day repayment closes the contract at ten-percent total interest");
        Check(last.Amount == 345 && last.Discount == 35 && last.Closed && last.Early && closed.Bank.Payments.Count == 2,
            "discounted settlement creates its second immutable receipt");
        Check(Get<Text>(hub, "_bankReceiptStampText").text == "ПОГАШЕНО" && Get<Image>(hub, "_bankReceiptProgress").fillAmount > .999f,
            "closed contract receives its settled stamp and full repayment progress");
        Save("hub-bank-receipt-paid.png"); InspectBankReceipt("settled receipt");
        await BankLoanClick("_bankReceiptPreviousButton", "read the earlier partial-payment receipt"); BankLoanStep(.1f);
        Check(Get<string>(hub, "_bankReceiptSelectedId") == first.Id && LoanDigits(Get<Text>(hub, "_bankReceiptRemaining").text) == "380",
            "receipt archive retains the earlier outstanding balance after the loan is closed");
        await BankLoanClick("_bankReceiptNextButton", "return to the final receipt"); BankLoanStep(.1f);
        Check(Get<string>(hub, "_bankReceiptSelectedId") == last.Id && LoanDigits(Get<Text>(hub, "_bankReceiptRemaining").text) == "0",
            "archive navigation restores the final zero-balance receipt");
        Check(Get<float>(hub, "_bankRepayTime") < 0 && Get<RectTransform[]>(hub, "_bankRepayCoins").All(c => !c.gameObject.activeInHierarchy),
            "reading saved receipts never starts another repayment flight");
        await CloseLoanReceipt();
        CheckBankDeskActions(false, "after full repayment");
        Check(PlayerHubRuntime.Current.Bank.Contract.Id == signed.Id && PlayerHubRuntime.Current.Bank.Payments.Count == 2,
            "removing the settled contract action preserves its saved contract and payment receipts");
        Call(hub, "ShowSignedBankContract"); BankLoanStep(.1f);
        Check(!Get<bool>(hub, "_bankContractShown"), "a settled contract cannot be opened through the active-contract entry point");
        Save("hub-bank-desk-repaid.png");
        ResizeCanvas(2000, 900); ((RectTransform)hub.transform).sizeDelta = new Vector2(2000, 900); Call(hub, "Resize"); BankLoanStep(.02f);
        CheckBankDeskActions(false, "after full repayment at 2000x900");
        Save("hub-bank-desk-repaid-2000x900.png");
        ResizeCanvas(1600, 900); ((RectTransform)hub.transform).sizeDelta = new Vector2(1600, 900); Call(hub, "Resize"); BankLoanStep(.02f);
        await BankBack();
        await OpenLoanDeskFromBoss();
        CheckBankDeskActions(false, "after reopening a settled bank desk");
        await BankLoanClick("_bankLoanTakeButton", "request the next loan after repayment"); BankLoanStep(.5f);
        Check(Get<bool>(hub, "_bankContractShown") && !Get<bool>(hub, "_bankContractReadOnly")
            && PlayerHubRuntime.Current.Bank.Offer.Number == signed.Number + 1, "the centered button opens the next unsigned contract after repayment");
        Call(hub, "CloseBankLoan"); BankLoanStep(.5f);
        CheckBankDeskActions(false, "after declining the next unsigned contract");
        await BankBack();
    }

    private static async Task CheckBankRepaymentFlight(int walletBefore, int debit, string capture)
    {
        var accepted = PlayerHubRuntime.Current;
        var count = accepted.Bank.Payments.Count;
        var paymentId = accepted.Bank.Payments.Last().Id;
        BankLoanStep(.02f);
        Check(Get<float>(hub, "_bankRepayTime") >= 0 && Get<int>(hub, "_bankRepayWalletFrom") == walletBefore
            && Get<int>(hub, "_bankRepayWalletTo") == walletBefore - debit,
            "successful repayment animates only the actual wallet debit: " + capture);
        Check(LoanDigits(Get<Text>(hub, "_bankLoanWalletText").text) == walletBefore.ToString(),
            "wallet keeps its starting value before the coins reach the bank: " + capture);
        Check(!Get<CanvasGroup>(hub, "_bankReceiptGroup").interactable,
            "receipt controls are blocked during the repayment flight: " + capture);
        Call(hub, "CloseBankReceipt"); Call(hub, "CloseBankLoan");
        await (Task)Call(hub, "PayBankReceiptAsync");
        Check(Get<bool>(hub, "_bankReceiptShown") && Get<bool>(hub, "_bankLoanShown")
            && PlayerHubRuntime.Current.Bank.Payments.Count == count && PlayerHubRuntime.Current.Profile.Coins == walletBefore - debit,
            "Back and repeated payment cannot interrupt or charge the accepted transfer twice: " + capture);
        BankLoanStep(.25f);
        var coins = Get<RectTransform[]>(hub, "_bankRepayCoins");
        var first = coins.First(c => c.gameObject.activeInHierarchy);
        var layer = Get<RectTransform>(hub, "_bankLoanLayer");
        var wallet = Get<RectTransform>(hub, "_bankLoanWallet");
        var origin = (Vector2)layer.InverseTransformPoint(wallet.TransformPoint(new Vector3(-104, 0, 0))) - layer.rect.center;
        var initial = (Vector2)layer.InverseTransformPoint(first.position) - layer.rect.center;
        var bank = new Vector2(0, -220);
        Check(Vector2.Distance(initial, origin) < Vector2.Distance(initial, bank),
            "repayment coins start beside the actual wallet: " + capture);
        Check(Get<RectTransform>(hub, "_bankReceiptPaper").anchoredPosition.y < -400,
            "receipt moves aside so the banker can receive the repayment: " + capture);
        var effects = Get<RectTransform>(hub, "_bankRepayEffects");
        Check(effects.GetSiblingIndex() > Get<RectTransform>(hub, "_bankReceiptLayer").GetSiblingIndex()
            && coins.Where(c => c.gameObject.activeInHierarchy).All(c => !c.GetComponent<Image>().raycastTarget),
            "repayment coins render above the receipt without catching taps: " + capture);
        BankLoanStep(.38f);
        var later = (Vector2)layer.InverseTransformPoint(first.position) - layer.rect.center;
        Check(first.gameObject.activeInHierarchy && Vector2.Distance(later, bank) < Vector2.Distance(initial, bank),
            "flying coins travel from the wallet toward the bank desk: " + capture);
        Save("hub-bank-repayment-flight-" + capture + ".png");
        BankLoanStep(.30f);
        var shown = int.Parse(LoanDigits(Get<Text>(hub, "_bankLoanWalletText").text));
        Check(shown < walletBefore && shown > walletBefore - debit,
            "wallet visibly counts down as repayment coins arrive: " + capture);
        for (var elapsed = 0f; Get<float>(hub, "_bankRepayTime") >= 0 && elapsed < 5; elapsed += .1f) BankLoanStep(.1f);
        BankLoanStep(.35f);
        Check(Get<float>(hub, "_bankRepayTime") < 0 && coins.All(c => !c.gameObject.activeInHierarchy)
            && LoanDigits(Get<Text>(hub, "_bankLoanWalletText").text) == (walletBefore - debit).ToString(),
            "repayment finishes at the saved wallet balance with no remaining particles: " + capture);
        Check(Get<CanvasGroup>(hub, "_bankReceiptGroup").interactable
            && Get<RectTransform>(hub, "_bankReceiptPaper").anchoredPosition.sqrMagnitude < .01f,
            "receipt returns and its controls unlock after the bank receives the payment: " + capture);
        Check(PlayerHubRuntime.Current.Bank.Payments.Count == count && Get<string>(hub, "_bankReceiptSelectedId") == paymentId,
            "animation displays the same immutable accepted receipt: " + capture);
    }

    private static async Task CheckBankRepaymentMotionPreferences()
    {
        await BankLoanProfile(2, 20);
        var issued = await PlayerHubRuntime.ExecuteAsync(new HubCommand { Action = "bank_request", Amount = 100 });
        var preference = await PlayerHubRuntime.ExecuteAsync(new HubCommand { Action = "settings_update", Target = "reduced_motion", Flag = true });
        Check(issued.Success && preference.Success, "reduced-motion repayment fixture has a signed loan");
        await OpenLoanDeskFromBoss();
        await BankLoanClick("_bankLoanDebtButton", "open reduced-motion repayment"); BankLoanStep(.3f);
        var input = Get<InputField>(hub, "_bankReceiptAmountInput");
        await TypeLoanAmount(input, "0");
        await (Task)Call(hub, "PayBankReceiptAsync"); BankLoanStep(.1f);
        Check(Get<float>(hub, "_bankRepayTime") < 0 && PlayerHubRuntime.Current.Bank.Payments.Count == 0,
            "an invalid payment produces neither a transfer animation nor a saved debit");
        await TypeLoanAmount(input, "1");
        var before = PlayerHubRuntime.Current.Profile.Coins;
        await BankLoanClick("_bankReceiptPayButton", "repay one coin with reduced motion"); BankLoanStep(.4f);
        Check(PlayerHubRuntime.Current.Profile.Coins == before - 1 && Get<float>(hub, "_bankRepayTime") < 0
            && Get<RectTransform[]>(hub, "_bankRepayCoins").All(c => c == null || !c.gameObject.activeInHierarchy),
            "reduced motion saves the one-coin payment without flying particles");
        Check(LoanDigits(Get<Text>(hub, "_bankLoanWalletText").text) == (before - 1).ToString()
            && Get<CanvasGroup>(hub, "_bankReceiptGroup").interactable
            && Get<RectTransform>(hub, "_bankReceiptPaper").anchoredPosition.sqrMagnitude < .01f,
            "reduced motion shows the final balance and stationary readable receipt immediately");
        Save("hub-bank-repayment-reduced-motion.png");
        preference = await PlayerHubRuntime.ExecuteAsync(new HubCommand { Action = "settings_update", Target = "reduced_motion", Flag = false });
        Check(preference.Success, "normal repayment motion can be restored");
        await BankLoanClick("_bankReceiptNextPaymentButton", "make repayment before leaving the desk"); BankLoanStep(.1f);
        await TypeLoanAmount(input, "10");
        await BankLoanClick("_bankReceiptPayButton", "repay ten coins before navigation"); BankLoanStep(.3f);
        var paid = PlayerHubRuntime.Current.Profile.Coins;
        hub.Navigate("profile"); BankLoanStep(.1f);
        Check(Get<float>(hub, "_bankRepayTime") < 0 && Get<RectTransform[]>(hub, "_bankRepayCoins").All(c => !c.gameObject.activeInHierarchy)
            && !Get<bool>(hub, "_bankReceiptShown") && PlayerHubRuntime.Current.Profile.Coins == paid,
            "leaving the bank clears transient particles while retaining its already saved payment");
        // EditMode does not advance Update while awaiting an editor frame. Finish the ordinary desk fade before tapping the city.
        BankLoanStep(.4f);
        Check(!Get<RectTransform>(hub, "_bankLoanLayer").gameObject.activeSelf,
            "the outgoing bank backdrop finishes its fade before the city is entered again");
        hub.GoHome(); await NextFrame(); DiveNow(0); DiveNow(4); await OpenLoanDeskFromBoss();
        Check(LoanDigits(Get<Text>(hub, "_bankLoanWalletText").text) == paid.ToString(),
            "returning to the banker displays the persisted balance after an interrupted animation");
        await BankBack();
    }

    private static void CheckBankDeskActions(bool activeLoan, string context)
    {
        var take = Get<Button>(hub, "_bankLoanTakeButton");
        var debt = Get<Button>(hub, "_bankLoanDebtButton");
        var read = Get<Button>(hub, "_bankLoanReadButton");
        Check(take.gameObject.activeInHierarchy == !activeLoan && debt.gameObject.activeInHierarchy == activeLoan
            && read.gameObject.activeInHierarchy == activeLoan, "desk actions match the active signed loan " + context);
        Check(!Get<Button>(hub, "_bankCollectorsButton").gameObject.activeInHierarchy, "no collector action without overdue debt " + context);
        if (activeLoan) return;
        var layer = Get<RectTransform>(hub, "_bankLoanLayer");
        var rect = (RectTransform)take.transform;
        var centre = layer.InverseTransformPoint(rect.TransformPoint(rect.rect.center));
        Check(Mathf.Abs(centre.x - layer.rect.center.x) < .1f && take.IsInteractable(),
            "the only loan action is centered and clickable " + context);
    }

    private static async Task CloseLoanReceipt()
    {
        var layer = Get<RectTransform>(hub, "_bankReceiptLayer");
        await ClickButton(layer.GetComponentsInChildren<Button>().Single(b => ButtonLabel(b) == "К СТОЛУ"), "return from receipt to banker");
        BankLoanStep(.4f);
        Check(!Get<bool>(hub, "_bankReceiptShown") && Get<bool>(hub, "_bankLoanShown") && Get<int>(hub, "_cityLevel") == 4,
            "receipt Back leaves the banker desk open and does not leave the bank");
    }

    private static void InspectBankReceipt(string size)
    {
        Canvas.ForceUpdateCanvases(); camera.Render();
        var paper = Get<RectTransform>(hub, "_bankReceiptPaper");
        var parchment = paper.Find("Receipt parchment")?.GetComponent<Image>();
        BankLayoutCheck(parchment != null && parchment.sprite != null && parchment.sprite.texture.width >= 1536,
            "receipt uses HD parchment behind its crisp native frame at " + size);
        Check(paper.GetComponent<Image>().sprite == null, "the receipt frame is rendered natively without enlarging a bitmap at " + size);
        var canvas = (RectTransform)canvasObject.transform;
        var corners = new Vector3[4]; paper.GetWorldCorners(corners);
        BankLayoutCheck(corners.All(c => canvas.rect.Contains(canvas.InverseTransformPoint(c))), "repayment paper fits the viewport at " + size);
        foreach (var label in paper.GetComponentsInChildren<Text>().Where(t => t.gameObject.activeInHierarchy && !string.IsNullOrEmpty(t.text)))
        {
            var mesh = label.canvasRenderer.GetMesh(); var rect = label.rectTransform.rect; var bounds = mesh.bounds;
            BankLayoutCheck(mesh.vertexCount > 0 && bounds.min.x >= rect.xMin - 2 && bounds.max.x <= rect.xMax + 2
                && bounds.min.y >= rect.yMin - 2 && bounds.max.y <= rect.yMax + 2, "receipt glyphs fit at " + size + ": " + label.name);
            label.rectTransform.GetWorldCorners(corners);
            BankLayoutCheck(corners.All(c => paper.rect.Contains(paper.InverseTransformPoint(c))), "receipt text stays on its paper at " + size + ": " + label.name);
        }
    }

    private static string LoanPlain(string value) => Regex.Replace(value ?? "", "<[^>]*>", "");
    private static string LoanDigits(string value) => new string(LoanPlain(value).Where(char.IsDigit).ToArray());

    private static void CheckLoanReturn(int amount, string trick)
    {
        var expected = HubLoans.Debt(amount, trick).ToString();
        var text = Get<Text>(hub, "_bankLoanReturnText").text.Split('\n')[0];
        Check(LoanDigits(text) == "23" + expected, "repayment clause matches the selected amount and service terms");
    }

    private static void InspectLoanPaper(string size)
    {
        Canvas.ForceUpdateCanvases(); camera.Render();
        var paper = Get<RectTransform>(hub, "_bankContract");
        var content = Get<RectTransform>(hub, "_bankContractContent");
        var viewport = Get<RectTransform>(hub, "_bankContractViewport");
        var canvas = (RectTransform)canvasObject.transform;
        BankLayoutCheck(Quaternion.Angle(paper.localRotation, Quaternion.identity) < .01f, "contract remains straight at " + size);
        Check(paper.rect.width >= 1180, "contract uses wide readable paper at " + size);
        var body = Get<Text>(hub, "_bankLoanPartiesText");
        var bodyEm = Vector2.Distance(RectTransformUtility.WorldToScreenPoint(camera, body.transform.position),
            RectTransformUtility.WorldToScreenPoint(camera, body.transform.TransformPoint(Vector3.up * body.fontSize)));
        Check(body.fontSize >= 30 && bodyEm >= 18, "scroll contract body keeps its larger font on the actual screen at " + size);
        var corners = new Vector3[4]; paper.GetWorldCorners(corners);
        BankLayoutCheck(corners.All(c => canvas.rect.Contains(canvas.InverseTransformPoint(c))), "contract viewport fits the screen at " + size);
        Check(viewport.GetComponent<RectMask2D>() != null || viewport.GetComponent<Mask>() != null,
            "the long document is clipped to its paper viewport at " + size);
        var labels = paper.GetComponentsInChildren<Text>().Where(t => t.gameObject.activeInHierarchy && !string.IsNullOrEmpty(t.text)).ToArray();
        BankLayoutCheck(labels.Any(t => LoanPlain(t.text).StartsWith("1.1.")) && labels.Any(t => LoanPlain(t.text).StartsWith("4.4.")),
            "all four sections remain in the same continuous document at " + size);
        foreach (var label in labels)
        {
            var rect = label.rectTransform.rect;
            var settings = label.GetGenerationSettings(new Vector2(rect.width, 0));
            var required = label.cachedTextGeneratorForLayout.GetPreferredHeight(label.text, settings) / label.pixelsPerUnit;
            BankLayoutCheck(required <= rect.height + 2, "contract text is not vertically truncated at " + size + ": " + label.name);
            var inContent = label.transform.IsChildOf(content);
            if (inContent)
            {
                label.rectTransform.GetWorldCorners(corners);
                BankLayoutCheck(corners.All(c => content.rect.Contains(content.InverseTransformPoint(c))),
                    "contract text stays inside its continuous paper at " + size + ": " + label.name);
            }
            if (!BankContractCentreVisible(label.rectTransform)) continue;
            var mesh = label.canvasRenderer.GetMesh(); var bounds = mesh.bounds;
            BankLayoutCheck(mesh.vertexCount > 0, "visible contract text has glyphs at " + size + ": " + label.name);
            BankLayoutCheck(bounds.min.x >= rect.xMin - 2 && bounds.max.x <= rect.xMax + 2 && bounds.min.y >= rect.yMin - 2 && bounds.max.y <= rect.yMax + 2,
                "contract glyphs fit their text box at " + size + ": " + label.name + " (" + bounds + " in " + rect + ")");
        }
        foreach (var button in paper.GetComponentsInChildren<Button>().Where(b => b.gameObject.activeInHierarchy && b.IsInteractable()))
        {
            if (!BankContractCentreVisible((RectTransform)button.transform)) continue;
            var hit = Hit((RectTransform)button.transform);
            BankLayoutCheck(hit.gameObject != null && (hit.gameObject == button.gameObject || hit.gameObject.transform.IsChildOf(button.transform)),
                "visible contract button receives its own tap at " + size + ": " + ButtonLabel(button));
        }
    }

    private static bool BankContractCentreVisible(RectTransform target)
    {
        var viewport = Get<RectTransform>(hub, "_bankContractViewport");
        var content = Get<RectTransform>(hub, "_bankContractContent");
        return !target.IsChildOf(content) || viewport.rect.Contains(viewport.InverseTransformPoint(target.TransformPoint(target.rect.center)));
    }

}

// The field receives explicit keyboard events; these providers only supply an empty IME state.
// This avoids reading legacy Input in the Input-System-only QA project.
public sealed class BankLoanQaInput : BaseInput
{
    public override string compositionString => "";
    public override IMECompositionMode imeCompositionMode { get; set; }
    public override Vector2 compositionCursorPos { get; set; }
}

public sealed class BankLoanQaInputModule : BaseInputModule
{
    public override void Process() { }
}
