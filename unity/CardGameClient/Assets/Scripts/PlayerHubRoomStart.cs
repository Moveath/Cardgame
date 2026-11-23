using System.Linq;
using System.Threading.Tasks;
using CardGame.Core.Meta;

namespace CardGameClient
{
    public sealed partial class PlayerHubController
    {
        private async void StartDurakRoomWithBots()
        {
            if (_busy) return;
            var room = CurrentRoom();
            if (room == null || room.GameId != "durak" || room.Started) return;

            SetBusy(true);
            while (room.Members.Count < room.MaxPlayers)
            {
                var added = await PlayerHubRuntime.ExecuteAsync(new HubCommand { Action = "add_room_bot" });
                if (this == null) return;
                if (!added.Success)
                {
                    SetBusy(false);
                    Toast(PlayerHubRuntime.ErrorText(added));
                    return;
                }
                _state = added.Snapshot;
                room = CurrentRoom();
                if (room == null) break;
            }

            if (room == null)
            {
                SetBusy(false);
                Toast("Комната больше не найдена.");
                return;
            }

            var self = room.Members.FirstOrDefault(member => member.PlayerId == _state.Profile.Id);
            if (self != null && !self.Ready)
            {
                var ready = await PlayerHubRuntime.ExecuteAsync(new HubCommand { Action = "room_ready", Flag = true });
                if (this == null) return;
                if (!ready.Success)
                {
                    SetBusy(false);
                    Toast(PlayerHubRuntime.ErrorText(ready));
                    return;
                }
                _state = ready.Snapshot;
            }

            SetBusy(false);
            await Dispatch("start_room");
        }
    }
}
