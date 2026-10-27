using HarmonyLib;
using KikiMAIster.Core;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace KikiMAIster.Patches;

/// What Vulnerable, Weak and Strength down changed on each real damage instance (spec 01-metrics §B2).
/// Hook.ModifyDamage also runs for previews: its result is only kept, then reported when the game confirms it was
/// a real hit (AfterModifyingDamageAmount receives the same modifier list right after).
public static class DebuffEffectPatches
{
    private sealed record Pending(IEnumerable<AbstractModel> Modifiers, List<DebuffEffect> Effects);

    private static Pending? _last;

    /// Set while the mod computes its own damage previews (missed lethal…), which must never be reported.
    public static bool Computing;

    [HarmonyPatch(typeof(Hook), nameof(Hook.ModifyDamage))]
    public static class ModifyDamage
    {
        public static void Postfix(Creature? target, Creature? dealer, ValueProp props, CardModel? cardSource, CardPlay? cardPlay,
            CardPreviewMode previewMode, decimal __result, ref IEnumerable<AbstractModel> modifiers)
        {
            if (Computing || previewMode != CardPreviewMode.None || target == null || dealer == null) return;
            var mods = modifiers;
            if (mods is not ICollection<AbstractModel> { Count: > 0 }) return;
            Capture.Safe("Hook.ModifyDamage", () =>
            {
                var effects = new List<DebuffEffect>();
                var damage = (double)__result;
                foreach (var m in mods)
                {
                    switch (m)
                    {
                        // Vulnerable on an enemy: extra damage for the player (or pet) hitting it.
                        case VulnerablePower v when v.Owner == target && target.IsEnemy && target.HpDisplay == HpDisplay.Normal:
                        {
                            var factor = (double)v.ModifyDamageMultiplicative(target, __result, props, dealer, cardSource, cardPlay);
                            if (factor > 1) Add(effects, target, v, dealer, damage - damage / factor, offense: true, fifo: true);
                            break;
                        }
                        // Weak on an enemy attacking a player or pet: damage it did not deal.
                        case WeakPower w when w.Owner == dealer && dealer.IsEnemy:
                        {
                            var factor = (double)w.ModifyDamageMultiplicative(target, __result, props, dealer, cardSource, cardPlay);
                            if (factor is > 0 and < 1) Add(effects, dealer, w, target, damage / factor - damage, offense: false, fifo: true);
                            break;
                        }
                        // Strength down on an attacking enemy: the lost Strength, shared by everyone who removed it.
                        case StrengthPower s when s.Owner == dealer && dealer.IsEnemy && s.Amount < 0:
                            effects.Add(new DebuffEffect(Capture.ToActor(dealer), $"{Capture.IdOf(s)}~DOWN", Capture.ToActor(target),
                                (double)-s.Amount, Offense: false, Fifo: false));
                            break;
                    }
                }
                _last = effects.Count > 0 ? new Pending(mods, effects) : null;
            });
        }

        private static void Add(List<DebuffEffect> effects, Creature holder, PowerModel power, Creature beneficiary, double amount,
            bool offense, bool fifo)
        {
            if (amount <= 0) return;
            effects.Add(new DebuffEffect(Capture.ToActor(holder), Capture.IdOf(power), Capture.ToActor(beneficiary), amount, offense, fifo));
        }
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterModifyingDamageAmount))]
    public static class RealHit
    {
        public static void Prefix(IEnumerable<AbstractModel> modifiers) => Capture.Safe("AfterModifyingDamageAmount", () =>
        {
            if (_last == null || !ReferenceEquals(_last.Modifiers, modifiers)) return;
            foreach (var e in _last.Effects) Capture.Emit(e);
            _last = null;
        });
    }
}
