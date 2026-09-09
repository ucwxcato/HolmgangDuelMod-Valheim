#if VALHEIM_RUNTIME
using Catosaurluna.HolmgangDuelMod.Core.Domain;
using Catosaurluna.HolmgangDuelMod.Core.Players;
using Catosaurluna.HolmgangDuelMod.Core.Runtime;
using System.Reflection;
using UnityEngine;

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
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var player in Player.GetAllPlayers())
        {
            if (player is null) continue;
            var stableId = player.GetPlayerID().ToString();
            if (!seen.Add(stableId)) continue;
            yield return new PlayerSnapshot(
                stableId,
                player.GetPlayerName(),
                worldId,
                new DuelPosition(player.transform.position.x, player.transform.position.y, player.transform.position.z),
                player.GetHealth(),
                isOnline: true,
                isDead: player.IsDead());
        }

        // A dedicated server may not have remote Player GameObjects in
        // Player.GetAllPlayers(). Its connected peers still carry the
        // authenticated platform UID and the latest networked position.
        if (ZNet.instance is null || !ZNet.instance.IsServer())
            yield break;

        foreach (var peer in ZNet.instance.GetPeers())
        {
            if (peer is null) continue;
            var peerType = peer.GetType();
            if (peerType.GetField("m_server", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(peer) is true)
                continue;

            if (peerType.GetField("m_uid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(peer) is not long uid || uid <= 0)
                continue;

            var stableId = uid.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!seen.Add(stableId)) continue;

            var name = peerType.GetField("m_playerName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(peer) as string;
            var position = peerType.GetField("m_refPos", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(peer) is Vector3 refPos
                ? new DuelPosition(refPos.x, refPos.y, refPos.z)
                : new DuelPosition(0, 0, 0);

            yield return new PlayerSnapshot(
                stableId,
                string.IsNullOrWhiteSpace(name) ? stableId : name,
                worldId,
                position,
                health: 100,
                isOnline: true,
                isDead: false);
        }
    }
}

internal sealed class ValheimAdminAuthorizer : IAdminAuthorizer
{
    public bool IsAdministrator(string stableId)
    {
        if (ZNet.instance is null || !ZNet.instance.IsServer() || string.IsNullOrWhiteSpace(stableId))
            return false;

        try
        {
            // Use Valheim's native check so the server applies its own admin
            // list loading and platform-ID normalization rules.
            return ZNet.instance.IsAdmin(stableId);
        }
        catch
        {
            return false;
        }
    }
}
#endif
