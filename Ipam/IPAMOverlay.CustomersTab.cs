using System;
using System.Collections.Generic;
using UnityEngine;

namespace DHCPSwitches;

// Customers tab: customer list → per-customer servers → Add server wizard (top-level GUI.Window).

public static partial class IPAMOverlay
{
    private static bool _customersTabServerBufferDirty = true;

    /// <summary>Drilled-in server table only — does not clear VLAN strings (those are stable per save once the contract exists).</summary>
    private static void MarkCustomersTabServerBufferDirty()
    {
        _customersTabServerBufferDirty = true;
        _customersTabCustomerListRowsDirty = true;
    }

    private static void EnsureCustomersTabCustomerListRows()
    {
        var sig = ComputeCustomersTabCustomerListSourceSignature();
        if (!_customersTabCustomerListRowsDirty
            && sig == _customersTabCustomerListSourceSign
            && CustomersTabCustomerListRows.Count > 0)
        {
            return;
        }

        _customersTabCustomerListRowsDirty = false;
        _customersTabCustomerListSourceSign = sig;
        RebuildCustomersTabCustomerListRows();
    }

    /// <summary>
    /// FNV-1a 64-bit mix of server→customer assignments and scene <see cref="CustomerBase"/> ids.
    /// VLAN columns use a separate long-lived cache; this only gates row rebuild work.
    /// </summary>
    private static ulong ComputeCustomersTabCustomerListSourceSignature()
    {
        unchecked
        {
            const ulong prime = 1099511628211UL;
            ulong h = 14695981039346656037UL;
            var servers = _cachedServers;
            h ^= (ulong)(uint)servers.Length * prime;
            for (var i = 0; i < servers.Length; i++)
            {
                var s = servers[i];
                if (s == null)
                {
                    continue;
                }

                int sid;
                try
                {
                    sid = s.GetInstanceID();
                }
                catch
                {
                    continue;
                }

                int cidKey;
                try
                {
                    cidKey = IsServerWithoutCustomerAssignment(s) ? -1 : s.GetCustomerID();
                }
                catch
                {
                    continue;
                }

                h = (h ^ ((ulong)(uint)sid * prime)) * prime;
                h = (h ^ ((ulong)(uint)cidKey * prime)) * prime;
            }

            var scene = GameSubnetHelper.GetSceneCustomersForFrame();
            h ^= (ulong)(uint)scene.Length * prime;
            for (var i = 0; i < scene.Length; i++)
            {
                var cb = scene[i];
                if (cb == null)
                {
                    continue;
                }

                int cid;
                try
                {
                    cid = cb.customerID;
                }
                catch
                {
                    continue;
                }

                h = (h ^ ((ulong)(uint)cid * prime)) * prime;
            }

            return h;
        }
    }

    /// <summary>Input System Esc while the add-server window is open (same edge latch as IOPS).</summary>
    public static void TickCustomersAddServerWizardInput()
    {
        if (!IsVisible || !_customersTabAddServerWizardOpen || !LicenseManager.IsIPAMUnlocked)
        {
            return;
        }

        if (IpamEscapePressedThisFrame)
        {
            _customersTabAddServerWizardOpen = false;
            _customersTabAddServerSelectedInstanceId = -1;
        }
    }

    private static int CountServersMatchingCustomersTabFilter()
    {
        return CountServersForCustomersTabCustomerId(_customersTabFilterCustomerId);
    }

    private static int CountServersForCustomersTabCustomerId(int customerId)
    {
        var n = 0;
        foreach (var s in _cachedServers)
        {
            if (s == null)
            {
                continue;
            }

            if (customerId < 0)
            {
                if (!IsServerWithoutCustomerAssignment(s))
                {
                    continue;
                }
            }
            else
            {
                if (IsServerWithoutCustomerAssignment(s))
                {
                    continue;
                }

                int cid;
                try
                {
                    cid = s.GetCustomerID();
                }
                catch
                {
                    continue;
                }

                if (cid != customerId)
                {
                    continue;
                }
            }

            n++;
        }

        return n;
    }

    private static float ComputeCustomersTabCustomerListContentHeight(int rowCount)
    {
        var y = CardPad;
        y += SectionTitleH + 2f + 7f;
        y += SectionTitleH + 4f + TableHeaderH + rowCount * TableRowH + 48f + CardPad;
        return Mathf.Max(280f, y);
    }

    private static float ComputeCustomersTabCustomerServersContentHeight(int rowCount)
    {
        var y = CardPad;
        y += SectionTitleH + 2f + 7f;
        y += 34f + SectionTitleH + 4f + 28f;
        y += SectionTitleH + 4f + TableHeaderH + rowCount * TableRowH + 52f + CardPad;
        return Mathf.Max(300f, y);
    }

    private static int GetCustomersTabCustomerListRowCount()
    {
        EnsureCustomersTabCustomerListRows();
        return CustomersTabCustomerListRows.Count;
    }

    private static void RebuildCustomersTabCustomerListRows()
    {
        CustomersTabCustomerListRows.Clear();
        BuildCustomersTabServerCountsByCustomerId(out var counts);
        CustomersTabCustomerListRows.Add((-1, "Unassigned servers", counts[-1], "—", "—", "—", "—"));
        var unique = new Dictionary<int, CustomerBase>();
        foreach (var cbase in GameSubnetHelper.GetSceneCustomersForFrame())
        {
            if (cbase == null)
            {
                continue;
            }

            if (!TryGetCustomerId(cbase, out var cid) || cid < 0)
            {
                continue;
            }

            if (!unique.TryGetValue(cid, out var existing))
            {
                unique[cid] = cbase;
                continue;
            }

            var existingName = GetCustomerName(existing);
            var currentName = GetCustomerName(cbase);
            if (string.IsNullOrWhiteSpace(existingName)
                && !string.IsNullOrWhiteSpace(currentName))
            {
                unique[cid] = cbase;
            }
        }

        var sortedIds = new List<int>(unique.Keys);
        sortedIds.Sort((a, b) => b.CompareTo(a));
        foreach (var cid in sortedIds)
        {
            var cb = unique[cid];
            var nm = cb.customerItem != null ? cb.customerItem.customerName : "";
            var title = $"#{cb.customerID}  {(string.IsNullOrWhiteSpace(nm) ? "—" : nm.Trim())}";
            var cnt = counts.TryGetValue(cid, out var c) ? c : 0;
            GameSubnetHelper.GetCustomerVlanIdsDisplay(cb, _cachedServers, out var vx, out var vr, out var vm, out var vg);
            CustomersTabCustomerListRows.Add((cid, title, cnt, vx, vr, vm, vg));
        }
    }

    /// <summary>Single pass over <see cref="_cachedServers"/> for customer list counts (was O(customers × servers) per rebuild).</summary>
    private static void BuildCustomersTabServerCountsByCustomerId(out Dictionary<int, int> counts)
    {
        counts = new Dictionary<int, int>();
        counts[-1] = 0;
        foreach (var s in _cachedServers)
        {
            if (s == null)
            {
                continue;
            }

            if (IsServerWithoutCustomerAssignment(s))
            {
                counts[-1]++;
                continue;
            }

            int cid;
            try
            {
                cid = s.GetCustomerID();
            }
            catch
            {
                continue;
            }

            if (counts.TryGetValue(cid, out var n))
            {
                counts[cid] = n + 1;
            }
            else
            {
                counts[cid] = 1;
            }
        }
    }

    private static void FillCustomersTabServersBuffer()
    {
        if (!_customersTabServerBufferDirty)
        {
            return;
        }

        _customersTabServerBufferDirty = false;
        CustomersTabServersBuffer.Clear();
        foreach (var s in _cachedServers)
        {
            if (s == null)
            {
                continue;
            }

            if (_customersTabFilterCustomerId < 0)
            {
                if (!IsServerWithoutCustomerAssignment(s))
                {
                    continue;
                }
            }
            else
            {
                if (IsServerWithoutCustomerAssignment(s))
                {
                    continue;
                }

                int cid;
                try
                {
                    cid = s.GetCustomerID();
                }
                catch
                {
                    continue;
                }

                if (cid != _customersTabFilterCustomerId)
                {
                    continue;
                }
            }

            CustomersTabServersBuffer.Add(s);
        }

        CustomersTabServersBuffer.Sort(CompareServersForSort);
    }

    private static void FillCustomersTabAddServerCandidateBuffer()
    {
        CustomersTabAddServerCandidateBuffer.Clear();
        foreach (var s in _cachedServers)
        {
            if (s == null || !IsServerWithoutCustomerAssignment(s))
            {
                continue;
            }

            CustomersTabAddServerCandidateBuffer.Add(s);
        }

        CustomersTabAddServerCandidateBuffer.Sort(CompareServersForSort);
    }

    private static void PruneServerSelectionForCustomersTabView()
    {
        MarkCustomersTabServerBufferDirty();
        FillCustomersTabServersBuffer();
        var keep = new HashSet<int>();
        foreach (var s in CustomersTabServersBuffer)
        {
            if (s != null)
            {
                keep.Add(s.GetInstanceID());
            }
        }

        var remove = new List<int>();
        foreach (var id in _selectedServerInstanceIds)
        {
            if (!keep.Contains(id))
            {
                remove.Add(id);
            }
        }

        foreach (var id in remove)
        {
            _selectedServerInstanceIds.Remove(id);
        }

        UpdateAnchorServerForDetail();
    }

    private static string GetCustomersTabDrillBreadcrumbTitle()
    {
        if (_customersTabFilterCustomerId < 0)
        {
            return "Unassigned servers";
        }

        foreach (var cb in GameSubnetHelper.GetSceneCustomersForFrame())
        {
            if (cb == null)
            {
                continue;
            }

            if (!TryGetCustomerId(cb, out var cid) || cid != _customersTabFilterCustomerId)
            {
                continue;
            }

            var nm = cb.customerItem != null ? cb.customerItem.customerName : "";
            var label = string.IsNullOrWhiteSpace(nm) ? "—" : nm.Trim();
            return $"#{cid}  {Trunc(label, 42)}";
        }

        return $"Customer #{_customersTabFilterCustomerId}";
    }

    private static string BuildCustomersTabTypeSummaryLine()
    {
        var total = CustomersTabServersBuffer.Count;
        var n3 = 0;
        var n7 = 0;
        foreach (var s in CustomersTabServersBuffer)
        {
            if (s == null)
            {
                continue;
            }

            var lab = DeviceInventoryReflection.GetServerFormFactorLabel(s);
            if (string.Equals(lab, "7 U", StringComparison.Ordinal))
            {
                n7++;
            }
            else if (string.Equals(lab, "3 U", StringComparison.Ordinal))
            {
                n3++;
            }
        }

        var parts = $"{total} server{(total == 1 ? "" : "s")}";
        if (n7 > 0)
        {
            parts += $" · {n7}×7 U";
        }

        if (n3 > 0)
        {
            parts += $" · {n3}×3 U";
        }

        return parts;
    }

    private static void CustomersTabNavigateToCustomer(int customerId)
    {
        _customersTabFilterCustomerId = customerId;
        _customersTabScreen = CustomersTabScreen.CustomerServers;
        _customersTabAddServerWizardOpen = false;
        _customersTabAddServerSelectedInstanceId = -1;
        MarkCustomersTabServerBufferDirty();
        PruneServerSelectionForCustomersTabView();
        _scroll = Vector2.zero;
        RecomputeContentHeight();
    }

    private static void CustomersTabNavigateToCustomerList()
    {
        _customersTabScreen = CustomersTabScreen.CustomerList;
        _customersTabAddServerWizardOpen = false;
        _customersTabAddServerSelectedInstanceId = -1;
        MarkCustomersTabServerBufferDirty();
        _scroll = Vector2.zero;
        RecomputeContentHeight();
    }

    private static void DrawCustomersCustomerListScreen(float innerW, float x0, ref float y, float cardW)
    {
        GUI.Label(new Rect(x0, y, 280, SectionTitleH), "Customers", _stSectionTitle);
        y += SectionTitleH + 4f;

        var hdrR = new Rect(x0, y, cardW, TableHeaderH);
        GUI.DrawTexture(hdrR, _texTableHeader);
        GetTableColumnWidths(cardW, out var cw0, out var cw1, out var cw2, out var cw3, out var cw4, out var cw5);
        var hx = x0;
        GUI.Label(new Rect(hx + 6f, y, cw0 - 12f, TableHeaderH), "Customer", _stTableCell);
        hx += cw0;
        GUI.Label(new Rect(hx + 6f, y, cw1 - 12f, TableHeaderH), "Servers", _stTableCell);
        hx += cw1;
        GUI.Label(new Rect(hx + 6f, y, cw2 - 12f, TableHeaderH), "X VLAN", _stTableCell);
        hx += cw2;
        GUI.Label(new Rect(hx + 6f, y, cw3 - 12f, TableHeaderH), "RISC VLAN", _stTableCell);
        hx += cw3;
        GUI.Label(new Rect(hx + 6f, y, cw4 - 12f, TableHeaderH), "Mainframe VLAN", _stTableCell);
        hx += cw4;
        GUI.Label(new Rect(hx + 6f, y, cw5 - 12f, TableHeaderH), "GPU VLAN", _stTableCell);
        ProcessTableColumnGrips(hdrR, cardW, 92450);
        y += TableHeaderH;
        var bodyTopY = y;

        EnsureCustomersTabCustomerListRows();
        var rows = CustomersTabCustomerListRows;
        for (var i = 0; i < rows.Count; i++)
        {
            var entry = rows[i];
            var r = new Rect(x0, y, cardW, TableRowH);
            string col0;
            string col1;
            string col2;
            string col3;
            string col4;
            string col5;
            if (ShouldComputeTruncatedInventoryCellText && InventoryScrollRowWantsRepaintText(r.yMin, r.yMax))
            {
                col0 = CellTextForCol(0, entry.title, cardW);
                col1 = CellTextForCol(1, entry.serverCount.ToString("N0"), cardW);
                col2 = CellTextForCol(2, entry.vlanX, cardW);
                col3 = CellTextForCol(3, entry.vlanRisc, cardW);
                col4 = CellTextForCol(4, entry.vlanMf, cardW);
                col5 = CellTextForCol(5, entry.vlanGpu, cardW);
            }
            else
            {
                col0 = "";
                col1 = "";
                col2 = "";
                col3 = "";
                col4 = "";
                col5 = "";
            }
            if (TableDataRowClick(
                    r,
                    StableRowHint(10, null, entry.customerId),
                    i % 2 == 1,
                    false,
                    col0,
                    col1,
                    col2,
                    col3,
                    col4,
                    col5,
                    cardW))
            {
                CustomersTabNavigateToCustomer(entry.customerId);
            }

            y += TableRowH;
        }

        DrawCustomersTableColumnBodyGuides(x0, bodyTopY, y, cardW);

        y += 8f;
        GUI.Label(
            new Rect(x0, y, cardW, 44f),
            "Click a customer to open its server list. Use Add server to attach an unplaced / unassigned server from the scene, then configure IPv4 in the bottom panel.",
            _stHint);
        y += 48f;
    }

    private static void DrawCustomersCustomerServersScreen(float innerW, float x0, ref float y, float cardW)
    {
        FillCustomersTabServersBuffer();

        var backRect = new Rect(x0 + cardW - 168f, y, 160f, 26f);
        if (ImguiButtonOnce(backRect, "← All customers", 92400, _stMutedBtn))
        {
            CustomersTabNavigateToCustomerList();
            return;
        }

        y += 30f;

        var serversTitleY = y;
        GUI.Label(new Rect(x0, serversTitleY, Mathf.Max(120f, cardW - 200f), SectionTitleH), "Servers", _stSectionTitle);
        if (_customersTabFilterCustomerId >= 0)
        {
            var addRect = new Rect(x0 + cardW - 140f, serversTitleY, 132f, SectionTitleH);
            if (ImguiButtonOnce(addRect, "Add server", 92401, _stPrimaryBtn))
            {
                _customersTabAddServerTargetCustomerId = _customersTabFilterCustomerId;
                _customersTabAddServerSelectedInstanceId = -1;
                _customersTabAddServerWizardOpen = true;
                _customersTabAddServerWizardScroll = Vector2.zero;
            }
        }

        y += SectionTitleH + 4f;
        GUI.Label(new Rect(x0, y, cardW, 22), BuildCustomersTabTypeSummaryLine(), _stMuted);
        y += 26f;

        DrawSortableTableHeader(
            new Rect(x0, y, cardW, TableHeaderH),
            ref _serverSortColumn,
            ref _serverSortAscending,
            "Name",
            "Customer",
            "Type",
            "IPv4 address",
            "EOL",
            "Status",
            610,
            true);
        y += TableHeaderH;

        for (var i = 0; i < CustomersTabServersBuffer.Count; i++)
        {
            var server = CustomersTabServersBuffer[i];
            var r = new Rect(x0, y, cardW, TableRowH);
            if (server == null)
            {
                TableDataRowClick(
                    r,
                    StableRowHint(8, null, i),
                    i % 2 == 1,
                    false,
                    "(removed)",
                    "—",
                    "—",
                    "—",
                    "—",
                    "—",
                    cardW);
                y += TableRowH;
                continue;
            }

            var ip = DHCPManager.GetServerIP(server);
            string dispName;
            string cust;
            string typeCol;
            string ipCol;
            string eolCol;
            string status;
            if (ShouldComputeTruncatedInventoryCellText && InventoryScrollRowWantsRepaintText(r.yMin, r.yMax))
            {
                var hasIp = !string.IsNullOrWhiteSpace(ip) && ip != "0.0.0.0";
                var ipRaw = string.IsNullOrWhiteSpace(ip) ? "—" : ip;
                ipCol = CellTextForCol(3, ipRaw, cardW);
                status = CellTextForCol(5, hasIp ? "Assigned" : "No address", cardW);
                cust = CellTextForCol(1, GetCustomerDisplayName(server), cardW);
                eolCol = TableEolCellDisplay(server, cardW);
                var dispRaw = DeviceInventoryReflection.GetDisplayName(server);
                dispName = CellTextForCol(0, string.IsNullOrEmpty(dispRaw) ? "—" : dispRaw, cardW);
                typeCol = CellTextForCol(2, DeviceInventoryReflection.GetServerFormFactorLabel(server), cardW);
            }
            else
            {
                dispName = "";
                cust = "";
                typeCol = "";
                ipCol = "";
                eolCol = "";
                status = "";
            }

            if (TableDataRowClick(
                    r,
                    StableRowHint(8, server, i),
                    i % 2 == 1,
                    IsServerRowSelected(server),
                    dispName,
                    cust,
                    typeCol,
                    ipCol,
                    eolCol,
                    status,
                    cardW))
            {
                HandleServerRowClick(server, i, ip, CustomersTabServersBuffer);
            }

            y += TableRowH;
        }

        GUI.Label(
            new Rect(x0, y, cardW, 44f),
            "Select servers here, then use the bottom panel to assign IPv4 (and customer, if needed). Add server opens a picker for servers that are not on a contract yet.",
            _stHint);
        y += 48f;
    }

    private static void DrawCustomersView(float innerW)
    {
        var x0 = CardPad;
        var y = CardPad;
        var cardW = innerW - CardPad * 2f;
        _lastInventoryCardWidth = cardW;
        if (_tableColumnsAutoFitPending && cardW > 80f)
        {
            if (_customersTabScreen == CustomersTabScreen.CustomerList)
            {
                AutoFitCustomersTabCustomerListColumns(cardW);
            }
            else
            {
                AutoFitInventoryTableColumns(cardW);
            }

            _tableColumnsAutoFitPending = false;
        }

        var crumb = _customersTabScreen == CustomersTabScreen.CustomerList
            ? "Organization  /  Customers"
            : $"Organization  /  Customers  /  {GetCustomersTabDrillBreadcrumbTitle()}";
        GUI.Label(new Rect(x0, y - 2, cardW, SectionTitleH), crumb, _stBreadcrumb);
        y += SectionTitleH + 2f;
        GUI.DrawTexture(new Rect(x0, y, cardW, 1f), _texTableHeader);
        y += 6f;

        if (_customersTabScreen == CustomersTabScreen.CustomerList)
        {
            DrawCustomersCustomerListScreen(innerW, x0, ref y, cardW);
        }
        else
        {
            DrawCustomersCustomerServersScreen(innerW, x0, ref y, cardW);
        }
    }

    private static void DrawCustomersAddServerWindow(int windowId)
    {
        GUI.DragWindow(new Rect(0f, 0f, 10000f, 24f));

        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
        {
            _customersTabAddServerWizardOpen = false;
            _customersTabAddServerSelectedInstanceId = -1;
            Event.current.Use();
            return;
        }

        var w = _customersTabAddServerWindowRect.width;
        const float pad = 12f;
        var innerW = w - pad * 2f;
        var x = pad;
        var y = 8f;

        if (ImguiButtonOnce(new Rect(w - pad - 88f, y, 80f, 24f), "Close", 92301, _stMutedBtn))
        {
            _customersTabAddServerWizardOpen = false;
            _customersTabAddServerSelectedInstanceId = -1;
            return;
        }

        var cb = GameSubnetHelper.FindCustomerBaseByCustomerId(_customersTabAddServerTargetCustomerId);
        var hdr = cb != null
            ? $"Choose a server, then assign it to customer #{_customersTabAddServerTargetCustomerId} — {Trunc(GetCustomerName(cb), 40)}"
            : $"Choose a server for customer #{_customersTabAddServerTargetCustomerId}";
        GUI.Label(new Rect(x, y + 28f, innerW, 48f), hdr, _stMuted);
        y += 82f;

        FillCustomersTabAddServerCandidateBuffer();
        if (CustomersTabAddServerCandidateBuffer.Count == 0)
        {
            GUI.Label(
                new Rect(x, y, innerW, 80f),
                "No eligible servers in the scene (every server already appears on a customer contract, or the game has not exposed any unassigned rack servers yet).\n\nPlace a server in the rack first, then try again.",
                _stHint);
            y += 88f;
        }
        else
        {
            GUI.Label(new Rect(x, y, innerW, 22f), "Unassigned servers (scene)", _stSectionTitle);
            y += 26f;

            var listH = Mathf.Max(220f, _customersTabAddServerWindowRect.height - y - 96f);
            var listRect = new Rect(x, y, innerW, listH);
            var rowH = TableRowH;
            var contentH = CustomersTabAddServerCandidateBuffer.Count * rowH + 8f;
            _customersTabAddServerWizardScroll = GUI.BeginScrollView(
                listRect,
                _customersTabAddServerWizardScroll,
                new Rect(0f, 0f, innerW - 20f, contentH));
            for (var i = 0; i < CustomersTabAddServerCandidateBuffer.Count; i++)
            {
                var srv = CustomersTabAddServerCandidateBuffer[i];
                if (srv == null)
                {
                    continue;
                }

                var ry = i * rowH;
                var rr = new Rect(2f, ry + 2f, innerW - 24f, rowH - 4f);
                var nm = DeviceInventoryReflection.GetDisplayName(srv);
                var ty = DeviceInventoryReflection.GetServerFormFactorLabel(srv);
                var line = $"{(string.IsNullOrEmpty(nm) ? "—" : Trunc(nm, 52))}   ·   {ty}";
                var sel = srv.GetInstanceID() == _customersTabAddServerSelectedInstanceId;
                var st = sel ? _stNavItemActive : _stMutedBtn;
                var dedupe = HashCode.Combine(92350, srv.GetInstanceID());
                if (ImguiButtonOnce(rr, line, dedupe, st))
                {
                    _customersTabAddServerSelectedInstanceId = srv.GetInstanceID();
                }
            }

            GUI.EndScrollView();
            y += listH + 10f;
        }

        var assignY = _customersTabAddServerWindowRect.height - pad - 40f;
        var assignW = 220f;
        var canAssign = _customersTabAddServerSelectedInstanceId >= 0
                        && cb != null
                        && CustomersTabAddServerCandidateBuffer.Count > 0;
        GUI.enabled = canAssign;
        if (ImguiButtonOnce(new Rect(x, assignY, assignW, 32f), "Assign & configure", 92302, _stPrimaryBtn))
        {
            var sel = FindServerByInstanceId(_customersTabAddServerSelectedInstanceId);
            if (sel != null && cb != null && TrySetServerCustomer(sel, cb))
            {
                DHCPManager.ClearLastSetIpError();
                InvalidateCustomerCache();
                InvalidateDeviceCache();
                MarkCustomersTabServerBufferDirty();
                ClearSwitchSelection();
                _selectedServerInstanceIds.Clear();
                _selectedServerInstanceIds.Add(sel.GetInstanceID());
                _serverRangeAnchorInstanceId = sel.GetInstanceID();
                UpdateAnchorServerForDetail();
                LoadOctetsFromIp(DHCPManager.GetServerIP(sel));
                if (LicenseManager.IsDHCPUnlocked)
                {
                    ModDebugLog.Bootstrap();
                    ModDebugLog.WriteDhcpAssign($"UI: Add-server wizard assigned server to customer #{_customersTabAddServerTargetCustomerId}");
                    DHCPManager.AssignDhcpToSingleServer(sel);
                    DHCPManager.ClearLastSetIpError();
                    LoadOctetsFromIp(DHCPManager.GetServerIP(sel));
                    BeginImGuiInputRecoveryBurst();
                }

                _customersTabAddServerWizardOpen = false;
                _customersTabAddServerSelectedInstanceId = -1;
            }
            else
            {
                DHCPManager.SetLastIpamError("Could not assign the selected server to this customer (game API).");
            }
        }

        GUI.enabled = true;
        if (ImguiButtonOnce(new Rect(x + assignW + 12f, assignY, 100f, 32f), "Cancel", 92303, _stMutedBtn))
        {
            _customersTabAddServerWizardOpen = false;
            _customersTabAddServerSelectedInstanceId = -1;
        }
    }
}
