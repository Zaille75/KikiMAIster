using System.Runtime.CompilerServices;
using HarmonyLib;
using KikiMAIster.Core;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace KikiMAIster.Patches;

public static class BlockAndPowerPatches
{
    private sealed record BlockCall(Player? Giver, Source Source, int PlayId = 0);

    private static readonly ConditionalWeakTable<Creature, BlockCall> PendingGiver = new();

    /// Decides who gives the block: card owner, potion user, owner of the power/relic, else the receiver.
    [HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.GainBlock), typeof(Creature), typeof(decimal), typeof(ValueProp),
        typeof(CardPlay), typeof(bool))]
    public static class GainBlock
    {
        public static void Prefix(Creature creature, CardPlay? cardPlay) => Capture.Safe("CreatureCmd.GainBlock", () =>
        {
            if (cardPlay != null)
            {
                PendingGiver.AddOrUpdate(creature, new BlockCall(cardPlay.Card.Owner, Source.Card(Capture.IdOf(cardPlay.Card)),
                    BlockAlternatives.PlayIdFor(cardPlay)));
                return;
            }
            var model = Capture.CallingModelType();
            var source = Capture.SourceOf(model);
            Player? giver = source.Kind switch
            {
                SourceKind.Potion => Capture.PotionUser,
                SourceKind.Card => Capture.CurrentActingPlayer,
                _ => creature.CombatState is { } cs ? Capture.UniqueOwnerOf(model, cs.Players) : null
            };
            PendingGiver.AddOrUpdate(creature, new BlockCall(giver, source));
        });
    }

    /// The history gets the final amount, after Dexterity, Frail…
    [HarmonyPatch(typeof(CombatHistory), nameof(CombatHistory.BlockGained))]
    public static class HistoryBlock
    {
        public static void Prefix(Creature receiver, int amount) => Capture.Safe("History.BlockGained", () =>
        {
            PendingGiver.TryGetValue(receiver, out var call);
            PendingGiver.Remove(receiver);
            var giver = call?.Giver != null ? Capture.ToActor(call.Giver) : Capture.ToActor(receiver);
            Capture.Emit(new BlockGained(Capture.ToActor(receiver), giver, amount, call?.Source ?? Source.Unknown, call?.PlayId ?? 0));
        });
    }

    [HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.LoseBlock))]
    public static class LoseBlock
    {
        public static void Prefix(Creature target, decimal amount, Creature? remover) => Capture.Safe("CreatureCmd.LoseBlock", () =>
        {
            if (CombatManager.Instance.IsOverOrEnding || target.IsDead || amount <= 0) return;
            var lost = (int)Math.Min(amount, target.Block);
            if (lost <= 0) return;
            // A player removing their own block (to use it for something else) is "spent", not wasted.
            var spent = remover != null && remover.IsPlayer && remover == target;
            Capture.Emit(new BlockRemoved(Capture.ToActor(target), lost, spent));
        });
    }

    [HarmonyPatch(typeof(CombatHistory), nameof(CombatHistory.PowerReceived))]
    public static class HistoryPower
    {
        public static void Prefix(PowerModel power, decimal amount, Creature? applier) => Capture.Safe("History.PowerReceived", () =>
        {
            BlockAlternatives.OnEffect();
            var id = Capture.IdOf(power);
            var value = (double)amount;
            var debuff = power.Type == PowerType.Debuff;
            // A buff given a negative amount (Strength down, Dexterity down…) is a debuff: recorded as "<ID>~DOWN".
            if (!debuff && power.GetTypeForAmount(amount) == PowerType.Debuff)
            {
                id += "~DOWN";
                value = -value;
                debuff = true;
            }
            // Temporary "down" powers (Enfeebling Touch…) apply a Strength/Dexterity/Focus loss together with
            // themselves: before (first application) or right after (stacking). The loss counts only once, under
            // "<STAT>~DOWN@<TEMPORARY POWER>": the overlay groups it with the stat, the details keep the origin.
            // A loss is held back and dropped if its temporary power comes next.
            var temporary = power is TemporaryStrengthPower or TemporaryDexterityPower or TemporaryFocusPower;
            if (temporary && debuff)
            {
                var stat = power switch
                {
                    TemporaryStrengthPower => ModelDb.GetId<StrengthPower>().Entry,
                    TemporaryDexterityPower => ModelDb.GetId<DexterityPower>().Entry,
                    _ => ModelDb.GetId<FocusPower>().Entry
                };
                id = $"{stat}~DOWN@{id}";
            }
            // The stat loss a temporary power applies to its holder is already counted as that temporary power.
            if (id.EndsWith("~DOWN") && TakeSuppressed(power.Owner)) return;
            Capture.Emit(new PowerChanged(Capture.ToActor(applier), Capture.ToActor(power.Owner), id, value, debuff));
        });
    }

    // Temporary "down" powers (Enfeebling Touch…) apply a real Strength/Dexterity/Focus loss to their holder.
    // That loss is skipped (it is counted once, as the temporary power). Decided when the command starts, from
    // the call stack, because the history entry may come later.
    private static readonly ConditionalWeakTable<Creature, StrongBox<int>> Suppressed = new();

    private static void SuppressIfFromTemporaryPower(PowerModel power, Creature target, decimal amount)
    {
        if (amount >= 0 || power is not (StrengthPower or DexterityPower or FocusPower)) return;
        var caller = Capture.CallingModelType();
        if (caller == null || !(typeof(TemporaryStrengthPower).IsAssignableFrom(caller) ||
                                typeof(TemporaryDexterityPower).IsAssignableFrom(caller) ||
                                typeof(TemporaryFocusPower).IsAssignableFrom(caller))) return;
        Suppressed.GetOrCreateValue(target).Value++;
    }

    private static bool TakeSuppressed(Creature target)
    {
        if (!Suppressed.TryGetValue(target, out var box) || box.Value <= 0) return false;
        box.Value--;
        return true;
    }

    [HarmonyPatch(typeof(PowerCmd), nameof(PowerCmd.Apply), typeof(PlayerChoiceContext), typeof(PowerModel), typeof(Creature),
        typeof(decimal), typeof(Creature), typeof(CardModel), typeof(bool))]
    public static class PowerApply
    {
        public static void Prefix(PowerModel power, Creature target, decimal amount) =>
            Capture.Safe("PowerCmd.Apply", () => SuppressIfFromTemporaryPower(power, target, amount));
    }

    [HarmonyPatch(typeof(PowerCmd), nameof(PowerCmd.ModifyAmount))]
    public static class PowerModify
    {
        public static void Prefix(PowerModel power, decimal offset) =>
            Capture.Safe("PowerCmd.ModifyAmount", () => SuppressIfFromTemporaryPower(power, power.Owner, offset));
    }

    [HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Heal))]
    public static class Heal
    {
        public static void Prefix(Creature creature, decimal amount) => Capture.Safe("CreatureCmd.Heal", () =>
        {
            if (!CombatManager.Instance.IsInProgress || creature.IsDead) return;
            var healed = (int)Math.Min(amount, creature.MaxHp - creature.CurrentHp);
            if (healed <= 0) return;
            BlockAlternatives.OnEffect();
            Capture.Emit(new Healed(Capture.ToActor(Capture.CurrentActingPlayer), Capture.ToActor(creature), healed));
        });
    }
}
