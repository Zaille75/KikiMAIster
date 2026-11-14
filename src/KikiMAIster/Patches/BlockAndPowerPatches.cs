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
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.ValueProps;

namespace KikiMAIster.Patches;

public static class BlockAndPowerPatches
{
    private sealed record BlockCall(Player? Giver, Source Source, int PlayId = 0);

    private static readonly ConditionalWeakTable<Creature, BlockCall> PendingGiver = new();

    /// The player whose Frost orb is triggering (v1.1.1, Zaille75): under Hibernation its block also goes to every other player,
    /// and it is the orb owner's, not the receiver's. Async-local: each orb trigger's awaited steps keep their own owner.
    private static readonly AsyncLocal<Player?> FrostOwner = new();

    [HarmonyPatch(typeof(FrostOrb), nameof(FrostOrb.Passive))]
    public static class FrostPassive
    {
        public static void Prefix(FrostOrb __instance) => Capture.Safe("FrostOrb.Passive", () => FrostOwner.Value = __instance.Owner);
    }

    [HarmonyPatch(typeof(FrostOrb), nameof(FrostOrb.Evoke))]
    public static class FrostEvoke
    {
        public static void Prefix(FrostOrb __instance) => Capture.Safe("FrostOrb.Evoke", () => FrostOwner.Value = __instance.Owner);
    }

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
                SourceKind.Orb when model == typeof(FrostOrb) && FrostOwner.Value is { } orbOwner => orbOwner,
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
            Capture.Emit(new BlockGained(Capture.ToActor(receiver), giver, amount, call?.Source ?? Source.Unknown, call?.PlayId ?? 0,
                StatPart(call?.Source.Kind == SourceKind.Orb && call.Giver != null ? call.Giver.Creature : receiver, amount, call?.Source)));
            // Sacrifice: its block comes from the pet's max HP (allies who gave those HP enabled it).
            if (call?.Source is { Kind: SourceKind.Card } src && src.Id == ModelDb.GetId<Sacrifice>().Entry)
                Capture.Emit(new PetHpBlock(Capture.ToActor(receiver), receiver.Player?.Osty is { } osty ? Capture.ToActor(osty) : default));
            // Oubli (temporary Dexterity from an ally, v0.17.38): the Dexterity they gave added to a card's block.
            if (call?.Source.Kind == SourceKind.Card && receiver.IsPlayer)
                Capture.Safe("block enabled", () => EnabledByAllyDexterity(receiver, amount));
            BlockAlternatives.OnBlockGained(call?.PlayId ?? 0, amount);
            // En boule / Pleutre (v0.18.9): an enemy's block set off by a player's hit.
            if (receiver.IsEnemy && call?.Source is { Kind: SourceKind.Power } guard &&
                (guard.Id == ModelDb.GetId<CurlUpPower>().Entry || guard.Id == ModelDb.GetId<SkittishPower>().Entry))
                BlockAlternatives.OnEnemyBlockOnHit(receiver, amount, guard.ToString());
            if (call?.Source is { Kind: SourceKind.Power } fed) BlockAlternatives.OnPowerTriggered(receiver, fed.ToString());
            if (call?.Source is { Kind: SourceKind.Orb } orb && orb.Id == ModelDb.GetId<FrostOrb>().Entry && (call.Giver ?? receiver.Player) is { } owner)
                BlockAlternatives.OnOrbTriggered(owner, null, "orderBlock");
        });
    }

    /// The part of a block gain due to the receiver's Dexterity (a card's block, after Frail) or Focus (a Frost orb: its owner's,
    /// v1.1.1), so the share a relic or potion gave counts for it (v0.17.45).
    private static double StatPart(Creature receiver, int amount, Source? source)
    {
        if (amount <= 0 || source is not { } src) return 0;
        if (src.Kind == SourceKind.Card && receiver.GetPower<DexterityPower>() is { Amount: > 0 } dex)
            return Math.Min(amount, (double)dex.Amount * (receiver.HasPower<FrailPower>() ? 0.75 : 1));
        if (src.Kind == SourceKind.Orb && src.Id == ModelDb.GetId<FrostOrb>().Entry && receiver.GetPower<FocusPower>() is { Amount: > 0 } focus)
            return Math.Min(amount, (double)focus.Amount);
        return 0;
    }

    /// Temporary Dexterity given by allies (Oubli): each ally's points, within the Dexterity the player really has, added
    /// that much to this block gain.
    private static void EnabledByAllyDexterity(Creature receiver, int amount)
    {
        var dexterity = receiver.GetPower<DexterityPower>()?.Amount ?? 0;
        if (dexterity <= 0 || amount <= 0) return;
        var given = receiver.Powers.OfType<TemporaryDexterityPower>()
            .Where(p => p.Type == PowerType.Buff && p.Amount > 0 && p.Applier is { IsPlayer: true } a && a != receiver).ToList();
        var total = given.Sum(p => (double)p.Amount);
        if (total <= 0) return;
        var scale = Math.Min(1, dexterity / total);
        foreach (var p in given)
            Capture.Emit(new BlockEnabled(Capture.ToActor(receiver), Capture.ToActor(p.Applier), Math.Min(amount, p.Amount * scale)));
    }

    /// Strength players removed from each enemy for good (Strength down cards, potions…), v0.17.38. Temporary losses are read
    /// from their own powers.
    private static readonly ConditionalWeakTable<Creature, StrongBox<double>> PermanentStrengthDown = new();

    /// The Strength players removed from this enemy that is still gone: permanent losses + temporary ones (Enfeebling
    /// Touch…) still on it.
    public static double StrengthDownFromPlayers(Creature enemy)
    {
        var permanent = PermanentStrengthDown.TryGetValue(enemy, out var box) ? box.Value : 0;
        var temporary = enemy.Powers.OfType<TemporaryStrengthPower>()
            .Where(p => p.Type == PowerType.Debuff && p.Applier is { IsPlayer: true }).Sum(p => Math.Abs((double)p.Amount));
        return permanent + temporary;
    }

    [HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.LoseBlock))]
    public static class LoseBlock
    {
        public static void Prefix(Creature target, decimal amount, Creature? remover) =>
            Capture.Safe("CreatureCmd.LoseBlock", () => BlockLost(target, amount, remover));
    }

    private static void BlockLost(Creature target, decimal amount, Creature? remover)
    {
        if (CombatManager.Instance.IsOverOrEnding || target.IsDead || amount <= 0) return;
        var lost = (int)Math.Min(amount, target.Block);
        if (lost <= 0) return;
        // A player removing their own block (to use it for something else) is "spent", not wasted.
        var spent = remover != null && remover.IsPlayer && remover == target;
        Capture.Emit(new BlockRemoved(Capture.ToActor(target), lost, spent));
    }

    [HarmonyPatch(typeof(CombatHistory), nameof(CombatHistory.PowerReceived))]
    public static class HistoryPower
    {
        public static void Prefix(PowerModel power, decimal amount, Creature? applier) => Capture.Safe("History.PowerReceived", () =>
        {
            var enabledBy = EnabledBy(applier);
            BlockAlternatives.OnPowerApplied(power, amount, Stacking.Remove(power));
            // Strength given by another player (Blaze…): counts for the giver. Temporary Strength (Coordinate) is read from
            // its own power at each hit instead.
            if (power is StrengthPower && amount > 0 && power.Owner.IsPlayer && applier is { IsPlayer: true } giver &&
                giver != power.Owner && !FromTemporaryStrength())
                Capture.Emit(new StrengthGiven(Capture.ToActor(power.Owner), Capture.ToActor(giver), (double)amount));
            // Dominate: the Strength it gives equals the target's Vulnerable, borrowed from whoever applied it.
            if (power is StrengthPower && amount > 0 && power.Owner.IsPlayer && Capture.CardPlays.TryPeek(out var play) &&
                play.Card is Dominate && play.Target is { IsEnemy: true } target)
                Capture.Emit(new StrengthBorrowed(Capture.ToActor(power.Owner), Capture.ToActor(target), ModelDb.GetId<VulnerablePower>().Entry, (double)amount));
            // Esquisse & co.: cards or energy promised to an ally for their next turn. Counted as help only when that turn
            // comes (the fight goes on and the ally is alive, v0.17.2); the draw or energy itself has no acting player
            // and is not counted again.
            if (amount > 0 && power is DrawCardsNextTurnPower or EnergyNextTurnPower && power.Owner.IsPlayer &&
                applier is { IsPlayer: true } helper && helper != power.Owner)
                NextTurnHelp.Add((helper, power.Owner, power is DrawCardsNextTurnPower ? "cards" : "energy", (double)amount));
            var id = Capture.IdOf(power);
            var value = (double)amount;
            // À l'attaque ! marks an enemy so that Osty's hits summon: the game calls it a debuff, but it does nothing
            // against the enemy (v0.17.1: not an Affaiblissement).
            var debuff = power.Type == PowerType.Debuff && power is not SicEmPower;
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
            if (power is StrengthPower && amount < 0 && power.Owner.IsEnemy && applier is { IsPlayer: true })
                PermanentStrengthDown.GetOrCreateValue(power.Owner).Value += (double)-amount;
            // A relic applying it (Vajra, Soufre, Sac de billes…, v0.17.45): its origin, and its owner as the applier when the game
            // gives none (Soufre's Strength on the enemies).
            var origin = Capture.CurrentOrigin;
            if (origin == "" && Capture.CallingModelType() is { } caller && typeof(RelicModel).IsAssignableFrom(caller))
            {
                origin = Capture.SourceOf(caller).ToString();
                if (applier == null && power.Owner.CombatState is { } cs) applier = Capture.UniqueOwnerOf(caller, cs.Players)?.Creature;
            }
            Capture.Emit(new PowerChanged(Capture.ToActor(applier), Capture.ToActor(power.Owner), id, value, debuff, origin,
                enabledBy is { } e ? Capture.ToActor(e.Giver) : default, enabledBy?.Source ?? ""));
        });
    }

    private static bool FromTemporaryStrength() =>
        Capture.CallingModelType() is { } caller && typeof(TemporaryStrengthPower).IsAssignableFrom(caller);

    /// A power applied by a player because of a buff an ally gave them (Concoct: Poison when they hit): the applier
    /// keeps it (and its damage), the ally who gave the buff enabled it. Null when no ally is involved.
    private static (Creature Giver, string Source)? EnabledBy(Creature? applier)
    {
        if (applier is not { IsPlayer: true }) return null;
        var model = Capture.CallingModelType();
        if (model == null || !typeof(PowerModel).IsAssignableFrom(model)) return null;
        var buff = applier.Powers.FirstOrDefault(p => p.GetType() == model);
        return buff?.Applier is { IsPlayer: true } giver && giver != applier ? (giver, Capture.IdOf(buff)) : null;
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

    /// Powers whose amount is being changed on a creature that already had them (stacking): reported by the history
    /// before the new amount is set, so the history alone cannot tell a first application from added turns.
    private static readonly HashSet<PowerModel> Stacking = new(ReferenceEqualityComparer.Instance);

    /// Help promised for the ally's next turn (Esquisse…): giver, ally, kind, amount.
    private static readonly List<(Creature Giver, Creature Ally, string Kind, double Amount)> NextTurnHelp = new();

    /// The players' next turn starts: the promised help is given to allies still alive.
    public static void GiveNextTurnHelp()
    {
        foreach (var (giver, ally, kind, amount) in NextTurnHelp.Where(h => h.Ally.IsAlive))
            Capture.Emit(new HelpGiven(Capture.ToActor(giver), kind, amount));
        NextTurnHelp.Clear();
    }

    /// Combat start or end: help promised for a turn that never came is dropped.
    public static void Reset()
    {
        Stacking.Clear();
        NextTurnHelp.Clear();
    }

    [HarmonyPatch(typeof(PowerCmd), nameof(PowerCmd.ModifyAmount))]
    public static class PowerModify
    {
        public static void Prefix(PowerModel power, decimal offset) => Capture.Safe("PowerCmd.ModifyAmount", () =>
        {
            Stacking.Add(power);
            SuppressIfFromTemporaryPower(power, power.Owner, offset);
        });
    }

    /// Heals outside fights cut by the Max HP, per player: the floor, the HP cut and what healed (v0.17.42).
    private static readonly Dictionary<Player, (int Floor, int Cut, string Source)> CappedHeals = new();

    /// Max HP gained outside fights after a heal of the same floor was cut by the old Max HP (Faux marchand: Fiole de sang
    /// before Mangue): the heal would have counted in full the other way round. A misplay outside fights (v0.17.42,
    /// Zaille75), with the HP lost.
    /// The relic, potion or card healing / raising Max HP; else the event or room of the floor ("Event:FAKE_MERCHANT").
    private static string HealSource(Player player)
    {
        var source = Capture.SourceOf(Capture.CallingModelType());
        if (source.Kind != SourceKind.Unknown) return source.ToString();
        if (player.RunState.CurrentMapPointHistoryEntry?.Rooms.LastOrDefault()?.ModelId is { } id &&
            ModelDb.GetByIdOrNull<AbstractModel>(id) is { } model)
            return $"Event:{Capture.IdOf(model)}";
        return "Unknown";
    }

    [HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.GainMaxHp))]
    public static class GainMaxHp
    {
        public static void Prefix(Creature creature, decimal amount) => Capture.Safe("CreatureCmd.GainMaxHp", () =>
        {
            if (CombatManager.Instance.IsInProgress || creature.Player is not { } player ||
                !CappedHeals.TryGetValue(player, out var heal) || heal.Floor != player.RunState.TotalFloor || heal.Cut <= 0) return;
            var lost = Math.Min(heal.Cut, (int)amount);
            if (lost <= 0) return;
            CappedHeals[player] = heal with { Cut = heal.Cut - lost };
            var source = HealSource(player);
            ModLog.Info($"max HP after a capped heal: {heal.Source} before {source}, {lost} HP of heal lost");
            Capture.Emit(new MissedChance(Capture.ToActor(player), "maxHpAfterHeal",
                new[] { heal.Source, source, lost.ToString(System.Globalization.CultureInfo.InvariantCulture) }));
        });
    }

    [HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Heal))]
    public static class Heal
    {
        public static void Prefix(Creature creature, decimal amount) => Capture.Safe("CreatureCmd.Heal", () =>
        {
            if (creature.IsDead) return;
            if (!CombatManager.Instance.IsInProgress)
            {
                if (creature.IsPlayer && creature.Player is { } healedPlayer && amount > creature.MaxHp - creature.CurrentHp)
                {
                    var floor = healedPlayer.RunState.TotalFloor;
                    var cut = (int)(amount - (creature.MaxHp - creature.CurrentHp));
                    var previous = CappedHeals.TryGetValue(healedPlayer, out var h) && h.Floor == floor ? h.Cut : 0;
                    CappedHeals[healedPlayer] = (floor, previous + cut, HealSource(healedPlayer));
                }
                // Réparer at a campfire (v0.17.17): the heal given to the ally counts as HP saved for them.
                if (RestSitePatches.Mender is { } mender && creature.IsPlayer && creature.Player != mender)
                {
                    var given = (int)Math.Min(amount, creature.MaxHp - creature.CurrentHp);
                    if (given > 0) Capture.Emit(new Healed(Capture.ToActor(mender), Capture.ToActor(creature), given));
                    RestSitePatches.Mender = null;
                }
                // Right after a fight (Sang ardent…, v0.17.38): the game's history counts it on the fight's floor, which the
                // heals outside fights leave out.
                else if (creature.IsPlayer && creature.Player?.RunState.CurrentRoom is CombatRoom)
                {
                    var healedAfter = Math.Min(amount, creature.MaxHp - creature.CurrentHp);
                    if (healedAfter > 0) Capture.Emit(new AfterFightHealed(Capture.ToActor(creature), (double)healedAfter));
                }
                return;
            }
            var healed = (int)Math.Min(amount, creature.MaxHp - creature.CurrentHp);
            if (healed <= 0) return;
            BlockAlternatives.OnEffect();
            var healer = Capture.ToActor(Capture.CurrentActingPlayer);
            var target = Capture.ToActor(creature);
            if (target.Kind == ActorKind.Pet)
            {
                // The heal that is part of a summon is already counted by History.Summoned.
                if (!Capture.IsInsideCall(typeof(OstyCmd)))
                    Capture.Emit(new PetHpGiven(healer.Kind == ActorKind.Player ? healer : Actor.Player(target.PlayerId, 0), healed,
                        Actor.Player(target.PlayerId, 0), target));
                // Déchaînement grows with Titos's HP only (1.2.0: not Wuppy's).
                if (creature.PetOwner is { } petOwner && petOwner.Osty == creature) BlockAlternatives.OnPetHpRaised(petOwner, healed);
                return;
            }
            // What healed (v0.17.39): the potion being drunk, else the relic, card or power calling.
            var source = Capture.CurrentOrigin != "" ? Capture.CurrentOrigin : Capture.SourceOf(Capture.CallingModelType()).ToString();
            Capture.Emit(new Healed(healer, target, healed, source));
        });
    }

    /// Summon (new pet, revive, max HP gain): HP given to the pet, and an effect of the card being played.
    [HarmonyPatch(typeof(CombatHistory), nameof(CombatHistory.Summoned))]
    public static class Summoned
    {
        public static void Prefix(int amount, Player player) => Capture.Safe("History.Summoned", () =>
        {
            BlockAlternatives.OnEffect();
            // A card that summons for every player (co-op) is credited to whoever played it, not to each pet owner.
            var giver = Capture.CurrentActingPlayer ?? player;
            // History.Summoned is Titos's (OstyCmd): the pet is the player's Osty (1.2.0: Wuppy can be there too).
            Capture.Emit(new PetHpGiven(Capture.ToActor(giver), amount, Capture.ToActor(player),
                player.Osty is { } osty ? Capture.ToActor(osty) : default));
            BlockAlternatives.OnPetHpRaised(player, amount);
        });
    }
}
