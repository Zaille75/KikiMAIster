namespace KikiMAIster.Core;

/// Block layers on one player's creature, consumed first-in first-out (spec 01-metrics §F).
public sealed class BlockLedger
{
    public sealed record Layer(ulong GiverId, int Round, int PlayId = 0)
    {
        public double Amount { get; set; }
        /// Damage this layer already absorbed (its leftover is then overshoot, never a misplay).
        public double Absorbed { get; set; }
    }

    private readonly LinkedList<Layer> _layers = new();

    public double Total => _layers.Sum(l => l.Amount);

    public void Add(ulong giverId, double amount, int round, int playId = 0)
    {
        if (amount > 0) _layers.AddLast(new Layer(giverId, round, playId) { Amount = amount });
    }

    /// Takes up to `amount` from the oldest layers. Returns what was taken from each layer.
    /// If the layers hold less than `amount`, the rest is unattributed (not returned).
    public List<(Layer Layer, double Taken)> Consume(double amount)
    {
        var taken = new List<(Layer, double)>();
        while (amount > 0 && _layers.First is { } node)
        {
            var take = Math.Min(amount, node.Value.Amount);
            taken.Add((node.Value, take));
            node.Value.Amount -= take;
            amount -= take;
            if (node.Value.Amount <= 0) _layers.RemoveFirst();
        }
        return taken;
    }

    /// Removes every remaining layer and returns them.
    public List<Layer> Clear()
    {
        var all = _layers.ToList();
        _layers.Clear();
        return all;
    }
}
