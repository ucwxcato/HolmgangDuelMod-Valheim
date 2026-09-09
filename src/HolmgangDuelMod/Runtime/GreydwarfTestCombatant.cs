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
    private readonly Dictionary<string, GameObject> spawned = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SpawnAttempt> pendingSpawns = new(StringComparer.Ordinal);
    private static readonly FieldInfo InstancesField = typeof(ZNetScene).GetField("m_instances", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
    private const string TestCombatantMarker = "HolmgangDuelMod.TestCombatant";

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
        var attempt = new SpawnAttempt(position, ZNetScene.instance.GetPrefabHash(prefab), CaptureExistingGreydwarfs(ZNetScene.instance.GetPrefabHash(prefab)));
        Debug.Log($"[HolmgangDuelMod] Requesting one Greydwarf test proxy for {session.SessionId} at {position}.");
        ZNetScene.instance.SpawnObject(position, Quaternion.identity, prefab);
        pendingSpawns[session.Second.StableId] = attempt;

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
            if (pendingSpawns.TryGetValue(stableId, out var pending) &&
                Time.unscaledTime - pending.RequestedAt < 3f)
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

        pendingSpawns.Remove(stableId);
        if (!spawned.Remove(stableId, out var instance) || !instance)
            return;

        if (ZNetScene.instance is not null)
            ZNetScene.instance.Destroy(instance);
        else
            UnityEngine.Object.Destroy(instance);
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

    private static bool TryFindSpawnedGreydwarf(SpawnAttempt attempt, out GameObject instance)
    {
        instance = null!;
        if (ZNetScene.instance is null || InstancesField.GetValue(ZNetScene.instance) is not IDictionary instances)
            return false;

        var nearestDistance = float.MaxValue;
        foreach (DictionaryEntry entry in instances)
        {
            if (entry.Value is not ZNetView view || !view)
                continue;

            GameObject candidate;
            try
            {
                var zdo = view.GetZDO();
                if (zdo.GetPrefab() != attempt.PrefabHash || attempt.ExistingZdos.Contains(zdo))
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
            if (distance >= nearestDistance)
                continue;

            if (candidate.GetComponent<Character>() is null)
                continue;

            view.GetZDO().Set(TestCombatantMarker, true);
            nearestDistance = distance;
            instance = candidate;
        }

        return instance;
    }

    private sealed class SpawnAttempt
    {
        public SpawnAttempt(Vector3 position, int prefabHash, HashSet<ZDO> existingZdos)
        {
            Position = position;
            PrefabHash = prefabHash;
            ExistingZdos = existingZdos;
            RequestedAt = Time.unscaledTime;
        }

        public Vector3 Position { get; }
        public int PrefabHash { get; }
        public HashSet<ZDO> ExistingZdos { get; }
        public float RequestedAt { get; }
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
