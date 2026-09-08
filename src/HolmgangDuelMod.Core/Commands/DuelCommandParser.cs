namespace Catosaurluna.HolmgangDuelMod.Core.Commands;

public enum DuelCommandKind
{
    NotACommand,
    Invalid,
    Request,
    Accept,
    Cancel,
    Status,
    Help,
    TestStart,
    TestDamage,
    TestLeave,
    TestCancel,
    TestReset
}

public sealed class ParsedDuelCommand
{
    public ParsedDuelCommand(DuelCommandKind kind, string? playerName = null, double? health = null, string? error = null)
    {
        Kind = kind;
        PlayerName = playerName;
        Health = health;
        Error = error;
    }

    public DuelCommandKind Kind { get; }
    public string? PlayerName { get; }
    public double? Health { get; }
    public string? Error { get; }
}

public static class DuelCommandParser
{
    public static ParsedDuelCommand Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return new ParsedDuelCommand(DuelCommandKind.NotACommand);

        var tokens = Tokenize(input.Trim());
        if (tokens.Count == 0 || !tokens[0].StartsWith('/'))
            return new ParsedDuelCommand(DuelCommandKind.NotACommand);

        var command = tokens[0][1..].ToLowerInvariant();
        if (command == "duel") return ParseDuel(tokens);
        if (command == "dueltest") return ParseTest(tokens);
        return new ParsedDuelCommand(DuelCommandKind.NotACommand);
    }

    private static ParsedDuelCommand ParseDuel(IReadOnlyList<string> tokens)
    {
        if (tokens.Count == 1) return Invalid("Usage: /duel <player>");
        var subcommand = tokens[1].ToLowerInvariant();
        if (subcommand is "help") return new ParsedDuelCommand(DuelCommandKind.Help);
        if (subcommand is "status") return new ParsedDuelCommand(DuelCommandKind.Status);
        if (subcommand is "cancel") return new ParsedDuelCommand(DuelCommandKind.Cancel);
        if (subcommand is "accept")
            return tokens.Count == 2
                ? Invalid("Usage: /duel accept <player>")
                : new ParsedDuelCommand(DuelCommandKind.Accept, Join(tokens, 2));
        return new ParsedDuelCommand(DuelCommandKind.Request, Join(tokens, 1));
    }

    private static ParsedDuelCommand ParseTest(IReadOnlyList<string> tokens)
    {
        if (tokens.Count < 2) return Invalid("Usage: /dueltest <start|damage|leave|cancel|reset>");
        return tokens[1].ToLowerInvariant() switch
        {
            "start" when tokens.Count == 2 => new ParsedDuelCommand(DuelCommandKind.TestStart),
            "damage" when tokens.Count == 3 && double.TryParse(tokens[2], out var health) && health >= 0
                => new ParsedDuelCommand(DuelCommandKind.TestDamage, health: health),
            "leave" when tokens.Count == 2 => new ParsedDuelCommand(DuelCommandKind.TestLeave),
            "cancel" when tokens.Count == 2 => new ParsedDuelCommand(DuelCommandKind.TestCancel),
            "reset" when tokens.Count == 2 => new ParsedDuelCommand(DuelCommandKind.TestReset),
            _ => Invalid("Invalid /dueltest syntax.")
        };
    }

    private static ParsedDuelCommand Invalid(string message) =>
        new(DuelCommandKind.Invalid, error: message);

    private static string Join(IReadOnlyList<string> tokens, int start) =>
        string.Join(' ', tokens.Skip(start));

    private static List<string> Tokenize(string input)
    {
        var tokens = new List<string>();
        var token = new System.Text.StringBuilder();
        var quoted = false;
        foreach (var character in input)
        {
            if (character == '"')
            {
                quoted = !quoted;
                continue;
            }
            if (char.IsWhiteSpace(character) && !quoted)
            {
                if (token.Length > 0)
                {
                    tokens.Add(token.ToString());
                    token.Clear();
                }
                continue;
            }
            token.Append(character);
        }
        if (token.Length > 0) tokens.Add(token.ToString());
        return tokens;
    }
}
