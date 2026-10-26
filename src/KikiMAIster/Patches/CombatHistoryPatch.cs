using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace KikiMAIster.Patches;

/// Spike: every entry the game adds to its combat history is logged.
/// CombatHistory.Add only records entries during live combat, on every client.
[HarmonyPatch(typeof(CombatHistory), "Add")]
public static class CombatHistoryPatch
{
    public static void Postfix(ICombatState combatState, CombatHistoryEntry entry)
    {
        try
        {
            ModLog.Info(Describe(combatState, entry));
            SpikeTally.Record(entry);
        }
        catch (Exception e)
        {
            ModLog.Error("CombatHistory.Add postfix", e);
        }
    }

    private static string Describe(ICombatState combatState, CombatHistoryEntry entry) => entry switch
    {
        DamageReceivedEntry d =>
            $"DAMAGE R{combatState.RoundNumber} {Who(d.Dealer)} -> {Who(d.Receiver)}: blocked={d.Result.BlockedDamage} " +
            $"unblocked={d.Result.UnblockedDamage} overkill={d.Result.OverkillDamage} killed={d.Result.WasTargetKilled} " +
            $"card={d.CardSource?.Id.Entry ?? "-"} props={d.Result.Props}",
        BlockGainedEntry b =>
            $"BLOCK R{combatState.RoundNumber} {Who(b.Receiver)} +{b.Amount} card={b.CardPlay?.Card.Id.Entry ?? "-"} " +
            $"cardOwner={b.CardPlay?.Card.Owner.Creature.Name ?? "-"}",
        PowerReceivedEntry p =>
            $"POWER R{combatState.RoundNumber} {Who(p.Applier)} -> {Who(p.Actor)}: {p.Power.Id.Entry} x{p.Amount} type={p.Power.Type}",
        _ => $"OTHER {entry.GetType().Name}: {entry.HumanReadableString}"
    };

    internal static string Who(Creature? c)
    {
        if (c == null) return "<none>";
        if (c.IsPet) return $"{c.Name}(pet of {c.PetOwner!.Creature.Name})";
        return c.IsPlayer ? $"{c.Name}[player]" : c.Name;
    }
}
