using KikiMAIster.Core;
using Xunit;

namespace KikiMAIster.Core.Tests;

/// 1.2.1: the Fool's arrows (spec 01-metrics "The Fool's arrows").
public class FoolArrowsTests
{
    private static readonly Actor A = Actor.Player(1, 1);
    private static readonly object Enemy = new();
    private static readonly object Other = new();
    private int _nextId;

    private static TurnCard Card(string id, int cost = 1, int damage = 0, bool noArrow = false, bool shot = false,
        CardKind kind = CardKind.Attack, bool block = false) => new()
    {
        Ref = new object(), Key = $"Card:{id}", Kind = kind, Cost = cost, HitsEnemies = kind == CardKind.Attack, NoArrow = noArrow, Shot = shot,
        GainsBlock = block, Block = block ? 5 : 0,
        DamageOn = damage > 0 ? new Dictionary<object, int>(ReferenceEqualityComparer.Instance) { [Enemy] = damage } : new Dictionary<object, int>()
    };

    private TurnPlay Play(string id, int energy = 1, CardKind kind = CardKind.Attack, object? target = null, int hits = 0, int raw = 6,
        params object[] inHand) => Play(id, energy, kind, target, hits, raw, 1, inHand);

    private TurnPlay Play(string id, int energy, CardKind kind, object? target, int hits, int raw, int arrows, params object[] inHand)
    {
        var p = new TurnPlay
        {
            Id = ++_nextId, Owner = A, CardRef = new object(), CardKey = $"Card:{id}", Kind = kind, EnergySpent = energy, Target = target,
            HandAtPlay = inHand.ToList(), ArrowsReady = arrows, KindCount = _nextId
        };
        for (var i = 0; i < hits; i++) p.Hits.Add(new EnemyHit(target ?? Enemy, raw, -1));
        return p;
    }

    private static List<MissedChance> Missed(TurnResult r, string kind) => r.Events.OfType<MissedChance>().Where(m => m.Kind == kind).ToList();

    private static TurnState State(TurnPlayer player, params TurnPlay[] plays)
    {
        var s = new TurnState { Players = { player }, Plays = plays.ToList() };
        s.EnemiesLeft[Enemy] = 40;
        s.EnemiesLeft[Other] = 40;
        return s;
    }

    private static HashSet<object> Set(params object[] items) => new(items, ReferenceEqualityComparer.Instance);

    [Fact]
    public void RedShot_IsNeverAFreeCard_NorABlockAlternative_NorTheStrongerAttack()
    {
        var quick = Card("QUICK_SHOT", cost: 0, damage: 10, noArrow: true, shot: true);
        var defend = Play("DEFEND", kind: CardKind.Skill);
        defend.GaveBlock = true;
        defend.UsefulCardsInHand.Add(new SeenCard(quick.Ref, quick.Key));
        var strike = Play("STRIKE", target: Enemy, hits: 1);
        strike.Stronger = new Better(quick.Ref, quick.Key, 10, 6);
        var r = TurnReview.Resolve(State(new TurnPlayer { Actor = A, Hand = { quick } }, defend, strike));
        Assert.Empty(Missed(r, "freeCard"));
        Assert.DoesNotContain(r.Events.OfType<BlockPlayResolved>(), b => b.HadAlternative);
        Assert.Empty(r.Events.OfType<WeakAttack>());
    }

    [Fact]
    public void LethalLeft_NoMoreShotsThanArrows()
    {
        TurnState S(int arrows)
        {
            var player = new TurnPlayer
            {
                Actor = A, Energy = 2, ArrowsReady = arrows,
                Hand = { Card("FOOLISH_SHOT", damage: 15, shot: true), Card("FOOLISH_SHOT", damage: 15, shot: true) }
            };
            var s = new TurnState { Players = { player }, LastEnemy = Enemy };
            s.EnemiesLeft[Enemy] = 25;
            return s;
        }
        Assert.Empty(TurnReview.Resolve(S(1)).Events.OfType<MissedLethal>());
        Assert.Single(TurnReview.Resolve(S(2)).Events.OfType<MissedLethal>());
    }

    [Fact]
    public void FrontHandLethal_NoMoreShotsThanArrows()
    {
        var hand = new[] { new Lethal.Attack("A", 1, false, new[] { 15 }, Shot: true), new Lethal.Attack("B", 1, false, new[] { 15 }, Shot: true) };
        Assert.Null(Lethal.Find(hand, 2, new[] { 25 }, maxShots: 1));
        Assert.NotNull(Lethal.Find(hand, 2, new[] { 25 }, maxShots: 2));
    }

    [Fact]
    public void Nail_OnATargetWithoutArrow_DidNothing_UnlessItCompletedARelic()
    {
        var nail = Play("NAIL", 2, target: Enemy);
        nail.NotImpaled = true;
        var r = TurnReview.Resolve(State(new TurnPlayer { Actor = A }, nail));
        Assert.Equal(new[] { "Card:NAIL", "2" }, Missed(r, "notImpaled").Single().Args);
        Assert.Equal(2, r.Events.OfType<HelpGiven>().Where(h => h.Kind == "energyUseless").Sum(h => h.Amount));
        // Third attack of the turn with Kunai: it triggered the relic.
        nail.KindCount = 3;
        var kunai = new TurnPlayer { Actor = A, Relics = { new TurnRelic("Relic:KUNAI", RelicTrigger.Attacks, 3) } };
        Assert.Empty(Missed(TurnReview.Resolve(State(kunai, nail)), "notImpaled"));
    }

    [Fact]
    public void WahHammer_BeforeTheShotThatImpaledItsTarget()
    {
        var shotCard = new object();
        var hammer = Play("WAH_HAMMER", target: Enemy, hits: 1, inHand: shotCard);
        var shot = new TurnPlay
        {
            Id = ++_nextId, Owner = A, CardRef = shotCard, CardKey = "Card:FOOLISH_SHOT", Kind = CardKind.Attack, EnergySpent = 1, Target = Enemy,
            ShotArrow = true, ArrowsReady = 1
        };
        var r = TurnReview.Resolve(State(new TurnPlayer { Actor = A }, hammer, shot));
        Assert.Equal(new[] { "Card:WAH_HAMMER", "Card:FOOLISH_SHOT" }, Missed(r, "impaleFirst").Single().Args);
        // No arrow ready when the Hammer was played: no other way round.
        hammer.ArrowsReady = 0;
        Assert.Empty(Missed(TurnReview.Resolve(State(new TurnPlayer { Actor = A }, hammer, shot)), "impaleFirst"));
    }

    [Fact]
    public void Nail_BeforeTheShot_IsImpaleFirst_NotNotImpaled()
    {
        var shotCard = new object();
        var nail = Play("NAIL", 2, target: Enemy, inHand: shotCard);
        nail.NotImpaled = true;
        var shot = new TurnPlay
        {
            Id = ++_nextId, Owner = A, CardRef = shotCard, CardKey = "Card:FOOLISH_SHOT", Kind = CardKind.Attack, EnergySpent = 1, Target = Enemy,
            ShotArrow = true, ArrowsReady = 1
        };
        var r = TurnReview.Resolve(State(new TurnPlayer { Actor = A }, nail, shot));
        Assert.Single(Missed(r, "impaleFirst"));
        Assert.Empty(Missed(r, "notImpaled"));
    }

    [Fact]
    public void SerratedArrows_AttackBeforeTheImpalingShot_LostItsBonus()
    {
        var shotCard = new object();
        var strike = Play("STRIKE", target: Enemy, hits: 1, raw: 8, inHand: shotCard);
        strike.SerratedBonus = 0.25;
        var shot = new TurnPlay
        {
            Id = ++_nextId, Owner = A, CardRef = shotCard, CardKey = "Card:FOOLISH_SHOT", Kind = CardKind.Attack, EnergySpent = 1, Target = Enemy,
            ShotArrow = true, ArrowsReady = 1
        };
        var r = TurnReview.Resolve(State(new TurnPlayer { Actor = A }, strike, shot));
        Assert.Equal(new[] { "Card:STRIKE", "2", "0", "Card:FOOLISH_SHOT" }, Missed(r, "orderTurn").Single().Args);
    }

    [Fact]
    public void ShootFirst_ThreeHitsBeforeTheShot_WouldHaveFreedTheArrow()
    {
        TurnResult Run(int twinHits, bool stuck)
        {
            _nextId = 0;
            var shotCard = new object();
            var strike = Play("STRIKE", target: Enemy, hits: 1, inHand: shotCard);
            var twin = Play("TWIN_STRIKE", target: Enemy, hits: twinHits, inHand: shotCard);
            var shot = new TurnPlay
            {
                Id = ++_nextId, Owner = A, CardRef = shotCard, CardKey = "Card:FOOLISH_SHOT", Kind = CardKind.Attack, EnergySpent = 1, Target = Enemy,
                ShotArrow = true, ArrowsReady = 1
            };
            var player = new TurnPlayer { Actor = A, OwnArrowIn = stuck ? Set(Enemy) : Set() };
            return TurnReview.Resolve(State(player, strike, twin, shot));
        }
        Assert.Equal(new[] { "Card:FOOLISH_SHOT", "Card:STRIKE+Card:TWIN_STRIKE", "3" }, Missed(Run(2, true), "shootFirst").Single().Args);
        Assert.Empty(Missed(Run(1, true), "shootFirst"));   // 2 hits: not enough
        Assert.Empty(Missed(Run(2, false), "shootFirst"));  // the arrow came back anyway
    }

    [Fact]
    public void ChoFirm_AfterTheShotThatTookTheLastArrow_LostABlock()
    {
        var choCard = new object();
        var shot = Play("FOOLISH_SHOT", target: Enemy, hits: 1, inHand: choCard);
        shot.ShotArrow = true;
        var cho = new TurnPlay
        {
            Id = ++_nextId, Owner = A, CardRef = choCard, CardKey = "Card:CHO_FIRM", Kind = CardKind.Skill, EnergySpent = 1, ArrowsReady = 0,
            GaveBlock = true, BlockAmount = 6, BlockGains = 1
        };
        var r = TurnReview.Resolve(State(new TurnPlayer { Actor = A, Block = 6, Incoming = 20 }, shot, cho));
        Assert.Equal(new[] { "Card:FOOLISH_SHOT", "0", "6", "Card:CHO_FIRM" }, Missed(r, "orderTurn").Single().Args);
        // The block was enough: nothing lost.
        Assert.Empty(Missed(TurnReview.Resolve(State(new TurnPlayer { Actor = A, Block = 6, Incoming = 5 }, shot, cho)), "orderTurn"));
    }

    [Fact]
    public void WhistlingArrows_AfterAShot_LostOneMorePlay()
    {
        var whistleCard = new object();
        var shot = Play("FOOLISH_SHOT", target: Enemy, hits: 1, inHand: whistleCard);
        shot.ShotArrow = true;
        var whistle = new TurnPlay
        {
            Id = ++_nextId, Owner = A, CardRef = whistleCard, CardKey = "Card:WHISTLING_ARROWS", Kind = CardKind.Skill, EnergySpent = 0,
            GaveBlock = true, BlockAmount = 3, BlockGains = 1
        };
        var r = TurnReview.Resolve(State(new TurnPlayer { Actor = A, Block = 3, Incoming = 10 }, shot, whistle));
        Assert.Equal(new[] { "Card:FOOLISH_SHOT", "0", "3", "Card:WHISTLING_ARROWS" }, Missed(r, "orderTurn").Single().Args);
    }

    [Fact]
    public void Aim_SpentOnFoolishShot_WhileHeavyShotCameAfter()
    {
        var heavyCard = new object();
        var foolish = Play("FOOLISH_SHOT", target: Enemy, hits: 1, raw: 21, inHand: heavyCard);
        foolish.ShotArrow = true;
        foolish.AimAtStart = 6;
        var heavy = new TurnPlay
        {
            Id = ++_nextId, Owner = A, CardRef = heavyCard, CardKey = "Card:HEAVY_SHOT", Kind = CardKind.Attack, EnergySpent = 2, Target = Enemy,
            ShotArrow = true, AimFactor = 2
        };
        heavy.Hits.Add(new EnemyHit(Enemy, 20, -1));
        var r = TurnReview.Resolve(State(new TurnPlayer { Actor = A }, foolish, heavy));
        Assert.Equal(new[] { "Card:FOOLISH_SHOT", "6", "0", "Card:HEAVY_SHOT" }, Missed(r, "orderTurn").Single().Args);
        // Heavy Shot had Aim again (Nock): nothing lost.
        heavy.AimAtStart = 5;
        Assert.Empty(Missed(TurnReview.Resolve(State(new TurnPlayer { Actor = A }, foolish, heavy)), "orderTurn"));
    }

    [Fact]
    public void Quietus_WithNoShotAfter_IsLost()
    {
        var quietus = Play("QUIETUS", kind: CardKind.Skill);
        quietus.HadOtherEffect = true;
        quietus.HadNonDamageEffect = true;
        quietus.PowersGiven.Add("Power:QUIETUS_POWER");
        var player = new TurnPlayer { Actor = A, UnusedBuffs = { "Power:QUIETUS_POWER" } };
        var r = TurnReview.Resolve(State(player, quietus));
        Assert.Equal(new[] { "Card:QUIETUS", "1" }, Missed(r, "buffUnused").Single().Args);
        Assert.Equal(1, r.Events.OfType<HelpGiven>().Where(h => h.Kind == "energyUseless").Sum(h => h.Amount));
        Assert.Empty(Missed(TurnReview.Resolve(State(new TurnPlayer { Actor = A }, quietus)), "buffUnused"));
    }

    [Fact]
    public void ExpectAFight_CountsNoMoreShotsThanArrows()
    {
        // 3 energy, Paré au combat (3) kept, 2 Foolish Shots (1) in hand, 1 arrow: first, it paid for itself and only 1 Shot.
        var cut = new TurnCard { Ref = new object(), Key = "Card:EXPECT_A_FIGHT", Kind = CardKind.Skill, Cost = 3, GainsBlock = true };
        TurnPlayer Player(int arrows) => new()
        {
            Actor = A, Energy = 3, AttackDiscount = cut, ArrowsReady = arrows,
            Hand = { cut, Card("FOOLISH_SHOT", damage: 15, shot: true), Card("FOOLISH_SHOT", damage: 15, shot: true) }
        };
        Assert.Equal(new[] { "Card:EXPECT_A_FIGHT", "2" }, Missed(TurnReview.Resolve(State(Player(2))), "attackDiscount").Single().Args);
        Assert.Equal(new[] { "Card:EXPECT_A_FIGHT", "1" }, Missed(TurnReview.Resolve(State(Player(1))), "attackDiscount").Single().Args);
    }

}

/// 1.2.1 (inbox of 2026-11-13): co-op arrows, card makers first, Feu de couverture late.
public class FoolCoopTests
{
    private static readonly Actor A = Actor.Player(1, 1);
    private static readonly Actor B = Actor.Player(2, 1);
    private static readonly object Enemy = new();
    private int _id;

    private TurnPlay Play(Actor owner, string id, int energy = 1, CardKind kind = CardKind.Attack, object? target = null, int energyBefore = 3,
        params object[] inHand)
    {
        var p = new TurnPlay
        {
            Id = ++_id, Owner = owner, CardRef = new object(), CardKey = $"Card:{id}", Kind = kind, EnergySpent = energy, Target = target,
            HandAtPlay = inHand.ToList(), EnergyBefore = energyBefore
        };
        if (target != null && kind == CardKind.Attack) p.Hits.Add(new EnemyHit(target, 8, -1));
        return p;
    }

    private static List<MissedChance> Missed(TurnResult r, string kind) => r.Events.OfType<MissedChance>().Where(m => m.Kind == kind).ToList();

    private static TurnState State(IEnumerable<TurnPlayer> players, params TurnPlay[] plays)
    {
        var s = new TurnState { Players = players.ToList(), Plays = plays.ToList() };
        s.EnemiesLeft[Enemy] = 50;
        return s;
    }

    [Fact]
    public void AllysShotImpaledTheTargetAfterMyBowSmack_AndMySerratedAttack()
    {
        var allyShotCard = new object();
        var smack = Play(A, "BOW_SMACK", target: Enemy);
        smack.AlliesHandAtPlay.Add(allyShotCard);
        smack.SerratedBonus = 0.5;
        var shot = new TurnPlay
        {
            Id = ++_id, Owner = B, CardRef = allyShotCard, CardKey = "Card:FOOLISH_SHOT", Kind = CardKind.Attack, EnergySpent = 1, Target = Enemy,
            ShotArrow = true, ArrowsReady = 1
        };
        var r = TurnReview.Resolve(State(new[] { new TurnPlayer { Actor = A }, new TurnPlayer { Actor = B } }, smack, shot));
        var miss = Missed(r, "impaleFirst").Single();
        Assert.Equal(A, miss.Player);
        Assert.Equal(new[] { "Card:BOW_SMACK", "Card:FOOLISH_SHOT|2" }, miss.Args);
        Assert.Equal(new[] { "Card:BOW_SMACK", "4", "0", "Card:FOOLISH_SHOT|2" }, Missed(r, "orderTurn").Single().Args);
    }

    [Fact]
    public void Abundance_PlayedAfterOtherCards()
    {
        var abundanceCard = new object();
        var strike = Play(A, "STRIKE", target: Enemy, energyBefore: 3, inHand: abundanceCard);
        var abundance = new TurnPlay
        {
            Id = ++_id, Owner = A, CardRef = abundanceCard, CardKey = "Card:ABUNDANCE^", Kind = CardKind.Skill, EnergySpent = 0, EnergyBefore = 2
        };
        var r = TurnReview.Resolve(State(new[] { new TurnPlayer { Actor = A } }, strike, abundance));
        Assert.Equal(new[] { "Card:ABUNDANCE^", "Card:STRIKE" }, Missed(r, "makerFirst").Single().Args);
        // A card that drew may come first.
        strike.CardsDrawn = 2;
        Assert.Empty(Missed(TurnReview.Resolve(State(new[] { new TurnPlayer { Actor = A } }, strike, abundance)), "makerFirst"));
    }

    [Fact]
    public void CoveringFire_PlayedAfterAShot_LostTheAllysBlock()
    {
        var cfCard = new object();
        var shot = Play(A, "FOOLISH_SHOT", target: Enemy, inHand: cfCard);
        shot.ShotArrow = true;
        var cf = new TurnPlay
        {
            Id = ++_id, Owner = A, CardRef = cfCard, CardKey = "Card:COVERING_FIRE^", Kind = CardKind.Power, EnergySpent = 0, AllyBlockPerShot = 11
        };
        var players = new[] { new TurnPlayer { Actor = A }, new TurnPlayer { Actor = B, Block = 5, Incoming = 12 } };
        Assert.Equal(new[] { "Card:COVERING_FIRE^", "Card:FOOLISH_SHOT", "7" }, Missed(TurnReview.Resolve(State(players, shot, cf)), "coveringFireLate").Single().Args);
        var covered = new[] { new TurnPlayer { Actor = A }, new TurnPlayer { Actor = B, Block = 12, Incoming = 12 } };
        Assert.Empty(Missed(TurnReview.Resolve(State(covered, shot, cf)), "coveringFireLate"));
    }
}
