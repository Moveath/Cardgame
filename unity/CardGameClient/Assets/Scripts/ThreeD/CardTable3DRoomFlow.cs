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
            Current = new RoomContext
            {
                RoomId = room.Id,
                RoomCode = room.Code,
                PlayerNames = names,
                IsBot = members.Select(member => member.IsBot).ToArray(),
                Settings = (room.GameSettings ?? HubGameSettings.DefaultFor(room.GameId)).Copy(),
                ReturnPage = "room"
            };
            _returnDestination = ReturnDestination.None;
            _returnPage = "";
        }

        public static void BeginQuick(HubSnapshot snapshot, int playerCount, HubGameSettings settings)
        {
            var names = new System.Collections.Generic.List<string> { snapshot?.Profile?.DisplayName ?? PlayerHubRuntime.DisplayName };
            var bots = snapshot?.People?.Where(person => person.IsBot).Select(person => person.Name).ToArray()
                ?? Array.Empty<string>();
            for (var i = 0; names.Count < Mathf.Clamp(playerCount, 2, 6); i++)
                names.Add(i < bots.Length && !string.IsNullOrEmpty(bots[i]) ? bots[i] : "Бот " + (i + 1));
            Current = new RoomContext
            {
                RoomId = "",
                RoomCode = "",
                PlayerNames = names.ToArray(),
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
