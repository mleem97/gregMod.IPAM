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
}
