#if VALHEIM_RUNTIME
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace Catosaurluna.HolmgangDuelMod.Runtime;

/// <summary>
/// The one player ID used by every server-side duel path: the directory, the
/// duel commands, the damage hook, and PvP restore. It is the platform user ID
/// of the player's connection, which is also what the RPC caller path resolves.
/// Valheim's character ID (Player.GetPlayerID) is used only when no connection
/// can be matched, and that fallback is logged once per player.
/// </summary>
internal static class PlayerIdentity
{
    private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly HashSet<long> loggedFallbacks = new();

    public static string CanonicalId(Player player)
    {
        var platformId = PlatformIdOf(player);
        if (platformId is not null)
            return platformId;

        var characterId = player.GetPlayerID();
        if (loggedFallbacks.Add(characterId))
            Debug.LogWarning($"[HolmgangDuelMod] No connection matched {player.GetPlayerName()}; using character ID {characterId} for duel identity.");
        return characterId.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Finds the connection whose character ZDO ID matches this player's character
    /// object and returns its platform user ID. Only the server can see every
    /// connection, so clients return null.
    /// </summary>
    public static string? PlatformIdOf(Player player)
    {
        if (ZNet.instance is null || !ZNet.instance.IsServer() || !player)
            return null;

        var view = player.GetComponent<ZNetView>();
        if (view is null)
            return null;

        var characterUid = view.GetZDO().m_uid;
        foreach (var peer in ZNet.instance.GetPeers())
        {
            if (peer is null)
                continue;

            var characterId = peer.GetType().GetField("m_characterID", AnyInstance)?.GetValue(peer);
            if (characterId is not ZDOID zdoId || !zdoId.Equals(characterUid))
                continue;

            var userId = zdoId.UserID;
            return userId > 0 ? userId.ToString(CultureInfo.InvariantCulture) : null;
        }

        return null;
    }
}
#endif
