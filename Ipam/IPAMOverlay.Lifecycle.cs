using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace DHCPSwitches;

// Per-frame cache refresh, Input System workarounds for IOPS toolbar, IMGUI focus recovery after closing overlays.
// Does not own: OnGUI draw order (see Draw in IPAMOverlay.cs).

public static partial class IPAMOverlay
{
    private static double _ipamPerfAccBackdropMs;
    private static double _ipamPerfAccWindowMs;
    private static int _ipamPerfWindowPasses;
    private static float _ipamPerfNextLogTime = -1f;
    private static float _ipamNextPlayerInputRescanTime;

    private static bool HardwarePointerInWindowLocalRect(Rect windowRect, Rect localRect, out Vector2 localPointer)
    {
        localPointer = default;
        if (localRect.width <= 0f)
        {
            return false;
        }

        var mouse = Mouse.current;
        if (mouse == null)
        {
            return false;
        }

        var mp = mouse.position.ReadValue();
        var guiScreen = new Vector2(mp.x, Screen.height - mp.y);
        localPointer = new Vector2(guiScreen.x - windowRect.x, guiScreen.y - windowRect.y);
        return localRect.Contains(localPointer);
    }
    /// <summary>
    /// Deactivates any <see cref="UnityEngine.InputSystem.PlayerInput"/> spawned after IPAM opened so letter keys
    /// (e.g. pause bound to P) do not reach gameplay while the overlay is up.
    /// </summary>
    internal static void TickIpamGameInputSuppression()
    {
        if (!IsVisible)
        {
            return;
        }

        if (Time.unscaledTime >= _ipamNextPlayerInputRescanTime)
        {
            _ipamNextPlayerInputRescanTime = Time.unscaledTime + 2.5f;
            GameInputSuppression.RefreshWhileActive();
        }
    }

    public static void TickDeviceListCache()
    {
        if (!IsVisible)
        {
            return;
        }

        var t = Time.realtimeSinceStartup;
        if (t >= _nextSubnetSceneRefreshTime)
        {
            _nextSubnetSceneRefreshTime = t + SubnetSceneRefreshInterval;
            // Scene caches refreshed
        }

        var eolFullSnapshotDue = t >= _nextEolSnapshotRefreshTime;

        if (t >= _nextListRefreshTime)
        {
            _nextListRefreshTime = t + ListRefreshInterval;
            _cachedSwitches = FilterAlive(UnityEngine.Object.FindObjectsOfType<NetworkSwitch>());
            _cachedServers = FilterAlive(UnityEngine.Object.FindObjectsOfType<Server>());
            GameSubnetHelper.RebuildAssetManagementDeviceLineServerCache();
            var sc = _cachedServers.Length;
            var swc = _cachedSwitches.Length;
            if (sc != _lastDeviceListServerCount || swc != _lastDeviceListSwitchCount)
            {
                _tableColumnsAutoFitPending = true;
                _lastDeviceListServerCount = sc;
                _lastDeviceListSwitchCount = swc;
            }

            PruneIpamEolSnapshotForRemovedDevices();
            if (!eolFullSnapshotDue)
            {
                EnsureIpamEolSnapshotForNewDevices();
            }

            _serverSortListDirty = true;
            _switchSortListDirty = true;
            MarkCustomersTabServerBufferDirty();
            RecomputeContentHeight();
        }

        if (eolFullSnapshotDue)
        {
            _nextEolSnapshotRefreshTime = t + EolSnapshotRefreshInterval;
            RebuildIpamEolSnapshot();
            _tableColumnsAutoFitPending = true;
            _serverSortListDirty = true;
            _switchSortListDirty = true;
            MarkCustomersTabServerBufferDirty();
        }
    }

    internal static void RecordIpamPerfDrawMs(double backdropAndShellMs, double windowCallbackMs)
    {
        _ipamPerfAccBackdropMs += backdropAndShellMs;
        _ipamPerfAccWindowMs += windowCallbackMs;
        _ipamPerfWindowPasses++;
    }

    /// <summary>Throttled IPAM GUI cost when <c>DHCPSwitches-ipam-perf.flag</c> is present (call from <see cref="DHCPSwitchesBehaviour.Update"/> while IPAM is open).</summary>
    public static void TickIpamPerfLog()
    {
        if (!IsVisible)
        {
            _ipamPerfNextLogTime = -1f;
            _ipamPerfAccBackdropMs = 0d;
            _ipamPerfAccWindowMs = 0d;
            _ipamPerfWindowPasses = 0;
            return;
        }

        if (!ModDebugLog.IsIpamPerfLoggingEnabled)
        {
            _ipamPerfNextLogTime = -1f;
            return;
        }

        var t = Time.realtimeSinceStartup;
        if (_ipamPerfNextLogTime < 0f)
        {
            _ipamPerfNextLogTime = t + 2f;
        }

        if (t < _ipamPerfNextLogTime)
        {
            return;
        }

        _ipamPerfNextLogTime = t + 2f;
        if (_ipamPerfWindowPasses <= 0)
        {
            ModDebugLog.WriteIpamPerf(
                $"nav={_navSection} servers={_cachedServers.Length} switches={_cachedSwitches.Length} guiWindowPasses=0 (no Draw this interval)");
            return;
        }

        var inv = NumberFormatInfo.InvariantInfo;
        ModDebugLog.WriteIpamPerf(
            string.Format(
                inv,
                "nav={0} servers={1} switches={2} guiWindowPasses={3} backdropShellMs={4:F2} windowMs={5:F2} avgWindowMs={6:F3}",
                _navSection,
                _cachedServers.Length,
                _cachedSwitches.Length,
                _ipamPerfWindowPasses,
                _ipamPerfAccBackdropMs,
                _ipamPerfAccWindowMs,
                _ipamPerfAccWindowMs / _ipamPerfWindowPasses));
        _ipamPerfAccBackdropMs = 0d;
        _ipamPerfAccWindowMs = 0d;
        _ipamPerfWindowPasses = 0;
    }

    /// <summary>
    /// Full-screen <see cref="UiRaycastBlocker"/> can prevent IMGUI from seeing mouse clicks while IPAM is open.
    /// Hardware mouse + last frame's screen rect opens the IOPS dialog when IMGUI does not fire.
    /// </summary>
    public static void TickInputSystemIopsToolbarClick()
    {
        if (!IsVisible || !LicenseManager.IsIPAMUnlocked || _iopsCalculatorOpen)
        {
            return;
        }

        var mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
        {
            return;
        }

        var mp = mouse.position.ReadValue();
        var rectEmpty = _iopsToolbarRectWindowLocal.width <= 0f;
        var hit = HardwarePointerInWindowLocalRect(_windowRect, _iopsToolbarRectWindowLocal, out var ptrLocal);

        if (ModDebugLog.IsIpamFileLogEnabled && (rectEmpty || !hit))
        {
            IpamDebugLog.IopsHardwareProbe(mp, ptrLocal, _windowRect, _iopsToolbarRectWindowLocal, rectEmpty, hit);
        }

        if (rectEmpty || !hit)
        {
            return;
        }

        OpenIopsCalculator();
        if (ModDebugLog.IsIpamFileLogEnabled)
        {
            IpamDebugLog.IopsOpenedViaInputFallback(Time.frameCount, mp, _iopsToolbarScreenRect);
        }
    }

    /// <summary>
    /// IMGUI <see cref="EventType.KeyDown"/> often never receives typing when the Input System owns the keyboard.
    /// Read digits / Esc / Backspace here so the IOPS window can calculate.
    /// </summary>
    public static void TickIopsCalculatorInputSystem()
    {
        if (!IsVisible || !_iopsCalculatorOpen || !LicenseManager.IsIPAMUnlocked)
        {
            return;
        }

        var kb = Keyboard.current;
        if (kb == null)
        {
            return;
        }

        if (kb.escapeKey.wasPressedThisFrame)
        {
            CloseIopsCalculatorModal("Escape");
            return;
        }

        if (kb.backspaceKey.wasPressedThisFrame)
        {
            if (_iopsCalculatorDigits.Length > 0)
            {
                _iopsCalculatorDigits = _iopsCalculatorDigits.Substring(0, _iopsCalculatorDigits.Length - 1);
            }

            return;
        }

        for (var d = 0; d <= 9; d++)
        {
            if (!kb[IopsDigitKeys[d]].wasPressedThisFrame && !kb[IopsNumpadKeys[d]].wasPressedThisFrame)
            {
                continue;
            }

            if (_iopsCalculatorDigits.Length >= 14)
            {
                return;
            }

            if (_iopsCalculatorDigits.Length == 0 && d == 0)
            {
                return;
            }

            _iopsCalculatorDigits += (char)('0' + d);
            return;
        }
    }

    public static void TickOctetInputSystem()
    {
        if (!IsVisible || _activeOctetSlot < 0)
        {
            return;
        }

        var kb = Keyboard.current;
        if (kb == null)
        {
            return;
        }

        if (kb.escapeKey.wasPressedThisFrame)
        {
            _activeOctetSlot = -1;
            return;
        }

        if (kb.backspaceKey.wasPressedThisFrame)
        {
            var current = GetOctetValue(_activeOctetSlot);
            SetOctetValue(_activeOctetSlot, current / 10);
            return;
        }

        if (kb.periodKey.wasPressedThisFrame || kb.commaKey.wasPressedThisFrame)
        {
            _activeOctetSlot = Mathf.Min(3, _activeOctetSlot + 1);
            return;
        }

        for (var d = 0; d <= 9; d++)
        {
            if (!kb[IopsDigitKeys[d]].wasPressedThisFrame && !kb[IopsNumpadKeys[d]].wasPressedThisFrame)
            {
                continue;
            }

            var current = GetOctetValue(_activeOctetSlot);
            var next = current * 10 + d;
            if (next <= 255)
            {
                SetOctetValue(_activeOctetSlot, next);
            }

            return;
        }
    }

    public static void InvalidateDeviceCache()
    {
        _nextListRefreshTime = 0f;
        _nextEolSnapshotRefreshTime = 0f;
        _nextSubnetSceneRefreshTime = 0f;
        // Scene caches invalidated
        _serverSortListDirty = true;
        _switchSortListDirty = true;
        _eolDisplayByInstanceId.Clear();
        _tableColumnsAutoFitPending = true;
        _lastDeviceListServerCount = -1;
        _lastDeviceListSwitchCount = -1;
        CustomerDisplayNameCache.Clear();
        DHCPManager.ClearCaches();
        GameSubnetHelper.RebuildAssetManagementDeviceLineServerCache();
        MarkCustomersTabServerBufferDirty();
    }
    /// <summary>Call from <c>OnGUI</c> (start and, when overlays are closed, end of pass) so IMGUI does not keep capture after closing windows.</summary>
    public static void PumpImGuiInputRecovery()
    {
        var inBurst = _imguiRecoverUntilExclusive != 0 && Time.frameCount < _imguiRecoverUntilExclusive;
        if (!_pendingImguiStateRelease && !inBurst)
        {
            return;
        }

        _pendingImguiStateRelease = false;
        try
        {
            GUIUtility.hotControl = 0;
            GUIUtility.keyboardControl = 0;
        }
        catch
        {
            // Safe if GUI state not ready on this pass
        }

        if (!IsVisible)
        {
            try
            {
                EventSystem.current?.SetSelectedGameObject(null);
            }
            catch
            {
            }
        }
    }

    /// <summary>After closing IPAM/CLI, clear IMGUI modal state for several frames and the next <c>OnGUI</c> pass.</summary>
    public static void BeginImGuiInputRecoveryBurst(int extraFramesInclusive = 8)
    {
        _pendingImguiStateRelease = true;
        var until = Time.frameCount + extraFramesInclusive;
        if (_imguiRecoverUntilExclusive == 0 || until > _imguiRecoverUntilExclusive)
        {
            _imguiRecoverUntilExclusive = until;
        }
    }

    /// <summary>Queue IMGUI capture release for the next <c>OnGUI</c> (e.g. when closing CLI).</summary>
    public static void ScheduleImguiInputRecovery()
    {
        BeginImGuiInputRecoveryBurst();
    }
}
