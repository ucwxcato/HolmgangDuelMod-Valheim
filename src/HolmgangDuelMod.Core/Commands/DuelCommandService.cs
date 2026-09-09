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
    private readonly Dictionary<string, TestSession> testSessions = new(StringComparer.Ordinal);

    public DuelCommandService(
        DuelManager manager,
        DuelSettings settings,
        IPlayerDirectory players,
        IAdminAuthorizer adminAuthorizer,
        IDuelClock clock,
        ITestCombatantController? testCombatant = null,
        ITestModeGate? testModeGate = null)
    {
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

        return manager.TryCreateRequest(
            ToParticipant(caller),
            ToParticipant(target),
            clock.UtcNow,
            TimeSpan.FromSeconds(settings.RequestTimeoutSeconds),
            out _)
            ? $"Duel request sent to {target.DisplayName}. They can use /duel {caller.DisplayName} to accept."
            : "That player is busy, on cooldown, or already has a pending duel request.";
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
            manager.TryEndSession(session.SessionId, DuelResult.Draw(DuelEndReason.Cancelled), clock.UtcNow, TimeSpan.FromSeconds(settings.PostDuelCooldownSeconds));
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
            return "Could not spawn the Greydwarf test opponent.";
        }
        return "Simulated duel started against TestOpponent.";
    }

    private string TestDamage(PlayerSnapshot caller, double health)
    {
        if (!testSessions.TryGetValue(caller.StableId, out var test)) return "No simulated duel is active.";
        if (testCombatant is not null)
            return "Live test opponent active. Hit the Greydwarf normally; do not use /dueltest damage.";
        test.Health = health;
        if (health <= settings.DefeatHealth && manager.TryGetSession(caller.StableId, out var session) && session is not null)
        {
            manager.TryEndSession(session.SessionId, DuelResult.Winner(DuelEndReason.HealthThreshold, caller.StableId, test.OpponentId), clock.UtcNow, TimeSpan.FromSeconds(settings.PostDuelCooldownSeconds));
            testSessions.Remove(caller.StableId);
            return "TestOpponent reached the defeat threshold. You win.";
        }
        return $"TestOpponent health set to {health:0.##}.";
    }

    private string TestLeave(PlayerSnapshot caller)
    {
        if (!testSessions.TryGetValue(caller.StableId, out var test)) return "No simulated duel is active.";
        if (manager.TryGetSession(caller.StableId, out var session) && session is not null)
            manager.TryEndSession(session.SessionId, DuelResult.Winner(DuelEndReason.RadiusExit, caller.StableId, test.OpponentId), clock.UtcNow, TimeSpan.FromSeconds(settings.PostDuelCooldownSeconds));
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
            manager.TryEndSession(session.SessionId, DuelResult.Draw(reason), clock.UtcNow, TimeSpan.FromSeconds(settings.PostDuelCooldownSeconds));
        testCombatant?.Destroy(test.OpponentId);
        testSessions.Remove(caller.StableId);
        return message;
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
