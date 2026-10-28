using KikiMAIster.Core;

namespace KikiMAIster.Core.Tests;

public class DamageTests : TestBase
{
    [Fact]
    public void DamageToEnemy_SplitsHpBlockAndOverkill()
    {
        Hit(A, Enemy, blocked: 5, hp: 10);
        Hit(A, Enemy, blocked: 0, hp: 5, overkill: 10, killed: true);

        var s = Combat(IdA);
        Assert.Equal(15, s.DamageDealt);
        Assert.Equal(5, s.DamageIntoBlock);
        Assert.Equal(10, s.Overkill);
        Assert.Equal(30, s.TotalRawDamage);
        Assert.Equal(1, s.Kills);
        Assert.Equal(15, s.BiggestHit);
        Assert.Equal(15, s.DamageBySource["Card:STRIKE"]); // breakdown of damage dealt (HP)
    }

    [Fact]
    public void PetDamage_CountsForOwner_UnderSourcePet()
    {
        Hit(PetA, Enemy, blocked: 0, hp: 9, card: "UNLEASH");

        Assert.Equal(9, Combat(IdA).DamageDealt);
        Assert.Equal(9, Combat(IdA).DamageBySource["Pet"]);
    }

    [Fact]
    public void Poison_SplitInProportionToStacks()
    {
        Power(A, Enemy, "POISON_POWER", 6);
        Power(B, Enemy, "POISON_POWER", 4);
        Engine.Handle(new DamageDealt(Actor.None, Enemy, 0, 10, 0, false, Source.Dot("POISON_POWER")));

        Assert.Equal(6, Combat(IdA).DamageDealt, 6);
        Assert.Equal(4, Combat(IdB).DamageDealt, 6);
        Assert.Equal(6, Combat(IdA).DotDamage, 6);
    }

    [Fact]
    public void Poison_DecayKeepsProportions_KillGoesToBiggestShare()
    {
        Power(A, Enemy, "POISON_POWER", 6);
        Power(B, Enemy, "POISON_POWER", 4);
        Power(Actor.None, Enemy, "POISON_POWER", -1);   // decay after a tick
        Power(B, Enemy, "POISON_POWER", 5);             // now A 5.4, B 8.6
        Engine.Handle(new DamageDealt(Actor.None, Enemy, 0, 14, 0, true, Source.Dot("POISON_POWER")));

        Assert.Equal(5.4, Combat(IdA).DamageDealt, 6);
        Assert.Equal(8.6, Combat(IdB).DamageDealt, 6);
        Assert.Equal(0, Combat(IdA).Kills);
        Assert.Equal(1, Combat(IdB).Kills);
    }

    [Fact]
    public void DoomKill_CreditedToApplier_WithKill()
    {
        Power(A, Enemy, "DOOM_POWER", 6);
        Power(A, Enemy, "DOOM_POWER", 6);
        Engine.Handle(new DamageDealt(Actor.None, Enemy, 0, 13, 0, true, Source.Dot("DOOM_POWER")));

        Assert.Equal(13, Combat(IdA).DamageDealt, 6);
        Assert.Equal(13, Combat(IdA).DamageBySource["DamageOverTime:DOOM_POWER"], 6);
        Assert.Equal(1, Combat(IdA).Kills);
    }

    [Fact]
    public void HpLossReducedByRelic_CountsAsPrevented()
    {
        Engine.Handle(new HpLossPrevented(A, 1, "Relic:TUNGSTEN_ROD"));
        Engine.Handle(new HpLossPrevented(PetA, 1, "Relic:TUNGSTEN_ROD"));
        Engine.Handle(new HpLossPrevented(Enemy, 5, "Power:INTANGIBLE_POWER"));

        Assert.Equal(2, Combat(IdA).PreventedByEffects);
        Assert.Equal(2, Combat(IdA).PreventedByEffectSource["Relic:TUNGSTEN_ROD"]);
        Assert.Equal(2, Combat(IdA).PreventedTotal);
    }

    [Fact]
    public void PetHpGiven_TrackedSeparately_NeverWastedOrUseless()
    {
        Engine.Handle(new PetHpGiven(A, 6));
        Engine.Handle(new PetHpGiven(A, 3));
        Engine.Handle(new CombatEnded(true));

        Assert.Equal(9, Combat(IdA).PetHpGiven);
        Assert.Equal(0, Combat(IdA).WastedBlock + Combat(IdA).UselessBlock);
    }

    [Fact]
    public void DebuffsApplied_CountedPerType()
    {
        Power(A, Enemy, "VULNERABLE_POWER", 2);
        Power(A, Enemy2, "VULNERABLE_POWER", 2);
        Power(A, Enemy, "WEAK_POWER", 1);
        Power(Actor.None, Enemy, "VULNERABLE_POWER", -1); // decay is not "applied"

        var d = Combat(IdA).DebuffsApplied;
        Assert.Equal(2, d["VULNERABLE_POWER"].Count);
        Assert.Equal(4, d["VULNERABLE_POWER"].Stacks);
        Assert.Equal(1, d["WEAK_POWER"].Count);
    }

    [Fact]
    public void FriendlyFire_DebuffsOnAlliesAndSelf()
    {
        Power(A, B, "VULNERABLE_POWER", 1);
        Power(A, A, "NO_DRAW_POWER", 1);
        Power(A, A, "STRENGTH_POWER", 3, debuff: false); // buffs are not friendly fire

        var f = Combat(IdA).FriendlyDebuffs;
        Assert.Equal(1, f["VULNERABLE_POWER"].Count);
        Assert.False(f.ContainsKey("NO_DRAW_POWER")); // on self: self-inflicted, not friendly fire (v0.17.7)
        Assert.Equal(1, Combat(IdA).SelfDebuffs["NO_DRAW_POWER"].Count);
        Assert.False(f.ContainsKey("STRENGTH_POWER"));
    }

    [Fact]
    public void DebuffsReceivedFromEnemies()
    {
        Power(Enemy, A, "FRAIL_POWER", 2);
        Assert.Equal(2, Combat(IdA).DebuffsReceived["FRAIL_POWER"].Stacks);
    }

    [Fact]
    public void SelfDamage_And_OtherSources()
    {
        Hit(A, A, blocked: 0, hp: 3, card: "BLOODLETTING");
        Engine.Handle(new DamageDealt(Actor.None, A, 0, 2, 0, false, Source.Unknown));

        Assert.Equal(3, Combat(IdA).HpLostFromSelf);
        Assert.Equal(3, Combat(IdA).SelfDamage);
        Assert.Equal(2, Combat(IdA).HpLostOther);
        Assert.Equal(5, Combat(IdA).HpLost);
    }

    [Fact]
    public void PetAbsorbingHits_IsProtection_AndUsesOwnerBlock()
    {
        Block(A, A, 3);
        EnemyHits(PetA, blocked: 3, hp: 5, killed: true);

        var s = Combat(IdA);
        // v0.17.8: block that protected the pet counts only once the pet died and its owner loses HP.
        Assert.Equal(0, s.PreventedOwnBlock);
        Assert.Equal(5, s.AbsorbedByPet);
        Assert.Equal(1, s.PetDeaths);
        Assert.Equal(0, s.HpLost);
        EnemyHits(A, 0, 2);
        Assert.Equal(2, s.PreventedOwnBlock);
        EnemyHits(A, 0, 4);
        Assert.Equal(3, s.PreventedOwnBlock);
    }

    [Fact]
    public void Healing_SelfAndAllies()
    {
        Engine.Handle(new Healed(A, A, 4));
        Engine.Handle(new Healed(A, B, 6));
        Engine.Handle(new Healed(Actor.None, C, 2));

        Assert.Equal(4, Combat(IdA).HealingSelf);
        Assert.Equal(6, Combat(IdA).HealingAllies);
        Assert.Equal(2, Combat(IdC).HealingSelf);
    }

    [Fact]
    public void Scopes_TurnResetsEachRound_CombatResetsEachCombat_RunKeepsAll()
    {
        Hit(A, Enemy, 0, 5);
        Engine.Handle(new RoundStarted(2));
        Hit(A, Enemy, 0, 7);

        Assert.Equal(7, Engine.Get(Scope.Turn, IdA).DamageDealt);
        Assert.Equal(12, Combat(IdA).DamageDealt);

        Engine.Handle(new CombatEnded(true));
        Engine.Handle(new CombatStarted(new[] { new PlayerInfo(IdA, "A", "IRONCLAD") }));
        Hit(A, Enemy, 0, 1);

        Assert.Equal(1, Combat(IdA).DamageDealt);
        Assert.Equal(13, Engine.Get(Scope.Run, IdA).DamageDealt);
    }

    [Fact]
    public void DotFromPotion_CountsAsPotionDamage()
    {
        Engine.Handle(new PowerChanged(A, Enemy, "DEMISE_POWER", 9, true, "Potion:POWDERED_DEMISE"));
        Engine.Handle(new DamageDealt(Actor.None, Enemy, 0, 9, 0, false, Source.Dot("DEMISE_POWER")));

        Assert.Equal(9, Combat(IdA).DamageDealt);
        Assert.Equal(9, Combat(IdA).DamageBySource["Potion:POWDERED_DEMISE"]);
        Assert.Equal(0, Combat(IdA).DotDamage);
    }

    [Fact]
    public void ThornsFromPotion_SplitBetweenPotionAndPower()
    {
        Engine.Handle(new PowerChanged(A, A, "THORNS_POWER", 3, false, "Potion:LIQUID_BRONZE"));
        Engine.Handle(new PowerChanged(A, A, "THORNS_POWER", 1, false));
        Engine.Handle(new DamageDealt(A, Enemy, 0, 4, 0, false, Source.Power("THORNS_POWER")));

        Assert.Equal(4, Combat(IdA).DamageDealt);
        Assert.Equal(3, Combat(IdA).DamageBySource["Potion:LIQUID_BRONZE"], 6);
        Assert.Equal(1, Combat(IdA).DamageBySource["Power:THORNS_POWER"], 6);
    }

    [Fact]
    public void BestCardPlay_CountsEveryHitAndTheCardsItPlays()
    {
        // Knife Trap plays two Shivs: one card play for the record.
        Engine.Handle(new CardEvent(A, CardAction.Played));
        Engine.Handle(new CardEvent(A, CardAction.Played));
        Hit(A, Enemy, 2, 4, card: "SHIV");
        Engine.Handle(new CardPlayFinished(A));
        Engine.Handle(new CardEvent(A, CardAction.Played));
        Hit(A, Enemy, 0, 6, 1, killed: true, card: "SHIV");
        Engine.Handle(new CardPlayFinished(A));
        Engine.Handle(new CardPlayFinished(A));
        Assert.Equal(13, Combat(IdA).BestCardPlay);
        Assert.Equal(7, Combat(IdA).BiggestHit);

        // A new card starts a new count; poison is not a card play.
        Engine.Handle(new CardEvent(A, CardAction.Played));
        Hit(A, Enemy2, 0, 5);
        Engine.Handle(new DamageDealt(Actor.None, Enemy2, 0, 20, 0, false, Source.Dot("POISON_POWER")));
        Engine.Handle(new CardPlayFinished(A));
        Assert.Equal(13, Engine.Get(Scope.Run, IdA).BestCardPlay);
    }

    [Fact]
    public void MissedEntries_KnowTheirFight()
    {
        Engine.Handle(new MissedChance(A, "order", new[] { "Card:STRIKE", "Card:BASH", "4" }));
        Assert.Equal(1, Combat(IdA).MissedEntries[0].Combat);
        Assert.Equal(1, Combat(IdA).MissedChoices);
    }

    [Fact]
    public void PetGivenToAlly_AbsorbedDamageIsTheGiversHelp()
    {
        var petB = Actor.Pet(IdB, 11);
        Engine.Handle(new PetHpGiven(B, 5, B));
        Engine.Handle(new PetHpGiven(A, 15, B)); // Legion of Bone played by A
        Engine.Handle(new DamageDealt(Enemy, petB, 0, 8, 0, false, Source.Unknown));
        Assert.Equal(6, Combat(IdA).PetAbsorbedForAllies);
        Assert.Equal(6, Combat(IdA).SavedForAllies);
        Assert.Equal(2, Combat(IdB).AbsorbedByPet);
        Assert.Equal(0, Combat(IdA).PreventedSelf);
    }

    [Fact]
    public void AllyBlockKept_CountsAllyHpLost()
    {
        Engine.Handle(new AllyBlockCardKept(A, "Card:LIFT", 8, AllAllies: false));
        EnemyHits(B, 0, 5);
        EnemyHits(C, 0, 12);
        EnemyHits(A, 0, 3); // A's own loss does not count
        Engine.Handle(new RoundStarted(2));
        Assert.Equal(8, Combat(IdA).MissedByKind["allyBlockKept"]);
        Assert.Equal(8, Combat(IdA).AvoidableHp);
        Assert.Equal(8, Trophies.All.First(c => c.Key == "trophy.friendly").Value(Combat(IdA)));
    }

    [Fact]
    public void HelpIsKeptPerAlly()
    {
        Block(B, A, 6);
        EnemyHits(B, 6, 0);
        Engine.Handle(new Healed(A, C, 4));
        Power(A, Enemy, "VULNERABLE_POWER", 2);
        Engine.Handle(new DebuffEffect(Enemy, "VULNERABLE_POWER", C, 5, Offense: true, Credit: DebuffCredit.CurrentTurn));
        Assert.Equal(6, Combat(IdA).SavedPerAlly["2"]);
        Assert.Equal(4, Combat(IdA).SavedPerAlly["3"]);
        Assert.Equal(5, Combat(IdA).EnabledPerAlly["3"]);
    }
}
