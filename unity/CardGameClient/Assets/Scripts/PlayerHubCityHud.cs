using System;
using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>
    /// Панель города (2026-10-31): всегда видна, пока игрок гуляет по городу. Слева сверху — аватар в рамке с мастями,
    /// уровень и полоса опыта (профиль), под ним друзья и почта; справа сверху — серебряные и золотые тун-тун коины
    /// и настройки, под ними задания и коллекция (круглые кнопки с красными точками); справа снизу — «ИГРАТЬ».
    /// В зданиях остаются только монеты и настройки (слева там «ВЫЙТИ»), у Лавки и рынка — свой кошелёк, панели нет.
    /// Арт — UI/Hub/Hud (листы ChatGPT, нарезка scripts/hud/cut_hud.py); цифры и надписи — кодом.
    /// </summary>
    public sealed partial class PlayerHubController
    {
        private RectTransform _cityHud, _hudProfile, _hudWallet, _hudSideLeft, _hudSideRight, _hudPlay;
        private Text _hudName, _hudLevel, _hudSilver, _hudGold;
        private Image _hudPortrait, _hudXp;
        private readonly (string Key, GameObject Dot, Text Count)[] _hudBadges = new (string, GameObject, Text)[4];

        private static Sprite HudArt(string name) => Resources.Load<Sprite>("UI/Hub/Hud/" + name);

        private void BuildCityHud()
        {
            if (_cityHud != null || _stage == null || HudArt("avatar-frame") == null) return;
            _cityHud = StageLayer("City HUD");
            var phone = MobileUi.Phone ? 1.2f : 1f;

            // Профиль: опора в левом верхнем углу, на телефоне растёт внутрь экрана.
            _hudProfile = MakeRect(_cityHud, "Profile", 16, 10, 400, 112);
            _hudProfile.localScale = Vector3.one * phone;
            // Круг чуть больше окна рамки (её окно — 64 % ширины, центр на 49 % высоты) и уходит под золото без щели.
            var disc = MakeRect(_hudProfile, "Avatar disc", 16, 15, 68, 68).gameObject.AddComponent<Image>();
            disc.sprite = UiTextures.Circle(128, Color.white);
            disc.color = new Color(.3f, .16f, .1f);
            disc.raycastTarget = false;
            disc.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            // Портрет крупнее круга: в круг попадают голова и плечи.
            _hudPortrait = CenteredImage(disc.transform, "Current avatar", -14, -4, 96, 108);
            HudImage(_hudProfile, "avatar-frame", "Avatar frame", 0, 0, 100, 100);
            var badge = HudImage(_hudProfile, "level-badge", "Level badge", 70, 64, 42, 42);
            _hudLevel = MakeLabel(badge.transform, "1", 0, 2, 42, 38, 20);
            _hudLevel.alignment = TextAnchor.MiddleCenter;
            _hudLevel.fontStyle = FontStyle.Bold;
            _hudLevel.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(0, -2);
            _hudName = MakeLabel(_hudProfile, "Игрок", 112, 16, 280, 36, 26);
            _hudName.fontStyle = FontStyle.Bold;
            _hudName.resizeTextForBestFit = true;
            _hudName.resizeTextMinSize = 16;
            _hudName.resizeTextMaxSize = 26;
            var nameShadow = _hudName.gameObject.AddComponent<Shadow>();
            nameShadow.effectColor = new Color(0, 0, 0, .85f);
            nameShadow.effectDistance = new Vector2(1, -2);
            var bar = HudImage(_hudProfile, "xp-bar", "XP", 108, 56, 240, 28);
            bar.type = Image.Type.Sliced;
            bar.pixelsPerUnitMultiplier = 58f / 28;
            // Заполнение — внутри деревянного жёлоба, между заклёпками.
            _hudXp = MakeRect(bar.transform, "Progress", 21, 8, 0, 12).gameObject.AddComponent<Image>();
            Round(_hudXp, 198, 12, 6);
            _hudXp.color = new Color(1f, .8f, .25f);
            _hudXp.raycastTarget = false;
            OverlayButton(_hudProfile, "Профиль", 0, 0, 350, 104, () => Navigate("profile"));
            PinToCorner(_hudProfile, -1);

            // Кошелёк и настройки: опора в правом верхнем углу.
            // Плашка 200, «плюс» выступает за её правый край на 22, монета — за левый; между плашками — зазор под них.
            const float walletWidth = 620, gear = 82, plateStep = 254;
            _hudWallet = MakeRect(_cityHud, "Wallet", 1600 - 16, 10, walletWidth, 90);
            _hudWallet.pivot = new Vector2(1, 1);
            _hudWallet.localScale = Vector3.one * phone;
            HudRound(_hudWallet, "Настройки", "button-wood", "icon-settings", walletWidth - gear, 4, gear, ShowSettingsWindow);
            var goldX = walletWidth - gear - 30 - 222;
            // «Плюсы» — касса банка Тузяры на нужной вкладке (PlayerHubCashWindow.cs).
            _hudGold = HudCurrency(_hudWallet, HubCurrencyArt.Gold, goldX, () => ShowCashWindow(1));
            _hudSilver = HudCurrency(_hudWallet, HubCurrencyArt.Silver, goldX - plateStep, () => ShowCashWindow(0));
            PinToCorner(_hudWallet, 1);

            // Разделы — по краям: слева под профилем люди (друзья, почта), справа под кошельком задания и коллекция.
            _hudSideLeft = HudColumn("Sections left", 16, 136, new Vector2(0, 1), 0,
                ("friends", "Друзья", "icon-friends"), ("chats", "Почта", "icon-mail"));
            PinToCorner(_hudSideLeft, -1);
            _hudSideRight = HudColumn("Sections right", 1600 - 16, 120, new Vector2(1, 1), 2,
                ("progress", "Задания", "icon-quests"), ("inventory", "Коллекция", "icon-collection"));
            PinToCorner(_hudSideRight, 1);

            // «ИГРАТЬ» — справа снизу, к выбору игры.
            _hudPlay = MakeRect(_cityHud, "Play", 1600 - 24, 900 - 24 - 104, 380, 104);
            _hudPlay.pivot = new Vector2(1, 1);
            _hudPlay.localScale = Vector3.one * phone;
            var plate = HudImage(_hudPlay, "play-plate", "Plate", 0, 0, 380, 104);
            plate.type = Image.Type.Sliced;
            plate.pixelsPerUnitMultiplier = 176f / 104;
            plate.raycastTarget = true;
            plate.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2, -4);
            HudSignLabel(plate.transform, "ИГРАТЬ", 0, 6, 380, 88, 60);
            HudButton(plate, OpenGames);
            _cityHud.gameObject.SetActive(false);
            RefreshCityHud();
        }

        /// <summary>Столбик круглых кнопок с подписью и красной точкой; <paramref name="pivot"/> — угол, к которому он прижат.</summary>
        private RectTransform HudColumn(string name, float x, float y, Vector2 pivot, int firstBadge,
            params (string Page, string Title, string Icon)[] items)
        {
            const float size = 100, label = 34, step = size + label + 12, width = 130;
            var column = MakeRect(_cityHud, name, x, y, width, items.Length * step);
            column.pivot = pivot;
            column.localScale = Vector3.one * (MobileUi.Phone ? 1.2f : 1f);
            for (var i = 0; i < items.Length; i++)
            {
                var (page, title, icon) = items[i];
                // «Задания» — окно поверх города (PlayerHubTasksWindow.cs), остальное — страницы разделов.
                Action go = page == "progress" ? (Action)ShowTasksWindow : () => Navigate(page);
                var button = HudRound(column, title, "button-wood", icon, (width - size) / 2, i * step, size, go);
                HudSignLabel(column, title, 0, i * step + size, width, label, 28);
                var (dot, count) = Badge(button.transform, size - 28, -2);
                _hudBadges[firstBadge + i] = (page, dot, count);
            }
            return column;
        }

        private Image HudImage(Transform parent, string art, string name, float x, float y, float w, float h)
        {
            var image = MakeRect(parent, name, x, y, w, h).gameObject.AddComponent<Image>();
            image.sprite = HudArt(art);
            image.raycastTarget = false;
            return image;
        }

        /// <summary>Нажатие как у кнопок-вывесок: под мышью светлее, нажатая темнее и проседает.</summary>
        private static Button HudButton(Image target, Action action)
        {
            var button = target.gameObject.AddComponent<Button>();
            button.targetGraphic = target;
            var colors = button.colors;
            colors.normalColor = new Color(.92f, .92f, .92f);
            colors.highlightedColor = Color.white;
            colors.selectedColor = new Color(.92f, .92f, .92f);
            colors.pressedColor = new Color(.72f, .72f, .72f);
            button.colors = colors;
            button.onClick.AddListener(() => action());
            target.gameObject.AddComponent<HubPress>();
            return button;
        }

        /// <summary>Круглая кнопка: золотая оправа с деревом и значок поверх; имя — для проверок.</summary>
        private Button HudRound(Transform parent, string name, string art, string icon, float x, float y, float size, Action action)
        {
            var rect = MakeRect(parent, name, x, y, size, size);
            // Опора в центре: нажатая кнопка проседает к своей середине.
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(x + size / 2, -y - size / 2);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = HudArt(art);
            image.preserveAspect = true;
            var shadow = rect.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, .5f);
            shadow.effectDistance = new Vector2(1, -3);
            // Опора значка в центре: иначе широкий значок (письмо) прижимается к верху круга.
            var mark = CenteredImage(rect, "Icon", size * .19f, size * .19f, size * .62f, size * .62f);
            mark.sprite = HudArt(icon);
            return HudButton(image, action);
        }

        /// <summary>Плашка монет: дерево в золотой рамке, монета заходит на левый край, справа — «плюс».</summary>
        private Text HudCurrency(Transform parent, Sprite coin, float x, Action plus)
        {
            var plate = HudImage(parent, "currency-plate", "Currency pill", x, 18, 200, 54);
            plate.type = Image.Type.Sliced;
            plate.pixelsPerUnitMultiplier = 109f / 54;
            var icon = MakeRect(parent, "Coin", x - 22, 6, 58, 76).gameObject.AddComponent<Image>();
            icon.sprite = coin;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            var amount = MakeLabel(plate.transform, "", 40, 8, plus == null ? 146 : 132, 38, 26);
            amount.fontStyle = FontStyle.Bold;
            amount.alignment = TextAnchor.MiddleCenter;
            amount.resizeTextForBestFit = true;
            amount.resizeTextMinSize = 14;
            amount.resizeTextMaxSize = 26;
            var shade = amount.gameObject.AddComponent<Shadow>();
            shade.effectColor = new Color(0, 0, 0, .8f);
            shade.effectDistance = new Vector2(1, -2);
            if (plus != null)
            {
                // «Плюс» по центру высоты плашки и на её правом краю, как монета на левом.
                var add = HudImage(plate.transform, "icon-plus", "Пополнить", 176, 4, 46, 46);
                add.preserveAspect = true;
                add.raycastTarget = true;
                HudButton(add, plus);
            }
            return amount;
        }

        /// <summary>Золотые буквы узким шрифтом вывесок, как на кнопках города.</summary>
        private Text HudSignLabel(Transform parent, string text, float x, float y, float w, float h, int size)
        {
            var label = MakeLabel(parent, text, x, y, w, h, size);
            var font = s_signNarrow != null ? s_signNarrow : s_signNarrow = Resources.Load<Font>("Fonts/SignNarrow");
            if (font != null) label.font = font;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            // У узкого шрифта вывесок высокая строка: крупная надпись не помещается в рамку и иначе пропадает целиком.
            label.verticalOverflow = VerticalWrapMode.Overflow;
            var look = label.gameObject.AddComponent<HubSignText>();
            look.Top = SignGoldTop;
            look.Bottom = SignGoldBottom;
            var shade = label.gameObject.AddComponent<Outline>();
            shade.effectColor = new Color(.06f, .03f, 0, .9f);
            shade.effectDistance = new Vector2(1.5f, -1.5f);
            return label;
        }

        /// <summary>Каждый кадр: панель — пока игрок стоит в городе без открытых окон, подходов и нырков.</summary>
        private void UpdateCityHud()
        {
            // Сообщение (покупка в кассе, ошибка) над открытым окном — внизу, под доской: сверху его закрыла бы вывеска окна.
            if (_toastPill != null)
            {
                var over = CityWindowShown;
                _toastPill.rectTransform.anchoredPosition = new Vector2(420, over ? -(900 - 2 - 46) : -25);
                if (over && _toastPill.gameObject.activeSelf) _toastPill.transform.SetAsLastSibling();
            }
            var free = _state != null && _page == "home" && _diveTarget < 0 && _visitPhase == 0 && !_mapOpen && _mapFade <= 0
                && !GiftShown && !QuestsShown && !BankShown && !ArchiveShown && !CasinoShown && !ShopInside && !MarketInside
                && !CityWindowShown;
            if (free) BuildCityHud();
            if (_cityHud == null) return;
            if (_cityHud.gameObject.activeSelf != free) _cityHud.gameObject.SetActive(free);
            if (!free) return;
            // В зданиях слева «ВЫЙТИ»: профиль, разделы и «ИГРАТЬ» — только на улице.
            var street = !IsInterior(_cityLevel);
            if (_hudProfile.gameObject.activeSelf != street) _hudProfile.gameObject.SetActive(street);
            if (_hudSideLeft.gameObject.activeSelf != street) _hudSideLeft.gameObject.SetActive(street);
            if (_hudSideRight.gameObject.activeSelf != street) _hudSideRight.gameObject.SetActive(street);
            if (_hudPlay.gameObject.activeSelf != street) _hudPlay.gameObject.SetActive(street);
            // PinToCorner держит верхние углы; «ИГРАТЬ» — нижний правый угол окна, как значок карты в левом.
            var size = ((RectTransform)transform).rect.size / Mathf.Max(.01f, _stage.localScale.x);
            _hudPlay.anchoredPosition = new Vector2(1600 - 24 + Mathf.Max(0, size.x - 1600) / 2,
                -(900 - 24 - 104) - Mathf.Max(0, size.y - 900) / 2);
            _cityHud.SetAsLastSibling();
        }

        /// <summary>Имя, уровень, опыт, монеты и красные точки — вместе с остальной шапкой.</summary>
        private void RefreshCityHud()
        {
            if (_cityHud == null || _state == null) return;
            var profile = _state.Profile;
            _hudName.text = profile.DisplayName;
            _hudLevel.text = profile.Level.ToString();
            _hudXp.rectTransform.sizeDelta = new Vector2(198 * Mathf.Clamp01((profile.XP % 100) / 100f), 12);
            _hudXp.enabled = profile.XP % 100 > 0;
            _hudSilver.text = HubMoney(profile.Coins);
            _hudGold.text = HubMoney(profile.Gold);
            _hudPortrait.sprite = PlayerProfile.Avatar;
            foreach (var (key, dot, count) in _hudBadges)
            {
                if (dot == null) continue;
                var value = key == "progress" ? ReadyQuests()
                    : key == "friends" ? BadgeFor("friends") - MailUnreadAll().Count
                    : key == "chats" ? MailUnreadAll().Count : 0;
                SetBadge(dot, count, value);
            }
        }
    }
}
