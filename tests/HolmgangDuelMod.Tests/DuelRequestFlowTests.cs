using Catosaurluna.HolmgangDuelMod.Core.Commands;
using Catosaurluna.HolmgangDuelMod.Core.Configuration;
using Catosaurluna.HolmgangDuelMod.Core.Domain;
using Catosaurluna.HolmgangDuelMod.Core.Players;

namespace Catosaurluna.HolmgangDuelMod.Tests;

public sealed class DuelRequestFlowTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 8, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Request_notifies_the_target_and_reciprocal_duel_command_accepts_it()
    {
        var manager = new DuelManager();
        var notifications = new List<(string TargetId, string Message)>();
        var service = Service(manager, notifications, new PlayerSnapshot("a", "Alice", "world", new DuelPosition(0, 0, 0), 100), new PlayerSnapshot("b", "Bob", "world", new DuelPosition(1, 0, 0), 100));

        var sent = service.Handle("a", new ParsedDuelCommand(DuelCommandKind.Request, "Bob"));
        var accepted = service.Handle("b", new ParsedDuelCommand(DuelCommandKind.Request, "Alice"));

        Assert.Contains("sent", sent, StringComparison.OrdinalIgnoreCase);
        Assert.Single(notifications);
        Assert.Equal("b", notifications[0].TargetId);
        Assert.Contains("Alice challenged you", notifications[0].Message, StringComparison.Ordinal);
        Assert.Contains("accepted", accepted, StringComparison.OrdinalIgnoreCase);
        Assert.True(manager.TryGetSession("a", out var session));
        Assert.Equal(DuelState.Countdown, session!.State);
    }

    [Fact]
    public void Player_names_match_regardless_of_case()
    {
        var manager = new DuelManager();
        var service = Service(manager, new List<(string, string)>(), new PlayerSnapshot("a", "Alice", "world", new DuelPosition(0, 0, 0), 100), new PlayerSnapshot("b", "Bob", "world", new DuelPosition(1, 0, 0), 100));

        var sent = service.Handle("a", new ParsedDuelCommand(DuelCommandKind.Request, "BOB"));
        var accepted = service.Handle("b", new ParsedDuelCommand(DuelCommandKind.Accept, "aLiCe"));

        Assert.Contains("sent", sent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("accepted", accepted, StringComparison.OrdinalIgnoreCase);
        Assert.True(manager.TryGetSession("a", out _));
    }

    [Fact]
    public void Requester_cannot_accept_their_own_request()
    {
        var manager = new DuelManager();
        var service = Service(manager, new List<(string, string)>(), new PlayerSnapshot("a", "Alice", "world", new DuelPosition(0, 0, 0), 100), new PlayerSnapshot("b", "Bob", "world", new DuelPosition(1, 0, 0), 100));

        service.Handle("a", new ParsedDuelCommand(DuelCommandKind.Request, "Bob"));
        var response = service.Handle("a", new ParsedDuelCommand(DuelCommandKind.Request, "Bob"));

        Assert.DoesNotContain("accepted", response, StringComparison.OrdinalIgnoreCase);
        Assert.False(manager.TryGetSession("a", out _));
    }

    private static DuelCommandService Service(
        DuelManager manager,
        List<(string TargetId, string Message)> notifications,
        params PlayerSnapshot[] players)
    {
        return new DuelCommandService(
            manager,
            new DuelSettings(),
            new PlayerDirectory(players),
            new AlwaysAdmin(),
            new FixedClock(Start),
            notifyPlayer: (targetId, message) => notifications.Add((targetId, message)));
    }

    private sealed class FixedClock : IDuelClock
    {
        public FixedClock(DateTimeOffset now) => UtcNow = now;
        public DateTimeOffset UtcNow { get; }
    }

    private sealed class AlwaysAdmin : IAdminAuthorizer
    {
        public bool IsAdministrator(string stableId) => true;
    }
}
