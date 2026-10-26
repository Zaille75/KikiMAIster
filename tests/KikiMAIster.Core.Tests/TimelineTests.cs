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
}
