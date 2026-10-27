using System.Globalization;

namespace KikiMAIster.Core;

/// End-of-run trophies (spec 02-ui §3): who was the best of the run in each category, plus all-time records and
/// personal bests. The category list is a proposal, to be discussed with Zaille75.
public static class Trophies
{
    /// LowerIsBetter: the smallest value wins (only counted for a victory, else an early defeat would win).
    /// CoopOnly: needs allies. Fun: a "shame" trophy (most wasted…), shown after the others.
    public sealed record Category(string Key, Func<PlayerStats, double> Value, bool LowerIsBetter = false, bool CoopOnly = false,
        bool Fun = false);

    public static readonly Category[] All =
    {
        new("trophy.damage", s => s.DamageDealt),
        new("trophy.biggestHit", s => s.BiggestHit),
        new("trophy.bestCard", s => s.BestCardPlay),
        new("trophy.combo", s => s.MostCardsInTurn),
        new("trophy.kills", s => s.Kills),
        new("trophy.debuffs", s => s.DebuffsApplied.Values.Sum(t => t.Stacks)),
        new("trophy.prevented", s => s.PreventedSelf),
        new("trophy.saved", s => s.SavedForAllies, CoopOnly: true),
        new("trophy.enabled", s => s.DamageEnabledAllies, CoopOnly: true),
        new("trophy.taken", s => s.TakenForAllies, CoopOnly: true),
        new("trophy.energy", s => s.EnergyGiven, CoopOnly: true),
        new("trophy.cards", s => s.CardsGiven, CoopOnly: true),
        new("trophy.untouched", s => s.HpLost, LowerIsBetter: true),
        new("trophy.useless", s => s.UnusedBlock, Fun: true),
        new("trophy.missed", s => s.MissedTotal, Fun: true),
        new("trophy.friendly", s => s.FriendlyDamage + s.MissedByKind.GetValueOrDefault("allyBlockKept"), CoopOnly: true, Fun: true),
        new("trophy.deaths", s => s.Deaths, CoopOnly: true, Fun: true),
    };

    /// The winner of a category (null when nobody scored, or for a tie at 0).
    public static (ulong Id, double Value)? Winner(Category c, IReadOnlyDictionary<ulong, PlayerStats> run)
    {
        if (run.Count == 0 || (c.CoopOnly && run.Count < 2)) return null;
        var ranked = run.Select(kv => (Id: kv.Key, Value: c.Value(kv.Value)));
        var best = c.LowerIsBetter ? ranked.OrderBy(x => x.Value).ThenBy(x => x.Id).First()
            : ranked.OrderByDescending(x => x.Value).ThenBy(x => x.Id).First();
        return !c.LowerIsBetter && best.Value <= 0 ? null : best;
    }
}

/// All-time records, personal bests and records per character, per category; one run = one value per player.
/// Saved as "key=value|name|date" lines: "<category>.all" for the all-time record, "<category>.<playerId>" for
/// each player's personal best, "<category>.char.<CHARACTER>" for the best of each character (any player).
public sealed class RecordBook
{
    public sealed record Entry(double Value, string Name, string Date);

    private readonly Dictionary<string, Entry> _entries = new();
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public Entry? Best(string category) => _entries.GetValueOrDefault($"{category}.all");
    public Entry? Personal(string category, ulong playerId) => _entries.GetValueOrDefault($"{category}.{playerId}");
    public Entry? OfCharacter(string category, string character) => _entries.GetValueOrDefault($"{category}.char.{character}");

    /// Every character's record in a category ("IRONCLAD" → entry).
    public IEnumerable<(string Character, Entry Entry)> Characters(string category)
    {
        var prefix = $"{category}.char.";
        return _entries.Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal))
            .Select(kv => (kv.Key[prefix.Length..], kv.Value)).OrderBy(x => x.Item1, StringComparer.Ordinal);
    }

    /// What a finished run changed: new all-time records, new personal bests and new character records.
    public sealed record Update(HashSet<string> NewRecords, HashSet<(string Category, ulong Player)> NewPersonal,
        HashSet<(string Category, string Character)> NewCharacter);

    /// Adds a finished run. Lower-is-better categories only count a victory. Zero values never make a record.
    /// Characters: each player's character ("IRONCLAD"…); players without one get no character record.
    public Update Add(IReadOnlyDictionary<ulong, PlayerStats> run, IReadOnlyDictionary<ulong, string> names, bool victory, string date,
        IReadOnlyDictionary<ulong, string>? characters = null)
    {
        var update = new Update(new(), new(), new());
        foreach (var c in Trophies.All)
        {
            if (c.LowerIsBetter && !victory) continue;
            if (c.CoopOnly && run.Count < 2) continue;
            foreach (var (id, s) in run)
            {
                var v = c.Value(s);
                if (!c.LowerIsBetter && v <= 0) continue;
                var entry = new Entry(v, names.GetValueOrDefault(id, id.ToString(Inv)), date);
                if (Beats(c, v, Personal(c.Key, id)))
                {
                    _entries[$"{c.Key}.{id}"] = entry;
                    update.NewPersonal.Add((c.Key, id));
                }
                var character = characters?.GetValueOrDefault(id) ?? "";
                if (character != "" && Beats(c, v, OfCharacter(c.Key, character)))
                {
                    _entries[$"{c.Key}.char.{character}"] = entry;
                    update.NewCharacter.Add((c.Key, character));
                }
                if (Beats(c, v, Best(c.Key)))
                {
                    _entries[$"{c.Key}.all"] = entry;
                    update.NewRecords.Add(c.Key);
                }
            }
        }
        return update;
    }

    private static bool Beats(Trophies.Category c, double v, Entry? old) =>
        old == null || (c.LowerIsBetter ? v < old.Value : v > old.Value);

    public string Write() =>
        string.Join('\n', _entries.OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}={kv.Value.Value.ToString("R", Inv)}|{Clean(kv.Value.Name)}|{kv.Value.Date}")) + "\n";

    public static RecordBook Read(string text)
    {
        var book = new RecordBook();
        foreach (var line in text.Split('\n'))
        {
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var parts = line[(eq + 1)..].TrimEnd('\r').Split('|');
            if (parts.Length < 3 || !double.TryParse(parts[0], NumberStyles.Float, Inv, out var v)) continue;
            book._entries[line[..eq]] = new Entry(v, parts[1], parts[2]);
        }
        return book;
    }

    private static string Clean(string s) => s.Replace('|', '/').Replace('\n', ' ').Replace('\r', ' ');
}
