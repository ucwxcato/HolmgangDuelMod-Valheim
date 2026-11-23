#if VALHEIM_RUNTIME
using BepInEx;
using Catosaurluna.HolmgangDuelMod.Core.Domain;
using Catosaurluna.HolmgangDuelMod.Core.Runtime;
using UnityEngine;

namespace Catosaurluna.HolmgangDuelMod.Runtime;

/// <summary>
/// Enables PvP only for the two duelists while their duel is Active, and puts
/// each player's original PvP setting back on every end path. Who may damage
/// whom is still decided by <see cref="DuelDamageHooks"/>, so a global PvP
/// change never exposes a duelist to anyone outside the duel.
///
/// Each original setting is written to a journal before PvP is enabled. If the
/// server stops mid-duel, the journal restores the setting when that player is
/// seen again, so PvP does not stay on in their saved character.
/// </summary>
internal sealed class DuelPvpState
{
    private const string JournalFileName = "catosaurluna.holmgangduelmod.pvp-journal.txt";
    private readonly DuelPvpJournal journal;
    private readonly Dictionary<Guid, List<(string Id, Player Player, bool Original)>> engaged = new();
    private readonly HashSet<string> activePlayerIds = new(StringComparer.Ordinal);

    public DuelPvpState()
        : this(new DuelPvpJournal(System.IO.Path.Combine(Paths.ConfigPath, JournalFileName)))
    {
    }

    public DuelPvpState(DuelPvpJournal journal)
    {
        this.journal = journal;
        if (journal.HasPending)
            Debug.LogWarning("[HolmgangDuelMod] A previous server stopped during a duel. Restoring PvP for those players when they return.");
    }

    public void Engage(DuelSession session)
    {
        if (engaged.ContainsKey(session.SessionId))
            return;

        var entries = new List<(string Id, Player Player, bool Original)>();
        foreach (var participant in new[] { session.First, session.Second })
        {
            if (participant.IsSimulated)
                continue;

            var player = FindPlayer(participant.StableId);
            if (player is null)
            {
                // Fail closed: a duelist without a Player object is not enabled.
                Debug.LogWarning($"[HolmgangDuelMod] Could not find duelist {participant.DisplayName} to enable duel PvP.");
                continue;
            }

            var id = participant.StableId;
            var original = journal.TryGetOriginal(id, out var pendingOriginal) ? pendingOriginal : player.IsPVPEnabled();
            if (!journal.Add(id, original))
            {
                Debug.LogWarning($"[HolmgangDuelMod] Could not write the PvP journal for {participant.DisplayName}; duel PvP stays off.");
                continue;
            }

            var before = player.IsPVPEnabled();
            player.SetPVP(true);
            entries.Add((id, player, original));
            activePlayerIds.Add(id);
            Debug.Log($"[HolmgangDuelMod] Duel PvP on for {participant.DisplayName} (id {id}, original {original}). " +
                      $"Flag before={before} after={player.IsPVPEnabled()} canSwitch={player.CanSwitchPVP()} owner={IsOwner(player)}.");
        }

        engaged[session.SessionId] = entries;
    }

    public void Restore(DuelSession session)
    {
        if (!engaged.Remove(session.SessionId, out var entries))
            return;

        foreach (var (id, player, original) in entries)
        {
            activePlayerIds.Remove(id);
            // A player who already left keeps the journal entry, and the setting
            // is restored when they return.
            if (!player)
                continue;

            var before = player.IsPVPEnabled();
            player.SetPVP(original);
            journal.Remove(id);
            Debug.Log($"[HolmgangDuelMod] Duel PvP restored for id {id}. Flag before={before} after={player.IsPVPEnabled()} canSwitch={player.CanSwitchPVP()} owner={IsOwner(player)}.");
        }
    }

    /// <summary>
    /// Restores journalled players who are online and not in a duel. Runs on the
    /// server only, from the authoritative tick.
    /// </summary>
    public void ReconcileReturningPlayers()
    {
        if (!journal.HasPending)
            return;

        foreach (var player in Player.GetAllPlayers())
        {
            if (!player)
                continue;

            var id = PlayerIdentity.CanonicalId(player);
            if (activePlayerIds.Contains(id) || !journal.TryGetOriginal(id, out var original))
                continue;

            player.SetPVP(original);
            journal.Remove(id);
            Debug.Log($"[HolmgangDuelMod] Restored PvP for returning player {player.GetPlayerName()} (id {id}).");
        }
    }

    private static bool IsOwner(Player player) => player.GetComponent<ZNetView>()?.IsOwner() ?? false;

    private static Player? FindPlayer(string stableId)
    {
        foreach (var player in Player.GetAllPlayers())
        {
            if (player && PlayerIdentity.CanonicalId(player) == stableId)
                return player;
        }

        return null;
    }
}
#endif
