using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace DHCPSwitches;

// IPAMOverlay partial map (same static class):
// - IPAMOverlay.cs (this file): shared state, visibility, Draw() ordering vs modal layer.
// - IPAMOverlay.ImGui.cs: textures, GUIStyle setup, ImguiButtonOnce, toolbar width, toasts.
// - IPAMOverlay.InventoryTable.cs: EOL snapshot dict, column weights, sorting, table rows/headers.
// - IPAMOverlay.Lifecycle.cs: scene cache ticks, Input System IOPS fallback, IMGUI recovery, FilterAlive.
// - IPAMOverlay.IopsModal.cs: standalone IOPS window + IMGUI keyboard pump + debug mouse line.
// - IPAMOverlay.WindowUi.cs: main GUI.Window callback, nav/sections, selection + detail panel + octet editor.

public static partial class IPAMOverlay
{
    private static bool _visible;

    /// <summary>Matches <see cref="Time.frameCount"/> when F1 toggled IPAM this frame (legacy input suppression).</summary>
    internal static int LegacyF1ConsumedFrame { get; private set; } = -1;

    internal static void NotifyF1ToggleHandledThisFrame()
    {
        LegacyF1ConsumedFrame = Time.frameCount;
    }

    public static bool IsVisible
    {
        get => _visible;
        set
        {
            if (value && !_visible)
            {
                _nextListRefreshTime = 0f;
                _nextSubnetSceneRefreshTime = 0f;
                _serverSortListDirty = true;
                _switchSortListDirty = true;
                MarkCustomersTabServerBufferDirty();
                _tableColumnsAutoFitPending = true;
                if (_windowRect.width < WindowMinW)
                {
                    _windowRect.width = WindowMinW;
                }

                if (_windowRectRestored.width < WindowMinW)
                {
                    _windowRectRestored.width = WindowMinW;
                }

                ModDebugLog.WriteIpam($"IPAM open frame={Time.frameCount}");
                _ipamWindowBaseHeight = Mathf.Max(WindowMinH, _windowRect.height);
                _ipamHadDetailSelectionLastFrame = false;
                _iopsToolbarRectWindowLocal = default;
                _iopsToolbarScreenRect = default;
                _iopsToolbarRectLogHash = 0;
                _nextEolSnapshotRefreshTime = 0f;
            }

            if (!value)
            {
                _customerDropdownOpen = false;
                CloseIopsCalculatorModal("IPAM hidden");
                _iopsToolbarRectWindowLocal = default;
                _iopsToolbarScreenRect = default;
                _serverRangeAnchorInstanceId = -1;
                _switchRangeAnchorInstanceId = -1;
                _selectedNetworkSwitchInstanceIds.Clear();
                _selectedNetworkSwitch = null;
                _eolDisplayByInstanceId.Clear();
                _ipamResizeDrag = false;
                _columnGripWeightsStart = null;
                _activeOctetSlot = -1;
                BeginImGuiInputRecoveryBurst();
                UiRaycastBlocker.SetBlocking(false);
                GameInputSuppression.SetSuppressed(false);
                IpamMenuOcclusion.Tick(false);
                ModDebugLog.WriteIpam($"IPAM close frame={Time.frameCount} recoverUntil={_imguiRecoverUntilExclusive}");
            }

            _visible = value;
        }
    }

    private static readonly Dictionary<int, string> CustomerDisplayNameCache = new();

    /// <summary>
    /// True when the server is not on a customer for IPAM purposes: negative <see cref="Server.GetCustomerID"/>,
    /// or the game left a default positive ID before rack placement — then we require an <see cref="AssetManagementDeviceLine"/>
    /// reference and/or a real IPv4 to treat them as assigned.
    /// </summary>
    internal static bool IsServerWithoutCustomerAssignment(Server server)
    {
        if (server == null)
        {
            return true;
        }

        int cid;
        try
        {
            cid = server.GetCustomerID();
        }
        catch
        {
            return true;
        }

        if (cid < 0)
        {
            return true;
        }

        if (GameSubnetHelper.IsServerReferencedByAssetManagementDeviceLine(server))
        {
            return false;
        }

        var ip = DHCPManager.GetServerIP(server);
        var hasRealIp = !string.IsNullOrWhiteSpace(ip) && ip != "0.0.0.0";
        return !hasRealIp;
    }

    internal static string GetCustomerDisplayName(Server server)
    {
        if (server == null)
        {
            return "Unknown";
        }

        if (IsServerWithoutCustomerAssignment(server))
        {
            return "—";
        }

        var cid = server.GetCustomerID();
        if (cid < 0)
        {
            return "—";
        }

        if (CustomerDisplayNameCache.TryGetValue(cid, out var cached))
        {
            return cached;
        }

        var name = TryGetCustomerDisplayNameById(cid);
        if (!string.IsNullOrWhiteSpace(name))
        {
            var result = $"#{cid} {name}";
            CustomerDisplayNameCache[cid] = result;
            return result;
        }

        var fallback = $"Customer {cid}";
        CustomerDisplayNameCache[cid] = fallback;
        return fallback;
    }

    private static string TryGetCustomerDisplayNameById(int customerId)
    {
        if (customerId < 0)
        {
            return null;
        }

        var customer = GameSubnetHelper.FindCustomerBaseByCustomerId(customerId);
        if (customer == null)
        {
            return null;
        }

        var name = GetCustomerName(customer);
        return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
    }

    private static Rect _windowRect = new(48f, 48f, 1200f, 640f);
    private static Rect _windowRectRestored = new(48f, 48f, 1200f, 640f);
    private static bool _windowMaximized;

    /// <summary>Window height without the edit-object strip (non-maximized); extra height is added while a device is selected.</summary>
    private static float _ipamWindowBaseHeight = 640f;

    private static bool _ipamHadDetailSelectionLastFrame;
    private static Vector2 _scroll = Vector2.zero;
    internal static Server _selectedServer;
    /// <summary>First selected switch in current sort order (detail panel / CLI); mirrors the switch instance-id set.</summary>
    private static NetworkSwitch _selectedNetworkSwitch;
    private static readonly HashSet<int> _selectedNetworkSwitchInstanceIds = new();
    private static readonly HashSet<int> _selectedServerInstanceIds = new();
    /// <summary>Last plain-clicked server for Shift+click range (Explorer-style).</summary>
    private static int _serverRangeAnchorInstanceId = -1;
    /// <summary>Last plain-clicked switch for Shift+click range in the switch table.</summary>
    private static int _switchRangeAnchorInstanceId = -1;
    private static readonly List<Server> SelectedServersScratch = new();
    private static readonly List<CustomerBase> CustomerPickBuffer = new();
    private static bool _customerDropdownOpen;
    private static Vector2 _customerDropdownScroll;

    /// <summary>Customers tab: filtered server rows (rebuilt each draw).</summary>
    private static readonly List<Server> CustomersTabServersBuffer = new();

    /// <summary>Customers tab: sentinel <c>-1</c> = servers with no customer (<see cref="IsServerWithoutCustomerAssignment"/>); else match <see cref="Server.GetCustomerID"/> (including 0).</summary>
    private static int _customersTabFilterCustomerId = -1;

    private static bool _customersTabFilterMenuOpen;
    private static Vector2 _customersTabFilterScroll;

    private static bool _ipamResizeDrag;
    private static Vector2 _ipamResizeStartMouse;
    private static Vector2 _ipamResizeStartSize;

    /// <summary>Right-packed inventory row: trailing margin + gaps + Fit columns + IOPS calc.</summary>
    private static int _windowMinWFrame = -1;
    private static float _windowMinWCached = 1020f;

    private static int _lastDeviceListServerCount = -1;
    private static int _lastDeviceListSwitchCount = -1;

    private static string _ipamToast;
    private static float _ipamToastUntil;

    private static float WindowMinW
    {
        get
        {
            var f = Time.frameCount;
            if (f == _windowMinWFrame)
            {
                return _windowMinWCached;
            }

            _windowMinWFrame = f;
            _windowMinWCached = ComputeToolbarInventoryMinWidth() + 50f;
            return _windowMinWCached;
        }
    }
    private const float WindowMinH = 480f;

    private enum NavSection
    {
        Dashboard = 0,
        Devices = 1,
        IpAddresses = 2,
        Customers = 3,
    }

    private static NavSection _navSection = NavSection.Devices;

    private static Server[] _cachedServers = System.Array.Empty<Server>();
    private static NetworkSwitch[] _cachedSwitches = System.Array.Empty<NetworkSwitch>();
    private static float _nextListRefreshTime;
    /// <summary>Full EOL string recompute (reflection on every device) — separate from device list refresh to reduce load.</summary>
    private static float _nextEolSnapshotRefreshTime;
    /// <summary>Customer/MGM caches — cheaper to run less often than the device list.</summary>
    private static float _nextSubnetSceneRefreshTime;
    private static float _cachedContentHeight = 320f;
    /// <summary>EOL strings from periodic snapshot; avoids reflection every IMGUI pass and while sorting.</summary>
    internal static readonly Dictionary<int, string> _eolDisplayByInstanceId = new();
    private static readonly HashSet<int> IpamEolAliveScratch = new();
    private static readonly List<int> IpamEolRemoveScratch = new();

    private static readonly List<Server> SortedServersBuffer = new();
    private static readonly List<NetworkSwitch> SortedSwitchesBuffer = new();
    private static bool _serverSortListDirty = true;
    private static bool _switchSortListDirty = true;
    private static int _serverSortColumn;
    private static bool _serverSortAscending = true;
    private static int _switchSortColumn;
    private static bool _switchSortAscending = true;

    private const float ListRefreshInterval = 0.7f;
    private const float SubnetSceneRefreshInterval = 2.25f;
    /// <summary>How often to recompute all EOL display strings (live countdowns were heavy when tied to frequent list ticks).</summary>
    private const float EolSnapshotRefreshInterval = 60f;

    // Layout (NetBox-style shell)
    /// <summary>Title/subtitle row + button row so actions never cover “Inventory”.</summary>
    private const float ToolbarH = 74f;
    private const float ToolbarTitleBlockH = 44f;
    /// <summary>Two rows: title + window buttons, then DHCP/IPAM license toggles on the right.</summary>
    private const float TitleBarH = 54f;
    private const float SidebarW = 208f;

    private static readonly float[] TableColWeight = { 0.2f, 0.17f, 0.08f, 0.17f, 0.14f, 0.24f };
    private static float _columnGripMouseStartX;
    private static float[] _columnGripWeightsStart;
    private static bool _tableColumnsAutoFitPending = true;
    private static float _lastInventoryCardWidth;
    private const float MinColWeight = 0.045f;
    private const float MaxColWeight = 0.52f;
    private const float TableRowH = 30f;
    private const float SectionTitleH = 22f;
    private const float TableHeaderH = 26f;
    private const float CardPad = 14f;

    /// <summary>Editable IP as four octets — GUI.TextField breaks under IL2CPP (TextEditor unstripping).</summary>
    private static int _oct0 = 192, _oct1 = 168, _oct2 = 1, _oct3 = 10;
    private static int _activeOctetSlot = -1;

    private static bool _iopsCalculatorOpen;
    /// <summary>Digits only — typed via <see cref="EventType.KeyDown"/> (no TextField on IL2CPP).</summary>
    private static string _iopsCalculatorDigits = "";
    private static int _iopsCalcKeyDedupeFrame = -1;
    private static readonly HashSet<int> _iopsCalcKeyDigests = new();

    /// <summary>Screen-space rect for the IOPS toolbar button (debug log / legacy probe).</summary>
    private static Rect _iopsToolbarScreenRect;
    /// <summary>Toolbar button rect in <see cref="GUI.Window"/> coordinates — used with hardware mouse + top-left GUI space.</summary>
    private static Rect _iopsToolbarRectWindowLocal;
    private static int _iopsToolbarRectLogHash;
    private static int _ipamDebugLastMouseDownFrame = -1;
    /// <summary>Separate top-level IMGUI window for IOPS math (not nested inside IPAM — avoids clipping/depth issues).</summary>
    private static Rect _iopsStandaloneWindowRect = new(200f, 120f, 460f, 280f);

    private const int IopsPer2UServer = 5000;
    private const int IopsPer4UServer = 12000;

    /// <summary>
    /// Unity's <see cref="Key"/> values are not contiguous — do not use <c>(Key)((int)Key.Digit0 + d)</c>
    /// (that maps digit slots to unrelated keys like Meta/Ctrl and fires bogus digits).
    /// </summary>
    private static readonly Key[] IopsDigitKeys =
    {
        Key.Digit0, Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4,
        Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9
    };

    private static readonly Key[] IopsNumpadKeys =
    {
        Key.Numpad0, Key.Numpad1, Key.Numpad2, Key.Numpad3, Key.Numpad4,
        Key.Numpad5, Key.Numpad6, Key.Numpad7, Key.Numpad8, Key.Numpad9
    };

    private static Texture2D _texBackdrop;
    private static Texture2D _texSidebar;
    private static Texture2D _texToolbar;
    private static Texture2D _texPageBg;
    private static Texture2D _texCard;
    private static Texture2D _texTableHeader;
    private static Texture2D _texRowA;
    private static Texture2D _texRowB;
    private static Texture2D _texRowHover;
    private static Texture2D _texNavActive;
    private static Texture2D _texPrimaryBtn;
    private static Texture2D _texPrimaryBtnHover;
    private static Texture2D _texMutedBtn;
    private static Texture2D _texMutedBtnHover;
    private static Texture2D _texNavBtnHover;
    private static Texture2D _texModalDim;
    /// <summary>1×1 white — tint with <see cref="GUI.DrawTexture"/> for chart fills.</summary>
    private static Texture2D _texWhite;
    private static bool _texturesReady;

    private static GUIStyle _stModalBlocker;
    private static GUIStyle _stWindowTitle;
    private static GUIStyle _stToolbarTitle;
    private static GUIStyle _stToolbarSub;
    private static GUIStyle _stBadgeOn;
    private static GUIStyle _stBadgeOff;
    private static GUIStyle _stNavItemActive;
    private static GUIStyle _stNavHint;
    private static GUIStyle _stBreadcrumb;
    private static GUIStyle _stSectionTitle;
    private static GUIStyle _stTableHeaderText;
    private static GUIStyle _stHeaderSortBtn;
    private static GUIStyle _stTableCell;
    private static GUIStyle _stNavBtn;
    private static GUIStyle _stMuted;
    /// <summary>Muted body text, centered (e.g. empty chart placeholder).</summary>
    private static GUIStyle _stMutedCenter;
    private static GUIStyle _stHint;
    private static GUIStyle _stError;
    private static GUIStyle _stFormLabel;
    private static GUIStyle _stOctetVal;
    private static GUIStyle _stPrimaryBtn;
    private static GUIStyle _stMutedBtn;
    /// <summary>Result line — same face as <see cref="_stTableCell"/> (IPAM body text).</summary>
    private static GUIStyle _stIopsResult;
    /// <summary>Larger type for IOPS calculator 4 U / 2 U server counts.</summary>
    private static GUIStyle _stIopsResultCounts;
    /// <summary>Large metric on the IPAM dashboard hero cards.</summary>
    private static GUIStyle _stDashboardHeroValue;
    /// <summary>Placeholder on the result card when no valid IOPS entered (muted like <see cref="_stMuted"/>).</summary>
    private static GUIStyle _stIopsResultPlaceholder;
    private static bool _stylesReady;
    private static int _imguiButtonDedupeFrame = -1;
    private static int _imguiButtonDedupeKey = -1;
    private static bool _pendingImguiStateRelease;
    /// <summary>Exclusive end frame for repeated IMGUI clears (multiple <c>OnGUI</c> passes / scroll views).</summary>
    private static int _imguiRecoverUntilExclusive;


    public static void InvalidateCustomerCache()
    {
        CustomerDisplayNameCache.Clear();
        GameSubnetHelper.InvalidateSceneCustomerFrameCache();
        _serverSortListDirty = true; // Forces the table to redraw
        MarkCustomersTabServerBufferDirty();
    }


    public static void Draw()
    {
        if (!IsVisible)
        {
            return;
        }

        EnsureTextures();
        EnsureStyles();
        PumpIpamDebugOnGuiMouse();

        var oldDepth = GUI.depth;
        // Prefer drawing after other IMGUI in the frame; depth helps automatic layout stacks.
        // Unity IMGUI: lower GUI.depth values are drawn ON TOP of higher ones (see GUI.depth docs).
        const int ipamDepth = 32000;
        GUI.depth = ipamDepth;

        var oldBg = GUI.backgroundColor;
        var oldContent = GUI.contentColor;

        // Full-screen IMGUI control: absorbs pointer events for IMGUI stacks. Do not disable
        // UnityEngine.EventSystems.EventSystem here — Data Center's UI_SelectedBorder.Update null-refs when it is off.
        // Drawn before the window so the window (drawn later) still receives hits inside its rect.
        var fullScreen = new Rect(0f, 0f, Screen.width, Screen.height);
        var perf = ModDebugLog.IsIpamPerfLoggingEnabled;
        var tBackdrop0 = perf ? Time.realtimeSinceStartupAsDouble : 0d;
        GUI.Box(fullScreen, string.Empty, _stModalBlocker);

        GUI.DrawTexture(_windowRect, _texBackdrop, ScaleMode.StretchToFill, false, 0f, Color.white, 0f, 0f);

        GUI.backgroundColor = Color.white;
        GUI.contentColor = new Color(0.92f, 0.94f, 0.96f, 1f);

        var tWindow0 = perf ? Time.realtimeSinceStartupAsDouble : 0d;
        _windowRect = GUI.Window(9001, _windowRect, (GUI.WindowFunction)DrawWindow, " ");
        if (perf)
        {
            var tEnd = Time.realtimeSinceStartupAsDouble;
            RecordIpamPerfDrawMs((tWindow0 - tBackdrop0) * 1000.0, (tEnd - tWindow0) * 1000.0);
        }

        // IOPS modal must be drawn *after* GUI.Window returns: controls nested inside the window callback
        // can fail hit-testing (clicks/keys) on IL2CPP; top-level rects use screen space matching Event.mousePosition.
        // Keyboard pump runs here too so KeyDown is handled outside the window's GUI group.
        if (LicenseManager.IsIPAMUnlocked && _iopsCalculatorOpen)
        {
            PumpIopsCalculatorKeyboard();
            // Standalone screen-space window (not nested in IPAM) so it always paints and receives input.
            GUI.depth = 0;
            var dimCol = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.5f);
            if (Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), _texModalDim, ScaleMode.StretchToFill);
            }

            GUI.color = dimCol;
            // Match IPAM shell: default GUI.Window skin paints a light client area; swap window chrome to _texBackdrop.
            var winSt = GUI.skin.window;
            var oldWinBg = winSt.normal.background;
            var oldWinOnBg = winSt.onNormal.background;
            var oldWinTxt = winSt.normal.textColor;
            var oldWinOnTxt = winSt.onNormal.textColor;
            winSt.normal.background = _texBackdrop;
            winSt.onNormal.background = _texBackdrop;
            winSt.normal.textColor = new Color32(248, 250, 252, 255);
            winSt.onNormal.textColor = new Color32(248, 250, 252, 255);
            _iopsStandaloneWindowRect = GUI.Window(9002, _iopsStandaloneWindowRect, (GUI.WindowFunction)DrawIopsStandaloneWindow, "IOPS sizing");
            winSt.normal.background = oldWinBg;
            winSt.onNormal.background = oldWinOnBg;
            winSt.normal.textColor = oldWinTxt;
            winSt.onNormal.textColor = oldWinOnTxt;
        }

        GUI.backgroundColor = oldBg;
        GUI.contentColor = oldContent;
        GUI.depth = oldDepth;
    }
}