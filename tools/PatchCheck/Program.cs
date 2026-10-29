using System.Reflection;
using System.Runtime.Loader;
// Loads the mod + game DLLs and checks that every [HarmonyPatch] target resolves to exactly one method.
var lib = Path.GetFullPath(args[0]); var mod = Path.GetFullPath(args[1]);
AssemblyLoadContext.Default.Resolving += (ctx, name) => { var p = Path.Combine(lib, name.Name + ".dll"); return File.Exists(p) ? ctx.LoadFromAssemblyPath(p) : null; };
var harmony = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(lib, "0Harmony.dll"));
var attrType = harmony.GetType("HarmonyLib.HarmonyPatch")!;
var modAsm = AssemblyLoadContext.Default.LoadFromAssemblyPath(mod);
int ok = 0, bad = 0;
foreach (var t in modAsm.GetTypes())
{
    var attrs = t.GetCustomAttributes(attrType, false);
    if (attrs.Length == 0) continue;
    Type? decl = null; string? name = null; Type[]? argTypes = null;
    foreach (var a in attrs)
    {
        var info = attrType.GetField("info")!.GetValue(a)!;
        var it = info.GetType();
        decl ??= (Type?)it.GetField("declaringType")!.GetValue(info);
        name ??= (string?)it.GetField("methodName")!.GetValue(info);
        argTypes ??= (Type[]?)it.GetField("argumentTypes")!.GetValue(info);
    }
    var methods = decl!.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        .Where(m => m.Name == name && (argTypes == null || m.GetParameters().Select(p => p.ParameterType).SequenceEqual(argTypes))).ToList();
    var prefix = t.GetMethod("Prefix") ?? t.GetMethod("Postfix");
    var target = methods.Count == 1 ? methods[0] : null;
    var missingParams = target == null ? new List<string>() : prefix!.GetParameters().Where(p => !p.Name!.StartsWith("__") && !target.GetParameters().Any(q => q.Name == p.Name)).Select(p => p.Name!).ToList();
    if (methods.Count == 1 && missingParams.Count == 0) { ok++; Console.WriteLine($"OK   {t.Name} -> {decl.Name}.{name}"); }
    else { bad++; Console.WriteLine($"FAIL {t.Name} -> {decl.Name}.{name}: {methods.Count} matches, unknown params [{string.Join(",", missingParams)}]"); }
}
// Private game members the mod reads by name (KikiMAIster.ReflectionTargets): each must still exist.
var targets = modAsm.GetType("KikiMAIster.ReflectionTargets");
if (targets == null) { bad++; Console.WriteLine("FAIL ReflectionTargets class not found in the mod"); }
else
{
    var find = targets.GetMethod("Find")!;
    var all = (System.Collections.IEnumerable)targets.GetProperty("All")!.GetValue(null)!;
    foreach (var t in all)
    {
        if (find.Invoke(null, new[] { t }) != null) { ok++; Console.WriteLine($"OK   reflection {t}"); }
        else { bad++; Console.WriteLine($"FAIL reflection {t}: not found in the game"); }
    }
}
Console.WriteLine($"{ok} ok, {bad} failed");
