namespace KikiMAIster.Core;

/// An enemy died in the players' turn while a playable Compostage was in a hand (1.2.5). KillKey: the card or potion that
/// killed it. Energy: what Compostage would have given the whole team.
public sealed record KillBeforeCompost(Actor Killer, string KillKey, string CompostKey, Actor Holder, int Energy);

/// Rules of 1.2.5 (feedback of 2026-11-22, spec 01-metrics).
public static partial class TurnReview
{
    private const string TopKey = "Relic:UNCEASING_TOP";

    /// The turn ended with the fight still on: each kill made before a Compostage in hand lost its energy. For the killer.
    private static void CompostLate(TurnState s, TurnResult r)
    {
        foreach (var k in s.KillsBeforeCompost)
            r.Events.Add(new MissedChance(k.Killer, "compostLate", new[] { k.KillKey, Owned(k.CompostKey, k.Holder, k.Killer), N(k.Energy) }));
    }

    /// L'Insensé played with nothing else in hand: it auto-played nothing.
    private static void FoolEmptyHand(TurnState s, TurnPlayer p, TurnResult r)
    {
        foreach (var x in s.Plays.Where(x => x.Owner == p.Actor && x.NoCardToAutoPlay))
            r.Events.Add(new MissedChance(p.Actor, "foolEmptyHand", new[] { x.CardKey }));
    }

    /// The cards the energy left could have paid, cheapest first (an X card last, for 1 energy). Free cards have their own miss;
    /// no more Shots than arrows ready; no block card once the block covers the attack.
    private static List<TurnCard> CardsEnergyPaid(TurnPlayer p, int energy, Func<TurnCard, bool> helps)
    {
        var paid = new List<TurnCard>();
        var budget = energy;
        var shots = 0;
        var block = p.Block;
        foreach (var c in p.Hand.Where(c => helps(c) && (c.CostsX || c.Cost > 0)).OrderBy(c => c.CostsX).ThenBy(c => c.Cost))
        {
            var cost = c.CostsX ? 1 : c.Cost;
            if (cost > budget) break;
            if (c.Shot && shots >= p.ArrowsReady) continue;
            if (c.GainsBlock && c.Kind == CardKind.Skill && block >= p.Incoming) continue;
            budget -= cost;
            if (c.Shot) shots++;
            if (c.GainsBlock) block += c.Block;
            paid.Add(c);
        }
        return paid;
    }
}
