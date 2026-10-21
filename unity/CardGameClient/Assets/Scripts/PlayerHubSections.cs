using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    public sealed partial class PlayerHubController
    {
        private void BuildHubProgress()
        {
            Title("Мой прогресс", "Задания, коллекция достижений и награды за игру");
            Panel("Progress overview", 0, 0, 1160, 88);
            Label("Уровень " + _state.Profile.Level, 24, 16, 290, 35, 28).color = HubPageGold;
            Label(HubMoney(_state.Profile.XP) + " опыта", 24, 52, 290, 27, 18).color = HubPageMuted;
            Progress(_state.Profile.XP % 100, 100, 340, 37, 780, 17);

            var questsReady = _state.Quests.Count(item => !item.Claimed && item.Progress >= item.Goal);
            var achievementsReady = _state.Achievements.Count(item => item.Unlocked && !item.Claimed);
            HubSectionTile("Задания", "На сегодня и на неделю", questsReady > 0 ? "Наград к получению: " + questsReady : "Выбирай цель для следующей партии",
                0, 111, "quests");
            HubSectionTile("Достижения", "Особые моменты за столом", "Открыто " + _state.Achievements.Count(item => item.Unlocked) + " / " + _state.Achievements.Count
                + (achievementsReady > 0 ? " · наград: " + achievementsReady : ""), 590, 111, "achievements");
            HubSectionTile("Брейнрот пасс", _state.Pass.Name, "Уровень " + _state.Pass.Level + " · две дорожки наград", 0, 346, "pass");
            HubSectionTile("Ранги", "Твоё место среди игроков", HubRankName(_state.Profile.Rating) + " · " + HubMoney(_state.Profile.Rating) + " рейтинга", 590, 346, "ranks");
            Button("Обучение с Шестёркой", 0, 587, 420, 52, () => Navigate("tutorial"));
            Label("Правила всех игр всегда можно повторить.", 449, 601, 710, 35, 20).color = HubPageMuted;
        }

        /// <summary>Плитка раздела в его цвете: значок, описание, состояние и кнопка перехода того же цвета.</summary>
        private void HubSectionTile(string title, string detail, string status, float x, float y, string route, Action action = null,
            string section = null)
        {
            var accent = Accent(section ?? SectionOf(route).Key);
            var tile = Panel("Section " + route + " " + title, x, y, 570, 215);
            tile.GetComponent<Image>().color = Tint(accent, .6f);
            var disc = MakeRect(tile, "Icon disc", 570 - 84, 18, 64, 64).gameObject.AddComponent<Image>();
            disc.sprite = UiTextures.Circle(128, Color.white);
            disc.color = accent;
            disc.raycastTarget = false;
            var icon = MakeRect(disc.transform, "Icon", 12, 12, 40, 40).gameObject.AddComponent<Image>();
            icon.sprite = Icon(SectionOf(route).Icon);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            Label(detail, x + 22, y + 20, 450, 70, 23).color = Color.white;
            Label(status, x + 22, y + 97, 526, 35, 20).color = HubPageGold;
            Paint(Button(title, x + 20, y + 145, 530, 50, action ?? (() => Navigate(route))), accent);
        }
    }
}
