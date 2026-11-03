using Godot;
using HarmonyLib;
using KikiMAIster.UI;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Modding;

namespace KikiMAIster;

[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    public const string Id = "KikiMAIster";

    public static void Initialize()
    {
        try
        {
            PlayLog.Install();
            // Each patch class on its own (v1.1.0): one that fails is logged, the others still apply.
            var harmony = new Harmony("kikimaister");
            int applied = 0, failed = 0;
            foreach (var type in AccessTools.GetTypesFromAssembly(typeof(ModEntry).Assembly))
            {
                try
                {
                    var processor = harmony.CreateClassProcessor(type);
                    if (processor.Patch() is { Count: > 0 }) applied++;
                }
                catch (Exception e)
                {
                    failed++;
                    ModLog.Error($"patch {type.FullName} not applied", e);
                }
            }
            ModLog.Info($"loaded (v{typeof(ModEntry).Assembly.GetName().Version}, game branch {GameBranch.Name}, {applied} patches" +
                        (failed > 0 ? $", {failed} failed)" : ")"));
            ReflectionTargets.CheckAll();
            // Mods load during the game's startup, after NGame._Ready. The overlay is attached on the first
            // input or combat start (see Overlay.EnsureAttached): a deferred call from here never ran in v0.2.0.
            ModLog.Info($"main thread: {OS.GetThreadCallerId() == OS.GetMainThreadId()}, NGame ready: {NGame.Instance != null}");
        }
        catch (Exception e)
        {
            ModLog.Error("failed to initialize", e);
        }
    }
}
