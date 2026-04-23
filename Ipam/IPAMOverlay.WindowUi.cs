using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DHCPSwitches;

// Main IPAM window: title/toolbar, navigation, scroll content, bottom detail panel, server/switch selection helpers.

public static partial class IPAMOverlay
{
    private static bool HasDetailSelection()
    {
        return _selectedNetworkSwitchInstanceIds.Count > 0 || _selectedServerInstanceIds.Count > 0;
    }

    private static float GetDetailPanelHeight()
    {
        if (_selectedNetworkSwitchInstanceIds.Count > 1)
        {
            return 168f;
        }

        if (_selectedNetworkSwitchInstanceIds.Count == 1)
        {
            return 138f;
        }

        if (_selectedServerInstanceIds.Count > 1)
        {
            return _customerDropdownOpen ? 304f : 300f;
        }

        if (_selectedServer != null)
        {
            return _customerDropdownOpen ? 264f : 260f;
        }

        return 0f;
    }

    /// <summary>
    /// Non-maximized: grow window by the edit panel height so inventory keeps the same vertical space.
    /// Skipped during corner resize so drag is not overwritten each frame.
    /// </summary>
    private static void SyncIpamWindowHeightForDetailPanel()
    {
        if (_ipamResizeDrag)
        {
            return;
        }

        if (_windowMaximized)
        {
            _ipamHadDetailSelectionLastFrame = HasDetailSelection();
            return;
        }

        var dph = HasDetailSelection() ? GetDetailPanelHeight() : 0f;

        if (dph <= 0f)
        {
            if (_ipamHadDetailSelectionLastFrame)
            {
                _windowRect.height = Mathf.Max(WindowMinH, _ipamWindowBaseHeight);
            }

            _ipamWindowBaseHeight = Mathf.Max(WindowMinH, _windowRect.height);
            _ipamHadDetailSelectionLastFrame = false;
            return;
        }

        _ipamHadDetailSelectionLastFrame = true;
        var target = Mathf.Max(WindowMinH, _ipamWindowBaseHeight + dph);
        var maxH = Screen.height - _windowRect.y - 8f;
        if (maxH > WindowMinH)
        {
            target = Mathf.Min(target, maxH);
        }

        _windowRect.height = target;
    }

    private static void DrawWindow(int id)
    {
        SyncIpamWindowHeightForDetailPanel();
        var w = _windowRect.width;
        var h = _windowRect.height;
        var dhcpUnlocked = LicenseManager.IsDHCPUnlocked;
        var ipamUnlocked = LicenseManager.IsIPAMUnlocked;

        if (!Enum.IsDefined(typeof(NavSection), _navSection))
        {
            _navSection = NavSection.Devices;
        }

        if (!ipamUnlocked)
        {
            CloseIopsCalculatorModal("IPAM locked");
            _iopsToolbarScreenRect = default;
            _iopsToolbarRectWindowLocal = default;
            _iopsToolbarRectLogHash = 0;
        }

        GUI.DrawTexture(new Rect(0, 0, w, TitleBarH), _texSidebar);
        GUI.Label(new Rect(12, 6, w - 280, 22), "IPAM  ·  Data Center", _stWindowTitle);
        var maxLabel = _windowMaximized ? "Restore" : "Maximize";
        if (GUI.Button(new Rect(w - 178, 4, 82, 22), maxLabel, _stMutedBtn))
        {
            if (_windowMaximized)
            {
                _windowMaximized = false;
                _windowRect = _windowRectRestored;
            }
            else
            {
                var dphM = HasDetailSelection() ? GetDetailPanelHeight() : 0f;
                _ipamWindowBaseHeight = Mathf.Max(WindowMinH, _windowRect.height - dphM);
                _windowRectRestored = _windowRect;
                _windowMaximized = true;
                _windowRect = new Rect(10f, 10f, Screen.width - 20f, Screen.height - 20f);
            }
        }

        if (GUI.Button(new Rect(w - 86, 4, 78, 22), "Close", _stMutedBtn))
        {
            IsVisible = false;
        }

        const float licBtnW = 84f;
        const float licBtnH = 22f;
        const float licY = 28f;
        var licIpamX = w - 90f;
        if (ImguiButtonOnce(
                new Rect(licIpamX, licY, licBtnW, licBtnH),
                new GUIContent(
                    ipamUnlocked ? "IPAM: ON" : "IPAM: locked",
                    "Toggle IPAM (inventory tables, IP editor, navigation). Ctrl+D toggles DHCP+IPAM together."),
                8801,
                ipamUnlocked ? _stPrimaryBtn : _stMutedBtn))
        {
            LicenseManager.ToggleIpamUnlock();
        }

        var licDhcpX = licIpamX - licBtnW - 8f;
        if (ImguiButtonOnce(
                new Rect(licDhcpX, licY, licBtnW, licBtnH),
                new GUIContent(
                    dhcpUnlocked ? "DHCP: ON" : "DHCP: locked",
                    "Toggle DHCP (per-server DHCP, detail panel). Ctrl+D toggles DHCP+IPAM together."),
                8802,
                dhcpUnlocked ? _stPrimaryBtn : _stMutedBtn))
        {
            LicenseManager.ToggleDhcpUnlock();
        }

        var toolbarY = TitleBarH;
        GUI.DrawTexture(new Rect(0, toolbarY, w, ToolbarH), _texToolbar);
        GUI.Label(new Rect(16, toolbarY + 6, w - 32f, 22), "Inventory", _stToolbarTitle);
        GUI.Label(new Rect(16, toolbarY + 26, w - 32f, 16), "Live devices · IPv4 assignments", _stToolbarSub);

        var btnRowY = toolbarY + ToolbarTitleBlockH;
        // Pack from the right on the second row only (keeps row 1 clear for the title).
        const float tr = 14f;
        const float ty = 4f;
        const float g = 8f;
        const float btnH = 26f;
        float TW(GUIStyle st, string t) => ToolbarTextButtonWidth(st, t);
        var fitColsW = TW(_stMutedBtn, "Fit columns");
        var iopsCalcW = TW(_stMutedBtn, "IOPS calc");
        var tx = w - tr;
        tx -= g + fitColsW;
        if (ImguiButtonOnce(new Rect(tx, btnRowY + ty, fitColsW, btnH), "Fit columns", 16, _stMutedBtn))
        {
            if (_lastInventoryCardWidth > 80f)
            {
                AutoFitInventoryTableColumns(_lastInventoryCardWidth);
            }
            else
            {
                _tableColumnsAutoFitPending = true;
            }
        }

        tx -= g + iopsCalcW;
        if (ipamUnlocked)
        {
            var iopsLocal = new Rect(tx, btnRowY + ty, iopsCalcW, btnH);
            _iopsToolbarRectWindowLocal = iopsLocal;
            var tl = GUIUtility.GUIToScreenPoint(new Vector2(iopsLocal.xMin, iopsLocal.yMin));
            var br = GUIUtility.GUIToScreenPoint(new Vector2(iopsLocal.xMax, iopsLocal.yMax));
            _iopsToolbarScreenRect = Rect.MinMaxRect(
                Mathf.Min(tl.x, br.x),
                Mathf.Min(tl.y, br.y),
                Mathf.Max(tl.x, br.x),
                Mathf.Max(tl.y, br.y));
            if (ModDebugLog.IsIpamFileLogEnabled)
            {
                var sh = (int)(tl.x * 3f + tl.y * 5f + br.x * 7f + br.y * 11f);
                if (sh != _iopsToolbarRectLogHash)
                {
                    _iopsToolbarRectLogHash = sh;
                    IpamDebugLog.IopsToolbarScreenRectUpdated(_windowRect, iopsLocal, _iopsToolbarScreenRect);
                }
            }

            if (IopsCalcToolbarButton(iopsLocal, "IOPS calc"))
            {
                OpenIopsCalculator();
                if (ModDebugLog.IsIpamFileLogEnabled)
                {
                    IpamDebugLog.IopsOpenedViaImgui(Time.frameCount);
                }
            }
        }

        if (!string.IsNullOrEmpty(_ipamToast) && Time.realtimeSinceStartup < _ipamToastUntil)
        {
            GUI.Label(new Rect(16, btnRowY + btnH + 2f, w - 32f, 22f), _ipamToast, _stHint);
        }

        var bodyTop = toolbarY + ToolbarH;
        var detailH = HasDetailSelection() ? GetDetailPanelHeight() : 0f;
        var bodyH = h - bodyTop - detailH;
        GUI.DrawTexture(new Rect(0, bodyTop, w, bodyH), _texPageBg);

        // Sidebar
        GUI.DrawTexture(new Rect(0, bodyTop, SidebarW, bodyH), _texSidebar);
        GUI.Label(new Rect(12, bodyTop + 10, SidebarW - 16, 16), "NAVIGATION", _stNavHint);
        DrawNavEntry(new Rect(8, bodyTop + 30, SidebarW - 8, 32), NavSection.Dashboard, "Dashboard");
        DrawNavEntry(new Rect(8, bodyTop + 64, SidebarW - 8, 32), NavSection.Devices, "Devices");
        DrawNavEntry(new Rect(8, bodyTop + 98, SidebarW - 8, 32), NavSection.IpAddresses, "IP addresses");
        DrawNavEntry(new Rect(8, bodyTop + 132, SidebarW - 8, 32), NavSection.Customers, "Customers");
        var tipY = bodyTop + 172f;
        var tipH = Mathf.Max(36f, bodyTop + bodyH - tipY - 8f);
        GUI.Label(
            new Rect(8, tipY, SidebarW - 12, tipH),
            "Tip: plain click selects one; Ctrl toggles; Shift+click range within the same table (switches or servers) from the last plain click there. Drag column headers to resize; Fit columns sizes to content.",
            _stNavHint);

        var contentX = SidebarW + 10f;
        var contentW = w - contentX - 12f;

        if (!ipamUnlocked)
        {
            GUI.DrawTexture(new Rect(contentX, bodyTop + 8, contentW, bodyH - 16), _texCard);
            GUI.Label(new Rect(contentX + CardPad, bodyTop + 24, contentW - CardPad * 2, 40), "Organization  /  Devices", _stBreadcrumb);
            GUI.Label(
                new Rect(contentX + CardPad, bodyTop + 56, contentW - CardPad * 2, 60),
                "IPAM license not unlocked.\nUse the IPAM: locked button in the title bar (or Ctrl+D) to unlock.",
                _stMuted);
            GUI.DragWindow(new Rect(0, 0, w, TitleBarH + ToolbarH));
            return;
        }

        var scrollTop = bodyTop + 8f;
        var scrollH = bodyH - 16f;
        var scrollViewRect = new Rect(contentX, scrollTop, contentW, scrollH);
        var innerW = scrollViewRect.width - 20f;

        // IOPS modal (drawn after GUI.Window) blocks input via its own layer; do not disable the scroll view
        // here — that froze scroll/selection whenever the dialog failed to paint on top.

        GUI.DrawTexture(new Rect(contentX + 2, scrollTop + 2, contentW - 4, scrollH - 4), _texCard);

        _scroll = GUI.BeginScrollView(
            scrollViewRect,
            _scroll,
            new Rect(0, 0, innerW, _cachedContentHeight));

        switch (_navSection)
        {
            case NavSection.Dashboard:
                DrawDashboard(innerW);
                break;
            case NavSection.Devices:
                DrawDeviceTables(innerW);
                break;
            case NavSection.IpAddresses:
                DrawIpAddressTable(innerW);
                break;
            case NavSection.Customers:
                DrawCustomersView(innerW);
                break;
        }

        GUI.EndScrollView();

        if (HasDetailSelection())
        {
            var panelTop = h - detailH;
            GUI.DrawTexture(new Rect(0, panelTop, w, detailH), _texPageBg);
            if (_selectedNetworkSwitchInstanceIds.Count > 0)
            {
                DrawSwitchDetail();
            }
            else
            {
                DrawServerDetail();
            }
        }

        // BeginScrollView/EndScrollView can leave GUI.enabled false on Unity's internal stack.
        GUI.enabled = true;

        if (!_iopsCalculatorOpen)
        {
            DrawWindowResizeHandle(w, h);
        }

        GUI.DragWindow(new Rect(0, 0, w, TitleBarH + ToolbarH));

        // Consume any unhandled mouse down events to prevent them from passing through to underlying UI
        if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
        {
            Event.current.Use();
        }
    }
    private static bool IsServerRowSelected(Server server)
    {
        return server != null && _selectedServerInstanceIds.Contains(server.GetInstanceID());
    }

    private static bool IsSwitchRowSelected(NetworkSwitch sw)
    {
        return sw != null && _selectedNetworkSwitchInstanceIds.Contains(sw.GetInstanceID());
    }

    private static void ClearSwitchSelection()
    {
        _selectedNetworkSwitchInstanceIds.Clear();
        _selectedNetworkSwitch = null;
        _switchRangeAnchorInstanceId = -1;
    }

    private static void UpdatePrimarySelectedSwitch()
    {
        _selectedNetworkSwitch = null;
        EnsureSortedSwitches();
        foreach (var sw in SortedSwitchesBuffer)
        {
            if (sw != null && _selectedNetworkSwitchInstanceIds.Contains(sw.GetInstanceID()))
            {
                _selectedNetworkSwitch = sw;
                return;
            }
        }
    }

    private static NetworkSwitch FindNetworkSwitchByInstanceId(int instanceId)
    {
        foreach (var sw in _cachedSwitches)
        {
            if (sw != null && sw.GetInstanceID() == instanceId)
            {
                return sw;
            }
        }

        return null;
    }

    private static int FindSortedSwitchIndex(int instanceId)
    {
        EnsureSortedSwitches();
        for (var i = 0; i < SortedSwitchesBuffer.Count; i++)
        {
            var sw = SortedSwitchesBuffer[i];
            if (sw != null && sw.GetInstanceID() == instanceId)
            {
                return i;
            }
        }

        return -1;
    }

    /// <param name="ctrlHeld">Windows Explorer style: Ctrl toggles membership without clearing the rest.</param>
    private static void ActivateSwitchRow(NetworkSwitch sw, bool ctrlHeld)
    {
        if (sw == null)
        {
            return;
        }

        _selectedServerInstanceIds.Clear();
        _selectedServer = null;
        _serverRangeAnchorInstanceId = -1;
        if (!ctrlHeld)
        {
            _selectedNetworkSwitchInstanceIds.Clear();
            _selectedNetworkSwitchInstanceIds.Add(sw.GetInstanceID());
        }
        else
        {
            if (!_selectedNetworkSwitchInstanceIds.Add(sw.GetInstanceID()))
            {
                _selectedNetworkSwitchInstanceIds.Remove(sw.GetInstanceID());
            }
        }

        UpdatePrimarySelectedSwitch();
    }

    private static void HandleSwitchRowClick(NetworkSwitch sw, int sortedIndex)
    {
        if (sw == null)
        {
            return;
        }

        var e = Event.current;
        var ctrl = e.control || e.command;
        var shift = e.shift;
        _selectedServerInstanceIds.Clear();
        _selectedServer = null;
        _serverRangeAnchorInstanceId = -1;
        _customerDropdownOpen = false;

        if (shift && !ctrl && _switchRangeAnchorInstanceId >= 0)
        {
            var anchorIdx = FindSortedSwitchIndex(_switchRangeAnchorInstanceId);
            if (anchorIdx < 0)
            {
                anchorIdx = sortedIndex;
            }

            var lo = Mathf.Min(anchorIdx, sortedIndex);
            var hi = Mathf.Max(anchorIdx, sortedIndex);
            _selectedNetworkSwitchInstanceIds.Clear();
            for (var i = lo; i <= hi; i++)
            {
                var s = SortedSwitchesBuffer[i];
                if (s != null)
                {
                    _selectedNetworkSwitchInstanceIds.Add(s.GetInstanceID());
                }
            }

            UpdatePrimarySelectedSwitch();
            return;
        }

        if (ctrl)
        {
            ActivateSwitchRow(sw, true);
            return;
        }

        _switchRangeAnchorInstanceId = sw.GetInstanceID();
        ActivateSwitchRow(sw, false);
    }

    /// <param name="ctrlHeld">Windows Explorer style: Ctrl toggles membership without clearing the rest.</param>
    private static void ActivateServerRow(Server server, bool ctrlHeld)
    {
        if (server == null)
        {
            return;
        }

        ClearSwitchSelection();
        if (!ctrlHeld)
        {
            _selectedServerInstanceIds.Clear();
            _selectedServerInstanceIds.Add(server.GetInstanceID());
        }
        else
        {
            if (!_selectedServerInstanceIds.Add(server.GetInstanceID()))
            {
                _selectedServerInstanceIds.Remove(server.GetInstanceID());
            }
        }

        if (!ctrlHeld)
        {
            _customerDropdownOpen = false;
        }

        UpdateAnchorServerForDetail();
    }

    private static int FindServerIndexInList(int instanceId, List<Server> list)
    {
        if (list == null)
        {
            return -1;
        }

        for (var i = 0; i < list.Count; i++)
        {
            var s = list[i];
            if (s != null && s.GetInstanceID() == instanceId)
            {
                return i;
            }
        }

        return -1;
    }

    private static Server FindServerByInstanceId(int instanceId)
    {
        foreach (var s in _cachedServers)
        {
            if (s != null && s.GetInstanceID() == instanceId)
            {
                return s;
            }
        }

        return null;
    }

    private static void HandleServerRowClick(Server server, int sortedIndex, string ip, List<Server> viewRows)
    {
        if (server == null || viewRows == null)
        {
            return;
        }

        // IMGUI: use Event modifiers — Unity Input System keyboard state is unreliable during OnGUI.
        var e = Event.current;
        var ctrl = e.control || e.command;
        var shift = e.shift;
        ClearSwitchSelection();
        _customerDropdownOpen = false;

        if (shift && !ctrl && _serverRangeAnchorInstanceId >= 0)
        {
            var anchorIdx = FindServerIndexInList(_serverRangeAnchorInstanceId, viewRows);
            if (anchorIdx < 0)
            {
                anchorIdx = sortedIndex;
            }

            var lo = Mathf.Min(anchorIdx, sortedIndex);
            var hi = Mathf.Max(anchorIdx, sortedIndex);
            _selectedServerInstanceIds.Clear();
            for (var i = lo; i <= hi; i++)
            {
                var s = viewRows[i];
                if (s != null)
                {
                    _selectedServerInstanceIds.Add(s.GetInstanceID());
                }
            }

            UpdateAnchorServerForDetail();
            DHCPManager.ClearLastSetIpError();
            if (_selectedServerInstanceIds.Count == 1)
            {
                LoadOctetsFromIp(DHCPManager.GetServerIP(server));
            }

            return;
        }

        if (ctrl)
        {
            ActivateServerRow(server, true);
            DHCPManager.ClearLastSetIpError();
            if (_selectedServerInstanceIds.Count == 1)
            {
                LoadOctetsFromIp(ip);
            }

            return;
        }

        _serverRangeAnchorInstanceId = server.GetInstanceID();
        ActivateServerRow(server, false);
        DHCPManager.ClearLastSetIpError();
        LoadOctetsFromIp(ip);
    }

    private static void UpdateAnchorServerForDetail()
    {
        _selectedServer = null;
        foreach (var s in _cachedServers)
        {
            if (s != null && _selectedServerInstanceIds.Contains(s.GetInstanceID()))
            {
                _selectedServer = s;
                break;
            }
        }
    }

    private static void DrawWindowResizeHandle(float w, float h)
    {
        if (_windowMaximized)
        {
            return;
        }

        const float sz = 18f;
        var r = new Rect(w - sz, h - sz, sz, sz);
        var id = GUIUtility.GetControlID(0x5E11C0A1, FocusType.Passive, r);
        var e = Event.current;
        switch (e.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (e.button == 0 && r.Contains(e.mousePosition))
                {
                    _ipamResizeDrag = true;
                    _ipamResizeStartMouse = e.mousePosition;
                    _ipamResizeStartSize = new Vector2(_windowRect.width, _windowRect.height);
                    GUIUtility.hotControl = id;
                    e.Use();
                }

                break;
            case EventType.MouseDrag:
                if (GUIUtility.hotControl == id && _ipamResizeDrag)
                {
                    var dx = e.mousePosition.x - _ipamResizeStartMouse.x;
                    var dy = e.mousePosition.y - _ipamResizeStartMouse.y;
                    _windowRect.width = Mathf.Max(WindowMinW, _ipamResizeStartSize.x + dx);
                    _windowRect.height = Mathf.Max(WindowMinH, _ipamResizeStartSize.y + dy);
                    e.Use();
                }

                break;
            case EventType.MouseUp:
                if (GUIUtility.hotControl == id)
                {
                    GUIUtility.hotControl = 0;
                    _ipamResizeDrag = false;
                    _windowRectRestored = _windowRect;
                    var dphR = HasDetailSelection() ? GetDetailPanelHeight() : 0f;
                    _ipamWindowBaseHeight = Mathf.Max(WindowMinH, _windowRect.height - dphR);
                    e.Use();
                }

                break;
            case EventType.Repaint:
                GUI.Box(r, "⋰", _stMutedBtn);
                break;
        }
    }

    private static void CollectSelectedServersIntoScratch()
    {
        SelectedServersScratch.Clear();
        foreach (var s in _cachedServers)
        {
            if (s != null && _selectedServerInstanceIds.Contains(s.GetInstanceID()))
            {
                SelectedServersScratch.Add(s);
            }
        }
    }

    private static void DrawNavEntry(Rect r, NavSection target, string text)
    {
        var active = _navSection == target;
        if (active)
        {
            GUI.DrawTexture(r, _texNavActive);
            GUI.Label(new Rect(r.x + 6, r.y, r.width - 8, r.height), text, _stNavItemActive);
            return;
        }

        if (ImguiButtonOnce(r, text, 300 + (int)target, _stNavBtn))
        {
            _navSection = target;
            _scroll = Vector2.zero;
            _customersTabFilterMenuOpen = false;
            RecomputeContentHeight();
        }
    }

    private static void RecomputeContentHeight()
    {
        switch (_navSection)
        {
            case NavSection.Dashboard:
                _cachedContentHeight = ComputeDashboardContentHeight();
                return;
            case NavSection.IpAddresses:
            {
                var sv = _cachedServers.Length;
                var y = CardPad + SectionTitleH + 2f + 7f + SectionTitleH + 4f + TableHeaderH + sv * TableRowH + CardPad;
                _cachedContentHeight = Mathf.Max(220f, y);
                return;
            }
            case NavSection.Customers:
            {
                var n = CountServersMatchingCustomersTabFilter();
                _cachedContentHeight = ComputeCustomersTabContentHeight(n);
                return;
            }
            default:
                break;
        }

        var sw = _cachedSwitches.Length;
        var sv2 = _cachedServers.Length;
        var yd = CardPad;
        yd += SectionTitleH + 2f + 7f;
        yd += SectionTitleH + 4f + TableHeaderH + sw * TableRowH;
        yd += 18f + SectionTitleH + 4f + TableHeaderH + sv2 * TableRowH;
        _cachedContentHeight = Mathf.Max(260f, yd + CardPad);
    }

    private static NetworkSwitch[] FilterAlive(NetworkSwitch[] raw)
    {
        if (raw == null || raw.Length == 0)
        {
            return System.Array.Empty<NetworkSwitch>();
        }

        var list = new List<NetworkSwitch>(raw.Length);
        foreach (var x in raw)
        {
            if (x != null)
            {
                list.Add(x);
            }
        }

        return list.ToArray();
    }

    private static Server[] FilterAlive(Server[] raw)
    {
        if (raw == null || raw.Length == 0)
        {
            return System.Array.Empty<Server>();
        }

        var list = new List<Server>(raw.Length);
        foreach (var x in raw)
        {
            if (x != null)
            {
                list.Add(x);
            }
        }

        return list.ToArray();
    }

    /// <summary>
    /// IMGUI assigns control IDs in call order. Always emit the same control sequence (full-width table rows).
    /// </summary>
    private static void DrawDeviceTables(float innerW)
    {
        var x0 = CardPad;
        var y = CardPad;
        var cardW = innerW - CardPad * 2f;
        _lastInventoryCardWidth = cardW;
        if (_tableColumnsAutoFitPending && cardW > 80f)
        {
            AutoFitInventoryTableColumns(cardW);
            _tableColumnsAutoFitPending = false;
        }

        GUI.Label(new Rect(x0, y - 2, cardW, SectionTitleH), "Organization  /  Devices  /  All", _stBreadcrumb);
        y += SectionTitleH + 2f;

        GUI.DrawTexture(new Rect(x0, y, cardW, 1f), _texTableHeader);
        y += 6f;

        // --- Switches card ---
        GUI.Label(new Rect(x0, y, 200, SectionTitleH), "Network switches", _stSectionTitle);
        y += SectionTitleH + 4f;

        DrawSortableTableHeader(
            new Rect(x0, y, cardW, TableHeaderH),
            ref _switchSortColumn,
            ref _switchSortAscending,
            "Name",
            "Customer",
            "Role",
            "Mgmt IPv4",
            "EOL",
            "Status",
            600,
            false);
        y += TableHeaderH;

        EnsureSortedSwitches();
        for (var i = 0; i < SortedSwitchesBuffer.Count; i++)
        {
            var sw = SortedSwitchesBuffer[i];
            var r = new Rect(x0, y, cardW, TableRowH);
            var nameRaw = sw != null ? DeviceInventoryReflection.GetDisplayName(sw) : "(removed)";
            var name = CellTextForCol(0, string.IsNullOrEmpty(nameRaw) ? "—" : nameRaw, cardW);
            var roleRaw = "Switch";
            var role = CellTextForCol(2, roleRaw, cardW);
            var eolCol = TableEolCellDisplay(sw, cardW);
            if (TableDataRowClick(
                    r,
                    StableRowHint(1, sw, i),
                    i % 2 == 1,
                    IsSwitchRowSelected(sw),
                    name,
                    "—",
                    role,
                    "—",
                    eolCol,
                    CellTextForCol(5, "Active", cardW),
                    cardW))
            {
                HandleSwitchRowClick(sw, i);
            }

            y += TableRowH;
        }

        y += 18f;

        // --- Servers card ---
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

        EnsureSortedServers();
        for (var i = 0; i < SortedServersBuffer.Count; i++)
        {
            var server = SortedServersBuffer[i];
            var r = new Rect(x0, y, cardW, TableRowH);

            if (server == null)
            {
                TableDataRowClick(
                    r,
                    StableRowHint(2, null, i),
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
                    StableRowHint(2, server, i),
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
                HandleServerRowClick(server, i, ip, SortedServersBuffer);
            }

            y += TableRowH;
        }
    }

    private static void CollectDashboardStats(
        out int customerContracts,
        out int n4u,
        out int n2u,
        out int nOther,
        out int totalServers,
        out long ratedIopsSum)
    {
        customerContracts = 0;
        n4u = 0;
        n2u = 0;
        nOther = 0;
        totalServers = 0;
        ratedIopsSum = 0;

        var seen = new Dictionary<int, byte>();
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

            seen[cid] = 0;
        }

        customerContracts = seen.Count;

        foreach (var s in _cachedServers)
        {
            if (s == null)
            {
                continue;
            }

            totalServers++;
            var lab = DeviceInventoryReflection.GetServerFormFactorLabel(s);
            if (string.Equals(lab, "4 U", StringComparison.Ordinal))
            {
                n4u++;
                ratedIopsSum += IopsPer4UServer;
            }
            else if (string.Equals(lab, "2 U", StringComparison.Ordinal))
            {
                n2u++;
                ratedIopsSum += IopsPer2UServer;
            }
            else
            {
                nOther++;
            }
        }
    }

    private static readonly Color32 DashboardColor4U = new(0, 188, 164, 255);
    private static readonly Color32 DashboardColor2U = new(56, 189, 248, 255);
    private static readonly Color32 DashboardColorOther = new(148, 163, 184, 255);
    private static readonly Color32 DashboardTrackDim = new(34, 42, 56, 255);

    private static float ComputeDashboardContentHeight()
    {
        const float heroH = 92f;
        const float sectionGap = 18f;
        const float barH = 28f;
        const float legendBlockH = 72f;
        const float sceneCardH = 68f;
        var y = CardPad;
        y += SectionTitleH + 2f + 1f + 6f;
        y += heroH + sectionGap;
        y += SectionTitleH + 4f + barH + 10f + legendBlockH;
        y += 26f + sectionGap;
        y += SectionTitleH + 4f + sceneCardH + 16f;
        y += 72f + CardPad;
        return Mathf.Max(420f, y);
    }

    private static void DashboardDrawTintedRect(Rect r, Color tint)
    {
        if (_texWhite == null)
        {
            return;
        }

        GUI.DrawTexture(r, _texWhite, ScaleMode.StretchToFill, false, 0f, tint, 0f, 0f);
    }

    private static void DrawDashboardHeroCard(Rect r, string title, string value, string subtitle)
    {
        if (_texCard != null)
        {
            GUI.DrawTexture(r, _texCard, ScaleMode.StretchToFill, false, 0f, Color.white, 0f, 0f);
        }

        if (_texNavActive != null)
        {
            GUI.DrawTexture(new Rect(r.x, r.y, 4f, r.height), _texNavActive, ScaleMode.StretchToFill, false, 0f, Color.white, 0f, 0f);
        }

        var padX = 14f;
        var innerW = Mathf.Max(40f, r.width - padX - 10f);
        var tx = r.x + padX;
        var ty = r.y + 10f;
        GUI.Label(new Rect(tx, ty, innerW, 16f), title, _stMuted);
        ty += 18f;
        var valSt = _stDashboardHeroValue ?? _stIopsResultCounts;
        GUI.Label(new Rect(tx, ty, innerW, 36f), value, valSt);
        ty += 38f;
        GUI.Label(new Rect(tx, ty, innerW, 22f), subtitle, _stMuted);
    }

    private static void DrawDashboardServerMixBar(float x0, float y, float w, float h, int n4u, int n2u, int nOther)
    {
        var bar = new Rect(x0, y, w, h);
        DashboardDrawTintedRect(bar, DashboardTrackDim);
        var mix = n4u + n2u + nOther;
        if (mix <= 0)
        {
            GUI.Label(bar, "No servers in inventory cache", _stMutedCenter ?? _stMuted);
            return;
        }

        var w4 = (n4u / (float)mix) * w;
        var w2 = (n2u / (float)mix) * w;
        var wO = Mathf.Max(0f, w - w4 - w2);
        var x = x0;
        if (w4 > 0.5f)
        {
            DashboardDrawTintedRect(new Rect(x, y, w4, h), DashboardColor4U);
            x += w4;
        }

        if (w2 > 0.5f)
        {
            DashboardDrawTintedRect(new Rect(x, y, w2, h), DashboardColor2U);
            x += w2;
        }

        if (wO > 0.5f)
        {
            DashboardDrawTintedRect(new Rect(x, y, wO, h), DashboardColorOther);
        }
    }

    private static void DrawDashboardLegendLine(Rect r, Color32 swatchColor, string text)
    {
        const float sw = 10f;
        DashboardDrawTintedRect(new Rect(r.x, r.y + 5f, sw, sw), swatchColor);
        GUI.Label(new Rect(r.x + 16f, r.y, Mathf.Max(20f, r.width - 16f), 22f), text, _stMuted);
    }

    private static void DrawDashboardSceneCard(Rect r, string title, int count, float fill01, Color32 barTint)
    {
        if (_texCard != null)
        {
            GUI.DrawTexture(r, _texCard, ScaleMode.StretchToFill, false, 0f, Color.white, 0f, 0f);
        }

        var pad = 12f;
        var innerW = Mathf.Max(40f, r.width - pad * 2f);
        var tx = r.x + pad;
        var ty = r.y + 8f;
        GUI.Label(new Rect(tx, ty, innerW, 16f), title, _stMuted);
        ty += 18f;
        var valSt = _stIopsResultCounts ?? _stTableCell;
        GUI.Label(new Rect(tx, ty, innerW, 30f), count.ToString("N0"), valSt);
        ty += 32f;
        var track = new Rect(tx, ty, innerW, 8f);
        DashboardDrawTintedRect(track, DashboardTrackDim);
        var fill = Mathf.Clamp01(fill01);
        if (fill > 0.001f)
        {
            DashboardDrawTintedRect(new Rect(track.x, track.y, Mathf.Max(2f, track.width * fill), track.height), barTint);
        }
    }

    private static void DrawDashboard(float innerW)
    {
        CollectDashboardStats(
            out var customerContracts,
            out var n4u,
            out var n2u,
            out var nOther,
            out var totalServers,
            out var ratedIopsSum);

        var x0 = CardPad;
        var y = CardPad;
        var w = innerW - CardPad * 2f;
        const float heroH = 92f;
        const float heroGap = 12f;
        const float sectionGap = 18f;
        const float barH = 28f;
        const float legendRowH = 22f;
        const float sceneCardH = 68f;

        GUI.Label(new Rect(x0, y - 2, w, SectionTitleH), "Organization  /  Dashboard", _stBreadcrumb);
        y += SectionTitleH + 2f;
        GUI.DrawTexture(new Rect(x0, y, w, 1f), _texTableHeader);
        y += 6f;

        var half = (w - heroGap) * 0.5f;
        DrawDashboardHeroCard(
            new Rect(x0, y, half, heroH),
            "Customer contracts",
            customerContracts.ToString("N0"),
            "Distinct CustomerBase IDs in scene");
        DrawDashboardHeroCard(
            new Rect(x0 + half + heroGap, y, half, heroH),
            "Rated IOPS (2 U + 4 U)",
            ratedIopsSum.ToString("N0"),
            $"{n4u}×{IopsPer4UServer:N0} + {n2u}×{IopsPer2UServer:N0} (4 U + 2 U only)");
        y += heroH + sectionGap;

        GUI.Label(new Rect(x0, y, w, SectionTitleH), "Server inventory (by rack type)", _stSectionTitle);
        y += SectionTitleH + 4f;
        DrawDashboardServerMixBar(x0, y, w, barH, n4u, n2u, nOther);
        y += barH + 10f;

        var mix = n4u + n2u + nOther;
        float P(int n) => mix > 0 ? (100f * n) / mix : 0f;
        DrawDashboardLegendLine(
            new Rect(x0, y, w, legendRowH),
            DashboardColor4U,
            $"4 U servers  ·  {n4u}  ({P(n4u):0.#}%)  — {IopsPer4UServer:N0} IOPS each");
        y += legendRowH;
        DrawDashboardLegendLine(
            new Rect(x0, y, w, legendRowH),
            DashboardColor2U,
            $"2 U servers  ·  {n2u}  ({P(n2u):0.#}%)  — {IopsPer2UServer:N0} IOPS each");
        y += legendRowH;
        DrawDashboardLegendLine(
            new Rect(x0, y, w, legendRowH),
            DashboardColorOther,
            $"Other / unknown  ·  {nOther}  ({P(nOther):0.#}%)  — excluded from IOPS total");
        y += legendRowH + 6f;

        GUI.Label(new Rect(x0, y, w, 24f), $"Total rated IOPS:  {ratedIopsSum:N0}", _stTableCell);
        y += 26f + sectionGap;

        var swCount = _cachedSwitches.Length;
        var sceneDenom = Mathf.Max(1, swCount + totalServers);
        var fillSw = swCount / (float)sceneDenom;
        var fillSv = totalServers / (float)sceneDenom;

        GUI.Label(new Rect(x0, y, w, SectionTitleH), "Scene devices", _stSectionTitle);
        y += SectionTitleH + 4f;
        var halfScene = (w - heroGap) * 0.5f;
        DrawDashboardSceneCard(
            new Rect(x0, y, halfScene, sceneCardH),
            "Network switches",
            swCount,
            fillSw,
            DashboardColor2U);
        DrawDashboardSceneCard(
            new Rect(x0 + halfScene + heroGap, y, halfScene, sceneCardH),
            "Servers (all types)",
            totalServers,
            fillSv,
            DashboardColor4U);
        y += sceneCardH + 16f;

        GUI.Label(
            new Rect(x0, y, w, 72f),
            "IOPS totals use the same mod constants as the IOPS sizing calculator. Open Devices or Customers for full tables; assign IPs from the bottom panel.",
            _stHint);
    }

    private static void DrawIpAddressTable(float innerW)
    {
        var x0 = CardPad;
        var y = CardPad;
        var cardW = innerW - CardPad * 2f;
        _lastInventoryCardWidth = cardW;
        if (_tableColumnsAutoFitPending && cardW > 80f)
        {
            AutoFitInventoryTableColumns(cardW);
            _tableColumnsAutoFitPending = false;
        }

        GUI.Label(new Rect(x0, y - 2, cardW, SectionTitleH), "Organization  /  IP addresses", _stBreadcrumb);
        y += SectionTitleH + 2f;
        GUI.DrawTexture(new Rect(x0, y, cardW, 1f), _texTableHeader);
        y += 6f;

        GUI.Label(new Rect(x0, y, 220, SectionTitleH), "IPv4 assignments", _stSectionTitle);
        y += SectionTitleH + 4f;

        DrawSortableTableHeader(
            new Rect(x0, y, cardW, TableHeaderH),
            ref _serverSortColumn,
            ref _serverSortAscending,
            "Device",
            "Customer",
            "Type",
            "IPv4 address",
            "EOL",
            "Status",
            620,
            true);
        y += TableHeaderH;

        EnsureSortedServers();
        for (var i = 0; i < SortedServersBuffer.Count; i++)
        {
            var server = SortedServersBuffer[i];
            var r = new Rect(x0, y, cardW, TableRowH);
            if (server == null)
            {
                TableDataRowClick(
                    r,
                    StableRowHint(4, null, i),
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
                    StableRowHint(4, server, i),
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
                HandleServerRowClick(server, i, ip, SortedServersBuffer);
            }

            y += TableRowH;
        }
    }

    private static string Trunc(string s, int max)
    {
        if (string.IsNullOrEmpty(s))
        {
            return "";
        }

        return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
    }

    private static string GetCustomerDropdownSummaryLabel()
    {
        if (SelectedServersScratch.Count == 0)
        {
            return "Choose customer…";
        }

        var d0 = GetCustomerDisplayName(SelectedServersScratch[0]);
        for (var i = 1; i < SelectedServersScratch.Count; i++)
        {
            if (GetCustomerDisplayName(SelectedServersScratch[i]) != d0)
            {
                return "(different customers in selection)";
            }
        }

        if (d0 == "—")
        {
            return "Choose customer…";
        }

        var id0 = SelectedServersScratch[0].GetCustomerID();
        return $"#{id0}  {Trunc(d0, 40)}";
    }

    private static void ApplyCustomerAssignToSelection(CustomerBase cb)
    {
        if (cb == null)
        {
            return;
        }

        DHCPManager.ClearLastSetIpError();
        var assigned = 0;
        var failed = 0;
        foreach (var server in SelectedServersScratch)
        {
            if (server == null)
            {
                continue;
            }

            if (TrySetServerCustomer(server, cb))
            {
                assigned++;
            }
            else
            {
                failed++;
            }
        }

        if (assigned > 0)
        {
            InvalidateDeviceCache();
            UpdateAnchorServerForDetail();
            if (LicenseManager.IsDHCPUnlocked)
            {
                ModDebugLog.Bootstrap();
                ModDebugLog.WriteDhcpAssign(
                    $"UI: after customer assign to {GetCustomerName(cb)} — invoking AssignDhcpToServers (selection={SelectedServersScratch.Count})");
                DHCPManager.AssignDhcpToServers(SelectedServersScratch);
                DHCPManager.ClearLastSetIpError();
                BeginImGuiInputRecoveryBurst();
            }
        }

        if (failed > 0)
        {
            DHCPManager.SetLastIpamError("Customer assignment failed for one or more selected servers.");
        }
    }

    private static bool TrySetServerCustomer(Server server, CustomerBase customer)
    {
        if (server == null || customer == null)
        {
            return false;
        }

        if (!TryGetCustomerId(customer, out var customerId) || customerId < 0)
        {
            return false;
        }

        var serverType = server.GetType();
        var methodNames = new[]
        {
            "SetCustomerID",
            "SetCustomerId",
            "SetCustomer",
            "AssignCustomer",
            "AssignCustomerID",
            "AssignCustomerId",
            "SetCustomerObject",
            "SetCustomerBase"
        };

        foreach (var methodName in methodNames)
        {
            if (TryInvokeCustomerAssignmentMethod(server, serverType, methodName, customerId, customer))
            {
                return true;
            }
        }

        foreach (var method in serverType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!method.Name.Contains("Customer", StringComparison.OrdinalIgnoreCase) || method.GetParameters().Length != 1)
            {
                continue;
            }

            if (TryInvokeCustomerAssignmentMethod(server, serverType, method.Name, customerId, customer))
            {
                return true;
            }
        }

        var candidateNames = new[]
        {
            "customerID",
            "customerId",
            "CustomerID",
            "CustomerId",
            "customer",
            "Customer",
            "customerBase",
            "CustomerBase"
        };

        foreach (var name in candidateNames)
        {
            var field = serverType.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                if (TryWriteServerCustomerField(field, server, customerId, customer))
                {
                    return true;
                }
            }

            var prop = serverType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop != null && prop.CanWrite)
            {
                if (TryWriteServerCustomerProperty(prop, server, customerId, customer))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryInvokeCustomerAssignmentMethod(Server server, Type serverType, string methodName, int customerId, CustomerBase customer)
    {
        var method = serverType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (method == null || method.GetParameters().Length != 1)
        {
            return false;
        }

        var parameter = method.GetParameters()[0];
        var paramType = parameter.ParameterType;
        try
        {
            if (paramType == typeof(int) || paramType == typeof(short) || paramType == typeof(long)
                || paramType == typeof(byte) || paramType == typeof(sbyte)
                || paramType == typeof(ushort) || paramType == typeof(uint) || paramType == typeof(ulong))
            {
                var value = Convert.ChangeType(customerId, paramType);
                method.Invoke(server, new[] { value });
                return true;
            }

            if (paramType.IsAssignableFrom(typeof(CustomerBase)))
            {
                method.Invoke(server, new object[] { customer });
                return true;
            }

            if (paramType == typeof(object))
            {
                method.Invoke(server, new object[] { customer });
                return true;
            }
        }
        catch
        {
            // ignore invocation failures and keep looking
        }

        return false;
    }

    private static bool TryWriteServerCustomerField(FieldInfo field, Server server, int customerId, CustomerBase customer)
    {
        var fieldType = field.FieldType;
        try
        {
            if (fieldType == typeof(int) || fieldType == typeof(short) || fieldType == typeof(long)
                || fieldType == typeof(byte) || fieldType == typeof(sbyte)
                || fieldType == typeof(ushort) || fieldType == typeof(uint) || fieldType == typeof(ulong))
            {
                field.SetValue(server, Convert.ChangeType(customerId, fieldType));
                return true;
            }

            if (fieldType.IsAssignableFrom(typeof(CustomerBase)) || fieldType == typeof(object))
            {
                field.SetValue(server, customer);
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool TryWriteServerCustomerProperty(PropertyInfo prop, Server server, int customerId, CustomerBase customer)
    {
        var propType = prop.PropertyType;
        try
        {
            if (propType == typeof(int) || propType == typeof(short) || propType == typeof(long)
                || propType == typeof(byte) || propType == typeof(sbyte)
                || propType == typeof(ushort) || propType == typeof(uint) || propType == typeof(ulong))
            {
                prop.SetValue(server, Convert.ChangeType(customerId, propType));
                return true;
            }

            if (propType.IsAssignableFrom(typeof(CustomerBase)) || propType == typeof(object))
            {
                prop.SetValue(server, customer);
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool TryGetCustomerId(CustomerBase customer, out int customerId)
    {
        customerId = -1;
        if (customer == null)
        {
            return false;
        }

        try
        {
            customerId = customer.customerID;
            return true;
        }
        catch
        {
        }

        var customerType = customer.GetType();
        var idField = customerType.GetField("customerID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (idField != null && idField.FieldType == typeof(int))
        {
            customerId = (int)idField.GetValue(customer);
            return true;
        }

        var idProperty = customerType.GetProperty("customerID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (idProperty != null && idProperty.PropertyType == typeof(int))
        {
            customerId = (int)idProperty.GetValue(customer);
            return true;
        }

        return false;
    }

    private static string GetCustomerName(CustomerBase customer)
    {
        if (customer == null)
        {
            return null;
        }

        try
        {
            return customer.customerItem != null ? customer.customerItem.customerName : null;
        }
        catch
        {
            return null;
        }
    }

    private static void DrawCustomerDropdownAssign(float px, ref float py, float w)
    {
        CustomerPickBuffer.Clear();
        var uniqueCustomers = new Dictionary<int, CustomerBase>();
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

            if (!uniqueCustomers.TryGetValue(cid, out var existing))
            {
                uniqueCustomers[cid] = cb;
                continue;
            }

            var existingName = GetCustomerName(existing);
            var currentName = GetCustomerName(cb);
            if (string.IsNullOrWhiteSpace(existingName)
                && !string.IsNullOrWhiteSpace(currentName))
            {
                uniqueCustomers[cid] = cb;
            }
        }

        CustomerPickBuffer.AddRange(uniqueCustomers.Values);
        CustomerPickBuffer.Sort((a, b) =>
        {
            TryGetCustomerId(b, out var bid);
            TryGetCustomerId(a, out var aid);
            return bid.CompareTo(aid);
        });
        GUI.Label(new Rect(px, py + 3, 78, 22), "Customer:", _stFormLabel);
        var fieldW = Mathf.Min(w - px - 100, 520f);
        var dropBtnRect = new Rect(px + 82, py, fieldW, 24);
        const float listH = 80f;
        var dropListRect = new Rect(px + 82, py + 26, fieldW, listH);

        var e = Event.current;
        if (_customerDropdownOpen && e.type == EventType.MouseDown && e.button == 0)
        {
            if (!dropBtnRect.Contains(e.mousePosition) && !dropListRect.Contains(e.mousePosition))
            {
                _customerDropdownOpen = false;
            }
        }

        string summary;
        if (CustomerPickBuffer.Count == 0)
        {
            summary = "No active contracts in scene ▾";
        }
        else if (_customerDropdownOpen)
        {
            summary = "Select customer… ▾";
        }
        else
        {
            summary = GetCustomerDropdownSummaryLabel() + "  ▾";
        }

        if (CustomerPickBuffer.Count > 0 && GUI.Button(dropBtnRect, summary, _stMutedBtn))
        {
            _customerDropdownOpen = !_customerDropdownOpen;
        }
        else if (CustomerPickBuffer.Count == 0)
        {
            GUI.Label(dropBtnRect, summary, _stMuted);
        }

        py += 28f;
        if (!_customerDropdownOpen || CustomerPickBuffer.Count == 0)
        {
            return;
        }

        GUI.Box(dropListRect, GUIContent.none);
        _customerDropdownScroll = GUI.BeginScrollView(
            dropListRect,
            _customerDropdownScroll,
            new Rect(0, 0, fieldW - 22, CustomerPickBuffer.Count * 28f));
        for (var i = 0; i < CustomerPickBuffer.Count; i++)
        {
            var cb = CustomerPickBuffer[i];
            var nm = cb.customerItem != null ? cb.customerItem.customerName : "";
            var line = $"#{cb.customerID}  {(string.IsNullOrWhiteSpace(nm) ? "—" : nm.Trim())}";
            if (GUI.Button(new Rect(4, i * 28f, fieldW - 28, 26), line, _stMutedBtn))
            {
                ApplyCustomerAssignToSelection(cb);
                _customerDropdownOpen = false;
            }
        }

        GUI.EndScrollView();
        py += listH + 4f;
    }

    private static void DrawServerDetail()
    {
        CollectSelectedServersIntoScratch();
        if (SelectedServersScratch.Count == 0)
        {
            return;
        }

        var w = _windowRect.width;
        var h = _windowRect.height;
        var dph = GetDetailPanelHeight();
        var panelY = h - dph;
        GUI.DrawTexture(new Rect(0, panelY, w, 1f), _texTableHeader);

        var px = 16f;
        var py = panelY + 8f;
        var n = SelectedServersScratch.Count;

        GUI.Label(
            new Rect(px, py, w - 32, 20),
            n == 1 ? "Edit object · Server" : $"Edit object · {n} servers",
            _stSectionTitle);
        py += 22f;

        if (n > 1)
        {
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < SelectedServersScratch.Count && i < 6; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                sb.Append(Trunc(DeviceInventoryReflection.GetDisplayName(SelectedServersScratch[i]), 18));
            }

            if (SelectedServersScratch.Count > 6)
            {
                sb.Append(" …");
            }

            GUI.Label(new Rect(px, py, w - 32, 34), $"Selected: {sb}", _stMuted);
            py += 36f;
        }
        else
        {
            var s0 = SelectedServersScratch[0];
            var currentIp = DHCPManager.GetServerIP(s0);
            var ipDisp = string.IsNullOrWhiteSpace(currentIp) || currentIp == "0.0.0.0" ? "—" : currentIp;
            GUI.Label(
                new Rect(px, py, w - 32, 18),
                $"Name   {Trunc(DeviceInventoryReflection.GetDisplayName(s0), 56)}    │    IPv4   {ipDisp}",
                _stMuted);
            py += 18f;
            var hasRealIp = !string.IsNullOrWhiteSpace(currentIp) && currentIp != "0.0.0.0";
            var cidStr = hasRealIp ? s0.GetCustomerID().ToString() : "—";
            GUI.Label(
                new Rect(px, py, w - 32, 18),
                $"Game customerID   {cidStr}",
                _stMuted);
            py += 22f;
        }

        DrawCustomerDropdownAssign(px, ref py, w);
        py += 4f;
        GUI.Label(
            new Rect(px, py, w - px - 24, 16),
            "Multi-select: choosing a customer assigns them and runs DHCP on empty addresses. Single server: use DHCP auto below.",
            _stHint);
        py += 20f;

        if (n > 1)
        {
            var ox = px;
            if (ImguiButtonOnce(new Rect(ox, py, 148, 26), "DHCP all selected", 50, _stPrimaryBtn))
            {
                ModDebugLog.Bootstrap();
                ModDebugLog.WriteDhcpAssign(
                    $"UI: DHCP all selected clicked (selection={SelectedServersScratch.Count} servers)");
                DHCPManager.AssignDhcpToServers(SelectedServersScratch);
                DHCPManager.ClearLastSetIpError();
            }

            ox += 156f;
            if (ImguiButtonOnce(new Rect(ox, py, 118, 26), "Clear all IPs", 51, _stMutedBtn))
            {
                foreach (var srv in SelectedServersScratch)
                {
                    DHCPManager.SetServerIP(srv, "0.0.0.0", suppressAutoAssignOnEmpty: true);
                }

                DHCPManager.ClearLastSetIpError();
                InvalidateDeviceCache();
            }

            ox += 126f;
            if (ImguiButtonOnce(new Rect(ox, py, 100, 26), "Deselect", 52, _stMutedBtn))
            {
                _selectedServerInstanceIds.Clear();
                _selectedServer = null;
                _serverRangeAnchorInstanceId = -1;
            }

            py += 32f;
        }

        if (n == 1)
        {
            var s0 = SelectedServersScratch[0];
            GUI.Label(new Rect(px, py + 2, 72, 22), "Address", _stFormLabel);
            float ox = px + 78f;
            var oy = py;
            DrawOctetEditor(ref _oct0, ref ox, oy, 0);
            GUI.Label(new Rect(ox, oy + 2, 10, 22), ".", _stOctetVal);
            ox += 12f;
            DrawOctetEditor(ref _oct1, ref ox, oy, 1);
            GUI.Label(new Rect(ox, oy + 2, 10, 22), ".", _stOctetVal);
            ox += 12f;
            DrawOctetEditor(ref _oct2, ref ox, oy, 2);
            GUI.Label(new Rect(ox, oy + 2, 10, 22), ".", _stOctetVal);
            ox += 12f;
            DrawOctetEditor(ref _oct3, ref ox, oy, 3);

            py += 30f;
            ox = px + 78f;
            var btnY = py;
            if (ImguiButtonOnce(new Rect(ox, btnY, 88, 26), "Apply", 32, _stPrimaryBtn))
            {
                DHCPManager.SetServerIP(s0, BuildIpFromOctets());
            }

            ox += 96f;
            if (ImguiButtonOnce(new Rect(ox, btnY, 108, 26), "DHCP auto", 33, _stMutedBtn))
            {
                if (DHCPManager.AssignDhcpToSingleServer(s0))
                {
                    DHCPManager.ClearLastSetIpError();
                    LoadOctetsFromIp(DHCPManager.GetServerIP(s0));
                }
            }

            ox += 116f;
            if (ImguiButtonOnce(new Rect(ox, btnY, 96, 26), "Clipboard", 34, _stMutedBtn))
            {
                LoadOctetsFromIp(GUIUtility.systemCopyBuffer?.Trim());
            }

            ox += 104f;
            if (ImguiButtonOnce(new Rect(ox, btnY, 92, 26), "Clear", 35, _stMutedBtn))
            {
                if (DHCPManager.SetServerIP(s0, "0.0.0.0", suppressAutoAssignOnEmpty: true))
                {
                    LoadOctetsFromIp("0.0.0.0");
                    DHCPManager.ClearLastSetIpError();
                    InvalidateDeviceCache();
                }
            }

            py += 32f;
            GUI.Label(
                new Rect(px, py, w - px - 24, 28),
                "Must match usable addresses for this contract (see rack keypad). Do not assign the gateway IP as the host.",
                _stHint);
        }
        else
        {
            GUI.Label(
                new Rect(px, py, w - px - 24, 22),
                "DHCP all / Clear all apply to every highlighted server. Ctrl toggles; Shift+click selects a range from the last plain click.",
                _stHint);
        }

        var err = DHCPManager.LastSetIpError;
        if (!string.IsNullOrEmpty(err))
        {
            GUI.Label(new Rect(px, panelY + dph - 28, w - px - 24, 26), err, _stError);
        }
    }

    private static void DrawSwitchDetail()
    {
        var w = _windowRect.width;
        var h = _windowRect.height;
        var dph = GetDetailPanelHeight();
        var panelY = h - dph;
        GUI.DrawTexture(new Rect(0, panelY, w, 1f), _texTableHeader);

        var px = 16f;
        var py = panelY + 6f;
        var n = _selectedNetworkSwitchInstanceIds.Count;
        if (n > 1)
        {
            GUI.Label(new Rect(px, py, w - 32, 18), $"Edit object · {n} network devices", _stSectionTitle);
            py += 20f;
            var sb = new System.Text.StringBuilder();
            EnsureSortedSwitches();
            var added = 0;
            foreach (var sw in SortedSwitchesBuffer)
            {
                if (sw == null || !_selectedNetworkSwitchInstanceIds.Contains(sw.GetInstanceID()))
                {
                    continue;
                }

                if (added > 0)
                {
                    sb.Append(", ");
                }

                sb.Append(Trunc(DeviceInventoryReflection.GetDisplayName(sw), 22));
                added++;
                if (added >= 6)
                {
                    sb.Append(" …");
                    break;
                }
            }

            GUI.Label(new Rect(px, py, w - 32, 34), $"Selected: {sb}", _stMuted);
            py += 38f;
            if (ImguiButtonOnce(new Rect(px, py, 120, 26), "Deselect all", 42, _stMutedBtn))
            {
                ClearSwitchSelection();
            }

            py += 32f;
            GUI.Label(
                new Rect(px, py, w - px - 24, 36),
                "Ctrl toggles selection; Shift+click selects a range in the switch list from the last plain click.",
                _stHint);
            return;
        }

        var swOne = _selectedNetworkSwitch;
        var role = "Switch";

        GUI.Label(new Rect(px, py, w - 32, 18), "Edit object · Network device", _stSectionTitle);
        py += 20f;
        GUI.Label(
            new Rect(px, py, w - 32, 16),
            $"Name   {Trunc(swOne != null ? DeviceInventoryReflection.GetDisplayName(swOne) : "", 72)}    │    Role   {role}",
            _stMuted);
        py += 20f;

        var ox = px;
        if (ImguiButtonOnce(new Rect(ox, py, 96, 26), "Deselect", 41, _stMutedBtn))
        {
            ClearSwitchSelection();
        }

        py += 30f;
    }

    private static void DrawOctetEditor(ref int oct, ref float x, float y, int octetSlot)
    {
        oct = Mathf.Clamp(oct, 0, 255);
        const int hintBase = 0x2E435000;
        var minusHint = hintBase + octetSlot * 4;
        var plusHint = minusHint + 1;

        if (OctetStepButton(new Rect(x, y, 26, 26), "−", minusHint))
        {
            oct = Mathf.Max(0, oct - 1);
        }

        x += 28f;
        var labelRect = new Rect(x, y + 2, 36, 22);
        if (_activeOctetSlot == octetSlot)
        {
            GUI.Box(labelRect, GUIContent.none);
        }

        GUI.Label(labelRect, oct.ToString(), _stOctetVal);
        if (Event.current.type == EventType.MouseDown
            && Event.current.button == 0
            && labelRect.Contains(Event.current.mousePosition))
        {
            _activeOctetSlot = octetSlot;
            Event.current.Use();
        }

        if (_activeOctetSlot == octetSlot && TryHandleOctetKeyboardEvent(Event.current))
        {
            Event.current.Use();
        }

        x += 40f;
        if (OctetStepButton(new Rect(x, y, 26, 26), "+", plusHint))
        {
            oct = Mathf.Min(255, oct + 1);
        }

        x += 30f;
    }

    private static void LoadOctetsFromIp(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
        {
            _oct0 = 192;
            _oct1 = 168;
            _oct2 = 1;
            _oct3 = 10;
            return;
        }

        var parts = ip.Trim().Split('.');
        if (parts.Length != 4)
        {
            return;
        }

        if (int.TryParse(parts[0], out var a))
        {
            _oct0 = Mathf.Clamp(a, 0, 255);
        }

        if (int.TryParse(parts[1], out var b))
        {
            _oct1 = Mathf.Clamp(b, 0, 255);
        }

        if (int.TryParse(parts[2], out var c))
        {
            _oct2 = Mathf.Clamp(c, 0, 255);
        }

        if (int.TryParse(parts[3], out var d))
        {
            _oct3 = Mathf.Clamp(d, 0, 255);
        }
    }

    private static string BuildIpFromOctets()
    {
        return $"{Mathf.Clamp(_oct0, 0, 255)}.{Mathf.Clamp(_oct1, 0, 255)}.{Mathf.Clamp(_oct2, 0, 255)}.{Mathf.Clamp(_oct3, 0, 255)}";
    }

    private static int GetOctetValue(int slot)
    {
        return slot switch
        {
            0 => _oct0,
            1 => _oct1,
            2 => _oct2,
            3 => _oct3,
            _ => 0,
        };
    }

    private static void SetOctetValue(int slot, int value)
    {
        value = Mathf.Clamp(value, 0, 255);
        switch (slot)
        {
            case 0: _oct0 = value; break;
            case 1: _oct1 = value; break;
            case 2: _oct2 = value; break;
            case 3: _oct3 = value; break;
        }
    }

    private static void BackspaceActiveOctet()
    {
        if (_activeOctetSlot < 0)
        {
            return;
        }

        var current = GetOctetValue(_activeOctetSlot);
        SetOctetValue(_activeOctetSlot, current / 10);
    }

    private static void MoveActiveOctetFocusNext()
    {
        if (_activeOctetSlot < 0)
        {
            return;
        }

        _activeOctetSlot = Mathf.Min(3, _activeOctetSlot + 1);
    }

    private static void AppendDigitToActiveOctet(int digit)
    {
        if (_activeOctetSlot < 0)
        {
            return;
        }

        var current = GetOctetValue(_activeOctetSlot);
        var next = current * 10 + digit;
        if (next > 255)
        {
            return;
        }

        SetOctetValue(_activeOctetSlot, next);
    }

    private static bool TryHandleOctetKeyboardEvent(Event e)
    {
        if (_activeOctetSlot < 0 || e.type != EventType.KeyDown || Keyboard.current != null)
        {
            return false;
        }

        if (e.keyCode == KeyCode.Escape)
        {
            _activeOctetSlot = -1;
            return true;
        }

        if (e.keyCode == KeyCode.Backspace)
        {
            BackspaceActiveOctet();
            return true;
        }

        if (e.keyCode == KeyCode.Period || e.keyCode == KeyCode.Comma || e.character == '.' || e.character == ',')
        {
            MoveActiveOctetFocusNext();
            return true;
        }

        if (e.character >= '0' && e.character <= '9')
        {
            AppendDigitToActiveOctet(e.character - '0');
            return true;
        }

        return false;
    }
}
