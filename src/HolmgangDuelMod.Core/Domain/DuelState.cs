namespace Catosaurluna.HolmgangDuelMod.Core.Domain;

public enum DuelState
{
    Pending,
    Countdown,
    Active,
    Ending,
    Completed,
    Cancelled
}

public enum DuelEndReason
{
    HealthThreshold,
    RadiusExit,
    Disconnect,
    Death,
    WorldTransfer,
    Cancelled,
    Timeout,
    AuthorityLost,
    InvalidParticipant,
    Shutdown
}

public sealed class DuelParticipant
{
    public DuelParticipant(string stableId, string displayName, bool isSimulated = false)
    {
        if (string.IsNullOrWhiteSpace(stableId))
            throw new ArgumentException("A stable participant ID is required.", nameof(stableId));
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("A participant display name is required.", nameof(displayName));

        StableId = stableId;
        DisplayName = displayName;
        IsSimulated = isSimulated;
    }

    public string StableId { get; }
    public string DisplayName { get; }
    public bool IsSimulated { get; }
}

public readonly struct DuelPosition
{
    public DuelPosition(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public double X { get; }
    public double Y { get; }
    public double Z { get; }

    public double HorizontalDistanceSquared(DuelPosition other)
    {
        var x = X - other.X;
        var z = Z - other.Z;
        return (x * x) + (z * z);
    }
}

public sealed class DuelResult
{
    private DuelResult(DuelEndReason reason, string? winnerId, string? loserId, bool isDraw)
    {
        Reason = reason;
        WinnerId = winnerId;
        LoserId = loserId;
        IsDraw = isDraw;
    }

    public DuelEndReason Reason { get; }
    public string? WinnerId { get; }
    public string? LoserId { get; }
    public bool IsDraw { get; }

    public static DuelResult Winner(DuelEndReason reason, string winnerId, string loserId) =>
        new(reason, winnerId, loserId, false);

    public static DuelResult Draw(DuelEndReason reason) => new(reason, null, null, true);
}
