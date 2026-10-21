using CardGame.Core.Meta;

namespace CardGame.Core.Tests;

public sealed partial class MetaHubTests
{
    [Fact]
    public async Task ChronicleOpensChaptersByLevelAndPointsToTheNextOne()
    {
        var f = new Fixture(); var s = (await f.Service.LoadAsync()).Snapshot;
        Assert.Equal(1, s.Profile.Level);
        Assert.Equal(1, HubChronicle.OpenCount(s));
        Assert.True(HubChronicle.IsOpen(s, HubChronicle.Chapters[0]));
        Assert.Same(HubChronicle.Chapters[1], HubChronicle.NextLocked(s));

        s.Profile.Level = 5;
        Assert.Equal(4, HubChronicle.OpenCount(s));
        Assert.Equal("night_owner", HubChronicle.NextLocked(s)!.Id);

        s.Profile.Level = 99;
        Assert.Equal(HubChronicle.Chapters.Length, HubChronicle.OpenCount(s));
        Assert.Null(HubChronicle.NextLocked(s));
    }

    [Fact]
    public void ChronicleChaptersFitTheirBookSpreadAndOnlyTheGreatGameIsTorn()
    {
        var chapters = HubChronicle.Chapters;
        Assert.Equal(chapters.Length, chapters.Select(c => c.Id).Distinct().Count());
        Assert.Equal(1, chapters[0].Level);
        Assert.True(chapters.Zip(chapters.Skip(1), (a, b) => a.Level < b.Level).All(ascending => ascending));
        Assert.All(chapters, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Title));
            // Страница книги вмещает около 450 знаков крупным шрифтом.
            Assert.InRange(c.Left.Length, 120, 450);
            Assert.InRange(c.Right.Length, 120, 450);
        });
        Assert.Equal(new[] { "great_game" }, chapters.Where(c => c.Torn).Select(c => c.Id));
    }
}
