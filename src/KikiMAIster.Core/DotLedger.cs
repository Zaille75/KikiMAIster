namespace KikiMAIster.Core;

/// Who applied the stacks of a power (e.g. Poison, Thorns) on one creature, and from what origin.
/// Damage is split in proportion to the active stacks of each applier (spec 01-metrics §B).
/// Origin: "" for a card/power/relic, or "Potion:<ID>" when the stacks came from a potion (their damage then
/// counts as potion damage).
public sealed class DotLedger
{
    /// Applier 0 = stacks from a non-player source (enemy, unknown): never credited.
    private readonly Dictionary<Key, double> _stacks = new();

    /// Enabler: an ally whose buff made the applier apply these stacks (Concoct), 0 if none; EnablerSource: that buff.
    public readonly record struct Key(ulong Applier, string Origin, ulong Enabler = 0, string EnablerSource = "");

    public double Total => _stacks.Values.Sum();

    public void Apply(ulong applierId, double amount, string origin = "", ulong enabler = 0, string enablerSource = "")
    {
        if (amount > 0)
        {
            var key = new Key(applierId, origin, enabler, enablerSource);
            _stacks[key] = _stacks.GetValueOrDefault(key) + amount;
            return;
        }
        // Stacks removed (decay, cleanse): remove proportionally from every applier.
        var total = Total;
        if (total <= 0) return;
        var keep = Math.Max(0, total + amount) / total;
        foreach (var key in _stacks.Keys.ToList()) _stacks[key] *= keep;
    }

    /// Shares of each applier and origin (sum = 1), or empty when no stacks are known.
    public IEnumerable<(ulong ApplierId, string Origin, double Share)> Shares() =>
        Detailed().Select(d => (d.Key.Applier, d.Key.Origin, d.Share));

    /// Shares with the enabler of each part.
    public IEnumerable<(Key Key, double Share)> Detailed()
    {
        var total = Total;
        if (total <= 0) yield break;
        foreach (var (key, s) in _stacks)
            if (s > 0) yield return (key, s / total);
    }
}
