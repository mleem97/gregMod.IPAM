using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace DHCPSwitches;

public static class IPAMOverlay
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
                IpamEolSnapshot.Clear();
                _ipamResizeDrag = false;
                _columnGripWeightsStart = null;
                BeginImGuiInputRecoveryBurst();
                UiRaycastBlocker.SetBlocking(DeviceTerminalOverlay.IsVisible);
                GameInputSuppression.SetSuppressed(DeviceTerminalOverlay.IsVisible);
                IpamMenuOcclusion.Tick(DeviceTerminalOverlay.IsVisible);
                ModDebugLog.WriteIpam($"IPAM close frame={Time.frameCount} recoverUntil={_imguiRecoverUntilExclusive}");
            }

            _visible = value;
        }
    }

    private static Rect _windowRect = new(48f, 48f, 1200f, 640f);
    private static Rect _windowRectRestored = new(48f, 48f, 1200f, 640f);
    private static bool _windowMaximized;
    private static Vector2 _scroll = Vector2.zero;
    private static Server _selectedServer;
    private static NetworkSwitch _selectedNetworkSwitch;
    private static readonly HashSet<int> _selectedServerInstanceIds = new();
    /// <summary>Last plain-clicked server for Shift+click range (Explorer-style).</summary>
    private static int _serverRangeAnchorInstanceId = -1;
    private static readonly List<Server> SelectedServersScratch = new();
    private static readonly List<CustomerBase> CustomerPickBuffer = new();
    private static bool _customerDropdownOpen;
    private static Vector2 _customerDropdownScroll;

    private static bool _ipamResizeDrag;
    private static Vector2 _ipamResizeStartMouse;
    private static Vector2 _ipamResizeStartSize;

    /// <summary>Right-packed inventory row: tr + gaps + Fit + Auto-DHCP + Fill + L3 + Pause + Alarms + Tech + IOPS.</summary>
    private const float ToolbarInventoryActionsMinW =
        14f + 8f * 8f + 96f + 168f + 118f + 100f + 152f + 118f + 132f + 108f;
    private const float WindowMinW = ToolbarInventoryActionsMinW + 50f;
    private const float WindowMinH = 480f;

    private enum NavSection
    {
        Dashboard = 0,
        Devices = 1,
        IpAddresses = 2,
        Prefixes = 3,
    }

    private static NavSection _navSection = NavSection.Devices;

    private static Server[] _cachedServers = System.Array.Empty<Server>();
    private static NetworkSwitch[] _cachedSwitches = System.Array.Empty<NetworkSwitch>();
    private static float _nextListRefreshTime;
    /// <summary>Full EOL string recompute (reflection on every device) — separate from device list refresh to reduce load.</summary>
    private static float _nextEolSnapshotRefreshTime;
    /// <summary>Customer/MGM <see cref="GameSubnetHelper.RefreshSceneCaches"/> — cheaper to run less often than the device list.</summary>
    private static float _nextSubnetSceneRefreshTime;
    private static float _cachedContentHeight = 320f;
    /// <summary>EOL strings from periodic snapshot; avoids reflection every IMGUI pass and while sorting.</summary>
    private static readonly Dictionary<int, string> IpamEolSnapshot = new();
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
    private static float GetDetailPanelHeight()
    {
        if (_selectedNetworkSwitch != null)
        {
            return 228f;
        }

        if (_selectedServerInstanceIds.Count > 1)
        {
            return _customerDropdownOpen ? 304f : 300f;
        }

        if (_selectedServer != null)
        {
            return _customerDropdownOpen ? 264f : 260f;
        }

        return 100f;
    }
    private const float TableRowH = 30f;
    private const float SectionTitleH = 22f;
    private const float TableHeaderH = 26f;
    private const float CardPad = 14f;

    /// <summary>Editable IP as four octets — GUI.TextField breaks under IL2CPP (TextEditor unstripping).</summary>
    private static int _oct0 = 192, _oct1 = 168, _oct2 = 1, _oct3 = 10;

    private static bool _iopsCalculatorOpen;
    /// <summary>Digits only — typed via <see cref="EventType.KeyDown"/> (no TextField on IL2CPP).</summary>
    private static string _iopsCalculatorDigits = "";
    /// <summary>0 = 2U server, 1 = 4U server.</summary>
    private static int _iopsCalculatorServerKind;
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
    private static GUIStyle _stHint;
    private static GUIStyle _stError;
    private static GUIStyle _stFormLabel;
    private static GUIStyle _stOctetVal;
    private static GUIStyle _stPrimaryBtn;
    private static GUIStyle _stMutedBtn;
    /// <summary>Result line — same face as <see cref="_stTableCell"/> (IPAM body text).</summary>
    private static GUIStyle _stIopsResult;
    /// <summary>Placeholder on the result card when no valid IOPS entered (muted like <see cref="_stMuted"/>).</summary>
    private static GUIStyle _stIopsResultPlaceholder;
    private static bool _stylesReady;

    /// <summary>IL2CPP stubs omit <c>RectOffset(l,r,t,b)</c> and <c>new GUIStyle(other)</c>; build via property setters.</summary>
    private static RectOffset Ro(int l, int r, int t, int b)
    {
        var o = new RectOffset();
        o.left = l;
        o.right = r;
        o.top = t;
        o.bottom = b;
        return o;
    }

    /// <summary>
    /// <see cref="GUI.Window"/> may run the window function several times per mouse release (layout/repaint).
    /// <see cref="GUI.Button"/> can then return true multiple times in one frame — dedupe per control key.
    /// </summary>
    private static int _imguiButtonDedupeFrame = -1;
    private static int _imguiButtonDedupeKey = -1;

    private static bool ImguiButtonOnce(Rect r, string text, int dedupeKey, GUIStyle style = null)
    {
        var pressed = style != null ? GUI.Button(r, text, style) : GUI.Button(r, text);
        if (!pressed)
        {
            return false;
        }

        var f = Time.frameCount;
        if (f == _imguiButtonDedupeFrame && dedupeKey == _imguiButtonDedupeKey)
        {
            return false;
        }

        _imguiButtonDedupeFrame = f;
        _imguiButtonDedupeKey = dedupeKey;
        return true;
    }

    private static bool ImguiButtonOnce(Rect r, GUIContent content, int dedupeKey, GUIStyle style = null)
    {
        var pressed = style != null ? GUI.Button(r, content, style) : GUI.Button(r, content);
        if (!pressed)
        {
            return false;
        }

        var f = Time.frameCount;
        if (f == _imguiButtonDedupeFrame && dedupeKey == _imguiButtonDedupeKey)
        {
            return false;
        }

        _imguiButtonDedupeFrame = f;
        _imguiButtonDedupeKey = dedupeKey;
        return true;
    }

    /// <summary>
    /// Single-step +/- for octets. Does not use <see cref="GUI.Button"/> — inside <see cref="GUI.Window"/> that helper
    /// can report the same release multiple times per click; <see cref="Event.GetTypeForControl"/> fires once per control.
    /// </summary>
    private static bool OctetStepButton(Rect r, string label, int controlHint)
    {
        var id = GUIUtility.GetControlID(controlHint, FocusType.Passive, r);
        var e = Event.current;

        switch (e.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (e.button == 0 && r.Contains(e.mousePosition))
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
                if (r.Contains(e.mousePosition))
                {
                    return true;
                }

                break;
            case EventType.Repaint:
                if (_stMutedBtn != null)
                {
                    _stMutedBtn.Draw(r, new GUIContent(label), id);
                }
                else
                {
                    GUI.skin.button.Draw(r, new GUIContent(label), id);
                }

                break;
        }

        return false;
    }

    /// <summary>
    /// IOPS toolbar control: <see cref="GUI.Button"/> is unreliable inside <see cref="GUI.Window"/> on some IL2CPP builds;
    /// use explicit MouseDown/MouseUp like <see cref="OctetStepButton"/>.
    /// </summary>
    private static bool IopsCalcToolbarButton(Rect r, string label)
    {
        const int controlHint = 0x49A0_0000;
        var id = GUIUtility.GetControlID(controlHint, FocusType.Passive, r);
        var e = Event.current;

        switch (e.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (e.button == 0 && r.Contains(e.mousePosition))
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
                if (r.Contains(e.mousePosition))
                {
                    return true;
                }

                break;
            case EventType.Repaint:
                if (_stMutedBtn != null)
                {
                    _stMutedBtn.Draw(r, new GUIContent(label), id);
                }
                else
                {
                    GUI.skin.button.Draw(r, new GUIContent(label), id);
                }

                break;
        }

        return false;
    }

    /// <summary>
    /// <see cref="Mouse.current.position"/> is bottom-left; IMGUI / <see cref="Event.mousePosition"/> is top-left.
    /// </summary>
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

    private static void RebuildIpamEolSnapshot()
    {
        IpamEolSnapshot.Clear();
        FillIpamEolSnapshotForCachedDevices();
    }

    /// <summary>Add EOL for devices not yet in <see cref="IpamEolSnapshot"/> (runs on list refresh; cheap when few new rows).</summary>
    private static void EnsureIpamEolSnapshotForNewDevices()
    {
        foreach (var s in _cachedServers)
        {
            if (s == null)
            {
                continue;
            }

            var id = s.GetInstanceID();
            if (IpamEolSnapshot.ContainsKey(id))
            {
                continue;
            }

            if (DeviceInventoryReflection.TryGetEolDisplay(s, out var t))
            {
                IpamEolSnapshot[id] = t;
            }
        }

        foreach (var sw in _cachedSwitches)
        {
            if (sw == null)
            {
                continue;
            }

            var id = sw.GetInstanceID();
            if (IpamEolSnapshot.ContainsKey(id))
            {
                continue;
            }

            if (DeviceInventoryReflection.TryGetEolDisplay(sw, out var t))
            {
                IpamEolSnapshot[id] = t;
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
                IpamEolSnapshot[s.GetInstanceID()] = t;
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
                IpamEolSnapshot[sw.GetInstanceID()] = t;
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
        foreach (var id in IpamEolSnapshot.Keys)
        {
            if (!alive.Contains(id))
            {
                remove.Add(id);
            }
        }

        foreach (var id in remove)
        {
            IpamEolSnapshot.Remove(id);
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

        return IpamEolSnapshot.TryGetValue(o.GetInstanceID(), out eol);
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

        BumpHeader(0, "IPv4 address");
        BumpHeader(1, "Customer");
        BumpHeader(2, "Role");
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
        IpamEolSnapshot.Clear();
    }

    private static bool _pendingImguiStateRelease;
    /// <summary>Exclusive end frame for repeated IMGUI clears (multiple <c>OnGUI</c> passes / scroll views).</summary>
    private static int _imguiRecoverUntilExclusive;

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
        GUI.Box(fullScreen, string.Empty, _stModalBlocker);

        GUI.DrawTexture(_windowRect, _texBackdrop, ScaleMode.StretchToFill, false, 0f, Color.white, 0f, 0f);

        GUI.backgroundColor = Color.white;
        GUI.contentColor = new Color(0.92f, 0.94f, 0.96f, 1f);

        _windowRect = GUI.Window(9001, _windowRect, (GUI.WindowFunction)DrawWindow, " ");

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

    private static void EnsureTextures()
    {
        if (_texturesReady)
        {
            return;
        }

        _texBackdrop = MakeTexture(10, 12, 16, 255);
        _texSidebar = MakeTexture(24, 30, 40, 255);
        _texToolbar = MakeTexture(28, 34, 44, 255);
        _texPageBg = MakeTexture(20, 24, 32, 255);
        _texCard = MakeTexture(30, 36, 46, 255);
        _texTableHeader = MakeTexture(40, 48, 60, 255);
        _texRowA = MakeTexture(34, 40, 52, 255);
        _texRowB = MakeTexture(38, 45, 58, 255);
        _texRowHover = MakeTexture(52, 62, 78, 255);
        _texNavActive = MakeTexture(0, 122, 111, 255);
        _texPrimaryBtn = MakeTexture(0, 133, 120, 255);
        _texPrimaryBtnHover = MakeTexture(0, 152, 136, 255);
        _texMutedBtn = MakeTexture(48, 55, 68, 255);
        _texMutedBtnHover = MakeTexture(58, 66, 82, 255);
        _texNavBtnHover = MakeTexture(38, 46, 60, 255);
        _texModalDim = MakeTexture(0, 0, 0, 140);
        _texturesReady = true;
    }

    private static void EnsureStyles()
    {
        if (_stylesReady)
        {
            return;
        }

        _stModalBlocker = new GUIStyle();
        _stModalBlocker.normal.background = _texModalDim;
        _stModalBlocker.border = Ro(0, 0, 0, 0);

        var lf = GUI.skin.label.font;
        var bf = GUI.skin.button.font;

        _stWindowTitle = new GUIStyle();
        _stWindowTitle.font = lf;
        _stWindowTitle.fontSize = 13;
        _stWindowTitle.fontStyle = FontStyle.Bold;
        _stWindowTitle.alignment = TextAnchor.MiddleLeft;
        _stWindowTitle.padding = Ro(10, 8, 0, 0);
        _stWindowTitle.normal.textColor = new Color32(248, 250, 252, 255);

        _stToolbarTitle = new GUIStyle();
        _stToolbarTitle.font = lf;
        _stToolbarTitle.fontSize = 15;
        _stToolbarTitle.fontStyle = FontStyle.Bold;
        _stToolbarTitle.alignment = TextAnchor.MiddleLeft;
        _stToolbarTitle.normal.textColor = new Color32(236, 240, 247, 255);

        _stToolbarSub = new GUIStyle();
        _stToolbarSub.font = lf;
        _stToolbarSub.fontSize = 11;
        _stToolbarSub.alignment = TextAnchor.MiddleLeft;
        _stToolbarSub.normal.textColor = new Color32(154, 164, 178, 255);

        _stBadgeOn = new GUIStyle();
        _stBadgeOn.font = lf;
        _stBadgeOn.fontSize = 9;
        _stBadgeOn.fontStyle = FontStyle.Bold;
        _stBadgeOn.alignment = TextAnchor.MiddleCenter;
        _stBadgeOn.normal.textColor = new Color32(110, 231, 210, 255);
        _stBadgeOn.normal.background = MakeTexture(12, 56, 52, 255);
        _stBadgeOn.border = Ro(4, 4, 4, 4);

        _stBadgeOff = new GUIStyle();
        _stBadgeOff.font = lf;
        _stBadgeOff.fontSize = 9;
        _stBadgeOff.fontStyle = FontStyle.Bold;
        _stBadgeOff.alignment = TextAnchor.MiddleCenter;
        _stBadgeOff.normal.textColor = new Color32(140, 148, 160, 255);
        _stBadgeOff.normal.background = MakeTexture(45, 50, 60, 255);
        _stBadgeOff.border = Ro(4, 4, 4, 4);

        _stNavItemActive = new GUIStyle();
        _stNavItemActive.font = lf;
        _stNavItemActive.fontSize = 12;
        _stNavItemActive.alignment = TextAnchor.MiddleLeft;
        _stNavItemActive.padding = Ro(16, 8, 0, 0);
        _stNavItemActive.normal.textColor = Color.white;

        _stNavHint = new GUIStyle();
        _stNavHint.font = lf;
        _stNavHint.fontSize = 10;
        _stNavHint.alignment = TextAnchor.UpperLeft;
        _stNavHint.wordWrap = true;
        _stNavHint.padding = Ro(14, 10, 8, 4);
        _stNavHint.normal.textColor = new Color32(148, 163, 184, 255);

        _stBreadcrumb = new GUIStyle();
        _stBreadcrumb.font = lf;
        _stBreadcrumb.fontSize = 11;
        _stBreadcrumb.alignment = TextAnchor.MiddleLeft;
        _stBreadcrumb.normal.textColor = new Color32(140, 152, 168, 255);

        _stSectionTitle = new GUIStyle();
        _stSectionTitle.font = lf;
        _stSectionTitle.fontSize = 12;
        _stSectionTitle.fontStyle = FontStyle.Bold;
        _stSectionTitle.alignment = TextAnchor.MiddleLeft;
        _stSectionTitle.normal.textColor = new Color32(226, 232, 240, 255);

        _stTableHeaderText = new GUIStyle();
        _stTableHeaderText.font = lf;
        _stTableHeaderText.fontSize = 10;
        _stTableHeaderText.fontStyle = FontStyle.Bold;
        _stTableHeaderText.alignment = TextAnchor.MiddleLeft;
        _stTableHeaderText.padding = Ro(12, 8, 0, 0);
        _stTableHeaderText.normal.textColor = new Color32(176, 186, 200, 255);

        _stHeaderSortBtn = new GUIStyle();
        _stHeaderSortBtn.font = lf;
        _stHeaderSortBtn.fontSize = 10;
        _stHeaderSortBtn.fontStyle = FontStyle.Bold;
        _stHeaderSortBtn.alignment = TextAnchor.MiddleLeft;
        _stHeaderSortBtn.padding = Ro(10, 6, 0, 0);
        _stHeaderSortBtn.normal.textColor = new Color32(176, 186, 200, 255);
        _stHeaderSortBtn.hover.textColor = new Color32(220, 228, 240, 255);
        _stHeaderSortBtn.active.textColor = Color.white;
        _stHeaderSortBtn.hover.background = MakeTexture(52, 60, 74, 220);
        _stHeaderSortBtn.border = Ro(2, 2, 2, 2);

        _stTableCell = new GUIStyle();
        _stTableCell.font = lf;
        _stTableCell.fontSize = 12;
        _stTableCell.alignment = TextAnchor.MiddleLeft;
        _stTableCell.padding = Ro(12, 8, 0, 0);
        _stTableCell.clipping = TextClipping.Clip;
        _stTableCell.normal.textColor = new Color32(220, 226, 235, 255);

        _stNavBtn = new GUIStyle();
        _stNavBtn.font = lf;
        _stNavBtn.fontSize = 12;
        _stNavBtn.alignment = TextAnchor.MiddleLeft;
        _stNavBtn.padding = Ro(16, 8, 0, 0);
        _stNavBtn.normal.background = _texSidebar;
        _stNavBtn.hover.background = _texNavBtnHover;
        _stNavBtn.active.background = _texNavBtnHover;
        _stNavBtn.normal.textColor = new Color32(203, 213, 225, 255);
        _stNavBtn.hover.textColor = new Color32(240, 244, 250, 255);
        _stNavBtn.active.textColor = Color.white;
        _stNavBtn.border = Ro(0, 0, 0, 0);

        _stMuted = new GUIStyle();
        _stMuted.font = lf;
        _stMuted.fontSize = 11;
        _stMuted.alignment = TextAnchor.MiddleLeft;
        _stMuted.normal.textColor = new Color32(154, 164, 178, 255);

        _stHint = new GUIStyle();
        _stHint.font = lf;
        _stHint.fontSize = 10;
        _stHint.alignment = TextAnchor.UpperLeft;
        _stHint.wordWrap = true;
        _stHint.normal.textColor = new Color32(130, 170, 255, 255);

        _stError = new GUIStyle();
        _stError.font = lf;
        _stError.fontSize = 10;
        _stError.alignment = TextAnchor.UpperLeft;
        _stError.wordWrap = true;
        _stError.normal.textColor = new Color32(255, 130, 120, 255);

        _stFormLabel = new GUIStyle();
        _stFormLabel.font = lf;
        _stFormLabel.fontSize = 11;
        _stFormLabel.fontStyle = FontStyle.Bold;
        _stFormLabel.alignment = TextAnchor.MiddleLeft;
        _stFormLabel.normal.textColor = new Color32(200, 208, 218, 255);

        _stOctetVal = new GUIStyle();
        _stOctetVal.font = lf;
        _stOctetVal.fontSize = 12;
        _stOctetVal.fontStyle = FontStyle.Bold;
        _stOctetVal.alignment = TextAnchor.MiddleCenter;
        _stOctetVal.normal.textColor = new Color32(240, 242, 248, 255);

        _stPrimaryBtn = new GUIStyle();
        _stPrimaryBtn.font = bf;
        _stPrimaryBtn.fontSize = 11;
        _stPrimaryBtn.fontStyle = FontStyle.Bold;
        _stPrimaryBtn.alignment = TextAnchor.MiddleCenter;
        _stPrimaryBtn.padding = Ro(12, 12, 6, 6);
        _stPrimaryBtn.normal.background = _texPrimaryBtn;
        _stPrimaryBtn.hover.background = _texPrimaryBtnHover;
        _stPrimaryBtn.active.background = MakeTexture(0, 104, 94, 255);
        _stPrimaryBtn.normal.textColor = Color.white;
        _stPrimaryBtn.hover.textColor = Color.white;
        _stPrimaryBtn.active.textColor = Color.white;
        _stPrimaryBtn.border = Ro(3, 3, 3, 3);

        _stMutedBtn = new GUIStyle();
        _stMutedBtn.font = bf;
        _stMutedBtn.fontSize = 11;
        _stMutedBtn.alignment = TextAnchor.MiddleCenter;
        _stMutedBtn.padding = Ro(10, 10, 5, 5);
        _stMutedBtn.normal.background = _texMutedBtn;
        _stMutedBtn.hover.background = _texMutedBtnHover;
        _stMutedBtn.active.background = _texMutedBtnHover;
        _stMutedBtn.normal.textColor = new Color32(230, 234, 240, 255);
        _stMutedBtn.hover.textColor = Color.white;
        _stMutedBtn.active.textColor = Color.white;
        _stMutedBtn.border = Ro(3, 3, 3, 3);

        _stIopsResult = new GUIStyle();
        _stIopsResult.font = lf;
        _stIopsResult.fontSize = 12;
        _stIopsResult.fontStyle = FontStyle.Normal;
        _stIopsResult.alignment = TextAnchor.UpperLeft;
        _stIopsResult.wordWrap = true;
        _stIopsResult.padding = Ro(0, 0, 0, 0);
        _stIopsResult.clipping = TextClipping.Clip;
        _stIopsResult.normal.textColor = new Color32(220, 226, 235, 255);

        _stIopsResultPlaceholder = new GUIStyle();
        _stIopsResultPlaceholder.font = lf;
        _stIopsResultPlaceholder.fontSize = 11;
        _stIopsResultPlaceholder.fontStyle = FontStyle.Normal;
        _stIopsResultPlaceholder.alignment = TextAnchor.UpperLeft;
        _stIopsResultPlaceholder.wordWrap = true;
        _stIopsResultPlaceholder.padding = Ro(0, 0, 0, 0);
        _stIopsResultPlaceholder.clipping = TextClipping.Clip;
        _stIopsResultPlaceholder.normal.textColor = new Color32(154, 164, 178, 255);

        _stylesReady = true;
    }

    private static Texture2D MakeTexture(byte r, byte g, byte b, byte a)
    {
        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Point,
        };
        tex.SetPixel(0, 0, new Color32(r, g, b, a));
        tex.Apply();
        return tex;
    }

    private static void DrawWindow(int id)
    {
        var w = _windowRect.width;
        var h = _windowRect.height;
        var dhcpUnlocked = LicenseManager.IsDHCPUnlocked;
        var ipamUnlocked = LicenseManager.IsIPAMUnlocked;

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
                    "Toggle DHCP (bulk assign, per-server DHCP, fill-empty). Ctrl+D toggles DHCP+IPAM together."),
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
        const float pauseW = 152f;
        const float autoW = 168f;
        const float fitColsW = 96f;
        var tx = w - tr;
        tx -= g + fitColsW;
        if (ImguiButtonOnce(new Rect(tx, btnRowY + ty, fitColsW, 26), "Fit columns", 16, _stMutedBtn))
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

        tx -= g + autoW;
        if (ImguiButtonOnce(new Rect(tx, btnRowY + ty, autoW, 26), "Auto-DHCP (all servers)", 10, _stPrimaryBtn) && dhcpUnlocked)
        {
            DHCPManager.AssignAllServers();
        }

        const float fillW = 118f;
        tx -= g + fillW;
        var fillOn = DHCPManager.EmptyIpAutoFillEnabled;
        var fillLabel = fillOn ? "Fill empty: ON" : "Fill empty: OFF";
        if (ImguiButtonOnce(new Rect(tx, btnRowY + ty, fillW, 26), fillLabel, 12, _stMutedBtn))
        {
            DHCPManager.EmptyIpAutoFillEnabled = !DHCPManager.EmptyIpAutoFillEnabled;
        }

        const float l3W = 100f;
        tx -= g + l3W;
        var l3On = ReachabilityService.EnforcementEnabled;
        if (ImguiButtonOnce(new Rect(tx, btnRowY + ty, l3W, 26), l3On ? "L3: ON" : "L3: OFF", 14, _stMutedBtn))
        {
            ReachabilityService.EnforcementEnabled = !ReachabilityService.EnforcementEnabled;
            ModDebugLog.Bootstrap();
            ModDebugLog.WriteLine(
                ReachabilityService.EnforcementEnabled
                    ? "IPAM: L3 enforcement ON — AddAppPerformance will be checked against routers/DHCP (see IOPS BLOCKED/ALLOW lines when flow is running)."
                    : "IPAM: L3 enforcement OFF — reachability gate skipped for AddAppPerformance (IOPS ALLOW still logs occasionally when flow runs).");
        }

        const float techW = 132f;
        const float clrAlarmW = 118f;
        tx -= g + pauseW;
        if (ImguiButtonOnce(new Rect(tx, btnRowY + ty, pauseW, 26), DHCPManager.IsFlowPaused ? "Resume flow" : "Pause flow", 11, _stMutedBtn))
        {
            DHCPManager.ToggleFlow();
        }

        tx -= g + clrAlarmW;
        if (ImguiButtonOnce(new Rect(tx, btnRowY + ty, clrAlarmW, 26), "Clear alarms", 13, _stMutedBtn))
        {
            RunClearAlarmsOnSelection();
        }

        tx -= g + techW;
        if (ImguiButtonOnce(new Rect(tx, btnRowY + ty, techW, 26), "Send technician", 12, _stMutedBtn))
        {
            RunSendTechnicianOnSelection();
        }

        const float iopsCalcW = 108f;
        tx -= g + iopsCalcW;
        if (ipamUnlocked)
        {
            var iopsLocal = new Rect(tx, btnRowY + ty, iopsCalcW, 26);
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

        var bodyTop = toolbarY + ToolbarH;
        var dph = GetDetailPanelHeight();
        var bodyH = h - bodyTop - dph;
        GUI.DrawTexture(new Rect(0, bodyTop, w, bodyH), _texPageBg);

        // Sidebar
        GUI.DrawTexture(new Rect(0, bodyTop, SidebarW, bodyH), _texSidebar);
        GUI.Label(new Rect(12, bodyTop + 10, SidebarW - 16, 16), "NAVIGATION", _stNavHint);
        DrawNavEntry(new Rect(8, bodyTop + 30, SidebarW - 8, 32), NavSection.Dashboard, "Dashboard");
        DrawNavEntry(new Rect(8, bodyTop + 64, SidebarW - 8, 32), NavSection.Devices, "Devices");
        DrawNavEntry(new Rect(8, bodyTop + 98, SidebarW - 8, 32), NavSection.IpAddresses, "IP addresses");
        DrawNavEntry(new Rect(8, bodyTop + 132, SidebarW - 8, 32), NavSection.Prefixes, "Prefixes");
        GUI.Label(
            new Rect(8, bodyTop + bodyH - 88, SidebarW - 12, 80),
            "Tip: plain click selects one; Ctrl toggles; Shift+click range. Drag vertical bars in table headers to resize columns; Fit columns sizes to content. Customer dropdown = active contracts only.",
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
            default:
                DrawPrefixesPlaceholder(innerW);
                break;
        }

        GUI.EndScrollView();

        var panelTop = h - dph;
        GUI.DrawTexture(new Rect(0, panelTop, w, dph), _texPageBg);
        if (_selectedNetworkSwitch != null)
        {
            DrawSwitchDetail();
        }
        else if (_selectedServerInstanceIds.Count > 0)
        {
            DrawServerDetail();
        }
        else
        {
            DrawDetailEmptyHint(panelTop, dph, w);
        }

        // BeginScrollView/EndScrollView can leave GUI.enabled false on Unity's internal stack.
        GUI.enabled = true;

        if (!_iopsCalculatorOpen)
        {
            DrawWindowResizeHandle(w, h);
        }

        GUI.DragWindow(new Rect(0, 0, w, TitleBarH + ToolbarH));
    }

    private static int IopsCalcKeyDigest(Event e)
    {
        unchecked
        {
            if (e.keyCode == KeyCode.Escape)
            {
                return (int)0x1A000001;
            }

            if (e.keyCode == KeyCode.Backspace)
            {
                return (int)0x1A000002;
            }

            if (e.keyCode >= KeyCode.Alpha0 && e.keyCode <= KeyCode.Alpha9)
            {
                return (int)0x1A001000 ^ (int)(e.keyCode - KeyCode.Alpha0);
            }

            if (e.keyCode >= KeyCode.Keypad0 && e.keyCode <= KeyCode.Keypad9)
            {
                return (int)0x1A002000 ^ (int)(e.keyCode - KeyCode.Keypad0);
            }

            if (e.keyCode == KeyCode.None && e.character >= '0' && e.character <= '9')
            {
                return (int)0x1A003000 ^ e.character;
            }

            return (int)0x1A004000 ^ (int)e.keyCode ^ (e.character * 397);
        }
    }

    /// <summary>
    /// Same physical key often produces two KeyDown events (e.g. Alpha1 vs character '1'). Register alternates so the second is ignored.
    /// </summary>
    private static void RegisterIopsDuplicateKeyDigests(Event e)
    {
        unchecked
        {
            if (e.keyCode >= KeyCode.Alpha0 && e.keyCode <= KeyCode.Alpha9)
            {
                var d = (int)(e.keyCode - KeyCode.Alpha0);
                _iopsCalcKeyDigests.Add((int)0x1A003000 ^ ('0' + d));
            }
            else if (e.keyCode >= KeyCode.Keypad0 && e.keyCode <= KeyCode.Keypad9)
            {
                var d = (int)(e.keyCode - KeyCode.Keypad0);
                _iopsCalcKeyDigests.Add((int)0x1A003000 ^ ('0' + d));
            }
            else if (e.keyCode == KeyCode.None && e.character >= '0' && e.character <= '9')
            {
                var d = (int)(e.character - '0');
                _iopsCalcKeyDigests.Add((int)0x1A001000 ^ d);
                _iopsCalcKeyDigests.Add((int)0x1A002000 ^ d);
            }
        }
    }

    private static void PumpIopsCalculatorKeyboard()
    {
        // When the Input System keyboard exists, <see cref="TickIopsCalculatorInputSystem"/> owns digits/Esc/Backspace.
        // IMGUI KeyDown for the same physical key would append twice (e.g. "00" for one press of 0).
        if (Keyboard.current != null)
        {
            return;
        }

        var e = Event.current;
        if (e.type != EventType.KeyDown)
        {
            return;
        }

        var f = Time.frameCount;
        if (f != _iopsCalcKeyDedupeFrame)
        {
            _iopsCalcKeyDedupeFrame = f;
            _iopsCalcKeyDigests.Clear();
        }

        var digest = IopsCalcKeyDigest(e);
        if (_iopsCalcKeyDigests.Contains(digest))
        {
            e.Use();
            return;
        }

        _iopsCalcKeyDigests.Add(digest);
        RegisterIopsDuplicateKeyDigests(e);

        if (e.keyCode == KeyCode.Escape)
        {
            CloseIopsCalculatorModal("Escape");
            e.Use();
            return;
        }

        if (e.keyCode == KeyCode.Backspace)
        {
            if (_iopsCalculatorDigits.Length > 0)
            {
                _iopsCalculatorDigits = _iopsCalculatorDigits.Substring(0, _iopsCalculatorDigits.Length - 1);
            }

            e.Use();
            return;
        }

        char? digit = null;
        if (e.keyCode >= KeyCode.Alpha0 && e.keyCode <= KeyCode.Alpha9)
        {
            digit = (char)('0' + (e.keyCode - KeyCode.Alpha0));
        }
        else if (e.keyCode >= KeyCode.Keypad0 && e.keyCode <= KeyCode.Keypad9)
        {
            digit = (char)('0' + (e.keyCode - KeyCode.Keypad0));
        }
        else if (e.keyCode == KeyCode.None && e.character >= '0' && e.character <= '9')
        {
            digit = e.character;
        }

        if (digit != null && _iopsCalculatorDigits.Length < 14)
        {
            if (_iopsCalculatorDigits.Length == 0 && digit.Value == '0')
            {
                e.Use();
                return;
            }

            _iopsCalculatorDigits += digit.Value;
            e.Use();
        }
    }

    private static void CloseIopsCalculatorModal(string reason = null)
    {
        var wasOpen = _iopsCalculatorOpen;
        _iopsCalculatorOpen = false;
        if (wasOpen)
        {
            // Resync list + EOL on next tick (avoids stale table/EOL until some unrelated event invalidated cache).
            _nextListRefreshTime = 0f;
            _nextEolSnapshotRefreshTime = 0f;
            if (ModDebugLog.IsIpamFileLogEnabled && !string.IsNullOrEmpty(reason))
            {
                IpamDebugLog.IopsModalClosed(reason);
            }
        }
    }

    private static void OpenIopsCalculator()
    {
        _iopsCalculatorOpen = true;
        const float ww = 460f;
        const float wh = 280f;
        _iopsStandaloneWindowRect = new Rect(
            Mathf.Max(8f, (Screen.width - ww) * 0.5f),
            Mathf.Max(8f, (Screen.height - wh) * 0.5f),
            ww,
            wh);
    }

    /// <summary>One MouseDown line per frame when IPAM debug file is enabled (compare with [IOPS probe] from Update).</summary>
    private static void PumpIpamDebugOnGuiMouse()
    {
        if (!ModDebugLog.IsIpamFileLogEnabled)
        {
            return;
        }

        var e = Event.current;
        if (e == null || e.type != EventType.MouseDown || e.button != 0)
        {
            return;
        }

        if (_ipamDebugLastMouseDownFrame == Time.frameCount)
        {
            return;
        }

        _ipamDebugLastMouseDownFrame = Time.frameCount;
        IpamDebugLog.OnGuiMouseDown(_windowRect, e.mousePosition);
    }

    /// <summary>Top-level <see cref="GUI.Window"/> — mod-side math only (constants IopsPer2UServer / IopsPer4UServer).</summary>
    private static void DrawIopsStandaloneWindow(int id)
    {
        GUI.DragWindow(new Rect(0f, 0f, 10000f, 24f));

        // Solid shell (same pixels as IPAM) — avoids washed-out / tinted chrome from GUI.color or 9-slice gaps.
        if (Event.current.type == EventType.Repaint)
        {
            var fillW = _iopsStandaloneWindowRect.width;
            var oldGc = GUI.color;
            GUI.color = Color.white;
            var fillH = Mathf.Min(2000f, _iopsStandaloneWindowRect.height + 48f);
            GUI.DrawTexture(new Rect(0f, 0f, fillW, fillH), _texBackdrop, ScaleMode.StretchToFill, false, 0f, Color.white, 0f, 0f);
            GUI.color = oldGc;
        }

        const float pad = 12f;
        const float resultPad = 10f;
        var w = _iopsStandaloneWindowRect.width;
        var innerW = w - pad * 2f;
        var x = pad;
        var y = 6f;

        GUI.Label(
            new Rect(x, y, innerW, 38f),
            "Simple sizing: servers needed = required IOPS ÷ per-server IOPS (rounded up). Mod constants only — not read from the game.",
            _stMuted);
        y += 40f;

        GUI.Label(new Rect(x, y, 160f, 22f), "Required IOPS", _stFormLabel);
        y += 24f;
        var fieldRect = new Rect(x, y, innerW, 28f);
        GUI.Box(fieldRect, GUIContent.none, _stMutedBtn);
        var disp = string.IsNullOrEmpty(_iopsCalculatorDigits) ? "(type digits — keyboard)" : _iopsCalculatorDigits + "_";
        GUI.Label(new Rect(fieldRect.x + 10f, fieldRect.y + 5f, fieldRect.width - 16f, 20f), disp, _stTableCell);
        y += 34f;

        GUI.Label(new Rect(x, y, innerW, 18f), "Server type (IOPS per server)", _stFormLabel);
        y += 22f;
        var half = (innerW - 8f) * 0.5f;
        var twoU = _iopsCalculatorServerKind == 0;
        var fourU = _iopsCalculatorServerKind == 1;
        if (GUI.Button(new Rect(x, y, half, 30f), $"2U  ({IopsPer2UServer:N0} IOPS)", twoU ? _stPrimaryBtn : _stMutedBtn))
        {
            _iopsCalculatorServerKind = 0;
        }

        if (GUI.Button(new Rect(x + half + 8f, y, half, 30f), $"4U  ({IopsPer4UServer:N0} IOPS)", fourU ? _stPrimaryBtn : _stMutedBtn))
        {
            _iopsCalculatorServerKind = 1;
        }

        y += 38f;

        var perServerKind = _iopsCalculatorServerKind == 0 ? IopsPer2UServer : IopsPer4UServer;
        string resultLine1;
        GUIStyle resultStyle1;
        if (string.IsNullOrEmpty(_iopsCalculatorDigits)
            || !ulong.TryParse(_iopsCalculatorDigits, out var reqIops)
            || reqIops == 0)
        {
            resultLine1 = "Enter a positive IOPS requirement to see server count.";
            resultStyle1 = _stIopsResultPlaceholder;
        }
        else
        {
            var need = (long)((reqIops + (ulong)perServerKind - 1UL) / (ulong)perServerKind);
            resultLine1 = $"Servers needed: {need}";
            resultStyle1 = _stIopsResult;
        }

        var textW = innerW - resultPad * 2f;
        var rh1 = resultStyle1.CalcHeight(new GUIContent(resultLine1), textW);

        var cardH = resultPad * 2f + rh1;
        var cardRect = new Rect(x, y, innerW, cardH);
        if (Event.current.type == EventType.Repaint)
        {
            GUI.DrawTexture(cardRect, _texCard, ScaleMode.StretchToFill, false, 0f, Color.white, 0f, 0f);
        }

        GUI.Label(new Rect(x + resultPad, y + resultPad, textW, rh1), resultLine1, resultStyle1);

        y += cardH;

        y += 10f;
        const float footerRowH = 28f;
        const float bottomPad = 8f;
        if (GUI.Button(new Rect(x + innerW - 100f, y, 100f, footerRowH), "Close", _stMutedBtn))
        {
            CloseIopsCalculatorModal("Close button");
        }

        GUI.Label(new Rect(x, y + 2f, innerW - 108f, footerRowH), "Esc closes · Backspace erases digits", _stMuted);

        // Fit window to content (fixed 400px left a large empty band under the footer).
        var clientBottom = y + footerRowH + bottomPad;
        // Total window height = client area (this callback) + title bar; border.top alone is often 9-slice, not full chrome.
        const float titleBarApprox = 24f;
        var desiredTotalH = clientBottom + titleBarApprox;
        if (Mathf.Abs(_iopsStandaloneWindowRect.height - desiredTotalH) > 0.5f)
        {
            _iopsStandaloneWindowRect.height = desiredTotalH;
        }
    }

    private static bool IsServerRowSelected(Server server)
    {
        return server != null && _selectedServerInstanceIds.Contains(server.GetInstanceID());
    }

    /// <param name="ctrlHeld">Windows Explorer style: Ctrl toggles membership without clearing the rest.</param>
    private static void ActivateServerRow(Server server, bool ctrlHeld)
    {
        if (server == null)
        {
            return;
        }

        _selectedNetworkSwitch = null;
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

    private static int FindSortedServerIndex(int instanceId)
    {
        EnsureSortedServers();
        for (var i = 0; i < SortedServersBuffer.Count; i++)
        {
            var s = SortedServersBuffer[i];
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

    private static void HandleServerRowClick(Server server, int sortedIndex, string ip)
    {
        if (server == null)
        {
            return;
        }

        // IMGUI: use Event modifiers — Unity Input System keyboard state is unreliable during OnGUI.
        var e = Event.current;
        var ctrl = e.control || e.command;
        var shift = e.shift;
        _selectedNetworkSwitch = null;
        _customerDropdownOpen = false;

        if (shift && !ctrl && _serverRangeAnchorInstanceId >= 0)
        {
            var anchorIdx = FindSortedServerIndex(_serverRangeAnchorInstanceId);
            if (anchorIdx < 0)
            {
                anchorIdx = sortedIndex;
            }

            var lo = Mathf.Min(anchorIdx, sortedIndex);
            var hi = Mathf.Max(anchorIdx, sortedIndex);
            _selectedServerInstanceIds.Clear();
            for (var i = lo; i <= hi; i++)
            {
                var s = SortedServersBuffer[i];
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

    private static bool SelectionHasEolTarget()
    {
        foreach (var sid in _selectedServerInstanceIds)
        {
            var s = FindServerByInstanceId(sid);
            if (s != null && DeviceInventoryReflection.AppearsPastOrCriticalEol(s))
            {
                return true;
            }
        }

        if (_selectedNetworkSwitch != null && DeviceInventoryReflection.AppearsPastOrCriticalEol(_selectedNetworkSwitch))
        {
            return true;
        }

        return false;
    }

    private static void RunSendTechnicianOnSelection()
    {
        if (!SelectionHasEolTarget())
        {
            ModLogging.Warning("Send technician: select device(s) at or past EOL (EOL column).");
            return;
        }

        var ok = 0;
        foreach (var sid in _selectedServerInstanceIds)
        {
            var s = FindServerByInstanceId(sid);
            if (s != null
                && DeviceInventoryReflection.AppearsPastOrCriticalEol(s)
                && DeviceInventoryReflection.TrySendTechnician(s))
            {
                ok++;
            }
        }

        if (_selectedNetworkSwitch != null
            && DeviceInventoryReflection.AppearsPastOrCriticalEol(_selectedNetworkSwitch)
            && DeviceInventoryReflection.TrySendTechnician(_selectedNetworkSwitch))
        {
            ok++;
        }

        if (ok > 0)
        {
            ModLogging.Msg($"Send technician: game API invoked on {ok} device(s).");
        }
        else
        {
            ModLogging.Warning("Send technician: no matching method on selected device(s) (check game version / ILSpy).");
        }
    }

    private static void RunClearAlarmsOnSelection()
    {
        if (!SelectionHasEolTarget())
        {
            ModLogging.Warning("Clear alarms: select device(s) at or past EOL.");
            return;
        }

        var ok = 0;
        foreach (var sid in _selectedServerInstanceIds)
        {
            var s = FindServerByInstanceId(sid);
            if (s != null
                && DeviceInventoryReflection.AppearsPastOrCriticalEol(s)
                && DeviceInventoryReflection.TryClearAlarms(s))
            {
                ok++;
            }
        }

        if (_selectedNetworkSwitch != null
            && DeviceInventoryReflection.AppearsPastOrCriticalEol(_selectedNetworkSwitch)
            && DeviceInventoryReflection.TryClearAlarms(_selectedNetworkSwitch))
        {
            ok++;
        }

        if (ok > 0)
        {
            ModLogging.Msg($"Clear alarms: game API invoked on {ok} device(s).");
        }
        else
        {
            ModLogging.Warning("Clear alarms: no matching method on selected device(s).");
        }
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
                    e.Use();
                }

                break;
            case EventType.Repaint:
                GUI.Box(r, "⋰", _stMutedBtn);
                break;
        }
    }

    private static void DrawDetailEmptyHint(float panelTop, float dph, float w)
    {
        GUI.Label(
            new Rect(16f, panelTop + 16f, w - 32f, dph - 24f),
            "Select a server (Ctrl+click toggles, Shift+click range from last plain click) or a switch. Drag the corner to resize; use Maximize for full screen.",
            _stMuted);
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
            RecomputeContentHeight();
        }
    }

    private static void RecomputeContentHeight()
    {
        switch (_navSection)
        {
            case NavSection.Dashboard:
                _cachedContentHeight = 260f;
                return;
            case NavSection.IpAddresses:
            {
                var sv = _cachedServers.Length;
                var y = CardPad + SectionTitleH + 2f + 7f + SectionTitleH + 4f + TableHeaderH + sv * TableRowH + CardPad;
                _cachedContentHeight = Mathf.Max(220f, y);
                return;
            }
            case NavSection.Prefixes:
                _cachedContentHeight = 240f;
                return;
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
            var roleRaw = sw != null
                ? (NetworkDeviceClassifier.GetKind(sw) == NetworkDeviceKind.Router ? "Router" : "L2 switch")
                : "—";
            var role = CellTextForCol(2, roleRaw, cardW);
            var eolCol = TableEolCellDisplay(sw, cardW);
            if (TableDataRowClick(
                    r,
                    StableRowHint(1, sw, i),
                    i % 2 == 1,
                    _selectedNetworkSwitch == sw,
                    name,
                    "—",
                    role,
                    "—",
                    eolCol,
                    CellTextForCol(5, "Active", cardW),
                    cardW))
            {
                _selectedServerInstanceIds.Clear();
                _selectedServer = null;
                _serverRangeAnchorInstanceId = -1;
                _selectedNetworkSwitch = sw;
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
            "Role",
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
                    "Server",
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
            var cust = CellTextForCol(1, GameSubnetHelper.GetCustomerDisplayName(server), cardW);
            var eolCol = TableEolCellDisplay(server, cardW);
            var dispRaw = DeviceInventoryReflection.GetDisplayName(server);
            var dispName = CellTextForCol(0, string.IsNullOrEmpty(dispRaw) ? "—" : dispRaw, cardW);
            if (TableDataRowClick(
                    r,
                    StableRowHint(2, server, i),
                    i % 2 == 1,
                    IsServerRowSelected(server),
                    dispName,
                    cust,
                    CellTextForCol(2, "Server", cardW),
                    ipCol,
                    eolCol,
                    status,
                    cardW))
            {
                HandleServerRowClick(server, i, ip);
            }

            y += TableRowH;
        }
    }

    private static void DrawDashboard(float innerW)
    {
        var x0 = CardPad;
        var y = CardPad;
        var w = innerW - CardPad * 2f;
        GUI.Label(new Rect(x0, y - 2, w, SectionTitleH), "Organization  /  Dashboard", _stBreadcrumb);
        y += SectionTitleH + 8f;
        GUI.Label(new Rect(x0, y, w, SectionTitleH), "Overview", _stSectionTitle);
        y += SectionTitleH + 6f;
        var sw = _cachedSwitches.Length;
        var sv = _cachedServers.Length;
        GUI.Label(new Rect(x0, y, w, 22), $"Network switches in scene:  {sw}", _stMuted);
        y += 24f;
        GUI.Label(new Rect(x0, y, w, 22), $"Servers in scene:  {sv}", _stMuted);
        y += 30f;
        GUI.Label(
            new Rect(x0, y, w, 72f),
            "Open Devices for full inventory tables. IP addresses shows a flat IPv4 list. Toolbar actions apply to all servers.",
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
            "Role",
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
            var cust = CellTextForCol(1, GameSubnetHelper.GetCustomerDisplayName(server), cardW);
            var eolCol = TableEolCellDisplay(server, cardW);
            var dispRaw = DeviceInventoryReflection.GetDisplayName(server);
            var dispName = CellTextForCol(0, string.IsNullOrEmpty(dispRaw) ? "—" : dispRaw, cardW);
            if (TableDataRowClick(
                    r,
                    StableRowHint(4, server, i),
                    i % 2 == 1,
                    IsServerRowSelected(server),
                    dispName,
                    cust,
                    CellTextForCol(2, "Server", cardW),
                    ipCol,
                    eolCol,
                    status,
                    cardW))
            {
                HandleServerRowClick(server, i, ip);
            }

            y += TableRowH;
        }
    }

    private static void DrawPrefixesPlaceholder(float innerW)
    {
        var x0 = CardPad;
        var y = CardPad;
        var w = innerW - CardPad * 2f;
        GUI.Label(new Rect(x0, y - 2, w, SectionTitleH), "Organization  /  Prefixes", _stBreadcrumb);
        y += SectionTitleH + 10f;
        GUI.Label(
            new Rect(x0, y, w, 100f),
            "Prefixes follow customer contracts in the base game. Per-VLAN / per-switch DHCP scopes are planned for a future mod release.",
            _stMuted);
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

        var d0 = GameSubnetHelper.GetCustomerDisplayName(SelectedServersScratch[0]);
        for (var i = 1; i < SelectedServersScratch.Count; i++)
        {
            if (GameSubnetHelper.GetCustomerDisplayName(SelectedServersScratch[i]) != d0)
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
        var anyOk = false;
        string lastErr = null;
        foreach (var srv in SelectedServersScratch)
        {
            if (ServerCustomerBinding.TryBindServerToCustomer(srv, cb, out var assignErr))
            {
                anyOk = true;
            }
            else
            {
                lastErr = assignErr;
            }
        }

        if (anyOk)
        {
            GameSubnetHelper.RefreshSceneCaches();
            InvalidateDeviceCache();
            UpdateAnchorServerForDetail();
            if (SelectedServersScratch.Count > 1
                && LicenseManager.IsDHCPUnlocked)
            {
                DHCPManager.AssignDhcpToServers(SelectedServersScratch);
                DHCPManager.ClearLastSetIpError();
                BeginImGuiInputRecoveryBurst();
            }
        }

        if (!anyOk && !string.IsNullOrEmpty(lastErr))
        {
            DHCPManager.SetLastIpamError(lastErr);
        }
    }

    private static void DrawCustomerDropdownAssign(float px, ref float py, float w)
    {
        GameSubnetHelper.FillActiveCustomersForPicker(CustomerPickBuffer);
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
            var modPriv = "—";
            if (CustomerPrivateSubnetRegistry.TryGetPrivateLanCidrForServer(s0, out var privCidr))
            {
                modPriv = privCidr;
            }

            GUI.Label(
                new Rect(px, py, w - 32, 18),
                $"Game customerID   {cidStr}    │    Mod private LAN   {modPriv}  (DHCP / reachability use this /24)",
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
        var py = panelY + 10f;
        var sw = _selectedNetworkSwitch;
        var kind = sw != null ? NetworkDeviceClassifier.GetKind(sw) : NetworkDeviceKind.Layer2Switch;
        var role = kind == NetworkDeviceKind.Router ? "Router (L3)" : "Layer 2 switch";
        var model = sw != null ? NetworkDeviceClassifier.GetModelDisplay(sw) : "";
        var modelLine = string.IsNullOrEmpty(model) ? "" : $"    │    Model   {Trunc(model, 40)}";

        GUI.Label(new Rect(px, py, w - 32, 20), "Edit object · Network device", _stSectionTitle);
        py += 22f;
        GUI.Label(
            new Rect(px, py, w - 32, 18),
            $"Name   {Trunc(sw != null ? DeviceInventoryReflection.GetDisplayName(sw) : "", 72)}    │    Role   {role}{modelLine}",
            _stMuted);
        py += 24f;

        var ox = px;
        if (ImguiButtonOnce(new Rect(ox, py, 120, 26), "Open CLI", 40, _stPrimaryBtn) && sw != null)
        {
            DeviceTerminalOverlay.OpenFor(sw);
        }

        ox += 128f;
        if (ImguiButtonOnce(new Rect(ox, py, 96, 26), "Deselect", 41, _stMutedBtn))
        {
            _selectedNetworkSwitch = null;
        }

        py += 34f;
        GUI.Label(
            new Rect(px, py, w - px - 24, 44),
            "CLI: type enable, then configure terminal. Routers: hostname, interface Gi0/n, ip address, ip route. Switches: vlan, interface Fa0/n, switchport mode access, switchport access vlan.",
            _stHint);
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
        GUI.Label(new Rect(x, y + 2, 36, 22), oct.ToString(), _stOctetVal);
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
}
