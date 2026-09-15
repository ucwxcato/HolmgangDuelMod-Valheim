#if VALHEIM_RUNTIME
using System.Collections;
using System.Reflection;
using Catosaurluna.HolmgangDuelMod.Core.Domain;
using Catosaurluna.HolmgangDuelMod.Core.Players;
using Catosaurluna.HolmgangDuelMod.Core.Runtime;
using UnityEngine;

namespace Catosaurluna.HolmgangDuelMod.Runtime;

internal sealed class GreydwarfTestCombatant : ITestCombatantController
{
    internal static GreydwarfTestCombatant? ActiveInstance { get; private set; }
    private readonly Dictionary<string, GameObject> spawned = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SpawnAttempt> pendingSpawns = new(StringComparer.Ordinal);
    private static readonly FieldInfo InstancesField = typeof(ZNetScene).GetField("m_instances", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
    private const string TestCombatantMarker = "HolmgangDuelMod.TestCombatant";
    private const string TestCombatantIdMarker = "HolmgangDuelMod.TestCombatantId";

    private readonly HashSet<string> damageEnabled = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Vector3> protectedCenters = new(StringComparer.Ordinal);

    public GreydwarfTestCombatant() => ActiveInstance = this;

    public void EnableDamage(string stableId) => damageEnabled.Add(stableId);
    public void DisableDamage(string stableId) => damageEnabled.Remove(stableId);
    public void ProtectCountdown(string stableId, Vector3 center) => protectedCenters[stableId] = center;

    internal bool ShouldAllowDamage(GameObject target)
    {
        foreach (var pair in spawned)
        {
            if (pair.Value == target)
                return damageEnabled.Contains(pair.Key);
        }

        // Cover duplicate/leftover creatures that are not returned by the
        // network instance scan. The harness owns this small arena pocket.
        foreach (var pair in protectedCenters)
        {
            if ((target.transform.position - pair.Value).sqrMagnitude <= 100f)
                return damageEnabled.Contains(pair.Key);
        }

        return true;
    }

    public bool TrySpawn(DuelSession session)
    {
        if (spawned.TryGetValue(session.Second.StableId, out var existing))
        {
            if (existing)
                return true;
            spawned.Remove(session.Second.StableId);
        }

        // Native ZDO registration is asynchronous. Treat an in-flight request
        // as the same spawn, rather than issuing a second SpawnObject call.
        if (pendingSpawns.ContainsKey(session.Second.StableId))
            return true;

        if (ZNetScene.instance is null || ZNet.instance is null || !ZNet.instance.IsServer())
            return false;

        var prefab = ZNetScene.instance.GetPrefab("Greydwarf");
        if (prefab is null)
            return false;

        var position = new Vector3((float)session.Center.X + 2f, (float)session.Center.Y, (float)session.Center.Z);
        var prefabHash = ZNetScene.instance.GetPrefabHash(prefab);

        // The test world is intentionally reused. Remove leftovers from an
        // older harness build in the proxy spawn pocket before capturing the
        // baseline, otherwise an untagged old duplicate looks like a second
        // opponent in the next run.
        DestroyGreydwarfsNear(position, prefabHash, 8f);
        var attempt = new SpawnAttempt(session.Second.StableId, position, prefabHash, CaptureExistingGreydwarfs(prefabHash));

        // Valheim 1.0 can advance network callbacks while SpawnObject runs.
        // Record the in-flight request first so a re-entrant lifecycle check
        // cannot issue a second Greydwarf spawn for the same duel.
        pendingSpawns[session.Second.StableId] = attempt;
        Debug.Log($"[HolmgangDuelMod] Requesting one Greydwarf test proxy for {session.SessionId} at {position}.");
        try
        {
            ZNetScene.instance.SpawnObject(position, Quaternion.identity, prefab);
        }
        catch (Exception exception)
        {
            pendingSpawns.Remove(session.Second.StableId);
            Debug.LogWarning($"[HolmgangDuelMod] Greydwarf test proxy request failed: {exception.Message}");
            return false;
        }

        if (TryFindSpawnedGreydwarf(attempt, out var instance))
            spawned[session.Second.StableId] = instance;

        // SpawnObject may register the ZDO during the next scene tick. The
        // position remains tracked so TryGet can resolve it on that tick.
        return true;
    }

    public bool TryGet(string stableId, out PlayerSnapshot? participant)
    {
        if ((!spawned.TryGetValue(stableId, out var instance) || !instance) &&
            pendingSpawns.TryGetValue(stableId, out var attempt) &&
            TryFindSpawnedGreydwarf(attempt, out instance))
        {
            spawned[stableId] = instance;
            Debug.Log($"[HolmgangDuelMod] Greydwarf test proxy registered for {stableId}.");
        }

        if (!spawned.TryGetValue(stableId, out instance) || !instance)
        {
            // SpawnObject can take until the next ZNetScene update to expose
            // the instance. Do not cancel the countdown in that short window;
            // fail closed if registration never completes.
            // Dedicated-server ZDO registration can take several seconds on
            // Valheim 1.0 while the zone streams in. Keep the logical proxy
            // alive through the countdown and a generous registration window;
            // the coordinator must not cancel at countdown second 8 merely
            // because the native object is still replicating.
            if (pendingSpawns.TryGetValue(stableId, out var pending) &&
                Time.unscaledTime - pending.RequestedAt < 30f)
            {
                participant = new PlayerSnapshot(
                    stableId,
                    "TestOpponent",
                    ZNet.instance is null ? "unknown" : ZNet.instance.GetWorldName(),
                    new DuelPosition(pending.Position.x, pending.Position.y, pending.Position.z),
                    health: 100d,
                    isOnline: true,
                    isDead: false);
                return true;
            }

            if (pendingSpawns.Remove(stableId))
                Debug.LogWarning($"[HolmgangDuelMod] Greydwarf test proxy did not register for {stableId}; cancelling the duel.");
            spawned.Remove(stableId);
            participant = null;
            return false;
        }

        var character = instance.GetComponent<Character>();
        if (character is null)
        {
            participant = null;
            return false;
        }

        var currentPosition = instance.transform.position;
        participant = new PlayerSnapshot(
            stableId,
            "TestOpponent",
            ZNet.instance is null ? "unknown" : ZNet.instance.GetWorldName(),
            new DuelPosition(currentPosition.x, currentPosition.y, currentPosition.z),
            character.GetHealth(),
            isOnline: true,
            isDead: character.IsDead());
        return true;
    }

    public void Destroy(string stableId)
    {
        // Resolve before dropping pending state so cleanup also catches a ZDO
        // that appeared in the same tick the duel ended.
        if ((!spawned.TryGetValue(stableId, out var tracked) || !tracked) &&
            pendingSpawns.TryGetValue(stableId, out var pending) &&
            TryFindSpawnedGreydwarf(pending, out tracked))
        {
            spawned[stableId] = tracked;
        }

        if (!spawned.Remove(stableId, out var instance) || !instance)
        {
            // A ZDO may be registered after a cancellation. Keep its attempt
            // until the normal update loop sees and removes that creature.
            if (pendingSpawns.TryGetValue(stableId, out var attempt))
                attempt.Cancelled = true;
            protectedCenters.Remove(stableId);
            return;
        }

        pendingSpawns.Remove(stableId);
        damageEnabled.Remove(stableId);
        protectedCenters.Remove(stableId);
        DestroyTaggedInstances(stableId, instance);
    }

    public void DestroyStaleTestCombatants()
    {
        if (ZNetScene.instance is null || InstancesField.GetValue(ZNetScene.instance) is not IDictionary instances)
            return;

        var stale = new List<GameObject>();
        foreach (DictionaryEntry entry in instances)
        {
            if (entry.Value is not ZNetView view || !view)
                continue;

            try
            {
                if (view.GetZDO().GetBool(TestCombatantMarker, false))
                    stale.Add(view.gameObject);
            }
            catch
            {
                // A destroyed network view is already cleaned up.
            }
        }

        foreach (var instance in stale)
        {
            if (instance)
                ZNetScene.instance.Destroy(instance);
        }
    }

    public void CleanupPendingSpawns()
    {
        foreach (var pair in pendingSpawns.ToArray())
        {
            if (!pair.Value.Cancelled)
                continue;

            if (TryFindSpawnedGreydwarf(pair.Value, out var instance))
            {
                pendingSpawns.Remove(pair.Key);
                DestroyTaggedInstances(pair.Key, instance);
                Debug.Log($"[HolmgangDuelMod] Removed delayed Greydwarf test proxy for {pair.Key}.");
                continue;
            }

            if (Time.unscaledTime - pair.Value.RequestedAt >= 3f)
            {
                pendingSpawns.Remove(pair.Key);
                Debug.LogWarning($"[HolmgangDuelMod] Timed out waiting to remove delayed Greydwarf test proxy for {pair.Key}.");
            }
        }
    }

    private static HashSet<ZDO> CaptureExistingGreydwarfs(int prefabHash)
    {
        var existing = new HashSet<ZDO>();
        if (ZNetScene.instance is null || InstancesField.GetValue(ZNetScene.instance) is not IDictionary instances)
            return existing;

        foreach (DictionaryEntry entry in instances)
        {
            if (entry.Value is not ZNetView view || !view)
                continue;

            try
            {
                var zdo = view.GetZDO();
                if (zdo.GetPrefab() == prefabHash)
                    existing.Add(zdo);
            }
            catch
            {
                // A destroyed network view cannot participate in a new spawn.
            }
        }

        return existing;
    }

    private static void DestroyGreydwarfsNear(Vector3 position, int prefabHash, float radius)
    {
        if (ZNetScene.instance is null || InstancesField.GetValue(ZNetScene.instance) is not IDictionary instances)
            return;

        var stale = new List<GameObject>();
        var radiusSquared = radius * radius;
        foreach (DictionaryEntry entry in instances)
        {
            if (entry.Value is not ZNetView view || !view)
                continue;
            try
            {
                var zdo = view.GetZDO();
                if (zdo.GetPrefab() != prefabHash ||
                    (view.transform.position - position).sqrMagnitude > radiusSquared)
                    continue;
                stale.Add(view.gameObject);
            }
            catch
            {
                // A destroyed network view is already cleaned up.
            }
        }

        foreach (var instance in stale)
        {
            if (instance)
                ZNetScene.instance.Destroy(instance);
        }

        if (stale.Count > 0)
            Debug.Log($"[HolmgangDuelMod] Cleared {stale.Count} Greydwarf(s) from the test proxy spawn area.");
    }

    private static bool TryFindSpawnedGreydwarf(SpawnAttempt attempt, out GameObject instance)
    {
        instance = null!;
        if (ZNetScene.instance is null || InstancesField.GetValue(ZNetScene.instance) is not IDictionary instances)
            return false;

        var nearestDistance = float.MaxValue;
        var candidates = new List<(ZNetView View, GameObject Object, float Distance)>();
        foreach (DictionaryEntry entry in instances)
        {
            if (entry.Value is not ZNetView view || !view)
                continue;

            GameObject candidate;
            try
            {
                var zdo = view.GetZDO();
                var taggedForSession = zdo.GetString(TestCombatantIdMarker, string.Empty) == attempt.StableId;
                if (zdo.GetPrefab() != attempt.PrefabHash ||
                    (!taggedForSession && (attempt.Bound || attempt.ExistingZdos.Contains(zdo))))
                    continue;
                candidate = view.gameObject;
                if (!candidate)
                    continue;
            }
            catch
            {
                continue;
            }

            var distance = (candidate.transform.position - attempt.Position).sqrMagnitude;
            if (candidate.GetComponent<Character>() is null)
                continue;

            candidates.Add((view, candidate, distance));
        }

        foreach (var candidate in candidates.OrderBy(item => item.Distance))
        {
            if (candidate.Distance < nearestDistance)
            {
                nearestDistance = candidate.Distance;
                instance = candidate.Object;
            }

            // Tag every object created by this attempt. If Valheim delivered
            // two objects for one request, both are cleaned together and a
            // dead first object can never cause a rebind to the second.
            candidate.View.GetZDO().Set(TestCombatantMarker, true);
            candidate.View.GetZDO().Set(TestCombatantIdMarker, attempt.StableId);
        }

        if (instance is not null)
            attempt.Bound = true;

        return instance;
    }

    private static void DestroyTaggedInstances(string stableId, GameObject? fallback)
    {
        var destroyed = new HashSet<int>();
        if (fallback)
        {
            destroyed.Add(fallback.GetInstanceID());
            if (ZNetScene.instance is not null)
                ZNetScene.instance.Destroy(fallback);
            else
                UnityEngine.Object.Destroy(fallback);
        }

        if (ZNetScene.instance is null || InstancesField.GetValue(ZNetScene.instance) is not IDictionary instances)
            return;

        foreach (DictionaryEntry entry in instances)
        {
            if (entry.Value is not ZNetView view || !view)
                continue;
            try
            {
                var zdo = view.GetZDO();
                if (zdo.GetString(TestCombatantIdMarker, string.Empty) != stableId ||
                    !destroyed.Add(view.gameObject.GetInstanceID()))
                    continue;
                ZNetScene.instance.Destroy(view.gameObject);
            }
            catch
            {
                // A destroyed network view is already cleaned up.
            }
        }
    }

    private sealed class SpawnAttempt
    {
        public SpawnAttempt(string stableId, Vector3 position, int prefabHash, HashSet<ZDO> existingZdos)
        {
            StableId = stableId;
            Position = position;
            PrefabHash = prefabHash;
            ExistingZdos = existingZdos;
            RequestedAt = Time.unscaledTime;
        }

        public Vector3 Position { get; }
        public string StableId { get; }
        public int PrefabHash { get; }
        public HashSet<ZDO> ExistingZdos { get; }
        public float RequestedAt { get; }
        public bool Cancelled { get; set; }
        public bool Bound { get; set; }
    }
}

internal sealed class CompositeRuntimeParticipantSource : IRuntimeParticipantSource
{
    private readonly ValheimPlayerDirectory players;
    private readonly GreydwarfTestCombatant combatant;

    public CompositeRuntimeParticipantSource(ValheimPlayerDirectory players, GreydwarfTestCombatant combatant)
    {
        this.players = players;
        this.combatant = combatant;
    }

    public bool TryGet(string stableId, out PlayerSnapshot? participant)
    {
        if (players.TryGet(stableId, out participant)) return true;
        return combatant.TryGet(stableId, out participant);
    }
}
#endif
