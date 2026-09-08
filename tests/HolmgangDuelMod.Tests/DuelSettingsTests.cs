using Catosaurluna.HolmgangDuelMod.Core.Configuration;

namespace Catosaurluna.HolmgangDuelMod.Tests;

public sealed class DuelSettingsTests
{
    [Fact]
    public void Invalid_values_reset_to_safe_defaults_and_are_reported()
    {
        var result = DuelSettingsValidator.Normalize(new DuelSettings
        {
            DuelRadius = -1,
            CountdownSeconds = 9999,
            PostDuelCooldownSeconds = -1
        });

        Assert.Equal(15, result.Settings.DuelRadius);
        Assert.Equal(10, result.Settings.CountdownSeconds);
        Assert.Equal(5, result.Settings.PostDuelCooldownSeconds);
        Assert.Equal(3, result.Corrections.Count);
    }
}
