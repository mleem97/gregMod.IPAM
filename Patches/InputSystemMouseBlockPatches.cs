using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine.InputSystem.Controls;

namespace GregModIPAM;

/// <summary>
/// While IPAM is open, suppress mouse button reads on the Input System path so world / menu clicks do not fire
/// behind the overlay. Mod code that still needs hardware mouse wraps reads in <see cref="IpamGameInputGate.BeginHardwareMouseBypass"/>.
/// </summary>
internal static class InputSystemMouseBlockPatches
{
    internal static void TryApply(HarmonyLib.Harmony harmonyInstance)
    {
        foreach (var nested in typeof(InputSystemMouseBlockPatches).GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Static))
        {
            try
            {
                harmonyInstance.CreateClassProcessor(nested).Patch();
            }
            catch (Exception ex)
            {
                ModLogging.Warning($"gregMod.IPAM: Input System mouse patch {nested.Name} failed: {ex.Message}");
            }
        }
    }

    private static bool ShouldStripMouseButton(ButtonControl control)
    {
        if (!IpamGameInputGate.ShouldStripGameMouse || control == null)
        {
            return false;
        }

        var path = control.path;
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        return path.IndexOf("/mouse/leftButton", StringComparison.OrdinalIgnoreCase) >= 0
               || path.IndexOf("/mouse/rightButton", StringComparison.OrdinalIgnoreCase) >= 0
               || path.IndexOf("/mouse/middleButton", StringComparison.OrdinalIgnoreCase) >= 0
               || path.IndexOf("/mouse/forwardButton", StringComparison.OrdinalIgnoreCase) >= 0
               || path.IndexOf("/mouse/backButton", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    [HarmonyPatch(typeof(ButtonControl), nameof(ButtonControl.wasPressedThisFrame), MethodType.Getter)]
    private static class MouseWasPressedPatch
    {
        private static void Postfix(ButtonControl __instance, ref bool __result)
        {
            if (__result && ShouldStripMouseButton(__instance))
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(ButtonControl), nameof(ButtonControl.wasReleasedThisFrame), MethodType.Getter)]
    private static class MouseWasReleasedPatch
    {
        private static void Postfix(ButtonControl __instance, ref bool __result)
        {
            if (__result && ShouldStripMouseButton(__instance))
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(ButtonControl), nameof(ButtonControl.isPressed), MethodType.Getter)]
    private static class MouseIsPressedPatch
    {
        private static void Postfix(ButtonControl __instance, ref bool __result)
        {
            if (__result && ShouldStripMouseButton(__instance))
            {
                __result = false;
            }
        }
    }
}
