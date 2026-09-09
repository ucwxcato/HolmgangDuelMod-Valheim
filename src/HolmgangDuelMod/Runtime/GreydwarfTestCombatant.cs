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
    private readonly Dictionary<string, Vector3> spawnPositions = new(StringComparer.Ordinal);
    private static readonly FieldInfo InstancesField = typeof(ZNetScene).GetField("m_instances", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;

    public bool TrySpawn(DuelSession session)
    {
        if (spawned.TryGetValue(session.Second.StableId, out var existing))
        {
            if (existing)
                return false;
            spawned.Remove(session.Second.StableId);
        }

        if (ZNetScene.instance is null || ZNet.instance is null || !ZNet.instance.IsServer())
            return false;

        var prefab = ZNetScene.instance.GetPrefab("Greydwarf");
        if (prefab is null)
            return false;

        var position = new Vector3((float)session.Center.X + 2f, (float)session.Center.Y, (float)session.Center.Z);
        ZNetScene.instance.SpawnObject(position, Quaternion.identity, prefab);
        spawnPositions[session.Second.StableId] = position;

        if (TryFindSpawnedGreydwarf(position, out var instance))
            spawned[session.Second.StableId] = instance;

        // SpawnObject may register the ZDO during the next scene tick. The
        // position remains tracked so TryGet can resolve it on that tick.
        return true;
    }

    public bool TryGet(string stableId, out PlayerSnapshot? participant)
    {
        if ((!spawned.TryGetValue(stableId, out var instance) || !instance) &&
            spawnPositions.TryGetValue(stableId, out var expectedPosition) &&
            TryFindSpawnedGreydwarf(expectedPosition, out instance))
        {
            spawned[stableId] = instance;
        }

        if (!spawned.TryGetValue(stableId, out instance) || !instance)
        {
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
        spawnPositions.Remove(stableId);
        if (!spawned.Remove(stableId, out var instance) || !instance)
            return;

        if (ZNetScene.instance is not null)
            ZNetScene.instance.Destroy(instance);
        else
            UnityEngine.Object.Destroy(instance);
    }

    private static bool TryFindSpawnedGreydwarf(Vector3 position, out GameObject instance)
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
                candidate = view.gameObject;
                if (!candidate)
                    continue;
                if (!candidate.name.StartsWith("Greydwarf", StringComparison.Ordinal))
                    continue;
            }
            catch
            {
                continue;
            }

            var distance = (candidate.transform.position - position).sqrMagnitude;
            if (distance >= nearestDistance)
                continue;

            if (candidate.GetComponent<Character>() is null)
                continue;

            nearestDistance = distance;
            instance = candidate;
        }

        return instance;
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
