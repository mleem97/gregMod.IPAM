using System;
using System.Collections.Generic;
using System.Linq;

namespace GregModIPAM;

internal static class NetworkHealthScore
{
    internal static int ComputeScore()
    {
        var score = 100;
        var prefixes = IpamDataStore.GetPrefixes();
        var servers = UnityEngine.Object.FindObjectsOfType<Server>();

        // Deduction: overlapping prefixes
        for (var i = 0; i < prefixes.Count; i++)
        {
            for (var j = i + 1; j < prefixes.Count; j++)
            {
                if (RouteMath.Ipv4CidrRangesOverlap(prefixes[i].Cidr ?? "", prefixes[j].Cidr ?? ""))
                {
                    score -= 5;
                }
            }
        }

        // Deduction: servers without IP
        var serversWithoutIp = 0;
        foreach (var s in servers)
        {
            var ip = DHCPManager.GetServerIP(s);
            if (string.IsNullOrWhiteSpace(ip) || ip == "0.0.0.0")
            {
                serversWithoutIp++;
            }
        }

        score -= serversWithoutIp * 2;

        // Deduction: exhausted scopes
        foreach (var scope in IpamDataStore.GetDhcpScopes())
        {
            if (!string.IsNullOrEmpty(scope.Cidr))
            {
                var usable = GameSubnetHelper.GetUsableIpsForSubnet(scope.Cidr, false);
                if (usable != null)
                {
                    var assigned = CountAssignedInCidr(scope.Cidr, servers);
                    var pct = (float)assigned / usable.Length;
                    if (pct >= 0.95f) score -= 10;
                    else if (pct >= 0.80f) score -= 5;
                }
            }
        }

        return Math.Max(0, Math.Min(100, score));
    }

    private static int CountAssignedInCidr(string cidr, Server[] servers)
    {
        var count = 0;
        foreach (var s in servers)
        {
            var ip = DHCPManager.GetServerIP(s);
            if (!string.IsNullOrWhiteSpace(ip) && ip != "0.0.0.0" && RouteMath.IsIpInCidr(ip, cidr))
            {
                count++;
            }
        }

        return count;
    }

    internal static string GetScoreLabel(int score)
    {
        return score switch
        {
            >= 90 => "Excellent",
            >= 70 => "Good",
            >= 50 => "Fair",
            >= 30 => "Poor",
            _ => "Critical",
        };
    }

    internal static UnityEngine.Color GetScoreColor(int score)
    {
        return score switch
        {
            >= 90 => new UnityEngine.Color(0.2f, 0.75f, 0.45f, 1f),
            >= 70 => new UnityEngine.Color(0.4f, 0.8f, 0.3f, 1f),
            >= 50 => new UnityEngine.Color(0.95f, 0.75f, 0.2f, 1f),
            >= 30 => new UnityEngine.Color(0.95f, 0.5f, 0.2f, 1f),
            _ => new UnityEngine.Color(0.95f, 0.25f, 0.2f, 1f),
        };
    }
}
