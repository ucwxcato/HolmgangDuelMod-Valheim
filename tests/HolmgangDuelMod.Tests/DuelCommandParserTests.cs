using Catosaurluna.HolmgangDuelMod.Core.Commands;

namespace Catosaurluna.HolmgangDuelMod.Tests;

public sealed class DuelCommandParserTests
{
    [Theory]
    [InlineData("/duel Alice", DuelCommandKind.Request, "Alice")]
    [InlineData("/duel Alice Example", DuelCommandKind.Request, "Alice Example")]
    [InlineData("/duel accept Alice", DuelCommandKind.Accept, "Alice")]
    [InlineData("/DUEL ACCEPT \"Alice Example\"", DuelCommandKind.Accept, "Alice Example")]
    public void Parses_player_commands(string input, DuelCommandKind expectedKind, string expectedName)
    {
        var parsed = DuelCommandParser.Parse(input);

        Assert.Equal(expectedKind, parsed.Kind);
        Assert.Equal(expectedName, parsed.PlayerName);
    }

    [Fact]
    public void Parses_admin_test_damage()
    {
        var parsed = DuelCommandParser.Parse("/dueltest damage 1");

        Assert.Equal(DuelCommandKind.TestDamage, parsed.Kind);
        Assert.Equal(1, parsed.Health);
    }

    [Fact]
    public void Rejects_invalid_test_damage_and_missing_player()
    {
        Assert.Equal(DuelCommandKind.Invalid, DuelCommandParser.Parse("/dueltest damage -1").Kind);
        Assert.Equal(DuelCommandKind.Invalid, DuelCommandParser.Parse("/duel accept").Kind);
        Assert.Equal(DuelCommandKind.NotACommand, DuelCommandParser.Parse("hello").Kind);
    }
}
