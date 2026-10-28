using System.Globalization;

namespace KikiMAIster.Core;

/// Rock-paper-scissors fights for a treasure relic two or more players picked (spec 01-metrics §C4), kept across runs:
/// for every pair of players, how many fights each one won against the other. A fight's winner beats every other player
/// in it. Saved as "pair.<a>.<b>=winsA;winsB" (a &lt; b), "name.<id>=Name" and "fight=<id>" (the latest fights, so a
/// reloaded treasure room is not counted twice).
public sealed class Chifumi
{
    private const int KeptFights = 300;
    private readonly Dictionary<(ulong A, ulong B), (int A, int B)> _pairs = new();
    private readonly Dictionary<ulong, string> _names = new();
    private readonly List<string> _fights = new();
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public string NameOf(ulong id) => _names.GetValueOrDefault(id, id.ToString(Inv));

    /// Adds a fight; false when this fight was already counted.
    public bool Record(string fightId, ulong winner, IEnumerable<ulong> players, IReadOnlyDictionary<ulong, string>? names = null)
    {
        if (_fights.Contains(fightId)) return false;
        _fights.Add(fightId);
        if (_fights.Count > KeptFights) _fights.RemoveRange(0, _fights.Count - KeptFights);
        foreach (var (id, name) in names ?? new Dictionary<ulong, string>())
            if (!string.IsNullOrWhiteSpace(name)) _names[id] = name;
        foreach (var loser in players.Distinct().Where(p => p != winner))
        {
            var key = winner < loser ? (winner, loser) : (loser, winner);
            var (a, b) = _pairs.GetValueOrDefault(key);
            _pairs[key] = winner < loser ? (a + 1, b) : (a, b + 1);
        }
        return true;
    }

    /// Fights won by `player` against `other`, and lost.
    public (int Won, int Lost) Against(ulong player, ulong other)
    {
        if (player == other) return (0, 0);
        var (a, b) = _pairs.GetValueOrDefault(player < other ? (player, other) : (other, player));
        return player < other ? (a, b) : (b, a);
    }

    /// Every pair that fought at least once.
    public IEnumerable<(ulong A, ulong B, int WinsA, int WinsB)> Pairs() =>
        _pairs.Where(kv => kv.Value.A + kv.Value.B > 0).Select(kv => (kv.Key.A, kv.Key.B, kv.Value.A, kv.Value.B));

    public string Write()
    {
        var lines = _pairs.OrderBy(kv => kv.Key.A).ThenBy(kv => kv.Key.B)
            .Select(kv => $"pair.{kv.Key.A.ToString(Inv)}.{kv.Key.B.ToString(Inv)}={kv.Value.A.ToString(Inv)};{kv.Value.B.ToString(Inv)}")
            .Concat(_names.OrderBy(kv => kv.Key).Select(kv => $"name.{kv.Key.ToString(Inv)}={Clean(kv.Value)}"))
            .Concat(_fights.Select(f => $"fight={Clean(f)}"));
        return string.Join('\n', lines) + "\n";
    }

    public static Chifumi Read(string text)
    {
        var c = new Chifumi();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq];
            var value = line[(eq + 1)..];
            var parts = key.Split('.');
            if (key == "fight") c._fights.Add(value);
            else if (parts[0] == "name" && parts.Length == 2 && ulong.TryParse(parts[1], NumberStyles.None, Inv, out var id)) c._names[id] = value;
            else if (parts[0] == "pair" && parts.Length == 3 && ulong.TryParse(parts[1], NumberStyles.None, Inv, out var a) &&
                     ulong.TryParse(parts[2], NumberStyles.None, Inv, out var b) && value.Split(';') is { Length: 2 } wins &&
                     int.TryParse(wins[0], NumberStyles.None, Inv, out var wa) && int.TryParse(wins[1], NumberStyles.None, Inv, out var wb))
                c._pairs[a < b ? (a, b) : (b, a)] = a < b ? (wa, wb) : (wb, wa);
        }
        return c;
    }

    private static string Clean(string s) => s.Replace('\n', ' ').Replace('\r', ' ');
}
