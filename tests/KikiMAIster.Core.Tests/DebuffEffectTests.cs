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
        EnemyHits(B, 0, 6);
        Engine.Handle(new DebuffEffect(Enemy, "WEAK_POWER", A, 2, Offense: false, Credit: DebuffCredit.CurrentTurn));
        EnemyHits(A, 0, 4);

        Assert.Equal(3, Combat(IdA).PreventedByDebuffsAllies);
        Assert.Equal(2, Combat(IdA).PreventedByDebuffsSelf);
        Assert.Equal(3, Combat(IdA).SavedForAllies);
        Assert.Equal(5, Combat(IdA).PreventedTotal);
    }

    /// Fight 1 of the 2026-10-27 co-op run: A's Weak turns a 6 hit into 4 on B, who has 5 block. Without Weak, only 1
    /// would have gone through the block: 1 HP saved, not 2.
    [Fact]
    public void Weak_CountsOnlyWhatTheBlockWouldNotHaveStopped()
    {
        Power(A, Enemy, "WEAK_POWER", 1);
        Block(B, B, 5);
        Engine.Handle(new DebuffEffect(Enemy, "WEAK_POWER", B, 2, Offense: false, Credit: DebuffCredit.CurrentTurn));
        EnemyHits(B, 4, 0);
        Assert.Equal(1, Combat(IdA).SavedForAllies);

        Block(C, C, 10);
        Engine.Handle(new DebuffEffect(Enemy, "WEAK_POWER", C, 2, Offense: false, Credit: DebuffCredit.CurrentTurn));
        EnemyHits(C, 4, 0);
        Assert.Equal(1, Combat(IdA).SavedForAllies);
    }

    /// The same hit on B's pet: its saved HP only count once the pet died and B then lost HP (v0.17.4).
    [Fact]
    public void Weak_OnPet_CountsOnlyAfterThePetDiedAndTheOwnerLostHp()
    {
        var petB = Actor.Pet(IdB, 11);
        Power(A, Enemy, "WEAK_POWER", 1);
        Block(B, B, 5);
        Engine.Handle(new DebuffEffect(Enemy, "WEAK_POWER", petB, 2, Offense: false, Credit: DebuffCredit.CurrentTurn));
        EnemyHits(petB, 4, 0);
        Assert.Equal(0, Combat(IdA).SavedForAllies);

        EnemyHits(B, 0, 3); // the pet is alive: nothing yet
        Assert.Equal(0, Combat(IdA).SavedForAllies);
        EnemyHits(petB, 0, 6, killed: true);
        EnemyHits(B, 0, 3);
        Assert.Equal(1, Combat(IdA).SavedForAllies);
    }

    [Fact]
    public void StrengthDown_SharedByStacks_AcrossOrigins()
    {
        Power(A, Enemy, "STRENGTH_POWER~DOWN", 3);
        Power(B, Enemy, "STRENGTH_POWER~DOWN@ENFEEBLING_TOUCH_POWER", 1);
        Engine.Handle(new DebuffEffect(Enemy, "STRENGTH_POWER~DOWN", C, 4, Offense: false, Credit: DebuffCredit.Stacks));
        EnemyHits(C, 0, 5);

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

public class HelpTests : TestBase
{
    [Fact]
    public void Concoct_PoisonDamageStaysWithAttacker_EnabledForConcoctPlayer()
    {
        Engine.Handle(new PowerChanged(B, Enemy, "POISON_POWER", 3, true, "", A, "CONCOCT_POWER"));
        Engine.Handle(new DamageDealt(Actor.None, Enemy, 0, 3, 0, false, Source.Dot("POISON_POWER")));

        Assert.Equal(3, Combat(IdB).DamageDealt);
        Assert.Equal(3, Combat(IdA).DamageEnabledAllies);
        Assert.Equal(0, Combat(IdA).DamageDealt);
    }

    [Fact]
    public void MostCardsInTurn_IsTheBestTurn()
    {
        Engine.Handle(new CardEvent(A, CardAction.Played));
        Engine.Handle(new CardEvent(A, CardAction.Played));
        Engine.Handle(new RoundStarted(2));
        Engine.Handle(new CardEvent(A, CardAction.Played));
        Assert.Equal(2, Combat(IdA).MostCardsInTurn);
        Assert.Equal(3, Combat(IdA).CardsPlayed);
    }

    [Fact]
    public void BiggestHit_IncludesBlockAndOverkill()
    {
        Hit(A, Enemy, blocked: 4, hp: 6, overkill: 10, killed: true);
        Assert.Equal(20, Combat(IdA).BiggestHit);
    }
}

/// v0.17.5: attacks that grow with the pet's HP (Déchaînement, Protecteur).
public class PetHpAttackTests : TestBase
{
    [Fact]
    public void Unleash_PartFromHpSavedOrGivenByAnAlly_IsEnabledForThatAlly()
    {
        var petB = Actor.Pet(IdB, 11);
        Engine.Handle(new PetHpGiven(B, 6, B));
        Engine.Handle(new PetHpGiven(C, 2, B));
        Power(A, Enemy, "WEAK_POWER", 1);
        Engine.Handle(new DebuffEffect(Enemy, "WEAK_POWER", petB, 2, Offense: false, Credit: DebuffCredit.CurrentTurn));
        EnemyHits(petB, 0, 3);
        // Osty has 8 HP left (2 of them saved by A's Weak); Déchaînement's HP part dealt 8 damage.
        Engine.Handle(new PetHpAttack(petB, 8, 0, 8));
        Assert.Equal(2, Combat(IdA).DamageEnabledAllies, 6);
        Assert.Equal(1.5, Combat(IdC).DamageEnabledAllies, 6); // 6 other HP × 2/8 given by C
        Assert.Equal(0, Combat(IdB).DamageEnabledAllies);

        // Protecteur (max HP): only HP given count.
        Engine.Handle(new PetHpAttack(petB, 0, 8, 8));
        Assert.Equal(3.5, Combat(IdC).DamageEnabledAllies, 6);
    }

    [Fact]
    public void Sacrifice_BlockFromPetHpGivenByAnAlly_IsEnabledThenSaved()
    {
        Engine.Handle(new PetHpGiven(B, 6, B));
        Engine.Handle(new PetHpGiven(C, 2, B));
        Engine.Handle(new BlockGained(B, B, 24, Source.Card("SACRIFICE"), PlayId: 3));
        Engine.Handle(new PetHpBlock(B));
        Assert.Equal(6, Combat(IdC).BlockEnabledForAllies, 6);
        EnemyHits(B, 8, 0);
        Assert.Equal(2, Combat(IdC).SavedForAllies, 6);
        Assert.Equal(8, Combat(IdB).PreventedOwnBlock, 6);
    }

    [Fact]
    public void BlockOnPet_FeedsUnleash_ForTheAllyWhoGaveIt()
    {
        var petB = Actor.Pet(IdB, 11);
        Engine.Handle(new PetHpGiven(B, 8, B));
        Block(B, C, 4); // C's block on B
        EnemyHits(petB, 4, 0);
        Assert.Equal(0, Combat(IdC).SavedForAllies);
        Engine.Handle(new PetHpAttack(petB, 8, 0, 8));
        Assert.Equal(4, Combat(IdC).DamageEnabledAllies, 6);
    }
}
