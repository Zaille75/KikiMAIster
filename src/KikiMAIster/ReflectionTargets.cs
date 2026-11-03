using System.Reflection;

namespace KikiMAIster;

/// Private game members the mod reads by name (not reachable through Harmony patches). PatchCheck (build) checks each one
/// resolves in the game DLLs, and the mod logs an error at start-up if one is missing after a game update (v0.17.40).
public static class ReflectionTargets
{
    /// TypeName: the game type's full name (text, so that listing the targets loads nothing: PatchCheck reads them without
    /// running the game's code). Parameters: the method's parameter count; -1 for a property.
    public sealed record Target(string TypeName, string Name, int Parameters = -1)
    {
        public Type? Type => System.Type.GetType($"{TypeName}, sts2");
        /// Only on the beta branch: not listed on the main branch.
        public bool BetaOnly { get; init; }
        public override string ToString() => $"{TypeName}.{Name}";
    }

    /// The beta passes a CardPlay too (10 parameters, main branch 9).
    public static readonly Target ModifyDamageInternal = new("MegaCrit.Sts2.Core.Hooks.Hook", "ModifyDamageInternal", GameBranch.IsBeta ? 10 : 9);
    public static readonly Target RestSiteLocalPlayer = new("MegaCrit.Sts2.Core.Multiplayer.Game.RestSiteSynchronizer", "LocalPlayer") { BetaOnly = true };
    public static readonly Target SoulboundAddingSoul = new("MegaCrit.Sts2.Core.Models.Powers.SoulboundPower", "IsAddingSoul") { BetaOnly = true };
    public static readonly Target PenNibAttackToDouble = new("MegaCrit.Sts2.Core.Models.Relics.PenNib", "AttackToDouble");

    public static IEnumerable<Target> All => typeof(ReflectionTargets).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(Target)).Select(f => (Target)f.GetValue(null)!).Where(t => GameBranch.IsBeta || !t.BetaOnly);

    /// The member, or null when the game no longer has it (one method of that name and parameter count, or the property).
    public static MemberInfo? Find(Target t)
    {
        const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        if (t.Type is not { } type) return null;
        if (t.Parameters < 0) return type.GetProperty(t.Name, any);
        var methods = type.GetMethods(any).Where(m => m.Name == t.Name && m.GetParameters().Length == t.Parameters).ToList();
        return methods.Count == 1 ? methods[0] : null;
    }

    public static MethodInfo? Method(Target t) => Find(t) as MethodInfo;

    /// At start-up: one error line per missing member (the stat that uses it stays empty, nothing breaks).
    public static void CheckAll()
    {
        foreach (var t in All)
            if (Find(t) == null) ModLog.Error($"game member not found (game update?): {t} — the stat using it is disabled");
    }
}
