using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CardGame.Core.Meta
{
    public sealed class HubQuestTemplate
    {
        public string Id { get; }
        public string Name { get; }
        public string Event { get; }
        public string Game { get; }
        public string Tier { get; }
        public int Goal { get; }
        public HubQuestTemplate(string id, string name, string kind, string game, int goal, string tier)
        { Id = id; Name = name; Event = kind; Game = game; Goal = goal; Tier = tier; }
    }

    /// <summary>Five persistent slots; completed unclaimed notes survive their deadline.</summary>
    public static class HubQuests
    {
        public static readonly IReadOnlyList<string> Slots = Array.AsReadOnly(new[] { "easy", "medium", "hard", "weekly-0", "weekly-1" });
        public static readonly IReadOnlyList<HubQuestTemplate> Templates = Array.AsReadOnly(new[]
        {
            T("easy-matches", "Сыграй 2 партии", "match", "", 2, "easy"),
            T("easy-durak", "Сыграй партию в дурака", "match", "durak", 1, "easy"),
            T("easy-bluff", "Сыграй партию в блеф", "match", "bluff", 1, "easy"),
            T("easy-govno", "Сыграй партию в «Говно»", "match", "govno", 1, "easy"),
            T("easy-poker", "Сыграй партию в покер", "match", "poker", 1, "easy"),
            T("easy-chests", "Сыграй партию в «Сундучки»", "match", "chests", 1, "easy"),
            T("easy-coins", "Заработай 100 тун-тун коинов в партиях", "match_coins", "", 100, "easy"),
            T("medium-win", "Выиграй партию", "win", "", 1, "medium"),
            T("medium-durak", "Сыграй 3 партии в дурака", "match", "durak", 3, "medium"),
            T("medium-bluff", "Сыграй 3 партии в блеф", "match", "bluff", 3, "medium"),
            T("medium-chests", "Собери 5 сундучков", "chests", "chests", 5, "medium"),
            T("medium-games", "Сыграй в 2 разные игры", "different_games", "", 2, "medium"),
            T("medium-stake", "Сыграй партию со ставкой", "stake", "", 1, "medium"),
            T("medium-coins", "Заработай 300 тун-тун коинов в партиях", "match_coins", "", 300, "medium"),
            T("hard-wins", "Выиграй 3 партии", "win", "", 3, "hard"),
            T("hard-durak", "Выиграй в дурака", "win", "durak", 1, "hard"),
            T("hard-bluff", "Выиграй в блеф", "win", "bluff", 1, "hard"),
            T("hard-govno", "Выиграй в «Говно»", "win", "govno", 1, "hard"),
            T("hard-poker", "Выиграй в покер", "win", "poker", 1, "hard"),
            T("hard-chests", "Собери 10 сундучков", "chests", "chests", 10, "hard"),
            T("hard-streak", "Выиграй 2 партии подряд", "streak", "", 2, "hard"),
            T("hard-matches", "Сыграй 5 партий", "match", "", 5, "hard"),
            T("week-matches", "Сыграй 15 партий", "match", "", 15, "weekly"),
            T("week-wins", "Выиграй 7 партий", "win", "", 7, "weekly"),
            T("week-games", "Сыграй во все 5 игр", "different_games", "", 5, "weekly"),
            T("week-win-games", "Выиграй в 3 разных играх", "different_wins", "", 3, "weekly"),
            T("week-chests", "Собери 30 сундучков", "chests", "chests", 30, "weekly"),
            T("week-coins", "Заработай 1500 тун-тун коинов в партиях", "match_coins", "", 1500, "weekly"),
            T("week-dailies", "Выполни 10 дневных поручений", "daily_claim", "", 10, "weekly"),
            T("week-durak", "Сыграй 10 партий в дурака", "match", "durak", 10, "weekly"),
            T("week-bluff", "Выиграй 5 партий в блеф", "win", "bluff", 5, "weekly"),
            T("week-stake", "Сыграй 5 партий со ставкой", "stake", "", 5, "weekly")
        });
        private static HubQuestTemplate T(string id, string name, string kind, string game, int goal, string tier)
            => new HubQuestTemplate(id, name, kind, game, goal, tier);

        public static int Notifications(HubSnapshot snapshot) => snapshot.Quests.Count(q => !q.Seen || Ready(q));
        public static bool Ready(HubQuest q) => !q.Claimed && q.Goal > 0 && q.Progress >= q.Goal;
        public static string TierName(string tier) => tier == "easy" ? "ЛЁГКОЕ" : tier == "medium" ? "СРЕДНЕЕ" : tier == "hard" ? "СЛОЖНОЕ" : "НА НЕДЕЛЮ";

        public static string ResultHint(HubSnapshot? snapshot)
        {
            var note = snapshot?.Quests.Where(q => !q.Claimed && q.Goal > 0)
                .OrderByDescending(q => q.Progress / (double)q.Goal).ThenBy(q => q.ExpiresAt).FirstOrDefault();
            if (note == null) return "";
            return Ready(note) ? "Поручение выполнено — забери у Шестёрки"
                : "Поручение: " + note.Progress + " / " + note.Goal + " · " + note.Name;
        }

        public static void Refresh(HubSnapshot s, long now, bool force = false)
        {
            if (s.Quests == null) s.Quests = new List<HubQuest>();
            if (s.QuestHistory == null) s.QuestHistory = new List<HubQuestHistory>();
            // Missing Template/Slot is the old four-quest schema. Do not retain its stale notes.
            s.Quests.RemoveAll(q => q == null || !Slots.Contains(q.Slot) || !Templates.Any(t => t.Id == q.Template));
            foreach (var slot in Slots)
            {
                var tier = slot.StartsWith("weekly", StringComparison.Ordinal) ? "weekly" : slot;
                var current = s.Quests.FirstOrDefault(q => q.Slot == slot && q.Tier == tier);
                s.Quests.RemoveAll(q => q.Slot == slot && q != current);
                // Older weekly notes ended on Monday. Preserve their progress and give each its full issued week.
                if (current != null && tier == "weekly") current.ExpiresAt = checked(current.IssuedAt + HubQuest.WeeklySeconds);
                if (!force && current != null && (current.ExpiresAt > now || Ready(current)))
                {
                    // Display copy may change independently of a saved note's progress and rewards.
                    current.Name = Templates.First(t => t.Id == current.Template).Name;
                    if (current.Games == null) current.Games = new List<string>();
                    continue;
                }
                if (current != null) s.Quests.Remove(current);
                var history = s.QuestHistory.FirstOrDefault(h => h.Tier == tier);
                if (history == null) { history = new HubQuestHistory { Tier = tier }; s.QuestHistory.Add(history); }
                if (history.Templates == null) history.Templates = new List<string>();
                var candidates = Templates.Where(t => t.Tier == tier && !history.Templates.TakeLast(HubQuest.HistorySize).Contains(t.Id)
                    && !s.Quests.Any(q => q.Template == t.Id)).ToArray();
                var template = candidates[StableSeed(s.Profile.Id, slot, now) % (uint)candidates.Length];
                var rewards = tier == "easy" ? (HubQuest.EasyCoins, HubQuest.EasyXP)
                    : tier == "medium" ? (HubQuest.MediumCoins, HubQuest.MediumXP)
                    : tier == "hard" ? (HubQuest.HardCoins, HubQuest.HardXP) : (HubQuest.WeeklyCoins, HubQuest.WeeklyXP);
                s.QuestSequence = checked(s.QuestSequence + 1);
                s.Quests.Add(new HubQuest { Id = "quest-" + slot + "-" + now.ToString(CultureInfo.InvariantCulture) + "-" + s.QuestSequence,
                    Template = template.Id, Tier = tier, Slot = slot, IssuedAt = now, Name = template.Name, Event = template.Event,
                    Game = template.Game, Goal = template.Goal, Period = tier == "weekly" ? "weekly" : "daily",
                    RewardCoins = rewards.Item1, RewardXP = rewards.Item2,
                    ExpiresAt = now + (tier == "weekly" ? HubQuest.WeeklySeconds : HubQuest.DailySeconds) });
                history.Templates.Add(template.Id);
                if (history.Templates.Count > HubQuest.HistorySize) history.Templates.RemoveRange(0, history.Templates.Count - HubQuest.HistorySize);
            }
        }

        private static uint StableSeed(string player, string slot, long issued)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (var c in player + "|" + slot + "|" + issued.ToString(CultureInfo.InvariantCulture)) hash = (hash ^ c) * 16777619;
                return hash;
            }
        }

        public static void Event(HubSnapshot s, string kind, string game, int count)
        {
            foreach (var q in s.Quests)
            {
                if (q.Claimed || q.Progress >= q.Goal || (q.Game.Length > 0 && q.Game != game)) continue;
                if (q.Event == "streak")
                {
                    if (kind == "win") q.Progress = q.Streak = Math.Min(q.Goal, q.Streak + 1);
                    else if (kind == "loss") q.Progress = q.Streak = 0;
                }
                else if ((q.Event == "different_games" && kind == "match") || (q.Event == "different_wins" && kind == "win"))
                {
                    if (!q.Games.Contains(game)) q.Games.Add(game);
                    q.Progress = Math.Min(q.Goal, q.Games.Count);
                }
                else if (q.Event == kind) q.Progress = (int)Math.Min(q.Goal, (long)q.Progress + Math.Max(0, count));
            }
        }
    }
}
