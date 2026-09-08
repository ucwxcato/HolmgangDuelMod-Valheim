using Catosaurluna.HolmgangDuelMod.Core.Domain;

namespace Catosaurluna.HolmgangDuelMod.Core.Players;

public sealed class PlayerSnapshot
{
    public PlayerSnapshot(
        string stableId,
        string displayName,
        string worldId,
        DuelPosition position,
        double health,
        bool isOnline = true,
        bool isDead = false)
    {
        StableId = stableId;
        DisplayName = displayName;
        WorldId = worldId;
        Position = position;
        Health = health;
        IsOnline = isOnline;
        IsDead = isDead;
    }

    public string StableId { get; }
    public string DisplayName { get; }
    public string WorldId { get; }
    public DuelPosition Position { get; set; }
    public double Health { get; set; }
    public bool IsOnline { get; set; }
    public bool IsDead { get; set; }
}

public enum PlayerLookupStatus
{
    Found,
    NotFound,
    Ambiguous
}

public sealed class PlayerLookupResult
{
    private PlayerLookupResult(PlayerLookupStatus status, PlayerSnapshot? player, IReadOnlyList<PlayerSnapshot> matches)
    {
        Status = status;
        Player = player;
        Matches = matches;
    }

    public PlayerLookupStatus Status { get; }
    public PlayerSnapshot? Player { get; }
    public IReadOnlyList<PlayerSnapshot> Matches { get; }

    public static PlayerLookupResult Found(PlayerSnapshot player) =>
        new(PlayerLookupStatus.Found, player, new[] { player });

    public static PlayerLookupResult NotFound() =>
        new(PlayerLookupStatus.NotFound, null, Array.Empty<PlayerSnapshot>());

    public static PlayerLookupResult Ambiguous(IReadOnlyList<PlayerSnapshot> matches) =>
        new(PlayerLookupStatus.Ambiguous, null, matches);
}

public interface IPlayerDirectory
{
    bool TryGetByStableId(string stableId, out PlayerSnapshot? player);
    PlayerLookupResult ResolveOnline(string query);
}

public sealed class PlayerDirectory : IPlayerDirectory
{
    private readonly IReadOnlyCollection<PlayerSnapshot> players;

    public PlayerDirectory(IEnumerable<PlayerSnapshot> players)
    {
        this.players = players.ToArray();
    }

    public bool TryGetByStableId(string stableId, out PlayerSnapshot? player)
    {
        player = players.FirstOrDefault(candidate => candidate.IsOnline && candidate.StableId == stableId);
        return player is not null;
    }

    public PlayerLookupResult ResolveOnline(string query)
    {
        var online = players.Where(player => player.IsOnline).ToArray();
        var exact = online.Where(player => string.Equals(player.DisplayName, query, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (exact.Length == 1) return PlayerLookupResult.Found(exact[0]);
        if (exact.Length > 1) return PlayerLookupResult.Ambiguous(exact);

        var partial = online.Where(player => player.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        return partial.Length switch
        {
            1 => PlayerLookupResult.Found(partial[0]),
            > 1 => PlayerLookupResult.Ambiguous(partial),
            _ => PlayerLookupResult.NotFound()
        };
    }
}
