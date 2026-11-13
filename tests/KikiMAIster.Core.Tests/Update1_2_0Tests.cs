using KikiMAIster.Core;
using Xunit;

namespace KikiMAIster.Core.Tests;

/// 1.2.0 (game update beta v0.113.0): the Fool's Shots with no arrow ready.
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
}
