using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>
    /// Монеты летят в кошелёк и из него (автор, 2026-11-03: «везде, как в доме Шестёрки»). Любая перемена серебра или
    /// золота в профиле — награда, покупка, обмен, ролик, возврат из партии — видна полётом: заработанное летит дугой от
    /// места нажатия (или середины экрана) к счётчику, потраченное — из счётчика к месту нажатия. Счётчик досчитывает по
    /// мере того, как монеты долетают или улетают, и вздрагивает. Счётчик — тот, что сейчас виден: касса, окна заданий и
    /// коллекции, кошельки Лавки и Рынка, панель города, шапка разделов; где его нет (казино, Архив, гадалка, настройки) —
    /// на время полёта в правом верхнем углу проявляется кошелёк как на панели города, только без шестерёнки. Поручения и подарок Шестёрки и банк
    /// рисуют свой полёт — там этот не включается. С «меньше движения» монет нет, числа сразу новые.
    /// </summary>
    public sealed partial class PlayerHubController
    {
        private sealed class CoinFlow
        {
            public bool Gold, Spend;
            public int From, To, Passed;      // Passed — сколько монет уже долетело (заработок) или вылетело (трата)
            public Text Counter;              // null — кошелёк в углу
            public Vector2 Source;            // место нажатия в слое полёта
            public readonly List<Image> Coins = new List<Image>();
            public float Time, Pulse;
            public float Length => (Coins.Count - 1) * FlowCoinDelay + FlowCoinTime;
        }

        // Как у поручений Шестёрки: монеты по очереди через 0,055 с, каждая летит 0,65 с.
        private const float FlowCoinDelay = .055f, FlowCoinTime = .65f, FlowPressMemory = 2.5f;
        private RectTransform _flowLayer, _flowWallet;
        private CanvasGroup _flowWalletGroup;
        private Text _flowWalletSilver, _flowWalletGold;
        private readonly List<CoinFlow> _flows = new List<CoinFlow>();
        // Баланс, который уже показан полётом; int.MinValue — профиль ещё не видели.
        private int _flowCoins = int.MinValue, _flowGold;
        private Vector2 _flowPress;
        private float _flowPressAt = -10, _flowWalletAlpha, _flowWalletHold;

        private void LateUpdate()
        {
            // Последнее нажатие — откуда полетят монеты: команда приходит через кадр-другой после него.
            if (Pointer.current != null && Pointer.current.press.wasPressedThisFrame)
            {
                _flowPress = Pointer.current.position.ReadValue();
                _flowPressAt = Time.unscaledTime;
            }
            UpdateCoinFlow(Time.unscaledDeltaTime);
        }

        /// <summary>
        /// После всех Update: заметить перемену баланса и вести полёты. Счётчики пишут настоящий баланс в своих Update —
        /// здесь, позже них, на время полёта в них ставится досчитанное.
        /// </summary>
        private void UpdateCoinFlow(float deltaTime)
        {
            if (_state == null || _stage == null) return;
            // Play без перезагрузки сцены: слой прошлого запуска мог пропасть вместе с недолетевшими монетами.
            if (_flowLayer == null && (_flows.Count > 0 || _flowWalletAlpha > 0)) { _flows.Clear(); _flowWalletAlpha = _flowWalletHold = 0; }
            var coins = _state.Profile.Coins;
            var gold = _state.Profile.Gold;
            if (_flowCoins == int.MinValue) { _flowCoins = coins; _flowGold = gold; }
            if (coins != _flowCoins) { StartCoinFlow(false, _flowCoins, coins); _flowCoins = coins; }
            if (gold != _flowGold) { StartCoinFlow(true, _flowGold, gold); _flowGold = gold; }
            if (_flows.Count == 0 && _flowWalletAlpha <= 0) return;
            _flowLayer.SetAsLastSibling();

            var corner = false;
            foreach (var flow in _flows)
            {
                flow.Time += deltaTime;
                // Окно со счётчиком закрыли на лету — монеты доводит кошелёк в углу.
                if (flow.Counter != null && !flow.Counter.gameObject.activeInHierarchy) flow.Counter = null;
                corner |= flow.Counter == null;
                PaintCoinFlow(flow, deltaTime);
            }
            int shownCoins = coins - Pending(false), shownGold = gold - Pending(true);
            foreach (var flow in _flows)
                if (flow.Counter != null) flow.Counter.text = HubMoney(flow.Gold ? shownGold : shownCoins);
            for (var i = _flows.Count - 1; i >= 0; i--)
            {
                var flow = _flows[i];
                if (flow.Time < flow.Length) continue;
                foreach (var coin in flow.Coins)
                    if (Application.isPlaying) Destroy(coin.gameObject);
                    else DestroyImmediate(coin.gameObject);
                if (flow.Counter != null) flow.Counter.rectTransform.localScale = Vector3.one;
                _flows.RemoveAt(i);
            }

            // Кошелёк в углу: проявляется, пока в него летят, и гаснет через полсекунды после последней монеты.
            _flowWalletHold = corner ? .5f : Mathf.Max(0, _flowWalletHold - deltaTime);
            _flowWalletAlpha = Mathf.MoveTowards(_flowWalletAlpha, corner || _flowWalletHold > 0 ? 1 : 0, deltaTime / .2f);
            _flowWalletGroup.alpha = _flowWalletAlpha;
            _flowWallet.gameObject.SetActive(_flowWalletAlpha > 0);
            _flowWalletSilver.text = HubMoney(shownCoins);
            _flowWalletGold.text = HubMoney(shownGold);
        }

        /// <summary>Сколько ещё «в пути» у серебра или золота: настоящий баланс минус это — то, что видно на счётчике.</summary>
        private int Pending(bool gold)
        {
            var pending = 0L;
            foreach (var flow in _flows)
                if (flow.Gold == gold)
                    pending += (long)(flow.To - flow.From) * (flow.Coins.Count - flow.Passed) / flow.Coins.Count;
            return (int)pending;
        }

        private void StartCoinFlow(bool gold, int from, int to)
        {
            // Свой полёт у поручений и подарка Шестёрки и у банка; «меньше движения» — без полёта.
            if (QuestsShown || GiftShown || BankShown || BankServiceShown || _state.Settings.ReducedMotion) return;
            if (_flowLayer == null) BuildCoinFlow();
            var delta = System.Math.Abs((long)to - from);
            var count = delta < 10 ? 4 : delta < 100 ? 6 : delta < 1000 ? 8 : 10;
            var flow = new CoinFlow { Gold = gold, Spend = to < from, From = from, To = to, Counter = CoinFlowCounter(gold) };
            var canvas = _stage.GetComponentInParent<Canvas>();
            var camera = canvas == null || canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.rootCanvas.worldCamera;
            if (Time.unscaledTime - _flowPressAt < FlowPressMemory
                && RectTransformUtility.ScreenPointToLocalPointInRectangle(_flowLayer, _flowPress, camera, out var press))
                flow.Source = press - _flowLayer.rect.center;
            for (var i = 0; i < count; i++)
            {
                var coin = QuestArt(_flowLayer, "Flow coin", null, flow.Source, new Vector2(46, 60), Color.white);
                coin.sprite = gold ? HubCurrencyArt.Gold : HubCurrencyArt.Silver;
                coin.preserveAspect = true;
                coin.gameObject.SetActive(false);
                flow.Coins.Add(coin);
            }
            _flows.Add(flow);
        }

        /// <summary>Видимый сейчас счётчик серебра или золота; null — на экране его нет.</summary>
        private Text CoinFlowCounter(bool gold)
        {
            var counters = gold
                ? new[] { _cashShown ? _cashGold : null, _hudGold, _gold }
                : new[] { _cashShown ? _cashSilver : null, TasksShown ? _tasksWallet : null, CollectionShown ? _collectionWallet : null,
                    _shopWalletText, _marketWalletText, _hudSilver, _wallet };
            foreach (var counter in counters)
                if (counter != null && counter.gameObject.activeInHierarchy) return counter;
            return null;
        }

        /// <summary>Монета у левого края плашки счётчика, в координатах слоя полёта.</summary>
        private Vector2 CoinFlowTarget(CoinFlow flow)
        {
            var counter = flow.Counter != null ? flow.Counter : flow.Gold ? _flowWalletGold : _flowWalletSilver;
            var plate = counter.rectTransform.parent as RectTransform ?? counter.rectTransform;
            var world = plate.TransformPoint(new Vector3(plate.rect.xMin + 10, plate.rect.center.y));
            return (Vector2)_flowLayer.InverseTransformPoint(world) - _flowLayer.rect.center;
        }

        /// <summary>
        /// Монеты по очереди летят дугой вверх, веером: заработок — от места нажатия к счётчику и мельчает к концу, трата —
        /// от счётчика к месту нажатия, растёт и тает. Долетела или вылетела монета — счётчик вздрагивает.
        /// </summary>
        private void PaintCoinFlow(CoinFlow flow, float deltaTime)
        {
            var target = CoinFlowTarget(flow);
            var middle = (flow.Coins.Count - 1) / 2f;
            var passed = 0;
            for (var i = 0; i < flow.Coins.Count; i++)
            {
                var t = Mathf.Clamp01((flow.Time - i * FlowCoinDelay) / FlowCoinTime);
                if (flow.Spend ? t > 0 : t >= 1) passed++;
                var coin = flow.Coins[i];
                coin.gameObject.SetActive(t > 0 && t < 1);
                var from = flow.Spend ? target : flow.Source + new Vector2((i - middle) * 12, -30);
                var to = flow.Spend ? flow.Source + new Vector2((i - middle) * 14, 0) : target;
                coin.rectTransform.anchoredPosition = Vector2.Lerp(from, to, Mathf.SmoothStep(0, 1, t))
                    + new Vector2((i - middle) * 16, 150) * Mathf.Sin(t * Mathf.PI);
                coin.rectTransform.localScale = Vector3.one * (flow.Spend ? Mathf.Lerp(.72f, 1.05f, t) : Mathf.Lerp(1, .72f, t));
                coin.color = new Color(1, 1, 1, flow.Spend ? 1 - Mathf.SmoothStep(.7f, 1, t) : 1);
            }
            if (passed > flow.Passed) flow.Pulse = 1;
            flow.Passed = passed;
            flow.Pulse = Mathf.MoveTowards(flow.Pulse, 0, deltaTime / .18f);
            if (flow.Counter != null) flow.Counter.rectTransform.localScale = Vector3.one * (1 + (flow.Spend ? .08f : .16f) * flow.Pulse);
        }

        /// <summary>Слой полёта поверх всего, не ловит нажатия; кошелёк в углу — как у панели города, без шестерёнки и «плюсов».</summary>
        private void BuildCoinFlow()
        {
            _flowLayer = StageLayer("Coin flow");
            var group = _flowLayer.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            // Плашки — вплотную к правому краю (шестерёнки нет): над окном не задевают его табличку-заголовок.
            const float walletWidth = 500, goldX = walletWidth - 208;
            _flowWallet = MakeRect(_flowLayer, "Flow wallet", 1600 - 16, 10, walletWidth, 90);
            _flowWallet.pivot = new Vector2(1, 1);
            _flowWallet.localScale = Vector3.one * (MobileUi.Phone ? 1.2f : 1f);
            _flowWalletGold = HudCurrency(_flowWallet, HubCurrencyArt.Gold, goldX, null);
            _flowWalletSilver = HudCurrency(_flowWallet, HubCurrencyArt.Silver, goldX - 254, null);
            _flowWalletGroup = _flowWallet.gameObject.AddComponent<CanvasGroup>();
            _flowWalletGroup.alpha = 0;
            _flowWallet.gameObject.SetActive(false);
            PinToCorner(_flowWallet, 1);
        }
    }
}
