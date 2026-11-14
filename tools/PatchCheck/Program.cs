using System.Reflection;
using System.Runtime.Loader;
// Loads the mod + game DLLs and checks that every [HarmonyPatch] target resolves to exactly one method.
var lib = Path.GetFullPath(args[0]); var mod = Path.GetFullPath(args[1]);
AssemblyLoadContext.Default.Resolving += (ctx, name) =>
{
    var p = Path.Combine(lib, name.Name + ".dll");
    if (File.Exists(p)) return ctx.LoadFromAssemblyPath(p);
    return name.Name == "Sentry.Godot" ? SentryStub(name) : null;
};
// sts2.dll's module initializer calls Sentry.Godot (not in lib/): the mod's branch checks touch game types, so an empty
// stand-in lets it run here.
static Assembly SentryStub(AssemblyName name)
{
    var asm = new System.Reflection.Emit.PersistedAssemblyBuilder(name, typeof(object).Assembly);
    var type = asm.DefineDynamicModule("Sentry.Godot").DefineType("Sentry.Godot.Internal.SentryGodotInitializer",
        TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
    type.DefineMethod("AutoInit", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes).GetILGenerator()
        .Emit(System.Reflection.Emit.OpCodes.Ret);
    type.CreateType();
    var bytes = new MemoryStream();
    asm.Save(bytes);
    bytes.Position = 0;
    return AssemblyLoadContext.Default.LoadFromStream(bytes);
}
var harmony = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(lib, "0Harmony.dll"));
var attrType = harmony.GetType("HarmonyLib.HarmonyPatch")!;
var modAsm = AssemblyLoadContext.Default.LoadFromAssemblyPath(mod);
int ok = 0, bad = 0;
foreach (var t in modAsm.GetTypes())
{
    var attrs = t.GetCustomAttributes(attrType, false);
    if (attrs.Length == 0) continue;
    // Prepare() false: Harmony skips the class on this game branch (v1.1.0).
    if (t.GetMethod("Prepare", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, Type.EmptyTypes) is { } prepare &&
        prepare.Invoke(null, null) is false)
    { Console.WriteLine($"SKIP {t.Name} (not for this game branch)"); continue; }
    var prefix0 = t.GetMethod("Prefix") ?? t.GetMethod("Postfix");
    // TargetMethod(): the class picks its method itself.
    if (t.GetMethod("TargetMethod", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, Type.EmptyTypes) is { } pick)
    {
        var m = pick.Invoke(null, null) as MethodBase;
        var unknown = m == null ? new List<string>() : prefix0!.GetParameters().Where(p => !p.Name!.StartsWith("__") && !m.GetParameters().Any(q => q.Name == p.Name)).Select(p => p.Name!).ToList();
        if (m != null && unknown.Count == 0) { ok++; Console.WriteLine($"OK   {t.Name} -> {m.DeclaringType!.Name}.{m.Name} (TargetMethod)"); }
        else { bad++; Console.WriteLine($"FAIL {t.Name}: TargetMethod {(m == null ? "found nothing" : "unknown params [" + string.Join(",", unknown) + "]")}"); }
        continue;
    }
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

// GameApi's main branch delegates (made by reflection at start-up): each must be found on the main branch (v0.111.0).
{
    var api = modAsm.GetType("KikiMAIster.GameApi")!;
    var beta = (bool)modAsm.GetType("KikiMAIster.GameBranch")!.GetField("IsV113")!.GetValue(null)!;
    Console.WriteLine($"INFO game branch detected: {(beta ? "beta" : "main")}");
    foreach (var f in api.GetFields(BindingFlags.NonPublic | BindingFlags.Static).Where(f => typeof(Delegate).IsAssignableFrom(f.FieldType)))
    {
        object? value;
        try { value = f.GetValue(null); }
        catch (TypeInitializationException e) { bad++; Console.WriteLine($"FAIL GameApi start-up: {e.InnerException?.Message}"); break; }
        if (beta || value != null) { ok++; Console.WriteLine($"OK   GameApi.{f.Name}"); }
        else { bad++; Console.WriteLine($"FAIL GameApi.{f.Name}: game method not found on the main branch"); }
    }
}

// One build for both game branches (v1.1.0): every game type / member the mod's code names must exist in this game
// build, except inside methods marked [BranchOnly] (called only after a runtime branch check). A missing one would throw
// MissingMethodException / TypeLoadException when the method using it runs.
{
    using var fs = File.OpenRead(mod);
    using var pe = new System.Reflection.PortableExecutable.PEReader(fs);
    var md = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
    var module = modAsm.ManifestModule;
    var missing = new List<(int token, string what)>();
    void Try(int token, Func<string> describe)
    {
        try { module.ResolveMember(token); }
        catch (ArgumentException) { } // needs generic arguments (open generic context): its game types are checked as type references
        catch (Exception e) when (e is MissingMemberException or TypeLoadException or FileNotFoundException)
        { missing.Add((token, describe())); }
    }
    string TypeRefName(System.Reflection.Metadata.TypeReferenceHandle h)
    { var r = md.GetTypeReference(h); return $"{md.GetString(r.Namespace)}.{md.GetString(r.Name)}"; }
    foreach (var h in md.TypeReferences)
    {
        var token = System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(h);
        try { module.ResolveType(token); } catch (Exception e) when (e is TypeLoadException or FileNotFoundException) { missing.Add((token, "type " + TypeRefName(h))); }
    }
    foreach (var h in md.MemberReferences)
    {
        var r = md.GetMemberReference(h);
        var parent = r.Parent.Kind == System.Reflection.Metadata.HandleKind.TypeReference ? TypeRefName((System.Reflection.Metadata.TypeReferenceHandle)r.Parent) : r.Parent.Kind.ToString();
        Try(System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(h), () => $"{parent}.{md.GetString(r.Name)}");
    }
    for (var i = 1; i <= System.Reflection.Metadata.Ecma335.MetadataReaderExtensions.GetTableRowCount(md, System.Reflection.Metadata.Ecma335.TableIndex.MethodSpec); i++)
    {
        var spec = md.GetMethodSpecification(System.Reflection.Metadata.Ecma335.MetadataTokens.MethodSpecificationHandle(i));
        string Describe()
        {
            if (spec.Method.Kind != System.Reflection.Metadata.HandleKind.MemberReference) return "generic method (mod's own)";
            var r = md.GetMemberReference((System.Reflection.Metadata.MemberReferenceHandle)spec.Method);
            var parent = r.Parent.Kind == System.Reflection.Metadata.HandleKind.TypeReference ? TypeRefName((System.Reflection.Metadata.TypeReferenceHandle)r.Parent) : r.Parent.Kind.ToString();
            return $"generic {parent}.{md.GetString(r.Name)}<…>";
        }
        Try(0x2B000000 | i, Describe);
    }
    // Which mod methods use each missing token (IL scan for the 4 token bytes after an opcode).
    bool BranchOnly(MethodBase m)
    {
        static bool Marked(MemberInfo x) => x.GetCustomAttributes(false).Any(a => a.GetType().Name == "BranchOnlyAttribute");
        if (Marked(m)) return true;
        for (var t = m.DeclaringType; t != null; t = t.DeclaringType) if (Marked(t)) return true;
        // Lambdas / local functions: <Parent>b__… or <Parent>g__…, in the type or a nested closure class.
        var name = m.Name.StartsWith('<') ? m.Name[1..m.Name.IndexOf('>')] : null;
        for (var t = m.DeclaringType; name != null && t != null; t = t.DeclaringType)
            foreach (var p in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                if (p.Name == name && Marked(p)) return true;
        return false;
    }
    var bodies = new List<(MethodBase m, byte[] il)>();
    foreach (var t in modAsm.GetTypes())
        foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly).Cast<MethodBase>()
                     .Concat(t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)))
        {
            try { if (m.GetMethodBody()?.GetILAsByteArray() is { } il) bodies.Add((m, il)); }
            catch (TypeLoadException e) // a local variable of a type this game build lacks: read its IL from the file
            {
                if (BranchOnly(m)) { ok++; Console.WriteLine($"OK   branch-only locals in {t.Name}.{m.Name}"); }
                else { bad++; Console.WriteLine($"FAIL {t.Name}.{m.Name} has a local of a missing type: {e.TypeName}"); }
                var def = md.GetMethodDefinition((System.Reflection.Metadata.MethodDefinitionHandle)System.Reflection.Metadata.Ecma335.MetadataTokens.EntityHandle(m.MetadataToken));
                if (def.RelativeVirtualAddress != 0) bodies.Add((m, System.Reflection.Metadata.PEReaderExtensions.GetMethodBody(pe, def.RelativeVirtualAddress).GetILBytes()!));
            }
        }
    static bool Uses(byte[] il, int token)
    {
        var b = BitConverter.GetBytes(token);
        for (var i = 1; i + 4 <= il.Length; i++)
            if (il[i] == b[0] && il[i + 1] == b[1] && il[i + 2] == b[2] && il[i + 3] == b[3]) return true;
        return false;
    }
    foreach (var (token, what) in missing)
    {
        var users = bodies.Where(x => Uses(x.il, token)).Select(x => x.m).ToList();
        var unguarded = users.Where(m => !BranchOnly(m)).ToList();
        if (users.Count > 0 && unguarded.Count == 0) { ok++; Console.WriteLine($"OK   branch-only {what}"); continue; }
        // A type used only in signatures of [BranchOnly] methods / other missing members' parents: no body uses it.
        if (users.Count == 0 && what.StartsWith("type ")) { Console.WriteLine($"INFO branch-only {what} (no direct use in a method body)"); continue; }
        bad++;
        Console.WriteLine($"FAIL missing in this game build: {what}, used by {(unguarded.Count == 0 ? "(unknown)" : string.Join(", ", unguarded.Select(m => m.DeclaringType!.Name + "." + m.Name)))}");
    }
}
Console.WriteLine($"{ok} ok, {bad} failed");
