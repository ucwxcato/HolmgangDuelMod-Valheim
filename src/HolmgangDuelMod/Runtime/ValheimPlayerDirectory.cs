#if VALHEIM_RUNTIME
using Catosaurluna.HolmgangDuelMod.Core.Domain;
using Catosaurluna.HolmgangDuelMod.Core.Players;
using Catosaurluna.HolmgangDuelMod.Core.Runtime;
using System.Reflection;

namespace Catosaurluna.HolmgangDuelMod.Runtime;

internal sealed class ValheimPlayerDirectory : IPlayerDirectory, IRuntimeParticipantSource
{
    public bool TryGet(string stableId, out PlayerSnapshot? participant) => TryGetByStableId(stableId, out participant);

    public bool TryGetByStableId(string stableId, out PlayerSnapshot? player)
    {
        player = SnapshotPlayers().FirstOrDefault(candidate => candidate.StableId == stableId);
        return player is not null;
    }

    public PlayerLookupResult ResolveOnline(string query)
    {
        var directory = new PlayerDirectory(SnapshotPlayers());
        return directory.ResolveOnline(query);
    }

    public bool TryGetLocalPlayerId(out string stableId)
    {
        if (Player.m_localPlayer is null)
        {
            stableId = string.Empty;
            return false;
        }

        stableId = Player.m_localPlayer.GetPlayerID().ToString();
        return true;
    }

    private static IEnumerable<PlayerSnapshot> SnapshotPlayers()
    {
        var worldId = ZNet.instance is null ? "unknown" : ZNet.instance.GetWorldName();
        foreach (var player in Player.GetAllPlayers())
        {
            if (player is null) continue;
            yield return new PlayerSnapshot(
                player.GetPlayerID().ToString(),
                player.GetPlayerName(),
                worldId,
                new DuelPosition(player.transform.position.x, player.transform.position.y, player.transform.position.z),
                player.GetHealth(),
                isOnline: true,
                isDead: player.IsDead());
        }
    }
}

internal sealed class ValheimAdminAuthorizer : IAdminAuthorizer
{
    private readonly FieldInfo? adminListField = typeof(ZNet).GetField("m_adminList", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private readonly MethodInfo? listContainsId = typeof(ZNet).GetMethod("ListContainsId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    public bool IsAdministrator(string stableId)
    {
        if (ZNet.instance is null || adminListField is null || listContainsId is null)
            return false;

        try
        {
            var adminList = adminListField.GetValue(ZNet.instance);
            return adminList is not null && listContainsId.Invoke(ZNet.instance, new[] { adminList, stableId }) is true;
        }
        catch
        {
            return false;
        }
    }
}
#endif
