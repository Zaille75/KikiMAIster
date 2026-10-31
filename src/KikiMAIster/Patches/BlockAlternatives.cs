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
using MegaCrit.Sts2.Core.Models.Afflictions;
using MegaCrit.Sts2.Core.Models.Potions;

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
    /// A card play of the turn: what the rules need (Core TurnPlay, v0.17.41) plus the game objects to follow it.
    /// Stronger: a stronger attack in hand on the same target (same cost or cheaper). PenNibBetter: attack doubled by Pen Nib
    /// while a stronger affordable attack was in hand (both shown doubled). BiggerBlock: a block card in hand giving more
    /// block (same cost or cheaper). UselessDebuff: 1 Weak on an enemy that does not attack this turn, or 1 Vulnerable on
    /// an enemy with none that nobody hits before the end of the turn (spec §F, F8).
    private sealed class Play : TurnPlay
    {
        public required Player Player { get; init; }
        public CardModel Card => (CardModel)CardRef;
        /// Orbs this play triggered (evoke or passive) that Focus boosts, for the wrong-order check of a later Focus
        /// (v0.17.32): Lightning / Glass hits ("order", with their enemy), Frost block ("orderBlock"), Dark charging
        /// ("orderDark": its stored damage). Plasma gives energy, which Focus does not change.
        public List<(Creature? Enemy, string Kind)> OrbTriggers { get; } = new();
        /// Hits the player's pet (Titos) made during this play: Calcification boosts them, Strength does not (v0.18.5).
        public List<Creature> PetHits { get; } = new();
        /// An effect other than damage, block or a debuff on an enemy (draw, energy, own buff…): it may have made a later card
        /// playable, so its place in the turn was a choice (wrong order, v0.18.5: a debuff on the enemy no longer counts).
        public bool OtherThanDebuff { get; set; }
        /// Enemies it put Doom on, and whether it put another debuff on an enemy (Doom-only cards, v0.18.6).
        public List<Creature> DoomTargets { get; } = new();
        public bool OtherDebuff { get; set; }
    }

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
    /// Cards the player chose this turn that lowered their own Strength (Amitié: −2 Strength for +1 energy, or Décomposition
    /// after a card, v0.18.5), and by how much; the same for Dexterity (Décomposition).
    private static readonly List<(Play Play, double Loss)> StrengthDrops = new();
    private static readonly List<(Play Play, double Loss)> DexterityDrops = new();
    /// The first play of each card this turn: a card played again (Déflagration, Duplication, Forme résonnante…) is the
    /// same choice, its copies add their hits and block to it (v0.17.38).
    private static readonly Dictionary<CardModel, Play> FirstPlays = new(ReferenceEqualityComparer.Instance);
    /// Players who take an extra turn right after this one (Ambre gris, Œil de Paël, v0.17.38): no enemy attacks before
    /// their block is cleared.
    private static readonly HashSet<Player> ExtraTurn = new();
    /// Players whose own card killed their pet this turn (Détonation osseuse, v0.17.45).
    private static readonly HashSet<Player> PetSacrificed = new();
    /// Ruée's end-of-turn plays that found no attack in hand, per player (v0.18.6).
    private static readonly Dictionary<Player, int> StampedeShort = new();
    /// The attacks Ruée played for free when it had a play for each of them (v0.18.7).
    private static readonly Dictionary<Player, List<CardModel>> StampedeAll = new();

    public static void Reset()
    {
        ClearTurn();
        InProgress.Clear();
        GrowingAffordable.Clear();
        GrowingPlayed.Clear();
    }

    /// The per-turn state, cleared in one place at the end of each players' turn (and at a fight's start).
    private static void ClearTurn()
    {
        OrderLosses.Clear();
        StrengthDrops.Clear();
        DexterityDrops.Clear();
        ThisTurn.Clear();
        FirstPlays.Clear();
        ExtraTurn.Clear();
        FrontHand.Clear();
        _justFinished = null;
        TurnPlays.Clear();
        PendingVulnerable.Clear();
        PetSacrificed.Clear();
        StampedeShort.Clear();
        StampedeAll.Clear();
    }

    /// A damage cap stopped part of a hit of the player's card play in progress (v0.18.7).
    public static void OnCapped(Creature dealer)
    {
        if (InProgress.TryPeek(out var play) && play != null && play.Player.Creature == dealer) play.Capped = true;
    }

    /// A card drawn during the player's own card play (not the turn's draw, v0.18.7).
    public static void OnCardDrawn(Player owner, bool fromHandDraw)
    {
        if (!fromHandDraw && InProgress.TryPeek(out var play) && play != null && play.Player == owner) play.CardsDrawn++;
    }

    /// Ruée is about to play attacks from the hand (v0.18.6): how many of its plays will find none.
    public static void OnStampede(Player player, int amount)
    {
        var inHand = player.PlayerCombatState?.Hand.Cards.Where(c => c.Type == CardType.Attack && !c.Keywords.Contains(CardKeyword.Unplayable)).ToList()
                     ?? new List<CardModel>();
        var attacks = inHand.Count;
        if (amount > attacks) StampedeShort[player] = StampedeShort.GetValueOrDefault(player) + amount - attacks;
        // Every attack in hand will be played: which one the player kept did not depend on luck.
        if (amount >= attacks && attacks > 0)
        {
            if (!StampedeAll.TryGetValue(player, out var all)) StampedeAll[player] = all = new();
            all.AddRange(inHand);
        }
    }

    /// A potion used in a fight when it could do nothing (v0.18.6, Zaille75: Goutte de folie with no card that costs energy in
    /// hand). Only the cases sure from the game state: heal at full HP, draw with no room or nothing to draw or under No Draw,
    /// a card to pick from an empty or unsuitable pile, nothing to double or upgrade, no orb slot.
    public static bool UselessPotion(PotionModel potion, Creature? target)
    {
        var player = (target ?? potion.Owner?.Creature)?.Player ?? potion.Owner;
        if (player?.PlayerCombatState is not { } st || player.Creature is not { } creature) return false;
        var hand = st.Hand.Cards;
        bool NothingToDraw() => creature.HasPower<NoDrawPower>() || hand.Count >= CardPile.MaxCardsInHand ||
                                st.DrawPile.Cards.Count + st.DiscardPile.Cards.Count == 0;
        return potion switch
        {
            BloodPotion => creature.CurrentHp >= creature.MaxHp,
            SwiftPotion => NothingToDraw(),
            TouchOfInsanity => !hand.Any(c => c.CostsEnergyOrStars(includeGlobalModifiers: false) || c.CostsEnergyOrStars(includeGlobalModifiers: true)),
            BlessingOfTheForge => !hand.Any(c => c.IsUpgradable),
            Fortifier => creature.Block <= 0,
            SoldiersStew => !st.AllCards.Any(c => c.Tags.Contains(CardTag.Strike)),
            LiquidMemories => st.DiscardPile.Cards.Count == 0,
            DropletOfPrecognition => st.DrawPile.Cards.Count == 0,
            DistilledChaos => st.DrawPile.Cards.Count + st.DiscardPile.Cards.Count == 0,
            EssenceOfDarkness => st.OrbQueue.Capacity == 0,
            GamblersBrew or Ashwater => hand.Count == 0,
            _ => false
        };
    }

    /// A draw was stopped (Pas de pioche) during the card play in progress (v0.18.6).
    public static void OnDrawPrevented()
    {
        if (InProgress.TryPeek(out var play) && play != null) play.DrawBlocked = true;
    }

    /// Energy the card play in progress gave its own player (Offrande…, v0.18.6).
    public static void OnEnergyGiven(Player player, decimal amount)
    {
        if (InProgress.TryPeek(out var play) && play != null && play.Player == player) play.EnergyGiven += (int)amount;
    }

    /// Cards created into a player's hand: those beyond the hand's limit go to the discard pile (v0.18.6).
    public static void OnCardsCreated(Player owner, int count)
    {
        if (!InProgress.TryPeek(out var play) || play == null || play.Player != owner || owner.PlayerCombatState is not { } st) return;
        var lost = st.Hand.Cards.Count + count - CardPile.MaxCardsInHand;
        if (lost <= 0) return;
        play.LostToFullHand += Math.Min(lost, count);
        foreach (var c in st.Hand.Cards) play.HandWhenFull.Add(c);
    }

    /// The player's own card killed their pet (v0.17.45): an attack the pet makes, left in hand, could have come first.
    public static void OnPetSacrificed(Player player) => PetSacrificed.Add(player);

    /// Something other than block happened (power applied, damage, heal, draw, orb, card created…):
    /// credited to the card play in progress, if any.
    public static void OnEffect(bool damage = false, bool enemyDebuff = false)
    {
        if (!InProgress.TryPeek(out var play) || play == null) return;
        play.HadOtherEffect = true;
        if (damage) return;
        play.HadNonDamageEffect = true;
        if (!enemyDebuff) play.OtherThanDebuff = true;
    }

    /// A power applied during the play in progress: an effect, except 1 Weak on an enemy that does not attack this turn
    /// (it wears off before doing anything).
    /// Stacked: the power was already on its owner (adding turns to it is always an effect).
    public static void OnPowerApplied(PowerModel power, decimal amount, bool stacked)
    {
        Capture.Safe("wrong order", () => CheckOrder(power, amount, stacked));
        // A power the card put on its own player (v0.18.6): later cards it feeds were not in the wrong order.
        if (InProgress.TryPeek(out var giver) && giver != null && amount > 0)
        {
            if (power.Owner == giver.Player.Creature) giver.PowersGiven.Add($"Power:{Capture.IdOf(power)}");
            else if (power is DoomPower && power.Owner.IsEnemy) giver.DoomTargets.Add(power.Owner);
            else if (power.Owner.IsEnemy) giver.OtherDebuff = true;
        }
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
        OnEffect(enemyDebuff: power.Owner.IsEnemy);
    }

    /// Wrong order (spec §E0): a boost (Vulnerable put on an enemy that had none, Strength, Dexterity, Focus, Calcification…)
    /// given by a card the player chose or a potion, after plays of the same turn it would have boosted (and while it was
    /// already in hand); or a Strength / Dexterity drop before plays it lowered.
    private static void CheckOrder(PowerModel power, decimal amount, bool stacked)
    {
        var caller = Capture.CallingModelType();
        if (amount < 0)
        {
            if (power is not (StrengthPower or DexterityPower) || caller == null) return;
            InProgress.TryPeek(out var inProgress);
            // A card lowering its player's own Strength (Amitié): attacks played after it, that were already in hand, lose that
            // much per hit (v0.17.15); checked when they hit (OnHit).
            if (power is StrengthPower && inProgress != null && power.Owner == inProgress.Player.Creature && typeof(CardModel).IsAssignableFrom(caller))
            {
                StrengthDrops.Add((inProgress, (double)-amount));
                return;
            }
            // Décomposition (Chasseur-tueur, v0.18.5): −1 Strength and −1 Dexterity after each card the player plays. Attacks and
            // block cards should come first; a drop after an attack or a block card is not counted (one of them had to go first).
            var dropping = inProgress ?? _justFinished;
            if (caller != typeof(TenderPower) || dropping == null || power.Owner != dropping.Player.Creature ||
                dropping.Card.Type == CardType.Attack || dropping.GaveBlock) return;
            (power is StrengthPower ? StrengthDrops : DexterityDrops).Add((dropping, (double)-amount));
            return;
        }
        InProgress.TryPeek(out var play);
        // Monologue (Strength after each card this turn) and Rupture (Strength when a card costs HP), v0.18.5: the card that
        // triggered it is the boost; an attack triggering it is not counted (one attack had to go first).
        var triggered = power is StrengthPower && (caller == typeof(MonologuePower) || caller == typeof(RupturePower));
        if (triggered)
        {
            play ??= _justFinished;
            if (play == null || play.Card.Type == CardType.Attack) return;
        }
        Player? player;
        string boost;
        if (play != null)
        {
            player = play.Player;
            boost = Key(play.Card);
        }
        else if (Capture.PotionUser != null && Capture.CardPlayers.Count == 0 && Capture.PotionId != "")
        {
            player = Capture.PotionUser;
            boost = Capture.PotionId;
        }
        else return;
        // Only the card or potion's own effect: a relic or power reacting to the play (Shuriken…) does not depend on the order.
        if (!triggered && (caller == null || !(typeof(CardModel).IsAssignableFrom(caller) || typeof(PotionModel).IsAssignableFrom(caller) ||
                                               typeof(TemporaryStrengthPower).IsAssignableFrom(caller) || typeof(TemporaryDexterityPower).IsAssignableFrom(caller))))
            return;
        var earlier = ThisTurn.TakeWhile(p => p != play)
            .Where(p => p.Player == player && (play == null || p.HandAtPlay.Contains(play.Card))).ToList();
        var attacks = earlier.Where(p => p.Card.Type == CardType.Attack && !p.OtherThanDebuff).ToList();
        var blocks = earlier.Where(p => p.GaveBlock && p.BlockGains > 0 && !p.HadOtherEffect).ToList();
        // Damage per hit added to some of the player's own hits (not the pet's) on each enemy, Vulnerable included.
        void PerHit(IEnumerable<Play> plays, double perHit)
        {
            foreach (var e in plays)
                foreach (var g in OwnHits(e).GroupBy(h => (Creature)h.Target))
                    OrderLosses.Add(new OrderLoss(e, boost, "order", g.Key, perHit * g.Count() * Vulnerable(g.Key, player)));
        }
        switch (power)
        {
            case VulnerablePower when power.Owner.IsEnemy && !stacked:
            {
                // The game's multiplier (×1.5, more with some relics, v0.17.38).
                var extra = VulnerableFactor(power, power.Owner, player.Creature) - 1;
                foreach (var e in attacks.Concat(AlliesBefore(play, player)))
                {
                    var raw = e.Hits.Where(h => ReferenceEquals(h.Target, power.Owner)).Sum(h => h.Raw);
                    if (raw > 0) OrderLosses.Add(new OrderLoss(e, boost, "order", power.Owner, raw * extra));
                }
                break;
            }
            case StrengthPower when power.Owner == player.Creature:
                PerHit(attacks, (double)amount);
                break;
            // Focus gained after orbs fired this turn (Potion de focalisation after an evoke): each orb hit lost that much
            // damage, each Frost trigger that much block, each Dark charge that much stored damage (v0.17.32).
            case FocusPower when power.Owner == player.Creature:
                foreach (var e in earlier)
                    foreach (var (enemy, kind) in e.OrbTriggers)
                        OrderLosses.Add(new OrderLoss(e, boost, kind, enemy, (double)amount));
                break;
            case DexterityPower when power.Owner == player.Creature:
                foreach (var e in blocks)
                    OrderLosses.Add(new OrderLoss(e, boost, "orderBlock", null, (double)amount * e.BlockGains));
                break;
            // v0.18.5 (Zaille75): the same with the other boosts. Calcification: each hit of the player's pet (Titos).
            case CalcifyPower when power.Owner == player.Creature:
                foreach (var e in earlier.Where(p => !p.OtherThanDebuff))
                    foreach (var g in e.PetHits.GroupBy(h => h))
                        OrderLosses.Add(new OrderLoss(e, boost, "order", g.Key, (double)amount * g.Count() * Vulnerable(g.Key, player)));
                break;
            // Précision: each Surin hit; Lames fantomatiques: the first Surin of the turn only.
            case AccuracyPower when power.Owner == player.Creature:
                PerHit(attacks.Where(p => p.Card.Tags.Contains(CardTag.Shiv)), (double)amount);
                break;
            case PhantomBladesPower when power.Owner == player.Creature:
                PerHit(ThisTurn.Where(p => p.Player == player && p.Card.Tags.Contains(CardTag.Shiv)).Take(1).Intersect(attacks), (double)amount);
                break;
#if !GAME_DEFAULT
            // Un pour tous (beta only): each hit of an X-cost attack.
            case OneForAllPower when power.Owner == player.Creature:
                PerHit(attacks.Where(p => p.Card.EnergyCost.CostsX), (double)amount);
                break;
#endif
            // Pistage: +50% on enemies with Weak.
            case TrackingPower when power.Owner == player.Creature && !stacked:
                foreach (var e in attacks)
                    foreach (var g in e.Hits.GroupBy(h => (Creature)h.Target).Where(g => g.Key.HasPower<WeakPower>()))
                        OrderLosses.Add(new OrderLoss(e, boost, "order", g.Key, g.Sum(h => h.Raw) * (double)amount / 100));
                break;
            // Bien attaché: each block gain of a Défense; Camouflage: block doubled this turn.
            case FastenPower when power.Owner == player.Creature:
                foreach (var e in blocks.Where(p => p.Card.Tags.Contains(CardTag.Defend)))
                    OrderLosses.Add(new OrderLoss(e, boost, "orderBlock", null, (double)amount * e.BlockGains));
                break;
            case ShadowmeldPower when power.Owner == player.Creature:
                foreach (var e in blocks)
                    OrderLosses.Add(new OrderLoss(e, boost, "orderBlock", null, e.BlockAmount * (Math.Pow(2, (double)amount) - 1)));
                break;
            // Conquérant: Lame souveraine deals double damage to that enemy.
            case ConquerorPower when power.Owner.IsEnemy && !stacked:
                foreach (var e in attacks.Where(p => p.Card is SovereignBlade))
                {
                    var raw = OwnHits(e).Where(h => ReferenceEquals(h.Target, power.Owner)).Sum(h => h.Raw);
                    if (raw > 0) OrderLosses.Add(new OrderLoss(e, boost, "order", power.Owner, raw));
                }
                break;
            // Co-op: Encerclement and Renverser multiply the allies' attack damage on that enemy; an ally's attack made while the
            // card was already in the player's hand should have come after it (the miss is the attacker's, like Vulnérable).
            case FlankingPower or KnockdownPower when power.Owner.IsEnemy && !stacked:
                foreach (var e in AlliesBefore(play, player))
                {
                    var raw = e.Hits.Where(h => ReferenceEquals(h.Target, power.Owner)).Sum(h => h.Raw);
                    if (raw > 0) OrderLosses.Add(new OrderLoss(e, boost, "order", power.Owner, raw * ((double)amount - 1)));
                }
                break;
        }
    }

    /// The allies' attacks made this turn, before this play, while its card was already in the player's hand (co-op).
    private static IEnumerable<Play> AlliesBefore(Play? play, Player player) => play == null ? Array.Empty<Play>()
        : ThisTurn.TakeWhile(p => p != play).Where(p => p.Player != player && p.Card.Type == CardType.Attack && !p.OtherThanDebuff &&
                                                        p.AlliesHandAtPlay.Contains(play.Card)).ToList();

    /// The play's hits made by the player (its pet's left out: Strength does not change them).
    private static IEnumerable<EnemyHit> OwnHits(Play play)
    {
        var pet = play.PetHits.ToList();
        foreach (var h in play.Hits)
            if (pet.FirstOrDefault(c => ReferenceEquals(c, h.Target)) is { } c) pet.Remove(c);
            else yield return h;
    }

    /// Vulnerable's multiplier on this enemy for the player's attacks (1 without Vulnerable).
    private static double Vulnerable(Creature enemy, Player player) =>
        enemy.GetPower<VulnerablePower>() is { } v ? VulnerableFactor(v, enemy, player.Creature) : 1;

    /// Forge (v0.18.5, Zaille75): Lame souveraine played before a Forge card that was already in hand lost the forged damage.
    public static void OnForge(decimal amount, Player forger)
    {
        if (amount <= 0 || !InProgress.TryPeek(out var play) || play == null || play.Player != forger) return;
        foreach (var e in ThisTurn.TakeWhile(p => p != play).Where(p => p.Player == forger && p.Card is SovereignBlade &&
                                                                       !p.OtherThanDebuff && p.HandAtPlay.Contains(play.Card)))
            foreach (var g in OwnHits(e).GroupBy(h => (Creature)h.Target))
                OrderLosses.Add(new OrderLoss(e, Key(play.Card), "order", g.Key, (double)amount * g.Count() * Vulnerable(g.Key, forger)));
    }

    /// Vulnerable's damage multiplier on this enemy for the player's attacks (1.5 when the game gives none).
    private static double VulnerableFactor(PowerModel vulnerable, Creature enemy, Creature dealer)
    {
        var f = (double)DebuffEffectPatches.Mult(vulnerable, enemy, 10, ValueProp.Move, dealer, null, null) ;
        return f > 1 ? f : 1.5;
    }

    /// A hit of the card play in progress on an enemy (for the wrong-order and overkill checks). Raw: block + HP, the overkill
    /// apart.
    public static void OnHit(CardModel card, Creature target, int raw, Creature? dealer = null, int overkill = 0, bool killed = false)
    {
        if (raw <= 0 || !target.IsEnemy || !InProgress.TryPeek(out var play) || play == null || play.Card != card) return;
        var slow = target.GetPower<SlowPower>() is { } power ? (int)power.DynamicVars["SlowAmount"].BaseValue : -1;
        play.Hits.Add(new EnemyHit(target, raw, slow));
        if (killed && ReferenceEquals(target, play.Target))
        {
            play.KilledTarget = true;
            play.Overkill += overkill;
        }
        // Hit by the player after a Strength drop it could have been played before (Osty does not use Strength).
        if (dealer != play.Player.Creature)
        {
            if (dealer?.PetOwner == play.Player) play.PetHits.Add(target);
            return;
        }
        foreach (var (drop, loss) in StrengthDrops.Where(d => d.Play.Player == play.Player && d.Play.HandAtPlay.Contains(card)))
            OrderLosses.Add(new OrderLoss(drop, Key(card), "order", target, Math.Min(loss, raw), play));
    }

    /// A damage potion thrown at an enemy that the player's attacks of this turn had already hit, when the potion alone
    /// would have killed it before those attacks (v0.17.30): the attacks were wasted on it (they could have hit another
    /// enemy, or been kept). One miss per earlier attack, in "Choix" (kind "orderPotion"), with the damage it dealt there.
    /// Raw: the potion's whole hit (block, HP, overkill); Overkill: the part beyond the enemy's HP.
    public static void OnPotionHit(Player player, string potion, Creature target, int raw, int overkill)
    {
        if (raw <= 0) return;
        var earlier = ThisTurn.Where(p => p.Player == player && p.Card.Type == CardType.Attack && !p.HadNonDamageEffect)
            .Select(p => (Play: p, Damage: p.Hits.Where(h => ReferenceEquals(h.Target, target)).Sum(h => h.Raw))).Where(x => x.Damage > 0).ToList();
        if (earlier.Count == 0) return;
        // The enemy's HP + block before those attacks = what the potion met + what the attacks took.
        var before = raw - overkill + earlier.Sum(x => x.Damage);
        if (raw < before) return;
        foreach (var (play, damage) in earlier)
        {
            ModLog.Info($"potion after attacks: {Capture.IdOf(play.Card)} before {potion}, {damage} damage wasted");
            Capture.Emit(new MissedChance(Capture.ToActor(player), "orderPotion", new[] { Key(play.Card), potion, N(damage) }));
        }
    }

    /// Slow (Bygone Effigy): each card played this turn makes the enemy take 10% more attack damage. A non-attack card
    /// played after an attack that hit it, while it was already in hand, would have boosted that attack (spec §E0,
    /// wrong order, v0.17.0).
    private static void CheckSlowOrder(Play late)
    {
        if (late.Card.Type == CardType.Attack) return;
        foreach (var early in ThisTurn.Where(p => p != late && p.Player == late.Player && p.Card.Type == CardType.Attack &&
                                                  !p.HadNonDamageEffect && p.HandAtPlay.Contains(late.Card)))
            foreach (var (target, raw, slow) in early.Hits.Where(h => h.Slow >= 0))
                OrderLosses.Add(new OrderLoss(early, Key(late.Card), "orderSlow", target, raw * 0.1 / (1 + 0.1 * slow)));
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

    /// An orb Focus boosts fired during the player's own card play. Kind: "order" (hit on that enemy), "orderBlock"
    /// (Frost block), "orderDark" (Dark orb charged).
    public static void OnOrbTriggered(Player owner, Creature? enemy, string kind = "order")
    {
        if (InProgress.TryPeek(out var play) && play != null && play.Player == owner) play.OrbTriggers.Add((enemy, kind));
    }

    public static void OnBlockGained(int playId, int amount)
    {
        if (playId == 0) return;
        var play = ThisTurn.FirstOrDefault(p => p.Id == playId);
        if (play == null) return;
        play.BlockAmount += amount;
        play.BlockGains++;
        // Block gained after a Dexterity drop it could have been played before (Décomposition, v0.18.5).
        foreach (var (drop, loss) in DexterityDrops.Where(d => d.Play.Player == play.Player && d.Play.HandAtPlay.Contains(play.Card)))
            OrderLosses.Add(new OrderLoss(drop, Key(play.Card), "orderBlock", null, Math.Min(loss, amount), play));
    }

    /// One of the player's powers fired for the card play in progress, or for the one that just ended (Image rémanente's block,
    /// Lames fantomatiques' damage, v0.18.6).
    public static void OnPowerTriggered(Creature owner, string powerKey)
    {
        var play = InProgress.TryPeek(out var p) && p != null ? p : _justFinished;
        if (play != null && play.Player.Creature == owner) play.PowersTriggered.Add(powerKey);
    }

    /// Another card discarded, exhausted or moved by the play in progress (Survivor, Graveblast bringing a card back to
    /// the hand…). The played card itself going to its pile is not an effect. Not damage: an attack doing this was a real
    /// choice, never a weak attack (v0.17.1).
    public static void OnCardEffect(CardModel card)
    {
        if (!InProgress.TryPeek(out var play) || play == null || play.Card == card) return;
        play.HadOtherEffect = true;
        play.HadNonDamageEffect = true;
        play.OtherThanDebuff = true;
    }

    public static void OnPlayFinished()
    {
        if (InProgress.Count > 0 && InProgress.Pop() is { } play) _justFinished = play;
    }

    /// The card play that just ended (until the next one starts): what it triggers afterwards (Trou noir after Stars were
    /// spent, AfterCardPlayed) is its follow-up (v0.17.42).
    private static Play? _justFinished;

    /// Damage from one of the player's powers on an enemy: a follow-up of the card they just played.
    public static void OnFollowUpHit(Creature dealer, string source, Creature target, int raw)
    {
        if (raw <= 0 || _justFinished is not { } play || play.Player.Creature != dealer) return;
        play.FollowUps.Add(new FollowUpHit(source, target, raw));
    }

    /// Card play starts: remember the useful cards in the player's hand. Auto-played cards were not a choice.
    public static void OnPlayStarted(CardPlay cardPlay)
    {
        _justFinished = null;
        if (IsGrowing(cardPlay.Card)) GrowingPlayed.Add(cardPlay.Card);
        if (!TurnPlays.TryGetValue(Capture.PlayerOf(cardPlay), out var counts)) TurnPlays[Capture.PlayerOf(cardPlay)] = counts = new();
        counts[cardPlay.Card.Type] = counts.GetValueOrDefault(cardPlay.Card.Type) + 1;
        var hand = Capture.PlayerOf(cardPlay).PlayerCombatState?.Hand.Cards;
        if (cardPlay.IsAutoPlay || hand == null)
        {
            InProgress.Push(null);
            return;
        }
        // Played again by Déflagration & co.: the same choice, paid once (v0.17.38).
        if (cardPlay.PlayIndex > 0 && FirstPlays.TryGetValue(cardPlay.Card, out var first))
        {
            Plays.AddOrUpdate(cardPlay, first);
            InProgress.Push(first);
            return;
        }
        // Not checked at turn start: the hand before this first card is still the front hand.
        if (!FrontHand.ContainsKey(Capture.PlayerOf(cardPlay)))
            FrontHand[Capture.PlayerOf(cardPlay)] = FindLethal(Capture.PlayerOf(cardPlay), hand.Append(cardPlay.Card).Distinct().ToList(),
                (Capture.PlayerOf(cardPlay).PlayerCombatState?.Energy ?? 0) + cardPlay.Resources.EnergySpent);
        var aim = Aim(cardPlay, hand);
        var play = new Play
        {
            EnemiesAlive = cardPlay.Card.CombatState is { } cs ? RealEnemies(cs).Count : 1,
            EnergyBefore = (Capture.PlayerOf(cardPlay).PlayerCombatState?.Energy ?? 0) + cardPlay.Resources.EnergySpent,
            Target = aim.Target,
            TargetLeftBefore = aim.Left,
            DamageOn = aim.DamageOn,
            HandDamageOnTarget = aim.HandDamage,
            AlliesHandAtPlay = AlliesHand(Capture.PlayerOf(cardPlay)),
            Id = ++_nextId,
            Player = Capture.PlayerOf(cardPlay),
            Owner = Capture.ToActor(Capture.PlayerOf(cardPlay)),
            CardRef = cardPlay.Card,
            CardKey = Key(cardPlay.Card),
            Kind = KindOf(cardPlay.Card.Type),
            EnergySpent = cardPlay.Resources.EnergySpent,
            StarsSpent = cardPlay.Resources.StarsSpent,
            Ethereal = cardPlay.Card.Keywords.Contains(CardKeyword.Ethereal),
            // Genetic Algorithm / The Scythe grow each time they are played: a side effect no hook reports.
            // Sacrifice kills the player's own pet (validated by Zaille75: never wasted).
            HadOtherEffect = IsGrowing(cardPlay.Card) || cardPlay.Card is Sacrifice,
            HadNonDamageEffect = IsGrowing(cardPlay.Card),
            OtherThanDebuff = IsGrowing(cardPlay.Card),
            UsefulCardsInHand = hand.Where(c => c != cardPlay.Card && IsUseful(c)).Select(c => new SeenCard(c, Key(c))).ToList(),
            HandAtPlay = hand.Where(c => c != cardPlay.Card).ToList<object>()
        };
        var energyBefore = (Capture.PlayerOf(cardPlay).PlayerCombatState?.Energy ?? 0) + cardPlay.Resources.EnergySpent;
        if (cardPlay.Card.Type == CardType.Attack && cardPlay.Target is { IsEnemy: true } target)
        {
            if (IsDoubledByPenNib(cardPlay)) play.PenNibBetter = StrongerAttack(cardPlay.Card, target, hand, energyBefore, doubled: true);
            else play.Stronger = StrongerAttack(cardPlay.Card, target, hand, Cost(cardPlay.Card), doubled: false);
        }
        if (cardPlay.Card.GainsBlock) play.BiggerBlock = BiggerBlock(cardPlay.Card, hand);
        Capture.Safe("wrong order (Slow)", () => CheckSlowOrder(play));
        Plays.AddOrUpdate(cardPlay, play);
        FirstPlays[cardPlay.Card] = play;
        ThisTurn.Add(play);
        InProgress.Push(play);
    }

    /// A single-target attack: its target, the target's HP + block, the card's estimated damage on each enemy and that of
    /// every other single-target attack in hand on the target (overkill checks, v0.17.42).
    private static (Creature? Target, int Left, Dictionary<object, int> DamageOn, Dictionary<object, int> HandDamage) Aim(CardPlay cardPlay,
        IEnumerable<CardModel> hand)
    {
        var damageOn = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        var handDamage = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        var card = cardPlay.Card;
        if (card.Type != CardType.Attack || card.TargetType != TargetType.AnyEnemy || cardPlay.Target is not { IsEnemy: true } target ||
            card.CombatState is not { } combat)
            return (null, 0, damageOn, handDamage);
        try
        {
            foreach (var e in RealEnemies(combat).Where(e => HitCount(card, e) != null)) damageOn[e] = EstimateDamage(card, e);
            foreach (var c in hand.Where(c => c != card && c.Type == CardType.Attack && c.TargetType == TargetType.AnyEnemy && HitCount(c, target) != null))
                handDamage[c] = EstimateDamage(c, target);
        }
        catch (Exception e)
        {
            ModLog.Error("overkill estimates", e);
        }
        return (target, target.CurrentHp + target.Block, damageOn, handDamage);
    }

    /// The cards in the other players' hands (co-op order against Slow).
    private static HashSet<object> AlliesHand(Player player)
    {
        var cards = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var p in player.Creature.CombatState?.Players ?? Array.Empty<Player>())
            if (p != player)
                foreach (var c in p.PlayerCombatState?.Hand.Cards ?? Array.Empty<CardModel>())
                    cards.Add(c);
        return cards;
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

    /// End of the players' turn: the misplay rules (Core TurnReview, v0.17.41) on a snapshot of the turn, then the per-turn
    /// state is cleared.
    public static void ResolveTurn(ICombatState combat)
    {
        ExtraTurn.Clear();
        foreach (var p in combat.Players.Where(p => p.Creature.IsAlive))
            Capture.Safe("extra turn", () =>
            {
                if (Hook.ShouldTakeExtraTurn(combat, p)) ExtraTurn.Add(p);
            });
        Capture.Safe("missed lethal", () => ResolveLethal(combat));
        Capture.Safe("turn review", () =>
        {
            var result = TurnReview.Resolve(Snapshot(combat));
            foreach (var line in result.Log) ModLog.Info(line);
            foreach (var e in result.Events) Capture.Emit(e);
            foreach (var card in result.GrowingAffordable) GrowingAffordable.Add((CardModel)card);
            // Doom-only cards (v0.18.6): their energy is judged when their enemies die (StatsEngine).
            foreach (var play in ThisTurn.Where(p => p.DoomTargets.Count > 0 && p.EnergySpent > 0 && !p.OtherDebuff && !p.OtherThanDebuff &&
                                                     !p.GaveBlock && p.Hits.Count == 0))
                Capture.Emit(new DoomPlayed(play.Owner, play.CardKey, play.EnergySpent, play.DoomTargets.Distinct().Select(c => Capture.ToActor(c)).ToList()));
        });
        ClearTurn();
    }

    /// What the rules need from the game at the end of the turn.
    private static TurnState Snapshot(ICombatState combat)
    {
        var real = RealEnemies(combat);
        var s = new TurnState
        {
            NothingToKill = real.Count == 0,
            EnemyAsleep = real.Any(e => e.HasPower<AsleepPower>()),
            OpenEnemy = real.Any(e => !(e.GetPower<HardenedShellPower>() is { } p && p.DisplayAmount <= 0)),
            // The cap met: Carapace endurcie, or the enemy shown with infinite HP (Géant de la Cascade about to explode).
            ShellKey = real.Count == 0 && combat.Enemies.FirstOrDefault(e => e.IsAlive && e.Monster != null) is { Monster: { } giant }
                ? $"Monster:{Capture.IdOf(giant)}" : $"Power:{ModelDb.GetId<HardenedShellPower>().Entry}"
        };
        foreach (var e in combat.Enemies.Where(e => e.IsAlive)) s.EnemiesLeft[e] = e.CurrentHp + e.Block;
        foreach (var player in combat.Players)
            Capture.Safe("turn snapshot", () =>
            {
                if (PlayerView(combat, player) is { } view) s.Players.Add(view);
            });
        foreach (var play in ThisTurn)
        {
            Capture.Safe("turn snapshot (thorns)", () => play.Thorns = (double)ThornsDamage(play.Card, play.Player.Creature));
            s.Plays.Add(play);
        }
        s.OrderLosses.AddRange(OrderLosses);
        return s;
    }

    private static TurnPlayer? PlayerView(ICombatState combat, Player player)
    {
        if (player.PlayerCombatState is not { } st) return null;
        var creature = player.Creature;
        RelicModel? orichalcum = (RelicModel?)player.GetRelic<Orichalcum>() ?? player.GetRelic<FakeOrichalcum>();
        return new TurnPlayer
        {
            Actor = Capture.ToActor(player),
            Alive = creature.IsAlive,
            Energy = st.Energy,
            Block = creature.Block,
            Incoming = IncomingDamage(creature),
            EnergyResets = Hook.ShouldPlayerResetEnergy(combat, player),
            PaelsTears = player.GetRelic<PaelsTears>() is { } tears ? $"Relic:{Capture.IdOf(tears)}" : null,
            Orichalcum = orichalcum == null ? null : $"Relic:{Capture.IdOf(orichalcum)}",
            OrichalcumBlock = orichalcum == null ? 0 : (int)orichalcum.DynamicVars.Block.BaseValue,
            Relics = player.Relics.Select(RelicView).OfType<TurnRelic>().ToList(),
            Hand = st.Hand.Cards.Select(c => CardView(c, combat, creature)).ToList(),
            GambitActive = creature.GetPower<TheGambitPower>() != null,
            AlliesAlive = combat.Players.Any(p => p != player && p.Creature.IsAlive),
            PetAlive = !player.IsOstyMissing,
            PetSacrificedThisTurn = PetSacrificed.Contains(player),
            StampedeShort = StampedeShort.GetValueOrDefault(player),
            StampedeKey = $"Power:{ModelDb.GetId<StampedePower>().Entry}",
            StampedePlayed = (StampedeAll.GetValueOrDefault(player) ?? new()).Select(c => CardView(c, combat, creature)).ToList(),
            Plays = (TurnPlays.GetValueOrDefault(player) ?? new()).ToDictionary(kv => KindOf(kv.Key), kv => kv.Value)
        };
    }

    private static TurnCard CardView(CardModel c, ICombatState combat, Creature creature) => new()
    {
        Ref = c,
        Key = Key(c),
        Kind = KindOf(c.Type),
        Cost = Cost(c),
        CostsX = c.EnergyCost.CostsX,
        CostsStars = c.CurrentStarCost > 0 || c.HasStarCostX,
        PlayableExceptEnergy = PlayableExceptEnergy(c),
        PlayableInstead = PlayableInstead(c),
        LacksStars = LacksStars(c),
        KeptOnPurpose = KeptOnPurpose(c, combat),
        NeedsPet = c.Tags.Contains(CardTag.OstyAttack),
        SacrificesPet = SacrificesPet.Contains(c.Id.Entry),
        GainsBlock = c.GainsBlock,
        Block = c.GainsBlock ? EstimateBlock(c) : 0,
        // Mimic copies the block of an ally onto the player: block for the player.
        BlockFor = c is Mimic ? BlockTarget.Self
            : c.TargetType == TargetType.AnyAlly ? BlockTarget.OneAlly
            : c.TargetType == TargetType.AllAllies ? BlockTarget.AllAllies
            : BlockTarget.Self,
        Exhausts = c.Keywords.Contains(CardKeyword.Exhaust),
        Gambit = c is TheGambit,
        Growing = IsGrowing(c),
        Rage = c is Rage,
        HitsEnemies = c.TargetType is TargetType.AnyEnemy or TargetType.AllEnemies,
        Thorns = (double)ThornsDamage(c, creature),
        Punished = PunishedAttack(c),
        DealsDamage = c.Type != CardType.Attack || DealsDamage(c, combat),
        DamageOn = c.Type != CardType.Attack ? new Dictionary<object, int>() :
            combat.HittableEnemies.Where(e => HitCount(c, e) != null).ToDictionary(e => (object)e, e => EstimateDamage(c, e))
    };

    private static CardKind KindOf(CardType type) => type switch
    {
        CardType.Attack => CardKind.Attack,
        CardType.Skill => CardKind.Skill,
        CardType.Power => CardKind.Power,
        CardType.Status => CardKind.Status,
        CardType.Curse => CardKind.Curse,
        _ => CardKind.Other
    };

    /// Relics that trigger on cards played in a turn (Every: the "Cards" variable, 3 by default).
    private static TurnRelic? RelicView(RelicModel relic)
    {
        RelicTrigger? trigger = relic switch
        {
            Shuriken or Kunai or OrnamentalFan or Kusarigama => RelicTrigger.Attacks,
            LetterOpener => RelicTrigger.Skills,
            RainbowRing => RelicTrigger.AllTypes,
            BrilliantScarf => RelicTrigger.NthCardFree,
            ScreamingFlagon => RelicTrigger.EmptyHand,
            _ => null
        };
        if (trigger == null) return null;
        var every = relic.DynamicVars.TryGetValue("Cards", out var v) ? v.IntValue : 3;
        return new TurnRelic($"Relic:{Capture.IdOf(relic)}", trigger.Value, every);
    }

    /// A hit of the card play in progress that met a Carapace endurcie at its limit (0 damage).
    public static void OnShellCapped(CardModel card)
    {
        if (InProgress.TryPeek(out var play) && play != null && play.Card == card) play.ShellCapped++;
    }

    /// Missed lethal: the front hand could kill every enemy, yet one of them is alive at the end of the turn.
    private static void ResolveLethal(ICombatState combat)
    {
        var enemies = RealEnemies(combat);
        if (enemies.Count == 0) return;
        foreach (var (player, front) in FrontHand)
            // The enemies the front hand could kill must still be alive: killing them can bring new ones (Parasite
            // Krapo's larvae), which the front hand could not see (v0.17.3).
            if (front is { } lethal && lethal.Targets.Any(t => t.IsAlive && t.HpDisplay == HpDisplay.Normal))
            {
                ModLog.Info($"missed lethal: {player.NetId} [{string.Join(", ", lethal.Attacks.Select(a => a.Id))}]");
                Capture.Emit(new MissedLethal(Capture.ToActor(player), lethal.Attacks.Select(a => a.Id).ToList(),
                    enemies.Sum(e => e.CurrentHp + e.Block)));
            }
    }

    /// Enemies that can really be hurt: an enemy shown with infinite HP (Waterfall Giant about to explode) is not one
    /// left to kill (spec §E0, invulnerable phase).
    private static List<Creature> RealEnemies(ICombatState? combat) =>
        (combat?.HittableEnemies ?? Array.Empty<Creature>()).Where(e => e.HpDisplay == HpDisplay.Normal).ToList();

    private static string Key(CardModel card) => $"Card:{Capture.IdOf(card)}";
    private static string N(int v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// The attack would deal damage to at least one enemy (cards whose damage the mod cannot compute count as useful).
    private static bool DealsDamage(CardModel card, ICombatState combat)
    {
        if (!card.DynamicVars.TryGetValue("Damage", out _) && !card.DynamicVars.TryGetValue("OstyDamage", out _)) return true;
        return combat.HittableEnemies.Any(e => EstimateDamage(card, e) > 0);
    }

    /// Cards that grow each time they are played (card audit, spec §E0): playing them is never a waste, and not playing
    /// them when affordable is a missed opportunity.
    internal static bool IsGrowing(CardModel c) =>
        c is GeneticAlgorithm or TheScythe or Claw or Maul or Rampage || c.Id.Entry == "THE_BALL"; // The Ball: beta only

    /// Pen Nib picks the attack to double before the play starts.
    private static bool IsDoubledByPenNib(CardPlay cardPlay)
    {
        var nib = Capture.PlayerOf(cardPlay).GetRelic<PenNib>();
        if (nib == null) return false;
        return Traverse.Create(nib).Property(ReflectionTargets.PenNibAttackToDouble.Name).GetValue<CardModel>() == cardPlay.Card;
    }

    /// A block card in hand giving more block than the one played (same cost or cheaper).
    private static Better? BiggerBlock(CardModel played, IEnumerable<CardModel> hand)
    {
        try
        {
            var block = EstimateBlock(played);
            if (block <= 0) return null;
            var cost = Cost(played);
            var best = hand.Where(c => c != played && c.GainsBlock && c is not TheGambit && !c.EnergyCost.CostsX && Cost(c) <= cost && PlayableExceptEnergy(c))
                .Select(c => (Card: c, Block: EstimateBlock(c))).Where(x => x.Block > block)
                .OrderByDescending(x => x.Block).FirstOrDefault();
            return best.Card == null ? null : new Better(best.Card, Key(best.Card), best.Block, block);
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
            // Enemies whose death does not end the fight (they come back, or the fight goes on) are not "lethal" (v0.17.38).
            var all = RealEnemies(player.Creature.CombatState).Where(e => !ComesBack(e)).ToList();
            var enemies = all.Where(e => !e.IsSecondaryEnemy).ToList();
            if (enemies.Count == 0) enemies = all.ToList();
            if (enemies.Count == 0) return null;
            // Résonnant: only playable as the first card of the turn (left out); Lié under Entraves: one per turn (the first kept).
            var chained = player.Creature.HasPower<ChainsOfBindingPower>();
            var boundKept = false;
            bool Allowed(CardModel c)
            {
                if (c.Affliction is Ringing) return false;
                if (!chained || c.Affliction is not Bound) return true;
                if (boundKept) return false;
                boundKept = true;
                return true;
            }
            var attacks = hand.Where(c => c.Type == CardType.Attack && !c.EnergyCost.CostsX && PlayableExceptEnergy(c) && !LacksStars(c) &&
                                          c.TargetType is TargetType.AnyEnemy or TargetType.AllEnemies && Allowed(c))
                .Select(c => new Lethal.Attack($"Card:{Capture.IdOf(c)}", Cost(c), c.TargetType == TargetType.AllEnemies,
                    enemies.Select(e => EstimateDamage(c, e)).ToList()))
                .ToList();
            // Paresse: at most that many cards this turn.
            var maxPlays = player.Creature.GetPower<SlothPower>()?.Amount ?? int.MaxValue;
            var found = Lethal.Find(attacks, energy, enemies.Select(e => e.CurrentHp + e.Block).ToList(), maxPlays);
            return found == null ? null : (found.ToArray(), enemies);
        }
        catch (Exception e)
        {
            ModLog.Error("lethal check", e);
            return null;
        }
    }

    /// An enemy whose death does not end the fight: it comes back (Adaptable, Reconnexion, Illusion) or keeps the fight
    /// going (Infesté, Stock, Éruption de vapeur, Surprise).
    private static bool ComesBack(Creature enemy) =>
        enemy.Powers.Any(p => p is AdaptablePower or ReattachPower or IllusionPower or InfestedPower or StockPower or SteamEruptionPower or
            SurprisePower);

    /// The strongest attack in hand on the same target, if it beats the attack played, costs no more, and the
    /// played attack could not kill the target anyway.
    /// Budget: the most the stronger attack may cost. Doubled: the played attack is doubled by Pen Nib (its estimate
    /// already is), so the other attacks are compared doubled too.
    private static Better? StrongerAttack(CardModel played, Creature target, IEnumerable<CardModel> hand, int budget,
        bool doubled)
    {
        try
        {
            // Cards whose number of hits the mod cannot know (Whirlwind, Fiend Fire…) are never compared.
            if (HitCount(played, target) == null) return null;
            var damage = EstimateDamage(played, target);
            if (damage <= 0 || damage >= target.CurrentHp + target.Block) return null;
            var factor = doubled ? 2 : 1;
            var best = hand.Where(c => c != played && c.Type == CardType.Attack && !c.EnergyCost.CostsX && CostWithout(c, played) <= budget &&
                                       c.TargetType is TargetType.AnyEnemy or TargetType.AllEnemies && PlayableExceptEnergy(c) &&
                                       HitCount(c, target) != null)
                .Select(c => (Card: c, Damage: EstimateDamage(c, target) * factor))
                .Where(x => x.Damage > damage).OrderByDescending(x => x.Damage).FirstOrDefault();
            return best.Card == null ? null : new Better(best.Card, Key(best.Card), best.Damage, damage);
        }
        catch (Exception e)
        {
            ModLog.Error("stronger attack", e);
            return null;
        }
    }

    /// Cards that always hit twice without saying it in a variable.
    private static readonly HashSet<string> TwoHits = new() { "ASTRAL_PULSE", "MAUL", "DAGGER_SPRAY", "TWIN_STRIKE", "UPROAR", "RIP_AND_TEAR", "REFRACT", "THRASH" };

    /// Attacks whose "Repeat" counts something else than hits (Ice Lance: Frost orbs channeled; Heirloom Hammer: copies),
    /// found by an audit of every attack with a Repeat variable (v0.17.32).
    private static readonly HashSet<string> RepeatNotHits = new() { "ICE_LANCE", "HEIRLOOM_HAMMER" };

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
        if (TwoHits.Contains(id)) return 2;
        if (!RepeatNotHits.Contains(id) && card.DynamicVars.TryGetValue("Repeat", out var repeat)) return Math.Max(1, repeat.IntValue);
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
#if GAME_DEFAULT
            damage = Hook.ModifyDamage(owner.RunState, card.CombatState, target, dealer, v.BaseValue, props, card,
                ModifyDamageHookType.All, CardPreviewMode.None, out _);
#else
            damage = Hook.ModifyDamage(owner.RunState, card.CombatState, target, dealer, v.BaseValue, props, card, null,
                ModifyDamageHookType.All, CardPreviewMode.None, out _);
#endif
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

    /// Damage the player will take before their block is cleared: the attacks the enemies announced, computed for this
    /// player (the game's intent shows the local player's numbers, v0.17.38), plus the status cards in hand that hurt at
    /// the end of the turn and can be blocked (Brûlure…). Nothing when the player takes an extra turn first.
    internal static int IncomingDamage(Creature creature)
    {
        var incoming = 0;
        try
        {
            if (creature.Player is { } p && ExtraTurn.Contains(p)) return 0;
            if (creature.CombatState is not { } combat) return 0;
            DebuffEffectPatches.Computing = true;
            foreach (var enemy in combat.Enemies)
                if (enemy.IsAlive && enemy.Monster != null)
                    foreach (var intent in enemy.Monster.NextMove.Intents.OfType<AttackIntent>())
                    {
                        if (intent.DamageCalc is not { } calc) continue;
#if GAME_DEFAULT
                        var hit = Hook.ModifyDamage(combat.RunState, combat, creature, enemy, calc(), ValueProp.Move, null,
                            ModifyDamageHookType.All, CardPreviewMode.None, out _);
#else
                        var hit = Hook.ModifyDamage(combat.RunState, combat, creature, enemy, calc(), ValueProp.Move, null, null,
                            ModifyDamageHookType.All, CardPreviewMode.None, out _);
#endif
                        incoming += Math.Max(0, (int)hit) * Math.Max(1, intent.Repeats);
                    }
            foreach (var card in creature.Player?.PlayerCombatState?.Hand.Cards ?? Array.Empty<CardModel>())
                if (card.HasTurnEndInHandEffect && card.DynamicVars.TryGetValue("Damage", out var v) && v is DamageVar dv &&
                    !dv.Props.HasFlag(ValueProp.Unblockable))
                    incoming += Math.Max(0, (int)dv.BaseValue);
        }
        catch (Exception e)
        {
            ModLog.Error("incoming damage", e);
        }
        finally
        {
            DebuffEffectPatches.Computing = false;
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
    /// (Retain, or the whole hand is kept: Main retenue…), it costs Stars (they are not refilled each turn), or it Exhausts
    /// (gone once played: v0.17.34, Zaille75 — Surins and Entrave des ténèbres included; missed lethal still uses it), or it
    /// exhausts or transforms other cards of the hand (v0.17.44, Zaille75: HORS DE MA VUE ! kept not to lose a card), or it
    /// costs HP to play (v0.18.4, Zaille75: Percée kept at 3 HP is a choice), or it only evokes orbs (v0.18.5, Activation double).
    private static bool KeptOnPurpose(CardModel card, ICombatState combat) =>
        card.ShouldRetainThisTurn || card.CurrentStarCost > 0 || card.HasStarCostX || card.Keywords.Contains(CardKeyword.Exhaust) ||
        ConsumesHand.Contains(card.Id.Entry) || EnergyOnly.Contains(card.Id.Entry) || SacrificesPet.Contains(card.Id.Entry) ||
        CostsHp.Contains(card.Id.Entry) || EvokesOrbs.Contains(card.Id.Entry) || !Hook.ShouldFlush(combat, card.Owner);

    /// Cards that only evoke the player's orbs (v0.18.5, Zaille75): keeping an orb is a choice.
    private static readonly HashSet<string> EvokesOrbs = new() { "DUALCAST", "MULTI_CAST", "QUADCAST" };

    /// Cards that make their player lose HP when played (audit of every card, v0.18.4; Saignée is also in EnergyOnly).
    private static readonly HashSet<string> CostsHp = new()
    {
        "BLOOD_WALL", "BLOODLETTING", "BRAND", "BREAKTHROUGH", "DEMONIC_SHIELD", "HEMOKINESIS", "OFFERING"
    };

    /// Cards that only give energy (audit of every card, v0.17.45: Sursis…): they cannot use the energy left.
    private static readonly HashSet<string> EnergyOnly = new()
    {
        "ALIGNMENT", "BELIEVE_IN_YOU", "BLOODLETTING", "BORROWED_TIME", "DOUBLE_ENERGY", "ENERGY_SURGE", "EXPECT_A_FIGHT",
        "FORGOTTEN_RITUAL", "LUMINESCE", "PRODUCTION", "SUPERCRITICAL", "TACTICIAN", "TURBO", "WISP"
    };

    /// Cards that kill the player's own pet (v0.17.45, Zaille75: a side effect nobody wants, except for a missed lethal).
    private static readonly HashSet<string> SacrificesPet = new() { "BONE_SHARDS", "SACRIFICE" };

    /// Cards that exhaust or transform other cards of the hand, chosen or not (audit of every card, v0.17.44). Compactage is
    /// left out: it only transforms status cards.
    private static readonly HashSet<string> ConsumesHand = new()
    {
        "BEGONE", "BRAND", "BURNING_PACT", "GUARDS", "PURITY", "SCAVENGE", "TRUE_GRIT", "CINDER", "FIEND_FIRE", "SECOND_WIND",
        "STOKE", "THRASH", "EIDOLON", "PRIMAL_FORCE"
    };

    /// The player has not enough Stars for this card.
    private static bool LacksStars(CardModel card)
    {
        card.CanPlay(out var reason, out _);
        return (reason & UnplayableReason.StarCostTooHigh) != UnplayableReason.None;
    }

    /// An attack the enemies punish like Thorns (v0.17.20): Personal Hive (Apicultueur) adds a Dazed to the deck per hit.
    /// Single target: punished only if every enemy it could hit punishes and it would kill none of them; all enemies:
    /// punished if any of them punishes (unless it kills every punishing one). Not playing it is then a choice.
    /// v0.18.6: also a card Souillé (Prisme infesté's Étincelle vitale) while an enemy attacks: playing it makes every hit
    /// this turn deal more damage, so keeping it is a choice.
    private static bool PunishedAttack(CardModel card)
    {
        if (card.CombatState is not { } combat) return false;
        if (card.Affliction is Tainted) return combat.HittableEnemies.Any(Attacks);
        if (card.Type != CardType.Attack || card.CombatState is not { } cs) return false;
        var enemies = cs.HittableEnemies.Where(e => e.HpDisplay == HpDisplay.Normal).ToList();
        if (enemies.Count == 0) return false;
        bool Punishes(Creature e) => e.HasPower<PersonalHivePower>() && EstimateDamage(card, e) < e.CurrentHp + e.Block;
        return card.TargetType == TargetType.AllEnemies ? enemies.Any(Punishes) : enemies.All(Punishes);
    }

    private static int Cost(CardModel card) => Math.Max(0, card.EnergyCost.GetWithModifiers(CostModifiers.All));

    /// A card's cost had the played card not been played (v0.18.4): Piétinement already costs 1 less when an attack starts
    /// (the game lowers it before the play), so it could not have replaced that attack at this price. The other cards
    /// whose cost drops with plays (Localisation, Cri de la banshee) only change after the play.
    private static int CostWithout(CardModel card, CardModel played) =>
        Cost(card) + (card is Stomp && played.Type == CardType.Attack && played.Owner == card.Owner ? 1 : 0);

    /// Playable instead of a card already played: a limit on the cards played this turn (Paresse, Résonnant) does not count,
    /// the played card took its place (v0.17.38).
    private static bool PlayableInstead(CardModel card)
    {
        card.CanPlay(out var reason, out var preventer);
        reason &= ~(UnplayableReason.EnergyCostTooHigh | UnplayableReason.StarCostTooHigh);
        if (reason == UnplayableReason.BlockedByHook && preventer is SlothPower or RingingPower) return true;
        return reason == UnplayableReason.None;
    }

    private static bool PlayableExceptEnergy(CardModel card)
    {
        card.CanPlay(out var reason, out _);
        return (reason & ~(UnplayableReason.EnergyCostTooHigh | UnplayableReason.StarCostTooHigh)) == UnplayableReason.None;
    }
}
