using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CardGame.Core.Meta;
using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    public sealed partial class PlayerHubController
    {
        private bool _bankContractReadOnly, _bankContractAtEnd, _bankFineInspecting, _bankTrapRevealPending;
        private ScrollRect _bankContractScroll;
        private Scrollbar _bankContractScrollbar;
        private RectTransform _bankContractViewport, _bankContractContent, _bankSignatureBlock, _bankScrollThumb, _bankFineModalLayer;
        private Button _bankLoanReadButton, _bankCollectorsButton, _bankBookReceiptButton, _bankFineChallengeButton, _bankFineCloseButton;
        private Text _bankBookRate, _bankBookFee, _bankBookEarly, _bankBookRepayment, _bankBookFreedom,
            _bankBookNewLoan, _bankBookVictory, _bankBookCollection, _bankBookJoke, _bankBookBorrower,
            _bankBookSignedInfo, _bankContractScrollHint, _bankContractPromise, _bankCorrectionText, _bankCorrectionOriginal,
            _bankFineReadingTitle, _bankFineResponse;
        private RectTransform _bankBookEarlyCircle;
        private string _bankInspectedClause = "";
        private static readonly Color LoanRed = new Color(.55f, .075f, .055f);
        private readonly Dictionary<string, Button> _bankClauseButtons = new Dictionary<string, Button>();
        private readonly List<BankContractRow> _bankContractRows = new List<BankContractRow>();
        private sealed class BankContractRow
        {
            public RectTransform Rect;
            public Text Label;
            public float FixedHeight;
        }

        private RectTransform ContractRow(string name, float height)
        {
            var rect = LoanRect(_bankContractContent, name, Vector2.zero, new Vector2(1030, height));
            _bankContractRows.Add(new BankContractRow { Rect = rect, FixedHeight = height });
            return rect;
        }

        private Text ContractParagraph(string name, string text, bool heading = false, string clause = null)
        {
            var row = ContractRow(name + " row", 0);
            var label = LoanText(row, name, text, Vector2.zero, new Vector2(1010, 80), heading ? 34 : 32, LoanInk, heading);
            if (!heading) label.font = _font;
            label.alignment = TextAnchor.UpperLeft;
            label.supportRichText = true;
            label.lineSpacing = 1.12f;
            _bankContractRows[_bankContractRows.Count - 1].Label = label;
            if (clause != null)
            {
                var target = row.gameObject.AddComponent<Image>(); target.color = new Color(.65f, .4f, .12f, .035f);
                var button = row.gameObject.AddComponent<Button>(); button.targetGraphic = target;
                var colors = button.colors; colors.highlightedColor = new Color(1, .89f, .64f); button.colors = colors;
                button.onClick.AddListener(() => _ = InspectBankClause(clause));
                _bankClauseButtons[clause] = button;
            }
            return label;
        }

        private void UnderlineContract(Text label)
        {
            var line = LoanArt(label.transform.parent, "Condition underline", null, Vector2.zero, new Vector2(1000, 1.4f), new Color(.26f, .13f, .045f, .65f)).rectTransform;
            line.anchorMin = line.anchorMax = new Vector2(.5f, 0); line.anchoredPosition = new Vector2(0, 4);
        }

        private void BuildBankBook()
        {
            _bankContract = LoanRect(_bankLoanLayer, "Loan contract", Vector2.zero, LoanPaperSize);
            _bankContractGroup = _bankContract.gameObject.AddComponent<CanvasGroup>();
            var cover = LoanArt(_bankContract, "Document backing", null, Vector2.zero, LoanPaperSize, new Color(.25f, .11f, .035f));
            cover.raycastTarget = true;
            var shadow = cover.gameObject.AddComponent<Shadow>(); shadow.effectColor = new Color(0, 0, 0, .6f); shadow.effectDistance = new Vector2(6, -8);
            LoanArt(_bankContract, "Gold document edge", null, Vector2.zero, LoanPaperSize - new Vector2(10, 10), new Color(.68f, .44f, .15f));
            var paper = LoanArt(_bankContract, "Paper", null, Vector2.zero, LoanPaperSize - new Vector2(25, 25), new Color(1, .95f, .8f));
            var source = Resources.Load<Sprite>("UI/Hub/bank-contract");
            if (source != null)
            {
                var r = source.rect;
                paper.sprite = Sprite.Create(source.texture, new Rect(r.x + r.width * .15f, r.y + r.height * .18f, r.width * .70f, r.height * .50f), new Vector2(.5f, .5f), source.pixelsPerUnit);
                paper.color = Color.white;
            }
            _bankLoanTitleText = LoanText(_bankContract, "Contract title", "", new Vector2(-25, 390), new Vector2(1000, 60), 32, LoanInk, true);
            LoanArt(_bankContract, "Header rule", null, new Vector2(0, 346), new Vector2(1050, 2), new Color(.5f, .29f, .09f, .5f));
            _bankContractViewport = LoanRect(_bankContract, "Contract viewport", new Vector2(-5, -9), new Vector2(1050, 694));
            var capture = _bankContractViewport.gameObject.AddComponent<Image>(); capture.color = Color.clear;
            _bankContractViewport.gameObject.AddComponent<RectMask2D>();
            _bankContractContent = LoanRect(_bankContractViewport, "Contract content", Vector2.zero, new Vector2(1030, 2600));
            _bankContractContent.anchorMin = _bankContractContent.anchorMax = _bankContractContent.pivot = new Vector2(.5f, 1);
            _bankContractScroll = _bankContractViewport.gameObject.AddComponent<HubSmoothScroll>();
            _bankContractScroll.viewport = _bankContractViewport; _bankContractScroll.content = _bankContractContent;
            _bankContractScroll.horizontal = false; _bankContractScroll.vertical = true;
            _bankContractScroll.movementType = ScrollRect.MovementType.Clamped;
            _bankContractScroll.scrollSensitivity = 36; _bankContractScroll.decelerationRate = .16f;
            ((HubSmoothScroll)_bankContractScroll).WheelSmoothTime = .42f;
            _bankContractScroll.onValueChanged.AddListener(_ => PaintBankBook(0));
            // The artwork is slim, but the entire gutter can be grabbed with a finger.
            var gutter = LoanArt(_bankContract, "Document scrollbar", null, new Vector2(548, -9), new Vector2(48, 682), Color.clear);
            gutter.raycastTarget = true;
            LoanArt(gutter.transform, "Document scroll track", null, Vector2.zero, new Vector2(6, 682), new Color(.45f, .26f, .08f, .18f));
            var handle = LoanArt(gutter.transform, "Document scroll position", null, Vector2.zero, new Vector2(48, 130), Color.clear);
            handle.raycastTarget = true; _bankScrollThumb = handle.rectTransform;
            _bankScrollThumb.sizeDelta = Vector2.zero;
            var grip = LoanArt(handle.transform, "Scroll grip", null, Vector2.zero, new Vector2(16, 0), new Color(.53f, .31f, .10f));
            grip.sprite = UiTextures.RoundedRect(24, 48, 8, Color.white); grip.type = Image.Type.Sliced;
            grip.rectTransform.anchorMin = new Vector2(.5f, 0); grip.rectTransform.anchorMax = new Vector2(.5f, 1);
            for (var i = -1; i <= 1; i++)
                LoanArt(handle.transform, "Grip mark " + i, null, new Vector2(0, i * 7), new Vector2(8, 2), new Color(1, .82f, .46f, .85f));
            var scrollbar = gutter.gameObject.AddComponent<HubDragScrollbar>();
            scrollbar.ScrollView = (HubSmoothScroll)_bankContractScroll;
            scrollbar.direction = Scrollbar.Direction.BottomToTop; scrollbar.handleRect = _bankScrollThumb;
            scrollbar.targetGraphic = grip; scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };
            var barColors = scrollbar.colors; barColors.highlightedColor = new Color(1, .87f, .63f);
            barColors.pressedColor = new Color(1, .94f, .8f); scrollbar.colors = barColors;
            _bankContractScrollbar = scrollbar; _bankContractScroll.verticalScrollbar = scrollbar;
            _bankContractScrollHint = LoanText(_bankContract, "Scroll hint", "Прокрутите вниз к подписи", new Vector2(0, -395), new Vector2(1010, 48), 25, LoanInk);
            var date = ContractRow("Contract date row", 45);
            _bankLoanDateText = LoanText(date, "Contract date", "", new Vector2(290, 0), new Vector2(430, 43), 28, LoanInk);
            _bankLoanDateText.font = _font; _bankLoanDateText.alignment = TextAnchor.MiddleRight;
            var city = LoanText(date, "Contract city", "г. Card City", new Vector2(-295, 0), new Vector2(420, 43), 28, LoanInk); city.font = _font; city.alignment = TextAnchor.MiddleLeft;
            _bankContractPromise = ContractParagraph("Banker's promise", ""); _bankContractPromise.color = new Color(.38f, .22f, .09f);
            _bankCorrectionOriginal = ContractParagraph("Original disputed condition", ""); _bankCorrectionOriginal.fontSize = 28; _bankCorrectionOriginal.color = new Color(.46f, .29f, .16f);
            LoanArt(_bankCorrectionOriginal.transform, "Cancelled original condition", null, new Vector2(-100, 0), new Vector2(790, 2), LoanBlue);
            _bankCorrectionText = ContractParagraph("Corrected conditions", ""); _bankCorrectionText.color = LoanBlue;
            _bankLoanPartiesText = ContractParagraph("Contract parties", ""); _bankLoanPartiesText.supportRichText = false;
            ContractParagraph("Section one", "1. ПРЕДМЕТ ДОГОВОРА", true);
            ContractParagraph("Loan clause", "1.1. Банк передаёт Заёмщику денежные средства в размере:");
            var amountRow = ContractRow("Loan amount controls", 210);
            var amount = LoanArt(amountRow, "Custom loan amount", null, new Vector2(-145, 65), new Vector2(285, 64), new Color(1, .98f, .89f, .75f)); amount.raycastTarget = true;
            LoanArt(amount.transform, "Amount underline", null, new Vector2(0, -31), new Vector2(280, 2), LoanRed);
            _bankLoanAmountText = LoanText(amount.transform, "Loan amount", "100", Vector2.zero, new Vector2(269, 64), 40, LoanRed); _bankLoanAmountText.fontStyle = FontStyle.Bold;
            _bankLoanAmountInput = amount.gameObject.AddComponent<InputField>();
            _bankLoanAmountInput.targetGraphic = amount; _bankLoanAmountInput.textComponent = _bankLoanAmountText;
            _bankLoanAmountInput.contentType = InputField.ContentType.IntegerNumber; _bankLoanAmountInput.lineType = InputField.LineType.SingleLine;
            _bankLoanAmountInput.keyboardType = TouchScreenKeyboardType.NumberPad; _bankLoanAmountInput.characterLimit = 7;
            _bankLoanAmountInput.customCaretColor = true; _bankLoanAmountInput.caretColor = LoanInk;
            _bankLoanAmountInput.selectionColor = new Color(.75f, .45f, .2f, .3f);
            _bankLoanAmountInput.onValidateInput = (text, index, c) => c >= '0' && c <= '9' ? c : '\0';
            _bankLoanAmountInput.SetTextWithoutNotify("100"); _bankLoanAmountInput.onValueChanged.AddListener(ReadBankLoanAmount);
            _bankLoanAmountInput.onEndEdit.AddListener(value => { if (LoanAmountValid) _bankLoanAmountInput.SetTextWithoutNotify(_bankAmount.ToString()); });
            var currency = LoanText(amountRow, "Loan currency", "тун-тун коинов", new Vector2(175, 65), new Vector2(315, 64), 36, LoanRed); currency.fontStyle = FontStyle.Bold;
            var captions = new[] { "−", "100", "500", "1000", "МАКС", "+" }; var values = new[] { -50, 100, 500, 1000, int.MaxValue, 50 };
            for (var i = 0; i < values.Length; i++)
            {
                var value = values[i];
                var button = LoanPaperButton(amountRow, captions[i], new Vector2(-425 + i * 170, -15), new Vector2(153, 60),
                    () => SetBankLoanAmount(value == -50 ? _bankAmount - 50 : value == 50 ? _bankAmount + 50 : value == int.MaxValue ? LoanMaximum() : value));
                _bankLoanAmountButtons.Add(button); _bankLoanAmountValues.Add(value);
            }
            _bankLoanHistoryText = LoanText(amountRow, "History limit", "", new Vector2(0, -78), new Vector2(1010, 50), 27, LoanInk);
            _bankBookSignedInfo = ContractParagraph("Signed contract record", "");
            ContractParagraph("Return undertaking", "1.2. Заёмщик обязуется вернуть сумму займа и проценты в срок, установленный настоящим Договором.");
            ContractParagraph("Section two", "2. ПРОЦЕНТЫ И СРОК", true);
            _bankBookRate = ContractParagraph("Interest clause", "", false, "rate");
            _bankBookFee = ContractParagraph("Processing clause", "", false, "fee");
            _bankLoanReturnText = ContractParagraph("Repayment clause", "");
            _bankLoanDueText = ContractParagraph("Deadline clause", "", false, "short");
            _bankBookEarly = ContractParagraph("Early settlement clause", "");
            _bankBookEarlyCircle = LoanRect(_bankBookEarly.transform.parent, "Six helper blue circle", new Vector2(-218, 0), new Vector2(566, 49));
            _bankBookEarlyCircle.anchorMin = _bankBookEarlyCircle.anchorMax = new Vector2(.5f, 0); _bankBookEarlyCircle.anchoredPosition = new Vector2(-218, 28);
            for (var i = 0; i < 60; i++)
            {
                Vector2 Point(float a) => new Vector2(Mathf.Cos(a) * (282 + 3 * Mathf.Sin(a * 5)), Mathf.Sin(a) * (24 + 2 * Mathf.Sin(a * 3)));
                var a = i * Mathf.PI * 2 / 60; var b = (i + 1) * Mathf.PI * 2 / 60;
                var from = Point(a); var to = Point(b); var delta = to - from;
                var stroke = LoanArt(_bankBookEarlyCircle, "Pen stroke " + i, null, (from + to) / 2, new Vector2(delta.magnitude + 1, 1.8f), LoanBlue);
                stroke.rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            }
            ContractParagraph("Section three", "3. ПОРЯДОК ПОГАШЕНИЯ", true);
            _bankBookRepayment = ContractParagraph("Partial repayment clause", "3.1. Заёмщик вправе погашать долг полностью или частями, в любом размере от 1 тун-тун коина.");
            _bankBookFreedom = ContractParagraph("Freedom of stakes clause", "");
            _bankBookNewLoan = ContractParagraph("No second loan clause", "3.3. Новый заём предоставляется только после полного погашения действующего."); UnderlineContract(_bankBookNewLoan);
            ContractParagraph("Section four", "4. ОТВЕТСТВЕННОСТЬ СТОРОН", true);
            _bankLoanCollectionsText = ContractParagraph("Collectors clause", "", false, "collection"); UnderlineContract(_bankLoanCollectionsText);
            _bankBookVictory = ContractParagraph("Collector victory clause", "");
            _bankBookCollection = ContractParagraph("Withholding clause", ""); UnderlineContract(_bankBookCollection);
            _bankBookJoke = ContractParagraph("Bank disclaimer joke", "<b>4.4. Банк ответственности не несёт. Никогда.</b>");
            _bankFineText = ContractParagraph("Fine print", "", false, "payment"); _bankFineText.fontSize = 28; _bankFineText.color = new Color(.37f, .24f, .12f);
            _bankFineButton = _bankClauseButtons["payment"]; _bankFineGroup = _bankFineButton.gameObject.AddComponent<CanvasGroup>();
            _bankLoanLimitText = ContractParagraph("Loan limit", "");
            _bankSignatureBlock = ContractRow("Signatures and decision", 340);
            var banker = LoanText(_bankSignatureBlock, "Bank signatory", "БАНК:\nПредседатель правления\nТуз Треф", new Vector2(-290, 70), new Vector2(430, 120), 28, LoanInk); banker.font = _font; banker.alignment = TextAnchor.UpperLeft; banker.lineSpacing = 1.1f;
            _bankBookBorrower = LoanText(_bankSignatureBlock, "Borrower signature caption", "ЗАЁМЩИК:", new Vector2(200, 105), new Vector2(280, 50), 28, LoanInk); _bankBookBorrower.font = _font; _bankBookBorrower.alignment = TextAnchor.MiddleLeft;
            LoanArt(_bankSignatureBlock, "Signature rule", null, new Vector2(200, 27), new Vector2(280, 1), LoanInk);
            _bankLoanSignature = LoanText(_bankSignatureBlock, "Signature", "", new Vector2(200, 48), new Vector2(260, 60), 34, LoanBlue); _bankLoanSignature.fontStyle = FontStyle.Italic; _bankLoanSignature.alignment = TextAnchor.MiddleLeft;
            _bankLoanDeclineButton = LoanPaperButton(_bankSignatureBlock, "ОТКАЗАТЬСЯ", new Vector2(-285, -95), new Vector2(410, 66), DeclineBankLoan);
            _bankLoanAgreeButton = LoanPaperButton(_bankSignatureBlock, "ПОДПИСАТЬ", new Vector2(285, -95), new Vector2(410, 66), () => _ = SignBankLoanAsync(), true);
            _bankBookReceiptButton = LoanPaperButton(_bankSignatureBlock, "КВИТАНЦИИ", new Vector2(285, -95), new Vector2(410, 66), () => { _bankContractShown = false; _bankContractFade = 0; ShowBankReceipt(); }, true);
            _bankLoanPen = LoanArt(_bankSignatureBlock, "Signing pen", "bank-pen", Vector2.zero, new Vector2(185, 136), LoanInk).rectTransform;
            _bankLoanPen.pivot = new Vector2(.96f, .06f); _bankLoanPen.GetComponent<Image>().preserveAspect = true;
            _bankLoanStamp = LoanRect(_bankSignatureBlock, "Decision stamp", new Vector2(440, 70), new Vector2(180, 180));
            _bankStampImage = LoanArt(_bankLoanStamp, "Stamp face", "bank-stamp-face", Vector2.zero, new Vector2(180, 180), Color.clear);
            _bankLoanStampText = LoanText(_bankLoanStamp, "Stamp word", "", new Vector2(0, -48), new Vector2(160, 35), 23, LoanBlue); _bankLoanStampText.fontStyle = FontStyle.Bold;
        }

        private void LayoutBankContract()
        {
            var y = 16f;
            foreach (var row in _bankContractRows)
            {
                if (row.Label != null && string.IsNullOrEmpty(row.Label.text)) { row.Rect.gameObject.SetActive(false); continue; }
                row.Rect.gameObject.SetActive(true);
                var height = row.Label == null ? row.FixedHeight : Mathf.Ceil(row.Label.preferredHeight) + 22;
                row.Rect.anchorMin = row.Rect.anchorMax = new Vector2(.5f, 1); row.Rect.pivot = new Vector2(.5f, 1);
                row.Rect.sizeDelta = new Vector2(1030, height); row.Rect.anchoredPosition = new Vector2(0, -y);
                if (row.Label != null) { row.Label.rectTransform.sizeDelta = new Vector2(1010, height - 16); row.Label.rectTransform.anchoredPosition = Vector2.zero; }
                y += height + 14;
            }
            _bankContractContent.sizeDelta = new Vector2(1030, y + 10);
            Canvas.ForceUpdateCanvases(); PaintBankBook(0);
        }

        private void ResetBankContractScroll()
        {
            _bankContractScroll.StopMovement(); _bankContractScroll.verticalNormalizedPosition = 1;
            _bankContractContent.anchoredPosition = Vector2.zero; _bankContractAtEnd = false;
        }

        private void PaintBankBook(float deltaTime)
        {
            if (_bankContractScroll == null) return;
            _bankContractAtEnd = _bankContractScroll.verticalNormalizedPosition <= .015f;
            _bankContractScroll.enabled = !_busy && _bankIssueTime < 0 && !_bankFineInspecting;
            _bankBookReceiptButton.gameObject.SetActive(_bankContractReadOnly);
            _bankBookReceiptButton.interactable = !LoanWorking;
            _bankContractScrollHint.text = _bankContractAtEnd ? "Конец договора" : "Прокрутите вниз к подписи · пункты с примечаниями можно нажать";
            _bankContractScrollbar.interactable = _bankContractScroll.enabled && _bankContractShown;
        }

        private static string BookRed(string text) => "<color=#8F1916><b>" + text + "</b></color>";
        private const string BookNote = " <color=#735131><size=25>· примечание</size></color>";

        private void BindBankBook()
        {
            var bank = _state.Bank; var offer = bank.Offer; var saved = _bankContractReadOnly ? bank.Contract : null;
            if (saved == null && offer != null && offer.Id == _bankOfferId) _bankOfferTrick = offer.Trick ?? "";
            var trick = saved != null ? saved.Trick : _bankOfferTrick; var version = saved != null ? saved.TermsVersion : 2;
            var principal = saved != null ? saved.Principal : _bankAmount;
            var number = saved != null ? saved.Number : offer != null ? offer.Number : bank.OfferSequence;
            var signedAt = saved != null ? saved.SignedAt : LoanNow; var date = DateTimeOffset.FromUnixTimeSeconds(signedAt).ToLocalTime();
            var due = DateTimeOffset.FromUnixTimeSeconds(saved != null ? saved.OriginalDueAt : LoanNow + HubLoans.Days(trick) * 86400L).ToLocalTime();
            var borrower = saved != null ? (saved.Borrower ?? "Игрок").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ') : LoanBorrowerName();
            if (string.IsNullOrWhiteSpace(borrower)) borrower = "Игрок";
            _bankLoanTitleText.text = "ДОГОВОР ЗАЙМА № " + number.ToString("D4") + "-ТЗ";
            _bankLoanDateText.text = date.ToString("dd.MM.yyyy");
            _bankContractPromise.text = "Тузяра обещал: «" + (version >= 2 ? "Двадцать" : "Десять") + " процентов. Три дня. Всё по-честному»";
            var corrected = saved != null ? saved.TrickChallenged : offer != null && offer.Revealed;
            var original = saved != null ? saved.OriginalTrick : offer != null ? offer.OriginalTrick : "";
            _bankCorrectionOriginal.text = corrected && !string.IsNullOrEmpty(original) ? "Прежнее условие: " + LoanFinePrint(original).TrimStart('*') : "";
            _bankCorrectionText.text = corrected ? "20% за весь срок, без комиссии, 3 дня.\nТуз Треф. Исправленному верить." : "";
            _bankLoanPartiesText.text = "АО «Банк Тузяры», в лице Председателя правления Туза Треф, действующего на основании Устава и личного авторитета (далее — «Банк»), и " + borrower + " (далее — «Заёмщик») заключили настоящий Договор:";
            if (saved != null) _bankLoanAmountInput.SetTextWithoutNotify(principal.ToString());
            var valid = saved != null || LoanAmountValid; var total = saved != null ? saved.TotalDebt : HubLoans.Debt(principal, trick, version);
            var rate = trick == "daily" ? (version >= 2 ? 20 : 10) : HubLoans.InterestPercent(trick, version);
            _bankBookRate.text = "2.1. Процентная ставка — " + rate + "%" + (trick == "daily" ? "*." : " за весь срок.") + BookNote;
            _bankBookFee.text = trick == "fee" ? "2.2. Комиссия за оформление — 20% от суммы займа." + BookNote : "2.2. Оформление договора — без комиссии." + BookNote;
            _bankLoanReturnText.text = "2.3. Сумма к возврату — " + BookRed(valid ? HubMoney(total) : "—") + " тун-тун коинов.";
            _bankLoanDueText.text = "2.4. Срок возврата — 3 (три) дня" + (trick == "short" ? "*" : "") + ", не позднее " + BookRed(due.ToString("dd.MM.yyyy, HH:mm")) + "." + BookNote;
            _bankBookEarly.text = version >= 2 ? "2.5. При полном погашении за первые сутки\nставка снижается до <b>10%.</b>" : "2.5. Условия ранее подписанного договора сохраняются. Досрочная скидка не предусмотрена.";
            _bankBookEarlyCircle.gameObject.SetActive(version >= 2);
            _bankBookFreedom.text = version >= 2 ? "3.2. До окончания срока Заёмщик свободен в выборе столов и ставок." : "3.2. Пока есть долг, доступны только столы без ставок.";
            _bankLoanCollectionsText.text = (version >= 2 ? "4.1. По истечении срока" : "4.1. Через 12 часов просрочки") + " к Заёмщику направляются представители Банка (братья Вальты Бубен) для партии в игру по выбору Банка." + BookNote;
            _bankBookVictory.text = version >= 2 ? "4.2. Победа Заёмщика продлевает срок на 1 (одни) сутки. Продление предоставляется однократно." : "4.2. В случае победы Банк списывает четверть оставшегося долга.";
            _bankBookCollection.text = version >= 2 ? "4.3. При поражении либо по истечении продления 50% всех поступлений Заёмщика направляются в счёт долга до полного погашения." : "4.3. При поражении Банк списывает доступный баланс. 50% поступлений идут в погашение долга.";
            _bankLoanLimitText.text = "Лимит Заёмщика: " + HubMoney(saved != null ? saved.CreditLimit : bank.CreditLimit) + " тун-тун коинов.";
            _bankBookBorrower.text = "ЗАЁМЩИК:";
            _bankLoanHistoryText.text = !valid ? "Введите сумму от 100 до " + HubMoney(LoanMaximum()) : "Своя сумма — нажмите на число. Доступно до " + HubMoney(LoanMaximum());
            _bankLoanHistoryText.color = valid ? LoanInk : LoanRed;
            _bankBookSignedInfo.text = saved == null ? "" : "Подписан " + date.ToString("dd.MM.yyyy, HH:mm") + " · сохранённый экземпляр";
            foreach (var button in _bankLoanAmountButtons) button.gameObject.SetActive(saved == null);
            _bankLoanHistoryText.gameObject.SetActive(saved == null);
            _bankFineText.text = version >= 2 ? "Примечание. Срок отсчитывается с момента подписания. Частичные платежи учитываются при досрочном погашении. Нажмите, чтобы прочитать подробнее." : "Примечание. Это ранее подписанный договор. Его условия сохраняются; досрочная скидка не предусмотрена. Нажмите, чтобы прочитать подробнее.";
            _bankFineButton.gameObject.SetActive(true); _bankFineGroup.alpha = 1;
            if (saved != null) FitBankSignature(borrower);
            LayoutBankContract(); RefreshLoanButtons();
        }

        private void ShowSignedBankContract()
        {
            if (!_bankLoanShown || LoanWorking || !HasActiveBankContract || _bankReceiptShown || BankServiceShown) return;
            _bankContractReadOnly = true; _bankContractShown = true; _bankLoanBubbleTime = 0;
            _bankLoanStampText.text = "ПОДПИСАН"; LoanPaintStamp(true); BindBankContract(); ResetBankContractScroll();
        }

        private void BuildBankFineReading()
        {
            _bankFineModalLayer = LoanRect(_bankLoanLayer, "Clause reading overlay", Vector2.zero, new Vector2(1600, 900));
            var dim = LoanArt(_bankFineModalLayer, "Clause reading shade", null, Vector2.zero, new Vector2(4800, 2400), new Color(.04f, .025f, .012f, .7f)); dim.raycastTarget = true;
            _bankFineReading = LoanArt(_bankFineModalLayer, "Fine print reading", null, Vector2.zero, new Vector2(1060, 630), new Color(1, .94f, .78f)).rectTransform;
            _bankFineReading.GetComponent<Image>().raycastTarget = true;
            var edge = _bankFineReading.gameObject.AddComponent<HubOutline>(); edge.effectColor = new Color(.45f, .25f, .075f); edge.effectDistance = new Vector2(4, -4);
            _bankFineReadingGroup = _bankFineReading.gameObject.AddComponent<CanvasGroup>();
            _bankFineReadingTitle = LoanText(_bankFineReading, "Fine print reading title", "ПРИМЕЧАНИЕ К ДОГОВОРУ", new Vector2(0, 250), new Vector2(940, 60), 33, LoanInk, true);
            _bankFineReadingText = LoanText(_bankFineReading, "Magnified fine print", "", new Vector2(0, 85), new Vector2(920, 235), 34, LoanInk); _bankFineReadingText.font = _font; _bankFineReadingText.lineSpacing = 1.15f;
            _bankFineResponse = LoanText(_bankFineReading, "Banker's response", "", new Vector2(0, -117), new Vector2(920, 130), 30, LoanBlue); _bankFineResponse.font = _font; _bankFineResponse.lineSpacing = 1.1f;
            _bankFineCloseButton = LoanPaperButton(_bankFineReading, "ПОНЯТНО", new Vector2(-250, -245), new Vector2(430, 68), CloseBankFineReading);
            _bankFineChallengeButton = LoanPaperButton(_bankFineReading, "ОСПОРИТЬ УСЛОВИЕ", new Vector2(250, -245), new Vector2(430, 68), () => _ = ChallengeBankFineAsync(), true);
            _bankMagnifier = LoanArt(_bankFineReading, "Magnifier", "bank-magnifier", new Vector2(-464, 250), new Vector2(68, 68), LoanInk).rectTransform; _bankMagnifier.GetComponent<Image>().preserveAspect = true;
            _bankFineModalLayer.gameObject.SetActive(false);
        }

        private string InspectedBankTrick => _bankContractReadOnly && _state.Bank.Contract != null ? _state.Bank.Contract.Trick : _bankOfferTrick;

        private string BankClauseExplanation(string key)
        {
            var trick = InspectedBankTrick;
            var legacy = _bankContractReadOnly && _state.Bank.Contract != null && _state.Bank.Contract.TermsVersion < 2;
            switch (key)
            {
                case "rate": return trick == "rate" ? "Процентная ставка — 40% за весь срок.\nТузяра обещал 20%." : trick == "daily" ? "Процент начисляется за каждый день. За три дня ставка составит " + HubLoans.InterestPercent(trick, _bankContractReadOnly ? _state.Bank.Contract.TermsVersion : 2) + "%." : "Проценты начисляются один раз за весь срок займа. Ставка соответствует договору.";
                case "fee": return trick == "fee" ? "К процентам добавляется комиссия за оформление: ещё 20% от суммы займа.\nВсего — 40% сверх суммы займа." : "Дополнительной комиссии за оформление нет. В сумму возврата входят только заём и проценты.";
                case "short": return trick == "short" ? (_state.Bank.OfferSequence % 2 == 0 ? "Индивидуальный срок этого предложения — 2 дня. Указание на три дня к нему не применяется." : "Заёмщик обязуется вернуть деньги через 48 часов после подписания. Обещанные три дня сокращены до двух.") : "На возврат даются три полных дня с момента подписания. Точная дата указана в договоре.";
                case "collection": return trick == "double" ? "При первой просрочке остаток долга удваивается. Это условие действует дополнительно к приходу коллекторов." : legacy ? "Коллекторы приходят через 12 часов после срока. Победа списывает четверть остатка долга. При поражении списывается доступный баланс; половина поступлений идёт в погашение." : "Коллекторы приходят по истечении срока. Победа даёт одно продление; условия сохраняются в подписанном экземпляре.";
                default: return legacy ? "Срок отсчитывается с момента подписания. Частичные платежи уменьшают долг. У этого ранее подписанного договора нет досрочной скидки; сохраняются первоначальные условия." : "Срок отсчитывается с момента подписания. Каждый частичный платёж уменьшает долг. Досрочные 10% применяются при полном погашении в первые 24 часа, с учётом уже внесённых денег.";
            }
        }

        private async Task InspectBankClause(string key)
        {
            if (!_bankContractShown || LoanWorking || !_bankClauseButtons.ContainsKey(key)) return;
            _bankLoanAmountInput.DeactivateInputField(); _bankContractScroll.StopMovement();
            _bankInspectedClause = key; _bankFineInspecting = true; _bankFineTime = 0;
            _bankFineReadingText.color = LoanInk;
            _bankFineReadingTitle.text = "ПРИМЕЧАНИЕ К ДОГОВОРУ"; _bankFineReadingText.text = BankClauseExplanation(key);
            _bankFineResponse.text = _bankContractReadOnly ? "Условия сохранены при подписании." : "Не совпадает с обещанием Тузяры? Можно возразить.";
            _bankFineChallengeButton.gameObject.SetActive(!_bankContractReadOnly); _bankFineModalLayer.gameObject.SetActive(true); _bankFineModalLayer.SetAsLastSibling();
            if (_bankContractReadOnly) return;
            SetBusy(true);
            var result = await PlayerHubRuntime.ExecuteAsync(new HubCommand { Action = "bank_read_fine", Text = _bankOfferId });
            if (this == null) return;
            SetBusy(false);
            if (result.Success) _state = result.Snapshot; else _bankFineResponse.text = PlayerHubRuntime.ErrorText(result);
        }

        private async Task ChallengeBankFineAsync()
        {
            if (!_bankFineInspecting || _busy || _bankContractReadOnly || string.IsNullOrEmpty(_bankInspectedClause)) return;
            var target = _bankInspectedClause;
            if (target == "rate" && _bankOfferTrick == "daily") target = "daily";
            if (target == "collection" && _bankOfferTrick == "double") target = "double";
            SetBusy(true);
            var result = await PlayerHubRuntime.ExecuteAsync(new HubCommand { Action = "bank_challenge_fine", Text = _bankOfferId, Target = target });
            if (this == null) return;
            SetBusy(false);
            if (!result.Success) { _bankFineResponse.text = result.Error == "fine_print" ? "«Здесь всё как обещано. Читай внимательнее, дружок»." : PlayerHubRuntime.ErrorText(result); return; }
            _state = result.Snapshot; _bankOfferTrick = _state.Bank.Offer.Trick; _bankFineDone = true;
            _bankFineReadingTitle.text = "ТУЗЯРА ПОПАЛСЯ";
            _bankFineResponse.text = "«Ладно, тебя не проведёшь. Уберём… техническую неточность».";
            _bankFineReadingText.text = "Условие исправлено: 20% за весь срок, без комиссии, три дня на возврат.\nК возврату: " + HubMoney(HubLoans.Debt(_bankAmount, "")) + " тун-тун коинов.";
            _bankFineChallengeButton.gameObject.SetActive(false); BindBankContract();
            Toast("Подвох найден. Достижение «Мелкий шрифт» — в Архиве.");
        }

        private void CloseBankFineReading()
        {
            if (_busy) return;
            _bankFineInspecting = false; _bankFineTime = -1; _bankInspectedClause = "";
            _bankFineModalLayer.gameObject.SetActive(false); PaintBankBook(0); RefreshLoanButtons();
        }

        private void RevealSignedBankTrick()
        {
            _bankTrapRevealPending = false;
            var bank = _state.Bank; if (string.IsNullOrEmpty(bank.Trick)) return;
            _bankFineInspecting = true; _bankFineTime = 0; _bankInspectedClause = "";
            _bankFineReadingTitle.text = "ДОГОВОР УЖЕ ПОДПИСАН"; _bankFineReadingText.color = LoanRed;
            _bankFineReadingText.text = LoanFinePrint(bank.Trick).TrimStart('*') + ".\nПо договору вернуть " + HubMoney(bank.Contract != null ? bank.Contract.TotalDebt : bank.Debt) + " тун-тун коинов до " + DateTimeOffset.FromUnixTimeSeconds(bank.DueAt).ToLocalTime().ToString("dd.MM, HH:mm") + ".";
            _bankFineResponse.text = "«Надо было читать, дружок. Я ведь всё написал».\nВ первые сутки ещё можно закрыть заём под 10%.";
            _bankFineChallengeButton.gameObject.SetActive(false); _bankFineModalLayer.gameObject.SetActive(true); _bankFineModalLayer.SetAsLastSibling();
        }
    }
}
