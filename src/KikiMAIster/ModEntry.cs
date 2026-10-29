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
            new Harmony("kikimaister").PatchAll(typeof(ModEntry).Assembly);
            ModLog.Info($"loaded (v{typeof(ModEntry).Assembly.GetName().Version})");
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
