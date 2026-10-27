using System.Globalization;

namespace KikiMAIster.Core;

/// How often each group composition (the characters of the players, e.g. 2 Silent + 1 Ironclad) was played and won
/// (spec 02-ui v0.16.0). One finished run = one game of its composition. Saved as "IRONCLAD+SILENT+SILENT=played;won".
public sealed class Compositions
{
    public sealed record Tally(int Played, int Won)
    {
        public double WinRate => Played > 0 ? (double)Won / Played : 0;
    }

    private readonly Dictionary<string, Tally> _tallies = new();
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// The composition key: characters sorted, joined with "+" (the order of the players does not matter).
    public static string KeyOf(IEnumerable<string> characters) =>
        string.Join('+', characters.Where(c => c != "").OrderBy(c => c, StringComparer.Ordinal));

    public static IReadOnlyList<string> Members(string key) => key == "" ? Array.Empty<string>() : key.Split('+');

    public Tally Get(string key) => _tallies.GetValueOrDefault(key) ?? new Tally(0, 0);

    public void Add(IEnumerable<string> characters, bool victory)
    {
        var key = KeyOf(characters);
        if (key == "") return;
        var t = Get(key);
        _tallies[key] = new Tally(t.Played + 1, t.Won + (victory ? 1 : 0));
    }

    /// Compositions played with this number of players, most played first.
    public IEnumerable<(string Key, Tally Tally)> Played(int players) =>
        _tallies.Where(kv => Members(kv.Key).Count == players)
            .OrderByDescending(kv => kv.Value.Played).ThenBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (kv.Key, kv.Value));

    /// Every possible composition for this number of players (a character can be taken several times).
    public static IEnumerable<string> AllPossible(IReadOnlyList<string> characters, int players)
    {
        var sorted = characters.Distinct().OrderBy(c => c, StringComparer.Ordinal).ToList();
        IEnumerable<List<string>> Build(int start, int left)
        {
            if (left == 0)
            {
                yield return new List<string>();
                yield break;
            }
            for (var i = start; i < sorted.Count; i++)
                foreach (var rest in Build(i, left - 1))
                {
                    rest.Insert(0, sorted[i]);
                    yield return rest;
                }
        }
        return players <= 0 ? Enumerable.Empty<string>() : Build(0, players).Select(KeyOf);
    }

    public IEnumerable<string> NeverPlayed(IReadOnlyList<string> characters, int players) =>
        AllPossible(characters, players).Where(k => Get(k).Played == 0);

    /// Played at least once, never won.
    public IEnumerable<string> NeverWon(IReadOnlyList<string> characters, int players) =>
        AllPossible(characters, players).Where(k => Get(k) is { Played: > 0, Won: 0 });

    public string Write() =>
        string.Join('\n', _tallies.OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}={kv.Value.Played.ToString(Inv)};{kv.Value.Won.ToString(Inv)}")) + "\n";

    public static Compositions Read(string text)
    {
        var c = new Compositions();
        foreach (var line in text.Split('\n'))
        {
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var parts = line[(eq + 1)..].TrimEnd('\r').Split(';');
            if (parts.Length < 2 || !int.TryParse(parts[0], NumberStyles.None, Inv, out var played) ||
                !int.TryParse(parts[1], NumberStyles.None, Inv, out var won)) continue;
            c._tallies[KeyOf(line[..eq].Split('+'))] = new Tally(played, won);
        }
        return c;
    }
}
