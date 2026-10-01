using System;
using System.Linq;
using CardGame.Core.Meta;
using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    public sealed partial class PlayerHubController
    {
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

        private void BuildHubFriends()
        {
            Title("Друзья", "Локальная демонстрация: участники и ответы имитируются на этом устройстве");
            HubTabs(new[] { "Мои друзья", "Найти игроков", "Заявки", "Приглашения" }, new[] { "friends", "people", "requests", "invites" }, _hubFriendsTab,
                key => { _hubFriendsTab = key; _hubListPages["friends"] = 0; Navigate("friends"); });
            if (_hubFriendsTab == "requests") { BuildHubRequests(); return; }
            if (_hubFriendsTab == "invites") { BuildHubInvites(); return; }
            var search = Input(_hubFriendSearch, 0, 55, 760);
            Button("Найти", 780, 55, 170, 42, () => { _hubFriendSearch = search.text.Trim(); _hubListPages["friends"] = 0; Navigate("friends"); });
            Button("Сбросить", 970, 55, 190, 42, () => { _hubFriendSearch = ""; Navigate("friends"); });
            var people = _state.People.Where(person => (_hubFriendsTab != "friends" || _state.Friends.Contains(person.Id))
                && (string.IsNullOrWhiteSpace(_hubFriendSearch) || person.Name.IndexOf(_hubFriendSearch, StringComparison.OrdinalIgnoreCase) >= 0
                    || person.Id.IndexOf(_hubFriendSearch, StringComparison.OrdinalIgnoreCase) >= 0)).ToArray();
            var start = HubPageStart("friends", people.Length, 5);
            if (people.Length == 0) HubEmpty("Пока никого нет", "Открой «Найти игроков», чтобы отправить локальную заявку.", 124);
            for (var i = start; i < Math.Min(start + 5, people.Length); i++)
            {
                var person = people[i]; var y = 113 + (i - start) * 95;
                Panel("Friend " + person.Id, 0, y, 1160, 83);
                HubPortrait(AvatarCatalog.Find(person.AvatarId), 12, y + 7, 62, 70);
                Label(person.Name, 95, y + 9, 450, 30, 23);
                Label("Ур. " + person.Level + " · " + person.Rating + " · " + person.Status, 95, y + 44, 470, 27, 17).color = HubPageMuted;
                Button("Профиль", 565, y + 20, 150, 42, () => Navigate("friend:" + person.Id));
                var friend = _state.Friends.Contains(person.Id);
                var pending = _state.FriendRequests.Any(request => request.Status == "pending"
                    && ((request.FromId == _state.Profile.Id && request.ToId == person.Id) || (request.ToId == _state.Profile.Id && request.FromId == person.Id)));
                Button(friend ? "Сообщения" : pending ? "Открыть заявки" : "Добавить", 735, y + 20, 190, 42,
                    () => { if (friend) Navigate("chat:" + person.Id); else if (pending) { _hubFriendsTab = "requests"; Navigate("friends"); } else Send("friend_request", person.Id); });
                Button("Пригласить", 945, y + 20, 198, 42,
                    () => Send("invite_friend", person.Id, _state.CurrentRoomId), friend && !string.IsNullOrEmpty(_state.CurrentRoomId));
            }
            if (string.IsNullOrEmpty(_state.CurrentRoomId))
                Label("Для приглашения друга сначала создай комнату или войди в неё.", 0, 582, 1160, 26, 18).color = HubPageMuted;
        }

        private void BuildHubRequests()
        {
            var requests = _state.FriendRequests.Where(request => request.Status == "pending").ToArray();
            var start = HubPageStart("requests", requests.Length, 5);
            if (requests.Length == 0) HubEmpty("Новых заявок нет", "Найди демо-игрока и отправь запрос. Входящие заявки и ответы сохраняются локально.", 65);
            for (var i = start; i < Math.Min(start + 5, requests.Length); i++)
            {
                var request = requests[i]; var incoming = request.ToId == _state.Profile.Id;
                var id = incoming ? request.FromId : request.ToId;
                var person = _state.People.FirstOrDefault(entry => entry.Id == id);
                var y = 65 + (i - start) * 104;
                Panel("Request " + request.Id, 0, y, 1160, 92);
                Label(person == null ? id : person.Name, 20, y + 12, 580, 30, 24);
                Label(incoming ? "Входящая локальная заявка" : "Исходящая · можно имитировать ответ", 20, y + 48, 580, 28, 18).color = HubPageMuted;
                Button(incoming ? "Принять" : "Демо: принимает", 620, y + 25, 245, 42,
                    () => Send(incoming ? "friend_accept" : "friend_mock_reply", request.Id, flag: true));
                Button(incoming ? "Отклонить" : "Демо: отклоняет", 887, y + 25, 250, 42,
                    () => Send(incoming ? "friend_decline" : "friend_mock_reply", request.Id, flag: false));
            }
        }

        private void BuildHubPerson(string id)
        {
            var person = _state.People.FirstOrDefault(entry => entry.Id == id);
            if (person == null) { Title("Игрок не найден"); Button("К друзьям", 0, 30, 240, 48, () => Navigate("friends")); return; }
            Title(person.Name, "Профиль демо-участника · " + person.Status);
            Panel("Person", 0, 0, 1160, 310);
            HubPortrait(AvatarCatalog.Find(person.AvatarId), 35, 24, 210, 240);
            Label("Уровень " + person.Level, 300, 45, 780, 40, 30).color = HubPageGold;
            Label(HubRankName(person.Rating) + " · " + HubMoney(person.Rating) + " рейтинга", 300, 103, 780, 40, 26);
            Label("ID: " + person.Id + "\n" + (person.IsBot ? "Локальный бот. Другой человек не получает эти действия." : "Демонстрационный профиль."), 300, 163, 800, 86, 20).color = HubPageMuted;
            var friend = _state.Friends.Contains(id);
            Button(friend ? "Удалить из друзей" : "Добавить в друзья", 0, 340, 310, 50,
                () => Send(friend ? "friend_remove" : "friend_request", id));
            Button("Переписка", 335, 340, 260, 50, () => Navigate("chat:" + id), friend);
            Button("В мою комнату", 620, 340, 295, 50, () => Send("invite_friend", id, _state.CurrentRoomId), friend && !string.IsNullOrEmpty(_state.CurrentRoomId));
            Button("К списку друзей", 0, 418, 310, 48, () => Navigate("friends"));
        }

        private void BuildHubInvites()
        {
            var invitations = _state.Invites.Where(invite => invite.Status == "pending").ToArray();
            var start = HubPageStart("invitations", invitations.Length, 5);
            if (invitations.Length == 0)
                HubEmpty("Приглашений пока нет", "Создай комнату и пригласи друга. Здесь можно принять входящее приглашение или проверить ответ локального бота.", 70);
            for (var i = start; i < Math.Min(start + 5, invitations.Length); i++)
            {
                var invitation = invitations[i];
                var incoming = invitation.ToId == _state.Profile.Id;
                var id = incoming ? invitation.FromId : invitation.ToId;
                var person = _state.People.FirstOrDefault(entry => entry.Id == id);
                var room = _state.Rooms.FirstOrDefault(entry => entry.Id == invitation.RoomId);
                var available = room != null && !room.Started && room.Members.Count < room.MaxPlayers
                    && !room.Members.Any(member => member.PlayerId == invitation.ToId);
                var y = 65 + (i - start) * 105;
                Panel("Invitation " + invitation.Id, 0, y, 1160, 94);
                Label((incoming ? "Тебя приглашает " : "Приглашение для ") + (person == null ? id : person.Name), 18, y + 9, 655, 32, 23).color = HubPageGold;
                Label(room == null ? "Комната уже недоступна" : room.Name + " · " + HubGameName(room.GameId)
                    + " · " + room.Members.Count + "/" + room.MaxPlayers, 18, y + 47, 655, 35, 18).color = HubPageMuted;
                Button(available ? incoming ? "Принять" : "Демо: принял" : "Недоступно", 710, y + 25, 210, 44,
                    () => Send("invite_accept", invitation.Id), available);
                Button(incoming ? "Отклонить" : "Демо: отказ", 940, y + 25, 202, 44,
                    () => Send("invite_decline", invitation.Id));
            }
        }

        private void BuildHubChat(string id)
        {
            var person = _state.People.FirstOrDefault(entry => entry.Id == id);
            Title("Переписка · " + (person == null ? id : person.Name), "Локальный чат. Сообщения не уходят другим людям.");
            if (!_state.Friends.Contains(id))
            {
                HubEmpty("Сначала добавьте друг друга", "Переписка станет доступна после принятия локальной заявки.", 30);
                Button("К профилю", 0, 225, 280, 48, () => Navigate("friend:" + id));
                return;
            }
            var messages = _state.Chats.Where(message => message.ConversationId == id).ToArray();
            var pageKey = "chat:" + id;
            if (!_hubListPages.ContainsKey(pageKey)) _hubListPages[pageKey] = Math.Max(0, (messages.Length - 1) / 6);
            var start = HubPageStart(pageKey, messages.Length, 6);
            for (var i = start; i < Math.Min(start + 6, messages.Length); i++)
            {
                var message = messages[i]; var y = (i - start) * 77;
                Panel("Message " + message.Id, 0, y, 1160, 69);
                var sender = message.FromId == _state.Profile.Id ? "Ты" : person == null ? message.FromId : person.Name;
                Label(sender + (message.IsLocalMock ? " · демо" : ""), 16, y + 5, 190, 26, 17).color = HubPageGold;
                var preview = message.Text.Replace('\n', ' ').Replace('\r', ' ');
                if (preview.Length > 100) preview = preview.Substring(0, 100) + "…";
                Label(preview, 220, y + 8, 700, 55, 20);
                Button("Открыть", 950, y + 15, 190, 39, () => Navigate("message:" + message.Id));
            }
            if (messages.Length == 0) HubEmpty("Начни разговор", "Ответ собеседника будет демонстрацией, сохранённой только здесь.", 25);
            var input = Input("", 0, 488, 905);
            input.characterLimit = 400;
            Button("Отправить", 930, 488, 230, 42, () => { _hubListPages.Remove(pageKey); Send("chat_send", id, input.text); });
            Button("К профилю", 0, 554, 250, 44, () => Navigate("friend:" + id));
        }

        private void BuildHubMessage(string id)
        {
            var message = _state.Chats.FirstOrDefault(entry => entry.Id == id);
            if (message == null)
            {
                Title("Сообщение недоступно");
                HubEmpty("Сообщение не найдено", "Старая запись могла выйти за пределы локальной истории.", 0);
                Button("К друзьям", 0, 190, 280, 48, () => Navigate("friends"));
                return;
            }
            var person = _state.People.FirstOrDefault(entry => entry.Id == message.FromId);
            var sender = message.FromId == _state.Profile.Id ? "Ты" : person == null ? message.FromId : person.Name;
            Title("Сообщение · " + sender, HubDate(message.CreatedAt) + (message.IsLocalMock ? " · локальный чат" : ""));
            Panel("Full message", 0, 0, 1160, 510);
            var viewport = MakeRect(_content, "Message viewport", 26, 24, 1108, 458);
            viewport.gameObject.AddComponent<RectMask2D>();
            var hitArea = viewport.gameObject.AddComponent<Image>();
            hitArea.color = Color.clear;
            var body = MakeRect(viewport, "Scrollable message", 0, 0, 1088, 458);
            var text = MakeLabel(body, message.Text, 0, 0, 1088, 458, 25);
            var height = Mathf.Max(458, text.preferredHeight + 12);
            body.sizeDelta = new Vector2(1088, height);
            text.rectTransform.sizeDelta = new Vector2(1088, height);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = body;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30;
            if (height > 458) Label("Прокрути текст колёсиком или перетаскиванием.", 0, 519, 1100, 25, 18).color = HubPageMuted;
            Button("Назад в переписку", 0, 549, 380, 52, Back);
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
