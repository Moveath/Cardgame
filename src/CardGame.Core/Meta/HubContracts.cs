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
        public int CaughtCheats;
        public int CheatsPlayed;
        public int TimesCaught;
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
        public List<HubQuestHistory> QuestHistory = new List<HubQuestHistory>();
        public int QuestSequence;
        public HubPass Pass = new HubPass();
        public HubBank Bank = new HubBank();
        public HubBankJobBoard BankJobs = new HubBankJobBoard();
        public HubGift Gift = new HubGift();
        public HubFortuneState Fortune = new HubFortuneState();
        // «Карточный вестник» (киоск на площади, 2026-10-09): номер последнего прочитанного выпуска; выпуск новее — кружок
        // на киоске. 0 — ни одного.
        public int NewsSeen;
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
        // Игра, в которую сыграли за одним столом, если заявка пришла после партии (2026-10-10); иначе пусто.
        public string Game = "";
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
        // Письмо друга, которое игрок ещё не открыл (почта таверны, 2026-10-10); в старых сохранениях — прочитано.
        public bool Unread;
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
        public const long DailySeconds = 8 * 3600, WeeklySeconds = 7 * 86400;
        public const int EasyCoins = 40, EasyXP = 20, MediumCoins = 80, MediumXP = 40,
            HardCoins = 150, HardXP = 70, WeeklyCoins = 300, WeeklyXP = 150, HistorySize = 5;
        public string Id = "";
        public string Template = "";
        public string Tier = "";
        public string Slot = "";
        public long IssuedAt;
        public bool Seen;
        public List<string> Games = new List<string>();
        public int Streak;
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
    public sealed class HubQuestHistory
    {
        public string Tier = "";
        public List<string> Templates = new List<string>();
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
        public string DecisionCode = ""; // approved, limit, history, debt, collector
        public HubLoanOffer? Offer;
        public int OfferSequence;
        public int LastTrickOfferNumber; // Last generated attempt, including a corrected or refused offer.
        public string Trick = ""; // Terms of the active loan; empty means the honest contract.
        public bool OverdueDoubled;
        public int TermsVersion; // 0 in old saves; active legacy loans migrate to 1. New loans use 2.
        public long SignedAt;
        public bool ExtensionUsed;
        public HubLoanContract? Contract;
        public List<HubLoanContract> Contracts = new List<HubLoanContract>();
        public List<HubLoanPayment> Payments = new List<HubLoanPayment>();
        public int PaymentSequence;
    }
    [Serializable]
    public sealed class HubLoanContract
    {
        public string Id = "";
        public int Number;
        public string Borrower = "";
        public long SignedAt;
        public int Principal;
        public int CreditLimit;
        public long OriginalDueAt;
        public int TermsVersion;
        public string Trick = "";
        public string OriginalTrick = ""; // Original attempted clause; Trick contains the agreed terms.
        public bool TrickChallenged;
        public int InterestPercent;
        public int TotalDebt;
        public string Status = "";
    }
    [Serializable]
    public sealed class HubLoanPayment
    {
        public string Id = "";
        public string ContractId = "";
        public int Amount;
        public int PaidBefore;
        public int PaidAfter;
        public int DebtBefore;
        public int DebtAfter;
        public long At;
        public int Discount;
        public bool Early;
        public bool Closed;
        public string Source = "payment"; // payment, income, collectors
    }
    [Serializable]
    public sealed class HubLoanPayoff
    {
        public int Amount;
        public int Discount;
        public bool Early;
    }
    [Serializable]
    public sealed class HubLoanOffer
    {
        public string Id = "";
        public int Number;
        public string Trick = ""; // empty, rate, fee, short; saved daily/double clauses remain supported.
        public string OriginalTrick = "";
        public bool FineRead;
        public bool Revealed; // True only after successfully challenging the specific clause.
    }
    /// <summary>
    /// Подарок Шестёрки (2026-10-07): серебряные тун-тун коины раз в <see cref="Cooldown"/> секунд, отсчёт — от
    /// открытия. Новый профиль может открыть сразу.
    /// </summary>
    [Serializable]
    public sealed class HubGift
    {
        public const int Coins = 50;
        public const long Cooldown = 12 * 3600;
        public long ReadyAt;   // с этой секунды (unix) подарок можно открыть
        public long OpenedAt;  // когда открыт последний раз; 0 — ни разу
        public int Opened;     // сколько раз открыт
        public int LastCoins;  // сколько коинов было в последнем
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
        public string BankJobId = "";
        public string OpponentName = "";
        public string OpponentAvatarId = "";
        public HubGameSettings? GameSettings;
    }
}
