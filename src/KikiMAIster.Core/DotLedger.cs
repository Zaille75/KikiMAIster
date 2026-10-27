namespace KikiMAIster.Core;

/// Who applied the stacks of a power (e.g. Poison, Thorns) on one creature, and from what origin.
/// Damage is split in proportion to the active stacks of each applier (spec 01-metrics §B).
/// Origin: "" for a card/power/relic, or "Potion:<ID>" when the stacks came from a potion (their damage then
/// counts as potion damage).
public sealed class DotLedger
{
    /// Applier 0 = stacks from a non-player source (enemy, unknown): never credited.
    private readonly Dictionary<(ulong Applier, string Origin), double> _stacks = new();

    public double Total => _stacks.Values.Sum();

    public void Apply(ulong applierId, double amount, string origin = "")
    {
        if (amount > 0)
        {
            _stacks[(applierId, origin)] = _stacks.GetValueOrDefault((applierId, origin)) + amount;
            return;
        }
        // Stacks removed (decay, cleanse): remove proportionally from every applier.
        var total = Total;
        if (total <= 0) return;
        var keep = Math.Max(0, total + amount) / total;
        foreach (var key in _stacks.Keys.ToList()) _stacks[key] *= keep;
    }

    /// Shares of each applier and origin (sum = 1), or empty when no stacks are known.
    public IEnumerable<(ulong ApplierId, string Origin, double Share)> Shares()
    {
        var total = Total;
        if (total <= 0) yield break;
        foreach (var ((id, origin), s) in _stacks)
            if (s > 0) yield return (id, origin, s / total);
    }
}
