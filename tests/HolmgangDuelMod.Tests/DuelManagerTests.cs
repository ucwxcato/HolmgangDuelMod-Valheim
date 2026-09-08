using Catosaurluna.HolmgangDuelMod.Core.Domain;

namespace Catosaurluna.HolmgangDuelMod.Tests;

public sealed class DuelManagerTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Reciprocal_acceptance_creates_countdown_session()
    {
        var manager = new DuelManager();
        var alice = new DuelParticipant("a", "Alice");
        var bob = new DuelParticipant("b", "Bob");

        Assert.True(manager.TryCreateRequest(alice, bob, Start, TimeSpan.FromSeconds(30), out _));
        Assert.True(manager.TryAcceptRequest("b", "a", new DuelPosition(0, 0, 0), 15, Start, TimeSpan.FromSeconds(10), out var session));
        Assert.NotNull(session);
        Assert.Equal(DuelState.Countdown, session!.State);
        Assert.True(manager.TryGetSession("a", out _));
        Assert.True(manager.TryGetSession("b", out _));
    }

    [Fact]
    public void Expired_request_cannot_be_accepted()
    {
        var manager = new DuelManager();
        Assert.True(manager.TryCreateRequest(
            new DuelParticipant("a", "Alice"),
            new DuelParticipant("b", "Bob"),
            Start,
            TimeSpan.FromSeconds(1),
            out _));

        Assert.False(manager.TryAcceptRequest(
            "b",
            "a",
            new DuelPosition(0, 0, 0),
            15,
            Start.AddSeconds(1),
            TimeSpan.FromSeconds(10),
            out _));
    }

    [Fact]
    public void Ending_session_removes_busy_indexes_and_applies_cooldown()
    {
        var manager = new DuelManager();
        Assert.True(manager.TryCreateRequest(
            new DuelParticipant("a", "Alice"),
            new DuelParticipant("b", "Bob"),
            Start,
            TimeSpan.FromSeconds(30),
            out _));
        Assert.True(manager.TryAcceptRequest("b", "a", new DuelPosition(0, 0, 0), 15, Start, TimeSpan.FromSeconds(1), out var session));

        session!.StartActive(Start.AddSeconds(1));
        Assert.True(manager.TryEndSession(session.SessionId, DuelResult.Winner(DuelEndReason.HealthThreshold, "a", "b"), Start.AddSeconds(2), TimeSpan.FromSeconds(5)));
        Assert.False(manager.TryGetSession("a", out _));
        Assert.False(manager.TryCreateRequest(new DuelParticipant("a", "Alice"), new DuelParticipant("b", "Bob"), Start.AddSeconds(3), TimeSpan.FromSeconds(30), out _));
        Assert.True(manager.TryCreateRequest(new DuelParticipant("a", "Alice"), new DuelParticipant("b", "Bob"), Start.AddSeconds(8), TimeSpan.FromSeconds(30), out _));
    }

    [Fact]
    public void Duplicate_pending_request_and_duplicate_end_signal_are_rejected()
    {
        var manager = new DuelManager();
        var alice = new DuelParticipant("a", "Alice");
        var bob = new DuelParticipant("b", "Bob");

        Assert.True(manager.TryCreateRequest(alice, bob, Start, TimeSpan.FromSeconds(30), out _));
        Assert.False(manager.TryCreateRequest(alice, bob, Start, TimeSpan.FromSeconds(30), out _));
        Assert.True(manager.TryAcceptRequest("b", "a", new DuelPosition(0, 0, 0), 15, Start, TimeSpan.FromSeconds(1), out var session));

        session!.StartActive(Start.AddSeconds(1));
        var result = DuelResult.Winner(DuelEndReason.HealthThreshold, "a", "b");
        Assert.True(manager.TryEndSession(session.SessionId, result, Start.AddSeconds(2), TimeSpan.Zero));
        Assert.False(manager.TryEndSession(session.SessionId, result, Start.AddSeconds(2), TimeSpan.Zero));
    }
}
