using KikiMAIster.Core;
using Xunit;

namespace KikiMAIster.Core.Tests;

/// v0.17.38 (code review of 2026-10-29): kills once per enemy, own pet kill, cards played again, heals after fights,
/// block enabled by an ally's Dexterity, card limit in the lethal check, status damage by creator.
public class Review0_17_38Tests : TestBase
{
    [Fact]
    public void EnemyThatComesBack_IsKilledOnce()
    {
        Hit(A, Enemy, 0, 10, killed: true);
        Hit(A, Enemy, 0, 10, killed: true); // Reconnexion: the same enemy dies again
        Hit(B, Enemy2, 0, 5, killed: true);
        Assert.Equal(1, Combat(IdA).Kills);
        Assert.Equal(1, Combat(IdB).Kills);
    }

    [Fact]
    public void OwnerKillingTheirPet_IsNotProtection()
    {
        Engine.Handle(new PetHpGiven(B, 6, A)); // B gave Osty 6 HP
        Engine.Handle(new PetHpGiven(A, 6, A));
        Hit(A, PetA, 0, 12, killed: true, card: "SACRIFICE");
        Assert.Equal(0, Combat(IdA).AbsorbedByPet);
        Assert.Equal(0, Combat(IdB).PetAbsorbedForAllies);
        Assert.Equal(1, Combat(IdA).PetDeaths);
        // The owner losing HP afterwards releases nothing.
        EnemyHits(A, 0, 5);
        Assert.Equal(0, Combat(IdB).SavedForAllies);
    }

    [Fact]
    public void CardPlayedAgain_IsOneCardAndOnePlay()
    {
        Engine.Handle(new CardEvent(A, CardAction.Played));
        Hit(A, Enemy, 0, 8);
        Engine.Handle(new CardPlayFinished(A));
        Engine.Handle(new CardEvent(A, CardAction.Played, Repeat: true)); // Déflagration
        Hit(A, Enemy, 0, 8);
        Engine.Handle(new CardPlayFinished(A));
        Assert.Equal(1, Combat(IdA).CardsPlayed);
        Assert.Equal(1, Combat(IdA).MostCardsInTurn);
        Assert.Equal(16, Combat(IdA).BestCardPlay);
    }

    [Fact]
    public void HealAfterFight_CountsOutsideFights_AndSurvivesHistoryRead()
    {
        Engine.Handle(new CombatEnded(true));
        Engine.Handle(new AfterFightHealed(A, 6)); // Sang ardent
        Engine.Handle(new RunHistoryRead(A, 0, 0, new Dictionary<string, double>(), null,
            new Dictionary<string, double> { ["restSite"] = 20 }));
        var s = Engine.Get(Scope.Run, IdA);
        Assert.Equal(26, s.HealedOutside);
        Assert.Equal(6, s.HealedOutsideBy["afterFight"]);
    }

    [Fact]
    public void BlockFromAllyDexterity_IsEnabledBlock_AndItsSavingsAreTheirs()
    {
        Block(A, A, 8);
        Engine.Handle(new BlockEnabled(A, B, 3)); // Oubli: 3 of those 8 from B's Dexterity
        Assert.Equal(3, Combat(IdB).BlockEnabledForAllies);
        EnemyHits(A, 8, 0);
        Assert.Equal(3, Combat(IdB).SavedByEnabledBlock, 3);
        Assert.Equal(8, Combat(IdA).PreventedOwnBlock);
    }

    [Fact]
    public void Lethal_RespectsTheCardLimit()
    {
        var attacks = new[]
        {
            new Lethal.Attack("Card:STRIKE", 1, false, new[] { 6 }),
            new Lethal.Attack("Card:STRIKE", 1, false, new[] { 6 })
        };
        Assert.NotNull(Lethal.Find(attacks, 3, new[] { 12 }));
        Assert.Null(Lethal.Find(attacks, 3, new[] { 12 }, maxPlays: 1)); // Paresse
    }

    [Fact]
    public void StatusDamage_GoesToItsCreator()
    {
        Engine.Handle(new DamageDealt(Actor.Enemy(0), A, 0, 2, 0, false, Source.Card("BURN"))); // an enemy's Brûlure
        Engine.Handle(new DamageDealt(A, A, 0, 3, 0, false, Source.Card("REGRET"))); // a curse of A's deck
        Engine.Handle(new DamageDealt(B, A, 0, 1, 0, false, Source.Card("BURN"))); // a Brûlure from B
        Assert.Equal(2, Combat(IdA).HpLostFromEnemies);
        Assert.Equal(3, Combat(IdA).HpLostFromSelf);
        Assert.Equal(1, Combat(IdA).HpLostFromAllies);
        Assert.Equal(0, Combat(IdA).HpLostOther);
    }
}
