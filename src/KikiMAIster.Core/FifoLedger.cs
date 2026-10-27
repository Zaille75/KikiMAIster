namespace KikiMAIster.Core;

/// Who is responsible for a duration debuff (Vulnerable, Weak) on one creature, turn by turn: stacks are queued
/// in the order they were applied and each turn lost removes the oldest one (spec 01-metrics §B2).
/// Example: A applies 2 Vulnerable, then B applies 2 → turns 1-2 count for A, turns 3-4 for B.
public sealed class FifoLedger
{
    private sealed class Chunk(ulong applier)
    {
        public ulong Applier { get; } = applier;
        public double Amount { get; set; }
    }

    private readonly LinkedList<Chunk> _chunks = new();

    public void Apply(ulong applierId, double amount)
    {
        if (amount > 0)
        {
            _chunks.AddLast(new Chunk(applierId) { Amount = amount });
            return;
        }
        var remove = -amount;
        while (remove > 0 && _chunks.First is { } node)
        {
            var take = Math.Min(remove, node.Value.Amount);
            node.Value.Amount -= take;
            remove -= take;
            if (node.Value.Amount <= 1e-9) _chunks.RemoveFirst();
        }
    }

    /// The player whose stacks are active now (0 = none, or an enemy's own stacks).
    public ulong Current => _chunks.First?.Value.Applier ?? 0;

    /// Each applier's share of the turns still queued (sum = 1; applier 0 = enemy or unknown).
    public IEnumerable<(ulong ApplierId, double Share)> Remaining()
    {
        var total = _chunks.Sum(c => c.Amount);
        if (total <= 0) yield break;
        foreach (var g in _chunks.GroupBy(c => c.Applier)) yield return (g.Key, g.Sum(c => c.Amount) / total);
    }
}
