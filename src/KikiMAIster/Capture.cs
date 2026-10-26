using System.Diagnostics;
using System.Runtime.CompilerServices;
using KikiMAIster.Core;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace KikiMAIster;

/// Shared state of the capture layer: the engine, creature ids and the "who is acting" context.
/// Everything that knows game types lives in this project; the engine only sees Actor/Source/StatEvent.
public static class Capture
{
    public static readonly StatsEngine Engine = new();

    private static readonly ConditionalWeakTable<Creature, StrongBox<int>> CreatureIds = new();
    private static int _nextCreatureId;

    /// Card plays in progress (a card can trigger another card's auto-play).
    public static readonly Stack<Player> CardPlayers = new();

    /// Player using a potion right now, if any.
    public static Player? PotionUser;

    public static void Emit(StatEvent e)
    {
        try
        {
            Engine.Handle(e);
            if (Settings.LogEvents) ModLog.Info($"EVENT {e}");
        }
        catch (Exception ex)
        {
            ModLog.Error($"engine failed on {e}", ex);
        }
    }

    /// Runs a patch body; a mod error must never break the game.
    public static void Safe(string where, Action body)
    {
        try { body(); }
        catch (Exception ex) { ModLog.Error(where, ex); }
    }

    public static Actor ToActor(Creature? c)
    {
        if (c == null) return Actor.None;
        var id = CreatureIds.GetValue(c, _ => new StrongBox<int>(++_nextCreatureId)).Value;
        if (c.IsPet && c.PetOwner != null) return Actor.Pet(c.PetOwner.NetId, id);
        if (c.IsPlayer) return Actor.Player(c.Player!.NetId, id);
        if (c.IsEnemy) return Actor.Enemy(id);
        return new Actor(ActorKind.None, id);
    }

    public static Actor ToActor(Player? p) => p == null ? Actor.None : ToActor(p.Creature);

    /// The player currently acting through a card or a potion, if any.
    public static Player? CurrentActingPlayer => CardPlayers.Count > 0 ? CardPlayers.Peek() : PotionUser;

    public static string IdOf(AbstractModel model) => model.Id.Entry;

    /// Finds which game model (power, relic, orb, potion, card) is calling the current game command,
    /// by walking the call stack. Only valid in a prefix of a command, before its first await.
    public static Type? CallingModelType()
    {
        foreach (var frame in new StackTrace(2, false).GetFrames())
        {
            var type = frame.GetMethod()?.DeclaringType;
            // Async methods run in a compiler-generated nested class (e.g. PoisonPower+<Trigger>d__5).
            while (type is { IsNested: true } && type.Name.StartsWith('<')) type = type.DeclaringType;
            if (type == null) continue;
            if (typeof(MonsterModel).IsAssignableFrom(type)) return null;
            if (typeof(PowerModel).IsAssignableFrom(type) || typeof(RelicModel).IsAssignableFrom(type) ||
                typeof(OrbModel).IsAssignableFrom(type) || typeof(PotionModel).IsAssignableFrom(type) ||
                typeof(CardModel).IsAssignableFrom(type))
                return type;
        }
        return null;
    }

    public static Source SourceOf(Type? modelType)
    {
        if (modelType == null) return Source.Unknown;
        var id = ModelDb.GetId(modelType).Entry;
        if (typeof(PowerModel).IsAssignableFrom(modelType)) return Source.Power(id);
        if (typeof(RelicModel).IsAssignableFrom(modelType)) return new Source(SourceKind.Relic, id);
        if (typeof(OrbModel).IsAssignableFrom(modelType)) return new Source(SourceKind.Orb, id);
        if (typeof(PotionModel).IsAssignableFrom(modelType)) return Source.Potion(id);
        return Source.Card(id);
    }

    /// The player who owns a power or relic of this type, if exactly one player does.
    public static Player? UniqueOwnerOf(Type? modelType, IEnumerable<Player> players)
    {
        if (modelType == null) return null;
        var owners = players.Where(p =>
            p.Creature.Powers.Any(x => x.GetType() == modelType) ||
            p.Relics.Any(x => x.GetType() == modelType)).Take(2).ToList();
        return owners.Count == 1 ? owners[0] : null;
    }
}
