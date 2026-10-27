using System.Globalization;

namespace KikiMAIster.Core;

/// How often each group composition (the characters of the players, e.g. 2 Silent + 1 Ironclad) was played and won, per
/// ascension level (spec 02-ui v0.16.0, v0.16.1). One finished run = one game of its composition.
/// Saved as "A10|IRONCLAD+SILENT+SILENT=played;won". Ascension null in the queries = every level together.
public sealed class Compositions
{
    public sealed record Tally(int Played, int Won)
    {
        public double WinRate => Played > 0 ? (double)Won / Played : 0;
    }

    /// Key: (ascension, composition).
    private readonly Dictionary<(int Ascension, string Key), Tally> _tallies = new();
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// The composition key: characters sorted, joined with "+" (the order of the players does not matter).
    public static string KeyOf(IEnumerable<string> characters) =>
        string.Join('+', characters.Where(c => c != "").OrderBy(c => c, StringComparer.Ordinal));

    public static IReadOnlyList<string> Members(string key) => key == "" ? Array.Empty<string>() : key.Split('+');

    public Tally Get(string key, int? ascension = null)
    {
        var tallies = _tallies.Where(kv => kv.Key.Key == key && (ascension == null || kv.Key.Ascension == ascension)).Select(kv => kv.Value).ToList();
        return new Tally(tallies.Sum(t => t.Played), tallies.Sum(t => t.Won));
    }

    /// The ascension levels played, highest first.
    public IEnumerable<int> Ascensions() => _tallies.Keys.Select(k => k.Ascension).Distinct().OrderByDescending(a => a);

    /// The level with the most games (null when nothing was played).
    public int? MostPlayedAscension() =>
        _tallies.GroupBy(kv => kv.Key.Ascension).OrderByDescending(g => g.Sum(kv => kv.Value.Played)).ThenByDescending(g => g.Key)
            .Select(g => (int?)g.Key).FirstOrDefault();

    public void Add(IEnumerable<string> characters, int ascension, bool victory)
    {
        var key = KeyOf(characters);
        if (key == "") return;
        var t = _tallies.GetValueOrDefault((ascension, key)) ?? new Tally(0, 0);
        _tallies[(ascension, key)] = new Tally(t.Played + 1, t.Won + (victory ? 1 : 0));
    }

    /// Compositions played with this number of players, most played first.
    public IEnumerable<(string Key, Tally Tally)> Played(int players, int? ascension = null) =>
        _tallies.Keys.Select(k => k.Key).Distinct().Where(k => Members(k).Count == players)
            .Select(k => (Key: k, Tally: Get(k, ascension))).Where(x => x.Tally.Played > 0)
            .OrderByDescending(x => x.Tally.Played).ThenBy(x => x.Key, StringComparer.Ordinal);

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

    public IEnumerable<string> NeverPlayed(IReadOnlyList<string> characters, int players, int? ascension = null) =>
        AllPossible(characters, players).Where(k => Get(k, ascension).Played == 0);

    /// Played at least once, never won.
    public IEnumerable<string> NeverWon(IReadOnlyList<string> characters, int players, int? ascension = null) =>
        AllPossible(characters, players).Where(k => Get(k, ascension) is { Played: > 0, Won: 0 });

    public string Write() =>
        string.Join('\n', _tallies.OrderBy(kv => kv.Key.Ascension).ThenBy(kv => kv.Key.Key, StringComparer.Ordinal)
            .Select(kv => $"A{kv.Key.Ascension.ToString(Inv)}|{kv.Key.Key}={kv.Value.Played.ToString(Inv)};{kv.Value.Won.ToString(Inv)}")) + "\n";

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
            var name = line[..eq];
            var bar = name.IndexOf('|');
            var ascension = 0;
            if (bar > 1 && name[0] == 'A' && int.TryParse(name[1..bar], NumberStyles.None, Inv, out var a)) ascension = a;
            c._tallies[(ascension, KeyOf(name[(bar + 1)..].Split('+')))] = new Tally(played, won);
        }
        return c;
    }
}
