using System;
using System.Collections.Generic;
using UnityEngine;

namespace DHCPSwitches;

// Inventory tables: EOL string snapshot, resizable columns, server/switch sort, row rendering.
// Does not own: DHCP assignment (DHCPManager), main shell (WindowUi).

public static partial class IPAMOverlay
{
    private static void RebuildIpamEolSnapshot()
    {
        _eolDisplayByInstanceId.Clear();
        FillIpamEolSnapshotForCachedDevices();
    }

    /// <summary>Add EOL for devices not yet in <see cref="_eolDisplayByInstanceId"/> (runs on list refresh; cheap when few new rows).</summary>
    private static void EnsureIpamEolSnapshotForNewDevices()
    {
        foreach (var s in _cachedServers)
        {
            if (s == null)
            {
                continue;
            }

            var id = s.GetInstanceID();
            if (_eolDisplayByInstanceId.ContainsKey(id))
            {
                continue;
            }

            if (DeviceInventoryReflection.TryGetEolDisplay(s, out var t))
            {
                _eolDisplayByInstanceId[id] = t;
            }
        }

        foreach (var sw in _cachedSwitches)
        {
            if (sw == null)
            {
                continue;
            }

            var id = sw.GetInstanceID();
            if (_eolDisplayByInstanceId.ContainsKey(id))
            {
                continue;
            }

            if (DeviceInventoryReflection.TryGetEolDisplay(sw, out var t))
            {
                _eolDisplayByInstanceId[id] = t;
            }
        }
    }

    private static void FillIpamEolSnapshotForCachedDevices()
    {
        foreach (var s in _cachedServers)
        {
            if (s == null)
            {
                continue;
            }

            if (DeviceInventoryReflection.TryGetEolDisplay(s, out var t))
            {
                _eolDisplayByInstanceId[s.GetInstanceID()] = t;
            }
        }

        foreach (var sw in _cachedSwitches)
        {
            if (sw == null)
            {
                continue;
            }

            if (DeviceInventoryReflection.TryGetEolDisplay(sw, out var t))
            {
                _eolDisplayByInstanceId[sw.GetInstanceID()] = t;
            }
        }
    }

    private static void PruneIpamEolSnapshotForRemovedDevices()
    {
        var alive = IpamEolAliveScratch;
        alive.Clear();
        foreach (var s in _cachedServers)
        {
            if (s != null)
            {
                alive.Add(s.GetInstanceID());
            }
        }

        foreach (var sw in _cachedSwitches)
        {
            if (sw != null)
            {
                alive.Add(sw.GetInstanceID());
            }
        }

        var remove = IpamEolRemoveScratch;
        remove.Clear();
        foreach (var id in _eolDisplayByInstanceId.Keys)
        {
            if (!alive.Contains(id))
            {
                remove.Add(id);
            }
        }

        foreach (var id in remove)
        {
            _eolDisplayByInstanceId.Remove(id);
        }
    }

    /// <summary>Snapshotted EOL only — no per-frame reflection fallback (that was costly during IMGUI).</summary>
    private static bool TryGetIpamEolString(UnityEngine.Object o, out string eol)
    {
        eol = null;
        if (o == null)
        {
            return false;
        }

        return _eolDisplayByInstanceId.TryGetValue(o.GetInstanceID(), out eol);
    }

    private static string TableEolCellDisplay(UnityEngine.Object o, float cardW)
    {
        if (o == null)
        {
            return CellTextForCol(4, "—", cardW);
        }

        var raw = TryGetIpamEolString(o, out var t) ? t : "—";
        return CellTextForCol(4, raw, cardW);
    }

    private static string TableDisplayName(UnityEngine.Object o, int maxLen)
    {
        var n = DeviceInventoryReflection.GetDisplayName(o);
        return string.IsNullOrEmpty(n) ? "—" : Trunc(n, maxLen);
    }

    private static string TruncCellToWidth(string text, float widthPx)
    {
        if (string.IsNullOrEmpty(text) || widthPx < 12f || _stTableCell == null)
        {
            return text ?? "";
        }

        if (_stTableCell.CalcSize(new GUIContent(text)).x <= widthPx)
        {
            return text;
        }

        const string ell = "…";
        for (var len = text.Length; len > 0; len--)
        {
            var t = len >= text.Length ? text : text.Substring(0, len) + ell;
            if (_stTableCell.CalcSize(new GUIContent(t)).x <= widthPx)
            {
                return t;
            }
        }

        return ell;
    }

    private static string CellTextForCol(int col, string raw, float cardWidth)
    {
        GetTableColumnWidths(cardWidth, out var c0, out var c1, out var c2, out var c3, out var c4, out var c5);
        var w = col switch
        {
            0 => c0,
            1 => c1,
            2 => c2,
            3 => c3,
            4 => c4,
            5 => c5,
            _ => 40f,
        };
        return TruncCellToWidth(raw, w - 8f);
    }

    private static void NormalizeTableColWeights()
    {
        float s = 0f;
        for (var i = 0; i < 6; i++)
        {
            s += TableColWeight[i];
        }

        if (s < 0.001f)
        {
            return;
        }

        for (var i = 0; i < 6; i++)
        {
            TableColWeight[i] /= s;
        }
    }

    /// <summary>Name / customer / role / IPv4 / EOL / status — widths from <see cref="TableColWeight"/>.</summary>
    private static void GetTableColumnWidths(float cardWidth, out float c0, out float c1, out float c2, out float c3, out float c4, out float c5)
    {
        NormalizeTableColWeights();
        c0 = cardWidth * TableColWeight[0];
        c1 = cardWidth * TableColWeight[1];
        c2 = cardWidth * TableColWeight[2];
        c3 = cardWidth * TableColWeight[3];
        c4 = cardWidth * TableColWeight[4];
        c5 = cardWidth * TableColWeight[5];
    }

    private static void ProcessTableColumnGrips(Rect headerRect, float cardWidth, int gripHintBase)
    {
        var e = Event.current;
        GetTableColumnWidths(cardWidth, out var w0, out var w1, out var w2, out var w3, out var w4, out var w5);
        var ws = new[] { w0, w1, w2, w3, w4, w5 };
        var x = headerRect.x;
        for (var boundary = 0; boundary < 5; boundary++)
        {
            x += ws[boundary];
            var grip = new Rect(x - 3f, headerRect.y, 6f, headerRect.height);
            var id = GUIUtility.GetControlID(gripHintBase + boundary, FocusType.Passive, grip);

            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (e.button == 0 && grip.Contains(e.mousePosition))
                    {
                        GUIUtility.hotControl = id;
                        _columnGripMouseStartX = e.mousePosition.x;
                        _columnGripWeightsStart = (float[])TableColWeight.Clone();
                        e.Use();
                    }

                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != id)
                    {
                        break;
                    }

                    e.Use();
                    if (_columnGripWeightsStart == null || _columnGripWeightsStart.Length != 6)
                    {
                        break;
                    }

                    var dx = e.mousePosition.x - _columnGripMouseStartX;
                    var dw = dx / Mathf.Max(80f, cardWidth);
                    var left = Mathf.Clamp(_columnGripWeightsStart[boundary] + dw, MinColWeight, MaxColWeight);
                    var right = Mathf.Clamp(_columnGripWeightsStart[boundary + 1] - dw, MinColWeight, MaxColWeight);
                    var pairSum = left + right;
                    var origPair = _columnGripWeightsStart[boundary] + _columnGripWeightsStart[boundary + 1];
                    if (pairSum > 0.0001f)
                    {
                        var scale = origPair / pairSum;
                        TableColWeight[boundary] = left * scale;
                        TableColWeight[boundary + 1] = right * scale;
                        NormalizeTableColWeights();
                    }

                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        _columnGripWeightsStart = null;
                        e.Use();
                    }

                    break;
                case EventType.Repaint:
                    var hover = grip.Contains(e.mousePosition) || GUIUtility.hotControl == id;
                    if (hover && _texPrimaryBtn != null)
                    {
                        var oc = GUI.color;
                        GUI.color = new Color(1f, 1f, 1f, 0.45f);
                        GUI.DrawTexture(new Rect(grip.x + 2f, grip.y + 5f, 2f, grip.height - 10f), _texPrimaryBtn);
                        GUI.color = oc;
                    }

                    break;
            }
        }
    }

    private static void AutoFitInventoryTableColumns(float cardWidth)
    {
        if (!_stylesReady || cardWidth < 200f || _stTableCell == null || _stHeaderSortBtn == null)
        {
            return;
        }

        EnsureSortedSwitches();
        EnsureSortedServers();
        var minPx = new float[6];
        void BumpHeader(int col, string label)
        {
            var w = _stHeaderSortBtn.CalcSize(new GUIContent(label)).x + 14f;
            if (w > minPx[col])
            {
                minPx[col] = w;
            }
        }

        BumpHeader(0, "Name");
        BumpHeader(1, "Customer");
        BumpHeader(2, "Role");
        BumpHeader(3, "IPv4 address");
        BumpHeader(3, "Mgmt IPv4");
        BumpHeader(4, "EOL");
        BumpHeader(5, "Status");

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

        foreach (var sw in SortedSwitchesBuffer)
        {
            if (sw == null)
            {
                continue;
            }

            BumpCell(0, DeviceInventoryReflection.GetDisplayName(sw));
            BumpCell(2, NetworkDeviceClassifier.GetKind(sw) == NetworkDeviceKind.Router ? "Router" : "L2 switch");
            if (TryGetIpamEolString(sw, out var eolSw))
            {
                BumpCell(4, eolSw);
            }

            BumpCell(5, "Active");
        }

        foreach (var server in SortedServersBuffer)
        {
            if (server == null)
            {
                continue;
            }

            BumpCell(0, DeviceInventoryReflection.GetDisplayName(server));
            BumpCell(1, GameSubnetHelper.GetCustomerDisplayName(server));
            BumpCell(2, "Server");
            var ip = DHCPManager.GetServerIP(server);
            BumpCell(3, string.IsNullOrWhiteSpace(ip) ? "—" : ip);
            if (TryGetIpamEolString(server, out var eolS))
            {
                BumpCell(4, eolS);
            }

            var hasIp = !string.IsNullOrWhiteSpace(ip) && ip != "0.0.0.0";
            BumpCell(5, hasIp ? "Assigned" : "No address");
        }

        for (var i = 0; i < 6; i++)
        {
            minPx[i] = Mathf.Clamp(minPx[i], 52f, cardWidth * 0.48f);
        }

        var sum = minPx[0] + minPx[1] + minPx[2] + minPx[3] + minPx[4] + minPx[5];
        if (sum < cardWidth)
        {
            var slack = cardWidth - sum;
            minPx[0] += slack * 0.45f;
            minPx[1] += slack * 0.3f;
            minPx[5] += slack * 0.25f;
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
            TableColWeight[i] = minPx[i] / cardWidth;
        }

        NormalizeTableColWeights();
    }

    /// <summary>Stable per Unity object so row order can change (sort) without IMGUI control ID drift.</summary>
    private static int StableRowHint(int section, UnityEngine.Object obj, int uniqueIfNull = 0)
    {
        if (obj != null)
        {
            return HashCode.Combine(section, obj.GetInstanceID());
        }

        return HashCode.Combine(section, unchecked((int)0x9E637E00), uniqueIfNull);
    }

    /// <summary>One IMGUI control per row; columns drawn on Repaint to align with headers.</summary>
    private static bool TableDataRowClick(
        Rect rowRect,
        int controlHint,
        bool altStripe,
        bool rowSelected,
        string col1,
        string col2,
        string col3,
        string col4,
        string col5,
        string col6,
        float cardWidth)
    {
        var id = GUIUtility.GetControlID(controlHint, FocusType.Passive, rowRect);
        var e = Event.current;
        var bgBase = altStripe ? _texRowB : _texRowA;
        GetTableColumnWidths(cardWidth, out var w0, out var w1, out var w2, out var w3, out var w4, out var w5);

        switch (e.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (e.button == 0 && rowRect.Contains(e.mousePosition))
                {
                    GUIUtility.hotControl = id;
                    e.Use();
                }

                break;
            case EventType.MouseUp:
                if (GUIUtility.hotControl != id)
                {
                    break;
                }

                GUIUtility.hotControl = 0;
                e.Use();
                if (rowRect.Contains(e.mousePosition))
                {
                    return true;
                }

                break;
            case EventType.Repaint:
                var hover = rowRect.Contains(e.mousePosition);
                var bg = rowSelected
                    ? _texNavActive
                    : (hover || GUIUtility.hotControl == id ? _texRowHover : bgBase);
                GUI.DrawTexture(rowRect, bg);
                var x0 = rowRect.x;
                var ry = rowRect.y;
                var rh = rowRect.height;
                DrawTableCellText(new Rect(x0, ry, w0, rh), col1);
                DrawTableCellText(new Rect(x0 + w0, ry, w1, rh), col2);
                DrawTableCellText(new Rect(x0 + w0 + w1, ry, w2, rh), col3);
                DrawTableCellText(new Rect(x0 + w0 + w1 + w2, ry, w3, rh), col4);
                DrawTableCellText(new Rect(x0 + w0 + w1 + w2 + w3, ry, w4, rh), col5);
                DrawTableCellText(new Rect(x0 + w0 + w1 + w2 + w3 + w4, ry, w5, rh), col6);
                break;
        }

        return false;
    }

    private static void DrawTableCellText(Rect r, string text)
    {
        if (Event.current.type != EventType.Repaint || _stTableCell == null)
        {
            return;
        }

        _stTableCell.Draw(r, new GUIContent(text), false, false, false, false);
    }

    private static void EnsureSortedServers()
    {
        if (!_serverSortListDirty)
        {
            return;
        }

        _serverSortListDirty = false;
        SortedServersBuffer.Clear();
        foreach (var s in _cachedServers)
        {
            SortedServersBuffer.Add(s);
        }

        SortedServersBuffer.Sort(CompareServersForSort);
    }

    private static void EnsureSortedSwitches()
    {
        if (!_switchSortListDirty)
        {
            return;
        }

        _switchSortListDirty = false;
        SortedSwitchesBuffer.Clear();
        foreach (var sw in _cachedSwitches)
        {
            SortedSwitchesBuffer.Add(sw);
        }

        SortedSwitchesBuffer.Sort(CompareSwitchesForSort);
    }

    private static int CompareServersForSort(Server a, Server b)
    {
        if (a == null && b == null)
        {
            return 0;
        }

        if (a == null)
        {
            return 1;
        }

        if (b == null)
        {
            return -1;
        }

        int cmp = _serverSortColumn switch
        {
            0 => string.Compare(
                DeviceInventoryReflection.GetDisplayName(a),
                DeviceInventoryReflection.GetDisplayName(b),
                StringComparison.OrdinalIgnoreCase),
            1 => string.Compare(
                GameSubnetHelper.GetCustomerDisplayName(a),
                GameSubnetHelper.GetCustomerDisplayName(b),
                StringComparison.OrdinalIgnoreCase),
            2 => 0,
            3 => IpSortKey(DHCPManager.GetServerIP(a)).CompareTo(IpSortKey(DHCPManager.GetServerIP(b))),
            4 => string.Compare(
                EolSortKey(a),
                EolSortKey(b),
                StringComparison.OrdinalIgnoreCase),
            5 => ServerHasAssignedIpRank(a).CompareTo(ServerHasAssignedIpRank(b)),
            _ => 0,
        };

        if (cmp == 0)
        {
            cmp = string.Compare(
                DeviceInventoryReflection.GetDisplayName(a),
                DeviceInventoryReflection.GetDisplayName(b),
                StringComparison.OrdinalIgnoreCase);
        }

        return _serverSortAscending ? cmp : -cmp;
    }

    private static int ServerHasAssignedIpRank(Server s)
    {
        var ip = DHCPManager.GetServerIP(s);
        return !string.IsNullOrWhiteSpace(ip) && ip != "0.0.0.0" ? 1 : 0;
    }

    private static string EolSortKey(Server s)
    {
        return s != null && TryGetIpamEolString(s, out var t) ? t : "\uFFFF";
    }

    private static string EolSortKeySwitch(NetworkSwitch sw)
    {
        return sw != null && TryGetIpamEolString(sw, out var t) ? t : "\uFFFF";
    }

    private static ulong IpSortKey(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip) || ip == "0.0.0.0")
        {
            return 0;
        }

        var p = ip.Trim().Split('.');
        if (p.Length != 4)
        {
            return 0;
        }

        ulong v = 0;
        for (var i = 0; i < 4; i++)
        {
            if (!uint.TryParse(p[i], out var o) || o > 255)
            {
                return 0;
            }

            v = (v << 8) | o;
        }

        return v;
    }

    private static int CompareSwitchesForSort(NetworkSwitch a, NetworkSwitch b)
    {
        if (a == null && b == null)
        {
            return 0;
        }

        if (a == null)
        {
            return 1;
        }

        if (b == null)
        {
            return -1;
        }

        int cmp = _switchSortColumn switch
        {
            0 => string.Compare(
                DeviceInventoryReflection.GetDisplayName(a),
                DeviceInventoryReflection.GetDisplayName(b),
                StringComparison.OrdinalIgnoreCase),
            1 => 0,
            2 => 0,
            3 => 0,
            4 => string.Compare(EolSortKeySwitch(a), EolSortKeySwitch(b), StringComparison.OrdinalIgnoreCase),
            5 => 0,
            _ => 0,
        };

        if (cmp == 0)
        {
            cmp = string.Compare(
                DeviceInventoryReflection.GetDisplayName(a),
                DeviceInventoryReflection.GetDisplayName(b),
                StringComparison.OrdinalIgnoreCase);
        }

        return _switchSortAscending ? cmp : -cmp;
    }

    private static void DrawSortableTableHeader(
        Rect r,
        ref int sortColumn,
        ref bool sortAscending,
        string h0,
        string h1,
        string h2,
        string h3,
        string h4,
        string h5,
        int dedupeBase,
        bool markServerSortDirtyOnClick)
    {
        GUI.DrawTexture(r, _texTableHeader);
        GetTableColumnWidths(r.width, out var c0, out var c1, out var c2, out var c3, out var c4, out var c5);
        var x = r.x;
        var labels = new[] { h0, h1, h2, h3, h4, h5 };
        var widths = new[] { c0, c1, c2, c3, c4, c5 };
        for (var i = 0; i < 6; i++)
        {
            var lab = labels[i];
            if (sortColumn == i)
            {
                lab += sortAscending ? " ▲" : " ▼";
            }

            var cell = new Rect(x, r.y, widths[i], r.height);
            if (ImguiButtonOnce(cell, lab, dedupeBase + i, _stHeaderSortBtn))
            {
                if (sortColumn == i)
                {
                    sortAscending = !sortAscending;
                }
                else
                {
                    sortColumn = i;
                    sortAscending = true;
                }

                if (markServerSortDirtyOnClick)
                {
                    _serverSortListDirty = true;
                }
                else
                {
                    _switchSortListDirty = true;
                }
            }

            x += widths[i];
        }

        ProcessTableColumnGrips(r, r.width, dedupeBase + 80);
    }
}
