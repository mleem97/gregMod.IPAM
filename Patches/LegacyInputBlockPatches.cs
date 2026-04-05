using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DHCPSwitches;

/// <summary>
/// While the router/switch CLI is open, suppress legacy <see cref="Input"/> keys that still reach gameplay
/// (Escape closing menus, P for pause / bind, Tab for UI focus) without blocking typing in IMGUI.
/// </summary>
internal static class LegacyInputBlockPatches
{
    private static System.Type InputType =>
        System.Type.GetType("UnityEngine.Input, UnityEngine.InputLegacyModule");

    private static bool ShouldBlock(KeyCode key)
    {
        var cli = DeviceTerminalOverlay.IsVisible;
        var ipam = IPAMOverlay.IsVisible;

        if (cli)
        {
            return key is KeyCode.Escape or KeyCode.P or KeyCode.Tab;
        }

        // F1 toggles IPAM; suppress legacy F1 while IPAM is open and on the frame we handled the toggle.
        if (key == KeyCode.F1
            && (ipam || Time.frameCount == IPAMOverlay.LegacyF1ConsumedFrame))
        {
            return true;
        }

        return false;
    }

    /// <summary>Project Input Manager often maps Escape to a virtual "Cancel" button; that path does not use <see cref="KeyCode"/>.</summary>
    private static bool IsVirtualCancelButton(string buttonName)
    {
        if (string.IsNullOrEmpty(buttonName))
        {
            return false;
        }

        return buttonName.Equals("Cancel", StringComparison.OrdinalIgnoreCase);
    }

    [HarmonyPatch]
    private static class GetKeyDownPatch
    {
        private static MethodBase TargetMethod()
        {
            var t = InputType;
            return t == null ? null : AccessTools.Method(t, "GetKeyDown", new[] { typeof(KeyCode) });
        }

        private static bool Prepare() => TargetMethod() != null;

        private static void Postfix(KeyCode key, ref bool __result)
        {
            if (ShouldBlock(key))
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch]
    private static class GetKeyPatch
    {
        private static MethodBase TargetMethod()
        {
            var t = InputType;
            return t == null ? null : AccessTools.Method(t, "GetKey", new[] { typeof(KeyCode) });
        }

        private static bool Prepare() => TargetMethod() != null;

        private static void Postfix(KeyCode key, ref bool __result)
        {
            if (ShouldBlock(key))
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch]
    private static class GetKeyUpPatch
    {
        private static MethodBase TargetMethod()
        {
            var t = InputType;
            return t == null ? null : AccessTools.Method(t, "GetKeyUp", new[] { typeof(KeyCode) });
        }

        private static bool Prepare() => TargetMethod() != null;

        private static void Postfix(KeyCode key, ref bool __result)
        {
            if (ShouldBlock(key))
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch]
    private static class GetButtonDownStringPatch
    {
        private static MethodBase TargetMethod()
        {
            var t = InputType;
            return t == null ? null : AccessTools.Method(t, "GetButtonDown", new[] { typeof(string) });
        }

        private static bool Prepare() => TargetMethod() != null;

        private static void Postfix(string buttonName, ref bool __result)
        {
            if (DeviceTerminalOverlay.IsVisible && IsVirtualCancelButton(buttonName))
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch]
    private static class GetButtonStringPatch
    {
        private static MethodBase TargetMethod()
        {
            var t = InputType;
            return t == null ? null : AccessTools.Method(t, "GetButton", new[] { typeof(string) });
        }

        private static bool Prepare() => TargetMethod() != null;

        private static void Postfix(string buttonName, ref bool __result)
        {
            if (DeviceTerminalOverlay.IsVisible && IsVirtualCancelButton(buttonName))
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch]
    private static class GetButtonUpStringPatch
    {
        private static MethodBase TargetMethod()
        {
            var t = InputType;
            return t == null ? null : AccessTools.Method(t, "GetButtonUp", new[] { typeof(string) });
        }

        private static bool Prepare() => TargetMethod() != null;

        private static void Postfix(string buttonName, ref bool __result)
        {
            if (DeviceTerminalOverlay.IsVisible && IsVirtualCancelButton(buttonName))
            {
                __result = false;
            }
        }
    }

    internal static void TryApply(HarmonyLib.Harmony harmonyInstance)
    {
        if (InputType == null)
        {
            ModLogging.Msg("DHCPSwitches: UnityEngine.InputLegacyModule not loaded — skipping legacy key block patches.");
            return;
        }

        foreach (var nested in typeof(LegacyInputBlockPatches).GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Static))
        {
            try
            {
                harmonyInstance.CreateClassProcessor(nested).Patch();
            }
            catch (System.Exception ex)
            {
                ModLogging.Warning($"DHCPSwitches: legacy input patch {nested.Name} failed: {ex.Message}");
            }
        }
    }
}
