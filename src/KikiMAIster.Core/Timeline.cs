namespace KikiMAIster.Core;

/// Cumulative combat totals of one player at the end of a round (for the end-of-combat chart).
public sealed record RoundTotals(double Damage, double Prevented, double HpLost, double DebuffStacks)
{
    public static RoundTotals Of(PlayerStats s) =>
        new(s.DamageDealt, s.PreventedTotal, s.HpLost, s.DebuffsApplied.Values.Sum(t => t.Stacks));
}

/// Totals of every player at the end of a round. Round 0 = start of the combat (all zero).
public sealed record RoundPoint(int Round, IReadOnlyDictionary<ulong, RoundTotals> Players);

/// A finished combat: its round-by-round cumulative totals.
public sealed record CombatRecord(int Number, bool Victory, IReadOnlyList<RoundPoint> Rounds);
