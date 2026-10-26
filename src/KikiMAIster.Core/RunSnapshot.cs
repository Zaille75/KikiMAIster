using System.Globalization;
using System.Reflection;

namespace KikiMAIster.Core;

/// Saves and restores the run-scope stats as plain "key=value" lines, so a run continued after
/// Save & Quit keeps its totals. Written after each combat, when the game also saves the run.
public static class RunSnapshot
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static readonly PropertyInfo[] Numbers = typeof(PlayerStats).GetProperties()
        .Where(p => p.PropertyType == typeof(double) && p.CanWrite).ToArray();

    private static readonly PropertyInfo[] Amounts = typeof(PlayerStats).GetProperties()
        .Where(p => p.PropertyType == typeof(Dictionary<string, double>)).ToArray();

    private static readonly PropertyInfo[] Tallies = typeof(PlayerStats).GetProperties()
        .Where(p => p.PropertyType == typeof(Dictionary<string, PowerTally>)).ToArray();

    public static string Write(string runKey, int floor, StatsEngine engine)
    {
        var lines = new List<string> { "format=1", $"run={runKey}", $"floor={floor}" };
        foreach (var (id, s) in engine.Get(Scope.Run))
        {
            var info = engine.Players.GetValueOrDefault(id);
            lines.Add($"player.{id}={Clean(info?.Name ?? "")}|{Clean(info?.Character ?? "")}");
            foreach (var p in Numbers)
            {
                var v = (double)p.GetValue(s)!;
                if (v != 0) lines.Add($"{id}.{p.Name}={v.ToString("R", Inv)}");
            }
            foreach (var p in Amounts)
                foreach (var (k, v) in (Dictionary<string, double>)p.GetValue(s)!)
                    lines.Add($"{id}.{p.Name}.{k}={v.ToString("R", Inv)}");
            foreach (var p in Tallies)
                foreach (var (k, t) in (Dictionary<string, PowerTally>)p.GetValue(s)!)
                    lines.Add($"{id}.{p.Name}.{k}={t.Count};{t.Stacks.ToString("R", Inv)}");
        }
        return string.Join('\n', lines) + "\n";
    }

    public sealed record Snapshot(string RunKey, int Floor, IReadOnlyList<PlayerInfo> Players, Dictionary<ulong, PlayerStats> Stats);

    /// Returns null when the text is not a valid snapshot. Unknown keys are ignored (older/newer versions).
    public static Snapshot? Read(string text)
    {
        string? run = null;
        var floor = -1;
        var players = new List<PlayerInfo>();
        var stats = new Dictionary<ulong, PlayerStats>();
        PlayerStats Of(ulong id) => stats.TryGetValue(id, out var s) ? s : stats[id] = new PlayerStats();

        foreach (var line in text.Split('\n'))
        {
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq];
            var value = line[(eq + 1)..].TrimEnd('\r');
            try
            {
                if (key == "run") { run = value; continue; }
                if (key == "floor") { floor = int.Parse(value, Inv); continue; }
                if (key.StartsWith("player."))
                {
                    var id = ulong.Parse(key[7..], Inv);
                    var parts = value.Split('|');
                    players.Add(new PlayerInfo(id, parts[0], parts.Length > 1 ? parts[1] : ""));
                    Of(id);
                    continue;
                }
                var dot1 = key.IndexOf('.');
                if (dot1 <= 0 || !ulong.TryParse(key[..dot1], NumberStyles.None, Inv, out var pid)) continue;
                var rest = key[(dot1 + 1)..];
                var dot2 = rest.IndexOf('.');
                var name = dot2 < 0 ? rest : rest[..dot2];
                var sub = dot2 < 0 ? null : rest[(dot2 + 1)..];
                var s = Of(pid);
                if (sub == null)
                    Numbers.FirstOrDefault(p => p.Name == name)?.SetValue(s, double.Parse(value, Inv));
                else if (Amounts.FirstOrDefault(p => p.Name == name) is { } a)
                    ((Dictionary<string, double>)a.GetValue(s)!)[sub] = double.Parse(value, Inv);
                else if (Tallies.FirstOrDefault(p => p.Name == name) is { } t)
                {
                    var parts = value.Split(';');
                    ((Dictionary<string, PowerTally>)t.GetValue(s)!)[sub] = new PowerTally
                    {
                        Count = int.Parse(parts[0], Inv), Stacks = double.Parse(parts[1], Inv)
                    };
                }
            }
            catch (FormatException) { }
            catch (OverflowException) { }
            catch (IndexOutOfRangeException) { }
        }
        return run == null || floor < 0 ? null : new Snapshot(run, floor, players, stats);
    }

    private static string Clean(string s) => s.Replace('|', '/').Replace('\n', ' ').Replace('\r', ' ');
}
