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
            // Mods load during the game's startup, after NGame._Ready: attach the overlay on the next frame.
            Callable.From(() => Capture.Safe("attach overlay", () =>
            {
                if (NGame.Instance != null) Overlay.Attach(NGame.Instance);
                else ModLog.Error("NGame.Instance is null, overlay not attached");
            })).CallDeferred();
        }
        catch (Exception e)
        {
            ModLog.Error("failed to initialize", e);
        }
    }
}
