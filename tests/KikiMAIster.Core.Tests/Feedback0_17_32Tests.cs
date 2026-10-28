using KikiMAIster.Core;
using Xunit;

namespace KikiMAIster.Core.Tests;

/// v0.17.32: block from a play that had an alternative absorbs last (Givre orbs), rest before the boss in Choix.
public class Feedback0_17_32Tests : TestBase
{
    [Fact]
    public void FrostOrbs_AbsorbBeforeTheReplaceableDefend()
    {
        // Défense (no alternative), Défense (Frappe in hand), a Givre evoke, then 2 Givre passives: 19 block for 9 + 5.
        Engine.Handle(new BlockGained(A, A, 5, Source.Card("DEFEND"), PlayId: 2));
        Engine.Handle(new BlockGained(A, A, 5, Source.Card("DEFEND"), PlayId: 3));
        Engine.Handle(new BlockGained(A, A, 5, new Source(SourceKind.Orb, "FROST_ORB")));
        Engine.Handle(new BlockPlayResolved(3, true, new BlockChoice("Card:DEFEND", "Card:STRIKE", new[] { "Card:STRIKE" }, 15, 14)));
        Engine.Handle(new BlockPlayResolved(2, false));
        Engine.Handle(new BlockGained(A, A, 2, new Source(SourceKind.Orb, "FROST_ORB")));
        Engine.Handle(new BlockGained(A, A, 2, new Source(SourceKind.Orb, "FROST_ORB")));
        EnemyHits(A, 9, 0);
        EnemyHits(A, 5, 0);
        Engine.Handle(new RoundStarted(2));
        Engine.Handle(new BlockObserved(A, 0));
        Assert.Equal(5, Combat(IdA).WastedBlock);
        Assert.Equal(0, Combat(IdA).UselessBlock);
        Assert.Equal(1, Combat(IdA).WastedPlayCount);
    }

    [Fact]
    public void Ledger_LastLayersUsedOnlyWhenTheOthersAreEmpty()
    {
        var ledger = new BlockLedger();
        ledger.Add(1, 5, 1, playId: 1);
        ledger.Add(1, 5, 1, playId: 2);
        var taken = ledger.Consume(7, l => l.PlayId == 1);
        Assert.Equal(new[] { (2, 5.0), (1, 2.0) }, taken.Select(t => (t.Layer.PlayId, t.Taken)).ToArray());
        Assert.Equal(3, ledger.Total);
    }

    [Fact]
    public void RestBeforeTheBoss_CountsInChoix()
    {
        Engine.Handle(new MissedChance(A, "restBoss", new[] { "RestSite:HEAL", "40", "12" }));
        Assert.Equal(1, Combat(IdA).MissedChoices);
        Assert.Equal(1, Combat(IdA).MissedWasted + Combat(IdA).MissedChoices);
    }
}
