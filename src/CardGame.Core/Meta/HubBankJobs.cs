using System;
using System.Collections.Generic;
using System.Linq;

namespace CardGame.Core.Meta
{
    [Serializable]
    public sealed class HubBankJobBoard
    {
        public List<HubBankJob> Jobs = new List<HubBankJob>();
        public int Sequence;
        public long IssuedAt;
        public long RefreshAt;
        public string ActiveJobId = "";
    }

    [Serializable]
    public sealed class HubBankJob
    {
        public string Id = "";
        public string Template = "";
        public string Name = "";
        public string Description = "";
        public string Quote = "";
        public string Game = "durak";
        public string BotName = "";
        public string AvatarId = "";
        public int Progress;
        public int Goal;
        public int RewardCoins;
        public int RewardXP;
        public long IssuedAt;
        public long ExpiresAt;
        public bool Accepted;
        public bool Claimed;
        public bool Seen;
    }

    /// <summary>Three local bot cases. Accepted work and earned rewards never expire.</summary>
    public static class HubBankJobs
    {
        public const long WeeklySeconds = 7 * 86400;
        public static readonly string[] Templates = { "named_opponent", "catch_cheats", "quiet_win" };
        public static bool Ready(HubBankJob? job) => job != null && !job.Claimed && job.Progress >= job.Goal && job.Goal > 0;
        public static HubBankJob? Active(HubBankJobBoard? board) => board?.Jobs?.FirstOrDefault(j => j.Id == board.ActiveJobId && j.Accepted && !j.Claimed);
        public static int Notifications(HubBankJobBoard? board) => board?.Jobs?.Count(j => Ready(j) || !j.Seen && !j.Claimed) ?? 0;
        public static HubGameSettings Settings(HubBankJob job) => new HubGameSettings
        { DeckSize = 36, AllowCheating = job.Template == "catch_cheats" || job.Template == "quiet_win" };

        public static void Refresh(HubSnapshot snapshot, long now)
        {
            var board = snapshot.BankJobs ?? (snapshot.BankJobs = new HubBankJobBoard());
            if (board.Jobs == null) board.Jobs = new List<HubBankJob>();
            if (board.RefreshAt <= now)
            {
                board.IssuedAt = now;
                board.RefreshAt = checked(now + WeeklySeconds);
            }
            foreach (var template in Templates)
            {
                var current = board.Jobs.FirstOrDefault(j => j.Template == template);
                if (current != null && (Ready(current) || current.Accepted && !current.Claimed || current.ExpiresAt > now)) continue;
                if (current != null) board.Jobs.Remove(current);
                board.Jobs.Add(Create(template, checked(++board.Sequence), now, board.RefreshAt));
            }
            // A completed case occupies the active slot until its reward is collected.
            if (Active(board) == null) board.ActiveJobId = "";
            board.Jobs = board.Jobs.OrderBy(j => Array.IndexOf(Templates, j.Template)).ToList();
        }

        private static HubBankJob Create(string template, int sequence, long now, long expires)
        {
            var job = new HubBankJob { Id = "tuzyara-case-" + sequence, Template = template, IssuedAt = now,
                ExpiresAt = expires, Goal = 1, RewardCoins = 180, RewardXP = 70 };
            if (template == "named_opponent")
            {
                job.Name = "Проучи нахала"; job.BotName = "Феликс Наглый"; job.AvatarId = "avatar_04";
                job.Quote = "Этот тип решил, что за моим столом можно задирать нос. Напомни ему правила приличия.";
                job.Description = "Выиграй у Феликса Наглого в дурака. Стол без ставок и обмана. Открывается кнопкой «К делу».";
            }
            else if (template == "catch_cheats")
            {
                job.Name = "Чужие руки"; job.BotName = "Жора Ловкач"; job.AvatarId = "avatar_07";
                job.Quote = "У Жоры руки быстрые. Мне нужны доказательства. Два точных попадания — и мы в расчёте.";
                job.Description = "Поймай Жору Ловкача на настоящем обмане 2 раза. Можно за несколько законченных партий. Ошибочные обвинения не считаются. Стол без ставок.";
                job.Goal = 2; job.RewardCoins = 250; job.RewardXP = 100;
            }
            else
            {
                job.Name = "Работа без шума"; job.BotName = "Марк Осторожный"; job.AvatarId = "avatar_10";
                job.Quote = "Выиграть мало. Сделай один тихий ход так, чтобы никто не поднял шум. И доведи дело до конца.";
                job.Description = "Выиграй у Марка Осторожного, сыграв хотя бы одну карту с обманом. За всю эту партию тебя ни разу не должны поймать. Стол без ставок.";
                job.RewardCoins = 320; job.RewardXP = 130;
            }
            return job;
        }

        internal static void CompleteMatch(HubSnapshot snapshot, HubMatchEntry? entry, HubCommand result)
        {
            if (entry == null || entry.IsCollector || entry.Abandoned || entry.BankJobId.Length == 0) return;
            var job = Active(snapshot.BankJobs);
            if (job == null || Ready(job) || job.Id != entry.BankJobId || job.Game != entry.Game) return;
            if (job.Template == "named_opponent" && result.Flag && result.Amount == 0) job.Progress = job.Goal;
            else if (job.Template == "catch_cheats" && entry.GameSettings?.AllowCheating == true)
                job.Progress = (int)Math.Min(job.Goal, (long)job.Progress + result.CaughtCheats);
            else if (job.Template == "quiet_win" && entry.GameSettings?.AllowCheating == true
                && result.Flag && result.Amount == 0 && result.CheatsPlayed > 0 && result.TimesCaught == 0)
                job.Progress = job.Goal;
        }
    }

    public sealed partial class LocalPlayerHubService
    {
        private string ApplyBankJob(HubSnapshot snapshot, HubCommand command, string action, string target)
        {
            var board = snapshot.BankJobs;
            if (action == "bank_jobs_seen")
            {
                foreach (var seen in board.Jobs) seen.Seen = true;
                return "Личные поручения просмотрены";
            }
            var job = board.Jobs.FirstOrDefault(j => j.Id == target);
            Require(job != null && !job.Claimed, "bank_job", "Это поручение уже закрыто или обновилось.");
            if (action == "bank_job_accept")
            {
                Require(snapshot.ActiveMatchId.Length == 0, "active_match", "Сначала заверши текущую партию.");
                Require(board.ActiveJobId.Length == 0 || board.ActiveJobId == job!.Id, "bank_job_active", "Сначала закончи принятое поручение.");
                job!.Accepted = job.Seen = true; board.ActiveJobId = job.Id;
                return "Поручение принято · «" + job.Name + "»";
            }
            Require(job!.Accepted && board.ActiveJobId == job.Id, "bank_job", "Сначала прими это поручение.");
            if (action == "bank_job_start")
            {
                Require(!HubBankJobs.Ready(job), "bank_job_ready", "Дело уже сделано. Забери награду у Тузяры.");
                Require(snapshot.ActiveMatchId.Length == 0, "active_match", "Сначала заверши текущую партию.");
                Require(!snapshot.Bank.CollectorPending || HubLoans.Version(snapshot.Bank) >= HubLoans.CurrentTermsVersion,
                    "collector", "Сначала сыграй с коллекторами.");
                var entry = new HubMatchEntry { Id = "tuzyara-match-" + command.Id, Game = job.Game, BankJobId = job.Id,
                    OpponentName = job.BotName, OpponentAvatarId = job.AvatarId, GameSettings = HubBankJobs.Settings(job) };
                snapshot.Matches.Add(entry); snapshot.ActiveMatchId = entry.Id;
                return "Личный стол · «" + job.Name + "»";
            }
            Require(action == "bank_job_claim" && HubBankJobs.Ready(job), "reward", "Поручение ещё не выполнено или награда уже получена.");
            job.Claimed = job.Seen = true; board.ActiveJobId = "";
            Credit(snapshot, job.RewardCoins); Experience(snapshot, job.RewardXP);
            HubBankJobs.Refresh(snapshot, Now);
            return "Дело закрыто. Вот теперь узнаю делового человека.";
        }
    }
}
