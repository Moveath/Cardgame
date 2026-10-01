using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace CardGame.Core.Meta
{
    /// <summary>Local-only mock. Currency/purchases/clock must become server-authoritative before online billing.</summary>
    public sealed class LocalPlayerHubService : IPlayerHubService
    {
        private readonly IHubStore _store;
        private readonly IHubClock _clock;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private HubSnapshot? _snapshot;
        private static readonly string[] Games = { "durak", "bluff", "chests", "govno", "poker" };

        public LocalPlayerHubService(IHubStore store, IHubClock clock)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public async Task<HubResult> LoadAsync()
        {
            await _gate.WaitAsync();
            try
            {
                await EnsureLoaded();
                var next = Copy(_snapshot!);
                Refresh(next);
                await Commit(next);
                return Result(true, "", "Локальный профиль загружен");
            }
            catch (Exception) { return Result(false, "storage", "Не удалось загрузить локальный профиль. Попробуй ещё раз."); }
            finally { _gate.Release(); }
        }

        public async Task<HubResult> ExecuteAsync(HubCommand command)
        {
            await _gate.WaitAsync();
            try
            {
                await EnsureLoaded();
                if (command == null || string.IsNullOrWhiteSpace(command.Id) || command.Id.Length > 128)
                    return Result(false, "invalid_id", "Команде нужен уникальный номер запроса.");
                var fingerprint = Fingerprint(command);
                var receipt = _snapshot!.Receipts.FirstOrDefault(r => r.Id == command.Id);
                if (receipt != null)
                    return receipt.Fingerprint == fingerprint ? Result(true, "", receipt.Message)
                        : Result(false, "id_conflict", "Этот номер запроса уже использован для другого действия.");
                var next = Copy(_snapshot);
                Refresh(next);
                var message = Apply(next, command);
                next.Receipts.Add(new HubReceipt { Id = command.Id, Fingerprint = fingerprint, Message = message });
                await Commit(next);
                return Result(true, "", message);
            }
            catch (HubFailure e) { return Result(false, e.Code, e.Message); }
            catch (Exception) { return Result(false, "storage", "Изменения не сохранены. Можно безопасно повторить запрос."); }
            finally { _gate.Release(); }
        }

        private async Task EnsureLoaded()
        {
            if (_snapshot != null) return;
            var saved = await _store.LoadAsync();
            var next = saved == null ? HubSeed.Create(Now) : Copy(saved);
            if (next.Version != 1 || next.Profile == null || next.Profile.Coins < 0 || next.Bank == null || next.Bank.Debt < 0)
                throw new InvalidOperationException("Unsupported or invalid local save.");
            HubSeed.MergeCatalog(next, Now);
            foreach (var room in next.Rooms.Where(r => r.GameSettings == null && Games.Contains(r.GameId)))
            {
                room.GameSettings = HubGameSettings.DefaultFor(room.GameId);
                room.MaxPlayers = Math.Max(room.MaxPlayers, Math.Max(HubGameRules.MinimumPlayers(room.GameId), room.Members.Count));
            }
            Refresh(next);
            if (saved == null) await _store.SaveAsync(Copy(next));
            _snapshot = next;
        }

        private async Task Commit(HubSnapshot next)
        {
            if (next.Profile.Coins < 0 || next.Bank.Debt < 0) throw new InvalidOperationException("Negative economy balance.");
            next.Revision = checked(next.Revision + 1);
            next.UpdatedAt = Now;
            await _store.SaveAsync(Copy(next)); // Only publish after successful persistence.
            _snapshot = next;
        }

        private HubResult Result(bool success, string error, string message)
            => new HubResult { Success = success, Error = error, Message = message, Snapshot = _snapshot == null ? new HubSnapshot() : Copy(_snapshot) };

        private long Now => Math.Max(0, _clock.UnixSeconds);

        private string Apply(HubSnapshot s, HubCommand c)
        {
            var action = (c.Action ?? "").Trim().ToLowerInvariant();
            var target = (c.Target ?? "").Trim();
            var text = (c.Text ?? "").Trim();
            switch (action)
            {
                case "profile_update":
                    Require(text.Length >= 2 && text.Length <= 24, "name", "Имя должно содержать от 2 до 24 символов.");
                    s.Profile.DisplayName = text;
                    if (target.Length > 0) Equip(s, target);
                    return "Профиль сохранён";
                case "set_avatar":
                    Require(s.Catalog.Any(i => i.Id == target && i.Category == "avatar"), "avatar", "Такого персонажа нет в локальном каталоге.");
                    Equip(s, target); return "Персонаж выбран";
                case "settings_update": return Settings(s, target, text, c.Amount, c.Flag);
                case "tutorial_select":
                    Require(Games.Contains(target), "game", "Урок для этой игры пока не готов.");
                    if (s.Tutorial.ActiveGame != target) { s.Tutorial.ActiveGame = target; s.Tutorial.Step = 0; }
                    return "Урок выбран";
                case "tutorial_next": s.Tutorial.Step = Math.Min(3, s.Tutorial.Step + 1); return "Следующий шаг обучения";
                case "tutorial_restart":
                    Require(Games.Contains(target), "game", "Урок для этой игры пока не готов.");
                    s.Tutorial.ActiveGame = target; s.Tutorial.Step = 0;
                    s.Tutorial.CompletedGames.Remove(target); return "Урок открыт заново";
                case "tutorial_complete":
                    s.Tutorial.Completed = true; s.Tutorial.Step = target.Length > 0 ? 0 : 3;
                    if (target.Length > 0 && !s.Tutorial.CompletedGames.Contains(target)) s.Tutorial.CompletedGames.Add(target);
                    return "Обучение пройдено";
                case "tutorial_skip":
                    s.Tutorial.Completed = s.Tutorial.Skipped = true;
                    Progress(s, "tutorial_skip", 1); return "Обучение пропущено · достижение открыто";
                case "cosmetic_unlock":
                    var item = FindItem(s, target);
                    Require(!item.IsDefault, "owned", "Этот предмет уже бесплатный.");
                    GrantItem(s, item.Id, item.Quantity);
                    if (item.Category == "consumable") Progress(s, "gardener", 1);
                    return "Открыто в локальном демо. Деньги не списывались.";
                case "equip": Equip(s, target); return "Предмет надет";
                case "unequip":
                    Require(target != "avatar", "avatar", "Нужно выбрать другого персонажа.");
                    Require(s.Catalog.Any(i => i.Category == target && i.Category != "consumable"), "slot", "Такого слота нет.");
                    s.Equipped.RemoveAll(e => e.Slot == target);
                    // An explicit empty slot survives catalog migrations; absence means old schema.
                    s.Equipped.Add(new HubEquipment { Slot = target, ItemId = "" }); return "Предмет снят";
                case "tomato_buy": GrantItem(s, "tomato_pack", 10); Progress(s, "gardener", 1); return "Демо-пачка: +10 помидоров";
                case "tomato_throw":
                    Require(target.Length > 0 && target != s.Profile.Id, "target", "Выбери соперника.");
                    Require(s.People.Any(p => p.Id == target) || s.CurrentRoomId.Length > 0 && Room(s).Members.Any(m => m.PlayerId == target), "target", "Игрок не найден.");
                    var tomatoes = s.Inventory.FirstOrDefault(i => i.ItemId == "tomato_pack");
                    Require(tomatoes != null && tomatoes.Quantity > 0, "inventory", "Помидоры закончились.");
                    Require(s.Profile.LastTomatoAt == 0 || Now - s.Profile.LastTomatoAt >= 2, "cooldown", "Подожди две секунды между бросками.");
                    tomatoes!.Quantity--; s.Profile.TomatoesThrown++; s.Profile.LastTomatoAt = Now;
                    Progress(s, "gardener", 1); return "Помидор брошен · правила игры не изменились";
                case "friend_request":
                    Person(s, target);
                    Require(!s.Friends.Contains(target), "friend", "Вы уже друзья.");
                    Require(!s.FriendRequests.Any(r => r.Status == "pending" && (r.FromId == target || r.ToId == target)), "pending", "Заявка уже ожидает ответа.");
                    s.FriendRequests.Add(new HubFriendRequest { Id = "friend-" + c.Id, FromId = s.Profile.Id, ToId = target, CreatedAt = Now });
                    return "Локальная заявка отправлена боту";
                case "friend_accept": return ResolveFriend(s, target, true, false);
                case "friend_decline": return ResolveFriend(s, target, false, false);
                case "friend_mock_reply": return ResolveFriend(s, target, c.Flag, true);
                case "friend_remove":
                    Require(s.Friends.Remove(target), "friend", "Игрока нет в друзьях."); return "Друг удалён";
                case "chat_send": case "send_chat":
                    Require(text.Length > 0 && text.Length <= 400, "message", "Сообщение должно содержать от 1 до 400 символов.");
                    var isRoom = target == s.CurrentRoomId && target.Length > 0;
                    Require(isRoom || s.Friends.Contains(target), "friend", "Чат доступен с друзьями или в своей комнате.");
                    s.Chats.Add(new HubChatMessage { Id = "chat-" + c.Id, ConversationId = target, FromId = s.Profile.Id, Text = text, CreatedAt = Now, IsLocalMock = true });
                    var bot = isRoom ? Room(s).Members.FirstOrDefault(m => m.IsBot)?.PlayerId : target;
                    if (!string.IsNullOrEmpty(bot)) s.Chats.Add(new HubChatMessage { Id = "reply-" + c.Id, ConversationId = target, FromId = bot!, Text = "[Локальный бот] Договорились, сыграем!", CreatedAt = Now, IsLocalMock = true });
                    if (s.Chats.Count > 200) s.Chats.RemoveRange(0, s.Chats.Count - 200);
                    return "Сообщение в локальном чате";
                case "create_room": case "room_create":
                    Require(Games.Contains(target), "game", "Выбери доступную игру.");
                    var createSettings = c.GameSettings?.Copy() ?? HubGameSettings.DefaultFor(target);
                    ValidateGameSettings(target, createSettings, c.Amount);
                    Require(c.Value >= 0 && c.Value <= 1000000, "stake", "Недопустимая ставка.");
                    Require(c.Value == 0 || s.Bank.Debt == 0, "debt", "Пока есть долг, доступны столы без ставок.");
                    LeaveRoom(s);
                    var newRoom = new HubRoom { Id = "room-" + c.Id, Code = RoomCode(c.Id), Name = text.Length == 0 ? "Стол " + s.Profile.DisplayName : text.Substring(0, Math.Min(32, text.Length)), GameId = target, GameSettings = createSettings, HostId = s.Profile.Id, MaxPlayers = c.Amount, Private = c.Flag, Stake = c.Value };
                    newRoom.Members.Add(new HubRoomMember { PlayerId = s.Profile.Id });
                    s.Rooms.Add(newRoom); s.CurrentRoomId = newRoom.Id; return "Локальная комната создана";
                case "join_room": case "room_join": JoinRoom(s, target.Length > 0 ? target : text); return "Ты в локальной комнате";
                case "leave_room": case "room_leave":
                    if (s.ActiveMatchId.Length > 0) Abandon(s, s.ActiveMatchId);
                    LeaveRoom(s); return "Ты вышел из комнаты";
                case "room_ready": case "set_ready": case "set_room_ready":
                    var readyRoom = Room(s); Require(!readyRoom.Started, "started", "Партия уже началась.");
                    // Local bots accept the updated rules when the local player confirms them.
                    if (c.Flag) foreach (var member in readyRoom.Members.Where(m => m.IsBot)) member.Ready = true;
                    readyRoom.Members.First(m => m.PlayerId == s.Profile.Id).Ready = c.Flag; return c.Flag ? "Ты готов" : "Готовность снята";
                case "room_add_bot": case "add_room_bot":
                    var addRoom = EditableRoom(s);
                    Require(addRoom.Members.Count < addRoom.MaxPlayers, "full", "В комнате нет свободных мест.");
                    var person = target.Length > 0 ? Person(s, target) : s.People.FirstOrDefault(p => !addRoom.Members.Any(m => m.PlayerId == p.Id));
                    Require(person != null && !addRoom.Members.Any(m => m.PlayerId == person.Id), "member", "Этот бот уже сидит за столом.");
                    addRoom.Members.Add(new HubRoomMember { PlayerId = person!.Id, IsBot = true, Ready = true }); return "Бот добавлен";
                case "room_remove_bot": case "remove_room_bot": case "remove_room_member":
                    Require(EditableRoom(s).Members.RemoveAll(m => m.PlayerId == target && m.IsBot) > 0, "member", "Бот не найден."); return "Бот убран";
                case "set_room_stake":
                    var stakeRoom = EditableRoom(s); Require(c.Amount >= 0 && c.Amount <= 1000000, "stake", "Недопустимая ставка.");
                    Require(c.Amount == 0 || s.Bank.Debt == 0, "debt", "Пока есть долг, доступны столы без ставок.");
                    if (stakeRoom.Stake != c.Amount) ResetReady(stakeRoom);
                    stakeRoom.Stake = c.Amount; return "Ставка сохранена";
                case "update_room_settings":
                    var editRoom = EditableRoom(s);
                    Require(editRoom.HostId == s.Profile.Id, "host", "Настройки меняет хозяин комнаты.");
                    var editedSettings = c.GameSettings?.Copy() ?? HubGameSettings.DefaultFor(editRoom.GameId);
                    ValidateGameSettings(editRoom.GameId, editedSettings, c.Amount);
                    Require(c.Amount >= editRoom.Members.Count, "capacity", "Нельзя убрать уже занятые места.");
                    Require(c.Value >= 0 && c.Value <= 1000000, "stake", "Недопустимая ставка.");
                    Require(c.Value == 0 || s.Bank.Debt == 0, "debt", "Пока есть долг, доступны столы без ставок.");
                    if (editRoom.MaxPlayers != c.Amount || editRoom.Stake != c.Value
                        || !(editRoom.GameSettings ?? HubGameSettings.DefaultFor(editRoom.GameId)).HasSameRules(editedSettings))
                        ResetReady(editRoom);
                    editRoom.Name = text.Length == 0 ? "Стол " + s.Profile.DisplayName : text.Substring(0, Math.Min(32, text.Length));
                    editRoom.MaxPlayers = c.Amount; editRoom.Private = c.Flag; editRoom.Stake = c.Value;
                    editRoom.GameSettings = editedSettings;
                    return "Настройки комнаты сохранены";
                case "room_game": case "set_room_game":
                    Require(Games.Contains(target), "game", "Игра не найдена."); var gameRoom = EditableRoom(s);
                    var changedSettings = HubGameSettings.DefaultFor(target);
                    ValidateGameSettings(target, changedSettings, gameRoom.MaxPlayers);
                    if (gameRoom.GameId != target || !(gameRoom.GameSettings ?? HubGameSettings.DefaultFor(gameRoom.GameId)).HasSameRules(changedSettings))
                        ResetReady(gameRoom);
                    gameRoom.GameId = target; gameRoom.GameSettings = changedSettings; return "Игра выбрана";
                case "invite_friend": case "room_invite":
                    Require(s.Friends.Contains(target), "friend", "Пригласить можно друга.");
                    var inviteRoom = Room(s); Require(!inviteRoom.Started && inviteRoom.Members.Count < inviteRoom.MaxPlayers, "full", "В комнате нет доступных мест.");
                    Require(!inviteRoom.Members.Any(m => m.PlayerId == target), "member", "Друг уже за столом.");
                    s.Invites.Add(new HubInvite { Id = "invite-" + c.Id, FromId = s.Profile.Id, ToId = target, RoomId = inviteRoom.Id });
                    return "Локальное приглашение создано; ответ бота можно принять в демо";
                case "invite_accept": case "invite_decline":
                    var invitation = s.Invites.FirstOrDefault(i => i.Id == target && i.Status == "pending");
                    Require(invitation != null, "invite", "Приглашение не найдено.");
                    if (action == "invite_decline") { invitation!.Status = "declined"; return "Приглашение отклонено"; }
                    if (invitation!.ToId == s.Profile.Id) JoinRoom(s, invitation.RoomId);
                    else
                    {
                        var invitedRoom = s.Rooms.FirstOrDefault(r => r.Id == invitation.RoomId);
                        Require(invitedRoom != null && !invitedRoom.Started && invitedRoom.Members.Count < invitedRoom.MaxPlayers, "full", "Комната уже недоступна.");
                        Require(!invitedRoom!.Members.Any(m => m.PlayerId == invitation.ToId), "member", "Друг уже за столом.");
                        invitedRoom.Members.Add(new HubRoomMember { PlayerId = invitation.ToId, Ready = true, IsBot = true });
                    }
                    invitation.Status = "accepted"; return "Приглашение принято локально";
                case "start_room": case "room_start": return StartRoom(s, c.Id);
                case "begin_match":
                    Require(Games.Contains(target), "game", "Игра не найдена.");
                    Require(s.ActiveMatchId.Length == 0, "active_match", "Сначала заверши текущую партию.");
                    Require(!s.Bank.CollectorPending, "collector", "Сначала сыграй с коллекторами.");
                    Require(c.Amount >= 0 && c.Amount <= s.Profile.Coins, "coins", "Недостаточно коинов для входа.");
                    Require(c.Amount == 0 || s.Bank.Debt == 0, "debt", "Пока есть долг, доступны столы без ставок.");
                    s.Profile.Coins -= c.Amount;
                    s.Matches.Add(new HubMatchEntry { Id = c.Id, Game = target, Stake = c.Amount }); s.ActiveMatchId = c.Id;
                    return "Локальная партия началась";
                case "abandon_match": Abandon(s, target.Length > 0 ? target : s.ActiveMatchId); return "Партия завершена досрочно; ставка не возвращается";
                case "report_match": return ReportMatch(s, c, target, text);
                case "report_chests":
                    Require(c.Amount >= 0 && c.Amount <= 13, "count", "Неверное число сундучков."); Chests(s, c.Amount); return "Сундучки учтены";
                case "spider_killed": Progress(s, "arachnophobe", 1); return "Паучок пойман · достижение открыто";
                case "achievement_claim":
                    var achievement = s.Achievements.FirstOrDefault(a => a.Id == target);
                    Require(achievement != null && achievement.Unlocked && !achievement.Claimed, "reward", "Награда ещё недоступна или уже получена.");
                    achievement!.Claimed = true; Credit(s, achievement.RewardCoins); Experience(s, achievement.RewardXP); return "Награда за достижение получена";
                case "quest_claim":
                    var quest = s.Quests.FirstOrDefault(q => q.Id == target);
                    Require(quest != null && quest.Progress >= quest.Goal && !quest.Claimed && quest.ExpiresAt > Now, "reward", "Задание ещё не выполнено или награда уже получена.");
                    quest!.Claimed = true; Credit(s, quest.RewardCoins); Experience(s, quest.RewardXP); return "Награда за задание получена";
                case "pass_premium": s.Pass.Premium = true; return "Премиум-дорожка открыта в демо · без оплаты";
                case "pass_claim":
                    var reward = s.Pass.Rewards.FirstOrDefault(r => r.Id == target);
                    Require(reward != null && !reward.Claimed && reward.Level <= s.Pass.Level && (!reward.Premium || s.Pass.Premium), "reward", "Награда пропуска пока недоступна или уже получена.");
                    reward!.Claimed = true;
                    Require(!reward.Premium || reward.Coins == 0, "premium_currency", "Премиум-дорожка содержит только косметику.");
                    if (reward.Coins > 0) Credit(s, reward.Coins);
                    if (reward.ItemId.Length > 0) GrantItem(s, reward.ItemId, 1);
                    return "Награда пропуска получена";
                case "bank_request":
                    Require(s.Bank.Debt == 0 && s.Bank.Status != "pending", "loan", "Сначала погаси текущий кредит или дождись решения.");
                    Require(c.Amount > 0 && c.Amount <= 1000000, "amount", "Укажи положительную сумму кредита.");
                    s.Bank.RequestId = c.Id; s.Bank.RequestedAmount = c.Amount; s.Bank.Status = "pending";
                    s.Bank.ReviewAt = Now + 30; s.Bank.Decision = "Заявка рассматривается · 30 секунд в локальном демо";
                    return s.Bank.Decision;
                case "bank_review":
                    Require(s.Bank.Status != "pending", "pending", "Заявка ещё рассматривается."); return s.Bank.Decision;
                case "bank_repay":
                    Require(s.Bank.Debt > 0, "debt", "Задолженности нет.");
                    var payment = c.Amount == 0 ? Math.Min(s.Profile.Coins, s.Bank.Debt) : c.Amount;
                    Require(payment > 0 && payment <= s.Profile.Coins && payment <= s.Bank.Debt, "amount", "Сумма должна укладываться в баланс и остаток долга.");
                    s.Profile.Coins -= payment; Repay(s, payment); return "Погашено " + payment + " коинов";
                case "play_collectors": case "start_collectors":
                    Require(s.Bank.CollectorPending && s.Bank.Debt > 0, "collector", "Коллекторы пока не пришли.");
                    Require(s.ActiveMatchId.Length == 0, "active_match", "Сначала заверши текущую партию.");
                    var entry = new HubMatchEntry { Id = "collector-" + c.Id, Game = s.Bank.CollectorGame, IsCollector = true };
                    s.Matches.Add(entry); s.ActiveMatchId = entry.Id; return "Коллекторы выбрали игру · " + entry.Game;
                default: throw new HubFailure("unknown_action", "Это действие пока не поддерживается.");
            }
        }

        private string ReportMatch(HubSnapshot s, HubCommand command, string game, string entryId)
        {
            Require(Games.Contains(game), "game", "Игра не найдена.");
            Require(command.Value >= 0 && command.Value <= 13, "count", "Неверное число сундучков.");
            Require(command.Amount == 0 || command.Amount == 1, "outcome", "Неизвестный исход партии.");
            var draw = command.Amount == 1;
            if (entryId.Length == 0 && s.ActiveMatchId.Length > 0) entryId = s.ActiveMatchId;
            var entry = entryId.Length == 0 ? null : s.Matches.FirstOrDefault(m => m.Id == entryId);
            Require(entryId.Length == 0 || entry != null, "match", "Партия не найдена.");
            var collectorLoss = entry != null && entry.IsCollector && !draw && !command.Flag;
            if (entry != null)
            {
                Require(entry.Game == game, "match", "Результат относится к другой игре.");
                if (entry.Completed) return "Результат этой партии уже учтён";
                entry.Completed = true;
                if (entry.IsCollector && !draw) Collectors(s, command.Flag);
                else if (draw && entry.Stake > 0) s.Profile.Coins = checked(s.Profile.Coins + entry.Stake);
                else if (command.Flag && entry.Stake > 0) Credit(s, checked(entry.Stake * 2));
                if (entry.Stake == 67) Progress(s, "six_seven", 1);
                if (s.ActiveMatchId == entry.Id) s.ActiveMatchId = "";
                foreach (var room in s.Rooms.Where(r => r.LocalMatchId == entry.Id)) { room.Started = false; room.Members.Where(m => !m.IsBot).ToList().ForEach(m => m.Ready = false); }
            }
            s.Profile.Matches = checked(s.Profile.Matches + 1);
            if (!draw && command.Flag) { s.Profile.Wins++; s.Profile.Rating = checked(s.Profile.Rating + 20); Progress(s, "first_win", 1); }
            else if (!draw) { s.Profile.Losses++; s.Profile.Rating = Math.Max(0, s.Profile.Rating - 10); }
            // The collectors' loss leaves the wallet at zero; a participation payout here
            // would immediately undo that rule. Subsequent earnings still repay half the debt.
            if (!collectorLoss) Credit(s, !draw && command.Flag ? 80 : 20);
            Experience(s, !draw && command.Flag ? 40 : 20);
            Progress(s, "ten_matches", s.Profile.Matches, true);
            QuestEvent(s, "match", game, 1);
            if (!draw && command.Flag) QuestEvent(s, "win", game, 1);
            if (game == "chests")
            {
                Chests(s, command.Value);
                // Только за доигранную партию: брошенная закрывается раньше и сюда не доходит.
                if (command.Value == 0) Progress(s, "pendalist", 1);
            }
            return collectorLoss ? "Партия с коллекторами учтена · +20 опыта, без награды коинами"
                : draw ? "Ничья · ставка возвращена, +20 коинов и 20 опыта за участие"
                : command.Flag ? "Победа · +80 коинов и 40 опыта до удержания долга" : "Партия учтена · +20 коинов и 20 опыта до удержания долга";
        }

        private string StartRoom(HubSnapshot s, string commandId)
        {
            var room = Room(s);
            if (room.Started && room.LocalMatchId.Length > 0) return "Эта партия уже запущена";
            ValidateGameSettings(room.GameId, room.GameSettings ?? HubGameSettings.DefaultFor(room.GameId), room.MaxPlayers);
            Require(s.ActiveMatchId.Length == 0, "active_match", "Сначала заверши текущую партию.");
            Require(room.Members.Count >= HubGameRules.MinimumPlayers(room.GameId), "players", "За столом пока недостаточно игроков.");
            Require(room.Members.Count <= room.MaxPlayers, "capacity", "В комнате слишком много игроков для выбранных правил.");
            Require(room.Members.All(m => m.Ready), "ready", "Все игроки должны подтвердить готовность.");
            Require(!s.Bank.CollectorPending, "collector", "Сначала сыграй с коллекторами.");
            Require(room.Stake == 0 || s.Bank.Debt == 0, "debt", "Пока есть долг, доступны столы без ставок.");
            Require(room.Stake <= s.Profile.Coins, "coins", "Не хватает заработанных коинов для входа.");
            s.Profile.Coins -= room.Stake;
            var entry = new HubMatchEntry { Id = "match-" + commandId, Game = room.GameId, Stake = room.Stake };
            s.Matches.Add(entry); s.ActiveMatchId = room.LocalMatchId = entry.Id; room.Started = true;
            return "Локальная партия началась";
        }

        private void Abandon(HubSnapshot s, string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            var entry = s.Matches.FirstOrDefault(m => m.Id == id);
            Require(entry != null, "match", "Партия не найдена.");
            if (entry!.Completed) return;
            entry.Completed = entry.Abandoned = true;
            // Leaving the collectors' game is a loss, so restarting cannot cancel the debt event.
            if (entry.IsCollector) Collectors(s, false);
            if (s.ActiveMatchId == id) s.ActiveMatchId = "";
            foreach (var room in s.Rooms.Where(r => r.LocalMatchId == id)) { room.Started = false; room.Members.Where(m => !m.IsBot).ToList().ForEach(m => m.Ready = false); }
        }

        private void Refresh(HubSnapshot s)
        {
            var day = Now / 86400;
            var week = (day + 3) / 7; // Unix epoch was Thursday; weekly reset is Monday UTC.
            s.Quests.RemoveAll(q => q.ExpiresAt <= Now);
            Quest(s, "daily-matches-" + day, "Сыграй 3 партии", "match", "", "daily", 3, 60, 30, (day + 1) * 86400);
            Quest(s, "daily-win-" + day, "Выиграй партию", "win", "", "daily", 1, 40, 20, (day + 1) * 86400);
            Quest(s, "weekly-durak-" + week, "Сыграй 3 партии в дурака", "match", "durak", "weekly", 3, 100, 50, (week * 7 + 4) * 86400);
            Quest(s, "weekly-chests-" + week, "Собери 10 сундучков за неделю", "chests", "chests", "weekly", 10, 150, 60, (week * 7 + 4) * 86400);
            var earningDays = s.IncomeDays.Where(d => d.Day >= day - 6 && d.Day <= day).ToArray();
            var average = earningDays.Length == 0 ? 0 : earningDays.Sum(d => (long)d.Gross) / earningDays.Length;
            s.Bank.CreditLimit = (int)Math.Min(5000, Math.Max(100, Math.Min(100L + s.Profile.Level * 100L, average * 3)));
            if (s.Bank.Status == "pending" && Now >= s.Bank.ReviewAt)
            {
                var approved = s.Bank.RequestedAmount <= s.Bank.CreditLimit && (s.Profile.Matches >= 2 || s.Bank.RequestedAmount <= s.Bank.CreditLimit / 2);
                if (approved)
                {
                    s.Bank.Principal = s.Bank.RequestedAmount;
                    s.Bank.Debt = checked(s.Bank.Principal + (s.Bank.Principal * s.Bank.InterestPercent + 99) / 100);
                    s.Profile.Coins = checked(s.Profile.Coins + s.Bank.Principal); // Loan principal is not earned XP/income.
                    s.Bank.DueAt = s.Bank.ReviewAt + 3 * 86400;
                    s.Bank.CollectorAt = s.Bank.DueAt + 12 * 3600;
                    s.Bank.Status = "approved"; s.Bank.Decision = "Одобрено " + s.Bank.Principal + " · вернуть " + s.Bank.Debt + " за 3 дня";
                }
                else { s.Bank.Status = "rejected"; s.Bank.Decision = "Отказ: сумма или история игр пока не подходят. Лимит " + s.Bank.CreditLimit; }
            }
            if (s.Bank.Debt > 0 && !s.Bank.CollectionActive && Now >= s.Bank.DueAt)
            {
                s.Bank.Status = "overdue";
                if (Now >= s.Bank.CollectorAt)
                {
                    if (!s.Bank.CollectorPending) s.Bank.CollectorGame = Games[(s.Profile.Matches + s.Profile.Level) % Games.Length];
                    s.Bank.CollectorPending = true; s.Bank.Status = "collection";
                }
            }
        }

        private static void Quest(HubSnapshot s, string id, string name, string kind, string game, string period, int goal, int coins, int xp, long expires)
        {
            if (!s.Quests.Any(q => q.Id == id)) s.Quests.Add(new HubQuest { Id = id, Name = name, Event = kind, Game = game, Period = period, Goal = goal, RewardCoins = coins, RewardXP = xp, ExpiresAt = expires });
        }

        private static void QuestEvent(HubSnapshot s, string kind, string game, int count)
        {
            foreach (var quest in s.Quests.Where(q => q.Event == kind && (q.Game.Length == 0 || q.Game == game))) quest.Progress = Math.Min(quest.Goal, quest.Progress + count);
        }

        private static void Chests(HubSnapshot s, int count)
        {
            Progress(s, "mentalist", count, true);
            QuestEvent(s, "chests", "chests", count);
        }

        private void Credit(HubSnapshot s, int amount)
        {
            Require(amount >= 0, "amount", "Начисление не может быть отрицательным.");
            var withholding = 0;
            if (s.Bank.CollectionActive && s.Bank.Debt > 0)
            {
                var split = (long)amount + s.Bank.IncomeRemainder;
                withholding = (int)Math.Min(s.Bank.Debt, split / 2);
                s.Bank.IncomeRemainder = (int)(split % 2);
                Repay(s, withholding);
            }
            s.Profile.Coins = checked(s.Profile.Coins + amount - withholding);
            s.Profile.CoinsEarned = checked(s.Profile.CoinsEarned + amount);
            var day = Now / 86400;
            var income = s.IncomeDays.FirstOrDefault(d => d.Day == day);
            if (income == null) { income = new HubIncomeDay { Day = day }; s.IncomeDays.Add(income); }
            income.Gross = checked(income.Gross + amount);
            s.IncomeDays.RemoveAll(d => d.Day < day - 30);
        }

        private static void Repay(HubSnapshot s, int amount)
        {
            s.Bank.Debt -= amount;
            s.Bank.Repaid = checked(s.Bank.Repaid + amount);
            if (s.Bank.Debt == 0)
            {
                s.Bank.Status = "paid"; s.Bank.CollectionActive = s.Bank.CollectorPending = false;
                s.Bank.IncomeRemainder = 0; s.Bank.Decision = "Кредит полностью погашен";
            }
            else if (s.Bank.CollectionActive) s.Bank.Status = "repaying";
        }

        private static void Collectors(HubSnapshot s, bool won)
        {
            // Repayment is allowed while a collector table is open. Finishing or leaving it
            // must still release the match, without charging an already-cleared debt again.
            if (s.Bank.Debt == 0) return;
            Require(s.Bank.CollectorPending && s.Bank.Debt > 0, "collector", "Коллекторская партия уже учтена.");
            if (won)
            {
                var forgiven = Math.Max(1, s.Bank.Debt / 4);
                // Never erase the complete debt through a collector victory.
                s.Bank.Debt -= Math.Min(s.Bank.Debt - 1, forgiven);
                s.Bank.Decision = "Победа над коллекторами: списана четверть долга";
            }
            else
            {
                var taken = s.Profile.Coins;
                s.Profile.Coins = 0;
                Repay(s, Math.Min(taken, s.Bank.Debt));
                s.Bank.Decision = "Коллекторы забрали баланс; остаток долга хранится отдельно";
            }
            s.Bank.CollectorPending = false;
            s.Bank.CollectionActive = s.Bank.Debt > 0;
            s.Bank.Status = s.Bank.Debt > 0 ? "repaying" : "paid";
        }

        private static void Experience(HubSnapshot s, int amount)
        {
            s.Profile.XP = checked(s.Profile.XP + amount);
            s.Profile.Level = 1 + s.Profile.XP / 100;
            s.Pass.XP = checked(s.Pass.XP + amount);
            s.Pass.Level = Math.Min(10, 1 + s.Pass.XP / 100);
        }

        private static void Progress(HubSnapshot s, string id, int value, bool maximum = false)
        {
            var achievement = s.Achievements.First(a => a.Id == id);
            achievement.Progress = Math.Min(achievement.Goal, maximum ? Math.Max(achievement.Progress, value) : achievement.Progress + value);
            achievement.Unlocked = achievement.Progress >= achievement.Goal;
        }

        private static string ResolveFriend(HubSnapshot s, string id, bool accept, bool mockOutgoing)
        {
            var request = s.FriendRequests.FirstOrDefault(r => r.Id == id && r.Status == "pending");
            Require(request != null && (mockOutgoing ? request.FromId == s.Profile.Id : request.ToId == s.Profile.Id), "request", "Такая заявка не найдена.");
            request!.Status = accept ? "accepted" : "declined";
            var person = mockOutgoing ? request.ToId : request.FromId;
            if (accept && !s.Friends.Contains(person)) s.Friends.Add(person);
            return accept ? "Друг добавлен локально" : "Заявка отклонена";
        }

        private static string Settings(HubSnapshot s, string key, string text, int amount, bool flag)
        {
            switch (key)
            {
                case "sound": s.Settings.Sound = flag; break;
                case "music": s.Settings.Music = flag; break;
                case "vibration": s.Settings.Vibration = flag; break;
                case "reduced_motion": s.Settings.ReducedMotion = flag; break;
                case "notifications": s.Settings.Notifications = flag; break;
                case "private_profile": s.Settings.PrivateProfile = flag; break;
                case "volume": Require(amount >= 0 && amount <= 100, "volume", "Громкость — от 0 до 100."); s.Settings.Volume = amount; break;
                case "language": Require(text == "ru", "language", "Сейчас доступен русский язык."); s.Settings.Language = text; break;
                default: throw new HubFailure("setting", "Настройка не найдена.");
            }
            return "Настройки сохранены";
        }

        private static HubCatalogItem FindItem(HubSnapshot s, string id)
        {
            var item = s.Catalog.FirstOrDefault(i => i.Id == id);
            Require(item != null, "item", "Предмет не найден."); return item!;
        }

        private static void GrantItem(HubSnapshot s, string id, int count)
        {
            var item = FindItem(s, id);
            var owned = s.Inventory.FirstOrDefault(i => i.ItemId == id);
            if (owned == null) { owned = new HubOwnedItem { ItemId = id, Quantity = 0 }; s.Inventory.Add(owned); }
            owned.Quantity = item.Category == "consumable" ? checked(owned.Quantity + count) : 1;
        }

        private static void Equip(HubSnapshot s, string id)
        {
            var item = FindItem(s, id);
            Require(item.Category != "consumable", "item", "Расходник нужно использовать, а не надевать.");
            Require(s.Inventory.Any(i => i.ItemId == id && i.Quantity > 0), "owned", "Сначала открой предмет.");
            s.Equipped.RemoveAll(e => e.Slot == item.Category);
            s.Equipped.Add(new HubEquipment { Slot = item.Category, ItemId = id });
            if (item.Category == "avatar") s.Profile.AvatarName = id;
        }

        private static HubPerson Person(HubSnapshot s, string id)
        {
            var person = s.People.FirstOrDefault(p => p.Id == id);
            Require(person != null, "person", "Игрок не найден."); return person!;
        }

        private static HubRoom Room(HubSnapshot s)
        {
            var room = s.Rooms.FirstOrDefault(r => r.Id == s.CurrentRoomId);
            Require(room != null && room.Members.Any(m => m.PlayerId == s.Profile.Id), "room", "Сначала войди в комнату."); return room!;
        }

        private static HubRoom EditableRoom(HubSnapshot s)
        {
            var room = Room(s);
            Require(!room.Started, "started", "Нельзя менять стол во время партии.");
            Require(room.HostId == s.Profile.Id || room.Members.All(m => m.IsBot || m.PlayerId == s.Profile.Id), "host", "Настройки меняет хозяин комнаты.");
            return room;
        }

        private static void LeaveRoom(HubSnapshot s)
        {
            var old = s.Rooms.FirstOrDefault(r => r.Id == s.CurrentRoomId);
            if (old != null)
            {
                Require(!old.Started, "started", "Сначала заверши текущую партию.");
                old.Members.RemoveAll(m => m.PlayerId == s.Profile.Id);
                if (old.Members.Count == 0) s.Rooms.Remove(old);
                else if (old.HostId == s.Profile.Id) old.HostId = old.Members[0].PlayerId;
            }
            s.CurrentRoomId = "";
        }

        private static void JoinRoom(HubSnapshot s, string idOrCode)
        {
            var room = s.Rooms.FirstOrDefault(r => r.Id == idOrCode || string.Equals(r.Code, idOrCode, StringComparison.OrdinalIgnoreCase));
            Require(room != null && !room.Started, "room", "Комната не найдена или партия уже началась.");
            if (s.CurrentRoomId == room!.Id) return;
            Require(room.Members.Count < room.MaxPlayers, "full", "Комната заполнена.");
            Require(room.Stake == 0 || s.Bank.Debt == 0, "debt", "Пока есть долг, доступны столы без ставок.");
            LeaveRoom(s);
            room.Members.Add(new HubRoomMember { PlayerId = s.Profile.Id });
            s.CurrentRoomId = room.Id;
        }

        private static void ResetReady(HubRoom room)
        {
            foreach (var member in room.Members) member.Ready = false;
        }

        private static void ValidateGameSettings(string game, HubGameSettings settings, int capacity)
        {
            Require(Games.Contains(game), "game", "Игра не найдена.");
            Require(HubGameRules.DeckSizes(game).Contains(settings.DeckSize), "deck", "Эта колода недоступна в выбранной игре.");
            Require(game == "durak" || !settings.AllowTransfer && !settings.NeighboursOnly && !settings.AllowCheating,
                "rules", "Перевод, подкидывание и жульничество доступны только в дураке.");
            Require(game == "bluff" || !settings.Shootout, "rules", "Перестрелки доступны только в «Верю — не верю».");
            Require(game == "govno" || !settings.Chaos, "rules", "Хаос доступен только в «Говно».");
            var minimum = HubGameRules.MinimumPlayers(game); var maximum = HubGameRules.MaximumPlayers(game, settings);
            Require(capacity >= minimum && capacity <= maximum, "capacity", "Для этих правил нужно от " + minimum + " до " + maximum + " мест.");
        }
        private static string RoomCode(string id)
        {
            unchecked { uint hash = 2166136261; foreach (var c in id) hash = (hash ^ c) * 16777619; return (hash % 1000000).ToString("D6"); }
        }
        private static void Require(bool test, string code, string message) { if (!test) throw new HubFailure(code, message); }
        private sealed class HubFailure : Exception { internal string Code; internal HubFailure(string code, string message) : base(message) { Code = code; } }

        private static string Fingerprint(HubCommand c)
        {
            var original = Part(c.Action) + Part(c.Target) + Part(c.Text) + Part(c.Amount.ToString()) + Part(c.Flag.ToString()) + Part(c.Value.ToString());
            // Keep old receipt identities for commands written before settings were added.
            var rules = c.GameSettings;
            return rules == null ? original : original + Part("game-settings-v1") + Part(rules.DeckSize.ToString())
                + Part(rules.AllowTransfer.ToString()) + Part(rules.NeighboursOnly.ToString())
                + Part(rules.AllowCheating.ToString()) + Part(rules.Shootout.ToString()) + Part(rules.Chaos.ToString());
        }
        private static string Part(string? value) => (value ?? "").Length + ":" + (value ?? "");

        private static T Copy<T>(T value) => (T)CopyValue(value!)!;
        private static object? CopyValue(object? value)
        {
            if (value == null) return null;
            var type = value.GetType();
            if (type.IsPrimitive || type == typeof(string) || type.IsEnum) return value;
            if (value is IList list)
            {
                var copy = (IList)Activator.CreateInstance(type)!;
                foreach (var entry in list) copy.Add(CopyValue(entry));
                return copy;
            }
            var result = Activator.CreateInstance(type)!;
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance)) field.SetValue(result, CopyValue(field.GetValue(value)));
            return result;
        }
    }
}
