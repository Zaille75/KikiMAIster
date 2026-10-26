using HarmonyLib;
using KikiMAIster.Core;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace KikiMAIster.Patches;

/// Run / combat / round boundaries, and who is currently acting (card play, potion).
public static class CombatFlowPatches
{
    private static WeakReference<IRunState>? _currentRun;

    [HarmonyPatch(typeof(Hook), nameof(Hook.BeforeCombatStart))]
    public static class CombatStart
    {
        public static void Prefix(IRunState runState, ICombatState? combatState) => Capture.Safe("BeforeCombatStart", () =>
        {
            if (_currentRun == null || !_currentRun.TryGetTarget(out var run) || !ReferenceEquals(run, runState))
            {
                _currentRun = new WeakReference<IRunState>(runState);
                Capture.Emit(new RunStarted());
            }
            Capture.CardPlayers.Clear();
            Capture.PotionUser = null;
            DamagePatches.Reset();
            var players = combatState?.Players ?? runState.Players;
            Capture.Emit(new CombatStarted(players.Select(p =>
                new PlayerInfo(p.NetId, p.Creature.Name, p.Character.Id.Entry)).ToList()));
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

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCombatEnd))]
    public static class CombatEnd
    {
        public static void Prefix(ICombatState? combatState) => Capture.Safe("AfterCombatEnd", () =>
        {
            DamagePatches.FlushPending();
            var victory = combatState?.Players.Any(p => p.Creature.CurrentHp > 0) ?? true;
            Capture.Emit(new CombatEnded(victory));
            Summary.LogCombat(Capture.Engine);
        });
    }

    [HarmonyPatch(typeof(CombatHistory), nameof(CombatHistory.CardPlayStarted))]
    public static class CardPlayStart
    {
        public static void Prefix(CardPlay cardPlay) =>
            Capture.Safe("CardPlayStarted", () => Capture.CardPlayers.Push(cardPlay.Card.Owner));
    }

    [HarmonyPatch(typeof(CombatHistory), nameof(CombatHistory.CardPlayFinished))]
    public static class CardPlayEnd
    {
        public static void Prefix() => Capture.Safe("CardPlayFinished", () =>
        {
            if (Capture.CardPlayers.Count > 0) Capture.CardPlayers.Pop();
        });
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.BeforePotionUsed))]
    public static class PotionStart
    {
        public static void Prefix(PotionModel potion) => Capture.Safe("BeforePotionUsed", () => Capture.PotionUser = potion.Owner);
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterPotionUsed))]
    public static class PotionEnd
    {
        public static void Prefix() => Capture.Safe("AfterPotionUsed", () => Capture.PotionUser = null);
    }
}
