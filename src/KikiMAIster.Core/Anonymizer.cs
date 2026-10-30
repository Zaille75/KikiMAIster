using System.Globalization;
using System.Text.RegularExpressions;

namespace KikiMAIster.Core;

/// Removes personal information from a report's text before it is written (spec 03-technical, "Reports"). Every player
/// seen this session becomes "Joueur 1", "Joueur 2"… in the order seen: their names become "Joueur N" and their Steam ids
/// "id-joueur-N". Other Steam ids, profile paths and the account / computer names are masked too.
/// Thread-safe: names are registered from the game thread and errors from any thread.
public sealed class Anonymizer
{
    /// Shorter names are left alone, so they cannot replace parts of ordinary words.
    private const int MinNameLength = 3;

    /// Smaller ids are local player numbers (solo, tests), not Steam ids: replacing "1" would ruin the text.
    private const ulong MinId = 1_000_000;

    private readonly object _lock = new();
    private readonly List<ulong> _ids = new();
    private readonly Dictionary<string, int> _names = new(StringComparer.OrdinalIgnoreCase);
    private string? _user;
    private string? _machine;

    private static readonly Regex ProfilePath = new(@"(?i)(\\Users\\|/Users/|/home/)[^\\/\r\n""'<>|:*?]+", RegexOptions.Compiled);
    private static readonly Regex SteamId = new(@"(?<!\d)\d{17}(?!\d)", RegexOptions.Compiled);

    /// A player seen this session. A null or empty name only registers the id.
    public void Register(ulong id, string? name)
    {
        lock (_lock)
        {
            var n = Number(id);
            if (n == 0 || string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();
            if (name.Length >= MinNameLength && name != id.ToString(CultureInfo.InvariantCulture)) _names.TryAdd(name, n);
        }
    }

    /// The account and computer names (Environment.UserName / MachineName).
    public void RegisterMachine(string? user, string? machine)
    {
        lock (_lock)
        {
            _user = user is { Length: >= MinNameLength } ? user : null;
            _machine = machine is { Length: >= MinNameLength } ? machine : null;
        }
    }

    /// The number of this player ("Joueur N"), 0 when the id is not a Steam id.
    private int Number(ulong id)
    {
        if (id < MinId) return 0;
        var i = _ids.IndexOf(id);
        if (i >= 0) return i + 1;
        _ids.Add(id);
        return _ids.Count;
    }

    public string Scrub(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        List<(string Name, int N)> names;
        List<ulong> ids;
        string? user, machine;
        lock (_lock)
        {
            // Longest first: "Kiki Max" is replaced before "Kiki".
            names = _names.Select(kv => (kv.Key, kv.Value)).OrderByDescending(x => x.Key.Length).ToList();
            ids = _ids.ToList();
            user = _user;
            machine = _machine;
        }
        for (var i = 0; i < ids.Count; i++)
            text = Whole(text, ids[i].ToString(CultureInfo.InvariantCulture), $"id-joueur-{i + 1}");
        text = SteamId.Replace(text, "[steam-id]");
        text = ProfilePath.Replace(text, m => m.Groups[1].Value + "[user]");
        foreach (var (name, n) in names) text = Whole(text, name, $"Joueur {n}");
        if (user != null) text = Whole(text, user, "[user]");
        if (machine != null) text = Whole(text, machine, "[pc]");
        return text;
    }

    /// Replaces the value only where it is not part of a longer word or number ("Kiki" stays in "KikiMAIster").
    private static string Whole(string text, string value, string replacement) =>
        Regex.Replace(text, $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(value)}(?![\p{{L}}\p{{N}}_])", replacement.Replace("$", "$$"),
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
