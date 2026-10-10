using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CardGame.Core.Meta;
using UnityEngine;

namespace CardGameClient
{
    /// <summary>One replaceable async data service shared by menus and game-result adapters.</summary>
    public static class PlayerHubRuntime
    {
        private static IPlayerHubService _service;
        private static Task<HubResult> _loading;
        private static string _legacyAvatar;
        private static string _matchId;
        private static string _gameId;
        private static Task<HubResult> _startingMatch;
        private static HubCommand _pendingResult;
        private static bool _reporting;
        public static bool HasPendingResult=>_pendingResult!=null;
        private static bool _subscribed;
        private static int _durakCaughtCheats, _durakCheatsPlayed, _durakTimesCaught;
        public static HubSnapshot Current { get; private set; }
        /// <summary>Часы сервиса: таймер подарка в UI считает по ним же, что и сервис.</summary>
        public static IHubClock Clock { get; private set; } = new UnityHubClock();
        public static string LastMessage { get; private set; } = "";
        public static event Action<HubSnapshot> Changed;

        public static string AvatarName => Current?.Profile.AvatarName ?? UnityHubStore.LegacyAvatar;
        public static string DisplayName => Current?.Profile.DisplayName ?? "Игрок";

        public static void UseService(IPlayerHubService service,IHubClock clock=null)
        {
            _service=service;Clock=clock??new UnityHubClock();Current=null;_loading=null;_legacyAvatar=null;
            _matchId=null;_gameId=null;_startingMatch=null;_pendingResult=null;_reporting=false;LastMessage="";
            HubRoster.Clear();ResetDurakEvidence();Subscribe();
        }

        private static void EnsureService()
        {
            if(_service!=null)return;
            var store=new UnityHubStore();
            _legacyAvatar=store.Exists?null:UnityHubStore.LegacyAvatar;
            Clock=new UnityHubClock();
            _service=new LocalPlayerHubService(store,Clock);
            Subscribe();
        }

        private static void Subscribe()
        {
            if(_subscribed)return;_subscribed=true;
            HubMatchReporter.MatchCompleted+=OnMatchCompleted;
            HubMatchReporter.SpiderKilled+=OnSpiderKilled;
            HubMatchReporter.DurakEvidence+=OnDurakEvidence;
        }

        public static Task<HubResult> LoadAsync()
        {
            EnsureService();
            return _loading??(_loading=LoadCoreAsync());
        }

        private static async Task<HubResult> LoadCoreAsync()
        {
            try
            {
                var result=await _service.LoadAsync();
                if(result.Success && !string.IsNullOrEmpty(_legacyAvatar))
                {
                    var migration=await _service.ExecuteAsync(new HubCommand
                    {Id="import-existing-avatar",Action="set_avatar",Target=_legacyAvatar});
                    if(migration.Success)result=migration;
                    _legacyAvatar=null;
                }
                // A process restart cannot resume a live table. Release its room and settle the forfeit once.
                if(result.Success && !string.IsNullOrEmpty(result.Snapshot.ActiveMatchId))
                    result=await _service.ExecuteAsync(new HubCommand{Id="recover:"+result.Snapshot.ActiveMatchId,
                        Action="abandon_match",Target=result.Snapshot.ActiveMatchId});
                Publish(result);return result;
            }
            catch(Exception error){return Failure(error);}
        }

        public static async Task<HubResult> RefreshAsync()
        {
            EnsureService();
            try {var result=await _service.LoadAsync();if(result.Success)_loading=Task.FromResult(result);Publish(result);return result;}
            catch(Exception error){return Failure(error);}
        }

        public static async Task<HubResult> ExecuteAsync(HubCommand command)
        {
            var loaded=await LoadAsync();
            if(!loaded.Success)return loaded;
            if(string.IsNullOrEmpty(command.Id))command.Id=Guid.NewGuid().ToString("N");
            try {var result=await _service.ExecuteAsync(command);Publish(result);return result;}
            catch(Exception error){return Failure(error);}
        }

        public static async Task<bool> SelectAvatarAsync(string name)
        {
            var result=await ExecuteAsync(new HubCommand{Action="set_avatar",Target=name??""});
            return result.Success;
        }

        private static void Publish(HubResult result)
        {
            LastMessage=result.Success?result.Message:ErrorText(result);
            if(!result.Success)return;
            Current=result.Snapshot;
            AudioListener.volume=Current.Settings.Sound?Mathf.Clamp01(Current.Settings.Volume/100f):0;
            var listeners=Changed;
            if(listeners!=null)
                foreach(Action<HubSnapshot> listener in listeners.GetInvocationList())
                    try{listener(Current);}catch(Exception error){Debug.LogException(error);}
        }

        public static string ErrorText(HubResult result)
            =>string.IsNullOrEmpty(result.Message)?result.Error:result.Message;

        private static HubResult Failure(Exception error)
        {
            LastMessage="Не удалось сохранить или загрузить профиль. Попробуй ещё раз.";
            Debug.LogWarning("Local player hub: "+error.Message);
            return new HubResult{Success=false,Error=LastMessage,Snapshot=Current??new HubSnapshot()};
        }

        public static void BeginGame(string gameId,string matchId=null)
        {
            ResetDurakEvidence();
            _gameId=gameId;
            _matchId=string.IsNullOrEmpty(matchId)?Guid.NewGuid().ToString("N"):matchId;
            _startingMatch=string.IsNullOrEmpty(matchId)
                ?ExecuteAsync(new HubCommand{Id=_matchId,Action="begin_match",Target=gameId})
                :null;
        }

        public static async Task<HubResult> BeginGameAsync(string gameId)
        {
            if(HasPendingResult)return new HubResult{Success=false,Error="Сначала сохрани результат предыдущей партии.",Snapshot=Current};
            HubRoster.Clear();
            BeginGame(gameId);
            var result=await _startingMatch;
            if(!result.Success){_matchId=null;_gameId=null;_startingMatch=null;}
            return result;
        }

        public static async Task<bool> ExitGameAsync()
        {
            if(HasPendingResult && !(await RetryPendingResultAsync()).Success)return false;
            var id=_matchId;
            var starting=_startingMatch;_startingMatch=null;
            if(starting!=null)await starting;
            if(!string.IsNullOrEmpty(id) && Current!=null && Current.ActiveMatchId==id)
            {
                var result=await ExecuteAsync(new HubCommand{Action="abandon_match",Target=id});
                if(!result.Success)return false;
            }
            _matchId=null;_gameId=null;HubRoster.Clear();return true;
        }

        private static async void OnMatchCompleted(string gameId,bool won,int chests,bool draw)
        {
            if(_reporting)return;
            if(HasPendingResult){await RetryPendingResultAsync();return;}
            _reporting=true;
            try
            {
                if(_gameId!=gameId || string.IsNullOrEmpty(_matchId))BeginGame(gameId);
                var token=_matchId;
                var starting=_startingMatch;_startingMatch=null;
                if(starting!=null && !(await starting).Success)return;
                _pendingResult=new HubCommand{Id="match-result:"+token,Action="report_match",Target=gameId,
                    Text=token,Flag=won,Value=chests,Amount=draw?1:0,
                    CaughtCheats=gameId=="durak"?_durakCaughtCheats:0,
                    CheatsPlayed=gameId=="durak"?_durakCheatsPlayed:0,
                    TimesCaught=gameId=="durak"?_durakTimesCaught:0};
                await RetryPendingResultAsync();
            }
            finally{_reporting=false;}
        }

        public static async Task<HubResult> RetryPendingResultAsync()
        {
            if(_pendingResult==null)return new HubResult{Success=true,Snapshot=Current};
            var command=_pendingResult;
            var result=await ExecuteAsync(command);
            if(result.Success && _pendingResult==command)
            {_pendingResult=null;if(_matchId==command.Text)_matchId=null;}
            return result;
        }

        private static void ResetDurakEvidence()
        {_durakCaughtCheats=0;_durakCheatsPlayed=0;_durakTimesCaught=0;}

        private static void OnDurakEvidence(int caughtCheats,int cheatsPlayed,int timesCaught)
        {
            if(_gameId!="durak" || string.IsNullOrEmpty(_matchId) || HasPendingResult)return;
            _durakCaughtCheats=Math.Max(_durakCaughtCheats,caughtCheats);
            _durakCheatsPlayed=Math.Max(_durakCheatsPlayed,cheatsPlayed);
            _durakTimesCaught=Math.Max(_durakTimesCaught,timesCaught);
        }

        private static async void OnSpiderKilled()
        {await ExecuteAsync(new HubCommand{Action="spider_killed"});}
    }

    internal sealed class UnityHubClock : IHubClock
    {public long UnixSeconds=>DateTimeOffset.UtcNow.ToUnixTimeSeconds();}

    /// <summary>Development-only local persistence. No networking, credentials or payment data.</summary>
    internal sealed class UnityHubStore : IHubStore
    {
        private readonly string _path=Path.Combine(Application.persistentDataPath,"player-hub-local-v1.json");
        internal bool Exists=>File.Exists(_path);
        internal static string LegacyAvatar
        {
            get
            {
                var name=PlayerPrefs.GetString("player.avatar","yegor_01");
                foreach(var person in new[]{"ali","aslan","danil","marat","yegor","yelnur"})
                    if(name.StartsWith(person+"_",StringComparison.Ordinal))return person+"_01";
                return name;
            }
        }
        public Task<HubSnapshot> LoadAsync()
        {
            if(!Exists)return Task.FromResult<HubSnapshot>(null);
            var json=File.ReadAllText(_path,Encoding.UTF8);
            var snapshot=JsonUtility.FromJson<HubSnapshot>(json);
            if(snapshot==null || snapshot.Profile==null)throw new InvalidDataException("Invalid local profile file");
            return Task.FromResult(snapshot);
        }
        public Task SaveAsync(HubSnapshot snapshot)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            var temporary=_path+".tmp";
            File.WriteAllText(temporary,JsonUtility.ToJson(snapshot,true),new UTF8Encoding(false));
            if(File.Exists(_path))File.Replace(temporary,_path,_path+".bak");
            else File.Move(temporary,_path);
            return Task.CompletedTask;
        }
    }
}
