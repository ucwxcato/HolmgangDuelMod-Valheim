#if VALHEIM_RUNTIME
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace Catosaurluna.HolmgangDuelMod.Runtime;

/// <summary>
/// One-time migration for the reused test world. Earlier builds spawned a
/// native Greydwarf as the test opponent and tagged it with this ZDO key. The
/// current test opponent is non-networked and never spawns, so this sweep only
/// removes those leftovers. It does not identify or track the live opponent.
/// Delete this class once the test world has been cleaned.
/// </summary>
internal static class LegacyTestCreatureCleanup
{
    private const string LegacyMarker = "HolmgangDuelMod.TestCombatant";
    private static readonly FieldInfo? InstancesField =
        typeof(ZNetScene).GetField("m_instances", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    public static int RemoveLegacyProxies()
    {
        if (ZNetScene.instance is null || InstancesField?.GetValue(ZNetScene.instance) is not IDictionary instances)
            return 0;

        var stale = new List<GameObject>();
        foreach (DictionaryEntry entry in instances)
        {
            if (entry.Value is not ZNetView view || !view)
                continue;

            try
            {
                if (view.GetZDO().GetBool(LegacyMarker, false))
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

        return stale.Count;
    }
}
#endif
