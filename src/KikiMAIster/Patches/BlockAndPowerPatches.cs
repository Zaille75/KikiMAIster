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
            var e = new PowerChanged(Capture.ToActor(applier), Capture.ToActor(power.Owner), id, value, debuff);

            // Temporary "down" powers (Enfeebling Touch…) apply a Strength/Dexterity/Focus loss together with
            // themselves: before (first application) or right after (stacking). The loss counts only once, as the
            // temporary power: a loss is held back and dropped if its temporary power comes next.
            var temporary = power is TemporaryStrengthPower or TemporaryDexterityPower or TemporaryFocusPower;
            if (temporary && _heldDown is { } held && held.Target == e.Target) _heldDown = null;
            if (id.EndsWith("~DOWN"))
            {
                if (_lastTemporaryTarget == e.Target)
                {
                    _lastTemporaryTarget = null;
                    return;
                }
                FlushHeldDown();
                _heldDown = e;
                return;
            }
            Capture.Emit(e);
            _lastTemporaryTarget = temporary ? e.Target : null;
        });
    }

    private static PowerChanged? _heldDown;
    private static Actor? _lastTemporaryTarget;

    /// Emits a held "down" change that no temporary power followed. Called before any other event.
    public static void FlushHeldDown()
    {
        _lastTemporaryTarget = null;
        if (_heldDown is not { } held) return;
        _heldDown = null;
        Capture.Emit(held);
    }

    [HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Heal))]
    public static class Heal
    {
        public static void Prefix(Creature creature, decimal amount) => Capture.Safe("CreatureCmd.Heal", () =>
        {
            if (!CombatManager.Instance.IsInProgress || creature.IsDead) return;
            var healed = (int)Math.Min(amount, creature.MaxHp - creature.CurrentHp);
            if (healed <= 0) return;
            Capture.Emit(new Healed(Capture.ToActor(Capture.CurrentActingPlayer), Capture.ToActor(creature), healed));
        });
    }
}
