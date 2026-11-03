using System.Reflection;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace KikiMAIster;

/// One DLL for both game branches (v1.1.0, spec 03-technical "Game branches"). The mod is compiled against the beta;
/// the few main branch differences are read from the game's own API at start-up.
public static class GameBranch
{
    /// CardPlay.Player only exists on the beta branch (v0.111.0; the main branch v0.107.1 has none).
    public static readonly bool IsBeta = typeof(CardPlay).GetProperty("Player") != null;

    /// "beta" or "main", for the log and F9 reports.
    public static string Name => IsBeta ? "beta" : "main";
}

/// Marks code that names game types or members of one branch only: call it only after a GameBranch.IsBeta check
/// (PatchCheck allows the missing references there and nowhere else). Keep these methods small, and not inlined, so the
/// runtime only resolves their references on the branch that has them.
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class BranchOnlyAttribute : Attribute;

/// Game calls whose signature differs between the branches (the beta passes a CardPlay too).
public static class GameApi
{
    private delegate decimal MainModifyDamage(IRunState runState, ICombatState? combatState, Creature? target, Creature? dealer,
        decimal damage, ValueProp props, CardModel? cardSource, ModifyDamageHookType hookType, CardPreviewMode previewMode,
        out IEnumerable<AbstractModel> modifiers);
    private delegate decimal MainModifier(AbstractModel model, Creature? target, decimal amount, ValueProp props, Creature? dealer,
        CardModel? cardSource);

    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    private static T? Make<T>(Type type, string name, int parameters) where T : Delegate
    {
        if (GameBranch.IsBeta) return null;
        var m = type.GetMethods(Any).SingleOrDefault(m => m.Name == name && m.GetParameters().Length == parameters);
        // An open-instance delegate on a virtual method calls the model's own override.
        return m == null ? null : (T)Delegate.CreateDelegate(typeof(T), m);
    }

    private static readonly MainModifyDamage? MainHook = Make<MainModifyDamage>(typeof(Hook), nameof(Hook.ModifyDamage), 10);
    private static readonly MainModifier? MainAdditive = Make<MainModifier>(typeof(AbstractModel), nameof(AbstractModel.ModifyDamageAdditive), 5);
    private static readonly MainModifier? MainMultiplicative = Make<MainModifier>(typeof(AbstractModel), nameof(AbstractModel.ModifyDamageMultiplicative), 5);

    public static decimal ModifyDamage(IRunState runState, ICombatState? combatState, Creature? target, Creature? dealer, decimal damage,
        ValueProp props, CardModel? cardSource, CardPlay? cardPlay, ModifyDamageHookType hookType, CardPreviewMode previewMode,
        out IEnumerable<AbstractModel> modifiers)
    {
        if (GameBranch.IsBeta)
            return BetaModifyDamage(runState, combatState, target, dealer, damage, props, cardSource, cardPlay, hookType, previewMode, out modifiers);
        return (MainHook ?? throw new MissingMethodException("Hook.ModifyDamage (main branch)"))(runState, combatState, target, dealer,
            damage, props, cardSource, hookType, previewMode, out modifiers);
    }

    public static decimal ModifyDamageAdditive(AbstractModel m, Creature? target, decimal amount, ValueProp props, Creature? dealer,
        CardModel? card, CardPlay? play) => GameBranch.IsBeta
        ? BetaAdditive(m, target, amount, props, dealer, card, play)
        : (MainAdditive ?? throw new MissingMethodException("AbstractModel.ModifyDamageAdditive (main branch)"))(m, target, amount, props, dealer, card);

    public static decimal ModifyDamageMultiplicative(AbstractModel m, Creature? target, decimal amount, ValueProp props, Creature? dealer,
        CardModel? card, CardPlay? play) => GameBranch.IsBeta
        ? BetaMultiplicative(m, target, amount, props, dealer, card, play)
        : (MainMultiplicative ?? throw new MissingMethodException("AbstractModel.ModifyDamageMultiplicative (main branch)"))(m, target, amount, props, dealer, card);

    /// Who plays the card: CardPlay.Player (beta), the card's owner (main).
    public static Player PlayerOf(CardPlay play) => GameBranch.IsBeta ? BetaPlayer(play) : play.Card.Owner;

    [BranchOnly, MethodImpl(MethodImplOptions.NoInlining)]
    private static decimal BetaModifyDamage(IRunState runState, ICombatState? combatState, Creature? target, Creature? dealer, decimal damage,
        ValueProp props, CardModel? cardSource, CardPlay? cardPlay, ModifyDamageHookType hookType, CardPreviewMode previewMode,
        out IEnumerable<AbstractModel> modifiers) =>
        Hook.ModifyDamage(runState, combatState, target, dealer, damage, props, cardSource, cardPlay, hookType, previewMode, out modifiers);

    [BranchOnly, MethodImpl(MethodImplOptions.NoInlining)]
    private static decimal BetaAdditive(AbstractModel m, Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? card,
        CardPlay? play) => m.ModifyDamageAdditive(target, amount, props, dealer, card, play);

    [BranchOnly, MethodImpl(MethodImplOptions.NoInlining)]
    private static decimal BetaMultiplicative(AbstractModel m, Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? card,
        CardPlay? play) => m.ModifyDamageMultiplicative(target, amount, props, dealer, card, play);

    [BranchOnly, MethodImpl(MethodImplOptions.NoInlining)]
    private static Player BetaPlayer(CardPlay play) => play.Player;
}
