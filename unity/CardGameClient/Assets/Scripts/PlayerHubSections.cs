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

        private void BuildHubShopFront()
        {
            Title("Магазин", "Выбери отдел. Пока предметы открываются пробно, без оплаты.");
            HubShopTile("Персонаж и костюмы", "Аватары, одежда, головные уборы и внешность", "character", 0, 12);
            HubShopTile("Стол и карты", "Сукно, рубашки, сундучки и дорожки", "table", 590, 12);
            HubShopTile("Рамки профиля", "Выдели свой портрет", "frame", 0, 247);
            HubShopTile("Предметы и эмоции", "Реакции за столом и броски помидоров", "consumable", 590, 247);
            Button("Весь каталог", 0, 498, 360, 52, () => OpenHubShopCategory("all"));
            Button("Моя коллекция", 388, 498, 360, 52, () => Navigate("inventory"));
            HubCurrencyLegend(HubCurrencyArt.Silver, HubCurrencyArt.SilverName, "Основная валюта · почти все покупки", 0);
            HubCurrencyLegend(HubCurrencyArt.Gold, HubCurrencyArt.GoldName, "Более редкие и ценные", 590);
        }

        private void HubCurrencyLegend(Sprite sprite, string name, string detail, float x)
        {
            Panel("Currency " + name, x, 566, 570, 85);
            HubPortrait(sprite, x + 13, 573, 56, 72);
            Label(name, x + 88, 576, 464, 31, 21).color = HubPageGold;
            Label(detail, x + 88, 616, 464, 26, 18).color = HubPageMuted;
        }

        private void HubShopTile(string title, string detail, string category, float x, float y)
        {
            var items = _state.Catalog.Where(item => HubCategoryMatches(item.Category, category)).ToArray();
            var owned = items.Count(item => HubOwned(item.Id));
            HubSectionTile(title, detail, items.Length + " предметов · в коллекции " + owned, x, y, "shop-catalog",
                () => OpenHubShopCategory(category), "shop-" + category);
        }

        private void OpenHubShopCategory(string category)
        {
            _hubShopCategory = category;
            _hubListPages["shop"] = 0;
            Navigate("shop-catalog");
        }

        private static string HubShopSectionName(string category)
        {
            switch (category)
            {
                case "character": return "Персонаж и костюмы";
                case "table": return "Стол и карты";
                case "frame": return "Рамки профиля";
                case "consumable": return "Предметы и эмоции";
                default: return "Весь каталог";
            }
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

        private void BuildHubChats()
        {
            Title("Чаты", "Личные разговоры с друзьями · пока сообщения сохраняются только на этом устройстве");
            Button("Найти друзей", 0, 0, 300, 48, () => { _hubFriendsTab = "people"; Navigate("friends"); });
            Label(_state.Friends.Count + " друзей", 330, 13, 600, 35, 21).color = HubPageMuted;
            var conversations = _state.People.Where(person => _state.Friends.Contains(person.Id))
                .Select(person => new
                {
                    Person = person,
                    Last = _state.Chats.Where(message => message.ConversationId == person.Id).OrderByDescending(message => message.CreatedAt).FirstOrDefault()
                })
                .OrderByDescending(item => item.Last == null ? 0 : item.Last.CreatedAt).ThenBy(item => item.Person.Name).ToArray();
            if (conversations.Length == 0)
            {
                HubEmpty("Твои разговоры появятся здесь", "Добавь друга, чтобы написать ему или договориться о следующей партии.", 93);
                return;
            }
            var start = HubPageStart("chats", conversations.Length, 5);
            for (var i = start; i < Math.Min(start + 5, conversations.Length); i++)
            {
                var conversation = conversations[i]; var person = conversation.Person;
                var y = 73 + (i - start) * 102;
                Panel("Conversation " + person.Id, 0, y, 1160, 90);
                HubPortrait(AvatarCatalog.Find(person.AvatarId), 16, y + 9, 64, 73);
                Label(person.Name, 104, y + 9, 720, 31, 25).color = HubPageGold;
                var preview = conversation.Last == null ? "Начать разговор" : conversation.Last.Text.Replace('\n', ' ').Replace('\r', ' ');
                if (preview.Length > 72) preview = preview.Substring(0, 72) + "…";
                Label(preview, 104, y + 47, 775, 33, 19).color = HubPageMuted;
                Button("Открыть чат", 917, y + 22, 223, 46, () => Navigate("chat:" + person.Id));
            }
        }
    }
}
