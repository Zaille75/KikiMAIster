namespace KikiMAIster.Core;

/// Turn: the round being played. PreviousTurn: the last finished round (frozen when a new round starts or the
/// combat ends, then completed by what is only known afterwards, e.g. its wasted block).
public enum Scope { Turn, PreviousTurn, Combat, Run }

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

    // B2. Debuff effects (Vulnerable, Weak, Strength down…)
    /// Extra damage this player's debuffs on enemies let the player themself / their allies deal (Vulnerable).
    public double DamageEnabledSelf { get; set; }
    public double DamageEnabledAllies { get; set; }
    /// Damage enemies did not deal to this player / their allies because of this player's debuffs (Weak, Strength down).
    public double PreventedByDebuffsSelf { get; set; }
    public double PreventedByDebuffsAllies { get; set; }
    /// Per debuff ("VULNERABLE_POWER"…): extra damage enabled + damage prevented.
    public Dictionary<string, double> DebuffEffectBySource { get; } = new();

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
    /// Why block was wasted: one entry per block card play (not saved with the run).
    public List<WastedPlay> WastedPlays { get; } = new();
    public double BlockSpent { get; set; }
    public double AbsorbedByPet { get; set; }
    /// HP loss avoided thanks to relics/powers that reduce it (Tungsten Rod, Intangible…), per source.
    public double PreventedByEffects { get; set; }
    public Dictionary<string, double> PreventedByEffectSource { get; } = new();
    /// Everything that really stopped damage: own block, block given to allies, pet, relics/powers, debuffs.
    public double PreventedTotal => PreventedSelf + PreventedForAllies + PetAbsorbedForAllies + PreventedByDebuffsAllies;
    /// What this player prevented for themself (trophy Forteresse): own block, own pet, relics/powers, own debuffs.
    public double PreventedSelf => PreventedOwnBlock + AbsorbedByPet + PreventedByEffects + PreventedByDebuffsSelf;
    /// Damage absorbed by an ally's pet, from the pet HP this player gave it (Legion of Bone…).
    public double PetAbsorbedForAllies { get; set; }
    /// Damage stopped by block (own block + block given to allies).
    public double PreventedByBlock => PreventedOwnBlock + PreventedForAllies;
    /// Block that stopped nothing, wasted or useless (v0.9.0: one "useless" column; wasted is a missed opportunity).
    public double UnusedBlock => WastedBlock + UselessBlock;
    public double PetDeaths { get; set; }
    /// HP given to pets (summon, max HP gain, heal): what the pet could then absorb.
    public double PetHpGiven { get; set; }
    public double HealingSelf { get; set; }
    public double HealingAllies { get; set; }

    // E. Missed opportunities
    public double MissedLethals { get; set; }
    public List<MissedLethalTurn> MissedLethalTurns { get; } = new();
    public double WeakAttacks { get; set; }
    public List<WeakAttackPlay> WeakAttackPlays { get; } = new();
    /// Other missed opportunities per kind ("smallBlock", "energy", "relic"…), and their latest explanations.
    public Dictionary<string, double> MissedByKind { get; } = new();
    public List<MissedEntry> MissedEntries { get; } = new();
    /// Worse choices: weak attacks + smaller block, Pen Nib on a weak attack, block below Orichalcum, growing card not played.
    public double MissedChoices => WeakAttacks + new[] { "smallBlock", "penNib", "orichalcum", "order", "orderBlock" }.Sum(k => MissedByKind.GetValueOrDefault(k));
    /// Block card plays whose block was wasted (gâché, rule F3), counted once per play (saved with the run).
    public double WastedPlayCount { get; set; }
    /// "Gâché" misplays (v0.13.1): wasted block card plays + growing cards never played in a fight.
    public double MissedWasted => WastedPlayCount + MissedByKind.GetValueOrDefault("growth");
    /// Every missed opportunity counted once (trophies).
    public double MissedTotal => MissedLethals + MissedWasted + MissedChoices + MissedByKind.GetValueOrDefault("relic") + MissedByKind.GetValueOrDefault("paelsTears") +
                                 MissedByKind.GetValueOrDefault("potion") + (MissedByKind.GetValueOrDefault("blockKept") > 0 ? 1 : 0) +
                                 (MissedByKind.GetValueOrDefault("allyBlockKept") > 0 ? 1 : 0);
    /// HP lost (own and allies') while a playable block card stayed in hand.
    public double AvoidableHp => MissedByKind.GetValueOrDefault("blockKept") + MissedByKind.GetValueOrDefault("allyBlockKept");

    // Co-op
    /// HP this player saved for allies: block given that absorbed damage + their debuffs' prevented damage + healing.
    /// The same help per ally (key: the ally's player id): HP saved for them, extra damage they dealt thanks to this player.
    public Dictionary<string, double> SavedPerAlly { get; } = new();
    public Dictionary<string, double> EnabledPerAlly { get; } = new();
    public double SavedForAllies => PreventedForAllies + PreventedByDebuffsAllies + HealingAllies + PetAbsorbedForAllies;
    public double Deaths { get; set; }
    /// Help that is not damage or block: energy, cards (drawn, created, brought to hand), orbs given to allies, and
    /// damage taken instead of allies (Intercept).
    public double EnergyGiven { get; set; }
    public double CardsGiven { get; set; }
    public double OrbsGiven { get; set; }
    public double TakenForAllies { get; set; }
    /// Most cards played in one turn (record of the scope).
    public double MostCardsInTurn { get; set; }
    /// Most damage dealt by one card play: all its hits and the cards it played, block and overkill included.
    public double BestCardPlay { get; set; }

    // Other
    public double CardsDrawn { get; set; }
    public double CardsPlayed { get; set; }
    public double CardsExhausted { get; set; }
    public double CardsDiscarded { get; set; }
    public double PotionsUsed { get; set; }

    // D. Damage taken
    public double HpLostFromEnemies { get; set; }
    public double HpLostFromAllies { get; set; }
    public double HpLostFromSelf { get; set; }
    public double HpLostOther { get; set; }
    public double HpLost => HpLostFromEnemies + HpLostFromAllies + HpLostFromSelf + HpLostOther;
    public double DamageBlocked { get; set; }
    public Dictionary<string, PowerTally> DebuffsReceived { get; } = new();

    /// Returns true when this block card play was not wasted before (a new misplay).
    internal static bool AddWasted(List<WastedPlay> list, int playId, int round, BlockChoice choice, double amount, int combat)
    {
        var entry = list.FirstOrDefault(w => w.PlayId == playId);
        var added = entry == null;
        if (entry == null) list.Add(entry = new WastedPlay(playId, round, choice, combat));
        entry.Amount += amount;
        return added;
    }

    internal static void AddPower(Dictionary<string, PowerTally> map, string powerId, double stacks)
    {
        if (!map.TryGetValue(powerId, out var t)) map[powerId] = t = new PowerTally();
        t.Count++;
        t.Stacks += stacks;
    }

    internal static void Add(Dictionary<string, double> map, string key, double amount) =>
        map[key] = map.GetValueOrDefault(key) + amount;
}
