using KikiMAIster.Core;

namespace KikiMAIster.Core.Tests;

/// v0.18.5: energy gained during fights, and the damage debuffs avoided kept apart from the damage they added.
public class Feedback0_18_5Tests : TestBase
{
    [Fact]
    public void EnergyGained_AddsUp_BySource()
    {
        Engine.Handle(new EnergyGained(A, "Card:BORROWED_TIME", 2));
        Engine.Handle(new EnergyGained(A, "Orb:PLASMA_ORB", 1));
        Engine.Handle(new EnergyGained(A, "Card:BORROWED_TIME", 2));
        Engine.Handle(new EnergyGained(Enemy, "Unknown", 5));
        var s = Engine.Get(Scope.Run, IdA);
        Assert.Equal(5, s.EnergyGained);
        Assert.Equal(4, s.EnergyGainedBySource["Card:BORROWED_TIME"]);
        Assert.Equal(1, s.EnergyGainedBySource["Orb:PLASMA_ORB"]);
    }

    [Fact]
    public void DebuffAvoided_OnlyTheDefensiveEffects()
    {
        Power(A, Enemy, "WEAK_POWER", 1);
        Power(A, Enemy, "VULNERABLE_POWER", 1);
        Engine.Handle(new DebuffEffect(Enemy, "WEAK_POWER", A, 3, Offense: false, Credit: DebuffCredit.CurrentTurn));
        EnemyHits(A, 0, 6);
        Engine.Handle(new DebuffEffect(Enemy, "VULNERABLE_POWER", A, 4, Offense: true, Credit: DebuffCredit.CurrentTurn));
        var s = Combat(IdA);
        Assert.Equal(3, s.DebuffAvoidedBySource["WEAK_POWER"]);
        Assert.False(s.DebuffAvoidedBySource.ContainsKey("VULNERABLE_POWER"));
        Assert.Equal(4, s.DebuffEffectBySource["VULNERABLE_POWER"]);
    }
}
