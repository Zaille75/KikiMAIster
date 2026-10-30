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
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Multiplayer.Game;

namespace KikiMAIster.Patches;

/// Run-level stats outside combat (spec 01-metrics §C4): Max HP and campfire choices come from the game's own map
/// history (complete after Save & Quit or a crash, never counted twice); rock-paper-scissors fights for a treasure relic
/// are captured when the game settles them (every player's game computes the same result).
public static class RunHistoryPatches
{
    private static string _signature = "";
    /// Floors in the history when a read failed: not tried again (and logged again) until a floor is added (v0.17.42).
    private static int _failedAt = -1;

    /// Reads every player's Max HP changes, HP lost / healed outside fights and campfire choices from the run's map history; emits only when they changed.
    /// Ended: the run is over, so its last floor is complete too (event outcomes).
    public static void Read(IRunState? run, bool ended = false) => Capture.Safe("RunHistory.Read", () =>
    {
        if (run == null) return;
        SeePlayers(run);
        Capture.Safe("RunHistory.Events", () => LogEventOutcomes(run, ended));
        var floors = run.MapPointHistory.Sum(a => a.Count());
        if (floors == _failedAt) return;
        try
        {
            ReadHistory(run);
        }
        catch
        {
            _failedAt = floors;
            throw;
        }
    });

    private static void ReadHistory(IRunState run)
    {
        var totals = run.Players.ToDictionary(p => p.NetId, _ => (Gained: 0, Lost: 0, Choices: new Dictionary<string, double>(),
            HpLost: new Dictionary<string, double>(), Healed: new Dictionary<string, double>(), Eco: new double[9],
            Picks: new Dictionary<string, double>()));
        // Each game records the other players' shop purchases as gold lost, not spent (v0.17.45, both logs of 2026-10-29).
        var local = LocalId();
        foreach (var act in run.MapPointHistory)
            foreach (var point in act)
            {
                // HP lost and healed on a floor without a fight (an event that turns into a fight counts as a fight), by
                // kind of floor: events (Neow included), campfires, others (shop, treasure: relics…), v0.17.35.
                var fight = point.Rooms.Any(r => r.RoomType is RoomType.Monster or RoomType.Elite or RoomType.Boss);
                // The Ancients at the start of each act heal to full (shown apart, v0.17.38); Neow first sets HP to 0, so
                // its "heal" is the whole Max HP: left out.
                // The room's model can be an encounter (a fight room): looked up as any model, never cast (v0.17.42).
                var ancients = point.Rooms.Select(r => r.ModelId is { } id ? ModelDb.GetByIdOrNull<AbstractModel>(id) : null)
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
                    if (entry.PlayerId != local && run.Players.Count > 1 && point.Rooms.Any(r => r.RoomType == RoomType.Shop))
                    {
                        t.Eco[1] += entry.GoldLost;
                        t.Eco[2] -= entry.GoldLost;
                    }
                    // Card rewards (v0.17.45): cards offered and taken, offers where none was taken, by kind of floor.
                    if (entry.CardChoices.Count > 0)
                    {
                        var pickKind = fight ? "fight" : point.Rooms.Any(r => r.RoomType == RoomType.Event) ? "event" : "other";
                        var taken = entry.CardChoices.Count(c => c.wasPicked);
                        Add(t.Picks, $"offered.{pickKind}", entry.CardChoices.Count);
                        Add(t.Picks, $"taken.{pickKind}", taken);
                        if (taken == 0) Add(t.Picks, $"skipped.{pickKind}", 1);
                    }
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
            $"{kv.Key}:{kv.Value.Gained}:{kv.Value.Lost}:{Sig(kv.Value.HpLost)}:{Sig(kv.Value.Healed)}:{Sig(kv.Value.Picks)}:{string.Join(',', kv.Value.Eco)}:{string.Join(',', kv.Value.Choices.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => $"{r.Key}={r.Value}"))}"));
        if (signature == _signature) return;
        _signature = signature;
        foreach (var p in run.Players)
        {
            var t = totals[p.NetId];
            var e = t.Eco;
            // The event's own text shows dictionaries as type names: the HP figures, readable (v0.17.38).
            ModLog.Info($"run history {p.NetId}: HP lost outside [{Sig(t.HpLost)}], healed outside [{Sig(t.Healed)}]");
            Capture.Emit(new RunHistoryRead(Capture.ToActor(p), t.Gained, t.Lost, t.Choices, t.HpLost, t.Healed,
                new RunEconomy(e[0], e[1], e[2] + e[3], e[3], e[4], e[5], e[6], e[7], e[8]), t.Picks));
        }
    }

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

    /// Floors whose event outcome is already in the log (this run, this game session).
    private static readonly HashSet<int> LoggedEvents = new();

    /// What each player got from each event and Ancient (v0.18.1, log only, no stat): option chosen, cards gained /
    /// removed / upgraded / transformed / enchanted, relics, potions, gold, HP. Logged once a floor is complete (a later
    /// floor exists, or the run ended); after Save & Quit the run's past events are logged again from the history.
    private static void LogEventOutcomes(IRunState run, bool ended)
    {
        var points = run.MapPointHistory.SelectMany(a => a).ToList();
        for (var i = 0; i < points.Count; i++)
        {
            if (i == points.Count - 1 && !ended) break;
            var point = points[i];
            var rooms = point.Rooms.Where(r => r.RoomType == RoomType.Event).ToList();
            if (rooms.Count == 0 || !LoggedEvents.Add(i)) continue;
            var ids = string.Join(" + ", rooms.Select(r => r.ModelId?.Entry ?? "?"));
            var fight = point.Rooms.Any(r => r.RoomType is RoomType.Monster or RoomType.Elite or RoomType.Boss) ? " (then a fight)" : "";
            foreach (var e in point.PlayerStats)
            {
                var name = run.Players.FirstOrDefault(p => p.NetId == e.PlayerId) is { } pl ? Capture.PlayerName(pl, run.Players.Count) : e.PlayerId.ToString(CultureInfo.InvariantCulture);
                var parts = new List<string>();
                var chosen = e.EventChoices.Select(c => Key(c.Title)).Concat(e.AncientChoices.Where(c => c.WasChosen).Select(c => Key(c.Title))).ToList();
                parts.Add($"chose [{string.Join(", ", chosen)}]");
                List(parts, "cards +", e.CardsGained.Select(Card));
                List(parts, "cards -", e.CardsRemoved.Select(Card));
                List(parts, "upgraded", e.UpgradedCards.Select(c => c.Entry));
                List(parts, "downgraded", e.DowngradedCards.Select(c => c.Entry));
                List(parts, "transformed", e.CardsTransformed.Select(t => $"{Card(t.OriginalCard)} → {Card(t.FinalCard)}"));
                List(parts, "enchanted", e.CardsEnchanted.Select(c => $"{Card(c.Card)} ({c.Enchantment.Entry})"));
                List(parts, "card offer", e.CardChoices.Select(c => Card(c.Card) + (c.wasPicked ? "*" : "")));
                List(parts, "relics +", e.RelicChoices.Where(c => c.wasPicked).Select(c => c.choice.Entry));
                List(parts, "relics -", e.RelicsRemoved.Select(r => r.Entry));
                List(parts, "potions +", e.PotionChoices.Where(c => c.wasPicked).Select(c => c.choice.Entry));
                List(parts, "potions thrown", e.PotionDiscarded.Select(p => p.Entry));
                if (e.GoldGained != 0 || e.GoldSpent != 0 || e.GoldLost != 0 || e.GoldStolen != 0)
                    parts.Add($"gold +{e.GoldGained} -{e.GoldSpent + e.GoldLost + e.GoldStolen}");
                if (e.DamageTaken != 0 || e.HpHealed != 0) parts.Add($"HP -{e.DamageTaken} +{e.HpHealed}");
                if (e.MaxHpGained != 0 || e.MaxHpLost != 0) parts.Add($"Max HP +{e.MaxHpGained} -{e.MaxHpLost}");
                ModLog.Info($"event outcome, floor {i + 1} {ids}{fight}, {name}: {string.Join("; ", parts)}");
            }
        }
    }

    private static void List(List<string> parts, string label, IEnumerable<string> items)
    {
        var list = items.ToList();
        if (list.Count > 0) parts.Add($"{label} {string.Join(", ", list)}");
    }

    private static string Card(SerializableCard c) =>
        (c.Id?.Entry ?? "?") + (c.CurrentUpgradeLevel > 0 ? "+" : "") + (c.Enchantment?.Id is { } en ? $" ({en.Entry})" : "");

    /// An option's key ("ABYSSAL_BATHS.pages.INITIAL.options.LINGER"), shortened to what follows "options." when present.
    private static string Key(MegaCrit.Sts2.Core.Localization.LocString? title)
    {
        var key = title?.LocEntryKey ?? "?";
        var at = key.IndexOf(".options.", StringComparison.Ordinal);
        return at >= 0 ? key[(at + 9)..].Replace(".title", "") : key;
    }

    /// Shared co-op events (v0.18.1, log only): the game picks one of the players' votes at random; logs every vote and
    /// the option kept, so whoever decided is known. Runs before the votes are cleared.
    [HarmonyPatch(typeof(EventSynchronizer), "ChooseOptionForSharedEvent")]
    public static class SharedEventChosen
    {
        public static void Prefix(EventSynchronizer __instance, uint optionIndex) => Capture.Safe("SharedEventChosen", () =>
        {
            var ev = __instance.GetLocalEvent();
            var players = ev.Owner?.RunState?.Players ?? new List<Player>();
            string Option(uint index) => index < ev.CurrentOptions.Count ? ev.CurrentOptions[(int)index].TextKey : $"#{index}";
            var votes = players.Select(p => (p, Vote: __instance.GetPlayerVote(p))).ToList();
            var deciders = votes.Where(v => v.Vote == optionIndex).Select(v => Capture.PlayerName(v.p, players.Count)).ToList();
            ModLog.Info($"shared event {ev.Id.Entry}: {Option(optionIndex)} chosen (voted by {string.Join(", ", deciders)}); votes: " +
                        string.Join(", ", votes.Select(v => $"{Capture.PlayerName(v.p, players.Count)}={(v.Vote is { } o ? Option(o) : "-")}")));
        });
    }

    private static void Add(Dictionary<string, double> d, string key, double v) => d[key] = d.GetValueOrDefault(key) + v;

    /// This game's player (0 when unknown).
    private static ulong LocalId()
    {
        try
        {
            return RunManager.Instance.NetService.NetId;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static string _playersSig = "";

    /// The run's players and their names, as soon as the game knows them (v0.17.45: the panel showed Steam ids before the
    /// first fight).
    private static void SeePlayers(IRunState run)
    {
        var players = run.Players.Select(p => new PlayerInfo(p.NetId, Capture.PlayerName(p, run.Players.Count), p.Character.Id.Entry)).ToList();
        var sig = string.Join(';', players.Select(p => $"{p.PlayerId}={p.Name}"));
        if (sig == _playersSig) return;
        _playersSig = sig;
        Capture.Emit(new PlayersSeen(players));
    }

    private static string Sig(Dictionary<string, double> d) =>
        string.Join(',', d.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}"));

    /// A new run: read again even if the figures look the same.
    public static void Reset()
    {
        _signature = "";
        _playersSig = "";
        _failedAt = -1;
        LoggedEvents.Clear();
    }

    /// Map votes (v0.17.45, Godot): when every player has chosen the next floor, the last one made the others wait the time
    /// since the one before. Counted once per floor; a vote changed or withdrawn keeps the latest time.
    private static string _voteSource = "";
    private static readonly Dictionary<ulong, double> VoteTimes = new();
    private static bool _voteDone;
    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();

    [HarmonyPatch(typeof(MapSelectionSynchronizer), nameof(MapSelectionSynchronizer.PlayerVotedForMapCoord))]
    public static class MapVoted
    {
        public static void Postfix(MapSelectionSynchronizer __instance, Player player, MapLocation source, MapVote? destination) =>
            Capture.Safe("MapVote", () =>
            {
                var key = source.ToString();
                if (key != _voteSource)
                {
                    _voteSource = key;
                    VoteTimes.Clear();
                    _voteDone = false;
                }
                if (destination.HasValue) VoteTimes[player.NetId] = Clock.Elapsed.TotalSeconds;
                else VoteTimes.Remove(player.NetId);
                var players = player.RunState?.Players ?? new List<Player>();
                if (_voteDone || players.Count < 2 || players.Any(p => !__instance.GetVote(p).HasValue || !VoteTimes.ContainsKey(p.NetId))) return;
                _voteDone = true;
                var order = VoteTimes.OrderByDescending(kv => kv.Value).ToList();
                var waited = order[0].Value - order[1].Value;
                var last = players.First(p => p.NetId == order[0].Key);
                ModLog.Info($"map vote: {Capture.PlayerName(last, players.Count)} chose last, {waited:0.0} s after the others");
                Capture.Emit(new WaitedFor(Capture.ToActor(last), waited));
            });
    }

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
            // What each one threw, round by round (v0.17.45); a player out of the fight has no move.
            var rounds = __result.fight?.rounds ?? new List<RelicPickingFightRound>();
            var moves = players.Select((p, i) => (p.NetId, Moves: (IReadOnlyList<string>)rounds
                    .Select(r => i < r.moves.Count ? r.moves[i] : null).OfType<RelicPickingFightMove>().Select(m => m.ToString()).ToList()))
                .ToDictionary(x => x.NetId, x => x.Moves);
            Capture.Emit(new ChifumiFought(fightId, Capture.ToActor(winner), players.Select(p => Capture.ToActor(p)).ToList(), moves, rounds.Count));
            UI.ChifumiStore.Record(fightId, winner.NetId, players.Select(p => p.NetId), names, moves, rounds.Count);
        });
    }
}
