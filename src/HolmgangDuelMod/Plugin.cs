using Catosaurluna.HolmgangDuelMod.Core;

#if VALHEIM_RUNTIME
using BepInEx;
using Catosaurluna.HolmgangDuelMod.Core.Commands;
using Catosaurluna.HolmgangDuelMod.Runtime;

namespace Catosaurluna.HolmgangDuelMod;

[BepInPlugin(PluginMetadata.Guid, PluginMetadata.Name, PluginMetadata.Version)]
public sealed class Plugin : BaseUnityPlugin
{
    private PluginConfiguration? configuration;

    private void Awake()
    {
        configuration = PluginConfiguration.Bind(Config);
        var settings = configuration.ReadValidated(out var corrections);
        foreach (var correction in corrections)
            Logger.LogWarning(correction);

        new DuelCommandRegistration(HandleCommand).Register();

        Logger.LogInfo($"{PluginMetadata.Name} {PluginMetadata.Version} loaded.");
        Logger.LogInfo($"Duel radius: {settings.DuelRadius}; countdown: {settings.CountdownSeconds}s; admin test mode: {settings.EnableAdminTestMode}.");
    }

    private string HandleCommand(ParsedDuelCommand command)
    {
        return command.Kind switch
        {
            DuelCommandKind.Help => "HolmgangDuelMod: /duel <player>, /duel accept <player>, /duel cancel, /duel status",
            DuelCommandKind.Invalid => command.Error ?? "Invalid HolmgangDuelMod command.",
            DuelCommandKind.NotACommand => "Invalid HolmgangDuelMod command.",
            _ => "HolmgangDuelMod command transport is ready; player duel integration is not enabled yet."
        };
    }
}
#else
namespace Catosaurluna.HolmgangDuelMod;

/// <summary>
/// Build-time placeholder used until a local Valheim installation supplies runtime assemblies.
/// </summary>
public static class Plugin
{
    public static string BuildStatus => "Runtime references unavailable; configure ValheimInstall to build the plugin.";
}
#endif
