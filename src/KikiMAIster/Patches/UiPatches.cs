using Godot;
using HarmonyLib;
using KikiMAIster.UI;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace KikiMAIster.Patches;

public static class UiPatches
{
    [HarmonyPatch(typeof(NGame), nameof(NGame._Ready))]
    public static class AttachOverlay
    {
        public static void Postfix(NGame __instance) => Capture.Safe("NGame._Ready", () => Overlay.Attach(__instance));
    }

    /// Leaving the rewards for the map closes the summary that opened at the end of the combat.
    [HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.Open))]
    public static class MapOpened
    {
        public static void Postfix() => Capture.Safe("NMapScreen.Open", CombatSummary.HideAutoShown);
    }

    /// The character-select screen (solo or co-op lobby): the Compositions window reads its mode, players and ascension.
    [HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.OnSubmenuOpened))]
    public static class LobbyOpened
    {
        public static void Postfix(NCharacterSelectScreen __instance) =>
            Capture.Safe("CharacterSelect.Opened", () => CompositionWindow.Lobby = new WeakReference<NCharacterSelectScreen>(__instance));
    }

    [HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.OnSubmenuClosed))]
    public static class LobbyClosed
    {
        public static void Postfix() => Capture.Safe("CharacterSelect.Closed", () => CompositionWindow.Lobby = null);
    }

    [HarmonyPatch(typeof(NGame), nameof(NGame._Input))]
    public static class Hotkey
    {
        public static void Postfix(InputEvent inputEvent) => Capture.Safe("NGame._Input", () =>
        {
            // Any input (even a mouse move) proves the game is running on the main thread: attach if needed.
            Overlay.EnsureAttached("input");
            if (inputEvent is InputEventMouseButton { Pressed: true } click) Overlay.RaiseAt(click.Position);
            if (inputEvent is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F7 }) Overlay.Toggle();
            // F8: group compositions, anywhere (the lobby included).
            if (inputEvent is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F8 }) CompositionWindow.Toggle();
        });
    }
}
