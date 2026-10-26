using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using static KikiMAIster.Patches.CombatHistoryPatch;

namespace KikiMAIster.Patches;

/// Spike v0.0.2: the same events captured at several levels, to find which capture point is reliable.
/// Each log line is tagged with the capture point it came from.
public static class DiagnosticPatches
{
    [HarmonyPatch(typeof(CombatHistory), nameof(CombatHistory.DamageReceived))]
    public static class HistoryDamageReceived
    {
        public static void Prefix(Creature receiver, Creature? dealer, DamageResult result, CardModel? cardSource) =>
            Safe(() => ModLog.Info($"[HistDmg] {Who(dealer)} -> {Who(receiver)}: {Dmg(result)} card={cardSource?.Id.Entry ?? "-"}"));
    }

    [HarmonyPatch(typeof(CombatHistory), nameof(CombatHistory.PowerReceived))]
    public static class HistoryPowerReceived
    {
        public static void Prefix(PowerModel power, decimal amount, Creature? applier) =>
            Safe(() => ModLog.Info($"[HistPow] {Who(applier)} -> {Who(power.Owner)}: {power.Id.Entry} {amount:+0;-0} type={power.Type}"));
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterDamageReceived))]
    public static class HookAfterDamageReceived
    {
        public static void Prefix(Creature target, DamageResult result, Creature? dealer, CardModel? cardSource) =>
            Safe(() => ModLog.Info($"[HookDmg] {Who(dealer)} -> {Who(target)}: {Dmg(result)} card={cardSource?.Id.Entry ?? "-"}"));
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterPowerAmountChanged))]
    public static class HookAfterPowerAmountChanged
    {
        public static void Prefix(PowerModel power, decimal amount, Creature? applier, CardModel? cardSource) =>
            Safe(() => ModLog.Info($"[HookPow] {Who(applier)} -> {Who(power.Owner)}: {power.Id.Entry} {amount:+0;-0} card={cardSource?.Id.Entry ?? "-"}"));
    }

    internal static string Dmg(DamageResult r) =>
        $"blocked={r.BlockedDamage} unblocked={r.UnblockedDamage} overkill={r.OverkillDamage} killed={r.WasTargetKilled} props={r.Props}";

    private static void Safe(Action a)
    {
        try { a(); }
        catch (Exception e) { ModLog.Error("diagnostic patch", e); }
    }
}
