using System;
using System.Linq;

namespace CardGame.Core.Meta
{
    internal static class HubSeed
    {
        internal static HubSnapshot Create(long now)
        {
            var s = new HubSnapshot { UpdatedAt = now };
            s.Profile.JoinedAt = now;
            s.Profile.Coins = s.Profile.CoinsEarned = 300;
            s.IncomeDays.Add(new HubIncomeDay { Day = now / 86400, Gross = 300 });
            var ids = new[] { "ali", "aslan", "danil", "marat", "yegor", "yelnur" };
            var names = new[] { "Али", "Аслан", "Данил", "Марат", "Егор", "Ельнур" };
            for (var i = 0; i < ids.Length; i++)
            {
                s.People.Add(new HubPerson { Id = "bot-" + ids[i], Name = names[i], AvatarId = ids[i] + "_01", Level = 3 + i, Rating = 980 + i * 65 });
                Item(s, ids[i] + "_01", names[i], "avatar", "Базовый персонаж", "#D9B765", true, "PlayerAvatars/" + ids[i] + "/" + ids[i] + "_01");
            }
            for (var i = 1; i <= 12; i++)
                Item(s, "avatar_" + i.ToString("00"), "Персонаж " + i, "avatar", "Базовый персонаж", "#D9B765", true, "Avatars/avatar_" + i.ToString("00"));
            Item(s, "frame_default", "Простая рамка", "frame", "Базовая рамка", "#89968E", true);
            Item(s, "frame_gold", "Золотая рамка", "frame", "Золотой контур портрета", "#E0BC57", rarity: HubShop.Rare);
            Item(s, "frame_berry", "Ягодная рамка", "frame", "Яркий контур портрета", "#C76C9C", rarity: HubShop.Uncommon);
            Item(s, "cardback_default", "Классика", "cardback", "Базовая рубашка карт", "#58766A", true);
            Item(s, "cardback_emerald", "Изумруд", "cardback", "Изумрудная рубашка", "#23785E", rarity: HubShop.Uncommon);
            Item(s, "cardback_wine", "Винная рубашка", "cardback", "Бордовая рубашка", "#813C53", rarity: HubShop.Uncommon);
            Item(s, "cardface_default", "Лицевая сторона · классика", "cardface", "Базовое оформление лицевой стороны карт", "#ECE0C7", true);
            Item(s, "cardface_contrast", "Крупные индексы", "cardface", "Выбор оформления · новое изображение появится позже", "#F1F0E6");
            Item(s, "chest_default", "Деревянный сундучок", "chest", "Базовый вид сундучка", "#967044", true);
            Item(s, "chest_copper", "Медный сундучок", "chest", "Выбор скина сундука · арт появится позже", "#B7784D", rarity: HubShop.Rare);
            Item(s, "trail_default", "Пунктирная дорожка", "trail", "Базовый след в Сундучках", "#BAAF83", true);
            Item(s, "trail_dollars", "Денежная дорожка", "trail", "Выбор следа · отображение появится позже", "#81AA74", rarity: HubShop.Rare);
            Item(s, "emote_default", "Базовые эмоции", "emote", "Базовый набор реакций", "#D5B96B", true);
            Item(s, "emote_playful", "Весёлые эмоции", "emote", "Выбор набора · картинки появятся позже", "#E6986A", rarity: HubShop.Uncommon);
            Item(s, "table_default", "Зелёное сукно", "table", "Базовое сукно", "#174F3D", true);
            Item(s, "table_blue", "Ночной стол", "table", "Синее сукно", "#223D60");
            Item(s, "table_plum", "Слива", "table", "Фиолетовое сукно", "#523655", rarity: HubShop.Uncommon);
            Item(s, "background_default", "Тёплый зал", "background", "Базовый фон", "#302D28", true);
            Item(s, "background_night", "Ночная комната", "background", "Тёмный фон", "#172135");
            Item(s, "hair_natural", "Свои волосы", "hair", "Исходный цвет волос", "#59442E", true);
            Item(s, "hair_chestnut", "Каштановые волосы", "hair", "Выбор для настройки внешности", "#724830");
            Item(s, "hair_blond", "Светлые волосы", "hair", "Выбор для настройки внешности", "#C6A15B");
            Item(s, "eyes_natural", "Свои глаза", "eyes", "Исходный цвет глаз", "#6B7256", true);
            Item(s, "eyes_blue", "Голубые глаза", "eyes", "Выбор для настройки внешности", "#5B93B6");
            Item(s, "eyes_green", "Зелёные глаза", "eyes", "Выбор для настройки внешности", "#679863");
            Item(s, "hat_cap", "Кепка", "hat", "Слот головного убора · арт появится позже", "#AC8764", rarity: HubShop.Uncommon);
            Item(s, "outfit_midnight", "Ночной костюм", "outfit", "Слот костюма · арт появится позже", "#243D4B", rarity: HubShop.Rare);
            Item(s, "tomato_pack", "Помидоры ×10", "consumable", "Косметический бросок без влияния на правила", "#DB5341");
            s.Catalog.Last().Quantity = 10;
            foreach (var item in s.Catalog.Where(i => i.IsDefault)) s.Inventory.Add(new HubOwnedItem { ItemId = item.Id });
            foreach (var slot in new[] { "frame", "cardback", "cardface", "chest", "trail", "emote", "table", "background", "hair", "eyes" })
                s.Equipped.Add(new HubEquipment { Slot = slot, ItemId = s.Catalog.First(i => i.Category == slot && i.IsDefault).Id });
            s.Equipped.Add(new HubEquipment { Slot = "avatar", ItemId = s.Profile.AvatarName });
            s.Inventory.Add(new HubOwnedItem { ItemId = "tomato_pack", Quantity = 3 });
            s.Friends.Add("bot-yegor");
            s.Friends.Add("bot-ali");
            AddMailContacts(s, now);
            s.FriendRequests.Add(new HubFriendRequest { Id = "request-danil", FromId = "bot-danil", ToId = s.Profile.Id, CreatedAt = now });
            s.Chats.Add(new HubChatMessage { Id = "welcome-chat", ConversationId = "bot-yegor", FromId = "bot-yegor", Text = "Привет! Это локальный демо-чат. Сыграем?", CreatedAt = now, IsLocalMock = true, Unread = true });
            s.Rooms.Add(new HubRoom { Id = "room-demo-durak", Code = "DURAK1", Name = "Вечерний стол", GameId = "durak", HostId = "bot-yegor", MaxPlayers = 4,
                Members = { new HubRoomMember { PlayerId = "bot-yegor", IsBot = true, Ready = true }, new HubRoomMember { PlayerId = "bot-ali", IsBot = true, Ready = true } } });
            s.Rooms.Add(new HubRoom { Id = "room-demo-poker", Code = "POKER1", Name = "Покер с ботами", GameId = "poker", HostId = "bot-aslan", MaxPlayers = 6,
                Members = { new HubRoomMember { PlayerId = "bot-aslan", IsBot = true, Ready = true } } });
            s.Invites.Add(new HubInvite { Id = "invite-yegor", FromId = "bot-yegor", ToId = s.Profile.Id, RoomId = "room-demo-durak" });
            Achievement(s, "tutorial_skip", "Дохуя умный", "Пропусти обучение с Тузярой", 1, 25, 10);
            Achievement(s, "six_seven", "Сикс Севен", "Сыграй партию со ставкой ровно 67 коинов", 1, 67, 25);
            Achievement(s, "mentalist", "Менталист", "Собери 5 сундучков за одну партию", 5, 100, 40);
            Achievement(s, "pendalist", "Пендалист", "Закончи партию в «Сундучки», не собрав ни одного сундучка", 1, 30, 15);
            Achievement(s, "gardener", "Садовник", "Получи пачку помидоров или брось помидор", 1, 25, 10);
            Achievement(s, "arachnophobe", "Арахнофоб", "Поймай паучка в пустом сундуке", 1, 30, 15);
            Achievement(s, "fine_print", "Мелкий шрифт", "Найди и оспорь подвох в договоре Тузяры", 1, 50, 30);
            Achievement(s, "first_win", "Первая победа", "Выиграй партию", 1, 50, 25);
            Achievement(s, "ten_matches", "Свой за столом", "Закончи 10 партий", 10, 150, 50);
            for (var level = 1; level <= 10; level++)
            {
                s.Pass.Rewards.Add(new HubPassReward { Id = "free-" + level, Level = level, Name = (25 + level * 10) + " коинов", Coins = 25 + level * 10 });
                var rewards = new[] { "frame_gold", "cardback_emerald", "table_blue", "eyes_blue", "hair_chestnut", "frame_berry", "cardback_wine", "table_plum", "hat_cap", "outfit_midnight" };
                var item = s.Catalog.First(i => i.Id == rewards[level - 1]);
                s.Pass.Rewards.Add(new HubPassReward { Id = "premium-" + level, Level = level, Name = item.Name, ItemId = item.Id, Premium = true });
            }
            return s;
        }

        // Version1 remains readable when a client update adds catalog slots. Never replace owned
        // quantities, purchases, wallet, equipment choices, or existing progression.
        // New local mail contacts are merged once; removed friendships stay removed.
        internal static void MergeCatalog(HubSnapshot existing, long now)
        {
            var defaults = Create(now);
            AddMailContacts(existing, now);
            foreach (var item in defaults.Catalog)
            {
                var saved = existing.Catalog.FirstOrDefault(i => i.Id == item.Id);
                if (saved == null) existing.Catalog.Add(item);
                // Цена и редкость — данные каталога, а не игрока: берутся из нынешней версии игры.
                else { saved.Price = item.Price; saved.Rarity = item.Rarity; saved.PriceLabel = item.PriceLabel; }
                if (item.IsDefault && !existing.Inventory.Any(i => i.ItemId == item.Id))
                    existing.Inventory.Add(new HubOwnedItem { ItemId = item.Id, Quantity = 1 });
            }
            foreach (var slot in defaults.Equipped)
                if (!existing.Equipped.Any(e => e.Slot == slot.Slot))
                    existing.Equipped.Add(new HubEquipment { Slot = slot.Slot, ItemId = slot.Slot == "avatar" ? existing.Profile.AvatarName : slot.ItemId });
            // Достижения, добавленные после сохранения профиля, появляются с нулевым прогрессом.
            foreach (var achievement in defaults.Achievements)
            {
                var saved = existing.Achievements.FirstOrDefault(a => a.Id == achievement.Id);
                if (saved == null) existing.Achievements.Add(achievement);
                else if (achievement.Id == "fine_print") saved.Description = achievement.Description;
            }
        }

        // Each person is added once: removed friends stay removed after a reload.
        private static void AddMailContacts(HubSnapshot s, long now)
        {
            var names = new[] { "Лера", "Тимур", "Соня", "Артур", "Ника", "Марк" };
            var ids = new[] { "lera", "timur", "sonya", "artur", "nika", "mark" };
            var avatars = new[] { 2, 1, 4, 3, 6, 5 };
            var greetings = new[] { "Привет! Заглядывай в таверну — сыграем вместе.", "Будет место за столом — зови!",
                "Люблю сундучки. А какая игра нравится тебе?", "Готов к новой партии. Выбирай игру!",
                "Хорошая компания уже есть, осталось раздать карты.", "После партии можно договориться о реванше." };
            for (var i = 0; i < ids.Length; i++)
            {
                var id = "bot-mail-" + ids[i];
                var avatar = "avatar_" + avatars[i].ToString("00");
                var existing = s.People.FirstOrDefault(person => person.Id == id);
                if (existing != null)
                {
                    if (existing.IsBot) existing.AvatarId = avatar;
                    continue;
                }
                s.People.Add(new HubPerson { Id = id, Name = names[i], AvatarId = avatar,
                    Level = 4 + i, Rating = 1050 + i * 45, IsBot = true, Status = "Локальный бот" });
                if (!s.Friends.Contains(id)) s.Friends.Add(id);
                if (!s.Chats.Any(message => message.Id == "welcome-" + id))
                    s.Chats.Add(new HubChatMessage { Id = "welcome-" + id, ConversationId = id, FromId = id,
                        Text = greetings[i], CreatedAt = now - 60 * (i + 1), IsLocalMock = true, Unread = false });
            }
        }

        // Цена — по редкости (HubShop.PriceFor); базовое и расходники в Лавке не продаются.
        private static void Item(HubSnapshot s, string id, string name, string category, string description, string color, bool basic = false,
            string resource = "", string rarity = HubShop.Common)
        {
            var price = basic || category == "consumable" ? 0 : HubShop.PriceFor(rarity);
            s.Catalog.Add(new HubCatalogItem { Id = id, Name = name, Category = category, Description = description, Color = color, IsDefault = basic,
                Resource = resource, Rarity = rarity, Price = price, PriceLabel = basic ? "Бесплатно" : price > 0 ? price + " коинов" : "Демо · открыть" });
        }

        private static void Achievement(HubSnapshot s, string id, string name, string description, int goal, int coins, int xp)
            => s.Achievements.Add(new HubAchievement { Id = id, Name = name, Description = description, Goal = goal, RewardCoins = coins, RewardXP = xp });
    }
}
