namespace KikiMAIster.Core;

public enum ActorKind { None, Player, Pet, Enemy }

/// A creature as seen by the engine. Pets carry their owner's player id.
public readonly record struct Actor(ActorKind Kind, int CreatureId, ulong PlayerId = 0)
{
    public static readonly Actor None = new(ActorKind.None, 0);

    public static Actor Player(ulong playerId, int creatureId) => new(ActorKind.Player, creatureId, playerId);
    public static Actor Pet(ulong ownerId, int creatureId) => new(ActorKind.Pet, creatureId, ownerId);
    public static Actor Enemy(int creatureId) => new(ActorKind.Enemy, creatureId);

    /// True for players and pets: the player this actor counts for.
    public bool IsPlayerSide => Kind is ActorKind.Player or ActorKind.Pet;
    public bool IsEnemy => Kind == ActorKind.Enemy;
}

public enum SourceKind { Card, Power, DamageOverTime, Pet, Potion, Relic, Orb, Unknown }

/// What caused a damage, block, power or heal. Id is the game's model id (e.g. "STRIKE_IRONCLAD").
public readonly record struct Source(SourceKind Kind, string Id = "")
{
    public static readonly Source Unknown = new(SourceKind.Unknown);
    public static Source Card(string id) => new(SourceKind.Card, id);
    public static Source Power(string id) => new(SourceKind.Power, id);
    public static Source Dot(string powerId) => new(SourceKind.DamageOverTime, powerId);
    public static Source Potion(string id) => new(SourceKind.Potion, id);

    public override string ToString() => Id == "" ? Kind.ToString() : $"{Kind}:{Id}";
}

/// Neutral events produced by the capture layer (Patches/) and consumed by the StatsEngine.
public abstract record StatEvent;

public sealed record RunStarted : StatEvent;

public sealed record PlayerInfo(ulong PlayerId, string Name, string Character);

/// Name: the encounter's name in the game language ("" if unknown).
public sealed record CombatStarted(IReadOnlyList<PlayerInfo> Players, string Name = "") : StatEvent;

/// A new round starts (player turn then enemy turn = one round).
public sealed record RoundStarted(int Round) : StatEvent;

public sealed record CombatEnded(bool Victory) : StatEvent;

/// One damage instance. Hp excludes overkill. For damage-over-time, Dealer is None and Source is Dot.
public sealed record DamageDealt(
    Actor Dealer, Actor Target, int Blocked, int Hp, int Overkill, bool Killed, Source Source) : StatEvent;

/// Block added on Receiver. Giver is the player who created it (card owner, potion user…).
/// PlayId: the card play that gave the block (0 = not a card the player chose: relic, power, potion, auto-play).
public sealed record BlockGained(Actor Receiver, Actor Giver, int Amount, Source Source, int PlayId = 0) : StatEvent;

/// HP given to a pet (summon, max HP gain, heal). Giver: the player who gave it. Separate from block:
/// never wasted or useless.
public sealed record PetHpGiven(Actor Giver, double Amount) : StatEvent;

/// HP loss reduced by a relic or power (Tungsten Rod, Intangible, Buffer…) on a player or pet. SourceId: the
/// model that reduced it ("Relic:TUNGSTEN_ROD"…).
public sealed record HpLossPrevented(Actor Target, double Amount, string SourceId) : StatEvent;

/// End of the player's turn: whether a block card play had a real alternative (spec 01-metrics §F, "wasted" vs "useless").
/// Choice: why it had an alternative (for the explanation shown on wasted block), when it had one.
public sealed record BlockPlayResolved(int PlayId, bool HadAlternative, BlockChoice? Choice = null) : StatEvent;

/// A block card played while a useful card stayed in hand. Card ids are "Card:ID" keys.
/// UsefulInHand: useful cards still unplayed in hand at the end of the turn. BlockAtTurnEnd: the player's block
/// when the turn ended. IncomingDamage: attack damage the enemies announced against that player.
public sealed record BlockChoice(string BlockCard, string Alternative, IReadOnlyList<string> UsefulInHand,
    int BlockAtTurnEnd, int IncomingDamage);

/// Wasted block of one block card play, with the reason (spec 01-metrics §F).
public sealed class WastedPlay(int playId, int round, BlockChoice choice)
{
    public int PlayId { get; } = playId;
    public int Round { get; } = round;
    public BlockChoice Choice { get; } = choice;
    public double Amount { get; set; }
}

/// All remaining block on the creature disappeared without absorbing damage (turn start, combat end…).
public sealed record BlockCleared(Actor Creature) : StatEvent;

/// The creature's real block after the game cleared it at turn start. Layers above that amount
/// disappeared without absorbing damage (covers full clears and effects that keep some block).
public sealed record BlockObserved(Actor Creature, int ActualBlock) : StatEvent;

/// Part of the block disappeared without absorbing damage (removed by an effect, or spent by a card).
public sealed record BlockRemoved(Actor Creature, int Amount, bool SpentByCard) : StatEvent;

/// A power's amount changed on Target (Amount < 0 = stacks removed, e.g. decay). IsDebuff from the game.
/// Origin: "Potion:<ID>" when a potion applied it (its damage then counts as potion damage), else "".
public sealed record PowerChanged(Actor Applier, Actor Target, string PowerId, double Amount, bool IsDebuff, string Origin = "") : StatEvent;

public sealed record Healed(Actor Healer, Actor Target, int Amount) : StatEvent;

/// A debuff changed a damage instance (spec 01-metrics §B2). Holder: the creature carrying the debuff.
/// Beneficiary: the player (or pet) who dealt more damage (Offense: Vulnerable on an enemy, Bully's bonus) or took
/// less (Weak / Strength down on the attacking enemy). Credit: who is responsible (see DebuffCredit).
public sealed record DebuffEffect(Actor Holder, string PowerId, Actor Beneficiary, double Amount, bool Offense, DebuffCredit Credit) : StatEvent;

/// CurrentTurn: the applier of the current turn of a duration debuff (Vulnerable, Weak: first in, first out).
/// RemainingTurns: every applier, by the turns each still has in the queue (effects that use all the stacks at once:
/// Bully, Dominate). Stacks: every applier by stacks, over all "<PowerId>…" keys (Strength down).
public enum DebuffCredit { CurrentTurn, RemainingTurns, Stacks }

/// A player gained Strength from a debuff on an enemy (Dominate: Strength = the target's Vulnerable). Those Strength
/// points remember who applied the debuff (by remaining turns) for the rest of the combat.
public sealed record StrengthBorrowed(Actor Player, Actor Holder, string PowerId, double Amount) : StatEvent;

/// A real attack hit of this player (or pet) got Damage extra from their Strength (Strength points in total): the
/// share of borrowed Strength is damage enabled for whoever it was borrowed from.
public sealed record StrengthUsed(Actor Player, double Strength, double Damage) : StatEvent;

/// A player could have killed every enemy with the attacks in hand at the start of the turn (front hand),
/// but the enemies were still alive at the end of it. Cards: "Card:ID" keys of the lethal combination.
public sealed record MissedLethal(Actor Player, IReadOnlyList<string> Cards, int EnemiesHp) : StatEvent;

/// A player played an attack while a stronger one (same cost or cheaper, same target) stayed in hand unplayed.
public sealed record WeakAttack(Actor Player, string Played, int PlayedDamage, string Better, int BetterDamage) : StatEvent;

public enum CardAction { Drawn, Played, Exhausted, Discarded }

public sealed record CardEvent(Actor Player, CardAction Action) : StatEvent;

public sealed record PotionUsed(Actor Player) : StatEvent;

/// A player died (in co-op the others keep playing).
public sealed record PlayerDied(Actor Player) : StatEvent;

/// The run ended (victory or defeat): the end-of-run summary can be shown.
public sealed record RunEnded(bool Victory) : StatEvent;

/// A missed lethal turn, kept for its explanation (not saved with the run).
public sealed record MissedLethalTurn(int Round, IReadOnlyList<string> Cards, int EnemiesHp);

/// A weaker attack played while a stronger one stayed in hand (not saved with the run).
public sealed record WeakAttackPlay(int Round, string Played, int PlayedDamage, string Better, int BetterDamage);

/// Another missed opportunity (spec 01-metrics §E0). Kind: "smallBlock", "penNib", "orichalcum", "growth", "energy",
/// "relic", "potion". Args: values for its explanation ("Card:ID"/"Relic:ID" keys are shown with their game names).
/// Amount: what is counted (1 per occurrence, energy points for "energy", potions for "potion").
public sealed record MissedChance(Actor Player, string Kind, IReadOnlyList<string> Args, double Amount = 1) : StatEvent;

/// End of a player turn with a playable block card kept in hand: if the player then loses HP to enemies before the
/// next round, min(HP lost, Block) counts as "HP that could have been saved" (kind "blockKept").
public sealed record BlockCardKept(Actor Player, string Card, int Block) : StatEvent;

/// A missed opportunity kept for its explanation (not saved with the run).
public sealed record MissedEntry(int Round, string Kind, IReadOnlyList<string> Args, double Amount);
