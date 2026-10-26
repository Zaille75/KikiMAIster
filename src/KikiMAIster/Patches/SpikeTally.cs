using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace KikiMAIster.Patches;

/// Spike: per-player damage total for the combat, logged when the combat ends.
public static class SpikeTally
{
    private static readonly Dictionary<string, int> DamageByPlayer = new();

    public static void Record(CombatHistoryEntry entry)
    {
        if (entry is not DamageReceivedEntry d || d.Dealer == null || !d.Receiver.IsEnemy) return;
        var owner = d.Dealer.IsPet ? d.Dealer.PetOwner!.Creature : d.Dealer;
        if (!owner.IsPlayer) return;
        DamageByPlayer[owner.Name] = DamageByPlayer.GetValueOrDefault(owner.Name) + d.Result.UnblockedDamage;
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCombatEnd))]
    public static class CombatEndPatch
    {
        public static void Prefix(IRunState runState, ICombatState? combatState, CombatRoom room)
        {
            try
            {
                var summary = string.Join(", ", DamageByPlayer.Select(kv => $"{kv.Key}={kv.Value}"));
                ModLog.Info($"COMBAT END: damage dealt to enemies by player: {(summary == "" ? "none" : summary)}");
                DamageByPlayer.Clear();
            }
            catch (Exception e)
            {
                ModLog.Error("AfterCombatEnd prefix", e);
            }
        }
    }
}
