using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CardGame.Core.Meta
{
    [Serializable]
    public sealed class HubFortuneState
    {
        public string EffectId = "";
        public long DrawnAt;
        public long ReadyAt;
        public int DrawCount;
        public int SelectedCardIndex = -1;
        public int RemainingMatches;
        public string TargetGame = "";
        public bool Triggered;
        public string LastMatchId = "";
        public int LastCoinsAdjustment;
        public int LastXPAdjustment;
        public string LastPlayedGame = "";
    }

    public sealed class HubFortuneDefinition
    {
        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public string Quote { get; }
        public int Matches { get; }
        public string Resource => "UI/Hub/fortune-card-" + Id;
        internal HubFortuneDefinition(string id, string name, string description, string quote, int matches)
        { Id = id; Name = name; Description = description; Quote = quote; Matches = matches; }
    }

    /// <summary>Local daily prediction. Only the normal completed-match payout is affected.</summary>
    public static class HubFortunes
    {
        public const long Cooldown = 24 * 3600;
        private static readonly string[] Games = { "durak", "bluff", "chests", "govno", "poker" };
        public static readonly IReadOnlyList<HubFortuneDefinition> Definitions = Array.AsReadOnly(new[]
        {
            new HubFortuneDefinition("golden-diamond", "Золотая бубна", "+20% к обычной награде тун-тун коинами за 3 завершённые партии.",
                "Золото любит тех, кто не боится сделать ход.", 3),
            new HubFortuneDefinition("wise-king", "Мудрый король", "+25% к опыту за 3 завершённые партии.",
                "Каждая партия учит. Сегодня ты услышишь больше остальных.", 3),
            new HubFortuneDefinition("helping-hand", "Рука помощи", "+40 тун-тун коинов при следующем поражении в пределах 3 завершённых партий. Ничья не считается поражением.",
                "Даже когда карты против тебя, кто-то протянет руку.", 3),
            new HubFortuneDefinition("black-cat", "Чёрная кошка", "−10% от обычной награды тун-тун коинами за 2 завершённые партии.",
                "Кошка уже перешла дорогу. Пройди её спокойно.", 2),
            new HubFortuneDefinition("stingy-fate", "Скупая судьба", "−20% от обычной награды тун-тун коинами за 1 завершённую партию.",
                "Сегодня судьба считает каждую монету. Это ненадолго.", 1),
            new HubFortuneDefinition("new-road", "Новая дорога", "+60 тун-тун коинов за победу в указанной другой игре в пределах 3 завершённых партий.",
                "Знакомый стол подождёт. Твоя удача свернула на другую улицу.", 3)
        });

        public static HubFortuneDefinition? Find(string? id) => Definitions.FirstOrDefault(card => card.Id == id);
        public static bool Active(HubFortuneState? state) => state != null && state.RemainingMatches > 0 && Find(state.EffectId) != null;
        public static bool CanDraw(HubFortuneState? state, long now) => state == null || Math.Max(0, now) >= state.ReadyAt;
        public static string GameName(string? game)
        {
            switch (game)
            {
                case "durak": return "Дурак";
                case "bluff": return "Верю — не верю";
                case "chests": return "Сундучки";
                case "govno": return "Говно";
                case "poker": return "Покер";
                default: return "игра";
            }
        }
        public static string EffectText(HubFortuneState? state)
        {
            var definition = Find(state?.EffectId);
            if (definition == null) return "Выбери одну из шести карт: предсказание изменит обычные награды за партии.";
            return definition.Id == "new-road"
                ? "+60 тун-тун коинов за победу в игре «" + GameName(state!.TargetGame) + "» в пределах 3 завершённых партий."
                : definition.Description;
        }

        internal static void Refresh(HubSnapshot snapshot)
        {
            if (snapshot.Fortune == null) snapshot.Fortune = new HubFortuneState();
        }

        internal static void Draw(HubSnapshot snapshot, long now, int selected)
        {
            var state = snapshot.Fortune;
            // A deterministic shuffle makes failed persistence and a process restart unable to reroll
            // a chosen slot in the same spread. The slot never directly names a desired effect.
            var seed = Hash(snapshot.Profile.Id + "|" + snapshot.Profile.JoinedAt.ToString(CultureInfo.InvariantCulture)
                + "|" + state.DrawCount.ToString(CultureInfo.InvariantCulture) + "|" + state.ReadyAt.ToString(CultureInfo.InvariantCulture));
            var cards = Enumerable.Range(0, Definitions.Count).ToArray();
            for (var i = cards.Length - 1; i > 0; i--)
            {
                seed = Next(seed); var other = (int)(seed % (uint)(i + 1));
                var value = cards[i]; cards[i] = cards[other]; cards[other] = value;
            }
            var definition = Definitions[cards[selected]];
            state.EffectId = definition.Id; state.DrawnAt = now; state.ReadyAt = checked(now + Cooldown);
            state.DrawCount = checked(state.DrawCount + 1); state.SelectedCardIndex = selected;
            state.RemainingMatches = definition.Matches; state.Triggered = false;
            state.TargetGame = ""; state.LastMatchId = ""; state.LastCoinsAdjustment = state.LastXPAdjustment = 0;
            if (definition.Id == "new-road")
            {
                var last = Games.Contains(state.LastPlayedGame) ? state.LastPlayedGame
                    : snapshot.Matches.LastOrDefault(match => match.Completed && !match.Abandoned && !match.IsCollector)?.Game ?? "durak";
                var alternatives = Games.Where(game => game != last).ToArray();
                state.TargetGame = alternatives[(int)(Next(seed) % (uint)alternatives.Length)];
            }
        }

        internal static HubFortuneReward MatchReward(HubSnapshot snapshot, HubMatchEntry? entry,
            string matchId, string game, bool won, bool draw, int coins, int xp)
        {
            var reward = new HubFortuneReward { Coins = coins, XP = xp };
            var state = snapshot.Fortune;
            state.LastMatchId = matchId; state.LastCoinsAdjustment = state.LastXPAdjustment = 0;
            // Collector settlement is a separate bank event, including legacy zero-wallet rules.
            if (entry == null || entry.IsCollector || entry.Abandoned) return reward;
            state.LastPlayedGame = game;
            if (!Active(state)) return reward;
            state.RemainingMatches--;
            switch (state.EffectId)
            {
                case "golden-diamond": reward.Coins = coins * 120 / 100; break;
                case "wise-king": reward.XP = xp * 125 / 100; break;
                case "black-cat": reward.Coins = coins * 90 / 100; break;
                case "stingy-fate": reward.Coins = coins * 80 / 100; break;
                case "helping-hand":
                    if (!won && !draw) { reward.Coins += 40; state.RemainingMatches = 0; state.Triggered = true; }
                    break;
                case "new-road":
                    if (won && !draw && game == state.TargetGame) { reward.Coins += 60; state.RemainingMatches = 0; state.Triggered = true; }
                    break;
            }
            state.LastCoinsAdjustment = reward.Coins - coins;
            state.LastXPAdjustment = reward.XP - xp;
            reward.EffectName = Find(state.EffectId)!.Name;
            return reward;
        }

        private static uint Next(uint value)
        {
            if (value == 0) value = 0x9e3779b9;
            unchecked { value ^= value << 13; value ^= value >> 17; return value ^ (value << 5); }
        }
        private static uint Hash(string value)
        {
            unchecked { uint hash = 2166136261; foreach (var character in value) hash = (hash ^ character) * 16777619; return hash; }
        }
    }

    internal sealed class HubFortuneReward
    {
        internal int Coins;
        internal int XP;
        internal string EffectName = "";
    }

    public sealed partial class LocalPlayerHubService
    {
        private string DrawFortune(HubSnapshot snapshot, HubCommand command)
        {
            Require(command.Amount >= 0 && command.Amount < HubFortunes.Definitions.Count, "fortune_card", "Выбери одну из шести карт.");
            Require(snapshot.ActiveMatchId.Length == 0, "active_match", "Сначала заверши текущую партию.");
            Require(HubFortunes.CanDraw(snapshot.Fortune, Now), "fortune_cooldown", "Предсказание уже открыто. Следующий бесплатный расклад — через 24 часа после предыдущего.");
            HubFortunes.Draw(snapshot, Now, command.Amount);
            return "Предсказание: «" + HubFortunes.Find(snapshot.Fortune.EffectId)!.Name + "». " + HubFortunes.EffectText(snapshot.Fortune);
        }
    }
}
