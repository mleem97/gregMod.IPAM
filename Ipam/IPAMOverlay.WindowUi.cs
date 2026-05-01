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

        var scale = Mathf.Clamp(UiFontScale, 0.5f, 2.0f);
        float Sp(float px) => Mathf.Round(px * scale);

        GUI.DrawTexture(new Rect(0, 0, w, TitleBarH), _texSidebar);
        GUI.Label(new Rect(12, Sp(6), w - 280, Mathf.Max(18f, Sp(22))), "IPAM  ·  Data Center", _stWindowTitle);
        var maxLabel = _windowMaximized ? "Restore" : "Maximize";
        var topBtnH = Mathf.Max(18f, Sp(22));
        var topBtnWMax = Mathf.Max(82f, TW(_stMutedBtn, "Maximize"));
        var topBtnWClose = Mathf.Max(78f, TW(_stMutedBtn, "Close"));
        var topRightPad = Mathf.Max(10f, Sp(10f));
        var topY = Sp(4);
        var closeX = w - topRightPad - topBtnWClose;
        var maxX = closeX - Sp(8f) - topBtnWMax;
        if (GUI.Button(new Rect(maxX, topY, topBtnWMax, topBtnH), maxLabel, _stMutedBtn))
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

        if (GUI.Button(new Rect(closeX, topY, topBtnWClose, topBtnH), "Close", _stMutedBtn))
        {
            IsVisible = false;
        }

        var licBtnH = Mathf.Max(18f, Sp(22));
        var licY = Mathf.Max(topY + topBtnH + Sp(6f), TitleBarH - licBtnH - Sp(4));
        var ipamLabel = ipamUnlocked ? "IPAM: ON" : "IPAM: locked";
        var dhcpLabel = dhcpUnlocked ? "DHCP: ON" : "DHCP: locked";
        var licBtnW = Mathf.Max(84f, Mathf.Max(TW(_stMutedBtn, "IPAM: locked"), TW(_stPrimaryBtn, "DHCP: locked")));
        var licIpamX = w - topRightPad - licBtnW;
        if (ImguiButtonOnce(
                new Rect(licIpamX, licY, licBtnW, licBtnH),
                new GUIContent(
                    ipamLabel,
                    "Toggle IPAM (inventory tables, IP editor, navigation). Ctrl+D toggles DHCP+IPAM together."),
                8801,
                ipamUnlocked ? _stPrimaryBtn : _stMutedBtn))
        {
            LicenseManager.ToggleIpamUnlock();
        }

        var licDhcpX = licIpamX - licBtnW - Sp(8f);
        if (ImguiButtonOnce(
                new Rect(licDhcpX, licY, licBtnW, licBtnH),
                new GUIContent(
                    dhcpLabel,
                    "Toggle DHCP (per-server DHCP, detail panel). Ctrl+D toggles DHCP+IPAM together."),
                8802,
                dhcpUnlocked ? _stPrimaryBtn : _stMutedBtn))
        {
            LicenseManager.ToggleDhcpUnlock();
        }

        var toolbarY = TitleBarH;
        GUI.DrawTexture(new Rect(0, toolbarY, w, ToolbarH), _texToolbar);
        var toolbarTitleH = Mathf.Max(18f, Sp(22));
        var toolbarSubH = Mathf.Max(14f, Sp(16));
        GUI.Label(new Rect(16, toolbarY + Sp(6), w - 32f, toolbarTitleH), "Inventory", _stToolbarTitle);
        GUI.Label(new Rect(16, toolbarY + Sp(6) + toolbarTitleH, w - 32f, toolbarSubH), "Live devices · IPv4 assignments", _stToolbarSub);

        var btnRowY = toolbarY + ToolbarTitleBlockH;
        // Pack from the right on the second row only (keeps row 1 clear for the title).
        const float tr = 14f;
        var ty = Sp(4f);
        const float g = 8f;
        var btnH = Mathf.Max(20f, Sp(26f));
        float TW(GUIStyle st, string t) => ToolbarTextButtonWidth(st, t);
        var fitColsW = TW(_stMutedBtn, "Fit columns");
        var perfW = Mathf.Max(TW(_stMutedBtn, "Perf: off"), TW(_stMutedBtn, "Perf: on"));
        var iopsCalcW = TW(_stMutedBtn, "IOPS calc");
        var tx = w - tr;
        tx -= g + fitColsW;
        if (ImguiButtonOnce(new Rect(tx, btnRowY + ty, fitColsW, btnH), "Fit columns", 16, _stMutedBtn))
        {
            if (_navSection == NavSection.Ipam && _ipamSub == IpamSubSection.Prefixes && _lastInventoryCardWidth > 80f)
            {
                AutoFitIpamPrefixTableColumns(_lastInventoryCardWidth);
                RecomputeContentHeight();
            }
            else if (_navSection == NavSection.Customers
                     && _customersTabScreen == CustomersTabScreen.CustomerList
                     && _lastInventoryCardWidth > 80f)
            {
                AutoFitCustomersTabCustomerListColumns(_lastInventoryCardWidth);
            }
            else if (_lastInventoryCardWidth > 80f)
            {
                AutoFitInventoryTableColumns(_lastInventoryCardWidth);
            }
            else
            {
                _tableColumnsAutoFitPending = true;
            }
        }

        tx -= g + perfW;
        var perfLabel = ModDebugLog.IpamPerfRuntimeEnabled ? "Perf: on" : "Perf: off";
        if (ImguiButtonOnce(new Rect(tx, btnRowY + ty, perfW, btnH), perfLabel, 17, _stMutedBtn))
        {
            if (ModDebugLog.IpamPerfRuntimeEnabled)
            {
                ModDebugLog.WriteIpamPerf("Disabled from IPAM toolbar (runtime).");
                ModDebugLog.IpamPerfRuntimeEnabled = false;
                ShowIpamToast("Perf log off (no more lines written).");
            }
            else
            {
                ModDebugLog.IpamPerfRuntimeEnabled = true;
                ModDebugLog.WriteIpamPerf("Enabled from IPAM toolbar (runtime).");
                var perfPath = ModDebugLog.GetIpamPerfLogPath() ?? "(path unavailable)";
                ShowIpamToast($"Perf log on — see file next to _Data: DHCPSwitches-ipam-perf.log");
                ModLogging.Msg("[DHCPSwitches] IPAM perf log: " + perfPath);
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
        var navX = 8f;
        var navW = SidebarW - navX;
        var navHeaderY = bodyTop + Sp(10f);
        var navHeaderH = Mathf.Max(16f, _stNavHint != null ? _stNavHint.CalcHeight(new GUIContent("NAVIGATION"), SidebarW - 16f) : 16f);
        GUI.Label(new Rect(12, navHeaderY, SidebarW - 16, navHeaderH), "NAVIGATION", _stNavHint);
        var navRowH = Mathf.Max(28f, Sp(32f));
        var navStartY = navHeaderY + navHeaderH + Sp(6f);
        DrawNavEntry(new Rect(navX, navStartY + navRowH * 0, navW, navRowH), NavSection.Dashboard, "Dashboard");
        DrawNavEntry(new Rect(navX, navStartY + navRowH * 1, navW, navRowH), NavSection.Devices, "Devices");
        var ipamToggleRect = new Rect(navX, navStartY + navRowH * 2, navW, navRowH);
        var ipamChevron = _ipamSidebarExpanded ? "\u25BC" : "\u25B6";
        var ipamToggleLabel = $"IPAM  {ipamChevron}";
        var ipamNavActive = _navSection == NavSection.Ipam;
        if (ipamNavActive)
        {
            GUI.DrawTexture(ipamToggleRect, _texNavActive);
        }

        var ipamToggleStyle = ipamNavActive ? _stNavItemActive : _stNavBtn;
        if (ImguiButtonOnce(ipamToggleRect, ipamToggleLabel, 9048, ipamToggleStyle))
        {
            _ipamSidebarExpanded = !_ipamSidebarExpanded;
            _ipamFormFieldFocus = IpamFormFocusNone;
        }

        float navAfterIpam;
        if (_ipamSidebarExpanded)
        {
            var ipamSubIndent = Sp(10f);
            var subNavW = navW - ipamSubIndent;
            var ipamSubBaseY = navStartY + navRowH * 3;
            DrawIpamSubNav(new Rect(navX + ipamSubIndent, ipamSubBaseY + navRowH * 0, subNavW, navRowH), IpamSubSection.IpAddresses, "IP addresses", 9050);
            DrawIpamSubNav(new Rect(navX + ipamSubIndent, ipamSubBaseY + navRowH * 1, subNavW, navRowH), IpamSubSection.Prefixes, "Prefixes", 9051);
            DrawIpamSubNav(new Rect(navX + ipamSubIndent, ipamSubBaseY + navRowH * 2, subNavW, navRowH), IpamSubSection.Vlans, "VLANs", 9052);
            navAfterIpam = ipamSubBaseY + navRowH * 3 + Sp(8f);
        }
        else
        {
            navAfterIpam = navStartY + navRowH * 3 + Sp(8f);
        }
        DrawNavEntry(new Rect(navX, navAfterIpam, navW, navRowH), NavSection.Customers, "Customers");
        DrawNavEntry(new Rect(navX, navAfterIpam + navRowH, navW, navRowH), NavSection.Settings, "Settings");
        var tipY = navAfterIpam + navRowH * 2 + Sp(6f);
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

        // IOPS sizing / Add-server wizard are separate GUI.Window on top. Skip heavy inventory while open.
        if (_iopsCalculatorOpen || _customersTabAddServerWizardOpen)
        {
            var pauseTop = bodyTop + 8f;
            var pauseH = bodyH - 16f;
            GUI.DrawTexture(new Rect(contentX + 2, pauseTop + 2, contentW - 4, pauseH - 4), _texCard);
            GUI.Label(
                new Rect(contentX + CardPad, pauseTop + CardPad, contentW - CardPad * 2, 40f),
                "Organization  /  Inventory (paused)",
                _stBreadcrumb);
            var pauseMsg = _iopsCalculatorOpen
                ? "IOPS sizing is open.\n\nDevice tables are not redrawn while it is open (smoother typing). Close the sizing window (Esc, Close, or click outside) to use the list and detail panel again."
                : "Add server is open in a separate window. Close it (Esc, Close, or the window button) to return here.";
            GUI.Label(
                new Rect(contentX + CardPad, pauseTop + CardPad + 48f, contentW - CardPad * 2, 140f),
                pauseMsg,
                _stMuted);
            GUI.enabled = true;
            GUI.DragWindow(new Rect(0, 0, w, TitleBarH + ToolbarH));
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                Event.current.Use();
            }

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
        BeginInventoryScrollRowRepaintCull(_scroll.y, scrollH);
        try
        {
            switch (_navSection)
            {
                case NavSection.Dashboard:
                    DrawDashboard(innerW);
                    break;
                case NavSection.Devices:
                    DrawDeviceTables(innerW);
                    break;
                case NavSection.Ipam:
                    switch (_ipamSub)
                    {
                        case IpamSubSection.IpAddresses:
                            DrawIpAddressTable(innerW);
                            break;
                        case IpamSubSection.Prefixes:
                            DrawIpamPrefixesView(innerW);
                            break;
                        case IpamSubSection.Vlans:
                            DrawIpamVlansView(innerW);
                            break;
                    }

                    break;
                case NavSection.Customers:
                    DrawCustomersView(innerW);
                    break;
                case NavSection.Settings:
                    DrawSettingsView(innerW);
                    break;
            }
        }
        finally
        {
            EndInventoryScrollRowRepaintCull();
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

        if (!_iopsCalculatorOpen && !_customersTabAddServerWizardOpen)
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
            if (target == NavSection.Customers)
            {
                MarkCustomersTabServerBufferDirty();
            }

            if (target != NavSection.Customers)
            {
                _customersTabAddServerWizardOpen = false;
                _customersTabScreen = CustomersTabScreen.CustomerList;
            }

            _ipamFormFieldFocus = IpamFormFocusNone;
            if (target != NavSection.Ipam)
            {
                _ipamPrefixesDrillParentId = null;
                _ipamPrefixAddAsRoot = false;
                _ipamIpAddressFilterCidr = null;
                _ipamIpAddressPageIndex = 0;
                _ipamIpAddrPageMenuOpen = false;
                IpamIpAddressViewBuffer.Clear();
            }

            _navSection = target;
            _scroll = Vector2.zero;
            if (target != NavSection.Devices)
            {
                _ipamDevicesSwitchPageMenuOpen = false;
                _ipamDevicesServerPageMenuOpen = false;
            }

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
            case NavSection.Settings:
                _cachedContentHeight = 420f;
                return;
            case NavSection.Ipam:
                switch (_ipamSub)
                {
                    case IpamSubSection.IpAddresses:
                    {
                        EnsureSortedServers();
                        var filterExtra = string.IsNullOrWhiteSpace(_ipamIpAddressFilterCidr) ? 0f : 26f;
                        var n = GetIpamIpAddressViewRows().Count;
                        ClampIpamIpAddressPagingState(n);
                        var start = _ipamIpAddressPageIndex * _ipamIpAddressPageSize;
                        var bodyRows = n == 0 ? 1 : Mathf.Min(_ipamIpAddressPageSize, n - start);
                        const float paginationBarH = 28f;
                        var y = CardPad + SectionTitleH + 2f + 7f + SectionTitleH + 4f + filterExtra + TableHeaderH
                            + bodyRows * TableRowH + paginationBarH + CardPad;
                        _cachedContentHeight = Mathf.Max(220f, y);
                        return;
                    }
                    case IpamSubSection.Prefixes:
                        _cachedContentHeight = Mathf.Max(260f, ComputeIpamPrefixesContentHeight());
                        return;
                    case IpamSubSection.Vlans:
                        _cachedContentHeight = Mathf.Max(220f, ComputeIpamVlansContentHeight());
                        return;
                    default:
                        _cachedContentHeight = 260f;
                        return;
                }
            case NavSection.Customers:
            {
                if (_customersTabScreen == CustomersTabScreen.CustomerList)
                {
                    var rows = GetCustomersTabCustomerListRowCount();
                    _cachedContentHeight = ComputeCustomersTabCustomerListContentHeight(rows);
                }
                else
                {
                    var n = CountServersMatchingCustomersTabFilter();
                    _cachedContentHeight = ComputeCustomersTabCustomerServersContentHeight(n);
                }

                return;
            }
            default:
                break;
        }

        EnsureSortedSwitches();
        EnsureSortedServers();
        var swN = SortedSwitchesBuffer.Count;
        var svN = SortedServersBuffer.Count;
        ClampInventoryPageIndex(ref _ipamDevicesSwitchPageIndex, swN);
        ClampInventoryPageIndex(ref _ipamDevicesServerPageIndex, svN);
        var ps = _ipamIpAddressPageSize;
        var swStart = _ipamDevicesSwitchPageIndex * ps;
        var svStart = _ipamDevicesServerPageIndex * ps;
        var swBody = swN == 0 ? 1 : Mathf.Min(ps, swN - swStart);
        var svBody = svN == 0 ? 1 : Mathf.Min(ps, svN - svStart);
        const float devicesPaginationBarH = 28f;
        var yd = CardPad;
        yd += SectionTitleH + 2f + 7f;
        yd += SectionTitleH + 4f + TableHeaderH + swBody * TableRowH + devicesPaginationBarH;
        yd += 18f + SectionTitleH + 4f + TableHeaderH + svBody * TableRowH + devicesPaginationBarH;
        _cachedContentHeight = Mathf.Max(260f, yd + CardPad);
    }

    private static void DrawSettingsView(float innerW)
    {
        var x0 = CardPad;
        var y = CardPad;
        var cardW = innerW - CardPad * 2f;

        GUI.Label(new Rect(x0, y - 2, cardW, SectionTitleH), "Organization  /  Settings", _stBreadcrumb);
        y += SectionTitleH + 2f;
        GUI.DrawTexture(new Rect(x0, y, cardW, 1f), _texTableHeader);
        y += 10f;

        GUI.Label(new Rect(x0, y, cardW, SectionTitleH), "UI font scale", _stSectionTitle);
        y += SectionTitleH + 6f;

        var pct = Mathf.RoundToInt(UiFontScale * 100f);
        GUI.Label(new Rect(x0, y, 180f, 22f), $"Scale: {pct}%", _stMuted);

        var sliderW = Mathf.Max(220f, cardW - 260f);
        var sliderX = x0 + 180f;
        var sliderRect = new Rect(sliderX, y + 3f, sliderW, 18f);
        var newScale = GUI.HorizontalSlider(sliderRect, UiFontScale, 0.5f, 2.0f);
        if (Mathf.Abs(newScale - UiFontScale) > 0.0001f)
        {
            UiFontScale = newScale;
        }

        var resetRect = new Rect(sliderRect.xMax + 14f, y, 64f, 22f);
        if (GUI.Button(resetRect, "100%", _stMutedBtn))
        {
            UiFontScale = 1f;
        }

        y += 34f;
        GUI.Label(
            new Rect(x0, y, cardW, 72f),
            "Adjusts the IPAM overlay font sizes (live). Range is 50% to 200%.",
            _stHint);
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
        var tableW = cardW - IpamIpAddressGearColW;
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

        var headerRowSw = y;
        var gearRectSw = new Rect(x0 + tableW, headerRowSw, IpamIpAddressGearColW, TableHeaderH);
        var menuDropRectSw = new Rect(x0 + cardW - 132f, headerRowSw + TableHeaderH + 2f, 128f, 68f);
        var eCloseSw = Event.current;
        if (eCloseSw != null && eCloseSw.type == EventType.MouseDown && eCloseSw.button == 0 && _ipamDevicesSwitchPageMenuOpen)
        {
            if (!menuDropRectSw.Contains(eCloseSw.mousePosition) && !gearRectSw.Contains(eCloseSw.mousePosition))
            {
                _ipamDevicesSwitchPageMenuOpen = false;
            }
        }

        DrawSortableTableHeader(
            new Rect(x0, headerRowSw, tableW, TableHeaderH),
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
        if (ImguiButtonOnce(gearRectSw, "\u2699", 9201, _stMutedBtn))
        {
            _ipamDevicesSwitchPageMenuOpen = !_ipamDevicesSwitchPageMenuOpen;
            _ipamDevicesServerPageMenuOpen = false;
        }

        y += TableHeaderH;

        EnsureSortedSwitches();
        var totalSw = SortedSwitchesBuffer.Count;
        ClampInventoryPageIndex(ref _ipamDevicesSwitchPageIndex, totalSw);
        var ps = _ipamIpAddressPageSize;
        var swPageStart = _ipamDevicesSwitchPageIndex * ps;
        var swPageEnd = totalSw == 0 ? 0 : Mathf.Min(totalSw, swPageStart + ps);

        for (var pi = swPageStart; pi < swPageEnd; pi++)
        {
            var sw = SortedSwitchesBuffer[pi];
            var r = new Rect(x0, y, tableW, TableRowH);
            var menuBlocksRowPointer = _ipamDevicesSwitchPageMenuOpen && menuDropRectSw.Overlaps(r);
            string name;
            string role;
            string eolCol;
            string statusCol;
            if (ShouldComputeTruncatedInventoryCellText && InventoryScrollRowWantsRepaintText(r.yMin, r.yMax))
            {
                var nameRaw = sw != null ? DeviceInventoryReflection.GetDisplayName(sw) : "(removed)";
                name = CellTextForCol(0, string.IsNullOrEmpty(nameRaw) ? "—" : nameRaw, tableW);
                role = CellTextForCol(2, "Switch", tableW);
                eolCol = TableEolCellDisplay(sw, tableW);
                statusCol = CellTextForCol(5, "Active", tableW);
            }
            else
            {
                name = "";
                role = "";
                eolCol = "";
                statusCol = "";
            }

            if (TableDataRowClick(
                    r,
                    StableRowHint(1, sw, pi),
                    pi % 2 == 1,
                    IsSwitchRowSelected(sw),
                    name,
                    "—",
                    role,
                    "—",
                    eolCol,
                    statusCol,
                    tableW,
                    menuBlocksRowPointer))
            {
                HandleSwitchRowClick(sw, pi);
            }

            y += TableRowH;
        }

        if (totalSw == 0)
        {
            var stubR = new Rect(x0, y, tableW, TableRowH);
            var stubMenuBlock = _ipamDevicesSwitchPageMenuOpen && menuDropRectSw.Overlaps(stubR);
            TableDataRowClick(
                stubR,
                StableRowHint(1, null, 0),
                false,
                false,
                "—",
                "—",
                "—",
                "—",
                "—",
                "—",
                tableW,
                stubMenuBlock);
            y += TableRowH;
        }

        var pageCountSw = totalSw == 0 ? 1 : (totalSw + ps - 1) / ps;
        var swDispStart = totalSw == 0 ? 0 : swPageStart + 1;
        var swDispEnd = totalSw == 0 ? 0 : swPageEnd;
        var labelSw = totalSw == 0
            ? "No network switches"
            : $"Page {_ipamDevicesSwitchPageIndex + 1} / {pageCountSw}   ·   {swDispStart}-{swDispEnd} of {totalSw}";
        GUI.Label(new Rect(x0, y + 2f, tableW - 200f, 22f), labelSw, _stHint);
        var navYSw = y + 1f;
        if (ImguiButtonOnce(new Rect(x0 + tableW - 168f, navYSw, 72f, 22f), "Previous", 9202, _stMutedBtn))
        {
            if (_ipamDevicesSwitchPageIndex > 0)
            {
                _ipamDevicesSwitchPageIndex--;
                RecomputeContentHeight();
            }
        }

        if (ImguiButtonOnce(new Rect(x0 + tableW - 90f, navYSw, 82f, 22f), "Next", 9203, _stMutedBtn))
        {
            if (_ipamDevicesSwitchPageIndex < pageCountSw - 1)
            {
                _ipamDevicesSwitchPageIndex++;
                RecomputeContentHeight();
            }
        }

        y += 28f;

        DrawInventoryPageSizePopup(menuDropRectSw, ref _ipamDevicesSwitchPageMenuOpen, 9204, 9205, 9206);

        y += 18f;

        // --- Servers card ---
        GUI.Label(new Rect(x0, y, 200, SectionTitleH), "Servers", _stSectionTitle);
        y += SectionTitleH + 4f;

        var headerRowSv = y;
        var gearRectSv = new Rect(x0 + tableW, headerRowSv, IpamIpAddressGearColW, TableHeaderH);
        var menuDropRectSv = new Rect(x0 + cardW - 132f, headerRowSv + TableHeaderH + 2f, 128f, 68f);
        var eCloseSv = Event.current;
        if (eCloseSv != null && eCloseSv.type == EventType.MouseDown && eCloseSv.button == 0 && _ipamDevicesServerPageMenuOpen)
        {
            if (!menuDropRectSv.Contains(eCloseSv.mousePosition) && !gearRectSv.Contains(eCloseSv.mousePosition))
            {
                _ipamDevicesServerPageMenuOpen = false;
            }
        }

        DrawSortableTableHeader(
            new Rect(x0, headerRowSv, tableW, TableHeaderH),
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
        if (ImguiButtonOnce(gearRectSv, "\u2699", 9211, _stMutedBtn))
        {
            _ipamDevicesServerPageMenuOpen = !_ipamDevicesServerPageMenuOpen;
            _ipamDevicesSwitchPageMenuOpen = false;
        }

        y += TableHeaderH;

        EnsureSortedServers();
        var totalSv = SortedServersBuffer.Count;
        ClampInventoryPageIndex(ref _ipamDevicesServerPageIndex, totalSv);
        var svPageStart = _ipamDevicesServerPageIndex * ps;
        var svPageEnd = totalSv == 0 ? 0 : Mathf.Min(totalSv, svPageStart + ps);

        for (var pi = svPageStart; pi < svPageEnd; pi++)
        {
            var server = SortedServersBuffer[pi];
            var r = new Rect(x0, y, tableW, TableRowH);
            var menuBlocksRowPointerSv = _ipamDevicesServerPageMenuOpen && menuDropRectSv.Overlaps(r);

            if (server == null)
            {
                TableDataRowClick(
                    r,
                    StableRowHint(2, null, pi),
                    pi % 2 == 1,
                    false,
                    "(removed)",
                    "—",
                    "—",
                    "—",
                    "—",
                    "—",
                    tableW,
                    menuBlocksRowPointerSv);
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
                ipCol = CellTextForCol(3, ipRaw, tableW);
                status = CellTextForCol(5, hasIp ? "Assigned" : "No address", tableW);
                cust = CellTextForCol(1, GetCustomerDisplayName(server), tableW);
                eolCol = TableEolCellDisplay(server, tableW);
                var dispRaw = DeviceInventoryReflection.GetDisplayName(server);
                dispName = CellTextForCol(0, string.IsNullOrEmpty(dispRaw) ? "—" : dispRaw, tableW);
                typeCol = CellTextForCol(2, DeviceInventoryReflection.GetServerFormFactorLabel(server), tableW);
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
                    StableRowHint(2, server, pi),
                    pi % 2 == 1,
                    IsServerRowSelected(server),
                    dispName,
                    cust,
                    typeCol,
                    ipCol,
                    eolCol,
                    status,
                    tableW,
                    menuBlocksRowPointerSv))
            {
                HandleServerRowClick(server, pi, ip, SortedServersBuffer);
            }

            y += TableRowH;
        }

        if (totalSv == 0)
        {
            var stubR = new Rect(x0, y, tableW, TableRowH);
            var stubMenuBlockSv = _ipamDevicesServerPageMenuOpen && menuDropRectSv.Overlaps(stubR);
            TableDataRowClick(
                stubR,
                StableRowHint(2, null, 0),
                false,
                false,
                "—",
                "—",
                "—",
                "—",
                "—",
                "—",
                tableW,
                stubMenuBlockSv);
            y += TableRowH;
        }

        var pageCountSv = totalSv == 0 ? 1 : (totalSv + ps - 1) / ps;
        var svDispStart = totalSv == 0 ? 0 : svPageStart + 1;
        var svDispEnd = totalSv == 0 ? 0 : svPageEnd;
        var labelSv = totalSv == 0
            ? "No servers"
            : $"Page {_ipamDevicesServerPageIndex + 1} / {pageCountSv}   ·   {svDispStart}-{svDispEnd} of {totalSv}";
        GUI.Label(new Rect(x0, y + 2f, tableW - 200f, 22f), labelSv, _stHint);
        var navYSv = y + 1f;
        if (ImguiButtonOnce(new Rect(x0 + tableW - 168f, navYSv, 72f, 22f), "Previous", 9212, _stMutedBtn))
        {
            if (_ipamDevicesServerPageIndex > 0)
            {
                _ipamDevicesServerPageIndex--;
                RecomputeContentHeight();
            }
        }

        if (ImguiButtonOnce(new Rect(x0 + tableW - 90f, navYSv, 82f, 22f), "Next", 9213, _stMutedBtn))
        {
            if (_ipamDevicesServerPageIndex < pageCountSv - 1)
            {
                _ipamDevicesServerPageIndex++;
                RecomputeContentHeight();
            }
        }

        y += 28f;

        DrawInventoryPageSizePopup(menuDropRectSv, ref _ipamDevicesServerPageMenuOpen, 9214, 9215, 9216);
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
        foreach (var cb in GameSubnetHelper.GetSceneCustomersForFrame())
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
            if (string.Equals(lab, "7 U", StringComparison.Ordinal)
                || string.Equals(lab, "4 U", StringComparison.Ordinal))
            {
                n4u++;
                ratedIopsSum += IopsPer4UServer;
            }
            else if (string.Equals(lab, "3 U", StringComparison.Ordinal)
                     || string.Equals(lab, "2 U", StringComparison.Ordinal))
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
        var heroH = Mathf.Max(72f, Mathf.Round(92f * UiFontScale));
        const float sectionGap = 18f;
        var barH = Mathf.Max(22f, Mathf.Round(28f * UiFontScale));
        var legendBlockH = Mathf.Max(58f, Mathf.Round(72f * UiFontScale));
        var sceneCardH = Mathf.Max(58f, Mathf.Round(68f * UiFontScale));
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

        var padX = Mathf.Max(10f, Mathf.Round(14f * UiFontScale));
        var innerW = Mathf.Max(40f, r.width - padX - 10f);
        var tx = r.x + padX;
        var ty = r.y + Mathf.Max(6f, Mathf.Round(10f * UiFontScale));
        var titleH = Mathf.Max(14f, Mathf.Round(16f * UiFontScale));
        GUI.Label(new Rect(tx, ty, innerW, titleH), title, _stMuted);
        ty += titleH + Mathf.Max(2f, Mathf.Round(2f * UiFontScale));
        var valSt = _stDashboardHeroValue ?? _stIopsResultCounts;
        var valH = Mathf.Max(30f, Mathf.Round(36f * UiFontScale));
        GUI.Label(new Rect(tx, ty, innerW, valH), value, valSt);
        ty += valH + Mathf.Max(2f, Mathf.Round(2f * UiFontScale));
        GUI.Label(new Rect(tx, ty, innerW, Mathf.Max(18f, Mathf.Round(22f * UiFontScale))), subtitle, _stMuted);
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

        var pad = Mathf.Max(8f, Mathf.Round(12f * UiFontScale));
        var innerW = Mathf.Max(40f, r.width - pad * 2f);
        var tx = r.x + pad;
        var ty = r.y + Mathf.Max(5f, Mathf.Round(8f * UiFontScale));
        var titleH = Mathf.Max(14f, Mathf.Round(16f * UiFontScale));
        GUI.Label(new Rect(tx, ty, innerW, titleH), title, _stMuted);
        ty += titleH + Mathf.Max(2f, Mathf.Round(2f * UiFontScale));
        var valSt = _stIopsResultCounts ?? _stTableCell;
        var countH = Mathf.Max(24f, Mathf.Round(30f * UiFontScale));
        GUI.Label(new Rect(tx, ty, innerW, countH), count.ToString("N0"), valSt);
        ty += countH + Mathf.Max(2f, Mathf.Round(2f * UiFontScale));
        var track = new Rect(tx, ty, innerW, Mathf.Max(6f, Mathf.Round(8f * UiFontScale)));
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
        var heroH = Mathf.Max(72f, Mathf.Round(92f * UiFontScale));
        const float heroGap = 12f;
        const float sectionGap = 18f;
        var barH = Mathf.Max(22f, Mathf.Round(28f * UiFontScale));
        var legendRowH = Mathf.Max(18f, Mathf.Round(22f * UiFontScale));
        var sceneCardH = Mathf.Max(58f, Mathf.Round(68f * UiFontScale));

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
            "Rated IOPS (3 U + 7 U)",
            ratedIopsSum.ToString("N0"),
            $"{n4u}×{IopsPer4UServer:N0} + {n2u}×{IopsPer2UServer:N0} (7 U + 3 U tiers)");
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
            $"7 U servers  ·  {n4u}  ({P(n4u):0.#}%)  — {IopsPer4UServer:N0} IOPS each");
        y += legendRowH;
        DrawDashboardLegendLine(
            new Rect(x0, y, w, legendRowH),
            DashboardColor2U,
            $"3 U servers  ·  {n2u}  ({P(n2u):0.#}%)  — {IopsPer2UServer:N0} IOPS each");
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

    private static void NormalizeInventoryPageSize()
    {
        if (_ipamIpAddressPageSize != 25 && _ipamIpAddressPageSize != 50 && _ipamIpAddressPageSize != 100)
        {
            _ipamIpAddressPageSize = 25;
        }
    }

    private static void ClampInventoryPageIndex(ref int pageIndex, int totalCount)
    {
        NormalizeInventoryPageSize();
        var ps = _ipamIpAddressPageSize;
        if (totalCount <= 0)
        {
            pageIndex = 0;
            return;
        }

        var maxPage = (totalCount - 1) / ps;
        if (pageIndex > maxPage)
        {
            pageIndex = maxPage;
        }

        if (pageIndex < 0)
        {
            pageIndex = 0;
        }
    }

    private static void ClampIpamIpAddressPagingState(int totalCount)
    {
        ClampInventoryPageIndex(ref _ipamIpAddressPageIndex, totalCount);
    }

    /// <summary>Shared 25/50/100 popup for IP addresses and Devices tables.</summary>
    private static void DrawInventoryPageSizePopup(Rect menuDropRect, ref bool menuOpen, int id25, int id50, int id100)
    {
        if (!menuOpen)
        {
            return;
        }

        if (Event.current.type == EventType.Repaint)
        {
            DrawTintedRect(menuDropRect, new Color(0.08f, 0.1f, 0.12f, 0.96f));
        }

        var optY = menuDropRect.y + 4f;
        if (ImguiButtonOnce(new Rect(menuDropRect.x + 4f, optY, menuDropRect.width - 8f, 20f), "25 per page", id25, _stMutedBtn))
        {
            _ipamIpAddressPageSize = 25;
            _ipamIpAddressPageIndex = 0;
            _ipamDevicesSwitchPageIndex = 0;
            _ipamDevicesServerPageIndex = 0;
            menuOpen = false;
            RecomputeContentHeight();
        }

        optY += 22f;
        if (ImguiButtonOnce(new Rect(menuDropRect.x + 4f, optY, menuDropRect.width - 8f, 20f), "50 per page", id50, _stMutedBtn))
        {
            _ipamIpAddressPageSize = 50;
            _ipamIpAddressPageIndex = 0;
            _ipamDevicesSwitchPageIndex = 0;
            _ipamDevicesServerPageIndex = 0;
            menuOpen = false;
            RecomputeContentHeight();
        }

        optY += 22f;
        if (ImguiButtonOnce(new Rect(menuDropRect.x + 4f, optY, menuDropRect.width - 8f, 20f), "100 per page", id100, _stMutedBtn))
        {
            _ipamIpAddressPageSize = 100;
            _ipamIpAddressPageIndex = 0;
            _ipamDevicesSwitchPageIndex = 0;
            _ipamDevicesServerPageIndex = 0;
            menuOpen = false;
            RecomputeContentHeight();
        }
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

        GUI.Label(new Rect(x0, y - 2, cardW, SectionTitleH), "Organization  /  IPAM  /  IP addresses", _stBreadcrumb);
        y += SectionTitleH + 2f;
        GUI.DrawTexture(new Rect(x0, y, cardW, 1f), _texTableHeader);
        y += 6f;

        GUI.Label(new Rect(x0, y, 220, SectionTitleH), "IPv4 assignments", _stSectionTitle);
        y += SectionTitleH + 4f;

        if (!string.IsNullOrWhiteSpace(_ipamIpAddressFilterCidr))
        {
            GUI.Label(new Rect(x0, y, cardW - 100f, 22f), $"Filtered to prefix: {_ipamIpAddressFilterCidr}", _stHint);
            if (ImguiButtonOnce(new Rect(x0 + cardW - 92f, y, 86f, 22f), "Clear filter", 9112, _stMutedBtn))
            {
                _ipamIpAddressFilterCidr = null;
                IpamIpAddressViewBuffer.Clear();
                _ipamIpAddressPageIndex = 0;
                RecomputeContentHeight();
            }

            y += 26f;
        }

        var tableW = cardW - IpamIpAddressGearColW;
        var headerRowY = y;
        var gearRect = new Rect(x0 + tableW, headerRowY, IpamIpAddressGearColW, TableHeaderH);
        var menuDropRect = new Rect(x0 + cardW - 132f, headerRowY + TableHeaderH + 2f, 128f, 68f);
        var eClose = Event.current;
        if (eClose != null && eClose.type == EventType.MouseDown && eClose.button == 0 && _ipamIpAddrPageMenuOpen)
        {
            if (!menuDropRect.Contains(eClose.mousePosition) && !gearRect.Contains(eClose.mousePosition))
            {
                _ipamIpAddrPageMenuOpen = false;
            }
        }

        DrawSortableTableHeader(
            new Rect(x0, headerRowY, tableW, TableHeaderH),
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
        if (ImguiButtonOnce(gearRect, "\u2699", 9115, _stMutedBtn))
        {
            _ipamIpAddrPageMenuOpen = !_ipamIpAddrPageMenuOpen;
            _ipamDevicesSwitchPageMenuOpen = false;
            _ipamDevicesServerPageMenuOpen = false;
        }

        y += TableHeaderH;

        EnsureSortedServers();
        var ipViewRows = GetIpamIpAddressViewRows();
        var totalRows = ipViewRows.Count;
        ClampIpamIpAddressPagingState(totalRows);
        var pageStart = _ipamIpAddressPageIndex * _ipamIpAddressPageSize;
        var pageEnd = totalRows == 0 ? 0 : Mathf.Min(totalRows, pageStart + _ipamIpAddressPageSize);

        for (var pageI = pageStart; pageI < pageEnd; pageI++)
        {
            var i = pageI;
            var server = ipViewRows[i];
            var r = new Rect(x0, y, tableW, TableRowH);
            var menuBlocksRowPointer = _ipamIpAddrPageMenuOpen && menuDropRect.Overlaps(r);
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
                    tableW,
                    menuBlocksRowPointer);
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
                ipCol = CellTextForCol(3, ipRaw, tableW);
                status = CellTextForCol(5, hasIp ? "Assigned" : "No address", tableW);
                cust = CellTextForCol(1, GetCustomerDisplayName(server), tableW);
                eolCol = TableEolCellDisplay(server, tableW);
                var dispRaw = DeviceInventoryReflection.GetDisplayName(server);
                dispName = CellTextForCol(0, string.IsNullOrEmpty(dispRaw) ? "—" : dispRaw, tableW);
                typeCol = CellTextForCol(2, DeviceInventoryReflection.GetServerFormFactorLabel(server), tableW);
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
                    StableRowHint(4, server, i),
                    i % 2 == 1,
                    IsServerRowSelected(server),
                    dispName,
                    cust,
                    typeCol,
                    ipCol,
                    eolCol,
                    status,
                    tableW,
                    menuBlocksRowPointer))
            {
                HandleServerRowClick(server, i, ip, ipViewRows);
            }

            y += TableRowH;
        }

        if (totalRows == 0)
        {
            var stubR = new Rect(x0, y, tableW, TableRowH);
            var stubMenuBlock = _ipamIpAddrPageMenuOpen && menuDropRect.Overlaps(stubR);
            TableDataRowClick(
                stubR,
                StableRowHint(4, null, 0),
                false,
                false,
                "—",
                "—",
                "—",
                "—",
                "—",
                "—",
                tableW,
                stubMenuBlock);
            y += TableRowH;
        }

        var pageCount = totalRows == 0 ? 1 : (totalRows + _ipamIpAddressPageSize - 1) / _ipamIpAddressPageSize;
        var label = totalRows == 0
            ? "No servers"
            : $"Page {_ipamIpAddressPageIndex + 1} / {pageCount}   ·   {pageStart + 1}-{pageEnd} of {totalRows}";
        GUI.Label(new Rect(x0, y + 2f, tableW - 200f, 22f), label, _stHint);
        var navY = y + 1f;
        if (ImguiButtonOnce(new Rect(x0 + tableW - 168f, navY, 72f, 22f), "Previous", 9116, _stMutedBtn))
        {
            if (_ipamIpAddressPageIndex > 0)
            {
                _ipamIpAddressPageIndex--;
                RecomputeContentHeight();
            }
        }

        if (ImguiButtonOnce(new Rect(x0 + tableW - 90f, navY, 82f, 22f), "Next", 9117, _stMutedBtn))
        {
            if (_ipamIpAddressPageIndex < pageCount - 1)
            {
                _ipamIpAddressPageIndex++;
                RecomputeContentHeight();
            }
        }

        y += 28f;

        DrawInventoryPageSizePopup(menuDropRect, ref _ipamIpAddrPageMenuOpen, 9118, 9119, 9120);
    }

    private static List<Server> GetIpamIpAddressViewRows()
    {
        EnsureSortedServers();
        if (string.IsNullOrWhiteSpace(_ipamIpAddressFilterCidr))
        {
            return SortedServersBuffer;
        }

        IpamIpAddressViewBuffer.Clear();
        foreach (var s in SortedServersBuffer)
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

            if (RouteMath.IsIpv4InCidr(ip.Trim(), _ipamIpAddressFilterCidr))
            {
                IpamIpAddressViewBuffer.Add(s);
            }
        }

        return IpamIpAddressViewBuffer;
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
        foreach (var cb in GameSubnetHelper.GetSceneCustomersForFrame())
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
            _ipamFormFieldFocus = IpamFormFocusNone;
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
