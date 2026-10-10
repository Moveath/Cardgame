using System;
using System.Collections.Generic;
using CardGame.Core.Meta;
using UnityEngine;

namespace CardGameClient
{
    /// <summary>Copies a room's identities once, so its seats stay the same throughout the match.</summary>
    public static class HubRoster
    {
        private sealed class Roster
        {
            public readonly string GameId;
            public readonly string[] Names;
            public readonly Sprite[] Portraits;

            public Roster(string gameId, List<string> names, List<Sprite> portraits)
            {
                GameId = gameId;
                Names = names.ToArray();
                Portraits = portraits.ToArray();
            }
        }

        private static Roster _current;

        public static void Configure(HubSnapshot snapshot, string roomId)
        {
            Clear();
            if (snapshot == null || snapshot.Profile == null || string.IsNullOrEmpty(roomId)) return;
            var room = snapshot.Rooms.Find(entry => entry.Id == roomId);
            if (room == null || !room.Members.Exists(member => member.PlayerId == snapshot.Profile.Id)) return;

            // Core games keep the local player at index zero even when another member owns the room.
            var names = new List<string> { snapshot.Profile.DisplayName };
            var portraits = new List<Sprite> { AvatarCatalog.Find(snapshot.Profile.AvatarName) };
            foreach (var member in room.Members)
            {
                if (member.PlayerId == snapshot.Profile.Id) continue;
                var person = snapshot.People.Find(entry => entry.Id == member.PlayerId);
                names.Add(person == null ? "Бот" : person.Name);
                portraits.Add(person == null ? null : AvatarCatalog.Find(person.AvatarId));
            }

            _current = new Roster(room.GameId, names, portraits);
        }

        public static void ConfigureBankJob(HubSnapshot snapshot, HubBankJob job)
        {
            Clear();
            if (snapshot == null || job == null) return;
            var match = snapshot.Matches.Find(entry => entry.Id == snapshot.ActiveMatchId && entry.BankJobId == job.Id);
            if (match == null) return;
            _current = new Roster(match.Game,
                new List<string> { snapshot.Profile.DisplayName, match.OpponentName },
                new List<Sprite> { AvatarCatalog.Find(snapshot.Profile.AvatarName), AvatarCatalog.Find(match.OpponentAvatarId) });
        }

        public static void Clear() => _current = null;

        public static bool TryApply(string gameId, IList<string> names, Sprite[] portraits)
        {
            var roster = _current;
            if (roster == null || !string.Equals(roster.GameId, gameId, StringComparison.Ordinal)
                || names == null || portraits == null || names.Count != roster.Names.Length
                || portraits.Length != roster.Portraits.Length) return false;

            for (var index = 0; index < roster.Names.Length; index++)
            {
                names[index] = roster.Names[index];
                portraits[index] = roster.Portraits[index];
            }

            return true;
        }
    }
}
