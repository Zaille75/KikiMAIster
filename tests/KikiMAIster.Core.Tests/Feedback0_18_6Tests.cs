using KikiMAIster.Core;

namespace KikiMAIster.Core.Tests;

/// v0.18.6: Doom that killed nothing, a turn at 1 HP that really ended at 1 HP.
public class Feedback0_18_6Tests : TestBase
{
    private static readonly Source DoomDot = new(SourceKind.DamageOverTime, "DOOM_POWER");

    [Fact]
    public void Doom_OnAnEnemyKilledAnotherWay_IsUseless_WithTheEnergyOfDoomOnlyCards()
    {
        Power(A, Enemy, "DOOM_POWER", 7);
        Engine.Handle(new DoomPlayed(A, "Card:NEGATIVE_PULSE", 1, new[] { Enemy }));
        Hit(A, Enemy, 0, 9, killed: true);
        var s = Combat(IdA);
        Assert.Equal(7, s.DoomApplied);
        Assert.Equal(7, s.DoomUseless);
        Assert.Equal(1, s.EnergyUseless);
    }

    [Fact]
    public void Doom_ThatKills_IsUseful()
    {
        Power(A, Enemy, "DOOM_POWER", 30);
        Engine.Handle(new DoomPlayed(A, "Card:DOOM", 2, new[] { Enemy }));
        Engine.Handle(new DamageDealt(Actor.None, Enemy, 0, 29, 0, true, DoomDot));
        Engine.Handle(new CombatEnded(true));
        var s = Engine.Get(Scope.Run, IdA);
        Assert.Equal(0, s.DoomUseless);
        Assert.Equal(0, s.EnergyUseless);
    }

    [Fact]
    public void DoomOnTwoEnemies_UselessOnlyWhenNoneDiesOfIt()
    {
        Power(A, Enemy, "DOOM_POWER", 5);
        Power(A, Enemy2, "DOOM_POWER", 5);
        Engine.Handle(new DoomPlayed(A, "Card:DOOM_ALL", 2, new[] { Enemy, Enemy2 }));
        Hit(A, Enemy, 0, 9, killed: true);
        Assert.Equal(0, Combat(IdA).EnergyUseless);
        Engine.Handle(new DamageDealt(Actor.None, Enemy2, 0, 5, 0, true, DoomDot));
        Engine.Handle(new CombatEnded(true));
        var s = Engine.Get(Scope.Run, IdA);
        Assert.Equal(5, s.DoomUseless);
        Assert.Equal(0, s.EnergyUseless);
    }

    [Fact]
    public void Doom_LeftAtTheEndOfTheFight_IsUseless()
    {
        Power(A, Enemy, "DOOM_POWER", 4);
        Engine.Handle(new CombatEnded(false));
        Assert.Equal(4, Engine.Get(Scope.Run, IdA).DoomUseless);
    }

    [Fact]
    public void TurnAtOneHp_HealedOrEndingTheFight()
    {
        Engine.Handle(new PlayerRoundState(A, 1, true, false));
        Engine.Handle(new Healed(A, A, 3));
        Engine.Handle(new RoundStarted(2));
        Engine.Handle(new PlayerRoundState(A, 1, true, false));
        Assert.Equal(0, Combat(IdA).TurnsAtOneHp);
        // Won at 1 HP: the turn ended at 1 HP.
        Engine.Handle(new CombatEnded(true));
        Assert.Equal(1, Engine.Get(Scope.Run, IdA).TurnsAtOneHp);
    }
}

/// v0.18.6: wrong order counted once per card, Ruée, draws stopped by Pas de pioche, full hand, surplus energy.
public class TurnReview0_18_6Tests
{
    private static readonly Actor A = Actor.Player(1, 1);
    private static readonly object Enemy = new();
    private int _nextId;

    private static TurnCard Card(string id, CardKind kind = CardKind.Attack, int cost = 1, int block = 0, bool kept = false) => new()
    {
        Ref = new object(), Key = $"Card:{id}", Kind = kind, Cost = cost, GainsBlock = block > 0, Block = block, KeptOnPurpose = kept,
        HitsEnemies = kind == CardKind.Attack
    };

    private TurnPlay Play(string id, CardKind kind = CardKind.Attack, int energy = 1, int enemies = 1, IEnumerable<object>? hand = null,
        object? card = null) => new()
    {
        Id = ++_nextId, Owner = A, CardRef = card ?? new object(), CardKey = $"Card:{id}", Kind = kind, EnergySpent = energy,
        EnemiesAlive = enemies, EnergyBefore = 3, HandAtPlay = (hand ?? Array.Empty<object>()).ToList()
    };

    private static TurnResult Resolve(TurnPlayer player, IEnumerable<TurnPlay> plays, IEnumerable<OrderLoss>? losses = null)
    {
        var s = new TurnState { Players = { player }, Plays = plays.ToList() };
        if (losses != null) s.OrderLosses.AddRange(losses);
        s.EnemiesLeft[Enemy] = 30;
        return TurnReview.Resolve(s);
    }

    private static List<MissedChance> Missed(TurnResult r, string kind) => r.Events.OfType<MissedChance>().Where(m => m.Kind == kind).ToList();

    [Fact]
    public void WrongOrder_OneMissPerEarlierCard_AndNotForTheCardsItsPowerFed()
    {
        // Décomposition: Lames fantomatiques before Précipitation (1 damage, 1 block) and Surin (it boosted the Surin).
        var phantom = Play("PHANTOM_BLADES", CardKind.Power);
        phantom.PowersGiven.Add("Power:PHANTOM_BLADES_POWER");
        var dash = Play("DASH");
        var shiv = Play("SHIV", energy: 0);
        shiv.PowersTriggered.Add("Power:PHANTOM_BLADES_POWER");
        var player = new TurnPlayer { Actor = A, Block = 12, Incoming = 18 };
        var r = Resolve(player, new[] { phantom, dash, shiv }, new[]
        {
            new OrderLoss(phantom, "Card:DASH", "orderBlock", null, 1, dash),
            new OrderLoss(phantom, "Card:DASH", "order", Enemy, 1, dash),
            new OrderLoss(phantom, "Card:SHIV", "order", Enemy, 1, shiv)
        });
        var miss = Missed(r, "orderTurn").Single();
        Assert.Equal(new[] { "Card:PHANTOM_BLADES", "1", "1", "Card:DASH" }, miss.Args);
        Assert.Equal(1, miss.Amount);
    }

    [Fact]
    public void Stampede_LastAttackPlayedByHand_WithACardLeftForItsEnergy()
    {
        var bloodWall = Card("BLOOD_WALL", CardKind.Skill, cost: 2, block: 16, kept: true);
        var setup = Play("SETUP_STRIKE");
        var player = new TurnPlayer
        {
            Actor = A, Energy = 1, Block = 15, Incoming = 17, Hand = { bloodWall }, StampedeShort = 1, StampedeKey = "Power:STAMPEDE_POWER"
        };
        var miss = Missed(Resolve(player, new[] { setup }), "stampede").Single();
        Assert.Equal(new[] { "Card:SETUP_STRIKE", "Power:STAMPEDE_POWER", "Card:BLOOD_WALL" }, miss.Args);
        // Two enemies: Ruée could have hit the other one.
        Assert.Empty(Missed(Resolve(player, new[] { Play("SETUP_STRIKE", enemies: 2) }), "stampede"));
        // Block already enough: nothing better to do with the energy.
        var covered = new TurnPlayer { Actor = A, Energy = 1, Block = 20, Incoming = 17, Hand = { bloodWall }, StampedeShort = 1 };
        Assert.Empty(Missed(Resolve(covered, new[] { Play("SETUP_STRIKE") }), "stampede"));
    }

    [Fact]
    public void DrawBlocked_MissedWhenItsEnergyWasLeft()
    {
        var offering = Play("OFFERING", CardKind.Skill, energy: 0);
        offering.DrawBlocked = true;
        offering.EnergyGiven = 2;
        offering.HadOtherEffect = true;
        Assert.Single(Missed(Resolve(new TurnPlayer { Actor = A, Energy = 2 }, new[] { offering }), "noDraw"));
        Assert.Empty(Missed(Resolve(new TurnPlayer { Actor = A, Energy = 1 }, new[] { offering }), "noDraw"));
    }

    [Fact]
    public void FullHand_CardPlayedAfter_CouldHaveMadeRoom()
    {
        var defendRef = new object();
        var sleeve = Play("UP_MY_SLEEVE", CardKind.Skill, energy: 2);
        sleeve.LostToFullHand = 2;
        sleeve.HandWhenFull.Add(defendRef);
        var defend = Play("DEFEND", CardKind.Skill, card: defendRef);
        var shiv = Play("SHIV", energy: 0);
        var miss = Missed(Resolve(new TurnPlayer { Actor = A }, new[] { sleeve, shiv, defend }), "handFull").Single();
        Assert.Equal(new[] { "Card:UP_MY_SLEEVE", "1", "Card:DEFEND" }, miss.Args);
        Assert.Equal(1, miss.Amount);
    }

    [Fact]
    public void EnergyLeft_WithOnlyCardsKeptOnPurpose_IsSurplus()
    {
        var r = Resolve(new TurnPlayer { Actor = A, Energy = 1, Hand = { Card("BLADE_DANCE", kept: true) } }, Array.Empty<TurnPlay>());
        Assert.Empty(Missed(r, "energy"));
        Assert.Equal(1, r.Events.OfType<HelpGiven>().Single(h => h.Kind == "energySurplus").Amount);
    }

    [Fact]
    public void UselessBlock_WithNothingElseToPlay_IsSurplus()
    {
        var defend = Play("DEFEND", CardKind.Skill);
        defend.GaveBlock = true;
        defend.BlockAmount = 3;
        var r = Resolve(new TurnPlayer { Actor = A, Block = 17, Incoming = 0 }, new[] { defend });
        Assert.DoesNotContain(r.Events.OfType<HelpGiven>(), h => h.Kind == "energyUseless");
        Assert.Equal(1, r.Events.OfType<HelpGiven>().Single(h => h.Kind == "energySurplus").Amount);
    }
}
