using KikiMAIster.Core;

namespace KikiMAIster.Core.Tests;

public class RunSnapshotTests : TestBase
{
    [Fact]
    public void RoundTripKeepsRunTotals()
    {
        Hit(A, Enemy, 3, 10, 2, true, "STRIKE_IRONCLAD");
        Power(B, Enemy, "POISON_POWER", 6);
        Block(B, B, 5);
        EnemyHits(B, 5, 2);
        Engine.Handle(new CombatEnded(true));

        var text = RunSnapshot.Write("seed|1", 7, Engine);
        var snap = RunSnapshot.Read(text)!;
        Assert.Equal("seed|1", snap.RunKey);
        Assert.Equal(7, snap.Floor);

        var restored = new StatsEngine();
        restored.Handle(new RunStarted());
        restored.RestoreRun(snap.Players, snap.Stats);

        var a = restored.Get(Scope.Run, IdA);
        Assert.Equal(10, a.DamageDealt);
        Assert.Equal(2, a.Overkill);
        Assert.Equal(1, a.Kills);
        Assert.Equal(10, a.DamageBySource["Card:STRIKE_IRONCLAD"]);
        var b = restored.Get(Scope.Run, IdB);
        Assert.Equal(6, b.DebuffsApplied["POISON_POWER"].Stacks);
        Assert.Equal(5, b.PreventedOwnBlock);
        Assert.Equal("B", restored.Players[IdB].Name);

        // A new combat keeps adding to the restored run totals.
        restored.Handle(new CombatStarted(snap.Players));
        restored.Handle(new RoundStarted(1));
        restored.Handle(new DamageDealt(A, Enemy, 0, 4, 0, false, Source.Card("STRIKE_IRONCLAD")));
        Assert.Equal(14, restored.Get(Scope.Run, IdA).DamageDealt);
    }

    [Fact]
    public void GarbageIsRejected() => Assert.Null(RunSnapshot.Read("hello\nworld"));
}
