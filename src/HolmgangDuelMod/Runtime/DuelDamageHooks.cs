#if VALHEIM_RUNTIME
using HarmonyLib;
using Catosaurluna.HolmgangDuelMod.Core.Domain;

namespace Catosaurluna.HolmgangDuelMod.Runtime;

/// <summary>
/// The plugin sets this once the duel manager exists. While it is null the
/// hook allows vanilla damage, so nothing is filtered before the plugin loads.
/// </summary>
internal static class DuelDamageGate
{
    public static DuelManager? Manager { get; set; }
}

/// <summary>
/// Duel-only PvP filter. Decisions come from <see cref="DuelDamageRules"/>;
/// this class only translates Valheim attacker and target objects into stable
/// player IDs. Attackers and targets that are not players are not filtered.
/// </summary>
// Player does not declare Damage; it inherits Character.Damage, so patch the
// declaring type. Non-player attackers and targets fall through to vanilla.
[HarmonyPatch(typeof(Character), nameof(Character.Damage))]
internal static class DuelDamageHooks
{
    private static bool Prefix(Character __instance, HitData hit)
    {
        var manager = DuelDamageGate.Manager;
        if (manager is null)
            return true;

        var attacker = hit?.GetAttacker() as Player;
        var target = __instance as Player;
        var attackerId = attacker is null ? null : PlayerIdentity.CanonicalId(attacker);
        var targetId = target is null ? null : PlayerIdentity.CanonicalId(target);
        return DuelDamageRules.Evaluate(manager, attackerId, targetId) == DamageDecision.Allow;
    }
}
#endif
