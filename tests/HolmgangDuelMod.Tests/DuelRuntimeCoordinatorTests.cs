using Catosaurluna.HolmgangDuelMod.Core.Configuration;
using Catosaurluna.HolmgangDuelMod.Core.Domain;
using Catosaurluna.HolmgangDuelMod.Core.Players;
using Catosaurluna.HolmgangDuelMod.Core.Runtime;

namespace Catosaurluna.HolmgangDuelMod.Tests;

public sealed class DuelRuntimeCoordinatorTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 8, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Countdown_transitions_to_active_and_presentation_is_ordered()
    {
        var first = Player("a", "Alice", 0);
        var second = Player("b", "Bob", 5);
        var manager = StartSession(first, second, TimeSpan.FromSeconds(10), out _);
        var presentation = new RecordingPresentation();
        var coordinator = new DuelRuntimeCoordinator(manager, new DuelSettings(), new SnapshotSource(first, second), presentation);

        coordinator.Tick(Start);
        coordinator.Tick(Start.AddSeconds(10));

        Assert.Equal(new[] { "countdown", "started" }, presentation.Events);
        Assert.True(manager.TryGetSession("a", out var session));
        Assert.Equal(DuelState.Active, session!.State);
    }

    [Fact]
    public void Health_threshold_ends_with_the_other_player_as_winner()
    {
        var first = Player("a", "Alice", 0);
        var second = Player("b", "Bob", 5);
        var manager = StartSession(first, second, TimeSpan.FromSeconds(1), out var session);
        session.StartActive(Start.AddSeconds(1));
        first.Health = 1;
        var presentation = new RecordingPresentation();
        var coordinator = new DuelRuntimeCoordinator(manager, new DuelSettings(), new SnapshotSource(first, second), presentation);

        coordinator.Tick(Start.AddSeconds(2));

        Assert.False(manager.TryGetSession("a", out _));
        Assert.Equal("b", presentation.Result!.WinnerId);
        Assert.Equal(DuelEndReason.HealthThreshold, presentation.Result.Reason);
    }

    [Fact]
    public void Radius_exit_ends_immediately_and_leaver_loses()
    {
        var first = Player("a", "Alice", 0);
        var second = Player("b", "Bob", 5);
        var manager = StartSession(first, second, TimeSpan.FromSeconds(1), out var session);
        session.StartActive(Start.AddSeconds(1));
        first.Position = new DuelPosition(16, 0, 0);
        var presentation = new RecordingPresentation();
        var coordinator = new DuelRuntimeCoordinator(manager, new DuelSettings(), new SnapshotSource(first, second), presentation);

        coordinator.Tick(Start.AddSeconds(2));

        Assert.Equal("b", presentation.Result!.WinnerId);
        Assert.Equal("a", presentation.Result.LoserId);
        Assert.Equal(DuelEndReason.RadiusExit, presentation.Result.Reason);
    }

    [Fact]
    public void Disconnect_is_a_loss_and_cleanup_is_called_once()
    {
        var first = Player("a", "Alice", 0);
        var second = Player("b", "Bob", 5);
        var manager = StartSession(first, second, TimeSpan.FromSeconds(1), out var session);
        session.StartActive(Start.AddSeconds(1));
        first.IsOnline = false;
        var presentation = new RecordingPresentation();
        var coordinator = new DuelRuntimeCoordinator(manager, new DuelSettings(), new SnapshotSource(first, second), presentation);

        coordinator.Tick(Start.AddSeconds(2));
        coordinator.Tick(Start.AddSeconds(3));

        Assert.Equal(1, presentation.EndCount);
        Assert.Equal("b", presentation.Result!.WinnerId);
        Assert.Equal(DuelEndReason.Disconnect, presentation.Result.Reason);
    }

    private static DuelManager StartSession(PlayerSnapshot first, PlayerSnapshot second, TimeSpan countdown, out DuelSession session)
    {
        var manager = new DuelManager();
        Assert.True(manager.TryCreateRequest(
            new DuelParticipant(first.StableId, first.DisplayName),
            new DuelParticipant(second.StableId, second.DisplayName),
            Start,
            TimeSpan.FromSeconds(30),
            out _));
        Assert.True(manager.TryAcceptRequest(second.StableId, first.StableId, first.Position, 15, Start, countdown, out session!));
        return manager;
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
        public List<string> Events { get; } = new();
        public DuelResult? Result { get; private set; }
        public int EndCount { get; private set; }
        public void CountdownStarted(DuelSession session) => Events.Add("countdown");
        public void DuelStarted(DuelSession session) => Events.Add("started");
        public void DuelEnded(DuelSession session, DuelResult result)
        {
            Events.Add("ended");
            Result = result;
            EndCount++;
        }
    }
}
