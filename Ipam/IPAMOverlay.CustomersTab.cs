using System;
using System.Collections.Generic;
using UnityEngine;

namespace DHCPSwitches;

// Customers navigation: filter by contract, compact server list, same detail panel for IP + customer assign.

public static partial class IPAMOverlay
{
    private static readonly List<(int customerId, string line)> CustomersTabFilterScratch = new();

    private static int CountServersMatchingCustomersTabFilter()
    {
        var n = 0;
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

            n++;
        }

        return n;
    }

    private static float ComputeCustomersTabContentHeight(int rowCount)
    {
        var y = CardPad;
        y += SectionTitleH + 2f + 7f;
        y += SectionTitleH + 4f + 28f + 6f + 22f + 10f;
        y += SectionTitleH + 4f + TableHeaderH + rowCount * TableRowH + 26f + CardPad;
        return Mathf.Max(300f, y);
    }

    private static void FillCustomersTabServersBuffer()
    {
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

    private static void PruneServerSelectionForCustomersTabView()
    {
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

    private static string GetCustomersTabFilterButtonSummaryClosed()
    {
        if (_customersTabFilterCustomerId < 0)
        {
            return "Unassigned servers";
        }

        foreach (var cb in UnityEngine.Object.FindObjectsOfType<CustomerBase>())
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
            return $"#{cid}  {Trunc(label, 36)}";
        }

        return $"Customer #{_customersTabFilterCustomerId}";
    }

    private static void BuildCustomersTabFilterPickList()
    {
        CustomersTabFilterScratch.Clear();
        CustomersTabFilterScratch.Add((-1, "Unassigned servers"));
        var unique = new Dictionary<int, CustomerBase>();
        foreach (var cb in UnityEngine.Object.FindObjectsOfType<CustomerBase>())
        {
            if (cb == null)
            {
                continue;
            }

            if (!TryGetCustomerId(cb, out var cid) || cid < 0)
            {
                continue;
            }

            if (!unique.TryGetValue(cid, out var existing))
            {
                unique[cid] = cb;
                continue;
            }

            var existingName = GetCustomerName(existing);
            var currentName = GetCustomerName(cb);
            if (string.IsNullOrWhiteSpace(existingName)
                && !string.IsNullOrWhiteSpace(currentName))
            {
                unique[cid] = cb;
            }
        }

        var sortedIds = new List<int>(unique.Keys);
        sortedIds.Sort((a, b) => b.CompareTo(a));
        foreach (var cid in sortedIds)
        {
            var cb = unique[cid];
            var nm = cb.customerItem != null ? cb.customerItem.customerName : "";
            var line = $"#{cb.customerID}  {(string.IsNullOrWhiteSpace(nm) ? "—" : nm.Trim())}";
            CustomersTabFilterScratch.Add((cb.customerID, line));
        }
    }

    private static string BuildCustomersTabTypeSummaryLine()
    {
        var total = CustomersTabServersBuffer.Count;
        var n2 = 0;
        var n4 = 0;
        foreach (var s in CustomersTabServersBuffer)
        {
            if (s == null)
            {
                continue;
            }

            var lab = DeviceInventoryReflection.GetServerFormFactorLabel(s);
            if (string.Equals(lab, "2 U", StringComparison.Ordinal))
            {
                n2++;
            }
            else if (string.Equals(lab, "4 U", StringComparison.Ordinal))
            {
                n4++;
            }
        }

        var parts = $"{total} server{(total == 1 ? "" : "s")}";
        if (n4 > 0)
        {
            parts += $" · {n4}×4 U";
        }

        if (n2 > 0)
        {
            parts += $" · {n2}×2 U";
        }

        return parts;
    }

    private static void DrawCustomersTabCustomerFilter(float px, ref float py, float w)
    {
        GUI.Label(new Rect(px, py + 3, 78, 22), "Customer:", _stFormLabel);
        var fieldW = Mathf.Min(w - px - 100, 520f);
        var dropBtnRect = new Rect(px + 82, py, fieldW, 24);
        const float listH = 120f;
        var dropListRect = new Rect(px + 82, py + 26, fieldW, listH);

        var e = Event.current;
        if (_customersTabFilterMenuOpen && e.type == EventType.MouseDown && e.button == 0)
        {
            if (!dropBtnRect.Contains(e.mousePosition) && !dropListRect.Contains(e.mousePosition))
            {
                _customersTabFilterMenuOpen = false;
            }
        }

        var summary = _customersTabFilterMenuOpen
            ? "Select customer… ▾"
            : (GetCustomersTabFilterButtonSummaryClosed() + "  ▾");
        if (GUI.Button(dropBtnRect, summary, _stMutedBtn))
        {
            _customersTabFilterMenuOpen = !_customersTabFilterMenuOpen;
        }

        py += 28f;
        if (!_customersTabFilterMenuOpen)
        {
            return;
        }

        BuildCustomersTabFilterPickList();
        if (CustomersTabFilterScratch.Count == 0)
        {
            return;
        }

        GUI.Box(dropListRect, GUIContent.none);
        _customersTabFilterScroll = GUI.BeginScrollView(
            dropListRect,
            _customersTabFilterScroll,
            new Rect(0, 0, fieldW - 22, CustomersTabFilterScratch.Count * 28f));
        for (var i = 0; i < CustomersTabFilterScratch.Count; i++)
        {
            var entry = CustomersTabFilterScratch[i];
            if (GUI.Button(new Rect(4, i * 28f, fieldW - 28, 26), entry.line, _stMutedBtn))
            {
                if (_customersTabFilterCustomerId != entry.customerId)
                {
                    _customersTabFilterCustomerId = entry.customerId;
                    PruneServerSelectionForCustomersTabView();
                    RecomputeContentHeight();
                }

                _customersTabFilterMenuOpen = false;
            }
        }

        GUI.EndScrollView();
        py += listH + 4f;
    }

    private static void DrawCustomersView(float innerW)
    {
        FillCustomersTabServersBuffer();

        var x0 = CardPad;
        var y = CardPad;
        var cardW = innerW - CardPad * 2f;
        _lastInventoryCardWidth = cardW;
        if (_tableColumnsAutoFitPending && cardW > 80f)
        {
            AutoFitInventoryTableColumns(cardW);
            _tableColumnsAutoFitPending = false;
        }

        GUI.Label(new Rect(x0, y - 2, cardW, SectionTitleH), "Organization  /  Customers", _stBreadcrumb);
        y += SectionTitleH + 2f;
        GUI.DrawTexture(new Rect(x0, y, cardW, 1f), _texTableHeader);
        y += 6f;

        GUI.Label(new Rect(x0, y, 260, SectionTitleH), "Customer scope", _stSectionTitle);
        y += SectionTitleH + 4f;
        DrawCustomersTabCustomerFilter(x0, ref y, cardW);
        GUI.Label(new Rect(x0, y, cardW, 22), BuildCustomersTabTypeSummaryLine(), _stMuted);
        y += 26f;

        GUI.Label(new Rect(x0, y, 200, SectionTitleH), "Servers", _stSectionTitle);
        y += SectionTitleH + 4f;

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
            var hasIp = !string.IsNullOrWhiteSpace(ip) && ip != "0.0.0.0";
            var ipRaw = string.IsNullOrWhiteSpace(ip) ? "—" : ip;
            var ipCol = CellTextForCol(3, ipRaw, cardW);
            var status = CellTextForCol(5, hasIp ? "Assigned" : "No address", cardW);
            var cust = CellTextForCol(1, GetCustomerDisplayName(server), cardW);
            var eolCol = TableEolCellDisplay(server, cardW);
            var dispRaw = DeviceInventoryReflection.GetDisplayName(server);
            var dispName = CellTextForCol(0, string.IsNullOrEmpty(dispRaw) ? "—" : dispRaw, cardW);
            var typeCol = CellTextForCol(2, DeviceInventoryReflection.GetServerFormFactorLabel(server), cardW);
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
            new Rect(x0, y, cardW, 40f),
            "Select servers here, then use the bottom panel to assign a customer and IPv4 (same workflow as Devices).",
            _stHint);
    }
}
