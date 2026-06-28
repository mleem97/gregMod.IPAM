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
///
/// All patches are applied manually via TryApply — nested types with [HarmonyPatch] are skipped by PatchAll
/// to avoid hard-failure when a target method is unavailable on a specific Unity/IL2CPP build.
/// </summary>
internal static class InputSystemMouseBlockPatches
{
    internal static void TryApply(HarmonyLib.Harmony harmonyInstance)
    {
        var applied = 0;
        var skipped = 0;

        // Button patches (ButtonControl properties — stable across Unity versions)
        applied += TryPatchProperty(harmonyInstance, typeof(ButtonControl), "wasPressedThisFrame", typeof(MouseWasPressedPatch));
        applied += TryPatchProperty(harmonyInstance, typeof(ButtonControl), "wasReleasedThisFrame", typeof(MouseWasReleasedPatch));
        applied += TryPatchProperty(harmonyInstance, typeof(ButtonControl), "isPressed", typeof(MouseIsPressedPatch));

        // Delta patch (Vector2Control.ReadValue — may not resolve on all IL2CPP builds)
        var deltaPatched = TryPatchMethod(harmonyInstance, typeof(Vector2Control), "ReadValue", typeof(MouseDeltaStripPatch));
        if (deltaPatched) applied++; else skipped++;

        ModReleaseLog.HarmonyPatch($"InputSystemMouseBlockPatches: {applied} applied, {skipped} skipped (optional)", true);
    }

    private static int TryPatchProperty(HarmonyLib.Harmony h, Type targetType, string propertyName, Type patchType)
    {
        try
        {
            var prop = targetType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (prop == null)
            {
                ModReleaseLog.Warning($"InputSystemMouseBlockPatches: {targetType.Name}.{propertyName} not found, skipping");
                return 0;
            }

            var getter = prop.GetGetMethod();
            if (getter == null)
            {
                ModReleaseLog.Warning($"InputSystemMouseBlockPatches: {targetType.Name}.{propertyName} getter not found, skipping");
                return 0;
            }

            h.Patch(getter, postfix: new HarmonyMethod(patchType.GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic)));
            ModReleaseLog.HarmonyPatch($"InputSystemMouseBlockPatches.{patchType.Name}", true);
            return 1;
        }
        catch (Exception ex)
        {
            ModReleaseLog.HarmonyPatch($"InputSystemMouseBlockPatches.{patchType.Name}", false, ex.Message);
            return 0;
        }
    }

    private static bool TryPatchMethod(HarmonyLib.Harmony h, Type targetType, string methodName, Type patchType)
    {
        try
        {
            var method = targetType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
            if (method == null)
            {
                ModReleaseLog.Warning($"InputSystemMouseBlockPatches: {targetType.Name}.{methodName}() not found, skipping (optional delta blocker)");
                return false;
            }

            h.Patch(method, postfix: new HarmonyMethod(patchType.GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic)));
            ModReleaseLog.HarmonyPatch($"InputSystemMouseBlockPatches.{patchType.Name}", true);
            return true;
        }
        catch (Exception ex)
        {
            ModReleaseLog.Warning($"InputSystemMouseBlockPatches: {targetType.Name}.{methodName}() patch skipped: {ex.Message}");
            return false;
        }
    }

    // ── Shared logic ──

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

    // ── Patch postfixes (referenced by TryApply via reflection) ──

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
