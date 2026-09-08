namespace Catosaurluna.HolmgangDuelMod.Core.Domain;

public sealed class DuelRequest
{
    public DuelRequest(
        DuelParticipant requester,
        DuelParticipant target,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
    {
        if (requester.StableId == target.StableId)
            throw new ArgumentException("A player cannot request a duel with themselves.");
        if (expiresAt <= createdAt)
            throw new ArgumentException("A request must expire after it is created.");

        Requester = requester;
        Target = target;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public DuelParticipant Requester { get; }
    public DuelParticipant Target { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset ExpiresAt { get; }

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;
    public bool Matches(string requesterId, string targetId) =>
        Requester.StableId == requesterId && Target.StableId == targetId;
}
