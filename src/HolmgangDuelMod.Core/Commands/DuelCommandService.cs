using Catosaurluna.HolmgangDuelMod.Core.Configuration;
using Catosaurluna.HolmgangDuelMod.Core.Domain;
using Catosaurluna.HolmgangDuelMod.Core.Players;
using Catosaurluna.HolmgangDuelMod.Core.Runtime;

namespace Catosaurluna.HolmgangDuelMod.Core.Commands;

public interface IDuelClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemDuelClock : IDuelClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public interface ITestModeGate
{
    bool IsEnabled { get; }
}

public sealed class SettingsTestModeGate : ITestModeGate
{
    public SettingsTestModeGate(bool isEnabled) => IsEnabled = isEnabled;
    public bool IsEnabled { get; }
}

public sealed class DuelCommandService
{
    private const string TestOpponentIdPrefix = "holmgangduelmod.test:";
    private readonly DuelManager manager;
    private readonly DuelSettings settings;
    private readonly IPlayerDirectory players;
    private readonly IDuelClock clock;
    private readonly ITestCombatantController? testCombatant;
    private readonly ITestModeGate testModeGate;
    private readonly Action<DuelSession, DuelResult>? sessionEnded;
    private readonly Action<string, string>? notifyPlayer;
    private readonly Dictionary<string, TestSession> testSessions = new(StringComparer.Ordinal);

    public DuelCommandService(
        DuelManager manager,
        DuelSettings settings,
        IPlayerDirectory players,
        IAdminAuthorizer adminAuthorizer,
        IDuelClock clock,
        ITestCombatantController? testCombatant = null,
        ITestModeGate? testModeGate = null,
        Action<DuelSession, DuelResult>? sessionEnded = null,
        Action<string, string>? notifyPlayer = null)
    {
        this.sessionEnded = sessionEnded;
        this.notifyPlayer = notifyPlayer;
        this.manager = manager;
        this.settings = settings;
        this.players = players;
        this.clock = clock;
        this.testCombatant = testCombatant;
        this.testModeGate = testModeGate ?? new SettingsTestModeGate(settings.EnableAdminTestMode);
    }

    public string Handle(string callerId, ParsedDuelCommand command)
    {
        if (!players.TryGetByStableId(callerId, out var caller) || caller is null)
            return "You must be online to use HolmgangDuelMod commands.";

        return command.Kind switch
        {
            DuelCommandKind.Request => Request(caller, command.PlayerName),
            DuelCommandKind.Accept => Accept(caller, command.PlayerName),
            DuelCommandKind.Cancel => Cancel(caller),
            DuelCommandKind.Status => Status(caller),
            DuelCommandKind.Help => Help(),
            DuelCommandKind.TestStart or DuelCommandKind.TestDamage or DuelCommandKind.TestLeave or DuelCommandKind.TestCancel or DuelCommandKind.TestReset
                => HandleTest(caller, command),
            DuelCommandKind.Invalid => command.Error ?? "Invalid duel command.",
            _ => "Usage: /duel <player>"
        };
    }

    private string Request(PlayerSnapshot caller, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return "Usage: /duel <player>";
        var result = players.ResolveOnline(query);
        if (result.Status == PlayerLookupStatus.NotFound) return "No online player matched that name.";
        if (result.Status == PlayerLookupStatus.Ambiguous)
            return "That name is ambiguous: " + string.Join(", ", result.Matches.Select(match => match.DisplayName));
        var target = result.Player!;
        if (target.StableId == caller.StableId) return "You cannot duel yourself.";
        if (target.WorldId != caller.WorldId) return "Both players must be in the same world.";
        if (caller.Position.HorizontalDistanceSquared(target.Position) > settings.RequestMaxDistance * settings.RequestMaxDistance)
            return "That player is too far away to receive a duel request.";

        // Reciprocal acceptance: if the target already asked this caller to duel,
        // /duel <target> accepts that request instead of creating a new one.
        if (manager.HasPendingRequest(target.StableId, caller.StableId, clock.UtcNow))
            return Accept(caller, target.DisplayName);

        if (!manager.TryCreateRequest(
                ToParticipant(caller),
                ToParticipant(target),
                clock.UtcNow,
                TimeSpan.FromSeconds(settings.RequestTimeoutSeconds),
                out _))
            return "That player is busy, on cooldown, or already has a pending duel request.";

        notifyPlayer?.Invoke(target.StableId,
            $"{caller.DisplayName} challenged you to a duel. Type /duel {caller.DisplayName} to accept within {settings.RequestTimeoutSeconds:0} seconds.");
        return $"Duel request sent to {target.DisplayName}. They can use /duel {caller.DisplayName} to accept.";
    }

    private string Accept(PlayerSnapshot caller, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return "Usage: /duel accept <player>";
        var result = players.ResolveOnline(query);
        if (result.Status != PlayerLookupStatus.Found) return LookupFailure(result);
        var requester = result.Player!;
        if (requester.WorldId != caller.WorldId) return "Both players must be in the same world.";
        if (caller.Position.HorizontalDistanceSquared(requester.Position) > settings.RequestMaxDistance * settings.RequestMaxDistance)
            return "That player is too far away to accept the duel request.";

        var center = Midpoint(caller.Position, requester.Position);
        return manager.TryAcceptRequest(
            caller.StableId,
            requester.StableId,
            center,
            settings.DuelRadius,
            clock.UtcNow,
            TimeSpan.FromSeconds(settings.CountdownSeconds),
            out _)
            ? $"Duel accepted. Countdown started with {requester.DisplayName}."
            : "No matching duel request was found, or the request expired.";
    }

    private string Cancel(PlayerSnapshot caller)
    {
        if (manager.TryCancelRequest(caller.StableId, clock.UtcNow))
            return "Duel request cancelled.";
        if (manager.TryGetSession(caller.StableId, out var session) && session is not null)
        {
            EndSession(session, DuelResult.Draw(DuelEndReason.Cancelled));
            return "Duel cancelled.";
        }
        return "You have no pending request or duel to cancel.";
    }

    private string Status(PlayerSnapshot caller)
    {
        if (manager.TryGetSession(caller.StableId, out var session) && session is not null)
            return $"Duel status: {session.State} against {session.OtherParticipant(caller.StableId).DisplayName}.";
        return "You have no active duel.";
    }

    private string HandleTest(PlayerSnapshot caller, ParsedDuelCommand command)
    {
        if (!testModeGate.IsEnabled)
            return "Server duel test mode is disabled.";

        PruneEndedTestSessions();
        return command.Kind switch
        {
            DuelCommandKind.TestStart => TestStart(caller),
            DuelCommandKind.TestDamage => TestDamage(caller, command.Health!.Value),
            DuelCommandKind.TestLeave => TestLeave(caller),
            DuelCommandKind.TestCancel => TestCancel(caller),
            DuelCommandKind.TestReset => TestReset(caller),
            _ => "Invalid duel test command."
        };
    }

    private string TestStart(PlayerSnapshot caller)
    {
        if (testSessions.ContainsKey(caller.StableId) || manager.TryGetSession(caller.StableId, out _))
            return "You already have a duel or test duel.";

        var fake = new DuelParticipant(TestOpponentIdPrefix + caller.StableId, "TestOpponent", true);
        var now = clock.UtcNow;
        if (!manager.TryCreateRequest(ToParticipant(caller), fake, now, TimeSpan.FromSeconds(5), out _))
            return "Could not create the simulated duel request.";
        if (!manager.TryAcceptRequest(fake.StableId, caller.StableId, caller.Position, settings.DuelRadius, now, TimeSpan.FromSeconds(settings.CountdownSeconds), out var session) || session is null)
            return "Could not start the simulated duel.";

        testSessions[caller.StableId] = new TestSession(session.SessionId, fake.StableId, settings.DefeatHealth);
        if (testCombatant is not null && !testCombatant.TrySpawn(session))
        {
            manager.TryEndSession(session.SessionId, DuelResult.Draw(DuelEndReason.InvalidParticipant), now, TimeSpan.Zero);
            testSessions.Remove(caller.StableId);
            return "Could not create the test opponent.";
        }
        return "Simulated duel started against TestOpponent.";
    }

    private string TestDamage(PlayerSnapshot caller, double health)
    {
        if (!testSessions.TryGetValue(caller.StableId, out var test)) return "No simulated duel is active.";
        if (testCombatant is not null)
        {
            return testCombatant.TrySetHealth(test.OpponentId, health) switch
            {
                TestDamageOutcome.Applied => $"TestOpponent health set to {health:0.##}. The defeat rule resolves on the next server tick.",
                TestDamageOutcome.ProtectedByCountdown => "TestOpponent is protected during the countdown; no damage was applied.",
                _ => "No simulated duel is active."
            };
        }
        test.Health = health;
        if (health <= settings.DefeatHealth && manager.TryGetSession(caller.StableId, out var session) && session is not null)
        {
            EndSession(session, DuelResult.Winner(DuelEndReason.HealthThreshold, caller.StableId, test.OpponentId));
            testSessions.Remove(caller.StableId);
            return "TestOpponent reached the defeat threshold. You win.";
        }
        return $"TestOpponent health set to {health:0.##}.";
    }

    private string TestLeave(PlayerSnapshot caller)
    {
        if (!testSessions.TryGetValue(caller.StableId, out var test)) return "No simulated duel is active.";
        if (manager.TryGetSession(caller.StableId, out var session) && session is not null)
            EndSession(session, DuelResult.Winner(DuelEndReason.RadiusExit, caller.StableId, test.OpponentId));
        testCombatant?.Destroy(test.OpponentId);
        testSessions.Remove(caller.StableId);
        return "TestOpponent left the arena. You win.";
    }

    private string TestCancel(PlayerSnapshot caller) => EndTest(caller, "Simulated duel cancelled.", DuelEndReason.Cancelled);
    private string TestReset(PlayerSnapshot caller) => EndTest(caller, "Simulated duel reset.", DuelEndReason.Cancelled);

    private string EndTest(PlayerSnapshot caller, string message, DuelEndReason reason)
    {
        if (!testSessions.TryGetValue(caller.StableId, out var test)) return "No simulated duel is active.";
        if (manager.TryGetSession(caller.StableId, out var session) && session is not null)
            EndSession(session, DuelResult.Draw(reason));
        testCombatant?.Destroy(test.OpponentId);
        testSessions.Remove(caller.StableId);
        return message;
    }

    /// <summary>
    /// Ends a session from a command and tells the presentation layer, so
    /// flag, bubble, and client end visuals are removed on every command path.
    /// </summary>
    private void EndSession(DuelSession session, DuelResult result)
    {
        if (manager.TryEndSession(session.SessionId, result, clock.UtcNow, TimeSpan.FromSeconds(settings.PostDuelCooldownSeconds)))
            sessionEnded?.Invoke(session, result);
    }

    /// <summary>
    /// The coordinator ends test duels on its own (health, radius, disconnect).
    /// Drop test bookkeeping for any duel that is no longer indexed so the
    /// caller can start a new test duel.
    /// </summary>
    private void PruneEndedTestSessions()
    {
        foreach (var pair in testSessions.ToArray())
        {
            if (manager.TryGetSession(pair.Key, out var session) &&
                session is not null &&
                session.SessionId == pair.Value.SessionId)
                continue;

            testCombatant?.Destroy(pair.Value.OpponentId);
            testSessions.Remove(pair.Key);
        }
    }

    private static string LookupFailure(PlayerLookupResult result) => result.Status switch
    {
        PlayerLookupStatus.Ambiguous => "That name is ambiguous: " + string.Join(", ", result.Matches.Select(match => match.DisplayName)),
        _ => "No online player matched that name."
    };

    private static string Help() => "Commands: /duel <player>, /duel accept <player>, /duel cancel, /duel status. Admin test: /dueltest start|damage <hp>|leave|cancel|reset.";

    private static DuelParticipant ToParticipant(PlayerSnapshot player) =>
        new(player.StableId, player.DisplayName);

    private static DuelPosition Midpoint(DuelPosition first, DuelPosition second) =>
        new((first.X + second.X) / 2, (first.Y + second.Y) / 2, (first.Z + second.Z) / 2);

    private sealed class TestSession
    {
        public TestSession(Guid sessionId, string opponentId, double health)
        {
            SessionId = sessionId;
            OpponentId = opponentId;
            Health = health;
        }

        public Guid SessionId { get; }
        public string OpponentId { get; }
        public double Health { get; set; }
    }
}
