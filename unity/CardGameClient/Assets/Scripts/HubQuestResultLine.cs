using System;
using System.Linq;
using CardGame.Core.Meta;
using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>Result-only hint, refreshed after the authoritative match report completes.</summary>
    public sealed class HubQuestResultLine : MonoBehaviour
    {
        private Text _label;
        private Func<bool> _visible;
        private bool _shown;

        public static void Attach(Text result, Func<bool> visible, float width = 1000)
        {
            var rect = new GameObject("Quest progress after match", typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(result.transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0); rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(0, -24); rect.sizeDelta = new Vector2(width, 36);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = result.font; text.fontSize = 22; text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color(1, .86f, .52f); text.raycastTarget = false; text.supportRichText = false;
            text.resizeTextForBestFit = true; text.resizeTextMinSize = 16; text.resizeTextMaxSize = 22;
            var shadow = text.gameObject.AddComponent<Shadow>(); shadow.effectColor = new Color(0, 0, 0, .8f); shadow.effectDistance = new Vector2(1, -2);
            var line = rect.gameObject.AddComponent<HubQuestResultLine>(); line._label = text; line._visible = visible;
            line.Update(); line.Refresh(PlayerHubRuntime.Current);
        }

        private void OnEnable() { PlayerHubRuntime.Changed += Refresh; Refresh(PlayerHubRuntime.Current); }
        private void OnDisable() { PlayerHubRuntime.Changed -= Refresh; }
        private void Update()
        {
            if (_label == null) return;
            var show = _visible != null && _visible();
            _label.enabled = show;
            if (show && !_shown) Refresh(PlayerHubRuntime.Current);
            _shown = show;
        }
        private static string Signed(int value) => (value < 0 ? "−" : "+") + Math.Abs(value);

        private void Refresh(HubSnapshot snapshot)
        {
            if (_label == null) return;
            _label.text = HubQuests.ResultHint(snapshot);
            _label.color = new Color(1, .86f, .52f);
            var fortune = snapshot?.Fortune;
            var last = snapshot?.Matches.LastOrDefault();
            var definition = HubFortunes.Find(fortune?.EffectId);
            // Never show an earlier payout while the current game is reporting or was abandoned.
            if (definition == null || last == null || !last.Completed || last.Abandoned || last.IsCollector
                || snapshot.ActiveMatchId.Length != 0 || last.Id != fortune.LastMatchId
                || (fortune.LastCoinsAdjustment == 0 && fortune.LastXPAdjustment == 0)) return;
            var change = fortune.LastCoinsAdjustment != 0
                ? Signed(fortune.LastCoinsAdjustment) + " тун-тун коинов"
                : Signed(fortune.LastXPAdjustment) + " опыта";
            _label.text = "«" + definition.Name + "»: " + change;
            var quest = snapshot.Quests.Where(q => !q.Claimed && q.Goal > 0)
                .OrderByDescending(q => q.Progress / (double)q.Goal).ThenBy(q => q.ExpiresAt).FirstOrDefault();
            if (quest != null) _label.text += HubQuests.Ready(quest) ? " · поручение выполнено"
                : " · поручение " + quest.Progress + "/" + quest.Goal;
            if (fortune.LastCoinsAdjustment < 0) _label.color = new Color(1, .65f, .67f);
        }
    }
}
