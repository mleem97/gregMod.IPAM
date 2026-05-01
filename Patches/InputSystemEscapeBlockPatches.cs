using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine.InputSystem.Controls;

namespace DHCPSwitches;

/// <summary>
/// While IPAM is open, games still read <see cref="Keyboard.escapeKey"/> directly for pause / menu toggle.
/// Postfixes strip Escape button presses/releases so only our overlay handles Escape (after we snapshot it in
/// <see cref="IPAMOverlay.InputSystemAfterUpdateCaptureEscape"/>).
/// </summary>
internal static class InputSystemEscapeBlockPatches
{
    internal static void TryApply(HarmonyLib.Harmony harmonyInstance)
    {
        foreach (var nested in typeof(InputSystemEscapeBlockPatches).GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Static))
        {
            try
            {
                harmonyInstance.CreateClassProcessor(nested).Patch();
            }
            catch (Exception ex)
            {
                ModLogging.Warning($"DHCPSwitches: Input System escape patch {nested.Name} failed: {ex.Message}");
            }
        }
    }

    private static bool ShouldStripEscapeButton(ButtonControl control)
    {
        if (!IPAMOverlay.IsVisible || control == null)
        {
            return false;
        }

        var path = control.path;
        return !string.IsNullOrEmpty(path)
            && path.IndexOf("/escape", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    [HarmonyPatch(typeof(ButtonControl), nameof(ButtonControl.wasPressedThisFrame), MethodType.Getter)]
    private static class EscapeWasPressedPatch
    {
        private static void Postfix(ButtonControl __instance, ref bool __result)
        {
            if (__result && ShouldStripEscapeButton(__instance))
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(ButtonControl), nameof(ButtonControl.wasReleasedThisFrame), MethodType.Getter)]
    private static class EscapeWasReleasedPatch
    {
        private static void Postfix(ButtonControl __instance, ref bool __result)
        {
            if (__result && ShouldStripEscapeButton(__instance))
            {
                __result = false;
            }
        }
    }
}
