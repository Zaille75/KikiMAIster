using KikiMAIster.Core;
using Xunit;

namespace KikiMAIster.Core.Tests;

/// 1.2.2: the Fool's melodies and more arrow mistakes (spec 01-metrics "The Fool's melodies" and the rows after it).
public class FoolMelodyTests
{
    private static readonly Actor A = Actor.Player(1, 1);
    private static readonly Actor B = Actor.Player(2, 1);
    private static readonly object Enemy = new();
    private static readonly object Other = new();
    private int _id;

    private TurnPlay P(string id, int energy = 1, CardKind kind = CardKind.Attack, object? target = null, int hits = 0, int raw = 6,
        int energyBefore = 5, string[]? tags = null, string[]? before = null, bool costsX = false, int targetLeft = 0)
    {
        var p = new TurnPlay
        {
            Id = ++_id, Owner = A, CardRef = new object(), CardKey = $"Card:{id}", Kind = kind, EnergySpent = energy, Target = target,
            EnergyBefore = energyBefore, CostsX = costsX, KindCount = _id, ArrowsReady = 1, TargetLeftBefore = targetLeft
        };
        p.Melody = new HashSet<string>(tags ?? Array.Empty<string>());
        p.MelodyBefore = new HashSet<string>(before ?? Array.Empty<string>());
        for (var i = 0; i < hits; i++) p.Hits.Add(new EnemyHit(target ?? Enemy, raw, -1));
        return p;
    }

    /// The cards were in hand when "at" was played.
    private static void Hold(TurnPlay at, params TurnPlay[] cards)
    {
        foreach (var c in cards) at.HandAtPlay.Add(c.CardRef);
    }

    private static TurnCard Card(string id, int cost = 1, CardKind kind = CardKind.Skill, string[]? tags = null) => new()
    {
        Ref = new object(), Key = $"Card:{id}", Kind = kind, Cost = cost, Melody = new HashSet<string>(tags ?? Array.Empty<string>())
    };

    private static List<MissedChance> Missed(TurnResult r, string kind) => r.Events.OfType<MissedChance>().Where(m => m.Kind == kind).ToList();

    private static TurnState State(TurnPlayer player, params TurnPlay[] plays) => State(new[] { player }, plays);

    private static TurnState State(IEnumerable<TurnPlayer> players, params TurnPlay[] plays)
    {
        var s = new TurnState { Players = players.ToList(), Plays = plays.ToList() };
        s.EnemiesLeft[Enemy] = 40;
        s.EnemiesLeft[Other] = 40;
        return s;
    }

    private static TurnPlayer Me(string[]? melodies = null) => new() { Actor = A, MelodyTags = new HashSet<string>(melodies ?? Array.Empty<string>()) };

    [Fact]
    public void MelodicShot_BeforeMelodyCards_LostOneHitPerTag()
    {
        var shot = P("MELODIC_SHOT", target: Enemy, hits: 1, raw: 12, energyBefore: 3);
        var wah = P("WAH_TOUCH", target: Enemy, hits: 1, tags: new[] { "WAH" });
        var cho = P("CHO_FIRM", kind: CardKind.Skill, tags: new[] { "CHO" }, before: new[] { "WAH" });
        Hold(shot, wah, cho);
        var r = TurnReview.Resolve(State(Me(new[] { "WAH", "CHO" }), shot, wah, cho));
        Assert.Equal(new[] { "Card:MELODIC_SHOT", "24", "0", "Card:WAH_TOUCH+Card:CHO_FIRM" }, Missed(r, "orderTurn").Single().Args);
        // Energy for only one of them first: one tag.
        var tight = P("MELODIC_SHOT", target: Enemy, hits: 1, raw: 12, energyBefore: 2);
        Hold(tight, wah, cho);
        Assert.Equal(new[] { "Card:MELODIC_SHOT", "12", "0", "Card:WAH_TOUCH" },
            Missed(TurnReview.Resolve(State(Me(new[] { "WAH", "CHO" }), tight, wah, cho)), "orderTurn").Single().Args);
    }

    [Fact]
    public void MelodicShot_ThenWahHammerOnTheEnemyItImpaled_IsNotAMistake()
    {
        var shot = P("MELODIC_SHOT", target: Enemy, hits: 1, raw: 12);
        shot.ShotArrow = true;
        var hammer = P("WAH_HAMMER", target: Enemy, hits: 1, raw: 16, tags: new[] { "WAH" });
        hammer.ArrowIn.Add(Enemy);
        Hold(shot, hammer);
        Assert.Empty(Missed(TurnReview.Resolve(State(Me(new[] { "WAH" }), shot, hammer)), "orderTurn"));
    }

    [Fact]
    public void HarmonicShield_BeforeACho_LostItsBlockPerTag_WhenTheBlockWasShort()
    {
        var shield = P("HARMONIC_SHIELD", kind: CardKind.Skill, before: new[] { "WAH" });
        shield.MelodyUnit = 5;
        shield.GaveBlock = true;
        shield.BlockGains = 1;
        var cho = P("CHO_FIRM", kind: CardKind.Skill, tags: new[] { "CHO" }, before: new[] { "WAH" });
        Hold(shield, cho);
        var short_ = new TurnPlayer { Actor = A, Incoming = 20, Block = 5, MelodyTags = new() { "WAH", "CHO" } };
        Assert.Equal(new[] { "Card:HARMONIC_SHIELD", "0", "5", "Card:CHO_FIRM" },
            Missed(TurnReview.Resolve(State(short_, shield, cho)), "orderTurn").Single().Args);
        var covered = new TurnPlayer { Actor = A, Incoming = 5, Block = 5, MelodyTags = new() { "WAH", "CHO" } };
        Assert.Empty(Missed(TurnReview.Resolve(State(covered, shield, cho)), "orderTurn"));
    }

    [Fact]
    public void SteadyCadence_BeforeMelodyCards_DrewLess()
    {
        var cadence = P("STEADY_CADENCE", target: Enemy, hits: 1, raw: 10);
        var wah = P("WAH_TOUCH", target: Enemy, hits: 1, tags: new[] { "WAH" });
        var ki = P("KI_CHANT", kind: CardKind.Skill, tags: new[] { "KI" }, before: new[] { "WAH" });
        Hold(cadence, wah, ki);
        Assert.Equal(new[] { "Card:STEADY_CADENCE", "Card:WAH_TOUCH+Card:KI_CHANT", "Draws", "2" },
            Missed(TurnReview.Resolve(State(Me(new[] { "WAH", "KI" }), cadence, wah, ki)), "melodyFirst").Single().Args);
        cadence.DrawBlocked = true;
        Assert.Empty(Missed(TurnReview.Resolve(State(Me(new[] { "WAH", "KI" }), cadence, wah, ki)), "melodyFirst"));
    }

    [Fact]
    public void Coda_BeforeMelodyCards_CountsOnlyWhenTheEnergyWouldHavePaidACard()
    {
        var coda = P("CODA", energy: 6, target: Enemy, hits: 1, raw: 40, energyBefore: 8);
        var wah = P("WAH_TOUCH", target: Other, hits: 1, tags: new[] { "WAH" });
        var cho = P("CHO_FIRM", kind: CardKind.Skill, tags: new[] { "CHO" }, before: new[] { "WAH" });
        Hold(coda, wah, cho);
        var withCard = new TurnPlayer { Actor = A, MelodyTags = new() { "WAH", "CHO" }, Hand = { Card("DEFLECT", cost: 3) } };
        Assert.Equal(new[] { "Card:CODA", "Card:WAH_TOUCH+Card:CHO_FIRM", "Energy", "4" },
            Missed(TurnReview.Resolve(State(withCard, coda, wah, cho)), "melodyFirst").Single().Args);
        Assert.Empty(Missed(TurnReview.Resolve(State(Me(new[] { "WAH", "CHO" }), coda, wah, cho)), "melodyFirst"));
    }

    [Fact]
    public void WahBlast_BeforeChoLimbs_WhoseBonusCameFromIt_IsNotAMistake()
    {
        var blast = P("WAH_BLAST", energy: 2, target: Enemy, hits: 1, raw: 14, tags: new[] { "WAH" });
        var limbs = P("CHO_LIMBS", energy: 0, kind: CardKind.Skill, tags: new[] { "CHO" }, before: new[] { "WAH" });
        Hold(blast, limbs);
        Assert.Empty(Missed(TurnReview.Resolve(State(Me(new[] { "WAH", "CHO" }), blast, limbs)), "orderTurn"));
        // A Wah card played before both: Branches Cho had its bonus anyway, Déflagration Wah lost its second hit.
        var wah = P("WAH_TOUCH", target: Other, hits: 1, tags: new[] { "WAH" });
        var blast2 = P("WAH_BLAST", energy: 2, target: Enemy, hits: 1, raw: 14, tags: new[] { "WAH" }, before: new[] { "WAH" });
        var limbs2 = P("CHO_LIMBS", energy: 0, kind: CardKind.Skill, tags: new[] { "CHO" }, before: new[] { "WAH" });
        Hold(blast2, limbs2);
        Assert.Equal(new[] { "Card:WAH_BLAST", "14", "0", "Card:CHO_LIMBS" },
            Missed(TurnReview.Resolve(State(Me(new[] { "WAH", "CHO" }), wah, blast2, limbs2)), "orderTurn").Single().Args);
    }

    [Fact]
    public void KiRest_BeforeAWah_LostItsRetainedHand()
    {
        var rest = P("KI_REST", energy: 2, kind: CardKind.Skill, tags: new[] { "KI" });
        var wah = P("WAH_TOUCH", target: Enemy, hits: 1, tags: new[] { "WAH" }, before: new[] { "KI" });
        Hold(rest, wah);
        var player = new TurnPlayer { Actor = A, MelodyTags = new() { "KI", "WAH" }, Hand = { Card("NAIL", cost: 2, kind: CardKind.Attack) } };
        Assert.Equal(new[] { "Card:KI_REST", "Card:WAH_TOUCH", "Retain", "1" }, Missed(TurnReview.Resolve(State(player, rest, wah)), "melodyFirst").Single().Args);
        // Nothing worth keeping in hand: no loss.
        Assert.Empty(Missed(TurnReview.Resolve(State(Me(new[] { "KI", "WAH" }), rest, wah)), "melodyFirst"));
    }

    [Fact]
    public void ViolentChord_AfterTheFirstWah_LostItsDamageOnEveryEnemy()
    {
        var wah = P("WAH_TOUCH", target: Enemy, hits: 1, energyBefore: 3, tags: new[] { "WAH" });
        var chord = P("VIOLENT_CHORD", kind: CardKind.Power, before: new[] { "WAH" });
        chord.MelodyUnit = 9;
        Hold(wah, chord);
        Assert.Equal(new[] { "Card:WAH_TOUCH", "18", "0", "Card:VIOLENT_CHORD" },
            Missed(TurnReview.Resolve(State(Me(new[] { "WAH" }), wah, chord)), "orderTurn").Single().Args);
        // Drawn after the Wah: nothing lost.
        var wah2 = P("WAH_TOUCH", target: Enemy, hits: 1, tags: new[] { "WAH" });
        Assert.Empty(Missed(TurnReview.Resolve(State(Me(new[] { "WAH" }), wah2, chord)), "orderTurn"));
    }

    [Fact]
    public void Motif_AfterTheFirstCardOfEachTag_DrewLess()
    {
        var wah = P("WAH_TOUCH", target: Enemy, hits: 1, tags: new[] { "WAH" });
        var cho = P("CHO_FIRM", kind: CardKind.Skill, tags: new[] { "CHO" }, before: new[] { "WAH" });
        var motif = P("MOTIF", kind: CardKind.Power, before: new[] { "WAH", "CHO" });
        motif.MelodyUnit = 1;
        Hold(wah, motif);
        Hold(cho, motif);
        Assert.Equal(new[] { "Card:MOTIF", "Card:WAH_TOUCH+Card:CHO_FIRM", "Draws", "2" },
            Missed(TurnReview.Resolve(State(Me(new[] { "WAH", "CHO" }), wah, cho, motif)), "melodyLate").Single().Args);
    }

    [Fact]
    public void Lullaby_AfterTheThreeTags_LostItsBlock()
    {
        var wah = P("WAH_TOUCH", target: Enemy, hits: 1, tags: new[] { "WAH" });
        var choKi = P("CHO_KI", kind: CardKind.Skill, tags: new[] { "CHO", "KI" }, before: new[] { "WAH" });
        var lullaby = P("LULLABY", energy: 0, kind: CardKind.Power, before: new[] { "WAH", "CHO", "KI" });
        lullaby.MelodyUnit = 20;
        Hold(choKi, lullaby);
        var player = new TurnPlayer { Actor = A, Incoming = 30, MelodyTags = new() { "WAH", "CHO", "KI" } };
        Assert.Equal(new[] { "Card:CHO_KI", "0", "20", "Card:LULLABY" }, Missed(TurnReview.Resolve(State(player, wah, choKi, lullaby)), "orderTurn").Single().Args);
    }

    [Fact]
    public void KiVerse_WithNoMelodyCardAfterIt_IsUnused()
    {
        var verse = P("KI_VERSE", kind: CardKind.Skill, tags: new[] { "KI" });
        verse.PowersGiven.Add("Power:KI_VERSE_POWER");
        var player = new TurnPlayer { Actor = A, UnusedBuffs = new() { "Power:KI_VERSE_POWER" }, MelodyTags = new() { "KI" } };
        var r = TurnReview.Resolve(State(player, verse));
        Assert.Equal(new[] { "Card:KI_VERSE", "1" }, Missed(r, "verseUnused").Single().Args);
        Assert.Empty(Missed(r, "buffUnused"));
        // Vers Ki+ with one melody card after it: partly used, its power is still there.
        var wah = P("WAH_TOUCH", target: Enemy, hits: 1, tags: new[] { "WAH" }, before: new[] { "KI" });
        Assert.Empty(Missed(TurnReview.Resolve(State(player, verse, wah)), "verseUnused"));
    }

    [Fact]
    public void ConductorsBaton_OneTagShort_WhenTheBlockWasShort()
    {
        TurnPlayer Player(int block) => new()
        {
            Actor = A, Energy = 1, Incoming = 10, Block = block, MelodyTags = new() { "WAH", "CHO" },
            Relics = { new TurnRelic("Relic:CONDUCTORS_BATON", RelicTrigger.AllMelodies, 3, GivesBlock: true) },
            Hand = { Card("KI_CHANT", tags: new[] { "KI" }) }
        };
        Assert.Equal(new[] { "Relic:CONDUCTORS_BATON" }, Missed(TurnReview.Resolve(State(Player(0))), "relic").Single().Args);
        Assert.Empty(Missed(TurnReview.Resolve(State(Player(10))), "relic"));
    }

    [Fact]
    public void Tune_NoKiPlayed_WithAKiCardInHand()
    {
        var player = new TurnPlayer
        {
            Actor = A, Energy = 1, Relics = { new TurnRelic("Power:TUNE_POWER", RelicTrigger.KiMelody, 1) }, Hand = { Card("KI_CHANT", tags: new[] { "KI" }) }
        };
        Assert.Single(Missed(TurnReview.Resolve(State(player)), "relic"));
    }

    [Fact]
    public void EpicRetrieval_AnotherEnemyWasOneHitFromFreeingAnArrow()
    {
        var retrieval = P("EPIC_RETRIEVAL", energy: 2, target: Enemy, hits: 1, raw: 15);
        retrieval.CardBonus = 3;
        retrieval.ArrowHitsLeft[Enemy] = 2;
        retrieval.ArrowHitsLeft[Other] = 1;
        var player = new TurnPlayer { Actor = A, Hand = { Card("NAIL", cost: 2, kind: CardKind.Attack) } };
        Assert.Equal(new[] { "Card:EPIC_RETRIEVAL", "", "Energy", "3" }, Missed(TurnReview.Resolve(State(player, retrieval)), "retrievalTarget").Single().Args);
        // Nothing the energy would have paid: no loss.
        Assert.Empty(Missed(TurnReview.Resolve(State(Me(), retrieval)), "retrievalTarget"));
        // It freed one (energy back): no mistake.
        retrieval.EnergyGiven = 3;
        Assert.Empty(Missed(TurnReview.Resolve(State(player, retrieval)), "retrievalTarget"));
    }

    [Fact]
    public void EpicRetrieval_AnAttackPlayedAfterItWouldHaveBroughtTheArrowToOneHit()
    {
        var retrieval = P("EPIC_RETRIEVAL", energy: 2, target: Enemy, hits: 1, raw: 15, energyBefore: 3);
        retrieval.CardBonus = 3;
        retrieval.ArrowHitsLeft[Enemy] = 2;
        var strike = P("STRIKE", target: Enemy, hits: 1);
        Hold(retrieval, strike);
        var player = new TurnPlayer { Actor = A, Hand = { Card("NAIL", cost: 2, kind: CardKind.Attack) } };
        Assert.Equal(new[] { "Card:EPIC_RETRIEVAL", "Card:STRIKE", "Energy", "3" },
            Missed(TurnReview.Resolve(State(player, retrieval, strike)), "retrievalOrder").Single().Args);
        // Two hits would have freed the arrow before it: no line.
        var twin = P("TWIN_STRIKE", target: Enemy, hits: 2);
        var retrieval2 = P("EPIC_RETRIEVAL", energy: 2, target: Enemy, hits: 1, raw: 15, energyBefore: 3);
        retrieval2.CardBonus = 3;
        retrieval2.ArrowHitsLeft[Enemy] = 2;
        Hold(retrieval2, twin);
        Assert.Empty(Missed(TurnReview.Resolve(State(player, retrieval2, twin)), "retrievalOrder"));
    }

    [Fact]
    public void ArrowPayoff_OnATargetWithoutAnArrow_WhileAnotherEnemyHadOne()
    {
        var hammer = P("WAH_HAMMER", target: Enemy, hits: 1, raw: 9, tags: new[] { "WAH" });
        hammer.ArrowIn.Add(Other);
        Assert.Equal(new[] { "Card:WAH_HAMMER", "", "Damage", "9" }, Missed(TurnReview.Resolve(State(Me(new[] { "WAH" }), hammer)), "payoffTarget").Single().Args);
        hammer.KilledTarget = true;
        Assert.Empty(Missed(TurnReview.Resolve(State(Me(new[] { "WAH" }), hammer)), "payoffTarget"));

        var hop = P("KI_HOP", target: Enemy, hits: 1, raw: 7, tags: new[] { "KI" });
        hop.CardBonus = 2;
        hop.ArrowIn.Add(Other);
        Assert.Equal(new[] { "Card:KI_HOP", "", "Draws", "2" }, Missed(TurnReview.Resolve(State(Me(new[] { "KI" }), hop)), "payoffTarget").Single().Args);

        var smack = P("BOW_SMACK", target: Enemy, hits: 1, raw: 8);
        smack.CardBonus = 1;
        smack.ArrowIn.Add(Other);
        var player = new TurnPlayer { Actor = A, Hand = { Card("STRIKE", cost: 1, kind: CardKind.Attack) } };
        Assert.Equal(new[] { "Card:BOW_SMACK", "", "Energy", "1" }, Missed(TurnReview.Resolve(State(player, smack)), "payoffTarget").Single().Args);
    }

    [Fact]
    public void Quietus_OnAWeakShot_WhileAStrongerOneWasPlayedLater()
    {
        var quick = P("QUICK_SHOT", energy: 0, target: Enemy, hits: 1, raw: 10);
        quick.ShotArrow = true;
        quick.QuietusAtStart = 2;
        quick.DamageOn[Enemy] = 10;
        var heavy = P("HEAVY_SHOT", energy: 2, target: Enemy, hits: 1, raw: 16);
        heavy.ShotArrow = true;
        heavy.DamageOn[Enemy] = 16;
        Hold(quick, heavy);
        Assert.Equal(new[] { "Card:QUICK_SHOT", "11", "0", "Card:HEAVY_SHOT" }, Missed(TurnReview.Resolve(State(Me(), quick, heavy)), "orderTurn").Single().Args);
    }

    [Fact]
    public void RepeatShot_AtZeroEnergy_AfterDamageOnlyAttacks()
    {
        TurnPlay Repeat(int perHit)
        {
            var x = P("REPEAT_SHOT", energy: 0, target: Enemy, costsX: true, targetLeft: 28);
            x.HitDamage = perHit;
            return x;
        }
        var s1 = P("STRIKE", target: Enemy, hits: 1, raw: 6);
        var s2 = P("STRIKE", target: Enemy, hits: 1, raw: 6);
        var repeat = Repeat(15);
        Hold(s1, repeat);
        Hold(s2, repeat);
        Assert.Equal(new[] { "Card:REPEAT_SHOT", "Card:STRIKE", "2", "30", "12" },
            Missed(TurnReview.Resolve(State(Me(), s1, s2, repeat)), "repeatShotEmpty").Single().Args);
        // Weaker than what was played: no mistake.
        var weak = Repeat(5);
        Hold(s1, weak);
        Hold(s2, weak);
        Assert.Empty(Missed(TurnReview.Resolve(State(Me(), s1, s2, weak)), "repeatShotEmpty"));
        // The attacks also put a debuff: a choice.
        var d1 = P("WAH_TOUCH", target: Enemy, hits: 1, raw: 6);
        d1.HadNonDamageEffect = true;
        var repeat2 = Repeat(15);
        Hold(d1, repeat2);
        Assert.Empty(Missed(TurnReview.Resolve(State(Me(), d1, repeat2)), "repeatShotEmpty"));
    }

    [Fact]
    public void CoveringFire_CreatedByAbundanceAfterAShot_CountsInsteadOfMakerFirst()
    {
        var shot = P("FOOLISH_SHOT", target: Enemy, hits: 1, energyBefore: 3);
        shot.ShotArrow = true;
        var abundance = P("ABUNDANCE^", kind: CardKind.Skill, energyBefore: 2);
        Hold(shot, abundance);
        var cf = P("COVERING_FIRE^", energy: 0, kind: CardKind.Power);
        cf.AllyBlockPerShot = 7;
        abundance.Created.Add(cf.CardRef);
        var players = new[] { new TurnPlayer { Actor = A }, new TurnPlayer { Actor = B, Incoming = 12 } };
        var r = TurnReview.Resolve(State(players, shot, abundance, cf));
        Assert.Equal(new[] { "Card:ABUNDANCE^", "Card:FOOLISH_SHOT", "7" }, Missed(r, "coveringFireLate").Single().Args);
        Assert.Empty(Missed(r, "makerFirst"));
    }
}
