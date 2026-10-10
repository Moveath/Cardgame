using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CardGame.Core.Meta;
using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>Три личных поручения Тузяры: конверты на столе и одна крупная записка.</summary>
    public sealed partial class PlayerHubController
    {
        private sealed class BankJobEnvelopeView
        {
            internal RectTransform Rect;
            internal Button Button;
            internal Text Title, State, Reward, Number;
            internal HubBankJob Model;
        }

        private RectTransform _bankJobsLayer, _bankJobsSelection, _bankJobNote, _bankJobStamp;
        private CanvasGroup _bankJobsGroup;
        private readonly BankJobEnvelopeView[] _bankJobEnvelopes = new BankJobEnvelopeView[3];
        private readonly RectTransform[] _bankJobCoins = new RectTransform[8];
        private Text _bankJobsRefreshText, _bankJobTitle, _bankJobIdentity, _bankJobQuote;
        private Text _bankJobDescription, _bankJobProgressText, _bankJobRewardText, _bankJobHint;
        private Text _bankJobStampText;
        private Image _bankJobProgress;
        private Button _bankJobPrimaryButton, _bankJobBackButton;
        private bool _bankJobsShown, _bankJobsMarking, _bankJobsRefreshing, _bankJobReturnPending;
        private string _bankJobSelectedId = "";
        private HubBankJob _bankJobClaimedNote;
        private float _bankJobsFade, _bankJobsRefreshAt, _bankJobClaimTime = -1;
        private int _bankJobWalletFrom, _bankJobWalletTo;
        private bool BankJobsShown => _bankJobsShown;
        private bool BankJobWorking => LoanWorking || _bankJobClaimTime >= 0;

        public void ReturnToBankJobs()
        {
            _bankJobReturnPending = true;
            GoToBankDesk();
        }

        private void ShowBankJobs()
        {
            if (!_bankLoanShown || LoanWorking || _state == null || _bankContractShown || _bankContractFade > 0 || _bankReceiptShown || _bankProtectionShown) return;
            if (_bankJobsLayer == null) BuildBankJobs();
            _bankJobReturnPending = false;
            _bankJobsShown = true;
            _bankJobsFade = 0;
            _bankJobsGroup.alpha = LoanReduced ? 1 : 0;
            _bankJobSelectedId = "";
            _bankJobClaimedNote = null;
            _bankJobClaimTime = -1;
            _bankJobsRefreshAt = Time.unscaledTime + 1;
            _bankJobsLayer.gameObject.SetActive(true);
            _bankJobsLayer.SetAsLastSibling();
            _bankLoanWallet.SetAsLastSibling();
            BindBankJobs();
            // Hide desk controls immediately; the return flag is already cleared above.
            PaintBankDeskServices(0);
            _ = MarkBankJobsSeenAsync();
        }

        private bool TryCloseBankJobs()
        {
            if (!_bankJobsShown) return false;
            if (BankJobWorking) return true;
            if (_bankJobSelectedId.Length > 0)
            {
                _bankJobSelectedId = "";
                _bankJobClaimedNote = null;
                BindBankJobs();
            }
            else HideBankJobs();
            return true;
        }

        private void HideBankJobs()
        {
            _bankJobsShown = false;
            _bankJobClaimTime = -1;
            _bankJobSelectedId = "";
            _bankJobClaimedNote = null;
            if (_bankJobsLayer != null) _bankJobsLayer.gameObject.SetActive(false);
            foreach (var coin in _bankJobCoins) if (coin != null) coin.gameObject.SetActive(false);
        }

        private void SelectBankJob(string id)
        {
            if (!_bankJobsShown || BankJobWorking) return;
            if (_state.BankJobs.Jobs.All(job => job.Id != id)) return;
            _bankJobSelectedId = id;
            _bankJobClaimedNote = null;
            BindBankJobs();
        }

        private HubBankJob SelectedBankJob()
            => _state?.BankJobs?.Jobs.FirstOrDefault(job => job.Id == _bankJobSelectedId) ?? _bankJobClaimedNote;

        private async Task MarkBankJobsSeenAsync()
        {
            if (_bankJobsMarking || _state.BankJobs.Jobs.All(job => job.Seen)) return;
            _bankJobsMarking = true;
            try
            {
                var result = await PlayerHubRuntime.ExecuteAsync(new HubCommand { Action = "bank_jobs_seen" });
                if (this == null) return;
                if (result.Success) _state = result.Snapshot;
                else Toast(PlayerHubRuntime.ErrorText(result));
            }
            finally { _bankJobsMarking = false; }
        }

        private async Task RefreshBankJobsAsync()
        {
            if (_bankJobsRefreshing) return;
            _bankJobsRefreshing = true;
            try
            {
                var result = await PlayerHubRuntime.RefreshAsync();
                if (this == null) return;
                if (result.Success) _state = result.Snapshot;
                else Toast(PlayerHubRuntime.ErrorText(result));
            }
            finally { _bankJobsRefreshing = false; }
        }

        private async Task ActOnBankJobAsync()
        {
            if (!_bankJobsShown || BankJobWorking) return;
            var job = SelectedBankJob();
            if (job == null || job.Claimed) return;
            var ready = HubBankJobs.Ready(job);
            var action = ready ? "bank_job_claim" : job.Accepted ? "bank_job_start" : "bank_job_accept";
            if (action == "bank_job_start" && PlayerHubRuntime.HasPendingResult)
            { Toast("Сначала сохрани результат предыдущей партии."); return; }
            if (action == "bank_job_start" && _launch == null)
            { Toast("Игровой стол пока недоступен."); return; }
            var before = _state.Profile.Coins;
            SetBusy(true);
            var result = await PlayerHubRuntime.ExecuteAsync(new HubCommand { Action = action, Target = job.Id });
            if (this == null) return;
            SetBusy(false);
            if (!result.Success) { Toast(PlayerHubRuntime.ErrorText(result)); BindBankJobs(); return; }
            _state = result.Snapshot;
            if (action == "bank_job_claim")
            {
                // Keep the completed note visible even if a weekly rollover replaces its slot.
                job.Claimed = true;
                _bankJobClaimedNote = job;
                _bankJobWalletFrom = before;
                _bankJobWalletTo = _state.Profile.Coins;
                _bankJobClaimTime = 0;
                LoanSay("Вот теперь узнаю делового человека.");
            }
            else if (action == "bank_job_start")
            {
                HubRoster.ConfigureBankJob(_state, job);
                PlayerHubRuntime.BeginGame(job.Game, _state.ActiveMatchId);
                HideBankJobs();
                _bankLoanShown = false;
                _bankLoanFade = 0;
                _bankLoanLayer.gameObject.SetActive(false);
                _launch.Invoke(job.Game, 2);
                return;
            }
            BindBankJobs();
            Render();
        }

        private void PaintBankJobs(float deltaTime)
        {
            if (_bankJobReturnPending && _bankLoanShown && !LoanWorking) ShowBankJobs();
            if (_bankJobsLayer == null) return;
            if (!_bankLoanShown || _page != "home" || _cityLevel != BankLevel)
            { HideBankJobs(); return; }
            if (!_bankJobsShown) return;
            _bankJobsFade = Mathf.MoveTowards(_bankJobsFade, 1, deltaTime / .18f);
            _bankJobsGroup.alpha = LoanReduced ? 1 : Mathf.SmoothStep(0, 1, _bankJobsFade);
            _bankJobsGroup.interactable = !BankJobWorking;
            if (Time.unscaledTime >= _bankJobsRefreshAt)
            {
                _bankJobsRefreshAt = Time.unscaledTime + 1;
                if (!_busy && _state.BankJobs.RefreshAt <= LoanNow) _ = RefreshBankJobsAsync();
            }
            BindBankJobs();
            PaintBankJobReward(deltaTime);
        }

        private void BindBankJobs()
        {
            if (_bankJobsLayer == null || _state == null) return;
            var board = _state.BankJobs;
            var selected = SelectedBankJob();
            var noteOpen = selected != null && _bankJobSelectedId.Length > 0;
            _bankJobsSelection.gameObject.SetActive(!noteOpen);
            _bankJobNote.gameObject.SetActive(noteOpen);
            var active = HubBankJobs.Active(board);
            var remaining = Math.Max(0, board.RefreshAt - LoanNow);
            _bankJobsRefreshText.text = "Новые конверты через " + LoanRemaining(remaining);
            for (var i = 0; i < _bankJobEnvelopes.Length; i++)
            {
                var view = _bankJobEnvelopes[i];
                var job = i < board.Jobs.Count ? board.Jobs[i] : null;
                view.Model = job;
                view.Rect.gameObject.SetActive(job != null);
                if (job == null) continue;
                view.Number.text = "ЛИЧНОЕ ДЕЛО · " + (i + 1).ToString("D2");
                view.Title.text = job.Name;
                view.State.text = job.Claimed ? "ДЕЛО ЗАКРЫТО" : HubBankJobs.Ready(job) ? "НАГРАДА ЖДЁТ" : job.Accepted ? "В РАБОТЕ · " + job.Progress + " / " + job.Goal : "ОТКРЫТЬ КОНВЕРТ";
                view.State.color = HubBankJobs.Ready(job) ? new Color(1, .86f, .36f) : job.Claimed ? new Color(.68f, .64f, .52f) : new Color(1, .91f, .70f);
                view.Reward.text = job.Claimed ? "Тузяра доволен." : "+" + HubMoney(job.RewardCoins) + " тун-тун коинов · +" + job.RewardXP + " опыта";
                view.Button.interactable = !BankJobWorking;
            }
            if (!noteOpen) return;
            _bankJobTitle.text = selected.Name.ToUpperInvariant();
            _bankJobIdentity.text = "Личное поручение Тузяры · " + GameTitle(selected.Game) + " · " + selected.BotName;
            _bankJobQuote.text = "«" + selected.Quote + "»";
            _bankJobDescription.text = selected.Description;
            _bankJobProgressText.text = selected.Claimed ? "ПОРУЧЕНИЕ ВЫПОЛНЕНО" : "Прогресс: " + selected.Progress + " / " + selected.Goal;
            _bankJobProgress.fillAmount = Mathf.Clamp01(selected.Progress / (float)Math.Max(1, selected.Goal));
            _bankJobRewardText.text = (selected.Claimed ? "Награда выдана: " : "Награда: ") + HubMoney(selected.RewardCoins) + " тун-тун коинов\n+" + selected.RewardXP + " опыта";
            _bankJobPrimaryButton.gameObject.SetActive(!selected.Claimed);
            var otherActive = active != null && active.Id != selected.Id;
            var readyToClaim = HubBankJobs.Ready(selected);
            var caption = readyToClaim ? "ЗАБРАТЬ НАГРАДУ" : selected.Accepted ? "К ДЕЛУ" : "БЕРУСЬ";
            _bankJobPrimaryButton.GetComponentInChildren<Text>().text = caption;
            _bankJobPrimaryButton.interactable = !BankJobWorking && (readyToClaim || selected.Accepted || !otherActive);
            _bankJobBackButton.interactable = !BankJobWorking;
            _bankJobBackButton.GetComponentInChildren<Text>().text = selected.Accepted || selected.Claimed ? "К КОНВЕРТАМ" : "НЕ СЕГОДНЯ";
            _bankJobHint.text = selected.Claimed ? "Вот теперь узнаю делового человека."
                : readyToClaim ? "Дело сделано. Твоя награда уже на столе."
                : otherActive && !selected.Accepted ? "Сначала закончи дело «" + active.Name + "»."
                : selected.Accepted ? "Партия без ставки. Прогресс сохраняется — можно вернуться позже."
                : "Берёмся за одно дело. Отказаться сейчас можно без штрафа.";
            var closed = selected.Claimed;
            _bankJobStamp.gameObject.SetActive(closed);
            _bankJobStampText.text = "ДЕЛО\nЗАКРЫТО";
        }

        private void PaintBankJobReward(float deltaTime)
        {
            if (_bankJobClaimTime < 0) return;
            _bankJobClaimTime += deltaTime;
            var time = _bankJobClaimTime;
            var stamp = LoanReduced ? 1 : Mathf.SmoothStep(0, 1, Mathf.Clamp01(time / .22f));
            _bankJobStamp.localScale = Vector3.one * Mathf.Lerp(1.28f, 1, stamp);
            var target = (Vector2)_bankJobsLayer.InverseTransformPoint(_bankLoanWallet.TransformPoint(new Vector3(-104, 0, 0))) - _bankJobsLayer.rect.center;
            var landed = 0;
            for (var i = 0; i < _bankJobCoins.Length; i++)
            {
                var u = Mathf.Clamp01((time - .22f - i * .055f) / .72f);
                var visible = time >= .22f + i * .055f && u < 1 && !LoanReduced;
                var origin = new Vector2(-180 + i * 22, -177);
                var at = Vector2.Lerp(origin, target, Mathf.SmoothStep(0, 1, u));
                at += new Vector2((i - 3.5f) * 10, 145) * Mathf.Sin(u * Mathf.PI);
                _bankJobCoins[i].gameObject.SetActive(visible);
                _bankJobCoins[i].anchoredPosition = at;
                _bankJobCoins[i].localScale = Vector3.one * Mathf.Lerp(.9f, .65f, u);
                if (u >= 1) landed++;
            }
            var progress = LoanReduced ? 1 : landed / (float)_bankJobCoins.Length;
            _bankLoanWalletText.text = HubMoney(Mathf.RoundToInt(Mathf.Lerp(_bankJobWalletFrom, _bankJobWalletTo, progress)));
            if (time >= (LoanReduced ? .12f : 1.4f))
            {
                _bankJobClaimTime = -1;
                _bankLoanWalletText.text = HubMoney(_state.Profile.Coins);
                _bankJobStamp.localScale = Vector3.one;
                foreach (var coin in _bankJobCoins) coin.gameObject.SetActive(false);
                _bankJobsGroup.interactable = !LoanWorking;
                BindBankJobs();
            }
        }

        private Text BankJobText(Transform parent, string name, string value, Vector2 at, Vector2 size, int fontSize, Color ink, bool regular = false)
        {
            var label = LoanText(parent, name, value, at, size, fontSize, ink);
            if (regular) { label.font = _font; label.lineSpacing = 1.02f; }
            return label;
        }

        private void BuildBankJobs()
        {
            _bankJobsLayer = LoanRect(_bankLoanLayer, "Bank personal jobs", Vector2.zero, new Vector2(1600, 900));
            _bankJobsGroup = _bankJobsLayer.gameObject.AddComponent<CanvasGroup>();
            var dim = LoanArt(_bankJobsLayer, "Jobs shade", null, Vector2.zero, new Vector2(4800, 2400), new Color(.025f, .008f, .003f, .88f));
            dim.raycastTarget = true;
            _bankJobsSelection = LoanRect(_bankJobsLayer, "Three sealed envelopes", Vector2.zero, new Vector2(1500, 850));
            var heading = LoanText(_bankJobsSelection, "Personal jobs title", "ЛИЧНЫЕ ПОРУЧЕНИЯ", new Vector2(0, 353), new Vector2(1260, 80), 44, new Color(1, .88f, .57f), true);
            heading.fontStyle = FontStyle.Bold;
            BankJobText(_bankJobsSelection, "Personal jobs introduction", "Есть дела, которые я доверяю только своим.", new Vector2(0, 294), new Vector2(1260, 55), 32, new Color(1, .94f, .80f));
            _bankJobsRefreshText = BankJobText(_bankJobsSelection, "Jobs weekly refresh", "", new Vector2(0, 243), new Vector2(1260, 46), 27, new Color(.83f, .71f, .52f));
            for (var i = 0; i < _bankJobEnvelopes.Length; i++)
            {
                var view = new BankJobEnvelopeView();
                view.Rect = LoanRect(_bankJobsSelection, "Personal envelope " + i, new Vector2((i - 1) * 470, -2), new Vector2(450, 440));
                var envelope = LoanArt(view.Rect, "Sealed envelope art", null, new Vector2(0, 20), new Vector2(446, 254), new Color(1, .92f, .73f));
                envelope.sprite = BankObjectSprite("bank-job-envelope");
                envelope.preserveAspect = true;
                envelope.raycastTarget = true;
                view.Button = envelope.gameObject.AddComponent<Button>();
                view.Button.targetGraphic = envelope;
                var colors = view.Button.colors;
                colors.highlightedColor = new Color(1, .96f, .84f);
                colors.pressedColor = new Color(.82f, .74f, .59f);
                view.Button.colors = colors;
                var captured = view;
                view.Button.onClick.AddListener(() => { if (captured.Model != null) SelectBankJob(captured.Model.Id); });
                view.Number = BankJobText(view.Rect, "Job case number", "", new Vector2(0, 188), new Vector2(430, 47), 27, new Color(.91f, .76f, .47f));
                view.Title = BankJobText(view.Rect, "Envelope addressee", "", new Vector2(0, 75), new Vector2(390, 80), 35, new Color(.36f, .08f, .025f));
                view.Title.fontStyle = FontStyle.Bold;
                view.State = BankJobText(view.Rect, "Envelope state", "", new Vector2(0, -156), new Vector2(440, 64), 33, new Color(1, .91f, .70f));
                view.Reward = BankJobText(view.Rect, "Envelope reward", "", new Vector2(0, -212), new Vector2(430, 80), 28, new Color(.91f, .80f, .60f));
                _bankJobEnvelopes[i] = view;
            }
            BankJobText(_bankJobsSelection, "One job at a time", "Одно дело за раз. Готовая награда дождётся тебя.", new Vector2(0, -292), new Vector2(1350, 58), 30, new Color(.96f, .86f, .67f));
            ReceiptButton(_bankJobsSelection, "К СТОЛУ", new Vector2(-535, -369), new Vector2(300, 66), () => TryCloseBankJobs());
            BankJobText(_bankJobsSelection, "Tap an envelope", "Нажми на конверт, чтобы прочитать поручение.", new Vector2(192, -369), new Vector2(1040, 55), 29, new Color(.91f, .78f, .57f));

            _bankJobNote = BuildCompactReceiptPaper(_bankJobsLayer, new Vector2(1320, 840));
            _bankJobNote.name = "Personal job note";
            _bankJobTitle = LoanText(_bankJobNote, "Job note title", "", new Vector2(50, 334), new Vector2(1020, 75), 37, LoanInk, true);
            _bankJobIdentity = BankJobText(_bankJobNote, "Job note identity", "", new Vector2(20, 270), new Vector2(1130, 48), 28, LoanInk);
            LoanArt(_bankJobNote, "Job paper divider", null, new Vector2(0, 239), new Vector2(1130, 2), new Color(.49f, .28f, .08f, .4f));
            _bankJobQuote = BankJobText(_bankJobNote, "Tuzyara private message", "", new Vector2(0, 182), new Vector2(1110, 86), 30, new Color(.42f, .19f, .075f), true);
            _bankJobQuote.fontStyle = FontStyle.Italic;
            BankJobText(_bankJobNote, "Job objective heading", "ЧТО НУЖНО СДЕЛАТЬ", new Vector2(0, 120), new Vector2(1090, 51), 30, ReceiptRed);
            _bankJobDescription = BankJobText(_bankJobNote, "Job precise objective", "", new Vector2(0, 27), new Vector2(1120, 120), 34, LoanInk, true);
            var track = LoanArt(_bankJobNote, "Job progress track", null, new Vector2(0, -51), new Vector2(1070, 10), new Color(.37f, .24f, .1f, .18f));
            _bankJobProgress = LoanArt(track.transform, "Job progress", null, Vector2.zero, new Vector2(1070, 10), new Color(.39f, .44f, .18f));
            _bankJobProgress.sprite = UiTextures.RoundedRect(64, 16, 4, Color.white);
            _bankJobProgress.type = Image.Type.Filled;
            _bankJobProgress.fillMethod = Image.FillMethod.Horizontal;
            _bankJobProgressText = BankJobText(_bankJobNote, "Job progress label", "", new Vector2(0, -92), new Vector2(1090, 56), 31, LoanInk);
            _bankJobRewardText = BankJobText(_bankJobNote, "Job note reward", "", new Vector2(-105, -167), new Vector2(860, 105), 34, new Color(.38f, .19f, .025f));
            _bankJobRewardText.fontStyle = FontStyle.Bold;
            _bankJobHint = BankJobText(_bankJobNote, "Job action detail", "", new Vector2(0, -260), new Vector2(1100, 75), 27, LoanInk, true);
            _bankJobBackButton = ReceiptButton(_bankJobNote, "К КОНВЕРТАМ", new Vector2(-357, -346), new Vector2(392, 68), () => TryCloseBankJobs());
            _bankJobPrimaryButton = ReceiptButton(_bankJobNote, "БЕРУСЬ", new Vector2(357, -346), new Vector2(392, 68), () => _ = ActOnBankJobAsync(), true);
            _bankJobStamp = LoanRect(_bankJobNote, "Job closed stamp", new Vector2(469, -171), new Vector2(184, 98));
            _bankJobStamp.localRotation = Quaternion.Euler(0, 0, -8);
            var stampEdge = LoanArt(_bankJobStamp, "Job stamp edge", null, Vector2.zero, new Vector2(184, 98), new Color(.49f, .09f, .055f, .78f));
            stampEdge.sprite = UiTextures.RoundedRect(128, 64, 8, Color.white);
            stampEdge.type = Image.Type.Sliced;
            var stampFace = LoanArt(_bankJobStamp, "Job stamp paper", null, Vector2.zero, new Vector2(176, 90), new Color(.99f, .91f, .70f));
            stampFace.sprite = stampEdge.sprite;
            stampFace.type = Image.Type.Sliced;
            _bankJobStampText = BankJobText(_bankJobStamp, "Job closed words", "ДЕЛО\nЗАКРЫТО", Vector2.zero, new Vector2(168, 80), 30, new Color(.49f, .09f, .055f));
            _bankJobStampText.fontStyle = FontStyle.Bold;
            for (var i = 0; i < _bankJobCoins.Length; i++)
            {
                var coin = LoanArt(_bankJobsLayer, "Job reward flying coin " + i, null, Vector2.zero, new Vector2(68, 68), Color.white);
                coin.sprite = HubCurrencyArt.Silver;
                coin.preserveAspect = true;
                _bankJobCoins[i] = coin.rectTransform;
                coin.gameObject.SetActive(false);
            }
            _bankJobsLayer.gameObject.SetActive(false);
        }
    }
}
