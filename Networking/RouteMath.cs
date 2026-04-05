using System;
using System.Net;

namespace DHCPSwitches;

internal static class RouteMath
{
    internal static bool TryParseIpv4(string s, out uint ip)
    {
        ip = 0;
        if (string.IsNullOrWhiteSpace(s))
        {
            return false;
        }

        if (!IPAddress.TryParse(s.Trim(), out var addr))
        {
            return false;
        }

        var b = addr.GetAddressBytes();
        if (b.Length != 4)
        {
            return false;
        }

        ip = (uint)((b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]);
        return true;
    }

    internal static string FormatIpv4(uint ip)
    {
        return $"{(ip >> 24) & 255}.{(ip >> 16) & 255}.{(ip >> 8) & 255}.{ip & 255}";
    }

    internal static int MaskToPrefixLength(uint mask)
    {
        if (mask == 0)
        {
            return 0;
        }

        var m = mask;
        var count = 0;
        while ((m & 0x80000000) != 0)
        {
            count++;
            m <<= 1;
        }

        if (m != 0)
        {
            return -1;
        }

        return count;
    }

    /// <summary>Parses dotted-decimal mask or CIDR suffix only (<c>/28</c>, <c>/24</c>) as used after <c>ip address</c>.</summary>
    internal static bool TryParseSubnetMaskField(string maskField, out uint maskUint)
    {
        maskUint = 0;
        if (string.IsNullOrWhiteSpace(maskField))
        {
            return false;
        }

        var t = maskField.Trim();
        if (t.StartsWith("/", StringComparison.Ordinal))
        {
            if (!int.TryParse(t.AsSpan(1), out var pl) || pl < 0 || pl > 32)
            {
                return false;
            }

            maskUint = pl == 0 ? 0u : (0xFFFFFFFFu << (32 - pl));
            return true;
        }

        if (!TryParseIpv4(t, out maskUint))
        {
            return false;
        }

        return MaskToPrefixLength(maskUint) >= 0;
    }

    /// <summary>Converts <c>/28</c> or valid dotted mask to dotted form for storage in <see cref="RouterInterfaceConfig.SubnetMask"/>.</summary>
    internal static bool TryNormalizeSubnetToDotted(string maskField, out string dottedMask)
    {
        dottedMask = null;
        if (!TryParseSubnetMaskField(maskField, out var m))
        {
            return false;
        }

        dottedMask = FormatIpv4(m);
        return true;
    }

    internal static bool PrefixCovers(uint network, int prefixLen, uint address)
    {
        if (prefixLen < 0 || prefixLen > 32)
        {
            return false;
        }

        if (prefixLen == 0)
        {
            return true;
        }

        var mask = prefixLen == 32 ? 0xFFFFFFFFu : (0xFFFFFFFFu << (32 - prefixLen));
        return (network & mask) == (address & mask);
    }

    internal static bool TryParsePrefix(string dest, out uint network, out int prefixLen)
    {
        network = 0;
        prefixLen = -1;
        if (string.IsNullOrWhiteSpace(dest))
        {
            return false;
        }

        var t = dest.Trim();
        var slash = t.IndexOf('/');
        if (slash > 0)
        {
            var ipPart = t.Substring(0, slash);
            if (!TryParseIpv4(ipPart, out network))
            {
                return false;
            }

            if (!int.TryParse(t.Substring(slash + 1), out var pl) || pl < 0 || pl > 32)
            {
                return false;
            }

            prefixLen = pl;
            if (pl == 0)
            {
                network = 0;
                return true;
            }

            if (pl == 32)
            {
                return true;
            }

            var mask = 0xFFFFFFFFu << (32 - pl);
            network &= mask;
            return true;
        }

        if (!TryParseIpv4(t, out network))
        {
            return false;
        }

        prefixLen = 32;
        return true;
    }

    /// <summary>Network address and prefix for a host IP and mask (same rules as connected routes in <see cref="RouterForwarding"/>).</summary>
    internal static bool TryGetIpv4ConnectedNetwork(string ip, string maskField, out uint network, out int prefixLen)
    {
        network = 0;
        prefixLen = -1;
        if (string.IsNullOrWhiteSpace(ip) || string.IsNullOrWhiteSpace(maskField))
        {
            return false;
        }

        if (!TryParseIpv4(ip.Trim(), out var ipU))
        {
            return false;
        }

        if (!TryParseSubnetMaskField(maskField.Trim(), out var maskU))
        {
            return false;
        }

        prefixLen = MaskToPrefixLength(maskU);
        if (prefixLen < 0 || prefixLen > 32)
        {
            return false;
        }

        network = prefixLen == 0 ? 0u : (ipU & (0xFFFFFFFFu << (32 - prefixLen)));
        return true;
    }

    /// <summary>True if two connected IPv4 subnets share any host (one contains the other's network).</summary>
    internal static bool ConnectedIpv4SubnetsConflict(uint netA, int plA, uint netB, int plB)
    {
        if (plA < 0 || plA > 32 || plB < 0 || plB > 32)
        {
            return false;
        }

        return PrefixCovers(netA, plA, netB) || PrefixCovers(netB, plB, netA);
    }
}
