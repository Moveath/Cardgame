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
    public sealed partial class LocalPlayerHubService : IPlayerHubService
    {
        private readonly IHubStore _store;
        private readonly IHubClock _clock;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private HubSnapshot? _snapshot;
        private static readonly string[] Games = { "durak", "bluff", "chests", "govno", "poker" };
        // Ответы локальных ботов в переписке — по очереди (настоящих людей пока нет; IsLocalMock у сообщения).
        private static readonly string[] BotReplies = { "Договорились, сыграем!", "Давай! Жду тебя за столом.", "Сейчас подойду, раздавай." };

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
            if (next.Gift == null) next.Gift = new HubGift(); // Сохранения до подарка Шестёрки.
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
                    var asked = Person(s, target);
                    Require(!s.Friends.Contains(target), "friend", "Вы уже друзья.");
                    Require(!s.FriendRequests.Any(r => r.Status == "pending" && (r.FromId == target || r.ToId == target)), "pending", "Заявка уже ожидает ответа.");
                    var sentRequest = new HubFriendRequest { Id = "friend-" + c.Id, FromId = s.Profile.Id, ToId = target, CreatedAt = Now };
                    s.FriendRequests.Add(sentRequest);
                    if (!asked.IsBot) return "Заявка отправлена";
                    // Живых игроков пока нет: местный бот отвечает сразу (2026-10-10).
                    sentRequest.Status = "accepted"; s.Friends.Add(target);
                    return asked.Name + " принимает заявку — теперь вы друзья";
                case "friend_cancel":
                    var cancelled = s.FriendRequests.FirstOrDefault(r => r.Id == target && r.Status == "pending" && r.FromId == s.Profile.Id);
                    Require(cancelled != null, "request", "Такая заявка не найдена.");
                    cancelled!.Status = "cancelled"; return "Заявка отменена";
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
                    var replies = s.Chats.Count(m => m.ConversationId == target && m.FromId == bot);
                    if (!string.IsNullOrEmpty(bot)) s.Chats.Add(new HubChatMessage { Id = "reply-" + c.Id, ConversationId = target, FromId = bot!, Text = BotReplies[replies % BotReplies.Length], CreatedAt = Now, IsLocalMock = true, Unread = !isRoom });
                    if (s.Chats.Count > 200) s.Chats.RemoveRange(0, s.Chats.Count - 200);
                    return "Сообщение в локальном чате";
                case "create_room": case "room_create":
                    Require(Games.Contains(target), "game", "Выбери доступную игру.");
                    var createSettings = c.GameSettings?.Copy() ?? HubGameSettings.DefaultFor(target);
                    ValidateGameSettings(target, createSettings, c.Amount);
                    Require(c.Value >= 0 && c.Value <= 1000000, "stake", "Недопустимая ставка.");
                    Require(c.Value == 0 || !HubLoans.HasStakeRestriction(s.Bank, Now), "debt", "По условиям действующего кредита сейчас доступны только столы без ставок.");
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
                    Require(c.Amount == 0 || !HubLoans.HasStakeRestriction(s.Bank, Now), "debt", "По условиям действующего кредита сейчас доступны только столы без ставок.");
                    if (stakeRoom.Stake != c.Amount) ResetReady(stakeRoom);
                    stakeRoom.Stake = c.Amount; return "Ставка сохранена";
                case "update_room_settings":
                    var editRoom = EditableRoom(s);
                    Require(editRoom.HostId == s.Profile.Id, "host", "Настройки меняет хозяин комнаты.");
                    var editedSettings = c.GameSettings?.Copy() ?? HubGameSettings.DefaultFor(editRoom.GameId);
                    ValidateGameSettings(editRoom.GameId, editedSettings, c.Amount);
                    Require(c.Amount >= editRoom.Members.Count, "capacity", "Нельзя убрать уже занятые места.");
                    Require(c.Value >= 0 && c.Value <= 1000000, "stake", "Недопустимая ставка.");
                    Require(c.Value == 0 || !HubLoans.HasStakeRestriction(s.Bank, Now), "debt", "По условиям действующего кредита сейчас доступны только столы без ставок.");
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
                    var invite = new HubInvite { Id = "invite-" + c.Id, FromId = s.Profile.Id, ToId = target, RoomId = inviteRoom.Id };
                    s.Invites.Add(invite);
                    var guest = Person(s, target);
                    if (!guest.IsBot) return "Приглашение отправлено";
                    // Местный бот-друг садится за стол сразу.
                    inviteRoom.Members.Add(new HubRoomMember { PlayerId = target, Ready = true, IsBot = true });
                    invite.Status = "accepted"; return guest.Name + " садится за стол";
                case "friend_play": return PlayWithFriend(s, c.Id, target, text);
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
                    Require(!s.Bank.CollectorPending || HubLoans.Version(s.Bank) >= HubLoans.CurrentTermsVersion, "collector", "Сначала сыграй с коллекторами.");
                    Require(c.Amount >= 0 && c.Amount <= s.Profile.Coins, "coins", "Недостаточно коинов для входа.");
                    Require(c.Amount == 0 || !HubLoans.HasStakeRestriction(s.Bank, Now), "debt", "По условиям действующего кредита сейчас доступны только столы без ставок.");
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
                case "bank_jobs_seen": case "bank_job_accept": case "bank_job_start": case "bank_job_claim":
                    return ApplyBankJob(s, c, action, target);
                case "quest_claim":
                    var quest = s.Quests.FirstOrDefault(q => q.Id == target);
                    Require(quest != null && HubQuests.Ready(quest), "reward", "Поручение ещё не выполнено или награда уже получена.");
                    quest!.Claimed = quest.Seen = true;
                    Credit(s, quest.RewardCoins); Experience(s, quest.RewardXP);
                    if (quest.Period == "daily") QuestEvent(s, "daily_claim", "", 1);
                    HubQuests.Refresh(s, Now);
                    return "Награда за поручение получена";
                case "quests_seen":
                    foreach (var note in s.Quests) note.Seen = true;
                    return "Поручения просмотрены";
                case "quests_debug_refresh":
                    HubQuests.Refresh(s, Now, true);
                    return "Тест: новые поручения";
                case "quests_debug_complete":
                    foreach (var note in s.Quests.Where(q => q.Period == "daily" && !q.Claimed)) note.Progress = note.Goal;
                    return "Тест: дневные поручения выполнены";
                case "fortune_draw": return DrawFortune(s, c);
                // ТЕСТ (2026-10-09, по просьбе автора): новая карта у гадалки сразу, без 24 часов. Убрать вместе с кнопкой
                // «Тест: новая карта» (PlayerHubFortune.cs).
                case "fortune_debug_ready": s.Fortune.ReadyAt = Now; return "Тест: можно выбрать новую карту";
                // Переписка открыта — письма друга прочитаны (почта таверны).
                case "chat_read":
                    Require(target.Length > 0, "conversation", "Не выбран разговор.");
                    foreach (var m in s.Chats.Where(m => m.ConversationId == target && m.FromId != s.Profile.Id)) m.Unread = false;
                    return "Письма прочитаны";
                case "news_read":
                    Require(c.Amount > 0, "issue", "Неверный номер выпуска.");
                    s.NewsSeen = Math.Max(s.NewsSeen, c.Amount); return "Выпуск прочитан";
                case "gift_open":
                    Require(Now >= s.Gift.ReadyAt, "gift", "Подарок ещё не готов.");
                    Credit(s, HubGift.Coins);
                    s.Gift.Opened = checked(s.Gift.Opened + 1); s.Gift.LastCoins = HubGift.Coins;
                    s.Gift.OpenedAt = Now; s.Gift.ReadyAt = Now + HubGift.Cooldown;
                    return "Подарок открыт · +" + HubGift.Coins + " коинов";
                // ТЕСТ (2026-10-07, по просьбе автора): подарок готов сразу, без 12 часов. Убрать вместе с кнопкой
                // «Тест: подарок сейчас» (PlayerHubSixGift.cs).
                case "gift_debug_ready": s.Gift.ReadyAt = Now; return "Тест: подарок готов";
                case "pass_premium": s.Pass.Premium = true; return "Премиум-дорожка открыта в демо · без оплаты";
                case "pass_claim":
                    var reward = s.Pass.Rewards.FirstOrDefault(r => r.Id == target);
                    Require(reward != null && !reward.Claimed && reward.Level <= s.Pass.Level && (!reward.Premium || s.Pass.Premium), "reward", "Награда пропуска пока недоступна или уже получена.");
                    reward!.Claimed = true;
                    Require(!reward.Premium || reward.Coins == 0, "premium_currency", "Премиум-дорожка содержит только косметику.");
                    if (reward.Coins > 0) Credit(s, reward.Coins);
                    if (reward.ItemId.Length > 0) GrantItem(s, reward.ItemId, 1);
                    return "Награда пропуска получена";
                case "bank_offer":
                    EnsureLoanOffer(s);
                    return "Договор подготовлен";
                case "bank_read_fine":
                    var fineOffer = CurrentLoanOffer(s, text);
                    fineOffer.FineRead = true;
                    return fineOffer.Revealed ? "Поправка уже внесена · действуют обычные условия"
                        : "Условие прочитано. Если оно расходится с обещанием Тузяры, его можно оспорить.";
                case "bank_challenge_fine":
                    var disputedOffer = CurrentLoanOffer(s, text);
                    if (disputedOffer.Revealed)
                    {
                        Require(target.Length > 0 && target == disputedOffer.OriginalTrick,
                            "fine_print", "Эта поправка относится к другому условию договора.");
                        return "Поправка уже внесена · действуют обычные условия";
                    }
                    Require(disputedOffer.FineRead, "fine_print", "Сначала прочитай условие, которое хочешь оспорить.");
                    Require(disputedOffer.Trick.Length > 0, "fine_print", "В этом договоре обычные условия. Исправлять нечего.");
                    Require(target.Length > 0 && target == disputedOffer.Trick,
                        "fine_print", "В выбранном пункте ошибки нет. Сверь ставку, комиссию и срок с обещанием Тузяры.");
                    disputedOffer.OriginalTrick = disputedOffer.Trick;
                    disputedOffer.Revealed = true; disputedOffer.Trick = "";
                    Progress(s, "fine_print", 1);
                    return "Ладно, тебя не проведёшь. Уберём… техническую неточность. Действуют обычные условия · достижение «Мелкий шрифт» открыто";
                case "bank_sign":
                    var offer = CurrentLoanOffer(s, text);
                    Require(c.Amount >= 100, "amount", "Минимальная сумма кредита — 100 тун-тун коинов.");
                    s.Bank.Offer = null; // A signed decision consumes this contract, including a refusal.
                    return DecideLoan(s, c.Id, c.Amount, offer.Trick, Now, HubLoans.CurrentTermsVersion, offer);
                case "bank_request":
                    // Legacy clients still use this command, but no longer wait thirty seconds.
                    Require(s.Bank.Debt == 0 && s.Bank.Status != "pending", "loan", "Сначала погаси текущий кредит.");
                    Require(c.Amount > 0 && c.Amount <= 1000000, "amount", "Укажи положительную сумму кредита.");
                    var legacyOffer = EnsureLoanOffer(s);
                    s.Bank.Offer = null;
                    return DecideLoan(s, c.Id, c.Amount, legacyOffer.Trick, Now, HubLoans.CurrentTermsVersion, legacyOffer);
                case "bank_review": return s.Bank.Decision;
                case "bank_repay":
                    Require(s.Bank.Debt > 0, "debt", "Задолженности нет.");
                    var quote = HubLoans.PayoffQuote(s.Bank, Now);
                    var payment = c.Amount == 0 ? Math.Min(s.Profile.Coins, quote.Amount) : c.Amount;
                    Require(payment > 0 && payment <= s.Profile.Coins && payment <= quote.Amount,
                        "amount", "Платёж — от 1 тун-тун коина до суммы полного погашения и доступного баланса.");
                    var closesEarly = quote.Early && payment == quote.Amount;
                    s.Profile.Coins -= payment;
                    Repay(s, payment, closesEarly ? quote.Discount : 0, closesEarly, "payment", c.Id);
                    return "Погашено " + payment + " тун-тун коинов" + (closesEarly ? " · досрочно, ставка 10%" : "");
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
            var earnedBefore = s.Profile.CoinsEarned;
            Require(Games.Contains(game), "game", "Игра не найдена.");
            Require(command.Value >= 0 && command.Value <= 13, "count", "Неверное число сундучков.");
            Require(command.Amount == 0 || command.Amount == 1, "outcome", "Неизвестный исход партии.");
            Require(command.CaughtCheats >= 0 && command.CaughtCheats <= 1024 && command.CheatsPlayed >= 0 && command.CheatsPlayed <= 1024
                && command.TimesCaught >= 0 && command.TimesCaught <= command.CheatsPlayed, "count", "Неверная статистика обмана.");
            var draw = command.Amount == 1;
            if (entryId.Length == 0 && s.ActiveMatchId.Length > 0) entryId = s.ActiveMatchId;
            var entry = entryId.Length == 0 ? null : s.Matches.FirstOrDefault(m => m.Id == entryId);
            Require(entryId.Length == 0 || entry != null, "match", "Партия не найдена.");
            var collectorLoss = entry != null && entry.IsCollector && !draw && !command.Flag;
            var legacyCollectorLoss = collectorLoss && HubLoans.Version(s.Bank) < HubLoans.CurrentTermsVersion;
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
                AskFriendshipAfter(s, entry);
            }
            HubBankJobs.CompleteMatch(s, entry, command);
            s.Profile.Matches = checked(s.Profile.Matches + 1);
            if (!draw && command.Flag) { s.Profile.Wins++; s.Profile.Rating = checked(s.Profile.Rating + 20); Progress(s, "first_win", 1); }
            else if (!draw) { s.Profile.Losses++; s.Profile.Rating = Math.Max(0, s.Profile.Rating - 10); }
            // Only legacy signed contracts zero the wallet and suppress this participation payout.
            var ordinaryReward = HubFortunes.MatchReward(s, entry, entry?.Id ?? command.Id, game, command.Flag, draw,
                legacyCollectorLoss ? 0 : !draw && command.Flag ? 80 : 20, !draw && command.Flag ? 40 : 20);
            if (!legacyCollectorLoss) Credit(s, ordinaryReward.Coins);
            Experience(s, ordinaryReward.XP);
            Progress(s, "ten_matches", s.Profile.Matches, true);
            QuestEvent(s, "match", game, 1);
            if (!draw && command.Flag) QuestEvent(s, "win", game, 1);
            else if (!draw) QuestEvent(s, "loss", game, 1);
            if (entry != null && entry.Stake > 0) QuestEvent(s, "stake", game, 1);
            // Only this match's gross payout, including a won stake; no gifts, claims or returned draw stake.
            QuestEvent(s, "match_coins", game, s.Profile.CoinsEarned - earnedBefore);
            if (game == "chests")
            {
                Chests(s, command.Value);
                // Только за доигранную партию: брошенная закрывается раньше и сюда не доходит.
                if (command.Value == 0) Progress(s, "pendalist", 1);
            }
            var rewardText = "+" + ordinaryReward.Coins + " тун-тун коинов и " + ordinaryReward.XP + " опыта";
            var fortuneText = ordinaryReward.EffectName.Length > 0 ? " · предсказание «" + ordinaryReward.EffectName + "»" : "";
            return legacyCollectorLoss ? "Партия с коллекторами учтена · +20 опыта, без награды тун-тун коинами"
                : (draw ? "Ничья · ставка возвращена, " + rewardText + " за участие"
                : (command.Flag ? "Победа · " : "Партия учтена · ") + rewardText + " до удержания долга") + fortuneText;
        }

        /// <summary>
        /// «Позвать играть» из окна друзей: свой закрытый стол выбранной игры без ставки, друг садится сразу (местный бот),
        /// в играх от трёх свободное место до минимума занимает местный бот — можно начинать.
        /// </summary>
        private string PlayWithFriend(HubSnapshot s, string commandId, string friendId, string game)
        {
            var friend = Person(s, friendId);
            Require(s.Friends.Contains(friendId), "friend", "Позвать можно друга.");
            Require(Games.Contains(game), "game", "Выбери игру.");
            Require(s.ActiveMatchId.Length == 0, "active_match", "Сначала заверши текущую партию.");
            var settings = HubGameSettings.DefaultFor(game);
            var minimum = HubGameRules.MinimumPlayers(game);
            var seats = Math.Min(HubGameRules.MaximumPlayers(game, settings), Math.Max(minimum, 4));
            LeaveRoom(s);
            var room = new HubRoom { Id = "room-" + commandId, Code = RoomCode(commandId), Name = "Стол " + s.Profile.DisplayName, GameId = game,
                GameSettings = settings, HostId = s.Profile.Id, MaxPlayers = seats, Private = true };
            room.Members.Add(new HubRoomMember { PlayerId = s.Profile.Id });
            s.Invites.Add(new HubInvite { Id = "invite-" + commandId, FromId = s.Profile.Id, ToId = friendId, RoomId = room.Id,
                Status = friend.IsBot ? "accepted" : "pending" });
            if (friend.IsBot) room.Members.Add(new HubRoomMember { PlayerId = friendId, Ready = true, IsBot = true });
            foreach (var bot in s.People.Where(p => p.IsBot && room.Members.All(m => m.PlayerId != p.Id)).OrderBy(p => s.Friends.Contains(p.Id))
                         .Take(Math.Max(0, minimum - room.Members.Count)).ToList())
                room.Members.Add(new HubRoomMember { PlayerId = bot.Id, Ready = true, IsBot = true });
            s.Rooms.Add(room); s.CurrentRoomId = room.Id;
            return friend.IsBot ? friend.Name + " садится за стол" : "Приглашение отправлено";
        }

        /// <summary>Сыграли за одним столом — местный бот, ещё не друг и не отказавший, просится в друзья (одна заявка за партию).</summary>
        private void AskFriendshipAfter(HubSnapshot s, HubMatchEntry entry)
        {
            var room = s.Rooms.FirstOrDefault(r => r.LocalMatchId == entry.Id);
            var mate = room?.Members.Select(m => m.PlayerId).FirstOrDefault(id => id != s.Profile.Id && !s.Friends.Contains(id)
                && s.People.Any(p => p.Id == id && p.IsBot)
                && !s.FriendRequests.Any(r => (r.FromId == id || r.ToId == id) && (r.Status == "pending" || r.Status == "declined")));
            if (mate != null)
                s.FriendRequests.Add(new HubFriendRequest { Id = "request-" + entry.Id, FromId = mate, ToId = s.Profile.Id, CreatedAt = Now, Game = entry.Game });
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
            Require(!s.Bank.CollectorPending || HubLoans.Version(s.Bank) >= HubLoans.CurrentTermsVersion, "collector", "Сначала сыграй с коллекторами.");
            Require(room.Stake == 0 || !HubLoans.HasStakeRestriction(s.Bank, Now), "debt", "По условиям действующего кредита сейчас доступны только столы без ставок.");
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
            QuestEvent(s, "loss", entry.Game, 1);
            // Leaving the collectors' game is a loss, so restarting cannot cancel the debt event.
            if (entry.IsCollector) Collectors(s, false);
            if (s.ActiveMatchId == id) s.ActiveMatchId = "";
            foreach (var room in s.Rooms.Where(r => r.LocalMatchId == id)) { room.Started = false; room.Members.Where(m => !m.IsBot).ToList().ForEach(m => m.Ready = false); }
        }

        private void Refresh(HubSnapshot s)
        {
            var day = Now / 86400;
            HubQuests.Refresh(s, Now);
            HubBankJobs.Refresh(s, Now);
            HubFortunes.Refresh(s);
            var earningDays = s.IncomeDays.Where(d => d.Day >= day - 6 && d.Day <= day).ToArray();
            var average = earningDays.Length == 0 ? 0 : earningDays.Sum(d => (long)d.Gross) / earningDays.Length;
            s.Bank.CreditLimit = (int)Math.Min(5000, Math.Max(100, Math.Min(100L + s.Profile.Level * 100L, average * 3)));
            NormalizeLoan(s);
            if (s.Bank.Status == "pending")
            {
                var issuedAt = s.Bank.ReviewAt > 0 ? Math.Min(Now, s.Bank.ReviewAt) : Now;
                // A previously pending request keeps the old printed conditions.
                DecideLoan(s, s.Bank.RequestId, s.Bank.RequestedAmount, "", issuedAt, HubLoans.LegacyTermsVersion, null);
            }
            var bank = s.Bank;
            if (bank.Debt > 0 && bank.Trick == "double" && !bank.OverdueDoubled && Now >= bank.DueAt)
            {
                bank.Debt = checked(bank.Debt * 2); bank.OverdueDoubled = true;
            }
            if (bank.Debt > 0 && !bank.CollectionActive && Now >= bank.DueAt)
            {
                if (HubLoans.Version(bank) >= HubLoans.CurrentTermsVersion && bank.ExtensionUsed)
                {
                    bank.CollectionActive = true; bank.CollectorPending = false; bank.Status = "repaying";
                    bank.Decision = "Продление закончилось · половина будущих доходов идёт в погашение";
                }
                else
                {
                    bank.Status = "overdue";
                    if (Now >= bank.CollectorAt)
                    {
                        if (!bank.CollectorPending) bank.CollectorGame = Games[(s.Profile.Matches + s.Profile.Level) % Games.Length];
                        bank.CollectorPending = true; bank.Status = "collection";
                    }
                }
            }
            SyncLoanStatus(bank);
        }

        private static void NormalizeLoan(HubSnapshot s)
        {
            var bank = s.Bank;
            if (bank.Offer != null && !string.IsNullOrWhiteSpace(bank.Offer.Id))
            {
                if (string.IsNullOrEmpty(bank.Offer.OriginalTrick) && !string.IsNullOrEmpty(bank.Offer.Trick))
                    bank.Offer.OriginalTrick = bank.Offer.Trick;
                if (bank.Offer.Revealed) bank.Offer.FineRead = true;
                if (!string.IsNullOrEmpty(bank.Offer.OriginalTrick) || bank.Offer.Revealed)
                    bank.LastTrickOfferNumber = Math.Max(bank.LastTrickOfferNumber, bank.Offer.Number);
            }
            if (bank.Contracts == null) bank.Contracts = new List<HubLoanContract>();
            if (bank.Payments == null) bank.Payments = new List<HubLoanPayment>();
            if (bank.Contract != null && string.IsNullOrWhiteSpace(bank.Contract.Id)) bank.Contract = null;
            if (bank.TermsVersion == 0 && (bank.Debt > 0 || bank.Principal > 0 || bank.Status == "pending")) bank.TermsVersion = HubLoans.LegacyTermsVersion;
            if (bank.Contract == null && (bank.Debt > 0 || bank.Principal > 0))
            {
                bank.SignedAt = bank.ReviewAt > 0 ? bank.ReviewAt : Math.Max(0, bank.DueAt - HubLoans.Days(bank.Trick) * 86400L);
                bank.Contract = new HubLoanContract
                {
                    Id = "legacy-loan-" + (string.IsNullOrEmpty(bank.RequestId) ? bank.SignedAt + "-" + bank.Principal : bank.RequestId),
                    Number = 0, Borrower = s.Profile.DisplayName, SignedAt = bank.SignedAt, Principal = bank.Principal,
                    CreditLimit = bank.CreditLimit, OriginalDueAt = bank.DueAt, TermsVersion = HubLoans.Version(bank), Trick = bank.Trick, OriginalTrick = bank.Trick,
                    InterestPercent = bank.InterestPercent, TotalDebt = bank.Principal > 0 ? HubLoans.Debt(bank.Principal, bank.Trick, HubLoans.Version(bank)) : checked(bank.Debt + bank.Repaid),
                    Status = bank.Status
                };
            }
            if (bank.Contract != null && string.IsNullOrEmpty(bank.Contract.OriginalTrick) && !string.IsNullOrEmpty(bank.Contract.Trick))
                bank.Contract.OriginalTrick = bank.Contract.Trick;
            if (bank.Contract != null && !bank.Contracts.Any(c => c.Id == bank.Contract.Id)) bank.Contracts.Add(Copy(bank.Contract));
            foreach (var contract in bank.Contracts)
            {
                if (string.IsNullOrEmpty(contract.OriginalTrick) && !string.IsNullOrEmpty(contract.Trick))
                    contract.OriginalTrick = contract.Trick;
                if (!string.IsNullOrEmpty(contract.OriginalTrick))
                    bank.LastTrickOfferNumber = Math.Max(bank.LastTrickOfferNumber, contract.Number);
            }
            if (bank.Contracts.Count > 10) bank.Contracts.RemoveRange(0, bank.Contracts.Count - 10);
            if (bank.Payments.Count > 10) bank.Payments.RemoveRange(0, bank.Payments.Count - 10);
        }

        private static void SyncLoanStatus(HubBank bank)
        {
            if (bank.Contract == null || bank.Debt == 0 && bank.Status != "paid") return;
            bank.Contract.Status = bank.Status;
            var archived = bank.Contracts.FirstOrDefault(c => c.Id == bank.Contract.Id);
            if (archived != null) archived.Status = bank.Status;
        }

        private static HubLoanOffer EnsureLoanOffer(HubSnapshot s)
        {
            // Unity JsonUtility materializes a serialized null nested object as an empty offer.
            // The identity, not object presence, distinguishes an issued contract from an empty slot.
            if (s.Bank.Offer == null || string.IsNullOrWhiteSpace(s.Bank.Offer.Id) || s.Bank.Offer.Number <= 0)
            {
                s.Bank.OfferSequence = checked(s.Bank.OfferSequence + 1);
                s.Bank.Offer = HubLoans.Offer(s.Profile.Id, s.Bank.OfferSequence, s.Bank.LastTrickOfferNumber);
                if (s.Bank.Offer.Trick.Length > 0) s.Bank.LastTrickOfferNumber = s.Bank.OfferSequence;
            }
            return s.Bank.Offer;
        }

        private static HubLoanOffer CurrentLoanOffer(HubSnapshot s, string id)
        {
            Require(id.Length > 0 && s.Bank.Offer != null && s.Bank.Offer.Id == id,
                "offer", "Этот договор уже закрыт. Возьми новый у Тузяры.");
            return s.Bank.Offer!;
        }

        private static string DecideLoan(HubSnapshot s, string requestId, int amount, string trick, long issuedAt, int version, HubLoanOffer? offer)
        {
            var bank = s.Bank;
            bank.RequestId = requestId; bank.RequestedAmount = amount;
            var refusal = bank.CollectorPending ? "collector" : bank.Debt > 0 ? "debt"
                : amount <= 0 || amount > bank.CreditLimit ? "limit"
                : s.Profile.Matches < 2 && amount > bank.CreditLimit / 2 ? "history" : "";
            if (refusal.Length > 0)
            {
                bank.DecisionCode = refusal;
                if (bank.Debt == 0) bank.Status = "rejected";
                bank.Decision = refusal == "collector" ? "Ребята тебя ждут. Сначала с ними."
                    : refusal == "debt" ? "Сначала старый долг, потом поговорим."
                    : refusal == "history" ? "Сначала поиграй. Половину лимита — дам."
                    : "Губу закатай. Лимит — " + bank.CreditLimit + " тун-тун коинов.";
                return bank.Decision;
            }
            bank.Principal = amount; bank.Debt = HubLoans.Debt(amount, trick, version);
            s.Profile.Coins = checked(s.Profile.Coins + amount); // Principal is never earned income or XP.
            bank.Trick = trick; bank.InterestPercent = HubLoans.InterestPercent(trick, version);
            bank.TermsVersion = version; bank.SignedAt = issuedAt; bank.ExtensionUsed = false;
            bank.DueAt = checked(issuedAt + HubLoans.Days(trick) * 86400L);
            bank.CollectorAt = checked(bank.DueAt + (version >= HubLoans.CurrentTermsVersion ? HubLoans.CollectorDelay : HubLoans.LegacyCollectorDelay));
            bank.ReviewAt = issuedAt;
            bank.Repaid = bank.IncomeRemainder = 0;
            bank.OverdueDoubled = bank.CollectionActive = bank.CollectorPending = false;
            bank.Status = bank.DecisionCode = "approved";
            bank.Contract = new HubLoanContract
            {
                Id = offer?.Id ?? "legacy-loan-" + requestId, Number = offer?.Number ?? 0,
                Borrower = s.Profile.DisplayName, SignedAt = issuedAt, Principal = amount, CreditLimit = bank.CreditLimit,
                OriginalDueAt = bank.DueAt, TermsVersion = version, Trick = trick, InterestPercent = bank.InterestPercent,
                OriginalTrick = offer?.OriginalTrick ?? trick, TrickChallenged = offer?.Revealed ?? false,
                TotalDebt = bank.Debt, Status = bank.Status
            };
            bank.Contracts.Add(Copy(bank.Contract));
            if (bank.Contracts.Count > 10) bank.Contracts.RemoveAt(0);
            bank.Decision = "Одобрено " + amount + " · вернуть " + bank.Debt + " за " + HubLoans.Days(trick) + " дня";
            return bank.Decision;
        }

        private static void QuestEvent(HubSnapshot s, string kind, string game, int count)
            => HubQuests.Event(s, kind, game, count);

        private static void Chests(HubSnapshot s, int count)
        {
            Progress(s, "mentalist", count, true);
            QuestEvent(s, "chests", "chests", count);
        }

        private void Credit(HubSnapshot s, int amount)
        {
            Require(amount >= 0, "amount", "Начисление не может быть отрицательным.");
            var withholding = 0;
            if (HubLoans.WithholdsIncome(s.Bank))
            {
                var split = (long)amount + s.Bank.IncomeRemainder;
                withholding = (int)Math.Min(s.Bank.Debt, split / 2);
                s.Bank.IncomeRemainder = (int)(split % 2);
                if (withholding > 0) Repay(s, withholding, source: "income");
            }
            s.Profile.Coins = checked(s.Profile.Coins + amount - withholding);
            s.Profile.CoinsEarned = checked(s.Profile.CoinsEarned + amount);
            var day = Now / 86400;
            var income = s.IncomeDays.FirstOrDefault(d => d.Day == day);
            if (income == null) { income = new HubIncomeDay { Day = day }; s.IncomeDays.Add(income); }
            income.Gross = checked(income.Gross + amount);
            s.IncomeDays.RemoveAll(d => d.Day < day - 30);
        }

        private void Repay(HubSnapshot s, int amount, int discount = 0, bool early = false, string source = "payment", string id = "")
        {
            var bank = s.Bank;
            var receipt = new HubLoanPayment
            {
                Id = string.IsNullOrEmpty(id) ? "loan-payment-" + checked(++bank.PaymentSequence) : id,
                ContractId = bank.Contract?.Id ?? "", Amount = amount, PaidBefore = bank.Repaid,
                DebtBefore = bank.Debt, At = Now, Discount = discount, Early = early, Source = source
            };
            bank.Debt = checked(bank.Debt - amount - discount);
            bank.Repaid = checked(bank.Repaid + amount);
            if (bank.Debt == 0)
            {
                bank.Status = "paid"; bank.CollectionActive = bank.CollectorPending = false;
                bank.IncomeRemainder = 0; bank.Decision = "Кредит полностью погашен";
            }
            else if (bank.CollectionActive) bank.Status = "repaying";
            receipt.PaidAfter = bank.Repaid; receipt.DebtAfter = bank.Debt; receipt.Closed = bank.Debt == 0;
            bank.Payments.Add(receipt);
            if (bank.Payments.Count > 10) bank.Payments.RemoveAt(0);
            SyncLoanStatus(bank);
        }

        private void Collectors(HubSnapshot s, bool won)
        {
            var bank = s.Bank;
            if (bank.Debt == 0) return;
            Require(bank.CollectorPending, "collector", "Коллекторская партия уже учтена.");
            if (HubLoans.Version(bank) >= HubLoans.CurrentTermsVersion)
            {
                bank.CollectorPending = false;
                if (won && !bank.ExtensionUsed)
                {
                    bank.ExtensionUsed = true; bank.CollectionActive = false;
                    bank.DueAt = checked(Now + HubLoans.ExtensionSeconds); bank.CollectorAt = bank.DueAt;
                    bank.Status = "extended"; bank.Decision = "Победа над коллекторами · срок продлён на одни сутки";
                }
                else
                {
                    bank.CollectionActive = true; bank.Status = "repaying";
                    bank.Decision = "Половина будущих доходов идёт в погашение · текущий баланс сохранён";
                }
                SyncLoanStatus(bank);
                return;
            }
            // Previously signed v1 contracts retain forgiveness and the wallet seizure.
            if (won)
            {
                var forgiven = Math.Max(1, bank.Debt / 4);
                bank.Debt -= Math.Min(bank.Debt - 1, forgiven);
                bank.Decision = "Победа над коллекторами: списана четверть долга";
            }
            else
            {
                var taken = s.Profile.Coins; s.Profile.Coins = 0;
                if (taken > 0) Repay(s, Math.Min(taken, bank.Debt), source: "collectors");
                bank.Decision = "Коллекторы забрали баланс; остаток долга хранится отдельно";
            }
            bank.CollectorPending = false; bank.CollectionActive = bank.Debt > 0;
            bank.Status = bank.Debt > 0 ? "repaying" : "paid";
            SyncLoanStatus(bank);
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

        private void JoinRoom(HubSnapshot s, string idOrCode)
        {
            var room = s.Rooms.FirstOrDefault(r => r.Id == idOrCode || string.Equals(r.Code, idOrCode, StringComparison.OrdinalIgnoreCase));
            Require(room != null && !room.Started, "room", "Комната не найдена или партия уже началась.");
            if (s.CurrentRoomId == room!.Id) return;
            Require(room.Members.Count < room.MaxPlayers, "full", "Комната заполнена.");
            Require(room.Stake == 0 || !HubLoans.HasStakeRestriction(s.Bank, Now), "debt", "По условиям действующего кредита сейчас доступны только столы без ставок.");
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
            if (c.CaughtCheats != 0 || c.CheatsPlayed != 0 || c.TimesCaught != 0)
                original += Part("durak-evidence") + Part(c.CaughtCheats.ToString()) + Part(c.CheatsPlayed.ToString()) + Part(c.TimesCaught.ToString());
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
