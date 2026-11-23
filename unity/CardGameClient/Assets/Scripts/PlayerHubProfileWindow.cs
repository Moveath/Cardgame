using System;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>Профиль на общей доске города: портрет, имя, опыт и результаты. Пять значков — из листа profile-icons.</summary>
    public sealed partial class PlayerHubController
    {
        private RectTransform _profileLayer, _profileWindow;
        private CanvasGroup _profileGroup;
        private Image _profilePortrait, _profileXpFill;
        private InputField _profileNameInput;
        private Button _profileSave;
        private Text _profileName, _profileRank, _profileLevel, _profileXp, _profileId, _profileNameHint;
        private Text _profileMatches, _profileLosses, _profileWins, _profileWinRate, _profileRating, _profileRatingRank;
        private Text _profileAchievements, _profileProgress, _profileSilver, _profileGold, _profileEarned, _profileDebt;
        private bool _profileShown, _profileEscapeHeld;
        private float _profileFade;
        private string _profilePage, _profileBoundName;
        private static Sprite[] s_profileIcons;
        private bool ProfileShown => _profileShown || _profileFade > 0;

        private void ShowProfileWindow()
        {
            if (_state == null || _busy) return;
            if (_profileLayer == null) BuildProfileWindow();
            _profileShown = true;
            _profilePage = _page;
            _profileBoundName = _state.Profile.DisplayName;
            _profileNameInput.SetTextWithoutNotify(_profileBoundName);
            _profileNameHint.text = "От 2 до 24 символов";
            _profileEscapeHeld = Keyboard.current != null && Keyboard.current.escapeKey.isPressed;
            _profileLayer.gameObject.SetActive(true);
            _profileLayer.SetAsLastSibling();
            _profileGroup.blocksRaycasts = true;
            PaintProfileWindow();
            RefreshMapButton();
        }

        private void CloseProfileWindow()
        {
            _profileShown = false;
            if (_profileNameInput != null) _profileNameInput.DeactivateInputField();
            var events = UnityEngine.EventSystems.EventSystem.current;
            if (events != null && events.currentSelectedGameObject != null && _profileLayer != null
                && events.currentSelectedGameObject.transform.IsChildOf(_profileLayer)) events.SetSelectedGameObject(null);
        }

        // Переход к соседнему окну не оставляет под ним вторую доску и второй обработчик Esc.
        private void ProfileNavigate(string page)
        {
            if (_busy) return;
            DismissProfileWindow();
            Navigate(page);
        }

        private void DismissProfileWindow()
        {
            CloseProfileWindow();
            _profileFade = 0;
            _profileGroup.blocksRaycasts = false;
            _profileLayer.gameObject.SetActive(false);
        }

        private async void SaveProfileName()
        {
            if (_busy || _state == null) return;
            var name = _profileNameInput.text.Trim();
            if (name.Length < 2 || name.Length > 24)
            {
                _profileNameHint.text = "Нужно от 2 до 24 символов";
                return;
            }
            // Имя меняется отдельно от аватара: выбор персонажа остаётся за его галереей.
            var result = await Dispatch("profile_update", text: name);
            if (this == null) return;
            _profileNameHint.text = result.Success ? "Имя сохранено" : PlayerHubRuntime.ErrorText(result);
            if (result.Success)
            {
                _profileBoundName = _state.Profile.DisplayName;
                _profileNameInput.SetTextWithoutNotify(_profileBoundName);
            }
            PaintProfileWindow();
        }

        private void BuildProfileWindow()
        {
            _profileLayer = CityWindow("Profile window", "МОЙ ПРОФИЛЬ", out _profileGroup, out _profileWindow);
            BuildProfilePortrait();

            ProfileSign(_profileWindow, "ИМЯ ИГРОКА", new Vector2(-55, 304), new Vector2(270, 32), 27);
            _profileNameHint = ProfileText(_profileWindow, "Name hint", "От 2 до 24 символов",
                new Vector2(347, 304), new Vector2(430, 28), 18);
            _profileNameHint.alignment = TextAnchor.MiddleRight;
            var input = ProfilePanel(_profileWindow, "Player name input", new Vector2(70, 251), new Vector2(520, 66));
            input.raycastTarget = true;
            _profileNameInput = input.gameObject.AddComponent<InputField>();
            _profileNameInput.targetGraphic = input;
            _profileNameInput.textComponent = ProfileText(input.transform, "Name text", "", Vector2.zero, new Vector2(476, 48), 28);
            _profileNameInput.textComponent.alignment = TextAnchor.MiddleLeft;
            _profileNameInput.textComponent.supportRichText = false;
            _profileNameInput.characterLimit = 24;
            _profileNameInput.lineType = InputField.LineType.SingleLine;
            _profileNameInput.onValueChanged.AddListener(_ => _profileNameHint.text = "От 2 до 24 символов");
            _profileSave = ProfileButton(_profileWindow, "СОХРАНИТЬ", new Vector2(455, 251), new Vector2(210, 64), SaveProfileName);
            _profileId = ProfileText(_profileWindow, "Player ID", "", new Vector2(185, 198), new Vector2(750, 28), 16);
            _profileId.alignment = TextAnchor.MiddleLeft;
            _profileId.color = new Color(.72f, .63f, .48f);

            BuildProfileStat("СЫГРАНО", "profile-played", -65, out _profileMatches, out _profileLosses);
            BuildProfileStat("ПОБЕДЫ", "profile-wins", 190, out _profileWins, out _profileWinRate);
            var rating = BuildProfileStat("РЕЙТИНГ", "profile-rating", 445, out _profileRating, out _profileRatingRank);
            var rankButton = rating.gameObject.AddComponent<Button>();
            rankButton.targetGraphic = rating; rating.raycastTarget = true;
            rankButton.onClick.AddListener(() => ProfileNavigate("ranks"));
            rating.gameObject.AddComponent<HubPress>();

            var progress = ProfilePanel(_profileWindow, "Profile progress", new Vector2(-5, -105), new Vector2(370, 226));
            ProfileSign(progress.transform, "ДОСТИЖЕНИЯ", new Vector2(0, 82), new Vector2(320, 32), 27);
            ProfileIcon(progress.transform, "profile-achievements", new Vector2(-112, 30), 64);
            _profileAchievements = ProfileSign(progress.transform, "", new Vector2(36, 30), new Vector2(220, 48), 42);
            _profileProgress = ProfileText(progress.transform, "Progress details", "", new Vector2(0, -28), new Vector2(320, 46), 19);
            ProfileButton(progress.transform, "ОТКРЫТЬ", new Vector2(0, -79), new Vector2(300, 44), () => ProfileNavigate("achievements"), 23);

            var wallet = ProfilePanel(_profileWindow, "Profile wallet", new Vector2(380, -105), new Vector2(370, 226));
            ProfileSign(wallet.transform, "КОШЕЛЁК", new Vector2(0, 82), new Vector2(320, 32), 27);
            var silver = ProfileIcon(wallet.transform, null, new Vector2(-144, 30), 42); silver.sprite = HubCurrencyArt.Silver;
            _profileSilver = ProfileText(wallet.transform, "Silver balance", "", new Vector2(-57, 30), new Vector2(132, 40), 24);
            var gold = ProfileIcon(wallet.transform, null, new Vector2(30, 30), 42); gold.sprite = HubCurrencyArt.Gold;
            _profileGold = ProfileText(wallet.transform, "Gold balance", "", new Vector2(113, 30), new Vector2(120, 40), 24);
            _profileEarned = ProfileText(wallet.transform, "Total earned", "", new Vector2(0, -17), new Vector2(326, 30), 18);
            _profileDebt = ProfileText(wallet.transform, "Debt", "", new Vector2(0, -45), new Vector2(326, 26), 18);
            ProfileButton(wallet.transform, "В БАНК", new Vector2(0, -79), new Vector2(300, 44), () => ProfileNavigate("bank"), 23);

            ProfileText(_profileWindow, "Profile storage", "Профиль сохранён на этом устройстве",
                new Vector2(185, -248), new Vector2(750, 28), 17).color = new Color(.72f, .63f, .48f);
            ProfileButton(_profileWindow, "КОЛЛЕКЦИЯ", new Vector2(-390, -318), new Vector2(300, 64), () => ProfileNavigate("inventory"));
            WindowCloseButton(_profileWindow, SettingsWindowH - 40 - 64, CloseProfileWindow);
            ProfileButton(_profileWindow, "НАСТРОЙКИ", new Vector2(390, -318), new Vector2(300, 64), () => ProfileNavigate("settings"));
        }

        private void BuildProfilePortrait()
        {
            var panel = ProfilePanel(_profileWindow, "Profile identity", new Vector2(-390, 40), new Vector2(350, 574));
            ProfileSign(panel.transform, "ПЕРСОНАЖ", new Vector2(0, 251), new Vector2(300, 36), 28);
            const float ring = 220, disc = ring * .68f, k = disc / 68;
            var circle = QuestArt(panel.transform, "Avatar disc", null, new Vector2(0, 135), new Vector2(disc, disc), new Color(.3f, .16f, .1f));
            circle.sprite = UiTextures.Circle(256, Color.white);
            circle.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            _profilePortrait = CenteredImage(circle.transform, "Current avatar", -14 * k, -4 * k, 96 * k, 108 * k);
            ProfileIcon(panel.transform, "avatar-frame", new Vector2(0, 133), ring);
            _profileName = ProfileSign(panel.transform, "", new Vector2(0, -10), new Vector2(308, 48), 36);
            _profileName.supportRichText = false;
            _profileRank = ProfileText(panel.transform, "Rank", "", new Vector2(0, -51), new Vector2(300, 30), 21);
            var badge = ProfileIcon(panel.transform, "profile-level", new Vector2(-108, -99), 82);
            // Центр бордового поля ниже центра спрайта: сверху у медальона корона.
            _profileLevel = ProfileText(badge.transform, "Level number", "", new Vector2(0, -7), new Vector2(42, 38), 27);
            _profileLevel.fontStyle = FontStyle.Bold;
            var levelShadow = _profileLevel.gameObject.AddComponent<Shadow>();
            levelShadow.effectColor = new Color(.15f, .015f, .01f, .9f); levelShadow.effectDistance = new Vector2(1, -1);
            ProfileSign(panel.transform, "УРОВЕНЬ", new Vector2(35, -105), new Vector2(222, 42), 32);
            var bar = ProfileIcon(panel.transform, "xp-bar", new Vector2(0, -161), 0);
            bar.rectTransform.sizeDelta = new Vector2(290, 28);
            bar.preserveAspect = false; bar.type = Image.Type.Sliced; bar.pixelsPerUnitMultiplier = 58f / 28;
            _profileXpFill = MakeRect(bar.transform, "Experience fill", 21, 8, 0, 12).gameObject.AddComponent<Image>();
            Round(_profileXpFill, 248, 12, 6); _profileXpFill.color = new Color(1, .8f, .25f); _profileXpFill.raycastTarget = false;
            _profileXp = ProfileText(panel.transform, "Experience", "", new Vector2(0, -190), new Vector2(306, 32), 18);
            ProfileButton(panel.transform, "СМЕНИТЬ АВАТАР", new Vector2(0, -238), new Vector2(300, 56), OpenAvatarPicker, 26);
        }

        private Image BuildProfileStat(string title, string icon, float x, out Text amount, out Text detail)
        {
            var panel = ProfilePanel(_profileWindow, "Profile stat " + title, new Vector2(x, 98), new Vector2(240, 152));
            ProfileIcon(panel.transform, icon, new Vector2(-82, 43), 50);
            ProfileSign(panel.transform, title, new Vector2(24, 43), new Vector2(160, 30), 25);
            amount = ProfileSign(panel.transform, "", new Vector2(0, -1), new Vector2(204, 48), 44);
            detail = ProfileText(panel.transform, "Stat detail", "", new Vector2(0, -48), new Vector2(210, 28), 18);
            return panel;
        }

        private Image ProfilePanel(Transform parent, string name, Vector2 at, Vector2 size)
        {
            var panel = QuestArt(parent, name, null, at, size, new Color(.1f, .06f, .035f));
            panel.sprite = UiTextures.RoundedRect(96, 96, 20, Color.white); panel.type = Image.Type.Sliced;
            var edge = panel.gameObject.AddComponent<Outline>();
            edge.effectColor = new Color(.85f, .62f, .25f, .55f); edge.effectDistance = new Vector2(2, -2);
            return panel;
        }

        private Text ProfileText(Transform parent, string name, string value, Vector2 at, Vector2 size, int fontSize)
        {
            var text = QuestText(parent, name, value, at, size, fontSize, SettingsCream);
            text.font = _font; text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 12; text.resizeTextMaxSize = fontSize;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private Text ProfileSign(Transform parent, string value, Vector2 at, Vector2 size, int fontSize)
        {
            var text = SettingsSign(parent, value, at, size, fontSize);
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = true; text.resizeTextMinSize = 14; text.resizeTextMaxSize = fontSize;
            return text;
        }

        private Image ProfileIcon(Transform parent, string asset, Vector2 at, float size)
        {
            var art = QuestArt(parent, asset ?? "Currency", null, at, new Vector2(size, size), Color.white);
            if (asset != null && asset.StartsWith("profile-", StringComparison.Ordinal))
            {
                if (s_profileIcons == null) s_profileIcons = Resources.LoadAll<Sprite>("UI/Hub/Hud/profile-icons");
                art.sprite = s_profileIcons.FirstOrDefault(sprite => sprite.name == asset);
            }
            else art.sprite = asset == null ? null : HudArt(asset);
            art.preserveAspect = true;
            return art;
        }

        private Button ProfileButton(Transform parent, string label, Vector2 at, Vector2 size, Action action, int fontSize = 30)
        {
            var rect = (RectTransform)parent;
            var button = CityButton(parent, label, (rect.sizeDelta.x - size.x) / 2 + at.x,
                (rect.sizeDelta.y - size.y) / 2 - at.y, size.x, size.y, action);
            HdPlaque(button.GetComponent<Image>());
            var text = button.GetComponentInChildren<Text>(); text.fontSize = fontSize;
            return button;
        }

        private void UpdateProfileWindow(float deltaTime, bool typing)
        {
            if (_profileLayer == null) return;
            if (_profileShown && _page != _profilePage) CloseProfileWindow();
            var escape = Keyboard.current != null && Keyboard.current.escapeKey.isPressed;
            // Первый Esc в поле отменяет редактирование; следующий закрывает окно.
            if (_profileShown && escape && !_profileEscapeHeld)
            {
                if (typing || _profileNameInput.isFocused)
                {
                    _profileNameInput.DeactivateInputField();
                    _profileNameInput.SetTextWithoutNotify(_profileBoundName);
                    if (UnityEngine.EventSystems.EventSystem.current != null)
                        UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
                }
                else CloseProfileWindow();
            }
            _profileEscapeHeld = escape;
            _profileFade = Mathf.MoveTowards(_profileFade, _profileShown ? 1 : 0, deltaTime / .18f);
            _profileGroup.alpha = Mathf.SmoothStep(0, 1, _profileFade);
            _profileGroup.blocksRaycasts = _profileShown;
            if (!ProfileShown)
            {
                if (_profileLayer.gameObject.activeSelf) { _profileLayer.gameObject.SetActive(false); RefreshMapButton(); }
                return;
            }
            _profileWindow.localScale = Vector3.one * (_state.Settings.ReducedMotion ? 1 : Mathf.Lerp(.94f, 1, _profileGroup.alpha));
            // Уходящая доска не перекрывает окно, которое только что открыли из профиля.
            if (_profileShown) _profileLayer.SetAsLastSibling();
            PaintProfileWindow();
        }

        private void PaintProfileWindow()
        {
            if (_state == null) return;
            var p = _state.Profile;
            _profilePortrait.sprite = PlayerProfile.Avatar;
            _profileName.text = p.DisplayName;
            _profileRank.text = HubRankName(p.Rating);
            _profileLevel.text = p.Level.ToString();
            _profileXp.text = (p.XP % 100) + " / 100 опыта · всего " + HubMoney(p.XP);
            _profileXpFill.rectTransform.sizeDelta = new Vector2(248 * Mathf.Clamp01((p.XP % 100) / 100f), 12);
            _profileXpFill.enabled = p.XP % 100 > 0;
            _profileId.text = "ID: " + p.Id;
            if (_profileBoundName != p.DisplayName && !_profileNameInput.isFocused)
            {
                // Обновления кошелька/опыта не затирают имя, которое ещё набирают.
                if (_profileNameInput.text == _profileBoundName) _profileNameInput.SetTextWithoutNotify(p.DisplayName);
                _profileBoundName = p.DisplayName;
            }
            var draft = _profileNameInput.text.Trim();
            _profileSave.interactable = !_busy && draft.Length >= 2 && draft.Length <= 24 && draft != p.DisplayName;
            _profileMatches.text = HubMoney(p.Matches);
            _profileLosses.text = "Поражений: " + HubMoney(p.Losses);
            _profileWins.text = HubMoney(p.Wins);
            _profileWinRate.text = p.Matches == 0 ? "Первая победа впереди" : Mathf.RoundToInt(p.Wins * 100f / p.Matches) + "% от сыгранных партий";
            _profileRating.text = HubMoney(p.Rating);
            _profileRatingRank.text = "Таблица игроков ›";
            _profileAchievements.text = _state.Achievements.Count(a => a.Unlocked) + " / " + _state.Achievements.Count;
            _profileProgress.text = "Пропуск: уровень " + _state.Pass.Level + "\nДрузей: " + _state.Friends.Count;
            _profileSilver.text = HubMoney(p.Coins);
            _profileGold.text = HubMoney(p.Gold);
            _profileEarned.text = "Заработано: " + HubMoney(p.CoinsEarned);
            _profileDebt.text = _state.Bank.Debt > 0 ? "Долг: " + HubMoney(_state.Bank.Debt) : "Долгов нет";
        }
    }
}
