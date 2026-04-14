using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine.InputSystem;

namespace DHCPSwitches;

/// <summary>
/// Blocks UI "cancel" (usually bound to Escape) from reaching Unity's Input System UI module while the CLI is open.
/// </summary>
internal static class InputSystemUiCancelPatches
{
    internal static void TryApply(HarmonyLib.Harmony harmonyInstance)
    {
        foreach (var nested in typeof(InputSystemUiCancelPatches).GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Static))
        {
            try
            {
                harmonyInstance.CreateClassProcessor(nested).Patch();
            }
            catch (System.Exception ex)
            {
                ModLogging.Warning($"DHCPSwitches: Input System UI patch {nested.Name} failed: {ex.Message}");
            }
        }
    }

    [HarmonyPatch]
    private static class InputSystemUiOnCancelPatch
    {
        private static MethodBase TargetMethod()
        {
            var t = typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule);
            return AccessTools.GetDeclaredMethods(t)
                .FirstOrDefault(m => m.Name == "OnCancel" && m.GetParameters().Length == 1);
        }

        private static bool Prepare() => TargetMethod() != null;

        private static bool Prefix()
        {
            return !IPAMOverlay.IsVisible;
        }
    }
}
