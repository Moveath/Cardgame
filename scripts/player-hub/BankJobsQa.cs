// Companion partial for PlayerHubQa.cs. Tuzyara jobs and roof-card preview; memory-only profiles.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CardGame.Core.Meta;
using CardGameClient;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static partial class PlayerHubQa
{
    private static readonly List<string> BankJobsLayoutIssues = new List<string>();

    private static async Task CheckBankJobs()
    {
        var previousStore = store; var previousClock = clock;
        BankJobsLayoutIssues.Clear();
        try
        {
            await BankLoanProfile(2, 20);
            hub.GoHome(); await NextFrame(); DiveNow(0); DiveNow(4);
            await OpenLoanDeskFromBoss();
            var board = PlayerHubRuntime.Current.BankJobs;
            Check(board.Jobs.Count == 3 && board.Jobs.Select(j => j.Template).Distinct().Count() == 3,
                "Tuzyara starts with three distinct personal cases");
            Check(board.RefreshAt == clock.Now + HubBankJobs.WeeklySeconds,
                "personal cases receive seven complete days");
            Check(Get<Button>(hub, "_bankJobsDeskButton").gameObject.activeInHierarchy
                && Get<Button>(hub, "_bankProtectionDeskButton").gameObject.activeInHierarchy,
                "the folder and protection card are visible objects on Tuzyara's desk");
            await ClickButton(Get<Button>(hub, "_bankJobsDeskButton"), "open Tuzyara's case folder");
            JobsQaStep(.5f);
            Check(Get<bool>(hub, "_bankJobsShown") && PlayerHubRuntime.Current.BankJobs.Jobs.All(j => j.Seen),
                "opening the folder marks its three cases seen");
            Check(JobEnvelopeViews().Length == 3, "the folder renders three separately selectable envelopes");
            var beforeReading = PlayerHubRuntime.Current.Profile.Coins;
            foreach (var size in new[] { new Vector2Int(1600, 900), new Vector2Int(2000, 900), new Vector2Int(1280, 720), new Vector2Int(960, 540) })
            {
                ResizeCanvas(size.x, size.y); ((RectTransform)hub.transform).sizeDelta = size; Call(hub, "Resize");
                JobsQaStep(.1f);
                var suffix = size.x + "x" + size.y;
                InspectBankJobsLayer(Get<RectTransform>(hub, "_bankJobsLayer"), "envelopes " + suffix);
                Save("hub-bank-jobs-" + suffix + ".png");
                foreach (var jobId in PlayerHubRuntime.Current.BankJobs.Jobs.Select(j => j.Id).ToArray())
                {
                    await OpenJobNoteQa(jobId);
                    Check(Get<RectTransform>(hub, "_bankJobNote").gameObject.activeInHierarchy,
                        "an envelope opens one readable case note at " + suffix);
                    Check(!Get<RectTransform>(hub, "_bankJobsSelection").gameObject.activeInHierarchy,
                        "the three envelopes leave room for the selected note at " + suffix);
                    InspectBankJobsLayer(Get<RectTransform>(hub, "_bankJobsLayer"), "case " + JobQa(jobId).Template + " " + suffix);
                    if (JobQa(jobId).Template == "catch_cheats") Save("hub-bank-job-note-" + suffix + ".png");
                    await CloseJobNoteQa();
                }
            }
            Check(PlayerHubRuntime.Current.Profile.Coins == beforeReading && string.IsNullOrEmpty(PlayerHubRuntime.Current.BankJobs.ActiveJobId),
                "reading and declining case notes changes neither money nor acceptance");
            ResizeCanvas(1600, 900); ((RectTransform)hub.transform).sizeDelta = new Vector2(1600, 900); Call(hub, "Resize"); JobsQaStep(.1f);

            var first = PlayerHubRuntime.Current.BankJobs.Jobs.Single(j => j.Template == "named_opponent");
            await OpenJobNoteQa(first.Id); await JobPrimaryQa("accept the named-opponent case");
            Check(JobQa(first.Id).Accepted && PlayerHubRuntime.Current.BankJobs.ActiveJobId == first.Id,
                "accepting reserves the single active case slot");
            await CloseJobNoteQa();
            var other = PlayerHubRuntime.Current.BankJobs.Jobs.Single(j => j.Template == "catch_cheats");
            await OpenJobNoteQa(other.Id);
            Check(!Get<Button>(hub, "_bankJobPrimaryButton").IsInteractable(), "a second case cannot be accepted while the first is active");
            await CloseJobNoteQa();
            await ReloadJobsQa();
            Check(JobQa(first.Id).Accepted && PlayerHubRuntime.Current.BankJobs.ActiveJobId == first.Id,
                "accepted case survives Unity JSON serialization and a fresh service load");
            await OpenJobNoteQa(first.Id);
            await FinishJobMatchQa(first.Id, false, 0, 0, 0);
            Check(JobQa(first.Id).Progress == 0, "losing against the named opponent does not finish his case");
            await FinishJobMatchQa(first.Id, true, 0, 0, 0);
            Check(HubBankJobs.Ready(JobQa(first.Id)), "winning the dedicated match makes its case reward ready");
            await ClaimJobQa(first.Id);

            other = PlayerHubRuntime.Current.BankJobs.Jobs.Single(j => j.Template == "catch_cheats");
            await OpenJobNoteQa(other.Id); await JobPrimaryQa("accept the cheating investigation");
            await FinishJobMatchQa(other.Id, false, 1, 0, 0);
            Check(JobQa(other.Id).Progress == 1, "one verified bot catch reaches the case through the runtime evidence bridge");
            var oldRefresh = PlayerHubRuntime.Current.BankJobs.RefreshAt;
            var idleId = PlayerHubRuntime.Current.BankJobs.Jobs.Single(j => j.Template == "quiet_win").Id;
            clock.Now = oldRefresh + 1;
            var advanced = await PlayerHubRuntime.RefreshAsync(); Check(advanced.Success, "case week refresh saves successfully"); JobsQaStep(.2f);
            Check(JobQa(other.Id).Accepted && JobQa(other.Id).Progress == 1,
                "unfinished accepted work survives the weekly refresh");
            Check(PlayerHubRuntime.Current.BankJobs.Jobs.Single(j => j.Template == "quiet_win").Id != idleId,
                "unaccepted expired envelopes are replaced by the next week");
            await FinishJobMatchQa(other.Id, false, 0, 0, 0);
            Check(JobQa(other.Id).Progress == 1, "new match evidence starts empty and does not reuse the previous catch");
            await FinishJobMatchQa(other.Id, false, 1, 0, 0);
            Check(HubBankJobs.Ready(JobQa(other.Id)), "two real catches across finished matches complete the investigation");
            clock.Now = PlayerHubRuntime.Current.BankJobs.RefreshAt + 1;
            advanced = await PlayerHubRuntime.RefreshAsync(); Check(advanced.Success, "second case week refresh succeeds");
            await ReloadJobsQa();
            Check(HubBankJobs.Ready(JobQa(other.Id)) && PlayerHubRuntime.Current.BankJobs.ActiveJobId == other.Id,
                "an earned unclaimed case reward survives another week and a reload");
            await OpenJobNoteQa(other.Id); Save("hub-bank-job-ready.png");
            await ClaimJobQa(other.Id);

            var quiet = PlayerHubRuntime.Current.BankJobs.Jobs.Single(j => j.Template == "quiet_win");
            await OpenJobNoteQa(quiet.Id); await JobPrimaryQa("accept the quiet job");
            await FinishJobMatchQa(quiet.Id, true, 0, 0, 0);
            Check(JobQa(quiet.Id).Progress == 0, "an ordinary win cannot complete the quiet cheating job");
            await FinishJobMatchQa(quiet.Id, true, 0, 1, 1);
            Check(JobQa(quiet.Id).Progress == 0, "a caught cheating move prevents the quiet-job reward");
            await FinishJobMatchQa(quiet.Id, true, 0, 1, 0);
            Check(HubBankJobs.Ready(JobQa(quiet.Id)), "an undetected cheating move and victory complete the quiet job");
            await ClaimJobQa(quiet.Id);
            Call(hub, "HideBankJobs"); JobsQaStep(.4f);
            await CheckBankProtectionPreviewQa();
            Check(BankJobsLayoutIssues.Count == 0, "all personal case and protection-card layouts passed:\n" + string.Join("\n", BankJobsLayoutIssues));
        }
        finally
        {
            Call(hub, "HideBankJobs");
            if (Get<bool>(hub, "_bankProtectionShown")) Call(hub, "CloseBankProtection");
            Set(hub, "_bankLoanShown", false); Set(hub, "_bankContractShown", false); Set(hub, "_bankReceiptShown", false);
            BankLoanStep(1f);
            store = previousStore; clock = previousClock;
            PlayerHubRuntime.UseService(new LocalPlayerHubService(store, clock), clock);
            var restored = await PlayerHubRuntime.LoadAsync(); Check(restored.Success, "personal-case QA restores the preceding memory profile");
            Call(hub, "OnSnapshot", restored.Snapshot);
            ResizeCanvas(1600, 900); ((RectTransform)hub.transform).sizeDelta = new Vector2(1600, 900); Call(hub, "Resize");
            hub.GoHome(); DiveNow(0);
            System.IO.File.WriteAllLines(System.IO.Path.Combine(Output, "bank-jobs-layout-issues.txt"), BankJobsLayoutIssues);
        }
    }

    private static void JobsQaStep(float seconds)
    {
        BankLoanStep(seconds);
        Call(hub, "PaintBankJobs", 0f);
        Canvas.ForceUpdateCanvases();
    }

    private static object[] JobEnvelopeViews()
        => ((IEnumerable)Get<object>(hub, "_bankJobEnvelopes")).Cast<object>().ToArray();
    private static HubBankJob JobQa(string id) => PlayerHubRuntime.Current.BankJobs.Jobs.Single(j => j.Id == id);

    private static async Task OpenJobNoteQa(string id)
    {
        if (!Get<bool>(hub, "_bankJobsShown"))
        {
            Call(hub, "ReturnToBankJobs"); DriveMapTrip(() => Get<bool>(hub, "_bankLoanShown")); JobsQaStep(.5f); await NextFrame();
        }
        if (!string.IsNullOrEmpty(Get<string>(hub, "_bankJobSelectedId"))) await CloseJobNoteQa();
        var envelope = JobEnvelopeViews().Single(view => ((HubBankJob)Item(view, "Model")).Id == id);
        await ClickButton((Button)Item(envelope, "Button"), "open case " + JobQa(id).Name); JobsQaStep(.3f);
        Check(Get<string>(hub, "_bankJobSelectedId") == id, "the selected envelope opens its matching case");
    }

    private static async Task CloseJobNoteQa()
    {
        await ClickButton(Get<Button>(hub, "_bankJobBackButton"), "return from case note to the folder"); JobsQaStep(.3f);
        Check(Get<bool>(hub, "_bankJobsShown") && string.IsNullOrEmpty(Get<string>(hub, "_bankJobSelectedId")),
            "note Back leaves the folder open");
    }

    private static async Task JobPrimaryQa(string description)
    {
        await ClickButton(Get<Button>(hub, "_bankJobPrimaryButton"), description); JobsQaStep(.3f);
    }

    private static async Task FinishJobMatchQa(string id, bool won, int catches, int cheats, int caught)
    {
        await OpenJobNoteQa(id);
        var job = JobQa(id); var matches = PlayerHubRuntime.Current.Profile.Matches;
        await JobPrimaryQa("launch dedicated match for " + job.Name);
        var token = PlayerHubRuntime.Current.ActiveMatchId;
        var entry = PlayerHubRuntime.Current.Matches.Single(m => m.Id == token);
        Check(entry.BankJobId == id && launchedGame == job.Game && launchedCount == 2,
            "case launch reserves its own two-player match and reaches the game callback");
        Check(entry.Stake == 0 && entry.OpponentName == job.BotName
            && entry.GameSettings.AllowCheating == (job.Template != "named_opponent"),
            "dedicated match keeps its promised opponent, free entry and cheating rules");
        var names = new List<string> { "player", "bot" }; var portraits = new Sprite[2];
        Check(HubRoster.TryApply(job.Game, names, portraits) && names[1] == job.BotName && portraits[1] != null,
            "the dedicated opponent's name and portrait reach the game roster");
        HubMatchReporter.ReportDurakEvidence(catches, cheats, caught);
        HubMatchReporter.ReportMatchCompleted(job.Game, won);
        await WaitFor(() => PlayerHubRuntime.Current.Profile.Matches == matches + 1, "personal case result persists");
        Check(string.IsNullOrEmpty(PlayerHubRuntime.Current.ActiveMatchId) && !PlayerHubRuntime.HasPendingResult,
            "the case result settles and releases the active match");
        Check(await PlayerHubRuntime.ExitGameAsync(), "return from the finished case game succeeds");
        Call(hub, "ReturnToBankJobs"); DriveMapTrip(() => Get<bool>(hub, "_bankLoanShown")); JobsQaStep(.5f); await NextFrame();
        Check(Get<bool>(hub, "_bankJobsShown"), "finished case returns to Tuzyara's folder");
        await OpenJobNoteQa(id);
    }

    private static async Task ClaimJobQa(string id)
    {
        await OpenJobNoteQa(id);
        var job = JobQa(id); var before = PlayerHubRuntime.Current.Profile.Coins; var xp = PlayerHubRuntime.Current.Profile.XP;
        await JobPrimaryQa("claim completed case reward");
        Check(PlayerHubRuntime.Current.Profile.Coins == before + job.RewardCoins && PlayerHubRuntime.Current.Profile.XP == xp + job.RewardXP,
            "claim credits exactly the printed case coins and XP");
        Check(string.IsNullOrEmpty(PlayerHubRuntime.Current.BankJobs.ActiveJobId), "claim releases the slot for the next case");
        Save("hub-bank-job-closed-" + job.Template + ".png");
        JobsQaStep(2f);
        var paid = PlayerHubRuntime.Current.Profile.Coins; var paidXp = PlayerHubRuntime.Current.Profile.XP;
        var repeated = await PlayerHubRuntime.ExecuteAsync(new HubCommand { Action = "bank_job_claim", Target = id });
        Check(!repeated.Success && PlayerHubRuntime.Current.Profile.Coins == paid && PlayerHubRuntime.Current.Profile.XP == paidXp,
            "the same completed case cannot pay twice through a new request");
    }

    private static async Task ReloadJobsQa()
    {
        Check(string.IsNullOrEmpty(PlayerHubRuntime.Current.ActiveMatchId), "case reload fixture has no live table to forfeit");
        PlayerHubRuntime.UseService(new LocalPlayerHubService(store, clock), clock);
        var loaded = await PlayerHubRuntime.LoadAsync(); Check(loaded.Success, "fresh runtime reads only the injected memory store");
        Call(hub, "OnSnapshot", loaded.Snapshot); JobsQaStep(.2f);
    }

    private static async Task CheckBankProtectionPreviewQa()
    {
        var before = PlayerHubRuntime.Current.Profile.Coins; var debt = PlayerHubRuntime.Current.Bank.Debt;
        foreach (var size in new[] { new Vector2Int(1600, 900), new Vector2Int(2000, 900), new Vector2Int(1280, 720), new Vector2Int(960, 540) })
        {
            ResizeCanvas(size.x, size.y); ((RectTransform)hub.transform).sizeDelta = size; Call(hub, "Resize"); BankLoanStep(.1f);
            await ClickButton(Get<Button>(hub, "_bankProtectionDeskButton"), "read the protection card"); BankLoanStep(.5f);
            Check(Get<bool>(hub, "_bankProtectionShown"), "the black protection card opens its description");
            var layer = Get<RectTransform>(hub, "_bankProtectionLayer");
            InspectBankJobsLayer(layer, "protection preview " + size.x + "x" + size.y);
            var text = string.Join(" ", layer.GetComponentsInChildren<Text>().Where(t => t.gameObject.activeInHierarchy).Select(t => t.text));
            Check(text.IndexOf("кредит", StringComparison.OrdinalIgnoreCase) >= 0 && text.IndexOf("облав", StringComparison.OrdinalIgnoreCase) >= 0,
                "protection preview explains both honest loans and raid warnings");
            var actions = layer.GetComponentsInChildren<Button>().Where(b => b.gameObject.activeInHierarchy && b.IsInteractable()).ToArray();
            Check(actions.Length > 0 && actions.All(b => ButtonLabel(b) == "К СТОЛУ" || ButtonLabel(b) == "НАЗАД"),
                "unavailable protection offers only return actions and no purchase action");
            Save("hub-bank-protection-" + size.x + "x" + size.y + ".png");
            await ClickButton(actions[0], "close protection preview"); BankLoanStep(.5f);
            Check(!Get<bool>(hub, "_bankProtectionShown") && Get<bool>(hub, "_bankLoanShown"),
                "protection Back returns to the same banker desk");
        }
        Check(PlayerHubRuntime.Current.Profile.Coins == before && PlayerHubRuntime.Current.Bank.Debt == debt,
            "reading the future protection service neither charges money nor alters a signed debt");
    }

    private static void JobLayoutQa(bool condition, string message)
    {
        checks++;
        if (condition) return;
        BankJobsLayoutIssues.Add(message); LayoutIssues.Add(message);
        Debug.LogWarning("Bank jobs layout: " + message);
    }

    private static Rect JobsWorldRect(RectTransform target)
    {
        var corners = new Vector3[4]; target.GetWorldCorners(corners);
        return Rect.MinMaxRect(corners.Min(c => c.x), corners.Min(c => c.y), corners.Max(c => c.x), corners.Max(c => c.y));
    }

    private static void InspectBankJobsLayer(RectTransform layer, string context)
    {
        Canvas.ForceUpdateCanvases(); camera.Render();
        var rootCanvas = (RectTransform)canvasObject.transform;
        var corners = new Vector3[4];
        var controls = layer.GetComponentsInChildren<Button>().Where(b => b.gameObject.activeInHierarchy && b.IsInteractable()).ToArray();
        foreach (var button in controls)
        {
            var rect = (RectTransform)button.transform; rect.GetWorldCorners(corners);
            JobLayoutQa(corners.All(c => rootCanvas.rect.Contains(rootCanvas.InverseTransformPoint(c))),
                context + ": button stays on screen: " + ButtonLabel(button));
            foreach (var uv in new[] { new Vector2(.5f, .5f), new Vector2(.2f, .5f), new Vector2(.8f, .5f) })
            {
                var local = new Vector2(Mathf.Lerp(rect.rect.xMin, rect.rect.xMax, uv.x), Mathf.Lerp(rect.rect.yMin, rect.rect.yMax, uv.y));
                var point = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(local));
                var results = new List<RaycastResult>();
                canvasObject.GetComponent<GraphicRaycaster>().Raycast(new PointerEventData(events) { position = point }, results);
                JobLayoutQa(results.Count > 0 && (results[0].gameObject == button.gameObject || results[0].gameObject.transform.IsChildOf(button.transform)),
                    context + ": button receives its own tap at " + uv + ": " + ButtonLabel(button));
            }
        }
        for (var i = 0; i < controls.Length; i++)
            for (var j = i + 1; j < controls.Length; j++)
                JobLayoutQa(!JobsWorldRect((RectTransform)controls[i].transform).Overlaps(JobsWorldRect((RectTransform)controls[j].transform)),
                    context + ": action hit areas do not overlap: " + ButtonLabel(controls[i]) + " / " + ButtonLabel(controls[j]));
        var glyphs = new List<(Text Label, Rect Bounds)>();
        var labels = layer.GetComponentsInChildren<Text>().Where(t => t.gameObject.activeInHierarchy && !string.IsNullOrWhiteSpace(t.text) && t.color.a > .05f).ToArray();
        foreach (var label in labels)
        {
            var rect = label.rectTransform.rect; var mesh = label.canvasRenderer.GetMesh(); var bounds = mesh.bounds;
            var glyphMin = label.rectTransform.TransformPoint(bounds.min); var glyphMax = label.rectTransform.TransformPoint(bounds.max);
            glyphs.Add((label, Rect.MinMaxRect(Mathf.Min(glyphMin.x, glyphMax.x), Mathf.Min(glyphMin.y, glyphMax.y), Mathf.Max(glyphMin.x, glyphMax.x), Mathf.Max(glyphMin.y, glyphMax.y))));
            var settings = label.GetGenerationSettings(new Vector2(rect.width, 0));
            var needed = label.cachedTextGeneratorForLayout.GetPreferredHeight(label.text, settings) / label.pixelsPerUnit;
            JobLayoutQa(needed <= rect.height + 2, context + ": text height fits: " + label.name);
            JobLayoutQa(mesh.vertexCount > 0 && bounds.min.x >= rect.xMin - 2 && bounds.max.x <= rect.xMax + 2
                && bounds.min.y >= rect.yMin - 2 && bounds.max.y <= rect.yMax + 2,
                context + ": glyphs fit text box: " + label.name);
            label.rectTransform.GetWorldCorners(corners);
            JobLayoutQa(corners.All(c => rootCanvas.rect.Contains(rootCanvas.InverseTransformPoint(c))),
                context + ": text stays on screen: " + label.name);
            if (label.name == "Job precise objective" || label.name == "Raid warning benefit" || label.name == "Honest loans benefit")
            {
                var em = Vector2.Distance(RectTransformUtility.WorldToScreenPoint(camera, label.transform.position),
                    RectTransformUtility.WorldToScreenPoint(camera, label.transform.TransformPoint(Vector3.up * label.fontSize)));
                JobLayoutQa(em >= 18, context + ": important body text stays readable on the actual display: " + label.name);
            }
            foreach (var button in controls.Where(b => !label.transform.IsChildOf(b.transform) && label.name != "Envelope addressee"))
                JobLayoutQa(!JobsWorldRect(label.rectTransform).Overlaps(JobsWorldRect((RectTransform)button.transform)),
                    context + ": controls do not cover text: " + label.name + " / " + ButtonLabel(button));
        }
        for (var i = 0; i < glyphs.Count; i++)
            for (var j = i + 1; j < glyphs.Count; j++)
                JobLayoutQa(!glyphs[i].Bounds.Overlaps(glyphs[j].Bounds), context + ": rendered labels do not overlap: "
                    + glyphs[i].Label.name + " / " + glyphs[j].Label.name);
    }
}
