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
        /// The whole hand when the card was played (the card itself left out).
        public required List<CardModel> HandAtPlay { get; init; }
        /// Raw damage (HP + block) of each hit this card dealt to an enemy, and the enemy's Slow (cards played before the
        /// hit this turn, each +10% damage; -1 without Slow).
        public List<(Creature Target, int Raw, int Slow)> Hits { get; } = new();
        /// Number of times this play gave block (Dexterity counts once per gain).
        public int BlockGains { get; set; }
    }

    /// Damage or block a later card or potion would have added to an earlier play of the turn (wrong order, spec §E0).
    /// Kind "order" / "orderSlow": Extra damage on Enemy. Kind "orderBlock": Extra block.
    private sealed record OrderLoss(Play Early, string Boost, string Kind, Creature? Enemy, double Extra);

    private static readonly ConditionalWeakTable<CardPlay, Play> Plays = new();
    private static readonly List<Play> ThisTurn = new();
    private static readonly Stack<Play?> InProgress = new();
    private static int _nextId;
    /// Players whose front hand was already checked this turn, and the lethal combination found (if any).
    /// Targets: the enemies that had to die (a lethal turn is missed only if one of them is still alive).
    private static readonly Dictionary<Player, (Lethal.Attack[] Attacks, List<Creature> Targets)?> FrontHand = new();
    /// Cards each player played this turn (auto-plays included, as relics count them), by type.
    private static readonly Dictionary<Player, Dictionary<CardType, int>> TurnPlays = new();
    /// Enemies given their first Vulnerable this turn, and the play that gave it (useless until someone hits them).
    private static readonly Dictionary<Creature, Play> PendingVulnerable = new();
    /// Growing cards that could be played at the end of some turn of this fight, and those that were played.
    private static readonly HashSet<CardModel> GrowingAffordable = new(ReferenceEqualityComparer.Instance);
    private static readonly HashSet<CardModel> GrowingPlayed = new(ReferenceEqualityComparer.Instance);
    private static readonly List<OrderLoss> OrderLosses = new();
    /// Cards the player chose this turn that lowered their own Strength (Amitié: −2 Strength for +1 energy), and by how much.
    private static readonly List<(Play Play, double Loss)> StrengthDrops = new();

    public static void Reset()
    {
        OrderLosses.Clear();
        StrengthDrops.Clear();
        ThisTurn.Clear();
        InProgress.Clear();
        FrontHand.Clear();
        TurnPlays.Clear();
        PendingVulnerable.Clear();
        GrowingAffordable.Clear();
        GrowingPlayed.Clear();
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
        Capture.Safe("wrong order", () => CheckOrder(power, amount, stacked));
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

    /// Wrong order (spec §E0): Vulnerable put on an enemy that had none, Strength or Dexterity gained, by a card the
    /// player chose or a potion, after plays of the same turn it would have boosted (and while it was already in hand).
    private static void CheckOrder(PowerModel power, decimal amount, bool stacked)
    {
        // A card lowering its player's own Strength (Amitié): attacks played after it, that were already in hand, lose
        // that much per hit (v0.17.15); checked when they hit (OnHit).
        if (amount < 0 && power is StrengthPower && InProgress.TryPeek(out var dropping) && dropping != null &&
            power.Owner == dropping.Owner.Creature && Capture.CallingModelType() is { } dropCaller && typeof(CardModel).IsAssignableFrom(dropCaller))
        {
            StrengthDrops.Add((dropping, (double)-amount));
            return;
        }
        if (amount <= 0 || power is not (VulnerablePower or StrengthPower or DexterityPower)) return;
        InProgress.TryPeek(out var play);
        Player? player;
        string boost;
        if (play != null)
        {
            player = play.Owner;
            boost = Key(play.Card);
        }
        else if (Capture.PotionUser != null && Capture.CardPlayers.Count == 0 && Capture.PotionId != "")
        {
            player = Capture.PotionUser;
            boost = Capture.PotionId;
        }
        else return;
        // Only the card or potion's own effect: a relic or power reacting to the play (Shuriken…) does not depend on the order.
        var caller = Capture.CallingModelType();
        if (caller == null || !(typeof(CardModel).IsAssignableFrom(caller) || typeof(PotionModel).IsAssignableFrom(caller) ||
                                typeof(TemporaryStrengthPower).IsAssignableFrom(caller) || typeof(TemporaryDexterityPower).IsAssignableFrom(caller)))
            return;
        var earlier = ThisTurn.TakeWhile(p => p != play)
            .Where(p => p.Owner == player && (play == null || p.HandAtPlay.Contains(play.Card))).ToList();
        switch (power)
        {
            case VulnerablePower when power.Owner.IsEnemy && !stacked:
                foreach (var e in earlier.Where(p => p.Card.Type == CardType.Attack && !p.HadNonDamageEffect))
                {
                    var raw = e.Hits.Where(h => h.Target == power.Owner).Sum(h => h.Raw);
                    if (raw > 0) OrderLosses.Add(new OrderLoss(e, boost, "order", power.Owner, raw * 0.5));
                }
                break;
            case StrengthPower when power.Owner == player.Creature:
                foreach (var e in earlier.Where(p => p.Card.Type == CardType.Attack && !p.HadNonDamageEffect))
                    foreach (var g in e.Hits.GroupBy(h => h.Target))
                        OrderLosses.Add(new OrderLoss(e, boost, "order", g.Key, (double)amount * g.Count()));
                break;
            case DexterityPower when power.Owner == player.Creature:
                foreach (var e in earlier.Where(p => p.GaveBlock && p.BlockGains > 0 && !p.HadOtherEffect))
                    OrderLosses.Add(new OrderLoss(e, boost, "orderBlock", null, (double)amount * e.BlockGains));
                break;
        }
    }

    /// A hit of the card play in progress on an enemy (for the wrong-order check).
    public static void OnHit(CardModel card, Creature target, int raw, Creature? dealer = null)
    {
        if (raw <= 0 || !target.IsEnemy || !InProgress.TryPeek(out var play) || play == null || play.Card != card) return;
        var slow = target.GetPower<SlowPower>() is { } power ? (int)power.DynamicVars["SlowAmount"].BaseValue : -1;
        play.Hits.Add((target, raw, slow));
        // Hit by the player after a Strength drop it could have been played before (Osty does not use Strength).
        if (dealer != play.Owner.Creature) return;
        foreach (var (drop, loss) in StrengthDrops.Where(d => d.Play.Owner == play.Owner && d.Play.HandAtPlay.Contains(card)))
            OrderLosses.Add(new OrderLoss(drop, Key(card), "order", target, Math.Min(loss, raw)));
    }

    /// Slow (Bygone Effigy): each card played this turn makes the enemy take 10% more attack damage. A non-attack card
    /// played after an attack that hit it, while it was already in hand, would have boosted that attack (spec §E0,
    /// wrong order, v0.17.0).
    private static void CheckSlowOrder(Play late)
    {
        if (late.Card.Type == CardType.Attack) return;
        foreach (var early in ThisTurn.Where(p => p != late && p.Owner == late.Owner && p.Card.Type == CardType.Attack &&
                                                  !p.HadNonDamageEffect && p.HandAtPlay.Contains(late.Card)))
            foreach (var (target, raw, slow) in early.Hits.Where(h => h.Slow >= 0))
                OrderLosses.Add(new OrderLoss(early, Key(late.Card), "orderSlow", target, raw * 0.1 / (1 + 0.1 * slow)));
    }

    /// End of the turn: damage is only lost on enemies still alive (up to their HP + block), block only when it was
    /// below the incoming attack (up to the difference). One miss per (earlier play, boosting card or potion).
    private static void ResolveOrder()
    {
        var deficit = new Dictionary<Player, int>();
        foreach (var g in OrderLosses.GroupBy(o => (o.Early, o.Boost, o.Kind)))
        {
            var (early, boost, kind) = g.Key;
            double lost;
            if (kind is "order" or "orderSlow")
                lost = g.GroupBy(o => o.Enemy!).Sum(e => e.Key.IsAlive ? Math.Min(e.Sum(o => o.Extra), e.Key.CurrentHp + e.Key.Block) : 0);
            else
            {
                var creature = early.Owner.Creature;
                if (!deficit.TryGetValue(early.Owner, out var left)) left = Math.Max(0, IncomingDamage(creature) - creature.Block);
                lost = Math.Min(g.Sum(o => o.Extra), left);
                deficit[early.Owner] = left - (int)lost;
            }
            var amount = (int)Math.Round(lost);
            if (amount < 1) continue;
            ModLog.Info($"wrong order: {Capture.IdOf(early.Card)} before {boost}, {kind} {amount}");
            Capture.Emit(new MissedChance(Capture.ToActor(early.Owner), kind, new[] { Key(early.Card), boost, N(amount) }));
        }
        OrderLosses.Clear();
        StrengthDrops.Clear();
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
        if (play == null) return;
        play.BlockAmount += amount;
        play.BlockGains++;
    }

    /// Another card discarded, exhausted or moved by the play in progress (Survivor, Graveblast bringing a card back to
    /// the hand…). The played card itself going to its pile is not an effect. Not damage: an attack doing this was a real
    /// choice, never a weak attack (v0.17.1).
    public static void OnCardEffect(CardModel card)
    {
        if (!InProgress.TryPeek(out var play) || play == null || play.Card == card) return;
        play.HadOtherEffect = true;
        play.HadNonDamageEffect = true;
    }

    public static void OnPlayFinished()
    {
        if (InProgress.Count > 0) InProgress.Pop();
    }

    /// Card play starts: remember the useful cards in the player's hand. Auto-played cards were not a choice.
    public static void OnPlayStarted(CardPlay cardPlay)
    {
        if (IsGrowing(cardPlay.Card)) GrowingPlayed.Add(cardPlay.Card);
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
            HadOtherEffect = IsGrowing(cardPlay.Card) || cardPlay.Card is Sacrifice,
            HadNonDamageEffect = IsGrowing(cardPlay.Card),
            UsefulCardsInHand = hand.Where(c => c != cardPlay.Card && IsUseful(c)).ToList(),
            HandAtPlay = hand.Where(c => c != cardPlay.Card).ToList()
        };
        var energyBefore = (cardPlay.Player.PlayerCombatState?.Energy ?? 0) + cardPlay.Resources.EnergySpent;
        if (cardPlay.Card.Type == CardType.Attack && cardPlay.Target is { IsEnemy: true } target)
        {
            if (IsDoubledByPenNib(cardPlay)) play.PenNibBetter = StrongerAttack(cardPlay.Card, target, hand, energyBefore, doubled: true);
            else play.Stronger = StrongerAttack(cardPlay.Card, target, hand, Cost(cardPlay.Card), doubled: false);
        }
        if (cardPlay.Card.GainsBlock) play.BiggerBlock = BiggerBlock(cardPlay.Card, hand);
        Capture.Safe("wrong order (Slow)", () => CheckSlowOrder(play));
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

    /// End of the fight: a growing card that could have been played but never was, and is still in the deck, did not
    /// grow (spec §E0, counted once per card and fight).
    public static void ResolveCombat()
    {
        foreach (var card in GrowingAffordable.Where(c => !GrowingPlayed.Contains(c) && c.Pile?.Type is PileType.Hand or PileType.Draw or PileType.Discard))
            Capture.Emit(new MissedChance(Capture.ToActor(card.Owner), "growth", new[] { Key(card) }));
        GrowingAffordable.Clear();
        GrowingPlayed.Clear();
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
                !used.Contains(c) && state.Hand.Cards.Contains(c) && !KeptOnPurpose(c, combat) && Cost(c) <= budget && PlayableExceptEnergy(c) &&
                ThornsDamage(c, creature) <= Math.Max(0, surplus) && !PunishedAttack(c));
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
        Capture.Safe("wrong order", ResolveOrder);
        var enemies = RealEnemies(combat);
        if (enemies.Count > 0)
            foreach (var (player, front) in FrontHand)
                // The enemies the front hand could kill must still be alive: killing them can bring new ones (Parasite
                // Krapo's larvae), which the front hand could not see (v0.17.3).
                if (front is { } lethal && lethal.Targets.Any(t => t.IsAlive && t.HpDisplay == HpDisplay.Normal))
                {
                    ModLog.Info($"missed lethal: {player.NetId} [{string.Join(", ", lethal.Attacks.Select(a => a.Id))}]");
                    Capture.Emit(new MissedLethal(Capture.ToActor(player), lethal.Attacks.Select(a => a.Id).ToList(),
                        enemies.Sum(e => e.CurrentHp + e.Block)));
                }
        FrontHand.Clear();

        foreach (var play in ThisTurn)
        {
            var hand = play.Owner.PlayerCombatState?.Hand.Cards;
            if (hand == null) continue;
            var actor = Capture.ToActor(play.Owner);
            if (play.Stronger is { } better && !play.HadNonDamageEffect && hand.Contains(better.Card) && !KeptOnPurpose(better.Card, combat))
                Capture.Emit(new WeakAttack(actor, Key(play.Card), better.Played, Key(better.Card), better.Damage));
            // Pen Nib doubled a weak attack: counted even if the stronger one was played later (it was not doubled).
            if (play.PenNibBetter is { } nib && !play.HadNonDamageEffect)
                Capture.Emit(new MissedChance(actor, "penNib", new[] { Key(play.Card), N(nib.Played), Key(nib.Card), N(nib.Damage) }));
            // A smaller block card, while the bigger one stayed in hand and the block was not enough for the attack.
            if (play.BiggerBlock is { } bigger && !play.HadOtherEffect && hand.Contains(bigger.Card) && !KeptOnPurpose(bigger.Card, combat) &&
                play.Owner.Creature.Block < IncomingDamage(play.Owner.Creature))
                Capture.Emit(new MissedChance(actor, "smallBlock", new[] { Key(play.Card), N(bigger.Played), Key(bigger.Card), N(bigger.Block) }));
        }

        foreach (var player in combat.Players.Where(p => p.Creature.IsAlive))
            Capture.Safe("end of turn checks", () => EndOfTurnChecks(combat, player));
        TurnPlays.Clear();
        PendingVulnerable.Clear();
        GrowingAffordable.Clear();
        GrowingPlayed.Clear();
    }

    /// Enemies that can really be hurt: an enemy shown with infinite HP (Waterfall Giant about to explode) is not one
    /// left to kill (spec §E0, invulnerable phase).
    private static List<Creature> RealEnemies(ICombatState? combat) =>
        (combat?.HittableEnemies ?? Array.Empty<Creature>()).Where(e => e.HpDisplay == HpDisplay.Normal).ToList();

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
        // Only an invulnerable enemy left (Waterfall Giant about to explode): nothing to spend energy on.
        var nothingToKill = RealEnemies(combat).Count == 0;

        // Unused energy: lost at the end of the turn (not kept by Ice Cream & co., not wanted by Pael's Tears).
        // Cards kept on purpose (Retain, Star cost) are left out: not playing them is a choice (spec §E0, v0.17.0).
        // So is a block card when the block already covers the incoming attack (v0.17.1).
        var blockCovered = player.Creature.Block >= IncomingDamage(player.Creature);
        // An Exhaust card (Feu follet…) is gone once played: keeping it for later is a choice (v0.17.18).
        bool WouldHelp(CardModel c) => Affordable(c) && !KeptOnPurpose(c, combat) && !(blockCovered && c.GainsBlock && c.Type == CardType.Skill) &&
                                       !c.Keywords.Contains(CardKeyword.Exhaust) && !PunishedAttack(c);
        if (energy > 0 && !nothingToKill && hand.Any(WouldHelp) && Hook.ShouldPlayerResetEnergy(combat, player) &&
            player.GetRelic<PaelsTears>() == null)
        {
            // Args: the energy, then the cards that energy could have paid (most expensive first, at most 3).
            var playable = hand.Where(WouldHelp).GroupBy(c => Capture.IdOf(c)).Select(g => g.First())
                .OrderByDescending(c => c.EnergyCost.CostsX ? energy : Cost(c)).Take(3).Select(Key);
            Capture.Emit(new MissedChance(actor, "energy", new[] { N(energy) }.Concat(playable).ToArray(), energy));
        }

        // Pael's Tears gives energy next turn for energy left: energy spent on a useless card (at any time in the turn)
        // would have been left.
        var uselessPlays = UselessPlays(player);
        if (energy == 0 && player.GetRelic<PaelsTears>() is { } tears && uselessPlays.FirstOrDefault() is { } useless)
            Capture.Emit(new MissedChance(actor, "paelsTears", new[] { $"Relic:{Capture.IdOf(tears)}", Key(useless.Card) }));

        // A growing card could have been played this turn with the energy left, or instead of a useless card (v0.17.7).
        var spareEnergy = energy + uselessPlays.Select(p => p.EnergySpent).DefaultIfEmpty(0).Max();
        foreach (var growing in hand.Where(c => IsGrowing(c) && !IsStatusOrCurse(c) && PlayableExceptEnergy(c) && !c.EnergyCost.CostsX &&
                                                Cost(c) <= spareEnergy))
            GrowingAffordable.Add(growing);

        // Block for one ally (Lift…) does not protect the player: only the ally check below.
        var kept = hand.Where(c => c.GainsBlock && Affordable(c) && (c.TargetType != TargetType.AnyAlly || c is Mimic)).Select(c => (Card: c, Block: EstimateBlock(c)))
            .Where(x => x.Block > 0).OrderByDescending(x => x.Block).FirstOrDefault();
        if (kept.Card != null) Capture.Emit(new BlockCardKept(actor, Key(kept.Card), kept.Block));

        // A block card for allies (Lift, Rally…) kept in hand while an ally is alive: their HP lost may have been avoidable.
        if (combat.Players.Any(p => p != player && p.Creature.IsAlive))
        {
            var forAllies = hand.Where(c => c.GainsBlock && c.TargetType is TargetType.AnyAlly or TargetType.AllAllies && c is not Mimic && Affordable(c))
                .Select(c => (Card: c, Block: EstimateBlock(c))).Where(x => x.Block > 0).OrderByDescending(x => x.Block).FirstOrDefault();
            if (forAllies.Card != null)
                Capture.Emit(new AllyBlockCardKept(actor, Key(forAllies.Card), forAllies.Block, forAllies.Card.TargetType == TargetType.AllAllies));
        }

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
            if (IsStatusOrCurse(c) || !PlayableExceptEnergy(c) || KeptOnPurpose(c, combat)) return false;
            if (!free && (!energyLost || !Affordable(c))) return false;
            if (c.Type == CardType.Attack && (ThornsDamage(c, creature) > Math.Max(0, surplus) || !DealsDamage(c, combat) || PunishedAttack(c))) return false;
            if (blockNeeded && !c.GainsBlock && !free && Cost(c) + blockCards.Min(Cost) > energy) return false;
            return true;
        }
        foreach (var relic in nothingToKill ? Enumerable.Empty<RelicModel>() : player.Relics)
        {
            if (MissedRelicTrigger(relic, plays, hand, energy, WorthPlaying))
                Capture.Emit(new MissedChance(actor, "relic", new[] { $"Relic:{Capture.IdOf(relic)}" }));
        }
    }

    /// Cards the player paid energy for this turn that did nothing useful: a block card with no other effect whose block
    /// was not needed (block at the end of the turn minus its block still covers the incoming attack), or a card whose
    /// only effect was a useless debuff (F8).
    private static List<Play> UselessPlays(Player player)
    {
        var incoming = IncomingDamage(player.Creature);
        return ThisTurn.Where(p => p.Owner == player && p.EnergySpent > 0 && !p.HadOtherEffect &&
            ((p.GaveBlock && !p.Ethereal && player.Creature.Block - p.BlockAmount >= incoming) || (p.UselessDebuff && !p.GaveBlock))).ToList();
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

    /// Cards that grow each time they are played (card audit, spec §E0): playing them is never a waste, and not playing
    /// them when affordable is a missed opportunity.
    internal static bool IsGrowing(CardModel c) =>
        c is GeneticAlgorithm or TheScythe or TheBall or Claw or Maul or Rampage;

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
    private static (Lethal.Attack[] Attacks, List<Creature> Targets)? FindLethal(Player player, List<CardModel> hand, int energy)
    {
        try
        {
            var all = RealEnemies(player.Creature.CombatState);
            var enemies = all.Where(e => !e.IsSecondaryEnemy).ToList();
            if (enemies.Count == 0) enemies = all.ToList();
            if (enemies.Count == 0) return null;
            var attacks = hand.Where(c => c.Type == CardType.Attack && !c.EnergyCost.CostsX && PlayableExceptEnergy(c) && !LacksStars(c) &&
                                          c.TargetType is TargetType.AnyEnemy or TargetType.AllEnemies)
                .Select(c => new Lethal.Attack($"Card:{Capture.IdOf(c)}", Cost(c), c.TargetType == TargetType.AllEnemies,
                    enemies.Select(e => EstimateDamage(c, e)).ToList()))
                .ToList();
            var found = Lethal.Find(attacks, energy, enemies.Select(e => e.CurrentHp + e.Block).ToList());
            return found == null ? null : (found.ToArray(), enemies);
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
            // Cards whose number of hits the mod cannot know (Whirlwind, Fiend Fire…) are never compared.
            if (HitCount(played, target) == null) return null;
            var damage = EstimateDamage(played, target);
            if (damage <= 0 || damage >= target.CurrentHp + target.Block) return null;
            var factor = doubled ? 2 : 1;
            var best = hand.Where(c => c != played && c.Type == CardType.Attack && !c.EnergyCost.CostsX && Cost(c) <= budget &&
                                       c.TargetType is TargetType.AnyEnemy or TargetType.AllEnemies && PlayableExceptEnergy(c) &&
                                       HitCount(c, target) != null)
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

    /// Cards that always hit twice without saying it in a variable.
    private static readonly HashSet<string> TwoHits = new() { "ASTRAL_PULSE", "MAUL", "DAGGER_SPRAY", "TWIN_STRIKE", "UPROAR", "RIP_AND_TEAR", "REFRACT", "THRASH" };

    /// Cards whose number of hits depends on things the mod does not compute (energy spent, cards in piles…).
    private static readonly HashSet<string> UnknownHits = new()
        { "SPITE", "ERADICATE", "FLAK_CANNON", "FIEND_FIRE", "HEAVENLY_DRILL", "MAD_SCIENCE", "SKEWER", "STARDUST", "VOLLEY", "WHIRLWIND" };

    /// Number of hits of an attack on this target (card audit of every "hit count" in the game), null when unknown.
    /// "CalculatedHits" is the game's own count (En miettes: 1 + times the player lost HP this fight; Barrage…).
    private static int? HitCount(CardModel card, Creature? target)
    {
        var id = card.Id.Entry;
        if (UnknownHits.Contains(id)) return null;
        if (card.DynamicVars.TryGetValue("CalculatedHits", out var calc) && calc is CalculatedVar cv) return Math.Max(1, (int)cv.Calculate(target));
        if (card.DynamicVars.TryGetValue("Repeat", out var repeat)) return Math.Max(1, repeat.IntValue);
        if (TwoHits.Contains(id)) return 2;
        if (card is Dismantle) return target?.HasPower<VulnerablePower>() == true ? 2 : 1;
        return 1;
    }

    /// The game's own damage computation for a card against one enemy (Strength, Vulnerable, Weak…) × its hits
    /// (1 when unknown: the result stays a minimum). 0 for cards whose damage is computed in another way.
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
        return Math.Max(0, (int)damage) * (HitCount(card, target) ?? 1);
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
        return perHit * (HitCount(card, null) ?? 1);
    }

    /// A card the player may keep on purpose, so not playing it is never a misplay (spec §E0, v0.17.0): it is retained
    /// (Retain, or the whole hand is kept: Main retenue…), or it costs Stars (they are not refilled each turn).
    private static bool KeptOnPurpose(CardModel card, ICombatState combat) =>
        card.ShouldRetainThisTurn || card.CurrentStarCost > 0 || card.HasStarCostX || !Hook.ShouldFlush(combat, card.Owner);

    /// The player has not enough Stars for this card.
    private static bool LacksStars(CardModel card)
    {
        card.CanPlay(out var reason, out _);
        return (reason & UnplayableReason.StarCostTooHigh) != UnplayableReason.None;
    }

    /// An attack the enemies punish like Thorns (v0.17.20): Personal Hive (Apicultueur) adds a Dazed to the deck per hit.
    /// Single target: punished only if every enemy it could hit punishes and it would kill none of them; all enemies:
    /// punished if any of them punishes (unless it kills every punishing one). Not playing it is then a choice.
    private static bool PunishedAttack(CardModel card)
    {
        if (card.Type != CardType.Attack || card.CombatState is not { } cs) return false;
        var enemies = cs.HittableEnemies.Where(e => e.HpDisplay == HpDisplay.Normal).ToList();
        if (enemies.Count == 0) return false;
        bool Punishes(Creature e) => e.HasPower<PersonalHivePower>() && EstimateDamage(card, e) < e.CurrentHp + e.Block;
        return card.TargetType == TargetType.AllEnemies ? enemies.Any(Punishes) : enemies.All(Punishes);
    }

    private static int Cost(CardModel card) => Math.Max(0, card.EnergyCost.GetWithModifiers(CostModifiers.All));

    private static bool PlayableExceptEnergy(CardModel card)
    {
        card.CanPlay(out var reason, out _);
        return (reason & ~(UnplayableReason.EnergyCostTooHigh | UnplayableReason.StarCostTooHigh)) == UnplayableReason.None;
    }
}
