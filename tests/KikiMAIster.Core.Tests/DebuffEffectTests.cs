using KikiMAIster.Core;

namespace KikiMAIster.Core.Tests;

public class DebuffEffectTests : TestBase
{
    private void Vulnerable(Actor dealer, double extra) =>
        Engine.Handle(new DebuffEffect(Enemy, "VULNERABLE_POWER", dealer, extra, Offense: true, Credit: DebuffCredit.CurrentTurn));

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
        Engine.Handle(new DebuffEffect(Enemy, "WEAK_POWER", B, 3, Offense: false, Credit: DebuffCredit.CurrentTurn));
        Engine.Handle(new DebuffEffect(Enemy, "WEAK_POWER", A, 2, Offense: false, Credit: DebuffCredit.CurrentTurn));

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
        Engine.Handle(new DebuffEffect(Enemy, "STRENGTH_POWER~DOWN", C, 4, Offense: false, Credit: DebuffCredit.Stacks));

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

public class BorrowedTests : TestBase
{
    [Fact]
    public void Bully_BonusSharedByRemainingTurns()
    {
        Power(A, Enemy, "VULNERABLE_POWER", 2);
        Power(B, Enemy, "VULNERABLE_POWER", 1);
        Engine.Handle(new DebuffEffect(Enemy, "VULNERABLE_POWER", C, 6, Offense: true, Credit: DebuffCredit.RemainingTurns));

        Assert.Equal(4, Combat(IdA).DamageEnabledAllies, 6);
        Assert.Equal(2, Combat(IdB).DamageEnabledAllies, 6);
        Assert.Equal(0, Combat(IdC).DamageDealt); // the attacker's damage itself comes from DamageDealt events only
    }

    [Fact]
    public void Dominate_StrengthRemembersWhoAppliedVulnerable()
    {
        Power(B, Enemy, "VULNERABLE_POWER", 2);
        Power(A, Enemy, "VULNERABLE_POWER", 1);
        Engine.Handle(new StrengthBorrowed(A, Enemy, "VULNERABLE_POWER", 3));   // A plays Dominate: +3 Strength
        Engine.Handle(new StrengthUsed(A, Strength: 3, Damage: 3));              // next hit: +3 damage from Strength

        Assert.Equal(2, Combat(IdB).DamageEnabledAllies, 6);
        Assert.Equal(1, Combat(IdA).DamageEnabledSelf, 6);
    }

    [Fact]
    public void Dominate_OwnStrengthDilutesTheBorrowedShare()
    {
        Power(B, Enemy, "VULNERABLE_POWER", 2);
        Engine.Handle(new StrengthBorrowed(A, Enemy, "VULNERABLE_POWER", 2));
        Engine.Handle(new StrengthUsed(A, Strength: 4, Damage: 4));              // 2 borrowed + 2 own Strength

        Assert.Equal(2, Combat(IdB).DamageEnabledAllies, 6);
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

public class MissedChanceTests : TestBase
{
    [Fact]
    public void BlockKept_CountsHpLostUpToTheCardsBlock()
    {
        Engine.Handle(new BlockCardKept(A, "Card:DEFEND", 5));
        EnemyHits(A, blocked: 0, hp: 3);
        EnemyHits(A, blocked: 0, hp: 4);
        Engine.Handle(new RoundStarted(2));

        Assert.Equal(5, Combat(IdA).MissedByKind["blockKept"]);
        Assert.Single(Combat(IdA).MissedEntries);
    }

    [Fact]
    public void BlockKept_NoHpLost_NothingCounted()
    {
        Engine.Handle(new BlockCardKept(A, "Card:DEFEND", 5));
        Engine.Handle(new RoundStarted(2));
        Assert.False(Combat(IdA).MissedByKind.ContainsKey("blockKept"));
    }

    [Fact]
    public void Choices_SumWeakAttacksAndOtherKinds()
    {
        Engine.Handle(new WeakAttack(A, "Card:STRIKE", 6, "Card:BASH", 8));
        Engine.Handle(new MissedChance(A, "smallBlock", new[] { "Card:DEFEND", "5", "Card:SHRUG", "8" }));
        Engine.Handle(new MissedChance(A, "energy", new[] { "2" }, 2));
        Assert.Equal(2, Combat(IdA).MissedChoices);
        Assert.Equal(2, Combat(IdA).MissedByKind["energy"]);
    }
}

public class CoopCreditTests : TestBase
{
    [Fact]
    public void Direct_CreditsTheApplier()
    {
        Engine.Handle(new DebuffEffect(Enemy, "FLANKING_POWER", B, 5, Offense: true, Credit: DebuffCredit.Direct, Applier: A));
        Assert.Equal(5, Combat(IdA).DamageEnabledAllies);
    }

    [Fact]
    public void GivenAndTemporaryStrength_SharedWithOwnStrength()
    {
        Engine.Handle(new StrengthGiven(B, A, 2));                                     // Blaze from A
        Engine.Handle(new StrengthUsed(B, Strength: 5, Damage: 5, Temporary: new[] { (C, 1.0) })); // Coordinate from C, 2 own

        Assert.Equal(2, Combat(IdA).DamageEnabledAllies, 6);
        Assert.Equal(1, Combat(IdC).DamageEnabledAllies, 6);
    }

    [Fact]
    public void DotWithDealer_CreditedToThatPlayerOnly()
    {
        Power(A, Enemy, "STRANGLE_POWER", 2);
        Power(B, Enemy, "STRANGLE_POWER", 2);
        Engine.Handle(new DamageDealt(A, Enemy, 0, 2, 0, false, Source.Dot("STRANGLE_POWER")));
        Assert.Equal(2, Combat(IdA).DamageDealt);
        Assert.Equal(0, Combat(IdB).DamageDealt);
    }
}
