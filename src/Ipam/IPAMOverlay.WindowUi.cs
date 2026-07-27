using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GregModIPAM;

// Main IPAM window: title/toolbar, navigation, scroll content; switches keep a bottom strip; servers use popup 9005 only.

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

        // Servers: no bottom chrome — editing is only in standalone window 9005.
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

        if (!Enum.IsDefined(typeof(DevicesSubSection), _devicesSub))
        {
            _devicesSub = DevicesSubSection.Switches;
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
        if (ImguiButtonOnce(new Rect(maxX, topY, topBtnWMax, topBtnH), maxLabel, 8800, _stMutedBtn))
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

        if (ImguiButtonOnce(new Rect(closeX, topY, topBtnWClose, topBtnH), "Close", 8799, _stMutedBtn))
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
                    "Toggle IPAM (inventory tables, IP editor, navigation)."),
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
                    "Toggle DHCP (per-server DHCP, detail panel)."),
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
        if ((dhcpUnlocked || ipamUnlocked) && _selectedServerInstanceIds.Count > 0)
        {
            var editSrvW = TW(_stPrimaryBtn, "Edit servers");
            tx -= g + editSrvW;
            if (ImguiButtonOnce(new Rect(tx, btnRowY + ty, editSrvW, btnH), "Edit servers", 8815, _stPrimaryBtn))
            {
                OpenServerEditPopupForSelection();
            }
        }

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
            else if (_lastInventoryTableWidth > 80f)
            {
                AutoFitInventoryTableColumns(_lastInventoryTableWidth);
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
                ShowIpamToast($"Perf log on — see file next to _Data: gregModIPAM-ipam-perf.log");
                ModLogging.Msg("[gregMod.IPAM] IPAM perf log: " + perfPath);
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
        NavIcons.EnsureInit();
        var navX = 8f;
        var navW = SidebarW - navX;
        var navRowH = Mathf.Max(28f, Sp(32f));
        var navStartY = bodyTop + Sp(10f);
        var iconSize = 18f;
        var iconPad = 6f;

        // ── Dashboard ──
        DrawNavEntryWithIcon(new Rect(navX, navStartY, navW, navRowH), NavSection.Dashboard, "Overview", "dashboard", iconSize, iconPad);

        // ── Customers ──
        DrawNavEntryWithIcon(new Rect(navX, navStartY + navRowH, navW, navRowH), NavSection.Customers, "Customers", "customers", iconSize, iconPad);

        // ── Racks ──
        DrawNavEntryWithIcon(new Rect(navX, navStartY + navRowH * 2, navW, navRowH), NavSection.Racks, "Racks", "rack", iconSize, iconPad);

        // ── Servers & Devices (category header) ──
        var assetsY = navStartY + navRowH * 3;
        var assetsChevronTex = NavIcons.Get(_devicesSidebarExpanded ? "chevron_d" : "chevron_r");
        DrawCategoryHeader(new Rect(navX, assetsY, navW, navRowH), "SERVERS & DEVICES", assetsChevronTex, iconSize, iconPad,
            _navSection == NavSection.Devices, ref _devicesSidebarExpanded);

        float navAfterAssets;
        if (_devicesSidebarExpanded)
        {
            var subIndent = Sp(14f);
            var subNavW = navW - subIndent;
            var subBaseY = assetsY + navRowH;
            DrawSubNavWithIcon(new Rect(navX + subIndent, subBaseY, subNavW, navRowH), DevicesSubSection.Switches, "Switches", "switch", 9060, iconSize, iconPad);
            DrawSubNavWithIcon(new Rect(navX + subIndent, subBaseY + navRowH, subNavW, navRowH), DevicesSubSection.Routers, "Routers", "router", 9061, iconSize, iconPad);
            DrawSubNavWithIcon(new Rect(navX + subIndent, subBaseY + navRowH * 2, subNavW, navRowH), DevicesSubSection.Firewall, "Firewall", "firewall", 9062, iconSize, iconPad);
            DrawSubNavWithIcon(new Rect(navX + subIndent, subBaseY + navRowH * 3, subNavW, navRowH), DevicesSubSection.Servers, "Servers", "server", 9063, iconSize, iconPad);
            navAfterAssets = subBaseY + navRowH * 4;
        }
        else
        {
            navAfterAssets = assetsY + navRowH;
        }

        // ── Network (category header) ──
        var networkChevronTex = NavIcons.Get(_ipamSidebarExpanded ? "chevron_d" : "chevron_r");
        DrawCategoryHeader(new Rect(navX, navAfterAssets, navW, navRowH), "NETWORK", networkChevronTex, iconSize, iconPad,
            _navSection == NavSection.Ipam, ref _ipamSidebarExpanded);

        float navAfterNetwork;
        if (_ipamSidebarExpanded)
        {
            var subIndent = Sp(14f);
            var subNavW = navW - subIndent;
            var subBaseY = navAfterAssets + navRowH;
            DrawIpamSubNavWithIcon(new Rect(navX + subIndent, subBaseY, subNavW, navRowH), IpamSubSection.IpAddresses, "IP addresses", "ip", 9050, iconSize, iconPad);
            DrawIpamSubNavWithIcon(new Rect(navX + subIndent, subBaseY + navRowH, subNavW, navRowH), IpamSubSection.Prefixes, "Prefixes", "prefix", 9051, iconSize, iconPad);
            DrawIpamSubNavWithIcon(new Rect(navX + subIndent, subBaseY + navRowH * 2, subNavW, navRowH), IpamSubSection.Vlans, "VLANs", "vlan", 9052, iconSize, iconPad);
            DrawIpamSubNavWithIcon(new Rect(navX + subIndent, subBaseY + navRowH * 3, subNavW, navRowH), IpamSubSection.DhcpScopes, "DHCP Scopes", "dhcp", 9053, iconSize, iconPad);
            navAfterNetwork = subBaseY + navRowH * 4;
        }
        else
        {
            navAfterNetwork = navAfterAssets + navRowH;
        }

        // ── Help ──
        DrawIpamSubNavWithIcon(new Rect(navX, navAfterNetwork, navW, navRowH), IpamSubSection.Tutorial, "Help", "tutorial", 9054, iconSize, iconPad);

        // ── Settings ──
        DrawNavEntryWithIcon(new Rect(navX, navAfterNetwork + navRowH, navW, navRowH), NavSection.Settings, "Settings", "settings", iconSize, iconPad);
        var tipY = navAfterNetwork + navRowH * 2 + Sp(6f);
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
            GUI.Label(new Rect(contentX + CardPad, bodyTop + 24, contentW - CardPad * 2, 40), "Organization / Servers & Devices", _stBreadcrumb);
            GUI.Label(
                new Rect(contentX + CardPad, bodyTop + 56, contentW - CardPad * 2, 60),
                "IPAM license not unlocked.\nUse the IPAM: locked button in the title bar to unlock.",
                _stMuted);
            GUI.DragWindow(new Rect(0, 0, w, TitleBarH + ToolbarH));
            return;
        }

        var inlineModalOpen = _ipamChildPrefixWizardOpen
                              || _ipamPrefixDeleteConfirmOpen
                              || ShouldDrawServerEditPopup();

        // Modal flows pause the inventory/body so background controls cannot consume the same click first.
        if (_iopsCalculatorOpen || _customersTabAddServerWizardOpen || inlineModalOpen)
        {
            var pauseTop = bodyTop + 8f;
            var pauseH = bodyH - 16f;
            GUI.DrawTexture(new Rect(contentX + 2, pauseTop + 2, contentW - 4, pauseH - 4), _texCard);
            GUI.Label(
                new Rect(contentX + CardPad, pauseTop + CardPad, contentW - CardPad * 2, 40f),
                "Organization / Servers & Devices (paused)",
                _stBreadcrumb);
            var pauseMsg = _iopsCalculatorOpen
                ? "IOPS sizing is open.\n\nDevice tables are not redrawn while it is open (smoother typing). Close the sizing window (Esc, Close, or click outside) to use the list and detail panel again."
                : _customersTabAddServerWizardOpen
                    ? "Add server is open in a separate window. Close it (Esc, Close, or the window button) to return here."
                    : "A modal dialog is open. Close or apply it to return to the inventory and detail panel.";
            GUI.Label(
                new Rect(contentX + CardPad, pauseTop + CardPad + 48f, contentW - CardPad * 2, 140f),
                pauseMsg,
                _stMuted);
            GUI.enabled = true;
            GUI.DragWindow(new Rect(0, 0, w, TitleBarH + ToolbarH));
            DrawModals(w, h);
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

        SafeBeginScrollView(
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
                case NavSection.Racks:
                    DrawRacksView(innerW);
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
                        case IpamSubSection.DhcpScopes:
                            DrawIpamDhcpScopesView(innerW);
                            break;
                        case IpamSubSection.Tutorial:
                            DrawIpamTutorialView(innerW);
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

        _scroll = SafeConsumeManualScrollPosition(_scroll);
        _scroll.x = Mathf.Clamp(_scroll.x, 0f, Mathf.Max(0f, innerW - scrollViewRect.width + 20f));
        _scroll.y = Mathf.Clamp(_scroll.y, 0f, Mathf.Max(0f, _cachedContentHeight - scrollH));
        SafeEndScrollView();

        if (_selectedNetworkSwitchInstanceIds.Count > 0 && detailH > 0f)
        {
            var panelTop = h - detailH;
            GUI.DrawTexture(new Rect(0, panelTop, w, detailH), _texPageBg);
            DrawSwitchDetail();
        }

        // BeginScrollView/EndScrollView can leave GUI.enabled false on Unity's internal stack.
        GUI.enabled = true;

        if (!_ipamResizeDrag)
        {
            GUI.DragWindow(new Rect(0, 0, w, TitleBarH + ToolbarH));
        }

        if (ShouldDrawWindowResizeHandle())
        {
            var gripLocal = GetWindowResizeGripLocalRect(w, h);
            var gripHover = _ipamResizeDrag;
            if (!gripHover && TryHardwareGuiScreenPointer(out var gripPtr))
            {
                gripHover = GetWindowResizeGripScreenRect().Contains(gripPtr);
            }

            DrawWindowResizeGripVisual(gripLocal, gripHover);
        }

        DrawModals(w, h);

        // Block pass-through to game UI only when no IMGUI control claimed the click (Il2Cpp Event has no isUsed).
        if (Event.current.type == EventType.MouseDown
            && Event.current.button == 0
            && GUIUtility.hotControl == 0
            && !_ipamResizeDrag
            && !IsMouseOverWindowResizeGripLocal(w, h))
        {
            Event.current.Use();
        }
    }

    private const float WindowResizeGripSize = 32f;

    private static bool ShouldDrawWindowResizeHandle()
    {
        return !_windowMaximized
               && !_iopsCalculatorOpen
               && !_customersTabAddServerWizardOpen
               && !_ipamChildPrefixWizardOpen
               && !_ipamPrefixDeleteConfirmOpen
               && !ShouldDrawServerEditPopup();
    }

    private static Rect GetWindowResizeGripLocalRect(float w, float h)
    {
        return new Rect(w - WindowResizeGripSize, h - WindowResizeGripSize, WindowResizeGripSize, WindowResizeGripSize);
    }

    private static Rect GetWindowResizeGripScreenRect()
    {
        return new Rect(
            _windowRect.xMax - WindowResizeGripSize,
            _windowRect.yMax - WindowResizeGripSize,
            WindowResizeGripSize,
            WindowResizeGripSize);
    }

    private static bool IsMouseOverWindowResizeGripLocal(float w, float h)
    {
        if (!ShouldDrawWindowResizeHandle() || Event.current == null)
        {
            return false;
        }

        return GetWindowResizeGripLocalRect(w, h).Contains(Event.current.mousePosition);
    }

    private static void DrawModals(float winW, float winH)
    {
        if (LicenseManager.IsIPAMUnlocked && _iopsCalculatorOpen)
        {
            PumpIopsCalculatorKeyboard();
            DrawInlineModalPanel(winW, winH, "IOPS sizing", (GUI.WindowFunction)DrawIopsStandaloneWindow);
        }

        if (LicenseManager.IsIPAMUnlocked && _customersTabAddServerWizardOpen)
        {
            DrawInlineModalPanel(winW, winH, "Add server", (GUI.WindowFunction)DrawCustomersAddServerWindow);
        }

        if (LicenseManager.IsIPAMUnlocked && _ipamChildPrefixWizardOpen)
        {
            DrawInlineModalPanel(winW, winH, "Prefix", (GUI.WindowFunction)DrawIpamChildPrefixWizardWindow);
        }

        if (LicenseManager.IsIPAMUnlocked && _ipamPrefixDeleteConfirmOpen)
        {
            DrawInlineModalPanel(winW, winH, "Confirm delete", (GUI.WindowFunction)DrawIpamPrefixDeleteConfirmWindow);
        }

        if (ShouldDrawServerEditPopup())
        {
            DrawInlineModalPanel(winW, winH, "Edit object · Server", (GUI.WindowFunction)DrawServerEditPopupWindow);
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
                _autoPrefixCidr = null;
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
                _autoPrefixCidr = null;
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
                _autoPrefixCidr = null;
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
                _autoPrefixCidr = null;
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

    /// <summary>
    /// Screen-space corner grip paint only — pointer input is handled in <see cref="TickInputSystemWindowResize"/>
    /// because <see cref="UiRaycastBlocker"/> blocks IMGUI mouse events while IPAM is open.
    /// </summary>
    private static void DrawWindowResizeHandleScreenSpace()
    {
        if (!ShouldDrawWindowResizeHandle() || Event.current == null || Event.current.type != EventType.Repaint)
        {
            return;
        }

        var r = GetWindowResizeGripScreenRect();
        var hover = _ipamResizeDrag;
        if (!hover && TryHardwareGuiScreenPointer(out var ptr))
        {
            hover = r.Contains(ptr);
        }
        else if (!hover)
        {
            hover = r.Contains(Event.current.mousePosition);
        }

        DrawWindowResizeGripVisual(r, hover);
    }

    private static void DrawWindowResizeGripVisual(Rect r, bool hover)
    {
        if (Event.current == null || Event.current.type != EventType.Repaint)
        {
            return;
        }

        var fill = hover ? _texNavActive ?? _texMutedBtnHover : _texMutedBtn ?? _texRowA;
        if (fill != null)
        {
            GUI.DrawTexture(r, fill, ScaleMode.StretchToFill);
        }

        var oc = GUI.color;
        GUI.color = hover ? new Color(0.45f, 1f, 0.94f, 1f) : new Color(0f, 0.88f, 0.78f, 1f);
        const float border = 2f;
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, border), Texture2D.whiteTexture, ScaleMode.StretchToFill);
        GUI.DrawTexture(new Rect(r.x, r.yMax - border, r.width, border), Texture2D.whiteTexture, ScaleMode.StretchToFill);
        GUI.DrawTexture(new Rect(r.x, r.y, border, r.height), Texture2D.whiteTexture, ScaleMode.StretchToFill);
        GUI.DrawTexture(new Rect(r.xMax - border, r.y, border, r.height), Texture2D.whiteTexture, ScaleMode.StretchToFill);
        GUI.color = oc;
    }

    private static void ApplyWindowResizeDragDelta(Vector2 mousePosition)
    {
        var dx = mousePosition.x - _ipamResizeStartMouse.x;
        var dy = mousePosition.y - _ipamResizeStartMouse.y;
        _windowRect.width = Mathf.Max(WindowMinW, _ipamResizeStartSize.x + dx);
        _windowRect.height = Mathf.Max(WindowMinH, _ipamResizeStartSize.y + dy);
        var maxW = Screen.width - _windowRect.x - 8f;
        var maxH = Screen.height - _windowRect.y - 8f;
        if (maxW > WindowMinW)
        {
            _windowRect.width = Mathf.Min(_windowRect.width, maxW);
        }

        if (maxH > WindowMinH)
        {
            _windowRect.height = Mathf.Min(_windowRect.height, maxH);
        }
    }

    private static void FinishWindowResizeDrag()
    {
        GUIUtility.hotControl = 0;
        _ipamResizeDrag = false;
        _windowRectRestored = _windowRect;
        var dphR = HasDetailSelection() ? GetDetailPanelHeight() : 0f;
        _ipamWindowBaseHeight = Mathf.Max(WindowMinH, _windowRect.height - dphR);
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
                _ipamDevicesFirewallPageMenuOpen = false;
                _ipamDevicesServerPageMenuOpen = false;
            }

            RecomputeContentHeight();
        }
    }

    private static void DrawIcon(Rect r, string iconKey, float size, float pad)
    {
        var tex = NavIcons.Get(iconKey);
        if (tex != null)
        {
            GUI.DrawTexture(new Rect(r.x + pad, r.y + (r.height - size) * 0.5f, size, size), tex, ScaleMode.ScaleToFit, true, 0f,
                new Color(1f, 1f, 1f, GUI.color.a), 0f, 0f);
        }
    }

    private static void DrawCategoryHeader(Rect r, string label, Texture2D chevron, float iconSize, float pad, bool anyChildActive, ref bool expanded)
    {
        var bgTint = anyChildActive ? new Color(0.08f, 0.12f, 0.18f, 0.6f) : new Color(0.05f, 0.07f, 0.10f, 0.3f);
        if (Event.current.type == EventType.Repaint)
        {
            DrawTintedRect(r, bgTint);
        }

        GUI.Label(r, label, _stNavCategory);
        if (chevron != null)
        {
            var chevSize = 12f;
            GUI.DrawTexture(new Rect(r.x + r.width - pad - chevSize, r.y + (r.height - chevSize) * 0.5f, chevSize, chevSize), chevron,
                ScaleMode.ScaleToFit, true, 0f, new Color(0.5f, 0.55f, 0.65f, 0.9f), 0f, 0f);
        }

        if (ImguiButtonOnce(r, "", 9047, GUIStyle.none))
        {
            expanded = !expanded;
            _ipamFormFieldFocus = IpamFormFocusNone;
        }
    }

    private static void DrawNavEntryWithIcon(Rect r, NavSection target, string text, string iconKey, float iconSize, float pad)
    {
        var active = _navSection == target;
        if (active)
        {
            GUI.DrawTexture(r, _texNavActive);
            DrawIcon(r, iconKey, iconSize, pad);
            GUI.Label(new Rect(r.x + 28, r.y, r.width - 30, r.height), text, _stNavItemActive);
            return;
        }

        if (ImguiButtonOnce(r, "", 300 + (int)target, _stNavBtn))
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
                _ipamDevicesFirewallPageMenuOpen = false;
                _ipamDevicesServerPageMenuOpen = false;
            }

            RecomputeContentHeight();
        }

        DrawIcon(r, iconKey, iconSize, pad);
        GUI.Label(new Rect(r.x + 28, r.y, r.width - 30, r.height), text, _stNavBtn);
    }

    private static void DrawSubNavWithIcon(Rect r, DevicesSubSection sub, string text, string iconKey, int dedupeKey, float iconSize, float pad)
    {
        var active = _navSection == NavSection.Devices && _devicesSub == sub;
        if (active)
        {
            GUI.DrawTexture(r, _texNavActive);
            DrawIcon(r, iconKey, iconSize, pad);
            GUI.Label(new Rect(r.x + 28, r.y, r.width - 30, r.height), text, _stNavSubActive);
            return;
        }

        if (ImguiButtonOnce(r, "", dedupeKey, _stNavSubBtn))
        {
            _ipamFormFieldFocus = IpamFormFocusNone;
            _ipamIpAddrPageMenuOpen = false;
            _ipamPrefixPageMenuOpen = false;
            _ipamDevicesSwitchPageMenuOpen = false;
            _ipamDevicesFirewallPageMenuOpen = false;
            _ipamDevicesServerPageMenuOpen = false;
            _customersTabAddServerWizardOpen = false;
            _ipamFormFieldFocus = IpamFormFocusNone;
            _navSection = NavSection.Devices;
            _devicesSub = sub;
            _scroll = Vector2.zero;
            RecomputeContentHeight();
        }

        DrawIcon(r, iconKey, iconSize, pad);
        GUI.Label(new Rect(r.x + 28, r.y, r.width - 30, r.height), text, _stNavSubBtn);
    }

    private static void DrawDevicesSubNav(Rect r, DevicesSubSection sub, string text, int dedupeKey)
    {
        var active = _navSection == NavSection.Devices && _devicesSub == sub;
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
            _ipamFormFieldFocus = IpamFormFocusNone;
            _navSection = NavSection.Devices;
            _devicesSub = sub;
            _scroll = Vector2.zero;
            RecomputeContentHeight();
        }
    }

    private static int GetDevicesTabSearchFocusSlot() => _devicesSub switch
    {
        DevicesSubSection.Switches => IpamFormFocusDevicesSwitchSearch,
        DevicesSubSection.Routers => IpamFormFocusDevicesRouterSearch,
        DevicesSubSection.Firewall => IpamFormFocusDevicesFirewallSearch,
        DevicesSubSection.Servers => IpamFormFocusDevicesServerSearch,
        _ => IpamFormFocusDevicesSwitchSearch,
    };

    private static string GetDevicesTabSearchBuf() => _devicesSub switch
    {
        DevicesSubSection.Switches => _devicesTabSwitchSearchBuf,
        DevicesSubSection.Routers => _devicesTabRouterSearchBuf,
        DevicesSubSection.Firewall => _devicesTabFirewallSearchBuf,
        DevicesSubSection.Servers => _devicesTabServerSearchBuf,
        _ => "",
    };

    private static bool DeviceSwitchSearchMatches(NetworkSwitch sw, string query, string roleLabel)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        if (sw == null)
        {
            return DeviceInventoryReflection.InventorySearchQueryMatches(query, "(removed)");
        }

        if (DeviceInventoryReflection.InventorySearchQueryMatches(query, DeviceInventoryReflection.GetDisplayName(sw)))
        {
            return true;
        }

        if (DeviceInventoryReflection.InventorySearchQueryMatches(query, roleLabel))
        {
            return true;
        }

        if (DeviceInventoryReflection.InventorySearchQueryMatches(query, "Active"))
        {
            return true;
        }

        if (TryGetIpamEolString(sw, out var eol) && DeviceInventoryReflection.InventorySearchQueryMatches(query, eol))
        {
            return true;
        }

        try
        {
            return DeviceInventoryReflection.InventorySearchQueryMatches(query, sw.name);
        }
        catch
        {
            return false;
        }
    }

    private static bool DeviceServerSearchMatches(Server server, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        if (server == null)
        {
            return DeviceInventoryReflection.InventorySearchQueryMatches(query, "(removed)");
        }

        if (DeviceInventoryReflection.InventorySearchQueryMatches(query, DeviceInventoryReflection.GetDisplayName(server)))
        {
            return true;
        }

        try
        {
            if (DeviceInventoryReflection.InventorySearchQueryMatches(query, server.lastDisplayedLabel))
            {
                return true;
            }
        }
        catch
        {
            // Il2Cpp
        }

        var overrideName = NamingConventionStore.TryGetOverrideName(server);
        if (DeviceInventoryReflection.InventorySearchQueryMatches(query, overrideName))
        {
            return true;
        }

        if (DeviceInventoryReflection.InventorySearchQueryMatches(query, GetCustomerDisplayName(server)))
        {
            return true;
        }

        if (DeviceInventoryReflection.InventorySearchQueryMatches(query, DeviceInventoryReflection.GetServerFormFactorLabel(server)))
        {
            return true;
        }

        var ip = DHCPManager.GetServerIP(server);
        if (DeviceInventoryReflection.InventorySearchQueryMatches(query, ip))
        {
            return true;
        }

        if (TryGetIpamEolString(server, out var eol) && DeviceInventoryReflection.InventorySearchQueryMatches(query, eol))
        {
            return true;
        }

        var hasIp = !string.IsNullOrWhiteSpace(ip) && ip != "0.0.0.0";
        return DeviceInventoryReflection.InventorySearchQueryMatches(query, GetIpv4AssignmentStatusLabel(ip));
    }

    private static void BuildFilteredDeviceSwitchRowIndices(
        List<NetworkSwitch> source,
        string query,
        string roleLabel,
        List<int> dest)
    {
        dest.Clear();
        for (var i = 0; i < source.Count; i++)
        {
            if (DeviceSwitchSearchMatches(source[i], query, roleLabel))
            {
                dest.Add(i);
            }
        }
    }

    private static void BuildFilteredDeviceServerRowIndices(string query, List<int> dest)
    {
        dest.Clear();
        EnsureSortedServers();
        for (var i = 0; i < SortedServersBuffer.Count; i++)
        {
            if (DeviceServerSearchMatches(SortedServersBuffer[i], query))
            {
                dest.Add(i);
            }
        }
    }

    private static int GetDevicesSubFilteredRowCount()
    {
        PartitionSortedSwitchesForDeviceTab();
        switch (_devicesSub)
        {
            case DevicesSubSection.Switches:
                BuildFilteredDeviceSwitchRowIndices(
                    DeviceTabSwitchesOnlyScratch,
                    _devicesTabSwitchSearchBuf,
                    "Switch",
                    DeviceTabFilteredRowScratch);
                return DeviceTabFilteredRowScratch.Count;
            case DevicesSubSection.Routers:
                BuildFilteredDeviceSwitchRowIndices(
                    DeviceTabRoutersOnlyScratch,
                    _devicesTabRouterSearchBuf,
                    "Router",
                    DeviceTabFilteredRowScratch);
                return DeviceTabFilteredRowScratch.Count;
            case DevicesSubSection.Firewall:
                BuildFilteredDeviceSwitchRowIndices(
                    DeviceTabFirewallsOnlyScratch,
                    _devicesTabFirewallSearchBuf,
                    "Firewall",
                    DeviceTabFilteredRowScratch);
                return DeviceTabFilteredRowScratch.Count;
            case DevicesSubSection.Servers:
                BuildFilteredDeviceServerRowIndices(_devicesTabServerSearchBuf, DeviceTabFilteredRowScratch);
                return DeviceTabFilteredRowScratch.Count;
            default:
                return 0;
        }
    }

    private static void ClearDevicesTabSearchBuf(DevicesSubSection sub)
    {
        switch (sub)
        {
            case DevicesSubSection.Switches:
                _devicesTabSwitchSearchBuf = "";
                _ipamDevicesSwitchPageIndex = 0;
                break;
            case DevicesSubSection.Routers:
                _devicesTabRouterSearchBuf = "";
                _ipamDevicesRouterPageIndex = 0;
                break;
            case DevicesSubSection.Firewall:
                _devicesTabFirewallSearchBuf = "";
                _ipamDevicesFirewallPageIndex = 0;
                break;
            case DevicesSubSection.Servers:
                _devicesTabServerSearchBuf = "";
                _ipamDevicesServerPageIndex = 0;
                break;
        }

        if (_ipamFormFieldFocus == GetDevicesTabSearchFocusSlotForSub(sub))
        {
            _ipamFormFieldFocus = IpamFormFocusNone;
        }

        RecomputeContentHeight();
    }

    private static int GetDevicesTabSearchFocusSlotForSub(DevicesSubSection sub) => sub switch
    {
        DevicesSubSection.Switches => IpamFormFocusDevicesSwitchSearch,
        DevicesSubSection.Routers => IpamFormFocusDevicesRouterSearch,
        DevicesSubSection.Firewall => IpamFormFocusDevicesFirewallSearch,
        DevicesSubSection.Servers => IpamFormFocusDevicesServerSearch,
        _ => IpamFormFocusNone,
    };

    private static void DrawDevicesTabSearchBar(ref float y, float x0, float tableW, int focusSlot, DevicesSubSection sub)
    {
        GUI.Label(new Rect(x0, y, 72f, 22f), "Search", _stMuted);
        var buf = GetIpamFormFocusBufferForSlot(focusSlot);
        const float clearW = 56f;
        var showClear = !string.IsNullOrWhiteSpace(buf);
        var fieldW = Mathf.Max(120f, tableW - 76f - (showClear ? clearW + 8f : 0f));
        DrawIpamFormTextField(new Rect(x0 + 76f, y, fieldW, 22f), focusSlot, 96, IpamTextFieldKind.Name);
        if (showClear
            && ImguiButtonOnce(new Rect(x0 + 76f + fieldW + 8f, y, clearW, 22f), "Clear", 9064 + (int)sub, _stMutedBtn))
        {
            ClearDevicesTabSearchBuf(sub);
        }

        y += DevicesTabSearchBarH;
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
            case NavSection.Racks:
                _cachedContentHeight = Mathf.Max(360f, ComputeRacksContentHeight());
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
                    case IpamSubSection.DhcpScopes:
                        _cachedContentHeight = Mathf.Max(220f, ComputeIpamDhcpScopesContentHeight());
                        return;
                    case IpamSubSection.Tutorial:
                        _cachedContentHeight = Mathf.Max(400f, ComputeIpamTutorialContentHeight());
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
            case NavSection.Devices:
            {
                var totalRows = GetDevicesSubFilteredRowCount();
                var ps = _ipamIpAddressPageSize;
                int pageStart;
                switch (_devicesSub)
                {
                    case DevicesSubSection.Switches:
                        ClampInventoryPageIndex(ref _ipamDevicesSwitchPageIndex, totalRows);
                        pageStart = _ipamDevicesSwitchPageIndex * ps;
                        break;
                    case DevicesSubSection.Routers:
                        ClampInventoryPageIndex(ref _ipamDevicesRouterPageIndex, totalRows);
                        pageStart = _ipamDevicesRouterPageIndex * ps;
                        break;
                    case DevicesSubSection.Firewall:
                        ClampInventoryPageIndex(ref _ipamDevicesFirewallPageIndex, totalRows);
                        pageStart = _ipamDevicesFirewallPageIndex * ps;
                        break;
                    case DevicesSubSection.Servers:
                        ClampInventoryPageIndex(ref _ipamDevicesServerPageIndex, totalRows);
                        pageStart = _ipamDevicesServerPageIndex * ps;
                        break;
                    default:
                        pageStart = 0;
                        break;
                }

                var bodyRows = totalRows == 0 ? 1 : Mathf.Min(ps, totalRows - pageStart);
                const float devicesPaginationBarH = 28f;
                var yd = CardPad + SectionTitleH + 2f + 7f + SectionTitleH + 4f + DevicesTabSearchBarH + TableHeaderH
                    + bodyRows * TableRowH + devicesPaginationBarH + CardPad;
                _cachedContentHeight = Mathf.Max(220f, yd);
                return;
            }
            default:
                break;
        }

        _cachedContentHeight = 260f;
    }

    private static void DrawSettingsView(float innerW)
    {
        var x0 = CardPad;
        var y = CardPad;
        var cardW = innerW - CardPad * 2f;

        GUI.Label(new Rect(x0, y - 2, cardW, SectionTitleH), "Organization / Settings", _stBreadcrumb);
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
        var newScale = ImguiHorizontalSlider(sliderRect, UiFontScale, 0.5f, 2.0f, 8811);
        if (Mathf.Abs(newScale - UiFontScale) > 0.0001f)
        {
            UiFontScale = newScale;
        }

        var resetRect = new Rect(sliderRect.xMax + 14f, y, 64f, 22f);
        if (ImguiButtonOnce(resetRect, "100%", 8810, _stMutedBtn))
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

    private static void PartitionSortedSwitchesForDeviceTab()
    {
        DeviceTabSwitchesOnlyScratch.Clear();
        DeviceTabRoutersOnlyScratch.Clear();
        DeviceTabFirewallsOnlyScratch.Clear();
        EnsureSortedSwitches();
        foreach (var sw in SortedSwitchesBuffer)
        {
            if (sw == null)
            {
                continue;
            }

            if (DeviceInventoryReflection.NetworkSwitchBehavesAsFirewall(sw))
            {
                DeviceTabFirewallsOnlyScratch.Add(sw);
            }
            else if (DeviceInventoryReflection.NetworkSwitchBehavesAsRouter(sw))
            {
                DeviceTabRoutersOnlyScratch.Add(sw);
            }
            else
            {
                DeviceTabSwitchesOnlyScratch.Add(sw);
            }
        }
    }

    private static string DevicesSubBreadcrumbLabel() => _devicesSub switch
    {
        DevicesSubSection.Switches => "Switches",
        DevicesSubSection.Routers => "Routers",
        DevicesSubSection.Firewall => "Firewall",
        DevicesSubSection.Servers => "Servers",
        _ => "Devices",
    };

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
        _lastInventoryTableWidth = tableW;
        if (_tableColumnsAutoFitPending && tableW > 80f)
        {
            AutoFitInventoryTableColumns(tableW);
            _tableColumnsAutoFitPending = false;
        }

        GUI.Label(
            new Rect(x0, y - 2, cardW, SectionTitleH),
            $"Organization / Servers & Devices  /  {DevicesSubBreadcrumbLabel()}",
            _stBreadcrumb);
        y += SectionTitleH + 2f;

        GUI.DrawTexture(new Rect(x0, y, cardW, 1f), _texTableHeader);
        y += 6f;

        PartitionSortedSwitchesForDeviceTab();
        var ps = _ipamIpAddressPageSize;

        switch (_devicesSub)
        {
            case DevicesSubSection.Switches:
                DrawDevicesNetworkSwitchTable(
                    ref y,
                    x0,
                    cardW,
                    tableW,
                    ps,
                    DevicesSubSection.Switches,
                    "Network switches",
                    DeviceTabSwitchesOnlyScratch,
                    _devicesTabSwitchSearchBuf,
                    "Switch",
                    ref _ipamDevicesSwitchPageIndex,
                    ref _ipamDevicesSwitchPageMenuOpen,
                    600,
                    9201,
                    9202,
                    9203,
                    9204,
                    9205,
                    9206,
                    1,
                    "No network switches");
                break;
            case DevicesSubSection.Routers:
                DrawDevicesNetworkSwitchTable(
                    ref y,
                    x0,
                    cardW,
                    tableW,
                    ps,
                    DevicesSubSection.Routers,
                    "Network routers",
                    DeviceTabRoutersOnlyScratch,
                    _devicesTabRouterSearchBuf,
                    "Router",
                    ref _ipamDevicesRouterPageIndex,
                    ref _ipamDevicesRouterPageMenuOpen,
                    636,
                    0,
                    9270,
                    9271,
                    0,
                    0,
                    0,
                    8,
                    "No network routers",
                    showGear: false,
                    pageSizeHint: "Page size matches Network switches (gear on Switches tab).");
                break;
            case DevicesSubSection.Firewall:
                DrawDevicesNetworkSwitchTable(
                    ref y,
                    x0,
                    cardW,
                    tableW,
                    ps,
                    DevicesSubSection.Firewall,
                    "Firewalls",
                    DeviceTabFirewallsOnlyScratch,
                    _devicesTabFirewallSearchBuf,
                    "Firewall",
                    ref _ipamDevicesFirewallPageIndex,
                    ref _ipamDevicesFirewallPageMenuOpen,
                    650,
                    9281,
                    9282,
                    9283,
                    9284,
                    9285,
                    9286,
                    9,
                    "No firewalls");
                break;
            case DevicesSubSection.Servers:
                DrawDevicesServerTable(ref y, x0, cardW, tableW, ps);
                break;
        }
    }

    private static void DrawDevicesNetworkSwitchTable(
        ref float y,
        float x0,
        float cardW,
        float tableW,
        int ps,
        DevicesSubSection sub,
        string sectionTitle,
        List<NetworkSwitch> rows,
        string searchQuery,
        string roleLabel,
        ref int pageIndex,
        ref bool pageMenuOpen,
        int headerDedupeBase,
        int gearBtnId,
        int prevBtnId,
        int nextBtnId,
        int pageMenuId25,
        int pageMenuId50,
        int pageMenuId100,
        int rowHintSection,
        string emptyLabel,
        bool showGear = true,
        string pageSizeHint = null)
    {
        GUI.Label(new Rect(x0, y, 260, SectionTitleH), sectionTitle, _stSectionTitle);
        y += SectionTitleH + 4f;

        DrawDevicesTabSearchBar(ref y, x0, tableW, GetDevicesTabSearchFocusSlotForSub(sub), sub);
        BuildFilteredDeviceSwitchRowIndices(rows, searchQuery, roleLabel, DeviceTabFilteredRowScratch);

        var headerRow = y;
        var gearRect = new Rect(x0 + tableW, headerRow, IpamIpAddressGearColW, TableHeaderH);
        var menuDropRect = new Rect(x0 + cardW - 132f, headerRow + TableHeaderH + 2f, 128f, 68f);
        var eClose = Event.current;
        if (eClose != null && eClose.type == EventType.MouseDown && eClose.button == 0 && pageMenuOpen)
        {
            if (!menuDropRect.Contains(eClose.mousePosition) && (!showGear || !gearRect.Contains(eClose.mousePosition)))
            {
                pageMenuOpen = false;
            }
        }

        DrawSortableTableHeader(
            new Rect(x0, headerRow, tableW, TableHeaderH),
            ref _switchSortColumn,
            ref _switchSortAscending,
            "Name",
            "Customer",
            "Role",
            "Mgmt IPv4",
            "EOL",
            "Status",
            headerDedupeBase,
            false);
        if (showGear && gearBtnId != 0 && ImguiButtonOnce(gearRect, "\u2699", gearBtnId, _stMutedBtn))
        {
            pageMenuOpen = !pageMenuOpen;
            _ipamDevicesSwitchPageMenuOpen = false;
            _ipamDevicesFirewallPageMenuOpen = false;
            _ipamDevicesServerPageMenuOpen = false;
        }

        y += TableHeaderH;

        var total = DeviceTabFilteredRowScratch.Count;
        ClampInventoryPageIndex(ref pageIndex, total);
        var pageStart = pageIndex * ps;
        var pageEnd = total == 0 ? 0 : Mathf.Min(total, pageStart + ps);

        for (var pi = pageStart; pi < pageEnd; pi++)
        {
            var rowIdx = DeviceTabFilteredRowScratch[pi];
            var sw = rows[rowIdx];
            var r = new Rect(x0, y, tableW, TableRowH);
            var menuBlocksRowPointer = pageMenuOpen && menuDropRect.Overlaps(r);
            string name;
            string role;
            string eolCol;
            string statusCol;
            if (ShouldComputeTruncatedInventoryCellText
                && InventoryScrollRowWantsRepaintTextOrSelected(r.yMin, r.yMax, IsSwitchRowSelected(sw)))
            {
                var nameRaw = sw != null ? DeviceInventoryReflection.GetDisplayName(sw) : "(removed)";
                name = CellTextForCol(0, string.IsNullOrEmpty(nameRaw) ? "—" : nameRaw, tableW);
                role = CellTextForCol(2, roleLabel, tableW);
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

            var sortedIdx = sw != null ? FindSortedSwitchIndex(sw.GetInstanceID()) : rowIdx;
            var toggleRect = sw != null ? GetDeviceToggleRect(r) : default;
            if (TableDataRowClick(
                    r,
                    StableRowHint(rowHintSection, sw, rowIdx),
                    pi % 2 == 1,
                    IsSwitchRowSelected(sw),
                    name,
                    "—",
                    role,
                    "—",
                    eolCol,
                    statusCol,
                    tableW,
                    menuBlocksRowPointer,
                    sw != null ? toggleRect : null))
            {
                HandleSwitchRowClick(sw, sortedIdx >= 0 ? sortedIdx : rowIdx);
            }

            // Toggle button
            if (sw != null)
            {
                var toggleKey = 95000 + Mathf.Abs(sw.GetInstanceID()) % 10000;
                var isActive = IsDeviceActive(sw);
                if (DrawDeviceToggle(r, isActive, toggleKey))
                {
                    ToggleDevice(sw);
                    ModReleaseLog.Info($"Device toggle: {DeviceInventoryReflection.GetDisplayName(sw)} -> {(isActive ? "OFF" : "ON")}");
                }
            }

            y += TableRowH;
        }

        if (total == 0)
        {
            var stubR = new Rect(x0, y, tableW, TableRowH);
            var stubMenuBlock = pageMenuOpen && menuDropRect.Overlaps(stubR);
            var stubText = !string.IsNullOrWhiteSpace(searchQuery) ? "No matches" : "—";
            TableDataRowClick(
                stubR,
                StableRowHint(rowHintSection, null, 0),
                false,
                false,
                stubText,
                "—",
                "—",
                "—",
                "—",
                "—",
                tableW,
                stubMenuBlock);
            y += TableRowH;
        }

        var pageCount = total == 0 ? 1 : (total + ps - 1) / ps;
        var dispStart = total == 0 ? 0 : pageStart + 1;
        var dispEnd = total == 0 ? 0 : pageEnd;
        var noRowsLabel = !string.IsNullOrWhiteSpace(searchQuery) && total == 0
            ? "No matches — adjust search"
            : emptyLabel;
        var label = total == 0
            ? noRowsLabel
            : $"Page {pageIndex + 1} / {pageCount}   ·   {dispStart}-{dispEnd} of {total}";
        GUI.Label(new Rect(x0, y + 2f, tableW - 200f, 22f), label, _stHint);
        var navY = y + 1f;
        if (prevBtnId != 0 && ImguiButtonOnce(new Rect(x0 + tableW - 168f, navY, 72f, 22f), "Previous", prevBtnId, _stMutedBtn))
        {
            if (pageIndex > 0)
            {
                pageIndex--;
                RecomputeContentHeight();
            }
        }

        if (nextBtnId != 0 && ImguiButtonOnce(new Rect(x0 + tableW - 90f, navY, 82f, 22f), "Next", nextBtnId, _stMutedBtn))
        {
            if (pageIndex < pageCount - 1)
            {
                pageIndex++;
                RecomputeContentHeight();
            }
        }

        y += 28f;

        if (showGear && pageMenuId25 != 0)
        {
            DrawInventoryPageSizePopup(menuDropRect, ref pageMenuOpen, pageMenuId25, pageMenuId50, pageMenuId100);
        }
        else if (!string.IsNullOrEmpty(pageSizeHint))
        {
            GUI.Label(new Rect(x0, y, tableW, 18f), pageSizeHint, _stMuted);
            y += 22f;
        }
    }

    private static void DrawDevicesServerTable(ref float y, float x0, float cardW, float tableW, int ps)
    {
        GUI.Label(new Rect(x0, y, 200, SectionTitleH), "Servers", _stSectionTitle);
        y += SectionTitleH + 4f;

        DrawDevicesTabSearchBar(
            ref y,
            x0,
            tableW,
            IpamFormFocusDevicesServerSearch,
            DevicesSubSection.Servers);
        BuildFilteredDeviceServerRowIndices(_devicesTabServerSearchBuf, DeviceTabFilteredRowScratch);

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
            _ipamDevicesFirewallPageMenuOpen = false;
        }

        y += TableHeaderH;

        EnsureSortedServers();
        var totalSv = DeviceTabFilteredRowScratch.Count;
        ClampInventoryPageIndex(ref _ipamDevicesServerPageIndex, totalSv);
        var svPageStart = _ipamDevicesServerPageIndex * ps;
        var svPageEnd = totalSv == 0 ? 0 : Mathf.Min(totalSv, svPageStart + ps);

        for (var pi = svPageStart; pi < svPageEnd; pi++)
        {
            var rowIdx = DeviceTabFilteredRowScratch[pi];
            var server = SortedServersBuffer[rowIdx];
            var r = new Rect(x0, y, tableW, TableRowH);
            var menuBlocksRowPointerSv = _ipamDevicesServerPageMenuOpen && menuDropRectSv.Overlaps(r);

            if (server == null)
            {
                TableDataRowClick(
                    r,
                    StableRowHint(2, null, rowIdx),
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
            var toggleRectSv = GetDeviceToggleRect(r);
            string dispName;
            string cust;
            string typeCol;
            string ipCol;
            string eolCol;
            string status;
            if (ShouldComputeTruncatedInventoryCellText
                && InventoryScrollRowWantsRepaintTextOrSelected(r.yMin, r.yMax, IsServerRowSelected(server)))
            {
                var hasIp = !string.IsNullOrWhiteSpace(ip) && ip != "0.0.0.0";
                var ipRaw = string.IsNullOrWhiteSpace(ip) ? "—" : ip;
                ipCol = CellTextForCol(3, ipRaw, tableW);
                status = CellTextForCol(5, GetIpv4AssignmentStatusLabel(ip), tableW);
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
                    StableRowHint(2, server, rowIdx),
                    pi % 2 == 1,
                    IsServerRowSelected(server),
                    dispName,
                    cust,
                    typeCol,
                    ipCol,
                    eolCol,
                    status,
                    tableW,
                    menuBlocksRowPointerSv,
                    toggleRectSv))
            {
                HandleServerRowClick(server, rowIdx, ip, SortedServersBuffer);
            }

            // Toggle button
            var toggleKeySv = 96000 + Mathf.Abs(server.GetInstanceID()) % 10000;
            var isActiveSv = IsServerRemotePowerActive(server);
            if (DrawDeviceToggle(r, isActiveSv, toggleKeySv))
            {
                ToggleServerFromInventory(server);
                ModReleaseLog.Info($"Server toggle: {DeviceInventoryReflection.GetDisplayName(server)} -> {(isActiveSv ? "OFF" : "ON")}");
            }

            y += TableRowH;
        }

        if (totalSv == 0)
        {
            var stubR = new Rect(x0, y, tableW, TableRowH);
            var stubMenuBlockSv = _ipamDevicesServerPageMenuOpen && menuDropRectSv.Overlaps(stubR);
            var stubText = !string.IsNullOrWhiteSpace(_devicesTabServerSearchBuf) ? "No matches" : "—";
            TableDataRowClick(
                stubR,
                StableRowHint(2, null, 0),
                false,
                false,
                stubText,
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
        var noSvLabel = !string.IsNullOrWhiteSpace(_devicesTabServerSearchBuf) && totalSv == 0
            ? "No matches — adjust search"
            : "No servers";
        var labelSv = totalSv == 0
            ? noSvLabel
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
        out int n7u,
        out int n3u,
        out int nOther,
        out int totalServers,
        out long ratedIopsSum)
    {
        customerContracts = 0;
        n7u = 0;
        n3u = 0;
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
            if (string.Equals(lab, "7 U", StringComparison.Ordinal))
            {
                n7u++;
                ratedIopsSum += IopsPer7UServer;
            }
            else if (string.Equals(lab, "3 U", StringComparison.Ordinal))
            {
                n3u++;
                ratedIopsSum += IopsPer3UServer;
            }
            else
            {
                nOther++;
            }
        }
    }

    // ── Dashboard Colors (Material dark theme) ──
    private static readonly Color32 DashboardColor4U = new(0, 188, 164, 255);
    private static readonly Color32 DashboardColor2U = new(56, 189, 248, 255);
    private static readonly Color32 DashboardColorOther = new(148, 163, 184, 255);
    private static readonly Color32 DashboardTrackDim = new(34, 42, 56, 255);
    private static readonly Color DashCardBg = new(0.08f, 0.10f, 0.13f, 0.85f);
    private static readonly Color DashCardBorder = new(0.14f, 0.17f, 0.22f, 0.6f);
    private static readonly Color DashTeal = new(0f, 0.74f, 0.64f, 1f);
    private static readonly Color DashBlue = new(0.22f, 0.74f, 0.97f, 1f);
    private static readonly Color DashOrange = new(0.95f, 0.55f, 0.2f, 1f);
    private static readonly Color DashGreen = new(0.3f, 0.85f, 0.45f, 1f);
    private static readonly Color DashRed = new(0.95f, 0.3f, 0.25f, 1f);
    private static readonly Color DashYellow = new(0.95f, 0.78f, 0.2f, 1f);

    private static float ComputeDashboardContentHeight()
    {
        var cardH = Mathf.Max(100f, Mathf.Round(120f * UiFontScale));
        var metricCardH = Mathf.Max(80f, Mathf.Round(96f * UiFontScale));
        var sceneCardH = Mathf.Max(70f, Mathf.Round(84f * UiFontScale));
        const float gap = 14f;
        var y = CardPad;
        y += SectionTitleH + 4f + 20f; // header
        y += cardH + gap; // health card
        y += metricCardH + gap; // metric cards row
        y += cardH + gap; // server inventory
        y += Mathf.Ceil(sceneCardH * 0.5f) + gap; // scene devices grid
        y += 60f + CardPad; // info strip
        return Mathf.Max(520f, y);
    }

    // ── Device Toggle Button ──

    private static readonly Color ToggleOnColor = new(0.2f, 0.75f, 0.45f, 1f);
    private static readonly Color ToggleOffColor = new(0.85f, 0.25f, 0.2f, 1f);
    private static readonly Color ToggleOnHover = new(0.3f, 0.85f, 0.55f, 1f);
    private static readonly Color ToggleOffHover = new(0.95f, 0.35f, 0.3f, 1f);

    private static GUIStyle _stToggleOn;
    private static GUIStyle _stToggleOff;

    private static bool DrawDeviceToggle(Rect rowRect, bool isActive, int dedupeKey)
    {
        var btnRect = GetDeviceToggleRect(rowRect);
        var label = isActive ? "⏻ ON" : "⏻ OFF";
        var style = isActive ? _stToggleOn : _stToggleOff;
        if (style == null) style = _stMutedBtn;

        return ImguiButtonOnce(btnRect, label, dedupeKey, style);
    }

    private static Rect GetDeviceToggleRect(Rect rowRect)
    {
        var btnW = 64f;
        var btnH = Mathf.Max(20f, rowRect.height - 6f);
        return new Rect(rowRect.xMax - btnW - 8f, rowRect.y + (rowRect.height - btnH) * 0.5f, btnW, btnH);
    }

    private static bool IsDeviceActive(UnityEngine.Object obj)
    {
        if (obj == null) return false;
        try
        {
            if (obj is Component c) return c.gameObject.activeSelf;
            if (obj is GameObject go) return go.activeSelf;
        }
        catch { }
        return true;
    }

    private static bool IsServerRemotePowerActive(Server server)
    {
        if (server == null)
        {
            return false;
        }

        var tracked = ServerPowerController.TryGetTrackedServerPowerState(server.GetInstanceID());
        return tracked ?? IsDeviceActive(server);
    }

    private static void ToggleServerFromInventory(Server server)
    {
        if (server == null)
        {
            return;
        }

        if (ServerPowerController.TryToggleServerByInstanceId(server.GetInstanceID()))
        {
            RecomputeContentHeight();
            return;
        }

        ToggleDevice(server);
    }

    private static void ToggleDevice(UnityEngine.Object obj)
    {
        if (obj == null) return;
        try
        {
            if (obj is Component c) c.gameObject.SetActive(!c.gameObject.activeSelf);
            else if (obj is GameObject go) go.SetActive(!go.activeSelf);
        }
        catch { }
    }

    // ── Material Card Helpers ──

    private static void DashDrawCard(Rect r)
    {
        if (_texCard != null)
        {
            GUI.DrawTexture(r, _texCard, ScaleMode.StretchToFill, false, 0f, DashCardBg, 0f, 0f);
        }

        // subtle top border accent
        DrawTintedRect(new Rect(r.x, r.y, r.width, 2f), DashCardBorder);
    }

    private static void DashDrawProgressBar(Rect r, float fill01, Color barColor)
    {
        DrawTintedRect(r, DashboardTrackDim);
        var f = Mathf.Clamp01(fill01);
        if (f > 0.001f)
        {
            DrawTintedRect(new Rect(r.x, r.y, Mathf.Max(3f, r.width * f), r.height), barColor);
        }
    }

    private static void DashDrawMetricCard(Rect r, string title, string value, string subtitle, Color accentColor)
    {
        DashDrawCard(r);
        var pad = Mathf.Max(10f, Mathf.Round(14f * UiFontScale));
        var tx = r.x + pad;
        var ty = r.y + Mathf.Max(8f, Mathf.Round(12f * UiFontScale));
        var innerW = r.width - pad * 2f;
        // accent line
        DrawTintedRect(new Rect(r.x + 4f, ty, 3f, Mathf.Max(20f, r.height - 24f)), accentColor);
        GUI.Label(new Rect(tx + 6f, ty, innerW - 6f, 18f), title, _stMuted);
        ty += 22f;
        var valSt = _stIopsResultCounts ?? _stTableCell;
        GUI.Label(new Rect(tx + 6f, ty, innerW - 6f, 32f), value, valSt);
        ty += 36f;
        GUI.Label(new Rect(tx + 6f, ty, innerW - 6f, 18f), subtitle, _stMuted);
    }

    private static void DashDrawSceneCard(Rect r, string title, int count, float fill01, Color barColor, string emptyMsg)
    {
        DashDrawCard(r);
        var pad = Mathf.Max(10f, Mathf.Round(14f * UiFontScale));
        var tx = r.x + pad;
        var ty = r.y + Mathf.Max(8f, Mathf.Round(12f * UiFontScale));
        var innerW = r.width - pad * 2f;
        GUI.Label(new Rect(tx, ty, innerW, 16f), title, _stMuted);
        ty += 20f;
        var valSt = _stIopsResultCounts ?? _stTableCell;
        if (count > 0)
        {
            GUI.Label(new Rect(tx, ty, innerW, 28f), count.ToString("N0"), valSt);
            ty += 32f;
            DashDrawProgressBar(new Rect(tx, ty, innerW, 8f), fill01, barColor);
        }
        else
        {
            GUI.Label(new Rect(tx, ty, innerW, 22f), emptyMsg ?? "No devices detected", _stMuted);
        }
    }

    private static void DashDrawLegendRow(Rect r, Color swatch, string text)
    {
        DrawTintedRect(new Rect(r.x, r.y + 4f, 10f, 10f), swatch);
        GUI.Label(new Rect(r.x + 16f, r.y, r.width - 16f, 18f), text, _stMuted);
    }

    // ── Dashboard Main ──

    private static void DrawDashboard(float innerW)
    {
        CollectDashboardStats(
            out var customerContracts,
            out var n7u,
            out var n3u,
            out var nOther,
            out var totalServers,
            out var ratedIopsSum);

        var sceneLoaded = totalServers > 0 || _cachedSwitches.Length > 0;

        // Scene device counts (routers/firewalls filtered from switches)
        var swTotal = _cachedSwitches.Length;
        var routerCount = 0;
        var firewallCount = 0;
        var pureSwitchCount = 0;
        foreach (var sw in _cachedSwitches)
        {
            if (sw == null) continue;
            if (DeviceInventoryReflection.NetworkSwitchBehavesAsFirewall(sw)) firewallCount++;
            else if (DeviceInventoryReflection.NetworkSwitchBehavesAsRouter(sw)) routerCount++;
            else pureSwitchCount++;
        }

        // Assigned IP count
        var assignedIpCount = 0;
        foreach (var s in _cachedServers)
        {
            if (s == null) continue;
            var ip = DHCPManager.GetServerIP(s);
            if (!string.IsNullOrWhiteSpace(ip) && ip != "0.0.0.0") assignedIpCount++;
        }

        // Health
        var healthScore = NetworkHealthScore.ComputeScore();
        var healthLabel = NetworkHealthScore.GetScoreLabel(healthScore);
        var healthColor = NetworkHealthScore.GetScoreColor(healthScore);

        // Layout
        var x0 = CardPad;
        var y = CardPad;
        var w = innerW - CardPad * 2f;
        var cardH = Mathf.Max(100f, Mathf.Round(120f * UiFontScale));
        var metricCardH = Mathf.Max(80f, Mathf.Round(96f * UiFontScale));
        var sceneCardH = Mathf.Max(70f, Mathf.Round(84f * UiFontScale));
        const float gap = 14f;
        var colGap = 12f;

        // ── Header ──
        GUI.Label(new Rect(x0, y - 2, w, SectionTitleH), "Organization / Overview", _stBreadcrumb);
        y += SectionTitleH + 2f;
        GUI.DrawTexture(new Rect(x0, y, w, 1f), _texTableHeader);
        y += 6f;
        GUI.Label(new Rect(x0, y, w, 28f), "Inventory", _stToolbarTitle);
        y += 30f;
        GUI.Label(new Rect(x0, y, w, 18f), "Live devices · IPv4 assignments", _stMuted);
        y += 24f + gap;

        if (!sceneLoaded)
        {
            GUI.Label(new Rect(x0, y, w, 40f), "Waiting for scene data…", _stMuted);
            return;
        }

        // ── 1. Network Health Card ──
        var healthCardRect = new Rect(x0, y, w, cardH);
        DashDrawCard(healthCardRect);
        var hPad = Mathf.Max(14f, Mathf.Round(18f * UiFontScale));
        var htx = x0 + hPad;
        var hty = y + Mathf.Max(12f, Mathf.Round(16f * UiFontScale));
        GUI.Label(new Rect(htx, hty, w - hPad * 2f, 20f), "Network Health", _stMuted);
        hty += 24f;
        var valSt = _stIopsResultCounts ?? _stTableCell;
        GUI.Label(new Rect(htx, hty, 200f, 36f), $"{healthScore}/100", valSt);
        // health label with color
        var prevColor = GUI.contentColor;
        GUI.contentColor = healthColor;
        GUI.Label(new Rect(htx + 140f, hty + 8f, 120f, 24f), healthLabel, _stSectionTitle);
        GUI.contentColor = prevColor;
        hty += 42f;
        DashDrawProgressBar(new Rect(htx, hty, w - hPad * 2f, 10f), healthScore / 100f, healthColor);
        y += cardH + gap;

        // ── 2. Summary Metric Cards (2-column) ──
        var halfW = (w - colGap) * 0.5f;
        DashDrawMetricCard(
            new Rect(x0, y, halfW, metricCardH),
            "Customer contracts",
            customerContracts.ToString("N0"),
            "Distinct CustomerBase IDs in scene",
            DashTeal);
        DashDrawMetricCard(
            new Rect(x0 + halfW + colGap, y, halfW, metricCardH),
            "Rated IOPS",
            ratedIopsSum.ToString("N0"),
            $"{n7u}×{IopsPer7UServer:N0} + {n3u}×{IopsPer3UServer:N0} (7 U + 3 U tiers)",
            DashBlue);
        y += metricCardH + gap;

        // Second row: Assigned IPs + Unassigned devices
        var unassigned = totalServers - assignedIpCount;
        DashDrawMetricCard(
            new Rect(x0, y, halfW, metricCardH),
            "Assigned IPv4 addresses",
            assignedIpCount.ToString("N0"),
            $"of {totalServers} total servers",
            DashGreen);
        DashDrawMetricCard(
            new Rect(x0 + halfW + colGap, y, halfW, metricCardH),
            "Unassigned devices",
            unassigned.ToString("N0"),
            unassigned > 0 ? "Servers without IPv4 assignment" : "All servers have IPs",
            unassigned > 0 ? DashOrange : DashGreen);
        y += metricCardH + gap;

        // ── 3. Server Inventory Card ──
        var legH = Mathf.Max(18f, Mathf.Round(20f * UiFontScale));
        var invCardH = Mathf.Max(180f, Mathf.Round(220f * UiFontScale));
        var invCardRect = new Rect(x0, y, w, invCardH);
        DashDrawCard(invCardRect);
        var invPad = hPad;
        var invTx = x0 + invPad;
        var invTy = y + Mathf.Max(12f, Mathf.Round(16f * UiFontScale));
        var invInnerW = w - invPad * 2f;
        GUI.Label(new Rect(invTx, invTy, invInnerW, 20f), "Server inventory", _stMuted);
        invTy += 6f;
        GUI.Label(new Rect(invTx, invTy, invInnerW, 24f), "By rack type", _stSectionTitle);
        invTy += 28f;

        var mix = n7u + n3u + nOther;
        // Stacked bar
        DashDrawProgressBar(new Rect(invTx, invTy, invInnerW, 14f), 1f, DashboardTrackDim);
        if (mix > 0)
        {
            var w7 = (n7u / (float)mix) * invInnerW;
            var w3 = (n3u / (float)mix) * invInnerW;
            var bx = invTx;
            if (w7 > 0.5f) { DrawTintedRect(new Rect(bx, invTy, w7, 14f), DashboardColor4U); bx += w7; }
            if (w3 > 0.5f) { DrawTintedRect(new Rect(bx, invTy, w3, 14f), DashboardColor2U); bx += w3; }
            if (invInnerW - bx + invTx > 0.5f) { DrawTintedRect(new Rect(bx, invTy, invInnerW - (bx - invTx), 14f), DashboardColorOther); }
        }
        invTy += 22f;

        // Legend
        float P(int n) => mix > 0 ? (100f * n) / mix : 0f;
        DashDrawLegendRow(new Rect(invTx, invTy, invInnerW, legH), DashboardColor4U,
            $"7 U servers  ·  {n7u}  ({P(n7u):0.#}%)  — {IopsPer7UServer:N0} IOPS each");
        invTy += legH;
        DashDrawLegendRow(new Rect(invTx, invTy, invInnerW, legH), DashboardColor2U,
            $"3 U servers  ·  {n3u}  ({P(n3u):0.#}%)  — {IopsPer3UServer:N0} IOPS each");
        invTy += legH;
        DashDrawLegendRow(new Rect(invTx, invTy, invInnerW, legH), DashboardColorOther,
            $"Other / unknown  ·  {nOther}  ({P(nOther):0.#}%)  — excluded from IOPS total");
        invTy += legH + 6f;
        GUI.Label(new Rect(invTx, invTy, invInnerW, 22f), $"Total rated IOPS:  {ratedIopsSum:N0}", _stTableCell);
        y += invCardH + gap;

        // ── 4. Scene Devices Grid (2x2) ──
        var sceneHalfW = (w - colGap) * 0.5f;
        var sceneDenom = Mathf.Max(1, swTotal + totalServers + routerCount + firewallCount);
        DashDrawSceneCard(
            new Rect(x0, y, sceneHalfW, sceneCardH),
            "Network switches",
            pureSwitchCount,
            pureSwitchCount / (float)sceneDenom,
            DashTeal,
            "No switches found");
        DashDrawSceneCard(
            new Rect(x0 + sceneHalfW + colGap, y, sceneHalfW, sceneCardH),
            "Servers",
            totalServers,
            totalServers / (float)sceneDenom,
            DashBlue,
            "No servers detected");
        y += sceneCardH + gap;
        DashDrawSceneCard(
            new Rect(x0, y, sceneHalfW, sceneCardH),
            "Routers",
            routerCount,
            routerCount / (float)sceneDenom,
            DashTeal,
            "No routers found");
        DashDrawSceneCard(
            new Rect(x0 + sceneHalfW + colGap, y, sceneHalfW, sceneCardH),
            "Firewalls",
            firewallCount,
            firewallCount / (float)sceneDenom,
            DashOrange,
            "No firewalls found");
        y += sceneCardH + gap;

        // ── 5. Info Strip ──
        var infoRect = new Rect(x0, y, w, 48f);
        DrawTintedRect(infoRect, new Color(0.06f, 0.08f, 0.10f, 0.5f));
        GUI.Label(
            new Rect(x0 + 10f, y + 6f, w - 20f, 36f),
            "IOPS totals use the same mod constants as the IOPS sizing calculator. Open Servers & Devices or Customers for full tables; assign IPs from the bottom panel.",
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
            _ipamDevicesRouterPageIndex = 0;
            _ipamDevicesFirewallPageIndex = 0;
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
            _ipamDevicesRouterPageIndex = 0;
            _ipamDevicesFirewallPageIndex = 0;
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
            _ipamDevicesRouterPageIndex = 0;
            _ipamDevicesFirewallPageIndex = 0;
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
        var tableW = cardW - IpamIpAddressGearColW;
        _lastInventoryCardWidth = cardW;
        _lastInventoryTableWidth = tableW;
        if (_tableColumnsAutoFitPending && tableW > 80f)
        {
            AutoFitInventoryTableColumns(tableW);
            _tableColumnsAutoFitPending = false;
        }

        GUI.Label(new Rect(x0, y - 2, cardW, SectionTitleH), "Organization / Network  /  IP addresses", _stBreadcrumb);
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
            if (ShouldComputeTruncatedInventoryCellText
                && InventoryScrollRowWantsRepaintTextOrSelected(r.yMin, r.yMax, IsServerRowSelected(server)))
            {
                var hasIp = !string.IsNullOrWhiteSpace(ip) && ip != "0.0.0.0";
                var ipRaw = FormatServerIpWithContainingPrefix(ip);
                ipCol = CellTextForCol(3, ipRaw, tableW);
                status = CellTextForCol(5, GetIpv4AssignmentStatusLabel(ip), tableW);
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

    private static string GetIpv4AssignmentStatusLabel(string ip)
    {
        return !string.IsNullOrWhiteSpace(ip) && ip != "0.0.0.0" ? "IPv4 assigned" : "Needs IPv4";
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

            // Auto-load prefix and next free IP into octet editor for single-server selection
            if (SelectedServersScratch.Count == 1)
            {
                var srv = SelectedServersScratch[0];
                var assignedIp = DHCPManager.GetServerIP(srv);
                if (!string.IsNullOrWhiteSpace(assignedIp) && assignedIp != "0.0.0.0")
                {
                    LoadOctetsFromIp(assignedIp);
                    // Determine and cache the customer's CIDR for display
                    _autoPrefixCidr = ResolveCustomerPrefixCidr(srv, cb);
                    ShowIpamToast($"IP auto-assigned: {assignedIp}");
                }
                else
                {
                    // Try next free
                    var nextIp = DHCPManager.GetNextFreeIpForServer(srv);
                    if (!string.IsNullOrEmpty(nextIp))
                    {
                        LoadOctetsFromIp(nextIp);
                        _autoPrefixCidr = ResolveCustomerPrefixCidr(srv, cb);
                        ShowIpamToast($"Suggested: {nextIp}");
                    }
                    else
                    {
                        _autoPrefixCidr = ResolveCustomerPrefixCidr(srv, cb);
                        ShowIpamToast("No free IP in contract subnet");
                    }
                }
            }
        }

        if (failed > 0)
        {
            DHCPManager.SetLastIpamError("Customer assignment failed for one or more selected servers.");
        }
    }

    private static string _autoPrefixCidr;

    private static string ResolveCustomerPrefixCidr(Server server, CustomerBase cb)
    {
        if (server == null || cb == null)
        {
            return null;
        }

        try
        {
            var tryOrder = GameSubnetHelper.BuildDhcpCidrTryOrder(server, cb, null, logSteps: false);
            if (tryOrder != null && tryOrder.Count > 0)
            {
                return tryOrder[0];
            }
        }
        catch
        {
            // Il2Cpp
        }

        return null;
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

        if (CustomerPickBuffer.Count > 0 && ImguiButtonOnce(dropBtnRect, summary, 940110, _stMutedBtn))
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
        SafeBeginScrollView(
            dropListRect,
            _customerDropdownScroll,
            new Rect(0, 0, fieldW - 22, CustomerPickBuffer.Count * 28f));
        for (var i = 0; i < CustomerPickBuffer.Count; i++)
        {
            var cb = CustomerPickBuffer[i];
            var nm = cb.customerItem != null ? cb.customerItem.customerName : "";
            var line = $"#{cb.customerID}  {(string.IsNullOrWhiteSpace(nm) ? "—" : nm.Trim())}";
            if (ImguiButtonOnce(new Rect(4, i * 28f, fieldW - 28, 26), line, 940200 + i, _stMutedBtn))
            {
                StartInlineCustomerAssign(cb);
            }
        }

        _customerDropdownScroll = SafeConsumeManualScrollPosition(_customerDropdownScroll);
        SafeEndScrollView();
        py += listH + 4f;
    }

    private static void DrawInlineCustomerAssignSection(float px, ref float py, float iw)
    {
        var cust = _inlineAssignCustomer;

        CollectSelectedServersIntoScratch();
        var n = SelectedServersScratch.Count;

        GUI.Label(new Rect(px, py, iw, 22f), "Assign customer, IPv4, or naming", _stSectionTitle);
        py += 26f;

        if (cust != null && _inlineAssignMode != 2)
        {
            var cn = GetCustomerName(cust);
            GUI.Label(
                new Rect(px, py, iw, 22f),
                $"Customer   #{cust.customerID}  {Trunc(cn ?? "", 48)}",
                _stMuted);
            py += 24f;
        }

        GUI.Label(new Rect(px, py, iw, 20f), $"{n} server(s) selected. Recommended path: choose a customer, then auto-assign IPv4.", _stMuted);
        py += 24f;

        GUI.Label(new Rect(px, py + 3, 52f, 22f), "Mode", _stFormLabel);
        var mx = px + 56f;
        var modeCount = LicenseManager.IsIPAMUnlocked ? 3 : 2;
        var mw = (iw - 60f) / modeCount - 4f;
        var sel0 = _inlineAssignMode == 0;
        if (ImguiButtonOnce(new Rect(mx, py, mw, 26f), "Customer + auto IPv4", 940300, sel0 ? _stPrimaryBtn : _stMutedBtn))
        {
            _inlineAssignMode = 0;
            ResetServerEditPopupScrollForModeChange();
        }

        mx += mw + 6f;
        if (LicenseManager.IsIPAMUnlocked)
        {
            if (ImguiButtonOnce(
                    new Rect(mx, py, mw, 26f),
                    "Manual subnet",
                    940301,
                    _inlineAssignMode == 1 ? _stPrimaryBtn : _stMutedBtn))
            {
                _inlineAssignMode = 1;
                ResetServerEditPopupScrollForModeChange();
            }

            mx += mw + 6f;
        }

        if (ImguiButtonOnce(
                new Rect(mx, py, mw, 26f),
                "Naming",
                940302,
                _inlineAssignMode == 2 ? _stPrimaryBtn : _stMutedBtn))
        {
            _inlineAssignMode = 2;
            ResetServerEditPopupScrollForModeChange();
        }

        py += 34f;

        if (_inlineAssignMode == 2)
        {
            DrawInlineNamingSection(px, ref py, iw, cust);
        }
        else if (cust == null)
        {
            GUI.Label(
                new Rect(px, py, iw, 44f),
                "Choose a customer above to use the recommended path or a manual subnet. Naming works without a customer.",
                _stHint);
            py += 48f;
        }
        else if (_inlineAssignMode == 0)
        {
            GUI.Label(
                new Rect(px, py, iw, 48f),
                "Recommended: assigns the selected customer, then gives each server the next free IPv4 from that customer setup when auto IPv4 is available.",
                _stHint);
            py += 52f;
        }
        else if (LicenseManager.IsIPAMUnlocked)
        {
            GUI.Label(
                new Rect(px, py, iw, 44f),
                "Advanced: choose the subnet yourself. Each selected server gets the first free usable IPv4 from the subnet or free block you pick here.",
                _stHint);
            py += 48f;

            GUI.Label(new Rect(px, py, 72f, 22f), "Search", _stFormLabel);
            DrawIpamFormTextField(
                new Rect(px + 76f, py, Mathf.Min(iw - 80f, 360f), 22f),
                IpamFormFocusInlinePrefixSearch,
                128,
                IpamTextFieldKind.Name);
            py += 28f;

            var plist = GetInlinePrefixPickOptions(_inlineIpamPrefixSearchBuf);
            const float rowH = 26f;
            var rowW = Mathf.Max(120f, iw - 18f);
            for (var i = 0; i < plist.Count; i++)
            {
                var opt = plist[i];
                var marked = string.Equals(opt.PickKey, _inlineIpamPrefixPickKey, StringComparison.Ordinal)
                             || (IsInlineAvailableBlockSelected()
                                 && opt.PickKey.StartsWith("free:", StringComparison.Ordinal)
                                 && string.Equals(
                                     opt.PickKey.Substring("free:".Length).Trim(),
                                     (_inlineIpamFreeBlockAnchorCidr ?? "").Trim(),
                                     StringComparison.OrdinalIgnoreCase));
                var rowStyle = opt.PickKey.StartsWith("free:", StringComparison.Ordinal) && !marked
                    ? _stHint
                    : marked ? _stPrimaryBtn : _stMutedBtn;
                if (ImguiButtonOnce(
                        new Rect(px, py, rowW, rowH - 2f),
                        opt.Label,
                        930000 + i,
                        rowStyle))
                {
                    SetInlineIpamPrefixPick(opt.PickKey ?? "");
                }

                py += rowH;
            }

            py += 4f;

            if (IsInlineAvailableBlockSelected())
            {
                GUI.Label(
                    new Rect(px, py, iw, 28f),
                    $"Will assign the first free usable IPv4 from {_inlineIpamAvailableCidrBuf} to {n} selected server(s).",
                    _stHint);
                py += 32f;

                GUI.Label(new Rect(px, py, iw, 20f), "Resize Available block", _stSectionTitle);
                py += 24f;

                GUI.Label(new Rect(px, py + 2f, 44f, 22f), "CIDR", _stFormLabel);
                DrawIpamFormTextField(
                    new Rect(px + 48f, py, Mathf.Min(iw - 52f, 360f), 22f),
                    IpamFormFocusInlineAvailableCidr,
                    64,
                    IpamTextFieldKind.Cidr);
                py += 28f;

                GUI.Label(new Rect(px, py + 2f, 96f, 22f), "Prefix length", _stFormLabel);
                var stepX = px + 100f;
                const int plHintBase = 0x49A19000;
                if (OctetStepButton(new Rect(stepX, py, 26f, 26f), "−", plHintBase))
                {
                    TryAdjustInlineAvailablePrefixLen(-1);
                }

                stepX += 30f;
                var usableN = RouteMath.CountIpamUsableHosts((_inlineIpamAvailableCidrBuf ?? "").Trim());
                var plText = "/—";
                if (RouteMath.TryParseIpv4Cidr((_inlineIpamAvailableCidrBuf ?? "").Trim(), out _, out var plNow))
                {
                    plText = "/" + plNow.ToString(CultureInfo.InvariantCulture) + "   " + usableN.ToString(CultureInfo.InvariantCulture);
                }

                GUI.Label(new Rect(stepX, py + 2f, 96f, 22f), plText, _stTableCell);
                stepX += 100f;
                if (OctetStepButton(new Rect(stepX, py, 26f, 26f), "+", plHintBase + 1))
                {
                    TryAdjustInlineAvailablePrefixLen(1);
                }

                py += 30f;
                GUI.Label(
                    new Rect(px, py, iw, 28f),
                    $"Inside {_inlineIpamFreeBlockAnchorCidr}. Use − / + or edit CIDR; cannot exceed the selected Available row.",
                    _stHint);
                py += 32f;
            }

        }

        if (!string.IsNullOrEmpty(_inlineAssignError))
        {
            GUI.Label(new Rect(px, py, iw, 44f), _inlineAssignError, _stError);
            py += 48f;
        }

        var btnY = py;
        if (ImguiButtonOnce(new Rect(px, btnY, 120f, 30f), "Apply", 940303, _stPrimaryBtn))
        {
            ApplyInlineCustomerAssign();
        }

        if (ImguiButtonOnce(new Rect(px + 130f, btnY, 120f, 30f), "Cancel", 940304, _stMutedBtn))
        {
            ClearInlineCustomerAssign();
        }

        py += 36f;
    }

    private static void ResetServerEditPopupScrollForModeChange()
    {
        _serverEditPopupScroll = Vector2.zero;
        _serverEditPopupContentH = EstimateServerEditPopupContentHeight();
        _inlineIpamPrefixListScroll = Vector2.zero;
        InvalidateInlineIpamPrefixPickCache();
    }

    private static float EstimateServerEditPopupContentHeight()
    {
        CollectSelectedServersIntoScratch();
        var n = SelectedServersScratch.Count;
        var h = 120f;
        if (n > 1)
        {
            h += 38f;
        }
        else
        {
            h += 44f;
        }

        h += 72f;
        h += 120f;

        if (_inlineAssignMode == 2)
        {
            h += 520f + Mathf.Min(SelectedServersScratch.Count, 32) * 22f;
        }
        else if (_inlineAssignMode == 1 && LicenseManager.IsIPAMUnlocked)
        {
            var plist = GetInlinePrefixPickOptions(_inlineIpamPrefixSearchBuf);
            h += 72f + plist.Count * 26f;
            if (IsInlineAvailableBlockSelected())
            {
                h += 152f;
            }
        }
        else
        {
            h += 56f;
        }

        h += 52f;
        if (n > 1)
        {
            h += 72f;
        }
        else
        {
            h += 52f;
            if (_serverDetailAdvancedOpen)
            {
                h += 64f;
                var srv = SelectedServersScratch.Count > 0 ? SelectedServersScratch[0] : null;
                if (srv != null)
                {
                    var tenancy = IpamDataStore.GetTenancyForServer(srv.GetInstanceID());
                    var isDedicated = string.Equals(tenancy?.Mode ?? "Dedicated", "Dedicated", StringComparison.Ordinal);
                    if (!isDedicated)
                    {
                        var tenants = tenancy?.Tenants ?? new List<TenantAllocation>();
                        h += 24f + (tenants.Count == 0 ? 24f : tenants.Count * 22f);
                    }
                }
            }
        }

        return h;
    }

    private static void DrawServerEditPopupWindow(int windowId)
    {
        _ = windowId;
        // Solid shell (same as IOPS modal) — GUI.Window skin can leave a light/semi-transparent client area.
        if (Event.current.type == EventType.Repaint)
        {
            var fillW = _serverEditPopupRect.width;
            var fillH = Mathf.Min(2000f, _serverEditPopupRect.height + 48f);
            var oldGc = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(0f, 0f, fillW, fillH), _texBackdrop, ScaleMode.StretchToFill, false, 0f, Color.white, 0f, 0f);
            GUI.color = oldGc;
        }

        var w = _serverEditPopupRect.width;
        var h = _serverEditPopupRect.height;
        CollectSelectedServersIntoScratch();
        if (SelectedServersScratch.Count == 0)
        {
            _serverEditPopupDismissed = true;
            return;
        }

        var px = 12f;
        var iw = w - 24f;
        var err = DHCPManager.LastSetIpError;
        var errReserve = !string.IsNullOrEmpty(err) ? 38f : 0f;
        const float headerH = 28f;
        var viewH = Mathf.Max(120f, h - headerH - errReserve - 4f);
        var innerH = Mathf.Max(EstimateServerEditPopupContentHeight(), _serverEditPopupContentH, 1f);
        var maxScrollY = Mathf.Max(0f, innerH - viewH);
        _serverEditPopupScroll.y = Mathf.Clamp(_serverEditPopupScroll.y, 0f, maxScrollY);
        var inner = new Rect(0f, 0f, iw, innerH);
        SafeBeginScrollView(
            new Rect(px, headerH, iw, viewH),
            _serverEditPopupScroll,
            inner);
        var contentPy = 0f;
        DrawServerEditPopupBody(0f, ref contentPy, iw);
        _serverEditPopupContentH = Mathf.Max(_serverEditPopupContentH, contentPy + 16f);
        _serverEditPopupScroll = SafeConsumeManualScrollPosition(_serverEditPopupScroll);
        SafeEndScrollView();

        // Some IL2CPP IMGUI paths leave GUI.enabled false after nested scroll / control draws.
        GUI.enabled = true;

        if (!string.IsNullOrEmpty(err))
        {
            GUI.Label(new Rect(px, h - 36f, w - 24f, 30f), err, _stError);
        }

        if (ImguiButtonOnce(new Rect(w - 84f, 6f, 72f, 22f), "Close", 940305, _stMutedBtn))
        {
            _serverEditPopupDismissed = true;
            ClearInlineCustomerAssign();
        }
    }

    private static void DrawServerEditPopupBody(float px, ref float py, float iw)
    {
        SafeScrollTryHandleWheel();
        var popupFullW = iw + 24f;
        var n = SelectedServersScratch.Count;

        GUI.Label(
            new Rect(px, py, iw, 22f),
            n == 1 ? "Edit object · Server" : $"Edit object · {n} servers",
            _stSectionTitle);
        py += 26f;

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

            GUI.Label(new Rect(px, py, iw, 36f), $"Selected: {sb}", _stMuted);
            py += 38f;
        }
        else
        {
            var s0 = SelectedServersScratch[0];
            var currentIp = DHCPManager.GetServerIP(s0);
            var ipDisp = string.IsNullOrWhiteSpace(currentIp) || currentIp == "0.0.0.0" ? "—" : currentIp;
            GUI.Label(
                new Rect(px, py, iw, 18f),
                $"Name   {Trunc(DeviceInventoryReflection.GetDisplayName(s0), 56)}    │    IPv4   {ipDisp}",
                _stMuted);
            py += 20f;
            var hasRealIp = !string.IsNullOrWhiteSpace(currentIp) && currentIp != "0.0.0.0";
            var cidStr = hasRealIp ? s0.GetCustomerID().ToString() : "—";
            GUI.Label(
                new Rect(px, py, iw, 18f),
                $"Game customerID   {cidStr}",
                _stMuted);
            py += 24f;
        }

        DrawCustomerDropdownAssign(px, ref py, popupFullW);
        py += 4f;
        DrawInlineCustomerAssignSection(px, ref py, iw);

        if (n > 1)
        {
            var ox = px;
            if (ImguiButtonOnce(new Rect(ox, py, 148, 26), "DHCP all selected", 940306, _stPrimaryBtn))
            {
                ModDebugLog.Bootstrap();
                ModDebugLog.WriteDhcpAssign(
                    $"UI: DHCP all selected clicked (selection={SelectedServersScratch.Count} servers)");
                DHCPManager.AssignDhcpToServers(SelectedServersScratch);
                DHCPManager.ClearLastSetIpError();
            }

            ox += 156f;
            if (ImguiButtonOnce(new Rect(ox, py, 118, 26), "Clear all IPs", 940307, _stMutedBtn))
            {
                foreach (var srv in SelectedServersScratch)
                {
                    DHCPManager.SetServerIP(srv, "0.0.0.0", suppressAutoAssignOnEmpty: true);
                }

                DHCPManager.ClearLastSetIpError();
                InvalidateDeviceCache();
            }

            ox += 126f;
            if (ImguiButtonOnce(new Rect(ox, py, 100, 26), "Deselect", 940308, _stMutedBtn))
            {
                _selectedServerInstanceIds.Clear();
                _autoPrefixCidr = null;
                _selectedServer = null;
                _serverRangeAnchorInstanceId = -1;
            }

            py += 32f;
        }

        if (n > 1)
        {
            GUI.Label(
                new Rect(px, py, iw, 36f),
                "DHCP all / Clear all apply to every highlighted server. Ctrl toggles; Shift+click selects a range from the last plain click.",
                _stHint);
            py += 40f;
        }
        else
        {
            var srv = SelectedServersScratch[0];
            var currentIp = DHCPManager.GetServerIP(srv);

            // Resolve customer prefix for display
            var cidr = _autoPrefixCidr;
            if (string.IsNullOrEmpty(cidr))
            {
                var cb = GameSubnetHelper.FindCustomerBaseForServer(srv);
                cidr = ResolveCustomerPrefixCidr(srv, cb);
                _autoPrefixCidr = cidr;
            }

            // Load current IP or auto-suggest from prefix
            if (!string.IsNullOrWhiteSpace(currentIp) && currentIp != "0.0.0.0")
            {
                LoadOctetsFromIp(currentIp);
            }
            else if (!string.IsNullOrEmpty(cidr))
            {
                // Auto-suggest next free IP from customer prefix
                var nextIp = DHCPManager.GetNextFreeIpForServer(srv);
                if (!string.IsNullOrEmpty(nextIp))
                {
                    LoadOctetsFromIp(nextIp);
                }
            }

            // Prefix info line
            if (!string.IsNullOrEmpty(cidr))
            {
                GUI.Label(new Rect(px, py, iw, 18f), $"Customer subnet:  {cidr}", _stHint);
                py += 20f;
            }

            // Octet Editor Row
            var ox = px;
            DrawOctetEditor(ref _oct0, ref ox, py, 0);
            GUI.Label(new Rect(ox, py + 2, 10, 22), ".", _stOctetVal);
            ox += 12f;
            DrawOctetEditor(ref _oct1, ref ox, py, 1);
            GUI.Label(new Rect(ox, py + 2, 10, 22), ".", _stOctetVal);
            ox += 12f;
            DrawOctetEditor(ref _oct2, ref ox, py, 2);
            GUI.Label(new Rect(ox, py + 2, 10, 22), ".", _stOctetVal);
            ox += 12f;
            DrawOctetEditor(ref _oct3, ref ox, py, 3);
            py += 30f;

            // Apply IP + Next Free Buttons
            var bx = px;
            if (ImguiButtonOnce(new Rect(bx, py, 100, 26), "Apply IP", 940309, _stPrimaryBtn))
            {
                var ip = $"{_oct0}.{_oct1}.{_oct2}.{_oct3}";
                if (DHCPManager.SetServerIP(srv, ip))
                {
                    ShowIpamToast($"IP set to {ip}");
                    InvalidateDeviceCache();
                }
                else if (!string.IsNullOrEmpty(DHCPManager.LastSetIpError))
                {
                    ShowIpamToast(DHCPManager.LastSetIpError);
                }
            }

            bx += 108f;
            if (ImguiButtonOnce(new Rect(bx, py, 120, 26), "Next Free IP", 940310, _stMutedBtn))
            {
                var nextIp = DHCPManager.GetNextFreeIpForServer(srv);
                if (!string.IsNullOrEmpty(nextIp))
                {
                    LoadOctetsFromIp(nextIp);
                    ShowIpamToast($"Suggested: {nextIp}");
                }
                else
                {
                    ShowIpamToast("No free IP available");
                }
            }

            py += 32f;

            // Error Display
            if (!string.IsNullOrEmpty(DHCPManager.LastSetIpError))
            {
                GUI.Label(new Rect(px, py, iw, 36f), DHCPManager.LastSetIpError, _stError);
                py += 40f;
            }

            // Advanced server options
            py += 4f;
            var tenancy = IpamDataStore.GetTenancyForServer(srv.GetInstanceID());
            var currentMode = tenancy?.Mode ?? "Dedicated";
            var isDedicated = currentMode == "Dedicated";
            var advancedRect = new Rect(px, py, iw, 22f);
            DrawTintedRect(advancedRect, new Color(0.08f, 0.10f, 0.14f, 0.7f));
            var advancedTitle = _serverDetailAdvancedOpen
                ? "Advanced server options ▾"
                : $"Advanced server options ▸  ({(isDedicated ? "1 customer per server" : "shared between customers")})";
            GUI.Label(new Rect(px + 8f, py, iw - 16f, 22f), advancedTitle, _stSectionTitle);
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && advancedRect.Contains(Event.current.mousePosition))
            {
                _serverDetailAdvancedOpen = !_serverDetailAdvancedOpen;
                _serverEditPopupContentH = EstimateServerEditPopupContentHeight();
                Event.current.Use();
            }

            py += 26f;
            if (!_serverDetailAdvancedOpen)
            {
                GUI.Label(new Rect(px, py, iw, 22f), "Open this only if you need shared-tenancy behavior or tenant-level cleanup.", _stHint);
                py += 26f;
            }
            else
            {
                if (ImguiButtonOnce(new Rect(px, py, 80f, 22f), "Dedicated", 940311, isDedicated ? _stPrimaryBtn : _stMutedBtn))
                {
                    IpamDataStore.TrySetServerMode(srv.GetInstanceID(), "Dedicated", 1, out _);
                }

                if (ImguiButtonOnce(new Rect(px + 88f, py, 80f, 22f), "Shared", 940312, !isDedicated ? _stPrimaryBtn : _stMutedBtn))
                {
                    IpamDataStore.TrySetServerMode(srv.GetInstanceID(), "Shared", 4, out _);
                }

                GUI.Label(new Rect(px + 176f, py + 2f, iw - 180f, 20f),
                    isDedicated ? "1 customer per server" : "Multiple customers can share this server",
                    _stHint);
                py += 28f;

                if (!isDedicated)
                {
                    var tenants = tenancy?.Tenants ?? new List<TenantAllocation>();
                    GUI.Label(new Rect(px, py, iw, 22f), $"Allocated tenants ({tenants.Count}/{tenancy?.MaxTenants ?? 4})", _stSectionTitle);
                    py += 24f;

                    if (tenants.Count == 0)
                    {
                        GUI.Label(new Rect(px, py, iw, 22f), "No tenants allocated. Use the Customers tab to assign them.", _stMuted);
                        py += 24f;
                    }
                    else
                    {
                        for (var ti = 0; ti < tenants.Count; ti++)
                        {
                            var t = tenants[ti];
                            DrawTintedRect(new Rect(px, py, iw, 20f), ti % 2 == 0 ? new Color(0.05f, 0.06f, 0.08f, 0.4f) : new Color(0.04f, 0.05f, 0.06f, 0.3f));
                            GUI.Label(new Rect(px + 4f, py, iw - 80f, 20f), $"{t.CustomerName} (ID: {t.CustomerId})", _stTableCell);
                            GUI.Label(new Rect(px + iw - 80f, py, 50f, 20f), $"{t.AllocatedIps}/{t.MaxIps} IP", _stTableCell);
                            if (ImguiButtonOnce(new Rect(px + iw - 24f, py + 1f, 22f, 18f), "×", 57 + ti, _stMutedBtn))
                            {
                                IpamDataStore.TryRemoveTenant(srv.GetInstanceID(), t.CustomerId, out _);
                            }

                            py += 22f;
                        }
                    }
                }
            }
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

        // Mouse-Wheel auf aktives Octet
        if (_activeOctetSlot == octetSlot
            && Event.current.type == EventType.ScrollWheel
            && labelRect.Contains(Event.current.mousePosition))
        {
            var delta = Mathf.RoundToInt(-Event.current.delta.y);
            oct = Mathf.Clamp(oct + delta, 0, 255);
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
