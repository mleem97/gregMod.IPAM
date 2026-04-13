using System.Globalization;
using System.Net;

namespace DHCPSwitches;

/// <summary>Reserved private IPv4 ranges (RFC 1918). Optional checks for mod-assigned LANs.</summary>
public static class Ipv4Rfc1918
{
    public static bool TryParseIpv4(string ip, out uint be)
    {
        be = 0;
        if (string.IsNullOrWhiteSpace(ip) || !IPAddress.TryParse(ip.Trim(), out var addr)
                                          || addr.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return false;
        }

        var b = addr.GetAddressBytes();
        if (b.Length != 4)
        {
            return false;
        }

        be = ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
        return true;
    }

    public static bool IsPrivateRfc1918(string ip)
    {
        if (!TryParseIpv4(ip, out var v))
        {
            return false;
        }

        var a = (byte)((v >> 24) & 0xff);
        var b = (byte)((v >> 16) & 0xff);
        if (a == 10)
        {
            return true;
        }

        if (a == 172 && b >= 16 && b <= 31)
        {
            return true;
        }

        if (a == 192 && b == 168)
        {
            return true;
        }

        return false;
    }
}
