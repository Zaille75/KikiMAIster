using KikiMAIster.Core;

namespace KikiMAIster.Core.Tests;

/// Examples and edge cases from docs/specs/01-metrics.md §F.
public class BlockAttributionTests : TestBase
{
    [Fact]
    public void Example1_HelpOnTopOfExistingBlock_IsUseless()
    {
        Block(B, B, 5);
        Block(B, A, 5);
        EnemyHits(B, blocked: 5, hp: 0);
        Engine.Handle(new BlockCleared(B));

        Assert.Equal(5, Combat(IdB).PreventedOwnBlock);
        Assert.Equal(0, Combat(IdA).PreventedForAllies);
        Assert.Equal(5, Combat(IdA).UnusedGivenToAllies);
        Assert.Equal(0, Combat(IdB).UnusedOwn);
    }

    [Fact]
    public void Example2_HelpBeyondExistingBlock_IsPrevented()
    {
        Block(B, B, 5);
        Block(B, A, 5);
        EnemyHits(B, blocked: 8, hp: 0);
        Engine.Handle(new BlockCleared(B));

        Assert.Equal(5, Combat(IdB).PreventedOwnBlock);
        Assert.Equal(3, Combat(IdA).PreventedForAllies);
        Assert.Equal(2, Combat(IdA).UnusedGivenToAllies);
    }

    [Fact]
    public void Example3_PartialProtection()
    {
        Block(B, A, 6);
        EnemyHits(B, blocked: 6, hp: 4);

        Assert.Equal(6, Combat(IdA).PreventedForAllies);
        Assert.Equal(4, Combat(IdB).HpLostFromEnemies);
        Assert.Equal(6, Combat(IdB).DamageBlocked);
    }

    [Fact]
    public void Example4_OldestLayerGetsTheCredit()
    {
        Block(B, A, 5);
        Block(B, B, 5);
        EnemyHits(B, blocked: 5, hp: 0);
        Engine.Handle(new BlockCleared(B));

        Assert.Equal(5, Combat(IdA).PreventedForAllies);
        Assert.Equal(5, Combat(IdB).UnusedOwn);
        Assert.Equal(0, Combat(IdB).PreventedOwnBlock);
    }

    [Fact]
    public void Example5_FirstGiverGetsTheCredit()
    {
        Block(B, A, 5);
        Block(B, C, 5);
        EnemyHits(B, blocked: 5, hp: 0);
        Engine.Handle(new BlockCleared(B));

        Assert.Equal(5, Combat(IdA).PreventedForAllies);
        Assert.Equal(5, Combat(IdC).UnusedGivenToAllies);
    }

    [Fact]
    public void Example6_MultiHit()
    {
        Block(B, B, 5);
        Block(B, A, 5);
        EnemyHits(B, blocked: 4, hp: 0);
        EnemyHits(B, blocked: 4, hp: 0);
        EnemyHits(B, blocked: 2, hp: 2);

        Assert.Equal(5, Combat(IdB).PreventedOwnBlock);
        Assert.Equal(5, Combat(IdA).PreventedForAllies);
        Assert.Equal(2, Combat(IdB).HpLostFromEnemies);
    }

    [Fact]
    public void Example7_TeamBlock_OneLayerPerRecipient()
    {
        Block(A, A, 4);
        Block(B, A, 4);
        Block(C, A, 4);
        EnemyHits(B, blocked: 4, hp: 0);
        Engine.Handle(new CombatEnded(true));

        Assert.Equal(4, Combat(IdA).BlockGainedSelf);
        Assert.Equal(8, Combat(IdA).BlockGivenToAllies);
        Assert.Equal(4, Combat(IdA).PreventedForAllies);
        Assert.Equal(4, Combat(IdA).UnusedGivenToAllies);   // the 4 given to C
        Assert.Equal(4, Combat(IdA).UnusedOwn);   // its own 4
    }

    [Fact]
    public void Example8_LaterOwnBlockIsWasted()
    {
        Block(A, A, 5);
        Block(A, B, 5);
        Block(A, A, 5);
        EnemyHits(A, blocked: 10, hp: 0);
        Engine.Handle(new BlockCleared(A));

        Assert.Equal(5, Combat(IdA).PreventedOwnBlock);
        Assert.Equal(5, Combat(IdB).PreventedForAllies);
        Assert.Equal(5, Combat(IdA).UnusedOwn);
        Assert.Equal(0, Combat(IdB).UnusedGivenToAllies);
    }

    [Fact]
    public void BlockLeftAtCombatEnd_IsWasted()
    {
        Block(A, A, 7);
        Block(A, B, 3);
        Engine.Handle(new CombatEnded(true));

        Assert.Equal(7, Combat(IdA).UnusedOwn);
        Assert.Equal(3, Combat(IdB).UnusedGivenToAllies);
    }

    [Fact]
    public void BlockKeptAcrossTurns_KeepsGiverAndOrder()
    {
        Block(B, A, 5);
        Engine.Handle(new RoundStarted(2)); // no BlockCleared: block retained
        Block(B, B, 5);
        EnemyHits(B, blocked: 5, hp: 0);

        Assert.Equal(5, Combat(IdA).PreventedForAllies);
        Assert.Equal(0, Combat(IdB).PreventedOwnBlock);
    }

    [Fact]
    public void BlockRemovedByEffect_IsWasted_AndSpentByCard_IsTrackedApart()
    {
        Block(A, A, 10);
        Engine.Handle(new BlockRemoved(A, 4, SpentByCard: false));
        Engine.Handle(new BlockRemoved(A, 3, SpentByCard: true));
        EnemyHits(A, blocked: 3, hp: 0);

        Assert.Equal(4, Combat(IdA).UnusedOwn);
        Assert.Equal(3, Combat(IdA).BlockSpent);
        Assert.Equal(3, Combat(IdA).PreventedOwnBlock);
    }

    [Fact]
    public void FriendlyFireAbsorbedByBlock_CountsAsPrevented()
    {
        Block(B, A, 5);
        Hit(C, B, blocked: 5, hp: 1);

        Assert.Equal(5, Combat(IdA).PreventedForAllies);
        Assert.Equal(1, Combat(IdB).HpLostFromAllies);
        Assert.Equal(1, Combat(IdC).FriendlyDamage);
    }

    [Fact]
    public void BlockWithoutKnownLayer_IsUnattributed()
    {
        EnemyHits(A, blocked: 6, hp: 0);
        Assert.Equal(6, Engine.UnattributedBlock);
        Assert.Equal(0, Combat(IdA).PreventedOwnBlock);
    }

    [Fact]
    public void WastedBlockFromPreviousRound_NotInCurrentTurnScope()
    {
        Block(A, A, 5);
        Engine.Handle(new RoundStarted(2));
        Engine.Handle(new BlockCleared(A));

        Assert.Equal(5, Combat(IdA).UnusedOwn);
        Assert.Equal(0, Engine.Get(Scope.Turn, IdA).UnusedOwn);
    }

    [Fact]
    public void BlockObserved_AfterTurnStart_ExcessIsWasted_OldestFirst()
    {
        Block(A, B, 4);
        Block(A, A, 6);
        Engine.Handle(new BlockObserved(A, 0));      // normal clear
        Assert.Equal(4, Combat(IdB).UnusedGivenToAllies);
        Assert.Equal(6, Combat(IdA).UnusedOwn);

        Block(A, A, 10);
        Engine.Handle(new BlockObserved(A, 7));      // an effect kept 7 block
        Assert.Equal(9, Combat(IdA).UnusedOwn);
        EnemyHits(A, blocked: 7, hp: 0);
        Assert.Equal(7, Combat(IdA).PreventedOwnBlock);
    }

    // ---------- Wasted (had an alternative) vs useless (no alternative) ----------

    [Fact]
    public void UnusedBlock_WithAlternative_IsWasted_WithoutIsUseless()
    {
        Engine.Handle(new BlockGained(A, A, 5, Source.Card("DEFEND"), PlayId: 1));
        Engine.Handle(new BlockGained(A, A, 4, Source.Card("DEFEND"), PlayId: 2));
        Engine.Handle(new BlockGained(A, A, 3, Source.Power("PLATED_ARMOR_POWER")));
        Engine.Handle(new BlockPlayResolved(1, true));
        Engine.Handle(new BlockPlayResolved(2, false));
        Engine.Handle(new RoundStarted(2));
        Engine.Handle(new BlockObserved(A, 0));

        Assert.Equal(5, Combat(IdA).WastedBlock);
        Assert.Equal(7, Combat(IdA).UselessBlock);
        Assert.Equal(12, Combat(IdA).UnusedOwn);
    }

    [Fact]
    public void UnusedBlockGivenToAlly_SplitTheSameWay_AndTrackedAsGivenToAllies()
    {
        Engine.Handle(new BlockGained(B, A, 6, Source.Card("SHIELD_ALLY"), PlayId: 7));
        Engine.Handle(new BlockPlayResolved(7, true));
        Engine.Handle(new CombatEnded(true));

        Assert.Equal(6, Combat(IdA).WastedBlock);
        Assert.Equal(6, Combat(IdA).UnusedGivenToAllies);
        Assert.Equal(0, Combat(IdA).UnusedOwn);
    }

    [Fact]
    public void PreviousTurn_HoldsTheFinishedRound_WithItsWastedBlockAndReason()
    {
        var choice = new BlockChoice("Card:DEFEND", "Card:STRIKE", new[] { "Card:STRIKE" }, 5, 0);
        Engine.Handle(new BlockGained(A, A, 5, Source.Card("DEFEND"), PlayId: 3));
        Hit(A, Enemy, 0, 7);
        Engine.Handle(new BlockPlayResolved(3, true, choice));
        Engine.Handle(new RoundStarted(2));
        Engine.Handle(new BlockObserved(A, 0));

        var prev = Engine.Get(Scope.PreviousTurn, IdA);
        Assert.Equal(7, prev.DamageDealt);
        Assert.Equal(5, prev.WastedBlock);
        var why = Assert.Single(prev.WastedPlays);
        Assert.Equal(5, why.Amount);
        Assert.Equal("Card:STRIKE", why.Choice.Alternative);
        Assert.Equal(0, Engine.Get(Scope.Turn, IdA).DamageDealt);

        // New events go to the current turn only.
        Hit(A, Enemy, 0, 3);
        Assert.Equal(7, Engine.Get(Scope.PreviousTurn, IdA).DamageDealt);
    }

    [Fact]
    public void Gache_CountsMisplays_OncePerBlockCardPlay()
    {
        var choice = new BlockChoice("Card:DEFEND", "Card:STRIKE", new[] { "Card:STRIKE" }, 10, 0);
        Engine.Handle(new BlockGained(A, A, 5, Source.Card("DEFEND"), PlayId: 4));
        Engine.Handle(new BlockGained(A, A, 5, Source.Card("DEFEND"), PlayId: 5));
        Engine.Handle(new BlockPlayResolved(4, true, choice));
        Engine.Handle(new BlockPlayResolved(5, true, choice));
        Engine.Handle(new RoundStarted(2));
        Engine.Handle(new BlockObserved(A, 3));   // part of play 5 kept (Barricade-like), then lost at combat end
        Engine.Handle(new CombatEnded(true));
        Engine.Handle(new MissedChance(A, "growth", new[] { "Card:RAMPAGE" }));

        Assert.Equal(2, Combat(IdA).WastedPlayCount);
        Assert.Equal(10, Combat(IdA).WastedBlock);
        Assert.Equal(3, Engine.Get(Scope.Run, IdA).MissedWasted);
    }
}
