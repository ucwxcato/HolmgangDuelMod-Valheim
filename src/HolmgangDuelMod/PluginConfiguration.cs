#if VALHEIM_RUNTIME
using BepInEx.Configuration;
using Catosaurluna.HolmgangDuelMod.Core.Configuration;

namespace Catosaurluna.HolmgangDuelMod;

internal sealed class PluginConfiguration
{
    private PluginConfiguration(
        ConfigEntry<float> requestMaxDistance,
        ConfigEntry<float> duelRadius,
        ConfigEntry<float> countdownSeconds,
        ConfigEntry<float> defeatHealth,
        ConfigEntry<float> requestTimeoutSeconds,
        ConfigEntry<float> postDuelCooldownSeconds,
        ConfigEntry<float> boundaryCheckIntervalSeconds,
        ConfigEntry<bool> enableFlag,
        ConfigEntry<bool> enableBubble,
        ConfigEntry<bool> allowDuelWhileGlobalPvpEnabled,
        ConfigEntry<bool> enableAdminTestMode)
    {
        RequestMaxDistance = requestMaxDistance;
        DuelRadius = duelRadius;
        CountdownSeconds = countdownSeconds;
        DefeatHealth = defeatHealth;
        RequestTimeoutSeconds = requestTimeoutSeconds;
        PostDuelCooldownSeconds = postDuelCooldownSeconds;
        BoundaryCheckIntervalSeconds = boundaryCheckIntervalSeconds;
        EnableFlag = enableFlag;
        EnableBubble = enableBubble;
        AllowDuelWhileGlobalPvpEnabled = allowDuelWhileGlobalPvpEnabled;
        EnableAdminTestMode = enableAdminTestMode;
    }

    private ConfigEntry<float> RequestMaxDistance { get; }
    private ConfigEntry<float> DuelRadius { get; }
    private ConfigEntry<float> CountdownSeconds { get; }
    private ConfigEntry<float> DefeatHealth { get; }
    private ConfigEntry<float> RequestTimeoutSeconds { get; }
    private ConfigEntry<float> PostDuelCooldownSeconds { get; }
    private ConfigEntry<float> BoundaryCheckIntervalSeconds { get; }
    private ConfigEntry<bool> EnableFlag { get; }
    private ConfigEntry<bool> EnableBubble { get; }
    private ConfigEntry<bool> AllowDuelWhileGlobalPvpEnabled { get; }
    private ConfigEntry<bool> EnableAdminTestMode { get; }

    public static PluginConfiguration Bind(ConfigFile config)
    {
        return new PluginConfiguration(
            config.Bind("Duel", "RequestMaxDistance", 20f, "Maximum distance for sending or accepting a duel request."),
            config.Bind("Duel", "DuelRadius", 15f, "Horizontal radius of an active duel."),
            config.Bind("Duel", "CountdownSeconds", 10f, "Countdown duration before combat begins."),
            config.Bind("Duel", "DefeatHealth", 1f, "Health threshold that ends an active duel."),
            config.Bind("Duel", "RequestTimeoutSeconds", 30f, "How long an unanswered request remains valid."),
            config.Bind("Duel", "PostDuelCooldownSeconds", 5f, "Cooldown after a duel ends."),
            config.Bind("Duel", "BoundaryCheckIntervalSeconds", 0.10f, "Interval between authoritative boundary checks."),
            config.Bind("Duel", "EnableFlag", true, "Show the temporary duel flag."),
            config.Bind("Duel", "EnableBubble", true, "Show the temporary ward-like duel boundary."),
            config.Bind("Duel", "AllowDuelWhileGlobalPvpEnabled", true, "Allow duel rules when global PvP is already enabled."),
            config.Bind("Testing", "EnableAdminTestMode", false, "Enable admin-only simulated-opponent test commands."));
    }

    public DuelSettings ReadValidated(out IReadOnlyList<string> corrections)
    {
        var result = DuelSettingsValidator.Normalize(new DuelSettings
        {
            RequestMaxDistance = RequestMaxDistance.Value,
            DuelRadius = DuelRadius.Value,
            CountdownSeconds = CountdownSeconds.Value,
            DefeatHealth = DefeatHealth.Value,
            RequestTimeoutSeconds = RequestTimeoutSeconds.Value,
            PostDuelCooldownSeconds = PostDuelCooldownSeconds.Value,
            BoundaryCheckIntervalSeconds = BoundaryCheckIntervalSeconds.Value,
            EnableFlag = EnableFlag.Value,
            EnableBubble = EnableBubble.Value,
            AllowDuelWhileGlobalPvpEnabled = AllowDuelWhileGlobalPvpEnabled.Value,
            EnableAdminTestMode = EnableAdminTestMode.Value
        });
        corrections = result.Corrections;
        return result.Settings;
    }
}
#endif
