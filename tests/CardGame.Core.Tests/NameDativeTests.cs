using CardGame.Core.Games.Bluff;
using Xunit;

namespace CardGame.Core.Tests;

/// <summary>
/// «Аслану порвали туз»: концовка блефа называет проигравшего в дательном падеже.
/// Проверяем все имена, что бывают за столом, — и друзей, и запасные для ботов.
/// </summary>
public class NameDativeTests
{
    [Theory]
    [InlineData("Егор", "Егору")]
    [InlineData("Аслан", "Аслану")]
    [InlineData("Алишер", "Алишеру")]
    [InlineData("Ельнур", "Ельнуру")]
    [InlineData("Марат", "Марату")]
    [InlineData("Данил", "Данилу")]
    [InlineData("Борис", "Борису")]
    [InlineData("Вера", "Вере")]
    [InlineData("Ольга", "Ольге")]
    [InlineData("Марина", "Марине")]
    [InlineData("Игорь", "Игорю")]
    [InlineData("Сергей", "Сергею")]
    [InlineData("Пётр", "Петру")]
    [InlineData("Павел", "Павлу")]
    [InlineData("Илья", "Илье")]
    [InlineData("Мария", "Марии")]
    public void CommonNames_TakeTheDativeEnding(string name, string dative)
    {
        Assert.Equal(dative, BluffRules.NameDative(name));
    }

    /// <summary>Имена на гласную по-русски не склоняются: «Али порвали туз».</summary>
    [Theory]
    [InlineData("Али")]
    [InlineData("Мари")]
    [InlineData("Роберто")]
    public void NamesEndingInAVowel_StayAsTheyAre(string name)
    {
        Assert.Equal(name, BluffRules.NameDative(name));
    }

    /// <summary>Второй образ того же друга приходит с номером — склоняется только имя.</summary>
    [Fact]
    public void RepeatNumber_IsKeptAfterTheName()
    {
        Assert.Equal("Егору 2", BluffRules.NameDative("Егор 2"));
        Assert.Equal("Али 3", BluffRules.NameDative("Али 3"));
    }
}
