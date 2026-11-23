namespace Catosaurluna.HolmgangDuelMod.Core.Runtime;

/// <summary>
/// Durable record of each duelist's original PvP setting, written before PvP is
/// enabled for a duel. It lives beside the BepInEx config, not in the world save,
/// so an interrupted server can restore the setting when the player returns.
/// </summary>
public sealed class DuelPvpJournal
{
    private readonly string path;
    private readonly Dictionary<string, bool> entries = new(StringComparer.Ordinal);

    public DuelPvpJournal(string path)
    {
        this.path = path;
        Load();
    }

    public bool HasPending => entries.Count > 0;

    public bool TryGetOriginal(string playerId, out bool original) => entries.TryGetValue(playerId, out original);

    /// <summary>
    /// Records the original setting, keeping any earlier entry, because an earlier
    /// entry came from an interrupted duel and is the true original. Returns false
    /// when the journal cannot be written, so the caller must not enable PvP.
    /// </summary>
    public bool Add(string playerId, bool original)
    {
        if (entries.ContainsKey(playerId))
            return true;

        entries[playerId] = original;
        if (Save())
            return true;

        entries.Remove(playerId);
        return false;
    }

    public void Remove(string playerId)
    {
        if (entries.Remove(playerId))
            Save();
    }

    private void Load()
    {
        if (!File.Exists(path))
            return;

        foreach (var line in File.ReadAllLines(path))
        {
            var parts = line.Split('=');
            if (parts.Length == 2 && bool.TryParse(parts[1], out var value) && parts[0].Length > 0)
                entries[parts[0]] = value;
        }
    }

    private bool Save()
    {
        try
        {
            if (entries.Count == 0)
            {
                if (File.Exists(path))
                    File.Delete(path);
                return true;
            }

            File.WriteAllLines(path, entries.Select(pair => $"{pair.Key}={pair.Value}"));
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
