using KikiMAIster.Core;
using Xunit;

namespace KikiMAIster.Core.Tests;

/// v0.17.39: enchantment damage, HP lost by kind, heals by source, fight length and fight records.
public class Feedback0_17_39Tests : TestBase
{
    [Fact]
    public void EnchantmentDamage_IsADetail()
    {
        Hit(A, Enemy, 0, 9);
        Engine.Handle(new DamagePart(A, "Enchantment:SHARP", 3));
        Assert.Equal(9, Combat(IdA).DamageDealt);
        Assert.Equal(3, Combat(IdA).DamageByEnchantment["Enchantment:SHARP"]);
    }

    [Fact]
    public void HpLost_ByKind()
    {
        EnemyHits(A, 0, 6); // an enemy move, no kind given
        Engine.Handle(new DamageDealt(Enemy, A, 0, 3, 0, false, Source.Power("THORNS_POWER"), "reaction"));
        Engine.Handle(new DamageDealt(Actor.Enemy(0), A, 0, 2, 0, false, Source.Card("BURN"), "status"));
        Engine.Handle(new DamageDealt(B, A, 0, 1, 0, false, Source.Card("STRIKE")));
        var k = Combat(IdA).HpLostByKind;
        Assert.Equal(6, k["attack"]);
        Assert.Equal(3, k["reaction"]);
        Assert.Equal(2, k["status"]);
        Assert.Equal(1, k["ally"]);
    }

    [Fact]
    public void Heals_BySource()
    {
        Engine.Handle(new Healed(A, A, 5, "Potion:BLOOD_POTION"));
        Engine.Handle(new Healed(A, B, 4, "Card:BANDAGE_UP"));
        Assert.Equal(5, Combat(IdA).HealingBySource["Potion:BLOOD_POTION"]);
        Assert.Equal(4, Combat(IdA).HealingBySource["Card:BANDAGE_UP"]);
    }

    [Fact]
    public void FightLength_PerFightAndRunTotals()
    {
        Engine.Handle(new RoundStarted(2));
        Engine.Handle(new RoundStarted(3));
        Engine.Handle(new CombatEnded(true, 125));
        Assert.Equal(3, Engine.LastCombat!.Stats[IdA].FightTurns);
        Assert.Equal(125, Engine.LastCombat!.Stats[IdA].FightSeconds);
        var run = Engine.Get(Scope.Run, IdA);
        Assert.Equal(1, run.FightsTimed);
        Assert.Equal(3, run.FightTurns);
    }

    [Fact]
    public void FightRecords_ByLevelPlayersAndSoloCharacter()
    {
        var solo = FightRecords.Key(10, 1, "IRONCLAD", "JAW_WORM");
        var coop = FightRecords.Key(10, 2, "IRONCLAD", "JAW_WORM");
        Assert.Equal("A10.1p.IRONCLAD.JAW_WORM", solo);
        Assert.Equal("A10.2p.JAW_WORM", coop);
        var book = new FightRecords();
        Assert.False(book.Add(solo, 5, 120, "d1").Broken); // first win: stored, not a broken record
        var r = book.Add(solo, 4, 130, "d2");
        Assert.True(r.NewTurns);
        Assert.False(r.NewSeconds);
        Assert.Equal(5, r.OldTurns);
        Assert.True(book.Add(solo, 6, 90, "d3").NewSeconds);
        Assert.Null(book.Turns(coop));
        var back = FightRecords.Read(book.Write());
        Assert.Equal(4, back.Turns(solo)!.Value);
        Assert.Equal(90, back.Seconds(solo)!.Value);
    }
}
