using System.Diagnostics;
using System.Runtime.CompilerServices;
using KikiMAIster.Core;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;

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
    /// The same card plays (card, target…).
    public static readonly Stack<CardPlay> CardPlays = new();

    /// Player using a potion right now, if any, and that potion ("Potion:<ID>").
    public static Player? PotionUser;
    public static string PotionId = "";

    /// "Potion:<ID>" while a potion is being used (not inside a card it plays), else "".
    public static string CurrentOrigin => PotionUser != null && CardPlayers.Count == 0 ? PotionId : "";

    /// Damage dealt by a player or pet, and block, whose source was not found this fight (v0.17.41). The source comes from
    /// a call-stack walk that only works before the game command's first await: a game update adding one would turn sources
    /// into "Unknown" with no error, so the count is logged at the end of each fight.
    private static int _unknownDamage, _unknownBlock;
    private static string _unknownExample = "";

    public static void ResetUnknownSources()
    {
        _unknownDamage = _unknownBlock = 0;
        _unknownExample = "";
    }

    public static void LogUnknownSources()
    {
        if (_unknownDamage + _unknownBlock > 0)
            ModLog.Info($"sources unknown this fight: {_unknownDamage} player damage, {_unknownBlock} block (first: {_unknownExample})");
        ResetUnknownSources();
    }

    private static void CountUnknown(StatEvent e)
    {
        switch (e)
        {
            case DamageDealt d when d.Source.Kind == SourceKind.Unknown && d.Dealer.IsPlayerSide:
                _unknownDamage++;
                break;
            case BlockGained b when b.Source.Kind == SourceKind.Unknown && b.Receiver.IsPlayerSide:
                _unknownBlock++;
                break;
            default:
                return;
        }
        if (_unknownExample == "") _unknownExample = e.ToString();
    }

    public static void Emit(StatEvent e)
    {
        try
        {
            CountUnknown(e);
            PlayLog.OnEvent(e);
            Engine.Handle(e);
            // Card plays are shown by the play log (▶ / ◀ lines, v0.17.43): their raw events would only add noise.
            if (Settings.LogEvents && e is not (CardEvent or CardPlayFinished))
                ModLog.Info(e is MissedChance m ? $"EVENT MissedChance {m.Player} {m.Kind} [{string.Join(", ", m.Args)}] {m.Amount}" : $"EVENT {e}");
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
        var isNew = false;
        var id = CreatureIds.GetValue(c, _ =>
        {
            isNew = true;
            return new StrongBox<int>(++_nextCreatureId);
        }).Value;
        if (isNew) PlayLog.Seen(c, id, true);
        if (c.IsPet && c.PetOwner != null) return Actor.Pet(c.PetOwner.NetId, id);
        if (c.IsPlayer) return Actor.Player(c.Player!.NetId, id);
        if (c.IsEnemy) return Actor.Enemy(id);
        return new Actor(ActorKind.None, id);
    }

    public static Actor ToActor(Player? p) => p == null ? Actor.None : ToActor(p.Creature);

    /// The player playing a card (the main branch's CardPlay has no Player: the card's owner).
#if GAME_DEFAULT
    public static Player PlayerOf(CardPlay play) => play.Card.Owner;
#else
    public static Player PlayerOf(CardPlay play) => play.Player;
#endif

    /// The player currently acting through a card or a potion, if any.
    public static Player? CurrentActingPlayer => CardPlayers.Count > 0 ? CardPlayers.Peek() : PotionUser;

    /// Solo: the character name. Co-op: the player's Steam name, so two players of the same character differ.
    public static string PlayerName(Player p, int playerCount)
    {
        if (playerCount > 1)
        {
            try
            {
                var name = PlatformUtil.GetPlayerNameRaw(RunManager.Instance.NetService.Platform, p.NetId);
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
            catch (Exception e)
            {
                ModLog.Error("player name", e);
            }
        }
        return p.Creature.Name;
    }

    public static string IdOf(AbstractModel model)
    {
        UI.GameNames.Register(model.Id.Entry, model.GetType());
        return model.Id.Entry;
    }

    /// True when a method of `type` (or of its async state machines) is on the current call stack.
    public static bool IsInsideCall(Type type)
    {
        foreach (var frame in new StackTrace(1, false).GetFrames())
        {
            var t = frame.GetMethod()?.DeclaringType;
            while (t is { IsNested: true } && t.Name.StartsWith('<')) t = t.DeclaringType;
            if (t == type) return true;
        }
        return false;
    }

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
        UI.GameNames.Register(id, modelType);
        if (typeof(PowerModel).IsAssignableFrom(modelType)) return Source.Power(id);
        if (typeof(RelicModel).IsAssignableFrom(modelType)) return new Source(SourceKind.Relic, id);
        if (typeof(OrbModel).IsAssignableFrom(modelType)) return new Source(SourceKind.Orb, id);
        if (typeof(PotionModel).IsAssignableFrom(modelType)) return Source.Potion(id);
        return Source.Card(id);
    }

    /// The player creature holding a buff of this type that deals damage without a dealer (Haunt…): the player
    /// acting right now if they hold it, else the only holder.
    public static Creature? PowerHolder(Type? modelType, IEnumerable<Player> players)
    {
        if (modelType == null || !typeof(PowerModel).IsAssignableFrom(modelType)) return null;
        var holders = players.Where(p => p.Creature.Powers.Any(x => x.GetType() == modelType)).ToList();
        var acting = CurrentActingPlayer;
        if (acting != null && holders.Contains(acting)) return acting.Creature;
        return holders.Count == 1 ? holders[0].Creature : null;
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
