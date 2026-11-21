using KikiMAIster.Core;
using Xunit;

namespace KikiMAIster.Core.Tests;

/// 1.2.4 (Zaille75's feedback of 2026-11-21): shoot last against Ralenti, Visée into Dur à cuire everywhere, free Vers Ki,
/// Larmes de Paël keep 1 energy only, card makers after cards that fed them, free card spent on a 0-cost card, potions at death.
public class Update1_2_4Tests
{
    private static readonly Actor A = Actor.Player(1, 1);
    private readonly object _x = new(), _y = new();
    private int _nextId;

    private TurnPlay Play(string id, int energy = 1, int energyBefore = 3, object? target = null, CardKind kind = CardKind.Attack,
        params object[] inHand) => new()
    {
        Id = ++_nextId, Owner = A, CardRef = new object(), CardKey = $"Card:{id}", Kind = kind, EnergySpent = energy,
        EnergyBefore = energyBefore, Target = target, HandAtPlay = inHand.ToList()
    };

    private TurnState State(TurnPlayer player, params TurnPlay[] plays)
    {
        var s = new TurnState { Players = { player }, Plays = plays.ToList() };
        s.EnemiesLeft[_x] = 100;
        s.EnemiesLeft[_y] = 100;
        return s;
    }

    private static List<MissedChance> Missed(TurnResult r, string kind) => r.Events.OfType<MissedChance>().Where(m => m.Kind == kind).ToList();

    private HashSet<object> Set(params object[] items)
    {
        var s = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var i in items) s.Add(i);
        return s;
    }

    [Fact]
    public void ShootLast_AgainstRalenti_IsNotShootFirst()
    {
        TurnResult Run(int slow)
        {
            var shotCard = new object();
            var strikes = Enumerable.Range(0, 3).Select(_ =>
            {
                var p = Play("STRIKE_FOOL", target: _x, inHand: shotCard);
                p.ArrowsReady = 1;
                p.Hits.Add(new EnemyHit(_x, 8, slow < 0 ? -1 : slow));
                return p;
            }).ToList();
            var shot = new TurnPlay
            {
                Id = ++_nextId, Owner = A, CardRef = shotCard, CardKey = "Card:QUICK_SHOT", Kind = CardKind.Attack, Target = _x,
                ShotArrow = true, ArrowsReady = 1
            };
            shot.Hits.Add(new EnemyHit(_x, 35, slow < 0 ? -1 : slow + 3));
            return TurnReview.Resolve(State(new TurnPlayer { Actor = A, OwnArrowIn = Set(_x) }, strikes.Append(shot).ToArray()));
        }
        Assert.Single(Missed(Run(-1), "shootFirst"));
        Assert.Empty(Missed(Run(2), "shootFirst"));
    }

    [Fact]
    public void AimIntoDurACuire_NotAMistake_WhenEveryEnemyHasIt()
    {
        TurnResult Run(bool both)
        {
            var shot = Play("RAIN_SHOT", target: _x);
            shot.ShotArrow = true;
            shot.AimAtStart = 6;
            shot.Capped = true;
            shot.CapStopped = 9;
            shot.CapKeys.Add("Power:HARD_TO_KILL_POWER");
            var s = State(new TurnPlayer { Actor = A }, shot);
            s.EnemyHardToKill.Add(_x);
            if (both) s.EnemyHardToKill.Add(_y);
            return TurnReview.Resolve(s);
        }
        Assert.Empty(Missed(Run(true), "aimCapped"));
        Assert.Single(Missed(Run(false), "aimCapped"));
    }

    [Fact]
    public void VersKiUnused_OnlyWhenItCostEnergy()
    {
        TurnResult Run(int energy)
        {
            var verse = Play("KI_VERSE", energy: energy, kind: CardKind.Skill);
            verse.PowersGiven.Add("Power:KI_VERSE_POWER");
            return TurnReview.Resolve(State(new TurnPlayer { Actor = A, UnusedBuffs = { "Power:KI_VERSE_POWER" } }, verse));
        }
        Assert.Single(Missed(Run(1), "verseUnused"));
        Assert.Empty(Missed(Run(0), "verseUnused"));
    }

    [Fact]
    public void PaelsTears_KeepsOneEnergy_TheRestIsSurplus()
    {
        int Surplus(int energy) => TurnReview.Resolve(State(new TurnPlayer { Actor = A, Energy = energy, PaelsTears = "Relic:PAELS_TEARS" }))
            .Events.OfType<HelpGiven>().Where(h => h.Kind == "energySurplus").Sum(h => (int)h.Amount);
        Assert.Equal(11, Surplus(12));
        Assert.Equal(0, Surplus(1));
    }

    [Fact]
    public void MakerFirst_NotAfterACardThatFedIt()
    {
        TurnResult Run(bool fed, bool vulnerable)
        {
            _nextId = 0;
            var maker = Play("INFERNAL_BLADE", kind: CardKind.Skill);
            var made = Play("THRASH", energy: 0, target: _x);
            maker.Created.Add(made.CardRef);
            var feel = Play("FEEL_NO_PAIN", kind: CardKind.Power, energyBefore: 3, inHand: maker.CardRef);
            var tremble = Play("TREMBLE", kind: CardKind.Skill, energyBefore: 3, target: _x, inHand: maker.CardRef);
            feel.PowersGiven.Add("Power:FEEL_NO_PAIN_POWER");
            if (fed) maker.PowersTriggered.Add("Power:FEEL_NO_PAIN_POWER");
            if (vulnerable) tremble.VulnerableOn.Add(_x);
            made.Hits.Add(new EnemyHit(_x, 9, -1, Vulnerable: true));
            return TurnReview.Resolve(State(new TurnPlayer { Actor = A }, tremble, feel, maker, made));
        }
        Assert.Equal("Card:TREMBLE+Card:FEEL_NO_PAIN", Missed(Run(false, false), "makerFirst").Single().Args[1]);
        Assert.Equal("Card:TREMBLE", Missed(Run(true, false), "makerFirst").Single().Args[1]);
        Assert.Empty(Missed(Run(true, true), "makerFirst"));
    }

    [Fact]
    public void FreeCardSpentOnAZeroCostCard_WhenACardPaidWithEnergyCameAfter()
    {
        TurnResult Run(bool laterPaid)
        {
            _nextId = 0;
            var later = Play("CHO_READY", energy: laterPaid ? 1 : 0, kind: CardKind.Skill);
            var nock = Play("NOCK", energy: 0, kind: CardKind.Skill, inHand: later.CardRef);
            nock.FreeCardWasted = true;
            return TurnReview.Resolve(State(new TurnPlayer { Actor = A }, nock, later));
        }
        Assert.Equal(new[] { "Card:NOCK", "Card:CHO_READY" }, Missed(Run(true), "freeCardWasted").Single().Args);
        Assert.Empty(Missed(Run(false), "freeCardWasted"));
        Assert.Contains("freeCardWasted", PlayerStats.ChoiceKinds);
    }

    [Fact]
    public void PotionsAtDeath_AMistakeOnlyWhenOneCouldHaveSavedThePlayer()
    {
        var turn = new LastTurnView(0, 5, new List<(int, int)> { (1, 5) });
        // Short of 2 HP: a Potion de bloc saves, a Potion de précision does not.
        Assert.True(DeathPotions.CouldSave(new PotionView("Potion:BLOCK_POTION", Block: 12), 2, 14, turn));
        Assert.False(DeathPotions.CouldSave(new PotionView("Potion:PRECISION_POTION"), 2, 14, turn));
        // Potion d'énergie: only with a block card kept that it would pay (an empty hand: no).
        Assert.True(DeathPotions.CouldSave(new PotionView("Potion:ENERGY_POTION", Energy: 2), 2, 14, turn));
        Assert.False(DeathPotions.CouldSave(new PotionView("Potion:ENERGY_POTION", Energy: 2), 2, 14, new LastTurnView(2, 5, new List<(int, int)>())));
        // Faiblesse: −25% of the killing blow (45 → 11 less), Fortifiant: the block doubled.
        Assert.True(DeathPotions.CouldSave(new PotionView("Potion:WEAK_POTION", Weak: true), 10, 45, turn));
        Assert.False(DeathPotions.CouldSave(new PotionView("Potion:WEAK_POTION", Weak: true), 20, 45, turn));
        Assert.True(DeathPotions.CouldSave(new PotionView("Potion:FORTIFIER", DoublesBlock: true), 5, 14, turn));
    }
}
