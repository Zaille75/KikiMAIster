using System.Globalization;
using KikiMAIster.Core;

namespace KikiMAIster.UI;

/// Fastest wins per encounter, from run to run (`KikiMAIster.fights.cfg` next to the DLL, v0.17.39), and the notice shown
/// in the combat summary when a fight beats one.
public static class FightRecordStore
{
    private static FightRecords? _book;

    /// The notice of each fight of this session that broke a record, by fight number.
    public static readonly Dictionary<int, string> Notes = new();

    private static string? FilePath
    {
        get
        {
            var dir = Path.GetDirectoryName(typeof(FightRecordStore).Assembly.Location);
            return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "KikiMAIster.fights.cfg");
        }
    }

    private static FightRecords Book
    {
        get
        {
            if (_book != null) return _book;
            _book = new FightRecords();
            Capture.Safe("FightRecordStore.Load", () =>
            {
                if (FilePath != null && File.Exists(FilePath)) _book = FightRecords.Read(File.ReadAllText(FilePath));
            });
            return _book;
        }
    }

    /// A won fight: updates the records of its encounter and remembers the notice when one was beaten.
    public static void Add(string key, int fight, int turns, double seconds)
    {
        var result = Book.Add(key, turns, seconds, DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        if (FilePath != null) File.WriteAllText(FilePath, Book.Write());
        ModLog.Info($"fight record {key}: {turns} rounds, {seconds:F1} s → new rounds {result.NewTurns} (was {result.OldTurns}), " +
                    $"new time {result.NewSeconds} (was {result.OldSeconds})");
        if (!result.Broken) return;
        var parts = new List<string>();
        if (result.NewTurns)
            parts.Add(string.Format(Loc.T("record.turns"), turns, result.OldTurns!.Value.ToString("0", CultureInfo.InvariantCulture)));
        if (result.NewSeconds) parts.Add(string.Format(Loc.T("record.time"), Duration(seconds), Duration(result.OldSeconds!.Value)));
        Notes[fight] = "★ " + Loc.T("record.new") + " " + string.Join(" · ", parts);
    }

    /// "3:42" (or "1:02:05").
    public static string Duration(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Round(seconds));
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : t.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }
}
