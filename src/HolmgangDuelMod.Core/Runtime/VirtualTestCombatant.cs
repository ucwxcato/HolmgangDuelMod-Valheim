using Catosaurluna.HolmgangDuelMod.Core.Domain;
using Catosaurluna.HolmgangDuelMod.Core.Players;

namespace Catosaurluna.HolmgangDuelMod.Core.Runtime;

/// <summary>
/// Server-owned, non-networked test opponent. It has no Valheim object, no
/// ZDO and no world-save footprint: its position and health live only in
/// memory and are read by the same coordinator that evaluates real players.
/// Damage is applied only through <see cref="TrySetHealth"/>, and only while
/// the owning session is <see cref="DuelState.Active"/>.
/// </summary>
public sealed class VirtualTestCombatant : ITestCombatantController
{
    public const double DefaultHealth = 100;
    private const double SpawnOffsetMeters = 2;

    private readonly DuelManager manager;
    private readonly Func<string> worldIdProvider;
    private readonly double initialHealth;
    private readonly Dictionary<string, State> opponents = new(StringComparer.Ordinal);

    public VirtualTestCombatant(DuelManager manager, Func<string> worldIdProvider, double initialHealth = DefaultHealth)
    {
        this.manager = manager;
        this.worldIdProvider = worldIdProvider;
        this.initialHealth = initialHealth;
    }

    public bool TrySpawn(DuelSession session)
    {
        var opponent = session.First.IsSimulated ? session.First : session.Second;
        if (!session.ContainsParticipant(opponent.StableId))
            return false;

        if (opponents.ContainsKey(opponent.StableId))
            return true;

        var position = new DuelPosition(session.Center.X + SpawnOffsetMeters, session.Center.Y, session.Center.Z);
        opponents[opponent.StableId] = new State(position, session.Center, session.Radius, initialHealth);
        return true;
    }

    public bool TryGet(string stableId, out PlayerSnapshot? participant)
    {
        if (!opponents.TryGetValue(stableId, out var state))
        {
            participant = null;
            return false;
        }

        participant = new PlayerSnapshot(
            stableId,
            "TestOpponent",
            worldIdProvider(),
            state.Position,
            state.Health,
            isOnline: true,
            isDead: false);
        return true;
    }

    /// <summary>
    /// Applies an explicit test damage event. Refused unless the opponent's
    /// session is Active, so no damage can land during the countdown.
    /// </summary>
    public TestDamageOutcome TrySetHealth(string stableId, double health)
    {
        if (!opponents.TryGetValue(stableId, out var state))
            return TestDamageOutcome.NoOpponent;

        if (!manager.TryGetSession(stableId, out var session) || session is null)
            return TestDamageOutcome.NoOpponent;

        if (session.State != DuelState.Active)
            return TestDamageOutcome.ProtectedByCountdown;

        state.Health = Math.Max(0, health);
        return TestDamageOutcome.Applied;
    }

    /// <summary>
    /// Moves the opponent outside the duel radius. The coordinator then
    /// resolves the radius-exit rule on its next authoritative tick.
    /// </summary>
    public bool TryMoveOutsideRadius(string stableId)
    {
        if (!opponents.TryGetValue(stableId, out var state))
            return false;

        var offset = state.Radius + SpawnOffsetMeters + 1;
        state.Position = new DuelPosition(state.Center.X + offset, state.Center.Y, state.Center.Z);
        return true;
    }

    public void Destroy(string stableId) => opponents.Remove(stableId);

    private sealed class State
    {
        public State(DuelPosition position, DuelPosition center, double radius, double health)
        {
            Position = position;
            Center = center;
            Radius = radius;
            Health = health;
        }

        public DuelPosition Position { get; set; }
        public DuelPosition Center { get; }
        public double Radius { get; }
        public double Health { get; set; }
    }
}

public enum TestDamageOutcome
{
    Applied,
    NoOpponent,
    ProtectedByCountdown
}
