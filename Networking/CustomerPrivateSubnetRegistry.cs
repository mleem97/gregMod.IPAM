using System.Collections.Generic;
using UnityEngine;

namespace DHCPSwitches;

/// <summary>
/// Assigns a deterministic RFC1918 /24 per customer for server addressing. WAN/public /30-/31 comes from the game's contract
/// (<see cref="CustomerBase.subnetsPerApp"/>); servers use the private LAN; the router must get a public IP from the contract and a static route toward the private prefix.
/// </summary>
internal static class CustomerPrivateSubnetRegistry
{
    /// <summary>Maps customerID → 172.16.0.0/12 style /24 (second octet 16–31).</summary>
    internal static bool TryGetPrivateLanCidrForCustomerId(int customerId, out string cidr)
    {
        cidr = null;
        var id = customerId;
        var b2 = 16 + (Math.Abs(id) % 16);
        var b3 = (Math.Abs(id) / 16) % 256;
        cidr = $"172.{b2}.{b3}.0/24";
        return true;
    }

    internal static bool TryGetPrivateLanCidrForServer(Server server, out string cidr)
    {
        cidr = null;
        if (server == null)
        {
            return false;
        }

        var cb = GameSubnetHelper.FindCustomerBaseForServer(server);
        if (cb == null)
        {
            return false;
        }

        return TryGetPrivateLanCidrForCustomerId(cb.customerID, out cidr);
    }

    internal static bool TryGetPrivatePrefixForCustomer(CustomerBase customer, out uint network, out int prefixLen)
    {
        network = 0;
        prefixLen = -1;
        if (customer == null)
        {
            return false;
        }

        if (!TryGetPrivateLanCidrForCustomerId(customer.customerID, out var cidr))
        {
            return false;
        }

        return RouteMath.TryParsePrefix(cidr, out network, out prefixLen);
    }

    internal static bool IpBelongsToCustomerPrivateLan(Server server, string ip)
    {
        if (server == null || string.IsNullOrWhiteSpace(ip))
        {
            return false;
        }

        if (!TryGetPrivateLanCidrForServer(server, out var cidr))
        {
            return false;
        }

        if (!RouteMath.TryParsePrefix(cidr, out var net, out var len))
        {
            return false;
        }

        if (!RouteMath.TryParseIpv4(ip, out var a))
        {
            return false;
        }

        return RouteMath.PrefixCovers(net, len, a);
    }

    /// <summary>Usable host addresses in the private /24, excluding network, broadcast, and .1 (default gateway).</summary>
    internal static IEnumerable<string> EnumerateDhcpCandidates(string cidr)
    {
        if (string.IsNullOrWhiteSpace(cidr) || !RouteMath.TryParsePrefix(cidr.Trim(), out var network, out var len))
        {
            yield break;
        }

        if (len <= 0 || len >= 32)
        {
            yield break;
        }

        var hostMask = (1u << (32 - len)) - 1u;
        var broadcast = network | hostMask;
        for (var ip = network + 1; ip < broadcast; ip++)
        {
            var last = ip & 255;
            if (last == 0 || last == 255)
            {
                continue;
            }

            if (last == 1)
            {
                continue;
            }

            yield return RouteMath.FormatIpv4(ip);
        }
    }
}
