using System.Runtime.CompilerServices;
using KikiMAIster.Core;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace KikiMAIster.Patches;

/// Decides, at the end of each player turn, whether each block card played that turn had a real alternative:
/// an attack that was in hand when the block card was played, was never played, and could be paid with the
/// block card's energy + the energy left at the end of the turn (spec 01-metrics §F, wasted vs useless).
public static class BlockAlternatives
{
    private sealed class Play
    {
        public required int Id { get; init; }
        public required Player Owner { get; init; }
        public required int EnergySpent { get; init; }
        public required List<CardModel> AttacksInHand { get; init; }
        public bool GaveBlock { get; set; }
    }

    private static readonly ConditionalWeakTable<CardPlay, Play> Plays = new();
    private static readonly List<Play> ThisTurn = new();
    private static int _nextId;

    public static void Reset() => ThisTurn.Clear();

    /// Card play starts: remember the attacks in the player's hand. Auto-played cards were not a choice.
    public static void OnPlayStarted(CardPlay cardPlay)
    {
        if (cardPlay.IsAutoPlay) return;
        var hand = cardPlay.Player.PlayerCombatState?.Hand.Cards;
        if (hand == null) return;
        var play = new Play
        {
            Id = ++_nextId,
            Owner = cardPlay.Player,
            EnergySpent = cardPlay.Resources.EnergySpent,
            AttacksInHand = hand.Where(c => c != cardPlay.Card && c.Type == CardType.Attack).ToList()
        };
        Plays.AddOrUpdate(cardPlay, play);
        ThisTurn.Add(play);
    }

    /// The play id to attach to block gained from this card play (0 if not tracked).
    public static int PlayIdFor(CardPlay cardPlay)
    {
        if (!Plays.TryGetValue(cardPlay, out var play)) return 0;
        play.GaveBlock = true;
        return play.Id;
    }

    /// End of the players' turn: each attack can be the alternative of only one block card.
    public static void ResolveTurn()
    {
        var used = new HashSet<CardModel>();
        foreach (var play in ThisTurn.Where(p => p.GaveBlock))
        {
            var state = play.Owner.PlayerCombatState;
            var budget = play.EnergySpent + (state?.Energy ?? 0);
            var alternative = state == null ? null : play.AttacksInHand.FirstOrDefault(c =>
                !used.Contains(c) && state.Hand.Cards.Contains(c) && Cost(c) <= budget && PlayableExceptEnergy(c));
            if (alternative != null) used.Add(alternative);
            Capture.Emit(new BlockPlayResolved(play.Id, alternative != null));
        }
        ThisTurn.Clear();
    }

    private static int Cost(CardModel card) => Math.Max(0, card.EnergyCost.GetWithModifiers(CostModifiers.All));

    private static bool PlayableExceptEnergy(CardModel card)
    {
        card.CanPlay(out var reason, out _);
        return (reason & ~(UnplayableReason.EnergyCostTooHigh | UnplayableReason.StarCostTooHigh)) == UnplayableReason.None;
    }
}
