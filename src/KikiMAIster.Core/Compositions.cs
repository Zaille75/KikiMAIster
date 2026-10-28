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

    /// How the played compositions are listed (spec 02-ui v0.17.0): by games or by win rate, in either direction.
    public enum Order { Games, WinRate }

    /// Compositions played with this number of players, most played first (or as asked; ties: the other figure, then the name).
    public IEnumerable<(string Key, Tally Tally)> Played(int players, int? ascension = null, Order order = Order.Games, bool descending = true)
    {
        var list = _tallies.Keys.Select(k => k.Key).Distinct().Where(k => Members(k).Count == players)
            .Select(k => (Key: k, Tally: Get(k, ascension))).Where(x => x.Tally.Played > 0);
        Func<(string Key, Tally Tally), double> first = order == Order.Games ? x => x.Tally.Played : x => x.Tally.WinRate;
        Func<(string Key, Tally Tally), double> second = order == Order.Games ? x => x.Tally.WinRate : x => x.Tally.Played;
        var sorted = descending ? list.OrderByDescending(first).ThenByDescending(second) : list.OrderBy(first).ThenBy(second);
        return sorted.ThenBy(x => x.Key, StringComparer.Ordinal);
    }

    /// A finished run as the game's history knows it: its players (ids), their characters, ascension and result.
    public sealed record Run(IReadOnlyList<ulong> Players, IReadOnlyList<string> Characters, int Ascension, bool Victory);

    /// The tallies of the runs played by exactly this group of players (any order; spec 02-ui v0.17.0).
    public static Compositions OfGroup(IEnumerable<Run> runs, IReadOnlyCollection<ulong> group)
    {
        var c = new Compositions();
        var wanted = group.ToHashSet();
        foreach (var run in runs.Where(r => r.Players.Count == wanted.Count && r.Players.ToHashSet().SetEquals(wanted)))
            c.Add(run.Characters, run.Ascension, run.Victory);
        return c;
    }

    /// Two-player runs as a grid (spec 02-ui v0.17.7): my character × my partner's character (the order matters:
    /// I play Ironclad and he plays Silent is another cell than the reverse). Partner null = any partner.
    /// Ascension null = every level.
    public static Dictionary<(string Mine, string Theirs), Tally> Grid(IEnumerable<Run> runs, ulong me, ulong? partner, int? ascension)
    {
        var grid = new Dictionary<(string, string), Tally>();
        foreach (var run in runs)
        {
            if (run.Players.Count != 2 || (ascension != null && run.Ascension != ascension)) continue;
            var i = run.Players.ToList().IndexOf(me);
            if (i < 0 || (partner != null && run.Players[1 - i] != partner)) continue;
            var key = (run.Characters[i], run.Characters[1 - i]);
            var t = grid.GetValueOrDefault(key) ?? new Tally(0, 0);
            grid[key] = new Tally(t.Played + 1, t.Won + (run.Victory ? 1 : 0));
        }
        return grid;
    }

    /// Per player (spec 02-ui v0.17.8, 3–4 players): the runs of this size in which each of these players played each
    /// character. GroupOnly: only the runs of exactly these players; else every run of that player with this many players.
    public static Dictionary<(ulong Player, string Character), List<Run>> PerPlayer(IEnumerable<Run> runs, IReadOnlyCollection<ulong> players,
        int size, bool groupOnly, int? ascension)
    {
        var wanted = players.ToHashSet();
        var result = new Dictionary<(ulong, string), List<Run>>();
        foreach (var run in runs)
        {
            if (run.Players.Count != size || (ascension != null && run.Ascension != ascension)) continue;
            if (groupOnly && !run.Players.ToHashSet().SetEquals(wanted)) continue;
            for (var i = 0; i < run.Players.Count; i++)
            {
                if (!wanted.Contains(run.Players[i])) continue;
                var key = (run.Players[i], run.Characters[i]);
                if (!result.TryGetValue(key, out var list)) result[key] = list = new();
                list.Add(run);
            }
        }
        return result;
    }

    /// The tallies of every run.
    public static Compositions Of(IEnumerable<Run> runs)
    {
        var c = new Compositions();
        foreach (var run in runs) c.Add(run.Characters, run.Ascension, run.Victory);
        return c;
    }

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
