namespace Catosaurluna.HolmgangDuelMod.Core.Domain;

public enum DamageDecision
{
    Allow,
    Block
}

/// <summary>
/// Duel-only player damage. Player-vs-player damage is allowed only between the
/// two participants of the same Active duel. A player who is in any duel is
/// protected from every other player. Damage without a player attacker (monsters,
/// environment) and damage to non-player targets is not filtered here.
/// </summary>
public static class DuelDamageRules
{
    public static DamageDecision Evaluate(DuelManager manager, string? attackerPlayerId, string? targetPlayerId)
    {
        if (string.IsNullOrEmpty(attackerPlayerId) || string.IsNullOrEmpty(targetPlayerId))
            return DamageDecision.Allow;

        var attackerInDuel = manager.TryGetSession(attackerPlayerId, out var attackerSession) && attackerSession is not null;
        var targetInDuel = manager.TryGetSession(targetPlayerId, out var targetSession) && targetSession is not null;
        if (!attackerInDuel && !targetInDuel)
            return DamageDecision.Allow;

        if (attackerInDuel && targetInDuel &&
            attackerPlayerId != targetPlayerId &&
            attackerSession!.SessionId == targetSession!.SessionId &&
            attackerSession.State == DuelState.Active)
            return DamageDecision.Allow;

        return DamageDecision.Block;
    }
}
