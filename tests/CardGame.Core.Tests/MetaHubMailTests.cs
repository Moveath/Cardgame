using CardGame.Core.Meta;

namespace CardGame.Core.Tests;

public sealed partial class MetaHubTests
{
    [Fact]
    public async Task MailContactsMigrateOnceWithoutRestoringRemovedFriendsOrReplacingHistory()
    {
        var f = new Fixture();
        var fresh = (await f.Service.LoadAsync()).Snapshot;
        Assert.Equal(6, fresh.People.Count(p => p.Id.StartsWith("bot-mail-")));
        var legacy = f.Store.Read();
        legacy.People.RemoveAll(p => p.Id.StartsWith("bot-mail-"));
        legacy.Friends.RemoveAll(id => id.StartsWith("bot-mail-"));
        legacy.Chats.RemoveAll(m => m.ConversationId.StartsWith("bot-mail-"));
        var oldIds = legacy.Chats.Select(m => m.Id).ToArray();
        legacy.Profile.Coins = 731;
        f.Store.Write(legacy);
        f.Service = new LocalPlayerHubService(f.Store, f.Clock);
        var migrated = (await f.Service.LoadAsync()).Snapshot;
        Assert.Equal(731, migrated.Profile.Coins);
        Assert.Equal(6, migrated.Friends.Count(id => id.StartsWith("bot-mail-")));
        Assert.All(oldIds, id => Assert.Contains(migrated.Chats, m => m.Id == id));
        Assert.All(migrated.Chats.Where(m => m.ConversationId.StartsWith("bot-mail-")), m => Assert.False(m.Unread));
        var sent = await f.Ok("chat_send", "bot-mail-lera", "Сыграем?");
        Assert.Contains(sent.Chats, m => m.FromId == "bot-mail-lera" && m.Id != "welcome-bot-mail-lera");
        await f.Ok("friend_remove", "bot-mail-lera");
        var beforeReload = f.Store.Read();
        var again = (await new LocalPlayerHubService(f.Store, f.Clock).LoadAsync()).Snapshot;
        Assert.DoesNotContain("bot-mail-lera", again.Friends);
        Assert.Equal(beforeReload.Chats.Count, again.Chats.Count);
        Assert.Equal(6, again.People.Count(p => p.Id.StartsWith("bot-mail-")));
    }
}
