using System;
using System.Linq;
using System.Threading.Tasks;
using CardGame.Core.Meta;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CardGameClient.ThreeD
{
    /// <summary>Transfers the current local room into the standalone 3D scene and back.</summary>
    public static class CardTable3DRoomFlow
    {
        public enum ReturnDestination { None, Lobby, Home }

        public sealed class RoomContext
        {
            public string RoomId;
            public string RoomCode;
            public string[] PlayerNames;
            public string[] AvatarIds;
            public bool[] IsBot;
            public HubGameSettings Settings;
            public string ReturnPage;
        }

        private static ReturnDestination _returnDestination;
        private static string _returnPage;
        public static RoomContext Current { get; private set; }

        public static void Begin(HubSnapshot snapshot, HubRoom room)
        {
            if (snapshot == null || room == null) throw new ArgumentNullException(snapshot == null ? nameof(snapshot) : nameof(room));
            var members = room.Members.ToList();
            var local = members.FindIndex(member => member.PlayerId == snapshot.Profile.Id);
            if (local > 0)
            {
                var player = members[local];
                members.RemoveAt(local);
                members.Insert(0, player);
            }

            var names = members.Select(member => member.PlayerId == snapshot.Profile.Id
                ? snapshot.Profile.DisplayName
                : snapshot.People.FirstOrDefault(person => person.Id == member.PlayerId)?.Name ?? "Бот").ToArray();
            var avatars = members.Select(member => member.PlayerId == snapshot.Profile.Id
                ? snapshot.Profile.AvatarName
                : snapshot.People.FirstOrDefault(person => person.Id == member.PlayerId)?.AvatarId ?? "").ToArray();
            Current = new RoomContext
            {
                RoomId = room.Id,
                RoomCode = room.Code,
                PlayerNames = names,
                AvatarIds = avatars,
                IsBot = members.Select(member => member.IsBot).ToArray(),
                Settings = (room.GameSettings ?? HubGameSettings.DefaultFor(room.GameId)).Copy(),
                ReturnPage = "room"
            };
            _returnDestination = ReturnDestination.None;
            _returnPage = "";
        }

        public static void BeginQuick(HubSnapshot snapshot, int playerCount, HubGameSettings settings)
        {
            var ownAvatar = !string.IsNullOrEmpty(snapshot?.Profile?.AvatarName)
                ? snapshot.Profile.AvatarName
                : PlayerHubRuntime.AvatarName;
            var names = new System.Collections.Generic.List<string> { snapshot?.Profile?.DisplayName ?? PlayerHubRuntime.DisplayName };
            var avatars = new System.Collections.Generic.List<string> { ownAvatar };
            var ownFriend = AvatarCatalog.PersonalTitleOfId(ownAvatar);
            var eligible = snapshot?.People?
                .Where(person => person.IsBot && (string.IsNullOrEmpty(ownFriend)
                    || !string.Equals(AvatarCatalog.PersonalTitleOfId(person.AvatarId), ownFriend, StringComparison.OrdinalIgnoreCase)))
                .ToList()
                ?? new System.Collections.Generic.List<HubPerson>();
            // Сначала друзья (Али, Аслан, Егор, Ельнур, Марат и т.д. в случайном порядке мест), затем остальные боты.
            var friendBots = eligible.Where(p => !string.IsNullOrEmpty(AvatarCatalog.PersonalTitleOfId(p.AvatarId))).ToList();
            var otherBots = eligible.Where(p => string.IsNullOrEmpty(AvatarCatalog.PersonalTitleOfId(p.AvatarId))).ToList();
            for (var i = friendBots.Count - 1; i > 0; i--)
            {
                var j = UnityEngine.Random.Range(0, i + 1);
                (friendBots[i], friendBots[j]) = (friendBots[j], friendBots[i]);
            }
            var bots = friendBots.Concat(otherBots).ToArray();
            for (var i = 0; names.Count < Mathf.Clamp(playerCount, 2, 6); i++)
            {
                var person = i < bots.Length ? bots[i] : null;
                var avId = person?.AvatarId ?? "";
                avatars.Add(avId);
                var personal = AvatarCatalog.PersonalTitleOfId(avId);
                if (!string.IsNullOrEmpty(personal))
                    names.Add(AvatarCatalog.NameForAvatar(avId, names));
                else if (person != null && !string.IsNullOrEmpty(person.Name))
                    names.Add(person.Name);
                else
                    names.Add(AvatarCatalog.NameForAvatar(avId, names));
            }
            Current = new RoomContext
            {
                RoomId = "",
                RoomCode = "",
                PlayerNames = names.ToArray(),
                AvatarIds = avatars.ToArray(),
                IsBot = names.Select((name, index) => index > 0).ToArray(),
                Settings = (settings ?? HubGameSettings.DefaultFor("durak")).Copy(),
                ReturnPage = "game:durak"
            };
            _returnDestination = ReturnDestination.None;
            _returnPage = "";
        }

        public static ReturnDestination ConsumeReturnDestination(out string returnPage)
        {
            var result = _returnDestination;
            _returnDestination = ReturnDestination.None;
            returnPage = _returnPage;
            _returnPage = "";
            Current = null;
            return result;
        }

        public static async void ReturnToLobby() => await Return(ReturnDestination.Lobby);
        public static async void ReturnToHome() => await Return(ReturnDestination.Home);

        private static async Task Return(ReturnDestination destination)
        {
            if (!await PlayerHubRuntime.ExitGameAsync())
            {
                Debug.LogWarning("Не удалось завершить локальную партию: " + PlayerHubRuntime.LastMessage);
                return;
            }

            if (destination == ReturnDestination.Home && !string.IsNullOrEmpty(PlayerHubRuntime.Current?.CurrentRoomId))
            {
                var result = await PlayerHubRuntime.ExecuteAsync(new HubCommand { Action = "leave_room" });
                if (!result.Success) Debug.LogWarning("Не удалось покинуть локальную комнату: " + PlayerHubRuntime.ErrorText(result));
            }

            _returnDestination = destination;
            _returnPage = Current?.ReturnPage ?? "";
            Current = null;
            SceneManager.LoadScene("SampleScene");
        }
    }
}
