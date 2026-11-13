using KikiMAIster.Core;
using Xunit;

namespace KikiMAIster.Core.Tests;

/// 1.2.0 (game update beta v0.113.0): the Fool's Shots with no arrow ready, Paré au combat kept.
public class Update1_2_0Tests
{
    private static readonly Actor A = Actor.Player(1, 1);
    private static readonly object Enemy = new();
    private int _nextId;

    private static TurnCard Card(string id, int cost = 1, int damage = 0, bool noArrow = false) => new()
    {
        Ref = new object(), Key = $"Card:{id}", Kind = CardKind.Attack, Cost = cost, HitsEnemies = true, NoArrow = noArrow,
        DamageOn = damage > 0 ? new Dictionary<object, int>(ReferenceEqualityComparer.Instance) { [Enemy] = damage } : new Dictionary<object, int>()
    };

    private TurnPlay Shot(int energy = 1) => new()
    {
        Id = ++_nextId, Owner = A, CardRef = new object(), CardKey = "Card:FOOLISH_SHOT", Kind = CardKind.Attack, EnergySpent = energy,
        EmptyShot = true
    };

    private static List<MissedChance> Missed(TurnResult r, string kind) => r.Events.OfType<MissedChance>().Where(m => m.Kind == kind).ToList();

    private static TurnState State(TurnPlayer player, params TurnPlay[] plays)
    {
        var s = new TurnState { Players = { player }, Plays = plays.ToList() };
        s.EnemiesLeft[Enemy] = 40;
        return s;
    }

    [Fact]
    public void EmptyShot_IsWasted_AndItsEnergyForNothing()
    {
        var r = TurnReview.Resolve(State(new TurnPlayer { Actor = A }, Shot()));
        Assert.Equal(new[] { "Card:FOOLISH_SHOT", "1" }, Missed(r, "emptyShot").Single().Args);
        Assert.Equal(1, r.Events.OfType<HelpGiven>().Where(h => h.Kind == "energyUseless").Sum(h => h.Amount));
    }

    [Fact]
    public void EmptyShot_ThatDidSomethingElse_IsFine()
    {
        var shot = Shot();
        shot.HadNonDamageEffect = true;
        Assert.Empty(Missed(TurnReview.Resolve(State(new TurnPlayer { Actor = A }, shot)), "emptyShot"));
        var hit = Shot();
        hit.Hits.Add(new EnemyHit(Enemy, 15, 15));
        Assert.Empty(Missed(TurnReview.Resolve(State(new TurnPlayer { Actor = A }, hit)), "emptyShot"));
    }

    [Fact]
    public void ShotWithoutArrow_LeftInHand_IsNoMissedEnergy()
    {
        var player = new TurnPlayer { Actor = A, Energy = 1, Hand = { Card("FOOLISH_SHOT", damage: 15, noArrow: true) } };
        Assert.Empty(Missed(TurnReview.Resolve(State(player)), "energy"));
        var withArrow = new TurnPlayer { Actor = A, Energy = 1, Hand = { Card("FOOLISH_SHOT", damage: 15) } };
        Assert.Single(Missed(TurnReview.Resolve(State(withArrow)), "energy"));
    }

    private TurnPlay Attack(string key, int energy, object? inHand = null) => new()
    {
        Id = ++_nextId, Owner = A, CardRef = new object(), CardKey = $"Card:{key}", Kind = CardKind.Attack, EnergySpent = energy,
        HandAtPlay = inHand == null ? new List<object>() : new List<object> { inHand }
    };

    /// 3 energy: Paré au combat (3) kept, two 1-cost Frappes played; a third Frappe and a 2-cost Coup de tonnerre left.
    /// With it first: 0 left for Frappes at 0 → the two played + the third, Coup de tonnerre at 1 not paid: one more attack.
    [Fact]
    public void ExpectAFight_Kept_WhenItPaidForMoreAttacks()
    {
        var cut = new TurnCard { Ref = new object(), Key = "Card:EXPECT_A_FIGHT", Kind = CardKind.Skill, Cost = 3, GainsBlock = true };
        var player = new TurnPlayer
        {
            Actor = A, Energy = 1, AttackDiscount = cut,
            Hand = { cut, Card("STRIKE_IRONCLAD", cost: 1, damage: 6), Card("THUNDERCLAP", cost: 2, damage: 4) }
        };
        var r = TurnReview.Resolve(State(player, Attack("STRIKE_IRONCLAD", 1, cut.Ref), Attack("STRIKE_IRONCLAD", 1, cut.Ref)));
        Assert.Equal(new[] { "Card:EXPECT_A_FIGHT", "1" }, Missed(r, "attackDiscount").Single().Args);
        // Drawn after the attacks: they could not have been cheaper, and 1 energy does not pay for it.
        var late = TurnReview.Resolve(State(player, Attack("STRIKE_IRONCLAD", 1), Attack("STRIKE_IRONCLAD", 1)));
        Assert.Empty(Missed(late, "attackDiscount"));
    }
}

/// 1.2.0: a Tisse-Mort with Titos and Wuppy (two pets), the Pie voleuse trophy.
public class Pets1_2_0Tests : TestBase
{
    [Fact]
    public void TwoPets_WuppyDying_KeepsTitosHpGivenByAnAlly()
    {
        var titos = Actor.Pet(IdA, 10);
        var wuppy = Actor.Pet(IdA, 12);
        Engine.Handle(new PetHpGiven(A, 5, A, titos));
        Engine.Handle(new PetHpGiven(B, 5, A, titos)); // B gave Titos 5 HP
        EnemyHits(wuppy, 0, 6, killed: true);           // Wuppy dies: its HP were A's own
        Assert.Equal(6, Combat(IdA).AbsorbedByPet);
        Assert.Equal(0, Combat(IdB).PetAbsorbedForAllies);
        EnemyHits(titos, 0, 4);                          // Titos: half of it is B's help
        Assert.Equal(2, Combat(IdB).PetAbsorbedForAllies);
        Assert.Equal(8, Combat(IdA).AbsorbedByPet);
        Assert.Equal(1, Combat(IdA).PetDeaths);
    }

    [Fact]
    public void Magpie_MostRelicsAtTheEnd()
    {
        Engine.Handle(new CombatEnded(true));
        Engine.Handle(new RunHistoryRead(A, 0, 0, new Dictionary<string, double>(), Economy: new RunEconomy(0, 0, 0, 0, 0, 0, 0, 0, 0, 7)));
        Engine.Handle(new RunHistoryRead(B, 0, 0, new Dictionary<string, double>(), Economy: new RunEconomy(0, 0, 0, 0, 0, 0, 0, 0, 0, 9)));
        var run = new Dictionary<ulong, PlayerStats> { [IdA] = Engine.Get(Scope.Run, IdA), [IdB] = Engine.Get(Scope.Run, IdB) };
        Assert.Equal(new[] { (IdB, 9.0) }, Trophies.Winners(Trophies.All.Single(c => c.Key == "trophy.relics"), run));
    }
}
