using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace GregModIPAM;

// Prefixes (parent/child, utilization) and VLAN list for the IPAM nav section.

public static partial class IPAMOverlay
{
    /// <summary>Max lines drawn in the drilled-folder “IPv4 in this prefix” block (scroll the window for the rest).</summary>
    private const int IpamDrilledIpListDisplayMax = 400;

    /// <summary>Hide the per-host IPv4 table for aggregate prefixes (/16 and shorter); show only the prefix tree.</summary>
    private const int IpamDrilledIpv4HideWhenPrefixLenAtOrBelow = 16;

    private static string _ipamPrefixFormCidr = "";
    private static string _ipamPrefixFormName = "";
    private static string _ipamPrefixFormError = "";
    private static string _ipamSelectedPrefixId;

    private static string _ipamVlanFormId = "";
    private static string _ipamVlanFormName = "";
    private static string _ipamVlanFormError = "";

    private static void DrawIpamSubNav(Rect r, IpamSubSection sub, string text, int dedupeKey)
    {
        var active = _navSection == NavSection.Ipam && _ipamSub == sub;
        if (active)
        {
            GUI.DrawTexture(r, _texNavActive);
            GUI.Label(new Rect(r.x + 6, r.y, r.width - 8, r.height), text, _stNavItemActive);
            return;
        }

        if (ImguiButtonOnce(r, text, dedupeKey, _stNavBtn))
        {
            _ipamFormFieldFocus = IpamFormFocusNone;
            _ipamIpAddrPageMenuOpen = false;
            _ipamPrefixPageMenuOpen = false;
            _ipamDevicesSwitchPageMenuOpen = false;
            _ipamDevicesFirewallPageMenuOpen = false;
            _ipamDevicesServerPageMenuOpen = false;
            _customersTabAddServerWizardOpen = false;
            if (sub != IpamSubSection.Prefixes)
            {
                _ipamPrefixesDrillParentId = null;
                _ipamPrefixAddAsRoot = false;
                CloseIpamChildPrefixWizard();
            }

            if (sub == IpamSubSection.Prefixes)
            {
                _ipamIpAddressFilterCidr = null;
            }

            _navSection = NavSection.Ipam;
            _ipamSub = sub;
            _scroll = Vector2.zero;
            RecomputeContentHeight();
        }
    }

    private static int CountPrefixTreeRowsForHeight()
    {
        var prefixes = IpamDataStore.GetPrefixes();
        if (prefixes.Count == 0)
        {
            return 1;
        }

        if (string.IsNullOrEmpty(_ipamPrefixesDrillParentId))
        {
            var n = 0;
            WalkVisibleMerged(prefixes, null, ref n);
            return n;
        }

        if (prefixes.All(p => p.Id != _ipamPrefixesDrillParentId))
        {
            var n2 = 0;
            WalkVisibleMerged(prefixes, null, ref n2);
            return n2;
        }

        var merged = BuildMergedFolderRows(prefixes, _ipamPrefixesDrillParentId);
        NormalizeIpamPrefixPageSize();
        ClampIpamPrefixPageIndex(merged.Count);
        var start = _ipamPrefixPageIndex * _ipamPrefixPageSize;
        var visible = Mathf.Min(_ipamPrefixPageSize, Mathf.Max(0, merged.Count - start));
        return Mathf.Max(1, visible);
    }

    /// <summary>Height of the drilled-folder prefix pagination bar (matches IP address table).</summary>
    private const float IpamPrefixPaginationBarH = 28f;

    private static void NormalizeIpamPrefixPageSize()
    {
        if (_ipamPrefixPageSize != 25 && _ipamPrefixPageSize != 50 && _ipamPrefixPageSize != 100)
        {
            _ipamPrefixPageSize = 50;
        }
    }

    private static void ClampIpamPrefixPageIndex(int totalCount)
    {
        NormalizeIpamPrefixPageSize();
        var ps = _ipamPrefixPageSize;
        if (totalCount <= 0)
        {
            _ipamPrefixPageIndex = 0;
            return;
        }

        var maxPage = (totalCount - 1) / ps;
        if (_ipamPrefixPageIndex > maxPage)
        {
            _ipamPrefixPageIndex = maxPage;
        }

        if (_ipamPrefixPageIndex < 0)
        {
            _ipamPrefixPageIndex = 0;
        }
    }

    private static void DrawIpamPrefixFolderPagination(float x0, ref float y, float cardW, int totalRows)
    {
        var tableW = cardW - IpamIpAddressGearColW;
        NormalizeIpamPrefixPageSize();
        ClampIpamPrefixPageIndex(totalRows);
        var ps = _ipamPrefixPageSize;
        var pageCount = totalRows == 0 ? 1 : (totalRows + ps - 1) / ps;
        var pageStart = _ipamPrefixPageIndex * ps;
        var pageEnd = totalRows == 0 ? 0 : Mathf.Min(totalRows, pageStart + ps);

        var navY = y + 1f;
        var gearRect = new Rect(x0 + tableW, y, IpamIpAddressGearColW, 22f);
        var menuDropRect = new Rect(x0 + cardW - 132f, y + 22f, 128f, 68f);
        var eClose = Event.current;
        if (eClose != null && eClose.type == EventType.MouseDown && eClose.button == 0 && _ipamPrefixPageMenuOpen)
        {
            if (!menuDropRect.Contains(eClose.mousePosition) && !gearRect.Contains(eClose.mousePosition))
            {
                _ipamPrefixPageMenuOpen = false;
            }
        }

        var label = totalRows == 0
            ? "No rows"
            : $"Page {_ipamPrefixPageIndex + 1} / {pageCount}   ·   {pageStart + 1}-{pageEnd} of {totalRows}";
        GUI.Label(new Rect(x0, y + 2f, tableW - 200f, 22f), label, _stHint);
        if (ImguiButtonOnce(new Rect(x0 + tableW - 168f, navY, 72f, 22f), "Previous", 9132, _stMutedBtn))
        {
            if (_ipamPrefixPageIndex > 0)
            {
                _ipamPrefixPageIndex--;
                RecomputeContentHeight();
            }
        }

        if (ImguiButtonOnce(new Rect(x0 + tableW - 90f, navY, 82f, 22f), "Next", 9133, _stMutedBtn))
        {
            if (_ipamPrefixPageIndex < pageCount - 1)
            {
                _ipamPrefixPageIndex++;
                RecomputeContentHeight();
            }
        }

        if (ImguiButtonOnce(gearRect, "\u2699", 9134, _stMutedBtn))
        {
            _ipamPrefixPageMenuOpen = !_ipamPrefixPageMenuOpen;
            _ipamIpAddrPageMenuOpen = false;
        }

        DrawIpamPrefixPageSizePopup(menuDropRect);
        y += IpamPrefixPaginationBarH;
    }

    private static void DrawIpamPrefixPageSizePopup(Rect menuDropRect)
    {
        if (!_ipamPrefixPageMenuOpen)
        {
            return;
        }

        if (Event.current.type == EventType.Repaint)
        {
            DrawTintedRect(menuDropRect, new Color(0.08f, 0.1f, 0.12f, 0.96f));
        }

        var optY = menuDropRect.y + 4f;
        if (ImguiButtonOnce(new Rect(menuDropRect.x + 4f, optY, menuDropRect.width - 8f, 20f), "25 per page", 9135, _stMutedBtn))
        {
            _ipamPrefixPageSize = 25;
            _ipamPrefixPageIndex = 0;
            _ipamPrefixPageMenuOpen = false;
            RecomputeContentHeight();
        }

        optY += 22f;
        if (ImguiButtonOnce(new Rect(menuDropRect.x + 4f, optY, menuDropRect.width - 8f, 20f), "50 per page", 9136, _stMutedBtn))
        {
            _ipamPrefixPageSize = 50;
            _ipamPrefixPageIndex = 0;
            _ipamPrefixPageMenuOpen = false;
            RecomputeContentHeight();
        }

        optY += 22f;
        if (ImguiButtonOnce(new Rect(menuDropRect.x + 4f, optY, menuDropRect.width - 8f, 20f), "100 per page", 9137, _stMutedBtn))
        {
            _ipamPrefixPageSize = 100;
            _ipamPrefixPageIndex = 0;
            _ipamPrefixPageMenuOpen = false;
            RecomputeContentHeight();
        }
    }

    private static ulong NetworkSortKey(IpamPrefixEntry p)
    {
        if (p == null || !RouteMath.TryParseIpv4Cidr((p.Cidr ?? "").Trim(), out var net, out var len))
        {
            return ulong.MaxValue;
        }

        return ((ulong)net << 8) | (uint)len;
    }

    private static void DrawIpamPrefixesView(float innerW)
    {
        var x0 = CardPad;
        var y = CardPad;
        var cardW = innerW - CardPad * 2f;
        _lastInventoryCardWidth = cardW;

        GUI.Label(new Rect(x0, y - 2, cardW, SectionTitleH), "Organization  /  IPAM  /  Prefixes", _stBreadcrumb);
        y += SectionTitleH + 2f;
        GUI.DrawTexture(new Rect(x0, y, cardW, 1f), _texTableHeader);
        y += 8f;

        var prefixes = IpamDataStore.GetPrefixes();
        var drillPagingSig = _ipamPrefixesDrillParentId ?? "";
        if (!string.Equals(_ipamPrefixPagingDrillSig, drillPagingSig, StringComparison.Ordinal))
        {
            _ipamPrefixPagingDrillSig = drillPagingSig;
            _ipamPrefixPageIndex = 0;
        }

        if (!string.IsNullOrEmpty(_ipamPrefixesDrillParentId) && prefixes.All(p => p.Id != _ipamPrefixesDrillParentId))
        {
            _ipamPrefixesDrillParentId = null;
        }

        GUI.Label(new Rect(x0, y, cardW, 20f), "Add prefix", _stSectionTitle);
        y += 22f;
        GUI.Label(new Rect(x0, y, 52f, 22f), "CIDR", _stFormLabel);
        DrawIpamFormTextField(
            new Rect(x0 + 56f, y, Mathf.Min(220f, cardW - 200f), 22f),
            IpamFormFocusPrefixCidr,
            64,
            IpamTextFieldKind.Cidr);
        GUI.Label(new Rect(x0 + 280f, y, 44f, 22f), "Name", _stFormLabel);
        DrawIpamFormTextField(
            new Rect(x0 + 326f, y, Mathf.Max(80f, cardW - 340f), 22f),
            IpamFormFocusPrefixName,
            128,
            IpamTextFieldKind.Name);
        y += 28f;

        IpamPrefixParentMode addMode;
        Guid? explicitParentId = null;
        if (_ipamPrefixAddAsRoot)
        {
            addMode = IpamPrefixParentMode.ForceRoot;
        }
        else if (!string.IsNullOrEmpty(_ipamSelectedPrefixId) && Guid.TryParse(_ipamSelectedPrefixId, out var selG))
        {
            addMode = IpamPrefixParentMode.ExplicitParent;
            explicitParentId = selG;
        }
        else
        {
            addMode = IpamPrefixParentMode.AutoPickContainedParent;
        }

        string hint;
        if (_ipamPrefixAddAsRoot)
        {
            hint = "Adds a top-level prefix (ignores selection and auto-parent).";
        }
        else if (!string.IsNullOrEmpty(_ipamSelectedPrefixId) && Guid.TryParse(_ipamSelectedPrefixId, out var selForHint))
        {
            hint = $"New prefix will be a child of the selected row ({selForHint:D}).";
        }
        else
        {
            hint = "Parent is chosen automatically: the tightest existing prefix that fully contains your CIDR, or top-level if none fits. Select a row to force a different parent, or use “Add as root”.";
        }

        GUI.Label(new Rect(x0, y, cardW, 36f), hint, _stHint);
        y += 38f;

        var addRootWas = _ipamPrefixAddAsRoot;
        _ipamPrefixAddAsRoot = ImguiToggleOnce(
            new Rect(x0, y, Mathf.Min(360f, cardW - 8f), 22f),
            _ipamPrefixAddAsRoot,
            9108,
            new GUIContent("Add as root prefix (sibling / new top-level)"));
        if (_ipamPrefixAddAsRoot && !addRootWas)
        {
            _ipamSelectedPrefixId = null;
        }

        y += 26f;

        var addRect = new Rect(x0, y, 120f, 26f);
        if (ImguiButtonOnce(addRect, "Add prefix", 9101, _stPrimaryBtn))
        {
            _ipamPrefixFormError = "";
            if (!IpamDataStore.TryAddPrefix(_ipamPrefixFormCidr, _ipamPrefixFormName, null, addMode, explicitParentId, out var err))
            {
                _ipamPrefixFormError = err ?? "Could not add prefix.";
            }
            else
            {
                _ipamPrefixFormCidr = "";
                _ipamSelectedPrefixId = null;
                RecomputeContentHeight();
            }
        }

        var delRect = new Rect(addRect.xMax + 10f, y, 140f, 26f);
        if (ImguiButtonOnce(delRect, "Delete selected", 9102, _stMutedBtn))
        {
            RequestIpamPrefixDelete(_ipamSelectedPrefixId);
        }

        y += 34f;
        if (!string.IsNullOrEmpty(_ipamPrefixFormError))
        {
            GUI.Label(new Rect(x0, y, cardW, 44f), _ipamPrefixFormError, _stError);
            y += 46f;
        }

        y += 6f;
        IpamPrefixEntry ipamDrilledScope = null;
        if (!string.IsNullOrEmpty(_ipamPrefixesDrillParentId))
        {
            ipamDrilledScope = prefixes.FirstOrDefault(p => string.Equals(p.Id, _ipamPrefixesDrillParentId, StringComparison.Ordinal));
            var scopeLabel = ipamDrilledScope == null
                ? "prefix"
                : $"{(ipamDrilledScope.Cidr ?? "").Trim()}{(string.IsNullOrEmpty(ipamDrilledScope.Name) ? "" : $"  ({ipamDrilledScope.Name})")}";
            GUI.Label(new Rect(x0, y, cardW - 268f, SectionTitleH), $"Inside: {scopeLabel}", _stSectionTitle);
            var editFolderRect = new Rect(x0 + cardW - 260f, y, 128f, SectionTitleH);
            var backRect = new Rect(x0 + cardW - 126f, y, 118f, SectionTitleH);
            if (ImguiButtonOnce(editFolderRect, "Edit this prefix", 9107, _stMutedBtn))
            {
                OpenIpamChildPrefixWizardEdit(_ipamPrefixesDrillParentId);
            }

            if (ImguiButtonOnce(backRect, "↑ All prefixes", 9105, _stMutedBtn))
            {
                _ipamPrefixesDrillParentId = null;
                _ipamPrefixLastClickedRowId = null;
                _ipamPrefixLastClickTime = -1f;
                RecomputeContentHeight();
            }

            y += SectionTitleH + 2f;
        }

        var listTitle = string.IsNullOrEmpty(_ipamPrefixesDrillParentId) ? "All prefixes" : "Prefixes in this folder";
        GUI.Label(new Rect(x0, y, cardW, SectionTitleH), listTitle, _stSectionTitle);
        y += SectionTitleH + 2f;
        if (prefixes.Count > 0)
        {
            GUI.Label(
                new Rect(x0, y, cardW - 100f, 20f),
                "Tip: double-click any column on a row (except Actions) to open that prefix and list only its children.",
                _stHint);
            if (ImguiButtonOnce(new Rect(x0 + cardW - 96f, y, 92f, 20f), "Deselect row", 9106, _stMutedBtn))
            {
                _ipamSelectedPrefixId = null;
            }

            y += 22f;
            GUI.Label(
                new Rect(x0, y, cardW, 36f),
                "Utilization: each server is counted only on the narrowest prefix in this table that contains its IP (so overlapping ranges do not double-count). Sibling /24s only show servers in their own octet.",
                _stHint);
            y += 38f;
        }

        y += 4f;

        DrawIpamPrefixTableHeader(x0, y, cardW, out var colPrefix, out var colStatus, out var colChild, out var colFree, out var colUtil, out var colTenant, out var colActions);
        y += TableHeaderH;

        if (prefixes.Count == 0)
        {
            GUI.Label(new Rect(x0, y, cardW, 28f), "No prefixes yet. Add a root CIDR above (e.g. 10.0.0.0/8), then add children (e.g. 10.0.1.0/24 under 10.0.0.0/8).", _stMuted);
            return;
        }

        var treeParentId = string.IsNullOrEmpty(_ipamPrefixesDrillParentId) ? null : _ipamPrefixesDrillParentId;
        var skipPrefixTreeBody = false;
        if (treeParentId != null)
        {
            var mergedTop = BuildMergedFolderRows(prefixes, treeParentId);
            if (mergedTop.Count == 0)
            {
                GUI.Label(
                    new Rect(x0, y, cardW, 44f),
                    "No child prefixes or visible free splits in this folder yet. Use Add prefix above, or open the parent prefix to create from Available rows.",
                    _stMuted);
                y += 48f;
                skipPrefixTreeBody = true;
            }
        }

        if (!skipPrefixTreeBody)
        {
            if (treeParentId != null)
            {
                var mergedAll = BuildMergedFolderRows(prefixes, treeParentId);
                NormalizeIpamPrefixPageSize();
                ClampIpamPrefixPageIndex(mergedAll.Count);
                var ps = _ipamPrefixPageSize;
                var start = _ipamPrefixPageIndex * ps;
                var nPage = Mathf.Min(ps, Mathf.Max(0, mergedAll.Count - start));
                for (var i = 0; i < nPage; i++)
                {
                    DrawMergedFolderRow(
                        mergedAll[start + i],
                        prefixes,
                        0,
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
                        true);
                }

                if (mergedAll.Count > 0)
                {
                    DrawIpamPrefixFolderPagination(x0, ref y, cardW, mergedAll.Count);
                }
            }
            else
            {
                DrawPrefixTreeRows(
                    prefixes,
                    treeParentId,
                    0,
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
                    false);
            }
        }

        if (ipamDrilledScope != null && ShouldShowDrilledPrefixIpv4Section(ipamDrilledScope))
        {
            y += 10f;
            DrawDrilledPrefixIpAssignmentsBlock(x0, ref y, cardW, ipamDrilledScope, prefixes);
        }
    }

    private static void IpamPruneDrillAfterPrefixMutation()
    {
        if (string.IsNullOrEmpty(_ipamPrefixesDrillParentId))
        {
            return;
        }

        var all = IpamDataStore.GetPrefixes();
        if (all.All(p => p.Id != _ipamPrefixesDrillParentId))
        {
            _ipamPrefixesDrillParentId = null;
        }
    }

    /// <summary>
    /// Counts servers whose IPv4 lies inside <paramref name="cidr"/> (one count per server). Used for parent-prefix
    /// utilization so totals include addresses delegated to child prefixes.
    /// </summary>
    private static int CountAssignedServersWithIpInCidr(string cidr)
    {
        if (string.IsNullOrWhiteSpace(cidr) || !RouteMath.TryParseIpv4Cidr(cidr.Trim(), out _, out _))
        {
            return 0;
        }

        cidr = cidr.Trim();
        var n = 0;
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

            if (RouteMath.IsIpv4InCidr(ip.Trim(), cidr))
            {
                n++;
            }
        }

        return n;
    }

    /// <summary>
    /// Counts servers whose IP falls in <paramref name="p"/>.Cidr only when that prefix is the
    /// <b>most specific</b> (longest mask) among all known IPAM prefixes containing the IP — avoids double-count
    /// when e.g. 10.0.0.0/8 and 10.10.1.0/24 both exist, and matches NetBox-style attribution.
    /// </summary>
    private static int CountAssignedServersExclusiveToPrefix(IpamPrefixEntry p, IReadOnlyList<IpamPrefixEntry> all)
    {
        if (p == null || all == null || all.Count == 0)
        {
            return 0;
        }

        var n = 0;
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

            if (!TryGetMostSpecificContainingPrefix(ip.Trim(), all, out var winner) || winner == null)
            {
                continue;
            }

            if (string.Equals(winner.Id, p.Id, StringComparison.Ordinal))
            {
                n++;
            }
        }

        return n;
    }

    private static bool TryGetMostSpecificContainingPrefix(string ip, IReadOnlyList<IpamPrefixEntry> all, out IpamPrefixEntry winner)
    {
        winner = null;
        var bestLen = -1;
        foreach (var q in all)
        {
            if (q == null)
            {
                continue;
            }

            var qc = (q.Cidr ?? "").Trim();
            if (!RouteMath.TryParseIpv4Cidr(qc, out _, out var qLen))
            {
                continue;
            }

            if (!RouteMath.IsIpv4InCidr(ip, qc))
            {
                continue;
            }

            if (qLen > bestLen)
            {
                bestLen = qLen;
                winner = q;
            }
            else if (qLen == bestLen && winner != null && string.CompareOrdinal(q.Id, winner.Id) < 0)
            {
                winner = q;
            }
        }

        return winner != null;
    }

    private static float ComputeIpamPrefixesContentHeight()
    {
        var formBlock = 108f + 40f + 38f + 26f;
        if (!string.IsNullOrEmpty(_ipamPrefixFormError))
        {
            formBlock += 46f;
        }

        if (!string.IsNullOrEmpty(_ipamPrefixesDrillParentId))
        {
            formBlock += SectionTitleH + 4f;
        }

        if (IpamDataStore.GetPrefixes().Count > 0)
        {
            formBlock += 62f;
        }

        var drilledIpBlockH = 0f;
        if (!string.IsNullOrEmpty(_ipamPrefixesDrillParentId))
        {
            var all = IpamDataStore.GetPrefixes();
            var ds = all.FirstOrDefault(p => string.Equals(p.Id, _ipamPrefixesDrillParentId, StringComparison.Ordinal));
            if (ds != null && ShouldShowDrilledPrefixIpv4Section(ds))
            {
                var list = CollectServersExclusiveToPrefixForDrilledPanel(ds, all);
                var show = Mathf.Min(list.Count, IpamDrilledIpListDisplayMax);
                drilledIpBlockH = SectionTitleH + 2f + 46f;
                if (list.Count == 0)
                {
                    drilledIpBlockH += 44f;
                }
                else
                {
                    drilledIpBlockH += TableHeaderH + show * TableRowH;
                    if (list.Count > IpamDrilledIpListDisplayMax)
                    {
                        drilledIpBlockH += 26f;
                    }
                }

                drilledIpBlockH += 10f;
                drilledIpBlockH += 4f;
            }
        }

        var prefixPagingBar = 0f;
        if (!string.IsNullOrEmpty(_ipamPrefixesDrillParentId))
        {
            var allP = IpamDataStore.GetPrefixes();
            var drillId = _ipamPrefixesDrillParentId;
            if (allP.Any(p => string.Equals(p.Id, drillId, StringComparison.Ordinal)))
            {
                if (BuildMergedFolderRows(allP, drillId).Count > 0)
                {
                    prefixPagingBar = IpamPrefixPaginationBarH;
                }
            }
        }

        var rows = Mathf.Max(1, CountPrefixTreeRowsForHeight());
        return CardPad * 2f + SectionTitleH + 10f + formBlock + SectionTitleH + 4f + TableHeaderH + rows * TableRowH + drilledIpBlockH + prefixPagingBar + 28f;
    }

    private static bool ShouldShowDrilledPrefixIpv4Section(IpamPrefixEntry scope)
    {
        if (scope == null)
        {
            return false;
        }

        var c = (scope.Cidr ?? "").Trim();
        return RouteMath.TryParseIpv4Cidr(c, out _, out var pl)
               && pl > IpamDrilledIpv4HideWhenPrefixLenAtOrBelow;
    }

    private static void DrawDrilledPrefixIpAssignmentsBlock(
        float x0,
        ref float y,
        float cardW,
        IpamPrefixEntry scope,
        IReadOnlyList<IpamPrefixEntry> allPrefixes)
    {
        if (!ShouldShowDrilledPrefixIpv4Section(scope))
        {
            return;
        }

        var titleBtnW = Mathf.Min(132f, cardW * 0.28f);
        GUI.Label(new Rect(x0, y, Mathf.Max(120f, cardW - titleBtnW - 8f), SectionTitleH), "IPv4 in this prefix", _stSectionTitle);
        if (ImguiButtonOnce(new Rect(x0 + cardW - titleBtnW, y + 1f, titleBtnW, Mathf.Max(22f, SectionTitleH - 2f)), "View in IP list", 9124, _stMutedBtn))
        {
            NavigateIpAddressesToCidrFilter((scope.Cidr ?? "").Trim());
        }

        y += SectionTitleH + 2f;
        GUI.Label(
            new Rect(x0, y, cardW, 44f),
            "Only addresses whose narrowest IPAM prefix is this folder (IPs delegated to a child prefix are listed under that child). Open jumps to IP addresses filtered to that host.",
            _stHint);
        y += 46f;

        var list = CollectServersExclusiveToPrefixForDrilledPanel(scope, allPrefixes);

        if (list.Count == 0)
        {
            GUI.Label(
                new Rect(x0, y, cardW, 40f),
                "No server IPv4 attributed directly to this prefix (IPs mapped to a child prefix appear under that child).",
                _stMuted);
            y += 44f;
            return;
        }

        var colIp = Mathf.Clamp(cardW * 0.26f, 120f, 200f);
        var colBtn = Mathf.Clamp(cardW * 0.14f, 72f, 108f);
        var colDev = Mathf.Max(100f, cardW - colIp - colBtn - 12f);

        GUI.Label(new Rect(x0, y, colIp, TableHeaderH), "IPv4 address", _stTableHeaderText);
        GUI.Label(new Rect(x0 + colIp, y, colDev, TableHeaderH), "Device", _stTableHeaderText);
        GUI.Label(new Rect(x0 + colIp + colDev, y, colBtn, TableHeaderH), "IP list", _stTableHeaderText);
        y += TableHeaderH;

        var show = Mathf.Min(list.Count, IpamDrilledIpListDisplayMax);
        for (var i = 0; i < show; i++)
        {
            var (srv, ip) = list[i];
            var r = new Rect(x0, y, cardW, TableRowH);
            var ev = Event.current;
            if (ev.type == EventType.Repaint)
            {
                var alt = i % 2 == 1;
                DrawTintedRect(
                    r,
                    alt ? new Color(0.06f, 0.08f, 0.1f, 0.5f) : new Color(0.04f, 0.05f, 0.06f, 0.35f));
            }

            var dn = Trunc(DeviceInventoryReflection.GetDisplayName(srv), 46);
            GUI.Label(new Rect(x0 + 4f, y, colIp - 6f, TableRowH), ip, _stTableCell);
            GUI.Label(new Rect(x0 + colIp, y, colDev - 4f, TableRowH), string.IsNullOrEmpty(dn) ? "—" : dn, _stTableCell);
            var openKey = 912500 + Mathf.Abs(HashCode.Combine(ip, srv != null ? srv.GetInstanceID() : i)) % 78000;
            if (ImguiButtonOnce(
                    new Rect(x0 + colIp + colDev + 2f, y + 3f, colBtn - 4f, TableRowH - 6f),
                    "Open",
                    openKey,
                    _stMutedBtn))
            {
                NavigateIpAddressesToCidrFilter($"{ip.Trim()}/32");
            }

            y += TableRowH;
        }

        if (list.Count > IpamDrilledIpListDisplayMax)
        {
            GUI.Label(
                new Rect(x0, y, cardW, 22f),
                $"… and {list.Count - IpamDrilledIpListDisplayMax} more in this CIDR. Use View in IP list for the full table.",
                _stHint);
            y += 26f;
        }

        y += 4f;
    }

    private static void DrawIpamVlansView(float innerW)
    {
        var x0 = CardPad;
        var y = CardPad;
        var cardW = innerW - CardPad * 2f;
        _lastInventoryCardWidth = cardW;

        GUI.Label(new Rect(x0, y - 2, cardW, SectionTitleH), "Organization  /  IPAM  /  VLANs", _stBreadcrumb);
        y += SectionTitleH + 2f;
        GUI.DrawTexture(new Rect(x0, y, cardW, 1f), _texTableHeader);
        y += 8f;

        GUI.Label(new Rect(x0, y, cardW, 20f), "Add VLAN", _stSectionTitle);
        y += 22f;
        GUI.Label(new Rect(x0, y, 70f, 22f), "VLAN ID", _stFormLabel);
        DrawIpamFormTextField(new Rect(x0 + 74f, y, 72f, 22f), IpamFormFocusVlanId, 4, IpamTextFieldKind.VlanIdDigits);
        GUI.Label(new Rect(x0 + 160f, y, 44f, 22f), "Name", _stFormLabel);
        DrawIpamFormTextField(
            new Rect(x0 + 206f, y, Mathf.Max(120f, cardW - 320f), 22f),
            IpamFormFocusVlanName,
            128,
            IpamTextFieldKind.Name);
        var addVlanRect = new Rect(x0 + cardW - 124f, y, 114f, 26f);
        if (ImguiButtonOnce(addVlanRect, "Add VLAN", 9103, _stPrimaryBtn))
        {
            _ipamVlanFormError = "";
            if (!int.TryParse(_ipamVlanFormId.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var vid))
            {
                _ipamVlanFormError = "VLAN ID must be a number.";
            }
            else if (!IpamDataStore.TryAddVlan(vid, _ipamVlanFormName, out var err))
            {
                _ipamVlanFormError = err ?? "Could not add VLAN.";
            }
            else
            {
                _ipamVlanFormName = "";
                RecomputeContentHeight();
            }
        }

        y += 30f;
        if (!string.IsNullOrEmpty(_ipamVlanFormError))
        {
            GUI.Label(new Rect(x0, y, cardW, 22f), _ipamVlanFormError, _stError);
            y += 24f;
        }

        y += 8f;
        GUI.Label(new Rect(x0, y, cardW, SectionTitleH), "Defined VLANs", _stSectionTitle);
        y += SectionTitleH + 4f;

        GUI.Label(new Rect(x0, y, 72f, TableHeaderH), "ID", _stTableHeaderText);
        GUI.Label(new Rect(x0 + 80f, y, cardW - 220f, TableHeaderH), "Name", _stTableHeaderText);
        GUI.Label(new Rect(x0 + cardW - 90f, y, 80f, TableHeaderH), "", _stTableHeaderText);
        y += TableHeaderH;

        var vlans = IpamDataStore.GetVlans().OrderBy(v => v.VlanId).ToList();
        if (vlans.Count == 0)
        {
            GUI.Label(new Rect(x0, y, cardW, 28f), "No VLANs stored yet. IDs are local to this mod (game VLAN APIs vary by build).", _stMuted);
            return;
        }

        for (var i = 0; i < vlans.Count; i++)
        {
            var v = vlans[i];
            var r = new Rect(x0, y, cardW, TableRowH);
            var alt = i % 2 == 1;
            if (Event.current.type == EventType.Repaint)
            {
                var tint = alt ? new Color(0.06f, 0.08f, 0.1f, 0.5f) : new Color(0.04f, 0.05f, 0.06f, 0.35f);
                DrawTintedRect(r, tint);
            }

            GUI.Label(new Rect(x0 + 6f, y, 64f, TableRowH), v.VlanId.ToString(CultureInfo.InvariantCulture), _stTableCell);
            GUI.Label(new Rect(x0 + 80f, y, cardW - 200f, TableRowH), v.Name ?? "", _stTableCell);
            if (Guid.TryParse(v.Id, out var gid) && ImguiButtonOnce(new Rect(x0 + cardW - 88f, y + 2f, 80f, TableRowH - 4f), "Delete", 9200 + i, _stMutedBtn))
            {
                IpamDataStore.TryDeleteVlan(gid, out _);
                RecomputeContentHeight();
            }

            y += TableRowH;
        }
    }

    private static float ComputeIpamVlansContentHeight()
    {
        var top = CardPad * 2f + SectionTitleH + 10f + 52f + 36f;
        if (!string.IsNullOrEmpty(_ipamVlanFormError))
        {
            top += 24f;
        }

        var n = IpamDataStore.GetVlans().Count;
        if (n == 0)
        {
            n = 1;
        }

        return top + SectionTitleH + 4f + TableHeaderH + n * TableRowH + 24f;
    }

    private static string _ipamScopeFormName = "";
    private static string _ipamScopeFormCidr = "";
    private static string _ipamScopeFormLevel = "Global";
    private static string _ipamScopeFormError = "";
    private static readonly HashSet<string> _selectedScopeIds = new();
    private static string _scopeRangeAnchorId;
    private static bool _scopeAddFormOpen;

    private static void DrawIpamDhcpScopesView(float innerW)
    {
        var x0 = CardPad;
        var y = CardPad;
        var cardW = innerW - CardPad * 2f;

        // ── Info Box ──
        GUI.Label(new Rect(x0, y, cardW, SectionTitleH), "DHCP Scopes", _stSectionTitle);
        y += SectionTitleH + 4f;
        GUI.Label(
            new Rect(x0, y, cardW, 44f),
            "Scopes override the default contract-based DHCP CIDR. Higher priority (lower number) wins. Level determines which servers match: Global = all, VLAN = servers on that VLAN, Switch = servers on that rack device.",
            _stHint);
        y += 50f;

        // ── Add Form (collapsible card) ──
        var addCardRect = new Rect(x0, y, cardW, 22f);
        DrawTintedRect(addCardRect, new Color(0.08f, 0.10f, 0.14f, 0.7f));
        GUI.Label(new Rect(x0 + 8f, y, cardW - 16f, 22f), "+ Add new scope", _stSectionTitle);
        if (Event.current.type == EventType.MouseDown && addCardRect.Contains(Event.current.mousePosition))
        {
            _scopeAddFormOpen = !_scopeAddFormOpen;
            Event.current.Use();
        }

        y += 26f;

        if (_scopeAddFormOpen)
        {
            DrawTintedRect(new Rect(x0, y, cardW, 100f), new Color(0.05f, 0.06f, 0.08f, 0.5f));

            GUI.Label(new Rect(x0 + 8f, y + 4f, 46f, 20f), "Name", _stFormLabel);
            DrawIpamFormTextField(
                new Rect(x0 + 58f, y + 4f, Mathf.Max(120f, cardW * 0.28f), 20f),
                IpamFormFocusScopeName,
                64,
                IpamTextFieldKind.Name);

            GUI.Label(new Rect(x0 + 58f + Mathf.Max(120f, cardW * 0.28f) + 10f, y + 4f, 36f, 20f), "CIDR", _stFormLabel);
            DrawIpamFormTextField(
                new Rect(x0 + 58f + Mathf.Max(120f, cardW * 0.28f) + 50f, y + 4f, 130f, 20f),
                IpamFormFocusScopeCidr,
                18,
                IpamTextFieldKind.Cidr);

            GUI.Label(new Rect(x0 + 8f, y + 30f, 46f, 20f), "Level", _stFormLabel);
            var levelBtnW = 72f;
            var levels = new[] { "Global", "VLAN", "Switch" };
            for (var li = 0; li < levels.Length; li++)
            {
                var lx = x0 + 58f + li * (levelBtnW + 4f);
                var sel = _ipamScopeFormLevel == levels[li];
                if (ImguiButtonOnce(new Rect(lx, y + 30f, levelBtnW, 20f), levels[li], 9300 + li, sel ? _stPrimaryBtn : _stMutedBtn))
                {
                    _ipamScopeFormLevel = levels[li];
                }
            }

            if (ImguiButtonOnce(new Rect(x0 + cardW - 114f, y + 30f, 106f, 24f), "Add Scope", 9310, _stPrimaryBtn))
            {
                _ipamScopeFormError = "";
                if (string.IsNullOrWhiteSpace(_ipamScopeFormName))
                {
                    _ipamScopeFormError = "Name is required.";
                }
                else if (string.IsNullOrWhiteSpace(_ipamScopeFormCidr))
                {
                    _ipamScopeFormError = "CIDR is required.";
                }
                else if (!IpamDataStore.TryAddDhcpScope(
                             _ipamScopeFormName,
                             _ipamScopeFormLevel,
                             _ipamScopeFormCidr,
                             null,
                             null,
                             0,
                             out var err))
                {
                    _ipamScopeFormError = err ?? "Could not add scope.";
                }
                else
                {
                    _ipamScopeFormName = "";
                    _ipamScopeFormCidr = "";
                    _scopeAddFormOpen = false;
                    RecomputeContentHeight();
                }
            }

            y += 58f;
            if (!string.IsNullOrEmpty(_ipamScopeFormError))
            {
                GUI.Label(new Rect(x0 + 8f, y, cardW - 16f, 20f), _ipamScopeFormError, _stError);
                y += 22f;
            }
        }

        y += 8f;

        // ── Scope List ──
        var scopes = IpamDataStore.GetDhcpScopes().OrderBy(s => s.Priority).ToList();
        var listTitle = scopes.Count == 0 ? "Configured Scopes" : $"Configured Scopes ({scopes.Count})";
        GUI.Label(new Rect(x0, y, cardW, SectionTitleH), listTitle, _stSectionTitle);
        y += SectionTitleH + 4f;

        // Toolbar: Select All / Delete Selected
        if (_selectedScopeIds.Count > 0)
        {
            if (ImguiButtonOnce(new Rect(x0, y, 100f, 22f), $"Delete ({_selectedScopeIds.Count})", 9315, _stMutedBtn))
            {
                foreach (var id in _selectedScopeIds)
                {
                    if (Guid.TryParse(id, out var gid))
                    {
                        IpamDataStore.TryDeleteDhcpScope(gid, out _);
                    }
                }

                _selectedScopeIds.Clear();
                _scopeRangeAnchorId = null;
                RecomputeContentHeight();
            }

            if (ImguiButtonOnce(new Rect(x0 + 108f, y, 80f, 22f), "Clear sel.", 9316, _stMutedBtn))
            {
                _selectedScopeIds.Clear();
                _scopeRangeAnchorId = null;
            }

            y += 26f;
        }

        // Table header
        var colPriority = 64f;
        var colLevel = 72f;
        var colCidr = 130f;
        var colBtn = 60f;
        var colName = cardW - colPriority - colLevel - colCidr - colBtn - 8f;

        GUI.Label(new Rect(x0, y, colPriority, TableHeaderH), "Prio", _stTableHeaderText);
        GUI.Label(new Rect(x0 + colPriority, y, colLevel, TableHeaderH), "Level", _stTableHeaderText);
        GUI.Label(new Rect(x0 + colPriority + colLevel, y, colName, TableHeaderH), "Name", _stTableHeaderText);
        GUI.Label(new Rect(x0 + colPriority + colLevel + colName, y, colCidr, TableHeaderH), "CIDR", _stTableHeaderText);
        y += TableHeaderH;

        if (scopes.Count == 0)
        {
            GUI.Label(new Rect(x0, y, cardW, 28f), "No scopes configured yet. Click \"+ Add new scope\" above to create one.", _stMuted);
            return;
        }

        // Rows
        for (var i = 0; i < scopes.Count; i++)
        {
            var s = scopes[i];
            var isSelected = _selectedScopeIds.Contains(s.Id);
            var rowRect = new Rect(x0, y, cardW, TableRowH);

            // Row background
            if (Event.current.type == EventType.Repaint)
            {
                Color tint;
                if (isSelected)
                {
                    tint = new Color(0.12f, 0.22f, 0.38f, 0.7f);
                }
                else
                {
                    tint = i % 2 == 1 ? new Color(0.06f, 0.08f, 0.1f, 0.5f) : new Color(0.04f, 0.05f, 0.06f, 0.35f);
                }

                DrawTintedRect(rowRect, tint);
            }

            // Row click (multiselect)
            if (Event.current.type == EventType.MouseDown && rowRect.Contains(Event.current.mousePosition) && Event.current.button == 0)
            {
                var e = Event.current;
                var ctrl = e.control || e.command;
                var shift = e.shift;

                if (shift && !ctrl && _scopeRangeAnchorId != null)
                {
                    // Range select
                    _selectedScopeIds.Clear();
                    var anchorIdx = scopes.FindIndex(x => x.Id == _scopeRangeAnchorId);
                    if (anchorIdx < 0) anchorIdx = i;
                    var lo = Mathf.Min(anchorIdx, i);
                    var hi = Mathf.Max(anchorIdx, i);
                    for (var j = lo; j <= hi; j++)
                    {
                        _selectedScopeIds.Add(scopes[j].Id);
                    }
                }
                else if (ctrl)
                {
                    // Toggle
                    if (!_selectedScopeIds.Add(s.Id))
                    {
                        _selectedScopeIds.Remove(s.Id);
                    }

                    _scopeRangeAnchorId = s.Id;
                }
                else
                {
                    // Single select
                    _selectedScopeIds.Clear();
                    _selectedScopeIds.Add(s.Id);
                    _scopeRangeAnchorId = s.Id;
                }

                Event.current.Use();
            }

            // Row content
            var textColor = isSelected ? Color.white : _stTableCell.normal.textColor;
            var prevColor = _stTableCell.normal.textColor;
            _stTableCell.normal.textColor = textColor;

            GUI.Label(new Rect(x0 + 4f, y, colPriority - 4f, TableRowH), s.Priority.ToString(CultureInfo.InvariantCulture), _stTableCell);
            GUI.Label(new Rect(x0 + colPriority, y, colLevel, TableRowH), s.Level ?? "", _stTableCell);
            GUI.Label(new Rect(x0 + colPriority + colLevel, y, colName, TableRowH), s.Name ?? "", _stTableCell);
            GUI.Label(new Rect(x0 + colPriority + colLevel + colName, y, colCidr, TableRowH), s.Cidr ?? "", _stTableCell);

            _stTableCell.normal.textColor = prevColor;

            // Delete button (only if not multiselect)
            if (_selectedScopeIds.Count <= 1)
            {
                if (Guid.TryParse(s.Id, out var gid) && ImguiButtonOnce(new Rect(x0 + cardW - colBtn + 2f, y + 2f, colBtn - 4f, TableRowH - 4f), "Delete", 9320 + i, _stMutedBtn))
                {
                    IpamDataStore.TryDeleteDhcpScope(gid, out _);
                    _selectedScopeIds.Remove(s.Id);
                    RecomputeContentHeight();
                }
            }

            y += TableRowH;
        }
    }

    private static float ComputeIpamDhcpScopesContentHeight()
    {
        var top = CardPad * 2f + SectionTitleH + 4f + 50f + 26f;
        if (_scopeAddFormOpen)
        {
            top += 100f;
            if (!string.IsNullOrEmpty(_ipamScopeFormError))
            {
                top += 22f;
            }
        }

        top += 8f + SectionTitleH + 4f;
        if (_selectedScopeIds.Count > 0)
        {
            top += 26f;
        }

        var n = IpamDataStore.GetDhcpScopes().Count;
        if (n == 0)
        {
            n = 1;
        }

        return top + TableHeaderH + n * TableRowH + 24f;
    }

    private static void DrawTutorialSection(float x0, ref float y, float cardW, string title, string body)
    {
        DrawTintedRect(new Rect(x0, y, cardW, 22f), new Color(0.08f, 0.10f, 0.14f, 0.7f));
        GUI.Label(new Rect(x0 + 8f, y, cardW - 16f, 22f), title, _stSectionTitle);
        y += 26f;
        GUI.Label(new Rect(x0 + 8f, y, cardW - 16f, 80f), body, _stHint);
        y += 84f;
    }

    private static void DrawIpamTutorialView(float innerW)
    {
        var x0 = CardPad;
        var y = CardPad;
        var cardW = innerW - CardPad * 2f;

        GUI.Label(new Rect(x0, y, cardW, SectionTitleH), "How to use gregMod.IPAM", _stSectionTitle);
        y += SectionTitleH + 8f;
        GUI.Label(
            new Rect(x0, y, cardW, 36f),
            "This guide explains the core features of the IPAM overlay. Press F1 to open/close the overlay at any time.",
            _stMuted);
        y += 40f;

        // Section 1: IP Assignment
        DrawTutorialSection(x0, ref y, cardW, "1. IP Assignment (Servers)",
            "Open IPAM (F1) → select a server → use the Octet Editor to set an IP manually, or click \"Next Free IP\" for automatic assignment. " +
            "DHCP auto-assign runs when a server is placed without an IP. Use Ctrl+L to assign all servers at once.");

        // Section 2: Prefixes
        DrawTutorialSection(x0, ref y, cardW, "2. IPAM Prefixes",
            "Prefixes define your network segments (e.g. 10.0.1.0/24). Create a prefix tree to organize subnets. " +
            "The utilization bar shows how many IPs are assigned. Prefixes are persisted in UserData/gregMod.IPAM/ipam_data.json.");

        // Section 3: VLANs
        DrawTutorialSection(x0, ref y, cardW, "3. VLANs",
            "Define VLAN IDs and names. VLANs can be used to segment network traffic. " +
            "DHCP scopes can be bound to specific VLANs so servers on that VLAN get IPs from the matching scope.");

        // Section 4: DHCP Scopes
        DrawTutorialSection(x0, ref y, cardW, "4. DHCP Scopes",
            "Scopes override the default contract-based DHCP CIDR. Set a priority (lower = higher precedence). " +
            "Level options: Global (all servers), VLAN (servers on that VLAN), Switch (servers on that rack device). " +
            "Use Ctrl+Click for multi-select, Shift+Click for range select.");

        // Section 5: Racks
        DrawTutorialSection(x0, ref y, cardW, "5. Rack Management",
            "The Racks tab shows your physical rack layout. Drag devices from the pick list into rack slots. " +
            "Patch panels can be labeled. The rack diagram visualizes U-height and device placement.");

        // Section 6: Naming Templates
        DrawTutorialSection(x0, ref y, cardW, "6. Naming Templates",
            "Bulk-name devices using patterns like {customer}-{seq} or {row}{col}. " +
            "Tokens: {customer}, {ip}, {octet}, {prefix}, {rack}, {role}, {color}, {seq}, {letter}. " +
            "Preview names before applying. Conventions can be saved and reused.");

        // Section 7: Customers
        DrawTutorialSection(x0, ref y, cardW, "7. Customer Management",
            "The Customers tab shows all customers and their assigned servers. " +
            "Assign servers to customers, configure DHCP, and manage multi-tenant setups.");

        // Section 8: Keyboard Shortcuts
        DrawTutorialSection(x0, ref y, cardW, "8. Keyboard Shortcuts",
            "F1 = Toggle IPAM overlay (takes mouse focus from game)\n" +
            "Escape = Close IPAM + open game pause menu\n" +
            "P = Close IPAM only (no pause menu)\n" +
            "Ctrl+L = Assign DHCP to all servers\n" +
            "Mouse wheel on octet = Increment/decrement IP\n" +
            "Ctrl+Click = Multi-select rows\n" +
            "Shift+Click = Range-select rows");

        // Section 9: Troubleshooting
        DrawTutorialSection(x0, ref y, cardW, "9. Troubleshooting",
            "If DHCP fails: check that the server is on a customer contract with a valid subnet. " +
            "If IPs collide: the overlay prevents duplicate assignments. " +
            "Check UserData/gregMod.IPAM/ for persisted data. Debug logs: gregModIPAM-debug.log beside _Data folder.");

        y += 8f;
        GUI.Label(
            new Rect(x0, y, cardW, 22f),
            "gregMod.IPAM v0.5.0 — TeamGreg Modding (mleem97 & mochimus)",
            _stMuted);
    }

    private static float ComputeIpamTutorialContentHeight()
    {
        return CardPad * 2f + SectionTitleH + 8f + 40f + 9 * 84f + 30f;
    }

    private static void DrawIpamPrefixTableHeader(
        float x0,
        float y,
        float cardW,
        out float colPrefix,
        out float colStatus,
        out float colChild,
        out float colFree,
        out float colUtil,
        out float colTenant,
        out float colActions)
    {
        var headerR = new Rect(x0, y, cardW, TableHeaderH);
        GUI.DrawTexture(headerR, _texTableHeader);
        GetIpamPrefixColumnWidths(cardW, out colPrefix, out colStatus, out colChild, out colFree, out colUtil, out colTenant, out colActions);
        const int gripBase = 91480;
        ProcessIpamPrefixColumnGripsInteraction(headerR, cardW, gripBase);

        var x = x0;
        var widths = new[] { colPrefix, colStatus, colChild, colFree, colUtil, colTenant, colActions };
        var labels = new[]
        {
            "Prefix",
            "Role",
            "Children",
            "Free /N",
            "Utilization",
            "Tenant",
            "Actions",
        };
        for (var i = 0; i < 7; i++)
        {
            var leftPad = i > 0 ? ColumnGripHalfWidth : 0f;
            var rightPad = i < 6 ? ColumnGripHalfWidth : 0f;
            var cell = new Rect(x + leftPad + 4f, y, widths[i] - leftPad - rightPad - 8f, TableHeaderH);
            if (i == 3)
            {
                GUI.Label(
                    cell,
                    new GUIContent(
                        labels[i],
                        "Non-overlapping CIDR-aligned subnets of the template size (/N) that still fit under this prefix after direct child prefixes. ~ indicates overlapping children."),
                    _stTableHeaderText);
            }
            else
            {
                GUI.Label(cell, labels[i], _stTableHeaderText);
            }

            x += widths[i];
        }

        DrawIpamPrefixColumnGripGuides(headerR, cardW, gripBase);
    }

    private static void ProcessIpamPrefixColumnGripsInteraction(Rect headerRect, float cardWidth, int gripHintBase)
    {
        var e = Event.current;
        if (e == null)
        {
            return;
        }

        var et = e.type;
        if (et != EventType.MouseDown && et != EventType.MouseDrag && et != EventType.MouseUp)
        {
            return;
        }

        GetIpamPrefixColumnWidths(
            cardWidth,
            out var w0,
            out var w1,
            out var w2,
            out var w3,
            out var w4,
            out var w5,
            out var w6);
        var ws = new[] { w0, w1, w2, w3, w4, w5, w6 };
        var x = headerRect.x;
        for (var boundary = 0; boundary < 6; boundary++)
        {
            x += ws[boundary];
            var grip = new Rect(
                x - ColumnGripHalfWidth,
                headerRect.y,
                ColumnGripHalfWidth * 2f,
                headerRect.height);
            var id = GUIUtility.GetControlID(gripHintBase + boundary, FocusType.Passive, grip);

            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (GUI.enabled && e.button == 0 && grip.Contains(e.mousePosition))
                    {
                        GUIUtility.hotControl = id;
                        _columnGripMouseStartX = e.mousePosition.x;
                        _ipamPrefixColumnGripWeightsStart = (float[])IpamPrefixTableColWeight.Clone();
                        e.Use();
                    }

                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != id)
                    {
                        break;
                    }

                    e.Use();
                    if (_ipamPrefixColumnGripWeightsStart == null || _ipamPrefixColumnGripWeightsStart.Length != 7)
                    {
                        break;
                    }

                    var dx = e.mousePosition.x - _columnGripMouseStartX;
                    var dw = dx / Mathf.Max(80f, cardWidth);
                    var left = Mathf.Clamp(_ipamPrefixColumnGripWeightsStart[boundary] + dw, MinColWeight, MaxColWeight);
                    var right = Mathf.Clamp(_ipamPrefixColumnGripWeightsStart[boundary + 1] - dw, MinColWeight, MaxColWeight);
                    var pairSum = left + right;
                    var origPair = _ipamPrefixColumnGripWeightsStart[boundary] + _ipamPrefixColumnGripWeightsStart[boundary + 1];
                    if (pairSum > 0.0001f)
                    {
                        var scale = origPair / pairSum;
                        IpamPrefixTableColWeight[boundary] = left * scale;
                        IpamPrefixTableColWeight[boundary + 1] = right * scale;
                        NormalizeIpamPrefixTableColWeights();
                    }

                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        _ipamPrefixColumnGripWeightsStart = null;
                        e.Use();
                    }

                    break;
            }
        }
    }

    private static void DrawIpamPrefixColumnGripGuides(Rect headerRect, float cardWidth, int gripHintBase)
    {
        if (Event.current == null || Event.current.type != EventType.Repaint || cardWidth < 120f)
        {
            return;
        }

        GetIpamPrefixColumnWidths(
            cardWidth,
            out var w0,
            out var w1,
            out var w2,
            out var w3,
            out var w4,
            out var w5,
            out var w6);
        var ws = new[] { w0, w1, w2, w3, w4, w5, w6 };
        var x = headerRect.x;
        var lineH = Mathf.Max(1f, headerRect.height);
        var lineY = headerRect.y;
        var oc = GUI.color;
        var mouse = Event.current.mousePosition;
        for (var boundary = 0; boundary < 6; boundary++)
        {
            x += ws[boundary];
            var grip = new Rect(
                x - ColumnGripHalfWidth,
                headerRect.y,
                ColumnGripHalfWidth * 2f,
                headerRect.height);
            var id = GUIUtility.GetControlID(gripHintBase + boundary, FocusType.Passive, grip);
            var hover = grip.Contains(mouse) || GUIUtility.hotControl == id;
            GUI.color = hover
                ? new Color(0.4f, 1f, 0.92f, 1f)
                : new Color(0f, 0.78f, 0.66f, 1f);
            GUI.DrawTexture(
                new Rect(x - 1f - (hover ? 1f : 0f), lineY, hover ? 4f : 2f, lineH),
                Texture2D.whiteTexture,
                ScaleMode.StretchToFill);
        }

        GUI.color = oc;
    }

    private static void NormalizeIpamPrefixTableColWeights()
    {
        var s = 0f;
        for (var i = 0; i < 7; i++)
        {
            s += IpamPrefixTableColWeight[i];
        }

        if (s < 0.0001f)
        {
            return;
        }

        for (var i = 0; i < 7; i++)
        {
            IpamPrefixTableColWeight[i] /= s;
        }
    }

    private static void GetIpamPrefixColumnWidths(
        float cardW,
        out float colPrefix,
        out float colStatus,
        out float colChild,
        out float colFree,
        out float colUtil,
        out float colTenant,
        out float colActions)
    {
        NormalizeIpamPrefixTableColWeights();
        colPrefix = cardW * IpamPrefixTableColWeight[0];
        colStatus = cardW * IpamPrefixTableColWeight[1];
        colChild = cardW * IpamPrefixTableColWeight[2];
        colFree = cardW * IpamPrefixTableColWeight[3];
        colUtil = cardW * IpamPrefixTableColWeight[4];
        colTenant = cardW * IpamPrefixTableColWeight[5];
        colActions = cardW * IpamPrefixTableColWeight[6];
    }

    private static void AutoFitIpamPrefixTableColumns(float cardWidth)
    {
        if (!_stylesReady || cardWidth < 200f || _stTableCell == null || _stTableHeaderText == null)
        {
            return;
        }

        var all = IpamDataStore.GetPrefixes();
        var minPx = new float[7];
        void BumpHeader(int col, string label)
        {
            var w = _stTableHeaderText.CalcSize(new GUIContent(label)).x + 14f;
            if (w > minPx[col])
            {
                minPx[col] = w;
            }
        }

        BumpHeader(0, "Prefix");
        BumpHeader(1, "Role");
        BumpHeader(2, "Children");
        BumpHeader(3, "Free /N");
        BumpHeader(4, "Utilization");
        BumpHeader(5, "Tenant");
        BumpHeader(6, "Actions");

        void BumpCell(int col, string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var w = _stTableCell.CalcSize(new GUIContent(text)).x + 10f;
            if (w > minPx[col])
            {
                minPx[col] = w;
            }
        }

        void BumpMuted(int col, string text)
        {
            if (string.IsNullOrEmpty(text) || _stMuted == null)
            {
                return;
            }

            var w = _stMuted.CalcSize(new GUIContent(text)).x + 10f;
            if (w > minPx[col])
            {
                minPx[col] = w;
            }
        }

        foreach (var p in all)
        {
            if (p == null)
            {
                continue;
            }

            var cidr = (p.Cidr ?? "").Trim();
            var name = string.IsNullOrEmpty(p.Name) ? "" : $"  ({p.Name})";
            BumpCell(0, cidr + name);

            var directChildren = all.Count(c => c != null && string.Equals(c.ParentId, p.Id, StringComparison.Ordinal));
            var hasParent = !string.IsNullOrEmpty(p.ParentId);
            var role = directChildren > 0 ? "Parent" : (hasParent ? "Child" : "Root");
            BumpCell(1, role);
            BumpCell(2, directChildren.ToString(CultureInfo.InvariantCulture));

            var kids = all.Where(c => c != null && string.Equals(c.ParentId, p.Id, StringComparison.Ordinal)).ToList();
            if (IpamPrefixAvailability.TryComputeFreeSplitSlots(cidr, kids, out var tpl, out _, out var fr, out var ov))
            {
                BumpMuted(3, ov ? $"~{fr}" : fr.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                BumpCell(3, "—");
            }

            var cap = Mathf.Max(1, RouteMath.CountIpamUsableHosts(cidr));
            var used = directChildren > 0
                ? CountAssignedServersWithIpInCidr(cidr)
                : CountAssignedServersExclusiveToPrefix(p, all);
            var util = Mathf.Clamp01(used / (float)cap);
            var pct = Mathf.RoundToInt(util * 100f);
            BumpMuted(4, $"{pct}% ({used}/{cap})");

            var tenant = string.IsNullOrEmpty(p.Tenant) ? "—" : p.Tenant;
            BumpCell(5, tenant);
        }

        BumpCell(6, "IPs");
        BumpCell(6, "Edit");

        minPx[4] = Mathf.Max(minPx[4], 120f);

        for (var i = 0; i < 7; i++)
        {
            minPx[i] = Mathf.Clamp(minPx[i], 52f, cardWidth * 0.42f);
        }

        var sum = minPx[0] + minPx[1] + minPx[2] + minPx[3] + minPx[4] + minPx[5] + minPx[6];
        if (sum < cardWidth)
        {
            var slack = cardWidth - sum;
            minPx[0] += slack * 0.28f;
            minPx[4] += slack * 0.18f;
            minPx[5] += slack * 0.32f;
            minPx[6] += slack * 0.12f;
        }
        else
        {
            var scale = cardWidth / sum;
            for (var i = 0; i < 7; i++)
            {
                minPx[i] *= scale;
            }
        }

        for (var i = 0; i < 7; i++)
        {
            IpamPrefixTableColWeight[i] = minPx[i] / cardWidth;
        }

        NormalizeIpamPrefixTableColWeights();
    }

    private static void DrawTintedRect(Rect r, Color tint)
    {
        var old = GUI.color;
        GUI.color = tint;
        GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill);
        GUI.color = old;
    }
}
