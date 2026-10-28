using HarmonyLib;
using KikiMAIster.Core;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Multiplayer.Game;

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
        });
    }

#if GAME_DEFAULT
    /// Main branch: no "leaving" message; when the campfire screen closes, every player's options left are checked.
    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Rooms.NRestSiteRoom), nameof(MegaCrit.Sts2.Core.Nodes.Rooms.NRestSiteRoom._ExitTree))]
    public static class RoomLeft
    {
        public static void Prefix() => Capture.Safe("RestSite.RoomLeft", () =>
        {
            var sync = MegaCrit.Sts2.Core.Runs.RunManager.Instance.RestSiteSynchronizer;
            foreach (var player in CombatFlowPatches.CurrentRun?.Players ?? Array.Empty<Player>())
                Check(player, sync.GetOptionsForPlayer(player));
        });
    }
#else
    [HarmonyPatch(typeof(RestSiteSynchronizer), nameof(RestSiteSynchronizer.BeforeLocalRestSiteExited))]
    public static class LocalLeft
    {
        public static void Prefix(RestSiteSynchronizer __instance) => Capture.Safe("RestSite.LocalLeft", () =>
        {
            if (Traverse.Create(__instance).Property("LocalPlayer").GetValue<Player>() is { } me)
                Check(me, __instance.GetOptionsForPlayer(me));
        });
    }

    [HarmonyPatch(typeof(RestSiteSynchronizer), "HandleRestSiteSkippedMessage")]
    public static class RemoteLeft
    {
        public static void Prefix(RestSiteSynchronizer __instance, ulong senderId) => Capture.Safe("RestSite.RemoteLeft", () =>
        {
            var player = CombatFlowPatches.CurrentRun?.Players.FirstOrDefault(p => p.NetId == senderId);
            if (player != null) Check(player, __instance.GetOptionsForPlayer(player));
        });
    }
#endif

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
