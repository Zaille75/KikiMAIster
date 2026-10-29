using System.Globalization;
using HarmonyLib;
using KikiMAIster.Core;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.TreasureRelicPicking;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace KikiMAIster.Patches;

/// Run-level stats outside combat (spec 01-metrics §C4): Max HP and campfire choices come from the game's own map
/// history (complete after Save & Quit or a crash, never counted twice); rock-paper-scissors fights for a treasure relic
/// are captured when the game settles them (every player's game computes the same result).
public static class RunHistoryPatches
{
    private static string _signature = "";

    /// Reads every player's Max HP changes, HP lost / healed outside fights and campfire choices from the run's map history; emits only when they changed.
    public static void Read(IRunState? run) => Capture.Safe("RunHistory.Read", () =>
    {
        if (run == null) return;
        var totals = run.Players.ToDictionary(p => p.NetId, _ => (Gained: 0, Lost: 0, Choices: new Dictionary<string, double>(),
            HpLost: new Dictionary<string, double>(), Healed: new Dictionary<string, double>(), Eco: new double[9]));
        foreach (var act in run.MapPointHistory)
            foreach (var point in act)
            {
                // HP lost and healed on a floor without a fight (an event that turns into a fight counts as a fight), by
                // kind of floor: events (Neow included), campfires, others (shop, treasure: relics…), v0.17.35.
                var fight = point.Rooms.Any(r => r.RoomType is RoomType.Monster or RoomType.Elite or RoomType.Boss);
                // The Ancients at the start of each act heal to full (shown apart, v0.17.38); Neow first sets HP to 0, so
                // its "heal" is the whole Max HP: left out.
                var ancients = point.Rooms.Select(r => r.ModelId is { } id ? ModelDb.GetByIdOrNull<EventModel>(id) : null)
                    .OfType<AncientEventModel>().ToList();
                var neow = ancients.Any(a => a is Neow);
                var kind = ancients.Count > 0 ? "ancient"
                    : point.Rooms.Any(r => r.RoomType == RoomType.Event) ? "event"
                    : point.Rooms.Any(r => r.RoomType == RoomType.RestSite) ? "restSite" : "other";
                foreach (var entry in point.PlayerStats)
                {
                    if (!totals.TryGetValue(entry.PlayerId, out var t)) continue;
                    t.Gained += entry.MaxHpGained;
                    t.Lost += entry.MaxHpLost;
                    if (!fight && entry.DamageTaken > 0) t.HpLost[kind] = t.HpLost.GetValueOrDefault(kind) + entry.DamageTaken;
                    if (!fight && !neow && entry.HpHealed > 0) t.Healed[kind] = t.Healed.GetValueOrDefault(kind) + entry.HpHealed;
                    // Gold and curses (v0.17.36): gained, spent, lost, stolen, peak, now, curses gained / removed, purchases.
                    t.Eco[0] += entry.GoldGained;
                    t.Eco[1] += entry.GoldSpent;
                    t.Eco[2] += entry.GoldLost;
                    t.Eco[3] += entry.GoldStolen;
                    t.Eco[4] = Math.Max(t.Eco[4], entry.CurrentGold);
                    t.Eco[5] = entry.CurrentGold;
                    t.Eco[6] += entry.CardsGained.Count(IsCurse);
                    t.Eco[7] += entry.CardsRemoved.Count(IsCurse);
                    t.Eco[8] += entry.BoughtRelics.Count + entry.BoughtPotions.Count + entry.BoughtColorless.Count;
                    foreach (var choice in entry.RestSiteChoices) t.Choices[choice] = t.Choices.GetValueOrDefault(choice) + 1;
                    totals[entry.PlayerId] = t;
                }
            }
        var signature = string.Join(';', totals.OrderBy(kv => kv.Key).Select(kv =>
            $"{kv.Key}:{kv.Value.Gained}:{kv.Value.Lost}:{Sig(kv.Value.HpLost)}:{Sig(kv.Value.Healed)}:{string.Join(',', kv.Value.Eco)}:{string.Join(',', kv.Value.Choices.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => $"{r.Key}={r.Value}"))}"));
        if (signature == _signature) return;
        _signature = signature;
        foreach (var p in run.Players)
        {
            var t = totals[p.NetId];
            var e = t.Eco;
            // The event's own text shows dictionaries as type names: the HP figures, readable (v0.17.38).
            ModLog.Info($"run history {p.NetId}: HP lost outside [{Sig(t.HpLost)}], healed outside [{Sig(t.Healed)}]");
            Capture.Emit(new RunHistoryRead(Capture.ToActor(p), t.Gained, t.Lost, t.Choices, t.HpLost, t.Healed,
                new RunEconomy(e[0], e[1], e[2] + e[3], e[3], e[4], e[5], e[6], e[7], e[8])));
        }
    });

    /// A curse card, except the Ascender's Bane every player gets from the ascension level (not a choice).
    private static bool IsCurse(SerializableCard card) =>
        card.Id is { } id && id.Entry != "ASCENDERS_BANE" && ModelDb.GetByIdOrNull<CardModel>(id)?.Type == CardType.Curse;

    /// An event that starts a fight (heal 22 then fight…): the history counts the whole floor as a fight, so the event's
    /// own HP change is read when the fight starts: HP now minus HP at the end of the previous floor (v0.17.36).
    public static void PreFight(IRunState run) => Capture.Safe("RunHistory.PreFight", () =>
    {
        if (run.CurrentMapPointHistoryEntry is not { } current || !current.Rooms.Any(r => r.RoomType == RoomType.Event)) return;
        var points = run.MapPointHistory.SelectMany(a => a).ToList();
        var index = points.IndexOf(current);
        if (index < 1) return;
        var previous = points[index - 1];
        foreach (var player in run.Players)
        {
            var before = previous.GetEntry(player.NetId).CurrentHp;
            var delta = player.Creature.CurrentHp - before;
            ModLog.Info($"event before the fight, floor {run.TotalFloor}: player {player.NetId} HP {before} → {player.Creature.CurrentHp}");
            if (delta == 0) continue;
            Capture.Emit(new PreFightEvent(Capture.ToActor(player), $"{RunStore.KeyOf(run)}:{run.TotalFloor.ToString(CultureInfo.InvariantCulture)}",
                Math.Max(0, -delta), Math.Max(0, delta)));
        }
    });

    private static string Sig(Dictionary<string, double> d) =>
        string.Join(',', d.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}"));

    /// A new run: read again even if the figures look the same.
    public static void Reset() => _signature = "";

    /// Several players picked the same relic: the game plays rock-paper-scissors until one is left.
    [HarmonyPatch(typeof(RelicPickingResult), nameof(RelicPickingResult.GenerateRelicFight))]
    public static class RelicFight
    {
        public static void Postfix(RelicPickingResult __result, List<Player> players, RelicModel relic) => Capture.Safe("GenerateRelicFight", () =>
        {
            if (__result.player is not { } winner || players.Count < 2) return;
            var run = winner.RunState;
            var fightId = $"{RunStore.KeyOf(run)}:{run.TotalFloor.ToString(CultureInfo.InvariantCulture)}:{relic.Id.Entry}";
            var names = players.ToDictionary(p => p.NetId, p => Capture.PlayerName(p, players.Count));
            ModLog.Info($"chifumi for {relic.Id.Entry}: {names[winner.NetId]} won against " +
                        string.Join(", ", players.Where(p => p != winner).Select(p => names[p.NetId])));
            Capture.Emit(new ChifumiFought(fightId, Capture.ToActor(winner), players.Select(p => Capture.ToActor(p)).ToList()));
            UI.ChifumiStore.Record(fightId, winner.NetId, players.Select(p => p.NetId), names);
        });
    }
}
