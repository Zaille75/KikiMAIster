using KikiMAIster.Core;
using Xunit;

namespace KikiMAIster.Core.Tests;

/// v0.17.45 (co-op runs of 2026-10-29): a relic's share of damage and block counts for it, Soufre's enemy Strength is charged
/// to its owner, damage stopped by caps is Excédent, misplay fixes (pet attacks, energy cards, Orichalque, caps), new trophies.
public class Feedback0_17_45Tests : TestBase
{
    private const string Brimstone = "Relic:BRIMSTONE";

    private double Column(ulong id, string key) => Combat(id).DamageBySource.GetValueOrDefault(key);

    [Fact]
    public void StrengthFromARelic_ItsShareOfTheHitCountsForTheRelic_TotalUnchanged()
    {
        Engine.Handle(new PowerChanged(A, A, "STRENGTH_POWER", 2, false, Brimstone));
        Engine.Handle(new PowerChanged(A, A, "STRENGTH_POWER", 2, false));
        Engine.Handle(new StrengthUsed(A, 4, 4, HitSource: "Card:STRIKE"));
        Hit(A, Enemy, 0, 10);
        Assert.Equal(10, Combat(IdA).DamageDealt);
        Assert.Equal(2, Column(IdA, Brimstone), 3);
        Assert.Equal(8, Column(IdA, "Card:STRIKE"), 3);
    }

    [Fact]
    public void RelicModifier_MovesWhole_PowerModifier_OnlyTheShareARelicGave()
    {
        Engine.Handle(new DamageModifier(A, "Relic:PEN_NIB", 6, "Card:STRIKE"));
        Engine.Handle(new PowerChanged(A, A, "VIGOR_POWER", 8, false, "Relic:AKABEKO"));
        Engine.Handle(new DamageModifier(A, "Power:VIGOR_POWER", 8, "Card:STRIKE"));
        Engine.Handle(new PowerChanged(A, A, "LETHALITY_POWER", 50, false));
        Engine.Handle(new DamageModifier(A, "Power:LETHALITY_POWER", 5, "Card:STRIKE"));
        Hit(A, Enemy, 0, 30);
        Assert.Equal(6, Column(IdA, "Relic:PEN_NIB"), 3);
        Assert.Equal(8, Column(IdA, "Relic:AKABEKO"), 3);
        Assert.Equal(16, Column(IdA, "Card:STRIKE"), 3);
    }

    [Fact]
    public void VulnerableFromARelic_ItsExtraDamageCountsForTheRelic()
    {
        Engine.Handle(new PowerChanged(A, Enemy, "VULNERABLE_POWER", 1, true, "Relic:BAG_OF_MARBLES"));
        Engine.Handle(new DebuffEffect(Enemy, "VULNERABLE_POWER", A, 3, true, DebuffCredit.CurrentTurn, HitSource: "Card:STRIKE"));
        Hit(A, Enemy, 0, 9);
        Assert.Equal(3, Column(IdA, "Relic:BAG_OF_MARBLES"), 3);
        Assert.Equal(6, Column(IdA, "Card:STRIKE"), 3);
    }

    [Fact]
    public void Brimstone_EnemyStrengthOnAPlayer_ChargedToTheRelicOwner()
    {
        // A's Soufre gave the enemy 2 Strength, the enemy its own 2: half of the 4 extra damage is A's.
        Engine.Handle(new PowerChanged(A, Enemy, "STRENGTH_POWER", 2, false, Brimstone));
        Engine.Handle(new PowerChanged(Actor.None, Enemy, "STRENGTH_POWER", 2, false));
        Engine.Handle(new EnemyStrengthHit(Enemy, B, 4));
        EnemyHits(B, 0, 10);
        Assert.Equal(8, Combat(IdB).HpLostFromEnemies, 3);
        Assert.Equal(2, Combat(IdB).HpLostFromAllies, 3);
        Assert.Equal(2, Combat(IdA).FriendlyDamage, 3);
        Assert.Equal(2, Combat(IdA).HarmByRelic[Brimstone], 3);
        // On A: self-inflicted, only for the HP the hit took (block took the rest).
        Engine.Handle(new EnemyStrengthHit(Enemy, A, 4));
        EnemyHits(A, 9, 1);
        Assert.Equal(1, Combat(IdA).HpLostFromSelf, 3);
        Assert.Equal(10, Combat(IdA).HpLost + Combat(IdB).HpLost - 1, 3);
    }

    [Fact]
    public void DexterityFromARelic_ItsBlockIsALayerOfTheRelic()
    {
        Engine.Handle(new PowerChanged(A, A, "DEXTERITY_POWER", 1, false, "Relic:ODDLY_SMOOTH_STONE"));
        Engine.Handle(new BlockGained(A, A, 6, Source.Card("DEFEND"), Dexterity: 1));
        EnemyHits(A, 6, 0);
        Assert.Equal(1, Combat(IdA).BlockStoppedBySource["Relic:ODDLY_SMOOTH_STONE"], 3);
        Assert.Equal(5, Combat(IdA).BlockStoppedBySource["Card:DEFEND"], 3);
    }

    [Fact]
    public void DamageStoppedByACap_IsOverkill()
    {
        Engine.Handle(new DamageCapped(PetA, Enemy, 160));
        Assert.Equal(160, Combat(IdA).Overkill);
        Assert.Equal(160, Combat(IdA).DamageCapped);
    }

    [Fact]
    public void RoundStates_CountDeadAloneAndOneHp()
    {
        Engine.Handle(new PlayerRoundState(A, 0, false, false));
        Engine.Handle(new PlayerRoundState(B, 1, true, true));
        Assert.Equal(1, Combat(IdA).TurnsDead);
        Assert.Equal(1, Combat(IdB).TurnsAlone);
        // v0.18.6: a turn at 1 HP counts once it ended at 1 HP (the next round starts at 1 HP).
        Assert.Equal(0, Combat(IdB).TurnsAtOneHp);
        Engine.Handle(new PlayerRoundState(B, 1, true, true));
        Assert.Equal(1, Combat(IdB).TurnsAtOneHp);
    }

    [Fact]
    public void Ties_EveryWinner_AndParticipation_ForWhoWonNothing()
    {
        var run = new Dictionary<ulong, PlayerStats>
        {
            [IdA] = new() { DamageDealt = 10, Kills = 1 }, [IdB] = new() { DamageDealt = 10 }, [IdC] = new()
        };
        Assert.Equal(new ulong[] { IdA, IdB }, Trophies.Winners(Trophies.All.Single(c => c.Key == "trophy.damage"), run).Select(w => w.Id));
        var participation = Trophies.Winners(Trophies.All.Single(c => c.Participation), run).Select(w => w.Id).ToList();
        Assert.DoesNotContain(IdA, participation);
        Assert.DoesNotContain(IdB, participation);
    }

    [Fact]
    public void ChifumiMoves_AndLongest_AreKept()
    {
        var moves = new Dictionary<ulong, IReadOnlyList<string>> { [IdA] = new[] { "Rock", "Paper" }, [IdB] = new[] { "Rock", "Rock" } };
        Engine.Handle(new ChifumiFought("f1", A, new[] { A, B }, moves, 2));
        var s = Engine.Get(Scope.Run, IdA);
        Assert.Equal(1, s.ChifumiMoves["Paper"]);
        Assert.Equal(2, s.ChifumiLongest);
        Assert.Equal(0, s.ChifumiLost);
        var book = new Chifumi();
        book.Record("f1", IdA, new[] { IdA, IdB }, null, moves, 2, "2026-10-29");
        var read = Chifumi.Read(book.Write());
        Assert.Equal(2, read.MovesOf(IdB)["Rock"]);
        Assert.Equal((2, IdA, "2026-10-29"), read.Longest);
    }

    [Fact]
    public void CardPicks_FromTheHistory()
    {
        Engine.Handle(new CombatEnded(true));
        Engine.Handle(new RunHistoryRead(A, 0, 0, new Dictionary<string, double>(),
            CardPicks: new Dictionary<string, double> { ["offered.fight"] = 6, ["taken.fight"] = 1, ["skipped.fight"] = 1, ["offered.event"] = 3 }));
        var s = Engine.Get(Scope.Run, IdA);
        Assert.Equal(9, s.CardsOffered);
        Assert.Equal(1, s.CardsTaken);
        Assert.Equal(1, s.CardOffersSkipped);
    }

    [Fact]
    public void PlayersSeen_NamesKnownBeforeTheFirstFight()
    {
        Engine.Handle(new PlayersSeen(new[] { new PlayerInfo(IdA, "Kiki", "NECROBINDER") }));
        Assert.Equal("Kiki", Engine.Players[IdA].Name);
    }
}

/// v0.17.45 misplay rules.
public class TurnReview0_17_45Tests
{
    private static readonly Actor A = Actor.Player(1, 1);
    private static readonly object Enemy = new();

    private static TurnCard Card(string id, CardKind kind = CardKind.Attack, int cost = 1, bool needsPet = false, bool kept = false) => new()
    {
        Ref = new object(), Key = $"Card:{id}", Kind = kind, Cost = cost, NeedsPet = needsPet, KeptOnPurpose = kept,
        HitsEnemies = kind == CardKind.Attack, DamageOn = new Dictionary<object, int> { [Enemy] = 7 }
    };

    private static TurnResult Resolve(TurnPlayer p, bool openEnemy = true, bool nothingToKill = false, params TurnPlay[] plays)
    {
        var s = new TurnState { Players = { p }, Plays = plays.ToList(), OpenEnemy = openEnemy, NothingToKill = nothingToKill };
        s.EnemiesLeft[Enemy] = 50;
        return TurnReview.Resolve(s);
    }

    private static List<MissedChance> Missed(TurnResult r, string kind) => r.Events.OfType<MissedChance>().Where(m => m.Kind == kind).ToList();

    [Fact]
    public void PetAttack_PetDead_NoEnergyMiss_UnlessTheOwnCardKilledItThisTurn()
    {
        var rattle = Card("RATTLE", needsPet: true);
        Assert.Empty(Missed(Resolve(new TurnPlayer { Actor = A, Energy = 2, Hand = { rattle }, PetAlive = false }), "energy"));
        Assert.Single(Missed(Resolve(new TurnPlayer { Actor = A, Energy = 2, Hand = { rattle }, PetAlive = false, PetSacrificedThisTurn = true }), "energy"));
    }

    [Fact]
    public void EnergyOnlyOrPetKillingCard_KeptOnPurpose_NoEnergyMiss()
    {
        Assert.Empty(Missed(Resolve(new TurnPlayer { Actor = A, Energy = 2, Hand = { Card("BORROWED_TIME", CardKind.Skill, kept: true) } }), "energy"));
    }

    [Fact]
    public void Orichalcum_SameBlockAsTheCard_IsNoMisplay()
    {
        var defend = new TurnPlay
        {
            Id = 1, Owner = A, CardRef = new object(), CardKey = "Card:DEFEND", Kind = CardKind.Skill, EnergySpent = 1, GaveBlock = true,
            BlockAmount = 6, BlockGains = 1
        };
        TurnPlayer Player(int block) => new() { Actor = A, Block = block, Incoming = 10, Orichalcum = "Relic:ORICHALCUM", OrichalcumBlock = 6 };
        Assert.Empty(Missed(Resolve(Player(6), plays: defend), "orichalcum"));
        Assert.Single(Missed(Resolve(Player(5), plays: defend), "orichalcum"));
    }

    [Fact]
    public void EveryEnemyCapped_EnergyKeptWithOnlyAttacks_IsSpare_AndAttacksIntoTheCap_ForNothing()
    {
        var r = Resolve(new TurnPlayer { Actor = A, Energy = 1, Hand = { Card("STRIKE") } }, openEnemy: false);
        Assert.Empty(Missed(r, "energy"));
        Assert.Single(r.Events.OfType<HelpGiven>(), h => h.Kind == "energySurplus");

        var capped = new TurnPlay { Id = 2, Owner = A, CardRef = new object(), CardKey = "Card:SQUEEZE", Kind = CardKind.Attack, EnergySpent = 3, ShellCapped = 1 };
        r = Resolve(new TurnPlayer { Actor = A }, nothingToKill: true, plays: capped);
        // v0.18.6: nothing else to play: the energy is surplus, not spent for nothing.
        Assert.Equal(3, r.Events.OfType<HelpGiven>().Single(h => h.Kind == "energySurplus").Amount);
    }
}
