using System.Globalization;
using HarmonyLib;
using KikiMAIster.Core;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.TreasureRelicPicking;
using MegaCrit.Sts2.Core.Models;
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
            HpLost: new Dictionary<string, double>(), Healed: new Dictionary<string, double>()));
        foreach (var act in run.MapPointHistory)
            foreach (var point in act)
            {
                // HP lost and healed on a floor without a fight (an event that turns into a fight counts as a fight), by
                // kind of floor: events (Neow included), campfires, others (shop, treasure: relics…), v0.17.35.
                var fight = point.Rooms.Any(r => r.RoomType is RoomType.Monster or RoomType.Elite or RoomType.Boss);
                var kind = point.Rooms.Any(r => r.RoomType == RoomType.Event) ? "event"
                    : point.Rooms.Any(r => r.RoomType == RoomType.RestSite) ? "restSite" : "other";
                foreach (var entry in point.PlayerStats)
                {
                    if (!totals.TryGetValue(entry.PlayerId, out var t)) continue;
                    t.Gained += entry.MaxHpGained;
                    t.Lost += entry.MaxHpLost;
                    if (!fight && entry.DamageTaken > 0) t.HpLost[kind] = t.HpLost.GetValueOrDefault(kind) + entry.DamageTaken;
                    if (!fight && entry.HpHealed > 0) t.Healed[kind] = t.Healed.GetValueOrDefault(kind) + entry.HpHealed;
                    foreach (var choice in entry.RestSiteChoices) t.Choices[choice] = t.Choices.GetValueOrDefault(choice) + 1;
                    totals[entry.PlayerId] = t;
                }
            }
        var signature = string.Join(';', totals.OrderBy(kv => kv.Key).Select(kv =>
            $"{kv.Key}:{kv.Value.Gained}:{kv.Value.Lost}:{Sig(kv.Value.HpLost)}:{Sig(kv.Value.Healed)}:{string.Join(',', kv.Value.Choices.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => $"{r.Key}={r.Value}"))}"));
        if (signature == _signature) return;
        _signature = signature;
        foreach (var p in run.Players)
        {
            var t = totals[p.NetId];
            Capture.Emit(new RunHistoryRead(Capture.ToActor(p), t.Gained, t.Lost, t.Choices, t.HpLost, t.Healed));
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
