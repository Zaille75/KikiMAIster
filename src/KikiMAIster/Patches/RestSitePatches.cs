using HarmonyLib;
using KikiMAIster.Core;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace KikiMAIster.Patches;

/// Tente miniature (spec 01-metrics §E0, v0.17.14): every campfire option stays available after the first choice. Leaving
/// with a useful option not taken is a missed choice ("Choix"). Checked when the game drops the remaining options: the
/// local player leaving, or a remote player's "skipped" message.
public static class RestSitePatches
{
    /// The player who chose Réparer (Mend) and has not healed an ally yet: that heal is theirs.
    public static Player? Mender;

    [HarmonyPatch(typeof(RestSiteSynchronizer), "ChooseOption")]
    public static class OptionChosen
    {
        public static void Prefix(RestSiteSynchronizer __instance, Player player, int optionIndex) => Capture.Safe("RestSite.ChooseOption", () =>
        {
            var options = __instance.GetOptionsForPlayer(player);
            Mender = optionIndex < options.Count && options[optionIndex] is MendRestSiteOption ? player : null;
            // Rest: maybe not needed if the next fight is the boss (checked when it ends). Not with the Miniature Tent (the
            // other options stay) or a relic rewarding the rest itself.
            if (optionIndex < options.Count && options[optionIndex] is HealRestSiteOption heal && player.GetRelic<MiniatureTent>() == null &&
                !player.Relics.Any(RewardsResting))
                Rests[player] = (heal.OptionId, player.Creature.CurrentHp, player.RunState.TotalFloor);
        });
    }

    /// Rests taken at a campfire: option id, HP before resting, floor. Kept until the next fight starts.
    private static readonly Dictionary<Player, (string Option, int HpBefore, int Floor)> Rests = new();
    /// Players who rested just before the boss fight in progress: option id, HP they would have had without resting, HP at
    /// the fight's start (both after the Pantographe's heal, v0.17.34).
    private static readonly Dictionary<Player, (string Option, int HpBefore, int HpAtStart)> BossRests = new();

    /// A fight starts: a rest from the floor just before counts if this is the boss.
    public static void OnCombatStarted(IRunState run, RoomType room)
    {
        BossRests.Clear();
        if (room == RoomType.Boss)
            foreach (var (player, rest) in Rests.Where(r => r.Value.Floor == run.TotalFloor - 1))
            {
                // Pantographe heals at the boss fight's start (after this hook): with or without the rest.
                var max = player.Creature.MaxHp;
                var heal = player.GetRelic<Pantograph>() is { } pantograph ? (int)pantograph.DynamicVars.Heal.BaseValue : 0;
                int Healed(int hp) => Math.Min(max, hp + heal);
                BossRests[player] = (rest.Option, Healed(rest.HpBefore), Healed(player.Creature.CurrentHp));
            }
        Rests.Clear();
    }

    /// The boss fight is won: a rest was not needed when the HP before resting already covered what the fight cost
    /// (still alive at the end), so another campfire option (Smith…) was the better choice ("Choix", v0.17.32).
    public static void OnCombatEnded(bool victory)
    {
        foreach (var (player, rest) in BossRests)
        {
            var lost = rest.HpAtStart - player.Creature.CurrentHp;
            ModLog.Info($"rest before the boss by {player.NetId}: {rest.HpBefore} HP before resting, the fight cost {lost}, victory {victory}");
            if (victory && player.Creature.IsAlive && rest.HpBefore > lost)
                Capture.Emit(new MissedChance(Capture.ToActor(player), "restBoss",
                    new[] { $"RestSite:{rest.Option}", rest.HpBefore.ToString(), Math.Max(0, lost).ToString() }));
        }
        BossRests.Clear();
    }

    /// Main branch: no "leaving" message; when the campfire screen closes, every player's options left are checked.
    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Rooms.NRestSiteRoom), nameof(MegaCrit.Sts2.Core.Nodes.Rooms.NRestSiteRoom._ExitTree))]
    public static class RoomLeft
    {
        public static bool Prepare() => !GameBranch.IsBeta;

        public static void Prefix() => Capture.Safe("RestSite.RoomLeft", () =>
        {
            var sync = MegaCrit.Sts2.Core.Runs.RunManager.Instance.RestSiteSynchronizer;
            foreach (var player in CombatFlowPatches.CurrentRun?.Players ?? Array.Empty<Player>())
                Check(player, sync.GetOptionsForPlayer(player));
        });
    }
    [HarmonyPatch(typeof(RestSiteSynchronizer), nameof(RestSiteSynchronizer.BeforeLocalRestSiteExited))]
    public static class LocalLeft
    {
        public static bool Prepare() => GameBranch.IsBeta;

        public static void Prefix(RestSiteSynchronizer __instance) => Capture.Safe("RestSite.LocalLeft", () =>
        {
            if (Traverse.Create(__instance).Property(ReflectionTargets.RestSiteLocalPlayer.Name).GetValue<Player>() is { } me)
                Check(me, __instance.GetOptionsForPlayer(me));
        });
    }

    [HarmonyPatch(typeof(RestSiteSynchronizer), "HandleRestSiteSkippedMessage")]
    public static class RemoteLeft
    {
        public static bool Prepare() => GameBranch.IsBeta;

        public static void Prefix(RestSiteSynchronizer __instance, ulong senderId) => Capture.Safe("RestSite.RemoteLeft", () =>
        {
            var player = CombatFlowPatches.CurrentRun?.Players.FirstOrDefault(p => p.NetId == senderId);
            if (player != null) Check(player, __instance.GetOptionsForPlayer(player));
        });
    }

    private static void Check(Player player, IReadOnlyList<RestSiteOption> left)
    {
        if (player.GetRelic<MiniatureTent>() is not { } tent || left.Count == 0) return;
        // Only after a first choice at this campfire (the tent's extra choices were not taken).
        if (player.RunState.CurrentMapPointHistoryEntry?.GetEntry(player.NetId).RestSiteChoices.Count is not > 0) return;
        var useful = left.Where(o => Useful(o, player)).Select(o => o.OptionId).ToList();
        ModLog.Info($"campfire left by {player.NetId} with options [{string.Join(", ", left.Select(o => o.OptionId))}], useful: [{string.Join(", ", useful)}]");
        if (useful.Count == 0) return;
        Capture.Emit(new MissedChance(Capture.ToActor(player), "tent",
            new[] { $"Relic:{Capture.IdOf(tent)}" }.Concat(useful.Select(id => $"RestSite:{id}")).ToArray()));
    }

    /// Rest is useless at full HP unless something rewards resting itself (Minuscule boîte aux lettres, Attrape-rêves…);
    /// Mend is useless when every ally is at full HP; the game greys out what cannot be done (Smith with nothing to upgrade…).
    private static bool Useful(RestSiteOption option, Player player)
    {
        if (!option.IsEnabled) return false;
        return option switch
        {
            HealRestSiteOption => player.Creature.CurrentHp < player.Creature.MaxHp || player.Relics.Any(RewardsResting),
            MendRestSiteOption => player.RunState.Players.Any(p => p != player && p.Creature.IsAlive && p.Creature.CurrentHp < p.Creature.MaxHp),
            _ => true
        };
    }

    private static bool RewardsResting(RelicModel relic) =>
        new[] { nameof(AbstractModel.AfterRestSiteHeal), nameof(AbstractModel.TryModifyRestSiteHealRewards) }
            .Any(name => relic.GetType().GetMethod(name)?.DeclaringType != typeof(AbstractModel));
}
