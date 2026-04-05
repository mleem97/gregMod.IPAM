using System;

namespace DHCPSwitches;

/// <summary>RFC 1918 private IPv4 ranges: 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16. All other unicast IPv4 is treated as public for this mod.</summary>
internal static class Ipv4Rfc1918
{
    internal static bool IsPrivateAddress(uint ip)
    {
        var b0 = (int)((ip >> 24) & 255);
        var b1 = (int)((ip >> 16) & 255);
        if (b0 == 10)
        {
            return true;
        }

        if (b0 == 172 && b1 >= 16 && b1 <= 31)
        {
            return true;
        }

        if (b0 == 192 && b1 == 168)
        {
            return true;
        }

        return false;
    }

    internal static bool IsPrivateAddress(string ip)
    {
        return RouteMath.TryParseIpv4(ip, out var u) && IsPrivateAddress(u);
    }

    /// <summary>True if the CIDR network address lies in RFC1918 space (servers should use these LANs).</summary>
    internal static bool IsPrivateCidr(string cidr)
    {
        if (string.IsNullOrWhiteSpace(cidr) || !RouteMath.TryParsePrefix(cidr.Trim(), out var net, out _))
        {
            return false;
        }

        return IsPrivateAddress(net);
    }

    /// <summary>Typical customer WAN block: /30 or /31, not RFC1918.</summary>
    internal static bool LooksLikePublicPtpBlock(string cidr)
    {
        if (string.IsNullOrWhiteSpace(cidr) || !RouteMath.TryParsePrefix(cidr.Trim(), out _, out var len))
        {
            return false;
        }

        return len is 30 or 31 && !IsPrivateCidr(cidr);
    }
}
