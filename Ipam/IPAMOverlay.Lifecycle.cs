using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace DHCPSwitches;

// Per-frame cache refresh, Input System workarounds for IOPS toolbar, IMGUI focus recovery after closing overlays.
// Does not own: OnGUI draw order (see Draw in IPAMOverlay.cs).

public static partial class IPAMOverlay
{
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
            GameSubnetHelper.RefreshSceneCaches();
        }

        var eolFullSnapshotDue = t >= _nextEolSnapshotRefreshTime;

        if (t >= _nextListRefreshTime)
        {
            _nextListRefreshTime = t + ListRefreshInterval;
            _cachedSwitches = FilterAlive(UnityEngine.Object.FindObjectsOfType<NetworkSwitch>());
            _cachedServers = FilterAlive(UnityEngine.Object.FindObjectsOfType<Server>());
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
            RecomputeContentHeight();
        }

        if (eolFullSnapshotDue)
        {
            _nextEolSnapshotRefreshTime = t + EolSnapshotRefreshInterval;
            RebuildIpamEolSnapshot();
            _tableColumnsAutoFitPending = true;
            _serverSortListDirty = true;
            _switchSortListDirty = true;
        }
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

    public static void InvalidateDeviceCache()
    {
        _nextListRefreshTime = 0f;
        _nextEolSnapshotRefreshTime = 0f;
        _nextSubnetSceneRefreshTime = 0f;
        GameSubnetHelper.InvalidateSceneCaches();
        _serverSortListDirty = true;
        _switchSortListDirty = true;
        _eolDisplayByInstanceId.Clear();
        _tableColumnsAutoFitPending = true;
        _lastDeviceListServerCount = -1;
        _lastDeviceListSwitchCount = -1;
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

        if (!IsVisible && !DeviceTerminalOverlay.IsVisible)
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
