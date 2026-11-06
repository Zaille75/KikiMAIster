using KikiMAIster.Core;
using Xunit;

namespace KikiMAIster.Core.Tests;

/// v1.1.1 (GitHub reports of 2026-11-05, Zaille75): Slow order with place bonuses (Létalité…), Vulnérable kept (Tope là),
/// Givre block given to allies under Hibernation.
public class Feedback1_1_1Tests : TestBase
{
    private static readonly Actor PA = Actor.Player(IdA, 1);
    private static readonly Actor PB = Actor.Player(IdB, 2);
    private readonly object _y = new();
    private int _id;

    private TurnPlay Attack(string key, Actor? owner = null, int energy = 1, IEnumerable<object>? hand = null, IEnumerable<object>? alliesHand = null) => new()
    {
        Id = ++_id, Owner = owner ?? PA, CardRef = new object(), CardKey = $"Card:{key}", Kind = CardKind.Attack, EnergySpent = energy,
        HandAtPlay = (hand ?? Array.Empty<object>()).ToList(),
        AlliesHandAtPlay = new HashSet<object>(alliesHand ?? Array.Empty<object>(), ReferenceEqualityComparer.Instance)
    };

    private TurnResult Resolve(TurnState s) => TurnReview.Resolve(s);

    private TurnState State(params TurnPlay[] plays)
    {
        var s = new TurnState { Players = { new TurnPlayer { Actor = PA }, new TurnPlayer { Actor = PB } }, Plays = plays.ToList() };
        s.EnemiesLeft[_y] = 93;
        return s;
    }

    private static List<MissedChance> Missed(TurnResult r, string kind) => r.Events.OfType<MissedChance>().Where(m => m.Kind == kind).ToList();

    /// Idole oubliée, round 3: Létalité 75, Vulnérable, Ralenti. Frappe+ at Slow 6: 9 × 1.6 × 1.75 × 1.5 = 37.8 (37, Létalité's
    /// part 16.2), then Frappe at Slow 7: 6 × 1.7 × 1.5 = 15.3 (15). Swapped: 25 + 22 = 47 < 52, the order was right.
    [Fact]
    public void SlowOrder_LethalityOnTheBigAttack_IsNotAMiss()
    {
        var strike = Attack("STRIKE_NECROBINDER");
        var strikePlus = Attack("STRIKE_NECROBINDER^", hand: new[] { strike.CardRef });
        strikePlus.Hits.Add(new EnemyHit(_y, 37, 6, PlaceMult: 16.2, Vulnerable: true));
        strike.Hits.Add(new EnemyHit(_y, 15, 7, Vulnerable: true));
        Assert.True(TurnReview.SwapGain(strikePlus, strikePlus.Hits, strike, strike.Hits) < -4);
        Assert.Empty(Missed(Resolve(State(strikePlus, strike)), "orderTurn"));
    }

    [Fact]
    public void SlowOrder_WithoutPlaceBonus_StillCountsTheBiggerFirst()
    {
        // 30 then 10 (13 at Slow 3): 0.1 × 3 × (30 − 10) = 6 lost, as before.
        var small = Attack("STRIKE");
        var big = Attack("BLUDGEON", hand: new[] { small.CardRef });
        big.Hits.Add(new EnemyHit(_y, 30, 0));
        small.Hits.Add(new EnemyHit(_y, 13, 3));
        Assert.Equal(6, TurnReview.SwapGain(big, big.Hits, small, small.Hits), 1);
        var miss = Missed(Resolve(State(big, small)), "orderTurn").Single();
        Assert.Equal("6", miss.Args[1]);
    }

    [Fact]
    public void SlowOrder_AnAllysAttack_DoesNotTakeThePlayersLethality()
    {
        // A's attack with Létalité (base 10 × 1.75 = 17.5 at Slow 0), B's 6-damage attack at Slow 3 (7.8): B playing first,
        // A's attack would still be A's first attack: 0.1 × 3 × (10 × 1.75 − 6) = 3.45 lost.
        var ally = Attack("STRIKE", owner: PB);
        var mine = Attack("BIG", alliesHand: new[] { ally.CardRef });
        mine.Hits.Add(new EnemyHit(_y, 17, 0, PlaceMult: 7.5));
        ally.Hits.Add(new EnemyHit(_y, 8, 3));
        var gain = TurnReview.SwapGain(mine, mine.Hits, ally, ally.Hits);
        Assert.InRange(gain, 3, 4);
    }

    private static TurnCard HighFive(int cost = 2, int damage = 11) => new()
    {
        Ref = new object(), Key = "Card:HIGH_FIVE", Kind = CardKind.Attack, Cost = cost, VulnerableStacks = 2, AllEnemies = true,
        NeedsPet = true, HitsEnemies = true, DamageOn = new Dictionary<object, int>()
    };

    /// Scarabrideur, round 1: Tope là (11, Vulnérable 2 on every enemy) kept, Claquement de doigts 7, Déchaînement 7, Frappe 6
    /// played; the ally's two Frappes (6 + 6) hit too. Best line: Tope là 11 + Claquement 7 × 1.5 = 21.5 → +1.5 this turn, and
    /// Vulnérable 2 next turn: 50% of the 32 attack damage of the turn = +16.
    [Fact]
    public void VulnerableKept_WhilePlainAttacksWerePlayed_IsAMiss()
    {
        var tope = HighFive();
        ((Dictionary<object, int>)tope.DamageOn)[_y] = 11;
        var ally1 = Attack("STRIKE_DEFECT", owner: PB);
        var ally2 = Attack("STRIKE_DEFECT", owner: PB);
        ally1.Hits.Add(new EnemyHit(_y, 6, -1));
        ally2.Hits.Add(new EnemyHit(_y, 6, -1));
        var snap = Attack("SNAP");
        var unleash = Attack("UNLEASH");
        var strike = Attack("STRIKE_NECROBINDER");
        snap.Hits.Add(new EnemyHit(_y, 7, -1));
        unleash.Hits.Add(new EnemyHit(_y, 7, -1));
        strike.Hits.Add(new EnemyHit(_y, 6, -1));
        var s = new TurnState
        {
            Players = { new TurnPlayer { Actor = PA, Energy = 0, Hand = { tope } }, new TurnPlayer { Actor = PB } },
            Plays = { ally1, ally2, snap, unleash, strike }
        };
        s.EnemiesLeft[_y] = 48;
        var miss = Missed(Resolve(s), "vulnLeft").Single();
        Assert.Equal(new[] { "Card:HIGH_FIVE", "Card:SNAP+Card:UNLEASH+Card:STRIKE_NECROBINDER", "2", "2", "16" }, miss.Args);
    }

    [Fact]
    public void VulnerableKept_AfterAPowerWasPlayedInstead_IsNotAMiss()
    {
        // 3 energy: a power (2) and Frappe (1). Tope là would only replace Frappe: not enough energy, the power was a choice.
        var tope = HighFive();
        ((Dictionary<object, int>)tope.DamageOn)[_y] = 11;
        var power = new TurnPlay { Id = ++_id, Owner = PA, CardRef = new object(), CardKey = "Card:DEMON_FORM", Kind = CardKind.Power, EnergySpent = 2 };
        var strike = Attack("STRIKE");
        strike.Hits.Add(new EnemyHit(_y, 6, -1));
        var s = new TurnState { Players = { new TurnPlayer { Actor = PA, Energy = 0, Hand = { tope } } }, Plays = { power, strike } };
        s.EnemiesLeft[_y] = 48;
        Assert.Empty(Missed(Resolve(s), "vulnLeft"));
    }

    [Fact]
    public void VulnerableKept_EnemyAlreadyVulnerableAndDying_IsNotAMiss()
    {
        // Already Vulnérable 2 for next turn, and Frappe + Frappe (12) beat Tope là alone (11): nothing gained.
        var tope = HighFive();
        ((Dictionary<object, int>)tope.DamageOn)[_y] = 11;
        var s1 = Attack("STRIKE");
        var s2 = Attack("STRIKE");
        s1.Hits.Add(new EnemyHit(_y, 9, -1, Vulnerable: true));
        s2.Hits.Add(new EnemyHit(_y, 9, -1, Vulnerable: true));
        var s = new TurnState { Players = { new TurnPlayer { Actor = PA, Energy = 0, Hand = { tope } } }, Plays = { s1, s2 } };
        s.EnemiesLeft[_y] = 30;
        s.EnemyVulnerable[_y] = 3;
        Assert.Empty(Missed(Resolve(s), "vulnLeft"));
    }

    [Fact]
    public void FrostBlockGivenToAnAlly_IsTheOrbOwnersBlockGiven()
    {
        // C's Givre orb under Hibernation gives 2 block to A; the enemy hits A for 10 (2 blocked).
        Engine.Handle(new BlockGained(A, C, 2, new Source(SourceKind.Orb, "FROST_ORB")));
        EnemyHits(A, 2, 8);
        Assert.Equal(2, Combat(IdC).BlockGivenToAllies);
        Assert.Equal(2, Combat(IdC).PreventedForAllies);
        Assert.Equal(0, Combat(IdA).BlockGainedSelf);
    }
}
