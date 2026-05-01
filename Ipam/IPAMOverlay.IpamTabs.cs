using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace DHCPSwitches;

// Prefixes (parent/child, utilization) and VLAN list for the IPAM nav section.

public static partial class IPAMOverlay
{
    private static void IpamClosePrefixEditPanel()
    {
        _ipamPrefixEditId = null;
        _ipamPrefixEditNameBuf = "";
        _ipamPrefixEditTenantBuf = "";
        _ipamPrefixEditSaveError = null;
        if (_ipamFormFieldFocus == IpamFormFocusEditPrefixName || _ipamFormFieldFocus == IpamFormFocusEditPrefixTenant)
        {
            _ipamFormFieldFocus = IpamFormFocusNone;
        }
    }

    private static void IpamNavigateToPrefixIps(IpamPrefixEntry p)
    {
        if (p == null)
        {
            return;
        }

        IpamClosePrefixEditPanel();
        _ipamIpAddressFilterCidr = (p.Cidr ?? "").Trim();
        _ipamIpAddressPageIndex = 0;
        _ipamFormFieldFocus = IpamFormFocusNone;
        _navSection = NavSection.Ipam;
        _ipamSub = IpamSubSection.IpAddresses;
        _scroll = Vector2.zero;
        _customersTabFilterMenuOpen = false;
        RecomputeContentHeight();
    }

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
            if (sub != IpamSubSection.Prefixes)
            {
                _ipamPrefixesDrillParentId = null;
                _ipamPrefixAddAsRoot = false;
                IpamClosePrefixEditPanel();
            }

            if (sub == IpamSubSection.Prefixes)
            {
                _ipamIpAddressFilterCidr = null;
            }

            _navSection = NavSection.Ipam;
            _ipamSub = sub;
            _scroll = Vector2.zero;
            _customersTabFilterMenuOpen = false;
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
            Walk(prefixes, null, ref n);
            return n;
        }

        if (prefixes.All(p => p.Id != _ipamPrefixesDrillParentId))
        {
            var n2 = 0;
            Walk(prefixes, null, ref n2);
            return n2;
        }

        var sub = 0;
        Walk(prefixes, _ipamPrefixesDrillParentId, ref sub);
        return Mathf.Max(1, sub);
    }

    private static void Walk(IReadOnlyList<IpamPrefixEntry> all, string parentId, ref int n)
    {
        IEnumerable<IpamPrefixEntry> q = all.Where(p =>
            string.IsNullOrEmpty(parentId)
                ? string.IsNullOrEmpty(p.ParentId)
                : string.Equals(p.ParentId, parentId, StringComparison.Ordinal));
        foreach (var p in q.OrderBy(static p => NetworkSortKey(p)))
        {
            n++;
            Walk(all, p.Id, ref n);
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
        _ipamPrefixAddAsRoot = GUI.Toggle(new Rect(x0, y, Mathf.Min(360f, cardW - 8f), 22f), _ipamPrefixAddAsRoot, "Add as root prefix (sibling / new top-level)");
        if (_ipamPrefixAddAsRoot && !addRootWas)
        {
            _ipamSelectedPrefixId = null;
        }

        y += 26f;

        var addRect = new Rect(x0, y, 120f, 26f);
        if (ImguiButtonOnce(addRect, "Add prefix", 9101, _stPrimaryBtn))
        {
            _ipamPrefixFormError = "";
            if (!IpamDataStore.TryAddPrefix(_ipamPrefixFormCidr, _ipamPrefixFormName, addMode, explicitParentId, out var err))
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
            _ipamPrefixFormError = "";
            if (string.IsNullOrEmpty(_ipamSelectedPrefixId) || !Guid.TryParse(_ipamSelectedPrefixId, out var delId))
            {
                _ipamPrefixFormError = "Select a prefix to delete (subtree is removed).";
            }
            else if (!IpamDataStore.TryDeletePrefix(delId, out var err))
            {
                _ipamPrefixFormError = err ?? "Delete failed.";
            }
            else
            {
                _ipamSelectedPrefixId = null;
                IpamPruneDrillAfterPrefixMutation();
                RecomputeContentHeight();
            }
        }

        y += 34f;
        if (!string.IsNullOrEmpty(_ipamPrefixFormError))
        {
            GUI.Label(new Rect(x0, y, cardW, 44f), _ipamPrefixFormError, _stError);
            y += 46f;
        }

        y += 6f;
        if (!string.IsNullOrEmpty(_ipamPrefixesDrillParentId))
        {
            var scope = prefixes.FirstOrDefault(p => string.Equals(p.Id, _ipamPrefixesDrillParentId, StringComparison.Ordinal));
            var scopeLabel = scope == null
                ? "prefix"
                : $"{(scope.Cidr ?? "").Trim()}{(string.IsNullOrEmpty(scope.Name) ? "" : $"  ({scope.Name})")}";
            GUI.Label(new Rect(x0, y, cardW - 136f, SectionTitleH), $"Inside: {scopeLabel}", _stSectionTitle);
            var backRect = new Rect(x0 + cardW - 128f, y, 120f, SectionTitleH);
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
            GUI.Label(new Rect(x0, y, cardW - 100f, 20f), "Tip: double-click a row to open it and list only its children.", _stHint);
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

        GetIpamPrefixColumnWidths(cardW, out var colPrefix, out var colStatus, out var colChild, out var colUtil, out var colTenant, out var colActions);

        GUI.Label(new Rect(x0, y, colPrefix, TableHeaderH), "Prefix", _stTableHeaderText);
        GUI.Label(new Rect(x0 + colPrefix, y, colStatus, TableHeaderH), "Role", _stTableHeaderText);
        GUI.Label(new Rect(x0 + colPrefix + colStatus, y, colChild, TableHeaderH), "Children", _stTableHeaderText);
        GUI.Label(new Rect(x0 + colPrefix + colStatus + colChild, y, colUtil, TableHeaderH), "Utilization", _stTableHeaderText);
        GUI.Label(new Rect(x0 + colPrefix + colStatus + colChild + colUtil, y, colTenant, TableHeaderH), "Tenant", _stTableHeaderText);
        GUI.Label(new Rect(x0 + colPrefix + colStatus + colChild + colUtil + colTenant, y, colActions, TableHeaderH), "Actions", _stTableHeaderText);
        y += TableHeaderH;

        if (prefixes.Count == 0)
        {
            GUI.Label(new Rect(x0, y, cardW, 28f), "No prefixes yet. Add a root CIDR above (e.g. 10.0.0.0/8), then add children (e.g. 10.0.1.0/24 under 10.0.0.0/8).", _stMuted);
            return;
        }

        var treeParentId = string.IsNullOrEmpty(_ipamPrefixesDrillParentId) ? null : _ipamPrefixesDrillParentId;
        if (treeParentId != null)
        {
            var subCount = 0;
            Walk(prefixes, treeParentId, ref subCount);
            if (subCount == 0)
            {
                GUI.Label(
                    new Rect(x0, y, cardW, 44f),
                    "No child prefixes under this folder yet. Add one above — the parent is this folder unless you pick another row or use “Add as root”.",
                    _stMuted);
                return;
            }
        }

        DrawPrefixTreeRows(prefixes, treeParentId, 0, x0, ref y, cardW, colPrefix, colStatus, colChild, colUtil, colTenant, colActions);

        DrawIpamPrefixEditPanel(innerW, ref y);
    }

    private static void DrawIpamPrefixEditPanel(float innerW, ref float y)
    {
        if (string.IsNullOrEmpty(_ipamPrefixEditId))
        {
            return;
        }

        var prefixes = IpamDataStore.GetPrefixes();
        var entry = prefixes.FirstOrDefault(p => string.Equals(p.Id, _ipamPrefixEditId, StringComparison.Ordinal));
        if (entry == null)
        {
            IpamClosePrefixEditPanel();
            return;
        }

        var x0 = CardPad;
        var cardW = innerW - CardPad * 2f;
        y += 14f;
        GUI.DrawTexture(new Rect(x0, y, cardW, 1f), _texTableHeader);
        y += 10f;
        GUI.Label(new Rect(x0, y, cardW, SectionTitleH), "Edit prefix", _stSectionTitle);
        y += SectionTitleH + 4f;
        var cidrDisp = (entry.Cidr ?? "").Trim();
        GUI.Label(new Rect(x0, y, cardW, 22f), $"CIDR: {cidrDisp}", _stMuted);
        y += 26f;
        GUI.Label(new Rect(x0, y, 48f, 22f), "Name", _stFormLabel);
        DrawIpamFormTextField(new Rect(x0 + 52f, y, Mathf.Min(320f, cardW - 60f), 22f), IpamFormFocusEditPrefixName, 128, IpamTextFieldKind.Name);
        y += 28f;
        GUI.Label(new Rect(x0, y, 52f, 22f), "Tenant", _stFormLabel);
        DrawIpamFormTextField(new Rect(x0 + 56f, y, Mathf.Min(360f, cardW - 64f), 22f), IpamFormFocusEditPrefixTenant, 128, IpamTextFieldKind.Name);
        y += 30f;
        if (!string.IsNullOrEmpty(_ipamPrefixEditSaveError))
        {
            GUI.Label(new Rect(x0, y, cardW, 22f), _ipamPrefixEditSaveError, _stError);
            y += 24f;
        }

        var saveR = new Rect(x0, y, 88f, 26f);
        if (ImguiButtonOnce(saveR, "Save", 9110, _stPrimaryBtn))
        {
            _ipamPrefixEditSaveError = null;
            if (IpamDataStore.TryUpdatePrefixMetadata(_ipamPrefixEditId, _ipamPrefixEditNameBuf, _ipamPrefixEditTenantBuf, out var err))
            {
                IpamClosePrefixEditPanel();
                RecomputeContentHeight();
            }
            else
            {
                _ipamPrefixEditSaveError = err ?? "Save failed.";
            }
        }

        if (ImguiButtonOnce(new Rect(saveR.xMax + 10f, y, 88f, 26f), "Cancel", 9111, _stMutedBtn))
        {
            IpamClosePrefixEditPanel();
        }

        y += 32f;
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
        float colUtil,
        float colTenant,
        float colActions)
    {
        var q = all.Where(p =>
                string.IsNullOrEmpty(parentId)
                    ? string.IsNullOrEmpty(p.ParentId)
                    : string.Equals(p.ParentId, parentId, StringComparison.Ordinal))
            .OrderBy(static p => NetworkSortKey(p))
            .ToList();

        foreach (var p in q)
        {
            var r = new Rect(x0, y, cardW, TableRowH);
            var rowSelectRect = new Rect(x0, y, Mathf.Max(40f, cardW - colActions - 4f), TableRowH);
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && rowSelectRect.Contains(e.mousePosition))
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

            var indent = 14f * depth;
            var cidr = (p.Cidr ?? "").Trim();
            var name = string.IsNullOrEmpty(p.Name) ? "" : $"  ({p.Name})";
            var directChildren = all.Count(c => string.Equals(c.ParentId, p.Id, StringComparison.Ordinal));
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
            // Rows with child prefixes: aggregate all servers in this CIDR (includes space delegated to children).
            // Leaf rows: exclusive count (tightest matching prefix only).
            var used = directChildren > 0
                ? CountAssignedServersWithIpInCidr(cidr)
                : CountAssignedServersExclusiveToPrefix(p, all);
            var util = Mathf.Clamp01(used / (float)cap);

            GUI.Label(new Rect(x0 + 6f + indent, y, colPrefix - 6f - indent, TableRowH), cidr + name, _stTableCell);
            GUI.Label(new Rect(x0 + colPrefix, y, colStatus, TableRowH), roleLabel, _stTableCell);
            GUI.Label(
                new Rect(x0 + colPrefix + colStatus, y, colChild, TableRowH),
                directChildren.ToString(CultureInfo.InvariantCulture),
                _stTableCell);

            var utilColLeft = x0 + colPrefix + colStatus + colChild;
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
            GUI.Label(new Rect(x0 + colPrefix + colStatus + colChild + colUtil, y, colTenant, TableRowH), tenantDisp, _stTableCell);

            var actX = x0 + colPrefix + colStatus + colChild + colUtil + colTenant;
            var btnW = (colActions - 10f) * 0.5f;
            var btnH = TableRowH - 6f;
            var btnY = y + 3f;
            var hid = Math.Abs((p.Id ?? "").GetHashCode());
            var ipsKey = 913000 + (hid & 0x3fff);
            if (ImguiButtonOnce(new Rect(actX + 2f, btnY, btnW, btnH), "IPs", ipsKey, _stMutedBtn))
            {
                IpamNavigateToPrefixIps(p);
            }

            if (ImguiButtonOnce(new Rect(actX + 6f + btnW, btnY, btnW, btnH), "Edit", ipsKey + 20000, _stMutedBtn))
            {
                _ipamPrefixEditId = p.Id;
                _ipamPrefixEditNameBuf = p.Name ?? "";
                _ipamPrefixEditTenantBuf = p.Tenant ?? "";
                _ipamPrefixEditSaveError = null;
                _ipamFormFieldFocus = IpamFormFocusNone;
            }

            y += TableRowH;
            DrawPrefixTreeRows(all, p.Id, depth + 1, x0, ref y, cardW, colPrefix, colStatus, colChild, colUtil, colTenant, colActions);
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

        var rows = Mathf.Max(1, CountPrefixTreeRowsForHeight());
        if (!string.IsNullOrEmpty(_ipamPrefixesDrillParentId))
        {
            var prefixes = IpamDataStore.GetPrefixes();
            var sc = 0;
            Walk(prefixes, _ipamPrefixesDrillParentId, ref sc);
            if (sc == 0)
            {
                formBlock += 28f;
            }
        }

        if (!string.IsNullOrEmpty(_ipamPrefixEditId))
        {
            formBlock += 156f;
        }

        return CardPad * 2f + SectionTitleH + 10f + formBlock + SectionTitleH + 4f + TableHeaderH + rows * TableRowH + 28f;
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

    private static void NormalizeIpamPrefixTableColWeights()
    {
        var s = 0f;
        for (var i = 0; i < 6; i++)
        {
            s += IpamPrefixTableColWeight[i];
        }

        if (s < 0.0001f)
        {
            return;
        }

        for (var i = 0; i < 6; i++)
        {
            IpamPrefixTableColWeight[i] /= s;
        }
    }

    private static void GetIpamPrefixColumnWidths(
        float cardW,
        out float colPrefix,
        out float colStatus,
        out float colChild,
        out float colUtil,
        out float colTenant,
        out float colActions)
    {
        NormalizeIpamPrefixTableColWeights();
        colPrefix = cardW * IpamPrefixTableColWeight[0];
        colStatus = cardW * IpamPrefixTableColWeight[1];
        colChild = cardW * IpamPrefixTableColWeight[2];
        colUtil = cardW * IpamPrefixTableColWeight[3];
        colTenant = cardW * IpamPrefixTableColWeight[4];
        colActions = cardW * IpamPrefixTableColWeight[5];
    }

    private static void AutoFitIpamPrefixTableColumns(float cardWidth)
    {
        if (!_stylesReady || cardWidth < 200f || _stTableCell == null || _stTableHeaderText == null)
        {
            return;
        }

        var all = IpamDataStore.GetPrefixes();
        var minPx = new float[6];
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
        BumpHeader(3, "Utilization");
        BumpHeader(4, "Tenant");
        BumpHeader(5, "Actions");

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

            var cap = Mathf.Max(1, RouteMath.CountDhcpUsableHosts(cidr));
            var used = directChildren > 0
                ? CountAssignedServersWithIpInCidr(cidr)
                : CountAssignedServersExclusiveToPrefix(p, all);
            var util = Mathf.Clamp01(used / (float)cap);
            var pct = Mathf.RoundToInt(util * 100f);
            BumpMuted(3, $"{pct}% ({used}/{cap})");

            var tenant = string.IsNullOrEmpty(p.Tenant) ? "—" : p.Tenant;
            BumpCell(4, tenant);
        }

        BumpCell(5, "IPs");
        BumpCell(5, "Edit");

        minPx[3] = Mathf.Max(minPx[3], 120f);

        for (var i = 0; i < 6; i++)
        {
            minPx[i] = Mathf.Clamp(minPx[i], 52f, cardWidth * 0.42f);
        }

        var sum = minPx[0] + minPx[1] + minPx[2] + minPx[3] + minPx[4] + minPx[5];
        if (sum < cardWidth)
        {
            var slack = cardWidth - sum;
            minPx[0] += slack * 0.30f;
            minPx[3] += slack * 0.20f;
            minPx[4] += slack * 0.35f;
            minPx[5] += slack * 0.15f;
        }
        else
        {
            var scale = cardWidth / sum;
            for (var i = 0; i < 6; i++)
            {
                minPx[i] *= scale;
            }
        }

        for (var i = 0; i < 6; i++)
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
