namespace DHCPSwitches;

/// <summary>Lightweight device kind checks for IPAM / dispatch (expand as needed).</summary>
public static class NetworkDeviceClassifier
{
    public static bool IsServer(UnityEngine.Object o) => o is Server;
}
