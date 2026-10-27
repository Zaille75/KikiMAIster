using KikiMAIster.Core;

namespace KikiMAIster.Core.Tests;

public class DebuffEffectTests : TestBase
{
    private void Vulnerable(Actor dealer, double extra) =>
        Engine.Handle(new DebuffEffect(Enemy, "VULNERABLE_POWER", dealer, extra, Offense: true, Fifo: true));

    [Fact]
    public void Vulnerable_TurnsAreCreditedFirstInFirstOut()
    {
        Power(A, Enemy, "VULNERABLE_POWER", 2);   // turns 1-2: A
        Power(B, Enemy, "VULNERABLE_POWER", 2);   // turns 3-4: B
        Vulnerable(C, 3);                         // turn 1: C hits, A enabled it
        Power(Actor.None, Enemy, "VULNERABLE_POWER", -1);
        Power(Actor.None, Enemy, "VULNERABLE_POWER", -1);
        Vulnerable(A, 4);                         // turn 3: B enabled A's hit
        Vulnerable(B, 2);                         // turn 3: B's own hit

        Assert.Equal(3, Combat(IdA).DamageEnabledAllies);
        Assert.Equal(4, Combat(IdB).DamageEnabledAllies);
        Assert.Equal(2, Combat(IdB).DamageEnabledSelf);
        Assert.Equal(6, Combat(IdB).DebuffEffectBySource["VULNERABLE_POWER"]);
    }

    [Fact]
    public void Weak_PreventsDamage_ForTheTarget()
    {
        Power(A, Enemy, "WEAK_POWER", 1);
        Engine.Handle(new DebuffEffect(Enemy, "WEAK_POWER", B, 3, Offense: false, Fifo: true));
        Engine.Handle(new DebuffEffect(Enemy, "WEAK_POWER", A, 2, Offense: false, Fifo: true));

        Assert.Equal(3, Combat(IdA).PreventedByDebuffsAllies);
        Assert.Equal(2, Combat(IdA).PreventedByDebuffsSelf);
        Assert.Equal(3, Combat(IdA).SavedForAllies);
        Assert.Equal(5, Combat(IdA).PreventedTotal);
    }

    [Fact]
    public void StrengthDown_SharedByStacks_AcrossOrigins()
    {
        Power(A, Enemy, "STRENGTH_POWER~DOWN", 3);
        Power(B, Enemy, "STRENGTH_POWER~DOWN@ENFEEBLING_TOUCH_POWER", 1);
        Engine.Handle(new DebuffEffect(Enemy, "STRENGTH_POWER~DOWN", C, 4, Offense: false, Fifo: false));

        Assert.Equal(3, Combat(IdA).PreventedByDebuffsAllies, 6);
        Assert.Equal(1, Combat(IdB).PreventedByDebuffsAllies, 6);
    }

    [Fact]
    public void EnemyOwnDebuff_CreditsNobody()
    {
        Power(Enemy, Enemy, "VULNERABLE_POWER", 1);
        Vulnerable(A, 5);
        Assert.Equal(0, Combat(IdA).DamageEnabledSelf);
    }
}

public class LethalTests
{
    private static Lethal.Attack Single(string id, int cost, params int[] dmg) => new(id, cost, false, dmg);

    [Fact]
    public void FindsCheapestCombination()
    {
        var hand = new[] { Single("A", 1, 6, 6), Single("B", 1, 6, 6), Single("C", 2, 13, 13), new Lethal.Attack("D", 1, true, new[] { 4, 4 }) };
        var found = Lethal.Find(hand, 3, new[] { 10, 2 });
        Assert.NotNull(found);
        Assert.True(found!.Sum(a => a.Cost) <= 3);
    }

    [Fact]
    public void NoLethal_WhenEnergyTooLow()
    {
        var hand = new[] { Single("A", 2, 10), Single("B", 2, 10) };
        Assert.Null(Lethal.Find(hand, 3, new[] { 15 }));
        Assert.NotNull(Lethal.Find(hand, 4, new[] { 15 }));
    }

    [Fact]
    public void SingleTargetAttacks_SpreadOverEnemies()
    {
        var hand = new[] { Single("A", 1, 6, 6), Single("B", 1, 6, 6) };
        Assert.NotNull(Lethal.Find(hand, 2, new[] { 5, 6 }));
        Assert.Null(Lethal.Find(hand, 2, new[] { 5, 7 }));
    }
}
