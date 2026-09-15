#if VALHEIM_RUNTIME
using HarmonyLib;
using UnityEngine;

namespace Catosaurluna.HolmgangDuelMod.Runtime;

/// <summary>
/// Countdown is a staging period. Only the authoritative runtime coordinator
/// may enable damage for the tracked test proxy after the session becomes
/// Active; unrelated creatures keep vanilla damage behavior.
/// </summary>
[HarmonyPatch(typeof(Character), nameof(Character.Damage))]
internal static class DuelCombatHooks
{
    private static bool Prefix(Character __instance)
    {
        var combatant = GreydwarfTestCombatant.ActiveInstance;
        return combatant is null || combatant.ShouldAllowDamage(__instance.gameObject);
    }
}
#endif
