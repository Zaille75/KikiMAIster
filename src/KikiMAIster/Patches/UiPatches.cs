using Godot;
using HarmonyLib;
using KikiMAIster.UI;
using MegaCrit.Sts2.Core.Nodes;

namespace KikiMAIster.Patches;

public static class UiPatches
{
    [HarmonyPatch(typeof(NGame), nameof(NGame._Ready))]
    public static class AttachOverlay
    {
        public static void Postfix(NGame __instance) => Capture.Safe("NGame._Ready", () => Overlay.Attach(__instance));
    }

    [HarmonyPatch(typeof(NGame), nameof(NGame._Input))]
    public static class Hotkey
    {
        public static void Postfix(InputEvent inputEvent) => Capture.Safe("NGame._Input", () =>
        {
            if (inputEvent is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F7 }) Overlay.Toggle();
        });
    }
}
