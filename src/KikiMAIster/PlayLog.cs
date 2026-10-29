using System.Globalization;
using System.Text;
using KikiMAIster.Core;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using KikiMAIster.UI;

namespace KikiMAIster;

/// A readable play-by-play of each fight in the mod's log (v0.17.43, Zaille75), so every stat can be checked afterwards
/// against the cards really played, in solo and co-op:
///   ═══ fight start: players, enemies;
///   ─── each player's turn start: HP, block, energy, Stars, hand, piles, powers; enemies and their intents (damage per player);
///   ▶ a card played (by whom, target, energy / Stars paid, energy left), then ◀ what it did (damage, block, powers, heals);
///   ⚗ a potion; ■ each player's turn end (energy and Stars left, block, incoming attack, cards kept).
/// Creatures are named in every EVENT line too: players by name (co-op: Steam name / character), pets "Osty (name)", enemies
/// "Larve#7" (the number tells two enemies of the same kind apart).
public static class PlayLog
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly Dictionary<int, string> Names = new();

    /// The plays in progress (a card can play other cards): who, which card, and what happened during it.
    private static readonly Stack<(string Who, string Card, List<string> Effects)> Open = new();
    private static int _roundLogged = -1;

    public static void Install() => Actor.Namer = NameOf;

    private static string NameOf(Actor a)
    {
        if (Names.TryGetValue(a.CreatureId, out var n)) return n;
        // A player or pet first seen before the run was ready (v0.17.45): named as soon as the game knows them.
        if (a.Kind is ActorKind.Player or ActorKind.Pet && PlayerById(a.PlayerId) is { } player)
        {
            try
            {
                var name = a.Kind == ActorKind.Player ? Who(player) : $"{player.Osty?.Name ?? "Pet"} ({Who(player)})";
                if (!name.Contains('?')) Names[a.CreatureId] = name;
                return name;
            }
            catch (Exception)
            {
                // Not ready yet: the id below.
            }
        }
        return a.Kind switch
        {
            ActorKind.None => "-",
            ActorKind.Enemy => a.CreatureId == 0 ? "an enemy" : $"Enemy#{a.CreatureId}",
            _ => $"{a.Kind}#{a.CreatureId}"
        };
    }

    private static Player? PlayerById(ulong id)
    {
        try
        {
            return Patches.CombatFlowPatches.CurrentRun?.Players.FirstOrDefault(p => p.NetId == id);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// Remembers a creature's readable name (called when the mod first sees it); enemies joining a fight are logged.
    public static void Seen(Creature c, int id, bool isNew)
    {
        try
        {
            if (Names.ContainsKey(id) && !isNew) return;
            // Players and pets are named when needed (NameOf): early in a run the game cannot give their names yet.
            if (c.IsPlayer || c.IsPet) return;
            var name = $"{c.Name}#{id.ToString(Inv)}";
            Names[id] = name;
            if (isNew && c.IsEnemy && Settings.LogEvents)
                ModLog.Info($"+ {name} HP {c.CurrentHp}/{c.MaxHp}, block {c.Block}, powers {Powers(c)}");
        }
        catch (Exception e)
        {
            ModLog.Error("play log name", e);
        }
    }

    /// "Régent" in solo, "Max/Régent" in co-op; "?" when the game cannot tell yet.
    public static string Who(Player p)
    {
        try
        {
            var count = p.RunState?.Players.Count ?? 1;
            var character = GameNames.Of($"Character:{p.Character.Id.Entry}");
            return count > 1 ? $"{Capture.PlayerName(p, count)}/{character}" : character;
        }
        catch (Exception)
        {
            return "?";
        }
    }

    /// The game's title, upgrade included ("Frappe+"), and the card's id.
    private static string Card(CardModel c) => $"{c.Title} [{c.Id.Entry}]";
    private static string Cards(IEnumerable<CardModel> cards) => string.Join(", ", cards.Select(c => c.Title));
    private static string Powers(Creature c) =>
        c.Powers.Count == 0 ? "-" : string.Join(", ", c.Powers.Select(p => $"{p.Title.GetFormattedText()} {p.Amount.ToString(Inv)}"));
    private static string Creature(Creature c) => NameOf(Capture.ToActor(c));

    private static void Log(string text)
    {
        if (Settings.LogEvents) ModLog.Info(text);
    }

    public static void CombatStarted(ICombatState? combat, IReadOnlyList<Player> players, string encounter) => Capture.Safe("play log", () =>
    {
        _roundLogged = -1;
        Open.Clear();
        Log($"═══ fight: {encounter} · players: {string.Join(", ", players.Select(p => $"{Who(p)} HP {p.Creature.CurrentHp}/{p.Creature.MaxHp}"))}");
        foreach (var e in combat?.Enemies ?? Array.Empty<Creature>()) Capture.ToActor(e);
    });

    /// A player's hand is drawn: their state, and once per round the enemies and what they will do to each player.
    public static void TurnStarted(Player player) => Capture.Safe("play log", () =>
    {
        if (player.PlayerCombatState is not { } st || player.Creature.CombatState is not { } combat) return;
        if (combat.RoundNumber != _roundLogged)
        {
            _roundLogged = combat.RoundNumber;
            var sb = new StringBuilder($"─── round {combat.RoundNumber.ToString(Inv)}");
            foreach (var e in combat.Enemies.Where(e => e.IsAlive))
                sb.Append($"\n    {Creature(e)}: HP {e.CurrentHp}/{e.MaxHp}, block {e.Block}, powers {Powers(e)}, intent {Intent(e, combat)}");
            Log(sb.ToString());
        }
        var c = player.Creature;
        Log($"─── {Who(player)} turn: HP {c.CurrentHp}/{c.MaxHp}, block {c.Block}, energy {st.Energy}, stars {st.Stars}, " +
            $"powers {Powers(c)}{(player.IsOstyAlive && player.Osty is { } osty ? $", {Creature(osty)} HP {osty.CurrentHp}/{osty.MaxHp} block {osty.Block}" : "")}\n" +
            $"    hand: {Cards(st.Hand.Cards)}\n    draw {st.DrawPile.Cards.Count()}, discard {st.DiscardPile.Cards.Count()}, exhaust {st.ExhaustPile.Cards.Count()}");
    });

    /// The enemy's move: each attack with its damage on every player (co-op: the numbers differ per player).
    private static string Intent(Creature enemy, ICombatState combat)
    {
        try
        {
            if (enemy.Monster?.NextMove is not { } move) return "-";
            var parts = move.Intents.Select(i => i is AttackIntent a && a.DamageCalc is { } calc
                ? $"attack {a.Repeats.ToString(Inv)}×{(int)calc()} base (" + string.Join(", ", combat.Players.Where(p => p.Creature.IsAlive)
                    .Select(p => $"{Who(p)} total {a.GetTotalDamage(new[] { p.Creature }, enemy)}")) + ")"
                : i.GetType().Name.Replace("Intent", ""));
            return $"{move.Id}: {string.Join(", ", parts)}";
        }
        catch (Exception)
        {
            return "?";
        }
    }

    public static void PlayStarted(CardPlay play) => Capture.Safe("play log", () =>
    {
        var player = Capture.PlayerOf(play);
        var who = Who(player);
        var card = Card(play.Card);
        var target = play.Target is { } t ? $" → {Creature(t)}" : "";
        var paid = $"paid {play.Resources.EnergySpent} energy{(play.Resources.StarsSpent > 0 ? $", {play.Resources.StarsSpent} stars" : "")}";
        var left = player.PlayerCombatState is { } st ? $", left {st.Energy} energy, {st.Stars} stars" : "";
        var how = (play.IsAutoPlay ? " (auto-played)" : "") + (play.PlayIndex > 0 ? $" (played again #{play.PlayIndex + 1})" : "");
        Log($"▶ {who}: {card}{target} · {paid}{left}{how}");
        Open.Push((who, card, new List<string>()));
    });

    public static void PlayFinished() => Capture.Safe("play log", () =>
    {
        if (!Open.TryPop(out var play)) return;
        Log($"◀ {play.Who}: {play.Card} → {(play.Effects.Count == 0 ? "nothing seen" : string.Join("; ", play.Effects))}");
    });

    /// A player died during their own card: its plays are closed without their summary.
    public static void PlaysAbandoned()
    {
        while (Open.TryPop(out var play)) Log($"◀ {play.Who}: {play.Card} → interrupted ({string.Join("; ", play.Effects)})");
    }

    /// What happened during the card play in progress, for its summary line.
    public static void OnEvent(StatEvent e)
    {
        if (!Open.TryPeek(out var play)) return;
        var text = e switch
        {
            DamageDealt d => $"{NameOf(d.Target)} −{d.Hp} HP" + (d.Blocked > 0 ? $" −{d.Blocked} block" : "") +
                             (d.Overkill > 0 ? $" +{d.Overkill} overkill{(d.OverkillAvoidable ? " (avoidable)" : "")}" : "") +
                             (d.Killed ? " KILLED" : "") + $" by {NameOf(d.Dealer)} ({d.Source})",
            BlockGained b => $"{NameOf(b.Receiver)} +{b.Amount} block ({b.Source})",
            PowerChanged p => $"{NameOf(p.Target)} {GameNames.Of(p.PowerId)} {(p.Amount > 0 ? "+" : "")}{p.Amount.ToString(Inv)} (by {NameOf(p.Applier)})",
            Healed h => $"{NameOf(h.Target)} +{h.Amount} HP healed",
            // v0.17.45: what used to show as "nothing seen".
            PetHpGiven g => $"{NameOf(g.Owner)}'s pet +{g.Amount} HP",
            HelpGiven { Kind: "energy" } g => $"{NameOf(g.Giver)} gave {g.Amount} energy",
            HelpGiven { Kind: "cards" } g => $"{NameOf(g.Giver)} gave {g.Amount} card",
            DamageCapped c => $"{NameOf(c.Target)} cap stopped {c.Amount:0} damage of {NameOf(c.Dealer)}",
            MissedChance m => $"MISS {m.Kind}",
            _ => null
        };
        if (text != null) play.Effects.Add(text);
    }

    /// Something the card play in progress did that no stat event tells (Stars, energy, cards created; v0.17.45).
    public static void Note(string text)
    {
        if (Open.TryPeek(out var play)) play.Effects.Add(text);
    }

    public static void Potion(PotionModel potion, Creature? target) => Capture.Safe("play log", () =>
        Log($"⚗ {(potion.Owner is { } p ? Who(p) : "?")}: {potion.Title.GetFormattedText()} [{potion.Id.Entry}]{(target != null ? $" → {Creature(target)}" : "")}"));

    /// End of the players' turn: what each player kept (energy, Stars, block, cards) and the attack coming.
    public static void TurnEnded(ICombatState combat) => Capture.Safe("play log", () =>
    {
        foreach (var p in combat.Players.Where(p => p.Creature.IsAlive))
        {
            if (p.PlayerCombatState is not { } st) continue;
            Log($"■ {Who(p)} end of turn: energy {st.Energy}, stars {st.Stars}, block {p.Creature.Block}, " +
                $"incoming {Patches.BlockAlternatives.IncomingDamage(p.Creature)}, hand kept: {(st.Hand.Cards.Count == 0 ? "-" : Cards(st.Hand.Cards))}");
        }
    });
}
