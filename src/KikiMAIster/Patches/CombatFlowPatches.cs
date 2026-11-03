using System.Runtime.CompilerServices;
using HarmonyLib;
using KikiMAIster.Core;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Runs;

namespace KikiMAIster.Patches;

/// Run / combat / round boundaries, and who is currently acting (card play, potion).
public static class CombatFlowPatches
{
    private static WeakReference<IRunState>? _currentRun;
    private static bool _combatOpen;
    private static string? _runKey;
    private static int _floor;
    /// The fight being played: its real time, and its record key (encounter, ascension, players, solo character, v0.17.39).
    private static readonly System.Diagnostics.Stopwatch FightClock = new();
    /// Time of the last enemy killed: a won fight lasts from its start to that death, not to the end of the last
    /// animations (v0.17.42, Zaille75). Null when no enemy died (they fled): the fight's end is used.
    private static double? _lastKillSeconds;

    /// An enemy died (any cause).
    public static void EnemyKilled()
    {
        if (_combatOpen) _lastKillSeconds = FightClock.Elapsed.TotalSeconds;
    }
    private static string? _fightKey;

    /// The ascension level of the run being played (group compositions are counted per level).
    public static int Ascension { get; private set; }
    /// The number of players of the run being played.
    public static int PlayerCount { get; private set; }

    /// The run being played (null before the first run of the session).
    public static IRunState? CurrentRun => _currentRun != null && _currentRun.TryGetTarget(out var run) ? run : null;

    /// Ends the combat once, whether it was won (AfterCombatEnd) or lost (LoseCombat).
    private static void EndCombat(bool victory)
    {
        if (!_combatOpen) return;
        _combatOpen = false;
        DamagePatches.FlushPending();
        BlockAlternatives.ResolveCombat();
        BlockAndPowerPatches.Reset();
        Capture.Safe("rest before the boss", () => RestSitePatches.OnCombatEnded(victory));
        var seconds = victory && _lastKillSeconds is { } kill ? kill : FightClock.Elapsed.TotalSeconds;
        FightClock.Reset();
        _lastKillSeconds = null;
        Capture.Emit(new CombatEnded(victory, seconds));
        Capture.LogUnknownSources();
        if (victory && _fightKey != null && Capture.Engine.LastCombat is { } fight)
        {
            var key = _fightKey;
            Capture.Safe("fight records", () => UI.FightRecordStore.Add(key, fight.Number, fight.LastRound, seconds));
        }
        _fightKey = null;
        RunHistoryPatches.Read(CurrentRun);
        Summary.LogCombat(Capture.Engine);
        if (_runKey != null) RunStore.Save(_runKey, _floor, Capture.Engine);
    }

    private static string EncounterName(ICombatState? combatState)
    {
        try
        {
            return combatState?.Encounter?.Title.GetFormattedText() ?? "";
        }
        catch (Exception)
        {
            return "";
        }
    }

    /// Starts tracking a run the first time it is seen, and restores its saved totals if it is a continued run.
    private static void EnsureRun(IRunState runState)
    {
        _floor = runState.TotalFloor;
        Ascension = runState.AscensionLevel;
        PlayerCount = runState.Players.Count;
        if (_currentRun != null && _currentRun.TryGetTarget(out var run) && ReferenceEquals(run, runState))
        {
            RunHistoryPatches.Read(runState);
            return;
        }
        _currentRun = new WeakReference<IRunState>(runState);
        _runKey = null;
        _runOver = false;
        Capture.Emit(new RunStarted());
        Capture.Safe("run key", () => _runKey = RunStore.KeyOf(runState));
        if (_runKey != null) RunStore.TryRestore(_runKey, _floor, Capture.Engine);
        RunHistoryPatches.Reset();
        RunHistoryPatches.Read(runState);
        // Composition stats are rebuilt from the game's history before this run can end (it would be counted twice).
        _ = UI.CompositionStore.Data;
    }

    // New or continued run, solo or co-op: show the right run totals before the first combat.
    [HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpNewSingleplayer))]
    public static class NewSolo
    {
        public static void Prefix(RunState state) => Capture.Safe("SetUpNewSingleplayer", () => EnsureRun(state));
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpNewMultiplayer))]
    public static class NewCoop
    {
        public static void Prefix(RunState state) => Capture.Safe("SetUpNewMultiplayer", () => EnsureRun(state));
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpSavedSingleplayer))]
    public static class SavedSolo
    {
        public static void Prefix(RunState state) => Capture.Safe("SetUpSavedSingleplayer", () => EnsureRun(state));
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpSavedMultiplayer))]
    public static class SavedCoop
    {
        public static void Prefix(RunState state) => Capture.Safe("SetUpSavedMultiplayer", () => EnsureRun(state));
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.BeforeCombatStart))]
    public static class CombatStart
    {
        public static void Prefix(IRunState runState, ICombatState? combatState) => Capture.Safe("BeforeCombatStart", () =>
        {
            EnsureRun(runState);
            Capture.CardPlayers.Clear();
            Capture.CardPlays.Clear();
            Capture.PotionUser = null;
            DamagePatches.Reset();
            BlockAlternatives.Reset();
            Capture.ResetUnknownSources();
            BlockAndPowerPatches.Reset();
            _combatOpen = true;
            FightClock.Restart();
            _lastKillSeconds = null;
            _fightKey = null;
            Capture.Safe("fight key", () =>
            {
                var fighters = combatState?.Players ?? runState.Players;
                if (combatState?.Encounter is { } e)
                    _fightKey = FightRecords.Key(runState.AscensionLevel, fighters.Count, fighters.Count == 1 ? fighters[0].Character.Id.Entry : "",
                        e.Id.Entry);
            });
            RunHistoryPatches.PreFight(runState);
            if (combatState?.Encounter is { } encounter)
                Capture.Safe("rest before the boss", () => RestSitePatches.OnCombatStarted(runState, encounter.RoomType));
            KikiMAIster.UI.Overlay.EnsureAttached("combat start");
            KikiMAIster.UI.CombatSummary.HideAutoShown();
            var players = combatState?.Players ?? runState.Players;
            PlayLog.CombatStarted(combatState, players, EncounterName(combatState));
            Capture.Emit(new CombatStarted(players.Select(p =>
                new PlayerInfo(p.NetId, Capture.PlayerName(p, players.Count), p.Character.Id.Entry)).ToList(),
                EncounterName(combatState)));
        });
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.BeforeSideTurnStart))]
    public static class RoundStart
    {
        public static void Prefix(ICombatState combatState, CombatSide side) => Capture.Safe("BeforeSideTurnStart", () =>
        {
            if (side != CombatSide.Player) return;
            DamagePatches.FlushPending();
            DebuffEffectPatches.ResetOffense();
            Capture.Emit(new RoundStarted(combatState.RoundNumber));
            BlockAndPowerPatches.GiveNextTurnHelp();
            ActingEnemy = null;
            // Each player's state for the fun trophies (v0.17.45): dead, alone alive (co-op), at 1 HP.
            var players = combatState.Players;
            foreach (var p in players)
            {
                var alive = p.Creature.IsAlive;
                var alone = alive && players.Count > 1 && players.All(o => o == p || !o.Creature.IsAlive);
                Capture.Emit(new PlayerRoundState(Capture.ToActor(p), p.Creature.CurrentHp, alive, alone));
            }
        });
    }

    /// The enemy whose move is being played (v0.17.45): status cards it creates name it as their creator.
    public static Creature? ActingEnemy;

    [HarmonyPatch(typeof(MonsterModel), nameof(MonsterModel.PerformMove))]
    public static class EnemyMove
    {
        public static void Prefix(MonsterModel __instance) => Capture.Safe("MonsterModel.PerformMove", () => ActingEnemy = __instance.Creature);
    }

    [HarmonyPatch]
    public static class TurnEnd
    {
        /// Hook.BeforeSideTurnEnd (beta), named BeforeTurnEnd on the main branch.
        public static System.Reflection.MethodBase TargetMethod() =>
            AccessTools.Method(typeof(Hook), GameBranch.IsBeta ? nameof(Hook.BeforeSideTurnEnd) : "BeforeTurnEnd");

        public static void Prefix(ICombatState combatState, CombatSide side) => Capture.Safe("BeforeSideTurnEnd", () =>
        {
            if (side != CombatSide.Player) return;
            PlayLog.TurnEnded(combatState);
            BlockAlternatives.ResolveTurn(combatState);
        });
    }

    /// Hand drawn: the front hand of the turn (missed lethal check).
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterPlayerTurnStart))]
    public static class PlayerTurnStart
    {
        public static void Prefix(Player player) => Capture.Safe("AfterPlayerTurnStart", () =>
        {
            PlayLog.TurnStarted(player);
            BlockAlternatives.OnTurnStarted(player);
            Capture.Safe("max energy", () => MaxEnergyGained(player));
        });
    }

    /// Énergie gagnée (v0.18.6, Zaille75: Domaine): what each relic or power adds to the player's max energy, every turn.
    private static void MaxEnergyGained(Player player)
    {
        decimal max = player.MaxEnergy;
        foreach (var model in player.Relics.Cast<AbstractModel>().Concat(player.Creature.Powers))
        {
            var added = model.ModifyMaxEnergy(player, max) - max;
            if (added <= 0) continue;
            max += added;
            Capture.Emit(new Core.EnergyGained(Capture.ToActor(player), Capture.SourceOf(model.GetType()).ToString(), (double)added));
        }
    }

    /// Called for every creature starting its turn, after the game cleared (or kept) its block.
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterBlockCleared))]
    public static class BlockClearedAtTurnStart
    {
        public static void Prefix(Creature creature) => Capture.Safe("AfterBlockCleared", () =>
        {
            var actor = Capture.ToActor(creature);
            if (actor.Kind == ActorKind.Player) Capture.Emit(new BlockObserved(actor, creature.Block));
        });
    }

    /// A player died (removal not prevented by a revive effect).
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterDeath))]
    public static class Death
    {
        public static void Prefix(Creature creature, bool wasRemovalPrevented) => Capture.Safe("AfterDeath", () =>
        {
            // After a victory the game kills every player to close the run (v0.18.10): not a death.
            if (wasRemovalPrevented || !creature.IsPlayer || _runOver) return;
            Capture.Emit(new PlayerDied(Capture.ToActor(creature)));
            // Died with potions that could have been drunk in combat (spec §E0).
            var potions = creature.Player?.Potions.Count(p => p.Usage is PotionUsage.CombatOnly or PotionUsage.AnyTime) ?? 0;
            if (potions > 0)
                Capture.Emit(new MissedChance(Capture.ToActor(creature), "potion", new[] { potions.ToString(System.Globalization.CultureInfo.InvariantCulture) }, potions));
        });
    }

    /// The run has ended (its first OnEnded): later deaths and OnEnded calls are the game closing it.
    private static bool _runOver;

    /// Victory or defeat: the end-of-run summary (trophies, records).
    [HarmonyPatch(typeof(RunManager), nameof(RunManager.OnEnded))]
    public static class RunOver
    {
        public static void Prefix(bool isVictory) => Capture.Safe("RunManager.OnEnded", () =>
        {
            // A won run ends twice: WinRun, then the defeat the game sends when it kills the players (v0.18.10).
            if (_runOver) return;
            _runOver = true;
            RunHistoryPatches.Read(CurrentRun, ended: true);
            Capture.Emit(new RunEnded(isVictory));
            KikiMAIster.UI.RunSummary.OnRunEnded(isVictory);
        });
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCombatEnd))]
    public static class CombatEnd
    {
        public static void Prefix(ICombatState? combatState) => Capture.Safe("AfterCombatEnd", () =>
            EndCombat(combatState?.Players.Any(p => p.Creature.CurrentHp > 0) ?? true));
    }

    /// All players dead: the game never calls AfterCombatEnd.
    [HarmonyPatch(typeof(CombatManager), nameof(CombatManager.LoseCombat))]
    public static class CombatLost
    {
        public static void Prefix() => Capture.Safe("LoseCombat", () => EndCombat(false));
    }

    [HarmonyPatch(typeof(CombatHistory), nameof(CombatHistory.CardPlayStarted))]
    public static class CardPlayStart
    {
        public static void Prefix(CardPlay cardPlay) =>
            Capture.Safe("CardPlayStarted", () =>
            {
                Capture.CardPlayers.Push(cardPlay.Card.Owner);
                Capture.CardPlays.Push(cardPlay);
                Capture.Emit(new CardEvent(Capture.ToActor(cardPlay.Card.Owner), CardAction.Played, Repeat: cardPlay.PlayIndex > 0));
                BlockAlternatives.OnPlayStarted(cardPlay);
                PlayLog.PlayStarted(cardPlay);
            });
    }

    [HarmonyPatch(typeof(CombatHistory), nameof(CombatHistory.CardPlayFinished))]
    public static class CardPlayEnd
    {
        public static void Prefix() => Capture.Safe("CardPlayFinished", () =>
        {
            PlayLog.PlayFinished();
            if (Capture.CardPlayers.Count > 0) Capture.Emit(new CardPlayFinished(Capture.ToActor(Capture.CardPlayers.Pop())));
            if (Capture.CardPlays.Count > 0) Capture.CardPlays.Pop();
            BlockAlternatives.OnPlayFinished();
        });
    }

    /// A card or potion effect is over. A player who died during their own card never reaches CardPlayFinished: their plays
    /// are closed here, else they would stay "the player acting" for the rest of the fight (v0.17.38).
    [HarmonyPatch(typeof(CombatManager), nameof(CombatManager.EndCardOrPotionEffect))]
    public static class CardOrPotionEffectEnd
    {
        public static void Prefix(Player player) => Capture.Safe("EndCardOrPotionEffect", () =>
        {
            if (!player.Creature.IsDead) return;
            PlayLog.PlaysAbandoned();
            while (Capture.CardPlays.TryPeek(out var play) && Capture.PlayerOf(play) == player)
            {
                Capture.CardPlays.Pop();
                if (Capture.CardPlayers.Count > 0) Capture.Emit(new CardPlayFinished(Capture.ToActor(Capture.CardPlayers.Pop())));
                BlockAlternatives.OnPlayFinished();
            }
            if (Capture.PotionUser == player) Capture.PotionUser = null;
        });
    }

    // Effects other than block during a card play (spec §F, F4): draw, orb, created card.
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardDrawn))]
    public static class CardDrawn
    {
        public static void Prefix(CardModel card, bool fromHandDraw) => Capture.Safe("AfterCardDrawn", () =>
        {
            BlockAlternatives.OnEffect();
            BlockAlternatives.OnCardDrawn(card.Owner, fromHandDraw);
            // Drawn for an ally (Huddle Up…): help given by the player acting.
            if (!fromHandDraw && Capture.CurrentActingPlayer is { } giver && card.Owner != giver)
                Capture.Emit(new HelpGiven(Capture.ToActor(giver), "cards", 1));
            Capture.Emit(new CardEvent(Capture.ToActor(card.Owner), CardAction.Drawn));
        });
    }

    /// An enemy loses every power at once (v0.18.9, Zaille75): Cobaye and the other enemies with several forms start each form
    /// without the debuffs put on them, so the turns still queued in those debuffs credit nobody any more.
    [HarmonyPatch(typeof(Creature), nameof(Creature.RemoveAllPowersInternalExcept))]
    public static class PowersCleared
    {
        public static void Postfix(Creature __instance, IEnumerable<PowerModel> __result) => Capture.Safe("Creature.RemoveAllPowers", () =>
        {
            if (!__instance.IsEnemy || __result is not ICollection<PowerModel> removed || removed.Count == 0) return;
            Capture.Emit(new Core.PowersCleared(Capture.ToActor(__instance), removed.Select(p => Capture.IdOf(p)).ToList()));
        });
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardDiscarded))]
    public static class CardDiscarded
    {
        public static void Prefix(CardModel card) => Capture.Safe("AfterCardDiscarded", () =>
        {
            BlockAlternatives.OnCardDiscarded(card);
            Capture.Emit(new CardEvent(Capture.ToActor(card.Owner), CardAction.Discarded));
        });
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardExhausted))]
    public static class CardExhausted
    {
        public static void Prefix(CardModel card) => Capture.Safe("AfterCardExhausted", () =>
        {
            BlockAlternatives.OnCardEffect(card);
            Capture.Emit(new CardEvent(Capture.ToActor(card.Owner), CardAction.Exhausted));
        });
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterOrbChanneled))]
    public static class OrbChanneled
    {
        public static void Prefix(Player player) => Capture.Safe("AfterOrbChanneled", () =>
        {
            BlockAlternatives.OnEffect();
            if (Capture.CurrentActingPlayer is { } giver && giver != player) Capture.Emit(new HelpGiven(Capture.ToActor(giver), "orbs", 1));
        });
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardGeneratedForCombat))]
    public static class CardGenerated
    {
        public static void Prefix(CardModel card, Player? creator) => Capture.Safe("AfterCardGeneratedForCombat", () =>
        {
            BlockAlternatives.OnEffect();
            // Status and curse cards remember who created them (no creator: an enemy), for the damage they deal to their
            // holder (Brûlure…, v0.17.38). They are never cards given.
            if (card.Type is CardType.Status or CardType.Curse)
            {
                DamagePatches.StatusCreators.AddOrUpdate(card, new StrongBox<Player?>(creator));
                if (creator == null && ActingEnemy is { } enemy) DamagePatches.StatusEnemies.AddOrUpdate(card, enemy);
                PlayLog.Note($"{card.Title} created in {PlayLog.Who(card.Owner)}'s deck");
                return;
            }
            PlayLog.Note($"{card.Title} created for {PlayLog.Who(card.Owner)}");
            // A card created in another player's piles (Blade Symphony passes no creator: the player acting made it).
            var giver = creator ?? Capture.CurrentActingPlayer;
            // Âme sœur: the Soul copied into the ally's draw pile is "created" by that ally; it is the Soulbound
            // applier's gift (v0.17.1).
            if (GameBranch.IsBeta && SoulBinder(card) is { } binder) giver = binder; // Âme sœur: beta only
            if (giver != null && card.Owner != giver)
            {
                DamagePatches.GiftedCards.AddOrUpdate(card, giver);
                Capture.Emit(new HelpGiven(Capture.ToActor(giver), "cards", 1));
            }
        });
    }

    [BranchOnly, MethodImpl(MethodImplOptions.NoInlining)]
    private static Player? SoulBinder(CardModel card) =>
        card is Soul && card.Owner.Creature.GetPower<SoulboundPower>() is { } soulbound && soulbound.Applier?.Player is { } binder &&
        binder != card.Owner && Traverse.Create(soulbound).Property(ReflectionTargets.SoulboundAddingSoul.Name).GetValue<bool>()
            ? binder : null;

    // Side effects that make a block card more than block (Armaments upgrades, Gather Light gives stars).
    [HarmonyPatch(typeof(CardCmd), nameof(CardCmd.Upgrade), typeof(IEnumerable<CardModel>), typeof(CardPreviewStyle))]
    public static class CardUpgraded
    {
        public static void Prefix() => Capture.Safe("CardCmd.Upgrade", () => BlockAlternatives.OnEffect());
    }

    // More side effects (spec §F, F4 audit of every block card): another card moved between piles (Hologram, Cosmic
    // Indifference, Compact's transform…), energy gained (Constellation), Forge (Bulwark), Sly given (Hand Trick).
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardChangedPiles))]
    public static class CardMoved
    {
        public static void Prefix(CardModel card, PileType oldPile) => Capture.Safe("AfterCardChangedPiles", () =>
        {
            // A discard is judged by AfterCardDiscarded (v0.18.9: an effect only when something reacts to it).
            if (!BlockAlternatives.Discarding(card, oldPile)) BlockAlternatives.OnCardEffect(card);
            // A card brought back to an ally's hand (Tutor…); draws and created cards are counted by their own hooks.
            if (card.Pile?.Type == PileType.Hand && Capture.CurrentActingPlayer is { } giver && card.Owner != giver &&
                (oldPile is PileType.Discard or PileType.Exhaust || (oldPile == PileType.Draw && Capture.CardPlays.TryPeek(out var play) && play.Card.Id.Entry == "TUTOR"))) // Tutor: beta only
                Capture.Emit(new HelpGiven(Capture.ToActor(giver), "cards", 1));
        });
    }

    [HarmonyPatch(typeof(PlayerCmd), nameof(PlayerCmd.GainEnergy))]
    public static class EnergyGained
    {
        public static void Prefix(decimal amount, Player player) => Capture.Safe("PlayerCmd.GainEnergy", () =>
        {
            BlockAlternatives.OnEffect();
            BlockAlternatives.OnEnergyGiven(player, amount);
            PlayLog.Note($"{PlayLog.Who(player)} +{amount} energy");
            // Énergie gagnée (v0.18.5): whatever gave it (card, power, orb, relic, potion).
            Capture.Emit(new Core.EnergyGained(Capture.ToActor(player), Capture.SourceOf(Capture.CallingModelType()).ToString(), (double)amount));
            if (Capture.CurrentActingPlayer is { } giver && giver != player) Capture.Emit(new HelpGiven(Capture.ToActor(giver), "energy", (double)amount));
        });
    }

    /// Ruée (v0.18.6): checked before it plays the attacks of the hand.
    [HarmonyPatch(typeof(StampedePower), nameof(StampedePower.AfterAutoPostPlayPhaseEntered))]
    public static class Stampede
    {
        public static void Prefix(StampedePower __instance, Player player) => Capture.Safe("Stampede", () =>
        {
            if (player == __instance.Owner.Player) BlockAlternatives.OnStampede(player, __instance.Amount);
        });
    }

    /// A draw stopped by Pas de pioche (v0.18.6).
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterPreventingDraw))]
    public static class DrawPrevented
    {
        public static void Prefix() => Capture.Safe("AfterPreventingDraw", BlockAlternatives.OnDrawPrevented);
    }

    /// Cards created into the hand (v0.18.6): those that do not fit go to the discard pile.
    [HarmonyPatch(typeof(CardPileCmd), nameof(CardPileCmd.AddGeneratedCardsToCombat))]
    public static class CardsCreated
    {
        public static void Prefix(IEnumerable<CardModel> cards, PileType newPileType) => Capture.Safe("AddGeneratedCardsToCombat", () =>
        {
            if (newPileType != PileType.Hand) return;
            foreach (var g in cards.Where(c => c.Owner != null).GroupBy(c => c.Owner)) BlockAlternatives.OnCardsCreated(g.Key, g.Count());
        });
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterForge))]
    public static class Forged
    {
        public static void Prefix(decimal amount, Player forger) => Capture.Safe("AfterForge", () =>
        {
            BlockAlternatives.OnEffect();
            BlockAlternatives.OnForge(amount, forger);
        });
    }

    [HarmonyPatch(typeof(CardCmd), nameof(CardCmd.ApplySingleTurnSly))]
    public static class SlyGiven
    {
        public static void Prefix() => Capture.Safe("CardCmd.ApplySingleTurnSly", () => BlockAlternatives.OnEffect());
    }

    /// Choosing a card in hand to upgrade (Armaments) is a side effect even when nothing could be upgraded.
    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromHandForUpgrade))]
    public static class UpgradeChoice
    {
        public static void Prefix() => Capture.Safe("CardSelectCmd.FromHandForUpgrade", () => BlockAlternatives.OnEffect());
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterStarsGained))]
    public static class StarsGained
    {
        public static void Prefix(int amount, Player gainer) => Capture.Safe("AfterStarsGained", () =>
        {
            BlockAlternatives.OnEffect();
            PlayLog.Note($"{PlayLog.Who(gainer)} +{amount} stars");
        });
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.BeforePotionUsed))]
    public static class PotionStart
    {
        public static void Prefix(PotionModel potion, Creature? target) => Capture.Safe("BeforePotionUsed", () =>
        {
            PlayLog.Potion(potion, target);
            Capture.PotionUser = potion.Owner;
            Capture.PotionId = $"Potion:{Capture.IdOf(potion)}";
            Capture.Emit(new PotionUsed(Capture.ToActor(potion.Owner)));
            // A potion that could do nothing (v0.18.6): an error, in Gâché.
            if (potion.Owner?.Creature.CombatState != null && BlockAlternatives.UselessPotion(potion, target))
                Capture.Emit(new MissedChance(Capture.ToActor(potion.Owner), "potionUseless", new[] { Capture.PotionId }));
        });
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterPotionUsed))]
    public static class PotionEnd
    {
        public static void Prefix() => Capture.Safe("AfterPotionUsed", () => Capture.PotionUser = null);
    }
}
