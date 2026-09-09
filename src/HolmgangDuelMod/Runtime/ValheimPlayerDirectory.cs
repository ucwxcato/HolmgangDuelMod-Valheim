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
            if (ZNet.instance.IsAdmin(stableId))
                return true;

            // Depending on the Valheim platform/runtime, the connected peer's
            // m_uid and its character ZDO UserID can be different identity
            // representations. The duel caller remains keyed by m_uid, but
            // admin authorization must also check the authenticated peer's
            // character UserID.
            if (!long.TryParse(stableId, out var peerUid))
                return false;

            foreach (var peer in ZNet.instance.GetPeers())
            {
                if (peer is null) continue;
                var peerType = peer.GetType();
                var uid = peerType.GetField("m_uid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(peer);
                if (uid is not long candidateUid || candidateUid != peerUid)
                    continue;

                var characterId = peerType.GetField("m_characterID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(peer);
                var userId = characterId?.GetType().GetProperty("UserID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(characterId);
                if (userId is long characterUserId && characterUserId > 0 && ZNet.instance.IsAdmin(characterUserId.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                    return true;
            }

            return false;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[HolmgangDuelMod] Admin check failed for {stableId}: {exception.Message}");
            return false;
        }
    }
}
#endif
