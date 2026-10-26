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
    /// Block that saved nothing although the giver had a playable attack instead (spec §F).
    public double WastedBlock { get; set; }
    /// Block that saved nothing with no alternative (no playable attack, relic/power block…).
    public double UselessBlock { get; set; }
    /// Part of the wasted + useless block that this player had given to allies (co-op detail).
    public double UnusedGivenToAllies { get; set; }
    public double UnusedOwn => WastedBlock + UselessBlock - UnusedGivenToAllies;
    public double BlockSpent { get; set; }
    public double AbsorbedByPet { get; set; }
    /// HP loss avoided thanks to relics/powers that reduce it (Tungsten Rod, Intangible…), per source.
    public double PreventedByEffects { get; set; }
    public Dictionary<string, double> PreventedByEffectSource { get; } = new();
    /// Everything that really stopped damage: own block, block given to allies, pet, relics/powers.
    public double PreventedTotal => PreventedOwnBlock + PreventedForAllies + AbsorbedByPet + PreventedByEffects;
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
