using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;

namespace DHCPSwitches;

/// <summary>IPv4 CIDR parsing and host enumeration for DHCP when the game does not expose a usable-IP API.</summary>
public static class RouteMath
{
    /// <summary>Parses <c>a.b.c.d/prefix</c> or bare <c>a.b.c.d</c> (treated as /32).</summary>
    public static bool TryParseIpv4Cidr(string cidr, out uint networkBe, out int prefixLen)
    {
        networkBe = 0;
        prefixLen = 0;
        if (string.IsNullOrWhiteSpace(cidr))
        {
            return false;
        }

        var s = cidr.Trim();
        var slash = s.IndexOf('/');
        string addrPart;
        if (slash >= 0)
        {
            addrPart = s.Substring(0, slash).Trim();
            if (!int.TryParse(s.Substring(slash + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out prefixLen)
                || prefixLen < 0
                || prefixLen > 32)
            {
                return false;
            }
        }
        else
        {
            addrPart = s;
            prefixLen = 32;
        }

        if (!IPAddress.TryParse(addrPart, out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = ip.GetAddressBytes();
        if (bytes.Length != 4)
        {
            return false;
        }

        networkBe = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
        if (prefixLen < 32)
        {
            var mask = prefixLen == 0 ? 0u : uint.MaxValue << (32 - prefixLen);
            networkBe &= mask;
        }

        return true;
    }

    /// <summary>
    /// Enumerates assignable host addresses in the CIDR (network + 1 .. broadcast - 1).
    /// For /31 and /32, yields nothing (no classic host range).
    /// </summary>
    public static IEnumerable<string> EnumerateDhcpCandidates(string cidr, bool skipTypicalGatewayLastOctet = true)
    {
        if (!TryParseIpv4Cidr(cidr, out var networkBe, out var prefixLen))
        {
            yield break;
        }

        if (prefixLen >= 31)
        {
            yield break;
        }

        var hostBits = 32 - prefixLen;
        var numHosts = 1u << hostBits;
        if (numHosts < 2)
        {
            yield break;
        }

        var broadcast = networkBe | (numHosts - 1);
        var firstHost = networkBe + 1;
        var lastHost = broadcast - 1;
        if (firstHost > lastHost)
        {
            yield break;
        }

        for (var h = firstHost; h <= lastHost; h++)
        {
            var a = (byte)((h >> 24) & 0xff);
            var b = (byte)((h >> 16) & 0xff);
            var c = (byte)((h >> 8) & 0xff);
            var d = (byte)(h & 0xff);
            if (skipTypicalGatewayLastOctet && d == 1)
            {
                continue;
            }

            yield return string.Format(CultureInfo.InvariantCulture, "{0}.{1}.{2}.{3}", a, b, c, d);
        }
    }

    /// <summary>IPv4 dotted-quad to big-endian uint (network byte order as integer).</summary>
    public static bool TryIpv4StringToUint(string s, out uint be)
    {
        be = 0;
        if (string.IsNullOrWhiteSpace(s))
        {
            return false;
        }

        if (!IPAddress.TryParse(s.Trim(), out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = ip.GetAddressBytes();
        if (bytes.Length != 4)
        {
            return false;
        }

        be = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
        return true;
    }

    public static bool IsIpv4InCidr(string ip, string cidr)
    {
        if (!TryParseIpv4Cidr(cidr, out var networkBe, out var prefixLen))
        {
            return false;
        }

        if (!TryIpv4StringToUint(ip, out var ipBe))
        {
            return false;
        }

        return IsIpv4UintInPrefix(ipBe, networkBe, prefixLen);
    }

    public static bool IsIpv4UintInPrefix(uint ipBe, uint networkBe, int prefixLen)
    {
        if (prefixLen < 0 || prefixLen > 32)
        {
            return false;
        }

        if (prefixLen == 0)
        {
            return true;
        }

        if (prefixLen == 32)
        {
            return ipBe == networkBe;
        }

        var mask = uint.MaxValue << (32 - prefixLen);
        return (ipBe & mask) == (networkBe & mask);
    }

    /// <summary>Counts addresses yielded by <see cref="EnumerateDhcpCandidates"/> (same gateway skip rule).</summary>
    public static int CountDhcpUsableHosts(string cidr, bool skipTypicalGatewayLastOctet = true)
    {
        var n = 0;
        foreach (var _ in EnumerateDhcpCandidates(cidr, skipTypicalGatewayLastOctet))
        {
            n++;
        }

        return n;
    }

    /// <summary>True if <paramref name="childCidr"/> is a strictly more specific subnet inside <paramref name="parentCidr"/>.</summary>
    public static bool IsStrictChildOf(string childCidr, string parentCidr)
    {
        if (!TryParseIpv4Cidr(parentCidr, out var pNet, out var pLen))
        {
            return false;
        }

        if (!TryParseIpv4Cidr(childCidr, out var cNet, out var cLen))
        {
            return false;
        }

        if (cLen <= pLen || cLen > 32)
        {
            return false;
        }

        return IsIpv4UintInPrefix(cNet, pNet, pLen);
    }

    /// <summary>Human-readable reason when <see cref="IsStrictChildOf"/> would be false (for IPAM UI).</summary>
    public static string ExplainStrictChildFailure(string childCidr, string parentCidr)
    {
        var child = (childCidr ?? "").Trim();
        var parent = (parentCidr ?? "").Trim();
        if (!TryParseIpv4Cidr(parent, out var pNet, out var pLen))
        {
            return "Parent CIDR is invalid.";
        }

        if (!TryParseIpv4Cidr(child, out var cNet, out var cLen))
        {
            return "Child CIDR is invalid.";
        }

        if (cLen <= pLen)
        {
            return $"Child must be more specific than parent /{pLen} (you used /{cLen}). Try e.g. /{Math.Min(32, pLen + 1)} or longer inside the parent range.";
        }

        if (!IsIpv4UintInPrefix(cNet, pNet, pLen))
        {
            return $"Network {child} is not inside parent {parent}. The child’s addresses must all fall in the parent’s range.";
        }

        return "That combination is not allowed.";
    }
}
