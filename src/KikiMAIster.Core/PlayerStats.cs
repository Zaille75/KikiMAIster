namespace KikiMAIster.Core;

public enum Scope { Turn, Combat, Run }

/// Count and total stacks of one power (e.g. Vulnerable applied 3 times for 5 stacks).
public sealed class PowerTally
{
    public int Count { get; set; }
    public double Stacks { get; set; }
}

/// All stats of one player for one scope. Amounts are doubles because damage-over-time is split
/// between players (e.g. 60% / 40%). Definitions: docs/specs/01-metrics.md.
public sealed class PlayerStats
{
    // A. Offense
    public double DamageDealt { get; set; }
    public double DamageIntoBlock { get; set; }
    public double Overkill { get; set; }
    public double TotalRawDamage => DamageDealt + DamageIntoBlock + Overkill;
    public double Kills { get; set; }
    public double BiggestHit { get; set; }
    public Dictionary<string, double> DamageBySource { get; } = new();

    // B. Debuffs
    public Dictionary<string, PowerTally> DebuffsApplied { get; } = new();
    public double DotDamage { get; set; }

    // B. Friendly fire
    public Dictionary<string, PowerTally> FriendlyDebuffs { get; } = new();
    public double FriendlyDamage { get; set; }
    public double SelfDamage { get; set; }

    // C. Protection & support
    public double BlockGainedSelf { get; set; }
    public double BlockGivenToAllies { get; set; }
    public double PreventedOwnBlock { get; set; }
    public double PreventedForAllies { get; set; }
    public double WastedBlock { get; set; }
    public double UselessHelp { get; set; }
    public double BlockSpent { get; set; }
    public double AbsorbedByPet { get; set; }
    public double PetDeaths { get; set; }
    public double HealingSelf { get; set; }
    public double HealingAllies { get; set; }

    // D. Damage taken
    public double HpLostFromEnemies { get; set; }
    public double HpLostFromAllies { get; set; }
    public double HpLostFromSelf { get; set; }
    public double HpLostOther { get; set; }
    public double HpLost => HpLostFromEnemies + HpLostFromAllies + HpLostFromSelf + HpLostOther;
    public double DamageBlocked { get; set; }
    public Dictionary<string, PowerTally> DebuffsReceived { get; } = new();

    internal static void AddPower(Dictionary<string, PowerTally> map, string powerId, double stacks)
    {
        if (!map.TryGetValue(powerId, out var t)) map[powerId] = t = new PowerTally();
        t.Count++;
        t.Stacks += stacks;
    }

    internal static void Add(Dictionary<string, double> map, string key, double amount) =>
        map[key] = map.GetValueOrDefault(key) + amount;
}
