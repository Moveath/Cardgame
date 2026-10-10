using System;
using System.Linq;
using CardGame.Core.Meta;
using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    public sealed partial class PlayerHubController
    {
        // Закладка окна друзей таверны: friends, requests или people (PlayerHubTavernFriends.cs).
        private string _hubFriendsTab = "friends";

        private void BuildHubProfile()
        {
            var profile = _state.Profile;
            Title("Мой профиль", "Локальный профиль на этом устройстве");
            Panel("Profile", 0, 0, 1160, 250);
            HubPortrait(AvatarCatalog.Find(profile.AvatarName), 20, 12, 180, 210);
            Label("Имя игрока", 232, 18, 500, 28, 20).color = HubPageMuted;
            var name = Input(profile.DisplayName, 232, 54, 460);
            name.characterLimit = 24;
            Button("Сохранить имя", 710, 54, 220, 42,
                () => Send("profile_update", profile.AvatarName, name.text));
            Label("ID: " + profile.Id, 232, 112, 700, 30, 18).color = HubPageMuted;
            Label("Уровень " + profile.Level + "  ·  " + HubMoney(profile.XP) + " опыта  ·  " + HubRankName(profile.Rating),
                232, 151, 860, 34, 23).color = HubPageGold;
            Button("Выбрать аватар", 232, 196, 245, 40, OpenAvatarPicker);
            Button("Образ и предметы", 498, 196, 270, 40, () => Navigate("inventory"));
            HubPanelText("Statistics", "Статистика", "Партий: " + profile.Matches + "\nПобед: " + profile.Wins + "   Поражений: " + profile.Losses
                + "\nРейтинг: " + HubMoney(profile.Rating), 0, 272, 368, 175);
            HubPanelText("Balance", "Тун-тун коины", HubMoney(profile.Coins) + " на счету\n" + HubMoney(profile.CoinsEarned)
                + " заработано за всё время\n" + (_state.Bank.Debt > 0 ? "Должник · осталось " + HubMoney(_state.Bank.Debt) : "Долгов нет"), 394, 272, 372, 175);
            HubPanelText("Progress", "Мои достижения", _state.Achievements.Count(a => a.Unlocked) + " / " + _state.Achievements.Count
                + " открыто\nПропуск: уровень " + _state.Pass.Level + "\nДрузей: " + _state.Friends.Count, 792, 272, 368, 175);
            Button("Достижения", 0, 473, 270, 48, () => Navigate("achievements"));
            Button("Рейтинг", 295, 473, 270, 48, () => Navigate("ranks"));
            Button("Банк", 590, 473, 270, 48, () => Navigate("bank"));
            Button("Настройки", 885, 473, 275, 48, () => Navigate("settings"));
            Label("Коины здесь виртуальные. Покерные фишки и кошелёк профиля — отдельные балансы.", 0, 555, 1160, 64, 20).color = HubPageMuted;
        }

        private void BuildHubRanks()
        {
            Title("Рейтинг", "Локальная таблица: твои результаты и демонстрационные соперники");
            var entries = _state.People.Select(person => new HubPerson { Id = person.Id, Name = person.Name, Rating = person.Rating, Level = person.Level }).ToList();
            entries.Add(new HubPerson { Id = _state.Profile.Id, Name = _state.Profile.DisplayName + " · ты", Rating = _state.Profile.Rating, Level = _state.Profile.Level });
            var sorted = entries.OrderByDescending(person => person.Rating).ThenBy(person => person.Name).ToArray();
            var start = HubPageStart("ranks", sorted.Length, 7);
            Label("Место", 18, 4, 100, 32, 19).color = HubPageMuted;
            Label("Игрок", 150, 4, 440, 32, 19).color = HubPageMuted;
            Label("Ранг", 650, 4, 250, 32, 19).color = HubPageMuted;
            Label("Рейтинг", 990, 4, 150, 32, 19).color = HubPageMuted;
            for (var i = start; i < Math.Min(start + 7, sorted.Length); i++)
            {
                var person = sorted[i]; var y = 49 + (i - start) * 72;
                Panel("Rank " + i, 0, y, 1160, 61);
                Label((i + 1).ToString("00"), 18, y + 12, 110, 35, 25).color = HubPageGold;
                Button(person.Name, 140, y + 9, 480, 43, () => Navigate(person.Id == _state.Profile.Id ? "profile" : "friend:" + person.Id));
                Label(HubRankName(person.Rating), 650, y + 15, 300, 35, 22);
                Label(HubMoney(person.Rating), 990, y + 15, 150, 35, 24).color = HubPageGold;
            }
            Label("Бронза < 1 200 · Серебро 1 200 · Золото 1 500 · Платина 1 800 · Алмаз 2 200", 0, 563, 1160, 35, 18).color = HubPageMuted;
        }

        private static string HubRankName(int rating) => rating >= 2200 ? "Алмаз" : rating >= 1800 ? "Платина" : rating >= 1500 ? "Золото" : rating >= 1200 ? "Серебро" : "Бронза";
    }
}
