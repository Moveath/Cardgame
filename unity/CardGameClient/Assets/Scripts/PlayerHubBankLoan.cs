using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CardGame.Core.Meta;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>The banker's desk and a server-owned loan offer. The city remains underneath this layer.</summary>
    public sealed partial class PlayerHubController
    {
        private const int LoanCoinCount = 14;
        private static readonly Color LoanInk = new Color(.22f, .115f, .045f);
        private static readonly Color LoanBlue = new Color(.06f, .20f, .43f);
        private static readonly Vector2 LoanPaperSize = new Vector2(1180, 880);
        private RectTransform _bankLoanLayer, _bankLoanDesk, _bankContract, _bankLoanWallet, _bankLoanBubble;
        private RectTransform _bankLoanStamp, _bankLoanPen, _bankMagnifier, _bankFineReading;
        private CanvasGroup _bankLoanGroup, _bankContractGroup, _bankFineGroup, _bankFineReadingGroup, _bankLoanBubbleGroup;
        private Image _bankContractDim, _bankStampImage;
        private Text _bankLoanWalletText, _bankLoanBubbleText, _bankLoanStampText, _bankLoanTitleText;
        private Text _bankLoanAmountText, _bankLoanReturnText, _bankLoanDueText, _bankLoanCollectionsText;
        private Text _bankLoanLimitText, _bankLoanHistoryText, _bankFineText, _bankFineReadingText;
        private Text _bankLoanSignature, _bankLoanDebtText, _bankLoanDebtTerms, _bankLoanPartiesText, _bankLoanDateText;
        private InputField _bankLoanAmountInput;
        private Button _bankLoanTakeButton, _bankLoanDebtButton, _bankLoanAgreeButton, _bankLoanDeclineButton, _bankFineButton;
        private readonly List<Button> _bankLoanAmountButtons = new List<Button>();
        private readonly List<int> _bankLoanAmountValues = new List<int>();
        private readonly RectTransform[] _bankLoanCoins = new RectTransform[LoanCoinCount];
        private bool _bankLoanShown, _bankContractShown, _bankLoanEscapeHeld, _bankLoanApproved, _bankFineDone;
        private float _bankLoanFade, _bankContractFade, _bankLoanBubbleTime = -1;
        private float _bankIssueTime = -1, _bankFineTime = -1, _bankLoanRefreshAt;
        private int _bankAmount = 100, _bankWalletFrom, _bankWalletTo;
        private string _bankOfferId = "", _bankOfferTrick = "", _bankFineOriginal = "", _bankSignatureName = "";
        private bool BankShown => _bankLoanShown || _bankLoanFade > 0;
        private bool LoanWorking => _busy || _bankIssueTime >= 0 || _bankFineInspecting || BankRepaymentAnimating;
        private bool HasActiveBankContract => _state != null && _state.Bank.Debt > 0
            && _state.Bank.Contract != null && !string.IsNullOrEmpty(_state.Bank.Contract.Id);
        private bool LoanReduced => _state != null && _state.Settings.ReducedMotion;
        private long LoanNow => PlayerHubRuntime.Clock.UnixSeconds;
        private bool LoanAmountValid => _bankLoanAmountInput != null
            && int.TryParse(_bankLoanAmountInput.text, out var amount) && amount >= 100 && amount <= LoanMaximum();

        private void ShowBankLoan()
        {
            if (_page != "home" || _cityLevel != BankLevel || _diveTarget >= 0 || _visitPhase != 0 || _mapOpen || _state == null) return;
            if (_bankLoanLayer == null) BuildBankLoan();
            HideBankServices();
            _bankLoanShown = true;
            _bankContractShown = false;
            _bankContractReadOnly = false;
            _bankContractFade = 0;
            _bankIssueTime = _bankFineTime = -1;
            _bankFineInspecting = _bankTrapRevealPending = false;
            _bankFineModalLayer.gameObject.SetActive(false);
            _bankLoanLayer.gameObject.SetActive(true);
            _bankLoanLayer.SetAsLastSibling();
            _bankLoanEscapeHeld = Keyboard.current != null && Keyboard.current.escapeKey.isPressed;
            LoanSay(LoanGreeting(), true);
            PaintBankLoan(0);
        }

        private string LoanGreeting()
        {
            var bank = _state.Bank;
            if (bank.CollectorPending) return "Ребята хотят с тобой сыграть.";
            if (bank.CollectionActive) return "Половина поступлений — Банку. До полного расчёта.";
            if (bank.Debt > 0)
            {
                if (bank.DueAt <= LoanNow) return "Срок вышел, дружок. Я всё помню.";
                var days = Math.Max(1, (bank.DueAt - LoanNow + 86399) / 86400);
                return "Помню-помню. Осталось " + days + " дн., дружок.";
            }
            return "Деньги нужны? У меня их… много.";
        }

        private void CloseBankLoan()
        {
            if (_bankFineInspecting) { CloseBankFineReading(); return; }
            if (TryCloseBankJobs()) return;
            if (_bankProtectionShown) { CloseBankProtection(); return; }
            if (LoanWorking) return;
            if (_bankReceiptShown) { CloseBankReceipt(); return; }
            if (_bankContractShown) { DeclineBankLoan(); return; }
            // A second click during the paper's exit must not also leave the desk.
            if (_bankContractFade > 0) return;
            _bankLoanShown = false;
        }

        private void DeclineBankLoan()
        {
            if (LoanWorking || !_bankContractShown) return;
            _bankContractShown = false;
            if (!_bankContractReadOnly) LoanSay("Как знаешь. Двери открыты.");
            _bankContractReadOnly = false;
        }

        private void OpenBankDebt()
        {
            if (LoanWorking || _bankContractShown || BankServiceShown) return;
            ShowBankReceipt();
        }

        private void OpenBankCollectors()
        {
            if (LoanWorking || _bankContractShown) return;
            _bankLoanShown = false; _bankLoanFade = 0;
            _bankLoanLayer.gameObject.SetActive(false);
            Navigate("bank");
        }

        private async Task OfferBankLoanAsync()
        {
            if (!_bankLoanShown || LoanWorking || _bankContractShown || _bankContractFade > 0 || BankServiceShown) return;
            SetBusy(true);
            var result = await PlayerHubRuntime.ExecuteAsync(new HubCommand { Action = "bank_offer" });
            if (this == null) return;
            SetBusy(false);
            if (!result.Success) { Toast(PlayerHubRuntime.ErrorText(result)); return; }
            _state = result.Snapshot;
            if (_state.Bank.Offer == null) { Toast("Договор пока недоступен"); return; }
            _bankOfferId = _state.Bank.Offer.Id;
            _bankOfferTrick = _state.Bank.Offer.Trick;
            _bankAmount = Mathf.Clamp(_bankAmount, 100, Math.Max(100, LoanMaximum()));
            _bankLoanAmountInput.SetTextWithoutNotify(_bankAmount.ToString());
            _bankLoanSignature.text = "";
            _bankLoanStamp.gameObject.SetActive(false);
            _bankLoanPen.gameObject.SetActive(false);
            _bankFineTime = -1;
            _bankContractShown = true;
            _bankContractReadOnly = false;
            _bankLoanBubbleTime = 0;
            BindBankContract(); ResetBankContractScroll();
            Render();
        }

        private int LoanMaximum()
        {
            var limit = Math.Max(0, _state.Bank.CreditLimit);
            return _state.Profile.Matches < 2 ? limit / 2 : limit;
        }

        private void SetBankLoanAmount(int amount)
        {
            if (!_bankContractShown || _bankContractReadOnly || LoanWorking) return;
            var maximum = LoanMaximum();
            if (maximum < 100) return;
            _bankAmount = Mathf.Clamp(amount, 100, maximum);
            _bankLoanAmountInput.SetTextWithoutNotify(_bankAmount.ToString());
            BindBankContract();
        }

        private void ReadBankLoanAmount(string value)
        {
            if (!_bankContractShown || LoanWorking) return;
            if (int.TryParse(value, out var amount) && amount >= 100 && amount <= LoanMaximum()) _bankAmount = amount;
            BindBankContract();
        }

        private async Task SignBankLoanAsync()
        {
            if (!_bankLoanShown || !_bankContractShown || _bankContractReadOnly || !_bankContractAtEnd || LoanWorking || !LoanAmountValid || string.IsNullOrEmpty(_bankOfferId)) return;
            _bankLoanAmountInput.DeactivateInputField();
            var before = _state.Profile.Coins;
            SetBusy(true);
            var result = await PlayerHubRuntime.ExecuteAsync(new HubCommand { Action = "bank_sign", Text = _bankOfferId, Amount = _bankAmount });
            if (this == null) return;
            SetBusy(false);
            if (!result.Success) { Toast(PlayerHubRuntime.ErrorText(result)); return; }
            _state = result.Snapshot;
            _bankWalletFrom = before;
            _bankWalletTo = _state.Profile.Coins;
            var code = _state.Bank.DecisionCode;
            _bankLoanApproved = code == "approved" || string.IsNullOrEmpty(code) && _state.Bank.Debt > 0;
            _bankLoanStampText.text = _bankLoanApproved ? "ОДОБРЕНО" : "ОТКАЗАНО";
            LoanPaintStamp(_bankLoanApproved);
            FitBankSignature(LoanBorrowerName());
            _bankLoanSignature.text = "";
            _bankTrapRevealPending = _bankLoanApproved && !string.IsNullOrEmpty(_state.Bank.Trick);
            _bankIssueTime = 0;
            Render();
        }

        private Task ReadBankFineAsync() => InspectBankClause("payment");

        private void FitBankSignature(string name)
        {
            _bankSignatureName = name.Length > 28 ? name.Substring(0, 28) : name;
            _bankLoanSignature.fontSize = 34;
            _bankLoanSignature.text = _bankSignatureName;
            var width = _bankLoanSignature.rectTransform.rect.width - 10;
            if (_bankLoanSignature.preferredWidth > width)
                _bankLoanSignature.fontSize = Mathf.Max(1, Mathf.FloorToInt(34 * width / _bankLoanSignature.preferredWidth));
        }

        private string LoanBorrowerName()
        {
            var name = (_state.Profile.DisplayName ?? "").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
            return name.Length == 0 ? "Игрок" : name;
        }

        private void BindBankContract() => BindBankBook();

        private void RefreshLoanButtons()
        {
            var maximum = LoanMaximum();
            for (var i = 0; i < _bankLoanAmountButtons.Count; i++)
            {
                var value = _bankLoanAmountValues[i];
                var target = value == -50 ? _bankAmount - 50 : value == 50 ? _bankAmount + 50 : value == int.MaxValue ? maximum : value;
                _bankLoanAmountButtons[i].interactable = !LoanWorking && !_bankContractReadOnly && target >= 100 && target <= maximum && (target != _bankAmount || !LoanAmountValid);
            }
            _bankLoanAgreeButton.interactable = !LoanWorking && !_bankContractReadOnly && _bankContractAtEnd && LoanAmountValid;
            _bankLoanAmountInput.interactable = !LoanWorking && !_bankContractReadOnly && maximum >= 100;
            _bankLoanDeclineButton.interactable = !LoanWorking;
            foreach (var clause in _bankClauseButtons.Values) clause.interactable = !LoanWorking;
            if (_bankFineChallengeButton != null) _bankFineChallengeButton.interactable = !_busy;
            if (_bankFineCloseButton != null) _bankFineCloseButton.interactable = !_busy;
        }

        private static string LoanFinePrint(string trick)
        {
            switch (trick)
            {
                case "rate": return "*ставка — 40% за весь срок";
                case "fee": return "*ставка 20% и ещё 20% комиссии за оформление";
                case "daily": return "*20% за каждый из трёх дней — всего 60%";
                case "double": return "*при просрочке долг удваивается";
                case "short": return "*срок — 2 дня";
                default: return "";
            }
        }

        private void LoanSay(string line, bool waiting = false)
        {
            _bankLoanBubbleText.text = line;
            _bankLoanBubbleTime = waiting ? -1 : 4.5f;
            _bankLoanBubble.gameObject.SetActive(true);
        }

        private static Vector2 LoanReceiptSize(float width) => new Vector2(width, width * 377f / 558f);

        private static RectTransform LoanRect(Transform parent, string name, Vector2 at, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = at;
            rect.sizeDelta = size;
            return rect;
        }

        private Image LoanArt(Transform parent, string name, string resource, Vector2 at, Vector2 size, Color fallback)
        {
            var image = LoanRect(parent, name, at, size).gameObject.AddComponent<Image>();
            image.sprite = string.IsNullOrEmpty(resource) ? null : Resources.Load<Sprite>("UI/Hub/" + resource);
            image.color = image.sprite == null ? fallback : Color.white;
            image.raycastTarget = false;
            return image;
        }

        private Text LoanText(Transform parent, string name, string text, Vector2 at, Vector2 size, int fontSize, Color color, bool display = false)
        {
            var label = LoanRect(parent, name, at, size).gameObject.AddComponent<Text>();
            label.font = Resources.Load<Font>(display ? "Fonts/SignDisplay" : "Fonts/SignNarrow") ?? _font;
            label.text = text;
            label.supportRichText = false;
            label.fontSize = fontSize;
            label.color = color;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            // SignNarrow has a tall ascender box (1.482 em). Keep glyphs visible even
            // in compact labels; extra line-box height does not enlarge the printed letters.
            label.lineSpacing = .82f;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.rectTransform.sizeDelta = new Vector2(size.x, Mathf.Max(size.y, Mathf.Ceil(fontSize * 1.6f)));
            label.raycastTarget = false;
            return label;
        }

        private Button LoanPaperButton(Transform parent, string text, Vector2 at, Vector2 size, Action action, bool primary = false)
        {
            var image = LoanArt(parent, text, null, at, size, primary ? new Color(.28f, .12f, .035f) : new Color(.87f, .69f, .38f, .7f));
            image.sprite = UiTextures.RoundedRect(64, 64, 7, Color.white);
            image.type = Image.Type.Sliced;
            image.raycastTarget = true;
            var outline = image.gameObject.AddComponent<HubOutline>();
            outline.effectColor = new Color(.38f, .22f, .07f, .75f);
            outline.effectDistance = new Vector2(1, -1);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.disabledColor = new Color(.64f, .60f, .53f, .55f);
            colors.highlightedColor = new Color(1, .93f, .77f);
            button.colors = colors;
            button.onClick.AddListener(() => action());
            LoanText(image.transform, "Caption", text, Vector2.zero, size - new Vector2(8, 4), size.y >= 48 ? 25 : 24,
                primary ? new Color(1, .91f, .68f) : LoanInk);
            return button;
        }

        private void BuildBankLoan()
        {
            _bankLoanLayer = StageLayer("Bank loan desk");
            _bankLoanGroup = _bankLoanLayer.gameObject.AddComponent<CanvasGroup>();
            var shade = LoanArt(_bankLoanLayer, "Bank backdrop", null, Vector2.zero, new Vector2(4800, 2400), new Color(.02f, .015f, .01f));
            shade.raycastTarget = true;
            _bankLoanDesk = LoanArt(_bankLoanLayer, "Bank desk", "bank-desk", Vector2.zero, new Vector2(1600, 900), new Color(.14f, .08f, .035f)).rectTransform;
            _bankLoanTakeButton = CityButton(_bankLoanLayer, "ВЗЯТЬ КРЕДИТ", 600, 745, 400, 74, () => _ = OfferBankLoanAsync());
            _bankLoanDebtButton = CityButton(_bankLoanLayer, "", 560, 610, 480, 94, OpenBankDebt);
            _bankLoanReadButton = CityButton(_bankLoanLayer, "МОЙ ДОГОВОР", 600, 735, 400, 74, ShowSignedBankContract);
            _bankCollectorsButton = CityButton(_bankLoanLayer, "КОЛЛЕКТОРЫ", 625, 835, 350, 48, OpenBankCollectors);
            _bankLoanDebtText = LoanText(_bankLoanDebtButton.transform, "Debt", "", new Vector2(0, 13), new Vector2(456, 40), 31, new Color(1, .90f, .66f));
            _bankLoanDebtTerms = LoanText(_bankLoanDebtButton.transform, "Debt terms", "", new Vector2(0, -23), new Vector2(456, 32), 22, new Color(.95f, .81f, .56f));
            _bankLoanBubble = LoanArt(_bankLoanLayer, "Bank speech", "bank-receipt", new Vector2(-535, 210), LoanReceiptSize(440), new Color(.99f, .90f, .70f)).rectTransform;
            _bankLoanBubble.GetComponent<Image>().preserveAspect = true;
            _bankLoanBubbleGroup = _bankLoanBubble.gameObject.AddComponent<CanvasGroup>();
            _bankLoanBubbleText = LoanText(_bankLoanBubble, "Speech", "", new Vector2(0, -38), new Vector2(356, 148), 30, LoanInk);
            BuildBankDeskServices();
            _bankContractDim = LoanArt(_bankLoanLayer, "Contract dim", null, Vector2.zero, new Vector2(4800, 2400), new Color(0, 0, 0, 0));
            _bankContractDim.raycastTarget = true;
            BuildBankBook();
            BuildBankFineReading();
            _bankLoanWallet = LoanArt(_bankLoanLayer, "Bank wallet", null, new Vector2(646, 396), new Vector2(260, 60), new Color(.08f, .045f, .025f, .95f)).rectTransform;
            _bankLoanWallet.GetComponent<Image>().sprite = UiTextures.RoundedRect(64, 64, 26, Color.white);
            _bankLoanWallet.GetComponent<Image>().type = Image.Type.Sliced;
            var walletCoin = LoanArt(_bankLoanWallet, "Coin", null, new Vector2(-104, 0), new Vector2(64, 64), Color.white);
            walletCoin.sprite = HubCurrencyArt.Silver;
            walletCoin.preserveAspect = true;
            _bankLoanWalletText = LoanText(_bankLoanWallet, "Balance", "", new Vector2(28, 0), new Vector2(184, 50), 35, Color.white);
            PinToCorner(_bankLoanWallet, 1);
            PinToCorner((RectTransform)CityButton(_bankLoanLayer, "НАЗАД", 24, 24, 170, 56, CloseBankLoan).transform, -1);
            for (var i = 0; i < LoanCoinCount; i++)
            {
                var coin = LoanArt(_bankLoanLayer, "Loan flying coin " + i, null, Vector2.zero, new Vector2(64, 64), Color.white);
                coin.sprite = HubCurrencyArt.Silver;
                coin.preserveAspect = true;
                _bankLoanCoins[i] = coin.rectTransform;
                coin.gameObject.SetActive(false);
            }
            BuildBankJobs();
            _bankLoanLayer.gameObject.SetActive(false);
        }
    }
}
