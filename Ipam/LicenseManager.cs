using UnityEngine.InputSystem;

namespace DHCPSwitches;

/// <summary>
/// Feature gates for DHCP / IPAM. Default: both enabled. Title-bar toggles or Ctrl+D (syncs both) for testing locked state.
/// Later: hook into ComputerShop / save unlock GUIDs (see DHCPSwitchesMod constants).
/// </summary>
internal static class LicenseManager
{
    private static bool _simulateDhcpLocked;
    private static bool _simulateIpamLocked;

    internal static bool IsDHCPUnlocked => !_simulateDhcpLocked;
    internal static bool IsIPAMUnlocked => !_simulateIpamLocked;

    internal static void ToggleDhcpUnlock()
    {
        _simulateDhcpLocked = !_simulateDhcpLocked;
        ModLogging.Msg(
            _simulateDhcpLocked
                ? "DHCP locked (bulk assign, DHCP auto, fill-empty)."
                : "DHCP unlocked.");
    }

    internal static void ToggleIpamUnlock()
    {
        _simulateIpamLocked = !_simulateIpamLocked;
        ModLogging.Msg(
            _simulateIpamLocked
                ? "IPAM locked (tables, IP editor, nav)."
                : "IPAM unlocked.");
    }

    /// <summary>Ctrl+D: flip both locks together (legacy debug shortcut).</summary>
    internal static void HandleDebugUnlock()
    {
        var kb = Keyboard.current;
        if (kb == null)
        {
            return;
        }

        if (!kb.leftCtrlKey.isPressed || !kb.dKey.wasPressedThisFrame)
        {
            return;
        }

        var next = !_simulateDhcpLocked;
        _simulateDhcpLocked = next;
        _simulateIpamLocked = next;
        ModLogging.Msg(
            next
                ? "Debug: DHCP + IPAM locked (Ctrl+D or title bar to unlock)."
                : "Debug: DHCP + IPAM unlocked.");
    }
}
