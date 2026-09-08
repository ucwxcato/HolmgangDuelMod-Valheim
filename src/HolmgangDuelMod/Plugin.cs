using Catosaurluna.HolmgangDuelMod.Core;

#if VALHEIM_RUNTIME
using BepInEx;
using UnityEngine;
using Catosaurluna.HolmgangDuelMod.Core.Commands;
using Catosaurluna.HolmgangDuelMod.Core.Configuration;
using Catosaurluna.HolmgangDuelMod.Core.Domain;
using Catosaurluna.HolmgangDuelMod.Core.Players;
using Catosaurluna.HolmgangDuelMod.Core.Runtime;
using Catosaurluna.HolmgangDuelMod.Runtime;

namespace Catosaurluna.HolmgangDuelMod;

[BepInPlugin(PluginMetadata.Guid, PluginMetadata.Name, PluginMetadata.Version)]
[BepInDependency("com.jotunn.jotunn")]
public sealed class Plugin : BaseUnityPlugin
{
    private PluginConfiguration? configuration;
    private ValheimPlayerDirectory? playerDirectory;
    private DuelCommandService? commandService;
    private DuelRuntimeCoordinator? runtimeCoordinator;
    private GreydwarfTestCombatant? greydwarfTestCombatant;
    private DuelTestRpcBridge? duelTestRpc;
    private ServerAuthoritativeTestModeGate? testModeGate;
    private DuelSettings? settings;
    private float nextRuntimeTick;

    private void Awake()
    {
        configuration = PluginConfiguration.Bind(Config);
        settings = configuration.ReadValidated(out var corrections);
        foreach (var correction in corrections)
            Logger.LogWarning(correction);

        playerDirectory = new ValheimPlayerDirectory();
        var manager = new DuelManager();
        greydwarfTestCombatant = new GreydwarfTestCombatant();
        testModeGate = new ServerAuthoritativeTestModeGate(settings.EnableAdminTestMode);
        commandService = new DuelCommandService(
            manager,
            settings,
            playerDirectory,
            new ValheimAdminAuthorizer(),
            new SystemDuelClock(),
            greydwarfTestCombatant,
            testModeGate);
        runtimeCoordinator = new DuelRuntimeCoordinator(
            manager,
            settings,
            new CompositeRuntimeParticipantSource(playerDirectory, greydwarfTestCombatant),
            new DuelRuntimePresentation(settings, greydwarfTestCombatant));
        duelTestRpc = new DuelTestRpcBridge((callerId, command) =>
            commandService.Handle(callerId, command));
        nextRuntimeTick = Time.unscaledTime;
        new DuelCommandRegistration(HandleCommand).Register();

        Logger.LogInfo($"{PluginMetadata.Name} {PluginMetadata.Version} loaded.");
        Logger.LogInfo($"Duel radius: {settings.DuelRadius}; countdown: {settings.CountdownSeconds}s; admin test mode: {settings.EnableAdminTestMode}.");
    }

    private void Update()
    {
        if (runtimeCoordinator is null || settings is null || Time.unscaledTime < nextRuntimeTick)
            return;

        nextRuntimeTick = Time.unscaledTime + (float)settings.BoundaryCheckIntervalSeconds;
        runtimeCoordinator.Tick(DateTimeOffset.UtcNow);
    }

    private string HandleCommand(ParsedDuelCommand command)
    {
        if (playerDirectory is null || commandService is null || !playerDirectory.TryGetLocalPlayerId(out var localPlayerId))
            return "HolmgangDuelMod requires a loaded local player.";

        if (command.Kind == DuelCommandKind.TestStart ||
            command.Kind == DuelCommandKind.TestDamage ||
            command.Kind == DuelCommandKind.TestLeave ||
            command.Kind == DuelCommandKind.TestCancel ||
            command.Kind == DuelCommandKind.TestReset)
        {
            if (ZNet.instance is not null && ZNet.instance.IsServer())
                return commandService.Handle(localPlayerId, command);

            return duelTestRpc is not null && duelTestRpc.TrySend(localPlayerId, command, out var requestMessage)
                ? requestMessage
                : "Duel test commands must be sent while connected to the dedicated server.";
        }

        return commandService.Handle(localPlayerId, command);
        /*
        return command.Kind switch
        {
            DuelCommandKind.Help => "HolmgangDuelMod: /duel <player>, /duel accept <player>, /duel cancel, /duel status",
            DuelCommandKind.Invalid => command.Error ?? "Invalid HolmgangDuelMod command.",
            DuelCommandKind.NotACommand => "Invalid HolmgangDuelMod command.",
            _ => "HolmgangDuelMod command transport is ready; player duel integration is not enabled yet."
        };
        */
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
