using System.Runtime.CompilerServices;
using KikiMAIster.Core;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;

namespace KikiMAIster.Patches;

/// Decides, at the end of each player turn, whether each block card played that turn had a real alternative:
/// a useful card (attack, skill or power that gives no block) that was in hand when the block card was played,
/// was never played, and could be paid with the
/// block card's energy + the energy left at the end of the turn (spec 01-metrics §F, wasted vs useless).
/// A block card that also did something else (debuff, damage, draw…) or is Ethereal never counts as wasted.
/// Also finds the other missed opportunities of the turn (spec 01-metrics §E): a lethal front hand that was not
/// played, and a weak attack played while a stronger one stayed in hand.
public static class BlockAlternatives
{
    private sealed class Play
    {
        public required int Id { get; init; }
        public required Player Owner { get; init; }
        public required CardModel Card { get; init; }
        public required int EnergySpent { get; init; }
        public required List<CardModel> UsefulCardsInHand { get; init; }
        public bool GaveBlock { get; set; }
        public bool HadOtherEffect { get; set; }
        /// An effect other than block or damage (debuff, draw, upgrade…): an attack played for it was a real choice.
        public bool HadNonDamageEffect { get; set; }
        /// Block this play gave (all receivers).
        public int BlockAmount { get; set; }
        /// It applied a debuff that does nothing: 1 Weak on an enemy that does not attack this turn, or 1 Vulnerable on an
        /// enemy with none that nobody hits before the end of the turn (spec §F, F8).
        public bool UselessDebuff { get; set; }
        public required bool Ethereal { get; init; }
        /// Attack played: a stronger attack in hand on the same target (same cost or cheaper), with both damages.
        public (CardModel Card, int Damage, int Played)? Stronger { get; set; }
        /// Attack doubled by Pen Nib while a stronger affordable attack was in hand (both shown doubled).
        public (CardModel Card, int Damage, int Played)? PenNibBetter { get; set; }
        /// Block card played: a block card in hand giving more block (same cost or cheaper), with both amounts.
        public (CardModel Card, int Block, int Played)? BiggerBlock { get; set; }
    }

    private static readonly ConditionalWeakTable<CardPlay, Play> Plays = new();
    private static readonly List<Play> ThisTurn = new();
    private static readonly Stack<Play?> InProgress = new();
    private static int _nextId;
    /// Players whose front hand was already checked this turn, and the lethal combination found (if any).
    private static readonly Dictionary<Player, Lethal.Attack[]?> FrontHand = new();
    /// Cards each player played this turn (auto-plays included, as relics count them), by type.
    private static readonly Dictionary<Player, Dictionary<CardType, int>> TurnPlays = new();
    /// Enemies given their first Vulnerable this turn, and the play that gave it (useless until someone hits them).
    private static readonly Dictionary<Creature, Play> PendingVulnerable = new();

    public static void Reset()
    {
        ThisTurn.Clear();
        InProgress.Clear();
        FrontHand.Clear();
        TurnPlays.Clear();
        PendingVulnerable.Clear();
    }

    /// Something other than block happened (power applied, damage, heal, draw, orb, card created…):
    /// credited to the card play in progress, if any.
    public static void OnEffect(bool damage = false)
    {
        if (!InProgress.TryPeek(out var play) || play == null) return;
        play.HadOtherEffect = true;
        if (!damage) play.HadNonDamageEffect = true;
    }

    /// A power applied during the play in progress: an effect, except 1 Weak on an enemy that does not attack this turn
    /// (it wears off before doing anything).
    /// Stacked: the power was already on its owner (adding turns to it is always an effect).
    public static void OnPowerApplied(PowerModel power, decimal amount, bool stacked)
    {
        if (InProgress.TryPeek(out var play) && play != null && !stacked && amount == 1 && power.Owner.IsEnemy)
        {
            if (power is WeakPower && !Attacks(power.Owner))
            {
                play.UselessDebuff = true;
                return;
            }
            // Useful only if someone hits that enemy with an attack before the end of the turn (OnVulnerableUsed).
            if (power is VulnerablePower)
            {
                play.UselessDebuff = true;
                PendingVulnerable[power.Owner] = play;
                return;
            }
        }
        OnEffect();
    }

    /// A real attack hit was increased by Vulnerable on this enemy: the play that applied it did something.
    public static void OnVulnerableUsed(Creature enemy)
    {
        if (!PendingVulnerable.Remove(enemy, out var play)) return;
        play.HadOtherEffect = true;
        play.HadNonDamageEffect = true;
    }

    private static bool Attacks(Creature enemy)
    {
        try
        {
            return enemy.Monster?.NextMove.Intents.OfType<AttackIntent>().Any() ?? false;
        }
        catch (Exception)
        {
            return true;
        }
    }

    public static void OnBlockGained(int playId, int amount)
    {
        if (playId == 0) return;
        var play = ThisTurn.FirstOrDefault(p => p.Id == playId);
        if (play != null) play.BlockAmount += amount;
    }

    /// Another card discarded or exhausted by the play in progress (Survivor…). The played card itself going to
    /// its pile is not an effect.
    public static void OnCardEffect(CardModel card)
    {
        if (InProgress.TryPeek(out var play) && play != null && play.Card != card) play.HadOtherEffect = true;
    }

    public static void OnPlayFinished()
    {
        if (InProgress.Count > 0) InProgress.Pop();
    }

    /// Card play starts: remember the useful cards in the player's hand. Auto-played cards were not a choice.
    public static void OnPlayStarted(CardPlay cardPlay)
    {
        if (!TurnPlays.TryGetValue(cardPlay.Player, out var counts)) TurnPlays[cardPlay.Player] = counts = new();
        counts[cardPlay.Card.Type] = counts.GetValueOrDefault(cardPlay.Card.Type) + 1;
        var hand = cardPlay.Player.PlayerCombatState?.Hand.Cards;
        if (cardPlay.IsAutoPlay || hand == null)
        {
            InProgress.Push(null);
            return;
        }
        // Not checked at turn start: the hand before this first card is still the front hand.
        if (!FrontHand.ContainsKey(cardPlay.Player))
            FrontHand[cardPlay.Player] = FindLethal(cardPlay.Player, hand.Append(cardPlay.Card).Distinct().ToList(),
                (cardPlay.Player.PlayerCombatState?.Energy ?? 0) + cardPlay.Resources.EnergySpent);
        var play = new Play
        {
            Id = ++_nextId,
            Owner = cardPlay.Player,
            Card = cardPlay.Card,
            EnergySpent = cardPlay.Resources.EnergySpent,
            Ethereal = cardPlay.Card.Keywords.Contains(CardKeyword.Ethereal),
            // Genetic Algorithm / The Scythe grow each time they are played: a side effect no hook reports.
            // Sacrifice kills the player's own pet (validated by Zaille75: never wasted).
            HadOtherEffect = cardPlay.Card is GeneticAlgorithm or TheScythe or Sacrifice,
            HadNonDamageEffect = cardPlay.Card is GeneticAlgorithm or TheScythe,
            UsefulCardsInHand = hand.Where(c => c != cardPlay.Card && IsUseful(c)).ToList()
        };
        var energyBefore = (cardPlay.Player.PlayerCombatState?.Energy ?? 0) + cardPlay.Resources.EnergySpent;
        if (cardPlay.Card.Type == CardType.Attack && cardPlay.Target is { IsEnemy: true } target)
        {
            if (IsDoubledByPenNib(cardPlay)) play.PenNibBetter = StrongerAttack(cardPlay.Card, target, hand, energyBefore, doubled: true);
            else play.Stronger = StrongerAttack(cardPlay.Card, target, hand, Cost(cardPlay.Card), doubled: false);
        }
        if (cardPlay.Card.GainsBlock) play.BiggerBlock = BiggerBlock(cardPlay.Card, hand);
        Plays.AddOrUpdate(cardPlay, play);
        ThisTurn.Add(play);
        InProgress.Push(play);
    }

    /// The play id to attach to block gained from this card play (0 if not tracked).
    public static int PlayIdFor(CardPlay cardPlay)
    {
        if (!Plays.TryGetValue(cardPlay, out var play)) return 0;
        play.GaveBlock = true;
        return play.Id;
    }

    /// The player's hand is drawn: check the front hand for a lethal combination.
    public static void OnTurnStarted(Player player)
    {
        if (player.PlayerCombatState is { } st) FrontHand[player] = FindLethal(player, st.Hand.Cards.ToList(), st.Energy);
    }

    /// End of the players' turn: each attack can be the alternative of only one block card.
    public static void ResolveTurn(ICombatState combat)
    {
        ResolveMissedChances(combat);
        var used = new HashSet<CardModel>();
        foreach (var play in ThisTurn.Where(p => p.GaveBlock))
        {
            var state = play.Owner.PlayerCombatState;
            var budget = play.EnergySpent + (state?.Energy ?? 0);
            var creature = play.Owner.Creature;
            var surplus = creature.Block - IncomingDamage(creature);
            // An attack into Thorns is an alternative only if the block left would still have covered the Thorns damage.
            var alternative = state == null || play.HadOtherEffect || play.Ethereal ? null : play.UsefulCardsInHand.FirstOrDefault(c =>
                !used.Contains(c) && state.Hand.Cards.Contains(c) && Cost(c) <= budget && PlayableExceptEnergy(c) &&
                ThornsDamage(c, creature) <= Math.Max(0, surplus));
            if (alternative != null) used.Add(alternative);
            ModLog.Info($"block play {play.Id}: other effect {play.HadOtherEffect}, ethereal {play.Ethereal}, energy budget {budget}, useful cards in hand when played: " +
                        $"[{string.Join(", ", play.UsefulCardsInHand.Select(c => Capture.IdOf(c)))}] → alternative: " +
                        (alternative != null ? Capture.IdOf(alternative) : "none"));
            Capture.Emit(new BlockPlayResolved(play.Id, alternative != null,
                alternative == null || state == null ? null : Explain(play, alternative, state)));
        }
        ThisTurn.Clear();
    }

    /// Missed lethal (front hand could kill every enemy, yet they are alive at the end of the turn) and weak attacks
    /// (the stronger attack is still in hand at the end of the turn, and the weak one did nothing else).
    private static void ResolveMissedChances(ICombatState combat)
    {
        var enemies = combat.HittableEnemies;
        if (enemies.Count > 0)
            foreach (var (player, lethal) in FrontHand)
                if (lethal != null)
                {
                    ModLog.Info($"missed lethal: {player.NetId} [{string.Join(", ", lethal.Select(a => a.Id))}]");
                    Capture.Emit(new MissedLethal(Capture.ToActor(player), lethal.Select(a => a.Id).ToList(),
                        enemies.Sum(e => e.CurrentHp + e.Block)));
                }
        FrontHand.Clear();

        foreach (var play in ThisTurn)
        {
            var hand = play.Owner.PlayerCombatState?.Hand.Cards;
            if (hand == null) continue;
            var actor = Capture.ToActor(play.Owner);
            if (play.Stronger is { } better && !play.HadNonDamageEffect && hand.Contains(better.Card))
                Capture.Emit(new WeakAttack(actor, Key(play.Card), better.Played, Key(better.Card), better.Damage));
            // Pen Nib doubled a weak attack: counted even if the stronger one was played later (it was not doubled).
            if (play.PenNibBetter is { } nib && !play.HadNonDamageEffect)
                Capture.Emit(new MissedChance(actor, "penNib", new[] { Key(play.Card), N(nib.Played), Key(nib.Card), N(nib.Damage) }));
            // A smaller block card, while the bigger one stayed in hand and the block was not enough for the attack.
            if (play.BiggerBlock is { } bigger && !play.HadOtherEffect && hand.Contains(bigger.Card) &&
                play.Owner.Creature.Block < IncomingDamage(play.Owner.Creature))
                Capture.Emit(new MissedChance(actor, "smallBlock", new[] { Key(play.Card), N(bigger.Played), Key(bigger.Card), N(bigger.Block) }));
        }

        foreach (var player in combat.Players.Where(p => p.Creature.IsAlive))
            Capture.Safe("end of turn checks", () => EndOfTurnChecks(combat, player));
        TurnPlays.Clear();
        PendingVulnerable.Clear();
    }

    private static string Key(CardModel card) => $"Card:{Capture.IdOf(card)}";
    private static string N(int v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// Per player at the end of the turn: unused energy, growing card not played, block card kept, Orichalcum
    /// beaten by a small block, relic triggers missed by one card (spec 01-metrics §E0).
    private static void EndOfTurnChecks(ICombatState combat, Player player)
    {
        if (player.PlayerCombatState is not { } st) return;
        var actor = Capture.ToActor(player);
        var energy = st.Energy;
        var hand = st.Hand.Cards.ToList();
        bool Affordable(CardModel c) => !IsStatusOrCurse(c) && PlayableExceptEnergy(c) && (c.EnergyCost.CostsX || Cost(c) <= energy);
        var plays = TurnPlays.GetValueOrDefault(player) ?? new Dictionary<CardType, int>();

        // Unused energy: lost at the end of the turn (not kept by Ice Cream & co., not wanted by Pael's Tears).
        if (energy > 0 && hand.Any(Affordable) && Hook.ShouldPlayerResetEnergy(combat, player) && player.GetRelic<PaelsTears>() == null)
            Capture.Emit(new MissedChance(actor, "energy", new[] { N(energy) }, energy));

        // Pael's Tears gives energy next turn for energy left: energy spent on a useless card (at any time in the turn)
        // would have been left.
        if (energy == 0 && player.GetRelic<PaelsTears>() is { } tears)
        {
            var incoming = IncomingDamage(player.Creature);
            var useless = ThisTurn.FirstOrDefault(p => p.Owner == player && p.EnergySpent > 0 && !p.HadOtherEffect &&
                ((p.GaveBlock && !p.Ethereal && player.Creature.Block - p.BlockAmount >= incoming) || (p.UselessDebuff && !p.GaveBlock)));
            if (useless != null)
                Capture.Emit(new MissedChance(actor, "paelsTears", new[] { $"Relic:{Capture.IdOf(tears)}", Key(useless.Card) }));
        }

        foreach (var growing in hand.Where(c => c is GeneticAlgorithm or TheScythe && Affordable(c)))
            Capture.Emit(new MissedChance(actor, "growth", new[] { Key(growing) }));

        var kept = hand.Where(c => c.GainsBlock && Affordable(c)).Select(c => (Card: c, Block: EstimateBlock(c)))
            .Where(x => x.Block > 0).OrderByDescending(x => x.Block).FirstOrDefault();
        if (kept.Card != null) Capture.Emit(new BlockCardKept(actor, Key(kept.Card), kept.Block));

        // Orichalcum gives its block only when the player has none: a smaller block card played instead was a loss.
        RelicModel? orichalcum = (RelicModel?)player.GetRelic<Orichalcum>() ?? player.GetRelic<FakeOrichalcum>();
        if (orichalcum != null)
        {
            var given = (int)orichalcum.DynamicVars.Block.BaseValue;
            var block = player.Creature.Block;
            if (block > 0 && block <= given && ThisTurn.Any(p => p.Owner == player && p.GaveBlock && !p.HadOtherEffect && !p.Ethereal))
                Capture.Emit(new MissedChance(actor, "orichalcum", new[] { $"Relic:{Capture.IdOf(orichalcum)}", N(block), N(given) }));
        }

        // Relic triggers: the extra card must have been worth playing (spec §E0): energy that would be lost anyway, not an
        // attack into Thorns the block could not afford, and not taking the energy a needed block card would use.
        var energyLost = Hook.ShouldPlayerResetEnergy(combat, player) && player.GetRelic<PaelsTears>() == null;
        var creature = player.Creature;
        var surplus = creature.Block - IncomingDamage(creature);
        var blockCards = hand.Where(c => c.GainsBlock && Affordable(c)).ToList();
        var blockNeeded = surplus < 0 && blockCards.Count > 0;
        bool WorthPlaying(CardModel c, bool free)
        {
            if (IsStatusOrCurse(c) || !PlayableExceptEnergy(c)) return false;
            if (!free && (!energyLost || !Affordable(c))) return false;
            if (c.Type == CardType.Attack && (ThornsDamage(c, creature) > Math.Max(0, surplus) || !DealsDamage(c, combat))) return false;
            if (blockNeeded && !c.GainsBlock && !free && Cost(c) + blockCards.Min(Cost) > energy) return false;
            return true;
        }
        foreach (var relic in player.Relics)
        {
            if (MissedRelicTrigger(relic, plays, hand, energy, WorthPlaying))
                Capture.Emit(new MissedChance(actor, "relic", new[] { $"Relic:{Capture.IdOf(relic)}" }));
        }
    }

    /// One more card would have triggered the relic this turn, and a playable one was in hand.
    private static bool MissedRelicTrigger(RelicModel relic, Dictionary<CardType, int> plays, List<CardModel> hand, int energy,
        Func<CardModel, bool, bool> worthPlaying)
    {
        int Played(CardType t) => plays.GetValueOrDefault(t);
        bool InHand(CardType t) => hand.Any(c => c.Type == t && worthPlaying(c, false));
        int Every() => relic.DynamicVars.TryGetValue("Cards", out var v) ? v.IntValue : 3;
        switch (relic)
        {
            case Shuriken or Kunai or OrnamentalFan or Kusarigama:
                return Played(CardType.Attack) % Every() == Every() - 1 && InHand(CardType.Attack);
            case LetterOpener:
                return Played(CardType.Skill) % Every() == Every() - 1 && InHand(CardType.Skill);
            case RainbowRing:
            {
                var types = new[] { CardType.Attack, CardType.Skill, CardType.Power };
                var missing = types.Where(t => Played(t) == 0).ToList();
                return missing.Count == 1 && InHand(missing[0]);
            }
            case BrilliantScarf:
                // The Nth card of the turn is free: energy does not matter.
                return plays.Values.Sum() == Every() - 1 && hand.Any(c => worthPlaying(c, true));
            case ScreamingFlagon:
                // Damage to all enemies when the hand is empty at the end of the turn: every card must be worth playing.
                return hand.Count > 0 && hand.All(c => !c.EnergyCost.CostsX && worthPlaying(c, false)) && hand.Sum(Cost) <= energy;
            default:
                return false;
        }
    }

    /// The attack would deal damage to at least one enemy (cards whose damage the mod cannot compute count as useful).
    private static bool DealsDamage(CardModel card, ICombatState combat)
    {
        if (!card.DynamicVars.TryGetValue("Damage", out _) && !card.DynamicVars.TryGetValue("OstyDamage", out _)) return true;
        return combat.HittableEnemies.Any(e => EstimateDamage(card, e) > 0);
    }

    private static bool IsStatusOrCurse(CardModel c) => c.Type is CardType.Status or CardType.Curse;

    /// Pen Nib picks the attack to double before the play starts.
    private static bool IsDoubledByPenNib(CardPlay cardPlay)
    {
        var nib = cardPlay.Player.GetRelic<PenNib>();
        if (nib == null) return false;
        return Traverse.Create(nib).Property("AttackToDouble").GetValue<CardModel>() == cardPlay.Card;
    }

    /// A block card in hand giving more block than the one played (same cost or cheaper).
    private static (CardModel, int, int)? BiggerBlock(CardModel played, IEnumerable<CardModel> hand)
    {
        try
        {
            var block = EstimateBlock(played);
            if (block <= 0) return null;
            var cost = Cost(played);
            var best = hand.Where(c => c != played && c.GainsBlock && !c.EnergyCost.CostsX && Cost(c) <= cost && PlayableExceptEnergy(c))
                .Select(c => (Card: c, Block: EstimateBlock(c))).Where(x => x.Block > block)
                .OrderByDescending(x => x.Block).FirstOrDefault();
            return best.Card == null ? null : (best.Card, best.Block, block);
        }
        catch (Exception e)
        {
            ModLog.Error("bigger block", e);
            return null;
        }
    }

    /// The game's own block computation for a card (Dexterity, Frail…); 0 when its block is computed another way.
    private static int EstimateBlock(CardModel card)
    {
        if (!card.DynamicVars.TryGetValue("Block", out var v) || v is not BlockVar bv || card.CombatState is not { } cs) return 0;
        var block = Hook.ModifyBlock(cs, card.Owner.Creature, bv.BaseValue, bv.Props, card, null, out _);
        var times = card.DynamicVars.TryGetValue("Repeat", out var repeat) ? Math.Max(1, repeat.IntValue) : 1;
        return Math.Max(0, (int)block) * times;
    }

    /// The front hand's lethal combination, if the attacks in hand could kill every enemy that must die.
    private static Lethal.Attack[]? FindLethal(Player player, List<CardModel> hand, int energy)
    {
        try
        {
            var all = player.Creature.CombatState?.HittableEnemies ?? Array.Empty<Creature>();
            var enemies = all.Where(e => !e.IsSecondaryEnemy).ToList();
            if (enemies.Count == 0) enemies = all.ToList();
            if (enemies.Count == 0) return null;
            var attacks = hand.Where(c => c.Type == CardType.Attack && !c.EnergyCost.CostsX && PlayableExceptEnergy(c) &&
                                          c.TargetType is TargetType.AnyEnemy or TargetType.AllEnemies)
                .Select(c => new Lethal.Attack($"Card:{Capture.IdOf(c)}", Cost(c), c.TargetType == TargetType.AllEnemies,
                    enemies.Select(e => EstimateDamage(c, e)).ToList()))
                .ToList();
            var found = Lethal.Find(attacks, energy, enemies.Select(e => e.CurrentHp + e.Block).ToList());
            return found?.ToArray();
        }
        catch (Exception e)
        {
            ModLog.Error("lethal check", e);
            return null;
        }
    }

    /// The strongest attack in hand on the same target, if it beats the attack played, costs no more, and the
    /// played attack could not kill the target anyway.
    /// Budget: the most the stronger attack may cost. Doubled: the played attack is doubled by Pen Nib (its estimate
    /// already is), so the other attacks are compared doubled too.
    private static (CardModel, int, int)? StrongerAttack(CardModel played, Creature target, IEnumerable<CardModel> hand, int budget,
        bool doubled)
    {
        try
        {
            var damage = EstimateDamage(played, target);
            if (damage <= 0 || damage >= target.CurrentHp + target.Block) return null;
            var factor = doubled ? 2 : 1;
            var best = hand.Where(c => c != played && c.Type == CardType.Attack && !c.EnergyCost.CostsX && Cost(c) <= budget &&
                                       c.TargetType is TargetType.AnyEnemy or TargetType.AllEnemies && PlayableExceptEnergy(c))
                .Select(c => (Card: c, Damage: EstimateDamage(c, target) * factor))
                .Where(x => x.Damage > damage).OrderByDescending(x => x.Damage).FirstOrDefault();
            return best.Card == null ? null : (best.Card, best.Damage, damage);
        }
        catch (Exception e)
        {
            ModLog.Error("stronger attack", e);
            return null;
        }
    }

    /// The game's own damage computation for a card against one enemy (Strength, Vulnerable, Weak…) × its hits.
    /// 0 for cards whose damage is computed in another way (they are left out: the result stays a minimum).
    private static int EstimateDamage(CardModel card, Creature target)
    {
        var owner = card.Owner;
        Creature? dealer;
        DynamicVar? v;
        if (card.DynamicVars.TryGetValue("Damage", out v)) dealer = owner.Creature;
        else if (card.DynamicVars.TryGetValue("OstyDamage", out v)) dealer = owner.IsOstyAlive ? owner.Osty : null;
        else return 0;
        if (dealer == null) return 0;
        var props = v switch { DamageVar dv => dv.Props, OstyDamageVar ov => ov.Props, _ => ValueProp.Move };
        decimal damage;
        DebuffEffectPatches.Computing = true;
        try
        {
            damage = Hook.ModifyDamage(owner.RunState, card.CombatState, target, dealer, v.BaseValue, props, card, null,
                ModifyDamageHookType.All, CardPreviewMode.None, out _);
        }
        finally
        {
            DebuffEffectPatches.Computing = false;
        }
        var hits = card.DynamicVars.TryGetValue("Repeat", out var repeat) ? Math.Max(1, repeat.IntValue) : 1;
        return Math.Max(0, (int)damage) * hits;
    }

    /// Attacks, and skills/powers that give no block (debuffs, draw, buffs…). Status, curses and other block
    /// cards are not an alternative to a block card.
    private static bool IsUseful(CardModel card) =>
        card.Type == CardType.Attack || (card.Type is CardType.Skill or CardType.Power && !card.GainsBlock);

    /// The reason shown on wasted block: useful cards left in hand, block at the end of the turn and the attack
    /// damage the enemies announced against this player.
    private static BlockChoice Explain(Play play, CardModel alternative, PlayerCombatState state)
    {
        var creature = play.Owner.Creature;
        var useful = state.Hand.Cards.Where(IsUseful).Select(c => $"Card:{Capture.IdOf(c)}").ToList();
        return new BlockChoice($"Card:{Capture.IdOf(play.Card)}", $"Card:{Capture.IdOf(alternative)}", useful,
            creature.Block, IncomingDamage(creature));
    }

    /// Attack damage the enemies announced against this player.
    internal static int IncomingDamage(Creature creature)
    {
        var incoming = 0;
        try
        {
            foreach (var enemy in creature.CombatState?.Enemies ?? Array.Empty<Creature>())
                if (enemy.IsAlive && enemy.Monster != null)
                    incoming += enemy.Monster.NextMove.Intents.OfType<AttackIntent>()
                        .Sum(i => i.GetTotalDamage(new[] { creature }, enemy));
        }
        catch (Exception e)
        {
            ModLog.Error("incoming damage", e);
        }
        return incoming;
    }

    /// Thorns damage the player would take by playing this attack: every enemy hit for an attack on all enemies,
    /// else the enemy with the least Thorns; once per hit.
    private static decimal ThornsDamage(CardModel card, Creature player)
    {
        if (card.Type != CardType.Attack) return 0;
        var enemies = player.CombatState?.HittableEnemies ?? Array.Empty<Creature>();
        if (enemies.Count == 0) return 0;
        var thorns = enemies.Select(e => e.GetPowerAmount<ThornsPower>()).ToList();
        var perHit = card.TargetType == TargetType.AllEnemies ? thorns.Sum() : thorns.Min();
        var hits = card.DynamicVars.TryGetValue("Repeat", out var repeat) ? Math.Max(1, repeat.IntValue) : 1;
        return perHit * hits;
    }

    private static int Cost(CardModel card) => Math.Max(0, card.EnergyCost.GetWithModifiers(CostModifiers.All));

    private static bool PlayableExceptEnergy(CardModel card)
    {
        card.CanPlay(out var reason, out _);
        return (reason & ~(UnplayableReason.EnergyCostTooHigh | UnplayableReason.StarCostTooHigh)) == UnplayableReason.None;
    }
}
