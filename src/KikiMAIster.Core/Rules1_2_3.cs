namespace KikiMAIster.Core;

/// The 1.2.3 rules (Zaille75's feedback of 2026-11-17, spec 01-metrics rows "Played again with no arrow" → "Melody block card").
public static partial class TurnReview
{
    private const string SlipperyKey = "Power:SLIPPERY_POWER";

    /// A Shot played again (Duplicateur, Forme résonnante, Vers Ki…) whose copy found no arrow ready: Choix, "emptyReplay".
    private static void EmptyReplays(TurnState s, TurnPlayer p, TurnResult r)
    {
        foreach (var x in s.Plays.Where(x => x.Owner == p.Actor && x.EmptyReplay))
        {
            r.Log.Add($"empty replay: {x.CardKey}");
            r.Events.Add(new MissedChance(p.Actor, "emptyReplay", new[] { x.CardKey }));
        }
    }

    /// Cards created into a full hand by a card that was already in hand at an earlier play, and that played just before it would
    /// have kept them without changing what was played (spec "Created into a full hand, could come earlier"): Choix, "makeEarlier"
    /// (the card, the cards lost, the play it could have come before).
    private static void MakeEarlier(TurnState s, TurnPlayer p, TurnResult r)
    {
        var mine = s.Plays.Where(x => x.Owner == p.Actor).ToList();
        for (var m = 0; m < mine.Count; m++)
        {
            var maker = mine[m];
            if (maker.LostToFullHand <= 0 || maker.CreatedToHand <= 0 || r.FullHandExplained.Contains(maker)) continue;
            // The latest earlier play it could have come before (closest to where it was played).
            for (var i = m - 1; i >= 0; i--)
            {
                var at = mine[i];
                if (!InHandAt(at, maker)) break;
                var between = mine.Skip(i).Take(m - i).ToList();
                var fits = at.HandAtPlay.Count + maker.CreatedToHand <= MaxHandSize;
                var sameCards = between.Skip(1).All(y => InHandAt(at, y));
                var affordable = between.All(y => y.EnergyBefore - maker.EnergySpent >= y.EnergySpent);
                if (!fits || !sameCards || !affordable) continue;
                r.Log.Add($"make earlier: {maker.CardKey} lost {maker.LostToFullHand}, room before {at.CardKey}");
                r.Events.Add(new MissedChance(p.Actor, "makeEarlier", new[] { maker.CardKey, N(maker.LostToFullHand), at.CardKey }));
                break;
            }
        }
    }

    /// Visée spent by a Shot on a hit a cap stopped (spec "Visée into a cap"): Choix, "aimCapped" (the Shot, the cap, damage lost).
    /// Glissant only when it went away this turn and a later Shot (already in hand) hit that enemy without Visée.
    private static void AimCapped(TurnState s, TurnPlayer p, TurnResult r)
    {
        var mine = s.Plays.Where(x => x.Owner == p.Actor).ToList();
        for (var i = 0; i < mine.Count; i++)
        {
            var x = mine[i];
            if (!x.ShotArrow || x.AimAtStart <= 0 || !x.Capped) continue;
            var other = x.CapKeys.FirstOrDefault(k => k != SlipperyKey);
            int lost;
            string cap;
            if (other != null)
            {
                lost = (int)Math.Round(Math.Min(x.AimAtStart * x.AimFactor, x.CapStopped));
                cap = other;
            }
            else
            {
                var enemy = x.Target is { } t && x.SlipperyOn.Contains(t) ? t : x.SlipperyOn.FirstOrDefault();
                if (enemy == null || s.EnemySlippery.GetValueOrDefault(enemy) > 0 || !s.EnemiesLeft.TryGetValue(enemy, out var left)) continue;
                var later = mine.Skip(i + 1).FirstOrDefault(y => y.ShotArrow && y.AimAtStart == 0 && ReferenceEquals(y.Target, enemy) &&
                                                                 !y.SlipperyOn.Contains(enemy) && InHandAt(x, y));
                if (later == null) continue;
                lost = (int)Math.Round(Math.Min(x.AimAtStart * later.AimFactor, left));
                cap = SlipperyKey;
            }
            if (lost < 1) continue;
            r.Log.Add($"aim capped: {x.CardKey} into {cap}, {lost} lost");
            r.Events.Add(new MissedChance(p.Actor, "aimCapped", new[] { x.CardKey, cap, N(lost) }));
        }
    }

    /// An empty Shot / a card on a target without an arrow is a mistake only when it cost energy that would have paid one more
    /// useful card left in hand (1.2.3, Zaille75).
    private static bool EnergyMissed(TurnState s, TurnPlayer p, TurnPlay x)
    {
        if (x.EnergySpent <= 0) return false;
        var costs = p.Hand.Where(c => UsefulLeft(s, p, c)).Select(c => c.Cost).OrderBy(c => c).ToList();
        int Count(int budget)
        {
            var n = 0;
            foreach (var c in costs)
            {
                if (c > budget) break;
                budget -= c;
                n++;
            }
            return n;
        }
        return Count(p.Energy + x.EnergySpent) > Count(p.Energy);
    }

    /// A block card whose melody tag a later card of the same player counted, or that completed what a melody relic or power needs
    /// (spec "Melody block card"): it did something besides its block.
    public static bool MelodyCounted(TurnState s, TurnPlay x)
    {
        if (x.Melody.Count == 0 && x.AutoMelody.Count == 0) return false;
        var added = Added(s, x);
        if (added.Count == 0) return false;
        foreach (var y in s.Plays.SkipWhile(y => y != x).Skip(1).Where(y => y.Owner == x.Owner))
        {
            if (!MelodyPayoffs.TryGetValue(CardId(y.CardKey), out var counted) || !counted.Any(added.Contains)) continue;
            // A yes / no payoff: x gave it only if none of its tags was played before x.
            if (!BinaryPayoffs.Contains(CardId(y.CardKey)) || !counted.Any(x.MelodyBefore.Contains)) return true;
        }
        if (s.Player(x.Owner) is not { } p) return false;
        return p.Relics.Any(rl => rl.Trigger == RelicTrigger.AllMelodies) && p.MelodyTags.Count == AllTags.Length ||
               p.Relics.Any(rl => rl.Trigger == RelicTrigger.KiMelody) && added.Contains("KI");
    }
}
