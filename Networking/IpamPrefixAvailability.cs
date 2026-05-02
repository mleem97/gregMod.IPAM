using System;
using System.Collections.Generic;

namespace DHCPSwitches;

/// <summary>
/// phpIPAM-style “free split” counts: how many non-overlapping, CIDR-aligned child subnets of a template size
/// still fit under a parent after direct child prefixes are carved out.
/// </summary>
internal static class IpamPrefixAvailability
{
    /// <summary>Template child prefix length: /24 when <c>parentLen + 8 ≤ 24</c>, else <c>min(32, parentLen + 1)</c>.</summary>
    public static int GetTemplateChildPrefixLen(int parentLen)
    {
        if (parentLen < 0 || parentLen >= 32)
        {
            return -1;
        }

        if (parentLen + 8 <= 24)
        {
            return 24;
        }

        return Math.Min(32, parentLen + 1);
    }

    /// <summary>
    /// Computes aligned /N slot counts. <paramref name="freeSlots"/> is <c>totalSlots − union(occupied slots)</c>.
    /// </summary>
    public static bool TryComputeFreeSplitSlots(
        string parentCidr,
        IReadOnlyList<IpamPrefixEntry> directChildren,
        out int templateLen,
        out ulong totalSlots,
        out ulong freeSlots,
        out bool childRangesOverlap)
    {
        templateLen = -1;
        totalSlots = 0;
        freeSlots = 0;
        childRangesOverlap = false;

        if (string.IsNullOrWhiteSpace(parentCidr)
            || !RouteMath.TryParseIpv4Cidr(parentCidr.Trim(), out var pNet, out var pLen))
        {
            return false;
        }

        templateLen = GetTemplateChildPrefixLen(pLen);
        if (templateLen < 0 || templateLen <= pLen)
        {
            return false;
        }

        var parentStart = (ulong)pNet;
        var shiftTotal = templateLen - pLen;
        if (shiftTotal >= 64)
        {
            return false;
        }

        totalSlots = 1UL << shiftTotal;
        var blockSize = 1UL << (32 - templateLen);
        if (blockSize == 0)
        {
            return false;
        }

        var parentSpan = 1UL << (32 - pLen);
        var parentEnd = parentStart + parentSpan - 1UL;

        var intervals = new List<(ulong Lo, ulong Hi)>();
        ulong rawSum = 0;

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

                if (!RouteMath.IsStrictChildOf(cc, parentCidr.Trim()))
                {
                    continue;
                }

                var cStart = (ulong)cNet;
                var childSpan = 1UL << (32 - cLen);
                var cEnd = cStart + childSpan - 1UL;

                var clipStart = Math.Max(cStart, parentStart);
                var clipEnd = Math.Min(cEnd, parentEnd);
                if (clipStart > clipEnd)
                {
                    continue;
                }

                var off0 = clipStart - parentStart;
                var off1 = clipEnd - parentStart;
                var slotLo = off0 / blockSize;
                var slotHi = off1 / blockSize;
                if (slotHi < slotLo)
                {
                    continue;
                }

                rawSum += slotHi - slotLo + 1UL;
                intervals.Add((slotLo, slotHi));
            }
        }

        var usedUnion = MergeInclusiveIntervalsAndMeasure(intervals);
        childRangesOverlap = rawSum > usedUnion && intervals.Count > 1;

        freeSlots = totalSlots > usedUnion ? totalSlots - usedUnion : 0UL;
        return true;
    }

    /// <summary>
    /// Lists maximal contiguous runs of free template-sized slots under <paramref name="parentCidr"/>
    /// after carving out <paramref name="directChildren"/>.
    /// </summary>
    public static bool TryEnumerateFreeTemplateSlotRuns(
        string parentCidr,
        IReadOnlyList<IpamPrefixEntry> directChildren,
        out int templateLen,
        out ulong totalSlots,
        out List<(ulong FirstSlotIndex, ulong SlotCount)> runs)
    {
        runs = new List<(ulong FirstSlotIndex, ulong SlotCount)>();
        templateLen = -1;
        totalSlots = 0;

        if (string.IsNullOrWhiteSpace(parentCidr)
            || !RouteMath.TryParseIpv4Cidr(parentCidr.Trim(), out var pNet, out var pLen))
        {
            return false;
        }

        templateLen = GetTemplateChildPrefixLen(pLen);
        if (templateLen < 0 || templateLen <= pLen)
        {
            return false;
        }

        var shiftTotal = templateLen - pLen;
        if (shiftTotal >= 64)
        {
            return false;
        }

        totalSlots = 1UL << shiftTotal;
        var blockSize = 1UL << (32 - templateLen);
        if (blockSize == 0)
        {
            return false;
        }

        var parentStart = (ulong)pNet;
        var parentSpan = 1UL << (32 - pLen);
        var parentEnd = parentStart + parentSpan - 1UL;

        var intervals = new List<(ulong Lo, ulong Hi)>();

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

                if (!RouteMath.IsStrictChildOf(cc, parentCidr.Trim()))
                {
                    continue;
                }

                var cStart = (ulong)cNet;
                var childSpan = 1UL << (32 - cLen);
                var cEnd = cStart + childSpan - 1UL;

                var clipStart = Math.Max(cStart, parentStart);
                var clipEnd = Math.Min(cEnd, parentEnd);
                if (clipStart > clipEnd)
                {
                    continue;
                }

                var off0 = clipStart - parentStart;
                var off1 = clipEnd - parentStart;
                var slotLo = off0 / blockSize;
                var slotHi = off1 / blockSize;
                if (slotHi < slotLo)
                {
                    continue;
                }

                intervals.Add((slotLo, slotHi));
            }
        }

        var merged = MergeInclusiveIntervals(intervals);
        ulong cur = 0;
        foreach (var (lo, hi) in merged)
        {
            if (lo > cur)
            {
                runs.Add((cur, lo - cur));
            }

            if (hi >= cur)
            {
                cur = hi + 1UL;
            }
        }

        if (cur < totalSlots)
        {
            runs.Add((cur, totalSlots - cur));
        }

        return true;
    }

    private static List<(ulong Lo, ulong Hi)> MergeInclusiveIntervals(List<(ulong Lo, ulong Hi)> intervals)
    {
        var result = new List<(ulong Lo, ulong Hi)>();
        if (intervals.Count == 0)
        {
            return result;
        }

        intervals.Sort((a, b) => a.Lo.CompareTo(b.Lo));
        var curLo = intervals[0].Lo;
        var curHi = intervals[0].Hi;

        for (var i = 1; i < intervals.Count; i++)
        {
            var (lo, hi) = intervals[i];
            if (lo <= curHi + 1UL)
            {
                if (hi > curHi)
                {
                    curHi = hi;
                }
            }
            else
            {
                result.Add((curLo, curHi));
                curLo = lo;
                curHi = hi;
            }
        }

        result.Add((curLo, curHi));
        return result;
    }

    private static ulong MergeInclusiveIntervalsAndMeasure(List<(ulong Lo, ulong Hi)> intervals)
    {
        if (intervals.Count == 0)
        {
            return 0;
        }

        intervals.Sort((a, b) => a.Lo.CompareTo(b.Lo));
        ulong sum = 0;
        var curLo = intervals[0].Lo;
        var curHi = intervals[0].Hi;

        for (var i = 1; i < intervals.Count; i++)
        {
            var (lo, hi) = intervals[i];
            if (lo <= curHi + 1UL)
            {
                if (hi > curHi)
                {
                    curHi = hi;
                }
            }
            else
            {
                sum += curHi - curLo + 1UL;
                curLo = lo;
                curHi = hi;
            }
        }

        sum += curHi - curLo + 1UL;
        return sum;
    }

    /// <summary>Developer sanity checks on fixed CIDRs; returns false if any invariant fails.</summary>
    internal static bool RunSanityChecks()
    {
        var empty = Array.Empty<IpamPrefixEntry>();
        if (!TryComputeFreeSplitSlots("10.0.0.0/16", empty, out var tl, out var tot, out var fr, out var ov)
            || tl != 24
            || tot != 256UL
            || fr != 256UL
            || ov)
        {
            return false;
        }

        var oneChild = new IpamPrefixEntry[]
        {
            new()
            {
                Id = "a",
                ParentId = "",
                Cidr = "10.0.1.0/24",
                Name = "",
                Tenant = ""
            }
        };

        if (!TryComputeFreeSplitSlots("10.0.0.0/16", oneChild, out _, out tot, out fr, out ov)
            || tot != 256UL
            || fr != 255UL
            || ov)
        {
            return false;
        }

        if (!TryComputeFreeSplitSlots("192.168.0.0/29", empty, out tl, out tot, out fr, out ov)
            || tl != 30
            || tot < 2UL
            || fr < 2UL)
        {
            return false;
        }

        return true;
    }
}
