using System.Globalization;

namespace KikiMAIster.Core;

public enum CardKind { Attack, Skill, Power, Status, Curse, Other }

/// Who a block card protects: the player (Mimic included), one ally, or every ally.
public enum BlockTarget { Self, OneAlly, AllAllies }

/// Relics that trigger on a number or a mix of cards played in a turn (spec §E0, relic triggers missed by one card).
public enum RelicTrigger { Attacks, Skills, AllTypes, NthCardFree, EmptyHand }

/// A card in a player's hand at the end of the turn, with what the game said about it (filled by the capture layer).
/// Ref: the game's card object, used only to match it with the cards seen during the turn.
public sealed class TurnCard
{
    private static readonly IReadOnlyDictionary<object, int> NoDamage = new Dictionary<object, int>();

    public required object Ref { get; init; }
    public required string Key { get; init; }
    public required CardKind Kind { get; init; }
    public int Cost { get; init; }
    public bool CostsX { get; init; }
    /// It costs Stars (a fixed amount or X).
    public bool CostsStars { get; init; }
    /// Playable now if the player had the energy and Stars.
    public bool PlayableExceptEnergy { get; init; } = true;
    /// Playable instead of a card already played: a limit on cards per turn (Paresse, Résonnant) does not count.
    public bool PlayableInstead { get; init; } = true;
    public bool LacksStars { get; init; }
    /// Retained, paid with Stars or Exhaust, consuming the hand, only giving energy (Sursis…), or killing the player's pet
    /// (Détonation osseuse…): not playing it is a choice.
    public bool KeptOnPurpose { get; init; }
    /// An attack made by the pet (Cliquetis…, v0.17.45): it does nothing while the pet is dead.
    public bool NeedsPet { get; init; }
    /// The Fool (1.2.0): a Shot with no arrow ready (the game shows it in red): playing it now would do nothing.
    /// 1.2.1: also Nail while no enemy has an arrow in it, and Recall / Uproot / Wrest while none has one of the player's.
    public bool NoArrow { get; init; }
    /// One of the Fool's Shots (damage "from an arrow"): it needs an arrow ready (1.2.1).
    public bool Shot { get; init; }
    /// Playing it kills the player's own pet (Détonation osseuse, Sacrifice; v0.17.45).
    public bool SacrificesPet { get; init; }
    public bool GainsBlock { get; init; }
    /// The game's block estimate (0 when computed another way).
    public int Block { get; init; }
    public BlockTarget BlockFor { get; init; }
    public bool Exhausts { get; init; }
    /// La grande manœuvre.
    public bool Gambit { get; init; }
    /// A card that grows each time it is played.
    public bool Growing { get; init; }
    public bool Rage { get; init; }
    /// Targets one enemy or all of them.
    public bool HitsEnemies { get; init; }
    /// Thorns damage the player would take by playing it.
    public double Thorns { get; init; }
    /// The enemies punish it like Thorns (Personal Hive), or it is Souillé while an enemy attacks (v0.18.6).
    public bool Punished { get; init; }
    public bool DealsDamage { get; init; } = true;
    /// Estimated damage on each enemy; an enemy is missing when the card's number of hits is unknown.
    public IReadOnlyDictionary<object, int> DamageOn { get; init; } = NoDamage;
    /// An attack putting that much Vulnérable on its target, or on every enemy (Tope là, Coup…; v1.1.1).
    public int VulnerableStacks { get; init; }
    public bool AllEnemies { get; init; }

    public bool StatusOrCurse => Kind is CardKind.Status or CardKind.Curse;
    /// Attacks, and skills / powers that give no block: an alternative to a block card.
    public bool Useful => Kind == CardKind.Attack || (Kind is CardKind.Skill or CardKind.Power && !GainsBlock);
    /// Costs nothing to play (0 energy, no Stars, not X).
    public bool Free => !CostsX && Cost == 0 && !CostsStars;
}

/// A better card the player had in hand when playing a card: Value is its damage or block, Played the played card's.
public sealed record Better(object Card, string Key, int Value, int Played);

/// A card seen in hand when a card was played.
public readonly record struct SeenCard(object Card, string Key);

/// A hit of a card on an enemy: raw damage (HP + block, overkill left out) and the enemy's Slow when hit (cards played
/// before it this turn, each +10% attack damage; -1 without Slow).
/// v1.1.1 (Zaille75): the part of the hit given by its place among the player's attacks of the turn: PlaceMult from a
/// multiplier (Létalité, Géantification, Pointe de stylo), PlaceAdd from a bonus (Vigueur), PlaceShiv from Lames fantomatiques
/// (first Surin). Swapped with another attack of the same player, that part goes to the other card.
/// Vulnerable: the enemy already had Vulnérable when hit (v1.1.1).
public readonly record struct EnemyHit(object Target, int Raw, int Slow, double PlaceMult = 0, double PlaceAdd = 0, double PlaceShiv = 0,
    bool Vulnerable = false)
{
    public double Place => PlaceMult + PlaceAdd + PlaceShiv;
}

/// Damage dealt right after a card by something it triggered (Trou noir after Stars were spent), on one enemy.
public readonly record struct FollowUpHit(string Source, object Target, int Raw);

/// A card the player chose to play this turn (auto-plays are not choices). Filled during the turn by the capture layer.
public class TurnPlay
{
    public required int Id { get; init; }
    public required Actor Owner { get; init; }
    public required object CardRef { get; init; }
    public required string CardKey { get; init; }
    public required CardKind Kind { get; init; }
    /// A Surin (Lames fantomatiques, v1.1.1).
    public bool Shiv { get; init; }
    public int EnergySpent { get; init; }
    /// An X-cost card (1.2.0: Paré au combat does not lower it).
    public bool CostsX { get; init; }
    /// Stars paid for this card (v0.17.34).
    public int StarsSpent { get; init; }
    public bool Ethereal { get; init; }
    public bool GaveBlock { get; set; }
    public bool HadOtherEffect { get; set; }
    /// An effect other than block or damage (debuff, draw, upgrade…): an attack played for it was a real choice.
    public bool HadNonDamageEffect { get; set; }
    /// Block this play gave (all receivers), and how many times (Dexterity counts once per gain).
    public int BlockAmount { get; set; }
    public int BlockGains { get; set; }
    /// Its only debuff did nothing (spec §F, F8).
    public bool UselessDebuff { get; set; }
    /// Hits on an enemy whose Carapace endurcie limit was already reached (0 damage).
    public int ShellCapped { get; set; }
    /// Thorns damage the player took or would take for this card (set at the end of the turn).
    public double Thorns { get; set; }
    /// Useful cards in hand when it was played (it left out), and the whole hand.
    public List<SeenCard> UsefulCardsInHand { get; init; } = new();
    public List<object> HandAtPlay { get; init; } = new();
    /// A stronger attack on the same target, Pen Nib doubling a weaker attack, a bigger block card.
    public Better? Stronger { get; set; }
    public Better? PenNibBetter { get; set; }
    public Better? BiggerBlock { get; set; }

    /// Hits on enemies (v0.17.42: moved here from the capture layer).
    public List<EnemyHit> Hits { get; } = new();
    /// Cards in the allies' hands when it was played (co-op order against Slow, v0.17.42).
    public HashSet<object> AlliesHandAtPlay { get; init; } = new(ReferenceEqualityComparer.Instance);
    /// Single-target attack (v0.17.42): the enemy aimed at and its HP + block before the card.
    public object? Target { get; init; }
    public int TargetLeftBefore { get; init; }
    /// The card killed its target, with that much damage beyond its HP.
    public bool KilledTarget { get; set; }
    public int Overkill { get; set; }
    /// The card's estimated damage on each enemy when it was played (enemies whose hit count is unknown are missing).
    public Dictionary<object, int> DamageOn { get; init; } = new(ReferenceEqualityComparer.Instance);
    /// Estimated damage on the target of each attack in hand when it was played.
    public Dictionary<object, int> HandDamageOnTarget { get; init; } = new(ReferenceEqualityComparer.Instance);
    /// Damage it triggered right after (Trou noir…).
    public List<FollowUpHit> FollowUps { get; } = new();
    /// Powers it put on its own player ("Power:AFTERIMAGE_POWER"…), and the player's powers that fired for it (block after it,
    /// damage added to its hits): a card played after one of its powers fed it was not in the wrong order (v0.18.6).
    public HashSet<string> PowersGiven { get; } = new();
    public HashSet<string> PowersTriggered { get; } = new();
    /// Living enemies when it was played (Ruée, v0.18.6).
    public int EnemiesAlive { get; init; } = 1;
    /// Energy before it was paid (full hand, v0.18.6).
    public int EnergyBefore { get; init; }
    /// Its draw was stopped (Pas de pioche) and the energy it gave (Offrande…, v0.18.6).
    public bool DrawBlocked { get; set; }
    public int EnergyGiven { get; set; }
    /// Cards it created that went to the discard pile because the hand was full, and the hand at that moment (v0.18.6).
    public int LostToFullHand { get; set; }
    /// A damage cap stopped part of its damage (Dur à cuire, Carapace endurcie…), and the cards it drew (v0.18.7).
    public bool Capped { get; set; }
    public int CardsDrawn { get; set; }
    /// The enemies a cap stopped part of its damage on (v0.18.9, Zaille75: Dur à cuire, Glissant…): a boost played after it
    /// would have been stopped too, so it lost nothing there.
    public HashSet<object> CappedOn { get; } = new(ReferenceEqualityComparer.Instance);
    /// Played again this turn (Écho, Déflagration…): its copies added their damage and block (v0.18.9).
    public bool PlayedAgain { get; set; }
    /// An Ethereal status or curse it made the player discard (v0.18.9): left in hand it would have been exhausted.
    public string? DiscardedEthereal { get; set; }
    /// The Fool (1.2.0): a Shot card played with no arrow ready (all of them stuck in enemies): it shot nothing.
    public bool EmptyShot { get; set; }

    /// The Fool (1.2.1). ShotArrow: it shot an arrow. ArrowsReady: the player's arrows ready when it started. OwnArrowIn /
    /// ArrowIn: enemies with one of the player's arrows / any arrow in them when it started. SerratedBonus: Serrated Arrows'
    /// bonus then (0.25 = +25%). AimAtStart: the player's Aim when this arrow attack started; AimFactor: how many times it
    /// takes Aim (Heavy Shot 2, Rapid Shot per hit, Rain Shot 1 + each other enemy). NotImpaled: Nail / Recall / Uproot /
    /// Wrest played on an enemy without the arrow it needs. KindCount: the cards of its kind the player had played this
    /// turn with it (auto-plays included, as relics count them).
    public bool ShotArrow { get; set; }
    public int ArrowsReady { get; set; }
    public HashSet<object> OwnArrowIn { get; set; } = new(ReferenceEqualityComparer.Instance);
    public HashSet<object> ArrowIn { get; set; } = new(ReferenceEqualityComparer.Instance);
    public double SerratedBonus { get; set; }
    public int AimAtStart { get; set; }
    public double AimFactor { get; set; } = 1;
    public bool NotImpaled { get; set; }
    public int KindCount { get; set; }
    /// Set by the rules: a Shot stuck an arrow in its target later in the turn ("impaleFirst" instead of "notImpaled").
    public bool ImpaledLater { get; set; }
    public HashSet<object> HandWhenFull { get; } = new(ReferenceEqualityComparer.Instance);

    /// The first enemy it hit, with all the raw damage it dealt to that enemy; null when it hit no enemy.
    public (object Target, int Damage)? FirstHit =>
        Hits.Count == 0 ? null : (Hits[0].Target, Hits.Where(h => ReferenceEquals(h.Target, Hits[0].Target)).Sum(h => h.Raw));
}

/// Damage or block a later card or potion would have added to an earlier play (wrong order, spec §E0).
/// Kind "order" / "orderSlow": Extra damage on Enemy; "orderBlock": Extra block; "orderDark": Dark orb charge.
/// Late: the later play that lost it, when known (a Strength / Dexterity drop, v0.18.6).
public sealed record OrderLoss(TurnPlay Early, string Boost, string Kind, object? Enemy, double Extra, TurnPlay? Late = null);

/// GivesBlock: its trigger only gives block (Éventail décoratif, v0.18.9): missed only when that block was needed.
public sealed record TurnRelic(string Key, RelicTrigger Trigger, int Every, bool GivesBlock = false);

/// A player at the end of the turn.
public sealed class TurnPlayer
{
    public required Actor Actor { get; init; }
    public bool Alive { get; init; } = true;
    public int Energy { get; init; }
    public int Block { get; init; }
    /// Damage the player will take before their block is cleared (0 when they take an extra turn first).
    public int Incoming { get; init; }
    /// Energy left is lost at the end of the turn (not kept by a relic or power).
    public bool EnergyResets { get; init; } = true;
    /// "Relic:PAELS_TEARS" when the player has it (energy left is wanted).
    public string? PaelsTears { get; init; }
    /// Orichalcum (or its fake) and the block it gives.
    public string? Orichalcum { get; init; }
    public int OrichalcumBlock { get; init; }
    public List<TurnRelic> Relics { get; init; } = new();
    public List<TurnCard> Hand { get; init; } = new();
    /// Paré au combat (beta v0.113.0) left in hand: "Attacks in your Hand cost 1 less Energy until played" (1.2.0).
    public TurnCard? AttackDiscount { get; init; }
    /// La grande manœuvre's power is already on the player.
    public bool GambitActive { get; init; }
    public bool AlliesAlive { get; init; }
    /// The player's pet is alive, or was killed this turn by one of the player's own cards (v0.17.45: an attack it makes could
    /// have been played before).
    public bool PetAlive { get; init; } = true;
    public bool PetSacrificedThisTurn { get; init; }
    /// Cards played this turn (auto-plays included, as relics count them), by kind.
    public Dictionary<CardKind, int> Plays { get; init; } = new();
    /// Ruée: how many of its end-of-turn plays found no attack in hand (v0.18.6), and its key.
    public int StampedeShort { get; init; }
    public string StampedeKey { get; init; } = "";
    /// Ruée had enough plays for every attack in hand (v0.18.7): those attacks, which it played for free.
    public List<TurnCard> StampedePlayed { get; init; } = new();
    /// A missed lethal of the turn's first hand was already counted for this player (v0.18.9).
    public bool LethalMissed { get; init; }
    /// The Fool (1.2.1): arrows ready at the end of the turn (no limit for others), enemies with one of the player's arrows in
    /// them, and the player's powers that end with the turn unused ("Power:QUIETUS_POWER").
    public int ArrowsReady { get; init; } = int.MaxValue;
    public HashSet<object> OwnArrowIn { get; init; } = new(ReferenceEqualityComparer.Instance);
    public HashSet<string> UnusedBuffs { get; init; } = new();

    public TurnCard? InHand(object card) => Hand.FirstOrDefault(c => ReferenceEquals(c.Ref, card));
    public bool Affordable(TurnCard c) => !c.StatusOrCurse && c.PlayableExceptEnergy && (c.CostsX || c.Cost <= Energy);
}

/// Everything the end-of-turn misplay rules need, read from the game when the players' turn ends.
public sealed class TurnState
{
    /// Every player (dead ones too: their plays are still resolved).
    public List<TurnPlayer> Players { get; init; } = new();
    public List<TurnPlay> Plays { get; init; } = new();
    public List<OrderLoss> OrderLosses { get; init; } = new();
    /// Only an invulnerable enemy left (Waterfall Giant about to explode): nothing to spend energy on.
    public bool NothingToKill { get; init; }
    /// An enemy is asleep (Matriarche lagavulin): not hitting it may be on purpose.
    public bool EnemyAsleep { get; init; }
    /// An enemy without Carapace endurcie at its limit.
    public bool OpenEnemy { get; init; } = true;
    public string ShellKey { get; init; } = "";
    /// HP + block of each enemy alive.
    public Dictionary<object, int> EnemiesLeft { get; init; } = new(ReferenceEqualityComparer.Instance);
    /// The only enemy left, when its death ends the fight (v0.18.9: lethal missed at the end of the turn).
    public object? LastEnemy { get; init; }
    /// Vulnérable stacks of each enemy alive at the end of the turn (v1.1.1).
    public Dictionary<object, int> EnemyVulnerable { get; init; } = new(ReferenceEqualityComparer.Instance);

    public TurnPlayer? Player(Actor a) => Players.FirstOrDefault(p => p.Actor == a);
}

public sealed class TurnResult
{
    public List<StatEvent> Events { get; } = new();
    public List<string> Log { get; } = new();
    /// Growing cards that could have been played this turn (spec §E0: missed if never played in the fight).
    public List<object> GrowingAffordable { get; } = new();
}

/// The misplay rules checked at the end of each players' turn (spec 01-metrics §E0 and §F): wrong order, weak attacks,
/// energy left, block cards kept, relic triggers, free cards left, wasted block, Stars, Carapace endurcie. Pure: the
/// capture layer fills a TurnState and emits what comes back (v0.17.41, moved out of the Harmony patches to be tested).
public static class TurnReview
{
    private static string N(int v) => v.ToString(CultureInfo.InvariantCulture);

    /// A card of another player in a miss's text (v0.18.9, Zaille75: "Chute d'étoile (Kiki)"): "Card:X|<player id>".
    public static string Owned(string key, Actor owner, Actor viewer) =>
        owner == viewer || owner.PlayerId == 0 ? key : $"{key}|{owner.PlayerId.ToString(CultureInfo.InvariantCulture)}";

    /// Powers dealing their damage to a random enemy (v0.18.9, Zaille75): with several enemies alive, where it lands is luck.
    public static readonly HashSet<string> RandomTargets = new()
        { "Power:SERPENT_FORM_POWER", "Power:JUGGERNAUT_POWER", "Power:HAUNT_POWER", "Power:CACOPHONY_POWER" };

    public static TurnResult Resolve(TurnState s)
    {
        var r = new TurnResult();
        SlowAttackOrder(s);
        ArrowOrder(s, r);
        ResolveOrder(s, r);
        WeakPlays(s, r);
        Overkills(s, r);
        LethalLeft(s, r);
        foreach (var p in s.Players.Where(p => p.Alive)) EndOfTurnChecks(s, p, r);
        foreach (var p in s.Players.Where(p => p.Alive))
        {
            FreeCardsLeft(s, p, r);
            Stampede(s, p, r);
            DrawBlocked(s, p, r);
            FullHand(s, p, r);
            CappedAttacks(s, p, r);
            StampedeSwap(s, p, r);
            DrawBeforeMaking(s, p, r);
            VulnerableLeft(s, p, r);
        }
        var used = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var withAlternative = BlockAlternatives(s, used, r);
        foreach (var p in s.Players.Where(p => p.Alive))
        {
            Stars(s, p, withAlternative, used, r);
            Shell(s, p, used, r);
        }
        return r;
    }

    /// Wrong order: damage is only lost on enemies still alive (up to their HP + block), block only when it was below the
    /// incoming attack (up to the difference).
    /// v0.18.6 (Zaille75): one miss per earlier play and turn ("orderTurn": the play, damage lost, block lost, then the later
    /// cards or potions), and no loss for a later play one of the earlier play's powers fed (Image rémanente gives block for
    /// each card after it, Lames fantomatiques adds damage to the Surin after it).
    private static void ResolveOrder(TurnState s, TurnResult r)
    {
        var deficit = new Dictionary<Actor, int>();
        var lost = new List<(TurnPlay Early, string Boost, bool Block, int Amount)>();
        // v0.18.9 (Zaille75): nothing is lost on an enemy whose cap already stopped part of the earlier play's damage there.
        foreach (var g in s.OrderLosses.Where(o => (o.Late == null || !o.Late.PowersTriggered.Overlaps(o.Early.PowersGiven)) &&
                                                   !(o.Enemy != null && o.Early.CappedOn.Contains(o.Enemy)))
                     .GroupBy(o => (o.Early, o.Boost, o.Kind)))
        {
            var (early, boost, kind) = g.Key;
            double amount;
            // Dark orb charged: the damage it stores is lost whatever the enemies left.
            if (kind == "orderDark")
                amount = g.Sum(o => o.Extra);
            // Damage on a random enemy (Forme du serpent, v0.18.9): lost up to what the enemies had left.
            else if (kind == "orderRandom")
                amount = Math.Min(g.Sum(o => o.Extra), s.EnemiesLeft.Values.Sum());
            else if (kind is "order" or "orderSlow")
                amount = g.Where(o => o.Enemy != null).GroupBy(o => o.Enemy!, ReferenceEqualityComparer.Instance)
                    .Sum(e => s.EnemiesLeft.TryGetValue(e.Key, out var left) ? Math.Min(e.Sum(o => o.Extra), left) : 0);
            else
            {
                if (!deficit.TryGetValue(early.Owner, out var left))
                    left = s.Player(early.Owner) is { } p ? Math.Max(0, p.Incoming - p.Block) : 0;
                amount = Math.Min(g.Sum(o => o.Extra), left);
                deficit[early.Owner] = left - (int)amount;
            }
            var rounded = (int)Math.Round(amount);
            if (rounded < 1) continue;
            r.Log.Add($"wrong order: {early.CardKey} before {boost}, {kind} {rounded}");
            lost.Add((early, boost, kind == "orderBlock", rounded));
        }
        // v0.18.9 (Zaille75): one miss per player and turn. Args: the earlier cards ("Card:A+Card:B"), damage lost, block lost,
        // then the later cards or potions.
        foreach (var g in lost.GroupBy(x => x.Early.Owner))
        {
            var damage = g.Where(x => !x.Block).Sum(x => x.Amount);
            var block = g.Where(x => x.Block).Sum(x => x.Amount);
            var early = string.Join('+', g.Select(x => x.Early.CardKey).Distinct());
            var boosts = g.Select(x => x.Boost).Distinct();
            r.Events.Add(new MissedChance(g.Key, "orderTurn", new[] { early, N(damage), N(block) }.Concat(boosts).ToArray()));
        }
    }

    /// Slow (Ralenti): each card played makes the enemy take 10% more attack damage, so attacks on it should go from the
    /// smallest to the biggest (v0.17.42, Zaille75). A bigger attack played before a smaller one that was already in a hand
    /// (the player's own, or an ally's in co-op) lost damage: the two played the other way round would have dealt more.
    /// Counted for the player of the bigger attack, in Choix ("orderSlow"), when at least 1 damage is lost.
    /// v1.1.1 (Zaille75, Idole oubliée with Létalité): a bonus given by the attack's place (Létalité, Vigueur…) moves to the
    /// other card when two attacks of the same player swap; between two players each keeps its own (SwapGain).
    private static void SlowAttackOrder(TurnState s)
    {
        var attacks = s.Plays.Where(p => p.Kind == CardKind.Attack && !p.HadNonDamageEffect).ToList();
        for (var i = 0; i < attacks.Count; i++)
            for (var j = i + 1; j < attacks.Count; j++)
            {
                var (early, late) = (attacks[i], attacks[j]);
                var inHand = early.Owner == late.Owner
                    ? early.HandAtPlay.Any(c => ReferenceEquals(c, late.CardRef))
                    : early.AlliesHandAtPlay.Contains(late.CardRef);
                if (!inHand) continue;
                foreach (var target in early.Hits.Where(h => h.Slow >= 0).Select(h => h.Target).Distinct(ReferenceEqualityComparer.Instance))
                {
                    var earlyHits = early.Hits.Where(h => ReferenceEquals(h.Target, target) && h.Slow >= 0).ToList();
                    var lateHits = late.Hits.Where(h => ReferenceEquals(h.Target, target) && h.Slow >= 0).ToList();
                    if (lateHits.Count == 0 || lateHits[0].Slow <= earlyHits[0].Slow) continue;
                    var extra = SwapGain(early, earlyHits, late, lateHits);
                    if (extra > 0) s.OrderLosses.Add(new OrderLoss(early, late.CardKey, "orderSlow", target, extra));
                }
            }
    }

    /// Damage two attacks on one Slowed enemy would have dealt more played the other way round (negative: the order was right).
    /// Each card's base is its raw damage without its place bonus, Slow removed; swapped, it takes the other's Slow, and the
    /// place bonus follows the place for cards of the same player (Lames fantomatiques: only between two Surins).
    public static double SwapGain(TurnPlay early, IReadOnlyList<EnemyHit> earlyHits, TurnPlay late, IReadOnlyList<EnemyHit> lateHits)
    {
        double Base(IReadOnlyList<EnemyHit> hits) => hits.Sum(h => (h.Raw - h.Place) / (1 + 0.1 * h.Slow));
        // The multiplier of the place (1.75 for Létalité 75), and the bonuses added there.
        double Mult(IReadOnlyList<EnemyHit> hits)
        {
            var mult = hits.Sum(h => h.PlaceMult);
            var without = hits.Sum(h => h.Raw - h.Place);
            return mult > 0 && without > 0 ? 1 + mult / without : 1;
        }
        double Add(IReadOnlyList<EnemyHit> hits) => hits.Sum(h => h.PlaceAdd);
        double Shiv(IReadOnlyList<EnemyHit> hits) => hits.Sum(h => h.PlaceShiv);
        var (se, sl) = (1 + 0.1 * earlyHits[0].Slow, 1 + 0.1 * lateHits[0].Slow);
        var (be, bl) = (Base(earlyHits), Base(lateHits));
        var played = earlyHits.Sum(h => h.Raw) + lateHits.Sum(h => h.Raw);
        var same = early.Owner == late.Owner;
        // What the first place gives, and the second: same player, they swap; else each card keeps its own.
        var (firstMult, secondMult) = same ? (Mult(earlyHits), Mult(lateHits)) : (Mult(lateHits), Mult(earlyHits));
        var (firstAdd, secondAdd) = same ? (Add(earlyHits), Add(lateHits)) : (Add(lateHits), Add(earlyHits));
        // Lames fantomatiques: the first Surin's bonus; it moves only from a Surin to a Surin.
        var shivMoves = same && early.Shiv && late.Shiv;
        var (firstShiv, secondShiv) = shivMoves ? (Shiv(earlyHits), Shiv(lateHits)) : (Shiv(lateHits), Shiv(earlyHits));
        var swapped = bl * se * firstMult + firstAdd + firstShiv + be * sl * secondMult + secondAdd + secondShiv;
        return swapped - played;
    }

    /// "Card:CHO_FIRM^" → "CHO_FIRM".
    private static string CardId(string key)
    {
        var id = key.StartsWith("Card:", StringComparison.Ordinal) ? key[5..] : key;
        var up = id.IndexOf('^');
        return up < 0 ? id : id[..up];
    }

    /// Cards that pay off an arrow in their target (any player's): Bow Smack, Ki Hop, Wah Hammer, Nail.
    private static readonly HashSet<string> ImpalePayoffs = new() { "BOW_SMACK", "KI_HOP", "WAH_HAMMER", "NAIL" };

    /// Arrows stuck in an enemy come back only after 3 powered hits of their owner on it.
    private const int HitsToFreeArrow = 3;

    /// The Fool's order mistakes (1.2.1, spec 01-metrics "The Fool's arrows"):
    /// - "impaleFirst": Bow Smack / Ki Hop / Wah Hammer / Nail on an enemy with no arrow in it, then the player's Shot stuck one
    ///   there while it was in hand and an arrow was ready; with Serrated Arrows, every earlier attack on that enemy lost its
    ///   bonus (wrong order, damage);
    /// - "shootFirst": the Shot's arrow is still stuck at the end of the turn while 3+ of the player's hits on that enemy came
    ///   before it (Shot in hand, arrow ready): shot first, they freed the arrow;
    /// - Cho Firm with no arrow left / Whistling Arrows after a Shot they were in hand for (wrong order, block);
    /// - Aim spent by a Shot while one taking more of it was played after with no Aim left (wrong order, damage).
    private static void ArrowOrder(TurnState s, TurnResult r)
    {
        var serrated = new HashSet<(TurnPlay, object)>();
        for (var i = 0; i < s.Plays.Count; i++)
        {
            var shot = s.Plays[i];
            if (!shot.ShotArrow || shot.Target is not { } enemy || shot.KilledTarget) continue;
            // The player's cards played before it while it was in hand and an arrow was ready: it could have come first.
            var before = s.Plays.Take(i).Where(x => x.Owner == shot.Owner && x.ArrowsReady > 0 &&
                                                    x.HandAtPlay.Any(h => ReferenceEquals(h, shot.CardRef))).ToList();
            int HitsOn(TurnPlay x) => x.Hits.Count(h => ReferenceEquals(h.Target, enemy));
            foreach (var x in before.Where(x => !x.ArrowIn.Contains(enemy)))
            {
                if (ImpalePayoffs.Contains(CardId(x.CardKey)) && ReferenceEquals(x.Target, enemy) && !x.ImpaledLater)
                {
                    x.ImpaledLater = true;
                    r.Log.Add($"impale first: {x.CardKey} before {shot.CardKey}");
                    r.Events.Add(new MissedChance(x.Owner, "impaleFirst", new[] { x.CardKey, shot.CardKey }));
                }
                if (x.SerratedBonus > 0 && x.Kind == CardKind.Attack && HitsOn(x) > 0 && serrated.Add((x, enemy)))
                    s.OrderLosses.Add(new OrderLoss(x, shot.CardKey, "order", enemy,
                        x.Hits.Where(h => ReferenceEquals(h.Target, enemy)).Sum(h => h.Raw) * x.SerratedBonus));
            }
            // Shoot first: the arrow is still in the enemy at the end of the turn.
            if (shot.OwnArrowIn.Contains(enemy) || s.Player(shot.Owner) is not { } owner || !owner.OwnArrowIn.Contains(enemy)) continue;
            var hitters = before.Where(x => x.Kind == CardKind.Attack && !x.OwnArrowIn.Contains(enemy) && HitsOn(x) > 0).ToList();
            var hits = hitters.Sum(HitsOn);
            if (hits < HitsToFreeArrow) continue;
            r.Log.Add($"shoot first: {shot.CardKey} after {hits} hits on its target");
            r.Events.Add(new MissedChance(shot.Owner, "shootFirst",
                new[] { shot.CardKey, string.Join('+', hitters.Select(x => x.CardKey).Distinct()), N(hits) }));
        }

        for (var i = 0; i < s.Plays.Count; i++)
        {
            var late = s.Plays[i];
            // Cho Firm: twice its block with an arrow ready. Whistling Arrows: back in hand at each shot.
            var id = CardId(late.CardKey);
            var cho = id == "CHO_FIRM" && late.ArrowsReady == 0 && late.BlockGains == 1;
            if ((cho || id == "WHISTLING_ARROWS") && late.GaveBlock && late.BlockGains > 0 &&
                s.Plays.Take(i).LastOrDefault(x => x.Owner == late.Owner && x.ShotArrow && x.HandAtPlay.Any(h => ReferenceEquals(h, late.CardRef))) is { } shot &&
                (!cho || shot.ArrowsReady == 1))
                s.OrderLosses.Add(new OrderLoss(shot, late.CardKey, "orderBlock", null, (double)late.BlockAmount / late.BlockGains, late));
            // Aim: the first arrow attack takes all of it.
            if (late.ShotArrow && late.AimAtStart > 0 && late.Target != null &&
                s.Plays.Skip(i + 1).Where(x => x.Owner == late.Owner && x.ShotArrow && x.AimAtStart == 0 && x.Target != null &&
                                               x.AimFactor > late.AimFactor && late.HandAtPlay.Any(h => ReferenceEquals(h, x.CardRef)))
                    .OrderByDescending(x => x.AimFactor).FirstOrDefault() is { } better)
                s.OrderLosses.Add(new OrderLoss(late, better.CardKey, "order", better.Target, late.AimAtStart * (better.AimFactor - late.AimFactor)));
        }
    }

    /// The game's Vulnérable multiplier (relics that change it are left out of this estimate).
    private const double VulnerableFactor = 1.5;

    /// Vulnérable kept (v1.1.1, Zaille75: Tope là left in hand while Claquement de doigts, Déchaînement and Frappe were played).
    /// An attack putting Vulnérable on enemies stayed in hand while the player paid for plain attacks (no other effect) it could
    /// have replaced. Best line: that card first, then the best of those attacks that still fit its energy, each hit × 1.5 on an
    /// enemy it made Vulnérable (hits on an enemy already Vulnérable are not boosted again), damage capped by each enemy's HP.
    /// Gain = that line − the attacks played, this turn only (orbs never get Vulnérable). Counted ("vulnLeft") when the gain is
    /// at least 1, or when the line deals as much while its Vulnérable lasts into the next turn (2+ stacks, enemy alive at the
    /// end of the turn, not already Vulnérable for next turn). v1.1.1 (Zaille75): no estimate of the next turn's damage, only
    /// "Vulnérable N for the next turn". Any other card the player paid for (power, debuff, block, attack with another effect) was a choice: its
    /// energy is not available to the line.
    private static void VulnerableLeft(TurnState s, TurnPlayer p, TurnResult r)
    {
        var plain = s.Plays.Where(x => x.Owner == p.Actor && x.Kind == CardKind.Attack && !x.HadNonDamageEffect && !x.GaveBlock && !x.PlayedAgain)
            .Take(10).ToList();
        if (plain.Count == 0) return;
        var budget = p.Energy + plain.Sum(x => x.EnergySpent);
        var played = plain.Sum(x => x.Hits.Sum(h => h.Raw));
        // Each enemy's HP + block before these attacks (0 left for the dead ones).
        var before = new Dictionary<object, double>(ReferenceEqualityComparer.Instance);
        foreach (var h in plain.SelectMany(x => x.Hits)) before[h.Target] = before.GetValueOrDefault(h.Target) + h.Raw;
        foreach (var key in before.Keys.ToList()) before[key] += s.EnemiesLeft.GetValueOrDefault(key);
        foreach (var (enemy, left) in s.EnemiesLeft) before.TryAdd(enemy, left);
        (TurnCard Card, double Gain, int Lasting, List<TurnPlay> Line)? best = null;
        foreach (var v in p.Hand.Where(c => c.Kind == CardKind.Attack && c.VulnerableStacks > 0 && c.PlayableExceptEnergy && c.PlayableInstead &&
                                            !c.KeptOnPurpose && !c.CostsX && !c.LacksStars && !c.Punished && (!c.NeedsPet || p.PetAlive) && !c.NoArrow &&
                                            c.Cost <= budget))
        {
            // Its targets: every enemy, or the best single one.
            var choices = v.AllEnemies ? new[] { before.Keys.ToList() } : before.Keys.Select(e => new List<object> { e }).ToArray();
            foreach (var targets in choices)
            {
                var vulnerable = new HashSet<object>(targets, ReferenceEqualityComparer.Instance);
                var room = budget - v.Cost;
                var dealt = new Dictionary<object, double>(ReferenceEqualityComparer.Instance);
                foreach (var e in targets) dealt[e] = v.DamageOn.GetValueOrDefault(e);
                // The best subset of the plain attacks that still fits (at most 10 cards: 1,024 subsets).
                var (bestLine, bestDamage) = (new List<TurnPlay>(), Capped(dealt));
                for (var mask = 1; mask < 1 << plain.Count; mask++)
                {
                    var line = plain.Where((_, i) => (mask & (1 << i)) != 0).ToList();
                    if (line.Sum(x => x.EnergySpent) > room) continue;
                    var total = new Dictionary<object, double>(dealt, ReferenceEqualityComparer.Instance);
                    foreach (var h in line.SelectMany(x => x.Hits))
                        total[h.Target] = total.GetValueOrDefault(h.Target) + h.Raw * (vulnerable.Contains(h.Target) && !h.Vulnerable ? VulnerableFactor : 1);
                    var damage = Capped(total);
                    if (damage > bestDamage) (bestLine, bestDamage) = (line, damage);
                }
                var gain = bestDamage - played;
                // Next turn: an enemy still alive and not already Vulnérable then.
                var lasting = v.VulnerableStacks >= 2 &&
                              targets.Any(e => s.EnemiesLeft.GetValueOrDefault(e) > 0 && s.EnemyVulnerable.GetValueOrDefault(e) < 2);
                var counted = gain >= 1 || (lasting && Math.Round(gain) >= 0);
                if (counted && (best == null || gain > best.Value.Gain || (lasting && best.Value.Lasting == 0 && gain >= best.Value.Gain)))
                    best = (v, gain, lasting ? v.VulnerableStacks : 0, bestLine);
            }
        }
        if (best is not { } b) return;
        r.Log.Add($"vulnerable kept: {b.Card.Key}, this turn {b.Gain:0.#}, Vulnerable next turn {b.Lasting}, line {string.Join(' ', b.Line.Select(x => x.CardKey))}");
        r.Events.Add(new MissedChance(p.Actor, "vulnLeft", new[]
        {
            b.Card.Key, string.Join('+', plain.Select(x => x.CardKey).Distinct()), N((int)Math.Round(b.Gain)), N(b.Lasting)
        }));

        double Capped(Dictionary<object, double> total) => total.Sum(kv => Math.Min(kv.Value, before.GetValueOrDefault(kv.Key, kv.Value)));
    }

    /// An attack wasted on an enemy that would have died anyway (v0.17.42, Zaille75), counted in Gâché:
    /// - "overkillFollowUp": the card killed its target, but what it triggered right after (Trou noir) would have killed it
    ///   alone; aimed at another enemy still alive at the end of the turn, it would have dealt its damage there;
    /// - "overkill": the card killed its target with damage to spare, then a smaller attack that was in hand and would have
    ///   killed it too hit another enemy, still alive at the end of the turn: swapped, that enemy took more.
    /// The damage lost is capped by what that enemy had left.
    private static void Overkills(TurnState s, TurnResult r)
    {
        int Left(object enemy) => s.EnemiesLeft.GetValueOrDefault(enemy);
        foreach (var big in s.Plays.Where(p => p.Kind == CardKind.Attack && p.Target != null && p.KilledTarget && p.Overkill > 0 && !p.HadNonDamageEffect))
        {
            var others = big.DamageOn.Where(kv => !ReferenceEquals(kv.Key, big.Target) && Left(kv.Key) > 0).ToList();
            if (others.Count == 0) continue;
            // v0.18.9 (Zaille75): a power hitting a random enemy (Forme du serpent) is luck while several enemies are alive.
            var followUp = big.FollowUps.Where(f => !ReferenceEquals(f.Target, big.Target) && !(RandomTargets.Contains(f.Source) && big.EnemiesAlive > 1))
                .GroupBy(f => f.Source)
                .Select(g => (Source: g.Key, Damage: g.Min(f => f.Raw))).Where(x => x.Damage >= big.TargetLeftBefore)
                .OrderByDescending(x => x.Damage).FirstOrDefault();
            if (followUp.Source != null)
            {
                var lost = others.Max(kv => Math.Min(kv.Value, Left(kv.Key)));
                if (lost < 1) continue;
                r.Events.Add(new MissedChance(big.Owner, "overkillFollowUp", new[] { big.CardKey, followUp.Source, N(lost) }));
                continue;
            }
            var after = s.Plays.SkipWhile(p => p != big).Skip(1);
            foreach (var small in after.Where(p => p.Owner == big.Owner && p.Kind == CardKind.Attack && !p.HadNonDamageEffect))
            {
                if (!big.HandDamageOnTarget.TryGetValue(small.CardRef, out var onTarget) || onTarget < big.TargetLeftBefore) continue;
                if (small.FirstHit is not { } hit || ReferenceEquals(hit.Target, big.Target) || Left(hit.Target) <= 0 ||
                    !big.DamageOn.TryGetValue(hit.Target, out var bigThere)) continue;
                var lost = Math.Min(bigThere - hit.Damage, Left(hit.Target));
                if (lost < 1) continue;
                r.Events.Add(new MissedChance(big.Owner, "overkill", new[] { big.CardKey, small.CardKey, N(lost) }));
                break;
            }
        }
    }

    /// Lethal missed at the end of the turn (v0.18.9, Zaille75: the last Putrilimace left at 1 HP while Frappes stayed in hand):
    /// the last enemy, whose death ends the fight, is alive, and the attacks left in the players' hands, paid with the energy
    /// left plus the energy spent on cards that did nothing but block (or nothing), would have killed it. Co-op: the players'
    /// hands together. Counted as a missed lethal for every player whose attacks were part of it (not twice in a turn).
    private static void LethalLeft(TurnState s, TurnResult r)
    {
        if (s.LastEnemy is not { } enemy || !s.EnemiesLeft.TryGetValue(enemy, out var left) || left <= 0) return;
        var parts = new List<(TurnPlayer Player, List<TurnCard> Cards)>();
        var total = 0;
        foreach (var p in s.Players.Where(p => p.Alive))
        {
            var freed = s.Plays.Where(x => x.Owner == p.Actor && x.Kind != CardKind.Attack && x.EnergySpent > 0 && !x.HadOtherEffect)
                .Sum(x => x.EnergySpent);
            var budget = p.Energy + freed;
            var attacks = p.Hand.Where(c => c.Kind == CardKind.Attack && c.PlayableExceptEnergy && !c.CostsX && !c.LacksStars && !c.Punished &&
                                            (!c.NeedsPet || p.PetAlive) && !c.NoArrow && c.DamageOn.TryGetValue(enemy, out var d) && d > 0).Take(10).ToList();
            var (bestDamage, bestMask) = (0, 0);
            for (var mask = 1; mask < 1 << attacks.Count; mask++)
            {
                var (cost, damage, shots) = (0, 0, 0);
                for (var i = 0; i < attacks.Count; i++)
                    if ((mask & (1 << i)) != 0)
                    {
                        cost += attacks[i].Cost;
                        damage += attacks[i].DamageOn[enemy];
                        if (attacks[i].Shot) shots++;
                    }
                // 1.2.1: no more Shots than the arrows ready.
                if (cost <= budget && shots <= p.ArrowsReady && damage > bestDamage) (bestDamage, bestMask) = (damage, mask);
            }
            if (bestDamage <= 0) continue;
            total += bestDamage;
            parts.Add((p, attacks.Where((_, i) => (bestMask & (1 << i)) != 0).ToList()));
        }
        if (total < left) return;
        foreach (var (p, _) in parts.Where(x => !x.Player.LethalMissed))
        {
            var cards = parts.SelectMany(x => x.Cards.Select(c => Owned(c.Key, x.Player.Actor, p.Actor))).ToList();
            r.Log.Add($"lethal left: {p.Actor} [{string.Join(", ", cards)}] on {left}");
            r.Events.Add(new MissedLethal(p.Actor, cards, left));
        }
    }

    /// Weak attack (the stronger one still in hand), Pen Nib on a weak attack, smaller block card (the bigger one still in
    /// hand and the block short of the attack).
    private static void WeakPlays(TurnState s, TurnResult r)
    {
        foreach (var play in s.Plays)
        {
            if (s.Player(play.Owner) is not { } p) continue;
            // v0.18.9 (Zaille75): not an attack that also gave block (Vague de fer) or was played again (Écho).
            if (play.Stronger is { } better && !play.HadNonDamageEffect && !play.GaveBlock && !play.PlayedAgain &&
                p.InHand(better.Card) is { KeptOnPurpose: false, NoArrow: false })
                r.Events.Add(new WeakAttack(play.Owner, play.CardKey, better.Played, better.Key, better.Value));
            // Pen Nib doubled a weak attack: counted even if the stronger one was played later (it was not doubled).
            if (play.PenNibBetter is { } nib && !play.HadNonDamageEffect)
                r.Events.Add(new MissedChance(play.Owner, "penNib", new[] { play.CardKey, N(nib.Played), nib.Key, N(nib.Value) }));
            if (play.BiggerBlock is { } bigger && !play.HadOtherEffect && p.InHand(bigger.Card) is { KeptOnPurpose: false } &&
                p.Block < p.Incoming)
                r.Events.Add(new MissedChance(play.Owner, "smallBlock", new[] { play.CardKey, N(bigger.Played), bigger.Key, N(bigger.Value) }));
        }
    }

    /// Cards the player paid energy for this turn that did nothing useful: a block card with no other effect whose block
    /// was not needed (block at the end of the turn minus its block still covers the incoming attack), or a card whose
    /// only effect was a useless debuff (F8).
    /// v0.17.45: also an attack whose every hit met a cap already reached (Carapace endurcie at its limit, Géant de la Cascade
    /// about to explode) and that did nothing else.
    /// v0.18.4: the block cards are judged from the last played back, each one without the block of those already found
    /// useless (Armement 5 + Défense+ 8 + Défense 5 against 10: only the last Défense was not needed, not both Défenses).
    public static List<TurnPlay> UselessPlays(TurnState s, TurnPlayer p)
    {
        var mine = s.Plays.Where(x => x.Owner == p.Actor && x.EnergySpent > 0 && !x.HadOtherEffect).ToList();
        var uselessBlock = new HashSet<TurnPlay>();
        var removed = 0.0;
        foreach (var x in Enumerable.Reverse(mine).Where(x => x.GaveBlock && !x.Ethereal))
            if (p.Block - removed - x.BlockAmount >= p.Incoming)
            {
                uselessBlock.Add(x);
                removed += x.BlockAmount;
            }
        return mine.Where(x => uselessBlock.Contains(x) || (x.UselessDebuff && !x.GaveBlock) || DidNothing(p, x) ||
            (x.Kind == CardKind.Attack && x.ShellCapped > 0 && x.FirstHit == null && !x.HadNonDamageEffect))
            .Concat(BuffsUnused(s, p)).ToList();
    }

    /// The Fool (1.2.0 / 1.2.1): a Shot played with no arrow ready, or Nail / Recall / Uproot / Wrest on a target without the
    /// arrow it needs, that did nothing else. Not when it completed a relic count (Shuriken, Kunai… for attacks, Coupe-papier
    /// for skills) or made one of the player's powers fire.
    public static bool DidNothing(TurnPlayer p, TurnPlay x) =>
        (x.EmptyShot || x.NotImpaled) && x.Hits.Count == 0 && !x.HadNonDamageEffect && !x.GaveBlock && x.PowersTriggered.Count == 0 &&
        !p.Relics.Any(r => r.Every > 0 && x.KindCount > 0 && x.KindCount % r.Every == 0 &&
                           (r.Trigger == RelicTrigger.Attacks && x.Kind == CardKind.Attack || r.Trigger == RelicTrigger.Skills && x.Kind == CardKind.Skill));

    /// Cards whose power on the player ended the turn unused (Quietus with no arrow attack after it, 1.2.1).
    private static IEnumerable<TurnPlay> BuffsUnused(TurnState s, TurnPlayer p) =>
        s.Plays.Where(x => x.Owner == p.Actor && x.PowersGiven.Overlaps(p.UnusedBuffs));

    /// Per player: unused energy, growing card affordable, block card kept, Orichalcum beaten by a small block, relic
    /// triggers missed by one card.
    private static void EndOfTurnChecks(TurnState s, TurnPlayer p, TurnResult r)
    {
        var actor = p.Actor;
        var energy = p.Energy;
        var hand = p.Hand;
        var energyLost = p.EnergyResets && p.PaelsTears == null;

        // Unused energy. Cards kept on purpose are left out (v0.17.0); so is a block card when the block already covers
        // the incoming attack (v0.17.1), and an attack into Thorns the block cannot afford (v0.17.34).
        var blockCovered = p.Block >= p.Incoming;
        var thornsRoom = Math.Max(0, p.Block - p.Incoming);
        bool Punished(TurnCard c) => c.Punished || c.Thorns > thornsRoom;
        bool NeedlessBlock(TurnCard c) => blockCovered && c.GainsBlock && c.Kind == CardKind.Skill;
        // v0.17.45: an attack is no use when every enemy is at its damage cap (Carapace endurcie), nor a pet attack while the
        // pet is dead (unless the player's own card killed it this turn: the attack could have come first).
        bool CanHit(TurnCard c) => c.Kind != CardKind.Attack || (s.OpenEnemy && !s.NothingToKill);
        bool PetReady(TurnCard c) => (!c.NeedsPet || p.PetAlive || p.PetSacrificedThisTurn) && !c.NoArrow;
        bool WouldHelp(TurnCard c) => p.Affordable(c) && !c.KeptOnPurpose && !NeedlessBlock(c) && !Punished(c) && CanHit(c) && PetReady(c);
        var missedEnergy = energy > 0 && !s.NothingToKill && !s.EnemyAsleep && energyLost && hand.Any(WouldHelp);
        if (missedEnergy)
        {
            // Args: the energy, then the cards it could have paid (most expensive first, at most 3).
            var playable = hand.Where(WouldHelp).GroupBy(c => c.Key).Select(g => g.First())
                .OrderByDescending(c => c.CostsX ? energy : c.Cost).Take(3).Select(c => c.Key);
            r.Events.Add(new MissedChance(actor, "energy", new[] { N(energy) }.Concat(playable).ToArray(), energy));
        }
        // Energy lost with nothing useful to spend it on (v0.17.29): counted for the trophy "Survolté".
        // v0.18.6: cards kept on purpose (Danse des lames…) do not stop it from counting.
        var energySurplus = !missedEnergy && energy > 0 && !s.EnemyAsleep && energyLost &&
                      (s.NothingToKill || !hand.Any(c => p.Affordable(c) && !c.KeptOnPurpose && !NeedlessBlock(c) && !Punished(c) && CanHit(c) && PetReady(c)))
            ? energy : 0;

        if (energySurplus > 0) r.Events.Add(new HelpGiven(actor, "energySurplus", energySurplus));

        // Paré au combat kept (1.2.0, Zaille75): played first, it made every attack in hand 1 cheaper. With the energy the
        // attacks played since it was in hand cost plus the energy left, it would have paid for itself and more attacks
        // (those played, cheaper, then the ones left in hand, cheapest first). Choix: "attackDiscount" (card, attacks more).
        if (p.AttackDiscount is { PlayableExceptEnergy: true, CostsX: false } cut && !s.NothingToKill && !s.EnemyAsleep)
        {
            var played = s.Plays.Where(x => x.Owner == actor && x.Kind == CardKind.Attack && !x.CostsX && x.HandAtPlay.Contains(cut.Ref)).ToList();
            var left = played.Sum(x => x.EnergySpent) + energy - cut.Cost;
            if (left >= 0)
            {
                var usable = hand.Where(c => c != cut && c.Kind == CardKind.Attack && c.PlayableExceptEnergy && !c.CostsX && !c.CostsStars &&
                                             !c.KeptOnPurpose && !Punished(c) && CanHit(c) && PetReady(c)).ToList();
                // 1.2.1: no more Shots left in hand than the arrows ready.
                var shots = usable.Where(c => c.Shot).OrderBy(c => c.Cost).Skip(Math.Max(0, p.ArrowsReady)).ToHashSet();
                var costs = played.Select(x => Math.Max(0, x.EnergySpent - 1))
                    .Concat(usable.Where(c => !shots.Contains(c)).Select(c => Math.Max(0, c.Cost - 1)))
                    .OrderBy(c => c);
                var attacks = 0;
                foreach (var cost in costs)
                {
                    if (cost > left) break;
                    left -= cost;
                    attacks++;
                }
                if (attacks > played.Count) r.Events.Add(new MissedChance(actor, "attackDiscount", new[] { cut.Key, N(attacks - played.Count) }));
            }
        }

        // Energy spent for nothing (v0.17.37), even with nothing else to play (v0.18.7, Zaille75: a stat, not a mistake);
        // Pael's Tears: that energy would have been left.
        var useless = UselessPlays(s, p);
        var spentForNothing = useless.Sum(x => x.EnergySpent);
        if (spentForNothing > 0) r.Events.Add(new HelpGiven(actor, "energyUseless", spentForNothing));
        if (energy == 0 && p.PaelsTears is { } tears && useless.FirstOrDefault() is { } first)
            r.Events.Add(new MissedChance(actor, "paelsTears", new[] { tears, first.CardKey }));
        // A useless card that made the player discard an Ethereal curse or status (v0.18.9, Zaille75): left in hand it would
        // have been exhausted; discarded, it comes back. Fine when the card's block was needed.
        foreach (var x in useless.Where(x => x.DiscardedEthereal != null))
            r.Events.Add(new MissedChance(actor, "discardEthereal", new[] { x.CardKey, x.DiscardedEthereal! }));
        // The Fool (1.2.0, Zaille75: "the most obvious mistake"): a Shot played with no arrow ready (the game shows it in red)
        // shoots nothing. Free ones too: the card is gone for the turn. 1.2.1: Nail / Recall / Uproot / Wrest on a target
        // without the arrow they need ("notImpaled"; "impaleFirst" instead when a Shot impaled it later), Quietus unused.
        foreach (var x in s.Plays.Where(x => x.Owner == actor && DidNothing(p, x)))
            if (x.EmptyShot) r.Events.Add(new MissedChance(actor, "emptyShot", new[] { x.CardKey, N(x.EnergySpent) }));
            else if (!x.ImpaledLater) r.Events.Add(new MissedChance(actor, "notImpaled", new[] { x.CardKey, N(x.EnergySpent) }));
        foreach (var x in BuffsUnused(s, p))
            r.Events.Add(new MissedChance(actor, "buffUnused", new[] { x.CardKey, N(x.EnergySpent) }));

        // A growing card could have been played with the energy left, or instead of a useless card (v0.17.7).
        var spareEnergy = energy + useless.Select(x => x.EnergySpent).DefaultIfEmpty(0).Max();
        foreach (var growing in hand.Where(c => c.Growing && !c.StatusOrCurse && c.PlayableExceptEnergy && !c.CostsX && c.Cost <= spareEnergy))
            r.GrowingAffordable.Add(growing.Ref);

        // Block card kept: not one for a single ally (only the ally check below), not La grande manœuvre (its own rule),
        // never an Exhaust card (v0.17.34).
        var kept = hand.Where(c => c.GainsBlock && p.Affordable(c) && !c.Exhausts && !c.Gambit && !c.SacrificesPet && !c.Punished && c.BlockFor != BlockTarget.OneAlly &&
                                   c.Block > 0)
            .OrderByDescending(c => c.Block).FirstOrDefault();
        if (kept != null) r.Events.Add(new BlockCardKept(actor, kept.Key, kept.Block));
        // La grande manœuvre kept in hand (v0.17.23): judged later (HP lost this round, none the next one).
        if (hand.FirstOrDefault(c => c.Gambit && p.Affordable(c)) is { } gambit && !p.GambitActive)
            r.Events.Add(new BlockCardKept(actor, gambit.Key, gambit.Block, Risky: true));
        // A block card for allies kept while an ally is alive.
        if (p.AlliesAlive)
        {
            var forAllies = hand.Where(c => c.GainsBlock && c.BlockFor != BlockTarget.Self && p.Affordable(c) && !c.Exhausts && !c.Punished && c.Block > 0)
                .OrderByDescending(c => c.Block).FirstOrDefault();
            if (forAllies != null)
                r.Events.Add(new AllyBlockCardKept(actor, forAllies.Key, forAllies.Block, forAllies.BlockFor == BlockTarget.AllAllies));
        }

        // Orichalcum gives its block only when the player has none: a smaller block card played instead was a loss (v0.17.45:
        // strictly smaller; the same block lost nothing, Zaille75).
        if (p.Orichalcum is { } orichalcum && p.Block > 0 && p.Block < p.OrichalcumBlock &&
            s.Plays.Any(x => x.Owner == actor && x.GaveBlock && !x.HadOtherEffect && !x.Ethereal))
            r.Events.Add(new MissedChance(actor, "orichalcum", new[] { orichalcum, N(p.Block), N(p.OrichalcumBlock) }));

        // Relic triggers: the extra card must have been worth playing: energy that would be lost anyway, not an attack into
        // Thorns the block could not afford, and not taking the energy a needed block card would use.
        var surplus = p.Block - p.Incoming;
        var blockCards = hand.Where(c => c.GainsBlock && p.Affordable(c)).ToList();
        var blockNeeded = surplus < 0 && blockCards.Count > 0;
        bool WorthPlaying(TurnCard c, bool free)
        {
            if (c.StatusOrCurse || !c.PlayableExceptEnergy || c.KeptOnPurpose || c.Gambit) return false;
            if (!free && (!energyLost || !p.Affordable(c))) return false;
            // v0.18.10: not an attack on a sleeping enemy (waking it is a choice).
            if (c.Kind == CardKind.Attack && (c.Thorns > Math.Max(0, surplus) || !c.DealsDamage || c.Punished || s.EnemyAsleep)) return false;
            if (blockNeeded && !c.GainsBlock && !free && c.Cost + blockCards.Min(b => b.Cost) > energy) return false;
            return true;
        }
        if (s.NothingToKill) return;
        foreach (var relic in p.Relics.Where(x => !x.GivesBlock || p.Block < p.Incoming))
            if (MissedRelicTrigger(relic, p.Plays, hand, energy, WorthPlaying))
                r.Events.Add(new MissedChance(actor, "relic", new[] { relic.Key }));
    }

    /// One more card would have triggered the relic this turn, and a playable one was in hand.
    private static bool MissedRelicTrigger(TurnRelic relic, Dictionary<CardKind, int> plays, List<TurnCard> hand, int energy,
        Func<TurnCard, bool, bool> worthPlaying)
    {
        int Played(CardKind k) => plays.GetValueOrDefault(k);
        bool InHand(CardKind k) => hand.Any(c => c.Kind == k && worthPlaying(c, false));
        var every = relic.Every;
        switch (relic.Trigger)
        {
            case RelicTrigger.Attacks:
                return Played(CardKind.Attack) % every == every - 1 && InHand(CardKind.Attack);
            case RelicTrigger.Skills:
                return Played(CardKind.Skill) % every == every - 1 && InHand(CardKind.Skill);
            case RelicTrigger.AllTypes:
            {
                var missing = new[] { CardKind.Attack, CardKind.Skill, CardKind.Power }.Where(k => Played(k) == 0).ToList();
                return missing.Count == 1 && InHand(missing[0]);
            }
            case RelicTrigger.NthCardFree:
                // The Nth card of the turn is free: energy does not matter.
                return plays.Values.Sum() == every - 1 && hand.Any(c => worthPlaying(c, true));
            case RelicTrigger.EmptyHand:
                // Damage to all enemies when the hand is empty at the end of the turn: every card must be worth playing.
                return hand.Count > 0 && hand.All(c => !c.CostsX && worthPlaying(c, false)) && hand.Sum(c => c.Cost) <= energy;
            default:
                return false;
        }
    }

    /// A useful free card left in hand is a misplay counted in "Gâché" (v0.17.26), unless kept on purpose, Rage with no
    /// attack played while it was in hand, punished, dealing no damage, or nothing left to hit (v0.18.10: nor an attack while
    /// an enemy sleeps, Matriarche lagavulin).
    private static void FreeCardsLeft(TurnState s, TurnPlayer p, TurnResult r)
    {
        if (s.NothingToKill) return;
        var room = Math.Max(0, p.Block - p.Incoming);
        // Rage gives block for each attack played after it: left in hand, it lost something only if an attack was played
        // while it was in hand (v0.17.34).
        bool RageWouldHelp(TurnCard rage) =>
            s.Plays.Any(x => x.Owner == p.Actor && x.Kind == CardKind.Attack && x.HandAtPlay.Any(h => ReferenceEquals(h, rage.Ref)));
        foreach (var c in p.Hand.Where(c => c.Free && c.Useful && !c.StatusOrCurse && c.PlayableExceptEnergy && !c.KeptOnPurpose && !c.NoArrow &&
                                            !c.Punished && !(c.Rage && !RageWouldHelp(c)) &&
                                            (c.Kind != CardKind.Attack || (c.DealsDamage && c.Thorns <= room && !s.EnemyAsleep))))
            r.Events.Add(new MissedChance(p.Actor, "freeCard", new[] { c.Key }));
    }

    /// Ruée plays attacks from the hand at the end of the turn (v0.18.6, Zaille75): when it found none, the last attacks the
    /// player paid for themself (one enemy alive, so Ruée would have hit the same one) could have been left to it, and their
    /// energy spent on a card still in hand. Counted in Gâché ("stampede"), one per attack, the latest first.
    private static void Stampede(TurnState s, TurnPlayer p, TurnResult r)
    {
        if (p.StampedeShort <= 0 || s.NothingToKill) return;
        var budget = p.Energy;
        var used = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var attack in s.Plays.Where(x => x.Owner == p.Actor && x.Kind == CardKind.Attack && x.EnergySpent > 0 && x.EnemiesAlive == 1)
                     .Reverse().Take(p.StampedeShort))
        {
            budget += attack.EnergySpent;
            // Any card left in hand that the energy pays (a block card only if the block was short; HP-costing cards included).
            var instead = p.Hand.FirstOrDefault(c => !used.Contains(c.Ref) && !c.StatusOrCurse && c.PlayableExceptEnergy && !c.CostsX && !c.Punished && !c.NoArrow &&
                                                    c.Cost <= budget && c.Kind != CardKind.Attack && (!c.GainsBlock || p.Block < p.Incoming));
            if (instead == null) continue;
            used.Add(instead.Ref);
            budget -= instead.Cost;
            r.Events.Add(new MissedChance(p.Actor, "stampede", new[] { attack.CardKey, p.StampedeKey, instead.Key }));
        }
    }

    /// A card whose draw was stopped by Pas de pioche (v0.18.6, Zaille75: Offrande after Ivresse du combat): a misplay when
    /// it did nothing else, or when the energy it gave was still left at the end of the turn. Counted in Gâché ("noDraw").
    private static void DrawBlocked(TurnState s, TurnPlayer p, TurnResult r)
    {
        foreach (var play in s.Plays.Where(x => x.Owner == p.Actor && x.DrawBlocked))
            if (play.EnergyGiven > 0 ? p.Energy >= play.EnergyGiven : !play.HadOtherEffect)
                r.Events.Add(new MissedChance(p.Actor, "noDraw", new[] { play.CardKey }));
    }

    /// Cards created into a full hand go to the discard pile (v0.18.6, Zaille75: Passe-passe with 6 cards in hand): each card
    /// played after it that was already in hand, and could have been paid before, would have made room for one of them.
    /// Counted in Choix ("handFull"): one per card that could have been kept.
    private static void FullHand(TurnState s, TurnPlayer p, TurnResult r)
    {
        foreach (var maker in s.Plays.Where(x => x.Owner == p.Actor && x.LostToFullHand > 0))
        {
            var room = maker.EnergyBefore - maker.EnergySpent;
            var before = new List<TurnPlay>();
            foreach (var later in s.Plays.SkipWhile(x => x != maker).Skip(1)
                         // Already in hand before the creating card (not the cards it created, Surins of a played-again Passe-passe).
                         .Where(x => x.Owner == p.Actor && maker.HandWhenFull.Contains(x.CardRef) && maker.HandAtPlay.Any(h => ReferenceEquals(h, x.CardRef))))
            {
                if (before.Count >= maker.LostToFullHand) break;
                if (later.EnergySpent > room) continue;
                room -= later.EnergySpent;
                before.Add(later);
            }
            if (before.Count == 0) continue;
            r.Events.Add(new MissedChance(p.Actor, "handFull",
                new[] { maker.CardKey, N(before.Count) }.Concat(before.Select(x => x.CardKey).Distinct()).ToArray(), before.Count));
        }
    }

    /// A card left in hand that would have been worth playing with more energy (v0.18.7).
    private static bool UsefulLeft(TurnState s, TurnPlayer p, TurnCard c) =>
        !c.StatusOrCurse && c.PlayableExceptEnergy && !c.KeptOnPurpose && !c.Punished && !c.CostsX && !c.LacksStars &&
        (!c.GainsBlock || p.Block < p.Incoming) && (c.Kind != CardKind.Attack || (s.OpenEnemy && !s.NothingToKill && c.DealsDamage)) &&
        (!c.NeedsPet || p.PetAlive) && !c.NoArrow;

    /// The useful card left in hand that only the saved energy would have paid (not already affordable).
    private static TurnCard? PaidBySaving(TurnState s, TurnPlayer p, int saved, HashSet<object> used, object? besides = null) =>
        p.Hand.Where(c => !ReferenceEquals(c.Ref, besides) && !used.Contains(c.Ref) && UsefulLeft(s, p, c) && c.Cost > p.Energy && c.Cost <= p.Energy + saved)
            .OrderByDescending(c => c.Cost).FirstOrDefault();

    /// An attack whose damage a cap stopped in part (v0.18.7, Zaille75): a cheaper attack left in hand would have dealt as much
    /// to that enemy, and the energy saved would have paid a useful card left in hand. Damage only. Counted in Choix ("capCheaper").
    private static void CappedAttacks(TurnState s, TurnPlayer p, TurnResult r)
    {
        var used = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var x in s.Plays.Where(x => x.Owner == p.Actor && x.Kind == CardKind.Attack && x.Capped && x.EnergySpent > 0 &&
                                             !x.HadNonDamageEffect && x.FirstHit != null))
        {
            var dealt = x.FirstHit!.Value.Damage;
            var cheaper = x.HandDamageOnTarget.Select(kv => (Card: p.InHand(kv.Key), Damage: kv.Value))
                .Where(a => a.Card is { KeptOnPurpose: false, CostsX: false, Punished: false, NoArrow: false } c && !used.Contains(c.Ref) && c.Cost < x.EnergySpent && a.Damage >= dealt)
                .OrderBy(a => a.Card!.Cost).Select(a => a.Card).FirstOrDefault();
            if (cheaper == null) continue;
            if (PaidBySaving(s, p, x.EnergySpent - cheaper.Cost, used, cheaper.Ref) is not { } instead) continue;
            used.Add(cheaper.Ref);
            used.Add(instead.Ref);
            r.Events.Add(new MissedChance(p.Actor, "capCheaper", new[] { x.CardKey, cheaper.Key, instead.Key }));
        }
    }

    /// Ruée played every attack left in hand for free (v0.18.7, Zaille75): an expensive damage-only attack the player paid for
    /// (one enemy alive) could have been left to Ruée, a cheaper one it played paid instead, and the energy saved would have
    /// paid a useful card left in hand. Counted in Gâché ("stampedeSwap").
    private static void StampedeSwap(TurnState s, TurnPlayer p, TurnResult r)
    {
        if (p.StampedePlayed.Count == 0 || s.NothingToKill) return;
        var used = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var y in s.Plays.Where(x => x.Owner == p.Actor && x.Kind == CardKind.Attack && x.EnergySpent > 0 && !x.HadNonDamageEffect &&
                                             x.EnemiesAlive == 1))
        {
            var z = p.StampedePlayed.Where(c => !used.Contains(c.Ref) && !c.CostsX && c.Cost < y.EnergySpent &&
                                                y.HandAtPlay.Any(h => ReferenceEquals(h, c.Ref)))
                .OrderBy(c => c.Cost).FirstOrDefault();
            if (z == null) continue;
            if (PaidBySaving(s, p, y.EnergySpent - z.Cost, used) is not { } instead) continue;
            used.Add(z.Ref);
            used.Add(instead.Ref);
            r.Events.Add(new MissedChance(p.Actor, "stampedeSwap", new[] { y.CardKey, z.Key, instead.Key }));
        }
    }

    /// A card drawing cards played before a card creating cards into the now full hand (v0.18.7, Zaille75: Acrobaties then
    /// Passe-passe): the other way round, the created cards had room and the draw only stopped (nothing is lost by a draw).
    /// Counted when the creating card was already in hand and the energy allowed it first. Choix ("drawFirst").
    private static void DrawBeforeMaking(TurnState s, TurnPlayer p, TurnResult r)
    {
        foreach (var maker in s.Plays.Where(x => x.Owner == p.Actor && x.LostToFullHand > 0))
            foreach (var draw in s.Plays.TakeWhile(x => x != maker).Where(x => x.Owner == p.Actor && x.CardsDrawn > 0 &&
                         x.HandAtPlay.Any(h => ReferenceEquals(h, maker.CardRef)) && x.EnergyBefore >= x.EnergySpent + maker.EnergySpent))
                r.Events.Add(new MissedChance(p.Actor, "drawFirst",
                    new[] { draw.CardKey, maker.CardKey, N(Math.Min(maker.LostToFullHand, draw.CardsDrawn)) }));
    }

    /// Wasted block (spec §F): each block card played had a real alternative if a useful card in hand when it was played is
    /// still in hand, affordable with its energy + the energy left. Each card can be the alternative of only one block card.
    /// Newest block plays first (v0.17.25): block is used oldest first, so the last one played is the one most likely left
    /// unused. Returns the plays that got an alternative.
    private static HashSet<TurnPlay> BlockAlternatives(TurnState s, HashSet<object> used, TurnResult r)
    {
        var withAlternative = new HashSet<TurnPlay>();
        foreach (var play in s.Plays.Where(x => x.GaveBlock).Reverse())
        {
            var p = s.Player(play.Owner);
            var budget = play.EnergySpent + (p?.Energy ?? 0);
            var room = p == null ? 0 : Math.Max(0, p.Block - p.Incoming);
            // A free card left in hand is its own miss (FreeCardsLeft); an attack into Thorns counts only if the block left
            // would still cover the Thorns damage.
            var alternative = p == null || play.HadOtherEffect || play.Ethereal ? null : play.UsefulCardsInHand
                .Select(seen => p.InHand(seen.Card))
                .FirstOrDefault(c => c != null && !used.Contains(c.Ref) && !c.KeptOnPurpose && !c.NoArrow && c.Cost <= budget && c.PlayableInstead &&
                                     !c.Free && c.Thorns <= room && !c.Punished);
            if (alternative != null)
            {
                used.Add(alternative.Ref);
                withAlternative.Add(play);
            }
            r.Log.Add($"block play {play.Id}: other effect {play.HadOtherEffect}, ethereal {play.Ethereal}, energy budget {budget}, " +
                      $"useful cards in hand when played: [{string.Join(", ", play.UsefulCardsInHand.Select(c => c.Key))}] → alternative: " +
                      (alternative?.Key ?? "none"));
            r.Events.Add(new BlockPlayResolved(play.Id, alternative != null, alternative == null || p == null ? null :
                new BlockChoice(play.CardKey, alternative.Key, p.Hand.Where(c => c.Useful).Select(c => c.Key).ToList(), p.Block, p.Incoming)));
        }
        return withAlternative;
    }

    /// Stars are not refilled each turn (v0.17.34): a card paid with Stars that did nothing but block or damage was a misplay
    /// when its block was not needed (Gâché, "starsWasted", unless already a wasted block), or when an energy card left in
    /// hand, paid with the energy left, did the same job (Choix, "stars").
    private static void Stars(TurnState s, TurnPlayer p, HashSet<TurnPlay> withAlternative, HashSet<object> used, TurnResult r)
    {
        var energy = p.Energy;
        bool Candidate(TurnCard c) => !used.Contains(c.Ref) && !c.StatusOrCurse && c.PlayableExceptEnergy && !c.CostsX && !c.CostsStars &&
                                      !c.KeptOnPurpose && !c.NoArrow && c.Cost <= energy;
        foreach (var play in s.Plays.Where(x => x.Owner == p.Actor && x.StarsSpent > 0))
        {
            TurnCard? instead = null;
            if (play.GaveBlock && !play.HadOtherEffect && play.Kind != CardKind.Attack)
            {
                var missing = p.Incoming - (p.Block - play.BlockAmount);
                if (missing <= 0)
                {
                    if (!withAlternative.Contains(play))
                        r.Events.Add(new MissedChance(p.Actor, "starsWasted", new[] { play.CardKey, N(play.StarsSpent) }));
                    continue;
                }
                instead = p.Hand.Where(c => Candidate(c) && c.GainsBlock && c.BlockFor != BlockTarget.OneAlly && c.Block >= missing)
                    .OrderBy(c => c.Cost).FirstOrDefault();
            }
            else if (play.Kind == CardKind.Attack && !play.HadNonDamageEffect && play.FirstHit is { } hit)
                instead = p.Hand.Where(c => Candidate(c) && c.Kind == CardKind.Attack && c.HitsEnemies && !c.Punished && c.Thorns <= play.Thorns &&
                                            c.DamageOn.TryGetValue(hit.Target, out var damage) && damage >= hit.Damage)
                    .OrderBy(c => c.Cost).FirstOrDefault();
            if (instead == null) continue;
            used.Add(instead.Ref);
            energy -= instead.Cost;
            r.Log.Add($"stars: {play.CardKey} paid {play.StarsSpent} Stars, {instead.Key} could do it with the energy left");
            r.Events.Add(new MissedChance(p.Actor, "stars", new[] { play.CardKey, N(play.StarsSpent), instead.Key }));
        }
    }

    /// An attack whose every hit met a Carapace endurcie at its limit (0 damage) and that did nothing else, while a more
    /// useful card was playable instead (v0.17.34): a block card when the block did not cover the attack, an attack on an
    /// enemy without that limit, or another useful card. Counted in Choix ("shell").
    private static void Shell(TurnState s, TurnPlayer p, HashSet<object> used, TurnResult r)
    {
        var blockShort = p.Block < p.Incoming;
        foreach (var play in s.Plays.Where(x => x.Owner == p.Actor && x.ShellCapped > 0 && x.FirstHit == null && !x.HadNonDamageEffect &&
                                                x.Kind == CardKind.Attack))
        {
            var budget = play.EnergySpent + p.Energy;
            var instead = play.HandAtPlay.Select(p.InHand).FirstOrDefault(c => c != null && !used.Contains(c.Ref) && !c.StatusOrCurse &&
                c.PlayableExceptEnergy && !c.LacksStars && !c.KeptOnPurpose && !c.NoArrow && !c.CostsX && c.Cost <= budget &&
                (c.GainsBlock ? blockShort
                    : c.Kind == CardKind.Attack ? s.OpenEnemy && c.DealsDamage && !c.Punished
                    : c.Useful));
            if (instead == null) continue;
            used.Add(instead.Ref);
            r.Events.Add(new MissedChance(p.Actor, "shell", new[] { play.CardKey, s.ShellKey, instead.Key }));
        }
    }
}
