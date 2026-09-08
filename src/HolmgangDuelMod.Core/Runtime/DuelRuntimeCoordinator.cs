using Catosaurluna.HolmgangDuelMod.Core.Configuration;
using Catosaurluna.HolmgangDuelMod.Core.Domain;
using Catosaurluna.HolmgangDuelMod.Core.Players;

namespace Catosaurluna.HolmgangDuelMod.Core.Runtime;

public interface IRuntimeParticipantSource
{
    bool TryGet(string stableId, out PlayerSnapshot? participant);
}

public interface ITestCombatantController : IRuntimeParticipantSource
{
    bool TrySpawn(DuelSession session);
    void Destroy(string stableId);
}

public interface IDuelPresentation
{
    void CountdownStarted(DuelSession session);
    void DuelStarted(DuelSession session);
    void DuelEnded(DuelSession session, DuelResult result);
}

public sealed class NullDuelPresentation : IDuelPresentation
{
    public void CountdownStarted(DuelSession session) { }
    public void DuelStarted(DuelSession session) { }
    public void DuelEnded(DuelSession session, DuelResult result) { }
}

public sealed class DuelRuntimeCoordinator
{
    private readonly DuelManager manager;
    private readonly DuelSettings settings;
    private readonly IRuntimeParticipantSource participants;
    private readonly IDuelPresentation presentation;
    private readonly HashSet<Guid> presentedCountdowns = new();

    public DuelRuntimeCoordinator(
        DuelManager manager,
        DuelSettings settings,
        IRuntimeParticipantSource participants,
        IDuelPresentation? presentation = null)
    {
        this.manager = manager;
        this.settings = settings;
        this.participants = participants;
        this.presentation = presentation ?? new NullDuelPresentation();
    }

    public void Tick(DateTimeOffset now)
    {
        manager.Expire(now);
        foreach (var session in manager.GetSessions())
        {
            if (session.State == DuelState.Countdown)
                TickCountdown(session, now);
            else if (session.State == DuelState.Active)
                TickActive(session, now);
        }
    }

    private void TickCountdown(DuelSession session, DateTimeOffset now)
    {
        if (presentedCountdowns.Add(session.SessionId))
            presentation.CountdownStarted(session);

        if (!TryGetPair(session, out var first, out var second))
        {
            End(session, DuelResult.Draw(DuelEndReason.InvalidParticipant), now);
            return;
        }
        if (first.IsDead || second.IsDead || !first.IsOnline || !second.IsOnline || first.WorldId != second.WorldId)
        {
            End(session, DuelResult.Draw(DuelEndReason.InvalidParticipant), now);
            return;
        }
        if (session.CountdownEndsAt <= now)
        {
            session.StartActive(now);
            presentation.DuelStarted(session);
        }
    }

    private void TickActive(DuelSession session, DateTimeOffset now)
    {
        if (!TryGetPair(session, out var first, out var second))
        {
            End(session, DuelResult.Draw(DuelEndReason.InvalidParticipant), now);
            return;
        }

        if (!first.IsOnline || !second.IsOnline)
        {
            EndForParticipantFailure(session, first, second, DuelEndReason.Disconnect, now);
            return;
        }
        if (first.IsDead || second.IsDead)
        {
            EndForParticipantFailure(session, first, second, DuelEndReason.Death, now);
            return;
        }
        if (first.WorldId != second.WorldId)
        {
            End(session, DuelResult.Draw(DuelEndReason.WorldTransfer), now);
            return;
        }

        if (first.Health <= settings.DefeatHealth || second.Health <= settings.DefeatHealth)
        {
            if (first.Health <= settings.DefeatHealth && second.Health <= settings.DefeatHealth)
                End(session, DuelResult.Draw(DuelEndReason.HealthThreshold), now);
            else if (first.Health <= settings.DefeatHealth)
                End(session, DuelResult.Winner(DuelEndReason.HealthThreshold, second.StableId, first.StableId), now);
            else
                End(session, DuelResult.Winner(DuelEndReason.HealthThreshold, first.StableId, second.StableId), now);
            return;
        }

        var firstInside = session.IsInside(first.Position);
        var secondInside = session.IsInside(second.Position);
        if (!firstInside || !secondInside)
        {
            if (!firstInside && !secondInside)
                End(session, DuelResult.Draw(DuelEndReason.RadiusExit), now);
            else if (!firstInside)
                End(session, DuelResult.Winner(DuelEndReason.RadiusExit, second.StableId, first.StableId), now);
            else
                End(session, DuelResult.Winner(DuelEndReason.RadiusExit, first.StableId, second.StableId), now);
        }
    }

    private void EndForParticipantFailure(
        DuelSession session,
        PlayerSnapshot first,
        PlayerSnapshot second,
        DuelEndReason reason,
        DateTimeOffset now)
    {
        var firstFailed = !first.IsOnline || first.IsDead;
        var secondFailed = !second.IsOnline || second.IsDead;
        var result = firstFailed == secondFailed
            ? DuelResult.Draw(reason)
            : firstFailed
                ? DuelResult.Winner(reason, second.StableId, first.StableId)
                : DuelResult.Winner(reason, first.StableId, second.StableId);
        End(session, result, now);
    }

    private void End(DuelSession session, DuelResult result, DateTimeOffset now)
    {
        if (!manager.TryEndSession(session.SessionId, result, now, TimeSpan.FromSeconds(settings.PostDuelCooldownSeconds)))
            return;
        presentedCountdowns.Remove(session.SessionId);
        presentation.DuelEnded(session, result);
    }

    private bool TryGetPair(DuelSession session, out PlayerSnapshot first, out PlayerSnapshot second)
    {
        if (participants.TryGet(session.First.StableId, out var firstSnapshot) && firstSnapshot is not null &&
            participants.TryGet(session.Second.StableId, out var secondSnapshot) && secondSnapshot is not null)
        {
            first = firstSnapshot;
            second = secondSnapshot;
            return true;
        }

        first = null!;
        second = null!;
        return false;
    }
}
