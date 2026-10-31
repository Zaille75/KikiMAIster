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
                DamagePart part => part with { Amount = part.Amount * scale },
                DamageModifier m => m with { Amount = m.Amount * scale },
                _ => e
            });
    }

    public static void ResetOffense() => Offense.Clear();

    private static Pending? _last;

    /// Set while the mod computes its own damage previews (missed lethal…), which must never be reported.
    public static bool Computing;

    // The two game branches: the beta passes the CardPlay to damage modifiers, the main branch does not.
#if GAME_DEFAULT
    internal static decimal Mult(AbstractModel m, Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? card, CardPlay? _) =>
        m.ModifyDamageMultiplicative(target, amount, props, dealer, card);
    internal static decimal Additive(AbstractModel m, Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? card, CardPlay? _) =>
        m.ModifyDamageAdditive(target, amount, props, dealer, card);
#else
    internal static decimal Mult(AbstractModel m, Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? card, CardPlay? play) =>
        m.ModifyDamageMultiplicative(target, amount, props, dealer, card, play);
    internal static decimal Additive(AbstractModel m, Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? card, CardPlay? play) =>
        m.ModifyDamageAdditive(target, amount, props, dealer, card, play);
#endif

    [HarmonyPatch(typeof(Hook), nameof(Hook.ModifyDamage))]
    public static class ModifyDamage
    {
#if GAME_DEFAULT
        // The main branch has no CardPlay in damage modifiers.
        public static void Postfix(IRunState runState, ICombatState? combatState, Creature? target, Creature? dealer, decimal damage,
            ValueProp props, CardModel? cardSource, ModifyDamageHookType modifyDamageHookType, CardPreviewMode previewMode, decimal __result,
            ref IEnumerable<AbstractModel> modifiers)
        {
            CardPlay? cardPlay = null;
#else
        public static void Postfix(IRunState runState, ICombatState? combatState, Creature? target, Creature? dealer, decimal damage,
            ValueProp props, CardModel? cardSource, CardPlay? cardPlay, ModifyDamageHookType modifyDamageHookType, CardPreviewMode previewMode,
            decimal __result, ref IEnumerable<AbstractModel> modifiers)
        {
#endif
            if (Computing || previewMode != CardPreviewMode.None || target == null || dealer == null ||
                modifyDamageHookType != ModifyDamageHookType.All) return;
            var mods = modifiers;
            if (mods is not ICollection<AbstractModel> collection) return;
            // Strength down needs no modifier: the enemy's Strength may be back to 0 (+2 of its own, −2 from a player).
            if (collection.Count == 0 && !(dealer.IsEnemy && props.IsPoweredAttack()) && cardSource?.Enchantment == null) return;
            var baseDamage = damage;
            Capture.Safe("Hook.ModifyDamage", () =>
            {
                var effects = new List<StatEvent>();
                // A hit capped by Intangible / Dur à cuire on the target (v0.17.38): Vulnerable and Strength did not change it;
                // on a player or pet, the cap is damage prevented (the part that would have gone through the block).
                var caps = mods.OfType<PowerModel>().Where(p => p is IntangiblePower or HardToKillPower && p.Owner == target).ToList();
                if (caps.Count > 0 && Capture.ToActor(target).IsPlayerSide)
                    Capture.Safe("damage cap", () => AddCapped(effects, caps, runState, combatState, target, dealer, baseDamage, props, cardSource,
                        cardPlay, __result));
                // Enemies with a cap (Dur à cuire) hit by a player: what the cap stopped counts in Excédent (v0.17.45).
                if (caps.Count > 0 && target.IsEnemy && Capture.ToActor(dealer).IsPlayerSide)
                    Capture.Safe("enemy damage cap", () => AddEnemyCapped(effects, runState, combatState, target, dealer, baseDamage, props,
                        cardSource, cardPlay, __result));
                var vulnerable = new List<Creature>();
                var damage = (double)__result;
                // The damage key of this hit in the stats (v0.17.45): parts a relic gave move from it to the relic.
                var hitSource = dealer.IsPet ? "Pet" : cardSource != null ? $"Card:{Capture.IdOf(cardSource)}" : "";
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
                        case VulnerablePower v when v.Owner == target && target.IsEnemy && target.HpDisplay == HpDisplay.Normal && caps.Count == 0:
                        {
                            var factor = (double)Mult(v, target, __result, props, dealer, cardSource, cardPlay);
                            if (factor > 1)
                            {
                                Add(effects, target, v, dealer, damage - damage / factor, offense: true, fifo: true, hitSource);
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
                                                  target.HpDisplay == HpDisplay.Normal && caps.Count == 0:
                        {
                            var temporary = dealer.Powers.OfType<TemporaryStrengthPower>()
                                .Where(t => t.Amount > 0 && t.Applier is { IsPlayer: true } a && a != dealer)
                                .Select(t => (Capture.ToActor(t.Applier), (double)t.Amount)).ToList();
                            effects.Add(new StrengthUsed(Capture.ToActor(dealer), s.Amount, Math.Min(damage, s.Amount * vulnFactor), temporary, hitSource));
                            break;
                        }
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
                // Strength down on an attacking enemy (v0.17.38): the Strength players removed and that is still gone, whatever
                // Strength the enemy has (Rituel…), × the hit's Weak and the target's Vulnerable; shared by who removed it.
                if (dealer.IsEnemy && props.IsPoweredAttack() && Capture.ToActor(target).IsPlayerSide && caps.Count == 0 &&
                    BlockAndPowerPatches.StrengthDownFromPlayers(dealer) is var down and > 0)
                {
                    var factor = mods.OfType<WeakPower>().Where(w => w.Owner == dealer)
                        .Concat<PowerModel>(mods.OfType<VulnerablePower>().Where(v => v.Owner == target))
                        .Aggregate(1.0, (f, p) => f * (double)Mult(p, target, __result, props, dealer, cardSource, cardPlay));
                    effects.Add(new DebuffEffect(Capture.ToActor(dealer), $"{ModelDb.GetId<StrengthPower>().Entry}~DOWN", Capture.ToActor(target),
                        down * factor, Offense: false, Credit: DebuffCredit.Stacks));
                }
                // What the dealer holds that made the hit bigger (v0.17.45): its relics (Pointe de stylo…) and its own powers
                // (Vigueur, Létalité…). The parts are scaled so that together with Strength and Vulnerable they never exceed
                // the hit; the engine moves to a relic or potion only the share it gave.
                if (target.IsEnemy && target.HpDisplay == HpDisplay.Normal && caps.Count == 0 && hitSource != "" && Capture.ToActor(dealer).IsPlayerSide)
                    Capture.Safe("damage modifiers", () => AddModifiers(effects, mods, target, dealer, props, cardSource, cardPlay, __result, vulnFactor, hitSource));
                // An enemy's Strength on a player's hit (v0.17.45): the share a player's relic gave it (Soufre) is charged to them.
                if (dealer.IsEnemy && props.IsPoweredAttack() && target.IsPlayer && caps.Count == 0 && dealer.GetPower<StrengthPower>() is { Amount: > 0 } enemyStrength)
                {
                    var factor = mods.OfType<WeakPower>().Where(w => w.Owner == dealer)
                        .Concat<PowerModel>(mods.OfType<VulnerablePower>().Where(v => v.Owner == target))
                        .Aggregate(1.0, (f, p) => f * (double)Mult(p, target, __result, props, dealer, cardSource, cardPlay));
                    effects.Add(new EnemyStrengthHit(Capture.ToActor(dealer), Capture.ToActor(target), Math.Min(damage, (double)enemyStrength.Amount * factor)));
                }
                // Damage added by the card's enchantment (v0.17.39): the hit without it, through the same modifiers.
                if (cardSource?.Enchantment is { } enchantment && target.IsEnemy && Capture.ToActor(dealer).IsPlayerSide)
                    Capture.Safe("enchantment damage", () =>
                    {
                        var without = WithoutEnchantment(runState, combatState, target, dealer, baseDamage, props, cardSource, cardPlay);
                        var extra = (double)(__result - without);
                        if (extra > 0) effects.Add(new DamagePart(Capture.ToActor(dealer), $"Enchantment:{Capture.IdOf(enchantment)}", extra));
                    });
                _last = effects.Count > 0 ? new Pending(mods, effects, vulnerable, dealer, target) : null;
            });
        }

        private static readonly System.Reflection.MethodInfo? Internal = ReflectionTargets.Method(ReflectionTargets.ModifyDamageInternal);

        /// The game's damage computation without the card's enchantment (its private step after the enchantment).
        private static decimal WithoutEnchantment(IRunState runState, ICombatState? combatState, Creature target, Creature dealer, decimal damage,
            ValueProp props, CardModel cardSource, CardPlay? cardPlay)
        {
            if (Internal == null) return decimal.MaxValue;
#if GAME_DEFAULT
            var args = new object?[] { runState, combatState, target, dealer, damage, props, cardSource, ModifyDamageHookType.All, null };
#else
            var args = new object?[] { runState, combatState, target, dealer, damage, props, cardSource, cardPlay, ModifyDamageHookType.All, null };
#endif
            Computing = true;
            try
            {
                return Math.Max(0, (decimal)Internal.Invoke(null, args)!);
            }
            finally
            {
                Computing = false;
            }
        }

        /// Intangible / Dur à cuire capped the hit before the block: what the hit would have done without the cap, minus
        /// what it does, past the block (the pet uses its owner's block), is HP loss prevented by that power.
        private static void AddCapped(List<StatEvent> effects, List<PowerModel> caps, IRunState runState, ICombatState? combatState,
            Creature target, Creature dealer, decimal damage, ValueProp props, CardModel? cardSource, CardPlay? cardPlay, decimal capped)
        {
            decimal uncapped;
            Computing = true;
            try
            {
#if GAME_DEFAULT
                uncapped = Hook.ModifyDamage(runState, combatState, target, dealer, damage, props, cardSource,
                    ModifyDamageHookType.Additive | ModifyDamageHookType.Multiplicative, CardPreviewMode.None, out _);
#else
                uncapped = Hook.ModifyDamage(runState, combatState, target, dealer, damage, props, cardSource, cardPlay,
                    ModifyDamageHookType.Additive | ModifyDamageHookType.Multiplicative, CardPreviewMode.None, out _);
#endif
            }
            finally
            {
                Computing = false;
            }
            var block = props.HasFlag(ValueProp.Unblockable) ? 0 : (target.PetOwner?.Creature ?? target).Block;
            var saved = (double)(Math.Max(0, (int)uncapped - block) - Math.Max(0, (int)capped - block));
            if (saved <= 0) return;
            foreach (var cap in caps)
                effects.Add(new HpLossPrevented(Capture.ToActor(target), saved / caps.Count, $"Power:{Capture.IdOf(cap)}"));
        }

        private static void AddModifiers(List<StatEvent> effects, IEnumerable<AbstractModel> mods, Creature target, Creature dealer, ValueProp props,
            CardModel? cardSource, CardPlay? cardPlay, decimal result, double vulnFactor, string hitSource)
        {
            var damage = (double)result;
            var owner = dealer.PetOwner ?? dealer.Player;
            var parts = new List<(string Key, double Amount)>();
            foreach (var m in mods)
            {
                string key;
                if (m is RelicModel relic && owner != null && relic.Owner == owner) key = $"Relic:{Capture.IdOf(relic)}";
                else if (m is PowerModel power && power.Owner == dealer && power is not (StrengthPower or VulnerablePower or WeakPower or TemporaryStrengthPower))
                    key = $"Power:{Capture.IdOf(power)}";
                else continue;
                var factor = (double)Mult(m, target, result, props, dealer, cardSource, cardPlay);
                var additive = (double)Additive(m, target, result, props, dealer, cardSource, cardPlay);
                var amount = (factor > 1 ? damage - damage / factor : 0) + (additive > 0 ? additive * vulnFactor : 0);
                if (amount > 0) parts.Add((key, amount));
                if (amount > 0 && m is PowerModel) BlockAlternatives.OnPowerTriggered(dealer, key);
            }
            if (parts.Count == 0) return;
            // Strength and Vulnerable are already measured: the modifiers share what is left of the hit at most.
            var taken = effects.Sum(e => e switch { StrengthUsed u => u.Damage, DebuffEffect { Offense: true } d => d.Amount, _ => 0 });
            var room = Math.Max(0, damage - taken);
            var total = parts.Sum(x => x.Amount);
            var scale = total > room ? room / total : 1;
            foreach (var (key, amount) in parts)
                effects.Add(new DamageModifier(Capture.ToActor(dealer), key, amount * scale, hitSource));
        }

        /// Dur à cuire on an enemy capped a player's hit: the damage without the cap minus the hit, counted in Excédent.
        private static void AddEnemyCapped(List<StatEvent> effects, IRunState runState, ICombatState? combatState, Creature target, Creature dealer,
            decimal damage, ValueProp props, CardModel? cardSource, CardPlay? cardPlay, decimal capped)
        {
            decimal uncapped;
            Computing = true;
            try
            {
#if GAME_DEFAULT
                uncapped = Hook.ModifyDamage(runState, combatState, target, dealer, damage, props, cardSource,
                    ModifyDamageHookType.Additive | ModifyDamageHookType.Multiplicative, CardPreviewMode.None, out _);
#else
                uncapped = Hook.ModifyDamage(runState, combatState, target, dealer, damage, props, cardSource, cardPlay,
                    ModifyDamageHookType.Additive | ModifyDamageHookType.Multiplicative, CardPreviewMode.None, out _);
#endif
            }
            finally
            {
                Computing = false;
            }
            var stopped = (double)(uncapped - capped);
            if (stopped <= 0) return;
            effects.Add(new DamageCapped(Capture.ToActor(dealer), Capture.ToActor(target), stopped));
            BlockAlternatives.OnCapped(dealer);
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
            bool offense, bool fifo, string hitSource = "")
        {
            if (amount <= 0) return;
            effects.Add(new DebuffEffect(Capture.ToActor(holder), Capture.IdOf(power), Capture.ToActor(beneficiary), amount, offense,
                fifo ? DebuffCredit.CurrentTurn : DebuffCredit.RemainingTurns, HitSource: hitSource));
        }
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterModifyingDamageAmount))]
    public static class RealHit
    {
        public static void Prefix(IEnumerable<AbstractModel> modifiers) => Capture.Safe("AfterModifyingDamageAmount", () =>
        {
            if (_last == null || !ReferenceEquals(_last.Modifiers, modifiers)) return;
            // Damage avoided (Weak…) is resolved by the engine against the hit; extra damage waits for the hit's HP.
            var offense = _last.Effects.Where(e => e is DebuffEffect { Offense: true } or StrengthUsed or DamagePart or DamageModifier).ToList();
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
