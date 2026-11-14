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
    /// Beta v0.113.0 (2026-11-12): Hook.ShouldTakeExtraTurn tells which model gives the extra turn; the main branch is
    /// v0.111.0 (v1.2.0). The old main branch v0.107.1 is no longer supported (1.2.2).
    public static readonly bool IsV113 =
        typeof(Hook).GetMethods().Any(m => m.Name == nameof(Hook.ShouldTakeExtraTurn) && m.GetParameters().Length == 3);

    /// "beta" or "main", for the log and F9 reports.
    public static string Name => IsV113 ? "beta" : "main";
}

/// Marks code that names game types or members of one branch only: call it only after a GameBranch.IsV113 check
/// (PatchCheck allows the missing references there and nowhere else). Keep these methods small, and not inlined, so the
/// runtime only resolves their references on the branch that has them.
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class BranchOnlyAttribute : Attribute;

/// Game calls whose signature differs between the branches.
public static class GameApi
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    private delegate bool OldExtraTurn(ICombatState combatState, Player player);
    private static readonly OldExtraTurn? OldShouldTakeExtraTurn = GameBranch.IsV113 ? null :
        typeof(Hook).GetMethods(Any).SingleOrDefault(m => m.Name == nameof(Hook.ShouldTakeExtraTurn) && m.GetParameters().Length == 2) is { } old
            ? (OldExtraTurn)Delegate.CreateDelegate(typeof(OldExtraTurn), old) : null;

    /// Hook.ShouldTakeExtraTurn: v0.113.0 adds an out parameter (the model giving the turn).
    public static bool ShouldTakeExtraTurn(ICombatState combat, Player player) => GameBranch.IsV113
        ? V113ShouldTakeExtraTurn(combat, player)
        : (OldShouldTakeExtraTurn ?? throw new MissingMethodException("Hook.ShouldTakeExtraTurn (before v0.113.0)"))(combat, player);

    [BranchOnly, MethodImpl(MethodImplOptions.NoInlining)]
    private static bool V113ShouldTakeExtraTurn(ICombatState combat, Player player) => Hook.ShouldTakeExtraTurn(combat, player, out _);
}
