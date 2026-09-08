#if VALHEIM_RUNTIME
using Catosaurluna.HolmgangDuelMod.Core.Domain;
using Catosaurluna.HolmgangDuelMod.Core.Players;
using Catosaurluna.HolmgangDuelMod.Core.Runtime;
using UnityEngine;

namespace Catosaurluna.HolmgangDuelMod.Runtime;

internal sealed class GreydwarfTestCombatant : ITestCombatantController
{
    private readonly Dictionary<string, GameObject> spawned = new(StringComparer.Ordinal);

    public bool TrySpawn(DuelSession session)
    {
        if (spawned.ContainsKey(session.Second.StableId) || ZNetScene.instance is null)
            return false;

        var prefab = ZNetScene.instance.GetPrefab("Greydwarf");
        if (prefab is null)
            return false;

        var position = new Vector3((float)session.Center.X + 2f, (float)session.Center.Y, (float)session.Center.Z);
        var instance = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity);
        if (instance.GetComponent<Character>() is null)
        {
            UnityEngine.Object.Destroy(instance);
            return false;
        }

        spawned[session.Second.StableId] = instance;
        return true;
    }

    public bool TryGet(string stableId, out PlayerSnapshot? participant)
    {
        if (!spawned.TryGetValue(stableId, out var instance) || instance is null)
        {
            participant = null;
            return false;
        }

        var character = instance.GetComponent<Character>();
        if (character is null)
        {
            participant = null;
            return false;
        }

        var position = instance.transform.position;
        participant = new PlayerSnapshot(
            stableId,
            "TestOpponent",
            ZNet.instance is null ? "unknown" : ZNet.instance.GetWorldName(),
            new DuelPosition(position.x, position.y, position.z),
            character.GetHealth(),
            isOnline: true,
            isDead: character.IsDead());
        return true;
    }

    public void Destroy(string stableId)
    {
        if (!spawned.Remove(stableId, out var instance) || instance is null)
            return;
        UnityEngine.Object.Destroy(instance);
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
