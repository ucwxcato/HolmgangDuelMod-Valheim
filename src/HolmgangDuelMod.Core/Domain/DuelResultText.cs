namespace Catosaurluna.HolmgangDuelMod.Core.Domain;

/// <summary>
/// Player-facing wording for a finished duel. Names come from the session
/// snapshots, so the message uses display names while outcomes use stable IDs.
/// </summary>
public static class DuelResultText
{
    public static string Describe(DuelSession session, DuelResult result)
    {
        var reason = ReasonText(result.Reason);
        if (result.IsDraw)
            return $"Duel between {session.First.DisplayName} and {session.Second.DisplayName} ended in a draw: {reason}.";

        var winner = NameOf(session, result.WinnerId);
        var loser = NameOf(session, result.LoserId);
        return $"{winner} won the duel against {loser}: {reason}.";
    }

    private static string NameOf(DuelSession session, string? stableId)
    {
        if (stableId == session.First.StableId) return session.First.DisplayName;
        if (stableId == session.Second.StableId) return session.Second.DisplayName;
        return "Unknown";
    }

    private static string ReasonText(DuelEndReason reason) => reason switch
    {
        DuelEndReason.HealthThreshold => "defeated",
        DuelEndReason.RadiusExit => "left the duel area",
        DuelEndReason.Disconnect => "disconnected",
        DuelEndReason.Death => "died",
        DuelEndReason.WorldTransfer => "left the world",
        DuelEndReason.Cancelled => "cancelled",
        DuelEndReason.Timeout => "timed out",
        DuelEndReason.AuthorityLost => "lost server authority",
        DuelEndReason.InvalidParticipant => "a participant was unavailable",
        DuelEndReason.Shutdown => "the server shut down",
        _ => reason.ToString()
    };
}
