using Catosaurluna.HolmgangDuelMod.Core.Commands;
using Catosaurluna.HolmgangDuelMod.Core.Configuration;
using Catosaurluna.HolmgangDuelMod.Core.Domain;
using Catosaurluna.HolmgangDuelMod.Core.Players;
using Catosaurluna.HolmgangDuelMod.Core.Runtime;

namespace Catosaurluna.HolmgangDuelMod.Tests;

public sealed class VirtualTestCombatantTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 8, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Spawn_places_one_opponent_inside_the_radius_at_full_health()
    {
        var manager = new DuelManager();
        var session = StartTestCountdown(manager, "a", 0, out var opponentId);
        var combatant = new VirtualTestCombatant(manager, () => "world");

        Assert.True(combatant.TrySpawn(session));
        Assert.True(combatant.TrySpawn(session));
        Assert.True(combatant.TryGet(opponentId, out var opponent));
        Assert.Equal("TestOpponent", opponent!.DisplayName);
        Assert.Equal("world", opponent.WorldId);
        Assert.Equal(VirtualTestCombatant.DefaultHealth, opponent.Health);
        Assert.True(session.IsInside(opponent.Position));
    }

    [Fact]
    public void Damage_is_refused_during_countdown_and_applied_once_active()
    {
        var manager = new DuelManager();
        var session = StartTestCountdown(manager, "a", 0, out var opponentId);
        var combatant = new VirtualTestCombatant(manager, () => "world");
        combatant.TrySpawn(session);

        Assert.Equal(TestDamageOutcome.ProtectedByCountdown, combatant.TrySetHealth(opponentId, 0));
        Assert.True(combatant.TryGet(opponentId, out var beforeStart));
        Assert.Equal(VirtualTestCombatant.DefaultHealth, beforeStart!.Health);

        session.StartActive(Start.AddSeconds(10));

        Assert.Equal(TestDamageOutcome.Applied, combatant.TrySetHealth(opponentId, 0));
        Assert.True(combatant.TryGet(opponentId, out var afterStart));
        Assert.Equal(0, afterStart!.Health);
    }

    [Fact]
    public void Damage_to_an_unknown_or_destroyed_opponent_is_refused()
    {
        var manager = new DuelManager();
        var session = StartTestCountdown(manager, "a", 0, out var opponentId);
        var combatant = new VirtualTestCombatant(manager, () => "world");

        Assert.Equal(TestDamageOutcome.NoOpponent, combatant.TrySetHealth(opponentId, 0));

        combatant.TrySpawn(session);
        combatant.Destroy(opponentId);

        Assert.False(combatant.TryGet(opponentId, out _));
        Assert.Equal(TestDamageOutcome.NoOpponent, combatant.TrySetHealth(opponentId, 0));
    }

    [Fact]
    public void Health_threshold_on_the_virtual_opponent_resolves_through_the_coordinator()
    {
        var manager = new DuelManager();
        var caller = Player("a", "Alice", 0);
        var session = StartTestCountdown(manager, "a", 0, out var opponentId);
        var combatant = new VirtualTestCombatant(manager, () => "world");
        combatant.TrySpawn(session);
        session.StartActive(Start.AddSeconds(10));
        combatant.TrySetHealth(opponentId, 0);
        var presentation = new RecordingPresentation();
        var coordinator = new DuelRuntimeCoordinator(
            manager,
            new DuelSettings(),
            new CompositeParticipantSource(new SnapshotSource(caller), combatant),
            presentation,
            combatant);

        coordinator.Tick(Start.AddSeconds(10.1));

        Assert.False(manager.TryGetSession("a", out _));
        Assert.Equal("a", presentation.Result!.WinnerId);
        Assert.Equal(opponentId, presentation.Result.LoserId);
        Assert.Equal(DuelEndReason.HealthThreshold, presentation.Result.Reason);
        Assert.False(combatant.TryGet(opponentId, out _));
    }

    [Fact]
    public void Moving_the_virtual_opponent_outside_the_radius_is_an_immediate_loss()
    {
        var manager = new DuelManager();
        var caller = Player("a", "Alice", 0);
        var session = StartTestCountdown(manager, "a", 0, out var opponentId);
        var combatant = new VirtualTestCombatant(manager, () => "world");
        combatant.TrySpawn(session);
        session.StartActive(Start.AddSeconds(10));
        Assert.True(combatant.TryMoveOutsideRadius(opponentId));
        var presentation = new RecordingPresentation();
        var coordinator = new DuelRuntimeCoordinator(
            manager,
            new DuelSettings(),
            new CompositeParticipantSource(new SnapshotSource(caller), combatant),
            presentation,
            combatant);

        coordinator.Tick(Start.AddSeconds(10.1));

        Assert.Equal("a", presentation.Result!.WinnerId);
        Assert.Equal(opponentId, presentation.Result.LoserId);
        Assert.Equal(DuelEndReason.RadiusExit, presentation.Result.Reason);
    }

    [Fact]
    public void Test_duel_can_be_restarted_after_the_coordinator_ends_it()
    {
        var manager = new DuelManager();
        var settings = new DuelSettings { EnableAdminTestMode = true };
        var caller = Player("a", "Alice", 0);
        var clock = new MutableClock(Start);
        var combatant = new VirtualTestCombatant(manager, () => "world");
        var service = new DuelCommandService(
            manager,
            settings,
            new PlayerDirectory(new[] { caller }),
            new AlwaysAdmin(),
            clock,
            combatant);
        var coordinator = new DuelRuntimeCoordinator(
            manager,
            settings,
            new CompositeParticipantSource(new SnapshotSource(caller), combatant),
            testCombatant: combatant);

        Assert.Contains("started", service.Handle("a", new ParsedDuelCommand(DuelCommandKind.TestStart)), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("protected", service.Handle("a", new ParsedDuelCommand(DuelCommandKind.TestDamage, health: 0)), StringComparison.OrdinalIgnoreCase);

        clock.Now = Start.AddSeconds(10);
        coordinator.Tick(clock.Now);
        Assert.Contains("health set", service.Handle("a", new ParsedDuelCommand(DuelCommandKind.TestDamage, health: 0)), StringComparison.OrdinalIgnoreCase);

        coordinator.Tick(clock.Now.AddSeconds(0.1));
        Assert.False(manager.TryGetSession("a", out _));

        clock.Now = Start.AddSeconds(30);
        Assert.Contains("started", service.Handle("a", new ParsedDuelCommand(DuelCommandKind.TestStart)), StringComparison.OrdinalIgnoreCase);
    }

    private static DuelSession StartTestCountdown(DuelManager manager, string callerId, double x, out string opponentId)
    {
        var caller = new DuelParticipant(callerId, "Alice");
        var opponent = new DuelParticipant("holmgangduelmod.test:" + callerId, "TestOpponent", isSimulated: true);
        opponentId = opponent.StableId;
        Assert.True(manager.TryCreateRequest(caller, opponent, Start, TimeSpan.FromSeconds(5), out _));
        Assert.True(manager.TryAcceptRequest(
            opponent.StableId,
            caller.StableId,
            new DuelPosition(x, 0, 0),
            15,
            Start,
            TimeSpan.FromSeconds(10),
            out var session));
        return session!;
    }

    private static PlayerSnapshot Player(string id, string name, double x) =>
        new(id, name, "world", new DuelPosition(x, 0, 0), 100);

    private sealed class SnapshotSource : IRuntimeParticipantSource
    {
        private readonly Dictionary<string, PlayerSnapshot> snapshots;
        public SnapshotSource(params PlayerSnapshot[] snapshots) => this.snapshots = snapshots.ToDictionary(snapshot => snapshot.StableId);
        public bool TryGet(string stableId, out PlayerSnapshot? participant) => snapshots.TryGetValue(stableId, out participant);
    }

    private sealed class RecordingPresentation : IDuelPresentation
    {
        public DuelResult? Result { get; private set; }
        public void CountdownStarted(DuelSession session) { }
        public void DuelStarted(DuelSession session) { }
        public void DuelEnded(DuelSession session, DuelResult result) => Result = result;
    }

    private sealed class MutableClock : IDuelClock
    {
        public MutableClock(DateTimeOffset now) => Now = now;
        public DateTimeOffset Now { get; set; }
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class AlwaysAdmin : IAdminAuthorizer
    {
        public bool IsAdministrator(string stableId) => true;
    }
}
