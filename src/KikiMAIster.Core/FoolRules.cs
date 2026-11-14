namespace KikiMAIster.Core;

/// The Fool's melodies and more arrow mistakes (1.2.2, spec 01-metrics "The Fool's melodies" and the rows after it).
public static partial class TurnReview
{
    private static readonly string[] AllTags = { "WAH", "CHO", "KI" };

    /// Cards that read the turn's melodies when played, and the tags they count (all three for those that grow with each one).
    private static readonly Dictionary<string, string[]> MelodyPayoffs = new()
    {
        ["MELODIC_SHOT"] = AllTags, ["RHYTHM_STRIKE"] = AllTags, ["HARMONIC_SHIELD"] = AllTags, ["STEADY_CADENCE"] = AllTags,
        ["CODA"] = AllTags, ["WAH_BLAST"] = new[] { "CHO", "KI" }, ["CHO_LIMBS"] = new[] { "WAH", "KI" }, ["KI_REST"] = new[] { "WAH", "CHO" }
    };

    /// Payoffs with a yes / no bonus (one of their tags is enough).
    private static readonly HashSet<string> BinaryPayoffs = new() { "WAH_BLAST", "CHO_LIMBS", "KI_REST" };

    /// Powers set off by the turn's first card of a tag, and the tags they react to.
    private static readonly Dictionary<string, string[]> FirstTagPowers = new()
    {
        ["VIOLENT_CHORD"] = new[] { "WAH" }, ["SONG_PATH"] = new[] { "CHO" }, ["MOTIF"] = AllTags, ["LOST_LUTE"] = AllTags
    };

    /// Arrow payoffs that still do something on an enemy without an arrow (Clouer does nothing: "notImpaled").
    private static readonly HashSet<string> TargetPayoffs = new() { "BOW_SMACK", "KI_HOP", "WAH_HAMMER" };

    private const string KiVersePower = "Power:KI_VERSE_POWER";

    private static bool InHandAt(TurnPlay at, TurnPlay card) => at.HandAtPlay.Any(h => ReferenceEquals(h, card.CardRef));

    /// The tags a play added to the turn's melodies (its card's first ones, and those of the cards it auto-played): the tags played
    /// before the player's next card (or at the end of the turn) that were not played before it.
    private static HashSet<string> Added(TurnState s, TurnPlay x)
    {
        var after = s.Plays.SkipWhile(y => y != x).Skip(1).FirstOrDefault(y => y.Owner == x.Owner)?.MelodyBefore
                    ?? s.Player(x.Owner)?.MelodyTags ?? new HashSet<string>();
        var added = new HashSet<string>(after);
        added.ExceptWith(x.MelodyBefore);
        return added;
    }

    /// Melody order (before ResolveOrder: damage and block losses are wrong-order losses).
    private static void MelodyOrder(TurnState s, TurnResult r)
    {
        foreach (var p in s.Players)
        {
            PayoffsTooEarly(s, p, r);
            PowersTooLate(s, p, r);
        }
    }

    /// Its cost once played after the melody cards: Coda 2 less per tag gained, Branches Cho 2 less with its bonus.
    private static int CostAfter(string id, TurnPlay payoff, int extra) => id switch
    {
        "CODA" => Math.Max(0, payoff.EnergySpent - 2 * extra),
        "CHO_LIMBS" when extra > 0 => Math.Max(0, payoff.EnergySpent - 2),
        _ => payoff.EnergySpent
    };

    /// A melody payoff played before melody cards already in hand that would have added a tag it counts (spec "Melody payoff too
    /// early"). Damage and block: wrong-order losses; draw, energy, Main retenue: Choix "melodyFirst" (card, cards, loss, amount).
    private static void PayoffsTooEarly(TurnState s, TurnPlayer p, TurnResult r)
    {
        var mine = s.Plays.Where(x => x.Owner == p.Actor).ToList();
        var used = new HashSet<object>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < mine.Count; i++)
        {
            var payoff = mine[i];
            var id = CardId(payoff.CardKey);
            if (!MelodyPayoffs.TryGetValue(id, out var counted) || payoff.EnergyGiven > 0) continue;
            var binary = BinaryPayoffs.Contains(id);
            var had = payoff.MelodyBefore.Intersect(counted).ToHashSet();
            if (had.Count == counted.Length || (binary && had.Count > 0)) continue;
            // The melody cards moved before it, in the order played, while the energy allows.
            var gained = new HashSet<string>();
            var moved = new List<TurnPlay>();
            var energy = 0;
            foreach (var m in mine.Skip(i + 1))
            {
                if (!InHandAt(payoff, m)) continue;
                var adds = m.Melody.Where(t => counted.Contains(t) && !had.Contains(t) && !gained.Contains(t)).ToList();
                if (adds.Count == 0 || NeededPayoff(s, mine, payoff, m)) continue;
                var extraThen = binary ? 1 : gained.Count + adds.Count;
                if (payoff.EnergyBefore < energy + m.EnergySpent + CostAfter(id, payoff, extraThen)) continue;
                gained.UnionWith(adds);
                moved.Add(m);
                energy += m.EnergySpent;
                if (binary || had.Count + gained.Count >= counted.Length) break;
            }
            if (gained.Count == 0) continue;
            var extra = binary ? 1 : gained.Count;
            var boosts = string.Join('+', moved.Select(m => m.CardKey).Distinct());
            r.Log.Add($"melody first: {payoff.CardKey} before {boosts}, +{extra} melody");
            switch (id)
            {
                case "MELODIC_SHOT" or "RHYTHM_STRIKE" when payoff.Hits.Count > 0:
                {
                    // 1 + melodies hits per play (Écho & co. repeat them).
                    var plays = Math.Max(1, (int)Math.Round((double)payoff.Hits.Count / (1 + had.Count)));
                    var target = payoff.Target ?? payoff.Hits[0].Target;
                    s.OrderLosses.Add(new OrderLoss(payoff, boosts, "order", target, payoff.Hits.Average(h => h.Raw) * extra * plays));
                    break;
                }
                case "WAH_BLAST":
                    // Its second hit on each enemy.
                    foreach (var g in payoff.Hits.GroupBy(h => h.Target, ReferenceEqualityComparer.Instance))
                        s.OrderLosses.Add(new OrderLoss(payoff, boosts, "order", g.Key, g.Sum(h => h.Raw)));
                    break;
                case "HARMONIC_SHIELD" when payoff.MelodyUnit > 0:
                    s.OrderLosses.Add(new OrderLoss(payoff, boosts, "orderBlock", null, payoff.MelodyUnit * extra * Math.Max(1, payoff.BlockGains)));
                    break;
                case "STEADY_CADENCE" when !payoff.DrawBlocked:
                {
                    var lost = Math.Min(extra, MaxHandSize - payoff.HandAtPlay.Count - payoff.CardsDrawn);
                    if (lost >= 1) r.Events.Add(new MissedChance(p.Actor, "melodyFirst", new[] { payoff.CardKey, boosts, "Draws", N(lost) }));
                    break;
                }
                case "CODA" or "CHO_LIMBS":
                {
                    var saved = payoff.EnergySpent - CostAfter(id, payoff, extra);
                    if (saved < 1 || PaidBySaving(s, p, saved, used) is not { } instead) break;
                    used.Add(instead.Ref);
                    r.Events.Add(new MissedChance(p.Actor, "melodyFirst", new[] { payoff.CardKey, boosts, "Energy", N(saved) }));
                    break;
                }
                case "KI_REST":
                {
                    var kept = p.Hand.Count(c => !c.StatusOrCurse && !c.Ethereal && !c.Retains);
                    if (kept >= 1) r.Events.Add(new MissedChance(p.Actor, "melodyFirst", new[] { payoff.CardKey, boosts, "Retain", N(kept) }));
                    break;
                }
            }
        }
    }

    /// The melody card needed the payoff where it was: an arrow payoff on the enemy the payoff had just impaled, or a payoff whose
    /// own melody bonus came only from the payoff's tags (one of the two had to lose).
    private static bool NeededPayoff(TurnState s, List<TurnPlay> mine, TurnPlay payoff, TurnPlay m)
    {
        if (ImpalePayoffs.Contains(CardId(m.CardKey)) && m.Target is { } t && payoff.ShotArrow && ReferenceEquals(payoff.Target, t) &&
            !payoff.ArrowIn.Contains(t) && m.ArrowIn.Contains(t))
            return true;
        var id = CardId(m.CardKey);
        if (!BinaryPayoffs.Contains(id) || !m.MelodyBefore.Overlaps(MelodyPayoffs[id])) return false;
        var others = new HashSet<string>();
        foreach (var x in mine.TakeWhile(x => x != m).Where(x => x != payoff))
        {
            others.UnionWith(x.Melody);
            others.UnionWith(Added(s, x));
        }
        return !others.Overlaps(MelodyPayoffs[id]);
    }

    /// A melody power played after the turn's first card of a tag it reacts to, while it was in hand then and the energy allowed it
    /// first (spec "Melody power too late"). Accord violent / Voie de la musique / Berceuse: wrong-order losses; Motif / Luth perdu:
    /// Choix "melodyLate" (power, cards, loss, amount).
    private static void PowersTooLate(TurnState s, TurnPlayer p, TurnResult r)
    {
        var mine = s.Plays.Where(x => x.Owner == p.Actor).ToList();
        bool CouldComeFirst(TurnPlay early, TurnPlay power) =>
            InHandAt(early, power) && early.EnergyBefore >= early.EnergySpent + power.EnergySpent;
        for (var i = 0; i < mine.Count; i++)
        {
            var power = mine[i];
            var id = CardId(power.CardKey);
            if (power.MelodyUnit <= 0) continue;
            if (id == "LULLABY")
            {
                // Played once the three tags were already played: nothing this turn. The card that completed them.
                if (!AllTags.All(power.MelodyBefore.Contains)) continue;
                if (mine.Take(i).LastOrDefault(x => Added(s, x).Count > 0) is { } last && CouldComeFirst(last, power))
                    s.OrderLosses.Add(new OrderLoss(last, power.CardKey, "orderBlock", null, power.MelodyUnit));
                continue;
            }
            if (!FirstTagPowers.TryGetValue(id, out var tags)) continue;
            var firsts = new List<TurnPlay>();
            foreach (var tag in tags)
                if (mine.Take(i).FirstOrDefault(x => Added(s, x).Contains(tag)) is { } first && CouldComeFirst(first, power))
                    firsts.Add(first);
            if (firsts.Count == 0) continue;
            r.Log.Add($"melody late: {power.CardKey} after {string.Join(' ', firsts.Select(f => f.CardKey))}");
            switch (id)
            {
                case "VIOLENT_CHORD":
                    foreach (var e in s.EnemiesLeft.Keys) s.OrderLosses.Add(new OrderLoss(firsts[0], power.CardKey, "order", e, power.MelodyUnit));
                    break;
                case "SONG_PATH":
                    s.OrderLosses.Add(new OrderLoss(firsts[0], power.CardKey, "orderBlock", null, power.MelodyUnit));
                    break;
                default:
                    r.Events.Add(new MissedChance(p.Actor, "melodyLate", new[] { power.CardKey, string.Join('+', firsts.Select(f => f.CardKey).Distinct()),
                        id == "MOTIF" ? "Draws" : "Plays", N((int)Math.Round(power.MelodyUnit * firsts.Count)) }));
                    break;
            }
        }
    }

    /// Récupération héroïque that freed none of the player's arrows (no energy back), when another enemy was at 1 hit from freeing one
    /// ("retrievalTarget") or the player's attacks played after it, already in hand, would have brought one there ("retrievalOrder").
    /// Only when that energy would have paid a useful card left in hand.
    private static void Retrieval(TurnState s, TurnPlayer p, TurnResult r)
    {
        var mine = s.Plays.Where(x => x.Owner == p.Actor).ToList();
        var used = new HashSet<object>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < mine.Count; i++)
        {
            var x = mine[i];
            if (CardId(x.CardKey) != "EPIC_RETRIEVAL" || x.EnergyGiven > 0 || x.CardBonus <= 0 || x.ArrowHitsLeft.Count == 0) continue;
            string kind;
            var moved = "";
            if (x.ArrowHitsLeft.Any(kv => kv.Value == 1 && !ReferenceEquals(kv.Key, x.Target)))
                kind = "retrievalTarget";
            else if (RetrievalLine(mine, i) is { } line)
            {
                kind = "retrievalOrder";
                moved = string.Join('+', line.Select(y => y.CardKey).Distinct());
            }
            else continue;
            if (PaidBySaving(s, p, x.CardBonus, used) is not { } instead) continue;
            used.Add(instead.Ref);
            r.Log.Add($"retrieval: {kind} {moved}");
            r.Events.Add(new MissedChance(p.Actor, kind, new[] { x.CardKey, moved, "Energy", N(x.CardBonus) }));
        }
    }

    /// The cheapest set of the player's attacks played after Récupération héroïque (index i), already in hand, whose hits on an enemy
    /// with one of the player's arrows are exactly the hits it needed minus one (none killing it), within the energy before it.
    private static List<TurnPlay>? RetrievalLine(List<TurnPlay> mine, int i)
    {
        var x = mine[i];
        List<TurnPlay>? best = null;
        var bestEnergy = int.MaxValue;
        foreach (var (enemy, left) in x.ArrowHitsLeft.Where(kv => kv.Value >= 2))
        {
            int HitsOn(TurnPlay y) => y.Hits.Count(h => ReferenceEquals(h.Target, enemy));
            var later = mine.Skip(i + 1).Where(y => y.Kind == CardKind.Attack && InHandAt(x, y) && HitsOn(y) > 0 &&
                                                    !(y.KilledTarget && ReferenceEquals(y.Target, enemy))).Take(12).ToList();
            for (var mask = 1; mask < 1 << later.Count; mask++)
            {
                var line = later.Where((_, k) => (mask & (1 << k)) != 0).ToList();
                var energy = line.Sum(y => y.EnergySpent);
                if (line.Sum(HitsOn) != left - 1 || x.EnergyBefore < x.EnergySpent + energy) continue;
                if (best == null || energy < bestEnergy || (energy == bestEnergy && line.Count < best.Count)) (best, bestEnergy) = (line, energy);
            }
        }
        return best;
    }

    /// Coup d'arc, Bond Ki or Marteau Wah on an enemy with no arrow while another enemy had one (spec "Arrow payoff on the wrong
    /// target"). Choix "payoffTarget" (card, "", loss, amount).
    private static void PayoffTarget(TurnState s, TurnPlayer p, TurnResult r)
    {
        var used = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var x in s.Plays.Where(x => x.Owner == p.Actor))
        {
            var id = CardId(x.CardKey);
            if (!TargetPayoffs.Contains(id) || x.Target is not { } t || x.ArrowIn.Contains(t) || x.KilledTarget || x.ImpaledLater) continue;
            var others = x.ArrowIn.Where(e => !ReferenceEquals(e, t)).ToList();
            if (others.Count == 0) continue;
            string loss;
            int lost;
            switch (id)
            {
                case "BOW_SMACK":
                    if (x.CardBonus <= 0 || PaidBySaving(s, p, x.CardBonus, used) is not { } instead) continue;
                    used.Add(instead.Ref);
                    (loss, lost) = ("Energy", x.CardBonus);
                    break;
                case "KI_HOP":
                    (loss, lost) = ("Draws", Math.Min(x.CardBonus, MaxHandSize - x.HandAtPlay.Count - x.CardsDrawn));
                    break;
                default:
                    // Marteau Wah: ×2 on an impaled enemy, its damage once more (up to that enemy's HP left).
                    var dealt = x.Hits.Where(h => ReferenceEquals(h.Target, t)).Sum(h => h.Raw);
                    (loss, lost) = ("Damage", Math.Min(dealt, others.Max(e => s.EnemiesLeft.GetValueOrDefault(e))));
                    break;
            }
            if (lost < 1) continue;
            r.Events.Add(new MissedChance(p.Actor, "payoffTarget", new[] { x.CardKey, "", loss, N(lost) }));
        }
    }

    /// Tir répété played with 0 energy (it does nothing), when attacks played before it while it was in hand, dealing damage only,
    /// cost energy it would have turned into more damage (spec "Tir répété at 0 energy"). Choix "repeatShotEmpty" (card, attacks,
    /// energy, its damage, theirs).
    private static void RepeatShotEmpty(TurnState s, TurnPlayer p, TurnResult r)
    {
        var mine = s.Plays.Where(x => x.Owner == p.Actor).ToList();
        for (var i = 0; i < mine.Count; i++)
        {
            var x = mine[i];
            if (CardId(x.CardKey) != "REPEAT_SHOT" || !x.CostsX || x.EnergySpent > 0 || x.HitDamage <= 0 || x.Target is not { } t) continue;
            var pure = mine.Take(i).Where(y => y.Kind == CardKind.Attack && y.EnergySpent > 0 && !y.HadNonDamageEffect && !y.GaveBlock &&
                                               !y.PlayedAgain && InHandAt(y, x)).Take(10).ToList();
            (List<TurnPlay> Line, int Energy, int Would, int Dealt)? best = null;
            for (var mask = 1; mask < 1 << pure.Count; mask++)
            {
                var line = pure.Where((_, k) => (mask & (1 << k)) != 0).ToList();
                // It needs an arrow: one ready, or the one a Shot of that line took.
                if (x.ArrowsReady == 0 && !line.Any(y => y.ShotArrow)) continue;
                var energy = line.Sum(y => y.EnergySpent);
                var dealt = line.Sum(y => y.Hits.Sum(h => h.Raw));
                var onTarget = line.Sum(y => y.Hits.Where(h => ReferenceEquals(h.Target, t)).Sum(h => h.Raw));
                var would = Math.Min(energy * x.HitDamage, x.TargetLeftBefore + onTarget);
                if (would - dealt >= 1 && (best == null || would - dealt > best.Value.Would - best.Value.Dealt))
                    best = (line, energy, would, dealt);
            }
            if (best is not { } b) continue;
            r.Log.Add($"repeat shot at 0: {b.Energy} energy, {b.Would} instead of {b.Dealt}");
            r.Events.Add(new MissedChance(p.Actor, "repeatShotEmpty", new[] { x.CardKey, string.Join('+', b.Line.Select(y => y.CardKey).Distinct()),
                N(b.Energy), N(b.Would), N(b.Dealt) }));
        }
    }

    /// Feu de couverture's Shots it lost (1.2.1), and (1.2.2) the Shots played before the card maker that created it while that
    /// maker was in hand, when the energy allowed the maker first and the hand was not full. Maker: shown instead of the card then.
    private static List<(TurnPlay Card, TurnPlay? Maker, List<TurnPlay> Shots, int Lost)> CoveringFireMisses(TurnState s, TurnPlayer p)
    {
        var misses = new List<(TurnPlay, TurnPlay?, List<TurnPlay>, int)>();
        bool IsShot(TurnPlay x) => x.Owner == p.Actor && (x.ShotArrow || x.EmptyShot);
        for (var i = 0; i < s.Plays.Count; i++)
        {
            var cf = s.Plays[i];
            if (cf.Owner != p.Actor || cf.AllyBlockPerShot <= 0) continue;
            var shots = s.Plays.Take(i).Where(x => IsShot(x) && InHandAt(x, cf)).ToList();
            TurnPlay? maker = null;
            var mi = s.Plays.FindIndex(0, i, x => x.Owner == p.Actor && x.Created.Contains(cf.CardRef));
            if (mi >= 0)
            {
                var m = s.Plays[mi];
                var before = s.Plays.Take(mi).Where(x => IsShot(x) && InHandAt(x, m)).ToList();
                if (before.Count > 0 && before[0].EnergyBefore >= m.EnergySpent + before[0].EnergySpent && before[0].HandAtPlay.Count + 1 < MaxHandSize)
                {
                    maker = m;
                    shots = before.Concat(shots).ToList();
                }
            }
            if (shots.Count == 0) continue;
            var lost = s.Players.Where(a => a.Actor != p.Actor && a.Alive)
                .Sum(a => Math.Min(shots.Count * cf.AllyBlockPerShot, Math.Max(0, a.Incoming - a.Block)));
            if (lost >= 1) misses.Add((cf, maker, shots, lost));
        }
        return misses;
    }
}
