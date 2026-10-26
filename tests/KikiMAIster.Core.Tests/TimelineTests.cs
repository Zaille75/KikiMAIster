using KikiMAIster.Core;

namespace KikiMAIster.Core.Tests;

public class TimelineTests : TestBase
{
    [Fact]
    public void CumulativeTotalsPerRound()
    {
        Hit(A, Enemy, 0, 6);
        Hit(B, Enemy, 0, 4);
        Engine.Handle(new RoundStarted(2));
        Hit(A, Enemy, 0, 5);
        Engine.Handle(new RoundStarted(3));
        Hit(B, Enemy, 0, 10, killed: true);
        Engine.Handle(new CombatEnded(true));

        var combat = Engine.LastCombat!;
        Assert.True(combat.Victory);
        Assert.Equal(new[] { 0, 1, 2, 3 }, combat.Rounds.Select(r => r.Round));
        Assert.Equal(new[] { 0.0, 6, 11, 11 }, combat.Rounds.Select(r => r.Players[IdA].Damage));
        Assert.Equal(new[] { 0.0, 4, 4, 14 }, combat.Rounds.Select(r => r.Players[IdB].Damage));
    }

    [Fact]
    public void CombatEndingRightAfterRoundStart_DoesNotDuplicateTheRound()
    {
        Engine.Handle(new RoundStarted(2));
        Engine.Handle(new CombatEnded(false));
        Assert.Equal(new[] { 0, 1, 2 }, Engine.LastCombat!.Rounds.Select(r => r.Round));
    }

    [Fact]
    public void CurrentCombat_HasLivePoint_AndFinishedCombatsAreKeptForTheRun()
    {
        Hit(A, Enemy, 0, 6);
        var live = Engine.CurrentCombat!;
        Assert.True(live.InProgress);
        Assert.Equal(new[] { 0, 1 }, live.Rounds.Select(r => r.Round));
        Assert.Equal(6, live.Rounds[^1].Players[IdA].Damage);

        Engine.Handle(new CombatEnded(true));
        Assert.Null(Engine.CurrentCombat);
        Engine.Handle(new CombatStarted(new[] { new PlayerInfo(IdA, "A", "IRONCLAD") }, "Jaw Worm"));
        Engine.Handle(new RoundStarted(1));
        Engine.Handle(new CombatEnded(false));

        Assert.Equal(new[] { 1, 2 }, Engine.Combats.Select(c => c.Number));
        Assert.Equal("Jaw Worm", Engine.Combats[1].Name);
        Engine.Handle(new RunStarted());
        Assert.Empty(Engine.Combats);
    }
}
