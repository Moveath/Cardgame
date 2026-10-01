using CardGame.Core.Games.Bluff;
using Xunit;

namespace CardGame.Core.Tests;

public class NameGenitiveTests
{
    [Theory]
    [InlineData("Егор", "Егора")]
    [InlineData("Ельнур", "Ельнура")]
    [InlineData("Марат", "Марата")]
    [InlineData("Али", "Али")]
    [InlineData("Аслан", "Аслана")]
    [InlineData("Данил", "Данила")]
    [InlineData("Борис", "Бориса")]
    [InlineData("Вера", "Веры")]
    [InlineData("Игорь", "Игоря")]
    [InlineData("Ольга", "Ольги")]
    [InlineData("Пётр", "Петра")]
    [InlineData("Марина", "Марины")]
    [InlineData("Сергей", "Сергея")]
    public void EveryCurrentPlayerName_IdentifiesTheChestOwner(string name, string owner)
    {
        Assert.Equal("Сундук " + owner, "Сундук " + BluffRules.NameGenitive(name));
    }

    [Theory]
    [InlineData("Петр", "Петра")]
    [InlineData("Павел", "Павла")]
    [InlineData("Лев", "Льва")]
    [InlineData("Илья", "Ильи")]
    [InlineData("Мария", "Марии")]
    [InlineData("Саша", "Саши")]
    [InlineData("Роберто", "Роберто")]
    [InlineData("Мари", "Мари")]
    [InlineData("Alex", "Alex")]
    [InlineData("", "")]
    public void CommonOtherNames_KeepExpectedEndings(string name, string owner)
    {
        Assert.Equal(owner, BluffRules.NameGenitive(name));
    }

    [Theory]
    [InlineData("Егор 2", "Егора 2")]
    [InlineData("Али 3", "Али 3")]
    [InlineData("Ольга 2", "Ольги 2")]
    [InlineData("Пётр 2", "Петра 2")]
    public void RepeatNumber_RemainsAfterDeclinedName(string name, string owner)
    {
        Assert.Equal(owner, BluffRules.NameGenitive(name));
    }
}
