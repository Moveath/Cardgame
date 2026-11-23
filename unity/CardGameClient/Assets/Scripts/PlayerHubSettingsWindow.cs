using System;
using System.Collections.Generic;
using CardGame.Core.Meta;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>
    /// Окно настроек в городе (2026-11-01): шестерёнка панели города открывает его поверх улицы, как доску поручений.
    /// Доска в золотой рамке, вывеска «НАСТРОЙКИ», шесть переключателей, громкость, язык, «Лаборатория» и «ЗАКРЫТЬ»;
    /// Esc и «Назад» закрывают. Арт — UI/Hub/Hud: чёткая табличка вывесок plaque-hd (доска, вывеска, кнопки), плашка монет
    /// (строки), значки set-*; переключатели вписаны в закруглённый конец строки, громкость — ползунок (<see cref="HubSlider"/>)
    /// на любое значение 0–100 и ±10. Страница «settings» разделов — прежняя.
    /// </summary>
    public sealed partial class PlayerHubController
    {
        private sealed class SettingsToggle
        {
            public Func<HubSettings, bool> Read;
            public Image Track, Knob, Art;
            public Text State;
            public float T = -1;
        }

        private static readonly (string Key, string Title, string Detail, string Icon, Func<HubSettings, bool> Read)[] SettingsRows =
        {
            ("sound", "ЗВУКИ", "Карты, действия и эффекты", "set-sound", s => s.Sound),
            ("music", "МУЗЫКА", "Фоновая музыка, когда есть", "set-music", s => s.Music),
            ("vibration", "ВИБРАЦИЯ", "На телефонах, где она есть", "set-vibration", s => s.Vibration),
            ("reduced_motion", "МЕНЬШЕ ДВИЖЕНИЯ", "Короткие переходы без полёта", "set-motion", s => s.ReducedMotion),
            ("notifications", "УВЕДОМЛЕНИЯ", "Для будущих приглашений", "set-notifications", s => s.Notifications),
            ("private_profile", "ЗАКРЫТЫЙ ПРОФИЛЬ", "Для будущего онлайна", "set-private", s => s.PrivateProfile),
        };

        private const float SettingsWindowW = 1200, SettingsWindowH = 780, SettingsRowH = 100, SettingsTrackW = 470;
        private static readonly Color SettingsCream = new Color(1f, .93f, .8f);
        private RectTransform _settingsLayer, _settingsWindow, _settingsFill, _settingsKnob;
        private CanvasGroup _settingsGroup;
        private Text _settingsVolume, _settingsRussian;
        private Button _settingsMinus, _settingsPlus;
        private readonly List<SettingsToggle> _settingsToggles = new List<SettingsToggle>();
        private bool _settingsShown, _settingsEscapeHeld;
        // Экран, поверх которого открыто окно: ушли с него — окно закрывается.
        private string _settingsPage;
        private float _settingsFade;
        // Громкость под пальцем, пока её ведут и сохраняют; −1 — показывать сохранённую.
        private int _settingsDrag = -1;
        private bool SettingsShown => _settingsShown || _settingsFade > 0;

        private void ShowSettingsWindow()
        {
            if (_state == null || _busy) return;
            if (_settingsLayer == null) BuildSettingsWindow();
            _settingsShown = true;
            _settingsPage = _page;
            _settingsLayer.gameObject.SetActive(true);
            _settingsLayer.SetAsLastSibling();
            foreach (var toggle in _settingsToggles) toggle.T = -1;
            PaintSettingsWindow(0);
            RefreshMapButton();
        }

        private void CloseSettingsWindow() => _settingsShown = false;

        private void BuildSettingsWindow()
        {
            _settingsLayer = CityWindow("Settings window", "НАСТРОЙКИ", out _settingsGroup, out _settingsWindow);

            for (var i = 0; i < SettingsRows.Length; i++)
            {
                var row = SettingsRows[i];
                var at = new Vector2(i % 2 == 0 ? -287 : 287, 255 - i / 2 * (SettingsRowH + 15));
                var plate = SettingsRow(row.Key, row.Title, row.Detail, row.Icon, at, 556);
                _settingsToggles.Add(SettingsSwitch(plate, row.Key, row.Read, 556));
            }

            var volume = SettingsRow("volume", "ГРОМКОСТЬ", null, "set-sound", new Vector2(0, -95), 1130);
            _settingsVolume = SettingsSign(volume, "80%", new Vector2(-195, 0), new Vector2(110, 60), 36);
            var track = QuestArt(volume, "Volume track", null, new Vector2(110, 0), new Vector2(SettingsTrackW, 34), Color.white);
            track.sprite = HudArt("xp-bar");
            track.type = Image.Type.Sliced;
            track.pixelsPerUnitMultiplier = 58f / 34;
            // Ползунок ловит палец на полосе выше и шире жёлоба: попасть легче, особенно на телефоне.
            var hit = QuestArt(volume, "Volume slider", null, track.rectTransform.anchoredPosition, new Vector2(SettingsTrackW + 50, 72), new Color(0, 0, 0, 0));
            hit.raycastTarget = true;
            var slider = hit.gameObject.AddComponent<HubSlider>();
            slider.Track = track.rectTransform;
            slider.Inset = 26;
            slider.Moved = SlideVolume;
            slider.Released = ReleaseVolume;
            var fill = MakeRect(track.transform, "Volume fill", 26, 10, 0, 14).gameObject.AddComponent<Image>();
            Round(fill, SettingsTrackW - 52, 14, 7);
            fill.color = new Color(1f, .8f, .25f);
            fill.raycastTarget = false;
            _settingsFill = fill.rectTransform;
            var knob = QuestArt(track.transform, "Volume knob", null, Vector2.zero, new Vector2(40, 52), Color.white);
            knob.sprite = HudArt("slider-knob") ?? HubCurrencyArt.Gold;
            knob.preserveAspect = true;
            knob.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(1, -3);
            _settingsKnob = knob.rectTransform;
            _settingsMinus = SettingsRoundSign(volume, "Тише", "−", new Vector2(410, 0),
                () => Send("settings_update", "volume", amount: Math.Max(0, _state.Settings.Volume - 10)));
            _settingsPlus = SettingsRoundSign(volume, "Громче", "+", new Vector2(485, 0),
                () => Send("settings_update", "volume", amount: Math.Min(100, _state.Settings.Volume + 10)));

            var language = SettingsRow("language", "ЯЗЫК", "Английский появится позже", "set-language", new Vector2(0, -210), 1130);
            var russian = CityButton(language, "РУССКИЙ", 1130 - 30 - 230 - 18 - 250, (SettingsRowH - 58) / 2, 250, 58,
                () => Send("settings_update", "language", "ru"));
            _settingsRussian = russian.GetComponentInChildren<Text>();
            HdPlaque(russian.GetComponent<Image>());
            var english = CityButton(language, "ENGLISH · СКОРО", 1130 - 30 - 230, (SettingsRowH - 58) / 2, 230, 58, () => { });
            HdPlaque(english.GetComponent<Image>());
            english.interactable = false;
            english.GetComponentInChildren<Text>().color = new Color(1, 1, 1, .45f);

            var bottom = SettingsWindowH - 40 - 64;
            var lab = CityButton(_settingsWindow, "ЛАБОРАТОРИЯ", 70, bottom, 290, 64, () => { CloseSettingsWindow(); Navigate("extras"); });
            HdPlaque(lab.GetComponent<Image>());
            if (HudArt("set-lab") != null)
            {
                // Колба — внутри таблички слева, надпись сдвигается вправо.
                var flask = CenteredImage(lab.transform, "Icon", 18, 6, 52, 52);
                flask.sprite = HudArt("set-lab");
                var caption = lab.GetComponentInChildren<Text>().rectTransform;
                caption.sizeDelta = new Vector2(caption.sizeDelta.x - 70, caption.sizeDelta.y);
                caption.anchoredPosition += new Vector2(64, 0);
            }
            WindowCloseButton(_settingsWindow, bottom, CloseSettingsWindow);
            _settingsLayer.gameObject.SetActive(false);
        }

        /// <summary>«ЗАКРЫТЬ» посередине нижнего ряда окна. На телефоне она крупнее соседей — растёт поровну вверх и вниз.</summary>
        private void WindowCloseButton(RectTransform board, float top, System.Action close)
        {
            var width = board.sizeDelta.x;
            var button = CityButton(board, "ЗАКРЫТЬ", (width - 280) / 2, top, 280, 64, close);
            HdPlaque(button.GetComponent<Image>());
            ((RectTransform)button.transform).anchoredPosition = new Vector2(width / 2, -top - 32);
        }

        /// <summary>
        /// Окно поверх города в стиле вывесок (настройки, задания): затемнение шире сцены ловит нажатия — город под ним
        /// не листается; доска 1200×780 — табличка вывесок крупно (тёмное поле в толстой золотой рамке 46 px с заклёпками);
        /// сверху табличка-заголовок. Чёткая табличка — plaque-hd (scripts/hud/make_plaque.py): на экране она только
        /// уменьшается; старая 160×72 мылилась. Слой создаётся скрытым.
        /// </summary>
        private RectTransform CityWindow(string name, string title, out CanvasGroup group, out RectTransform board, Vector2? size = null)
        {
            // Размер доски — общий 1200×780; касса шире (PlayerHubCashWindow.cs).
            var boardSize = size ?? new Vector2(SettingsWindowW, SettingsWindowH);
            var layer = StageLayer(name);
            group = layer.gameObject.AddComponent<CanvasGroup>();
            var center = QuestRect(layer, "Center", new Vector2(800, -450), Vector2.zero);
            center.anchorMin = center.anchorMax = new Vector2(0, 1);
            var dim = QuestArt(center, name + " dim", null, Vector2.zero, new Vector2(4800, 2400), new Color(.02f, .012f, .01f, .82f));
            dim.raycastTarget = true;
            board = QuestRect(center, name + " board", new Vector2(0, -6), boardSize);
            var image = board.gameObject.AddComponent<Image>();
            image.sprite = HudArt("plaque-hd");
            if (image.sprite == null) image.sprite = s_signPlaque != null ? s_signPlaque : s_signPlaque = Resources.Load<Sprite>("UI/Hub/sign-plaque");
            image.type = Image.Type.Sliced;
            if (image.sprite != null) image.pixelsPerUnitMultiplier = image.sprite.border.x / 46f;
            image.raycastTarget = true;
            var shadow = image.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, .6f);
            shadow.effectDistance = new Vector2(4, -8);
            HdPlaque(MapPlaque(board, title, boardSize.x / 2 - 250, -44, 500, 88).GetComponent<Image>());
            layer.gameObject.SetActive(false);
            return layer;
        }

        /// <summary>Строка настройки: деревянная плашка в золоте, значок, золотое название и пояснение под ним.</summary>
        private RectTransform SettingsRow(string key, string title, string detail, string icon, Vector2 at, float width)
        {
            var plate = QuestArt(_settingsWindow, "Setting " + key, null, at, new Vector2(width, SettingsRowH), Color.white);
            plate.sprite = HudArt("currency-plate");
            plate.type = Image.Type.Sliced;
            plate.pixelsPerUnitMultiplier = 109f / SettingsRowH;
            var art = HudArt(icon);
            var left = 52f;
            if (art != null)
            {
                var image = QuestArt(plate.transform, "Icon", null, new Vector2(-width / 2 + 72, 0), new Vector2(64, 64), Color.white);
                image.sprite = art;
                image.preserveAspect = true;
                left = 116;
            }
            // Справа — переключатель во весь закруглённый конец строки (18 px рамки + 160).
            var textWidth = key == "volume" ? 220 : width - left - 200;
            var name = SettingsSign(plate.transform, title, Vector2.zero, new Vector2(textWidth, 40), 32);
            // Буквы вывески встают по центру своей рамки (HubSignText): рамка — ровно по слову, от левого края.
            // Длинное название («ЗАКРЫТЫЙ ПРОФИЛЬ» рядом со значком) мельчает, а не заезжает под переключатель.
            if (name.preferredWidth > textWidth) name.fontSize = Mathf.FloorToInt(name.fontSize * textWidth / name.preferredWidth);
            var nameWidth = Mathf.Min(textWidth, name.preferredWidth + 4);
            name.rectTransform.sizeDelta = new Vector2(nameWidth, 40);
            name.rectTransform.anchoredPosition = new Vector2(-width / 2 + left + nameWidth / 2, detail == null ? 0 : 15);
            if (detail != null)
            {
                var line = QuestText(plate.transform, "Detail", detail, new Vector2(-width / 2 + left + textWidth / 2, -22), new Vector2(textWidth, 26), 18, SettingsCream);
                line.font = _font;
                line.alignment = TextAnchor.MiddleLeft;
                // Длинное пояснение мельчает, а не заезжает под переключатель.
                line.resizeTextForBestFit = true;
                line.resizeTextMinSize = 13;
                line.resizeTextMaxSize = 18;
                line.verticalOverflow = VerticalWrapMode.Truncate;
            }
            return plate.rectTransform;
        }

        /// <summary>Золотые буквы узким шрифтом вывесок с тёмной обводкой.</summary>
        private Text SettingsSign(Transform parent, string text, Vector2 at, Vector2 size, int fontSize)
        {
            var label = QuestText(parent, "Sign " + text, text, at, size, fontSize, SignGoldTop);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            var look = label.gameObject.AddComponent<HubSignText>();
            look.Top = SignGoldTop;
            look.Bottom = SignGoldBottom;
            var outline = label.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(.06f, .03f, 0, .9f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            return label;
        }

        /// <summary>
        /// Переключатель вписан в закруглённый правый конец строки: у плашки монет дерево внутри рамки — 67 % высоты,
        /// до золотой рамки справа 18 px (при высоте строки 100); заклёпку на конце плашки он закрывает. Картинки toggle-on/off или жёлоб с золотой кнопкой, которая едет.
        /// </summary>
        private SettingsToggle SettingsSwitch(RectTransform row, string key, Func<HubSettings, bool> read, float width)
        {
            var toggle = new SettingsToggle { Read = read };
            var size = new Vector2(160, 66);
            var rect = QuestRect(row, "Toggle " + key, new Vector2(width / 2 - 18 - size.x / 2, -1), size);
            var on = HudArt("toggle-on");
            var hit = rect.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);
            if (on != null && HudArt("toggle-off") != null)
            {
                toggle.Art = QuestArt(rect, "Switch", null, Vector2.zero, size, Color.white);
                toggle.Art.preserveAspect = true;
            }
            else
            {
                // Скругление нарисовано крупнее, чем на экране: края остаются чёткими.
                toggle.Track = QuestArt(rect, "Track", null, Vector2.zero, size, Color.white);
                toggle.Track.sprite = UiTextures.RoundedRect(256, 128, 64, Color.white);
                toggle.Track.type = Image.Type.Sliced;
                toggle.Track.pixelsPerUnitMultiplier = 64f / (size.y / 2);
                var rim = toggle.Track.gameObject.AddComponent<HubOutline>();
                rim.effectColor = new Color(0, 0, 0, .45f);
                rim.effectDistance = new Vector2(1.5f, -1.5f);
                toggle.State = QuestText(rect, "State", "", Vector2.zero, new Vector2(84, 40), 27, SettingsCream);
                toggle.Knob = QuestArt(rect, "Knob", null, Vector2.zero, new Vector2(56, 56), new Color(1f, .8f, .33f));
                toggle.Knob.sprite = UiTextures.Circle(128, Color.white);
                toggle.Knob.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(1, -2);
                var shine = QuestArt(toggle.Knob.transform, "Shine", null, new Vector2(-8, 10), new Vector2(26, 18), new Color(1, 1, .9f, .5f));
                shine.sprite = UiTextures.Circle(64, Color.white);
            }
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => { if (_state != null) Send("settings_update", key, flag: !read(_state.Settings)); });
            rect.gameObject.AddComponent<HubPress>();
            return toggle;
        }

        /// <summary>Круглая деревянная кнопка с золотым знаком (− и + громкости).</summary>
        private Button SettingsRoundSign(RectTransform row, string name, string sign, Vector2 at, Action action)
        {
            var rect = QuestRect(row, name, at, new Vector2(64, 64));
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = HudArt("button-wood");
            image.preserveAspect = true;
            var label = SettingsSign(rect, sign, new Vector2(0, 3), new Vector2(64, 64), 46);
            label.font = _font;
            label.fontStyle = FontStyle.Bold;
            return HudButton(image, action);
        }

        /// <summary>Табличка вывесок в чётком виде (Hud/plaque-hd): рамка — та же доля высоты, что у <see cref="CityButton"/>.</summary>
        private static void HdPlaque(Image image)
        {
            var hd = HudArt("plaque-hd");
            if (hd == null || image == null) return;
            image.sprite = hd;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = hd.border.y / (image.rectTransform.sizeDelta.y * .3f);
        }

        /// <summary>Громкость под пальцем: любое целое 0–100, пока ведут — только на экране.</summary>
        private void SlideVolume(float share) => _settingsDrag = Mathf.RoundToInt(Mathf.Clamp01(share) * 100);

        /// <summary>Палец отпущен: сохранить, что под ним, и снова показывать сохранённое.</summary>
        private async void ReleaseVolume(float share)
        {
            SlideVolume(share);
            var value = _settingsDrag;
            if (_state != null && value != _state.Settings.Volume) await Dispatch("settings_update", "volume", amount: value);
            if (this != null) _settingsDrag = -1;
        }

        /// <summary>Каждый кадр: Esc, проявление и положение переключателей и громкости по сохранённым настройкам.</summary>
        private void UpdateSettingsWindow(float deltaTime, bool typing)
        {
            if (_settingsLayer == null) return;
            if (_settingsShown && _page != _settingsPage) _settingsShown = false;
            var escape = !typing && Keyboard.current != null && Keyboard.current.escapeKey.isPressed;
            if (_settingsShown && escape && !_settingsEscapeHeld) CloseSettingsWindow();
            _settingsEscapeHeld = escape;
            _settingsFade = Mathf.MoveTowards(_settingsFade, _settingsShown ? 1 : 0, deltaTime / .18f);
            _settingsGroup.alpha = Mathf.SmoothStep(0, 1, _settingsFade);
            _settingsGroup.blocksRaycasts = _settingsShown;
            if (!SettingsShown)
            {
                // Кнопка карты обновляется не каждый кадр: вернуть её, как только окно погасло.
                if (_settingsLayer.gameObject.activeSelf) { _settingsLayer.gameObject.SetActive(false); RefreshMapButton(); }
                return;
            }
            // Доска чуть «подлетает» при появлении; у «меньше движения» — только проявление.
            var reduced = _state != null && _state.Settings.ReducedMotion;
            _settingsWindow.localScale = Vector3.one * (reduced ? 1 : Mathf.Lerp(.94f, 1, _settingsGroup.alpha));
            _settingsLayer.SetAsLastSibling();
            PaintSettingsWindow(deltaTime);
        }

        private void PaintSettingsWindow(float deltaTime)
        {
            if (_state == null) return;
            var settings = _state.Settings;
            var on = HudArt("toggle-on");
            var off = HudArt("toggle-off");
            foreach (var toggle in _settingsToggles)
            {
                var enabled = toggle.Read(settings);
                toggle.T = toggle.T < 0 ? (enabled ? 1 : 0) : Mathf.MoveTowards(toggle.T, enabled ? 1 : 0, deltaTime / .15f);
                if (toggle.Art != null) { toggle.Art.sprite = enabled ? on : off; continue; }
                toggle.Track.color = Color.Lerp(new Color(.45f, .1f, .08f), new Color(.16f, .5f, .2f), toggle.T);
                toggle.Knob.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(-47, 47, Mathf.SmoothStep(0, 1, toggle.T)), 0);
                toggle.State.text = enabled ? "ВКЛ" : "ВЫКЛ";
                toggle.State.rectTransform.anchoredPosition = new Vector2(enabled ? -24 : 24, 0);
            }
            var volume = Mathf.Clamp(_settingsDrag >= 0 ? _settingsDrag : settings.Volume, 0, 100);
            _settingsVolume.text = volume + "%";
            var inner = SettingsTrackW - 52;
            _settingsFill.sizeDelta = new Vector2(inner * volume / 100f, 14);
            _settingsFill.GetComponent<Image>().enabled = volume > 0;
            _settingsKnob.anchoredPosition = new Vector2(-inner / 2 + inner * volume / 100f, 2);
            _settingsMinus.interactable = volume > 0;
            _settingsPlus.interactable = volume < 100;
            _settingsRussian.text = settings.Language == "ru" ? "РУССКИЙ ✓" : "РУССКИЙ";
        }
    }
}
