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

public sealed record CombatStarted(IReadOnlyList<PlayerInfo> Players) : StatEvent;

/// A new round starts (player turn then enemy turn = one round).
public sealed record RoundStarted(int Round) : StatEvent;

public sealed record CombatEnded(bool Victory) : StatEvent;

/// One damage instance. Hp excludes overkill. For damage-over-time, Dealer is None and Source is Dot.
public sealed record DamageDealt(
    Actor Dealer, Actor Target, int Blocked, int Hp, int Overkill, bool Killed, Source Source) : StatEvent;

/// Block added on Receiver. Giver is the player who created it (card owner, potion user…).
public sealed record BlockGained(Actor Receiver, Actor Giver, int Amount, Source Source) : StatEvent;

/// All remaining block on the creature disappeared without absorbing damage (turn start, combat end…).
public sealed record BlockCleared(Actor Creature) : StatEvent;

/// The creature's real block after the game cleared it at turn start. Layers above that amount
/// disappeared without absorbing damage (covers full clears and effects that keep some block).
public sealed record BlockObserved(Actor Creature, int ActualBlock) : StatEvent;

/// Part of the block disappeared without absorbing damage (removed by an effect, or spent by a card).
public sealed record BlockRemoved(Actor Creature, int Amount, bool SpentByCard) : StatEvent;

/// A power's amount changed on Target (Amount < 0 = stacks removed, e.g. decay). IsDebuff from the game.
public sealed record PowerChanged(Actor Applier, Actor Target, string PowerId, double Amount, bool IsDebuff) : StatEvent;

public sealed record Healed(Actor Healer, Actor Target, int Amount) : StatEvent;
