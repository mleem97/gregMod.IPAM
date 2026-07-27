namespace GregModIPAM;

/// <summary>
/// Feature gates for DHCP / IPAM. Default: both enabled. Title-bar toggles simulate locked state for testing.
/// Later: hook into ComputerShop / save unlock GUIDs (see GregModIPAMMod constants).
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
}
