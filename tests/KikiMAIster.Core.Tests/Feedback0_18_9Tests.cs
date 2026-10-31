using KikiMAIster.Core;

namespace KikiMAIster.Core.Tests;

/// v0.18.9 (co-op runs of 2026-10-31): stats side.
public class Feedback0_18_9Tests : TestBase
{
    [Fact]
    public void EnemyChangingForm_ClearsTheDebuffQueue()
    {
        // Cobaye: B's Weak turns are gone with its first form; the Weak A put on the new form saved C's HP.
        Power(B, Enemy, "WEAK_POWER", 3);
        Engine.Handle(new PowersCleared(Enemy, new[] { "WEAK_POWER" }));
        Power(A, Enemy, "WEAK_POWER", 2);
        Engine.Handle(new DebuffEffect(Enemy, "WEAK_POWER", C, 3, Offense: false, Credit: DebuffCredit.CurrentTurn));
        EnemyHits(C, 0, 9);
        Assert.Equal(3, Combat(IdA).SavedForAllies);
        Assert.Equal(0, Combat(IdB).SavedForAllies);
    }

    [Fact]
    public void BlockKept_NotCounted_WhenTheAttackerDiedOfTheirReaction()
    {
        // Titos gives its damage back: the enemy died before its second hit, which a block card would have changed.
        Engine.Handle(new BlockCardKept(A, "Card:DEFEND", 5));
        EnemyHits(A, 0, 4);
        Engine.Handle(new DamageDealt(A, Enemy, 0, 8, 0, true, Source.Power("NECRO_MASTERY_POWER")));
        Engine.Handle(new RoundStarted(2));
        Assert.Equal(0, Combat(IdA).AvoidableHp);
    }

    [Fact]
    public void Accuracy_BonusCountsAsPowerDamage()
    {
        Hit(A, Enemy, 0, 10, card: "SHIV");
        Engine.Handle(new DamageModifier(A, "Power:ACCURACY_POWER", 6, "Card:SHIV"));
        var s = Combat(IdA);
        Assert.Equal(4, s.DamageBySource["Card:SHIV"]);
        Assert.Equal(6, s.DamageBySource["Power:ACCURACY_POWER"]);
    }

    [Fact]
    public void Participation_IgnoresTheFunTrophies()
    {
        var a = new PlayerStats { DamageDealt = 50, TurnsAtOneHp = 0 };
        var b = new PlayerStats { DamageDealt = 10, TurnsAtOneHp = 3 };
        var run = new Dictionary<ulong, PlayerStats> { [IdA] = a, [IdB] = b };
        var participation = Trophies.All.Single(c => c.Participation);
        // B won only a fun trophy (Ce n'est qu'une égratinure…) and a few main ones may tie at 0: B must not have won any main one.
        var winners = Trophies.Winners(participation, run).Select(w => w.Id).ToList();
        var mainWon = Trophies.All.Where(c => !c.Fun && !c.Participation).SelectMany(c => Trophies.Winners(c, run)).Select(w => w.Id).ToHashSet();
        Assert.Equal(mainWon.Contains(IdB), !winners.Contains(IdB));
        Assert.DoesNotContain(IdA, winners);
    }
}

/// v0.18.9: end-of-turn rules.
public class TurnReview0_18_9Tests
{
    private static readonly Actor A = Actor.Player(1, 1);
    private static readonly Actor B = Actor.Player(2, 2);
    private static readonly object Enemy = new();
    private static readonly object Enemy2 = new();
    private int _nextId;

    private static TurnCard Card(string id, CardKind kind = CardKind.Attack, int cost = 1, int damage = 0) => new()
    {
        Ref = new object(), Key = $"Card:{id}", Kind = kind, Cost = cost, HitsEnemies = kind == CardKind.Attack,
        DamageOn = damage > 0 ? new Dictionary<object, int>(ReferenceEqualityComparer.Instance) { [Enemy] = damage } : new Dictionary<object, int>()
    };

    private TurnPlay Play(string id, CardKind kind = CardKind.Attack, int energy = 1, Actor? owner = null, int enemies = 1) => new()
    {
        Id = ++_nextId, Owner = owner ?? A, CardRef = new object(), CardKey = $"Card:{id}", Kind = kind, EnergySpent = energy, EnemiesAlive = enemies
    };

    private static List<MissedChance> Missed(TurnResult r, string kind) => r.Events.OfType<MissedChance>().Where(m => m.Kind == kind).ToList();

    private static TurnState State(IEnumerable<TurnPlayer> players, IEnumerable<TurnPlay> plays, object? last = null, int left = 30)
    {
        var s = new TurnState { Players = players.ToList(), Plays = plays.ToList(), LastEnemy = last };
        s.EnemiesLeft[Enemy] = left;
        return s;
    }

    [Fact]
    public void WrongOrder_NothingLost_OnAnEnemyWhoseCapStoppedTheEarlierHits()
    {
        // Lame souveraine already capped by Dur à cuire, then Monologue's Strength: the extra damage would have been capped too.
        var blade = Play("SOVEREIGN_BLADE", energy: 2);
        blade.CappedOn.Add(Enemy);
        var s = State(new[] { new TurnPlayer { Actor = A } }, new[] { blade });
        s.OrderLosses.Add(new OrderLoss(blade, "Card:GATHER_LIGHT", "order", Enemy, 3));
        Assert.Empty(Missed(TurnReview.Resolve(s), "orderTurn"));
    }

    [Fact]
    public void WrongOrder_OneMissPerPlayerAndTurn()
    {
        var bash = Play("BASH", energy: 2);
        var bully = Play("BULLY", energy: 0);
        var s = State(new[] { new TurnPlayer { Actor = A } }, new[] { bash, bully });
        s.OrderLosses.Add(new OrderLoss(bash, "Card:SETUP_STRIKE", "order", Enemy, 4));
        s.OrderLosses.Add(new OrderLoss(bully, "Card:SETUP_STRIKE", "order", Enemy, 4));
        var miss = Missed(TurnReview.Resolve(s), "orderTurn").Single();
        Assert.Equal(new[] { "Card:BASH+Card:BULLY", "8", "0", "Card:SETUP_STRIKE" }, miss.Args);
    }

    [Fact]
    public void WrongOrder_RandomTarget_CappedByTheEnemiesLeft()
    {
        // Forme du serpent played after two cards: 2 × 4 damage, the only enemy has 5 left.
        var a = Play("STRIKE");
        var b = Play("DEFEND", CardKind.Skill);
        var s = State(new[] { new TurnPlayer { Actor = A } }, new[] { a, b }, left: 5);
        s.OrderLosses.Add(new OrderLoss(a, "Card:SERPENT_FORM", "orderRandom", null, 4));
        s.OrderLosses.Add(new OrderLoss(b, "Card:SERPENT_FORM", "orderRandom", null, 4));
        Assert.Equal("8", Missed(TurnReview.Resolve(s), "orderTurn").Single().Args[1]);
    }

    [Fact]
    public void WeakAttack_NotWhenItGaveBlockOrWasPlayedAgain()
    {
        var strike = Card("STRIKE");
        var player = new TurnPlayer { Actor = A, Hand = { strike } };
        var wave = Play("IRON_WAVE");
        wave.Stronger = new Better(strike.Ref, strike.Key, 9, 7);
        Assert.Single(TurnReview.Resolve(State(new[] { player }, new[] { wave })).Events.OfType<WeakAttack>());
        wave.GaveBlock = true;
        Assert.Empty(TurnReview.Resolve(State(new[] { player }, new[] { wave })).Events.OfType<WeakAttack>());
        wave.GaveBlock = false;
        wave.PlayedAgain = true;
        Assert.Empty(TurnReview.Resolve(State(new[] { player }, new[] { wave })).Events.OfType<WeakAttack>());
    }

    [Fact]
    public void OverkillFollowUp_RandomPower_NotCountedWithSeveralEnemies()
    {
        TurnPlay Shiv(string source, int enemies)
        {
            var shiv = Play("SHIV", energy: 0, enemies: enemies);
            var big = new TurnPlay
            {
                Id = shiv.Id, Owner = A, CardRef = shiv.CardRef, CardKey = "Card:SHIV", Kind = CardKind.Attack, EnemiesAlive = enemies,
                Target = Enemy, TargetLeftBefore = 4, KilledTarget = true, Overkill = 6,
                DamageOn = { [Enemy2] = 10 }
            };
            big.FollowUps.Add(new FollowUpHit(source, Enemy2, 12));
            return big;
        }
        TurnResult Run(TurnPlay p)
        {
            var s = State(new[] { new TurnPlayer { Actor = A } }, new[] { p });
            s.EnemiesLeft[Enemy2] = 20;
            return TurnReview.Resolve(s);
        }
        Assert.Empty(Missed(Run(Shiv("Power:SERPENT_FORM_POWER", 2)), "overkillFollowUp"));
        Assert.Single(Missed(Run(Shiv("Power:BLACK_HOLE_POWER", 2)), "overkillFollowUp"));
    }

    [Fact]
    public void LethalLeft_TheTwoHandsTogether()
    {
        // The last Putrilimace has 15 left; each player played a Défense instead of a Frappe (9) still in hand.
        var a = new TurnPlayer { Actor = A, Hand = { Card("STRIKE", damage: 9) } };
        var b = new TurnPlayer { Actor = B, Hand = { Card("STRIKE", damage: 9) } };
        var defendA = Play("DEFEND", CardKind.Skill);
        defendA.GaveBlock = true;
        var defendB = Play("DEFEND", CardKind.Skill, owner: B);
        defendB.GaveBlock = true;
        var r = TurnReview.Resolve(State(new[] { a, b }, new[] { defendA, defendB }, last: Enemy, left: 15));
        var lethal = r.Events.OfType<MissedLethal>().ToList();
        Assert.Equal(2, lethal.Count);
        Assert.Equal(new[] { "Card:STRIKE", "Card:STRIKE|2" }, lethal.Single(l => l.Player == A).Cards);
        // One hand alone was not enough, and without the Défenses nothing could be paid.
        Assert.Empty(TurnReview.Resolve(State(new[] { a }, new[] { defendA }, last: Enemy, left: 15)).Events.OfType<MissedLethal>());
        Assert.Empty(TurnReview.Resolve(State(new[] { a, b }, Array.Empty<TurnPlay>(), last: Enemy, left: 15)).Events.OfType<MissedLethal>());
    }

    [Fact]
    public void DiscardingAnEtherealCurse_ForNothing()
    {
        var survivor = Play("SURVIVOR", CardKind.Skill);
        survivor.GaveBlock = true;
        survivor.BlockAmount = 8;
        survivor.DiscardedEthereal = "Card:ASCENDERS_BANE";
        var r = TurnReview.Resolve(State(new[] { new TurnPlayer { Actor = A, Block = 8, Incoming = 0 } }, new[] { survivor }));
        Assert.Equal(new[] { "Card:SURVIVOR", "Card:ASCENDERS_BANE" }, Missed(r, "discardEthereal").Single().Args);
        // Its block was needed: fine.
        Assert.Empty(Missed(TurnReview.Resolve(State(new[] { new TurnPlayer { Actor = A, Block = 8, Incoming = 10 } }, new[] { survivor })),
            "discardEthereal"));
    }

    [Fact]
    public void BlockRelic_MissedOnlyWhenTheBlockWasNeeded()
    {
        TurnPlayer Player(int incoming) => new()
        {
            Actor = A, Energy = 1, Incoming = incoming, Hand = { Card("STRIKE", damage: 6) },
            Relics = { new TurnRelic("Relic:ORNAMENTAL_FAN", RelicTrigger.Attacks, 3, GivesBlock: true) },
            Plays = { [CardKind.Attack] = 2 }
        };
        Assert.Empty(Missed(TurnReview.Resolve(State(new[] { Player(0) }, Array.Empty<TurnPlay>())), "relic"));
        Assert.Single(Missed(TurnReview.Resolve(State(new[] { Player(10) }, Array.Empty<TurnPlay>())), "relic"));
    }
}
