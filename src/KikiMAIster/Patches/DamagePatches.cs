using System.Runtime.CompilerServices;
using HarmonyLib;
using KikiMAIster.Core;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace KikiMAIster.Patches;

/// Damage capture. Main source: CombatHistory.DamageReceived. The game skips it for the hit that ends
/// the combat, so every HP loss is also kept as "pending" and emitted later if it was never reported.
public static class DamagePatches
{
    /// DotPowerId: set when the calling model is a debuff on the target (Poison, Doom…). Decided when the command
    /// starts, because a creature that dies loses its powers before a late (pending) report is made.
    private sealed record DamageCall(Creature? Dealer, CardModel? Card, Type? CallingModel, string? DotPowerId = null);

    private sealed record PendingHit(DamageResult Result, DamageCall? Call);

    private static readonly ConditionalWeakTable<DamageResult, object> Seen = new();
    private static readonly ConditionalWeakTable<Creature, DamageCall> LastCallOnTarget = new();
    private static readonly List<PendingHit> Pending = new();

    public static void Reset() => Pending.Clear();

    /// Every damage command funnels into this overload: remember who/what is dealing damage to each target.
    [HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Damage), typeof(PlayerChoiceContext), typeof(IEnumerable<Creature>),
        typeof(decimal), typeof(ValueProp), typeof(Creature), typeof(CardModel), typeof(CardPlay))]
    public static class DamageCommand
    {
        public static void Prefix(IEnumerable<Creature>? targets, Creature? dealer, CardModel? cardSource) =>
            Capture.Safe("CreatureCmd.Damage", () =>
            {
                if (targets == null) return;
                // The call stack is only needed when the card is unknown (powers, relics, orbs, poison…).
                var call = new DamageCall(dealer, cardSource, cardSource == null ? Capture.CallingModelType() : null);
                foreach (var t in targets) LastCallOnTarget.AddOrUpdate(t, WithDot(call, t));
            });
    }

    /// Kills that are not damage (Doom, executes…) also go through LoseHpInternal: give them a context
    /// so they are credited to the right power or card instead of the last damage call on that creature.
    [HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Kill), typeof(IReadOnlyCollection<Creature>), typeof(bool))]
    public static class KillCommand
    {
        public static void Prefix(IReadOnlyCollection<Creature> creatures) => Capture.Safe("CreatureCmd.Kill", () =>
        {
            var model = Capture.CallingModelType();
            var dealer = model != null && typeof(CardModel).IsAssignableFrom(model) ? Capture.CurrentActingPlayer?.Creature : null;
            var call = new DamageCall(dealer, null, model);
            foreach (var c in creatures) LastCallOnTarget.AddOrUpdate(c, WithDot(call, c));
        });
    }

    [HarmonyPatch(typeof(Creature), nameof(Creature.LoseHpInternal))]
    public static class LoseHp
    {
        public static void Postfix(Creature __instance, DamageResult __result) => Capture.Safe("LoseHpInternal", () =>
        {
            if (!CombatManager.Instance.IsInProgress) return;
            LastCallOnTarget.TryGetValue(__instance, out var call);
            Pending.Add(new PendingHit(__result, call));
        });
    }

    [HarmonyPatch(typeof(CombatHistory), nameof(CombatHistory.DamageReceived))]
    public static class HistoryDamage
    {
        public static void Prefix(Creature receiver, Creature? dealer, DamageResult result, CardModel? cardSource) =>
            Capture.Safe("History.DamageReceived", () =>
            {
                LastCallOnTarget.TryGetValue(receiver, out var call);
                Report(result, dealer, cardSource, call);
            });
    }

    /// After an attack, report hits the history skipped (the killing blow that ends the combat).
    [HarmonyPatch(typeof(CombatHistory), nameof(CombatHistory.CreatureAttacked))]
    public static class HistoryAttack
    {
        public static void Prefix(Creature attacker, IReadOnlyList<DamageResult> damageResults) =>
            Capture.Safe("History.CreatureAttacked", () =>
            {
                foreach (var r in damageResults)
                {
                    LastCallOnTarget.TryGetValue(r.Receiver, out var call);
                    Report(r, attacker, call?.Card, call);
                }
                FlushPending();
            });
    }

    /// Emits HP losses never reported by the history (e.g. a Poison tick that kills the last enemy).
    public static void FlushPending()
    {
        foreach (var p in Pending)
            Report(p.Result, p.Call?.Dealer, p.Call?.Card, p.Call);
        Pending.Clear();
    }

    private static void Report(DamageResult r, Creature? dealer, CardModel? card, DamageCall? call)
    {
        var callingModel = call?.CallingModel;
        if (Seen.TryGetValue(r, out _)) return;
        Seen.Add(r, true);
        if (r.TotalDamage + r.OverkillDamage <= 0) return;

        var target = r.Receiver;
        Source source;
        if (card != null) source = Source.Card(Capture.IdOf(card));
        else if (call?.DotPowerId is { } powerId && (dealer == null || dealer == target))
        {
            // Damage-over-time debuff (Poison, Constrict, Doom…): credited to whoever applied the stacks.
            source = Source.Dot(powerId);
            dealer = null;
        }
        else
        {
            source = Capture.SourceOf(callingModel);
            // A card killing without dealing damage (execute effects): credit the player playing it.
            if (dealer == null && source.Kind == SourceKind.Card) dealer = Capture.CurrentActingPlayer?.Creature;
        }

        Capture.Emit(new DamageDealt(Capture.ToActor(dealer), Capture.ToActor(target),
            r.BlockedDamage, r.UnblockedDamage, r.OverkillDamage, r.WasTargetKilled, source));
    }

    private static DamageCall WithDot(DamageCall call, Creature target)
    {
        if (call.CallingModel == null) return call;
        var power = target.Powers.FirstOrDefault(p => p.GetType() == call.CallingModel);
        return power?.Type == PowerType.Debuff ? call with { DotPowerId = Capture.IdOf(power) } : call;
    }
}
