using Catosaurluna.HolmgangDuelMod.Core.Commands;
using Catosaurluna.HolmgangDuelMod.Core.Configuration;
using Catosaurluna.HolmgangDuelMod.Core.Domain;
using Catosaurluna.HolmgangDuelMod.Core.Players;

namespace Catosaurluna.HolmgangDuelMod.Tests;

public sealed class DuelCommandServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Request_and_accept_require_proximity_and_start_countdown()
    {
        var alice = Player("a", "Alice", 0);
        var bob = Player("b", "Bob", 10);
        var service = Service(alice, bob);

        Assert.Contains("request sent", service.Handle("a", new ParsedDuelCommand(DuelCommandKind.Request, "Bob")), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("countdown", service.Handle("b", new ParsedDuelCommand(DuelCommandKind.Accept, "Alice")), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Countdown", service.Handle("a", new ParsedDuelCommand(DuelCommandKind.Status)), StringComparison.Ordinal);
    }

    [Fact]
    public void Ambiguous_name_is_refused_without_creating_request()
    {
        var alice = Player("a", "Alice", 0);
        var bob = Player("b", "BobOne", 5);
        var bobTwo = Player("c", "BobTwo", 5);
        var service = Service(alice, bob, bobTwo);

        var response = service.Handle("a", new ParsedDuelCommand(DuelCommandKind.Request, "Bob"));

        Assert.Contains("ambiguous", response, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Admin_test_commands_use_normal_session_and_cleanup_paths()
    {
        var admin = Player("a", "Alice", 0);
        var service = Service(admin, adminMode: true);

        Assert.Contains("started", service.Handle("a", new ParsedDuelCommand(DuelCommandKind.TestStart)), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("health set", service.Handle("a", new ParsedDuelCommand(DuelCommandKind.TestDamage, health: 10)), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("win", service.Handle("a", new ParsedDuelCommand(DuelCommandKind.TestDamage, health: 1)), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no active", service.Handle("a", new ParsedDuelCommand(DuelCommandKind.Status)), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Connected_player_can_use_test_commands_when_server_mode_is_enabled()
    {
        var service = Service(Player("a", "Alice", 0), adminMode: true, isAdmin: false);

        var response = service.Handle("a", new ParsedDuelCommand(DuelCommandKind.TestStart));

        Assert.Contains("started", response, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Far_player_is_refused()
    {
        var service = Service(Player("a", "Alice", 0), Player("b", "Bob", 100));

        var response = service.Handle("a", new ParsedDuelCommand(DuelCommandKind.Request, "Bob"));

        Assert.Contains("far", response, StringComparison.OrdinalIgnoreCase);
    }

    private static DuelCommandService Service(params PlayerSnapshot[] players) => Service(players, false, true);

    private static DuelCommandService Service(PlayerSnapshot player, bool adminMode, bool isAdmin = true) =>
        Service(new[] { player }, adminMode, isAdmin);

    private static DuelCommandService Service(PlayerSnapshot[] players, bool adminMode, bool isAdmin)
    {
        return new DuelCommandService(
            new DuelManager(),
            new DuelSettings { EnableAdminTestMode = adminMode },
            new PlayerDirectory(players),
            new FixedAdminAuthorizer(isAdmin),
            new FixedClock(Start));
    }

    private static PlayerSnapshot Player(string id, string name, double x) =>
        new(id, name, "world", new DuelPosition(x, 0, 0), 100);

    private sealed class FixedClock : IDuelClock
    {
        public FixedClock(DateTimeOffset now) => UtcNow = now;
        public DateTimeOffset UtcNow { get; }
    }

    private sealed class FixedAdminAuthorizer : IAdminAuthorizer
    {
        private readonly bool isAdmin;
        public FixedAdminAuthorizer(bool isAdmin) => this.isAdmin = isAdmin;
        public bool IsAdministrator(string stableId) => isAdmin;
    }
}
