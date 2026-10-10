using System;
using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    public sealed partial class PlayerHubController
    {
        private const int BankRepayCoinCapacity = 12;
        private const float BankRepayReturnAt = 1.30f, BankRepayFinishAt = 1.55f;
        private readonly RectTransform[] _bankRepayCoins = new RectTransform[BankRepayCoinCapacity];
        private RectTransform _bankRepayEffects;
        private Text _bankRepayDebitText;
        private Image _bankReceiptShade;
        private float _bankRepayTime = -1;
        private int _bankRepayWalletFrom, _bankRepayWalletTo, _bankRepayCoinCount;
        private bool BankRepaymentAnimating => _bankRepayTime >= 0;

        private void BeginBankRepayment(int walletBefore)
        {
            ResetBankRepayment();
            _bankRepayWalletFrom = walletBefore;
            _bankRepayWalletTo = _state.Profile.Coins;
            var debited = Math.Max(0, walletBefore - _bankRepayWalletTo);
            // Animation only presents a payment already accepted and persisted by the service.
            if (LoanReduced || debited == 0) { _bankReceiptStampTime = 0; return; }
            if (_bankRepayEffects == null)
            {
                _bankRepayEffects = LoanRect(_bankLoanLayer, "Repayment flight", Vector2.zero, new Vector2(1600, 900));
                for (var i = 0; i < BankRepayCoinCapacity; i++)
                {
                    var coin = LoanArt(_bankRepayEffects, "Repayment coin " + i, null, Vector2.zero, new Vector2(64, 64), Color.white);
                    coin.sprite = HubCurrencyArt.Silver; coin.preserveAspect = true;
                    _bankRepayCoins[i] = coin.rectTransform;
                    coin.gameObject.SetActive(false);
                }
                _bankRepayDebitText = LoanText(_bankRepayEffects, "Wallet payment debit", "", Vector2.zero, new Vector2(210, 58), 34, new Color(1, .88f, .55f));
                _bankRepayDebitText.fontStyle = FontStyle.Bold;
                var shadow = _bankRepayDebitText.gameObject.AddComponent<Shadow>();
                shadow.effectColor = new Color(.08f, .025f, .005f, .9f); shadow.effectDistance = new Vector2(2, -2);
            }
            _bankRepayCoinCount = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(debited)), 1, BankRepayCoinCapacity);
            _bankRepayDebitText.text = "−" + HubMoney(debited);
            _bankRepayEffects.gameObject.SetActive(true);
            _bankRepayEffects.SetAsLastSibling();
            _bankLoanWallet.SetAsLastSibling();
            _bankRepayTime = 0;
            _bankReceiptStampTime = -1;
            PaintBankRepayment();
        }

        private void AdvanceBankRepayment(float deltaTime)
        {
            if (!BankRepaymentAnimating) return;
            _bankRepayTime += Mathf.Max(0, deltaTime);
            if (_bankRepayTime < BankRepayFinishAt && !LoanReduced) return;
            ResetBankRepayment();
            _bankLoanWalletText.text = HubMoney(_state.Profile.Coins);
            _bankReceiptStampTime = 0;
            _bankReceiptRevision = -1;
        }

        private void ResetBankRepayment()
        {
            _bankRepayTime = -1;
            if (_bankRepayEffects != null) _bankRepayEffects.gameObject.SetActive(false);
            foreach (var coin in _bankRepayCoins) if (coin != null) coin.gameObject.SetActive(false);
            if (_bankReceiptPaper != null) _bankReceiptPaper.anchoredPosition = Vector2.zero;
            if (_bankReceiptShade != null) _bankReceiptShade.color = new Color(.02f, .015f, .01f, .64f);
        }

        // Paint after issuance has updated the same wallet label, so there is one visible counter.
        private void PaintBankRepayment()
        {
            if (!BankRepaymentAnimating || _bankRepayEffects == null) return;
            var t = _bankRepayTime;
            var away = t < BankRepayReturnAt ? Mathf.SmoothStep(0, 1, Mathf.Clamp01(t / .20f))
                : 1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01((t - BankRepayReturnAt) / (BankRepayFinishAt - BankRepayReturnAt)));
            _bankReceiptPaper.anchoredPosition = new Vector2(0, -950 * away);
            _bankReceiptShade.color = new Color(.02f, .015f, .01f, .64f * (1 - away));
            var origin = (Vector2)_bankRepayEffects.InverseTransformPoint(_bankLoanWallet.TransformPoint(new Vector3(-104, 0, 0)));
            var landed = 0;
            for (var i = 0; i < _bankRepayCoins.Length; i++)
            {
                var coin = _bankRepayCoins[i];
                var elapsed = t - .18f - i * .045f;
                var flight = Mathf.Clamp01(elapsed / .62f);
                coin.gameObject.SetActive(i < _bankRepayCoinCount && elapsed >= 0 && flight < 1);
                if (i >= _bankRepayCoinCount) continue;
                if (flight >= 1) landed++;
                var spread = (i - (_bankRepayCoinCount - 1) / 2f) * 7;
                var target = new Vector2(-35 + spread, -220 + Mathf.Abs(spread) * .12f);
                var startControl = origin + new Vector2(-170 - spread, -65);
                var endControl = target + new Vector2(130 + spread, 195);
                var v = 1 - flight;
                coin.anchoredPosition = v * v * v * origin + 3 * v * v * flight * startControl
                    + 3 * v * flight * flight * endControl + flight * flight * flight * target;
                coin.localScale = Vector3.one * Mathf.Lerp(.87f, .55f, flight);
                coin.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(flight * Mathf.PI) * (24 + spread));
                coin.GetComponent<Image>().canvasRenderer.SetAlpha(1 - Mathf.InverseLerp(.78f, 1, flight));
            }
            var display = Mathf.RoundToInt(Mathf.Lerp(_bankRepayWalletFrom, _bankRepayWalletTo, landed / (float)_bankRepayCoinCount));
            _bankLoanWalletText.text = HubMoney(display);
            _bankRepayDebitText.rectTransform.anchoredPosition = origin + new Vector2(112, -61 - Mathf.Min(t, 1) * 15);
            _bankRepayDebitText.canvasRenderer.SetAlpha(Mathf.Clamp01(t / .12f) * (1 - Mathf.InverseLerp(1.1f, BankRepayFinishAt, t)));
        }
    }
}
