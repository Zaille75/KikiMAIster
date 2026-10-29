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

/// Seconds: real time since the fight started (v0.17.39; 0 = unknown).
public sealed record CombatEnded(bool Victory, double Seconds = 0) : StatEvent;

/// One damage instance. Hp excludes overkill. For damage-over-time, Dealer is None and Source is Dot.
/// Kind (v0.17.39), on a player only: "attack" (an enemy's move), "reaction" (an enemy's power: Thorns…), "status" (a status
/// or curse card), "dot" (Poison, Doom… on the player); "" = from the dealer (self, ally, other).
/// OverkillAvoidable (v0.17.42): the overkill came from a single-target attack the player aimed while another enemy was
/// alive (else unavoidable: last enemy, all-enemies or random hits, powers, relics…).
public sealed record DamageDealt(
    Actor Dealer, Actor Target, int Blocked, int Hp, int Overkill, bool Killed, Source Source, string Kind = "",
    bool OverkillAvoidable = false) : StatEvent;

/// Block added on Receiver. Giver is the player who created it (card owner, potion user…).
/// PlayId: the card play that gave the block (0 = not a card the player chose: relic, power, potion, auto-play).
public sealed record BlockGained(Actor Receiver, Actor Giver, int Amount, Source Source, int PlayId = 0) : StatEvent;

/// HP given to a pet (summon, max HP gain, heal). Giver: the player who gave it. Separate from block:
/// never wasted or useless.
/// Owner: the player whose pet got the HP (None = the giver's own pet).
public sealed record PetHpGiven(Actor Giver, double Amount, Actor Owner = default) : StatEvent;

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
public sealed class WastedPlay(int playId, int round, BlockChoice choice, int combat = 0)
{
    public int PlayId { get; } = playId;
    public int Round { get; } = round;
    /// The fight's number in the run (shown in the run scope).
    public int Combat { get; } = combat;
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
/// EnabledBy: an ally whose buff made the applier apply it (Concoct: Poison when the holder hits); the damage stays the
/// applier's, the ally gets it as damage enabled. EnabledSource: that buff ("CONCOCT_POWER").
public sealed record PowerChanged(Actor Applier, Actor Target, string PowerId, double Amount, bool IsDebuff, string Origin = "",
    Actor EnabledBy = default, string EnabledSource = "") : StatEvent;

/// Source (v0.17.39): what healed ("Relic:BURNING_BLOOD", "Card:…", "Potion:…"); "" = unknown.
public sealed record Healed(Actor Healer, Actor Target, int Amount, string Source = "") : StatEvent;

/// A debuff changed a damage instance (spec 01-metrics §B2). Holder: the creature carrying the debuff.
/// Beneficiary: the player (or pet) who dealt more damage (Offense: Vulnerable on an enemy, Bully's bonus) or took
/// less (Weak / Strength down on the attacking enemy). Credit: who is responsible (see DebuffCredit).
/// Applier: for Credit = Direct, the player responsible (the power remembers who applied it: Flanking, One for All…).
public sealed record DebuffEffect(Actor Holder, string PowerId, Actor Beneficiary, double Amount, bool Offense, DebuffCredit Credit,
    Actor Applier = default) : StatEvent;

/// CurrentTurn: the applier of the current turn of a duration debuff (Vulnerable, Weak: first in, first out).
/// RemainingTurns: every applier, by the turns each still has in the queue (effects that use all the stacks at once:
/// Bully, Dominate). Stacks: every applier by stacks, over all "<PowerId>…" keys (Strength down).
/// Direct: the Applier given with the effect.
public enum DebuffCredit { CurrentTurn, RemainingTurns, Stacks, Direct }

/// A pet's attack whose damage grows with its HP (v0.17.5): FromCurrentHp / FromMaxHp = the part of the damage dealt
/// due to the pet's current HP (Déchaînement) or max HP (Protecteur); PetHp = its current HP at the hit.
public sealed record PetHpAttack(Actor Pet, double FromCurrentHp, double FromMaxHp, double PetHp) : StatEvent;

/// The block just gained by Receiver was computed from its pet's max HP (Sacrifice: 3 × max HP, v0.17.6): the allies
/// who gave the pet HP enabled that block, in the same proportions.
public sealed record PetHpBlock(Actor Receiver) : StatEvent;

/// A player gave Strength to another player (Blaze…): those points count for the giver for the rest of the combat.
public sealed record StrengthGiven(Actor Player, Actor Giver, double Amount) : StatEvent;

/// A player gained Strength from a debuff on an enemy (Dominate: Strength = the target's Vulnerable). Those Strength
/// points remember who applied the debuff (by remaining turns) for the rest of the combat.
public sealed record StrengthBorrowed(Actor Player, Actor Holder, string PowerId, double Amount) : StatEvent;

/// A real attack hit of this player (or pet) got Damage extra from their Strength (Strength points in total): the
/// share of borrowed Strength is damage enabled for whoever it was borrowed from.
/// Temporary: Strength points from temporary powers given by another player (Coordinate), by giver.
public sealed record StrengthUsed(Actor Player, double Strength, double Damage, IReadOnlyList<(Actor From, double Points)>? Temporary = null) : StatEvent;

/// A player could have killed every enemy with the attacks in hand at the start of the turn (front hand),
/// but the enemies were still alive at the end of it. Cards: "Card:ID" keys of the lethal combination.
public sealed record MissedLethal(Actor Player, IReadOnlyList<string> Cards, int EnemiesHp) : StatEvent;

/// A player played an attack while a stronger one (same cost or cheaper, same target) stayed in hand unplayed.
public sealed record WeakAttack(Actor Player, string Played, int PlayedDamage, string Better, int BetterDamage) : StatEvent;

public enum CardAction { Drawn, Played, Exhausted, Discarded }

/// Repeat: the same card played again by Déflagration, Duplication, Forme résonnante… (v0.17.38): not a new card played.
public sealed record CardEvent(Actor Player, CardAction Action, bool Repeat = false) : StatEvent;

/// A card play is over (its hits and the cards it played itself included): closes the "best card play" count.
public sealed record CardPlayFinished(Actor Player) : StatEvent;

public sealed record PotionUsed(Actor Player) : StatEvent;

/// A player died (in co-op the others keep playing).
public sealed record PlayerDied(Actor Player) : StatEvent;

/// The run ended (victory or defeat): the end-of-run summary can be shown.
public sealed record RunEnded(bool Victory) : StatEvent;

/// Several players picked the same treasure relic and the game settled it with rock-paper-scissors (spec 01-metrics
/// §C4). FightId: identifies the fight in the run (a reloaded treasure room is not counted twice).
public sealed record ChifumiFought(string FightId, Actor Winner, IReadOnlyList<Actor> Players) : StatEvent;

/// What the game's map history says about one player so far in the run: Max HP gained and lost, campfire choices
/// (option id → times), HP lost and healed outside fights by kind of floor ("event", "restSite", "other"; v0.17.34–35).
/// Replaces the previous values (the history is complete each time).
public sealed record RunHistoryRead(Actor Player, double MaxHpGained, double MaxHpLost, IReadOnlyDictionary<string, double> RestSiteChoices,
    IReadOnlyDictionary<string, double>? HpLostOutside = null, IReadOnlyDictionary<string, double>? HealedOutside = null,
    RunEconomy? Economy = null) : StatEvent;

/// Gold and curses over the run so far, from the game's map history (v0.17.36). Lost: lost + stolen. Peak: most gold held
/// at the end of a floor. Now: gold held at the end of the last floor. Purchases: relics, potions and colorless cards bought.
public sealed record RunEconomy(double GoldGained, double GoldSpent, double GoldLost, double GoldStolen, double GoldPeak, double GoldNow,
    double CursesGained, double CursesRemoved, double Purchases);

/// HP lost or healed by an event just before the fight it starts (heal 22 then fight…, v0.17.36): the player's HP when the
/// fight starts minus their HP at the end of the previous floor. Key: the floor, counted once (fight reloaded…).
public sealed record PreFightEvent(Actor Player, string Key, double Lost, double Healed) : StatEvent;

/// Part of a hit's damage that came from something on the card itself (Key "Enchantment:<ID>": Tranchant, Tezcatara's
/// ember…, v0.17.39), for its share of the HP the hit removed. Dealer: the player (or pet) hitting.
public sealed record DamagePart(Actor Dealer, string Key, double Amount) : StatEvent;

/// HP healed right after a won fight (Sang ardent, Sang noir, Os à viande…, v0.17.38): outside the fight, counted with the
/// heals outside fights (kind "afterFight").
public sealed record AfterFightHealed(Actor Player, double Amount) : StatEvent;

/// Part of the block Receiver just gained was added by Enabler's help (Oubli: temporary Dexterity given by an ally,
/// v0.17.38): "Armure permise" for that ally, and what it stops is HP they saved.
public sealed record BlockEnabled(Actor Receiver, Actor Enabler, double Amount) : StatEvent;

/// A missed lethal turn, kept for its explanation (not saved with the run). Combat: the fight's number in the run.
public sealed record MissedLethalTurn(int Round, IReadOnlyList<string> Cards, int EnemiesHp, int Combat = 0);

/// A weaker attack played while a stronger one stayed in hand (not saved with the run).
public sealed record WeakAttackPlay(int Round, string Played, int PlayedDamage, string Better, int BetterDamage, int Combat = 0);

/// Another missed opportunity (spec 01-metrics §E0). Kind: "smallBlock", "penNib", "orichalcum", "growth", "energy",
/// "relic", "potion", "order" (damage lost by playing in the wrong order), "orderBlock" (block lost), "orderSlow" (a
/// non-attack card played after an attack it would have boosted through Slow). Args: values for its explanation ("Card:ID"/"Relic:ID" keys are shown with their game names).
/// Amount: what is counted (1 per occurrence, energy points for "energy", potions for "potion").
public sealed record MissedChance(Actor Player, string Kind, IReadOnlyList<string> Args, double Amount = 1) : StatEvent;

/// End of a player turn with a playable block card kept in hand: if the player then loses HP to enemies before the
/// next round, min(HP lost, Block) counts as "HP that could have been saved" (kind "blockKept").
/// Risky (v0.17.23, La grande manœuvre: any attack getting through afterwards kills): counted only if HP were lost this
/// round and none the next one.
public sealed record BlockCardKept(Actor Player, string Card, int Block, bool Risky = false) : StatEvent;

/// End of a player turn with a playable block card for allies kept in hand (Lift, Rally…), an ally being alive: the HP
/// allies then lose to enemies before the next round could have been saved, up to Block (kind "allyBlockKept").
/// AllAllies: the card protects every ally (sum of their losses), else one ally (the most one ally lost).
public sealed record AllyBlockCardKept(Actor Player, string Card, int Block, bool AllAllies) : StatEvent;

/// A missed opportunity kept for its explanation (not saved with the run).
public sealed record MissedEntry(int Round, string Kind, IReadOnlyList<string> Args, double Amount, int Combat = 0);

/// Help that is not damage or block (spec 01-metrics §C2): Kind "energy" (energy given), "cards" (cards drawn, created
/// or brought to an ally's hand), "orbs" (orbs channeled for an ally), "taken" (damage taken instead of allies: Intercept),
/// "energySurplus" (not help: energy lost with nothing useful to spend it on, counted for the player themself),
/// "energyUseless" (not help: energy spent on a card that did nothing useful, v0.17.37).
public sealed record HelpGiven(Actor Giver, string Kind, double Amount) : StatEvent;
