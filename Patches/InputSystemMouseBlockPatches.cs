using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace GregModIPAM;

/// <summary>
/// While IPAM is open, suppress mouse button and delta reads on the Input System path so camera rotation
/// and world / menu clicks do not fire behind the overlay.
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

    private static bool ShouldStripMouseDelta(InputControl control)
    {
        if (!IPAMOverlay.IsVisible || control == null)
        {
            return false;
        }

        var path = control.path;
        return !string.IsNullOrEmpty(path)
            && path.IndexOf("/mouse/delta", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool ShouldStripMousePosition(InputControl control)
    {
        if (!IPAMOverlay.IsVisible || control == null)
        {
            return false;
        }

        var path = control.path;
        return !string.IsNullOrEmpty(path)
            && path.IndexOf("/mouse/position", StringComparison.OrdinalIgnoreCase) >= 0
            && !IpamGameInputGate.ShouldStripGameMouse; // position only if not already in bypass
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

    [HarmonyPatch(typeof(Vector2Control), nameof(Vector2Control.ReadValue), MethodType.Normal)]
    private static class MouseDeltaStripPatch
    {
        private static void Postfix(InputControl __instance, ref Vector2 __result)
        {
            if (ShouldStripMouseDelta(__instance))
            {
                __result = Vector2.zero;
            }
        }
    }
}
