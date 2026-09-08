namespace Catosaurluna.HolmgangDuelMod.Core.Domain;

public sealed class DuelManager
{
    private readonly Dictionary<string, DuelRequest> requestsByTarget = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DuelSession> sessionsByParticipant = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, DuelSession> sessionsById = new();
    private readonly Dictionary<string, DateTimeOffset> cooldowns = new(StringComparer.Ordinal);
    private readonly object sync = new();

    public bool TryCreateRequest(
        DuelParticipant requester,
        DuelParticipant target,
        DateTimeOffset now,
        TimeSpan timeout,
        out DuelRequest? request)
    {
        lock (sync)
        {
            request = null;
            RemoveExpiredRequests(now);
            if (requester.StableId == target.StableId || IsBusy(requester.StableId) || IsBusy(target.StableId))
                return false;
            if (cooldowns.TryGetValue(requester.StableId, out var cooldownUntil) && now < cooldownUntil)
                return false;
            if (timeout <= TimeSpan.Zero || requestsByTarget.ContainsKey(target.StableId))
                return false;

            request = new DuelRequest(requester, target, now, now.Add(timeout));
            requestsByTarget[target.StableId] = request;
            return true;
        }
    }

    public bool TryAcceptRequest(
        string accepterId,
        string requesterId,
        DuelPosition center,
        double radius,
        DateTimeOffset now,
        TimeSpan countdown,
        out DuelSession? session)
    {
        lock (sync)
        {
            session = null;
            RemoveExpiredRequests(now);
            if (!requestsByTarget.TryGetValue(accepterId, out var request) ||
                !request.Matches(requesterId, accepterId) ||
                HasActiveSession(accepterId) ||
                HasActiveSession(requesterId))
                return false;
            if (countdown <= TimeSpan.Zero)
                return false;

            requestsByTarget.Remove(accepterId);
            session = DuelSession.Create(request.Requester, request.Target, center, radius, now);
            session.StartCountdown(now, countdown);
            IndexSession(session);
            return true;
        }
    }

    public bool TryCancelRequest(string playerId, DateTimeOffset now)
    {
        lock (sync)
        {
            RemoveExpiredRequests(now);
            var key = requestsByTarget.Values
                .Where(request => request.Requester.StableId == playerId || request.Target.StableId == playerId)
                .Select(request => request.Target.StableId)
                .FirstOrDefault();
            return key is not null && requestsByTarget.Remove(key);
        }
    }

    public bool TryEndSession(Guid sessionId, DuelResult result, DateTimeOffset now, TimeSpan cooldown)
    {
        lock (sync)
        {
            if (!sessionsById.TryGetValue(sessionId, out var session) || !session.TryEnd(result, now))
                return false;

            RemoveSessionIndexes(session);
            var cooldownUntil = now.Add(cooldown < TimeSpan.Zero ? TimeSpan.Zero : cooldown);
            cooldowns[session.First.StableId] = cooldownUntil;
            cooldowns[session.Second.StableId] = cooldownUntil;
            return true;
        }
    }

    public bool TryGetSession(string participantId, out DuelSession? session)
    {
        lock (sync)
            return sessionsByParticipant.TryGetValue(participantId, out session);
    }

    public IReadOnlyList<DuelSession> GetSessions()
    {
        lock (sync)
            return sessionsById.Values.ToArray();
    }

    public void Expire(DateTimeOffset now)
    {
        lock (sync)
        {
            RemoveExpiredRequests(now);
            foreach (var pair in cooldowns.Where(pair => now >= pair.Value).ToArray())
                cooldowns.Remove(pair.Key);
        }
    }

    private bool IsBusy(string participantId) =>
        sessionsByParticipant.ContainsKey(participantId) ||
        requestsByTarget.Values.Any(request =>
            request.Requester.StableId == participantId || request.Target.StableId == participantId);

    private bool HasActiveSession(string participantId) =>
        sessionsByParticipant.ContainsKey(participantId);

    private void IndexSession(DuelSession session)
    {
        sessionsById.Add(session.SessionId, session);
        sessionsByParticipant.Add(session.First.StableId, session);
        sessionsByParticipant.Add(session.Second.StableId, session);
    }

    private void RemoveSessionIndexes(DuelSession session)
    {
        sessionsById.Remove(session.SessionId);
        sessionsByParticipant.Remove(session.First.StableId);
        sessionsByParticipant.Remove(session.Second.StableId);
    }

    private void RemoveExpiredRequests(DateTimeOffset now)
    {
        foreach (var pair in requestsByTarget.Where(pair => pair.Value.IsExpired(now)).ToArray())
            requestsByTarget.Remove(pair.Key);
    }
}
