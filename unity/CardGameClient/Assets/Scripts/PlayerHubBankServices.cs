using System;
using System.Collections.Generic;
using CardGame.Core.Meta;
using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    public sealed partial class PlayerHubController
    {
        private Button _bankJobsDeskButton, _bankProtectionDeskButton;
        private RectTransform _bankJobsDeskBadge, _bankProtectionLayer, _bankProtectionCard;
        private Text _bankJobsDeskBadgeText;
        private bool _bankProtectionShown;
        private static readonly Dictionary<string, Sprite> BankObjectSprites = new Dictionary<string, Sprite>();
        private bool BankServiceShown => BankJobsShown || _bankProtectionShown;

        // Sprite regions trim only transparent padding; the supplied PNG files stay unchanged.
        private static Sprite BankObjectSprite(string resource)
        {
            if (BankObjectSprites.TryGetValue(resource, out var saved) && saved != null) return saved;
            var source = Resources.Load<Sprite>("UI/Hub/" + resource);
            if (source == null) return null;
            Rect box;
            switch (resource)
            {
                case "bank-jobs-folder": box = new Rect(110f/1448, 100f/1086, 1268f/1448, 878f/1086); break;
                case "bank-job-envelope": box = new Rect(53f/1448, 158f/1086, 1342f/1448, 770f/1086); break;
                case "bank-protection-card": box = new Rect(30f/1536, 106f/1024, 1477f/1536, 818f/1024); break;
                default: return source;
            }
            var r = source.rect;
            saved = Sprite.Create(source.texture, new Rect(r.x + box.x * r.width, r.y + box.y * r.height,
                box.width * r.width, box.height * r.height), new Vector2(.5f, .5f), source.pixelsPerUnit, 0, SpriteMeshType.FullRect);
            saved.name = resource + "-object";
            BankObjectSprites[resource] = saved;
            return saved;
        }

        private void BuildBankDeskServices()
        {
            _bankJobsDeskButton = BankDeskObject("Personal jobs folder", "bank-jobs-folder", "ЛИЧНЫЕ ДЕЛА",
                new Vector2(-550, -230), new Vector2(438, 298), ShowBankJobs);
            _bankProtectionDeskButton = BankDeskObject("Protection card", "bank-protection-card", "КРЫША ТУЗЯРЫ",
                new Vector2(550, -230), new Vector2(430, 238), ShowBankProtection);
            LoanText(_bankProtectionDeskButton.transform, "Card inscription", "ПОКРОВИТЕЛЬСТВО",
                new Vector2(0, 30), new Vector2(366, 54), 32, new Color(1, .86f, .5f));
            LoanText(_bankProtectionDeskButton.transform, "Card owner", "Банк Тузяры · для своих",
                new Vector2(0, -23), new Vector2(366, 42), 24, new Color(.93f, .85f, .66f));
            _bankJobsDeskBadge = LoanArt(_bankJobsDeskButton.transform, "Job reward marker", null,
                new Vector2(168, 135), new Vector2(62, 62), new Color(.71f, .07f, .025f)).rectTransform;
            _bankJobsDeskBadge.GetComponent<Image>().sprite = UiTextures.Circle(96, Color.white);
            _bankJobsDeskBadgeText = LoanText(_bankJobsDeskBadge, "Ready jobs", "", Vector2.zero, new Vector2(58, 58), 34, Color.white);
            _bankJobsDeskBadge.gameObject.SetActive(false);
        }

        private Button BankDeskObject(string name, string resource, string title, Vector2 at, Vector2 artSize, Action click)
        {
            var hit = LoanArt(_bankLoanLayer, name, null, at, new Vector2(460, 360), Color.clear);
            hit.raycastTarget = true;
            var picture = LoanArt(hit.transform, name + " art", null, new Vector2(0, 25), artSize, Color.white);
            picture.sprite = BankObjectSprite(resource); picture.preserveAspect = true;
            var button = hit.gameObject.AddComponent<Button>();
            button.targetGraphic = picture;
            var colors = button.colors;
            colors.highlightedColor = new Color(1, .94f, .79f);
            colors.pressedColor = new Color(.78f, .67f, .51f); colors.fadeDuration = .1f;
            button.colors = colors; button.onClick.AddListener(() => click());
            var plaque = LoanArt(hit.transform, "Object name", null, new Vector2(0, -141), new Vector2(418, 64), new Color(.19f, .08f, .025f, .97f));
            plaque.sprite = UiTextures.RoundedRect(64, 64, 12, Color.white); plaque.type = Image.Type.Sliced;
            LoanText(plaque.transform, "Caption", title, Vector2.zero, new Vector2(402, 62), 32, new Color(1, .88f, .57f));
            return button;
        }

        private void PaintBankDeskServices(float deltaTime)
        {
            var visible = _bankLoanShown && !_bankContractShown && _bankContractFade <= 0 && !_bankReceiptShown && !BankServiceShown;
            _bankJobsDeskButton.gameObject.SetActive(visible);
            _bankProtectionDeskButton.gameObject.SetActive(visible);
            _bankJobsDeskButton.interactable = _bankProtectionDeskButton.interactable = !LoanWorking;
            var ready = 0;
            if (_state?.BankJobs?.Jobs != null)
                foreach (var job in _state.BankJobs.Jobs) if (HubBankJobs.Ready(job) && !job.Claimed) ready++;
            _bankJobsDeskBadge.gameObject.SetActive(ready > 0);
            _bankJobsDeskBadgeText.text = ready.ToString();
            if (BankServiceShown)
            {
                _bankLoanTakeButton.gameObject.SetActive(false); _bankLoanDebtButton.gameObject.SetActive(false);
                _bankLoanReadButton.gameObject.SetActive(false); _bankCollectorsButton.gameObject.SetActive(false);
                _bankLoanBubble.gameObject.SetActive(false);
            }
            if (_bankProtectionShown) _bankLoanWallet.SetAsLastSibling();
            PaintBankJobs(deltaTime);
        }

        private void ShowBankProtection()
        {
            if (!_bankLoanShown || LoanWorking || _bankContractShown || _bankContractFade > 0 || _bankReceiptShown || BankJobsShown) return;
            if (_bankProtectionLayer == null) BuildBankProtection();
            _bankProtectionShown = true;
            _bankProtectionLayer.gameObject.SetActive(true); _bankProtectionLayer.SetAsLastSibling();
            _bankLoanWallet.SetAsLastSibling();
            PaintBankDeskServices(0);
        }

        private void CloseBankProtection()
        {
            if (LoanWorking) return;
            _bankProtectionShown = false;
            if (_bankProtectionLayer != null) _bankProtectionLayer.gameObject.SetActive(false);
        }

        private void HideBankServices()
        {
            HideBankJobs();
            _bankProtectionShown = false;
            if (_bankProtectionLayer != null) _bankProtectionLayer.gameObject.SetActive(false);
        }

        private void BuildBankProtection()
        {
            _bankProtectionLayer = LoanRect(_bankLoanLayer, "Bank protection preview", Vector2.zero, new Vector2(1600, 900));
            var shade = LoanArt(_bankProtectionLayer, "Protection shade", null, Vector2.zero, new Vector2(4800, 2400), new Color(.02f, .012f, .007f, .72f));
            shade.raycastTarget = true;
            var card = LoanArt(_bankProtectionLayer, "Patronage card", null, Vector2.zero, new Vector2(1400, 775), Color.white);
            card.sprite = BankObjectSprite("bank-protection-card"); card.preserveAspect = true;
            _bankProtectionCard = card.rectTransform;
            var gold = new Color(1, .84f, .43f); var cream = new Color(1, .95f, .81f);
            LoanText(_bankProtectionCard, "Protection title", "ПОД МОИМ ПОКРОВИТЕЛЬСТВОМ", new Vector2(0, 206), new Vector2(1190, 72), 41, gold, true);
            LoanText(_bankProtectionCard, "Bank promise", "«Своих не обманываю». Тузяра", new Vector2(0, 136), new Vector2(1140, 54), 32, cream);
            LoanText(_bankProtectionCard, "Raid warning benefit", "Люди Тузяры предупредят об облаве.\nУспеешь уйти через чёрный ход — или останешься на свой риск.",
                new Vector2(0, 39), new Vector2(1110, 98), 33, cream);
            LoanText(_bankProtectionCard, "Honest loans benefit", "Пока действует крыша — новые кредиты без подвоха.\nПодписанные договоры и долги остаются в силе.",
                new Vector2(0, -72), new Vector2(1110, 98), 33, cream);
            LoanText(_bankProtectionCard, "Protection availability", "Подполье пока закрыто. Покровительство ещё не продаётся.",
                new Vector2(0, -166), new Vector2(1140, 52), 30, gold);
            LoanText(_bankProtectionCard, "Protection joke", "От плохих карт не защищает. Играть надо уметь.",
                new Vector2(0, -225), new Vector2(1140, 48), 28, new Color(.83f, .78f, .65f));
            ReceiptButton(_bankProtectionCard, "К СТОЛУ", new Vector2(0, -305), new Vector2(350, 64), CloseBankProtection);
            PinToCorner((RectTransform)CityButton(_bankProtectionLayer, "НАЗАД", 24, 24, 170, 56, CloseBankProtection).transform, -1);
            _bankProtectionLayer.gameObject.SetActive(false);
        }
    }
}
