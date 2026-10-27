namespace KikiMAIster.Core;

/// Cumulative combat totals of one player at the end of a round (for the end-of-combat chart).
/// Saved: HP saved for allies (co-op).
public sealed record RoundTotals(double Damage, double Prevented, double HpLost, double DebuffStacks, double Saved = 0)
{
    public static RoundTotals Of(PlayerStats s) =>
        new(s.DamageDealt, s.PreventedTotal, s.HpLost, s.DebuffsApplied.Values.Sum(t => t.Stacks), s.SavedForAllies);
}

/// Totals of every player at the end of a round. Round 0 = start of the combat (all zero).
public sealed record RoundPoint(int Round, IReadOnlyDictionary<ulong, RoundTotals> Players);

/// A combat of the run: its round-by-round cumulative totals. Victory is null while the combat is in progress
/// (the last point is then the round being played).
/// Stats: every player's full stats for this combat (live while it is in progress).
public sealed record CombatRecord(int Number, string Name, bool? Victory, IReadOnlyList<RoundPoint> Rounds,
    IReadOnlyDictionary<ulong, PlayerStats> Stats)
{
    public bool InProgress => Victory == null;
    public int LastRound => Rounds.Count > 0 ? Rounds[^1].Round : 0;
}
