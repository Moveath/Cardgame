using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CardGame.Core.Meta;
using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>Payment entry and immutable receipts persisted by the bank service.</summary>
    public sealed partial class PlayerHubController
    {
        private static readonly Color ReceiptRed = new Color(.65f, .105f, .065f);
        private RectTransform _bankReceiptLayer, _bankReceiptPaper, _bankReceiptForm, _bankReceiptConfirmation, _bankReceiptStamp;
        private CanvasGroup _bankReceiptGroup;
        private Text _bankReceiptTitle, _bankReceiptIdentity, _bankReceiptTotal, _bankReceiptPaid, _bankReceiptRemaining;
        private Text _bankReceiptProgressText, _bankReceiptDiscount, _bankReceiptAfter, _bankReceiptWalletHint;
        private Text _bankReceiptAccepted, _bankReceiptSaved, _bankReceiptStampText, _bankReceiptHistoryLabel;
        private Text[] _bankReceiptHeadings;
        private string _bankReceiptPaymentReaction = "";
        private static Sprite _bankReceiptPaperTexture, _bankReceiptCrest;
        private Image _bankReceiptProgress;
        private InputField _bankReceiptAmountInput;
        private Button _bankReceiptPayButton, _bankReceiptArchiveButton, _bankReceiptNextPaymentButton;
        private Button _bankReceiptPreviousButton, _bankReceiptNextButton;
        private readonly List<Button> _bankReceiptPresets = new List<Button>();
        private readonly int[] _bankReceiptPresetValues = { 1, 10, 100, -1, -2 };
        private bool _bankReceiptShown;
        private float _bankReceiptFade, _bankReceiptStampTime = -1, _bankReceiptRefreshAt;
        private string _bankReceiptSelectedId = "";
        private long _bankReceiptRevision = -1;
        private bool BankReceiptTyping => _bankReceiptShown && _bankReceiptAmountInput != null && _bankReceiptAmountInput.isFocused;
        private bool ReceiptWorking => LoanWorking || _bankReceiptStampTime >= 0 && _bankReceiptStampTime < .3f;

        private void ShowBankReceipt()
        {
            if (!_bankLoanShown || LoanWorking || _state == null || BankServiceShown) return;
            if (_bankReceiptLayer == null) BuildBankReceipt();
            _bankContractShown = false; _bankContractFade = 0; _bankContractReadOnly = false;
            _bankLoanAmountInput.DeactivateInputField();
            ResetBankRepayment();
            _bankReceiptShown = true; _bankReceiptFade = 0; _bankReceiptStampTime = -1;
            _bankReceiptSelectedId = "";
            _bankReceiptPaymentReaction = "";
            if (_state.Bank.Debt <= 0)
            {
                var latest = LatestBankPayment();
                if (latest != null) _bankReceiptSelectedId = latest.Id;
            }
            _bankReceiptAmountInput.SetTextWithoutNotify(Math.Max(0, ReceiptMaximum()).ToString());
            _bankReceiptLayer.gameObject.SetActive(true);
            _bankReceiptLayer.SetAsLastSibling();
            _bankLoanWallet.SetAsLastSibling();
            BindBankReceipt();
        }

        private void CloseBankReceipt()
        {
            if (ReceiptWorking) return;
            _bankReceiptAmountInput.DeactivateInputField();
            _bankReceiptShown = false;
            _bankReceiptLayer.gameObject.SetActive(false);
            var reaction = _bankReceiptPaymentReaction;
            _bankReceiptPaymentReaction = "";
            LoanSay(reaction.Length > 0 ? reaction : LoanGreeting(), reaction.Length == 0);
        }

        private HubLoanPayment LatestBankPayment()
        {
            var payments = _state.Bank.Payments;
            if (payments == null) return null;
            var contract = _state.Bank.Contract;
            for (var i = payments.Count - 1; i >= 0; i--)
                if (contract == null || string.IsNullOrEmpty(contract.Id) || payments[i].ContractId == contract.Id) return payments[i];
            return null;
        }

        private List<HubLoanPayment> BankReceiptHistory()
        {
            var result = new List<HubLoanPayment>();
            var contract = _state.Bank.Contract;
            if (_state.Bank.Payments != null)
                foreach (var payment in _state.Bank.Payments)
                    if (contract == null || string.IsNullOrEmpty(contract.Id) || payment.ContractId == contract.Id) result.Add(payment);
            return result;
        }

        private HubLoanPayment SelectedBankPayment()
        {
            if (_bankReceiptSelectedId.Length == 0 || _state.Bank.Payments == null) return null;
            foreach (var payment in _state.Bank.Payments)
                if (payment.Id == _bankReceiptSelectedId) return payment;
            return null;
        }

        private int ReceiptMaximum() => Math.Min(Math.Max(0, _state.Profile.Coins), HubLoans.PayoffQuote(_state.Bank, LoanNow).Amount);

        private bool ReceiptAmount(out int amount)
        {
            amount = 0;
            return _bankReceiptAmountInput != null && int.TryParse(_bankReceiptAmountInput.text, out amount)
                && amount >= 1 && amount <= ReceiptMaximum();
        }

        private int ReceiptPresetAmount(int preset)
        {
            var maximum = ReceiptMaximum();
            var quote = HubLoans.PayoffQuote(_state.Bank, LoanNow);
            return preset == -2 ? maximum : preset == -1 ? Math.Min(maximum, (quote.Amount + 1) / 2) : preset;
        }

        private void SetBankReceiptAmount(int preset)
        {
            if (!_bankReceiptShown || ReceiptWorking || _bankReceiptSelectedId.Length > 0) return;
            var value = ReceiptPresetAmount(preset);
            if (value < 1 || value > ReceiptMaximum()) return;
            _bankReceiptAmountInput.SetTextWithoutNotify(value.ToString());
            BindBankReceipt();
        }

        private void ShowBankReceiptArchive()
        {
            if (ReceiptWorking) return;
            var latest = LatestBankPayment();
            if (latest == null) return;
            _bankReceiptAmountInput.DeactivateInputField();
            _bankReceiptSelectedId = latest.Id; _bankReceiptStampTime = -1;
            BindBankReceipt();
        }

        private void MoveBankReceiptHistory(int direction)
        {
            if (ReceiptWorking) return;
            var history = BankReceiptHistory();
            var index = history.FindIndex(item => item.Id == _bankReceiptSelectedId);
            index += direction;
            if (index < 0 || index >= history.Count) return;
            _bankReceiptSelectedId = history[index].Id; _bankReceiptStampTime = -1;
            BindBankReceipt();
        }

        private void NewBankReceiptPayment()
        {
            if (ReceiptWorking || _state.Bank.Debt <= 0) return;
            _bankReceiptSelectedId = ""; _bankReceiptStampTime = -1;
            _bankReceiptAmountInput.SetTextWithoutNotify(Math.Max(0, ReceiptMaximum()).ToString());
            BindBankReceipt();
        }

        private async Task PayBankReceiptAsync()
        {
            if (!_bankReceiptShown || ReceiptWorking || _bankReceiptSelectedId.Length > 0 || !ReceiptAmount(out var amount)) return;
            _bankReceiptAmountInput.DeactivateInputField();
            var walletBefore = _state.Profile.Coins;
            var commandId = Guid.NewGuid().ToString("N");
            SetBusy(true);
            var result = await PlayerHubRuntime.ExecuteAsync(new HubCommand { Id = commandId, Action = "bank_repay", Amount = amount });
            if (this == null) return;
            SetBusy(false);
            if (!result.Success) { Toast(PlayerHubRuntime.ErrorText(result)); BindBankReceipt(); return; }
            _state = result.Snapshot;
            _bankReceiptSelectedId = commandId;
            var payment = SelectedBankPayment();
            _bankReceiptPaymentReaction = payment == null ? "" : !payment.Closed ? "Начало положено. Остальное тоже моё."
                : payment.Discount > 0 ? "Уже всё? Даже проценты не успели соскучиться." : "Рассчитались. Какая досада.";
            BeginBankRepayment(walletBefore);
            BindBankReceipt();
            Render();
        }

        private void UpdateBankReceipt(float deltaTime)
        {
            if (_bankReceiptLayer == null) return;
            if (!_bankLoanShown || _page != "home" || _cityLevel != BankLevel)
            {
                ResetBankRepayment();
                _bankReceiptShown = false;
                _bankReceiptPaymentReaction = "";
                _bankReceiptLayer.gameObject.SetActive(false);
                return;
            }
            if (!_bankReceiptShown) return;
            _bankReceiptFade = Mathf.MoveTowards(_bankReceiptFade, 1, deltaTime / .18f);
            _bankReceiptGroup.alpha = _bankReceiptFade;
            _bankReceiptPaper.localScale = Vector3.one * (LoanReduced ? 1 : Mathf.Lerp(.97f, 1, Mathf.SmoothStep(0, 1, _bankReceiptFade)));
            var wasStamping = _bankReceiptStampTime >= 0 && _bankReceiptStampTime < .3f;
            if (_bankReceiptStampTime >= 0) _bankReceiptStampTime += deltaTime;
            // Restore payment controls on the same frame that the stamp finishes.
            if (wasStamping && _bankReceiptStampTime >= .3f) _bankReceiptRevision = -1;
            AdvanceBankRepayment(deltaTime);
            var stamp = _bankReceiptStampTime < 0 || LoanReduced ? 1 : Mathf.Clamp01(_bankReceiptStampTime / .23f);
            _bankReceiptStamp.localScale = Vector3.one * Mathf.Lerp(1.55f, 1, Mathf.SmoothStep(0, 1, stamp));
            if (_bankReceiptRevision != _state.Revision || Time.unscaledTime >= _bankReceiptRefreshAt)
            {
                _bankReceiptRefreshAt = Time.unscaledTime + .25f;
                BindBankReceipt();
            }
            _bankReceiptGroup.interactable = !ReceiptWorking;
        }

        private void BindBankReceipt()
        {
            if (_bankReceiptLayer == null || _state == null) return;
            _bankReceiptRevision = _state.Revision;
            var bank = _state.Bank;
            var payment = SelectedBankPayment();
            var confirmed = payment != null;
            var contract = bank.Contract;
            var contractNumber = contract != null ? contract.Number.ToString("D4") : bank.OfferSequence.ToString("D4");
            var borrower = contract != null && !string.IsNullOrEmpty(contract.Borrower) ? contract.Borrower : LoanBorrowerName();
            _bankReceiptTitle.text = confirmed ? "КВИТАНЦИЯ О ПОГАШЕНИИ" : "ПОГАШЕНИЕ ЗАЙМА";
            _bankReceiptIdentity.text = "АО «Банк Тузяры» · договор № " + contractNumber + "-ТЗ · " + borrower;
            _bankReceiptIdentity.resizeTextForBestFit = false;
            var paid = confirmed ? payment.PaidAfter : bank.Repaid;
            var debt = confirmed ? payment.DebtAfter : bank.Debt;
            var discount = confirmed ? payment.Discount : 0;
            var progressTotal = confirmed ? payment.PaidBefore + payment.DebtBefore : paid + debt;
            _bankReceiptHeadings[0].text = confirmed ? "ДОЛГ ДО ПЛАТЕЖА" : "СЕЙЧАС ДОЛЖЕН";
            _bankReceiptHeadings[1].text = confirmed ? "ВНЕСЕНО" : "ВНОСИШЬ";
            _bankReceiptHeadings[2].text = confirmed ? "ОСТАТОК" : "ОСТАНЕТСЯ";
            _bankReceiptTotal.text = HubMoney(confirmed ? payment.DebtBefore : bank.Debt);
            _bankReceiptPaid.text = confirmed ? HubMoney(payment.Amount) : "";
            _bankReceiptPaid.gameObject.SetActive(confirmed || bank.Debt <= 0);
            _bankReceiptRemaining.text = HubMoney(debt);
            _bankReceiptProgress.fillAmount = progressTotal <= 0 ? 0 : Mathf.Clamp01((paid + discount) / (float)progressTotal);
            _bankReceiptProgressText.text = "Всего внесено: " + HubMoney(paid) + " тун-тун коинов";
            _bankReceiptProgressText.rectTransform.anchoredPosition = new Vector2(confirmed ? -150 : 0, -135);
            _bankReceiptProgressText.rectTransform.sizeDelta = new Vector2(confirmed ? 770 : 1080, 46);
            var progressWidth = confirmed ? 770 : 1080;
            var progressTrack = (RectTransform)_bankReceiptProgress.transform.parent;
            progressTrack.anchoredPosition = new Vector2(confirmed ? -150 : 0, -172);
            progressTrack.sizeDelta = new Vector2(progressWidth, 10);
            _bankReceiptProgress.rectTransform.sizeDelta = new Vector2(progressWidth, 10);
            _bankReceiptForm.gameObject.SetActive(!confirmed && bank.Debt > 0);
            _bankReceiptConfirmation.gameObject.SetActive(confirmed || bank.Debt <= 0);
            _bankReceiptPayButton.gameObject.SetActive(!confirmed && bank.Debt > 0);
            _bankReceiptArchiveButton.gameObject.SetActive(!confirmed && LatestBankPayment() != null);
            _bankReceiptNextPaymentButton.gameObject.SetActive(confirmed && bank.Debt > 0);
            var history = BankReceiptHistory();
            var index = history.FindIndex(item => item.Id == _bankReceiptSelectedId);
            _bankReceiptPreviousButton.gameObject.SetActive(confirmed && history.Count > 1);
            _bankReceiptNextButton.gameObject.SetActive(confirmed && history.Count > 1);
            _bankReceiptPreviousButton.interactable = !ReceiptWorking && index > 0;
            _bankReceiptNextButton.interactable = !ReceiptWorking && index >= 0 && index < history.Count - 1;
            _bankReceiptHistoryLabel.text = confirmed ? "Квитанция " + (index + 1) + " / " + history.Count : "";
            _bankReceiptNextPaymentButton.interactable = !ReceiptWorking;
            _bankReceiptArchiveButton.interactable = !ReceiptWorking;
            if (confirmed)
            {
                var date = DateTimeOffset.FromUnixTimeSeconds(payment.At).ToLocalTime().ToString("dd.MM.yyyy, HH:mm");
                var source = payment.Source == "income" ? "Удержано из поступления" : payment.Source == "collectors" ? "Платёж по итогам игры" : "Принято из кошелька";
                _bankReceiptAccepted.text = source + "\n" + date;
                _bankReceiptStampText.text = payment.Closed ? "ПОГАШЕНО" : "ОПЛАЧЕНО";
                _bankReceiptStamp.gameObject.SetActive(!BankRepaymentAnimating);
                _bankReceiptSaved.text = (payment.Closed ? "Договор закрыт. Обязательства исполнены." : "Платёж принят. Можно продолжить погашение частями.")
                    + (payment.Discount > 0 ? "\nОтдельно списано по скидке: " + HubMoney(payment.Discount) + " тун-тун коинов." : "")
                    + "\nКвитанция сохранена в архиве банка.";
                return;
            }
            if (bank.Debt <= 0)
            {
                _bankReceiptAccepted.text = "Задолженности нет.";
                _bankReceiptSaved.text = "Сохранённых платежей по этому договору пока нет.";
                _bankReceiptStamp.gameObject.SetActive(false);
                return;
            }
            var quote = HubLoans.PayoffQuote(bank, LoanNow);
            var valid = ReceiptAmount(out var amount);
            _bankReceiptAmountInput.interactable = !ReceiptWorking && ReceiptMaximum() > 0;
            _bankReceiptPayButton.interactable = !ReceiptWorking && valid;
            _bankReceiptWalletHint.text = "В кошельке: " + HubMoney(_state.Profile.Coins) + " тун-тун коинов";
            var closes = valid && amount == quote.Amount;
            var remaining = valid ? Math.Max(0, bank.Debt - amount - (closes ? quote.Discount : 0)) : bank.Debt;
            _bankReceiptRemaining.text = valid ? HubMoney(remaining) : "—";
            if (valid)
            {
                var previewPaid = bank.Repaid + amount;
                var previewDiscount = closes ? quote.Discount : 0;
                _bankReceiptProgress.fillAmount = progressTotal <= 0 ? 0 : Mathf.Clamp01((previewPaid + previewDiscount) / (float)progressTotal);
                // The counter is money actually paid; a waived discount is never called a payment.
                _bankReceiptProgressText.text = "После оплаты: внесено " + HubMoney(previewPaid) + " тун-тун коинов";
            }
            _bankReceiptAfter.text = valid ? "" : "Укажи от 1 до " + HubMoney(ReceiptMaximum()) + " тун-тун коинов";
            if (ReceiptMaximum() < 1) _bankReceiptAfter.text = "В кошельке пока нет средств для платежа.";
            if (quote.Early && quote.Discount > 0)
                _bankReceiptDiscount.text = "Закрой сейчас за " + HubMoney(quote.Amount) + " — сэкономишь " + HubMoney(quote.Discount) + " тун-тун коинов.\n"
                    + "Ставка 10% действует до " + DateTimeOffset.FromUnixTimeSeconds(bank.SignedAt + HubLoans.EarlySeconds).ToLocalTime().ToString("dd.MM, HH:mm") + ".";
            else _bankReceiptDiscount.text = "Можно вносить частями — от 1 тун-тун коина.\nКаждый платёж сохраняется в архиве.";
            for (var i = 0; i < _bankReceiptPresets.Count; i++)
            {
                var value = ReceiptPresetAmount(_bankReceiptPresetValues[i]);
                _bankReceiptPresets[i].interactable = !ReceiptWorking && value >= 1 && value <= ReceiptMaximum();
            }
        }

        private Button ReceiptButton(Transform parent, string text, Vector2 at, Vector2 size, Action action, bool primary = false)
        {
            var button = LoanPaperButton(parent, text, at, size, action, primary);
            var caption = button.GetComponentInChildren<Text>();
            caption.fontSize = 30;
            caption.rectTransform.sizeDelta = new Vector2(size.x - 12, Mathf.Max(size.y - 4, 45));
            return button;
        }

        private void BuildBankReceipt()
        {
            var paperSize = new Vector2(1320, 880);
            _bankReceiptLayer = LoanRect(_bankLoanLayer, "Bank repayment layer", Vector2.zero, new Vector2(1600, 900));
            _bankReceiptGroup = _bankReceiptLayer.gameObject.AddComponent<CanvasGroup>();
            var dim = LoanArt(_bankReceiptLayer, "Repayment shade", null, Vector2.zero, new Vector2(4800, 2400), new Color(.02f, .015f, .01f, .64f));
            dim.raycastTarget = true; _bankReceiptShade = dim;
            _bankReceiptPaper = BuildCompactReceiptPaper(_bankReceiptLayer, paperSize);
            _bankReceiptTitle = LoanText(_bankReceiptPaper, "Receipt title", "", new Vector2(50, 338), new Vector2(1000, 62), 36, LoanInk, true);
            _bankReceiptIdentity = LoanText(_bankReceiptPaper, "Receipt contract", "", new Vector2(20, 286), new Vector2(1090, 48), 28, LoanInk);
            LoanArt(_bankReceiptPaper, "Receipt divider", null, new Vector2(0, 252), new Vector2(1140, 2), new Color(.5f, .29f, .09f, .5f));
            var columns = new[] { -365f, 0f, 365f };
            var values = new Text[3];
            _bankReceiptHeadings = new Text[3];
            for (var i = 0; i < columns.Length; i++)
            {
                _bankReceiptHeadings[i] = LoanText(_bankReceiptPaper, "Receipt heading " + i, "", new Vector2(columns[i], 206), new Vector2(350, 48), 30, LoanInk);
                values[i] = LoanText(_bankReceiptPaper, "Receipt amount " + i, "", new Vector2(columns[i], 138), new Vector2(350, 80), 48, i == 2 ? ReceiptRed : LoanInk);
                values[i].fontStyle = FontStyle.Bold;
                LoanText(_bankReceiptPaper, "Receipt units " + i, "тун-тун коинов", new Vector2(columns[i], 80), new Vector2(350, 46), 26, LoanInk);
            }
            _bankReceiptTotal = values[0]; _bankReceiptPaid = values[1]; _bankReceiptRemaining = values[2];
            _bankReceiptProgressText = LoanText(_bankReceiptPaper, "Repayment progress label", "", new Vector2(0, -135), new Vector2(1080, 46), 28, LoanInk);
            var track = LoanArt(_bankReceiptPaper, "Repayment progress track", null, new Vector2(0, -172), new Vector2(1080, 10), new Color(.37f, .24f, .1f, .18f));
            _bankReceiptProgress = LoanArt(track.transform, "Repayment progress", null, Vector2.zero, new Vector2(1080, 10), new Color(.36f, .41f, .16f));
            _bankReceiptProgress.sprite = UiTextures.RoundedRect(64, 16, 4, Color.white);
            _bankReceiptProgress.type = Image.Type.Filled; _bankReceiptProgress.fillMethod = Image.FillMethod.Horizontal;
            _bankReceiptForm = LoanRect(_bankReceiptPaper, "Repayment entry", Vector2.zero, paperSize);
            var field = LoanArt(_bankReceiptForm, "Exact payment", null, new Vector2(0, 138), new Vector2(320, 78), new Color(1, .98f, .89f, .85f));
            field.raycastTarget = true;
            LoanArt(field.transform, "Payment underline", null, new Vector2(0, -39), new Vector2(320, 2), new Color(.55f, .30f, .09f));
            var inputText = LoanText(field.transform, "Payment digits", "", Vector2.zero, new Vector2(298, 80), 48, ReceiptRed);
            inputText.fontStyle = FontStyle.Bold;
            _bankReceiptAmountInput = field.gameObject.AddComponent<InputField>();
            _bankReceiptAmountInput.targetGraphic = field; _bankReceiptAmountInput.textComponent = inputText;
            _bankReceiptAmountInput.contentType = InputField.ContentType.IntegerNumber;
            _bankReceiptAmountInput.lineType = InputField.LineType.SingleLine;
            _bankReceiptAmountInput.keyboardType = TouchScreenKeyboardType.NumberPad;
            _bankReceiptAmountInput.characterLimit = 9;
            _bankReceiptAmountInput.customCaretColor = true; _bankReceiptAmountInput.caretColor = LoanInk;
            _bankReceiptAmountInput.selectionColor = new Color(.75f, .45f, .2f, .3f);
            _bankReceiptAmountInput.onValidateInput = (text, index, c) => c >= '0' && c <= '9' ? c : '\0';
            _bankReceiptAmountInput.onValueChanged.AddListener(value => BindBankReceipt());
            _bankReceiptAmountInput.onEndEdit.AddListener(value => { if (ReceiptAmount(out var amount)) _bankReceiptAmountInput.SetTextWithoutNotify(amount.ToString()); });
            var captions = new[] { "1", "10", "100", "ПОЛОВИНА", "ВСЁ" };
            for (var i = 0; i < captions.Length; i++)
            {
                var preset = _bankReceiptPresetValues[i];
                _bankReceiptPresets.Add(ReceiptButton(_bankReceiptForm, captions[i], new Vector2(-432 + i * 216, 5), new Vector2(194, 58), () => SetBankReceiptAmount(preset)));
            }
            _bankReceiptWalletHint = LoanText(_bankReceiptForm, "Payment balance", "", new Vector2(0, -66), new Vector2(1080, 50), 30, LoanInk);
            _bankReceiptDiscount = LoanText(_bankReceiptForm, "Early repayment terms", "", new Vector2(0, -226), new Vector2(1110, 90), 28, LoanInk);
            _bankReceiptDiscount.lineSpacing = .9f;
            _bankReceiptAfter = LoanText(_bankReceiptForm, "Debt after payment", "", new Vector2(0, -299), new Vector2(1080, 46), 28, ReceiptRed);
            _bankReceiptConfirmation = LoanRect(_bankReceiptPaper, "Saved repayment", Vector2.zero, paperSize);
            _bankReceiptAccepted = LoanText(_bankReceiptConfirmation, "Accepted payment", "", new Vector2(-150, -18), new Vector2(760, 116), 32, LoanInk);
            _bankReceiptAccepted.lineSpacing = .9f;
            _bankReceiptSaved = LoanText(_bankReceiptConfirmation, "Saved payment detail", "", new Vector2(-150, -241), new Vector2(770, 116), 28, LoanInk);
            _bankReceiptSaved.lineSpacing = .86f;
            _bankReceiptStamp = LoanArt(_bankReceiptConfirmation, "Paid stamp", "bank-stamp-face", new Vector2(447, -89), new Vector2(230, 230), Color.clear).rectTransform;
            _bankReceiptStamp.localRotation = Quaternion.Euler(0, 0, -12);
            _bankReceiptStampText = LoanText(_bankReceiptStamp, "Paid stamp text", "ОПЛАЧЕНО", new Vector2(0, -58), new Vector2(196, 45), 28, LoanBlue);
            _bankReceiptStampText.fontStyle = FontStyle.Bold;
            // Input validation and the archive text have their own rows above the footer.
            var footerSize = new Vector2(280, 58);
            ReceiptButton(_bankReceiptPaper, "К СТОЛУ", new Vector2(-390, -364), footerSize, CloseBankReceipt);
            _bankReceiptPayButton = ReceiptButton(_bankReceiptPaper, "ОПЛАТИТЬ", new Vector2(390, -364), footerSize, () => _ = PayBankReceiptAsync(), true);
            _bankReceiptArchiveButton = ReceiptButton(_bankReceiptPaper, "КВИТАНЦИИ", new Vector2(0, -364), footerSize, ShowBankReceiptArchive);
            _bankReceiptNextPaymentButton = ReceiptButton(_bankReceiptPaper, "ЕЩЁ ПЛАТЁЖ", new Vector2(390, -364), footerSize, NewBankReceiptPayment, true);
            _bankReceiptHistoryLabel = LoanText(_bankReceiptPaper, "Receipt archive position", "", new Vector2(0, -364), new Vector2(244, 46), 27, LoanInk);
            _bankReceiptPreviousButton = ReceiptButton(_bankReceiptPaper, "‹", new Vector2(-158, -364), new Vector2(58, 58), () => MoveBankReceiptHistory(-1));
            _bankReceiptNextButton = ReceiptButton(_bankReceiptPaper, "›", new Vector2(158, -364), new Vector2(58, 58), () => MoveBankReceiptHistory(1));
            PinToCorner((RectTransform)CityButton(_bankReceiptLayer, "НАЗАД", 24, 24, 170, 56, CloseBankReceipt).transform, -1);
            _bankReceiptLayer.gameObject.SetActive(false);
        }

        private RectTransform BuildCompactReceiptPaper(Transform parent, Vector2 size)
        {
            // Reuse unlettered paper and the crest as UI sprite regions. No raster is modified:
            // a compact native frame leaves the repayment figures in charge of the screen.
            var art = Resources.Load<Sprite>("UI/Hub/bank-receipt-hd");
            if (art != null && _bankReceiptPaperTexture == null)
            {
                var r = art.rect;
                _bankReceiptPaperTexture = Sprite.Create(art.texture,
                    new Rect(r.x + r.width * .13f, r.y + r.height * .15f, r.width * .74f, r.height * .50f),
                    new Vector2(.5f, .5f), art.pixelsPerUnit, 0, SpriteMeshType.FullRect);
                _bankReceiptCrest = Sprite.Create(art.texture,
                    new Rect(r.x + r.width * .432f, r.y + r.height * .698f, r.width * .14f, r.height * .216f),
                    new Vector2(.5f, .5f), art.pixelsPerUnit, 0, SpriteMeshType.FullRect);
            }
            var paper = LoanArt(parent, "Repayment paper", null, Vector2.zero, size, new Color(.22f, .105f, .025f));
            paper.raycastTarget = true;
            var shadow = paper.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, .55f); shadow.effectDistance = new Vector2(5, -7);
            LoanArt(paper.transform, "Receipt gold frame", null, Vector2.zero, size - new Vector2(10, 10), new Color(.87f, .65f, .22f));
            LoanArt(paper.transform, "Receipt frame ink", null, Vector2.zero, size - new Vector2(20, 20), new Color(.43f, .22f, .055f));
            var texture = LoanArt(paper.transform, "Receipt parchment", null, Vector2.zero, size - new Vector2(26, 26), new Color(1, .94f, .75f));
            texture.sprite = _bankReceiptPaperTexture;
            if (texture.sprite != null) texture.color = Color.white;
            var crestRim = LoanArt(paper.transform, "Receipt crest medallion", null, new Vector2(-520, 332), new Vector2(112, 112), new Color(.43f, .22f, .055f));
            crestRim.sprite = UiTextures.Circle(128, Color.white);
            var crestGold = LoanArt(crestRim.transform, "Receipt crest gold rim", null, Vector2.zero, new Vector2(108, 108), new Color(.91f, .72f, .30f));
            crestGold.sprite = crestRim.sprite;
            var crestFace = LoanArt(crestGold.transform, "Receipt crest round face", null, Vector2.zero, new Vector2(100, 100), new Color(1, .94f, .75f));
            crestFace.sprite = crestRim.sprite;
            crestFace.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var crest = LoanArt(crestFace.transform, "Receipt small crest", null, Vector2.zero, new Vector2(102, 105), Color.clear);
            crest.sprite = _bankReceiptCrest; crest.preserveAspect = true;
            if (crest.sprite != null) crest.color = Color.white;
            return paper.rectTransform;
        }
    }
}
