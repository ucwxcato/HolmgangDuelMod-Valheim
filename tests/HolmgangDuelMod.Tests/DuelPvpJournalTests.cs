using Catosaurluna.HolmgangDuelMod.Core.Runtime;

namespace Catosaurluna.HolmgangDuelMod.Tests;

public sealed class DuelPvpJournalTests
{
    [Fact]
    public void Entry_survives_a_restart_until_removed()
    {
        var path = TempPath();
        try
        {
            var journal = new DuelPvpJournal(path);
            Assert.True(journal.Add("player-1", original: false));

            var reloaded = new DuelPvpJournal(path);
            Assert.True(reloaded.HasPending);
            Assert.True(reloaded.TryGetOriginal("player-1", out var original));
            Assert.False(original);

            reloaded.Remove("player-1");
            Assert.False(new DuelPvpJournal(path).HasPending);
            Assert.False(File.Exists(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Earlier_original_is_kept_when_the_player_engages_again()
    {
        var path = TempPath();
        try
        {
            var journal = new DuelPvpJournal(path);
            journal.Add("player-1", original: false);

            // A second engagement sees PvP already on from the interrupted duel.
            journal.Add("player-1", original: true);

            Assert.True(journal.TryGetOriginal("player-1", out var original));
            Assert.False(original);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Unwritable_journal_refuses_to_record()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"hdm-journal-dir-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            // A directory at the file path makes every write fail.
            var path = Path.Combine(directory, "journal.txt");
            Directory.CreateDirectory(path);

            var journal = new DuelPvpJournal(path);

            Assert.False(journal.Add("player-1", original: false));
            Assert.False(journal.HasPending);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"hdm-journal-{Guid.NewGuid():N}.txt");
}
