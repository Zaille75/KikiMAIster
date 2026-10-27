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
    public void RoundTripKeepsFinishedCombats()
    {
        Hit(A, Enemy, 0, 7, 0, false, "STRIKE_IRONCLAD");
        Engine.Handle(new RoundStarted(2));
        Hit(A, Enemy, 0, 5, 0, true, "BASH");
        Engine.Handle(new CombatEnded(true));
        Engine.Handle(new CombatStarted(new[] { new PlayerInfo(IdA, "A", "IRONCLAD") }, "Jaw Worm"));
        Engine.Handle(new RoundStarted(1));
        EnemyHits(A, 0, 4);
        Engine.Handle(new CombatEnded(false));

        var snap = RunSnapshot.Read(RunSnapshot.Write("seed|1", 9, Engine))!;
        var restored = new StatsEngine();
        restored.Handle(new RunStarted());
        restored.RestoreRun(snap.Players, snap.Stats, snap.Combats);

        Assert.Equal(2, restored.Combats.Count);
        var first = restored.Combats[0];
        Assert.Equal(1, first.Number);
        Assert.True(first.Victory);
        Assert.Equal(new[] { 0, 1, 2 }, first.Rounds.Select(r => r.Round));
        Assert.Equal(12, first.Rounds[^1].Players[IdA].Damage);
        Assert.Equal(12, first.Stats[IdA].DamageDealt);
        Assert.Equal(5, first.Stats[IdA].DamageBySource["Card:BASH"]);
        var second = restored.Combats[1];
        Assert.Equal("Jaw Worm", second.Name);
        Assert.False(second.Victory);
        Assert.Equal(4, second.Stats[IdA].HpLost);

        // The next fight is number 3.
        restored.Handle(new CombatStarted(snap.Players));
        Assert.Equal(3, restored.CurrentCombat!.Number);
    }

    [Fact]
    public void GarbageIsRejected() => Assert.Null(RunSnapshot.Read("hello\nworld"));
}
