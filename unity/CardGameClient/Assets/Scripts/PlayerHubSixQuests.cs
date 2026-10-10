using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CardGame.Core.Meta;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>Поручения Шестёрки: крупный план пробковой доски. Данные и награды принадлежат сервису.</summary>
    public sealed partial class PlayerHubController
    {
        private sealed class QuestNoteView
        {
            internal RectTransform Rect, Fill, Stamp, Pin;
            internal CanvasGroup Group, Ink, StampGroup;
            internal Image Paper, Glow;
            internal Text Title, Progress, Coins, XP, Timer;
            internal Button Claim;
            internal GameObject New;
            internal HubQuest Model, Pending;
            internal Vector2 Position;
            internal float Angle, ChangeTime = -1;
            internal bool Weekly;
        }

        private RectTransform _questsLayer, _questsBoard, _questsWallet;
        private CanvasGroup _questsGroup;
        private Text _questsWalletText, _questsXP;
        private readonly QuestNoteView[] _questNotes = new QuestNoteView[5];
        private readonly RectTransform[] _questCoins = new RectTransform[7];
        private readonly HashSet<string> _questsNew = new HashSet<string>();
        private readonly Color _questInk = new Color(.22f, .12f, .065f);
        private bool _questsShown, _questsEscapeHeld, _questsRefreshing, _questsMarking;
        private float _questsFade, _questsRefreshAt, _questsClock, _questsClaimTime = -1;
        private int _questsWalletFrom, _questsWalletTo;
        private QuestNoteView _questsClaimView;
        private Vector2 _questsClaimOrigin;
        private bool QuestsShown => _questsShown || _questsFade > 0;

        private void ShowSixQuests()
        {
            if (_page != "home" || _cityLevel != SixLevel || _diveTarget >= 0 || _visitPhase != 0
                || _mapOpen || GiftShown || BankShown || _busy || _state == null) return;
            if (_questsLayer == null) BuildSixQuests();
            _questsNew.Clear();
            foreach (var q in _state.Quests.Where(q => !q.Seen)) _questsNew.Add(q.Id);
            _questsShown = true;
            _questsWalletFrom = _questsWalletTo = _state.Profile.Coins;
            _questsLayer.gameObject.SetActive(true);
            _questsLayer.SetAsLastSibling();
            if (_sixBubble != null) _sixBubble.gameObject.SetActive(false);
            _sixBubbleTime = -1;
            for (var i = 0; i < _questNotes.Length; i++)
            {
                _questNotes[i].ChangeTime = -1; _questNotes[i].Pending = null;
                BindQuestNote(_questNotes[i], _state.Quests.FirstOrDefault(q => q.Slot == HubQuests.Slots[i]));
            }
            PaintSixQuests(0);
            _ = MarkSixQuestsSeen();
            UpdateInteriorControls(); RefreshMapButton();
        }

        private void CloseSixQuests()
        {
            if (_busy || _questsClaimTime >= 0) return;
            _questsShown = false;
        }

        private async Task MarkSixQuestsSeen()
        {
            if (_questsMarking || !_questsShown || _state == null || !_state.Quests.Any(q => !q.Seen)) return;
            _questsMarking = true;
            var known = new HashSet<string>(_state.Quests.Select(q => q.Id));
            foreach (var q in _state.Quests.Where(q => !q.Seen)) _questsNew.Add(q.Id);
            try
            {
                var result = await PlayerHubRuntime.ExecuteAsync(new HubCommand { Action = "quests_seen" });
                if (this == null) return;
                if (result.Success)
                {
                    _state = result.Snapshot;
                    foreach (var q in _state.Quests.Where(q => !known.Contains(q.Id))) _questsNew.Add(q.Id);
                    RefreshSixQuestBadge(); UpdateGiftMarks(GiftReady());
                }
                else Toast(PlayerHubRuntime.ErrorText(result));
            }
            finally { _questsMarking = false; }
        }

        private async Task RefreshSixQuests()
        {
            if (_questsRefreshing) return;
            _questsRefreshing = true;
            try
            {
                var result = await PlayerHubRuntime.RefreshAsync();
                if (this == null) return;
                if (result.Success) _state = result.Snapshot;
                else Toast(PlayerHubRuntime.ErrorText(result));
            }
            finally { _questsRefreshing = false; }
        }

        private async Task ClaimSixQuest(QuestNoteView view)
        {
            var note = view.Model;
            if (!_questsShown || _busy || _questsClaimTime >= 0 || note == null || !HubQuests.Ready(note)) return;
            var coins = _state.Profile.Coins;
            SetBusy(true);
            var result = await PlayerHubRuntime.ExecuteAsync(new HubCommand { Action = "quest_claim", Target = note.Id });
            if (this == null) return;
            SetBusy(false);
            if (!result.Success) { Toast(PlayerHubRuntime.ErrorText(result)); return; }
            _state = result.Snapshot;
            _questsWalletFrom = coins; _questsWalletTo = _state.Profile.Coins;
            _questsClaimView = view;
            _questsClaimOrigin = (Vector2)_questsLayer.InverseTransformPoint(view.Rect.position) - _questsLayer.rect.center;
            // The old snapshot stays on screen until its stamp lands, even for an overdue claim.
            note.Claimed = note.Seen = true;
            view.Model = note;
            _questsXP.text = "+" + note.RewardXP + " опыта";
            _questsClaimTime = 0;
            RefreshSixQuestBadge(); UpdateGiftMarks(GiftReady()); Render();
        }

        private RectTransform QuestRect(Transform parent, string name, Vector2 at, Vector2 size)
        {
            var rect = MakeRect(parent, name, 0, 0, 0, 0);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = at; rect.sizeDelta = size;
            return rect;
        }

        private Text QuestText(Transform parent, string name, string value, Vector2 at, Vector2 size, int fontSize, Color color)
        {
            var label = QuestRect(parent, name, at, size).gameObject.AddComponent<Text>();
            label.font = Resources.Load<Font>("Fonts/SignNarrow") ?? _font;
            label.text = value; label.fontSize = fontSize; label.color = color;
            label.alignment = TextAnchor.MiddleCenter; label.supportRichText = false; label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap; label.verticalOverflow = VerticalWrapMode.Overflow;
            return label;
        }

        private Image QuestArt(Transform parent, string name, string asset, Vector2 at, Vector2 size, Color fallback)
        {
            var image = QuestRect(parent, name, at, size).gameObject.AddComponent<Image>();
            image.sprite = string.IsNullOrEmpty(asset) ? null : Resources.Load<Sprite>("UI/Hub/" + asset);
            image.color = image.sprite != null ? Color.white : fallback;
            image.raycastTarget = false;
            return image;
        }

        private Button QuestButton(Transform parent, string title, Vector2 at, Vector2 size, Action action, int fontSize = 27)
        {
            var plate = QuestArt(parent, title, null, at, size, new Color(.30f, .16f, .075f));
            plate.sprite = UiTextures.RoundedRect(64, 64, 8, Color.white); plate.type = Image.Type.Sliced;
            plate.raycastTarget = true;
            var border = plate.gameObject.AddComponent<HubOutline>();
            border.effectColor = new Color(.64f, .39f, .15f); border.effectDistance = new Vector2(2, -2);
            var button = plate.gameObject.AddComponent<Button>(); button.targetGraphic = plate;
            button.onClick.AddListener(() => action());
            QuestText(plate.transform, "Caption", title, Vector2.zero, size - new Vector2(16, 4), fontSize, new Color(1, .90f, .66f));
            return button;
        }

        private void BuildSixQuests()
        {
            _questsLayer = StageLayer("Six quests");
            _questsGroup = _questsLayer.gameObject.AddComponent<CanvasGroup>();
            var dim = QuestArt(_questsLayer, "Quest dim", null, Vector2.zero, new Vector2(4800, 2400), new Color(.025f, .015f, .01f, .88f));
            dim.raycastTarget = true; // Blocks the room, including swipes between its furniture.
            _questsBoard = QuestArt(_questsLayer, "Cork board", "six-quests-board", new Vector2(0, -28), new Vector2(1320, 790), new Color(.20f, .085f, .035f)).rectTransform;
            if (_questsBoard.GetComponent<Image>().sprite == null)
                QuestArt(_questsBoard, "Cork fallback", null, Vector2.zero, new Vector2(1190, 667), new Color(.56f, .32f, .14f));
            var title = QuestText(_questsLayer, "Quests title", "ПОРУЧЕНИЯ ШЕСТЁРКИ", new Vector2(0, 395), new Vector2(990, 60), 46, SignGoldTop);
            var gold = title.gameObject.AddComponent<HubSignText>(); gold.Top = SignGoldTop; gold.Bottom = SignGoldBottom;
            PinToCorner((RectTransform)CityButton(_questsLayer, "НАЗАД", 24, 24, 170, 56, CloseSixQuests).transform, -1);
            _questsWallet = QuestArt(_questsLayer, "Quest wallet", null, GiftWalletAt, GiftWalletSize, new Color(.08f, .045f, .025f)).rectTransform;
            var coin = QuestArt(_questsWallet, "Coin", null, new Vector2(-104, 0), new Vector2(64, 64), Color.white);
            coin.sprite = HubCurrencyArt.Silver; coin.preserveAspect = true;
            _questsWalletText = QuestText(_questsWallet, "Amount", "", new Vector2(28, 0), new Vector2(184, 50), 35, Color.white);
            PinToCorner(_questsWallet, 1);
            for (var i = 0; i < 5; i++) _questNotes[i] = BuildQuestNote(i);
            var weekHeading = QuestRect(_questsLayer, "Weekly heading", new Vector2(0, -110), new Vector2(500, 54));
            var weekTitle = QuestText(weekHeading, "Weekly title", "НЕДЕЛЬНЫЕ ПОРУЧЕНИЯ", Vector2.zero, new Vector2(460, 48), 36, new Color32(156, 28, 22, 255));
            weekTitle.fontStyle = FontStyle.Bold;
            if (Application.isEditor || Debug.isDebugBuild)
            {
                QuestButton(_questsLayer, "Тест: новые поручения", new Vector2(-193, -412), new Vector2(360, 32), () => { if (_questsClaimTime < 0) Send("quests_debug_refresh"); }, 19);
                QuestButton(_questsLayer, "Тест: выполнить", new Vector2(193, -412), new Vector2(360, 32), () => { if (_questsClaimTime < 0) Send("quests_debug_complete"); }, 19);
            }
            _questsXP = QuestText(_questsLayer, "Quest XP", "", Vector2.zero, new Vector2(340, 70), 38, new Color(1, .91f, .47f));
            var xpShadow = _questsXP.gameObject.AddComponent<Shadow>(); xpShadow.effectColor = Color.black; xpShadow.effectDistance = new Vector2(2, -2);
            for (var i = 0; i < _questCoins.Length; i++)
            {
                var image = QuestArt(_questsLayer, "Quest flying coin " + i, null, Vector2.zero, new Vector2(45, 45), Color.white);
                image.sprite = HubCurrencyArt.Silver; image.preserveAspect = true;
                _questCoins[i] = image.rectTransform; image.gameObject.SetActive(false);
            }
            _questsLayer.gameObject.SetActive(false);
        }

        private QuestNoteView BuildQuestNote(int index)
        {
            var weekly = index >= 3;
            var size = weekly ? new Vector2(525, 218) : new Vector2(326, 354);
            var position = weekly ? new Vector2(index == 3 ? -287 : 287, -250) : new Vector2((index - 1) * 372, 95);
            var view = new QuestNoteView { Position = position, Angle = new[] { -1.8f, 1.1f, -1.3f, .8f, -1.1f }[index], Weekly = weekly };
            view.Rect = QuestRect(_questsLayer, "Quest note " + HubQuests.Slots[index], position, size);
            view.Group = view.Rect.gameObject.AddComponent<CanvasGroup>();
            view.Glow = QuestArt(view.Rect, "Ready glow", null, Vector2.zero, size + new Vector2(85, 65), Color.white);
            view.Glow.sprite = UiTextures.RadialGlow(256, Color.white);
            view.Paper = QuestArt(view.Rect, "Paper", weekly ? "six-quests-note-wide" : "six-quests-note", Vector2.zero, size, new Color(.96f, .82f, .56f));
            var shadow = view.Paper.gameObject.AddComponent<Shadow>(); shadow.effectDistance = new Vector2(3, -6); shadow.effectColor = new Color(0, 0, 0, .55f);
            var ink = QuestRect(view.Rect, "Writing", Vector2.zero, size);
            view.Ink = ink.gameObject.AddComponent<CanvasGroup>(); view.Ink.blocksRaycasts = true;
            if (!weekly)
            {
                var tierColor = index == 0 ? new Color(.19f, .34f, .10f) : index == 1 ? new Color(.51f, .30f, .045f) : new Color(.58f, .13f, .07f);
                QuestText(ink, "Tier", HubQuests.TierName(HubQuests.Slots[index]), new Vector2(0, 120), new Vector2(256, 40), 27, tierColor);
            }
            view.Title = QuestText(ink, "Task", "", new Vector2(0, weekly ? 57 : 49), new Vector2(size.x - 54, weekly ? 60 : 96), weekly ? 28 : 30, _questInk);
            view.Title.resizeTextForBestFit = true; view.Title.resizeTextMinSize = 24; view.Title.resizeTextMaxSize = weekly ? 28 : 30;
            view.Title.verticalOverflow = VerticalWrapMode.Truncate;
            var barY = weekly ? 10 : -21;
            // Keep both the bar and the longest counter inside the uneven paper edges.
            var countWidth = weekly ? 116f : 110f;
            var barWidth = size.x - 60 - countWidth - 14;
            var track = QuestArt(ink, "Progress track", null, new Vector2(-size.x / 2 + 30 + barWidth / 2, barY), new Vector2(barWidth, 11), new Color(.27f, .15f, .06f, .22f));
            view.Fill = QuestArt(track.transform, "Progress fill", null, Vector2.zero, new Vector2(barWidth, 11), new Color(.36f, .40f, .16f)).rectTransform;
            view.Fill.anchorMin = view.Fill.anchorMax = view.Fill.pivot = new Vector2(0, .5f); view.Fill.anchoredPosition = Vector2.zero;
            view.Progress = QuestText(ink, "Progress count", "", new Vector2(size.x / 2 - 30 - countWidth / 2, barY), new Vector2(countWidth, 38), 25, _questInk);
            view.Progress.horizontalOverflow = HorizontalWrapMode.Overflow;
            var rewardY = weekly ? -30 : -67;
            var rewardCoin = QuestArt(ink, "Reward coin", null, new Vector2(-100, rewardY), new Vector2(34, 34), Color.white);
            rewardCoin.sprite = HubCurrencyArt.Silver; rewardCoin.preserveAspect = true;
            view.Coins = QuestText(ink, "Reward coins", "", new Vector2(-47, rewardY), new Vector2(80, 40), 28, _questInk);
            QuestText(ink, "XP star", "★", new Vector2(43, rewardY), new Vector2(32, 38), 25, new Color(.57f, .31f, .065f));
            view.XP = QuestText(ink, "Reward XP", "", new Vector2(96, rewardY), new Vector2(80, 40), 28, _questInk);
            var statusY = weekly ? -77 : -129;
            view.Timer = QuestText(ink, "Deadline", "", new Vector2(0, statusY), new Vector2(size.x - 40, weekly ? 44 : 52), weekly ? 24 : 25, _questInk);
            view.Claim = QuestButton(ink, "ЗАБРАТЬ", new Vector2(0, statusY), new Vector2(weekly ? 240 : 250, weekly ? 40 : 49), () => _ = ClaimSixQuest(view));
            view.Stamp = QuestRect(view.Rect, "Completed stamp", new Vector2(0, weekly ? 41 : 28), new Vector2(weekly ? 300 : 273, 66));
            view.StampGroup = view.Stamp.gameObject.AddComponent<CanvasGroup>(); view.StampGroup.blocksRaycasts = false;
            var stampColor = new Color(.65f, .14f, .085f, .9f);
            var half = view.Stamp.sizeDelta / 2;
            QuestText(view.Stamp, "Stamp text", "ВЫПОЛНЕНО", Vector2.zero, view.Stamp.sizeDelta, 34, stampColor);
            foreach (var y in new[] { -half.y, half.y }) QuestArt(view.Stamp, "Stamp rule", null, new Vector2(0, y), new Vector2(view.Stamp.sizeDelta.x, 3), stampColor);
            foreach (var x in new[] { -half.x, half.x }) QuestArt(view.Stamp, "Stamp edge", null, new Vector2(x, 0), new Vector2(3, view.Stamp.sizeDelta.y), stampColor);
            var pinName = weekly || index == 2 ? "red" : index == 0 ? "green" : "yellow";
            var pinColor = pinName == "green" ? new Color(.18f, .42f, .1f) : pinName == "yellow" ? new Color(.95f, .62f, .12f) : new Color(.75f, .12f, .08f);
            var pin = QuestArt(view.Rect, "Pin", "six-quests-pin-" + pinName, new Vector2(0, size.y / 2 - 4), new Vector2(42, 66), pinColor);
            pin.preserveAspect = true;
            if (pin.sprite == null) pin.sprite = UiTextures.Circle(64, Color.white);
            view.Pin = pin.rectTransform;
            var tag = QuestArt(view.Rect, "New tag", null, new Vector2(size.x / 2 - 41, size.y / 2 - 22), new Vector2(81, 30), new Color(.68f, .11f, .06f));
            QuestText(tag.transform, "New label", "НОВОЕ", Vector2.zero, new Vector2(78, 28), 18, new Color(1, .92f, .72f));
            view.New = tag.gameObject;
            return view;
        }

        private void BindQuestNote(QuestNoteView view, HubQuest model)
        {
            view.Model = model;
            view.Rect.gameObject.SetActive(model != null);
            if (model == null) return;
            view.Title.text = model.Name;
            view.Coins.text = "+" + model.RewardCoins;
            view.XP.text = "+" + model.RewardXP;
            if (!model.Seen) _questsNew.Add(model.Id);
        }

        private void UpdateSixQuests(float deltaTime, bool typing)
        {
            _questsClock += deltaTime;
            if (_state != null && !_busy && Time.unscaledTime >= _questsRefreshAt)
            {
                _questsRefreshAt = Time.unscaledTime + 1;
                if (_state.Quests.Any(q => q.ExpiresAt <= GiftNow && !HubQuests.Ready(q))) _ = RefreshSixQuests();
                if (_questsShown) _ = MarkSixQuestsSeen();
            }
            if (_questsLayer == null) return;
            if (_questsShown && (_page != "home" || _cityLevel != SixLevel)) _questsShown = false;
            var keyboard = typing ? null : Keyboard.current;
            var escape = keyboard != null && keyboard.escapeKey.isPressed;
            if (_questsShown && escape && !_questsEscapeHeld) { _cityVerticalHeld = 1; CloseSixQuests(); }
            _questsEscapeHeld = escape;
            _questsFade = Mathf.MoveTowards(_questsFade, _questsShown ? 1 : 0, deltaTime / .2f);
            _questsGroup.alpha = Mathf.SmoothStep(0, 1, _questsFade);
            _questsGroup.blocksRaycasts = QuestsShown;
            if (!QuestsShown) { _questsLayer.gameObject.SetActive(false); return; }
            if (_questsClaimTime >= 0) _questsClaimTime += _state.Settings.ReducedMotion ? 2 : deltaTime;
            PaintSixQuests(deltaTime);
        }

        private void PaintSixQuests(float deltaTime)
        {
            for (var i = 0; i < _questNotes.Length; i++)
            {
                var view = _questNotes[i];
                var latest = _state.Quests.FirstOrDefault(q => q.Slot == HubQuests.Slots[i]);
                var claiming = view == _questsClaimView && _questsClaimTime >= 0;
                if (!claiming && latest != null)
                {
                    if (view.Model == null) BindQuestNote(view, latest);
                    else if (latest.Id == view.Model.Id) view.Model = latest;
                    else if (view.ChangeTime < 0) { view.Pending = latest; view.ChangeTime = 0; }
                }
                var shift = 0f;
                var alpha = 1f;
                var pinScale = 1f;
                if (view.ChangeTime >= 0)
                {
                    view.ChangeTime += _state.Settings.ReducedMotion ? 1 : deltaTime;
                    var t = view.ChangeTime;
                    if (t < .22f) { shift = -110 * t / .22f; alpha = 1 - t / .22f; }
                    else
                    {
                        if (view.Pending != null) { BindQuestNote(view, view.Pending); view.Pending = null; }
                        var enter = Mathf.Clamp01((t - .22f) / .34f);
                        shift = 65 * (1 - Mathf.SmoothStep(0, 1, enter)); alpha = enter;
                        pinScale = 1 + .18f * Mathf.Sin(enter * Mathf.PI);
                    }
                    if (t >= .56f) view.ChangeTime = -1;
                }
                view.Group.alpha = alpha;
                view.Pin.localScale = Vector3.one * pinScale;
                var note = view.Model;
                if (note == null) continue;
                var ready = HubQuests.Ready(note);
                var progressText = note.Progress + " / " + note.Goal;
                if (view.Progress.text != progressText)
                {
                    view.Progress.text = progressText;
                    // Keep 1499 / 1500 on one line; recompute only when the count changes.
                    view.Progress.fontSize = 25;
                    if (view.Progress.preferredWidth > view.Progress.rectTransform.rect.width - 2)
                        view.Progress.fontSize = Mathf.Max(16, Mathf.FloorToInt(25 * (view.Progress.rectTransform.rect.width - 2) / view.Progress.preferredWidth));
                }
                var track = (RectTransform)view.Fill.parent;
                view.Fill.sizeDelta = new Vector2(track.sizeDelta.x * Mathf.Clamp01(note.Progress / (float)Math.Max(1, note.Goal)), 11);
                view.New.SetActive(_questsNew.Contains(note.Id) && !note.Claimed);
                view.Ink.alpha = note.Claimed ? .62f : 1;
                var paper = view.Paper.sprite != null ? Color.white : new Color(.96f, .82f, .56f);
                view.Paper.color = note.Claimed ? new Color(paper.r * .86f, paper.g * .86f, paper.b * .86f) : paper;
                view.Glow.color = new Color(1, .79f, .30f, ready ? .35f + (_state.Settings.ReducedMotion ? 0 : .10f * Mathf.Sin(_questsClock * 2)) : 0);
                view.Claim.gameObject.SetActive(ready);
                view.Claim.interactable = ready && !_busy && _questsClaimTime < 0;
                view.Timer.gameObject.SetActive(!ready);
                var remaining = Math.Max(0, note.ExpiresAt - GiftNow);
                view.Timer.text = (note.Claimed ? "Новое через " : "Осталось ") + QuestDuration(remaining);
                view.Stamp.gameObject.SetActive(note.Claimed);
                var stampT = claiming ? Mathf.Clamp01(_questsClaimTime / .28f) : 1;
                view.Stamp.localScale = Vector3.one * Mathf.Lerp(1.6f, 1, Mathf.SmoothStep(0, 1, stampT));
                view.Stamp.localRotation = Quaternion.Euler(0, 0, -8);
                view.StampGroup.alpha = stampT;
                var shake = claiming && _questsClaimTime < .36f ? Mathf.Sin(_questsClaimTime * 65) * 3 * (1 - _questsClaimTime / .36f) : 0;
                view.Rect.anchoredPosition = view.Position + new Vector2(shake, shift);
                view.Rect.localRotation = Quaternion.Euler(0, 0, view.Angle + shake * .2f);
            }
            PaintQuestReward();
        }

        private static string QuestDuration(long seconds)
            => seconds >= 86400
                ? seconds / 86400 + " д " + seconds % 86400 / 3600 + " ч"
                : (seconds / 3600).ToString("D2") + ":" + (seconds % 3600 / 60).ToString("D2")
                    + ":" + (seconds % 60).ToString("D2");

        private void PaintQuestReward()
        {
            var active = _questsClaimTime >= 0;
            var target = (Vector2)_questsLayer.InverseTransformPoint(_questsWallet.TransformPoint(new Vector3(-104, 0, 0))) - _questsLayer.rect.center;
            var landed = 0;
            for (var i = 0; i < _questCoins.Length; i++)
            {
                var t = active ? Mathf.Clamp01((_questsClaimTime - .15f - i * .055f) / .65f) : 1;
                _questCoins[i].gameObject.SetActive(active && t > 0 && t < 1);
                if (t >= 1) landed++;
                var start = _questsClaimOrigin + new Vector2((i - 3) * 12, -45);
                var at = Vector2.Lerp(start, target, Mathf.SmoothStep(0, 1, t));
                at += new Vector2((i - 3) * 16, 150) * Mathf.Sin(t * Mathf.PI);
                _questCoins[i].anchoredPosition = at;
                _questCoins[i].localScale = Vector3.one * Mathf.Lerp(1, .72f, t);
            }
            var balance = active ? Mathf.RoundToInt(Mathf.Lerp(_questsWalletFrom, _questsWalletTo, landed / (float)_questCoins.Length)) : _state.Profile.Coins;
            _questsWalletText.text = HubMoney(balance);
            _questsXP.gameObject.SetActive(active && _questsClaimTime < 1.3f);
            _questsXP.rectTransform.anchoredPosition = _questsClaimOrigin + new Vector2(0, 55 + 95 * Mathf.Clamp01(_questsClaimTime / 1.3f));
            _questsXP.color = new Color(1, .91f, .47f, 1 - Mathf.Clamp01(_questsClaimTime / 1.3f));
            if (_questsClaimTime >= 1.4f) { _questsClaimTime = -1; _questsClaimView = null; }
        }
    }
}
