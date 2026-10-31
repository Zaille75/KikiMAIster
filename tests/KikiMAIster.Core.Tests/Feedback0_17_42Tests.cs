using KikiMAIster.Core;
using Xunit;

namespace KikiMAIster.Core.Tests;

/// v0.17.42 (Régent solo run, 2026-10-29): overkill (column, Gâché misplays, trophies), Slow order between attacks (co-op
/// included), Max HP after a capped heal.
public class Feedback0_17_42Tests : TestBase
{
    private static readonly Actor PA = Actor.Player(IdA, 1);
    private static readonly Actor PB = Actor.Player(IdB, 2);
    private readonly object _x = new(), _y = new();
    private int _id;

    private TurnPlay Attack(string key, Actor? owner = null, object? target = null, int left = 0, IEnumerable<object>? hand = null,
        IEnumerable<object>? alliesHand = null) => new()
    {
        Id = ++_id, Owner = owner ?? PA, CardRef = new object(), CardKey = $"Card:{key}", Kind = CardKind.Attack, EnergySpent = 1,
        Target = target, TargetLeftBefore = left, HandAtPlay = (hand ?? Array.Empty<object>()).ToList(),
        AlliesHandAtPlay = new HashSet<object>(alliesHand ?? Array.Empty<object>(), ReferenceEqualityComparer.Instance)
    };

    private TurnResult Resolve(params TurnPlay[] plays)
    {
        var s = new TurnState { Players = { new TurnPlayer { Actor = PA }, new TurnPlayer { Actor = PB } }, Plays = plays.ToList() };
        s.EnemiesLeft[_y] = 20;
        return TurnReview.Resolve(s);
    }

    private static List<MissedChance> Missed(TurnResult r, string kind) => r.Events.OfType<MissedChance>().Where(m => m.Kind == kind).ToList();

    [Fact]
    public void BigAttackOnAWeakEnemy_ThenSmallOnAnother_IsGaché()
    {
        var small = Attack("STRIKE");
        var big = Attack("FALLING_STAR", target: _x, left: 3);
        big.KilledTarget = true;
        big.Overkill = 5;
        big.DamageOn[_y] = 8;
        big.HandDamageOnTarget[small.CardRef] = 6; // Frappe would have killed the 3-HP enemy
        small.Hits.Add(new EnemyHit(_y, 6, -1));
        var miss = Missed(Resolve(big, small), "overkill").Single();
        Assert.Equal(new[] { "Card:FALLING_STAR", "Card:STRIKE", "2" }, miss.Args);
    }

    [Fact]
    public void AttackKillingWhatBlackHoleWouldKill_IsGaché()
    {
        var big = Attack("FALLING_STAR", target: _x, left: 3);
        big.KilledTarget = true;
        big.Overkill = 5;
        big.DamageOn[_y] = 8;
        big.FollowUps.Add(new FollowUpHit("Power:BLACK_HOLE_POWER", _y, 3));
        var miss = Missed(Resolve(big), "overkillFollowUp").Single();
        Assert.Equal(new[] { "Card:FALLING_STAR", "Power:BLACK_HOLE_POWER", "8" }, miss.Args);
        // Black Hole too weak to kill it: not a misplay.
        big.FollowUps.Clear();
        big.FollowUps.Add(new FollowUpHit("Power:BLACK_HOLE_POWER", _y, 2));
        Assert.Empty(Missed(Resolve(big), "overkillFollowUp"));
    }

    [Fact]
    public void SlowAttacks_BiggerFirst_CountsOnlyWhenOneDamageIsLost()
    {
        // Zaille75's case: 8 then 6 against Slow 0 → 1: 0.2 damage, not counted.
        var six = Attack("STRIKE");
        var eight = Attack("FALLING_STAR", hand: new[] { six.CardRef });
        eight.Hits.Add(new EnemyHit(_y, 8, 0));
        six.Hits.Add(new EnemyHit(_y, 7, 1)); // 6 × 1.1
        Assert.Empty(Missed(Resolve(eight, six), "orderTurn"));

        // 30 then 10, three cards apart: 0.1 × 3 × 20 = 6 damage lost.
        var ten = Attack("STRIKE");
        var thirty = Attack("BIG", hand: new[] { ten.CardRef });
        thirty.Hits.Add(new EnemyHit(_y, 30, 0));
        ten.Hits.Add(new EnemyHit(_y, 13, 3));
        Assert.Equal("6", Missed(Resolve(thirty, ten), "orderTurn").Single().Args[1]);
    }

    [Fact]
    public void SlowAttacks_CoopOrder_GoesToThePlayerOfTheBiggerAttack()
    {
        var allySmall = Attack("STRIKE", owner: PB);
        var big = Attack("BIG", alliesHand: new[] { allySmall.CardRef });
        big.Hits.Add(new EnemyHit(_y, 30, 0));
        allySmall.Hits.Add(new EnemyHit(_y, 13, 3));
        var miss = Missed(Resolve(big, allySmall), "orderTurn").Single();
        Assert.Equal(PA, miss.Player);
        // The ally's card was not in their hand yet: nothing to reorder.
        var later = Attack("BIG");
        later.Hits.Add(new EnemyHit(_y, 30, 0));
        Assert.Empty(Missed(Resolve(later, allySmall), "orderTurn"));
    }

    [Fact]
    public void Overkill_AvoidablePart_AndMisplayKindsCounted()
    {
        Engine.Handle(new DamageDealt(A, Enemy, 0, 3, 5, true, Source.Card("FALLING_STAR"), OverkillAvoidable: true));
        Engine.Handle(new DamageDealt(A, Enemy2, 0, 2, 4, true, Source.Card("STRIKE")));
        Engine.Handle(new MissedChance(A, "overkill", new[] { "Card:A", "Card:B", "2" }));
        Engine.Handle(new MissedChance(A, "overkillFollowUp", new[] { "Card:A", "Power:BLACK_HOLE_POWER", "8" }));
        var s = Combat(IdA);
        Assert.Equal(9, s.Overkill);
        Assert.Equal(5, s.OverkillAvoidable);
        Assert.Equal(2, s.MissedWasted);
    }

    [Fact]
    public void MaxHpAfterCappedHeal_IsAMisplayOutsideFights()
    {
        Engine.Handle(new CombatEnded(true));
        Engine.Handle(new MissedChance(A, "maxHpAfterHeal", new[] { "Relic:FAKE_BLOOD_VIAL", "Relic:FAKE_MANGO", "3" }));
        var s = Engine.Get(Scope.Run, IdA);
        Assert.Equal(1, s.MissedOutside);
        Assert.Equal(0, s.MissedChoices + s.MissedWasted);
        Assert.Equal(0, s.MissedEntries.Last().Round);
    }

    [Fact]
    public void OverkillTrophies_MostAndLeast()
    {
        var run = new Dictionary<ulong, PlayerStats> { [IdA] = new() { Overkill = 30 }, [IdB] = new() { Overkill = 5 } };
        Assert.Equal(IdA, Trophies.Winner(Trophies.All.Single(c => c.Key == "trophy.overkill"), run)!.Value.Id);
        Assert.Equal(IdB, Trophies.Winner(Trophies.All.Single(c => c.Key == "trophy.efficientDamage"), run)!.Value.Id);
    }
}
