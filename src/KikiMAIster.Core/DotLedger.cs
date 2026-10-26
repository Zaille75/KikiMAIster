namespace KikiMAIster.Core;

/// Who applied the stacks of a damage-over-time power (e.g. Poison) on one creature.
/// Damage is split in proportion to the active stacks of each applier (spec 01-metrics §B).
public sealed class DotLedger
{
    /// Key 0 = stacks from a non-player source (enemy, unknown): never credited.
    private readonly Dictionary<ulong, double> _stacks = new();

    public double Total => _stacks.Values.Sum();

    public void Apply(ulong applierId, double amount)
    {
        if (amount > 0)
        {
            _stacks[applierId] = _stacks.GetValueOrDefault(applierId) + amount;
            return;
        }
        // Stacks removed (decay, cleanse): remove proportionally from every applier.
        var total = Total;
        if (total <= 0) return;
        var keep = Math.Max(0, total + amount) / total;
        foreach (var id in _stacks.Keys.ToList()) _stacks[id] *= keep;
    }

    /// Shares of each applier (sum = 1), or empty when no stacks are known.
    public IEnumerable<(ulong ApplierId, double Share)> Shares()
    {
        var total = Total;
        if (total <= 0) yield break;
        foreach (var (id, s) in _stacks)
            if (s > 0) yield return (id, s / total);
    }
}
