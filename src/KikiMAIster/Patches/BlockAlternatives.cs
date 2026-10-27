using System.Runtime.CompilerServices;
using KikiMAIster.Core;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
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
        public required bool Ethereal { get; init; }
        /// Attack played: a stronger attack in hand on the same target (same cost or cheaper), with both damages.
        public (CardModel Card, int Damage, int Played)? Stronger { get; set; }
    }

    private static readonly ConditionalWeakTable<CardPlay, Play> Plays = new();
    private static readonly List<Play> ThisTurn = new();
    private static readonly Stack<Play?> InProgress = new();
    private static int _nextId;
    /// Players whose front hand was already checked this turn, and the lethal combination found (if any).
    private static readonly Dictionary<Player, Lethal.Attack[]?> FrontHand = new();

    public static void Reset()
    {
        ThisTurn.Clear();
        InProgress.Clear();
        FrontHand.Clear();
    }

    /// Something other than block happened (power applied, damage, heal, draw, orb, card created…):
    /// credited to the card play in progress, if any.
    public static void OnEffect(bool damage = false)
    {
        if (!InProgress.TryPeek(out var play) || play == null) return;
        play.HadOtherEffect = true;
        if (!damage) play.HadNonDamageEffect = true;
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
            UsefulCardsInHand = hand.Where(c => c != cardPlay.Card && IsUseful(c)).ToList()
        };
        if (cardPlay.Card.Type == CardType.Attack && cardPlay.Target is { IsEnemy: true } target)
            play.Stronger = StrongerAttack(cardPlay.Card, target, hand);
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
            if (play.Stronger is not { } better || play.HadNonDamageEffect) continue;
            if (play.Owner.PlayerCombatState?.Hand.Cards.Contains(better.Card) != true) continue;
            Capture.Emit(new WeakAttack(Capture.ToActor(play.Owner), $"Card:{Capture.IdOf(play.Card)}", better.Played,
                $"Card:{Capture.IdOf(better.Card)}", better.Damage));
        }
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
    private static (CardModel, int, int)? StrongerAttack(CardModel played, Creature target, IEnumerable<CardModel> hand)
    {
        try
        {
            var damage = EstimateDamage(played, target);
            if (damage <= 0 || damage >= target.CurrentHp + target.Block) return null;
            var cost = Cost(played);
            var best = hand.Where(c => c != played && c.Type == CardType.Attack && !c.EnergyCost.CostsX && Cost(c) <= cost &&
                                       c.TargetType is TargetType.AnyEnemy or TargetType.AllEnemies && PlayableExceptEnergy(c))
                .Select(c => (Card: c, Damage: EstimateDamage(c, target)))
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
