using System;
using UnityEngine;

namespace CardGameClient
{
    /// <summary>
    /// Local presentation-to-hub notifications. Controllers own their completion guards;
    /// this facade retains no match, player or scene objects and never awards currency itself.
    /// </summary>
    public static class HubMatchReporter
    {
        public static event Action<string, bool, int, bool> MatchCompleted;
        public static event Action SpiderKilled;

        public static void ReportMatchCompleted(string gameId, bool won, int chestsThisMatch = 0, bool draw = false)
        {
            var listeners = MatchCompleted;
            if (listeners == null) return;
            foreach (Action<string, bool, int, bool> listener in listeners.GetInvocationList())
            {
                try { listener(gameId, won, Math.Max(0, chestsThisMatch), draw); }
                catch (Exception error) { Debug.LogException(error); }
            }
        }

        public static void ReportSpiderKilled()
        {
            var listeners = SpiderKilled;
            if (listeners == null) return;
            foreach (Action listener in listeners.GetInvocationList())
            {
                try { listener(); }
                catch (Exception error) { Debug.LogException(error); }
            }
        }
    }
}
