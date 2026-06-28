using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine.InputSystem.Controls;

namespace GregModIPAM;

/// <summary>
/// While IPAM is open, Escape is NOT blocked — the game opens the pause menu alongside IPAM closing.
/// P key IS blocked so it only closes IPAM without opening the pause menu.
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
                ModLogging.Warning($"gregMod.IPAM: Input System escape patch {nested.Name} failed: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(ButtonControl), nameof(ButtonControl.wasPressedThisFrame), MethodType.Getter)]
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
