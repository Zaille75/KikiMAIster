using KikiMAIster.Core;
using Xunit;

namespace KikiMAIster.Core.Tests;

/// v0.17.41: the end-of-turn misplay rules (TurnReview), tested on hand-made turn snapshots.
public class TurnReviewTests
{
    private static readonly Actor A = Actor.Player(1, 1);
    private static readonly object Enemy = new();
    private int _nextId;

    private static TurnCard Card(string id, CardKind kind = CardKind.Attack, int cost = 1, int block = 0, bool kept = false,
        int damage = 6, bool growing = false, double thorns = 0) => new()
    {
        Ref = new object(), Key = $"Card:{id}", Kind = kind, Cost = cost, GainsBlock = block > 0, Block = block, KeptOnPurpose = kept,
        Growing = growing, Thorns = thorns, HitsEnemies = kind == CardKind.Attack,
        DamageOn = kind == CardKind.Attack ? new Dictionary<object, int> { [Enemy] = damage } : new Dictionary<object, int>()
    };

    private TurnPlay Play(TurnCard card, Actor? owner = null, int energy = 1, int blockGiven = 0, IEnumerable<TurnCard>? hand = null,
        int stars = 0) => new()
    {
        Id = ++_nextId, Owner = owner ?? A, CardRef = card.Ref, CardKey = card.Key, Kind = card.Kind, EnergySpent = energy, StarsSpent = stars,
        GaveBlock = blockGiven > 0, BlockAmount = blockGiven, BlockGains = blockGiven > 0 ? 1 : 0,
        UsefulCardsInHand = (hand ?? Array.Empty<TurnCard>()).Where(c => c.Useful).Select(c => new SeenCard(c.Ref, c.Key)).ToList(),
        HandAtPlay = (hand ?? Array.Empty<TurnCard>()).Select(c => c.Ref).ToList()
    };

    private static TurnPlayer Player(params TurnCard[] hand) => new() { Actor = A, Energy = 0, Hand = hand.ToList() };

    private static TurnResult Resolve(TurnPlayer player, params TurnPlay[] plays)
    {
        var s = new TurnState { Players = { player }, Plays = plays.ToList() };
        s.EnemiesLeft[Enemy] = 30;
        return TurnReview.Resolve(s);
    }

    private static List<MissedChance> Missed(TurnResult r, string kind) => r.Events.OfType<MissedChance>().Where(m => m.Kind == kind).ToList();

    [Fact]
    public void GrowingCard_AffordableAtTurnEnd_IsReported()
    {
        var claw = Card("CLAW", cost: 0, growing: true);
        var r = Resolve(Player(claw));
        Assert.Contains(claw.Ref, r.GrowingAffordable);
    }

    [Fact]
    public void NewestBlockPlay_GetsTheOnlyAlternative()
    {
        var neutralize = Card("NEUTRALIZE", cost: 1);
        var defend = Card("DEFEND", CardKind.Skill, block: 5);
        var p1 = Play(defend, blockGiven: 5, hand: new[] { neutralize });
        var p2 = Play(defend, blockGiven: 5, hand: new[] { neutralize });
        var player = new TurnPlayer { Actor = A, Energy = 0, Block = 10, Incoming = 0, Hand = { neutralize } };
        var resolved = Resolve(player, p1, p2).Events.OfType<BlockPlayResolved>().ToDictionary(e => e.PlayId);
        Assert.True(resolved[p2.Id].HadAlternative);
        Assert.False(resolved[p1.Id].HadAlternative);
        Assert.Equal("Card:NEUTRALIZE", resolved[p2.Id].Choice!.Alternative);
    }

    [Fact]
    public void BlockNotNeeded_IsEnergySpentForNothing()
    {
        var defend = Card("DEFEND", CardKind.Skill, block: 5);
        var player = new TurnPlayer { Actor = A, Block = 5, Incoming = 0 };
        var r = Resolve(player, Play(defend, blockGiven: 5));
        Assert.Equal(1, r.Events.OfType<HelpGiven>().Single(h => h.Kind == "energyUseless").Amount);
    }

    [Fact]
    public void EnergyLeft_WithAPlayableAttack_IsMissed_ButNotWithOnlyNeedlessBlock()
    {
        var strike = Card("STRIKE");
        var r = Resolve(new TurnPlayer { Actor = A, Energy = 1, Hand = { strike } });
        Assert.Equal(new[] { "1", "Card:STRIKE" }, Missed(r, "energy").Single().Args);

        var defend = Card("DEFEND", CardKind.Skill, block: 5);
        r = Resolve(new TurnPlayer { Actor = A, Energy = 1, Block = 8, Incoming = 6, Hand = { defend } });
        Assert.Empty(Missed(r, "energy"));
        Assert.Single(r.Events.OfType<HelpGiven>(), h => h.Kind == "energySurplus");
        // The block card kept is still reported (judged later against the HP lost).
        Assert.Single(r.Events.OfType<BlockCardKept>());
    }

    [Fact]
    public void Shuriken_OneAttackShort_IsMissedRelic_UnlessThornsHurtTooMuch()
    {
        var player = new TurnPlayer
        {
            Actor = A, Energy = 1, Hand = { Card("STRIKE") }, Relics = { new TurnRelic("Relic:SHURIKEN", RelicTrigger.Attacks, 3) },
            Plays = { [CardKind.Attack] = 2 }
        };
        Assert.Single(Missed(Resolve(player), "relic"));

        var thorny = new TurnPlayer
        {
            Actor = A, Energy = 1, Hand = { Card("STRIKE", thorns: 3) }, Relics = { new TurnRelic("Relic:SHURIKEN", RelicTrigger.Attacks, 3) },
            Plays = { [CardKind.Attack] = 2 }
        };
        Assert.Empty(Missed(Resolve(thorny), "relic"));
    }

    [Fact]
    public void WrongOrder_CountsOnlyOnEnemiesAlive_UpToWhatTheyHaveLeft()
    {
        var strike = Play(Card("STRIKE"));
        var dead = new object();
        var s = new TurnState
        {
            Players = { Player() }, Plays = { strike },
            OrderLosses =
            {
                new OrderLoss(strike, "Card:INFLAME", "order", Enemy, 4),
                new OrderLoss(strike, "Card:INFLAME", "order", dead, 4)
            }
        };
        s.EnemiesLeft[Enemy] = 3;
        var miss = Missed(TurnReview.Resolve(s), "order").Single();
        Assert.Equal(new[] { "Card:STRIKE", "Card:INFLAME", "3" }, miss.Args);
    }

    [Fact]
    public void FreeCardLeft_IsMissed_UnlessKeptOnPurpose()
    {
        Assert.Single(Missed(Resolve(Player(Card("NEUTRALIZE", cost: 0))), "freeCard"));
        Assert.Empty(Missed(Resolve(Player(Card("SHIV", cost: 0, kept: true))), "freeCard"));
    }

    [Fact]
    public void StarBlockNotNeeded_IsStarsWasted()
    {
        var shield = Card("STAR_SHIELD", CardKind.Skill, cost: 0, block: 8);
        var player = new TurnPlayer { Actor = A, Block = 8, Incoming = 0 };
        var r = Resolve(player, Play(shield, energy: 0, blockGiven: 8, stars: 2));
        Assert.Equal(new[] { "Card:STAR_SHIELD", "2" }, Missed(r, "starsWasted").Single().Args);
    }

    [Fact]
    public void WeakAttack_OnlyWhenTheStrongerOneStayedInHand()
    {
        var bash = Card("BASH", damage: 8);
        var weak = Play(Card("STRIKE"));
        weak.Stronger = new Better(bash.Ref, bash.Key, 8, 6);
        Assert.Single(Resolve(Player(bash), weak).Events.OfType<WeakAttack>());
        Assert.Empty(Resolve(Player(), weak).Events.OfType<WeakAttack>());
    }

    [Fact]
    public void AllyBlockCardKept_OnlyWithAnAllyAlive()
    {
        var lift = new TurnCard { Ref = new object(), Key = "Card:LIFT", Kind = CardKind.Skill, GainsBlock = true, Block = 6, BlockFor = BlockTarget.OneAlly };
        var alone = new TurnPlayer { Actor = A, Energy = 1, Hand = { lift } };
        Assert.Empty(Resolve(alone).Events.OfType<AllyBlockCardKept>());
        var withAlly = new TurnPlayer { Actor = A, Energy = 1, Hand = { lift }, AlliesAlive = true };
        var r = Resolve(withAlly);
        Assert.Single(r.Events.OfType<AllyBlockCardKept>());
        Assert.Empty(r.Events.OfType<BlockCardKept>()); // one ally only: not the player's own block
    }
}
