using System.Globalization;

namespace KikiMAIster.Core;

/// Fastest wins of each encounter, from run to run (spec 01-metrics §C5, v0.17.39): fewest rounds and shortest real time,
/// kept apart by ascension level, number of players and, in solo, character. Saved as "key.turns=4|date" and
/// "key.seconds=95.2|date" lines; key = "A10.2p.<ENCOUNTER>" (co-op) or "A10.1p.<CHARACTER>.<ENCOUNTER>" (solo).
public sealed class FightRecords
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public sealed record Entry(double Value, string Date);

    /// What a won fight changed. Old values are null when the encounter had no record yet (first win: stored, not a
    /// record broken).
    public sealed record Result(double? OldTurns, bool NewTurns, double? OldSeconds, bool NewSeconds)
    {
        public bool Broken => NewTurns || NewSeconds;
    }

    private readonly Dictionary<string, Entry> _entries = new();

    public static string Key(int ascension, int players, string character, string encounter) =>
        players == 1 && character != ""
            ? $"A{ascension.ToString(Inv)}.1p.{character}.{encounter}"
            : $"A{ascension.ToString(Inv)}.{players.ToString(Inv)}p.{encounter}";

    public Entry? Turns(string key) => _entries.GetValueOrDefault($"{key}.turns");
    public Entry? Seconds(string key) => _entries.GetValueOrDefault($"{key}.seconds");

    /// A won fight: keeps the fewest rounds and the shortest time (0 seconds = unknown, ignored).
    public Result Add(string key, int turns, double seconds, string date)
    {
        var oldTurns = Turns(key)?.Value;
        var oldSeconds = Seconds(key)?.Value;
        var newTurns = turns > 0 && (oldTurns == null || turns < oldTurns);
        var newSeconds = seconds > 0 && (oldSeconds == null || seconds < oldSeconds);
        if (newTurns) _entries[$"{key}.turns"] = new Entry(turns, date);
        if (newSeconds) _entries[$"{key}.seconds"] = new Entry(Math.Round(seconds, 1), date);
        return new Result(oldTurns, newTurns && oldTurns != null, oldSeconds, newSeconds && oldSeconds != null);
    }

    public string Write() =>
        string.Join('\n', _entries.OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}={kv.Value.Value.ToString("R", Inv)}|{kv.Value.Date}")) + "\n";

    public static FightRecords Read(string text)
    {
        var book = new FightRecords();
        foreach (var line in text.Split('\n'))
        {
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var parts = line[(eq + 1)..].TrimEnd('\r').Split('|');
            if (double.TryParse(parts[0], NumberStyles.Float, Inv, out var v))
                book._entries[line[..eq]] = new Entry(v, parts.Length > 1 ? parts[1] : "");
        }
        return book;
    }
}
