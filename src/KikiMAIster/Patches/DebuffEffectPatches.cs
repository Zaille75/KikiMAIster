using HarmonyLib;
using KikiMAIster.Core;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace KikiMAIster.Patches;

/// What Vulnerable, Weak and Strength down changed on each real damage instance (spec 01-metrics §B2).
/// Hook.ModifyDamage also runs for previews: its result is only kept, then reported when the game confirms it was
/// a real hit (AfterModifyingDamageAmount receives the same modifier list right after).
public static class DebuffEffectPatches
{
    /// Vulnerable: enemies whose Vulnerable increased this hit.
    private sealed record Pending(IEnumerable<AbstractModel> Modifiers, List<StatEvent> Effects, List<Creature> Vulnerable,
        Creature Dealer, Creature Target);

    /// Extra damage of a hit (Vulnerable, Pendaison, Strength from others…), held until the hit is reported: only the
    /// share that really removed HP counts (v0.17.19: not the part into block or overkill, like "Dégâts").
    private static readonly Dictionary<(Creature Dealer, Creature Target), Queue<List<StatEvent>>> Offense = new();

    /// The hit was reported: its extra damage is scaled to the HP it removed (HP ÷ raw damage).
    public static void ReleaseOffense(Creature? dealer, Creature target, int hp, int raw)
    {
        if (dealer == null || !Offense.TryGetValue((dealer, target), out var queue) || queue.Count == 0) return;
        var effects = queue.Dequeue();
        if (raw <= 0 || hp <= 0) return;
        var scale = Math.Min(1.0, (double)hp / raw);
        foreach (var e in effects)
            Capture.Emit(e switch
            {
                DebuffEffect d => d with { Amount = d.Amount * scale },
                StrengthUsed u => u with { Damage = u.Damage * scale },
                _ => e
            });
    }

    public static void ResetOffense() => Offense.Clear();

    private static Pending? _last;

    /// Set while the mod computes its own damage previews (missed lethal…), which must never be reported.
    public static bool Computing;

    // The two game branches: the beta passes the CardPlay to damage modifiers, the main branch does not.
#if GAME_DEFAULT
    private static decimal Mult(AbstractModel m, Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? card, CardPlay? _) =>
        m.ModifyDamageMultiplicative(target, amount, props, dealer, card);
    private static decimal Additive(AbstractModel m, Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? card, CardPlay? _) =>
        m.ModifyDamageAdditive(target, amount, props, dealer, card);
#else
    private static decimal Mult(AbstractModel m, Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? card, CardPlay? play) =>
        m.ModifyDamageMultiplicative(target, amount, props, dealer, card, play);
    private static decimal Additive(AbstractModel m, Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? card, CardPlay? play) =>
        m.ModifyDamageAdditive(target, amount, props, dealer, card, play);
#endif

    [HarmonyPatch(typeof(Hook), nameof(Hook.ModifyDamage))]
    public static class ModifyDamage
    {
#if GAME_DEFAULT
        // The main branch has no CardPlay in damage modifiers.
        public static void Postfix(Creature? target, Creature? dealer, ValueProp props, CardModel? cardSource,
            CardPreviewMode previewMode, decimal __result, ref IEnumerable<AbstractModel> modifiers)
        {
            CardPlay? cardPlay = null;
#else
        public static void Postfix(Creature? target, Creature? dealer, ValueProp props, CardModel? cardSource, CardPlay? cardPlay,
            CardPreviewMode previewMode, decimal __result, ref IEnumerable<AbstractModel> modifiers)
        {
#endif
            if (Computing || previewMode != CardPreviewMode.None || target == null || dealer == null) return;
            var mods = modifiers;
            if (mods is not ICollection<AbstractModel> { Count: > 0 }) return;
            Capture.Safe("Hook.ModifyDamage", () =>
            {
                var effects = new List<StatEvent>();
                var vulnerable = new List<Creature>();
                var damage = (double)__result;
                // Vulnerable multiplier of this hit (1 if none): the other effects are measured before it.
                var vulnFactor = mods.OfType<VulnerablePower>().Where(v => v.Owner == target)
                    .Select(v => (double)Mult(v, target, __result, props, dealer, cardSource, cardPlay))
                    .DefaultIfEmpty(1).First();
                if (vulnFactor <= 0) vulnFactor = 1;
                // Bully: +ExtraDamage per Vulnerable stack, shared by the players whose Vulnerable turns are on the target.
                if (cardSource is Bully bully && target.IsEnemy && target.HpDisplay == HpDisplay.Normal &&
                    target.GetPower<VulnerablePower>() is { Amount: > 0 } targetVuln)
                {
                    var extra = (double)(bully.DynamicVars.ExtraDamage.BaseValue * targetVuln.Amount);
                    var baseDamage = (double)bully.DynamicVars.CalculationBase.BaseValue + extra;
                    if (baseDamage > 0)
                        Add(effects, target, targetVuln, dealer, damage / vulnFactor * extra / baseDamage, offense: true, fifo: false);
                }
                foreach (var m in mods)
                {
                    switch (m)
                    {
                        // Vulnerable on an enemy: extra damage for the player (or pet) hitting it.
                        case VulnerablePower v when v.Owner == target && target.IsEnemy && target.HpDisplay == HpDisplay.Normal:
                        {
                            var factor = (double)Mult(v, target, __result, props, dealer, cardSource, cardPlay);
                            if (factor > 1)
                            {
                                Add(effects, target, v, dealer, damage - damage / factor, offense: true, fifo: true);
                                vulnerable.Add(target);
                            }
                            break;
                        }
                        // Weak on an enemy attacking a player or pet: damage it did not deal.
                        case WeakPower w when w.Owner == dealer && dealer.IsEnemy:
                        {
                            var factor = (double)Mult(w, target, __result, props, dealer, cardSource, cardPlay);
                            // The game drops the fraction of the final damage: 6 × 0.75 = 4.5 hits for 4, so Weak avoided 2.
                            if (factor is > 0 and < 1) Add(effects, dealer, w, target, Math.Floor(damage / factor) - Math.Floor(damage), offense: false, fifo: true);
                            break;
                        }
                        // A player's Strength: the part borrowed from others' Vulnerable (Dominate) is damage they enabled.
                        // Temporary Strength from another player (Coordinate) is read from its power, which keeps its applier.
                        case StrengthPower s when s.Owner == dealer && dealer.IsPlayer && s.Amount > 0 && target.IsEnemy &&
                                                  target.HpDisplay == HpDisplay.Normal:
                        {
                            var temporary = dealer.Powers.OfType<TemporaryStrengthPower>()
                                .Where(t => t.Amount > 0 && t.Applier is { IsPlayer: true } a && a != dealer)
                                .Select(t => (Capture.ToActor(t.Applier), (double)t.Amount)).ToList();
                            effects.Add(new StrengthUsed(Capture.ToActor(dealer), s.Amount, Math.Min(damage, s.Amount * vulnFactor), temporary));
                            break;
                        }
                        // Strength down on an attacking enemy: the lost Strength, shared by everyone who removed it.
                        case StrengthPower s when s.Owner == dealer && dealer.IsEnemy && s.Amount < 0:
                            effects.Add(new DebuffEffect(Capture.ToActor(dealer), $"{Capture.IdOf(s)}~DOWN", Capture.ToActor(target),
                                (double)-s.Amount, Offense: false, Credit: DebuffCredit.Stacks));
                            break;
                        // Any other power that remembers which player applied it: a debuff on the enemy hit (Flanking,
                        // Knockdown), a buff an ally gave the attacker (One for All), or a debuff on the attacking enemy.
                        // Intercept: the interceptor takes its allies' hits (multiplier = 1 + covered allies).
                        case InterceptPower ip when ip.Owner == target && dealer.IsEnemy:
                        {
                            var factor = (double)Mult(ip, target, __result, props, dealer, cardSource, cardPlay);
                            if (factor > 1) effects.Add(new HelpGiven(Capture.ToActor(target), "taken", damage - damage / factor));
                            break;
                        }
                        // Tank: the Tank's player takes 50% more while allies are alive (taken for them, like Intercept)…
                        case TankPower tp when tp.Owner == target && dealer.IsEnemy &&
                                               target.CombatState?.GetTeammatesOf(target).Any(t => t != target && t.IsAlive && t.IsPlayer) == true:
                        {
                            var factor = (double)Mult(tp, target, __result, props, dealer, cardSource, cardPlay);
                            if (factor > 1) effects.Add(new HelpGiven(Capture.ToActor(target), "taken", damage - damage / factor));
                            break;
                        }
                        // …and its allies (Guarded) take 50% less: HP saved for them by the Tank's player.
                        case GuardedPower g when g.Owner == target && dealer.IsEnemy && g.Applier is { IsPlayer: true } guard && guard != target:
                        {
                            var factor = (double)Mult(g, target, __result, props, dealer, cardSource, cardPlay);
                            if (factor is > 0 and < 1)
                                effects.Add(new DebuffEffect(Capture.ToActor(target), Capture.IdOf(g), Capture.ToActor(target), Math.Floor(damage / factor) - Math.Floor(damage),
                                    Offense: false, Credit: DebuffCredit.Direct, Applier: Capture.ToActor(guard)));
                            break;
                        }
                        case PowerModel p when p is not (VulnerablePower or WeakPower or StrengthPower or TemporaryStrengthPower or InterceptPower or
                                                   TankPower or GuardedPower) &&
                                               p.Applier is { IsPlayer: true } applier:
                            AddApplied(effects, p, applier, target, dealer, props, cardSource, cardPlay, __result, vulnFactor);
                            break;
                    }
                }
                _last = effects.Count > 0 ? new Pending(mods, effects, vulnerable, dealer, target) : null;
            });
        }

        private static void AddApplied(List<StatEvent> effects, PowerModel p, Creature applier, Creature target, Creature dealer,
            ValueProp props, CardModel? cardSource, CardPlay? cardPlay, decimal result, double vulnFactor)
        {
            var factor = (double)Mult(p, target, result, props, dealer, cardSource, cardPlay);
            var additive = (double)Additive(p, target, result, props, dealer, cardSource, cardPlay);
            var damage = (double)result;
            var dealerSide = Capture.ToActor(dealer);
            var offense = (p.Owner == target && target.IsEnemy && target.HpDisplay == HpDisplay.Normal && dealerSide.IsPlayerSide) ||
                          (p.Owner == dealer && dealerSide.IsPlayerSide && applier != dealer && target.IsEnemy);
            var defense = p.Owner == dealer && dealer.IsEnemy && Capture.ToActor(target).IsPlayerSide;
            double amount;
            if (offense) amount = (factor > 1 ? damage - damage / factor : 0) + (additive > 0 ? additive * vulnFactor : 0);
            else if (defense) amount = (factor is > 0 and < 1 ? damage / factor - damage : 0) + (additive < 0 ? -additive : 0);
            else return;
            if (amount <= 0) return;
            effects.Add(new DebuffEffect(Capture.ToActor(p.Owner), Capture.IdOf(p), Capture.ToActor(offense ? dealer : target), amount,
                offense, DebuffCredit.Direct, Capture.ToActor(applier)));
        }

        private static void Add(List<StatEvent> effects, Creature holder, PowerModel power, Creature beneficiary, double amount,
            bool offense, bool fifo)
        {
            if (amount <= 0) return;
            effects.Add(new DebuffEffect(Capture.ToActor(holder), Capture.IdOf(power), Capture.ToActor(beneficiary), amount, offense,
                fifo ? DebuffCredit.CurrentTurn : DebuffCredit.RemainingTurns));
        }
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterModifyingDamageAmount))]
    public static class RealHit
    {
        public static void Prefix(IEnumerable<AbstractModel> modifiers) => Capture.Safe("AfterModifyingDamageAmount", () =>
        {
            if (_last == null || !ReferenceEquals(_last.Modifiers, modifiers)) return;
            // Damage avoided (Weak…) is resolved by the engine against the hit; extra damage waits for the hit's HP.
            var offense = _last.Effects.Where(e => e is DebuffEffect { Offense: true } or StrengthUsed).ToList();
            foreach (var e in _last.Effects.Except(offense)) Capture.Emit(e);
            if (offense.Count > 0 && _last.Target.IsEnemy)
            {
                if (!Offense.TryGetValue((_last.Dealer, _last.Target), out var queue)) Offense[(_last.Dealer, _last.Target)] = queue = new();
                queue.Enqueue(offense);
            }
            else foreach (var e in offense) Capture.Emit(e);
            foreach (var enemy in _last.Vulnerable) BlockAlternatives.OnVulnerableUsed(enemy);
            _last = null;
        });
    }
}
