using System;
using System.Linq;
using CardGame.Core.Meta;
using UnityEngine;

namespace CardGameClient
{
    public sealed partial class PlayerHubController
    {
        private void BuildHubShopCatalog()
        {
            Title("Магазин · " + HubShopSectionName(_hubShopCategory), "Выбери предмет для своей коллекции. Пока всё открывается локально, без оплаты.");
            HubCatalogTabs(_hubShopCategory, category => { _hubShopCategory = category; _hubListPages["shop"] = 0; Navigate("shop-catalog"); });
            var items = _state.Catalog.Where(item => HubCategoryMatches(item.Category, _hubShopCategory)).OrderBy(item => HubOwned(item.Id)).ToArray();
            var start = HubPageStart("shop", items.Length, 6);
            for (var i = start; i < Math.Min(start + 6, items.Length); i++)
            {
                var item = items[i]; var n = i - start;
                var x = n % 2 * 590; var y = 62 + n / 2 * 171;
                HubItemCard(item, x, y, 166);
                var owned = HubOwned(item.Id);
                var consumable = item.Category == "consumable";
                Label(consumable ? item.PriceLabel : owned ? "Уже в коллекции" : "Без оплаты · локальное открытие", x + 16, y + 83, 528, 26, 17).color = HubPageMuted;
                Button(consumable ? "+10 помидоров (локально)" : owned ? "Открыть инвентарь" : "Получить (локально)", x + 16, y + 113, 528, 44,
                    () => { if (consumable) Send("tomato_buy", item.Id); else if (owned) Navigate("inventory"); else Send("cosmetic_unlock", item.Id); });
            }
            if (items.Length == 0) HubEmpty("В этой категории пока пусто", "Другие доступные предметы есть во вкладке «Все».", 85);
            Label("Новые образы не дают преимуществ. Реальные покупки здесь не подключены.", 0, 578, 1160, 28, 18).color = HubPageMuted;
        }

        private void BuildHubInventory()
        {
            Title("Инвентарь и образ", "Выбор сохраняется в профиле. Предметы без готового рисунка пока меняют только слот.");
            HubCatalogTabs(_hubInventoryCategory, category => { _hubInventoryCategory = category; _hubListPages["inventory"] = 0; Navigate("inventory"); });
            var items = _state.Catalog.Where(item => HubOwned(item.Id) && HubCategoryMatches(item.Category, _hubInventoryCategory)).ToArray();
            var start = HubPageStart("inventory", items.Length, 6);
            for (var i = start; i < Math.Min(start + 6, items.Length); i++)
            {
                var item = items[i]; var n = i - start;
                var x = n % 2 * 590; var y = 62 + n / 2 * 171;
                HubItemCard(item, x, y, 166);
                var equipped = _state.Equipped.Any(slot => slot.ItemId == item.Id);
                var quantity = _state.Inventory.Where(entry => entry.ItemId == item.Id).Sum(entry => entry.Quantity);
                Label(item.Category == "consumable" ? "В наличии: " + quantity : equipped ? "Сейчас выбрано" : "Слот: " + HubCategoryName(item.Category),
                    x + 16, y + 83, 528, 26, 17).color = equipped ? HubPageGold : HubPageMuted;
                Button(item.Category == "consumable" ? "Бросить в демо-игрока" : equipped ? item.Category == "avatar" ? "Сейчас выбран" : "Снять" : "Выбрать", x + 16, y + 113, 528, 44,
                    () => { if (item.Category == "consumable") Navigate("throw:" + item.Id); else Send(equipped ? "unequip" : "equip", equipped ? item.Category : item.Id); },
                    !equipped || item.Category != "avatar");
            }
            if (items.Length == 0)
            {
                HubEmpty("Коллекция пока пуста", "Открой предмет в магазине или получи награду за задание.", 80);
                Button("В магазин", 0, 270, 270, 48, () => Navigate("shop"));
            }
            Button("Выбрать персонажа", 0, 574, 300, 44, OpenAvatarPicker);
            Label("Головной убор, костюм, волосы и глаза — отдельные слоты.", 445, 579, 715, 28, 17).color = HubPageMuted;
        }

        private void HubItemCard(HubCatalogItem item, float x, float y, float height)
        {
            Panel("Item " + item.Id, x, y, 570, height);
            var swatch = Panel("Item colour", x + 16, y + 14, 42, 42);
            if (ColorUtility.TryParseHtmlString(item.Color, out var color)) swatch.GetComponent<UnityEngine.UI.Image>().color = color;
            if (item.Category == "avatar") HubPortrait(AvatarCatalog.Find(item.Id), x + 16, y + 12, 42, 66);
            Label(item.Name, x + 76, y + 11, 476, 31, 22).color = HubPageGold;
            Label(HubCategoryName(item.Category) + " · " + item.Description, x + 76, y + 45, 476, 38, 17).color = HubPageMuted;
        }

        private bool HubOwned(string id) => _state.Inventory.Any(entry => entry.ItemId == id && entry.Quantity > 0);

        private void HubCatalogTabs(string selected, Action<string> select)
        {
            HubTabs(new[] { "Все", "Персонаж", "Стол и карты", "Рамки", "Предметы / эмоции" },
                new[] { "all", "character", "table", "frame", "consumable" }, selected, select);
        }

        private static bool HubCategoryMatches(string category, string filter)
        {
            if (filter == "all") return true;
            if (filter == "character") return category == "avatar" || category == "hat" || category == "outfit" || category == "hair" || category == "eyes";
            if (filter == "table") return category == "table" || category == "cardback" || category == "cardface"
                || category == "background" || category == "chest" || category == "trail";
            if (filter == "consumable") return category == "consumable" || category == "emote";
            return category == filter;
        }

        private static string HubCategoryName(string category)
        {
            switch (category)
            {
                case "avatar": return "Персонаж";
                case "hat": return "Головной убор";
                case "outfit": return "Костюм";
                case "hair": return "Волосы";
                case "eyes": return "Глаза";
                case "frame": return "Рамка";
                case "cardback": return "Рубашка карт";
                case "cardface": return "Лицевая сторона карт";
                case "chest": return "Сундук";
                case "trail": return "Дорожка";
                case "emote": return "Эмоции";
                case "table": return "Стол";
                case "background": return "Фон";
                case "consumable": return "Предмет";
                default: return category;
            }
        }

        private void BuildHubThrow()
        {
            Title("Тренировка броска", "Один помидор расходуется из инвентаря. Выбор не влияет на карты и результаты.");
            Label("В кого бросим? Все участники здесь — локальные демо-профили.", 0, 0, 1160, 54, 23);
            var people = _state.People.Where(person => person.IsBot).Take(6).ToArray();
            for (var i = 0; i < people.Length; i++)
            {
                var person = people[i]; var x = i % 3 * 390; var y = 78 + i / 3 * 203;
                Panel("Tomato target " + person.Id, x, y, 370, 180);
                HubPortrait(AvatarCatalog.Find(person.AvatarId), x + 16, y + 10, 90, 105);
                Label(person.Name, x + 130, y + 37, 220, 34, 24).color = HubPageGold;
                Button("Бросить помидор", x + 16, y + 125, 338, 40, () => Send("tomato_throw", person.Id));
            }
            Button("В инвентарь", 0, 535, 280, 48, () => Navigate("inventory"));
        }

        private void BuildHubBank()
        {
            var bank = _state.Bank;
            Title("Банк Тузяры", "Кабинет хозяина банка · локальные заявки и долг в виртуальных коинах");
            var office = Panel("Tuzyara office", 0, 0, 322, 648);
            office.GetComponent<UnityEngine.UI.Image>().color = Color.Lerp(Accent("bank"), HubNight, .72f);
            Label("ТУЗЯРА", 25, 24, 270, 43, 31).color = HubPageGold;
            var suit = Label("♣", 30, 73, 260, 157, 123);
            suit.color = HubPageGold;
            suit.alignment = TextAnchor.MiddleCenter;
            Label("Хозяин банка", 25, 242, 270, 34, 25).color = HubPageGold;
            Label("«Добро пожаловать.\nСначала посмотрим,\nчто у тебя на счету». ", 25, 294, 272, 130, 23);
            Label("Два валета бубен\nзанимаются долгами\nпо его поручению.", 25, 477, 272, 102, 21).color = HubPageMuted;

            HubPanelText("Wallet", "На счету", HubMoney(_state.Profile.Coins) + " коинов", 350, 0, 260, 119);
            HubPanelText("Debt", "Долг", HubMoney(bank.Debt) + " коинов" + (bank.Debt > 0 ? "\nк возврату" : "\nвсё погашено"), 625, 0, 260, 119);
            HubPanelText("Limit", "Лимит заявки", HubMoney(bank.CreditLimit) + " коинов · " + bank.InterestPercent + "%", 900, 0, 260, 119);
            Panel("Bank decision", 350, 137, 810, 132);
            Label(HubBankStatus(bank.Status), 369, 151, 772, 35, 25).color = HubPageGold;
            Label(bank.Decision, 369, 195, 772, 60, 20).color = HubPageMuted;
            if (bank.Debt > 0)
            {
                Label("Срок: " + HubDate(bank.DueAt) + "\nПогашено: " + HubMoney(bank.Repaid), 350, 290, 800, 67, 22);
                Button("Погасить доступную сумму", 350, 370, 810, 49, () => Send("bank_repay"), _state.Profile.Coins > 0);
                if (bank.CollectorPending)
                {
                    Panel("Collectors", 350, 437, 810, 162);
                    Label("Коллекторы ждут за столом", 370, 452, 770, 35, 25).color = HubPageGold;
                    Label("Игра: " + HubGameName(bank.CollectorGame) + ". Долг решится за игровым столом.", 370, 492, 770, 28, 19).color = HubPageMuted;
                    Button("Сыграть с коллекторами", 370, 535, 770, 46, () => Send("play_collectors"), bank.CollectorPending);
                }
                else Label(bank.CollectionActive
                    ? "Коллекторы ушли. Пока долг не погашен, 50% новых поступлений автоматически идут в банк, остальные остаются тебе."
                    : "Верни долг до указанного срока. Пока коллекторы не приходили, погашение выполняется вручную.",
                    350, 449, 810, 114, 22).color = HubPageMuted;
            }
            else if (bank.Status == "pending")
            {
                Label("Запрошено: " + HubMoney(bank.RequestedAmount) + "\nРассмотрение: " + HubDate(bank.ReviewAt), 350, 291, 810, 82, 23);
                Button("Проверить решение", 350, 393, 810, 49, () => Send("bank_review"));
            }
            else
            {
                Label("Сумма новой заявки", 350, 292, 800, 34, 23);
                var amount = Input(Math.Min(500, bank.CreditLimit).ToString(), 350, 338, 355);
                amount.contentType = UnityEngine.UI.InputField.ContentType.IntegerNumber;
                Button("Подать заявку", 725, 338, 435, 44,
                    () => { int.TryParse(amount.text, out var value); Send("bank_request", amount: value); }, bank.CreditLimit > 0);
                Label("Лимит зависит от прогресса. Заявка может быть отклонена; коины появятся после одобрения.", 350, 411, 810, 90, 22).color = HubPageMuted;
            }
            Label("Долг хранится отдельно от баланса. Реальных кредитов здесь нет.", 350, 619, 810, 29, 18).color = HubPageMuted;
        }

        private static string HubBankStatus(string status)
        {
            switch (status)
            {
                case "pending": return "Заявка на рассмотрении";
                case "approved": return "Заявка одобрена";
                case "rejected": return "В заявке отказано";
                case "overdue": return "Срок погашения прошёл";
                case "collection": return "Дело у коллекторов";
                case "repaying": return "Погашение долга";
                case "paid": return "Долг погашен";
                default: return "Банк открыт";
            }
        }

        private static string HubDate(long seconds)
        {
            return seconds <= 0 ? "—" : DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime().ToString("dd.MM HH:mm");
        }
    }
}
