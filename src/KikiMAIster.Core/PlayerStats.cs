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
    /// Overkill of single-target attacks aimed while another enemy was alive (v0.17.42); the rest is unavoidable.
    public double OverkillAvoidable { get; set; }
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
    /// The part of it that is damage avoided (Affaibli…), by debuff (v0.18.5: the PV évités / Debuff details).
    public Dictionary<string, double> DebuffAvoidedBySource { get; } = new();

    // B. Friendly fire
    public Dictionary<string, PowerTally> FriendlyDebuffs { get; } = new();
    /// Debuffs a player put on themself (Influx neuronal's Fatalité, boss-forced curses…): Damage section (v0.17.7).
    public Dictionary<string, PowerTally> SelfDebuffs { get; } = new();
    public double FriendlyDamage { get; set; }
    public double SelfDamage { get; set; }
    /// HP the player made themself lose, by source; HP allies made them lose, by ally (v0.18.6, tooltips).
    public Dictionary<string, double> SelfDamageBySource { get; } = new();
    public Dictionary<string, double> HpLostPerAlly { get; } = new();
    /// HP that the Strength this player's relics gave enemies made the players lose (Soufre, Pierre philosophale; v0.17.45),
    /// by "Relic:<ID>": already in SelfDamage / FriendlyDamage.
    public Dictionary<string, double> HarmByRelic { get; } = new();

    // C. Protection & support
    public double BlockGainedSelf { get; set; }
    public double BlockGivenToAllies { get; set; }
    public double PreventedOwnBlock { get; set; }
    public double PreventedForAllies { get; set; }
    /// Block that saved nothing although the giver had a playable attack instead (spec §F).
    public double WastedBlock { get; set; }
    /// Block that saved nothing with no alternative (no playable attack, relic/power block…).
    public double UselessBlock { get; set; }
    /// Part of the useless block that could not be avoided (v0.17.34): the leftover of a block that stopped part of the
    /// damage (needed 1 more, only a 5 in hand), or block no card chose (relics, powers, orbs).
    public double UnavoidableBlock { get; set; }
    /// Part of the wasted + useless block that this player had given to allies (co-op detail).
    public double UnusedGivenToAllies { get; set; }
    public double UnusedOwn => WastedBlock + UselessBlock - UnusedGivenToAllies;
    /// Why block was wasted: one entry per block card play (not saved with the run).
    public List<WastedPlay> WastedPlays { get; } = new();
    public double BlockSpent { get; set; }
    /// Per block source ("Card:DEFEND_IRONCLAD"…): damage that block stopped, and block that stopped nothing (v0.17.17).
    public Dictionary<string, double> BlockStoppedBySource { get; } = new();
    public Dictionary<string, double> BlockUnusedBySource { get; } = new();
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
    public double MissedChoices => WeakAttacks + ChoiceKinds.Sum(k => MissedByKind.GetValueOrDefault(k));
    public static readonly string[] ChoiceKinds =
        { "smallBlock", "penNib", "orichalcum", "order", "orderBlock", "orderSlow", "orderTurn", "handFull", "capCheaper", "drawFirst", "blockOnHit", "growth", "tent", "orderPotion", "restBoss", "orderDark", "stars", "shell" };
    /// Block card plays whose block was wasted (gâché, rule F3), counted once per play (saved with the run).
    public double WastedPlayCount { get; set; }
    /// "Gâché" misplays: wasted block card plays + useful free cards left in hand (v0.17.26; growing cards: Choix).
    /// v0.17.42: an attack wasted on an enemy a smaller attack or a follow-up (Trou noir) would have killed ("overkill").
    public double MissedWasted => WastedPlayCount + WastedKinds.Sum(k => MissedByKind.GetValueOrDefault(k));
    public static readonly string[] WastedKinds = { "freeCard", "starsWasted", "overkill", "overkillFollowUp", "stampede", "stampedeSwap", "noDraw", "potionUseless", "discardEthereal" };
    /// Misplays outside fights (v0.17.42): Max HP gained after a heal that was capped by the old Max HP.
    public double MissedOutside => MissedByKind.GetValueOrDefault("maxHpAfterHeal");
    /// Every missed opportunity counted once (trophies).
    public double MissedTotal => MissedLethals + MissedWasted + MissedChoices + MissedOutside + MissedByKind.GetValueOrDefault("relic") + MissedByKind.GetValueOrDefault("paelsTears") +
                                 MissedByKind.GetValueOrDefault("potion") + (MissedByKind.GetValueOrDefault("blockKept") > 0 ? 1 : 0) +
                                 (MissedByKind.GetValueOrDefault("allyBlockKept") > 0 ? 1 : 0);
    /// HP lost (own and allies') while a playable block card stayed in hand.
    public double AvoidableHp => MissedByKind.GetValueOrDefault("blockKept") + MissedByKind.GetValueOrDefault("allyBlockKept");

    // Co-op
    /// HP this player saved for allies: block given that absorbed damage + their debuffs' prevented damage + healing.
    /// The same help per ally (key: the ally's player id): HP saved for them, extra damage they dealt thanks to this player.
    public Dictionary<string, double> SavedPerAlly { get; } = new();
    public Dictionary<string, double> EnabledPerAlly { get; } = new();
    public double SavedForAllies => PreventedForAllies + PreventedByDebuffsAllies + HealingAllies + PetAbsorbedForAllies + SavedByEnabledBlock;
    /// Block an ally could gain thanks to this player (Sacrifice: from pet HP this player gave, v0.17.6), and the damage
    /// that block then stopped (part of HP saved for allies).
    public double BlockEnabledForAllies { get; set; }
    public double SavedByEnabledBlock { get; set; }
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
    /// Energy lost at the end of a turn with nothing useful to spend it on (v0.17.29: trophy "Batterie pleine").
    public double EnergySurplus { get; set; }
    /// Energy spent for nothing (v0.17.37): paid for a block card whose block was not needed (block at the end of the turn
    /// minus its block still covers the attack) or a card whose only effect was a useless debuff.
    public double EnergyUseless { get; set; }
    /// Doom (Malédiction) put on enemies, and the stacks on enemies that did not die of it (v0.18.6).
    public double DoomApplied { get; set; }
    public double DoomUseless { get; set; }
    /// Energy gained during fights (Orbite, Sursis, Éclair+…; not the turn's refill), and by source (v0.18.5, Zaille75).
    public double EnergyGained { get; set; }
    public Dictionary<string, double> EnergyGainedBySource { get; } = new();

    // Run history (read from the game's own map history, run scope only: spec 01-metrics §C4)
    /// Max HP gained and lost during the run (events, relics, cards…).
    public double MaxHpGained { get; set; }
    public double MaxHpLost { get; set; }
    /// HP lost outside fights (events…, v0.17.34): the game's damage taken on floors without a fight.
    public double HpLostOutside { get; set; }
    /// HP healed outside fights (events, campfires, relics…, v0.17.35).
    public double HealedOutside { get; set; }
    /// HP lost / healed by events just before the fight they start (v0.17.36), already in the totals above; keys: floors seen.
    public double PreFightHpLost { get; set; }
    public double PreFightHealed { get; set; }
    public Dictionary<string, double> PreFightFloors { get; } = new();
    /// Damage added by card enchantments, by "Enchantment:<ID>" (v0.17.39, details of Dégâts; already in DamageDealt).
    public Dictionary<string, double> DamageByEnchantment { get; } = new();
    /// HP lost by kind (v0.17.39, details): "attack", "reaction", "status", "dot", "self", "ally", "other".
    public Dictionary<string, double> HpLostByKind { get; } = new();
    /// Heals this player gave (to self and allies), by source ("Relic:…", "Card:…", "Potion:…"; v0.17.39).
    public Dictionary<string, double> HealingBySource { get; } = new();
    /// Fight length (v0.17.39): combat scope = this fight's rounds and real seconds; run scope = totals, and FightsTimed =
    /// fights counted.
    public double FightTurns { get; set; }
    public double FightSeconds { get; set; }
    public double FightsTimed { get; set; }
    /// HP healed right after won fights (Sang ardent…, v0.17.38), already in HealedOutside ("afterFight").
    public double AfterFightHealed { get; set; }
    /// Gold and curses over the run (v0.17.36, run scope, from the game's map history).
    public double GoldGained { get; set; }
    public double GoldSpent { get; set; }
    public double GoldLost { get; set; }
    public double GoldStolen { get; set; }
    public double GoldPeak { get; set; }
    public double GoldNow { get; set; }
    public double CursesGained { get; set; }
    public double CursesRemoved { get; set; }
    public double Purchases { get; set; }
    /// Both by kind of floor: "event", "ancient" (act-start Ancients, v0.17.38), "restSite", "other"; heals also
    /// "afterFight" (v0.17.38).
    public Dictionary<string, double> HpLostOutsideBy { get; } = new();
    public Dictionary<string, double> HealedOutsideBy { get; } = new();
    /// Campfire choices, by the game's option id ("HEAL", "SMITH", "MEND"…): how many times each.
    public Dictionary<string, double> RestSiteChoices { get; } = new();
    /// Rock-paper-scissors when several players picked the same treasure relic: fights won / fought.
    public double ChifumiWon { get; set; }
    public double ChifumiPlayed { get; set; }
    /// What the player threw ("Rock", "Paper", "Scissors": times) and the longest chifumi they took part in (rounds), v0.17.45.
    public Dictionary<string, double> ChifumiMoves { get; } = new();
    public double ChifumiLongest { get; set; }
    public double ChifumiLost => ChifumiPlayed - ChifumiWon;
    /// Card rewards over the run (v0.17.45, from the game's history): "offered.<kind>", "taken.<kind>", "skipped.<kind>" with
    /// kind "fight", "event" or "other" (shop…).
    public Dictionary<string, double> CardPicks { get; } = new();
    public double CardsOffered => Sum(CardPicks, "offered.");
    public double CardsTaken => Sum(CardPicks, "taken.");
    public double CardOffersSkipped => Sum(CardPicks, "skipped.");
    /// Fun trophies (v0.17.45): rounds spent dead, alone alive (co-op), at 1 HP; seconds the others waited for this player
    /// on the map.
    public double TurnsDead { get; set; }
    public double TurnsAlone { get; set; }
    public double TurnsAtOneHp { get; set; }
    public double WaitedSeconds { get; set; }
    /// Damage a cap stopped (Carapace endurcie, Dur à cuire; v0.17.45), already in Overkill.
    public double DamageCapped { get; set; }

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

    private static double Sum(Dictionary<string, double> map, string prefix) =>
        map.Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal)).Sum(kv => kv.Value);

    internal static void Add(Dictionary<string, double> map, string key, double amount) =>
        map[key] = map.GetValueOrDefault(key) + amount;
}
