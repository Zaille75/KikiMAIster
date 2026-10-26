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
        Assert.Equal(5, Combat(IdA).UselessHelp);
        Assert.Equal(0, Combat(IdB).WastedBlock);
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
        Assert.Equal(2, Combat(IdA).UselessHelp);
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
        Assert.Equal(5, Combat(IdB).WastedBlock);
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
        Assert.Equal(5, Combat(IdC).UselessHelp);
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
        Assert.Equal(4, Combat(IdA).UselessHelp);   // the 4 given to C
        Assert.Equal(4, Combat(IdA).WastedBlock);   // its own 4
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
        Assert.Equal(5, Combat(IdA).WastedBlock);
        Assert.Equal(0, Combat(IdB).UselessHelp);
    }

    [Fact]
    public void BlockLeftAtCombatEnd_IsWasted()
    {
        Block(A, A, 7);
        Block(A, B, 3);
        Engine.Handle(new CombatEnded(true));

        Assert.Equal(7, Combat(IdA).WastedBlock);
        Assert.Equal(3, Combat(IdB).UselessHelp);
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

        Assert.Equal(4, Combat(IdA).WastedBlock);
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

        Assert.Equal(5, Combat(IdA).WastedBlock);
        Assert.Equal(0, Engine.Get(Scope.Turn, IdA).WastedBlock);
    }

    [Fact]
    public void BlockObserved_AfterTurnStart_ExcessIsWasted_OldestFirst()
    {
        Block(A, B, 4);
        Block(A, A, 6);
        Engine.Handle(new BlockObserved(A, 0));      // normal clear
        Assert.Equal(4, Combat(IdB).UselessHelp);
        Assert.Equal(6, Combat(IdA).WastedBlock);

        Block(A, A, 10);
        Engine.Handle(new BlockObserved(A, 7));      // an effect kept 7 block
        Assert.Equal(9, Combat(IdA).WastedBlock);
        EnemyHits(A, blocked: 7, hp: 0);
        Assert.Equal(7, Combat(IdA).PreventedOwnBlock);
    }
}
