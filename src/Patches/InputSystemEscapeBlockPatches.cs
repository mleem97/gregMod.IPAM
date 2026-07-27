using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine.InputSystem.Controls;

namespace GregModIPAM;

/// <summary>
/// While IPAM is open, Escape is NOT blocked — the game opens the pause menu alongside IPAM closing.
/// P key IS blocked so it only closes IPAM without opening the pause menu.
///
/// Applied manually via TryApply to avoid hard-failure if ButtonControl.wasPressedThisFrame is unavailable.
/// </summary>
internal static class InputSystemEscapeBlockPatches
{
    internal static void TryApply(HarmonyLib.Harmony harmonyInstance)
    {
        try
        {
            var prop = typeof(ButtonControl).GetProperty("wasPressedThisFrame", BindingFlags.Instance | BindingFlags.Public);
            if (prop == null)
            {
                ModReleaseLog.Warning("InputSystemEscapeBlockPatches: ButtonControl.wasPressedThisFrame not found, skipping");
                return;
            }

            var getter = prop.GetGetMethod();
            if (getter == null)
            {
                ModReleaseLog.Warning("InputSystemEscapeBlockPatches: ButtonControl.wasPressedThisFrame getter not found, skipping");
                return;
            }

            harmonyInstance.Patch(getter, postfix: new HarmonyMethod(typeof(PKeyStripPatch).GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic)));
            ModReleaseLog.HarmonyPatch("InputSystemEscapeBlockPatches.PKeyStripPatch", true);
        }
        catch (Exception ex)
        {
            ModReleaseLog.HarmonyPatch("InputSystemEscapeBlockPatches.PKeyStripPatch", false, ex.Message);
        }
    }

    private static class PKeyStripPatch
    {
        private static void Postfix(ButtonControl __instance, ref bool __result)
        {
            if (__result && IPAMOverlay.IsVisible && __instance != null)
            {
                var path = __instance.path;
                if (!string.IsNullOrEmpty(path) && path.IndexOf("/p", StringComparison.OrdinalIgnoreCase) >= 0
                    && path.Length >= 2 && path[path.Length - 2] == '/' && path[path.Length - 1] is 'p' or 'P')
                {
                    __result = false;
                }
            }
        }
    }
}
