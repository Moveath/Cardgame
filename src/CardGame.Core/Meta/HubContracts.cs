using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CardGame.Core.Meta
{
    public interface IPlayerHubService
    {
        Task<HubResult> LoadAsync();
        Task<HubResult> ExecuteAsync(HubCommand command);
    }

    public interface IHubStore
    {
        Task<HubSnapshot?> LoadAsync();
        Task SaveAsync(HubSnapshot snapshot);
    }

    public interface IHubClock { long UnixSeconds { get; } }

    [Serializable]
    public sealed class HubCommand
    {
        public string Id = "";
        public string Action = "";
        public string Target = "";
        public string Text = "";
        public int Amount;
        public bool Flag;
        public int Value;
        public HubGameSettings? GameSettings;
    }

    [Serializable]
    public sealed class HubResult
    {
        public bool Success;
        public string Error = "";
        public string Message = "";
        public HubSnapshot Snapshot = new HubSnapshot();
    }

    // Fields, plain lists, and no dictionaries: directly compatible with Unity JsonUtility.
    [Serializable]
    public sealed class HubSnapshot
    {
        public int Version = 1;
        public int Revision;
        public long UpdatedAt;
        public HubProfile Profile = new HubProfile();
        public List<HubCatalogItem> Catalog = new List<HubCatalogItem>();
        public List<HubOwnedItem> Inventory = new List<HubOwnedItem>();
        public List<HubEquipment> Equipped = new List<HubEquipment>();
        public List<HubPerson> People = new List<HubPerson>();
        public List<string> Friends = new List<string>();
        public List<HubFriendRequest> FriendRequests = new List<HubFriendRequest>();
        public List<HubInvite> Invites = new List<HubInvite>();
        public List<HubChatMessage> Chats = new List<HubChatMessage>();
        public List<HubRoom> Rooms = new List<HubRoom>();
        public string CurrentRoomId = "";
        public string ActiveMatchId = "";
        public List<HubAchievement> Achievements = new List<HubAchievement>();
        public List<HubQuest> Quests = new List<HubQuest>();
        public HubPass Pass = new HubPass();
        public HubBank Bank = new HubBank();
        public HubSettings Settings = new HubSettings();
        public HubTutorial Tutorial = new HubTutorial();
        public List<HubReceipt> Receipts = new List<HubReceipt>();
        public List<HubIncomeDay> IncomeDays = new List<HubIncomeDay>();
        public List<HubMatchEntry> Matches = new List<HubMatchEntry>();
    }

    [Serializable]
    public sealed class HubProfile
    {
        public string Id = "local-player";
        public string DisplayName = "Игрок";
        public string AvatarName = "avatar_01";
        public string Bio = "";
        public int Coins;
        public int XP;
        public int Level = 1;
        public int Rating = 1000;
        public int Matches;
        public int Wins;
        public int Losses;
        public int CoinsEarned;
        public int TomatoesThrown;
        public long JoinedAt;
        public long LastTomatoAt;
    }

    [Serializable]
    public sealed class HubCatalogItem
    {
        public string Id = "";
        public string Name = "";
        public string Category = ""; // avatar, frame, cardback, table, background, hat, outfit, consumable
        public string Description = "";
        public string Color = "#D9B765";
        public string Resource = "";
        public string PriceLabel = "Демо · открыть";
        public int Quantity = 1;
        public bool IsDefault;
        public bool IsPremium;
    }

    [Serializable]
    public sealed class HubOwnedItem { public string ItemId = ""; public int Quantity = 1; }
    [Serializable]
    public sealed class HubEquipment { public string Slot = ""; public string ItemId = ""; }
    [Serializable]
    public sealed class HubPerson
    {
        public string Id = "";
        public string Name = "";
        public string AvatarId = "";
        public int Level;
        public int Rating;
        public string Status = "Локальный бот";
        public bool IsBot = true;
    }
    [Serializable]
    public sealed class HubFriendRequest
    {
        public string Id = "";
        public string FromId = "";
        public string ToId = "";
        public string Status = "pending";
        public long CreatedAt;
    }
    [Serializable]
    public sealed class HubInvite
    {
        public string Id = "";
        public string FromId = "";
        public string ToId = "";
        public string RoomId = "";
        public string Status = "pending";
    }
    [Serializable]
    public sealed class HubChatMessage
    {
        public string Id = "";
        public string ConversationId = "";
        public string FromId = "";
        public string Text = "";
        public long CreatedAt;
        public bool IsLocalMock;
    }
    [Serializable]
    public sealed class HubRoom
    {
        public string Id = "";
        public string Code = "";
        public string Name = "";
        public string GameId = "durak";
        public HubGameSettings? GameSettings;
        public string HostId = "";
        public int MaxPlayers = 4;
        public int Stake;
        public bool Private;
        public bool Started;
        public string LocalMatchId = "";
        public List<HubRoomMember> Members = new List<HubRoomMember>();
    }
    [Serializable]
    public sealed class HubRoomMember { public string PlayerId = ""; public bool Ready; public bool IsBot; }
    [Serializable]
    public sealed class HubAchievement
    {
        public string Id = "";
        public string Name = "";
        public string Description = "";
        public int Progress;
        public int Goal = 1;
        public int RewardCoins;
        public int RewardXP;
        public bool Unlocked;
        public bool Claimed;
    }
    [Serializable]
    public sealed class HubQuest
    {
        public string Id = "";
        public string Name = "";
        public string Event = "";
        public string Game = "";
        public string Period = "daily";
        public int Progress;
        public int Goal;
        public int RewardCoins;
        public int RewardXP;
        public long ExpiresAt;
        public bool Claimed;
    }
    [Serializable]
    public sealed class HubPass
    {
        public string Id = "season-local-1";
        public string Name = "Брейнрот пасс · локальный сезон";
        public int XP;
        public int Level = 1;
        public bool Premium;
        public List<HubPassReward> Rewards = new List<HubPassReward>();
    }
    [Serializable]
    public sealed class HubPassReward
    {
        public string Id = "";
        public int Level;
        public string Name = "";
        public bool Premium;
        public int Coins;
        public string ItemId = "";
        public bool Claimed;
    }
    [Serializable]
    public sealed class HubBank
    {
        public int CreditLimit;
        public string Status = "idle"; // idle, pending, approved, rejected, overdue, collection, repaying, paid
        public string RequestId = "";
        public int RequestedAmount;
        public long ReviewAt;
        public int Principal;
        public int Debt;
        public long DueAt;
        public long CollectorAt;
        public bool CollectionActive;
        public bool CollectorPending;
        public string CollectorGame = "durak";
        public int Repaid;
        public int IncomeRemainder;
        public int InterestPercent = 10;
        public string Decision = "Заявок пока нет";
    }
    [Serializable]
    public sealed class HubSettings
    {
        public bool Sound = true;
        public bool Music = true;
        public bool Vibration = true;
        public bool ReducedMotion;
        public bool Notifications = true;
        public bool PrivateProfile;
        public int Volume = 80;
        public string Language = "ru";
    }
    [Serializable]
    public sealed class HubTutorial
    {
        public string ActiveGame = "durak";
        public int Step;
        public bool Completed;
        public bool Skipped;
        public List<string> CompletedGames = new List<string>();
    }
    [Serializable]
    public sealed class HubReceipt { public string Id = ""; public string Fingerprint = ""; public string Message = ""; }
    [Serializable]
    public sealed class HubIncomeDay { public long Day; public int Gross; }
    [Serializable]
    public sealed class HubMatchEntry
    {
        public string Id = "";
        public string Game = "";
        public int Stake;
        public bool Completed;
        public bool Abandoned;
        public bool IsCollector;
    }
}
