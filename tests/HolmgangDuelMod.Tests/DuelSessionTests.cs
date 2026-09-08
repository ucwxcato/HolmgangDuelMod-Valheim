using Catosaurluna.HolmgangDuelMod.Core.Domain;

namespace Catosaurluna.HolmgangDuelMod.Tests;

public sealed class DuelSessionTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Session_requires_countdown_before_active()
    {
        var session = CreateSession();

        session.StartCountdown(Start, TimeSpan.FromSeconds(10));
        Assert.Throws<InvalidOperationException>(() => session.StartActive(Start.AddSeconds(9)));

        session.StartActive(Start.AddSeconds(10));

        Assert.Equal(DuelState.Active, session.State);
    }

    [Fact]
    public void Radius_uses_horizontal_distance()
    {
        var session = CreateSession(radius: 10);

        Assert.True(session.IsInside(new DuelPosition(9, 100, 0)));
        Assert.False(session.IsInside(new DuelPosition(10.01, 0, 0)));
    }

    [Fact]
    public void Ending_is_idempotent_and_records_result()
    {
        var session = CreateSession();
        session.StartCountdown(Start, TimeSpan.FromSeconds(1));
        session.StartActive(Start.AddSeconds(1));
        var result = DuelResult.Winner(DuelEndReason.HealthThreshold, "a", "b");

        Assert.True(session.TryEnd(result, Start.AddSeconds(2)));
        Assert.False(session.TryEnd(result, Start.AddSeconds(3)));
        Assert.Equal(DuelState.Completed, session.State);
        Assert.Same(result, session.Result);
    }

    [Fact]
    public void Illegal_state_transitions_are_rejected()
    {
        var session = CreateSession();

        Assert.Throws<InvalidOperationException>(() => session.StartActive(Start));
        Assert.Throws<InvalidOperationException>(() => session.TryEnd(DuelResult.Draw(DuelEndReason.Cancelled), Start));

        session.StartCountdown(Start, TimeSpan.FromSeconds(1));
        Assert.Throws<InvalidOperationException>(() => session.StartCountdown(Start, TimeSpan.FromSeconds(1)));
        session.StartActive(Start.AddSeconds(1));
        Assert.Throws<InvalidOperationException>(() => session.StartActive(Start.AddSeconds(2)));
    }

    private static DuelSession CreateSession(double radius = 15) =>
        DuelSession.Create(
            new DuelParticipant("a", "Alice"),
            new DuelParticipant("b", "Bob"),
            new DuelPosition(0, 0, 0),
            radius,
            Start);
}
