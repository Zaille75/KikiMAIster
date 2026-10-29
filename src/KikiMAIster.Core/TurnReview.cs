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
    /// Retained, paid with Stars or Exhaust: not playing it is a choice.
    public bool KeptOnPurpose { get; init; }
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
    /// The enemies punish it like Thorns (Personal Hive).
    public bool Punished { get; init; }
    public bool DealsDamage { get; init; } = true;
    /// Estimated damage on each enemy; an enemy is missing when the card's number of hits is unknown.
    public IReadOnlyDictionary<object, int> DamageOn { get; init; } = NoDamage;

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
public readonly record struct EnemyHit(object Target, int Raw, int Slow);

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
    public int EnergySpent { get; init; }
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

    /// The first enemy it hit, with all the raw damage it dealt to that enemy; null when it hit no enemy.
    public (object Target, int Damage)? FirstHit =>
        Hits.Count == 0 ? null : (Hits[0].Target, Hits.Where(h => ReferenceEquals(h.Target, Hits[0].Target)).Sum(h => h.Raw));
}

/// Damage or block a later card or potion would have added to an earlier play (wrong order, spec §E0).
/// Kind "order" / "orderSlow": Extra damage on Enemy; "orderBlock": Extra block; "orderDark": Dark orb charge.
public sealed record OrderLoss(TurnPlay Early, string Boost, string Kind, object? Enemy, double Extra);

public sealed record TurnRelic(string Key, RelicTrigger Trigger, int Every);

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
    /// La grande manœuvre's power is already on the player.
    public bool GambitActive { get; init; }
    public bool AlliesAlive { get; init; }
    /// Cards played this turn (auto-plays included, as relics count them), by kind.
    public Dictionary<CardKind, int> Plays { get; init; } = new();

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

    public static TurnResult Resolve(TurnState s)
    {
        var r = new TurnResult();
        SlowAttackOrder(s);
        ResolveOrder(s, r);
        WeakPlays(s, r);
        Overkills(s, r);
        foreach (var p in s.Players.Where(p => p.Alive)) EndOfTurnChecks(s, p, r);
        foreach (var p in s.Players.Where(p => p.Alive)) FreeCardsLeft(s, p, r);
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
    /// incoming attack (up to the difference). One miss per (earlier play, boosting card or potion).
    private static void ResolveOrder(TurnState s, TurnResult r)
    {
        var deficit = new Dictionary<Actor, int>();
        foreach (var g in s.OrderLosses.GroupBy(o => (o.Early, o.Boost, o.Kind)))
        {
            var (early, boost, kind) = g.Key;
            double lost;
            // Dark orb charged: the damage it stores is lost whatever the enemies left.
            if (kind == "orderDark")
                lost = g.Sum(o => o.Extra);
            else if (kind is "order" or "orderSlow")
                lost = g.Where(o => o.Enemy != null).GroupBy(o => o.Enemy!, ReferenceEqualityComparer.Instance)
                    .Sum(e => s.EnemiesLeft.TryGetValue(e.Key, out var left) ? Math.Min(e.Sum(o => o.Extra), left) : 0);
            else
            {
                if (!deficit.TryGetValue(early.Owner, out var left))
                    left = s.Player(early.Owner) is { } p ? Math.Max(0, p.Incoming - p.Block) : 0;
                lost = Math.Min(g.Sum(o => o.Extra), left);
                deficit[early.Owner] = left - (int)lost;
            }
            var amount = (int)Math.Round(lost);
            if (amount < 1) continue;
            r.Log.Add($"wrong order: {early.CardKey} before {boost}, {kind} {amount}");
            r.Events.Add(new MissedChance(early.Owner, kind, new[] { early.CardKey, boost, N(amount) }));
        }
    }

    /// Slow (Ralenti): each card played makes the enemy take 10% more attack damage, so attacks on it should go from the
    /// smallest to the biggest (v0.17.42, Zaille75). A bigger attack played before a smaller one that was already in a hand
    /// (the player's own, or an ally's in co-op) lost 10% × (its base − the smaller one's base) per card played between
    /// them. Counted for the player of the bigger attack, in Choix ("orderSlow"), when at least 1 damage is lost.
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
                    if (lateHits.Count == 0) continue;
                    double Base(List<EnemyHit> hits) => hits.Sum(h => h.Raw / (1 + 0.1 * h.Slow));
                    var gap = lateHits[0].Slow - earlyHits[0].Slow;
                    var extra = 0.1 * gap * (Base(earlyHits) - Base(lateHits));
                    if (gap > 0 && extra > 0) s.OrderLosses.Add(new OrderLoss(early, late.CardKey, "orderSlow", target, extra));
                }
            }
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
            var followUp = big.FollowUps.Where(f => !ReferenceEquals(f.Target, big.Target)).GroupBy(f => f.Source)
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

    /// Weak attack (the stronger one still in hand), Pen Nib on a weak attack, smaller block card (the bigger one still in
    /// hand and the block short of the attack).
    private static void WeakPlays(TurnState s, TurnResult r)
    {
        foreach (var play in s.Plays)
        {
            if (s.Player(play.Owner) is not { } p) continue;
            if (play.Stronger is { } better && !play.HadNonDamageEffect && p.InHand(better.Card) is { KeptOnPurpose: false })
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
    public static List<TurnPlay> UselessPlays(TurnState s, TurnPlayer p) =>
        s.Plays.Where(x => x.Owner == p.Actor && x.EnergySpent > 0 && !x.HadOtherEffect &&
            ((x.GaveBlock && !x.Ethereal && p.Block - x.BlockAmount >= p.Incoming) || (x.UselessDebuff && !x.GaveBlock))).ToList();

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
        bool WouldHelp(TurnCard c) => p.Affordable(c) && !c.KeptOnPurpose && !NeedlessBlock(c) && !Punished(c);
        if (energy > 0 && !s.NothingToKill && !s.EnemyAsleep && energyLost && hand.Any(WouldHelp))
        {
            // Args: the energy, then the cards it could have paid (most expensive first, at most 3).
            var playable = hand.Where(WouldHelp).GroupBy(c => c.Key).Select(g => g.First())
                .OrderByDescending(c => c.CostsX ? energy : c.Cost).Take(3).Select(c => c.Key);
            r.Events.Add(new MissedChance(actor, "energy", new[] { N(energy) }.Concat(playable).ToArray(), energy));
        }
        // Energy lost with nothing useful to spend it on (v0.17.29): counted for the trophy "Batterie pleine".
        else if (energy > 0 && !s.EnemyAsleep && energyLost &&
                 (s.NothingToKill || !hand.Any(c => p.Affordable(c) && !NeedlessBlock(c) && !Punished(c))))
            r.Events.Add(new HelpGiven(actor, "energySurplus", energy));

        // Energy spent for nothing (v0.17.37); Pael's Tears: that energy would have been left.
        var useless = UselessPlays(s, p);
        var spentForNothing = useless.Sum(x => x.EnergySpent);
        if (spentForNothing > 0) r.Events.Add(new HelpGiven(actor, "energyUseless", spentForNothing));
        if (energy == 0 && p.PaelsTears is { } tears && useless.FirstOrDefault() is { } first)
            r.Events.Add(new MissedChance(actor, "paelsTears", new[] { tears, first.CardKey }));

        // A growing card could have been played with the energy left, or instead of a useless card (v0.17.7).
        var spareEnergy = energy + useless.Select(x => x.EnergySpent).DefaultIfEmpty(0).Max();
        foreach (var growing in hand.Where(c => c.Growing && !c.StatusOrCurse && c.PlayableExceptEnergy && !c.CostsX && c.Cost <= spareEnergy))
            r.GrowingAffordable.Add(growing.Ref);

        // Block card kept: not one for a single ally (only the ally check below), not La grande manœuvre (its own rule),
        // never an Exhaust card (v0.17.34).
        var kept = hand.Where(c => c.GainsBlock && p.Affordable(c) && !c.Exhausts && !c.Gambit && c.BlockFor != BlockTarget.OneAlly && c.Block > 0)
            .OrderByDescending(c => c.Block).FirstOrDefault();
        if (kept != null) r.Events.Add(new BlockCardKept(actor, kept.Key, kept.Block));
        // La grande manœuvre kept in hand (v0.17.23): judged later (HP lost this round, none the next one).
        if (hand.FirstOrDefault(c => c.Gambit && p.Affordable(c)) is { } gambit && !p.GambitActive)
            r.Events.Add(new BlockCardKept(actor, gambit.Key, gambit.Block, Risky: true));
        // A block card for allies kept while an ally is alive.
        if (p.AlliesAlive)
        {
            var forAllies = hand.Where(c => c.GainsBlock && c.BlockFor != BlockTarget.Self && p.Affordable(c) && !c.Exhausts && c.Block > 0)
                .OrderByDescending(c => c.Block).FirstOrDefault();
            if (forAllies != null)
                r.Events.Add(new AllyBlockCardKept(actor, forAllies.Key, forAllies.Block, forAllies.BlockFor == BlockTarget.AllAllies));
        }

        // Orichalcum gives its block only when the player has none: a smaller block card played instead was a loss.
        if (p.Orichalcum is { } orichalcum && p.Block > 0 && p.Block <= p.OrichalcumBlock &&
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
            if (c.Kind == CardKind.Attack && (c.Thorns > Math.Max(0, surplus) || !c.DealsDamage || c.Punished)) return false;
            if (blockNeeded && !c.GainsBlock && !free && c.Cost + blockCards.Min(b => b.Cost) > energy) return false;
            return true;
        }
        if (s.NothingToKill) return;
        foreach (var relic in p.Relics)
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
    /// attack played while it was in hand, punished, dealing no damage, or nothing left to hit.
    private static void FreeCardsLeft(TurnState s, TurnPlayer p, TurnResult r)
    {
        if (s.NothingToKill) return;
        var room = Math.Max(0, p.Block - p.Incoming);
        // Rage gives block for each attack played after it: left in hand, it lost something only if an attack was played
        // while it was in hand (v0.17.34).
        bool RageWouldHelp(TurnCard rage) =>
            s.Plays.Any(x => x.Owner == p.Actor && x.Kind == CardKind.Attack && x.HandAtPlay.Any(h => ReferenceEquals(h, rage.Ref)));
        foreach (var c in p.Hand.Where(c => c.Free && c.Useful && !c.StatusOrCurse && c.PlayableExceptEnergy && !c.KeptOnPurpose &&
                                            !c.Punished && !(c.Rage && !RageWouldHelp(c)) &&
                                            (c.Kind != CardKind.Attack || (c.DealsDamage && c.Thorns <= room))))
            r.Events.Add(new MissedChance(p.Actor, "freeCard", new[] { c.Key }));
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
                .FirstOrDefault(c => c != null && !used.Contains(c.Ref) && !c.KeptOnPurpose && c.Cost <= budget && c.PlayableInstead &&
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
                                      !c.KeptOnPurpose && c.Cost <= energy;
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
                c.PlayableExceptEnergy && !c.LacksStars && !c.KeptOnPurpose && !c.CostsX && c.Cost <= budget &&
                (c.GainsBlock ? blockShort
                    : c.Kind == CardKind.Attack ? s.OpenEnemy && c.DealsDamage && !c.Punished
                    : c.Useful));
            if (instead == null) continue;
            used.Add(instead.Ref);
            r.Events.Add(new MissedChance(p.Actor, "shell", new[] { play.CardKey, s.ShellKey, instead.Key }));
        }
    }
}
