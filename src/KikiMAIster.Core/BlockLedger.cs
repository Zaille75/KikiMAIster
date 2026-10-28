namespace KikiMAIster.Core;

/// Block layers on one player's creature, consumed first-in first-out (spec 01-metrics §F).
public sealed class BlockLedger
{
    /// Source: what gave the block ("Card:DEFEND_IRONCLAD", "Relic:…"), for the Protection details (v0.17.17).
    public sealed record Layer(ulong GiverId, int Round, int PlayId = 0, string Source = "")
    {
        public double Amount { get; set; }
        /// Damage this layer already absorbed (its leftover is then overshoot, never a misplay).
        public double Absorbed { get; set; }
        /// Allies who made this block possible, with their share (Sacrifice: block from pet max HP they gave, v0.17.6).
        public Dictionary<ulong, double>? Enablers { get; set; }
    }

    private readonly LinkedList<Layer> _layers = new();

    public double Total => _layers.Sum(l => l.Amount);

    /// The newest layer (null when empty).
    public Layer? Last => _layers.Last?.Value;

    /// First: the layer counts as the oldest one (block known in advance, e.g. Plating at the end of the turn).
    public void Add(ulong giverId, double amount, int round, int playId = 0, bool first = false, string source = "")
    {
        if (amount <= 0) return;
        var layer = new Layer(giverId, round, playId, source) { Amount = amount };
        if (first) _layers.AddFirst(layer);
        else _layers.AddLast(layer);
    }

    /// Takes up to `amount` from the oldest layers. Returns what was taken from each layer.
    /// If the layers hold less than `amount`, the rest is unattributed (not returned).
    /// Last: layers used only once every other layer is empty (block from a play that had an alternative, v0.17.32).
    public List<(Layer Layer, double Taken)> Consume(double amount, Func<Layer, bool>? last = null)
    {
        var taken = new List<(Layer, double)>();
        foreach (var pass in last == null ? new[] { true } : new[] { false, true })
        {
            var node = _layers.First;
            while (amount > 0 && node != null)
            {
                var next = node.Next;
                if (last == null || last(node.Value) == pass)
                {
                    var take = Math.Min(amount, node.Value.Amount);
                    taken.Add((node.Value, take));
                    node.Value.Amount -= take;
                    amount -= take;
                    if (node.Value.Amount <= 0) _layers.Remove(node);
                }
                node = next;
            }
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
