using HarmonyLib;
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
        }
        catch (Exception e)
        {
            ModLog.Error("failed to initialize", e);
        }
    }
}
