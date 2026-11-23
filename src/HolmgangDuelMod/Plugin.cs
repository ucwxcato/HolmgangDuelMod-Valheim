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
    private DuelRuntimePresentation? runtimePresentation;
    private VirtualTestCombatant? testCombatant;
    private DuelTestRpcBridge? duelTestRpc;
    private ServerAuthoritativeTestModeGate? testModeGate;
    private DuelSettings? settings;
    private float nextRuntimeTick;
    private bool legacyProxyCleanupDone;
    private bool duelProtectionArmed;

    private void Awake()
    {
        try
        {
            new HarmonyLib.Harmony(PluginMetadata.Guid).PatchAll(typeof(Plugin).Assembly);
            duelProtectionArmed = true;
        }
        catch (Exception exception)
        {
            // Fail closed: the plugin keeps loading, but duels cannot start without
            // the damage filter in place.
            Logger.LogError($"Duel damage protection failed to install; duels are disabled. {exception.Message}");
        }

        configuration = PluginConfiguration.Bind(Config);
        settings = configuration.ReadValidated(out var corrections);
        foreach (var correction in corrections)
            Logger.LogWarning(correction);

        playerDirectory = new ValheimPlayerDirectory();
        var manager = new DuelManager();
        DuelDamageGate.Manager = manager;
        testCombatant = new VirtualTestCombatant(manager, WorldName);
        testModeGate = new ServerAuthoritativeTestModeGate(settings.EnableAdminTestMode);
        commandService = new DuelCommandService(
            manager,
            settings,
            playerDirectory,
            new ValheimAdminAuthorizer(),
            new SystemDuelClock(),
            testCombatant,
            testModeGate,
            (session, result) => runtimePresentation?.DuelEnded(session, result),
            notifyPlayer: (platformId, message) => duelTestRpc?.NotifyPlayer(platformId, message));
        runtimePresentation = new DuelRuntimePresentation(
            settings,
            session => duelTestRpc?.BroadcastPresentation(session, ended: false),
            (session, result) => duelTestRpc?.BroadcastPresentation(session, ended: true, message: DuelResultText.Describe(session, result)));
        runtimeCoordinator = new DuelRuntimeCoordinator(
            manager,
            settings,
            new CompositeParticipantSource(playerDirectory, testCombatant),
            runtimePresentation,
            testCombatant,
            diagnostics: message => Logger.LogInfo(message));
        duelTestRpc = new DuelTestRpcBridge((callerId, command) =>
            commandService.Handle(callerId, command),
            (center, radius, ended, countdownSeconds) =>
            {
                if (runtimePresentation is null) return;
                if (ended)
                    runtimePresentation.HideClientTestPresentation();
                else
                    runtimePresentation.ShowClientTestPresentation(center, radius, countdownSeconds);
            },
            (command, response) =>
            {
                // The response RPC is already proven to reach the requesting
                // client. Use it as a visual fallback if a presentation RPC is
                // delayed or blocked by the current runtime/network setup.
                if (command.Kind != DuelCommandKind.TestStart ||
                    !response.StartsWith("Simulated duel started", StringComparison.OrdinalIgnoreCase) ||
                    runtimePresentation is null || settings is null || Player.m_localPlayer is null)
                    return;

                var position = Player.m_localPlayer.transform.position;
                runtimePresentation.ShowClientTestPresentation(
                    new DuelPosition(position.x, position.y, position.z),
                    settings.DuelRadius,
                    (int)Math.Ceiling(settings.CountdownSeconds));
            });
        nextRuntimeTick = Time.unscaledTime;
        new DuelCommandRegistration(HandleCommand).Register();

        Logger.LogInfo($"{PluginMetadata.Name} {PluginMetadata.Version} loaded.");
        Logger.LogInfo($"Duel radius: {settings.DuelRadius}; countdown: {settings.CountdownSeconds}s; server test mode: {settings.EnableAdminTestMode}.");
    }

    private void Update()
    {
        if (runtimeCoordinator is null || settings is null)
            return;

        if (!legacyProxyCleanupDone && ZNetScene.instance is not null && ZNet.instance is not null && ZNet.instance.IsServer())
        {
            legacyProxyCleanupDone = true;
            var removed = LegacyTestCreatureCleanup.RemoveLegacyProxies();
            if (removed > 0)
                Logger.LogInfo($"Removed {removed} legacy Greydwarf test proxy object(s) left in the world.");
        }

        if (Time.unscaledTime < nextRuntimeTick)
            return;

        nextRuntimeTick = Time.unscaledTime + (float)settings.BoundaryCheckIntervalSeconds;
        runtimeCoordinator.Tick(DateTimeOffset.UtcNow);
        if (ZNet.instance is not null && ZNet.instance.IsServer())
            runtimePresentation?.ReconcilePvp();
        runtimePresentation?.TickClientPresentation();
    }

    private static string WorldName() => ZNet.instance is null ? "unknown" : ZNet.instance.GetWorldName();

    private string HandleCommand(ParsedDuelCommand command)
    {
        if (playerDirectory is null || commandService is null || !playerDirectory.TryGetLocalPlayerId(out var localPlayerId))
            return "HolmgangDuelMod requires a loaded local player.";

        // Duel state is authoritative on the server, so every duel command runs
        // there. Clients only send the command and print the server's reply.
        if (!duelProtectionArmed && ZNet.instance is not null && ZNet.instance.IsServer() &&
            command.Kind is DuelCommandKind.Request or DuelCommandKind.Accept or DuelCommandKind.TestStart)
            return "Duels are disabled: damage protection failed to load. Check the BepInEx log.";

        // Duel state is authoritative on the server, so every duel command runs
        // there. Clients only send the command and print the server's reply.
        if (command.Kind is DuelCommandKind.Request or DuelCommandKind.Accept or DuelCommandKind.Cancel or DuelCommandKind.Status ||
            command.Kind == DuelCommandKind.TestStart ||
            command.Kind == DuelCommandKind.TestDamage ||
            command.Kind == DuelCommandKind.TestLeave ||
            command.Kind == DuelCommandKind.TestCancel ||
            command.Kind == DuelCommandKind.TestReset)
        {
            if (ZNet.instance is not null && ZNet.instance.IsServer())
                return commandService.Handle(localPlayerId, command);

            return duelTestRpc is not null && duelTestRpc.TrySend(localPlayerId, command, out var requestMessage)
                ? requestMessage
                : "Duel commands must be sent while connected to the dedicated server.";
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
