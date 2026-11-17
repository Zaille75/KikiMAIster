using KikiMAIster.Core;
using Xunit;

namespace KikiMAIster.Core.Tests;

/// 1.2.3 (Zaille75's feedback of 2026-11-17): copies with no arrow, created cards lost, overkill after an earlier hit, Visée
/// into a cap, empty Shots only when their energy mattered, Crabe furieux, melody block cards.
public class Update1_2_3Tests
{
    private static readonly Actor A = Actor.Player(1, 1);
    private readonly object _x = new(), _y = new();
    private int _nextId;

    private static TurnCard Card(string id, int cost = 1, int damage = 6) => new()
    {
        Ref = new object(), Key = $"Card:{id}", Kind = CardKind.Attack, Cost = cost, HitsEnemies = true,
        DamageOn = new Dictionary<object, int>(ReferenceEqualityComparer.Instance) { [new object()] = damage }
    };

    private TurnPlay Play(string id, int energy = 1, int energyBefore = 3, object? target = null, params object[] inHand) => new()
    {
        Id = ++_nextId, Owner = A, CardRef = new object(), CardKey = $"Card:{id}", Kind = CardKind.Attack, EnergySpent = energy,
        EnergyBefore = energyBefore, Target = target, HandAtPlay = inHand.ToList()
    };

    private TurnState State(TurnPlayer player, params TurnPlay[] plays)
    {
        var s = new TurnState { Players = { player }, Plays = plays.ToList() };
        s.EnemiesLeft[_x] = 30;
        s.EnemiesLeft[_y] = 14;
        return s;
    }

    private static List<MissedChance> Missed(TurnResult r, string kind) => r.Events.OfType<MissedChance>().Where(m => m.Kind == kind).ToList();

    [Fact]
    public void CopyWithNoArrow_IsAChoiceMistake()
    {
        var shot = Play("SKY_SHOT", target: _x);
        shot.ShotArrow = true;
        shot.EmptyReplay = true;
        Assert.Equal(new[] { "Card:SKY_SHOT" }, Missed(TurnReview.Resolve(State(new TurnPlayer { Actor = A }, shot)), "emptyReplay").Single().Args);
        Assert.Contains("emptyReplay", PlayerStats.ChoiceKinds);
    }

    [Fact]
    public void CreatedIntoAFullHand_CouldComeEarlier_OnlyWhenNothingElseChanges()
    {
        TurnState Turn(int strikeEnergyBefore)
        {
            var maker = Play("BRIGHTEN_UP", energyBefore: strikeEnergyBefore - 1);
            maker.LostToFullHand = 2;
            maker.CreatedToHand = 4;
            var filler = Enumerable.Range(0, 4).Select(_ => new object()).ToArray();
            var strike = Play("STRIKE", energyBefore: strikeEnergyBefore, inHand: filler.Append(maker.CardRef).ToArray());
            maker.HandAtPlay.AddRange(filler);
            return State(new TurnPlayer { Actor = A }, strike, maker);
        }
        // Allégresse first, then Frappe: 5 cards in hand + 4 created fit, and Frappe is still affordable.
        Assert.Equal(new[] { "Card:BRIGHTEN_UP", "2", "Card:STRIKE" }, Missed(TurnReview.Resolve(Turn(2)), "makeEarlier").Single().Args);
        // Its energy came later (Encore): Frappe would not have been affordable after it.
        Assert.Empty(Missed(TurnReview.Resolve(Turn(1)), "makeEarlier"));
    }

    [Fact]
    public void OverkillAfterAnEarlierHitOnTheSameTarget_IsGaché_NamingPenNib()
    {
        // Three Frappes into the same Larve (18 HP); the third, doubled by Pointe de stylo, killed it with 6 overkill.
        var big = new TurnPlay
        {
            Id = ++_nextId, Owner = A, CardRef = new object(), CardKey = "Card:STRIKE_FOOL", Kind = CardKind.Attack, EnergySpent = 1,
            EnergyBefore = 1, Target = _x, TargetLeftBefore = 6
        };
        var first = Play("STRIKE_FOOL", target: _x, inHand: big.CardRef);
        var second = Play("STRIKE_FOOL", target: _x, inHand: big.CardRef);
        foreach (var p in new[] { first, second })
        {
            p.Hits.Add(new EnemyHit(_x, 6, -1));
            p.DamageOn[_x] = 6;
            p.DamageOn[_y] = 6;
        }
        big.KilledTarget = true;
        big.Overkill = 6;
        big.PenNib = true;
        big.DamageOn[_y] = 12;
        var s = State(new TurnPlayer { Actor = A }, first, second, big);
        s.EnemiesLeft.Remove(_x);
        var miss = Missed(TurnReview.Resolve(s), "overkillEarlier").Single();
        Assert.Equal(new[] { "Card:STRIKE_FOOL", "Card:STRIKE_FOOL", "6", "Relic:PEN_NIB" }, miss.Args);
        Assert.Contains("overkillEarlier", PlayerStats.WastedKinds);
    }

    [Fact]
    public void AimIntoIntangible_IsLost_UpToWhatTheCapStopped()
    {
        var shot = Play("REPEAT_SHOT", target: _x);
        shot.ShotArrow = true;
        shot.AimAtStart = 6;
        shot.Capped = true;
        shot.CapStopped = 20;
        shot.CapKeys.Add("Power:INTANGIBLE_POWER");
        Assert.Equal(new[] { "Card:REPEAT_SHOT", "Power:INTANGIBLE_POWER", "6" },
            Missed(TurnReview.Resolve(State(new TurnPlayer { Actor = A }, shot)), "aimCapped").Single().Args);
    }

    [Fact]
    public void AimIntoGlissant_OnlyWhenGlissantWentAwayAndALaterShotLostIt()
    {
        TurnState Turn(int slipperyLeft, bool laterShot)
        {
            var later = Play("FOOLISH_SHOT", target: _x);
            later.ShotArrow = true;
            var shot = Play("REPEAT_SHOT", target: _x, inHand: later.CardRef);
            shot.ShotArrow = true;
            shot.AimAtStart = 6;
            shot.Capped = true;
            shot.CapStopped = 20;
            shot.CapKeys.Add("Power:SLIPPERY_POWER");
            shot.SlipperyOn.Add(_x);
            var s = laterShot ? State(new TurnPlayer { Actor = A }, shot, later) : State(new TurnPlayer { Actor = A }, shot);
            s.EnemySlippery[_x] = slipperyLeft;
            return s;
        }
        // Vantôme (Glissant 8 → 7): reducing Glissant was a choice.
        Assert.Empty(Missed(TurnReview.Resolve(Turn(7, laterShot: true)), "aimCapped"));
        Assert.Empty(Missed(TurnReview.Resolve(Turn(0, laterShot: false)), "aimCapped"));
        Assert.Equal(new[] { "Card:REPEAT_SHOT", "Power:SLIPPERY_POWER", "6" },
            Missed(TurnReview.Resolve(Turn(0, laterShot: true)), "aimCapped").Single().Args);
    }

    [Fact]
    public void NoArrowCard_IsAMistakeOnlyWhenItsEnergyWouldHavePaidAnotherCard()
    {
        TurnPlay Recall(int energy)
        {
            var p = Play("RECALL", energy, target: _x);
            p.NotImpaled = true;
            return p;
        }
        // Rappel+ for 0 energy: nothing lost.
        Assert.Empty(Missed(TurnReview.Resolve(State(new TurnPlayer { Actor = A, Hand = { Card("STRIKE") } }, Recall(0))), "notImpaled"));
        // 1 energy with nothing else to spend it on.
        Assert.Empty(Missed(TurnReview.Resolve(State(new TurnPlayer { Actor = A }, Recall(1))), "notImpaled"));
        // 1 energy that would have paid a Frappe left in hand.
        Assert.Single(Missed(TurnReview.Resolve(State(new TurnPlayer { Actor = A, Hand = { Card("STRIKE") } }, Recall(1))), "notImpaled"));
        // Energy left already paid the Frappe: the card's energy changed nothing.
        Assert.Empty(Missed(TurnReview.Resolve(State(new TurnPlayer { Actor = A, Energy = 1, Hand = { Card("STRIKE") } }, Recall(1))), "notImpaled"));
    }

    [Fact]
    public void CrabRage_TheLastCrabGains99Block()
    {
        var hand = new[] { new Lethal.Attack("HEAVY_SHOT", 2, false, new[] { 110, 110 }), new Lethal.Attack("FOOLISH_SHOT", 1, false, new[] { 80, 80 }) };
        var needs = new[] { 107, 80 };
        Assert.NotNull(Lethal.Find(hand, 3, needs));
        Assert.Null(Lethal.Find(hand, 3, needs, deathBlock: new[] { 99, 99 }));
        // Only one enemy gains it: killing it last with enough damage still works.
        Assert.NotNull(Lethal.Find(new[] { new Lethal.Attack("BIG", 1, false, new[] { 300, 300 }), new Lethal.Attack("S", 1, false, new[] { 10, 10 }) },
            2, new[] { 100, 10 }, deathBlock: new[] { 99, 0 }));
    }

    [Fact]
    public void MelodyBlockCard_ThatAPayoffCounted_IsNeverGaché()
    {
        var strike = new TurnCard { Ref = new object(), Key = "Card:STRIKE_FOOL", Kind = CardKind.Attack, Cost = 1, HitsEnemies = true };
        TurnState Turn(bool blastAfter)
        {
            var cho = new TurnPlay
            {
                Id = ++_nextId, Owner = A, CardRef = new object(), CardKey = "Card:CHO_FIRM", Kind = CardKind.Skill, EnergySpent = 1,
                EnergyBefore = 3, GaveBlock = true, BlockAmount = 12, BlockGains = 2, Melody = { "CHO" },
                UsefulCardsInHand = { new SeenCard(strike.Ref, strike.Key) }
            };
            var plays = new List<TurnPlay> { cho };
            if (blastAfter)
                plays.Add(new TurnPlay
                {
                    Id = ++_nextId, Owner = A, CardRef = new object(), CardKey = "Card:WAH_BLAST", Kind = CardKind.Attack, EnergySpent = 2,
                    Melody = { "WAH" }, MelodyBefore = { "CHO" }
                });
            var player = new TurnPlayer { Actor = A, Energy = 1, Block = 12, Incoming = 0, Hand = { strike }, MelodyTags = { "CHO", "WAH" } };
            return new TurnState { Players = { player }, Plays = plays };
        }
        Assert.False(TurnReview.Resolve(Turn(blastAfter: true)).Events.OfType<BlockPlayResolved>().Single().HadAlternative);
        Assert.True(TurnReview.Resolve(Turn(blastAfter: false)).Events.OfType<BlockPlayResolved>().Single().HadAlternative);
    }
}
