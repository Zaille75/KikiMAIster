using System.Globalization;
using System.Reflection;

namespace KikiMAIster.Core;

/// Saves and restores the run-scope stats and the finished combats (stats + chart) as plain "key=value" lines, so a run
/// continued after Save & Quit keeps its totals and its previous fights. Written after each combat, when the game also
/// saves the run. Combat lines: "combat.N=name|1" (1 = victory), "combat.N.r.R.<player>=damage;prevented;hpLost;debuffs;saved",
/// "combat.N.s.<player>.<stat>=…" (same stat lines as the run).
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
            WriteStats(lines, $"{id}", s);
        }
        foreach (var c in engine.Combats)
        {
            lines.Add($"combat.{c.Number}={Clean(c.Name)}|{(c.Victory == true ? 1 : 0)}");
            foreach (var point in c.Rounds)
                foreach (var (id, t) in point.Players)
                    lines.Add($"combat.{c.Number}.r.{point.Round}.{id}=" + string.Join(';',
                        new[] { t.Damage, t.Prevented, t.HpLost, t.DebuffStacks, t.Saved }.Select(v => v.ToString("R", Inv))));
            foreach (var (id, s) in c.Stats) WriteStats(lines, $"combat.{c.Number}.s.{id}", s);
        }
        return string.Join('\n', lines) + "\n";
    }

    private static void WriteStats(List<string> lines, string prefix, PlayerStats s)
    {
        foreach (var p in Numbers)
        {
            var v = (double)p.GetValue(s)!;
            if (v != 0) lines.Add($"{prefix}.{p.Name}={v.ToString("R", Inv)}");
        }
        foreach (var p in Amounts)
            foreach (var (k, v) in (Dictionary<string, double>)p.GetValue(s)!)
                lines.Add($"{prefix}.{p.Name}.{k}={v.ToString("R", Inv)}");
        foreach (var p in Tallies)
            foreach (var (k, t) in (Dictionary<string, PowerTally>)p.GetValue(s)!)
                lines.Add($"{prefix}.{p.Name}.{k}={t.Count};{t.Stacks.ToString("R", Inv)}");
        // The explanations of the misses (v0.17.17: kept across Save & Quit), the latest ones of each list.
        void List<T>(string name, IEnumerable<T> items, Func<T, IEnumerable<string>> fields)
        {
            var i = 0;
            foreach (var item in items.TakeLast(KeptExplanations))
                lines.Add($"{prefix}.list.{name}.{i++}=" + string.Join(';', fields(item).Select(Uri.EscapeDataString)));
        }
        List("MissedEntries", s.MissedEntries, m => new[] { I(m.Round), m.Kind, D(m.Amount), I(m.Combat), Join(m.Args) });
        List("MissedLethalTurns", s.MissedLethalTurns, m => new[] { I(m.Round), I(m.EnemiesHp), I(m.Combat), Join(m.Cards) });
        List("WeakAttackPlays", s.WeakAttackPlays, w => new[] { I(w.Round), w.Played, I(w.PlayedDamage), w.Better, I(w.BetterDamage), I(w.Combat) });
        List("WastedPlays", s.WastedPlays, w => new[] { I(w.PlayId), I(w.Round), I(w.Combat), D(w.Amount), w.Choice.BlockCard, w.Choice.Alternative,
            I(w.Choice.BlockAtTurnEnd), I(w.Choice.IncomingDamage), Join(w.Choice.UsefulInHand) });
    }

    private const int KeptExplanations = 40;
    private static string I(int v) => v.ToString(Inv);
    private static string D(double v) => v.ToString("R", Inv);
    private static string Join(IEnumerable<string> items) => string.Join(',', items.Select(Uri.EscapeDataString));
    private static List<string> Split(string text) => text == "" ? new List<string>() : text.Split(',').Select(Uri.UnescapeDataString).ToList();

    /// One saved explanation: "list.<Name>.<index>" = escaped fields separated by ';'.
    private static void ReadExplanation(PlayerStats s, string name, string value)
    {
        var f = value.Split(';').Select(Uri.UnescapeDataString).ToArray();
        int Int(int k) => int.Parse(f[k], Inv);
        switch (name)
        {
            case "MissedEntries":
                s.MissedEntries.Add(new MissedEntry(Int(0), f[1], Split(f[4]), double.Parse(f[2], Inv), Int(3)));
                break;
            case "MissedLethalTurns":
                s.MissedLethalTurns.Add(new MissedLethalTurn(Int(0), Split(f[3]), Int(1), Int(2)));
                break;
            case "WeakAttackPlays":
                s.WeakAttackPlays.Add(new WeakAttackPlay(Int(0), f[1], Int(2), f[3], Int(4), Int(5)));
                break;
            case "WastedPlays":
                s.WastedPlays.Add(new WastedPlay(Int(0), Int(1), new BlockChoice(f[4], f[5], Split(f[8]), Int(6), Int(7)), Int(2))
                    { Amount = double.Parse(f[3], Inv) });
                break;
        }
    }

    public sealed record Snapshot(string RunKey, int Floor, IReadOnlyList<PlayerInfo> Players, Dictionary<ulong, PlayerStats> Stats,
        IReadOnlyList<CombatRecord> Combats);

    /// A combat being read back.
    private sealed class CombatParts
    {
        public string Name = "";
        public bool Victory;
        public readonly SortedDictionary<int, Dictionary<ulong, RoundTotals>> Rounds = new();
        public readonly Dictionary<ulong, PlayerStats> Stats = new();
    }

    /// Returns null when the text is not a valid snapshot. Unknown keys are ignored (older/newer versions).
    public static Snapshot? Read(string text)
    {
        string? run = null;
        var floor = -1;
        var players = new List<PlayerInfo>();
        var stats = new Dictionary<ulong, PlayerStats>();
        var combats = new SortedDictionary<int, CombatParts>();
        PlayerStats Of(ulong id) => stats.TryGetValue(id, out var s) ? s : stats[id] = new PlayerStats();
        CombatParts Combat(int n) => combats.TryGetValue(n, out var c) ? c : combats[n] = new CombatParts();

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
                if (key.StartsWith("combat."))
                {
                    var parts = key.Split('.', 5);
                    var c = Combat(int.Parse(parts[1], Inv));
                    if (parts.Length == 2)
                    {
                        var nv = value.Split('|');
                        c.Name = nv[0];
                        c.Victory = nv.Length > 1 && nv[1] == "1";
                    }
                    else if (parts[2] == "r" && parts.Length == 5)
                    {
                        var v = value.Split(';').Select(x => double.Parse(x, Inv)).ToArray();
                        if (!c.Rounds.TryGetValue(int.Parse(parts[3], Inv), out var round)) c.Rounds[int.Parse(parts[3], Inv)] = round = new();
                        round[ulong.Parse(parts[4], Inv)] = new RoundTotals(v[0], v[1], v[2], v[3], v.Length > 4 ? v[4] : 0);
                    }
                    else if (parts[2] == "s" && parts.Length >= 4)
                    {
                        var rest = key[(key.IndexOf(".s.", StringComparison.Ordinal) + 3)..];
                        var dot = rest.IndexOf('.');
                        if (dot > 0 && ulong.TryParse(rest[..dot], NumberStyles.None, Inv, out var cid))
                            ReadStat(c.Stats.TryGetValue(cid, out var cs) ? cs : c.Stats[cid] = new PlayerStats(), rest[(dot + 1)..], value);
                    }
                    continue;
                }
                var dot1 = key.IndexOf('.');
                if (dot1 <= 0 || !ulong.TryParse(key[..dot1], NumberStyles.None, Inv, out var pid)) continue;
                ReadStat(Of(pid), key[(dot1 + 1)..], value);
            }
            catch (FormatException) { }
            catch (OverflowException) { }
            catch (IndexOutOfRangeException) { }
        }
        var records = combats.Select(kv => new CombatRecord(kv.Key, kv.Value.Name, kv.Value.Victory,
            kv.Value.Rounds.Select(r => new RoundPoint(r.Key, r.Value)).ToList(), kv.Value.Stats)).ToList();
        return run == null || floor < 0 ? null : new Snapshot(run, floor, players, stats, records);
    }

    /// One stat line of a player: "<stat>" or "<stat>.<key>" (dictionaries).
    private static void ReadStat(PlayerStats s, string rest, string value)
    {
        var dot = rest.IndexOf('.');
        var name = dot < 0 ? rest : rest[..dot];
        var sub = dot < 0 ? null : rest[(dot + 1)..];
        if (name == "list" && sub != null && sub.IndexOf('.') is > 0 and var listDot)
            ReadExplanation(s, sub[..listDot], value);
        else if (sub == null)
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

    private static string Clean(string s) => s.Replace('|', '/').Replace('\n', ' ').Replace('\r', ' ');
}
