using System;
using System.Linq;
using CardGame.Core.Cards;
using UnityEngine;

namespace CardGameClient
{
    public sealed partial class PlayerHubController
    {
        private void BuildHubAchievements()
        {
            Title("Достижения", _state.Achievements.Count(entry => entry.Unlocked) + " из " + _state.Achievements.Count + " открыто");
            var entries = _state.Achievements.OrderBy(entry => entry.Claimed).ThenByDescending(entry => entry.Unlocked).ToArray();
            var start = HubPageStart("achievements", entries.Length, 5);
            for (var i = start; i < Math.Min(start + 5, entries.Length); i++)
            {
                var entry = entries[i]; var y = (i - start) * 115;
                Panel("Achievement " + entry.Id, 0, y, 1160, 103);
                Label(entry.Name, 18, y + 8, 760, 32, 25).color = HubPageGold;
                Label(entry.Id == "tutorial_skip" ? "Пропусти обучение с Шестёркой" : entry.Description, 18, y + 44, 755, 31, 19);
                Progress(entry.Progress, Math.Max(1, entry.Goal), 18, y + 84, 650, 10);
                Label(Math.Min(entry.Progress, entry.Goal) + " / " + entry.Goal, 690, y + 73, 110, 28, 17).color = HubPageMuted;
                Label("+" + entry.RewardCoins + " коинов · +" + entry.RewardXP + " XP", 815, y + 8, 325, 29, 18).color = HubPageMuted;
                Button(entry.Claimed ? "Получено" : entry.Unlocked ? "Забрать награду" : "Ещё не открыто", 815, y + 45, 325, 42,
                    () => Send("achievement_claim", entry.Id), entry.Unlocked && !entry.Claimed);
            }
        }

        private void BuildHubQuests()
        {
            Title("Поручения Шестёрки", "Прогресс записывается за реальные партии и действия в локальном профиле");
            HubTabs(new[] { "Все задания", "Дневные", "На неделю" }, new[] { "all", "daily", "weekly" }, _hubQuestKind,
                key => { _hubQuestKind = key; _hubListPages["quests"] = 0; Navigate("quests"); });
            var entries = _state.Quests.Where(entry => _hubQuestKind == "all" || entry.Period == _hubQuestKind).OrderBy(entry => entry.Claimed).ToArray();
            var start = HubPageStart("quests", entries.Length, 4);
            if (entries.Length == 0) HubEmpty("Заданий пока нет", "После обновления локального профиля здесь появятся доступные задания.", 80);
            for (var i = start; i < Math.Min(start + 4, entries.Length); i++)
            {
                var entry = entries[i]; var y = 61 + (i - start) * 128;
                Panel("Quest " + entry.Id, 0, y, 1160, 114);
                Label(entry.Name, 18, y + 10, 770, 35, 24).color = HubPageGold;
                Label(CardGame.Core.Meta.HubQuests.TierName(entry.Tier) + " · " + (entry.Claimed ? "Новое через " : entry.Progress >= entry.Goal ? "Награда ждёт" : "Осталось ") + (entry.Progress >= entry.Goal && !entry.Claimed ? "" : QuestDuration(Math.Max(0, entry.ExpiresAt - GiftNow))), 18, y + 49, 770, 29, 18).color = HubPageMuted;
                Progress(entry.Progress, Math.Max(1, entry.Goal), 18, y + 91, 650, 10);
                Label(Math.Min(entry.Progress, entry.Goal) + " / " + entry.Goal, 690, y + 80, 110, 28, 17).color = HubPageMuted;
                Label("+" + entry.RewardCoins + " коинов · +" + entry.RewardXP + " XP", 815, y + 14, 325, 30, 18).color = HubPageMuted;
                Button(entry.Claimed ? "Получено" : entry.Progress >= entry.Goal ? "Забрать награду" : "В процессе", 815, y + 59, 325, 42,
                    () => Send("quest_claim", entry.Id), !entry.Claimed && entry.Progress >= entry.Goal);
            }
        }

        private void BuildHubPass()
        {
            var pass = _state.Pass;
            Title("Брейнрот пасс", pass.Name + " · награды за локальный прогресс");
            Panel("Pass progress", 0, 0, 1160, 104);
            Label("Уровень " + pass.Level + " · " + HubMoney(pass.XP) + " XP", 20, 18, 630, 39, 29).color = HubPageGold;
            Label("Бесплатная дорожка и косметические награды премиума", 20, 61, 730, 31, 19).color = HubPageMuted;
            Button(pass.Premium ? "Премиум открыт" : "Премиум (локально)", 810, 29, 330, 47, () => Send("pass_premium"), !pass.Premium);
            var rewards = pass.Rewards.OrderBy(entry => entry.Level).ThenBy(entry => entry.Premium).ToArray();
            var start = HubPageStart("pass", rewards.Length, 6);
            for (var i = start; i < Math.Min(start + 6, rewards.Length); i++)
            {
                var entry = rewards[i]; var n = i - start;
                var x = n % 2 * 590; var y = 125 + n / 2 * 150;
                Panel("Pass reward " + entry.Id, x, y, 570, 137);
                Label("Ур. " + entry.Level + " · " + (entry.Premium ? "ПРЕМИУМ" : "БЕСПЛАТНО"), x + 17, y + 9, 536, 27, 17).color = HubPageMuted;
                Label(entry.Name, x + 17, y + 42, 536, 36, 23).color = HubPageGold;
                var available = pass.Level >= entry.Level && (!entry.Premium || pass.Premium);
                Button(entry.Claimed ? "Получено" : available ? "Забрать" : pass.Level < entry.Level ? "Нужен уровень " + entry.Level : "Нужен премиум", x + 17, y + 87, 536, 37,
                    () => Send("pass_claim", entry.Id), available && !entry.Claimed);
            }
            Label("Открытие премиума сейчас бесплатное и локальное. Косметика не меняет правила игр.", 0, 582, 1160, 25, 17).color = HubPageMuted;
        }

        private void BuildHubTutorial()
        {
            Title("Обучение с Шестёркой", "Твой помощник объяснит правила. Затем можно сыграть настоящую партию.");
            _hubLesson = string.IsNullOrEmpty(_state.Tutorial.ActiveGame) ? "durak" : _state.Tutorial.ActiveGame;
            HubTabs(new[] { "Дурак", "Верю — не верю", "Сундучки", "Говно", "Покер" },
                new[] { "durak", "bluff", "chests", "govno", "poker" }, _hubLesson,
                lesson => Send("tutorial_select", lesson));
            var done = _state.Tutorial.CompletedGames.Contains(_hubLesson);
            var step = Mathf.Clamp(_state.Tutorial.Step, 0, 3);
            Panel("Six helper lesson", 0, 65, 1160, 410);
            var badge = CardView.Create(_content, _font, .7f);
            badge.Rect.anchorMin = badge.Rect.anchorMax = new Vector2(0, 1);
            badge.Rect.anchoredPosition = new Vector2(93, -160);
            badge.ShowFace(new Card(Rank.Six, Suit.Spades)); badge.SetPlain();
            Label("ШЕСТЁРКА", 24, 235, 180, 35, 24).color = HubPageGold;
            var lessons = HubLessonSteps(_hubLesson);
            Label(done ? "Урок уже пройден" : "Шаг " + (Math.Min(step, 2) + 1) + " из 3", 240, 86, 885, 42, 27).color = HubPageGold;
            Label(done ? "Ты знаешь основу. Правила всегда можно перечитать здесь." : step >= 3 ? "Всё верно. Теперь закрепим это за настоящим столом." : lessons[Math.Min(step, 2)],
                240, 143, 850, 145, 27);
            Progress(done ? 3 : step, 3, 240, 333, 820, 14);
            if (!done && step < 3)
                Button(HubLessonAction(_hubLesson, step), 240, 380, 820, 55, () => Send("tutorial_next", _hubLesson));
            else if (!done)
                Button("Завершить урок", 240, 380, 820, 55, () => Send("tutorial_complete", _hubLesson));
            else
            {
                Button("Повторить урок",240,380,395,55,()=>Send("tutorial_restart",_hubLesson));
                Button("К играм",655,380,405,55,()=>Navigate("games"));
            }
            Button("Я умею · пропустить", 0, 508, 440, 49, () => Send("tutorial_skip", _hubLesson), !done);
            Button("Задания Шестёрки", 465, 508, 340, 49, () => Navigate("quests"));
            Label(_state.Tutorial.Skipped ? "Шестёрка всё помнит. Достижение «Дохуя умный» можно забрать в достижениях."
                : "При пропуске Шестёрка немного обидится и откроет достижение «Дохуя умный».", 0, 580, 1160, 62, 19).color = HubPageMuted;
        }

        private static string[] HubLessonSteps(string game)
        {
            switch (game)
            {
                case "bluff": return new[] { "Игрок кладёт закрытые карты и объявляет их ранг. Объявление может быть блефом.", "Следующий игрок решает: поверить и продолжить или проверить объявление.", "При проверке карты открываются. Кто ошибся, получает наказание по правилам комнаты." };
                case "chests": return new[] { "Твоя цель — собрать четыре карты одного ранга. Это один сундучок.", "Спрашивай у соперника карты того ранга, который уже есть у тебя.", "Собранное каре уходит в сундук. Запоминай ответы и собирай больше комплектов." };
                case "govno": return new[] { "У каждого четыре карты. Выбери одну и передай соседу слева до конца времени.", "Получай карту справа и собирай четыре одинаковых ранга. Собрал — тапни центр.", "После чужого крика быстро хлопни по центру. Последний теряет жизнь; жизни между раундами сохраняются." };
                case "poker": return new[] { "У тебя две закрытые карты. Пять общих откроются на столе: флоп, тёрн и ривер.", "Есть ставка — можно сбросить, уравнять или повысить. Нет долга — можно чекнуть.", "Выигрывает лучшая пятёрка из семи карт. Олл-ин ставит весь стек; одинаковые руки делят банк." };
                default: return new[] { "Атакующий кладёт карту. Защитник отвечает старшей той же масти или козырем.", "Подкидывать можно ранги, которые уже лежат на столе. Число карт защиты ограничивает атаку.", "Отбился — карты уходят в биту. Не можешь или не хочешь отбиваться — забирай. Освободи руку раньше остальных." };
            }
        }

        private static string HubLessonAction(string game, int step)
        {
            if (game == "durak") return step == 0 ? "Отбить шестёрку девяткой той же масти" : step == 1 ? "Подкинуть уже лежащий ранг" : "Отправить отбитые карты в биту";
            if (game == "poker") return step == 0 ? "Открыть флоп: три общие карты" : step == 1 ? "Чекнуть, если никто не поставил" : "Выбрать лучшие пять карт";
            if (game == "govno") return step == 0 ? "Передать карту влево" : step == 1 ? "Каре! Тапнуть по центру" : "Хлопнуть вслед за криком";
            if (game == "chests") return step == 0 ? "Собрать четыре туза" : step == 1 ? "Спросить туза у соперника" : "Убрать каре в сундук";
            return step == 0 ? "Объявить ранг закрытых карт" : step == 1 ? "Проверить объявление" : "Открыть карты и узнать результат";
        }

        private static string HubGameName(string game)
        {
            switch (game) { case "bluff": return "Верю — не верю"; case "chests": return "Сундучки"; case "govno": return "Говно"; case "poker": return "Покер"; default: return "Дурак"; }
        }
    }
}
