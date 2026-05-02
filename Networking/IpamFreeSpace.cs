using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;

namespace DHCPSwitches;

/// <summary>
/// phpIPAM-style free space: subtract child CIDRs from a parent, then cover remainder with maximal IPv4 CIDR blocks.
/// </summary>
internal static class IpamFreeSpace
{
    public static bool TryEnumerateMaximalFreeCidrs(
        string parentCidr,
        IReadOnlyList<IpamPrefixEntry> directChildren,
        out List<string> cidrs)
    {
        cidrs = new List<string>();
        if (string.IsNullOrWhiteSpace(parentCidr)
            || !RouteMath.TryParseIpv4Cidr(parentCidr.Trim(), out var pNet, out var pLen))
        {
            return false;
        }

        var parent = parentCidr.Trim();
        uint pLo = pNet;
        if (pLen >= 32)
        {
            return true;
        }

        uint span = 1u << (32 - pLen);
        uint pHi = pLo + span - 1;

        var free = new List<(uint Lo, uint Hi)> { (pLo, pHi) };

        if (directChildren != null)
        {
            foreach (var ch in directChildren)
            {
                if (ch == null)
                {
                    continue;
                }

                var cc = (ch.Cidr ?? "").Trim();
                if (string.IsNullOrEmpty(cc)
                    || !RouteMath.TryParseIpv4Cidr(cc, out var cNet, out var cLen))
                {
                    continue;
                }

                if (!RouteMath.IsStrictChildOf(cc, parent))
                {
                    continue;
                }

                uint cSpan = cLen >= 32 ? 1u : (1u << (32 - cLen));
                uint cLo = cNet;
                uint cHi = cLo + cSpan - 1UL > uint.MaxValue ? uint.MaxValue : cNet + cSpan - 1;

                free = SubtractIntervals(free, cLo, cHi);
            }
        }

        foreach (var (lo, hi) in free)
        {
            AppendMinimalCidrCover(lo, hi, cidrs);
        }

        cidrs.Sort(CompareCidrNetworkThenPrefixLen);
        return true;
    }

    /// <summary>
    /// Compact display for the prefixes table “Free /N” column: e.g. <c>1×/25, 2×/26</c> (maximal non-overlapping free blocks).
    /// </summary>
    public static bool TryFormatAggregatedFreeBlockCounts(
        string parentCidr,
        IReadOnlyList<IpamPrefixEntry> directChildren,
        out string display,
        out string tooltip)
    {
        display = "";
        tooltip = "";
        if (!TryEnumerateMaximalFreeCidrs(parentCidr, directChildren, out var cidrs) || cidrs.Count == 0)
        {
            return false;
        }

        var byPl = new Dictionary<int, int>();
        foreach (var c in cidrs)
        {
            if (!RouteMath.TryParseIpv4Cidr((c ?? "").Trim(), out _, out var pl))
            {
                continue;
            }

            byPl.TryGetValue(pl, out var n);
            byPl[pl] = n + 1;
        }

        if (byPl.Count == 0)
        {
            return false;
        }

        var parts = new List<string>();
        foreach (var pl in byPl.Keys.OrderBy(static x => x))
        {
            parts.Add($"{byPl[pl]}×/{pl}");
        }

        display = string.Join(", ", parts);
        var sb = new StringBuilder();
        sb.Append("Maximal free IPv4 blocks under this prefix (after direct children): ");
        sb.Append(string.Join(", ", cidrs));
        tooltip = sb.ToString();
        return true;
    }

    private static int CompareCidrNetworkThenPrefixLen(string a, string b)
    {
        if (!RouteMath.TryParseIpv4Cidr(a.Trim(), out var na, out var pla))
        {
            return 0;
        }

        if (!RouteMath.TryParseIpv4Cidr(b.Trim(), out var nb, out var plb))
        {
            return 0;
        }

        var c = na.CompareTo(nb);
        return c != 0 ? c : pla.CompareTo(plb);
    }

    private static List<(uint Lo, uint Hi)> SubtractIntervals(List<(uint Lo, uint Hi)> intervals, uint cLo, uint cHi)
    {
        var result = new List<(uint Lo, uint Hi)>();
        foreach (var (lo, hi) in intervals)
        {
            if (cHi < lo || cLo > hi)
            {
                result.Add((lo, hi));
                continue;
            }

            if (cLo <= lo && cHi >= hi)
            {
                continue;
            }

            if (cLo > lo)
            {
                result.Add((lo, cLo - 1u));
            }

            if (cHi < hi)
            {
                result.Add((cHi + 1u, hi));
            }
        }

        return result;
    }

    private static void AppendMinimalCidrCover(uint lo, uint hi, List<string> dst)
    {
        ulong cur = lo;
        var end = (ulong)hi;
        while (cur <= end)
        {
            ulong remain = end - cur + 1;
            ulong maxBlock = 1;
            for (var bit = 31; bit >= 0; bit--)
            {
                var trySize = 1UL << bit;
                if (trySize > remain)
                {
                    continue;
                }

                if ((cur & (trySize - 1)) != 0)
                {
                    continue;
                }

                if (cur + trySize - 1 > end)
                {
                    continue;
                }

                maxBlock = trySize;
                break;
            }

            if (maxBlock == 0)
            {
                break;
            }

            var pl = maxBlock == 1 ? 32 : 32 - BitOperations.Log2((uint)maxBlock);
            dst.Add(RouteMath.FormatIpv4Cidr((uint)cur, pl));
            cur += maxBlock;
        }
    }
}
