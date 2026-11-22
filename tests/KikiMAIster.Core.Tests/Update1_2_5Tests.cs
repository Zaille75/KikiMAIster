using KikiMAIster.Core;
using Xunit;

namespace KikiMAIster.Core.Tests;

/// 1.2.5: Godot's waits, and the feedback of 2026-11-22 (Compostage, one Erreur per card, Toupie, L'Insensé, Fatalité).
public class Update1_2_5Tests
{
    private static readonly Actor A = Actor.Player(1, 1);
    private static readonly Actor B = Actor.Player(2, 2);
    private static readonly object Enemy = new();

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 0)]
    [InlineData(5, 0)]
    [InlineData(5.5, 0.5)]
    [InlineData(32, 27)]
    public void EndTurnWait_DropsTheFirstFiveSeconds(double gap, double counted) =>
        Assert.Equal(counted, WaitRules.EndTurnWait(gap), 3);

    [Fact]
    public void MapWait_NoDrawing_IsTheWholeGap() =>
        Assert.Equal(20, WaitRules.MapWait(10, 30, Array.Empty<(double, double)>()), 3);

    [Fact]
    public void MapWait_RemovesStrokesInsideTheGap() =>
        Assert.Equal(12, WaitRules.MapWait(10, 30, new (double, double)[] { (12, 15), (20, 25) }), 3);

    [Fact]
    public void MapWait_ClipsStrokesToTheGap() =>
        Assert.Equal(14, WaitRules.MapWait(10, 30, new (double, double)[] { (5, 12), (26, 40) }), 3);

    [Fact]
    public void MapWait_IgnoresStrokesOutsideTheGap() =>
        Assert.Equal(20, WaitRules.MapWait(10, 30, new (double, double)[] { (1, 5), (31, 35) }), 3);

    [Fact]
    public void MapWait_CountsOverlapsOnce() =>
        Assert.Equal(14, WaitRules.MapWait(10, 30, new (double, double)[] { (12, 16), (14, 18) }), 3);

    [Fact]
    public void MapWait_DrawingTheWholeGap_IsZero() =>
        Assert.Equal(0, WaitRules.MapWait(10, 30, new (double, double)[] { (9, 31) }), 3);

    private static TurnCard Strike(int cost = 1) => new()
    {
        Ref = new object(), Key = "Card:STRIKE_FOOL", Kind = CardKind.Attack, Cost = cost, HitsEnemies = true,
        DamageOn = new Dictionary<object, int> { [Enemy] = 6 }
    };

    private static TurnCard Defend(int block = 5) => new()
    {
        Ref = new object(), Key = "Card:DEFEND_FOOL", Kind = CardKind.Skill, Cost = 1, GainsBlock = true, Block = block
    };

    private static TurnPlayer Player(int energy, params TurnCard[] hand) => new() { Actor = A, Energy = energy, Hand = hand.ToList() };

    private static TurnState State(TurnPlayer player, bool doomed = false, params TurnPlay[] plays)
    {
        var s = new TurnState { Players = { player }, Plays = plays.ToList(), EnemiesDoomed = doomed };
        s.EnemiesLeft[Enemy] = 100;
        return s;
    }

    private static List<MissedChance> Missed(TurnResult r, string kind) => r.Events.OfType<MissedChance>().Where(m => m.Kind == kind).ToList();

    [Fact]
    public void EnergyLeft_CountsOneErreurPerCardItCouldPay()
    {
        var r = TurnReview.Resolve(State(Player(4, Strike(), Strike())));
        var miss = Assert.Single(Missed(r, "energyCard"));
        Assert.Equal(2, miss.Amount);
        Assert.Equal(new[] { "2", "4", "Card:STRIKE_FOOL+Card:STRIKE_FOOL" }, miss.Args);
        Assert.Equal(4, Assert.Single(Missed(r, "energy")).Amount);
    }

    [Fact]
    public void EnergyLeft_CountsOnlyWhatItCanPay()
    {
        var r = TurnReview.Resolve(State(Player(1, Strike(), Strike())));
        Assert.Equal(1, Assert.Single(Missed(r, "energyCard")).Amount);
    }

    [Fact]
    public void EnergyLeft_StopsCountingBlockOnceTheAttackIsCovered()
    {
        var player = new TurnPlayer { Actor = A, Energy = 3, Incoming = 5, Hand = new() { Defend(), Defend(), Defend() } };
        var r = TurnReview.Resolve(State(player));
        Assert.Equal(1, Assert.Single(Missed(r, "energyCard")).Amount);
    }

    [Fact]
    public void EnemiesDoomed_AttacksLeftAreNoMistake()
    {
        var r = TurnReview.Resolve(State(Player(2, Strike(), Strike()), doomed: true));
        Assert.Empty(Missed(r, "energy"));
        Assert.Empty(Missed(r, "energyCard"));
    }

    [Fact]
    public void EnemiesDoomed_NotImpaledIsNoMistake()
    {
        var uproot = new TurnPlay { Id = 1, Owner = A, CardRef = new object(), CardKey = "Card:UPROOT", Kind = CardKind.Skill, EnergySpent = 1, NotImpaled = true };
        Assert.Empty(Missed(TurnReview.Resolve(State(Player(0, Strike()), true, uproot)), "notImpaled"));
        Assert.Single(Missed(TurnReview.Resolve(State(Player(0, Strike()), false, uproot)), "notImpaled"));
    }

    [Fact]
    public void EnemiesDoomed_AttackIsNoAlternativeToBlock()
    {
        var strike = Strike();
        var block = new TurnPlay
        {
            Id = 1, Owner = A, CardRef = new object(), CardKey = "Card:DEFEND_FOOL", Kind = CardKind.Skill, EnergySpent = 1, GaveBlock = true, BlockAmount = 5,
            BlockGains = 1, UsefulCardsInHand = new() { new SeenCard(strike.Ref, strike.Key) }, HandAtPlay = new() { strike.Ref }
        };
        var resolved = TurnReview.Resolve(State(Player(0, strike), true, block)).Events.OfType<BlockPlayResolved>().Single();
        Assert.False(resolved.HadAlternative);
    }

    [Fact]
    public void TopCard_LeftInHandIsAMiss_EvenIfItExhausts()
    {
        var card = new TurnCard { Ref = new object(), Key = "Card:BRIGHTEN_UP^", Kind = CardKind.Skill, Cost = 1, KeptOnPurpose = true, Exhausts = true, DrawnByTop = true };
        var miss = Assert.Single(Missed(TurnReview.Resolve(State(Player(1, card))), "topForgotten"));
        Assert.Equal(new[] { "Card:BRIGHTEN_UP^", "Relic:UNCEASING_TOP" }, miss.Args);
    }

    [Fact]
    public void TopCard_NotAffordable_IsNoMiss()
    {
        var card = new TurnCard { Ref = new object(), Key = "Card:BRIGHTEN_UP", Kind = CardKind.Skill, Cost = 1, KeptOnPurpose = true, DrawnByTop = true };
        Assert.Empty(Missed(TurnReview.Resolve(State(Player(0, card))), "topForgotten"));
    }

    [Fact]
    public void ExhaustCard_NotFromTheTop_StaysKeptOnPurpose()
    {
        var card = new TurnCard { Ref = new object(), Key = "Card:BRIGHTEN_UP", Kind = CardKind.Skill, Cost = 1, KeptOnPurpose = true };
        var r = TurnReview.Resolve(State(Player(1, card)));
        Assert.Empty(Missed(r, "topForgotten"));
        Assert.Empty(Missed(r, "energy"));
    }

    [Fact]
    public void Fool_WithAnEmptyHand_IsWasted()
    {
        var fool = new TurnPlay { Id = 1, Owner = A, CardRef = new object(), CardKey = "Card:THE_FOOL", Kind = CardKind.Skill, NoCardToAutoPlay = true };
        var miss = Assert.Single(Missed(TurnReview.Resolve(State(Player(0), false, fool)), "foolEmptyHand"));
        Assert.Contains("foolEmptyHand", PlayerStats.WastedKinds);
        Assert.Equal("Card:THE_FOOL", miss.Args[0]);
    }

    [Fact]
    public void KillBeforeCompost_GoesToTheKiller_NamingTheAllysCard()
    {
        var killer = new TurnPlayer { Actor = B, Energy = 0 };
        var s = new TurnState { Players = { Player(0), killer } };
        s.KillsBeforeCompost.Add(new KillBeforeCompost(B, "Card:FOOLISH_SHOT", "Card:COMPOST", A, 2));
        var miss = Assert.Single(Missed(TurnReview.Resolve(s), "compostLate"));
        Assert.Equal(B, miss.Player);
        Assert.Equal(new[] { "Card:FOOLISH_SHOT", "Card:COMPOST|1", "2" }, miss.Args);
        Assert.Contains("compostLate", PlayerStats.ChoiceKinds);
    }
}
