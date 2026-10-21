using System;
using System.Collections.Generic;
using System.Linq;
using CardGame.Core.Meta;
using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    public sealed partial class PlayerHubController
    {
        private readonly Dictionary<string, int> _hubListPages = new Dictionary<string, int>();
        private string _hubFeaturePage;
        private string _hubFriendSearch = "";
        private string _hubInventoryCategory = "all";
        private string _hubQuestKind = "all";
        private string _hubLesson = "durak";
        private string _hubExtra = "arena";
        private static readonly Color HubPageGold = new Color(.92f, .76f, .43f);
        private static readonly Color HubPageMuted = new Color(.77f, .75f, .94f);

        private bool RenderFeaturePage(string page)
        {
            _hubFeaturePage = page;
            // Друзья и карточка игрока — окно таверны «Червовая дама» (PlayerHubTavernFriends.cs).
            if (page.StartsWith("friend:", StringComparison.Ordinal)) { BuildTavernFriends(); return true; }
            // Чаты — почта таверны «Червовая дама» (PlayerHubTavernMail.cs).
            if (page.StartsWith("chat:", StringComparison.Ordinal)) { BuildTavernMail(); return true; }
            if (page.StartsWith("throw:", StringComparison.Ordinal)) { BuildHubThrow(); return true; }
            if (page.StartsWith("place:", StringComparison.Ordinal)) { BuildCityPlace(page.Substring(6)); return true; }
            switch (page)
            {
                case "fortune": BuildFortunePage(); return true;
                case "news": BuildNewsPage(); return true;
                case "profile": BuildHubProfile(); return true;
                case "friends": BuildTavernFriends(); return true;
                case "chats": BuildTavernMail(); return true;
                case "progress": BuildHubProgress(); return true;
                case "inventory": BuildHubInventory(); return true;
                case "achievements": BuildHubAchievements(); return true;
                case "quests": BuildHubQuests(); return true;
                case "pass": BuildHubPass(); return true;
                case "bank": BuildHubBank(); return true;
                case "settings": BuildHubSettings(); return true;
                case "tutorial": BuildHubTutorial(); return true;
                case "ranks": BuildHubRanks(); return true;
                case "extras": BuildHubExtras(); return true;
                default: return false;
            }
        }

        private int HubPageStart(string key, int count, int size, float y = 612)
        {
            var pages = Math.Max(1, (count + size - 1) / size);
            var page = _hubListPages.TryGetValue(key, out var saved) ? Math.Min(saved, pages - 1) : 0;
            page = Math.Max(0, page);
            _hubListPages[key] = page;
            var route = _hubFeaturePage;
            Label((page + 1) + " / " + pages, 528, y + 6, 100, 34, 19).color = HubPageMuted;
            Button("Предыдущая", 340, y, 165, 44, () => { _hubListPages[key] = page - 1; Navigate(route); }, page > 0);
            Button("Следующая", 648, y, 165, 44, () => { _hubListPages[key] = page + 1; Navigate(route); }, page + 1 < pages);
            return page * size;
        }

        private void HubTabs(string[] titles, string[] keys, string selected, Action<string> select, float y = 0)
        {
            var width = (1160f - (titles.Length - 1) * 10f) / titles.Length;
            for (var i = 0; i < titles.Length; i++)
            {
                var key = keys[i];
                var button = Button(titles[i], i * (width + 10), y, width, 44, () => select(key));
                if (key == selected) MarkSelected(button);
            }
        }

        private void HubPanelText(string name, string title, string detail, float x, float y, float width, float height)
        {
            Panel(name, x, y, width, height);
            Label(title, x + 18, y + 12, width - 36, 34, 24).color = HubPageGold;
            Label(detail, x + 18, y + 52, width - 36, height - 62, 19).color = HubPageMuted;
        }

        private void HubEmpty(string title, string detail, float y = 100)
        {
            HubPanelText("Empty list", title, detail, 0, y, 1160, 150);
        }

        private void HubPortrait(Sprite sprite, float x, float y, float width, float height)
        {
            var go = new GameObject("Profile portrait", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(_content, false);
            // Опора в центре: с опорой в углу Unity прижимает узкий портрет к левому краю рамки.
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(x + width / 2, -y - height / 2);
            rect.sizeDelta = new Vector2(width, height);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            if (sprite == null) image.color = new Color(.3f, .25f, .55f, .5f);
        }

        private static string HubMoney(int value) => value.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));
    }
}
