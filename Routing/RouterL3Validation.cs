using System;

namespace DHCPSwitches;

/// <summary>
/// Cisco-like checks so one router cannot host the same subnet on multiple interfaces (which made ping/route behave like a hub).
/// </summary>
internal static class RouterL3Validation
{
    internal static bool TryAcceptNewInterfaceAddress(
        RouterRuntimeConfig rc,
        int editingIfaceIndex,
        string newIp,
        string newMaskDotted,
        out string errorMessage)
    {
        errorMessage = null;
        if (rc == null || string.IsNullOrWhiteSpace(newIp) || string.IsNullOrWhiteSpace(newMaskDotted))
        {
            errorMessage = "% Internal error: invalid address request.";
            return false;
        }

        if (!RouteMath.TryGetIpv4ConnectedNetwork(newIp, newMaskDotted, out var newNet, out var newPl))
        {
            errorMessage = "% Invalid IPv4 address or mask.";
            return false;
        }

        var ipTrim = newIp.Trim();
        foreach (var iface in rc.Interfaces)
        {
            if (iface == null || iface.Index == editingIfaceIndex)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(iface.IpAddress) || string.IsNullOrWhiteSpace(iface.SubnetMask))
            {
                continue;
            }

            if (string.Equals(iface.IpAddress.Trim(), ipTrim, StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = $"% IPv4 address {ipTrim} is already assigned on {iface.Name}.";
                return false;
            }

            if (!RouteMath.TryGetIpv4ConnectedNetwork(iface.IpAddress, iface.SubnetMask, out var oNet, out var oPl))
            {
                continue;
            }

            if (RouteMath.ConnectedIpv4SubnetsConflict(newNet, newPl, oNet, oPl))
            {
                errorMessage =
                    $"% Subnet overlaps {iface.Name} ({iface.IpAddress.Trim()} {iface.SubnetMask.Trim()}). " +
                    "Use a different subnet per interface (or remove the other address first).";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Rejects routes that cannot work in our model: next-hop on this router, shadowed by a connected route, or next-hop not on any connected subnet.
    /// </summary>
    internal static bool TryAcceptStaticRoute(RouterRuntimeConfig rc, string destStr, string nextHop, out string errorMessage)
    {
        errorMessage = null;
        if (rc == null || string.IsNullOrWhiteSpace(destStr) || string.IsNullOrWhiteSpace(nextHop))
        {
            errorMessage = "% Internal error: invalid static route.";
            return false;
        }

        var nh = nextHop.Trim();
        if (!RouteMath.TryParseIpv4(nh, out var nhU))
        {
            errorMessage = "% Invalid next-hop address.";
            return false;
        }

        foreach (var iface in rc.Interfaces)
        {
            if (iface.Shutdown || string.IsNullOrWhiteSpace(iface.IpAddress))
            {
                continue;
            }

            if (string.Equals(iface.IpAddress.Trim(), nh, StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = "% Next-hop cannot be an address on this router (use a neighbor’s IP).";
                return false;
            }
        }

        if (!RouteMath.TryParsePrefix(destStr.Trim(), out var srNet, out var srLen))
        {
            errorMessage = "% Invalid static route destination.";
            return false;
        }

        if (srLen > 0)
        {
            var probe = srLen >= 32 ? srNet : srNet + 1;
            if (RouterForwarding.GetLongestConnectedPrefixForHost(rc, probe, out var connPl) && connPl >= srLen)
            {
                errorMessage =
                    "% Static route is shadowed by a connected interface (same or longer prefix). " +
                    "Connected routes win when the prefix length is equal; remove the static or use a more specific destination.";
                return false;
            }
        }

        if (!RouterForwarding.IsNextHopOnConnectedSubnet(rc, nhU, out _))
        {
            errorMessage =
                "% Next-hop is not reachable on a directly connected subnet of this router " +
                "(configure an interface in the same subnet as the next-hop first).";
            return false;
        }

        return true;
    }
}
