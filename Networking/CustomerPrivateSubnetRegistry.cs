using System.Collections.Generic;

namespace DHCPSwitches;

/// <summary>
/// Optional mod-defined private LAN CIDR per server when there is no game contract subnet (e.g. routed lab).
/// Populated by future UI/persistence if needed; empty by default so DHCP does not invent addresses.
/// </summary>
public static class CustomerPrivateSubnetRegistry
{
    /// <summary>Try mod-assigned /24 (or any CIDR <see cref="RouteMath"/> accepts) for this server.</summary>
    public static bool TryGetPrivateLanCidrForServer(Server server, out string cidr)
    {
        cidr = null;
        return false;
    }

    public static IEnumerable<string> EnumerateDhcpCandidates(string privateCidr)
    {
        return RouteMath.EnumerateDhcpCandidates(privateCidr, skipTypicalGatewayLastOctet: true);
    }
}
