using HarmonyLib;
using KikiMAIster.Core;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace KikiMAIster.Patches;

/// Run / combat / round boundaries, and who is currently acting (card play, potion).
public static class CombatFlowPatches
{
    private static WeakReference<IRunState>? _currentRun;
    private static bool _combatOpen;
    private static string? _runKey;
    private static int _floor;

    /// Ends the combat once, whether it was won (AfterCombatEnd) or lost (LoseCombat).
    private static void EndCombat(bool victory)
    {
        if (!_combatOpen) return;
        _combatOpen = false;
        DamagePatches.FlushPending();
        Capture.Emit(new CombatEnded(victory));
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
        if (_currentRun != null && _currentRun.TryGetTarget(out var run) && ReferenceEquals(run, runState)) return;
        _currentRun = new WeakReference<IRunState>(runState);
        _runKey = null;
        Capture.Emit(new RunStarted());
        Capture.Safe("run key", () => _runKey = RunStore.KeyOf(runState));
        if (_runKey != null) RunStore.TryRestore(_runKey, _floor, Capture.Engine);
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
            Capture.PotionUser = null;
            DamagePatches.Reset();
            BlockAlternatives.Reset();
            _combatOpen = true;
            KikiMAIster.UI.Overlay.EnsureAttached("combat start");
            KikiMAIster.UI.CombatSummary.HideAutoShown();
            var players = combatState?.Players ?? runState.Players;
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
            Capture.Emit(new RoundStarted(combatState.RoundNumber));
        });
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.BeforeSideTurnEnd))]
    public static class TurnEnd
    {
        public static void Prefix(ICombatState combatState, CombatSide side) => Capture.Safe("BeforeSideTurnEnd", () =>
        {
            if (side == CombatSide.Player) BlockAlternatives.ResolveTurn(combatState);
        });
    }

    /// Hand drawn: the front hand of the turn (missed lethal check).
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterPlayerTurnStart))]
    public static class PlayerTurnStart
    {
        public static void Prefix(Player player) => Capture.Safe("AfterPlayerTurnStart", () => BlockAlternatives.OnTurnStarted(player));
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
            if (!wasRemovalPrevented && creature.IsPlayer) Capture.Emit(new PlayerDied(Capture.ToActor(creature)));
        });
    }

    /// Victory or defeat: the end-of-run summary (trophies, records).
    [HarmonyPatch(typeof(RunManager), nameof(RunManager.OnEnded))]
    public static class RunOver
    {
        public static void Prefix(bool isVictory) => Capture.Safe("RunManager.OnEnded", () =>
        {
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
                Capture.Emit(new CardEvent(Capture.ToActor(cardPlay.Card.Owner), CardAction.Played));
                BlockAlternatives.OnPlayStarted(cardPlay);
            });
    }

    [HarmonyPatch(typeof(CombatHistory), nameof(CombatHistory.CardPlayFinished))]
    public static class CardPlayEnd
    {
        public static void Prefix() => Capture.Safe("CardPlayFinished", () =>
        {
            if (Capture.CardPlayers.Count > 0) Capture.CardPlayers.Pop();
            BlockAlternatives.OnPlayFinished();
        });
    }

    // Effects other than block during a card play (spec §F, F4): draw, orb, created card.
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardDrawn))]
    public static class CardDrawn
    {
        public static void Prefix(CardModel card) => Capture.Safe("AfterCardDrawn", () =>
        {
            BlockAlternatives.OnEffect();
            Capture.Emit(new CardEvent(Capture.ToActor(card.Owner), CardAction.Drawn));
        });
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardDiscarded))]
    public static class CardDiscarded
    {
        public static void Prefix(CardModel card) => Capture.Safe("AfterCardDiscarded", () =>
        {
            BlockAlternatives.OnCardEffect(card);
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
        public static void Prefix() => Capture.Safe("AfterOrbChanneled", () => BlockAlternatives.OnEffect());
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardGeneratedForCombat))]
    public static class CardGenerated
    {
        public static void Prefix() => Capture.Safe("AfterCardGeneratedForCombat", () => BlockAlternatives.OnEffect());
    }

    // Side effects that make a block card more than block (Armaments upgrades, Gather Light gives stars).
    [HarmonyPatch(typeof(CardCmd), nameof(CardCmd.Upgrade), typeof(IEnumerable<CardModel>), typeof(CardPreviewStyle))]
    public static class CardUpgraded
    {
        public static void Prefix() => Capture.Safe("CardCmd.Upgrade", () => BlockAlternatives.OnEffect());
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
        public static void Prefix() => Capture.Safe("AfterStarsGained", () => BlockAlternatives.OnEffect());
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.BeforePotionUsed))]
    public static class PotionStart
    {
        public static void Prefix(PotionModel potion) => Capture.Safe("BeforePotionUsed", () =>
        {
            Capture.PotionUser = potion.Owner;
            Capture.PotionId = $"Potion:{Capture.IdOf(potion)}";
            Capture.Emit(new PotionUsed(Capture.ToActor(potion.Owner)));
        });
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterPotionUsed))]
    public static class PotionEnd
    {
        public static void Prefix() => Capture.Safe("AfterPotionUsed", () => Capture.PotionUser = null);
    }
}
