using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace DHCPSwitches;

// Prefixes table: available free-slot rows (one row per block when reasonable) + child prefixes; IPs shown in drilled folder view only.

public static partial class IPAMOverlay
{
    private const float IpamAssignedIpSubRowH = 17f;

    private struct MergedFolderRow
    {
        public bool IsFreeGap;
        public IpamPrefixEntry Prefix;
        public string ParentFolderId;
        /// <summary>Set with <see cref="IpamFreeSpace"/> maximal cover; slot fields below unused.</summary>
        public string FreeExplicitCidr;
        public ulong FreeFirstSlot;
        public ulong FreeSlotCount;
        public int TemplateLen;
    }

    /// <summary>
    /// Beyond this, one merged “Available · first … last (N×)” row is used for that run so the table stays usable.
    /// </summary>
    private const int IpamMaxFreeSlotsListedAsSeparateRows = 400;

    private static List<MergedFolderRow> BuildMergedFolderRows(IReadOnlyList<IpamPrefixEntry> all, string parentId)
    {
        var q = all.Where(p =>
                string.IsNullOrEmpty(parentId)
                    ? string.IsNullOrEmpty(p.ParentId)
                    : string.Equals(p.ParentId, parentId, StringComparison.Ordinal))
            .OrderBy(static p => NetworkSortKey(p))
            .ToList();

        var rows = new List<MergedFolderRow>();
        if (!string.IsNullOrEmpty(parentId))
        {
            var parentEntry = all.FirstOrDefault(p => string.Equals(p.Id, parentId, StringComparison.Ordinal));

            // “Available · …” rows only for the folder you drilled into—not as nested rows in the full tree.
            var listingCurrentDrillFolder =
                !string.IsNullOrEmpty(_ipamPrefixesDrillParentId)
                && string.Equals(parentId, _ipamPrefixesDrillParentId, StringComparison.Ordinal);

            // Hide free splits when hosts exist only if we also show the IPv4-in-folder panel (/17+); keep /16 and shorter parents showing free space.
            const int hideFreeGapsWithHostsPrefixLenThreshold = 16;
            var showFreeGaps = false;
            if (listingCurrentDrillFolder && parentEntry != null)
            {
                // Only suppress when *this* folder directly owns host IPs—not addresses delegated to a child prefix
                // (otherwise /24 would hide “Available” rows while servers live under /27).
                var exclusiveHostCount = CollectServersExclusiveToPrefixForDrilledPanel(parentEntry, all).Count;
                var hasLen = RouteMath.TryParseIpv4Cidr((parentEntry.Cidr ?? "").Trim(), out _, out var pl);
                var suppressBecauseHosts =
                    exclusiveHostCount > 0 && hasLen && pl > hideFreeGapsWithHostsPrefixLenThreshold;
                showFreeGaps = !suppressBecauseHosts;
            }

            if (showFreeGaps
                && parentEntry != null
                && IpamFreeSpace.TryEnumerateMaximalFreeCidrs((parentEntry.Cidr ?? "").Trim(), q, out var maximal)
                && maximal.Count > 0)
            {
                foreach (var fc in maximal)
                {
                    rows.Add(new MergedFolderRow
                    {
                        IsFreeGap = true,
                        ParentFolderId = parentId,
                        FreeExplicitCidr = fc,
                    });
                }
            }
        }

        foreach (var p in q)
        {
            rows.Add(new MergedFolderRow { IsFreeGap = false, Prefix = p });
        }

        rows.Sort((a, b) => CompareMergedFolderRowsByAddress(a, b, all));
        return rows;
    }

    private static int CompareMergedFolderRowsByAddress(MergedFolderRow a, MergedFolderRow b, IReadOnlyList<IpamPrefixEntry> all)
    {
        var na = RowNetworkUint(a, all);
        var nb = RowNetworkUint(b, all);
        var c = na.CompareTo(nb);
        if (c != 0)
        {
            return c;
        }

        var pla = RowPrefixLenOrZero(a, all);
        var plb = RowPrefixLenOrZero(b, all);
        return pla.CompareTo(plb);
    }

    private static uint RowNetworkUint(MergedFolderRow m, IReadOnlyList<IpamPrefixEntry> all)
    {
        if (!m.IsFreeGap && m.Prefix != null)
        {
            return RouteMath.TryParseIpv4Cidr((m.Prefix.Cidr ?? "").Trim(), out var rowNet, out _)
                ? rowNet
                : uint.MaxValue;
        }

        if (m.IsFreeGap && !string.IsNullOrEmpty(m.FreeExplicitCidr))
        {
            return RouteMath.TryParseIpv4Cidr(m.FreeExplicitCidr.Trim(), out var net2, out _)
                ? net2
                : uint.MaxValue;
        }

        var parent = all.FirstOrDefault(p => string.Equals(p.Id, m.ParentFolderId, StringComparison.Ordinal));
        if (parent == null)
        {
            return uint.MaxValue;
        }

        if (!RouteMath.TryParseIpv4Cidr((parent.Cidr ?? "").Trim(), out var pn, out var pl))
        {
            return uint.MaxValue;
        }

        if (!RouteMath.TryGetNetworkForTemplateSlot(pn, pl, m.FreeFirstSlot, m.TemplateLen, out var slotNet))
        {
            return uint.MaxValue;
        }

        return slotNet;
    }

    private static int RowPrefixLenOrZero(MergedFolderRow m, IReadOnlyList<IpamPrefixEntry> all)
    {
        if (!m.IsFreeGap && m.Prefix != null)
        {
            return RouteMath.TryParseIpv4Cidr((m.Prefix.Cidr ?? "").Trim(), out _, out var pl) ? pl : 32;
        }

        if (m.IsFreeGap && !string.IsNullOrEmpty(m.FreeExplicitCidr))
        {
            return RouteMath.TryParseIpv4Cidr(m.FreeExplicitCidr.Trim(), out _, out var pl2) ? pl2 : 32;
        }

        return m.TemplateLen;
    }

    private static ulong MergedFolderSortKey(MergedFolderRow m, IReadOnlyList<IpamPrefixEntry> all)
    {
        if (!m.IsFreeGap && m.Prefix != null)
        {
            return NetworkSortKey(m.Prefix);
        }

        if (m.IsFreeGap && !string.IsNullOrEmpty(m.FreeExplicitCidr))
        {
            if (RouteMath.TryParseIpv4Cidr(m.FreeExplicitCidr.Trim(), out var fn, out var fpl))
            {
                return ((ulong)fn << 8) | (uint)fpl;
            }

            return ulong.MaxValue;
        }

        var parent = all.FirstOrDefault(p => string.Equals(p.Id, m.ParentFolderId, StringComparison.Ordinal));
        if (parent == null)
        {
            return ulong.MaxValue;
        }

        if (!RouteMath.TryParseIpv4Cidr((parent.Cidr ?? "").Trim(), out var pn, out var pl))
        {
            return ulong.MaxValue;
        }

        if (!RouteMath.TryGetNetworkForTemplateSlot(pn, pl, m.FreeFirstSlot, m.TemplateLen, out var net))
        {
            return ulong.MaxValue;
        }

        return ((ulong)net << 8) | (uint)m.TemplateLen;
    }

    /// <summary>
    /// All servers whose IPv4 lies in <paramref name="p"/>'s CIDR. <paramref name="IsExclusiveLeaf"/> is true when this
    /// prefix is also the narrowest (leaf) prefix containing that IP — false when a child prefix owns the address.
    /// </summary>
    private static List<(Server Server, string Ip, bool IsExclusiveLeaf)> CollectServersInPrefixCidrForIpList(
        IpamPrefixEntry p,
        IReadOnlyList<IpamPrefixEntry> all)
    {
        var list = new List<(Server, string, bool)>();
        if (p == null)
        {
            return list;
        }

        var cidr = (p.Cidr ?? "").Trim();
        if (string.IsNullOrWhiteSpace(cidr) || !RouteMath.TryParseIpv4Cidr(cidr, out _, out _))
        {
            return list;
        }

        foreach (var s in _cachedServers)
        {
            if (s == null)
            {
                continue;
            }

            var ip = DHCPManager.GetServerIP(s);
            if (string.IsNullOrWhiteSpace(ip) || ip == "0.0.0.0")
            {
                continue;
            }

            var t = ip.Trim();
            if (!RouteMath.IsIpv4InCidr(t, cidr))
            {
                continue;
            }

            var exclusive = TryGetMostSpecificContainingPrefix(t, all, out var winner)
                            && winner != null
                            && string.Equals(winner.Id, p.Id, StringComparison.Ordinal);
            list.Add((s, t, exclusive));
        }

        list.Sort((a, b) =>
        {
            var ta = a.Item2.Trim();
            var tb = b.Item2.Trim();
            if (RouteMath.TryIpv4StringToUint(ta, out var ua) && RouteMath.TryIpv4StringToUint(tb, out var ub))
            {
                return ua.CompareTo(ub);
            }

            return string.CompareOrdinal(ta, tb);
        });
        return list;
    }

    /// <summary>
    /// Servers whose IPv4 maps to this prefix as the narrowest containing IPAM row (excludes addresses owned by child prefixes).
    /// </summary>
    private static List<(Server Server, string Ip)> CollectServersExclusiveToPrefixForDrilledPanel(
        IpamPrefixEntry p,
        IReadOnlyList<IpamPrefixEntry> all)
    {
        var list = new List<(Server, string)>();
        if (p == null)
        {
            return list;
        }

        var cidr = (p.Cidr ?? "").Trim();
        if (string.IsNullOrWhiteSpace(cidr) || !RouteMath.TryParseIpv4Cidr(cidr, out _, out _))
        {
            return list;
        }

        foreach (var s in _cachedServers)
        {
            if (s == null)
            {
                continue;
            }

            var ip = DHCPManager.GetServerIP(s);
            if (string.IsNullOrWhiteSpace(ip) || ip == "0.0.0.0")
            {
                continue;
            }

            var t = ip.Trim();
            if (!RouteMath.IsIpv4InCidr(t, cidr))
            {
                continue;
            }

            if (!TryGetMostSpecificContainingPrefix(t, all, out var winner) || winner == null)
            {
                continue;
            }

            if (!string.Equals(winner.Id, p.Id, StringComparison.Ordinal))
            {
                continue;
            }

            list.Add((s, t));
        }

        list.Sort((a, b) =>
        {
            if (RouteMath.TryIpv4StringToUint(a.Item2, out var ua) && RouteMath.TryIpv4StringToUint(b.Item2, out var ub))
            {
                return ua.CompareTo(ub);
            }

            return string.CompareOrdinal(a.Item2, b.Item2);
        });
        return list;
    }

    private static void WalkVisibleMerged(IReadOnlyList<IpamPrefixEntry> all, string parentId, ref int n)
    {
        foreach (var mrow in BuildMergedFolderRows(all, parentId))
        {
            if (mrow.IsFreeGap)
            {
                n++;
                continue;
            }

            var p = mrow.Prefix;
            if (p == null)
            {
                continue;
            }

            n++;
            var directChildren = all.Count(c =>
                c != null && string.Equals(c.ParentId, p.Id, StringComparison.Ordinal));

            if (directChildren == 0 || !_ipamPrefixCollapsedIds.Contains(p.Id))
            {
                WalkVisibleMerged(all, p.Id, ref n);
            }
        }
    }

    /// <summary>Counts only direct children + free rows for this folder (no nested subtree).</summary>
    private static void WalkVisibleMergedFlat(IReadOnlyList<IpamPrefixEntry> all, string parentId, ref int n)
    {
        foreach (var _ in BuildMergedFolderRows(all, parentId))
        {
            n++;
        }
    }

    private static void DrawPrefixTreeRows(
        IReadOnlyList<IpamPrefixEntry> all,
        string parentId,
        int depth,
        float x0,
        ref float y,
        float cardW,
        float colPrefix,
        float colStatus,
        float colChild,
        float colFree,
        float colUtil,
        float colTenant,
        float colActions,
        bool flatDirectChildrenOnly = false)
    {
        foreach (var mrow in BuildMergedFolderRows(all, parentId))
        {
            DrawMergedFolderRow(
                mrow,
                all,
                depth,
                x0,
                ref y,
                cardW,
                colPrefix,
                colStatus,
                colChild,
                colFree,
                colUtil,
                colTenant,
                colActions,
                flatDirectChildrenOnly);
        }
    }

    private static void DrawMergedFolderRow(
        MergedFolderRow mrow,
        IReadOnlyList<IpamPrefixEntry> all,
        int depth,
        float x0,
        ref float y,
        float cardW,
        float colPrefix,
        float colStatus,
        float colChild,
        float colFree,
        float colUtil,
        float colTenant,
        float colActions,
        bool flatDirectChildrenOnly)
    {
        if (mrow.IsFreeGap)
        {
            DrawFreeGapPrefixRow(
                mrow,
                all,
                depth,
                x0,
                ref y,
                cardW,
                colPrefix,
                colStatus,
                colChild,
                colFree,
                colUtil,
                colTenant,
                colActions);
            return;
        }

        var p = mrow.Prefix;
        if (p == null)
        {
            return;
        }

        DrawSinglePrefixRow(
            p,
            all,
            depth,
            x0,
            ref y,
            cardW,
            colPrefix,
            colStatus,
            colChild,
            colFree,
            colUtil,
            colTenant,
            colActions,
            flatDirectChildrenOnly);
    }

    private static void DrawFreeGapPrefixRow(
        MergedFolderRow mrow,
        IReadOnlyList<IpamPrefixEntry> all,
        int depth,
        float x0,
        ref float y,
        float cardW,
        float colPrefix,
        float colStatus,
        float colChild,
        float colFree,
        float colUtil,
        float colTenant,
        float colActions)
    {
        var parentFolderId = mrow.ParentFolderId;
        var parentEntry = all.FirstOrDefault(p => string.Equals(p.Id, parentFolderId, StringComparison.Ordinal));
        var pc = (parentEntry?.Cidr ?? "").Trim();
        string firstCidr = "";
        string label;
        if (!string.IsNullOrEmpty(mrow.FreeExplicitCidr))
        {
            firstCidr = mrow.FreeExplicitCidr.Trim();
            label = $"Available · {firstCidr}";
        }
        else if (!RouteMath.TryParseIpv4Cidr(pc, out var pNet, out var pLen)
                 || !RouteMath.TryGetNetworkForTemplateSlot(pNet, pLen, mrow.FreeFirstSlot, mrow.TemplateLen, out var firstNet))
        {
            label = "Available space";
        }
        else
        {
            firstCidr = RouteMath.FormatIpv4Cidr(firstNet, mrow.TemplateLen);
            if (mrow.FreeSlotCount <= 1)
            {
                label = $"Available · {firstCidr}";
            }
            else
            {
                var lastSlot = mrow.FreeFirstSlot + mrow.FreeSlotCount - 1UL;
                if (RouteMath.TryGetNetworkForTemplateSlot(pNet, pLen, lastSlot, mrow.TemplateLen, out var lastNet))
                {
                    var lastCidr = RouteMath.FormatIpv4Cidr(lastNet, mrow.TemplateLen);
                    label =
                        $"Available · {firstCidr} … {lastCidr}  ({mrow.FreeSlotCount} × /{mrow.TemplateLen})";
                }
                else
                {
                    label = $"Available · {firstCidr} ({mrow.FreeSlotCount} blocks)";
                }
            }
        }

        var r = new Rect(x0, y, cardW, TableRowH);
        var e = Event.current;
        var indent = 14f * depth;
        var labelLeft = x0 + 6f + indent;

        if (e.type == EventType.Repaint)
        {
            DrawTintedRect(r, new Color(0.06f, 0.12f, 0.14f, 0.55f));
        }

        GUI.Label(new Rect(labelLeft, y, Mathf.Max(20f, cardW - colActions - 16f), TableRowH), label, _stMuted);
        GUI.Label(new Rect(x0 + colPrefix, y, colStatus, TableRowH), "Free", _stMuted);
        GUI.Label(new Rect(x0 + colPrefix + colStatus, y, colChild, TableRowH), "—", _stMuted);
        GUI.Label(new Rect(x0 + colPrefix + colStatus + colChild, y, colFree, TableRowH), "—", _stMuted);
        var utilColLeft = x0 + colPrefix + colStatus + colChild + colFree;
        GUI.Label(new Rect(utilColLeft, y, colUtil, TableRowH), "—", _stMuted);
        GUI.Label(new Rect(x0 + colPrefix + colStatus + colChild + colFree + colUtil, y, colTenant, TableRowH), "—", _stMuted);

        var actX = x0 + colPrefix + colStatus + colChild + colFree + colUtil + colTenant;
        var plusKey = 925000
                       + Mathf.Abs(
                           unchecked(
                               HashCode.Combine(
                                   mrow.ParentFolderId ?? "",
                                   (long)mrow.FreeFirstSlot,
                                   mrow.TemplateLen,
                                   (int)mrow.FreeSlotCount)))
                       % 78000;
        if (!string.IsNullOrEmpty(firstCidr)
            && ImguiButtonOnce(new Rect(actX + 2f, y + 3f, 28f, TableRowH - 6f), "+", plusKey, _stPrimaryBtn))
        {
            OpenIpamChildPrefixWizardCreate(mrow.ParentFolderId, firstCidr);
        }

        y += TableRowH;
    }

    private static void DrawSinglePrefixRow(
        IpamPrefixEntry p,
        IReadOnlyList<IpamPrefixEntry> all,
        int depth,
        float x0,
        ref float y,
        float cardW,
        float colPrefix,
        float colStatus,
        float colChild,
        float colFree,
        float colUtil,
        float colTenant,
        float colActions,
        bool flatDirectChildrenOnly = false)
    {
        var actXEarly = x0 + colPrefix + colStatus + colChild + colFree + colUtil + colTenant;
        var rowBodyRect = new Rect(x0, y, Mathf.Max(40f, actXEarly - x0 - 4f), TableRowH);

        var r = new Rect(x0, y, cardW, TableRowH);
        var e = Event.current;

        var indent = 14f * depth;
        var cidr = (p.Cidr ?? "").Trim();
        var name = string.IsNullOrEmpty(p.Name) ? "" : $"  ({p.Name})";
        var directChildren = all.Count(c => string.Equals(c.ParentId, p.Id, StringComparison.Ordinal));
        var chevronW = 14f;
        var chevronLeft = x0 + 6f + indent;
        var chevronRect = new Rect(chevronLeft, y, chevronW, TableRowH);
        var showExpandChevron = directChildren > 0 && !flatDirectChildrenOnly;
        var labelLeft = chevronLeft + (showExpandChevron ? chevronW + 2f : 0f);
        var prefixLabelRect = new Rect(labelLeft, y, Mathf.Max(20f, colPrefix - (labelLeft - x0) - 6f), TableRowH);

        if (showExpandChevron && e.type == EventType.MouseDown && e.button == 0 && chevronRect.Contains(e.mousePosition))
        {
            if (_ipamPrefixCollapsedIds.Contains(p.Id))
            {
                _ipamPrefixCollapsedIds.Remove(p.Id);
            }
            else
            {
                _ipamPrefixCollapsedIds.Add(p.Id);
            }

            RecomputeContentHeight();
            e.Use();
        }
        else if (e.type == EventType.MouseDown && e.button == 0 && rowBodyRect.Contains(e.mousePosition))
        {
            _ipamSelectedPrefixId = p.Id;
            var t = Time.realtimeSinceStartup;
            if (string.Equals(_ipamPrefixLastClickedRowId, p.Id, StringComparison.Ordinal) && t - _ipamPrefixLastClickTime < 0.4f)
            {
                _ipamPrefixesDrillParentId = p.Id;
                _ipamPrefixLastClickedRowId = null;
                _ipamPrefixLastClickTime = -1f;
                _scroll = Vector2.zero;
                RecomputeContentHeight();
            }
            else
            {
                _ipamPrefixLastClickedRowId = p.Id;
                _ipamPrefixLastClickTime = t;
            }

            e.Use();
        }

        var alt = (Mathf.FloorToInt(y / TableRowH) % 2) == 1;
        var sel = string.Equals(_ipamSelectedPrefixId, p.Id, StringComparison.Ordinal);
        if (e.type == EventType.Repaint)
        {
            var tint = sel ? new Color(0.12f, 0.28f, 0.38f, 0.85f) : (alt ? new Color(0.06f, 0.08f, 0.1f, 0.5f) : new Color(0.04f, 0.05f, 0.06f, 0.35f));
            DrawTintedRect(r, tint);
        }

        var hasParent = !string.IsNullOrEmpty(p.ParentId);
        string roleLabel;
        if (directChildren > 0)
        {
            roleLabel = "Parent";
        }
        else if (hasParent)
        {
            roleLabel = "Child";
        }
        else
        {
            roleLabel = "Root";
        }

        var cap = Mathf.Max(1, RouteMath.CountDhcpUsableHosts(cidr));
        var used = directChildren > 0
            ? CountAssignedServersWithIpInCidr(cidr)
            : CountAssignedServersExclusiveToPrefix(p, all);
        var util = Mathf.Clamp01(used / (float)cap);

        if (showExpandChevron)
        {
            var collapsed = _ipamPrefixCollapsedIds.Contains(p.Id);
            GUI.Label(
                chevronRect,
                new GUIContent(collapsed ? "▸" : "▾", "Show or hide child prefixes in this list (does not change drill scope)."),
                _stTableCell);
        }

        GUI.Label(new Rect(labelLeft, y, prefixLabelRect.width, TableRowH), cidr + name, _stTableCell);
        GUI.Label(new Rect(x0 + colPrefix, y, colStatus, TableRowH), roleLabel, _stTableCell);
        GUI.Label(
            new Rect(x0 + colPrefix + colStatus, y, colChild, TableRowH),
            directChildren.ToString(CultureInfo.InvariantCulture),
            _stTableCell);

        var kids = all.Where(c => c != null && string.Equals(c.ParentId, p.Id, StringComparison.Ordinal)).ToList();
        string freeDisp;
        string freeTip;
        if (!IpamPrefixAvailability.TryComputeFreeSplitSlots(cidr, kids, out var tpl, out _, out var fr, out var ov))
        {
            freeDisp = "—";
            freeTip = "Could not compute free splits for this prefix (invalid CIDR or no template).";
        }
        else if (tpl == 24)
        {
            freeDisp = ov ? $"~{fr}" : fr.ToString(CultureInfo.InvariantCulture);
            freeTip =
                $"Free /{tpl} subnets (aligned blocks) under this prefix after direct children. ~ means overlapping child ranges so the count is conservative.";
        }
        else if (IpamFreeSpace.TryFormatAggregatedFreeBlockCounts(cidr, kids, out var agg, out var aggTip))
        {
            freeDisp = ov ? $"~{agg}" : agg;
            freeTip = aggTip + (ov ? " Overlapping child ranges: split counts may be conservative." : "");
        }
        else
        {
            freeDisp = "0";
            freeTip = "No CIDR-aligned free space remains under this prefix after direct children.";
        }

        GUI.Label(
            new Rect(x0 + colPrefix + colStatus + colChild, y, colFree, TableRowH),
            new GUIContent(freeDisp, freeTip),
            ov ? _stMuted : _stTableCell);

        var utilColLeft = x0 + colPrefix + colStatus + colChild + colFree;
        const float utilPctReserve = 76f;
        var barX = utilColLeft + 4f;
        var barW = Mathf.Max(28f, colUtil - utilPctReserve - 10f);
        var barH = TableRowH - 10f;
        var barY = y + 5f;
        if (e.type == EventType.Repaint)
        {
            DrawTintedRect(new Rect(barX, barY, barW, barH), new Color(0.2f, 0.22f, 0.25f, 1f));
            DrawTintedRect(new Rect(barX, barY, barW * util, barH), new Color(0.2f, 0.75f, 0.45f, 1f));
        }

        var pct = Mathf.RoundToInt(util * 100f);
        var pctLeft = barX + barW + 6f;
        var utilBandRight = utilColLeft + colUtil - 6f;
        var pctW = Mathf.Max(36f, utilBandRight - pctLeft);
        GUI.Label(
            new Rect(pctLeft, y, pctW, TableRowH),
            $"{pct}% ({used}/{cap})",
            _stMuted);

        var tenantDisp = string.IsNullOrEmpty(p.Tenant) ? "—" : p.Tenant;
        GUI.Label(new Rect(x0 + colPrefix + colStatus + colChild + colFree + colUtil, y, colTenant, TableRowH), tenantDisp, _stTableCell);

        GUI.Label(new Rect(actXEarly + 2f, y + 2f, colActions - 4f, TableRowH - 4f), "—", _stMuted);

        y += TableRowH;

        if (!flatDirectChildrenOnly && (directChildren == 0 || !_ipamPrefixCollapsedIds.Contains(p.Id)))
        {
            DrawPrefixTreeRows(
                all,
                p.Id,
                depth + 1,
                x0,
                ref y,
                cardW,
                colPrefix,
                colStatus,
                colChild,
                colFree,
                colUtil,
                colTenant,
                colActions,
                flatDirectChildrenOnly);
        }
    }
}
