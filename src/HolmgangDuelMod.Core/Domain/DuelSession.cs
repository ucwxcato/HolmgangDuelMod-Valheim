namespace Catosaurluna.HolmgangDuelMod.Core.Domain;

public sealed class DuelSession
{
    private DuelSession(
        Guid sessionId,
        DuelParticipant first,
        DuelParticipant second,
        DuelPosition center,
        double radius,
        DateTimeOffset createdAt)
    {
        if (first.StableId == second.StableId)
            throw new ArgumentException("A duel requires two different participants.");
        if (radius <= 0)
            throw new ArgumentOutOfRangeException(nameof(radius));

        SessionId = sessionId;
        First = first;
        Second = second;
        Center = center;
        Radius = radius;
        CreatedAt = createdAt;
        State = DuelState.Pending;
    }

    public Guid SessionId { get; }
    public DuelParticipant First { get; }
    public DuelParticipant Second { get; }
    public DuelPosition Center { get; }
    public double Radius { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? CountdownEndsAt { get; private set; }
    public DateTimeOffset? EndedAt { get; private set; }
    public DuelState State { get; private set; }
    public DuelResult? Result { get; private set; }

    public static DuelSession Create(
        DuelParticipant first,
        DuelParticipant second,
        DuelPosition center,
        double radius,
        DateTimeOffset createdAt) =>
        new(Guid.NewGuid(), first, second, center, radius, createdAt);

    public bool ContainsParticipant(string stableId) =>
        First.StableId == stableId || Second.StableId == stableId;

    public DuelParticipant OtherParticipant(string stableId)
    {
        if (First.StableId == stableId) return Second;
        if (Second.StableId == stableId) return First;
        throw new InvalidOperationException("The participant is not part of this duel.");
    }

    public bool IsInside(DuelPosition position) =>
        Center.HorizontalDistanceSquared(position) <= Radius * Radius;

    public void StartCountdown(DateTimeOffset now, TimeSpan duration)
    {
        RequireState(DuelState.Pending);
        if (duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration));

        CountdownEndsAt = now.Add(duration);
        State = DuelState.Countdown;
    }

    public void StartActive(DateTimeOffset now)
    {
        RequireState(DuelState.Countdown);
        if (CountdownEndsAt is null || now < CountdownEndsAt.Value)
            throw new InvalidOperationException("The countdown has not completed.");

        State = DuelState.Active;
    }

    public bool TryEnd(DuelResult result, DateTimeOffset now)
    {
        if (State is DuelState.Completed or DuelState.Cancelled or DuelState.Ending)
            return false;
        if (State is not (DuelState.Countdown or DuelState.Active))
            throw new InvalidOperationException("A pending request cannot end as a duel result.");

        State = DuelState.Ending;
        Result = result ?? throw new ArgumentNullException(nameof(result));
        EndedAt = now;
        State = result.IsDraw || result.Reason is DuelEndReason.Cancelled or DuelEndReason.Timeout
            ? DuelState.Cancelled
            : DuelState.Completed;
        return true;
    }

    private void RequireState(DuelState expected)
    {
        if (State != expected)
            throw new InvalidOperationException($"Expected state {expected}, current state is {State}.");
    }
}
