using System;
using CardGame.Core.Meta;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CardGameClient
{
    public sealed partial class PlayerHubController
    {
        private void UpdateBankLoan(float deltaTime, bool typing)
        {
            if (_bankLoanLayer == null) return;
            if (_bankLoanShown && (_page != "home" || _cityLevel != BankLevel))
            {
                HideBankServices();
                _bankLoanShown = _bankContractShown = false;
                _bankIssueTime = _bankFineTime = -1;
                _bankFineInspecting = _bankTrapRevealPending = false;
                _bankFineModalLayer.gameObject.SetActive(false);
            }
            // Enter ends editing but uGUI may keep the field selected. Esc must work again then.
            var keyboard = typing && (_bankLoanAmountInput == null || _bankLoanAmountInput.isFocused || BankReceiptTyping) ? null : Keyboard.current;
            var escape = keyboard != null && keyboard.escapeKey.isPressed;
            if (_bankLoanShown && escape && !_bankLoanEscapeHeld)
            {
                _cityVerticalHeld = 1;
                CloseBankLoan();
            }
            _bankLoanEscapeHeld = escape;
            _bankLoanFade = Mathf.MoveTowards(_bankLoanFade, _bankLoanShown ? 1 : 0, deltaTime / (LoanReduced ? .1f : .35f));
            _bankLoanGroup.alpha = Mathf.SmoothStep(0, 1, _bankLoanFade);
            _bankLoanGroup.blocksRaycasts = BankShown;
            UpdateBankReceipt(deltaTime);
            if (!BankShown)
            {
                _bankLoanLayer.gameObject.SetActive(false);
                return;
            }
            if (_bankIssueTime >= 0) _bankIssueTime += deltaTime * (LoanReduced ? 3 : 1);
            if (_bankFineTime >= 0) _bankFineTime += deltaTime * (LoanReduced ? 2 : 1);
            if (_bankLoanBubbleTime > 0) _bankLoanBubbleTime = Mathf.Max(0, _bankLoanBubbleTime - deltaTime);
            if (!_busy && !LoanWorking && Time.unscaledTime >= _bankLoanRefreshAt)
            {
                _bankLoanRefreshAt = Time.unscaledTime + 5;
                // Updates the debt when its deadline passes while the player stays at the desk.
                if (_state.Bank.Debt > 0 && _state.Bank.DueAt <= LoanNow) _ = RefreshBankLoanAsync();
            }
            PaintBankLoan(deltaTime);
        }

        private async System.Threading.Tasks.Task RefreshBankLoanAsync()
        {
            var result = await PlayerHubRuntime.RefreshAsync();
            if (this == null) return;
            if (result.Success) _state = result.Snapshot;
        }

        private void PaintBankLoan(float deltaTime)
        {
            var viewport = ((RectTransform)transform).rect.size / Mathf.Max(.01f, _stage.localScale.x);
            var sprite = _bankLoanDesk.GetComponent<Image>().sprite;
            var aspect = sprite != null ? sprite.rect.width / sprite.rect.height : 16f / 9f;
            var height = Mathf.Max(900, viewport.y, Mathf.Max(1600, viewport.x) / aspect);
            _bankLoanDesk.sizeDelta = new Vector2(height * aspect, height);
            // A brief push towards the banker accompanies the change from the hall to the desk.
            _bankLoanDesk.localScale = Vector3.one * (LoanReduced ? 1 : 1 + .035f * Mathf.SmoothStep(0, 1, _bankLoanFade));
            var contractStep = LoanReduced ? .12f : .35f;
            _bankContractFade = Mathf.MoveTowards(_bankContractFade, _bankContractShown ? 1 : 0, deltaTime / contractStep);
            var paperT = Mathf.SmoothStep(0, 1, _bankContractFade);
            var paperVisible = _bankContractShown || _bankContractFade > 0;
            _bankContract.gameObject.SetActive(paperVisible);
            _bankContractGroup.alpha = LoanReduced ? paperT : 1;
            _bankContractGroup.interactable = _bankContractShown && !LoanWorking;
            _bankContractGroup.blocksRaycasts = paperVisible;
            _bankContractDim.gameObject.SetActive(paperVisible);
            _bankContractDim.color = new Color(.02f, .015f, .01f, .6f * paperT);
            var shake = 0f;
            if (!LoanReduced && _bankFineTime > .18f && _bankFineTime < .58f)
                shake = Mathf.Sin(_bankFineTime * 70) * 3 * (1 - (_bankFineTime - .18f) / .4f);
            var stampStart = _bankLoanApproved ? .62f : .08f;
            if (!LoanReduced && _bankIssueTime >= stampStart && _bankIssueTime < stampStart + .30f)
                shake = Mathf.Sin((_bankIssueTime - stampStart) * 80) * 4 * (1 - (_bankIssueTime - stampStart) / .3f);
            _bankContract.anchoredPosition = new Vector2(shake, LoanReduced ? 0 : -950 * (1 - paperT));
            _bankContract.localRotation = Quaternion.identity;
            var reading = _bankFineInspecting;
            _bankFineModalLayer.gameObject.SetActive(reading);
            _bankMagnifier.gameObject.SetActive(reading && !LoanReduced);
            if (reading)
            {
                _bankFineReadingGroup.alpha = LoanReduced ? 1 : Mathf.SmoothStep(0, 1, Mathf.Clamp01(_bankFineTime / .18f));
                _bankFineChallengeButton.interactable = _bankFineCloseButton.interactable = !_busy;
                ((RectTransform)_bankFineCloseButton.transform).anchoredPosition = new Vector2(_bankFineChallengeButton.gameObject.activeSelf ? -250 : 0, -245);
            }
            PaintBankBook(deltaTime);
            PaintLoanIssue(stampStart);
            // The signed document has no live decisions left. Clear the paper under the stamp.
            _bankLoanAgreeButton.gameObject.SetActive(_bankIssueTime < 0 && !_bankContractReadOnly);
            _bankLoanDeclineButton.gameObject.SetActive(_bankIssueTime < 0 && !_bankContractReadOnly);
            var bank = _state.Bank;
            _bankLoanTakeButton.gameObject.SetActive(bank.Debt <= 0 && !bank.CollectorPending && !bank.CollectionActive && !paperVisible && !_bankReceiptShown);
            _bankLoanTakeButton.interactable = !LoanWorking;
            _bankLoanDebtButton.gameObject.SetActive(bank.Debt > 0 && !paperVisible && _bankIssueTime < 0 && !_bankReceiptShown);
            _bankLoanDebtButton.interactable = !LoanWorking;
            _bankLoanReadButton.gameObject.SetActive(HasActiveBankContract && !paperVisible && !_bankReceiptShown);
            _bankLoanReadButton.interactable = !LoanWorking;
            _bankCollectorsButton.gameObject.SetActive(bank.CollectorPending && !paperVisible && !_bankReceiptShown);
            _bankCollectorsButton.interactable = !LoanWorking;
            var remaining = Math.Max(0, bank.DueAt - LoanNow);
            _bankLoanDebtText.text = "ДОЛГ: " + HubMoney(bank.Debt) + " · " + (remaining > 0 ? LoanRemaining(remaining) : "СРОК ВЫШЕЛ");
            _bankLoanDebtTerms.text = string.IsNullOrEmpty(bank.Trick) ? "Открыть квитанцию · погасить долг" : "Условия по сноске: " + (bank.Trick == "daily" && HubLoans.Version(bank) < 2 ? "10% — в день" : LoanFinePrint(bank.Trick).TrimStart('*'));
            var speechVisible = !_bankReceiptShown && !reading && _bankLoanBubbleTime != 0 && !paperVisible;
            _bankLoanBubble.gameObject.SetActive(speechVisible);
            _bankLoanBubbleGroup.alpha = _bankLoanBubbleTime < 0 ? 1 : Mathf.Clamp01(_bankLoanBubbleTime / .3f);
            _bankLoanBubble.anchoredPosition = reading ? new Vector2(-606, 150) : new Vector2(-535, 210);
            _bankLoanBubble.sizeDelta = LoanReceiptSize(reading ? 300 : 440);
            _bankLoanBubbleText.rectTransform.sizeDelta = reading ? new Vector2(240, 108) : new Vector2(356, 148);
            _bankLoanBubbleText.rectTransform.anchoredPosition = new Vector2(0, reading ? -32 : -38);
            // The small-print response stays above the dim layer, next to the document.
            if (reading) _bankFineModalLayer.SetAsLastSibling();
            RefreshLoanButtons();
            PaintBankRepayment();
            PaintBankDeskServices(deltaTime);
        }

        private static string LoanRemaining(long seconds)
            => seconds >= 86400 ? seconds / 86400 + " д " + seconds % 86400 / 3600 + " ч"
                : (seconds / 3600).ToString("D2") + ":" + (seconds % 3600 / 60).ToString("D2") + ":" + (seconds % 60).ToString("D2");

        private void LoanPaintStamp(bool approved)
        {
            var face = approved ? Resources.Load<Sprite>("UI/Hub/bank-stamp-face") : null;
            var ring = approved ? null : Resources.Load<Sprite>("UI/Hub/bank-stamp-ring");
            _bankStampImage.sprite = face != null ? face : ring;
            var ink = approved ? LoanBlue : new Color(.68f, .10f, .075f);
            var centre = _bankStampImage.transform.Find("Fallback stamp centre");
            if (_bankStampImage.sprite == null)
            {
                // A white primitive can take blue ink accurately; tinting red artwork cannot.
                _bankStampImage.sprite = UiTextures.Circle(128, Color.white);
                if (centre == null)
                {
                    var centreImage = LoanArt(_bankStampImage.transform, "Fallback stamp centre", null, Vector2.zero, new Vector2(162, 162), new Color(1, .92f, .71f));
                    centreImage.sprite = UiTextures.Circle(128, Color.white);
                    centre = centreImage.transform;
                }
                centre.gameObject.SetActive(true);
                _bankStampImage.color = ink;
            }
            else
            {
                if (centre != null) centre.gameObject.SetActive(false);
                _bankStampImage.color = Color.white;
            }
            // Blue face art has an empty lower ribbon; a simple ring takes its word in the centre.
            _bankLoanStampText.rectTransform.anchoredPosition = face != null ? new Vector2(0, -46) : Vector2.zero;
            _bankLoanStampText.rectTransform.sizeDelta = new Vector2(face != null ? 158 : 170, 36);
            _bankLoanStampText.color = ink;
            _bankLoanStampText.fontSize = face != null ? 23 : 25;
        }

        private void PaintLoanIssue(float stampStart)
        {
            var active = _bankIssueTime >= 0;
            var t = _bankIssueTime;
            var sign = active && _bankLoanApproved ? Mathf.Clamp01(t / .6f) : 0;
            _bankLoanPen.gameObject.SetActive(active && _bankLoanApproved && sign < 1 && !LoanReduced);
            if (active && _bankLoanApproved)
            {
                var count = Mathf.Clamp(Mathf.CeilToInt(_bankSignatureName.Length * sign), 0, _bankSignatureName.Length);
                _bankLoanSignature.text = _bankSignatureName.Substring(0, count);
                _bankLoanPen.anchoredPosition = new Vector2(70 + Mathf.Min(250, _bankLoanSignature.preferredWidth), 39 + (LoanReduced ? 0 : Mathf.Sin(t * 55) * 2));
                _bankLoanPen.localRotation = Quaternion.Euler(0, 0, -5 + Mathf.Sin(t * 25) * 2);
            }
            var stamp = active && t >= stampStart || _bankContractReadOnly;
            _bankLoanStamp.gameObject.SetActive(stamp);
            if (stamp)
            {
                var u = _bankContractReadOnly ? 1 : Mathf.Clamp01((t - stampStart) / .23f);
                var impact = LoanReduced ? 1 : Mathf.SmoothStep(0, 1, u);
                _bankLoanStamp.localScale = Vector3.one * .8f * Mathf.Lerp(1.12f, 1, impact);
                _bankLoanStamp.anchoredPosition = new Vector2(440, 70 + 12 * (1 - impact));
                _bankLoanStamp.localRotation = Quaternion.Euler(0, 0, -12);
                _bankStampImage.canvasRenderer.SetAlpha(u);
                _bankLoanStampText.canvasRenderer.SetAlpha(u);
            }
            var paperLeave = _bankLoanApproved ? 1.35f : 1.05f;
            if (active && t >= paperLeave && _bankContractShown)
            {
                _bankContractShown = false;
                if (_bankLoanApproved)
                {
                    var days = HubLoans.Days(_state.Bank.Trick);
                    LoanSay("Приятно иметь дело. " + (days == 2 ? "Два дня" : "Три дня") + ", дружок.");
                }
                else LoanSay(LoanRejection(_state.Bank.DecisionCode));
            }
            var origin = new Vector2(-64, 295);
            var target = (Vector2)_bankLoanLayer.InverseTransformPoint(_bankLoanWallet.TransformPoint(new Vector3(-104, 0, 0))) - _bankLoanLayer.rect.center;
            var landed = 0;
            for (var i = 0; i < LoanCoinCount; i++)
            {
                var coinTime = active && _bankLoanApproved ? t - 1.72f - i * .065f : -1;
                var fan = Mathf.Clamp01(coinTime / .30f);
                var flight = Mathf.Clamp01((coinTime - .30f) / .65f);
                var visible = coinTime >= 0 && flight < 1;
                _bankLoanCoins[i].gameObject.SetActive(visible);
                if (flight >= 1) landed++;
                var spread = (i - (LoanCoinCount - 1) / 2f) / 6.5f;
                var peak = new Vector2(spread * 315, 70 + Mathf.Abs(spread) * 120);
                var fanAt = Vector2.Lerp(origin, peak, Mathf.SmoothStep(0, 1, fan));
                var at = Vector2.Lerp(fanAt, target, Mathf.SmoothStep(0, 1, flight));
                at += new Vector2(spread * 48, 120) * Mathf.Sin(flight * Mathf.PI);
                _bankLoanCoins[i].anchoredPosition = at;
                _bankLoanCoins[i].localRotation = Quaternion.Euler(0, 0, LoanReduced ? 0 : spread * 35 * (1 - flight));
                _bankLoanCoins[i].localScale = Vector3.one * (LoanReduced ? 1 : Mathf.Lerp(.7f + .3f * fan, .68f, flight));
                if (LoanReduced) _bankLoanCoins[i].gameObject.SetActive(false);
            }
            var balance = active && _bankLoanApproved ? Mathf.RoundToInt(Mathf.Lerp(_bankWalletFrom, _bankWalletTo, landed / (float)LoanCoinCount)) : _state.Profile.Coins;
            _bankLoanWalletText.text = HubMoney(balance);
            var finished = active && t >= (_bankLoanApproved ? 3.75f : 1.55f);
            if (finished)
            {
                _bankIssueTime = -1;
                _bankLoanWalletText.text = HubMoney(_state.Profile.Coins);
                foreach (var coin in _bankLoanCoins) coin.gameObject.SetActive(false);
                if (_bankTrapRevealPending && _bankLoanShown) RevealSignedBankTrick();
            }
        }

        private string LoanRejection(string code)
        {
            switch (code)
            {
                case "history": return "Без истории не даю. Сыграй пару партий — приходи.";
                case "debt": return "Сначала старый долг, потом поговорим.";
                case "collector": return "Ребята тебя ждут. Сначала с ними.";
                case "limit": return "Губу закатай. Лимит — " + HubMoney(_state.Bank.CreditLimit) + ".";
                default: return _state.Bank.Decision;
            }
        }
    }
}
