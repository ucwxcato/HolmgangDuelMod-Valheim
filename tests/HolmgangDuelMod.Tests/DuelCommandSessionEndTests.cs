using Catosaurluna.HolmgangDuelMod.Core.Commands;
using Catosaurluna.HolmgangDuelMod.Core.Configuration;
using Catosaurluna.HolmgangDuelMod.Core.Domain;
using Catosaurluna.HolmgangDuelMod.Core.Players;
using Catosaurluna.HolmgangDuelMod.Core.Runtime;

namespace Catosaurluna.HolmgangDuelMod.Tests;

public sealed class DuelCommandSessionEndTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 8, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Cancel_and_leave_report_the_ended_session_to_presentation()
    {
        var manager = new DuelManager();
        var settings = new DuelSettings { EnableAdminTestMode = true };
        var caller = new PlayerSnapshot("a", "Alice", "world", new DuelPosition(0, 0, 0), 100);
        var combatant = new VirtualTestCombatant(manager, () => "world");
        var ended = new List<DuelResult>();
        var service = new DuelCommandService(
            manager,
            settings,
            new PlayerDirectory(new[] { caller }),
            new AlwaysAdmin(),
            new FixedClock(Start),
            combatant,
            sessionEnded: (_, result) => ended.Add(result));

        service.Handle("a", new ParsedDuelCommand(DuelCommandKind.TestStart));
        Assert.Contains("cancelled", service.Handle("a", new ParsedDuelCommand(DuelCommandKind.TestCancel)), StringComparison.OrdinalIgnoreCase);

        Assert.Single(ended);
        Assert.Equal(DuelEndReason.Cancelled, ended[0].Reason);
        Assert.False(manager.TryGetSession("a", out _));
    }

    [Fact]
    public void Leave_reports_the_ended_session_to_presentation()
    {
        var manager = new DuelManager();
        var settings = new DuelSettings { EnableAdminTestMode = true };
        var caller = new PlayerSnapshot("a", "Alice", "world", new DuelPosition(0, 0, 0), 100);
        var combatant = new VirtualTestCombatant(manager, () => "world");
        var ended = new List<DuelResult>();
        var service = new DuelCommandService(
            manager,
            settings,
            new PlayerDirectory(new[] { caller }),
            new AlwaysAdmin(),
            new FixedClock(Start),
            combatant,
            sessionEnded: (_, result) => ended.Add(result));

        service.Handle("a", new ParsedDuelCommand(DuelCommandKind.TestStart));
        service.Handle("a", new ParsedDuelCommand(DuelCommandKind.TestLeave));

        Assert.Single(ended);
        Assert.Equal(DuelEndReason.RadiusExit, ended[0].Reason);
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
