using System;
using UnityEngine;

namespace CardGameClient
{
    public sealed partial class PlayerHubController
    {
        private string _hubArenaChoice = "Без модификатора";
        private string _hubVoiceChoice = "Обычный";
        private string _hubEmotionChoice = "Радость";

        private void BuildHubSettings()
        {
            var settings = _state.Settings;
            Title("Настройки", "Предпочтения сохраняются в локальном профиле");
            HubSetting("Звуки", "Карты, действия и эффекты", "sound", settings.Sound, 0, 0);
            HubSetting("Музыка", "Фоновая музыка, когда доступна", "music", settings.Music, 590, 0);
            HubSetting("Вибрация", "На поддерживаемых устройствах", "vibration", settings.Vibration, 0, 125);
            HubSetting("Меньше движения", "Предпочтение для эффектов интерфейса", "reduced_motion", settings.ReducedMotion, 590, 125);
            HubSetting("Уведомления", "Предпочтение для будущих приглашений", "notifications", settings.Notifications, 0, 250);
            HubSetting("Закрытый профиль", "Настройка приватности для будущего онлайна", "private_profile", settings.PrivateProfile, 590, 250);
            Panel("Volume", 0, 387, 1160, 105);
            Label("Громкость: " + settings.Volume + "%", 20, 400, 510, 38, 26).color = HubPageGold;
            Progress(settings.Volume, 100, 20, 456, 740, 14);
            Button("− 10", 815, 417, 148, 45, () => Send("settings_update", "volume", amount: Math.Max(0, settings.Volume - 10)), settings.Volume > 0);
            Button("+ 10", 982, 417, 158, 45, () => Send("settings_update", "volume", amount: Math.Min(100, settings.Volume + 10)), settings.Volume < 100);
            Panel("Language", 0, 514, 1160, 89);
            Label("Язык интерфейса", 20, 527, 480, 31, 24).color = HubPageGold;
            Label("Доступен русский. Перевод на английский появится позже.", 20, 565, 765, 26, 17).color = HubPageMuted;
            Button(settings.Language == "ru" ? "Русский ✓" : "Русский", 810, 537, 153, 42, () => Send("settings_update", "language", "ru"));
            Button("Скоро: EN", 982, 537, 158, 42, () => { }, false);
            Button("Лаборатория · будущие функции", 0, 615, 500, 44, () => Navigate("extras"));
        }

        private void HubSetting(string title, string detail, string key, bool enabled, float x, float y)
        {
            Panel("Setting " + key, x, y, 570, 109);
            Label(title, x + 18, y + 11, 365, 35, 25).color = HubPageGold;
            Label(detail, x + 18, y + 56, 365, 44, 17).color = HubPageMuted;
            Button(enabled ? "Вкл." : "Выкл.", x + 405, y + 33, 146, 44, () => Send("settings_update", key, flag: !enabled));
        }

        private void BuildHubExtras()
        {
            Title("Лаборатория", "Макеты будущих функций. Новые режимы, голосовая связь и генерация фото пока не подключены.");
            HubTabs(new[] { "Арена", "Голос", "Аватар по фото", "Эмоции" }, new[] { "arena", "voice", "photo", "emotions" }, _hubExtra,
                key => { _hubExtra = key; Navigate("extras"); });
            if (_hubExtra == "arena")
            {
                HubPanelText("Arena preview", "Арена · подготовка перед партией", "Здесь можно примерить структуру выбора модификатора. Это макет: он не меняет правила существующих игр.", 0, 70, 1160, 150);
                var options = new[] { "Без модификатора", "Особая раздача", "Набор спецкарт" };
                for (var i = 0; i < options.Length; i++)
                {
                    var option = options[i];
                    Button(option, i * 395, 250, 370, 55, () => { _hubArenaChoice = option; Navigate("extras"); });
                }
                Label("В макете выбрано: " + _hubArenaChoice, 0, 346, 1160, 43, 28).color = HubPageGold;
                Label("Будущий режим объединит условия партии, классы и отдельный арсенал. Обычные карточные столы сохраняют свои правила.", 0, 414, 1160, 98, 24).color = HubPageMuted;
                Button("К обычным играм", 0, 560, 360, 48, () => Navigate("games"));
            }
            else if (_hubExtra == "voice")
            {
                HubPanelText("Voice preview", "Голос за столом", "Микрофон не включается, звук никуда не передаётся. Ниже — только предварительный выбор общего характера голоса.", 0, 70, 1160, 160);
                var options = new[] { "Обычный", "Робот", "Мультяшный" };
                for (var i = 0; i < options.Length; i++)
                {
                    var option = options[i];
                    Button(option, i * 395, 265, 370, 55, () => { _hubVoiceChoice = option; Navigate("extras"); });
                }
                Label("Выбор в макете: " + _hubVoiceChoice, 0, 361, 1160, 45, 29).color = HubPageGold;
                Label("Для живых игроков появятся включение микрофона, заглушение собеседника и общие голосовые фильтры.", 0, 429, 1160, 94, 23).color = HubPageMuted;
                Button("Настройки звука", 0, 560, 360, 48, () => Navigate("settings"));
            }
            else if (_hubExtra == "photo")
            {
                HubPanelText("Photo avatar preview", "Твой персонаж по фотографии", "Будущий путь: выбрать фото → посмотреть результат → подтвердить аватар. Сейчас фотографии не загружаются и не отправляются.", 0, 70, 1160, 160);
                HubPanelText("Photo step 1", "1 · Исходное фото", "Выбор снимка и согласие перед отправкой", 0, 265, 370, 168);
                HubPanelText("Photo step 2", "2 · Примерка", "Просмотр мультяшного результата и повтор", 395, 265, 370, 168);
                HubPanelText("Photo step 3", "3 · В профиль", "Применение образа и удаление исходника", 790, 265, 370, 168);
                Button("Выбрать готового персонажа", 0, 486, 540, 52, OpenAvatarPicker);
                Button("Открыть мой профиль", 566, 486, 430, 52, () => Navigate("profile"));
            }
            else
            {
                HubPanelText("Emotions preview", "Эмоции над аватаром", "Сначала — понятные реакции. Они не должны давать преимущество и менять ход партии.", 0, 70, 1160, 150);
                var options = new[] { "Радость", "Удивление", "Возмущение", "Победа" };
                for (var i = 0; i < options.Length; i++)
                {
                    var option = options[i];
                    Button(option, i * 295, 255, 275, 55, () => { _hubEmotionChoice = option; Navigate("extras"); });
                }
                Panel("Emotion bubble", 270, 358, 620, 93);
                Label(_hubEmotionChoice + "!", 295, 382, 570, 47, 34).color = HubPageGold;
                Button("Предметы для броска", 0, 540, 410, 51, () => Navigate("inventory"));
            }
        }
    }
}
