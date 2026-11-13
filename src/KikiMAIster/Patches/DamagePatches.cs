using System.Runtime.CompilerServices;
using HarmonyLib;
using KikiMAIster.Core;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.ValueProps;

namespace KikiMAIster.Patches;

/// Damage capture. Main source: CombatHistory.DamageReceived. The game skips it for the hit that ends
/// the combat, so every HP loss is also kept as "pending" and emitted later if it was never reported.
public static class DamagePatches
{
    /// DotPowerId: set when the calling model is a debuff on the target (Poison, Doom…). Decided when the command
    /// starts, because a creature that dies loses its powers before a late (pending) report is made.
    /// DotApplier: for a debuff kept per applier (Strangle), the player whose instance deals this damage.
    private sealed record DamageCall(Creature? Dealer, CardModel? Card, Type? CallingModel, string? DotPowerId = null, Creature? DotApplier = null);

    private sealed record PendingHit(DamageResult Result, DamageCall? Call);

    private static readonly ConditionalWeakTable<DamageResult, object> Seen = new();
    private static readonly ConditionalWeakTable<Creature, DamageCall> LastCallOnTarget = new();
    private static readonly List<PendingHit> Pending = new();

    public static void Reset()
    {
        Pending.Clear();
        DebuffEffectPatches.ResetOffense();
    }

    /// Every damage command funnels into this overload: remember who/what is dealing damage to each target.
    /// Beta v0.113.0: the funnel takes DamageTargetData (target + hit effect) instead, see DamageCommandV113.
    [HarmonyPatch]
    public static class DamageCommand
    {
        public static bool Prepare() => !GameBranch.IsV113;

        /// The beta adds a CardPlay at the end.
        public static System.Reflection.MethodBase TargetMethod() => AccessTools.Method(typeof(CreatureCmd), nameof(CreatureCmd.Damage),
            GameBranch.IsBeta
                ? new[] { typeof(PlayerChoiceContext), typeof(IEnumerable<Creature>), typeof(decimal), typeof(ValueProp), typeof(Creature), typeof(CardModel), typeof(CardPlay) }
                : new[] { typeof(PlayerChoiceContext), typeof(IEnumerable<Creature>), typeof(decimal), typeof(ValueProp), typeof(Creature), typeof(CardModel) });

        public static void Prefix(IEnumerable<Creature>? targets, Creature? dealer, CardModel? cardSource) =>
            Record(targets, dealer, cardSource);

        internal static void Record(IEnumerable<Creature>? targets, Creature? dealer, CardModel? cardSource) =>
            Capture.Safe("CreatureCmd.Damage", () =>
            {
                if (targets == null) return;
                BlockAlternatives.OnEffect(damage: true);
                // The call stack is only needed when the card is unknown (powers, relics, orbs, poison…).
                var call = new DamageCall(dealer, cardSource, cardSource == null ? Capture.CallingModelType() : null);
                foreach (var t in targets)
                {
                    var c = WithDot(call, t);
                    // A player's buff dealing damage with no dealer (Haunt: Soul cards) counts for its holder (the one whose
                    // power is triggering when two players hold it, v0.17.38).
                    if (c.Dealer == null && c.DotPowerId == null && c.CallingModel == typeof(HauntPower) && HauntOwner != null)
                        c = c with { Dealer = HauntOwner };
                    else if (c.Dealer == null && c.DotPowerId == null && t.CombatState is { } cs &&
                        Capture.PowerHolder(c.CallingModel, cs.Players) is { } holder)
                        c = c with { Dealer = holder };
                    // No dealer, from an enemy's power (Galvanisé…): that enemy; from the hit player's own relic (Forces
                    // précaires, Champi aromatique, Poison royal): self-inflicted (v0.17.38).
                    if (c.Dealer == null && c.DotPowerId == null && c.CallingModel is { } model && t.CombatState is { } state)
                    {
                        if (typeof(PowerModel).IsAssignableFrom(model) &&
                            state.Enemies.FirstOrDefault(e => e.Powers.Any(p => p.GetType() == model)) is { } enemy)
                            c = c with { Dealer = enemy };
                        else if (typeof(RelicModel).IsAssignableFrom(model) && t.Player is { } owner && owner.Relics.Any(r => r.GetType() == model))
                            c = c with { Dealer = t };
                    }
                    LastCallOnTarget.AddOrUpdate(t, c);
                }
            });
    }

    /// Beta v0.113.0 damage funnel: CreatureCmd.Damage(context, IEnumerable<DamageTargetData>, amount, props, dealer, card,
    /// play, hook type). AttackCommand calls it directly, the IEnumerable<Creature> overload forwards to it.
    [HarmonyPatch]
    public static class DamageCommandV113
    {
        public static bool Prepare() => GameBranch.IsV113;

        public static System.Reflection.MethodBase TargetMethod() => typeof(CreatureCmd).GetMethods().Single(m =>
            m.Name == nameof(CreatureCmd.Damage) && m.GetParameters() is { Length: > 1 } p &&
            p[1].ParameterType.IsGenericType && p[1].ParameterType.GetGenericArguments()[0].Name == "DamageTargetData");

        public static void Prefix(object? targetData, Creature? dealer, CardModel? cardSource) =>
            DamageCommand.Record(targetData == null ? null : Targets(targetData), dealer, cardSource);

        [BranchOnly, System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static List<Creature> Targets(object targetData) =>
            ((IEnumerable<MegaCrit.Sts2.Core.Entities.Creatures.DamageTargetData>)targetData).Select(d => d.target).ToList();
    }

    /// Kills that are not damage (Doom, executes…) also go through LoseHpInternal: give them a context
    /// so they are credited to the right power or card instead of the last damage call on that creature.
    /// Beta v0.113.0: the funnel takes the card that kills (Éclats d'os, Sacrifice) too, see KillCommandV113.
    [HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Kill), typeof(IReadOnlyCollection<Creature>), typeof(bool))]
    public static class KillCommand
    {
        public static bool Prepare() => !GameBranch.IsV113;

        public static void Prefix(IReadOnlyCollection<Creature> creatures) => Record(creatures);

        internal static void Record(IReadOnlyCollection<Creature> creatures) => Capture.Safe("CreatureCmd.Kill", () =>
        {
            var model = Capture.CallingModelType();
            var dealer = model != null && typeof(CardModel).IsAssignableFrom(model) ? Capture.CurrentActingPlayer?.Creature : null;
            var call = new DamageCall(dealer, null, model);
            // A creature at 0 HP is dying from the damage just dealt: keep that damage's card and dealer.
            foreach (var c in creatures.Where(c => c.CurrentHp > 0)) LastCallOnTarget.AddOrUpdate(c, WithDot(call, c));
        });
    }

    /// Beta v0.113.0 kill funnel: CreatureCmd.Kill(creatures, cardSource, force).
    [HarmonyPatch]
    public static class KillCommandV113
    {
        public static bool Prepare() => GameBranch.IsV113;

        public static System.Reflection.MethodBase TargetMethod() => AccessTools.Method(typeof(CreatureCmd), nameof(CreatureCmd.Kill),
            new[] { typeof(IReadOnlyCollection<Creature>), typeof(CardModel), typeof(bool) });

        public static void Prefix(IReadOnlyCollection<Creature> creatures) => KillCommand.Record(creatures);
    }

    /// HP loss reduced by relics/powers (Tungsten Rod, Intangible, Buffer…): credited as damage prevented to the
    /// player (or pet owner) hit. Called only by real damage commands, not by previews.
    [HarmonyPatch(typeof(Hook), nameof(Hook.ModifyHpLost))]
    public static class HpLossModifiers
    {
        public static void Postfix(Creature target, decimal amount, decimal __result, ref IEnumerable<AbstractModel> modifiers)
        {
            var models = modifiers?.ToList();
            Capture.Safe("Hook.ModifyHpLost", () =>
            {
                var saved = (double)(decimal.Truncate(amount) - decimal.Truncate(__result));
                // La botte raises a loss: only the models that lower it share the saving.
                models = models?.Where(m => m is not TheBoot).ToList();
                if (saved <= 0 || models == null || models.Count == 0) return;
                var actor = Capture.ToActor(target);
                if (!actor.IsPlayerSide) return;
                // Several modifiers on one hit: shared equally.
                foreach (var m in models)
                    Capture.Emit(new HpLossPrevented(actor, saved / models.Count, Capture.SourceOf(m.GetType()).ToString()));
            });
        }
    }

    [HarmonyPatch(typeof(Creature), nameof(Creature.LoseHpInternal))]
    public static class LoseHp
    {
        public static void Postfix(Creature __instance, DamageResult __result) => Capture.Safe("LoseHpInternal", () =>
        {
            if (!CombatManager.Instance.IsInProgress) return;
            if (__instance.IsEnemy && __instance.CurrentHp <= 0) CombatFlowPatches.EnemyKilled();
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
        // The extra damage of this hit (Vulnerable…) counts for the HP it removed only.
        BlockAlternatives.SetPlaceBonus(Array.Empty<DamageModifier>());
        DebuffEffectPatches.ReleaseOffense(dealer, r.Receiver, r.UnblockedDamage, r.BlockedDamage + r.UnblockedDamage + r.OverkillDamage);
        var target = r.Receiver;
        // A hit that did nothing because the enemy's Carapace endurcie limit was reached this turn (v0.17.34).
        if (card != null && target.IsEnemy && r.TotalDamage + r.OverkillDamage <= 0 && target.GetPower<HardenedShellPower>() is { } shell &&
            shell.DisplayAmount <= 0)
            BlockAlternatives.OnShellCapped(card);
        if (r.TotalDamage + r.OverkillDamage <= 0) return;

        // An enemy shown with infinite HP (Waterfall Giant about to explode) cannot be hurt for real: not counted. Carapace
        // endurcie shows "infinite with numbers" once its limit is reached: the hit reaching it was real (v0.17.34).
        // v0.17.45: the attack did nothing, like one into a Carapace endurcie at its limit (energy spent for nothing).
        if (target.IsEnemy && target.HpDisplay == HpDisplay.InfiniteWithoutNumbers)
        {
            if (card != null) BlockAlternatives.OnShellCapped(card);
            return;
        }
        Source source;
        Actor? dealerOverride = null;
        // A status or curse hurting its holder (Brûlure, Déclin, Regrets…, v0.17.38): the enemy that put it in the deck, the
        // ally who did, or the player themself (their own card, or a card of their deck). The game gives the holder as the
        // dealer (CreatureCmd.Damage with a card source: its owner), so that one is replaced too (v0.17.44).
        if (card is { Type: CardType.Status or CardType.Curse } && (dealer == null || dealer == card.Owner.Creature) && !target.IsEnemy)
        {
            if (!StatusCreators.TryGetValue(card, out var creator)) dealer = card.Owner.Creature;
            else if (creator.Value != null) dealer = creator.Value.Creature;
            // The enemy that created it (v0.17.45), when known; else "an enemy".
            else if (StatusEnemies.TryGetValue(card, out var enemy)) dealer = enemy;
            else dealerOverride = Actor.Enemy(0);
        }
        if (card != null) source = Source.Card(Capture.IdOf(card));
        else if (call?.DotPowerId is { } powerId && (dealer == null || dealer == target))
        {
            // Damage-over-time debuff (Poison, Constrict, Doom…): credited to whoever applied the stacks.
            source = Source.Dot(powerId);
            dealer = call.DotApplier;
        }
        else
        {
            source = Capture.SourceOf(callingModel);
            // A card killing without dealing damage (execute effects): credit the player playing it.
            if (dealer == null && source.Kind == SourceKind.Card) dealer = Capture.CurrentActingPlayer?.Creature;
        }

        // On a player: what kind of damage it was (v0.17.39, details of HP lost).
        var dealerActor = dealerOverride ?? Capture.ToActor(dealer);
        var kind = target.IsEnemy ? ""
            : source.Kind == SourceKind.DamageOverTime ? "dot"
            : card is { Type: CardType.Status or CardType.Curse } ? "status"
            : dealerActor.IsEnemy && source.Kind == SourceKind.Power ? "reaction"
            : dealerActor.IsEnemy ? "attack" : "";
        // Overkill of a single-target attack aimed while another enemy was alive: avoidable (v0.17.42).
        var avoidable = r.OverkillDamage > 0 && card is { Type: CardType.Attack, TargetType: TargetType.AnyEnemy } && dealer is { IsEnemy: false } &&
                        target.IsEnemy && target.CombatState is { } cs &&
                        cs.HittableEnemies.Any(e => e != target && e.IsAlive && e.HpDisplay == HpDisplay.Normal);
        // The player's own card killed their pet (Détonation osseuse, v0.17.45).
        if (target.IsPet && r.WasTargetKilled && target.PetOwner is { } petOwner && dealer == petOwner.Creature)
            BlockAlternatives.OnPetSacrificed(petOwner);
        // A Lightning / Glass orb hit: its Focus part (the stacks a relic or potion gave count for it, v0.17.45).
        if (source is { Kind: SourceKind.Orb } hitOrb && target.IsEnemy && r.UnblockedDamage > 0 && dealer?.GetPower<FocusPower>() is { Amount: > 0 } focus &&
            (hitOrb.Id == ModelDb.GetId<LightningOrb>().Entry || hitOrb.Id == ModelDb.GetId<GlassOrb>().Entry))
            Capture.Emit(new DamageModifier(dealerActor, $"Power:{Capture.IdOf(focus)}", Math.Min(r.UnblockedDamage, (double)focus.Amount), source.ToString()));
        Capture.Emit(new DamageDealt(dealerActor, Capture.ToActor(target),
            r.BlockedDamage, r.UnblockedDamage, r.OverkillDamage, r.WasTargetKilled, source, kind, avoidable));
        if (card != null && target.IsEnemy && r.UnblockedDamage > 0) CreditHelpers(card, dealer, target, r.UnblockedDamage);
        if (card == null && source.Kind == SourceKind.Potion && target.IsEnemy && r.WasTargetKilled && Capture.PotionUser is { } drinker)
            Capture.Safe("potion after attacks", () => BlockAlternatives.OnPotionHit(drinker, $"Potion:{source.Id}", target,
                r.BlockedDamage + r.UnblockedDamage + r.OverkillDamage, r.OverkillDamage));
        if (card is Unleash or Protector && dealer is { IsPet: true } pet && target.IsEnemy && r.UnblockedDamage > 0)
            Capture.Safe("pet HP attack", () => ReportPetHpAttack(card, pet, r.UnblockedDamage));
        if (card != null) BlockAlternatives.OnHit(card, target, r.BlockedDamage + r.UnblockedDamage, dealer, r.OverkillDamage, r.WasTargetKilled,
            r.BlockedDamage);
        // A player's power hitting an enemy right after a card (Trou noir): follow-up of that card (v0.17.42).
        else if (source.Kind == SourceKind.Power && target.IsEnemy && dealer is { IsPlayer: true })
            BlockAlternatives.OnFollowUpHit(dealer, source.ToString(), target, r.BlockedDamage + r.UnblockedDamage + r.OverkillDamage);
        // Orb hits Focus boosts (Lightning, Glass). A Dark orb's evoke deals what it stored before: Focus gained now
        // would not change it (its charging is checked in DarkCharged).
        if (source is { Kind: SourceKind.Orb } orb && (orb.Id == ModelDb.GetId<LightningOrb>().Entry || orb.Id == ModelDb.GetId<GlassOrb>().Entry) &&
            target.IsEnemy && dealer?.Player is { } orbOwner)
            BlockAlternatives.OnOrbTriggered(orbOwner, target);
    }

    /// Damage of a card an ally created for the dealer (Blade Symphony, Largesse, Outrage…), or of the extra plays an
    /// ally's Tag Team gave: enabled for that ally (spec 01-metrics §B2).
    private static void CreditHelpers(CardModel card, Creature? dealer, Creature target, int damage)
    {
        var dealerActor = Capture.ToActor(dealer);
        if (!dealerActor.IsPlayerSide) return;
        if (GiftedCards.TryGetValue(card, out var giver) && giver.NetId != dealerActor.PlayerId)
            Capture.Emit(new DebuffEffect(Capture.ToActor(target), $"Card:{Capture.IdOf(card)}", dealerActor, damage, Offense: true,
                Credit: DebuffCredit.Direct, Applier: Capture.ToActor(giver)));
        if (ExtraPlays.TryGetValue(card, out var extra) && Capture.CardPlays.TryPeek(out var play) && play.Card == card &&
            play.PlayIndex >= extra.From)
            Capture.Emit(new DebuffEffect(Capture.ToActor(target), "TAG_TEAM_POWER", dealerActor, damage, Offense: true,
                Credit: DebuffCredit.Direct, Applier: Capture.ToActor(extra.Applier)));
    }

    /// Déchaînement: base + 1 per current HP of Osty; Protecteur: base + 1 per max HP. The part of the damage due to the
    /// HP is base-free: damage × (HP part ÷ whole).
    private static void ReportPetHpAttack(CardModel card, Creature pet, int damage)
    {
        var vars = card.DynamicVars;
        var baseValue = (double)vars.CalculationBase.BaseValue;
        var perHp = (double)vars.ExtraDamage.BaseValue;
        var hp = card is Unleash ? pet.CurrentHp : pet.MaxHp;
        var whole = baseValue + perHp * hp;
        if (whole <= 0 || hp <= 0) return;
        var part = damage * perHp * hp / whole;
        Capture.Emit(card is Unleash ? new PetHpAttack(Capture.ToActor(pet), part, 0, pet.CurrentHp)
            : new PetHpAttack(Capture.ToActor(pet), 0, part, pet.CurrentHp));
    }

    /// Status and curse cards created during the fight, by who created them (null = an enemy; v0.17.38).
    public static readonly ConditionalWeakTable<CardModel, StrongBox<MegaCrit.Sts2.Core.Entities.Players.Player?>> StatusCreators = new();
    /// The enemy whose move created a status or curse card (v0.17.45).
    public static readonly ConditionalWeakTable<CardModel, Creature> StatusEnemies = new();

    /// The holder of the Haunt power triggering right now (two Nécrolieurs may both hold it, v0.17.38).
    private static Creature? HauntOwner;

    [HarmonyPatch(typeof(HauntPower), nameof(HauntPower.AfterCardPlayed))]
    public static class HauntTrigger
    {
        public static void Prefix(HauntPower __instance) => HauntOwner = __instance.Owner;
        public static void Postfix() => HauntOwner = null;
    }

    /// Cards created by a player for another player (the creator).
    public static readonly ConditionalWeakTable<CardModel, MegaCrit.Sts2.Core.Entities.Players.Player> GiftedCards = new();

    /// Cards played extra times by an ally's Tag Team: plays from index From on are the extra ones.
    private sealed record Extra(Creature Applier, int From);
    private static readonly ConditionalWeakTable<CardModel, Extra> ExtraPlays = new();

    [HarmonyPatch(typeof(TagTeamPower), nameof(TagTeamPower.ModifyCardPlayCount))]
    public static class TagTeamPlays
    {
        public static void Postfix(TagTeamPower __instance, CardModel card, int playCount, int __result) =>
            Capture.Safe("TagTeam.ModifyCardPlayCount", () =>
            {
                if (__result > playCount && __instance.Applier is { IsPlayer: true } applier)
                    ExtraPlays.AddOrUpdate(card, new Extra(applier, playCount));
            });
    }

    private static DamageCall WithDot(DamageCall call, Creature target)
    {
        if (call.CallingModel == null) return call;
        var powers = target.Powers.Where(p => p.GetType() == call.CallingModel).ToList();
        var power = powers.FirstOrDefault();
        if (power?.Type != PowerType.Debuff) return call;
        // One instance per applier (Strangle triggers on its applier's card plays): that applier's instance.
        Creature? applier = null;
        if (power.InstanceType == PowerInstanceType.InstancedPerApplier && Capture.CurrentActingPlayer?.Creature is { } acting)
            applier = powers.FirstOrDefault(p => p.Applier == acting)?.Applier;
        // On a player or pet (Fatalité from Influx neuronal, an enemy's Poison…): whoever applied it is the dealer, so the
        // HP lost count as self-inflicted, from allies or from enemies (v0.17.7).
        else if (!target.IsEnemy && power.Applier != null) applier = power.Applier;
        return call with { DotPowerId = Capture.IdOf(power), DotApplier = applier };
    }

    /// Carapace endurcie cutting a hit at its limit (v0.17.45): the damage it stopped is beyond what the enemy could take,
    /// counted in Excédent like overkill.
    [HarmonyPatch(typeof(HardenedShellPower), nameof(HardenedShellPower.ModifyHpLostBeforeOstyLate))]
    public static class ShellCut
    {
        public static void Postfix(HardenedShellPower __instance, Creature target, decimal amount, Creature? dealer, decimal __result) =>
            Capture.Safe("HardenedShell.Cut", () =>
            {
                if (DebuffEffectPatches.Computing || target != __instance.Owner || __result >= amount || !CombatManager.Instance.IsInProgress) return;
                var dealerActor = Capture.ToActor(dealer);
                if (!dealerActor.IsPlayerSide) return;
                Capture.Emit(new DamageCapped(dealerActor, Capture.ToActor(target), (double)(amount - __result)));
                if (dealer != null) BlockAlternatives.OnCapped(dealer, target);
            });
    }

    /// A Dark orb charging (its passive) during a card play: a later Focus would have stored more damage (v0.17.32).
    [HarmonyPatch(typeof(DarkOrb), nameof(DarkOrb.Passive))]
    public static class DarkCharged
    {
        public static void Prefix(DarkOrb __instance) => Capture.Safe("DarkOrb.Passive", () =>
            BlockAlternatives.OnOrbTriggered(__instance.Owner, null, "orderDark"));
    }
}
