namespace Catosaurluna.HolmgangDuelMod.Core.Configuration;

public sealed class DuelSettings
{
    public double RequestMaxDistance { get; set; } = 20;
    public double DuelRadius { get; set; } = 15;
    public double CountdownSeconds { get; set; } = 10;
    public double DefeatHealth { get; set; } = 1;
    public double RequestTimeoutSeconds { get; set; } = 30;
    public double PostDuelCooldownSeconds { get; set; } = 5;
    public double BoundaryCheckIntervalSeconds { get; set; } = 0.10;
    public bool EnableFlag { get; set; } = true;
    public bool EnableBubble { get; set; } = true;
    public bool AllowDuelWhileGlobalPvpEnabled { get; set; } = true;
    public bool EnableAdminTestMode { get; set; }
}

public sealed class DuelSettingsValidation
{
    public DuelSettingsValidation(DuelSettings settings, IReadOnlyList<string> corrections)
    {
        Settings = settings;
        Corrections = corrections;
    }

    public DuelSettings Settings { get; }
    public IReadOnlyList<string> Corrections { get; }
}

public static class DuelSettingsValidator
{
    public static DuelSettingsValidation Normalize(DuelSettings? input)
    {
        var source = input ?? new DuelSettings();
        var corrections = new List<string>();

        double Positive(double value, double fallback, string name, double max)
        {
            if (value > 0 && value <= max) return value;
            corrections.Add($"{name} was invalid and reset to {fallback}.");
            return fallback;
        }

        var settings = new DuelSettings
        {
            RequestMaxDistance = Positive(source.RequestMaxDistance, 20, nameof(DuelSettings.RequestMaxDistance), 1000),
            DuelRadius = Positive(source.DuelRadius, 15, nameof(DuelSettings.DuelRadius), 1000),
            CountdownSeconds = Positive(source.CountdownSeconds, 10, nameof(DuelSettings.CountdownSeconds), 300),
            DefeatHealth = Positive(source.DefeatHealth, 1, nameof(DuelSettings.DefeatHealth), 1000000),
            RequestTimeoutSeconds = Positive(source.RequestTimeoutSeconds, 30, nameof(DuelSettings.RequestTimeoutSeconds), 3600),
            PostDuelCooldownSeconds = source.PostDuelCooldownSeconds >= 0 && source.PostDuelCooldownSeconds <= 3600
                ? source.PostDuelCooldownSeconds
                : Invalid(source.PostDuelCooldownSeconds, 5, nameof(DuelSettings.PostDuelCooldownSeconds)),
            BoundaryCheckIntervalSeconds = Positive(source.BoundaryCheckIntervalSeconds, 0.10, nameof(DuelSettings.BoundaryCheckIntervalSeconds), 10),
            EnableFlag = source.EnableFlag,
            EnableBubble = source.EnableBubble,
            AllowDuelWhileGlobalPvpEnabled = source.AllowDuelWhileGlobalPvpEnabled,
            EnableAdminTestMode = source.EnableAdminTestMode
        };

        return new DuelSettingsValidation(settings, corrections);

        double Invalid(double _, double fallback, string name)
        {
            corrections.Add($"{name} was invalid and reset to {fallback}.");
            return fallback;
        }
    }
}
