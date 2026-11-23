using System;
using System.Collections.Generic;
using System.Linq;
using CardGame.Core.Meta;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>
    /// Окно заданий в городе (2026-11-02): кнопка «Задания» панели города открывает поверх улицы ту же доску в золотой
    /// рамке, что и настройки (<see cref="CityWindow"/>). Внутри — поручения Шестёрки: «НА СЕГОДНЯ» (лёгкое, среднее,
    /// сложное) и «НА НЕДЕЛЮ» (два). В строке значок сложности, название, сложность и время, полоса прогресса с «3 / 5»,
    /// награда серебром и опытом и справа «ЗАБРАТЬ» (выполнено), «ИГРАТЬ» (ещё нет) или печать «ВЫПОЛНЕНО». Внизу —
    /// «ДОСТИЖЕНИЯ И ПАСС» (прежняя страница прогресса), «ЗАКРЫТЬ» и кошелёк, где видно, как приходит награда.
    /// Арт — UI/Hub/Hud: task-easy/medium/hard/weekly, task-xp, task-done, task-trophy; пока его нет — круглые кнопки
    /// панели со звёздами, «★» и красная печать текстом. Доска поручений в доме Шестёрки остаётся своей.
    /// </summary>
    public sealed partial class PlayerHubController
    {
        private sealed class TaskRow
        {
            public string Slot, QuestId;
            public RectTransform Rect, Fill, Stamp;
            public CanvasGroup Group;
            public Text Name, Detail, Count, Coins, XP, NewTag;
            public Button Claim, Play;
            public float StampTime = -1;
        }

        private const float TaskRowH = 92, TaskRowW = 1130, TaskBarW = 250;
        // Отступ от края строк: значок слева и кнопка справа не закрывают заклёпки плашки; заголовки, «ДОСКА
        // ШЕСТЁРКИ», нижние кнопки и кошелёк стоят по тем же линиям.
        private const float TaskInset = 36;
        private static readonly Color TaskStampRed = new Color(.86f, .2f, .14f);
        private RectTransform _tasksLayer, _tasksWindow;
        private CanvasGroup _tasksGroup;
        private Text _tasksWallet;
        private readonly List<TaskRow> _taskRows = new List<TaskRow>();
        private readonly HashSet<string> _tasksNew = new HashSet<string>();
        private bool _tasksShown, _tasksEscapeHeld;
        // Экран, поверх которого открыто окно: ушли с него — окно закрывается.
        private string _tasksPage;
        private float _tasksFade, _tasksWalletShown, _tasksClock;
        private bool TasksShown => _tasksShown || _tasksFade > 0;
        /// <summary>Открыто окно поверх города — панель, карта и клавиши улицы ждут.</summary>
        private bool CityWindowShown => ProfileShown || SettingsShown || TasksShown || CollectionShown || CashShown;

        private void ShowTasksWindow()
        {
            if (_state == null || _busy) return;
            if (_tasksLayer == null) BuildTasksWindow();
            _tasksShown = true;
            _tasksPage = _page;
            _tasksLayer.gameObject.SetActive(true);
            _tasksLayer.SetAsLastSibling();
            _tasksWalletShown = _state.Profile.Coins;
            // «НОВОЕ» — на том, что игрок видит впервые; сразу отмечаем просмотренным, как доска Шестёрки.
            _tasksNew.Clear();
            foreach (var quest in _state.Quests.Where(q => !q.Seen)) _tasksNew.Add(quest.Id);
            if (_tasksNew.Count > 0) _ = Dispatch("quests_seen");
            PaintTasksWindow(0);
            RefreshMapButton();
        }

        private void CloseTasksWindow() => _tasksShown = false;

        private void BuildTasksWindow()
        {
            _tasksLayer = CityWindow("Tasks window", "ЗАДАНИЯ", out _tasksGroup, out _tasksWindow);
            TaskHeading("НА СЕГОДНЯ", 312);
            // Те же поручения висят на пробковой доске в доме Шестёрки — туда можно доехать.
            HdPlaque(CityButton(_tasksWindow, "ДОСКА ШЕСТЁРКИ ›", (SettingsWindowW + TaskRowW) / 2 - TaskInset - 300, 54, 300, 46, GoToSixBoard).GetComponent<Image>());
            for (var i = 0; i < 3; i++) _taskRows.Add(BuildTaskRow(HubQuests.Slots[i], 244 - i * (TaskRowH + 8)));
            TaskHeading("НА НЕДЕЛЮ", -32);
            for (var i = 3; i < HubQuests.Slots.Count; i++) _taskRows.Add(BuildTaskRow(HubQuests.Slots[i], -96 - (i - 3) * (TaskRowH + 8)));

            var bottom = SettingsWindowH - 40 - 64;
            var more = CityButton(_tasksWindow, "ДОСТИЖЕНИЯ И ПАСС", (SettingsWindowW - TaskRowW) / 2 + TaskInset, bottom, 360, 64, () => { CloseTasksWindow(); Navigate("progress"); });
            HdPlaque(more.GetComponent<Image>());
            var trophy = HudArt("task-trophy");
            if (trophy != null)
            {
                var cup = CenteredImage(more.transform, "Icon", 16, 6, 52, 52);
                cup.sprite = trophy;
                var caption = more.GetComponentInChildren<Text>().rectTransform;
                caption.sizeDelta = new Vector2(caption.sizeDelta.x - 70, caption.sizeDelta.y);
                caption.anchoredPosition += new Vector2(64, 0);
                // Рядом с кубком длинная надпись не помещается в своём обычном размере.
                more.GetComponentInChildren<Text>().fontSize = 25;
            }
            WindowCloseButton(_tasksWindow, bottom, CloseTasksWindow);
            // Награда «натекает» в кошелёк после «ЗАБРАТЬ».
            _tasksWallet = WindowWallet(_tasksWindow);
        }

        /// <summary>
        /// Кошелёк справа внизу окна (задания, коллекция): вместе с монетой той же ширины, что левая нижняя кнопка, —
        /// нижний ряд симметричен. Возвращает число.
        /// </summary>
        private Text WindowWallet(RectTransform board)
        {
            const float walletW = 360 - 29;
            var plate = QuestArt(board, "Window wallet", null, new Vector2(TaskRowW / 2 - TaskInset - walletW / 2, -318), new Vector2(walletW, 60), Color.white);
            plate.sprite = HudArt("currency-plate");
            plate.type = Image.Type.Sliced;
            plate.pixelsPerUnitMultiplier = 109f / 60;
            var coin = QuestArt(plate.transform, "Coin", null, new Vector2(-walletW / 2, 0), new Vector2(58, 76), Color.white);
            coin.sprite = HubCurrencyArt.Silver;
            coin.preserveAspect = true;
            // Число — посередине между монетой и закруглённым концом.
            var amount = QuestText(plate.transform, "Amount", "", new Vector2(7, 0), new Vector2(walletW - 90, 46), 30, Color.white);
            amount.font = _font;
            amount.fontStyle = FontStyle.Bold;
            return amount;
        }

        private void TaskHeading(string text, float y)
        {
            var label = SettingsSign(_tasksWindow, text, Vector2.zero, new Vector2(400, 36), 30);
            // Буквы вывески встают по центру своей рамки: рамка — ровно по слову, от левого края строк.
            var width = label.preferredWidth + 4;
            label.rectTransform.sizeDelta = new Vector2(width, 36);
            label.rectTransform.anchoredPosition = new Vector2(-TaskRowW / 2 + TaskInset + width / 2, y);
        }

        /// <summary>Строка задания на плашке монет; что в ней показать — решает <see cref="PaintTasksWindow"/>.</summary>
        private TaskRow BuildTaskRow(string slot, float y)
        {
            var row = new TaskRow { Slot = slot };
            var plate = QuestArt(_tasksWindow, "Task " + slot, null, new Vector2(0, y), new Vector2(TaskRowW, TaskRowH), Color.white);
            plate.sprite = HudArt("currency-plate");
            plate.type = Image.Type.Sliced;
            plate.pixelsPerUnitMultiplier = 109f / TaskRowH;
            row.Rect = plate.rectTransform;
            row.Group = plate.gameObject.AddComponent<CanvasGroup>();
            var left = -TaskRowW / 2;

            TaskBadge(plate.transform, slot, new Vector2(left + TaskInset + 39, 0));
            // Две строки (название и срок, полоса и награда) — на 20 выше и ниже середины плашки, на одной высоте с кнопкой.
            row.Name = QuestText(plate.transform, "Name", "", new Vector2(left + 124 + 200, 20), new Vector2(400, 40), 30, SignGoldTop);
            row.Name.alignment = TextAnchor.MiddleLeft;
            row.Name.horizontalOverflow = HorizontalWrapMode.Overflow;
            var outline = row.Name.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(.06f, .03f, 0, .9f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            row.Detail = QuestText(plate.transform, "Detail", "", new Vector2(left + 124 + 200, -17), new Vector2(400, 26), 18, SettingsCream);
            row.Detail.font = _font;
            row.Detail.alignment = TextAnchor.MiddleLeft;

            // Прогресс: жёлоб полосы опыта с золотой заливкой и счёт рядом.
            var track = QuestArt(plate.transform, "Progress", null, new Vector2(90, 19), new Vector2(TaskBarW, 28), Color.white);
            track.sprite = HudArt("xp-bar");
            track.type = Image.Type.Sliced;
            track.pixelsPerUnitMultiplier = 58f / 28;
            var fill = MakeRect(track.transform, "Fill", 21, 8, 0, 12).gameObject.AddComponent<Image>();
            Round(fill, TaskBarW - 42, 12, 6);
            fill.color = new Color(1f, .8f, .25f);
            fill.raycastTarget = false;
            row.Fill = fill.rectTransform;
            // Счёт — посередине между полосой и кнопкой.
            const float actionW = 190, actionH = 58;
            var actionLeft = TaskRowW / 2 - TaskInset - actionW;
            row.Count = QuestText(plate.transform, "Count", "", new Vector2((90 + TaskBarW / 2 + actionLeft) / 2, 19), new Vector2(112, 34), 24, Color.white);
            row.Count.font = _font;
            row.Count.fontStyle = FontStyle.Bold;
            row.Count.horizontalOverflow = HorizontalWrapMode.Overflow;

            // Награда: серебро и опыт под полосой.
            var coin = QuestArt(plate.transform, "Reward coin", null, new Vector2(90 - TaskBarW / 2 + 13, -17), new Vector2(26, 34), Color.white);
            coin.sprite = HubCurrencyArt.Silver;
            coin.preserveAspect = true;
            row.Coins = QuestText(plate.transform, "Reward coins", "", new Vector2(36, -17), new Vector2(76, 28), 20, Color.white);
            row.Coins.font = _font;
            row.Coins.alignment = TextAnchor.MiddleLeft;
            var xpArt = HudArt("task-xp");
            if (xpArt != null)
            {
                var star = QuestArt(plate.transform, "Reward XP star", null, new Vector2(84, -17), new Vector2(30, 30), Color.white);
                star.sprite = xpArt;
                star.preserveAspect = true;
            }
            else QuestText(plate.transform, "Reward XP star", "★", new Vector2(84, -17), new Vector2(30, 30), 24, SignGoldTop);
            row.XP = QuestText(plate.transform, "Reward XP", "", new Vector2(162, -17), new Vector2(110, 28), 20, Color.white);
            row.XP.font = _font;
            row.XP.alignment = TextAnchor.MiddleLeft;
            // Награда — всегда в одну строку (на телефоне шрифт крупнее).
            row.XP.horizontalOverflow = row.Coins.horizontalOverflow = HorizontalWrapMode.Overflow;

            // Справа — действие строки, в закруглённом конце плашки.
            var actionX = TaskRowW - TaskInset - actionW;
            row.Claim = CityButton(plate.transform, "ЗАБРАТЬ", actionX, (TaskRowH - actionH) / 2, actionW, actionH, () => ClaimTask(row));
            HdPlaque(row.Claim.GetComponent<Image>());
            row.Play = CityButton(plate.transform, "ИГРАТЬ", actionX, (TaskRowH - actionH) / 2, actionW, actionH, () => PlayForTask(row));
            HdPlaque(row.Play.GetComponent<Image>());
            row.Play.GetComponentInChildren<Text>().color = new Color(1, 1, 1, .8f);

            row.Stamp = QuestRect(plate.transform, "Done stamp", new Vector2(TaskRowW / 2 - TaskInset - actionW / 2, 0), new Vector2(actionW, actionH));
            var done = HudArt("task-done");
            if (done != null)
            {
                // Зелёная печать с галочкой и надпись рядом, ровно.
                var seal = QuestArt(row.Stamp, "Seal", null, new Vector2(-actionW / 2 + 18, 0), new Vector2(60, 60), Color.white);
                seal.sprite = done;
                seal.preserveAspect = true;
                var label = SettingsSign(row.Stamp, "ВЫПОЛНЕНО", new Vector2(22, 0), new Vector2(120, 40), 23);
                label.GetComponent<HubSignText>().Top = new Color(.75f, 1f, .6f);
                label.GetComponent<HubSignText>().Bottom = new Color(.35f, .78f, .3f);
            }
            else
            {
                var stamp = QuestText(row.Stamp, "Stamp text", "ВЫПОЛНЕНО", Vector2.zero, new Vector2(actionW, actionH - 8), 30, TaskStampRed);
                foreach (var edge in new[] { -1f, 1f })
                    QuestArt(row.Stamp, "Stamp rule", null, new Vector2(0, edge * (actionH / 2 - 6)), new Vector2(actionW - 10, 3), TaskStampRed);
                stamp.horizontalOverflow = HorizontalWrapMode.Overflow;
            }
            if (done == null) row.Stamp.localRotation = Quaternion.Euler(0, 0, -7);

            var tag = QuestArt(plate.transform, "New tag", null, new Vector2(left + 136, TaskRowH / 2 - 5), new Vector2(84, 26), new Color(.72f, .12f, .06f));
            tag.sprite = UiTextures.RoundedRect(64, 32, 10, Color.white);
            tag.type = Image.Type.Sliced;
            row.NewTag = QuestText(tag.transform, "New label", "НОВОЕ", Vector2.zero, new Vector2(80, 24), 18, new Color(1, .92f, .72f));
            return row;
        }

        /// <summary>Значок сложности: картинка task-* или круглая кнопка панели (зелёная, дерево, красная, синяя) со звёздами.</summary>
        private void TaskBadge(Transform parent, string slot, Vector2 at)
        {
            var art = HudArt(slot == "easy" ? "task-easy" : slot == "medium" ? "task-medium" : slot == "hard" ? "task-hard" : "task-weekly");
            if (art != null)
            {
                var icon = QuestArt(parent, "Tier", null, at, new Vector2(78, 78), Color.white);
                icon.sprite = art;
                icon.preserveAspect = true;
                return;
            }
            var disc = QuestArt(parent, "Tier", null, at, new Vector2(70, 70), Color.white);
            disc.sprite = slot == "weekly-0" || slot == "weekly-1" ? HudArt("level-badge")
                : HudArt(slot == "easy" ? "button-green" : slot == "medium" ? "button-wood" : "button-red");
            disc.preserveAspect = true;
            var stars = slot == "easy" ? "★" : slot == "medium" ? "★★" : slot == "hard" ? "★★★" : "7";
            var mark = QuestText(disc.transform, "Stars", stars, new Vector2(0, 1), new Vector2(64, 40), stars.Length > 2 ? 18 : 24, SignGoldTop);
            mark.horizontalOverflow = HorizontalWrapMode.Overflow;
            mark.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, .7f);
        }

        private async void ClaimTask(TaskRow row)
        {
            var quest = _state?.Quests.FirstOrDefault(q => q.Id == row.QuestId);
            if (quest == null || !HubQuests.Ready(quest) || _busy) return;
            var result = await Dispatch("quest_claim", quest.Id);
            if (this == null || !result.Success) return;
            row.StampTime = 0;
            _tasksNew.Remove(quest.Id);
        }

        /// <summary>
        /// К доске Шестёрки — как «Магазин» к Лавке (<see cref="GoToShop"/>): на улицу, проезд к его дому, нырок внутрь
        /// и подход к доске; на месте она открывается сама (<see cref="ShowSixQuests"/>).
        /// </summary>
        private void GoToSixBoard()
        {
            CloseTasksWindow();
            GoHome();
            _mapTripDistrict = -1;
            _mapTripPlace = Array.FindIndex(CityPlaces, place => place.Id == "six-quests");
            ContinueMapTrip();
        }

        /// <summary>«ИГРАТЬ» у невыполненного задания: к его игре, если она названа, иначе к выбору игры.</summary>
        private void PlayForTask(TaskRow row)
        {
            var quest = _state?.Quests.FirstOrDefault(q => q.Id == row.QuestId);
            CloseTasksWindow();
            if (quest != null && !string.IsNullOrEmpty(quest.Game)) Navigate("game:" + quest.Game);
            else Navigate("games");
        }

        /// <summary>Каждый кадр: Esc, проявление, таймеры, прогресс, печать и кошелёк.</summary>
        private void UpdateTasksWindow(float deltaTime, bool typing)
        {
            if (_tasksLayer == null) return;
            if (_tasksShown && _page != _tasksPage) _tasksShown = false;
            var escape = !typing && Keyboard.current != null && Keyboard.current.escapeKey.isPressed;
            if (_tasksShown && escape && !_tasksEscapeHeld) CloseTasksWindow();
            _tasksEscapeHeld = escape;
            _tasksFade = Mathf.MoveTowards(_tasksFade, _tasksShown ? 1 : 0, deltaTime / .18f);
            _tasksGroup.alpha = Mathf.SmoothStep(0, 1, _tasksFade);
            _tasksGroup.blocksRaycasts = _tasksShown;
            if (!TasksShown)
            {
                // Кнопка карты обновляется не каждый кадр: вернуть её, как только окно погасло.
                if (_tasksLayer.gameObject.activeSelf) { _tasksLayer.gameObject.SetActive(false); RefreshMapButton(); }
                return;
            }
            var reduced = _state != null && _state.Settings.ReducedMotion;
            _tasksWindow.localScale = Vector3.one * (reduced ? 1 : Mathf.Lerp(.94f, 1, _tasksGroup.alpha));
            _tasksLayer.SetAsLastSibling();
            PaintTasksWindow(deltaTime);
        }

        private void PaintTasksWindow(float deltaTime)
        {
            if (_state == null) return;
            var reduced = _state.Settings.ReducedMotion;
            _tasksClock += deltaTime;
            foreach (var row in _taskRows)
            {
                var quest = _state.Quests.FirstOrDefault(q => q.Slot == row.Slot);
                row.Rect.gameObject.SetActive(quest != null);
                if (quest == null) continue;
                if (row.QuestId != quest.Id)
                {
                    row.QuestId = quest.Id;
                    row.StampTime = -1;
                    row.Name.text = quest.Name;
                    // Длинное название мельчает, а не заезжает на полосу прогресса.
                    row.Name.fontSize = 30;
                    var room = row.Name.rectTransform.rect.width;
                    if (row.Name.preferredWidth > room) row.Name.fontSize = Mathf.Max(18, Mathf.FloorToInt(30 * room / row.Name.preferredWidth));
                }
                var ready = HubQuests.Ready(quest);
                var remaining = Math.Max(0, quest.ExpiresAt - GiftNow);
                row.Detail.text = HubQuests.TierName(quest.Tier) + " · "
                    + (quest.Claimed ? "новое через " + QuestDuration(remaining) : ready ? "награда ждёт!" : "осталось " + QuestDuration(remaining));
                row.Detail.color = ready ? new Color(.62f, 1f, .45f) : SettingsCream;
                var count = Math.Min(quest.Progress, quest.Goal) + " / " + quest.Goal;
                if (row.Count.text != count)
                {
                    // «1499 / 1500» — в одну строку: длинный счёт мельчает.
                    row.Count.text = count;
                    row.Count.fontSize = 24;
                    if (row.Count.preferredWidth > 112) row.Count.fontSize = Mathf.Max(15, Mathf.FloorToInt(24 * 112 / row.Count.preferredWidth));
                }
                var share = Mathf.Clamp01(quest.Progress / (float)Math.Max(1, quest.Goal));
                row.Fill.sizeDelta = new Vector2((TaskBarW - 42) * share, 12);
                row.Fill.GetComponent<Image>().enabled = share > 0;
                row.Coins.text = "+" + quest.RewardCoins;
                row.XP.text = "+" + quest.RewardXP + " опыта";
                row.NewTag.transform.parent.gameObject.SetActive(_tasksNew.Contains(quest.Id) && !quest.Claimed);
                row.Group.alpha = quest.Claimed ? .7f : 1;

                row.Claim.gameObject.SetActive(ready);
                row.Claim.interactable = !_busy;
                // Готовая награда чуть «дышит», чтобы её заметили.
                row.Claim.transform.localScale = Vector3.one * (ready && !reduced ? 1 + .04f * Mathf.Sin(_tasksClock * 4) : 1);
                row.Play.gameObject.SetActive(!ready && !quest.Claimed);
                row.Stamp.gameObject.SetActive(quest.Claimed);
                if (row.StampTime >= 0)
                {
                    row.StampTime += reduced ? 1 : deltaTime;
                    var t = Mathf.Clamp01(row.StampTime / .28f);
                    row.Stamp.localScale = Vector3.one * Mathf.Lerp(1.6f, 1, Mathf.SmoothStep(0, 1, t));
                    if (t >= 1) row.StampTime = -1;
                }
                else row.Stamp.localScale = Vector3.one;
            }
            // Награда «натекает» в кошелёк за полсекунды.
            var coins = _state.Profile.Coins;
            _tasksWalletShown = reduced || deltaTime <= 0 ? coins
                : Mathf.MoveTowards(_tasksWalletShown, coins, Mathf.Max(1, Mathf.Abs(coins - _tasksWalletShown)) * deltaTime / .5f + 1);
            _tasksWallet.text = HubMoney(Mathf.RoundToInt(_tasksWalletShown));
        }
    }
}
